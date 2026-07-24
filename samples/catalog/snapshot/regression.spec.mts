import { test, expect } from "@playwright/test";

// Regression suite: walks every NodeKind page, snapshots the active
// matrix at fixed viewport, diffs against committed baselines.
//
// Kinds list mirrors Matrix.expectedKindIds — the catalog's own
// coverage assertion (`warnMissingKinds`) logs a console.error at
// boot if any goes missing. Kept duplicated here so the spec stays
// self-contained — Playwright can't `await import` from a Fable
// output module without a Vite-side bridge.

const kinds = [
  "KPI", "Heading", "Markdown", "Badge", "Callout", "Progress", "Sparkline",
  "Spacer", "Skeleton", "LabelValueRow", "Button", "Select", "Form",
  "FormNumberRanged", "Filters", "FileUpload", "Grid", "Chart", "Table", "Map",
  "Dashboard", "Stack", "GridLayout", "SplitPanel", "Tabs", "Card", "Stepper",
  "StatList", "Disclosure", "Custom",
];

// Each test selects the kind via the side-nav button (the catalog
// stores no URL state today; deep-linking by URL would be a follow-on
// — see CATALOG.md "Extending the catalog"). Snapshots the
// `.catalog-matrix` element so the side-nav highlight and topbar
// chrome don't pollute the diff.

for (const kind of kinds) {
  test(`renders the ${kind} matrix without regression`, async ({ page }) => {
    await page.goto("/");
    await page.getByRole("button", { name: kind, exact: true }).first().click();
    const matrix = page.locator(".catalog-matrix");
    await expect(matrix).toBeVisible();
    await expect(matrix).toHaveScreenshot(`${kind}.png`);
  });
}
