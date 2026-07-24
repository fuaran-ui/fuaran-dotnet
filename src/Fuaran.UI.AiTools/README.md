# Fuaran.UI.AiTools

Runtime-introspection AI tools for Fuaran trees — the §4i tool surface a downstream
AI consumer calls to observe what the renderer did with its emitted tree.

Four read-only tools, deterministic contract (same inputs → same observable state):

- `Tools.getNodeState` — id, kind, resolved props, resolved binding values,
  current state, geometry. Filterable via `IncludeKey`.
- `Tools.getBindingValue` — a single Binding-typed slot's resolved value.
- `Tools.getRenderedDom` — geometry tree (x, y, width, height, overflowing) rooted
  at a node; child geometry follows the typed-tree children.
- `Tools.getRuntimeErrors` — FIFO drain of `ErrorEntry` records since an optional
  turn watermark.

## Standalone posture

Depends on `Fuaran.UI` + `Fuaran.UI.Ops` + `FSharp.Core` only — no Fable / Feliz /
forge substrate. The orchestrator runs server-side (.NET), so the package targets
.NET; the pure tree-introspection helpers are reflection-free (Fable-compat by
construction, mirrors the `Fuaran.UI.Ops.Introspect` shape).

## Seams (injectable read-only ports)

- `IGeometryProbe` — `tryGetGeometry: NodeId -> Geometry option`. Default
  `NoGeometryProbe` returns `None` (suitable for .NET tests + the orchestrator
  pre-renderer-mount phase). Browser-side implementations bridge to React refs
  / `getBoundingClientRect` and live in the consumer (renderer adapter).
- `IRuntimeErrorSink` — `Record` + `Drain (turnId option)`. Default
  `InMemoryRuntimeErrorSink` is a thread-safe FIFO; consumers replace it with
  their own buffer-management strategy (per-turn ring buffer, persistent log,
  etc.). The renderer writes binding-resolution errors / unwired-action warnings
  here; `Fuaran.UI.AiTools.Tools.getRuntimeErrors` drains them.
- `IIntrospectionClock` — `Now: unit -> DateTimeOffset`. Default
  `FixedClock(epoch)` returns the seeded epoch so introspection results are
  byte-stable across runs; the orchestrator wires a real clock in production
  and a seeded clock in debug sessions per §4i determinism contract.

## What's deferred to follow-ons

- Real expression strings (`$queries.totalRevenue.amount`) for the
  `Binding.Query` accessor — v1 emits `$queries.<name>` and documents the
  accessor-path omission. Recovering the accessor's dot-path requires either
  source-side metadata (Phase 25c manifest substrate) or reflection.
- Theme-aware DOM (concrete CSS classes / computed styles via the §4n theme
  function) — geometry probe stays renderer-side; this package's contract is
  the rectangle + overflow flag, not the rendered HTML body.
- `simulateInteraction` / `runEvalAssertions` / `applyOps` / `beginDebugSession`
  — the remaining six §4i tools. `applyOps` is already covered by `Fuaran.UI.Ops`;
  the rest sequence into Phase 12 follow-up sessions + downstream AI-consumer work.

See the workspace roadmap entry for Phase 12 (`Fern → Fuaran` carve-out)
for the phase-scoped tasks and acceptance criteria.
