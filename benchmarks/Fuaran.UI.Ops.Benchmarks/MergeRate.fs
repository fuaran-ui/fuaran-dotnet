module Fuaran.UI.Ops.Benchmarks.MergeRate

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
//   - `lca`        — Core's `Dag.mergeBase` over the DAG loaded once.
//   - `sink`       — `DagMerge.merge` through the sink: load, merge base, three
//                    replays from genesis, the reconciliation and the mint.
//   - `sink_ckpt`  — `DagMerge.mergeWith` with a Core checkpoint AT the fork
//                    (`DagFold.sealAt`), so replay folds only the divergence.
//   - `lookups_ckpt` — `DagMerge.mergeDag` over the DAG loaded once, with the
//                    same checkpoint: the merge alone, with no load in it.
//                    (The key predates Phase 2645, when the loaded history was
//                    a pair of lookup functions; what it times is unchanged.)
//
//  Stopwatch-based like `TreeDiffRate`, a warm-up first:
//  `dotnet run -c Release -- merge-rate [iterations] [n,n,...]`.
//
//  `merge-snapshot <path>` writes one line per merge (and per LCA) over seeded
//  random DAG histories, so a change to the merge engine is diffed against the
//  engine it replaced: the result case, the merge node's content address, the
//  merged tree's hash, the refusal's hash.
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
        /// The fixture's records as a Core DAG, loaded once outside every timed region.
        Dag: Fuaran.Core.Dag.T<StreamEntry<Msg>>
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

    let dag = sink.Records streamId |> Async.RunSynchronously |> DagOpRecord.toDag

    { Sink = sink
      Initial = initial
      Fork = fork
      ForkTree = tree
      HeadA = a
      HeadB = b
      Dag = dag }

type Scenario = { Name: string; Run: Fixture -> unit }

/// A fixture's Core checkpoint at the fork.
let private forkCheckpoint (f: Fixture) = DagFold.sealAt f.Fork f.ForkTree

/// The default merge context with the fork checkpoint.
let private checkpointed (f: Fixture) : MergeContext<Msg> =
    { MergeContext.defaults<Msg> with
        Checkpoint = Some(forkCheckpoint f) }

let private reachCache =
    System.Runtime.CompilerServices.ConditionalWeakTable<Fixture, Fuaran.Core.Dag.Reach<StreamEntry<Msg>>>()

/// The fixture's index, built once per fixture and reused by every indexed scenario: the index a caller carries (`reach.ofDag` times its one build).
let reachOf (f: Fixture) : Fuaran.Core.Dag.Reach<StreamEntry<Msg>> =
    reachCache.GetValue(f, fun f -> Fuaran.Core.Dag.Reach.ofDag f.Dag)


let private expectMerged (outcome: MergeResult<Msg>) =
    match outcome with
    | MergeResult.Merged _ -> ()
    | other -> failwithf "merge-rate expected Merged, got %A" other

let all: Scenario list =
    [ { Name = "lca"
        Run =
          fun f ->
              match Fuaran.Core.Dag.mergeBase f.Dag f.HeadA f.HeadB with
              | Some h when h = f.Fork -> ()
              | other -> failwithf "merge-rate expected the fork as merge base, got %A" other }
      { Name = "sink"
        Run =
          fun f ->
              DagMerge.merge f.Sink streamId f.Initial f.HeadA f.HeadB now
              |> Async.RunSynchronously
              |> expectMerged }
      { Name = "sink_ckpt"
        Run =
          fun f ->
              let context = checkpointed f

              let outcome =
                  DagMerge.mergeWith context f.Sink streamId f.Initial f.HeadA f.HeadB now
                  |> Async.RunSynchronously

              expectMerged outcome.Result }
      { Name = "lookups_ckpt"
        Run =
          fun f ->
              let context = checkpointed f

              (DagMerge.mergeDag context streamId f.Initial f.Dag f.HeadA f.HeadB now).Result
              |> expectMerged }
      // Phase 2692: the same merge over the index the caller carries.
      { Name = "indexed"
        Run =
          fun f ->
              (DagMerge.mergeIndexed MergeContext.defaults streamId f.Initial (reachOf f) f.HeadA f.HeadB now)
                  .Outcome.Result
              |> expectMerged }
      { Name = "indexed_ckpt"
        Run =
          fun f ->
              let context = checkpointed f

              (DagMerge.mergeIndexed context streamId f.Initial (reachOf f) f.HeadA f.HeadB now).Outcome.Result
              |> expectMerged } ]

