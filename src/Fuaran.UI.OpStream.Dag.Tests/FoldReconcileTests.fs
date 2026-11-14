module Fuaran.UI.OpStream.Dag.Tests.FoldReconcileTests

open System.IO
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.Merge
open Fuaran.UI.OpStream.TestSupport

// ============================================================================
//  The reconciliation policy over Core's conflict report (Phase 2293).
//
//  Leg A of the `foldFixtures` family — the F# policy reproduces every
//  committed verdict byte for byte — plus the properties the corpus alone
//  cannot pin: the verdict is symmetric under swapping the branches, the
//  safety net refuses a fold whose two orders differ, and a delta that does
//  not apply is refused before anything is folded.
// ============================================================================

let private corpusDir =
    Fuaran.Tests.CorpusRoot.tryFind ()
    |> Option.map (fun root -> Path.Combine(root, "merge-conformance"))
    |> Option.defaultValue (
        Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "..", "wire-format-fixtures", "merge-conformance")
    )

let private read (name: string) =
    File.ReadAllText(Path.Combine(corpusDir, name)).TrimEnd('\n', '\r')

/// The refusals with each conflict's sides swapped — what swapping the branches must produce.
let private mirrored (refusals: Refusal<TestMsg> list) : Refusal<TestMsg> list =
    refusals
    |> List.map (fun r ->
        { r with
            Conflict =
                { r.Conflict with
                    Left = r.Conflict.Right
                    Right = r.Conflict.Left } })

let private byId id =
    FoldCorpus.fixtures |> List.find (fun (i, _, _, _, _, _) -> i = id)

/// The children of `parent` in `tree`, by id.
let private childIds (tree: Node<TestMsg>) (parent: string) : string list =
    let rec find (n: Node<TestMsg>) =
        if n.Id = parent then
            Some n
        else
            Fuaran.UI.Ops.Introspect.getChildren n.Kind
            |> Option.defaultValue []
            |> List.tryPick find

    find tree
    |> Option.bind (fun n -> Fuaran.UI.Ops.Introspect.getChildren n.Kind)
    |> Option.defaultValue []
    |> List.map _.Id

// ---- precedence (Phase 2695) ------------------------------------------------

let private canonOp (op: TreeOp<TestMsg>) = CanonicalJson.encodeOp op

/// A dashboard holding two panes and a container with one child, so every touch of a
/// removed node (an edit, a restyle, an insert beneath, a move) has a node to land on.
let private precedenceBase () : Node<TestMsg> =
    container
        "dash"
        [ Fuaran.markdown "left" "Left pane"
          Fuaran.markdown "right" "Right pane"
          container "box" [ Fuaran.markdown "inner" "Inner" ] ]

let private restyle (id: string) (tone: ToneVariant) : TreeOp<TestMsg> =
    TreeOp.UpdateStyle(NodeId id, { Defaults.style with Tone = tone })

/// Each touch of a removed node §34.4 names: the touch's name, the removal, and the
/// other branch's op on the removed node.
let private removalPairs: (string * TreeOp<TestMsg> * TreeOp<TestMsg>) list =
    [ "an edit", TreeOp.RemoveNode leftChildId, TreeOp.EditNode(leftChildId, (Fuaran.markdown "left" "Edited").Kind)
      "a restyle", TreeOp.RemoveNode leftChildId, restyle "left" ToneVariant.Success
      "an insert beneath",
      TreeOp.RemoveNode(NodeId "box"),
      TreeOp.InsertChild(NodeId "box", Fuaran.markdown "new" "New")
      "a move", TreeOp.RemoveNode leftChildId, TreeOp.MoveNode(leftChildId, NodeId "box") ]

