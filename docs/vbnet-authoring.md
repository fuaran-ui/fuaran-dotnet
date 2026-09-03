# Authoring Fuaran UI trees in VB

`Fuaran.UI.VisualBasic` lets you author Fuaran UI trees in VB using **first-class
XML literals** – the notation closest to the mental model a VBA / VB developer
already has. You write the tree as XML, translate it to a wire-faithful node, and hand
the canonical JSON to any Fuaran renderer. No F# knowledge is required, and no
FSharp.Core type ever appears on the surface you touch.

This guide takes you from install to a non-trivial authored tree.

---

## 1. Install

```xml
<ItemGroup>
  <PackageReference Include="Fuaran.UI.VisualBasic" />
  <!-- Optional: compile-time checking of element/attribute names. -->
  <PackageReference Include="Fuaran.UI.Analyzers" PrivateAssets="all" />
</ItemGroup>
```

## 2. Getting started

```vb
Imports Fuaran.UI.VisualBasic

Dim tree = <Card id="insights" heading="Insights">
               <Metric id="revenue" label="Revenue" value="1234.5"
                       format-currency="GBP" tone="Brand"/>
           </Card>

Dim wireJson As String = FuaranXml.Encode(tree)
```

`FuaranXml.Translate` walks the runtime `XElement` your XML literal produces and builds
the tree; `FuaranXml.Encode` translates and encodes in one step. Every element name is a
**node kind**, every attribute maps to a **spec field**, and nested elements become the
parent's **children**.

## 3. Conventions

- **Literals and bindings.** An attribute value is a literal; a value prefixed `$` is a
  bound query the host resolves; a value prefixed `$state.` is a writable state slot:

  ```vb
  <Metric id="rev" label="Revenue" value="1234.5"/>    ' static value
  <Metric id="rev" label="Revenue" value="$revenue"/>  ' host-fed query — read-only
  <Disclosure id="d" heading="Advanced" open="$state.advancedOpen"> … </Disclosure>
                                                       ' reactive state — read AND written
  ```

  The two prefixes differ in **direction**, and that is what decides whether a control
  works on its own. A query binding is host-fed: the tree reads it and nothing writes it
  back. A state binding names a key in the reactive store, so when you bind a control's
  value to one and author no handler, the renderer writes the changed value straight to
  that key and every reader of it re-renders — no host code, and nothing to lose in
  serialisation:

  ```vb
  <DataGrid id="plan" source="$state.planRows" editable="true" edit-state-key="planRows">
      <Column type="text" field="month" label="Month"/>
      <Column type="numeric" field="revenue" label="Revenue"/>
  </DataGrid>
  ```

  An editable grid over a `$query` source is display-only for exactly this reason: its
  edits have nowhere to commit. The `FUARAN069` pre-emit check reports the same thing for
  a handler-free control whose value is not writable.

  Three details:

  - `$state.name` binds the slot and declares **no initial value**. Where a control has an
    initial state it carries its own attribute for it (`default-open`). A state binding
    that also carries a default is a different wire document; author that one through the
    C# factory surface (`Binding.State(key, value)`), which is callable from VB directly.
  - `$state` and `$state.` are authoring **errors**, not queries named `state`. The
    analyzer reports `FUARAN117` at compile time; the translator raises
    `FuaranXmlException` at run time.
  - `$stateful` is still a query named `stateful` — the discriminator is the `$state.`
    prefix, not the letters.

- **Formats.** `format-currency` / `format-number` / `format-percent` / `format-date`
  set the display format:

  ```vb
  <Metric id="rev" label="Revenue" value="$revenue" format-currency="GBP"/>
  ```

- **Enums** are written by name (case-insensitive): `tone="Brand"`, `variant="Info"`,
  `orientation="Horizontal"`.

- **Lists of things** are child elements: `<Item>` for a list, `<Option>` for a select,
  `<Field>` for a form, `<Column>` for a data grid, `<Marker>` for a map.

## 4. Layout

```vb
<Dashboard id="root"> … </Dashboard>
<Stack id="col" orientation="Vertical"> … </Stack>
<Grid id="g" cols="3"> … </Grid>
<Card id="c" heading="Summary"> … </Card>
<Tabs id="t" active-index="$tab"> … </Tabs>
<Modal id="m" open="$dialogOpen" heading="Confirm"> … </Modal>
<ScrollArea id="sc" orientation="Vertical" max-height="320"> … </ScrollArea>
```

## 5. Display

