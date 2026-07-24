# Catalog visual-diff harness

Playwright-driven pixel-diff suite for the Fuaran catalog (Phase 12.S).

## Suites

- **`regression.spec.mts`** — walks every `NodeKind` page in the catalog and snapshots the `.catalog-matrix` element. Baselines are per-kind PNGs under `regression.spec.mts-snapshots/`.
- **`parity.spec.mts`** — §4l down-shift verification. Renders both the Fuaran emission and the hand-rolled Feliz equivalent in isolation (`?parity=<id>&side=fuaran|feliz`) and asserts each side matches a shared baseline. A divergence between Fuaran and Feliz fails CI.
- **`interaction-state.spec.mts`** — Phase 12.N override-propagation contract. Loads the interaction-state fixture (`?interaction-state=1` baseline + `?interaction-state=1&bridge=on` with a consumer-bridge `:root` block) and asserts every reference-CSS-consumed tone-state variable resolves to the bridged value under `getComputedStyle`. No baseline PNGs — assertions are per-property RGB comparisons against the values declared in `samples/catalog/InteractionStates.fs`.

## Threshold

`maxDiffPixelRatio = 0.005` (0.5%) per the phase-body anti-pattern note: "don't make visual diffs gate every commit. Pixel-level diffs are noisy under font rendering / browser-version drift. Gate on intentional theme / renderer changes."

## Usage

```powershell
# Bring up the catalog dev server in a sibling terminal (port 23920):
dotnet fable -o output --noCache --watch    # in samples/catalog/
npm run dev                                  # in samples/catalog/

# First-time baseline generation (commits PNGs under *-snapshots/):
npm run snapshot:update

# Subsequent runs — fail on > 0.5% pixel diff:
npm run snapshot
```

## Baselines + version control

- Baselines are committed alongside the spec — they're part of the regression contract, not local-only test output.
- Baselines are platform-sensitive (font rendering differs across OSes); generate them on the CI runner's platform (Linux Chromium for most setups) and treat baselines generated on the developer machine as advisory.
- Updates to baselines are explicit operator decisions per the phase note. After a deliberate theme/renderer change, run `npm run snapshot:update`, eyeball each diff in the Playwright report, and commit the new baselines in the same PR as the renderer change.

## Adding coverage

- **New NodeKind** — add to the `kinds` array in `regression.spec.mts`. Mirror the addition in `samples/catalog/Matrix.fs` (the catalog's own coverage assertion enforces the same list at boot).
- **New parity pair** — declare in `samples/catalog/Parity.fs` + add the id to the `pairs` array in `parity.spec.mts`. Generate baselines via `npm run snapshot:update`.
- **New interaction-state probe** — add a row to the `checks` table in `interaction-state.spec.mts`. If the probe needs a new `data-testid`, mirror the addition in `samples/catalog/InteractionStates.fs`. If the bridge stylesheet needs an override added (e.g. covering a previously-unprobed tone-state-slot), update both `bridgeStylesheet` in `InteractionStates.fs` and the `overrides` literal in the spec — the two are duplicated by design to keep the fixture self-contained.

See `Fuaran/docs/CATALOG.md` for the operator guide and the catalog-as-eval-harness story.
