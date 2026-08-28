module Fuaran.UI.OpStream.Tests.StreamEntryTests

open System
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  StreamEntry.decode (the Phase 409 tail) — the inverse of the pinned
//  provenance-envelope encoding.
//
//  The headline property is `decode ∘ encode = id` over an op corpus. Two
//  things make it worth more than a shape check:
//
//   * The envelope's `op` is arbitrary canonical JSON. The corpus therefore
//     includes ops whose payloads carry the envelope's OWN key names
//     (`"ts"`, `"result"`, `"promptId"`, `"v"`) as literal string content, and
//     a nested `Batch` — the shapes a whole-string `IndexOf` scan (the
//     `Actor.tryDecode` technique, correct for a flat payload) would read an
//     envelope field out of.
//   * The round-trip is asserted at the BYTES, not just the record: re-encoding
//     a decoded entry must reproduce the original envelope exactly, which is
//     the property the chain actually depends on (the pre-image is the bytes).
// ============================================================================

let private ts (unix: int64) : DateTimeOffset = DateTimeOffset.FromUnixTimeSeconds unix

/// The obj-typed op decoder — the language tier's wire decoder with its
/// structured error flattened, the shape `StreamEntry.decode` takes.
let private decodeOp (json: string) : Result<TreeOp<obj>, string> =
    JsonDecode.decodeOp json
    |> Result.mapError (fun e -> sprintf "%s at %s: %s" e.Code e.Path e.Message)

let private style: SemanticStyle =
    { Defaults.style with
        Tone = ToneVariant.Success }

/// The op corpus. The last three are the adversarial ones: their payloads
/// contain the envelope's own field names as literal text.
let private opCorpus: TreeOp<obj> list =
    [ TreeOp.RemoveNode(NodeId "n1")
      TreeOp.UpdateStyle(NodeId "n1", style)
      TreeOp.InsertChild(NodeId "dash", Fuaran.markdown "note" "A note")
      TreeOp.MoveNode(NodeId "left", NodeId "dash")
      TreeOp.ReorderChildren(NodeId "dash", [ NodeId "right"; NodeId "left" ])
      TreeOp.Batch
          [ TreeOp.RemoveNode(NodeId "a")
            TreeOp.Batch [ TreeOp.RemoveNode(NodeId "b"); TreeOp.RemoveNode(NodeId "c") ] ]
      // Envelope key names as literal payload content.
      TreeOp.InsertChild(NodeId "dash", Fuaran.markdown "k1" "{\"ts\":1,\"result\":{\"kind\":\"failure\"},\"v\":9}")
      TreeOp.InsertChild(NodeId "dash", Fuaran.markdown "k2" "\"promptId\": not really a field")
      // Escapes the encoder writes as \uXXXX, plus quotes and backslashes.
      TreeOp.InsertChild(NodeId "dash", Fuaran.markdown "k3" "tab\there \"quoted\" back\\slash newline\nend") ]

let private lineageCorpus: (string option * OpResultEnvelope) list =
    [ None, OpResultEnvelope.Success
      Some "prompt-1", OpResultEnvelope.Success
      Some "prompt with \"quotes\" and \\ backslash", OpResultEnvelope.Success
      None, OpResultEnvelope.Failure("APPLY_REFUSED", "no such node")
      Some "p2", OpResultEnvelope.Failure("WRONG_TYPE", "expected a \"Binding\", got\na literal") ]

/// The cross product of ops × lineage × a couple of timestamps — the corpus the
/// round-trip property quantifies over.
let private entryCorpus: StreamEntry<obj> list =
    [ for op in opCorpus do
          for promptId, envelope in lineageCorpus do
              for unix in [ 0L; 1_700_000_000L ] do
                  { Op = op
                    Timestamp = ts unix
                    PromptId = promptId
                    ResultEnvelope = envelope } ]

