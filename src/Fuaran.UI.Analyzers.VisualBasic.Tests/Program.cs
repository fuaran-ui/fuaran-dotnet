using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Fuaran.UI.Analyzers.VisualBasic;

namespace Fuaran.UI.Analyzers.VisualBasic.Tests;

// Phase 315 — drives FuaranVbXmlAnalyzer over VB source snippets (with XML literals)
// and asserts the diagnostics: unknown element/attribute, duplicate id, unresolved
// source binding — plus the vocabulary single-source-of-truth pin.
internal static class Program
{
    private static int _passed;
    private static readonly List<string> Failures = new();

    private static async Task<int> Main()
    {
        // ── FUARAN060 — unknown element. ───────────────────────────────────────
        await Expect("FUARAN060 positive: typo'd element",
            """
            Module M
                Sub S()
                    Dim x = <Card id="c"><Metrik id="m"/></Card>
                End Sub
            End Module
            """,
            new[] { "FUARAN060" });

        await Expect("FUARAN060 negative: all known elements",
            """
            Module M
                Sub S()
                    Dim x = <Card id="c"><Metric id="m" label="x" value="1"/></Card>
                End Sub
            End Module
            """,
            Array.Empty<string>());

        // The regression the structural pin found. `<Tone>` is a real child
        // element — the translator has read it since Phase 750
        // (`ChildElements(c, "Tone")`) and it carries a row in
        // `Vocabulary.Attributes` — but it was absent from
        // `Vocabulary.Structural`, so `IsKnownElement` said no and every valid
        // use raised FUARAN060 and skipped the attribute check entirely.
        await Expect("FUARAN060 negative: <Tone> is a recognised structural child",
            """
            Module M
                Sub S()
                    Dim x = <DataGrid id="g" source="q"><Column type="text" field="f" label="F"><Tone value="ok" tone="positive"/></Column></DataGrid>
                End Sub
            End Module
            """,
            Array.Empty<string>());

        await Expect("non-Fuaran literal ignored",
            """
            Module M
                Sub S()
                    Dim x = <html><body id="b"/></html>
                End Sub
            End Module
            """,
            Array.Empty<string>());

        // ── FUARAN061 — unknown attribute. ─────────────────────────────────────
        await Expect("FUARAN061 positive: unknown attribute",
            """
            Module M
                Sub S()
                    Dim x = <Metric id="m" label="x" value="1" bogus="y"/>
                End Sub
            End Module
            """,
            new[] { "FUARAN061" });

        // ── FUARAN001 — duplicate id. ──────────────────────────────────────────
        await Expect("FUARAN001 positive: duplicate id",
            """
            Module M
                Sub S()
                    Dim x = <Card id="c"><Metric id="dup" label="x" value="1"/><Heading id="dup" text="y"/></Card>
                End Sub
            End Module
            """,
            new[] { "FUARAN001" });

        // ── FUARAN010 — unresolved source binding. ─────────────────────────────
        const string manifest = """{ "queries": ["knownQuery"], "msgCases": [] }""";

        await Expect("FUARAN010 positive: unresolved $binding",
            """
            Module M
                Sub S()
                    Dim x = <Metric id="m" label="x" value="$unknownQuery"/>
                End Sub
            End Module
            """,
            new[] { "FUARAN010" },
            manifest);

        await Expect("FUARAN010 negative: resolved $binding",
            """
            Module M
                Sub S()
                    Dim x = <Metric id="m" label="x" value="$knownQuery"/>
                End Sub
            End Module
            """,
            Array.Empty<string>(),
            manifest);

        // ── Vocabulary single source of truth: analyzer == translator. ─────────
        var translatorKinds = global::Fuaran.UI.VisualBasic.FuaranXml.KnownElements().ToHashSet(StringComparer.Ordinal);
        Check("vocabulary pin: analyzer Kinds == translator KnownElements",
            Vocabulary.Kinds.SetEquals(translatorKinds),
            $"analyzer-only: [{string.Join(",", Vocabulary.Kinds.Except(translatorKinds))}]  translator-only: [{string.Join(",", translatorKinds.Except(Vocabulary.Kinds))}]");

        // ── Phase 1104: the same question one level down — the attribute table
        //    against the IDL's per-kind FIELDS. The pin above is at kind level and
        //    cannot see a kind that gained a field.
        AuthoringSurfacePin.Run(Check);

        // ── The same question again for the CHILD-ELEMENT table. The pin above
        //    stops where the attribute stops; a structured wire field takes a
        //    child element instead, and `Vocabulary.Structural` — the set that
        //    decides whether FUARAN060 fires — was pinned against nothing.
        StructuralElementPin.Run(Check, AuthoringSurfacePin.FindIdl());

        Console.WriteLine($"[vb-analyzer-tests] {_passed} passed, {Failures.Count} failed.");
        foreach (var f in Failures)
        {
            Console.WriteLine($"  FAIL {f}");
        }

        return Failures.Count == 0 ? 0 : 1;
    }

    private static void Check(string name, bool condition, string? detail = null)
    {
        if (condition)
        {
            _passed++;
        }
        else
        {
            Failures.Add(detail is null ? name : $"{name}: {detail}");
        }
    }

    private static async Task Expect(string name, string source, string[] expected, string? manifestJson = null)
    {
        var diagnostics = await RunAnalyzer(source, manifestJson);
        var ids = diagnostics.Select(d => d.Id).Where(id => id.StartsWith("FUARAN")).OrderBy(x => x).ToArray();
        var want = expected.OrderBy(x => x).ToArray();
        Check(name, ids.SequenceEqual(want), $"expected [{string.Join(",", want)}], got [{string.Join(",", ids)}]");
    }

    private static async Task<ImmutableArray<Diagnostic>> RunAnalyzer(string source, string? manifestJson)
    {
        var references = ReferencePaths().Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToImmutableArray();
        var tree = VisualBasicSyntaxTree.ParseText(source);
        var compilation = VisualBasicCompilation.Create(
            "VbAnalyzerTest",
            new[] { tree },
            references,
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var compileErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (compileErrors.Length > 0)
        {
            throw new InvalidOperationException("VB snippet failed to compile: " + string.Join("; ", compileErrors.Select(e => e.ToString())));
        }

        var additionalFiles = manifestJson is null
            ? ImmutableArray<AdditionalText>.Empty
            : ImmutableArray.Create<AdditionalText>(new InMemoryAdditionalText("fuaran-validator.manifest.json", manifestJson));

        var withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new FuaranVbXmlAnalyzer()),
            new AnalyzerOptions(additionalFiles));

        return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static IEnumerable<string> ReferencePaths()
    {
        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "";
        // The VB snippets use only XML literals (System.Xml.Linq) + core BCL — the
        // trusted-platform set covers them; no Fuaran reference is needed (the analyzer
        // works on syntax + its embedded vocabulary).
        return tpa.Split(Path.PathSeparator).Where(p => p.Length > 0 && File.Exists(p));
    }
}

internal sealed class InMemoryAdditionalText : AdditionalText
{
    private readonly SourceText _text;

    public InMemoryAdditionalText(string path, string content)
    {
        Path = path;
        _text = SourceText.From(content);
    }

    public override string Path { get; }

    public override SourceText GetText(System.Threading.CancellationToken cancellationToken = default) => _text;
}
