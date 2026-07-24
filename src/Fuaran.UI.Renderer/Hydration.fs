module Fuaran.UI.Renderer.Hydration

// ============================================================================
//  Fuaran — client-side isomorphic hydration mount (Phase 143).
//
//  The client counterpart to `Fuaran.UI.Renderer.Server`'s hydration-ready
//  emission. Where the server renders a tree to HTML on plain .NET, the client
//  **hydrates** that server-rendered DOM in place — attaching React to the
//  existing markup with `hydrateRoot` instead of `createRoot` (which would
//  clobber the server DOM and re-render from scratch, causing a flash).
//
//  Two client models:
//   - **model-b (this module's primary path):** the client reconstructs the
//     *same* `Node<'Msg>` tree in F# (the same authoring code the server used)
//     and hydrates it. No in-browser wire-decode needed — the client owns the
//     tree. The Elmish update loop then drives interactivity via `render` (React
//     reuses the same root; subsequent renders are normal client updates).
//   - decode-the-embedded-JSON: reading the server's embedded wire tree and
//     decoding it in-browser needs a Fable-portable decoder — the F# `Ops`
//     `JsonDecode` is not Fable-portable, so that path is a TS-tier capability
//     (`@fuaran-ui/ops`). See `docs/SSR.md` "Isomorphic hydration".
//
//  Parity (Phase 142) makes hydration mismatch-free: the server (ViewEngine)
//  and client (Feliz) renderers emit the same class + ARIA markup for the same
//  tree, so React's hydration reconciler finds the DOM it expects.
// ============================================================================

open Feliz
open Fuaran.UI.Types

#if FABLE_COMPILER
open Fable.Core
open Fable.Core.JsInterop

/// A React root handle returned by `hydrateRoot`. Supports `.render(element)`
/// for subsequent updates — the Elmish-over-hydration update path.
type HydrationRoot = obj

[<Import("hydrateRoot", "react-dom/client")>]
let private hydrateRootNative (container: Browser.Types.Element) (element: ReactElement) : HydrationRoot = jsNative

/// Hydrate the server-rendered DOM inside `container` with `element`, using
/// React `hydrateRoot` (NOT `createRoot`). Returns the root handle; call
/// `render root element` for subsequent updates. The server + client markup
/// must agree (Phase 142 parity) or React logs a hydration-mismatch warning.
let hydrate (container: Browser.Types.Element) (element: ReactElement) : HydrationRoot =
    hydrateRootNative container element

/// Re-render an already-hydrated root with a new element (the Elmish update
/// path — React reuses the hydrated root, so this is a normal client update).
let render (root: HydrationRoot) (element: ReactElement) : unit = root?render element |> ignore

/// Convenience: hydrate the element into the container with the given id.
/// `None` when no such element exists.
let hydrateById (containerId: string) (element: ReactElement) : HydrationRoot option =
    let el = Browser.Dom.document.getElementById containerId

    if isNull (box el) then
        None
    else
        Some(hydrate (el :> Browser.Types.Element) element)

/// Hydrate every island boundary (`[data-fuaran-island]`) the server emitted
/// (Phase 163), each as an **independent** React root. `renderIsland islandId`
/// returns the `ReactElement` for that island — the host reconstructs the
/// island's `Node<'Msg>` (model-b, same authoring code) and renders it with its
/// own dispatch; `None` leaves the island as inert server HTML. An island whose
/// mount throws **degrades to its static HTML** without breaking the others
/// (error isolation, mirroring `ErrorBoundary`). Nodes outside an island are
/// never touched. Returns the mounted roots.
let hydrateIslands (renderIsland: string -> ReactElement option) : HydrationRoot list =
    let boundaries = Browser.Dom.document.querySelectorAll "[data-fuaran-island]"

    let roots = ResizeArray<HydrationRoot>()

    for i in 0 .. boundaries.length - 1 do
        // `querySelectorAll` items already type as `Element` in the current
        // Fable Browser binding, so a `:?>` downcast warns 67 ("always holds")
        // → error under the repo's TreatWarningsAsErrors. `unbox` is the
        // behaviour-preserving form.
        let el: Browser.Types.Element = unbox boundaries[i]
        let islandId = el.getAttribute "data-fuaran-island"

        if not (isNull (box islandId)) then
            match renderIsland islandId with
            | Some element ->
                try
                    roots.Add(hydrate el element)
                with _ ->
                    () // error isolation — degrade this island to its static HTML
            | None -> ()

    List.ofSeq roots

/// `hydrateIslands` over a `Node<'Msg>` lookup (the Phase 163 signature shape):
/// `lookup islandId` returns the reconstructed island subtree; `toElement`
/// renders it (the host's renderer + dispatch). The island marker is stripped
/// before rendering so the client output matches the boundary wrapper's
/// children exactly (mismatch-free).
let hydrateIslandsWith
    (toElement: Node<'Msg> -> ReactElement)
    (lookup: string -> Node<'Msg> option)
    : HydrationRoot list =
    hydrateIslands (fun islandId -> lookup islandId |> Option.map (Fuaran.UI.Node.stripIsland >> toElement))

#else

/// Browser-only on the .NET pipeline. The server tier renders to HTML strings
/// (`Fuaran.UI.Renderer.Server`); hydration attaches React in the browser, so
/// these entry points exist for API shape only and throw if reached off-browser.
type HydrationRoot = obj

let hydrate (_container: Browser.Types.Element) (_element: ReactElement) : HydrationRoot =
    failwith "Hydration.hydrate is browser-only (Fable + react-dom/client hydrateRoot)"

let render (_root: HydrationRoot) (_element: ReactElement) : unit =
    failwith "Hydration.render is browser-only"

let hydrateById (_containerId: string) (_element: ReactElement) : HydrationRoot option =
    failwith "Hydration.hydrateById is browser-only"

let hydrateIslands (_renderIsland: string -> ReactElement option) : HydrationRoot list =
    failwith "Hydration.hydrateIslands is browser-only (Fable + react-dom/client hydrateRoot)"

let hydrateIslandsWith
    (_toElement: Node<'Msg> -> ReactElement)
    (_lookup: string -> Node<'Msg> option)
    : HydrationRoot list =
    failwith "Hydration.hydrateIslandsWith is browser-only"

#endif
