module Fuaran.UI.OpStream.Dag.Tests.WireTests

open System
open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions

// ============================================================================
//  DagOpRecord wire forward-coupling (Phase 178 task — F# side).
//
//  The DAG record wire shape is an additive artifact: the linear OpRecord +
//  Node/TreeOp corpus are untouched. The `op` field nests the REAL canonical
//  TreeOp JSON (`CanonicalJson.encodeOp`), so decode routes the nested op
//  through the canonical `Fuaran.UI.Ops.JsonDecode.decodeOp` — the same
//  structural decoder the linear wire path uses. These tests lock:
//   1. round-trip — encode then decode reproduces the record,
//   2. byte-golden — a known record encodes to the exact canonical bytes the
//      cross-host conformance corpus pins (the TS @fuaran-ui/ops codec must
//      reproduce these byte-for-byte),
//   3. optional outcomeHash / promptId omission, and the multi-parent +
//      outcome-hash merge-node shape.
// ============================================================================

/// The host op-decoder used to decode the nested canonical TreeOp.
let private decodeOp (s: string) : Result<TreeOp<obj>, string> =
    JsonDecode.decodeOp s |> Result.mapError (sprintf "%A")

let private mkRecord
    (streamId: string)
    (hash: string)
    (parents: string list)
    (op: TreeOp<obj>)
    (outcome: string option)
    (prompt: string option)
    (unix: int64)
    (tombstoned: bool)
    : DagOpRecord<obj> =
    { StreamId = streamId
      Hash = hash
      Parents = parents
      Op = op
      OutcomeHash = outcome
      PromptId = prompt
      Actor = Actor.Human "u1"
      Timestamp = DateTimeOffset.FromUnixTimeSeconds unix
      ResultEnvelope = OpResultEnvelope.Success
      Tombstoned = tombstoned }

