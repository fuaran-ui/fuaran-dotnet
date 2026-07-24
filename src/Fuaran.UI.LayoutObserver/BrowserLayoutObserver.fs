namespace Fuaran.UI.LayoutObserver

// ─── BrowserLayoutObserver — Fable / ResizeObserver-backed ──────
//
// The production-path observer. Wires browser-native
// `ResizeObserver` per addressable element (discovered via
// `MutationObserver` on `[data-fuaran-node-id]`), reads
// `getBoundingClientRect` + `getComputedStyle` on the rAF tick,
// runs the shared `Flags.derive` logic, emits to subscribers per
// the configured debounce + change-detection policy.
//
// **Self-discovery via MutationObserver.** The renderer emits
// `data-fuaran-node-id` (per chunk 3); the observer scans the DOM
// on construction + watches via MutationObserver for new matching
// elements as they mount, and disconnects per-element observation
// when nodes leave the DOM. No per-element renderer ref hook — the
// renderer stays React-lifecycle-agnostic.
//
// **rAF coalescing.** ResizeObserver fires once per frame per
// observed element under burst conditions (drag-resize, animation,
// font load); rAF batches those into a single tick. A wall-clock
// floor (default 100ms via `LayoutObserverOptions.DebounceMs`)
// rate-limits beyond rAF — a 1000-event burst across 1s produces
// at most 10 subscriber emissions.
//
// **Change-detection.** The observer remembers each NodeId's
// previous flag set; when `EmitOnFlagChangeOnly = true` (the v1
// default), a tick that produces the same flags as the previous
// emission is suppressed. Initial emission per NodeId always fires
// regardless.
//
// **.NET-side stub.** Under `dotnet build`, the file compiles to a
// thin wrapper around `InMemoryLayoutObserver` — same interface,
// no browser plumbing. Lets the renderer / orchestration / tests
// build on .NET without a Fable substrate.

#if FABLE_COMPILER

open Fable.Core
open Fable.Core.JsInterop
open Browser
open Browser.Types
open System
open System.Collections.Generic

// The let bindings + helper
// functions below originally lived directly under `namespace
// Fuaran.UI.LayoutObserver`, which is illegal F# (top-level let
// bindings in a namespace surface as FS0201 "Namespaces cannot
// contain values" the moment FABLE_COMPILER is set and the source
// is exposed to F#'s parser). The `.NET-side` build never tripped
// this because `#if FABLE_COMPILER` hid the entire block; the
// Fable transpile path was never exercised through this file from
// a consumer until the worked-example host integration.
// Wrapping into `[<AutoOpen>] module private Internals = ...` keeps
// the call sites in the `BrowserLayoutObserver` type below
// unchanged while moving the lets into a module where F# accepts
// them. The AutoOpen attribute means subsequent type / module
// declarations in this same namespace see the helpers without
// `Internals.X` qualification.

