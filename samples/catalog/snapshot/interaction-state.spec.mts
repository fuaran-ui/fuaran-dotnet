import { test, expect, Page } from "@playwright/test";

// Phase 12.N — interaction-state token-surface override propagation.
//
// For each button variant × interaction-state combination the post-12.N
// reference CSS consumes, this spec asserts that a consumer-bridge
// `:root` block successfully overrides the resolved `getComputedStyle()`
// value. The page lives at `?interaction-state=1` (no bridge) or
// `?interaction-state=1&bridge=on` (bridge active); the catalog's
// `InteractionStates.fs` module emits the bridge `<style>` AFTER the
// renderer's `themeStyleElement` so the bridge wins the cascade.
//
// Per-pixel rgb assertions — distinct from `regression.spec.mts` (which
// pixel-diffs entire matrix pages) and `parity.spec.mts` (which pixel-
// diffs Fuaran vs hand-rolled Feliz). The three suites are complementary:
// this one verifies the typed-variable contract; the other two verify
// rendered output.

const BASELINE = "/?interaction-state=1";
const BRIDGED = "/?interaction-state=1&bridge=on";

// Mirrors `InteractionStates.bridgeStylesheet` — keep in sync. Distinct
// RGB values per slot avoid false positives.
const overrides = {
  brandHoverFg: "rgb(11, 22, 33)",
  brandHoverBg: "rgb(44, 55, 66)",
  defaultHoverBg: "rgb(77, 88, 99)",
  defaultHoverBorder: "rgb(108, 119, 130)",
  criticalHoverFg: "rgb(141, 152, 163)",
  brandActiveFg: "rgb(174, 185, 196)",
  brandActiveBg: "rgb(207, 218, 229)",
  defaultActiveBg: "rgb(240, 251, 6)",
  defaultActiveBorder: "rgb(17, 28, 39)",
  criticalActiveFg: "rgb(50, 61, 72)",
  brandDisabledFg: "rgb(83, 94, 105)",
  defaultDisabledBg: "rgb(116, 127, 138)",
  defaultDisabledFg: "rgb(149, 160, 171)",
  defaultDisabledBorder: "rgb(182, 193, 204)",
  criticalDisabledFg: "rgb(215, 226, 237)",
  focusRingColor: "rgb(12, 24, 36)",
};

// Reference CSS / `Defaults.theme` fallback values (post-12.N).
const fallbacks = {
  brandHoverFg: "rgb(30, 64, 175)", // #1e40af
  brandHoverBg: "rgb(219, 234, 254)", // #dbeafe
  defaultHoverBg: "rgb(249, 250, 251)", // #f9fafb
  defaultHoverBorder: "rgb(209, 213, 219)", // #d1d5db
  criticalHoverFg: "rgb(153, 27, 27)", // #991b1b
  brandActiveFg: "rgb(30, 58, 138)", // #1e3a8a
  brandActiveBg: "rgb(191, 219, 254)", // #bfdbfe
  defaultActiveBg: "rgb(243, 244, 246)", // #f3f4f6
  defaultActiveBorder: "rgb(156, 163, 175)", // #9ca3af
  criticalActiveFg: "rgb(127, 29, 29)", // #7f1d1d
  brandDisabledFg: "rgb(107, 114, 128)", // #6b7280
  defaultDisabledBg: "rgb(243, 244, 246)",
  defaultDisabledFg: "rgb(107, 114, 128)",
  defaultDisabledBorder: "rgb(209, 213, 219)",
  criticalDisabledFg: "rgb(107, 114, 128)",
};

// ─── Probe helpers ────────────────────────────────────────────────────

type ProbeFn = (page: Page, testId: string, prop: string) => Promise<string>;

const probeStatic: ProbeFn = async (page, testId, prop) =>
  page
    .locator(`[data-testid='${testId}']`)
    .evaluate((el, p) => getComputedStyle(el as Element).getPropertyValue(p), prop);

const probeHover: ProbeFn = async (page, testId, prop) => {
  await page.locator(`[data-testid='${testId}']`).hover();
  return probeStatic(page, testId, prop);
};

