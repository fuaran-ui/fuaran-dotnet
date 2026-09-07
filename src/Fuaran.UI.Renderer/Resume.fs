module Fuaran.UI.Renderer.Resume

// ============================================================================
//  Fuaran — zero-hydration resumability: client interpreter runtime.
//
//  The client counterpart to `Fuaran.UI.Renderer.Server.Resume`. The server
//  ships inert HTML + a flat `nodeId → { action, disposition }` envelope and
//  **zero framework JS executes at load**. This interpreter is the small client
//  runtime (NOT the app) that resumes the Elmish loop on first interaction:
//
//    load:      HTML + <script resume-envelope> + 1 document-root listener   ← 0 framework JS executed
//    1st event: walk to nearest node id → look up its Action → run it
//    later:     the touched subtree is live; the rest stays inert HTML
//
//  Three dispositions (decided server-side, §3 of `docs/RESUMABILITY-EXPLORATION.md`):
//   - `interpret` — data-shaped `Action` (`Navigate` / `Notify` / `SetState` /
//     `WriteToClipboard` / `AiTool` / `CommitLocal` / `Print`) is executed
//     directly against `IFuaranRuntime` with no view — the ≈ 0-JS happy path.
//   - `boot` — a `Dispatch msg` whose msg is opaque on the wire; hand the node
//     to the host's lazy module-boot (`BootSubtree`), which loads the chunk,
//     runs `update`, and renders the touched subtree via Fable/React.
//   - `fallback` — a `Call` / `ReadFileBody` continuation that can't serialise;
//     hand the subtree to the host's hydration path (`HydrateSubtree`) — §5,
//     that subtree only, never a broken page.
//
//  Resume-mismatch (§6): before interpreting, the tree-hash on the envelope is
//  compared to the `data-fuaran-resume-hash` marker the server stamped; a
//  disagreement (the host mutated the DOM after render) calls `OnMismatch`
//  (host client-render fallback) rather than running a stale map.
//
//  ── The parity claim, and how it is made true (Phase 1523) ────────────────
//
//  This header used to claim that "because it routes through the *same* runtime
//  the hydrated `runAction` uses, op-stream + telemetry emission is identical
//  (FGP 5 — resumability is a load strategy, not a new dispatch path)". Sharing
//  a RUNTIME is not sharing a DISPATCH PATH, and the difference was the whole of
//  it: every gate the hydrated path runs lives in `Render`, ABOVE the runtime,
//  and this interpreter reached past all of them.
//
//  Concretely, a resumed gesture ran with no `CanDispatch` consultation (Phase
//  782's default-deny), no `Sanitize.checkDestination` (Phase 1026's ambient
//  destination policy), no host-reserved `host.*` state-key refusal, and no
//  `ActionInvocation` record (Phase 889). So the open-redirect and
//  `javascript:`-route sinks that 782 closed on the hydrated path were open
//  here; a resumed `SetState` could write a host-reserved slot the hydrated one
//  refuses; and a host auditing its action records saw nothing at all for an
//  entire resumed session. Resumability is a LOAD strategy — it must not be a
//  second security posture.
//
//  Two changes make the claim true, and they are one change seen from two
//  sides. `interpret` routes every arm through the very functions
//  `Render.runActionCore` routes through — `applyDispatchGateOutcome`,
//  `treeNavigateOutcome`, `treeStateWriteOutcome` — and records the gesture at
//  the same outer emission point with the same `Chain`-is-one-invocation
//  semantics. And the envelope is read by a TYPED, DEPTH-CAPPED reader rather
//  than trusted by shape: `action?route` on a raw parsed object asserts a type
//  the value need not have, and the parsed value comes from `<script>` content
//  in a document the host may have mutated.
// ============================================================================

open Fuaran.UI.Types
open Fuaran.UI.Ops.ActionInvocation

