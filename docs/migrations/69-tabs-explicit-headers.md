# Phase 69 migration – `Fuaran.tabs` explicit headers + ARIA / keyboard navigation + typed tab-tag overlay

**Shipped:** 2026-05-29
**Scope:** `Fuaran.UI` typed contract + `Fuaran.UI.Renderer` runtime + reference CSS + `Fuaran.UI.Ops` JSON decoder + `Fuaran.UI.OpStream.Abstractions` canonical encoder + `Fuaran.UI.PreEmitValidate`.
**Stability impact:** Additive on every surface. Pre-Phase-69 `Fuaran.tabs` callers see no behavioural change.

## What changes

### 1. `TabsSpec` gains four optional fields

`Fuaran.UI/Types.fs` extends `TabsSpec<'Msg>`:

```fsharp
and TabsSpec<'Msg> =
    { Orientation: Orientation
      Children: Node<'Msg> list
      ActiveIndex: Binding<int>
      OnSelect: int -> Action<'Msg>
      TabHeaders: TabHeader list option        // NEW
      TabTags: string list option              // NEW
      ActiveTag: Binding<string> option        // NEW
      OnSelectTag: (string -> Action<'Msg>) option }  // NEW

and TabHeader =
    { Label: TextSource
      Icon: IconSource option
      Disabled: Binding<bool> option }
```

`Defaults.tabs<'Msg>` initialises every new field to `None`. `Defaults.tabHeader` ships an empty-label / no-icon / no-disabled header for `with`-record updates.

Strict-additive – pre-Phase-69 `Fuaran.tabs` calls that constructed `TabsSpec` via `{ Defaults.tabs<'Msg> with ... }` see no behavioural change. Hand-written direct record literals (`{ Orientation = ...; Children = ...; ActiveIndex = ...; OnSelect = ... }`) need the four new field assignments – search for `TabsSpec` references in your codebase to find them.

### 2. `Fuaran.tabsTagged` smart-ctor

`Fuaran.UI/Fuaran.fs` adds:

```fsharp
let tabsTagged (id: string) (spec: TabsSpec<'Msg>) : Node<'Msg>
```

Requires both `TabHeaders` and `TabTags` to be populated; throws a clear authoring error otherwise. The "I'm using the typed overlay" entry point – pair it with `ActiveTag` + `OnSelectTag` for DU-typed active-tab state.

Pre-Phase-69 `Fuaran.tabs` remains for the integer-indexed authoring shape – no need to migrate existing callers.

### 3. Renderer: full ARIA tablist semantics + keyboard navigation

`Fuaran.UI.Renderer/Render.fs` emits:

- `role="tablist"` + `aria-orientation` on the tab bar.
- Per-tab `role="tab"` + `aria-selected` + `aria-controls` + `tabindex` (active = 0, inactive = -1 – the standard ARIA roving-tabindex pattern).
- Per-panel `role="tabpanel"` + `aria-labelledby` + `tabindex=0`.
- Stable HTML `id`s derived from the parent NodeId: `<parent-id>-tab-N` and `<parent-id>-panel-N`.

Keyboard navigation on the tab bar:

| Key | Action |
|---|---|
| `ArrowLeft` / `ArrowRight` (horizontal) | Move focus + selection to the next / previous **enabled** tab. Wraps at ends. |
| `ArrowUp` / `ArrowDown` (vertical – driven by `Orientation = Vertical`) | Same as Left/Right but along the vertical axis. |
| `Home` | First enabled tab. |
| `End` | Last enabled tab. |
| `Enter` / `Space` | Activate the focused tab (idempotent under automatic activation). |

Focus management uses `document.getElementById(<tab-id>).focus()` against the stable IDs – no `React.useRef` thread needed, the IDs are the focus addresses. Disabled tabs (per-tab `Disabled` binding resolving to `true`) are skipped during arrow-key traversal and rendered with `aria-disabled="true"` + the new `.fuaran-tab-disabled` class.

