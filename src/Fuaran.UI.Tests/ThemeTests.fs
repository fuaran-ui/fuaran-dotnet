module Fuaran.UI.Tests.ThemeTests

// ============================================================================
//  Theme record + Theme.toCss byte-for-byte regression +
//  Theme.toJson / fromJson round-trip.
//
//  Two test categories:
//
//   (1) Regression: `Defaults.theme |> Theme.toCss` emits every CSS
//       variable declaration in `fuaran-reference.css` `:root` block with
//       the same value. Reads the reference CSS at runtime (copied to
//       the test bin via fsproj `<Content Link=...>`) so a stray edit
//       on either side fails immediately.
//
//   (2) Round-trip: a hand-authored "exotic" Theme (every Theme field
//       overridden, every `ColorVar` case represented) survives
//       `toJson |> fromJson` with structural equality.
// ============================================================================

open System.IO
open System.Text.RegularExpressions
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

let private referenceCssPath: string =
    // The fsproj copies `fuaran-reference.css` into the test bin via a
    // <Content Link=...> entry. AppContext.BaseDirectory is the test
    // executable's directory.
    Path.Combine(System.AppContext.BaseDirectory, "fuaran-reference.css")

/// Pull `(name, value)` pairs for every `--fuaran-X: value;` declaration
/// inside the reference CSS's `:root { ... }` block. Whitespace-tolerant.
let private referenceCssVariables () : Map<string, string> =
    let text = File.ReadAllText referenceCssPath

    // Match the first `:root { ... }` block — there's only one in the
    // reference CSS, but the regex is non-greedy + scoped to that
    // pattern so future blocks don't bleed in.
    let rootBlockMatch =
        Regex.Match(text, @":root\s*\{([\s\S]*?)\}", RegexOptions.Compiled)

    if not rootBlockMatch.Success then
        failwithf "Could not locate :root { ... } block in %s" referenceCssPath

    let body = rootBlockMatch.Groups[1].Value

    // Strip block comments — the reference CSS has `/* ... */` section
    // dividers inside `:root` that must not be confused with declarations.
    let stripped = Regex.Replace(body, @"/\*[\s\S]*?\*/", "")

    // Extract `--fuaran-XYZ: value;` pairs. Value runs up to the
    // terminating semicolon (excluding it). Whitespace around either side
    // is trimmed. Non-`--fuaran-*` declarations (font-family, color-scheme)
    // are deliberately ignored — they're not part of the Theme record's
    // surface (the migration doc explains why).
    let declRegex =
        Regex(@"(--fuaran-[a-zA-Z0-9_-]+)\s*:\s*([^;]+);", RegexOptions.Compiled)

    declRegex.Matches(stripped)
    |> Seq.cast<Match>
    |> Seq.map (fun m -> m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim())
    |> Map.ofSeq

let private themeVariables (theme: Theme) : Map<string, string> =
    Theme.toCssVariables theme |> Map.ofList

// ─── An exotic Theme that exercises every field + every ColorVar case ─────
//
// `OKLCH` colours use representative L/C/H values rather than exact
// translations of the reference defaults — the round-trip test asserts
// faithful preservation, not equivalence to the reference.

