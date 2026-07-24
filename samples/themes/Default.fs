module Fuaran.Samples.Themes.Default

// ============================================================================
//  Sample theme — default.
//
//  Mirrors `Fuaran.UI.Defaults.theme` byte-for-byte; serves as the canonical
//  starting point for new theme authoring. Copy this file, rename the
//  module, and tweak the values you need — the typed-record shape ensures
//  every CSS variable still resolves.
//
//  See `Fuaran/HOST-STYLING-CHECKLIST.md` for the full variable surface +
//  the consumer override paths.
// ============================================================================

open Fuaran.UI
open Fuaran.UI.Types

/// The default theme — identical to `Fuaran.UI.Defaults.theme`. Provided as
/// a sample so authors have a copy-and-customise starting point.
let theme: Theme = Defaults.theme
