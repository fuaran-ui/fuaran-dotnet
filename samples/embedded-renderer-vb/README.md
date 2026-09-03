# Embedded renderer — VB sample

A minimal ASP.NET app that authors a Fuaran tree **with VB XML literals**, serves its canonical
wire JSON, and hydrates it in the browser through the renderer embedded in
`Fuaran.UI.Renderer.Web`.

```
dotnet run --project samples/embedded-renderer-vb --urls http://localhost:5198
```

No port is hard-coded, so the sample needs no allocation from the workspace port band.

## What it demonstrates

**No Node toolchain.** No `package.json`, no bundler, no `wwwroot`. The browser renderer arrives
inside the package and is served by one call.

**XML literals as the authoring notation.** The tree is an `XElement`:

```vb
<Dashboard id="root">
    <Heading id="title" text="Embedded renderer - VB"/>
    <Disclosure id="details" heading="How this page was built" open="$state.detailsOpen" default-open="false">
        <Markdown id="details-body" text="..."/>
    </Disclosure>
</Dashboard>
```

`FuaranXml.Translate` walks it and drives the same shared factory surface every other authoring
tier uses, so the bytes are identical to the C# and F# equivalents. There is no VB codec.

**Encode for transport.** `TryEncodeForTransport` refuses a tree carrying interaction that
serialisation would lose, naming every offending node and slot.

**A live interaction, bound to state.** The disclosure's `open` slot is bound to `$state.detailsOpen`
and carries no handler, so toggling it writes that key in the reactive store and re-renders every
reader — no host code and no round trip. It survives serialisation because a state binding is data
where a handler would have been a closure.

## Two VB-specific things worth knowing

**Call `MapFuaranRenderer` as a static method.** The extension form
(`app.MapFuaranRenderer()`) is what a C# host writes; VB does not bind it here, because `Fuaran`
is a namespace *and* a type name in this runtime and import resolution goes the other way. The
static form is exact, needs no import, and is the same method:

```vb
Global.Fuaran.UI.Renderer.Web.FuaranRendererEndpointExtensions.MapFuaranRenderer(app)
```

The same clash is why the sample writes `Global.Fuaran.UI.Renderer.Theme.vocabularyFingerprint`.

**The dialect has two `$` prefixes, and they differ in direction.** `open="$panel"` is a QUERY
binding — host-fed and read-only, so a toggle has nowhere to write it back. `open="$state.detailsOpen"`
is a writable state slot, which is what this sample binds and what makes the disclosure live with no
host code. Bind an interactive control's value to `$state.<key>` and omit the handler; bind it to
`$name` when the host owns the value and the tree only reads it.

## What it does *not* demonstrate

**`Action.Dispatch`**, for the reason the C# sample's README gives: it carries a host closure that
the wire drops. See [`docs/EMBEDDED-RENDERER.md`](../../docs/EMBEDDED-RENDERER.md) §2.

**A host round trip.** The C# veneer now carries the `Action` vocabulary and the C# leg of this
pair shows the round trip, but the XML dialect has no attribute spelling for a handler — so from
VB it is authorable through the C# factory surface (plain .NET, callable from VB directly), not
from an XML literal.

## Files

| | |
|---|---|
| `Program.vb` | the whole app — author, encode, serve, mount |
| `EmbeddedRendererVb.vbproj` | three project references; no client toolchain |
