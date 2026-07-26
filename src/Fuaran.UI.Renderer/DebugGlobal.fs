module Fuaran.UI.Renderer.DebugGlobal

// ============================================================================
//  Fuaran — in-page introspection REPL (`window.__fuaran`) — F# Fable host
//  (Phase 90).
//
//  When the renderer is mounted with the debug flag set, it registers a single
//  guarded global — `window.__fuaran` — that exposes the running UI's *typed
//  layer* to the browser DevTools console. A developer types
//  `__fuaran.getNodeState("submit-btn")` and receives the typed snapshot — the
//  node's kind, its bound binding slots, its structural child ids — plus
//  per-slot resolved values (`getBindingValue`) and live DOM geometry
//  (`getRenderedDom`), not the untyped post-projection DOM node the browser
//  console would otherwise hand back. A single policy-gated `apply(opJson)`
//  mutation entry rounds out the surface.
//
//  This is the F# half of the cross-host REPL; the TS half shipped first
//  (`@fuaran-ui/renderer` `debugGlobal.ts`) and is the parity reference for
//  method names + payload shapes. The two hosts present an identical console
//  surface so a debugging session is host-agnostic.
//
//  ── Why a renderer-side replica of the introspection table, not a call into
//     `Fuaran.UI.AiTools` ──────────────────────────────────────────────────
//  `Fuaran.UI.AiTools` (the canonical §4i introspection tier) is .NET-targeted
//  — its `ResponseRender` boundary is `System.Text.Json`-based, and it depends
//  on `Fuaran.UI.Ops`, which is deliberately kept OUT of the renderer's Fable
//  graph (see the `#nowarn "67"` note at the top of `Render.fs`). Referencing
//  it here would re-introduce the non-Fable-portable Ops surface. So — exactly
//  as the TS half re-implements `kindName` / `extractBindingSlots` /
//  `bindingForSlot` / `childNodes` inside `@fuaran-ui/ai-tools` rather than
//  importing a shared module — this module replicates the small wire-shape
//  helpers (`kindName` mirrors `Ops.Introspect.kindName`;
//  `extractBindingSlots` / `resolveSlot` mirror `AiTools.Tools.extractBindings`
//  / `extractSlot`; `bindingExpression` mirrors `AiTools.BindingProbe.identify`)
//  against the live tree the renderer already holds, resolving binding *values*
//  through the renderer's own `BindingResolver`. The helpers stay pure F# over
//  `Fuaran.UI.Types`, so they compile on BOTH pipelines and the .NET test
//  runner pins them without a browser (Fable-vs-.NET pipeline parity).
//
//  FGP 4 (diagnostics discipline): this module emits NO `console.*` of its own
//  — `helpText` is a string the caller prints, every method returns a value,
//  and the only diagnostic path (a denied `apply`) routes through
//  `IFuaranRuntime.Warn`, the renderer's centralised warn channel.
//
//  FGP 3 (default-deny by shape): `apply(opJson)` routes through the runtime's
//  `CanDispatch` policy gate (`ActionDescriptor.ApplyTreeOp`) BEFORE the host's
//  apply handler ever decodes the op — a denied op returns the structured deny
//  envelope and never mutates the tree.
//
//  Gating: the global is registered only under a DEBUG build AND an explicit
//  host opt-in (`register debug:true …`). A release build sets
//  `compiledInDebug = false`, so `shouldRegister` is constant-false and the
//  registration is dead-code-eliminated — `window.__fuaran` is `undefined` in
//  production. The shape is explicitly DEBUG-ONLY / UNSTABLE and excluded from
//  semver (see `STABILITY.md`).
// ============================================================================

open Fuaran.UI.Types
open Fuaran.UI.Telemetry.Abstractions

/// Schema version of the `window.__fuaran` shape — independent of the
/// renderer package's semver (the global is debug-only / unstable). Mirrors
/// the TS `DEBUG_GLOBAL_VERSION`.
[<Literal>]
let Version = "0.1.0"

/// True iff this assembly was compiled under a DEBUG build. A Release pack
/// sets it `false`, which makes `shouldRegister` constant-false so the whole
/// registration is dead-code-eliminated — `window.__fuaran` is `undefined` in
/// production (the F# counterpart of the TS `import.meta.env.DEV` gate).
let compiledInDebug =
#if DEBUG
    true
#else
    false
#endif

/// The registration predicate: register the global only when the host opted
/// in (`debug = true`) AND the build is a DEBUG build. Pure + identical on
/// both pipelines, so the .NET test runner pins the gating contract directly.
let shouldRegister (debug: bool) : bool = debug && compiledInDebug

