// ============================================================================
//  Fable-side apply-parity CI runner (Phase 192 follow-on).
//
//  The .NET half of the cross-pipeline gate lives in
//  `src/Fuaran.UI.Ops.Tests/ApplyParityTests.fs`: it applies every
//  `wire-format-fixtures/ops/*` corpus op to `ApplyParity.baseTree` and asserts
//  the canonical-JSON projection against the committed oracle
//  (`src/Fuaran.UI.Ops.Tests/apply-parity.golden.json`), plus a drift guard
//  that the golden's embedded `op` bytes still match the corpus fixture bytes.
//
//  This is the Fable half, automated. It imports the Fable-built
//  `ApplyParity.evalOp` (the SAME pure module the .NET test exercises, compiled
//  through the Fable pipeline), replays each op, and asserts the result is
//  byte-identical to the SAME golden the .NET side asserts against. A
//  Fable-vs-.NET divergence (e.g. a float-formatter or coercion regression in
//  the Fable mirror) therefore fails CI, not just a manual browser session.
//
//  Why read the ops from the golden rather than the corpus directly: the corpus
//  (`wire-format-fixtures/`) lives in the workspace repo, NOT in this repo, so a
//  fuaran-local CI lane would otherwise need a cross-repo checkout + token. The
//  golden embeds each op's exact bytes, and the .NET suite's drift guard pins
//  `golden.op === corpus op` — so replaying `golden.op` here IS replaying the
//  corpus ops, with zero cross-repo dependency. The lane is live on every push,
//  not inert pending a secret.
//
//  Pure Node ESM — `ApplyParity.js` imports only the Ops engine + canonical
//  encoder + types (no React / DOM / Elmish), so no JSDOM or browser is needed.
//  Run after `dotnet fable ApplyDemo.fsproj -o output` has produced the output:
//      node parity-runner.mjs
//  Exits non-zero on any divergence or an empty golden.
// ============================================================================

import { readFileSync } from "node:fs";
import { fileURLToPath, pathToFileURL } from "node:url";
import { dirname, join } from "node:path";

const here = dirname(fileURLToPath(import.meta.url));

// The Fable output module exposing the shared, pipeline-neutral `evalOp`.
const fableOutput = join(here, "output", "ApplyParity.js");
// The committed oracle both pipelines assert against (self-contained: each row
// carries { name, op, expected }).
const goldenPath = join(here, "..", "..", "src", "Fuaran.UI.Ops.Tests", "apply-parity.golden.json");

const { evalOp } = await import(pathToFileURL(fableOutput).href);

const golden = JSON.parse(readFileSync(goldenPath, "utf8"));

let failures = 0;
const fail = (msg) => {
  failures++;
  console.error("FAIL " + msg);
};

if (golden.length === 0) {
  fail("apply-parity golden is empty — nothing to assert");
}

for (const { name, op, expected } of golden) {
  const got = evalOp(op);
  if (got === expected) {
    console.log("OK   " + name);
  } else {
    fail(`'${name}' Fable apply diverged from the golden\n  expected: ${expected}\n  got:      ${got}`);
  }
}

console.log(`\n${golden.length - failures} clean of ${golden.length} parity checks`);

if (failures > 0) {
  console.error(
    `\napply-parity Fable lane FAILED with ${failures} divergence(s). ` +
      "The Fable apply pipeline diverged from the committed golden. If the corpus " +
      "changed intentionally, regenerate apply-parity.golden.json (via the .NET " +
      "Fuaran.UI.Ops.Tests suite), review + commit it, then re-run the Fable build.",
  );
  process.exit(1);
}

console.log("apply-parity Fable lane: PASS");

// ============================================================================
//  Hash-digest parity lane (Phase 405). `HashChain.sha256Hex` routes through
//  the pure Fable-safe SHA-256; assert the Fable-compiled digest reproduces
//  the FIPS 180-4 vectors + a multi-byte UTF-8 case, proving the browser
//  pipeline computes the same chain bytes the .NET suite pins (its
//  HashChainTests carry the same vectors + BCL parity).
// ============================================================================

const hashChainModule = join(here, "output", "src", "Fuaran.UI.OpStream.Abstractions", "HashChain.js");
const { HashChain_sha256Hex } = await import(pathToFileURL(hashChainModule).href);

const shaVectors = [
  ["", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"],
  ["abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"],
  [
    "abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq",
    "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1",
  ],
  // multi-byte UTF-8 incl. an astral (surrogate-pair) code point — node:crypto oracle below.
  ["héllo wörld ✓ 你好 🎉", null],
];

const { createHash } = await import("node:crypto");
let shaFailures = 0;

for (const [input, pinned] of shaVectors) {
  const expected = pinned ?? createHash("sha256").update(input, "utf8").digest("hex");
  const got = HashChain_sha256Hex(input);
  if (got === expected) {
    console.log(`OK   sha256 ${JSON.stringify(input.slice(0, 24))}`);
  } else {
    shaFailures++;
    console.error(`FAIL sha256 ${JSON.stringify(input)}\n  expected: ${expected}\n  got:      ${got}`);
  }
}

if (shaFailures > 0) {
  console.error(`\nsha256-parity Fable lane FAILED with ${shaFailures} divergence(s).`);
  process.exit(1);
}

console.log("sha256-parity Fable lane: PASS");
