# Phase 62 migration – `Binding<'T>.Local` controlled-component text-state binding

**Shipped:** 2026-05-28
**Scope:** `Fuaran.UI` typed contract + `Fuaran.UI.Renderer` runtime + `Fuaran.UI.Ops.JsonDecode` decoder + `Fuaran.UI.Validator` AST walker + `Fuaran.UI.OpStream.Abstractions.CanonicalJson` encoder.
**Stability impact:** Additive across every surface. No reordering, renaming, or signature changes to existing DU cases, smart-ctor entry points, decoder branches, or validator codes. Pre-Phase-62 consumers see no behavioural change.

## What changes

### 1. `Binding<'T>.Local` + `LocalBinding<'T>` + `LocalFlushTrigger`

`Fuaran.UI/Types.fs` adds (additive DU case, additive record, additive DU):

```fsharp
type Binding<'T> =
    | ...
    | Local of LocalBinding<'T>                  // NEW

and LocalBinding<'T> =
    { InitialFrom: Binding<'T>
      FlushOn: LocalFlushTrigger
      OnCommit: 'T -> obj                        // boxed Action<'Msg>
      Format: ('T -> string) option
      Parse: string -> Result<'T, string> }

and LocalFlushTrigger =
    | OnBlur
    | OnSubmit
    | OnDebounce of milliseconds: int
    | OnCommitAction
```

The renderer maintains a per-`NodeId` `React.useState` slot for any `FormFieldKind.Text` / `FormFieldKind.Number` whose `Value` is a `Binding.Local`. Keystrokes update the buffer; the typed `OnCommit` dispatches on the configured `FlushOn` boundary – never per-keystroke.

### 2. `Action.CommitLocal` (additive DU case)

```fsharp
type Action<'Msg> =
    | ...
    | CommitLocal of nodeId: string              // NEW
```

The renderer's `runAction` handles `CommitLocal nodeId` natively (dispatches a DOM `CustomEvent` keyed `fuaran-commit-local-<nodeId>`) – no `IFuaranRuntime` substrate required.

### 3. `binding.local` smart-ctor

```fsharp
let local
    (initialFrom: Binding<'T>)
    (flushOn: LocalFlushTrigger)
    (onCommit: 'T -> Action<'Msg>)
    (format: ('T -> string) option)
    (parse: string -> Result<'T, string>)
    : Binding<'T>
```

`OnCommit` is obj-erased at the tree level the same way `Action.Call`'s `onResult: obj -> 'Msg` is – the smart-ctor boxes the typed `Action<'Msg>` and the renderer unboxes at dispatch.

### 4. JsonDecode forward-coupling

`Fuaran.UI.Ops.JsonDecode` grows:
- `"$type": "Local"` branch in `bindingGeneric` (recurses InitialFrom through the same machinery, decodes the FlushOn DU, places closure sentinels for OnCommit / Format / Parse).
- `decodeLocalFlushTrigger` for the 4-case `LocalFlushTrigger` DU (one case carries an integer ms payload).
- `"$type": "CommitLocal"` branch in `decodeAction`.

The canonical encoder (`Fuaran.UI.OpStream.Abstractions.CanonicalJson`) gains the matching `Local` / `CommitLocal` arms – the closure-bearing fields collapse to `<closure>` sentinels per the existing convention.

### 5. Validator: `FUARAN042` / `FUARAN043` / `FUARAN044`

`Fuaran.UI.Validator/LocalBindingCheck.fs` adds three defects:

| Code | Severity | When |
|---|---|---|
| `FUARAN042` | Error   | `binding.local` called with `format = None` literal. |
| `FUARAN043` | Warning | `binding.local` with `flushOn = OnCommitAction` declared and no `Action.CommitLocal _` reference appears anywhere in the project. |
| `FUARAN044` | Error   | `binding.local` used outside an enclosing `FormFieldKind.Text(...)` or `FormFieldKind.Number(...)` constructor. |

Walker is its own narrow pass (separate from the Fuaran.X smart-ctor walker) – keeps the rule's lexical pattern isolated.

## When to choose `State` vs `Local`

Rule of thumb:

- **`binding.state`** – every keystroke is meaningful to the model. Live search, inline filtering, slider-coupled numeric input. The AI-emit baseline.
- **`binding.local`** – only the final committed value matters; intermediate states would otherwise stream un-parseable text into the model. Salary inputs, formatted numeric fields, free-text drafts that need explicit commit.

