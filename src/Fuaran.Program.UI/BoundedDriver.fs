module Fuaran.Program.UI.BoundedDriver

open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.Introspect
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.BindingResolver
open Fuaran.Program.Bounded
open Fuaran.Program.UI

// ============================================================================
//  The no-`'Msg` server placement of the bounded program loop.
//
//  The UI tier's hand-authored driver runs a HAND-AUTHORED Elmish
//  `(Model, update, view)` loop on the server. This driver runs the *generated*-
//  app case: there is no hand-authored `update` / `'Msg` / `view` — only an
//  **emitted, wire-decoded `Node<obj>` tree** and its **state store**
//  (`BindingResolver.BindingSources`). The loop:
//
//    inbound LiveEvent
//      → G1 validate (`Validation.validate` — node exists / event legit /
//        payload in-bounds / action policy-gated)
//      → interpret the resolved bounded `Action` against the store
//        (`BoundedActions.runBoundedAction` — SetState mutates, the rest are
//        closure-free effects / no-ops; NO closure is ever invoked)
//      → re-resolve the FIXED base tree's bindings against the new store
//        (`Resolve.resolveTree`)
//      → diff old-resolved → new-resolved (`TreeOpDiff.diff`)
//      → lower to DomPatches (`Lowering.lower`) + ship the client effects.
//
//  The interpreter and the re-resolution pass are placement-neutral (they sit
//  beside this file); only the transport half — validate, diff, lower, budget —
//  is this placement's. The browser placement of the same algebra is
//  `Fuaran.Program.Runtime`.
//
//  ── G2 — resource bounds (the other half of "safe on shared infra") ─────────
//  The no-closures invariant (BoundedActions) prevents arbitrary *code*; it does
//  not prevent arbitrary *cost*. A generated tree can still drive an enormous
//  `Chain` or be pathologically large. `InteractionBudget` caps both per
//  interaction — the bounded-action cascade size (`MaxActions`) and the
//  re-resolve+diff tree size (`MaxNodes`, the memory/work proxy) — and a breach
//  surfaces a structured `BudgetExceeded` (NOT a hang, NOT a state mutation).
//  Bounded code (BoundedActions) + bounded cost (here) = safe to run untrusted
//  generated apps on shared / multi-tenant infrastructure.
//
//  Fable-clean + deterministic on purpose: the budget is step/size based, not
//  wall-clock (no `Stopwatch` / `Date.now`), so the same tree + event sequence
//  bounds identically at every placement and is unit-testable headlessly.
// ============================================================================

// ─── the driver ──────────────────────────────────────────────────────────────

/// The host-coupled seams the bounded driver delegates to (the portability
/// posture: this core takes no renderer / transport / sink dependency).
type BoundedServices =
    {
        /// G1 check (d): the dispatch policy gate (host maps `Action` → renderer
        /// `ActionDescriptor` → `IFuaranRuntime.CanDispatch`).
        CanDispatch: Action<obj> -> bool
        /// Render a node's HTML fragment (host wires its server renderer).
        /// The nodes handed here are already resolved (`Binding.Static`), so the
        /// host renderer needs no live binding sources to produce correct HTML.
        RenderFragment: Node<obj> -> string
        /// The ops applied this step, for the journal / telemetry sink.
        OnApply: TreeOp<obj> list -> unit
        /// G2 — per-interaction resource caps.
        Budget: InteractionBudget
    }

module BoundedServices =
    /// Default services — **DENY all dispatch**, no op sink, default budget.
    /// `renderFragment` MUST be supplied (HTML production is host-owned).
    ///
    /// This driver exists specifically to run emitted, wire-decoded trees, so an
    /// allow-everything gate default is the least defensible option: the whole
    /// point of the bounded path is that the tree is untrusted.
    /// `createPermissive` is the named opt-in back to it.
    let create (renderFragment: Node<obj> -> string) : BoundedServices =
        { CanDispatch = fun _ -> false
          RenderFragment = renderFragment
          OnApply = ignore
          Budget = InteractionBudget.defaults }

    /// **The named opt-in back to an allow-everything gate.**
    let createPermissive (renderFragment: Node<obj> -> string) : BoundedServices =
        { create renderFragment with
            CanDispatch = fun _ -> true }

/// One connection's bounded live state: the FIXED decoded tree (`BaseTree`), the
/// mutable store, the current resolved tree (the diff baseline), the cached node
/// count (for G2), and the injected services.
type BoundedSession =
    {
        BaseTree: Node<obj>
        Store: BoundedStore
        Resolved: Node<obj>
        /// The tree's cached render COST — the node count with data-bearing
        /// nodes weighted by their payload. Field name kept for source
        /// compatibility; `MaxNodes` is what it is compared against.
        NodeCount: int
        Services: BoundedServices
    }