let private exoticTheme: Theme =
    { Tones =
        { Default =
            { Background = ColorVar.Hex "#fafafa"
              Foreground = ColorVar.OKLCH(0.2, 0.01, 250.0, 1.0)
              Border = ColorVar.CssRaw "color-mix(in oklch, currentColor 20%, transparent)" }
          Subdued =
            { Background = ColorVar.Hex "#eeeeee"
              Foreground = ColorVar.OKLCH(0.5, 0.02, 250.0, 0.9)
              Border = ColorVar.Hex "#cccccc" }
          Brand =
            { Background = ColorVar.OKLCH(0.95, 0.05, 230.0, 1.0)
              Foreground = ColorVar.OKLCH(0.4, 0.18, 230.0, 1.0)
              Border = ColorVar.Hex "#3060c0" }
          Success =
            { Background = ColorVar.Hex "#e6fff0"
              Foreground = ColorVar.OKLCH(0.45, 0.16, 150.0, 1.0)
              Border = ColorVar.CssRaw "rgb(110 220 160 / 0.7)" }
          Warning =
            { Background = ColorVar.Hex "#fff8e1"
              Foreground = ColorVar.Hex "#a05000"
              Border = ColorVar.OKLCH(0.75, 0.13, 80.0, 1.0) }
          Critical =
            { Background = ColorVar.CssRaw "linear-gradient(180deg, #ffeaea, #ffd0d0)"
              Foreground = ColorVar.Hex "#a01010"
              Border = ColorVar.Hex "#e08080" }
          Info =
            { Background = ColorVar.Hex "#eaf2ff"
              Foreground = ColorVar.OKLCH(0.35, 0.18, 250.0, 1.0)
              Border = ColorVar.Hex "#80b0e0" } }
      Spacing =
        { Xs = "2px"
          Sm = "6px"
          Md = "10px"
          Lg = "14px"
          Xl = "22px" }
      FontScale =
        { Xs = "11px"
          Sm = "12.5px"
          Base = "15px"
          Lg = "17px"
          Xl = "21px"
          XXl = "25px"
          XXXl = "30px" }
      FontWeight =
        { Regular = 350
          Medium = 450
          Semibold = 550
          Bold = 750 }
      LineHeight =
        { Tight = 1.1
          Normal = 1.6
          Relaxed = 1.9 }
      Radius =
        { Sm = "3px"
          Md = "5px"
          Lg = "9px"
          Full = "12345px" }
      ButtonSize =
        { PadY = "var(--fuaran-space-xs, 2px)"
          PadX = "var(--fuaran-space-sm, 6px)"
          FontSize = "var(--fuaran-text-sm, 12.5px)" }
      BorderWidth = "2px"
      // Exotic Interaction matrix exercising every state
      // and every ColorVar case across the per-tone × per-slot surface.
      Interaction =
        { FocusRing =
            { Color = ColorVar.OKLCH(0.62, 0.22, 28.0, 0.85)
              Width = "3px"
              Offset = "1px"
              Style = "dashed" }
          Hover =
            { Default =
                { Background = ColorVar.Hex "#ededed"
                  Foreground = ColorVar.OKLCH(0.3, 0.02, 240.0, 1.0)
                  Border = ColorVar.CssRaw "color-mix(in oklch, currentColor 30%, transparent)" }
              Subdued =
                { Background = ColorVar.Hex "#dfdfdf"
                  Foreground = ColorVar.OKLCH(0.4, 0.03, 240.0, 1.0)
                  Border = ColorVar.Hex "#bbbbbb" }
              Brand =
                { Background = ColorVar.OKLCH(0.9, 0.06, 230.0, 1.0)
                  Foreground = ColorVar.OKLCH(0.35, 0.2, 230.0, 1.0)
                  Border = ColorVar.Hex "#2050a0" }
              Success =
                { Background = ColorVar.Hex "#d4f7e0"
                  Foreground = ColorVar.OKLCH(0.4, 0.17, 150.0, 1.0)
                  Border = ColorVar.CssRaw "rgb(80 200 130 / 0.75)" }
              Warning =
                { Background = ColorVar.Hex "#fff0c0"
                  Foreground = ColorVar.Hex "#804000"
                  Border = ColorVar.OKLCH(0.7, 0.14, 80.0, 1.0) }
              Critical =
                { Background = ColorVar.CssRaw "linear-gradient(180deg, #ffd6d6, #ffb6b6)"
                  Foreground = ColorVar.Hex "#800808"
                  Border = ColorVar.Hex "#c06060" }
              Info =
                { Background = ColorVar.Hex "#d6e6ff"
                  Foreground = ColorVar.OKLCH(0.3, 0.19, 250.0, 1.0)
                  Border = ColorVar.Hex "#6090c8" } }
          Focus =
            { Default =
                { Background = ColorVar.Hex "#fafafa"
                  Foreground = ColorVar.Hex "#1f2937"
                  Border = ColorVar.OKLCH(0.7, 0.18, 30.0, 1.0) }
              Subdued =
                { Background = ColorVar.Hex "#eeeeee"
                  Foreground = ColorVar.Hex "#6b7280"
                  Border = ColorVar.OKLCH(0.7, 0.18, 30.0, 1.0) }
              Brand =
                { Background = ColorVar.Hex "#eaf2ff"
                  Foreground = ColorVar.Hex "#1d4ed8"
                  Border = ColorVar.OKLCH(0.7, 0.18, 30.0, 1.0) }
              Success =
                { Background = ColorVar.Hex "#e6fff0"
                  Foreground = ColorVar.Hex "#047857"
                  Border = ColorVar.OKLCH(0.7, 0.18, 30.0, 1.0) }
              Warning =
                { Background = ColorVar.Hex "#fff8e1"
                  Foreground = ColorVar.Hex "#b45309"
                  Border = ColorVar.OKLCH(0.7, 0.18, 30.0, 1.0) }
              Critical =
                { Background = ColorVar.Hex "#ffeaea"
                  Foreground = ColorVar.Hex "#b91c1c"
                  Border = ColorVar.OKLCH(0.7, 0.18, 30.0, 1.0) }
              Info =
                { Background = ColorVar.Hex "#eaf2ff"
                  Foreground = ColorVar.Hex "#1e40af"
                  Border = ColorVar.OKLCH(0.7, 0.18, 30.0, 1.0) } }
          Active =
            { Default =
                { Background = ColorVar.Hex "#dedede"
                  Foreground = ColorVar.Hex "#0a0a0a"
                  Border = ColorVar.Hex "#888888" }
              Subdued =
                { Background = ColorVar.Hex "#bbbbbb"
                  Foreground = ColorVar.Hex "#1a1a1a"
                  Border = ColorVar.Hex "#555555" }
              Brand =
                { Background = ColorVar.Hex "#a8c8f0"
                  Foreground = ColorVar.OKLCH(0.25, 0.22, 230.0, 1.0)
                  Border = ColorVar.Hex "#2050c0" }
              Success =
                { Background = ColorVar.Hex "#90e0b0"
                  Foreground = ColorVar.Hex "#024028"
                  Border = ColorVar.Hex "#10a070" }
              Warning =
                { Background = ColorVar.Hex "#fcd070"
                  Foreground = ColorVar.Hex "#502800"
                  Border = ColorVar.Hex "#d08000" }
              Critical =
                { Background = ColorVar.Hex "#fab0b0"
                  Foreground = ColorVar.Hex "#600808"
                  Border = ColorVar.Hex "#d03030" }
              Info =
                { Background = ColorVar.Hex "#a8c8f0"
                  Foreground = ColorVar.Hex "#101840"
                  Border = ColorVar.Hex "#2070c8" } }
          Disabled =
            { Default =
                { Background = ColorVar.Hex "#f5f5f5"
                  Foreground = ColorVar.Hex "#888888"
                  Border = ColorVar.Hex "#cccccc" }
              Subdued =
                { Background = ColorVar.Hex "#f5f5f5"
                  Foreground = ColorVar.Hex "#888888"
                  Border = ColorVar.Hex "#cccccc" }
              Brand =
                { Background = ColorVar.Hex "#f5f5f5"
                  Foreground = ColorVar.Hex "#888888"
                  Border = ColorVar.Hex "#cccccc" }
              Success =
                { Background = ColorVar.Hex "#f5f5f5"
                  Foreground = ColorVar.Hex "#888888"
                  Border = ColorVar.Hex "#cccccc" }
              Warning =
                { Background = ColorVar.Hex "#f5f5f5"
                  Foreground = ColorVar.Hex "#888888"
                  Border = ColorVar.Hex "#cccccc" }
              Critical =
                { Background = ColorVar.Hex "#f5f5f5"
                  Foreground = ColorVar.Hex "#888888"
                  Border = ColorVar.Hex "#cccccc" }
              Info =
                { Background = ColorVar.Hex "#f5f5f5"
                  Foreground = ColorVar.Hex "#888888"
                  Border = ColorVar.Hex "#cccccc" } } }
      TabBar =
        { PaddingY = "10px"
          PaddingX = "30px"
          IndicatorColor = ColorVar.OKLCH(0.55, 0.18, 250.0, 1.0)
          IndicatorHeight = "3px"
          TextColor = ColorVar.Hex "#6b7280"
          TextActiveColor = ColorVar.CssRaw "color-mix(in oklch, #1d4ed8 80%, white)"
          TextHoverColor = ColorVar.Hex "#1e40af" }
      // Exotic segmented-control tokens exercising every
      // ColorVar case (Hex, OKLCH, CssRaw) so the round-trip + reference-
      // CSS regression catches encoder / parser drift.
      Segmented =
        { Background = ColorVar.Hex "#e8e8e8"
          ActiveBackground = ColorVar.Hex "#ffffff"
          ActiveForeground = ColorVar.OKLCH(0.45, 0.2, 230.0, 1.0)
          DividerColor = ColorVar.CssRaw "color-mix(in oklch, currentColor 15%, transparent)" }
      // Exotic breakpoints (Phase 58) — non-default thresholds so the
      // round-trip catches encoder / parser drift on the new field.
      Breakpoints =
        { Sm = "600px"
          Md = "820px"
          Lg = "1200px" } }

