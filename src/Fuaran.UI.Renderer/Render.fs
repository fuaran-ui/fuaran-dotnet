module Fuaran.UI.Renderer.Render

// FS0067 ("this type test or downcast will always hold") fires under the Fable
// pipeline on the DOM downcasts this renderer needs for the .NET pipeline (e.g.
// `descendants[i] :?> Browser.Types.Element` in the Custom exposed-NodeIds
// walk) — Fable's DOM typings already give the narrowed type, so the cast is
// redundant *under Fable* but required *under .NET*. Silence the redundant-cast
// warning so the dual-pipeline code compiles cleanly on both (the .NET build
// keeps the cast; Fable elides it). Surfaced once the renderer's Fable graph
// stopped pulling the non-Fable-portable Fuaran.UI.Ops (Ops.Abstractions split).
#nowarn "67"

// ============================================================================
//  Fuaran — renderer (§4c lines 504–542, §4f introspection contract)
//
//  Per-`NodeKind` dispatch to Feliz `ReactElement`.  Code compiles in both
//  the .NET pipeline (`dotnet build`) and the Fable pipeline (`dotnet fable`)
//  — Feliz' nuget surface looks identical from both sides; Fable rewrites
//  the calls to `React.createElement` on transpile.
//
//  Session 3a covered the seven seed components +
//  Heading/Badge/Spacer/Skeleton + Stack/Card layouts + Button input + a
//  simple-HTML-table fallback Grid renderer.
//
//  Session 3b adds:
//    - Real renderers for GridLayout / SplitPanel / Tabs / Stepper /
//      Sparkline / Form / Filters / FileUpload / Chart / Table / Map /
//      Select / Custom (no more "session 3b" placeholder divs).
//    - The `Action.Call` / `Notify` / `Navigate` / `SetState` / `AiTool`
//      runtime substrate via `IFuaranRuntime` — callers supply one at mount
//      time; the renderer dispatches through it instead of logging
//      `eprintfn` warnings (which remain only when no runtime is wired).
//    - Real markdown rendering via the npm `marked` library + i18n
//      resolution via `BindingSources.I18n`.
//
//  The renderer trusts the per-Kind type-tag invariant from session 2's
//  obj-erasure boundary.
// ============================================================================

open Feliz
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Telemetry.Abstractions

// ─── Correlation IDs for renderer-emitted error payloads ───────────────────
//
// `Render.fs` emits `ErrorPayload` records when a binding resolution fails
// inside a data-bound component (Metric / Progress / LabelValueRow / Grid /
// Chart / Map). Each emission gets a short correlation id so devtools / log
// filters can disambiguate failures, and the per-node render guard threads the
// same id into the DOM (`data-fuaran-render-correlation`) so a DOM-side failure
// marker correlates with its telemetry record.
//
// The id is **deterministic** (Phase 138): derived from the failing node's id
// via `Ids.deterministicCorrelationId` so the same tree renders byte-identical
// output — cache-stable + SSR/hydration-parity-safe. The seed is the node id
// (plus a slot discriminator where one node hosts more than one failure
// surface), so different nodes still get distinct ids while re-renders of the
// same tree stay stable. Hosts that genuinely need per-instance randomness use
// `Ids.randomCorrelationId`.

let private correlationId (seed: string) : string = Ids.deterministicCorrelationId seed

// ─── Render-failure helpers ────────────────────────────────────────────────
//
// `nodeKindName` projects a `NodeKind<'Msg>` to its discriminator name +
// inner-kind detail so the render-failure telemetry record carries
// "Display.Metric" / "Visualisation.Grid" rather than the bare top-level
// case. The orchestrator's drift detector aggregates on this string; a
// repeated "Display.Metric failure" pattern is far more actionable than a
// repeated "Display" pattern. ErrorBoundary doesn't fail itself (the
// boundary IS the catch); kept here for completeness.

// Public surface so the .NET-side test runner can pin the discriminator
// projection without driving Feliz's React-substrate. The renderer's
// internal call sites still consume it through the module namespace.
let nodeKindName<'Msg> (kind: NodeKind<'Msg>) : string =
    match kind with
    | NodeKind.Layout layout ->
        let inner =
            match layout with
            | LayoutKind.Box spec ->
                match spec.Role, spec.Layout with
                | BoxRole.Card, _ -> "Card"
                | (BoxRole.Dashboard, _)
                | (BoxRole.Group, BoxLayout.Auto) -> "Dashboard"
                | BoxRole.Separator, _ -> "Separator"
                | BoxRole.Group, BoxLayout.Grid _ -> "Grid"
                | BoxRole.Group, BoxLayout.Flex _ -> "Stack"
            | LayoutKind.SplitPanel _ -> "SplitPanel"
            | LayoutKind.Tabs _ -> "Tabs"
            | LayoutKind.Stepper _ -> "Stepper"
            | LayoutKind.SummaryList _ -> "SummaryList"
            | LayoutKind.Disclosure _ -> "Disclosure"
            | LayoutKind.Modal _ -> "Modal"
            | LayoutKind.ScrollArea _ -> "ScrollArea"

        "Layout." + inner
    | NodeKind.Display display ->
        let inner =
            match display with
            | DisplayKind.Heading _ -> "Heading"
            | DisplayKind.Markdown _ -> "Markdown"
            | DisplayKind.Metric _ -> "Metric"
            | DisplayKind.Badge _ -> "Badge"
            | DisplayKind.Sparkline _ -> "Sparkline"
            | DisplayKind.Callout _ -> "Callout"
            | DisplayKind.Progress _ -> "Progress"
            | DisplayKind.Skeleton _ -> "Skeleton"
            | DisplayKind.LabelValueRow _ -> "LabelValueRow"
            | DisplayKind.Fact _ -> "Fact"
            | DisplayKind.Link _ -> "Link"
            | DisplayKind.Image _ -> "Image"
            | DisplayKind.List _ -> "List"
            | DisplayKind.Toast _ -> "Toast"
            | DisplayKind.CodeBlock _ -> "CodeBlock"
            | DisplayKind.Math _ -> "Math"
            | DisplayKind.Drawing _ -> "Drawing"

        "Display." + inner
    | NodeKind.Input input ->
        let inner =
            match input with
            | InputKind.Form _ -> "Form"
            | InputKind.Filters _ -> "Filters"
            | InputKind.Button _ -> "Button"
            | InputKind.FileUpload _ -> "FileUpload"
            | InputKind.Select _ -> "Select"

        "Input." + inner
    | NodeKind.Visualisation vis ->
        let inner =
            match vis with
            | VisKind.DataGrid _ -> "Grid"
            | VisKind.Chart _ -> "Chart"
            | VisKind.Map _ -> "Map"

        "Visualisation." + inner
    | NodeKind.Custom(moduleId, componentId, _, _, _) -> sprintf "Custom.%s.%s" moduleId componentId
    | NodeKind.ErrorBoundary _ -> "ErrorBoundary"
    | NodeKind.Switch _ -> "Switch"
    | NodeKind.FragmentDecl _ -> "FragmentDecl"
    | NodeKind.FragmentRef _ -> "FragmentRef"
    | NodeKind.Mount spec -> sprintf "Mount.%s" spec.ScopeId

/// Emit one render-failure telemetry event through the optional sink.
/// Sink failures are swallowed (telemetry is best-effort by the
/// `IFuaranTelemetrySink` contract); a throwing sink does not poison
/// the render path. Pure helper so the call-sites stay readable.
/// Public so the .NET-side test runner can assert against the sink
/// without driving Feliz's React-substrate.
let emitRenderFailure
    (sink: IFuaranTelemetrySink option)
    (nodeId: string)
    (kindName: string)
    (errorMessage: string)
    (source: RenderFailureSource)
    : string =
    let corrId = correlationId (nodeId + "|" + kindName)

    match sink with
    | Some s ->
        try
            s.RecordRenderFailure
                { NodeId = nodeId
                  NodeKindName = kindName
                  ErrorMessage = errorMessage
                  CaughtBy = source
                  CorrelationId = corrId
                  PromptId = None
                  UserId = None
                  Timestamp = System.DateTimeOffset.UtcNow }
        with _ ->
            ()
    | None -> ()

    corrId

/// Default fallback placeholder rendered in place of a single throwing
/// node by the per-node render guard. Carries `data-fuaran-render-failed`
/// + `data-fuaran-render-correlation` so consumer-side selectors / log
/// filters can correlate the DOM-side failure marker with the
/// telemetry-side `CorrelationId`. The text body is intentionally
/// terse — hosts that want a custom failure UI wrap their nodes in
/// `Fuaran.errorBoundary` and author their own `Fallback` shape.
let private renderNodeFallback
    (nodeId: string)
    (kindName: string)
    (errorMessage: string)
    (corrId: string)
    : ReactElement =
    Html.div
        [ prop.className "fuaran-node-fallback"
          prop.custom ("data-fuaran-render-failed", "true")
          prop.custom ("data-fuaran-render-correlation", corrId)
          prop.text (sprintf "[fuaran: render failed for '%s' (%s) — %s]" nodeId kindName errorMessage) ]

// ─── Render context — bundles renderer-wide dependencies ───────────────────
//
// Sources + runtime + dispatch travel together through every recursive
// renderer call. A record beats threading three parameters because the
// per-Kind renderers' signatures stay readable AND because future renderer
// additions (eval probe hooks, theme overrides, etc.) can extend the record
// without rippling through every match arm.
//
// `'Msg` flows through `dispatch`; `Sources` + `Runtime` are 'Msg-agnostic.
// Keeping `Sources` separate from `Runtime` preserves the "data sources vs
// effect substrate" boundary (read-only state lookup vs side-effect ports).

type RenderContext<'Msg> =
    {
        Sources: BindingResolver.BindingSources
        Runtime: Runtime.IFuaranRuntime
        VisAdapter: VisAdapter.IVisualisationAdapter<'Msg>
        Dispatch: 'Msg -> unit
        /// Optional render-failure telemetry sink. When
        /// `Some sink`, the renderer emits a `RenderFailureTelemetry` event on
        /// every per-node-guard catch and on every `ErrorBoundary` catch.
        /// `None` (the default for the `renderWithSources` convenience entry
        /// point) silently swallows the failure into the fallback placeholder —
        /// useful for dev / test contexts where no observability backend is
        /// wired. See `renderWithSourcesAndSink` for the entry point that
        /// pre-supplies a real sink.
        TelemetrySink: IFuaranTelemetrySink option
        /// True when this render is happening underneath
        /// an active `NodeKind.ErrorBoundary`'s child subtree. The per-node
        /// render guard *suspends* itself in this state — throws propagate up
        /// to the boundary, which catches them and renders its `Fallback`
        /// subtree. The boundary explicitly resets this flag to `false` for
        /// the fallback's render so the fallback itself is per-node-guarded
        /// (so a flaky fallback doesn't blank the whole boundary). Nested
        /// boundaries set this flag back to true on entry.
        InErrorBoundary: bool
        /// The fragment registry collected from one
        /// pre-render walk of the input tree by `collectFragments`. Maps
        /// `FragmentId` → the decl's `Body`. Empty (`Map.empty`) for trees
        /// that don't declare fragments — zero-cost for the common case.
        /// First-decl-wins on name collision (`collectFragments` uses
        /// `Map.add` which overwrites, so the deepest / last-seen decl
        /// would win at runtime — the validator's FUARAN056 catches the
        /// collision at build time so this corner case is loud, not silent).
        Fragments: Map<FragmentId, Node<'Msg>>
        /// Cycle-guard for fragment expansion. As
        /// each `FragmentRef` expands, its target id is added; nested
        /// expansion that revisits an in-progress id renders a labelled
        /// placeholder rather than recurring forever. FUARAN058 catches
        /// most cycles at build time; this runtime guard is the
        /// belt-and-braces floor for trees the validator hasn't seen.
        ExpandingFragments: Set<FragmentId>
        /// The runtime scope this subtree renders under (Phase 266, §4o). `None`
        /// (the default) = the process-global default `StateStore` — byte-identical
        /// to pre-266 behaviour. `Some scopeId` routes `Binding.State` reads and
        /// `Action.SetState` writes to the isolated `StateStore.forScope scopeId`
        /// instance (Phase 128), so a `Mount` guest's state stays isolated from the
        /// host's. Set at a `Mount` boundary (per its guest scope id) or at a
        /// scope-aware render entry (`?scope`).
        Scope: string option
    }

// ─── Text-source rendering — handles i18n + bound text ─────────────────────

// ─── Accessibility-attribute emission ──────────────────────────────────────
//
// The `(key, value)` HTML-attribute projection of a `Node`'s `Accessibility`
// trait moved to the emission-agnostic `Fuaran.UI.Renderer.Accessibility`
// module in `Fuaran.UI.Renderer.Core` (Phase 138) so the server renderer can
// emit the same pairs without Feliz/Fable. Re-exported here as
// `Render.accessibilityAttributes` so existing call sites + `AccessibilityTests`
// are unchanged; the client renderer maps the pairs to Feliz `prop.custom`
// props at the `render` call site below.

/// Re-export of [`Accessibility.accessibilityAttributes`]. Projects an
/// `Accessibility option` (resolved against the supplied `BindingSources`)
/// into the `(attr-name, attr-value)` DOM-attribute pairs.
let accessibilityAttributes
    (sources: BindingResolver.BindingSources)
    (a11y: Accessibility option)
    : (string * string) list =
    Accessibility.accessibilityAttributes sources a11y

// ─── Text-source rendering — handles i18n + bound text ─────────────────────

