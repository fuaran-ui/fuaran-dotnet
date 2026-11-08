module Fuaran.UI.OpStream.Tests.ChainMigrationTests

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.OpStream.TestSupport

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

          test "migrateVerified re-chains a legacy stream into a valid new-format chain, fields preserved" {
              let legacy = buildLegacyChain ops

              let migrated =
                  match ChainMigration.migrateVerified legacy with
                  | Ok m -> m
                  | Error e -> failtestf "a clean legacy stream must migrate: %s" e

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

          test "migrateVerified refuses a stream already in the new format (Phase 1937)" {
              // The unverified `migrate` re-chained anything it was handed; Core's `rehash`
              // verifies the source first, so a stream that is not a legacy chain is named
              // rather than silently re-blessed.
              let migrated =
                  match buildLegacyChain ops |> ChainMigration.migrateVerified with
                  | Ok m -> m
                  | Error e -> failtestf "a clean legacy stream must migrate: %s" e

              match ChainMigration.migrateVerified migrated with
              | Error _ -> ()
              | Ok _ -> failtest "a new-format stream does not verify under the pre-406 format"
          }

          test "migrateLegacyEscape re-chains a pre-287 control-character actor stream (Phase 1937, D30)" {
              let escapedActor = Actor.Agent("m\n", "1\t", "id\r")

              let legacy =
                  (([], HashChain.genesisPreviousHash, 1), ops)
                  ||> List.fold (fun (acc, prev, seq) op ->
                      let stub: OpRecord<TestMsg> =
                          { StreamId = "escape"
                            Sequence = seq
                            PreviousHash = prev
                            Hash = ""
                            Op = op
                            PromptId = None
                            Actor = escapedActor
                            Timestamp = timestamp (1_700_000_000L + int64 seq)
                            ResultEnvelope = OpResultEnvelope.Success }

                      let payload =
                          Fuaran.Core.StreamConfig.legacyEscape.Payload
                              (seq - 1)
                              escapedActor
                              (StreamEntry.encode (StreamEntry.ofRecord stub))

                      let hash = StreamEntry.hashFn prev payload
                      { stub with Hash = hash } :: acc, hash, seq + 1)
                  |> fun (acc, _, _) -> List.rev acc

              Expect.isError (Verify.chain legacy) "the old bytes do not verify under the canonical payload"

              match ChainMigration.migrateLegacyEscape legacy with
              | Error e -> failtestf "a pre-287 stream must migrate: %s" e
              | Ok migrated ->
                  Expect.isOk (Verify.chain migrated) "the migrated stream verifies"
                  Expect.equal (migrated |> List.map _.Actor) (legacy |> List.map _.Actor) "actors preserved"
          }

          test "migrateLegacyEscape leaves a control-free stream's hashes unchanged (Phase 1937, D30)" {
              let records =
                  (([], HashChain.genesisPreviousHash, 1), ops)
                  ||> List.fold (fun (acc, prev, seq) op ->
                      let hash =
                          HashChain.computeHash
                              prev
                              op
                              seq
                              (timestamp 1_700_000_000L)
                              actor
                              None
                              OpResultEnvelope.Success

                      let r: OpRecord<TestMsg> =
                          { StreamId = "plain"
                            Sequence = seq
                            PreviousHash = prev
                            Hash = hash
                            Op = op
                            PromptId = None
                            Actor = actor
                            Timestamp = timestamp 1_700_000_000L
                            ResultEnvelope = OpResultEnvelope.Success }

                      r :: acc, hash, seq + 1)
                  |> fun (acc, _, _) -> List.rev acc

              match ChainMigration.migrateLegacyEscape records with
              | Error e -> failtestf "a control-free stream verifies under both payloads: %s" e
              | Ok migrated ->
                  Expect.equal
                      (migrated |> List.map (fun r -> r.PreviousHash, r.Hash))
                      (records |> List.map (fun r -> r.PreviousHash, r.Hash))
                      "every link and hash unchanged"
          } ]
