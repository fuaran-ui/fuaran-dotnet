module Fuaran.UI.OpStream.Tests.ChainCorpusTests

// System.Text.Json's GetString() is nullable; this test parses a controlled,
// committed fixture where every field is present, so the null cases are dead.
#nowarn "3261"

// Phase 407 — the shared cross-host chain corpus. The F# host asserts it
// reproduces `wire-format-fixtures/chain/chain-corpus.json` byte-for-byte; the
// TS host asserts the SAME file (op-stream/test/parity.test.ts). The corpus is
// the direct F#↔TS anchor for the Phase-406 chain formula — a drift on either
// host that changes a hash fails against the committed golden.

open System
open System.IO
open System.Text.Json
open Expecto
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types

/// Walk up from the test assembly until the workspace `wire-format-fixtures/`
/// corpus is found (a sibling of the `fuaran-dotnet/` repo).
/// `None` when the corpus is not there — a bare single-repo clone, or a git
/// worktree checked out elsewhere in the filesystem.
///
/// Returning an option rather than throwing is load-bearing: bound at module
/// level, a throw here happens in the TYPE INITIALIZER, so the runtime wraps it
/// in a `TargetInvocationException` and takes the WHOLE test assembly down —
/// reporting every op-stream test as broken where the truth is that these few
/// need the workspace checkout. A missing input degrades to a skip; it never
/// masks the tests beside it.
let private tryCorpusRoot () : string option = Fuaran.Tests.CorpusRoot.tryFind ()

let private root = tryCorpusRoot ()

let private readFixture (rel: string) =
    match root with
    | Some r -> File.ReadAllText(Path.Combine(r, rel))
    | None ->
        skiptest
            "wire-format-fixtures/ not found walking up from the test assembly — this chain-corpus test needs the workspace checkout (skipped in a bare single-repo clone or a worktree elsewhere)"

let private actorOf (e: JsonElement) : Actor =
    match e.GetProperty("kind").GetString() with
    | "human" -> Actor.Human(e.GetProperty("id").GetString())
    | "agent" ->
        Actor.Agent(
            e.GetProperty("model").GetString(),
            e.GetProperty("version").GetString(),
            e.GetProperty("id").GetString()
        )
    | k -> failwithf "unknown actor kind %s" k

let private resultOf (e: JsonElement) : OpResultEnvelope =
    match e.GetProperty("kind").GetString() with
    | "success" -> OpResultEnvelope.Success
    | "failure" -> OpResultEnvelope.Failure(e.GetProperty("code").GetString(), e.GetProperty("message").GetString())
    | k -> failwithf "unknown result kind %s" k

let private promptOf (e: JsonElement) : string option =
    if e.ValueKind = JsonValueKind.Null then
        None
    else
        Some(e.GetString())

