module Fuaran.UI.Renderer.RenderParity

open Fuaran.UI
open Fuaran.UI.Types

// ============================================================================
//  RenderParity — what BOTH renderers emit when a node cannot render (Phase 2041).
//
//  The client renderer (Feliz) and the server renderer (Feliz.ViewEngine) are
//  two hand-written per-kind matches whose output must agree, or a hydrating
//  client throws the server's markup away. The element SHAPES are re-emitted in
//  each renderer, because the two element builders are different libraries; the
//  NAMES, TEXT and IDS inside those elements are defined here, once, so the two
//  renderers cannot disagree about them:
//
//    * `nodeKindName` — the "Category.Kind" discriminator a render failure is
//      reported and correlated under.
//    * `renderFailureCorrelationId` / `renderFailureText` — the per-node guard's
//      fallback element (`data-fuaran-render-correlation` and its text).
//    * `boundaryDoubleFailureMessage` — what an `ErrorBoundary` reports when its
//      `Fallback` fails as well as its `Child`.
//    * `fragmentCycleText` — the placeholder for a `FragmentRef` that re-enters
//      a fragment it is already expanding.
//    * `depthExceededText` — the marker for a subtree nested past
//      `WireLimits.MaxDepth`.
//    * `customWrapperAttributes` — whether a registered `Custom` renderer's
//      body is wrapped, and the wrapper's class and data attributes (Phase 2885).
//
//  FSharp.Core only and Fable-portable, like the rest of this project.
// ============================================================================

/// The "Category.Kind" projection of a node kind — the discriminator a render
/// failure is reported, aggregated and correlated under on both tiers.
let nodeKindName<'Msg> (kind: NodeKind<'Msg>) : string =
    // Phase 692 — the "Category.Kind" projection survives the flattening (the
    // .NET-side tests pin these strings), but the category is now DERIVED
    // (`Kind.category`) and the kind name comes from `Kind.name`, so a new kind
    // extends those rather than a third enumeration here.
    match kind with
    // Box renders under a role-dependent display name — the retired Card /
    // Dashboard / Separator / Grid / Stack vocabulary the catalog pins.
    | NodeKind.Box spec ->
        let inner =
            match spec.Role, spec.Layout with
            | BoxRole.Card, _ -> "Card"
            | (BoxRole.Dashboard, _)
            | (BoxRole.Group, BoxLayout.Auto) -> "Dashboard"
            | BoxRole.Separator, _ -> "Separator"
            | BoxRole.Group, BoxLayout.Grid _ -> "Grid"
            // `Masonry` postdates the retired vocabulary this projection
            // preserves, so it names itself rather than borrowing `Grid`'s
            // label — a diagnostic that said "Layout.Grid" for a masonry
            // failure would send a reader to the wrong renderer arm.
            | BoxRole.Group, BoxLayout.Masonry _ -> "Masonry"
            | BoxRole.Group, BoxLayout.Flex _ -> "Stack"

        "Layout." + inner
    | NodeKind.Custom spec -> sprintf "Custom.%s.%s" spec.ModuleId spec.ComponentId
    | NodeKind.ErrorBoundary _ -> "ErrorBoundary"
    | NodeKind.Switch _ -> "Switch"
    | NodeKind.FragmentDecl _ -> "FragmentDecl"
    | NodeKind.FragmentRef _ -> "FragmentRef"
    | NodeKind.Mount spec -> sprintf "Mount.%s" spec.ScopeId
    | k ->
        let category =
            match Kind.category k with
            | NodeCategory.Layout -> "Layout"
            | NodeCategory.Display -> "Display"
            | NodeCategory.Input -> "Input"
            | NodeCategory.Visualisation -> "Visualisation"
            | NodeCategory.Structural -> "Structural"

        category + "." + Kind.name k

/// The correlation id the per-node render guard stamps on its fallback element
/// (`data-fuaran-render-correlation`) and on the telemetry record it emits. A
/// content hash of node id and kind, so the same failure renders byte-identical
/// output on both tiers and on every render.
let renderFailureCorrelationId (nodeId: string) (kindName: string) : string =
    Ids.deterministicCorrelationId (nodeId + "|" + kindName)

/// The text of the per-node guard's fallback element.
let renderFailureText (nodeId: string) (kindName: string) (errorMessage: string) : string =
    sprintf "[fuaran: render failed for '%s' (%s) — %s]" nodeId kindName errorMessage

/// The message an `ErrorBoundary` reports when its `Fallback` throws as well as
/// its `Child`.
let boundaryDoubleFailureMessage (childError: string) (fallbackError: string) : string =
    sprintf "child failed (%s); fallback also failed (%s)" childError fallbackError

/// The text of the placeholder a `FragmentRef` renders when it re-enters a
/// fragment it is already expanding (a reference cycle).
let fragmentCycleText (fragmentName: string) : string =
    sprintf "[fuaran:fragment cycle '%s']" fragmentName

/// The text of the marker emitted in place of a subtree nested past
/// `WireLimits.MaxDepth`.
let depthExceededText: string =
    "[subtree omitted: nesting exceeds the wire limit MaxDepth = "
    + string WireLimits.MaxDepth
    + "]"

/// The attribute a `Custom` node's wrapper carries its declared exposed ids on,
/// space-separated.
[<Literal>]
let CustomExposedNodeIdsAttribute = "data-fuaran-exposed-node-ids"

/// The attribute that marks a `Custom` node's hash verdict: `advisory` on the
/// wrapper of a body rendered past a mismatch, `strict` on the refusal element
/// the server renders in place of a refused body.
[<Literal>]
let CustomHashMismatchAttribute = "data-fuaran-custom-hash-mismatch"

/// The wrapper both renderers put around a REGISTERED `Custom` renderer's body
/// (Phase 2885), as its attributes in emission order, or `None` for no wrapper.
///
/// A wrapper is emitted when the node declares exposed ids or the body renders
/// past an ADVISORY hash mismatch, and never otherwise, so a `Custom` node that
/// declares neither renders its body bare on both tiers. The wrapper is the one
/// place in the document where those two facts are attributes: a host's
/// stylesheet, an inspector or a test can select a component's wrapper by its
/// per-component class, read which interior ids it promised, and see that it
/// rendered on a hash that did not verify, which on the client was otherwise a
/// warning on the runtime's channel alone. Until this phase only the server
/// emitted it, so a page hydrating server markup met a `div` the client never
/// built.
///
/// The placeholder for an unregistered renderer and the refusal of a strict
/// mismatch are not bodies, and are not wrapped.
let customWrapperAttributes
    (moduleId: string)
    (componentId: string)
    (exposedNodeIds: string list)
    (advisoryMismatch: bool)
    : (string * string) list option =
    let exposed =
        match exposedNodeIds with
        | [] -> []
        | ids -> [ CustomExposedNodeIdsAttribute, String.concat " " ids ]

    let advisory =
        if advisoryMismatch then
            [ CustomHashMismatchAttribute, "advisory" ]
        else
            []

    match exposed @ advisory with
    | [] -> None
    | attrs -> Some(("class", Css.customWrapper moduleId componentId) :: attrs)