let private renderText (ctx: RenderContext<'Msg>) (text: TextSource) : string =
    match text with
    | TextSource.Literal s -> s
    | TextSource.Bound binding ->
        // Phase 632 — text slots resolve through the scalar path, so a
        // `Binding.Transform` yields its 1×1 result cell (never the rows list).
        BindingResolver.tryResolveScalarText ctx.Sources binding
        |> Option.defaultValue ""
    | TextSource.I18n(key, args) ->
        match Map.tryFind key ctx.Sources.I18n with
        | Some template ->
            // Substitute `{argName}` placeholders with values from args.
            // Scalar `JVal` args render as their display string; a composite
            // arg falls back to compact JSON — full ICU-shape interpolation
            // is a session-4+ ergonomic upgrade.
            args
            |> Map.fold
                (fun (acc: string) (k: string) (v: JVal) ->
                    let needle = "{" + k + "}"

                    let replacement =
                        match v with
                        | JStr s -> s
                        | JInt i -> string i
                        | JFloat f -> string f
                        | JBool b -> (if b then "true" else "false")
                        | composite -> Json.render composite

                    acc.Replace(needle, replacement))
                template
        | None ->
            // Missing-translation case — keep loud, same shape session 3a
            // emitted so devs catch unregistered keys at sight.
            sprintf "[i18n:%s]" key

// ─── Options resolution + the `<opaque>` non-array source contract ─────────
//
// Phase 131: a non-array `Static` options source the encoder could not
// serialise round-trips to the `"<opaque>"` sentinel (WIRE_FORMAT.md §5). The
// JsonDecode host rebuilds it as a single-element placeholder
// `[ { Value = "<opaque>"; Label = Literal "<opaque>" } ]` — a NON-NULL ref so
// re-encode stays byte-stable (`box []` is `null`, which would emit JSON
// `null`, not `"<opaque>"`). That placeholder must NOT reach the DOM: it is an
// erased source, not a real option. The canonical cross-host contract is
// **render no concrete options** for an opaque/non-array options source, which
// is exactly what the TS `@fuaran-ui/renderer` `asArray` coercion produces
// (non-array → `[]`). `resolveOptions` is the F# rendering-side counterpart:
// it strips the opaque placeholder so every options-bearing control
// (Select / Choice / SegmentedChoice / ChoiceFilter / SegmentedFilter) emits
// identical DOM on both hosts.
let private opaqueOptionsSentinel = "<opaque>"

let private isOpaqueOptionPlaceholder (option: SelectOption) : bool =
    match option.Label with
    | TextSource.Literal label -> option.Value = opaqueOptionsSentinel && label = opaqueOptionsSentinel
    | _ -> false

let private resolveOptions (ctx: RenderContext<'Msg>) (binding: Binding<SelectOption list>) : SelectOption list =
    BindingResolver.tryResolve ctx.Sources binding
    |> Option.defaultValue []
    |> List.filter (isOpaqueOptionPlaceholder >> not)

// ─── The uniform icon hook ─────────────────────────────────────────────────
//
// Every icon-bearing spec (TabHeader / Fact / Metric / Callout / Button)
// renders its `IconSource` as ONE empty placement element:
//
//   <span class="fuaran-icon fuaran-{kind}-icon" data-icon="{name}" aria-hidden="true"></span>
//
// The icon NAME rides the `data-icon` attribute, never the text content — the
// reference CSS ships no glyphs, so a host with no icon system sees nothing
// (not the raw name), and a host maps `data-icon` to glyphs via its own
// mechanism (CSS `::before` content, font classes, or hydration-time SVG
// injection). `aria-hidden` because every icon-bearing spec pairs the icon
// with a visible text label. Both renderers and the TS tier emit this same
// shape — the SSR parity corpus pins it.

let private iconHook (kindClass: string) (name: string) : ReactElement =
    Html.span
        [ prop.className ("fuaran-icon " + kindClass)
          prop.custom ("data-icon", name)
          prop.custom ("aria-hidden", "true") ]

// ─── Detect Actions the renderer's runtime cannot execute ──────────────────
//
// When the caller supplies the `DiagnosticRuntime` (the .NET fallback /
// pre-wiring placeholder), the five non-Dispatch/Chain Action kinds will
// emit a warning rather than do anything useful. The renderer marks any
// button bound to such an Action with `fuaran-button-unwired` + a tooltip
// so the dev sees the gap visually rather than chasing a silent no-op.
//
// "Unwired" is determined purely by the Action's shape — if any branch
// reaches Call/Notify/Navigate/SetState/AiTool, mark it unwired. The
// runtime itself decides whether to no-op or actually execute; this is
// just a UX hint.

let rec private containsUnwiredAction (action: Action<'Msg>) : bool =
    match action with
    | Action.Dispatch _ -> false
    | Action.Chain actions -> actions |> List.exists containsUnwiredAction
    | Action.Call _
    | Action.Notify _
    | Action.Navigate _
    | Action.SetState _
    | Action.AiTool _
    // Invoke (Phase 283) dispatches a host-registered capability — "unwired" until a host provides it.
    | Action.Invoke _ -> true
    // CommitLocal dispatches a DOM custom event consumed by the
    // Local-bound input's `useEffect` listener — no runtime substrate
    // required. Renderer-native, not "unwired".
    | Action.CommitLocal _ -> false
    // WriteToClipboard routes through `IFuaranRuntime.WriteToClipboard`.
    // The browser runtime wires `navigator.clipboard.writeText`; the diagnostic
    // runtime warns. Treat as "wired" — the unwired-button tooltip is meant
    // for slots that lack a substrate at all (HTTP, router, AI tools), not
    // for slots whose substrate's failure mode is intrinsic to the host
    // (e.g. the browser's clipboard-permission UX). Hosts that want a
    // visual hint when no clipboard substrate is wired should override
    // `IFuaranRuntime.Warn`.
    | Action.WriteToClipboard _ -> false
    // ReadFileBody routes through `IFuaranRuntime.ReadFileBody` (browser
    // host wires `FileReader`; diagnostic runtime warns). Like clipboard,
    // the substrate's failure mode is intrinsic to the host (no File blob /
    // FileReader error), not the "no substrate wired" shape the unwired-
    // button tooltip flags — treat as wired.
    | Action.ReadFileBody _ -> false

// ─── Action interpretation ─────────────────────────────────────────────────
//
// `Dispatch` + `Chain` are renderer-native. The other five route through
// `IFuaranRuntime`. `Call`'s `onResult: obj -> 'Msg` closure is pre-wrapped
// with `dispatch` so the runtime stays 'Msg-generic.

/// Consult a runtime's dispatch policy gate (Phase 119) before invoking a
/// host effect. On allow, run `effect`; on deny, emit a diagnostic (FGP 4 —
/// `Warn` routes under both the Fable and .NET pipelines) and skip the effect.
/// Default runtimes allow everything, so an ungated host behaves exactly as
/// before. Public so tests can pin the allow / deny behaviour without a
/// browser render (`runAction` is private + render is Fable-only).
let applyDispatchGate
    (runtime: Runtime.IFuaranRuntime)
    (descriptor: Runtime.ActionDescriptor)
    (effect: unit -> unit)
    : unit =
    if runtime.CanDispatch descriptor then
        effect ()
    else
        runtime.Warn(
            sprintf "[Fuaran] dispatch denied by policy gate: %s" (Runtime.ActionDescriptor.describe descriptor)
        )

let rec private runAction (ctx: RenderContext<'Msg>) (action: Action<'Msg>) : unit =
    match action with
    | Action.Dispatch msg -> ctx.Dispatch msg
    | Action.Chain actions ->
        for a in actions do
            runAction ctx a
    | Action.Call(endpoint, onResult, into) ->
        let (ApiEndpoint ep) = endpoint

        // Phase 428: a `Some` closure wins (exactly the pre-428 behaviour); the
        // declarative `into` target writes the response to its store slot and
        // the reactive subscriptions re-render readers. Both `None` is a
        // fire-and-forget command call (FUARAN073 warns at validate time). A
        // failed / undecodable call never reaches the callback — the host's
        // `Call` implementation surfaces it (the default BrowserRuntime warns)
        // and the target slot stays unwritten, so readers keep their
        // `OnLoading` surface rather than showing a silent wrong value.
        applyDispatchGate ctx.Runtime (Runtime.ActionDescriptor.Call ep) (fun () ->
            match onResult, into with
            | Some f, _ -> ctx.Runtime.Call(endpoint, (fun raw -> ctx.Dispatch(f raw)))
            | None, Some target ->
                ctx.Runtime.Call(
                    endpoint,
                    fun raw ->
                        match target with
                        | CallResultTarget.IntoState key ->
                            // Scope-aware routing mirrors `Action.SetState` (Phase 266).
                            match ctx.Scope with
                            | Some scopeId -> (StateStore.forScope scopeId).Set(key, raw)
                            | None -> StateStore.set key raw
                        | CallResultTarget.IntoQuery name -> QueryStore.set name raw
                )
            | None, None -> ctx.Runtime.Call(endpoint, ignore))
    | Action.Notify(channel, payload) -> ctx.Runtime.Notify(channel, payload)
    | Action.Navigate route ->
        applyDispatchGate ctx.Runtime (Runtime.ActionDescriptor.Navigate route) (fun () -> ctx.Runtime.Navigate(route))
    | Action.SetState(key, value) ->
        // Scope-aware routing (Phase 266): a guest rendered under `Some scopeId`
        // writes to its own isolated `StateStore.forScope` instance so its state
        // never touches the host's default store (mirrors BrowserRuntime.SetState
        // — the `JVal` payload lowers to a plain JS value via the bridge). The
        // default (`None`) delegates to the host runtime exactly as before.
        match ctx.Scope with
        | Some scopeId ->
            let raw = Runtime.JsonBridge.jvalToJs value
            (StateStore.forScope scopeId).Set(key, raw)
        | None -> ctx.Runtime.SetState(key, value)
    | Action.AiTool(toolName, args) ->
        applyDispatchGate ctx.Runtime (Runtime.ActionDescriptor.AiTool toolName) (fun () ->
            ctx.Runtime.InvokeAiTool(toolName, args))
    | Action.CommitLocal nodeId ->
        // Dispatch a DOM CustomEvent on window keyed on the
        // node id; the corresponding LocalBinding's useEffect listener
        // (mounted in LocalBindings.fs) drains its buffer through the
        // typed `OnCommit`. The event name is namespaced under
        // `fuaran-commit-local-` so cross-app collisions stay
        // structurally unlikely.
        let eventName = sprintf "fuaran-commit-local-%s" nodeId
        let evt = Browser.Dom.window.document.createEvent "CustomEvent"
        evt.initEvent (eventName, false, true)
        Browser.Dom.window.dispatchEvent (evt) |> ignore
    | Action.WriteToClipboard text -> ctx.Runtime.WriteToClipboard(text)
    | Action.ReadFileBody(file, encoding, onRead) ->
        // Default-deny by shape (FGP 3): consult the policy gate before the
        // host reads the file. On allow, the runtime reads the blob (async at
        // the host level) and we dispatch `onRead body` from the callback —
        // mirroring how `Call`'s `onResult` is pre-wrapped with `dispatch`.
        applyDispatchGate ctx.Runtime (Runtime.ActionDescriptor.ReadFileBody file.Id) (fun () ->
            ctx.Runtime.ReadFileBody(file, encoding, (fun body -> ctx.Dispatch(onRead body))))
    | Action.Invoke(capabilityId, _args) ->
        // Phase 283 — invoke a host-registered capability as an effect. Default-deny by shape
        // (FGP 3): consult the policy gate before dispatch (reusing the AiTool descriptor — the
        // closest gate surface for a named host invocation). v1 surfaces the invocation via the
        // runtime diagnostic; a host wires real capability dispatch + Phase-27 replay through the
        // AiTools registry.
        applyDispatchGate ctx.Runtime (Runtime.ActionDescriptor.AiTool capabilityId) (fun () ->
            ctx.Runtime.Warn(sprintf "[Fuaran] capability invoke (no host dispatch wired): %s" capabilityId))

// ─── Control write-back default (Phase 426) ─────────────────────────────────
//
// When a covered control's event handler is omitted (`None` — the declarative /
// AI-authored shape, and the shape every decoded handler-free control takes),
// the renderer writes the changed value back to the control's own value
// binding — but ONLY when that binding is directly a writable store binding:
// `Binding.State(key, _)` ⇒ the (scope-aware) `StateStore`; `Binding.Filter
// name` ⇒ the `FilterStore` (Phase 423). Any other shape (`Static` / `Query` /
// wrapped `Local` / `Format` / …) means no write — the FUARAN069 inert-control
// check warns at validate time. The existing reactive walks
// (`collectStateKeys` / `collectFilterKeys`) then re-render every reader of
// the written slot, closing the declarative loop with zero host code. A
// present closure dispatches exactly as before and never touches a store.
// `Some value` writes the slot; `None` (a cleared choice) clears it, so
// readers fall back to their binding default.

let private writeBackTo (ctx: RenderContext<'Msg>) (binding: Binding<'T>) (value: obj option) : unit =
    match binding with
    | Binding.State(key, _) ->
        // Scope-aware routing mirrors `Action.SetState` (Phase 266): a guest
        // rendered under `Some scopeId` writes to its own isolated store.
        match ctx.Scope, value with
        | Some scopeId, Some v -> (StateStore.forScope scopeId).Set(key, v)
        | Some scopeId, None -> (StateStore.forScope scopeId).Remove key
        | None, Some v -> StateStore.set key v
        | None, None -> StateStore.remove key
    | Binding.Filter(name, _) ->
        match value with
        | Some v -> FilterStore.set name v
        | None -> FilterStore.clear name
    | _ -> ()

/// Phase 663 — replace one field of a grid row for the editable-grid State
/// write-back. Rows on the decoded path are `Map<string, obj>` (the
/// `Transform` / decoded-`State` row shape — same contract as
/// `BindingResolver.projectRowFieldValue`, including its Fable type-erasure
/// caveat); a non-Map host row passes through unchanged on .NET (no sound
/// write exists for an opaque row type).
let private updateRowField (row: obj) (field: string) (newValue: obj) : obj =
#if FABLE_COMPILER
    box (Map.add field newValue (unbox<Map<string, obj>> row))
#else
    match row with
    | :? Map<string, obj> as m -> box (Map.add field newValue m)
    | _ -> row
#endif

// ─── Custom bounded-escape verification ────────────────────────────────────
//
// `NodeKind.Custom` is the language's principled escape hatch. It carries
// optional `contentHash` + `exposedNodeIds` so the renderer can verify
// the body's identity hasn't drifted from its registered renderer AND walk
// the rendered DOM for declared interior NodeIds.
//
//   1. Hash check (synchronous, pre-dispatch). Compare the tree's declared
//      `ContentHash` against the registered renderer's hash. `Match` →
//      render normally; `MismatchAdvisory` → warn + render; `MismatchStrict`
//      → warn + route through `OnError`; `NoTreeHash` → silent (trees
//      that haven't opted into hashing); `RegistryNoHash` → warn + render
//      (tree declared a hash but the registry has none).
//
//   2. Exposed-NodeIds check (post-mount, asynchronous via setTimeout).
//      On Fable, scan the rendered Custom-wrapper subtree for
//      `[data-fuaran-node-id]` and verify each declared id appears.
//      Missing ids log through `IFuaranRuntime.Warn`. Non-blocking. On
//      .NET, no-op — FUARAN053 in the validator covers the build-time
//      AST-walk for the same invariant.

[<RequireQualifiedAccess>]
type private CustomHashOutcome =
    | NoTreeHash
    | Match
    | RegistryNoHash
    | MismatchAdvisory
    | MismatchStrict

let private classifyCustomHash (treeHash: ContentHash option) (registryHash: ContentHash option) : CustomHashOutcome =
    match treeHash, registryHash with
    | None, _ -> CustomHashOutcome.NoTreeHash
    | Some _, None -> CustomHashOutcome.RegistryNoHash
    | Some t, Some r ->
        if t.Algorithm = r.Algorithm && t.Hash = r.Hash then
            CustomHashOutcome.Match
        else
            match t.Strictness with
            | HashStrictness.StrictReplay -> CustomHashOutcome.MismatchStrict
            | HashStrictness.AdvisoryWarning -> CustomHashOutcome.MismatchAdvisory
            // `Enforced` is primarily a build-time gate (validator FUARAN062);
            // if a mismatch still reaches the renderer, treat it as strictly as
            // `StrictReplay` — route through `OnError` rather than render drift.
            | HashStrictness.Enforced -> CustomHashOutcome.MismatchStrict

/// Structured warn-channel payload for a hash mismatch. Hosts that route
/// `IFuaranRuntime.Warn` through their observability stack can pattern-
/// match on the `FuaranCustomHashMismatch` discriminator. JSON-shaped
/// single-line string so log-tail tooling stays readable.
let private formatHashMismatchPayload
    (moduleId: string)
    (componentId: string)
    (expected: ContentHash option)
    (actual: ContentHash option)
    : string =
    let renderHash =
        function
        | Some h -> sprintf "%s:%s" h.Algorithm h.Hash
        | None -> "(none)"

    sprintf
        "{ \"kind\": \"FuaranCustomHashMismatch\", \"moduleId\": \"%s\", \"componentId\": \"%s\", \"expected\": \"%s\", \"actual\": \"%s\" }"
        moduleId
        componentId
        (renderHash expected)
        (renderHash actual)

#if FABLE_COMPILER
/// Fable-side post-paint DOM walk. setTimeout(0) defers past React's
/// commit phase; the walk then scans the Custom wrapper's subtree for
/// declared `exposedNodeIds`. Re-schedules on every render — debouncing
/// is the host's concern via the `Warn` channel.
let private scheduleExposedNodeIdsVerification
    (parentNodeId: string)
    (expectedIds: NodeId list)
    (runtime: Runtime.IFuaranRuntime)
    : unit =
    Browser.Dom.window.setTimeout (
        (fun () ->
            try
                let selector = sprintf "[data-fuaran-node-id=\"%s\"]" parentNodeId
                let wrapper = Browser.Dom.document.querySelector selector

                if isNull wrapper then
                    runtime.Warn(
                        sprintf "Exposed-NodeIds verification: Custom wrapper '%s' not found in DOM." parentNodeId
                    )
                else
                    let descendants = wrapper.querySelectorAll "[data-fuaran-node-id]"

                    let presentIds =
                        [ for i in 0 .. descendants.length - 1 do
                              let el = descendants[i] :?> Browser.Types.Element
                              yield el.getAttribute "data-fuaran-node-id" ]
                        |> List.filter (fun s -> s <> parentNodeId && not (isNull s))
                        |> Set.ofList

                    for NodeId expected in expectedIds do
                        if not (Set.contains expected presentIds) then
                            runtime.Warn(
                                sprintf
                                    "Exposed-NodeIds verification: Custom '%s' declared exposed-id '%s' but no matching data-fuaran-node-id was emitted in the rendered DOM."
                                    parentNodeId
                                    expected
                            )
            with ex ->
                runtime.Warn(sprintf "Exposed-NodeIds verification threw for '%s': %s" parentNodeId ex.Message)),
        0
    )
    |> ignore
#else
/// .NET-side no-op — Browser.Dom is meaningless under `dotnet build` test
/// contexts. FUARAN053 covers the build-time AST-walk for the same
/// invariant.
let private scheduleExposedNodeIdsVerification
    (_parentNodeId: string)
    (_expectedIds: NodeId list)
    (_runtime: Runtime.IFuaranRuntime)
    : unit =
    ()
#endif

// ─── Fragment resolver + expansion ─────────────────────────────────────────
//
// `collectFragments` walks the input tree once before the first render and
// builds the `RenderContext.Fragments` registry. The walk visits every
// `NodeKind.FragmentDecl` it can reach via the standard structural arms
// (Layout children, ErrorBoundary.Child/Fallback, FragmentDecl.Body). Refs
// don't carry bodies, so they're not visited by collection.
//
// `namespaceNode` rewrites every interior `NodeId` of a fragment body by
// prepending the ref's `NodeId` (plus ".") to it — that's how multiple
// refs to the same fragment produce DOM-unique addressable ids:
// `ref1.btn` / `ref2.btn` rather than the bare `btn`. The rewrite is
// structural — every `NodeKind` arm that carries Node children is
// recursed into; `NodeKind.Custom` props / accessibility / styles /
// bindings are NOT id-bearing and stay untouched. Nested FragmentRef
// expansions get the prefix concatenated by the recursive render call,
// so `outerRef.innerRef.btn` is what reaches the DOM.

/// One-shot pre-render walk that collects every
/// reachable `NodeKind.FragmentDecl` body into a `Map<FragmentId, _>`.
/// The convenience entry points (`renderWithSources` /
/// `renderWithSourcesAndSink`) call this on the input tree and stash
/// the result on the `RenderContext.Fragments` field. Hand-constructed
/// contexts (catalog axes, test fixtures) call this directly so
/// `FragmentRef` expansion works the same way. Empty for trees that
/// don't declare fragments — zero-cost for the common case.
let rec collectFragments<'Msg> (acc: Map<FragmentId, Node<'Msg>>) (node: Node<'Msg>) : Map<FragmentId, Node<'Msg>> =
    match node.Kind with
    | NodeKind.FragmentDecl spec ->
        // Validator FUARAN056 catches duplicate names at build time, so a
        // collision here is a defect the validator missed (or a tree the
        // validator hasn't seen). Map.add picks last-seen semantically; the
        // alternative (first-wins via `if not Map.containsKey then add`) is
        // structurally identical for valid trees and only diverges for
        // already-broken trees. We pick the last-seen path because it's the
        // shorter code.
        let acc' = Map.add spec.Name spec.Body acc
        collectFragments acc' spec.Body
    | NodeKind.Layout layout ->
        let children =
            match layout with
            | LayoutKind.Box s -> s.Children
            | LayoutKind.SplitPanel s -> s.Children
            | LayoutKind.Tabs s -> s.Children
            | LayoutKind.Stepper s -> s.Children
            | LayoutKind.SummaryList s -> s.Children
            | LayoutKind.Disclosure s -> s.Children
            | LayoutKind.Modal s -> s.Children
            | LayoutKind.ScrollArea s -> s.Children

        children |> List.fold collectFragments acc
    | NodeKind.ErrorBoundary spec ->
        let acc' = collectFragments acc spec.Child
        collectFragments acc' spec.Fallback
    | NodeKind.Switch spec ->
        // Collect fragment decls reachable through the case children + default
        // so a FragmentRef inside any switch branch resolves at render time.
        let acc' =
            spec.Cases |> List.fold (fun a (_, child) -> collectFragments a child) acc

        collectFragments acc' spec.Default
    | NodeKind.Display _
    | NodeKind.Input _
    | NodeKind.Visualisation _
    | NodeKind.Custom _
    | NodeKind.FragmentRef _
    // Mount (§4o) — the guest is a separate scope with its own fragment table
    // resolved by its own loader; host-level fragment collection stops at the
    // boundary (same posture as FragmentRef).
    | NodeKind.Mount _ -> acc

// ─── Binding.State key collection (Phase 106) ──────────────────────────────
//
// Pure render-time pass that collects every `Binding.State` key a tree reads,
// wherever a `Binding<'T>` or a `TextSource` appears. `renderStateReactive`
// feeds the result to `StateStore.subscribeKeys` so a surface re-renders when
// any state key it reads changes — closing the gap where a global
// `Action.SetState` re-rendered only the control that owns the value, leaving
// every sibling reader stale until its own next render.
//
// FORWARD-COUPLING (same discipline as `collectFragments`): a new binding-
// bearing field on any spec — or a new `NodeKind` / `Binding` / `TextSource`
// case — must extend this walk, else its `Binding.State` readers silently
// miss the auto-subscription. The walk only ever reads the key *string*, so
// it stays generic over each binding's `'T` with no type-erasure hazard.

/// The reactive channel a key-collection walk targets (Phase 423; Selection joined in Phase 427).
/// One walk, parameterised by channel, so the `Binding.State` (`StateStore`), `Binding.Filter`
/// (`FilterStore`) and `Binding.Selection` (`SelectionStore`) subscription key-sets share a single
/// forward-coupling point: a new `Binding` / spec / `NodeKind` case extends the walk once and all
/// channels stay correct. Threading a plain value (not a rank-2 generic selector) keeps the generic
/// `keysOfBinding<'T>` inference intact.
type KeyChannel =
    | StateChannel
    | FilterChannel
    | SelectionChannel
    | QueryChannel

/// Reactive keys referenced by a single binding for `channel`, recursing into a `Local` binding's
/// re-sync source, an `I18n` binding's `{arg}` sub-bindings, and a `Format` binding's numeric source
/// (e.g. a salary `State` formatted as currency re-renders when the salary changes). `StateChannel`
/// collects `Binding.State` keys; `FilterChannel` collects `Binding.Filter` names. A `Transform`
/// binding's source + pipeline are static `Fuaran.Core` data (no `State`/`Filter` reader inside).
let rec keysOfBinding<'T> (channel: KeyChannel) (binding: Binding<'T>) : string list =
    match binding with
    | Binding.State(key, _) ->
        match channel with
        | StateChannel -> [ key ]
        | _ -> []
    | Binding.Filter(name, _) ->
        match channel with
        | FilterChannel -> [ name ]
        | _ -> []
    // A `Binding.Selection` reader (Phase 427) subscribes to its producer node's id on the
    // Selection channel, so a row click re-renders every reader of that grid.
    | Binding.Selection(NodeId nodeId, _, _, _) ->
        match channel with
        | SelectionChannel -> [ nodeId ]
        | _ -> []
    | Binding.Local local -> keysOfBinding channel local.InitialFrom
    | Binding.I18n(_, Some args) ->
        args
        |> Map.toList
        |> List.collect (fun (_, ab) -> keysOfBinding<obj> channel ab)
    | Binding.I18n(_, None) -> []
    | Binding.Format(source, _, _) -> keysOfBinding channel source
    // A parameterised `Transform` (Phase 424) reads through to each param's scalar source, so a chip
    // write re-evaluates every pipeline parameterised on it (its filter/state keys are the union of
    // its param sources' keys). A param-free `Transform` still contributes nothing.
    | Binding.Transform(_, _, parameters) -> parameters |> List.collect (fun (_, fromB) -> keysOfBinding channel fromB)
    // A `Query`'s `dependsOn` (Phase 421) names the FILTERS that scope it — a filter-store change
    // re-resolves the query. On the Filter channel it contributes those names (the invalidation
    // subscription); on the Query channel (Phase 428) it contributes its own name, so a
    // `Call … into Query <name>` write re-renders every reader of that slot.
    | Binding.Query(name, _, dependsOn) ->
        match channel with
        | FilterChannel -> dependsOn
        | QueryChannel -> [ name ]
        | StateChannel
        | SelectionChannel -> []
    | Binding.Invoke _
    | Binding.Static _
    | Binding.Computed _ -> []

/// Reactive keys referenced by a `TextSource` for `channel`. `Literal` carries none; `Bound` defers
/// to its binding; `TextSource.I18n` args are `JVal` literals (not bindings) so they carry none.
let keysOfText (channel: KeyChannel) (text: TextSource) : string list =
    match text with
    | TextSource.Bound b -> keysOfBinding channel b
    | TextSource.Literal _
    | TextSource.I18n _ -> []

let private keysOfTextOpt (channel: KeyChannel) (text: TextSource option) : string list =
    match text with
    | Some t -> keysOfText channel t
    | None -> []

let private keysOfBindingOpt (channel: KeyChannel) (binding: Binding<'T> option) : string list =
    match binding with
    | Some b -> keysOfBinding channel b
    | None -> []

let private keysOfFormFieldKind<'Msg> (channel: KeyChannel) (kind: FormFieldKind<'Msg>) : string list =
    match kind with
    | FormFieldKind.Text(v, _) -> keysOfBinding channel v
    | FormFieldKind.Number(v, _) -> keysOfBinding channel v
    | FormFieldKind.Checkbox(v, _) -> keysOfBinding channel v
    | FormFieldKind.TextArea(v, _, _) -> keysOfBinding channel v
    | FormFieldKind.RangedNumber(v, _, _) -> keysOfBinding channel v
    | FormFieldKind.Range(v, _, _) -> keysOfBinding channel v
    | FormFieldKind.Choice(opts, value, _) -> keysOfBinding channel opts @ keysOfBinding channel value
    | FormFieldKind.SegmentedChoice(opts, value, _, _) -> keysOfBinding channel opts @ keysOfBinding channel value
    | FormFieldKind.Date(v, _, _, _) -> keysOfBinding channel v


let rec collectKeys<'Msg> (channel: KeyChannel) (node: Node<'Msg>) : Set<string> =
    let a11yKeys =
        match node.Accessibility with
        | Some a -> keysOfBindingOpt channel a.Label @ keysOfBindingOpt channel a.Hidden
        | None -> []

    let directKeys, children = kindKeys channel node.Kind

    let childKeys =
        children
        |> List.fold (fun acc child -> Set.union acc (collectKeys channel child)) Set.empty

    Set.union (Set.ofList (a11yKeys @ directKeys)) childKeys

/// This node's own (non-descendant) reactive keys for `channel`, paired with its child `Node`s for
/// `collectKeys` to recurse into.
and private kindKeys<'Msg> (channel: KeyChannel) (kind: NodeKind<'Msg>) : string list * Node<'Msg> list =
    match kind with
    | NodeKind.Layout layout -> layoutKeys channel layout
    | NodeKind.Display display -> displayKeys channel display, []
    | NodeKind.Input input -> inputKeys channel input, []
    | NodeKind.Visualisation vis -> visKeys channel vis, []
    | NodeKind.ErrorBoundary spec -> [], [ spec.Child; spec.Fallback ]
    | NodeKind.Switch spec ->
        // Switch reads its StateKey off the state channel, so it registers that
        // key here (Phase 392): `renderStateReactive` subscribes the surface to
        // it, so a global `Action.SetState` re-renders and Switch re-selects the
        // matching case. Off the state channel the key contributes nothing. The
        // case children + default recurse for their own reactive keys.
        let ownKeys =
            match channel with
            | StateChannel -> [ spec.StateKey ]
            | _ -> []

        ownKeys, (spec.Cases |> List.map snd) @ [ spec.Default ]
    | NodeKind.FragmentDecl spec -> [], [ spec.Body ]
    | NodeKind.FragmentRef _ -> [], []
    // Custom props are JVal literals, not bindings — no reactive keys.
    | NodeKind.Custom _ -> [], []
    // Mount (§4o) — the guest owns its own scoped state store; its keys
    // live in the guest scope, not the host binding registry, so the
    // boundary contributes no host keys and no host children to recurse.
    | NodeKind.Mount _ -> [], []

