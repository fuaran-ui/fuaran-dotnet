module Fuaran.UI.Renderer.BrowserRuntime

// ============================================================================
//  Fuaran — browser-shaped IFuaranRuntime implementation (session 3b)
//
//  Default in-browser implementation of `IFuaranRuntime`. Wraps `fetch` for
//  `Action.Call`, `window.dispatchEvent` for `Action.Notify`, hash-based
//  routing for `Action.Navigate`, `sessionStorage` for `Action.SetState`,
//  and `console.warn` for `Action.AiTool` + `Warn`.
//
//  Consumer apps will replace this with an adapter that routes
//  `Notify` to `INotificationChannel`, `Navigate` to the SDK router,
//  `SetState` to the Elmish model, and `InvokeAiTool` to the downstream
//  orchestration tier's tool registry. The browser runtime is the
//  standalone-host shape — the §4l down-shift portability story
//  requires Fuaran apps run without any platform dependency.
//
//  The whole module is `#if FABLE_COMPILER`-gated because the .NET-side
//  build of the renderer has no `Browser.*` types. .NET callers should use
//  `Runtime.diagnostic` instead.
// ============================================================================

open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.LayoutObserver
open Fuaran.UI.Renderer.Runtime
open Feliz

#if FABLE_COMPILER
open Fable.Core
open Fable.Core.JsInterop
open Browser
open Browser.Types

// ─── JSON helpers — `JVal` payloads lower to plain JS at the host seam ────

let private jsonValueToObj (j: Fuaran.Core.JVal) : obj = Runtime.JsonBridge.jvalToJs j

// ─── fetch wrapper — minimal viable for the demo ──────────────────────────
//
// Real consumer apps will swap in Fable.Remoting / Fetch.fetchAs. The
// demo just needs a GET that decodes JSON into an obj so callbacks fire.

[<Emit("fetch($0).then(r => r.ok ? r.json() : r.text().then(t => { throw new Error(t || r.statusText) })).then($1).catch(e => $2(String(e)))")>]
let private fetchJsonInto (url: string) (onResult: obj -> unit) (onError: string -> unit) : unit = jsNative

// ─── window globals via Emit (avoids leaning on Browser.Dom's typed
//     surface, some of which is null-attributed and trips F# 10 nullness
//     even on the renderer's <Nullable>disable</Nullable>) ────────────────

[<Emit("window.location.hash = $0")>]
let private setLocationHash (route: string) : unit = jsNative

[<Emit("console.warn($0)")>]
let private consoleWarn (message: string) : unit = jsNative

[<Emit("console.info($0)")>]
let private consoleInfo (message: string) : unit = jsNative

[<Emit("JSON.stringify($0)")>]
let private jsonStringify (value: obj) : string = jsNative

[<Emit("(typeof window !== 'undefined' && typeof CustomEvent !== 'undefined') ? (window.dispatchEvent(new CustomEvent('fuaran:notify:' + $0, { detail: $1 })), true) : false")>]
let private dispatchWindowEvent (channel: string) (payload: obj) : bool = jsNative

// Async-clipboard preferred, `document.execCommand("copy")`
// fallback for older browsers / non-secure contexts. The .then/.catch
// keeps the typed surface fire-and-forget; failure routes through
// console.warn (the renderer's diagnostic channel of last resort).
[<Emit("(function(t){ try { if (navigator && navigator.clipboard && navigator.clipboard.writeText) { return navigator.clipboard.writeText(t).then(function(){return true;}).catch(function(e){ console.warn('[Fuaran] navigator.clipboard.writeText failed: ' + e); return false; }); } var ta=document.createElement('textarea'); ta.value=t; ta.setAttribute('readonly',''); ta.style.position='fixed'; ta.style.top='0'; ta.style.left='0'; ta.style.opacity='0'; document.body.appendChild(ta); ta.select(); var ok=false; try { ok=document.execCommand('copy'); } catch(e) { console.warn('[Fuaran] execCommand(copy) fallback failed: ' + e); } document.body.removeChild(ta); return ok; } catch(e) { console.warn('[Fuaran] Action.WriteToClipboard threw: ' + e); return false; } })($0)")>]
let private writeClipboard (text: string) : obj = jsNative