const probeActive: ProbeFn = async (page, testId, prop) => {
  const locator = page.locator(`[data-testid='${testId}']`);
  const box = await locator.boundingBox();
  if (!box) throw new Error(`no bounding box for ${testId}`);
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  await page.mouse.down();
  try {
    return await locator.evaluate(
      (el, p) => getComputedStyle(el as Element).getPropertyValue(p),
      prop,
    );
  } finally {
    await page.mouse.up();
  }
};

// ─── Assertion table ──────────────────────────────────────────────────

interface Check {
  name: string;
  probe: ProbeFn;
  testId: string;
  property: string;
  fallback: string;
  override: string;
}

const checks: Check[] = [
  // hover — .fuaran-button-{variant}:hover consumption per fuaran-reference.css
  {
    name: "primary:hover background → brand-hover-fg",
    probe: probeHover,
    testId: "btn-primary",
    property: "background-color",
    fallback: fallbacks.brandHoverFg,
    override: overrides.brandHoverFg,
  },
  {
    name: "secondary:hover background → default-hover-bg",
    probe: probeHover,
    testId: "btn-secondary",
    property: "background-color",
    fallback: fallbacks.defaultHoverBg,
    override: overrides.defaultHoverBg,
  },
  {
    name: "secondary:hover border → default-hover-border",
    probe: probeHover,
    testId: "btn-secondary",
    property: "border-top-color",
    fallback: fallbacks.defaultHoverBorder,
    override: overrides.defaultHoverBorder,
  },
  {
    // The canonical acceptance criterion: a consumer that overrides
    // --fuaran-tone-brand-hover-bg inverts the brand button's hover bg.
    name: "tertiary:hover background → brand-hover-bg (acceptance criterion)",
    probe: probeHover,
    testId: "btn-tertiary",
    property: "background-color",
    fallback: fallbacks.brandHoverBg,
    override: overrides.brandHoverBg,
  },
  {
    name: "destructive:hover background → critical-hover-fg",
    probe: probeHover,
    testId: "btn-destructive",
    property: "background-color",
    fallback: fallbacks.criticalHoverFg,
    override: overrides.criticalHoverFg,
  },
  // active — .fuaran-button-{variant}:active consumption
  {
    name: "primary:active background → brand-active-fg",
    probe: probeActive,
    testId: "btn-primary",
    property: "background-color",
    fallback: fallbacks.brandActiveFg,
    override: overrides.brandActiveFg,
  },
  {
    name: "secondary:active background → default-active-bg",
    probe: probeActive,
    testId: "btn-secondary",
    property: "background-color",
    fallback: fallbacks.defaultActiveBg,
    override: overrides.defaultActiveBg,
  },
  {
    name: "tertiary:active background → brand-active-bg",
    probe: probeActive,
    testId: "btn-tertiary",
    property: "background-color",
    fallback: fallbacks.brandActiveBg,
    override: overrides.brandActiveBg,
  },
  {
    name: "destructive:active background → critical-active-fg",
    probe: probeActive,
    testId: "btn-destructive",
    property: "background-color",
    fallback: fallbacks.criticalActiveFg,
    override: overrides.criticalActiveFg,
  },
  // disabled — static read; the testId targets the explicitly-disabled
  // mirror element so :disabled applies without interaction.
  {
    name: "primary:disabled background → brand-disabled-fg",
    probe: probeStatic,
    testId: "btn-primary-disabled",
    property: "background-color",
    fallback: fallbacks.brandDisabledFg,
    override: overrides.brandDisabledFg,
  },
  {
    name: "secondary:disabled background → default-disabled-bg",
    probe: probeStatic,
    testId: "btn-secondary-disabled",
    property: "background-color",
    fallback: fallbacks.defaultDisabledBg,
    override: overrides.defaultDisabledBg,
  },
  {
    name: "secondary:disabled color → default-disabled-fg",
    probe: probeStatic,
    testId: "btn-secondary-disabled",
    property: "color",
    fallback: fallbacks.defaultDisabledFg,
    override: overrides.defaultDisabledFg,
  },
  {
    name: "destructive:disabled background → critical-disabled-fg",
    probe: probeStatic,
    testId: "btn-destructive-disabled",
    property: "background-color",
    fallback: fallbacks.criticalDisabledFg,
    override: overrides.criticalDisabledFg,
  },
];

