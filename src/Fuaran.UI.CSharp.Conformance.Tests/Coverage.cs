using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.FSharp.Reflection;
using FsTypes = Fuaran.UI.Types;

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

        var kindCaseNames =
            UnionCaseNames(typeof(FsTypes.LayoutKind<object>))
                .Concat(UnionCaseNames(typeof(FsTypes.DisplayKind<object>)))
                .Concat(UnionCaseNames(typeof(FsTypes.InputKind<object>)))
                .Concat(UnionCaseNames(typeof(FsTypes.VisKind<object>)))
                // The structural NodeKind cases that aren't behind a category DU.
                .Concat(new[] { "Custom", "ErrorBoundary", "Switch", "FragmentDecl", "FragmentRef", "Mount" })
                .ToList();

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
