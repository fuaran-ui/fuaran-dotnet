module Fuaran.UI.OpStream.Dag.Tests.IndexedMergeTests

open System
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.Merge
open Fuaran.UI.OpStream.TestSupport

// ============================================================================
//  The merge over Core's reachability index (Phase 2692).
//
//  `DagMerge.mergeIndexed` asks every ancestry question of a merge — the base,
//  the fast-forward test, the two deltas, the replays — of Core's `Dag.Reach`,
//  and mints through `Dag.mergeIndexed`, handing back the index extended with
//  the merge node. The change is a COST change and nothing else, so these tests
//  hold three things:
//
//   1. on every `foldFixtures` triad, under every context the merge takes, the
//      indexed merge answers exactly what `mergeDag` answers;
//   2. on generated DAGs with criss-cross merges (two merge nodes over one
//      pair) and guest forks (a branch off an older node under another stream
//      id), every merge agrees, every Core ancestry answer the merge path asks
//      agrees indexed and unindexed, and the index CARRIED through Core's
//      indexed writes answers as `Reach.ofDag` of the DAG it ended on;
//   3. the carried index is the one handed back: `Merged` extends it with the
//      merge node, every other outcome returns the index merged over.
//
//  Go-red: answering any of them from the wrong index (the pre-merge one, or
//  one over a different DAG), or dropping the extension, fails (2) or (3).
// ============================================================================

type private Reach = Fuaran.Core.Dag.Reach<StreamEntry<TestMsg>>
type private Dag = Fuaran.Core.Dag.T<StreamEntry<TestMsg>>

let private streamId = "indexed-stream"
let private now = timestamp 1700000900L

/// What a merge outcome says, as a comparable string (`MergeResult` holds trees
/// and faults; their printed form is exact and total).
let private digest (outcome: GatedMergeOutcome<TestMsg>) : string = sprintf "%A" outcome

/// Every ancestry question the merge path asks of `a` and `b`, answered by the
/// index and by Core's unindexed walks over the same DAG: equal, or a failure
/// naming the question.
let private agreeOn (label: string) (reach: Reach) (dag: Dag) (a: string) (b: string) =
    let ids (nodes: Fuaran.Core.DagNode<_> list) = nodes |> List.map _.Id

    Expect.equal
        (Fuaran.Core.Dag.Reach.mergeBase reach a b)
        (Fuaran.Core.Dag.mergeBase dag a b)
        $"{label}: mergeBase {a} {b}"

    Expect.equal
        (Fuaran.Core.Dag.Reach.commonBase reach [ a; b ])
        (Fuaran.Core.Dag.commonBase dag [ a; b ])
        $"{label}: commonBase {a} {b}"

    Expect.equal
        (ids (Fuaran.Core.Dag.Reach.between reach a b))
        (ids (Fuaran.Core.Dag.between dag a b))
        $"{label}: between {a} {b}"

    Expect.equal
        (Fuaran.Core.Dag.Reach.reaches reach a b)
        (Set.contains a (Fuaran.Core.Dag.ancestorsOf dag b))
        $"{label}: reaches {a} {b}"

/// `agreeOn` over every ordered pair of `ids`.
let private agreeAll (label: string) (reach: Reach) (dag: Dag) (ids: string list) =
    for a in ids do
        for b in ids do
            agreeOn label reach dag a b

/// Merge `a` and `b` both ways and require one answer; the indexed merge's
/// carried index is returned with the outcome.
let private mergeBoth
    (context: MergeContext<TestMsg>)
    (initial: Node<TestMsg>)
    (reach: Reach)
    (a: string)
    (b: string)
    (at: DateTimeOffset)
    : GatedMergeOutcome<TestMsg> * Reach =
    let dag = Fuaran.Core.Dag.Reach.dag reach
    let plain = DagMerge.mergeDag context streamId initial dag a b at
    let indexed = DagMerge.mergeIndexed context streamId initial reach a b at
    Expect.equal (digest indexed.Outcome) (digest plain) $"mergeIndexed equals mergeDag on {a} {b}"

    match indexed.Outcome.Result with
    | MergeResult.Merged(record, _) ->
        let carried = Fuaran.Core.Dag.Reach.dag indexed.Reach
        Expect.isTrue (carried.Nodes.ContainsKey record.Hash) "a Merged outcome's index holds the merge node"
        Expect.equal carried.Nodes.Count (dag.Nodes.Count + 1) "and nothing else new"
    | _ -> Expect.isTrue (obj.ReferenceEquals(indexed.Reach, reach)) "any other outcome returns the index merged over"

    indexed.Outcome, indexed.Reach

