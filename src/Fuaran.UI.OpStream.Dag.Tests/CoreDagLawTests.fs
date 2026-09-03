module Fuaran.UI.OpStream.Dag.Tests.CoreDagLawTests

// ============================================================================
//  Phase 1476 — the Core conformance kit's MULTI-WRITER families, instantiated
//  over this tier's own op algebra, tree witness and footprint projection.
//
//  The tier's branching op-stream ships two ports and a real three-way merge,
//  and its confluence claims were demonstrated by example rather than
//  certified: `Conformance.dagLaws`, `FoldConfluence.laneFoldLaws`,
//  `Conformance.mergeConflictLaws`, `reconcileLaws`, `concurrencyLaws`,
//  `arbitrationLaws` and `footprintLaws` all ran only in Core's own suite, over
//  Core's own fixtures. Here they run over `Fuaran.UI`'s `Node` / `NodeId` /
//  `TreeOp` / canonical codec, so a defect in THIS tier — a reducer that is not
//  total, an encoder that is not injective, a footprint that understates what an
//  op touches — refutes a law rather than surfacing later as a divergence
//  between two replicas that both believe they agreed.
//
//  ---- what each family can and cannot see ---------------------------------
//
//  Two shapes, and the difference decides what a green verdict means.
//
//  `dagLaws` and `laneFoldLaws` are parameterised by a `StreamWitness` — this
//  tier's `Ops.Apply.apply` reducer and its canonical op codec — and run that
//  witness through CORE's `Dag` (`Dag.append` / `merge` / `verifyDag` /
//  `reconcileMany`). No `IDagOpStreamSink` enters their construction, and
//  neither does `Fuaran.UI.OpStream.Dag.Merge`. So they certify the tier's
//  reducer, codec and footprint under multi-writer folding; they say nothing
//  about the tier's own merge engine, which `MergeTests` / `MergeConformanceTests`
//  cover directly. The two-port question they CAN answer is the codec seam —
//  the Sqlite sink round-trips every op through a host `IOpJsonCodec` and the
//  in-memory sink's export path calls the same canonical pair — and the port
//  leg at the bottom of this file answers it against the real sinks.
//
//  The five skeleton-op families (`footprintLaws`, `mergeConflictLaws`,
//  `reconcileLaws`, `concurrencyLaws*`, `arbitrationLaws`) are parameterised by
//  a `NodeWitness` / `IdWitness` / `OpGen` and run over Core's skeleton-op
//  algebra against this tier's trees. They are port-independent by construction
//  — no persistence appears anywhere in them — so each is run ONCE. The one
//  place the tier's own code is genuinely the subject is
//  `concurrencyLawsWith`, which takes the footprint projection as a parameter:
//  it is instantiated with `CoreLawSupport.uiFootprintOfSkeleton`, the tier's
//  own `TreeOp` address-set function, so the confluence claim is made about the
//  function the lane fold actually relies on.
// ============================================================================

open System
open System.IO
open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.Tests.CoreLawSupport

module CoreConf = Fuaran.Core.Conformance
module FoldConfluence = Fuaran.Core.FoldConfluence
module UiApply = Fuaran.UI.Ops.Apply
module JsonDecode = Fuaran.UI.Ops.JsonDecode

/// One seed for the whole phase, so a refutation names a run anyone can reproduce.
let private seed = 20260904

// ---------------------------------------------------------------------------
//  the port leg — the codec seam both DAG sinks persist ops through
// ---------------------------------------------------------------------------

/// The canonical op codec a host supplies to the DAG sinks. `dagLaws`' JSONL round-trip law
/// runs over exactly this pair (as `StreamWitness.Encode` / `Decode`); here it is handed to a
/// real `SqliteDagSink`, so the same ops make the same trip through a database file.
let private canonicalCodec: IOpJsonCodec<obj> =
    { new IOpJsonCodec<obj> with
        member _.EncodeOp op = CanonicalJson.encodeOp op

        member _.DecodeOp json =
            JsonDecode.decodeOp json |> Result.mapError (sprintf "%A") }

/// A deterministic script of law-generated ops that all APPLY against the base tree — the
/// rejection cases `genStreamOp` mixes in are dropped here, because this leg is about
/// persistence, not reducer totality.
let private applyableScript (n: int) =
    let mutable rng = Fuaran.Core.ConfRng.ofSeed seed
    let mutable state = baseTree
    let mutable ops = []

    while List.length ops < n do
        let op, r = genStreamOp rng
        rng <- r

        match UiApply.apply op.Op state.Node with
        | Ok t ->
            state <- wrap t
            ops <- ops @ [ op.Op ]
        | Error _ -> ()

    ops, state

