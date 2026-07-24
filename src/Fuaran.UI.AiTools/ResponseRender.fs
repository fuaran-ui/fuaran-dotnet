module Fuaran.UI.AiTools.ResponseRender

// ============================================================================
//  Render the §4i introspection-tool responses to JSON.
//
//  Two wire conventions, both matched verbatim against the Fuaran design specification:
//
//   - The introspection envelope (NodeState / ResolvedBinding /
//     GeometryTree / ErrorEntry) uses **camelCase** keys per the §4i
//     example at lines 1145–1175 (`currentState`, `stateDetail`,
//     `resolvedValue`, `resolvedAt`).
//
//   - The tool-level error envelope (IntrospectError when a tool can't
//     proceed — NodeNotFound, SlotNotFound) uses the §4d shape with
//     **snake_case** hint keys (`node_kind`, `available_fields`,
//     `suggestion`). Mirrors `Fuaran.UI.Ops.ErrorRender`.
//
//  Pure rendering — no I/O. Same `System.Text.Json.Utf8JsonWriter`
//  posture `Fuaran.UI.Ops.ErrorRender` uses; the .NET pipeline target is
//  load-bearing here (the downstream AI consumer is server-side).
// ============================================================================

open System.IO
open System.Text.Json
open Fuaran.UI.Types
open Fuaran.UI.AiTools.Types

// ─── Token helpers ─────────────────────────────────────────────────────────

let private currentStateToken (state: CurrentState) : string =
    match state with
    | CurrentState.Normal -> "Normal"
    | CurrentState.Loading -> "Loading"
    | CurrentState.Empty -> "Empty"
    | CurrentState.Error -> "Error"

let private bindingSourceToken (source: BindingSource) : string =
    match source with
    | BindingSource.Static -> "Static"
    | BindingSource.Query _ -> "Query"
    | BindingSource.Filter _ -> "Filter"
    | BindingSource.Selection _ -> "Selection"
    | BindingSource.State _ -> "State"
    | BindingSource.Computed -> "Computed"
    | BindingSource.I18n _ -> "I18n"

let private bindingErrorCodeToken (code: BindingErrorCode) : string =
    match code with
    | BindingErrorCode.SourceUnregistered -> "SourceUnregistered"
    | BindingErrorCode.NotResolvedYet -> "NotResolvedYet"
    | BindingErrorCode.AccessorThrew -> "AccessorThrew"
    | BindingErrorCode.TypeMismatch -> "TypeMismatch"

let private introspectErrorCodeToken (code: IntrospectErrorCode) : string =
    match code with
    | IntrospectErrorCode.NodeNotFound -> "NodeNotFound"
    | IntrospectErrorCode.SlotNotFound -> "SlotNotFound"
    | IntrospectErrorCode.UnknownIncludeKey -> "UnknownIncludeKey"
    | IntrospectErrorCode.ProbeUnwired -> "ProbeUnwired"

// ─── Generic value rendering — write a boxed obj as best-effort JSON ───────
//
// Spec-record values stored in PropEntry / ResolvedBinding.ResolvedValue
// are obj-boxed (the cross-cutting introspection boundary). Render the
// common primitives directly; fall back to `value.ToString()` for
// non-primitive shapes so the orchestrator at least sees a stable string
// representation rather than `null` for un-renderable values.

let rec private writeBoxedValue (jw: Utf8JsonWriter) (value: obj) =
    match value with
    | :? string as s -> jw.WriteStringValue s
    | :? bool as b -> jw.WriteBooleanValue b
    | :? int as i -> jw.WriteNumberValue i
    | :? int64 as i -> jw.WriteNumberValue i
    | :? float as f -> jw.WriteNumberValue f
    | :? float32 as f -> jw.WriteNumberValue(float f)
    | :? decimal as d -> jw.WriteNumberValue d
    | _ ->
        // Best-effort fallback — render the F# `%A` form for stable
        // determinism (the orchestrator just needs identical inputs →
        // identical bytes; the exact shape doesn't have to be parseable).
        jw.WriteStringValue(sprintf "%A" value)

let private writeOptionalBoxed (jw: Utf8JsonWriter) (key: string) (value: obj option) =
    match value with
    | Some v ->
        jw.WritePropertyName key
        writeBoxedValue jw v
    | None -> jw.WriteNull key

