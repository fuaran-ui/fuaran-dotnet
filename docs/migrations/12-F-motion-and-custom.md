# Phase 12.F migration – motion vocabulary + Custom renderer registration

**Shipped:** 2026-05-27
**Scope:** `Fuaran.UI` typed contract + `Fuaran.UI.Renderer` runtime + reference CSS.
**Stability impact:** Additive on every surface; no breaking change for existing F# authors. Hosts that implement `IFuaranRuntime` directly (i.e. NOT via `MutableRuntime` or `BrowserRuntime`) MUST add a `TryRenderCustom` member – see "Stability impact" below.

## What changes

### 1. `Motion` DU + `Node.Motion : Motion option`

`Fuaran.UI/Types.fs` adds:

```fsharp
[<RequireQualifiedAccess>]
type Motion =
    | None
    | PulseDuringLoad
    | FadeInOnMount
    | SlideInFromBelow
    | ShakeOnError
    | RotateOnRefresh
    | SlideInFromRight
    | ExpandCollapse
```

`Node<'Msg>` gains a new field `Motion : Motion option` (defaults to `None`). Smart constructors pass `Defaults.Motion.none` so pre-12.F authors see no behavioural change. Opt in via the postfix-pipe helper:

```fsharp
Fuaran.kpi "rev" { Defaults.kpi with Label = TextSource.Literal "Revenue"; ... }
|> Node.withMotion Motion.FadeInOnMount
```

The renderer emits `fuaran-motion-{token}` on the outer wrapper (alongside the existing `Theme.nodeClassName` output). The reference CSS supplies `@keyframes` for the four most common tokens (`PulseDuringLoad` / `FadeInOnMount` / `ShakeOnError` / `SlideInFromRight`); the remaining four (`SlideInFromBelow` / `RotateOnRefresh` / `ExpandCollapse` / `None`) ship as no-op class hooks consumers can author overrides for. `@media (prefers-reduced-motion: reduce)` disables every shipped keyframe rule.

> **§4h discrepancy noted.** The design doc lists `SpinDuringLoad` and `ScaleOnHover` where this phase ships `RotateOnRefresh` and `SlideInFromRight`. The phase body is authoritative for the implementation; reconciling the design doc to match is a follow-up roadmap maintenance pass.

### 2. `IFuaranRuntime.TryRenderCustom` + `RegisterCustomRenderer`

`IFuaranRuntime` gains:

```fsharp
abstract TryRenderCustom :
    moduleId : string * componentId : string * props : Map<string, JsonValue> -> ReactElement option
```

Default implementations:

- `DiagnosticRuntime` returns `None` (preserves pre-12.F placeholder-only behaviour).
- `MutableRuntime` (new – .NET-side, for tests + non-browser hosts) and `BrowserRuntime` consult an internal `CustomRendererRegistry`. Hosts register via `RegisterCustomRenderer(moduleId, componentId, renderFn)`.

`Render.fs`'s `NodeKind.Custom` arm now:

1. Calls `ctx.Runtime.TryRenderCustom(moduleId, componentId, props)`.
2. On `Some element`, emits the registered element verbatim.
3. On `None`, emits the labelled placeholder body inside a `<div class="fuaran-custom-placeholder">` (the existing dashed-border styling re-rooted from `.fuaran-custom`).

In both branches the result lives **inside** the outer wrapper, so the outer wrapper's `data-fuaran-node-id` + ARIA + motion + extra-attributes emission applies uniformly. Pre-12.F the inner `.fuaran-custom` div was its own wrapper without `data-fuaran-node-id` – Phase 12.G's `LayoutObserver` walked `[data-fuaran-node-id]` and missed Custom nodes. This gap is closed.

### 3. `Node.ExtraAttributes : Map<string, string> option` (High-H follow-on)

Additive on `Node<'Msg>`; `None` (the default) emits no extra attributes. `Some map` emits each entry as a DOM attribute via `prop.custom (key, value)` on the outer wrapper. Use the validator helper:

