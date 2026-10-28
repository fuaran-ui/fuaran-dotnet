module Fuaran.UI.Renderer.Render

// FS0067 ("this type test or downcast will always hold") fires under the Fable
// pipeline on the DOM downcasts this renderer needs for the .NET pipeline (e.g.
// `descendants[i] :?> Browser.Types.Element` in the Custom exposed-NodeIds
// walk) — Fable's DOM typings already give the narrowed type, so the cast is
// redundant *under Fable* but required *under .NET*. Silence the redundant-cast
// warning so the dual-pipeline code compiles cleanly on both (the .NET build
// keeps the cast; Fable elides it). Surfaced once the renderer's Fable graph
// stopped pulling Fuaran.UI.Ops (the Ops.Abstractions split). That split was a
// LAYERING decision — the renderer's graph carries the type contract, not the
// decode + apply engine, which belongs at the host seam — and NOT a portability
// one: Fuaran.UI.Ops has been Fable-portable since Phase 191
// (`docs/migrations/191-fable-portable-ops.md`). The decision stands; the reason
// is stated precisely here because the older wording was cited as evidence for a
// Fable blocker that does not exist.
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

open System.Collections.Generic
open Feliz
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Telemetry.Abstractions
// Phase 889 — the user-action record + its sink seam. Already in this project's
// Fable graph via Telemetry.Abstractions; referenced explicitly so the
// dependency is declared where it is used.
open Fuaran.UI.Ops.ActionInvocation

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
    // Phase 2041 — the projection moved to `RenderParity` so the server
    // renderer names a failing node exactly as this one does.
    RenderParity.nodeKindName kind

// ─── The opaque session-context keys (Phase 330) ───────────────────────────
//
// The renderer reads exactly two well-known keys out of
// `RenderContext.SessionContext` and stamps their values onto render-failure
// telemetry. Both are GENERIC names for generic notions: an id that ties this
// render to the interaction that caused it, and an id for whoever the host
// attributes it to. The renderer never interprets either value — it copies
// them — so a host is free to put whatever correlates its own system in them.
//
// They are literals rather than magic strings at the call site so a host and
// the renderer cannot disagree about spelling.

/// `SessionContext` key whose value is stamped onto
/// `RenderFailureTelemetry.PromptId` — the correlation token for the
/// interaction that produced this render.
[<Literal>]
let promptIdKey = "promptId"

/// `SessionContext` key whose value is stamped onto
/// `RenderFailureTelemetry.UserId`.
[<Literal>]
let userIdKey = "userId"

/// Emit one render-failure telemetry event through the optional sink, stamped
/// with the host's opaque correlation context.
///
/// Sink failures are swallowed (telemetry is best-effort by the
/// `IFuaranTelemetrySink` contract); a throwing sink does not poison
/// the render path. Pure helper so the call-sites stay readable.
/// Public so the .NET-side test runner can assert against the sink
/// without driving Feliz's React-substrate.
///
/// `sessionContext` is read for `promptIdKey` / `userIdKey` only; an empty map
/// reproduces the pre-330 behaviour exactly (both fields `None`). The returned
/// `CorrelationId` is unchanged — a content hash of node id + kind, which
/// disambiguates concurrent failures WITHIN one frame and is a different job
/// from the interaction id.
let emitRenderFailureWithContext
    (sink: IFuaranTelemetrySink option)
    (sessionContext: Map<string, string>)
    (nodeId: string)
    (kindName: string)
    (errorMessage: string)
    (source: RenderFailureSource)
    : string =
    let corrId = RenderParity.renderFailureCorrelationId nodeId kindName

    match sink with
    | Some s ->
        try
            s.RecordRenderFailure
                { NodeId = nodeId
                  NodeKindName = kindName
                  ErrorMessage = errorMessage
                  CaughtBy = source
                  CorrelationId = corrId
                  PromptId = Map.tryFind promptIdKey sessionContext
                  UserId = Map.tryFind userIdKey sessionContext
                  Timestamp = System.DateTimeOffset.UtcNow }
        with ex ->
            // The isolation is right: a telemetry sink must not be able to turn
            // a render failure into a render CRASH. But this is the emission of
            // a failure report, so swallowing it loses the one record anybody
            // was going to read — a sink that throws here produces a render that
            // failed and a telemetry stream that says it did not.
            Diagnostics.warn ("the telemetry sink threw while recording a render failure for '" + nodeId + "'") (box ex)
    | None -> ()

    corrId

/// `emitRenderFailureWithContext` with no correlation context — the pre-330
/// arity, kept so existing callers compile unchanged. Equivalent to passing
/// `Map.empty`.
let emitRenderFailure
    (sink: IFuaranTelemetrySink option)
    (nodeId: string)
    (kindName: string)
    (errorMessage: string)
    (source: RenderFailureSource)
    : string =
    emitRenderFailureWithContext sink Map.empty nodeId kindName errorMessage source

// ─── Render context — bundles renderer-wide dependencies ───────────────────
//
// Everything a recursive renderer call needs travels in one record, so the
// per-kind renderers' signatures stay readable and a new dependency extends
// the record rather than every match arm. `'Msg` flows through `Dispatch`;
// everything else is `'Msg`-agnostic. Build one with `RenderContext.create`
// and adjust it with `with`: a full record literal outside `create` is a
// construction site every new field would break. Each field's history (the
// phase that introduced it and the reasoning behind its default) is recorded
// in `STABILITY.md` under 0.94.0, Phase 2067.

type RenderContext<'Msg> =
    {
        /// The read-only data every `Binding` resolves against.
        Sources: BindingResolver.BindingSources
        /// The host substrate every effecting `Action` runs through, including
        /// the `CanDispatch` gate, the custom-renderer registry and the
        /// `TryLoadGuest` mount seam.
        Runtime: Runtime.IFuaranRuntime
        /// The adapter that renders client-library visualisations.
        VisAdapter: VisAdapter.IVisualisationAdapter<'Msg>
        /// Where a dispatched `'Msg` goes.
        Dispatch: 'Msg -> unit
        /// Where render failures are reported (every per-node-guard catch and
        /// every `ErrorBoundary` catch), or `None` to swallow them into the
        /// fallback placeholder.
        TelemetrySink: IFuaranTelemetrySink option
        /// True under an active `ErrorBoundary`'s child subtree, where the
        /// per-node guard is suspended so a throw reaches the boundary.
        InErrorBoundary: bool
        /// The fragment registry `collectFragments` gathered from the tree in
        /// one pre-render walk; the last declaration wins on a name collision.
        Fragments: Map<FragmentId, Node<'Msg>>
        /// The fragment ids being expanded on this path, so a cycle renders a
        /// labelled placeholder instead of recursing.
        ExpandingFragments: Set<FragmentId>
        /// The host's opaque correlation map, read only for `promptIdKey` and
        /// `userIdKey`; never hashed and never on the wire.
        SessionContext: Map<string, string>
        /// The user-action record sink, or `None` to record nothing; the sink
        /// declares its own `CaptureMode`.
        ActionSink: IActionInvocationSink option
        /// The id of the node being rendered, set only when `ActionSink` is
        /// wired, so a recorded gesture names the node it came from.
        CurrentNodeId: string option
        /// The host's large-binary upload sink and destination registry, or
        /// `None` to perform no upload at all.
        UploadSink: Fuaran.UI.Ops.UploadSink.IFuaranUploadSink option
        /// What this render may do: its egress, `Custom` hash floor, CSP
        /// posture and scope, shared in shape with the server context.
        Policy: RenderPolicy
    }

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

// ─── The plan emitter (Phase 2108) ──────────────────────────────────────────

/// This renderer's emitter for `ElementPlan` — the one place the client turns a
/// planned element into a Feliz element. `class` becomes React's `className`;
/// every other attribute lands under its document name; inline style
/// declarations arrive in canonical CSS spelling and are camel-cased for
/// React's style object; a reconciliation key is React's and is emitted;
/// pre-built markup passes through Trusted Types. The server renderer's
/// `Emit` is the same mapping onto Feliz.ViewEngine.
[<RequireQualifiedAccess>]
module private Emit =
    let private attr (name: string) (value: string) : IReactProperty =
        match name with
        | "class" -> prop.className value
        // React's spelling; the plan carries the document's (either case).
        | "tabindex"
        | "tabIndex" -> prop.tabIndex (int value)
        | "srcset" -> prop.custom ("srcSet", value)
        // Phase 2290 — the form-control attributes, in React's spelling.
        | "for" -> prop.htmlFor value
        | "minlength" -> prop.minLength (int value)
        | "maxlength" -> prop.maxLength (int value)
        | "rows" -> prop.rows (int value)
        | _ -> prop.custom (name, value)

    /// A present boolean attribute as React's boolean property — never as an
    /// empty-string attribute, which React reads as FALSE and drops.
    let private flag (name: string) : IReactProperty =
        match name with
        | "disabled" -> prop.disabled true
        | "hidden" -> prop.hidden true
        | "open" -> prop.isOpen true
        | "controls" -> prop.controls true
        | "loop" -> prop.loop true
        | "autoplay" -> prop.autoPlay true
        | "muted" -> prop.muted true
        | "required" -> prop.required true
        | "multiple" -> prop.multiple true
        | _ -> prop.custom (name, true)

    /// Attribute pairs as properties.
    let attrs (pairs: (string * string) list) : IReactProperty list =
        pairs |> List.map (fun (k, v) -> attr k v)

    /// The handler properties an arm binds to a named attachment point
    /// (`ElementPlan.Attach`) and its key.
    type Attach = string -> string -> IReactProperty list

    let private property (attach: Attach) (p: ElementPlan.Prop) : IReactProperty list =
        match p with
        | ElementPlan.Prop.Attr(name, value) -> [ attr name value ]
        | ElementPlan.Prop.Style declarations ->
            [ prop.style (
                  declarations
                  |> List.map (fun (k, v) -> style.custom (ElementPlan.camelCaseProperty k, v))
              ) ]
        | ElementPlan.Prop.Flag name -> [ flag name ]
        | ElementPlan.Prop.Behaviour(point, key) -> attach point key
        // Phase 2290 — controlled values are React's own props; an option's
        // `Selected` is the server's spelling of what `value` already says.
        | ElementPlan.Prop.Value value -> [ prop.value value ]
        | ElementPlan.Prop.Values values -> [ prop.value (List.toArray values) ]
        | ElementPlan.Prop.Checked(on, _) -> [ prop.isChecked on ]
        | ElementPlan.Prop.Selected -> []

    let private unbound: Attach = fun _ _ -> []

    /// Emit a plan, binding its attachment points through `attach` (Phase 2278).
    let rec planWith (attach: Attach) (p: ElementPlan.Plan<ReactElement>) : ReactElement =
        match p with
        | ElementPlan.Plan.Host element -> element
        | ElementPlan.Plan.Nothing -> Html.none
        | ElementPlan.Plan.TextNode text -> Html.text text
        | ElementPlan.Plan.Element spec ->
            let props =
                [ match spec.Key with
                  | Some key -> prop.key key
                  | None -> ()
                  yield! spec.Props |> List.collect (property attach)
                  match spec.Content with
                  | ElementPlan.Content.Empty -> ()
                  | ElementPlan.Content.Text text -> prop.text text
                  | ElementPlan.Content.Children children -> prop.children (children |> List.map (planWith attach))
                  | ElementPlan.Content.InnerHtml markup -> prop.dangerouslySetInnerHTML (TrustedTypes.html markup) ]

            HtmlHelper.createElement spec.Tag props

    /// Emit a plan that declares no attachment point.
    let plan (p: ElementPlan.Plan<ReactElement>) : ReactElement = planWith unbound p

// ─── Reconciliation keys for repeated children ─────────────────────────────
//
//  React reconciles a list of siblings by POSITION unless each carries a key.
//  Position is the wrong identity for anything a reader can re-order: the
//  fifth `<tr>` after a sort is a different row, but React keeps the fifth
//  child's component instance and only rewrites the props it can see — so
//  everything the instance holds that the props do NOT carry stays where it
//  was. That is precisely the state this renderer keeps per instance: a
//  `Binding.Local` input buffer, a `SwitchStage` timer, a combobox's open
//  list, an uncommitted cell edit. The half-typed value follows the position
//  and lands on someone else's row.
//
//  So every repeated-children arm below states an identity. Where the tree
//  declares one it is used — a grid's `RowKey` / `RowKeyField`, a form field's
//  id, an option's value; where the arm is genuinely positional (a skeleton's
//  placeholder rows, a static table whose rows are literals in document order)
//  the index IS the identity and saying so is honest rather than a fallback.

/// The identity of one member of a repeated-children arm.
///
/// A declared key wins; anything else falls back to the position, marked so it
/// can never collide with a real key. Two spellings are refused as identities
/// even though they are `Some`: the empty string (the "no key contract" answer
/// a decoded grid gives) and the `"<closure>"` constant a decoded `RowKey`
/// projects — under the second, EVERY row would answer the same key, which is
/// worse than positional reconciliation because React would then treat four
/// different rows as one. `usableKey` in the grid arm applies the same rule to
/// selection, for the same reason.
let reconciliationKey (declared: string option) (index: int) : string =
    // Phase 2108 — defined once in `ElementPlan`, where the planned repeated
    // children (skeleton rows, list items) take their keys from.
    ElementPlan.reconciliationKey declared index

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

// Phase 1152 — `Action.Dispatch` is marked in-process-only by the IDL
// annotation, which renders as `[<Obsolete(…, false)>]`: FS0044 at every
// mention, and an error here under `TreatWarningsAsErrors`. Scoped off for this
// ONE declaration and reopened after it. `containsUnwiredAction` classifies
// every case for a dev-mode tooltip; it neither constructs a `Dispatch` nor
// serialises one, and its `Dispatch` arm answers `false` precisely because the
// renderer is the in-process host where the case is CORRECT.
#nowarn "44"

