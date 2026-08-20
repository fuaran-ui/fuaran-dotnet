module Fuaran.UI.Renderer.Accessibility

// ============================================================================
//  Fuaran — accessibility-attribute emission (pure spine, Phase 138).
//
//  Renders a `Node`'s `Accessibility` trait into a list of `(key, value)`
//  HTML-attribute pairs. The client renderer feeds these into `prop.custom`;
//  the server renderer (Phase 140) feeds the same pairs into ViewEngine
//  attributes — one parity-locked projection, two emission backends.
//
//  Kept as a pure helper (no Feliz / Fable reference) so `AccessibilityTests.fs`
//  can assert the attribute set without spinning up a ReactElement (Feliz's
//  .NET-side ReactElement is opaque; `(string * string) list` is the pure-F#
//  contract the tests pin) AND so the server renderer can call it from a
//  pure-.NET context. Extracted out of the Feliz-coupled `Render.fs` into
//  `Fuaran.UI.Renderer.Core` for exactly that reuse; `Render.fs` keeps a thin
//  re-export (`Render.accessibilityAttributes`) so existing call sites and
//  tests are unchanged.
//
//  `aria-labelledby` / `aria-describedby` carry the referenced Node's HTML
//  `id` — same string the `render` function emits as `prop.id` (the
//  `NodeId`'s inner string).
//
//  WHERE the projection lands is a separate question from what it contains,
//  and `forwardsToSemanticElement` below is the answer: see `docs/DECISIONS.md`
//  D4 (2026-08-20). Until then every renderer emitted the projection on the
//  node's wrapper `<div>` unconditionally, which put `role` / `aria-*` on a
//  non-interactive container for a kind whose body IS the semantic element.
// ============================================================================

open Fuaran.UI.Types

let private ariaRoleString (role: AriaRole) : string =
    match role with
    | AriaRole.Button -> "button"
    | AriaRole.Link -> "link"
    | AriaRole.Dialog -> "dialog"
    | AriaRole.Alert -> "alert"
    | AriaRole.Status -> "status"
    | AriaRole.Banner -> "banner"
    | AriaRole.Navigation -> "navigation"
    | AriaRole.Main -> "main"
    | AriaRole.Form -> "form"
    | AriaRole.Region -> "region"
    | AriaRole.Heading -> "heading"
    | AriaRole.Progressbar -> "progressbar"
    | AriaRole.Tab -> "tab"
    | AriaRole.Tablist -> "tablist"
    | AriaRole.Tabpanel -> "tabpanel"
    | AriaRole.Custom raw -> raw

let private liveRegionString (kind: LiveRegionKind) : string =
    match kind with
    | LiveRegionKind.Polite -> "polite"
    | LiveRegionKind.Assertive -> "assertive"
    | LiveRegionKind.Off -> "off"

/// Pure-F# helper: project an `Accessibility option` (resolved against the
/// supplied `BindingSources`) into the `(attr-name, attr-value)` pairs the
/// renderer emits as DOM attributes. Public so `AccessibilityTests.fs` can
/// assert the projection without a Feliz round-trip, and so the server
/// renderer (Phase 140) can emit the same pairs without Feliz/Fable.
let accessibilityAttributes
    (sources: BindingResolver.BindingSources)
    (a11y: Accessibility option)
    : (string * string) list =
    match a11y with
    | None -> []
    | Some a ->
        let labelAttr =
            a.Label
            |> Option.bind (fun b -> BindingResolver.tryResolve sources b)
            |> Option.filter (fun t -> t <> "")
            |> Option.map (fun t -> "aria-label", t)

        let labelledByAttr = a.LabelledBy |> Option.map (fun nid -> "aria-labelledby", nid)

        let describedByAttr =
            a.DescribedBy |> Option.map (fun nid -> "aria-describedby", nid)

        let roleAttr = a.Role |> Option.map (fun r -> "role", ariaRoleString r)

        let liveAttr = a.LiveRegion |> Option.map (fun k -> "aria-live", liveRegionString k)

        let hiddenAttr =
            a.Hidden
            |> Option.bind (fun b -> BindingResolver.tryResolve sources b)
            |> Option.bind (fun h -> if h then Some("aria-hidden", "true") else None)

        [ labelAttr; labelledByAttr; describedByAttr; roleAttr; liveAttr; hiddenAttr ]
        |> List.choose id

/// Does this kind render a body that IS the node's semantic element — so the
/// a11y projection belongs on the body, not on the wrapper `<div>`?
///
/// Three conditions, all required (`docs/DECISIONS.md` D4):
///
///  1. the body is a SINGLE root element — not a container of siblings, not a
///     label-wrapped control;
///  2. that element carries native semantics of its own (an interactive role,
///     or a graphic), so `role` / `aria-*` on an ancestor `<div>` is announced
///     against the wrong node;
///  3. the element IS the node — nothing else in the body competes for the
///     accessible name.
///
/// `Link` (`<a>`), `Button` (`<button>`) and `Image` (`<img>`) satisfy all
/// three. The form-field kinds deliberately do NOT: `Select` renders
/// `<label><span>…</span><select></label>`, so the control is not the body root
/// (1) and the wrapping `<label>` already supplies an accessible name (3) — a
/// forwarded `aria-label` would compete with it. Field-level targeting needs a
/// per-kind target selector, not this predicate.
///
/// Kind-level by construction: the wrapper must decide before the body is
/// rendered, and the only thing it has then is the `NodeKind`. Where an arm has
/// a runtime branch (the protected-email `Link`), the ARM owns placement within
/// its own body — see the `Link` arm in either renderer.
let forwardsToSemanticElement (kind: NodeKind<'Msg>) : bool =
    match kind with
    | NodeKind.Link _
    | NodeKind.Button _
    | NodeKind.Image _ -> true
    | _ -> false

/// Split already-sanitised `ExtraAttributes` pairs into the half that stays on
/// the wrapper and the half that follows the a11y projection: `(data-*, aria-*)`.
///
/// `data-*` is ADDRESSING — it sits beside `data-fuaran-node-id`, which is what
/// layout observers, DOM-snapshot hooks and the in-page introspection surface
/// scan for, so moving it would move the node's address. An `aria-*` hatch is an
/// accessibility attribute and belongs wherever the accessibility attributes go;
/// that half is the whole of the `aria-current` defect D4 records.
///
/// Only consulted for a kind that forwards — elsewhere both halves land on the
/// wrapper in the Map's key-sorted order, exactly as before.
let partitionExtraAttributes (pairs: (string * string) list) : (string * string) list * (string * string) list =
    pairs
    |> List.partition (fun (k, _) -> not (k.StartsWith("aria-", System.StringComparison.Ordinal)))
