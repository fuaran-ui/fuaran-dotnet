module Fuaran.Samples.Themes.HighContrast

// ============================================================================
//  Sample theme — high-contrast / WCAG-AAA accessibility variant.
//
//  Maximises foreground / background contrast for users with low vision or
//  in glare-heavy environments. Every tone uses pure black foreground on a
//  near-white background (or pure white on black for `Default`) so contrast
//  ratios reach the WCAG-AAA threshold of 7:1 for normal text. Borders are
//  thicker (3px) and emphasised; radii are smaller (3px) so component
//  edges read crisply.
//
//  Note: this is an illustrative starting point — final WCAG-AAA conformance
//  always requires per-component contrast verification in the actual UI
//  context. See `https://www.w3.org/TR/WCAG22/#contrast-enhanced`.
//
//  Consume via `Render.renderWithTheme
//  Fuaran.Samples.Themes.HighContrast.theme sources dispatch node`.
// ============================================================================

open Fuaran.UI
open Fuaran.UI.Types

let theme: Theme =
    { Defaults.theme with
        Tones =
            { Default =
                { Background = ColorVar.Hex "#000000"
                  Foreground = ColorVar.Hex "#ffffff"
                  Border = ColorVar.Hex "#ffffff" }
              Subdued =
                { Background = ColorVar.Hex "#1a1a1a"
                  Foreground = ColorVar.Hex "#e6e6e6"
                  Border = ColorVar.Hex "#cccccc" }
              Brand =
                { Background = ColorVar.Hex "#ffffff"
                  Foreground = ColorVar.Hex "#000080"
                  Border = ColorVar.Hex "#000080" }
              Success =
                { Background = ColorVar.Hex "#ffffff"
                  Foreground = ColorVar.Hex "#006600"
                  Border = ColorVar.Hex "#006600" }
              Warning =
                { Background = ColorVar.Hex "#ffffff"
                  Foreground = ColorVar.Hex "#7a3a00"
                  Border = ColorVar.Hex "#7a3a00" }
              Critical =
                { Background = ColorVar.Hex "#ffffff"
                  Foreground = ColorVar.Hex "#990000"
                  Border = ColorVar.Hex "#990000" }
              Info =
                { Background = ColorVar.Hex "#ffffff"
                  Foreground = ColorVar.Hex "#003366"
                  Border = ColorVar.Hex "#003366" } }
        Radius =
            { Sm = "3px"
              Md = "3px"
              Lg = "3px"
              Full = "9999px" }
        BorderWidth = "3px" }
