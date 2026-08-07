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
        /// Max render COST of the driven tree — the per-interaction
        /// re-resolve + diff cost (and a proxy for the memory ceiling, since the
        /// resolved tree + op list scale with it).
        ///
        /// Cost is the node count with data-bearing nodes weighted by the data
        /// they carry (Phase 790): a `Chart` costs one per (point × series), a
        /// `DataGrid` one per (row × column). For a tree with no data-bearing
        /// node this is exactly the node count, which is what it was before.
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

// ─── Per-kind render cost (Phase 790) ────────────────────────────────────────
//
// Most nodes cost 1: one node's worth of re-resolve, diff and render. A
// DATA-BEARING node is different — its render cost scales with data it carries
// INSIDE one node, which a node count cannot see. A `Chart` is a single node
// whose lowering emits geometry per (point × series); a `DataGrid` is a single
// node whose render emits a cell per (row × column). Counting those as 1 is what
// let a single node carry unbounded work behind a bounded-looking tree, and
// weighting them is what stops the shape reappearing on the next data-bearing
// kind: a new kind that carries its own data joins this function, and the
// existing `MaxNodes` budget then sees it with no host-side change.
//
// Only a `Binding.Static` payload is counted. A `Query` / `State` / `Transform`
// binding resolves at render time from the host's own store, so its size is not
// a property of the untrusted tree and is not this budget's business.

/// Ceiling on rows counted for cost. Reading a possibly-lazy payload to
/// exhaustion just to price it would itself be the unbounded work; a count this
/// far past any sane budget is already "refuse", so the exact figure past it
/// carries no decision.
[<Literal>]
let private maxCountedRows = 100_000

/// Saturating `int` arithmetic — a cost is a budget comparand, and an overflow
/// that wrapped NEGATIVE would read as "cheap" and admit the very tree the
/// budget exists to refuse.
let private satAdd (a: int) (b: int) : int =
    let sum = int64 a + int64 b

    if sum > int64 System.Int32.MaxValue then
        System.Int32.MaxValue
    else
        int sum

let private satMul (a: int) (b: int) : int =
    let product = int64 a * int64 b

    if product > int64 System.Int32.MaxValue then
        System.Int32.MaxValue
    else
        int product

let private staticSeqCount (binding: Binding<'t seq>) : int =
    match binding with
    | Binding.Static(Some items) -> items |> Seq.truncate maxCountedRows |> Seq.length
    | _ -> 0

let private staticListCount (binding: Binding<'t list>) : int =
    match binding with
    | Binding.Static(Some items) -> min maxCountedRows (List.length items)
    | _ -> 0

/// The render cost of ONE node, excluding its children.
let private nodeCost (node: Node<'a>) : int =
    match node.Kind with
    | NodeKind.Chart spec -> satAdd 1 (satMul (staticSeqCount spec.Source) (max 1 (List.length spec.YFields)))
    | NodeKind.DataGrid spec -> satAdd 1 (satMul (staticSeqCount spec.Source) (max 1 (List.length spec.Columns)))
    | NodeKind.Map spec -> satAdd 1 (staticListCount spec.Source)
    | NodeKind.Sparkline spec -> satAdd 1 (staticListCount spec.Source)
    | _ -> 1

/// The tree's total render cost — the node count, with data-bearing nodes
/// weighted by the data they carry. Named `countNodes` no longer: a cost is what
/// `MaxNodes` has always been comparing, and for every non-data-bearing tree
/// this is exactly the node count it was before.
///
/// ITERATIVE, with an explicit stack, and it STOPS as soon as the cost passes
/// `ceiling` (Phase 781). Both properties matter and neither was true before:
///
///  - Recursive, it was bounded by the caller's stack rather than by the budget,
///    so the function whose whole job is to bound a tree's cost was itself the
///    thing an over-deep tree could kill.
///  - Unconditional, it walked the ENTIRE tree before anyone compared the result
///    against `MaxNodes` — so the budget was charged after the work it exists to
///    refuse had already been done. The ceiling makes the check happen DURING
///    the walk: a tree ten thousand times over budget costs the same as one
///    marginally over.
///
/// The returned value is exact when it is at or below `ceiling`, and is
/// "greater than `ceiling`" otherwise — which is all a budget comparand needs,
/// since every use of it past that point is a refusal.
let private treeCostTo (ceiling: int) (node: Node<'a>) : int =
    let pending = System.Collections.Generic.Stack<Node<'a>>()
    pending.Push node
    let mutable total = 0

    while pending.Count > 0 && total <= ceiling do
        let current = pending.Pop()
        total <- satAdd total (nodeCost current)

        match getChildren current.Kind with
        | Some kids ->
            for kid in kids do
                pending.Push kid
        | None -> ()

    total

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
    {
        BaseTree: Node<obj>
        Store: BoundedStore
        Resolved: Node<obj>
        /// The tree's cached render COST (Phase 790) — the node count with
        /// data-bearing nodes weighted by their payload. Field name kept for
        /// source compatibility; `MaxNodes` is what it is compared against.
        NodeCount: int
        Services: BoundedServices
    }

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
///
/// **Budget ORDERING (Phase 781).** The cost is priced FIRST, with the walk
/// stopping the moment it passes `MaxNodes`, and `resolveTree` runs only if the
/// tree is within budget. Previously `init` did the opposite: it walked the
/// whole tree to price it and then resolved the whole tree, and nothing compared
/// the result against `MaxNodes` until the first `step` — so an over-budget tree
/// was fully walked twice by the very construction that was supposed to refuse
/// it, and only then declared too expensive. An over-budget session is still
/// RETURNED rather than refused (the signature is total and consumers depend on
/// that), but it is returned unresolved, and `step` rejects it with
/// `BudgetExceeded` on the first event exactly as before — so the observable
/// contract is unchanged and only the work is.
let init (services: BoundedServices) (store: BoundedStore) (wire: WireTree) : BoundedSession =
    let tree = WireTree.reify wire
    let budget = services.Budget.MaxNodes
    let cost = treeCostTo budget tree

    { BaseTree = tree
      Store = store
      Resolved = (if cost > budget then tree else resolveTree store tree)
      NodeCount = cost
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
            rejected (BudgetExceeded(sprintf "tree cost %d exceeds MaxNodes %d" session.NodeCount budget.MaxNodes))
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
