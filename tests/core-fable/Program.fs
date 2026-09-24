module CoreFable.Program

// The Fuaran.Core Fable smoke (Phase 217 — it moved here from fuaran-core, which runs no Fable
// compiler; the touches below are that repository's Phase 54 smoke, carried unchanged). Touch one
// public symbol per Fuaran.Core package so the Fable compiler pulls every package's source into the
// compilation — a clean compile here is what fuaran-core's "Fable-clean on encode AND decode" claim
// rests on. A touch matters beyond the project reference because `inline` members and generic
// instantiations are only compiled where they are used.
//
// With `CORE_PARITY` defined (see `core-fable.ps1`), the program is also the VALUE leg: `--vectors`
// prints `Fuaran.Core.ParityVectors.lines ()` — the cross-pipeline table the conformance kit ships
// — which the runner executes under .NET and under node and byte-compares. The table is compiled by
// the SAME Fable invocation that gates the surfaces it measures, so the two claims cannot drift.

open Fuaran.Core
open Fuaran.Core.Idl
// Phase 185 — `Fuaran.Core.Observer` is its own namespace, so the touch below needs it opened.
// `IObserver` is spelled out at its one use site rather than relied on here: `System.IObserver`
// exists, and a bare name that resolves differently depending on an unrelated `open` is not
// something a gate project should depend on.
open Fuaran.Core.Observer

// Tree — content hashing + the portable FNV-1a.
let private treeTouch = Hash.fnv1a "smoke"

// Hash — the pinned pure SHA-256. Both public forms, because the byte form and the hex form take
// different paths out of the compression pass and only the hex one would otherwise be compiled.
// This is the primitive whose Fable-cleanliness is load-bearing rather than incidental: a digest
// taken by a server has to verify in a browser, so a construct that does not transpile here is a
// cross-host verification failure, not a missing feature. The vectors themselves are pinned in
// `Fuaran.Core.Tests.HashTests`; what this file adds is that the code reaches the browser at all.
let private sha256Touch =
    sprintf "%s/%d" (Hash.sha256Hex "smoke") (Hash.sha256Bytes (Hash.utf8Bytes "smoke")).Length

// Ops — reference the skeleton-op type so Ops.fs compiles.
let private opsTouch: SkeletonOp<int, string> option = None

// Wire — canonical render / parse round-trip + the Phase-55 canonical float encoder.
let private wireTouch =
    let s = Json.render (JObj [ "k", JStr "v"; "n", JInt 1; "f", JFloat 1.5 ])

    match Json.parse s with
    | Ok _ -> Canon.canonicalFloat 1.5
    | Error e -> e

// OpStream + DAG — the empty stream / DAG + the default hash.
let private opStreamTouch = OpStream.empty
let private dagTouch = Dag.empty
let private hashTouch = OpStream.defaultHash

// Validator — forces Validator.fs (incl. ColumnValidator.cellToken's float layout) to compile.
let private validatorTouch = Validator.canonicalCodes ([]: Defect<string> list)

// Function — signature → JSON-schema projection (+ pulls in applyMemo / observedEffect / registry).
let private functionTouch =
    let sg: Signature =
        { Name = "s"
          Holes = []
          Effect = Effect.pureDeterministic }

    // Phase 210 — the capability seam's `Deferred` envelope: the host body answers in it and the
    // retyped `Registry.dispatch` has to compile under Fable too. Named deliberately, because
    // `Function.toJsonSchema` alone reached neither dispatcher.
    let cap = Capability.create "c" sg (ClientIsland Fable)
    let reg = Registry.empty |> Registry.register cap |> Result.toOption |> Option.get
    let body (_: Capability) () : Deferred<string> = Pending

    let dispatched =
        match Registry.dispatch reg "c" [] body with
        | Ok Pending -> "pending"
        | Ok other -> sprintf "%A" other
        | Error e -> sprintf "%A" e

    sprintf "%s|%s" (Function.toJsonSchema sg |> Json.render) dispatched

// Column + DataFrame — the cell-type probe + the Phase-55 float cell-string.
let private columnTouch = Cell.typeOf (Int 1)
let private dataFrameTouch = DataFrame.cellString (Float 1.5)

