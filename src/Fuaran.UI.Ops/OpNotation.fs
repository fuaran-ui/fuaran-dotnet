module Fuaran.UI.Ops.OpNotation

// ============================================================================
//  Op-diff review notation (Phase 381) — a human-readable `TreeOp` projection.
//
//  Principle 11 promises "AI-emit / human-read". The emit half has a canonical
//  surface (the wire JSON); the read half had two, and neither is a *diff*: the
//  rendered UI shows the result, and the canonical JSON shows the op with
//  sorted keys and `$type` tags. Neither answers the question a reviewer
//  actually asks between two turns — *what did the AI change?*
//
//  This module answers it: one line per op, `NodeId`-anchored, delta-only.
//
//      revenue-kpi: Source → $query.netRevenue
//      channel-grid: + child Metric "margin-kpi"
//      metric-1: style → emphasis=Loud tone=Success weight=Spacious
//
//  ── PROJECTION ONLY ───────────────────────────────────────────────────────
//
//  This is OUTPUT, not input. There is no decoder, no round-trip obligation,
//  and no wire-format change: the module exports `render*` functions and
//  nothing else. The notation is deliberately NOT a second syntax the hosts
//  must conform to — the canonical JSON remains the only interchange form, and
//  a notation change is a docs/golden change, never a conformance event.
//
//  ── GRAMMAR ───────────────────────────────────────────────────────────────
//
//  Every op renders as `<anchor>: <delta>`, where `<anchor>` is the NodeId the
//  op addresses (`<root>` for `ReplaceRoot`, which addresses the whole tree,
//  and `batch` for `Batch`, which addresses nothing directly):
//
//    EditNode         `<id>: kind → <Kind>`
//    UpdateProp       `<id>: <path> → <value>`         (bare value)
//    ReplaceBinding   `<id>: <slot> → <binding>`       (always a NAMED form —
//                                                       see below; that is how
//                                                       a binding replacement
//                                                       is told apart from a
//                                                       property update)
//    UpdateStyle      `<id>: style → <k>=<v> …`        (or `(defaults)`)
//    UpdateState      `<id>: state → <slot>=<node> …`  (or `(cleared)`)
//    InsertChild      `<parentId>: + child <node>`
//    RemoveNode       `<id>: - node`
//    MoveNode         `<id>: move → parent <newParentId>`
//    ReorderChildren  `<parentId>: reorder → [<id>, …]`
//    ReplaceRoot      `<root>: replace → <node>`
//    Batch            `batch (<n> ops):` + the inner ops, indented two spaces
//                     per nesting level
//
//  `<node>` is a summary, never a subtree dump (delta-only): the kind name, the
//  quoted id, and — when the subtree holds more than one node — its node count,
//  e.g. `Metric "margin-kpi"` / `Dashboard "dash-root" (9 nodes)`.
//
//  `<binding>` names its form so a binding is never mistaken for a plain value:
//
//    Static      `static <value>` / `static none`
//    Query       `$query.<name>` (+ ` deps[a, b]` when declared)
//    Filter      `$filter.<name>`
//    Selection   `$selection.<nodeId>` (+ `.<field>`)
//    State       `$state.<key>`
//    Computed    `<closure>`                (host-only — erases on the wire)
//    Local       `$local(<initialFrom>)`
//    Format      `$format(<source>)`
//    I18n        `$i18n.<key>`
//    Transform   `$transform(<n> steps)`
//    Invoke      `$invoke.<capabilityId>`
//
//  Erased values render as the EXISTING sentinels rather than inventing new
//  vocabulary: `<closure>` for a closure-bearing slot (`Binding.Computed`, a
//  `StateBehaviour.OnError` renderer) and `<opaque>` for a `PropValue.Native`
//  payload outside the encodable scalar set — the same two tokens the canonical
//  encoder emits, so a reviewer reading the notation and the JSON side by side
//  sees one vocabulary.
//
//  Scalar payloads route through `Fuaran.Core.Canon` (canonical escaping, the
//  pinned cross-host float layout, Ordinal-sorted object keys), so the notation
//  is deterministic AND key-order independent: two `JVal` payloads differing
//  only in field order render to the same line.
//
//  ── RECORD / TURN FRAMING ─────────────────────────────────────────────────
//
//  `renderTurn` / `renderTail` add the provenance frame over the op lines so a
//  stream tail reads as a change-log:
//
//      turn 12 · agent claude-fable-5 · 3 ops:
//        revenue-kpi: Source → $query.netRevenue
//        …
//
//  `Attribution` is a deliberate two-case MIRROR of the op-stream's `Actor`,
//  not a reference to it: `Fuaran.UI.OpStream.Abstractions` sits ABOVE this
//  package in the dependency graph (it references `Fuaran.UI.Ops`), so taking
//  `OpRecord` here would close a project-reference cycle. A record-holding
//  caller maps `Actor.Human id → Attribution.Human id` at the seam — three
//  lines — and in exchange the actor rendering stays pinned by this package's
//  own goldens rather than by whichever consumer happened to format it.
//
//  ── INTENDED CONSUMERS ────────────────────────────────────────────────────
//
//   * Inspector tabs — an "ops" pane beside the existing tree / JSON / source
//     projections, showing what the last turn changed rather than what the
//     tree now is. Wiring a playground inspector tab is follow-on work in that
//     surface's OWNING sibling, not here; this package ships the projection.
//   * Op-stream tail views — a live change-log over an `IOpStreamSink` tail
//     (`renderTail`), the shape an operator watches while an agent drives.
//   * PR review of committed op streams — a committed JSONL stream is
//     unreviewable as canonical JSON; `renderTail` over the decoded records
//     turns a diff of the stream into a diff of the CHANGES, which is what a
//     reviewer is actually approving.
//
//  All three read; none of them write. A consumer that wants to round-trip
//  notation back into ops has misread this module — decode the canonical JSON.
// ============================================================================