const waitForPage = async (page: Page) => {
  await page.locator("#interaction-state-page").waitFor();
};

for (const check of checks) {
  test(`baseline: ${check.name}`, async ({ page }) => {
    await page.goto(BASELINE);
    await waitForPage(page);
    const value = await check.probe(page, check.testId, check.property);
    expect(value.trim()).toBe(check.fallback);
  });

  test(`bridged: ${check.name}`, async ({ page }) => {
    await page.goto(BRIDGED);
    await waitForPage(page);
    await page.locator("[data-testid='bridge-stylesheet']").waitFor();
    const value = await check.probe(page, check.testId, check.property);
    expect(value.trim()).toBe(check.override);
  });
}

// ─── Focus-ring colour — variable-surface read ────────────────────────
//
// `:focus-visible` is gated by Chromium's keyboard-modality heuristic, and
// programmatic `.focus()` doesn't reliably trigger it under Playwright.
// The cleanest cascade-propagation check is therefore a direct variable
// read on `:root` — proves the bridge wins the cascade for the global
// ring colour. The reference CSS's outline rule
// `outline: ... var(--fuaran-focus-ring-color)` consumes the variable, so
// override propagation is mechanically equivalent.

test("baseline: --fuaran-focus-ring-color resolves to Defaults.theme fallback", async ({
  page,
}) => {
  await page.goto(BASELINE);
  await waitForPage(page);
  const value = await page.evaluate(() =>
    getComputedStyle(document.documentElement)
      .getPropertyValue("--fuaran-focus-ring-color")
      .trim(),
  );
  expect(value).toBe("#93c5fd");
});

test("bridged: --fuaran-focus-ring-color resolves to consumer-bridge override", async ({
  page,
}) => {
  await page.goto(BRIDGED);
  await waitForPage(page);
  await page.locator("[data-testid='bridge-stylesheet']").waitFor();
  const value = await page.evaluate(() =>
    getComputedStyle(document.documentElement)
      .getPropertyValue("--fuaran-focus-ring-color")
      .trim(),
  );
  expect(value).toBe(overrides.focusRingColor);
});

// ─── Surface-completeness check — every 12.N variable is declared ─────
//
// Catches regressions where a refactor accidentally drops a variable
// from `Defaults.theme` / `Theme.toCss` or the reference CSS `:root`
// block. The expected set is the 84-element tone × state × slot matrix
// plus the 4 focus-ring globals — 88 variables total, per
// `HOST-STYLING-CHECKLIST.md` §1.6.

test("post-12.N :root declares every interaction-state variable", async ({ page }) => {
  await page.goto(BASELINE);
  await waitForPage(page);

  const tones = ["default", "subdued", "brand", "success", "warning", "critical", "info"];
  const states = ["hover", "focus", "active", "disabled"];
  const slots = ["bg", "fg", "border"];
  const expected: string[] = [];
  for (const t of tones)
    for (const s of states) for (const slot of slots) expected.push(`--fuaran-tone-${t}-${s}-${slot}`);
  expected.push(
    "--fuaran-focus-ring-color",
    "--fuaran-focus-ring-width",
    "--fuaran-focus-ring-offset",
    "--fuaran-focus-ring-style",
  );

  const missing = await page.evaluate((names) => {
    const cs = getComputedStyle(document.documentElement);
    return names.filter((n) => cs.getPropertyValue(n).trim() === "");
  }, expected);

  expect(missing).toEqual([]);
  // Sanity: 7 tones × 4 states × 3 slots + 4 globals = 88. If the matrix
  // dimensions ever shift, the literal here surfaces the change loudly.
  expect(expected.length).toBe(88);
});
