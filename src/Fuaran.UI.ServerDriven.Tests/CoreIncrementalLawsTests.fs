module Fuaran.UI.ServerDriven.Tests.CoreIncrementalLawsTests

// ============================================================================
//  Phase 1479 — footprint and delta laws over the live-transform seam.
//
//  Phase 1179 made this tier the estate's first consumer of Core's incremental
//  seam outside the substrate's own tests: `TransformSource.Live` refresh runs
//  through `Incremental.primeOn` / `refreshOn`, and the recompute corpus was
//  re-recorded from it. What `LiveTransformCorpusTests.fs` asserts is RESULT
//  EQUALITY over seven recorded vectors. This file adds the rest of what the
//  seam promises, over the tier's OWN generated tables and edit streams.
//
//  ── TWO KINDS OF EVIDENCE, AND THEY ARE NOT THE SAME KIND ─────────────────
//  The four kit families this phase enrols are SELF-CONTAINED — each takes a
//  `(seed, iterations)` pair and nothing else, drawing Core's own tables, its
//  own pipelines and its own edit streams. They cannot see this tier's code, so
//  running them proves that THE PINNED KIT's contract holds on this machine at
//  this pin. That is real evidence, and it is exactly what the census records;
//  it is not evidence about the live-transform seam.
//
//  So the first list below runs them honestly as what they are, and the second
//  states the same properties over the REAL live path — `LiveTransformStore`
//  driving `Incremental.primeOn` / `refreshOn` — where a defect in this tier's
//  wiring is what turns them red:
//
//    (a) DELTA EQUIVALENCE. For generated tables and edit streams, the refresh
//        answers what a from-scratch evaluation over the changed source answers
//        (D19). Adequacy is asserted rather than hoped for: the generator must
//        reach a RESTRICTED refresh, a DECLINED one, and a PARTITION-GLOBAL
//        window — the family Core admitted on 2026-09-03 — or the run is
//        vacuous and says so.
//    (b) ONE-SCALE FOOTPRINT (D21). `Incremental.rowsEvaluated` on the refresh
//        never exceeds what priming over the SAME (changed) source costs, and
//        where the refresh DECLINES the two counts are EQUAL. The equality is
//        the part that makes it a scale claim rather than an inequality that a
//        differently-counted decline could satisfy by accident.
//    (c) DIRTY CONE. Over `BindingWalk.collect`, a State edit dirties exactly
//        the readers whose bindings read the edited key — no more, no fewer —
//        across all three surfaces that reach the State channel: a plain
//        `Binding.State` read, a `Transform`'s live SOURCE slot, and a
//        `Transform` PARAM bound to state.
//
//  ── WHAT IS DELIBERATELY NOT HERE ─────────────────────────────────────────
//  `Conformance.footprintLaws` is NOT adopted by this phase. Its subject is
//  `Ops.footprint` over the skeleton TREE-op algebra, not the incremental
//  dataframe footprint that shares the word; fuaran#1476 builds the witness it
//  needs and carries the row.
//
//  The seven corpus vectors run here as fixed cases for (b) ONLY, over all
//  seven rather than the restricted subset — the sibling file already carries
//  their result equality and its own strictly-fewer-rows claim, and restating
//  either would be a second copy of an assertion rather than a second check.
// ============================================================================

open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.ServerDriven

module CoreConf = Fuaran.Core.Conformance
module CoreIncr = Fuaran.Core.IncrementalDelta

/// Shared with the sibling adoption files: a law family reports per-law verdicts, and a run that
/// looked at the list without failing on it would be a green test over a red family.
let private assertAllPassed (context: string) (results: Fuaran.Core.LawResult list) =
    let failures = results |> List.filter (fun r -> not r.Passed)

    if not (List.isEmpty failures) then
        failures
        |> List.map (fun r -> sprintf "  %s — %A" r.Law r.Counterexample)
        |> String.concat "\n"
        |> failtestf "%s failed:\n%s" context

// ---------------------------------------------------------------------------
//  the tier's own generator — tables, pipelines, edit streams
// ---------------------------------------------------------------------------

/// A seed-replayable xorshift. Deliberately local and deliberately tiny: the kit's own `ConfRng` is
/// internal to the conformance assembly, and a test whose sample set cannot be replayed from a
/// printed seed is a test whose counterexample nobody can reproduce.
type private Rng = { mutable State: uint64 }