// Delta (Phase 98) — the delta algebra's encode AND decode, plus the identity witness. Named here
// rather than left to ride along on the DataFrame project reference: the point of this file is that
// each surface is reached DELIBERATELY, so a package that stops being Fable-clean is a compile error
// attached to the line that names it.
let private deltaTouch =
    let d =
        Delta.compose
            (Delta.ofRows (RowIdentity.byColumn "id").Scheme [ ByKey "s:a", RowAdded ])
            (Delta.ofColumns (RowIdentity.byColumn "id").Scheme [ "amount" ])

    match DeltaCodec.decode (DeltaCodec.encode d) with
    | Ok back -> Delta.toChange back
    | Error e -> Some(ColumnValuesChanged(ColumnCodec.errorString e))

// Incremental (Phase 99) — the classification, a primed state, and a delta-driven refresh. Named
// deliberately for the same reason `deltaTouch` is: the seam is the piece a browser-side consumer
// most wants (a re-render that re-evaluates only the rows that moved), so a package that stopped
// being Fable-clean here would be discovered by the consumer rather than by this file.
let private incrementalTouch =
    let idw = RowIdentity.byColumn "id"

    let t: Table =
        { Schema = [ "id", StringType; "a", IntType ]
          Columns =
            [ Column.create "id" StringType [ Str "r0"; Str "r1" ]
              Column.create "a" IntType [ Int 1; Int 2 ] ] }

    // The sort is deliberately part of the touched pipeline (Phase 115): the merge path is the
    // only place in the seam that reaches `List.sortWith`, `List.indexed` and a stored order, and a
    // pipeline without it would compile this file while leaving that path untouched.
    let pipeline =
        [ Filter(Binary(Gt, Col "a", Lit(Int 0))); Transform.sortBy [ "a", Asc ] ]

    let declared = Incremental.isIncremental (Incremental.plan pipeline)

    let ordering =
        DataFrame.rowCompareBy [ "a", IntType ] [ "a", Asc ] [ Int 1 ] [ Int 2 ]

    match Incremental.primeOn idw pipeline t with
    | Ok primed ->
        match Incremental.refreshOn idw pipeline primed (Delta.empty idw.Scheme) t with
        | Ok next -> declared, ordering, Incremental.footprintString (Incremental.footprint next)
        | Error e -> declared, ordering, DataFrame.errorString e
    | Error e -> declared, ordering, DataFrame.errorString e

// Phase 125 — the three new public shapes, touched because each carries a construct the Fable
// pipeline could plausibly refuse and a .NET suite would never notice. `Slot<'T>` is a GENERIC
// union in a wire codec; `ChainBreakReason` is a DU minted inside the op-stream walkers; and
// `ColExpr.Now`'s clock pinning is built on `lazy`, which is the one construct this phase
// introduced whose Fable behaviour is not obvious from its .NET behaviour. A browser-side consumer
// that binds a page size or names `now` reaches all three.
let private slotAndClockTouch =
    let t: Table =
        { Schema = [ "a", IntType ]
          Columns = [ Column.create "a" IntType [ Int 3; Int 1; Int 2 ] ] }

    // A param at both slot kinds, resolved through the SAME env an expression param reads.
    let paged =
        [ Sort [ Slot.Param "orderBy", Asc ]; Limit(Slot.Param "take", Slot.Lit 0) ]

    let env = Map.ofList [ "orderBy", Str "a"; "take", Int 2 ]
    let wire = DataFrameCodec.encodePipeline paged

    let bound =
        match DataFrame.evalPipelineInEnv env paged t with
        | Ok r -> string (List.length r.Columns)
        | Error e -> DataFrame.errorString e

    // The unbound refusal, which must be the strict named one rather than a default.
    let unbound =
        match DataFrame.evalPipelineInEnv Map.empty paged t with
        | Ok _ -> "UNBOUND SLOT PARAM WAS NOT REFUSED"
        | Error e -> DataFrame.errorString e

    // `Now` pinned through the lazy once-per-grain witness, and the unpinned refusal beside it.
    let clock: ClockWitness =
        fun g ->
            match g with
            | NowGrain.Date -> Date "2026-09-13"
            | NowGrain.Timestamp -> Timestamp "2026-09-13T00:00:00Z"

    let pinned =
        match DataFrame.evalPipelineAt clock [ Derive("d", Now NowGrain.Date) ] t with
        | Ok r -> string (List.length r.Schema)
        | Error e -> DataFrame.errorString e

    let unpinned =
        match DataFrame.evalPipeline [ Derive("d", Now NowGrain.Timestamp) ] t with
        | Ok _ -> "UNPINNED NOW WAS NOT REFUSED"
        | Error e -> DataFrame.errorString e

    let reason =
        ChainBreakReason.toString (ChainBreakReason.ofString "prev-hash link broken")

    sprintf "%s|%s|%s|%s|%s|%s" wire bound unbound pinned unpinned reason

