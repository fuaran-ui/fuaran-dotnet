# Fuaran visual component catalog – operator guide

> Phase 12.S, 2026-05-26. Pairs with [`HOST-STYLING-CHECKLIST.md`](../HOST-STYLING-CHECKLIST.md) (class-hook contract) and [`AI_AUTHORING_GUIDE.md`](AI_AUTHORING_GUIDE.md) (the JSON wire shape the catalog visualises).

The catalog is a Vite-hosted gallery that renders every `NodeKind` × `Tone` × `Weight` × `Emphasis` combination of the §4b type contract. Triple use:

1. **AI eval input** – the catalog is the substrate the Phase 12.E eval suite consumes. Paste a Fuaran emission into `?ai-eval=1`, see what renders, compare against the expected output.
2. **Human design review surface** – every consumer app consumes the same `Fuaran.UI.Renderer`. A CSS-token tweak in `Fuaran.UI.Renderer.Theme` can be visually verified against every component in one place, not asserted by manual review across apps.
3. **Regression-detection target** – the Playwright snapshot harness flags pixel diffs > 0.5% against committed baselines.

## Running the catalog locally

```powershell
# From Fuaran/samples/catalog/:
npm install
dotnet fable -o output --noCache --watch   # Fable transpile in watch mode
npm run dev                                 # Vite dev server on http://localhost:23920
```

