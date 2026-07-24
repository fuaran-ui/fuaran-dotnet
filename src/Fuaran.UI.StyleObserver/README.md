# Fuaran.UI.StyleObserver

Two concrete observers against [`Fuaran.UI.StyleObserver.Abstractions`](https://www.nuget.org/packages/Fuaran.UI.StyleObserver.Abstractions) — the resolved-style read-back twin of `Fuaran.UI.LayoutObserver`:

- **`BrowserStyleObserver`** (Fable / browser) — reads `getComputedStyle` per registered element, climbs ancestors to build the background-layer stack, folds it through the effective-background composite walk, derives WCAG contrast + the legibility flags, emits to subscribers per the configured debounce + change-detection policy. The shipping path for production Fuaran applications. Discovers nodes via `[data-fuaran-node-id]` and re-derives on `class` / `style` / `data-fuaran-tone` mutations (a theme toggle recolours the tree).
- **`InMemoryStyleObserver`** (pure .NET) — accepts hand-authored `StyleFixture` snapshots; runs under Expecto; powers the test suite and the future eval gate. Identical flag-derivation logic — same input, same output as the browser observer.

The load-bearing algorithm — the **effective-background composite walk** (`Flags.effectiveBackground`) — composites a translucent background-layer stack down to the first opaque layer so the WCAG contrast denominator is the colour the text actually sits on. It lives in the Abstractions package and is shared by both observers, so a fixture-pinned contrast result is exactly what the browser produces from the same colours.

## Manifest-aware verification (Phase 146)

When a [`Fuaran.UI.ThemeManifest`](https://www.nuget.org/packages/Fuaran.UI.ThemeManifest) is wired into an observer (`create*WithManifest` / the `(options, manifest)` constructor), each observation additionally carries the **manifest-aware** flags — render-time enforcement of a declared aesthetic-semantic budget, fed back in the manifest's own vocabulary (deterministic, no VLM in the verify path). `ManifestFlags` exposes the two surfaces:

- **Per-node** (`perNodeFlags`, appended automatically by a manifest-wired observer): `TokenResolutionFailed` (an emitted tone bound to no host token — the emission fell through the CSS), `OffPaletteColour` (a toned fill that isn't in the palette), `ContrastBelowDeclaredFloor` (a per-role floor stricter than the manifest-free AA default).
- **Tree-level** (`verifyUsageBudgets`): the area-weighted colour-distribution check (the 60-30-10 formalisation). It **composes with `Fuaran.UI.LayoutObserver`** — the caller joins each `StyleObservation` fill with the node's `LayoutObservation.Width × Height` area and passes `(observation, areaPx²)` pairs; per-token area share is compared to each `UsageBudget` invariant's `targetPct ± tolerancePct`, emitting `UsageBudgetExceeded("token", declared%, observed%)`. **This is the one flag that requires both observers** — without `LayoutObserver` areas it degrades gracefully (no budget flags; the per-node fidelity flags still fire).

**Custom-subtree policy — EXEMPT.** Every per-node manifest check fires only for *toned* nodes (those carrying an `EmittedTone`). Custom / domain-SVG content never carries a Fuaran `data-fuaran-tone`, so `OffPaletteColour` cannot spuriously fire on a chart's series colours or a logo's gradients. With no manifest wired, only the manifest-free (Phase 144) flags fire.

Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
