using System;
using System.Collections.Generic;
using System.Linq;
using FsFactory = global::Fuaran.UI.Fuaran;
using FsColumn = global::Fuaran.UI.Column;
using FsTypes = Fuaran.UI.Types;
using FsGen = Fuaran.UI.Generated;
using FsAction = Fuaran.UI.Generated.Action<object>;
// fuaran#665 — the typed row: the wire-expressible name→value map the rows
// slot carries (`Fuaran.Core.Row` = FSharpMap<string, obj> on the F# side).
using FsRow = Microsoft.FSharp.Collections.FSharpMap<string, object>;

namespace Fuaran.UI.CSharp;

// Phase 305 — the Visualisation kinds. DataGrid is generic over the row type;
// fuaran#665: its REQUIRED `ToRow` projects each row to the wire-expressible
// name→value map, and column accessors read that projected row by name (the
// same shape the F# facade takes — `'row` survives only at the source seam).
public static partial class Fuaran
{
    /// <summary>A chart. <c>Source</c> binds a typed row sequence (name→value maps);
    /// <c>XField</c>/<c>YFields</c> name the plotted keys.</summary>
    public static FuaranNode Chart(ChartOptions options) =>
        new(FsFactory.chart<object>(
            options.Id,
            // Generated ChartSpec ctor is Generated.fs declaration order (Kind,
            // Source, Stacked, XField, YFields, Title, ValueFormat,
            // OnPointClick), not the old Source-first hand order.
            new FsGen.ChartSpec<object>(
                options.Kind.ToFs(),
                (options.Source ?? Binding.Static(Enumerable.Empty<FsRow>())).Inner,
                options.Stacked,
                options.XField,
                Fs.List(options.YFields ?? Enumerable.Empty<string>()),
                options.Title is { } t ? Fs.Some(t.Inner) : Fs.None<FsGen.TextSource>(),
                options.ValueFormat is { } vf ? Fs.Some(vf.Inner) : Fs.None<FsGen.Format>(),
                options.XTitle is { } xt ? Fs.Some(xt.Inner) : Fs.None<FsGen.TextSource>(),
                options.YTitle is { } yt ? Fs.Some(yt.Inner) : Fs.None<FsGen.TextSource>(),
                options.Subtitle is { } st ? Fs.Some(st.Inner) : Fs.None<FsGen.TextSource>(),
                Fs.None<Microsoft.FSharp.Core.FSharpFunc<FsRow, FsAction>>())));

    /// <summary>A static (non-data-bound) HTML table.</summary>
    public static FuaranNode Table(TableOptions options) =>
        new(FsFactory.table<object>(
            options.Id,
            new FsTypes.TableSpec<object>(
                Fs.List((options.Headers ?? Enumerable.Empty<Text>()).Select(t => t.Inner)),
                Fs.List((options.Rows ?? Enumerable.Empty<IEnumerable<Text>>())
                    .Select(r => Fs.List(r.Select(t => t.Inner)))),
                Fs.None<Microsoft.FSharp.Core.FSharpFunc<int, FsAction>>(),
                // Phase 801 — the declarative sort-intent slots. The veneer does not
                // expose them yet: the §11 step-6 gates pin KINDS (Coverage.cs reflects
                // NodeKind cases; the VB analyzer pins Vocabulary.Kinds), so a payload-
                // field addition binds neither veneer. Authoring them from C#/VB is a
                // follow-up, not a gate failure.
                Fs.None<bool>(),
                Fs.None<FsGen.DefaultSort>())));

    /// <summary>A marker map.</summary>
    public static FuaranNode Map(MapOptions options) =>
        new(FsFactory.map<object>(
            options.Id,
            // Generated MapSpec ctor is Generated.fs declaration order (CentreLatitude,
            // CentreLongitude, Source, Zoom, OnMarkerClick), not the old Source-first
            // hand order; Source now binds an F# `MapMarker list`.
            new FsGen.MapSpec<object>(
                options.CentreLatitude,
                options.CentreLongitude,
                global::Fuaran.UI.Generated.Binding<Microsoft.FSharp.Collections.FSharpList<FsGen.MapMarker>>.NewStatic(
                    Fs.Some(
                        Fs.List(
                            (options.Markers ?? Enumerable.Empty<(double, double, string)>())
                                // Generated MapMarker declares (Label, Latitude, Longitude); Label is a bare string.
                                .Select(m => new FsGen.MapMarker(m.Item3, m.Item1, m.Item2))))),
                options.Zoom,
                Fs.None<Microsoft.FSharp.Core.FSharpFunc<FsGen.MapMarker, FsAction>>())));

