namespace Fuaran.UI.StyleObserver

// ─── BrowserStyleObserver — Fable / getComputedStyle-backed ─────
//
// The production-path observer and the resolved-style twin of
// `BrowserLayoutObserver`. Reads `getComputedStyle` per addressable
// element (discovered via `MutationObserver` on
// `[data-fuaran-node-id]`), climbs ancestors to build the
// background-layer stack the effective-background composite walk
// folds, runs the shared `Flags.toObservation` logic, emits to
// subscribers per the configured debounce + change-detection policy.
//
// **Self-discovery via MutationObserver.** The renderer emits
// `data-fuaran-node-id`; the observer scans the DOM on construction
// and watches via MutationObserver for new matching elements + class
// / style attribute changes (a theme toggle or tone change re-derives
// the affected node's flags). No per-element renderer ref hook — the
// renderer stays React-lifecycle-agnostic.
//
// **rAF coalescing.** A burst of style mutations (theme switch,
// font load, hover-driven recolour) batches into a single rAF tick;
// a wall-clock floor (default 100ms via
// `StyleObserverOptions.DebounceMs`) rate-limits beyond rAF.
//
// **Change-detection.** The observer remembers each NodeId's previous
// flag set; when `EmitOnFlagChangeOnly = true` (the v1 default), a
// tick that produces the same flags as the previous emission is
// suppressed. Initial emission per NodeId always fires.
//
// **The effective-background composite walk** is the load-bearing
// algorithm and lives in `Flags.effectiveBackground` (shared with the
// in-memory observer). This file's job is to *capture* the background
// layer stack (element's own `background-color`, then each ancestor's,
// element-first) and hand it to the shared derivation.
//
// **.NET-side stub.** Under `dotnet build`, the file compiles to a
// thin wrapper around `InMemoryStyleObserver` — same interface, no
// browser plumbing.

#if FABLE_COMPILER

open Fable.Core
open Fable.Core.JsInterop
open System
open System.Collections.Generic

[<AutoOpen>]
module private Internals =

    // ─── Browser-API thin wrappers ──────────────────────────────

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

    [<Emit("window.getComputedStyle($0)")>]
    let getComputedStyle (element: obj) : obj = jsNative

    [<Emit("$0[$1]")>]
    let propGet (target: obj) (name: string) : obj = jsNative

    let propGetString (target: obj) (name: string) : string = unbox<string> (propGet target name)

    [<Emit("new MutationObserver($0)")>]
    let newMutationObserver (callback: obj -> obj -> unit) : obj = jsNative

    [<Emit("$0.observe($1, $2)")>]
    let mutationObserverObserve (observer: obj) (target: obj) (options: obj) : unit = jsNative

    [<Emit("document.body")>]
    let documentBody () : obj = jsNative

    // ─── CSS colour parsing ─────────────────────────────────────
    //
    // Computed `color` / `background-color` always come back as
    // `rgb(r, g, b)` or `rgba(r, g, b, a)` (or `rgba(0, 0, 0, 0)` for
    // `transparent`). Parse defensively — anything unrecognised
    // becomes transparent so the layer is skipped by the composite walk.

    let private parseFloat (s: string) : float option =
        let mutable parsed = 0.0
        // 2-arg TryParse only — the 4-arg locale overload is unsupported
        // under Fable. Safe: CSS numeric tokens always use '.' as the
        // decimal separator (browser-level invariant).
        if Double.TryParse(s.Trim(), &parsed) then
            Some parsed
        else
            None

    let parseCssColor (raw: string) : Rgba =
        if isNull (box raw) || raw = "" || raw = "transparent" || raw = "none" then
            Rgba.transparent
        else
            let lower = raw.Trim().ToLowerInvariant()

            let inner =
                if lower.StartsWith("rgba(") then
                    Some(lower.Substring(5).TrimEnd(')'))
                elif lower.StartsWith("rgb(") then
                    Some(lower.Substring(4).TrimEnd(')'))
                else
                    None

            match inner with
            | None -> Rgba.transparent
            | Some body ->
                let parts = body.Split(',')

                match parts with
                | [| r; g; b |] ->
                    match parseFloat r, parseFloat g, parseFloat b with
                    | Some r, Some g, Some b -> Rgba.rgb r g b
                    | _ -> Rgba.transparent
                | [| r; g; b; a |] ->
                    match parseFloat r, parseFloat g, parseFloat b, parseFloat a with
                    | Some r, Some g, Some b, Some a -> Rgba.rgba r g b a
                    | _ -> Rgba.transparent
                | _ -> Rgba.transparent

    // ─── Element → StyleInput conversion ────────────────────────
    //
    // Climb parentElement collecting each ancestor's background-color
    // (element-first) until an opaque layer is reached or `<html>` is
    // passed — Flags.effectiveBackground folds the rest.

    [<Emit("""(function(el){
        var layers = [];
        var node = el;
        while (node && node !== document.documentElement) {
            var bg = window.getComputedStyle(node).backgroundColor;
            layers.push(bg);
            node = node.parentElement;
        }
        if (node) { layers.push(window.getComputedStyle(node).backgroundColor); }
        return layers;
    })($0)""")>]
    let backgroundColorStack (element: obj) : string array = jsNative

    let snapshotInput (element: obj) : Flags.StyleInput =
        let style = getComputedStyle element
        let fg = parseCssColor (propGetString style "color")

        let layers = backgroundColorStack element |> Array.map parseCssColor |> Array.toList

        let fontFamily =
            let raw = propGetString style "fontFamily"
            if isNull (box raw) || raw = "" then None else Some raw

        let emittedTone =
            let raw = getAttribute element "data-fuaran-tone"
            if isNull (box raw) || raw = "" then None else Some raw

        { Foreground = fg
          BackgroundLayers = layers
          FontFamily = fontFamily
          EmittedTone = emittedTone }