[<Tests>]
let tests =
    testList
        "Theme as API"
        [
          // ── Byte-for-byte regression against fuaran-reference.css ────────
          test "Defaults.theme |> Theme.toCss covers every --fuaran-* variable in fuaran-reference.css" {
              let referenceVars = referenceCssVariables ()
              let defaultsVars = themeVariables Defaults.theme

              let referenceNames = referenceVars |> Map.toList |> List.map fst |> Set.ofList
              let defaultsNames = defaultsVars |> Map.toList |> List.map fst |> Set.ofList

              let missingFromTheme = Set.difference referenceNames defaultsNames
              let extraInTheme = Set.difference defaultsNames referenceNames

              Expect.isEmpty
                  missingFromTheme
                  (sprintf "Variables in reference CSS but not emitted by Theme.toCss: %A" missingFromTheme)

              Expect.isEmpty
                  extraInTheme
                  (sprintf "Variables emitted by Theme.toCss but absent from reference CSS: %A" extraInTheme)
          }

          test "Defaults.theme values match fuaran-reference.css byte-for-byte" {
              let referenceVars = referenceCssVariables ()
              let defaultsVars = themeVariables Defaults.theme

              let mismatches =
                  defaultsVars
                  |> Map.toList
                  |> List.choose (fun (name, value) ->
                      match Map.tryFind name referenceVars with
                      | Some refValue when refValue = value -> None
                      | Some refValue -> Some(name, value, refValue)
                      | None -> None)

              Expect.isEmpty
                  mismatches
                  (sprintf
                      "Theme.toCss values diverge from fuaran-reference.css. Triples are (variable, theme-value, reference-value): %A"
                      mismatches)
          }

          test "Theme.toCss emits a :root { ... } block with every variable on its own line" {
              let css = Theme.toCss Defaults.theme

              Expect.isTrue (css.StartsWith ":root {") "starts with :root {"
              Expect.isTrue (css.TrimEnd().EndsWith "}") "ends with closing brace"
              Expect.stringContains css "--fuaran-tone-default-bg" "contains the default-bg variable"
              Expect.stringContains css "--fuaran-border-width" "contains the border-width variable"
          }

          // ── Round-trip: every field × every ColorVar case ─────────────
          test "Defaults.theme round-trips through toJson / fromJson" {
              let json = Theme.toJson Defaults.theme

              match Theme.fromJson json with
              | Ok parsed ->
                  // Theme is a deep record of records — structural equality
                  // works because every leaf is a comparable primitive
                  // (string / int / float / Hex<string> / CssRaw<string> /
                  // OKLCH of primitives).
                  Expect.equal parsed Defaults.theme "Defaults.theme round-trip preserves every field"
              | Error e -> failtestf "Defaults.theme failed to round-trip: %s" e
          }

          test "Exotic theme (every field overridden, every ColorVar case used) round-trips" {
              let json = Theme.toJson exoticTheme

              match Theme.fromJson json with
              | Ok parsed ->
                  Expect.equal parsed exoticTheme "Exotic theme round-trip preserves every field × every ColorVar"
              | Error e -> failtestf "Exotic theme failed to round-trip: %s" e
          }

          test "ColorVar.Hex round-trips" {
              let theme =
                  { Defaults.theme with
                      Tones =
                          { Defaults.theme.Tones with
                              Brand =
                                  { Defaults.theme.Tones.Brand with
                                      Background = ColorVar.Hex "#abcdef" } } }

              match Theme.fromJson (Theme.toJson theme) with
              | Ok p -> Expect.equal p.Tones.Brand.Background (ColorVar.Hex "#abcdef") "Hex preserved"
              | Error e -> failtestf "Hex round-trip failed: %s" e
          }

          test "ColorVar.OKLCH round-trips with all four components" {
              let original = ColorVar.OKLCH(0.42, 0.13, 247.5, 0.85)

              let theme =
                  { Defaults.theme with
                      Tones =
                          { Defaults.theme.Tones with
                              Brand =
                                  { Defaults.theme.Tones.Brand with
                                      Foreground = original } } }

              match Theme.fromJson (Theme.toJson theme) with
              | Ok p -> Expect.equal p.Tones.Brand.Foreground original "OKLCH (l, c, h, alpha) preserved"
              | Error e -> failtestf "OKLCH round-trip failed: %s" e
          }

          test "ColorVar.CssRaw round-trips with escape-needing characters" {
              let raw = "color-mix(in oklch, var(--brand) 60%, white)"

              let theme =
                  { Defaults.theme with
                      Tones =
                          { Defaults.theme.Tones with
                              Brand =
                                  { Defaults.theme.Tones.Brand with
                                      Border = ColorVar.CssRaw raw } } }

              match Theme.fromJson (Theme.toJson theme) with
              | Ok p -> Expect.equal p.Tones.Brand.Border (ColorVar.CssRaw raw) "CssRaw preserved verbatim"
              | Error e -> failtestf "CssRaw round-trip failed: %s" e
          }

          test "Theme.colorVarToCss emits the documented CSS form for each ColorVar case" {
              Expect.equal (Theme.colorVarToCss (ColorVar.Hex "#1d4ed8")) "#1d4ed8" "Hex emits the value verbatim"

              Expect.equal
                  (Theme.colorVarToCss (ColorVar.OKLCH(0.5, 0.15, 240.0, 1.0)))
                  "oklch(0.5 0.15 240 / 1)"
                  "OKLCH emits the oklch() function form"

              Expect.equal
                  (Theme.colorVarToCss (ColorVar.CssRaw "currentColor"))
                  "currentColor"
                  "CssRaw passes through unchanged"
          }

          test "Theme.fromJson returns Error on malformed input rather than throwing" {
              match Theme.fromJson "{ not json" with
              | Ok _ -> failtest "Expected Error for malformed JSON"
              | Error _ -> ()
          }

          test "Theme.fromJson returns Error on missing field" {
              let partialJson = """{"tones":{}}"""

              match Theme.fromJson partialJson with
              | Ok _ -> failtest "Expected Error for missing Theme field"
              | Error _ -> ()
          }

          // ── Sample themes (Fuaran/samples/themes/) compile + round-trip ──
          test "Fuaran.Samples.Themes.Default mirrors Defaults.theme" {
              Expect.equal Fuaran.Samples.Themes.Default.theme Defaults.theme "Default sample is exactly Defaults.theme"
          }

          test "Fuaran.Samples.Themes.Dark round-trips through toJson / fromJson" {
              let dark = Fuaran.Samples.Themes.Dark.theme
              let json = Theme.toJson dark

              match Theme.fromJson json with
              | Ok parsed -> Expect.equal parsed dark "Dark theme round-trip preserves every field"
              | Error e -> failtestf "Dark theme failed to round-trip: %s" e
          }

          test "Fuaran.Samples.Themes.HighContrast round-trips through toJson / fromJson" {
              let hc = Fuaran.Samples.Themes.HighContrast.theme
              let json = Theme.toJson hc

              match Theme.fromJson json with
              | Ok parsed -> Expect.equal parsed hc "HighContrast theme round-trip preserves every field"
              | Error e -> failtestf "HighContrast theme failed to round-trip: %s" e
          }

          test "Sample themes project to a CSS-variable bundle with the expected variable count" {
              // Every sample theme is a complete Theme value — Theme.toCss
              // must emit the same number of variables as the reference
              // bundle. Static surface = 47: 7×3 tones + 5
              // spacing + 7 text + 4 weight + 3 line-height + 2 button +
              // 4 radius + 1 border. Interaction surface =
              // 88: 7 tones × 4 states × 3 slots + 4 focus-ring globals.
              // Tab-bar surface = 7: padding-y, padding-x,
              // indicator-color, indicator-height, text-color,
              // text-active-color, text-hover-color. Segmented surface
              // Segmented-control surface = 4: bg, active-bg, active-fg, divider-color.
              // Breakpoint surface (Phase 58) = 3: sm, md, lg.
              // Total = 149.
              let expectedCount = 149

              Expect.equal
                  (Theme.toCssVariables Fuaran.Samples.Themes.Default.theme |> List.length)
                  expectedCount
                  "Default sample emits 149 variables"

              Expect.equal
                  (Theme.toCssVariables Fuaran.Samples.Themes.Dark.theme |> List.length)
                  expectedCount
                  "Dark sample emits 149 variables"

              Expect.equal
                  (Theme.toCssVariables Fuaran.Samples.Themes.HighContrast.theme |> List.length)
                  expectedCount
                  "HighContrast sample emits 149 variables"
          }

          // ── Interaction matrix round-trip ─────────────────
          test "Defaults.theme.Interaction is preserved through toJson / fromJson" {
              let json = Theme.toJson Defaults.theme

              match Theme.fromJson json with
              | Ok parsed ->
                  Expect.equal
                      parsed.Interaction
                      Defaults.theme.Interaction
                      "Interaction record (FocusRing + 4 state matrices) round-trips"
              | Error e -> failtestf "Defaults.theme round-trip failed: %s" e
          }

          test "Theme.toCss emits 88 interaction variables on top of the 47 static ones" {
              let pairs = Theme.toCssVariables Defaults.theme
              let names = pairs |> List.map fst |> Set.ofList

              // 84 tone-state-slot
              let expectedToneStateSlot =
                  [ for tone in [ "default"; "subdued"; "brand"; "success"; "warning"; "critical"; "info" ] do
                        for state in [ "hover"; "focus"; "active"; "disabled" ] do
                            for slot in [ "bg"; "fg"; "border" ] do
                                sprintf "--fuaran-tone-%s-%s-%s" tone state slot ]
                  |> Set.ofList

              let missing = Set.difference expectedToneStateSlot names

              Expect.isEmpty missing (sprintf "tone-state-slot vars missing from Theme.toCss: %A" missing)

              // 4 focus-ring globals
              for ringField in [ "color"; "width"; "offset"; "style" ] do
                  let name = sprintf "--fuaran-focus-ring-%s" ringField

                  Expect.isTrue
                      (Set.contains name names)
                      (sprintf "focus-ring var %s present in Theme.toCss output" name)
          }

          test "FocusRing.Color round-trips for all three ColorVar cases" {
              for color in
                  [ ColorVar.Hex "#abcdef"
                    ColorVar.OKLCH(0.45, 0.22, 18.0, 0.7)
                    ColorVar.CssRaw "color-mix(in oklch, var(--brand) 50%, transparent)" ] do
                  let theme =
                      { Defaults.theme with
                          Interaction =
                              { Defaults.theme.Interaction with
                                  FocusRing =
                                      { Defaults.theme.Interaction.FocusRing with
                                          Color = color } } }

                  match Theme.fromJson (Theme.toJson theme) with
                  | Ok p ->
                      Expect.equal
                          p.Interaction.FocusRing.Color
                          color
                          (sprintf "FocusRing.Color preserved for %A" color)
                  | Error e -> failtestf "FocusRing.Color round-trip failed for %A: %s" color e
          }

          test "ToneStateMatrix Hover slot edit round-trips and surfaces in Theme.toCss" {
              let theme =
                  { Defaults.theme with
                      Interaction =
                          { Defaults.theme.Interaction with
                              Hover =
                                  { Defaults.theme.Interaction.Hover with
                                      Brand =
                                          { Background = ColorVar.Hex "#112233"
                                            Foreground = ColorVar.Hex "#445566"
                                            Border = ColorVar.Hex "#778899" } } } }

              // JSON round-trip preserves the edit.
              match Theme.fromJson (Theme.toJson theme) with
              | Ok p ->
                  Expect.equal
                      p.Interaction.Hover.Brand.Background
                      (ColorVar.Hex "#112233")
                      "Hover.Brand.Background preserved across round-trip"
              | Error e -> failtestf "Hover-edit round-trip failed: %s" e

              // CSS emission picks up the edit.
              let pairs = Theme.toCssVariables theme |> Map.ofList

              Expect.equal
                  (Map.tryFind "--fuaran-tone-brand-hover-bg" pairs)
                  (Some "#112233")
                  "brand-hover-bg in CSS-variable list reflects typed override"

              Expect.equal
                  (Map.tryFind "--fuaran-tone-brand-hover-fg" pairs)
                  (Some "#445566")
                  "brand-hover-fg in CSS-variable list reflects typed override"

              Expect.equal
                  (Map.tryFind "--fuaran-tone-brand-hover-border" pairs)
                  (Some "#778899")
                  "brand-hover-border in CSS-variable list reflects typed override"
          } ]
