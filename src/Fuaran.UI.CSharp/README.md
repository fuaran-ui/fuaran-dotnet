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

## Actions — reaching the host

A control raises a `FuaranAction`. Three slots carry one: `ButtonOptions.OnClick`,
`FormOptions.OnSubmit` and `ModalOptions.OnDismiss`.

```csharp
Button(new()
{
    Id = "save",
    Label = "Save",
    Variant = ButtonVariant.Primary,
    // Reaches the host on a channel, with a JSON payload. A plain string, number
    // or bool converts implicitly; `Payload.Object` / `Payload.Array` compose.
    OnClick = FuaranAction.Notify("draft.saved", Payload.Object(("id", 42), ("dirty", false))),
});

Button(new()
{
    Id = "refresh",
    Label = "Refresh",
    // Calls the endpoint and writes the response to `$state.total` — every
    // `Binding.State("total")` reader re-renders. `CallIntoQuery` is the sibling
    // that writes a query-results slot instead.
    OnClick = FuaranAction.CallIntoState("/api/total", "total"),
});

// …and several in order:
OnClick = FuaranAction.Chain(
    FuaranAction.Notify("row.selected", "r-17"),
    FuaranAction.CallIntoState("/api/detail", "detail"));
```

**There is deliberately no `Dispatch`.** That case carries a host closure as its
message: the canonical encoder emits the discriminator and drops the payload, and a
decoding host rebuilds it as the `"<closure>"` sentinel — so a serialised `Dispatch`
arrives as an affordance that renders, fires, and does nothing. A veneer whose trees
are serialised must not be able to mint one. `EncodeForTransport` refuses such a
tree; the absence here is the other half of that answer, and it means the refusal
cannot fire on a tree this surface authored.

Typed host behaviour is reached the other way round — the host binds a handler table
to the artifact's declared action holes, uniform across hosts and needing no
per-language mechanism. The action a *tree* carries is `Notify` or a `Call` whose
result lands in a reactive slot.

The remaining handler slots (a select's change, a grid's row click, a form field's
edit) take a closure in the language tier too, and encode as `"<closure>"` on the
wire. They are not authorable from any tier as data, and the veneer does not pretend
otherwise: the bound state that *drives* the UI is what rides the wire.

See `docs/csharp-authoring.md` for the full authoring guide.