// ─── BrowserStyleObserver — the production observer ─────────────

/// Construct against the configured options. The observer attaches
/// its MutationObserver to `document.body` on construction; call
/// `Dispose()` to disconnect (renderer remount handles this
/// transparently in practice).
type BrowserStyleObserver(options: StyleObserverOptions, manifest: Fuaran.UI.ThemeManifest.ThemeManifest option) =
    let registry = Dictionary<string, obj>()
    let lastFlagSet = Dictionary<string, StyleFlag list>()
    let lastEmitAt = Dictionary<string, float>()
    let lastObservation = Dictionary<string, StyleObservation>()
    let subscribers = ResizeArray<string * StyleObservation -> unit>()
    let pendingNodeIds = HashSet<string>()
    let mutable rafHandle: int option = None
    let mutable disposed = false

    let buildObservation (nodeId: string) (element: obj) : StyleObservation =
        let baseObs = Flags.toObservation options nodeId (snapshotInput element)

        match manifest with
        | Some m ->
            { baseObs with
                Flags = baseObs.Flags @ ManifestFlags.perNodeFlags m baseObs }
        | None -> baseObs

    let emit (nodeId: string) (observation: StyleObservation) =
        for subscriber in subscribers do
            try
                subscriber (nodeId, observation)
            with _ ->
                // A subscriber throwing must not poison siblings; the
                // browser console already surfaces the trace.
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

    let registerElement (nodeId: string) (element: obj) =
        if not (registry.ContainsKey(nodeId)) then
            registry[nodeId] <- element
            scheduleFlush nodeId

    let unregisterElement (nodeId: string) =
        match registry.TryGetValue(nodeId) with
        | false, _ -> ()
        | true, _ ->
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
            // Cheap full rescan — re-walk the DOM on any mutation. The
            // registry is keyed by NodeId so re-registering is a no-op;
            // new elements get observed; departed elements are detected
            // by the mismatch and unregistered. Every still-present node
            // is rescheduled so a class / style mutation re-derives its
            // flags (a theme toggle recolours the whole tree).
            let nodeList = querySelectorAll "[data-fuaran-node-id]"
            let elements = arrayFrom nodeList
            let seen = HashSet<string>()

            for element in elements do
                let nodeId = getAttribute element "data-fuaran-node-id"

                if not (isNull (box nodeId)) && nodeId <> "" then
                    seen.Add(nodeId) |> ignore
                    registerElement nodeId element
                    scheduleFlush nodeId

            let toRemove =
                registry.Keys
                |> Seq.filter (fun nodeId -> not (seen.Contains(nodeId)))
                |> Seq.toList

            for nodeId in toRemove do
                unregisterElement nodeId)

    do
        scanInitialDom ()

        let options =
            createObj
                [ "childList" ==> true
                  "subtree" ==> true
                  "attributes" ==> true
                  "attributeFilter" ==> [| "class"; "style"; "data-fuaran-tone" |] ]

        mutationObserverObserve mutationObserver (documentBody ()) options

    /// Phase 144 constructor — no manifest, manifest-free flags only.
    new(options: StyleObserverOptions) = new BrowserStyleObserver(options, None)

    interface IStyleObserver with
        member _.Observe(nodeId: string) : StyleObservation option =
            match registry.TryGetValue(nodeId) with
            | true, element -> Some(buildObservation nodeId element)
            | false, _ ->
                match lastObservation.TryGetValue(nodeId) with
                | true, obs -> Some obs
                | false, _ -> None

        member _.ObserveTree(rootNodeId: string) : StyleObservation list =
            match registry.TryGetValue(rootNodeId) with
            | false, _ -> []
            | true, rootEl ->
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

        member _.Subscribe(handler: string * StyleObservation -> unit) : IDisposable =
            subscribers.Add(handler)

            { new IDisposable with
                member _.Dispose() = subscribers.Remove(handler) |> ignore }

        member _.Register(nodeId: string, element: obj) : unit = registerElement nodeId element

        member _.Unregister(nodeId: string) : unit = unregisterElement nodeId

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                propGet mutationObserver "disconnect"?call $ (mutationObserver) |> ignore

                match rafHandle with
                | Some h -> cancelAnimationFrame h
                | None -> ()