let private rngOf (seed: int) : Rng =
    { State = uint64 (uint32 seed) + 0x9E3779B97F4A7C15UL }

let private nextBits (r: Rng) : uint64 =
    let mutable x = r.State
    x <- x ^^^ (x <<< 13)
    x <- x ^^^ (x >>> 7)
    x <- x ^^^ (x <<< 17)
    r.State <- x
    x

let private intBelow (bound: int) (r: Rng) : int = int (nextBits r % uint64 bound)

/// The tier's grid shape: a string identity column the live seam keys on, plus a wide-ish value
/// column and a narrow one that partitions and groups. Rows are drawn 1..12 — the width the
/// generated pipelines need for ties, group members and window frames to exist at all.
let private rowBound = 12

let private identityColumn = "id"

let private schema: Schema =
    [ identityColumn, StringType; "a", IntType; "b", IntType ]

let private tableOf (rows: (string * int * int) list) : Table =
    { Schema = schema
      Columns =
        [ Column.create identityColumn StringType (rows |> List.map (fun (k, _, _) -> Str k))
          Column.create "a" IntType (rows |> List.map (fun (_, a, _) -> Int a))
          Column.create "b" IntType (rows |> List.map (fun (_, _, b) -> Int b)) ] }

/// The pipeline classes, chosen so every branch the seam takes is reachable and each is reachable
/// for a STATED reason rather than by luck of the draw. The adequacy assertions below name the
/// three the phase requires; the rest are here because a generator that only ever drew the easy
/// classes would certify the easy classes.
let private pipelines: (string * Transform list) list =
    [ "filter (row-local)", [ Filter(Binary(Gt, Col "a", Lit(Int 2))) ]
      "derive (row-local)", [ Derive("a2", Binary(Mul, Col "a", Lit(Int 2))) ]
      "filter then groupBy (maintained groups)",
      [ Filter(Binary(Gt, Col "a", Lit(Int 0)))
        GroupBy([ "b" ], [ { Name = "n"; Fn = Count; Of = "a" } ]) ]
      "sort then filter (merged order)", [ Sort [ "a", Asc ]; Filter(Binary(Gt, Col "a", Lit(Int 1))) ]
      "partitioned window (bounded frame)",
      [ Window
            { PartitionBy = [ "b" ]
              OrderBy = [ "a", Asc ]
              Fn = Lag
              Of = "a"
              As = "prev" } ]
      // The family Core admitted on 2026-09-03: a window with NO partition key, so the frame is the
      // whole table. Named as its own class because the phase requires it reached, and because an
      // empty `PartitionBy` is the one window shape a partitioned generator never draws.
      "partition-global window (bounded frame)",
      [ Window
            { PartitionBy = []
              OrderBy = [ "a", Asc ]
              Fn = Lag
              Of = "a"
              As = "prev" } ]
      // Two declines with different reasons: a verb the seam does not classify as row-local, and a
      // window whose frame is unbounded. Both must fall back INSIDE the seam and still answer right.
      "limit (declines — verb is not row-local)", [ Limit(3, 0) ]
      "unbounded window (declines — frame is unbounded)",
      [ Window
            { PartitionBy = []
              OrderBy = [ "a", Asc ]
              Fn = CumulSum
              Of = "a"
              As = "run" } ] ]

/// One generated case: the source, the source after an edit stream, and the pipeline read over both.
type private Case =
    {
        Seed: int
        Iteration: int
        PipelineName: string
        Pipeline: Transform list
        Source: Table
        Changed: Table
        /// How many edit ops the stream carried, for the counterexample citation.
        Edits: int
    }

let private cite (c: Case) (what: string) =
    sprintf "seed=%d iter=%d pipeline=%s edits=%d: %s" c.Seed c.Iteration c.PipelineName c.Edits what

