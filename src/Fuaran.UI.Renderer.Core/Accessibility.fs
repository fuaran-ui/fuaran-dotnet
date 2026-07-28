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
