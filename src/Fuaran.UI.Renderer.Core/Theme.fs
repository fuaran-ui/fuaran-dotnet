module Fuaran.UI.Renderer.Theme

// ============================================================================
//  Fuaran — semantic theme engine (§4b SemanticStyle, §4c lines 504–542)
//
//  The renderer never emits Tailwind classes or pixel-CSS strings. Authors
//  declare intent (`Tone.Brand` / `Weight.Compact` / `Emphasis.Loud`); the
//  theme function maps that to CSS-variable bindings on the rendered element.
//
//  Why CSS variables (not className tokens)?  AI-emit shape stability: a
//  variable name is a stable contract regardless of which palette / dark-mode
//  / vertical-pack theme is active in the consuming app.  Consumer apps
//  override the variables at the app shell layer; Fuaran emits the same shape
//  every time.  Same goal as a typical app's `--color-sidebar` / `--color-brand`
//  approach — semantic tokens beat hard-coded colour values.
//
//  Session 3a ships the mapping table.  Wiring the CSS variables into an
//  actual stylesheet is consumer-side work (consumer app shells provide
//  the palette); session 3b ships a reference theme stylesheet alongside
//  the demo project.
// ============================================================================

open Fuaran.UI.Types

/// parity: format a float invariantly across both pipelines. The .NET branch
/// pins InvariantCulture so a comma-decimal locale can't corrupt the CSS/JSON;
/// the Fable branch uses `string`, which JS renders culture-invariantly already.
/// Both yield shortest-round-trippable output, so emitted CSS/JSON is identical
/// to the prior per-site `ToString(InvariantCulture)` calls.
let inline private invariantFloat (v: float) : string =
#if FABLE_COMPILER
    string v
#else
    v.ToString(System.Globalization.CultureInfo.InvariantCulture)
#endif

/// Sanitise an author-supplied (or AI-emitted) string for safe use as a
/// CSS class-name fragment.  `NodeKind.Custom` carries `moduleId` and
/// `componentId` as unconstrained strings; spaces, quotes, or other
/// non-identifier characters would split a single fragment into multiple
/// classes when interpolated into `className`.  Replace anything outside
/// `[a-zA-Z0-9_-]` with `-`.  Fable-compatible — `System.Text.RegularExpressions`
/// is supported on both pipelines.
let private sanitiseClassFragment (raw: string) : string =
    System.Text.RegularExpressions.Regex.Replace(raw, "[^a-zA-Z0-9_-]", "-")

/// Map a `ToneVariant` to its CSS-variable name root.  Consumers wire
/// `--fuaran-tone-brand-bg` / `--fuaran-tone-brand-fg` / etc. in their stylesheet.
let toneVar (tone: ToneVariant) : string =
    match tone with
    | ToneVariant.Default -> "default"
    | ToneVariant.Subdued -> "subdued"
    | ToneVariant.Brand -> "brand"
    | ToneVariant.Success -> "success"
    | ToneVariant.Warning -> "warning"
    | ToneVariant.Critical -> "critical"
    | ToneVariant.Info -> "info"

/// Map a `StyleWeight` to its CSS-variable name root.  Affects padding,
/// font scale, density — not colour.
let weightVar (weight: StyleWeight) : string =
    match weight with
    | StyleWeight.Compact -> "compact"
    | StyleWeight.Standard -> "standard"
    | StyleWeight.Spacious -> "spacious"

/// Map an `Emphasis` to its CSS-variable name root.  Affects visual
/// prominence — borders, shadows, font weight — not colour or scale.
let emphasisVar (emphasis: Emphasis) : string =
    match emphasis with
    | Emphasis.Quiet -> "quiet"
    | Emphasis.Normal -> "normal"
    | Emphasis.Loud -> "loud"

/// Map a `Motion` token to its `fuaran-motion-{suffix}` class
/// suffix. Parallel to `toneVar`; the renderer composes
/// `fuaran-motion-{suffix}` and emits it on the outer wrapper when
/// `Node.Motion` is `Some token`. The reference CSS ships keyframes for
/// `pulse-during-load` / `fade-in-on-mount` / `shake-on-error` /
/// `slide-in-from-right`; the remaining four (`slide-in-from-below` /
/// `rotate-on-refresh` / `expand-collapse` / `none`) are no-op class
/// hooks the consumer can override.
let motionVar (motion: Motion) : string =
    match motion with
    | Motion.None -> "none"
    | Motion.PulseDuringLoad -> "pulse-during-load"
    | Motion.FadeInOnMount -> "fade-in-on-mount"
    | Motion.SlideInFromBelow -> "slide-in-from-below"
    | Motion.ShakeOnError -> "shake-on-error"
    | Motion.RotateOnRefresh -> "rotate-on-refresh"
    | Motion.SlideInFromRight -> "slide-in-from-right"
    | Motion.ExpandCollapse -> "expand-collapse"

/// Map a `StyleRole` to its `fuaran-role-{suffix}` class suffix (Phase 147).
/// `StyleRole.None` returns `None` — the default emits no fragment, so an
/// existing tree renders byte-identically.  The host CSS owns the pixels
/// (class-name-only, no inline style).
let styleRoleVar (role: StyleRole) : string option =
    match role with
    | StyleRole.None -> None
    | StyleRole.Eyebrow -> Some "eyebrow"
    | StyleRole.Data -> Some "data"
    | StyleRole.Lede -> Some "lede"
    | StyleRole.Caption -> Some "caption"

/// Map a `FontVoice` to its `fuaran-voice-{suffix}` class suffix (Phase 147).
/// `FontVoice.Default` returns `None` (no fragment, byte-identical default).
let fontVoiceVar (voice: FontVoice) : string option =
    match voice with
    | FontVoice.Default -> None
    | FontVoice.Display -> Some "display"
    | FontVoice.Structural -> Some "structural"

