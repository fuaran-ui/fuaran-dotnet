module Fuaran.Samples.Hydration.Main

// Client-side isomorphic hydration (model-b): reconstruct the SAME tree the
// server rendered (Tree.build) and attach React with hydrateRoot to the
// server-rendered DOM. After hydration, a minimal Elmish loop drives
// interactivity: dispatching a Msg updates the model and re-renders the
// already-hydrated root via Hydration.render (React reuses the root — a normal
// client update over the server-rendered shell). No createRoot, no in-browser
// wire-decode (the client owns the tree).

open Browser.Dom
open Fable.Core.JsInterop
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

// Capture React's hydration-mismatch warnings into a window-global so the
// harness can assert zero mismatches. Installed BEFORE hydrating.
emitJsStatement
    ()
    """
    window.__hydrationWarnings = [];
    var _e = console.error, _w = console.warn;
    console.error = function () { try { window.__hydrationWarnings.push('error: ' + Array.prototype.slice.call(arguments).map(function (x) { return (x && x.message) || String(x); }).join(' ')); } catch (e) {} return _e.apply(console, arguments); };
    console.warn = function () { try { window.__hydrationWarnings.push('warn: ' + Array.prototype.slice.call(arguments).map(function (x) { return (x && x.message) || String(x); }).join(' ')); } catch (e) {} return _w.apply(console, arguments); };
    """

type Msg = SetTab of int

let mutable private activeTab = 0
let mutable private root: Hydration.HydrationRoot option = None

/// The Elmish loop: update the model, then re-render the hydrated root.
let rec private dispatch (msg: Msg) : unit =
    (match msg with
     | SetTab i -> activeTab <- i)

    match root with
    | Some r -> Hydration.render r (view ())
    | None -> ()

/// Reconstruct the tree against the current model + render it to a ReactElement.
and private view () =
    Render.renderWithSources
        BindingResolver.empty
        dispatch
        (Tree.build<Msg> activeTab (fun i -> Action.dispatch (SetTab i)))

// Initial hydrate — attach React to the server-rendered DOM in place.
match Hydration.hydrateById "hydration-root" (view ()) with
| Some r ->
    root <- Some r
    console.log "[fuaran:hydration] hydrated #hydration-root with hydrateRoot"
| None -> console.error "[fuaran:hydration] container #hydration-root not found"
