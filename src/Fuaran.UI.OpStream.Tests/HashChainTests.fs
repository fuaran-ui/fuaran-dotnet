module Fuaran.UI.OpStream.Tests.HashChainTests

open System
open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Tests.TestSupport

/// The actor folded into every fixture hash below (Phase 320).
let private actor = Actor.Human "tester"

/// Phase 406: `computeHash` now folds `promptId` + `resultEnvelope` into the chain.
/// The legacy pre-image-component tests below exercise prev / op / seq / ts / actor,
/// so this shim defaults the two provenance fields; dedicated tests cover them.
let private ch prev op seq ts a =
    HashChain.computeHash prev op seq ts a None OpResultEnvelope.Success

[<Tests>]
let tests =
    testList
        "Fuaran.UI.OpStream — HashChain primitives"
        [ test "genesisPreviousHash is 64 zero characters" {
              Expect.equal HashChain.genesisPreviousHash.Length 64 "Genesis hash is 64 chars"
              Expect.isTrue (HashChain.genesisPreviousHash |> Seq.forall (fun c -> c = '0')) "All characters are '0'"
          }

          // ─── Phase 405 — the portable digest at the chain seam ─────────────
          // `HashChain.sha256Hex` routes through the pure Fable-safe SHA-256
          // (Fuaran.UI.Hashing); these pin FIPS 180-4 vectors + BCL parity AT
          // THIS SEAM so a future re-route cannot silently change chain bytes.

          test "sha256Hex matches the FIPS 180-4 vectors (empty / abc / two-block)" {
              Expect.equal
                  (HashChain.sha256Hex "")
                  "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
                  "empty-string vector"

              Expect.equal
                  (HashChain.sha256Hex "abc")
                  "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
                  "'abc' vector"

              Expect.equal
                  (HashChain.sha256Hex "abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq")
                  "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1"
                  "two-block vector"
          }

          test "sha256Hex matches the BCL byte-for-byte (multi-block, multi-byte UTF-8, boundary lengths)" {
              let bclSha256 (input: string) : string =
                  let bytes = System.Text.Encoding.UTF8.GetBytes(input)
                  let hash = System.Security.Cryptography.SHA256.HashData(bytes)
                  hash |> Array.map (fun b -> b.ToString("x2")) |> String.concat ""

              let corpus =
                  [ ""
                    "a"
                    "abc"
                    String.replicate 55 "x" // one byte under the single-block pad boundary
                    String.replicate 56 "x" // exactly the pad boundary — forces a second block
                    String.replicate 64 "x" // exactly one block
                    String.replicate 200 "chain" // multi-block
                    "héllo wörld — ünïcode ✓ 你好"
                    "emoji surrogate pair: \U0001F389 mixed with ASCII"
                    HashChain.genesisPreviousHash + "{\"op\":\"RemoveNode\"}1" ]

              for input in corpus do
                  Expect.equal (HashChain.sha256Hex input) (bclSha256 input) (sprintf "BCL parity for %A" input)
          }

          test "computeHash produces 64 lower-case hex characters" {
              let op = TreeOp.RemoveNode(NodeId "x"): TreeOp<TestMsg>

              let hash = ch HashChain.genesisPreviousHash op 1 (timestamp 100L) actor

              Expect.equal hash.Length 64 "Hash is 64 chars"

              Expect.isTrue
                  (hash |> Seq.forall (fun c -> (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                  "Hash is lower-case hex"
          }

          test "computeHash is deterministic" {
              let op = TreeOp.RemoveNode(NodeId "x"): TreeOp<TestMsg>

              let a = ch HashChain.genesisPreviousHash op 1 (timestamp 100L) actor

              let b = ch HashChain.genesisPreviousHash op 1 (timestamp 100L) actor

              Expect.equal a b "Same inputs yield same hash"
          }

          test "computeHash differs when previousHash differs" {
              let op = TreeOp.RemoveNode(NodeId "x"): TreeOp<TestMsg>

              let a = ch HashChain.genesisPreviousHash op 1 (timestamp 100L) actor

              let differentPrev = String.replicate 64 "f"
              let b = ch differentPrev op 1 (timestamp 100L) actor
              Expect.notEqual a b "Different previousHash yields different hash"
          }

          test "computeHash differs when sequence differs" {
              let op = TreeOp.RemoveNode(NodeId "x"): TreeOp<TestMsg>

              let a = ch HashChain.genesisPreviousHash op 1 (timestamp 100L) actor

              let b = ch HashChain.genesisPreviousHash op 2 (timestamp 100L) actor

              Expect.notEqual a b "Different sequence yields different hash"
          }

          test "computeHash differs when timestamp differs" {
              let op = TreeOp.RemoveNode(NodeId "x"): TreeOp<TestMsg>

              let a = ch HashChain.genesisPreviousHash op 1 (timestamp 100L) actor

              let b = ch HashChain.genesisPreviousHash op 1 (timestamp 200L) actor

              Expect.notEqual a b "Different timestamp yields different hash"
          }

          test "computeHash differs when op differs structurally" {
              let opA = TreeOp.RemoveNode(NodeId "x"): TreeOp<TestMsg>
              let opB = TreeOp.RemoveNode(NodeId "y"): TreeOp<TestMsg>

              let a = ch HashChain.genesisPreviousHash opA 1 (timestamp 100L) actor

              let b = ch HashChain.genesisPreviousHash opB 1 (timestamp 100L) actor

              Expect.notEqual a b "Different op target yields different hash"
          }

          test "computeHash differs when the actor differs (Phase 320 — attribution is hashed)" {
              let op = TreeOp.RemoveNode(NodeId "x"): TreeOp<TestMsg>

              let asHuman =
                  ch HashChain.genesisPreviousHash op 1 (timestamp 100L) (Actor.Human "alice")

              let asBob =
                  ch HashChain.genesisPreviousHash op 1 (timestamp 100L) (Actor.Human "bob")

              let asAgent =
                  ch HashChain.genesisPreviousHash op 1 (timestamp 100L) (Actor.Agent("claude", "4.8", "alice"))

              Expect.notEqual asHuman asBob "Different human id yields different hash"
              Expect.notEqual asHuman asAgent "Human vs Agent (same id) yields different hash"
          }

          // ─── Phase 406 — the provenance hole is closed ─────────────────────
          // PromptId + ResultEnvelope are now folded into the chain pre-image, so
          // re-attributing an op to a different prompt or flipping its recorded
          // outcome breaks Verify.chain. Pre-406 both were OUTSIDE the hash.

          test "computeHash differs when the promptId differs (Phase 406 — attribution to a prompt is hashed)" {
              let op = TreeOp.RemoveNode(NodeId "x"): TreeOp<TestMsg>
              let ts = timestamp 100L

              let none =
                  HashChain.computeHash HashChain.genesisPreviousHash op 1 ts actor None OpResultEnvelope.Success

              let promptA =
                  HashChain.computeHash
                      HashChain.genesisPreviousHash
                      op
                      1
                      ts
                      actor
                      (Some "prompt-a")
                      OpResultEnvelope.Success

              let promptB =
                  HashChain.computeHash
                      HashChain.genesisPreviousHash
                      op
                      1
                      ts
                      actor
                      (Some "prompt-b")
                      OpResultEnvelope.Success

              Expect.notEqual none promptA "Absent vs present promptId yields different hash"
              Expect.notEqual promptA promptB "Different promptId yields different hash"
          }

          test "computeHash differs when the resultEnvelope differs (Phase 406 — apply outcome is hashed)" {
              let op = TreeOp.RemoveNode(NodeId "x"): TreeOp<TestMsg>
              let ts = timestamp 100L

              let success =
                  HashChain.computeHash HashChain.genesisPreviousHash op 1 ts actor None OpResultEnvelope.Success

              let failure =
                  HashChain.computeHash
                      HashChain.genesisPreviousHash
                      op
                      1
                      ts
                      actor
                      None
                      (OpResultEnvelope.Failure("KindMismatch", "boom"))

              Expect.notEqual
                  success
                  failure
                  "Flipping Success -> Failure breaks the chain hash (was undetectable pre-406)"
          }

          // ─── Phase 411 — F14 resolved: Core's verifier verifies UI chains ──
          // The pre-image folds Core's 0-based record index, so a UI record maps
          // onto Core's shape via `Seq = Sequence - 1` and Core's own
          // `firstChainBreakWith` (with the domain's 64-zero genesis via
          // StreamConfig) verifies the chain with NO domain walker.

          test "Core.OpStream.firstChainBreakWith verifies a UI-built chain via the record adapter (F14 closed)" {
              let op1 = TreeOp.RemoveNode(NodeId "x"): TreeOp<TestMsg>
              let op2 = TreeOp.RemoveNode(NodeId "y"): TreeOp<TestMsg>
              let first = buildRecord "stream-1" 1 op1 None (timestamp 100L)
              let second = buildRecord "stream-1" 2 op2 (Some first) (timestamp 200L)
              let coreRecords = [ first; second ] |> List.map StreamEntry.toCoreRecord

              let breakOpt =
                  Fuaran.Core.OpStream.firstChainBreakWith
                      HashChain.chainConfig
                      StreamEntry.hashFn
                      (StreamEntry.coreWitness<TestMsg> ())
                      coreRecords

              Expect.isNone breakOpt "Core's verifier accepts the UI chain directly"

              // And Core catches a tamper on the mapped records too.
              let tampered =
                  coreRecords
                  |> List.map (fun r ->
                      if r.Seq = 1 then
                          { r with
                              Actor = Fuaran.Core.Actor.Human "mallory" }
                      else
                          r)

              let tamperBreak =
                  Fuaran.Core.OpStream.firstChainBreakWith
                      HashChain.chainConfig
                      StreamEntry.hashFn
                      (StreamEntry.coreWitness<TestMsg> ())
                      tampered

              Expect.isSome tamperBreak "Core's verifier catches a re-attributed record on the UI chain"
          }

          test "Verify.chain reports 1-based sequences through the Core delegation" {
              let op1 = TreeOp.RemoveNode(NodeId "x"): TreeOp<TestMsg>
              let op2 = TreeOp.RemoveNode(NodeId "y"): TreeOp<TestMsg>
              let op3 = TreeOp.RemoveNode(NodeId "z"): TreeOp<TestMsg>
              let r1 = buildRecord "s" 1 op1 None (timestamp 100L)
              let r2 = buildRecord "s" 2 op2 (Some r1) (timestamp 200L)
              let r3 = buildRecord "s" 3 op3 (Some r2) (timestamp 300L)

              // Drop the middle record: the gap is at the domain's sequence 2.
              match Verify.chain [ r1; r3 ] with
              | Error(VerificationError.OutOfOrder(expected, actual)) ->
                  Expect.equal expected 2 "expected sequence is 1-based"
                  Expect.equal actual 3 "actual sequence is 1-based"
              | other -> failtestf "expected OutOfOrder, got %A" other

              // Re-attribute the third record: HashMismatch at the domain's sequence 3.
              match
                  Verify.chain
                      [ r1
                        r2
                        { r3 with
                            Actor = Actor.Human "mallory" } ]
              with
              | Error(VerificationError.HashMismatch(seq, _, _)) -> Expect.equal seq 3 "1-based sequence in the error"
              | other -> failtestf "expected HashMismatch, got %A" other

              // Break the prev-link on the second record.
              match
                  Verify.chain
                      [ r1
                        { r2 with
                            PreviousHash = String.replicate 64 "f" } ]
              with
              | Error(VerificationError.PreviousHashMismatch(seq, _, _)) ->
                  Expect.equal seq 2 "1-based sequence in the error"
              | other -> failtestf "expected PreviousHashMismatch, got %A" other
          }

          test "buildRecord constructs a clean genesis record" {
              let op = TreeOp.RemoveNode(NodeId "x"): TreeOp<TestMsg>
              let record = buildRecord "stream-1" 1 op None (timestamp 100L)

              Expect.equal record.PreviousHash HashChain.genesisPreviousHash "Genesis prev is the all-zeros constant"
              Expect.equal record.Sequence 1 "Sequence is 1"
              Expect.equal record.Hash.Length 64 "Hash populated"
          }

          test "buildRecord links second record to first via PreviousHash = first.Hash" {
              let op1 = TreeOp.RemoveNode(NodeId "x"): TreeOp<TestMsg>
              let op2 = TreeOp.RemoveNode(NodeId "y"): TreeOp<TestMsg>
              let first = buildRecord "stream-1" 1 op1 None (timestamp 100L)
              let second = buildRecord "stream-1" 2 op2 (Some first) (timestamp 200L)

              Expect.equal second.PreviousHash first.Hash "Second's PreviousHash links to first's Hash"
              Expect.notEqual second.Hash first.Hash "Second's Hash differs from first's"
          }

          test "Verify.chain accepts a clean 3-record chain" {
              let op1 = TreeOp.RemoveNode(NodeId "x"): TreeOp<TestMsg>
              let op2 = TreeOp.RemoveNode(NodeId "y"): TreeOp<TestMsg>
              let op3 = TreeOp.RemoveNode(NodeId "z"): TreeOp<TestMsg>
              let r1 = buildRecord "stream-1" 1 op1 None (timestamp 100L)
              let r2 = buildRecord "stream-1" 2 op2 (Some r1) (timestamp 200L)
              let r3 = buildRecord "stream-1" 3 op3 (Some r2) (timestamp 300L)

              match Verify.chain [ r1; r2; r3 ] with
              | Ok() -> ()
              | Error e -> failtestf "Expected Ok, got Error %A" e
          }

          test "Verify.chain rejects a record whose actor was altered after hashing (tamper-evident)" {
              let op = TreeOp.RemoveNode(NodeId "x"): TreeOp<TestMsg>
              let r1 = buildRecord "stream-1" 1 op None (timestamp 100L)
              // Re-attribute the op without recomputing the hash — the chain must break.
              let tampered =
                  { r1 with
                      Actor = Actor.Human "mallory" }

              match Verify.chain [ tampered ] with
              | Ok() -> failtest "Expected the re-attributed record to break the chain"
              | Error(VerificationError.HashMismatch _) -> ()
              | Error e -> failtestf "Expected HashMismatch, got %A" e
          } ]
