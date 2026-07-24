# Fuaran.UI.Renderer

Fable + React + Feliz renderer for Fuaran UI trees. Walks the §4b `Node` typed-record tree from `Fuaran.UI` and dispatches per-`NodeKind` (Theme.fs / BindingResolver.fs / Render.fs) into Feliz elements. Targets both Fable consumers (browser bundle) and .NET-side test runners; the `<Nullable>disable</Nullable>` posture is required only for this project because Feliz 3.3.3 + Fable.React 5.x predate F# 10 nullness checks.

Pairs with `Fuaran.UI` (the language) and — when the UI surface is AI-driven — with a downstream orchestration tier shipped as a separate package family. Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