/// Replay a sink head along its PRIMARY-parent spine, exactly as `TestSupport.replaySpine`
/// does for the `TestMsg` suite (which cannot be reused: it is typed to that message).
let private replayFromSink (sink: IDagOpStreamSink<obj>) (streamId: string) (head: string) =
    let rec collect (hash: string) acc =
        match sink.TryGet(streamId, hash) |> Async.RunSynchronously with
        | None -> failtestf "replayFromSink: unknown hash %s" hash
        | Some r ->
            match r.Parents with
            | [] -> r :: acc
            | primary :: _ -> collect primary (r :: acc)

    collect head []
    |> List.fold
        (fun acc (r: DagOpRecord<obj>) ->
            acc
            |> Result.bind (fun t -> UiApply.apply r.Op t |> Result.mapError (sprintf "%A")))
        (Ok baseTree.Node)

/// Write `ops` into `sink` as a fork+merge DAG — genesis, two branches off it, and a merge
/// node whose primary parent is branch A — then replay the merge head back out of the sink.
let private roundTripThroughPort (sink: IDagOpStreamSink<obj>) (ops: TreeOp<obj> list) =
    let ts (i: int) =
        DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L + int64 i)

    let step (parents: string list) (i: int) (op: TreeOp<obj>) =
        let r =
            DagOpRecord.create "laws" parents op None (Actor.Human "law") (ts i) OpResultEnvelope.Success

        sink.Add r |> Async.RunSynchronously
        r

    match ops with
    | genesis :: rest when List.length rest >= 3 ->
        let g = step [] 0 genesis
        let a = step [ g.Hash ] 1 rest[0]
        let b = step [ g.Hash ] 2 rest[1]
        let m = step [ a.Hash; b.Hash ] 3 rest[2]
        // Every read path re-verifies (LoadVerification.Full is the default), so a record that
        // no longer hashes to its address is refused here rather than replayed.
        sink.Records "laws" |> Async.RunSynchronously |> ignore
        replayFromSink sink "laws" m.Hash
    | _ -> failtest "roundTripThroughPort needs at least four applyable ops"

// ---------------------------------------------------------------------------
//  the law lists
// ---------------------------------------------------------------------------

