module Fuaran.UI.Ops.ErrorRender

// ============================================================================
//  Render an (op, error) pair to the §4d AI-recovery JSON shape.
//
//  Per §4d lines 745–759 the AI consumes a flat envelope:
//
//    { "op":    { "kind": "...", "id": "...", "path": "...", ... },
//      "error": { "code": "...", "message": "...",
//                 "hint": { "node_kind": "...",
//                           "available_fields": [...],
//                           "nodes_with_<field>_field": [...],
//                           "suggestion": "..." } } }
//
//  The `nodes_with_<field>_field` key is dynamic — `<field>` is the failing
//  field name lowercased. Other hint keys are static.
//
//  The op echo is intentionally minimal: it identifies the failing op
//  structurally (kind / target id / addressable parameters) but does not
//  attempt to serialise closure-carrying values (Action / Binding accessors /
//  spec records with function-typed fields). The AI emitting the op already
//  has the typed value; the echo lets the orchestrator key its retry on
//  "which op failed", not re-derive the payload.
//
//  This module is pure rendering — it takes typed inputs and returns a
//  string. No I/O.
//
//  Fable portability: `System.Text.Json` / `Utf8JsonWriter` / `MemoryStream`
//  are server-only and the Fable compiler rejects them. This package is
//  pulled into Fable client compiles transitively (Renderer →
//  Telemetry.Abstractions → Ops), so the renderer must compile under Fable
//  even though the client never calls it. The server keeps the
//  `Utf8JsonWriter` implementation verbatim (byte-identical wire output for
//  the AI orchestrator + fixtures); the Fable side gets a hand-rolled
//  string builder mirroring `JsonDecode.fs`'s "no System.Text.Json" stance.
// ============================================================================

open Fuaran.UI.Types
open Fuaran.UI.Ops.Types

// ─── Shared token mappings (pure; identical on both compile targets) ───────

let private codeToken (code: ApplyErrorCode) : string =
    match code with
    | ApplyErrorCode.NodeNotFound -> "NodeNotFound"
    | ApplyErrorCode.ParentNotFound -> "ParentNotFound"
    | ApplyErrorCode.FieldNotFound -> "FieldNotFound"
    | ApplyErrorCode.SlotNotFound -> "SlotNotFound"
    | ApplyErrorCode.KindMismatch -> "KindMismatch"
    | ApplyErrorCode.ChildlessKind -> "ChildlessKind"
    | ApplyErrorCode.PositionOutOfRange -> "PositionOutOfRange"
    | ApplyErrorCode.OrderingMismatch -> "OrderingMismatch"
    | ApplyErrorCode.DuplicateNodeId -> "DuplicateNodeId"
    | ApplyErrorCode.PathInvalid -> "PathInvalid"
    | ApplyErrorCode.PathNotSupportedYet -> "PathNotSupportedYet"
    | ApplyErrorCode.BatchAborted _ -> "BatchAborted"

