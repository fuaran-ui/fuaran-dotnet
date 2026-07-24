# Fuaran.UI.LayoutObserver.Abstractions

Type contract for the [Fuaran](https://github.com/) layout-observability feedback channel — the AI-facing surface that turns raw browser geometry into a small fixed set of interpretation-shaped flags the orchestrator can reason about.

The flag set is fixed and additive-only post-ship: `OverflowHorizontal`, `OverflowVertical`, `ZeroDimension`, `SqueezedToMin`, `ChildClippedByAncestor`, `AspectRatioWildlyOff`. Concrete observers (`Fuaran.UI.LayoutObserver`) ship two implementations — `BrowserLayoutObserver` (Fable, `ResizeObserver`-backed) and `InMemoryLayoutObserver` (pure .NET, drives Expecto tests and the Phase 12.E eval gate).

Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
