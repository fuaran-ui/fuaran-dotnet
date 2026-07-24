# Fuaran.UI.Renderer.Core

The **emission-agnostic render spine** for the Fuaran UI language. This package
holds the parity-critical logic that every Fuaran renderer — the Feliz/React
client renderer (`Fuaran.UI.Renderer`) and the Feliz.ViewEngine server renderer
(`Fuaran.UI.Renderer.Server`) — must compute *identically*, factored out so the
two backends consume **one** source of truth instead of forking it.

**No Feliz, no Fable.React, no React** — this is the spine, not a renderer.
Under `dotnet build` the only package dependency is `FSharp.Core` (plus the
typed tree, `Fuaran.UI`). `Fable.Core` is referenced solely for
`Formatting.fs`'s `#if FABLE_COMPILER` `Intl` interop (the browser
locale-formatting path); the .NET build excludes that branch entirely, so the
server renderer consumes a pure-.NET assembly. Same Fable-portable posture as
`Fuaran.UI.LayoutObserver`. Everything here runs in a pure-.NET context and
transpiles cleanly under Fable.

## What's in here

| Module | Role |
|---|---|
| `Fuaran.UI.Renderer.Sanitize` | Render-time injection-safety floor — ExtraAttributes allowlist, URL-scheme gate, markdown raw-HTML sweep. |
| `Fuaran.UI.Renderer.Theme` | Semantic class-name vocabulary (`nodeClassName` / `kindClass` / `toneVar` / `weightVar` / `emphasisVar` / `motionVar`) + the `Theme` → CSS-variable projection + JSON round-trip. |
| `Fuaran.UI.Renderer.Formatting` | Locale-aware value formatting for `Binding.Format` (Intl on Fable, `System.Globalization` fallback on .NET). |
| `Fuaran.UI.Renderer.BindingResolver` | Resolves a typed `Binding<'T>` against `BindingSources` to a `Resolution<'T>` — the resolution that drives every data-bound component's state-slot dispatch. |
| `Fuaran.UI.Renderer.Accessibility` | Projects a `Node.Accessibility` trait to `(attr-name, attr-value)` pairs (the renderer-neutral aria/role attribute set). |
| `Fuaran.UI.Renderer.Ids` | Deterministic correlation ids (`deterministicCorrelationId`) so identical trees render byte-identical output — cache-stable + SSR/hydration-parity-safe — plus a `randomCorrelationId` escape hatch. |

## Namespace-preservation guarantee

The four modules extracted from `Fuaran.UI.Renderer` (`Sanitize`, `Theme`,
`Formatting`, `BindingResolver`) **keep their original
`Fuaran.UI.Renderer.*` module names**. `Fuaran.UI.Renderer` takes a package
reference on this assembly, so any consumer that imported
`Fuaran.UI.Renderer.Sanitize` / `.Theme` / `.Formatting` / `.BindingResolver`
sees no source change — the modules simply live in a different assembly now.
`Render.accessibilityAttributes` is re-exported from `Fuaran.UI.Renderer` for
the same reason.

This is the prerequisite extraction (Phase 138) for the Wave 18 SSR + isomorphic
hydration work: the server renderer reuses this spine without pulling Fable.
