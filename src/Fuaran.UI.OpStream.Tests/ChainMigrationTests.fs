module Fuaran.UI.OpStream.Tests.ChainMigrationTests

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.OpStream.Tests.TestSupport

// Phase 406 — the pre-406 -> Core-canonical chain migrator. Builds a stream in the
// FROZEN legacy format (via ChainMigration.legacyChainHash), then asserts the
// migrator verifies it, re-chains it into the new format, and that the result
// verifies + replays identically (the ops are untouched).

let private actor = Actor.Human "migrator"

/// Build a stream in the pre-406 legacy chain format.
let private buildLegacyChain (ops: TreeOp<TestMsg> list) : OpRecord<TestMsg> list =
    (([], HashChain.genesisPreviousHash, 1), ops)
    ||> List.fold (fun (acc, prev, seq) op ->
        let stub: OpRecord<TestMsg> =
            { StreamId = "legacy"
              Sequence = seq
              PreviousHash = prev
              Hash = ""
              Op = op
              PromptId = (if seq % 2 = 0 then Some "prompt-x" else None)
              Actor = actor
              Timestamp = timestamp (1_700_000_000L + int64 seq)
              ResultEnvelope = OpResultEnvelope.Success }

        let hash = ChainMigration.legacyChainHash prev stub
        { stub with Hash = hash } :: acc, hash, seq + 1)
    |> fun (acc, _, _) -> List.rev acc

let private ops: TreeOp<TestMsg> list =
    [ TreeOp.RemoveNode(NodeId "a")
      TreeOp.RemoveNode(NodeId "b")
      TreeOp.RemoveNode(NodeId "c") ]

[<Tests>]
let tests =
    testList
        "Fuaran.UI.OpStream — ChainMigration (Phase 406)"
        [ test "a legacy-format stream verifies under verifyLegacy but NOT under the new Verify.chain" {
              let legacy = buildLegacyChain ops
              Expect.isTrue (ChainMigration.verifyLegacy legacy) "legacy stream verifies under the pre-406 format"

              match Verify.chain legacy with
              | Ok() -> failtest "a legacy chain must NOT verify under the new Core-canonical format"
              | Error _ -> ()
          }

          test "migrate re-chains a legacy stream into a valid new-format chain, fields preserved" {
              let legacy = buildLegacyChain ops
              let migrated = ChainMigration.migrate legacy

              match Verify.chain migrated with
              | Ok() -> ()
              | Error e -> failtestf "migrated stream failed the new Verify.chain: %A" e

              // Every non-hash field is carried over verbatim (op / actor / ts / promptId / result / seq).
              List.zip legacy migrated
              |> List.iter (fun (l, m) ->
                  Expect.equal (CanonicalJson.encodeOp m.Op) (CanonicalJson.encodeOp l.Op) "op preserved"
                  Expect.equal m.Sequence l.Sequence "sequence preserved"
                  Expect.equal m.PromptId l.PromptId "promptId preserved"
                  Expect.equal m.Timestamp l.Timestamp "timestamp preserved"
                  Expect.equal m.Actor l.Actor "actor preserved"
                  Expect.notEqual m.Hash l.Hash "hash re-derived (format bumped)")
          }

          test "migrateVerified refuses a tampered legacy stream" {
              let legacy = buildLegacyChain ops
              // Tamper: re-attribute record 2's actor WITHOUT re-hashing. The actor IS in the
              // legacy pre-image, so this breaks verifyLegacy. (A promptId flip would NOT — that
              // is exactly the provenance hole 406 closes; it is undetectable pre-migration.)
              let tampered =
                  legacy
                  |> List.mapi (fun i r -> if i = 1 then { r with Actor = Actor.Human "forged" } else r)

              match ChainMigration.migrateVerified tampered with
              | Error _ -> ()
              | Ok _ -> failtest "a stream that does not verify under the legacy format must be refused"
          }

          test "migrate is stable — re-migrating an already-new-format chain is a no-op on the hashes" {
              let migrated = buildLegacyChain ops |> ChainMigration.migrate
              let again = ChainMigration.migrate migrated
              Expect.equal (again |> List.map _.Hash) (migrated |> List.map _.Hash) "idempotent"
          } ]
