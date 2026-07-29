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
| `__fuaran.findNodes(kind)` | The ids of every node whose kind tag equals `kind` (e.g. `"Metric"`, `"Button"`, `"GridLayout"`). Note `"Grid"`, not `"DataGrid"`, for the data grid — see the `kind` note below. |
| `__fuaran.apply(op)` | Policy-gated `TreeOp` mutation (see below). Accepts a JSON **string** or a structured **object**. |
| `__fuaran.canApply` | Whether this host wired a real apply path (Phase 739). |
| `__fuaran.treeRevision()` | An opaque token identifying the current tree state — compare for equality, never parse (Phase 739). |
| `__fuaran.subscribe(cb)` | Subscribe to committed tree changes; returns an unsubscribe function. Push, never poll (Phase 739). |
| `__fuaran.help()` | A one-screen reference string – `console.log(__fuaran.help())`. |

### Payload shapes

- **`kind`** is the **kind tag** (`Kind.name`, the vocabulary `HoleDecl.Slot` kind-constraints are matched against) – `"Stack"`, `"Metric"`, `"Button"`, `"GridLayout"` (the layout grid), `"Grid"` (the data grid), `"Custom"`, `"ErrorBoundary"`, `"FragmentDecl"`, `"FragmentRef"`. It matches `getNodeState(...).kind` and `findNodes(kind)`. It coincides with the JSON wire-format `kind.$type` for every kind **except the data grid**, which is `"Grid"` here and `"DataGrid"` on the wire — so a token copied out of a wire tree is directly usable in `findNodes` for every kind but that one. See [Host parity](#host-parity) for why the divergence is adapted at the relay boundary rather than resolved.
- **`bindings`** lists each bound binding slot as `{ slot, expression, source }`, where `expression` is the canonical wire form (`$static`, `$queries.<name>`, `$state.<key>`, `$filters.<name>`, `$selection.<nodeId>`, `$computed`, `$i18n.<key>`, `$local`, `$format`) and `source` is the `Binding` case (`Static` / `Query` / `Filter` / `Selection` / `State` / `Computed` / `I18n`). Optional slots (Metric `Trend`, Tabs `ActiveTag`, Button/Select/Form/FileUpload `Disabled`) appear only when present.
- **`getBindingValue`** returns one of:
  - `{ status: "resolved", value, expression, source }` – the slot resolved to a live value.
  - `{ status: "notResolved", expression, source }` – the data source is registered but hasn't resolved yet (a pending query, etc.).
  - `{ status: "errored", message, expression, source }` – the resolver threw (accessor / unbox failure).
  - `{ status: "i18nUnresolved", key, expression, source }` – an `I18n` binding with no translation for the key.
  - `{ status: "noOverride", expression: "$none", source: "Static" }` – an optional slot that is declared on the kind but currently absent.
  - `{ error }` – the slot name is not a binding slot on this node's kind. Use `getNodeState(id).bindings` to list the slots.

## `apply(op)` – policy-gated mutation

`__fuaran.apply(op)` is the one **mutating** entry. `op` may be a JSON **string** (what a console user types) or a structured **object** — a `postMessage` relay is a structured-clone channel with no text layer, and canonicalising is the host's obligation rather than the least-qualified peer's, so the surface accepts the object and serialises it itself. It obeys the same default-deny contract an AI-tool dispatch obeys (FGP 3): the call routes through the runtime's policy gate (`IFuaranRuntime.CanDispatch(ActionDescriptor.ApplyTreeOp …)`) **before** the host's apply handler ever decodes the op. A denied op returns the structured deny envelope and never touches the tree.

```js
> __fuaran.apply('{ "op": "UpdateProp", "nodeId": "revenue-metric", ... }')
{ ok: true, status: "applied" }                                  // gate allowed, host applied + re-rendered
{ ok: false, status: "denied", denied: true, error: "apply denied by policy gate: ApplyTreeOp(...)" }
{ ok: false, status: "unwired", error: "apply is not wired on this host (...)." }
{ ok: false, status: "decodeFailed", error: "..." }              // op JSON was not a valid TreeOp
{ ok: false, status: "rejected", error: "..." }                  // the apply engine rejected the op
```

The renderer owns neither the apply engine nor the host's tree state, so `apply` is wired by the host as an `ApplyHandler` (`string -> ApplyOutcome`) passed to `register` – it decodes the op, applies it through `Fuaran.UI.Ops`, and re-renders. A host that supplies `None` (read-only) returns the `unwired` envelope. A default-deny host (a BYOK playground, a read-only embed) denies every mutation through the same `CanDispatch` seam it uses to refuse `Call` / `Navigate` / `AiTool`.

## Change subscription — `treeRevision()` / `subscribe(cb)`

_Phase 739._ Two additions let a reader track a live tree without polling:

```js
> __fuaran.treeRevision()
"r-7"                                   // opaque: compare for equality, never parse
> const off = __fuaran.subscribe(c => console.log(c))
{ treeRevision: "r-8", cause: "host" }  // fires on each committed tree change
> off()                                 // release
> __fuaran.canApply
true                                    // whether this host wired a real apply path
```

**The hub lives outside the surface, and that is the load-bearing part.** `window.__fuaran` is REBUILT on every committed tree change — the host calls `register` from its render path, so a new tree means a new surface object. A subscription held on one instance would be dropped by the very event it exists to report. So the listener registry and the revision sequence live in `Fuaran.UI.Renderer.ChangeHub`, outside any surface instance, and the registration commits the tree to it.

Notifications **coalesce**: every commit in one turn collapses into a single notification carrying the latest revision, with the `apply` cause winning over `host` because it is the more specific answer. A change is a staleness signal, not a change log — a reader that needs the new state re-reads it. Commit is idempotent on tree *identity*, so a re-registration caused only by a change of `sources` or `runtime` is not reported as a tree change.

Hosts that want an isolated signal (a test, a second embedded renderer) pass their own hub via `DebugOptions.Hub`; the default is the page-wide one, so one page has one notion of "current".

## DevTools relay (`relay@1.0`)

_Phase 739._ `Fuaran.UI.Renderer.Relay` is the **page peer** of the DevTools relay contract: a same-origin `postMessage` envelope that carries the surface above across the page/extension boundary, so a browser extension — or any other same-page script — can inspect, and where the host permits edit, a live Fuaran UI.

The contract is specified language-neutrally in `DEVTOOLS_RELAY.md` in the wire-format specification repository, with an executable fixture family beside it. **This is a relay over the existing in-page surface, not a second introspection protocol**, and it is a client of the wire format rather than an extension of it: the relay profile `relay@1.0` versions independently of the wire profile `core@1.0`.

```fsharp
// once, at boot — the peer holds client subscriptions and reads the live
// surface per request, so it needs no rebuild when the tree moves
Relay.install true "0.6.0" |> ignore

// per render — registers window.__fuaran AND publishes the relay surface
Relay.registerAndPublish
    true   // debug opt-in
    true   // relay opt-in
    tree
    sources
    runtime
    { DebugGlobal.DebugOptions.defaults with ApplyHandler = Some handler }
```

**Off by default, and default-off is the point.** There are two postures of "off" and the contract prefers the stronger one: without the relay opt-in **no listener is installed at all**, so a probe gets no answer whatsoever — absent, not merely inert. A host that wants the honest development posture instead installs a peer with `OptedIn = false`, which answers `NOT_OPTED_IN` to every message including `hello`, telling a well-behaved client why it cannot proceed at the cost of confirming a host is present. `Relay.shouldInstall` adds the DEBUG-build gate on top, mirroring `shouldRegister`, so a release build installs nothing even if a flag is set wrongly.

**The relay has no side door.** Every mutation crosses the host's own decode → validate → policy path, in the page: an op arriving over the relay is the same `ApplyHandler` seam the console uses, behind the same `CanDispatch` gate. The relay contributes no apply engine, no validator and no policy of its own — it maps the outcome onto a refusal class and nothing more. Consequently a relay client cannot construct a tree state the host would not accept from its own code, and no message in the closed set adjusts policy or raises a client's privilege.

**Attribution is advisory.** A client may attach `{ actor, reason }` to an `apply`; the host records it and grants nothing on the basis of it. It is free-form text from an unprivileged peer.

To carry the contract's typed refusal detail, a host's `ApplyHandler` can return the richer outcomes:

| `ApplyOutcome` | Relay outcome |
|---|---|
| `AppliedWithTree newTree` | `apply.ok`, with the revision the `changed` event will carry — the two are the same token |
| `Applied` | `apply.ok`, with the hub's current revision (the host commits its new tree on its next render) |
| `DecodeFailedWith error` | `DECODE_FAILED`, carrying the wire format's own `DecodeError` verbatim |
| `RejectedWith (message, code)` | `VALIDATOR_REJECT`, carrying the host's diagnostic code |

### Host parity

F# is the **second** relay-conformant host; `@fuaran-ui/renderer` shipped the TypeScript peer first. The two are parity-locked on **message shapes and refusal classification, not on bytes** — relay envelopes are transport, are never hashed or journalled, and carry no byte-parity obligation. Both hosts run the same fixture family (`devtools-relay/`, self-enumerated by its own manifest) as a conformance gate: the F# leg is `Fuaran.UI.Tests/RelayCorpusTests.fs`, which drives every fixture through the peer on the .NET pipeline, with no browser — everything but the `postMessage` transport is pure F#.

One deliberate difference from this host's own console surface, sanctioned by the contract's "where hosts differed" rule: the relay reports the **wire** kind discriminator (`kind.$type`), where `__fuaran.getNodeState` reports `Kind.name`, the kind-constraint vocabulary. The two coincide for every kind except `DataGrid`, which is `"Grid"` in the console and `"DataGrid"` on the wire. A relay client filters on the token it read from a wire tree, so the peer adapts at the boundary; a test pins the mapping against the canonical encoder, so a second divergence fails the build.

## Diagnostics discipline (FGP 4)

The module writes **no** `console.*` of its own – `help()` returns a string for *you* to print, every method returns a value, and the only diagnostic (a denied `apply`) routes through the renderer's centralised `IFuaranRuntime.Warn` channel. The console output you see is exactly what you typed.

## See also

- [`devtools-console-sink.md`](devtools-console-sink.md) – the push half (Phase 91): passive telemetry narration to the console.
- The `@fuaran-ui/renderer` README "In-page introspection (`window.__fuaran`)" section – the byte-parity TypeScript host surface.
