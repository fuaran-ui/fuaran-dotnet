using System;
using FsTypes = Fuaran.UI.Types;
using FsGen = Fuaran.UI.Generated;

namespace Fuaran.UI.CSharp;

/// <summary>
/// A C#-native binding — the authoring facade over the F# <c>Binding&lt;'T&gt;</c>.
/// A plain value implicitly converts to a static binding, so
/// <c>Source = 1234.5</c> and <c>Href = "https://…"</c> bind with no helper call
/// (the C# edge over the TS union-via-helper shape). Reach for the
/// <see cref="Binding"/> factory for the data-bound cases (<c>Query</c> /
/// <c>State</c> / …).
/// </summary>
/// <typeparam name="T">The bound value type at the author surface.</typeparam>
public sealed class Binding<T>
{
    internal FsGen.Binding<T> Inner { get; }

    internal Binding(FsGen.Binding<T> fs) => Inner = fs;

    /// <summary>A literal, non-bound value (<c>Binding.Static</c>).</summary>
    public static implicit operator Binding<T>(T value) =>
        new(FsGen.Binding<T>.NewStatic(Microsoft.FSharp.Core.FSharpOption<T>.Some(value)));
}

/// <summary>
/// Factory entry points for the data-bound <c>Binding&lt;'T&gt;</c> cases. Mirrors
/// the F# <c>binding.*</c> surface. A static value needs no factory — assign it
/// directly (<see cref="Binding{T}"/>'s implicit conversion).
/// </summary>
public static class Binding
{
    /// <summary>An explicit static binding (rarely needed — a bare value converts implicitly).</summary>
    public static Binding<T> Static<T>(T value) =>
        new(FsGen.Binding<T>.NewStatic(Microsoft.FSharp.Core.FSharpOption<T>.Some(value)));

    /// <summary>
    /// A query binding. The name is what rides the wire; the accessor is a
    /// closure (never serialised) the host runtime uses to project the query
    /// result. The convenience overload supplies an identity projection — sound
    /// because the wire carries only the name.
    /// </summary>
    public static Binding<T> Query<T>(string name) =>
        new(FsGen.Binding<T>.NewQuery(
            name,
            Fs.Func<object, T>(o => (T)o),
            Microsoft.FSharp.Core.FSharpOption<Microsoft.FSharp.Collections.FSharpList<string>>.None));

    /// <summary>A query binding with an explicit typed projection from the query result.</summary>
    public static Binding<T> Query<TResult, T>(string name, Func<TResult, T> accessor) =>
        new(FsGen.Binding<T>.NewQuery(
            name,
            Fs.Func<object, T>(o => accessor((TResult)o)),
            Microsoft.FSharp.Core.FSharpOption<Microsoft.FSharp.Collections.FSharpList<string>>.None));

    /// <summary>A filter-source binding (resolves against the host filter store).</summary>
    public static Binding<T> Filter<T>(string name) =>
        new(FsGen.Binding<T>.NewFilter(name, Microsoft.FSharp.Core.FSharpOption<T>.None));

    /// <summary>A filter-source binding with a declared default — the value the
    /// resolver yields before the filter is first written (0.2.0).</summary>
    public static Binding<T> Filter<T>(string name, T defaultValue) =>
        new(FsGen.Binding<T>.NewFilter(name, Microsoft.FSharp.Core.FSharpOption<T>.Some(defaultValue)));

    /// <summary>A module-state binding keyed by <paramref name="key"/> with a default value.</summary>
    public static Binding<T> State<T>(string key, T defaultValue) =>
        new(FsGen.Binding<T>.NewState(key, Microsoft.FSharp.Core.FSharpOption<T>.Some(defaultValue)));

    /// <summary>
    /// A module-state binding whose slot carries NO default — it resolves to nothing
    /// until the key is first written (the wire's <c>{"$type":"State","key":…}</c> form,
    /// with <c>defaultValue</c> omitted). The F# twin is <c>binding.stateNoDefault</c>.
    /// </summary>
    /// <remarks>
    /// This is a DIFFERENT wire document from <see cref="State{T}(string, T)"/>, not a
    /// convenience over it: a declared default encodes a <c>defaultValue</c> key and an
    /// undeclared one omits it, and the corpus carries both (a defaulted disclosure
    /// <c>open</c>, an undefaulted select <c>value</c>). Either is writable, so both take
    /// the control write-back default when the handler is omitted.
    /// </remarks>
    public static Binding<T> State<T>(string key) =>
        new(FsGen.Binding<T>.NewState(key, Microsoft.FSharp.Core.FSharpOption<T>.None));

    /// <summary>A selection binding reading the current selection of node <paramref name="nodeId"/>.</summary>
    public static Binding<T> Selection<TRow, T>(string nodeId, Func<TRow, T> accessor) =>
        new(FsGen.Binding<T>.NewSelection(
            nodeId,
            Fs.Func<object, T>(o => accessor((TRow)o)),
            Microsoft.FSharp.Core.FSharpOption<T>.None,
            Microsoft.FSharp.Core.FSharpOption<string>.None));

