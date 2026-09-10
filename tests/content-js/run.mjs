// ============================================================================
//  The shipped-browser-script harness (Phase 1648).
//
//  Runs the three JavaScript files this repo ships to a reader's browser
//  against the DOM stub beside this file, and asserts what they DO. Before this
//  harness they had no behavioural coverage at all: Phase 1532 could add only a
//  source-shape guard, and the defect that motivated the harness — a
//  server-driven `ReadFileBody` that resolved to a `<label>`, found no `files`
//  and broke out in silence — has a perfectly ordinary shape.
//
//  Node stdlib only (`vm`, `fs`, `assert`). No npm install exists on this side
//  of the repo and this must not create one.
//
//  Run:  node tests/content-js/run.mjs
//  Exit: 0 all green · 1 a failure, named
//
//  ── The go-red half ────────────────────────────────────────────────────────
//  `--self-test` perturbs each subject in memory and asserts the harness turns
//  RED. A harness whose falsifier is unnamed is not a check, and a DOM stub is
//  exactly the kind of test double that can pass by understanding nothing. The
//  gate runs both halves.
// ============================================================================

import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import vm from "node:vm";
import assert from "node:assert/strict";

import { StubElement, StubDocument, StubFileReader, stubFile } from "./dom-stub.mjs";

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = join(here, "..", "..");

const SUBJECTS = {
    liveShim: join(repoRoot, "src", "Fuaran.UI.ServerDriven", "content", "fuaran-live-patch.js"),
    imageExpand: join(repoRoot, "src", "Fuaran.UI.Renderer", "content", "fuaran-image-expand.js"),
    referenceTables: join(repoRoot, "src", "Fuaran.UI.Renderer", "content", "fuaran-reference-tables.js")
};

/** Build a browser-ish context and evaluate `source` in it. Returns the context. */
function loadInto(source, { document = new StubDocument(), extras = {} } = {}) {
    const consoleErrors = [];
    const sandbox = {
        document,
        FileReader: StubFileReader,
        setTimeout,
        clearTimeout,
        console: {
            log: () => {},
            warn: () => {},
            error: (...args) => consoleErrors.push(args.join(" "))
        },
        MutationObserver: class {
            constructor(cb) {
                this.cb = cb;
            }
            observe() {}
            disconnect() {}
        },
        ...extras
    };
    sandbox.window = sandbox;
    sandbox.globalThis = sandbox;
    sandbox.self = sandbox;
    const context = vm.createContext(sandbox);
    vm.runInContext(source, context, { filename: "subject.js" });
    context.__consoleErrors = consoleErrors;
    return context;
}

const read = (key) => readFileSync(SUBJECTS[key], "utf8");

// ─── The cases ──────────────────────────────────────────────────────────────
//
// Each case is `{ name, run }`. `run` throws to fail. `mutate` (optional) is
// the perturbation `--self-test` applies to the subject source: the case MUST
// go red under it, or the case is not testing what it claims.

