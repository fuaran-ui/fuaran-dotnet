using System;

namespace Fuaran.UI.Analyzers.VisualBasic;

// Phase 315 — the VB XML-literal vocabulary the analyzer checks against. A
// netstandard2.0 analyzer cannot load the net10 veneer or read the vocabulary at run
// time, so the element and attribute tables are embedded as source.
//
// Phase 2241 — and that source is GENERATED. `Kinds`, `Structural` and `Attributes`
// live in Vocabulary.Generated.cs, emitted from src/Fuaran.UI.Idl/idl.json plus the
// dialect's declared divergences (src/Fuaran.UI.Idl/AuthoringDialect.fs) and checked by
// the same regeneration test as the generated structural layer
// (`FUARAN_REGEN=1 dotnet run --project src/Fuaran.UI.Idl.Tests`). This half keeps the
// BEHAVIOUR. `Kinds` is still pinned to the translator's registry by the analyzer
// suite (`Kinds == FuaranXml.KnownElements()`), so a kind the translator cannot build
// fails CI rather than silently disagreeing.
internal static partial class Vocabulary
{
    public static bool IsKnownElement(string name) => Kinds.Contains(name) || Structural.Contains(name);

    // Phase 1154 — the dialect's two BINDING spellings. `"$name"` is a host-fed query
    // (read-only); `"$state.<key>"` is a writable state slot, which is what makes a
    // handler-free control live under the write-back default. Both are `$`-prefixed, so
    // the analyzer must discriminate them for the same reason the translator must: the
    // FUARAN010 query-resolution check would otherwise report every state binding as an
    // unresolved query name.
    //
    // This is the analyzer's copy of `FuaranXml.IsStateBinding` / `StateBindingKey`
    // (a netstandard2.0 analyzer cannot load the net10 veneer), pinned to it by a test
    // exactly as `Kinds` is pinned to `KnownElements`.

    /// <summary>The value prefix that spells a writable state binding.</summary>
    public const string StateBindingPrefix = "$state";

    /// <summary>Whether the value uses the state spelling at all — true for a well-formed
    /// <c>"$state.&lt;key&gt;"</c> AND for a malformed <c>"$state"</c> / <c>"$state."</c>.
    /// A query whose name merely starts with the same letters (<c>"$stateful"</c>) is not
    /// the state spelling and keeps its meaning.</summary>
    public static bool IsStateBinding(string? value) =>
        value is not null
        && (string.Equals(value, StateBindingPrefix, StringComparison.Ordinal)
            || value.StartsWith(StateBindingPrefix + ".", StringComparison.Ordinal));

    /// <summary>The state key inside a <c>"$state.&lt;key&gt;"</c> value; null when the value
    /// is not the state spelling, and null when it is a malformed one — so a caller that has
    /// already established <see cref="IsStateBinding"/> reads null as "malformed".</summary>
    public static string? StateBindingKey(string? value) =>
        !IsStateBinding(value) || value!.Length <= StateBindingPrefix.Length + 1
            ? null
            : value.Substring(StateBindingPrefix.Length + 1);

    /// <summary>The nearest known element to a misspelling (case-insensitive exact, else min edit distance ≤ 3).</summary>
    public static string? Nearest(string name)
    {
        foreach (var k in Kinds)
        {
            if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase))
            {
                return k;
            }
        }

        string? best = null;
        int bestDist = int.MaxValue;
        foreach (var k in Kinds)
        {
            int dist = EditDistance(name.ToLowerInvariant(), k.ToLowerInvariant());
            if (dist < bestDist)
            {
                bestDist = dist;
                best = k;
            }
        }

        return bestDist <= 3 ? best : null;
    }

    private static int EditDistance(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }

            Array.Copy(curr, prev, b.Length + 1);
        }

        return prev[b.Length];
    }
}
