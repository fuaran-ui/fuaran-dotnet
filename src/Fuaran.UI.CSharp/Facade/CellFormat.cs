using FsGen = Fuaran.UI.Generated;

namespace Fuaran.UI.CSharp;

/// <summary>
/// A typed cell / metric display format — the authoring facade over the F#
/// <c>CellFormat</c>. Bounded by design (FGP 1): no raw format-string escape on
/// the typed surface. Distinct from the locale-aware <c>Binding.Format</c> family
/// (added in Phase 305).
/// </summary>
public readonly struct CellFormat
{
    internal FsGen.CellFormat Inner { get; }

    private CellFormat(FsGen.CellFormat fs) => Inner = fs;

    /// <summary>No formatting — the value renders as-is.</summary>
    public static CellFormat None { get; } = new(FsGen.CellFormat.None);

    /// <summary>
    /// A plain number; <paramref name="decimals"/> pins the fraction-digit count. A
    /// <paramref name="locale"/> renders it through the locale formatter (Phase 2490).
    /// </summary>
    public static CellFormat Number(int? decimals = null, Locale? locale = null) =>
        new(FsGen.CellFormat.NewNumber(Fs.OfNullable(decimals), LocaleOf(locale)));

    /// <summary>
    /// A currency amount; <paramref name="isoCode"/> is the ISO-4217 code (e.g. "GBP"). A
    /// <paramref name="locale"/> renders it grouped and symbol-led (Phase 2490).
    /// </summary>
    public static CellFormat Currency(string isoCode, Locale? locale = null) =>
        new(FsGen.CellFormat.NewCurrency(isoCode, LocaleOf(locale)));

    /// <summary>
    /// A percentage; <paramref name="decimals"/> pins the fraction-digit count. A
    /// <paramref name="locale"/> renders it through the locale formatter (Phase 2490).
    /// </summary>
    public static CellFormat Percent(int? decimals = null, Locale? locale = null) =>
        new(FsGen.CellFormat.NewPercent(Fs.OfNullable(decimals), LocaleOf(locale)));

    private static Microsoft.FSharp.Core.FSharpOption<FsGen.LocaleSource> LocaleOf(Locale? locale) =>
        locale is { } l ? Fs.Some(l.Inner) : Fs.None<FsGen.LocaleSource>();

    /// <summary>A value rounded to <paramref name="digits"/> significant digits.</summary>
    public static CellFormat SignificantDigits(int digits) =>
        new(FsGen.CellFormat.NewSignificantDigits(digits));

    /// <summary>A date formatted with the given .NET/Intl-style format string.</summary>
    public static CellFormat DateTime(string format) =>
        new(FsGen.CellFormat.NewDateTime(format));

    /// <summary>
    /// A trendable DURATION cell (Phase 819) — the raw value counts
    /// <paramref name="unit"/>s, rendered per <paramref name="style"/>.
    /// </summary>
    public static CellFormat Duration(DurationUnit unit, DurationStyle style) =>
        new(FsGen.CellFormat.NewDuration(unit.ToFs(), style.ToFs()));

    /// <summary>
    /// A relative time (Phase 819) — the raw value is a signed count of
    /// <paramref name="unit"/>s. The cell-vocabulary twin of
    /// <see cref="LocaleFormat.RelativeTime"/>.
    /// </summary>
    public static CellFormat RelativeTime(RelativeTimeUnit unit) =>
        new(FsGen.CellFormat.NewRelativeTime(unit.ToFs()));
}