// Query — declaration codec round-trip, plus the seam's `Deferred` envelope (Phase 198): the
// resolver answers in `Deferred<QueryResult>` and that envelope has to encode under Fable too.
let private queryTouch =
    let q: Query =
        { Id = "q"
          Params = []
          ResultSchema = [ "n", IntType ]
          Effect =
            { Host = ReadsHost
              Determinism = Network }
          Source = Ref "src"
          TimeoutMs = Some 5000
          PageSize = None }

    let pending = QueryCodec.encodeDeferredResult Pending

    let dispatched =
        match QueryRegistry.dispatch { Queries = Map.ofList [ q.Id, q ] } q.Id [] (fun _ -> Pending) with
        | Ok Pending -> "pending"
        | Ok other -> sprintf "%A" other
        | Error e -> sprintf "%A" e

    sprintf "%s|%s|%s" (QueryCodec.encode q) pending dispatched

// Conformance — a self-contained law (also exercises Wire's canonical float under Fable).
let private conformanceTouch = Conformance.canonicalFloatLaws 1 3

// Projection (Phase 58) — project / render / parseBack over a tiny inline witness: the
// whole encode path (the seam ships source for Fable) must be Fable-clean.
type private SmokeNode = { Id: string; Kids: SmokeNode list }

let private projectionTouch =
    let pw: ProjectionWitness<SmokeNode, string, string> =
        { Tree =
            { Id = _.Id
              KindTag = fun _ -> "n"
              Children = _.Kids
              ReplaceChildren = fun n cs -> { n with Kids = cs } }
          IdW =
            { ToString = id
              OfString = id
              Equals = (=) }
          Encode = _.Id
          Snippet = fun _ -> ""
          ParseBack = fun _ -> Ok [] }

    let root =
        { Id = "r"
          Kids = [ { Id = "c"; Kids = [] } ] }

    let p = Projection.project pw Whole root
    let snap = Projection.snapshot pw root
    let changed = Projection.project pw (ChangedSince snap) root

    match Projection.parseBack pw (Projection.render p) with
    | Ok _ -> sprintf "%d/%d" (Projection.sizeOf p) (List.length changed.Lines)
    | Error e -> e

// Propagation (Phase 68) — dependency map + topological sort (Tarjan SCC) + dirty propagation over an
// inline witness: the change-propagation surface (incl. the SCC collections) must be Fable-clean.
let private propagationTouch =
    let tw: NodeWitness<SmokeNode, string> =
        { Id = _.Id
          KindTag = fun _ -> "n"
          Children = _.Kids
          ReplaceChildren = fun n cs -> { n with Kids = cs } }

    let tidw: IdWitness<string> =
        { ToString = id
          OfString = id
          Equals = (=) }

    let root =
        { Id = "r"
          Kids = [ { Id = "a"; Kids = [] }; { Id = "b"; Kids = [] } ] }

    let readsOf (n: SmokeNode) =
        if n.Id = "a" then Seq.singleton "b" else Seq.empty

    let deps = Propagation.dependencyMap tw tidw readsOf root
    let sorted = Propagation.sort deps
    let dirty = Propagation.dirtyFromChangedIds deps (Set.singleton "b")
    let touched = Propagation.touchedBy tw tidw root (RemoveNode "a")
    // Phase 69 — the incremental recompute driver (evalFrom over the dirty subgraph) must be Fable-clean.
    let evalNode (resolve: string -> int option) id =
        Ok(
            (if id = "b" then 1 else 0)
            + (Map.find id deps
               |> Set.fold (fun s r -> s + (resolve r |> Option.defaultValue 0)) 0)
        )

    let recomputed =
        match Propagation.eval evalNode deps with
        | Ok prior ->
            match Propagation.evalFrom evalNode prior.Values (Set.singleton "b") deps with
            | Ok outcome -> Map.count outcome.Values
            | Error _ -> -1
        | Error _ -> -1

    sprintf
        "%d/%d/%d/%d/%d"
        (List.length sorted.Order)
        (List.length sorted.Cycles)
        (Set.count dirty)
        (Set.count touched)
        recomputed

