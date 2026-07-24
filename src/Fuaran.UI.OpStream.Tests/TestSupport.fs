module Fuaran.UI.OpStream.Tests.TestSupport

open System
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  Test fixtures + helpers.
//
//  TestMsg is the smallest viable 'Msg shape — single nullary case. The op
//  vocabulary tests don't need a richer DU; structural ops never construct
//  Msg values themselves.
//
//  buildRecord helper: assemble OpRecord<'Msg> values with the hash chain
//  computed correctly. Used by every test that needs to append a record.
//
//  TestOpCodec: hand-rolled codec covering only the closure-free op shapes
//  the Sqlite durability tests exercise — RemoveNode, UpdateStyle,
//  ReorderChildren, MoveNode. Encodes to canonical JSON via the abstractions
//  encoder; decodes via a small case-discriminator parser.
// ============================================================================

type TestMsg =
    | NoOp
    | Selected of int

let timestamp (unix: int64) : DateTimeOffset = DateTimeOffset.FromUnixTimeSeconds unix

let dashboardId = NodeId "dash"
let leftChildId = NodeId "left"
let rightChildId = NodeId "right"

let buildDashboard () : Node<TestMsg> =
    Fuaran.dashboard
        "dash"
        { Defaults.dashboard<TestMsg> with
            Children = [ Fuaran.markdown "left" "Left pane"; Fuaran.markdown "right" "Right pane" ] }

/// Compose a fresh OpRecord for streamId/sequence with hash chain computed.
/// Caller supplies the previous record (or `None` for the genesis).
let buildRecord
    (streamId: string)
    (sequence: int)
    (op: TreeOp<TestMsg>)
    (previous: OpRecord<TestMsg> option)
    (timestamp: DateTimeOffset)
    : OpRecord<TestMsg> =
    let previousHash =
        match previous with
        | None -> HashChain.genesisPreviousHash
        | Some prev -> prev.Hash

    let actor = Actor.Human "tester"

    let hash =
        HashChain.computeHash previousHash op sequence timestamp actor None OpResultEnvelope.Success

    { StreamId = streamId
      Sequence = sequence
      PreviousHash = previousHash
      Hash = hash
      Op = op
      PromptId = None
      Actor = actor
      Timestamp = timestamp
      ResultEnvelope = OpResultEnvelope.Success }

// ─── Tiny test-only codec for SqliteSink round-trip tests ─────────────────
//
// Tests exercise only closure-free ops via Sqlite: RemoveNode, UpdateStyle,
// MoveNode, ReorderChildren. Encoder pipes through CanonicalJson; decoder is
// a small format-specific parser keyed on the case discriminator.

module private SimpleSinkJson =

    let private toneOf (s: string) : ToneVariant =
        match s with
        | "Subdued" -> Subdued
        | "Brand" -> Brand
        | "Success" -> Success
        | "Warning" -> Warning
        | "Critical" -> Critical
        | "Info" -> Info
        | _ -> Default

    let private weightOf (s: string) : StyleWeight =
        match s with
        | "Compact" -> Compact
        | "Spacious" -> Spacious
        | _ -> Standard

    let private emphasisOf (s: string) : Emphasis =
        match s with
        | "Quiet" -> Quiet
        | "Loud" -> Loud
        | _ -> Normal

    // Hand-encoded compact format keyed on a single-letter discriminator.
    // RemoveNode:        R|<id>
    // UpdateStyle:       S|<id>|<tone>|<weight>|<emphasis>
    // MoveNode:          M|<target>|<newParent>|<newPos>
    // ReorderChildren:   O|<parent>|<id1>,<id2>,...
    //
    // The format is test-only — production SqliteSinks supply a codec that
    // covers their host's full 'Msg DU.

    let encode (op: TreeOp<TestMsg>) : string =
        match op with
        | TreeOp.RemoveNode(NodeId raw) -> sprintf "R|%s" raw
        | TreeOp.UpdateStyle(NodeId raw, style) -> sprintf "S|%s|%A|%A|%A" raw style.Tone style.Weight style.Emphasis
        | TreeOp.MoveNode(NodeId target, NodeId newParent, newPosition) ->
            sprintf "M|%s|%s|%d" target newParent newPosition
        | TreeOp.ReorderChildren(NodeId parent, newOrder) ->
            let ids = newOrder |> List.map (fun (NodeId raw) -> raw) |> String.concat ","
            sprintf "O|%s|%s" parent ids
        | other -> failwithf "TestOpCodec: unsupported op shape %A" other

    let decode (json: string) : Result<TreeOp<TestMsg>, string> =
        let parts = json.Split('|')

        match parts with
        | [| "R"; id |] -> Ok(TreeOp.RemoveNode(NodeId id))
        | [| "S"; id; tone; weight; emphasis |] ->
            let style =
                { Tone = toneOf tone
                  Weight = weightOf weight
                  Emphasis = emphasisOf emphasis
                  Role = StyleRole.None
                  Voice = FontVoice.Default }

            Ok(TreeOp.UpdateStyle(NodeId id, style))
        | [| "M"; target; newParent; newPosStr |] ->
            match Int32.TryParse newPosStr with
            | true, n -> Ok(TreeOp.MoveNode(NodeId target, NodeId newParent, n))
            | false, _ -> Error(sprintf "MoveNode: bad newPosition '%s'" newPosStr)
        | [| "O"; parent; idsCsv |] ->
            let ids =
                if idsCsv = "" then
                    []
                else
                    idsCsv.Split(',') |> Array.map (fun raw -> NodeId raw) |> List.ofArray

            Ok(TreeOp.ReorderChildren(NodeId parent, ids))
        | _ -> Error(sprintf "TestOpCodec: unrecognised payload '%s'" json)

let testCodec: IOpJsonCodec<TestMsg> =
    { new IOpJsonCodec<TestMsg> with
        member _.EncodeOp op = SimpleSinkJson.encode op
        member _.DecodeOp json = SimpleSinkJson.decode json }