/// Why the bounded driver produced no patches: a G1 gate rejection or a G2
/// budget breach. Either way the store is unchanged.
type BoundedReject =
    | Gate of Validation.RejectReason
    | BudgetExceeded of detail: string

/// The outcome of stepping a bounded session with one inbound event.
/// `Diagnostics` carries the bounded interpreter's readable no-op signals
/// ("this action is inert on the generated-app path") — observability for
/// emission debugging, never behaviour.
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
/// first diff baseline) is `Resolve.resolveTree store tree`.
///
/// **Budget ORDERING.** The cost is priced FIRST, with the walk stopping the
/// moment it passes `MaxNodes`, and `resolveTree` runs only if the tree is
/// within budget. The naive ordering is the opposite: walk the whole tree to
/// price it, then resolve the whole tree, and compare against `MaxNodes` only at
/// the first `step` — so an over-budget tree is fully walked twice by the very
/// construction supposed to refuse it, and only then declared too expensive. An
/// over-budget session is still RETURNED rather than refused (the signature is
/// total and consumers depend on that), but it is returned unresolved, and
/// `step` rejects it with `BudgetExceeded` on the first event — so the
/// observable contract is unchanged and only the work is.
let init (services: BoundedServices) (store: BoundedStore) (wire: WireTree) : BoundedSession =
    let tree = WireTree.reify wire
    let budget = services.Budget.MaxNodes
    let cost = Budget.treeCost budget tree

    { BaseTree = tree
      Store = store
      Resolved =
        (if cost > budget then
             tree
         else
             Resolve.resolveTree store tree)
      NodeCount = cost
      Services = services }

/// Build a bounded session ONLY if this host can cover everything the tree is
/// able to ask for — the pre-execution counterpart of the dispatch-time
/// refusals, asked once of the whole tree instead of per event on whichever
/// paths a session happens to take.
///
/// **Opt-in, and `init` remains the default.** The default posture is the one
/// this loop has always had: construction succeeds, and an uncoverable demand
/// is refused at the moment it is made, with the refusal recorded. That default
/// is not timidity — a session whose tree names one effect the host declines is
/// still a session that works for everything else, and refusing it wholesale
/// would be a stricter policy than the interpreter's own. A host that would
/// rather not start at all reaches for this.
///
/// `Error` carries EVERY finding, not the first: a host correcting its
/// registration wants the whole list, and stopping at the first would make that
/// an iterative guessing game.
let initStrict
    (coverage: HostCoverage)
    (services: BoundedServices)
    (store: BoundedStore)
    (wire: WireTree)
    : Result<BoundedSession, CoverageFinding list> =
    match Demanded.check coverage (WireTree.reify wire) with
    | [] -> Ok(init services store wire)
    | findings -> Error findings

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
/// `Rejected` is the EVENT-level refusal and nothing else (§10.5) — the trust
/// boundary or a budget declining the event itself. An arm that declines inside
/// an admitted event (a reserved state key, an unsafe destination) leaves it
/// `None` and shows as an ABSENT effect, which is what keeps "this surface was
/// rejected" and "this surface was inert" two different reports.
///
/// G1 validates against the CURRENT RESOLVED tree so a `Select` whose options
/// resolved to `Binding.Static` gets a precise bounds check; the resolved tree's
/// `Action` handlers are untouched by resolution, so action resolution is
/// identical to validating against the base tree.
let step (session: BoundedSession) (ev: LiveEvent) : BoundedSession * BoundedStepOutput =
    match Validation.validate session.Services.CanDispatch session.Resolved ev with
    | Error reason -> session, rejected (Gate reason)
    | Ok { Action = None } ->
        // Legitimate but no resolvable action — no state change, and per §10.5
        // deliberately NOT a refusal: the event was admitted and the fold found
        // nothing to run, which is a different fact from a rejected surface. It
        // is the ordinary outcome for a control whose behaviour rides a closure
        // slot, since the wire carries no closure and a decoder must not invent
        // one — inert at every host, whatever mechanism each uses to model the
        // erasure.
        session,
        { Patches = []
          Effects = []
          Rejected = None
          Diagnostics = [] }
    | Ok { Action = Some action } ->
        let budget = session.Services.Budget
        let cost = Budget.actionCascadeCost action

        if cost > budget.MaxActions then
            session,
            rejected (BudgetExceeded(sprintf "action cascade cost %d exceeds MaxActions %d" cost budget.MaxActions))
        elif session.NodeCount > budget.MaxNodes then
            session,
            rejected (BudgetExceeded(sprintf "tree cost %d exceeds MaxNodes %d" session.NodeCount budget.MaxNodes))
        else
            let outcome = BoundedActions.runBoundedAction ev.NodeId action session.Store
            let newResolved = Resolve.resolveTree outcome.Store session.BaseTree
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