// ─── ResolvedBinding / BindingError ────────────────────────────────────────

let private writeResolvedBinding (jw: Utf8JsonWriter) (rb: ResolvedBinding) =
    jw.WriteStartObject()
    jw.WriteString("expression", rb.Expression)
    writeOptionalBoxed jw "resolvedValue" rb.ResolvedValue

    match rb.TypeHint with
    | Some t -> jw.WriteString("type", t)
    | None -> ()

    jw.WriteString("source", bindingSourceToken rb.Source)
    jw.WriteEndObject()

let private writeBindingError (jw: Utf8JsonWriter) (err: BindingError) =
    jw.WriteStartObject()
    jw.WriteString("expression", err.Expression)
    jw.WriteString("source", bindingSourceToken err.Source)

    jw.WriteStartObject("error")
    jw.WriteString("code", bindingErrorCodeToken err.Code)
    jw.WriteString("message", err.Message)

    jw.WriteStartObject("hint")

    match err.Hint.NodeKind with
    | Some nk -> jw.WriteString("node_kind", nk)
    | None -> ()

    if not (List.isEmpty err.Hint.AvailableAlternatives) then
        jw.WriteStartArray("available_alternatives")

        for alt in err.Hint.AvailableAlternatives do
            jw.WriteStringValue alt

        jw.WriteEndArray()

    match err.Hint.Suggestion with
    | Some s -> jw.WriteString("suggestion", s)
    | None -> ()

    jw.WriteEndObject() // hint
    jw.WriteEndObject() // error
    jw.WriteEndObject()

let private writeResolutionResult (jw: Utf8JsonWriter) (result: ResolvedBindingResult) =
    match result with
    | ResolvedBindingResult.Resolved rb -> writeResolvedBinding jw rb
    | ResolvedBindingResult.Failed err -> writeBindingError jw err

// ─── PropEntry / Bindings map ──────────────────────────────────────────────

let private writeProps (jw: Utf8JsonWriter) (props: PropEntry list) =
    jw.WriteStartObject("props")

    for p in props do
        match p.Value with
        | Some v ->
            jw.WritePropertyName p.Name
            writeBoxedValue jw v
        | None ->
            // Functions / opaque slots — emit { "_unresolved": true,
            // "type": "<sig>" } so the orchestrator distinguishes
            // "explicitly absent" from "wired but opaque".
            jw.WriteStartObject(p.Name)
            jw.WriteBoolean("_unresolved", true)

            match p.TypeHint with
            | Some t -> jw.WriteString("type", t)
            | None -> ()

            jw.WriteEndObject()

    jw.WriteEndObject()

let private writeBindings (jw: Utf8JsonWriter) (bindings: Map<string, ResolvedBindingResult>) =
    jw.WriteStartObject("bindings")

    // Map iteration order is deterministic (ordered by key) in F#'s Map,
    // so the snapshot bytes are stable across runs.
    for KeyValue(slot, result) in bindings do
        jw.WritePropertyName slot
        writeResolutionResult jw result

    jw.WriteEndObject()

// ─── Geometry ──────────────────────────────────────────────────────────────

let private writeGeometry (jw: Utf8JsonWriter) (geometry: Geometry) =
    jw.WriteStartObject()
    jw.WriteNumber("x", geometry.X)
    jw.WriteNumber("y", geometry.Y)
    jw.WriteNumber("width", geometry.Width)
    jw.WriteNumber("height", geometry.Height)
    jw.WriteBoolean("overflowing", geometry.Overflowing)
    jw.WriteEndObject()

let rec private writeGeometryTree (jw: Utf8JsonWriter) (tree: GeometryTree) =
    jw.WriteStartObject()

    let (NodeId rawId) = tree.Id

    jw.WriteString("id", rawId)
    jw.WriteString("kind", tree.Kind)

    match tree.Geometry with
    | Some g ->
        jw.WritePropertyName "geometry"
        writeGeometry jw g
    | None -> jw.WriteNull "geometry"

    jw.WriteStartArray("children")

    for child in tree.Children do
        writeGeometryTree jw child

    jw.WriteEndArray()
    jw.WriteEndObject()

// ─── NodeState envelope ────────────────────────────────────────────────────

