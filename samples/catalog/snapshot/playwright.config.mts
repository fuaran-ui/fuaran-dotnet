import { defineConfig } from "@playwright/test";

// Visual-diff regression harness for the Fuaran catalog (Phase 12.S).
//
// Two suites:
//   - regression.spec.mts — walks every NodeKind page, snapshots the
//     active card matrix at fixed viewport.
//   - parity.spec.mts — pixel-diffs the Fuaran emission against the
//     hand-rolled Feliz equivalent for each pair declared in
//     Fuaran/samples/catalog/Parity.fs.
//
// The default-theme pass is the regression baseline; theme-switched
// passes can be added per the operator's discretion (a brand-themed
// snapshot run is the obvious extension when consumer rebranding ships).
//
// Threshold: 0.5% of pixels may differ before a test fails (per the
// phase body's "CI fails on per-pixel diff > 0.5%"). Anti-pattern
// guard: don't tighten this below 0.5% — font rendering / browser-
// version drift produces sub-pixel noise that crosses 0.2% routinely.

export default defineConfig({
  testDir: ".",
  fullyParallel: true,
  retries: 0,
  reporter: [["list"], ["html", { open: "never" }]],
  use: {
    // The catalog's vite dev port (see vite.config.mts `server.port`). Was
    // 23920 — a stale value matching neither the dev (24010) nor preview
    // (14010) port; corrected in Phase 58 when the responsive harness needed
    // to actually reach a running server.
    baseURL: "http://localhost:24010",
    viewport: { width: 1280, height: 800 },
    deviceScaleFactor: 1,
  },
  expect: {
    toHaveScreenshot: {
      // 0.5% of total pixels may differ. `maxDiffPixelRatio` is the
      // ratio-based gate; `threshold` is per-pixel colour tolerance
      // (0..1, default 0.2 — kept at default).
      maxDiffPixelRatio: 0.005,
    },
  },
  projects: [
    {
      name: "chromium",
      use: { browserName: "chromium" },
    },
  ],
  // The catalog must be running on http://localhost:24010 before
  // `npm run snapshot`. Operators start it via `npm run dev` in a
  // sibling terminal (or `npm run preview` after a `vite build`).
});
