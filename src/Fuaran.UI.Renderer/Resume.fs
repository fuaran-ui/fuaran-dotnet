module Fuaran.UI.Renderer.Resume

// ============================================================================
//  Fuaran — zero-hydration resumability: client interpreter runtime
//  (Phase 177 spike — throwaway-grade, Fable-portable per FGP 2).
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
//     `WriteToClipboard` / `AiTool` / `CommitLocal`) is executed directly
//     against `IFuaranRuntime` with no view — the ≈ 0-JS happy path. Because it
//     routes through the *same* runtime the hydrated `runAction` uses, op-stream
//     + telemetry emission is identical (FGP 5 — resumability is a load
//     strategy, not a new dispatch path).
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
// ============================================================================

open Fuaran.UI.Types

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

#if FABLE_COMPILER
open Fable.Core
open Fable.Core.JsInterop

/// Interpret one decoded data-shaped action (a parsed envelope `action` object)
/// against the runtime. `Chain` recurses; the closure-sentinel cases never
/// reach here (their nodes carry a `boot` / `fallback` disposition and are
/// routed before interpretation).
let rec private interpret (runtime: Runtime.IFuaranRuntime) (action: obj) : unit =
    match (action?``$type``: string) with
    | "Navigate" -> runtime.Navigate(action?route)
    | "Notify" -> runtime.Notify(action?channel, Runtime.JsonBridge.jsToJVal action?payload)
    | "SetState" -> runtime.SetState(action?key, Runtime.JsonBridge.jsToJVal action?value)
    | "AiTool" -> runtime.InvokeAiTool(action?toolName, Runtime.JsonBridge.jsToJVal action?args)
    | "WriteToClipboard" -> runtime.WriteToClipboard(action?text)
    | "CommitLocal" ->
        // Mirror `Render.runAction`: dispatch the namespaced DOM CustomEvent the
        // LocalBinding's listener drains on.
        let nodeId: string = action?nodeId
        let eventName = sprintf "fuaran-commit-local-%s" nodeId
        let evt = Browser.Dom.window.document.createEvent "CustomEvent"
        evt.initEvent (eventName, false, true)
        Browser.Dom.window.dispatchEvent evt |> ignore
    | "Chain" ->
        let ops: obj[] = action?ops

        for op in ops do
            interpret runtime op
    | other -> runtime.Warn(sprintf "[Fuaran:resume] no interpreter for action $type '%s'" other)

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
/// event type the envelope needs (`click` + `submit` for the spike). Returns
/// `false` (and calls `OnMismatch` / no-ops) when there is nothing to resume —
/// no envelope, or a tree-hash mismatch.
let install (rootId: string) (config: ResumeConfig) : bool =
    match readEnvelope rootId with
    | None -> false
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
            false
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
                        | "interpret" -> interpret config.Runtime entry?action
                        | "boot" -> config.BootSubtree nodeId
                        | "fallback" -> config.HydrateSubtree nodeId
                        | d -> config.Runtime.Warn(sprintf "[Fuaran:resume] unknown disposition '%s'" d)

            // One delegated listener per event type, at the document root (O(1),
            // not per-node). The spike covers click + submit — the listener set
            // is exactly the event types the envelope's handlers enumerate.
            Browser.Dom.document.addEventListener ("click", handle)
            Browser.Dom.document.addEventListener ("submit", handle)
            true

#else

/// Browser-only on the .NET pipeline (Fable + DOM delegation). The server tier
/// renders the resumable HTML string (`Renderer.Server.Resume`); the interpreter
/// runs in the browser, so these entry points exist for API shape only and
/// throw if reached off-browser.
let readEnvelope (_rootId: string) : obj option =
    failwith "Resume.readEnvelope is browser-only (Fable + DOM)"

let install (_rootId: string) (_config: ResumeConfig) : bool =
    failwith "Resume.install is browser-only (Fable + DOM event delegation)"

#endif