[<AutoOpen>]
module private Internals =

    // ─── Browser-API thin wrappers ──────────────────────────────
    //
    // Going through Emit lets us bypass `Browser.Dom`'s typed surface
    // where it's missing or null-attributed; matches `BrowserRuntime.fs`'s
    // pattern.

    [<Emit("requestAnimationFrame($0)")>]
    let requestAnimationFrame (callback: unit -> unit) : int = jsNative

    [<Emit("cancelAnimationFrame($0)")>]
    let cancelAnimationFrame (handle: int) : unit = jsNative

    [<Emit("performance.now()")>]
    let now () : float = jsNative

    [<Emit("document.querySelectorAll($0)")>]
    let querySelectorAll (selector: string) : obj = jsNative

    [<Emit("Array.from($0)")>]
    let arrayFrom (nodeList: obj) : obj array = jsNative

    [<Emit("$0.getAttribute($1)")>]
    let getAttribute (element: obj) (name: string) : string = jsNative

    [<Emit("$0.getBoundingClientRect()")>]
    let getBoundingClientRect (element: obj) : obj = jsNative

    [<Emit("window.getComputedStyle($0)")>]
    let getComputedStyle (element: obj) : obj = jsNative

    [<Emit("$0[$1]")>]
    let propGet (target: obj) (name: string) : obj = jsNative

    let propGetFloat (target: obj) (name: string) : float = unbox<float> (propGet target name)
    let propGetString (target: obj) (name: string) : string = unbox<string> (propGet target name)

    // Walks up parent elements looking for the nearest with computed
    // `overflow` ≠ `visible`. Returns the parent's bounding rect as a
    // 4-tuple, or None when no clipping ancestor is found before reaching
    // `<html>`.
    [<Emit("""(function(el){
        var node = el.parentElement;
        while (node && node !== document.documentElement) {
            var s = window.getComputedStyle(node);
            var ox = s.overflowX, oy = s.overflowY, o = s.overflow;
            if (ox !== 'visible' || oy !== 'visible' || (o && o !== 'visible')) {
                var r = node.getBoundingClientRect();
                return [r.left, r.top, r.right, r.bottom];
            }
            node = node.parentElement;
        }
        return null;
    })($0)""")>]
    let nearestClippingAncestorRect (element: obj) : obj = jsNative

    [<Emit("new ResizeObserver($0)")>]
    let newResizeObserver (callback: obj array -> unit) : obj = jsNative

    [<Emit("$0.observe($1)")>]
    let resizeObserverObserve (observer: obj) (element: obj) : unit = jsNative

    [<Emit("$0.unobserve($1)")>]
    let resizeObserverUnobserve (observer: obj) (element: obj) : unit = jsNative

    [<Emit("$0.disconnect()")>]
    let resizeObserverDisconnect (observer: obj) : unit = jsNative

    [<Emit("new MutationObserver($0)")>]
    let newMutationObserver (callback: obj -> obj -> unit) : obj = jsNative

    [<Emit("$0.observe($1, $2)")>]
    let mutationObserverObserve (observer: obj) (target: obj) (options: obj) : unit = jsNative

    [<Emit("document.body")>]
    let documentBody () : obj = jsNative

    // ─── Element → LayoutInput conversion ───────────────────────

    let parseStylePx (style: obj) (prop: string) : float option =
        let raw = propGetString style prop

        if isNull (box raw) || raw = "" || raw = "none" then
            None
        else
            // computed-style px values look like "120px" / "0px".
            let trimmed =
                if raw.EndsWith("px") then
                    raw.Substring(0, raw.Length - 2)
                else
                    raw

            let mutable parsed = 0.0

            // Fable workaround: the 4-arg `Double.TryParse(string,
            // NumberStyles, IFormatProvider, &result)` overload is
            // unsupported under Fable (silent NumberStyle / provider
            // ignore, surfaces as a Fable error under
            // TreatWarningsAsErrors). The 2-arg form is the common
            // subset both pipelines accept. Safe here because CSS
            // computed-style values always use `.` as the decimal
            // separator regardless of system locale (browser-level
            // invariant); the locale parameter would be a no-op
            // in this code path even on .NET.
            if Double.TryParse(trimmed, &parsed) then
                Some parsed
            else
                None

    let parseAspectRatio (style: obj) : float option =
        let raw = propGetString style "aspectRatio"

        if isNull (box raw) || raw = "" || raw = "auto" then
            None
        else
            // Browsers report aspect-ratio as either "1.5" / "16 / 9" / "auto".
            // Same Fable workaround as parseStylePx — 2-arg TryParse only.
            let parts = raw.Split([| '/' |], 2)

            if parts.Length = 2 then
                let mutable num = 0.0
                let mutable den = 0.0

                let okN = Double.TryParse(parts[0].Trim(), &num)
                let okD = Double.TryParse(parts[1].Trim(), &den)

                if okN && okD && den > 0.0 then Some(num / den) else None
            else
                let mutable parsed = 0.0

                if Double.TryParse(raw, &parsed) && parsed > 0.0 then
                    Some parsed
                else
                    None

    let snapshotInput (element: obj) : Flags.LayoutInput =
        let rect = getBoundingClientRect element
        let style = getComputedStyle element

        let width = propGetFloat rect "width"
        let height = propGetFloat rect "height"
        let left = propGetFloat rect "left"
        let top = propGetFloat rect "top"
        let right = propGetFloat rect "right"
        let bottom = propGetFloat rect "bottom"

        let scrollW = propGetFloat element "scrollWidth"
        let scrollH = propGetFloat element "scrollHeight"
        let clientW = propGetFloat element "clientWidth"
        let clientH = propGetFloat element "clientHeight"

        let overflowX = propGetString style "overflowX"
        let overflowY = propGetString style "overflowY"

        let ancestorRect = nearestClippingAncestorRect element

        let clipRect =
            if isNull ancestorRect then
                None
            else
                let arr = unbox<float array> ancestorRect
                Some(arr[0], arr[1], arr[2], arr[3])

        { Width = width
          Height = height
          ScrollWidth = Some scrollW
          ScrollHeight = Some scrollH
          ClientWidth = Some clientW
          ClientHeight = Some clientH
          OverflowX = if isNull (box overflowX) then None else Some overflowX
          OverflowY = if isNull (box overflowY) then None else Some overflowY
          MinWidth = parseStylePx style "minWidth"
          MinHeight = parseStylePx style "minHeight"
          ClippingAncestorRect = clipRect
          ElementRect = (left, top, right, bottom)
          ExpectedAspectRatio = parseAspectRatio style }