// ─── Wire kind discriminator ─────────────────────────────────────────────────
//
// Delegates to the base package's `Kind.name` — the single canonical kind-tag
// vocabulary (also what `Ops.Introspect.kindName` re-exports and what
// `HoleDecl.Slot` kind-constraints are matched against). This console surface
// previously carried its own hand-rolled mirror of the match; a drifted copy
// would have mislabelled the debug output relative to the enforcement
// vocabulary, so the copy is gone.

let kindName (kind: NodeKind<'Msg>) : string = Kind.name kind

// ─── Binding expression mapping (mirror of AiTools.BindingProbe.identify) ───
//
// Returns the (source-token, wire-form-expression) pair for a binding. The
// source token matches `ResponseRender.bindingSourceToken` ("Static" / "Query"
// / …); the expression matches the canonical `$static` / `$queries.<name>` /
// `$state.<key>` / … forms. `Local` → ("Computed", "$local") and `Format` →
// ("Computed", "$format"), matching the F# tier.

let bindingExpression (binding: Binding<'T>) : string * string =
    match binding with
    | Binding.Static _ -> "Static", "$static"
    | Binding.Query(name, _, _) -> "Query", sprintf "$queries.%s" name
    | Binding.Filter(name, _) -> "Filter", sprintf "$filters.%s" name
    | Binding.Selection(NodeId raw, _, _, _) -> "Selection", sprintf "$selection.%s" raw
    | Binding.State(key, _) -> "State", sprintf "$state.%s" key
    | Binding.Computed _ -> "Computed", "$computed"
    | Binding.I18n(key, _) -> "I18n", sprintf "$i18n.%s" key
    | Binding.Local _ -> "Computed", "$local"
    | Binding.Format _ -> "Computed", "$format"
    | Binding.Transform _ -> "Computed", "$transform"
    | Binding.Invoke _ -> "Computed", "$invoke"

/// One bound binding slot on a node — the slot name, its wire-form expression,
/// and the `Binding` case it came from. Mirrors the TS `BindingSlotInfo`.
type BindingSlotInfo =
    { Slot: string
      Expression: string
      Source: string }

let private slotInfo (name: string) (binding: Binding<'T>) : BindingSlotInfo =
    let source, expression = bindingExpression binding

    { Slot = name
      Expression = expression
      Source = source }

// ─── extractBindingSlots (mirror of AiTools.Tools.extractBindings) ──────────
//
// The bound binding slots a node carries, in the canonical `extractBindings`
// order. Optional slots (Metric.Trend, Tabs.ActiveTag,
// Button/Select/Form/FileUpload.Disabled) appear only when present — the same
// table the Phase 118 consistency property test pins. Forward-coupling: a new
// binding-typed slot on any spec must extend BOTH this and `resolveSlot` below.

let extractBindingSlots (kind: NodeKind<'Msg>) : BindingSlotInfo list =
    match kind with
    | NodeKind.Display(DisplayKind.Metric spec) ->
        [ slotInfo "Value" spec.Value
          match spec.Trend with
          | Some trend -> slotInfo "Trend" trend
          | None -> () ]
    | NodeKind.Display(DisplayKind.Sparkline spec) -> [ slotInfo "Source" spec.Source ]
    | NodeKind.Display(DisplayKind.Progress spec) -> [ slotInfo "Fraction" spec.Fraction ]
    | NodeKind.Display(DisplayKind.LabelValueRow spec) -> [ slotInfo "Value" spec.Value ]
    | NodeKind.Display(DisplayKind.Link spec) -> [ slotInfo "Href" spec.Href ]
    | NodeKind.Layout(LayoutKind.Stepper spec) -> [ slotInfo "ActiveStep" spec.ActiveStep ]
    | NodeKind.Layout(LayoutKind.Tabs spec) ->
        [ slotInfo "ActiveIndex" spec.ActiveIndex
          match spec.ActiveTag with
          | Some tag -> slotInfo "ActiveTag" tag
          | None -> () ]
    | NodeKind.Layout(LayoutKind.Disclosure spec) -> [ slotInfo "Open" spec.Open ]
    | NodeKind.Input(InputKind.Button spec) ->
        match spec.Disabled with
        | Some disabled -> [ slotInfo "Disabled" disabled ]
        | None -> []
    | NodeKind.Input(InputKind.Select spec) ->
        [ slotInfo "Source" spec.Source
          slotInfo "Value" spec.Value
          match spec.Disabled with
          | Some disabled -> slotInfo "Disabled" disabled
          | None -> () ]
    | NodeKind.Input(InputKind.Form spec) ->
        match spec.Disabled with
        | Some disabled -> [ slotInfo "Disabled" disabled ]
        | None -> []
    | NodeKind.Input(InputKind.FileUpload spec) ->
        match spec.Disabled with
        | Some disabled -> [ slotInfo "Disabled" disabled ]
        | None -> []
    | NodeKind.Visualisation(VisKind.DataGrid spec) -> [ slotInfo "Source" spec.Source ]
    | NodeKind.Visualisation(VisKind.Chart spec) -> [ slotInfo "Source" spec.Source ]
    | NodeKind.Visualisation(VisKind.Map spec) -> [ slotInfo "Source" spec.Source ]
    | _ -> []

// ─── Structural traversal (mirror of TS childNodes / walkNodes / findNode) ──
//
// Console-traversal shape: Layout children, ErrorBoundary [child; fallback],
// FragmentDecl [body]. This includes ErrorBoundary arms (unlike the
// apply-engine's `Ops.Introspect.getChildren`, which intentionally omits them)
// so `inspectTree` surfaces every addressable node a developer can query.

let childNodes (node: Node<'Msg>) : Node<'Msg> list =
    match node.Kind with
    | NodeKind.Layout layout ->
        match layout with
        | LayoutKind.Box s -> s.Children
        | LayoutKind.SplitPanel s -> s.Children
        | LayoutKind.Tabs s -> s.Children
        | LayoutKind.Stepper s -> s.Children
        | LayoutKind.SummaryList s -> s.Children
        | LayoutKind.Disclosure s -> s.Children
        | LayoutKind.Modal s -> s.Children
        | LayoutKind.ScrollArea s -> s.Children
    | NodeKind.ErrorBoundary spec -> [ spec.Child; spec.Fallback ]
    | NodeKind.Switch spec -> (spec.Cases |> List.map snd) @ [ spec.Default ]
    | NodeKind.FragmentDecl spec -> [ spec.Body ]
    | NodeKind.Display _
    | NodeKind.Input _
    | NodeKind.Visualisation _
    | NodeKind.Custom _
    | NodeKind.FragmentRef _
    // Mount (§4o) — the guest is a separate scope; its interior nodes are
    // addressable in the guest's own tree, not the host console traversal.
    | NodeKind.Mount _ -> []

/// Depth-first walk of every node in the tree, root first.
let rec walkNodes (tree: Node<'Msg>) : Node<'Msg> list =
    tree :: (childNodes tree |> List.collect walkNodes)

/// The first node with `id` (depth-first), or `None`.
let findNode (id: string) (tree: Node<'Msg>) : Node<'Msg> option =
    walkNodes tree
    |> List.tryFind (fun n ->
        let (NodeId raw) = n.Id
        raw = id)

/// Ids of every node whose wire kind discriminator equals `kind`.
let findNodesByKind (kind: string) (tree: Node<'Msg>) : string list =
    walkNodes tree
    |> List.filter (fun n -> kindName n.Kind = kind)
    |> List.map (fun n ->
        let (NodeId raw) = n.Id
        raw)

// ─── Structural introspection envelope (getNodeState / inspectTree) ─────────

/// The per-node structural snapshot — port of the self-contained subset of
/// F# `NodeState`, matching the TS `NodeIntrospection`.
type NodeIntrospection =
    { Id: string
      Kind: string
      Bindings: BindingSlotInfo list
      ChildIds: string list }

/// A recursive structural snapshot of a whole subtree (TS `TreeIntrospection`).
type TreeIntrospection =
    { Id: string
      Kind: string
      Bindings: BindingSlotInfo list
      ChildIds: string list
      Children: TreeIntrospection list }

let introspectNode (node: Node<'Msg>) : NodeIntrospection =
    let (NodeId raw) = node.Id

    { Id = raw
      Kind = kindName node.Kind
      Bindings = extractBindingSlots node.Kind
      ChildIds =
        childNodes node
        |> List.map (fun c ->
            let (NodeId cid) = c.Id
            cid) }

let rec inspectTree (node: Node<'Msg>) : TreeIntrospection =
    let n = introspectNode node

    { Id = n.Id
      Kind = n.Kind
      Bindings = n.Bindings
      ChildIds = n.ChildIds
      Children = childNodes node |> List.map inspectTree }

// ─── getBindingValue — resolve a single slot against the live sources ───────
//
// Mirrors `AiTools.Tools.extractSlot`, but resolves through the renderer's own
// `BindingResolver` (not the AiTools probe) so the value reflects exactly what
// the live render produced. Optional slots that are absent surface as
// `SlotNoOverride` (the canonical synthetic-`$none` posture — "slot exists, no
// override yet"), distinct from `SlotNotDeclared` ("this slot name is not a
// binding slot on this kind at all").

/// Outcome of resolving one named binding slot on a node.
[<RequireQualifiedAccess>]
type SlotResolution =
    /// The slot resolved to a value. `Value` is obj-boxed at the cross-cutting
    /// console boundary (the console caller reads it untyped).
    | Resolved of value: obj * expression: string * source: string
    /// The slot's data source is registered but has not resolved yet
    /// (`NotResolved` from the renderer's resolver).
    | NotResolved of expression: string * source: string
    /// The slot's resolver returned an error (accessor threw / unbox failed).
    | Errored of message: string * expression: string * source: string
    /// `Binding.I18n` returned the debug-placeholder (no translation for key).
    | I18nUnresolved of key: string * expression: string * source: string
    /// An optional slot that is declared on this kind but currently absent
    /// (`None`) — slot exists, no override (canonical synthetic-`$none`).
    | NoOverride
    /// The slot name is not a binding slot on this node's kind.
    | NotDeclared

let private resolveTo (sources: BindingResolver.BindingSources) (binding: Binding<'T>) : SlotResolution =
    let source, expression = bindingExpression binding

    match BindingResolver.resolve sources binding with
    | BindingResolver.Resolved v -> SlotResolution.Resolved(box v, expression, source)
    | BindingResolver.NotResolved -> SlotResolution.NotResolved(expression, source)
    | BindingResolver.Errored m -> SlotResolution.Errored(m, expression, source)
    | BindingResolver.I18nUnresolved k -> SlotResolution.I18nUnresolved(k, expression, source)

/// Resolve the named binding `slot` on `kind` against `sources`. Returns
/// `NotDeclared` when the slot is not a binding slot on the kind; `NoOverride`
/// when an optional slot is absent.
let resolveSlot (sources: BindingResolver.BindingSources) (kind: NodeKind<'Msg>) (slot: string) : SlotResolution =
    match kind, slot with
    | NodeKind.Display(DisplayKind.Metric spec), "Value" -> resolveTo sources spec.Value
    | NodeKind.Display(DisplayKind.Metric spec), "Trend" ->
        match spec.Trend with
        | Some trend -> resolveTo sources trend
        | None -> SlotResolution.NoOverride
    | NodeKind.Display(DisplayKind.Sparkline spec), "Source" -> resolveTo sources spec.Source
    | NodeKind.Display(DisplayKind.Progress spec), "Fraction" -> resolveTo sources spec.Fraction
    | NodeKind.Display(DisplayKind.LabelValueRow spec), "Value" -> resolveTo sources spec.Value
    | NodeKind.Display(DisplayKind.Link spec), "Href" -> resolveTo sources spec.Href
    | NodeKind.Layout(LayoutKind.Stepper spec), "ActiveStep" -> resolveTo sources spec.ActiveStep
    | NodeKind.Layout(LayoutKind.Tabs spec), "ActiveIndex" -> resolveTo sources spec.ActiveIndex
    | NodeKind.Layout(LayoutKind.Tabs spec), "ActiveTag" ->
        match spec.ActiveTag with
        | Some tag -> resolveTo sources tag
        | None -> SlotResolution.NoOverride
    | NodeKind.Layout(LayoutKind.Disclosure spec), "Open" -> resolveTo sources spec.Open
    | NodeKind.Input(InputKind.Button spec), "Disabled" ->
        match spec.Disabled with
        | Some disabled -> resolveTo sources disabled
        | None -> SlotResolution.NoOverride
    | NodeKind.Input(InputKind.Select spec), "Source" -> resolveTo sources spec.Source
    | NodeKind.Input(InputKind.Select spec), "Value" -> resolveTo sources spec.Value
    | NodeKind.Input(InputKind.Select spec), "Disabled" ->
        match spec.Disabled with
        | Some disabled -> resolveTo sources disabled
        | None -> SlotResolution.NoOverride
    | NodeKind.Input(InputKind.Form spec), "Disabled" ->
        match spec.Disabled with
        | Some disabled -> resolveTo sources disabled
        | None -> SlotResolution.NoOverride
    | NodeKind.Input(InputKind.FileUpload spec), "Disabled" ->
        match spec.Disabled with
        | Some disabled -> resolveTo sources disabled
        | None -> SlotResolution.NoOverride
    | NodeKind.Visualisation(VisKind.DataGrid spec), "Source" -> resolveTo sources spec.Source
    | NodeKind.Visualisation(VisKind.Chart spec), "Source" -> resolveTo sources spec.Source
    | NodeKind.Visualisation(VisKind.Map spec), "Source" -> resolveTo sources spec.Source
    | _ -> SlotResolution.NotDeclared

// ─── Policy-gated apply(op) (FGP 3) ─────────────────────────────────────────
//
// The renderer owns neither the apply engine (`Fuaran.UI.Ops`, outside the
// Fable graph) nor the Elmish tree, so the host wires the actual decode +
// apply + setState as an `ApplyHandler`. The debug global's responsibility is
// to enforce the SAME default-deny contract an AI-tool dispatch obeys: consult
// the runtime's `CanDispatch` gate BEFORE the handler ever decodes the op. A
// denied op returns the structured deny envelope and never reaches the
// handler; an allowed op is forwarded for decode + apply + re-render.

/// Outcome the host-supplied apply handler returns. Surfaced (as a structured
/// envelope) to the console caller.
[<RequireQualifiedAccess>]
type ApplyOutcome =
    /// The op decoded, the gate allowed it, and the host applied it + re-rendered.
    | Applied
    /// The op JSON could not be decoded to a `TreeOp`.
    | DecodeFailed of message: string
    /// The op decoded but the host's apply engine rejected it (structural
    /// apply error). `message` is the apply-error summary.
    | Rejected of message: string

/// Host-supplied "decode + apply + re-render" handler. Invoked ONLY after the
/// policy gate allows the op — it is never called for a denied op.
type ApplyHandler = string -> ApplyOutcome

/// Durable-sink wiring for the in-page apply path (Phase 193). Both legs are
/// optional and default to off, so `DebugSinks.none` reproduces the historical
/// warn-only behaviour exactly.
type DebugSinks =
    {
        /// Where a DENIED apply is recorded. The renderer already depends on the
        /// telemetry abstractions, so this leg is wired in-package.
        TelemetrySink: IFuaranTelemetrySink option
        /// Where a PERMITTED apply's op JSON is handed for journalling. The host
        /// wires this to `ApplyPersist.applyAndPersist` (or its own sink): the
        /// op-stream append helper that owns hash-chaining is not Fable-portable,
        /// so the chaining must not be rebuilt here. See the section header.
        OnApplied: (string -> unit) option
        /// Billing/audit subject recorded on the deny record. A console-driven
        /// mutation is operator-initiated, so hosts that do not model a user can
        /// leave the default.
        UserId: string
    }

[<RequireQualifiedAccess>]
module DebugSinks =

    /// No durable emission — the pre-Phase-193 behaviour (deny warns, permitted
    /// applies journal nowhere). The default for every existing call site.
    let none: DebugSinks =
        { TelemetrySink = None
          OnApplied = None
          UserId = "operator" }

/// The gate decision for an in-page apply. `Ok ()` when the runtime's
/// `CanDispatch(ApplyTreeOp …)` allows the mutation; `Error reason` (the deny
/// diagnostic) when it denies. Pure — the .NET test runner pins it directly,
/// the same way `DispatchGateTests` pins `Render.applyDispatchGate`.
let applyGateDecision (runtime: Runtime.IFuaranRuntime) (opJson: string) : Result<unit, string> =
    let descriptor = Runtime.ActionDescriptor.ApplyTreeOp opJson

    if runtime.CanDispatch descriptor then
        Ok()
    else
        Error(sprintf "apply denied by policy gate: %s" (Runtime.ActionDescriptor.describe descriptor))

// ─── Durable-sink emission for in-page apply (FGP 5, Phase 193) ─────────────
//
// `__fuaran.apply` is a THIRD dispatch path (it joins AI-tool dispatch and the
// fast path). For the op stream to stay the source of truth, a console-driven
// mutation must be as answerable as any other: every permitted op journals, and
// every denial records. Otherwise the console is an unrecorded side channel —
// "what did that session do?" has no answer.
//
// The two legs are wired differently, and not by preference:
//
//   * DENY → telemetry, in-package. `Fuaran.UI.Telemetry.Abstractions` is
//     already a renderer dependency, so `IFuaranTelemetrySink.RecordDeny` is
//     called directly. The deny envelope and the telemetry record are the same
//     event on two surfaces.
//
//   * PERMITTED → op stream, through a HOST SEAM. The op-stream append helper
//     that owns hash-chaining lives in `Fuaran.UI.OpStream.Replay`, which is
//     NOT source-packed for Fable — a browser renderer cannot call it. The only
//     in-renderer alternative would be to rebuild the chained record here,
//     duplicating the chaining logic at a second site; a mis-chained stream is
//     silent corruption of the thing FGP 5 makes authoritative. So the renderer
//     hands the applied op to a host callback and the host journals it with
//     `ApplyPersist.applyAndPersist` — chaining stays in the one function that
//     owns it, and the renderer gains no new dependency (FGP 2).
//
// Both decisions below are PURE so the .NET test runner pins them directly, the
// same way `applyGateDecision` is pinned — the Fable shell above is a thin
// dispatch over these.

/// The stable tool name the in-page apply path records denials under. A
/// dedicated name (not a real AI tool) so the drift detector can tell a
/// console-driven denial from a model-driven one. Parity-locked with the
/// TypeScript mirror.
[<Literal>]
let ApplyToolName = "__fuaran.apply"

/// Build the deny record for a rejected in-page apply. Pure, so the emitted
/// shape is pinned by a test and stays parity-locked with the TS mirror.
/// `ActiveModule` / `ActivePage` / `PromptId` are `None`: a console-driven
/// mutation is operator-initiated and belongs to no module, page, or prompt.
let denyTelemetry (userId: string) (occurredAt: System.DateTimeOffset) (reason: string) : DenyTelemetry =
    { ToolName = ApplyToolName
      Reason = reason
      ActiveModule = None
      ActivePage = None
      PromptId = None
      UserId = userId
      Timestamp = occurredAt }

/// Whether an outcome should be journalled to the op stream. ONLY a genuinely
/// applied op is durable: a decode failure never produced a `TreeOp`, and a
/// rejected one changed no tree, so journalling either would put an op in the
/// stream that never happened.
let shouldJournal (outcome: ApplyOutcome) : bool =
    match outcome with
    | ApplyOutcome.Applied -> true
    | ApplyOutcome.DecodeFailed _
    | ApplyOutcome.Rejected _ -> false

// ─── help() text ────────────────────────────────────────────────────────────

/// One-screen reference of the available methods. `help()` returns this for
/// the caller to print — the module never writes to `console.*` itself (FGP 4).
let helpText =
    "window.__fuaran — Fuaran in-page introspection (DEBUG-only, unstable)\n"
    + "  .getNodeState(id)         typed snapshot: kind, bound binding slots, child ids\n"
    + "  .getBindingValue(id,slot) resolve one binding slot's current value\n"
    + "  .getRenderedDom(id)       live DOM geometry (x/y/size + overflow/hidden flags)\n"
    + "  .inspectTree()            recursive structural snapshot of the whole tree\n"
    + "  .findNodes(kind)          ids of every node whose kind === <kind>\n"
    + "  .apply(opJson)            policy-gated TreeOp mutation (default-deny; deny → envelope)\n"
    + "  .help()                   this text\n"
    + "Tip: __fuaran.inspectTree() lists every node id you can query."

#if FABLE_COMPILER
open Fable.Core
open Fable.Core.JsInterop

// ─── Live DOM geometry (getRenderedDom) ─────────────────────────────────────
//
// Reads the `[data-fuaran-node-id]` element the renderer emits for the
// addressed node. Mirrors the TS `readGeometry` — bounding box + an
// overflowing flag (scroll size exceeds client size) + a hidden flag
// (display:none / visibility:hidden / zero-box). Self-contained JS (same
// `[<Emit>]`-the-whole-DOM-read posture `BrowserLayoutObserver` uses) so the
// read doesn't depend on the `Browser.Types` typed surface; returns a POJO or
// `null` (no element / server context).

[<Emit("""(function(id){
  if (typeof document === 'undefined') return null;
  var esc = (typeof CSS !== 'undefined' && CSS.escape) ? CSS.escape(id) : String(id).replace(/["\\]/g, '\\$&');
  var el = document.querySelector('[data-fuaran-node-id="' + esc + '"]');
  if (el === null) return null;
  var r = el.getBoundingClientRect();
  var overflowing = el.scrollWidth > el.clientWidth || el.scrollHeight > el.clientHeight;
  var st = (typeof window !== 'undefined') ? window.getComputedStyle(el) : undefined;
  var hidden = (st && (st.display === 'none' || st.visibility === 'hidden')) || (r.width === 0 && r.height === 0);
  return { x: r.x, y: r.y, width: r.width, height: r.height, overflowing: overflowing, hidden: hidden };
})($0)""")>]
let private readGeometryRaw (nodeId: string) : obj = jsNative

let private geometryObj (nodeId: string) : obj =
    let geometry = readGeometryRaw nodeId

    if isNull geometry then
        createObj [ "error", box (sprintf "No rendered element for node '%s'." nodeId) ]
    else
        geometry

// ─── POJO materialisation — F# records → camelCase JS objects ───────────────
//
// The console hands back plain JS objects (best DevTools UX — the developer
// drills into them), whose `JSON.stringify` matches the camelCase wire
// envelope. F# lists are materialised to real JS arrays via `Array.ofList`.

let private slotInfoToObj (s: BindingSlotInfo) : obj =
    createObj [ "slot", box s.Slot; "expression", box s.Expression; "source", box s.Source ]

let private slotInfosToArray (slots: BindingSlotInfo list) : obj =
    slots |> List.map slotInfoToObj |> Array.ofList |> box

let private nodeIntrospectionToObj (n: NodeIntrospection) : obj =
    createObj
        [ "id", box n.Id
          "kind", box n.Kind
          "bindings", slotInfosToArray n.Bindings
          "childIds", box (Array.ofList n.ChildIds) ]

let rec private treeIntrospectionToObj (t: TreeIntrospection) : obj =
    createObj
        [ "id", box t.Id
          "kind", box t.Kind
          "bindings", slotInfosToArray t.Bindings
          "childIds", box (Array.ofList t.ChildIds)
          "children", box (t.Children |> List.map treeIntrospectionToObj |> Array.ofList) ]

let private slotResolutionToObj (kindForError: string) (nodeId: string) (slot: string) (r: SlotResolution) : obj =
    match r with
    | SlotResolution.Resolved(value, expression, source) ->
        createObj
            [ "status", box "resolved"
              "value", value
              "expression", box expression
              "source", box source ]
    | SlotResolution.NotResolved(expression, source) ->
        createObj
            [ "status", box "notResolved"
              "expression", box expression
              "source", box source ]
    | SlotResolution.Errored(message, expression, source) ->
        createObj
            [ "status", box "errored"
              "message", box message
              "expression", box expression
              "source", box source ]
    | SlotResolution.I18nUnresolved(key, expression, source) ->
        createObj
            [ "status", box "i18nUnresolved"
              "key", box key
              "expression", box expression
              "source", box source ]
    | SlotResolution.NoOverride ->
        createObj
            [ "status", box "noOverride"
              "expression", box "$none"
              "source", box "Static" ]
    | SlotResolution.NotDeclared ->
        createObj
            [ "error",
              box (
                  sprintf
                      "Slot '%s' is not a binding slot on node '%s' (kind=%s). Use getNodeState(id).bindings to list the slots."
                      slot
                      nodeId
                      kindForError
              ) ]

let private applyEnvelope
    (runtime: Runtime.IFuaranRuntime)
    (sinks: DebugSinks)
    (handler: ApplyHandler option)
    (opJson: string)
    : obj =
    match handler with
    | None ->
        createObj
            [ "ok", box false
              "status", box "unwired"
              "error", box "apply is not wired on this host (no apply handler supplied to register)." ]
    | Some h ->
        match applyGateDecision runtime opJson with
        | Error reason ->
            // FGP 4: route the deny diagnostic through the renderer's
            // centralised warn channel — never raw console.*.
            runtime.Warn reason

            // FGP 5 (Phase 193): the deny envelope and the telemetry record are
            // the SAME event on two surfaces. Fire-and-forget — a failing sink
            // must never gate dispatch, so it cannot change what the caller sees.
            sinks.TelemetrySink
            |> Option.iter (fun sink ->
                try
                    sink.RecordDeny(denyTelemetry sinks.UserId System.DateTimeOffset.UtcNow reason)
                with _ ->
                    ())

            createObj
                [ "ok", box false
                  "status", box "denied"
                  "denied", box true
                  "error", box reason ]
        | Ok() ->
            let outcome = h opJson

            // Only a genuinely applied op is durable — see `shouldJournal`.
            if shouldJournal outcome then
                sinks.OnApplied
                |> Option.iter (fun journal ->
                    try
                        journal opJson
                    with _ ->
                        ())

            match outcome with
            | ApplyOutcome.Applied -> createObj [ "ok", box true; "status", box "applied" ]
            | ApplyOutcome.DecodeFailed m -> createObj [ "ok", box false; "status", box "decodeFailed"; "error", box m ]
            | ApplyOutcome.Rejected m -> createObj [ "ok", box false; "status", box "rejected"; "error", box m ]

// ─── Build + register the `window.__fuaran` surface ─────────────────────────
//
// `buildGlobal` closes over the CURRENT tree + sources, so re-registering each
// render keeps the global pointed at the live tree (the host calls `register`
// from its render path). Multi-arg methods are exposed via `System.Func` so
// Fable emits real n-ary JS functions (`__fuaran.getBindingValue(id, slot)`),
// not curried unary chains.

let buildGlobalWithSinks
    (tree: Node<'Msg>)
    (sources: BindingResolver.BindingSources)
    (runtime: Runtime.IFuaranRuntime)
    (sinks: DebugSinks)
    (applyHandler: ApplyHandler option)
    : obj =
    createObj
        [ "version", box Version
          "getNodeState",
          box (
              System.Func<string, obj>(fun id ->
                  match findNode id tree with
                  | Some node -> nodeIntrospectionToObj (introspectNode node)
                  | None -> createObj [ "error", box (sprintf "Node '%s' not found in tree." id) ])
          )
          "getBindingValue",
          box (
              System.Func<string, string, obj>(fun id slot ->
                  match findNode id tree with
                  | None -> createObj [ "error", box (sprintf "Node '%s' not found in tree." id) ]
                  | Some node -> slotResolutionToObj (kindName node.Kind) id slot (resolveSlot sources node.Kind slot))
          )
          "getRenderedDom", box (System.Func<string, obj>(fun id -> geometryObj id))
          "inspectTree", box (System.Func<obj>(fun () -> treeIntrospectionToObj (inspectTree tree)))
          "findNodes", box (System.Func<string, obj>(fun kind -> box (Array.ofList (findNodesByKind kind tree))))
          "apply", box (System.Func<string, obj>(fun opJson -> applyEnvelope runtime sinks applyHandler opJson))
          "help", box (System.Func<obj>(fun () -> box helpText)) ]

/// `buildGlobalWithSinks` with no durable emission — the historical surface.
let buildGlobal
    (tree: Node<'Msg>)
    (sources: BindingResolver.BindingSources)
    (runtime: Runtime.IFuaranRuntime)
    (applyHandler: ApplyHandler option)
    : obj =
    buildGlobalWithSinks tree sources runtime DebugSinks.none applyHandler

[<Emit("(typeof globalThis !== 'undefined') ? (globalThis.__fuaran = $0, true) : false")>]
let private setWindowGlobal (value: obj) : bool = jsNative

[<Emit("(typeof globalThis !== 'undefined' && globalThis.__fuaran) ? (delete globalThis.__fuaran, true) : false")>]
let private clearWindowGlobal () : bool = jsNative

/// Register `window.__fuaran` over the current `tree` + `sources` — but ONLY
/// when `shouldRegister debug` (DEBUG build + host opt-in). A no-op otherwise,
/// so production / opt-out hosts leave the global `undefined`. `applyHandler`
/// is `None` for read-only hosts (apply returns the `unwired` envelope) and
/// `Some` for hosts that own a TreeOp apply + setState loop. Call from the
/// render path to keep the global pointed at the live tree.
let registerWithSinks
    (debug: bool)
    (tree: Node<'Msg>)
    (sources: BindingResolver.BindingSources)
    (runtime: Runtime.IFuaranRuntime)
    (sinks: DebugSinks)
    (applyHandler: ApplyHandler option)
    : unit =
    if shouldRegister debug then
        setWindowGlobal (buildGlobalWithSinks tree sources runtime sinks applyHandler)
        |> ignore

/// `registerWithSinks` with no durable emission — the historical surface.
let register
    (debug: bool)
    (tree: Node<'Msg>)
    (sources: BindingResolver.BindingSources)
    (runtime: Runtime.IFuaranRuntime)
    (applyHandler: ApplyHandler option)
    : unit =
    registerWithSinks debug tree sources runtime DebugSinks.none applyHandler

/// Remove `window.__fuaran` if present. Safe to call unconditionally (e.g. a
/// React effect cleanup).
let unregister () : unit = clearWindowGlobal () |> ignore

#else

/// Non-Fable hosts (the .NET test runner / SSR) have no `window`; registration
/// is a no-op. The pure introspection helpers above are still exercised by the
/// .NET test runner.
let registerWithSinks
    (_debug: bool)
    (_tree: Node<'Msg>)
    (_sources: BindingResolver.BindingSources)
    (_runtime: Runtime.IFuaranRuntime)
    (_sinks: DebugSinks)
    (_applyHandler: ApplyHandler option)
    : unit =
    ()

let register
    (_debug: bool)
    (_tree: Node<'Msg>)
    (_sources: BindingResolver.BindingSources)
    (_runtime: Runtime.IFuaranRuntime)
    (_applyHandler: ApplyHandler option)
    : unit =
    ()

let unregister () : unit = ()

#endif
