using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Fuaran.UI.CSharp.Conformance.Tests;

// Phase 306 — corpus conformance by construction. Drives the full shared
// wire-format-fixtures corpus through the veneer:
//
//   * every node / op round-trip fixture decodes + re-encodes byte-identically
//     through the C#-native Decode facade (Phase 309), proving the veneer lands
//     on the canonical wire — no fourth CI leg, the byte-canonicity rides the
//     existing F#<->corpus gate;
//   * every reject fixture surfaces the canonical code + path;
//   * coverage-vs-corpus: every node fixture's kind.$type has a C# authoring
//     factory, so a corpus that grows a kind the veneer can't author fails here.
//
// The ARIA reconciliation (Phase 307): the corpus is authored BARE (no ARIA),
// whereas the C# factories inherit per-component ARIA from the F# smart ctors, so
// authoring a fixture via the factories and byte-comparing to the bare file would
// diverge for ARIA-bearing kinds. The decode round-trip proves byte-fidelity
// ARIA-agnostically (bare in -> bare out); the C#==F#-smart-ctor authoring parity
// is proven separately (Program.cs / Accessibility.cs). No corpus regen.
internal static class Conformance
{
    private sealed record Fixture(string Id, string Kind, string Decoder, string InputFile, string? ExpectedFile, string? ExpectedErrorCode, string? ExpectedPath);

    public static void Run(Harness h)
    {
        if (!Corpus.Available)
        {
            System.Console.WriteLine("[conformance] wire-format-fixtures corpus absent — skipping full corpus run.");
            return;
        }

        foreach (var f in LoadManifest())
        {
            switch (f.Kind)
            {
                case "node-round-trip":
                    RunNodeRoundTrip(h, f);
                    break;
                case "op-round-trip":
                    RunOpRoundTrip(h, f);
                    break;
                case "reject":
                    RunReject(h, f);
                    break;
            }
        }
    }

    private static void RunNodeRoundTrip(Harness h, Fixture f)
    {
        var input = Corpus.ReadFixture(f.InputFile);
        var expected = Corpus.ReadFixture(f.ExpectedFile ?? f.InputFile);

        if (!Decode.TryNode(input, out var node, out var err))
        {
            h.Check($"node round-trip {f.Id}", false, $"decode failed: {err.Code} @ {err.Path}");
            return;
        }

        h.ByteEqual($"node round-trip {f.Id}", node.Encode(), expected);

        // Coverage-vs-corpus: the fixture's kind.$type must have a C# authoring factory.
        var kindType = KindType(input);
        h.Check(
            $"coverage-vs-corpus: factory for {kindType} ({f.Id})",
            kindType is not null && Coverage.HasFactoryForKind(kindType),
            kindType is null ? "(no kind.$type in fixture)" : $"no C# factory authors kind {kindType}");
    }

    private static void RunOpRoundTrip(Harness h, Fixture f)
    {
        var input = Corpus.ReadFixture(f.InputFile);
        var expected = Corpus.ReadFixture(f.ExpectedFile ?? f.InputFile);

        if (!Decode.TryOp(input, out var op, out var err))
        {
            h.Check($"op round-trip {f.Id}", false, $"decode failed: {err.Code} @ {err.Path}");
            return;
        }

        h.ByteEqual($"op round-trip {f.Id}", op.Encode(), expected);
    }

    private static void RunReject(Harness h, Fixture f)
    {
        var input = Corpus.ReadFixture(f.InputFile);
        var error = f.Decoder == "op" ? Decode.Op(input).Error : Decode.Node(input).Error;

        if (error is null)
        {
            h.Check($"reject {f.Id}", false, "decoded ok — expected a reject");
            return;
        }

        h.Check(
            $"reject {f.Id}: code {f.ExpectedErrorCode}",
            error.Code == f.ExpectedErrorCode,
            $"expected {f.ExpectedErrorCode}, got {error.Code}");

        h.Check(
            $"reject {f.Id}: path {f.ExpectedPath}",
            f.ExpectedPath is null || error.Path.StartsWith(f.ExpectedPath),
            $"expected path prefix {f.ExpectedPath}, got {error.Path}");
    }

    private static IEnumerable<Fixture> LoadManifest()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Corpus.Root!, "manifest.json")));
        foreach (var el in doc.RootElement.GetProperty("fixtures").EnumerateArray())
        {
            yield return new Fixture(
                el.GetProperty("id").GetString()!,
                el.GetProperty("kind").GetString()!,
                el.TryGetProperty("decoder", out var d) ? d.GetString()! : "node",
                el.GetProperty("inputFile").GetString()!,
                el.TryGetProperty("expectedFile", out var e) ? e.GetString() : null,
                el.TryGetProperty("expectedErrorCode", out var c) ? c.GetString() : null,
                el.TryGetProperty("expectedPath", out var p) ? p.GetString() : null);
        }
    }

    // Parse corpus payloads at the wire format's own syntactic bound, not
    // System.Text.Json's 64-level default. A node level costs several JSON
    // levels, so the §21 accept fixture at exactly `MaxDepth` node levels is 72
    // JSON levels — well inside what the decoder accepts and past what the
    // default reader will read. On the default this harness did not fail a
    // fixture, it THREW, killing the whole conformance run at the first deep
    // payload.
    private static readonly JsonDocumentOptions WireJson = new()
    {
        MaxDepth = global::Fuaran.UI.WireLimits.MaxJsonDepth,
    };

    private static string? KindType(string nodeJson)
    {
        using var doc = JsonDocument.Parse(nodeJson, WireJson);
        return doc.RootElement.TryGetProperty("kind", out var kind) && kind.TryGetProperty("$type", out var t)
            ? t.GetString()
            : null;
    }
}
