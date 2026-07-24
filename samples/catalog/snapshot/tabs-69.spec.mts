import { test, expect, Page } from "@playwright/test";

// Phase 69 — explicit headers + ARIA / keyboard navigation + typed-tag
// overlay headless invariants.
//
// The catalog page at `?tabs-69=1` mounts the Tabs69 fixture: three tabs
// (Overview / Detail / Audit) wired through `Fuaran.tabsTagged`. Overview
// is a typed Fuaran body; Detail is a `NodeKind.Custom`-wrapped Feliz
// fragment registered via `IFuaranRuntime.RegisterCustomRenderer`; Audit
// carries `TabHeader.Disabled = Some(Static true)`.
//
// The spec probes four orthogonal contracts:
//
//   1. ARIA tablist emission — role / aria-selected / aria-controls /
//      aria-labelledby / tabindex / role=tabpanel.
//   2. Keyboard navigation — ArrowRight / ArrowLeft / Home / End move
//      focus + active state; the disabled Audit tab is SKIPPED during
//      arrow traversal.
//   3. Tag-overlay round-trip — TabsSpec.OnSelectTag fires with the
//      per-tab string tag; the model-side mirror reflects it.
//   4. Custom-escape — clicking the Detail tab renders the
//      Custom-registered Feliz fragment with its stable data-testid.

const URL = "/?tabs-69=1";

const waitForPage = async (page: Page) => {
  await page.locator("#tabs-69-page").waitFor();
};

const tablist = (page: Page) => page.locator("[role='tablist']");
const tabByIndex = (page: Page, i: number) =>
  page.locator(`[role='tab'][data-tab-index='${i}']`);
const tabPanel = (page: Page) => page.locator("[role='tabpanel']");
const activeTagMirror = (page: Page) =>
  page.locator("[data-testid='tabs-69-active-tag-mirror']");
const detailPane = (page: Page) =>
  page.locator("[data-testid='tabs-69-detail-pane']");

// ─── 1. ARIA tablist emission ─────────────────────────────────────────────

test("emits role=tablist + role=tab + aria-* per the W3C tablist pattern", async ({
  page,
}) => {
  await page.goto(URL);
  await waitForPage(page);

  // tablist container
  await expect(tablist(page)).toHaveAttribute("aria-orientation", "horizontal");

  // Three tab buttons.
  await expect(tabByIndex(page, 0)).toHaveText(/Overview/);
  await expect(tabByIndex(page, 1)).toHaveText(/Detail/);
  await expect(tabByIndex(page, 2)).toHaveText(/Audit/);

  // Overview is the initial active tab.
  await expect(tabByIndex(page, 0)).toHaveAttribute("aria-selected", "true");
  await expect(tabByIndex(page, 1)).toHaveAttribute("aria-selected", "false");
  await expect(tabByIndex(page, 2)).toHaveAttribute("aria-selected", "false");

  // Roving tabindex — active = 0, inactive = -1.
  await expect(tabByIndex(page, 0)).toHaveAttribute("tabindex", "0");
  await expect(tabByIndex(page, 1)).toHaveAttribute("tabindex", "-1");
  await expect(tabByIndex(page, 2)).toHaveAttribute("tabindex", "-1");

  // aria-controls + aria-labelledby reference stable IDs derived from the
  // parent NodeId + tab index.
  await expect(tabByIndex(page, 0)).toHaveAttribute(
    "aria-controls",
    "tabs-69-panel-0",
  );
  await expect(tabByIndex(page, 0)).toHaveAttribute("id", "tabs-69-tab-0");

  // Active panel — role=tabpanel + aria-labelledby + tabindex=0.
  await expect(tabPanel(page)).toHaveAttribute(
    "aria-labelledby",
    "tabs-69-tab-0",
  );
  await expect(tabPanel(page)).toHaveAttribute("id", "tabs-69-panel-0");
  await expect(tabPanel(page)).toHaveAttribute("tabindex", "0");
});

test("Audit tab carries aria-disabled and the disabled-class", async ({
  page,
}) => {
  await page.goto(URL);
  await waitForPage(page);

  await expect(tabByIndex(page, 2)).toHaveAttribute("aria-disabled", "true");
  await expect(tabByIndex(page, 2)).toHaveClass(/fuaran-tab-disabled/);
});

