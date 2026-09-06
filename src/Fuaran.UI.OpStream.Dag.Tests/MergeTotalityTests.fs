module Fuaran.UI.OpStream.Dag.Tests.MergeTotalityTests

open Expecto
open FSharp.Reflection
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.Introspect
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.InMemory
open Fuaran.UI.OpStream.Dag.Merge
open Fuaran.UI.OpStream.Dag.Tests.TestSupport

// ============================================================================
//  Phase 1526 — three-way merge TOTALITY.
//
//  Four claims the merge layer made and did not keep, each asserted here in the
//  form that was FALSE before this phase:
//
//   1. `DeleteModify` and `ConcurrentMove` are declared conflict classes
//      (`MergeConflict.fs`) with a documented mapping onto `ApplyErrorCode`.
//      Nothing in `src/` constructed either, so a node one side edited and the
//      other removed or moved away vanished with the edit and no envelope —
//      the merge returned `Ok`. Every refusal test below has a TWIN that must
//      still auto-merge, because a floor that refuses everything passes the
//      first half of that pair and fails the job.
//
//   2. A merge node commits to the merged tree by `OutcomeHash` and carries a
//      replay delta that is supposed to reach it. `TreeOp` is not total over
//      the node record — no op sets `Accessibility` or `Tooltip`, both of which
//      `TreeMerge` merges as facets — so a merge of those facets minted a node
//      whose delta reached a DIFFERENT tree, and mint, commit, replay and
//      verify all accepted it.
//
//   3. `DagPrimacy.cellsOf` decides which branch's author owns a contended
//      cell. A facet `TreeMerge` names but `cellsOf` does not attribute falls
//      back to the branch TIP, which is the exact mislabelling the per-cell
//      walk exists to prevent — reintroduced one facet at a time as the merge
//      grew (`style.direction` arrived in Phase 1472 and was never added).
//
//  The roster test at the end is the one that keeps 3 from happening again: it
//  derives BOTH sets rather than asserting a hand-written list, so a facet added
//  to either side alone fails it.
// ============================================================================

let private now = ts 9_000L

// ── fixtures ────────────────────────────────────────────────────────────────

let private movableId = NodeId "m"
let private boxAId = NodeId "boxa"
let private boxBId = NodeId "boxb"
let private boxCId = NodeId "boxc"

let private container (id: string) (children: Node<TestMsg> list) : Node<TestMsg> =
    Fuaran.dashboard
        id
        { Defaults.dashboard<TestMsg> with
            Children = children }

/// `dash → [ boxa → [ m ], boxb, boxc ]` — the shape a MOVE needs. The flat
/// two-pane genesis in `TestSupport` cannot express one: a node has to have
/// somewhere else to go.
let private buildNested () : Node<TestMsg> =
    container
        "dash"
        [ container "boxa" [ Fuaran.markdown "m" "Movable" ]
          container "boxb" []
          container "boxc" [] ]

/// Rewrite one node in place by id, leaving the rest of the tree alone. The
/// `Accessibility` and `Tooltip` facets have no `TreeOp` that sets them (which
/// is claim 2 above), so a fixture that contends either has to build its trees
/// directly rather than by applying ops.
let private mapNodeById (targetId: string) (f: Node<TestMsg> -> Node<TestMsg>) (root: Node<TestMsg>) : Node<TestMsg> =
    let rec go (n: Node<TestMsg>) : Node<TestMsg> =
        let self = if n.Id = targetId then f n else n

        match getChildren self.Kind with
        | None -> self
        | Some kids ->
            match withChildren self.Kind (kids |> List.map go) with
            | Some k -> { self with Kind = k }
            | None -> self

    go root

let private facetsOf (conflicts: MergeConflict list) : Set<string> =
    conflicts |> List.map _.Facet |> Set.ofList

/// Merge, requiring a REFUSAL. Returns the envelope.
let private refuses (baseT: Node<TestMsg>) (a: Node<TestMsg>) (b: Node<TestMsg>) : MergeConflict list =
    match TreeMerge.merge3Way baseT a b with
    | Error conflicts -> conflicts
    | Ok merged -> failtestf "expected a refusal, got a merge: %s" (canonical merged)