open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types

// ─── Sentinels (shared vocabulary with the canonical encoder) ───────────────

/// A closure-bearing slot: host-only, erased on the wire.
let private closureSentinel = "<closure>"

/// A `PropValue.Native` payload outside the encodable scalar set.
let private opaqueSentinel = "<opaque>"

// ─── Scalar rendering ───────────────────────────────────────────────────────

let private nodeIdText (NodeId raw) : string = raw

/// A canonically-escaped, quoted string — `Canon` owns the escape rules so the
/// notation never invents a second escaping convention.
let private quoted (s: string) : string = Canon.render (JStr s)

/// Best-effort scalar rendering of an erased `obj` payload. Mirrors the
/// canonical encoder's `appendObj` recognition set exactly (no reflection —
/// this package is Fable-portable); anything outside it is `<opaque>`, the same
/// verdict the encoder reaches.
let private objText (v: obj | null) : string =
    match v with
    | null -> "null"
    | :? string as s -> quoted s
    | :? bool as b -> (if b then "true" else "false")
    | :? int as n -> string n
    | :? int64 as n -> string n
    | :? float as f -> Canon.canonicalFloat f
    | :? float32 as f -> Canon.canonicalFloat (float f)
    | :? System.DateTimeOffset as t -> string (t.ToUnixTimeSeconds())
    | :? System.DateTime as t ->
        string (System.DateTimeOffset(t.ToUniversalTime(), System.TimeSpan.Zero).ToUnixTimeSeconds())
    | _ -> opaqueSentinel

/// `UpdateProp`'s two honest populations: `Wire` renders canonically (so field
/// order in the source JSON cannot change the line); `Native` renders through
/// the scalar set above.
let private propValueText (v: PropValue) : string =
    match v with
    | PropValue.Native o -> objText o
    | PropValue.Wire j -> Canon.render j

// ─── Bindings ───────────────────────────────────────────────────────────────

let private depsSuffix (dependsOn: string list option) : string =
    match dependsOn with
    | Some(_ :: _ as deps) -> " deps[" + String.concat ", " deps + "]"
    | _ -> ""