// FileReader-backed read of a selected file's blob (Phase 136). `mode`
// is one of "text" / "base64" / "dataurl": "text" → readAsText; "dataurl"
// → the full `data:<mime>;base64,…` string; "base64" → readAsDataURL with
// the `…;base64,` header stripped (the bytes-to-API shape). `cb` fires from
// the async onload callback — the typed dispatch surface stays callback-
// shaped (same posture as Call). Failures route through console.warn and
// the callback never fires.
[<Emit("(function(file, mode, cb){ try { var r = new FileReader(); r.onload = function(){ var res = String(r.result == null ? '' : r.result); if (mode === 'base64') { var i = res.indexOf(','); cb(i >= 0 ? res.slice(i + 1) : res); } else { cb(res); } }; r.onerror = function(){ console.warn('[Fuaran] Action.ReadFileBody: FileReader error'); }; if (mode === 'text') { r.readAsText(file); } else { r.readAsDataURL(file); } } catch (e) { console.warn('[Fuaran] Action.ReadFileBody threw: ' + e); } })($0, $1, $2)")>]
let private readFileBlob (file: obj) (mode: string) (cb: string -> unit) : unit = jsNative

type BrowserRuntime(layoutObserver: ILayoutObserver option, allowAll: bool) =
    let customRegistry = CustomRendererRegistry()

    /// Default constructor — no layout observer wired, DENY-by-default dispatch
    /// (Phase 782).
    new() = BrowserRuntime(None, false)

    /// Layout-observer constructor, DENY-by-default dispatch (Phase 782).
    new(layoutObserver: ILayoutObserver option) = BrowserRuntime(layoutObserver, false)

    /// Register a renderer for `NodeKind.Custom(moduleId,
    /// componentId, props, ...)`. Subsequent renders consult the registry
    /// before falling back to the labelled placeholder. The
    /// optional `contentHash` argument is the registered renderer's
    /// source hash; the renderer's pre-dispatch verification compares
    /// it against any hash the tree declares.
    member _.RegisterCustomRenderer
        (moduleId: string, componentId: string, renderFn: Map<string, JVal> -> ReactElement, ?contentHash: ContentHash)
        : unit =
        match contentHash with
        | Some h -> customRegistry.Register(moduleId, componentId, renderFn, h)
        | None -> customRegistry.Register(moduleId, componentId, renderFn)

    /// Direct registry access — exposed for hosts that want to inspect /
    /// pre-populate registration state.
    member _.CustomRendererRegistry: CustomRendererRegistry = customRegistry

    interface IFuaranRuntime with
        member _.Call(ApiEndpoint endpoint, onResult) =
            fetchJsonInto endpoint onResult (fun err ->
                consoleWarn (sprintf "[Fuaran] Action.Call(%s) failed: %s" endpoint err))

        member _.Notify(channel, payload) =
            let raw = jsonValueToObj payload
            let delivered = dispatchWindowEvent channel raw

            if not delivered then
                consoleInfo (
                    sprintf "[Fuaran] Action.Notify(%s) dispatched (no listener detected via window event)." channel
                )

        member _.Navigate(route) = setLocationHash route

        member _.SetState(key, value) =
            // Route Action.SetState through the persisted + observable
            // StateStore: it writes the raw payload (string values persist to
            // localStorage), and notifies subscribers so `useStateValue`
            // surfaces re-render. `Binding.State` reads it back via the
            // snapshot the host merges into `BindingSources.State`.
            StateStore.set key (jsonValueToObj value)

        member _.InvokeAiTool(toolName, args) =
            let raw = jsonValueToObj args
            consoleInfo (sprintf "[Fuaran] Action.AiTool(%s) called with args: %s" toolName (jsonStringify raw))

        member _.WriteToClipboard(text) = writeClipboard text |> ignore

        member _.ReadFileBody(file, encoding, onRead) =
            match file.Handle with
            | Some handle ->
                let mode =
                    match encoding with
                    | FileReadEncoding.Text -> "text"
                    | FileReadEncoding.Base64 -> "base64"
                    | FileReadEncoding.DataUrl -> "dataurl"

                readFileBlob handle mode onRead
            | None ->
                consoleWarn
                    "[Fuaran] Action.ReadFileBody: FileSelection.Ref.Handle was None (no browser File blob); onRead will not fire."

        member _.Warn(message) =
            consoleWarn (sprintf "[Fuaran] %s" message)

        member _.LayoutObserver = layoutObserver

        member _.TryRenderCustom(moduleId, componentId, props) =
            customRegistry.TryRender(moduleId, componentId, props)

        member _.TryGetCustomRenderer(moduleId, componentId) =
            customRegistry.TryGet(moduleId, componentId)

        // DENY-by-default (Phase 782). The browser runtime is the standalone
        // host shape — the BYOK-playground case with no orchestration tier
        // behind it — which is precisely the host that cannot afford an
        // allow-everything default. `createPermissive ()` is the named opt-out.
        member _.CanDispatch(_) = allowAll

        // Standalone browser runtime composes no guest loader — a Mount renders
        // its declared empty state (Phase 266). A host that composes guests
        // supplies an orchestration-tier runtime overriding this member.
        member _.TryLoadGuest(_) = None

