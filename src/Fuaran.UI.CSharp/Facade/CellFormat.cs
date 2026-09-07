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

    /// <summary>A plain number; <paramref name="decimals"/> pins the fraction-digit count.</summary>
    public static CellFormat Number(int? decimals = null) =>
        new(FsGen.CellFormat.NewNumber(Fs.OfNullable(decimals)));

    /// <summary>A currency amount; <paramref name="isoCode"/> is the ISO-4217 code (e.g. "GBP").</summary>
    public static CellFormat Currency(string isoCode) =>
        new(FsGen.CellFormat.NewCurrency(isoCode));

    /// <summary>A percentage; <paramref name="decimals"/> pins the fraction-digit count.</summary>
    public static CellFormat Percent(int? decimals = null) =>
        new(FsGen.CellFormat.NewPercent(Fs.OfNullable(decimals)));

    /// <summary>A value rounded to <paramref name="digits"/> significant digits.</summary>
    public static CellFormat SignificantDigits(int digits) =>
        new(FsGen.CellFormat.NewSignificantDigits(digits));

    /// <summary>A date formatted with the given .NET/Intl-style format string.</summary>
    public static CellFormat Date(string format) =>
        new(FsGen.CellFormat.NewDate(format));

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