```fsharp
node
|> Node.withExtraAttribute "data-cy" "apply-preset"
|> Node.withExtraAttribute "aria-describedby" "help-text"
```

`Node.withExtraAttribute` restricts keys to `data-*` / `aria-*` prefixes. Non-conforming keys are silently dropped and the validator emits a warning via `eprintfn` (same shape `DiagnosticRuntime` uses).

**The AI authoring guide explicitly forbids the AI populating `ExtraAttributes`.** The §4d JSON wire shape omits this field on emit; it's a consumer-only escape hatch for test hooks (`data-cy`, `data-testid`) and analytics tags (`data-analytics`).

## Stability impact

- **`Fuaran.UI` package surface – additive.** Pre-12.F authors using only smart constructors see no behavioural change. `Motion` and `ExtraAttributes` fields default to `None`; the renderer emits the same shape as before for any Node without these fields populated.
- **`Fuaran.UI.Renderer` package surface – additive in spirit, interface-extending in practice.** `IFuaranRuntime` gains a new abstract member; hosts that implement `IFuaranRuntime` directly MUST add the member. Pre-1.0 minor adds per [`STABILITY.md`](../../STABILITY.md) – no major bump required. Hosts using `MutableRuntime` / `BrowserRuntime` / `DiagnosticRuntime` see no change (the member is implemented in those types).
- **DOM-attribute relocation.** Custom-node `data-fuaran-node-id` was previously absent; it's now emitted on the outer wrapper alongside every other Kind. Orchestration-tier `SnapshotRegistry` / `LayoutObserver` (Phase 12.G) gain Custom coverage; no consumer-side breakage.
- **CSS – `.fuaran-custom` → `.fuaran-custom-placeholder`.** The dashed-border placeholder styling moved from `.fuaran-custom` (which conflicted with the per-instance `fuaran-custom-{module}-{component}` class) to `.fuaran-custom-placeholder` (emitted only when `TryRenderCustom` returns `None`). Consumers who styled against `.fuaran-custom` directly should switch to either `.fuaran-kind-custom` (every Custom node) or `.fuaran-custom-placeholder` (only the fallback).

## Verification

```powershell
dotnet fantomas .
dotnet build Fuaran.sln
dotnet run --project src/Fuaran.UI.Tests       # Expecto — Motion + Custom + Accessibility + Theme suites
cd src/Fuaran.UI.Renderer; dotnet fable -o output --noCache; cd ../..
```

Acceptance criteria from the phase body:
- All 8 motion tokens round-trip through the renderer under `dotnet build` and `dotnet fable`.
- Registered custom renderer dispatches; unregistered Custom falls back to the placeholder with `data-fuaran-node-id` present on the outer wrapper.
- 4 shipped motion keyframes animate visibly under `samples/demo`; 4 no-op hooks emit a class but no motion.
- `ThemeTests.fs` byte-for-byte parity holds (motion `@keyframes` live outside `:root`, so the regex-scoped parser ignores them).

## Rollback

Revert the Phase 12.F commit. The added fields default to `None` so reverting `Render.fs` + `Types.fs` + `Defaults.fs` + `Fuaran.fs` together restores the pre-12.F shape. Consumer code authoring `Motion` / `ExtraAttributes` / `RegisterCustomRenderer` fails to compile against the reverted surface – the canonical signal that something was using the new API.

## See also

- [`HOST-STYLING-CHECKLIST.md`](../../HOST-STYLING-CHECKLIST.md) §5 – Reserved class fragments (Custom nodes), updated for `.fuaran-custom-placeholder`.
- [`AI_AUTHORING_GUIDE.md`](../AI_AUTHORING_GUIDE.md) – Motion + Custom sections.
- Phase body: `roadmap/phases/12-F-fern-motion-vocabulary-and-custom-renderer-registration.md`.
