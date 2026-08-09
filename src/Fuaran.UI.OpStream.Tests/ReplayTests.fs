module Fuaran.UI.OpStream.Tests.ReplayTests

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.OpStream.Tests.TestSupport

// ============================================================================
//  Replay correctness — drive the apply engine through recorded op sequences
//  and verify the post-replay tree matches a freshly-applied counterpart.
// ============================================================================

let private childIds (root: Node<TestMsg>) : string list =
    match root.Kind with
    | NodeKind.Box(spec) -> spec.Children |> List.map _.Id
    | _ -> failwithf "Expected dashboard, got %A" root.Kind

[<Tests>]
let tests =
    testList
        "Fuaran.UI.OpStream — Replay engine"
        [ test "Replay against empty record set returns initial tree unchanged" {
              let tree = buildDashboard ()

              match Replay.applyTo tree [] with
              | Ok result ->
                  // Node carries closures (no structural equality); compare by id + child ids.
                  Expect.equal result.Id tree.Id "Root id unchanged"
                  Expect.equal (childIds result) (childIds tree) "Children unchanged"
              | Error e -> failtestf "Expected Ok, got Error %A" e
          }

          test "Replay applies a single RemoveNode op" {
              let tree = buildDashboard ()
              let op = TreeOp.RemoveNode(NodeId "right"): TreeOp<TestMsg>
              let record = buildRecord "stream" 1 op None (timestamp 100L)

              match Replay.applyTo tree [ record ] with
              | Ok result ->
                  let ids = childIds result
                  Expect.equal ids [ "left" ] "Right child removed"
              | Error e -> failtestf "Expected Ok, got Error %A" e
          }

          test "Replay applies an InsertChild + RemoveNode sequence" {
              let tree = buildDashboard ()

              let insertOp =
                  TreeOp.InsertChild(NodeId "dash", Fuaran.markdown "middle" "Middle pane")

              let removeOp = TreeOp.RemoveNode(NodeId "left")
              let r1 = buildRecord "stream" 1 insertOp None (timestamp 100L)
              let r2 = buildRecord "stream" 2 removeOp (Some r1) (timestamp 200L)

              match Replay.applyTo tree [ r1; r2 ] with
              | Ok result ->
                  let ids = childIds result
                  Expect.equal ids [ "right"; "middle" ] "Left removed; middle APPENDED rather than placed between"
              | Error e -> failtestf "Expected Ok, got Error %A" e
          }

          test "Replay produces tree structurally equal to direct apply chain" {
              let tree = buildDashboard ()
              let op1 = TreeOp.RemoveNode(NodeId "right"): TreeOp<TestMsg>
              let op2 = TreeOp.InsertChild(NodeId "dash", Fuaran.markdown "footer" "Footer")
              let r1 = buildRecord "stream" 1 op1 None (timestamp 100L)
              let r2 = buildRecord "stream" 2 op2 (Some r1) (timestamp 200L)

              let replayResult = Replay.applyTo tree [ r1; r2 ]

              let directResult =
                  match Apply.apply op1 tree with
                  | Ok t1 ->
                      match Apply.apply op2 t1 with
                      | Ok t2 -> Ok t2
                      | Error e -> Error e
                  | Error e -> Error e

              match replayResult, directResult with
              | Ok rResult, Ok dResult ->
                  Expect.equal (childIds rResult) (childIds dResult) "Replay matches direct apply"
              | _, _ -> failtest "Expected both Ok"
          }

          test "Replay surfaces the failing record's Sequence on apply error" {
              let tree = buildDashboard ()
              // Second op targets a node that doesn't exist after the first op removes it.
              let op1 = TreeOp.RemoveNode(NodeId "right"): TreeOp<TestMsg>
              let op2 = TreeOp.RemoveNode(NodeId "right"): TreeOp<TestMsg>
              let r1 = buildRecord "stream" 1 op1 None (timestamp 100L)
              let r2 = buildRecord "stream" 2 op2 (Some r1) (timestamp 200L)

              match Replay.applyTo tree [ r1; r2 ] with
              | Error(ReplayError.ApplyFailed(sequence, _)) -> Expect.equal sequence 2 "Failure points at sequence 2"
              | Error(ReplayError.ChainBroken e) -> failtestf "Expected an apply failure, not a chain break: %A" e
              | Ok _ -> failtest "Expected apply to fail at sequence 2"
          }

          test "Replay with ReorderChildren round-trips the child order" {
              let tree = buildDashboard ()

              let op =
                  TreeOp.ReorderChildren(NodeId "dash", [ NodeId "right"; NodeId "left" ]): TreeOp<TestMsg>

              let record = buildRecord "stream" 1 op None (timestamp 100L)

              match Replay.applyTo tree [ record ] with
              | Ok result ->
                  let ids = childIds result
                  Expect.equal ids [ "right"; "left" ] "Children reordered"
              | Error e -> failtestf "Expected Ok, got Error %A" e
          } ]
