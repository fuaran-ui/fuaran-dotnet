# Fuaran.UI.LayoutObserver – operator guide

**Phase 12.G.** Opt-in companion that gives the closed-loop AI orchestrator a window into the running UI's *geometry* – not just its semantic state. The orchestrator can already see what fields the user has filled, what buttons are visible, what rows are selected (via `_platform.ui.inspect_active_module`); the layout observer adds *how it actually rendered* – overflows, squeezed panels, zero-dimension children, clipped descendants, wildly-off aspect ratios.

Together with `Fuaran.UI.Telemetry` (post-12.T – what the AI did) and `Fuaran.UI.Eval` (post-12.E – does the release gate still pass), it forms the three sides of the orchestrator's situational awareness.

## Why a separate channel?

Today's `AIStateSnapshot` is purely semantic: `Fields = Map<...>; Buttons = Map<...>; Selections = Map<...>`. The AI can reason about *what state the UI is in*, but not about *whether the UI actually renders correctly*. Vibe-coding sessions have a recurring failure mode: the AI emits a tree that looks fine on review but renders broken on the user's viewport – a Stack squeezed flat by an oversized sibling, a child clipped by a parent's `overflow: hidden`, a chart with collapsed aspect ratio. Without a geometric channel, the orchestrator can't see this; the user has to.

The layout observer closes that gap. The AI gets a fixed set of **interpretation-shaped flags**, not raw pixels:

| Flag | Detected when |
|---|---|
| `OverflowHorizontal` | `scrollWidth > clientWidth` AND `overflow-x ≠ visible` |
| `OverflowVertical` | `scrollHeight > clientHeight` AND `overflow-y ≠ visible` |
| `ZeroDimension(axis)` | The element's `width` or `height` ≤ 0.5 px |
| `SqueezedToMin(axis)` | Rendered dimension hits the element's computed `min-width` / `min-height` |
| `ChildClippedByAncestor` | Element's bounding rect extends beyond the nearest clipping ancestor |
| `AspectRatioWildlyOff(factor)` | Observed `width / height` ratio diverges from declared `aspect-ratio` by ≥ `factor` (default 3.0x) |

The flag set is **additive-only post-ship.** Each flag is a load-bearing AI input – adding new ones is fine; redefining existing ones breaks every prompt cache that pattern-matches against them.

## Opt-in wiring

The observer is **opt-in** – none is wired by default, so there is zero overhead until
you ask for it. Construct it from the public `Fuaran.UI.LayoutObserver` package; the v1
defaults are a 100ms rAF-coalesced debounce, a 3.0x aspect-ratio threshold, and
change-only emission.

```fsharp
open Fuaran.UI.LayoutObserver

// Default options:
let observer = BrowserLayoutObserver.create ()

// …or tune them:
let tuned =
    { LayoutObserverOptions.defaults with
        DebounceMs = 50
        AspectRatioWildlyOffFactor = 2.0 }

let observerTuned = BrowserLayoutObserver.createWith tuned
```

