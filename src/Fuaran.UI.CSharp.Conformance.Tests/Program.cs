using System;
using Fuaran.UI.CSharp;
using FsCanon = Fuaran.UI.OpStream.Abstractions.CanonicalJson;

namespace Fuaran.UI.CSharp.Conformance.Tests;

// Phase 304 smoke coverage (Phase 306 grows this to the full corpus). Every check
// authors a tree through the C# `Fuaran.X(new() { … })` surface — which also
// exercises the consumer ergonomics (the `Fuaran` factory class resolves cleanly
// under `using Fuaran.UI.CSharp;` despite the `Fuaran` namespace) — and asserts
// its wire bytes.
internal static class Program
{
    private static int Main()
    {
        var h = new Harness();

        // ── ARIA-free leaf kinds: C# output byte-matches the bare corpus exactly.
        //    (heading / markdown use Defaults.Accessibility.none, so no ARIA is
        //    injected and the encoding equals the bare fixture.) ──────────────
        if (Corpus.Available)
        {
            var heading = Fuaran.Heading(new()
            {
                Id = "heading-1",
                Text = "Channel performance",
                Level = 2,
            });
            h.ByteEqual("heading-1 == corpus", heading.Encode(), Corpus.ReadFixture("nodes/heading-1.json"));

            var markdown = Fuaran.Markdown(new() { Id = "markdown-1", Text = "Updated hourly." });
            h.ByteEqual("markdown-1 == corpus", markdown.Encode(), Corpus.ReadFixture("nodes/markdown-1.json"));
        }
        else
        {
            Console.WriteLine("[smoke] wire-format-fixtures corpus absent — skipping corpus byte-compare.");
        }

        // ── ARIA-bearing kinds: C# output byte-matches the F# smart-ctor host,
        //    built independently here (the "mirror F# by construction" parity,
        //    Phase 307). The bare corpus fixture is intentionally NOT the target
        //    for these — the smart ctors inject per-component ARIA the corpus
        //    (authored bare) omits. ────────────────────────────────────────────
        var csMetric = Fuaran.Metric(new()
        {
            Id = "metric-1",
            Label = "Revenue",
            Value = 1234.5,
            Format = CellFormat.Currency("GBP"),
            Tone = Tone.Brand,
            Trend = 0.07,
            TrendFormat = CellFormat.Percent(1),
            Icon = "trending-up",
            Subtext = "vs last month",
        });
        h.ByteEqual("metric mirrors F# smart-ctor", csMetric.Encode(), Oracle.MetricFsHost());

        // The ARIA is genuinely present (the smart-ctor coupling is real, Phase 304).
        h.Check(
            "metric carries injected ARIA",
            csMetric.Encode().Contains("\"accessibility\":{\"liveRegion\":\"polite\"}"),
            csMetric.Encode());

        // ── Composite tree (Card > Metric): nested children encode faithfully. ──
        var card = Fuaran.Card(new()
        {
            Id = "insights",
            Heading = "Insights",
            Children = [csMetric],
        });
        h.Check("card encodes its id", card.Encode().Contains("\"id\":\"insights\""), card.Encode());
        h.Check("card contains nested metric", card.Encode().Contains("\"$type\":\"Metric\""));

        // ── Negative control: a mutated tree must NOT match, so no check passes
        //    vacuously. ────────────────────────────────────────────────────────
        var mutated = Fuaran.Heading(new() { Id = "heading-1", Text = "MUTATED", Level = 2 });
        if (Corpus.Available)
        {
            h.Check(
                "negative control: mutated heading != corpus",
                mutated.Encode() != Corpus.ReadFixture("nodes/heading-1.json"));
        }

        // ── Runtime facade end-to-end: author → validate → apply → verify-chain
        //    (Phase 559). Corpus-independent — a C#-only consumer exercises the
        //    shared F# apply engine + op-stream hash chain with no F# authored. ──
        Runtime.Run(h);

        // ── Full node-shape coverage guard (Phase 305). ────────────────────────
        Coverage.Run(h);

        // ── The 861–867 behaviour + constraint slots (Phase 873). No reflection
        //    over the kind set can notice these, which is why they are hand-written.
        //    Ordered AHEAD of the corpus pass deliberately: the §21 shape-limit
        //    fixture family is orphaned from the corpus generator, and its 72-level
        //    `nodes/limit-node-depth-at-max.json` throws System.Text.Json's depth-64
        //    default inside Conformance, aborting the process before Report runs.
        //    That is a pre-existing defect enumerated by fuaran#864 and filed
        //    separately — not repaired here, and not a reason to leave these checks
        //    unreachable behind it. ──────────────────────────────────────────────
        GapClosureSlots.Run(h);

        // ── Full corpus conformance (Phase 306). ───────────────────────────────
        Conformance.Run(h);

        // ── Authoring-parity for the ARIA-free leaf kinds (Phase 306). ──────────
        Authoring.Run(h);

        // ── Accessibility mirror-parity drift guard (Phase 307). ───────────────
        AccessibilityParity.Run(h);

        // ── C#-native decode (Phase 309) — no FSharp.Core result type touched. ──
        // Round-trip a canonical fixture through the decode facade.
        if (Corpus.Available)
        {
            var headingJson = Corpus.ReadFixture("nodes/heading-1.json");
            h.Check("decode: heading-1 round-trips", Decode.NodeRoundTrips(headingJson));

            var ok = Decode.TryNode(headingJson, out var decoded, out var _);
            h.Check("decode: TryNode succeeds + yields a node", ok && decoded is not null && decoded.Id == "heading-1");
        }

        // Structured error: canonical code + path, no FSharp.Core result on the surface.
        var invalid = Decode.Node("this is not json");
        h.Check("decode: invalid JSON → INVALID_JSON", !invalid.IsOk && invalid.Error!.Kind == DecodeErrorCode.InvalidJson);

        var missingId = Decode.Node("{\"kind\":{\"$type\":\"Markdown\",\"text\":{\"$type\":\"Literal\",\"text\":\"x\"}}}");
        h.Check(
            "decode: missing id → MISSING_FIELD at $.id",
            !missingId.IsOk && missingId.Error!.Kind == DecodeErrorCode.MissingField && missingId.Error.Path.StartsWith("$.id"),
            missingId.Error is null ? "(decoded ok — expected a reject)" : $"{missingId.Error.Code} @ {missingId.Error.Path}");

        return h.Report("csharp-conformance");
    }
}