// AiSurface (Phase 59) — catalogue + fast-path resolve + the proposal lifecycle over an
// inline witness: the whole surface must be Fable-clean.
let private aiSurfaceTouch =
    let w: AiSurfaceWitness<string list, string, string> =
        { ReadTools =
            [ { Name = "count"
                Description = "how many"
                Run = fun s -> JInt(List.length s) } ]
          OpKinds =
            [ { Kind = "add"
                Description = "append"
                Schema = Json.kindObj "signature" [] } ]
          KindOfOp = fun _ -> "add"
          Patterns =
            [ { Name = "add"
                Title = "Add"
                PromptAnchors = [ "add {x}" ]
                Emit = fun _ -> Ok [ "op" ] } ]
          Decide = fun _ _ -> Allow
          Apply = fun op s -> Ok(s @ [ op ])
          Explain = fun m -> { Message = m; Alternatives = [] } }

    let resolved = PatternBank.resolve w { Text = "add one"; Args = [] }

    match Proposals.submit w "a" "t" None [ "op" ] Proposals.Queue.empty [] with
    | Proposals.SubmitApplied s -> AiSurface.catalogueJson w + sprintf "%d/%A" (List.length s) resolved
    | other -> sprintf "%A" other

// Idl (Phase 97) — the domain-neutral half of the IDL engine: declare a two-kind vocabulary, then
// touch every leg a consumer can reach without the generator. This is the leg the split exists for:
// the browser hosts are Fable, so "the model, codec, sampler and sanitisation floor are portable" was
// an unprovable claim while the emitters sat in the same project and dragged
// `CultureInfo.InvariantCulture` in with them. Sampling is here rather than only encode/decode
// because the sampler's LCG is 64-bit arithmetic, which is exactly the shape that transpiles
// differently — compile-checked here, value-checked by the suite's seeded vectors on .NET.
let private idlTouch =
    let vocab: Idl =
        { Kinds =
            [ { Tag = "Note"
                Category = "leaf"
                Annotations = Annotations.Empty
                Fields =
                  [ { Name = "text"
                      Type = TStr
                      Opt = Required
                      Annotations = Annotations.Empty }
                    // Phase 113 — one ANNOTATED field, so the Fable leg actually reaches
                    // `Artifact.annotationsJson`. An empty set short-circuits before any of
                    // it runs, so an all-empty vocabulary would compile the type and prove
                    // nothing about the projection that reads it.
                    { Name = "weight"
                      Type = TFloat
                      Opt = Optional
                      Annotations =
                        { Deprecated =
                            Some
                                { Replacement = Some "text"
                                  Message = Some "carried by the text slot since 0.18.0" }
                          InProcessOnly = false
                          Since = Some "0.18.0" } } ] }
              { Tag = "Box"
                Category = "container"
                Annotations = Annotations.Empty
                Fields =
                  [ { Name = "children"
                      Type = TList TNode
                      Opt = Required
                      Annotations = Annotations.Empty } ] } ]
          Unions = []
          Enums = [ Declare.enumOf "Tone" [ "Calm"; "Loud" ] ]
          Records = []
          Defaults = []
          NodeFields = []
          Ops = []
          Wire = WireShape.Default
          Harden = HardenPolicy.Undeclared }

    let sampled = Sample.sampleNodes vocab [ "Note"; "Box" ] 20260821 4

    let roundTripped =
        sampled
        |> List.map (fun v ->
            match Encode.encode vocab v with
            | Error e -> e
            | Ok json ->
                match Decode.decode vocab json with
                | Ok _ -> json
                | Error e -> e)
        |> String.concat "|"

    sprintf
        "%d/%s/%s/%s/%d"
        (String.length (Artifact.render vocab))
        (Sanitize.sanitizeUrlOrBlank "javascript:alert(1)")
        (Sanitize.scrubMarkdown "[x](javascript:alert(1))")
        roundTripped
        (Map.count (Sanitize.sanitizeAttributes (Map.ofList [ "onclick", "x"; "title", "t" ])))

