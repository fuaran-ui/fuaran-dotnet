# Fuaran.UI.StyleObserver.Abstractions

Type contract for the [Fuaran](https://github.com/) resolved-style observability feedback channel — the AI-facing surface that turns the colours the host CSS actually resolved into a small fixed set of interpretation-shaped flags the orchestrator can reason about.

This is the **read-back twin of `Fuaran.UI.LayoutObserver.Abstractions`**. Where the layout observer closes the loop on *geometry* (did the emitted tree lay out sensibly?), the style observer closes it on *resolved style* (is the emitted `Tone.Brand` legible, distinguishable, non-null?) — deterministically, against `getComputedStyle`, not a vision/VLM critic.

The flag set is fixed and additive-only post-ship: `ContrastBelowAA`, `InvisibleText`, `AccentIndistinct` (the manifest-free cases). Each carries the observed WCAG `ratio` it fired against. Concrete observers (`Fuaran.UI.StyleObserver`) ship two implementations — `BrowserStyleObserver` (Fable, `getComputedStyle` + the effective-background composite walk) and `InMemoryStyleObserver` (pure .NET, drives Expecto tests and the future eval gate). Identical flag-derivation logic — same input, same output as the browser observer.

The load-bearing algorithm is the **effective-background composite walk**: an element's foreground may be translucent and its background a stack of translucent tints over ancestor surfaces. `Flags.effectiveBackground` composites that stack (source-over) down to the first opaque layer (falling back to the browser's white canvas) so the contrast denominator is the colour the text actually sits on.

`FSharp.Core`-only and Fable-free (no `Browser.*` dependency) so the test project references this assembly and runs under pure .NET; concrete observers cast `obj → Browser.Types.Element` at the boundary.

Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