/// Default browser runtime singleton — the demo wires this once at mount time.
/// DENY-by-default dispatch since Phase 782.
let create () : IFuaranRuntime = BrowserRuntime() :> IFuaranRuntime

/// Construct a browser runtime wired to a layout observer.
/// The observer's MutationObserver self-discovers `data-fuaran-node-id`
/// elements as the renderer mounts them; no per-element ref hook needed.
let createWithLayoutObserver (observer: ILayoutObserver) : IFuaranRuntime =
    BrowserRuntime(Some observer) :> IFuaranRuntime

/// **The named opt-in back to the pre-0.14.0 allow-everything dispatch posture**
/// (Phase 782) for the browser host. Prefer a real `CanDispatch` allow-list;
/// this exists so a host that needs the old behaviour states it in its own
/// source rather than inheriting it.
let createPermissive () : IFuaranRuntime =
    BrowserRuntime(None, true) :> IFuaranRuntime

/// Permissive twin of `createWithLayoutObserver` (Phase 782).
let createPermissiveWithLayoutObserver (observer: ILayoutObserver) : IFuaranRuntime =
    BrowserRuntime(Some observer, true) :> IFuaranRuntime

#else

/// Non-Fable consumers fall back to the diagnostic runtime — the browser
/// substrate is meaningless under `dotnet build`. DENY-by-default dispatch
/// since Phase 782.
let create () : IFuaranRuntime = Runtime.diagnostic

/// .NET-side twin of the browser host's named permissive opt-in (Phase 782).
let createPermissive () : IFuaranRuntime = Runtime.permissive

let private layoutObserverRuntime (allowAll: bool) (observer: ILayoutObserver) : IFuaranRuntime =
    let registry = CustomRendererRegistry()

    { new IFuaranRuntime with
        member _.Call(endpoint, onResult) =
            Runtime.diagnostic.Call(endpoint, onResult)

        member _.Notify(channel, payload) =
            Runtime.diagnostic.Notify(channel, payload)

        member _.Navigate(route) = Runtime.diagnostic.Navigate(route)

        member _.SetState(key, value) = Runtime.diagnostic.SetState(key, value)

        member _.InvokeAiTool(toolName, args) =
            Runtime.diagnostic.InvokeAiTool(toolName, args)

        member _.WriteToClipboard(text) =
            Runtime.diagnostic.WriteToClipboard(text)

        member _.ReadFileBody(file, encoding, onRead) =
            Runtime.diagnostic.ReadFileBody(file, encoding, onRead)

        member _.Warn(message) = Runtime.diagnostic.Warn(message)
        member _.LayoutObserver = Some observer

        member _.TryRenderCustom(moduleId, componentId, props) =
            registry.TryRender(moduleId, componentId, props)

        member _.TryGetCustomRenderer(moduleId, componentId) = registry.TryGet(moduleId, componentId)

        member _.CanDispatch(action) =
            if allowAll then
                Runtime.permissive.CanDispatch(action)
            else
                Runtime.diagnostic.CanDispatch(action)

        member _.TryLoadGuest(scopeId) =
            Runtime.diagnostic.TryLoadGuest(scopeId) }

/// .NET-side fallback for the layout-observer-wired browser runtime.
/// Returns a wrapper that delegates everything to the diagnostic runtime
/// but reports the wired observer through `LayoutObserver`. It
/// also carries a `CustomRendererRegistry` so .NET-side tests can
/// register Custom renderers against this shape.
let createWithLayoutObserver (observer: ILayoutObserver) : IFuaranRuntime = layoutObserverRuntime false observer

/// Permissive twin of `createWithLayoutObserver` (Phase 782).
let createPermissiveWithLayoutObserver (observer: ILayoutObserver) : IFuaranRuntime =
    layoutObserverRuntime true observer

#endif