```vb
<Heading id="h" text="Q4 report" level="2"/>
<Markdown id="md" text="Updated **hourly**."/>
<Badge id="b" label="Beta" variant="Info"/>
<List id="l" ordered="true"><Item>First</Item><Item>Second</Item></List>
<Divider id="hr" label="OR"/>
<Image id="img" src="/avatar.png" alt="User avatar" variant="Avatar"/>
<CodeBlock id="code" language="vbnet" line-numbers="true">Dim x = 1</CodeBlock>
<Math id="eq" display="Block">x^2 + y^2 = z^2</Math>
```

## 6. Forms

```vb
<Form id="signup" submit-label="Create account">
    <Field kind="text"     id="email" label="Email" required="true"/>
    <Field kind="number"   id="seats" label="Seats" initial="1"/>
    <Field kind="checkbox" id="terms" label="I accept the terms" required="true"/>
    <Field kind="choice"   id="plan"  label="Plan" selected="pro">
        <Option value="free" label="Free"/>
        <Option value="pro"  label="Pro"/>
    </Field>
</Form>

<Select id="region" label="Region" value="eu">
    <Option value="eu" label="Europe"/>
    <Option value="us" label="Americas"/>
</Select>
```

## 7. Data grids

The grid's columns are `<Column>` children; `field` names the row property (the host
reads it at runtime – it is not part of the wire), and `type` picks the cell shape:

```vb
<DataGrid id="sales-grid" source="$sales">
    <Column type="text"    label="Product" field="product"/>
    <Column type="numeric" label="Amount"  field="amount"/>
</DataGrid>
```

For an **editable** grid, bind the source to state rather than to a query, and name where
the edits commit — a grid over a read-only query source is display-only, because its edits
have nowhere to go:

```vb
<DataGrid id="plan-grid" source="$state.planRows" editable="true" edit-state-key="planRows">
    <Column type="text"    label="Month"   field="month"/>
    <Column type="numeric" label="Revenue" field="revenue"/>
</DataGrid>
```

## 8. The `Node(Of Object)` posture

A VB author **never names a message type**. The wire format carries no message payload
(a Fuaran tree is data, not a program), so interactive handlers – a button's click, a
form's submit – are opaque to the wire. A `dispatch="…"` attribute (where a future tier
supports it) is **author-side metadata**; the bound state that *drives* the UI (a tab
index, an open flag, a field value) is what rides the wire and round-trips. The
compile-time typed source-generator tier is deferred; the runtime translator is the
supported surface.

## 9. Accessibility – inherited by construction

The translator routes each element through the language tier's smart constructors, so a
node inherits the same per-component ARIA the F# host emits – a metric announces politely,
a card is a region – with **no VB ARIA table to maintain**. Inspect it with:

```vb
Dim aria = Accessibility.AriaJson(<Card id="c"/>)   ' {"role":"region"}
```

