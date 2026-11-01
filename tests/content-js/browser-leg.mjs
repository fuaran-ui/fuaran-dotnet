// ============================================================================
//  The browser leg of the DomPatch frame contract (Phase 2528).
//
//  The `server-driven/` corpus family (WIRE_FORMAT.md §33) states what a frame
//  holds and what applying it does to a DOM. The .NET suite certifies the
//  producer half (DomPatchCorpusTests.fs); this certifies the applier half: it
//  runs `fuaran-live-patch.js` in a REAL headless Chromium (Chrome or Edge, not
//  the DOM stub beside this file, which cannot move focus, lay out a scroll box
//  or parse HTML) and, for every case in the family:
//
//    * lowering + apply cases: set `dom.before`, apply `patches` through the
//      shim's `FuaranLive.applyPatches`, compare the DOM with `dom.after`, and
//      check the focus, caret and scroll state the case declares (Phase 158 QW5).
//      The DOM cases run twice: with the browser's native state-preserving move
//      (`moveBefore`) and with it removed, so the shim's fallback is certified too.
//    * frames: feed the vector's `sse` bytes through the shim's own SSE adapter
//      (over a scripted EventSource) and require exactly `frame`, with the
//      adapter's sequence moved to the frame's.
//    * inbound: in `dom`, after a frame of `seq`, perform `gesture` and require
//      the shim to send exactly `body`, the bytes the server's parser is
//      certified on.
//
//  Node stdlib only - no npm install exists on this side of the repo. The
//  browser is found at FUARAN_BROWSER, else at the usual Chrome / Edge / Chromium
//  locations; none found is a FAILURE naming the variable, never a skip. The
//  corpus is FUARAN_WIRE_FIXTURES, else a `wire-format-fixtures/` sibling found
//  walking up from this repo; absent is a failure too.
//
//  Run:  node tests/content-js/browser-leg.mjs [--self-test]
//  Exit: 0 green - 1 a failure, named
//
//  `--self-test` is the go-red half: each perturbation removes one behaviour
//  from the shim in memory, and the leg must go RED on it.
// ============================================================================