// ─── 2. Keyboard navigation ───────────────────────────────────────────────

test("ArrowRight moves focus + active to the next tab; ArrowLeft moves back", async ({
  page,
}) => {
  await page.goto(URL);
  await waitForPage(page);

  // Focus the active tab (Overview) — the standard ARIA pattern is to
  // enter the tab list via Tab, landing on whichever tab carries tabindex=0.
  await tabByIndex(page, 0).focus();

  // ArrowRight → Detail.
  await page.keyboard.press("ArrowRight");
  await expect(tabByIndex(page, 1)).toHaveAttribute("aria-selected", "true");
  await expect(activeTagMirror(page)).toHaveText("Active tag: detail");

  // ArrowLeft → Overview.
  await page.keyboard.press("ArrowLeft");
  await expect(tabByIndex(page, 0)).toHaveAttribute("aria-selected", "true");
  await expect(activeTagMirror(page)).toHaveText("Active tag: overview");
});

test("ArrowRight from Detail skips the disabled Audit tab and wraps to Overview", async ({
  page,
}) => {
  await page.goto(URL);
  await waitForPage(page);

  // Move from Overview to Detail first.
  await tabByIndex(page, 0).focus();
  await page.keyboard.press("ArrowRight");
  await expect(tabByIndex(page, 1)).toHaveAttribute("aria-selected", "true");

  // ArrowRight from Detail — Audit is disabled, so the next enabled tab is
  // Overview (wrap-around).
  await page.keyboard.press("ArrowRight");
  await expect(tabByIndex(page, 0)).toHaveAttribute("aria-selected", "true");
  await expect(activeTagMirror(page)).toHaveText("Active tag: overview");
});

test("Home and End jump to the first / last enabled tab", async ({ page }) => {
  await page.goto(URL);
  await waitForPage(page);

  await tabByIndex(page, 0).focus();

  // End → last enabled tab. Audit (index 2) is disabled, so End lands on
  // Detail (index 1).
  await page.keyboard.press("End");
  await expect(tabByIndex(page, 1)).toHaveAttribute("aria-selected", "true");

  // Home → first enabled tab (Overview).
  await page.keyboard.press("Home");
  await expect(tabByIndex(page, 0)).toHaveAttribute("aria-selected", "true");
});

// ─── 3. Tag-overlay round-trip ────────────────────────────────────────────

test("clicking a tab fires OnSelectTag with the per-tab string tag", async ({
  page,
}) => {
  await page.goto(URL);
  await waitForPage(page);

  // Initial: Overview is active.
  await expect(activeTagMirror(page)).toHaveText("Active tag: overview");

  // Click Detail — OnSelectTag fires with "detail", model updates,
  // the mirror reflects.
  await tabByIndex(page, 1).click();
  await expect(activeTagMirror(page)).toHaveText("Active tag: detail");
  await expect(tabByIndex(page, 1)).toHaveAttribute("aria-selected", "true");
});

test("clicking the disabled Audit tab does not change active state", async ({
  page,
}) => {
  await page.goto(URL);
  await waitForPage(page);

  // Force a click on the disabled tab; the renderer's onClick guard skips.
  await tabByIndex(page, 2).click({ force: true });
  await expect(activeTagMirror(page)).toHaveText("Active tag: overview");
  await expect(tabByIndex(page, 0)).toHaveAttribute("aria-selected", "true");
});

// ─── 4. Custom-escape — Detail tab renders the registered Feliz body ──────

test("Detail tab renders the NodeKind.Custom-registered Feliz fragment", async ({
  page,
}) => {
  await page.goto(URL);
  await waitForPage(page);

  // Initially Overview is active — Detail pane is NOT mounted (the renderer
  // only mounts the active child).
  await expect(detailPane(page)).toHaveCount(0);

  // Activate Detail.
  await tabByIndex(page, 1).click();
  await expect(detailPane(page)).toHaveCount(1);
  await expect(detailPane(page)).toContainText(
    "Detail (Custom-wrapped Feliz body)",
  );
});