The canonical fixture corpus is authored *bare* (no ARIA), so a tree you author carries
ARIA the bare fixtures omit – the intended, documented posture (see the C# authoring
guide's accessibility section for the shared cross-host decision), not a divergence.

## 10. Compile-time safety

The XML-literal surface is translated **at runtime**, so a typo'd element or attribute is
otherwise a runtime error. Reference **`Fuaran.UI.Analyzers`** to get **compile-time**
diagnostics – Roslyn parses VB XML literals into syntax trees, so the analyzer flags an
unknown element (`<Metrik>` → "did you mean `<Metric>`?"), a duplicate `id`, a
`source="$…"` that doesn't resolve against your manifest, and a `"$state"` value carrying
no key (`FUARAN117`) — as IDE squiggles + build errors, before you run. A well-formed
`"$state.<key>"` is deliberately *not* manifest-checked: a state key names a slot in the
reactive store, which is not a host query and is not listed there.

## 11. Prefer fluent? The same factories, without XML

The XML-literal surface is a veneer: `FuaranXml.Translate` drives the C# factory
surface (`Fuaran.UI.CSharp`) for you. That surface is plain .NET – no C#-only
construct appears on any public signature – so a VB author can also drive it
**directly**, in the same fluent options-record style the C# guide documents. VB's
`With { }` object initializers set the C# init-only records, the facade's implicit
conversions widen a bare `"Revenue"` / `1234.5` to `Text` / `Binding(Of Double)`
exactly as they do in C#, and C#'s `required`-member enforcement carries over: omit
a required field (an `Id`, a metric's `Label`) and VB fails the build with BC37321,
the same compile-time guarantee C# callers get.

```vb
Imports Csharp = Fuaran.UI.CSharp

Dim tree = Csharp.Fuaran.Card(New Csharp.CardOptions With {
    .Id = "insights",
    .Heading = "Insights",
    .Children = {
        Csharp.Fuaran.Metric(New Csharp.MetricOptions With {
            .Id = "revenue",
            .Label = "Revenue",
            .Value = 1234.5,
            .Format = Csharp.CellFormat.Currency("GBP"),
            .Tone = Csharp.Tone.Brand})}})

Dim wireJson As String = tree.Encode()
```

VB's `Imports` can also name the factory *type* itself – the moral equivalent of
C#'s `using static` – so the factory calls bind unqualified:

```vb
Imports Csharp = Fuaran.UI.CSharp   ' the options + facade types
Imports Fuaran.UI.CSharp.Fuaran     ' the factory class — Card(…), Metric(…) unqualified

Dim tree = Card(New Csharp.CardOptions With { .Id = "insights", … })
```

Both spellings produce **byte-identical wire JSON** to the §2 XML literal – all
three notations drive the same factories and encode through the same canonical F#
codec. The conformance suite pins this (`FluentAuthoring.vb` byte-compares the
fluent tree against its XML-literal equivalent), so the parity is a regression
check, not a doc claim.

**When to reach for which.** The XML literals stay the recommended idiom: closest
to the VBA mental model, and lighter per node – fluent VB is wordier than fluent C#
(VB has no target-typed `New()`, so every options record is spelled out). The
fluent surface earns its place when a tree is assembled *programmatically* (loops
and conditionals compose more naturally as expressions than as spliced XML), when
your team already writes C#-style builders, or when you're porting authoring code
from the C# guide line-for-line. Mixing is fine in one direction – `FuaranXml.Translate`
returns the same `FuaranNode` the factories do, so a literal-authored fragment can sit
inside a fluent-built tree's `Children` (the reverse – splicing a factory-built node
into an XML literal – is not supported; the translator walks XML only).

## 12. The runtime tier – apply, validate, op-streams (through the veneer)

Authoring is only half the story: the same runtime tier the C# guide describes – 
the tree-op **apply** engine, a runtime **validate** self-check, and the
**op-stream hash chain** – is available to VB **through the C# veneer**, which VB
already references (`Imports Csharp = Fuaran.UI.CSharp`). This is the established
layering: the VB XML-literal translator drives the C# factories, and the C#
package carries the runtime surface, so VB reaches the single shared F# engine
with no extra dependency and no F# of its own.

`FuaranXml.Translate` returns a `Csharp.FuaranNode` – exactly the handle the C#
runtime API takes:

```vb
Imports Csharp = Fuaran.UI.CSharp

Dim card = FuaranXml.Translate(<Card id="insights">
                                   <Metric id="rev" label="Revenue" value="1234.5" tone="Brand"/>
                               </Card>)

' Validate (runtime wire-shape self-check).
If Not Csharp.Fuaran.IsValid(card) Then ' inspect Csharp.Fuaran.Validate(card)
End If

' Apply a tree-op through the shared engine.
Dim m2 = FuaranXml.Translate(<Metric id="cost" label="Cost" value="500" tone="Default"/>)
Dim result = Csharp.Fuaran.Apply(card, Csharp.Ops.InsertChild("insights", 1, m2))
If result.IsOk Then Dim wire = result.Value.Encode()

' Record ops in a hash chain (corruption detection - see CRYPTO.md).
Dim chain = New Csharp.OpStreamChain("insights-stream")
Dim actor = Csharp.FuaranActor.Agent("claude", "opus-4", "agent-1")
chain.Append(Csharp.Ops.RemoveNode("cost"), actor)
If Not chain.Verify().IsIntact Then ' provenance broken
End If
```

The semantics – one engine, no re-implementation, structured `ApplyErrorCode`
errors, hash pre-image shared across hosts – are identical to the C# surface;
see the C# authoring guide §§12–15 for the full model. As there, **rendering is
not on this surface**: a conformant renderer (Fable/React, TypeScript, or the
Rust host) paints the wire tree; VB authors, validates, applies, and notarises it.

## 13. Wire fidelity

`FuaranXml.Encode` calls the language tier's canonical encoder directly – there is **no VB
codec**. A VB-authored tree therefore produces the exact same bytes as its equivalent in
any other Fuaran host, by construction.

---

## See also

- `WIRE_FORMAT.md` – the canonical wire-format specification.
- `csharp-authoring.md` – the C# fluent surface (the same wire contract, different notation).
- `Fuaran.UI.VisualBasic` package README – a quick-start summary.