/// Host handoffs the language tier can't supply itself — the app owns its
/// chunk loader, its hydration mount, and its client-render fallback. The
/// interpreter wires the document-root listener + envelope interpretation; the
/// host fills these three seams.
type ResumeConfig =
    {
        /// The runtime data-shaped actions execute against (parity with `runAction`).
        Runtime: Runtime.IFuaranRuntime
        /// Lazy-boot the module owning this node (a `Dispatch` handler): load the
        /// update/view chunk, run `update`, hand the subtree to Fable/React.
        BootSubtree: string -> unit
        /// Hydrate this subtree only (a non-serialisable `Call` / `ReadFileBody`
        /// handler) — degrade to the Phase 143 path for that subtree.
        HydrateSubtree: string -> unit
        /// Full client render — the embedded tree-hash disagreed with the DOM.
        OnMismatch: unit -> unit
    }

/// The handle `installWithHandle` returns: calling `Teardown` removes every
/// listener that install attached, and is idempotent (Phase 1523).
///
/// It exists because install attaches listeners to the DOCUMENT, not to the
/// render root — one delegated listener per event type, which is what makes
/// resume O(1) rather than per-node. A document listener outlives every element
/// it was installed for, so a host that resumed a root, tore it down, and
/// resumed another was left with two live interpreters over one document: the
/// first still holding its own envelope's action map, still dispatching against
/// its own runtime, for node ids the second root may well reuse. Nothing in the
/// pre-1523 surface could stop that, because nothing was returned to stop it
/// with.
type ResumeHandle =
    {
        /// Remove the installed listeners. Safe to call more than once.
        Teardown: unit -> unit
    }

/// The ambient destination policy a resumed `Navigate` is judged against
/// (Phase 1523).
///
/// A resumed document has no `RenderContext`, so it has no `EgressPolicy` to
/// inherit — which is why this is an explicitly-installed module default rather
/// than an omission. It starts at `denyNonLocalEgress`, the same default a
/// decoded tree gets on the hydrated path: an emission cannot declare its own
/// egress, so absent a host's declaration it gets none.
///
/// A host that declared a policy for its hydrated renders installs the same one
/// here, once, before `install`. That seam is deliberate rather than a
/// convenience: the two paths render the same documents, so a policy applying
/// to one and not the other would make a reader's protection depend on whether
/// the page had hydrated yet.
let mutable private resumeEgressPolicy: Sanitize.EgressPolicy =
    Sanitize.denyNonLocalEgress

/// Install the destination policy resumed actions are judged against. Call it
/// with the same policy the host passes to its renderer.
let installEgressPolicy (policy: Sanitize.EgressPolicy) : unit = resumeEgressPolicy <- policy

/// The policy currently installed — readable so a host can assert its own
/// wiring, and so a test can pin the default.
let egressPolicy () : Sanitize.EgressPolicy = resumeEgressPolicy

/// The action-invocation sink resumed gestures are recorded to (Phase 889 /
/// 1523) — `None` by default, exactly as `RenderContext.ActionSink` is.
///
/// Module-level for the same reason the policy above is: there is no context to
/// carry it on. A host that wired a sink for its hydrated renders wires the same
/// one here, and a resumed session then produces the same records a hydrated one
/// does — which is what makes an audit of those records mean anything.
let mutable private resumeActionSink: IActionInvocationSink option = None

/// Install the sink resumed gestures are recorded to.
let installActionSink (sink: IActionInvocationSink option) : unit = resumeActionSink <- sink

/// The maximum nesting the envelope reader walks before refusing.
///
/// The envelope is JSON parsed out of a `<script>` element in a document the
/// host may have mutated, and the reader is recursive: `Chain` contains actions,
/// and a `Notify` / `SetState` / `AiTool` payload is an arbitrary JSON value.
/// `JSON.parse` is itself iterative and imposes no bound, so a deeply-nested
/// envelope reaches a recursive reader at a depth no type states. This is the
/// `WireLimits`-class answer: a bound, refused with a diagnostic rather than a
/// stack overflow.
///
/// It is deliberately far below `WireLimits.MaxDepth`. An ENVELOPE is a flat map
/// of small actions, not a tree, so a legitimate one is a handful of levels deep
/// even with a nested `Chain`; a limit sized for a document would admit every
/// hostile envelope this one refuses.
[<Literal>]
let envelopeMaxDepth = 32

