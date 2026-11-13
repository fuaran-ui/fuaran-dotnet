module Fuaran.UI.OpStream.Dag.Tests.FoldReconcileTests

open System.IO
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
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
              let mutable recordedFaults = 0

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
                          // THE RECORDED FINDING (Phase 2695; not this phase's rule, awaiting a
                          // ruling). Under a named side, two shapes still reach the fold and fault:
                          // (1) two inserts of one id with different content
                          // (`fold-refusal-same-id-insert`), a `Conflict` whose ops apply in
                          // NEITHER order, so §34.4 says it refuses, yet precedence admits the
                          // class; (2) a re-asserted op that no longer applies at the script's
                          // tail: its own branch later removed or moved what it addresses, or
                          // the other branch changed the child list a re-asserted reorder names.
                          // This arm pins that nothing ELSE faults: never under the default
                          // context, and never on an edit beside a removal of its node.
                          if context.IsNone || besideItsRemoval then
                              failtestf "%s: a fold fault outside the recorded finding (%A)" (what context) e

                          recordedFaults <- recordedFaults + 1
                      | Error _ -> ()

              // The population must reach every answer, or the checks above asked nothing.
              Expect.isGreaterThan derived 0 "some pair derived"
              Expect.isGreaterThan refused 0 "some pair refused"
              Expect.isGreaterThan editsBesideRemovals 0 "some edit beside a removal of its node was drawn"
              printfn "Phase 2695 finding: %d precedence merges fault outside the DeleteModify rule" recordedFaults
          } ]
