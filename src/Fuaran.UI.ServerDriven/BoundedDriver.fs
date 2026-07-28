module Fuaran.UI.ServerDriven.BoundedDriver

open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.Introspect
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.BindingResolver

// ============================================================================
//  The no-`'Msg` server-driven driver (Phase 153, Wave 20).
//
//  Phase 152's `Driver` runs a HAND-AUTHORED Elmish `(Model, update, view)`
//  loop on the server. This driver runs the *generated*-app case: there is no
//  hand-authored `update` / `'Msg` / `view` — only an **AI-emitted, wire-decoded
//  `Node<obj>` tree** and its **state store** (`BindingResolver.BindingSources`).
//  The loop:
//
//    inbound LiveEvent
//      → G1 validate (Validation.validate — node exists / event legit / payload
//        in-bounds / action policy-gated; reused verbatim from 152 Track C)
//      → interpret the resolved bounded `Action` against the store
//        (BoundedActions.runBoundedAction — SetState mutates, the rest are
//        closure-free effects / no-ops; NO closure is ever invoked)
//      → re-resolve the FIXED base tree's bindings against the new store
//        (`resolveTree` — see below)
//      → diff old-resolved → new-resolved (TreeOpDiff.diff, 152 Track A)
//      → lower to DomPatches (Lowering.lower) + ship the client effects.
//
//  ── Why re-resolve into the tree (the binding-blind-diff problem) ───────────
//  The Track-A diff compares `Node` trees via canonical JSON, and a
//  `Binding.State(key, default)` canonical-encodes IDENTICALLY regardless of the
//  store value (it encodes the key + default, not the resolved value). So
//  diffing the raw decoded tree before/after a `SetState` yields NO ops — the
//  state change is invisible. The fix: `resolveTree` substitutes every
//  resolvable binding with `Binding.Static (resolved value)` (and every bound
//  `TextSource` with its resolved `Literal`) BEFORE the diff, so a state change
//  shows up as a real `Binding.Static old → Binding.Static new` drift the diff
//  can see and patch. The hand-authored 152 path sidesteps this by baking state
//  into the tree in its `view`; the bounded path has no `view`, so it resolves.
//
//  ── Coverage floor (documented, same posture as 152 Track A) ────────────────
//  `resolveTree` covers the state-reactive Display / Input / Layout kinds AI
//  generators actually use for live content (Heading / Markdown / Badge / Metric
//  / Callout / Progress / LabelValueRow / Link / Sparkline text+bindings;
//  Button / Select; Tabs / Stepper / Disclosure / Card / SummaryList). Kinds
//  with reactive bindings NOT yet covered (Visualisation Grid/Chart/Map,
//  Form/Filters/FileUpload, per-tab `TabHeaders`) PASS THROUGH unresolved — a
//  state change inside them won't patch yet (the documented coarse floor; the
//  follow-on extends the per-kind coverage). Containers recurse generically via
//  `Introspect.getChildren` / `withChildren`, so structure is never lost.
//
//  ── G2 — resource bounds (the other half of "safe on shared infra") ─────────
//  The no-closures invariant (BoundedActions) prevents arbitrary *code*; it does
//  not prevent arbitrary *cost*. A generated tree can still drive an enormous
//  `Chain` or be pathologically large. `InteractionBudget` caps both per
//  interaction — the bounded-action cascade size (`MaxActions`) and the
//  re-resolve+diff tree size (`MaxNodes`, the memory/work proxy) — and a breach
//  surfaces a structured `BudgetExceeded` (NOT a hang, NOT a state mutation).
//  Bounded code (BoundedActions) + bounded cost (here) = safe to run untrusted
//  generated apps on shared / multi-tenant infrastructure (Phase 154).
//
//  Fable-clean + deterministic on purpose: the budget is step/size based, not
//  wall-clock (no `Stopwatch` / `Date.now`), so the same tree + event sequence
//  bounds identically server-driven and is unit-testable headlessly.
// ============================================================================

// ─── binding / text re-resolution ────────────────────────────────────────────

