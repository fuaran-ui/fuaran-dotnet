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
    <Disclosure id="details" heading="How this page was built" default-open="false">
        <Markdown id="details-body" text="..."/>
    </Disclosure>
</Dashboard>
```

`FuaranXml.Translate` walks it and drives the same shared factory surface every other authoring
tier uses, so the bytes are identical to the C# and F# equivalents. There is no VB codec.

**Encode for transport.** `TryEncodeForTransport` refuses a tree carrying interaction that
serialisation would lose, naming every offending node and slot.

**A live interaction.** The disclosure is a native `<details>`: clicking it toggles in the browser
with no host code and no round trip.

## Two VB-specific things worth knowing

**Call `MapFuaranRenderer` as a static method.** The extension form
(`app.MapFuaranRenderer()`) is what a C# host writes; VB does not bind it here, because `Fuaran`
is a namespace *and* a type name in this runtime and import resolution goes the other way. The
static form is exact, needs no import, and is the same method:

```vb
Global.Fuaran.UI.Renderer.Web.FuaranRendererEndpointExtensions.MapFuaranRenderer(app)
```

The same clash is why the sample writes `Global.Fuaran.UI.Renderer.Theme.vocabularyFingerprint`.

**`$name` in the XML dialect is a QUERY binding, not a state binding.** A query binding is
host-fed and read-only, so `open="$panel"` reads a value the host supplies and a toggle cannot
write it back. The C# leg of this pair shows a state-bound control; from VB, a state binding is
authorable today only through the C# factory surface, which is plain .NET and callable from VB
directly. This sample uses the uncontrolled `<details>` instead, which is a real interaction and
needs no binding at all.

## What it does *not* demonstrate

**`Action.Dispatch`**, for the reason the C# sample's README gives: it carries a host closure that
the wire drops. See [`docs/EMBEDDED-RENDERER.md`](../../docs/EMBEDDED-RENDERER.md) §2.

**A host round trip.** The VB tier authors through the C# veneer, which does not yet expose the
`Action` vocabulary, so `Notify` is not authorable from either language today.

## Files

| | |
|---|---|
| `Program.vb` | the whole app — author, encode, serve, mount |
| `EmbeddedRendererVb.vbproj` | three project references; no client toolchain |
