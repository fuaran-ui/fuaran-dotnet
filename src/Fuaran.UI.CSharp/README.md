# Fuaran.UI.CSharp

An idiomatic **C# authoring veneer** over the Fuaran UI language. Author Fuaran UI
trees in modern C# — static factory + options records, `required` members that make
a missing field a compile error, C# 12 collection expressions for children, and
implicit conversions so `Label = "Revenue"` and `Value = 1234.5` bind with no helper
call. No FSharp.Core type appears on any public signature.

```csharp
using Fuaran.UI.CSharp;
using static Fuaran.UI.CSharp.Fuaran;   // the factory entry points as Dashboard(...), Heading(...), …

var tree = Dashboard(new()
{
    Id = "root",
    Children =
    [
        Heading(new() { Id = "title", Text = "Channel performance" }),
        Markdown(new() { Id = "note", Text = "**Hello** from a Fuaran tree." }),
    ],
});

string wireJson = tree.Encode();
```

This is the first tree of the **[C# get-started track](https://fuaran-ui.io/get-started/csharp)** —
the same example, rendered live from its canonical wire JSON on the site.

> **Reaching the factories.** The factory class is `Fuaran.UI.CSharp.Fuaran`. Because
> the runtime also ships a `Fuaran` **namespace** (the F# tier), a bare
> `Fuaran.Metric(…)` under `using Fuaran.UI.CSharp;` binds the namespace, not the
> type. Use **`using static Fuaran.UI.CSharp.Fuaran;`** (recommended — you write
> `Metric(new() { … })`) or the fully-qualified `Fuaran.UI.CSharp.Fuaran.Metric(…)`.
> The facade types (`Binding`, `CellFormat`, `Tone`, …) have no such clash — plain
> `using Fuaran.UI.CSharp;` reaches them.

The veneer is **wire-faithful by construction**: `Encode` calls the same canonical
F# encoder the F# host uses, so a C#-authored tree produces the same bytes as its
F# equivalent — there is no parallel C# codec. Decoding is covered by the C#-native
`Decode` surface.

See `docs/csharp-authoring.md` for the full authoring guide.