[<Tests>]
let tests =
    testList
        "Fuaran.UI.OpStream — shared chain corpus (Phase 407)"
        [ test "every record reproduces the committed cross-host golden byte-for-byte" {
              let doc = JsonDocument.Parse(readFixture "chain/chain-corpus.json")
              let recs = doc.RootElement.GetProperty("records")
              Expect.isGreaterThan (recs.GetArrayLength()) 0 "corpus is non-empty"

              let mutable prev = HashChain.genesisPreviousHash

              for r in recs.EnumerateArray() do
                  let opFixture = r.GetProperty("opFixture").GetString()
                  let sequence = r.GetProperty("sequence").GetInt32()

                  let ts =
                      DateTimeOffset.FromUnixTimeSeconds(r.GetProperty("timestampUnixSeconds").GetInt64())

                  let actor = actorOf (r.GetProperty("actor"))
                  let promptId = promptOf (r.GetProperty("promptId"))
                  let result = resultOf (r.GetProperty("result"))

                  Expect.equal (r.GetProperty("previousHash").GetString()) prev (sprintf "%s prev-link" opFixture)

                  let op =
                      match Fuaran.UI.Ops.JsonDecode.decodeOp (readFixture opFixture) with
                      | Ok op -> op
                      | Error e -> failtestf "decode %s failed: %A" opFixture e

                  let hash = HashChain.computeHash prev op sequence ts actor promptId result
                  Expect.equal hash (r.GetProperty("hash").GetString()) (sprintf "%s hash matches the golden" opFixture)
                  prev <- hash
          }

          test "the chain format version is folded first into the pre-image and round-trips through formatVersion" {
              let entry: StreamEntry<obj> =
                  { Op = TreeOp.RemoveNode(NodeId "n")
                    Timestamp = DateTimeOffset.FromUnixTimeSeconds 1700000000L
                    PromptId = None
                    ResultEnvelope = OpResultEnvelope.Success }

              let encoded = StreamEntry.encode entry

              Expect.stringStarts
                  encoded
                  (sprintf "{\"v\":%d," StreamEntry.chainFormatVersion)
                  "`v` (the chain format version) is the pinned FIRST field of the pre-image envelope"

              Expect.equal
                  (StreamEntry.formatVersion encoded)
                  (Some StreamEntry.chainFormatVersion)
                  "formatVersion lifts the version from an encoded envelope"

              Expect.equal
                  (StreamEntry.formatVersion "{\"op\":{},\"ts\":0}")
                  None
                  "a tagless (pre-406 / v1) envelope reports no version — the reader treats it as v1"

              Expect.equal
                  StreamEntry.chainFormatVersion
                  2
                  "the shipped chain format is v2 (Phase 406/411 + chainVersion)"
          }

          // Phase 2305 — the checkpoint the corpus seals by Core's strict rule, and the tail-break
          // vectors over the records above it. The reference seals and verifies through
          // `CheckpointSeal`, which is Core's `Sealed.seal` / `Sealed.firstBreak`.
          test "the corpus checkpoint reproduces Core's seal, and every tail-break vector breaks where it names" {
              let chainDoc = JsonDocument.Parse(readFixture "chain/chain-corpus.json")
              let cpDoc = JsonDocument.Parse(readFixture "chain/checkpoint-corpus.json")

              let records =
                  [ for r in chainDoc.RootElement.GetProperty("records").EnumerateArray() ->
                        let op =
                            match
                                Fuaran.UI.Ops.JsonDecode.decodeOp (readFixture (r.GetProperty("opFixture").GetString()))
                            with
                            | Ok op -> op
                            | Error e -> failtestf "decode failed: %A" e

                        let record: OpRecord<obj> =
                            { StreamId = "corpus"
                              Sequence = r.GetProperty("sequence").GetInt32()
                              PreviousHash = r.GetProperty("previousHash").GetString()
                              Hash = r.GetProperty("hash").GetString()
                              Op = op
                              PromptId = promptOf (r.GetProperty("promptId"))
                              Actor = actorOf (r.GetProperty("actor"))
                              Timestamp =
                                DateTimeOffset.FromUnixTimeSeconds(r.GetProperty("timestampUnixSeconds").GetInt64())
                              ResultEnvelope = resultOf (r.GetProperty("result")) }

                        record ]

              let cpEl = cpDoc.RootElement.GetProperty("checkpoint")
              let sequence = cpEl.GetProperty("sequence").GetInt32()
              let head = cpEl.GetProperty("previousChainHead").GetString()

              Expect.equal head records[sequence - 1].Hash "the checkpoint hangs from the record at its sequence"

              let state =
                  match Fuaran.UI.Ops.JsonDecode.decodeNodeObj (cpEl.GetProperty("state").GetRawText()) with
                  | Ok n -> n
                  | Error e -> failtestf "state decode failed: %A" e

              let checkpoint =
                  CheckpointSeal.create "corpus" sequence head (DateTimeOffset.FromUnixTimeSeconds 0L) state

              Expect.equal
                  checkpoint.SnapshotHash
                  (cpEl.GetProperty("snapshotHash").GetString())
                  "the corpus seal is Core's strict snapshot seal, byte for byte"

              let tail = records |> List.skip sequence
              let vectors = cpDoc.RootElement.GetProperty("vectors")
              Expect.isGreaterThan (vectors.GetArrayLength()) 0 "the corpus carries vectors"

              for v in vectors.EnumerateArray() do
                  let id = v.GetProperty("id").GetString()
                  let mutation = v.GetProperty("mutation")

                  let index () =
                      mutation.GetProperty("index").GetInt32()

                  let cp, tail' =
                      match mutation.GetProperty("kind").GetString() with
                      | "none" -> checkpoint, tail
                      | "snapshotHash" ->
                          { checkpoint with
                              SnapshotHash = mutation.GetProperty("value").GetString() },
                          tail
                      | "dropRecord" -> checkpoint, List.removeAt (index ()) tail
                      | "recordMember" ->
                          let i = index ()
                          let value = mutation.GetProperty("value")

                          let edited =
                              match mutation.GetProperty("member").GetString() with
                              | "previousHash" ->
                                  { tail[i] with
                                      PreviousHash = value.GetString() }
                              | "timestampUnixSeconds" ->
                                  { tail[i] with
                                      Timestamp = DateTimeOffset.FromUnixTimeSeconds(value.GetInt64()) }
                              | m -> failtestf "%s: unknown member %s" id m

                          checkpoint, List.updateAt i edited tail
                      | k -> failtestf "%s: unknown mutation %s" id k

                  let expect = v.GetProperty("expect")
                  let found = CheckpointSeal.firstBreak cp tail'

                  match expect.GetProperty("break").GetString(), found with
                  | "none", None -> ()
                  | "seal", Some(Fuaran.Core.SnapshotBreak.SnapshotHash _) -> ()
                  | "tail", Some(Fuaran.Core.SnapshotBreak.Tail b) ->
                      Expect.equal
                          b.Index
                          (expect.GetProperty("index").GetInt32())
                          (id + ": the break's index in the tail")

                      let reason =
                          match b.Reason with
                          | Fuaran.Core.ChainBreakReason.SequenceMismatch -> "sequenceMismatch"
                          | Fuaran.Core.ChainBreakReason.PrevHashLinkBroken -> "prevHashLinkBroken"
                          | Fuaran.Core.ChainBreakReason.HashMismatch -> "hashMismatch"
                          | Fuaran.Core.ChainBreakReason.Unrecognised r -> r

                      Expect.equal reason (expect.GetProperty("reason").GetString()) (id + ": the break's reason")
                  | want, got -> failtestf "%s: expected a %s break, found %A" id want got
          } ]