[<Tests>]
let tests =
    testList
        "Phase409.StreamEntry.decode"
        [ test "decode ∘ encode = id over the op × lineage corpus" {
              Expect.isGreaterThan (List.length entryCorpus) 50 "the corpus is not degenerate"

              for entry in entryCorpus do
                  let encoded = StreamEntry.encode entry

                  match StreamEntry.decode decodeOp encoded with
                  | Error e -> failtestf "decode failed for %s: %s" encoded e
                  | Ok decoded ->
                      Expect.equal decoded.Timestamp entry.Timestamp "timestamp round-trips"
                      Expect.equal decoded.PromptId entry.PromptId "promptId round-trips"
                      Expect.equal decoded.ResultEnvelope entry.ResultEnvelope "result envelope round-trips"
                      // The bytes are the contract — the chain pre-image IS this
                      // string, so an equal-looking record with different bytes
                      // would still break every downstream hash.
                      Expect.equal
                          (StreamEntry.encode decoded)
                          encoded
                          "re-encoding reproduces the envelope byte-for-byte"
          }

          test "the nested op is handed to the host decoder byte-for-byte" {
              // A whole-string scan would find the payload's own "ts" first.
              let entry =
                  { Op = TreeOp.InsertChild(NodeId "dash", Fuaran.markdown "k" "{\"ts\":99999}")
                    Timestamp = ts 1_700_000_000L
                    PromptId = None
                    ResultEnvelope = OpResultEnvelope.Success }

              match StreamEntry.decode decodeOp (StreamEntry.encode entry) with
              | Error e -> failtestf "decode failed: %s" e
              | Ok decoded ->
                  Expect.equal decoded.Timestamp entry.Timestamp "the envelope's ts wins over the payload's"

                  Expect.equal
                      (CanonicalJson.encodeOp decoded.Op)
                      (CanonicalJson.encodeOp entry.Op)
                      "the op survives intact"
          }

          test "an unsupported chain format version is refused by name" {
              let encoded =
                  StreamEntry.encode
                      { Op = TreeOp.RemoveNode(NodeId "n1")
                        Timestamp = ts 1L
                        PromptId = None
                        ResultEnvelope = OpResultEnvelope.Success }

              let bumped = encoded.Replace("{\"v\":2", "{\"v\":99")

              match StreamEntry.decode decodeOp bumped with
              | Ok _ -> failtest "a v99 envelope must be refused"
              | Error e -> Expect.stringContains e "unsupported chain format version 99" "the refusal names the version"

              // The tagless pre-406 form reads as v1 (per `formatVersion`), which
              // this host also does not implement — and must say so rather than
              // parse it as if it were v2.
              let tagless = encoded.Replace("{\"v\":2,", "{")

              match StreamEntry.decode decodeOp tagless with
              | Ok _ -> failtest "a tagless (v1) envelope must be refused"
              | Error e -> Expect.stringContains e "version 1" "the refusal names v1"
          }

          test "a malformed envelope is a named Error, never an exception" {
              let cases =
                  [ "", "not an object"
                    "{}", "no fields at all"
                    "{\"v\":2,\"ts\":1,\"result\":{\"kind\":\"success\"}}", "no op"
                    "{\"v\":2,\"op\":{\"$type\":\"RemoveNode\",\"target\":\"n\"},\"result\":{\"kind\":\"success\"}}",
                    "no ts"
                    "{\"v\":2,\"op\":{\"$type\":\"RemoveNode\",\"target\":\"n\"},\"ts\":\"soon\",\"result\":{\"kind\":\"success\"}}",
                    "non-integer ts"
                    "{\"v\":2,\"op\":{\"$type\":\"RemoveNode\",\"target\":\"n\"},\"ts\":1,\"result\":{\"kind\":\"maybe\"}}",
                    "unrecognised result kind"
                    "{\"v\":2,\"op\":{\"$type\":\"RemoveNode\",\"target\":\"n\"},\"ts\":1,\"result\":{\"kind\":\"failure\"}}",
                    "failure without code/message" ]

              for payload, why in cases do
                  match StreamEntry.decode decodeOp payload with
                  | Ok _ -> failtestf "expected a refusal (%s) for: %s" why payload
                  | Error e -> Expect.stringContains e "StreamEntry.decode" (sprintf "the error is attributed (%s)" why)
          }

          test "the Core witness decodes through Core.OpStream's JSONL" {
              // The point of the decode leg: the witness now satisfies the whole
              // `StreamWitness` contract, so Core's list operations work over it.
              let entries =
                  [ { Op = TreeOp.RemoveNode(NodeId "a")
                      Timestamp = ts 1_700_000_001L
                      PromptId = Some "p1"
                      ResultEnvelope = OpResultEnvelope.Success }
                    { Op = TreeOp.RemoveNode(NodeId "b")
                      Timestamp = ts 1_700_000_002L
                      PromptId = None
                      ResultEnvelope = OpResultEnvelope.Failure("E", "m") } ]

              let witness = StreamEntry.coreWitnessWith decodeOp

              let coreRecords: Fuaran.Core.OpRecord<StreamEntry<obj>> list =
                  entries
                  |> List.mapi (fun i e ->
                      { Seq = i
                        Actor = Fuaran.Core.Actor.Human "tester"
                        Op = e
                        PrevHash = string i
                        Hash = string (i + 1) })

              let jsonl = Fuaran.Core.OpStream.toJsonl witness coreRecords

              match Fuaran.Core.OpStream.fromJsonl witness jsonl with
              | Error e -> failtestf "Core.fromJsonl failed: %s" e
              | Ok back ->
                  // `TreeOp<obj>` carries closures, so `StreamEntry` has no
                  // structural equality — compare the canonical encodings, which
                  // is the contract anyway.
                  Expect.equal
                      (back |> List.map (fun r -> StreamEntry.encode r.Op))
                      (entries |> List.map StreamEntry.encode)
                      "the entries round-trip through Core's JSONL"

                  Expect.equal (Fuaran.Core.OpStream.toJsonl witness back) jsonl "re-emitting reproduces the JSONL"
          }

          test "the refusing verify-path witness says so" {
              let witness = StreamEntry.coreWitness<obj> ()

              // A WELL-FORMED envelope, so the refusal comes from the witness's
              // op decoder rather than from the envelope guards in front of it.
              let wellFormed =
                  StreamEntry.encode
                      { Op = TreeOp.RemoveNode(NodeId "n1")
                        Timestamp = ts 1L
                        PromptId = None
                        ResultEnvelope = OpResultEnvelope.Success }

              match witness.Decode wellFormed with
              | Ok _ -> failtest "coreWitness must not decode"
              | Error e -> Expect.stringContains e "coreWitnessWith" "the refusal points at the decoding witness"
          } ]
