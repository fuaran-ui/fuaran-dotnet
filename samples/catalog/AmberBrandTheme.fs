module Fuaran.Samples.Catalog.AmberBrandTheme

// ============================================================================
//  Catalog-side sample theme — amber-brand-shaped.
//
//  Second branded sample alongside TealBrandTheme. Amber-brand-shaped uses
//  an amber/orange brand accent + slightly heavier border radius — a
//  "warm professional services" palette. Demonstrates how a tenant rebrand
//  changes the visual feel of the whole component matrix without touching
//  any .fuaran-* class.
// ============================================================================

open Fuaran.UI
open Fuaran.UI.Types

let theme: Theme =
    { Defaults.theme with
        Tones =
            { Default = Defaults.tones.Default
              Subdued = Defaults.tones.Subdued
              Brand =
                { Background = ColorVar.Hex "#fffbeb"
                  Foreground = ColorVar.Hex "#b45309"
                  Border = ColorVar.Hex "#fcd34d" }
              Success = Defaults.tones.Success
              Warning = Defaults.tones.Warning
              Critical = Defaults.tones.Critical
              Info = Defaults.tones.Info }
        Radius =
            { Sm = "6px"
              Md = "10px"
              Lg = "14px"
              Full = "9999px" } }