/// The binding projection, generic in the payload type so the nested slots
/// (`Local.initialFrom` at `'T`, `Format.source` at `float`) render through the
/// same grammar. Explicitly annotated because `Format` instantiates the
/// recursion at a different type — the same polymorphic-recursion shape the
/// canonical encoder's `encodeBindingWith` uses.
///
/// The match is exhaustive by construction: a new `Binding` case fails this
/// build, so the notation cannot silently omit a form the language gained.
let rec private bindingTextWith<'T> (staticText: 'T -> string) (b: Binding<'T>) : string =
    match b with
    | Binding.Static v ->
        match v with
        | Some x -> "static " + staticText x
        | None -> "static none"
    | Binding.Query(name, _, dependsOn) -> "$query." + name + depsSuffix dependsOn
    | Binding.Filter(name, _) -> "$filter." + name
    | Binding.Selection(nodeId, _, _, field) ->
        "$selection."
        + nodeId
        + (match field with
           | Some f -> "." + f
           | None -> "")
    | Binding.State(key, _) -> "$state." + key
    | Binding.Computed _ -> closureSentinel
    | Binding.Local(_, _, initialFrom, _, _) -> "$local(" + bindingTextWith<'T> staticText initialFrom + ")"
    | Binding.Format(source, _, _) -> "$format(" + bindingTextFloat source + ")"
    | Binding.I18n(key, _) -> "$i18n." + key
    | Binding.Transform(_, pipeline, _) -> "$transform(" + string (List.length pipeline) + " steps)"
    | Binding.Invoke(capabilityId, _) -> "$invoke." + capabilityId

and private bindingTextFloat (b: Binding<float>) : string =
    bindingTextWith<float> Canon.canonicalFloat b

/// The slot-erased shape `TreeOp.ReplaceBinding` carries.
let private bindingText (b: Binding<obj>) : string = bindingTextWith<obj> objText b

// ─── Style / state / node summaries ─────────────────────────────────────────

/// `k=v` pairs for the fields the style actually SETS. The set is taken from
/// the generated canonical encoder, which already omits every field at its
/// neutral default — so the notation's idea of "what this style says" cannot
/// drift from the wire's, and a new style field is covered without an edit
/// here.
let private styleText (style: SemanticStyle) : string =
    let scalar (j: JVal) : string =
        match j with
        | JStr s -> s
        | other -> Canon.render other

    match Fuaran.UI.Generated.encodeSemanticStyleJson style with
    | JObj [] -> "(defaults)"
    | JObj fields ->
        fields
        |> List.sortBy fst
        |> List.map (fun (k, v) -> k + "=" + scalar v)
        |> String.concat " "
    | other -> Canon.render other

/// `<Kind> "<id>"`, plus the subtree size when it holds more than one node.
/// Delta-only: an inserted or replacement subtree is SUMMARISED, never dumped —
/// the reviewer wanting the contents reads the tree, not the diff.
let private nodeSummary (node: Node<'Msg>) : string =
    let head = Introspect.kindName node.Kind + " " + quoted node.Id

    match List.length (Introspect.allNodeIds node) with
    | 1 -> head
    | n -> head + " (" + string n + " nodes)"

/// The three `StateBehaviour` slots in a fixed order. `OnError` is a renderer
/// function, so it can only ever be reported as present.
let private stateText (state: StateBehaviour<'Msg>) : string =
    let slots =
        [ match state.OnLoading with
          | Some n -> "onLoading=" + nodeSummary n
          | None -> ()
          match state.OnEmpty with
          | Some n -> "onEmpty=" + nodeSummary n
          | None -> ()
          match state.OnError with
          | Some _ -> "onError=" + closureSentinel
          | None -> () ]

    if List.isEmpty slots then
        "(cleared)"
    else
        String.concat " " slots

// ─── Op lines ───────────────────────────────────────────────────────────────

/// One op → its line(s) at `depth` nesting levels of indentation. Only `Batch`
/// yields more than one line.
///
/// Exhaustive over `TreeOp` by construction — a new op case fails this build
/// rather than rendering as nothing.
let rec private opLines (depth: int) (op: TreeOp<'Msg>) : string list =
    let pad = String.replicate depth "  "

    match op with
    | TreeOp.EditNode(id, newKind) -> [ pad + nodeIdText id + ": kind → " + Introspect.kindName newKind ]
    | TreeOp.UpdateProp(id, path, value) -> [ pad + nodeIdText id + ": " + path + " → " + propValueText value ]
    | TreeOp.ReplaceBinding(id, slot, binding) -> [ pad + nodeIdText id + ": " + slot + " → " + bindingText binding ]
    | TreeOp.UpdateStyle(id, style) -> [ pad + nodeIdText id + ": style → " + styleText style ]
    | TreeOp.UpdateState(id, state) -> [ pad + nodeIdText id + ": state → " + stateText state ]
    | TreeOp.InsertChild(parentId, child) -> [ pad + nodeIdText parentId + ": + child " + nodeSummary child ]
    | TreeOp.RemoveNode id -> [ pad + nodeIdText id + ": - node" ]
    | TreeOp.MoveNode(id, newParentId) -> [ pad + nodeIdText id + ": move → parent " + nodeIdText newParentId ]
    | TreeOp.ReorderChildren(parentId, newOrder) ->
        [ pad
          + nodeIdText parentId
          + ": reorder → ["
          + (newOrder |> List.map nodeIdText |> String.concat ", ")
          + "]" ]
    | TreeOp.ReplaceRoot node -> [ pad + "<root>: replace → " + nodeSummary node ]
    | TreeOp.Batch ops ->
        (pad + "batch (" + string (List.length ops) + " ops):")
        :: (ops |> List.collect (opLines (depth + 1)))

/// Project one op. A `Batch` renders as its header line plus its inner ops,
/// indented two spaces per nesting level; every other op is a single line.
let render (op: TreeOp<'Msg>) : string = opLines 0 op |> String.concat "\n"

/// Project a list of ops — one line per op, no frame. `renderTurn` is the
/// framed counterpart.
let renderOps (ops: TreeOp<'Msg> list) : string =
    ops |> List.collect (opLines 0) |> String.concat "\n"

// ─── Provenance framing ─────────────────────────────────────────────────────

/// Who authored a turn. A deliberate mirror of the op-stream's `Actor` (see the
/// module header) — this package sits BELOW `Fuaran.UI.OpStream.Abstractions`,
/// so it names the attribution rather than importing it.
[<RequireQualifiedAccess>]
type Attribution =
    /// A person, by their stable id.
    | Human of id: string
    /// An agent, by model and stable id. The id is rendered only when it adds
    /// something the model name does not.
    | Agent of model: string * id: string
    /// No attribution recorded — the frame omits the actor segment entirely
    /// rather than asserting an unknown author.
    | Unattributed

/// The provenance frame rendered above a turn's op lines. Every field is
/// optional so a caller emits only what its records carry.
type OpFrame =
    {
        /// The turn / sequence number this batch of ops belongs to.
        Turn: int option
        /// Who authored the turn.
        Actor: Attribution
        /// The prompt that produced the turn, when the stream records one.
        PromptId: string option
    }

[<RequireQualifiedAccess>]
module OpFrame =

    /// A frame asserting nothing — the header degrades to the op count alone.
    let empty: OpFrame =
        { Turn = None
          Actor = Attribution.Unattributed
          PromptId = None }

    /// A frame for an agent-authored turn.
    let agent (turn: int) (model: string) (id: string) : OpFrame =
        { Turn = Some turn
          Actor = Attribution.Agent(model, id)
          PromptId = None }

    /// A frame for a human-authored turn.
    let human (turn: int) (id: string) : OpFrame =
        { Turn = Some turn
          Actor = Attribution.Human id
          PromptId = None }

let private attributionText (a: Attribution) : string option =
    match a with
    | Attribution.Unattributed -> None
    | Attribution.Human id -> Some("human " + id)
    | Attribution.Agent(model, id) ->
        if id = "" || id = model then
            Some("agent " + model)
        else
            Some("agent " + model + " (" + id + ")")

/// The frame header — `turn 12 · agent claude-fable-5 · 3 ops:`. The op count
/// is the number of TOP-LEVEL ops in the turn; a `Batch` counts as the one op
/// it is, and states its own inner count on its line.
let private frameHeader (frame: OpFrame) (opCount: int) : string =
    let segments =
        [ match frame.Turn with
          | Some t -> "turn " + string t
          | None -> ()
          match attributionText frame.Actor with
          | Some a -> a
          | None -> ()
          match frame.PromptId with
          | Some p -> "prompt " + p
          | None -> ()
          (if opCount = 1 then "1 op" else string opCount + " ops") ]

    String.concat " · " segments + ":"

/// Project one turn: the provenance header, then the turn's ops indented one
/// level beneath it.
let renderTurn (frame: OpFrame) (ops: TreeOp<'Msg> list) : string =
    let body = ops |> List.collect (opLines 1)
    String.concat "\n" (frameHeader frame (List.length ops) :: body)

/// Project a stream tail as a change-log — one framed block per turn, blank-line
/// separated. This is the PR-review / tail-view shape: the reader sees who
/// changed what, in order, without decoding a line of canonical JSON.
let renderTail (turns: (OpFrame * TreeOp<'Msg> list) list) : string =
    turns
    |> List.map (fun (frame, ops) -> renderTurn frame ops)
    |> String.concat "\n\n"
