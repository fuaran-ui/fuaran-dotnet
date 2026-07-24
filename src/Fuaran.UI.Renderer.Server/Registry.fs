module Fuaran.UI.Renderer.Server.Registry

// ============================================================================
//  Fuaran — server-side Custom-renderer registry (Phase 141).
//
//  The `NodeKind.Custom` escape hatch lets a host plug domain components
//  (engraved SVG, bespoke widgets, server-rendered charts) into a Fuaran tree.
//  The client renderer maps `(moduleId, componentId, props)` to a Feliz
//  `ReactElement` via its `CustomRendererRegistry`; this is the **HTML-string
//  twin** for the server renderer — a registry mapping the same key to a
//  `Feliz.ViewEngine` element.
//
//  Fuaran ships **the seam**; the domain renderers live in the consumer app,
//  never in the language tier (FGP 6 / the cultural-posture invariant). The
//  registered closure is a **host trust boundary** — exactly like the client
//  `RegisterCustomRenderer` — so it is expected to escape its own output; this
//  module does not police it (see `SANITIZATION.md` "Custom-renderer trust
//  boundary").
// ============================================================================

open Feliz.ViewEngine
open Fuaran.Core
open Fuaran.UI.Types

/// A server-side Custom renderer: the node's `props` → a server HTML node.
type ServerCustomRenderer = Map<string, JVal> -> ReactElement

/// Registry of server Custom renderers keyed on `(moduleId, componentId)`,
/// parallel to the client `CustomRendererRegistry`. `Hashes` optionally records
/// the registered renderer's content hash per key for the Phase 70
/// bounded-escape verification on the server path.
type ServerCustomRendererRegistry =
    { Renderers: Map<string * string, ServerCustomRenderer>
      Hashes: Map<string * string, string> }

/// The empty registry — no host components registered (every Custom node falls
/// back to the labelled placeholder, matching the client's unregistered path).
let empty: ServerCustomRendererRegistry =
    { Renderers = Map.empty
      Hashes = Map.empty }

/// Register a server Custom renderer for `(moduleId, componentId)`.
let register
    (moduleId: string)
    (componentId: string)
    (renderer: ServerCustomRenderer)
    (reg: ServerCustomRendererRegistry)
    : ServerCustomRendererRegistry =
    { reg with
        Renderers = Map.add (moduleId, componentId) renderer reg.Renderers }

/// Register a server Custom renderer plus the content hash the renderer's source
/// is expected to carry (the Phase 70 `ContentHash.Hash`). A `Custom` node that
/// declares a `ContentHash` is verified against this on render.
let registerWithHash
    (moduleId: string)
    (componentId: string)
    (hash: string)
    (renderer: ServerCustomRenderer)
    (reg: ServerCustomRendererRegistry)
    : ServerCustomRendererRegistry =
    { reg with
        Renderers = Map.add (moduleId, componentId) renderer reg.Renderers
        Hashes = Map.add (moduleId, componentId) hash reg.Hashes }

/// Register a typed Custom **contract** (Phase 164) — the server twin of the
/// client `CustomRendererRegistry.RegisterContract`. One contract value drives
/// both registries, so the four-way agreement (encode / client decode / server
/// decode / hash) is structural. A decode failure renders a labelled
/// placeholder naming the failing key + emits a diagnostic (12.D via `eprintfn`),
/// never a blank box. The contract's derived hash is recorded for the Phase-70
/// bounded-escape verification.
let registerContract
    (contract: Fuaran.UI.CustomContract<'Props>)
    (render: 'Props -> ReactElement)
    (reg: ServerCustomRendererRegistry)
    : ServerCustomRendererRegistry =
    let wrapped (props: Map<string, JVal>) : ReactElement =
        match contract.Decode props with
        | Ok p -> render p
        | Error e ->
            eprintfn
                "[Fuaran] Custom decode failed for %s.%s — key '%s': %s"
                contract.ModuleId
                contract.ComponentId
                e.Key
                e.Message

            Html.div
                [ prop.className (
                      sprintf
                          "fuaran-kind-custom-placeholder fuaran-custom-decode-error fuaran-custom-%s-%s"
                          contract.ModuleId
                          contract.ComponentId
                  )
                  prop.custom ("data-fuaran-custom-decode-error", e.Key)
                  prop.text (
                      sprintf
                          "[fuaran:custom %s.%s — decode error (key '%s'): %s]"
                          contract.ModuleId
                          contract.ComponentId
                          e.Key
                          e.Message
                  ) ]

    registerWithHash contract.ModuleId contract.ComponentId contract.Hash.Hash wrapped reg

let tryRender
    (moduleId: string)
    (componentId: string)
    (reg: ServerCustomRendererRegistry)
    : ServerCustomRenderer option =
    Map.tryFind (moduleId, componentId) reg.Renderers

let tryHash (moduleId: string) (componentId: string) (reg: ServerCustomRendererRegistry) : string option =
    Map.tryFind (moduleId, componentId) reg.Hashes