    /// <summary>A selection binding with a declared default — the value the
    /// resolver yields until the user first selects a row (0.2.9, the
    /// Filter-default convention).</summary>
    public static Binding<T> Selection<TRow, T>(string nodeId, Func<TRow, T> accessor, T defaultValue) =>
        new(FsGen.Binding<T>.NewSelection(
            nodeId,
            Fs.Func<object, T>(o => accessor((TRow)o)),
            Microsoft.FSharp.Core.FSharpOption<T>.Some(defaultValue),
            Microsoft.FSharp.Core.FSharpOption<string>.None));

    /// <summary>A declarative row-field selection binding (0.2.10, Phase 632):
    /// projects <paramref name="field"/> off the clicked row — the
    /// wire-expressible twin of a typed accessor. <paramref name="defaultValue"/>
    /// (the projected scalar) yields until the user first selects a row.</summary>
    public static Binding<T> SelectionField<T>(string nodeId, string field, T defaultValue) =>
        new(FsGen.Binding<T>.NewSelection(
            nodeId,
            Fs.Func<object, T>(o => FsTypes.Binding.projectSelectionField<T>(field, o)),
            Microsoft.FSharp.Core.FSharpOption<T>.Some(defaultValue),
            Microsoft.FSharp.Core.FSharpOption<string>.Some(field)));

    /// <summary>
    /// A locale-aware formatted string (Phase 102). Projects a numeric
    /// <paramref name="source"/> to a localised display <see cref="string"/> via the
    /// bounded <see cref="LocaleFormat"/> intent + <see cref="Locale"/> selector — drop
    /// it into any string slot. Distinct from <see cref="CellFormat"/> (grid/metric display).
    /// </summary>
    public static Binding<string> Format(Binding<double> source, LocaleFormat format, Locale? locale = null) =>
        new(FsGen.Binding<string>.NewFormat(source.Inner, format.Inner, (locale ?? Locale.Ambient).Inner));
}

/// <summary>Date-presentation breadth for <see cref="LocaleFormat.Date"/> — maps to the F# <c>DateStyle</c>.</summary>
public enum DateStyle
{
    Short,
    Medium,
    Long,
    Full,
}

/// <summary>Relative-time grain for <see cref="LocaleFormat.RelativeTime"/> — maps to the F# <c>RelativeTimeUnit</c>.</summary>
public enum RelativeTimeUnit
{
    Second,
    Minute,
    Hour,
    Day,
    Week,
    Month,
    Year,
}

/// <summary>
/// The bounded, semantic locale-aware formatting intent carried by
/// <see cref="Binding.Format"/> — the authoring facade over the F# <c>Format</c> DU.
/// No raw <c>Intl</c> option-bag escape (FGP 1).
/// </summary>
public readonly struct LocaleFormat
{
    internal FsGen.Format Inner { get; }

    private LocaleFormat(FsGen.Format inner) => Inner = inner;

    /// <summary>A plain localised number; <paramref name="decimals"/> pins the fraction-digit count.</summary>
    public static LocaleFormat Number(int? decimals = null) =>
        new(FsGen.Format.NewNumber(Fs.OfNullable(decimals)));

    /// <summary>A currency amount; <paramref name="isoCode"/> is the ISO-4217 code (e.g. "GBP").</summary>
    public static LocaleFormat Currency(string isoCode) =>
        new(FsGen.Format.NewCurrency(isoCode));

    /// <summary>A percentage of a ratio source (<c>0.42</c> → "42%").</summary>
    public static LocaleFormat Percent(int? decimals = null) =>
        new(FsGen.Format.NewPercent(Fs.OfNullable(decimals)));

    /// <summary>An absolute date/time (source read as whole Unix-epoch seconds).</summary>
    public static LocaleFormat Date(DateStyle style) =>
        new(FsGen.Format.NewDate(style.ToFs()));

    /// <summary>A relative time ("3 days ago"), source read as a signed count of <paramref name="unit"/>.</summary>
    public static LocaleFormat RelativeTime(RelativeTimeUnit unit) =>
        new(FsGen.Format.NewRelativeTime(unit.ToFs()));
}

/// <summary>The locale selector for <see cref="Binding.Format"/> — the facade over the F# <c>LocaleSource</c>.</summary>
public readonly struct Locale
{
    internal FsGen.LocaleSource Inner { get; }

    private Locale(FsGen.LocaleSource inner) => Inner = inner;

    /// <summary>Defer to the host-supplied ambient locale (the default).</summary>
    public static Locale Ambient { get; } = new(FsGen.LocaleSource.Ambient);

    /// <summary>Pin an explicit BCP-47 locale tag (e.g. "en-GB").</summary>
    public static Locale Explicit(string tag) => new(FsGen.LocaleSource.NewExplicit(tag));
}