// OpStream.Attributed (Phase 81) — the attributed-stream lift + envelope codec must be Fable-clean on
// encode AND decode (GP3): liftWitness derives an attributed witness, whose Encode/Decode wrap the inner
// codec in an attribution envelope decoded via the self-contained JSONL scanner.
let private attributedTouch =
    let sw: StreamWitness<int, int, string> =
        { Apply = fun op st -> Ok(st + op)
          Encode = fun op -> Json.render (JInt op)
          Decode = fun s -> Decode.parse s |> Result.bind Decode.asInt }

    let lifted = OpStream.Attributed.liftWitness sw

    let a: Attributed<int> =
        { Actor = "a"
          Session = "s"
          Turn = Some 1
          At = "t"
          Op = 5 }

    match lifted.Decode(lifted.Encode a) with
    | Ok a' -> sprintf "%d/%A" a'.Op a'.Turn
    | Error e -> e

// FoldConfluence (Phase 100) — the N-lane fold pack over a tiny inline string witness: the DAG
// carriage, the pairwise conflict sweep, the canonical report and the shrinker must all be
// Fable-clean, since a local-first client folds its own lanes in the browser.
let private foldConfluenceTouch =
    let sw: StreamWitness<string, string, string> =
        { Apply = fun op st -> Ok(st + op)
          Encode = id
          Decode = Ok }

    let noAddr: Set<string> = Set.empty

    let fp (_: string) : Footprint =
        { Reads = noAddr
          StructureWrites = noAddr
          ContentWrites = noAddr
          UnknownParentWrites = noAddr }

    let gen: LaneGen<string, string> =
        { State0 = ""
          BaseOp = "base"
          Lanes = fun n r -> [ for i in 1..n -> [ "op" + string i ] ], r }

    let results = FoldConfluence.laneFoldLaws sw fp id gen 2 3 2

    sprintf "%d/%d" (List.length results) (List.length (FoldConfluence.arrivalOrders 3))

// Phase 121 — the sample-adequacy guard. `laneFoldLaws` above reaches `reached` already, so this
// touch exists for the parts it does not: the generic `check` over an `AdequacyDemand` list, both
// demand shapes, and the census. A Fable-clean claim about a surface nothing compiles is a claim
// about nothing.
let private sampleAdequacyTouch =
    let demands: AdequacyDemand<int> list =
        [ ReachesEvery("parity", [ "even"; "odd" ], (fun n -> [ (if n % 2 = 0 then "even" else "odd") ]))
          Spans("magnitude", 2, id) ]

    let results = SampleAdequacy.check "smoke" 1 demands [ 1; 2 ]

    let censusKinds =
        SampleAdequacy.census
        |> List.map (fun (_, cls) ->
            match cls with
            | Guarded ds -> List.length ds
            | Unconditional _ -> 0)
        |> List.sum

    sprintf "%d/%b/%d" (List.length results) (results |> List.forall (fun r -> r.Passed)) censusKinds

// Phase 126 — the construct-then-encode family. It has to be Fable-clean for the same reason the
// rest of the kit does, and one more: the authoring surfaces it certifies are the builders a client
// calls, so a law that only ran on .NET would certify the wrong host. Both arms are touched — the
// adopted one and the by-name not-adopted report — because the second is a whole branch.
let private constructThenEncodeTouch =
    let codec: Corpus.Codec<string> =
        { Encode = fun s -> Json.render (JStr s)
          Decode = fun s -> Decode.parse s |> Result.bind Decode.asString }

    let witness: ConstructWitness<string> =
        { Surface = "the smoke's own constructor"
          Construct = fun s -> Ok(String.concat "" [ s ]) }

    let cases =
        [ { Corpus.Name = "one"
            Corpus.Kind = Corpus.RoundTrip
            Corpus.Json = codec.Encode "x"
            Corpus.Tag = "str" } ]

    let adopted = Conformance.constructThenEncodeLaws "smoke" codec (Some witness) cases
    let notAdopted = Conformance.constructThenEncodeLaws "smoke" codec None cases

    sprintf "%b/%b" (adopted |> List.forall (fun r -> r.Passed)) (notAdopted |> List.exists (fun r -> not r.Passed))