/// Compose a `SemanticStyle` into the BEM-style className the renderer
/// attaches to every Fuaran-rendered element.  Stable shape regardless of
/// component kind; downstream CSS hooks against `.fuaran-tone-brand` etc.
/// The Phase 147 `role`/`voice` fragments are appended only when non-default,
/// so a tree authored before those fields existed yields the identical class
/// string.
let className (style: SemanticStyle) : string =
    // Per-node hot path: string concat, not `sprintf` (which re-parses the format
    // string at runtime on every node every render under Fable). The default-style
    // case — no Role, no Voice, the overwhelming majority — returns the base class
    // directly, skipping the option-list / `List.choose` / `String.concat`. This is
    // a perf primitive: do NOT "simplify" it back to `sprintf` or a fragment list.
    let baseClass =
        "fuaran-node fuaran-tone-"
        + toneVar style.Tone
        + " fuaran-weight-"
        + weightVar style.Weight
        + " fuaran-emphasis-"
        + emphasisVar style.Emphasis

    match styleRoleVar style.Role, fontVoiceVar style.Voice with
    | None, None -> baseClass
    | role, voice ->
        let sb = System.Text.StringBuilder(baseClass)

        match role with
        | Some r -> sb.Append(" fuaran-role-").Append(r) |> ignore
        | None -> ()

        match voice with
        | Some v -> sb.Append(" fuaran-voice-").Append(v) |> ignore
        | None -> ()

        sb.ToString()

