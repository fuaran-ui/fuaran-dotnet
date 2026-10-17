module Fuaran.UI.Ops.Benchmarks.MergeRate

#nowarn "44" // the digest names every `MergeResult` case, the obsolete one included

open System
open System.Diagnostics
open System.IO
open System.Text
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.InMemory
open Fuaran.UI.OpStream.Dag.Merge

// ============================================================================
//  MergeRate (Phase 2062) — what a DAG merge costs against the length of the
//  history the two branches SHARE, and a snapshot of what merges return.
//
//  The history is a linear trunk of `n` records off a genesis dashboard of 16
//  markdown panes, each record restyling one pane. Two branches of ten records
//  each then fork off the trunk tip and restyle disjoint panes, so every merge
//  measured is the same `Merged` over a divergence of twenty records; only the
//  shared history below the fork grows. Scenarios:
//
//   - `lca`        — `DagTopology.lca` over the loaded parent relation alone.
//   - `sink`       — `DagMerge.merge` through the sink: load, LCA, three replays
//                    from genesis, the three-way merge and the mint.
//   - `sink_ckpt`  — `DagMerge.mergeGatedWith` with a checkpoint AT the fork, so
//                    replay folds only the divergence.
//   - `lookups_ckpt` — `DagMerge.mergeLookups` over records loaded once, with the
//                    same checkpoint: the merge alone, with no load in it.
//
//  Stopwatch-based like `TreeDiffRate`, a warm-up first:
//  `dotnet run -c Release -- merge-rate [iterations] [n,n,...]`.
//
//  `merge-snapshot <path>` writes one line per merge (and per LCA) over seeded
//  random DAG histories, so a change to the merge engine is diffed against the
//  engine it replaced: the result case, the merge node's content address and
//  outcome hash, the merged tree's hash, the conflict envelope's hash.
// ============================================================================

type Msg = | NoOp

let private streamId = "s"

let private paneCount = 16

let private pane (i: int) = $"p{i}"

let private genesisTree () : Node<Msg> =
    Fuaran.dashboard
        "dash"
        { Defaults.dashboard<Msg> with
            Children = [ for i in 0 .. paneCount - 1 -> Fuaran.markdown (pane i) $"Pane {i}" ] }

let private tones =
    [| ToneVariant.Default
       ToneVariant.Subdued
       ToneVariant.Brand
       ToneVariant.Success
       ToneVariant.Warning
       ToneVariant.Critical
       ToneVariant.Info |]

let private restyle (id: string) (tone: ToneVariant) : TreeOp<Msg> =
    TreeOp.UpdateStyle(NodeId id, { Defaults.style with Tone = tone })

let private stamp (unix: int64) = DateTimeOffset.FromUnixTimeSeconds unix

let private record (parents: string list) (op: TreeOp<Msg>) (prompt: string option) (unix: int64) : DagOpRecord<Msg> =
    DagOpRecord.create streamId parents op prompt (Actor.Human "bench") (stamp unix) OpResultEnvelope.Success

let private add (sink: IDagOpStreamSink<Msg>) (r: DagOpRecord<Msg>) = sink.Add r |> Async.RunSynchronously

/// The host classifier the suites use: no prompt id ⇒ `Primary`.
let recordAuthor (r: DagOpRecord<Msg>) : MergeAuthor =
    match r.PromptId with
    | None -> MergeAuthor.Primary
    | Some p -> MergeAuthor.Secondary(Some p)

let private now = stamp 9_000_000L

let private sha (s: string) = Fuaran.Core.Hash.sha256Hex s

let private treeHash (t: Node<Msg>) = CanonicalJson.encodeNode t |> sha

// ─── The linear fixture (merge-rate) ─────────────────────────────────────────

type Fixture =
    {
        Sink: IDagOpStreamSink<Msg>
        Initial: Node<Msg>
        Fork: string
        ForkTree: Node<Msg>
        HeadA: string
        HeadB: string
        /// The fixture's records, loaded once outside every timed region.
        GetRec: string -> DagOpRecord<Msg> option
        GetParents: string -> string list
    }

