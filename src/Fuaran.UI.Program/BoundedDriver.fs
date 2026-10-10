module Fuaran.UI.Program.BoundedDriver

open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.Introspect
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.BindingResolver
open Fuaran.Program.Bounded
open Fuaran.UI.Program

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

// ─── the confirm round trip (Phase 2106) ─────────────────────────────────────
//
//  `Action.Confirm` is two events on the bounded path. The GESTURE folds with
//  every confirm it reaches addressed (`UiWitness.address`), so each asks its
//  question with a token naming the confirm's path in the node's action. The
//  ANSWER is the originating event re-delivered with `confirmToken` (string) and
//  `confirmAccepted` (boolean); it passes the whole trust boundary again, and
//  then folds the addressed confirm as the core's `Choose` over the answer.
//
//  **Where the pending question lives.** In a host-reserved store slot, one key
//  per pending token, under `host.confirm.` — the store being the one thing
//  every placement already threads from step to step, the server placement's
//  core-owned session included. A tree cannot forge one: a tree-originated write
//  under the host prefix is refused on every path. And a tree cannot READ one
//  either: the slots are taken out of the store before a step folds or
//  re-resolves anything and put back after, so a pending question is the loop's
//  state carried in the store, never a value a binding resolves.
//
//  **The correlation rule.** An answer is admitted only when its token is
//  PENDING and still addresses a confirm in the node's current action; it then
//  consumes that token. Any other admitted event WITHDRAWS every pending
//  question — a question the reader moved on from is not one a late answer may
//  still act on. So a stale answer, a duplicate answer and a forged one are each
//  refused as an event (store unchanged, pending set untouched), and the
//  continuation of an admitted answer meets the dispatch gate ON ITS OWN, so a
//  confirm cannot smuggle an action the host would refuse.
//
//  A confirmation is a courtesy to the reader and never an authorisation: a
//  hostile surface answers yes without showing anyone a dialogue. Nothing here
//  claims otherwise.

/// The pending-question bookkeeping every bounded placement shares.
[<RequireQualifiedAccess>]
module ConfirmRoundTrip =

    /// The host-reserved prefix the pending tokens are kept under, one slot each.
    let PendingPrefix: string = Fuaran.UI.StateKeyPolicy.HostReservedPrefix + "confirm."

    /// The answer an event carries: both members, `confirmToken` a string and
    /// `confirmAccepted` a boolean. An event carrying anything less is not an
    /// answer, and is folded as the gesture it otherwise is.
    let answerOf (ev: LiveEvent) : (string * bool) option =
        match Map.tryFind ConfirmAnswer.TokenKey ev.Payload, Map.tryFind ConfirmAnswer.AcceptedKey ev.Payload with
        | Some(LiveValue.Str token), Some(LiveValue.Bool accepted) -> Some(token, accepted)
        | _ -> None

    /// Take the pending tokens out of a store: the tokens, and the store the
    /// step folds and re-resolves — the same store with no pending slot in it.
    let take (store: BoundedStore) : string list * BoundedStore =
        let pending, rest =
            store.State
            |> Map.partition (fun key _ -> key.StartsWith(PendingPrefix, System.StringComparison.Ordinal))

        (pending
         |> Map.toList
         |> List.map (fun (key, _) -> key.Substring PendingPrefix.Length)),
        { store with State = rest }

    /// Put the pending tokens back into the store a step left.
    let put (tokens: string list) (store: BoundedStore) : BoundedStore =
        { store with
            State =
                tokens
                |> List.fold
                    (fun state token ->
                        Map.add
                            (PendingPrefix + token)
                            (Fuaran.UI.Ops.Types.JValObj.toObj (Fuaran.Core.JBool true))
                            state)
                    store.State }

    /// The tokens a step ASKED, in the order it asked them.
    let asked (effects: ClientEffect list) : string list =
        effects
        |> List.choose (fun e ->
            match e with
            | ClientEffect.Confirm(_, token) -> Some token
            | _ -> None)

    /// What an admitted event folds, and which questions stay pending around
    /// that fold — or the event-level refusal an answer earns.
    ///
    /// A gesture folds its action addressed, and withdraws every pending
    /// question. An answer must name a pending token that still addresses a
    /// confirm in this node's action; its continuation is gated on its own;
    /// it folds as the core's `Choose`, and the other pending questions stand.
    let prepare
        (canDispatch: Action<obj> -> bool)
        (pending: string list)
        (ev: LiveEvent)
        (action: Action<obj>)
        : Result<Action<obj> * string list, Validation.RejectReason> =
        match answerOf ev with
        | None -> Ok(UiWitness.address action, [])
        | Some(token, accepted) ->
            if not (List.contains token pending) then
                Error(
                    Validation.RejectReason.PayloadOutOfBounds(
                        ev.NodeId,
                        "the confirm answer answers no pending question"
                    )
                )
            else
                match UiWitness.addressedConfirm ev.NodeId token action with
                | None ->
                    Error(
                        Validation.RejectReason.PayloadOutOfBounds(
                            ev.NodeId,
                            "the confirm answer addresses no Confirm in this node's action"
                        )
                    )
                | Some(path, confirm) ->
                    let branch =
                        match confirm with
                        | Action.Confirm(_, onConfirm, onCancel) -> if accepted then Some onConfirm else onCancel
                        | _ -> None

                    match branch with
                    | Some b when not (canDispatch b) ->
                        Error(Validation.RejectReason.DispatchDenied(ev.NodeId, Validation.describeAction b))
                    | _ -> Ok(UiWitness.answer path accepted confirm, pending |> List.filter (fun t -> t <> token))

    /// An admitted event whose node resolves to NO action: an answer there
    /// addresses nothing and is refused; anything else is a gesture that folds
    /// nothing, and — being an event that is not an answer — withdraws every
    /// pending question.
    let inert (ev: LiveEvent) (store: BoundedStore) : Result<BoundedStore, Validation.RejectReason> =
        match answerOf ev with
        | Some _ ->
            Error(
                Validation.RejectReason.PayloadOutOfBounds(
                    ev.NodeId,
                    "the confirm answer addresses no Confirm in this node's action"
                )
            )
        | None -> Ok(snd (take store))