import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
import { tmpdir } from "node:os";
import { delimiter, dirname, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = join(here, "..", "..");
const shimPath = join(repoRoot, "src", "Fuaran.UI.ServerDriven", "content", "fuaran-live-patch.js");

function corpusRoot() {
    const env = (process.env.FUARAN_WIRE_FIXTURES || "").trim();
    if (env) {
        if (!existsSync(join(env, "manifest.json")))
            throw new Error(`FUARAN_WIRE_FIXTURES is set to '${env}', which holds no manifest.json`);
        return env;
    }
    for (let dir = repoRoot; ; dir = dirname(dir)) {
        const candidate = join(dir, "wire-format-fixtures");
        if (existsSync(join(candidate, "manifest.json"))) return candidate;
        if (dirname(dir) === dir) break;
    }
    throw new Error("wire-format-fixtures/manifest.json not found walking up from this repo; set FUARAN_WIRE_FIXTURES to the corpus root");
}

function browserPath() {
    const env = (process.env.FUARAN_BROWSER || "").trim();
    if (env) {
        if (!existsSync(env)) throw new Error(`FUARAN_BROWSER is set to '${env}', which does not exist`);
        return env;
    }
    const fixed = [
        process.env.PROGRAMFILES && join(process.env.PROGRAMFILES, "Google", "Chrome", "Application", "chrome.exe"),
        process.env["PROGRAMFILES(X86)"] && join(process.env["PROGRAMFILES(X86)"], "Google", "Chrome", "Application", "chrome.exe"),
        process.env.LOCALAPPDATA && join(process.env.LOCALAPPDATA, "Google", "Chrome", "Application", "chrome.exe"),
        process.env["PROGRAMFILES(X86)"] && join(process.env["PROGRAMFILES(X86)"], "Microsoft", "Edge", "Application", "msedge.exe"),
        process.env.PROGRAMFILES && join(process.env.PROGRAMFILES, "Microsoft", "Edge", "Application", "msedge.exe"),
        "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
        "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"
    ].filter(Boolean);
    for (const p of fixed) if (existsSync(p)) return p;
    const names = ["google-chrome", "google-chrome-stable", "chromium", "chromium-browser", "microsoft-edge"];
    for (const dir of (process.env.PATH || "").split(delimiter))
        for (const name of names)
            if (dir && existsSync(join(dir, name))) return join(dir, name);
    throw new Error("no Chrome, Edge or Chromium found; set FUARAN_BROWSER to a Chromium-family browser executable");
}

/** The family's cases, read once. */
function readFamily() {
    const dir = join(corpusRoot(), "server-driven");
    const read = (f) => JSON.parse(readFileSync(join(dir, f), "utf8"));
    const dom = read("dom-patch-vectors.json");
    const frames = read("frame-vectors.json");
    return {
        lowering: dom.lowering,
        apply: dom.apply,
        frames: frames.frames,
        inbound: frames.inbound.filter((c) => c.gesture)
    };
}

// The harness that runs inside the page, after the shim. Writes its verdicts to
// <body data-fuaran-results> as base64 UTF-8 JSON, the one thing --dump-dom returns.
function harness() {
    var out = [];
    try {
        var data = JSON.parse(document.getElementById("fuaran-data").textContent);
        var root = document.getElementById("case-root");
        var norm = function (html) { var t = document.createElement("template"); t.innerHTML = html; return t.innerHTML; };
        var canon = function (v) {
            if (Array.isArray(v)) return "[" + v.map(canon).join(",") + "]";
            if (v && typeof v === "object")
                return "{" + Object.keys(v).sort().map(function (k) { return JSON.stringify(k) + ":" + canon(v[k]); }).join(",") + "}";
            return JSON.stringify(v);
        };
        var record = function (group, name, fn) {
            var why;
            try { why = fn(); } catch (e) { why = "threw: " + (e && e.stack || e); }
            if (document.activeElement && document.activeElement.blur) document.activeElement.blur();
            out.push({ group: group, name: name, ok: !why, why: why || null });
        };
        var describe = function (el) { return el ? el.outerHTML.slice(0, 120) : String(el); };

        var runDom = function (c) {
            root.innerHTML = c.dom.before;
            if (c.scroll) for (var sel in c.scroll.before) {
                var s = root.querySelector(sel);
                if (!s) return "scroll target absent: " + sel;
                s.scrollTop = c.scroll.before[sel];
                if (s.scrollTop !== c.scroll.before[sel]) return "the case could not scroll " + sel;
            }
            var focused = null;
            if (c.focus) {
                var f = c.focus.before;
                focused = root.querySelector(f.element);
                if (!focused) return "focus element absent: " + f.element;
                focused.focus();
                if (document.activeElement !== focused) return "the case could not focus " + f.element;
                if (f.selection) focused.setSelectionRange(f.selection[0], f.selection[1]);
                if (f.scrollTop != null) {
                    focused.scrollTop = f.scrollTop;
                    if (focused.scrollTop !== f.scrollTop) return "the case could not scroll the focused element";
                }
            }
            FuaranLive.applyPatches(c.patches);
            var want = norm(c.dom.after), got = root.innerHTML;
            if (got !== want) return "DOM after\n      got  " + got + "\n      want " + want;
            if (c.focus) {
                var a = c.focus.after, el = root.querySelector(a.element);
                if (document.activeElement !== el) return "focus lost: the active element is " + describe(document.activeElement);
                if (a.same && el !== focused) return "the focused element was re-created, not moved";
                if (a.selection && (el.selectionStart !== a.selection[0] || el.selectionEnd !== a.selection[1]))
                    return "caret " + el.selectionStart + ".." + el.selectionEnd + ", want " + a.selection.join("..");
                if (a.scrollTop != null && el.scrollTop !== a.scrollTop) return "focused scrollTop " + el.scrollTop + ", want " + a.scrollTop;
            }
            if (c.scroll) for (var sel2 in c.scroll.after) {
                var t = root.querySelector(sel2);
                if (!t || t.scrollTop !== c.scroll.after[sel2])
                    return "scrollTop of " + sel2 + " is " + (t && t.scrollTop) + ", want " + c.scroll.after[sel2];
            }
            return null;
        };

        var sseEvents = function (text) {
            var evs = [], cur = { data: [], event: "message", id: null };
            text.split("\n").forEach(function (line) {
                if (line === "") {
                    if (cur.data.length) evs.push({ type: cur.event, data: cur.data.join("\n"), id: cur.id });
                    cur = { data: [], event: "message", id: null };
                    return;
                }
                var i = line.indexOf(":"), field = i < 0 ? line : line.slice(0, i), value = i < 0 ? "" : line.slice(i + 1);
                if (value.charAt(0) === " ") value = value.slice(1);
                if (field === "data") cur.data.push(value);
                else if (field === "event") cur.event = value;
                else if (field === "id") cur.id = value;
            });
            return evs;
        };
        var deliver = function (es, text) {
            sseEvents(text).forEach(function (ev) { es.fire(ev.type, { data: ev.data, lastEventId: ev.id }); });
        };

        var runFrame = function (c) {
            var got = [];
            var adapter = FuaranLive.sseAdapter({ streamUrl: "/stream", sendUrl: "/event" });
            var es = adapter.connect(function (frame) { got.push(frame); }, function () {});
            var evs = sseEvents(c.sse);
            if (evs.length !== 1) return "sse holds " + evs.length + " events, want 1";
            if (evs[0].type !== "patch") return "event type " + evs[0].type;
            if (evs[0].id !== String(c.frame.seq)) return "event id " + evs[0].id + ", want " + c.frame.seq;
            if (evs[0].data !== c.json) return "data line is not the frame's JSON body";
            deliver(es, c.sse);
            if (got.length !== 1) return "the adapter delivered " + got.length + " frames";
            if (canon(got[0]) !== canon(c.frame)) return "frame\n      got  " + canon(got[0]) + "\n      want " + canon(c.frame);
            if (adapter.getSeq() !== c.frame.seq) return "the adapter's sequence is " + adapter.getSeq() + ", want " + c.frame.seq;
            return null;
        };

        var runInbound = function (c) {
            root.innerHTML = c.dom;
            var sent = [];
            FuaranLive.start({ streamUrl: "/stream", sendUrl: "/event" }, function (config) {
                var real = FuaranLive.sseAdapter(config);
                return { connect: real.connect, getSeq: real.getSeq, send: function (ev) { sent.push(JSON.stringify(ev)); } };
            });
            deliver(window.__fuaranStreams[window.__fuaranStreams.length - 1],
                "id: " + c.seq + "\nevent: patch\ndata: {\"seq\":" + c.seq + ",\"patches\":[],\"effects\":[]}\n\n");
            var g = c.gesture, el = root.querySelector(g.target);
            if (!el) return "gesture target absent: " + g.target;
            if (g.type === "click") el.click();
            else if (g.type === "input") { el.value = g.value; el.dispatchEvent(new Event("input", { bubbles: true })); }
            else if (g.type === "change") { el.checked = g.checked; el.dispatchEvent(new Event("change", { bubbles: true })); }
            else if (g.type === "submit") el.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
            else return "unknown gesture " + g.type;
            if (sent.length !== 1) return "the shim sent " + sent.length + " events";
            if (sent[0] !== c.body) return "body\n      got  " + sent[0] + "\n      want " + c.body;
            return null;
        };

        var domCases = function (label) {
            data.lowering.forEach(function (c) { record("lowering " + label, c.name, function () { return runDom(c); }); });
            data.apply.forEach(function (c) { record("apply " + label, c.name, function () { return runDom(c); }); });
        };
        domCases("(native move)");
        var native = { e: Element.prototype.moveBefore };
        delete Element.prototype.moveBefore;
        if (Element.prototype.moveBefore) out.push({ group: "harness", name: "remove moveBefore", ok: false, why: "still present" });
        domCases("(fallback move)");
        if (native.e) Element.prototype.moveBefore = native.e;
        data.frames.forEach(function (c) { record("frames", c.name, function () { return runFrame(c); }); });
        data.inbound.forEach(function (c) { record("inbound", c.name, function () { return runInbound(c); }); });
    } catch (e) {
        out.push({ group: "harness", name: "the harness ran", ok: false, why: String(e && e.stack || e) });
    }
    document.body.setAttribute("data-fuaran-results", btoa(unescape(encodeURIComponent(JSON.stringify(out)))));
}

// A scripted EventSource: the shim's SSE adapter subscribes to it, the harness fires the events.
function scriptedEventSource() {
    window.__fuaranStreams = [];
    window.EventSource = function (url) {
        this.url = url;
        this.listeners = {};
        window.__fuaranStreams.push(this);
    };
    window.EventSource.prototype.addEventListener = function (type, fn) {
        (this.listeners[type] = this.listeners[type] || []).push(fn);
    };
    window.EventSource.prototype.close = function () {};
    window.EventSource.prototype.fire = function (type, ev) {
        (this.listeners[type] || []).forEach(function (fn) { fn(ev); });
    };
}

const inlineScript = (src) => src.replace(/<\/script/gi, "<\\/script");

function page(shimSource, family) {
    const json = JSON.stringify(family).replace(/</g, "\\u003c");
    return `<!doctype html><html><head><meta charset="utf-8"></head><body>
<div id="case-root"></div>
<script type="application/json" id="fuaran-data">${json}</script>
<script>(${scriptedEventSource.toString()})();</script>
<script>${inlineScript(shimSource)}</script>
<script>(${harness.toString()})();</script>
</body></html>`;
}

/** Run one shim source in the browser; the per-case verdicts. */
function runInBrowser(browser, shimSource, family) {
    const dir = mkdtempSync(join(tmpdir(), "fuaran-browser-leg-"));
    try {
        const file = join(dir, "page.html");
        writeFileSync(file, page(shimSource, family), "utf8");
        const r = spawnSync(
            browser,
            [
                "--headless=new",
                "--disable-gpu",
                "--no-sandbox",
                "--no-first-run",
                "--no-default-browser-check",
                "--disable-extensions",
                `--user-data-dir=${join(dir, "profile")}`,
                "--dump-dom",
                pathToFileURL(file).href
            ],
            { encoding: "utf8", timeout: 120000, maxBuffer: 256 * 1024 * 1024, windowsHide: true }
        );
        if (r.error) throw new Error(`the browser did not run: ${r.error.message}`);
        const m = /data-fuaran-results="([A-Za-z0-9+/=]*)"/.exec(r.stdout || "");
        if (!m)
            throw new Error(`the page reported no verdicts (exit ${r.status}); the browser said:\n${(r.stderr || "").slice(-2000)}`);
        return JSON.parse(Buffer.from(m[1], "base64").toString("utf8"));
    } finally {
        try { rmSync(dir, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 }); } catch { /* a locked profile file is left in tmp */ }
    }
}

