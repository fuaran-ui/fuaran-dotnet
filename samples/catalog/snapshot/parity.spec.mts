import { test, expect } from "@playwright/test";

// Parity suite: §4l down-shift verification. For each (Fuaran node,
// Feliz function) pair declared in Fuaran/samples/catalog/Parity.fs,
// the catalog renders both sides in isolation via
// `?parity=<id>&side=fuaran|feliz`. The harness asserts each side
// matches a shared baseline — the operator generates the baseline
// from the Feliz side first (treating Feliz as the source of truth
// for §4l portability), then re-runs to populate the Fuaran-side
// baseline from the same image. Both baselines live under
// `parity.spec.mts-snapshots/` and are committed.
//
// On subsequent runs: if Fuaran's renderer drifts visually, the Fuaran
// test fails. If the hand-rolled Feliz pair drifts, the Feliz test
// fails. If fuaran-reference.css drifts in a way that hits both,
// both fail (and the operator regenerates baselines).
//
// Pair ids mirror Parity.pairs. Adding a new pair: declare it in
// Parity.fs + here.

const pairs = [
  "chip-strip",
  "stats-list",
  "kpi-grid",
  "form",
  "tabbed-card",
  "callout-stack",
];

for (const pair of pairs) {
  test(`parity (Feliz): ${pair}`, async ({ page }) => {
    await page.goto(`/?parity=${pair}&side=feliz`);
    const locator = page.locator(`#parity-feliz-${pair}`);
    await expect(locator).toBeVisible();
    await expect(locator).toHaveScreenshot(`${pair}.png`);
  });

  test(`parity (Fuaran): ${pair}`, async ({ page }) => {
    await page.goto(`/?parity=${pair}&side=fuaran`);
    const locator = page.locator(`#parity-fuaran-${pair}`);
    await expect(locator).toBeVisible();
    // Same baseline image — the Fuaran render must visually match the
    // Feliz render. Generate the baseline from the Feliz side first
    // (run `npm run snapshot:update`), then re-run; the Fuaran test
    // either matches or fails loudly.
    await expect(locator).toHaveScreenshot(`${pair}.png`);
  });
}
