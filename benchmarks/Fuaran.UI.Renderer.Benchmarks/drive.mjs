// Loads the built bundle in a real browser, runs the measurement, refuses a run
// that did not do its work, and writes the reading. See README.md.
//   node drive.mjs <out.json> [samples]
import { createServer } from "node:http";
import { readFile, writeFile } from "node:fs/promises";
import { cpus, platform, release, totalmem } from "node:os";
import { dirname, extname, join, normalize } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright-core";

const here = dirname(fileURLToPath(import.meta.url));
const dist = join(here, "dist");
const [out, samplesArg] = process.argv.slice(2);
if (!out) throw new Error("usage: node drive.mjs <out.json> [samples]");
const samples = Number(samplesArg ?? 40);

const types = { ".html": "text/html", ".js": "text/javascript", ".css": "text/css" };
const server = createServer(async (req, res) => {
  const path = new URL(req.url, "http://localhost").pathname;
  const file = normalize(join(dist, path === "/" ? "index.html" : path));
  try {
    const body = await readFile(file);
    // Cross-origin isolation raises performance.now() to 5 microsecond resolution.
    res.writeHead(200, {
      "content-type": types[extname(file)] ?? "application/octet-stream",
      "cross-origin-opener-policy": "same-origin",
      "cross-origin-embedder-policy": "require-corp",
    });
    res.end(body);
  } catch {
    res.writeHead(404).end();
  }
});
await new Promise((ok) => server.listen(0, "127.0.0.1", ok));
const { port } = server.address();

const browser = await chromium.launch({
  channel: process.env.FUARAN_BENCH_BROWSER ?? "msedge",
  headless: true,
  args: ["--js-flags=--expose-gc"],
});
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
const consoleErrors = [];
page.on("console", (m) => m.type() === "error" && consoleErrors.push(m.text()));
page.on("pageerror", (e) => consoleErrors.push(String(e)));
// The probe the reading's Trusted Types finding rests on: the same run with the API absent.
const trustedTypes = process.env.FUARAN_BENCH_TRUSTED_TYPES !== "off";
if (!trustedTypes) await page.addInitScript(() => Object.defineProperty(window, "trustedTypes", { value: undefined }));
await page.goto(`http://127.0.0.1:${port}/`);
await page.waitForFunction(() => typeof window.__runBench === "function", null, { timeout: 30000 }).catch((e) => {
  console.error(consoleErrors.join("\n"));
  throw e;
});
const environment = await page.evaluate(() => ({
  crossOriginIsolated: self.crossOriginIsolated,
  gc: typeof globalThis.gc === "function",
}));
page.setDefaultTimeout(0);
const raw = await page.evaluate((n) => window.__runBench(n), samples);
const browserVersion = browser.version();
await browser.close();
server.close();

const quantile = (sorted, q) => sorted[Math.min(sorted.length - 1, Math.floor(q * sorted.length))];
const round = (x) => Math.round(x * 1000) / 1000;
const summarise = (columns) =>
  Object.fromEntries(
    Object.entries(columns).map(([name, values]) => {
      const sorted = values.filter((v) => v !== null && !Number.isNaN(v)).sort((a, b) => a - b);
      const summary = sorted.length
        ? { median: round(quantile(sorted, 0.5)), p95: round(quantile(sorted, 0.95)), min: round(sorted[0]) }
        : null;
      return [name, summary];
    }),
  );

const mounted = ["writeReactive", "writePlain", "structural"];
const surfaces = raw.map((s) => {
  const { samples: isolated, ...shape } = s.isolated;
  return {
    surface: s.surface,
    shape,
    isolated: summarise(isolated),
    fullRender: summarise(s.fullRender),
    ...Object.fromEntries(mounted.map((op) => [op, summarise(s[op].samples)])),
    mutationsPerUpdate: Object.fromEntries(mounted.map((op) => [op, s[op].mutations])),
  };
});

// A run that skipped its work measures nothing.
const refuse = [];
for (const s of surfaces) {
  if (s.fullRender.elements.median < 100) refuse.push(`${s.surface}: a full render built ${s.fullRender.elements.median} elements`);
  if (s.fullRender.commits.min < 1) refuse.push(`${s.surface}: a full render committed nothing`);
  if (s.writeReactive.commits.min < 1) refuse.push(`${s.surface}: a reactive write re-rendered nothing`);
  if (s.writeReactive.domChanged.min < 1) refuse.push(`${s.surface}: a reactive write left the DOM unchanged`);
  if (s.structural.elementsMoved.min < 1) refuse.push(`${s.surface}: the structural change moved no element`);
}
if (!environment.crossOriginIsolated) refuse.push("the page is not cross-origin isolated: the clock is coarse");
if (!environment.gc) refuse.push("gc is not exposed: samples would carry each other's garbage");
if (refuse.length) {
  console.error(refuse.join("\n"));
  process.exitCode = 1;
}

const reading = {
  machine: {
    cpu: cpus()[0].model,
    logicalCores: cpus().length,
    memoryGiB: Math.round(totalmem() / 2 ** 30),
    os: `${platform()} ${release()}`,
    node: process.version,
    browser: browserVersion,
  },
  samples,
  trustedTypes,
  consoleErrors: { count: consoleErrors.length, first: consoleErrors.slice(0, 3).map((e) => e.slice(0, 300)) },
  surfaces,
};
await writeFile(out, JSON.stringify(reading, null, 2) + "\n");
console.log(`wrote ${out}`);