// Column.Ops (Phase 185) — the columnar op-algebra's whole round trip: `canApply`, `apply`, the
// partial `invert` (the undo a browser-side editor needs), the wire codec's encode AND decode, and
// the `StreamWitness` that makes a table's edits a hash-chained stream. Named deliberately, like
// `deltaTouch` and `incrementalTouch`: the package's own Description ends "Fable-clean", and until
// this phase nothing in the repository held it to that.
let private columnOpsTouch =
    let t: Table =
        { Schema = [ "id", StringType; "amount", IntType ]
          Columns =
            [ Column.create "id" StringType [ Str "r0" ]
              Column.create "amount" IntType [ Int 1 ] ] }

    let op = SetCell("amount", 0, Int 2)

    let applied =
        match ColumnOps.canApply op t with
        | Error r -> Error r
        | Ok() -> ColumnOps.apply op t

    let inverted =
        applied
        |> Result.bind (fun _ -> ColumnOps.invert op t)
        |> Result.map (fun back -> ColumnOps.decode (ColumnOps.encode back))

    sprintf
        "%A/%A/%A"
        inverted
        (ColumnOps.changeOf op)
        (ColumnOps.streamWitness.Apply op t |> Result.mapError ColumnOps.rejectionString)

// Observer (Phase 185) — the runtime-verification seam. Its header has claimed
// "FSharp.Core only + Fable-clean … the same engine drives the in-memory .NET test substrate and a
// Fable-compiled host" since it was written, while the package sat OFF this gate, grouped with the
// build-time tools. The whole engine is reached: register, update, derive, observe, subscribe.
let private observerTouch =
    let obs =
        InMemoryObserver.create<int, string> (fun n -> if n > 1 then [ "over" ] else [])

    let seam = obs :> Fuaran.Core.Observer.IObserver<int, string>
    use _sub = seam.Subscribe(fun _ -> ())
    obs.RegisterNode("n0", 1)
    obs.Update("n0", 2)

    sprintf "%A/%d" (seam.Observe "n0" |> Option.map (fun o -> o.Flags)) (seam.ObserveTree "n0" |> List.length)

/// The value leg. Defined only when the restored `Fuaran.Core.Conformance` ships the table — the
/// runner reads that off the restore rather than off a version number, and says so either way.
let private emitVectors () =
#if CORE_PARITY
    ParityVectors.lines () |> List.iter (printfn "%s")
    true
#else
    false
#endif

// Phase 118 — the VALUE leg's entry point. `--vectors` prints the cross-pipeline table and nothing
// else. It is a MODE of this program rather than a project of its own on purpose: the table has to
// be transpiled by the same Fable invocation that gates the surfaces it measures, or the two claims
// drift apart. Without `CORE_PARITY` the mode exits non-zero rather than printing nothing, so a
// runner that asked for vectors can never read an empty table as agreement.
[<EntryPoint>]
let main argv =
    if Array.contains "--vectors" argv then
        if emitVectors () then
            0
        else
            // A NON-ZERO status, not an empty table. (Fable drops `main`'s return value, so this code
            // speaks to the .NET run; under node the runner reads the lines, and an absent table is
            // a line-count mismatch there.)
            eprintfn "core-fable: --vectors asked of a build without CORE_PARITY"
            2
    else
        // Reference each touch so nothing is dead-code-eliminated before the compiler sees it.
        [ treeTouch
          sha256Touch
          attributedTouch
          (sprintf "%A" opsTouch)
          wireTouch
          (sprintf "%A" opStreamTouch)
          (sprintf "%A" dagTouch)
          (sprintf "%A" hashTouch)
          validatorTouch
          functionTouch
          (sprintf "%A" columnTouch)
          dataFrameTouch
          // `deltaTouch` was defined by Phase 98 but never referenced here, so the whole point of the
          // file — a compile error attached to the line that names the surface — did not apply to it.
          (sprintf "%A" deltaTouch)
          (sprintf "%A" incrementalTouch)
          slotAndClockTouch
          queryTouch
          (sprintf "%A" (List.length conformanceTouch))
          projectionTouch
          propagationTouch
          aiSurfaceTouch
          idlTouch
          foldConfluenceTouch
          sampleAdequacyTouch
          constructThenEncodeTouch
          columnOpsTouch
          observerTouch ]
        |> List.iter (printfn "%s")

        0
