using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Fuaran.UI.CSharp.Conformance.Tests;

// Phase 1691 — the C# authoring veneer, pinned against the corpus's enum-token
// table.
//
// The veneer mirrors each bounded F# vocabulary as a C#-native enum and
// translates through the `EnumMap.ToFs` seam, which is right (the IDL-bridge
// rule keeps F# runtime types off the public surface) and leaves one hole:
// every `ToFs` switch ends in a `_ =>` default, so a C# case with no F#
// counterpart maps SILENTLY to whichever case that default names. `Tone.Cyan`
// would author `ToneVariant.Default` and encode as `"Default"`, with nothing
// red anywhere.
//
// This reads `enum-tokens.json` — the corpus artefact, not a local copy of the
// vocabulary — and asserts, for every mirrored enum:
//
//   * the C# case names are exactly the declared host cases, in order; and
//   * `ToFs` maps each C# case to the F# case of the SAME NAME, which is what
//     the `_ =>` default cannot be relied on to do.
//
// The pairing is DERIVED from the `ToFs` overloads rather than declared here,
// because the C# name need not match the F# one (`Tone` mirrors
// `ToneVariant`), and a hand-kept name map would be a second place to forget a
// mirror. The seam itself is the enumeration.
internal static class EnumTokenPin
{
    // The number of ToFs pairs matching a declared enum when this phase shipped (34). A reflective
    // sweep that finds nothing passes every assertion it makes, so the floor is
    // the check that the sweep ran at all. It may only grow.
    private const int PairFloor = 34;

    public static void Run(Harness h)
    {
        if (!Corpus.Available)
        {
            Console.WriteLine("[enum-tokens] wire-format-fixtures corpus absent — skipping the enum-token pin.");
            return;
        }

        var path = Path.Combine(Corpus.Root!, "enum-tokens.json");
        if (!File.Exists(path))
        {
            h.Check(
                "enum-tokens.json is present in the corpus",
                false,
                $"{path} is absent — the corpus manifest points at it. Regenerate with `--emit-corpus`.");
            return;
        }

        var table = ReadTable(path);

        // Every static ToFs whose single parameter is a veneer enum. Internal,
        // so the lookup is NonPublic; declared on EnumMap today, but found by
        // shape rather than by class name.
        var pairs = typeof(SortDirection).Assembly
            .GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(m => m.Name == "ToFs")
            .Select(m => (Method: m, Parameters: m.GetParameters()))
            .Where(x => x.Parameters.Length == 1 && x.Parameters[0].ParameterType.IsEnum)
            .ToList();

        int checkedPairs = 0;

        foreach (var (method, parameters) in pairs)
        {
            var csharpType = parameters[0].ParameterType;
            var fsharpName = method.ReturnType.Name;

            if (!table.TryGetValue(fsharpName, out var declared))
            {
                // Not a bare-string enum of the wire vocabulary (the veneer also
                // mirrors host-only sets). Nothing in the table to pin it to.
                continue;
            }

            var csharpCases = Enum.GetNames(csharpType).ToList();
            var declaredCases = declared.Select(c => c.Case).ToList();

            h.Check(
                $"C# {csharpType.Name} mirrors the declared cases of {fsharpName}",
                csharpCases.SequenceEqual(declaredCases),
                $"declared [{string.Join(", ", declaredCases)}] but the veneer has [{string.Join(", ", csharpCases)}]. "
                + "A veneer case with no declared counterpart is not a compile error: ToFs's `_ =>` default absorbs it "
                + "and authors the wrong wire token.");

            foreach (var value in Enum.GetValues(csharpType))
            {
                var name = value.ToString()!;
                var mapped = method.Invoke(null, new[] { value })!;
                var fsCase = Microsoft.FSharp.Reflection.FSharpValue.GetUnionFields(mapped, mapped.GetType(), null).Item1.Name;

                h.Check(
                    $"C# {csharpType.Name}.{name} maps to {fsharpName}.{name}",
                    fsCase == name,
                    $"ToFs mapped it to {fsharpName}.{fsCase}. This is the `_ =>` default swallowing a case, which "
                    + "silently authors a different wire token than the one the author asked for.");
            }

            checkedPairs++;
        }

        h.Check(
            "the veneer's enum mirrors were actually swept",
            checkedPairs >= PairFloor,
            $"only {checkedPairs} ToFs pairs matched the declared table, against a floor of {PairFloor}. Either the "
            + "translation seam was renamed away from ToFs or the table was not read.");
    }

    private static Dictionary<string, List<(string Case, string Token)>> ReadTable(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var table = new Dictionary<string, List<(string, string)>>(StringComparer.Ordinal);

        foreach (var entry in doc.RootElement.GetProperty("enums").EnumerateArray())
        {
            var cases = entry
                .GetProperty("cases")
                .EnumerateArray()
                .Select(c => (c.GetProperty("case").GetString()!, c.GetProperty("token").GetString()!))
                .ToList();

            table[entry.GetProperty("name").GetString()!] = cases;
        }

        return table;
    }
}
