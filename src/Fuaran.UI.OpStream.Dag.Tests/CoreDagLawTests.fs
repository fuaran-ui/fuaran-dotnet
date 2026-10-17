module Fuaran.UI.OpStream.Dag.Tests.CoreDagLawTests

// ============================================================================
//  Phase 1476 — the Core conformance kit's MULTI-WRITER families, instantiated
//  over this tier's own op algebra, tree witness and footprint projection.
//
//  The tier's branching op-stream ships two ports and a real three-way merge,
//  and its confluence claims were demonstrated by example rather than
//  certified: `Conformance.dagLaws`, `FoldConfluence.laneFoldLawsAt`,
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
//  `dagLaws` and `laneFoldLawsAt` are parameterised by a `StreamWitness` — this
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
/// Phase 1587 — this pair was written by hand here and again in the linear
/// persistence suite before it existed anywhere a CONSUMER could reach it. It
/// is `OpJsonCodec.canonicalObj` now: the shipped reference codec at the erased
/// `'Msg`, so this leg exercises what a host actually gets rather than a
/// test-local reconstruction of it.
let private canonicalCodec: IOpJsonCodec<obj> = OpJsonCodec.canonicalObj ()

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

/// Replay a sink head through the production fold, `DagReplay.replay` (Phase 2043 retired the
/// suite's own copy of the spine walk), with the error rendered for the law's message.
let private replayFromSink (sink: IDagOpStreamSink<obj>) (streamId: string) (head: string) =
    Fuaran.UI.OpStream.Dag.Merge.DagReplay.replay
        (fun hash -> sink.TryGet(streamId, hash) |> Async.RunSynchronously)
        baseTree.Node
        head
    |> Result.mapError (sprintf "%A")

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
//  Phase 2004 — a vertical edit beside a concurrent removal of its ancestor
// ---------------------------------------------------------------------------

/// `root → g → p → [n; m]`, with `s` beside `g`: the edited node `n` has a parent and a
/// grandparent, and both can be removed.
let private ancestryTree =
    mkBox "root" [ mkBox "g" [ mkBox "p" [ mkMetric "n"; mkMetric "m" ] ]; mkMetric "s" ]

/// Every pair the phase names: each of the five vertical ops on `n`, beside a `RemoveNode` of
/// `n`'s parent and of its grandparent.
let private ancestorPairs =
    [ for name, edit in verticalOps (NodeId "n") do
          for ancestor in [ "p"; "g" ] do
              sprintf "%s(n) || RemoveNode(%s)" name ancestor, edit, TreeOp.RemoveNode(NodeId ancestor) ]

/// `first` then `second` from the ancestry tree, through the tier's own reducer.
let private applyInOrder (first: TreeOp<obj>) (second: TreeOp<obj>) =
    UiApply.apply first ancestryTree.Node
    |> Result.bind (UiApply.apply second)
    |> Result.map CanonicalJson.encodeNode

/// Both orders apply, and reach one state.
let private commutes (a: TreeOp<obj>) (b: TreeOp<obj>) =
    match applyInOrder a b, applyInOrder b a with
    | Ok x, Ok y -> x = y
    | _ -> false

/// The pairs `fp` declares INDEPENDENT although their two orders do not reach one state — exactly
/// what an understated footprint lets a reconciler compose.
let private unsoundUnder (fp: TreeOp<obj> -> Fuaran.Core.Footprint) =
    ancestorPairs
    |> List.filter (fun (_, edit, removal) ->
        Fuaran.Core.Ops.independent (fp edit) (fp removal)
        && not (commutes edit removal))
    |> List.map (fun (name, _, _) -> name)

