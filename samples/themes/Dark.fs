module Fuaran.Samples.Themes.Dark

// ============================================================================
//  Sample theme — dark mode.
//
//  Dark-mode variant of `Fuaran.UI.Defaults.theme`. Inverts the surface
//  + foreground tones; keeps brand / success / warning / critical / info
//  hues in the same OKLCH neighbourhood but at lower lightness so
//  borders stay visible against dark backgrounds. Dimensions (spacing,
//  font scale, radius, button size, border width) are unchanged — the
//  density story is the same in either mode.
//
//  Consume via `Render.renderWithTheme Fuaran.Samples.Themes.Dark.theme
//  sources dispatch node` to mount this bundle at the renderer root.
// ============================================================================

open Fuaran.UI
open Fuaran.UI.Types

let theme: Theme =
    { Defaults.theme with
        Tones =
            { Default =
                { Background = ColorVar.Hex "#0f172a"
                  Foreground = ColorVar.Hex "#f1f5f9"
                  Border = ColorVar.Hex "#334155" }
              Subdued =
                { Background = ColorVar.Hex "#1e293b"
                  Foreground = ColorVar.Hex "#94a3b8"
                  Border = ColorVar.Hex "#475569" }
              Brand =
                { Background = ColorVar.Hex "#1e3a8a"
                  Foreground = ColorVar.Hex "#93c5fd"
                  Border = ColorVar.Hex "#3b82f6" }
              Success =
                { Background = ColorVar.Hex "#064e3b"
                  Foreground = ColorVar.Hex "#6ee7b7"
                  Border = ColorVar.Hex "#10b981" }
              Warning =
                { Background = ColorVar.Hex "#78350f"
                  Foreground = ColorVar.Hex "#fcd34d"
                  Border = ColorVar.Hex "#f59e0b" }
              Critical =
                { Background = ColorVar.Hex "#7f1d1d"
                  Foreground = ColorVar.Hex "#fca5a5"
                  Border = ColorVar.Hex "#ef4444" }
              Info =
                { Background = ColorVar.Hex "#1e3a8a"
                  Foreground = ColorVar.Hex "#93c5fd"
                  Border = ColorVar.Hex "#3b82f6" } } }
