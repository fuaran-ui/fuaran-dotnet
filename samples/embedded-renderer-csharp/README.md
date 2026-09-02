# Embedded renderer — C# sample

A minimal ASP.NET app that authors a Fuaran tree **in C#**, serves its canonical wire JSON, and
hydrates it in the browser through the renderer embedded in `Fuaran.UI.Renderer.Web`.

```
dotnet run --project samples/embedded-renderer-csharp --urls http://localhost:5199
```

Then open the URL. No port is hard-coded, so the sample needs no allocation from the workspace
port band and two copies can run side by side.

## What it demonstrates

**No Node toolchain.** Look at this directory: there is no `package.json`, no bundler config, no
`wwwroot`. The browser renderer arrives inside the `Fuaran.UI.Renderer.Web` package and is served
by one `MapFuaranRenderer()` call. That is the whole claim.

**Encode for transport, not for the chain.** The tree goes through
`TryEncodeForTransport`, which refuses a tree carrying interaction that serialisation would lose
and names every offending node and slot. `Encode()` is unchanged and does not refuse — its
closure-blindness feeds the op-stream hash chain and is deliberate there. Which one you call is
how intent is declared.

**A live interaction that survives the wire.** The disclosure is bound to `$state.detailsOpen`.
Clicking it is handled entirely in the browser: the renderer writes the new value back to the
state slot and every reader of that slot re-renders. No round trip, no host code.

**A host round trip, authored in C#.** "Ping the host" carries a `FuaranAction.Notify` — channel
plus JSON payload, both of which survive serialisation, no closure anywhere. The page declares a
`NotifyEndpoint`, the mount snippet POSTs the notification to it, and `MapPost("/notify", …)`
logs it. That is a click authored in C#, crossing the wire, arriving in C# host code.

**A fingerprint you can query.** `GET /_fuaran/fingerprint.json` says which renderer version is
embedded, which wire profile it decodes, and the class vocabulary it was synced against. Run under
`ASPNETCORE_ENVIRONMENT=Development` and the page also carries a drift warning when any of that
disagrees with the authoring packages this app restored.

## What it does *not* demonstrate, and why

**`Action.Dispatch`.** It carries a host closure; the encoder drops the payload and the decoder
rebuilds it as the `"<closure>"` sentinel, so a serialised `Dispatch` arrives as an affordance that
renders, fires, and does nothing. Full Fable is the one tier where it survives, because there the
tree is never serialised. See [`docs/EMBEDDED-RENDERER.md`](../../docs/EMBEDDED-RENDERER.md) §2.

**A closure-shaped handler.** A select's change, a grid's row click, a form field's edit take a
closure in the language tier too; the encoder erases them to `"<closure>"`, so they are not
authorable as data from any tier and this sample does not fake one.

## Files

| | |
|---|---|
| `Program.cs` | the whole app — author, encode, serve, mount |
| `EmbeddedRendererCSharp.csproj` | four project references; no client toolchain |
