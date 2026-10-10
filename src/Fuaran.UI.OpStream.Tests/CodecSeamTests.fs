module Fuaran.UI.OpStream.Tests.CodecSeamTests

// ============================================================================
//  Phase 2065 — typed errors survive the codec seams.
//
//  `IOpJsonCodec.DecodeOp` and `INodeJsonCodec.DecodeNode` return `CodecError`,
//  so a decoder refusal keeps its code, path and expected shape through the
//  codec, and through a sink that reads a stored document back (the sink raises
//  `CodecDecodeFailed` carrying the same typed value). Each round trip below is
//  "typed error in -> the same typed error out"; the renderings are pinned
//  byte-for-byte to the strings each seam returned before it was typed.
// ============================================================================

// The mapper-refusal case needs an op carrying `Action.Dispatch`, which the IDL
// marks in-process-only (FS0044, an error here); this file builds one only to
// encode it and watch the refusal, exactly as `TreeOpMapLawsTests` does. The
// `box`ed payload is `objnull` under F# 10 (FS3261), as there.
#nowarn "44"
#nowarn "3261"

open System
open System.IO
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Sqlite
open Fuaran.UI.OpStream.Tests.TestSupport

/// An op document missing its required target — a decode refusal with a real
/// code and path, not just a message.
let private opMissingTarget = """{"$type":"RemoveNode"}"""

/// A node document whose id is empty — `EMPTY_NODE_ID`.
let private nodeWithEmptyId =
    """{"id":"","kind":{"$type":"Text","content":{"$type":"Literal","value":"x"}}}"""