That is the whole language-tier surface: the observer plus the flag-derivation logic.
**Surfacing the observation to an AI introspection layer** – so a runtime tool such as
`_platform.ui.inspect_active_module` picks the observer up and attaches a `layout` block
to its envelope, and the optional `_platform.ui.inspect_layout` drill-in tool is
registered – is the **runtime tier's** concern, one tier up. A runtime-tier consumer
opts in through that tier's own install step (a single `withLayoutObservation`-style
call); see the runtime tier's documentation for the exact builder. The wire shape that
flow produces is shown under [Wire format](#wire-format) below.

### Headless test substrate (Expecto / Phase 12.E eval)

```fsharp
open Fuaran.UI.LayoutObserver

let observer = InMemoryLayoutObserver.create ()

// Register a fixture per NodeId.
observer.RegisterFixture(
    "panel-1",
    { Input =
        { Flags.LayoutInput.empty 100.0 50.0 with
            ScrollWidth = Some 250.0
            ClientWidth = Some 100.0
            OverflowX = Some "hidden" }
      Parent = None })

// Assert flag derivation.
let observation = (observer :> ILayoutObserver).Observe("panel-1") |> Option.get
List.contains LayoutFlag.OverflowHorizontal observation.Flags  // true
```

Same `Flags.derive` logic runs under both pipelines – the in-memory observer is the substrate-free path for tests and the future Phase 12.E eval gate.

## How it discovers elements

The renderer emits `data-fuaran-node-id="..."` on every Fuaran node (Render.fs:1085). `BrowserLayoutObserver` self-discovers via:

1. **Initial DOM scan** at construction – `document.querySelectorAll("[data-fuaran-node-id]")` and register each.
2. **MutationObserver** on `document.body` (subtree, childList) – re-scan on every mutation; register new elements, unregister departed ones.
3. **ResizeObserver** on each registered element – fires on geometry change.

This means: **no renderer ref callbacks**. The renderer stays React-lifecycle-agnostic; the observer's MutationObserver is the only DOM watcher.

## Cost model

Acceptance criterion (verified in `Fuaran.UI.LayoutObserver.Tests`): a 1000-event resize burst against 50 registered nodes produces ≤ 10 subscriber emissions under the default options.

- **rAF coalescing**: ResizeObserver may fire many times per frame; all dirty NodeIds queue into a HashSet and flush once per rAF tick.
- **Wall-clock floor**: `LayoutObserverOptions.DebounceMs` (default 100ms) rate-limits per-node emissions further. A second resize burst within 100ms is suppressed.
- **Change-detection**: with `EmitOnFlagChangeOnly = true` (the v1 default), a tick that produces the same flag set as the previous emission is suppressed entirely. Initial emission per NodeId always fires.

## Wire format

When an observer is wired, `_platform.ui.inspect_active_module` returns:

```json
{
  "moduleId": "sales-analysis",
  "activePage": "/dataset",
  "snapshot": {
    "Fields": { "country": "US", "brand": "Acme" },
    "Buttons": {},
    "Selections": {},
    "Layout": {
      "panel-1": {
        "nodeId": "panel-1",
        "width": 320.00,
        "height": 240.00,
        "viewportX": 12.50,
        "viewportY": 80.00,
        "flags": [{"kind": "OverflowHorizontal"}]
      },
      "stack-2": {
        "nodeId": "stack-2",
        "width": 0.00,
        "height": 240.00,
        "viewportX": 332.50,
        "viewportY": 80.00,
        "flags": [{"kind": "ZeroDimension", "axis": "width"}]
      }
    }
  },
  "affordances": { /* ... */ }
}
```

When no observer is wired, the `Layout` field is absent entirely – **not** `"Layout": null`. The wire payload is byte-identical to pre-12.G behaviour for any host that hasn't opted in.

## `_platform.ui.inspect_layout` – drill-in AI tool

Registered only when an observer is wired. Args: `{ "nodeId"?: string }`.

```text
# Single-node mode
> _platform.ui.inspect_layout { "nodeId": "panel-sales-grid" }
{ "kind": "single",
  "observation": { "nodeId": "panel-sales-grid", "width": 800.00, ... } }

# Tree mode (no nodeId — returns the active module's tree)
> _platform.ui.inspect_layout {}
{ "kind": "tree",
  "observations": [ {...}, {...}, ... ] }

# Miss envelopes
> _platform.ui.inspect_layout { "nodeId": "ghost" }
{ "kind": "miss",
  "reason": "no-such-node",
  "detail": "No registered node with id 'ghost'. ..." }
```

## Eval-harness integration (forward pointer)

Phase 12.E (AI emission micro-eval) introduces a release-gate eval suite. The `InMemoryLayoutObserver` will become the substrate for a fifth gate – `layout-clean` – that asserts AI-emitted UI renders flag-clean against a prompt's declared viewport size before the emission is allowed through. v1 of this phase (12.G) ships the substrate; the eval gate ships when 12.E lands.

Similarly, Phase 12.T's `IFuaranTelemetrySink` will eventually carry `LayoutFlagRaised` / `LayoutFlagCleared` records for cross-session drift analysis. v1 doesn't depend on 12.T; the integration is a follow-on.

## See also

- [`STABILITY.md`](../../STABILITY.md) – Fuaran API stability policy. Phase 12.G is additive-only; no breaking change to any stable surface.
- [`Fuaran.UI.LayoutObserver.Abstractions`](../../src/Fuaran.UI.LayoutObserver.Abstractions/) – type contract.
- [`Fuaran.UI.LayoutObserver`](../../src/Fuaran.UI.LayoutObserver/) – `BrowserLayoutObserver` + `InMemoryLayoutObserver`.
- [`Fuaran.UI.LayoutObserver.Tests`](../../src/Fuaran.UI.LayoutObserver.Tests/) – 29-case test suite.
