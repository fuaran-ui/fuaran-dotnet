# Phase 63 migration – `FormFieldKind.NumberRanged` (numeric range + step constraints)

**Shipped:** 2026-05-28
**Scope:** `Fuaran.UI` typed contract + `Fuaran.UI.Renderer` runtime + `Fuaran.UI.Ops.JsonDecode` decoder + `Fuaran.UI.Validator` AST walker + `Fuaran.UI.OpStream.Abstractions.CanonicalJson` encoder.
**Stability impact:** Additive across every surface. No reordering, renaming, or signature changes to existing DU cases, smart-ctor entry points, decoder branches, or validator codes. Pre-Phase-63 consumers see no behavioural change.

## What changes

### 1. `FormFieldKind.NumberRanged` + `NumberFieldConstraints`

`Fuaran.UI/Types.fs` adds (parallel-additive DU case + new record):

```fsharp
type FormFieldKind<'Msg> =
    | ...
    | Number of value: Binding<float> * onChange: (float -> Action<'Msg>)     // unchanged
    | NumberRanged of                                                          // NEW
        value: Binding<float> *
        onChange: (float -> Action<'Msg>) *
        constraints: NumberFieldConstraints

and NumberFieldConstraints =
    { Min: float option
      Max: float option
      Step: float option }
```

Pre-Phase-63 authors keep using `FormFieldKind.Number(value, onChange)` – its shape, renderer behaviour, and wire form are byte-identical. Authors who need HTML-level bounds use the new `NumberRanged` case (typically via the smart-ctor below).

### 2. `Defaults.numberFieldConstraints`

`Fuaran.UI/Defaults.fs` adds the all-`None` default:

```fsharp
let numberFieldConstraints: NumberFieldConstraints =
    { Min = Option.None
      Max = Option.None
      Step = Option.None }
```

### 3. `FormFieldKind.numberRanged` / `numberStepped` smart-ctors

`Fuaran.UI/Fuaran.fs` adds a new `[<RequireQualifiedAccess>] module FormFieldKind` carrying:

```fsharp
val numberRanged :
    value: Binding<float> ->
    onChange: (float -> Action<'Msg>) ->
    ?min: float ->
    ?max: float ->
    ?step: float ->
    FormFieldKind<'Msg>

val numberStepped :
    value: Binding<float> ->
    onChange: (float -> Action<'Msg>) ->
    step: float ->
    FormFieldKind<'Msg>
```

Canonical authoring shape:

```fsharp
FormFieldKind.numberRanged
    (value = binding.state "year" 2024.0)
    (onChange = SetYear >> Action.dispatch)
    (min = 1979.0)
    (max = 2028.0)
```

### 4. Renderer projection (`Fuaran.UI.Renderer`)

`Render.fs` adds the `FormFieldKind.NumberRanged` arm next to the existing `Number` arm:

- `Binding.Local _` → routes through `LocalBindings.localNumberInput`, which now takes a `constraints: NumberFieldConstraints` field and projects each `Some` value into `prop.min` / `prop.max` / `prop.step` on the rendered `<input type=text inputMode=numeric>`.
- Non-`Local` bindings → render `<input type=number>` with `prop.min` / `prop.max` / `prop.step` emitted for each `Some` constraint.

The existing `FormFieldKind.Number` arm passes `Defaults.numberFieldConstraints` (all-None) when invoking the shared `localNumberInput`, so its rendered output is byte-identical to pre-Phase-63.

### 5. JsonDecode forward-coupling (`Fuaran.UI.Ops.JsonDecode`)

The `decodeFormFieldKind` discriminator switch grows a new `"NumberRanged"` branch. Wire shape:

```json
{
  "$type": "NumberRanged",
  "value": { "$type": "Static", "value": 2024.0 },
  "onChange": "<closure>",
  "min": 1979.0,
  "max": 2028.0,
  "step": 1.0
}
```

`min` / `max` / `step` are optional at the wire – each absent key decodes to `None` (mirrors the encoder's omit-when-None discipline per algorithm rule 4). The pre-Phase-63 `"Number"` wire shape is unchanged.

### 6. CanonicalJson encoder (`Fuaran.UI.OpStream.Abstractions`)

`encodeFormFieldKind` adds the matching `NumberRanged` arm that emits the `$type` discriminator plus only the present (`Some`) constraint fields. Hash output for a pre-Phase-63 `Number` payload is unchanged.

### 7. Validator rule – FUARAN051 (advisory)

`Fuaran.UI.Validator/NumberFieldRangeCheck.fs` adds a new narrow AST walker that finds `FormFieldKind.numberRanged` call sites whose `value` is a `Binding.Static <lit>` literal that falls outside the declared `[Min, Max]` interval. Mirrors `ScalarRangeCheck` (FUARAN050) but at the form-field call site rather than the `progress` ctor. Severity: `Warning` – advisory; does not fail the build.

Message shape:

```
FormFieldKind.numberRanged: the Binding.Static value literal 1900 is outside the
declared [Min, Max] range. The renderer will pass the literal through to the
browser's <input type=number>, which will clamp or reject the field on
submission. supportedRange={"kind":"decimalRange","min":1979,"max":2028}
```

Recovery suggestion: `set value to a number in [1979, 2028]`.

Statically undetectable shapes (no finding):
- `value` is a `Binding.Query` / `Binding.State` / `Binding.Computed` (no compile-time value).
- `min` / `max` are non-literal expressions.
- The call uses the lowered `FormFieldKind.NumberRanged(...)` DU constructor directly.

## Author migration

Pre-Phase-63 author code keeps compiling and rendering identically – no required changes. To gain HTML-level bounds for a numeric field, rewrite:

```fsharp
// Before — no DOM-level bounds; the browser accepts any float.
{ Defaults.formField<Msg> with
    Id = "year"
    Kind = FormFieldKind.Number(binding.state "year" 2024.0, SetYear >> Action.dispatch) }
```

to:

```fsharp
// After — `min` / `max` propagate to HTML `min` / `max`; FUARAN051 catches
// out-of-range Binding.Static literals at build time.
{ Defaults.formField<Msg> with
    Id = "year"
    Kind =
        FormFieldKind.numberRanged
            (binding.state "year" 2024.0)
            (SetYear >> Action.dispatch)
            (min = 1979.0)
            (max = 2028.0) }
```

For Local-bound formatted-numeric fields (`binding.local`), the same `numberRanged` smart-ctor wraps the `Binding.Local` and the renderer's local-buffer `<input type=text inputMode=numeric>` receives the constraints on its DOM attributes.

## Open follow-ups

- FUARAN051 currently fires only when the `Min` / `Max` named arguments are literal floats AND the `value` is a literal-bearing `Binding.Static`. A future tightening could detect the literal-bearing `Binding.State key defaultValue` shape too (the `defaultValue` is statically known the same way the `Static` payload is).
- The validator does not currently flag `Min > Max` (a clearly authoring-defect declaration). Add as a sibling `FUARAN052` if the pattern surfaces in real corpora.

## See also

- Phase 53 – `AIField` bounded-scalar value-space projection (orchestration-tier equivalent on the wire side).
- [Phase 62 – `Binding<'T>.Local`](62-binding-local.md) (the controlled-component substrate this phase pairs with for formatted numeric inputs).