let private decodeErrorOf (r: Result<'a, JsonDecode.DecodeError>) : JsonDecode.DecodeError =
    match r with
    | Error e -> e
    | Ok _ -> failtest "the fixture was meant to be refused by the decoder"

let private dispatchingButton: Node<obj> =
    { Id = "seam-button"
      Kind =
        NodeKind.Button
            { Defaults.button with
                Label = TextSource.Literal "Go"
                OnClick = Action.Dispatch(box "seam-payload") }
      State = None
      Style = None
      Accessibility = None
      Motion = None
      Fallback = None
      ExtraAttributes = None
      Tooltip = None
      Visible = None }

let private withTempDb (body: string -> unit) =
    let path =
        Path.Combine(Path.GetTempPath(), sprintf "fuaran-codec-seam-%s.db" (Guid.NewGuid().ToString("N")))

    try
        body (sprintf "Data Source=%s;Pooling=False" path)
    finally
        try
            File.Delete path
        with _ ->
            ()

[<Tests>]
let tests =
    testList
        "Phase 2065 — typed errors survive the codec seams"
        [ test "the reference op codec returns the decoder's DecodeError, intact" {
              let expected = decodeErrorOf (JsonDecode.decodeOp opMissingTarget)
              let codec = OpJsonCodec.canonicalObj ()

              match codec.DecodeOp opMissingTarget with
              | Error(CodecError.Decode actual) ->
                  Expect.equal actual expected "the same typed error comes out as went in"
                  Expect.equal actual.Code "MISSING_FIELD" "the code survives"
                  Expect.isNonEmpty actual.Path "the path survives"
              | other -> failtestf "expected a typed decode refusal, got %A" other
          }

          test "a mapper refusal is the typed MapRefusal, intact" {
              let json =
                  CanonicalJson.encodeOp (TreeOp.InsertChild(NodeId "seam-parent", dispatchingButton))

              let expected =
                  match JsonDecode.decodeOp json with
                  | Ok op ->
                      match TreeOpMap.TreeOp.mapMsg (fun (_: obj) -> (None: string option)) op with
                      | Error refusal -> refusal
                      | Ok _ -> failtest "the refusing mapper must refuse"
                  | Error e -> failtestf "the op must decode: %A" e

              let codec = OpJsonCodec.canonical (fun (_: obj) -> (None: string option))

              match codec.DecodeOp json with
              | Error(CodecError.Unmapped actual) -> Expect.equal actual expected "the refusal survives typed"
              | other -> failtestf "expected a typed mapper refusal, got %A" other
          }

          test "each rendering is byte-identical to the string the seam returned before it was typed" {
              let decodeError = decodeErrorOf (JsonDecode.decodeOp opMissingTarget)

              Expect.equal
                  (CodecError.render (CodecError.Decode decodeError))
                  (sprintf "%s at '%s': %s" decodeError.Code decodeError.Path decodeError.Message)
                  "a decode refusal renders as CODE at 'path': message"

              Expect.equal
                  (JsonDecode.DecodeError.render decodeError)
                  (sprintf "%s at '%s': %s" decodeError.Code decodeError.Path decodeError.Message)
                  "DecodeError.render is that same line"

              let refusal: TreeOpMap.MapRefusal =
                  { Op = "InsertChild"
                    Slot = "child"
                    Node = Some "p"
                    Payloads = [ "x" ] }

              Expect.equal
                  (CodecError.render (CodecError.Unmapped refusal))
                  (TreeOpMap.MapRefusal.render refusal)
                  "a mapper refusal renders as MapRefusal.render"

              match (OpJsonCodec.encodeOnly<obj> ()).DecodeOp "{}" with
              | Error e ->
                  Expect.equal
                      (CodecError.render e)
                      "OpJsonCodec.encodeOnly does not implement DecodeOp"
                      "the encode-only op codec's text"
              | Ok _ -> failtest "an encode-only codec reads nothing"

              match (NodeJsonCodec.encodeOnly<obj> ()).DecodeNode "{}" with
              | Error e ->
                  Expect.equal
                      (CodecError.render e)
                      "NodeJsonCodec.encodeOnly does not implement DecodeNode"
                      "the encode-only node codec's text"
              | Ok _ -> failtest "an encode-only codec reads nothing"
          }

          test "a refusal reaching a sink's read-back keeps its code and path" {
              let expected = decodeErrorOf (JsonDecode.decodeOp opMissingTarget)
              let reader = OpJsonCodec.canonical<TestMsg> (fun _ -> None)

              // Stores a document the decoder refuses, then reads it back.
              let codec =
                  { new IOpJsonCodec<TestMsg> with
                      member _.EncodeOp _ = opMissingTarget
                      member _.DecodeOp json = reader.DecodeOp json }

              withTempDb (fun connStr ->
                  let sink = SqliteSink.create connStr codec

                  let record =
                      buildRecord "seam" 1 (TreeOp.RemoveNode(NodeId "x")) None (timestamp 100L)

                  sink.Append record |> Async.RunSynchronously

                  try
                      sink.Replay("seam", 1, 1) |> Async.RunSynchronously |> ignore
                      failtest "the sink must refuse the stored document"
                  with CodecDecodeFailed(context, error) as ex ->
                      Expect.equal error (CodecError.Decode expected) "the sink raises the codec's typed error"

                      Expect.equal
                          ex.Message
                          (sprintf
                              "SqliteSink: codec failed to decode op at (StreamId=seam, Sequence=1): %s"
                              (JsonDecode.DecodeError.render expected))
                          "and its message is the text the sink always printed"

                      Expect.stringContains context "Sequence=1" "the context names where the sink was reading")
          }

          test "a refusal reaching a checkpoint read-back keeps its code and path" {
              let expected = decodeErrorOf (JsonDecode.decodeNodeObj nodeWithEmptyId)

              let nodeCodec =
                  { new INodeJsonCodec<TestMsg> with
                      member _.EncodeNode _ = nodeWithEmptyId

                      member _.DecodeNode json =
                          JsonDecode.decodeNodeObj json
                          |> Result.mapError CodecError.Decode
                          |> Result.map (fun _ -> buildDashboard ()) }

              withTempDb (fun connStr ->
                  let sink = SqliteSink.createWithCheckpoints connStr testCodec nodeCodec

                  let record =
                      buildRecord "seam" 1 (TreeOp.RemoveNode(NodeId "x")) None (timestamp 100L)

                  sink.Append record |> Async.RunSynchronously

                  let cp: Checkpoint<TestMsg> =
                      { StreamId = "seam"
                        Sequence = 1
                        PreviousChainHead = record.Hash
                        SnapshotHash = String.replicate 64 "a"
                        Snapshot = buildDashboard ()
                        Timestamp = DateTimeOffset.FromUnixTimeSeconds 200L }

                  sink.AppendCheckpoint cp |> Async.RunSynchronously

                  try
                      sink.LatestCheckpointAtOrBefore("seam", 1) |> Async.RunSynchronously |> ignore
                      failtest "the sink must refuse the stored snapshot"
                  with CodecDecodeFailed(_, error) ->
                      match error with
                      | CodecError.Decode actual ->
                          Expect.equal actual expected "the checkpoint read-back raises the decoder's typed error"
                          Expect.equal actual.Code "EMPTY_NODE_ID" "the code survives"
                      | other -> failtestf "expected a typed decode refusal, got %A" other)
          }

          test "rendering and telemetry read one code projection, and their text is unchanged" {
              let error code : ApplyError =
                  { Code = code
                    Message = "m"
                    Hint =
                      { NodeKind = None
                        AvailableFields = []
                        NodesWithField = None
                        Suggestion = None } }

              let outcome code =
                  Fuaran.UI.Telemetry.Abstractions.OpOutcome.ofApplyResult (
                      Error(error code): Result<Node<obj>, ApplyError>
                  )

              let op: TreeOp<obj> = TreeOp.RemoveNode(NodeId "n")

              Expect.stringContains
                  (ErrorRender.render op (error (ApplyErrorCode.BatchAborted 3)))
                  "\"code\":\"BatchAborted\",\"batch_index\":3"
                  "the envelope's code is the case name, its payload a separate member"

              Expect.equal
                  (outcome (ApplyErrorCode.BatchAborted 3))
                  (Fuaran.UI.Telemetry.Abstractions.OpOutcome.ApplyEngineError "BatchAborted(3): m")
                  "telemetry keeps its payload-in-parentheses spelling"

              Expect.equal
                  (outcome (ApplyErrorCode.PositionNotStructural "Switch.cases[0].child"))
                  (Fuaran.UI.Telemetry.Abstractions.OpOutcome.ApplyEngineError
                      "PositionNotStructural(Switch.cases[0].child): m")
                  "and for the slot-carrying case"

              Expect.equal
                  (outcome ApplyErrorCode.KindMismatch)
                  (Fuaran.UI.Telemetry.Abstractions.OpOutcome.ApplyEngineError "KindMismatch: m")
                  "a payload-free case is the bare name"

              Expect.equal (ApplyErrorCode.name ApplyErrorCode.LimitExceeded) "LimitExceeded" "the shared name"
          } ]