// ─── BrowserLayoutObserver — the production observer ────────────

/// Construct against the configured options. The observer attaches
/// its MutationObserver to `document.body` on construction; call
/// `Dispose()` to disconnect everything (renderer remount handles
/// this transparently in practice).
type BrowserLayoutObserver(options: LayoutObserverOptions) =
    let registry = Dictionary<string, obj>()
    let lastFlagSet = Dictionary<string, LayoutFlag list>()
    let lastEmitAt = Dictionary<string, float>()
    let lastObservation = Dictionary<string, LayoutObservation>()
    let subscribers = ResizeArray<string * LayoutObservation -> unit>()
    let pendingNodeIds = HashSet<string>()
    let mutable rafHandle: int option = None
    let mutable disposed = false

    let buildObservation (nodeId: string) (element: obj) : LayoutObservation =
        let input = snapshotInput element
        let flags = Flags.derive options input

        { NodeId = nodeId
          Width = input.Width
          Height = input.Height
          ViewportX = (let (l, _, _, _) = input.ElementRect in l)
          ViewportY = (let (_, t, _, _) = input.ElementRect in t)
          Flags = flags }

    let emit (nodeId: string) (observation: LayoutObservation) =
        for subscriber in subscribers do
            try
                subscriber (nodeId, observation)
            with _ ->
                // A subscriber throwing must not poison sibling
                // subscribers. Browser console already surfaces the
                // exception trace; the observer stays running.
                ()

    let flush () =
        rafHandle <- None
        let nowMs = now ()
        let pending = pendingNodeIds |> Seq.toList
        pendingNodeIds.Clear()

        for nodeId in pending do
            match registry.TryGetValue(nodeId) with
            | false, _ -> ()
            | true, element ->
                let observation = buildObservation nodeId element

                let previousFlags =
                    match lastFlagSet.TryGetValue(nodeId) with
                    | true, flags -> flags
                    | false, _ -> []

                let previousEmitAt =
                    match lastEmitAt.TryGetValue(nodeId) with
                    | true, t -> t
                    | false, _ -> -1.0

                let initialEmission = previousEmitAt < 0.0

                let respectsDebounce =
                    initialEmission || (nowMs - previousEmitAt) >= float options.DebounceMs

                let flagsChanged = observation.Flags <> previousFlags

                let shouldEmit =
                    respectsDebounce
                    && (initialEmission || (if options.EmitOnFlagChangeOnly then flagsChanged else true))

                lastObservation[nodeId] <- observation

                if shouldEmit then
                    lastFlagSet[nodeId] <- observation.Flags
                    lastEmitAt[nodeId] <- nowMs
                    emit nodeId observation

    let scheduleFlush (nodeId: string) =
        pendingNodeIds.Add(nodeId) |> ignore

        match rafHandle with
        | Some _ -> ()
        | None -> rafHandle <- Some(requestAnimationFrame flush)

    // ─── ResizeObserver — one shared instance ───────────────────
    //
    // ResizeObserver entries carry their target element under
    // `entry.target`. We look up the NodeId via the registry's
    // reverse lookup; this isn't O(1) but the registry is small
    // (typical app: tens of addressable nodes) and the callback
    // already runs at most once per frame.

    let resizeObserver =
        newResizeObserver (fun entries ->
            for entry in entries do
                let target = propGet entry "target"
                // Reverse-lookup NodeId by element identity.
                let mutable matched = None

                for kvp in registry do
                    if obj.ReferenceEquals(kvp.Value, target) then
                        matched <- Some kvp.Key

                match matched with
                | Some nodeId -> scheduleFlush nodeId
                | None -> ())

    let registerElement (nodeId: string) (element: obj) =
        if not (registry.ContainsKey(nodeId)) then
            registry[nodeId] <- element
            resizeObserverObserve resizeObserver element
            scheduleFlush nodeId

    let unregisterElement (nodeId: string) =
        match registry.TryGetValue(nodeId) with
        | false, _ -> ()
        | true, element ->
            resizeObserverUnobserve resizeObserver element
            registry.Remove(nodeId) |> ignore
            lastFlagSet.Remove(nodeId) |> ignore
            lastEmitAt.Remove(nodeId) |> ignore
            lastObservation.Remove(nodeId) |> ignore

    // ─── MutationObserver — DOM-scan + reactive discovery ───────

    let scanInitialDom () =
        let nodeList = querySelectorAll "[data-fuaran-node-id]"
        let elements = arrayFrom nodeList

        for element in elements do
            let nodeId = getAttribute element "data-fuaran-node-id"

            if not (isNull (box nodeId)) && nodeId <> "" then
                registerElement nodeId element

    let mutationObserver =
        newMutationObserver (fun _records _observer ->
            // Cheap full rescan — the spec mandates re-walking the
            // DOM on any mutation. The registry is keyed by NodeId
            // so re-registering is a no-op; new elements get
            // observed; departed elements are detected by the
            // mismatch and unregistered.
            let nodeList = querySelectorAll "[data-fuaran-node-id]"
            let elements = arrayFrom nodeList
            let seen = HashSet<string>()

            for element in elements do
                let nodeId = getAttribute element "data-fuaran-node-id"

                if not (isNull (box nodeId)) && nodeId <> "" then
                    seen.Add(nodeId) |> ignore
                    registerElement nodeId element

            // Detect departures.
            let toRemove =
                registry.Keys
                |> Seq.filter (fun nodeId -> not (seen.Contains(nodeId)))
                |> Seq.toList

            for nodeId in toRemove do
                unregisterElement nodeId)

    do
        // Kick off — scan current DOM, then begin watching.
        scanInitialDom ()
        let options = createObj [ "childList" ==> true; "subtree" ==> true ]
        mutationObserverObserve mutationObserver (documentBody ()) options

    interface ILayoutObserver with
        member _.Observe(nodeId: string) : LayoutObservation option =
            // Refresh from the live element if we have one; fall
            // back to the cached observation. The refresh path keeps
            // `Observe` honest under bursty conditions where the
            // rAF flush hasn't fired yet.
            match registry.TryGetValue(nodeId) with
            | true, element -> Some(buildObservation nodeId element)
            | false, _ ->
                match lastObservation.TryGetValue(nodeId) with
                | true, obs -> Some obs
                | false, _ -> None

        member this.ObserveTree(rootNodeId: string) : LayoutObservation list =
            match registry.TryGetValue(rootNodeId) with
            | false, _ -> []
            | true, _ ->
                // DOM-based walk: query descendants under the root
                // element that ALSO carry `data-fuaran-node-id`.
                let rootEl = registry[rootNodeId]
                let descendantsObj = propGet rootEl "querySelectorAll"
                let allDescendants = descendantsObj?call $ (rootEl, "[data-fuaran-node-id]")

                let elements =
                    if isNull allDescendants then
                        [||]
                    else
                        arrayFrom allDescendants

                let descendants =
                    elements
                    |> Array.choose (fun element ->
                        let nodeId = getAttribute element "data-fuaran-node-id"

                        if isNull (box nodeId) || nodeId = "" then
                            None
                        else
                            Some(buildObservation nodeId element))
                    |> Array.toList

                buildObservation rootNodeId rootEl :: descendants

        member _.Subscribe(handler: string * LayoutObservation -> unit) : IDisposable =
            subscribers.Add(handler)

            { new IDisposable with
                member _.Dispose() = subscribers.Remove(handler) |> ignore }

        member _.Register(nodeId: string, element: obj) : unit =
            // Renderer-driven explicit registration (chunk 3 chose
            // attribute-driven self-discovery, but Register stays
            // available for hosts that want to drive registration
            // manually — e.g. for custom non-`data-fuaran-node-id`
            // elements).
            registerElement nodeId element

        member _.Unregister(nodeId: string) : unit = unregisterElement nodeId

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                resizeObserverDisconnect resizeObserver
                // MutationObserver disconnect on dispose.
                propGet mutationObserver "disconnect"?call $ (mutationObserver) |> ignore

                match rafHandle with
                | Some h -> cancelAnimationFrame h
                | None -> ()

