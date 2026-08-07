module Fuaran.UI.ServerDriven.Driver

open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.ServerDriven.Validation

// ============================================================================
//  The per-connection driver (Phase 152, Track C).
//
//  Holds one connection's `(Model, update, view)` + the current rendered tree
//  and runs the *server-side* Elmish loop: an inbound `LiveEvent` is validated
//  (G1, `Validation.validate`), its resolved `Action` interpreted, `update` run,
//  the tree re-rendered, diffed (`TreeOpDiff.diff`), and lowered
//  (`Lowering.lower`) to the `DomPatch` list the shim applies — plus the
//  `ClientEffect` list for the inherently-browser arms.
//
//  ── The server-closure win ────────────────────────────────────────────────
//  Because the closures stay on the server, this loop natively executes the
//  *full* `Action` / `Binding` space — `Action.Dispatch` feeds `update`
//  directly, and the computational arms (`Call` / `SetState` / `Notify` /
//  `AiTool` / `CommitLocal`) run server-side via the injected
//  `InterpretHostEffect` (which may return a follow-up `'Msg` to fold, e.g.
//  `Call`'s `onResult`). No Fable compile of the closure, no bounded-action TS
//  interpreter — the driver just *invokes the closure*.
//
//  ── The server-executable / client-only split ─────────────────────────────
//  The *inherently browser* arms have no server form: `Navigate`,
//  `WriteToClipboard`, `ReadFileBody` lower to a `ClientEffect` (Track B) the
//  shim performs. `ReadFileBody`'s body round-trips back as a `file-read`
//  `LiveEvent` (the continuation is re-resolved on that inbound event — its
//  wiring is the host's, since the blob is browser-held).
//
//  ── Injection posture (the six portability rules) ─────────────────────────
//  Every host-coupled seam is injected via `DriverServices`, so this core takes
//  NO renderer / transport / op-stream-sink dependency: `RenderFragment` (HTML
//  is host-owned, wired to `Render.render`), `CanDispatch` (the G1 gate (d),
//  mapping to the renderer `ActionDescriptor`), `InterpretHostEffect` (the
//  computational arms), and `OnApply` (FGP 5 — applied ops to the journal /
//  telemetry sink; the host wires the `OpRecord` hash-chain).
//
//  A rejected event (G1 failure) mutates NO state — it returns the unchanged
//  session + `Rejected = Some reason` (the transport turns that into a
//  structured error frame + an audit event; default-deny by shape).
// ============================================================================

/// The host-coupled seams the driver delegates to. Injected so the core stays
/// renderer- / transport- / sink-free.
type DriverServices<'Msg> =
    {
        /// G1 check (d): the dispatch policy gate (host maps `Action` →
        /// renderer `ActionDescriptor` → `IFuaranRuntime.CanDispatch`).
        CanDispatch: Action<'Msg> -> bool
        /// Render a node's HTML fragment (host wires `Render.render`).
        RenderFragment: Node<'Msg> -> string
        /// Run a computational host-coupled arm (`Call` / `SetState` / `Notify`
        /// / `AiTool` / `CommitLocal`); may return a follow-up `'Msg` to fold.
        InterpretHostEffect: Action<'Msg> -> 'Msg option
        /// FGP 5 — the ops applied this step, for the journal / telemetry sink.
        OnApply: TreeOp<'Msg> list -> unit
        /// Phase 212 — the ALWAYS-ON G1 reject sink. `LiveConnection.Handle`
        /// emits every rejected step through it, independent of the
        /// interaction-telemetry opt-in, so a forged-node / dispatch-denied
        /// event leaves an audit trail by default. The host wires it to its
        /// logger (via `RejectReason.describe` for the log-safe text); the
        /// AspNetCore backend composes a default logging sink onto it at
        /// stream-open. Defaults to no-op in `create` (the transport-free core
        /// has nowhere to log).
        OnReject: RejectReason -> unit
    }

