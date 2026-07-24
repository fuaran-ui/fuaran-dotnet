module Fuaran.UI.Renderer.Server.Hydration

// ============================================================================
//  Fuaran — isomorphic hydration: server-side embedded-tree emission (Phase 143).
//
//  The "hydration-ready" emission mode: alongside the server-rendered HTML, embed
//  the canonical wire-format tree (and optionally seed state) as a
//  `<script type="application/json">` payload keyed to the render root's stable
//  id. The client `hydrate` mount reads that script, decodes it via
//  `Fuaran.UI.Ops.JsonDecode`, and attaches via React `hydrateRoot` against the
//  existing server DOM (instead of `createRoot` clobbering it) — server render
//  for first paint + SEO, hydrate for interactivity, one canonical tree across
//  both pipelines.
//
//  Parity (Phase 142) makes this mismatch-free: because the server and client
//  renderers emit the same class + ARIA markup for the same tree, React's
//  hydration reconciler finds the DOM it expects and attaches without a
//  re-render or a mismatch warning.
//
//  Script-injection safety: the embedded JSON has `<` / `>` / `&` replaced with
//  their `\uXXXX` escapes so a `</script>` substring inside string data cannot
//  break out of the `<script>` element. JSON parsers read the escapes back to
//  the original characters, so the decoded tree is byte-identical to what the
//  server encoded.
// ============================================================================

open Feliz.ViewEngine
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions

/// The id of the embedded-tree `<script>` element for a render root, derived
/// from the root node's stable id. The client `hydrate` mount looks the tree up
/// by this id.
let scriptId (rootId: string) : string = sprintf "fuaran-hydrate-%s" rootId

/// Escape a JSON string for safe embedding inside a `<script>` element. JSON
/// parsers decode `<` etc. back to the original characters, so the wire
/// tree round-trips unchanged.
let private escapeForScript (json: string) : string =
    json.Replace("<", "\\u003c").Replace(">", "\\u003e").Replace("&", "\\u0026")

/// The embedded-tree `<script type="application/json">` element carrying the
/// canonical wire-format JSON, keyed to the render root's stable id.
let embeddedTreeElement (node: Node<obj>) : ReactElement =
    let (NodeId rootId) = node.Id
    let json = CanonicalJson.encodeNode node |> escapeForScript

    Html.script
        [ prop.custom ("type", "application/json")
          prop.id (scriptId rootId)
          prop.custom ("data-fuaran-hydrate-root", rootId)
          prop.dangerouslySetInnerHTML json ]

/// Render a **hydration-ready** HTML string: the body-fragment HTML plus the
/// embedded wire-format tree as a `<script type="application/json">` payload the
/// client `hydrate` mount decodes (`JsonDecode.decodeNode`) and attaches via
/// React `hydrateRoot`. The host places this where it wants the hydratable
/// surface; the document shell + reference CSS stay host-owned.
let renderHydratable (sources: BindingResolver.BindingSources) (node: Node<obj>) : string =
    Render.render sources node + Render.htmlView (embeddedTreeElement node)

// ─── Islands (partial hydration, Phase 163) ─────────────────────────────────
//
// A page marks individual subtrees as islands (`Node.asIsland`). The server
// emits the page statically — the renderer lifts each island marker onto a
// `<div data-fuaran-island="…">` boundary wrapper (the hydration container) —
// and, per island, embeds a scoped `<script>` carrying only that subtree's wire
// tree. The client (`Renderer.Hydration.hydrateIslands`) mounts `hydrateRoot`
// per island only; everything outside an island stays inert static HTML, and a
// page with zero islands emits zero hydrate script.
//
// Mismatch-freedom: the boundary wrapper's children are exactly the island
// node's normal static render (marker stripped), which is byte-identical to
// what the client re-renders into the wrapper — so React hydrates without a
// reconciliation warning (Phase 142 parity, scoped per island).

/// The id of an island's embedded hydrate `<script>` (distinct from the
/// whole-tree `scriptId`, so islands and a whole-tree payload can coexist).
let islandScriptId (islandId: string) : string =
    sprintf "fuaran-hydrate-island-%s" islandId

/// The container children of a node kind (mirrors the renderer's
/// `collectFragments` child map) — the walk basis for island collection.
let private childrenOf (node: Node<obj>) : Node<obj> list =
    match node.Kind with
    | NodeKind.Layout(LayoutKind.Box s) -> s.Children
    | NodeKind.Layout(LayoutKind.SplitPanel s) -> s.Children
    | NodeKind.Layout(LayoutKind.Tabs s) -> s.Children
    | NodeKind.Layout(LayoutKind.Stepper s) -> s.Children
    | NodeKind.Layout(LayoutKind.SummaryList s) -> s.Children
    | NodeKind.Layout(LayoutKind.Disclosure s) -> s.Children
    | NodeKind.ErrorBoundary s -> [ s.Child ]
    | NodeKind.FragmentDecl s -> [ s.Body ]
    | _ -> []

/// DFS collect every island `(islandId, node)` in document order.
let rec private collectIslands (node: Node<obj>) : (string * Node<obj>) list =
    let here =
        match Fuaran.UI.Node.islandId node with
        | Some id -> [ id, node ]
        | None -> []

    here @ (childrenOf node |> List.collect collectIslands)

/// The per-island embedded `<script type="application/json">` payload carrying
/// the island subtree's wire tree (marker stripped — `ExtraAttributes` is
/// wire-omitted anyway, so the payload is the inert inner content).
let private islandScriptElement (islandId: string) (node: Node<obj>) : ReactElement =
    let json =
        CanonicalJson.encodeNode (Fuaran.UI.Node.stripIsland node) |> escapeForScript

    Html.script
        [ prop.custom ("type", "application/json")
          prop.id (islandScriptId islandId)
          prop.custom ("data-fuaran-island-payload", islandId)
          prop.dangerouslySetInnerHTML json ]

/// Render the page statically with per-island boundary markers (the renderer
/// wraps each `Node.asIsland` subtree in `<div data-fuaran-island>`) plus one
/// embedded hydrate `<script>` per island. A tree with zero islands returns
/// exactly `Render.render sources node` (no wrappers, no scripts).
let renderWithIslands (sources: BindingResolver.BindingSources) (node: Node<obj>) : string =
    let staticHtml = Render.render sources node

    let scripts =
        collectIslands node
        |> List.map (fun (id, islandNode) -> Render.htmlView (islandScriptElement id islandNode))
        |> String.concat ""

    staticHtml + scripts
