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
// every `ToFs` switch ended in a `_ =>` default, so a C# case with no F#
// counterpart mapped SILENTLY to whichever case that default named. `Tone.Cyan`
// would have authored `ToneVariant.Default` and encoded as `"Default"`, with
// nothing red anywhere (Phase 1930 made that arm throw).
//
// This reads `enum-tokens.json` — the corpus artefact, not a local copy of the
// vocabulary — and asserts, for every mirrored enum, that the C# case names are
// exactly the declared host cases, in order.
//
// Phase 2241 — the second leg went with the mirror it guarded. It asserted that
// `ToFs` maps each C# case to the F# case of the SAME NAME, because the seam was
// hand-kept. The seam is generated now (`Facade/EnumMap.Generated.cs`), one arm
// per vocabulary case mapping a name to the same name, and a vocabulary case the
// C# declaration lacks fails the compile there. What generation cannot catch is
// the other direction — a C# case the vocabulary does NOT declare reaches the
// `_ =>` arm, which since Phase 1930 throws rather than authoring a fallback case
// — and the case-list leg below asserts it at build time instead of at run time.
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
        RunFailsClosed(h);

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
                + "A veneer case with no declared counterpart is not a compile error: it reaches the generated ToFs "
                + "seam's `_ =>` arm and throws at authoring time.");

            checkedPairs++;
        }

        h.Check(
            "the veneer's enum mirrors were actually swept",
            checkedPairs >= PairFloor,
            $"only {checkedPairs} ToFs pairs matched the declared table, against a floor of {PairFloor}. Either the "
            + "translation seam was renamed away from ToFs or the table was not read.");
    }

    // ── Phase 1930 — the seam fails closed. ──────────────────────────────────
    //
    // Every generated `ToFs` switch used to end in `_ => <a fallback case>`, so a
    // value outside the declared cases authored a case nobody wrote. On
    // `EmbedPermission` that case was `AllowScripts`: `(EmbedPermission)99`
    // granted script to an embedded frame. The generator now ends each switch in
    // a throw. Two legs keep it that way: a source-reading pin (no `ToFs` body may
    // carry a `_ =>` arm that returns a value; the seam is generated, so this pins
    // the generator's output, in the shape of the analyzer suite's
    // authoring-surface pin) and a behavioural sweep that hands every `ToFs` an
    // undeclared value and expects a refusal. Neither needs the corpus.

    private const string CatchAllArm = "_ =>";

    /// <summary>The `ToFs` bodies in <paramref name="source"/> whose `_ =>` arm does
    /// not throw, each named by its signature line. Pure, so the negative control
    /// can prove it goes red.</summary>
    internal static List<string> CatchAllViolations(string source, out int bodies)
    {
        var violations = new List<string>();
        bodies = 0;
        string? current = null;

        foreach (var raw in source.Split('\n'))
        {
            var line = raw.Trim();

            if (line.Contains(" ToFs(this ", StringComparison.Ordinal))
            {
                current = line;
                bodies++;
            }
            else if (current is not null && line.StartsWith(CatchAllArm, StringComparison.Ordinal))
            {
                if (!line.Substring(CatchAllArm.Length).TrimStart().StartsWith("throw ", StringComparison.Ordinal))
                {
                    violations.Add(current);
                }
            }
            else if (current is not null && line.StartsWith("};", StringComparison.Ordinal))
            {
                current = null;
            }
        }

        return violations;
    }

    private static string? VeneerSourceRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Fuaran.UI.CSharp");
            if (File.Exists(Path.Combine(candidate, "Facade", "EnumMap.Generated.cs")))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void RunFailsClosed(Harness h)
    {
        // Negative control first: the scanner must be able to go red.
        var planted = string.Join(
            "\n",
            "internal static FsGen.X ToFs(this X v) =>",
            "    v switch",
            "    {",
            "        X.A => FsGen.X.A,",
            "        _ => FsGen.X.A,",
            "    };");
        h.Check(
            "fail-closed pin: the scanner reports a planted value-returning catch-all arm",
            CatchAllViolations(planted, out _).Count == 1);

        var root = VeneerSourceRoot();
        if (root is null)
        {
            h.Check(
                "fail-closed pin: the veneer's source tree is reachable",
                false,
                $"no src/Fuaran.UI.CSharp/Facade/EnumMap.Generated.cs above {AppContext.BaseDirectory}.");
        }
        else
        {
            var bodies = 0;
            var violations = new List<string>();
            var sep = Path.DirectorySeparatorChar;

            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{sep}obj{sep}", StringComparison.Ordinal)
                    || file.Contains($"{sep}bin{sep}", StringComparison.Ordinal))
                {
                    continue;
                }

                violations.AddRange(
                    CatchAllViolations(File.ReadAllText(file), out var n).Select(v => $"{Path.GetFileName(file)}: {v}"));
                bodies += n;
            }

            h.Check(
                "fail-closed pin: no ToFs map carries a catch-all arm that returns a case",
                violations.Count == 0,
                $"[{string.Join("; ", violations)}]: a value outside the declared cases would author a case nobody "
                + "wrote. The seam is generated: fix src/Fuaran.UI.Idl/AuthoringMirrors.fs and regenerate.");
            h.Check(
                "fail-closed pin: the ToFs bodies were actually read",
                bodies >= PairFloor,
                $"read {bodies} ToFs bodies against a floor of {PairFloor}.");
        }

        // Behavioural: every ToFs refuses an undeclared value and maps every declared one.
        var maps = typeof(SortDirection).Assembly
            .GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(m => m.Name == "ToFs")
            .Select(m => (Method: m, Parameters: m.GetParameters()))
            .Where(x => x.Parameters.Length == 1 && x.Parameters[0].ParameterType.IsEnum)
            .ToList();

        var leaks = new List<string>();
        var unmapped = new List<string>();

        foreach (var (method, parameters) in maps)
        {
            var type = parameters[0].ParameterType;

            try
            {
                var result = method.Invoke(null, new[] { Enum.ToObject(type, 99) });
                leaks.Add($"{type.Name}(99) -> {result}");
            }
            catch (TargetInvocationException e) when (e.InnerException is ArgumentOutOfRangeException)
            {
                // The refusal this leg asserts.
            }

            foreach (var value in Enum.GetValues(type))
            {
                try
                {
                    method.Invoke(null, new[] { value });
                }
                catch (TargetInvocationException e)
                {
                    unmapped.Add($"{type.Name}.{value}: {e.InnerException?.GetType().Name}");
                }
            }
        }

        h.Check(
            "fail-closed: every ToFs refuses a cast-out-of-range value",
            maps.Count >= PairFloor && leaks.Count == 0,
            $"swept {maps.Count} maps; these authored a case for an undeclared value: [{string.Join(", ", leaks)}]");
        h.Check(
            "fail-closed: every declared C# case still maps",
            unmapped.Count == 0,
            $"[{string.Join(", ", unmapped)}]");

        // The embed sandbox, end to end through the public factory.
        static FuaranNode Embed(params EmbedPermission[] permissions) =>
            Fuaran.Embed(new EmbedOptions
            {
                Id = "frame",
                Src = "https://example.com/widget",
                Title = "Widget",
                Permissions = permissions,
            });

        static bool Refuses(params EmbedPermission[] permissions)
        {
            try
            {
                Embed(permissions);
                return false;
            }
            catch (ArgumentOutOfRangeException)
            {
                return true;
            }
        }

        h.Check(
            "fail-closed: (EmbedPermission)99 refuses rather than granting AllowScripts",
            Refuses((EmbedPermission)99));
        h.Check(
            "fail-closed: (EmbedPermission)(-1) refuses",
            Refuses((EmbedPermission)(-1)));
        h.Check(
            "fail-closed: every declared EmbedPermission name still maps",
            !Refuses(Enum.GetValues<EmbedPermission>()));

        var forms = Embed(EmbedPermission.AllowForms).Encode();
        h.Check(
            "fail-closed: a declared AllowForms authors AllowForms and no script grant",
            forms.Contains("AllowForms", StringComparison.Ordinal)
            && !forms.Contains("AllowScripts", StringComparison.Ordinal),
            forms);
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