and private layoutKeys<'Msg> (channel: KeyChannel) (layout: LayoutKind<'Msg>) : string list * Node<'Msg> list =
    match layout with
    | LayoutKind.Box s -> keysOfTextOpt channel s.Heading, s.Children
    | LayoutKind.SplitPanel s -> [], s.Children
    | LayoutKind.SummaryList s -> keysOfTextOpt channel s.Heading, s.Children
    | LayoutKind.Stepper s -> keysOfBinding channel s.ActiveStep, s.Children
    | LayoutKind.Disclosure s -> (keysOfText channel s.Heading @ keysOfBinding channel s.Open), s.Children
    | LayoutKind.Tabs s ->
        let headerKeys =
            match s.TabHeaders with
            | Some headers ->
                headers
                |> List.collect (fun h -> keysOfText channel h.Label @ keysOfBindingOpt channel h.Disabled)
            | None -> []

        (keysOfBinding channel s.ActiveIndex
         @ keysOfBindingOpt channel s.ActiveTag
         @ headerKeys),
        s.Children
    | LayoutKind.Modal s -> (keysOfTextOpt channel s.Heading @ keysOfBinding channel s.Open), s.Children
    | LayoutKind.ScrollArea s -> [], s.Children

and private displayKeys<'Msg> (channel: KeyChannel) (display: DisplayKind<'Msg>) : string list =
    match display with
    | DisplayKind.Heading h -> keysOfText channel h.Text
    | DisplayKind.Markdown m -> keysOfText channel m.Text
    | DisplayKind.Metric k ->
        keysOfText channel k.Label
        @ keysOfBinding channel k.Value
        @ keysOfBindingOpt channel k.Trend
        @ keysOfTextOpt channel k.Subtext
    | DisplayKind.Badge b -> keysOfText channel b.Label
    | DisplayKind.Sparkline s -> keysOfBinding channel s.Source
    | DisplayKind.Callout c -> keysOfTextOpt channel c.Heading @ keysOfText channel c.Body
    | DisplayKind.Progress p ->
        keysOfBinding channel p.Fraction
        @ keysOfTextOpt channel p.Label
        @ keysOfTextOpt channel p.Caveat
    | DisplayKind.Skeleton _ -> []
    | DisplayKind.LabelValueRow r ->
        keysOfText channel r.Label
        @ keysOfBinding channel r.Value
        @ keysOfTextOpt channel r.Help
    | DisplayKind.Fact fa ->
        keysOfText channel fa.Label
        @ keysOfText channel fa.Value
        @ keysOfTextOpt channel fa.Help
    | DisplayKind.Link l -> keysOfBinding channel l.Href @ keysOfText channel l.Label
    | DisplayKind.Image i -> keysOfBinding channel i.Src @ keysOfText channel i.Alt
    | DisplayKind.List l -> l.Items |> List.collect (keysOfText channel)
    | DisplayKind.Toast t -> keysOfText channel t.Message @ keysOfBinding channel t.Open
    // CodeBlock + Math carry plain strings, not bindings — no reactive keys.
    | DisplayKind.CodeBlock _ -> []
    | DisplayKind.Math _ -> []
    // Phase 524 ships a placeholder render; the reactive DrawStyle-colour keys
    // are wired with the real SVG renderer in Phase 525.
    | DisplayKind.Drawing _ -> []

and private inputKeys<'Msg> (channel: KeyChannel) (input: InputKind<'Msg>) : string list =
    match input with
    | InputKind.Button b ->
        keysOfText channel b.Label
        @ keysOfTextOpt channel b.Tooltip
        @ keysOfBindingOpt channel b.Disabled
    | InputKind.FileUpload fu -> keysOfText channel fu.Label @ keysOfBindingOpt channel fu.Disabled
    | InputKind.Select s ->
        keysOfText channel s.Label
        @ keysOfBinding channel s.Source
        @ keysOfBinding channel s.Value
        // Phase 291 — the multi-select value binding (Some only in multi mode).
        @ keysOfBindingOpt channel s.Values
        @ keysOfTextOpt channel s.Placeholder
        @ keysOfBindingOpt channel s.Disabled
    | InputKind.Form f ->
        let fieldKeys =
            f.Fields
            |> List.collect (fun field ->
                keysOfText channel field.Label
                @ keysOfTextOpt channel field.Help
                @ keysOfFormFieldKind channel field.Kind)

        keysOfText channel f.SubmitLabel
        @ keysOfBindingOpt channel f.Disabled
        @ fieldKeys
    | InputKind.Filters filters ->
        filters
        |> List.collect (fun fs -> keysOfText channel fs.Label @ keysOfFormFieldKind channel fs.Field)

and private visKeys<'Msg> (channel: KeyChannel) (vis: VisKind<'Msg>) : string list =
    match vis with
    | VisKind.DataGrid g ->
        keysOfBinding channel g.Source
        @ (match g.StaticRows with
           | Some(headers, rows) ->
               (headers |> List.collect (keysOfText channel))
               @ (rows |> List.collect (List.collect (keysOfText channel)))
           | None -> [])
    | VisKind.Chart c -> keysOfBinding channel c.Source @ keysOfTextOpt channel c.Title
    | VisKind.Map m -> keysOfBinding channel m.Source

// ── Public channel aliases ──────────────────────────────────────────────────
//  The State-channel names are the Phase 106 public surface (the .NET test runner + reactive host
//  pin them); the Filter-channel twins (Phase 423) are the new surface. Both delegate to the one walk.

/// State keys referenced by a single binding (the `StateChannel` walk).
let stateKeysOfBinding<'T> (binding: Binding<'T>) : string list = keysOfBinding StateChannel binding

/// State keys referenced by a `TextSource` (the `StateChannel` walk).
let stateKeysOfText (text: TextSource) : string list = keysOfText StateChannel text

/// Collect every `Binding.State` key the tree rooted at `node` reads. Public so the .NET test
/// runner can pin the walk without driving the React substrate.
let collectStateKeys<'Msg> (node: Node<'Msg>) : Set<string> = collectKeys StateChannel node

/// Filter names referenced by a single binding (the `FilterChannel` walk, Phase 423) — the twin of
/// `stateKeysOfBinding`, recursing `Local` / `I18n` args / `Format` identically. A `Binding.Filter
/// name` reader auto-subscribes to that filter via `FilterStore`.
let filterKeysOfBinding<'T> (binding: Binding<'T>) : string list = keysOfBinding FilterChannel binding

/// Filter names referenced by a `TextSource` (the `FilterChannel` walk).
let filterKeysOfText (text: TextSource) : string list = keysOfText FilterChannel text

/// Collect every `Binding.Filter` name the tree rooted at `node` reads (Phase 423) — the twin of
/// `collectStateKeys`, so the reactive host subscribes a surface to its filter keys alongside its
/// state keys.
let collectFilterKeys<'Msg> (node: Node<'Msg>) : Set<string> = collectKeys FilterChannel node

/// Selection node-ids referenced by a single binding (the `SelectionChannel` walk, Phase 427) — the
/// third twin, recursing `Local` / `I18n` args / `Format` / `Transform` params identically. A
/// `Binding.Selection (nodeId, _)` reader auto-subscribes to that producer via `SelectionStore`.
let selectionKeysOfBinding<'T> (binding: Binding<'T>) : string list = keysOfBinding SelectionChannel binding

/// Selection node-ids referenced by a `TextSource` (the `SelectionChannel` walk).
let selectionKeysOfText (text: TextSource) : string list = keysOfText SelectionChannel text

/// Collect every `Binding.Selection` producer node-id the tree rooted at `node` reads (Phase 427) —
/// the third twin, so the reactive host subscribes a surface to its selection keys alongside its
/// state + filter keys.
let collectSelectionKeys<'Msg> (node: Node<'Msg>) : Set<string> = collectKeys SelectionChannel node

/// Query names referenced by a single binding (the `QueryChannel` walk, Phase 428) — a
/// `Binding.Query name` reader auto-subscribes to a declarative `Call … into Query name` write.
let queryKeysOfBinding<'T> (binding: Binding<'T>) : string list = keysOfBinding QueryChannel binding

/// Collect every `Binding.Query` name the tree rooted at `node` reads (Phase 428) — the fourth
/// twin, so the reactive host subscribes a surface to its query slots alongside the other channels.
let collectQueryKeys<'Msg> (node: Node<'Msg>) : Set<string> = collectKeys QueryChannel node

/// Rewrite every interior `NodeId` of a fragment
/// body by prepending the supplied `prefix` (e.g. `"ref1."`). Recurses
/// through every NodeKind arm that carries Node children so nested
/// declarations / refs / layouts get their ids rewritten consistently.
/// `NodeKind.Custom` props + `Accessibility.LabelledBy` /
/// `DescribedBy` references are NOT id-rewritten because the renderer
/// doesn't currently expose a portable way to know which prop / which
/// referenced id corresponds to a fragment-interior id — the
/// conservative behaviour is to leave them alone, surfacing as a
/// build-time validator follow-up if cross-prop id references become a
/// real authoring pattern inside fragment bodies.
let rec private namespaceNode<'Msg> (prefix: string) (node: Node<'Msg>) : Node<'Msg> =
    let (NodeId raw) = node.Id
    let newId = NodeId(prefix + raw)
    let newKind = namespaceKind prefix node.Kind
    { node with Id = newId; Kind = newKind }

and private namespaceKind<'Msg> (prefix: string) (kind: NodeKind<'Msg>) : NodeKind<'Msg> =
    match kind with
    | NodeKind.Layout layout ->
        let recur = namespaceNode prefix

        match layout with
        | LayoutKind.Box s ->
            NodeKind.Layout(
                LayoutKind.Box
                    { s with
                        Children = List.map recur s.Children }
            )
        | LayoutKind.SplitPanel s ->
            NodeKind.Layout(
                LayoutKind.SplitPanel
                    { s with
                        Children = List.map recur s.Children }
            )
        | LayoutKind.Tabs s ->
            NodeKind.Layout(
                LayoutKind.Tabs
                    { s with
                        Children = List.map recur s.Children }
            )
        | LayoutKind.Stepper s ->
            NodeKind.Layout(
                LayoutKind.Stepper
                    { s with
                        Children = List.map recur s.Children }
            )
        | LayoutKind.SummaryList s ->
            NodeKind.Layout(
                LayoutKind.SummaryList
                    { s with
                        Children = List.map recur s.Children }
            )
        | LayoutKind.Disclosure s ->
            NodeKind.Layout(
                LayoutKind.Disclosure
                    { s with
                        Children = List.map recur s.Children }
            )
        | LayoutKind.Modal s ->
            NodeKind.Layout(
                LayoutKind.Modal
                    { s with
                        Children = List.map recur s.Children }
            )
        | LayoutKind.ScrollArea s ->
            NodeKind.Layout(
                LayoutKind.ScrollArea
                    { s with
                        Children = List.map recur s.Children }
            )
    | NodeKind.ErrorBoundary spec ->
        NodeKind.ErrorBoundary
            { Child = namespaceNode prefix spec.Child
              Fallback = namespaceNode prefix spec.Fallback }
    | NodeKind.Switch spec ->
        // Rewrite interior ids in every case child + the default so a Switch
        // inside an expanded fragment gets DOM-unique namespaced ids (Phase 392).
        NodeKind.Switch
            { spec with
                Cases = spec.Cases |> List.map (fun (m, child) -> m, namespaceNode prefix child)
                Default = namespaceNode prefix spec.Default }
    | NodeKind.FragmentDecl spec ->
        // Nested declaration: rewrite the body's ids by the outer prefix.
        // The decl's Name is not prefixed — fragment names live in their
        // own namespace, distinct from NodeIds.
        NodeKind.FragmentDecl
            { spec with
                Body = namespaceNode prefix spec.Body }
    | NodeKind.FragmentRef _
    | NodeKind.Display _
    | NodeKind.Input _
    | NodeKind.Visualisation _
    | NodeKind.Custom _
    // Mount (§4o) — the guest interior is a separate scope namespaced by its
    // own loader; the host fragment prefix does not rewrite guest ids. Leave
    // the mount unchanged (same posture as FragmentRef / Custom).
    | NodeKind.Mount _ -> kind

// ─── Per-Kind body renderers ───────────────────────────────────────────────

// State-slot dispatch is inlined at each data-bound per-Kind renderer
// below (Metric / Progress / Grid). The shape is uniform:
//   - `NotResolved` + `OnLoading` slot wired → render the slot Node.
//   - `Errored msg` + `OnError` slot wired → render the slot factory
//     against an `ErrorKind.BindingResolution`-shaped payload (the §4b
//     `ErrorKind` case dedicated to "renderer could not resolve a
//     binding"); CorrelationId is generated per-emission via `correlationId`.
//   - Otherwise fall through to the kind's normal body.
// Grid additionally substitutes `OnEmpty` when its source resolves to
// an empty seq. Components without a primary binding (Heading,
// Markdown, Stack, etc.) skip the dispatch entirely.

/// Late-bound guest renderer (Phase 266, §4o). Set once at module init (right
/// after the recursive `render` group below) to `Some render`. The `Mount` arm
/// renders an obj-typed guest tree THROUGH this function value rather than by a
/// direct recursive call to `render` at type `obj` — F# forbids polymorphic
/// recursion, so a same-group `render` call at `obj` would pin the whole
/// `'Msg`-generic group to `obj`. Module-level `mutable` is justified: it is the
/// standard F# device for breaking a recursive definition across a type boundary,
/// written exactly once during module initialisation and read-only thereafter
/// (single-threaded init order guarantees it is `Some` before any render runs).
let mutable private renderGuestHook: (RenderContext<obj> -> Node<obj> -> ReactElement) option =
    None

