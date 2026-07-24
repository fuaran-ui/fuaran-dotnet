using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Fuaran.UI.Analyzers.Tests;

// Phase 314 — analyzer tests. Drives FuaranVeneerAnalyzer over source snippets and
// asserts the resulting diagnostics: positive + negative per rule.
internal static class Program
{
    private static int _passed;
    private static readonly List<string> Failures = new();

    private static async Task<int> Main()
    {
        // ── FUARAN001 — NodeId uniqueness. ─────────────────────────────────────
        await ExpectDiagnostics(
            "FUARAN001 positive: duplicate id across a block",
            """
            using Fuaran.UI.CSharp;
            using static Fuaran.UI.CSharp.Fuaran;
            class C { void M() {
                var a = Heading(new() { Id = "dup", Text = "x" });
                var b = Metric(new() { Id = "dup", Label = "y", Value = 1.0 });
            } }
            """,
            expected: new[] { "FUARAN001" });

        await ExpectDiagnostics(
            "FUARAN001 negative: unique ids",
            """
            using Fuaran.UI.CSharp;
            using static Fuaran.UI.CSharp.Fuaran;
            class C { void M() {
                var a = Heading(new() { Id = "one", Text = "x" });
                var b = Metric(new() { Id = "two", Label = "y", Value = 1.0 });
            } }
            """,
            expected: Array.Empty<string>());

        // ── FUARAN010 — Binding.Query name resolution. ─────────────────────────
        const string manifest = """{ "queries": ["knownQuery"], "msgCases": [] }""";

        await ExpectDiagnostics(
            "FUARAN010 positive: query not in manifest",
            """
            using Fuaran.UI.CSharp;
            class C { void M() {
                var b = Binding.Query<double>("unknownQuery");
            } }
            """,
            expected: new[] { "FUARAN010" },
            manifestJson: manifest);

        await ExpectDiagnostics(
            "FUARAN010 negative: query in manifest",
            """
            using Fuaran.UI.CSharp;
            class C { void M() {
                var b = Binding.Query<double>("knownQuery");
            } }
            """,
            expected: Array.Empty<string>(),
            manifestJson: manifest);

        await ExpectDiagnostics(
            "FUARAN010 silent: no manifest wired",
            """
            using Fuaran.UI.CSharp;
            class C { void M() {
                var b = Binding.Query<double>("anythingGoes");
            } }
            """,
            expected: Array.Empty<string>());

        Console.WriteLine($"[analyzer-tests] {_passed} passed, {Failures.Count} failed.");
        foreach (var f in Failures)
        {
            Console.WriteLine($"  FAIL {f}");
        }

        return Failures.Count == 0 ? 0 : 1;
    }

    private static async Task ExpectDiagnostics(string name, string source, string[] expected, string? manifestJson = null)
    {
        var diagnostics = await RunAnalyzer(source, manifestJson);
        var ids = diagnostics.Select(d => d.Id).OrderBy(x => x).ToArray();
        var want = expected.OrderBy(x => x).ToArray();

        if (ids.SequenceEqual(want))
        {
            _passed++;
        }
        else
        {
            Failures.Add($"{name}: expected [{string.Join(",", want)}], got [{string.Join(",", ids)}]");
        }
    }

    private static async Task<ImmutableArray<Diagnostic>> RunAnalyzer(string source, string? manifestJson)
    {
        var references = ReferencePaths()
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToImmutableArray();

        var compilation = CSharpCompilation.Create(
            "AnalyzerTest",
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        // Guard: the snippet itself must compile, else the analysis is meaningless.
        var compileErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (compileErrors.Length > 0)
        {
            throw new InvalidOperationException("Test snippet failed to compile: " + string.Join("; ", compileErrors.Select(e => e.ToString())));
        }

        var additionalFiles = manifestJson is null
            ? ImmutableArray<AdditionalText>.Empty
            : ImmutableArray.Create<AdditionalText>(new InMemoryAdditionalText("fuaran-validator.manifest.json", manifestJson));

        var options = new AnalyzerOptions(additionalFiles);
        var withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new FuaranVeneerAnalyzer()),
            options);

        return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    // Reference the whole trusted-platform-assembly set (framework) plus the veneer
    // + its F# deps, so a snippet using `Fuaran.UI.CSharp` resolves fully.
    private static IEnumerable<string> ReferencePaths()
    {
        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "";
        var paths = new HashSet<string>(tpa.Split(Path.PathSeparator).Where(p => p.Length > 0), StringComparer.OrdinalIgnoreCase)
        {
            typeof(global::Fuaran.UI.CSharp.FuaranNode).Assembly.Location,          // Fuaran.UI.CSharp
            typeof(global::Fuaran.UI.Types.NodeId).Assembly.Location,              // Fuaran.UI
            typeof(global::Microsoft.FSharp.Core.FSharpOption<int>).Assembly.Location, // FSharp.Core
        };

        return paths.Where(File.Exists);
    }
}

/// <summary>An in-memory <see cref="AdditionalText"/> for feeding the manifest to the analyzer.</summary>
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
