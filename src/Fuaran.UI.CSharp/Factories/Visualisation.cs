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
//
// SPEC-CONSTRUCTION-TRIPWIRE — the `new FsGen.<X>(…)` calls below are positional on
// purpose. An additive spec slot lands here as CS7036, at the one site that decides
// whether the veneer exposes it or passes the F# default; that is the mechanism, not
// churn. See src/Fuaran.UI.Tests/SpecConstructionTests.fs ("The C# authoring veneer").
public static partial class Fuaran
{
    /// <summary>A chart. <c>Source</c> binds a typed row sequence (name→value maps);
    /// <c>XField</c>/<c>YFields</c> name the plotted keys.</summary>
    public static FuaranNode Chart(ChartOptions options) =>
        new(FsFactory.chart<object>(
            options.Id,
            // Generated ChartSpec ctor is Generated.fs declaration order (Kind,
            // Source, Stacked, XField, YFields, Title, ValueFormat, XTitle,
            // YTitle, Subtitle, LegendPosition, DataLabels, XScale,
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
                options.LegendPosition is { } lp
                    ? Fs.Some(lp.ToFs())
                    : Fs.None<FsGen.ChartLegendPosition>(),
                options.DataLabels is { } dl
                    ? Fs.Some(dl.ToFs())
                    : Fs.None<FsGen.ChartDataLabels>(),
                options.XScale is { } xs
                    ? Fs.Some(xs.ToFs())
                    : Fs.None<FsGen.ChartXScale>(),
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
                // Phase 801's declarative sort-intent slots, surfaced here by Phase 873.
                // No §11 step-6 gate pins them — Coverage.cs reflects NodeKind cases and
                // the VB analyzer pins Vocabulary.Kinds, so a payload-FIELD addition binds
                // neither veneer and nothing was red while they were absent. That is what
                // made surfacing them a deliberate act rather than an automatic one.
                options.Sortable is { } tSortable ? Fs.Some(tSortable) : Fs.None<bool>(),
                options.DefaultSort is { } tSort ? Fs.Some(tSort.Inner) : Fs.None<FsGen.DefaultSort>())));

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
            // GridSpecOf's ctor is declaration order (Source, RowKey, Columns,
            // OnRowClick, Editable, SortStateKey, DefaultSort, PageSize,
            // PageStateKey, EditStateKey) — the five behaviour slots were added to
            // the typed facade by Phase 873, so the veneer no longer has to reach
            // past it to author them.
            new FsTypes.GridSpecOf<TRow, object>(
                (options.Source ?? Binding.Static(Enumerable.Empty<TRow>())).Inner,
                Fs.Func<FsRow, string>(row => (options.RowKey ?? (_ => ""))(row)),
                Fs.List((options.Columns ?? Enumerable.Empty<Column>()).Select(c => c.Inner)),
                Fs.None<Microsoft.FSharp.Core.FSharpFunc<FsRow, FsAction>>(),
                options.Editable,
                Fs.OptStr(options.SortStateKey),
                options.DefaultSort is { } ds ? Fs.Some(ds.Inner) : Fs.None<FsGen.DefaultSort>(),
                options.PageSize is { } ps ? Fs.Some(ps) : Fs.None<int>(),
                Fs.OptStr(options.PageStateKey),
                Fs.OptStr(options.EditStateKey),
                // Phase 1473 — the two print-break declarations, APPENDED to the
                // typed facade so no earlier ctor position moved.
                options.KeepRowsTogether,
                options.RepeatHeader)));
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

    /// <summary>
    /// NARROW this column's sortability (Phase 861). Sorting is a GRID-level
    /// behaviour — <see cref="DataGridOptions{TRow}.SortStateKey"/> is what turns
    /// the header affordance on — and a column flag only ever narrows it:
    /// <c>false</c> opts this column out. <c>true</c> restates the inherited
    /// default and is REFUSED (FUARAN094) under a grid declaring no
    /// <c>SortStateKey</c>, because a column cannot turn on a behaviour whose
    /// state key does not exist.
    /// </summary>
    public Column Sortable(bool sortable) =>
        new(new FsTypes.Column<object>(
            Inner.Label,
            Inner.Value,
            Inner.Format,
            Inner.Kind,
            Inner.Width,
            Fs.Some(sortable),
            Inner.Editable));

    /// <summary>
    /// NARROW this column's editability (Phase 863) — the same rule on the write
    /// side. <c>false</c> makes this column read-only under a grid-level
    /// <see cref="DataGridOptions{TRow}.Editable"/>, which is the declaration
    /// "read-only implied by omission" could not express. <c>true</c> is refused
    /// where the grid is not editable: a column narrows, never widens.
    /// </summary>
    public Column Editable(bool editable) =>
        new(new FsTypes.Column<object>(
            Inner.Label,
            Inner.Value,
            Inner.Format,
            Inner.Kind,
            Inner.Width,
            Inner.Sortable,
            Fs.Some(editable)));
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

    /// <summary>
    /// Which edge the legend occupies (Phase 880), or
    /// <see cref="ChartLegendPosition.None"/> to suppress it. Unset takes the
    /// host style's default — a vertical column on the right — which is not the
    /// same thing as no legend at all.
    /// </summary>
    public ChartLegendPosition? LegendPosition { get; init; }

    /// <summary>
    /// Whether the chart writes its values onto the picture (Phase 881).
    /// <see cref="ChartDataLabels.Ends"/> labels bar caps and line endpoints
    /// only — a stacked bar's total, never its interior segments. Unset means
    /// <see cref="ChartDataLabels.Off"/>, which is also the default.
    /// </summary>
    public ChartDataLabels? DataLabels { get; init; }

    /// <summary>
    /// What the x column means (Phase 882). <see cref="ChartXScale.Temporal"/>
    /// declares canonical ISO-8601 date cells and puts the axis on a continuous
    /// day-scale — calendar-aligned ticks, granularity-adaptive labels, and no
    /// default axis title (a date axis names itself). Unset means
    /// <see cref="ChartXScale.Category"/>, which is also the default.
    /// </summary>
    public ChartXScale? XScale { get; init; }

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

    /// <summary>Whether the reader may re-sort the table by clicking a header
    /// (Phase 801). Absent leaves the pre-801 wire byte-for-byte.</summary>
    public bool? Sortable { get; init; }

    /// <summary>The order the table OPENS in (Phase 801) — configuration, not
    /// data movement. <c>Column</c> indexes <see cref="Headers"/>.</summary>
    public DefaultSort? DefaultSort { get; init; }
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

    /// <summary>Whether cells are editable. Narrow it per column with
    /// <see cref="Column.Editable(bool)"/>.</summary>
    public bool Editable { get; init; }

    /// <summary>
    /// The State key carrying the sort descriptor
    /// <c>{"column": &lt;index&gt;, "direction": "asc"|"desc"}</c> (Phase 861).
    /// Declaring it IS the header affordance — a grid without it renders no
    /// sortable headers, so a "sortable" grid that names no key is prose, not a
    /// declaration.
    /// </summary>
    public string? SortStateKey { get; init; }

    /// <summary>
    /// The order the grid OPENS in (Phase 861) — the same record and wire
    /// spelling a static table uses. It applies while
    /// <see cref="SortStateKey"/> carries nothing; once the user has sorted,
    /// the state wins. Legal with no <see cref="SortStateKey"/> at all.
    /// </summary>
    public DefaultSort? DefaultSort { get; init; }

    /// <summary>
    /// How many rows a page holds (Phase 862). Paging is on only when this and
    /// <see cref="PageStateKey"/> are BOTH set. The pager itself is
    /// renderer-owned, which is what makes a decorative pager unauthorable.
    /// This is not a filter: paging chooses which WINDOW of the rows shows, a
    /// filter chooses which rows exist.
    /// </summary>
    public int? PageSize { get; init; }

    /// <summary>The State key carrying <c>{"page": &lt;1-based int&gt;}</c>
    /// (Phase 862), both written and read by the grid.</summary>
    public string? PageStateKey { get; init; }

    /// <summary>
    /// The DECLARED edit destination (Phase 863): the State key an edited cell's
    /// whole updated rows value commits to. Absent keeps the shipped default —
    /// write back to <see cref="Source"/> when that source is a direct state
    /// binding, display-only otherwise. Naming it is what lets a DECODED grid
    /// say where its edits land.
    /// </summary>
    public string? EditStateKey { get; init; }

    /// <summary>
    /// A ROW is one thing (Phase 1473): when the rendering is paged, no row is
    /// split across the page boundary, so a wrapped cell does not leave half its
    /// lines on one page and half on the next. This is the print-break half no
    /// wrapper reaches — a box around the grid keeps the WHOLE grid together, but
    /// nothing outside the grid knows where a row ends.
    /// </summary>
    public bool KeepRowsTogether { get; init; }

    /// <summary>
    /// Repeat the column headers at the top of every page the grid continues onto
    /// (Phase 1473), so a reader meeting the middle of a long grid still knows
    /// what each column is. The header row group is projected as a table header
    /// group, which makes the repetition the paged formatter's own job rather
    /// than script's — it holds with no JavaScript at all.
    /// </summary>
    public bool RepeatHeader { get; init; }
}