### 4. Typed tab-tag overlay

When both `TabTags` and `ActiveTag` are `Some`, the renderer:

1. Resolves the `ActiveTag` binding to a string.
2. Finds the string's position in `TabTags`.
3. Uses that position as the active index.

Falls back to `ActiveIndex` when either is `None`, when the resolved tag does not appear in `TabTags`, or when the tag binding fails to resolve. The integer-indexed `OnSelect` always fires on tab click; if `OnSelectTag` is also `Some`, it fires with the per-tab string tag. The integer-indexed `ActiveIndex` / `OnSelect` shape remains the canonical wire form – the tag overlay is consumer ergonomics on top.

### 5. Theme: 7 new tab-bar tokens

`Fuaran.UI/Types.fs` adds `TabBar` record + `Theme.TabBar` field:

```fsharp
and TabBar =
    { PaddingY: string
      PaddingX: string
      IndicatorColor: ColorVar
      IndicatorHeight: string
      TextColor: ColorVar
      TextActiveColor: ColorVar
      TextHoverColor: ColorVar }
```

`Defaults.tabBar` ships values that mirror the SDK `Layout.Tabs.tabGroup` shape: `8px` y-padding, `24px` x-padding, brand-fg indicator at `2px` height, subdued-fg / brand-fg / brand-hover-fg text triple. The colour fields default to `var(--fuaran-tone-*)` references so themes that override the brand stops carry through to tabs automatically.

Reference CSS at `content/fuaran-reference.css` mirrors this byte-for-byte. The `Theme.toCssVariables` count rises from 135 to **142**. The byte-for-byte regression in `ThemeTests` is updated to match.

`.fuaran-tab::after` now carries the bottom-border indicator (was a `border-bottom` on the button itself); this lets the indicator height / colour vary independently from any focus / hover border the button might pick up.

### 6. JsonDecode + CanonicalJson encoder forward-coupling

The canonical-JSON encoder (`Fuaran.UI.OpStream.Abstractions/CanonicalJson.fs`) emits `tabHeaders` / `tabTags` / `activeTag` only when `Some` – existing fixtures encode byte-identical. `onSelectTag` is a closure, undecoded (mirrors `OnSelect`).

The JSON decoder (`Fuaran.UI.Ops/JsonDecode.fs`) reads the same shape back. `decodeTabsSpec` learns four new optional-field branches; the integer-indexed `activeIndex` / `onSelect` path stays the canonical decode shape, defaulting to `Binding.Static 0` / no-op `Action.Chain []` exactly as pre-Phase-69.

A new round-trip fixture (`tabsExplicitHeaders`) in `Fuaran.UI.JsonDecode.Tests/Fixtures.fs` exercises the additive surface end-to-end.

### 7. PreEmitValidate FUARAN047 / 048 / 049

`Fuaran.UI/PreEmitValidate.fs` gains three new defect cases that map to the documented FUARAN codes:

| Code | Severity | Trigger | Defect case |
|---|---|---|---|
| FUARAN047 | Error | `TabHeaders.Length ≠ Children.Length` | `TabHeaderCountMismatch(nodeId, headerCount, childrenCount)` |
| FUARAN048 | Error | `TabTags.Length ≠ Children.Length` | `TabTagCountMismatch(nodeId, tagCount, childrenCount)` |
| FUARAN049 | Warning | `ActiveTag = Some _` but `TabTags = None` | `TabActiveTagWithoutTags(nodeId)` |

`PreEmitValidate.validate` reports all three in a single pass alongside the existing duplicate-NodeId / empty-NodeId / empty-Custom-identifier checks.

## Card-heading-inference fallback

Pre-Phase-69, the renderer inferred tab labels from the child's `LayoutKind.Card.Heading` (or fell back to the child's raw `NodeId` for non-Card children). That path still runs when `TabHeaders = None`. The migration is opt-in.