// ─── the bounded flush (Phase 2198) ──────────────────────────────────────────
//
//  `Action.CommitLocal` is the explicit "Apply" of a buffered form field. On
//  this path it WRITES. The key is the field's `commitTo`, looked up in the
//  fixed base tree. The value is the event's flush payload member named by the
//  field's id, the protocol the server-driven flush speaks. Both are resolved
//  after the trust boundary admits the event and after the confirm round trip
//  decides what the event folds (`UiWitness.flushCommits`). The core then folds
//  each commit as the `Assign` it is.
//
//  **Each flushed write meets the dispatch gate on its own, as the `SetState` it
//  is.** A commit is a state write whose key the tree declares. A host whose
//  policy admits the commit but not a write to that key would otherwise be
//  bypassed by `commitTo`, and the server-driven flush gates the same write
//  the same way. A denied write refuses the EVENT, as a denied confirm
//  continuation does: nothing folds, and the store is unchanged.

/// The flush every bounded placement shares.
[<RequireQualifiedAccess>]
module CommitFlush =

    /// The action an admitted event folds with its commits flushed, or the
    /// event-level refusal a gated write earns.
    let prepare
        (canDispatch: Action<obj> -> bool)
        (tree: Node<obj>)
        (ev: LiveEvent)
        (action: Action<obj>)
        : Result<Action<obj>, Validation.RejectReason> =
        let flushed, writes = UiWitness.flushCommits tree ev.Payload action

        match writes |> List.tryFind (canDispatch >> not) with
        | Some denied -> Error(Validation.RejectReason.DispatchDenied(ev.NodeId, Validation.describeAction denied))
        | None -> Ok flushed

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
        match ConfirmRoundTrip.inert ev session.Store with
        | Error reason -> session, rejected (Gate reason)
        | Ok store ->
            { session with Store = store },
            { Patches = []
              Effects = []
              Rejected = None
              Diagnostics = [] }
    | Ok { Action = Some resolvedAction } ->
        // Phase 2106 — the pending questions are taken out of the store before
        // anything folds or resolves, and the event is either a gesture (folded
        // addressed) or an answer (folded as the core's `Choose`, or refused).
        let pending, store = ConfirmRoundTrip.take session.Store

        // Phase 2198 — then the commits the event folds are flushed, each write
        // gated on its own.
        let prepared =
            ConfirmRoundTrip.prepare session.Services.CanDispatch pending ev resolvedAction
            |> Result.bind (fun (action, standing) ->
                CommitFlush.prepare session.Services.CanDispatch session.BaseTree ev action
                |> Result.map (fun flushed -> flushed, standing))

        match prepared with
        | Error reason -> session, rejected (Gate reason)
        | Ok(action, standing) ->
            let budget = session.Services.Budget
            let cost = Budget.actionCascadeCost action

            if cost > budget.MaxActions then
                session,
                rejected (BudgetExceeded(sprintf "action cascade cost %d exceeds MaxActions %d" cost budget.MaxActions))
            elif session.NodeCount > budget.MaxNodes then
                session,
                rejected (BudgetExceeded(sprintf "tree cost %d exceeds MaxNodes %d" session.NodeCount budget.MaxNodes))
            else
                let outcome = BoundedActions.runBoundedAction ev.NodeId action store
                let newResolved = Resolve.resolveTree outcome.Store session.BaseTree
                let ops = TreeOpDiff.diff session.Resolved newResolved
                session.Services.OnApply ops
                let patches = Lowering.lower session.Services.RenderFragment newResolved ops

                { session with
                    Store = ConfirmRoundTrip.put (standing @ ConfirmRoundTrip.asked outcome.Effects) outcome.Store
                    Resolved = newResolved },
                { Patches = patches
                  Effects = outcome.Effects
                  Rejected = None
                  Diagnostics = outcome.Diagnostics }
