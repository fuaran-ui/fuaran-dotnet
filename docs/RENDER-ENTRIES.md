# Render entries — the hosting matrix

Which entry point to call, and what each one wires for you.

`Fuaran.UI.Renderer.Render` exposes one general entry (`render`, which takes a
`RenderContext` you build with `RenderContext.create`) and a family of convenience
entries for the common host shapes. They are not alternatives to each other so
much as points on a grid: every one of them is `render` over
`RenderContext.create`, and they differ only in which fields they let you supply
and which they pin.

This page is the authoritative hosting story for that grid. It exists because
the alternative — each host reading `Render.fs` and inferring the rule — has
already produced one durable piece of folklore that outlived the behaviour it
described (see [Source-packed hosting](#source-packed-hosting-there-is-no-sink-parameter-trap)).

## Building a context: `RenderContext.create` plus `with`

Every entry below is `render` over one builder:

```fsharp
let ctx = Render.RenderContext.create sources dispatch node
Render.render { ctx with Runtime = myRuntime; TelemetrySink = Some mySink } node
```

`create` runs the two one-shot pre-render passes over `node` (its declared state seeds laid under
`sources.State`, and its fragment registry) and pins every other field to its convenience default.
Adjust what you need with `with`. Do **not** spell the record out in full: a full literal is a
construction site that every new field breaks, and the builder is the one place a new field gets its
default. `RenderContext.inScope scopeId ctx` renders under a runtime scope (below).

The four settings that decide what a document may do, rather than what it shows, live in one
`Policy: RenderPolicy` record (`Fuaran.UI.Renderer.Core`), and the server's `ServerRenderContext`
holds the same record, so a posture moves on both tiers together:

| `Policy` field | What it does | `RenderPolicy.defaults` |
|---|---|---|
| `Egress` | The destination policy every emitted `href`, `src` and route is checked against. | `Sanitize.denyNonLocalEgress` |
| `CustomHashFloor` | This render's own `Custom` content-hash floor. Raise-only: the stricter of it and the process floor applies. | `None` (the process floor) |
| `Csp` | The Content-Security-Policy posture: `Permissive` emits inline styles, `Strict nonce` emits generated classes. | `Csp.Permissive` |
| `Scope` | The runtime scope this tree renders under. | `None` (the root scope) |

## The `RenderContext` axes a host cares about

| Axis | `RenderContext` field | What it does | `create`'s default |
|---|---|---|---|
| **Runtime** | `Runtime: IFuaranRuntime` | The host substrate every effecting `Action` runs through — `Call` / `Navigate` / `SetState` / `AiTool` / clipboard / file-read — plus the `CanDispatch` policy gate, the custom-renderer registry, and the `TryLoadGuest` mount seam. | `Runtime.diagnostic` (logs, dispatches nothing) |
| **Sink** | `TelemetrySink: IFuaranTelemetrySink option` | Where render failures go. `Some sink` emits a `RenderFailureTelemetry` on every per-node-guard catch and every `ErrorBoundary` catch. `None` swallows them into the fallback placeholder. | `None` |
| **Scope** | `Policy.Scope: string option` | The runtime scope for `Binding.State` reads and `Action.SetState` writes. Set it with `RenderContext.inScope id`, which also merges `StateStore.forScope id`'s snapshot over `Sources.State`; setting `Policy.Scope` alone skips that merge. `None` uses the process-global store. | `None` |
| **Context** | `SessionContext: Map<string, string>` | The host's opaque correlation slot, stamped onto render-failure telemetry via the well-known keys `Render.promptIdKey` / `Render.userIdKey`. Read once per render — see the warning below. | `Map.empty` |
| **Action sink** | `ActionSink: IActionInvocationSink option` | Where user gestures are recorded. The sink declares its own capture mode. | `None` (nothing recorded) |
| **Upload sink** | `UploadSink: IFuaranUploadSink option` | The host's large-binary upload transport and destination registry. | `None` (no uploads) |

`VisAdapter` is a further field, pinned by `create` to `VisAdapter.noOp`; a host needing a real
visualisation adapter sets it with `with`.

## The matrix

| Entry | Runtime | Sink | Scope | Policy | Reach for it when |
|---|---|---|---|---|---|
| `renderWithSources` | pinned (diagnostic) | pinned `None` | pinned `None` | defaults | Tests, samples, a first spike. Anything effecting is inert. |
| `renderWithSourcesAndEgress` | pinned (diagnostic) | pinned `None` | pinned `None` | **your egress** | The named opt-out from default-deny egress, so a grep finds every host that widened it. |
| `renderWithSourcesAndCsp` | pinned (diagnostic) | pinned `None` | pinned `None` | **your CSP** | The client half of strict-CSP mode, for a hydrating host. See [`../SANITIZATION.md`](../SANITIZATION.md). |
| `renderWithSourcesAndSink` | **yours** | **yours** | pinned `None` | defaults | The ordinary production host. |
| `renderWithSourcesInScope` | **yours** | pinned `None` | **yours** | defaults | State isolation with no observability requirement — a test harness, or a host that has not wired telemetry yet. |
| `renderWithSourcesInScopeAndSink` | **yours** | **yours** | **yours** | defaults | **State isolation AND render-failure telemetry.** The registered-custom-renderer / mount-hosting shape. |
| `render` | **yours** | **yours** | **yours** | **yours** | Any combination the entries above do not cover: `{ RenderContext.create sources dispatch node with … }`. |

Every entry pins `SessionContext` to `Map.empty` and the action and upload sinks to `None`; a host
that needs any of them uses `render` with `with`. Two older entries,
`renderWithSourcesSinkAndContext` and `renderWithSourcesSinkContextAndActionSink`, are marked
`[<Obsolete>]` (Phase 2067): each is one `with` over `create`, which is what their obsolete messages
spell out.

Two wrappers compose over the above rather than adding axes:

| Entry | What it adds |
|---|---|
| `renderWithTheme theme …` | `renderWithSources` plus a `<style>` element carrying the `Theme`'s CSS-variable bundle. Same pinned axes as `renderWithSources`. |
| `renderStateReactive` | `renderWithSources` plus a subscription to every State / Filter / Selection / Query key the tree reads, so a store write re-renders the whole surface. Fable-only behaviour; on .NET it is `renderWithSources`. |

Server-side rendering is a different package with a deliberately smaller
surface — `Fuaran.UI.Renderer.Server.Render.render` / `renderWithTheme` /
`renderToElement`, over one context builder (`mkContextWith`) and its by-name policy variants
(`mkContextWithEgress`, `mkContextWithCsp`, `mkContextInScope`). It has **no runtime, no dispatch,
and no sink**: action-bearing nodes render inert by design. See [`SSR.md`](SSR.md).

### Why the family stops growing

Until the matrix was written, `renderWithSourcesInScope` hard-coded
`TelemetrySink = None`. A host wanting state isolation therefore paid for it by
losing every render-failure event — and that is exactly backwards, because the
host most likely to need isolation is a host rendering trees it did not author
(mounted guests, registered custom renderers), which is also the host most
likely to hit render failures worth reporting. So
`renderWithSourcesInScopeAndSink` exists and no entry makes a host choose.

Each further combination used to need either another entry or a host spelling out the whole record.
`RenderContext.create` plus `with` replaces both: a correlation context on top of a scope is
`{ (RenderContext.create sources dispatch node |> RenderContext.inScope id) with SessionContext = context }`.

### `SessionContext` is a value, not a subscription

Every context is read **once, for this render**. A host that
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
renders it under its own scope, inheriting the host's `Policy` (egress, hash
floor, CSP posture) apart from the scope. With no seam installed the guest renders
against an **unprivileged** runtime (Phase 783), and its dispatch is bridged
through the mount's `OnBubble`.

A host that wants to grant a rendered guest more, or gate it differently, installs a `GuestSeam`:

```fsharp
Render.installGuestSeam
    { WrapRuntime = fun ctx hostRuntime -> myPolicy.RestrictTo(ctx.ScopeId, ctx.Capabilities, hostRuntime)
      GateBubble  = fun ctx rawBubble   -> myPolicy.GateOutbound(ctx.Channel.Direction, rawBubble) }
```

- Installed once per process; `clearGuestSeam ()` restores the unprivileged default
  and `currentGuestSeam ()` reports what is installed.
- Consulted **per rendered mount**, so one policy can vary by scope id,
  declared capabilities, or channel direction — those three are the whole of
  `GuestSeamContext`.
- `WrapRuntime` returns the runtime the guest renders against; returning the
  host runtime unchanged is the identity policy.
- `GateBubble` returns the function the guest's dispatches run through;
  returning `rawBubble` unchanged is the identity policy.
- **With no seam installed, the guest is unprivileged.** Unregistered means
  unprivileged, never the host's own runtime.

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
