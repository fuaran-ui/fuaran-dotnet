module Fuaran.UI.Validator.AccessibilityCheck

// ============================================================================
//  Accessibility check.
//
//  Two rules, both keyed on the `A11yDetail` snapshot AstWalker captures for
//  `Fuaran.button` and `Fuaran.callout` calls:
//
//  - FUARAN040 (Error): a button with `Accessibility = None` explicit AND no
//    derivable Label. Triggers when the author opted out of the renderer's
//    fallback (which derives `aria-label` from `ButtonSpec.Label` when
//    `Accessibility.Label` is None) and also left the structural Label empty.
//    Surfaces as a build-failing finding so the button doesn't ship as an
//    unannounced screen-reader trap.
//
//  - FUARAN041 (Warning): a callout with `Tone = ToneVariant.Warning` or
//    `ToneVariant.Critical` AND `Accessibility = None` explicit. The default
//    `Defaults.Accessibility.callout` sets `LiveRegion = Assertive` so the
//    callout is announced; opting out of it on a Warning / Critical tone
//    loses the screen-reader announcement, which usually isn't the intent.
//
//  Scope. v1 covers only the explicit-opt-out cases — `Accessibility = None`
//  written in the record body. Inheriting the smart-ctor default (the common
//  path) doesn't fire either rule. Future versions can broaden coverage to
//  `Accessibility = Some { ... LiveRegion = None }` once the AST walker
//  extracts deeper into nested record shapes.
// ============================================================================

open Fuaran.UI.Validator.AstWalker
open Fuaran.UI.Validator.Findings

let private hasDerivableLabel (detail: A11yDetail) : bool =
    if not detail.HasLabelField then
        // No Label field set ⇒ structural label is the defaults' empty literal.
        // The renderer renders the button as an empty `<button>` — no aria-label.
        false
    else
        match detail.LabelLiteralValue with
        | Some literal -> literal <> ""
        // Non-literal Label (e.g. `Label = binding.query ...`) — trust the
        // binding to produce a non-empty string at runtime. FUARAN040 does not
        // fire; if the binding resolves empty, the renderer's empty-aria-label
        // omission still applies but that's a runtime concern, not a build-time
        // one. Validator stays conservative.
        | None -> true

let private isHighTone (tone: string) : bool = tone = "Warning" || tone = "Critical"

let check (calls: FuaranCall list) : Finding list =
    calls
    |> List.collect (fun call ->
        match call.Ctor, call.A11yDetail with
        | "button", Some detail when detail.AccessibilityExplicitNone && not (hasDerivableLabel detail) ->
            [ create
                  Error
                  "FUARAN040"
                  call.Location
                  "Interactive Fuaran.button has Accessibility = None explicit AND no derivable Label — the rendered button will have no aria-label, leaving it unannounced to screen readers. Provide an Accessibility.Label, an Accessibility.LabelledBy referencing a labelling node, or a non-empty ButtonSpec.Label." ]
        | "callout", Some detail when
            detail.AccessibilityExplicitNone
            && (detail.ToneCaseName |> Option.map isHighTone |> Option.defaultValue false)
            ->
            let tone = detail.ToneCaseName |> Option.defaultValue "Warning"

            [ create
                  Warning
                  "FUARAN041"
                  call.Location
                  (sprintf
                      "Fuaran.callout with Tone = ToneVariant.%s overrides the Accessibility default (Role = Alert, LiveRegion = Assertive) with Accessibility = None — the screen-reader announcement is lost. If suppression is intentional, leave a comment; otherwise inherit the default or set Accessibility.LiveRegion = Some LiveRegionKind.Assertive explicitly."
                      tone) ]
        | _ -> [])
