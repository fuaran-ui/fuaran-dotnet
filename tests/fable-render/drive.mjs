// Phase 2550 — runs the Fable-compiled client render suite under Node with a DOM.
//
//   node drive.mjs <compiled output dir> [--plant memo|prop|sink]
//
// Sets up jsdom's window as the global DOM, imports the compiled `Program.js`,
// and exits non-zero unless the suite published a verdict that ran at least one
// test and failed none. Exit codes: 0 green, 1 a red or vacuous suite, 2 a
// harness fault (no compiled suite, or a plant that could not be applied).
//
// `--plant` exists for `tests/fable-laws/fable-check.tests.ps1` alone: it edits
// the COMPILED renderer in place to re-create one regression the suite exists to
// catch, so that script can show the gate goes red on it. A plant that finds
// nothing to edit is a harness fault, never a pass. See README.md.
import { readdir, readFile, writeFile } from "node:fs/promises";
import { existsSync } from "node:fs";
import { join, resolve } from "node:path";
import { pathToFileURL } from "node:url";
import { JSDOM } from "jsdom";

const args = process.argv.slice(2);
const outDir = args[0];
const plantAt = args.indexOf("--plant");
const plant = plantAt >= 0 ? args[plantAt + 1] : null;

function fault(message) {
  console.error(`fable-render: ${message}`);
  process.exit(2);
}

if (!outDir) fault("usage: node drive.mjs <compiled output dir> [--plant memo|prop|sink]");
const entry = resolve(outDir, "Program.js");
if (!existsSync(entry)) fault(`no compiled suite at ${entry}`);

// ── The plants ────────────────────────────────────────────────────────────
// Each names the compiled file it edits, a pattern that must match exactly once
// in exactly one such file, its replacement, and an optional tail appended to
// the file. They re-create the three regressions the suite's claims rule out,
// at the place in the renderer each would actually happen.
const plants = {
  // A memo that returns a fresh element: the decision never hits.
  memo: {
    file: "Render.js",
    find: /(export function SubtreeMemo_hit\([^)]*\)\s*\{)/,
    replace: "$1\n    return undefined;",
  },
  // A `dangerouslySetInnerHTML` object built per emit: the per-markup table is
  // emptied before every lookup.
  prop: {
    file: "Render.js",
    find: /(function Emit_innerHtml\(markup\)\s*\{)/,
    replace: "$1\n    Emit_innerHtmlProps.clear();",
  },
  // A sink reparsed on re-render: the markdown markup differs per render, as it
  // would if the sanitiser stamped a fresh id into it.
  sink: {
    file: "Markdown.js",
    find: /export function toHtmlCited\(/,
    replace: "function toHtmlCited_unplanted(",
    append:
      "\nlet plantNonce = 0;\nexport function toHtmlCited(...args) {\n    return toHtmlCited_unplanted(...args) + `<!--${++plantNonce}-->`;\n}\n",
  },
};

async function findCompiled(name) {
  // Fable mirrors referenced projects under the output directory; the file is
  // found by name rather than by a path that depends on that layout.
  const stack = [resolve(outDir)];
  const hits = [];
  while (stack.length) {
    const dir = stack.pop();
    for (const e of await readdir(dir, { withFileTypes: true })) {
      if (e.isDirectory()) stack.push(join(dir, e.name));
      else if (e.name === name) hits.push(join(dir, e.name));
    }
  }
  return hits;
}

if (plant) {
  const spec = plants[plant];
  if (!spec) fault(`unknown plant '${plant}' (memo | prop | sink)`);
  const edits = [];
  for (const file of await findCompiled(spec.file)) {
    const text = await readFile(file, "utf8");
    const count = (text.match(new RegExp(spec.find.source, "g")) ?? []).length;
    if (count > 1) fault(`plant '${plant}': ${file} matches ${count} times, expected once`);
    if (count === 1) edits.push([file, text.replace(spec.find, spec.replace) + (spec.append ?? "")]);
  }
  if (edits.length !== 1) fault(`plant '${plant}': expected one ${spec.file} to edit, found ${edits.length}`);
  const [file, body] = edits[0];
  await writeFile(file, body);
  console.log(`fable-render: PLANTED '${plant}' in ${file}`);
}

// ── The DOM ───────────────────────────────────────────────────────────────
const dom = new JSDOM("<!doctype html><html><body></body></html>", {
  url: "http://localhost/",
  pretendToBeVisual: true,
});
const { window } = dom;
for (const name of Object.getOwnPropertyNames(window)) {
  if (name in globalThis) continue;
  try {
    globalThis[name] = window[name];
  } catch {
    // A few accessors refuse to be copied; nothing the suite reads.
  }
}
globalThis.window = window;
globalThis.document = window.document;
// React reads this to decide whether to warn about updates outside act(); the
// suite commits synchronously through flushSync instead.
globalThis.IS_REACT_ACT_ENVIRONMENT = false;

await import(pathToFileURL(entry).href);

const verdict = globalThis.__fuaranRenderVerdict;
if (!verdict) {
  console.error("fable-render: the suite published no verdict - it did not run to completion");
  process.exitCode = 1;
} else if (verdict.tests === 0) {
  console.error("fable-render: the suite ran no tests - a vacuous run is a failure");
  process.exitCode = 1;
} else if (verdict.failed > 0) {
  process.exitCode = 1;
}
dom.window.close();
