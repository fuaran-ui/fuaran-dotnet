using System;
using System.Collections.Generic;
using System.Linq;
using FsFactory = global::Fuaran.UI.Fuaran;
using FsColumn = global::Fuaran.UI.Column;
using FsTypes = Fuaran.UI.Types;
using FsGen = Fuaran.UI.Generated;
using FsAction = Fuaran.UI.Generated.Action<object>;

namespace Fuaran.UI.CSharp;

// Phase 305 — the Visualisation kinds. DataGrid is generic over the row type; its
// typed columns (Column.Text/Numeric/… with a `row -> value` selector) bridge the
// row-type erasure the F# tier manages internally (`Fuaran.grid` boxes the accessors).
public static partial class Fuaran
{
    /// <summary>A chart. <c>Source</c> binds a row sequence; <c>XField</c>/<c>YFields</c> name the plotted keys.</summary>
    public static FuaranNode Chart(ChartOptions options) =>
        new(FsFactory.chart<object>(
            options.Id,
            new FsTypes.ChartSpec<object>(
                (options.Source ?? Binding.Static(Enumerable.Empty<object>())).Inner,
                options.Kind.ToFs(),
                options.XField,
                Fs.List(options.YFields ?? Enumerable.Empty<string>()),
                options.Title is { } t ? Fs.Some(t.Inner) : Fs.None<FsGen.TextSource>(),
                Fs.None<Microsoft.FSharp.Core.FSharpFunc<object, FsAction>>(),
                options.Stacked)));

    /// <summary>A static (non-data-bound) HTML table.</summary>
    public static FuaranNode Table(TableOptions options) =>
        new(FsFactory.table<object>(
            options.Id,
            new FsTypes.TableSpec<object>(
                Fs.List((options.Headers ?? Enumerable.Empty<Text>()).Select(t => t.Inner)),
                Fs.List((options.Rows ?? Enumerable.Empty<IEnumerable<Text>>())
                    .Select(r => Fs.List(r.Select(t => t.Inner)))),
                Fs.None<Microsoft.FSharp.Core.FSharpFunc<int, FsAction>>())));

    /// <summary>A marker map.</summary>
    public static FuaranNode Map(MapOptions options) =>
        new(FsFactory.map<object>(
            options.Id,
            new FsTypes.MapSpec<object>(
                global::Fuaran.UI.Generated.Binding<IEnumerable<FsGen.MapMarker>>.NewStatic(
                    Fs.Some<IEnumerable<FsGen.MapMarker>>(
                        (options.Markers ?? Enumerable.Empty<(double, double, string)>())
                            // Generated MapMarker declares (Label, Latitude, Longitude); Label is a bare string.
                            .Select(m => new FsGen.MapMarker(m.Item3, m.Item1, m.Item2)))),
                options.CentreLatitude,
                options.CentreLongitude,
                options.Zoom,
                Fs.None<Microsoft.FSharp.Core.FSharpFunc<FsGen.MapMarker, FsAction>>())));

    /// <summary>A data-bound grid over rows of type <typeparamref name="TRow"/>.</summary>
    public static FuaranNode DataGrid<TRow>(DataGridOptions<TRow> options) =>
        new(FsFactory.grid<TRow, object>(
            options.Id,
            new FsTypes.GridSpecOf<TRow, object>(
                (options.Source ?? Binding.Static(Enumerable.Empty<TRow>())).Inner,
                Fs.Func<TRow, string>(options.RowKey ?? (_ => "")),
                Fs.List((options.Columns ?? Enumerable.Empty<Column<TRow>>()).Select(c => c.Inner)),
                Fs.None<Microsoft.FSharp.Core.FSharpFunc<TRow, FsAction>>(),
                options.Editable)));
}

/// <summary>A typed data-grid column over rows of type <typeparamref name="TRow"/>.</summary>
public sealed class Column<TRow>
{
    internal FsTypes.Column<TRow, object> Inner { get; }

    private Column(FsTypes.Column<TRow, object> inner) => Inner = inner;

    /// <summary>A text column reading <paramref name="value"/> from each row.</summary>
    public static Column<TRow> Text(string label, Func<TRow, string> value) =>
        new(FsColumn.text<TRow, object>(label, Fs.Func<TRow, string>(value)));

    /// <summary>A numeric column reading <paramref name="value"/> from each row.</summary>
    public static Column<TRow> Numeric(string label, Func<TRow, double> value) =>
        new(FsColumn.numeric<TRow, object>(label, Fs.Func<TRow, double>(value)));

    /// <summary>A boolean column reading <paramref name="value"/> from each row.</summary>
    public static Column<TRow> Bool(string label, Func<TRow, bool> value) =>
        new(FsColumn.@bool<TRow, object>(label, Fs.Func<TRow, bool>(value)));

    /// <summary>A date column reading <paramref name="value"/> from each row.</summary>
    public static Column<TRow> Date(string label, Func<TRow, DateTimeOffset> value) =>
        new(FsColumn.date<TRow, object>(label, Fs.Func<TRow, DateTimeOffset>(value)));
}

/// <summary>Options for <see cref="Fuaran.Chart"/>.</summary>
public sealed record ChartOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The bound row source.</summary>
    public Binding<IEnumerable<object>>? Source { get; init; }

    /// <summary>The chart kind (default line).</summary>
    public ChartKind Kind { get; init; } = ChartKind.Line;

    /// <summary>The x-axis field key.</summary>
    public string XField { get; init; } = "";

    /// <summary>The y-axis field keys.</summary>
    public IEnumerable<string>? YFields { get; init; }

    /// <summary>An optional chart title.</summary>
    public Text? Title { get; init; }

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

    /// <summary>The bound row source.</summary>
    public Binding<IEnumerable<TRow>>? Source { get; init; }

    /// <summary>A stable per-row key selector.</summary>
    public Func<TRow, string>? RowKey { get; init; }

    /// <summary>The typed columns.</summary>
    public IEnumerable<Column<TRow>>? Columns { get; init; }

    /// <summary>Whether cells are editable.</summary>
    public bool Editable { get; init; }
}
