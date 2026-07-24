namespace Fuaran.UI.StyleObserver

// ─── Style flags — the AI-facing legibility interpretations ─────
//
// The orchestrator's semantic-state channel is blind to *resolved
// style* failures: the AI emits `Tone.Brand`, the host CSS resolves
// it through the `--fuaran-tone-{tone}-{bg,fg,border}` contract, and
// nothing reports back whether the rendered result is legible,
// distinguishable, or even non-null. This DU is the small fixed
// vocabulary the style observer derives from resolved colours +
// WCAG contrast and surfaces to the AI via the `Style` snapshot +
// `_platform.ui.inspect_style`.
//
// **Twin of `LayoutFlag`.** `kind`/`encode` mirror `LayoutFlag`
// exactly (PascalCase-matched discriminators, tagged-object JSON,
// invariant-culture floats) so an AI prompt cache that pattern-matches
// the layout surface pattern-matches the style surface identically.
//
// **Additive-only post-ship.** Each flag is a load-bearing AI input
// — adding a new case is fine (existing consumers compile with an
// FS0025 exhaustiveness warning), but redefining an existing case
// breaks every prompt cache that pattern-matched against it.
//
// **Two tiers.** The first three cases are MANIFEST-FREE (Phase 144):
// derived from resolved colours + WCAG contrast alone, no theme
// contract needed. The last four are MANIFEST-AWARE (Phase 146):
// derived against a declared `Fuaran.UI.ThemeManifest` — the
// render-time enforcement of a declared aesthetic-semantic budget,
// fed back in the manifest's own vocabulary. Manifest-aware flags
// fire only when the observer has a manifest wired (graceful
// degradation otherwise).

/// The fixed set of resolved-style interpretation flags the observer
/// derives. Additive-only post-ship. Cases 1–3 are manifest-free
/// (Phase 144); cases 4–7 are manifest-aware (Phase 146).
type StyleFlag =
    /// The composited foreground-over-effective-background WCAG
    /// contrast ratio is below the WCAG AA normal-text floor (4.5:1
    /// default) but the text is still faintly visible. `ratio` is the
    /// observed contrast (≥ the invisible-text threshold, < the AA
    /// threshold).
    | ContrastBelowAA of ratio: float
    /// The contrast ratio is at or near 1.0 — the text is the same
    /// (or all-but-the-same) colour as the surface behind it, i.e.
    /// effectively invisible. `ratio` is the observed contrast
    /// (below the invisible-text threshold). The severe subset of
    /// the legibility failure space; fires *instead of*
    /// `ContrastBelowAA`, not alongside it.
    | InvisibleText of ratio: float
    /// The element declared a tone (`EmittedTone = Some _`) and has a
    /// distinct background tint, but that tint's contrast against the
    /// surface behind it is below the WCAG UI-component floor (3.0:1
    /// default) — the toned accent surface is indistinguishable from
    /// its container, so the tone conveys no visible signal. `ratio`
    /// is the accent-surface-vs-ancestor-surface contrast.
    | AccentIndistinct of ratio: float
    // ─── Manifest-aware (Phase 146) ─────────────────────────────
    /// The element emitted a tone/role (`slot`) the declared manifest
    /// has no token for — the AI's emission fell through the host CSS
    /// (resolved to `initial`/`unset`/transparent where a token was
    /// expected). `slot` is the unresolved tone/role name.
    | TokenResolutionFailed of slot: string
    /// A toned element's resolved fill (`value`) is not present in the
    /// manifest palette — an off-palette colour leaked through. Only
    /// toned elements are checked; untoned content (including Custom /
    /// domain-SVG subtrees, which carry no tone) is exempt by policy.
    | OffPaletteColour of value: string
    /// A token's share of total visible surface area breached its
    /// declared `UsageBudget` (the 60-30-10 formalisation). `token` is
    /// the manifest token name; `declaredPct` is the budget target;
    /// `observedPct` is the area-weighted observed share. A tree-level
    /// flag (requires both the style + layout observers).
    | UsageBudgetExceeded of token: string * declaredPct: float * observedPct: float
    /// A role's resolved contrast is below the manifest's declared
    /// per-role floor (stricter than the manifest-free AA default).
    /// `role` is the manifest role; `ratio` the observed contrast;
    /// `floor` the declared minimum.
    | ContrastBelowDeclaredFloor of role: string * ratio: float * floor: float

module StyleFlag =
    /// Stable string discriminator for JSON / log emission. The names
    /// are PascalCase-matched to the DU cases so AI prompt caches that
    /// pattern-match on either side stay aligned with the F# surface
    /// (identical discipline to `LayoutFlag.kind`).
    let kind (flag: StyleFlag) : string =
        match flag with
        | ContrastBelowAA _ -> "ContrastBelowAA"
        | InvisibleText _ -> "InvisibleText"
        | AccentIndistinct _ -> "AccentIndistinct"
        | TokenResolutionFailed _ -> "TokenResolutionFailed"
        | OffPaletteColour _ -> "OffPaletteColour"
        | UsageBudgetExceeded _ -> "UsageBudgetExceeded"
        | ContrastBelowDeclaredFloor _ -> "ContrastBelowDeclaredFloor"

    /// Encode a single flag as the AI-friendly tagged-object JSON
    /// shape. Invariant-culture floats so a German-locale build doesn't
    /// emit "3,21" — the wire format is JSON, period as decimal
    /// separator. Mirrors `LayoutFlag.encode` so the model
    /// pattern-matches both surfaces the same way.
    let encode (flag: StyleFlag) : string =
        let inv = System.Globalization.CultureInfo.InvariantCulture
        let f (n: float) = n.ToString("F2", inv)

        let escape (s: string) =
            s.Replace("\\", "\\\\").Replace("\"", "\\\"")

        match flag with
        | ContrastBelowAA ratio
        | InvisibleText ratio
        | AccentIndistinct ratio -> $"{{\"kind\":\"{kind flag}\",\"ratio\":{f ratio}}}"
        | TokenResolutionFailed slot -> $"{{\"kind\":\"TokenResolutionFailed\",\"slot\":\"{escape slot}\"}}"
        | OffPaletteColour value -> $"{{\"kind\":\"OffPaletteColour\",\"value\":\"{escape value}\"}}"
        | UsageBudgetExceeded(token, declaredPct, observedPct) ->
            $"{{\"kind\":\"UsageBudgetExceeded\",\"token\":\"{escape token}\",\"declaredPct\":{f declaredPct},\"observedPct\":{f observedPct}}}"
        | ContrastBelowDeclaredFloor(role, ratio, floor) ->
            $"{{\"kind\":\"ContrastBelowDeclaredFloor\",\"role\":\"{escape role}\",\"ratio\":{f ratio},\"floor\":{f floor}}}"