/** Each perturbation removes one behaviour the family certifies; the leg must go red on it. */
const PERTURBATIONS = [
    ["a granular patch lands on the wrapper, not the body",
        "return (el.hasAttribute(ATTR) && el.firstElementChild) || el;", "return el;"],
    ["a field address resolves to its first child",
        "return (el.hasAttribute(ATTR) && el.firstElementChild) || el;", "return el.firstElementChild || el;"],
    ["a fragment swap restores no focus (QW5)",
        "function restoreFocus(saved) {", "function restoreFocus(saved) { return;"],
    ["a move is a plain insert",
        "function moveInto(parent, el, before) {", "function moveInto(parent, el, before) { parent.insertBefore(el, before || null); return;"],
    ["the fallback move restores nothing",
        "    var top = el.scrollTop, left = el.scrollLeft;\n    parent.insertBefore(el, before || null);",
        "    parent.insertBefore(el, before || null); return;"],
    ["the SSE adapter drops the frame's sequence",
        "if (frame.seq != null) lastSeq = frame.seq;", ""],
    ["node ids are not CSS-escaped",
        "return (global.CSS && global.CSS.escape) ? global.CSS.escape(s) : String(s).replace(/[\"\\\\]/g, \"\\\\$&\");", "return s;"],
    ["a submit harvests nothing",
        "if (type === \"submit\") {\n      // Form flush", "if (type === \"submit\") { return {};\n      // Form flush"]
];

