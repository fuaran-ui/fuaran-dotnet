# Fuaran.UI.Renderer

Fable + React renderer for Fuaran UI trees. Walks the §4b `Node` typed-record tree from `Fuaran.UI` and dispatches per-`NodeKind` (Theme.fs / BindingResolver.fs / Render.fs) into React elements, built through the renderer's own internal binding. Every entry point returns the opaque `RenderedElement`, so no authoring DSL is in a host's restore closure; a host that authors in Feliz converts at the edge with `Fuaran.UI.Renderer.Feliz`. Targets both Fable consumers (browser bundle) and .NET-side test runners; the `<Nullable>disable</Nullable>` posture is required only for this project because its browser arms are written against Fable.Core's dynamic operators and Fable.Browser.Dom, which predate F# 10 nullness checks.

**Choosing an entry point:** `render` takes a `RenderContext` you build; the `renderWithSources*` family builds one for you and differs only in which axes it lets you supply — runtime, telemetry sink, runtime scope, correlation context. The grid, plus the `GuestSeam` capability policy for hosts that mount guests, is [`docs/RENDER-ENTRIES.md`](../../docs/RENDER-ENTRIES.md).

Pairs with `Fuaran.UI` (the language) and — when the UI surface is AI-driven — with a downstream orchestration tier shipped as a separate package family. Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