/// Merge, requiring AUTO-MERGE. Returns the merged tree.
let private merges (baseT: Node<TestMsg>) (a: Node<TestMsg>) (b: Node<TestMsg>) : Node<TestMsg> =
    match TreeMerge.merge3Way baseT a b with
    | Ok merged -> merged
    | Error conflicts ->
        failtestf "expected an auto-merge, got a refusal: %A" (conflicts |> List.map MergeConflict.cell)

[<Tests>]
let tests =
    testList
        "Dag.MergeTotality"
        [
          // ── DeleteModify ────────────────────────────────────────────────

          test "one side edits a node the other removes — DeleteModify, not a silent drop" {
              let baseT = buildDashboard ()

              let edited =
                  baseT
                  |> applyOk (
                      TreeOp.UpdateStyle(
                          rightChildId,
                          { Defaults.style with
                              Tone = ToneVariant.Success }
                      )
                  )

              let removed = baseT |> applyOk (TreeOp.RemoveNode rightChildId)

              let conflicts = refuses baseT edited removed
              Expect.equal (facetsOf conflicts) (Set.ofList [ "node" ]) "the whole node is the contended cell"

              let c = List.exactlyOne conflicts
              Expect.equal c.NodeId "right" "the refusal names the removed node"
              Expect.equal c.Class MergeConflictClass.DeleteModify "classed DeleteModify"

              // Phase 179 declared this mapping and nothing reached it, so the
              // assertion is that a RAISED conflict projects onto it — not that
              // the projection function returns what it is written to return.
              Expect.equal
                  (MergeConflictClass.toApplyErrorCode c.Class)
                  ApplyErrorCode.NodeNotFound
                  "the 179 mapping onto NodeNotFound is exercised by a real refusal"

              // The removing side holds no value for the cell; the editing side's
              // subtree is what a resolver keeps.
              let editedRight =
                  getChildren edited.Kind
                  |> Option.defaultValue []
                  |> List.find (fun n -> n.Id = "right")

              Expect.equal
                  (c.A |> Option.map _.Value)
                  (Some(canonical editedRight))
                  "the edited side carries the edited subtree"

              Expect.equal (c.B |> Option.map _.Value) (Some "") "the removing side carries no value"
          }

          test "the corrected twin — the removed node was NOT edited — still auto-merges" {
              let baseT = buildDashboard ()

              let editedElsewhere =
                  baseT
                  |> applyOk (
                      TreeOp.UpdateStyle(
                          leftChildId,
                          { Defaults.style with
                              Tone = ToneVariant.Success }
                      )
                  )

              let removed = baseT |> applyOk (TreeOp.RemoveNode rightChildId)

              let merged = merges baseT editedElsewhere removed
              let ids = getChildren merged.Kind |> Option.defaultValue [] |> List.map _.Id
              Expect.equal ids [ "left" ] "the removal stands and the unrelated edit survives"
          }

          test "the refusal transposes when the branches are swapped" {
              let baseT = buildDashboard ()

              let edited =
                  baseT
                  |> applyOk (
                      TreeOp.UpdateStyle(
                          rightChildId,
                          { Defaults.style with
                              Tone = ToneVariant.Success }
                      )
                  )

              let removed = baseT |> applyOk (TreeOp.RemoveNode rightChildId)

              let forward = List.exactlyOne (refuses baseT edited removed)
              let swapped = List.exactlyOne (refuses baseT removed edited)

              Expect.equal
                  (forward.NodeId, forward.Facet, forward.Class)
                  (swapped.NodeId, swapped.Facet, swapped.Class)
                  "same cell"

              Expect.equal forward.A swapped.B "forward A == swapped B"
              Expect.equal forward.B swapped.A "forward B == swapped A"
          }

          // ── ConcurrentMove ──────────────────────────────────────────────

          test "one side moves a node the other edits — ConcurrentMove, with both positions and both cells" {
              let baseT = buildNested ()
              let moved = baseT |> applyOk (TreeOp.MoveNode(movableId, boxBId))

              let edited =
                  baseT
                  |> applyOk (
                      TreeOp.UpdateStyle(
                          movableId,
                          { Defaults.style with
                              Tone = ToneVariant.Brand }
                      )
                  )

              let conflicts = refuses baseT moved edited
              Expect.equal (facetsOf conflicts) (Set.ofList [ "move"; "node" ]) "position AND content are both named"

              for c in conflicts do
                  Expect.equal c.NodeId "m" "the refusal names the moved node"
                  Expect.equal c.Class MergeConflictClass.ConcurrentMove "classed ConcurrentMove"

              let move = conflicts |> List.find (fun c -> c.Facet = "move")
              Expect.equal move.Base "boxa" "the base position"
              Expect.equal (move.A |> Option.map _.Value) (Some "boxb") "the mover's destination"
              Expect.equal (move.B |> Option.map _.Value) (Some "boxa") "the editor left it where it was"

              let node = conflicts |> List.find (fun c -> c.Facet = "node")
              Expect.notEqual (node.A |> Option.map _.Value) (node.B |> Option.map _.Value) "the two subtrees differ"
          }

          test "the corrected twin — a one-sided move with no competing edit — still auto-merges" {
              let baseT = buildNested ()
              let moved = baseT |> applyOk (TreeOp.MoveNode(movableId, boxBId))

              let elsewhere =
                  baseT
                  |> applyOk (
                      TreeOp.UpdateStyle(
                          boxCId,
                          { Defaults.style with
                              Tone = ToneVariant.Brand }
                      )
                  )

              let merged = merges baseT moved elsewhere

              let childIdsOf (id: string) =
                  let rec find (n: Node<TestMsg>) =
                      if n.Id = id then
                          Some n
                      else
                          getChildren n.Kind |> Option.defaultValue [] |> List.tryPick find

                  find merged
                  |> Option.map (fun n -> getChildren n.Kind |> Option.defaultValue [] |> List.map _.Id)

              Expect.equal (childIdsOf "boxa") (Some []) "the move emptied the source"
              Expect.equal (childIdsOf "boxb") (Some [ "m" ]) "…and filled the destination"
          }

          test "both sides move the same node to different parents — ONE refusal, not one per destination" {
              let baseT = buildNested ()
              let toB = baseT |> applyOk (TreeOp.MoveNode(movableId, boxBId))
              let toC = baseT |> applyOk (TreeOp.MoveNode(movableId, boxCId))

              let conflicts = refuses baseT toB toC

              // The fold reaches this node from BOTH destination parents. The two
              // visits compute the same values from the same whole-tree indexes,
              // so recording both would put two entries under one `(NodeId,
              // Facet)` key — which `MergeConflict.sortCanonical` documents as
              // unique within a merge.
              Expect.equal (List.length conflicts) 2 "one `move` entry and one `node` entry, not two of each"
              Expect.equal (facetsOf conflicts) (Set.ofList [ "move"; "node" ]) "position AND content"

              let move = conflicts |> List.find (fun c -> c.Facet = "move")
              Expect.equal (move.A |> Option.map _.Value) (Some "boxb") "A's destination"
              Expect.equal (move.B |> Option.map _.Value) (Some "boxc") "B's destination"
          }

          // ── the merge node's delta reproduces its OutcomeHash ───────────

          test "a merge that carries an accessibility facet is REFUSED at mint, not minted lossily" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let initial = buildDashboard ()

              let seed =
                  stepRecord
                      "s"
                      None
                      (TreeOp.UpdateStyle(
                          leftChildId,
                          { Defaults.style with
                              Tone = ToneVariant.Brand }
                      ))
                      1L

              add sink seed
              let baseTree = initial |> applyOk seed.Op

              let branchA =
                  stepRecord
                      "s"
                      (Some seed)
                      (TreeOp.UpdateStyle(
                          leftChildId,
                          { Defaults.style with
                              Tone = ToneVariant.Success }
                      ))
                      2L

              // `ReplaceRoot` is the ONLY op that can set `Accessibility` at all
              // — which is the gap, seen from the authoring side.
              let accessible =
                  baseTree
                  |> mapNodeById "right" (fun n ->
                      { n with
                          Accessibility =
                              Some
                                  { Defaults.Accessibility.empty with
                                      DescribedBy = Some "help-1" } })

              let branchB = stepRecord "s" (Some seed) (TreeOp.ReplaceRoot accessible) 3L
              add sink branchA
              add sink branchB

              match
                  DagMerge.merge recordAuthor sink "s" initial branchA.Hash branchB.Hash now
                  |> Async.RunSynchronously
              with
              | MergeResult.DeltaNotReplayable(tree, mismatch) ->
                  // The MERGE is right — the refusal is about what the DAG can
                  // honestly record, so the merged tree is handed back.
                  Expect.equal
                      (canonical tree |> HashChain.sha256Hex)
                      mismatch.OutcomeHash
                      "the mismatch names the hash of the tree the merge produced"

                  Expect.notEqual
                      mismatch.ReplayedHash
                      (Some mismatch.OutcomeHash)
                      "…and the delta reaches a different tree"

                  Expect.isNone mismatch.ApplyError "the delta applied cleanly; it simply arrived somewhere else"
              | other -> failtestf "expected DeltaNotReplayable, got %A" other
          }

          test "an ordinary merge still mints, and its delta reproduces its outcome hash" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let initial = buildDashboard ()

              let seed =
                  stepRecord
                      "s"
                      None
                      (TreeOp.UpdateStyle(
                          leftChildId,
                          { Defaults.style with
                              Tone = ToneVariant.Brand }
                      ))
                      1L

              add sink seed

              let branchA =
                  stepRecord
                      "s"
                      (Some seed)
                      (TreeOp.UpdateStyle(
                          leftChildId,
                          { Defaults.style with
                              Tone = ToneVariant.Success }
                      ))
                      2L

              let branchB =
                  stepRecord
                      "s"
                      (Some seed)
                      (TreeOp.UpdateStyle(
                          rightChildId,
                          { Defaults.style with
                              Tone = ToneVariant.Critical }
                      ))
                      3L

              add sink branchA
              add sink branchB

              match
                  DagMerge.merge recordAuthor sink "s" initial branchA.Hash branchB.Hash now
                  |> Async.RunSynchronously
              with
              | MergeResult.Merged(record, tree) ->
                  Expect.equal
                      record.OutcomeHash
                      (Some(canonical tree |> HashChain.sha256Hex))
                      "the node commits to the merged tree"

                  // The whole point of the mint check: replaying the spine must
                  // reach the same tree. `replaySpine` is the suite's own fold,
                  // independent of `DagReplay`'s.
                  add sink record

                  Expect.equal
                      (canonical (replaySpine sink "s" initial record.Hash))
                      (canonical tree)
                      "the spine replay reproduces it"
              | other -> failtestf "expected Merged, got %A" other
          }

          test "DagReplay reports a merge node whose delta does not reproduce its outcome hash" {
              // A node of exactly the shape the mint now refuses — written by an
              // older engine, another host, or by hand. Detection on READ is the
              // half a write-time refusal cannot cover: the stream outlives the
              // process that wrote it.
              let initial = buildDashboard ()

              let genesis =
                  stepRecord
                      "s"
                      None
                      (TreeOp.UpdateStyle(
                          leftChildId,
                          { Defaults.style with
                              Tone = ToneVariant.Brand }
                      ))
                      1L

              let sideline =
                  stepRecord
                      "s"
                      (Some genesis)
                      (TreeOp.UpdateStyle(
                          rightChildId,
                          { Defaults.style with
                              Tone = ToneVariant.Critical }
                      ))
                      2L

              let claimed = String.replicate 64 "0"

              let bogus =
                  DagOpRecord.createMerge
                      "s"
                      [ genesis.Hash; sideline.Hash ]
                      (TreeOp.Batch [])
                      claimed
                      None
                      (Actor.Human "tester")
                      (ts 3L)
                      OpResultEnvelope.Success

              let byHash =
                  [ genesis.Hash, genesis; sideline.Hash, sideline; bogus.Hash, bogus ]
                  |> Map.ofList

              match DagReplay.replay (fun h -> Map.tryFind h byHash) initial bogus.Hash with
              | Error(DagReplayError.MergeOutcomeMismatch(hash, expected, actual)) ->
                  Expect.equal hash bogus.Hash "the report names the offending node"
                  Expect.equal expected claimed "…the hash it committed to"
                  Expect.notEqual actual claimed "…and the one its delta actually reaches"
              | other -> failtestf "expected MergeOutcomeMismatch, got %A" other
          }

          // ── facet roster (M-C3) ─────────────────────────────────────────

          test "every facet TreeMerge names is attributable by DagPrimacy, and vice versa" {
              // BOTH sides are DERIVED. A hand-written expected list would have to
              // be edited by the same session that added a facet to one side and
              // forgot the other, which is the failure this replaces: Phase 1472
              // added `style.direction` as an independently-merged sub-field and
              // `cellsOf` did not learn it for three phases, so a pin on a text
              // direction was silently attributed to the branch tip.
              let baseT = buildDashboard ()
              let nested = buildNested ()

              let restyle (id: NodeId) (s: SemanticStyle) = TreeOp.UpdateStyle(id, s)

              // 1. every style sub-field contended at once. Named through
              //    `Defaults.style with` rather than as a full literal (the
              //    repo's own authoring-site lint) — and every field IS named,
              //    deliberately: the point of the fixture is that each sub-field
              //    differs on both sides, so a new sub-field arriving with no
              //    entry here would leave its facet uncontended and out of the
              //    roster, which the equality assertion below then catches.
              let styleA =
                  baseT
                  |> applyOk (
                      restyle
                          leftChildId
                          { Defaults.style with
                              Tone = ToneVariant.Brand
                              Weight = StyleWeight.Compact
                              Emphasis = Emphasis.Loud
                              Role = StyleRole.Data
                              Voice = FontVoice.Display
                              Direction = TextDirection.Ltr }
                  )

              let styleB =
                  baseT
                  |> applyOk (
                      restyle
                          leftChildId
                          { Defaults.style with
                              Tone = ToneVariant.Critical
                              Weight = StyleWeight.Spacious
                              Emphasis = Emphasis.Quiet
                              Role = StyleRole.Lede
                              Voice = FontVoice.Structural
                              Direction = TextDirection.Rtl }
                  )

              // 2. kind
              let kindA =
                  baseT
                  |> applyOk (TreeOp.EditNode(leftChildId, (Fuaran.markdown "left" "A text").Kind))

              let kindB =
                  baseT
                  |> applyOk (TreeOp.EditNode(leftChildId, (Fuaran.markdown "left" "B text").Kind))

              // 3. state
              let stateA =
                  baseT
                  |> applyOk (
                      TreeOp.UpdateState(
                          leftChildId,
                          { Defaults.stateBehaviour<TestMsg> with
                              OnEmpty = Some(Fuaran.markdown "empty" "A is empty") }
                      )
                  )

              let stateB =
                  baseT
                  |> applyOk (
                      TreeOp.UpdateState(
                          leftChildId,
                          { Defaults.stateBehaviour<TestMsg> with
                              OnEmpty = Some(Fuaran.markdown "empty" "B is empty") }
                      )
                  )

              // 4. accessibility and 5. tooltip — no op sets either, so the trees
              //    are built directly.
              let describedBy (v: string) (n: Node<TestMsg>) =
                  { n with
                      Accessibility =
                          Some
                              { Defaults.Accessibility.empty with
                                  DescribedBy = Some v } }

              let accA = baseT |> mapNodeById "left" (describedBy "a")
              let accB = baseT |> mapNodeById "left" (describedBy "b")

              let tip (v: string) (n: Node<TestMsg>) =
                  { n with
                      Tooltip = Some(TextSource.Literal v) }

              let tipA = baseT |> mapNodeById "left" (tip "A hint")
              let tipB = baseT |> mapNodeById "left" (tip "B hint")

              // 6. children — each side restructures the same parent differently
              let childrenA = baseT |> applyOk (TreeOp.RemoveNode leftChildId)
              let childrenB = baseT |> applyOk (TreeOp.RemoveNode rightChildId)

              // 7. insert — the same id with different content on both sides
              let insertA =
                  baseT
                  |> applyOk (TreeOp.InsertChild(dashboardId, Fuaran.markdown "new" "A wrote this"))

              let insertB =
                  baseT
                  |> applyOk (TreeOp.InsertChild(dashboardId, Fuaran.markdown "new" "B wrote this"))

              // 8. node — delete/modify
              let deleteModifyA =
                  baseT
                  |> applyOk (
                      restyle
                          rightChildId
                          { Defaults.style with
                              Tone = ToneVariant.Success }
                  )

              let deleteModifyB = baseT |> applyOk (TreeOp.RemoveNode rightChildId)

              // 9. move — moved on one side, edited on the other
              let moveA = nested |> applyOk (TreeOp.MoveNode(movableId, boxBId))

              let moveB =
                  nested
                  |> applyOk (
                      restyle
                          movableId
                          { Defaults.style with
                              Tone = ToneVariant.Brand }
                  )

              let treeMergeFacets =
                  [ baseT, styleA, styleB
                    baseT, kindA, kindB
                    baseT, stateA, stateB
                    baseT, accA, accB
                    baseT, tipA, tipB
                    baseT, childrenA, childrenB
                    baseT, insertA, insertB
                    baseT, deleteModifyA, deleteModifyB
                    nested, moveA, moveB ]
                  |> List.collect (fun (b, a, bb) -> refuses b a bb)
                  |> List.map _.Facet
                  |> Set.ofList

              // Every `TreeOp` case, so the sample cannot silently miss one.
              let sampleOps: TreeOp<TestMsg> list =
                  [ TreeOp.EditNode(leftChildId, (Fuaran.markdown "left" "x").Kind)
                    TreeOp.UpdateProp(leftChildId, "Text", PropValue.Wire(Fuaran.Core.JStr "x"))
                    TreeOp.ReplaceBinding(leftChildId, "Source", Binding.Static None)
                    TreeOp.UpdateStyle(leftChildId, Defaults.style)
                    TreeOp.UpdateState(leftChildId, Defaults.stateBehaviour<TestMsg>)
                    TreeOp.InsertChild(dashboardId, Fuaran.markdown "new" "x")
                    TreeOp.RemoveNode rightChildId
                    TreeOp.MoveNode(movableId, boxBId)
                    TreeOp.ReorderChildren(dashboardId, [ rightChildId; leftChildId ])
                    TreeOp.ReplaceRoot baseT
                    TreeOp.Batch [] ]

              let declaredCases =
                  FSharpType.GetUnionCases(typeof<TreeOp<TestMsg>>)
                  |> Array.map _.Name
                  |> Set.ofArray

              let sampledCases =
                  sampleOps
                  |> List.map (fun op -> (FSharpValue.GetUnionFields(op, typeof<TreeOp<TestMsg>>) |> fst).Name)
                  |> Set.ofList

              Expect.equal sampledCases declaredCases "the op sample covers every TreeOp case"

              let primacyFacets =
                  sampleOps |> List.collect DagPrimacy.cellsOf |> List.map snd |> Set.ofList

              Expect.equal
                  treeMergeFacets
                  primacyFacets
                  "TreeMerge and DagPrimacy name the SAME facet set — a facet one side knows and the other does not is a mislabelled pin"

              // A floor under both, so the test cannot pass by both sides being
              // empty (a refactor that stopped raising conflicts entirely would
              // otherwise leave it green).
              Expect.isTrue (Set.contains "style.direction" treeMergeFacets) "the Phase 1472 facet is among them"
              Expect.isTrue (Set.contains "node" treeMergeFacets) "…as is the delete/modify cell"
              Expect.isTrue (Set.contains "move" treeMergeFacets) "…and the move cell"
              Expect.isTrue (Set.contains "accessibility" treeMergeFacets) "…and accessibility"
              Expect.isTrue (Set.contains "tooltip" treeMergeFacets) "…and tooltip"
          } ]
