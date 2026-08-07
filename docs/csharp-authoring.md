# Authoring Fuaran UI trees in C#

`Fuaran.UI.CSharp` is an idiomatic C# authoring surface over the Fuaran UI language.
You compose a UI tree with static factory methods + options records, encode it to the
canonical wire JSON, and hand that JSON to any Fuaran renderer. No F# knowledge is
required, and no FSharp.Core type ever appears on the surface you touch.

This guide takes you from install to a non-trivial authored tree. It mirrors the
language tier's authoring spine – getting started, layout, display, forms, bindings,
actions, charts – in C# idiom.

---

## 1. Install

```xml
<ItemGroup>
  <PackageReference Include="Fuaran.UI.CSharp" />
  <!-- Optional: compile-time diagnostics for your authored trees. -->
  <PackageReference Include="Fuaran.UI.Analyzers" PrivateAssets="all" />
</ItemGroup>
```

Both packages are `net10.0` / analyzer-compatible and restore from the standard
NuGet sources your consumer app already uses.

## 2. Getting started

```csharp
using Fuaran.UI.CSharp;
using static Fuaran.UI.CSharp.Fuaran;   // the factory entry points

var tree = Card(new()
{
    Id = "insights",
    Heading = "Insights",
    Children =
    [
        Metric(new()
        {
            Id = "revenue",
            Label = "Revenue",
            Value = 1234.5,
            Format = CellFormat.Currency("GBP"),
            Tone = Tone.Brand,
        }),
    ],
});

string wireJson = tree.Encode();
```

### Reaching the factories

The factory class is `Fuaran.UI.CSharp.Fuaran`. The runtime also ships a `Fuaran`
**namespace**, so a bare `Fuaran.Metric(…)` under `using Fuaran.UI.CSharp;` binds the
namespace, not the type. Two idioms work:

- **Recommended:** `using static Fuaran.UI.CSharp.Fuaran;` – then write `Metric(new() { … })`.
- **Fully-qualified:** `Fuaran.UI.CSharp.Fuaran.Metric(new() { … })`.

The facade types (`Binding`, `CellFormat`, `Tone`, `Text`, …) have no such clash – 
plain `using Fuaran.UI.CSharp;` reaches them.

### The shape of every factory

Each factory takes one **options record**. `required` members are the mandatory fields,
so a missing `Id` or `Label` is a **compile error** ("you forgot Id"). Optional members
default, so you write only what differs. Children use C# 12 **collection expressions**
(`Children = [a, b]`).

```csharp
// A compile error — MetricOptions.Label and .Value are `required`:
// Metric(new() { Id = "m" });
```

## 3. Text, values, and implicit conversions

- **Text slots** (labels, headings) take a `Text`. A `string` converts implicitly to a
  literal; a `Binding<string>` converts to a bound value:

  ```csharp
  Heading(new() { Id = "h", Text = "Channel performance" });          // literal
  Heading(new() { Id = "h", Text = Binding.Query<string>("caption") }); // bound
  ```

- **Value slots** take a `Binding<T>`. A bare value converts implicitly to a static
  binding, so no helper is needed:

  ```csharp
  Metric(new() { Id = "m", Label = "Revenue", Value = 1234.5 });   // Value : Binding<double>
  Link(new()   { Id = "l", Href = "/about", Label = "About us" }); // Href  : Binding<string>
  ```

## 4. Layout

```csharp
Dashboard(new() { Id = "root", Children = [ /* … */ ] });
Stack(new()     { Id = "col", Orientation = Orientation.Vertical, Children = [ /* … */ ] });
Grid(new()      { Id = "g", Cols = 3, Children = [ /* … */ ] });
Card(new()      { Id = "c", Heading = "Summary", Children = [ /* … */ ] });
SplitPanel(new(){ Id = "sp", Weight = 0.5, Children = [ /* … */ ] });
```

**Overlays and containers** – the active tab / open state ride the wire via bound
values; the click handlers are runtime concerns (see §8):