let private opKindToken (op: TreeOp<'Msg>) : string =
    match op with
    | TreeOp.EditNode _ -> "EditNode"
    | TreeOp.UpdateProp _ -> "UpdateProp"
    | TreeOp.ReplaceBinding _ -> "ReplaceBinding"
    | TreeOp.UpdateStyle _ -> "UpdateStyle"
    | TreeOp.UpdateState _ -> "UpdateState"
    | TreeOp.InsertChild _ -> "InsertChild"
    | TreeOp.RemoveNode _ -> "RemoveNode"
    | TreeOp.MoveNode _ -> "MoveNode"
    | TreeOp.ReorderChildren _ -> "ReorderChildren"
    | TreeOp.ReplaceRoot _ -> "ReplaceRoot"
    | TreeOp.Batch _ -> "Batch"

#if !FABLE_COMPILER

// ─── Server implementation — System.Text.Json, byte-identical to pre-12.E ──

open System.Text.Json

let private writeNodeId (jw: Utf8JsonWriter) (key: string) (NodeId rawId) = jw.WriteString(key, rawId)

let private writeOpFields (jw: Utf8JsonWriter) (op: TreeOp<'Msg>) =
    jw.WriteString("kind", opKindToken op)

    match op with
    | TreeOp.EditNode(target, _) -> writeNodeId jw "id" target
    | TreeOp.UpdateProp(target, path, _) ->
        writeNodeId jw "id" target
        jw.WriteString("path", path)
    | TreeOp.ReplaceBinding(target, slot, _) ->
        writeNodeId jw "id" target
        jw.WriteString("slot", slot)
    | TreeOp.UpdateStyle(target, _) -> writeNodeId jw "id" target
    | TreeOp.UpdateState(target, _) -> writeNodeId jw "id" target
    | TreeOp.InsertChild(parentId, position, child) ->
        writeNodeId jw "parent_id" parentId
        jw.WriteNumber("position", position)
        writeNodeId jw "child_id" child.Id
    | TreeOp.RemoveNode target -> writeNodeId jw "id" target
    | TreeOp.MoveNode(target, newParentId, newPosition) ->
        writeNodeId jw "id" target
        writeNodeId jw "new_parent_id" newParentId
        jw.WriteNumber("new_position", newPosition)
    | TreeOp.ReorderChildren(parentId, newOrder) ->
        writeNodeId jw "parent_id" parentId
        jw.WriteStartArray("new_order")

        for NodeId rawId in newOrder do
            jw.WriteStringValue rawId

        jw.WriteEndArray()
    | TreeOp.ReplaceRoot node -> writeNodeId jw "id" node.Id
    | TreeOp.Batch inner -> jw.WriteNumber("inner_count", inner.Length)

let private writeHint (jw: Utf8JsonWriter) (failingField: string option) (hint: ApplyHint) =
    jw.WriteStartObject("hint")

    match hint.NodeKind with
    | Some nk -> jw.WriteString("node_kind", nk)
    | None -> ()

    if not (List.isEmpty hint.AvailableFields) then
        jw.WriteStartArray("available_fields")

        for f in hint.AvailableFields do
            jw.WriteStringValue f

        jw.WriteEndArray()

    match hint.NodesWithField with
    | Some(field, ids) when not (List.isEmpty ids) ->
        let key = sprintf "nodes_with_%s_field" (field.ToLowerInvariant())
        jw.WriteStartArray key

        for NodeId rawId in ids do
            jw.WriteStringValue rawId

        jw.WriteEndArray()
    | _ ->
        match failingField with
        | Some _ -> ()
        | None -> ()

    match hint.Suggestion with
    | Some s -> jw.WriteString("suggestion", s)
    | None -> ()

    jw.WriteEndObject()

let private failingFieldOf (op: TreeOp<'Msg>) : string option =
    match op with
    | TreeOp.UpdateProp(_, path, _) -> Some path
    | TreeOp.ReplaceBinding(_, slot, _) -> Some slot
    | _ -> None

/// Render an (op, error) pair to the §4d-shaped JSON envelope.
let render (op: TreeOp<'Msg>) (error: ApplyError) : string =
    use buffer = new System.IO.MemoryStream()

    do
        use jw = new Utf8JsonWriter(buffer)
        jw.WriteStartObject()

        // op
        jw.WriteStartObject("op")
        writeOpFields jw op
        jw.WriteEndObject()

        // error
        jw.WriteStartObject("error")
        jw.WriteString("code", codeToken error.Code)

        match error.Code with
        | ApplyErrorCode.BatchAborted idx -> jw.WriteNumber("batch_index", idx)
        | _ -> ()

        jw.WriteString("message", error.Message)
        writeHint jw (failingFieldOf op) error.Hint
        jw.WriteEndObject()

        jw.WriteEndObject()

    buffer.ToArray() |> System.Text.Encoding.UTF8.GetString

#else

// ─── Fable implementation — hand-rolled JSON string builder ────────────────
//
// Mirrors JsonDecode.fs's "no System.Text.Json (server-only); hand-roll"
// stance. Produces the same §4d envelope shape — minimal compact JSON with
// standard string escaping. (The Fable client never invokes `render`; this
// exists so the package compiles under Fable as a transitive dependency.)

let private jsonString (s: string) : string =
    let escaped =
        s
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\b", "\\b")
            .Replace("\f", "\\f")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t")

    "\"" + escaped + "\""

let private jsonObj (fields: string list) : string = "{" + String.concat "," fields + "}"

let private jsonArr (items: string list) : string = "[" + String.concat "," items + "]"

let private fieldStr (key: string) (value: string) : string = jsonString key + ":" + jsonString value

let private fieldNum (key: string) (value: int) : string = jsonString key + ":" + string value

let private fieldRaw (key: string) (rawJson: string) : string = jsonString key + ":" + rawJson

let private rawId (NodeId r) = r

let private opFields (op: TreeOp<'Msg>) : string list =
    let kind = fieldStr "kind" (opKindToken op)

    let rest =
        match op with
        | TreeOp.EditNode(target, _) -> [ fieldStr "id" (rawId target) ]
        | TreeOp.UpdateProp(target, path, _) -> [ fieldStr "id" (rawId target); fieldStr "path" path ]
        | TreeOp.ReplaceBinding(target, slot, _) -> [ fieldStr "id" (rawId target); fieldStr "slot" slot ]
        | TreeOp.UpdateStyle(target, _) -> [ fieldStr "id" (rawId target) ]
        | TreeOp.UpdateState(target, _) -> [ fieldStr "id" (rawId target) ]
        | TreeOp.InsertChild(parentId, position, child) ->
            [ fieldStr "parent_id" (rawId parentId)
              fieldNum "position" position
              fieldStr "child_id" (rawId child.Id) ]
        | TreeOp.RemoveNode target -> [ fieldStr "id" (rawId target) ]
        | TreeOp.MoveNode(target, newParentId, newPosition) ->
            [ fieldStr "id" (rawId target)
              fieldStr "new_parent_id" (rawId newParentId)
              fieldNum "new_position" newPosition ]
        | TreeOp.ReorderChildren(parentId, newOrder) ->
            [ fieldStr "parent_id" (rawId parentId)
              fieldRaw "new_order" (jsonArr (newOrder |> List.map (rawId >> jsonString))) ]
        | TreeOp.ReplaceRoot node -> [ fieldStr "id" (rawId node.Id) ]
        | TreeOp.Batch inner -> [ fieldNum "inner_count" inner.Length ]

    kind :: rest

let private hintObj (hint: ApplyHint) : string =
    [ yield!
          (match hint.NodeKind with
           | Some nk -> [ fieldStr "node_kind" nk ]
           | None -> [])
      yield!
          (if List.isEmpty hint.AvailableFields then
               []
           else
               [ fieldRaw "available_fields" (jsonArr (hint.AvailableFields |> List.map jsonString)) ])
      yield!
          (match hint.NodesWithField with
           | Some(field, ids) when not (List.isEmpty ids) ->
               [ fieldRaw
                     (sprintf "nodes_with_%s_field" (field.ToLowerInvariant()))
                     (jsonArr (ids |> List.map (rawId >> jsonString))) ]
           | _ -> [])
      yield!
          (match hint.Suggestion with
           | Some s -> [ fieldStr "suggestion" s ]
           | None -> []) ]
    |> jsonObj

/// Render an (op, error) pair to the §4d-shaped JSON envelope.
let render (op: TreeOp<'Msg>) (error: ApplyError) : string =
    let errorFields =
        [ fieldStr "code" (codeToken error.Code)
          match error.Code with
          | ApplyErrorCode.BatchAborted idx -> fieldNum "batch_index" idx
          | _ -> ()
          fieldStr "message" error.Message
          fieldRaw "hint" (hintObj error.Hint) ]

    jsonObj
        [ fieldRaw "op" (jsonObj (opFields op))
          fieldRaw "error" (jsonObj errorFields) ]

#endif