let private writeNodeState (jw: Utf8JsonWriter) (state: NodeState) =
    jw.WriteStartObject()

    let (NodeId rawId) = state.Id

    jw.WriteString("id", rawId)
    jw.WriteString("kind", state.Kind)

    match state.Props with
    | Some props -> writeProps jw props
    | None -> ()

    match state.Bindings with
    | Some bindings -> writeBindings jw bindings
    | None -> ()

    match state.CurrentState with
    | Some cs -> jw.WriteString("currentState", currentStateToken cs)
    | None -> ()

    match state.StateDetail with
    | Some sd ->
        jw.WriteStartObject("stateDetail")
        jw.WriteString("detail", sd.Detail)
        jw.WriteEndObject()
    | None ->
        if Option.isSome state.CurrentState then
            jw.WriteNull "stateDetail"

    match state.Geometry with
    | Some g ->
        jw.WritePropertyName "geometry"
        writeGeometry jw g
    | None ->
        // Include the null key when geometry was asked for but the probe
        // returned None, so the orchestrator distinguishes "asked + no
        // data" from "not asked".
        ()

    jw.WriteEndObject()

// ─── ErrorEntry list ───────────────────────────────────────────────────────

let private writeErrorEntry (jw: Utf8JsonWriter) (entry: ErrorEntry) =
    jw.WriteStartObject()
    jw.WriteString("id", entry.Id)

    match entry.TurnId with
    | Some t -> jw.WriteNumber("turnId", t)
    | None -> jw.WriteNull "turnId"

    jw.WriteString("code", entry.Code)
    jw.WriteString("message", entry.Message)

    match entry.NodeId with
    | Some(NodeId rawId) -> jw.WriteString("nodeId", rawId)
    | None -> jw.WriteNull "nodeId"

    jw.WriteString("recordedAt", entry.RecordedAt.ToString("o"))
    jw.WriteEndObject()

// ─── IntrospectError envelope (§4d-shape) ──────────────────────────────────

let private writeIntrospectError
    (jw: Utf8JsonWriter)
    (toolName: string)
    (args: (string * string) list)
    (err: IntrospectError)
    =
    jw.WriteStartObject()

    // tool / args echo
    jw.WriteStartObject("tool")
    jw.WriteString("name", toolName)

    for (k, v) in args do
        jw.WriteString(k, v)

    jw.WriteEndObject()

    // error
    jw.WriteStartObject("error")
    jw.WriteString("code", introspectErrorCodeToken err.Code)
    jw.WriteString("message", err.Message)

    jw.WriteStartObject("hint")

    match err.Hint.NodeKind with
    | Some nk -> jw.WriteString("node_kind", nk)
    | None -> ()

    if not (List.isEmpty err.Hint.AvailableFields) then
        jw.WriteStartArray("available_fields")

        for f in err.Hint.AvailableFields do
            jw.WriteStringValue f

        jw.WriteEndArray()

    match err.Hint.Suggestion with
    | Some s -> jw.WriteString("suggestion", s)
    | None -> ()

    jw.WriteEndObject() // hint
    jw.WriteEndObject() // error

    jw.WriteEndObject()

// ─── Public render functions ───────────────────────────────────────────────

let private buildJson (write: Utf8JsonWriter -> unit) : string =
    use buffer = new MemoryStream()

    do
        use jw = new Utf8JsonWriter(buffer)
        write jw

    buffer.ToArray() |> System.Text.Encoding.UTF8.GetString

let renderNodeState (state: NodeState) : string =
    buildJson (fun jw -> writeNodeState jw state)

let renderResolvedBindingResult (result: ResolvedBindingResult) : string =
    buildJson (fun jw -> writeResolutionResult jw result)

let renderGeometryTree (tree: GeometryTree) : string =
    buildJson (fun jw -> writeGeometryTree jw tree)

let renderErrorEntries (entries: ErrorEntry list) : string =
    buildJson (fun jw ->
        jw.WriteStartArray()

        for entry in entries do
            writeErrorEntry jw entry

        jw.WriteEndArray())

/// Render an `IntrospectError` to the §4d-shaped envelope. `toolName` is
/// the bare tool ("fuaran.getNodeState" / "fuaran.getBindingValue" / ...);
/// `args` is the kv-list of caller arguments to echo back so the
/// orchestrator can correlate the error with the call site.
let renderError (toolName: string) (args: (string * string) list) (err: IntrospectError) : string =
    buildJson (fun jw -> writeIntrospectError jw toolName args err)