module BrowserStyleObserver =
    /// Construct with the v1 default options.
    let create () : BrowserStyleObserver =
        new BrowserStyleObserver(StyleObserverOptions.defaults)

    /// Construct with host-tunable options.
    let createWith (options: StyleObserverOptions) : BrowserStyleObserver = new BrowserStyleObserver(options)

    /// Construct with a wired `ThemeManifest` — the per-node
    /// manifest-aware (Phase 146) flags are appended to each observation.
    let createWithManifest
        (options: StyleObserverOptions)
        (manifest: Fuaran.UI.ThemeManifest.ThemeManifest)
        : BrowserStyleObserver =
        new BrowserStyleObserver(options, Some manifest)

#else

// ─── .NET-side stub ─────────────────────────────────────────────
//
// Under `dotnet build` the browser observer falls back to the
// in-memory one — same interface, no browser plumbing. Lets the
// renderer + orchestration + tests build cleanly without Fable.

/// .NET-side: returns an InMemoryStyleObserver typed as
/// IStyleObserver. Consumers compile against the interface; concrete
/// behaviour is supplied by the in-memory implementation under .NET,
/// by the real browser observer under Fable.
type BrowserStyleObserver(options: StyleObserverOptions, manifest: Fuaran.UI.ThemeManifest.ThemeManifest option) =
    let inner = InMemoryStyleObserver(options, manifest) :> IStyleObserver

    /// Phase 144 constructor — no manifest, manifest-free flags only.
    new(options: StyleObserverOptions) = new BrowserStyleObserver(options, None)

    interface IStyleObserver with
        member _.Observe(nodeId) = inner.Observe(nodeId)
        member _.ObserveTree(rootNodeId) = inner.ObserveTree(rootNodeId)
        member _.Subscribe(handler) = inner.Subscribe(handler)
        member _.Register(nodeId, element) = inner.Register(nodeId, element)
        member _.Unregister(nodeId) = inner.Unregister(nodeId)

module BrowserStyleObserver =
    let create () : BrowserStyleObserver =
        new BrowserStyleObserver(StyleObserverOptions.defaults)

    let createWith (options: StyleObserverOptions) : BrowserStyleObserver = new BrowserStyleObserver(options)

    /// Construct with a wired `ThemeManifest` — the per-node
    /// manifest-aware (Phase 146) flags are appended to each observation.
    let createWithManifest
        (options: StyleObserverOptions)
        (manifest: Fuaran.UI.ThemeManifest.ThemeManifest)
        : BrowserStyleObserver =
        new BrowserStyleObserver(options, Some manifest)

#endif
