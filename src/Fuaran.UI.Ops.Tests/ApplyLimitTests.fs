module Fuaran.UI.Tests.ApplyLimits

// ============================================================================
//  The apply-time §21 guard (WIRE_FORMAT §21; WireLimits.MaxDepth / MaxNodes).
//
//  The decoder bounds what ARRIVES; nothing bounded what an apply PRODUCES. A
//  tree assembled op by op — a `Progressive` stream of small frames, a replay,
//  a driven session — grew past `MaxDepth` without any single op looking
//  unusual, and the result was a tree this host held happily and no host could
//  decode, including this one on the next round trip.
//
//  The refusal is asserted as an APPLY OUTCOME, not merely as a refusal. The
//  pre-emit validator already reported `MaxDepthExceeded`, but it walks a
//  finished tree and names whichever node it reached — a node that is not at
//  fault, in an operation long since finished. What these tests pin is that the
//  op that crossed the line is the one refused, and that the refusal reaches
//  the telemetry / op-stream mapping (FGP 5).
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops

/// A Box nested `depth` levels; the root is depth 1 and the DEEPEST node is
/// `n1`, so an insert addressed at `n1` adds exactly one level.
let rec private chain (depth: int) : Node<unit> =
    if depth <= 1 then
        Fuaran.dashboard
            "n1"
            { Defaults.dashboard<unit> with
                Children = [] }
    else
        Fuaran.dashboard
            (sprintf "n%d" depth)
            { Defaults.dashboard<unit> with
                Children = [ chain (depth - 1) ] }

let private leaf (nodeId: string) : Node<unit> =
    Fuaran.dashboard
        nodeId
        { Defaults.dashboard<unit> with
            Children = [] }

let private isLimitExceeded (result: Result<Node<unit>, ApplyError>) =
    match result with
    | Error err -> err.Code = ApplyErrorCode.LimitExceeded
    | Ok _ -> false

[<Tests>]
let applyLimitTests =
    testList
        "Fuaran.UI.Ops apply-time wire limits"
        [ test "InsertChild one level past MaxDepth is refused with LimitExceeded" {
              let tree = chain WireLimits.MaxDepth
              let result = Apply.apply (TreeOp.InsertChild(NodeId "n1", leaf "over")) tree

              match result with
              | Error err ->
                  Expect.equal err.Code ApplyErrorCode.LimitExceeded "The op that crossed the line is the one refused"
                  Expect.stringContains err.Message "MaxDepth" "The message names the limit that was breached"
              | Ok _ -> failtest "A tree one level past MaxDepth was applied"
          }

          test "InsertChild reaching exactly MaxDepth is accepted" {
              // A guard that refused the boundary case would be a liveness bug
              // wearing a safety fix's clothes.
              let tree = chain (WireLimits.MaxDepth - 1)
              let result = Apply.apply (TreeOp.InsertChild(NodeId "n1", leaf "at-the-limit")) tree

              match result with
              | Ok _ -> ()
              | Error err -> failtestf "A tree exactly at MaxDepth was refused: %A" err
          }

          test "ReplaceRoot with an over-deep tree is refused" {
              // The one op that can breach the limit in a single step from any
              // starting point.
              let overDeep = chain (WireLimits.MaxDepth + 1)
              let result = Apply.apply (TreeOp.ReplaceRoot overDeep) (leaf "root")

              Expect.isTrue (isLimitExceeded result) "An over-deep whole-tree swap must be refused"
          }

          test "A Batch whose composition breaches is refused and leaves the tree alone" {
              // The check runs on the RESULT, so a Batch whose individual ops
              // each look fine but whose composition crosses the line is
              // refused — and nothing is applied, since a Batch is
              // all-or-nothing.
              let tree = chain (WireLimits.MaxDepth - 1)

              let batch =
                  TreeOp.Batch
                      [ TreeOp.InsertChild(NodeId "n1", leaf "a")
                        TreeOp.InsertChild(NodeId "a", leaf "b") ]

              Expect.isTrue (Apply.apply batch tree |> isLimitExceeded) "The composed batch must be refused"
          }

          test "A non-growing op on an already-over-limit tree still applies" {
              // The seven ops that cannot grow the tree are not charged a walk
              // to establish what their own semantics already guarantee — and
              // refusing them would strand a tree they did not create.
              let over = chain (WireLimits.MaxDepth + 5)

              match Apply.apply (TreeOp.UpdateStyle(NodeId "n1", Defaults.style)) over with
              | Ok _ -> ()
              | Error err ->
                  Expect.notEqual
                      err.Code
                      ApplyErrorCode.LimitExceeded
                      "A non-growing op was refused by the limit guard"
          }

          test "The Progressive streaming path is covered by the same guard" {
              // `Streaming.applyFold` folds through `Apply.apply`, so the
              // progressive path needs no second guard — this pins that it is
              // genuinely the same one rather than a coincidence.
              let tree = chain WireLimits.MaxDepth

              let ops =
                  [ TreeOp.InsertChild(NodeId "n1", leaf "over-1")
                    TreeOp.InsertChild(NodeId "over-1", leaf "over-2") ]

              Expect.isTrue (Streaming.applyFold ops tree |> isLimitExceeded) "The streamed fold must refuse too"
          }

          test "The refusal renders as the shared LimitExceeded token" {
              // Go and Rust emit the same string, so a client recovering from
              // the refusal need not know which engine refused.
              let tree = chain WireLimits.MaxDepth

              match Apply.apply (TreeOp.InsertChild(NodeId "n1", leaf "over")) tree with
              | Error err ->
                  let rendered = ErrorRender.render (TreeOp.InsertChild(NodeId "n1", leaf "over")) err
                  Expect.stringContains rendered "LimitExceeded" "The rendered envelope carries the shared token"
              | Ok _ -> failtest "Expected a refusal"
          } ]
