module Fuaran.Samples.Catalog.SampleContext

// ============================================================================
//  The render context every catalog sample renders under.
//
//  `Render.RenderContext.create` plus the two things a catalog sample differs
//  in: the runtime it wires (the diagnostic one, or a browser runtime where a
//  sample writes state), and the PERMISSIVE egress posture. Every catalog tree
//  is HAND-AUTHORED, so the author is the trust boundary and the permissive
//  posture is correct, reached BY NAME here so a grep for `permissiveEgress`
//  finds it. A host rendering a DECODED tree must not copy this.
// ============================================================================

open Fuaran.UI.Types
open Fuaran.UI.Renderer

/// The context for rendering `node` in the catalog: `RenderContext.create`'s
/// defaults (no sink, no recording, no uploads, `Permissive` CSP, the root
/// scope) with `runtime` and the permissive egress posture.
let forTree
    (runtime: Runtime.IFuaranRuntime)
    (sources: BindingResolver.BindingSources)
    (dispatch: 'Msg -> unit)
    (node: Node<'Msg>)
    : Render.RenderContext<'Msg> =
    let ctx = Render.RenderContext.create sources dispatch node

    { ctx with
        Runtime = runtime
        Policy =
            { ctx.Policy with
                Egress = Sanitize.permissiveEgress } }

/// Render `node` under `forTree runtime sources dispatch node`, so each tree a
/// sample renders gets the context built for it.
let render
    (runtime: Runtime.IFuaranRuntime)
    (sources: BindingResolver.BindingSources)
    (dispatch: 'Msg -> unit)
    (node: Node<'Msg>)
    : Feliz.ReactElement =
    Render.render (forTree runtime sources dispatch node) node
