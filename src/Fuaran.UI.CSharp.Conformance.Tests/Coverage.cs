using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.FSharp.Reflection;
// `Fuaran.UI.Types.NodeKind` is a type ABBREVIATION since the 692 swap — erased
// in metadata, so C# reflects the generated declaration directly.
using FsTypes = Fuaran.UI.Generated;

namespace Fuaran.UI.CSharp.Conformance.Tests;

// Phase 305 coverage guard — the §11 forward-coupling reminder for the veneer:
// enumerate every shipped NodeKind/spec case by reflecting the F# DUs and assert a
// public C# factory exists for each. A new F# kind with no C# factory fails here,
// exactly as F#'s FS0025 exhaustiveness would flag a new case.
internal static class Coverage
{
    // The few cases whose C# factory name(s) differ from the F# DU case name. A
    // case maps to the SET of factories that author it, not to one name: `Media`
    // (Phase 1076) is the first kind whose spec carries a VARIANT DU
    // (`MediaKind = Video of VideoDetail | Audio`), and the veneer authors one
    // factory per variant rather than one factory taking a discriminator — video
    // has an autoplay/poster payload audio deliberately has not. Every listed
    // factory is REQUIRED, not any-of: a half-authored variant DU is precisely
    // the gap this pin exists to catch.
    private static readonly IReadOnlyDictionary<string, string[]> Alias = new Dictionary<string, string[]>
    {
        ["GridLayout"] = ["Grid"], // LayoutKind.GridLayout → Fuaran.Grid (DataGrid keeps its name)
        ["Media"] = ["Video", "Audio"], // MediaKind = Video of VideoDetail | Audio
    };

    /// <summary>The C# factory name(s) that author a given wire <c>kind.$type</c>.</summary>
    private static string[] FactoriesFor(string kindType) =>
        Alias.TryGetValue(kindType, out var mapped) ? mapped : [kindType];

    private static readonly HashSet<string> FactoryNames = typeof(Fuaran)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Where(m => typeof(FuaranNode).IsAssignableFrom(m.ReturnType))
        .Select(m => m.Name)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>Whether every public C# factory that authors the given wire <c>kind.$type</c> exists.</summary>
    public static bool HasFactoryForKind(string kindType) =>
        FactoriesFor(kindType).All(FactoryNames.Contains);

    public static void Run(Harness h)
    {
        var factoryNames = FactoryNames;

        // Phase 692 — NodeKind is flat: every kind (and the six structural
        // cases) enumerates directly off the one DU, which is exactly what
        // makes this pin total by reflection rather than by four lists.
        var kindCaseNames = UnionCaseNames(typeof(FsTypes.NodeKind<object>)).ToList();

        foreach (var caseName in kindCaseNames)
        {
            var expected = FactoriesFor(caseName);
            var missing = expected.Where(n => !factoryNames.Contains(n)).ToList();
            h.Check(
                $"coverage: factory {string.Join(" + ", expected.Select(n => "Fuaran." + n))} exists for kind {caseName}",
                missing.Count == 0,
                $"no public static {string.Join(" / ", missing.Select(n => $"Fuaran.{n}(…)"))} returning FuaranNode");
        }
    }

    private static IEnumerable<string> UnionCaseNames(Type duType) =>
        FSharpType.GetUnionCases(duType, null).Select(c => c.Name);
}
