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
    // The few cases whose C# factory name differs from the F# DU case name.
    private static readonly IReadOnlyDictionary<string, string> Alias = new Dictionary<string, string>
    {
        ["GridLayout"] = "Grid", // LayoutKind.GridLayout → Fuaran.Grid (DataGrid keeps its name)
    };

    private static readonly HashSet<string> FactoryNames = typeof(Fuaran)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Where(m => typeof(FuaranNode).IsAssignableFrom(m.ReturnType))
        .Select(m => m.Name)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>Whether a public C# factory exists for the given wire <c>kind.$type</c> (applying the alias map).</summary>
    public static bool HasFactoryForKind(string kindType) =>
        FactoryNames.Contains(Alias.TryGetValue(kindType, out var mapped) ? mapped : kindType);

    public static void Run(Harness h)
    {
        var factoryNames = FactoryNames;

        // Phase 692 — NodeKind is flat: every kind (and the six structural
        // cases) enumerates directly off the one DU, which is exactly what
        // makes this pin total by reflection rather than by four lists.
        var kindCaseNames = UnionCaseNames(typeof(FsTypes.NodeKind<object>)).ToList();

        foreach (var caseName in kindCaseNames)
        {
            var expected = Alias.TryGetValue(caseName, out var mapped) ? mapped : caseName;
            h.Check(
                $"coverage: factory Fuaran.{expected} exists for kind {caseName}",
                factoryNames.Contains(expected),
                $"no public static Fuaran.{expected}(…) returning FuaranNode");
        }
    }

    private static IEnumerable<string> UnionCaseNames(Type duType) =>
        FSharpType.GetUnionCases(duType, null).Select(c => c.Name);
}
