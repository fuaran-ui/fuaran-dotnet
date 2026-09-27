module Fuaran.UI.ServerDriven.Tests.ParameterisedSiteTests

open System
open System.Diagnostics
open System.IO
open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.ServerDriven

// ============================================================================
//  Phase 1761 — state for PARAMETERISED Transform sites.
//
//  A Transform over static data whose pipeline reads filter parameters used to
//  be evaluated in full on every render, whichever chip had moved: the rows
//  never change, so the row-delta seam had nothing to restrict, and no store
//  keyed the site at all. The renderer now closes the effective pipeline over
//  its bound params and keys the session store by that closed pipeline, on the
//  `Data` arm as well as the `Live` one. These legs state what that buys and
//  what it must never cost.
//
//  ── THE LAW ──────────────────────────────────────────────────────────────
//  For generated (tree, chip-edit stream) pairs, rendered through ONE store
//  across the whole stream:
//
//    * every site's rows equal the rows the same render produces with no store
//      furnished — the from-scratch evaluation, no allowance; and
//    * after an edit, every site whose pipeline does NOT read the edited chip is
//      answered `ReusedPrior` — asserted on the store's own footprint, so "it
//      evaluated nothing" is a measured claim, not an inference from the rows.
//
//  The second clause goes red if the store is bypassed (no footprint is
//  recorded for the site), if the key moves with a chip the pipeline does not
//  read (a miss re-primes and reports `Primed`), or if an unchanged static
//  source is diffed rather than recognised (the default store declares no row
//  identity, so a diff cannot key it and reports `FullRecompute`).
//
//  ── THE COST READING ──────────────────────────────────────────────────────
//  On the corpus's `filterable-static-dashboard` fixture and on a generated
//  24-site tree: the stream rendered with no store against the same stream
//  through one store, on BOTH scales — Core's deterministic `rowsEvaluated`
//  (the full side measured as each site's prime, which Core defines as a full
//  evaluation), and median wall-clock. The rows are asserted equal before any
//  figure is printed. Only the deterministic scale is asserted; the clock is
//  reported, never gated on.
// ============================================================================

/// A store that records every evaluation it performed, in call order. It
/// delegates to the real `LiveTransformStore` — what is under test is the
/// renderer's consultation and the store's reuse, not a second evaluator.
type private RecordingStore() =
    let inner = LiveTransformStore()
    let seen = ResizeArray<LiveTransformEvaluation>()

    /// Every evaluation since the last call, oldest first; clears the record.
    member _.Take() : LiveTransformEvaluation list =
        let xs = List.ofSeq seen
        seen.Clear()
        xs

    interface ILiveTransformStore with
        member _.Evaluate(site: string, pipeline: Transform list, source: Table) =
            inner.Evaluate(site, inner.IdentityColumn, pipeline, source)
            |> Result.map (fun e ->
                seen.Add e
                e.Result)

/// Resolve one site's row feed. An unresolved or errored resolution FAILS by
/// name rather than reading as an empty table.
let private rowsOf (sources: BindingSources) (site: Binding<Row seq>) : Row list =
    match BindingResolver.resolve<Row seq> sources site with
    | BindingResolver.Resolved rows -> List.ofSeq rows
    | BindingResolver.NotResolved -> failwith "the renderer left the Transform unresolved"
    | BindingResolver.Errored m -> failwithf "the renderer refused the Transform: %s" m
    | BindingResolver.I18nUnresolved k -> failwithf "the renderer read the Transform as an i18n key '%s'" k