#if FABLE_COMPILER
open Fable.Core
open Fable.Core.JsInterop

/// Is this raw envelope value a JS string? `TextSource.Literal`'s canonical form
/// IS the bare string, so this is how the resume path tells a literal clipboard
/// payload (which it can interpret) from a bound one (which it cannot).
[<Emit("typeof $0 === 'string'")>]
let private isJsString (v: obj) : bool = jsNative

/// Read a required string member. `None` when the member is absent or is not a
/// string — never `unbox`, which asserts a type the value need not have and
/// hands a non-string JS value to a runtime call as if it were one.
let private readString (o: obj) (key: string) : string option =
    if isNull (box o) then
        None
    else
        let v: obj = o?(key)
        if isJsString v then Some(unbox<string> v) else None

/// Read an arbitrary JSON payload member, bounded. `None` when the member is
/// absent or the budget is spent. Depth is charged against the same budget the
/// action reader spends, so a payload cannot be where an envelope hides its
/// nesting.
let private readPayload (o: obj) (key: string) (depth: int) : JVal option =
    if isNull (box o) || depth > envelopeMaxDepth then
        None
    else
        let v: obj = o?(key)

        if isNull v then
            None
        else
            Some(Runtime.JsonBridge.jsToJVal v)

/// Read one envelope `action` object into the typed `Action` the hydrated path
/// interprets, or `Error` with a reason.
///
/// Returning the SAME type the hydrated path dispatches is what makes the parity
/// claim checkable rather than asserted: from here on a resumed gesture and a
/// hydrated one are the same value flowing through the same functions, so there
/// is no second interpreter left to drift.
///
/// `'Msg` is instantiated at `unit`, and the closure-bearing cases are
/// unreachable by construction — a `Dispatch` / `Call`-with-continuation /
/// `ReadFileBody` node carries a `boot` or `fallback` disposition and is routed
/// before it reaches here. Arriving at one anyway is reported as an
/// envelope/interpreter disagreement rather than guessed at.
let rec private readAction (depth: int) (action: obj) : Result<Action<unit>, string> =
    if depth > envelopeMaxDepth then
        Error(sprintf "envelope nests deeper than the reader's limit of %d" envelopeMaxDepth)
    elif isNull (box action) then
        Error "envelope entry carries no action"
    else
        match readString action "$type" with
        | None -> Error "envelope action has no string `$type`"
        | Some "Navigate" ->
            // Phase 1536 — the route is a `TextSource`, whose canonical LITERAL
            // form is the bare JSON string this reader has always taken. A
            // non-string `route` is a bound or i18n route, and THIS PATH HOLDS
            // NO RESOLVER — the server's `disposition` routes such a node to
            // `fallback` for exactly that reason — so reaching here with one
            // means the envelope and the interpreter disagree. It is REFUSED
            // and reported, never coerced; the stake is higher than the
            // clipboard's beside it, because a coerced destination is a real
            // navigation the reader cannot undo, where a wrong clipboard write
            // is inert until they paste.
            //
            // `target` rides only when it is `"Blank"`. An unrecognised token
            // is refused rather than read as `Self`: a document that meant a
            // fresh context and misspelled it must not silently take over the
            // one the reader is in.
            match readString action "route" with
            | Some route ->
                match readString action "target" with
                | None -> Ok(Action.Navigate(TextSource.Literal route, NavigateTarget.Self))
                | Some "Self" -> Ok(Action.Navigate(TextSource.Literal route, NavigateTarget.Self))
                | Some "Blank" -> Ok(Action.Navigate(TextSource.Literal route, NavigateTarget.Blank))
                | Some other -> Error(sprintf "Navigate `target` is not Self | Blank: '%s'" other)
            | None ->
                Error "Navigate carries a bound route; this node should have been dispositioned 'fallback' and hydrated"
        | Some "Notify" ->
            match readString action "channel" with
            | Some channel ->
                let payload =
                    readPayload action "payload" (depth + 1) |> Option.defaultValue (JStr "")

                Ok(Action.Notify(channel, payload))
            | None -> Error "Notify has no string `channel`"
        | Some "SetState" ->
            match readString action "key" with
            | Some key -> Ok(Action.SetState(key, readPayload action "value" (depth + 1), None))
            | None -> Error "SetState has no string `key`"
        | Some "AiTool" ->
            match readString action "toolName" with
            | Some name ->
                let args = readPayload action "args" (depth + 1) |> Option.defaultValue (JStr "")
                Ok(Action.AiTool(name, args))
            | None -> Error "AiTool has no string `toolName`"
        | Some "WriteToClipboard" ->
            // Phase 1126 — the payload is a `TextSource`, whose canonical LITERAL
            // form is the bare JSON string this path has always read. A
            // non-string `text` is a bound or i18n payload, which the server's
            // `disposition` routes to `fallback` precisely because this path
            // holds no resolver — so reaching here with one means the envelope
            // and the interpreter disagree, and the honest response is to say so
            // rather than to copy `[object Object]` onto the reader's clipboard.
            match readString action "text" with
            | Some text -> Ok(Action.WriteToClipboard(TextSource.Literal text))
            | None ->
                Error
                    "WriteToClipboard carries a bound payload; this node should have been dispositioned 'fallback' and hydrated"
        | Some "Print" -> Ok Action.Print
        | Some "Focus" ->
            // Phase 1537 — a node id and nothing else, read exactly as
            // `CommitLocal`'s is.
            match readString action "nodeId" with
            | Some nodeId -> Ok(Action.Focus nodeId)
            | None -> Error "Focus has no string `nodeId`"
        | Some "Confirm" ->
            // Phase 1537 — the prompt is a `TextSource`, whose canonical LITERAL
            // form is the bare JSON string. A non-string `prompt` is a bound or
            // i18n question, and THIS PATH HOLDS NO RESOLVER — the server's
            // `disposition` routes such a node to `fallback` for exactly that
            // reason — so reaching here with one means the envelope and the
            // interpreter disagree. It is REFUSED and reported, never coerced:
            // showing the reader a declaration as the question and then acting
            // on their answer is worse than hydrating the subtree.
            //
            // Both continuations are read through this same reader, so a
            // continuation the resume path cannot construct (a `Dispatch`, a
            // `Call`) refuses the WHOLE confirm rather than yielding a dialogue
            // whose yes branch does nothing.
            match readString action "prompt" with
            | None ->
                Error "Confirm carries a bound prompt; this node should have been dispositioned 'fallback' and hydrated"
            | Some prompt ->
                let onConfirmRaw: obj = action?onConfirm

                if isNull onConfirmRaw then
                    Error "Confirm has no `onConfirm` action"
                else
                    match readAction (depth + 1) onConfirmRaw with
                    | Error e -> Error e
                    | Ok onConfirm ->
                        let onCancelRaw: obj = action?onCancel

                        if isNull onCancelRaw then
                            Ok(Action.Confirm(TextSource.Literal prompt, onConfirm, None))
                        else
                            readAction (depth + 1) onCancelRaw
                            |> Result.map (fun onCancel ->
                                Action.Confirm(TextSource.Literal prompt, onConfirm, Some onCancel))
        | Some "CommitLocal" ->
            match readString action "nodeId" with
            | Some nodeId -> Ok(Action.CommitLocal nodeId)
            | None -> Error "CommitLocal has no string `nodeId`"
        | Some "Chain" ->
            let ops: obj = action?ops

            if isNull ops || not (JS.Constructors.Array.isArray ops) then
                Error "Chain has no `ops` array"
            else
                let parsed = unbox<obj[]> ops |> Array.map (readAction (depth + 1)) |> Array.toList

                let firstError =
                    parsed
                    |> List.tryPick (fun r ->
                        match r with
                        | Error e -> Some e
                        | Ok _ -> None)

                match firstError with
                | Some e -> Error e
                | None ->
                    Ok(
                        Action.Chain(
                            parsed
                            |> List.choose (fun r ->
                                match r with
                                | Ok a -> Some a
                                | Error _ -> None)
                        )
                    )
        | Some other -> Error(sprintf "no interpreter for action $type '%s'" other)

