using System;
using System.IO;
using Fuaran.UI.Ops;
using Fuaran.UI.OpStream.Abstractions;
using static Fuaran.UI.CSharp.Poc.Fui;
using static Fuaran.UI.Types;

namespace Fuaran.UI.CSharp.Poc;

// ============================================================================
//  Wire-identity verification harness (Phase 172).
//
//  For every tree authored in C# (Trees.All):
//    1. encode it through the CANONICAL F# encoder
//       (CanonicalJson.encodeNode) and assert the bytes are IDENTICAL to the
//       language-neutral corpus fixture of the same name; and
//    2. decode the encoding back to a Node<obj> (the canonical F# decoder,
//       JsonDecode.decodeNode) and re-encode it, asserting the round-trip is
//       byte-stable.
//
//  Exit code 0 == every tree is wire-identical AND round-trip-stable; non-zero
//  on any divergence (so Build.fs's Test target gates on it).
// ============================================================================

internal static class Program
{
    private static int Main(string[] args)
    {
        string? corpusDir = LocateCorpus(args);
        if (corpusDir is null)
        {
            Console.Error.WriteLine(
                "Could not locate wire-format-fixtures/nodes. Pass the corpus dir as the first arg, " +
                "or run from inside the workspace tree.");
            return 2;
        }

        Console.WriteLine("Fuaran C# fluent-builder authoring PoC — wire-identity check (Phase 172)");
        Console.WriteLine($"Corpus: {corpusDir}");
        Console.WriteLine(new string('-', 72));

        int passed = 0, failed = 0;

        foreach (var (name, builder) in Trees.All())
        {
            Node<object> tree = builder.Build();

            // Leg 1 — canonical encode, byte-compare against the corpus fixture.
            string encoded = CanonicalJson.encodeNode(tree);
            string fixturePath = Path.Combine(corpusDir, name + ".json");
            string expected = File.ReadAllText(fixturePath);
            bool wireOk = string.Equals(encoded, expected, StringComparison.Ordinal);

            // Leg 2 — decode-and-re-encode round-trip stability. Uses the raw
            // decodeNodeObj path (re-encode boundary, not a live render).
            bool roundTripOk = false;
            string roundTripNote = "";
            var decoded = JsonDecode.decodeNodeObj(encoded);
            if (decoded.IsOk)
            {
                string reEncoded = CanonicalJson.encodeNode(decoded.ResultValue);
                roundTripOk = string.Equals(reEncoded, encoded, StringComparison.Ordinal);
                if (!roundTripOk)
                    roundTripNote = "re-encode differs";
            }
            else
            {
                roundTripNote = "decode failed: " + decoded.ErrorValue.Message;
            }

            bool ok = wireOk && roundTripOk;
            if (ok) passed++; else failed++;

            Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name,-16} wire={(wireOk ? "ok" : "DIFF")} roundtrip={(roundTripOk ? "ok" : "DIFF")}");

            if (!wireOk)
            {
                Console.WriteLine($"        expected: {expected}");
                Console.WriteLine($"        actual:   {encoded}");
                Console.WriteLine($"        {FirstDivergence(expected, encoded)}");
            }
            if (!roundTripOk && roundTripNote.Length > 0)
                Console.WriteLine($"        roundtrip: {roundTripNote}");
        }

        // Negative control — prove the byte-comparison has teeth: a tree that
        // differs by a single field must NOT match the corpus. Keeps a green run
        // from being vacuously green (a comparator that always returns true would
        // "pass" every fixture above).
        string mutated = CanonicalJson.encodeNode(
            Heading("heading-1").Level(2).Text("Channel performance — TYPO").Build());
        string headingFixture = File.ReadAllText(Path.Combine(corpusDir, "heading-1.json"));
        bool negativeControlHeld = !string.Equals(mutated, headingFixture, StringComparison.Ordinal);
        Console.WriteLine(
            $"  [{(negativeControlHeld ? "PASS" : "FAIL")}] negative-control  (a mutated tree must NOT match its fixture)");
        if (!negativeControlHeld) failed++;

        Console.WriteLine(new string('-', 72));
        Console.WriteLine($"{passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>First character index where two strings diverge, with a small
    /// context window — turns a wall-of-JSON diff into a pinpoint.</summary>
    private static string FirstDivergence(string a, string b)
    {
        int i = 0;
        int min = Math.Min(a.Length, b.Length);
        while (i < min && a[i] == b[i]) i++;
        int from = Math.Max(0, i - 20);
        string window(string s) => from < s.Length ? s.Substring(from, Math.Min(40, s.Length - from)) : "<eof>";
        return $"first divergence at index {i}: expected …{window(a)}…  actual …{window(b)}…";
    }

    /// <summary>Find wire-format-fixtures/nodes by walking up from the run
    /// directory and the assembly location (robust to whatever cwd dotnet run
    /// hands us).</summary>
    private static string? LocateCorpus(string[] args)
    {
        if (args.Length > 0 && Directory.Exists(args[0]))
            return args[0];

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "wire-format-fixtures", "nodes");
                if (Directory.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
        }
        return null;
    }
}