/// Per-`NodeKind` class fragment so CSS can hook against the kind (e.g.
/// `.fuaran-kind-metric { display: grid; ... }`).  The kind tag matches the
/// §4d JSON wire format's `"kind"` string for end-to-end consistency.
let kindClass (kind: NodeKind<'Msg>) : string =
    match kind with
    // Phase 390 — the unified `Box` derives its legacy kind-class hook from
    // role + layout mode, so the reference CSS (`.fuaran-kind-stack` /
    // `-grid-layout` / `-dashboard` / `-card`) is unchanged and rendered output
    // stays byte-identical across the merge.
    | NodeKind.Box(spec) ->
        match spec.Role, spec.Layout with
        | BoxRole.Dashboard, _ -> "fuaran-kind-dashboard"
        | BoxRole.Card, _ -> "fuaran-kind-card"
        | BoxRole.Separator, _ -> "fuaran-kind-divider"
        | BoxRole.Group, BoxLayout.Grid _ -> "fuaran-kind-grid-layout"
        | BoxRole.Group, (BoxLayout.Flex _ | BoxLayout.Auto) -> "fuaran-kind-stack"
    | NodeKind.SplitPanel(_) -> "fuaran-kind-split-panel"
    | NodeKind.Tabs(_) -> "fuaran-kind-tabs"
    | NodeKind.Stepper(_) -> "fuaran-kind-stepper"
    | NodeKind.SummaryList(_) -> "fuaran-kind-summary-list"
    | NodeKind.Disclosure(_) -> "fuaran-kind-disclosure"
    | NodeKind.Modal(_) -> "fuaran-kind-modal"
    | NodeKind.ScrollArea(_) -> "fuaran-kind-scroll-area"
    | NodeKind.Heading(_) -> "fuaran-kind-heading"
    | NodeKind.LabelValueRow(_) -> "fuaran-kind-label-value-row"
    | NodeKind.Fact(_) -> "fuaran-kind-fact"
    | NodeKind.Link(_) -> "fuaran-kind-link"
    | NodeKind.Markdown(_) -> "fuaran-kind-markdown"
    | NodeKind.Metric(_) -> "fuaran-kind-metric"
    | NodeKind.Badge(_) -> "fuaran-kind-badge"
    | NodeKind.Sparkline(_) -> "fuaran-kind-sparkline"
    | NodeKind.Callout(_) -> "fuaran-kind-callout"
    | NodeKind.Progress(_) -> "fuaran-kind-progress"
    | NodeKind.Skeleton(_) -> "fuaran-kind-skeleton"
    | NodeKind.Image(_) -> "fuaran-kind-image"
    | NodeKind.List(_) -> "fuaran-kind-list"
    | NodeKind.Toast(_) -> "fuaran-kind-toast"
    | NodeKind.CodeBlock(_) -> "fuaran-kind-code-block"
    | NodeKind.Math(_) -> "fuaran-kind-math"
    | NodeKind.Drawing(_) -> "fuaran-kind-drawing"
    | NodeKind.ErrorBoundary _ ->
        // Bare class hook for the boundary's
        // outer wrapper. The catch-and-fallback decision happens at
        // render time, not in CSS; this hook is here so consumers who
        // want to visually mark "this section may degrade" sections in
        // dev have a target.
        "fuaran-kind-error-boundary"
    | NodeKind.Switch _ ->
        // State-bound conditional child (Phase 392). A control-flow primitive
        // that emits only the matched case's wrapper — the hook is a dev marker
        // (no reference-CSS rule of its own, same posture as error-boundary).
        "fuaran-kind-switch"
    | NodeKind.Form(_) -> "fuaran-kind-form"
    | NodeKind.Filters(_) -> "fuaran-kind-filters"
    | NodeKind.Button(_) -> "fuaran-kind-button"
    | NodeKind.FileUpload(_) -> "fuaran-kind-file-upload"
    | NodeKind.Select(_) -> "fuaran-kind-select"
    | NodeKind.DataGrid(_) -> "fuaran-kind-grid"
    | NodeKind.Chart(_) -> "fuaran-kind-chart"
    | NodeKind.Map(_) -> "fuaran-kind-map"
    | NodeKind.Custom(moduleId, componentId, _, _, _) ->
        sprintf
            "fuaran-kind-custom fuaran-custom-%s-%s"
            (sanitiseClassFragment moduleId)
            (sanitiseClassFragment componentId)
    | NodeKind.FragmentDecl _ ->
        // The decl itself renders nothing visible
        // (the body is the *template*); the class hook lets consumers
        // mark "fragment template here" in dev tools without affecting
        // layout. Renderer treats this kind as zero-paint.
        "fuaran-kind-fragment-decl"
    | NodeKind.FragmentRef _ ->
        // The ref's outer wrapper carries a marker class so
        // consumer-side selectors / dev tooling can pick out "this is
        // an expanded fragment" sites without having to traverse the
        // expanded body's structural classes.
        "fuaran-kind-fragment-ref"
    | NodeKind.Mount _ ->
        // Isolation/embedding boundary (§4o). The mount's outer wrapper
        // carries a marker class so consumer-side selectors / dev tooling can
        // pick out guest-boundary sites; the guest's own rendered subtree
        // carries its own scoped classes inside the boundary.
        "fuaran-kind-mount"

/// Compose the full className for a node: kind class + semantic style.
let nodeClassName (kind: NodeKind<'Msg>) (style: SemanticStyle) : string =
    // Per-node hot path — string concat, not `sprintf`. Do not "simplify".
    kindClass kind + " " + className style

// ─── Theme — typed CSS-variable bundle ─────────────────────────────────────
//
// `Theme.toCss` projects a `Theme` record to a `:root { --fuaran-...: ...; ... }`
// block. The renderer attaches the emitted block to a `<style>` element it
// maintains so apps can swap Themes by re-rendering with a different value.
//
// `Theme.toJson` / `Theme.fromJson` use a hand-rolled emitter + parser
// (Fable.SimpleJson's emitter is Fable-only — it imports JS' native
// `JSON.stringify` via JsInterop — and its parser fails on .NET via the
// same JsInterop path on its `$Parser` initialiser). The Theme JSON
// shape is small + well-known (strings, numbers, objects only — no
// arrays / bools / nulls), so a 60-line parser handles it on both
// pipelines without introducing a Fable-only dependency.
//
// `Defaults.theme` is the byte-for-byte mirror of the
// `fuaran-reference.css` `:root` block — the regression test in
// `ThemeTests.fs` reads the reference CSS at runtime and asserts every
// `--fuaran-X: value;` line emitted by `Theme.toCss Defaults.theme` appears
// in the reference's `:root` block, and vice versa.

/// Project a `ColorVar` to its CSS value string.
let colorVarToCss (color: ColorVar) : string =
    match color with
    | ColorVar.Hex value -> value
    | ColorVar.OKLCH(l, c, h, alpha) ->
        let lStr = invariantFloat l
        let cStr = invariantFloat c
        let hStr = invariantFloat h
        let aStr = invariantFloat alpha
        sprintf "oklch(%s %s %s / %s)" lStr cStr hStr aStr
    | ColorVar.CssRaw raw -> raw

/// The 7 ToneVariant cases in stable order matching the
/// reference CSS layout (`HOST-STYLING-CHECKLIST.md` §1.1).
let private toneStopsInOrder (tones: Tones) : (string * ToneStops) list =
    [ "default", tones.Default
      "subdued", tones.Subdued
      "brand", tones.Brand
      "success", tones.Success
      "warning", tones.Warning
      "critical", tones.Critical
      "info", tones.Info ]

/// Same stable tone order as [[toneStopsInOrder]], applied to a state
/// matrix. 7 tones × 3 slots per state → 21 vars per state.
let private toneStateStopsInOrder (matrix: ToneStateMatrix) : (string * ToneStops) list =
    [ "default", matrix.Default
      "subdued", matrix.Subdued
      "brand", matrix.Brand
      "success", matrix.Success
      "warning", matrix.Warning
      "critical", matrix.Critical
      "info", matrix.Info ]

/// The 4 interaction states in stable order matching the reference CSS
/// layout (HOST-STYLING-CHECKLIST.md §1.6).
let private interactionStatesInOrder (interaction: Interaction) : (string * ToneStateMatrix) list =
    [ "hover", interaction.Hover
      "focus", interaction.Focus
      "active", interaction.Active
      "disabled", interaction.Disabled ]

/// Format a unitless float as the reference CSS does — invariant culture,
/// no trailing zero past the meaningful precision (`1.5` not `1.50`).
let private floatCss (value: float) : string = invariantFloat value

/// Emit the `(name, value)` pairs for every CSS variable a `Theme` projects.
/// Exposed as a separate helper so the test suite can diff against the
/// reference CSS's parsed pairs without re-parsing `Theme.toCss`'s output.
let toCssVariables (theme: Theme) : (string * string) list =
    [
      // Tone palette (7 × 3 = 21 vars)
      for toneName, stops in toneStopsInOrder theme.Tones do
          sprintf "--fuaran-tone-%s-bg" toneName, colorVarToCss stops.Background
          sprintf "--fuaran-tone-%s-fg" toneName, colorVarToCss stops.Foreground
          sprintf "--fuaran-tone-%s-border" toneName, colorVarToCss stops.Border

      // Spacing scale (5)
      "--fuaran-space-xs", theme.Spacing.Xs
      "--fuaran-space-sm", theme.Spacing.Sm
      "--fuaran-space-md", theme.Spacing.Md
      "--fuaran-space-lg", theme.Spacing.Lg
      "--fuaran-space-xl", theme.Spacing.Xl

      // Typography sizes (7)
      "--fuaran-text-xs", theme.FontScale.Xs
      "--fuaran-text-sm", theme.FontScale.Sm
      "--fuaran-text-base", theme.FontScale.Base
      "--fuaran-text-lg", theme.FontScale.Lg
      "--fuaran-text-xl", theme.FontScale.Xl
      "--fuaran-text-2xl", theme.FontScale.XXl
      "--fuaran-text-3xl", theme.FontScale.XXXl

      // Font weights (4)
      "--fuaran-font-weight-regular", string theme.FontWeight.Regular
      "--fuaran-font-weight-medium", string theme.FontWeight.Medium
      "--fuaran-font-weight-semibold", string theme.FontWeight.Semibold
      "--fuaran-font-weight-bold", string theme.FontWeight.Bold

      // Line heights (3)
      "--fuaran-line-height-tight", floatCss theme.LineHeight.Tight
      "--fuaran-line-height-normal", floatCss theme.LineHeight.Normal
      "--fuaran-line-height-relaxed", floatCss theme.LineHeight.Relaxed

      // Component dimensions (button pad 2 + radius 4 + border 1 = 7)
      "--fuaran-button-pad-y", theme.ButtonSize.PadY
      "--fuaran-button-pad-x", theme.ButtonSize.PadX
      "--fuaran-radius-sm", theme.Radius.Sm
      "--fuaran-radius-md", theme.Radius.Md
      "--fuaran-radius-lg", theme.Radius.Lg
      "--fuaran-radius-full", theme.Radius.Full
      "--fuaran-border-width", theme.BorderWidth

      // Responsive breakpoints (3) — Phase 58. The reference-CSS `@media`
      // rules repeat these px values literally (a media condition can't read
      // a custom property); the tokens exist so consumer JS / container
      // queries can read the same thresholds the renderer collapses at.
      "--fuaran-breakpoint-sm", theme.Breakpoints.Sm
      "--fuaran-breakpoint-md", theme.Breakpoints.Md
      "--fuaran-breakpoint-lg", theme.Breakpoints.Lg

      // Interaction matrix — 7 tones × 4 states × 3 slots = 84
      for stateName, matrix in interactionStatesInOrder theme.Interaction do
          for toneName, stops in toneStateStopsInOrder matrix do
              sprintf "--fuaran-tone-%s-%s-bg" toneName stateName, colorVarToCss stops.Background
              sprintf "--fuaran-tone-%s-%s-fg" toneName stateName, colorVarToCss stops.Foreground
              sprintf "--fuaran-tone-%s-%s-border" toneName stateName, colorVarToCss stops.Border

      // Focus ring — 4 globals
      "--fuaran-focus-ring-color", colorVarToCss theme.Interaction.FocusRing.Color
      "--fuaran-focus-ring-width", theme.Interaction.FocusRing.Width
      "--fuaran-focus-ring-offset", theme.Interaction.FocusRing.Offset
      "--fuaran-focus-ring-style", theme.Interaction.FocusRing.Style

      // Tab bar — 7 globals
      "--fuaran-tab-padding-y", theme.TabBar.PaddingY
      "--fuaran-tab-padding-x", theme.TabBar.PaddingX
      "--fuaran-tab-indicator-color", colorVarToCss theme.TabBar.IndicatorColor
      "--fuaran-tab-indicator-height", theme.TabBar.IndicatorHeight
      "--fuaran-tab-text-color", colorVarToCss theme.TabBar.TextColor
      "--fuaran-tab-text-active-color", colorVarToCss theme.TabBar.TextActiveColor
      "--fuaran-tab-text-hover-color", colorVarToCss theme.TabBar.TextHoverColor

      // Segmented control / radio-group — 4 globals
      "--fuaran-segmented-bg", colorVarToCss theme.Segmented.Background
      "--fuaran-segmented-active-bg", colorVarToCss theme.Segmented.ActiveBackground
      "--fuaran-segmented-active-fg", colorVarToCss theme.Segmented.ActiveForeground
      "--fuaran-segmented-divider-color", colorVarToCss theme.Segmented.DividerColor ]

/// Emit a `:root { --fuaran-...: value; ... }` block from a Theme. The
/// renderer mounts this into a `<style>` element it maintains. Apps that
/// don't supply a Theme see `Defaults.theme` (which mirrors the reference
/// CSS byte-for-byte, so no visual change).
///
/// The `ButtonSize.FontSize` axis is reserved for the typed surface — the
/// reference CSS doesn't expose it as its own variable yet (button font
/// size routes through `--fuaran-text-base` on `.fuaran-button`). Consumers
/// who want a per-component override can read it off the Theme directly.
let toCss (theme: Theme) : string =
    let lines =
        toCssVariables theme
        |> List.map (fun (name, value) -> sprintf "  %s: %s;" name value)

    String.concat "\n" ([ ":root {" ] @ lines @ [ "}" ])

// ─── Theme JSON round-trip ─────────────────────────────────────────────────
//
// `Theme.toJson` hand-rolls the JSON emission. Fable.SimpleJson's
// `Json.stringify` is Fable-only — it imports JS' native JSON.stringify
// via JsInterop and throws on .NET-side execution (the .NET test runner
// hits it). `Theme.fromJson` uses `SimpleJson.parse` (which DOES work on
// .NET via the library's regex-based fallback parser) + a hand-rolled
// walker over the resulting typed `Json` value.
//
// Wire shape is a flat-record JSON object:
//   { "tones": { "default": { "background": {"kind":"hex","value":"#ffffff"}, ... }, ... },
//     "spacing": {...}, ... }
// `ColorVar` cases are tagged: `{"kind":"hex","value":"..."}`,
// `{"kind":"oklch","l":...,"c":...,"h":...,"alpha":...}`,
// `{"kind":"cssRaw","value":"..."}`.

let private escapeJsonString (s: string) : string =
    let sb = System.Text.StringBuilder(s.Length + 4)

    for ch in s do
        match ch with
        | '"' -> sb.Append "\\\"" |> ignore
        | '\\' -> sb.Append "\\\\" |> ignore
        | '\n' -> sb.Append "\\n" |> ignore
        | '\r' -> sb.Append "\\r" |> ignore
        | '\t' -> sb.Append "\\t" |> ignore
        | '\b' -> sb.Append "\\b" |> ignore
        | '\f' -> sb.Append "\\f" |> ignore
        | c when int c < 0x20 -> sb.AppendFormat("\\u{0:x4}", int c) |> ignore
        | c -> sb.Append c |> ignore

    sb.ToString()

let private jsonString (value: string) : string = "\"" + escapeJsonString value + "\""

let private jsonNumber (value: float) : string = invariantFloat value

let private jsonInt (value: int) : string =
    value.ToString(System.Globalization.CultureInfo.InvariantCulture)

let private jsonObject (fields: (string * string) list) : string =
    let body =
        fields |> List.map (fun (k, v) -> jsonString k + ":" + v) |> String.concat ","

    "{" + body + "}"

let private colorVarToJsonString (color: ColorVar) : string =
    match color with
    | ColorVar.Hex value -> jsonObject [ "kind", jsonString "hex"; "value", jsonString value ]
    | ColorVar.OKLCH(l, c, h, alpha) ->
        jsonObject
            [ "kind", jsonString "oklch"
              "l", jsonNumber l
              "c", jsonNumber c
              "h", jsonNumber h
              "alpha", jsonNumber alpha ]
    | ColorVar.CssRaw raw -> jsonObject [ "kind", jsonString "cssRaw"; "value", jsonString raw ]

let private toneStopsToJsonString (stops: ToneStops) : string =
    jsonObject
        [ "background", colorVarToJsonString stops.Background
          "foreground", colorVarToJsonString stops.Foreground
          "border", colorVarToJsonString stops.Border ]

let private tonesToJsonString (tones: Tones) : string =
    jsonObject
        [ "default", toneStopsToJsonString tones.Default
          "subdued", toneStopsToJsonString tones.Subdued
          "brand", toneStopsToJsonString tones.Brand
          "success", toneStopsToJsonString tones.Success
          "warning", toneStopsToJsonString tones.Warning
          "critical", toneStopsToJsonString tones.Critical
          "info", toneStopsToJsonString tones.Info ]

let private toneStateMatrixToJsonString (matrix: ToneStateMatrix) : string =
    jsonObject
        [ "default", toneStopsToJsonString matrix.Default
          "subdued", toneStopsToJsonString matrix.Subdued
          "brand", toneStopsToJsonString matrix.Brand
          "success", toneStopsToJsonString matrix.Success
          "warning", toneStopsToJsonString matrix.Warning
          "critical", toneStopsToJsonString matrix.Critical
          "info", toneStopsToJsonString matrix.Info ]

let private focusRingToJsonString (ring: FocusRing) : string =
    jsonObject
        [ "color", colorVarToJsonString ring.Color
          "width", jsonString ring.Width
          "offset", jsonString ring.Offset
          "style", jsonString ring.Style ]

let private interactionToJsonString (interaction: Interaction) : string =
    jsonObject
        [ "focusRing", focusRingToJsonString interaction.FocusRing
          "hover", toneStateMatrixToJsonString interaction.Hover
          "focus", toneStateMatrixToJsonString interaction.Focus
          "active", toneStateMatrixToJsonString interaction.Active
          "disabled", toneStateMatrixToJsonString interaction.Disabled ]

/// Serialise a Theme to its flat-JSON wire shape. Hand-rolled emitter —
/// see the comment block above for why Fable.SimpleJson's `Json.stringify`
/// isn't usable here.
let toJson (theme: Theme) : string =
    jsonObject
        [ "tones", tonesToJsonString theme.Tones
          "spacing",
          jsonObject
              [ "xs", jsonString theme.Spacing.Xs
                "sm", jsonString theme.Spacing.Sm
                "md", jsonString theme.Spacing.Md
                "lg", jsonString theme.Spacing.Lg
                "xl", jsonString theme.Spacing.Xl ]
          "fontScale",
          jsonObject
              [ "xs", jsonString theme.FontScale.Xs
                "sm", jsonString theme.FontScale.Sm
                "base", jsonString theme.FontScale.Base
                "lg", jsonString theme.FontScale.Lg
                "xl", jsonString theme.FontScale.Xl
                "xxl", jsonString theme.FontScale.XXl
                "xxxl", jsonString theme.FontScale.XXXl ]
          "fontWeight",
          jsonObject
              [ "regular", jsonInt theme.FontWeight.Regular
                "medium", jsonInt theme.FontWeight.Medium
                "semibold", jsonInt theme.FontWeight.Semibold
                "bold", jsonInt theme.FontWeight.Bold ]
          "lineHeight",
          jsonObject
              [ "tight", jsonNumber theme.LineHeight.Tight
                "normal", jsonNumber theme.LineHeight.Normal
                "relaxed", jsonNumber theme.LineHeight.Relaxed ]
          "radius",
          jsonObject
              [ "sm", jsonString theme.Radius.Sm
                "md", jsonString theme.Radius.Md
                "lg", jsonString theme.Radius.Lg
                "full", jsonString theme.Radius.Full ]
          "buttonSize",
          jsonObject
              [ "padY", jsonString theme.ButtonSize.PadY
                "padX", jsonString theme.ButtonSize.PadX
                "fontSize", jsonString theme.ButtonSize.FontSize ]
          "borderWidth", jsonString theme.BorderWidth
          "interaction", interactionToJsonString theme.Interaction
          "tabBar",
          jsonObject
              [ "paddingY", jsonString theme.TabBar.PaddingY
                "paddingX", jsonString theme.TabBar.PaddingX
                "indicatorColor", colorVarToJsonString theme.TabBar.IndicatorColor
                "indicatorHeight", jsonString theme.TabBar.IndicatorHeight
                "textColor", colorVarToJsonString theme.TabBar.TextColor
                "textActiveColor", colorVarToJsonString theme.TabBar.TextActiveColor
                "textHoverColor", colorVarToJsonString theme.TabBar.TextHoverColor ]
          "segmented",
          jsonObject
              [ "background", colorVarToJsonString theme.Segmented.Background
                "activeBackground", colorVarToJsonString theme.Segmented.ActiveBackground
                "activeForeground", colorVarToJsonString theme.Segmented.ActiveForeground
                "dividerColor", colorVarToJsonString theme.Segmented.DividerColor ]
          "breakpoints",
          jsonObject
              [ "sm", jsonString theme.Breakpoints.Sm
                "md", jsonString theme.Breakpoints.Md
                "lg", jsonString theme.Breakpoints.Lg ] ]

// ─── fromJson — minimal hand-rolled JSON parser for the Theme wire shape ──
//
// Grammar (Theme JSON only — no arrays, booleans, or nulls):
//   value  := object | string | number
//   object := '{' (string ':' value (',' string ':' value)*)? '}'
//   string := '"' (escape | non-quote-non-backslash)* '"'
//             escapes: \" \\ \/ \n \r \t \b \f \uHHHH
//   number := optional '-' INT ('.' DIGIT+)? ('e' / 'E' (+/-)? DIGIT+)?
//
// Errors return `Result<Theme, string>` rather than throwing — the
// consumer (Portal, AI pipeline) typically wraps `fromJson` in an
// error-surfacing step rather than letting a parse failure crash the
// renderer.

[<RequireQualifiedAccess>]
type private JsonValue =
    | JObj of Map<string, JsonValue>
    | JStr of string
    | JNum of float

type private ParseState = { Source: string; mutable Pos: int }

let private (>>=) (r: Result<'a, string>) (f: 'a -> Result<'b, string>) : Result<'b, string> = Result.bind f r

let private peek (s: ParseState) : char =
    if s.Pos < s.Source.Length then s.Source[s.Pos] else '\000'

let private advance (s: ParseState) : unit = s.Pos <- s.Pos + 1

let private skipWhitespace (s: ParseState) : unit =
    while s.Pos < s.Source.Length
          && (let c = s.Source[s.Pos] in c = ' ' || c = '\t' || c = '\n' || c = '\r') do
        advance s

let private parseString (s: ParseState) : Result<string, string> =
    if peek s <> '"' then
        Error(sprintf "expected '\"' at position %d, got '%c'" s.Pos (peek s))
    else
        advance s // consume opening quote
        let sb = System.Text.StringBuilder()
        let mutable finished = false
        let mutable err: string option = None

        while not finished && Option.isNone err do
            if s.Pos >= s.Source.Length then
                err <- Some "unexpected end of input in string literal"
            else
                let c = s.Source[s.Pos]

                if c = '"' then
                    advance s
                    finished <- true
                elif c = '\\' then
                    advance s

                    if s.Pos >= s.Source.Length then
                        err <- Some "unexpected end of input in string escape"
                    else
                        let esc = s.Source[s.Pos]
                        advance s

                        match esc with
                        | '"' -> sb.Append '"' |> ignore
                        | '\\' -> sb.Append '\\' |> ignore
                        | '/' -> sb.Append '/' |> ignore
                        | 'n' -> sb.Append '\n' |> ignore
                        | 'r' -> sb.Append '\r' |> ignore
                        | 't' -> sb.Append '\t' |> ignore
                        | 'b' -> sb.Append '\b' |> ignore
                        | 'f' -> sb.Append '\f' |> ignore
                        | 'u' ->
                            if s.Pos + 4 > s.Source.Length then
                                err <- Some "truncated \\uHHHH escape"
                            else
                                let hex = s.Source.Substring(s.Pos, 4)
                                // Fable rejects `Int32.TryParse(string, NumberStyles, IFormatProvider)`
                                // — decode the four-hex-digit BMP codepoint by hand. Mirrors the
                                // catalog sample's `JsonDecode.fs:121` loop.
                                let mutable code = 0
                                let mutable hexOk = true

                                for ch in hex do
                                    let digit =
                                        if ch >= '0' && ch <= '9' then
                                            int ch - int '0'
                                        elif ch >= 'a' && ch <= 'f' then
                                            int ch - int 'a' + 10
                                        elif ch >= 'A' && ch <= 'F' then
                                            int ch - int 'A' + 10
                                        else
                                            hexOk <- false
                                            0

                                    code <- code * 16 + digit

                                if hexOk then
                                    sb.Append(char code) |> ignore
                                    s.Pos <- s.Pos + 4
                                else
                                    err <- Some(sprintf "invalid \\u escape: \\u%s" hex)
                        | other -> err <- Some(sprintf "invalid escape: \\%c" other)
                else
                    sb.Append c |> ignore
                    advance s

        match err with
        | Some e -> Error e
        | None -> Ok(sb.ToString())

let private parseNumber (s: ParseState) : Result<float, string> =
    let start = s.Pos
    // Sign
    if peek s = '-' then
        advance s

    // Integer part
    while s.Pos < s.Source.Length && (let c = s.Source[s.Pos] in c >= '0' && c <= '9') do
        advance s

    // Fraction
    if peek s = '.' then
        advance s

        while s.Pos < s.Source.Length && (let c = s.Source[s.Pos] in c >= '0' && c <= '9') do
            advance s

    // Exponent
    if peek s = 'e' || peek s = 'E' then
        advance s

        if peek s = '+' || peek s = '-' then
            advance s

        while s.Pos < s.Source.Length && (let c = s.Source[s.Pos] in c >= '0' && c <= '9') do
            advance s

    let raw = s.Source.Substring(start, s.Pos - start)

    if raw.Length = 0 || raw = "-" then
        Error(sprintf "expected number at position %d" start)
    else
        // Fable rejects `Double.TryParse(string, NumberStyles, IFormatProvider)`.
        // AI emissions are en-US-shaped (dot-decimal, no thousand-separators),
        // so the invariant-culture overload is redundant — single-arg suffices.
        match System.Double.TryParse raw with
        | true, n -> Ok n
        | false, _ -> Error(sprintf "invalid number literal: %s" raw)

let rec private parseValue (s: ParseState) : Result<JsonValue, string> =
    skipWhitespace s

    match peek s with
    | '{' -> parseObject s
    | '"' -> parseString s |> Result.map JsonValue.JStr
    | c when c = '-' || (c >= '0' && c <= '9') -> parseNumber s |> Result.map JsonValue.JNum
    | other -> Error(sprintf "unexpected character '%c' at position %d" other s.Pos)

and private parseObject (s: ParseState) : Result<JsonValue, string> =
    if peek s <> '{' then
        Error(sprintf "expected '{' at position %d" s.Pos)
    else
        advance s
        skipWhitespace s
        let mutable pairs: (string * JsonValue) list = []
        let mutable finished = false
        let mutable err: string option = None

        if peek s = '}' then
            advance s
            finished <- true

        while not finished && Option.isNone err do
            skipWhitespace s

            match parseString s with
            | Error e -> err <- Some e
            | Ok key ->
                skipWhitespace s

                if peek s <> ':' then
                    err <- Some(sprintf "expected ':' after key '%s' at position %d" key s.Pos)
                else
                    advance s

                    match parseValue s with
                    | Error e -> err <- Some e
                    | Ok value ->
                        pairs <- (key, value) :: pairs
                        skipWhitespace s

                        match peek s with
                        | ',' ->
                            advance s
                            skipWhitespace s
                        | '}' ->
                            advance s
                            finished <- true
                        | other ->
                            err <-
                                Some(
                                    sprintf
                                        "expected ',' or '}' after object value, got '%c' at position %d"
                                        other
                                        s.Pos
                                )

        match err with
        | Some e -> Error e
        | None -> Ok(JsonValue.JObj(Map.ofList (List.rev pairs)))

let private parseJson (json: string) : Result<JsonValue, string> =
    let state = { Source = json; Pos = 0 }
    let result = parseValue state
    // Trailing whitespace is allowed; trailing non-whitespace is not.
    skipWhitespace state

    match result with
    | Error _ -> result
    | Ok _ when state.Pos < state.Source.Length ->
        Error(sprintf "trailing characters after root value at position %d" state.Pos)
    | Ok _ -> result

let private lookup (key: string) (m: Map<string, JsonValue>) : Result<JsonValue, string> =
    match Map.tryFind key m with
    | Some v -> Ok v
    | None -> Error(sprintf "missing field: %s" key)

let private expectObject (path: string) (j: JsonValue) : Result<Map<string, JsonValue>, string> =
    match j with
    | JsonValue.JObj m -> Ok m
    | _ -> Error(sprintf "%s: expected object" path)

let private expectString (path: string) (j: JsonValue) : Result<string, string> =
    match j with
    | JsonValue.JStr s -> Ok s
    | _ -> Error(sprintf "%s: expected string" path)

let private expectNumber (path: string) (j: JsonValue) : Result<float, string> =
    match j with
    | JsonValue.JNum n -> Ok n
    | _ -> Error(sprintf "%s: expected number" path)

let private colorVarFromJson (path: string) (j: JsonValue) : Result<ColorVar, string> =
    expectObject path j
    >>= fun m ->
        lookup "kind" m
        >>= expectString (path + ".kind")
        >>= fun kind ->
            match kind with
            | "hex" -> lookup "value" m >>= expectString (path + ".value") >>= (ColorVar.Hex >> Ok)
            | "cssRaw" -> lookup "value" m >>= expectString (path + ".value") >>= (ColorVar.CssRaw >> Ok)
            | "oklch" ->
                lookup "l" m
                >>= expectNumber (path + ".l")
                >>= fun l ->
                    lookup "c" m
                    >>= expectNumber (path + ".c")
                    >>= fun c ->
                        lookup "h" m
                        >>= expectNumber (path + ".h")
                        >>= fun h ->
                            lookup "alpha" m
                            >>= expectNumber (path + ".alpha")
                            >>= fun alpha -> Ok(ColorVar.OKLCH(l, c, h, alpha))
            | other -> Error(sprintf "%s.kind: unknown ColorVar kind %s" path other)

let private toneStopsFromJson (path: string) (j: JsonValue) : Result<ToneStops, string> =
    expectObject path j
    >>= fun m ->
        lookup "background" m
        >>= colorVarFromJson (path + ".background")
        >>= fun bg ->
            lookup "foreground" m
            >>= colorVarFromJson (path + ".foreground")
            >>= fun fg ->
                lookup "border" m
                >>= colorVarFromJson (path + ".border")
                >>= fun border ->
                    Ok
                        { Background = bg
                          Foreground = fg
                          Border = border }

let private tonesFromJson (path: string) (j: JsonValue) : Result<Tones, string> =
    expectObject path j
    >>= fun m ->
        let stopAt name =
            lookup name m >>= toneStopsFromJson (path + "." + name)

        stopAt "default"
        >>= fun d ->
            stopAt "subdued"
            >>= fun s ->
                stopAt "brand"
                >>= fun b ->
                    stopAt "success"
                    >>= fun su ->
                        stopAt "warning"
                        >>= fun w ->
                            stopAt "critical"
                            >>= fun c ->
                                stopAt "info"
                                >>= fun i ->
                                    Ok
                                        { Default = d
                                          Subdued = s
                                          Brand = b
                                          Success = su
                                          Warning = w
                                          Critical = c
                                          Info = i }

let private spacingFromJson (path: string) (j: JsonValue) : Result<Spacing, string> =
    expectObject path j
    >>= fun m ->
        let s name =
            lookup name m >>= expectString (path + "." + name)

        s "xs"
        >>= fun xs ->
            s "sm"
            >>= fun sm ->
                s "md"
                >>= fun md ->
                    s "lg"
                    >>= fun lg ->
                        s "xl"
                        >>= fun xl ->
                            Ok
                                { Xs = xs
                                  Sm = sm
                                  Md = md
                                  Lg = lg
                                  Xl = xl }

let private fontScaleFromJson (path: string) (j: JsonValue) : Result<FontScale, string> =
    expectObject path j
    >>= fun m ->
        let s name =
            lookup name m >>= expectString (path + "." + name)

        s "xs"
        >>= fun xs ->
            s "sm"
            >>= fun sm ->
                s "base"
                >>= fun base' ->
                    s "lg"
                    >>= fun lg ->
                        s "xl"
                        >>= fun xl ->
                            s "xxl"
                            >>= fun xxl ->
                                s "xxxl"
                                >>= fun xxxl ->
                                    Ok
                                        { Xs = xs
                                          Sm = sm
                                          Base = base'
                                          Lg = lg
                                          Xl = xl
                                          XXl = xxl
                                          XXXl = xxxl }

let private fontWeightFromJson (path: string) (j: JsonValue) : Result<FontWeight, string> =
    expectObject path j
    >>= fun m ->
        let n name =
            lookup name m >>= expectNumber (path + "." + name) >>= (int >> Ok)

        n "regular"
        >>= fun r ->
            n "medium"
            >>= fun me ->
                n "semibold"
                >>= fun s ->
                    n "bold"
                    >>= fun b ->
                        Ok
                            { Regular = r
                              Medium = me
                              Semibold = s
                              Bold = b }

let private lineHeightFromJson (path: string) (j: JsonValue) : Result<LineHeight, string> =
    expectObject path j
    >>= fun m ->
        let f name =
            lookup name m >>= expectNumber (path + "." + name)

        f "tight"
        >>= fun t ->
            f "normal"
            >>= fun n -> f "relaxed" >>= fun r -> Ok { Tight = t; Normal = n; Relaxed = r }

let private radiusFromJson (path: string) (j: JsonValue) : Result<Radius, string> =
    expectObject path j
    >>= fun m ->
        let s name =
            lookup name m >>= expectString (path + "." + name)

        s "sm"
        >>= fun sm ->
            s "md"
            >>= fun md ->
                s "lg"
                >>= fun lg -> s "full" >>= fun f -> Ok { Sm = sm; Md = md; Lg = lg; Full = f }

let private buttonSizeFromJson (path: string) (j: JsonValue) : Result<ButtonSize, string> =
    expectObject path j
    >>= fun m ->
        let s name =
            lookup name m >>= expectString (path + "." + name)

        s "padY"
        >>= fun y ->
            s "padX"
            >>= fun x -> s "fontSize" >>= fun fs -> Ok { PadY = y; PadX = x; FontSize = fs }

let private toneStateMatrixFromJson (path: string) (j: JsonValue) : Result<ToneStateMatrix, string> =
    expectObject path j
    >>= fun m ->
        let stopAt name =
            lookup name m >>= toneStopsFromJson (path + "." + name)

        stopAt "default"
        >>= fun d ->
            stopAt "subdued"
            >>= fun s ->
                stopAt "brand"
                >>= fun b ->
                    stopAt "success"
                    >>= fun su ->
                        stopAt "warning"
                        >>= fun w ->
                            stopAt "critical"
                            >>= fun c ->
                                stopAt "info"
                                >>= fun i ->
                                    Ok
                                        { Default = d
                                          Subdued = s
                                          Brand = b
                                          Success = su
                                          Warning = w
                                          Critical = c
                                          Info = i }

let private focusRingFromJson (path: string) (j: JsonValue) : Result<FocusRing, string> =
    expectObject path j
    >>= fun m ->
        let s name =
            lookup name m >>= expectString (path + "." + name)

        lookup "color" m
        >>= colorVarFromJson (path + ".color")
        >>= fun color ->
            s "width"
            >>= fun w ->
                s "offset"
                >>= fun o ->
                    s "style"
                    >>= fun st ->
                        Ok
                            { Color = color
                              Width = w
                              Offset = o
                              Style = st }

let private interactionFromJson (path: string) (j: JsonValue) : Result<Interaction, string> =
    expectObject path j
    >>= fun m ->
        let matrixAt name =
            lookup name m >>= toneStateMatrixFromJson (path + "." + name)

        lookup "focusRing" m
        >>= focusRingFromJson (path + ".focusRing")
        >>= fun ring ->
            matrixAt "hover"
            >>= fun hover ->
                matrixAt "focus"
                >>= fun focus ->
                    matrixAt "active"
                    >>= fun active ->
                        matrixAt "disabled"
                        >>= fun disabled ->
                            Ok
                                { FocusRing = ring
                                  Hover = hover
                                  Focus = focus
                                  Active = active
                                  Disabled = disabled }

/// Decode the segmented-control / radio-group shape
/// from the flat-JSON wire form. Four `ColorVar` cases.
let private segmentedFromJson (path: string) (j: JsonValue) : Result<Segmented, string> =
    expectObject path j
    >>= fun m ->
        let c name =
            lookup name m >>= colorVarFromJson (path + "." + name)

        c "background"
        >>= fun bg ->
            c "activeBackground"
            >>= fun ab ->
                c "activeForeground"
                >>= fun af ->
                    c "dividerColor"
                    >>= fun dc ->
                        Ok
                            { Background = bg
                              ActiveBackground = ab
                              ActiveForeground = af
                              DividerColor = dc }

/// Decode the tab-bar shape from the flat-JSON wire
/// form. Same structure as `focusRingFromJson` — three string scalars +
/// four `ColorVar` cases.
let private tabBarFromJson (path: string) (j: JsonValue) : Result<TabBar, string> =
    expectObject path j
    >>= fun m ->
        let s name =
            lookup name m >>= expectString (path + "." + name)

        let c name =
            lookup name m >>= colorVarFromJson (path + "." + name)

        s "paddingY"
        >>= fun py ->
            s "paddingX"
            >>= fun px ->
                c "indicatorColor"
                >>= fun ic ->
                    s "indicatorHeight"
                    >>= fun ih ->
                        c "textColor"
                        >>= fun tc ->
                            c "textActiveColor"
                            >>= fun tac ->
                                c "textHoverColor"
                                >>= fun thc ->
                                    Ok
                                        { PaddingY = py
                                          PaddingX = px
                                          IndicatorColor = ic
                                          IndicatorHeight = ih
                                          TextColor = tc
                                          TextActiveColor = tac
                                          TextHoverColor = thc }

/// Decode the responsive-breakpoint shape (Phase 58) from the flat-JSON
/// wire form. Three string scalars.
let private breakpointsFromJson (path: string) (j: JsonValue) : Result<Breakpoints, string> =
    expectObject path j
    >>= fun m ->
        let s name =
            lookup name m >>= expectString (path + "." + name)

        s "sm"
        >>= fun sm -> s "md" >>= fun md -> s "lg" >>= fun lg -> Ok { Sm = sm; Md = md; Lg = lg }

/// Parse a Theme from its flat-JSON wire shape. Returns `Error <message>`
/// on malformed / missing field; the consumer (Portal / AI pipeline) wraps
/// this in an error-surfacing step rather than crashing the renderer.
let fromJson (json: string) : Result<Theme, string> =
    try
        parseJson json
        >>= fun parsed ->
            match parsed with
            | JsonValue.JObj m ->
                lookup "tones" m
                >>= tonesFromJson "tones"
                >>= fun tones ->
                    lookup "spacing" m
                    >>= spacingFromJson "spacing"
                    >>= fun spacing ->
                        lookup "fontScale" m
                        >>= fontScaleFromJson "fontScale"
                        >>= fun fontScale ->
                            lookup "fontWeight" m
                            >>= fontWeightFromJson "fontWeight"
                            >>= fun fontWeight ->
                                lookup "lineHeight" m
                                >>= lineHeightFromJson "lineHeight"
                                >>= fun lineHeight ->
                                    lookup "radius" m
                                    >>= radiusFromJson "radius"
                                    >>= fun radius ->
                                        lookup "buttonSize" m
                                        >>= buttonSizeFromJson "buttonSize"
                                        >>= fun buttonSize ->
                                            lookup "borderWidth" m
                                            >>= expectString "borderWidth"
                                            >>= fun borderWidth ->
                                                lookup "interaction" m
                                                >>= interactionFromJson "interaction"
                                                >>= fun interaction ->
                                                    lookup "tabBar" m
                                                    >>= tabBarFromJson "tabBar"
                                                    >>= fun tabBar ->
                                                        lookup "segmented" m
                                                        >>= segmentedFromJson "segmented"
                                                        >>= fun segmented ->
                                                            lookup "breakpoints" m
                                                            >>= breakpointsFromJson "breakpoints"
                                                            >>= fun breakpoints ->
                                                                Ok
                                                                    { Tones = tones
                                                                      Spacing = spacing
                                                                      FontScale = fontScale
                                                                      FontWeight = fontWeight
                                                                      LineHeight = lineHeight
                                                                      Radius = radius
                                                                      ButtonSize = buttonSize
                                                                      BorderWidth = borderWidth
                                                                      Interaction = interaction
                                                                      TabBar = tabBar
                                                                      Segmented = segmented
                                                                      Breakpoints = breakpoints }
            | _ -> Error "root: expected object"
    with ex ->
        Error(sprintf "JSON parse failed: %s" ex.Message)