/// Interpret ONE typed action against the runtime, through the gates.
///
/// This mirrors `Render.runActionCore` arm for arm and, wherever a gate exists,
/// calls the very same function rather than a copy of it — the only arrangement
/// under which "the resumed path refuses what the hydrated path refuses" is a
/// property rather than a hope. `denied` accumulates refusals so the outer
/// emission point classifies a `Chain` as ONE gesture, exactly as the hydrated
/// path does.
let rec private runResumed (runtime: Runtime.IFuaranRuntime) (denied: string list ref) (action: Action<unit>) : unit =
    let note (r: Result<unit, string>) : unit =
        match r with
        | Ok() -> ()
        | Error reason -> denied.Value <- reason :: denied.Value

    let gate (descriptor: Runtime.ActionDescriptor) (effect: unit -> unit) : unit =
        note (Render.applyDispatchGateOutcome runtime descriptor effect)

    match action with
    | Action.Chain actions ->
        for a in actions do
            runResumed runtime denied a
    // Phase 1536 — the LITERAL route the reader constructed, through the same
    // gate the hydrated path runs, and — for a `Blank` target — through the
    // same `Render.performNavigation`, so `noopener,noreferrer` is applied by
    // one function rather than by two that could drift. A bound route is
    // unconstructible from an envelope (see `readAction`) and so falls to the
    // catch-all below if one is ever synthesised.
    | Action.Navigate(TextSource.Literal route, target) ->
        note (Render.treeNavigateOutcome runtime resumeEgressPolicy route (Render.performNavigation runtime target))
    | Action.Notify(channel, payload) ->
        gate (Runtime.ActionDescriptor.Notify channel) (fun () -> runtime.Notify(channel, payload))
    | Action.SetState(key, value, _) ->
        gate (Runtime.ActionDescriptor.SetState key) (fun () ->
            note (
                Render.treeStateWriteOutcome runtime key (fun () ->
                    runtime.SetState(key, value |> Option.defaultValue (JStr "")))
            ))
    | Action.AiTool(name, args) ->
        gate (Runtime.ActionDescriptor.AiTool name) (fun () -> runtime.InvokeAiTool(name, args))
    | Action.WriteToClipboard(TextSource.Literal text) ->
        gate Runtime.ActionDescriptor.WriteToClipboard (fun () -> runtime.WriteToClipboard text)
    // Renderer-native, mirroring `Render.runActionCore`: no `IFuaranRuntime`
    // member backs it, because `window.print()` is the browser's own and takes
    // no arguments. Gated all the same — a resumed print and a hydrated print
    // are the same act, so they meet the same gate.
    | Action.Print -> gate Runtime.ActionDescriptor.Print (fun () -> Browser.Dom.window.print ())
    // Phase 1537 — the resumed confirm and the hydrated confirm are the same
    // act, so they meet the same two gates in the same order: this one asks
    // whether the tree may raise a dialogue, and the continuation re-enters
    // `runResumed` from the top, where it meets its own. A refusal of the
    // dialogue performs NEITHER branch — the reader was never asked.
    | Action.Confirm(TextSource.Literal prompt, onConfirm, onCancel) ->
        gate (Runtime.ActionDescriptor.Confirm prompt) (fun () ->
            if Browser.Dom.window.confirm prompt then
                runResumed runtime denied onConfirm
            else
                onCancel |> Option.iter (runResumed runtime denied))
    | Action.Focus nodeId ->
        gate (Runtime.ActionDescriptor.Focus nodeId) (fun () ->
            let selector =
                "[data-fuaran-node-id=\""
                + nodeId.Replace("\\", "\\\\").Replace("\"", "\\\"")
                + "\"]"

            let el = Browser.Dom.document.querySelector selector

            if isNull el then
                runtime.Warn(
                    sprintf "[Fuaran] Action.Focus('%s') addressed no rendered node — focus unchanged." nodeId
                )
            else
                (el :?> Browser.Types.HTMLElement).focus ())
    | Action.CommitLocal nodeId ->
        gate (Runtime.ActionDescriptor.CommitLocal nodeId) (fun () ->
            let eventName = sprintf "fuaran-commit-local-%s" nodeId
            let evt = Browser.Dom.window.document.createEvent "CustomEvent"
            evt.initEvent (eventName, false, true)
            Browser.Dom.window.dispatchEvent evt |> ignore)
    | _ ->
        // A payload shape or closure-bearing case the reader cannot construct
        // (`Dispatch`, `Call`, `ReadFileBody`, a bound `WriteToClipboard`).
        // Arriving here means the envelope and the interpreter disagree about
        // the disposition; it is RECORDED as a refusal rather than ignored.
        note (Error "resume interpreter reached an action this path holds no resolver for")

