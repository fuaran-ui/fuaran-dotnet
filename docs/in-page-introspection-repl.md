# In-page introspection REPL (`window.__fuaran`)

_Phase 90 – the "pull" half of the live-debug console._

`window.__fuaran` is a renderer-registered, config-gated debug global that exposes the running UI's **typed layer** to the browser DevTools console. A browser's DOM console hands back untyped, post-projection DOM nodes; Fuaran's premise is the inverse – the typed `Node<'Msg>` tree is the source of truth and the DOM is a projection – so the valuable debugging question is *"which node is this, what did this binding resolve to, why is this off-screen"*, which the raw console cannot answer. `window.__fuaran` answers it.

It is the **pull** counterpart to [`devtools-console-sink.md`](devtools-console-sink.md) (Phase 91 – **push**: passive telemetry narration). Together they make the typed layer legible in the console with no panel and no extension. The same surface ships from the TypeScript reference renderer (`@fuaran-ui/renderer`), with identical method names and payload shapes, so a console session is **host-agnostic** – the same `__fuaran.getNodeState("submit-btn")` works whether the page is the F# Fable host or the TS host.

> **Debug-only / unstable.** `window.__fuaran` is excluded from semver. It is registered **only** under a DEBUG build with an explicit host opt-in; a release build dead-code-eliminates the registration entirely, so `window.__fuaran` is `undefined` in production. Do not build features against its shape.

## Wiring (host side)

Call `DebugGlobal.register` from your render path, passing the live tree + binding sources. Re-registering each render keeps the global pointed at the current tree:

```fsharp
open Fuaran.UI.Renderer

let view (model: Model) (dispatch: Msg -> unit) =
    let tree = root model
    let sources = buildSources model
    // `debug = true` is the host opt-in; registration still requires a DEBUG
    // build (`DebugGlobal.shouldRegister`), so a release build leaves the
    // global `undefined`. `None` = no apply handler (read-only); pass
    // `Some handler` to enable policy-gated mutation (see `apply` below).
    DebugGlobal.register true tree sources runtime None
    Render.render { ... ; Sources = sources ; Runtime = runtime ; ... } tree
```

- `register debug tree sources runtime applyHandler` – registers `window.__fuaran` iff `shouldRegister debug` (host opt-in **and** DEBUG build). No-op otherwise, and a no-op entirely on the .NET pipeline (no `window`).
- `unregister ()` – removes the global if present (safe to call from an effect cleanup).
- `shouldRegister debug : bool` – the pure gating predicate (`debug && compiledInDebug`). `compiledInDebug` is `#if DEBUG`-driven, so a release pack forces it `false`.

## Methods

Every method returns a plain JS object (never throws, never a silent no-op); errors come back as a `{ error: "…" }` envelope. Type the calls straight into the DevTools console.

| Method | Returns |
|---|---|
| `__fuaran.version` | The shape's schema version (`"0.1.0"`), independent of package semver. |
| `__fuaran.getNodeState(id)` | `{ id, kind, bindings: [{ slot, expression, source }], childIds }` – the typed structural snapshot for a node, or `{ error }` if the id is absent. |
| `__fuaran.getBindingValue(id, slot)` | The resolved value of one binding slot against the live sources (see below), or `{ error }` if the slot is not a binding slot on that kind. |
| `__fuaran.getRenderedDom(id)` | `{ x, y, width, height, overflowing, hidden }` – live DOM geometry read from the node's `[data-fuaran-node-id]` element, or `{ error }`. |
| `__fuaran.inspectTree()` | A recursive `{ id, kind, bindings, childIds, children }` snapshot of the whole tree. The fastest way to list every queryable node id. |
| `__fuaran.findNodes(kind)` | The ids of every node whose wire kind discriminator equals `kind` (e.g. `"Metric"`, `"Button"`, `"GridLayout"`). |
| `__fuaran.apply(opJson)` | Policy-gated `TreeOp` mutation (see below). |
| `__fuaran.help()` | A one-screen reference string – `console.log(__fuaran.help())`. |

