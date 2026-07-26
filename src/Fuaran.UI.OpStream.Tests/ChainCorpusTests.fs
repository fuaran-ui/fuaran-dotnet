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
let private corpusRoot () : string =
    let rec walk (dir: DirectoryInfo) =
        if isNull dir then
            failwith "wire-format-fixtures/ not found walking up — the Fuaran workspace checkout is required."
        else
            let candidate = Path.Combine(dir.FullName, "wire-format-fixtures", "manifest.json")

            if File.Exists candidate then
                Path.Combine(dir.FullName, "wire-format-fixtures")
            else
                walk dir.Parent

    walk (DirectoryInfo(AppContext.BaseDirectory))

let private root = corpusRoot ()

let private readFixture (rel: string) =
    File.ReadAllText(Path.Combine(root, rel))

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
          } ]