/// Apply an edit stream addressed by the identity column's value, exactly as a live grid's inbound
/// events would: a cell write, an appended row, a removed row. A removal never empties the table —
/// an empty source is a legitimate state but it collapses every class into the same trivial answer,
/// so it is drawn deliberately rather than stumbled into.
let private edit (r: Rng) (rows: (string * int * int) list) : (string * int * int) list * int =
    let n = 1 + intBelow 3 r
    let mutable current = rows
    let mutable applied = 0

    for _ in 1..n do
        match intBelow 3 r with
        | 0 ->
            let i = intBelow (List.length current) r
            let a = intBelow 12 r - 5

            current <-
                current
                |> List.mapi (fun j (k, oldA, b) -> if j = i then (k, a, b) else (k, oldA, b))

            applied <- applied + 1
        | 1 ->
            let a = intBelow 12 r - 5
            let b = intBelow 3 r
            current <- current @ [ "n" + string (List.length current), a, b ]
            applied <- applied + 1
        | _ ->
            if List.length current > 1 then
                let i = intBelow (List.length current) r

                current <-
                    current
                    |> List.mapi (fun j row -> j, row)
                    |> List.filter (fun (j, _) -> j <> i)
                    |> List.map snd

                applied <- applied + 1

    current, applied

let private cases (seed: int) (iterations: int) : Case list =
    let r = rngOf seed

    [ for i in 0 .. iterations - 1 do
          let nRows = 1 + intBelow rowBound r

          let rows =
              [ for j in 0 .. nRows - 1 do
                    let a = intBelow 12 r - 5
                    let b = intBelow 3 r
                    "r" + string j, a, b ]

          let pk = intBelow (List.length pipelines) r
          let name, pipeline = List.item pk pipelines
          let changed, applied = edit r rows

          { Seed = seed
            Iteration = i
            PipelineName = name
            Pipeline = pipeline
            Source = tableOf rows
            Changed = tableOf changed
            Edits = applied } ]

// ---------------------------------------------------------------------------
//  driving the real seam
// ---------------------------------------------------------------------------

let private site = "grid:laws"