// ONE action, not counting the actions nested in it; `containsUnwiredAction`
// below adds those.
let private unwiredAt (action: Action<'Msg>) : bool =
    match action with
    | Action.Dispatch _ -> false
    // A chain reaches no substrate itself; its members are reached by the fold.
    | Action.Chain _ -> false
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
    // Phase 1124 — `window.print()` is the browser's own, always present, and
    // needs no substrate a host could fail to wire. Its failure mode (the reader
    // cancels the dialogue) is intrinsic to the host, which is the clipboard
    // arm's reasoning exactly — treat as wired, never as an unwired stub.
    | Action.Print -> false
    // Phase 1537 — `window.confirm()` and `.focus()` are the browser's own on
    // the same reasoning. A `Confirm` is "unwired" exactly when its own
    // CONTINUATIONS are: the dialogue always works, so what the reader would be
    // asking for is whatever the yes branch would do. `onCancel` counts too —
    // a cancel branch that reaches no substrate is as inert as a confirm one.
    | Action.Confirm _ -> false
    | Action.Focus _ -> false

#warnon "44"

/// Whether `action`, or any action nested in it, reaches no substrate. Phase
/// 2039 — the nesting (`Chain`'s members, a `Confirm`'s continuations) is the
/// generated `Action.fold`.
let private containsUnwiredAction (action: Action<'Msg>) : bool =
    Fuaran.UI.Generated.Action.fold (fun found a -> found || unwiredAt a) false action

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
let applyDispatchGateOutcome
    (runtime: Runtime.IFuaranRuntime)
    (descriptor: Runtime.ActionDescriptor)
    (effect: unit -> unit)
    : Result<unit, string> =
    if runtime.CanDispatch descriptor then
        effect ()
        Ok()
    else
        let reason =
            sprintf "dispatch denied by policy gate: %s" (Runtime.ActionDescriptor.describe descriptor)

        runtime.Warn("[Fuaran] " + reason)
        Error reason

let applyDispatchGate
    (runtime: Runtime.IFuaranRuntime)
    (descriptor: Runtime.ActionDescriptor)
    (effect: unit -> unit)
    : unit =
    applyDispatchGateOutcome runtime descriptor effect |> ignore

/// Perform a TREE-ORIGINATED write to the State channel, refusing any
/// host-reserved key (Phase 782). Every State write that originates in a
/// rendered tree — `Action.SetState`, a covered control's write-back, a
/// declarative `Call … into State` target — routes through here, so a reserved
/// key is unaddressable from a tree by construction rather than by whatever the
/// host's `CanDispatch` policy happened to say.
///
/// Phase 1550 widened WHAT is reserved and changed nothing about HOW: the test
/// is `StateKeys.isReserved` — the `host.` prefix, plus whatever the host has
/// declared by exact name — and the refusal takes the same path, with the same
/// `Warn` record, that it always has. A declared list needs no second denial
/// channel, and adding one would give the same fact two places to be reported
/// from and one place to be missed.
///
/// A refusal is RECORDED through `Warn`, never silent: a tree that tried to write
/// a host slot is a signal worth surfacing, and a silently-dropped write is
/// indistinguishable from a store that did not re-render.
///
/// Public so the .NET tests can pin the decision without a browser render
/// (precedent: `applyDispatchGate`).
let treeStateWriteOutcome (runtime: Runtime.IFuaranRuntime) (key: string) (write: unit -> unit) : Result<unit, string> =
    if StateKeys.isReserved key then
        let why =
            if StateKeys.isHostReserved key then
                sprintf "is under the host-reserved '%s' namespace" StateKeys.HostReservedPrefix
            else
                "was declared host-reserved by name"

        let reason =
            sprintf "State write refused — key '%s' %s and is not addressable from a rendered tree." key why

        runtime.Warn("[Fuaran] " + reason)
        Error reason
    else
        write ()
        Ok()

let treeStateWrite (runtime: Runtime.IFuaranRuntime) (key: string) (write: unit -> unit) : unit =
    treeStateWriteOutcome runtime key write |> ignore

/// Perform a TREE-DECLARED navigation (Phase 782): sanitise the route, then
/// gate it, then hand the SANITISED route to the host router.
///
/// Sanitising here rather than only at href/src render time is the point.
/// `IFuaranRuntime.Navigate` is documented as the seam a host wires to its SPA
/// router; the shipped browser runtime happens to assign `window.location.hash`,
/// which cannot execute script, but a host that wires `location.href` or
/// `router.push` — exactly what the interface invites — inherits a
/// `javascript:`-URL sink and an open redirect from a decoded tree.
///
/// The route arrives here as the CANONICAL DECODED FIELD, which is what makes
/// this total: the wire accepts `route` / `href` / `url` / `to` as aliases and
/// they all decode to this one field, so there is no spelling that reaches the
/// router around the check. A refusal emits nothing at all rather than
/// navigating to `about:blank` — a redirect the author never asked for is not an
/// improvement on a refused one.
///
/// Public so the .NET tests can pin the decision without a browser render
/// (precedent: `applyDispatchGate`).
/// Phase 1026 — the policy parameter is what makes this AMBIENT rather than
/// available. The scheme floor alone answers "is this route safe to have"; it
/// has nothing to say about "is this destination one the composition declared",
/// and only the second question closes an open redirect to an attacker's host.
/// A refusal here emits nothing at all rather than navigating to
/// `about:blank` — unlike an `href`, where the anchor must stay structurally
/// valid, a navigation the author never asked for is not an improvement on a
/// refused one.
let treeNavigateOutcome
    (runtime: Runtime.IFuaranRuntime)
    (policy: Sanitize.EgressPolicy)
    (route: string)
    (navigate: string -> unit)
    : Result<unit, string> =
    match Sanitize.checkDestination policy Sanitize.EgressClass.Route route with
    | Sanitize.EgressVerdict.Allowed safeRoute ->
        applyDispatchGateOutcome runtime (Runtime.ActionDescriptor.Navigate safeRoute) (fun () -> navigate safeRoute)
    | refused ->
        // `describeEgressVerdict` carries the CLASS and — where there is one —
        // the HOST, never the path or query, which is exactly where an
        // exfiltrated payload would be sitting. The `Warn` may carry the raw
        // route because a diagnostic a developer reads in their own console is
        // a different surface from a log that outlives the session; the
        // RECORDED reason keeps the scrubbed path, because a Phase 889 record
        // is durable and a route's query string is user data.
        runtime.Warn(sprintf "[Fuaran] Action.Navigate refused — %s: %s" (Sanitize.describeEgressVerdict refused) route)

        Error(
            sprintf
                "Action.Navigate refused — %s: %s"
                (Sanitize.describeEgressVerdict refused)
                (ActionInvocation.routePath route)
        )

/// Phase 1536 — perform an already-resolved, already-gated navigation into the
/// browsing context `target` names.
///
/// ONE function, shared by the hydrated interpreter and the zero-JS resume
/// interpreter, because `noopener,noreferrer` is a SECURITY property of the
/// `Blank` case and a property applied by two functions is a property that
/// eventually differs between them. Without `noopener` the opened document holds
/// a live `window.opener` handle back into the host page; without `noreferrer`
/// the destination is told where the reader came from.
///
/// `Blank` deliberately does NOT route through `IFuaranRuntime.Navigate`. That
/// seam is wired to an SPA router or `location.hash`, neither of which can open
/// a second browsing context at all — handing it a `Blank` would silently
/// navigate in place. Reaching `window.open` directly is the `Print` /
/// `CommitLocal` precedent: the browser's own API, where there is nothing for a
/// host to configure.
///
/// Public so both interpreters can share it, on the same reasoning that made
/// `applyDispatchGate` public. It performs; it never judges — the caller has
/// already run the egress check and the dispatch gate on `safeRoute`.
let performNavigation (runtime: Runtime.IFuaranRuntime) (target: NavigateTarget) (safeRoute: string) : unit =
    match target with
    | NavigateTarget.Self -> runtime.Navigate safeRoute
    | NavigateTarget.Blank ->
        Browser.Dom.window.``open`` (safeRoute, "_blank", "noopener,noreferrer")
        |> ignore

let treeNavigate
    (runtime: Runtime.IFuaranRuntime)
    (policy: Sanitize.EgressPolicy)
    (route: string)
    (navigate: string -> unit)
    : unit =
    treeNavigateOutcome runtime policy route navigate |> ignore

/// Perform a TREE-DECLARED API call (Phase 1523): resolve the endpoint against
/// the ambient destination policy under `EgressClass.Call`, then gate it, then
/// hand the SANITISED endpoint to the host runtime.
///
/// The `Call` class did not exist before this, so `Action.Call` was the one
/// outbound effect the Phase 1026 destination policy could not speak about: a
/// rendered tree plus one click reached `fetch()` at any origin, gated only by
/// whatever `CanDispatch` the host had wired — and with `into: State k` the
/// response was written straight into the store the rest of the tree renders
/// from. That is an exfiltration channel and an injection channel in one
/// gesture, which is precisely why the class is scoped separately from `Media`
/// rather than folded into it.
///
/// The ORDER is destination-then-gate, matching `treeNavigateOutcome` exactly:
/// a host's `CanDispatch` is a policy about intent, the destination check is a
/// policy about reach, and asking the host to adjudicate an endpoint the
/// composition never declared would put the wrong question to the wrong layer.
///
/// A refusal performs NO call at all — the same posture as `Navigate` and for
/// the same reason: a request to a substitute destination is not an improvement
/// on a refused one. The declarative `into` target therefore stays unwritten,
/// so readers keep their `OnLoading` surface rather than showing a wrong value.
/// The RECORDED reason carries the scrubbed description, never the endpoint's
/// query string, because a Phase 889 record is durable and a query string is
/// where an exfiltrated payload sits.
let treeCallOutcome
    (runtime: Runtime.IFuaranRuntime)
    (policy: Sanitize.EgressPolicy)
    (endpoint: string)
    (call: string -> unit)
    : Result<unit, string> =
    match Sanitize.checkDestination policy Sanitize.EgressClass.Call endpoint with
    | Sanitize.EgressVerdict.Allowed safeEndpoint ->
        applyDispatchGateOutcome runtime (Runtime.ActionDescriptor.Call safeEndpoint) (fun () -> call safeEndpoint)
    | refused ->
        runtime.Warn(sprintf "[Fuaran] Action.Call refused — %s: %s" (Sanitize.describeEgressVerdict refused) endpoint)

        Error(
            sprintf
                "Action.Call refused — %s: %s"
                (Sanitize.describeEgressVerdict refused)
                (ActionInvocation.routePath endpoint)
        )

/// The recursive action interpreter. NOT the emission point — see
/// `runActionAs` below and the `Chain` note there.
///
/// `denied` accumulates every gate refusal observed anywhere in the (possibly
/// chained) interpretation, so the single OUTER emission point can classify the
/// gesture without consulting any gate a second time. Refusals are collected
/// rather than short-circuited because `Chain` genuinely runs its remaining
/// arms after one is refused, and a record that reported only the first would
/// mis-describe the gesture.
// Phase 1152 — `Action.Dispatch` is marked in-process-only by the IDL
// annotation, which renders as `[<Obsolete(…, false)>]`: FS0044 at every
// mention, and an error here under `TreatWarningsAsErrors`. Scoped off for this
// ONE declaration and reopened after it. This is the interpreter's `Dispatch`
// arm — the IN-PROCESS host itself, executing the case on the one path where it
// is not merely correct but the point. A warning here would be the compiler
// telling the intended consumer not to consume.
#nowarn "44"

let rec private runActionCore (ctx: RenderContext<'Msg>) (denied: string list ref) (action: Action<'Msg>) : unit =
    let note (r: Result<unit, string>) : unit =
        match r with
        | Ok() -> ()
        | Error reason -> denied.Value <- reason :: denied.Value

    let gate (descriptor: Runtime.ActionDescriptor) (effect: unit -> unit) : unit =
        note (applyDispatchGateOutcome ctx.Runtime descriptor effect)

    let stateWrite (key: string) (write: unit -> unit) : unit =
        note (treeStateWriteOutcome ctx.Runtime key write)

    match action with
    | Action.Dispatch msg -> ctx.Dispatch msg
    | Action.Chain actions ->
        for a in actions do
            runActionCore ctx denied a
    | Action.Call(ep, onResult, into) ->
        // Phase 428: a `Some` closure wins (exactly the pre-428 behaviour); the
        // declarative `into` target writes the response to its store slot and
        // the reactive subscriptions re-render readers. Both `None` is a
        // fire-and-forget command call (FUARAN073 warns at validate time). A
        // failed / undecodable call never reaches the callback — the host's
        // `Call` implementation surfaces it (the default BrowserRuntime warns)
        // and the target slot stays unwritten, so readers keep their
        // `OnLoading` surface rather than showing a silent wrong value.
        // Phase 1523 — destination-then-gate, exactly as `Action.Navigate` has
        // run since 782/1026. `endpoint` above re-wraps the bare string for the
        // seam; the SANITISED string is what reaches the runtime, so a call
        // never leaves through a spelling the policy did not see.
        note (
            treeCallOutcome ctx.Runtime ctx.Policy.Egress ep (fun safeEp ->
                let endpoint = ApiEndpoint safeEp

                match onResult, into with
                | Some f, _ -> ctx.Runtime.Call(endpoint, (fun raw -> ctx.Dispatch(f raw)))
                | None, Some target ->
                    ctx.Runtime.Call(
                        endpoint,
                        fun raw ->
                            match target with
                            | CallResultTarget.State key ->
                                // Scope-aware routing mirrors `Action.SetState` (Phase 266);
                                // host-reserved keys are refused (Phase 782) — the response
                                // target is as tree-declared as an `Action.SetState` key is.
                                stateWrite key (fun () ->
                                    match ctx.Policy.Scope with
                                    | Some scopeId -> (StateStore.forScope scopeId).Set(key, raw)
                                    | None -> StateStore.set key raw)
                            | CallResultTarget.Query name -> QueryStore.set name raw
                    )
                | None, None -> ctx.Runtime.Call(endpoint, ignore))
        )
    | Action.Notify(channel, payload) ->
        // Phase 782 — `Notify` publishes onto a host-addressable channel, so it
        // is gated like every other outbound effect. Before 782 it reached the
        // runtime with no gate consultation at all.
        gate (Runtime.ActionDescriptor.Notify channel) (fun () -> ctx.Runtime.Notify(channel, payload))
    // Phase 782 — sanitise-then-gate on the ACTION path, not only where an
    // href/src is rendered. See `treeNavigate`.
    //
    // Phase 1536 — RESOLVE, THEN GATE, and the order is the whole point. The
    // route is a `TextSource`, so it may be computed from what the reader is
    // looking at; the egress check and the dispatch gate then judge the
    // RESOLVED string. Gating the template would consult the policy about
    // `/orders/{id}`, a URL nobody navigates to, while the string the host
    // actually receives went unexamined — which is not a weaker check, it is a
    // check of the wrong subject.
    //
    // A route that does not resolve WARNS and navigates nowhere (FGP 3). It
    // must not degrade to the empty string the way a bound LABEL does: `""` is
    // a real navigation (this document, query and fragment stripped), so the
    // rendering degradation would become a navigation the author never asked
    // for. `tryResolveTextSource` is the resolution that reports rather than
    // degrades.
    | Action.Navigate(route, target) ->
        // The EMPTY resolution is refused beside the absent one, and on the
        // same reasoning rather than as a tidy-up: a `State` binding carrying a
        // declared default of `""` RESOLVES, to a string that is not a
        // destination. Both are "there is nothing to navigate to".
        match
            BindingResolver.tryResolveTextSource ctx.Sources route
            |> Option.filter (System.String.IsNullOrWhiteSpace >> not)
        with
        | None ->
            ctx.Runtime.Warn
                "[Fuaran] Action.Navigate route did not resolve to a destination — no navigation performed. A bound route whose source is absent or empty, or an i18n key with no translation, is not a destination."

            note (Error "Action.Navigate refused — route did not resolve to a destination")
        | Some resolved ->
            note (treeNavigateOutcome ctx.Runtime ctx.Policy.Egress resolved (performNavigation ctx.Runtime target))
    | Action.SetState(key, value, valueFrom) ->
        // Phase 782 — gated, and host-reserved keys refused. Scope-aware routing
        // (Phase 266): a guest rendered under `Some scopeId` writes to its own
        // isolated `StateStore.forScope` instance so its state never touches the
        // host's default store (mirrors BrowserRuntime.SetState — the `JVal`
        // payload lowers to a plain JS value via the bridge). The default
        // (`None`) delegates to the host runtime.
        // Phase 818 — `valueFrom` (value XOR valueFrom, decode-enforced)
        // evaluates AT DISPATCH TIME, inside the same gate + host-reserved
        // guard the literal path runs under. An unresolved / unliftable source
        // performs NO write and warns — a derived write must never silently
        // write a wrong value.
        gate (Runtime.ActionDescriptor.SetState key) (fun () ->
            stateWrite key (fun () ->
                let payload: JVal option =
                    match valueFrom, value with
                    | Some b, _ ->
                        (match BindingResolver.resolveJVal ctx.Sources b with
                         | BindingResolver.Resolved jv -> Some jv
                         | BindingResolver.NotResolved ->
                             ctx.Runtime.Warn(
                                 sprintf
                                     "[Fuaran] Action.SetState(%s) valueFrom did not resolve — no write performed."
                                     key
                             )

                             None
                         | BindingResolver.Errored m ->
                             ctx.Runtime.Warn(sprintf "[Fuaran] Action.SetState(%s) valueFrom errored: %s" key m)
                             None
                         | BindingResolver.I18nUnresolved k ->
                             ctx.Runtime.Warn(
                                 sprintf
                                     "[Fuaran] Action.SetState(%s) valueFrom is an unresolved i18n key '%s' — no write performed."
                                     key
                                     k
                             )

                             None)
                    | None, v -> v

                match payload with
                | None -> ()
                | Some jv ->
                    (match ctx.Policy.Scope with
                     | Some scopeId ->
                         let raw = Runtime.JsonBridge.jvalToJs jv
                         (StateStore.forScope scopeId).Set(key, raw)
                     | None -> ctx.Runtime.SetState(key, jv))))
    | Action.AiTool(toolName, args) ->
        gate (Runtime.ActionDescriptor.AiTool toolName) (fun () -> ctx.Runtime.InvokeAiTool(toolName, args))
    | Action.CommitLocal nodeId ->
        // Dispatch a DOM CustomEvent on window keyed on the
        // node id; the corresponding LocalBinding's useEffect listener
        // (mounted in LocalBindings.fs) drains its buffer through the
        // typed `OnCommit`. The event name is namespaced under
        // `fuaran-commit-local-` so cross-app collisions stay
        // structurally unlikely.
        //
        // Phase 782 — gated. No `IFuaranRuntime` member backs it, which is why it
        // was missed, but dispatching a window event IS a host-observable effect:
        // anything listening on `fuaran-commit-local-*` is reachable from a
        // decoded tree that names the right node id.
        gate (Runtime.ActionDescriptor.CommitLocal nodeId) (fun () ->
            let eventName = sprintf "fuaran-commit-local-%s" nodeId
            let evt = Browser.Dom.window.document.createEvent "CustomEvent"
            evt.initEvent (eventName, false, true)
            Browser.Dom.window.dispatchEvent (evt) |> ignore)
    | Action.WriteToClipboard text ->
        // Phase 782 — gated. The clipboard is a cross-application channel: a
        // decoded tree writing it can plant content a user later pastes
        // somewhere with authority.
        //
        // Phase 1126 — the payload is a `TextSource`, resolved HERE, at
        // dispatch time, through the same `BindingResolver.resolveTextSource` the surrounding tree
        // renders its labels through. Resolving at dispatch rather than at
        // decode is the whole point of the widening: what lands on the
        // clipboard is what the reader was looking at when they asked for it,
        // not what the document said when it arrived. The substrate seam
        // (`IFuaranRuntime.WriteToClipboard`) still takes a `string` — it
        // performs a write, it does not evaluate a tree.
        gate Runtime.ActionDescriptor.WriteToClipboard (fun () ->
            ctx.Runtime.WriteToClipboard(BindingResolver.resolveTextSource ctx.Sources text))
    | Action.Print ->
        // Phase 1124 — the reader's own print dialogue. Renderer-native, with no
        // `IFuaranRuntime` member behind it: `window.print()` is the browser's
        // own and takes no arguments, so a seam member would be a port for a
        // capability every browser host already has — the `CommitLocal`
        // precedent, which reaches `Browser.Dom.window` directly for the same
        // reason.
        //
        // GATED all the same, and for the reason `CommitLocal`'s comment gives:
        // "no `IFuaranRuntime` member backs it" is not "unreachable". A decoded
        // tree from an untrusted emitter raising an unbidden print dialogue is a
        // host-observable effect — modal, focus-stealing, and on some platforms
        // a physical act — so a default-deny host refuses it through the same
        // `CanDispatch` seam it refuses `Call` / `Navigate` / `AiTool`.
        //
        // Nothing is read back. `window.print()` returns unit, blocks until the
        // reader dismisses the dialogue, and reports neither whether they printed
        // nor what they chose; the tree therefore learns nothing about the reader,
        // which is what keeps this the least-disclosing effect in the gated set.
        gate Runtime.ActionDescriptor.Print (fun () -> Browser.Dom.window.print ())
    | Action.Confirm(prompt, onConfirm, onCancel) ->
        // Phase 1537 — ONE dispatch path, gated twice, and the ordering is the
        // whole of the security argument.
        //
        // The FIRST gate is this one: `ActionDescriptor.Confirm` asks whether
        // this tree may raise a dialogue at all. `window.confirm()` is modal,
        // steals focus and blocks the page, so a host rendering untrusted trees
        // must be able to refuse it — the `Print` reasoning exactly, and for the
        // same reason it is renderer-native with no `IFuaranRuntime` member
        // behind it.
        //
        // The SECOND gate is not written here, and that is the point. On
        // acceptance the continuation re-enters `runActionCore` — this same
        // function, from the top — so a `Navigate` inside it meets
        // `treeNavigateOutcome`'s egress check and its own descriptor, a
        // `SetState` meets the host-reserved-key guard, a `Call` meets
        // `treeCallOutcome`. There is no branch here that performs an effect
        // directly, so there is no way for a confirm to carry an action past a
        // gate that would have refused it standing alone.
        //
        // A refusal of the confirm itself performs NEITHER branch. The reader
        // was never asked, so neither answer happened; running `onCancel` would
        // report a refusal the reader did not make.
        //
        // The prompt resolves at DISPATCH time through the same `BindingResolver.resolveTextSource` the
        // surrounding tree renders its labels through (the `WriteToClipboard`
        // precedent), so a question may name what the reader is looking at.
        gate (Runtime.ActionDescriptor.Confirm(BindingResolver.resolveTextSource ctx.Sources prompt)) (fun () ->
            if Browser.Dom.window.confirm (BindingResolver.resolveTextSource ctx.Sources prompt) then
                runActionCore ctx denied onConfirm
            else
                onCancel |> Option.iter (runActionCore ctx denied))
    | Action.Focus nodeId ->
        // Phase 1537 — move focus to the addressed node. Renderer-native for
        // the `Print` / `CommitLocal` reason: `.focus()` is the browser's own
        // and takes nothing a host could configure.
        //
        // GATED all the same. Focus theft is host-observable — it moves the
        // reader's caret and, on most engines, scrolls the element into view —
        // so a default-deny host refuses it through the same `CanDispatch` seam
        // it refuses `Call` / `Navigate` / `AiTool`.
        //
        // A node id that addresses nothing WARNS and moves nothing. It is not
        // an error: a document may address a node that a `Switch` branch or a
        // `Visible` predicate has legitimately removed from the flow, and
        // throwing there would take the whole gesture down. It is not silent
        // either — an author whose focus target never resolves has a defect,
        // and the warning is where they find it.
        gate (Runtime.ActionDescriptor.Focus nodeId) (fun () ->
            let selector =
                "[data-fuaran-node-id=\""
                + nodeId.Replace("\\", "\\\\").Replace("\"", "\\\"")
                + "\"]"

            let el = Browser.Dom.document.querySelector selector

            if isNull el then
                ctx.Runtime.Warn(
                    sprintf
                        "[Fuaran] Action.Focus('%s') addressed no rendered node — focus unchanged. The node may be absent from the tree, or removed from the flow by a Switch branch."
                        nodeId
                )
            else
                (el :?> Browser.Types.HTMLElement).focus ())
    | Action.ReadFileBody(fileRef, fileHandle, encoding, onRead) ->
        // Default-deny by shape (FGP 3): consult the policy gate before the
        // host reads the file. On allow, the runtime reads the blob (async at
        // the host level) and we dispatch `onRead body` from the callback —
        // mirroring how `Call`'s `onResult` is pre-wrapped with `dispatch`.
        // The generated case splits the id and the host-only handle; the
        // `IFuaranRuntime` seam keeps its `FileRef` record, so rebuild it at
        // the boundary. An absent `onRead` still reads (host effects fire) but
        // dispatches nothing.
        gate (Runtime.ActionDescriptor.ReadFileBody fileRef) (fun () ->
            let file: FileRef = { Id = fileRef; Handle = fileHandle }

            let dispatchRead =
                match onRead with
                | Some f -> fun (body: string) -> ctx.Dispatch(f body)
                | None -> ignore

            ctx.Runtime.ReadFileBody(file, encoding, dispatchRead))
    | Action.Invoke(capabilityId, _args) ->
        // Phase 283 — invoke a host-registered capability as an effect. Default-deny by shape
        // (FGP 3): consult the policy gate before dispatch (reusing the AiTool descriptor — the
        // closest gate surface for a named host invocation). v1 surfaces the invocation via the
        // runtime diagnostic; a host wires real capability dispatch + Phase-27 replay through the
        // AiTools registry.
        gate (Runtime.ActionDescriptor.AiTool capabilityId) (fun () ->
            ctx.Runtime.Warn(sprintf "[Fuaran] capability invoke (no host dispatch wired): %s" capabilityId))

#warnon "44"

/// **The single client-side emission point** (Phase 889). One record per user
/// gesture, at the outer entry, with the outcome the interpretation actually
/// produced.
///
/// Why the outer entry and not inside the interpreter: `runActionCore` recurses
/// through `Action.Chain`, so emitting inside it would yield N records for one
/// gesture with no way to tell a chain from N clicks. A chain is ONE
/// invocation, and the default redaction mode renders it as the bare `Chain`
/// with no constituents — a deliberate decision, not something inherited from
/// the log-safe helper.
///
/// The Phase 330 interaction id is read from `ctx.SessionContext` PER DISPATCH,
/// never captured: the host's context is rebuilt per render because the id
/// changes every turn, and a cached id stamps the first interaction's id onto
/// every later one — a correlation worse than none, because it looks right.
///
/// A throwing effect is recorded as `Failed` and then RE-RAISED. Recording must
/// not change what the renderer propagates to React.
let private runActionAs (provenance: AffordanceProvenance) (ctx: RenderContext<'Msg>) (action: Action<'Msg>) : unit =
    match ctx.ActionSink with
    | None ->
        // Recording off — the shipped default. No ref cell, no site, no record.
        runActionCore ctx (ref []) action
    | Some _ ->
        let denied = ref []

        let site =
            ActionInvocation.clientSite provenance ctx.CurrentNodeId (Map.tryFind promptIdKey ctx.SessionContext)

        let outcome =
            try
                runActionCore ctx denied action

                match List.rev denied.Value with
                | [] -> ActionOutcome.Dispatched
                | reasons -> ActionOutcome.Denied(String.concat "; " reasons)
            with ex ->
                ActionInvocation.emit ctx.ActionSink site (ActionOutcome.Failed ex.Message) action
                reraise ()

        ActionInvocation.emit ctx.ActionSink site outcome action

/// Interpret a TREE-DECLARED action — the shape every authored handler takes.
let private runAction (ctx: RenderContext<'Msg>) (action: Action<'Msg>) : unit =
    runActionAs AffordanceProvenance.TreeDeclared ctx action

/// Interpret a RENDERER-SYNTHESISED action — Phase 860's O2-A / Phase 866's
/// general rule, where the wire names a capability and the renderer draws the
/// affordance and dispatches an existing `Action` through this same gate.
///
/// Separate from `runAction` for exactly one reason and it is not cosmetic: the
/// resulting records genuinely answer "what did the user do" (they clicked a
/// sort header) but they are NOT actions any author or model emitted. A corpus
/// that cannot tell them apart attributes renderer behaviour to the emitter.
let private runSynthesisedAction (ctx: RenderContext<'Msg>) (action: Action<'Msg>) : unit =
    runActionAs AffordanceProvenance.RendererSynthesised ctx action

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
        // Host-reserved keys are refused (Phase 782) — the write-back default is
        // a tree-originated write like any other, and a decoded control binding
        // to `host.<x>` must not become a back door around `Action.SetState`.
        treeStateWrite ctx.Runtime key (fun () ->
            match ctx.Policy.Scope, value with
            | Some scopeId, Some v -> (StateStore.forScope scopeId).Set(key, v)
            | Some scopeId, None -> (StateStore.forScope scopeId).Remove key
            | None, Some v -> StateStore.set key v
            | None, None -> StateStore.remove key)
    | Binding.Filter(name, _) ->
        match value with
        | Some v -> FilterStore.set name v
        | None -> FilterStore.clear name
    | _ -> ()

/// Fuaran-UI Phase 1538 — a `Binding.Local`'s DECLARED commit destination. It
/// routes through `writeBackTo`'s `Binding.State` arm rather than touching the
/// store itself, so the flush inherits everything that path already owes: the
/// Phase 266 scope routing, and the Phase 782 refusal of host-reserved keys. A
/// declared commit target is a tree-originated write like any other, and a
/// decoded document must not reach `host.<x>` through a buffer when it cannot
/// reach it through `Action.SetState`.
///
/// A `Local` that ALSO carries an `onCommit` closure runs both. That pair is a
/// decode refusal, so it can only arise in an in-process tree an author built
/// by hand — where both were written deliberately and silently dropping one
/// would be the surprise.
let private commitLocalTo (ctx: RenderContext<'Msg>) (commitTo: string option) (value: obj) : unit =
    match commitTo with
    | Some key -> writeBackTo ctx (Binding.State(key, None): Binding<obj>) (Some value)
    | None -> ()

/// The control write-back dispatch (Phase 426) — THE one, for every client
/// form-field and filter-chip control. A present handler dispatches (the
/// closure wins and no store is touched); an omitted handler writes the typed
/// change back to the control's own value binding through `writeBackTo`, where
/// `None` is the CLEAR (a cleared choice removes the slot rather than storing
/// an empty value).
///
/// Module-level, and that is load-bearing rather than incidental: this body
/// existed three times over — here, as `renderFormField`'s local `handle`, and
/// as `renderFilterSpec`'s eight locals — and the two local copies were
/// unreachable from any test BY CONSTRUCTION, on the path carrying the same
/// hand-built-payload risk with none of the coverage. Every one of those call
/// sites now routes here. Keep it that way: a local re-implementation inside a
/// render function is untestable by construction, and the harness census in
/// `Fuaran.UI.Tests` fails on one reappearing.
let fieldChange
    (ctx: RenderContext<'Msg>)
    (onChange: ('v -> Action<'Msg>) option)
    (binding: Binding<'T>)
    (write: obj option)
    (v: 'v)
    : unit =
    match onChange with
    | Some h -> runAction ctx (h v)
    | None -> writeBackTo ctx binding write

/// Change dispatch for the dual-input pair form fields (`FormFieldKind.Range` /
/// `FormFieldKind.DateTimeRange`): either input's change emits the WHOLE pair. A
/// present handler dispatches it (the closure wins); an omitted handler writes
/// the rebuilt pair record back to the field's own value binding.
///
/// The write payload is a plain `obj`, NOT `obj option`, and that is the point:
/// a pair field has no cleared state, so the `None` a caller could otherwise
/// pass is the write-back CLEAR — it would erase the slot instead of storing
/// the new pair. Encoding "always writes" in the signature makes that defect
/// unrepresentable rather than merely fixed at today's call sites. Callers box
/// the pair RECORD (`RangePair` / `DateTimeRangePair`), which is what
/// `BindingResolver.tryResolve` reads back out; the `'v` tuple is the shape the
/// `onChange` closure takes. Module-level so the .NET tests can pin the
/// dispatch (precedent: `applyDispatchGate`).
let pairFieldChange
    (ctx: RenderContext<'Msg>)
    (onChange: ('v -> Action<'Msg>) option)
    (binding: Binding<'T>)
    (pair: obj)
    (v: 'v)
    : unit =
    fieldChange ctx onChange binding (Some pair) v

/// Phase 1141 — the two per-input change closures a rendered numeric-pair
/// control carries: `(minInput, maxInput)`. Either input emits the whole
/// rebuilt pair, so each closure must land its new value in ITS OWN slot and
/// carry the OTHER slot through from `current` unchanged. That construction is
/// the risk this seam exists to expose: it was previously written out inline at
/// every call site, where a swapped slot or a mis-boxed payload is invisible to
/// every behavioural test the renderer has (Feliz's .NET `ReactElement` is
/// opaque, so a rendered control's handler cannot be extracted and fired).
/// Building the closures here means the harness drives exactly the closure the
/// control carries.
///
/// `'T` is free because the write destination is the caller's binding: a form
/// field passes its own `Binding<RangePair>` value slot, a filter chip passes
/// the auto-bind `Binding.Filter(name, None)` its declarative write has always
/// targeted.
let rangeInputHandlers
    (ctx: RenderContext<'Msg>)
    (onChange: (float * float -> Action<'Msg>) option)
    (binding: Binding<'T>)
    (current: RangePair)
    : (float -> unit) * (float -> unit) =
    let write (pair: RangePair) =
        pairFieldChange ctx onChange binding (box pair) (pair.Min, pair.Max)

    (fun v -> write ({ Min = v; Max = current.Max }: RangePair)),
    (fun v -> write ({ Min = current.Min; Max = v }: RangePair))

/// Phase 1141 — `rangeInputHandlers` for the date pair: `(fromInput, toInput)`.
/// Phase 725's chip and the form's `DateRange` field are the same shape over
/// `DateTimeRangePair`, so they share one construction.
let dateRangeInputHandlers
    (ctx: RenderContext<'Msg>)
    (onChange: (string * string -> Action<'Msg>) option)
    (binding: Binding<'T>)
    (current: DateTimeRangePair)
    : (string -> unit) * (string -> unit) =
    let write (pair: DateTimeRangePair) =
        pairFieldChange ctx onChange binding (box pair) (pair.From, pair.To)

    (fun v -> write ({ From = v; To = current.To }: DateTimeRangePair)),
    (fun v -> write ({ From = current.From; To = v }: DateTimeRangePair))

/// Phase 663 — replace one field of a grid row for the editable-grid State
/// write-back. fuaran#665 typed the rows slot (`Row = Map<string, obj>`), so
/// the old Fable type-erasure caveat — and its `#if`-split unbox — is gone:
/// every row IS the map, statically.
let private updateRowField (row: Row) (field: string) (newValue: obj) : Row = Map.add field newValue row

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

// The `Custom` content-hash verdict + the host-configured strictness floor live
// in `Fuaran.UI.Renderer.CustomHash` (Phase 783), because the SERVER renderer
// needs exactly the same verdict from exactly the same floor. Abbreviated here
// so the dispatch body below reads unchanged.
type private CustomHashOutcome = CustomHash.CustomHashOutcome

let private classifyCustomHash
    (contextFloor: HashStrictness option)
    (treeHash: ContentHash option)
    (registryHash: ContentHash option)
    : CustomHashOutcome =
    CustomHash.classifyForRender contextFloor treeHash registryHash

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
    let renderHash (h: ContentHash option) =
        match h with
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
    (expectedIds: string list)
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

                    for expected in expectedIds do
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
    (_expectedIds: string list)
    (_runtime: Runtime.IFuaranRuntime)
    : unit =
    ()
#endif

// ─── The tooltip dismissal listener (Phase 1112) ────────────────────────────
//
// WCAG 1.4.13 asks that content revealed on hover or focus be DISMISSIBLE
// without moving the pointer or the focus. The reveal itself is pure CSS — the
// reference stylesheet shows `.fuaran-tooltip` on `:hover` / `:focus-within` of
// its `.fuaran-has-tooltip` wrapper — so the only thing script has to add is
// Escape, and it adds it by writing `data-fuaran-tooltip-dismissed` on the
// wrapper, which the stylesheet's last rule reads.
//
// ONE DOCUMENT-LEVEL LISTENER, not a per-node handler, for two reasons that are
// not stylistic. A per-node `onKeyDown` only fires when focus is already inside
// that node, so a POINTER user hovering a hint — the commonest case there is —
// could never dismiss it; the key event goes to the document. And the renderer's
// node function is not a React component, so it cannot hold an effect or a
// subscription of its own: a per-node listener would have to be attached and
// detached by hand on every render of every tooltip'd node.
//
// Installed lazily on the first hint rendered, and idempotent — the module-level
// flag means the cost is one `addEventListener` pair per document, whatever the
// tree contains. It writes and clears one attribute and touches nothing React
// owns, so a re-render never fights it.
#if FABLE_COMPILER
let mutable private tooltipDismissalInstalled = false

/// Clear the dismissal marker from every wrapper the pointer has left and the
/// focus has moved out of, so a dismissed hint can be summoned again. Run on
/// `pointerout` / `focusout`, both of which bubble to the document.
let private clearDismissedTooltips () =
    let dismissed =
        Browser.Dom.document.querySelectorAll "[data-fuaran-tooltip-dismissed]"

    for i in 0 .. dismissed.length - 1 do
        let el = dismissed[i] :?> Browser.Types.Element

        if not (el.matches ":hover") && not (el.matches ":focus-within") then
            el.removeAttribute "data-fuaran-tooltip-dismissed"

let private ensureTooltipDismissal () : unit =
    if not tooltipDismissalInstalled then
        tooltipDismissalInstalled <- true

        Browser.Dom.document.addEventListener (
            "keydown",
            fun ev ->
                let ke = ev :?> Browser.Types.KeyboardEvent

                if ke.key = "Escape" then
                    // The hints currently showing are exactly the wrappers under
                    // the pointer or holding focus — the same selector the
                    // stylesheet reveals on, so the two can never disagree about
                    // which hint Escape is aimed at.
                    let showing =
                        Browser.Dom.document.querySelectorAll
                            ".fuaran-has-tooltip:hover, .fuaran-has-tooltip:focus-within"

                    for i in 0 .. showing.length - 1 do
                        let el = showing[i] :?> Browser.Types.Element
                        el.setAttribute ("data-fuaran-tooltip-dismissed", "")
        )

        Browser.Dom.document.addEventListener ("pointerout", fun _ -> clearDismissedTooltips ())
        Browser.Dom.document.addEventListener ("focusout", fun _ -> clearDismissedTooltips ())
#else
/// .NET / SSR — there is no document to listen on, and no script tier at all.
/// The served markup carries the hint and its `aria-describedby` either way; the
/// CSS reveal still works, and Escape does not. `WIRE_FORMAT.md` §3.1 states that
/// degradation normatively rather than leaving hosts to discover it.
let private ensureTooltipDismissal () : unit = ()
#endif

// ─── Fragment resolver + expansion ─────────────────────────────────────────
//
// `collectFragments` walks the input tree once before the first render and
// builds the `RenderContext.Fragments` registry. The walk is
// `FragmentExpansion.collectFragments` (Phase 2244: shared with the server
// renderer, so the two tiers register the same fragments; its doc comment is
// where the enumeration is decided). Refs don't carry bodies, so they're not
// visited by collection.
//
// `FragmentExpansion.namespaceIds` (Phase 2041: shared with the server
// renderer, so the two tiers emit the same ids) rewrites every interior
// `NodeId` of a fragment body by prepending the ref's `NodeId` (plus ".") to it — that's how multiple
// refs to the same fragment produce DOM-unique addressable ids:
// `ref1.btn` / `ref2.btn` rather than the bare `btn`. The rewrite is
// structural — every position `NodeChildren.Reach.kindHeld` names is
// recursed into; `NodeKind.Custom` props / accessibility / styles /
// bindings are NOT id-bearing and stay untouched. Nested FragmentRef
// expansions get the prefix concatenated by the recursive render call,
// so `outerRef.innerRef.btn` is what reaches the DOM.

/// One-shot pre-render walk that collects every
/// reachable `NodeKind.FragmentDecl` body into a `Map<FragmentId, _>`.
/// `RenderContext.create` calls this on the input tree and stashes the
/// result on the `RenderContext.Fragments` field. Empty for trees that
/// don't declare fragments — zero-cost for the common case. The walk itself
/// is `FragmentExpansion.collectFragments`, keyed here by `FragmentId`.
let collectFragments<'Msg> (acc: Map<FragmentId, Node<'Msg>>) (node: Node<'Msg>) : Map<FragmentId, Node<'Msg>> =
    FragmentExpansion.collectFragments FragmentId acc node

// ─── Building a render context (Phase 2067) ────────────────────────────────

/// Builders over `RenderContext`. Every entry point below, the `Mount` guest
/// context and the catalog build their context here; a host adjusts the
/// result with `with` rather than spelling out the record.
[<RequireQualifiedAccess>]
module RenderContext =

    /// The context for rendering `node` against `sources`, dispatching to
    /// `dispatch`. It runs the two one-shot pre-render passes (the state seeds
    /// laid under `sources.State`, and the fragment registry) and pins every
    /// other field to its convenience default: the diagnostic runtime, the
    /// no-op visualisation adapter, no telemetry, action or upload sink, an
    /// empty session context, and `RenderPolicy.defaults` (default-deny
    /// egress, the process hash floor, `Permissive` CSP, the root scope).
    let create
        (sources: BindingResolver.BindingSources)
        (dispatch: 'Msg -> unit)
        (node: Node<'Msg>)
        : RenderContext<'Msg> =
        { Sources = StateSeeds.apply node sources
          Runtime = Runtime.diagnostic
          VisAdapter = VisAdapter.noOp<'Msg>
          Dispatch = dispatch
          TelemetrySink = None
          InErrorBoundary = false
          Fragments = collectFragments Map.empty node
          ExpandingFragments = Set.empty
          SessionContext = Map.empty
          ActionSink = None
          CurrentNodeId = None
          UploadSink = None
          Policy = RenderPolicy.defaults }

    /// `ctx` rendered under the runtime scope `scopeId` (Phase 266, §4o): the
    /// scope's `StateStore.forScope` snapshot is merged over `Sources.State`
    /// (scoped values winning over furnished ones, which win over seeds) and
    /// `Policy.Scope` routes `Action.SetState` writes to that isolated store.
    /// Setting `Policy.Scope` alone skips the snapshot merge, so a scoped
    /// render goes through here.
    let inScope (scopeId: string) (ctx: RenderContext<'Msg>) : RenderContext<'Msg> =
        let scopedState =
            (StateStore.forScope scopeId).Snapshot()
            |> Map.fold (fun acc k v -> Map.add k v acc) ctx.Sources.State

        { ctx with
            Sources = { ctx.Sources with State = scopedState }
            Policy = { ctx.Policy with Scope = Some scopeId } }

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
    // Phase 765 — `Now` is furnished once per render pass and reads no state
    // key or filter, so it opens no reactive channel.
    | Binding.Now _ -> []
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
    | Binding.Selection(nodeId, _, _, _) ->
        match channel with
        | SelectionChannel -> [ nodeId ]
        | _ -> []
    | Binding.Local(_, _, initialFrom, _, _, _, _) -> keysOfBinding channel initialFrom
    | Binding.I18n(_, Some args) ->
        args
        |> Map.toList
        |> List.collect (fun (_, ab) -> keysOfBinding<JVal> channel ab)
    | Binding.I18n(_, None) -> []
    | Binding.Format(source, _, _) -> keysOfBinding channel source
    // A parameterised `Transform` (Phase 424) reads through to each param's scalar source, so a chip
    // write re-evaluates every pipeline parameterised on it (its filter/state keys are the union of
    // its param sources' keys). A param-free `Transform` over a `Data` source still contributes
    // nothing. Phase 818 — a LIVE source contributes its own binding's channel keys, so a
    // `SetState` on the source key re-evaluates the pipeline and re-renders every reader (the
    // subscription semantics the reactive-derivation charter names).
    | Binding.Transform(source, _, parameters) ->
        let sourceKeys =
            match source with
            | TransformSource.Live(b, _) -> keysOfBinding channel b
            | TransformSource.Data _ -> []

        sourceKeys
        @ (defaultArg parameters []
           |> List.collect (fun (p: TransformParam) -> keysOfBinding channel p.From))
    // A `Query`'s `dependsOn` (Phase 421) names the FILTERS that scope it — a filter-store change
    // re-resolves the query. On the Filter channel it contributes those names (the invalidation
    // subscription); on the Query channel (Phase 428) it contributes its own name, so a
    // `Call … into Query <name>` write re-renders every reader of that slot.
    | Binding.Query(name, _, dependsOn) ->
        match channel with
        | FilterChannel -> defaultArg dependsOn []
        | QueryChannel -> [ name ]
        | StateChannel
        | SelectionChannel -> []
    // Fuaran-UI Phase 1534 — an `Expr` has no source slot, so its params ARE its
    // whole reactive edge: a chip or state write on any param source
    // re-evaluates the expression and re-renders every reader. The `Transform`
    // param walk above with the source half removed.
    | Binding.Expr(_, parameters) ->
        defaultArg parameters []
        |> List.collect (fun (p: TransformParam) -> keysOfBinding channel p.From)
    | Binding.Invoke _
    | Binding.Static _
    | Binding.Computed _ -> []

/// Reactive keys referenced by a `TextSource` for `channel`. `Literal` carries none; `Bound` defers
/// to its binding; `TextSource.I18n` defers to its ARGUMENTS.
///
/// Fuaran-UI Phase 1661 — the args are `Binding<JVal>` now rather than `JVal`
/// literals, so a caption reading "{count} items left" subscribes to whatever
/// channel `count` names. Missing this would not render wrongly on first paint;
/// it would render correctly once and then never update, which is the harder
/// defect to see.
let keysOfText (channel: KeyChannel) (text: TextSource) : string list =
    match text with
    | TextSource.Bound b -> keysOfBinding channel b
    | TextSource.I18n(_, args) -> args |> Map.toList |> List.collect (fun (_, ab) -> keysOfBinding channel ab)
    | TextSource.Literal _ -> []

let private keysOfTextOpt (channel: KeyChannel) (text: TextSource option) : string list =
    match text with
    | Some t -> keysOfText channel t
    | None -> []

let private keysOfBindingOpt (channel: KeyChannel) (binding: Binding<'T> option) : string list =
    match binding with
    | Some b -> keysOfBinding channel b
    | None -> []

/// Phase 596 auto-bind context for an absent (`None`) control value slot: the
/// slot behaves exactly as the decode-synthesised binding would have —
/// `Binding.State(field.Id, <typed placeholder>)` on a form field,
/// `Binding.Filter(chip.Name, None)` on a filter chip — so its reactive key is
/// the field id on the State channel / the chip name on the Filter channel.
[<RequireQualifiedAccess>]
type private ValueAutoBind =
    | FormField of id: string
    | FilterChip of name: string

let private keysOfFormFieldKind<'Msg>
    (channel: KeyChannel)
    (autoBind: ValueAutoBind)
    (kind: FormFieldKind<'Msg>)
    : string list =
    let keysOfValue (v: Binding<JVal> option) : string list =
        match v with
        | Some b -> keysOfBinding channel b
        | None ->
            match autoBind, channel with
            | ValueAutoBind.FormField id, StateChannel -> [ id ]
            | ValueAutoBind.FilterChip name, FilterChannel -> [ name ]
            | _ -> []

    // The option sources are the only per-case subscriptions: Choice and
    // SegmentedChoice carry a required `options` binding; the combobox
    // (Phase 1113) and the token field (Phase 1121, OPTIONAL `suggestions`)
    // subscribe to theirs too — a Query suggestion feed is the shape those
    // controls exist for. The value slot is the generated projection
    // (Phase 2177), so a new field kind subscribes with no arm to add.
    let optionKeys =
        match kind with
        | FormFieldKind.Choice(opts, _, _)
        | FormFieldKind.SegmentedChoice(opts, _, _, _)
        | FormFieldKind.Combobox(_, _, opts, _) -> keysOfBinding channel opts
        | FormFieldKind.Tokens(_, _, Some suggestions, _) -> keysOfBinding channel suggestions
        | _ -> []

    optionKeys @ keysOfValue (Fuaran.UI.Generated.FormFieldKind.value kind)


/// The single-`HashSet` DFS behind `collectKeys` (Phase 207) — the reactive
/// re-render path walks the whole tree on every frame to derive its
/// subscription set, and the pre-207 walk allocated a `Set` per node and
/// `Set.union`ed them bottom-up, so an N-node tree paid O(N) immutable-set
/// merges to produce one set. This threads ONE mutable accumulator down the
/// walk instead; `collectKeys` seals it into the same immutable `Set` at the
/// top, so every caller's result is unchanged (a `Set` is order-independent, so
/// the DFS order cannot show through).
///
/// Perf primitive: the `HashSet` + `for` shape is deliberate. Do NOT "simplify"
/// it back to a fold over `Set.union` — the result is identical, so no
/// behavioural test can see the regression.
let rec private collectKeysInto<'Msg> (channel: KeyChannel) (acc: HashSet<string>) (node: Node<'Msg>) : unit =
    match node.Accessibility with
    | Some a ->
        for k in keysOfBindingOpt channel a.Label do
            acc.Add k |> ignore

        for k in keysOfBindingOpt channel a.Hidden do
            acc.Add k |> ignore
    | None -> ()

    // Phase 936 — a `StateBehaviour` branch is a wire-encoded child node
    // rendered in place of the body, so a binding inside it is a real reader
    // that must be SUBSCRIBED exactly as the body's readers are. Until this
    // descent existed, such a binding was validated (`BindingWalk` descends
    // since 7052ede) but never subscribed — the `PageStateKey` shape: a write
    // moved the key in State and nothing on screen followed. Subscribing the
    // union of body + branches over-subscribes only while a branch is not
    // showing, and a spurious re-render is a no-op where a missed one is a
    // stale surface. `OnError` is a CLOSURE producing its node at error time;
    // its output is statically invisible to every walk in the estate (it is
    // also not wire-encodable, so decoded trees never carry one).
    match node.State with
    | Some sb ->
        match sb.OnEmpty with
        | Some slot -> collectKeysInto channel acc slot
        | None -> ()

        match sb.OnLoading with
        | Some slot -> collectKeysInto channel acc slot
        | None -> ()
    | None -> ()

    let directKeys, children = kindKeys channel node.Kind

    for k in directKeys do
        acc.Add k |> ignore

    for child in children do
        collectKeysInto channel acc child

/// Every reactive key for `channel` in the tree rooted at `node`. Public: the
/// .NET test runner and the reactive host both pin this walk.
and collectKeys<'Msg> (channel: KeyChannel) (node: Node<'Msg>) : Set<string> =
    let acc = HashSet<string>()
    collectKeysInto channel acc node
    Set.ofSeq acc

/// This node's own (non-descendant) reactive keys for `channel`, paired with its child `Node`s for
/// `collectKeys` to recurse into.
and private kindKeys<'Msg> (channel: KeyChannel) (kind: NodeKind<'Msg>) : string list * Node<'Msg> list =
    match kind with
    // -- Layout --
    | NodeKind.Box s -> keysOfTextOpt channel s.Heading, s.Children
    | NodeKind.SplitPanel s -> [], s.Children
    | NodeKind.SummaryList s -> keysOfTextOpt channel s.Heading, s.Children
    | NodeKind.Stepper s -> keysOfBinding channel s.ActiveStep, s.Children
    | NodeKind.Disclosure s -> (keysOfText channel s.Heading @ keysOfBinding channel s.Open), s.Children
    | NodeKind.Tabs s ->
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
    | NodeKind.Modal s -> (keysOfTextOpt channel s.Heading @ keysOfBinding channel s.Open), s.Children
    | NodeKind.ScrollArea s -> [], s.Children
    // -- Display --
    | NodeKind.Heading h -> keysOfText channel h.Text, []
    | NodeKind.Markdown m -> keysOfText channel m.Text, []
    | NodeKind.Metric k ->
        let __v =
            keysOfText channel k.Label
            @ keysOfBinding channel k.Value
            @ keysOfBindingOpt channel k.Trend
            // Phase 2472 - each further comparison's label and value.
            @ (k.Comparisons
               |> List.collect (fun c -> keysOfText channel c.Label @ keysOfBinding channel c.Value))
            @ keysOfTextOpt channel k.Subtext

        __v, []
    | NodeKind.Badge b -> keysOfText channel b.Label, []
    | NodeKind.Sparkline s -> keysOfBinding channel s.Source, []
    | NodeKind.Callout c -> keysOfTextOpt channel c.Heading @ keysOfText channel c.Body, []
    | NodeKind.Progress p ->
        let __v =
            keysOfBinding channel p.Fraction
            @ keysOfTextOpt channel p.Label
            @ keysOfTextOpt channel p.Caveat

        __v, []
    | NodeKind.Skeleton _ -> [], []
    | NodeKind.Icon _ -> [], []
    | NodeKind.LabelValueRow r ->
        let __v =
            keysOfText channel r.Label
            @ keysOfBinding channel r.Value
            @ keysOfTextOpt channel r.Help

        __v, []
    | NodeKind.Fact fa ->
        let __v =
            keysOfText channel fa.Label
            @ keysOfText channel fa.Value
            @ keysOfTextOpt channel fa.Help

        __v, []
    | NodeKind.Link l -> keysOfBinding channel l.Href @ keysOfText channel l.Label, []
    // Phase 1080 — every `srcSet` candidate's binding is a reactive key too:
    // a candidate resolved from a query re-renders when that query moves, on
    // exactly the terms the primary `src` does. Missing them would leave a
    // responsive image serving stale renditions from a live source.
    //
    // Phase 1079 closes the same omission for `Caption`, which Phase 1078
    // introduced and neither 1078 nor 1080 collected. A caption is a
    // `TextSource`, so it can be `Bound`/`I18n` over live sources exactly as
    // `Alt` can — and `Alt` has always been collected here. The two slots take
    // the same values from the same resolver, so a caption that never
    // re-rendered while the alt text beside it did was a defect of omission
    // rather than a design choice. `keysOfTextOpt` is the existing helper for
    // the optional case (the `FormField.Help` arm above uses it).
    | NodeKind.Image i ->
        keysOfBinding channel i.Src
        @ (i.SrcSet |> List.collect (fun e -> keysOfBinding channel e.Src))
        @ keysOfText channel i.Alt
        @ keysOfTextOpt channel i.Caption,
        []
    // Phase 1076 — `Poster` lives inside the `MediaKind.Video` payload rather
    // than on the spec, so a collector written from the record's field list
    // would miss it and a poster bound to a live source would never re-render.
    // Collected from the case, on the same terms as `Src`.
    | NodeKind.Media m ->
        let kindKeys =
            match m.Kind with
            | MediaKind.Video(_, poster) ->
                match poster with
                | Some p -> keysOfBinding channel p
                | None -> []
            | MediaKind.Audio -> []

        // Phase 1110 - every track carries a bindable `Src` and an i18n-capable
        // `Label`, and the transcript is an optional `TextSource`. All three are
        // collected for the reason the poster is: a slot that resolves from a
        // live source and is not collected is one that never re-renders, and a
        // caption menu frozen at its first values is a defect a reader meets
        // rather than one a test does.
        let trackKeys =
            m.Tracks
            |> List.collect (fun t -> keysOfBinding channel t.Src @ keysOfText channel t.Label)

        keysOfBinding channel m.Src
        @ keysOfText channel m.Label
        @ kindKeys
        @ trackKeys
        @ keysOfTextOpt channel m.Transcript,
        []
    // Phase 1111 — two reactive slots, both on the spec: the document URL and
    // the frame's accessible title. `Permissions` and `AspectRatio` are closed
    // enums with no binding to follow.
    | NodeKind.Embed e -> keysOfBinding channel e.Src @ keysOfText channel e.Title, []
    | NodeKind.List l -> l.Items |> List.collect (keysOfText channel), []
    | NodeKind.Toast t ->
        let __v = keysOfText channel t.Message @ keysOfBinding channel t.Open
        // CodeBlock + Math carry plain strings, not bindings — no reactive keys.

        __v, []
    | NodeKind.CodeBlock _ -> [], []
    | NodeKind.Math _ ->
        let __v = []
        // Phase 524 ships a placeholder render; the reactive DrawStyle-colour keys
        // are wired with the real SVG renderer in Phase 525.

        __v, []
    | NodeKind.Drawing d ->
        // Phase 936 — the reactive slots the analysis walk (`BindingWalk`) has
        // collected since Phase 524 and this walk never gained: the `DrawStyle`
        // colour/width/opacity bindings (recursing `Group` nesting), `Label`
        // text, and the drawing's `Title` / `Description`. The Math arm's old
        // "wired with the real SVG renderer in Phase 525" note never happened —
        // a state-bound fill rendered once and never followed its key.
        let keysOfDrawStyle (st: DrawStyle) =
            keysOfBindingOpt channel st.Fill
            @ keysOfBindingOpt channel st.Stroke
            @ keysOfBindingOpt channel st.StrokeWidth
            @ keysOfBindingOpt channel st.Opacity

        let rec keysOfShape (sh: Shape) =
            match sh with
            | Shape.Group(children, st) -> (children |> List.collect keysOfShape) @ keysOfDrawStyle st
            | Shape.Rectangle(_, _, _, _, _, st) -> keysOfDrawStyle st
            | Shape.Line(_, _, _, _, st) -> keysOfDrawStyle st
            | Shape.Polyline(_, st) -> keysOfDrawStyle st
            | Shape.Polygon(_, st) -> keysOfDrawStyle st
            | Shape.Curve(_, st) -> keysOfDrawStyle st
            | Shape.Circle(_, _, _, st) -> keysOfDrawStyle st
            | Shape.Ellipse(_, _, _, _, st) -> keysOfDrawStyle st
            | Shape.Label(_, _, text, st) -> keysOfText channel text @ keysOfDrawStyle st

        let __v =
            keysOfDrawStyle d.Style
            @ (d.Shapes |> List.collect keysOfShape)
            @ keysOfTextOpt channel d.Title
            @ keysOfTextOpt channel d.Description

        __v, []
    // -- Input --
    | NodeKind.Button b ->
        let __v =
            keysOfText channel b.Label
            @ keysOfTextOpt channel b.Tooltip
            @ keysOfBindingOpt channel b.Disabled

        __v, []
    | NodeKind.FileUpload fu -> keysOfText channel fu.Label @ keysOfBindingOpt channel fu.Disabled, []
    | NodeKind.Select s ->
        let __v =
            keysOfText channel s.Label
            @ keysOfBinding channel s.Source
            @ keysOfBindingOpt channel s.Value
            // Phase 291 — the multi-select value binding (Some only in multi mode).
            @ keysOfBindingOpt channel s.Values
            @ keysOfTextOpt channel s.Placeholder
            @ keysOfBindingOpt channel s.Disabled

        __v, []
    | NodeKind.Form f ->
        let __v =
            let fieldKeys =
                f.Fields
                |> List.collect (fun field ->
                    keysOfText channel field.Label
                    @ keysOfTextOpt channel field.Help
                    @ keysOfFormFieldKind channel (ValueAutoBind.FormField field.Id) field.Kind)

            keysOfText channel f.SubmitLabel
            @ keysOfBindingOpt channel f.Disabled
            @ fieldKeys

        __v, []
    | NodeKind.Filters filters ->
        let __v =
            filters.Items
            |> List.collect (fun (fs: FilterSpec<'Msg>) ->
                keysOfText channel fs.Label
                @ keysOfFormFieldKind channel (ValueAutoBind.FilterChip fs.Name) fs.Kind)

        __v, []
    // Phase 1120 — the tree subscribes BOTH State keys, on the grid's
    // `sortStateKey` argument exactly: a row toggle writes the expanded set and
    // a selection writes the selected id, and the tree READS both, so without
    // these two lines a reader would press Right, the state would move, and
    // nothing on screen would follow it. Its labels subscribe on their own
    // account, recursively — a bound label deep in a closed branch still
    // becomes live the moment its branch opens.
    | NodeKind.Tree t ->
        let rec labelKeys (items: TreeItem list) =
            items
            |> List.collect (fun i -> keysOfText channel i.Label @ labelKeys i.Children)

        let stateKeys =
            match channel with
            | StateChannel -> [ t.ExpandedStateKey; t.SelectionStateKey ] |> List.choose id
            | _ -> []

        (labelKeys t.Items @ stateKeys), []
    // -- Vis --
    | NodeKind.DataGrid g ->
        let __v =
            keysOfBinding channel g.Source
            // Phase 818 — a `sortStateKey` grid reads its sort descriptor off
            // the State channel, so it subscribes that key: a header click's
            // descriptor write re-renders (and re-sorts) the grid.
            @ (match g.SortStateKey, channel with
               | Some k, StateChannel -> [ k ]
               | _ -> [])
            // The identical argument for `pageStateKey` (Phase 862), which this
            // arm simply never gained: `renderGrid` reads the requested page off
            // the State channel via `BindingResolver.readPageDescriptor`, so a
            // pager click's write must re-render the grid or the page position
            // moves in State and nothing on screen follows it.
            //
            // `editStateKey` is deliberately NOT here, and the asymmetry is the
            // point: it is a write DESTINATION with no reader anywhere in the
            // renderer (Phase 932 established this) — an edit COMMITS there. A
            // tree that shows its edits does so because the author points the
            // grid's `source` at the same key, and THAT read is subscribed
            // through `source`, on this arm's first line.
            @ (match g.PageStateKey, channel with
               | Some k, StateChannel -> [ k ]
               | _ -> [])
            // Phase 1892 — the window descriptor, on the same argument: the
            // viewport's write must re-render the grid on the new window. The
            // declared total is a binding the grid resolves, so it subscribes
            // through the binding's own keys.
            @ (match g.WindowStateKey, channel with
               | Some k, StateChannel -> [ k ]
               | _ -> [])
            @ (match g.RowTotal with
               | Some total -> keysOfBinding channel total
               | None -> [])
            @ (match g.StaticRows with
               | Some sr ->
                   (sr.Headers |> List.collect (keysOfText channel))
                   @ (sr.Rows |> List.collect (List.collect (keysOfText channel)))
               | None -> [])

        __v, []
    | NodeKind.Chart c -> keysOfBinding channel c.Source @ keysOfTextOpt channel c.Title, []
    | NodeKind.Map m -> keysOfBinding channel m.Source, []
    | NodeKind.ErrorBoundary spec -> [], [ spec.Child; spec.Fallback ]
    | NodeKind.Switch spec ->
        // Switch reads its StateKey off the state channel, so it registers that
        // key here (Phase 392): `renderStateReactive` subscribes the surface to
        // it, so a global `Action.SetState` re-renders and Switch re-selects the
        // matching case. Off the state channel the key contributes nothing. The
        // case children + default recurse for their own reactive keys.
        // Phase 768 — the selector is any Binding, so the reactive walk defers
        // to keysOfBinding: a State selector still subscribes its key on the
        // state channel (the Phase 392 behaviour, unchanged), and a Filter
        // selector now subscribes its name on the filter channel.
        let ownKeys = keysOfBinding channel spec.On

        ownKeys, (spec.Cases |> List.map _.Child) @ [ spec.Default ]
    | NodeKind.FragmentDecl spec -> [], [ spec.Body ]
    | NodeKind.FragmentRef _ -> [], []
    // Custom props are JVal literals, not bindings — no reactive keys.
    | NodeKind.Custom _ -> [], []
    // Mount (§4o) — the guest owns its own scoped state store; its keys
    // live in the guest scope, not the host binding registry, so the
    // boundary contributes no host keys and no host children to recurse.
    | NodeKind.Mount _ -> [], []

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

/// The named fragment-expansion primitive (Phase 1151): the fragment `body`
/// with its interior ids namespaced under `prefix`, served from the
/// process-global `FragmentExpansion` cache when that pair has been expanded
/// before.
///
/// The cache is BORROWED, not threaded — nothing is added to `RenderContext`,
/// which is built fresh at every entry point and could therefore never own a
/// memo that hits. It is keyed on the BODY INSTANCE rather than the fragment
/// name, so a differently-declared tree reusing a name can never be served
/// another tree's expansion; `FragmentExpansion`'s header carries the full
/// lifetime, bound and concurrency posture.
///
/// Named, and public, for the Phase 207 reason: the memo is output-identical, so
/// no behavioural test can see it removed. A named call site is what a cleanup
/// pass reads as intent rather than as noise to tidy away, and it is what the
/// soundness suite and the render-allocation micro-case measure.
let expandFragment (prefix: string) (body: Node<'Msg>) : Node<'Msg> =
    FragmentExpansion.expandNamespaced prefix body

/// The same expansion with NO memo — the id-rewriting walk itself.
///
/// Public for two consumers that need the uncached form to say anything true
/// about the cached one: the soundness suite, which proves the cached and
/// recomputed trees are byte-identical (a claim it cannot make with only the
/// cached path in reach), and the render-allocation micro-case, which measures
/// the cost the memo removes. The renderer itself always goes through
/// `expandFragment`, and the source-shape guard in the soundness suite pins that.
let expandFragmentUncached (prefix: string) (body: Node<'Msg>) : Node<'Msg> =
    FragmentExpansion.namespaceIds prefix body

// ─── Declared field rules (`FormField.Rule`, Phase 864) ────────────────────
//
// WIRE_FORMAT splits the obligation a declared rule carries by host class.
// This renderer is a RENDERING HOST — it draws the form's submit affordance —
// so it MUST NOT submit while a declared rule is unmet, and MUST make visible
// which field is unmet. It is NOT a security boundary: client enforcement is
// an affordance, and a host that accepts submissions re-checks every declared
// constraint server-side (`Fuaran.UI.ServerDriven.FormValidation` is that
// floor on the server-driven tier).
//
// The work splits between the DOM and this module:
//
//   • `format` / `pattern` / `minLength` / `maxLength` project into the
//     platform's OWN constraint attributes on the control (`type`, `pattern`,
//     `minlength`, `maxlength`) — the same attributes the static/SSR emitter
//     writes, so browser, static projection and native surface agree without a
//     second definition of the rule.
//   • `compare` has NO HTML equivalent, which the specification states out
//     loud. It is emitted as a `data-fuaran-field-compare` DECLARATION so a
//     reader can see it was not silently dropped, and enforced by the submit
//     gate in `renderForm`. **RECORDED KNOWN LIMIT: that attribute is a
//     declaration, not a constraint — no platform machinery reads it.**
//
// `pattern` is deliberately NOT evaluated in F# here. This file is
// Fable-compiled and uses `System.Text.RegularExpressions` nowhere; the
// specification chose HTML `pattern` semantics (ECMA-262 source, implicitly
// anchored to the whole value) precisely so the browser's own check is the one
// definition. Native constraint validation runs BEFORE the `submit` event is
// dispatched at all, so an unmet `pattern` never reaches the gate below — the
// attribute is emitted and the browser enforces it.

[<RequireQualifiedAccess>]
module private FieldRules =

    module Shared = Fuaran.UI.Renderer.FieldRules

    /// The client's value adapter: a harvested `JVal` as the shape a rule reads.
    let private ruleValue (v: JVal) : Shared.RuleValue =
        match v with
        | JStr s -> Shared.RuleValue.Text s
        | JInt i -> Shared.RuleValue.Number(float i)
        | JFloat f -> Shared.RuleValue.Number f
        | JBool b -> Shared.RuleValue.Flag b
        | _ -> Shared.RuleValue.Other

    /// The first unmet slot of one field's declared rule, as its message.
    /// `values` is the form's own submit harvest (`SubmitPayload.harvestFields`
    /// keyed by field id) — the same values the submit would carry, read
    /// exactly as this renderer's own controls read them. `pattern` is left to
    /// the browser (see above), so no pattern predicate is passed.
    let unmet (resolveText: TextSource -> string) (values: Map<string, JVal>) (field: FormField<'Msg>) : string option =
        Shared.unmet None (resolveText >> Some) (fun id -> Map.tryFind id values |> Option.map ruleValue) field

    /// The platform's own constraint attributes for a declared rule.
    /// `withPattern` is false on controls where HTML's `pattern` does not apply
    /// (`<textarea>` has no `pattern` attribute), so the emitter never writes an
    /// attribute the platform ignores.
    let constraintAttrs (withPattern: bool) (rule: FieldRule option) : IReactProperty list =
        match rule with
        | None -> []
        | Some r ->
            [ if withPattern then
                  match r.Pattern with
                  | Some p -> prop.pattern p
                  | None -> ()
              match r.MinLength with
              | Some n -> prop.minLength n
              | None -> ()
              match r.MaxLength with
              | Some n -> prop.maxLength n
              | None -> ()
              // RECORDED KNOWN LIMIT — `compare` has no HTML equivalent, and
              // faking one would imply coverage the platform does not provide.
              // The declaration is emitted so it is visible in the DOM and so
              // the submit gate's decision is inspectable; the gate, not this
              // attribute, is what enforces it.
              match r.Compare with
              | Some c -> prop.custom ("data-fuaran-field-compare", Shared.compareDeclaration c)
              | None -> () ]

    /// Mark / clear the unmet fields in the DOM. `data-fuaran-field-error` is
    /// the SAME marker the server-driven tier's field-error patches use
    /// (`FormValidation.lower`), so a host's existing CSS hook surfaces a
    /// client-gate failure and a server-validation failure identically — no new
    /// class vocabulary, which is parity-locked with the TS renderer. The
    /// `aria-invalid` companion is what makes the unmet field visible to a
    /// screen reader, and the first unmet field is focused so the failure is
    /// visible without any host styling at all.
    let markUnmet (errors: (string * string) list) (allFieldIds: string list) : unit =
#if FABLE_COMPILER
        let errorById = Map.ofList errors

        for fieldId in allFieldIds do
            let el = Browser.Dom.document.getElementById fieldId

            if not (isNull el) then
                match Map.tryFind fieldId errorById with
                | Some message ->
                    el.setAttribute ("data-fuaran-field-error", message)
                    el.setAttribute ("aria-invalid", "true")
                | None ->
                    el.removeAttribute "data-fuaran-field-error"
                    el.removeAttribute "aria-invalid"

        match errors with
        | (firstId, _) :: _ ->
            let el = Browser.Dom.document.getElementById firstId

            if not (isNull el) then
                el.focus ()
        | [] -> ()
#else
        // .NET-side no-op — `Browser.Dom` is meaningless under `dotnet build`
        // (the same posture the tab-focus helper takes).
        ignore errors
        ignore allFieldIds
#endif

// ─── Per-Kind body renderers ───────────────────────────────────────────────

// ─── The render walk's depth bound (Phase 2041) ────────────────────────────
//
// The server renderer has bounded its walk at `WireLimits.MaxDepth` since
// Phase 781; this renderer had no bound, so a tree the server truncated was
// rendered in full here, and a hydrating client disagreed with the markup it
// was handed. The bound lives on `render` (the one function every nested node
// passes through) as a per-walk counter rather than a `RenderContext` field:
// widening that public record is a breaking change for every host that
// constructs one, and the depth is a property of the walk, not of the host's
// configuration. A walk is synchronous and runs on one thread, so the counter
// is thread-local on .NET (two hosts rendering on two threads never see each
// other's depth) and a plain cell under Fable (one event loop). `render`
// restores it in a `finally`, so a throw caught by a guard or a boundary
// leaves it exactly where the catching frame expects.

[<RequireQualifiedAccess>]
module private RenderDepth =
#if FABLE_COMPILER
    let mutable private cell = 0
    let current () : int = cell
    let set (depth: int) : unit = cell <- depth
#else
    let private cell = new System.Threading.ThreadLocal<int>(fun () -> 0)
    let current () : int = cell.Value
    let set (depth: int) : unit = cell.Value <- depth
#endif


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

// ─── Host-pluggable guest capability seam (§4o) ─────────────────────────────
//
// The `Mount` arm below renders a guest under its own scope, but it handed the
// guest the HOST runtime and bridged the guest's dispatch through the mount's
// `OnBubble` UNWRAPPED — so a capability gate was enforced only where an
// orchestration tier authored the mount itself or drove the guest's dispatch
// loop, never on the raw render path. A rendered guest was therefore not
// default-deny.
//
// The fix cannot be a direct dependency: the language tier must not reference
// the orchestration tier that owns the policy. So the gate arrives as a
// language-tier-resident hook the host installs, mirroring `renderGuestHook`
// above — a module-level `mutable` holding an `option`, consulted at the Mount
// arm, and `None` by default so a host that installs nothing renders exactly as
// it did before.

/// The non-generic policy surface of a `NodeKind.Mount`, handed to the
/// host-installed `GuestSeam`. Deliberately NOT the `MountSpec<'Msg>` itself:
/// the seam is a single function value shared by every `'Msg` instantiation of
/// the renderer (the same constraint that makes `renderGuestHook` `obj`-typed),
/// and a capability policy needs the mount's identity, its declared
/// capabilities, and its channel — none of which are `'Msg`-typed.
type GuestSeamContext =
    {
        /// The guest's runtime scope id (`MountSpec.ScopeId`).
        ScopeId: string
        /// The capabilities the mount DECLARES for the guest
        /// (`MountSpec.Capabilities`). A gate reads these as a *request*, not a
        /// grant — deciding what to allow is the host policy's job.
        Capabilities: string list
        /// The guest channel as the renderer will ACTUALLY honour it (Phase
        /// 783). Its `Direction` is `OutOnly` unless the tree declared `TwoWay`
        /// AND the seam granted it via `GrantTwoWay` (Phase 2135) — never simply
        /// what the tree declared, and never more than it declared.
        Channel: GuestChannel
        /// What the TREE asked for (Phase 783). `ChannelDirection` is a required
        /// wire field, so a hostile tree simply writes `TwoWay`; `OutOnly` was
        /// only ever the default of the *authoring* smart constructor, and no
        /// host-side clamp existed. The renderer now clamps, and hands the
        /// policy both values: `Channel` is what will happen, this is what was
        /// requested. A gate deciding `GrantTwoWay` reads this one; it is only
        /// ever consulted when this is `TwoWay` (Phase 2135).
        DeclaredDirection: ChannelDirection
    }

/// Host-pluggable capability seam for rendered `Mount` guests (§4o).
///
/// Installed once by the host via `installGuestSeam`; the `Mount` arm consults
/// it for **every** guest it renders, so a guest reached through the plain
/// render path is gated by the same policy as one an orchestration tier mounts
/// and drives itself.
///
/// - `WrapRuntime ctx hostRuntime` returns the `IFuaranRuntime` the guest gets.
///   A capability-scoping host returns a restricted runtime (deny the effects
///   the mount did not declare); returning `hostRuntime` unchanged is the
///   identity policy.
/// - `GateBubble ctx rawBubble` returns the function the guest's dispatches run
///   through. `rawBubble` is the renderer's own bridge — it runs the mount's
///   `OnBubble` in the HOST context (or does nothing when no bubble channel is
///   wired). A gate may drop, transform, or pass through; returning `rawBubble`
///   unchanged is the identity policy.
///
/// Both members are consulted per rendered mount, not per host, so a policy may
/// vary by scope id / capability set without reinstalling.
/// - `GrantTwoWay ctx` decides whether a mount that DECLARED `TwoWay` gets it
///   (Phase 783). It is consulted only for such a mount (Phase 2135): an
///   `OutOnly` declaration is never upgraded, whatever the seam would answer —
///   two-way needs BOTH the declaration and the grant. `ctx.DeclaredDirection`
///   is what the tree asked for and `ctx.Channel` is the clamped `OutOnly` the
///   renderer will otherwise use.
///   `TwoWay` is a HOST GRANT: the wire cannot confer it, because
///   `ChannelDirection` is a required wire field a decoded tree fills in itself.
///   `fun _ -> false` is the safe policy and the one to write unless a specific
///   mount genuinely needs host→guest push.
type GuestSeam =
    { WrapRuntime: GuestSeamContext -> Runtime.IFuaranRuntime -> Runtime.IFuaranRuntime
      GateBubble: GuestSeamContext -> (obj -> unit) -> (obj -> unit)
      GrantTwoWay: GuestSeamContext -> bool }

/// The installed guest seam. `None` is the default, and since Phase 783 it means
/// the guest is UNPRIVILEGED: it receives a `Runtime.UnprivilegedGuestRuntime`
/// (every capability refused, refusals recorded) and an `OutOnly` channel. It
/// previously meant the guest received the HOST's runtime unwrapped, which is
/// the opposite of what "no policy installed" should mean.
/// Private + set through `installGuestSeam` / `clearGuestSeam` because the
/// installing host lives in another assembly, which is the one way this differs
/// from `renderGuestHook` (set once, in-module, at init).
let mutable private guestSeam: GuestSeam option = None

/// Install the host's guest capability policy. Idempotent-by-replacement: a
/// second call replaces the first (there is one policy per process, as there is
/// one `StateStore`). Call before the first render that may reach a `Mount`.
let installGuestSeam (seam: GuestSeam) : unit = guestSeam <- Some seam

/// Remove any installed guest seam, restoring the ungated default. Exists for
/// host teardown and for test isolation — a test that installs a seam must clear
/// it, or the policy bleeds into every later case in the process.
let clearGuestSeam () : unit = guestSeam <- None

/// The currently-installed guest seam, if any. Read-only introspection for a
/// host that needs to know whether it (or another component) already installed
/// one before replacing it.
let currentGuestSeam () : GuestSeam option = guestSeam

/// The SELECTION DECISION for one already-identified chart datum: dispatch the
/// author's closure, or publish under the node's own id.
///
/// Held apart from `chartPointClick` because there are TWO ways a click arrives
/// at this decision and only one of them has a mark id. The first-party lowered
/// path emits raw SVG and recovers the row from `data-fuaran-mark`; an
/// `IVisualisationAdapter` (AG Charts' `listeners.nodeClick`) is handed the row
/// directly. Both must decide the same thing — the adapter path spent 933
/// deciding nothing at all — so the decision lives in one place and the
/// row-identification stays with whoever has to do it.
let chartPointSelected<'Msg>
    (dispatch: Action<'Msg> -> unit)
    (nodeId: string)
    (spec: ChartSpec<'Msg>)
    (row: Row)
    : unit =
    match spec.OnPointClick with
    // CLOSURE WINS — Phase 427's rule, unchanged. A host that supplied
    // `OnPointClick` in-process dispatches exactly as before and never touches
    // the store. The default below is for the DECODED case, where the slot
    // arrives `None` because a callback cannot cross the wire
    // (`WireSurvivability`: `NodeKind.Chart -> None // OnPointClick selection
    // host-only`) — which is why every decoded chart's point click was inert
    // before Phase 933, and why it stayed inert on the adapter path after it.
    | Some f -> dispatch (f row)
    // THE UNGATED WRITE, and why it is acceptable — recorded at the decision
    // rather than at either call site, because a later reader will otherwise
    // read the asymmetry as a defect (866).
    //
    // This calls `SelectionStore.set` DIRECTLY rather than going through
    // `runAction`, so it is not subject to the gate every dispatched `Action`
    // passes. The charter judged that acceptable on ONE property: a node may
    // publish only under its OWN `NodeId`. Unlike `SetState`, whose key space is
    // SHARED — an ungated write there could clobber a key belonging to any other
    // node — the blast radius here is the chart itself, and the worst a
    // malformed chart can do is misreport its own selection. The identical
    // property is what makes 427's grid write safe; it is a property of the
    // DESTINATION, not of who is writing.
    | None -> SelectionStore.set nodeId (box row)

/// Phase 427's grid twin of the decision above, factored for the same reason:
/// the first-party table wires it on each `<tr>`, an `IVisualisationAdapter`
/// wires it on AG Grid's `onRowClicked`, and the two had drifted — the adapter
/// dispatched the closure and dropped the default on the floor.
let gridRowSelected<'Msg> (dispatch: Action<'Msg> -> unit) (nodeId: string) (spec: GridSpec<'Msg>) (row: Row) : unit =
    match spec.OnRowClick with
    | Some f -> dispatch (f row)
    | None -> SelectionStore.set nodeId (box row)

/// Phase 933 — the chart point-click DECISION, lifted out of the DOM handler in
/// `renderChart` so the behaviour is reachable without a browser.
///
/// The handler around it is irreducibly Fable-side (it reads `event.target` and
/// walks to the nearest `data-fuaran-mark`); everything that decides what a
/// point click MEANS is here, and is what the Phase 933 tests exercise.
///
/// `dispatch` is the caller's `runAction ctx` — passed as a function rather than
/// taking the `RenderContext` so this stays independent of the render loop.
let chartPointClick<'Msg>
    (dispatch: Action<'Msg> -> unit)
    (nodeId: string)
    (spec: ChartSpec<'Msg>)
    (rows: Row list)
    (markId: string)
    : unit =
    match Fuaran.UI.Charts.rowOfMarkId spec rows markId with
    // A mark that resolves to no row: a SERIES-level mark (Line / Area draw one
    // polyline per series, so there is no datum under the pointer), or data that
    // has moved on since the SVG was painted. Both are no-ops, never a guess.
    | None -> ()
    | Some row -> chartPointSelected dispatch nodeId spec row

/// Phase 934 / Phase 1123 — the in-flight row move: which grid released it,
/// from which ABSOLUTE index, and (since 1123) everything a DIFFERENT grid
/// needs to complete the move without reaching into the source's bindings —
/// the row's identity, the row value to insert, the transfer key it travels on,
/// and the source's own removal commit.
///
/// Module-level because the state must survive between event handlers on two
/// independent elements, and — this is what 1123 added — between elements
/// belonging to two independent grids. It carries the KEYBOARD lift as well as
/// the pointer drag: the two routes reach the same drop logic, which is why
/// there is one holder and not two.
///
/// Keyed by the source grid's own id, so a within-grid drop is a REORDER (Phase
/// 934's path) and a cross-grid one is a TRANSFER, decided by comparing ids
/// rather than by two different drag channels.
///
/// Client-only by construction — SSR renders the handle, but no event ever
/// fires there.
let mutable private gridDragSource: GridTransfer.Lifted<Map<string, obj>> option =
    None

/// Phase 1123 — write a live-region line for one grid's transfer status.
/// Imperative because the renderer holds no hooks and nothing about a lift is
/// part of the document: the region is rendered EMPTY by both pipelines, so the
/// server and the client's first render agree, and only a reader's own gesture
/// ever puts text in it. A missing element (SSR, or a grid that declares no
/// transfer) is a silent no-op rather than a throw.
let private announceTransfer (nodeId: string) (message: string) : unit =
    let el = Browser.Dom.document.getElementById (nodeId + "-fuaran-drag-status")

    if not (isNull (box el)) then
        el.textContent <- message

// ─── The grid window viewport (Phase 1911) ─────────────────────────────────
//
// The interactive half of `windowStateKey` on the reference host. Phase 1892
// gave this renderer the READ — `renderGrid` presents the window the key holds
// and subscribes to the key — but nothing here ever WROTE it, so an F# client
// showed the first window forever. A grid declaring the key now renders inside
// a vertical scroll container that measures the visible rows on mount, on every
// render, on scroll and on resize, and writes `{"offset", "count"}` through the
// same `SetState` route the pager's `{"page": N}` takes. The measurement and the
// dedupe are `GridModel.stepWindowWriter` — pure, shared with the .NET
// tests, and parity-locked with the TS renderer's `GridWindowViewport` — so an
// unchanged window is never written and a write that re-renders cannot loop.
//
// The scroll extent of the whole range is kept by a spacer row before the
// window and one after it (none after where the total is unknown), so the
// scrollbar describes the set rather than the window. No `fuaran-*` class is
// minted: the container and spacers are structure, styled inline, exactly as
// the TS viewport styles them. A grid declaring no window key never reaches
// this component and emits byte-identical DOM to before; on .NET (no React) the
// call site renders the table without the viewport and writes nothing, which
// is the static-host rule.
//
// This component holds hooks, which the rest of this file deliberately does
// not; it is mounted through React's own `createElement` for the reason
// `ComboboxControl` and `PopoverSurface` are — a stable module-level function
// component, so its refs survive re-renders.
//
// Finding (Phase 1911, recorded where the next reader meets it): the phase's
// fourth task asked for "the F# client's window row" in `render-fidelity.json`
// to move from "reads" to "reads and writes". The manifest carries no such row
// — it declares per-NodeKind source / fallback / client-rich tiers, render
// obligations, intrinsic ARIA and speech rulings, and nothing about which host
// reads or writes a state key — so there was no row to move, and inventing a
// host-capability axis in that generated artefact is a wire-contract change,
// not a fidelity update.

/// Measure a grid viewport: `[| scrollTop; headHeight; rowHeight; clientHeight |]`,
/// a `0` wherever the element is missing or has no layout (the pure measure
/// then falls back as the TS viewport does).
[<Fable.Core.Emit("""(function(el){
  if (el == null) return [0, 0, 0, 0];
  var row = el.querySelector('tbody > tr.fuaran-grid-row');
  var head = el.querySelector('thead');
  return [
    el.scrollTop || 0,
    head != null ? head.getBoundingClientRect().height : 0,
    row != null ? row.getBoundingClientRect().height : 0,
    el.clientHeight || 0
  ];
})($0)""")>]
let private measureGridViewport (el: obj) : float array = Fable.Core.Util.jsNative

/// Re-measure when the viewport is resized (the visible count changes). A host
/// without `ResizeObserver` (an older test DOM) keeps the scroll and render
/// triggers and loses only this one.
[<Fable.Core.Emit("""(function(el, onResize){
  if (el == null || typeof ResizeObserver === 'undefined') return function(){};
  var observer = new ResizeObserver(function(){ onResize(); });
  observer.observe(el);
  return function(){ observer.disconnect(); };
})($0, $1)""")>]
let private observeViewportResize (el: obj) (onResize: unit -> unit) : unit -> unit = Fable.Core.Util.jsNative

[<Fable.Core.Import("createElement", "react")>]
let private createViewportElement (componentFn: obj) (props: obj) : ReactElement = Fable.Core.Util.jsNative

type private GridWindowViewportProps =
    {| offset: int
       presentedCount: int
       total: int option
       colSpan: int
       windowKey: string
       current: GridModel.RowWindow option
       write: string -> JVal -> unit
       renderTable: ReactElement list -> ReactElement list -> ReactElement |}

let private renderGridWindowViewport (props: GridWindowViewportProps) : ReactElement =
    let viewportRef = React.useElementRef ()

    let rowHeight, setRowHeight = React.useState GridModel.defaultWindowRowHeightPx

    let lastWritten = React.useRef (None: GridModel.RowWindow option)

    let measure () =
        match viewportRef.current with
        | None -> ()
        | Some el ->
            let m = measureGridViewport (box el)
            let measured = m[2]

            if measured > 0.0 && measured <> rowHeight then
                setRowHeight measured

            let written, write =
                GridModel.stepWindowWriter
                    (Some props.windowKey)
                    props.current
                    lastWritten.current
                    { ScrollTop = m[0]
                      HeaderHeight = m[1]
                      RowHeight = (if measured > 0.0 then measured else rowHeight)
                      ViewportHeight = m[3] }

            lastWritten.current <- written
            write |> Option.iter (fun (key, descriptor) -> props.write key descriptor)

    // The latest closure, so the scroll handler and the resize observer (bound
    // once) always measure against this render's `current`.
    let measureRef = React.useRef measure
    measureRef.current <- measure

    // After every render: a re-render with an unchanged viewport writes nothing.
    React.useEffect (fun () -> measureRef.current ())

    // A resized viewport changes the visible count.
    React.useEffect (
        (fun () ->
            match viewportRef.current with
            | None -> (fun () -> ())
            | Some el -> observeViewportResize (box el) (fun () -> measureRef.current ())),
        [||]
    )

    let spacer (rows: int) : ReactElement list =
        if rows > 0 then
            [ Html.tr
                  [ prop.custom ("aria-hidden", "true")
                    prop.style [ style.custom ("height", string (float rows * rowHeight) + "px") ]
                    prop.children [ Html.td [ prop.colSpan props.colSpan ] ] ] ]
        else
            []

    let trailing =
        match props.total with
        | Some total -> max 0 (total - props.offset - props.presentedCount)
        | None -> 0

    Html.div
        [ prop.ref viewportRef
          prop.style
              [ style.custom ("overflowY", "auto")
                style.custom ("maxHeight", string GridModel.windowViewportMaxHeightPx + "px") ]
          prop.onScroll (fun _ -> measureRef.current ())
          prop.children [ props.renderTable (spacer props.offset) (spacer trailing) ] ]

/// The per-kind dispatch. Phase 2108 / 2278 — a planned kind is ONE behaviour
/// emitted twice: its arm is a call to the shared `ElementPlan` planner (the
/// display, layout and media kinds) plus, on the client, the resolver closure
/// that binds the plan's named attachment points. The six STRUCTURAL arms —
/// `ErrorBoundary`, `Switch`, `Custom`, `FragmentDecl`, `FragmentRef`, `Mount` —
/// stay here by nature (Phase 2278 ruling): they are walk control, deciding
/// which node renders and how (error suspension, case selection, fragment
/// expansion with cycle tracking, host renderer registries, guest loaders),
/// and the walk is each renderer's own; the leaf markup they emit is
/// `ElementPlan.StructuralPlan`.
///
/// Phase 2290 — the input and visualisation kinds are planned too
/// (`ElementPlan.InputPlan` / `VisPlan`). Their WIDGET controls are this
/// renderer's named by-nature arms: the Combobox, Tokens and writable Rating
/// controls (field and filter chip), a `Binding.Local` Text / Number /
/// RangedNumber field, the FileUpload drop zone and stream shell, the bound
/// DataGrid (`renderGrid`), and a Chart a host VisAdapter draws. Each is a
/// stateful React component rendered in place of the plan's no-script FLOOR,
/// which is what the server serves.
let rec private renderKind
    (ctx: RenderContext<'Msg>)
    (parentNodeId: string)
    (state: StateBehaviour<'Msg>)
    (kind: NodeKind<'Msg>)
    // Phase 951 — the node's a11y projection, for the kinds that carry it on
    // their own semantic element. `[]` for every other kind, which is every arm
    // below except `Link` / `Button` / `Image`. See
    // `Accessibility.forwardsToSemanticElement` + `docs/DECISIONS.md` D4.
    (semanticAttrs: (string * string) list)
    : ReactElement =
    match kind with
    // Phase 2278 — the layout kinds are `ElementPlan.LayoutPlan` values; the
    // handler-bearing ones bind their named attachment points here.
    | NodeKind.Box spec -> Emit.plan (ElementPlan.LayoutPlan.box (planContext ctx parentNodeId) spec)
    | NodeKind.SplitPanel spec -> Emit.plan (ElementPlan.LayoutPlan.splitPanel (planContext ctx parentNodeId) spec)
    | NodeKind.Tabs spec ->
        let model = ElementPlan.TabsModel.ofSpec ctx.Sources spec

        // Selection: a declared `OnSelect` action wins, else the write-back to
        // `ActiveIndex`; the tag channel follows the same rule.
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

        // Roving focus follows the selection (WAI-ARIA tabs pattern).
        let focusTab (i: int) : unit =
#if FABLE_COMPILER
            let el = Browser.Dom.document.getElementById (Ids.tab parentNodeId i)

            if not (isNull el) then
                el.focus ()
#else
            ignore i
#endif

        Emit.planWith
            (fun point key ->
                match point with
                | ElementPlan.Attach.TabSelect -> [ prop.onClick (fun _ -> dispatchTabIndex (int key)) ]
                | ElementPlan.Attach.TabsKeyDown ->
                    [ prop.onKeyDown (fun (e: Browser.Types.KeyboardEvent) ->
                          match ElementPlan.TabsModel.keyTarget model e.key with
                          | Some i ->
                              e.preventDefault ()
                              dispatchTabIndex i
                              focusTab i
                          | None -> ()) ]
                | _ -> [])
            (ElementPlan.LayoutPlan.tabs (planContext ctx parentNodeId) spec model)
    | NodeKind.SummaryList spec -> Emit.plan (ElementPlan.LayoutPlan.summaryList (planContext ctx parentNodeId) spec)
    | NodeKind.Disclosure spec ->
        Emit.planWith
            (fun point _ ->
                match point with
                | ElementPlan.Attach.DisclosureToggle ->
                    [ prop.custom (
                          "onToggle",
                          System.Func<Browser.Types.Event, unit>(fun (e: Browser.Types.Event) ->
                              let target = e.target :?> Browser.Types.HTMLElement
                              let isOpen = not (isNull (target.getAttribute "open"))

                              match spec.OnToggle with
                              | Some onToggle -> runAction ctx (onToggle isOpen)
                              | None -> writeBackTo ctx spec.Open (Some(box isOpen)))
                      ) ]
                | _ -> [])
            (ElementPlan.LayoutPlan.disclosure (planContext ctx parentNodeId) spec)
    | NodeKind.Stepper spec ->
        Emit.planWith
            (fun point key ->
                match point, spec.OnSelect with
                | ElementPlan.Attach.StepSelect, Some onSelect ->
                    [ prop.onClick (fun _ -> runAction ctx (onSelect (int key))) ]
                | _ -> [])
            (ElementPlan.LayoutPlan.stepper (planContext ctx parentNodeId) spec)
    | NodeKind.Modal spec ->
        let pc = planContext ctx parentNodeId

        let dismiss () =
            match spec.OnDismiss with
            | Some action -> runAction ctx action
            | None -> writeBackTo ctx spec.Open (Some(box false))

        let attach (point: string) (_: string) : IReactProperty list =
            match point with
            | ElementPlan.Attach.ModalDismiss
            | ElementPlan.Attach.ModalOverlay -> [ prop.onClick (fun _ -> dismiss ()) ]
            | _ -> []

        if spec.Modality = ModalityKind.Popover then
            // The popover's outer element is its positioning component (anchor
            // placement, light dismiss — see `PopoverSurface.fs`), which writes
            // the plan's outer attributes itself; the dialog surface is planned.
            PopoverSurface.surface
                {| className = Css.Class.Popover
                   hidden = not (ElementPlan.LayoutPlan.modalOpen ctx.Sources spec)
                   anchor = spec.Anchor
                   dismissable = spec.Dismissable
                   onDismiss = dismiss
                   children = [ Emit.planWith attach (ElementPlan.LayoutPlan.popoverDialog pc spec) ] |}
        else
            Emit.planWith attach (ElementPlan.LayoutPlan.modal pc spec)
    | NodeKind.ScrollArea spec -> Emit.plan (ElementPlan.LayoutPlan.scrollArea (planContext ctx parentNodeId) spec)
    | NodeKind.Heading spec -> Emit.plan (ElementPlan.DisplayPlan.heading ctx.Sources spec)
    | NodeKind.Markdown spec -> Emit.plan (ElementPlan.DisplayPlan.markdown ctx.Policy.Egress ctx.Sources spec)
    | NodeKind.Metric spec ->
        // Phase 632 — a scalar slot: a `Binding.Transform` resolves to its 1×1
        // result cell.
        let resolution = BindingResolver.resolveScalarFloat ctx.Sources spec.Value

        match ElementPlan.DisplayPlan.stateSlot parentNodeId state resolution with
        | Some slot -> render ctx slot
        | None -> Emit.plan (ElementPlan.DisplayPlan.metric ctx.Sources resolution spec)
    | NodeKind.Badge spec -> Emit.plan (ElementPlan.DisplayPlan.badge ctx.Sources spec)
    | NodeKind.Skeleton spec -> Emit.plan (ElementPlan.DisplayPlan.skeleton spec)
    | NodeKind.Icon spec -> Emit.plan (ElementPlan.DisplayPlan.icon spec)
    | NodeKind.Callout spec -> Emit.plan (ElementPlan.DisplayPlan.callout ctx.Sources spec)
    | NodeKind.Progress spec ->
        let resolution = BindingResolver.resolve ctx.Sources spec.Fraction

        match ElementPlan.DisplayPlan.stateSlot parentNodeId state resolution with
        | Some slot -> render ctx slot
        | None ->
            Emit.plan (
                ElementPlan.DisplayPlan.progress
                    ctx.Sources
                    (ElementPlan.styleSite ctx.Policy.Csp None parentNodeId)
                    resolution
                    spec
            )
    | NodeKind.Sparkline spec ->
        BindingResolver.tryResolve ctx.Sources spec.Source
        |> Option.bind (Fuaran.UI.Charts.tryLowerSparkline spec)
        |> ElementPlan.DisplayPlan.sparkline ctx.Sources
        |> Emit.plan
    | NodeKind.Drawing spec -> Emit.plan (ElementPlan.DisplayPlan.drawing ctx.Sources spec)
    | NodeKind.LabelValueRow spec ->
        // Phase 632 — a scalar slot, as `Metric`'s.
        let resolution = BindingResolver.resolveScalarFloat ctx.Sources spec.Value

        match ElementPlan.DisplayPlan.stateSlot parentNodeId state resolution with
        | Some slot -> render ctx slot
        | None -> Emit.plan (ElementPlan.DisplayPlan.labelValueRow ctx.Sources resolution spec)
    | NodeKind.Fact spec -> Emit.plan (ElementPlan.DisplayPlan.fact ctx.Sources spec)
    // Phase 2278 — the media kinds are `ElementPlan.MediaPlan` values.
    | NodeKind.Link spec -> Emit.plan (ElementPlan.MediaPlan.link (planContext ctx parentNodeId) spec semanticAttrs)
    | NodeKind.Image spec -> Emit.plan (ElementPlan.MediaPlan.image (planContext ctx parentNodeId) spec semanticAttrs)
    | NodeKind.Media spec -> Emit.plan (ElementPlan.MediaPlan.media (planContext ctx parentNodeId) spec semanticAttrs)
    | NodeKind.Embed spec -> Emit.plan (ElementPlan.MediaPlan.embed (planContext ctx parentNodeId) spec semanticAttrs)
    | NodeKind.Tree spec ->
        // Phase 2290 — the markup is `ElementPlan.VisPlan.tree`, shared with the
        // server; this arm binds the row keyboard model and the label click.
        let treeState = ElementPlan.VisPlan.treeState ctx.Sources spec
        let expandedKeyNamed = treeState.ExpandedKeyNamed
        let expanded = treeState.Expanded
        // Write the open set back. The WHOLE set is written rather than a
        // per-row flag, because the specification fixes one shape for this slot
        // (an array of ids) and a host reading it must never have to reconcile
        // two spellings of the same fact.
        let setExpanded (ids: Set<string>) =
            match spec.ExpandedStateKey with
            | Some key ->
                let payload = JArr(ids |> Set.toList |> List.map JStr)
                runSynthesisedAction ctx (Action.SetState(key, Some payload, None))
            | None -> ()

        let toggle (itemId: string) (openIt: bool) =
            if expandedKeyNamed then
                setExpanded (
                    if openIt then
                        Set.add itemId expanded
                    else
                        Set.remove itemId expanded
                )

        // Selection is TWO independent effects, not a choice between them: a
        // declared handler runs, and a named key is written. A tree that wires
        // both wants both, and letting the handler suppress the write would
        // leave the key — the thing every other row's `aria-selected` is
        // computed from — silently stale.
        let select (itemId: string) =
            match spec.SelectionStateKey with
            | Some key -> runSynthesisedAction ctx (Action.SetState(key, Some(JStr itemId), None))
            | None -> ()

            match spec.OnSelect with
            | Some onSelect -> runAction ctx (onSelect itemId)
            | None -> ()

        // ── Focus movement ───────────────────────────────────────────────────
        //
        // Read off the DOM rather than recomputed from the spec, deliberately.
        // The rows a reader can move between are exactly the rows this render
        // put on the page, so asking the page is the one source that cannot
        // disagree with what is visible — a parallel walk of `spec.Items` would
        // be a second implementation of `visibleTreeItems`, free to drift from
        // the one that drew the tree.
        //
        // Every query is scoped to this tree's own root, so a tree nested inside
        // another host region never moves focus into a sibling widget.
        let rowsOf (el: Browser.Types.HTMLElement) : Browser.Types.HTMLElement list =
            match el.closest ".fuaran-tree" with
            | None -> []
            | Some root ->
                let nodes = root.querySelectorAll "[role=\"treeitem\"]"

                [ for i in 0 .. nodes.length - 1 -> nodes[i] :?> Browser.Types.HTMLElement ]

        let moveBy (el: Browser.Types.HTMLElement) (delta: int) =
            let rows = rowsOf el

            match rows |> List.tryFindIndex (fun r -> System.Object.ReferenceEquals(r, el)) with
            | Some i ->
                match List.tryItem (i + delta) rows with
                | Some target -> target.focus ()
                | None -> ()
            | None -> ()

        let moveToEdge (el: Browser.Types.HTMLElement) (last: bool) =
            match rowsOf el with
            | [] -> ()
            | rows -> (if last then List.last rows else List.head rows).focus ()

        // The parent ROW of a row — its nearest enclosing `treeitem`. Left on a
        // closed row goes here, which is what makes the hierarchy navigable
        // upward without a mouse.
        let moveToParent (el: Browser.Types.HTMLElement) =
            let rows = rowsOf el

            // Read off `aria-level` rather than walking `parentElement`, for the
            // reason the whole focus block reads off the DOM: the level a row
            // ANNOUNCES is the level a reader is told they are at, so navigating
            // by it cannot disagree with what assistive technology says. The
            // parent is the nearest PRECEDING row one level shallower.
            let levelOf (r: Browser.Types.HTMLElement) =
                let v = r.getAttribute "aria-level"

                if isNull v then
                    1
                else
                    match System.Int32.TryParse v with
                    | true, n -> n
                    | _ -> 1

            match rows |> List.tryFindIndex (fun r -> System.Object.ReferenceEquals(r, el)) with
            | Some i ->
                let mine = levelOf el

                rows
                |> List.truncate i
                |> List.rev
                |> List.tryFind (fun r -> levelOf r = mine - 1)
                |> Option.iter (fun (r: Browser.Types.HTMLElement) -> r.focus ())
            | None -> ()


        let rowOf (itemId: string) : ElementPlan.VisPlan.TreeRow =
            treeState.Rows
            |> Map.tryFind itemId
            |> Option.defaultValue { HasChildren = false; IsOpen = false }

        let onKey (itemId: string) (e: Browser.Types.KeyboardEvent) =
            let row = rowOf itemId
            let el = e.currentTarget :?> Browser.Types.HTMLElement
            let mutable handled = true

            match e.key with
            | "ArrowDown" -> moveBy el 1
            | "ArrowUp" -> moveBy el -1
            // The APG's two-press semantics, and the reason Right is not
            // simply "go to the first child": on a CLOSED parent the first
            // press OPENS and stays put, so the reader sees what they revealed
            // before being moved into it. A leaf does nothing at all rather
            // than swallowing the key.
            | "ArrowRight" ->
                if row.HasChildren && not row.IsOpen then toggle itemId true
                elif row.HasChildren && row.IsOpen then moveBy el 1
                else handled <- false
            // The mirror: an OPEN row closes, anything else walks up to its
            // parent. This is the movement a composition cannot offer at all —
            // the parent of a `Disclosure` is not a focusable thing.
            | "ArrowLeft" ->
                if row.HasChildren && row.IsOpen then
                    toggle itemId false
                else
                    moveToParent el
            | "Home" -> moveToEdge el false
            | "End" -> moveToEdge el true
            | "Enter"
            | " " -> select itemId
            | _ -> handled <- false

            if handled then
                e.preventDefault ()
                e.stopPropagation ()

        Emit.planWith
            (fun point itemId ->
                match point with
                | ElementPlan.Attach.TreeKeyDown -> [ prop.onKeyDown (onKey itemId) ]
                // The click lands on the LABEL, not the row, so clicking inside
                // an open branch acts on the row clicked rather than on every
                // ancestor containing it.
                | ElementPlan.Attach.TreeLabelClick ->
                    [ prop.onClick (fun e ->
                          e.stopPropagation ()
                          let row = rowOf itemId

                          if row.HasChildren && expandedKeyNamed then
                              toggle itemId (not row.IsOpen)

                          select itemId) ]
                | _ -> [])
            (ElementPlan.VisPlan.tree ctx.Sources spec treeState)
    | NodeKind.List spec -> Emit.plan (ElementPlan.DisplayPlan.list ctx.Sources spec)
    | NodeKind.Toast spec -> Emit.plan (ElementPlan.DisplayPlan.toast ctx.Sources spec)
    | NodeKind.CodeBlock spec -> Emit.plan (ElementPlan.DisplayPlan.codeBlock spec)
    | NodeKind.Math spec -> Emit.plan (ElementPlan.DisplayPlan.math spec)
    // -- Input --
    | NodeKind.Button spec -> renderButton ctx spec semanticAttrs
    | NodeKind.Select spec -> renderSelect ctx spec
    | NodeKind.Form spec -> renderForm ctx parentNodeId spec
    | NodeKind.Filters spec -> renderFilters ctx spec.Items
    | NodeKind.FileUpload spec -> renderFileUpload ctx parentNodeId spec
    // -- Vis --
    | NodeKind.DataGrid spec ->
        // Phase 393 — a static read-only grid renders the semantic <table> leg (byte-identical to the
        // retired Table); a data-bound grid takes the ordinary grid path.
        match spec.StaticRows with
        | Some sr ->
            // Phase 2290 — the table (and, when `Exportable`, its export button —
            // ruling R10) is `ElementPlan.VisPlan.staticGrid`, shared with the
            // server. Phase 1125 — a static grid holds its rows IN THE TREE, so
            // the scope is never a page; what is exported is the resolved cell
            // TEXT, exactly what the table shows.
            let project () =
                (sr.Headers |> List.map (BindingResolver.resolveTextSource ctx.Sources)),
                (sr.Rows |> List.map (List.map (BindingResolver.resolveTextSource ctx.Sources)))

            // Phase 2474 — a selectable static table's row click publishes the
            // row's record (`GridModel.staticRow`, what the designed-selection seed
            // lays down) under the table's own id: the Phase 427 default on the
            // static leg. Ungated for 427's reason — a node publishes only under
            // its own id. A static row honours no row-action closure.
            let attach: Emit.Attach =
                fun point key ->
                    match point with
                    | ElementPlan.Attach.GridRowSelect ->
                        match System.Int32.TryParse key with
                        | true, i when i >= 0 && i < List.length sr.Rows ->
                            [ prop.onClick (fun _ ->
                                  let texts =
                                      List.item i sr.Rows |> List.map (BindingResolver.resolveTextSource ctx.Sources)

                                  SelectionStore.set parentNodeId (box (GridModel.staticRow spec.Columns texts))) ]
                        | _ -> []
                    | _ -> gridExportAttach ctx parentNodeId project point key

            Emit.planWith attach (ElementPlan.VisPlan.staticGrid ctx.Sources parentNodeId spec sr)
        | None -> renderGrid ctx parentNodeId state spec
    | NodeKind.Chart spec -> renderChart ctx parentNodeId state spec
    | NodeKind.Map spec -> renderMap ctx parentNodeId state spec
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
                emitRenderFailureWithContext
                    ctx.TelemetrySink
                    ctx.SessionContext
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
                emitRenderFailureWithContext
                    ctx.TelemetrySink
                    ctx.SessionContext
                    parentNodeId
                    (nodeKindName kind + ".Fallback")
                    ex2.Message
                    RenderFailureSource.ErrorBoundary
                |> ignore

                Emit.plan (
                    ElementPlan.NodePlan.renderFailure
                        parentNodeId
                        (nodeKindName kind + ".Fallback")
                        (RenderParity.boundaryDoubleFailureMessage ex.Message ex2.Message)
                        corrId
                )
    | NodeKind.Switch spec ->
        // State-bound conditional child (Phase 392). Read the reactive state
        // value at `spec.StateKey` (scope-aware, mirroring `writeBackTo` /
        // `Action.SetState` routing), match its string form against each case in
        // order (first-match-wins), and render that case's child — else the
        // `Default`. The surface is subscribed to `spec.StateKey` via
        // `kindKeys`/`collectKeys`, so a global `Action.SetState` re-renders here
        // and re-selects the matching case with no bespoke dispatch path (FGP 3).
        let currentValue: string option =
            match spec.On with
            | Binding.State(key, dv) ->
                // The Phase 392 path, preserved verbatim: a State selector reads
                // the SCOPED StateStore (BindingResolver has no scope concept),
                // so fragment-expanded switches keep their per-scope state. A
                // 768-form defaultValue seeds the un-written key, mirroring the
                // Filter/Selection default law.
                let raw =
                    match ctx.Policy.Scope with
                    | Some scopeId -> (StateStore.forScope scopeId).Get key
                    | None -> StateStore.get key

                match raw with
                | Some v -> Some(if isNull v then "" else string v)
                | None -> dv
            | Binding.Selection(nodeId, _, dv, fld) ->
                // Phase 768 — the driving case (032/c6): the branch follows the
                // clicked row with NO writer. The decoded accessor is the
                // identity, so project the raw row's field here exactly as the
                // resolver's JVal path does — unboxing a whole row at string
                // would throw.
                let projector: obj -> obj =
                    match fld with
                    | Some f -> Binding.projectSelectionField<obj> f
                    | None -> id

                BindingResolver.tryResolve ctx.Sources (Binding.Selection(nodeId, projector, dv |> Option.map box, fld))
                |> Option.map (fun v -> if isNull v then "" else string v)
            | on ->
                // Filter / Static / Query / Now / Transform / Expr selectors
                // resolve through the SCALAR resolver at the string slot type.
                //
                // Fuaran-UI Phase 1535 — this was `tryResolve`, whose
                // `Binding.Transform` arm is row-only: it evaluates the pipeline
                // to a `Row seq` and `unbox`es that at `string`, so the one wire
                // spelling of "count > 3 ⇒ 'yes'" threw on .NET and silently
                // rendered the rows array under Fable — either way the switch
                // fell through to `Default`, with nothing anywhere saying why.
                // `resolveScalarText` reads the 1x1 result cell through the same
                // coercion every other text slot uses. Every other binding case
                // resolves exactly as it did before, so no shipped document
                // changes what it renders.
                BindingResolver.tryResolveScalarText ctx.Sources on
                |> Option.map (fun s -> if isNull (box s) then "" else s)

        // Fuaran-UI Phase 1535 — first-match-wins over BOTH kinds of case (a
        // literal `match` against the selector, a `when` predicate evaluated
        // here), through the one shared definition in `Renderer.Core` so this
        // renderer and the server renderer cannot drift on the order.
        let matched = BindingResolver.selectSwitchCase ctx.Sources currentValue spec.Cases

        let selected =
            match matched with
            | Some child -> render ctx child
            | None -> render ctx spec.Default

        // Phase 1122 — the STAGE, and it exists ONLY for the timed advance.
        //
        // The two transition tokens need no wrapper at all, which is worth
        // stating because reaching for one is the obvious first move. A
        // `Motion` token already lands as `fuaran-motion-{suffix}` on the
        // node's own outer wrapper (see `render` below), and a `Switch` whose
        // case changes REPLACES the child element — so the reference sheet's
        // `.fuaran-motion-cross-fade > *` rule animates the incoming child on
        // its own, with nothing here to arrange. Adding a wrapper for it would
        // have bought a second element, a hydration surface and a CSS selector
        // consumers would then depend on, for a behaviour the existing class
        // already carries.
        //
        // So a switch with no `AutoAdvanceMs` renders exactly what it rendered
        // before: the selected child, bare. Every pre-1122 tree keeps its DOM,
        // its selectors and its hydration by construction rather than by care.
        match spec.AutoAdvanceMs with
        | None -> selected
        | Some _ ->
            // The writer, and the ONE reason a declared interval can be inert:
            // auto-advance moves the switch's OWN key, so it exists only where
            // the selector IS that key. A `Selection` / `Filter` / `Query`
            // selector is driven by something else entirely, and writing into
            // it would mean the renderer inventing an owner for state the
            // document gave to another node. FUARAN128 reports the shape at
            // pre-emit so an author is told rather than left watching a
            // carousel that does not move.
            let advanceTo =
                match spec.On with
                | Binding.State(key, _) ->
                    Some(fun (v: string) ->
                        match ctx.Policy.Scope with
                        | Some scopeId -> (StateStore.forScope scopeId).Set(key, box v)
                        | None -> StateStore.set key (box v))
                | _ -> None

            SwitchStage.stage
                {| nodeId = parentNodeId
                   className = "fuaran-switch-stage"
                   autoAdvanceMs = spec.AutoAdvanceMs
                   // Phase 1535 — the timed advance cycles the MATCH values,
                   // which is the whole of what it can cycle: advancing writes
                   // a string into the switch's own selector key, and a
                   // predicate case is not selected by that key at all. A
                   // when-only switch therefore has an empty rotation and does
                   // not advance, which is what FUARAN128 already reports for
                   // "a declared interval with nothing for a tick to do".
                   matches = (spec.Cases |> List.choose _.Match)
                   current = currentValue
                   advanceTo = advanceTo
                   children = [ selected ] |}
    | NodeKind.Custom customSpec ->
        // The generated `CustomSpec` record replaces the old 5 inline case
        // fields — rebind them locally so the dispatch body reads unchanged.
        let moduleId = customSpec.ModuleId
        let componentId = customSpec.ComponentId
        let props = customSpec.Props
        let contentHash = customSpec.ContentHash
        // Dispatch through the runtime's SCOPE-CONSTRAINED Custom-renderer
        // surface (Phase 783). Bounded-escape verification:
        //   1. Scope check — the lookup is keyed by the render scope this tree
        //      is running under, so a tree cannot address a renderer registered
        //      for another surface. There is deliberately no cross-scope
        //      fallback: falling back would make the scoping advisory.
        //   2. Hash check (pre-dispatch) via TryGetCustomRendererInScope, under
        //      the host's strictness floor.
        //   3. Exposed-NodeIds DOM walk (post-mount) when the tree declares
        //      any. .NET-side: no-op (FUARAN053 covers build-time).
        // A host implementing `IFuaranRuntime` by hand that has not implemented
        // the scoped members gets the unregistered placeholder — fail closed;
        // "unimplemented" is a kind of unregistered.

        let renderPlaceholder () : ReactElement =
            let propKeys = props |> Map.toList |> List.map fst |> String.concat ", "

            Html.div
                [ prop.className "fuaran-custom-placeholder"
                  prop.children
                      [ Html.div
                            [ prop.className Css.Class.CustomLabel
                              prop.text (sprintf "Custom %s.%s" moduleId componentId) ]
                        Html.div
                            [ prop.className Css.Class.CustomProps
                              prop.text (sprintf "props: %s" propKeys) ] ] ]

        let registryProbe =
            ctx.Runtime.TryGetCustomRendererInScope(ctx.Policy.Scope, moduleId, componentId)

        let registryHash = registryProbe |> Option.bind snd
        let outcome = classifyCustomHash ctx.Policy.CustomHashFloor contentHash registryHash

        let dispatchToRenderer () : ReactElement =
            match registryProbe with
            | Some(fn, _) -> fn props
            | None ->
                match ctx.Runtime.TryRenderCustomInScope(ctx.Policy.Scope, moduleId, componentId, props) with
                | Some element -> element
                | None -> renderPlaceholder ()

        /// Refuse the node: warn, then route through `OnError` when the consumer
        /// supplied one, else the labelled placeholder so the failure stays
        /// visible rather than becoming a blank box.
        let refuse (reason: string) : ReactElement =
            ctx.Runtime.Warn(formatHashMismatchPayload moduleId componentId contentHash registryHash)

            match state.OnError with
            | Some onErr ->
                let payload: ErrorPayload =
                    { Kind = ErrorKind.BindingResolution
                      Message = sprintf "Custom node %s.%s refused: %s" moduleId componentId reason
                      CorrelationId = correlationId (parentNodeId + "|custom-hash") }

                render ctx (onErr payload)
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
                // OnError-routing: synthesize a BindingResolution-shaped
                // payload (closest existing ErrorKind for a renderer-side
                // failure that didn't cross the wire).
                refuse "the registered renderer's hash differs from the declared ContentHash (StrictReplay)"
            // Phase 783 — under an enforcing host floor, a hash that cannot be
            // verified is a refusal. Omitting the hash was the cheapest bypass:
            // `NoTreeHash` shared a branch with `Match` and rendered silently.
            | CustomHashOutcome.Unverifiable ->
                refuse
                    "the content hash could not be verified (the tree declared none, or the registry recorded none) and the host is enforcing"

        // Exposed-NodeIds post-mount DOM walk. Skip the syscall
        // entirely when the tree doesn't declare any (the common case) —
        // the generated `ExposedNodeIds: string list option` encodes the old
        // empty list as `None` (and a decoded `Some []` is the same no-op).
        match customSpec.ExposedNodeIds with
        | None
        | Some [] -> ()
        | Some exposedNodeIds -> scheduleExposedNodeIdsVerification parentNodeId exposedNodeIds ctx.Runtime

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
        // `FragmentRefSpec.Name` erased to a bare string in the generated
        // record; the context registry / expansion set stay keyed by
        // `FragmentId`, so wrap at this boundary.
        let rawName = spec.Name
        let fragmentKey = FragmentId rawName

        if Set.contains fragmentKey ctx.ExpandingFragments then
            ctx.Runtime.Warn(
                sprintf
                    "[fuaran:fragment] cycle detected expanding ref '%s' → fragment '%s'; rendering placeholder. Validator FUARAN058 catches this at build time."
                    parentNodeId
                    rawName
            )

            Emit.plan (ElementPlan.StructuralPlan.fragmentCycle rawName)
        else
            match Map.tryFind fragmentKey ctx.Fragments with
            | None ->
                ctx.Runtime.Warn(
                    sprintf
                        "[fuaran:fragment] ref '%s' points to fragment '%s' which has no declaration in this tree; rendering placeholder. Validator FUARAN057 catches this at build time."
                        parentNodeId
                        rawName
                )

                Emit.plan (ElementPlan.StructuralPlan.fragmentUnresolved rawName)
            | Some body ->
                // Prefix interior ids with the ref's NodeId + "." so
                // sibling refs to the same fragment stay DOM-unique.
                // `parentNodeId` is already the ref's fully-prefixed id
                // (the outer expansion, if any, rewrote it before this
                // render call), so nested expansion naturally produces
                // `outerRef.innerRef.btn` without re-concatenating an
                // ambient prefix.
                let prefix = FragmentExpansion.refPrefix parentNodeId

                // Phase 207 examined memoising this expansion per
                // `(fragment, prefix)`, found the specified shape both unsound
                // and unable to hit, and installed no cache; Phase 1151 made the
                // design decision it deferred and built it. The finding that
                // still governs the SHAPE, recorded here so it is not
                // re-derived: `prefix` is the REF's own node id, so within a
                // single render pass every expansion has a distinct key and no
                // per-render cache could ever hit; and the fragment NAME is not
                // the input to the expansion, since a differently-declared tree
                // may bind the same name to another body. So the memo is keyed
                // on the BODY INSTANCE plus the prefix and lives in a bounded,
                // process-global cache that outlives the render — see
                // `FragmentExpansion` for the lifetime, bound and concurrency
                // posture. `RenderContext` is unchanged: the cache is borrowed.
                let namespaced = expandFragment prefix body

                let expandedCtx =
                    { ctx with
                        ExpandingFragments = Set.add fragmentKey ctx.ExpandingFragments }

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
        //
        // The runtime the guest gets and the bubble it dispatches through both
        // pass the host's `GuestSeam` when one is installed, so a guest reached
        // through the plain render path is gated by the same capability policy as
        // one an orchestration tier mounts and drives itself.
        let boundaryChild =
            match ctx.Runtime.TryLoadGuest spec.ScopeId, renderGuestHook with
            | Some guestTree, Some renderGuest ->
                // `MountSpec.OnBubble` is optional in the generated record — an
                // unwired bubble channel swallows guest dispatches (inert,
                // exactly like the old no-op).
                let rawBubble (o: obj) =
                    match spec.OnBubble with
                    | Some onBubble -> runAction ctx (onBubble o)
                    | None -> ()

                // Phase 783 — CLAMP the declared channel before anything reads
                // it. `ChannelDirection` is a required wire field, so a decoded
                // tree simply writes `TwoWay`; `OutOnly` was only the default of
                // the AUTHORING smart constructor, and no host-side clamp
                // existed. `TwoWay` is now a host grant, never a wire-declared
                // property.
                let declaredDirection = spec.Channel.Direction

                let clampedChannel =
                    { spec.Channel with
                        Direction = ChannelDirection.OutOnly }

                let clampedCtx: GuestSeamContext =
                    { ScopeId = spec.ScopeId
                      Capabilities = spec.Capabilities
                      Channel = clampedChannel
                      DeclaredDirection = declaredDirection }

                let seamOpt = currentGuestSeam ()

                // The grant decision reads the CLAMPED context, so a policy sees
                // what will happen by default and what was asked for, and says
                // yes or no to the difference. Phase 2135 — it is consulted ONLY
                // for a mount that DECLARED `TwoWay`: two-way needs the
                // declaration AND the grant (SANITIZATION.md, "The `Mount`
                // boundary"), so a mount that declared `OutOnly` is never
                // upgraded past its own declaration, whatever the seam answers.
                let granted =
                    match seamOpt with
                    | Some seam when declaredDirection = ChannelDirection.TwoWay -> seam.GrantTwoWay clampedCtx
                    | _ -> false

                let effectiveChannel =
                    if granted then
                        { spec.Channel with
                            Direction = ChannelDirection.TwoWay }
                    else
                        clampedChannel

                // Record the downgrade rather than dropping it silently: a tree
                // asking for TwoWay and getting OutOnly is a fact the host's
                // observability should carry, and a silent clamp is
                // indistinguishable from a guest that simply never pushed.
                if declaredDirection = ChannelDirection.TwoWay && not granted then
                    ctx.Runtime.Warn(
                        sprintf
                            "[Fuaran] mount '%s' declared a TwoWay guest channel; downgraded to OutOnly. TwoWay is a host grant (GuestSeam.GrantTwoWay), not a wire-declared property."
                            spec.ScopeId
                    )

                let seamCtx =
                    { clampedCtx with
                        Channel = effectiveChannel }

                // Apply the host's capability policy. With NO seam installed the
                // guest is UNPRIVILEGED (Phase 783) — it used to receive the
                // host's own runtime unwrapped, which meant a host that had
                // installed nothing granted a mounted guest everything the host
                // could do. Unregistered must mean unprivileged.
                let guestRuntime, guestDispatch =
                    match seamOpt with
                    | Some seam -> seam.WrapRuntime seamCtx ctx.Runtime, seam.GateBubble seamCtx rawBubble
                    | None ->
                        Runtime.UnprivilegedGuestRuntime(ctx.Runtime, spec.ScopeId) :> Runtime.IFuaranRuntime, rawBubble

                let guestCtx: RenderContext<obj> =
                    // Phase 1075 — the guest seeds from ITS OWN tree, into its
                    // own scope. A guest declaration is invisible to the host
                    // and vice versa (charter §4, Phase 266's boundary
                    // unchanged): the host's walk never sees this tree, because
                    // the guest is loaded by the host's runtime rather than
                    // carried in the host's own nodes, so nothing leaks upward
                    // by construction. Downward, the guest scope's StateStore
                    // snapshot and the host's furnished State both sit OVER the
                    // guest's seeds (`RenderContext.inScope`), the precedence
                    // every other entry point applies; the host's default store
                    // is untouched.
                    { (RenderContext.create ctx.Sources guestDispatch guestTree
                       |> RenderContext.inScope spec.ScopeId) with
                        Runtime = guestRuntime
                        TelemetrySink = ctx.TelemetrySink
                        // The host's correlation context follows the guest: a
                        // failure inside a mounted region belongs to the same
                        // interaction as the render that mounted it (Phase 330).
                        SessionContext = ctx.SessionContext
                        ActionSink = ctx.ActionSink
                        CurrentNodeId = ctx.CurrentNodeId
                        UploadSink = ctx.UploadSink
                        // The guest INHERITS the host's policy, scope aside. Its
                        // tree is not more trusted than the tree that mounted
                        // it, so it cannot widen the egress it renders under
                        // (Phase 1026), it is verified at the floor the host
                        // raised for this request (Phase 1532), and it cannot
                        // put back under `Permissive` the inline style a strict
                        // document forbids (Phase 1545). Narrowing for a guest
                        // is a host act, through its own `GuestSeam`.
                        Policy =
                            { ctx.Policy with
                                Scope = Some spec.ScopeId } }

                // Route through the late-bound hook (a function *value*), not a
                // direct call into the recursive `render` group at type obj —
                // F# forbids polymorphic recursion, so the obj-typed guest render
                // crosses the type boundary via the hook set at module init below.
                renderGuest guestCtx guestTree
            | _ -> Emit.plan (ElementPlan.StructuralPlan.mountPlaceholder spec.ScopeId)

        Emit.plan (ElementPlan.StructuralPlan.mountBoundary spec.ScopeId (ElementPlan.Plan.Host boundaryChild))

// ─── Inputs ────────────────────────────────────────────────────────────────

/// What a container kind's plan needs from this renderer (Phase 2278): a
/// child renders through `render`, which carries the walk.
and private planContext (ctx: RenderContext<'Msg>) (nodeId: string) : ElementPlan.PlanContext<'Msg, ReactElement> =
    { Sources = ctx.Sources
      Csp = ctx.Policy.Csp
      Collector = None
      Egress = ctx.Policy.Egress
      NodeId = nodeId
      Child = fun child -> ElementPlan.Plan.Host(render ctx child) }

and private renderButton
    (ctx: RenderContext<'Msg>)
    (spec: ButtonSpec<'Msg>)
    // Phase 951 — the node's a11y projection, emitted on the `<button>` itself.
    (semanticAttrs: (string * string) list)
    : ReactElement =
    // Phase 2290 — the markup is `ElementPlan.InputPlan.button`, shared with
    // the server. The UNWIRED cue (ruling R1) is this renderer's runtime
    // decoration, applied here and never served: a button whose action reaches
    // Call / Notify / Navigate / SetState / AiTool / Invoke takes the unwired
    // class, and — unless the author's tooltip already speaks — a title naming
    // the substrate it routes through. It helps the operator see which buttons
    // rely on substrate vs pure Elmish; dispatching still works.
    let plan = ElementPlan.InputPlan.button ctx.Sources spec semanticAttrs

    let decorated =
        match plan with
        | ElementPlan.Plan.Element element when containsUnwiredAction spec.OnClick ->
            let unwiredClass = Css.buttonUnwired (Css.buttonVariant spec.Variant)

            let hasTitle =
                element.Props
                |> List.exists (function
                    | ElementPlan.Prop.Attr("title", _) -> true
                    | _ -> false)

            ElementPlan.Plan.Element
                { element with
                    Props =
                        (element.Props
                         |> List.map (function
                             | ElementPlan.Prop.Attr("class", _) -> ElementPlan.Prop.Attr("class", unwiredClass)
                             | p -> p))
                        @ (if hasTitle then
                               []
                           else
                               [ ElementPlan.Prop.Attr(
                                     "title",
                                     "This action routes through the IFuaranRuntime substrate (Action.Call/Notify/Navigate/SetState/AiTool)."
                                 ) ]) }
        | other -> other

    Emit.planWith
        (fun point _ ->
            match point with
            | ElementPlan.Attach.ButtonClick -> [ prop.onClick (fun _ -> runAction ctx spec.OnClick) ]
            | _ -> [])
        decorated

and private renderSelect (ctx: RenderContext<'Msg>) (spec: SelectSpec<'Msg>) : ReactElement =
    // Phase 2290 — the markup is `ElementPlan.InputPlan.select`, shared with
    // the server (rulings R2 / R3); this binds the change.
    let onChange: IReactProperty list =
        if spec.Multiple = Some true then
            // Phase 291 — `onChange` reads every selected option and fires
            // `OnChangeMulti` with the value list. Phase 426: an omitted
            // handler writes the list back to a writable `Values` binding.
            [ prop.custom (
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
              ) ]
        else
            [ prop.onChange (fun (v: string) ->
                  // Phase 426: the closure wins; an omitted handler writes the
                  // chosen option back to a writable `Value` binding (a cleared
                  // choice clears the slot).
                  let chosen = if v = "" then None else Some v

                  match spec.OnChange with
                  | Some onChange -> runAction ctx (onChange chosen)
                  | None ->
                      spec.Value
                      |> Option.iter (fun value -> writeBackTo ctx value (chosen |> Option.map box))) ]

    Emit.planWith
        (fun point _ ->
            match point with
            | ElementPlan.Attach.SelectChange -> onChange
            | _ -> [])
        (ElementPlan.InputPlan.select ctx.Sources spec)

and private renderForm (ctx: RenderContext<'Msg>) (nodeId: string) (spec: FormSpec<'Msg>) : ReactElement =
    // Phase 2290 — the markup (fields, submit, the Phase 130 disabled fieldset)
    // is `ElementPlan.InputPlan.form`, shared with the server; this binds the
    // submit handler.
    let onSubmit (e: Browser.Types.Event) : unit =
        e.preventDefault ()
        // Notify every Local-bound input whose FlushOn =
        // OnSubmit so each buffer drains through its OnCommit
        // BEFORE the form's typed OnSubmit fires. Order matters
        // because the typed OnSubmit may dispatch a network call
        // that reads the just-committed values.
        //
        // Dispatched on THIS form (the event's currentTarget) rather
        // than on the window, so the broadcast reaches this form's own
        // inputs and no longer drains every `OnSubmit` Local input on
        // the page — a search box in a header included.
        LocalBindings.dispatchFormCommit (unbox<Browser.Types.Element> e.currentTarget)
        // Phase 820 — submit-payload semantics: the form's current
        // field values (read exactly as this renderer's own controls
        // read them — see `SubmitPayload.harvestFields`) ride the
        // OnSubmit's `Notify` payloads under a "fields" key. A `Call`
        // has no payload slot on `IFuaranRuntime.Call(endpoint,
        // onResult)` — the client Call substrate is host-owned — so on
        // this tier the submit body reaches only the arms with a
        // payload slot; the server-driven tier delivers the Call body
        // through its `InterpretSubmitCall` seam (SERVER_DRIVEN.md).
        let fields = SubmitPayload.harvestFields ctx.Sources spec.Fields

        // Phase 864 — the declared-rule submit GATE. A rendering host
        // MUST NOT run the form's action while a declared rule is unmet,
        // and MUST make visible which field is unmet. The gate runs over
        // exactly the values the submit would have carried, so what is
        // checked and what would have been sent cannot diverge.
        //
        // The submit button is deliberately NOT disabled: disabling it
        // removes the gesture that reveals WHICH field is unmet, which is
        // the second half of the obligation. Native HTML makes the same
        // choice for its own constraint attributes — and note those run
        // even earlier, before this event is dispatched at all, so an
        // unmet `pattern` / `type` is already blocked by the browser.
        let values = Map.ofList fields

        let unmet =
            spec.Fields
            |> List.choose (fun f ->
                FieldRules.unmet (BindingResolver.resolveTextSource ctx.Sources) values f
                |> Option.map (fun m -> f.Id, m))

        FieldRules.markUnmet unmet [ for f in spec.Fields -> f.Id ]

        match unmet with
        | [] -> runAction ctx (SubmitPayload.attachToAction fields spec.OnSubmit)
        | _ -> ()

    Emit.planWith
        (fun point _ ->
            match point with
            | ElementPlan.Attach.FormSubmit -> [ prop.onSubmit onSubmit ]
            | _ -> [])
        (ElementPlan.InputPlan.form
            (planContext ctx nodeId)
            spec
            [ for field in spec.Fields -> ElementPlan.Plan.Host(renderFormField ctx field) ])

/// Phase 2290 — the handlers a segmented control binds (`InputPlan.segmentedChoice`):
/// a click per option, the WAI-ARIA radiogroup keyboard walk (arrows cycle
/// with wrap-around, Home / End), and the vertical radios' change.
/// `handleChange` is a side-effecting change handler (Phase 423), not an
/// `Action` factory: the form-field caller dispatches through `runAction`,
/// a declarative chip writes the FilterStore.
and private segmentAttach
    (ctx: RenderContext<'Msg>)
    (options: Binding<SelectOption list>)
    (value: Binding<string>)
    (handleChange: string option -> unit)
    : Emit.Attach =
    let opts = BindingResolver.resolveOptions ctx.Sources options

    let activeIndex =
        BindingResolver.tryResolve ctx.Sources value
        |> Option.bind (fun s -> if isNull s || s = "" then None else Some s)
        |> Option.bind (fun v -> opts |> List.tryFindIndex (fun o -> o.Value = v))
        |> Option.defaultValue -1

    let cycle (delta: int) : unit =
        if not (List.isEmpty opts) then
            let count = List.length opts

            let nextIndex =
                // No selection yet — entering with ArrowRight picks the first,
                // ArrowLeft the last; otherwise wrap-around both ways.
                if activeIndex < 0 then
                    (if delta > 0 then 0 else count - 1)
                else
                    ((activeIndex + delta) % count + count) % count

            handleChange (Some (List.item nextIndex opts).Value)

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

            opts
            |> List.tryHead
            |> Option.iter (fun first -> handleChange (Some first.Value))
        | "End" ->
            e.preventDefault ()
            opts |> List.tryLast |> Option.iter (fun last -> handleChange (Some last.Value))
        | _ -> ()

    fun point key ->
        match point with
        | ElementPlan.Attach.SegmentSelect -> [ prop.onClick (fun _ -> handleChange (Some key)) ]
        | ElementPlan.Attach.SegmentKeyDown -> [ prop.onKeyDown onKeyDown ]
        | ElementPlan.Attach.SegmentRadio ->
            [ prop.onChange (fun (checked': bool) ->
                  if checked' then
                      handleChange (Some key)) ]
        | _ -> []

/// Phase 2290 — the change handlers a planned form-field control binds
/// (`InputPlan.fieldControl`). Phase 426 — a `Some` handler dispatches (the
/// closure wins); a `None` handler writes the typed change back to the field's
/// own value binding (see `writeBackTo`), a cleared choice clearing the slot.
/// Phase 596 — an absent value slot is the auto-bind form, the binding the
/// decoder used to synthesise. Phase 1141 — the module-level `fieldChange` and
/// the pair seams, never a local copy.
and private fieldAttach (ctx: RenderContext<'Msg>) (field: FormField<'Msg>) : Emit.Attach =
    let onChange (handler: IReactProperty) : Emit.Attach =
        fun point _ ->
            if point = ElementPlan.Attach.FieldChange then
                [ handler ]
            else
                []

    let auto (value: 'T) : Binding<'T> = Binding.State(field.Id, Some value)

    match field.Kind with
    | FormFieldKind.Text(FieldView.Text value, FieldView.OnText handler)
    | FormFieldKind.TextArea(FieldView.Text value, FieldView.OnText handler, _)
    | FormFieldKind.Color(FieldView.OnText handler, FieldView.Text value) ->
        let value =
            value
            |> Option.defaultValue (
                match field.Kind with
                | FormFieldKind.Color _ -> auto Fuaran.UI.Defaults.ControlValueDefaults.color
                | _ -> auto Fuaran.UI.Defaults.ControlValueDefaults.text
            )

        onChange (prop.onChange (fun (v: string) -> fieldChange ctx handler value (Some(box v)) v))
    | FormFieldKind.Number(FieldView.Number value, FieldView.OnNumber handler)
    | FormFieldKind.RangedNumber(FieldView.Number value, FieldView.OnNumber handler, _, _, _) ->
        let value =
            value
            |> Option.defaultValue (auto Fuaran.UI.Defaults.ControlValueDefaults.number)

        onChange (prop.onChange (fun (v: float) -> fieldChange ctx handler value (Some(box v)) v))
    | FormFieldKind.Checkbox(FieldView.Bool value, FieldView.OnBool handler)
    | FormFieldKind.Toggle(FieldView.Bool value, FieldView.OnBool handler) ->
        let value =
            value
            |> Option.defaultValue (auto Fuaran.UI.Defaults.ControlValueDefaults.checkbox)

        onChange (prop.onChange (fun (b: bool) -> fieldChange ctx handler value (Some(box b)) b))
    | FormFieldKind.Choice(_, FieldView.Text value, FieldView.OnChoice handler) ->
        let value =
            value
            |> Option.defaultValue (Binding.State(field.Id, Fuaran.UI.Defaults.ControlValueDefaults.choice))

        onChange (
            prop.onChange (fun (v: string) ->
                let chosen = if v = "" then None else Some v
                fieldChange ctx handler value (chosen |> Option.map box) chosen)
        )
    | FormFieldKind.DateTime(FieldView.Text value, FieldView.OnChoice handler, _, _, _, _) ->
        let value =
            value
            |> Option.defaultValue (auto Fuaran.UI.Defaults.ControlValueDefaults.dateTime)

        // The Date onChange takes `string option` since the swap — an input
        // change always carries a value; the store write-back keeps the raw string.
        onChange (prop.onChange (fun (v: string) -> fieldChange ctx handler value (Some(box v)) (Some v)))
    | FormFieldKind.Range(FieldView.Range value, FieldView.OnRange handler, _, _, _) ->
        let value =
            value
            |> Option.defaultValue (auto Fuaran.UI.Defaults.ControlValueDefaults.range)

        let current: RangePair =
            BindingResolver.tryResolve ctx.Sources value
            |> Option.defaultValue { Min = 0.0; Max = 0.0 }

        let onMin, onMax = rangeInputHandlers ctx handler value current

        fun point key ->
            match point, key with
            | ElementPlan.Attach.FieldChange, "min" -> [ prop.onChange onMin ]
            | ElementPlan.Attach.FieldChange, "max" -> [ prop.onChange onMax ]
            | _ -> []
    | FormFieldKind.DateTimeRange(FieldView.DateRange value, FieldView.OnDateRange handler, _, _, _, _) ->
        let value =
            value
            |> Option.defaultValue (auto Fuaran.UI.Defaults.ControlValueDefaults.dateTimeRange)

        let current: DateTimeRangePair =
            BindingResolver.tryResolve ctx.Sources value
            |> Option.defaultValue { From = ""; To = "" }

        let onFrom, onTo = dateRangeInputHandlers ctx handler value current

        fun point key ->
            match point, key with
            | ElementPlan.Attach.FieldChange, "min" -> [ prop.onChange onFrom ]
            | ElementPlan.Attach.FieldChange, "max" -> [ prop.onChange onTo ]
            | _ -> []
    | FormFieldKind.SegmentedChoice(options, FieldView.Text value, FieldView.OnChoice handler, _) ->
        let value =
            value
            |> Option.defaultValue (Binding.State(field.Id, Fuaran.UI.Defaults.ControlValueDefaults.choice))

        segmentAttach ctx options value (fun chosen -> fieldChange ctx handler value (chosen |> Option.map box) chosen)
    // The widget kinds render their own component and bind nothing here.
    | _ -> fun _ _ -> []

/// One `Form` field. Phase 2290 — the label, the help and every plain
/// control are `ElementPlan.InputPlan` (rulings R4 / R5 / R15 / R16), shared
/// with the server. The WIDGET controls are this renderer's by nature: a
/// stateful component in place of the plan's no-script floor — the
/// `Binding.Local` buffers, the combobox, the rating and the token field.
and private renderFormField (ctx: RenderContext<'Msg>) (field: FormField<'Msg>) : ReactElement =
    let labelText = BindingResolver.resolveTextSource ctx.Sources field.Label
    let (NodeId fieldNodeId) = NodeId field.Id

    let control =
        match field.Kind with
        | FormFieldKind.Text(FieldView.Text(Some(Binding.Local(flushOn,
                                                               format,
                                                               initialFrom,
                                                               onCommit,
                                                               parse,
                                                               _,
                                                               commitTo))),
                             _) ->
            // Phase 864 RECORDED KNOWN LIMIT — `localTextInput` carries no
            // attribute-passthrough slot, so a declared rule's constraint
            // ATTRIBUTES are not projected onto this control: `format` /
            // `minLength` / `maxLength` lose per-keystroke enforcement but are
            // still gated at submit; `pattern` is enforced only by the
            // server-side re-check. Closing it needs an attribute slot on
            // `localTextInput`.
            LocalBindings.localTextInput
                {| nodeId = fieldNodeId
                   fieldId = field.Id
                   className = "fuaran-form-input"
                   required = field.Required
                   externalValue = BindingResolver.tryResolve ctx.Sources initialFrom |> Option.defaultValue ""
                   flushOn = flushOn
                   formatter = format
                   parser = parse
                   commit =
                    fun (parsed: string) ->
                        // An absent onCommit commits nothing THROUGH THE
                        // CLOSURE — `commitTo` is the wire-carried alternative
                        // (Fuaran-UI Phase 1538).
                        onCommit
                        |> Option.iter (fun oc -> runAction ctx (unbox<Action<'Msg>> (oc parsed)))

                        commitLocalTo ctx commitTo (box parsed) |}
        | FormFieldKind.Number(FieldView.Number(Some(Binding.Local(flushOn,
                                                                   format,
                                                                   initialFrom,
                                                                   onCommit,
                                                                   parse,
                                                                   _,
                                                                   commitTo))),
                               _)
        | FormFieldKind.RangedNumber(FieldView.Number(Some(Binding.Local(flushOn,
                                                                         format,
                                                                         initialFrom,
                                                                         onCommit,
                                                                         parse,
                                                                         _,
                                                                         commitTo))),
                                     _,
                                     _,
                                     _,
                                     _) ->
            // `type=text` + `inputMode=numeric`, so the consumer-side formatter
            // (thousands separators etc.) survives.
            let constraints: NumberFieldConstraints =
                match field.Kind with
                | FormFieldKind.RangedNumber(_, _, min, max, step) -> { Min = min; Max = max; Step = step }
                | _ -> Fuaran.UI.Defaults.numberFieldConstraints

            LocalBindings.localNumberInput
                {| nodeId = fieldNodeId
                   fieldId = field.Id
                   className = "fuaran-form-input"
                   required = field.Required
                   externalValue = BindingResolver.tryResolve ctx.Sources initialFrom |> Option.defaultValue 0.0
                   flushOn = flushOn
                   formatter = format
                   parser = parse
                   commit =
                    fun (parsed: float) ->
                        onCommit
                        |> Option.iter (fun oc -> runAction ctx (unbox<Action<'Msg>> (oc parsed)))

                        commitLocalTo ctx commitTo (box parsed)
                   constraints = constraints |}
        // Phase 1113 — the full WAI-ARIA combobox, in `ComboboxControl`.
        | FormFieldKind.Combobox(allowFreeText, FieldView.OnChoice onChange, options, FieldView.Text value) ->
            let value =
                value
                |> Option.defaultValue (Binding.State(field.Id, Fuaran.UI.Defaults.ControlValueDefaults.combobox))

            ComboboxControl.combobox
                {| fieldId = field.Id
                   className = Css.Class.FormFieldControlComboboxInput
                   listClassName = "fuaran-combobox-list"
                   required = field.Required
                   allowFreeText = allowFreeText
                   placeholder = ""
                   options = BindingResolver.resolveOptions ctx.Sources options
                   committed =
                    BindingResolver.tryResolve ctx.Sources value
                    |> Option.bind (fun s -> if isNull s || s = "" then None else Some s)
                   commit = fun chosen -> fieldChange ctx onChange value (chosen |> Option.map box) chosen |}
        // Phase 1130 — the star scale, in `RatingControl` (slider when it can
        // be written, img when it cannot).
        | FormFieldKind.Rating(allowHalf, max, FieldView.OnNumber onChange, FieldView.Number value) ->
            let value =
                value
                |> Option.defaultValue (Binding.State(field.Id, Some Fuaran.UI.Defaults.ControlValueDefaults.rating))

            RatingControl.rating
                {| fieldId = field.Id
                   className = "fuaran-form-field-control"
                   max = max
                   allowHalf = allowHalf
                   interactive = onChange.IsSome || Fuaran.UI.BindingWalk.isWriteBackTarget value
                   label = labelText
                   value =
                    BindingResolver.tryResolve ctx.Sources value
                    |> Option.defaultValue Fuaran.UI.Defaults.ControlValueDefaults.rating
                   commit = fun (v: float) -> fieldChange ctx onChange value (Some(box v)) v |}
        // Phase 1121 — the multi-token input, in `TokensControl`. `None` and
        // `Some []` suggestions stay apart: combobox ARIA only where a source
        // was DECLARED.
        | FormFieldKind.Tokens(allowFreeText, FieldView.OnTokens onChange, suggestions, FieldView.Tokens value) ->
            let value =
                value
                |> Option.defaultValue (Binding.State(field.Id, Some Fuaran.UI.Defaults.ControlValueDefaults.tokens))

            TokensControl.tokens
                {| fieldId = field.Id
                   className = Css.Class.FormFieldControlTokensInput
                   listClassName = "fuaran-tokens-suggestions"
                   required = field.Required
                   allowFreeText = allowFreeText
                   placeholder = ""
                   suggestions = suggestions |> Option.map (BindingResolver.resolveOptions ctx.Sources)
                   tokens =
                    BindingResolver.tryResolve ctx.Sources value
                    |> Option.defaultValue Fuaran.UI.Defaults.ControlValueDefaults.tokens
                   commit = fun next -> fieldChange ctx onChange value (Some(box next)) next |}
        | _ -> Emit.planWith (fieldAttach ctx field) (ElementPlan.InputPlan.fieldControl ctx.Sources field)

    Emit.plan (ElementPlan.InputPlan.formField ctx.Sources field (ElementPlan.Plan.Host control))

/// Phase 2290 — the change handlers a planned filter-chip control binds
/// (`InputPlan.filterControl`). Phase 423 — a chip whose `onChange` is `None`
/// writes its typed value to `$filters.<name>` (`filterWriteBinding`); a
/// `Some` closure dispatches. The READ comes from the chip's own slot, the
/// declarative WRITE always goes to `$filters.<name>` — routing it through the
/// resolved binding instead would change chip semantics for every control
/// kind, a language decision out of scope here.
and private filterAttach (ctx: RenderContext<'Msg>) (spec: FilterSpec<'Msg>) : Emit.Attach =
    let dest: Binding<obj> = Binding.Filter(spec.Name, None)

    let onChange (handler: IReactProperty) : Emit.Attach =
        fun point _ ->
            if point = ElementPlan.Attach.FieldChange then
                [ handler ]
            else
                []

    let auto (binding: Binding<'T> option) : Binding<'T> =
        binding |> Option.defaultValue (Binding.Filter(spec.Name, None))

    match spec.Kind with
    | FormFieldKind.Text(_, FieldView.OnText handler)
    | FormFieldKind.TextArea(_, FieldView.OnText handler, _)
    | FormFieldKind.Color(FieldView.OnText handler, _) ->
        onChange (prop.onChange (fun (v: string) -> fieldChange ctx handler dest (Some(box v)) v))
    | FormFieldKind.Number(_, FieldView.OnNumber handler)
    | FormFieldKind.RangedNumber(_, FieldView.OnNumber handler, _, _, _) ->
        onChange (prop.onChange (fun (v: float) -> fieldChange ctx handler dest (Some(box v)) v))
    | FormFieldKind.Checkbox(_, FieldView.OnBool handler)
    | FormFieldKind.Toggle(_, FieldView.OnBool handler) ->
        onChange (prop.onChange (fun (v: bool) -> fieldChange ctx handler dest (Some(box v)) v))
    | FormFieldKind.DateTime(_, FieldView.OnChoice handler, _, _, _, _) ->
        onChange (prop.onChange (fun (v: string) -> fieldChange ctx handler dest (Some(box v)) (Some v)))
    | FormFieldKind.Choice(_, _, FieldView.OnChoice handler) ->
        onChange (
            prop.onChange (fun (v: string) ->
                let chosen = if v = "" then None else Some v
                fieldChange ctx handler dest (chosen |> Option.map box) chosen)
        )
    | FormFieldKind.Range(FieldView.Range value, FieldView.OnRange handler, _, _, _) ->
        let current: RangePair =
            BindingResolver.tryResolve ctx.Sources (auto value)
            |> Option.defaultValue { Min = 0.0; Max = 0.0 }

        let onMin, onMax = rangeInputHandlers ctx handler dest current

        fun point key ->
            match point, key with
            | ElementPlan.Attach.FieldChange, "min" -> [ prop.onChange onMin ]
            | ElementPlan.Attach.FieldChange, "max" -> [ prop.onChange onMax ]
            | _ -> []
    | FormFieldKind.DateTimeRange(FieldView.DateRange value, FieldView.OnDateRange handler, _, _, _, _) ->
        let current: DateTimeRangePair =
            BindingResolver.tryResolve ctx.Sources (auto value)
            |> Option.defaultValue { From = ""; To = "" }

        let onFrom, onTo = dateRangeInputHandlers ctx handler dest current

        fun point key ->
            match point, key with
            | ElementPlan.Attach.FieldChange, "min" -> [ prop.onChange onFrom ]
            | ElementPlan.Attach.FieldChange, "max" -> [ prop.onChange onTo ]
            | _ -> []
    | FormFieldKind.SegmentedChoice(options, FieldView.Text value, FieldView.OnChoice handler, _) ->
        segmentAttach ctx options (auto value) (fun chosen ->
            fieldChange ctx handler dest (chosen |> Option.map box) chosen)
    | _ -> fun _ _ -> []

/// One `Filters` chip. Phase 2290 — the chip and every plain control are
/// `ElementPlan.InputPlan` (rulings R7 / R8 / R9), shared with the server;
/// the combobox, rating and token chips are this renderer's components by
/// nature, writing through `$filters.<name>` like every declarative chip.
and private renderFilterSpec (ctx: RenderContext<'Msg>) (spec: FilterSpec<'Msg>) : ReactElement =
    let labelText = BindingResolver.resolveTextSource ctx.Sources spec.Label
    let dest: Binding<obj> = Binding.Filter(spec.Name, None)

    let auto (binding: Binding<'T> option) : Binding<'T> =
        binding |> Option.defaultValue (Binding.Filter(spec.Name, None))

    let control =
        match spec.Kind with
        | FormFieldKind.Combobox(allowFreeText, FieldView.OnChoice onChange, options, FieldView.Text value) ->
            ComboboxControl.combobox
                {| fieldId = spec.Name + "-filter"
                   className = Css.Class.FilterInputComboboxInput
                   listClassName = "fuaran-combobox-list"
                   required = false
                   allowFreeText = allowFreeText
                   placeholder = labelText
                   options = BindingResolver.resolveOptions ctx.Sources options
                   committed =
                    BindingResolver.tryResolve ctx.Sources (auto value)
                    |> Option.bind (fun s -> if isNull s || s = "" then None else Some s)
                   commit = fun chosen -> fieldChange ctx onChange dest (chosen |> Option.map box) chosen |}
        | FormFieldKind.Rating(allowHalf, max, FieldView.OnNumber onChange, FieldView.Number value) ->
            RatingControl.rating
                {| fieldId = spec.Name
                   className = "fuaran-filter-control"
                   max = max
                   allowHalf = allowHalf
                   // A chip's declarative write always has somewhere to go.
                   interactive = true
                   label = labelText
                   value =
                    BindingResolver.tryResolve ctx.Sources (auto value)
                    |> Option.defaultValue Fuaran.UI.Defaults.ControlValueDefaults.rating
                   commit = fun (v: float) -> fieldChange ctx onChange dest (Some(box v)) v |}
        | FormFieldKind.Tokens(allowFreeText, FieldView.OnTokens onChange, suggestions, FieldView.Tokens value) ->
            TokensControl.tokens
                {| fieldId = spec.Name
                   className = "fuaran-filter-control fuaran-tokens-input"
                   listClassName = "fuaran-tokens-suggestions"
                   required = false
                   allowFreeText = allowFreeText
                   placeholder = ""
                   suggestions = suggestions |> Option.map (BindingResolver.resolveOptions ctx.Sources)
                   tokens =
                    BindingResolver.tryResolve ctx.Sources (auto value)
                    |> Option.defaultValue Fuaran.UI.Defaults.ControlValueDefaults.tokens
                   commit = fun next -> fieldChange ctx onChange dest (Some(box next)) next |}
        | _ -> Emit.planWith (filterAttach ctx spec) (ElementPlan.InputPlan.filterControl ctx.Sources spec)

    Emit.plan (ElementPlan.InputPlan.filter ctx.Sources spec (ElementPlan.Plan.Host control))

and private renderFilters (ctx: RenderContext<'Msg>) (specs: FilterSpec<'Msg> list) : ReactElement =
    Emit.plan (ElementPlan.InputPlan.filters [ for spec in specs -> ElementPlan.Plan.Host(renderFilterSpec ctx spec) ])

and private renderFileUpload
    (ctx: RenderContext<'Msg>)
    // Phase 1117 — the node's own id, taken from the dispatch rather than from
    // `ctx.CurrentNodeId`: that field is populated only when an action sink is
    // wired, and the upload's reserved state slot is keyed by the node on every
    // host. A slot keyed by `""` on an unrecorded host would collide across
    // every upload in the tree.
    (nodeId: string)
    (spec: FileUploadSpec<'Msg>)
    : ReactElement =
    // Phase 2290 — the label, the input and its attributes are
    // `ElementPlan.InputPlan` (ruling R13), shared with the server; this binds
    // the selection.
    let onSelect (files: Browser.Types.File list) : unit =
        let selections: FileSelection list =
            // Phase 136: carry an opaque `FileRef` per selection. `Ref.Id` is an
            // index-qualified stable token (the only part that ever serialises);
            // `Ref.Handle` boxes the actual browser `File` so
            // `Action.ReadFileBody` can read the blob without the consumer
            // touching `FileReader`.
            files
            |> List.mapi (fun i f ->
                { Name = f.name
                  Size = int64 f.size
                  MimeType = f.``type``
                  Ref =
                    { Id = sprintf "%d:%s" i f.name
                      Handle = Some(box f) } })

        // Phase 1548 — the declared ceilings, applied at SELECTION time. A
        // selection outside a declared ceiling is REFUSED and REPORTED, and no
        // `onSelect` runs — never TRUNCATED: silently uploading a subset of the
        // set the reader chose is a failure with no symptom. `maxBytes` is per
        // file, the quantity the G1 gate measures on the `file-read` route.
        // Reported through `Warn` because this control has no failure channel
        // of its own; inventing one would be a wire change.
        let tooMany =
            match spec.MaxFiles with
            | Some limit when List.length selections > limit -> Some(sprintf "maxFiles of %d" limit)
            | _ -> None

        let tooLarge =
            match spec.MaxBytes with
            | Some limit when selections |> List.exists (fun s -> s.Size > int64 limit) ->
                Some(sprintf "maxBytes of %d" limit)
            | _ -> None

        match tooMany |> Option.orElse tooLarge with
        | Some breached ->
            ctx.Runtime.Warn(
                sprintf
                    "FileUpload '%s': the selection is outside this control's declared %s, so it was refused. Nothing was read and no handler ran."
                    nodeId
                    breached
            )
        | None ->
            // `OnSelect` is optional since the swap — an absent handler means
            // no dispatch.
            spec.OnSelect
            |> Option.iter (fun onSelect -> runAction ctx (onSelect selections))

    let attach: Emit.Attach =
        fun point _ ->
            match point with
            // Feliz's `File list` overload pulls the files off `e.target.files`.
            | ElementPlan.Attach.FileSelect -> [ prop.onChange onSelect ]
            | _ -> []

    // Phase 1115 — the ingress gestures. Neither declared renders the plain
    // picker; either hands the same children to `FileUploadDrop`, which wraps
    // them in the drag / paste affordance (a stateful component, by nature)
    // without redefining the control.
    let control =
        if spec.DropTarget || spec.AcceptPaste then
            FileUploadDrop.dropZone
                {| className = Css.Class.FileUpload
                   dropTarget = spec.DropTarget
                   acceptPaste = spec.AcceptPaste
                   accept = spec.Accept
                   multiple = spec.Multiple
                   children =
                    ElementPlan.InputPlan.fileUploadChildren ctx.Sources spec
                    |> List.map (Emit.planWith attach) |}
        else
            Emit.planWith attach (ElementPlan.InputPlan.fileUpload ctx.Sources spec)

    // Phase 1117 — the streaming leg. No declared destination pays nothing; a
    // declared one wraps the control in the shell that owns the transfer and
    // its status line (by nature). `OnSelect` above is untouched: "the reader
    // chose these files" and "those files now exist at a destination" are two
    // facts about one gesture.
    match spec.Destination with
    | None -> control
    | Some destination ->
        UploadStream.streamShell
            {| destination = destination
               nodeId = nodeId
               sink = ctx.UploadSink
               gate = fun () -> ctx.Runtime.CanDispatch(Runtime.ActionDescriptor.Upload destination)
               warn = ctx.Runtime.Warn
               setState = fun key value -> ctx.Runtime.SetState(key, value)
               children = [ control ] |}

// ─── Visualisations ────────────────────────────────────────────────────────

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
          RunAction = runAction ctx
          NodeId = parentNodeId
          EgressPolicy = ctx.Policy.Egress
          // Phase 1611 — the whole-rows write, supplied rather than reachable.
          // `writeBackTo` is where Phase 266's scope routing and Phase 782's
          // host-reserved-key refusal live, and an adapter holds neither the
          // runtime nor the scope; handing it the WRITE rather than the store
          // is what keeps an adapter-backed edit crossing the same guards a
          // first-party one does.
          WriteRows = fun destination rows -> writeBackTo ctx destination (Some(box rows)) }

    match ctx.VisAdapter.RenderGrid(spec, visCtx) with
    | Some rendered ->
        // Phase 1611 — `exportable` is honoured on BOTH backends by ONE
        // implementation, and it is this one. The export control is chrome
        // AROUND whatever drew the grid — exactly where it sits on the
        // first-party leg, outside the `<table>` — so the adapter needs no copy
        // of the serialiser, the scope rule, the filename rule or the dispatch
        // gate, and the two backends cannot come to disagree about any of them.
        // A grid that declares nothing emits byte-identical output to before.
        if not spec.Exportable then
            rendered
        else
            let rows =
                match BindingResolver.resolve<Row seq> ctx.Sources spec.Source with
                | BindingResolver.Resolved seq -> Seq.toList seq
                | _ -> []

            // The rows in the order the DECLARATION puts them in — the same
            // descriptor the adapter hands its own backend, through the same
            // Phase-861 sorter the first-party leg uses. What differs between
            // the backends is only the tie order within equal keys, which each
            // library settles for itself; the declared order is the same file.
            let sorted =
                rows
                |> GridModel.sortRowsByDescriptor
                    spec.Columns
                    (BindingResolver.effectiveSortDescriptor spec.SortStateKey spec.DefaultSort ctx.Sources)

            let project () =
                let cellText (col: ColumnErased<'Msg>) (row: Row) : string =
                    let value =
                        match col.Value with
                        | Some accessor -> accessor row
                        | None ->
                            match col.Field with
                            | Some field -> BindingResolver.projectRowFieldValue row field
                            | None -> CellValue.Empty

                    Formatting.formatCellValue col.Format value

                // Phase 1909 — an action column (Button / ButtonGroup) carries
                // no data, so the file carries no column for it, even where a
                // pre-existing tree still declares a `field` on one.
                let exported = spec.Columns |> List.filter (Fuaran.UI.GridColumn.isAction >> not)

                (exported |> List.map _.Label),
                (sorted
                 |> List.map (fun row -> exported |> List.map (fun col -> cellText col row)))

            Html.div
                [ prop.className "fuaran-grid-exportable"
                  prop.children
                      [ rendered
                        gridExportControl
                            ctx
                            parentNodeId
                            (GridExport.scope (AgGridPlan.hostPages spec) (List.length sorted))
                            project ] ]
    | None ->

        let resolution = BindingResolver.resolve<Row seq> ctx.Sources spec.Source

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

            // Phase 818 — `sortStateKey`: the grid sorts its RESOLVED rows by
            // the state-carried descriptor before rendering (runtime-side sort
            // — the author wires no Transform). No descriptor written yet ⇒
            // natural source order.
            // Phase 861 — the effective order: the state slot decides, and a
            // declared `defaultSort` fills only the not-yet-sorted case. A grid
            // with no sort state key still honours a declared order.
            let sortDescriptor =
                BindingResolver.effectiveSortDescriptor spec.SortStateKey spec.DefaultSort ctx.Sources

            let rows =
                (match resolution with
                 | BindingResolver.Resolved seq -> Seq.toList seq
                 | _ -> [])
                |> GridModel.sortRowsByDescriptor spec.Columns sortDescriptor

            // Phase 862 — `pageStateKey` + `pageSize`: the grid shows one page
            // at a time and owns the pager that moves between them. `hostPages`
            // is the source-shape rule (a `Query` depending on the page key
            // returns the page itself), in which case the pager still renders
            // and drives the query but the grid does NOT slice again.
            // Phase 1892 — `gridPage` is the one page rule both render legs
            // read; a HOST-paged grid with a declared `rowTotal` now clamps and
            // names its last page, and one without keeps to previous/next.
            let pageInfo = BindingResolver.gridPage ctx.Sources spec (List.length rows)

            let pagination =
                pageInfo
                |> Option.map (fun (key, size, page, hostPages, _) -> key, size, page, hostPages)

            // Phase 1892 — a grid whose HOST returns the window slices nothing:
            // the rows it resolved ARE the window, page and all.
            let hostWindows = GridModel.gridHostWindows spec

            // The rows this render actually paints. `rowOffset` is what makes
            // the page-relative index the cell loop hands back addressable in
            // the FULL set — without it a page-2 edit would commit to the
            // matching row of page 1 (Phase 663's write-back indexes `rows`).
            let pageRows, pageOffset =
                match pagination with
                | Some(_, size, page, false) when not hostWindows ->
                    GridModel.sliceRowsToPage size page rows, (page - 1) * size
                | _ -> rows, 0

            // Phase 1892 — the row window, over the page. A client-sliced
            // window's offset joins the page offset, so an edit in a scrolled
            // window commits to the row the reader saw; a host-windowed grid's
            // rows are all `rows` holds, so it adds nothing.
            let window = BindingResolver.gridWindow ctx.Sources spec pageRows

            let pageRows, rowOffset =
                match window with
                | Some w when not hostWindows -> w.Rows, pageOffset + w.Offset
                | Some w -> w.Rows, pageOffset
                | None -> pageRows, pageOffset

            match pageRows, state.OnEmpty with
            | [], Some emptyNode -> render ctx emptyNode
            | _ ->

                // Phase 427 — the default row-click write (the 423/426 archetype for the
                // Selection channel): a data-bearing grid whose `OnRowClick` is `None` writes the
                // clicked row to the reactive `SelectionStore` under its own `NodeId`, so every
                // `Binding.Selection` reader of this grid re-renders with the row — decoded
                // master-detail with zero host code. A `Some` closure dispatches exactly as
                // before and never touches the store (closure wins).
                let rowKeyOf: (Row -> string) option =
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
                        // The selection store is obj-typed across node kinds; this grid's
                        // own entry is always the boxed `Row` its click handler stored.
                        Map.tryFind (NodeId parentNodeId) ctx.Sources.Selections
                        |> Option.map (fun sel -> keyOf (unbox<Row> sel))
                        |> Option.filter usableKey
                    | None -> None

                // Phase 663 — the grid write-back floor (the Phase 426 control default replayed
                // for the grid): an editable grid commits an edited cell as the WHOLE updated
                // rows value, so every other reader of that key (a Chart sourced on the same
                // `$state` entry) re-renders with the edit.
                // Phase 863 — WHERE it commits is `GridModel.editDestination`, the same
                // precedence the reorder path resolves through: a declared `editStateKey` wins,
                // else the 663 floor (the grid's own `State` source), else no destination and
                // no input. That is what makes a declared destination act — a `Query`-sourced
                // grid naming `editStateKey` has a writable slot, which is exactly the corner
                // `nodes/grid-declared-edit.json` pins and which 863's widening of FUARAN090
                // already tells authors is live.
                // Phase 862 — `rowIndex` arrives page-relative; `rowOffset`
                // lifts it into the full sorted set so the whole rows value
                // written back carries every row, not just this page's.
                //
                // Phase 1157 — and this host mints NO per-control slot attribute
                // for the commit, deliberately. The charter walk refused a
                // `data-*` write-back slot hint naming the state key (see
                // `docs/VOCABULARY.md`, Appendix A, Interaction / affordance
                // cluster): the destination is already on the wire, the closure
                // below reaches it in-process, and an attribute publishing a
                // store ADDRESS would let a generic DOM wirer write the store
                // past the scope routing and the host-reserved-key guard that
                // `writeBackTo` crosses. Every `data-*` token this renderer does
                // emit carries identity WITHIN THE TREE, never a store address,
                // and that line is the ruling. A host whose renderer emits inert
                // HTML obtains this destination out of band from its own core,
                // not by reading it back out of its own markup.
                let editCommit: (int -> string -> obj -> unit) option =
                    GridModel.editDestination spec.Editable spec.EditStateKey spec.Source
                    |> Option.map (fun dest ->
                        fun (rowIndex: int) (field: string) (newValue: obj) ->
                            let absolute = rowOffset + rowIndex

                            let newRows =
                                rows
                                |> List.mapi (fun i row ->
                                    if i = absolute then
                                        updateRowField row field newValue
                                    else
                                        row)

                            writeBackTo ctx dest (Some(box (Seq.ofList newRows))))

                // Phase 1126 — STRUCTURED PASTE. The same destination, the same
                // gate, the same whole-rows write-back as a single-cell edit:
                // pasting a block IS editing cells, so it mints no second
                // destination and crosses no softer path. Nothing about it
                // reaches the wire (`GridPaste`'s header records why a
                // `pasteable` flag beside `exportable` would be a second
                // spelling of a permission `editable` already grants).
                //
                // ONE write for the whole block, and that is a correctness
                // requirement rather than an optimisation: `editCommit` above
                // computes its new rows from `rows` as captured by THIS render,
                // so applying a block cell by cell would compute every write
                // from the pre-paste rows and keep only the last one.
                //
                // Which columns can receive a value is decided by exactly the
                // rule the per-cell `commit` below uses — read it off the same
                // three facts (`col.Value` absent, `col.Field` present, a Text
                // or Numeric `Kind`, not `editable: false`) so a column that
                // cannot be typed into cannot be pasted into either.
                let pasteColumnTargets: GridPaste.ColumnTarget option list =
                    [ for col in spec.Columns ->
                          match col.Value, col.Field, col.Kind, col.Editable with
                          | _, _, _, Some false -> None
                          | None, Some field, CellKindErased.Numeric, _ -> Some { Field = field; Numeric = true }
                          | None, Some field, CellKindErased.Text, _ -> Some { Field = field; Numeric = false }
                          | _ -> None ]

                let pasteBlock: (int -> int -> string -> bool) option =
                    GridModel.editDestination spec.Editable spec.EditStateKey spec.Source
                    |> Option.map (fun dest ->
                        fun (rowIndex: int) (columnIndex: int) (clipboardText: string) ->
                            let block = GridPaste.parse clipboardText

                            if not (GridPaste.isBlock block) then
                                // A single field is an ordinary paste into the
                                // focused input: hand it back to the browser,
                                // which does it better (selection, partial
                                // replacement, the reader's own undo stack).
                                false
                            else
                                // `rowIndex` arrives page-relative; lift it into
                                // the full sorted set, exactly as the edit path
                                // does, so a paste on page two writes page two.
                                let plan =
                                    GridPaste.plan
                                        (List.length rows)
                                        pasteColumnTargets
                                        (rowOffset + rowIndex)
                                        columnIndex
                                        block

                                match plan.Writes with
                                | [] ->
                                    // Every cell was dropped or skipped. Take
                                    // the gesture anyway — writing nothing is
                                    // the outcome, and letting the browser
                                    // insert the raw block into the focused
                                    // input instead would be worse than doing
                                    // nothing.
                                    true
                                | writes ->
                                    let byRow = writes |> List.groupBy (fun w -> w.RowIndex) |> Map.ofList

                                    let newRows =
                                        rows
                                        |> List.mapi (fun i row ->
                                            match Map.tryFind i byRow with
                                            | None -> row
                                            | Some cellWrites ->
                                                cellWrites
                                                |> List.fold
                                                    (fun (acc: Row) (w: GridPaste.CellWrite) ->
                                                        let boxed =
                                                            match w.Value with
                                                            | CellValue.Numeric f -> box f
                                                            | CellValue.Text s -> box s
                                                            | _ -> box ""

                                                        updateRowField acc w.Field boxed)
                                                    row)

                                    writeBackTo ctx dest (Some(box (Seq.ofList newRows)))
                                    true)

                // Phase 934 — declarative row reorder. The DESTINATION resolves by
                // the same precedence the edit path uses (a declared
                // `editStateKey` wins; else the Phase-663 State-source floor;
                // else NO affordance — a handle over host data would be a
                // gesture with no destination, the fake-affordance class Phase
                // 866 charters against). The write is the WHOLE moved rows
                // value through `writeBackTo`, i.e. the same `treeStateWrite`
                // gate + host-reserved-key guard + Phase-889 invocation record
                // every tree-originated state write crosses — a reorder IS an
                // edit of the row order, so it mints no second destination and
                // crosses no softer gate.
                //
                // Suppressed while a sort descriptor is IN EFFECT (user-written
                // or `defaultSort`): the sort re-imposes its order on the next
                // render, so a drag would visibly snap back — an affordance
                // that lies. Clear the sort, and the handles return.
                let reorderCommit: (int -> int -> unit) option =
                    match sortDescriptor with
                    | Some _ -> None
                    | None ->
                        GridModel.reorderDestination spec.Reorderable spec.EditStateKey spec.Source
                        |> Option.map (fun dest ->
                            fun (fromAbs: int) (toAbs: int) ->
                                let moved = GridModel.moveRow fromAbs toAbs rows

                                // `moveRow` returns the SAME list instance for an
                                // out-of-range or no-op move; writing it back
                                // would churn every reader for nothing.
                                if not (obj.ReferenceEquals(moved, rows)) then
                                    writeBackTo ctx dest (Some(box (Seq.ofList moved))))

                // Phase 1123 — the RELEASING half. The source resolves its own
                // removal destination and hands the target a closure over it,
                // so neither grid ever reaches into the other's bindings; the
                // destination is the shared `gridWriteDestination` rule again,
                // never a second write path.
                //
                // `None` is not "inert": a column that is a filtered view over
                // one collection has no writable slot, and that is precisely the
                // case cross-container transfer exists for — the record is still
                // written to the shared key, and the application applies it.
                let releaseCommit: (int -> unit) option =
                    GridModel.transferDestination spec.TransferOutKey spec.EditStateKey spec.Source
                    |> Option.map (fun dest ->
                        fun (absolute: int) ->
                            let remaining = GridTransfer.removeAt absolute rows

                            if not (obj.ReferenceEquals(remaining, rows)) then
                                writeBackTo ctx dest (Some(box (Seq.ofList remaining))))

                // Phase 1123 — everything a lift carries. Built here because
                // only this grid knows its own rows, its own identity slot and
                // its own removal destination; the consuming grid gets a value
                // and a closure and needs none of that.
                let liftedAt (absolute: int) : GridTransfer.Lifted<Row> option =
                    if absolute < 0 || absolute >= List.length rows then
                        None
                    else
                        let row = List.item absolute rows

                        // A transfer must NAME what moved, and `rowKey` erases to
                        // the `"<closure>"` placeholder on decode. Without a usable
                        // identity the row can still be REORDERED inside its own
                        // grid, so the drag is still begun — only its transfer half
                        // is withheld. FUARAN130 reports the declaration that got
                        // here.
                        let itemId =
                            match rowKeyOf with
                            | Some keyOf ->
                                let k = keyOf row
                                if usableKey k then k else ""
                            | None -> ""

                        Some
                            { SourceNodeId = parentNodeId
                              SourceIndex = absolute
                              ItemId = itemId
                              OutKey = (if itemId = "" then None else spec.TransferOutKey)
                              Row = row
                              Release = releaseCommit |> Option.map (fun r -> fun () -> r absolute) }

                // Advertised only where a lift can actually happen — an
                // undiscoverable shortcut and an advertised one that does
                // nothing are the same defect from opposite sides.
                let transferShortcuts =
                    match spec.TransferOutKey with
                    | Some _ -> "ArrowUp ArrowDown Control+X"
                    | None -> "ArrowUp ArrowDown"

                // Phase 1123 — the RECEIVING half: write the record the wire
                // specifies, then apply each end's own commit where that end has
                // a destination. The record is written FIRST and unconditionally,
                // because it is the only part of a transfer that is promised on
                // every source shape.
                let transferInDestination =
                    GridModel.transferDestination spec.TransferInKey spec.EditStateKey spec.Source

                let receive (lifted: GridTransfer.Lifted<Row>) (index: int) : unit =
                    match spec.TransferInKey with
                    | None -> ()
                    | Some inKey ->
                        let at = GridTransfer.clampInsert index (List.length rows)

                        // Routed through `runSynthesisedAction` so the write
                        // crosses the same gate, scope routing and
                        // host-reserved-key guard as any tree-originated
                        // `Action.SetState` — the sort header's and the pager's
                        // path exactly.
                        runSynthesisedAction
                            ctx
                            (Action.SetState(
                                inKey,
                                Some(GridTransfer.record lifted.ItemId lifted.SourceNodeId parentNodeId at),
                                None
                            ))

                        transferInDestination
                        |> Option.iter (fun dest ->
                            writeBackTo ctx dest (Some(box (Seq.ofList (GridTransfer.insertAt at lifted.Row rows)))))

                        lifted.Release |> Option.iter (fun release -> release ())
                        gridDragSource <- None

                        announceTransfer
                            parentNodeId
                            (GridTransfer.placeAnnouncement lifted.ItemId at (List.length rows + 1))

                // Phase 1123 — the visible drop state, toggled IMPERATIVELY on
                // the element whose own handler is running. The renderer holds no
                // hooks, and `:hover` is not updated during an HTML5 drag, so
                // there is no declarative route to "a compatible row is over me";
                // a class added and removed inside the drag handlers touches
                // nothing React re-renders, since no render happens between
                // `dragover` and `drop`. It is transient view state and never
                // part of the document.
                let dropStateOn (target: obj) (on: bool) : unit =
                    if not (isNull target) then
                        let el = target :?> Browser.Types.HTMLElement

                        if on then
                            el.classList.add "fuaran-drag-over"
                        else
                            el.classList.remove "fuaran-drag-over"

                // The handle is a real <button>: focusable, keyboard-activatable
                // and screen-reader announced with no ARIA re-plumbing. Keyboard
                // is arrow keys on the focused handle — the current pattern for
                // list reorder (`aria-grabbed` is deprecated and deliberately
                // absent); pointer is native HTML5 drag onto any row.
                //
                // Phase 1123 — drawn for a grid that declares `transferOutKey`
                // too, not only a reorderable one: the handle IS the drag source
                // and the keyboard lift point, so a releasing grid without one
                // would declare a capability with no way to invoke it. The arrow
                // keys still belong to `reorderCommit` alone.
                let handleShown = reorderCommit.IsSome || spec.TransferOutKey.IsSome

                // The name states the routes this handle ACTUALLY has, because a
                // handle on a transfer-only grid cannot be arrow-keyed and one on
                // a reorder-only grid cannot be lifted out — announcing either as
                // available would be the fake affordance in words rather than in
                // markup.
                let handleLabel (absolute: int) (total: int) : string =
                    match reorderCommit.IsSome, spec.TransferOutKey.IsSome with
                    | true, true ->
                        sprintf
                            "Move row %d of %d — drag, press an arrow key to reorder, or Control+X to lift it into another list"
                            (absolute + 1)
                            total
                    | true, false ->
                        sprintf "Reorder row %d of %d — drag, or press an arrow key to move it" (absolute + 1) total
                    | false, _ ->
                        sprintf
                            "Move row %d of %d — drag, or press Control+X to lift it into another list"
                            (absolute + 1)
                            total

                let reorderCellFor (rowIndex: int) : ReactElement list =
                    if not handleShown then
                        []
                    else
                        let absolute = rowOffset + rowIndex
                        let total = List.length rows

                        [ Html.td
                              [ prop.className "fuaran-grid-reorder-cell"
                                prop.children
                                    [ Html.button
                                          [ prop.className "fuaran-grid-reorder-handle"
                                            prop.type' "button"
                                            prop.custom ("data-reorder-handle", string absolute)
                                            prop.draggable true
                                            prop.custom ("aria-label", handleLabel absolute total)
                                            prop.custom ("aria-keyshortcuts", transferShortcuts)
                                            prop.onDragStart (fun _ -> gridDragSource <- liftedAt absolute)
                                            prop.onDragEnd (fun _ -> gridDragSource <- None)
                                            prop.onKeyDown (fun (e: Browser.Types.KeyboardEvent) ->
                                                match e.key, reorderCommit with
                                                | "ArrowUp", Some commit ->
                                                    e.preventDefault ()
                                                    commit absolute (absolute - 1)
                                                | "ArrowDown", Some commit ->
                                                    e.preventDefault ()
                                                    commit absolute (absolute + 1)
                                                | key, _ ->
                                                    // Phase 1123 — the keyboard route into a transfer,
                                                    // beside the arrow keys rather than replacing them:
                                                    // a lift moves a row BETWEEN lists, the arrows move
                                                    // it WITHIN one, and between them they reach every
                                                    // position the pointer reaches. Lift fires only where
                                                    // this grid declares it may release — a chord that
                                                    // silently did nothing on a reorder-only grid would
                                                    // be exactly the fake affordance the announcements
                                                    // exist to prevent.
                                                    match
                                                        GridTransfer.keyIntent key (e.ctrlKey || e.metaKey),
                                                        spec.TransferOutKey
                                                    with
                                                    | GridTransfer.TransferIntent.Lift, Some _ ->
                                                        e.preventDefault ()

                                                        match liftedAt absolute with
                                                        | Some lifted ->
                                                            gridDragSource <- Some lifted

                                                            announceTransfer
                                                                parentNodeId
                                                                (GridTransfer.liftAnnouncement lifted.ItemId)
                                                        | None -> ()
                                                    | GridTransfer.TransferIntent.Cancel, _ ->
                                                        if gridDragSource.IsSome then
                                                            e.preventDefault ()
                                                            gridDragSource <- None

                                                            announceTransfer
                                                                parentNodeId
                                                                GridTransfer.cancelAnnouncement
                                                    | _ -> ())
                                            prop.text "\u28ff" ] ] ] ]

                let reorderHeaderCells: ReactElement list =
                    if not handleShown then
                        []
                    else
                        [ Html.th
                              [ prop.className "fuaran-grid-reorder-header"
                                prop.custom ("scope", "col")
                                prop.custom ("aria-label", (if spec.TransferOutKey.IsSome then "Move" else "Reorder")) ] ]

                // Phase 934 / Phase 1123 — the drop-target props a row gains.
                // `preventDefault` on dragover is what marks the row droppable to
                // the browser.
                //
                // ONE set of handlers for BOTH routes, deliberately: a row is a
                // drop target for its own grid's reorder and for a transfer
                // arriving from another grid, and two prop lists would hand React
                // two `onDrop`s of which only the last survives — a silent
                // shadowing rather than an error. Which route applies is decided
                // by comparing the lifted row's source id with this grid's,
                // exactly as the module-level holder's doc comment says.
                let reorderRowProps (rowIndex: int) : IReactProperty list =
                    let absolute = rowOffset + rowIndex

                    let reorderRoute (lifted: GridTransfer.Lifted<Row>) =
                        match reorderCommit with
                        | Some commit when lifted.SourceNodeId = parentNodeId -> Some commit
                        | _ -> None

                    let transferRoute (lifted: GridTransfer.Lifted<Row>) =
                        GridTransfer.canReceive lifted parentNodeId spec.TransferInKey

                    if reorderCommit.IsNone && spec.TransferInKey.IsNone then
                        []
                    else
                        [ prop.onDragOver (fun (e: Browser.Types.DragEvent) ->
                              match gridDragSource with
                              | Some lifted when (reorderRoute lifted).IsSome || transferRoute lifted ->
                                  e.preventDefault ()
                                  dropStateOn e.currentTarget true
                              | _ -> ())
                          prop.onDragLeave (fun (e: Browser.Types.DragEvent) -> dropStateOn e.currentTarget false)
                          prop.onDrop (fun (e: Browser.Types.DragEvent) ->
                              dropStateOn e.currentTarget false

                              match gridDragSource with
                              | Some lifted ->
                                  match reorderRoute lifted with
                                  | Some commit ->
                                      e.preventDefault ()
                                      gridDragSource <- None
                                      commit lifted.SourceIndex absolute
                                  | None ->
                                      if transferRoute lifted then
                                          e.preventDefault ()
                                          receive lifted absolute
                              | None -> ()) ]

                // Phase 1123 — the KEYBOARD landing point, and the pointer's
                // append target. One focusable control per accepting grid rather
                // than a tab stop on every row: a reader lifts with Control+X,
                // tabs to the list they want, activates this, and then uses Phase
                // 934's arrow keys to position the row. Two shipped affordances
                // reach between them everywhere the pointer reaches, and the tab
                // order grows by one element per LIST instead of one per ROW.
                //
                // Activated with nothing lifted it REFUSES OUT LOUD rather than
                // doing nothing: the precondition is the reader's to meet, and
                // silence would make a real control read as broken.
                let placeControl () : ReactElement =
                    match spec.TransferInKey with
                    | None -> Html.none
                    | Some _ ->
                        Html.button
                            [ prop.className "fuaran-drag-place"
                              prop.type' "button"
                              prop.custom ("aria-label", "Place the lifted row at the end of this list")
                              prop.onDragOver (fun (e: Browser.Types.DragEvent) ->
                                  match gridDragSource with
                                  | Some lifted when GridTransfer.canReceive lifted parentNodeId spec.TransferInKey ->
                                      e.preventDefault ()
                                      dropStateOn e.currentTarget true
                                  | _ -> ())
                              prop.onDragLeave (fun (e: Browser.Types.DragEvent) -> dropStateOn e.currentTarget false)
                              prop.onDrop (fun (e: Browser.Types.DragEvent) ->
                                  dropStateOn e.currentTarget false

                                  match gridDragSource with
                                  | Some lifted when GridTransfer.canReceive lifted parentNodeId spec.TransferInKey ->
                                      e.preventDefault ()
                                      receive lifted (List.length rows)
                                  | _ -> ())
                              prop.onClick (fun _ ->
                                  match gridDragSource with
                                  | Some lifted when GridTransfer.canReceive lifted parentNodeId spec.TransferInKey ->
                                      receive lifted (List.length rows)
                                  | _ -> announceTransfer parentNodeId GridTransfer.nothingLiftedAnnouncement)
                              prop.text "Place here" ]

                // Rendered EMPTY by both pipelines, so the server's markup and
                // the client's first render agree and hydration finds what it
                // expects; only a reader's own gesture ever puts text in it.
                let transferStatus () : ReactElement =
                    Html.div
                        [ prop.id (parentNodeId + "-fuaran-drag-status")
                          prop.className "fuaran-drag-status"
                          prop.custom ("role", "status")
                          prop.custom ("aria-live", "polite") ]

                // Phase 818 — the sortable-header affordance for a
                // `sortStateKey` grid. A header whose column declares a
                // `field` renders as a sortable affordance (the Phase-801
                // static-table presentation vocabulary: `data-sortable` +
                // live `aria-sort`, keyboard-activatable); clicking header N
                // dispatches the equivalent of
                // `SetState(sortStateKey, {"column": N, "direction": …})` —
                // routed through `runAction` so the gate + host-reserved-key
                // guard + scope-aware store apply exactly as any tree write.
                // A field-less closure column is not sortable and renders
                // without the affordance.
                // A column's identity is the row property it projects, and its
                // label where it projects none (a button or a checkbox column
                // names no field). Columns are re-orderable, so this is the
                // same argument the row key makes one axis over.
                let columnKey (colIndex: int) (col: ColumnErased<'Msg>) : string =
                    reconciliationKey (Some(defaultArg col.Field col.Label)) colIndex

                let sortableHeader (colIndex: int) (col: ColumnErased<'Msg>) : ReactElement =
                    // Phase 861 — the column flag NARROWS, never widens: absent
                    // inherits (sortable iff the column has a `field`), `false`
                    // opts out. `true` cannot turn the affordance on where the
                    // grid names no sort state key — FUARAN094 refuses that
                    // pre-emit, and the renderer simply does not draw it.
                    // Phase 1909 — the field is read through `GridColumn.dataField`,
                    // which is `None` on an action column whatever it declares,
                    // so a Button / ButtonGroup column never offers a sort.
                    match spec.SortStateKey, Fuaran.UI.GridColumn.dataField col, col.Sortable with
                    | Some sortKey, Some _, (None | Some true) ->
                        let active =
                            match sortDescriptor with
                            | Some(c, d) when c = colIndex -> Some d
                            | _ -> None

                        // Phase 801's three-state cycle, adopted for the bound
                        // path: ascending → descending → AUTHORED. The third
                        // state writes an empty descriptor rather than clearing
                        // the key, because a cleared key means "not yet sorted"
                        // and would re-apply `defaultSort` — the user would ask
                        // for the emitter's order and be given the declared one.
                        let dispatchToggle () =
                            let next =
                                match active with
                                | Some SortDirection.Asc -> JObj [ "column", JInt colIndex; "direction", JStr "desc" ]
                                | Some SortDirection.Desc -> JObj []
                                | None -> JObj [ "column", JInt colIndex; "direction", JStr "asc" ]

                            runSynthesisedAction ctx (Action.SetState(sortKey, Some next, None))

                        Html.th
                            [ prop.key (columnKey colIndex col)
                              prop.className "fuaran-grid-header"
                              prop.custom ("data-sortable", "")
                              prop.tabIndex 0
                              match active with
                              | Some SortDirection.Asc -> prop.custom ("aria-sort", "ascending")
                              | Some SortDirection.Desc -> prop.custom ("aria-sort", "descending")
                              | None -> ()
                              prop.onClick (fun _ -> dispatchToggle ())
                              prop.onKeyDown (fun e ->
                                  if e.key = "Enter" || e.key = " " then
                                      e.preventDefault ()
                                      dispatchToggle ())
                              prop.text col.Label ]
                    | _ ->
                        Html.th
                            [ prop.key (columnKey colIndex col)
                              prop.className "fuaran-grid-header"
                              prop.text col.Label ]

                // Phase 862 — the pager. RENDERER-OWNED, which is the whole
                // point of the Phase-860 rule: because the grid draws it, the
                // control that writes the page state and the grid that reads it
                // cannot come apart, so the decorative-pager shape (a button
                // writing state nothing reads) is not authorable here.
                // Host-paged grids cannot know the row total, so the pager
                // degrades to previous/next with no page count — a stated limit
                // of this cut, not an oversight.
                let pager () : ReactElement =
                    match pagination with
                    | None -> Html.none
                    | Some(pageKey, size, page, hostPages) ->
                        let lastPage = pageInfo |> Option.bind (fun (_, _, _, _, last) -> last)

                        let goTo (target: int) =
                            runSynthesisedAction
                                ctx
                                (Action.SetState(pageKey, Some(JObj [ "page", JInt target ]), None))

                        let atStart = page <= 1

                        let atEnd =
                            match lastPage with
                            | Some last -> page >= last
                            | None -> false

                        let step (label: string) (target: int) (disabled: bool) =
                            Html.button
                                [ prop.className "fuaran-grid-pager-step"
                                  prop.type' "button"
                                  prop.disabled disabled
                                  if not disabled then
                                      prop.onClick (fun _ -> goTo target)
                                  prop.text label ]

                        Html.nav
                            [ prop.className "fuaran-grid-pager"
                              prop.custom ("aria-label", "Pagination")
                              prop.children
                                  [ step "Previous" (page - 1) atStart
                                    Html.span
                                        [ prop.className "fuaran-grid-pager-status"
                                          // The page position changes without the
                                          // surrounding layout moving, so a screen
                                          // reader is told politely rather than
                                          // left to discover it.
                                          prop.custom ("aria-live", "polite")
                                          prop.text (
                                              match lastPage with
                                              | Some last -> sprintf "Page %d of %d" page last
                                              | None -> sprintf "Page %d" page
                                          ) ]
                                    step "Next" (page + 1) atEnd ] ]

                let gridTableWith (before: ReactElement list) (after: ReactElement list) =
                    Html.table
                        [ prop.className (
                              "fuaran-grid"
                              + Theme.gridPrintBreakClasses spec.KeepRowsTogether spec.RepeatHeader
                          )
                          // Phase 1892 — a windowed grid tells assistive
                          // technology its rows are a slice; an unwindowed one
                          // carries no attribute, so its markup is unchanged.
                          match GridModel.windowRowCount window with
                          | Some count -> prop.custom ("aria-rowcount", count)
                          | None -> ()
                          prop.children
                              [ Html.thead
                                    [ Html.tr
                                          [ prop.children (
                                                reorderHeaderCells
                                                @ [ for (colIndex, col) in List.indexed spec.Columns ->
                                                        sortableHeader colIndex col ]
                                            ) ] ]
                                Html.tbody
                                    [ prop.children
                                          [ yield! before
                                            for (rowIndex, row) in List.indexed pageRows ->
                                                let isSelected =
                                                    match selectedKey, rowKeyOf with
                                                    | Some sel, Some keyOf -> keyOf row = sel
                                                    | _ -> false

                                                // Phase 934 — the cell list is hoisted so the row can
                                                // prepend the reorder handle without re-shaping the
                                                // (unchanged) per-cell body below.
                                                let bodyCells: ReactElement list =
                                                    [ for (columnIndex, col) in List.indexed spec.Columns ->
                                                          // Editable write-back applies only on the declarative
                                                          // path — a Field-projected Text/Numeric cell with no
                                                          // Value closure (a closure's projection need not
                                                          // correspond to any row field, so there is nothing
                                                          // sound to write). Date and the interactive cell
                                                          // kinds keep their existing behaviour.
                                                          // Phase 863 — the column flag
                                                          // NARROWS the grid-level
                                                          // capability: an explicit `false`
                                                          // is read-only, the declaration
                                                          // "implied by omission" could not
                                                          // make.
                                                          let commit: (CellValue -> unit) option =
                                                              match
                                                                  editCommit,
                                                                  col.Value,
                                                                  col.Field,
                                                                  col.Kind,
                                                                  col.Editable
                                                              with
                                                              | _, _, _, _, Some false -> None
                                                              | Some ec,
                                                                None,
                                                                Some field,
                                                                (CellKindErased.Text | CellKindErased.Numeric),
                                                                _ ->
                                                                  Some(fun cv ->
                                                                      match cv with
                                                                      | CellValue.Numeric f ->
                                                                          ec rowIndex field (box f)
                                                                      | CellValue.Text s -> ec rowIndex field (box s)
                                                                      | _ -> ())
                                                              | _ -> None

                                                          // Phase 1126 — the block-paste handler for THIS
                                                          // cell: the anchor is where the reader's caret is,
                                                          // so the block spreads down and right from here.
                                                          // Only offered where the cell itself is editable —
                                                          // a paste has to land somewhere, and a read-only
                                                          // cell is not an anchor.
                                                          let paste: (string -> bool) option =
                                                              match commit, pasteBlock with
                                                              | Some _, Some pb ->
                                                                  Some(fun text -> pb rowIndex columnIndex text)
                                                              | _ -> None

                                                          Html.td
                                                              [ prop.key (columnKey columnIndex col)
                                                                prop.className "fuaran-grid-cell"
                                                                prop.children
                                                                    [ renderGridCell ctx commit paste col row ] ] ]

                                                Html.tr (
                                                    [ // The row's own identity, not its position. This
                                                      // is the arm the key rule exists for: a sort or a
                                                      // filter re-orders these siblings under the
                                                      // reader, and every per-row instance the renderer
                                                      // holds — an uncommitted cell edit, a `Local`
                                                      // input buffer — must move with its row rather
                                                      // than stay at its index.
                                                      prop.key (
                                                          reconciliationKey
                                                              (rowKeyOf |> Option.map (fun keyOf -> keyOf row))
                                                              rowIndex
                                                      )
                                                      // Phase 1701 — the interactive marker is appended
                                                      // AFTER the selection one, so a row's first paint
                                                      // (nothing selected yet) is byte-identical to the
                                                      // static emission every other host produces for
                                                      // the same grid. Selection is a gesture's
                                                      // consequence and arrives later, by which point
                                                      // there is no static render to agree with.
                                                      prop.className (
                                                          (if isSelected then
                                                               "fuaran-grid-row fuaran-grid-row-selected"
                                                           else
                                                               "fuaran-grid-row")
                                                          + Theme.gridRowInteractiveClass spec.OnRowClick.IsSome
                                                      )
                                                      prop.onClick (fun _ ->
                                                          gridRowSelected (runAction ctx) parentNodeId spec row)
                                                      match GridModel.windowRowIndex window rowIndex with
                                                      | Some index -> prop.custom ("aria-rowindex", index)
                                                      | None -> () ]
                                                    @ reorderRowProps rowIndex
                                                    @ [ prop.children (reorderCellFor rowIndex @ bodyCells) ]
                                                )
                                            yield! after ] ] ] ]

                // Phase 1911 — a grid declaring `windowStateKey` renders inside
                // the scroll viewport that WRITES the window as it moves (see
                // `renderGridWindowViewport`); a grid declaring none renders the
                // table exactly as before, with no spacer and no container.
                let gridTable =
                    match spec.WindowStateKey, window with
                    | Some windowKey, Some presented ->
#if FABLE_COMPILER
                        createViewportElement
                            (box renderGridWindowViewport)
                            (box
                                {| offset = presented.Offset
                                   presentedCount = List.length presented.Rows
                                   total = presented.Total
                                   colSpan = List.length spec.Columns + List.length reorderHeaderCells
                                   windowKey = windowKey
                                   current = BindingResolver.readWindowDescriptor ctx.Sources windowKey
                                   write =
                                    fun (key: string) (descriptor: JVal) ->
                                        runSynthesisedAction ctx (Action.SetState(key, Some descriptor, None))
                                   renderTable = gridTableWith |})
#else
                        // .NET has no React to mount the viewport: the table
                        // renders the window the seeded State determines and
                        // writes nothing — the static-host rule.
                        ignore windowKey
                        ignore presented
                        gridTableWith [] []
#endif
                    | _ -> gridTableWith [] []

                let paged =
                    match pagination with
                    | None -> gridTable
                    | Some _ ->
                        // The paged grid gains ONE wrapper element; an unpaged grid
                        // emits byte-identical DOM to before this phase.
                        Html.div [ prop.className "fuaran-grid-paged"; prop.children [ gridTable; pager () ] ]

                // Phase 1125 — the export affordance. What it takes is `rows`:
                // the fully RESOLVED, SORTED set the client holds, never
                // `pageRows`. A reader who sorted a column and then exported
                // expects the file in the order they are looking at, and a reader
                // on page three expects the grid rather than page three.
                //
                // Where the HOST pages (`hostPages` — a `Query` whose `dependsOn`
                // names the page key) that resolved set IS one page, and there is
                // no honest way for the client to export more. The control's
                // accessible name says which of the two it is, on the
                // declared-total ruling's reasoning: the tree cannot substantiate
                // data it does not hold, so a control that promised a whole
                // dataset and delivered a page would be a fake affordance by
                // understatement. A full-dataset export over a paged query is host
                // chrome and stays out of the language.
                let exported =
                    if not spec.Exportable then
                        paged
                    else
                        let hostPaged =
                            match pagination with
                            | Some(_, _, _, hostPages) -> hostPages
                            | None -> false

                        // The column's own value projection, rendered through the
                        // column's own declared format — the same two steps the
                        // cell takes, so the file carries what the reader sees.
                        // The formatted-versus-raw decision and its cost are
                        // recorded in `GridExport`'s header and in the charter row.
                        let project () =
                            let cellText (col: ColumnErased<'Msg>) (row: Row) : string =
                                let value =
                                    match col.Value with
                                    | Some accessor -> accessor row
                                    | None ->
                                        match col.Field with
                                        | Some field -> BindingResolver.projectRowFieldValue row field
                                        | None -> CellValue.Empty

                                Formatting.formatCellValue col.Format value

                            // Phase 1909 — an action column carries no data, so
                            // the file carries no column for it (the AG-Grid
                            // leg's export makes the same cut).
                            let exported = spec.Columns |> List.filter (Fuaran.UI.GridColumn.isAction >> not)

                            (exported |> List.map _.Label),
                            (rows |> List.map (fun row -> exported |> List.map (fun col -> cellText col row)))

                        Html.div
                            [ prop.className "fuaran-grid-exportable"
                              prop.children
                                  [ paged
                                    gridExportControl
                                        ctx
                                        parentNodeId
                                        (GridExport.scope hostPaged (List.length rows))
                                        project ] ]

                // Phase 1123 — a grid that declares NEITHER end of the transfer
                // pair emits byte-identical DOM to before this phase, which is
                // why the chrome is conditional rather than always present and
                // empty.
                if spec.TransferInKey.IsNone && spec.TransferOutKey.IsNone then
                    exported
                else
                    Html.div
                        [ prop.className "fuaran-grid-transfer"
                          prop.children [ exported; placeControl (); transferStatus () ] ]

and private renderGridCell
    (ctx: RenderContext<'Msg>)
    (commit: (CellValue -> unit) option)
    // Phase 1126 — a structured-paste handler for this cell, when the grid has
    // an edit destination and this column can receive one. Returns `true` when
    // it handled the block (the caller then suppresses the browser's own
    // paste), `false` when the payload was a single value and belongs to the
    // input's native handling.
    (paste: (string -> bool) option)
    (col: ColumnErased<'Msg>)
    (row: Row)
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

    // Phase 1126 — the paste props for an editable cell's input, or nothing.
    // A block is consumed and the default suppressed; a single value falls
    // through to the browser untouched, on exactly the `FileUploadDrop`
    // reasoning — swallowing every paste that reaches an input would be a route
    // to a bug nobody could see the cause of.
    let pasteProps: IReactProperty list =
        match paste with
        | None -> []
        | Some handle ->
            [ prop.onPaste (fun (e: Browser.Types.ClipboardEvent) ->
                  let text: string = e.clipboardData.getData "text/plain"

                  if not (System.String.IsNullOrEmpty text) && handle text then
                      e.preventDefault ()) ]

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
                              commitCell (CellValue.Numeric v))
                      yield! pasteProps ]
            | CellKindErased.Numeric, _ ->
                // An Empty (or non-numeric) cell in a Numeric column: text input, committed
                // only when the entry parses numerically.
                Html.input
                    [ prop.className "fuaran-grid-cell-editable"
                      prop.type'.text
                      prop.value (Formatting.formatCellValue CellFormat.None value)
                      prop.onChange (fun (v: string) ->
                          match System.Double.TryParse v with
                          | true, f -> commitCell (CellValue.Numeric f)
                          | false, _ -> ())
                      yield! pasteProps ]
            | _, _ ->
                let current =
                    match value with
                    | CellValue.Text s -> s
                    | other -> Formatting.formatCellValue CellFormat.None other

                Html.input
                    [ prop.className "fuaran-grid-cell-editable"
                      prop.type'.text
                      prop.value current
                      prop.onChange (fun (v: string) -> commitCell (CellValue.Text v))
                      yield! pasteProps ]
        | None -> Html.span [ prop.text (Formatting.formatCellValue col.Format value) ]
    // Phase 1126 — the `Editable` cell kind carries NO paste handler,
    // deliberately. Its commit is an author closure per cell, so a block would
    // have to be applied one `runAction` at a time and the author's own handler
    // would see N gestures where the reader made one. The block path belongs to
    // the DECLARATIVE cells above, whose destination the grid itself owns and
    // can write in a single pass.
    | CellKindErased.Editable onEdit ->
        // The generated `onEdit` handler is optional — a `None` handler keeps
        // the identical input DOM but commits nothing (inert, exactly like the
        // old decoded no-op action).
        let runEdit (edited: CellValue) =
            match onEdit with
            | Some f -> runAction ctx (f (row, edited))
            | None -> ()

        match value with
        | CellValue.Numeric n ->
            Html.input
                [ prop.className "fuaran-grid-cell-editable"
                  prop.type'.number
                  prop.value n
                  prop.onChange (fun (v: float) -> runEdit (CellValue.Numeric v)) ]
        | CellValue.Text s ->
            Html.input
                [ prop.className "fuaran-grid-cell-editable"
                  prop.type'.text
                  prop.value s
                  prop.onChange (fun (v: string) -> runEdit (CellValue.Text v)) ]
        | _ -> Html.span [ prop.text (Formatting.formatCellValue col.Format value) ]
    | CellKindErased.Checkbox(getValue, onToggle) ->
        let current = getValue row

        Html.input
            [ prop.type'.checkbox
              prop.isChecked current
              prop.onChange (fun (b: bool) ->
                  match onToggle with
                  | Some f -> runAction ctx (f (row, b))
                  | None -> ()) ]
    | CellKindErased.Button(label, onClick) ->
        Html.button
            [ prop.className "fuaran-grid-cell-button"
              prop.text (BindingResolver.resolveTextSource ctx.Sources label)
              prop.onClick (fun e ->
                  e.stopPropagation () // don't trigger row-click handler

                  match onClick with
                  | Some f -> runAction ctx (f row)
                  | None -> ()) ]
    | CellKindErased.ButtonGroup buttons ->
        Html.span
            [ prop.className "fuaran-grid-cell-button-group"
              prop.children
                  [ for (buttonIndex, item) in List.indexed buttons ->
                        Html.button
                            [ prop.key (reconciliationKey None buttonIndex)
                              prop.className "fuaran-grid-cell-button"
                              prop.text (BindingResolver.resolveTextSource ctx.Sources item.Label)
                              prop.onClick (fun e ->
                                  e.stopPropagation ()

                                  match item.OnClick with
                                  | Some f -> runAction ctx (f row)
                                  | None -> ()) ] ] ]
    | CellKindErased.Link(href, label) ->
        // Phase 1026 — the ambient destination policy, same as the `Link` node.
        // Worth stating that this call site is not an afterthought: the href
        // here comes from a ROW ACCESSOR over bound data, so a single decoded
        // tree emits one per row, and a grid pointed at attacker-influenced
        // rows is the highest-volume egress surface the renderer has.
        let safeHref, egressAttrs =
            Sanitize.sanitizeUrlForEgress ctx.Policy.Egress Sanitize.EgressClass.Hyperlink (href row)

        Html.a (
            [ prop.className "fuaran-grid-cell-link"
              prop.href safeHref
              prop.text (BindingResolver.resolveTextSource ctx.Sources (label row)) ]
            @ Emit.attrs egressAttrs
        )
    | CellKindErased.Pill(label, tone) ->
        Html.span
            [ prop.className (Css.gridCellPill (Theme.toneVar (tone row)))
              prop.text (BindingResolver.resolveTextSource ctx.Sources (label row)) ]
    | CellKindErased.TonedPill(field, toneMap, defaultTone) ->
        // Phase 750 — the declarative twin. Deliberately the SAME element, class
        // vocabulary and text as the hosted `Pill` arm above: the wire variant exists
        // to make the tone rule *expressible*, not to render differently, and a
        // parity test pins the two against each other.
        let label, tone = GridModel.tonedPillOf row field toneMap defaultTone

        Html.span [ prop.className (Css.gridCellPill (Theme.toneVar tone)); prop.text label ]
    | CellKindErased.Progress(fraction, label) ->
        let f = fraction row

        Html.div
            [ prop.className "fuaran-grid-cell-progress"
              prop.children
                  [ Html.div
                        [ prop.className "fuaran-grid-cell-progress-fill"
                          prop.style [ style.width (length.percent (f * 100.0)) ] ]
                    match label with
                    | Some l -> Html.span [ prop.text (BindingResolver.resolveTextSource ctx.Sources (l row)) ]
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
          RunAction = runAction ctx
          NodeId = parentNodeId
          EgressPolicy = ctx.Policy.Egress
          // Phase 1611 — the same write path the grid leg supplies. A chart has
          // no whole-rows destination of its own, so this is the identity of
          // `writeBackTo` and nothing on the chart path calls it; supplying it
          // here rather than making the field optional keeps ONE context shape
          // across the seam, which is what lets a single adapter serve both.
          WriteRows = fun destination rows -> writeBackTo ctx destination (Some(box rows)) }

    match ctx.VisAdapter.RenderChart(spec, visCtx) with
    | Some rendered -> rendered
    | None ->
        // Phase 2290 — the state slots, the lowered Drawing SVG and the
        // placeholder (ruling R11) are `ElementPlan.VisPlan.chart`, shared with
        // the server. Phase 933 — the POINT-SELECTION DEFAULT: one handler on
        // the container maps `event.target` back to a datum through Phase 642's
        // `data-fuaran-mark` (the SVG is a raw string, so there is no per-mark
        // element to attach to); an `OnPointClick` of `None` publishes the
        // clicked datum under the chart's own node id. POINT SELECTION ONLY —
        // 866 left zoom and brush out.
        let drawn: Row list ref = ref []

        let svg (rows: Row list) : string =
            drawn.Value <- rows
            Fuaran.UI.Charts.renderSvg ctx.Sources (BindingResolver.resolveTextSource ctx.Sources) spec rows

        Emit.planWith
            (fun point _ ->
                match point with
                | ElementPlan.Attach.ChartPointClick ->
                    [ prop.onClick (fun (ev: Browser.Types.MouseEvent) ->
                          let clicked = ev.target :?> Browser.Types.Element
                          // Chrome (axes, gridlines, labels, the legend) carries
                          // no mark, so a click on it is a no-op.
                          match clicked.closest "[data-fuaran-mark]" with
                          | None -> ()
                          | Some marked ->
                              let markId = marked.getAttribute "data-fuaran-mark"

                              if not (isNull markId) then
                                  chartPointClick (runAction ctx) parentNodeId spec drawn.Value markId) ]
                | _ -> [])
            (ElementPlan.VisPlan.chart
                (planContext ctx parentNodeId)
                (Some state)
                spec
                (Fuaran.UI.Charts.isLowered spec.Kind)
                svg)

/// Hand a CSV document to the reader (Phase 1125).
///
/// Deliberately NOT a new mechanism. This is the platform's existing download
/// instruction — a url and a suggested name — performed the way that
/// instruction is performed everywhere else: an anchor carrying a `download`
/// attribute, activated and discarded. Nothing about the export earns a second
/// delivery path, and minting one would leave two spellings of "give this to
/// the reader" for a future change to keep in step.
and private deliverDownload (url: string) (suggestedName: string) : unit =
#if FABLE_COMPILER
    let anchor = Browser.Dom.document.createElement "a"
    anchor.setAttribute ("href", url)
    anchor.setAttribute ("download", suggestedName)
    // Detached anchors do not activate on every browser, so it is attached,
    // clicked and removed within one synchronous turn — the reader never sees
    // it, and no element outlives the gesture.
    Browser.Dom.document.body.appendChild anchor |> ignore
    anchor.click ()
    Browser.Dom.document.body.removeChild anchor |> ignore
#else
    // .NET-side no-op — `Browser.Dom` is meaningless under `dotnet build`, the
    // same posture the focus and tab helpers take. Every DECISION this
    // affordance makes lives in `GridExport`, which is pure and is tested; what
    // is unreachable here is only the handing over.
    ignore url
    ignore suggestedName
#endif

/// The grid's export control (Phase 1125).
///
/// RENDERER-OWNED, which is the whole of the grid-behaviour rule and the reason
/// a decorative export button is not authorable: the control that serialises the
/// rows and the grid that holds them cannot come apart, because they are the
/// same node.
///
/// `project` is a THUNK. Serialising a grid on every render to fill a button
/// nobody has pressed would be a per-render cost proportional to the row count,
/// and the rows are re-projected on the render that follows every sort, every
/// page and every edit. It runs once, inside the gesture.
///
/// The gate is consulted at ACTIVATION, not at render: refusing to draw the
/// control would tell the reader nothing, and drawing one that silently does
/// nothing is the fake-affordance shape. A denied export warns through the same
/// diagnostic channel every other refusal uses.
and private gridExportAttach
    (ctx: RenderContext<'Msg>)
    (nodeId: string)
    (project: unit -> string list * string list list)
    : Emit.Attach =
    fun point _ ->
        match point with
        | ElementPlan.Attach.GridExport ->
            [ prop.onClick (fun _ ->
                  applyDispatchGateOutcome ctx.Runtime (Runtime.ActionDescriptor.Export nodeId) (fun () ->
                      let headers, rows = project ()
                      let csv = GridExport.document headers rows
                      deliverDownload (GridExport.dataUrl csv) (GridExport.filename nodeId))
                  |> ignore) ]
        | _ -> []

/// The grid's export control (Phase 1125).
///
/// RENDERER-OWNED, which is the whole of the grid-behaviour rule and the reason
/// a decorative export button is not authorable: the control that serialises the
/// rows and the grid that holds them cannot come apart, because they are the
/// same node.
///
/// `project` is a THUNK. Serialising a grid on every render to fill a button
/// nobody has pressed would be a per-render cost proportional to the row count,
/// and the rows are re-projected on the render that follows every sort, every
/// page and every edit. It runs once, inside the gesture.
///
/// The gate is consulted at ACTIVATION, not at render: refusing to draw the
/// control would tell the reader nothing, and drawing one that silently does
/// nothing is the fake-affordance shape. A denied export warns through the same
/// diagnostic channel every other refusal uses. Phase 2290 — the button is
/// `ElementPlan.VisPlan.exportButton`, the one the static grid's plan carries.
and private gridExportControl
    (ctx: RenderContext<'Msg>)
    (nodeId: string)
    (scope: GridExport.Scope)
    (project: unit -> string list * string list list)
    : ReactElement =
    Emit.planWith (gridExportAttach ctx nodeId project) (ElementPlan.VisPlan.exportButton (GridExport.scopeLabel scope))

and private renderMap
    (ctx: RenderContext<'Msg>)
    (parentNodeId: string)
    (state: StateBehaviour<'Msg>)
    (spec: MapSpec<'Msg>)
    : ReactElement =
    // Phase 2290 — the state slots, the placeholder and the marker list are
    // `ElementPlan.VisPlan.map` (ruling R12), shared with the server; a
    // marker's click is bound here when `OnMarkerClick` is wired. Real map
    // rendering needs a host-provided library — adapter-shaped, like charts.
    Emit.planWith
        (fun point key ->
            match point, spec.OnMarkerClick with
            | ElementPlan.Attach.MarkerClick, Some onMarkerClick ->
                [ prop.onClick (fun _ ->
                      BindingResolver.tryResolve ctx.Sources spec.Source
                      |> Option.bind (List.tryItem (int key))
                      |> Option.iter (fun marker -> runAction ctx (onMarkerClick marker))) ]
            | _ -> [])
        (ElementPlan.VisPlan.map (planContext ctx parentNodeId) (Some state) spec)

// ─── Public entry point ────────────────────────────────────────────────────

/// Render a Fuaran `Node<'Msg>` to a Feliz `ReactElement` against an
/// explicit context (sources + runtime + dispatch).
/// Fuaran-UI Phase 1535 — CONDITIONAL PRESENCE, and the reason `render` is now
/// two lines in front of `renderPresent` rather than one function.
///
/// A resolved `false` on `node.Visible` removes the node from the output
/// entirely: no element, no placeholder, no `aria-hidden`, nothing in the layout
/// and nothing in the accessibility tree. The rule itself lives in
/// `BindingResolver.isNodeVisible` — ONE definition, shared with the server
/// renderer, because a node the client draws and the server omits is a hydration
/// mismatch.
///
/// The guard sits on `render` rather than at each of the ~90 call sites that
/// produce a child, so a kind added tomorrow inherits it without anyone
/// remembering to.
///
/// Absence, `NotResolved` and `Errored` all RENDER. An `Errored` predicate
/// additionally warns: a document asked a question the renderer could not
/// answer, and the node is on screen when its author may have meant it not to
/// be, which is exactly the state somebody should be told about.
///
/// **Depth-bounded** (Phase 2041), with the server renderer's limit, marker and
/// order: the depth is checked BEFORE visibility, as the server checks it, so a
/// hidden node past the limit renders the marker on both tiers. The root renders
/// at depth 1 and every nested `render` one deeper; past
/// `WireLimits.MaxDepth` the subtree is replaced by the shared depth marker and
/// the omission is reported through `Warn`.
and render (ctx: RenderContext<'Msg>) (node: Node<'Msg>) : ReactElement =
    let depth = RenderDepth.current () + 1

    if depth > Fuaran.UI.WireLimits.MaxDepth then
        ctx.Runtime.Warn(sprintf "[Fuaran] node '%s' %s" node.Id RenderParity.depthExceededText)
        Emit.plan (ElementPlan.NodePlan.depthExceeded node.Id)
    else
        RenderDepth.set depth

        try
            match BindingResolver.nodeVisibility ctx.Sources node with
            | Some(BindingResolver.Resolved false) -> Html.none
            | Some(BindingResolver.Errored m) ->
                ctx.Runtime.Warn(
                    sprintf "[Fuaran] node '%s' visible predicate errored (%s) — rendering the node" node.Id m
                )

                renderPresent ctx node
            | _ -> renderPresent ctx node
        finally
            RenderDepth.set (depth - 1)

and private renderPresent (ctx: RenderContext<'Msg>) (node: Node<'Msg>) : ReactElement =
    // `Node.Id` is a bare string in the generated envelope (the `NodeId`
    // wrapper erased at the tree level).
    let id = node.Id

    // Phase 889 — stamp the node onto the context the handler closures below
    // capture, so a user-action record can name the node the gesture was
    // attached to. `runAction` receives the resolved `Action` and nothing else,
    // so there is no other route to it.
    //
    // Guarded on the sink being wired: this is the per-node hot path and an
    // unrecorded render must not pay a record copy per node per frame (GP 13).
    let ctx =
        match ctx.ActionSink with
        | Some _ -> { ctx with CurrentNodeId = Some id }
        | None -> ctx

    // Phase 2108 — the wrapper is the shared `NodePlan`: tooltip, motion class,
    // bidi, accessibility / extra-attribute routing and the wrapper props are
    // decided once for both renderers, and this tier only emits them.
    let routing = ElementPlan.NodePlan.route ctx.Sources node

    // The dismissal listener (WCAG 1.4.13) is document-level and installed once,
    // lazily, the first time any node renders a hint. See
    // `ensureTooltipDismissal` for why it is not a per-node handler.
    if routing.TooltipText.IsSome then
        ensureTooltipDismissal ()

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
            // `Node.State` is optional in the generated envelope — `None` is
            // the old all-`None` slots record, so materialise
            // `Defaults.stateBehaviour` for the per-Kind dispatch (identical
            // slot behaviour for decoded trees).
            renderKind
                ctx
                id
                (node.State |> Option.defaultValue Fuaran.UI.Defaults.stateBehaviour)
                node.Kind
                routing.SemanticAttrs
        with ex when not ctx.InErrorBoundary ->
            let corrId =
                emitRenderFailureWithContext
                    ctx.TelemetrySink
                    ctx.SessionContext
                    id
                    (nodeKindName node.Kind)
                    ex.Message
                    RenderFailureSource.PerNodeGuard

            Emit.plan (ElementPlan.NodePlan.renderFailure id (nodeKindName node.Kind) ex.Message corrId)

    Emit.plan (ElementPlan.NodePlan.wrapper id routing (ElementPlan.Plan.Host kindBody))

// Wire the late-bound guest renderer (Phase 266): now that the recursive `render`
// group is fully defined, bind the hook to `render` instantiated at `obj`. This
// runs once at module initialisation, before any render is invoked, so the
// `Mount` arm always sees `Some`. The assignment lives OUTSIDE the recursive
// group, so instantiating `render` at `obj` here is not polymorphic recursion.
renderGuestHook <- Some render

/// Phase 1812 — the BEHIND reader's render (WIRE_FORMAT §15.3). A `Rendered`
/// view — the node itself, or the author-declared `fallback` lifted out of a
/// transport-only `Unknown` — renders through `render` exactly as any node
/// does; a `Placeholder` is the labelled degrade the section has always
/// specified, byte-identical to the server's (parity). The lift itself is
/// `Fuaran.UI.Ops.JsonDecode.BehindReader.view`; this renderer never sees an
/// `Unknown`, only what the reader decided to show.
let renderBehind (ctx: RenderContext<'Msg>) (view: BehindView<'Msg>) : ReactElement =
    match view with
    | BehindView.Rendered node -> render ctx node
    | BehindView.Placeholder(kind, required) ->
        let label =
            match required with
            | Some p -> sprintf "needs %s" p
            | None -> sprintf "unknown kind %s" kind

        Html.div
            [ prop.className Css.Class.UnknownPlaceholder
              prop.custom ("data-fuaran-kind", kind)
              match required with
              | Some p -> prop.custom ("data-fuaran-requires", p)
              | None -> ()
              prop.text label ]

// ─── Convenience entry points ──────────────────────────────────────────────
//
// Each entry below is `render` over `RenderContext.create` with the fields it
// lets a host supply, and nothing else; `docs/RENDER-ENTRIES.md` is the grid of
// which axes each one pins. A host needing a combination no entry offers builds
// `{ RenderContext.create sources dispatch node with … }` and calls `render`,
// rather than this family growing one permutation per field.

/// `render` with every axis at its convenience default (`RenderContext.create`):
/// the diagnostic runtime, so anything effecting is inert, and no sink.
let renderWithSources
    (sources: BindingResolver.BindingSources)
    (dispatch: 'Msg -> unit)
    (node: Node<'Msg>)
    : ReactElement =
    render (RenderContext.create sources dispatch node) node

/// `renderWithSources` with an EXPLICIT destination policy (Phase 1026) — the
/// named opt-out from the ambient default-deny.
///
/// Named rather than inferred, for the reason every other gate inversion in this
/// codebase is: a grep for `permissiveEgress` (or for this function) finds every
/// host that has widened its egress, so the choice is visible in the host's own
/// source instead of inherited silently. Passing `Sanitize.denyNonLocalEgress`
/// here is exactly `renderWithSources`.
///
/// Three postures a host reaches for, in descending order of how much they
/// should have to justify:
///
///   - `Sanitize.denyNonLocalEgress |> Sanitize.allowOrigin (Sanitize.HostSuffix "cdn.example") [ Sanitize.EgressClass.Media ]`
///     — the shape to prefer. Declares WHAT may be reached and FOR WHAT, and
///     stays default-deny for everything else.
///   - `Sanitize.denyNonLocalEgress` with `AllowNonNetwork = true` — the
///     narrowest fix for a surface whose only external destination is a
///     `mailto:` / `tel:` link.
///   - `Sanitize.permissiveEgress` — every destination, for a HAND-AUTHORED
///     tree where the author is the trust boundary. Correct for a catalog or a
///     sample; wrong for anything that renders a decoded tree.
let renderWithSourcesAndEgress
    (sources: BindingResolver.BindingSources)
    (egressPolicy: Sanitize.EgressPolicy)
    (dispatch: 'Msg -> unit)
    (node: Node<'Msg>)
    : ReactElement =
    let ctx = RenderContext.create sources dispatch node

    render
        { ctx with
            Policy =
                { ctx.Policy with
                    Egress = egressPolicy } }
        node

/// `render` with the host's runtime and telemetry sink — the ordinary
/// production host. Per-node-guard catches and `ErrorBoundary` catches are
/// emitted to the sink.
let renderWithSourcesAndSink
    (sources: BindingResolver.BindingSources)
    (runtime: Runtime.IFuaranRuntime)
    (telemetrySink: IFuaranTelemetrySink)
    (dispatch: 'Msg -> unit)
    (node: Node<'Msg>)
    : ReactElement =
    render
        { RenderContext.create sources dispatch node with
            Runtime = runtime
            TelemetrySink = Some telemetrySink }
        node

/// Correlation-aware render entry (Phase 330): `renderWithSourcesAndSink` plus
/// the host's opaque `SessionContext`. Superseded by `RenderContext.create`
/// plus `with`; it has no in-repo caller.
[<System.Obsolete("Use render { RenderContext.create sources dispatch node with Runtime = runtime; TelemetrySink = Some sink; SessionContext = context } node (Phase 2067).")>]
let renderWithSourcesSinkAndContext
    (sources: BindingResolver.BindingSources)
    (runtime: Runtime.IFuaranRuntime)
    (telemetrySink: IFuaranTelemetrySink)
    (sessionContext: Map<string, string>)
    (dispatch: 'Msg -> unit)
    (node: Node<'Msg>)
    : ReactElement =
    render
        { RenderContext.create sources dispatch node with
            Runtime = runtime
            TelemetrySink = Some telemetrySink
            SessionContext = sessionContext }
        node

/// User-action-recording render entry (Phase 889):
/// `renderWithSourcesSinkAndContext` plus the `IActionInvocationSink` every
/// gesture dispatched inside this tree is recorded through. Superseded by
/// `RenderContext.create` plus `with`; it has no in-repo caller.
[<System.Obsolete("Use render { RenderContext.create sources dispatch node with Runtime = runtime; TelemetrySink = Some sink; SessionContext = context; ActionSink = actionSink } node (Phase 2067).")>]
let renderWithSourcesSinkContextAndActionSink
    (sources: BindingResolver.BindingSources)
    (runtime: Runtime.IFuaranRuntime)
    (telemetrySink: IFuaranTelemetrySink)
    (sessionContext: Map<string, string>)
    (actionSink: IActionInvocationSink option)
    (dispatch: 'Msg -> unit)
    (node: Node<'Msg>)
    : ReactElement =
    render
        { RenderContext.create sources dispatch node with
            Runtime = runtime
            TelemetrySink = Some telemetrySink
            SessionContext = sessionContext
            ActionSink = actionSink }
        node

/// Scope-aware render entry (Phase 266, §4o): `render` with the host's runtime,
/// under the runtime scope `scopeId` (`RenderContext.inScope`). `Binding.State`
/// reads resolve against `StateStore.forScope scopeId` (its snapshot merged over
/// `sources.State`, scoped values winning) and `Action.SetState` writes route to
/// that isolated store, so a whole tree — or a `Mount` guest the orchestration
/// tier renders directly — keeps its state off the process-global default
/// store. The orchestration tier maps its `FuaranRuntimeScope` to the `scopeId`
/// string.
let renderWithSourcesInScope
    (scopeId: string)
    (sources: BindingResolver.BindingSources)
    (runtime: Runtime.IFuaranRuntime)
    (dispatch: 'Msg -> unit)
    (node: Node<'Msg>)
    : ReactElement =
    render
        { (RenderContext.create sources dispatch node |> RenderContext.inScope scopeId) with
            Runtime = runtime }
        node

/// `renderWithSourcesInScope` plus the telemetry sink of
/// `renderWithSourcesAndSink`. Isolation and observability are orthogonal: the
/// host most likely to need a scope (a registered-custom-renderer host mounting
/// guests) is also the host most likely to see render failures it did not
/// author, so no entry makes it trade one for the other.
let renderWithSourcesInScopeAndSink
    (scopeId: string)
    (sources: BindingResolver.BindingSources)
    (runtime: Runtime.IFuaranRuntime)
    (telemetrySink: IFuaranTelemetrySink)
    (dispatch: 'Msg -> unit)
    (node: Node<'Msg>)
    : ReactElement =
    render
        { (RenderContext.create sources dispatch node |> RenderContext.inScope scopeId) with
            Runtime = runtime
            TelemetrySink = Some telemetrySink }
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
    // Per-render hot path (Phase 207): each store overlays its live values onto
    // the caller's map through its own NAMED READ VIEW (`overlayOnto`). The
    // pre-207 route called `snapshot ()` first, which materialised a whole
    // intermediate `Map` per store per render for the sole purpose of folding it
    // away again — four throwaway maps on every reactive re-render. The
    // `isEmpty ()` guards are unchanged in meaning (an untouched store leaves
    // `sources` byte-identical, record copy included) and now answer without
    // building anything. Result-identical; do NOT "simplify" back to
    // `snapshot () |> Map.fold`.
    let withState =
        if StateStore.isEmpty () then
            sources
        else
            { sources with
                State = StateStore.overlayOnto sources.State }

    // Filter twin (Phase 423): merge the live `FilterStore` values over `sources.Filters` (store
    // wins) so a decoded chip's `$filters.<name>` write flows to every `Binding.Filter` reader.
    let withFilters =
        if FilterStore.isEmpty () then
            withState
        else
            { withState with
                Filters = FilterStore.overlayOnto withState.Filters }

    // Selection twin (Phase 427): merge the live `SelectionStore` values over
    // `sources.Selections` (store wins; raw node-id strings re-wrap as `NodeId`) so a default
    // row-click write flows to every `Binding.Selection` reader.
    let withSelections =
        if SelectionStore.isEmpty () then
            withFilters
        else
            { withFilters with
                Selections = SelectionStore.overlayOntoBy NodeId withFilters.Selections }

    // Query twin (Phase 428): merge the live `QueryStore` values (declarative `Call … into
    // Query` results) over `sources.QueryResults` (store wins) so a written slot flows to every
    // `Binding.Query` reader.
    if QueryStore.isEmpty () then
        withSelections
    else
        { withSelections with
            QueryResults = QueryStore.overlayOnto withSelections.QueryResults }

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
    Html.style [ prop.dangerouslySetInnerHTML (TrustedTypes.html (Theme.toCss theme)) ]

/// Phase 2482 — the chart tokens `renderWithTheme` last installed, so a
/// re-render under an unchanged theme does not re-measure its palette (and a
/// refused palette is reported once, not once per frame).
let mutable private installedThemeCharts: ChartTokens option = None

/// Install the theme's chart style (validated; a refused palette installs the
/// shipped default and is reported by slot or pair through `Diagnostics`). In
/// the browser the page has one theme, so the theme a page renders under IS the
/// chart style its charts lower under.
let private installThemeCharts (theme: Theme) : unit =
    if installedThemeCharts <> Some theme.Charts then
        installedThemeCharts <- Some theme.Charts

        for finding in Fuaran.UI.Charts.installChartStyleFromTheme theme do
            Diagnostics.warn
                "the theme's chart palette was refused and the default palette is drawn"
                (box (Fuaran.UI.ChartPalette.describe finding))

/// Convenience entry point that mounts a `Theme`'s CSS-variable bundle
/// alongside the rendered node tree. `Defaults.theme` mirrors the post-
/// `fuaran-reference.css` byte-for-byte, so apps that pass it see no
/// visual change. Apps composing their own Theme record get
/// it picked up at the renderer root without touching any stylesheet
/// file.
///
/// Phase 2482: it also installs the theme's chart style before the tree
/// renders, so every chart on the page draws the theme's series palette — or,
/// if the palette fails a gate, the shipped default, never the failing colour.
let renderWithTheme
    (theme: Theme)
    (sources: BindingResolver.BindingSources)
    (dispatch: 'Msg -> unit)
    (node: Node<'Msg>)
    : ReactElement =
    installThemeCharts theme
    React.Fragment [ themeStyleElement theme; renderWithSources sources dispatch node ]

// ─── Strict-CSP rendering (Phase 1545) ─────────────────────────────────────

/// `themeStyleElement` under an explicit CSP posture. Under `Csp.Strict nonce`
/// the element carries `nonce="…"`, so a host serving `style-src 'nonce-…'` can
/// mount the theme without `'unsafe-inline'`; under `Permissive` it is
/// `themeStyleElement`, unchanged. The raw-HTML sink still mints through the
/// `fuaran-renderer` Trusted Types policy — the two postures are orthogonal and
/// a host may send both directives.
let themeStyleElementWithCsp (csp: Csp.CspMode) (theme: Theme) : ReactElement =
    match Csp.nonce csp with
    | Some n ->
        Html.style
            [ prop.custom ("nonce", n)
              prop.dangerouslySetInnerHTML (TrustedTypes.html (Theme.toCss theme)) ]
    | None -> themeStyleElement theme

/// `renderWithSources` under an explicit CSP posture — the client half of the
/// strict mode, and the entry point a HYDRATING host calls.
///
/// Under `Csp.Strict` this renderer emits no `style` attribute on any node the
/// server also rendered: each continuous value rides the class the server
/// derived for the same node and slot, whose declaration is in the collected
/// `<style>` element already in the document. Hydration therefore leaves the
/// policy exactly as the server left it.
///
/// It does NOT mint a stylesheet of its own, and that is the boundary rather
/// than an omission: the declarations belong to the document the server sent,
/// and a nonce this tier invented would match no header. `SANITIZATION.md`
/// records what the mode does and does not claim on this path.
let renderWithSourcesAndCsp
    (csp: Csp.CspMode)
    (sources: BindingResolver.BindingSources)
    (dispatch: 'Msg -> unit)
    (node: Node<'Msg>)
    : ReactElement =
    let ctx = RenderContext.create sources dispatch node

    render
        { ctx with
            Policy = { ctx.Policy with Csp = csp } }
        node
