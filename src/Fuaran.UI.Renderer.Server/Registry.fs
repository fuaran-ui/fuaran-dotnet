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

/// Registry of server Custom renderers keyed on `(scope, moduleId, componentId)`,
/// parallel to the client `CustomRendererRegistry`. `Hashes` optionally records
/// the registered renderer's content hash per key for the Phase 70
/// bounded-escape verification on the server path.
///
/// **Phase 783 — the key carries a RENDER SCOPE**, matching the client registry
/// and for the same reason: it was one process-wide map keyed on ids taken
/// straight off the wire, so a tree rendered on a public surface could invoke a
/// renderer registered for a privileged one. `None` is the root scope, where the
/// unscoped `register` / `registerWithHash` / `registerContract` land, so an
/// existing host is unaffected; a host separates surfaces by rendering under
/// distinct `ServerRenderContext.Scope` values and registering with
/// `registerInScope`.
type ServerCustomRendererRegistry =
    { Renderers: Map<string option * string * string, ServerCustomRenderer>
      Hashes: Map<string option * string * string, string> }

/// The empty registry — no host components registered (every Custom node falls
/// back to the labelled placeholder, matching the client's unregistered path).
let empty: ServerCustomRendererRegistry =
    { Renderers = Map.empty
      Hashes = Map.empty }

/// Register a server Custom renderer for `(moduleId, componentId)` in the ROOT
/// render scope.
let register
    (moduleId: string)
    (componentId: string)
    (renderer: ServerCustomRenderer)
    (reg: ServerCustomRendererRegistry)
    : ServerCustomRendererRegistry =
    { reg with
        Renderers = Map.add (None, moduleId, componentId) renderer reg.Renderers }

/// Register a server Custom renderer reachable ONLY from trees rendered under
/// `scope` (Phase 783). A tree rendered under any other scope — including the
/// root — falls back to the labelled placeholder.
let registerInScope
    (scope: string)
    (moduleId: string)
    (componentId: string)
    (renderer: ServerCustomRenderer)
    (reg: ServerCustomRendererRegistry)
    : ServerCustomRendererRegistry =
    { reg with
        Renderers = Map.add (Some scope, moduleId, componentId) renderer reg.Renderers }

/// Scoped twin of `registerWithHash` (Phase 783).
let registerInScopeWithHash
    (scope: string)
    (moduleId: string)
    (componentId: string)
    (hash: string)
    (renderer: ServerCustomRenderer)
    (reg: ServerCustomRendererRegistry)
    : ServerCustomRendererRegistry =
    { reg with
        Renderers = Map.add (Some scope, moduleId, componentId) renderer reg.Renderers
        Hashes = Map.add (Some scope, moduleId, componentId) hash reg.Hashes }

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
        Renderers = Map.add (None, moduleId, componentId) renderer reg.Renderers
        Hashes = Map.add (None, moduleId, componentId) hash reg.Hashes }

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

/// Scope-constrained lookup (Phase 783). No cross-scope fallback — a fallback
/// would make the scoping advisory, which is the same as not having it.
let tryRenderInScope
    (scope: string option)
    (moduleId: string)
    (componentId: string)
    (reg: ServerCustomRendererRegistry)
    : ServerCustomRenderer option =
    Map.tryFind (scope, moduleId, componentId) reg.Renderers

/// Root-scope lookup.
let tryRender
    (moduleId: string)
    (componentId: string)
    (reg: ServerCustomRendererRegistry)
    : ServerCustomRenderer option =
    tryRenderInScope None moduleId componentId reg

/// Scope-constrained hash lookup (Phase 783).
let tryHashInScope
    (scope: string option)
    (moduleId: string)
    (componentId: string)
    (reg: ServerCustomRendererRegistry)
    : string option =
    Map.tryFind (scope, moduleId, componentId) reg.Hashes

let tryHash (moduleId: string) (componentId: string) (reg: ServerCustomRendererRegistry) : string option =
    tryHashInScope None moduleId componentId reg