// ─── The attribution (Phase 2692) ────────────────────────────────────────────
//
// What one merge spends, callee by callee, over the same fixture: each line
// times ONE call of a function the merge path makes (or could make), so the
// table in docs/DAG_STRATEGY.md can say where the cost of a merge goes. The
// `reach.*` lines ask Core's reachability index (`Dag.Reach`) the same
// question as the unindexed line beside them, over an index built once
// outside the timed region (`reach.ofDag` is that build, timed on its own;
// `reach.extend` is the per-write step a sink pays to keep it current).

let private ok (what: string) (r: Result<'T, 'E>) =
    match r with
    | Ok _ -> ()
    | Error e -> failwithf "merge-rate %s: %A" what e

let private expectSome (what: string) (o: 'T option) =
    if Option.isNone o then
        failwithf "merge-rate %s: None" what

let private mintEntry: StreamEntry<Msg> =
    { Op = TreeOp.Batch []
      Timestamp = now
      PromptId = None
      ResultEnvelope = OpResultEnvelope.Success }

let callees: Scenario list =
    [ { Name = "load"
        Run = fun f -> f.Sink.Records streamId |> Async.RunSynchronously |> DagOpRecord.toDag |> ignore }
      { Name = "mergeBase"
        Run = fun f -> Fuaran.Core.Dag.mergeBase f.Dag f.HeadA f.HeadB |> expectSome "mergeBase" }
      { Name = "betweenOps"
        Run = fun f -> Fuaran.Core.Dag.betweenOps f.Dag f.Fork f.HeadA |> ignore }
      { Name = "ancestorsOf"
        Run = fun f -> Fuaran.Core.Dag.ancestorsOf f.Dag f.HeadA |> ignore }
      { Name = "verifyCheckpoint"
        Run = fun f -> DagFold.verifyCheckpoint (forkCheckpoint f) f.Dag |> ok "verifyCheckpoint" }
      { Name = "replayTo"
        Run = fun f -> DagFold.replayTo f.Initial f.Dag f.HeadA |> ok "replayTo" }
      { Name = "replayFrom"
        Run = fun f -> DagFold.replayFrom (forkCheckpoint f) f.Dag f.HeadA |> ok "replayFrom" }
      { Name = "mint"
        Run =
          fun f ->
              Fuaran.Core.Dag.merge (DagOpRecord.stream ()) (Actor.Human "bench") mintEntry f.HeadA f.HeadB f.Dag
              |> ok "mint" }
      { Name = "reach.ofDag"
        Run = fun f -> Fuaran.Core.Dag.Reach.ofDag f.Dag |> ignore }
      { Name = "reach.mergeBase"
        Run =
          fun f ->
              Fuaran.Core.Dag.Reach.mergeBase (reachOf f) f.HeadA f.HeadB
              |> expectSome "reach.mergeBase" }
      { Name = "reach.between"
        Run = fun f -> Fuaran.Core.Dag.Reach.between (reachOf f) f.Fork f.HeadA |> ignore }
      { Name = "reach.ancestors"
        Run = fun f -> Fuaran.Core.Dag.Reach.ancestors (reachOf f) f.HeadA |> ignore }
      { Name = "reach.replayTo"
        Run =
          fun f ->
              Fuaran.Core.Dag.tryReplayToWith (DagFold.witness ()) f.Initial (reachOf f) f.HeadA
              |> ok "reach.replayTo" }
      { Name = "reach.replayFrom"
        Run =
          fun f ->
              Fuaran.Core.Dag.replayFromWith (DagFold.witness ()) (forkCheckpoint f) (reachOf f) f.HeadA
              |> ok "reach.replayFrom" }
      { Name = "reach.extend"
        Run =
          fun f ->
              Fuaran.Core.Dag.mergeIndexed
                  (DagOpRecord.stream ())
                  (Actor.Human "bench")
                  mintEntry
                  f.HeadA
                  f.HeadB
                  (reachOf f)
              |> ok "reach.extend" } ]

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

// ─── The bound (merge-bound, Phase 2692) ─────────────────────────────────────
//
// The cost pin: the indexed, checkpointed merge (`indexed_ckpt`) at 20,000
// records of shared history against the same merge at 1,000, over the same
// twenty-record divergence. The pin is on ALLOCATION, which repeats to within a
// few bytes on any machine; wall time is printed beside it and not pinned,
// because the hosts this runs on are rarely idle (docs/DAG_STRATEGY.md records
// runs whose wall figures moved 2x between two passes).
//
// The factor is the table's, not a target: measured 17.9x (3.30 MB -> 59.2 MB)
// at the Phase 2692 landing, against 26.3x (32.9 MB -> 864.5 MB) for the same
// merge through the unindexed walks (`lookups_ckpt`). So 20x passes the indexed
// merge and fails a merge path that falls back to the walks. It is NOT flat:
// what remains linear in the history is Core's indexed checkpoint replay
// (`Dag.replayFromWith` reads the checkpoint's whole closure as a set and the
// head's whole order), a Core finding the same document records.

/// The largest allocation ratio, 20,000 records over 1,000, the indexed merge may show.
let boundFactor = 20.0

/// The shared-history sizes the bound compares.
let boundSizes = 1_000, 20_000

/// The bound's measurement: (small, large) as (mean ms, bytes) each, and the
/// allocation ratio large / small.
let bound (iterations: int) : (float * float) * (float * float) * float =
    let scenario = all |> List.find (fun s -> s.Name = "indexed_ckpt")
    let smallN, largeN = boundSizes
    let small = fixture smallN
    let large = fixture largeN
    // The index is the caller's, built once outside the timed region.
    reachOf small |> ignore
    reachOf large |> ignore
    let s = measure scenario small iterations
    let l = measure scenario large iterations
    s, l, snd l / snd s

// ─── The identity snapshot (merge-snapshot) ──────────────────────────────────

let private digest (result: MergeResult<Msg>) : string =
    match result with
    | MergeResult.AlreadyMerged h -> $"already {h}"
    | MergeResult.FastForward h -> $"ff {h}"
    | MergeResult.Merged(r, t) ->
        let parents = String.Join(",", r.Parents)
        $"merged {r.Hash} tree={treeHash t} parents={parents} op={sha (CanonicalJson.encodeOp r.Op)}"
    | MergeResult.Refused fault ->
        let refusal = sprintf "%A" fault
        $"refused {sha refusal}"
    | MergeResult.DeltaNotReplayable(t, m) ->
        $"undeliverable tree={treeHash t} derived={m.DerivedHash} replayed=%A{m.ReplayedHash} fault=%A{m.Fault}"
    | MergeResult.MintRefused f -> $"mintrefused %A{f}"
    | MergeResult.NoCommonBase -> "nobase"
    | MergeResult.ReplayFailed e -> $"replayfailed %A{e}"

let private lcaDigest (r: string option) : string =
    match r with
    | None -> "none"
    | Some h -> $"unique {h}"

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

            match DagMerge.merge sink streamId initial ha hb at |> Async.RunSynchronously with
            | MergeResult.Merged(r, t) ->
                add sink r
                all.Add r.Hash

                // Sometimes mint the swapped twin too: two merge nodes over the
                // same pair is the criss-cross a later merge must resolve.
                if rng.Next 5 = 0 then
                    // A later stamp: the merge address sorts its parents, so the
                    // twin at the same instant would claim the same address.
                    let twinAt = stamp (tick ())

                    match DagMerge.merge sink streamId initial hb ha twinAt |> Async.RunSynchronously with
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

    let dag = sink.Records streamId |> Async.RunSynchronously |> DagOpRecord.toDag

    let pick () = all[rng.Next all.Count]

    for q in 1..120 do
        let a = pick ()
        let b = if rng.Next 10 = 0 then a else pick ()
        line $"lca {q} {a} {b} -> {lcaDigest (Fuaran.Core.Dag.mergeBase dag a b)}"

        let result = DagMerge.merge sink streamId initial a b now |> Async.RunSynchronously

        line $"merge {q} -> {digest result}"

        // Every fifth query again under a checkpoint at a random record.
        if q % 5 = 0 then
            let at = pick ()

            match DagFold.replayTo initial dag at with
            | Ok snap ->
                let context =
                    { MergeContext.defaults<Msg> with
                        Checkpoint = Some(DagFold.sealAt at snap) }

                let gated =
                    DagMerge.mergeWith context sink streamId initial a b now
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
            DagMerge.mergeIntoTrunkWith MergeContext.defaults sink streamId initial h now 3
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
            DagMerge.merge f.Sink streamId f.Initial f.HeadA f.HeadB now
            |> Async.RunSynchronously

        out.AppendLine $"linear n={n} -> {digest r}" |> ignore

let writeSnapshot (path: string) =
    let out = StringBuilder()
    linearScenarios out
    guestScenarios out

    for seed, steps in [ 1, 60; 2, 250; 3, 600 ] do
        randomHistory seed steps out

    File.WriteAllText(path, out.ToString())