[<Tests>]
let tests =
    testList
        "Dag.Wire"
        [ test "encode then decode round-trips an ordinary record" {
              let r =
                  mkRecord "stream-1" "h1" [ "p0" ] (TreeOp.RemoveNode(NodeId "n1")) None None 1700000000L false

              let json = DagWire.encodeRecord r

              match DagWire.decodeRecord decodeOp json with
              | Ok decoded ->
                  Expect.equal decoded.Hash r.Hash "hash"
                  Expect.equal decoded.Parents r.Parents "parents"
                  Expect.equal decoded.StreamId r.StreamId "streamId"
                  Expect.equal decoded.OutcomeHash None "no outcome hash on an ordinary node"
                  Expect.equal (decoded.Timestamp.ToUnixTimeSeconds()) 1700000000L "timestamp"
                  Expect.isFalse decoded.Tombstoned "live"
                  Expect.equal (DagWire.encodeRecord decoded) json "re-encode is byte-stable"
              | Error e -> failtestf "decode failed: %s" e
          }

          test "a multi-parent merge node round-trips its parents (author order) + outcome hash" {
              let pa = String.replicate 64 "a"
              let pb = String.replicate 64 "b"
              let oh = String.replicate 64 "c"

              let merge =
                  mkRecord "stream-1" "mh" [ pa; pb ] (TreeOp.Batch []) (Some oh) None 1700000001L false

              let json = DagWire.encodeRecord merge

              match DagWire.decodeRecord decodeOp json with
              | Ok decoded ->
                  Expect.equal decoded.Parents [ pa; pb ] "parents in author order"
                  Expect.equal decoded.OutcomeHash (Some oh) "outcome hash present"
              | Error e -> failtestf "merge decode failed: %s" e
          }

          test "byte-golden: a known ordinary record encodes to the pinned canonical bytes" {
              let r =
                  mkRecord "s1" "h1" [ "p1"; "p2" ] (TreeOp.RemoveNode(NodeId "n1")) None None 1700000000L false

              let expected =
                  """{"actor":{"kind":"human","id":"u1"},"hash":"h1","op":{"$type":"RemoveNode","target":"n1"},"parents":["p1","p2"],"resultEnvelope":{"$type":"Success"},"streamId":"s1","timestamp":1700000000,"tombstoned":false}"""

              Expect.equal (DagWire.encodeRecord r) expected "canonical bytes pinned"
          }

          test "byte-golden: an AGENT actor nests its pinned member order" {
              // `Actor.encode` pins `kind` first, then the case fields — NOT
              // Ordinal-sorted. The nested value embeds verbatim, exactly as the
              // nested op does, so this is what the TS host must reproduce.
              let r =
                  { mkRecord "s1" "h1" [] (TreeOp.RemoveNode(NodeId "n1")) None None 1700000000L false with
                      Actor = Actor.Agent("claude", "4.8", "planner") }

              Expect.stringContains
                  (DagWire.encodeRecord r)
                  """{"actor":{"kind":"agent","model":"claude","version":"4.8","id":"planner"},"hash":"""
                  "agent actor nests in pinned member order, first in the envelope"
          }

          test "a pre-1144 userId envelope is REFUSED by name, not lifted" {
              // The actor is inside the content address, so a lifted record would
              // carry a stored hash no host can reproduce — a silent verification
              // failure instead of a clear refusal. Go-red proof for that refusal.
              let legacy =
                  """{"hash":"h1","op":{"$type":"RemoveNode","target":"n1"},"parents":[],"resultEnvelope":{"$type":"Success"},"streamId":"s1","timestamp":1700000000,"tombstoned":false,"userId":"u1"}"""

              match DagWire.decodeRecord decodeOp legacy with
              | Ok _ -> failtest "a pre-1144 envelope must not decode"
              | Error e ->
                  Expect.stringContains e "userId" "the refusal names the retired field"
                  Expect.stringContains e "do not carry forward" "the refusal states the consequence"
          }

          test "recomputeHash distinguishes a Human from an Agent with the SAME id" {
              // The point of typing the actor: pre-1144 both of these hashed the
              // bare id "planner" and were indistinguishable by content address.
              let op = TreeOp.RemoveNode(NodeId "n1")
              let ts = DateTimeOffset.FromUnixTimeSeconds 1700000000L

              let asHuman =
                  DagOpRecord.create "s1" [] op None (Actor.Human "planner") ts OpResultEnvelope.Success

              let asAgent =
                  DagOpRecord.create
                      "s1"
                      []
                      op
                      None
                      (Actor.Agent("claude", "4.8", "planner"))
                      ts
                      OpResultEnvelope.Success

              Expect.notEqual asHuman.Hash asAgent.Hash "the Human/Agent distinction is inside the address"
          }

          test "optional fields are omitted when None and present when Some" {
              let withOpt =
                  mkRecord "s1" "h1" [] (TreeOp.RemoveNode(NodeId "n1")) (Some "oh") (Some "prompt-7") 1700000000L false

              let json = DagWire.encodeRecord withOpt
              Expect.stringContains json "\"outcomeHash\":\"oh\"" "outcomeHash present"
              Expect.stringContains json "\"promptId\":\"prompt-7\"" "promptId present"

              let bare =
                  mkRecord "s1" "h1" [] (TreeOp.RemoveNode(NodeId "n1")) None None 1700000000L false

              let bareJson = DagWire.encodeRecord bare
              Expect.isFalse (bareJson.Contains "outcomeHash") "outcomeHash omitted when None"
              Expect.isFalse (bareJson.Contains "promptId") "promptId omitted when None"

              match DagWire.decodeRecord decodeOp json with
              | Ok d ->
                  Expect.equal d.OutcomeHash (Some "oh") "outcome round-trips"
                  Expect.equal d.PromptId (Some "prompt-7") "prompt round-trips"
              | Error e -> failtestf "decode failed: %s" e
          } ]