    /// <summary>A data-bound grid over rows of type <typeparamref name="TRow"/>.
    /// The REQUIRED <c>ToRow</c> (fuaran#665) projects each row to the
    /// wire-expressible name→value map, so a static/state rows payload survives
    /// canonical encoding instead of collapsing to <c>"&lt;opaque&gt;"</c>.</summary>
    public static FuaranNode DataGrid<TRow>(DataGridOptions<TRow> options) =>
        new(FsFactory.grid<TRow, object>(
            options.Id,
            Fs.Func<TRow, FsRow>(r => Fs.Map(options.ToRow(r))),
            new FsTypes.GridSpecOf<TRow, object>(
                (options.Source ?? Binding.Static(Enumerable.Empty<TRow>())).Inner,
                Fs.Func<FsRow, string>(row => (options.RowKey ?? (_ => ""))(row)),
                Fs.List((options.Columns ?? Enumerable.Empty<Column>()).Select(c => c.Inner)),
                Fs.None<Microsoft.FSharp.Core.FSharpFunc<FsRow, FsAction>>(),
                options.Editable)));
}

/// <summary>A data-grid column. Accessors read the PROJECTED row (fuaran#665 —
/// the name→value map <c>ToRow</c> produced), so a column is row-type-free
/// exactly as on the F# author surface.</summary>
public sealed class Column
{
    internal FsTypes.Column<object> Inner { get; }

    private Column(FsTypes.Column<object> inner) => Inner = inner;

    /// <summary>A text column reading <paramref name="value"/> from each projected row.</summary>
    public static Column Text(string label, Func<IReadOnlyDictionary<string, object>, string> value) =>
        new(FsColumn.text<object>(label, Fs.Func<FsRow, string>(row => value(row))));

    /// <summary>A numeric column reading <paramref name="value"/> from each projected row.</summary>
    public static Column Numeric(string label, Func<IReadOnlyDictionary<string, object>, double> value) =>
        new(FsColumn.numeric<object>(label, Fs.Func<FsRow, double>(row => value(row))));

    /// <summary>A boolean column reading <paramref name="value"/> from each projected row.</summary>
    public static Column Bool(string label, Func<IReadOnlyDictionary<string, object>, bool> value) =>
        new(FsColumn.@bool<object>(label, Fs.Func<FsRow, bool>(row => value(row))));

    /// <summary>A date column reading <paramref name="value"/> from each projected row.</summary>
    public static Column Date(string label, Func<IReadOnlyDictionary<string, object>, DateTimeOffset> value) =>
        new(FsColumn.date<object>(label, Fs.Func<FsRow, DateTimeOffset>(row => value(row))));

    /// <summary>
    /// Render this column's value as a tone-bearing pill whose tone comes from a declared
    /// value&#8594;tone map (Phase 750). <paramref name="field"/> is the ROW PROPERTY name that
    /// supplies both the pill's text and the map key; <paramref name="defaultTone"/> tones a value
    /// the map does not mention.
    /// </summary>
    /// <remarks>
    /// This is the only interactive-looking cell kind the fluent facade can offer, and that is not
    /// an accident of scope: every other one (<c>Editable</c>, <c>Checkbox</c>, <c>Button</c>,
    /// <c>Link</c>, <c>Pill</c>, <c>Progress</c>) is defined by a closure over the row, which the
    /// facade has no way to model and the wire has no way to carry. A declared mapping has neither
    /// problem, so it is expressible here and it survives serialisation intact.
    /// </remarks>
    public Column WithTonedPill(
        string field,
        IEnumerable<KeyValuePair<string, Tone>> toneMap,
        Tone defaultTone = Tone.Default) =>
        new(FsColumn.withTonedPill<object>(
            field,
            Fs.Map(toneMap.Select(kv => new KeyValuePair<string, FsGen.ToneVariant>(kv.Key, kv.Value.ToFs()))),
            defaultTone.ToFs(),
            Inner));
}