/// Substitute a binding with `Binding.Static (resolved value)` when it resolves;
/// leave it untouched otherwise (NotResolved / Errored — the renderer's
/// loading / error states still apply). Generic over the binding's `'T`.
let private substB (sources: BindingSources) (b: Binding<'T>) : Binding<'T> =
    match BindingResolver.resolve sources b with
    | Resolved v -> Binding.Static(Some v)
    | NotResolved
    | Errored _
    | I18nUnresolved _ -> b

let private substBOpt (sources: BindingSources) (bo: Binding<'T> option) : Binding<'T> option =
    bo |> Option.map (substB sources)

/// Resolve a bound `TextSource` to its `Literal`; pass `Literal` / `I18n`
/// through (stable under `SetState`).
let private resolveText (sources: BindingSources) (t: TextSource) : TextSource =
    match t with
    | TextSource.Bound b ->
        match BindingResolver.resolve sources b with
        | Resolved s -> TextSource.Literal s
        | NotResolved
        | Errored _
        | I18nUnresolved _ -> t
    | TextSource.Literal _
    | TextSource.I18n _ -> t

let private resolveTextOpt (sources: BindingSources) (t: TextSource option) : TextSource option =
    t |> Option.map (resolveText sources)

/// Resolve this node's OWN state-reactive leaf fields (text + bindings) against
/// the store. Children are untouched here (the generic recursion in
/// `resolveTree` handles them). Uncovered kinds pass through (the floor).
let private resolveOwnFields (sources: BindingSources) (node: Node<obj>) : Node<obj> =
    let kind =
        match node.Kind with
        // Phase 692 — one flat match with one catch-all, where this was three
        // nested per-category matches each with its own. Uncovered kinds still
        // pass through (the floor).
        | NodeKind.Heading s ->
            NodeKind.Heading
                { s with
                    Text = resolveText sources s.Text }
        | NodeKind.Markdown s ->
            NodeKind.Markdown
                { s with
                    Text = resolveText sources s.Text }
        | NodeKind.Badge s ->
            NodeKind.Badge
                { s with
                    Label = resolveText sources s.Label }
        | NodeKind.Metric s ->
            NodeKind.Metric
                { s with
                    Label = resolveText sources s.Label
                    Value = substB sources s.Value
                    Trend = substBOpt sources s.Trend
                    Subtext = resolveTextOpt sources s.Subtext }
        | NodeKind.Callout s ->
            NodeKind.Callout
                { s with
                    Heading = resolveTextOpt sources s.Heading
                    Body = resolveText sources s.Body }
        | NodeKind.Progress s ->
            NodeKind.Progress
                { s with
                    Fraction = substB sources s.Fraction
                    Label = resolveTextOpt sources s.Label
                    Caveat = resolveTextOpt sources s.Caveat }
        | NodeKind.LabelValueRow s ->
            NodeKind.LabelValueRow
                { s with
                    Label = resolveText sources s.Label
                    Value = substB sources s.Value
                    Help = resolveTextOpt sources s.Help }
        | NodeKind.Link s ->
            NodeKind.Link
                { s with
                    Href = substB sources s.Href
                    Label = resolveText sources s.Label }
        | NodeKind.Sparkline s ->
            NodeKind.Sparkline
                { s with
                    Source = substB sources s.Source }

        | NodeKind.Button s ->
            NodeKind.Button
                { s with
                    Label = resolveText sources s.Label
                    Tooltip = resolveTextOpt sources s.Tooltip
                    Disabled = substBOpt sources s.Disabled }
        | NodeKind.Select s ->
            NodeKind.Select
                { s with
                    Label = resolveText sources s.Label
                    Source = substB sources s.Source
                    Value = substB sources s.Value
                    Placeholder = resolveTextOpt sources s.Placeholder }

        | NodeKind.Tabs s ->
            NodeKind.Tabs
                { s with
                    ActiveIndex = substB sources s.ActiveIndex
                    ActiveTag = substBOpt sources s.ActiveTag }
        | NodeKind.Stepper s ->
            NodeKind.Stepper
                { s with
                    ActiveStep = substB sources s.ActiveStep }
        | NodeKind.Disclosure s ->
            NodeKind.Disclosure
                { s with
                    Open = substB sources s.Open
                    Heading = resolveText sources s.Heading }
        | NodeKind.Box s ->
            NodeKind.Box
                { s with
                    Heading = resolveTextOpt sources s.Heading }
        | NodeKind.SummaryList s ->
            NodeKind.SummaryList
                { s with
                    Heading = resolveTextOpt sources s.Heading }

        | other -> other

    { node with Kind = kind }

/// Re-resolve a whole tree's state-reactive bindings against the store,
/// producing a tree whose changed values the (binding-blind) Track-A diff can
/// see. Structure is preserved (no node added / removed / re-id'd); only leaf
/// binding / text values change.
let rec resolveTree (sources: BindingSources) (node: Node<obj>) : Node<obj> =
    let node = resolveOwnFields sources node

    match getChildren node.Kind with
    | Some kids ->
        let kids' = kids |> List.map (resolveTree sources)

        match withChildren node.Kind kids' with
        | Some k -> { node with Kind = k }
        | None -> node
    | None -> node

// ─── G2 — per-interaction resource budget ────────────────────────────────────

/// Per-interaction resource caps for server-executed generated apps. Bounded
/// language (BoundedActions) ⇒ no arbitrary code; bounded cost (this) ⇒ no
/// arbitrary cost. Both are required before running untrusted generated apps on
/// shared infrastructure.
type InteractionBudget =
    {
        /// Max leaf-action count of one bounded-action cascade (a `Chain`
        /// flattens; each non-Chain action costs 1). Caps a pathological deep /
        /// wide `Chain`.
        MaxActions: int
        /// Max node count of the driven tree — the per-interaction
        /// re-resolve + diff cost (and a proxy for the memory ceiling, since the
        /// resolved tree + op list scale with it).
        MaxNodes: int
    }

module InteractionBudget =
    /// No caps — the single-tenant / trusted-author case (the generated app is
    /// your own). `MaxActions` / `MaxNodes` at `Int32.MaxValue`.
    let unlimited: InteractionBudget =
        { MaxActions = System.Int32.MaxValue
          MaxNodes = System.Int32.MaxValue }

    /// Conservative defaults for the multi-tenant "platform runs your app" case.
    let defaults: InteractionBudget = { MaxActions = 64; MaxNodes = 10_000 }

let rec private actionCost (a: Action<obj>) : int =
    match a with
    | Action.Chain xs -> xs |> List.sumBy actionCost
    | _ -> 1

let rec private countNodes (node: Node<'a>) : int =
    let kids = getChildren node.Kind |> Option.defaultValue []
    1 + (kids |> List.sumBy countNodes)

// ─── the driver ──────────────────────────────────────────────────────────────

/// The host-coupled seams the bounded driver delegates to (the six-portability-
/// rules posture: this core takes no renderer / transport / sink dependency).
type BoundedServices =
    {
        /// G1 check (d): the dispatch policy gate (host maps `Action` → renderer
        /// `ActionDescriptor` → `IFuaranRuntime.CanDispatch`).
        CanDispatch: Action<obj> -> bool
        /// Render a node's HTML fragment (host wires `Renderer.Server`'s render).
        /// The nodes handed here are already resolved (`Binding.Static`), so the
        /// host renderer needs no live binding sources to produce correct HTML.
        RenderFragment: Node<obj> -> string
        /// FGP 5 — the ops applied this step, for the journal / telemetry sink.
        OnApply: TreeOp<obj> list -> unit
        /// G2 — per-interaction resource caps.
        Budget: InteractionBudget
    }

module BoundedServices =
    /// Permissive defaults — allow all dispatch, no op sink, default budget.
    /// `renderFragment` MUST be supplied (HTML production is host-owned).
    let create (renderFragment: Node<obj> -> string) : BoundedServices =
        { CanDispatch = fun _ -> true
          RenderFragment = renderFragment
          OnApply = ignore
          Budget = InteractionBudget.defaults }

/// One connection's bounded live state: the FIXED decoded tree (`BaseTree`), the
/// mutable store, the current resolved tree (the diff baseline), the cached node
/// count (for G2), and the injected services.
type BoundedSession =
    { BaseTree: Node<obj>
      Store: BoundedStore
      Resolved: Node<obj>
      NodeCount: int
      Services: BoundedServices }

/// Why the bounded driver produced no patches: a G1 gate rejection or a G2
/// budget breach. Either way the store is unchanged.
type BoundedReject =
    | Gate of Validation.RejectReason
    | BudgetExceeded of detail: string

/// The outcome of stepping a bounded session with one inbound event.
/// `Diagnostics` (Phase 212) carries the bounded interpreter's readable no-op
/// signals ("this action is inert on the generated-app path") — observability
/// for AI-emission debugging, never behaviour.
type BoundedStepOutput =
    { Patches: DomPatch list
      Effects: ClientEffect list
      Rejected: BoundedReject option
      Diagnostics: BoundedDiagnostic list }

/// Build a bounded session from a decoded `WireTree` + its initial store. The
/// bounded driver is the *correct* consumer of a wire tree: it never invokes
/// the tree's (inert) closures — interactivity is re-derived from the store —
/// so `decodeNode json |> Result.map (BoundedDriver.init services store)` is
/// the safe end-to-end path with no `reify`. The initial resolved tree (the
/// first diff baseline) is `resolveTree store tree`.
let init (services: BoundedServices) (store: BoundedStore) (wire: WireTree) : BoundedSession =
    let tree = WireTree.reify wire

    { BaseTree = tree
      Store = store
      Resolved = resolveTree store tree
      NodeCount = countNodes tree
      Services = services }

let private rejected (r: BoundedReject) : BoundedStepOutput =
    { Patches = []
      Effects = []
      Rejected = Some r
      Diagnostics = [] }

/// Step the bounded session with one untrusted inbound event. Validates (G1),
/// budgets (G2), interprets the bounded action against the store, re-resolves +
/// diffs + lowers — returning the updated session + patch / effect output. On a
/// G1 rejection or a G2 budget breach the session is returned UNCHANGED with
/// `Rejected = Some _` and no patches (default-deny by shape; no hang).
///
/// G1 validates against the CURRENT RESOLVED tree so a `Select` whose options
/// resolved to `Binding.Static` gets a precise bounds check; the resolved tree's
/// `Action` handlers are untouched by resolution, so action resolution is
/// identical to validating against the base tree.
let step (session: BoundedSession) (ev: LiveEvent) : BoundedSession * BoundedStepOutput =
    match Validation.validate session.Services.CanDispatch session.Resolved ev with
    | Error reason -> session, rejected (Gate reason)
    | Ok { Action = None } ->
        // Legitimate but no server-resolvable action — no state change.
        session,
        { Patches = []
          Effects = []
          Rejected = None
          Diagnostics = [] }
    | Ok { Action = Some action } ->
        let budget = session.Services.Budget
        let cost = actionCost action

        if cost > budget.MaxActions then
            session,
            rejected (BudgetExceeded(sprintf "action cascade cost %d exceeds MaxActions %d" cost budget.MaxActions))
        elif session.NodeCount > budget.MaxNodes then
            session,
            rejected (BudgetExceeded(sprintf "tree size %d exceeds MaxNodes %d" session.NodeCount budget.MaxNodes))
        else
            let outcome = BoundedActions.runBoundedAction ev.NodeId action session.Store
            let newResolved = resolveTree outcome.Store session.BaseTree
            let ops = TreeOpDiff.diff session.Resolved newResolved
            session.Services.OnApply ops
            let patches = Lowering.lower session.Services.RenderFragment newResolved ops

            { session with
                Store = outcome.Store
                Resolved = newResolved },
            { Patches = patches
              Effects = outcome.Effects
              Rejected = None
              Diagnostics = outcome.Diagnostics }
