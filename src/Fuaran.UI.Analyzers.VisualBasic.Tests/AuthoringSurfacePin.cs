using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Fuaran.UI.Analyzers.VisualBasic.Tests;

// ============================================================================
//  Phase 2241 — the authoring-surface pin, re-pointed at the GENERATOR.
//
//  Phase 1104 pinned the analyzer's hand-kept attribute table to the IDL at
//  field level, and `StructuralElementPin` did the same for the child-element
//  set. Both tables are generated now (`Vocabulary.Generated.cs`, from
//  `src/Fuaran.UI.Idl/idl.json` + `AuthoringDialect.fs`), and the declarations
//  those pins held — renames, flattenings, sugar, gaps, the slot each child
//  element spells — are generator INPUT, orphan-refusing at generation. What
//  the old pins asserted about the table's agreement with the vocabulary is
//  therefore true by construction plus the regeneration check, and those legs
//  went with the hand mirror they guarded.
//
//  What generation does NOT give, and this pin exists for: the generator
//  derives a wire kind's row from its attribute-eligible fields, so a field
//  added to the vocabulary is ADMITTED by the analyzer the moment the file is
//  regenerated. The hand table made that a decision; generation makes it a
//  default. A default is only safe if the translator can honour it, and the VB
//  translator can set exactly what the C# options record for the kind carries
//  — it builds every kind through the C# factory. So: every derived attribute
//  names a property on `Fuaran.UI.CSharp.<Kind>Options`, or the dialect must
//  declare the field unspelled (`AuthoringDialect.unspelledFields`) so the
//  analyzer refuses it rather than admitting an attribute nothing reads.
// ============================================================================
internal static class AuthoringSurfacePin
{
    /// <summary>Kinds the C# veneer builds through more than one options record,
    /// with why. A derived attribute on one of these must be settable on EVERY
    /// record listed, since the translator picks the record per element.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> VariantOptions = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        // The one wire kind, two factories: `kind` selects MediaKind.Video or .Audio.
        ["Media"] = new[] { "VideoOptions", "AudioOptions" },
    };

    /// <summary>The options record(s) the translator sets for a kind: the declared
    /// variants, else `{Kind}Options` (or its generic `{Kind}Options&lt;TRow&gt;`).</summary>
    private static IReadOnlyList<Type?> OptionsFor(Assembly veneer, string element) =>
        VariantOptions.TryGetValue(element, out var names)
            ? names.Select(n => veneer.GetType($"Fuaran.UI.CSharp.{n}")).ToList()
            : new[] { veneer.GetType($"Fuaran.UI.CSharp.{element}Options") ?? veneer.GetType($"Fuaran.UI.CSharp.{element}Options`1") };

    /// <summary>camelCase wire field to the PascalCase options property.</summary>
    private static string Pascal(string field) => char.ToUpperInvariant(field[0]) + field.Substring(1);

    public static void Run(Action<string, bool, string?> check)
    {
        var derived = global::Fuaran.UI.AuthoringMirrors
            .derivedAttributes(global::Fuaran.UI.Vocabulary.uiIdl)
            .ToList();

        var veneer = typeof(global::Fuaran.UI.CSharp.FuaranNode).Assembly;
        var unreadable = new List<string>();

        foreach (var d in derived)
        {
            var settable = OptionsFor(veneer, d.Element)
                .All(o => o?.GetProperty(Pascal(d.Field), BindingFlags.Public | BindingFlags.Instance) is { CanWrite: true });

            if (!settable)
            {
                unreadable.Add($"{d.Element}.{d.Attribute} (no settable {d.Element}Options.{Pascal(d.Field)})");
            }
        }

        check(
            "authoring-surface pin: every derived attribute is one the C# options record can carry",
            unreadable.Count == 0,
            $"admitted by the generated analyzer vocabulary, settable by nothing: [{string.Join(", ", unreadable.OrderBy(x => x, StringComparer.Ordinal))}]. "
            + "Expose the field on the C# options record and read it in the VB Mapping, or declare it in "
            + "AuthoringDialect.unspelledFields (a GAP) and regenerate — an attribute the analyzer admits and the "
            + "translator cannot set is silently dropped.");

        // And each derived attribute is actually in the generated row, so the
        // check above is about the table the analyzer ships rather than a list
        // only this test computes.
        var unrowed = derived
            .Where(d => !Vocabulary.Attributes.TryGetValue(d.Element, out var row) || !row.Contains(d.Attribute))
            .Select(d => $"{d.Element}.{d.Attribute}")
            .ToList();

        check(
            "authoring-surface pin: every derived attribute is in the shipped attribute table",
            unrowed.Count == 0,
            $"missing from Vocabulary.Attributes: [{string.Join(", ", unrowed)}] — Vocabulary.Generated.cs is stale; regenerate it.");

        // No variant declaration may name a record that does not exist.
        var deadVariants = VariantOptions
            .SelectMany(kv => kv.Value.Where(n => veneer.GetType($"Fuaran.UI.CSharp.{n}") is null).Select(n => $"{kv.Key} -> {n}"))
            .ToList();

        check(
            "authoring-surface pin: every declared variant options record exists",
            deadVariants.Count == 0,
            $"stale: [{string.Join(", ", deadVariants)}]");

        // The vacuity guard: a derivation over an empty or wrongly-shaped
        // vocabulary satisfies both assertions above perfectly.
        check(
            "authoring-surface pin: the derivation actually ran",
            derived.Count >= 100 && derived.Select(d => d.Element).Distinct().Count() >= 30,
            $"derived {derived.Count} attributes over {derived.Select(d => d.Element).Distinct().Count()} kinds — too few to be the real vocabulary");
    }
}
