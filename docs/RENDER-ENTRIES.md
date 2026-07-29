# Render entries — the hosting matrix

Which entry point to call, and what each one wires for you.

`Fuaran.UI.Renderer.Render` exposes one general entry (`render`, which takes a
`RenderContext` you build yourself) and a family of convenience entries that
construct that context for the common host shapes. They are not alternatives to
each other so much as points on a grid: every one of them ends up calling
`render` with a `RenderContext`, and they differ only in which fields they let
you supply and which they pin.

This page is the authoritative hosting story for that grid. It exists because
the alternative — each host reading `Render.fs` and inferring the rule — has
already produced one durable piece of folklore that outlived the behaviour it
described (see [Source-packed hosting](#source-packed-hosting-there-is-no-sink-parameter-trap)).

## The four `RenderContext` axes a host cares about

| Axis | `RenderContext` field | What it does | Default when an entry pins it |
|---|---|---|---|
| **Runtime** | `Runtime: IFuaranRuntime` | The host substrate every effecting `Action` runs through — `Call` / `Navigate` / `SetState` / `AiTool` / clipboard / file-read — plus the `CanDispatch` policy gate, the custom-renderer registry, and the `TryLoadGuest` mount seam. | `Runtime.diagnostic` (logs, dispatches nothing) |
| **Sink** | `TelemetrySink: IFuaranTelemetrySink option` | Where render failures go. `Some sink` emits a `RenderFailureTelemetry` on every per-node-guard catch and every `ErrorBoundary` catch. `None` swallows them into the fallback placeholder. | `None` |
| **Scope** | `Scope: string option` | The runtime scope for `Binding.State` reads and `Action.SetState` writes. `Some id` routes both to the isolated `StateStore.forScope id`; `None` uses the process-global store. | `None` |
| **Context** | `SessionContext: Map<string, string>` | The host's opaque correlation slot, stamped onto render-failure telemetry via the well-known keys `Render.promptIdKey` / `Render.userIdKey`. Read once per render — see the warning below. | `Map.empty` |

`VisAdapter` is a fifth field, but every convenience entry pins it to
`VisAdapter.noOp`; a host needing a real visualisation adapter builds its own
`RenderContext` and calls `render`.

## The matrix

| Entry | Runtime | Sink | Scope | Context | Reach for it when |
|---|---|---|---|---|---|
| `renderWithSources` | pinned (diagnostic) | pinned `None` | pinned `None` | pinned empty | Tests, samples, a first spike. Anything effecting is inert. |
| `renderWithSourcesAndSink` | **yours** | **yours** | pinned `None` | pinned empty | The ordinary production host. |
| `renderWithSourcesSinkAndContext` | **yours** | **yours** | pinned `None` | **yours** | As above, plus you want render failures joined to the interaction that caused them. |
| `renderWithSourcesInScope` | **yours** | pinned `None` | **yours** | pinned empty | State isolation with no observability requirement — a test harness, or a host that has not wired telemetry yet. |
| `renderWithSourcesInScopeAndSink` | **yours** | **yours** | **yours** | pinned empty | **State isolation AND render-failure telemetry.** The registered-custom-renderer / mount-hosting shape. |
| `render` | **yours** | **yours** | **yours** | **yours** | Any combination the convenience entries do not cover, or a real `VisAdapter`. Build the `RenderContext` yourself. |

Two wrappers compose over the above rather than adding axes:

| Entry | What it adds |
|---|---|
| `renderWithTheme theme …` | `renderWithSources` plus a `<style>` element carrying the `Theme`'s CSS-variable bundle. Same pinned axes as `renderWithSources`. |
| `renderStateReactive` | `renderWithSources` plus a subscription to every State / Filter / Selection / Query key the tree reads, so a store write re-renders the whole surface. Fable-only behaviour; on .NET it is `renderWithSources`. |

Server-side rendering is a different package with a deliberately smaller
surface — `Fuaran.UI.Renderer.Server.Render.render` / `renderWithTheme` /
`renderToElement`. It has **no runtime, no dispatch, and no sink**: action-bearing
nodes render inert by design. See [`SSR.md`](SSR.md).

### Why scope and sink were not combinable before

Until this matrix was written, `renderWithSourcesInScope` hard-coded
`TelemetrySink = None`. A host wanting state isolation therefore paid for it by
losing every render-failure event — and that is exactly backwards, because the
host most likely to need isolation is a host rendering trees it did not author
(mounted guests, registered custom renderers), which is also the host most
likely to hit render failures worth reporting.

Isolation and observability are orthogonal, so
`renderWithSourcesInScopeAndSink` exists and no entry makes a host choose. If
you also need correlation context on top of a scope, build the `RenderContext`
and call `render` — the convenience family deliberately stops before the
combinatorial explosion.

### `SessionContext` is a value, not a subscription

Every entry that takes a context reads it **once, for this render**. A host that
captures a context at construction time and reuses it freezes the first
interaction's id onto every later frame's telemetry. Re-read your current value
at each render call; render is already per-frame, so this is the natural shape.

## Hosts that register custom renderers or mount guests

Two seams matter to this class of host, and both are consulted on the **plain
render path** — not only when an orchestration tier drives the tree.

### `IFuaranRuntime.TryRenderCustom` / `TryGetCustomRenderer`

`NodeKind.Custom` dispatches through the runtime you supply, which means any
entry from `renderWithSourcesAndSink` down the matrix works. `renderWithSources`
does **not** — it pins the diagnostic runtime, whose custom-renderer lookups
return `None`, so every `Custom` node renders a placeholder. A registered custom
renderer is a host trust boundary; see the "Custom-renderer trust boundary"
section of [`../SANITIZATION.md`](../SANITIZATION.md).

### `GuestSeam` — the mount capability policy

`NodeKind.Mount` resolves its guest through `IFuaranRuntime.TryLoadGuest` and
renders it under its own scope. By default the guest receives the **host**
runtime and its dispatch is bridged through the mount's `OnBubble` unwrapped —
which is fine for a trusted guest and wrong for anything else.

A host that wants a rendered guest to be default-deny installs a `GuestSeam`:

```fsharp
Render.installGuestSeam
    { WrapRuntime = fun ctx hostRuntime -> myPolicy.RestrictTo(ctx.ScopeId, ctx.Capabilities, hostRuntime)
      GateBubble  = fun ctx rawBubble   -> myPolicy.GateOutbound(ctx.Channel.Direction, rawBubble) }
```

- Installed once per process; `clearGuestSeam ()` restores the ungated default
  and `currentGuestSeam ()` reports what is installed.
- Consulted **per rendered mount**, so one policy can vary by scope id,
  declared capabilities, or channel direction — those three are the whole of
  `GuestSeamContext`.
- `WrapRuntime` returns the runtime the guest renders against; returning the
  host runtime unchanged is the identity policy.
- `GateBubble` returns the function the guest's dispatches run through;
  returning `rawBubble` unchanged is the identity policy.
- **With no seam installed, nothing changes.** A host that never calls
  `installGuestSeam` gets exactly the pre-seam behaviour.

The seam is a language-tier-resident hook rather than a direct dependency
because the language tier must not reference the orchestration tier that owns
the policy. The shape mirrors the renderer's existing late-bound
`renderGuestHook`.

## Source-packed hosting: there is no sink-parameter trap

Consumer-side lore held that passing an `IFuaranTelemetrySink` through
`renderWithSourcesAndSink` tripped a Fable assembly-identity split under
source-packed `PackageReference` consumption, and at least one consumer moved to
a Feliz island to avoid it. **That is not the behaviour.** Verified 2026-07-08: a
concrete `IFuaranTelemetrySink` object expression passed through
`renderWithSourcesAndSink` transpiles clean under Fable 5 from a source-packed
`PackageReference` consumer.

The claim's own citation pointed at a repository that never documented it, which
is the usual signature of folklore: a real symptom, a plausible cause attached
to it, and no source that can be checked. It is recorded here — in the doc a
host reads before choosing an entry — so it does not regrow. **A telemetry sink
is a normal parameter. Pass it.**

If you hit a genuine assembly-identity split, it is a Fable packaging question
(check that every tier resolves one copy of `Fuaran.UI.Telemetry.Abstractions`),
not a reason to pick a sink-less entry.

## See also

- [`../STABILITY.md`](../STABILITY.md) — which of these surfaces are stable and what a version bump means.
- [`TECHNICAL_GUIDE.md`](TECHNICAL_GUIDE.md) §6 — how the renderer dispatches once an entry has built the context.
- [`SSR.md`](SSR.md) — the server renderer's deliberately smaller surface.
- [`../SANITIZATION.md`](../SANITIZATION.md) — the custom-renderer trust boundary.