/// Interpret one raw envelope `action` value: read it typed, run it through the
/// gates, and record the gesture.
///
/// The emission point is HERE and not inside `runResumed`, for the reason
/// `Render.runActionAs` gives for its own placement: `runResumed` recurses
/// through `Action.Chain`, so emitting inside it would yield N records for one
/// gesture with no way to tell a chain from N clicks.
let private interpret (runtime: Runtime.IFuaranRuntime) (nodeId: string) (raw: obj) : unit =
    match readAction 0 raw with
    | Error reason ->
        runtime.Warn(sprintf "[Fuaran:resume] %s" reason)
        // An unreadable envelope entry is a REFUSED gesture, not an absent one:
        // the interpreter saw an interaction and declined to run it, and an audit
        // showing nothing there would be describing a different session.
        // `emitDescribed` is what lets the record exist at all — there is no
        // typed `Action` to describe, which is precisely the fact being recorded.
        ActionInvocation.emitDescribed
            resumeActionSink
            (ActionInvocation.clientSite AffordanceProvenance.TreeDeclared (Some nodeId) None)
            (ActionOutcome.Denied reason)
            "Unreadable"
    | Ok action ->
        match resumeActionSink with
        | None ->
            // Recording off — the shipped default. No ref cell, no site, no
            // record. The GATES still run: recording and gating are independent,
            // and conflating them is how the pre-1523 path came to have neither.
            runResumed runtime (ref []) action
        | Some _ ->
            let denied = ref []

            let site =
                ActionInvocation.clientSite AffordanceProvenance.TreeDeclared (Some nodeId) None

            let outcome =
                try
                    runResumed runtime denied action

                    match List.rev denied.Value with
                    | [] -> ActionOutcome.Dispatched
                    | reasons -> ActionOutcome.Denied(String.concat "; " reasons)
                with ex ->
                    ActionInvocation.emit resumeActionSink site (ActionOutcome.Failed ex.Message) action
                    reraise ()

            ActionInvocation.emit resumeActionSink site outcome action