/// `n` shared trunk records, then two disjoint ten-record branches off the tip.
let fixture (n: int) : Fixture =
    let sink = InMemoryDagSink.create<Msg> ()
    let initial = genesisTree ()
    let mutable tip: string list = []
    let mutable tree = initial

    for i in 0 .. n - 1 do
        let op = restyle (pane (i % paneCount)) tones[i % tones.Length]
        let r = record tip op None (int64 (1_000 + i))
        add sink r

        tree <-
            (match Apply.apply op tree with
             | Ok t -> t
             | Error e -> failwithf "fixture apply: %A" e)

        tip <- [ r.Hash ]

    let fork = List.head tip

    let branch (panes: int list) (tone: ToneVariant) (t0: int64) =
        panes
        |> List.mapi (fun i p -> i, p)
        |> List.fold
            (fun (parent: string) (i, p) ->
                let r = record [ parent ] (restyle (pane p) tone) None (t0 + int64 i)
                add sink r
                r.Hash)
            fork

    let a = branch [ for i in 0..9 -> i % 6 ] ToneVariant.Brand 5_000_000L
    let b = branch [ for i in 0..9 -> 8 + i % 6 ] ToneVariant.Critical 6_000_000L

    let byHash =
        sink.Records streamId
        |> Async.RunSynchronously
        |> List.map (fun r -> r.Hash, r)
        |> dict

    let getRec h =
        match byHash.TryGetValue h with
        | true, r -> Some r
        | false, _ -> None

    let getParents h =
        getRec h |> Option.map _.Parents |> Option.defaultValue []

    { Sink = sink
      Initial = initial
      Fork = fork
      ForkTree = tree
      HeadA = a
      HeadB = b
      GetRec = getRec
      GetParents = getParents }

type Scenario = { Name: string; Run: Fixture -> unit }

let private expectMerged (outcome: MergeResult<Msg>) =
    match outcome with
    | MergeResult.Merged _ -> ()
    | other -> failwithf "merge-rate expected Merged, got %A" other

let all: Scenario list =
    [ { Name = "lca"
        Run =
          fun f ->
              match DagTopology.lca f.GetParents f.HeadA f.HeadB with
              | LcaResult.Unique h when h = f.Fork -> ()
              | other -> failwithf "merge-rate expected the fork as LCA, got %A" other }
      { Name = "sink"
        Run =
          fun f ->
              DagMerge.merge recordAuthor f.Sink streamId f.Initial f.HeadA f.HeadB now
              |> Async.RunSynchronously
              |> expectMerged }
      { Name = "sink_ckpt"
        Run =
          fun f ->
              let context =
                  { MergeContext.defaults<Msg> with
                      Checkpoint = Some(DagCheckpoint.create streamId f.Fork f.ForkTree) }

              let outcome =
                  DagMerge.mergeGatedWith
                      recordAuthor
                      f.Sink
                      streamId
                      f.Initial
                      f.HeadA
                      f.HeadB
                      now
                      MergePolicy.lenient
                      context
                  |> Async.RunSynchronously

              expectMerged outcome.Result }
      { Name = "lookups_ckpt"
        Run =
          fun f ->
              let context =
                  { MergeContext.defaults<Msg> with
                      Checkpoint = Some(DagCheckpoint.create streamId f.Fork f.ForkTree) }

              (DagMerge.mergeLookups recordAuthor f.GetRec f.GetParents streamId f.Initial f.HeadA f.HeadB now context)
                  .Result
              |> expectMerged } ]

/// Mean wall-time (ms) and allocation (bytes, all threads) per run.
let measure (scenario: Scenario) (f: Fixture) (iterations: int) : float * float =
    scenario.Run f

    GC.Collect()
    GC.WaitForPendingFinalizers()
    GC.Collect()
    let allocBefore = GC.GetTotalAllocatedBytes true
    let sw = Stopwatch.StartNew()

    for _ in 1..iterations do
        scenario.Run f

    sw.Stop()
    let allocAfter = GC.GetTotalAllocatedBytes true
    sw.Elapsed.TotalMilliseconds / float iterations, float (allocAfter - allocBefore) / float iterations

// ─── The identity snapshot (merge-snapshot) ──────────────────────────────────

let private digest (result: MergeResult<Msg>) : string =
    match result with
    | MergeResult.AlreadyMerged h -> $"already {h}"
    | MergeResult.FastForward h -> $"ff {h}"
    | MergeResult.Merged(r, t) ->
        let outcome = defaultArg r.OutcomeHash "-"
        let parents = String.Join(",", r.Parents)
        $"merged {r.Hash} outcome={outcome} tree={treeHash t} parents={parents} op={sha (CanonicalJson.encodeOp r.Op)}"
    | MergeResult.NeedsManualMerge cs -> $"conflict n={List.length cs} envelope={sha (MergeConflict.encodeEnvelope cs)}"
    | MergeResult.DeltaNotReplayable(t, m) ->
        $"undeliverable tree={treeHash t} outcome={m.OutcomeHash} replayed=%A{m.ReplayedHash} apply=%A{m.ApplyError}"
    | MergeResult.NeedsThreeWayMerge cs -> $"""threeway {String.Join(",", cs)}"""
    | MergeResult.NoCommonBase -> "nobase"
    | MergeResult.ReplayFailed e -> $"replayfailed %A{e}"