module DriverServices =
    /// Default services — **DENY all dispatch** (Phase 782), no host-effects, no
    /// op sink, no reject sink (the AspNetCore backend layers its default
    /// logging sink). `renderFragment` MUST be supplied (HTML production is
    /// host-owned).
    ///
    /// The gate default was `true` until Phase 782, which meant an unconfigured
    /// server-driven host validated every inbound `LiveEvent` against a policy
    /// that permitted everything. `createPermissive` is the named opt-in back to
    /// that; a real host supplies `{ create render with CanDispatch = … }`.
    let create (renderFragment: Node<'Msg> -> string) : DriverServices<'Msg> =
        { CanDispatch = fun _ -> false
          RenderFragment = renderFragment
          InterpretHostEffect = fun _ -> None
          OnApply = ignore
          OnReject = ignore }

    /// **The named opt-in back to the pre-0.14.0 allow-everything gate**
    /// (Phase 782). Identical to `create` except that `CanDispatch` permits
    /// every action.
    let createPermissive (renderFragment: Node<'Msg> -> string) : DriverServices<'Msg> =
        { create renderFragment with
            CanDispatch = fun _ -> true }

/// One connection's live state: the server-held model + the Elmish loop + the
/// current rendered tree (the diff baseline) + the injected services.
type LiveSession<'Model, 'Msg> =
    { Model: 'Model
      Tree: Node<'Msg>
      Update: 'Msg -> 'Model -> 'Model
      View: 'Model -> Node<'Msg>
      Services: DriverServices<'Msg> }

/// The outcome of stepping a session with one inbound event. `Rejected` is
/// `Some` exactly when G1 rejected the event (and then `Patches`/`Effects` are
/// empty and the session is unchanged).
type StepOutput =
    { Patches: DomPatch list
      Effects: ClientEffect list
      Rejected: RejectReason option }

/// Build a session from the Elmish triple + services. The initial tree is
/// `view model`.
let init
    (services: DriverServices<'Msg>)
    (update: 'Msg -> 'Model -> 'Model)
    (view: 'Model -> Node<'Msg>)
    (model: 'Model)
    : LiveSession<'Model, 'Msg> =
    { Model = model
      Tree = view model
      Update = update
      View = view
      Services = services }

/// Interpret a resolved `Action` into the messages to fold through `update` and
/// the client effects to ship. `nodeId` is the originating event's node (for the
/// node-addressed client effects, `Focus` / `ReadFileBody`).
let rec private interpret
    (nodeId: string)
    (services: DriverServices<'Msg>)
    (action: Action<'Msg>)
    : 'Msg list * ClientEffect list =
    match action with
    | Action.Dispatch m -> [ m ], []
    // Inherently-browser arms → ClientEffect (no server form).
    // Phase 782 — the route is sanitised BEFORE it is shipped to the shim. The
    // shim performs the navigation with whatever router the host wired, so an
    // unsafe scheme emitted here is a `javascript:`-URL sink on the client. A
    // refused route emits NO effect rather than a neutered one: `about:blank` is
    // a navigation the author did not ask for.
    | Action.Navigate route ->
        match Fuaran.UI.Renderer.Sanitize.sanitizeUrl route with
        | Some safe -> [], [ ClientEffect.Navigate safe ]
        | None -> [], []
    | Action.WriteToClipboard text -> [], [ ClientEffect.WriteToClipboard text ]
    | Action.ReadFileBody(_, _, encoding, _) ->
        let enc =
            match encoding with
            | FileReadEncoding.Text -> "Text"
            | FileReadEncoding.Base64 -> "Base64"
            | FileReadEncoding.DataUrl -> "DataUrl"

        [], [ ClientEffect.ReadFileBody(nodeId, enc) ]
    | Action.Chain actions ->
        // Concatenate the interpretations in order.
        actions
        |> List.fold
            (fun (ms, es) a ->
                let m2, e2 = interpret nodeId services a
                ms @ m2, es @ e2)
            ([], [])
    // Computational host-coupled arms → the injected host interpreter (which
    // may yield a follow-up 'Msg to fold).
    | Action.Call _
    | Action.Notify _
    | Action.SetState _
    | Action.AiTool _
    | Action.CommitLocal _
    // Invoke (Phase 283) — a host-coupled capability dispatch; routes through the injected host
    // interpreter, like AiTool / Call.
    | Action.Invoke _ -> (services.InterpretHostEffect action |> Option.toList), []

/// Apply a list of already-resolved, already-gated `Action`s to the session in
/// one step: interpret each (`nodeId` is the originating event's node, for the
/// node-addressed client effects), fold the produced messages through `update`,
/// re-render, diff old→new, emit the ops (FGP 5), and lower to patches. The
/// shared core of `step` (a single resolved action) and the form-flush path
/// (`FormBuffer` — N buffered field-commit actions plus the form's `OnSubmit`,
/// applied atomically so one submit is one diff/patch).
let applyResolvedActions
    (session: LiveSession<'Model, 'Msg>)
    (nodeId: string)
    (actions: Action<'Msg> list)
    : LiveSession<'Model, 'Msg> * StepOutput =
    let msgs, effects =
        actions
        |> List.fold
            (fun (ms, es) a ->
                let m2, e2 = interpret nodeId session.Services a
                ms @ m2, es @ e2)
            ([], [])

    let newModel = msgs |> List.fold (fun m msg -> session.Update msg m) session.Model
    let newTree = session.View newModel
    let ops = TreeOpDiff.diff session.Tree newTree
    session.Services.OnApply ops
    let patches = Lowering.lower session.Services.RenderFragment newTree ops

    { session with
        Model = newModel
        Tree = newTree },
    { Patches = patches
      Effects = effects
      Rejected = None }

/// Step the session with one untrusted inbound event. Validates (G1), runs the
/// resolved action through `update`, re-renders, diffs, lowers — returning the
/// updated session + the patch / effect output. On G1 rejection the session is
/// returned unchanged with `Rejected = Some reason` and no patches.
let step (session: LiveSession<'Model, 'Msg>) (ev: LiveEvent) : LiveSession<'Model, 'Msg> * StepOutput =
    match validate session.Services.CanDispatch session.Tree ev with
    | Error reason ->
        session,
        { Patches = []
          Effects = []
          Rejected = Some reason }
    | Ok { Action = None } ->
        // Legitimate but no server-resolvable action (e.g. a browser-held file
        // select, or a buffered form-field change under policy (b)) — no state
        // change, no patches.
        session,
        { Patches = []
          Effects = []
          Rejected = None }
    | Ok { Action = Some action } -> applyResolvedActions session ev.NodeId [ action ]