// ─── 1. The fold fixtures ────────────────────────────────────────────────────

/// A genesis node (an empty Batch: the base IS the fixture's tree) and the two
/// branches' ops recorded off it.
let private forkOf (opsA: TreeOp<TestMsg> list) (opsB: TreeOp<TestMsg> list) =
    let genesis = stepRecord streamId None (TreeOp.Batch []) 1700000000L

    let branch (ops: TreeOp<TestMsg> list) (t0: int64) =
        ops
        |> List.indexed
        |> List.fold
            (fun (acc: DagOpRecord<TestMsg> list) (i, op) ->
                stepRecord streamId (Some(List.head acc)) op (t0 + int64 i) :: acc)
            [ genesis ]

    let a = branch opsA 1700000100L
    let b = branch opsB 1700000200L
    let records = a @ b |> List.distinctBy _.Hash
    genesis, (List.head a).Hash, (List.head b).Hash, DagOpRecord.toDag records

let private fixtureTests =
    test "on every foldFixtures triad, under every context, the indexed merge is mergeDag" {
        for (id, _, _, baseT, opsA, opsB) in FoldCorpus.fixtures do
            let genesis, a, b, dag = forkOf opsA opsB
            let reach = Fuaran.Core.Dag.Reach.ofDag dag

            let checkpoint =
                match DagFold.checkpointAt baseT dag genesis.Hash with
                | Ok cp -> cp
                | Error e -> failtestf "%s: checkpoint at genesis: %A" id e

            let contexts =
                [ "defaults", MergeContext.defaults
                  "precedence A",
                  { MergeContext.defaults with
                      Precedence = Some ReconcileSide.A }
                  "precedence B",
                  { MergeContext.defaults with
                      Precedence = Some ReconcileSide.B }
                  "checkpoint",
                  { MergeContext.defaults with
                      Checkpoint = Some checkpoint }
                  "gated",
                  { MergeContext.defaults with
                      Policy = MergePolicy.gated FoldCorpus.gatedValidator }
                  "diagnostic",
                  { MergeContext.defaults with
                      Policy = MergePolicy.diagnostic FoldCorpus.gatedValidator } ]

            for _, context in contexts do
                for x, y in [ a, b; b, a; a, a; genesis.Hash, a; b, genesis.Hash ] do
                    mergeBoth context baseT reach x y now |> ignore

            agreeAll id reach dag (dag.Nodes.Keys |> List.ofSeq)
    }

// ─── 2. The generated population ─────────────────────────────────────────────

let private paneCount = 6

let private initialTree () : Node<TestMsg> =
    container "dash" [ for i in 0 .. paneCount - 1 -> Fuaran.markdown $"p{i}" $"Pane {i}" ]

let private tones =
    [| ToneVariant.Default
       ToneVariant.Brand
       ToneVariant.Success
       ToneVariant.Warning
       ToneVariant.Critical |]