let private lcaDigest (r: LcaResult) : string =
    match r with
    | LcaResult.None -> "none"
    | LcaResult.Unique h -> $"unique {h}"
    | LcaResult.Ambiguous cs -> $"""ambiguous {String.Join(",", cs)}"""

/// A seeded random DAG over `steps` acts: extend a head with a restyle, an
/// insert or a remove valid on that head's tree, or merge two heads through the
/// CURRENT engine (keeping, sometimes, both parents live and, sometimes, the
/// swapped-parent twin — the criss-cross shape the recursive base resolves).
/// Because the generator merges through the engine under test, a changed
/// engine changes the history, and the snapshot shows it.
let private randomHistory (seed: int) (steps: int) (out: StringBuilder) =
    let rng = Random seed
    let sink = InMemoryDagSink.create<Msg> ()
    let initial = genesisTree ()
    let genesis = record [] (restyle (pane 0) ToneVariant.Brand) None 1L
    add sink genesis

    let apply op tree =
        match Apply.apply op tree with
        | Ok t -> Some t
        | Error _ -> None

    // (head hash, its tree)
    let heads = ResizeArray<string * Node<Msg>>()
    heads.Add(genesis.Hash, (apply genesis.Op initial).Value)
    let all = ResizeArray<string>([ genesis.Hash ])
    let mutable clock = 10L
    let mutable inserted = 0

    let tick () =
        clock <- clock + 1L
        clock

    let line (s: string) = out.AppendLine s |> ignore

    for step in 1..steps do
        let roll = rng.Next 100

        // Extend a head in place, fork a new head off one (the old head stays
        // live), or merge two heads.
        let fork = roll >= 40 && roll < 60 && heads.Count < 6

        if roll < 60 || heads.Count < 2 then
            let hi = rng.Next heads.Count
            let head, tree = heads[hi]

            let inserts =
                Introspect.getChildren tree.Kind
                |> Option.defaultValue []
                |> List.map _.Id
                |> List.filter (fun id -> id.StartsWith "n")

            let op =
                match rng.Next 10 with
                | 0 ->
                    inserted <- inserted + 1
                    TreeOp.InsertChild(NodeId "dash", Fuaran.markdown $"n{inserted}" $"Inserted {inserted}")
                | 1 when not (List.isEmpty inserts) -> TreeOp.RemoveNode(NodeId(inserts[rng.Next inserts.Length]))
                | _ -> restyle (pane (rng.Next paneCount)) tones[rng.Next tones.Length]

            let prompt = if rng.Next 4 = 0 then Some $"agent-{rng.Next 3}" else None

            match apply op tree with
            | Some tree' ->
                let r = record [ head ] op prompt (tick ())
                add sink r
                all.Add r.Hash

                if fork then
                    heads.Add(r.Hash, tree')
                else
                    heads[hi] <- (r.Hash, tree')
            | None -> ()
        else
            let i = rng.Next heads.Count
            let j = (i + 1 + rng.Next(heads.Count - 1)) % heads.Count
            let ha, _ = heads[i]
            let hb, _ = heads[j]
            let at = stamp (tick ())

            match
                DagMerge.merge recordAuthor sink streamId initial ha hb at
                |> Async.RunSynchronously
            with
            | MergeResult.Merged(r, t) ->
                add sink r
                all.Add r.Hash

                // Sometimes mint the swapped twin too: two merge nodes over the
                // same pair is the criss-cross a later merge must resolve.
                if rng.Next 5 = 0 then
                    // A later stamp: the merge address sorts its parents, so the
                    // twin at the same instant would claim the same address.
                    let twinAt = stamp (tick ())

                    match
                        DagMerge.merge recordAuthor sink streamId initial hb ha twinAt
                        |> Async.RunSynchronously
                    with
                    | MergeResult.Merged(twin, twinTree) ->
                        add sink twin
                        all.Add twin.Hash
                        heads.Add(twin.Hash, twinTree)
                    | _ -> ()

                if rng.Next 2 = 0 then
                    let keep = heads |> Seq.filter (fun (h, _) -> h <> ha && h <> hb) |> Seq.toList
                    heads.Clear()
                    heads.AddRange keep

                heads.Add(r.Hash, t)
            | MergeResult.FastForward h ->
                let keep = heads |> Seq.filter (fun (x, _) -> x <> ha && x <> hb) |> Seq.toList
                let tree = if h = ha then snd heads[i] else snd heads[j]
                heads.Clear()
                heads.AddRange keep
                heads.Add(h, tree)
            | _ -> ()

        if step % 50 = 0 then
            line $"history seed={seed} step={step} records={all.Count} heads={heads.Count}"

    let records = sink.Records streamId |> Async.RunSynchronously
    let byHash = records |> List.map (fun r -> r.Hash, r) |> dict

    let getParents h =
        match byHash.TryGetValue h with
        | true, r -> r.Parents
        | false, _ -> []

    let getRec h =
        match byHash.TryGetValue h with
        | true, r -> Some r
        | false, _ -> None

    let pick () = all[rng.Next all.Count]

    for q in 1..120 do
        let a = pick ()
        let b = if rng.Next 10 = 0 then a else pick ()
        line $"lca {q} {a} {b} -> {lcaDigest (DagTopology.lca getParents a b)}"

        let result =
            DagMerge.merge recordAuthor sink streamId initial a b now
            |> Async.RunSynchronously

        line $"merge {q} -> {digest result}"

        // Every fifth query again under a checkpoint at a random record.
        if q % 5 = 0 then
            let at = pick ()

            match DagReplay.replay getRec initial at with
            | Ok snap ->
                let context =
                    { MergeContext.defaults<Msg> with
                        Checkpoint = Some(DagCheckpoint.create streamId at snap) }

                let gated =
                    DagMerge.mergeGatedWith recordAuthor sink streamId initial a b now MergePolicy.lenient context
                    |> Async.RunSynchronously

                line $"merge-ckpt {q} at={at} -> {digest gated.Result}"
            | Error e -> line $"merge-ckpt {q} at={at} -> snapshot failed %A{e}"

    // The trunk path: point the trunk at the oldest record, then merge every
    // live head into it in turn, reporting each outcome and the trunk after.
    sink.TryAdvanceHead(streamId, None, genesis.Hash)
    |> Async.RunSynchronously
    |> ignore

    for (h, _) in Seq.toList heads do
        let outcome =
            DagMerge.mergeIntoTrunkWith recordAuthor sink streamId initial h now 3 MergeContext.defaults
            |> Async.RunSynchronously

        let trunk = sink.Head streamId |> Async.RunSynchronously
        let outcomeDigest = sprintf "%A" outcome |> sha
        line $"trunk {h} -> {outcomeDigest} head=%A{trunk}"

/// The guest path: a host trunk, a guest forked off one of its records, edits
/// on both sides (disjoint, then contended), converged through `GuestConvergence`.
let private guestScenarios (out: StringBuilder) =
    for hostLength in [ 1; 40; 400 ] do
        for contended in [ false; true ] do
            let sink = InMemoryDagSink.create<Msg> ()
            let initial = genesisTree ()
            let mutable tip: string list = []

            for i in 0 .. hostLength - 1 do
                let r =
                    record tip (restyle (pane (i % paneCount)) tones[i % tones.Length]) None (int64 (100 + i))

                add sink r
                tip <- [ r.Hash ]

            let anchor = List.head tip

            let hostHead =
                DagOpRecord.create
                    "s"
                    [ anchor ]
                    (restyle (pane 1) ToneVariant.Success)
                    None
                    (Actor.Human "host")
                    (stamp 50_000L)
                    OpResultEnvelope.Success

            add sink hostHead

            let g0 =
                GuestFork.genesis
                    "g1"
                    anchor
                    (restyle (pane (if contended then 1 else 2)) ToneVariant.Critical)
                    None
                    (Actor.Human "guest")
                    (stamp 50_001L)
                    OpResultEnvelope.Success

            add sink g0

            let g1 =
                DagOpRecord.create
                    (GuestFork.streamId "g1")
                    [ g0.Hash ]
                    (restyle (pane 3) ToneVariant.Warning)
                    None
                    (Actor.Human "guest")
                    (stamp 50_002L)
                    OpResultEnvelope.Success

            add sink g1

            let result =
                GuestConvergence.converge sink "s" "g1" initial hostHead.Hash g1.Hash now
                |> Async.RunSynchronously

            let ffResult =
                GuestConvergence.converge sink "s" "g1" initial anchor g1.Hash now
                |> Async.RunSynchronously

            out.AppendLine $"guest host={hostLength} contended={contended} -> {digest result}"
            |> ignore

            out.AppendLine $"guest-ff host={hostLength} contended={contended} -> {digest ffResult}"
            |> ignore

/// The linear merge-rate fixture's own results, at the sizes it is timed at.
let private linearScenarios (out: StringBuilder) =
    for n in [ 1; 100; 1_000 ] do
        let f = fixture n

        let r =
            DagMerge.merge recordAuthor f.Sink streamId f.Initial f.HeadA f.HeadB now
            |> Async.RunSynchronously

        out.AppendLine $"linear n={n} -> {digest r}" |> ignore

let writeSnapshot (path: string) =
    let out = StringBuilder()
    linearScenarios out
    guestScenarios out

    for seed, steps in [ 1, 60; 2, 250; 3, 600 ] do
        randomHistory seed steps out

    File.WriteAllText(path, out.ToString())
