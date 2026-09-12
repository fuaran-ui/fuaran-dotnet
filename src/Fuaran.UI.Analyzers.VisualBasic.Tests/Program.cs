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
        // ── FUARAN150 — unknown element. ───────────────────────────────────────
        await Expect("FUARAN150 positive: typo'd element",
            """
            Module M
                Sub S()
                    Dim x = <Card id="c"><Metrik id="m"/></Card>
                End Sub
            End Module
            """,
            new[] { "FUARAN150" });

        await Expect("FUARAN150 negative: all known elements",
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
        // use raised FUARAN150 and skipped the attribute check entirely.
        await Expect("FUARAN150 negative: <Tone> is a recognised structural child",
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

        // ── FUARAN151 — unknown attribute. ─────────────────────────────────────
        await Expect("FUARAN151 positive: unknown attribute",
            """
            Module M
                Sub S()
                    Dim x = <Metric id="m" label="x" value="1" bogus="y"/>
                End Sub
            End Module
            """,
            new[] { "FUARAN151" });

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

        // ── FUARAN117 / the state spelling (Phase 1154). ───────────────────────
        //
        // The FIRST of these is the reason the analyzer half shipped in the same
        // change-set as the translator: FUARAN010 checks every `$`-prefixed value
        // against the manifest's query list, and a state key is not a query name,
        // so without the discriminator every valid state binding would be reported
        // as an unresolved query — a false positive on the phase's own feature.
        await Expect("FUARAN010 negative: a state binding is not query-checked",
            """
            Module M
                Sub S()
                    Dim x = <Disclosure id="d" heading="H" open="$state.panelOpen"/>
                End Sub
            End Module
            """,
            Array.Empty<string>(),
            manifest);

        // And the discriminator is the "$state." prefix, not "starts with $state":
        // a query named `stateful` is still query-checked, and still unresolved.
        await Expect("FUARAN010 positive: '$stateful' is still a query name",
            """
            Module M
                Sub S()
                    Dim x = <Heading id="h" text="$stateful"/>
                End Sub
            End Module
            """,
            new[] { "FUARAN010" },
            manifest);

        await Expect("FUARAN117 positive: '$state' carries no key",
            """
            Module M
                Sub S()
                    Dim x = <Disclosure id="d" heading="H" open="$state"/>
                End Sub
            End Module
            """,
            new[] { "FUARAN117" });

        await Expect("FUARAN117 positive: '$state.' carries no key",
            """
            Module M
                Sub S()
                    Dim x = <Disclosure id="d" heading="H" open="$state."/>
                End Sub
            End Module
            """,
            new[] { "FUARAN117" });

        // Fires with NO manifest wired, unlike FUARAN010: this is a malformed
        // spelling the translator throws on, not a name that might resolve later.
        await Expect("FUARAN117 negative: a well-formed state binding, no manifest",
            """
            Module M
                Sub S()
                    Dim x = <Disclosure id="d" heading="H" open="$state.panelOpen"/>
                End Sub
            End Module
            """,
            Array.Empty<string>());

        // The second copy of the spelling, pinned to the translator's — the
        // `Kinds == KnownElements` discipline applied to the binding prefixes,
        // because a netstandard2.0 analyzer cannot load the net10 veneer and the
        // two would otherwise be free to disagree about what `$state` means.
        foreach (var value in new[]
                 {
                     "$state.panelOpen", "$state.a.b", "$state", "$state.", "$stateful",
                     "$panel", "state.panelOpen", "", "  ", "$",
                 })
        {
            var analyzerIs = Vocabulary.IsStateBinding(value);
            var translatorIs = global::Fuaran.UI.VisualBasic.FuaranXml.IsStateBinding(value);
            Check($"state-spelling pin: IsStateBinding(\"{value}\")",
                analyzerIs == translatorIs,
                $"analyzer: {analyzerIs}, translator: {translatorIs}");

            var analyzerKey = Vocabulary.StateBindingKey(value) ?? "<null>";
            var translatorKey = global::Fuaran.UI.VisualBasic.FuaranXml.StateBindingKey(value) ?? "<null>";
            Check($"state-spelling pin: StateBindingKey(\"{value}\")",
                analyzerKey == translatorKey,
                $"analyzer: {analyzerKey}, translator: {translatorKey}");
        }

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
        //    decides whether FUARAN150 fires — was pinned against nothing.
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
        var references = References.Value;
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

    // Built ONCE, and checked (Phase 1674). Two reasons, and the second is why this
    // is a fix rather than a tidy-up.
    //
    // COST: this set was rebuilt for EVERY snippet — a File.Exists probe over every
    // path on the trusted-platform list plus a MetadataReference.CreateFromFile for
    // each survivor, several hundred file opens per test, times every test in this
    // file.
    //
    // CORRECTNESS: those probes are the flake. A transient File.Exists failure — a
    // sharing violation while another process on the machine touches the shared
    // framework directory, which is the ordinary state of a machine running two
    // gates — silently DROPS an assembly from the reference set, and a VB
    // compilation missing a core reference does not report a clean diagnostic. It
    // throws a NullReferenceException from inside
    // VisualBasicCompilation.GetDiagnostics, which reaches the host as an unhandled
    // exception and exit -532462766, naming nothing. That is the transient failure
    // reported across Phase 1539's gate attempts, and it is invisible to a re-run
    // because the next run probes successfully.
    //
    // Building once narrows the window from every-test to once-per-process; the
    // ASSERTION below closes it, by turning a silently-incomplete set into a named
    // failure. A gate that cannot compile its own fixture must say so.
    private static readonly Lazy<ImmutableArray<MetadataReference>> References =
        new(() =>
        {
            var paths = ReferencePaths().ToArray();

            // The three the VB snippets here cannot compile without: the core
            // library, the reference facade the compiler resolves through, and XML
            // literals. Named rather than counted — a count is a number that drifts
            // with the framework, while an absent System.Private.CoreLib is a fact
            // about this run.
            foreach (var required in new[] { "System.Private.CoreLib", "System.Runtime", "System.Xml.Linq" })
            {
                if (!paths.Any(p => Path.GetFileNameWithoutExtension(p).Equals(required, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException(
                        $"The VB analyzer test harness could not resolve '{required}' from TRUSTED_PLATFORM_ASSEMBLIES "
                        + $"({paths.Length} path(s) found). Every VB compilation below would fail inside Roslyn with a "
                        + "NullReferenceException that names nothing, so the harness refuses here instead. This is "
                        + "usually a transient file-access failure under machine contention: re-run.");
                }
            }

            return paths.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToImmutableArray();
        });

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