[<Tests>]
let tests =
    testList
        "Core multi-writer laws (Fuaran.UI DAG)"
        [

          // ---- the op-DAG family ------------------------------------------
          testCase "the UI op-stream witness certifies under Core's dagLaws"
          <| fun _ ->
              // verifyDag accepts an intact DAG; replayTo is deterministic under a permuted
              // construction order; a tampered node is detected; the JSONL round-trip preserves
              // the DAG. Run over the tier's SHIPPED SHA-256 rather than the kit's FNV-1a
              // default — that is the hash the op-stream's chain actually uses.
              CoreConf.dagLaws coreSw streamGen uiHashFn seed 100
              |> assertAllPassed "dagLaws over the Fuaran.UI op-stream witness"

          // ---- the fold-confluence family ---------------------------------
          testCase "N-lane folding is arrival-order-invariant under Core's laneFoldLaws"
          <| fun _ ->
              // Three lanes off one base, folded under all 3! = 6 arrival orders per trial.
              // The lane generator reaches both adequacy classes the pack demands (a lane set
              // that folds AND one that halts); a run that reached only one fails its coverage
              // guard rather than reporting a hollow green.
              FoldConfluence.laneFoldLaws coreSw footprintOfEqOp hashState laneGen 3 seed 100
              |> assertAllPassed "laneFoldLaws over the Fuaran.UI lane generator"

          testCase "lane folding survives the host hash swap under laneFoldLawsWith"
          <| fun _ ->
              // The `With` form's parameter is the `HashFn`: the defaulted family above pins
              // the kit's FNV-1a, this one supplies the tier's shipped SHA-256. Node ids are
              // content hashes of (parents, actor, op), so the hash is what decides whether two
              // lanes carrying the same ops stay distinct chains — not a formality.
              FoldConfluence.laneFoldLawsWith coreSw footprintOfEqOp uiHashFn hashState laneGen 3 seed 60
              |> assertAllPassed "laneFoldLawsWith over the tier's SHA-256"

          // ---- the skeleton-op families -----------------------------------
          testCase "the tier's tree footprints are sound, monotone and deterministic (footprintLaws)"
          <| fun _ ->
              // Reassigned here from Phase 1479 by driver direction: this is a TREE-op law over
              // exactly the NodeWitness / IdWitness / OpGen built above, where 1479's subject is
              // the incremental (DataFrame) footprint. Soundness only fires on an INDEPENDENT
              // pair, so the adequacy guard is what keeps a green verdict from meaning "the
              // generator never produced one".
              CoreConf.footprintLaws nodew idw opGen encodeNode seed 100
              |> assertAllPassed "footprintLaws over the Fuaran.UI tree witness"

          testCase "merge-conflict reporting is symmetric, deterministic and complete (mergeConflictLaws)"
          <| fun _ ->
              CoreConf.mergeConflictLaws nodew idw opGen seed 100
              |> assertAllPassed "mergeConflictLaws over the Fuaran.UI tree witness"

          testCase "two-branch reconciliation is order-pinned and conflict-honest (reconcileLaws)"
          <| fun _ ->
              CoreConf.reconcileLaws nodew idw opGen encodeNode seed 100
              |> assertAllPassed "reconcileLaws over the Fuaran.UI tree witness"

          testCase "independent op pairs interleave confluently (concurrencyLaws)"
          <| fun _ ->
              // The defaulted form, pinned to Core's own `Ops.footprint`.
              CoreConf.concurrencyLaws nodew idw opGen encodeNode seed 100
              |> assertAllPassed "concurrencyLaws over the Fuaran.UI tree witness"

          testCase "the TIER's own footprint projection is confluent (concurrencyLawsWith)"
          <| fun _ ->
              // The `With` form's parameter is the footprint projection, so this run routes the
              // law through `uiFootprintOfSkeleton` — the tier's own `TreeOp` address-set
              // function, the same one `laneFoldLaws` folds through — rather than Core's.
              //
              // Stated precisely, because the difference matters: the law's generator emits
              // SKELETON ops, so what this certifies is the STRUCTURAL half of the tier's
              // projection (the five ops the runtime apply already delegates to Core), and a
              // change to that half's mapping would refute here. The VERTICAL half
              // (`UpdateStyle` / `UpdateProp` / `EditNode` / `UpdateState` / `ReplaceBinding`,
              // which Core's algebra has no case for) is unreachable from this generator and is
              // certified by `laneFoldLaws` above, which folds real `TreeOp` lanes — understating
              // a vertical op's footprint turns that family red, and only that family.
              CoreConf.concurrencyLawsWith uiFootprintOfSkeleton nodew idw opGen encodeNode seed 100
              |> assertAllPassed "concurrencyLawsWith over the tier's own footprint projection"

          testCase "proposal arbitration partitions totally and confluently (arbitrationLaws)"
          <| fun _ ->
              CoreConf.arbitrationLaws nodew idw opGen encodeNode seed 100
              |> assertAllPassed "arbitrationLaws over the Fuaran.UI tree witness"

          // ---- the port leg -----------------------------------------------
          testCase "the law-generated ops survive both DAG ports and replay identically"
          <| fun _ ->
              // What the law families structurally cannot reach: the ops they generate, written
              // into each real `IDagOpStreamSink` as a fork+merge DAG and read back. The
              // in-memory sink holds the op; the Sqlite sink round-trips it through the host
              // `IOpJsonCodec` into a database file — the same encode/decode pair `dagLaws`
              // certifies above, now crossing a process-durable boundary.
              let ops, _ = applyableScript 4

              // A merge node's op is the replay delta from its PRIMARY parent, so the spine is
              // genesis → branch A → merge; branch B's op is reachable but not on it. The
              // reference fold therefore skips B, which is what the sinks must reproduce.
              let referenceSpine =
                  match ops with
                  | g :: a :: _ :: m :: _ ->
                      [ g; a; m ]
                      |> List.fold
                          (fun acc op ->
                              acc
                              |> Result.bind (fun t -> UiApply.apply op t |> Result.mapError (sprintf "%A")))
                          (Ok baseTree.Node)
                  | _ -> failtest "expected at least four applyable ops"

              let inMemory = Fuaran.UI.OpStream.Dag.InMemory.InMemoryDagSink.create<obj> ()

              let fromMemory = roundTripThroughPort inMemory ops

              let dbPath =
                  Path.Combine(Path.GetTempPath(), sprintf "fuaran-dag-laws-%s.db" (Path.GetRandomFileName()))

              let fromSqlite =
                  try
                      let sqlite =
                          Fuaran.UI.OpStream.Dag.Sqlite.SqliteDagSink.create<obj>
                              (sprintf "Data Source=%s" dbPath)
                              canonicalCodec

                      roundTripThroughPort sqlite ops
                  finally
                      try
                          if File.Exists dbPath then
                              File.Delete dbPath
                      with _ ->
                          ()

              match referenceSpine, fromMemory, fromSqlite with
              | Ok reference, Ok mem, Ok sql ->
                  Expect.equal
                      (CanonicalJson.encodeNode mem)
                      (CanonicalJson.encodeNode reference)
                      "InMemoryDagSink replays the fork+merge spine to the in-process fold"

                  Expect.equal
                      (CanonicalJson.encodeNode sql)
                      (CanonicalJson.encodeNode reference)
                      "SqliteDagSink replays the same spine through the host op codec on disk"
              | r, m, s -> failtestf "a port leg failed to replay: reference=%A memory=%A sqlite=%A" r m s ]