> **Fable-compat note – `NumberStyles` / `IFormatProvider` overloads.** Fable 5.x rejects the multi-arg numeric-parse overloads (`Int32.TryParse(string, NumberStyles, IFormatProvider)`, `Double.TryParse(...)`, `Decimal.TryParse(...)` – "error FABLE: … provider argument is ignored"). The fix everywhere is the same: drop the culture argument and use the single-arg overload (AI emissions and the catalog's sample JSON are all `en-US`-shaped – dot-decimal, no thousands separators – so the invariant-culture distinction is moot). Applied in `Fuaran.UI.Renderer/Theme.fs` (the JSON Theme parser), `samples/catalog/JsonDecode.fs:159`, and `samples/catalog/LocalBindings.fs` (the `parseDecimalLenient` salary helper). The `.NET`-only Expecto test `src/Fuaran.UI.Tests/LocalBindingTests.fs` keeps the multi-arg overload – it is never Fable-transpiled.

Port allocation: `13920` server / `23920` Vite per the workspace [`CLAUDE.md`](../../CLAUDE.md) "Port allocation across sibling applications" mandate. Website-class slot, sibling-disjoint from `Fuaran/samples/demo` (`13910` / `23910`).

The catalog opens to the side-nav of NodeKinds (grouped by Display / Input / Visualisation / Layout / Custom). Clicking a kind populates the main pane with the cartesian sweep of (Tone × Weight × Emphasis) cards.

Each card shows:
- The **live render** of the picked spec.
- The **JSON wire shape** (collapsed by default; click to expand) – the canonical §4d projection emitted by [`Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode`](../src/Fuaran.UI.OpStream.Abstractions/CanonicalJson.fs).
- The **F# author snippet** (collapsed by default) – what the equivalent smart-constructor authoring looks like.

## Live theme switcher

The top-bar drop-down swaps between five typed `Theme` records:

| Option | Source | Purpose |
|---|---|---|
| Default | [`samples/themes/Default.fs`](../samples/themes/Default.fs) | Mirrors `Fuaran.UI.Defaults.theme` – the post-12.H reference. |
| Dark | [`samples/themes/Dark.fs`](../samples/themes/Dark.fs) | Dark-mode variant; inverts surface/foreground. |
| High Contrast | [`samples/themes/HighContrast.fs`](../samples/themes/HighContrast.fs) | A11y baseline. |
| Teal (branded) | [`samples/catalog/TealBrandTheme.fs`](../samples/catalog/TealBrandTheme.fs) | Teal/cyan accent – a branded sample. |
| Amber (branded) | [`samples/catalog/AmberBrandTheme.fs`](../samples/catalog/AmberBrandTheme.fs) | Amber accent + softer radius – a branded sample. |

Switching themes re-renders the whole matrix; the new `Theme` record projects through [`Render.themeStyleElement`](../src/Fuaran.UI.Renderer/Render.fs) into a `<style>` block React diffs. No reflow stutter (the CSS variables re-bind in place).

## AI-eval-input mode

URL: `http://localhost:23920/?ai-eval=1`

A single JSON-paste textarea sits at the top of the page. Clicking **Render** decodes the input via [`samples/catalog/JsonDecode.fs`](../samples/catalog/JsonDecode.fs) and either:

- **Success** – the resulting `Node<unit>` renders below.
- **Failure** – the §4d AI-recovery error envelope renders in the error pane.

### Decoder scope (v1)

The catalog's AI-eval decoder is deliberately narrow – covers Dashboard / Stack / Card + Markdown / Heading / Metric / Badge / Callout / Spacer / Skeleton. The full symmetric JSON-to-Node decoder ships with the Phase 12.Z persistence substrate; until then, the catalog accepts the §4d common subset and reports `DecodeError` with `supported_kinds` populated for AI recovery.

### Sample input

The textarea pre-fills with the §4d worked example (revenue Metric dashboard). Edit and click Render to iterate.

## Visual-diff regression harness

The Playwright suite lives under [`samples/catalog/snapshot/`](../samples/catalog/snapshot/). See [`snapshot/README.md`](../samples/catalog/snapshot/README.md) for the full operator guide.

Short form:

```powershell
# From Fuaran/samples/catalog/:
npm run snapshot           # fail on > 0.5% pixel diff against baselines
npm run snapshot:update    # regenerate baselines after a deliberate change
```

Two suites:
- **Regression** – walks every NodeKind, snapshots the matrix at fixed viewport (1280×800), diffs against committed baselines.
- **Parity** – §4l down-shift verification. For each (Fuaran node, hand-rolled Feliz function) pair in [`samples/catalog/Parity.fs`](../samples/catalog/Parity.fs), asserts both sides match a shared baseline. If Fuaran and Feliz diverge visually, the test fails.

Threshold: 0.5% of pixels may differ. Higher than that crosses font-rendering noise into actual visual change.

## Static reference site (Phase 169)

The catalog builds to a fully static site that hosts from any file server – the
published "what can Fuaran render?" front door, no clone required.

```powershell
# From Fuaran/samples/catalog/:
npm ci                     # or: npm install
npm run build              # dotnet fable -> vite build -> dist/
npm run preview            # serve dist/ on http://localhost:14010 (Vite preview)
```

- **`npm run build`** runs `dotnet fable -o output --noCache` then `vite build`,
  emitting `dist/`. Vite's `base: "./"` makes every asset reference relative, so
  the build hosts from a sub-path (e.g. GitHub Pages' `/<repo>/`) unchanged.
- **Build pipeline entry.** `dotnet run --project Build.fsproj -- Catalog`
  runs the Fable half (dotnet-pure) as a gate – a "compiles on .NET but breaks
  under Fable" regression fails the pipeline, not just a manual browser session.

### Deep-linking (query-string routing)

Every gallery selection is reflected into the query string, so any view is a
copyable link and the static host needs **no server-side routing** (the SPA
reads `window.location.search` on load):

| Param | Values | Default |
|---|---|---|
| `kind` | any `KindEntry.Id` (e.g. `Table`) | first entry (`Metric`) |
| `theme` | `default` / `dark` / `high-contrast` / `teal-brand` / `amber-brand` | `default` |
| `locale` | `en` / `fr` / `de` | `en` |
| `a11y` | `1` to enable the a11y audit | off |

Example: `…/?kind=Callout&theme=dark&locale=fr&a11y=1`. Unknown / absent values
fall back to the first option, so a stale or hand-typed link degrades gracefully
rather than 404ing. Selecting in the UI rewrites the address bar
(`history.replaceState`) so the current view is always re-copyable.

### Copyable wire JSON + the round-trip guard

Each card's "JSON wire shape" disclosure carries a **Copy JSON** button. The JSON
is the canonical `CanonicalJson.encodeNode` projection of the *same*
`Node<unit>` the card renders – so the catalog is a by-example companion to
[`WIRE_FORMAT.md`](WIRE_FORMAT.md) and the Phase 110 authoring pack.

That promise is **build-time-guarded**: `src/Fuaran.UI.Catalog.Tests/`
compile-links the catalog's `Matrix.fs` and asserts every entry's canonical JSON
decodes back through the canonical decoder across the full
(Tone × Weight × Emphasis) sweep. It runs in the Build.fs `Test` target, so a
card whose JSON stops round-tripping fails CI – no hand-maintained fixture list.

### Publishing

[`.github/workflows/catalog-pages.yml`](../.github/workflows/catalog-pages.yml)
builds the static site on every push to `main` and uploads `dist/` as a
**non-public staging artifact** (repo-members only), keeping the lane green
before the public launch. The **public deploy is a single operator action** (registered
in roadmap Phase 161):
enable GitHub Pages for the repo, then run the workflow via *Run workflow*
(`workflow_dispatch`) – the deploy job is gated on that manual trigger, so a
routine push never publishes publicly.

## Extending the catalog when Phase 12 sessions extend the type contract

When a new `NodeKind` lands or an existing one grows:

1. **Add a `KindEntry` to [`samples/catalog/Matrix.fs`](../samples/catalog/Matrix.fs).** Hand-pick a representative spec; the Tone × Weight × Emphasis sweep is automatic. Update `expectedKindIds` so the catalog's boot-time coverage assertion catches future drift. The Phase 169 wire-JSON round-trip guard (`src/Fuaran.UI.Catalog.Tests/`) compile-links `Matrix.fs`, so a new entry is covered automatically – but if its canonical JSON does not decode back through the canonical decoder, the `Test` target fails (a real wire-format gap, not a catalog bug).
2. **Add the JSON wire-shape mapping in [`samples/catalog/JsonDecode.fs`](../samples/catalog/JsonDecode.fs)** if the AI-eval-input mode should accept the new kind. Optional – the decoder reports unsupported kinds with the §4d hint, so omitting it just narrows the eval mode.
3. **Add to the regression suite's `kinds` array in [`snapshot/regression.spec.mts`](../samples/catalog/snapshot/regression.spec.mts).** Generate the baseline with `npm run snapshot:update`.
4. **Optional: add a parity pair** in [`samples/catalog/Parity.fs`](../samples/catalog/Parity.fs) if the new kind has a load-bearing real-world authoring shape worth pinning against a hand-rolled Feliz equivalent. Add to the parity suite's `pairs` array and regenerate baselines.
5. **Update [`HOST-STYLING-CHECKLIST.md`](../HOST-STYLING-CHECKLIST.md)** if the renderer emits new class hooks for the new kind.

## Anti-patterns to avoid

Per Phase 12.S anti-pattern notes:

- **Don't bake design opinions into the catalog.** The gallery shows what the renderer produces – not what it *should* produce. Design opinions live in themes; the catalog renders themes.
- **Don't make visual diffs gate every commit.** Pixel-level diffs are noisy under font rendering / browser-version drift. Gate on intentional theme / renderer changes; let normal phase commits pass.
- **Don't take the catalog cookbook-app port range.** The catalog is a website-class allocation per the workspace port-allocation mandate, not an application or cookbook-app.

## See also

- [`HOST-STYLING-CHECKLIST.md`](../HOST-STYLING-CHECKLIST.md) – class-hook + CSS-variable contract the catalog illustrates.
- [`THEME-BRIDGE-GUIDE.md`](THEME-BRIDGE-GUIDE.md) – how consumer apps bridge their design tokens to Fuaran variables.
- [`AI_AUTHORING_GUIDE.md`](AI_AUTHORING_GUIDE.md) – the §4d wire shape an AI author emits.
- [`Fuaran/samples/catalog/snapshot/README.md`](../samples/catalog/snapshot/README.md) – Playwright harness operator guide.
- `Fuaran/roadmap/COMPLETED_PHASES.md` – Phase 12.S anchor – phase body + acceptance criteria.
