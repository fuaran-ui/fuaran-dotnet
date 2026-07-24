# apply-demo — the in-page `window.__fuaran.apply(op)` host (Phase 90 / 191)

The minimal Fable browser host that runs the full client-side apply loop:

```
wire JSON → Ops.JsonDecode.decodeOp → policy gate (CanDispatch) → Ops.Apply.apply → setState → re-render
```

It is the first Fable consumer of the (Phase-191) Fable-clean `Fuaran.UI.Ops`
decode + apply engine. The host model holds a **`Node<obj>`** tree (the wire
decoder yields `TreeOp<obj>`, so a `Node<'Msg>` model would type-clash), and the
`window.__fuaran` REPL is registered with a real `ApplyHandler` (the
`samples/demo` host passes `None` and returns the `unwired` envelope).

## Run it

```powershell
# 1. Transpile to JS. NOTE the --define DEBUG: window.__fuaran only registers
#    when the renderer's DebugGlobal was compiled under DEBUG (its
#    `compiledInDebug` is `#if DEBUG`). Fable does NOT inherit the entry
#    project's DEBUG symbol into referenced projects, so pass it explicitly.
dotnet fable ApplyDemo.fsproj -o output --define DEBUG

# 2. Serve (port 24020 per the workspace port-allocation table).
npm install
npm run dev
```

Then open <http://localhost:24020/> and, in DevTools:

```js
__fuaran.getNodeState("headline-metric")
// mutate a TextSource field:
__fuaran.apply('{"$type":"UpdateProp","path":"Label","target":"headline-metric","value":{"$type":"Literal","text":"Mutated!"}}')
// mutate a Binding<float> field:
__fuaran.apply('{"$type":"UpdateProp","path":"Source","target":"headline-metric","value":{"$type":"Static","value":99}}')
```

The metric re-renders from `Original Label · 42` to `Mutated! · 99`. A malformed
op returns a `decodeFailed` envelope; a bad target returns a `rejected`
envelope; both leave the tree unchanged.

## Phase 191 acceptance (browser-verified 2026-06-16)

`__fuaran.apply(...)` on a `TextSource` field **and** a `Binding<float>` field
each mutated to the correct typed value and re-rendered, confirming the
`coerceField` coercion path runs correctly under Fable (it was dead before
Phase 191). Verified via the `preview_*` harness against this sample.

## Phase 192 apply-parity — Fable CI lane

`ApplyParity.fs` is a pure, pipeline-neutral module (`evalOp : opJson -> result
string`) shared by both apply pipelines. The .NET half
(`src/Fuaran.UI.Ops.Tests/ApplyParityTests.fs`) asserts every corpus op's
outcome against the committed oracle `src/Fuaran.UI.Ops.Tests/apply-parity.golden.json`.

[`parity-runner.mjs`](parity-runner.mjs) is the **automated Fable half**: it
imports the Fable-built `ApplyParity.evalOp`, replays every golden op, and
asserts byte-identical results against the same golden. A Fable-vs-.NET
divergence (e.g. a float-formatter or coercion regression) fails it.

```powershell
dotnet fable ApplyDemo.fsproj -o output --noCache   # no --define DEBUG needed
node parity-runner.mjs                               # exits non-zero on any divergence
```

It runs on every push/PR via [`.github/workflows/apply-parity-fable.yml`](../../.github/workflows/apply-parity-fable.yml).
Pure Node — `ApplyParity.js` pulls in only the Ops engine + canonical encoder
(no React / DOM / Elmish), so no JSDOM or `npm install` is needed. The runner
reads ops from the golden (whose bytes the .NET drift guard pins to the
workspace corpus), so the lane is self-contained in this repo.
