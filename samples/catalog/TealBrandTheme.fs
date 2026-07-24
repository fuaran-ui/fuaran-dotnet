module Fuaran.Samples.Catalog.TealBrandTheme

// ============================================================================
//  Catalog-side sample theme — teal-brand-shaped.
//
//  A second branded sample alongside the canonical Default / Dark /
//  HighContrast triplet. Teal-brand-shaped means: brand tone shifts to a
//  teal / cyan accent, success/warning/critical hues stay legible
//  against light surfaces. Used by the catalog's theme switcher to
//  demonstrate per-tenant rebranding via the typed Theme record.
//
//  This sample is catalog-local — it is not promoted into
//  `Fuaran/samples/themes/` because the canonical sibling samples
//  (Default, Dark, HighContrast) cover the audience for the
//  standalone-theme samples directory.
// ============================================================================

open Fuaran.UI
open Fuaran.UI.Types

let theme: Theme =
    { Defaults.theme with
        Tones =
            { Default = Defaults.tones.Default
              Subdued = Defaults.tones.Subdued
              Brand =
                { Background = ColorVar.Hex "#ecfeff"
                  Foreground = ColorVar.Hex "#0e7490"
                  Border = ColorVar.Hex "#67e8f9" }
              Success = Defaults.tones.Success
              Warning = Defaults.tones.Warning
              Critical = Defaults.tones.Critical
              Info =
                { Background = ColorVar.Hex "#f0fdfa"
                  Foreground = ColorVar.Hex "#0f766e"
                  Border = ColorVar.Hex "#5eead4" } } }
