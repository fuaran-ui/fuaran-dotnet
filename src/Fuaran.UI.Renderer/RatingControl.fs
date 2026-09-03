module Fuaran.UI.Renderer.RatingControl

// ============================================================================
//  Fuaran — the rating control's React component (Phase 1130)
//
//  Every judgement this control makes — the granularity, the clamp, the
//  keyboard model, the per-star fill, the announcement — lives in
//  `Fuaran.UI.Renderer.RatingModel` (in `Fuaran.UI.Renderer.Core`), which the SSR floor reads too. What
//  is here is the mounting and the interaction: the ARIA attributes the model
//  decided, the key handler that consults it, and the pointer surface.
//
//  ── Why this is a function component and not a composition ────────────────
//  `ComboboxControl`'s reason: the composition-based renderer holds no hooks,
//  so a control that needs React at all is wrapped and invoked through
//  `React.createElement`.
//
//  ── Cross-pipeline ────────────────────────────────────────────────────────
//  The model is ordinary F# and runs on .NET and Fable alike; only this file
//  touches React. The SSR floor for this control is the server renderer's
//  native radio group — see `RatingModel`'s header and `docs/SSR.md`, where the
//  difference between the two markups is stated normatively rather than left to
//  be discovered.
// ============================================================================

#nowarn "3261"

open Fable.Core
open Feliz
open Fuaran.UI.Renderer.RatingModel

[<Import("createElement", "react")>]
let private reactCreateElement (componentFn: obj) (props: obj) : ReactElement = jsNative

// ─── the component ──────────────────────────────────────────────────────────

/// Everything the control needs, already resolved: the renderer keeps its
/// `BindingSources` plumbing at the call site and this component holds only the
/// interaction.
type RatingProps =
    {| fieldId: string
       className: string
       max: int
       allowHalf: bool
       interactive: bool
       label: string
       value: float
       commit: float -> unit |}

let private starRow (props: RatingProps) : ReactElement list =
    [ for index, fill in List.indexed (fills props.max props.value) ->
          Html.span
              [ prop.key index
                prop.className ("fuaran-rating-star " + fillClass fill)
                // The fraction is a VALUE, so it rides as a custom property the
                // sheet reads; only the three-state class is vocabulary.
                prop.style
                    [ style.custom (
                          "--fuaran-rating-fill",
                          (fill * 100.0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                          + "%"
                      ) ]
                // The stars are decoration: the whole reading is on the control
                // itself (`aria-valuetext`, or the img label), and five
                // individually-announced glyphs would say it five more times.
                prop.custom ("aria-hidden", "true") ] ]

let private renderRating (props: RatingProps) : ReactElement =
    let shown = clamp props.max props.value

    let reading =
        let text = valueText props.max shown

        if props.label = "" then text else props.label + ": " + text

    if not props.interactive then
        // The display case. A picture of a score, named in full — no focus
        // stop, no slider role, nothing that invites a gesture the document
        // cannot honour.
        Html.span
            [ prop.className (props.className + " fuaran-rating fuaran-rating-static")
              prop.custom ("role", "img")
              prop.custom ("aria-label", reading)
              prop.custom ("data-fuaran-field", props.fieldId)
              prop.children (starRow props) ]
    else
        Html.span
            [ prop.className (props.className + " fuaran-rating")
              prop.custom ("role", "slider")
              prop.tabIndex 0
              prop.custom ("aria-valuemin", 0)
              prop.custom ("aria-valuemax", props.max)
              prop.custom ("aria-valuenow", shown)
              prop.custom ("aria-valuetext", valueText props.max shown)
              if props.label <> "" then
                  prop.custom ("aria-label", props.label)
              prop.custom ("data-fuaran-field", props.fieldId)
              prop.onKeyDown (fun ev ->
                  match keyIntent props.allowHalf props.max shown ev.key with
                  | Some next ->
                      // Only OUR keys are swallowed — `keyIntent` returning
                      // `None` leaves Tab and the host's chords alone.
                      ev.preventDefault ()

                      if next <> shown then
                          props.commit next
                  | None -> ())
              prop.children (
                  starRow props
                  @ [
                      // The click targets sit ON TOP of the star row, one per
                      // enterable position, so a pointer commits the same
                      // figures the keyboard can reach — including halves. They
                      // are `aria-hidden` and not focus stops: the slider above
                      // is the single control, and this is its pointer surface.
                      Html.span
                          [ prop.key "hit"
                            prop.className "fuaran-rating-hits"
                            prop.custom ("aria-hidden", "true")
                            prop.children
                                [ let positions = int (System.Math.Round(float props.max / step props.allowHalf))

                                  for i in 1..positions ->
                                      let target = snap props.allowHalf props.max (float i * step props.allowHalf)

                                      Html.span
                                          [ prop.key i
                                            prop.className "fuaran-rating-hit"
                                            prop.onClick (fun _ -> props.commit target) ] ] ] ]
              ) ]

/// Mount the control. Invoked through `React.createElement` for
/// `ComboboxControl`'s reason — the composition-based renderer holds no hooks,
/// so a control that needs them is wrapped as a function component.
let rating (props: RatingProps) : ReactElement =
    reactCreateElement renderRating (box props)