/// A seeded history: appends, forks, guest forks and merges (with swapped
/// twins, the criss-cross), the index carried through Core's indexed writes
/// and checked against the unindexed answers at every merge.
let private population (seed: int) (steps: int) =
    let rng = Random seed
    let initial = initialTree ()
    let actor = Actor.Human "gen"
    let genesis = stepRecord streamId None (TreeOp.Batch []) 10L
    let mutable reach = Fuaran.Core.Dag.Reach.ofDag (DagOpRecord.toDag [ genesis ])
    let heads = ResizeArray<string>([ genesis.Hash ])
    let all = ResizeArray<string>([ genesis.Hash ])
    let mutable clock = 100L
    let mutable guests = 0
    let mutable merges = 0
    let mutable twins = 0

    let tick () =
        clock <- clock + 1L
        clock

    let treeAt (head: string) =
        match DagFold.replayToWith initial reach head with
        | Ok t -> t
        | Error e -> failtestf "seed %d: replay %s: %A" seed head e

    /// Append `record` (a single-parent step) through Core's indexed write; the
    /// node Core writes must be the record's.
    let appendIndexed (record: DagOpRecord<TestMsg>) =
        match
            Fuaran.Core.Dag.appendIndexed
                (DagOpRecord.stream ())
                record.Node.Actor
                record.Entry
                (List.exactlyOne record.Parents)
                reach
        with
        | Ok indexed ->
            Expect.equal indexed.Written.Id record.Hash "Core's indexed append writes the record's node"
            reach <- indexed.Reach
        | Error e -> failtestf "seed %d: appendIndexed: %A" seed e

    let step (parent: string) (stream: string) =
        let tree = treeAt parent
        let pane = $"p{rng.Next paneCount}"

        let op =
            if rng.Next 8 = 0 && Introspect.findNode (NodeId pane) tree |> Option.isSome then
                TreeOp.RemoveNode(NodeId pane)
            else
                TreeOp.PatchStyle(
                    NodeId pane,
                    { StyleDelta.empty with
                        Tone = Some tones[rng.Next tones.Length] }
                )

        match Apply.apply op tree with
        | Ok _ ->
            let r =
                DagOpRecord.create stream [ parent ] op None actor (timestamp (tick ())) OpResultEnvelope.Success

            appendIndexed r
            all.Add r.Hash
            Some r.Hash
        | Error _ -> None

    let merge (a: string) (b: string) =
        let checkpoint =
            if rng.Next 3 = 0 then
                let at = all[rng.Next all.Count]
                Some(DagFold.sealAt at (treeAt at))
            else
                None

        let context =
            { MergeContext.defaults with
                Checkpoint = checkpoint
                Precedence =
                    match rng.Next 3 with
                    | 0 -> Some ReconcileSide.A
                    | 1 -> Some ReconcileSide.B
                    | _ -> None }

        agreeOn $"seed {seed}" reach (Fuaran.Core.Dag.Reach.dag reach) a b
        let outcome, carried = mergeBoth context initial reach a b (timestamp (tick ()))
        reach <- carried
        outcome.Result

    for _ in 1..steps do
        let roll = rng.Next 100

        if roll < 45 || heads.Count < 2 then
            // Extend a head in place, or fork a new head off it.
            let i = rng.Next heads.Count

            match step heads[i] streamId with
            | Some h when roll < 30 -> heads[i] <- h
            | Some h -> heads.Add h
            | None -> ()
        elif roll < 55 then
            // A guest fork: a branch under another stream id, anchored on an
            // older node rather than a head.
            guests <- guests + 1
            let anchor = all[rng.Next all.Count]

            match step anchor (GuestFork.streamId $"g{guests}") with
            | Some h -> heads.Add h
            | None -> ()
        else
            let i = rng.Next heads.Count
            let j = (i + 1 + rng.Next(heads.Count - 1)) % heads.Count
            let a, b = heads[i], heads[j]

            match merge a b with
            | MergeResult.Merged(r, _) ->
                merges <- merges + 1
                all.Add r.Hash
                heads.Add r.Hash

                // Sometimes the swapped twin too: two merge nodes over one pair.
                if rng.Next 3 = 0 then
                    match merge b a with
                    | MergeResult.Merged(twin, _) ->
                        twins <- twins + 1
                        all.Add twin.Hash
                        heads.Add twin.Hash
                    | _ -> ()
            | MergeResult.FastForward h ->
                heads.Remove a |> ignore
                heads.Remove b |> ignore
                heads.Add h
            | _ -> ()

    let dag = Fuaran.Core.Dag.Reach.dag reach
    Expect.equal dag.Nodes.Count all.Count $"seed {seed}: the carried index holds every node written"

    // The carried index answers as a fresh one and as the unindexed walks.
    let fresh = Fuaran.Core.Dag.Reach.ofDag dag
    let sample = [ for _ in 1..25 -> all[rng.Next all.Count] ] |> List.distinct

    for a in sample do
        for b in sample do
            agreeOn $"seed {seed} carried" reach dag a b

            Expect.equal
                (Fuaran.Core.Dag.Reach.mergeBase reach a b)
                (Fuaran.Core.Dag.Reach.mergeBase fresh a b)
                $"seed {seed}: carried and fresh index agree on {a} {b}"

    all.Count, guests, merges, twins

let private populationTests =
    test "on generated DAGs with criss-cross and guest-fork shapes, indexed and unindexed answers agree" {
        let counts = [ for seed in 1..8 -> population seed 60 ]
        let total f = counts |> List.sumBy f

        // The population is not vacuous: it grew histories, forked guests, and
        // recorded merges, criss-cross twins among them.
        Expect.isGreaterThan (total (fun (n, _, _, _) -> n)) 250 "the generated histories hold hundreds of nodes"
        Expect.isGreaterThan (total (fun (_, g, _, _) -> g)) 5 "and guest forks"
        Expect.isGreaterThan (total (fun (_, _, m, _) -> m)) 10 "and merge nodes"
        Expect.isGreaterThan (total (fun (_, _, _, t) -> t)) 2 "and criss-cross twins"
    }

[<Tests>]
let tests =
    testList "Dag.Merge over Core's reachability index (Phase 2692)" [ fixtureTests; populationTests ]