/// A chip's value as the filter store holds it: boxed, and never null.
let private chipValue (v: 'a) : obj = nonNull (box v)

let private sourcesOf (chips: Map<string, obj>) (store: ILiveTransformStore option) : BindingSources =
    { BindingSources.empty with
        Filters = chips
        LiveTransforms = store }

// ---------------------------------------------------------------------------
//  generated trees and chip-edit streams
// ---------------------------------------------------------------------------

/// A deterministic 64-bit LCG — a counterexample replays from its seed.
type private Rng = { mutable State: uint64 }

let private rngOf (seed: int) : Rng =
    { State = uint64 seed * 6364136223846793005UL + 1442695040888963407UL }

let private intBelow (bound: int) (r: Rng) : int =
    r.State <- r.State * 6364136223846793005UL + 1442695040888963407UL
    int ((r.State >>> 33) % uint64 bound)

/// The string chips, each over its own column, and the domain a chip draws from.
let private stringChips =
    [ "region", [ "north"; "south"; "east"; "west" ]
      "genre", [ "drama"; "docs"; "comedy" ]
      "month", [ "jan"; "feb"; "mar"; "apr" ] ]

/// The numeric chip: a floor over `n`.
let private floorChip = "floor"

let private floorDomain = [ 0.0; 10.0; 25.0; 40.0 ]

let private allChips = (stringChips |> List.map fst) @ [ floorChip ]

/// A static table of `rows` rows over every chip's column plus `n`.
let private tableOf (rows: int) (r: Rng) : Table =
    let pick (domain: string list) = domain[intBelow domain.Length r]

    let cells =
        [ for _ in 1..rows ->
              (stringChips |> List.map (fun (_, domain) -> Str(pick domain)))
              @ [ Float(float (intBelow 50 r)) ] ]

    let names = (stringChips |> List.map fst) @ [ "n" ]

    let types = (stringChips |> List.map (fun _ -> StringType)) @ [ FloatType ]

    { Schema = List.zip names types
      Columns =
        List.zip names types
        |> List.mapi (fun i (name, ty) ->
            { Name = name
              Type = ty
              Cells = cells |> List.map (fun row -> row[i]) }) }

/// The step that reads one chip.
let private readStep (chip: string) : Transform =
    if chip = floorChip then
        Filter(Binary(Ge, Col "n", Param floorChip))
    else
        Filter(Binary(Eq, Col chip, Param chip))

/// One generated site: the chips its pipeline READS, and its row-feed binding.
/// Every site DECLARES every chip as a param — the env always binds more than a
/// pipeline reads, which is exactly the shape the reuse rule is about.
type private GenSite =
    { Reads: Set<string>
      Binding: Binding<Row seq> }

let private declaredParams: TransformParam list =
    allChips
    |> List.map (fun c ->
        { Name = c
          From = Binding.Filter(c, None) })

let private siteOf (table: Table) (reads: string list) (tail: Transform list) : GenSite =
    { Reads = Set.ofList reads
      Binding =
        Binding.Transform(
            TransformSource.Data(Embedded table),
            (reads |> List.map readStep) @ tail,
            Some declaredParams
        ) }

/// A tail drawn from three shapes: nothing, a grouped count, a sorted top-N.
let private tailOf (r: Rng) : Transform list =
    match intBelow 3 r with
    | 0 -> []
    | 1 -> [ GroupBy([ "region" ], [ { Name = "count"; Fn = Count; Of = "n" } ]) ]
    | _ -> [ Transform.sortBy [ "n", Desc ]; Transform.limit 5 0 ]

let private sitesOf (count: int) (table: Table) (r: Rng) : GenSite list =
    [ for _ in 1..count ->
          let reads = allChips |> List.filter (fun _ -> intBelow 3 r = 0)
          siteOf table reads (tailOf r) ]

/// One chip edit: set a chip to a value from its domain, or clear it (an unset
/// chip prunes the filters that read it — the lenient rule).
type private Edit = { Chip: string; Value: obj option }

let private editOf (r: Rng) : Edit =
    let chip = allChips[intBelow allChips.Length r]

    let value =
        if intBelow 5 r = 0 then
            None
        elif chip = floorChip then
            Some(chipValue floorDomain[intBelow floorDomain.Length r])
        else
            let domain = stringChips |> List.find (fun (c, _) -> c = chip) |> snd
            Some(chipValue domain[intBelow domain.Length r])

    { Chip = chip; Value = value }

let private apply (chips: Map<string, obj>) (e: Edit) : Map<string, obj> =
    match e.Value with
    | Some v -> Map.add e.Chip v chips
    | None -> Map.remove e.Chip chips

/// Did the edit change the chip's value? A no-op edit moves no key.
let private moved (chips: Map<string, obj>) (e: Edit) : bool = Map.tryFind e.Chip chips <> e.Value

// ---------------------------------------------------------------------------
//  the law
// ---------------------------------------------------------------------------

/// Render every site once through the recording store and once with none,
/// asserting the rows equal site by site, and return each site's evaluation.
let private renderAll
    (context: string)
    (store: RecordingStore)
    (chips: Map<string, obj>)
    (sites: GenSite list)
    : LiveTransformEvaluation list =
    let stored = sourcesOf chips (Some(store :> ILiveTransformStore))
    let plain = sourcesOf chips None

    sites
    |> List.mapi (fun i s ->
        let viaStore = rowsOf stored s.Binding
        let evaluations = store.Take()

        Expect.equal
            viaStore
            (rowsOf plain s.Binding)
            (sprintf "%s, site %d: the stored result is the from-scratch evaluation" context i)

        match evaluations with
        | [ e ] -> e
        | other ->
            failtestf "%s, site %d: the renderer consulted the store %d times, not once" context i (List.length other))

let private seed = 20260927

let private lawIterations = 40

let private editsPerStream = 12

[<Tests>]
let tests =
    testList
        "Phase 1761 — state for parameterised Transform sites"
        [ testCase "stored results equal a from-scratch evaluation, and an unread chip's edit evaluates nothing"
          <| fun _ ->
              let mutable unreadEdits = 0
              let mutable readEdits = 0

              for i in 0 .. lawIterations - 1 do
                  let r = rngOf (seed + i * 7919)
                  let table = tableOf (20 + intBelow 40 r) r
                  let sites = sitesOf (3 + intBelow 8 r) table r
                  let store = RecordingStore()

                  // The first render's footprints are not asserted: two generated sites whose
                  // effective pipelines coincide share a key, so the second is served the first's
                  // evaluation on first sight — correct, and the reuse the key exists for.
                  renderAll (sprintf "iter=%d first render" i) store Map.empty sites |> ignore

                  let mutable chips = Map.empty

                  for k in 1..editsPerStream do
                      let edit = editOf r
                      let changed = moved chips edit
                      chips <- apply chips edit
                      let context = sprintf "iter=%d edit=%d (%s)" i k edit.Chip
                      let evaluations = renderAll context store chips sites

                      List.zip sites evaluations
                      |> List.iteri (fun j (s, e) ->
                          if not (Set.contains edit.Chip s.Reads) || not changed then
                              unreadEdits <- unreadEdits + 1

                              Expect.equal
                                  e.Footprint.Recompute
                                  ReusedPrior
                                  (sprintf
                                      "%s, site %d: a chip its pipeline does not read (or did not move) evaluated %d rows"
                                      context
                                      j
                                      (Incremental.rowsEvaluated e.Footprint))
                          else
                              readEdits <- readEdits + 1)

              // Adequacy: a stream that never edited an unread chip, or never a read one, certified
              // half of the law.
              Expect.isGreaterThan unreadEdits 0 "no site ever saw an edit to a chip it does not read"
              Expect.isGreaterThan readEdits 0 "no site ever saw an edit to a chip it reads"

          testCase "a bound numeric chip is closed into the pipeline, and a changed value is never approximated"
          <| fun _ ->
              let r = rngOf seed
              let table = tableOf 60 r
              let site = siteOf table [ floorChip ] []
              let store = RecordingStore()

              let render (floor: float) =
                  let chips = Map.ofList [ floorChip, chipValue floor ]
                  renderAll (sprintf "floor=%g" floor) store chips [ site ] |> List.exactlyOne

              let atTen = render 10.0
              let atForty = render 40.0
              let atTenAgain = render 10.0

              Expect.notEqual atTen.Footprint.Recompute ReusedPrior "the first render evaluates"

              Expect.notEqual
                  atForty.Footprint.Recompute
                  ReusedPrior
                  "a moved chip the pipeline reads is evaluated, never served from the prior value"

              Expect.isLessThan
                  (Table.rowCount atForty.Result)
                  (Table.rowCount atTen.Result)
                  "and the higher floor keeps fewer rows, so the param was not vacuous"

              Expect.equal
                  atTenAgain.Footprint.Recompute
                  ReusedPrior
                  "returning to a value the store still holds is served from the store: the key names the value"

          testCase "the renderer keeps a one-row Expr out of the store"
          <| fun _ ->
              let store = RecordingStore()

              let sources =
                  sourcesOf (Map.ofList [ "x", chipValue 2.0 ]) (Some(store :> ILiveTransformStore))

              let expr =
                  Binding.Expr(
                      Binary(Add, Param "x", Lit(Float 1.0)),
                      Some
                          [ { Name = "x"
                              From = Binding.Filter("x", None) } ]
                  )

              match BindingResolver.resolve<obj> sources expr with
              | BindingResolver.Resolved _ -> ()
              | other -> failtestf "the Expr did not resolve: %A" other

              Expect.isEmpty (store.Take()) "a scalar expression is not a site, and costs no store entry" ]

// ---------------------------------------------------------------------------
//  the cost reading
// ---------------------------------------------------------------------------

/// Every `Chart` / `DataGrid` row feed of a tree, in walk order.
let rec private rowFeedsOf (node: Node<'Msg>) : Binding<Row seq> list =
    let own =
        match node.Kind with
        | NodeKind.Chart c -> [ c.Source ]
        | NodeKind.DataGrid g -> [ g.Source ]
        | _ -> []

    own @ (StructuralQuery.children node |> List.collect rowFeedsOf)

type private Reading =
    { FullRows: int
      StoredRows: int
      FullMedianMs: float
      StoredMedianMs: float }

let private median (xs: float list) =
    let sorted = List.sort xs
    sorted[List.length sorted / 2]

/// The whole stream, rendered with no store and through one store, on both scales.
let private readCost
    (name: string)
    (feeds: Binding<Row seq> list)
    (stream: Map<string, obj> list)
    (runs: int)
    : Reading =
    // Equivalence first: nothing is reported over a stream whose stored rows differ.
    let equivalence = RecordingStore()

    for chips in stream do
        for f in feeds do
            Expect.equal
                (rowsOf (sourcesOf chips (Some(equivalence :> ILiveTransformStore))) f)
                (rowsOf (sourcesOf chips None) f)
                (sprintf "%s: the stored rows are the from-scratch rows" name)

    // The deterministic scale. Full: every render's evaluation as a PRIME (a fresh store per
    // render), which Core defines as a full evaluation and counts on the same scale as a refresh.
    let fullRows =
        stream
        |> List.sumBy (fun chips ->
            let fresh = RecordingStore()
            let sources = sourcesOf chips (Some(fresh :> ILiveTransformStore))

            feeds
            |> List.sumBy (fun f ->
                rowsOf sources f |> ignore
                fresh.Take() |> List.sumBy (fun e -> Incremental.rowsEvaluated e.Footprint)))

    let storedRows =
        let shared = RecordingStore()

        stream
        |> List.sumBy (fun chips ->
            let sources = sourcesOf chips (Some(shared :> ILiveTransformStore))

            feeds
            |> List.sumBy (fun f ->
                rowsOf sources f |> ignore
                shared.Take() |> List.sumBy (fun e -> Incremental.rowsEvaluated e.Footprint)))

    // The clock, reported only. Each stored run starts from an empty store, so its first
    // render pays the primes the full side pays on every render.
    let time (withStore: bool) =
        let store = LiveTransformStore() :> ILiveTransformStore
        let sw = Stopwatch.StartNew()

        for chips in stream do
            let sources = sourcesOf chips (if withStore then Some store else None)

            for f in feeds do
                rowsOf sources f |> ignore

        sw.Elapsed.TotalMilliseconds

    time true |> ignore
    time false |> ignore

    let full = [ for _ in 1..runs -> time false ]
    let stored = [ for _ in 1..runs -> time true ]

    let reading =
        { FullRows = fullRows
          StoredRows = storedRows
          FullMedianMs = median full
          StoredMedianMs = median stored }

    printfn
        "Phase 1761 cost — %s: %d sites x %d renders; rowsEvaluated full=%d stored=%d; median wall-clock full=%.2fms stored=%.2fms (%d runs)"
        name
        (List.length feeds)
        (List.length stream)
        reading.FullRows
        reading.StoredRows
        reading.FullMedianMs
        reading.StoredMedianMs
        runs

    reading

let private fixturePath () : string option =
    Fuaran.Tests.CorpusRoot.tryFind ()
    |> Option.map (fun root -> Path.Combine(root, "nodes", "filterable-static-dashboard.json"))
    |> Option.filter File.Exists

[<Tests>]
let costTests =
    testList
        "Phase 1761 — the cost reading, on both scales"
        [ testCase "the corpus's filterable-static-dashboard fixture"
          <| fun _ ->
              match fixturePath () with
              | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some path ->
                  let tree =
                      match Fuaran.UI.Ops.JsonDecode.decodeNodeObj (File.ReadAllText path) with
                      | Ok node -> node
                      | Error e -> failtestf "the fixture failed to decode: %s at %s" e.Code e.Path

                  let feeds = rowFeedsOf tree
                  Expect.equal (List.length feeds) 2 "the fixture carries its chart and its grid"

                  // A dashboard session: nothing selected, a region, a genre, an unrelated chip the
                  // pipelines do not read, back to no genre, and a repeated render with nothing moved.
                  let stream =
                      [ Map.empty
                        Map.ofList [ "region", chipValue "north" ]
                        Map.ofList [ "region", chipValue "north"; "genre", chipValue "drama" ]
                        Map.ofList
                            [ "region", chipValue "north"
                              "genre", chipValue "drama"
                              "unrelated", chipValue "x" ]
                        Map.ofList [ "region", chipValue "north"; "unrelated", chipValue "x" ]
                        Map.ofList [ "region", chipValue "north"; "unrelated", chipValue "x" ] ]

                  let reading = readCost "filterable-static-dashboard" feeds stream 31

                  Expect.isLessThan
                      reading.StoredRows
                      reading.FullRows
                      "the stored stream evaluates fewer rows than the full one"

          testCase "a generated 24-site tree"
          <| fun _ ->
              let r = rngOf (seed + 24)
              let table = tableOf 2_000 r

              // Every site reads exactly one chip, spread evenly, over a grouped or top-N tail —
              // the filter-chip dashboard: one chip moves, a quarter of the charts read it.
              let feeds =
                  [ for i in 0..23 ->
                        let chip = allChips[i % allChips.Length]
                        (siteOf table [ chip ] (tailOf r)).Binding ]

              let mutable chips = Map.empty

              let stream =
                  [ yield chips
                    for _ in 1..20 do
                        let edit = editOf r
                        chips <- apply chips edit
                        yield chips ]

              let reading = readCost "generated 24-site tree" feeds stream 7

              Expect.isLessThan
                  reading.StoredRows
                  reading.FullRows
                  "the stored stream evaluates fewer rows than the full one" ]