```csharp
Tabs(new()      { Id = "t", ActiveIndex = Binding.State("tab", 0), Children = [ /* … */ ] });
Modal(new()     { Id = "m", Open = Binding.State("dialogOpen", false), Heading = "Confirm", Children = [ /* … */ ] });
ScrollArea(new(){ Id = "sc", Orientation = ScrollOrientation.Vertical, MaxHeight = 320, Children = [ /* … */ ] });
Disclosure(new(){ Id = "d", Heading = "Advanced", Children = [ /* … */ ] });
```

## 5. Display

```csharp
Heading(new()  { Id = "h", Text = "Q4 report", Level = 2 });
Markdown(new() { Id = "md", Text = "Updated **hourly**." });
Badge(new()    { Id = "b", Label = "Beta", Variant = BadgeVariant.Info });
List(new()     { Id = "l", Items = [(Text)"First", (Text)"Second"], Ordered = true });
Divider(new()  { Id = "hr", Label = "OR" });
Image(new()    { Id = "img", Src = "/avatar.png", Alt = "User avatar", Variant = ImageVariant.Avatar });
CodeBlock(new(){ Id = "code", Language = "csharp", Code = "var x = 1;", LineNumbers = true });
Math(new()     { Id = "eq", Source = "x^2 + y^2 = z^2", Display = MathDisplay.Block });
```

## 6. Forms

Fields are built with the `FormField` factories; the field's value binding rides the
wire, the change handlers are runtime concerns:

```csharp
Form(new()
{
    Id = "signup",
    SubmitLabel = "Create account",
    Fields =
    [
        FormField.Text("email", "Email", required: true),
        FormField.Number("seats", "Seats", initial: 1),
        FormField.Checkbox("terms", "I accept the terms", required: true),
        FormField.Choice("plan", "Plan", selected: "pro",
            options: [("free", "Free"), ("pro", "Pro")]),
    ],
});

Select(new()
{
    Id = "region",
    Label = "Region",
    Options = [("eu", "Europe"), ("us", "Americas")],
    Value = "eu",
});
```

## 7. Bindings

A binding is how a value slot reads live data. Reach for the `Binding` factory for the
data-bound cases; a bare value is a static binding.

```csharp
Binding.Static(1234.5);              // a literal (also the implicit conversion)
Binding.Query<double>("totalRevenue"); // a named query the host resolves
Binding.State("tabIndex", 0);        // module state with a default
Binding.Filter<string>("search");    // a filter-store value
```

**Locale-aware formatting** – `Binding.Format` projects a numeric source to a localised
string binding, which (being a `Binding<string>`) drops into any text slot via the
implicit `Text` conversion:

```csharp
// A bound, formatted string — GBP currency in the ambient locale:
var revenue = Binding.Format(Binding.Query<double>("revenue"), LocaleFormat.Currency("GBP"));
// Percent with one fraction digit, pinned to a specific locale:
var share = Binding.Format(Binding.Query<double>("share"), LocaleFormat.Percent(1), Locale.Explicit("en-GB"));

Metric(new() { Id = "gbp", Label = "Revenue", Value = 1234.5, Subtext = revenue });
```

## 8. Actions and the `Node<obj>` posture

A C# author **never names a message type**. The wire format carries no `'Msg` (a Fuaran
tree is data, not a program), so interactive handlers – a button's click, a form's
submit, a select's change – are **opaque to the wire** and encode as a `"<closure>"`
sentinel. The bound state that *drives* the UI (a tab index, an open flag, a field
value) is what rides the wire and round-trips.

This is why the C# factories fix `Node<object>` and supply placeholder handlers by
construction: it is correct, not a gap. Your host runtime re-attaches real behaviour
downstream; the authored tree stays a pure, portable description of *what* to render.

```csharp
Button(new() { Id = "save", Label = "Save", Variant = ButtonVariant.Primary });
```

## 9. Charts and data grids

```csharp
Chart(new()
{
    Id = "sales",
    Kind = ChartKind.Bar,
    Source = Binding.Query<IEnumerable<object>>("salesByMonth"),
    XField = "month",
    YFields = ["revenue"],
    Title = "Monthly revenue",
});
```

The **data grid** is generic over your row type; typed columns read a value from each row:

```csharp
record SaleRow(string Product, double Amount);

DataGrid(new DataGridOptions<SaleRow>
{
    Id = "sales-grid",
    Source = Binding.Query<IEnumerable<SaleRow>>("sales"),
    RowKey = row => row.Product,
    Columns =
    [
        Column<SaleRow>.Text("Product", row => row.Product),
        Column<SaleRow>.Numeric("Amount", row => row.Amount),
    ],
});
```

## 10. Accessibility – inherited by construction

The factories couple to the language tier's smart constructors, so a node inherits the
same per-component ARIA the F# host emits – a metric announces politely, a card is a
region, a button carries the button role – with **no C# ARIA table to maintain**. You
can inspect what a node carries:

```csharp
NodeAccessibility? aria = Accessibility.Describe(myCard);
// aria.Role == "region"
```

The canonical wire-format fixture corpus is authored *bare* (no ARIA), so a tree you
author carries ARIA the bare fixtures omit – that is the intended, documented posture,
not a divergence. Conformance is proven ARIA-agnostically via the decode round-trip.

## 11. Decoding wire JSON

Reading a tree back from the wire is symmetric, and just as FSharp.Core-free:

```csharp
if (Decode.TryNode(wireJson, out var node, out var error))
{
    string reEncoded = node.Encode(); // byte-identical to wireJson
}
else
{
    // error.Kind is a canonical DecodeErrorCode; error.Path is a $-rooted location.
    Console.Error.WriteLine($"{error.Code} at {error.Path}: {error.Message}");
}
```

The canonical error codes are `InvalidJson`, `MissingField`, `WrongType`,
`UnknownDuCase`, `WrongNodeKind`, and `EmptyNodeId`.

## 12. The runtime model – one engine, reached from C#

Authoring is only half the story. The same three packages you already reference
(`Fuaran.UI`, `Fuaran.UI.Ops`, `Fuaran.UI.OpStream.Abstractions`) carry the whole
**runtime** tier – the tree-op apply engine, a wire-shape self-check, and the
op-stream hash chain. Phase 559 surfaces them on the C# veneer so a C#-only host
can drive them without writing F#. The rule is the same as for authoring:
**the veneer delegates to the single F# engine, never re-implements it.** There is
exactly one apply engine, one canonical encoder, one hash algorithm – a
C#-driven session and an F#-driven session produce identical bytes and identical
hashes, by construction.

One honest boundary: **rendering is not on the C# surface.** A wire tree is
rendered by the Fable/React renderer (`Fuaran.UI.Renderer`), the TypeScript
renderer (`@fuaran-ui/renderer`), or the Rust host – not from C#. C# authors,
validates, applies ops, and maintains provenance; a conformant renderer paints
the result.

## 13. Applying tree-ops (`Fuaran.Apply`)

A tree-op edits a tree – insert a child, remove a node, swap a kind, reorder
children. `Fuaran.Apply` runs one op through the F# engine and hands back either
the new tree or a structured error. The input tree is never mutated: on any
error the engine returns the pre-op tree, so "revert" is implicit (§4g).

```csharp
var card = Fuaran.Card(new() { Id = "insights", Heading = "Insights", Children = [metric] });

// Build a structural op (ids / positions / whole nodes — no F# needed) …
var op = Ops.InsertChild("insights", position: 1, child: secondMetric);

var result = Fuaran.Apply(card, op);
if (result.IsOk)
{
    string wire = result.Value.Encode(); // the new tree, canonical
}
else
{
    // result.Error.Code is a canonical ApplyErrorCode; the hint enumerates
    // AvailableFields + a Suggestion when the engine can name a fix.
    Console.Error.WriteLine($"{result.Error.Code}: {result.Error.Message}");
}

// Or the try-pattern:
if (Fuaran.TryApply(card, op, out var newTree, out var error)) { /* … */ }
```

`Ops` builds the **structural** op cases directly (`InsertChild`, `RemoveNode`,
`MoveNode`, `ReorderChildren`, `ReplaceRoot`, `EditNode`, `Batch`) – they carry
only ids, positions, and whole nodes, so they are pure data, not a
re-implementation of apply logic. For **field-level** edits (`UpdateProp`,
`ReplaceBinding`) an AI orchestrator emits a canonical op as wire JSON; decode it
and apply it the same way:

```csharp
var op = Ops.FromJson(wireOpJson); // == Decode.Op(...)
if (op.IsOk) { var next = Fuaran.Apply(tree, op.Value); }
```

`Batch` folds a list of ops all-or-nothing – the first failing inner op aborts
the whole batch and reports its index in `ApplyError.BatchInnerIndex`.

## 14. Validating at runtime (`Fuaran.Validate`)

```csharp
IReadOnlyList<ValidationFinding> findings = Fuaran.Validate(tree);
if (!Fuaran.IsValid(tree)) { /* inspect findings — Code / Path / Message */ }
```

`Validate` is the **runtime pre-emit self-check**: it encodes the tree
canonically, decodes it back through the shared F# codec, and confirms the
re-encode is byte-stable. A clean verdict means the tree is wire-survivable and
means the same thing on every host, because it runs the same encoder + decoder
every host runs.

Be clear about what this is *not*. The build-time `FUARAN*` rules (accessibility,
binding-name resolution, custom-health, …) are a **build-time F#-AST walker**
(`Fuaran.UI.Validator`) – they need F# source and a project, so there is no
runtime equivalent to delegate to and `Validate` does not attempt one. For
compile-time diagnostics on C#-authored trees, use the analyzer package (next
section); `Validate` is the runtime structural gate that complements it.

## 15. Op-streams and hash chains (`OpStreamChain`)

Every applied op can be recorded in a **hash-chained** log – the provenance
substrate for an AI-driven session. `OpStreamChain` builds and verifies one
entirely from C#: each `Append` computes the next record's SHA-256 over the
shared canonical pre-image (previous hash + op + sequence + timestamp + actor +
prompt + outcome), and `Verify` delegates to the shared F# verifier.

**What verification proves.** The chain detects corruption, truncation and
reordering of a stored stream. It is an *unkeyed* digest over data the store
itself holds, so it is not evidence against someone who can write the store:
they recompute the hashes and `Verify` passes. Evidence against a writer needs a
key they do not have – see [`CRYPTO.md`](../CRYPTO.md) and the signing seam it
describes.

```csharp
var chain = new OpStreamChain("insights-stream");
var actor = FuaranActor.Agent(model: "claude", version: "opus-4", id: "agent-1");

var e1 = chain.Append(insertOp, actor, promptId: "prompt-1");
var e2 = chain.Append(removeOp, actor, promptId: "prompt-2");

// e1.PreviousHash is the 64-zero genesis; e2.PreviousHash == e1.Hash.
ChainVerification v = chain.Verify();
if (!v.IsIntact) { Console.Error.WriteLine(v.Message); }
```

The author is a `FuaranActor` – `Human(id)` or `Agent(model, version, id)` – and
it is folded **into** the hash, so re-attributing an op breaks the chain unless
the chain is recomputed with it (attribution is covered by the digest, not
merely stored beside it). Because the pre-image is
shared across hosts, a chain built here verifies on the F# or TS host and
vice-versa; re-appending an identical op/actor/timestamp reproduces the identical
hash.

## 16. Compile-time checks (`Fuaran.UI.Analyzers`)

With the analyzer package referenced, your authored trees get IDE + build diagnostics:

- **FUARAN001** – a duplicate `Id` in the same code block (every NodeId must be unique).
- **FUARAN010** – a `Binding.Query` name that isn't in your `fuaran-validator.manifest.json`.

Wire the manifest so the query check has something to resolve against:

```xml
<ItemGroup>
  <AdditionalFiles Include="fuaran-validator.manifest.json" />
</ItemGroup>
```

```json
{ "queries": ["totalRevenue", "salesByMonth"], "msgCases": [] }
```

## 17. Wire fidelity

`Encode()` calls the language tier's canonical encoder directly – there is **no parallel
C# codec**. A C#-authored tree therefore produces the exact same bytes as its equivalent
in any other Fuaran host, by construction. That is the whole point of the veneer: an
idiomatic C# surface, wire-faithful for free.

---

## See also

- `WIRE_FORMAT.md` – the canonical wire-format specification the encoder targets.
- `Fuaran.UI.CSharp` package README – a quick-start summary.
- `Fuaran.UI.Analyzers` package README – the compile-time rule reference.
