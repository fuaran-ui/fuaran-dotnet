namespace Fuaran.UI.Renderer.Feliz

// ============================================================================
//  Fuaran.UI.Renderer.Feliz — the Feliz edge of the client renderer
//  (Phase 2514).
//
//  The renderer returns the opaque `RenderedElement` and keeps its React
//  binding internal, so a host that authors in Feliz converts here: a rendered
//  tree becomes a Feliz child, and a Custom renderer written in Feliz is
//  registered through the React-form members below. Both directions are
//  identity at runtime, because a `RenderedElement` is a React element; what
//  this package adds is the types.
// ============================================================================

open Feliz
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Runtime
#if FABLE_COMPILER
open Fuaran.UI.Renderer.BrowserRuntime
#endif

/// Conversions between the renderer's element and Feliz's.
module Rendered =

    /// A renderer element as a Feliz `ReactElement`, to place in a Feliz tree.
    let toReact (element: RenderedElement) : ReactElement = unbox element

    /// A Feliz `ReactElement` as a `RenderedElement`, to hand to the renderer.
    let ofReact (element: ReactElement) : RenderedElement = unbox element

/// The React-form Custom-renderer route: registration members that take a
/// renderer returning a Feliz element.
[<AutoOpen>]
module CustomRendererExtensions =

    type CustomRendererRegistry with

        /// `Register` for a renderer written in Feliz, in the ROOT render scope.
        member registry.RegisterReact
            (
                moduleId: string,
                componentId: string,
                renderFn: Map<string, JVal> -> ReactElement,
                ?contentHash: ContentHash
            ) : unit =
            let render = renderFn >> Rendered.ofReact

            match contentHash with
            | Some hash -> registry.Register(moduleId, componentId, render, hash)
            | None -> registry.Register(moduleId, componentId, render)

        /// `RegisterInScope` for a renderer written in Feliz: reachable only from
        /// trees rendered under `scope`.
        member registry.RegisterReactInScope
            (
                scope: string,
                moduleId: string,
                componentId: string,
                renderFn: Map<string, JVal> -> ReactElement,
                ?contentHash: ContentHash
            ) : unit =
            let render = renderFn >> Rendered.ofReact

            match contentHash with
            | Some hash -> registry.RegisterInScope(scope, moduleId, componentId, render, hash)
            | None -> registry.RegisterInScope(scope, moduleId, componentId, render)

        /// `RegisterContract` for a typed Custom contract rendered in Feliz.
        member registry.RegisterReactContract
            (contract: Fuaran.UI.CustomContract<'Props>, render: 'Props -> ReactElement)
            : unit =
            registry.RegisterContract(contract, render >> Rendered.ofReact)

#if FABLE_COMPILER
    // `BrowserRuntime` exists in the browser build only, like the renderer's
    // own module.
    type BrowserRuntime with

        /// `RegisterCustomRenderer` for a renderer written in Feliz.
        member runtime.RegisterReactRenderer
            (
                moduleId: string,
                componentId: string,
                renderFn: Map<string, JVal> -> ReactElement,
                ?contentHash: ContentHash
            ) : unit =
            runtime.CustomRendererRegistry.RegisterReact(moduleId, componentId, renderFn, ?contentHash = contentHash)

#endif

    type MutableRuntime with

        /// `RegisterCustomRenderer` for a renderer written in Feliz.
        member runtime.RegisterReactRenderer
            (
                moduleId: string,
                componentId: string,
                renderFn: Map<string, JVal> -> ReactElement,
                ?contentHash: ContentHash
            ) : unit =
            runtime.Registry.RegisterReact(moduleId, componentId, renderFn, ?contentHash = contentHash)
