import { test, expect, type Page } from "@playwright/test";

// ============================================================================
//  Phase 58 — mobile / responsive renderer.
//
//  Boots the `?viewport=mobile` catalog harness (MobileViewport.fs) at three
//  viewports and asserts the reference-CSS responsive collapse:
//
//   - phone (375):  layout grids collapse to a single column, no element is
//                   wider than the viewport (no clipped content / horizontal
//                   page scroll), and every interactive leaf reaches the 44px
//                   touch-target floor.
//   - tablet (768): dense grids reduce to at most two columns.
//   - desktop (1280): grids stay multi-column — desktop output is unchanged
//                   by the phase (the collapse is strictly width-conditional).
//
//  DOM measurements are the primary gate (robust across font / browser drift);
//  each viewport also takes a screenshot whose baseline the operator seeds on
//  the first `npm run snapshot:update`, exactly like the other catalog specs.
//
//  Nav is by URL query (like tabs-69.spec.mts); the page emits the
//  `#viewport-mobile-page` sentinel once mounted. The viewport is set
//  per-test so the shared chromium project's 1280px default (and the existing
//  regression / parity baselines) stay untouched.
// ============================================================================

const URL = "/?viewport=mobile";

async function boot(page: Page, width: number, height: number): Promise<void> {
  await page.setViewportSize({ width, height });
  await page.goto(URL);
  await page.locator("#viewport-mobile-page").waitFor();
}

/** Number of explicit grid tracks the browser computed for each rendered grid. */
async function gridTrackCounts(page: Page): Promise<number[]> {
  return page.evaluate(() =>
    [...document.querySelectorAll(".fuaran-layout-grid")].map(
      (g) => getComputedStyle(g as Element).gridTemplateColumns.split(" ").length,
    ),
  );
}

test.describe("Phase 58 responsive collapse", () => {
  test("phone (375): single-column, no overflow, 44px touch targets", async ({ page }) => {
    await boot(page, 375, 812);

    // Grids collapse to a single column.
    const tracks = await gridTrackCounts(page);
    expect(tracks.length).toBeGreaterThan(0);
    for (const t of tracks) expect(t).toBe(1);

    // No horizontal page overflow, and nothing renders wider than the viewport.
    const overflow = await page.evaluate(() => {
      const doc = document.documentElement;
      const vw = window.innerWidth;
      const tooWide = [...document.querySelectorAll("#viewport-mobile-page *")].filter(
        (el) => el.getBoundingClientRect().width > vw + 1,
      ).length;
      return { pageOverflow: doc.scrollWidth > doc.clientWidth + 1, tooWide };
    });
    expect(overflow.pageOverflow).toBe(false);
    expect(overflow.tooWide).toBe(0);

    // Interactive leaves reach the 44px touch-target floor.
    const undersized = await page.evaluate(() => {
      const sel = ".fuaran-button, .fuaran-tab, .fuaran-form-input, .fuaran-select-control";
      return [...document.querySelectorAll(sel)].filter((el) => {
        const h = el.getBoundingClientRect().height;
        return h > 0 && h < 44;
      }).length;
    });
    expect(undersized).toBe(0);

    // Wide tabular data / the tab bar carry a horizontal-scroll affordance
    // rather than overflowing the page.
    const overflowX = await page.evaluate(() =>
      [...document.querySelectorAll(".fuaran-tabs-bar, .fuaran-kind-grid, .fuaran-kind-table")].map(
        (el) => getComputedStyle(el as Element).overflowX,
      ),
    );
    expect(overflowX.length).toBeGreaterThan(0);
    for (const ox of overflowX) expect(ox).toBe("auto");

    await expect(page).toHaveScreenshot("viewport-mobile.png", { fullPage: true });
  });

  test("tablet (768): dense grids reduce to at most two columns", async ({ page }) => {
    await boot(page, 768, 1024);
    const tracks = await gridTrackCounts(page);
    expect(tracks.length).toBeGreaterThan(0);
    for (const t of tracks) expect(t).toBeLessThanOrEqual(2);
    await expect(page).toHaveScreenshot("viewport-tablet.png", { fullPage: true });
  });

  test("desktop (1280): grids stay multi-column (desktop output unchanged)", async ({ page }) => {
    await boot(page, 1280, 800);
    const tracks = await gridTrackCounts(page);
    expect(tracks.length).toBeGreaterThan(0);
    // At least one grid keeps more than one column at desktop width — proving
    // the collapse above is width-conditional, not unconditional.
    expect(Math.max(...tracks)).toBeGreaterThan(1);

    // The tab bar's scroll override does NOT apply above the sm breakpoint.
    const tabsOverflowX = await page.evaluate(() => {
      const el = document.querySelector(".fuaran-tabs-bar");
      return el ? getComputedStyle(el).overflowX : "n/a";
    });
    expect(tabsOverflowX).not.toBe("auto");

    await expect(page).toHaveScreenshot("viewport-desktop.png", { fullPage: true });
  });
});
