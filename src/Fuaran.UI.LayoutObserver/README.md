# Fuaran.UI.LayoutObserver

Two concrete observers against [`Fuaran.UI.LayoutObserver.Abstractions`](https://www.nuget.org/packages/Fuaran.UI.LayoutObserver.Abstractions):

- **`BrowserLayoutObserver`** (Fable / browser) — `ResizeObserver`-backed; rAF-coalesced; reads `getBoundingClientRect` + `getComputedStyle`; emits to subscribers per the configured debounce + change-detection policy. The shipping path for production Fuaran applications.
- **`InMemoryLayoutObserver`** (pure .NET) — accepts hand-authored `LayoutFixture` snapshots; runs under Expecto; powers the Phase 12.G test suite and the Phase 12.E future eval gate. Identical flag-derivation logic — same input, same output as the browser observer.

Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
