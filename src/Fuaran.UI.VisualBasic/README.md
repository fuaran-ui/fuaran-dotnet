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
- `format-currency` / `format-number` / `format-percent` / `format-date` map to the
  bounded cell-format vocabulary.
- Nested elements become the parent's children.
- `dispatch="…"` is author-side metadata — the wire carries no message type (§4g).

Pair with **`Fuaran.UI.Analyzers`** for compile-time checking of element/attribute
names (an unmapped element is otherwise a runtime error). See `docs/vbnet-authoring.md`.

Prefer fluent builders? The C# factory surface (`Fuaran.UI.CSharp`) is plain .NET and
callable from VB directly, byte-identical on the wire — see the authoring guide's
"Prefer fluent?" section.