/// The two arrival orders of a two-lane fold, one lane per op, through Core's reconciler and the
/// tier's footprint projection.
let private foldBothOrders (a: TreeOp<obj>) (b: TreeOp<obj>) =
    let fold lanes =
        FoldConfluence.foldOnce
            coreSw
            footprintOfEqOp
            uiHashFn
            hashState
            ancestryTree
            (wrapOp (TreeOp.Batch []))
            (lanes |> List.map (List.map wrapOp))

    fold [ [ a ]; [ b ] ], fold [ [ b ]; [ a ] ]

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
          testCase "N-lane folding is arrival-order-invariant under Core's laneFoldLawsAt"
          <| fun _ ->
              // Three lanes off one base (`laneBase`, deep enough that a lane can remove an ancestor
              // of a node another lane edits — Phase 2004), folded under all 3! = 6 arrival orders
              // per trial.
              // The lane generator reaches both adequacy classes the pack demands (a lane set
              // that folds AND one that halts); a run that reached only one fails its coverage
              // guard rather than reporting a hollow green.
              FoldConfluence.laneFoldLawsAt coreSw footprintOfEqOp hashState laneGen 3 seed 100
              |> assertAllPassed "laneFoldLawsAt over the Fuaran.UI lane generator"

          testCase "lane folding survives the host hash swap under laneFoldLawsWith"
          <| fun _ ->
              // The `With` form's parameter is the `HashFn`: the defaulted family above pins
              // the kit's FNV-1a, this one supplies the tier's shipped SHA-256. Node ids are
              // content hashes of (parents, actor, op), so the hash is what decides whether two
              // lanes carrying the same ops stay distinct chains — not a formality.
              FoldConfluence.laneFoldLawsWith coreSw footprintOfEqOp hashState laneGen 3 uiHashFn seed 60
              |> assertAllPassed "laneFoldLawsWith over the tier's SHA-256"

          testCase
              "the lane generator sees an ancestor removal beside a vertical edit (an understated footprint is red)"
          <| fun _ ->
              // Phase 2004's teeth for the family above. `laneBase` gives every edited leaf a
              // removable ancestor, so a lane removing it lands beside a lane editing the leaf.
              // Over the projection with the vertical arm's unknown-parent write erased, the fold
              // composes that pair, the composed script applies in one arrival order and is refused
              // in the other, and the family must say so. Before the generator was widened its base
              // was a root over three leaves, and this same erased projection passed.
              let failures =
                  FoldConfluence.laneFoldLawsAt
                      coreSw
                      (fun e -> footprintErasingVerticalTerm e.Op)
                      hashState
                      laneGen
                      3
                      seed
                      100
                  |> List.filter (fun r -> not r.Passed)

              Expect.isNonEmpty failures "an understated vertical footprint refutes the lane-fold family"

              Expect.isTrue
                  (failures
                   |> List.exists (fun r -> (sprintf "%A" r.Counterexample).Contains "RemoveNode"))
                  "the refutation's shrunk lanes name the ancestor removal"

          // ---- Phase 2004: vertical edits beside ancestor removals ---------
          testCase "a vertical edit and a concurrent removal of its ancestor are ordered, never composed"
          <| fun _ ->
              // Core's Phase 250 found this pair unsound for its own `UpdateNode`: removing an
              // ancestor takes the edited node with it, so the edit lands in one order and is
              // refused in the other, while the two footprints share no address. Probed in BOTH
              // directions first, so the pin is about a real non-commuting pair.
              for name, edit, removal in ancestorPairs do
                  Expect.isOk (applyInOrder edit removal) (sprintf "%s: the edit then the removal applies" name)

                  Expect.isError
                      (applyInOrder removal edit)
                      (sprintf "%s: the removal then the edit is refused — the edited node is gone" name)

              // The footprints must therefore call every pair dependent.
              Expect.isEmpty
                  (unsoundUnder footprintOfTreeOp)
                  "the tier's projection declares no non-commuting ancestor pair independent"

              // And dependence is what the reconciler acts on: the lane fold HALTS on each pair,
              // with one canonical report under both arrival orders, instead of composing a script
              // that applies in one order only.
              for name, edit, removal in ancestorPairs do
                  match foldBothOrders edit removal with
                  | (Fuaran.Core.LaneHalted r1), (Fuaran.Core.LaneHalted r2) ->
                      Expect.equal r1 r2 (sprintf "%s: one report under both arrival orders" name)
                  | o1, o2 -> failtestf "%s: expected the fold to halt under both orders, got %A / %A" name o1 o2

              // The erased-term falsifier: without the vertical arm's unknown-parent write every
              // one of the pairs reads as independent, which is the defect the term closes.
              Expect.equal
                  (unsoundUnder footprintErasingVerticalTerm)
                  (ancestorPairs |> List.map (fun (name, _, _) -> name))
                  "erasing the unknown-parent term frees every ancestor pair"

          testCase "the merge attributes a pinned vertical edit beside its ancestor's removal to no removed node"
          <| fun _ ->
              // The same pair through the tier's three-way merge and its per-cell attribution
              // (`DagPrimacy`). Branch A restyles `n` as a PRIMARY writer, then a secondary op
              // restyles `s`; branch B removes `n`'s parent `p`. The merge must refuse at the
              // removed ancestor, as one delete/modify, and must name no cell of `n` — a pin
              // attributed to a facet of a node the merged tree no longer holds would name a
              // writer that does not exist.
              let ts (i: int) =
                  DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L + int64 i)

              let record (parents: string list) (i: int) (actor: string) (op: TreeOp<obj>) =
                  DagOpRecord.create "attr" parents op None (Actor.Human actor) (ts i) OpResultEnvelope.Success

              let styled (id: string) (tone: ToneVariant) =
                  TreeOp.UpdateStyle(
                      NodeId id,
                      { Fuaran.UI.Defaults.style with
                          Tone = tone }
                  )

              let g = record [] 0 "base" (TreeOp.Batch [])
              let a1 = record [ g.Hash ] 1 "primary" (styled "n" ToneVariant.Success)
              let a2 = record [ a1.Hash ] 2 "secondary" (styled "s" ToneVariant.Critical)
              let b1 = record [ g.Hash ] 3 "secondary" (TreeOp.RemoveNode(NodeId "p"))

              let byHash = [ g; a1; a2; b1 ] |> List.map (fun r -> r.Hash, r) |> Map.ofList
              let getRec h = Map.tryFind h byHash

              let recordAuthor (r: DagOpRecord<obj>) =
                  match r.Actor with
                  | Actor.Human "primary" -> Fuaran.UI.OpStream.Dag.Merge.MergeAuthor.Primary
                  | _ -> Fuaran.UI.OpStream.Dag.Merge.MergeAuthor.Secondary None

              let tree (ops: TreeOp<obj> list) =
                  ops
                  |> List.fold (fun acc op -> acc |> Result.bind (UiApply.apply op)) (Ok ancestryTree.Node)
                  |> Result.defaultWith (failwithf "a branch failed to apply: %A")

              let authors (head: DagOpRecord<obj>) =
                  Fuaran.UI.OpStream.Dag.Merge.DagPrimacy.cellAuthorFn
                      recordAuthor
                      getRec
                      (Some g.Hash)
                      head.Hash
                      (recordAuthor head)
                  |> Result.defaultWith (failwithf "the attribution walk failed: %A")

              let authorsA, authorsB = authors a2, authors b1

              match
                  Fuaran.UI.OpStream.Dag.Merge.TreeMerge.merge3WayWithCellAuthor
                      (fun id facet -> authorsA id facet, authorsB id facet)
                      ancestryTree.Node
                      (tree [ a1.Op; a2.Op ])
                      (tree [ b1.Op ])
              with
              | Ok merged -> failtestf "the merge composed the pair: %s" (CanonicalJson.encodeNode merged)
              | Error conflicts ->
                  Expect.equal
                      (conflicts |> List.map (fun c -> c.NodeId, c.Facet, c.Class))
                      [ "p", "node", Fuaran.UI.OpStream.Dag.Merge.MergeConflictClass.DeleteModify ]
                      "one delete/modify, at the removed ancestor — no cell of the removed descendant"

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

          testCase "the SHIPPED footprint projection is sound at the tier's own ops (footprintLawsAt)"
          <| fun _ ->
              // Phase 2292. `footprintLaws` above is Core's skeleton family and takes no projection;
              // this is its domain-witness form, run over `TreeOpFootprint.ofOp` (through
              // `footprintOfEqOp`), the tier's reducer and a generator that draws every op kind the
              // projection has an arm for. An independent pair must commute under `Apply.apply`,
              // and the adequacy guard demands both an independent and an interfering pair. The
              // projection declares no slot access, so the kit's slot arms are vacuous by
              // declaration, and say so.
              CoreConf.footprintLawsAt coreSw footprintOfEqOp hashState footprintGen seed 300
              |> assertAllPassed "footprintLawsAt over the shipped TreeOp footprint"

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
              // The defaulted form, pinned to Core's own `Ops.footprint`. 300 iterations, Core's own
              // suite's figure for this family: from Core 0.33.0 the kit refuses a run that reached no
              // independent pair (sample adequacy), and under `Ops.footprint` — where a remove, a move
              // and an in-place update each write an unknown parent — a pair of four-op scripts is
              // independent only rarely, so the run is sized to reach enough of them.
              CoreConf.concurrencyLaws nodew idw opGen encodeNode seed 300
              |> assertAllPassed "concurrencyLaws over the Fuaran.UI tree witness"

          testCase "the TIER's own footprint projection is confluent (concurrencyLawsWith)"
          <| fun _ ->
              // The `With` form's parameter is the footprint projection, so this run routes the
              // law through `uiFootprintOfSkeleton` — the tier's own `TreeOp` address-set
              // function, the same one `laneFoldLawsAt` folds through — rather than Core's.
              //
              // Stated precisely, because the difference matters: the law's generator emits
              // SKELETON ops, so what this certifies is the STRUCTURAL half of the tier's
              // projection (the five ops the runtime apply already delegates to Core), and a
              // change to that half's mapping would refute here. The VERTICAL half
              // (`UpdateStyle` / `UpdateProp` / `EditNode` / `UpdateState` / `ReplaceBinding`,
              // which Core's algebra has no case for) is unreachable from this generator and is
              // certified by `laneFoldLawsAt` above, which folds real `TreeOp` lanes — understating
              // a vertical op's footprint turns that family red, and only that family.
              CoreConf.concurrencyLawsWith nodew idw opGen encodeNode uiFootprintOfSkeleton seed 300
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