// The independent F# smart-ctor oracle. Builds the same logical tree via the F#
// smart constructors with a hand-built spec (a construction path distinct from
// the options records), so a byte match proves the veneer's options→spec mapping
// is faithful. FSharp.Core interop is isolated here.
#nullable disable
internal static class Oracle
{
    internal static string MetricFsHost()
    {
        // Ctor arg order = Generated.fs MetricSpec declaration order (Label, Value, Format,
        // Tone, Weight, Emphasis, Trend, TrendFormat, TrendPolarity, Icon, Subtext).
        var spec = new global::Fuaran.UI.Generated.MetricSpec(
            global::Fuaran.UI.Generated.TextSource.NewLiteral("Revenue"),
            global::Fuaran.UI.Generated.Binding<double>.NewStatic(Microsoft.FSharp.Core.FSharpOption<double>.Some(1234.5)),
            global::Fuaran.UI.Generated.CellFormat.NewCurrency("GBP"),
            global::Fuaran.UI.Generated.ToneVariant.Brand,
            global::Fuaran.UI.Generated.StyleWeight.Standard,
            global::Fuaran.UI.Generated.Emphasis.Normal,
            Microsoft.FSharp.Core.FSharpOption<global::Fuaran.UI.Generated.Binding<double>>.Some(
                global::Fuaran.UI.Generated.Binding<double>.NewStatic(Microsoft.FSharp.Core.FSharpOption<double>.Some(0.07))),
            Microsoft.FSharp.Core.FSharpOption<global::Fuaran.UI.Generated.CellFormat>.Some(
                global::Fuaran.UI.Generated.CellFormat.NewPercent(Microsoft.FSharp.Core.FSharpOption<int>.Some(1))),
            global::Fuaran.UI.Generated.TrendPolarity.HigherIsBetter,
            Microsoft.FSharp.Core.FSharpOption<string>.Some("trending-up"),
            Microsoft.FSharp.Core.FSharpOption<global::Fuaran.UI.Generated.TextSource>.Some(
                global::Fuaran.UI.Generated.TextSource.NewLiteral("vs last month")));

        var node = global::Fuaran.UI.Fuaran.metric<object>("metric-1", spec);
        return FsCanon.encodeNode(node);
    }
}
