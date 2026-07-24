module Fuaran.UI.ServerDriven.Navigation

open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.ServerDriven.Driver

// ============================================================================
//  Server-driven navigation + routing — full-SSR routes ‖ in-place tree swap
//  (Phase 157).
//
//  The content-site + wizard app classes need NAVIGATION, and the server-driven
//  tier had no model for it. `Action.Navigate` + the crawlable `Display.Link`
//  (`<a href>`, Phase 139) exist, but it was undefined whether navigating does a
//  FULL SSR page load (a real URL → a fresh server render — the SEO-correct path
//  for content routes) or an IN-PLACE server-driven tree swap (no reload — the
//  app-like path for wizard steps / dashboard views). This module defines both
//  modes, the selector between them, and the URL/history sync.
//
//  ── The selector: the route-to-tree seam IS the mode switch ─────────────────
//  `RouteResolver` is the host's route table: `route -> Node tree option`. If it
//  resolves the route to a tree, the route is server-driven-renderable in-session
//  → IN-PLACE swap (diff old→new tree, patch, `history.pushState`). If it returns
//  `None`, the route is not an in-place route → FULL RELOAD (`ClientEffect.Navigate`
//  — the browser loads the URL and the server SSRs it fresh). Routing stays
//  host-owned: the language tier ships the mechanism, never an app's route table.
//
//  ── URL + history ──────────────────────────────────────────────────────────
//  An in-place nav emits `ClientEffect.PushState route` so the address bar +
//  back/forward stay correct without a reload. The shim listens for `popstate`
//  and round-trips a `popstate` `LiveEvent` carrying the popped `route`; the
//  driver swaps the tree to match (and does NOT push state again — the browser
//  already moved). Real URLs work in both modes (shareable, bookmarkable).
//
//  ── Deep-link + reload correctness ─────────────────────────────────────────
//  A direct hit on an in-place route URL is a normal HTTP request the host SSRs
//  with `Renderer.Server` (full first paint) — then the session drives in-place
//  from there. `firstPaintTree` is the seam the host renders that first paint
//  through, so there are no "only reachable by clicking from home" dead routes.
//
//  Pure + Fable-clean: `resolveNav` is a function of `(resolver, currentTree,
//  route)` → outcome; `stepWithRouting` composes over the shipped `Driver.step`.
// ============================================================================

/// The host's route table: resolve a route to the tree that route renders, or
/// `None` when the route is not server-driven-renderable in-session (→ full SSR
/// navigation). Host-owned — the language tier never ships a route table.
type RouteResolver<'Msg> = string -> Node<'Msg> option

/// The outcome of resolving a navigation.
type NavOutcome<'Msg> =
    /// The route is an in-place route: swap to `NewTree`, applying `Ops` /
    /// `Patches`; `PushState` carries the route to sync the URL (omitted on a
    /// `popstate`, where the browser already moved).
    | InPlace of newTree: Node<'Msg> * ops: TreeOp<'Msg> list * patches: DomPatch list * effects: ClientEffect list
    /// The route is not in-place: a full SSR page load (the browser navigates).
    | FullReload of route: string

let private routeOf (ev: LiveEvent) : string =
    match Map.tryFind "route" ev.Payload with
    | Some(LiveValue.Str s) -> s
    | _ -> "/"

/// Resolve a navigation to `route` against the host route table. `pushState`
/// distinguishes a user-initiated nav (push the new URL) from a `popstate`
/// (the URL already changed — don't push again). Pure: computes the in-place
/// swap's ops + patches, or signals a full reload.
let resolveNav
    (renderFragment: Node<'Msg> -> string)
    (resolver: RouteResolver<'Msg>)
    (currentTree: Node<'Msg>)
    (pushState: bool)
    (route: string)
    : NavOutcome<'Msg> =
    match resolver route with
    | Some routeTree ->
        let ops = TreeOpDiff.diff currentTree routeTree
        let patches = Lowering.lower renderFragment routeTree ops
        let effects = if pushState then [ ClientEffect.PushState route ] else []
        InPlace(routeTree, ops, patches, effects)
    | None -> FullReload route

/// The first-paint tree for a deep-link / reload of `route` — the host renders
/// this through `Renderer.Server` for full SSR, then the session drives in-place
/// from it. `None` when the route is unknown (the host serves its 404).
let firstPaintTree (resolver: RouteResolver<'Msg>) (route: string) : Node<'Msg> option = resolver route

/// Step a session with navigation routing layered over `fallback` (the plain or
/// form-validating step). Handles two routing events:
///   • a `popstate` event → swap the tree to the popped route in place, no
///     push-state (the browser already moved);
///   • a legitimate `Action.Navigate route` → in-place swap + `PushState` when
///     the resolver knows the route, else a full reload (`ClientEffect.Navigate`).
/// Every other event delegates to `fallback`.
///
/// (A non-routing event is validated twice — once here to classify it, once in
/// `fallback` — which is cheap + pure; the alternative is threading the
/// validated event through, which leaks the gate across the seam.)
let stepWithRouting
    (resolver: RouteResolver<'Msg>)
    (fallback: LiveSession<'Model, 'Msg> -> LiveEvent -> LiveSession<'Model, 'Msg> * StepOutput)
    (session: LiveSession<'Model, 'Msg>)
    (ev: LiveEvent)
    : LiveSession<'Model, 'Msg> * StepOutput =
    let applyOutcome (pushState: bool) (route: string) =
        match resolveNav session.Services.RenderFragment resolver session.Tree pushState route with
        | InPlace(newTree, ops, patches, effects) ->
            session.Services.OnApply ops

            { session with Tree = newTree },
            { Patches = patches
              Effects = effects
              Rejected = None }
        | FullReload r ->
            session,
            { Patches = []
              Effects = [ ClientEffect.Navigate r ]
              Rejected = None }

    if ev.Event = "popstate" then
        // A back/forward — not node-addressed; handled outside G1 (it carries a
        // route, not a node action). No push-state on the way back.
        applyOutcome false (routeOf ev)
    else
        match validate session.Services.CanDispatch session.Tree ev with
        | Ok { Action = Some(Action.Navigate route) } -> applyOutcome true route
        | _ -> fallback session ev
