module Fuaran.UI.OpStream.Tests.TamperTests

open System
open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Tests.TestSupport

// ============================================================================
//  Verify.chain rejects every tamper class — modifying any of the five
//  fields that feed into the hash (Hash, PreviousHash, Op, Sequence,
//  Timestamp) must produce a VerificationError. Plus an out-of-order check.
// ============================================================================

let private makeCleanChain () : OpRecord<TestMsg> list =
    let op1 = TreeOp.RemoveNode(NodeId "x"): TreeOp<TestMsg>
    let op2 = TreeOp.RemoveNode(NodeId "y"): TreeOp<TestMsg>
    let r1 = buildRecord "stream-1" 1 op1 None (timestamp 100L)
    let r2 = buildRecord "stream-1" 2 op2 (Some r1) (timestamp 200L)
    [ r1; r2 ]

[<Tests>]
let tests =
    testList
        "Fuaran.UI.OpStream — Tamper detection"
        [ test "Verify.chain rejects a tampered Hash" {
              let chain = makeCleanChain ()

              let tampered =
                  { chain[1] with
                      Hash = String.replicate 64 "0" }

              match Verify.chain [ chain[0]; tampered ] with
              | Error(VerificationError.HashMismatch _) -> ()
              | other -> failtestf "Expected HashMismatch, got %A" other
          }

          test "Verify.chain rejects a tampered PreviousHash" {
              let chain = makeCleanChain ()

              let tampered =
                  { chain[1] with
                      PreviousHash = String.replicate 64 "f" }

              match Verify.chain [ chain[0]; tampered ] with
              | Error(VerificationError.PreviousHashMismatch _) -> ()
              | other -> failtestf "Expected PreviousHashMismatch, got %A" other
          }

          test "Verify.chain rejects a tampered Op" {
              let chain = makeCleanChain ()
              // Substitute a different RemoveNode target; Hash stays from the
              // ORIGINAL op, so recompute fails.
              let tampered =
                  { chain[1] with
                      Op = TreeOp.RemoveNode(NodeId "tampered") }

              match Verify.chain [ chain[0]; tampered ] with
              | Error(VerificationError.HashMismatch _) -> ()
              | other -> failtestf "Expected HashMismatch, got %A" other
          }

          test "Verify.chain rejects a tampered Sequence" {
              let chain = makeCleanChain ()
              // Renumber record 2's Sequence to 99 — recompute fails because
              // the hash was computed with sequence=2.
              let tampered = { chain[1] with Sequence = 99 }

              match Verify.chain [ chain[0]; tampered ] with
              | Error(VerificationError.OutOfOrder _) -> ()
              | Error(VerificationError.HashMismatch _) -> ()
              | other -> failtestf "Expected OutOfOrder or HashMismatch, got %A" other
          }

          test "Verify.chain rejects a tampered Timestamp" {
              let chain = makeCleanChain ()
              // Hash was computed for unix=200; renumber to unix=500.
              let tampered =
                  { chain[1] with
                      Timestamp = timestamp 500L }

              match Verify.chain [ chain[0]; tampered ] with
              | Error(VerificationError.HashMismatch _) -> ()
              | other -> failtestf "Expected HashMismatch, got %A" other
          }

          test "Verify.chain rejects out-of-order records" {
              let chain = makeCleanChain ()
              // Swap the two records.
              match Verify.chain [ chain[1]; chain[0] ] with
              | Error(VerificationError.OutOfOrder _) -> ()
              | Error _ -> () // PreviousHash mismatch is also acceptable since the chain link is broken.
              | Ok() -> failtest "Expected Error, got Ok ()"
          } ]
