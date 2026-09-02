using Fuaran.UI.CSharp;
using Fuaran.UI.Renderer.Web;
using Microsoft.FSharp.Core;
using static Fuaran.UI.CSharp.Fuaran;

// ============================================================================
//  Phase 577 — a Fuaran tree authored in C#, rendered live in the browser.
//
//  Four moving parts, and that is the whole app:
//
//    1. Author the tree with the C# veneer.
//    2. Encode it FOR TRANSPORT — the encoder that refuses a tree whose
//       interaction would not survive serialisation.
//    3. `MapFuaranRenderer()` serves the embedded browser renderer.
//    4. `Snippet.mount` emits the HTML that hydrates the tree.
//
//  There is no Node toolchain in this directory. No package.json, no bundler,
//  no wwwroot: the renderer arrives inside Fuaran.UI.Renderer.Web. That is the
//  claim the sample exists to demonstrate.
//
//  WHAT THE INTERACTION IS, AND WHAT IT IS NOT.
//
//  The disclosure below is bound to `$state.detailsOpen`, so clicking it is a
//  REAL interaction handled entirely in the browser: the renderer writes the
//  new value back to the state slot and every reader of that slot re-renders,
//  with no round trip and no host code. That is the wire-representable
//  interaction this tier offers, and it works end to end.
//
//  It is NOT `Action.Dispatch`. That case carries a host closure, the encoder
//  drops the payload, and a decoding browser gets an affordance that fires and
//  does nothing. Full Fable is the one tier where it survives, because there
//  the tree is never serialised. See docs/EMBEDDED-RENDERER.md §2.
//
//  The button below is the OTHER kind: a `Notify`, which DOES cross the wire.
//  The renderer POSTs it to the `NotifyEndpoint` this page declares, so the
//  click reaches C# host code — a real round trip, authored in C#, with no
//  closure anywhere. (Phase 1153 gave the veneer its `Action` vocabulary; until
//  then this sample said plainly that it could not express one rather than
//  reaching past the veneer into the F# tier to fake it.)
// ============================================================================

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Serve the embedded renderer bundle, the reference stylesheet and the
// fingerprint under /_fuaran. One call; nothing to install.
app.MapFuaranRenderer();

static FuaranNode BuildTree() =>
    Dashboard(new()
    {
        Id = "root",
        Children =
        [
            Heading(new() { Id = "title", Text = "Embedded renderer — C#" }),
            Markdown(new()
            {
                Id = "note",
                Text =
                    "This tree was authored in **C#**, encoded as canonical wire JSON, and rendered "
                    + "in your browser by the renderer embedded in `Fuaran.UI.Renderer.Web`. "
                    + "No Node toolchain was involved on this side.",
            }),
            Disclosure(new()
            {
                Id = "details",
                Heading = "How this page was built",
                // Bound to a state slot, so the toggle is handled in the
                // browser and written back — a live interaction with no server.
                Open = Binding.State("detailsOpen", false),
                Children =
                [
                    Markdown(new()
                    {
                        Id = "details-body",
                        Text =
                            "The server authored a `Node` tree, encoded it with "
                            + "`encodeNodeForTransport`, and inlined the JSON into this page. "
                            + "The browser decoded it and rendered it. Toggling this panel wrote "
                            + "`$state.detailsOpen` and re-rendered every reader of that slot — "
                            + "no request was made.",
                    }),
                ],
            }),
            Button(new()
            {
                Id = "ping",
                Label = "Ping the host",
                Variant = ButtonVariant.Primary,
                // A `Notify` is wire-representable in full — channel and payload
                // both survive serialisation — so this button reaches C# host code
                // through the endpoint declared in the mount options below. No
                // closure is involved, which is exactly why it survives.
                OnClick = FuaranAction.Notify(
                    "sample.ping",
                    Payload.Object(("from", "embedded-renderer-csharp"))),
            }),
        ],
    });

app.MapGet("/", (IWebHostEnvironment env) =>
{
    // ENCODE FOR TRANSPORT, not `Encode()`. The transport encoder returns the
    // same canonical bytes and REFUSES a tree carrying a closure the wire would
    // drop — a `Dispatch`, a `Call` with an `onResult`, a `ReadFileBody` with
    // an `onRead`. Failing here is the whole value: the alternative is a button
    // that works in every test and does nothing in the browser.
    if (!BuildTree().TryEncodeForTransport(out var wireJson, out var lossy))
    {
        var slots = string.Join(", ", lossy.Select(p => $"{p.NodeId} ({p.Slot})"));

        return Results.Problem(
            $"This tree carries interaction that would not survive serialisation: {slots}. "
            + "Replace it with a wire-representable action, or render it in process with full Fable.");
    }

    var options = new Snippet.MountOptions(
        "fuaran-root",
        "/_fuaran",
        // Where a `Notify` is POSTed as {"channel": …, "payload": …}. Stated
        // rather than defaulted: a read-only page leaves it None on purpose.
        FSharpOption<string>.Some("/notify"),
        FSharpOption<string>.None, // no CSP nonce in the sample
        // Development only: warns when the embedded bundle and the authoring
        // packages this app restored have parted company.
        env.IsDevelopment());

    var html =
        $$"""
        <!doctype html>
        <html lang="en">
          <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Fuaran — embedded renderer (C#)</title>
            {{Snippet.styleLink("/_fuaran")}}
          </head>
          <body>
            {{Snippet.scriptTag("/_fuaran")}}
            {{Snippet.mount(options, Fuaran.UI.Renderer.Theme.vocabularyFingerprint, wireJson!)}}
          </body>
        </html>
        """;

    return Results.Content(html, "text/html; charset=utf-8");
});

// The host end of the round trip. The mount snippet POSTs every `Notify` here as
// {"channel": …, "payload": …}; this sample logs it, which is enough to see the
// click arrive in C#. A real host would dispatch on the channel.
app.MapPost("/notify", async (HttpRequest request, ILogger<Program> log) =>
{
    using var reader = new StreamReader(request.Body);
    log.LogInformation("Fuaran notify: {Body}", await reader.ReadToEndAsync());
    return Results.NoContent();
});

app.Run();
