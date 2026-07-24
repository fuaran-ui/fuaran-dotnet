# Phase 66 migration – `FormFieldKind.SegmentedChoice` (visible-options exclusive-choice primitive)

**Shipped:** 2026-05-30
**Scope:** `Fuaran.UI` typed contract + `Fuaran.UI.Renderer` runtime + reference CSS + `Fuaran.UI.Ops.JsonDecode` decoder + `Fuaran.UI.Validator` AST walker + `Fuaran.UI.OpStream.Abstractions.CanonicalJson` encoder + sample-catalog axis.
**Stability impact:** Additive across every surface. New parallel DU cases on `FormFieldKind` and `FilterKind`; new `Segmented` record on `Theme`; four new `--fuaran-segmented-*` CSS tokens. No reordering, renaming, or signature changes to existing DU cases, smart-ctor entry points, decoder branches, validator codes, or theme tokens. Pre-Phase-66 consumers see no behavioural change.

## What changes

### 1. `FormFieldKind.SegmentedChoice` + `FilterKind.SegmentedFilter`

`Fuaran.UI/Types.fs` adds (parallel-additive DU cases – `Choice` / `ChoiceFilter` remain the dropdown shapes):

```fsharp
type FormFieldKind<'Msg> =
    | ...
    | Choice of options * value * onChange                  // unchanged — dropdown
    | SegmentedChoice of                                     // NEW — visible options
        options: Binding<SelectOption list> *
        value: Binding<string option> *
        onChange: (string option -> Action<'Msg>) *
        orientation: Orientation

type FilterKind<'Msg> =
    | ...
    | ChoiceFilter of options * value * onChange            // unchanged — dropdown
    | SegmentedFilter of                                     // NEW — visible options
        options: Binding<SelectOption list> *
        value: Binding<string option> *
        onChange: (string option -> Action<'Msg>) *
        orientation: Orientation
```

The `Orientation` payload chooses between two visual surfaces:

- `Orientation.Horizontal` – pill-shaped segmented control. Inline `<div role="radiogroup">` of `<button role="radio">` elements styled with `--fuaran-segmented-*` tokens. Arrow keys cycle (wrap-around). Use for `≤5` short labels that read well side-by-side.
- `Orientation.Vertical` – native radio-button list. `<fieldset>` of `<input type="radio">` + `<label>` pairs grouped by shared `name`; the browser handles arrow-key navigation. Use for longer labels or when the choice forms a vertical settings preference.

### 2. `FormFieldKind.segmentedChoice` + `FilterKind.segmentedFilter` smart-ctors

`Fuaran.UI/Fuaran.fs` adds the smart-ctor entry points:

```fsharp
val FormFieldKind.segmentedChoice :
    options: Binding<SelectOption list> ->
    value: Binding<string option> ->
    onChange: (string option -> Action<'Msg>) ->
    orientation: Orientation ->
    FormFieldKind<'Msg>

val FilterKind.segmentedFilter :
    options: Binding<SelectOption list> ->
    value: Binding<string option> ->
    onChange: (string option -> Action<'Msg>) ->
    orientation: Orientation ->
    FilterKind<'Msg>
```

Authoring shape:

```fsharp
{ Defaults.formField<Msg> with
    Id = "metric"
    Label = TextSource.Literal "Metric"
    Kind =
        FormFieldKind.segmentedChoice
            (binding.``static``
                [ { Value = "effective"; Label = TextSource.Literal "Effective rate" }
                  { Value = "marginal";  Label = TextSource.Literal "Marginal rate" }
                  { Value = "takeHome";  Label = TextSource.Literal "Take-home %" } ])
            (binding.state "metric" None)
            (SetMetric >> Action.dispatch)
            Orientation.Horizontal }
```

### 3. New theme record `Segmented` + four new CSS tokens

`Fuaran.UI/Types.fs` adds `Segmented` to `Theme`:

```fsharp
and Segmented =
    { Background: ColorVar
      ActiveBackground: ColorVar
      ActiveForeground: ColorVar
      DividerColor: ColorVar }
```

`Fuaran.UI/Defaults.fs` ships defaults that route through the tone palette:

```fsharp
let segmented: Segmented =
    { Background = ColorVar.CssRaw "var(--fuaran-tone-subdued-bg, #f3f4f6)"
      ActiveBackground = ColorVar.CssRaw "var(--fuaran-tone-default-bg, #ffffff)"
      ActiveForeground = ColorVar.CssRaw "var(--fuaran-tone-brand-fg, #1d4ed8)"
      DividerColor = ColorVar.CssRaw "var(--fuaran-tone-default-border, #e5e7eb)" }
```

Reference CSS at `fuaran-reference.css` mirrors this byte-for-byte:

```css
--fuaran-segmented-bg: var(--fuaran-tone-subdued-bg, #f3f4f6);
--fuaran-segmented-active-bg: var(--fuaran-tone-default-bg, #ffffff);
--fuaran-segmented-active-fg: var(--fuaran-tone-brand-fg, #1d4ed8);
--fuaran-segmented-divider-color: var(--fuaran-tone-default-border, #e5e7eb);
```

Apps that override `Tones.Brand` / `Tones.Subdued` / `Tones.Default` see the segmented surface re-tint automatically – no separate override required for the common case. Apps that want explicit overrides set them via the typed `Theme` record.

### 4. JSON wire shape

The CanonicalJson encoder serialises:

```json
{
  "$type": "SegmentedChoice",
  "onChange": "<closure>",
  "options": { ... Binding<SelectOption list> ... },
  "orientation": "Horizontal",
  "value": { ... Binding<string option> ... }
}
```

The orientation field carries the canonical Orientation discriminator (`"Horizontal"` / `"Vertical"` uppercase – matching `StackSpec` / `TabsSpec`'s existing convention). `SegmentedFilter` uses the same shape under the `"$type": "SegmentedFilter"` discriminator.

The decoder is forward-coupled (Phase 12.E.0 rule): every fixture in `Fuaran.UI.JsonDecode.Tests/Fixtures.fs` that exercises a SegmentedChoice / SegmentedFilter case round-trips byte-equal.

### 5. Validator rule `FUARAN045` (Warning, advisory)

A `FormFieldKind.segmentedChoice` whose `options` argument is a statically-detectable `Binding.Static [ ... ]` (or `binding.static [ ... ]`) list literal with **more than 7 items** raises FUARAN045. Segmented controls work best with ≤5 visible options; >7 should reach for `FormFieldKind.Choice` (dropdown) instead – the visible-options trade-off inverts past that point.

The rule is advisory – Warning, not Error – so the build still passes during incremental adoption / experimentation. The walker only fires on statically-detectable shapes; an `options` bound via `Binding.Query` / `Binding.State` / `Binding.Computed` is silent (no compile-time count).

## Decision tree – `Choice` vs `SegmentedChoice`

| Situation | Reach for |
|---|---|
| ≤5 short options, user benefits from seeing them all at once | `SegmentedChoice` (Horizontal) |
| ≤7 options with longer labels (e.g. preference list) | `SegmentedChoice` (Vertical) |
| Options live in a long, dynamically-sized list (categories, countries, customers) | `Choice` (dropdown) |
| Selecting between content panels (not value options) | `Tabs` – distinct ARIA role (`tablist` not `radiogroup`) |
| The choice must be coloured / iconified per option | `Choice` today; richer per-option styling is a separate phase |

## Migration steps

- **New code**: use `FormFieldKind.segmentedChoice` / `FilterKind.segmentedFilter` directly when the visible-options shape fits the UX.
- **Existing `FormFieldKind.Choice` consumers**: no action required. The dropdown shape is unchanged.
- **Custom Feliz escapes for segmented controls**: replace with `FormFieldKind.SegmentedChoice` once Phase 66 ships in your pinned `Fuaran.UI` pack – the pilot app's `metricSelector` is the canonical translation example (tracked separately under Phase 68 cross-repo work).
- **Custom themes**: optional. Add a `Segmented` field if you want non-default colour stops; the `with` syntax inherits `Defaults.segmented` automatically.

## Anti-patterns to avoid

- **Don't repurpose `Choice` with an "as segmented" hint.** Parallel DU cases keep the contract clean. The `<select>` element and a `<div role="radiogroup">` have different ARIA semantics, different keyboard models, and different visual cascades.
- **Don't conflate segmented controls with tabs.** Tabs select between content panels (`role="tablist"`); segmented controls select among value options (`role="radiogroup"`).
- **Don't ship the Horizontal variant without keyboard arrow nav.** The renderer wires `onKeyDown` for Arrow Left/Right (cycles selection) + Home / End (jump to first / last). The Vertical variant relies on the browser's native `<input type="radio">` arrow-nav since the inputs share a `name`.
- **Don't override `--fuaran-segmented-*` via Tailwind classes.** Use the CSS variables. Orientation surfaces as the `fuaran-segmented-horizontal` / `fuaran-segmented-vertical` class fragment; layout rules key off that.

## Related

- [Phase 12.P – Feliz-parity additive bursts](12-P-feliz-parity-type-contract-completion.md) – earlier instance of the same shape: parallel additive DU cases for closing Feliz-escape gaps.
- [Phase 63 – `NumberRanged`](63-number-field-constraints.md) – parallel-additive precedent for `FormFieldKind`.
- [Phase 69 – Tabs explicit headers](69-tabs-explicit-headers.md) – `aria-orientation` + arrow-key navigation precedent.