/// A deterministic population of branch-delta pairs off `precedenceBase`: each branch draws
/// one to three ops, keeping only those that apply to what the branch holds so far, so
/// every delta applies from the base and no fault the reconcile meets is the delta's own.
/// Fresh ids are drawn from a small set, so two branches inserting one id is common.
let private generatedPairs (seed: int) (count: int) : (TreeOp<TestMsg> list * TreeOp<TestMsg> list) list =
    let mutable state = uint32 seed

    let below (n: int) =
        state <- state * 1664525u + 1013904223u
        int ((state >>> 8) % uint32 n)

    let pick (xs: 'T list) = xs |> List.item (below xs.Length)
    let panes = [ "left"; "right"; "inner" ]
    let containers = [ "dash"; "box" ]
    let tones = [ ToneVariant.Brand; ToneVariant.Success; ToneVariant.Critical ]

    let drawOp () : TreeOp<TestMsg> =
        match below 6 with
        | 0 -> TreeOp.InsertChild(NodeId(pick containers), Fuaran.markdown (sprintf "n%d" (below 3)) (pick panes))
        | 1 -> restyle (pick (panes @ containers)) (pick tones)
        | 2 -> TreeOp.EditNode(NodeId(pick panes), (Fuaran.markdown "x" (sprintf "text %d" (below 3))).Kind)
        | 3 -> TreeOp.RemoveNode(NodeId(pick (panes @ [ "box" ])))
        | 4 -> TreeOp.MoveNode(NodeId(pick panes), NodeId(pick containers))
        | _ ->
            TreeOp.ReorderChildren(
                NodeId "dash",
                pick [ [ "right"; "left"; "box" ]; [ "box"; "left"; "right" ] ]
                |> List.map NodeId
            )

    let drawDelta () =
        let n = 1 + below 3

        ((precedenceBase (), []), [ 1..n ])
        ||> List.fold (fun (tree, ops) _ ->
            let op = drawOp ()

            match Fuaran.UI.Ops.Apply.apply op tree with
            | Ok t -> t, ops @ [ op ]
            | Error _ -> tree, ops)
        |> snd

    [ for _ in 1..count -> drawDelta (), drawDelta () ]

// ---- the yield rule (Phase 2724) --------------------------------------------

let private contexts = [ None; Some ReconcileSide.A; Some ReconcileSide.B ]

/// `delta` with a reorder appended that lists `parent`'s children in the order the
/// delta's own head holds them — a restatement, unchanged where it applies.
/// `delta` itself when the head does not hold the parent.
let private restating (baseT: Node<TestMsg>) (parent: string) (delta: TreeOp<TestMsg> list) =
    let head = delta |> List.fold (fun t op -> applyOk op t) baseT

    match Fuaran.UI.Ops.Introspect.findNode (NodeId parent) head with
    | Some _ ->
        delta
        @ [ TreeOp.ReorderChildren(NodeId parent, childIds head parent |> List.map NodeId) ]
    | None -> delta

/// Mint the merge node of two branches recorded off a genesis (the base IS
/// `baseT`), commit it, and replay it through Core's closure fold: the tree the
/// node replays to, beside the tree the merge derived. `aFirst` decides which
/// branch's records carry the earlier timestamps, so both drain orders are met.
let private mintAndReplay
    (precedence: ReconcileSide option)
    (aFirst: bool)
    (baseT: Node<TestMsg>)
    (opsA: TreeOp<TestMsg> list)
    (opsB: TreeOp<TestMsg> list)
    =
    let streamId = "yield-stream"
    let genesis = stepRecord streamId None (TreeOp.Batch []) 1700000000L

    let branch (ops: TreeOp<TestMsg> list) (t0: int64) =
        ops
        |> List.indexed
        |> List.fold
            (fun (acc: DagOpRecord<TestMsg> list) (i, op) ->
                stepRecord streamId (Some(List.head acc)) op (t0 + int64 i) :: acc)
            [ genesis ]

    let a = branch opsA (if aFirst then 1700000100L else 1700000200L)
    let b = branch opsB (if aFirst then 1700000200L else 1700000100L)
    let records = a @ b |> List.distinctBy _.Hash
    let dag = DagOpRecord.toDag records

    let context =
        { MergeContext.defaults with
            Precedence = precedence }

    match
        (DagMerge.mergeDag context streamId baseT dag (List.head a).Hash (List.head b).Hash (timestamp 1700000900L))
            .Result
    with
    | MergeResult.Merged(record, tree) ->
        let reach = Fuaran.Core.Dag.Reach.ofDag (DagOpRecord.toDag (records @ [ record ]))

        match DagFold.replayToWith baseT reach record.Hash with
        | Ok replayed -> Some(canonical replayed, canonical tree)
        | Error e -> failtestf "the merge node does not replay: %A" e
    | _ -> None

[<Tests>]
let tests =
    testList
        "Dag.FoldReconcile"
        [ test "committed fold corpus matches the F# policy byte for byte" {
              if not (Directory.Exists corpusDir) then
                  skiptest "wire-format-fixtures/merge-conformance corpus absent (single-repo checkout)"
              else
                  for (id, expectation, _, baseT, opsA, opsB) in FoldCorpus.fixtures do
                      let suffix, bytes, _, _ = FoldCorpus.artefactOf id expectation baseT opsA opsB
                      Expect.equal (read (id + suffix)) bytes (sprintf "%s: F# verdict == committed artefact" id)
                      Expect.equal (read (id + ".base.json")) (canonical baseT) (sprintf "%s: base payload" id)
                      Expect.equal (read (id + ".a.ops.json")) (FoldCorpus.encodeOps opsA) (sprintf "%s: a ops" id)
                      Expect.equal (read (id + ".b.ops.json")) (FoldCorpus.encodeOps opsB) (sprintf "%s: b ops" id)
          }

          test "every fixture's verdict is symmetric under swapping the branches" {
              for (id, expectation, _, baseT, opsA, opsB) in FoldCorpus.fixtures do
                  match
                      FoldCorpus.verdictOf expectation baseT opsA opsB, FoldCorpus.verdictOf expectation baseT opsB opsA
                  with
                  | Ok ab, Ok ba ->
                      Expect.equal (canonical ba.Tree) (canonical ab.Tree) (sprintf "%s: one derived tree" id)
                  | Error(ReconcileFault.Refused ab), Error(ReconcileFault.Refused ba) ->
                      Expect.equal
                          (DagReconcile.encodeEnvelope ba)
                          (DagReconcile.encodeEnvelope (mirrored ab))
                          (sprintf "%s: the swapped envelope is the mirrored one" id)
                  | Error(ReconcileFault.IntroducedDefects ab), Error(ReconcileFault.IntroducedDefects ba) ->
                      Expect.equal ba ab (sprintf "%s: one verdict" id)
                  | x, y -> failtestf "%s: the two namings disagree: %A / %A" id x y
          }

          test "the remove-versus-move pair refuses as DeleteModify" {
              let _, _, _, baseT, opsA, opsB =
                  FoldCorpus.fixtures
                  |> List.find (fun (id, _, _, _, _, _) -> id = "fold-refusal-remove-vs-move")

              match DagReconcile.reconcile baseT opsA opsB with
              | Error(ReconcileFault.Refused refusals) ->
                  Expect.isTrue
                      (refusals |> List.exists (fun r -> r.Class = RefusalClass.DeleteModify))
                      "a DeleteModify entry"
              | other -> failtestf "expected a refusal, got %A" other
          }

          test "an interleave records the reorder the rule asks for, and an agreement records nothing" {
              let byId id =
                  FoldCorpus.fixtures |> List.find (fun (i, _, _, _, _, _) -> i = id)

              let _, _, _, b1, a1, c1 = byId "fold-insert-interleave"

              match DagReconcile.reconcile b1 a1 c1 with
              | Ok r ->
                  Expect.equal r.Interleaves 1 "one interleaved parent"
                  Expect.equal r.Script.Length 1 "the fold's A-first order (zzz, aaa) is reordered"
              | Error e -> failtestf "expected a derivation, got %A" e

              let _, _, _, b2, a2, c2 = byId "fold-agreement-remove"

              match DagReconcile.reconcile b2 a2 c2 with
              | Ok r ->
                  Expect.equal r.Agreements 1 "one agreement"
                  Expect.isEmpty r.Script "an agreement needs no script"
              | Error e -> failtestf "expected a derivation, got %A" e
          }

          test "a rebased reorder keeps its declared order and the append follows it" {
              let _, _, _, b, a, c = byId "fold-reorder-and-insert"

              match DagReconcile.reconcile b a c with
              | Ok r ->
                  Expect.equal (childIds r.Tree "dash") [ "right"; "left"; "new" ] "reordered run, then the append"
              | Error e -> failtestf "expected a derivation, got %A" e
          }

          test "a node both branches move into one parent beside other appends is listed once" {
              let _, _, _, b, a, c = byId "fold-double-move-and-appends"

              match DagReconcile.reconcile b a c with
              | Ok r ->
                  Expect.equal
                      (childIds r.Tree "dash")
                      [ "boxa"; "boxb"; "aa"; "m"; "zz" ]
                      "the agreed node once, where the merged runs first reach it"

                  for op in r.Script do
                      match op with
                      | TreeOp.ReorderChildren(_, order) ->
                          Expect.equal (List.distinct order) order "the recorded reorder lists every child once"
                      | other -> failtestf "a script holds reorders only, got %A" other
              | Error e -> failtestf "expected a derivation, got %A" e
          }

          test "a delta that does not apply is refused before anything is folded" {
              let baseT = buildDashboard ()

              match DagReconcile.reconcile baseT [] [ TreeOp.RemoveNode(NodeId "absent") ] with
              | Error(ReconcileFault.DeltaDoesNotApply(ReconcileSide.B, 0, _)) -> ()
              | other -> failtestf "expected DeltaDoesNotApply on B at 0, got %A" other
          }

          test "applyHeld applies an op whose effect is already held as no change, and nothing else" {
              let baseT = buildDashboard ()
              let removed = baseT |> applyOk (TreeOp.RemoveNode rightChildId)

              Expect.equal
                  (DagReconcile.applyHeld (TreeOp.RemoveNode rightChildId) removed
                   |> Result.map canonical)
                  (Ok(canonical removed))
                  "a second removal of a removed node is no change"

              let inserted =
                  baseT |> applyOk (TreeOp.InsertChild(dashboardId, Fuaran.markdown "new" "A"))

              Expect.equal
                  (DagReconcile.applyHeld (TreeOp.InsertChild(dashboardId, Fuaran.markdown "new" "A")) inserted
                   |> Result.map canonical)
                  (Ok(canonical inserted))
                  "a second insert of the identical node is no change"

              Expect.isError
                  (DagReconcile.applyHeld (TreeOp.InsertChild(dashboardId, Fuaran.markdown "new" "B")) inserted)
                  "an insert under a held id with DIFFERENT content is still the engine's DuplicateNodeId"
          }

          test "under precedence, a touch beside a removal of its node refuses as DeleteModify, never as a fold fault" {
              let baseT = precedenceBase ()

              for name, removal, touch in removalPairs do
                  for removalOn in [ ReconcileSide.A; ReconcileSide.B ] do
                      let left, right =
                          if removalOn = ReconcileSide.A then
                              removal, touch
                          else
                              touch, removal

                      let deleteModify (rs: Refusal<TestMsg> list) =
                          rs |> List.filter (fun r -> r.Class = RefusalClass.DeleteModify)

                      let byDefault =
                          match DagReconcile.reconcile baseT [ left ] [ right ] with
                          | Error(ReconcileFault.Refused rs) -> deleteModify rs
                          | other -> failtestf "%s: the default context must refuse, got %A" name other

                      for side in [ ReconcileSide.A; ReconcileSide.B ] do
                          let what = sprintf "%s, removal on %A, precedence %A" name removalOn side

                          match DagReconcile.reconcileWith (Some side) baseT [ left ] [ right ] with
                          | Error(ReconcileFault.Refused rs) ->
                              Expect.isTrue
                                  (rs
                                   |> List.exists (fun r ->
                                       r.Class = RefusalClass.DeleteModify
                                       && canonOp r.Conflict.Left = canonOp left
                                       && canonOp r.Conflict.Right = canonOp right))
                                  (sprintf "%s: a DeleteModify entry naming both ops" what)

                              Expect.equal
                                  (DagReconcile.encodeEnvelope rs)
                                  (DagReconcile.encodeEnvelope byDefault)
                                  (sprintf "%s: the envelope the default context gives the pair" what)
                          | other -> failtestf "%s: expected a DeleteModify refusal, got %A" what other
          }

          test "no DeleteModify answer is a fold fault: fixtures and a generated population, under every context" {
              let contexts = [ None; Some ReconcileSide.A; Some ReconcileSide.B ]

              let fixturePairs =
                  FoldCorpus.fixtures
                  |> List.map (fun (id, _, _, baseT, opsA, opsB) -> id, baseT, opsA, opsB)

              let generated =
                  generatedPairs 2695 1500
                  |> List.mapi (fun i (a, b) -> sprintf "generated #%d" i, precedenceBase (), a, b)

              let mutable refused = 0
              let mutable derived = 0

              let mutable editsBesideRemovals = 0

              for id, baseT, opsA, opsB in fixturePairs @ generated do
                  let what context =
                      sprintf
                          "%s under %A\nA: %s\nB: %s"
                          id
                          context
                          (FoldCorpus.encodeOps opsA)
                          (FoldCorpus.encodeOps opsB)

                  let head ops =
                      ops
                      |> List.fold (fun t op -> t |> Result.bind (Fuaran.UI.Ops.Apply.apply op)) (Ok baseT)

                  // §34.4's pair, stated from the deltas alone: one op removes, and the other
                  // addresses a node the base holds and the removing branch's head does not.
                  let besideItsRemoval =
                      match DagReconcile.reconcile baseT opsA opsB, head opsA, head opsB with
                      | Error(ReconcileFault.Refused rs), Ok headA, Ok headB ->
                          rs
                          |> List.exists (fun r ->
                              let removes =
                                  function
                                  | TreeOp.RemoveNode _ -> true
                                  | _ -> false

                              let other, removingHead =
                                  match removes r.Conflict.Left, removes r.Conflict.Right with
                                  | true, false -> Some r.Conflict.Right, headA
                                  | false, true -> Some r.Conflict.Left, headB
                                  | _ -> None, headA

                              other
                              |> Option.exists (fun o ->
                                  Fuaran.UI.Ops.TreeOp.targets o
                                  |> List.exists (fun n ->
                                      (Fuaran.UI.Ops.Introspect.findNode n baseT).IsSome
                                      && (Fuaran.UI.Ops.Introspect.findNode n removingHead).IsNone)))
                      | _ -> false

                  for context in contexts do
                      match DagReconcile.reconcileWith context baseT opsA opsB with
                      | Ok _ ->
                          derived <- derived + 1

                          if besideItsRemoval then
                              failtestf "%s: an edit beside a removal of its node derived" (what context)
                      | Error(ReconcileFault.Refused rs) ->
                          refused <- refused + 1

                          if besideItsRemoval then
                              editsBesideRemovals <- editsBesideRemovals + 1

                              Expect.isTrue
                                  (rs |> List.exists (fun r -> r.Class = RefusalClass.DeleteModify))
                                  (sprintf "%s: refused with a DeleteModify entry" (what context))

                          Expect.isNonEmpty rs (sprintf "%s: a refusal names its pairs" (what context))
                      | Error(ReconcileFault.FoldFaulted e) ->
                          // Phase 2725 (operator ruling 2026-11-14): no merge answers a fold
                          // fault, under any context. The 118 the Phase 2695 measurement left
                          // (fixtures and this population, under a named side) refuse with
                          // typed entries now; see the two tests below.
                          failtestf "%s: a fold fault (%A)" (what context) e
                      | Error _ -> ()

              // The population must reach every answer, or the checks above asked nothing.
              Expect.isGreaterThan derived 0 "some pair derived"
              Expect.isGreaterThan refused 0 "some pair refused"
              Expect.isGreaterThan editsBesideRemovals 0 "some edit beside a removal of its node was drawn"
          }

          test
              "under precedence, a pair that applies in neither order refuses with the entry the default context gives it" {
              // Shape 1 of the Phase 2695 finding: two inserts of one id, and (found by this
              // phase) two moves that would put each node inside the other.
              for case in [ "same-id-insert"; "mutual-move" ] do
                  let _, _, _, _, baseT, opsA, opsB =
                      FoldCorpus.precedenceFixtures
                      |> List.find (fun (i, _, _, _, _, _, _) -> i = "fold-precedence-a-" + case)

                  let byDefault =
                      match DagReconcile.reconcile baseT opsA opsB with
                      | Error(ReconcileFault.Refused rs) -> DagReconcile.encodeEnvelope rs
                      | other -> failtestf "%s: the default context must refuse, got %A" case other

                  for side in [ ReconcileSide.A; ReconcileSide.B ] do
                      match DagReconcile.reconcileWith (Some side) baseT opsA opsB with
                      | Error(ReconcileFault.Refused rs) ->
                          Expect.equal
                              (DagReconcile.encodeEnvelope rs)
                              byDefault
                              (sprintf "%s, precedence %A: the default context's entry" case side)
                      | other -> failtestf "%s, precedence %A: expected a refusal, got %A" case side other
          }

          test "under precedence, a re-asserted op that no longer applies refuses its pair with the default entry" {
              // Shape 2 of the Phase 2695 finding: its own branch later removed or moved what the
              // op addresses (b names the move its branch then removes), or the other branch
              // changed the child list a re-asserted reorder names (a and b of the stale reorder).
              for case, sides in
                  [ "superseded-move", [ ReconcileSide.B ]
                    "stale-reorder", [ ReconcileSide.A; ReconcileSide.B ] ] do
                  for side in sides do
                      let id = sprintf "fold-precedence-%s-%s" (FoldCorpus.sideText side) case

                      let _, _, _, _, baseT, opsA, opsB =
                          FoldCorpus.precedenceFixtures |> List.find (fun (i, _, _, _, _, _, _) -> i = id)

                      let defaults =
                          match DagReconcile.reconcile baseT opsA opsB with
                          | Error(ReconcileFault.Refused rs) -> rs
                          | other -> failtestf "%s: the default context must refuse, got %A" id other

                      match DagReconcile.reconcileWith (Some side) baseT opsA opsB with
                      | Error(ReconcileFault.Refused rs) ->
                          let entry (r: Refusal<TestMsg>) = DagReconcile.encodeEnvelope [ r ]

                          for r in rs do
                              Expect.contains
                                  (defaults |> List.map entry)
                                  (entry r)
                                  (sprintf "%s: an entry the default context gives" id)

                          let named = if side = ReconcileSide.A then opsA else opsB

                          Expect.isTrue
                              (rs
                               |> List.forall (fun r ->
                                   let mine =
                                       if side = ReconcileSide.A then
                                           r.Conflict.Left
                                       else
                                           r.Conflict.Right

                                   named |> List.exists (fun o -> canonOp o = canonOp mine)))
                              (sprintf "%s: each entry names the named side's re-asserted op" id)
                      | other -> failtestf "%s: expected a refusal, got %A" id other
          }

          test "committed precedence fixtures match the F# policy byte for byte, and swapping the deltas swaps the side" {
              let swap side =
                  if side = ReconcileSide.A then
                      ReconcileSide.B
                  else
                      ReconcileSide.A

              for id, side, expectation, _, baseT, opsA, opsB in FoldCorpus.precedenceFixtures do
                  if Directory.Exists corpusDir then
                      let suffix, bytes, _, _ =
                          FoldCorpus.precedenceArtefactOf id side expectation baseT opsA opsB

                      Expect.equal (read (id + suffix)) bytes (sprintf "%s: F# verdict == committed artefact" id)
                      Expect.equal (read (id + ".base.json")) (canonical baseT) (sprintf "%s: base payload" id)
                      Expect.equal (read (id + ".a.ops.json")) (FoldCorpus.encodeOps opsA) (sprintf "%s: a ops" id)
                      Expect.equal (read (id + ".b.ops.json")) (FoldCorpus.encodeOps opsB) (sprintf "%s: b ops" id)

                  match
                      DagReconcile.reconcileWith (Some side) baseT opsA opsB,
                      DagReconcile.reconcileWith (Some(swap side)) baseT opsB opsA
                  with
                  | Ok ab, Ok ba ->
                      Expect.equal (canonical ba.Tree) (canonical ab.Tree) (sprintf "%s: one derived tree" id)

                      for aFirst in [ true; false ] do
                          match mintAndReplay (Some side) aFirst baseT opsA opsB with
                          | Some(replayed, derived) ->
                              Expect.equal replayed derived (sprintf "%s (A first: %b): the node replays" id aFirst)
                          | None -> failtestf "%s: expected a merge node" id
                  | Error(ReconcileFault.Refused ab), Error(ReconcileFault.Refused ba) ->
                      Expect.equal
                          (DagReconcile.encodeEnvelope ba)
                          (DagReconcile.encodeEnvelope (mirrored ab))
                          (sprintf "%s: the swapped envelope is the mirrored one" id)
                  | x, y -> failtestf "%s: the two namings disagree: %A / %A" id x y
          }

          test
              "no precedence merge faults, every refusal is the default's, and every derivation is byte-identical to before" {
              let population seed =
                  generatedPairs seed 1500
                  |> List.mapi (fun i (a, b) -> sprintf "generated %d #%d" seed i, precedenceBase (), a, b)

              let fixturePairs =
                  FoldCorpus.fixtures
                  |> List.map (fun (id, _, _, baseT, opsA, opsB) -> id, baseT, opsA, opsB)

              let precedencePairs =
                  FoldCorpus.precedenceFixtures
                  |> List.map (fun (id, _, _, _, baseT, opsA, opsB) -> id, baseT, opsA, opsB)

              let mutable derivations = []
              let mutable settledNothing = 0

              for id, baseT, opsA, opsB in fixturePairs @ population 2695 @ population 2724 @ precedencePairs do
                  let byDefault =
                      match DagReconcile.reconcile baseT opsA opsB with
                      | Error(ReconcileFault.Refused rs) -> rs |> List.map (fun r -> DagReconcile.encodeEnvelope [ r ])
                      | _ -> []

                  for side in [ ReconcileSide.A; ReconcileSide.B ] do
                      match DagReconcile.reconcileWith (Some side) baseT opsA opsB with
                      | Ok r ->
                          if id.StartsWith "generated 2724" |> not && id.StartsWith "fold-precedence" |> not then
                              derivations <-
                                  sprintf
                                      "%s|%A|%s|%s"
                                      (id.Replace("generated 2695 ", "generated "))
                                      side
                                      (canonical r.Tree)
                                      (FoldCorpus.encodeOps r.Script)
                                  :: derivations
                      | Error(ReconcileFault.Refused rs) ->
                          // A named side refuses only pairs the default context refuses, with the
                          // entries it gives them: the envelope is a subset of the default's.
                          for r in rs do
                              Expect.contains
                                  byDefault
                                  (DagReconcile.encodeEnvelope [ r ])
                                  (sprintf "%s under %A: a default entry" id side)

                          if rs.Length = byDefault.Length then
                              settledNothing <- settledNothing + 1
                      | Error(ReconcileFault.FoldFaulted e) -> failtestf "%s under %A: a fold fault (%A)" id side e
                      | Error _ -> ()

              // The derivations the landed tree gave under a named side, over the Phase 2695
              // population (seed 2695) and the fold fixtures, before Phase 2725: their count
              // and the SHA-256 over `id|side|tree|script` lines in order. A merge that derived
              // still derives, byte for byte, and nothing that faulted now derives.
              let lines = List.rev derivations
              Expect.equal lines.Length 2372 "the derivation count before Phase 2725"

              Expect.equal
                  (Fuaran.Core.Hash.sha256Hex (String.concat "\n" lines))
                  "5ebed2d4d257964310986bd69880e5dc9244ca1c3dbd2b0d57c600412ed6faba"
                  "every precedence derivation is byte-identical to before"

              Expect.isGreaterThan settledNothing 0 "some precedence merge refused"
          }

          test
              "an unchanged reorder yields to a real reorder of its parent: every context, both namings, and the node replays to it" {
              for id, real in
                  [ "fold-reorder-beside-restated-order", [ "right"; "left" ]
                    "fold-restated-order-beside-reorder", [ "right"; "left" ]
                    "fold-append-restated-beside-reorder", [ "boxb"; "boxa"; "new" ] ] do
                  let _, _, _, baseT, opsA, opsB = byId id

                  for context in contexts do
                      for x, y in [ opsA, opsB; opsB, opsA ] do
                          match DagReconcile.reconcileWith context baseT x y with
                          | Ok r ->
                              Expect.equal
                                  (childIds r.Tree "dash")
                                  real
                                  (sprintf "%s under %A: the real order" id context)

                              Expect.isTrue
                                  (r.Script
                                   |> List.exists (fun op ->
                                       canonOp op = canonOp (
                                           TreeOp.ReorderChildren(NodeId "dash", real |> List.map NodeId)
                                       )))
                                  (sprintf "%s under %A: the script records the surviving order" id context)

                              Expect.equal r.Reasserted 0 (sprintf "%s under %A: nothing left to re-assert" id context)
                          | Error e -> failtestf "%s under %A: expected a derivation, got %A" id context e

                      for aFirst in [ true; false ] do
                          match mintAndReplay context aFirst baseT opsA opsB with
                          | Some(replayed, derived) ->
                              Expect.equal
                                  replayed
                                  derived
                                  (sprintf "%s under %A (A first: %b): replays" id context aFirst)
                          | None -> failtestf "%s under %A: expected a merge node" id context
          }

          test "yieldUnchangedReorders drops an unchanged reorder beside a real reorder of its parent, and nothing else" {
              let baseT = buildDashboard ()
              let restated = TreeOp.ReorderChildren(dashboardId, [ leftChildId; rightChildId ])
              let real = TreeOp.ReorderChildren(dashboardId, [ rightChildId; leftChildId ])
              let insert = TreeOp.InsertChild(dashboardId, Fuaran.markdown "new" "New")
              let remove = TreeOp.RemoveNode rightChildId

              let yieldOf a b =
                  let keptA, keptB, parents = DagReconcile.yieldUnchangedReorders baseT a b
                  FoldCorpus.encodeOps keptA, FoldCorpus.encodeOps keptB, parents

              Expect.equal
                  (yieldOf [ restated ] [ real ])
                  ("[]", FoldCorpus.encodeOps [ real ], [ "dash" ])
                  "beside a real reorder: the restated one leaves its delta, the parent is named"

              Expect.equal
                  (yieldOf [ real ] [ restated ])
                  (FoldCorpus.encodeOps [ real ], "[]", [ "dash" ])
                  "and the same on the other branch"

              for name, other in
                  [ "an insert into the parent", insert
                    "a removal of a child it lists", remove
                    "another restatement", restated
                    "a real reorder inside a Batch", TreeOp.Batch [ real ]
                    "a real reorder of another parent",
                    TreeOp.ReorderChildren(dashboardId, [ rightChildId; leftChildId; NodeId "box" ]) ] do
                  let other' = name.EndsWith "another parent"
                  let baseT = if other' then precedenceBase () else baseT

                  let restated =
                      if other' then
                          TreeOp.ReorderChildren(NodeId "box", [ NodeId "inner" ])
                      else
                          restated

                  let keptA, keptB, parents =
                      DagReconcile.yieldUnchangedReorders baseT [ restated ] [ other ]

                  Expect.equal
                      (FoldCorpus.encodeOps keptA, FoldCorpus.encodeOps keptB, parents)
                      (FoldCorpus.encodeOps [ restated ], FoldCorpus.encodeOps [ other ], [])
                      (sprintf "beside %s nothing is dropped" name)

              // A real reorder inside a Batch keeps the pair refused: the rule frees the
              // top-level pair, and the footprint frees nothing here.
              match DagReconcile.reconcile baseT [ restated ] [ TreeOp.Batch [ real ] ] with
              | Error(ReconcileFault.Refused _) -> ()
              | other -> failtestf "a restatement beside a Batch holding a real reorder still refuses, got %A" other
          }

          test "a restatement appended beside a real reorder of its parent changes no verdict and no tree, and replays" {
              let mutable fired = 0

              for i, (a, b) in generatedPairs 2724 1500 |> List.indexed do
                  let baseT = precedenceBase ()

                  for name, a', b' in
                      [ "A restates", restating baseT "dash" a, b
                        "B restates", a, restating baseT "dash" b ] do
                      let _, _, yielded = DagReconcile.yieldUnchangedReorders baseT a' b'

                      if not yielded.IsEmpty then
                          fired <- fired + 1

                          for context in contexts do
                              let what = sprintf "generated #%d, %s, under %A" i name context

                              let answer (r: Result<Reconciled<TestMsg>, ReconcileFault<TestMsg>>) =
                                  match r with
                                  | Ok r -> "derives " + canonical r.Tree
                                  | Error(ReconcileFault.Refused rs) -> "refuses " + DagReconcile.encodeEnvelope rs
                                  | Error e -> sprintf "%A" e

                              Expect.equal
                                  (answer (DagReconcile.reconcileWith context baseT a' b'))
                                  (answer (DagReconcile.reconcileWith context baseT a b))
                                  (sprintf "%s: the restatement yields and changes nothing" what)

                              match mintAndReplay context (i % 2 = 0) baseT a' b' with
                              | Some(replayed, derived) -> Expect.equal replayed derived (sprintf "%s: replays" what)
                              | None -> ()

              Expect.isGreaterThan fired 100 "the population reaches the rule"
          } ]