/// <summary>Options for <see cref="Fuaran.Chart"/>.</summary>
public sealed record ChartOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The bound row source — a sequence of name→value maps (fuaran#665).</summary>
    public Binding<IEnumerable<FsRow>>? Source { get; init; }

    /// <summary>The chart kind (default line).</summary>
    public ChartKind Kind { get; init; } = ChartKind.Line;

    /// <summary>The x-axis field key.</summary>
    public string XField { get; init; } = "";

    /// <summary>The y-axis field keys.</summary>
    public IEnumerable<string>? YFields { get; init; }

    /// <summary>An optional chart title.</summary>
    public Text? Title { get; init; }

    /// <summary>
    /// The value axis's number format (Phase 876) — reuses the same bounded
    /// <see cref="LocaleFormat"/> vocabulary <see cref="Binding.Format"/> takes.
    /// Absent leaves the lowering's canonical default rendering (thousands
    /// separators + decimals derived from the tick step).
    /// </summary>
    public LocaleFormat? ValueFormat { get; init; }

    /// <summary>
    /// The x-axis title (Phase 878). Absent falls back to the capitalised
    /// <see cref="XField"/> name — the axis is never left nameless.
    /// </summary>
    public Text? XTitle { get; init; }

    /// <summary>
    /// The y-axis title (Phase 878), rendered rotated alongside the axis.
    /// Absent falls back to the capitalised first <see cref="YFields"/> entry.
    /// </summary>
    public Text? YTitle { get; init; }

    /// <summary>
    /// A muted subtitle under the chart title (Phase 878) — the natural home
    /// for a units statement. Declaring one suppresses the lowering's own
    /// display-unit label, so the units are stated once.
    /// </summary>
    public Text? Subtitle { get; init; }

    /// <summary>Whether bar/area series stack.</summary>
    public bool Stacked { get; init; }
}

/// <summary>Options for <see cref="Fuaran.Table"/>.</summary>
public sealed record TableOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The header cells.</summary>
    public IEnumerable<Text>? Headers { get; init; }

    /// <summary>The body rows (each a sequence of cells).</summary>
    public IEnumerable<IEnumerable<Text>>? Rows { get; init; }
}

/// <summary>Options for <see cref="Fuaran.Map"/>.</summary>
public sealed record MapOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The markers as (latitude, longitude, label) triples.</summary>
    public IEnumerable<(double Latitude, double Longitude, string Label)>? Markers { get; init; }

    /// <summary>The map centre latitude.</summary>
    public double CentreLatitude { get; init; }

    /// <summary>The map centre longitude.</summary>
    public double CentreLongitude { get; init; }

    /// <summary>The zoom level (default 4).</summary>
    public int Zoom { get; init; } = 4;
}

/// <summary>Options for <see cref="Fuaran.DataGrid{TRow}"/>.</summary>
/// <typeparam name="TRow">The row type.</typeparam>
public sealed record DataGridOptions<TRow>
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>REQUIRED (fuaran#665): projects each row to the wire-expressible
    /// name→value cells. Deliberately not defaultable — the only candidate
    /// defaults silently produce empty rows, the exact loss the typed rows slot
    /// exists to end. A source whose rows are already name→value pairs passes
    /// them through.</summary>
    public required Func<TRow, IEnumerable<KeyValuePair<string, object>>> ToRow { get; init; }

    /// <summary>The bound row source.</summary>
    public Binding<IEnumerable<TRow>>? Source { get; init; }

    /// <summary>A stable per-row key selector over the PROJECTED row.</summary>
    public Func<IReadOnlyDictionary<string, object>, string>? RowKey { get; init; }

    /// <summary>The columns (row-type-free; accessors read the projected row).</summary>
    public IEnumerable<Column>? Columns { get; init; }

    /// <summary>Whether cells are editable.</summary>
    public bool Editable { get; init; }
}
