using FsGen = Fuaran.UI.Generated;

namespace Fuaran.UI.CSharp;

/// <summary>
/// A chart's data-addressed annotation (Phase 1490) — the authoring facade over
/// the F# <c>ChartAnnotation</c>.
/// </summary>
/// <remarks>
/// <para>
/// An annotation names a place in the DATA's coordinates and, optionally, a
/// label. It carries no geometry and no ink: there is deliberately no pixel, no
/// offset, no anchor, no colour, no opacity and no font to set here. The
/// geometry is the lowering's (derived from the plot rectangle, the text metrics
/// and the value scale) and the ink is the host style's — which is what makes a
/// data-addressed annotation survive a data change, a theme flip, a restyle and
/// a resize, where an overlay drawn at pixel coordinates is correct for exactly
/// one dataset on one canvas.
/// </para>
/// <para>
/// One closed union rather than one <see cref="ChartOptions"/> slot per member,
/// so a further member is a case here rather than another widening of the chart
/// options record.
/// </para>
/// </remarks>
public readonly struct ChartAnnotation
{
    internal FsGen.ChartAnnotation Inner { get; }

    private ChartAnnotation(FsGen.ChartAnnotation fs) => Inner = fs;

    /// <summary>
    /// A horizontal line at <paramref name="value"/> in the VALUE axis's own
    /// units — a named zero line, a target, a threshold, a budget.
    /// </summary>
    /// <param name="value">
    /// The value in axis units. It ENTERS the value domain before the axis is
    /// scaled, so a threshold above every bar is still drawn and the axis says
    /// so. It must be finite: NaN and the infinities name no place on an axis,
    /// and both the decoder and the pre-emit validator refuse them (FUARAN137).
    /// </param>
    /// <param name="label">
    /// An optional label, carried unresolved into the drawing and resolved at
    /// render time. Its placement is fit-gated: a label with no room is
    /// suppressed, never clipped and never moved onto a mark — and a suppressed
    /// label never suppresses its line.
    /// </param>
    public static ChartAnnotation ReferenceLine(double value, Text? label = null) =>
        new(FsGen.ChartAnnotation.NewReferenceLine(
            value,
            label is { } l ? Fs.Some(l.Inner) : Fs.None<FsGen.TextSource>()));

    /// <summary>
    /// A vertical line at an x address (Phase 1491) — a policy change, a launch,
    /// a shock. The mirror of <see cref="ReferenceLine"/> across the axes.
    /// </summary>
    /// <param name="at">
    /// Where on the x axis, in the axis's OWN address form: a
    /// <see cref="ChartAnnotationX.Category"/> key on a band axis, or a
    /// <see cref="ChartAnnotationX.Date"/> under a temporal one. The mismatch is
    /// refused rather than coerced (FUARAN139), and a category key must name
    /// exactly one row (FUARAN138).
    /// </param>
    /// <param name="label">
    /// An optional label, drawn at the top of the plot beside the line and
    /// carried unresolved into the drawing. Markers close together are the normal
    /// case, so its width budget runs to the NEXT marker's line: a label with no
    /// room is suppressed, never clipped and never moved across a neighbour — and
    /// a suppressed label never suppresses its marker.
    /// </param>
    public static ChartAnnotation EventMarker(ChartAnnotationX at, Text? label = null) =>
        new(FsGen.ChartAnnotation.NewEventMarker(
            at.Inner,
            label is { } l ? Fs.Some(l.Inner) : Fs.None<FsGen.TextSource>()));
}

/// <summary>
/// Where on the x axis an annotation sits (Phase 1491) — the authoring facade
/// over the F# <c>ChartAnnotationX</c>.
/// </summary>
/// <remarks>
/// Two forms, and they are the two the x axis already distinguishes: a category
/// key names a BAND on a band axis, an ISO-8601 date names an INSTANT under a
/// temporal one. Which applies is DECLARED by the chart's own x scale and never
/// sniffed from the address — a category key on a temporal axis, or a date on a
/// band axis, is refused rather than guessed at.
/// </remarks>
public readonly struct ChartAnnotationX
{
    internal FsGen.ChartAnnotationX Inner { get; }

    private ChartAnnotationX(FsGen.ChartAnnotationX fs) => Inner = fs;

    /// <summary>
    /// A band on a CATEGORY x axis, named by its key — the x cell as it is
    /// labelled. The key must appear in exactly one row: none names no band, and
    /// two give the band two centres to be drawn at.
    /// </summary>
    public static ChartAnnotationX Category(string key) =>
        new(FsGen.ChartAnnotationX.NewCategory(key));

    /// <summary>
    /// An instant on a TEMPORAL x axis, as a canonical ISO-8601 date
    /// (<c>YYYY-MM-DD</c>). It enters the axis extent before the ticks are
    /// chosen, so a marker beyond the last datum still draws and the axis says
    /// so — which is also why an unreadable date is refused outright (FUARAN140)
    /// rather than read as 1970-01-01.
    /// </summary>
    public static ChartAnnotationX Date(string iso) =>
        new(FsGen.ChartAnnotationX.NewDate(iso));
}
