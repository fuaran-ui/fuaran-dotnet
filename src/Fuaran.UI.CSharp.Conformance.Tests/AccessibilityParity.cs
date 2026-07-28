using System.Text.Json;
using Fuaran.UI.CSharp;
using FsFactory = global::Fuaran.UI.Fuaran;
using FsDefaults = global::Fuaran.UI.Defaults;
using FsCanon = Fuaran.UI.OpStream.Abstractions.CanonicalJson;
using FsNode = Fuaran.UI.Generated.Node<object>;

namespace Fuaran.UI.CSharp.Conformance.Tests;

// Phase 307 — mirror-parity test. Asserts that the C# factory's emitted
// `accessibility` shape equals the F# smart constructor's, per kind. Because the
// factories couple to the smart ctors, this holds by construction — but the test
// is the DRIFT GUARD: if a future edit made a C# factory build a node bare (or
// with an independent ARIA table) instead of via the smart ctor, it fails here.
// The F# side is built independently from `Defaults.X` (ARIA is fixed per kind,
// independent of spec content).
internal static class AccessibilityParity
{
    public static void Run(Harness h)
    {
        // ARIA-bearing kinds: C# factory ARIA must equal the F# smart-ctor ARIA.
        Parity(h, "metric",
            Fuaran.Metric(new() { Id = "m", Label = "", Value = 0.0 }),
            A11yOracle.Metric());
        Parity(h, "card",
            Fuaran.Card(new() { Id = "c" }),
            A11yOracle.Card());
        Parity(h, "dashboard",
            Fuaran.Dashboard(new() { Id = "d" }),
            A11yOracle.Dashboard());
        Parity(h, "button",
            Fuaran.Button(new() { Id = "b", Label = "" }),
            A11yOracle.Button());
        Parity(h, "callout",
            Fuaran.Callout(new() { Id = "x", Body = "" }),
            A11yOracle.Callout());
        Parity(h, "progress",
            Fuaran.Progress(new() { Id = "p" }),
            A11yOracle.Progress());
        Parity(h, "toast",
            Fuaran.Toast(new() { Id = "t", Message = "" }),
            A11yOracle.Toast());

        // Sanity: an ARIA-bearing kind genuinely carries ARIA, an ARIA-free one does not.
        h.Check("metric carries ARIA", Accessibility.AriaJson(Fuaran.Metric(new() { Id = "m", Label = "", Value = 0.0 })) is not null);
        h.Check("heading carries no ARIA", Accessibility.AriaJson(Fuaran.Heading(new() { Id = "h", Text = "" })) is null);
    }

    private static void Parity(Harness h, string kind, FuaranNode csharp, string fsharpEncoded) =>
        h.Check(
            $"a11y parity: C# {kind} mirrors F# smart-ctor",
            Aria(csharp.Encode()) == Aria(fsharpEncoded),
            $"C#={Aria(csharp.Encode())} F#={Aria(fsharpEncoded)}");

    private static string? Aria(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("accessibility", out var a) ? a.GetRawText() : null;
    }
}

// The F# smart-ctor oracle — nodes built directly from Defaults.X (ARIA is fixed
// per kind, so the spec content is irrelevant to the accessibility fragment).
#nullable disable
internal static class A11yOracle
{
    private static string Enc(FsNode node) => FsCanon.encodeNode(node);

    internal static string Metric() => Enc(FsFactory.metric<object>("m", FsDefaults.metric));
    internal static string Card() => Enc(FsFactory.card<object>("c", FsDefaults.card<object>()));
    internal static string Dashboard() => Enc(FsFactory.dashboard<object>("d", FsDefaults.dashboard<object>()));
    internal static string Button() => Enc(FsFactory.button<object>("b", FsDefaults.button<object>()));
    internal static string Callout() => Enc(FsFactory.callout<object>("x", FsDefaults.callout));
    internal static string Progress() => Enc(FsFactory.progress<object>("p", FsDefaults.progress));
    internal static string Toast() => Enc(FsFactory.toast<object>("t", FsDefaults.toast));
}