let rec private renderKind
    (ctx: RenderContext<'Msg>)
    (parentNodeId: string)
    (state: StateBehaviour<'Msg>)
    (kind: NodeKind<'Msg>)
    : ReactElement =
    match kind with
    | NodeKind.Layout layout -> renderLayout ctx parentNodeId layout
    | NodeKind.Display display -> renderDisplay ctx parentNodeId state display
    | NodeKind.Input input -> renderInput ctx input
    | NodeKind.Visualisation vis -> renderVis ctx parentNodeId state vis
    | NodeKind.ErrorBoundary spec ->
        // Render-time error boundary. The author wraps a subtree
        // they expect *may* fail under some inputs; this case catches throws
        // inside the child and substitutes the typed Fallback Node.
        //
        // Per-node-guard interplay: the boundary sets
        // `InErrorBoundary = true` for the child's render so the per-node
        // guard *suspends* — throws propagate up to this catch instead of
        // being absorbed by a fallback placeholder mid-subtree. That's
        // the whole point of authoring a boundary: opt into "I want the
        // FALLBACK subtree, not a placeholder where the bad leaf was". The
        // fallback then renders with `InErrorBoundary = false` so its own
        // children re-enable the per-node guard (a flaky fallback degrades
        // gracefully too, but not by escalating up to a grandparent
        // boundary — the boundary already absorbed responsibility).
        //
        // Nested boundaries: when the child subtree itself contains another
        // `ErrorBoundary`, the inner boundary's `InErrorBoundary = true`
        // override scopes locally — the inner catches its own subtree's
        // throws and the outer never sees them.
        let boundaryCtx = { ctx with InErrorBoundary = true }

        try
            render boundaryCtx spec.Child
        with ex ->
            let corrId =
                emitRenderFailure
                    ctx.TelemetrySink
                    parentNodeId
                    (nodeKindName kind)
                    ex.Message
                    RenderFailureSource.ErrorBoundary

            let fallbackCtx = { ctx with InErrorBoundary = false }

            try
                render fallbackCtx spec.Fallback
            with ex2 ->
                // Fallback itself threw — emit a secondary telemetry event
                // so the operator sees BOTH failures and falls back to a
                // bare-bones placeholder so something paints. The
                // correlation id from the primary failure threads through
                // to the placeholder so log filters can join the two.
                emitRenderFailure
                    ctx.TelemetrySink
                    parentNodeId
                    (nodeKindName kind + ".Fallback")
                    ex2.Message
                    RenderFailureSource.ErrorBoundary
                |> ignore

                renderNodeFallback
                    parentNodeId
                    (nodeKindName kind + ".Fallback")
                    (sprintf "child failed (%s); fallback also failed (%s)" ex.Message ex2.Message)
                    corrId
    | NodeKind.Switch spec ->
        // State-bound conditional child (Phase 392). Read the reactive state
        // value at `spec.StateKey` (scope-aware, mirroring `writeBackTo` /
        // `Action.SetState` routing), match its string form against each case in
        // order (first-match-wins), and render that case's child — else the
        // `Default`. The surface is subscribed to `spec.StateKey` via
        // `kindKeys`/`collectKeys`, so a global `Action.SetState` re-renders here
        // and re-selects the matching case with no bespoke dispatch path (FGP 3).
        let currentValue =
            match ctx.Scope with
            | Some scopeId -> (StateStore.forScope scopeId).Get spec.StateKey
            | None -> StateStore.get spec.StateKey

        let matched =
            match currentValue with
            | Some v ->
                let valueStr = if isNull v then "" else string v

                spec.Cases
                |> List.tryPick (fun (m, child) -> if m = valueStr then Some child else None)
            | None -> None

        match matched with
        | Some child -> render ctx child
        | None -> render ctx spec.Default
    | NodeKind.Custom(moduleId, componentId, props, contentHash, exposedNodeIds) ->
        // Dispatch through the runtime's Custom-renderer surface.
        // Bounded-escape verification:
        //   1. Hash check (pre-dispatch) via TryGetCustomRenderer.
        //   2. Exposed-NodeIds DOM walk (post-mount) when the tree declares
        //      any. .NET-side: no-op (FUARAN053 covers build-time).
        // Hosts implementing TryRenderCustom directly continue
        // working — their TryGetCustomRenderer returns None and the renderer
        // falls through to TryRenderCustom for the actual dispatch.

        let renderPlaceholder () : ReactElement =
            let propKeys = props |> Map.toList |> List.map fst |> String.concat ", "

            Html.div
                [ prop.className "fuaran-custom-placeholder"
                  prop.children
                      [ Html.div
                            [ prop.className "fuaran-custom-label"
                              prop.text (sprintf "Custom %s.%s" moduleId componentId) ]
                        Html.div
                            [ prop.className "fuaran-custom-props"
                              prop.text (sprintf "props: %s" propKeys) ] ] ]

        let registryProbe = ctx.Runtime.TryGetCustomRenderer(moduleId, componentId)
        let registryHash = registryProbe |> Option.bind snd
        let outcome = classifyCustomHash contentHash registryHash

        let dispatchToRenderer () : ReactElement =
            match registryProbe with
            | Some(fn, _) -> fn props
            | None ->
                match ctx.Runtime.TryRenderCustom(moduleId, componentId, props) with
                | Some element -> element
                | None -> renderPlaceholder ()

        let renderedElement =
            match outcome with
            | CustomHashOutcome.Match
            | CustomHashOutcome.NoTreeHash -> dispatchToRenderer ()
            | CustomHashOutcome.RegistryNoHash
            | CustomHashOutcome.MismatchAdvisory ->
                ctx.Runtime.Warn(formatHashMismatchPayload moduleId componentId contentHash registryHash)
                dispatchToRenderer ()
            | CustomHashOutcome.MismatchStrict ->
                ctx.Runtime.Warn(formatHashMismatchPayload moduleId componentId contentHash registryHash)
                // OnError-routing: synthesize a BindingResolution-shaped
                // payload (closest existing ErrorKind for a renderer-side
                // failure that didn't cross the wire) and render the
                // consumer-supplied error slot when present. When absent,
                // fall back to the placeholder so the failure stays visible.
                match state.OnError with
                | Some onErr ->
                    let payload =
                        { Kind = ErrorKind.BindingResolution
                          Message =
                            sprintf
                                "Custom hash mismatch for %s.%s — registered renderer's hash differs from the declared ContentHash (StrictReplay)."
                                moduleId
                                componentId
                          CorrelationId = correlationId (parentNodeId + "|custom-hash") }

                    render ctx (onErr payload)
                | None -> renderPlaceholder ()

        // Exposed-NodeIds post-mount DOM walk. Skip the syscall
        // entirely when the tree doesn't declare any (the common case).
        match exposedNodeIds with
        | [] -> ()
        | _ -> scheduleExposedNodeIdsVerification parentNodeId exposedNodeIds ctx.Runtime

        renderedElement
    | NodeKind.FragmentDecl _ ->
        // The declaration site renders nothing
        // visible. The Body is the *template* that FragmentRef sites
        // expand — emitting the body here would defeat the entire
        // emission-economy win (a referenced body would render twice:
        // once at the decl, once at the ref). The outer wrapper still
        // emits with `data-fuaran-node-id` so layout-observer +
        // op-stream-replay machinery keep addressing the decl node.
        Html.none
    | NodeKind.FragmentRef spec ->
        // Expand the referenced fragment with
        // interior NodeIds namespaced under the ref's id so multiple
        // refs to the same fragment produce DOM-unique addressable
        // ids. Unresolved + cyclic references render a labelled
        // placeholder rather than throwing — the renderer guard would
        // catch a throw, but the labelled placeholder is friendlier in
        // dev tools and threads through `Warn` for observability.
        let (FragmentId rawName) = spec.Name

        if Set.contains spec.Name ctx.ExpandingFragments then
            ctx.Runtime.Warn(
                sprintf
                    "[fuaran:fragment] cycle detected expanding ref '%s' → fragment '%s'; rendering placeholder. Validator FUARAN058 catches this at build time."
                    parentNodeId
                    rawName
            )

            Html.div
                [ prop.className "fuaran-fragment-cycle-placeholder"
                  prop.custom ("data-fuaran-fragment-cycle", rawName)
                  prop.text (sprintf "[fuaran:fragment cycle '%s']" rawName) ]
        else
            match Map.tryFind spec.Name ctx.Fragments with
            | None ->
                ctx.Runtime.Warn(
                    sprintf
                        "[fuaran:fragment] ref '%s' points to fragment '%s' which has no declaration in this tree; rendering placeholder. Validator FUARAN057 catches this at build time."
                        parentNodeId
                        rawName
                )

                Html.div
                    [ prop.className "fuaran-fragment-unresolved-placeholder"
                      prop.custom ("data-fuaran-fragment-unresolved", rawName)
                      prop.text (sprintf "[fuaran:fragment unresolved '%s']" rawName) ]
            | Some body ->
                // Prefix interior ids with the ref's NodeId + "." so
                // sibling refs to the same fragment stay DOM-unique.
                // `parentNodeId` is already the ref's fully-prefixed id
                // (the outer expansion, if any, rewrote it before this
                // render call), so nested expansion naturally produces
                // `outerRef.innerRef.btn` without re-concatenating an
                // ambient prefix.
                let prefix = parentNodeId + "."
                let namespaced = namespaceNode prefix body

                let expandedCtx =
                    { ctx with
                        ExpandingFragments = Set.add spec.Name ctx.ExpandingFragments }

                render expandedCtx namespaced
    | NodeKind.Mount spec ->
        // Isolation/embedding boundary (Phase 265/266, §4o). Resolve the guest
        // via the host's loader seam (`IFuaranRuntime.TryLoadGuest`); when a
        // guest tree is returned, render it under its OWN FuaranRuntimeScope
        // (scoped `StateStore`) with dispatch bridged through the mount's
        // `OnBubble` channel — a guest action (obj) becomes a host `Action<'Msg>`
        // run in the host context, so the host `'Msg` stays behind the boundary
        // while the guest space is fully obj-typed and its state is isolated.
        // The `data-fuaran-mount-scope` boundary attribute is preserved so the
        // LayoutObserver + TreeDiff address across the boundary (§4o.6). When no
        // loader is wired (the default / standalone / server case), the declared
        // empty state renders — a Mount in an unwired host is inert, never a throw.
        let boundaryChild =
            match ctx.Runtime.TryLoadGuest spec.ScopeId, renderGuestHook with
            | Some guestTree, Some renderGuest ->
                // Merge the guest scope's StateStore snapshot over the inherited
                // State sources (scoped values win) so the guest reads its own
                // isolated state; the host's default store is untouched.
                let scopedState =
                    (StateStore.forScope spec.ScopeId).Snapshot()
                    |> Map.fold (fun acc k v -> Map.add k v acc) ctx.Sources.State

                let guestCtx: RenderContext<obj> =
                    { Sources = { ctx.Sources with State = scopedState }
                      Runtime = ctx.Runtime
                      VisAdapter = VisAdapter.noOp<obj>
                      Dispatch = (fun (o: obj) -> runAction ctx (spec.OnBubble o))
                      TelemetrySink = ctx.TelemetrySink
                      InErrorBoundary = false
                      Fragments = collectFragments Map.empty guestTree
                      ExpandingFragments = Set.empty
                      Scope = Some spec.ScopeId }

                // Route through the late-bound hook (a function *value*), not a
                // direct call into the recursive `render` group at type obj —
                // F# forbids polymorphic recursion, so the obj-typed guest render
                // crosses the type boundary via the hook set at module init below.
                renderGuest guestCtx guestTree
            | _ ->
                Html.div
                    [ prop.className "fuaran-mount-placeholder"
                      prop.text (sprintf "[fuaran:mount '%s' — guest loader not attached]" spec.ScopeId) ]

        Html.div
            [ prop.className "fuaran-mount-boundary"
              prop.custom ("data-fuaran-mount-scope", spec.ScopeId)
              prop.children [ boundaryChild ] ]

// ─── Layouts ───────────────────────────────────────────────────────────────

and private renderLayout (ctx: RenderContext<'Msg>) (parentNodeId: string) (layout: LayoutKind<'Msg>) : ReactElement =
    match layout with
    // Phase 390 — the unified container. Role + layout mode drive the emitted
    // element + classes so each retired kind's HTML/a11y is byte-identical:
    // Card role → <section class="fuaran-layout-card">; Dashboard role →
    // <div class="fuaran-layout-dashboard">; Group+Grid → grid div; Group+Flex
    // → stack div; Separator role → <hr class="fuaran-layout-separator"> (the
    // retired `Divider`, Phase 459).
    | LayoutKind.Box spec ->
        match spec.Role, spec.Layout with
        | BoxRole.Card, _ ->
            Html.section
                [ prop.className "fuaran-layout-card"
                  prop.children
                      [ match spec.Heading with
                        | Some heading ->
                            Html.header [ prop.className "fuaran-card-heading"; prop.text (renderText ctx heading) ]
                        | None -> Html.none
                        Html.div
                            [ prop.className "fuaran-card-body"
                              prop.children (spec.Children |> List.map (render ctx)) ] ] ]
        | BoxRole.Dashboard, _
        | BoxRole.Group, BoxLayout.Auto ->
            Html.div
                [ prop.className "fuaran-layout-dashboard"
                  prop.children (spec.Children |> List.map (render ctx)) ]
        | BoxRole.Separator, _ -> Html.hr [ prop.className "fuaran-layout-separator" ]
        | BoxRole.Group, BoxLayout.Grid g ->
            // Column count rides on inline style (not a `-cols-N` class suffix)
            // so CSS hosts don't have to pre-declare / Tailwind-safelist every N.
            // The additive `TemplateColumns` field short-circuits the
            // `Cols`-based `repeat(N, 1fr)` emission when `Some` — the verbatim
            // string is emitted so irregular-column grids can be authored
            // without escaping to Feliz. `None` preserves the prior emission
            // shape byte-identical.
            let templateColumns =
                match g.TemplateColumns with
                | Some custom -> custom
                | None -> sprintf "repeat(%d, 1fr)" g.Cols

            // `gap` (Phase 459 — the Spacer replacement) emits only when set, so
            // gap-free grids stay byte-identical to the pre-459 emission.
            let gridStyle =
                [ style.custom ("gridTemplateColumns", templateColumns) ]
                @ (match g.Gap with
                   | Some n -> [ style.custom ("gap", sprintf "%dpx" n) ]
                   | None -> [])

            Html.div
                [ prop.className "fuaran-layout-grid"
                  prop.style gridStyle
                  prop.children (spec.Children |> List.map (render ctx)) ]
        | BoxRole.Group, BoxLayout.Flex f ->
            let dir =
                match f.Direction with
                | Vertical -> "fuaran-stack-vertical"
                | Horizontal -> "fuaran-stack-horizontal"

            let wrap = if f.Wrap then " fuaran-stack-wrap" else ""

            // `gap` emits only when set (Phase 459) — a gap-free stack carries no
            // `style` attribute, byte-identical to the pre-459 emission.
            Html.div (
                [ prop.className (sprintf "fuaran-layout-stack %s%s" dir wrap) ]
                @ (match f.Gap with
                   | Some n -> [ prop.style [ style.custom ("gap", sprintf "%dpx" n) ] ]
                   | None -> [])
                @ [ prop.children (spec.Children |> List.map (render ctx)) ]
            )
    | LayoutKind.SplitPanel spec ->
        // Two-child shape — the first child takes `Weight` of the row, the
        // second child takes `1 - Weight`. Renders both even when the child
        // count is more than 2 (extras land in the second pane); render
        // nothing-special when the child count is less than 2 (the
        // type-system doesn't enforce arity; the renderer must be
        // tolerant).
        let weightLeft = max 0.0 (min 1.0 spec.Weight)
        let weightRight = 1.0 - weightLeft

        let renderedChildren = spec.Children |> List.map (render ctx)

        let leftChildren, rightChildren =
            match renderedChildren with
            | [] -> [], []
            | [ a ] -> [ a ], []
            | a :: rest -> [ a ], rest

        Html.div
            [ prop.className "fuaran-layout-split-panel"
              prop.children
                  [ Html.div
                        [ prop.className "fuaran-split-pane fuaran-split-pane-left"
                          prop.style [ style.custom ("flex", sprintf "%f 1 0" weightLeft) ]
                          prop.children leftChildren ]
                    Html.div
                        [ prop.className "fuaran-split-pane fuaran-split-pane-right"
                          prop.style [ style.custom ("flex", sprintf "%f 1 0" weightRight) ]
                          prop.children rightChildren ] ] ]
    | LayoutKind.Tabs spec ->
        // Worked-example follow-on:
        // TabsSpec extends with `ActiveIndex: Binding<int>` and
        // `OnSelect: int -> Action<'Msg>`. The renderer extends
        // further with `TabHeaders` / `TabTags` / `ActiveTag` / `OnSelectTag`
        // for explicit per-tab declarations + a typed-tag overlay, plus full
        // ARIA tablist semantics + keyboard navigation. The integer-indexed
        // shape stays the canonical wire form; tag overlay is consumer
        // ergonomics for model-side DU-typed active-tab state.

        let parentNodeIdStr = parentNodeId

        // Card-heading-inference fallback (back-compat). Used only
        // when TabHeaders is None — legacy authoring shape.
        let tabsLabelFromChild (child: Node<'Msg>) : string =
            match child.Kind with
            | NodeKind.Layout(LayoutKind.Box { Role = BoxRole.Card
                                               Heading = Some h }) -> renderText ctx h
            | _ ->
                match child.Id with
                | NodeId s -> s

        // Resolved per-tab label + disabled state + optional icon.
        // When `TabHeaders = Some hs`, use the explicit declarations; when
        // `None`, walk children with the legacy inference path. FUARAN047
        // catches length mismatches at validate time so the runtime trusts
        // the alignment.
        let perTab
            : {| label: string
                 icon: string option
                 disabled: bool |} list =
            match spec.TabHeaders with
            | Some headers ->
                headers
                |> List.map (fun h ->
                    let disabled =
                        h.Disabled
                        |> Option.bind (BindingResolver.tryResolve ctx.Sources)
                        |> Option.defaultValue false

                    let icon = h.Icon |> Option.map (fun (IconSource s) -> s)

                    {| label = renderText ctx h.Label
                       icon = icon
                       disabled = disabled |})
            | None ->
                spec.Children
                |> List.map (fun child ->
                    {| label = tabsLabelFromChild child
                       icon = None
                       disabled = false |})

        let orientationClass =
            match spec.Orientation with
            | Horizontal -> "fuaran-tabs-horizontal"
            | Vertical -> "fuaran-tabs-vertical"

        // Tag-overlay resolution. When `TabTags` + `ActiveTag` are
        // both `Some`, resolve the tag binding to a string, find its position
        // in the tag list, and use that as the active index. Falls back to
        // the integer-indexed `ActiveIndex` when either is missing or the
        // resolved tag does not appear in `TabTags`. FUARAN049 warns on
        // ActiveTag-without-TabTags at validate time.
        let resolvedFromTag: int option =
            match spec.TabTags, spec.ActiveTag with
            | Some tags, Some tagBinding ->
                BindingResolver.tryResolve ctx.Sources tagBinding
                |> Option.bind (fun tag -> tags |> List.tryFindIndex ((=) tag))
            | _ -> None

        let activeIndex =
            resolvedFromTag
            |> Option.orElseWith (fun () -> BindingResolver.tryResolve ctx.Sources spec.ActiveIndex)
            |> Option.defaultValue 0
            |> max 0
            |> min (max 0 (spec.Children.Length - 1))

        let activeChild =
            spec.Children
            |> List.tryItem activeIndex
            |> Option.orElseWith (fun () -> spec.Children |> List.tryHead)

        // Composite click handler. Fires the integer-indexed `OnSelect i`
        // when present; additionally fires `OnSelectTag tag` when both the
        // typed-tag overlay and the per-tab tag are populated. Authors who
        // wire only the integer path see no behavioural change. Phase 426:
        // an omitted `OnSelect` writes the clicked index back to a writable
        // `ActiveIndex` binding (the write-back default), and an omitted
        // `OnSelectTag` with a populated tag overlay writes the clicked tag
        // back to a writable `ActiveTag` binding — so decoded tabs switch
        // panes with zero host code.
        let dispatchTabIndex (i: int) =
            match spec.OnSelect with
            | Some onSelect -> runAction ctx (onSelect i)
            | None -> writeBackTo ctx spec.ActiveIndex (Some(box i))

            match spec.OnSelectTag, spec.TabTags with
            | Some onTag, Some tags ->
                match List.tryItem i tags with
                | Some tag -> runAction ctx (onTag tag)
                | None -> ()
            | None, Some tags ->
                match spec.ActiveTag, List.tryItem i tags with
                | Some tagBinding, Some tag -> writeBackTo ctx tagBinding (Some(box tag))
                | _ -> ()
            | _ -> ()

        // Stable IDs the ARIA attributes reference. Derived from
        // the parent NodeId + tab index so cross-render snapshot diffs are
        // stable; collisions across multiple Tabs nodes on the same page are
        // structurally avoided by NodeId uniqueness (a renderer invariant).
        let tabId (i: int) = sprintf "%s-tab-%d" parentNodeIdStr i
        let panelId (i: int) = sprintf "%s-panel-%d" parentNodeIdStr i

        // Arrow-key navigation walks past disabled tabs. `dir` is
        // +1 / -1; wraps at list ends. Returns the same starting index when
        // no enabled tab is found (avoid infinite recursion via visited
        // counter).
        let nextEnabledIndex (start: int) (dir: int) : int =
            let n = perTab.Length

            if n = 0 then
                0
            else
                let rec loop (visited: int) (idx: int) =
                    if visited >= n then
                        start
                    else
                        let candidate = ((idx + dir) % n + n) % n

                        if perTab[candidate].disabled then
                            loop (visited + 1) candidate
                        else
                            candidate

                loop 0 start

        let firstEnabledIndex () : int =
            perTab |> List.tryFindIndex (fun t -> not t.disabled) |> Option.defaultValue 0

        let lastEnabledIndex () : int =
            perTab
            |> List.mapi (fun i t -> i, t)
            |> List.rev
            |> List.tryFind (fun (_, t) -> not t.disabled)
            |> Option.map fst
            |> Option.defaultValue (max 0 (perTab.Length - 1))

        let isVertical = spec.Orientation = Vertical

        // Focus management lives in the DOM directly — we read the stable
        // tab id and call `.focus()` on the matching element. This avoids
        // useRef-per-tab while still satisfying the ARIA tablist roving-
        // tabindex pattern (the active tab carries `tabindex=0`; the rest
        // carry `tabindex=-1`).
        let focusTab (i: int) : unit =
#if FABLE_COMPILER
            let el = Browser.Dom.document.getElementById (tabId i)

            if not (isNull el) then
                el.focus ()
#else
            ignore i
#endif

        let handleKeyDown (e: Browser.Types.KeyboardEvent) =
            let key = e.key
            let prevKey = if isVertical then "ArrowUp" else "ArrowLeft"
            let nextKey = if isVertical then "ArrowDown" else "ArrowRight"

            let target =
                if key = prevKey then Some(nextEnabledIndex activeIndex -1)
                elif key = nextKey then Some(nextEnabledIndex activeIndex 1)
                elif key = "Home" then Some(firstEnabledIndex ())
                elif key = "End" then Some(lastEnabledIndex ())
                elif key = "Enter" || key = " " then Some activeIndex
                else None

            match target with
            | Some i ->
                e.preventDefault ()
                dispatchTabIndex i
                focusTab i
            | None -> ()

        Html.div
            [ prop.className (sprintf "fuaran-layout-tabs %s" orientationClass)
              prop.children
                  [ Html.div
                        [ prop.className "fuaran-tabs-bar"
                          prop.role "tablist"
                          prop.custom ("aria-orientation", (if isVertical then "vertical" else "horizontal"))
                          prop.onKeyDown handleKeyDown
                          prop.children
                              [ for (i, t) in List.indexed perTab ->
                                    let isActive = i = activeIndex

                                    let cls =
                                        let parts =
                                            [ "fuaran-tab"
                                              if isActive then
                                                  "fuaran-tab-active"
                                              if t.disabled then
                                                  "fuaran-tab-disabled" ]

                                        String.concat " " parts

                                    let labelChildren =
                                        [ match t.icon with
                                          | Some iconSrc -> iconHook "fuaran-tab-icon" iconSrc
                                          | None -> ()
                                          Html.span [ prop.className "fuaran-tab-label"; prop.text t.label ] ]

                                    Html.button
                                        [ prop.id (tabId i)
                                          prop.className cls
                                          prop.role "tab"
                                          prop.custom ("aria-selected", (if isActive then "true" else "false"))
                                          prop.custom ("aria-controls", panelId i)
                                          prop.tabIndex (if isActive then 0 else -1)
                                          prop.custom ("data-tab-index", i)
                                          if t.disabled then
                                              prop.custom ("aria-disabled", "true")
                                              prop.disabled true
                                          prop.onClick (fun _ ->
                                              if not t.disabled then
                                                  dispatchTabIndex i)
                                          prop.children labelChildren ] ] ]
                    Html.div
                        [ prop.className "fuaran-tabs-panels"
                          prop.children (
                              match activeChild with
                              | Some childNode ->
                                  [ Html.div
                                        [ prop.id (panelId activeIndex)
                                          prop.role "tabpanel"
                                          prop.custom ("aria-labelledby", tabId activeIndex)
                                          prop.tabIndex 0
                                          prop.className "fuaran-tabs-panel"
                                          prop.children [ render ctx childNode ] ] ]
                              | None -> []
                          ) ] ] ]
    | LayoutKind.SummaryList spec ->
        // Feliz-parity additive: single-card-of-rows
        // shape — typically wraps LabelValueRow children with divider rules
        // between them (rendered via CSS, not per-child wrappers). Optional
        // section heading mirrors `Card.Heading`'s shape.
        Html.section
            [ prop.className "fuaran-layout-summary-list"
              prop.children
                  [ match spec.Heading with
                    | Some heading ->
                        Html.header
                            [ prop.className "fuaran-summary-list-heading"
                              prop.text (renderText ctx heading) ]
                    | None -> Html.none
                    Html.div
                        [ prop.className "fuaran-summary-list-body"
                          prop.children (spec.Children |> List.map (render ctx)) ] ] ]
    | LayoutKind.Disclosure spec ->
        // Additive: HTML-native `<details>` / `<summary>`
        // accordion. The `Open` binding overlays controlled-mode semantics
        // via React's `open` prop (`prop.isOpen`); when the binding resolves
        // to a value, that value drives the element. `resolvedOpen` folds
        // `DefaultOpen` in as the fallback, so the section starts in the right
        // state on the first frame without a separate `defaultOpen` attribute
        // (which React doesn't recognise on `<details>` and warns about — it
        // would also be dead, since the element is always controlled here).
        //
        // The `toggle` event fires on `<details>` whenever the open state
        // changes (user click or programmatic). The renderer reads the new
        // `open` value off `e.target` and dispatches `OnToggle`.
        //
        // Native HTML5 `<details>` already exposes `aria-expanded` through
        // the accessibility tree (browsers + assistive tech derive it from
        // the `open` attribute) — no explicit `aria-expanded` emission
        // needed. The outer `render` wrapper still applies the Node-level
        // Region role from `Defaults.Accessibility.disclosure`.
        let resolvedOpen =
            BindingResolver.tryResolve ctx.Sources spec.Open
            |> Option.defaultValue spec.DefaultOpen

        Html.details
            [ prop.className "fuaran-layout-disclosure"
              prop.isOpen resolvedOpen
              prop.custom (
                  "onToggle",
                  System.Func<Browser.Types.Event, unit>(fun (e: Browser.Types.Event) ->
                      // Fable.Browser.Dom 2.20 doesn't ship a typed
                      // HTMLDetailsElement binding. Read the `open` boolean
                      // attribute off the target HTMLElement — the attribute
                      // is present (value `""`) when open, absent (`null`)
                      // when closed.
                      let target = e.target :?> Browser.Types.HTMLElement
                      let isOpen = not (isNull (target.getAttribute "open"))
                      // Phase 426: the closure wins; an omitted handler writes
                      // the new open value back to a writable `Open` binding.
                      match spec.OnToggle with
                      | Some onToggle -> runAction ctx (onToggle isOpen)
                      | None -> writeBackTo ctx spec.Open (Some(box isOpen)))
              )
              prop.children
                  [ Html.summary
                        [ prop.className "fuaran-disclosure-summary"
                          prop.text (renderText ctx spec.Heading) ]
                    Html.div
                        [ prop.className "fuaran-disclosure-body"
                          prop.children (spec.Children |> List.map (render ctx)) ] ] ]
    | LayoutKind.Stepper spec ->
        // Each child becomes a numbered step; the active step is read from
        // `spec.ActiveStep` (a Binding<int>). Renderer marks the active
        // step with a class so CSS can style it. A step-header click fires
        // `spec.OnSelect i` (default no-op `Chain []`), mirroring tabs.
        let activeIndex =
            BindingResolver.tryResolve ctx.Sources spec.ActiveStep |> Option.defaultValue 0

        Html.div
            [ prop.className "fuaran-layout-stepper"
              prop.children
                  [ Html.ol
                        [ prop.className "fuaran-stepper-numbers"
                          prop.children
                              [ for i in 0 .. spec.Children.Length - 1 ->
                                    let isActive = i = activeIndex

                                    Html.li
                                        [ prop.className (
                                              if isActive then
                                                  "fuaran-stepper-step fuaran-stepper-step-active"
                                              else
                                                  "fuaran-stepper-step"
                                          )
                                          prop.onClick (fun _ -> runAction ctx (spec.OnSelect i))
                                          prop.text (sprintf "%d" (i + 1)) ] ] ]
                    Html.div
                        [ prop.className "fuaran-stepper-body"
                          prop.children (
                              match List.tryItem activeIndex spec.Children with
                              | Some node -> [ render ctx node ]
                              | None -> []
                          ) ] ] ]
    | LayoutKind.Modal spec ->
        // Phase 289 overlay render-fidelity contract: the overlay is ALWAYS in
        // the DOM (no React portal), positioned + z-indexed by CSS; closed =
        // the `hidden` attribute, not an absent node — so SSR and CSR emit the
        // identical structure and hydration never mismatches. `role="dialog"` +
        // `aria-modal` mark the dialog. Backdrop / close-button click fire
        // `OnDismiss` (client-only handlers; absent server-side, attached on
        // hydration — not a structural difference). Focus-trap is an additive
        // client enhancement that does not alter the hydrated DOM.
        let isOpen =
            BindingResolver.tryResolve ctx.Sources spec.Open |> Option.defaultValue false

        // Phase 426: the wire-survivable action wins; an omitted `OnDismiss`
        // writes `false` back to a writable `Open` binding — a decoded
        // dismissable modal closes itself with zero host code.
        let dismiss () =
            match spec.OnDismiss with
            | Some action -> runAction ctx action
            | None -> writeBackTo ctx spec.Open (Some(box false))

        let headingEls =
            match spec.Heading with
            | Some h -> [ Html.h2 [ prop.className "fuaran-modal-heading"; prop.text (renderText ctx h) ] ]
            | None -> []

        let dismissEls =
            if spec.Dismissable then
                [ Html.button
                      [ prop.className "fuaran-modal-dismiss"
                        prop.type' "button"
                        prop.ariaLabel "Close"
                        prop.onClick (fun _ -> dismiss ())
                        prop.text "×" ] ]
            else
                []

        Html.div
            [ prop.className "fuaran-modal-overlay"
              if not isOpen then
                  prop.custom ("hidden", "")
              prop.onClick (fun _ ->
                  if spec.Dismissable then
                      dismiss ())
              prop.children
                  [ Html.div
                        [ prop.className "fuaran-modal-dialog"
                          prop.role "dialog"
                          prop.custom ("aria-modal", "true")
                          prop.children (
                              headingEls
                              @ dismissEls
                              @ [ Html.div
                                      [ prop.className "fuaran-modal-body"
                                        prop.children (spec.Children |> List.map (render ctx)) ] ]
                          ) ] ] ]
    | LayoutKind.ScrollArea spec ->
        // Phase 289 — overflow/scroll container. The scroll axis is a class
        // (CSS owns `overflow`); optional pixel bounds are inline max-height /
        // max-width via the shared Feliz `style` DSL (identical SSR↔CSR).
        let axisClass =
            match spec.Orientation with
            | ScrollOrientation.Vertical -> "fuaran-scrollarea fuaran-scrollarea-vertical"
            | ScrollOrientation.Horizontal -> "fuaran-scrollarea fuaran-scrollarea-horizontal"
            | ScrollOrientation.Both -> "fuaran-scrollarea fuaran-scrollarea-both"

        let styleProps =
            [ match spec.MaxHeight with
              | Some h -> style.maxHeight (length.px h)
              | None -> ()
              match spec.MaxWidth with
              | Some w -> style.maxWidth (length.px w)
              | None -> () ]

        Html.div
            [ prop.className axisClass
              prop.tabIndex 0
              if not styleProps.IsEmpty then
                  prop.style styleProps
              prop.children (spec.Children |> List.map (render ctx)) ]

// ─── Displays ──────────────────────────────────────────────────────────────

and private renderDisplay
    (ctx: RenderContext<'Msg>)
    (parentNodeId: string)
    (state: StateBehaviour<'Msg>)
    (display: DisplayKind<'Msg>)
    : ReactElement =
    match display with
    | DisplayKind.Heading spec ->
        // Feliz-parity additive: Heading.Variant
        // appends `fuaran-heading-{variant}` so eyebrow / caption / lead
        // styling can pick out the shape without overriding `<h{Level}>`
        // semantics. `Standard` emits the bare `fuaran-heading` class —
        // existing callers see no class change.
        let variantSuffix =
            match spec.Variant with
            | HeadingVariant.Standard -> ""
            | HeadingVariant.Eyebrow -> " fuaran-heading-eyebrow"
            | HeadingVariant.Caption -> " fuaran-heading-caption"
            | HeadingVariant.Lead -> " fuaran-heading-lead"

        let props: IReactProperty list =
            [ prop.className (sprintf "fuaran-heading%s" variantSuffix)
              prop.text (renderText ctx spec.Text) ]

        // HTML heading levels are 1..6; anything outside that range falls to h6.
        match spec.Level with
        | 1 -> Html.h1 props
        | 2 -> Html.h2 props
        | 3 -> Html.h3 props
        | 4 -> Html.h4 props
        | 5 -> Html.h5 props
        | _ -> Html.h6 props
    | DisplayKind.Markdown spec ->
        // Phase 292: one deterministic GFM renderer in Renderer.Core. The same
        // `Markdown.toHtml` runs on Fable (client) and .NET (Renderer.Server),
        // so SSR↔CSR output is byte-identical by construction (was: npm `marked`
        // here, Markdig server-side — two different renders of the same node).
        let html = Markdown.toHtml (renderText ctx spec.Text)

        Html.div [ prop.className "fuaran-markdown"; prop.dangerouslySetInnerHTML html ]
    | DisplayKind.Metric spec -> renderMetric ctx parentNodeId state spec
    | DisplayKind.Badge spec ->
        Html.span
            [ prop.className (sprintf "fuaran-badge fuaran-badge-%s" (badgeVariantClass spec.Variant))
              prop.text (renderText ctx spec.Label) ]
    | DisplayKind.Skeleton spec ->
        Html.div
            [ prop.className "fuaran-skeleton"
              prop.children [ for _ in 1 .. spec.Rows -> Html.div [ prop.className "fuaran-skeleton-row" ] ] ]
    | DisplayKind.Callout spec -> renderCallout ctx spec
    | DisplayKind.Progress spec -> renderProgress ctx parentNodeId state spec
    | DisplayKind.Sparkline spec -> renderSparkline ctx spec
    | DisplayKind.Drawing spec ->
        // Phase 525 — first-party inline SVG from the canonical Core builder
        // (the ONE serialisation the SSR + TS + Python legs also emit, so the
        // client / server are parity by construction). Rides
        // `dangerouslySetInnerHTML` like Markdown/Math — an inert display node.
        Html.div [ prop.dangerouslySetInnerHTML (DrawingSvg.render ctx.Sources (renderText ctx) spec) ]
    | DisplayKind.LabelValueRow spec -> renderLabelValueRow ctx parentNodeId state spec
    | DisplayKind.Fact spec -> renderFact ctx spec
    | DisplayKind.Link spec ->
        // A real `<a href>` — crawlable + works with JS disabled. `href`
        // resolves the binding then passes through `Sanitize.sanitizeUrlOrBlank`
        // (blocks javascript:/vbscript:/raw data:; rejected URLs collapse to
        // `about:blank` so the anchor stays structurally valid). `rel` /
        // `target` emit when set; `download` emits a bare boolean attribute.
        let resolvedHref =
            BindingResolver.tryResolve ctx.Sources spec.Href |> Option.defaultValue ""

        let safeHref = Sanitize.sanitizeUrlOrBlank resolvedHref

        let optionalAttrs: IReactProperty list =
            [ match spec.Rel with
              | Some rel -> prop.rel rel
              | None -> ()
              match spec.Target with
              | Some target -> prop.custom ("target", target)
              | None -> ()
              if spec.Download then
                  prop.custom ("download", "") ]

        Html.a (
            [ prop.className "fuaran-link"; prop.href safeHref ]
            @ optionalAttrs
            @ [ prop.text (renderText ctx spec.Label) ]
        )
    | DisplayKind.Image spec ->
        // Phase 287 — real `<img>`; `src` resolves then passes through
        // `Sanitize.sanitizeUrlOrBlank` (blocks javascript:/vbscript:/file:);
        // `alt` is mandatory; `variant` appends an Avatar / Rounded class.
        let resolvedSrc =
            BindingResolver.tryResolve ctx.Sources spec.Src |> Option.defaultValue ""

        let safeSrc = Sanitize.sanitizeUrlOrBlank resolvedSrc

        let variantClass =
            match spec.Variant with
            | ImageVariant.Default -> "fuaran-image"
            | ImageVariant.Avatar -> "fuaran-image fuaran-image-avatar"
            | ImageVariant.Rounded -> "fuaran-image fuaran-image-rounded"

        Html.img
            [ prop.className variantClass
              prop.src safeSrc
              prop.alt (renderText ctx spec.Alt) ]
    | DisplayKind.List spec ->
        // Phase 287 — `<ol>` (ordered) / `<ul>` (unordered) of `<li>` items.
        let items =
            spec.Items
            |> List.map (fun item -> Html.li [ prop.className "fuaran-list-item"; prop.text (renderText ctx item) ])

        if spec.Ordered then
            Html.ol [ prop.className "fuaran-list fuaran-list-ordered"; prop.children items ]
        else
            Html.ul [ prop.className "fuaran-list fuaran-list-unordered"; prop.children items ]
    | DisplayKind.Toast spec ->
        // Phase 289 overlay render-fidelity contract: ALWAYS in the DOM (no
        // portal); closed = the `hidden` attribute. `role="status"` +
        // `aria-live="polite"` announce the message without interrupting.
        let isOpen =
            BindingResolver.tryResolve ctx.Sources spec.Open |> Option.defaultValue false

        let toneClass =
            match spec.Tone with
            | Default -> "default"
            | Subdued -> "subdued"
            | Brand -> "brand"
            | Success -> "success"
            | Warning -> "warning"
            | Critical -> "critical"
            | Info -> "info"

        let dismissEls =
            if spec.Dismissable then
                [ Html.button
                      [ prop.className "fuaran-toast-dismiss"
                        prop.type' "button"
                        prop.ariaLabel "Dismiss"
                        prop.text "×" ] ]
            else
                []

        Html.div
            [ prop.className (sprintf "fuaran-toast fuaran-toast-%s" toneClass)
              prop.role "status"
              prop.custom ("aria-live", "polite")
              if not isOpen then
                  prop.custom ("hidden", "")
              prop.children (
                  [ Html.span
                        [ prop.className "fuaran-toast-message"
                          prop.text (renderText ctx spec.Message) ] ]
                  @ dismissEls
              ) ]
    | DisplayKind.CodeBlock spec ->
        // Phase 290 — DETERMINISTIC `<pre><code>` (HTML-escaped via `prop.text`,
        // NO markdown library), byte-identical across all hosts + SSR. Syntax
        // highlighting is a client-only post-hydration enhancement that targets
        // the `language-{x}` class — explicitly NOT emitted here (outside the
        // parity output). Line numbers + highlight ranges are deterministic
        // class / data hooks the enhancement reads.
        let containerClass =
            if spec.LineNumbers then
                "fuaran-codeblock fuaran-codeblock-numbered"
            else
                "fuaran-codeblock"

        let highlightAttr =
            match spec.HighlightLines with
            | [] -> []
            | lines -> [ prop.custom ("data-highlight-lines", String.concat "," (lines |> List.map string)) ]

        let copyEls =
            if spec.Copyable then
                [ Html.button
                      [ prop.className "fuaran-codeblock-copy"
                        prop.type' "button"
                        prop.ariaLabel "Copy"
                        prop.text "Copy" ] ]
            else
                []

        Html.div (
            [ prop.className containerClass; prop.custom ("data-language", spec.Language) ]
            @ highlightAttr
            @ [ prop.children (
                    copyEls
                    @ [ Html.pre
                            [ prop.className "fuaran-codeblock-pre"
                              prop.children
                                  [ Html.code
                                        [ prop.className (sprintf "fuaran-codeblock-code language-%s" spec.Language)
                                          prop.text spec.Code ] ] ] ]
                ) ]
        )
    | DisplayKind.Math spec ->
        // Phase 658 — DETERMINISTIC native MathML for the closed subset (real
        // superscripts / fractions with NO JavaScript); the raw escaped LaTeX
        // source span for out-of-subset input (today's fallback, unchanged).
        // KaTeX upgrades EITHER shape client-only post-hydration (targets the
        // `.fuaran-math` container, reads `data-fuaran-math-src`), OUTSIDE the
        // parity output. `MathMl.translate` is a pure Fable-safe function shared
        // with the server renderer, so both emit byte-identical markup — see
        // docs/MATH-DEGRADATION.md (the normative subset + fixture oracle).
        let mathml = MathMl.translate spec.Source spec.Display

        let displayStr, isBlock =
            match spec.Display with
            | MathDisplay.Block -> "block", true
            | MathDisplay.Inline -> "inline", false

        let containerProps =
            [ prop.className (
                  if isBlock then
                      "fuaran-math fuaran-math-block"
                  else
                      "fuaran-math fuaran-math-inline"
              )
              prop.custom ("data-math-display", displayStr)
              prop.custom ("data-fuaran-math-src", spec.Source) ]

        let content =
            match mathml with
            | Some markup -> [ prop.dangerouslySetInnerHTML markup ]
            | None -> [ prop.children [ Html.span [ prop.className "fuaran-math-source"; prop.text spec.Source ] ] ]

        if isBlock then
            Html.div (containerProps @ content)
        else
            Html.span (containerProps @ content)

and private renderMetric
    (ctx: RenderContext<'Msg>)
    (parentNodeId: string)
    (state: StateBehaviour<'Msg>)
    (spec: MetricSpec)
    : ReactElement =
    // Phase 632 — the Metric value is a scalar slot: a `Binding.Transform`
    // resolves to its 1×1 result cell (a global aggregate / row-field lookup).
    let resolution = BindingResolver.resolveScalarFloat ctx.Sources spec.Value

    match resolution, state.OnLoading, state.OnError with
    | BindingResolver.NotResolved, Some loadingNode, _ -> render ctx loadingNode
    | BindingResolver.Errored msg, _, Some errorFn ->
        render
            ctx
            (errorFn
                { Kind = ErrorKind.BindingResolution
                  Message = msg
                  CorrelationId = correlationId parentNodeId })
    | _ ->
        Html.div
            [ prop.className (sprintf "fuaran-metric fuaran-metric-%s" (Theme.toneVar spec.Tone))
              prop.children
                  [ match spec.Icon with
                    | Some(IconSource icon) -> iconHook "fuaran-metric-icon" icon
                    | None -> Html.none
                    Html.div [ prop.className "fuaran-metric-label"; prop.text (renderText ctx spec.Label) ]
                    Html.div
                        [ prop.className "fuaran-metric-value"
                          prop.text (
                              match resolution with
                              | BindingResolver.Resolved value -> formatNumber spec.Format value
                              | BindingResolver.NotResolved -> "—"
                              | BindingResolver.Errored msg -> sprintf "(error: %s)" msg
                              | BindingResolver.I18nUnresolved key -> sprintf "[i18n:%s]" key
                          ) ]
                    match spec.Trend with
                    | Some trendBinding ->
                        Html.div
                            [ prop.className "fuaran-metric-trend"
                              prop.text (
                                  match BindingResolver.tryResolveScalarFloat ctx.Sources trendBinding with
                                  | Some t -> formatNumber (spec.TrendFormat |> Option.defaultValue CellFormat.None) t
                                  | None -> ""
                              ) ]
                    | None -> Html.none
                    match spec.Subtext with
                    | Some subtext ->
                        Html.div [ prop.className "fuaran-metric-subtext"; prop.text (renderText ctx subtext) ]
                    | None -> Html.none ] ]

and private renderFact (ctx: RenderContext<'Msg>) (spec: FactSpec) : ReactElement =
    // A labeled TEXT fact tile — Metric's chrome for a TextSource value.
    // No binding resolution stage: `renderText` already resolves the
    // Literal / Bound / I18n legs, exactly as it does for every label.
    let emphasisSuffix = if spec.Emphasis then " fuaran-fact-emphasis" else ""

    Html.div
        [ prop.className (sprintf "fuaran-fact fuaran-fact-%s%s" (Theme.toneVar spec.Tone) emphasisSuffix)
          prop.children
              [ Html.div [ prop.className "fuaran-fact-label"; prop.text (renderText ctx spec.Label) ]
                Html.div
                    [ prop.className "fuaran-fact-value"
                      prop.children
                          [ match spec.Icon with
                            | Some(IconSource icon) -> iconHook "fuaran-fact-icon" icon
                            | None -> Html.none
                            Html.span [ prop.text (renderText ctx spec.Value) ] ] ]
                match spec.Help with
                | Some help -> Html.div [ prop.className "fuaran-fact-help"; prop.text (renderText ctx help) ]
                | None -> Html.none ] ]

and private renderCallout (ctx: RenderContext<'Msg>) (spec: CalloutSpec) : ReactElement =
    Html.div
        [ prop.className (sprintf "fuaran-callout fuaran-callout-%s" (Theme.toneVar spec.Tone))
          prop.children
              [ match spec.Icon with
                | Some(IconSource icon) -> iconHook "fuaran-callout-icon" icon
                | None -> Html.none
                match spec.Heading with
                | Some heading ->
                    Html.div [ prop.className "fuaran-callout-heading"; prop.text (renderText ctx heading) ]
                | None -> Html.none
                Html.div [ prop.className "fuaran-callout-body"; prop.text (renderText ctx spec.Body) ]
                if spec.Dismissable then
                    Html.button
                        [ prop.className "fuaran-callout-dismiss"
                          prop.text "×"
                          prop.ariaLabel "Dismiss" ]
                else
                    Html.none ] ]

and private renderProgress
    (ctx: RenderContext<'Msg>)
    (parentNodeId: string)
    (state: StateBehaviour<'Msg>)
    (spec: ProgressSpec)
    : ReactElement =
    let resolution = BindingResolver.resolve ctx.Sources spec.Fraction

    match resolution, state.OnLoading, state.OnError with
    | BindingResolver.NotResolved, Some loadingNode, _ -> render ctx loadingNode
    | BindingResolver.Errored msg, _, Some errorFn ->
        render
            ctx
            (errorFn
                { Kind = ErrorKind.BindingResolution
                  Message = msg
                  CorrelationId = correlationId parentNodeId })
    | _ ->
        let fraction =
            match resolution with
            | BindingResolver.Resolved value -> value
            | _ -> 0.0

        Html.div
            [ prop.className (
                  sprintf
                      "fuaran-progress fuaran-progress-%s%s"
                      (Theme.toneVar spec.Tone)
                      (if spec.Indeterminate then
                           " fuaran-progress-indeterminate"
                       else
                           "")
              )
              prop.children
                  [ match spec.Label with
                    | Some label ->
                        Html.div [ prop.className "fuaran-progress-label"; prop.text (renderText ctx label) ]
                    | None -> Html.none
                    Html.div
                        [ prop.className "fuaran-progress-bar"
                          prop.children
                              [ Html.div
                                    [ prop.className "fuaran-progress-fill"
                                      prop.style [ style.width (length.percent (fraction * 100.0)) ] ] ] ]
                    match spec.Caveat with
                    | Some caveat ->
                        Html.div [ prop.className "fuaran-progress-caveat"; prop.text (renderText ctx caveat) ]
                    | None -> Html.none ] ]

and private renderLabelValueRow
    (ctx: RenderContext<'Msg>)
    (parentNodeId: string)
    (state: StateBehaviour<'Msg>)
    (spec: LabelValueRowSpec)
    : ReactElement =
    // Feliz-parity additive: label-left,
    // value-right, baseline-aligned single row. Honours `OnLoading` /
    // `OnError` like `Metric` (resolver against `Source`); `OnEmpty` is
    // intentionally not handled here — a single-row primitive has no
    // empty-collection semantics, unlike Grid.
    // Phase 632 — a scalar slot: Transform resolves to its 1×1 result cell.
    let resolution = BindingResolver.resolveScalarFloat ctx.Sources spec.Value

    match resolution, state.OnLoading, state.OnError with
    | BindingResolver.NotResolved, Some loadingNode, _ -> render ctx loadingNode
    | BindingResolver.Errored msg, _, Some errorFn ->
        render
            ctx
            (errorFn
                { Kind = ErrorKind.BindingResolution
                  Message = msg
                  CorrelationId = correlationId parentNodeId })
    | _ ->
        let emphasisSuffix =
            if spec.Emphasis then
                " fuaran-label-value-row-emphasis"
            else
                ""

        let valueText =
            match resolution with
            | BindingResolver.Resolved value -> formatNumber spec.Format value
            | BindingResolver.NotResolved -> "—"
            | BindingResolver.Errored msg -> sprintf "(error: %s)" msg
            | BindingResolver.I18nUnresolved key -> sprintf "[i18n:%s]" key

        Html.div
            [ prop.className (sprintf "fuaran-label-value-row%s" emphasisSuffix)
              prop.children
                  [ Html.div
                        [ prop.className "fuaran-label-value-row-label-block"
                          prop.children
                              [ Html.span
                                    [ prop.className "fuaran-label-value-row-label"
                                      prop.text (renderText ctx spec.Label) ]
                                match spec.Help with
                                | Some help ->
                                    Html.span
                                        [ prop.className "fuaran-label-value-row-help"
                                          prop.text (renderText ctx help) ]
                                | None -> Html.none ] ]
                    Html.span [ prop.className "fuaran-label-value-row-value"; prop.text valueText ] ] ]

and private renderSparkline (ctx: RenderContext<'Msg>) (spec: SparklineSpec) : ReactElement =
    // Inline SVG polyline; AG Charts sparkline is overkill for the
    // typical "trend mini-chart next to a Metric" use case the §4c contract
    // is designed for. Sparkline scales x by index and y by min/max
    // value; renders inside a 100 × 30 viewBox so it fits next to text.
    let resolution = BindingResolver.tryResolve ctx.Sources spec.Source

    match resolution with
    | Some series when not (Seq.isEmpty series) ->
        let values = series |> Seq.toArray
        let n = values.Length
        let minV = Array.min values
        let maxV = Array.max values

        let range = if maxV - minV < 1e-9 then 1.0 else maxV - minV

        let toPoint (i: int) (v: float) : string =
            let x = if n <= 1 then 50.0 else float i / float (n - 1) * 100.0
            let y = 30.0 - (v - minV) / range * 28.0 - 1.0
            sprintf "%.2f,%.2f" x y

        let points = values |> Array.mapi toPoint |> String.concat " "

        Svg.svg
            [ svg.className "fuaran-sparkline"
              svg.viewBox (0, 0, 100, 30)
              svg.custom ("preserveAspectRatio", "none")
              svg.children
                  [ Svg.polyline
                        [ svg.className "fuaran-sparkline-line"
                          svg.fill "none"
                          svg.stroke "currentColor"
                          svg.strokeWidth 1.5
                          svg.custom ("points", points) ] ] ]
    | _ -> Html.div [ prop.className "fuaran-sparkline fuaran-sparkline-empty"; prop.text "—" ]

// ─── Inputs ────────────────────────────────────────────────────────────────

and private renderInput (ctx: RenderContext<'Msg>) (input: InputKind<'Msg>) : ReactElement =
    match input with
    | InputKind.Button spec -> renderButton ctx spec
    | InputKind.Select spec -> renderSelect ctx spec
    | InputKind.Form spec -> renderForm ctx spec
    | InputKind.Filters specs -> renderFilters ctx specs
    | InputKind.FileUpload spec -> renderFileUpload ctx spec

and private renderButton (ctx: RenderContext<'Msg>) (spec: ButtonSpec<'Msg>) : ReactElement =
    let unwired = containsUnwiredAction spec.OnClick

    // Once a runtime is supplied the "unwired" hint is just a UI cue —
    // dispatching to the runtime still works — but it helps the
    // operator see which buttons rely on substrate vs pure Elmish.
    let variantClass = buttonVariantClass spec.Variant

    let className =
        if unwired then
            sprintf "fuaran-button fuaran-button-%s fuaran-button-unwired" variantClass
        else
            sprintf "fuaran-button fuaran-button-%s" variantClass

    // Worked-example follow-on:
    // ButtonSpec.Tooltip wins over the unwired-action hint. Author-
    // supplied tooltips are explicit; the unwired hint is best-effort
    // operator UX. If both fire, the explicit Tooltip is more useful.
    let tooltipText =
        match spec.Tooltip with
        | Some text -> Some(renderText ctx text)
        | None when unwired ->
            Some
                "This action routes through the IFuaranRuntime substrate (Action.Call/Notify/Navigate/SetState/AiTool)."
        | None -> None

    // Optional bound disabled-state: emit the HTML `disabled` attribute
    // when the binding resolves `true`. An absent binding (or one that
    // can't resolve) leaves the button enabled — the v1 default.
    let isDisabled =
        spec.Disabled
        |> Option.bind (BindingResolver.tryResolve ctx.Sources)
        |> Option.defaultValue false

    Html.button
        [ prop.className className
          // The uniform icon hook: an icon-bearing button wraps its label as a
          // text node beside the hook; an icon-less button keeps the plain
          // `prop.text` shape (markup unchanged for existing trees).
          match spec.Icon with
          | Some(IconSource icon) ->
              prop.children [ iconHook "fuaran-button-icon" icon; Html.text (renderText ctx spec.Label) ]
          | None -> prop.text (renderText ctx spec.Label)
          match tooltipText with
          | Some t -> prop.title t
          | None -> ()
          if isDisabled then
              prop.disabled true
          prop.onClick (fun _ -> runAction ctx spec.OnClick) ]

and private renderSelect (ctx: RenderContext<'Msg>) (spec: SelectSpec<'Msg>) : ReactElement =
    let options = resolveOptions ctx spec.Source

    let selected = BindingResolver.tryResolve ctx.Sources spec.Value |> Option.flatten

    let placeholderItem =
        match spec.Placeholder with
        | Some placeholder -> [ Html.option [ prop.value ""; prop.text (renderText ctx placeholder) ] ]
        | None -> []

    let optionItems =
        [ for option in options -> Html.option [ prop.value option.Value; prop.text (renderText ctx option.Label) ] ]

    // Phase 130: optional bound disabled-state — emit the HTML `disabled`
    // attribute on the `<select>` when the binding resolves `true`.
    let isDisabled =
        spec.Disabled
        |> Option.bind (BindingResolver.tryResolve ctx.Sources)
        |> Option.defaultValue false

    let control =
        if spec.Multiple then
            // Phase 291 — `<select multiple>`. The selection is the resolved
            // `Values` list; `onChange` reads every selected option and fires
            // `OnChangeMulti` with the value list. Phase 426: an omitted
            // handler writes the list back to a writable `Values` binding
            // (the write-back default); no `Values` binding ⇒ no write.
            let selectedValues =
                spec.Values
                |> Option.bind (BindingResolver.tryResolve ctx.Sources)
                |> Option.defaultValue []

            Html.select
                [ prop.className "fuaran-select-control"
                  prop.multiple true
                  prop.value (selectedValues |> List.toArray)
                  if isDisabled then
                      prop.disabled true
                  prop.custom (
                      "onChange",
                      System.Func<Browser.Types.Event, unit>(fun (e: Browser.Types.Event) ->
                          let target = e.target :?> Browser.Types.HTMLSelectElement
                          let opts = target.selectedOptions

                          let chosen =
                              [ for i in 0 .. opts.length - 1 -> (opts[i] :?> Browser.Types.HTMLOptionElement).value ]

                          match spec.OnChangeMulti, spec.Values with
                          | Some onChangeMulti, _ -> runAction ctx (onChangeMulti chosen)
                          | None, Some values -> writeBackTo ctx values (Some(box chosen))
                          | None, None -> ())
                  )
                  prop.children optionItems ]
        else
            Html.select
                [ prop.className "fuaran-select-control"
                  prop.value (selected |> Option.defaultValue "")
                  if isDisabled then
                      prop.disabled true
                  prop.onChange (fun (v: string) ->
                      // Phase 426: the closure wins; an omitted handler writes
                      // the chosen option back to a writable `Value` binding
                      // (a cleared choice clears the slot).
                      let chosen = if v = "" then None else Some v

                      match spec.OnChange with
                      | Some onChange -> runAction ctx (onChange chosen)
                      | None -> writeBackTo ctx spec.Value (chosen |> Option.map box))
                  prop.children (placeholderItem @ optionItems) ]

    Html.label
        [ prop.className "fuaran-select"
          prop.children
              [ Html.span [ prop.className "fuaran-select-label"; prop.text (renderText ctx spec.Label) ]
                control ] ]

and private renderForm (ctx: RenderContext<'Msg>) (spec: FormSpec<'Msg>) : ReactElement =
    let fieldNodes = [ for field in spec.Fields -> renderFormField ctx field ]

    let submitNode =
        Html.button
            [ prop.className "fuaran-form-submit"
              prop.type'.submit
              prop.text (renderText ctx spec.SubmitLabel) ]

    // Phase 130: optional bound form-level disabled-state. When the slot is
    // present, wrap the fields + submit in a `<fieldset>` so a single
    // resolved `disabled` cascades to every descendant control (native HTML).
    // When the slot is absent (`None`), render the body directly — unchanged
    // DOM for the common case. Parity-locked with the TS `@fuaran-ui/renderer`
    // `renderForm` (the `fuaran-form-fieldset` wrapper).
    let body = fieldNodes @ [ submitNode ]

    let formChildren =
        match spec.Disabled with
        | Some disabled ->
            let isDisabled =
                BindingResolver.tryResolve ctx.Sources disabled |> Option.defaultValue false

            [ Html.fieldSet
                  [ prop.className "fuaran-form-fieldset"
                    if isDisabled then
                        prop.disabled true
                    prop.children body ] ]
        | None -> body

    Html.form
        [ prop.className "fuaran-form"
          prop.onSubmit (fun (e: Browser.Types.Event) ->
              e.preventDefault ()
              // Notify every Local-bound input whose FlushOn =
              // OnSubmit so each buffer drains through its OnCommit
              // BEFORE the form's typed OnSubmit fires. Order matters
              // because the typed OnSubmit may dispatch a network call
              // that reads the just-committed values.
              LocalBindings.dispatchFormCommit ()
              runAction ctx spec.OnSubmit)
          prop.children formChildren ]

and private renderFormField (ctx: RenderContext<'Msg>) (field: FormField<'Msg>) : ReactElement =
    let labelText = renderText ctx field.Label

    let labelWithRequired = if field.Required then labelText + " *" else labelText

    let (NodeId fieldNodeId) = NodeId field.Id

    // Phase 426 — the control write-back default. A `Some` handler dispatches
    // exactly as before (the closure wins); a `None` handler writes the typed
    // change back to the field's own value binding when that binding is
    // directly `Binding.State` / `Binding.Filter` (see `writeBackTo`). A
    // cleared choice clears the slot rather than writing an empty value.
    let handle (onChange: ('v -> Action<'Msg>) option) (binding: Binding<'T>) (write: obj option) (v: 'v) : unit =
        match onChange with
        | Some h -> runAction ctx (h v)
        | None -> writeBackTo ctx binding write

    let control =
        match field.Kind with
        | FormFieldKind.Text(value, onChange) ->
            match value with
            | Binding.Local local ->
                // Local-bound text field — render via the
                // function-component shape that maintains the per-NodeId
                // React.useState buffer and InitialFrom re-sync invariant.
                let external =
                    BindingResolver.tryResolve ctx.Sources local.InitialFrom
                    |> Option.defaultValue ""

                let formatter = local.Format |> Option.defaultValue id

                let commit (parsed: string) : unit =
                    // local.OnCommit returns an obj-erased Action; unbox
                    // back to the typed Action<'Msg> the smart-ctor wrapped.
                    let action = unbox<Action<'Msg>> (local.OnCommit parsed)
                    runAction ctx action

                LocalBindings.localTextInput
                    {| nodeId = fieldNodeId
                       fieldId = field.Id
                       className = "fuaran-form-input"
                       required = field.Required
                       externalValue = external
                       flushOn = local.FlushOn
                       formatter = formatter
                       parser = local.Parse
                       commit = commit |}
            | _ ->
                let current = BindingResolver.tryResolve ctx.Sources value |> Option.defaultValue ""

                Html.input
                    [ prop.className "fuaran-form-input"
                      prop.type'.text
                      prop.id field.Id
                      prop.required field.Required
                      prop.value current
                      prop.onChange (fun (v: string) -> handle onChange value (Some(box v)) v) ]
        | FormFieldKind.Number(value, onChange) ->
            match value with
            | Binding.Local local ->
                // Local-bound number field — see the Text-side
                // mirror above. The renderer uses `type=text` +
                // `inputMode=numeric` so the consumer-side formatter
                // (thousands separators etc.) survives.
                let external =
                    BindingResolver.tryResolve ctx.Sources local.InitialFrom
                    |> Option.defaultValue 0.0

                let formatter = local.Format |> Option.defaultValue (fun v -> string v)

                let commit (parsed: float) : unit =
                    let action = unbox<Action<'Msg>> (local.OnCommit parsed)
                    runAction ctx action

                LocalBindings.localNumberInput
                    {| nodeId = fieldNodeId
                       fieldId = field.Id
                       className = "fuaran-form-input"
                       required = field.Required
                       externalValue = external
                       flushOn = local.FlushOn
                       formatter = formatter
                       parser = local.Parse
                       commit = commit
                       // `FormFieldKind.Number` carries no
                       // constraints, so the existing arm renders byte-
                       // identically by passing the all-None default.
                       constraints = Fuaran.UI.Defaults.numberFieldConstraints |}
            | _ ->
                let current =
                    BindingResolver.tryResolve ctx.Sources value |> Option.defaultValue 0.0

                Html.input
                    [ prop.className "fuaran-form-input"
                      prop.type'.number
                      prop.id field.Id
                      prop.required field.Required
                      prop.value current
                      prop.onChange (fun (v: float) -> handle onChange value (Some(box v)) v) ]
        | FormFieldKind.Checkbox(value, onToggle) ->
            let current =
                BindingResolver.tryResolve ctx.Sources value |> Option.defaultValue false

            Html.input
                [ prop.className "fuaran-form-checkbox"
                  prop.type'.checkbox
                  prop.id field.Id
                  prop.isChecked current
                  prop.onChange (fun (b: bool) -> handle onToggle value (Some(box b)) b) ]
        | FormFieldKind.Choice(options, value, onChange) ->
            let opts = resolveOptions ctx options
            let current = BindingResolver.tryResolve ctx.Sources value |> Option.flatten

            let optionItems =
                Html.option [ prop.value ""; prop.text "—" ]
                :: [ for option in opts ->
                         Html.option [ prop.value option.Value; prop.text (renderText ctx option.Label) ] ]

            Html.select
                [ prop.className "fuaran-form-select"
                  prop.id field.Id
                  prop.required field.Required
                  prop.value (current |> Option.defaultValue "")
                  prop.onChange (fun (v: string) ->
                      let chosen = if v = "" then None else Some v
                      handle onChange value (chosen |> Option.map box) chosen)
                  prop.children optionItems ]
        | FormFieldKind.Range(value, onChange, _) ->
            // 0.2.0 filters-unification: dual-thumb numeric range as a form
            // control (absorbed FilterKind.RangeFilter). Two paired number
            // inputs bound to the (min, max) tuple; either change emits the
            // whole tuple through the standard write-back.
            let current =
                BindingResolver.tryResolve ctx.Sources value |> Option.defaultValue (0.0, 0.0)

            let minV, maxV = current

            Html.span
                [ prop.className "fuaran-field-range"
                  prop.children
                      [ Html.input
                            [ prop.type'.number
                              prop.className "fuaran-field-range-min"
                              prop.value minV
                              prop.onChange (fun (v: float) -> handle onChange value None (v, maxV)) ]
                        Html.span [ prop.className "fuaran-field-range-sep"; prop.text "–" ]
                        Html.input
                            [ prop.type'.number
                              prop.className "fuaran-field-range-max"
                              prop.value maxV
                              prop.onChange (fun (v: float) -> handle onChange value None (minV, v)) ] ] ]
        | FormFieldKind.RangedNumber(value, onChange, constraints) ->
            // Parallel-additive Number case with optional Min /
            // Max / Step. Emit the corresponding HTML attributes when
            // present; the input otherwise renders exactly like the
            // plain `Number` arm (Local-bound variant included).
            let minAttrs =
                [ match constraints.Min with
                  | Some m -> prop.min m
                  | None -> ()
                  match constraints.Max with
                  | Some m -> prop.max m
                  | None -> ()
                  match constraints.Step with
                  | Some s -> prop.step s
                  | None -> () ]

            match value with
            | Binding.Local local ->
                let external =
                    BindingResolver.tryResolve ctx.Sources local.InitialFrom
                    |> Option.defaultValue 0.0

                let formatter = local.Format |> Option.defaultValue (fun v -> string v)

                let commit (parsed: float) : unit =
                    let action = unbox<Action<'Msg>> (local.OnCommit parsed)
                    runAction ctx action

                LocalBindings.localNumberInput
                    {| nodeId = fieldNodeId
                       fieldId = field.Id
                       className = "fuaran-form-input"
                       required = field.Required
                       externalValue = external
                       flushOn = local.FlushOn
                       formatter = formatter
                       parser = local.Parse
                       commit = commit
                       constraints = constraints |}
            | _ ->
                let current =
                    BindingResolver.tryResolve ctx.Sources value |> Option.defaultValue 0.0

                Html.input (
                    [ prop.className "fuaran-form-input"
                      prop.type'.number
                      prop.id field.Id
                      prop.required field.Required
                      prop.value current
                      prop.onChange (fun (v: float) -> handle onChange value (Some(box v)) v) ]
                    @ minAttrs
                )
        | FormFieldKind.TextArea(value, onChange, rows) ->
            let current = BindingResolver.tryResolve ctx.Sources value |> Option.defaultValue ""

            Html.textarea
                [ prop.className "fuaran-form-textarea"
                  prop.id field.Id
                  prop.required field.Required
                  prop.rows rows
                  prop.value current
                  prop.onChange (fun (v: string) -> handle onChange value (Some(box v)) v) ]
        | FormFieldKind.SegmentedChoice(options, value, onChange, orientation) ->
            // Visible-options exclusive-choice input. Horizontal
            // emits a `role="radiogroup"` of `role="radio"` buttons styled
            // as a segmented control; Vertical emits a `<fieldset>` of
            // `<input type=radio>` elements grouped by `name = fieldId` so
            // the browser handles arrow-key cycling natively.
            renderSegmentedChoiceCore
                ctx
                field.Id
                options
                value
                (fun chosen -> handle onChange value (chosen |> Option.map box) chosen)
                orientation
        | FormFieldKind.Date(value, onChange, variant, constraints) ->
            // Phase 288 — native date / time / datetime control. The bound
            // value is an ISO-8601 string; min/max are ISO strings, step is
            // seconds. Parity-locked with the SSR + TS + Python renderers.
            let current = BindingResolver.tryResolve ctx.Sources value |> Option.defaultValue ""

            let inputType =
                match variant with
                | DateVariant.Date -> "date"
                | DateVariant.Time -> "time"
                | DateVariant.DateTime -> "datetime-local"

            let constraintAttrs =
                [ match constraints.Min with
                  | Some m -> prop.custom ("min", m)
                  | None -> ()
                  match constraints.Max with
                  | Some m -> prop.custom ("max", m)
                  | None -> ()
                  match constraints.Step with
                  | Some s -> prop.step s
                  | None -> () ]

            Html.input (
                [ prop.className "fuaran-form-input fuaran-form-date"
                  prop.type' inputType
                  prop.id field.Id
                  prop.required field.Required
                  prop.value current
                  prop.onChange (fun (v: string) -> handle onChange value (Some(box v)) v) ]
                @ constraintAttrs
            )

    Html.div
        [ prop.className "fuaran-form-field"
          prop.children
              [ Html.label
                    [ prop.className "fuaran-form-label"
                      prop.htmlFor field.Id
                      prop.text labelWithRequired ]
                control
                match field.Help with
                | Some help -> Html.div [ prop.className "fuaran-form-help"; prop.text (renderText ctx help) ]
                | None -> Html.none ] ]

and private renderFilters (ctx: RenderContext<'Msg>) (specs: FilterSpec<'Msg> list) : ReactElement =
    Html.div
        [ prop.className "fuaran-filters"
          prop.children [ for spec in specs -> renderFilterSpec ctx spec ] ]

and private renderFilterSpec (ctx: RenderContext<'Msg>) (spec: FilterSpec<'Msg>) : ReactElement =
    let labelText = renderText ctx spec.Label

    // Phase 423 default write path: a chip whose `onChange` is `None` (the declarative / AI-authored
    // shape) writes its typed value to the reactive `FilterStore` under `spec.Name` — zero host code,
    // and every `Binding.Filter` reader re-renders (the `useFilterKeys` subscription). A `Some`
    // closure dispatches exactly as before (no store write) — F#-authored apps are unchanged.
    let writeChoiceValue (chosen: string option) : unit =
        match chosen with
        | Some v -> FilterStore.set spec.Name (box v)
        | None -> FilterStore.clear spec.Name // a cleared choice removes the key

    let handleText (onChange: (string -> Action<'Msg>) option) (v: string) : unit =
        match onChange with
        | Some oc -> runAction ctx (oc v)
        | None -> FilterStore.set spec.Name (box v)

    let handleChoice (onChange: (string option -> Action<'Msg>) option) (chosen: string option) : unit =
        match onChange with
        | Some oc -> runAction ctx (oc chosen)
        | None -> writeChoiceValue chosen

    let handleRange (onChange: (float * float -> Action<'Msg>) option) (pair: float * float) : unit =
        match onChange with
        | Some oc -> runAction ctx (oc pair)
        | None -> FilterStore.set spec.Name (box pair)

    let handleFloat (onChange: (float -> Action<'Msg>) option) (v: float) : unit =
        match onChange with
        | Some oc -> runAction ctx (oc v)
        | None -> FilterStore.set spec.Name (box v)

    let handleBool (onToggle: (bool -> Action<'Msg>) option) (v: bool) : unit =
        match onToggle with
        | Some ot -> runAction ctx (ot v)
        | None -> FilterStore.set spec.Name (box v)

    // 0.2.0 filters-unification: the chip's control is an ordinary
    // FormFieldKind; every declarative (handler-free) control writes its own
    // `$filters.<name>`.
    let control =
        match spec.Field with
        | FormFieldKind.Text(value, onChange) ->
            let current = BindingResolver.tryResolve ctx.Sources value |> Option.defaultValue ""

            Html.input
                [ prop.className "fuaran-filter-input"
                  prop.type'.text
                  prop.placeholder labelText
                  prop.value current
                  prop.onChange (fun (v: string) -> handleText onChange v) ]
        | FormFieldKind.Number(value, onChange) ->
            let current =
                BindingResolver.tryResolve ctx.Sources value |> Option.defaultValue 0.0

            Html.input
                [ prop.className "fuaran-filter-input"
                  prop.type'.number
                  prop.value current
                  prop.onChange (fun (v: float) -> handleFloat onChange v) ]
        | FormFieldKind.RangedNumber(value, onChange, _) ->
            let current =
                BindingResolver.tryResolve ctx.Sources value |> Option.defaultValue 0.0

            Html.input
                [ prop.className "fuaran-filter-input"
                  prop.type'.number
                  prop.value current
                  prop.onChange (fun (v: float) -> handleFloat onChange v) ]
        | FormFieldKind.Checkbox(value, onToggle) ->
            let current =
                BindingResolver.tryResolve ctx.Sources value |> Option.defaultValue false

            Html.input
                [ prop.className "fuaran-filter-checkbox"
                  prop.type'.checkbox
                  prop.isChecked current
                  prop.onChange (fun (v: bool) -> handleBool onToggle v) ]
        | FormFieldKind.TextArea(value, onChange, rows) ->
            let current = BindingResolver.tryResolve ctx.Sources value |> Option.defaultValue ""

            Html.textarea
                [ prop.className "fuaran-filter-input"
                  prop.rows rows
                  prop.value current
                  prop.onChange (fun (v: string) -> handleText onChange v) ]
        | FormFieldKind.Date(value, onChange, _, _) ->
            let current = BindingResolver.tryResolve ctx.Sources value |> Option.defaultValue ""

            Html.input
                [ prop.className "fuaran-filter-input"
                  prop.type'.date
                  prop.value current
                  prop.onChange (fun (v: string) -> handleText onChange v) ]
        | FormFieldKind.Choice(options, value, onChange) ->
            let opts = resolveOptions ctx options
            let current = BindingResolver.tryResolve ctx.Sources value |> Option.flatten

            let optionItems =
                Html.option [ prop.value ""; prop.text "—" ]
                :: [ for option in opts ->
                         Html.option [ prop.value option.Value; prop.text (renderText ctx option.Label) ] ]

            Html.select
                [ prop.className "fuaran-filter-select"
                  prop.value (current |> Option.defaultValue "")
                  prop.onChange (fun (v: string) -> handleChoice onChange (if v = "" then None else Some v))
                  prop.children optionItems ]
        | FormFieldKind.Range(value, onChange, _) ->
            // Two-input range — min + max bound to a tuple binding;
            // any change emits the whole tuple back. Real range
            // sliders are session-4+ ergonomic territory.
            let current =
                BindingResolver.tryResolve ctx.Sources value |> Option.defaultValue (0.0, 0.0)

            let minV, maxV = current

            Html.span
                [ prop.className "fuaran-filter-range"
                  prop.children
                      [ Html.input
                            [ prop.type'.number
                              prop.className "fuaran-filter-range-min"
                              prop.value minV
                              prop.onChange (fun (v: float) -> handleRange onChange (v, maxV)) ]
                        Html.span [ prop.className "fuaran-filter-range-sep"; prop.text "–" ]
                        Html.input
                            [ prop.type'.number
                              prop.className "fuaran-filter-range-max"
                              prop.value maxV
                              prop.onChange (fun (v: float) -> handleRange onChange (minV, v)) ] ] ]
        | FormFieldKind.SegmentedChoice(options, value, onChange, orientation) ->
            // Visible-options exclusive-choice filter. Parallel
            // surface to `FormFieldKind.SegmentedChoice`; uses the filter's
            // `Name` as the id-namespace for the radiogroup / fieldset.
            renderSegmentedChoiceCore ctx spec.Name options value (handleChoice onChange) orientation

    Html.label
        [ prop.className "fuaran-filter"
          prop.children
              [ Html.span [ prop.className "fuaran-filter-label"; prop.text labelText ]
                control ] ]

/// Shared segmented-control / radio-group renderer.
/// Drives both `FormFieldKind.SegmentedChoice` (id namespace = field.Id) and
/// `FilterKind.SegmentedFilter` (id namespace = spec.Name). Emits two
/// distinct shapes depending on orientation:
///
///   Horizontal — `<div role="radiogroup">` of `<button role="radio"
///     aria-checked={…}>` per option. Keyboard arrow keys cycle focus +
///     selection (wraps at ends, matching the WAI-ARIA radiogroup pattern).
///
///   Vertical — `<fieldset>` with a visually-hidden `<legend>` and per-
///     option `<input type="radio" name=idNamespace>` + `<label>`. The
///     shared `name` makes the browser handle arrow-key cycling natively.
and private renderSegmentedChoiceCore
    (ctx: RenderContext<'Msg>)
    (idNamespace: string)
    (options: Binding<SelectOption list>)
    (value: Binding<string option>)
    // A side-effecting change handler (Phase 423), not an `Action` factory: the form-field caller
    // dispatches through `runAction`, while a declarative `SegmentedFilter` writes the FilterStore —
    // so the shared core stays agnostic to which channel the change drives.
    (handleChange: string option -> unit)
    (orientation: Orientation)
    : ReactElement =
    let opts = resolveOptions ctx options
    let current = BindingResolver.tryResolve ctx.Sources value |> Option.flatten

    let optionId (index: int) : string = sprintf "%s-opt-%d" idNamespace index

    match orientation with
    | Horizontal ->
        // Per-option button. `role="radio"` + `aria-checked` carry the
        // semantic radiogroup contract; `tabindex` is set to 0 only on the
        // active option (the roving-tabindex pattern) so Tab moves into the
        // group once and Arrow keys move within it.
        let activeIndex =
            match current with
            | Some v -> opts |> List.tryFindIndex (fun o -> o.Value = v) |> Option.defaultValue -1
            | None -> -1

        let optionButton (index: int) (option: SelectOption) : ReactElement =
            let isActive = index = activeIndex
            let labelText = renderText ctx option.Label

            Html.button
                [ prop.className "fuaran-segmented-option"
                  prop.type'.button
                  prop.id (optionId index)
                  prop.ariaChecked isActive
                  prop.role "radio"
                  prop.tabIndex (
                      if isActive then 0
                      elif activeIndex < 0 && index = 0 then 0
                      else -1
                  )
                  prop.text labelText
                  prop.onClick (fun _ -> handleChange (Some option.Value)) ]

        let cycle (delta: int) : unit =
            if List.isEmpty opts then
                ()
            else
                let count = List.length opts

                let nextIndex =
                    match activeIndex with
                    | i when i < 0 ->
                        // No selection yet — entering with ArrowRight picks
                        // the first; ArrowLeft picks the last.
                        if delta > 0 then 0 else count - 1
                    | i ->
                        // Wrap-around for both directions per the WAI-ARIA
                        // radiogroup pattern.
                        ((i + delta) % count + count) % count

                let nextOption = opts |> List.item nextIndex
                handleChange (Some nextOption.Value)

        let onKeyDown (e: Browser.Types.KeyboardEvent) : unit =
            match e.key with
            | "ArrowRight"
            | "ArrowDown" ->
                e.preventDefault ()
                cycle 1
            | "ArrowLeft"
            | "ArrowUp" ->
                e.preventDefault ()
                cycle -1
            | "Home" ->
                e.preventDefault ()

                match opts with
                | first :: _ -> handleChange (Some first.Value)
                | [] -> ()
            | "End" ->
                e.preventDefault ()

                match List.tryLast opts with
                | Some last -> handleChange (Some last.Value)
                | None -> ()
            | _ -> ()

        Html.div
            [ prop.className "fuaran-segmented-horizontal"
              prop.id idNamespace
              prop.role "radiogroup"
              prop.custom ("aria-orientation", "horizontal")
              prop.onKeyDown onKeyDown
              prop.children [ for index, option in List.indexed opts -> optionButton index option ] ]
    | Vertical ->
        // Native radio inputs grouped by shared `name = idNamespace` — the
        // browser handles arrow-key cycling automatically. Each row is a
        // `<input>` + `<label>` pair; the fieldset's `<legend>` is the
        // group's accessible name (visually-hidden via reference CSS).
        let optionRow (index: int) (option: SelectOption) : ReactElement =
            let inputId = optionId index
            let labelText = renderText ctx option.Label
            let isChecked = current = Some option.Value

            Html.div
                [ prop.className "fuaran-segmented-row"
                  prop.children
                      [ Html.input
                            [ prop.type'.radio
                              prop.id inputId
                              prop.name idNamespace
                              prop.value option.Value
                              prop.isChecked isChecked
                              prop.onChange (fun (checked': bool) ->
                                  if checked' then
                                      handleChange (Some option.Value)) ]
                        Html.label [ prop.htmlFor inputId; prop.text labelText ] ] ]

        Html.fieldSet
            [ prop.className "fuaran-segmented-vertical"
              prop.custom ("aria-orientation", "vertical")
              prop.children (
                  Html.legend [ prop.className "fuaran-segmented-legend"; prop.text idNamespace ]
                  :: [ for index, option in List.indexed opts -> optionRow index option ]
              ) ]

and private renderFileUpload (ctx: RenderContext<'Msg>) (spec: FileUploadSpec<'Msg>) : ReactElement =
    let acceptStr =
        if List.isEmpty spec.Accept then
            ""
        else
            String.concat "," spec.Accept

    // Phase 130: optional bound disabled-state — emit the HTML `disabled`
    // attribute on the file input when the binding resolves `true`.
    let isDisabled =
        spec.Disabled
        |> Option.bind (BindingResolver.tryResolve ctx.Sources)
        |> Option.defaultValue false

    let inputProps: IReactProperty list =
        [ prop.className "fuaran-file-upload-input"
          prop.type'.file
          prop.multiple spec.Multiple
          if isDisabled then
              prop.disabled true
          if acceptStr <> "" then
              prop.accept acceptStr
          // Feliz `prop.onChange (fun (files: File list) -> ...)` overload —
          // pulls files off `e.target.files` for us. Per-file metadata is
          // browser-side; the consumer's Action.Call typically pairs the
          // list with a multipart upload.
          prop.onChange (fun (files: Browser.Types.File list) ->
              let selections =
                  // Phase 136: carry an opaque `FileRef` per selection. `Ref.Id`
                  // is an index-qualified stable token (the only part that ever
                  // serialises); `Ref.Handle` boxes the actual browser `File`
                  // so `Action.ReadFileBody` can read the blob without the
                  // consumer touching `FileReader`.
                  files
                  |> List.mapi (fun i f ->
                      { Name = f.name
                        Size = int64 f.size
                        MimeType = f.``type``
                        Ref =
                          { Id = sprintf "%d:%s" i f.name
                            Handle = Some(box f) } })

              runAction ctx (spec.OnSelect selections)) ]

    Html.label
        [ prop.className "fuaran-file-upload"
          prop.children
              [ Html.span
                    [ prop.className "fuaran-file-upload-label"
                      prop.text (renderText ctx spec.Label) ]
                Html.input inputProps ] ]

// ─── Visualisations ────────────────────────────────────────────────────────

and private renderVis
    (ctx: RenderContext<'Msg>)
    (parentNodeId: string)
    (state: StateBehaviour<'Msg>)
    (vis: VisKind<'Msg>)
    : ReactElement =
    match vis with
    | VisKind.DataGrid spec ->
        // Phase 393 — a static read-only grid renders the semantic <table> leg (byte-identical to the
        // retired Table); a data-bound grid takes the ordinary grid path.
        match spec.StaticRows with
        | Some(headers, rows) ->
            renderTable
                ctx
                { Headers = headers
                  Rows = rows
                  OnRowClick = None }
        | None -> renderGrid ctx parentNodeId state spec
    | VisKind.Chart spec -> renderChart ctx parentNodeId state spec
    | VisKind.Map spec -> renderMap ctx parentNodeId state spec

and private renderGrid
    (ctx: RenderContext<'Msg>)
    (parentNodeId: string)
    (state: StateBehaviour<'Msg>)
    (spec: GridSpec<'Msg>)
    : ReactElement =
    // Adapter first — when the consumer wires an AG-Grid-shaped
    // IVisualisationAdapter, defer to it and let it handle row-data
    // resolution / state-slot dispatch / event mapping. Falling through
    // to the simple-table fallback covers the standalone-posture path
    // (no third-party grid dep required).
    let visCtx: VisAdapter.VisualisationContext<'Msg> =
        { Sources = ctx.Sources
          State = state
          RecurseRender = render ctx
          RunAction = runAction ctx }

    match ctx.VisAdapter.RenderGrid(spec, visCtx) with
    | Some rendered -> rendered
    | None ->

        let resolution = BindingResolver.resolve<obj seq> ctx.Sources spec.Source

        match resolution, state.OnLoading, state.OnError with
        | BindingResolver.NotResolved, Some loadingNode, _ -> render ctx loadingNode
        | BindingResolver.Errored msg, _, Some errorFn ->
            render
                ctx
                (errorFn
                    { Kind = ErrorKind.BindingResolution
                      Message = msg
                      CorrelationId = correlationId parentNodeId })
        | _ ->

            let rows =
                match resolution with
                | BindingResolver.Resolved seq -> Seq.toList seq
                | _ -> []

            match rows, state.OnEmpty with
            | [], Some emptyNode -> render ctx emptyNode
            | _ ->

                // Phase 427 — the default row-click write (the 423/426 archetype for the
                // Selection channel): a data-bearing grid whose `OnRowClick` is `None` writes the
                // clicked row to the reactive `SelectionStore` under its own `NodeId`, so every
                // `Binding.Selection` reader of this grid re-renders with the row — decoded
                // master-detail with zero host code. A `Some` closure dispatches exactly as
                // before and never touches the store (closure wins).
                let rowKeyOf: (obj -> string) option =
                    match spec.RowKey, spec.RowKeyField with
                    | Some key, _ -> Some key
                    | None, Some field -> Some(fun row -> BindingResolver.projectRowFieldString row field)
                    | None, None -> None

                // Selected-row visual state: compare the current selection (already merged from
                // the live store by the reactive host) against each row by stable row key. With
                // no key contract there is no reliable identity — no visual state (the Phase 425
                // unstable-key validator advice applies). An empty key, or the decoded `RowKey`
                // placeholder (a `"<closure>"`-constant closure — every row would "match"), is
                // no identity either.
                let usableKey (k: string) = k <> "" && k <> "<closure>"

                let selectedKey: string option =
                    match rowKeyOf with
                    | Some keyOf ->
                        Map.tryFind (NodeId parentNodeId) ctx.Sources.Selections
                        |> Option.map keyOf
                        |> Option.filter usableKey
                    | None -> None

                // Phase 663 — the grid write-back floor (the Phase 426 control default replayed
                // for the grid): `Editable = true` over a DIRECT `Binding.State` source commits
                // an edited cell as the WHOLE updated rows value to the state key, so every
                // other reader of that key (a Chart sourced on the same `$state` entry)
                // re-renders with the edit. Any other source shape has no writable slot — a
                // Transform pipeline is not invertible, Static/Query rows are host data — so
                // the grid stays display-only (FUARAN090 warns pre-emit).
                let editCommit: (int -> string -> obj -> unit) option =
                    match spec.Editable, spec.Source with
                    | true, Binding.State _ ->
                        Some(fun rowIndex field newValue ->
                            let newRows =
                                rows
                                |> List.mapi (fun i row ->
                                    if i = rowIndex then
                                        updateRowField row field newValue
                                    else
                                        row)

                            writeBackTo ctx spec.Source (Some(box (Seq.ofList newRows))))
                    | _ -> None

                Html.table
                    [ prop.className "fuaran-grid"
                      prop.children
                          [ Html.thead
                                [ Html.tr
                                      [ prop.children
                                            [ for col in spec.Columns ->
                                                  Html.th [ prop.className "fuaran-grid-header"; prop.text col.Label ] ] ] ]
                            Html.tbody
                                [ prop.children
                                      [ for (rowIndex, row) in List.indexed rows ->
                                            let isSelected =
                                                match selectedKey, rowKeyOf with
                                                | Some sel, Some keyOf -> keyOf row = sel
                                                | _ -> false

                                            Html.tr
                                                [ prop.className (
                                                      if isSelected then
                                                          "fuaran-grid-row fuaran-grid-row-selected"
                                                      else
                                                          "fuaran-grid-row"
                                                  )
                                                  prop.onClick (fun _ ->
                                                      match spec.OnRowClick with
                                                      | Some f -> runAction ctx (f row)
                                                      | None -> SelectionStore.set parentNodeId (box row))
                                                  prop.children
                                                      [ for col in spec.Columns ->
                                                            // Editable write-back applies only on the declarative
                                                            // path — a Field-projected Text/Numeric cell with no
                                                            // Value closure (a closure's projection need not
                                                            // correspond to any row field, so there is nothing
                                                            // sound to write). Date and the interactive cell
                                                            // kinds keep their existing behaviour.
                                                            let commit: (CellValue -> unit) option =
                                                                match editCommit, col.Value, col.Field, col.Kind with
                                                                | Some ec,
                                                                  None,
                                                                  Some field,
                                                                  (CellKindErased.Text | CellKindErased.Numeric) ->
                                                                    Some(fun cv ->
                                                                        match cv with
                                                                        | CellValue.Numeric f ->
                                                                            ec rowIndex field (box f)
                                                                        | CellValue.Text s -> ec rowIndex field (box s)
                                                                        | _ -> ())
                                                                | _ -> None

                                                            Html.td
                                                                [ prop.className "fuaran-grid-cell"
                                                                  prop.children [ renderGridCell ctx commit col row ] ] ] ] ] ] ] ]

and private renderGridCell
    (ctx: RenderContext<'Msg>)
    (commit: (CellValue -> unit) option)
    (col: ColumnErased<'Msg>)
    (row: obj)
    : ReactElement =
    // Phase 425 — the closure wins when present; else the declarative `Field` projects the row
    // property; else the cell is empty. A decoded grid renders data from `Field` with zero host code.
    let value =
        match col.Value with
        | Some accessor -> accessor row
        | None ->
            match col.Field with
            | Some field -> BindingResolver.projectRowFieldValue row field
            | None -> CellValue.Empty
    // Cell rendering is largely text-based for the non-interactive Kinds
    // (Text / Numeric / Date) and delegates to a typed renderer for the
    // interactive ones. The simple-table fallback handles Text/Numeric/
    // Date plus Editable + Checkbox + Button + Link + Pill + Progress
    // here; AG Grid adapter handles the full set with native AG Grid
    // cell renderers.
    match col.Kind with
    | CellKindErased.Text
    | CellKindErased.Numeric
    | CellKindErased.Date ->
        // Phase 663 — a `commit` (the grid-level State write-back, threaded from `renderGrid`
        // only for Field-projected Text/Numeric cells on an editable State-sourced grid) turns
        // the display cell into the same input shapes as `CellKindErased.Editable`, committing
        // the RAW value (never the formatted rendering). Absent `commit`, the display cell is
        // byte-identical to the pre-663 span.
        match commit with
        | Some commitCell ->
            match col.Kind, value with
            | CellKindErased.Numeric, CellValue.Numeric n ->
                Html.input
                    [ prop.className "fuaran-grid-cell-editable"
                      prop.type'.number
                      prop.value n
                      prop.onChange (fun (v: float) ->
                          // An empty / mid-edit number input parses NaN — never commit it
                          // (a NaN cell would silently flatten every chart on the key).
                          if not (System.Double.IsNaN v) then
                              commitCell (CellValue.Numeric v)) ]
            | CellKindErased.Numeric, _ ->
                // An Empty (or non-numeric) cell in a Numeric column: text input, committed
                // only when the entry parses numerically.
                Html.input
                    [ prop.className "fuaran-grid-cell-editable"
                      prop.type'.text
                      prop.value (renderCellValue CellFormat.None value)
                      prop.onChange (fun (v: string) ->
                          match System.Double.TryParse v with
                          | true, f -> commitCell (CellValue.Numeric f)
                          | false, _ -> ()) ]
            | _, _ ->
                let current =
                    match value with
                    | CellValue.Text s -> s
                    | other -> renderCellValue CellFormat.None other

                Html.input
                    [ prop.className "fuaran-grid-cell-editable"
                      prop.type'.text
                      prop.value current
                      prop.onChange (fun (v: string) -> commitCell (CellValue.Text v)) ]
        | None -> Html.span [ prop.text (renderCellValue col.Format value) ]
    | CellKindErased.Editable onEdit ->
        match value with
        | CellValue.Numeric n ->
            Html.input
                [ prop.className "fuaran-grid-cell-editable"
                  prop.type'.number
                  prop.value n
                  prop.onChange (fun (v: float) -> runAction ctx (onEdit (row, CellValue.Numeric v))) ]
        | CellValue.Text s ->
            Html.input
                [ prop.className "fuaran-grid-cell-editable"
                  prop.type'.text
                  prop.value s
                  prop.onChange (fun (v: string) -> runAction ctx (onEdit (row, CellValue.Text v))) ]
        | _ -> Html.span [ prop.text (renderCellValue col.Format value) ]
    | CellKindErased.Checkbox(getValue, onToggle) ->
        let current = getValue row

        Html.input
            [ prop.type'.checkbox
              prop.isChecked current
              prop.onChange (fun (b: bool) -> runAction ctx (onToggle (row, b))) ]
    | CellKindErased.Button(label, onClick) ->
        Html.button
            [ prop.className "fuaran-grid-cell-button"
              prop.text (renderText ctx label)
              prop.onClick (fun e ->
                  e.stopPropagation () // don't trigger row-click handler
                  runAction ctx (onClick row)) ]
    | CellKindErased.ButtonGroup buttons ->
        Html.span
            [ prop.className "fuaran-grid-cell-button-group"
              prop.children
                  [ for (label, onClick) in buttons ->
                        Html.button
                            [ prop.className "fuaran-grid-cell-button"
                              prop.text (renderText ctx label)
                              prop.onClick (fun e ->
                                  e.stopPropagation ()
                                  runAction ctx (onClick row)) ] ] ]
    | CellKindErased.Link(href, label) ->
        // Pass through `Sanitize.sanitizeUrlOrBlank` so a
        // `javascript:` / `vbscript:` / unknown-scheme href emitted from
        // an AI-supplied row accessor renders as `about:blank` instead
        // of executing. Same-origin relative paths and the http/https/
        // mailto/tel allowlist pass through unchanged.
        Html.a
            [ prop.className "fuaran-grid-cell-link"
              prop.href (Sanitize.sanitizeUrlOrBlank (href row))
              prop.text (renderText ctx (label row)) ]
    | CellKindErased.Pill(label, tone) ->
        Html.span
            [ prop.className (sprintf "fuaran-grid-cell-pill fuaran-pill-%s" (Theme.toneVar (tone row)))
              prop.text (renderText ctx (label row)) ]
    | CellKindErased.Progress(fraction, label) ->
        let f = fraction row

        Html.div
            [ prop.className "fuaran-grid-cell-progress"
              prop.children
                  [ Html.div
                        [ prop.className "fuaran-grid-cell-progress-fill"
                          prop.style [ style.width (length.percent (f * 100.0)) ] ]
                    match label with
                    | Some l -> Html.span [ prop.text (renderText ctx (l row)) ]
                    | None -> Html.none ] ]
    | CellKindErased.Custom render' ->
        // Custom cell renderers nest a full Node<'Msg> inside the cell.
        let nestedNode = render' Runtime.JsonBridge.jsToJVal

        render ctx nestedNode

and private renderChart
    (ctx: RenderContext<'Msg>)
    (parentNodeId: string)
    (state: StateBehaviour<'Msg>)
    (spec: ChartSpec<'Msg>)
    : ReactElement =
    // Adapter first — when wired (e.g. AG Charts via Fuaran's in-tree
    // adapter or a platform adapter), defer to it. Falling through gives
    // a labelled placeholder + row count so the demo at least shows
    // the chart slot is reaching live data.
    let visCtx: VisAdapter.VisualisationContext<'Msg> =
        { Sources = ctx.Sources
          State = state
          RecurseRender = render ctx
          RunAction = runAction ctx }

    match ctx.VisAdapter.RenderChart(spec, visCtx) with
    | Some rendered -> rendered
    | None ->

        let resolution = BindingResolver.resolve<obj seq> ctx.Sources spec.Source

        match resolution, state.OnLoading, state.OnError with
        | BindingResolver.NotResolved, Some loadingNode, _ -> render ctx loadingNode
        | BindingResolver.Errored msg, _, Some errorFn ->
            render
                ctx
                (errorFn
                    { Kind = ErrorKind.BindingResolution
                      Message = msg
                      CorrelationId = correlationId parentNodeId })
        | _ ->
            match resolution, spec.Kind with
            | BindingResolver.Resolved rows, kind when Fuaran.UI.Charts.isLowered kind ->
                // Phase 526 — first-party render: lower the semantic Chart to a
                // Drawing and emit it as inline SVG (Phase 525). No third-party
                // charting dependency; renders identically SSR + CSR (the same
                // lowering + Drawing builder). The lowered-kind set is
                // `Charts.isLowered` — the single source of truth (636/637/638
                // arms included), so this branch never drifts from the engine.
                Html.div
                    [ prop.dangerouslySetInnerHTML (
                          DrawingSvg.render ctx.Sources (renderText ctx) (Fuaran.UI.Charts.lower spec rows)
                      ) ]
            | _ ->
                // Unresolved data, or a chart kind whose lowering rule has not
                // shipped yet (Heatmap): a labelled placeholder + row count so
                // the chart slot stays visible.
                let rowCount =
                    match resolution with
                    | BindingResolver.Resolved seq -> Seq.length seq
                    | _ -> 0

                Html.div
                    [ prop.className "fuaran-chart"
                      prop.children
                          [ match spec.Title with
                            | Some title ->
                                Html.div [ prop.className "fuaran-chart-title"; prop.text (renderText ctx title) ]
                            | None -> Html.none
                            Html.div
                                [ prop.className "fuaran-chart-placeholder"
                                  prop.text (
                                      sprintf
                                          "[Chart placeholder: %A — %d rows × {%s} → {%s}. Wire AgChartAdapter for live rendering.]"
                                          spec.Kind
                                          rowCount
                                          spec.XField
                                          (String.concat ", " spec.YFields)
                                  ) ] ] ]

and private renderTable (ctx: RenderContext<'Msg>) (spec: TableSpec<'Msg>) : ReactElement =
    let headerCells =
        [ for h in spec.Headers -> Html.th [ prop.className "fuaran-table-header"; prop.text (renderText ctx h) ] ]

    let bodyRows =
        [ for (i, row) in List.indexed spec.Rows ->
              Html.tr
                  [ prop.className "fuaran-table-row"
                    if spec.OnRowClick.IsSome then
                        prop.onClick (fun _ ->
                            match spec.OnRowClick with
                            | Some f -> runAction ctx (f i)
                            | None -> ())
                    prop.children
                        [ for cell in row ->
                              Html.td [ prop.className "fuaran-table-cell"; prop.text (renderText ctx cell) ] ] ] ]

    Html.table
        [ prop.className "fuaran-table"
          prop.children
              [ Html.thead [ Html.tr [ prop.children headerCells ] ]
                Html.tbody [ prop.children bodyRows ] ] ]

and private renderMap
    (ctx: RenderContext<'Msg>)
    (parentNodeId: string)
    (state: StateBehaviour<'Msg>)
    (spec: MapSpec<'Msg>)
    : ReactElement =
    // Session 3b: labelled placeholder + the marker count so the demo
    // shows the slot is reaching live data. Real Leaflet integration
    // requires a host-provided npm dep — out of Fuaran's standalone-posture
    // scope, so it's adapter-shaped like AG Charts.
    let resolution = BindingResolver.resolve<MapMarker seq> ctx.Sources spec.Source

    match resolution, state.OnLoading, state.OnError with
    | BindingResolver.NotResolved, Some loadingNode, _ -> render ctx loadingNode
    | BindingResolver.Errored msg, _, Some errorFn ->
        render
            ctx
            (errorFn
                { Kind = ErrorKind.BindingResolution
                  Message = msg
                  CorrelationId = correlationId parentNodeId })
    | _ ->
        let markers =
            match resolution with
            | BindingResolver.Resolved seq -> Seq.toList seq
            | _ -> []

        Html.div
            [ prop.className "fuaran-map"
              prop.children
                  [ Html.div
                        [ prop.className "fuaran-map-placeholder"
                          prop.text (
                              sprintf
                                  "[Map placeholder: %d markers around (%.4f, %.4f) zoom %d. Wire a Leaflet adapter for live rendering.]"
                                  markers.Length
                                  spec.CentreLatitude
                                  spec.CentreLongitude
                                  spec.Zoom
                          ) ]
                    if not markers.IsEmpty then
                        Html.ul
                            [ prop.className "fuaran-map-marker-list"
                              prop.children
                                  [ for marker in markers ->
                                        Html.li
                                            [ prop.className "fuaran-map-marker"
                                              prop.text (
                                                  sprintf
                                                      "%s @ (%.4f, %.4f)"
                                                      (renderText ctx marker.Label)
                                                      marker.Latitude
                                                      marker.Longitude
                                              )
                                              if spec.OnMarkerClick.IsSome then
                                                  prop.onClick (fun _ ->
                                                      match spec.OnMarkerClick with
                                                      | Some f -> runAction ctx (f marker)
                                                      | None -> ()) ] ] ] ] ]

// ─── Formatters ────────────────────────────────────────────────────────────

and private formatNumber (format: CellFormat) (value: float) : string =
    match format with
    | CellFormat.None -> string value
    // Precision-from-arg sprintf ("%.*f", "%.*g") is unsupported by Fable's
    // printf — its fsFormat regex only accepts literal digits in the precision
    // group, so e.g. printf "%.*f" falls into the no-match branch and returns
    // the format string itself (not a curried printer), tripping
    // "toText(...) is not a function" at the call site. ToString("F<n>") /
    // ToString("G<n>") round-trip cleanly through both .NET and Fable.
    | CellFormat.Number(Some decimals) -> value.ToString("F" + string decimals)
    | CellFormat.Number None ->
        // Fable-compatible "is whole?" check — System.Double.IsInteger is not
        // in Fable's supported surface, so test via floor instead. No IsNaN
        // guard needed: NaN ≠ NaN by IEEE 754, so `NaN = floor NaN` is false.
        if value = floor value then
            sprintf "%.0f" value
        else
            sprintf "%g" value
    | CellFormat.Currency code -> sprintf "%s %.2f" code value
    | CellFormat.Percent(Some decimals) -> (value * 100.0).ToString("F" + string decimals) + "%"
    | CellFormat.Percent None -> sprintf "%.1f%%" (value * 100.0)
    | CellFormat.SignificantDigits digits -> value.ToString("G" + string digits)
    | CellFormat.Date _ -> string value
    | CellFormat.Custom f -> f (CellValue.Numeric value)

and private renderCellValue (format: CellFormat) (value: CellValue) : string =
    match format with
    | CellFormat.Custom f -> f value
    | _ ->
        match value with
        | CellValue.Numeric n -> formatNumber format n
        | CellValue.Text s -> s
        | CellValue.Bool b -> if b then "true" else "false"
        | CellValue.Date d ->
            match format with
            | CellFormat.Date fmt -> d.ToString(fmt)
            | _ -> d.ToString("yyyy-MM-dd")
        | CellValue.Empty -> ""

and private badgeVariantClass (variant: BadgeVariant) : string =
    match variant with
    | BadgeVariant.Neutral -> "neutral"
    | BadgeVariant.Brand -> "brand"
    | BadgeVariant.Success -> "success"
    | BadgeVariant.Warning -> "warning"
    | BadgeVariant.Critical -> "critical"
    | BadgeVariant.Info -> "info"

and private buttonVariantClass (variant: ButtonVariant) : string =
    match variant with
    | ButtonVariant.Primary -> "primary"
    | ButtonVariant.Secondary -> "secondary"
    | ButtonVariant.Tertiary -> "tertiary"
    | ButtonVariant.Destructive -> "destructive"

// ─── Public entry point ────────────────────────────────────────────────────

/// Render a Fuaran `Node<'Msg>` to a Feliz `ReactElement` against an
/// explicit context (sources + runtime + dispatch).
and render (ctx: RenderContext<'Msg>) (node: Node<'Msg>) : ReactElement =
    let id =
        match node.Id with
        | NodeId s -> s

    // Project Node.Accessibility into HTML aria-* / role
    // attributes via the shared `accessibilityAttributes` helper (testable
    // in isolation). `prop.custom` emits the kebab-case attribute name
    // verbatim — React 16+ accepts `aria-*` props this way without
    // case-conversion gymnastics.
    let a11yProps: IReactProperty list =
        accessibilityAttributes ctx.Sources node.Accessibility
        |> List.map (fun (k, v) -> prop.custom (k, v))

    // Append the motion-token class to the outer wrapper
    // when `Node.Motion` is `Some token`. Defaults to no class (the
    // original shape) when `None`. The reference CSS supplies four
    // keyframe rules; the remaining four tokens are no-op class hooks.
    let baseClassName = Theme.nodeClassName node.Kind node.Style

    let className =
        match node.Motion with
        // Per-node hot path — string concat, not sprintf. Do not "simplify".
        | Some motion -> baseClassName + " fuaran-motion-" + Theme.motionVar motion
        | None -> baseClassName

    // Emit consumer-side `data-*` / `aria-*` test
    // hooks from `Node.ExtraAttributes`. A render-time
    // floor: even if a hand-built record-with bypasses the smart-ctor's
    // prefix gate, `Sanitize.sanitizeExtraAttributes` filters the map
    // down to the entries that pass the data-*/aria-* allowlist + value
    // safety check before emission. Iteration order is the Map's natural
    // ordering (key-sorted) so the emitted attribute list is deterministic
    // across re-renders.
    let extraAttrs: IReactProperty list =
        match node.ExtraAttributes with
        | Some attrs ->
            attrs
            |> Sanitize.sanitizeExtraAttributes
            |> Map.toList
            |> List.map (fun (k, v) -> prop.custom (k, v))
        | None -> []

    // Per-node render guard. A throwing leaf-body renderer
    // (binding accessor crash, malformed spec, etc.) is caught here so
    // sibling nodes stay live — the outer wrapper still emits with its
    // `data-fuaran-node-id` so layout observers / op-stream replay / the
    // AiTools introspection surface keep addressing the node. The
    // fallback child carries `data-fuaran-render-failed` so consumer-side
    // CSS / dev tooling can pick it out.
    //
    // The guard suspends itself when `ctx.InErrorBoundary = true`: a
    // surrounding `NodeKind.ErrorBoundary` opted into "I want the
    // FALLBACK subtree, not a placeholder where the bad leaf was", so
    // throws must propagate up to its catch. The when-clause filter is
    // load-bearing here — without it the per-node guard would absorb
    // throws before the boundary saw them.
    let kindBody: ReactElement =
        try
            renderKind ctx id node.State node.Kind
        with ex when not ctx.InErrorBoundary ->
            let corrId =
                emitRenderFailure
                    ctx.TelemetrySink
                    id
                    (nodeKindName node.Kind)
                    ex.Message
                    RenderFailureSource.PerNodeGuard

            renderNodeFallback id (nodeKindName node.Kind) ex.Message corrId

    // Per-node wrapper props. `data-fuaran-node-id` is the addressable-element
    // marker the browser LayoutObserver's MutationObserver scans for (binding a
    // ResizeObserver on mount); it is independent of the HTML `id` so id-handling
    // changes don't break layout addressing.
    //
    // The common case — no a11y, no extras — is a 4-element literal (no append,
    // no ResizeArray). Only when a11y or extras are present do we build via a
    // ResizeArray, replacing the old 4-way `List.append` that copied the base
    // list 2-3x per node per frame even when both were empty. Order is
    // load-bearing (id, node-id, class, a11y, extras, children) and identical in
    // both branches. Perf primitive: do not "simplify" back to a `@` chain.
    let wrapperProps: IReactProperty list =
        match a11yProps, extraAttrs with
        | [], [] ->
            [ prop.id id
              prop.custom ("data-fuaran-node-id", id)
              prop.className className
              prop.children [ kindBody ] ]
        | _ ->
            let props =
                ResizeArray<IReactProperty>(4 + List.length a11yProps + List.length extraAttrs)

            props.Add(prop.id id)
            props.Add(prop.custom ("data-fuaran-node-id", id))
            props.Add(prop.className className)
            props.AddRange a11yProps
            props.AddRange extraAttrs
            props.Add(prop.children [ kindBody ])
            List.ofSeq props

    Html.div wrapperProps

// Wire the late-bound guest renderer (Phase 266): now that the recursive `render`
// group is fully defined, bind the hook to `render` instantiated at `obj`. This
// runs once at module initialisation, before any render is invoked, so the
// `Mount` arm always sees `Some`. The assignment lives OUTSIDE the recursive
// group, so instantiating `render` at `obj` here is not polymorphic recursion.
renderGuestHook <- Some render

/// Convenience entry point that constructs the `RenderContext` for callers
/// that only need the default `DiagnosticRuntime` + the no-op visualisation
/// adapter. Identical signature to the session-3a `render` so existing
/// in-tree callers continue to compile — the runtime and the adapter are
/// supplied implicitly.
let renderWithSources
    (sources: BindingResolver.BindingSources)
    (dispatch: 'Msg -> unit)
    (node: Node<'Msg>)
    : ReactElement =
    render
        { Sources = sources
          Runtime = Runtime.diagnostic
          VisAdapter = VisAdapter.noOp<'Msg>
          Dispatch = dispatch
          TelemetrySink = None
          InErrorBoundary = false
          // One-shot pre-render fragment registry walk.
          // Empty for trees that don't declare fragments — zero-cost for the
          // common case.
          Fragments = collectFragments Map.empty node
          ExpandingFragments = Set.empty
          Scope = None }
        node

/// Convenience entry point that pre-wires the optional
/// `IFuaranTelemetrySink` so per-node-guard catches and `ErrorBoundary`
/// catches surface to the host's observability backend. Hosts that
/// implement `IFuaranTelemetrySink` against a host platform's telemetry
/// implementation (or a Prometheus / OTel exporter, or `Fuaran.UI.Telemetry.Default.ConsoleSink`
/// for dev) pass it here; the renderer threads it through the
/// `RenderContext` without further wiring.
let renderWithSourcesAndSink
    (sources: BindingResolver.BindingSources)
    (runtime: Runtime.IFuaranRuntime)
    (telemetrySink: IFuaranTelemetrySink)
    (dispatch: 'Msg -> unit)
    (node: Node<'Msg>)
    : ReactElement =
    render
        { Sources = sources
          Runtime = runtime
          VisAdapter = VisAdapter.noOp<'Msg>
          Dispatch = dispatch
          TelemetrySink = Some telemetrySink
          InErrorBoundary = false
          Fragments = collectFragments Map.empty node
          ExpandingFragments = Set.empty
          Scope = None }
        node

/// Scope-aware render entry (Phase 266, §4o). Renders `node` under an explicit
/// runtime `scopeId`: `Binding.State` reads resolve against
/// `StateStore.forScope scopeId` (its snapshot merged over `sources.State`, scoped
/// values winning) and `Action.SetState` writes route to that isolated store, so
/// a whole tree — or a `Mount` guest the orchestration tier renders directly —
/// keeps its state off the process-global default store. `scopeId = None` is
/// exactly `renderWithSources` (byte-identical; the default store). The
/// orchestration tier maps its `FuaranRuntimeScope` to the `scopeId` string.
let renderWithSourcesInScope
    (scopeId: string)
    (sources: BindingResolver.BindingSources)
    (runtime: Runtime.IFuaranRuntime)
    (dispatch: 'Msg -> unit)
    (node: Node<'Msg>)
    : ReactElement =
    let scopedState =
        (StateStore.forScope scopeId).Snapshot()
        |> Map.fold (fun acc k v -> Map.add k v acc) sources.State

    render
        { Sources = { sources with State = scopedState }
          Runtime = runtime
          VisAdapter = VisAdapter.noOp<'Msg>
          Dispatch = dispatch
          TelemetrySink = None
          InErrorBoundary = false
          Fragments = collectFragments Map.empty node
          ExpandingFragments = Set.empty
          Scope = Some scopeId }
        node

// ─── State-reactive render (Phase 106) ─────────────────────────────────────
//
// `renderStateReactive` is the GP-13 opt-in companion to `renderWithSources`.
// It collects the `Binding.State` keys the tree reads and subscribes the
// rendered surface to them via `StateStore.subscribeKeys`, so a single global
// `Action.SetState` re-renders EVERY visible reader of that key — not just the
// control that owns the value. Callers that don't opt in keep calling
// `renderWithSources` and see byte-identical legacy behaviour (each surface
// refreshes on its own next tick, the pre-Phase-106 model).
//
// Consumer-adoption note: a data-heavy app with a global toggle
// (Cash/Real terms, theme, locale) lifted into chrome gets instant whole-
// module refresh for free by swapping `renderWithSources` →
// `renderStateReactive` at the surface root. No per-component subscription
// glue — the reactivity is derived from the tree's own `Binding.State` reads.
//
// Cross-pipeline (FGP 4): the React subscription path is browser-only and
// guarded by `#if FABLE_COMPILER`. On .NET (Expecto / SSR) the entry point
// renders once against the live snapshot — reactivity is a browser concern,
// and the .NET test runner pins the pure pieces (`collectStateKeys` +
// `StateStore.subscribeKeys`) directly.

/// Merge the live `StateStore` snapshot over `sources.State` (store wins) so a
/// re-render reads the current value for every `Binding.State`. Idempotent if
/// the host already merged the snapshot.
let private withLiveState (sources: BindingResolver.BindingSources) : BindingResolver.BindingSources =
    let stateSnap = StateStore.snapshot ()

    let withState =
        if Map.isEmpty stateSnap then
            sources
        else
            { sources with
                State = stateSnap |> Map.fold (fun acc k v -> Map.add k v acc) sources.State }

    // Filter twin (Phase 423): merge the live `FilterStore` snapshot over `sources.Filters` (store
    // wins) so a decoded chip's `$filters.<name>` write flows to every `Binding.Filter` reader.
    let filterSnap = FilterStore.snapshot ()

    let withFilters =
        if Map.isEmpty filterSnap then
            withState
        else
            { withState with
                Filters = filterSnap |> Map.fold (fun acc k v -> Map.add k v acc) withState.Filters }

    // Selection twin (Phase 427): merge the live `SelectionStore` snapshot over
    // `sources.Selections` (store wins; raw node-id strings re-wrap as `NodeId`) so a default
    // row-click write flows to every `Binding.Selection` reader.
    let selectionSnap = SelectionStore.snapshot ()

    let withSelections =
        if Map.isEmpty selectionSnap then
            withFilters
        else
            { withFilters with
                Selections =
                    selectionSnap
                    |> Map.fold (fun acc k v -> Map.add (NodeId k) v acc) withFilters.Selections }

    // Query twin (Phase 428): merge the live `QueryStore` snapshot (declarative `Call … into
    // Query` results) over `sources.QueryResults` (store wins) so a written slot flows to every
    // `Binding.Query` reader.
    let querySnap = QueryStore.snapshot ()

    if Map.isEmpty querySnap then
        withSelections
    else
        { withSelections with
            QueryResults =
                querySnap
                |> Map.fold (fun acc k v -> Map.add k v acc) withSelections.QueryResults }

#if FABLE_COMPILER
open Fable.Core // `jsNative` for the createElement import below

[<Import("createElement", "react")>]
let private reactCreateElement (componentFn: obj) (props: obj) : ReactElement = jsNative

// Stable module-level function component (identity preserved across renders,
// so it never remounts). `useStateKeys` subscribes the surface to its state
// keys and bumps a re-render tick on change. `withLiveState` is re-applied
// HERE, inside the component, so each re-render re-reads the current snapshot
// — merging it once before the prop is passed would freeze a stale snapshot
// into every re-render.
let private stateReactiveComponent
    (props:
        {| sources: BindingResolver.BindingSources
           dispatch: 'Msg -> unit
           node: Node<'Msg>
           keys: Set<string>
           filterKeys: Set<string>
           selectionKeys: Set<string>
           queryKeys: Set<string> |})
    : ReactElement =
    StateStore.useStateKeys props.keys |> ignore
    // Filter twin (Phase 423): subscribe the surface to its `Binding.Filter` names too, so a chip
    // write (`FilterStore.set`) re-renders every reader alongside the State channel.
    FilterStore.useFilterKeys props.filterKeys |> ignore
    // Selection twin (Phase 427): subscribe to the `Binding.Selection` producer ids too, so a
    // default row-click write (`SelectionStore.set`) re-renders every detail reader.
    SelectionStore.useSelectionKeys props.selectionKeys |> ignore
    // Query twin (Phase 428): subscribe to the `Binding.Query` names too, so a declarative
    // `Call … into Query <name>` write (`QueryStore.set`) re-renders every reader of that slot.
    QueryStore.useQueryKeys props.queryKeys |> ignore
    renderWithSources (withLiveState props.sources) props.dispatch props.node
#endif

/// GP-13 opt-in entry point: render `node`, subscribing the surface to every
/// `Binding.State` key it reads so a global `Action.SetState` re-renders the
/// whole surface. Default callers keep `renderWithSources` (no behaviour
/// change). See the section header for the adoption + cross-pipeline notes.
let renderStateReactive
    (sources: BindingResolver.BindingSources)
    (dispatch: 'Msg -> unit)
    (node: Node<'Msg>)
    : ReactElement =
#if FABLE_COMPILER
    let keys = collectStateKeys node
    let filterKeys = collectFilterKeys node
    let selectionKeys = collectSelectionKeys node
    let queryKeys = collectQueryKeys node

    // Pass the ORIGINAL sources; the component re-merges the live snapshot on
    // every re-render (see `stateReactiveComponent`).
    reactCreateElement
        (box stateReactiveComponent)
        (box
            {| sources = sources
               dispatch = dispatch
               node = node
               keys = keys
               filterKeys = filterKeys
               selectionKeys = selectionKeys
               queryKeys = queryKeys |})
#else
    // .NET / SSR: no React runtime to subscribe to — render once with the live
    // snapshot merged. Reactivity is a browser concern.
    renderWithSources (withLiveState sources) dispatch node
#endif

// ─── Theme mounting ────────────────────────────────────────────────────────
//
// `Theme.styleElement` and `renderWithTheme` are the optional companion to
// the typed `Theme` record. Consumers used to ship `fuaran-reference.css`
// (or a hand-rolled bridge) as a separate stylesheet; the renderer didn't
// emit any styling layer. That path is unchanged for
// `renderWithSources` (no auto-mounted theme) and adds `renderWithTheme`
// as a strict superset: pass a `Theme`, get the rendered node PLUS a
// `<style>` element carrying the projected CSS-variable bundle.
//
// Apps that want the byte-for-byte equivalent of the reference CSS use
// `renderWithTheme Defaults.theme sources dispatch node`. Apps swapping
// themes at runtime re-render with the new theme — React diffs the
// `<style>` content and the variables re-bind without a full reflow.

/// Project a `Theme` to a Feliz `<style>` element carrying its
/// CSS-variable bundle. Mount alongside the rendered node tree to enable
/// the typed-theme path. See `renderWithTheme` for the wrapping entry
/// point.
let themeStyleElement (theme: Theme) : ReactElement =
    Html.style [ prop.dangerouslySetInnerHTML (Theme.toCss theme) ]

/// Convenience entry point that mounts a `Theme`'s CSS-variable bundle
/// alongside the rendered node tree. `Defaults.theme` mirrors the post-
/// `fuaran-reference.css` byte-for-byte, so apps that pass it see no
/// visual change. Apps composing their own Theme record get
/// it picked up at the renderer root without touching any stylesheet
/// file.
let renderWithTheme
    (theme: Theme)
    (sources: BindingResolver.BindingSources)
    (dispatch: 'Msg -> unit)
    (node: Node<'Msg>)
    : ReactElement =
    React.Fragment [ themeStyleElement theme; renderWithSources sources dispatch node ]