A useful diagnostic: if you find yourself reaching for a `[<ReactComponent>]` + `useState` + `useEffect` wrapper around a Fuaran form-field, that's the signal to use `Local` instead.

## Canonical four shapes

### Salary-style – `OnBlur` + thousands formatter

```fsharp
FormFieldKind.Number(
    binding.local
        (binding.state "salary" 50000m)
        LocalFlushTrigger.OnBlur
        (fun v -> Action.dispatch (SetSalary v))
        (Some formatThousands)
        parseDecimalLenient,
    (fun _ -> Action.Chain []))
```

Partial-decimal typing (`"5."` trailing dot) survives mid-edit; the model only sees parsed values after the user moves focus away.

### Live-validating – `OnDebounce 250`

```fsharp
FormFieldKind.Text(
    binding.local
        (binding.state "email" "")
        (LocalFlushTrigger.OnDebounce 250)
        (fun v -> Action.dispatch (SetEmail v))
        (Some id)
        parseEmail,
    (fun _ -> Action.Chain []))
```

Parse errors keep the buffer-text visible; the field's `Help` slot surfaces a typed error message.

### Explicit commit – `OnCommitAction` + Apply button

```fsharp
let applyButton =
    Fuaran.button "apply"
        { Defaults.button<Msg> with
            Label = TextSource.Literal "Apply"
            OnClick = Action.CommitLocal "note-input" }
```

`Action.CommitLocal "note-input"` fires the DOM `fuaran-commit-local-note-input` event the input's `useEffect` listens for. Buffer flushes through `OnCommit`.

### Re-sync semantics

External `InitialFrom`-side changes (preset apply, reset-to-defaults, URL hydration) re-sync the buffer when `Parse(buffer) ≠ newExternalValue`. Mid-edit typing position survives any re-render that doesn't change the underlying typed value – this is the cursor-preservation invariant.

## Wire-shape registry pattern (deferred)

The phase doc's wire shape names `format` / `parse` as registry keys (`"thousands"`, `"decimalLenient"`):

```json
{
  "binding": {
    "$type": "Local",
    "initialFrom": { "$type": "State", "key": "salary", "defaultValue": 50000 },
    "flushOn": { "$type": "OnBlur" },
    "format": "thousands",
    "parse": "decimalLenient"
  }
}
```

The Phase 62 decoder accepts the `Local` wire shape and rebuilds the structural Binding payload, but leaves `Format = None` and `Parse = <closure-sentinel-error>` per the storage-shape erasure rule. A host-provided registry – looked up by name at orchestrator-side decode time – is the v2 surface. Consumers re-attach typed formatter / parser closures downstream via their `moduleMsgDecoder` per Phase 12.E.0 forward coupling.

## Anti-patterns

- **Don't make `Local` the default for every Text input.** Live-search needs per-keystroke dispatch; `binding.state` stays canonical there.
- **Don't bypass `Parse` errors silently.** A parse failure during typing keeps the buffer visible but does NOT dispatch; the form's Submit / blur trigger also checks parse-result and surfaces the field's `Help` slot.
- **Don't ship without the cursor-preservation invariant.** The renderer's `useEffect` dependency array is exactly `[| externalValue |]` and the body checks `Parse(buffer) ≠ externalValue` before re-seeding. The catalog's `local-bindings.spec.mts` exercises this.
- **Don't conflate `Local` with `Computed`.** `Computed` is a read-side derivation from `BindingContext`. `Local` is a write-side staging buffer. The responsibilities are opposite.

## Catalog page + Playwright spec

- `samples/catalog/LocalBindings.fs` – mounts at `?local-bindings=1`. Salary / Email / Note + a model-side mirror panel for inspecting commits.
- `samples/catalog/snapshot/local-bindings.spec.mts` – exercises the four flush shapes plus the re-sync invariant.

## Open follow-ups

- **Format / Parse registry.** Wire-shape consumers currently lose typed Format / Parse on decode. A host-provided registry keyed on the canonical six (`identity`, `thousands`, `currency`, `percent`, `decimalLenient`, `intLenient`) is the v2 surface.
- **Per-form OnSubmit scoping.** The renderer's `dispatchFormCommit` fires a window-wide `fuaran-form-commit` event. Multiple forms on the same page hear each other's submits. Scoping by form NodeId is a future refinement.
- **AI-authoring eval prompts.** The §4a Principle 11 release gate adds Local-targeted prompts to the canonical eval suite when Phase 12.E ships (orchestrator-side).