function main() {
    const selfTest = process.argv.includes("--self-test");
    const browser = browserPath();
    const family = readFamily();
    const shim = readFileSync(shimPath, "utf8");
    const count = family.lowering.length + family.apply.length;
    if (count === 0 || family.frames.length === 0 || family.inbound.length === 0)
        throw new Error("the server-driven family is empty: a leg certified by reading nothing is no leg");

    if (!selfTest) {
        const results = runInBrowser(browser, shim, family);
        const failed = results.filter((r) => !r.ok);
        for (const f of failed) console.log(`  FAIL [${f.group}] ${f.name}\n      ${f.why}`);
        console.log(`browser leg (${browser}): ${results.length - failed.length}/${results.length} green over the server-driven family`);
        return failed.length === 0 ? 0 : 1;
    }

    let bad = 0;
    for (const [name, from, to] of PERTURBATIONS) {
        if (!shim.includes(from)) {
            console.log(`  SELF-TEST BROKEN: '${name}' no longer finds its target in the shim`);
            bad++;
            continue;
        }
        const results = runInBrowser(browser, shim.replace(from, to), family);
        const red = results.filter((r) => !r.ok);
        if (red.length === 0) {
            console.log(`  STAYED GREEN: ${name}`);
            bad++;
        } else console.log(`  red as required: ${name} (${red.length} case(s), e.g. [${red[0].group}] ${red[0].name})`);
    }
    console.log(bad === 0 ? `browser leg self-test: every perturbation went red` : `browser leg self-test: ${bad} perturbation(s) proved nothing`);
    return bad === 0 ? 0 : 1;
}

try {
    process.exitCode = main();
} catch (e) {
    console.log(`browser leg FAILED: ${e.message}`);
    process.exitCode = 1;
}