/// Read + JSON-parse the resume envelope `<script>` for `rootId`. `None` when
/// the script is absent (the page wasn't rendered resumable).
let readEnvelope (rootId: string) : obj option =
    let el = Browser.Dom.document.getElementById (sprintf "fuaran-resume-%s" rootId)

    if isNull (box el) then
        None
    else
        Some(JS.JSON.parse el.textContent)

/// Resolve the nearest enclosing addressable node id for an event target.
let private nearestNodeId (target: obj) : string option =
    if isNull target then
        None
    else
        let el: Browser.Types.Element = unbox target

        // `Element.closest` returns an `Element option` in the Fable Browser
        // binding (whereas `getAttribute` returns a nullable string).
        match el.closest "[data-fuaran-node-id]" with
        | Some hit ->
            let id = hit.getAttribute "data-fuaran-node-id"
            if isNull (box id) then None else Some id
        | None -> None

/// Install the resume interpreter for the render root `rootId`: verify the
/// tree-hash, then attach **one** delegated listener at the document root per
/// event type the envelope needs (`click` + `submit`). Returns `None` when there
/// is nothing to resume — no envelope, or a tree-hash mismatch (which calls
/// `OnMismatch`) — and otherwise a handle that tears the listeners down.
let installWithHandle (rootId: string) (config: ResumeConfig) : ResumeHandle option =
    match readEnvelope rootId with
    | None -> None
    | Some envelope ->
        // Resume-mismatch detection (§6): the envelope's tree-hash vs the marker
        // the server stamped on the resume-root script element.
        let scriptEl =
            Browser.Dom.document.getElementById (sprintf "fuaran-resume-%s" rootId)

        let stamped = scriptEl.getAttribute "data-fuaran-resume-hash"
        let envHash: string = envelope?treeHash

        if isNull (box stamped) || stamped <> envHash then
            config.Runtime.Warn "[Fuaran:resume] tree-hash mismatch — falling back to client render"
            config.OnMismatch()
            None
        else
            let actions: obj = envelope?actions

            let handle (e: Browser.Types.Event) =
                match nearestNodeId e.target with
                | None -> ()
                | Some nodeId ->
                    let entry: obj = actions?(nodeId)

                    if not (isNull (box entry)) then
                        // The submit listener must stop the inert <form>'s native
                        // navigation so resume owns the interaction.
                        if e.``type`` = "submit" then
                            e.preventDefault ()

                        match (entry?disposition: string) with
                        | "interpret" -> interpret config.Runtime nodeId entry?action
                        | "boot" -> config.BootSubtree nodeId
                        | "fallback" -> config.HydrateSubtree nodeId
                        | d -> config.Runtime.Warn(sprintf "[Fuaran:resume] unknown disposition '%s'" d)

            // One delegated listener per event type, at the document root (O(1),
            // not per-node). The listener set is exactly the event types the
            // envelope's handlers enumerate.
            Browser.Dom.document.addEventListener ("click", handle)
            Browser.Dom.document.addEventListener ("submit", handle)

            let mutable torn = false

            Some
                { Teardown =
                    fun () ->
                        if not torn then
                            torn <- true
                            Browser.Dom.document.removeEventListener ("click", handle)
                            Browser.Dom.document.removeEventListener ("submit", handle) }

/// `installWithHandle` reduced to the boolean the pre-1523 surface returned, so
/// every existing call site compiles unchanged. A host that tears roots down
/// wants the handle.
let install (rootId: string) (config: ResumeConfig) : bool =
    (installWithHandle rootId config).IsSome

#else

/// Browser-only on the .NET pipeline (Fable + DOM delegation). The server tier
/// renders the resumable HTML string (`Renderer.Server.Resume`); the interpreter
/// runs in the browser, so these entry points exist for API shape only and throw
/// if reached off-browser.
let readEnvelope (_rootId: string) : obj option =
    failwith "Resume.readEnvelope is browser-only (Fable + DOM)"

let installWithHandle (_rootId: string) (_config: ResumeConfig) : ResumeHandle option =
    failwith "Resume.installWithHandle is browser-only (Fable + DOM event delegation)"

let install (_rootId: string) (_config: ResumeConfig) : bool =
    failwith "Resume.install is browser-only (Fable + DOM event delegation)"

#endif