const cases = [
    {
        name: "ReadFileBody reads the body from the upload's own <input type=file>",
        subject: "liveShim",
        // The old resolution: take whatever `byId` answered with. Under this the
        // wrapper <label> has no `files`, so nothing is sent.
        mutate: (src) =>
            src.replace(
                /var host = byId\(fx\.nodeId\);[\s\S]*?var file = input\.files && input\.files\[0\];/,
                "var input = byId(fx.nodeId);\n        var file = input && input.files && input.files[0];"
            ),
        run: (source) => {
            const document = new StubDocument();

            // The markup BOTH SSR hosts emit: the node marker on the wrapper
            // <label>, the file control inside it. This shape is the whole
            // point of the case — it is why the old `byId` resolution failed.
            const wrapper = new StubElement("label", {
                "data-fuaran-node-id": "upload-1",
                class: "fuaran-file-upload"
            });
            const input = new StubElement("input", { type: "file", class: "fuaran-file-upload-input" });
            input.files = [stubFile("notes.txt", "hello from the reader")];
            input.files.length = 1;
            wrapper.appendChild(input);
            document.body.appendChild(wrapper);

            const context = loadInto(source, { document });
            const sent = [];
            context.window.FuaranLive.performEffects(
                [{ kind: "ReadFileBody", nodeId: "upload-1", encoding: "Text" }],
                (event) => sent.push(event)
            );

            assert.equal(sent.length, 1, "exactly one LiveEvent is sent back for one ReadFileBody effect");
            assert.equal(sent[0].nodeId, "upload-1");
            assert.equal(sent[0].event, "file-read");
            assert.equal(sent[0].payload.body, "hello from the reader", "the BODY is what the continuation consumes");
            assert.equal(sent[0].payload.encoding, "Text");
            assert.equal(sent[0].payload.count, 1, "the selection's shape rides with the body (Phase 1548)");
            assert.equal(sent[0].payload.size, 21);
        }
    },
    {
        name: "ReadFileBody on a node with no file control REFUSES LOUDLY",
        subject: "liveShim",
        // Removing the console.error is exactly the silence this case exists to
        // forbid — and it must be visible as a failure, not as a quieter pass.
        mutate: (src) => src.replace(/global\.console && global\.console\.error/, "false"),
        run: (source) => {
            const document = new StubDocument();
            const wrapper = new StubElement("div", { "data-fuaran-node-id": "not-an-upload" });
            document.body.appendChild(wrapper);

            const context = loadInto(source, { document });
            const sent = [];
            context.window.FuaranLive.performEffects(
                [{ kind: "ReadFileBody", nodeId: "not-an-upload", encoding: "Text" }],
                (event) => sent.push(event)
            );

            assert.equal(sent.length, 0, "nothing is sent — there is no body to send");
            assert.equal(
                context.__consoleErrors.length,
                1,
                "and the shim SAYS SO. Breaking out silently is the defect Phase 1648 fixed: a continuation that never fires, with no observable anywhere"
            );
            assert.match(context.__consoleErrors[0], /ReadFileBody/);
            assert.match(context.__consoleErrors[0], /not-an-upload/, "the message names the node, which is the diagnosis");
        }
    },
    {
        name: "ReadFileBody on a resolved input with NO selection stays quiet",
        subject: "liveShim",
        run: (source) => {
            const document = new StubDocument();
            const wrapper = new StubElement("label", { "data-fuaran-node-id": "upload-2" });
            const input = new StubElement("input", { type: "file" });
            input.files = [];
            wrapper.appendChild(input);
            document.body.appendChild(wrapper);

            const context = loadInto(source, { document });
            const sent = [];
            context.window.FuaranLive.performEffects(
                [{ kind: "ReadFileBody", nodeId: "upload-2", encoding: "Text" }],
                (event) => sent.push(event)
            );

            assert.equal(sent.length, 0, "nothing selected, nothing sent");
            assert.equal(
                context.__consoleErrors.length,
                0,
                "and NO error — the reader simply has not picked a file yet, which is the one outcome the old silence was right about"
            );
        }
    },
    {
        name: "the image-expand enhancement binds one click listener, and only one across two loads",
        subject: "imageExpand",
        mutate: (src) => src.replace(/if \(document\.documentElement\.getAttribute\(BOUND\) === 'true'\) \{\s*return;\s*\}/, ""),
        run: (source) => {
            const document = new StubDocument();
            loadInto(source, { document });
            assert.equal(document.listenerCount("click"), 1, "one delegated listener after the first load");

            // A page that both bundles the file and follows the README's
            // <script src> line loads it twice. That is ordinary, and it used to
            // bind twice — after which one click ran the expansion twice.
            loadInto(source, { document });
            assert.equal(
                document.listenerCount("click"),
                1,
                "still ONE after a second load — the double-load guard leaves the first load's listener in charge"
            );
            assert.equal(document.documentElement.getAttribute("data-fuaran-image-expand-bound"), "true");
        }
    },
    {
        name: "the table-sort enhancement observes once across two loads",
        subject: "referenceTables",
        mutate: (src) => src.replace(/document\.documentElement\.setAttribute\(OBSERVED, 'true'\);/, ""),
        run: (source) => {
            const document = new StubDocument();
            loadInto(source, { document });
            assert.equal(
                document.documentElement.getAttribute("data-fuaran-tables-observed"),
                "true",
                "the document-level marker is set on the first load"
            );

            // The per-table marker already made a second observer harmless; the
            // document marker makes it absent. Loading twice must not change
            // the marker or throw.
            loadInto(source, { document });
            assert.equal(document.documentElement.getAttribute("data-fuaran-tables-observed"), "true");
        }
    }
];

// ─── The runner ─────────────────────────────────────────────────────────────

const selfTest = process.argv.includes("--self-test");
let failures = 0;

for (const c of cases) {
    const source = read(c.subject);

    if (selfTest) {
        if (!c.mutate) {
            console.log(`  ~  ${c.name} — no perturbation declared (skipped in --self-test)`);
            continue;
        }
        const perturbed = c.mutate(source);
        if (perturbed === source) {
            console.log(`  ✗  ${c.name} — the perturbation matched NOTHING, so it proves nothing`);
            failures += 1;
            continue;
        }
        let wentRed = false;
        try {
            c.run(perturbed);
        } catch {
            wentRed = true;
        }
        if (wentRed) {
            console.log(`  ✓  ${c.name} — goes red when the behaviour is removed`);
        } else {
            console.log(`  ✗  ${c.name} — STAYED GREEN against a subject with the behaviour removed`);
            failures += 1;
        }
        continue;
    }

    try {
        c.run(source);
        console.log(`  ✓  ${c.name}`);
    } catch (err) {
        console.log(`  ✗  ${c.name}`);
        console.log(`     ${err && err.message ? err.message : err}`);
        failures += 1;
    }
}

const label = selfTest ? "content-js harness (go-red self-test)" : "content-js harness";
if (failures === 0) {
    console.log(`${label}: ${cases.length} case(s), all green.`);
    process.exit(0);
}
console.log(`${label}: ${failures} of ${cases.length} case(s) FAILED.`);
process.exit(1);
