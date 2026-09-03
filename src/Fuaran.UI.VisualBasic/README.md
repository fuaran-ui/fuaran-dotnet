# Fuaran.UI.VisualBasic

Author Fuaran UI trees in **VB via first-class XML literals** — the notation the
design brief calls the strongest argument for VB tier-1 parity (it is closer to the
VBA mental model than fluent builders).

```vb
Imports Fuaran.UI.VisualBasic

Dim tree = <Dashboard id="root">
               <Heading id="title" text="Channel performance"/>
               <Markdown id="note" text="**Hello** from a Fuaran tree."/>
           </Dashboard>

Dim wireJson As String = FuaranXml.Encode(tree)
```

This is the first tree of the **[VB get-started track](https://fuaran-ui.io/get-started/vb)** —
the same example, rendered live from its canonical wire JSON on the site.

`FuaranXml.Translate` walks the runtime `XElement` and drives the shared C# factory
surface, so a VB-authored tree **inherits per-component ARIA and encodes through the
same F# codec every host uses** — there is no VB codec, and no FSharp.Core type appears
on the surface you touch.

## Conventions

- An attribute is a literal; a `"$name"` value is a bound query (`value="$revenue"`).
- A `"$state.name"` value is a **writable state slot** (`open="$state.panelOpen"`). See
  "The two binding prefixes" below — the difference is direction, and it decides
  whether a control is live.
- `format-currency` / `format-number` / `format-percent` / `format-date` map to the
  bounded cell-format vocabulary.
- Nested elements become the parent's children.
- `dispatch="…"` is author-side metadata — the wire carries no message type (§4g).

## The two binding prefixes

Both start with `$`, and they differ in **direction**:

| Spelling | Binding | Direction |
|---|---|---|
| `value="$revenue"` | `Binding.Query` | the host feeds it; the tree reads it |
| `open="$state.panelOpen"` | `Binding.State` | reactive state the tree reads **and writes** |

Only the second makes a control *live on its own*. When a control's value binding is a
state slot and you author no handler, the renderer writes the changed value straight
back to that slot and every reader of the key re-renders — no host code, and it survives
serialisation because there is no closure in it to lose:

```vb
<Disclosure id="details" heading="Advanced" open="$state.advancedOpen">
    <Markdown id="body" text="Toggling this writes `advancedOpen` and re-renders every reader."/>
</Disclosure>

<DataGrid id="plan" source="$state.planRows" editable="true" edit-state-key="planRows">
    <Column type="text" field="month" label="Month"/>
    <Column type="numeric" field="revenue" label="Revenue"/>
</DataGrid>
```

A query-bound control with no handler is *inert* — the write-back has nowhere to write —
which is what the `FUARAN069` pre-emit check reports.

Three details worth knowing:

- **`$state.name` binds the slot; it declares no initial value.** Where a control has an
  initial state it has its own attribute for it (`default-open` on a `Disclosure`), which
  keeps one fact in one place. A state binding that *carries* a default is a different wire
  document, and it is authorable from the C# factory surface (`Binding.State(key, value)`)
  rather than from a second XML spelling.
- **`$state` and `$state.` are errors**, not queries named `state` — the prefix must carry a
  key. The analyzer reports it as `FUARAN117` at compile time.
- **`$stateful` is still a query named `stateful`.** The discriminator is the `$state.`
  prefix, not the letters.

Pair with **`Fuaran.UI.Analyzers`** for compile-time checking of element/attribute
names (an unmapped element is otherwise a runtime error). See `docs/vbnet-authoring.md`.

Prefer fluent builders? The C# factory surface (`Fuaran.UI.CSharp`) is plain .NET and
callable from VB directly, byte-identical on the wire — see the authoring guide's
"Prefer fluent?" section.