### Payload shapes

- **`kind`** is the canonical **wire discriminator** – `"Stack"`, `"Metric"`, `"Button"`, `"GridLayout"` (the layout grid), `"Grid"` (the data grid), `"Custom"`, `"ErrorBoundary"`, `"FragmentDecl"`, `"FragmentRef"`. It matches `getNodeState(...).kind`, `findNodes(kind)`, and the JSON wire-format `kind` tag.
- **`bindings`** lists each bound binding slot as `{ slot, expression, source }`, where `expression` is the canonical wire form (`$static`, `$queries.<name>`, `$state.<key>`, `$filters.<name>`, `$selection.<nodeId>`, `$computed`, `$i18n.<key>`, `$local`, `$format`) and `source` is the `Binding` case (`Static` / `Query` / `Filter` / `Selection` / `State` / `Computed` / `I18n`). Optional slots (Metric `Trend`, Tabs `ActiveTag`, Button/Select/Form/FileUpload `Disabled`) appear only when present.
- **`getBindingValue`** returns one of:
  - `{ status: "resolved", value, expression, source }` – the slot resolved to a live value.
  - `{ status: "notResolved", expression, source }` – the data source is registered but hasn't resolved yet (a pending query, etc.).
  - `{ status: "errored", message, expression, source }` – the resolver threw (accessor / unbox failure).
  - `{ status: "i18nUnresolved", key, expression, source }` – an `I18n` binding with no translation for the key.
  - `{ status: "noOverride", expression: "$none", source: "Static" }` – an optional slot that is declared on the kind but currently absent.
  - `{ error }` – the slot name is not a binding slot on this node's kind. Use `getNodeState(id).bindings` to list the slots.

## `apply(opJson)` – policy-gated mutation

`__fuaran.apply(opJson)` is the one **mutating** entry. It obeys the same default-deny contract an AI-tool dispatch obeys (FGP 3): the call routes through the runtime's policy gate (`IFuaranRuntime.CanDispatch(ActionDescriptor.ApplyTreeOp …)`) **before** the host's apply handler ever decodes the op. A denied op returns the structured deny envelope and never touches the tree.

```js
> __fuaran.apply('{ "op": "UpdateProp", "nodeId": "revenue-metric", ... }')
{ ok: true, status: "applied" }                                  // gate allowed, host applied + re-rendered
{ ok: false, status: "denied", denied: true, error: "apply denied by policy gate: ApplyTreeOp(...)" }
{ ok: false, status: "unwired", error: "apply is not wired on this host (...)." }
{ ok: false, status: "decodeFailed", error: "..." }              // op JSON was not a valid TreeOp
{ ok: false, status: "rejected", error: "..." }                  // the apply engine rejected the op
```

The renderer owns neither the apply engine nor the host's tree state, so `apply` is wired by the host as an `ApplyHandler` (`string -> ApplyOutcome`) passed to `register` – it decodes the op, applies it through `Fuaran.UI.Ops`, and re-renders. A host that supplies `None` (read-only) returns the `unwired` envelope. A default-deny host (a BYOK playground, a read-only embed) denies every mutation through the same `CanDispatch` seam it uses to refuse `Call` / `Navigate` / `AiTool`.

## Diagnostics discipline (FGP 4)

The module writes **no** `console.*` of its own – `help()` returns a string for *you* to print, every method returns a value, and the only diagnostic (a denied `apply`) routes through the renderer's centralised `IFuaranRuntime.Warn` channel. The console output you see is exactly what you typed.

## See also

- [`devtools-console-sink.md`](devtools-console-sink.md) – the push half (Phase 91): passive telemetry narration to the console.
- The `@fuaran-ui/renderer` README "In-page introspection (`window.__fuaran`)" section – the byte-parity TypeScript host surface.