let private ok (context: string) (r: Result<'a, string>) : 'a =
    match r with
    | Ok v -> v
    | Error e -> failtestf "%s: the seam refused — %s" context e

/// Prime over `source`, then refresh against `changed` — the render-then-edit sequence the tier is
/// built for, through the tier's own store rather than a direct `Incremental` call.
let private primeThenRefresh (c: Case) : LiveTransformEvaluation =
    let store = LiveTransformStore()

    store.Evaluate(site, identityColumn, c.Pipeline, c.Source)
    |> ok (cite c "priming")
    |> ignore

    store.Evaluate(site, identityColumn, c.Pipeline, c.Changed)
    |> ok (cite c "refresh")

/// The baseline the footprint is measured against: priming a FRESH store over the CHANGED source.
/// Measuring against the base would be wrong — an append makes the new source larger, and the
/// appended row legitimately passes through more steps than the rows it joined.
let private fullOver (c: Case) : LiveTransformEvaluation =
    LiveTransformStore().Evaluate(site, identityColumn, c.Pipeline, c.Changed)
    |> ok (cite c "full evaluation")

let private sameTable (context: string) (expected: Table) (actual: Table) =
    Expect.equal (List.map fst actual.Schema) (List.map fst expected.Schema) (context + ": column names")
    Expect.equal actual.Schema expected.Schema (context + ": schema")
    Expect.equal (actual.Columns |> List.map _.Cells) (expected.Columns |> List.map _.Cells) (context + ": cells")

let private isRestricted (f: RecomputeFootprint) =
    match f.Recompute with
    | RowsRecomputed _
    | GroupsRecomputed _ -> true
    | _ -> false

let private isDeclined (f: RecomputeFootprint) =
    match f.Recompute with
    | FullRecompute _ -> true
    | _ -> false

// ---------------------------------------------------------------------------
//  (c) the dirty cone over the binding walk
// ---------------------------------------------------------------------------

/// The key a cone case's readers read. Non-readers read `otherKey` or nothing at all, so "no more"
/// is a claim with something to be wrong about.
let private editedKey = "orders"

let private otherKey = "filters"

let private countPipeline: Transform list =
    [ GroupBy([ "b" ], [ { Name = "n"; Fn = Count; Of = "a" } ]) ]

let private paramPipeline: Transform list =
    [ Filter(Binary(Gt, Col "a", Param "threshold")) ]

/// A badge whose label is a plain `Binding.State` read.
let private stateReader (id: string) (key: string) : Node<obj> =
    Fuaran.badge
        id
        { Defaults.badge with
            Label = TextSource.Bound(Binding.State(key, None)) }

/// A badge whose label is a `Transform` over a LIVE `Binding.State` source — the seam this phase is
/// about. The walk records it as `TransformStateSource`, which reaches the State channel since
/// Phase 1075; a cone that missed it would deny an edge the renderer has honoured since Phase 818.
let private transformSourceReader (id: string) (key: string) : Node<obj> =
    Fuaran.badge
        id
        { Defaults.badge with
            Label =
                TextSource.Bound(
                    Binding.Transform(
                        TransformSource.Live(Binding.State(key, None), HostPrelude.TransformLive.emptySource),
                        countPipeline,
                        None
                    )
                ) }

/// A badge whose `Transform` reads an EMBEDDED table but takes its filter threshold from state. The
/// source has not moved; the param has, and the pipeline's answer moves with it — so this reader is
/// dirty on an edit to `key` exactly as the two above are.
let private transformParamReader (id: string) (key: string) : Node<obj> =
    Fuaran.badge
        id
        { Defaults.badge with
            Label =
                TextSource.Bound(
                    Binding.Transform(
                        TransformSource.Data(Embedded { Schema = []; Columns = [] }),
                        paramPipeline,
                        Some
                            [ { Name = "threshold"
                                From = Binding.State(key, None) } ]
                    )
                ) }

let private inertReader (id: string) : Node<obj> =
    Fuaran.badge
        id
        { Defaults.badge with
            Label = TextSource.Literal "static" }

/// One generated binding set, with the generator's OWN record of which readers read `editedKey`.
/// That record is the oracle: it is not derived from the walk, so the walk cannot agree with it by
/// construction.
type private ConeCase =
    { Tree: Node<obj>
      Dirty: Set<string>
      Clean: Set<string> }

let private coneCase (seed: int) (iteration: int) : ConeCase =
    let r = rngOf (seed + iteration * 7919)
    let n = 3 + intBelow 6 r
    let mutable dirty = Set.empty
    let mutable clean = Set.empty

    let children =
        [ for i in 0 .. n - 1 do
              let id = sprintf "n%d" i

              match intBelow 6 r with
              | 0 ->
                  dirty <- Set.add id dirty
                  stateReader id editedKey
              | 1 ->
                  dirty <- Set.add id dirty
                  transformSourceReader id editedKey
              | 2 ->
                  dirty <- Set.add id dirty
                  transformParamReader id editedKey
              | 3 ->
                  clean <- Set.add id clean
                  stateReader id otherKey
              | 4 ->
                  clean <- Set.add id clean
                  transformSourceReader id otherKey
              | _ ->
                  clean <- Set.add id clean
                  inertReader id ]

    { Tree =
        Fuaran.dashboard
            "root"
            { Defaults.dashboard<obj> with
                Children = children }
      Dirty = dirty
      Clean = clean }

/// The tier's dirty cone for one state key: every reader whose bindings reach that key on the State
/// channel. Both cases are counted, and that is the whole content of the claim — a `State` read and
/// a `Transform`'s live source are two spellings of one subscription, and the renderer has treated
/// them as one since Phase 818.
let private dirtyCone (facts: BindingWalk.TreeBindingFacts) (key: string) : Set<string> =
    facts.Uses
    |> List.choose (fun u ->
        match u.Use with
        | BindingWalk.BindingUse.State k when k = key -> Some u.Reader
        | BindingWalk.BindingUse.TransformStateSource(k, _) when k = key -> Some u.Reader
        | _ -> None)
    |> Set.ofList

// ---------------------------------------------------------------------------
//  the tests
// ---------------------------------------------------------------------------

/// One seed for every family and every tier-shaped property, so a counterexample from any of them
/// replays the same sample set.
let private seed = 20260904

let private iterations = 100

/// The iteration count the two `IncrementalDelta` families are run at, and it is NOT 100 for a
/// measured reason rather than a hopeful one.
///
/// That family's sample-adequacy guard demands every refresh class it distinguishes be reached, and
/// its `window-restricted` class is MARGINAL at 100 iterations: swept over 60 independent seeds on
/// the pinned 0.18.0 kit, the guard fires on 2/60 at the kit's own shipped bound of 9 and 3/60 at
/// this tier's bound of 12. It is therefore a property of the family's generator, not of the bound
/// this file picks — which matters, because the obvious diagnosis (the wider bound broke it) is
/// wrong and would have sent the fix to the wrong place.
///
/// The guard's own remedy text forbids the two cheap escapes by name: raising the count until it
/// happens to pass at one seed, or hunting a seed, each leaves the law certified by one trial. So
/// the count was raised and then MEASURED across the same 60-seed sweep: at 200 the guard fires on
/// 0/60 at both bounds, and at 150 on 1/60 at bound 9. 200 is where the class stops being a coin
/// flip, not where this file's seed stopped failing.
let private deltaIterations = 200

[<Tests>]
let tests =
    testList
        "LiveTransform — footprint and delta laws (Core conformance)"
        [

          // ---- the pinned kit's own families, run as what they are ----

          testList
              "the pinned kit's incremental contract holds at this pin"
              [ testCase "IncrementalDelta.laws certifies the incremental seam at the kit's shipped row bound"
                <| fun _ ->
                    // Self-contained: it draws Core's own tables, pipelines and edit streams, so a
                    // green row here is evidence about THE PIN, never about this tier. The tier's
                    // own statement of the same property is the `over the tier's live path` list
                    // below, and the census row cites both.
                    CoreIncr.laws seed deltaIterations
                    |> assertAllPassed "IncrementalDelta.laws at the shipped bound"

                testCase "IncrementalDelta.lawsWith certifies at the tier's own live-grid row bound"
                <| fun _ ->
                    // The `With` form is adopted on its own terms rather than as a second spelling:
                    // its parameter is the table-WIDTH bound, and 12 is chosen to span this tier's
                    // shapes rather than the kit's. The tier's generated grids draw 1..12 rows
                    // (`rowBound` above) and the seven recorded corpus vectors hold 6 each, so a
                    // bound of 12 covers both — where the shipped bound of 9 covers neither's top
                    // end.
                    CoreIncr.lawsWith rowBound seed deltaIterations
                    |> assertAllPassed (sprintf "IncrementalDelta.lawsWith at rowBound=%d" rowBound)

                testCase "the row bound is a real lever — narrowing it turns the family red"
                <| fun _ ->
                    // The go-red proof for a SELF-CONTAINED family, in the suite rather than in a
                    // session's memory, and it is the proof the kit's own doc comment names: narrow
                    // the span and watch the adequacy guard fail. Without it, "the family passed"
                    // and "the family was capable of failing" are two sentences and only one of
                    // them was checked — a green row over a family whose parameter did nothing
                    // would read exactly the same.
                    //
                    // A bound of 1 makes every drawn table one row wide, so no tie between a named
                    // and an unnamed row can arise and the order-sensitive laws read nothing. It is
                    // deterministic rather than lucky: measured red on 30/30 independent seeds.
                    let narrowed = CoreIncr.lawsWith 1 seed deltaIterations

                    Expect.isNonEmpty
                        (narrowed |> List.filter (fun r -> not r.Passed))
                        "narrowing the row bound to 1 left the family green — its span parameter reaches nothing"

                testCase "the all-pass assertion is itself falsifiable"
                <| fun _ ->
                    // The other half of the same worry, one level down: every row above is only
                    // worth the assertion that reads it. A harness that logged the verdicts and
                    // returned would be green over a red family, so it is shown failing on a
                    // deliberately failed law once.
                    Expect.throws
                        (fun () ->
                            assertAllPassed
                                "go-red probe"
                                [ { Law = "a deliberately failed law"
                                    Passed = false
                                    Counterexample = Some "the probe's own counterexample" } ])
                        "assertAllPassed accepted a failed law"

                testCase "incrementalLaws certifies change-driven and op-driven equivalence at this pin"
                <| fun _ ->
                    // `DataFrame.evalFrom`'s equivalence — the claim UNDER the seam this tier
                    // consumes. Enrolled here rather than called unused because the tier's live
                    // path IS incremental dataframe evaluation; the census row records that
                    // reasoning where the next reader will look for it.
                    CoreConf.incrementalLaws seed iterations
                    |> assertAllPassed "Conformance.incrementalLaws"

                testCase "dirtyPropagationLaws certifies the propagation seam's cone at this pin"
                <| fun _ ->
                    // Self-contained over Core's `Propagation.dirtyFromChangedIds` and a toy pull
                    // evaluator. It cannot see `BindingWalk`, so the tier-shaped cone property is
                    // stated separately below and the census row cites the pair.
                    CoreConf.dirtyPropagationLaws seed iterations
                    |> assertAllPassed "Conformance.dirtyPropagationLaws" ]

          // ---- (a) + (b) over the tier's own live path ----

          testList
              "over the tier's live path"
              [ testCase "the refresh answers what a full evaluation over the changed source answers"
                <| fun _ ->
                    // D19, over the tier's own tables and edit streams rather than the corpus's.
                    // Stated with no allowance and over EVERY class, the declined ones included: a
                    // decline that answered differently would be a defect the footprint never
                    // reveals, because a decline is a measured outcome and not an error.
                    for c in cases seed iterations do
                        let refreshed = primeThenRefresh c

                        let reference =
                            LiveTransform.reference c.Pipeline c.Changed |> ok (cite c "reference")

                        sameTable (cite c "refresh vs a full evaluation") reference refreshed.Result

                testCase "the refresh evaluates no more rows than a full evaluation, on one scale"
                <| fun _ ->
                    // D21. The inequality alone would be satisfiable by an evaluator whose decline
                    // was counted in a smaller unit than the baseline it is measured against —
                    // which is exactly the defect fuaran-core#117 fixed — so the DECLINED class is
                    // additionally asserted EQUAL: a fall-back runs the same reference evaluation
                    // the baseline prime runs, so any difference is a difference of unit.
                    for c in cases seed iterations do
                        let refreshed = primeThenRefresh c
                        let full = fullOver c

                        let refreshRows = Incremental.rowsEvaluated refreshed.Footprint
                        let fullRows = Incremental.rowsEvaluated full.Footprint

                        Expect.isLessThanOrEqual
                            refreshRows
                            fullRows
                            (cite c "a refresh evaluated more rows than a full evaluation would")

                        if isDeclined refreshed.Footprint then
                            Expect.equal
                                refreshRows
                                fullRows
                                (cite
                                    c
                                    "a declined refresh and the full evaluation it falls back to are not on one scale")

                testCase "the generated sample reaches a restricted, a declined and a partition-global refresh"
                <| fun _ ->
                    // Adequacy, asserted rather than assumed. A law list is only worth its sample:
                    // the two clauses above would pass green over a sample that never restricted
                    // anything, and a run that never drew a partition-global window would certify
                    // the family Core admitted on 2026-09-03 by not exercising it.
                    let xs = cases seed iterations

                    let restricted =
                        xs |> List.filter (fun c -> isRestricted (primeThenRefresh c).Footprint)

                    let declined =
                        xs |> List.filter (fun c -> isDeclined (primeThenRefresh c).Footprint)

                    let partitionGlobal =
                        xs
                        |> List.filter (fun c ->
                            c.Pipeline
                            |> List.exists (function
                                | Window w -> List.isEmpty w.PartitionBy
                                | _ -> false))

                    Expect.isNonEmpty restricted "no generated sample reached a RESTRICTED refresh"
                    Expect.isNonEmpty declined "no generated sample reached a DECLINED refresh"

                    Expect.isNonEmpty
                        partitionGlobal
                        "no generated sample reached a PARTITION-GLOBAL window (the family Core admitted 2026-09-03)"

                    // The partition-global class must also actually REFRESH rather than decline for
                    // some sample, or "reached" would mean only that the pipeline was drawn.
                    Expect.isNonEmpty
                        (partitionGlobal
                         |> List.filter (fun c ->
                             let f = (primeThenRefresh c).Footprint
                             isRestricted f || f.Recompute = ReusedPrior))
                        "every partition-global window sample declined — the class was drawn but never exercised"

                testCase "the seven corpus vectors obey the one-scale bound, declined ones included"
                <| fun _ ->
                    // The recorded vectors as fixed cases for (b). Their RESULT equality and the
                    // strictly-fewer-rows claim over the restricted subset live in
                    // `LiveTransformCorpusTests.fs`; what is added here is the bound over ALL seven,
                    // which that file's restricted filter structurally excludes.
                    for name, pipeline, source, changed, key in LiveTransformCorpusTests.corpusCases () do
                        let store = LiveTransformStore()

                        store.Evaluate(site, key, pipeline, source) |> ok (name + ": priming") |> ignore

                        let refreshed =
                            store.Evaluate(site, key, pipeline, changed) |> ok (name + ": refresh")

                        let full =
                            LiveTransformStore().Evaluate(site, key, pipeline, changed)
                            |> ok (name + ": full evaluation")

                        Expect.isLessThanOrEqual
                            (Incremental.rowsEvaluated refreshed.Footprint)
                            (Incremental.rowsEvaluated full.Footprint)
                            (name + ": the refresh evaluated more rows than a full evaluation would") ]

          // ---- (c) the dirty cone over the binding walk ----

          testList
              "a State edit dirties exactly the bindings whose pipelines read it"
              [ testCase "the dirty cone over the binding walk is sound and minimal on generated binding sets"
                <| fun _ ->
                    // The oracle is the GENERATOR's own record of what it built, not a second walk
                    // over the tree — so agreement is a fact about `BindingWalk`, not a tautology.
                    // Three surfaces reach the State channel and all three are drawn: a plain
                    // `Binding.State` read, a `Transform`'s live SOURCE slot, and a `Transform`
                    // PARAM bound to state. Dropping any one of them under-reports the cone, and a
                    // reader left un-refreshed after an edit is the defect this asserts against.
                    for i in 0..49 do
                        let c = coneCase seed i
                        let facts = BindingWalk.collect c.Tree

                        Expect.isFalse
                            facts.StateKeys.OpaqueReader
                            (sprintf
                                "iter=%d: the generated tree holds an opaque reader, so 'no more' is not a claim this sample can make"
                                i)

                        Expect.equal
                            (dirtyCone facts editedKey)
                            c.Dirty
                            (sprintf "iter=%d: the cone for an edit to '%s' is not what the tree reads" i editedKey)

                        // The other half, and the one an over-reporting walk fails: a reader of a
                        // DIFFERENT key, and a reader of no key at all, are outside the cone.
                        Expect.isEmpty
                            (Set.intersect (dirtyCone facts editedKey) c.Clean |> Set.toList)
                            (sprintf "iter=%d: a reader that does not read '%s' is inside its cone" i editedKey)

                testCase "the generated binding sets reach both halves of the cone"
                <| fun _ ->
                    // Adequacy again: a sample in which every reader is dirty says nothing about
                    // minimality, and one in which none is says nothing about soundness.
                    let xs = [ for i in 0..49 -> coneCase seed i ]

                    Expect.isNonEmpty
                        (xs |> List.filter (fun c -> not (Set.isEmpty c.Dirty)))
                        "no generated binding set held a reader of the edited key"

                    Expect.isNonEmpty
                        (xs |> List.filter (fun c -> not (Set.isEmpty c.Clean)))
                        "no generated binding set held a reader outside the edited key's cone"

                testCase "each of the three State-channel surfaces is reached on its own"
                <| fun _ ->
                    // Named rather than inferred from the mix: a generator whose draw happened to
                    // omit the Transform PARAM surface would certify a cone that never had to see
                    // it, and that surface is the one an implementation forgets.
                    let coneOf (node: Node<obj>) =
                        dirtyCone
                            (BindingWalk.collect (
                                Fuaran.dashboard
                                    "root"
                                    { Defaults.dashboard<obj> with
                                        Children = [ node ] }
                            ))
                            editedKey

                    Expect.equal
                        (coneOf (stateReader "s" editedKey))
                        (Set.singleton "s")
                        "a plain Binding.State read is in the cone"

                    Expect.equal
                        (coneOf (transformSourceReader "t" editedKey))
                        (Set.singleton "t")
                        "a Transform's live State SOURCE slot is in the cone"

                    Expect.equal
                        (coneOf (transformParamReader "p" editedKey))
                        (Set.singleton "p")
                        "a Transform PARAM bound to state is in the cone"

                    Expect.isEmpty
                        (coneOf (inertReader "i") |> Set.toList)
                        "a reader that binds no state is outside the cone" ] ]