module BrowserLayoutObserver =
    /// Construct with the v1 default options.
    let create () : BrowserLayoutObserver =
        new BrowserLayoutObserver(LayoutObserverOptions.defaults)

    /// Construct with host-tunable options.
    let createWith (options: LayoutObserverOptions) : BrowserLayoutObserver = new BrowserLayoutObserver(options)

#else

// ─── .NET-side stub ─────────────────────────────────────────────
//
// Under `dotnet build` the browser observer falls back to the
// in-memory one — same interface, no browser plumbing. Lets the
// renderer + orchestration + tests build cleanly without Fable.

/// .NET-side: returns an InMemoryLayoutObserver typed as
/// ILayoutObserver. The renderer + AIInspectTool integration code
/// compiles against the interface; concrete behaviour is supplied
/// by the in-memory implementation under .NET, by the real browser
/// observer under Fable.
type BrowserLayoutObserver(options: LayoutObserverOptions) =
    let inner = InMemoryLayoutObserver(options) :> ILayoutObserver

    interface ILayoutObserver with
        member _.Observe(nodeId) = inner.Observe(nodeId)
        member _.ObserveTree(rootNodeId) = inner.ObserveTree(rootNodeId)
        member _.Subscribe(handler) = inner.Subscribe(handler)
        member _.Register(nodeId, element) = inner.Register(nodeId, element)
        member _.Unregister(nodeId) = inner.Unregister(nodeId)

module BrowserLayoutObserver =
    let create () : BrowserLayoutObserver =
        new BrowserLayoutObserver(LayoutObserverOptions.defaults)

    let createWith (options: LayoutObserverOptions) : BrowserLayoutObserver = new BrowserLayoutObserver(options)

#endif
