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
}
