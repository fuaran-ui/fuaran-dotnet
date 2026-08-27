using FsGen = Fuaran.UI.Generated;
using FsJVal = global::Fuaran.Core.JVal;

namespace Fuaran.UI.CSharp;

// Phase 873 — the declarative value records the gap-closure wave (861–867)
// added to spec payloads. They are DATA, not closures, which is exactly why
// they can live on this veneer at all: every other grid and form slot the
// facade cannot reach is defined by a closure the wire has no way to carry.
//
// None of them is pinned by a §11 step-6 gate (the C# `Coverage.cs` reflects
// `NodeKind` cases and the VB analyzer pins the kind vocabulary, so a payload
// FIELD binds neither) — Phase 801 recorded that, and recorded the honest
// consequence that authoring the slots from C#/VB is a follow-up nothing would
// fail without. This file is that follow-up.

/// <summary>
/// A declared INITIAL sort order (Phase 801 for a static table, Phase 861 for a
/// bound grid — the same record and the same wire spelling in both places).
/// Configuration, not data movement: it says which order the table OPENS in,
/// where a transform-pipeline <c>sort</c> fixes one order permanently and
/// leaves no header to click.
/// </summary>
/// <remarks>
/// On a bound grid it applies while the grid's <c>SortStateKey</c> carries
/// nothing; once the user has sorted, the state wins. Declaring it with no
/// <c>SortStateKey</c> at all is legal and means an opening order without
/// interactive re-sorting.
/// </remarks>
public sealed record DefaultSort
{
    /// <summary>The column, as a zero-based index into the columns (or, for a
    /// static table, into <c>TableOptions.Headers</c>).</summary>
    public required int Column { get; init; }

    /// <summary>The direction. Lower-case on the wire: <c>"asc"</c> / <c>"desc"</c>.</summary>
    public SortDirection Direction { get; init; } = SortDirection.Asc;

    internal FsGen.DefaultSort Inner =>
        // Generated DefaultSort ctor is declaration order (Column, Direction).
        new(Column, Direction.ToFs());
}

/// <summary>
/// The cross-field half of a <see cref="FieldRule"/> (Phase 864): compare this
/// field's value against another declared value.
/// </summary>
public sealed record CompareRule
{
    private CompareRule(FsGen.CompareRule inner) => Inner = inner;

    internal FsGen.CompareRule Inner { get; }

    /// <summary>
    /// Compare against the current value of another field in the same form,
    /// named by its field id — the shape the wire spells as a keyless
    /// <c>State</c> binding, and the one every cross-field rule sighted so far
    /// wanted ("end date on or after start date").
    /// </summary>
    public static CompareRule AgainstField(string fieldId, CompareOp op) =>
        // Generated CompareRule ctor is declaration order (Against, Op). The
        // `against` binding carries NO default: a cross-field rule with nothing
        // to compare against yet is unmet, not satisfied by a stand-in value.
        new(new FsGen.CompareRule(
            FsGen.Binding<FsJVal>.NewState(fieldId, Microsoft.FSharp.Core.FSharpOption<FsJVal>.None),
            op.ToFs()));
}

/// <summary>
/// A declared constraint on a form field's value (Phase 864) — the language's
/// answer to restating the rule as help text, which every sighted emission did.
/// A rule is a DECLARATION: the host is obliged to enforce it at submit, and
/// may additionally surface it per keystroke.
/// </summary>
/// <remarks>
/// Every slot is independent and every one is optional; an entirely empty rule
/// is refused by the validator rather than treated as "no constraint". Note the
/// numeric bound is NOT here — a numeric range is
/// <c>FormFieldKind.RangedNumber</c>, which shipped long before this rule
/// vocabulary and is not restated by it.
/// </remarks>
public sealed record FieldRule
{
    /// <summary>A named text shape (email / url / tel). Semantic, not a regex —
    /// reach for <see cref="Pattern"/> for a shape the vocabulary does not name.</summary>
    public TextFormat? Format { get; init; }

    /// <summary>A regular expression the value must match. Host-evaluated, so
    /// keep it bounded — nothing underwrites a third host's regex engine.</summary>
    public string? Pattern { get; init; }

    /// <summary>Minimum length, inclusive.</summary>
    public int? MinLength { get; init; }

    /// <summary>Maximum length, inclusive.</summary>
    public int? MaxLength { get; init; }

    /// <summary>A comparison against another field — see <see cref="CompareRule.AgainstField"/>.</summary>
    public CompareRule? Compare { get; init; }

    /// <summary>The message shown when the rule is unmet. Absent leaves the host
    /// to phrase one, which it can do well for <see cref="Format"/> and badly
    /// for <see cref="Pattern"/> — so a pattern rule should carry one.</summary>
    public Text? Message { get; init; }

    internal FsGen.FieldRule Inner =>
        // Generated FieldRule ctor is declaration order (Compare, Format,
        // MaxLength, Message, MinLength, Pattern) — alphabetical, not the
        // author-facing order above.
        new(
            Compare is { } c ? Fs.Some(c.Inner) : Fs.None<FsGen.CompareRule>(),
            Format is { } f ? Fs.Some(f.ToFs()) : Fs.None<FsGen.TextFormat>(),
            MaxLength is { } max ? Fs.Some(max) : Fs.None<int>(),
            Message is { } m ? Fs.Some(m.Inner) : Fs.None<FsGen.TextSource>(),
            MinLength is { } min ? Fs.Some(min) : Fs.None<int>(),
            Fs.OptStr(Pattern));
}
