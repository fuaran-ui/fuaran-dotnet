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
//  Fable portability: this package is pulled into Fable client compiles
//  transitively (Renderer → Telemetry.Abstractions → Ops), so `render` is
//  written over `JVal` and the canonical writer, which both compile targets
//  share — one implementation, the same bytes on each (Phase 2064).
// ============================================================================

open Fuaran.Core
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
    | ApplyErrorCode.LimitExceeded -> "LimitExceeded"
    | ApplyErrorCode.PositionNotStructural _ -> "PositionNotStructural"

/// The envelope's `kind` token — the op's case name (Phase 2044: projected,
/// not matched here).
let private opKindToken (op: TreeOp<'Msg>) : string = Fuaran.UI.Ops.TreeOp.kindName op

// ─── The envelope as a `JVal`, rendered by the canonical writer ────────────
//
// ONE implementation on every compile target (Phase 2064). The envelope is
// built as a `JVal` and written by `Canon.renderOrdered`: the canonical escape
// (only `"`, `\` and control characters, the latter as lower-case `\u00xx`)
// with the members in the order they are authored here — `op` before
// `error`, `kind` first — because the §4d envelope is read by a model, and its
// order is part of how it reads.
//
// The bytes this changed, named per pipeline (against the two implementations
// it replaced):
//
//  * .NET (was `Utf8JsonWriter` with its default encoder): `<` `>` `&` `'` `+`
//    are now written raw rather than as `\u003C` `\u003E` `\u0026` `\u0027`
//    `\u002B`; `"` is `\"` rather than `\u0022`; a non-ASCII character is
//    written raw (UTF-8) rather than as `\uXXXX`; and a control character is
//    `\u00xx` in lower-case hex — `\n` `\r` `\t` `\b` `\f` included — where it
//    was the short escape.
//  * Fable (was a hand-rolled string builder): `\n` `\r` `\t` `\b` `\f` are now
//    `\u000a` `\u000d` `\u0009` `\u0008` `\u000c`; every other control
//    character, which it used to write raw (invalid JSON), is now `\u00xx`.
//
// Everything else — keys, their order, numbers — is byte-identical to both.

let private nodeIdValue (NodeId rawId) : JVal = JStr rawId

let private opFields (op: TreeOp<'Msg>) : (string * JVal) list =
    let rest =
        match op with
        | TreeOp.EditNode(target, _) -> [ "id", nodeIdValue target ]
        | TreeOp.UpdateProp(target, path, _) -> [ "id", nodeIdValue target; "path", JStr path ]
        | TreeOp.ReplaceBinding(target, slot, _) -> [ "id", nodeIdValue target; "slot", JStr slot ]
        | TreeOp.UpdateStyle(target, _) -> [ "id", nodeIdValue target ]
        | TreeOp.UpdateState(target, _) -> [ "id", nodeIdValue target ]
        | TreeOp.InsertChild(parentId, child) -> [ "parent_id", nodeIdValue parentId; "child_id", JStr child.Id ]
        | TreeOp.RemoveNode target -> [ "id", nodeIdValue target ]
        | TreeOp.MoveNode(target, newParentId) -> [ "id", nodeIdValue target; "new_parent_id", nodeIdValue newParentId ]
        | TreeOp.ReorderChildren(parentId, newOrder) ->
            [ "parent_id", nodeIdValue parentId
              "new_order", JArr(newOrder |> List.map nodeIdValue) ]
        | TreeOp.ReplaceRoot node -> [ "id", JStr node.Id ]
        | TreeOp.Batch inner -> [ "inner_count", JInt inner.Length ]

    ("kind", JStr(opKindToken op)) :: rest

let private hintValue (hint: ApplyHint) : JVal =
    JObj
        [ match hint.NodeKind with
          | Some nk -> "node_kind", JStr nk
          | None -> ()
          if not (List.isEmpty hint.AvailableFields) then
              "available_fields", JArr(hint.AvailableFields |> List.map JStr)
          match hint.NodesWithField with
          | Some(field, ids) when not (List.isEmpty ids) ->
              sprintf "nodes_with_%s_field" (field.ToLowerInvariant()), JArr(ids |> List.map nodeIdValue)
          | _ -> ()
          match hint.Suggestion with
          | Some s -> "suggestion", JStr s
          | None -> () ]

/// Render an (op, error) pair to the §4d-shaped JSON envelope.
let render (op: TreeOp<'Msg>) (error: ApplyError) : string =
    let errorFields =
        [ "code", JStr(codeToken error.Code)
          match error.Code with
          | ApplyErrorCode.BatchAborted idx -> "batch_index", JInt idx
          | _ -> ()
          "message", JStr error.Message
          "hint", hintValue error.Hint ]

    Canon.renderOrdered (JObj [ "op", JObj(opFields op); "error", JObj errorFields ])