A future phase may emit a `console.warn` advisory when the child is NOT a Card AND `TabHeaders = None` – for now the fallback is silent (back-compat).

## Custom-escape pattern for non-Fuaran tab bodies

The walkthrough's pre-Phase-69 framing – "Fuaran.tabs would require wrapping the Feliz tab bodies as `Node<Msg>`, and there's no Custom-node escape sufficient for embedding live Feliz" – is **stale post-Phase-12.F**. `NodeKind.Custom` + `IFuaranRuntime.TryRenderCustom` is exactly the affordance the walkthrough said was missing.

The recommended pattern for a tab body that cannot reasonably translate to typed Fuaran (e.g. a Feliz heatmap with per-cell colour gradients):

```fsharp
// Author side — declare a Custom node placeholder
let heatmapTab : Node<Msg> =
    Fuaran.custom "heatmap-tab" "Individual" "HeatmapTab" Map.empty

// Runtime side — register the renderer at mount time
runtime.RegisterCustomRenderer("Individual", "HeatmapTab", fun _moduleId _componentId _props ->
    HeatmapView.render model dispatch)

// Tabs side — mix Fuaran-native and Custom-wrapped bodies
Fuaran.tabsTagged
    "results-tabs"
    { Defaults.tabs<Msg> with
        Children = [ breakdownTab; heatmapTab; incomeTab ]
        TabHeaders =
            Some
                [ { Defaults.tabHeader with Label = TextSource.Literal "Breakdown" }
                  { Defaults.tabHeader with Label = TextSource.Literal "Heatmap" }
                  { Defaults.tabHeader with Label = TextSource.Literal "Income" } ]
        TabTags = Some [ "breakdown"; "heatmap"; "income" ]
        ActiveTag = Some(binding.computed (fun _ -> tagOf model.ActiveResultTab))
        OnSelectTag = Some(fun tag -> Action.dispatch (SetActiveTab(tabOf tag))) }
```

The model-side `ActiveResultTab : ResultTab` (a typed DU) stays as-is; `tagOf` / `tabOf` helpers bridge between the wire `string` tag and the typed DU.

## When to use the typed-tag overlay

| Model-side state shape | Use |
|---|---|
| `int` | `Fuaran.tabs` with `ActiveIndex` / `OnSelect` |
| Typed DU (`Overview` / `Detail` / `Audit`) | `Fuaran.tabsTagged` with `TabTags` / `ActiveTag` / `OnSelectTag` |
| URL deep-link slug | Either – depends on whether the slug is naturally an int or a string |

The rule: use the typed-tag overlay when the model-side active-tab state is a DU rather than an int, so the consumer doesn't maintain a parallel `int <-> 'TabTag` mapping. The integer-indexed path stays the canonical wire form (one source of truth); the tag overlay is ergonomics on top.

## Worked example

The pilot app's client view is the canonical reference – see the pilot-app walkthrough (maintainers' workspace docs) "Scope decisions" table for the pre-Phase-69 framing being retired.

## Coverage

- `Fuaran.UI.Tests/PreEmitValidateTests.fs` – four new tests covering FUARAN047 / 048 / 049 + a positive alignment case.
- `Fuaran.UI.JsonDecode.Tests/Fixtures.fs` – `tabsExplicitHeaders` fixture; the existing `RoundTripTests` auto-pick it up.
- `Fuaran.UI.Tests/ThemeTests.fs` – `exoticTheme` extended with a `TabBar` block; the byte-for-byte regression count updated 135 → 142.

## Forward coupling

Any future phase adding a new `TabHeader` field MUST update:

1. The CanonicalJson encoder's `encodeTabHeader` arm.
2. The JsonDecode decoder's `decodeTabHeaderEntry` arm.
3. `Defaults.tabHeader`.
4. The PreEmitValidate FUARAN047/048/049 invariants if the new field interacts with header/children alignment.
