using FsGen = Fuaran.UI.Generated;

namespace Fuaran.UI.CSharp;

// C#-native enum mirrors of the F# bounded vocabularies. Wrapping them (rather
// than re-exporting the F# DUs) keeps the public surface free of F# runtime
// types — the IDL-bridge rule (Phase 304): an engine swap must not touch
// consumer code, so no `Fuaran.UI.Types.*` name appears on a public signature.
// Each maps 1:1 to its F# case; the internal Map helpers below are the single
// translation seam.

/// <summary>Semantic tone — maps to the F# <c>ToneVariant</c>.</summary>
public enum Tone
{
    Default,
    Subdued,
    Brand,
    Success,
    Warning,
    Critical,
    Info,
}

/// <summary>Density weight — maps to the F# <c>StyleWeight</c>.</summary>
public enum Weight
{
    Compact,
    Standard,
    Spacious,
}

/// <summary>Visual emphasis — maps to the F# <c>Emphasis</c>.</summary>
public enum Emphasis
{
    Quiet,
    Normal,
    Loud,
}

/// <summary>Button presentation — maps to the F# <c>ButtonVariant</c>.</summary>
public enum ButtonVariant
{
    Primary,
    Secondary,
    Tertiary,
    Destructive,
}

/// <summary>Badge presentation — maps to the F# <c>BadgeVariant</c>.</summary>
public enum BadgeVariant
{
    Neutral,
    Brand,
    Success,
    Warning,
    Critical,
    Info,
}

/// <summary>The base direction of ONE authored value — maps to the F#
/// <c>TextDirection</c> (Fuaran-UI Phase 1472).</summary>
/// <remarks>
/// <para>
/// <see cref="Auto"/> is the identity and says nothing: the bidirectional
/// algorithm resolves the value from its own characters, which is what a
/// document that declares nothing gets. <see cref="Ltr"/> / <see cref="Rtl"/>
/// declare that this value reads that way whatever surrounds it, and the
/// renderer isolates the run so the surrounding prose cannot reorder it.
/// </para>
/// <para>
/// Reach for it on an OPAQUE IDENTIFIER inside prose of the other direction —
/// an account number, a reference code, a URL. It names nothing about the
/// document's own direction, the reader's locale, or which side the layout
/// runs from.
/// </para>
/// </remarks>
public enum TextDirection
{
    /// <summary>Resolve from the value's own characters (the default; omitted on the wire).</summary>
    Auto,

    /// <summary>This value reads left-to-right, whatever surrounds it.</summary>
    Ltr,

    /// <summary>This value reads right-to-left, whatever surrounds it.</summary>
    Rtl,
}

/// <summary>Layout / scroll axis — maps to the F# <c>Orientation</c>.</summary>
public enum Orientation
{
    Vertical,
    Horizontal,
}

/// <summary>Heading presentation — maps to the F# <c>HeadingVariant</c>.</summary>
public enum HeadingVariant
{
    Standard,
    Eyebrow,
    Caption,
    Lead,
}

/// <summary>Image presentation — maps to the F# <c>ImageVariant</c>.</summary>
public enum ImageVariant
{
    Default,
    Avatar,
    Rounded,
}

/// <summary>How an image fills its box — maps to the F# <c>ImageFit</c>.</summary>
public enum ImageFit
{
    Natural,
    Cover,
    Contain,
}

/// <summary>The box an image reserves before it loads — maps to the F# <c>ImageAspect</c>.</summary>
public enum ImageAspect
{
    Natural,
    Square,
    FourThree,
    ThreeTwo,
    SixteenNine,
}

/// <summary>Image fetch timing — maps to the F# <c>ImageLoading</c>.</summary>
public enum ImageLoading
{
    Eager,
    Lazy,
}

/// <summary>What a media text track carries — maps to the F# <c>TrackKind</c>.
/// Closed at four; there is deliberately no metadata case, whose cues no user agent
/// renders.</summary>
public enum TrackKind
{
    Subtitles,
    Captions,
    Descriptions,
    Chapters,
}

/// <summary>One deliberate relaxation of an embed's sandbox — maps to the F#
/// <c>EmbedPermission</c>. Closed at four, and the DEFAULT is to declare none, which is
/// total denial. What is deliberately absent is as much the design as what is here:
/// there is no top-level-navigation case (a framed document navigating the page that
/// framed it is the drive-by redirect) and no downloads case.</summary>
public enum EmbedPermission
{
    /// <summary>The framed document may run script.</summary>
    AllowScripts,

    /// <summary>The framed document keeps its own origin rather than being given an
    /// opaque one. Together with <see cref="AllowScripts"/> this is the documented
    /// sandbox escape on a SAME-origin frame, and FUARAN116 warns about the pair; it is
    /// also what every real cross-origin embed needs, which is why it is a warning
    /// rather than a refusal.</summary>
    AllowSameOrigin,

    /// <summary>The framed document may submit forms.</summary>
    AllowForms,

    /// <summary>The framed document may enter fullscreen. NOT a sandbox token — it is a
    /// permissions-policy directive, and the renderers emit it on a separate
    /// attribute.</summary>
    AllowFullscreen,
}

/// <summary>Scroll axis — maps to the F# <c>ScrollOrientation</c>.</summary>
public enum ScrollOrientation
{
    Vertical,
    Horizontal,
    Both,
}

/// <summary>
/// Which overlay a <see cref="Fuaran.Modal"/> is (Phase 1119) — maps to the F#
/// <c>ModalityKind</c>. <c>Modal</c> is the blocking task surface (scrim, focus
/// trap, <c>aria-modal</c>); <c>Popover</c> is the transient anchored one (no
/// scrim, no trap, light-dismiss), positioned against
/// <see cref="ModalOptions.Anchor"/>.
/// </summary>
public enum Modality
{
    Modal,
    Popover,
}

/// <summary>Math presentation — maps to the F# <c>MathDisplay</c>.</summary>
public enum MathDisplay
{
    Inline,
    Block,
}

/// <summary>Chart kind — maps to the F# <c>ChartKind</c>.</summary>
public enum ChartKind
{
    Line,
    Bar,
    Area,
    Pie,
    Scatter,
    Heatmap,
}

/// <summary>
/// Which edge the chart legend occupies (Phase 880) — maps to the F#
/// <c>ChartLegendPosition</c>. Leaving <c>ChartOptions.LegendPosition</c> unset
/// takes the host style's default (<c>Right</c>); <c>None</c> suppresses the
/// legend outright.
/// </summary>
public enum ChartLegendPosition
{
    Top,
    Right,
    Bottom,
    None,
}

/// <summary>
/// Whether a chart writes its values onto the picture (Phase 881) — maps to the
/// F# <c>ChartDataLabels</c>. Leaving <c>ChartOptions.DataLabels</c> unset means
/// <c>Off</c>, which is also the default. There are only two values: <c>Ends</c>
/// labels bar caps and line endpoints, and no member of this enum can ask for a
/// label on every interior point.
/// </summary>
public enum ChartDataLabels
{
    Off,
    Ends,
}

/// <summary>
/// What a chart's x column means (Phase 882) — maps to the F#
/// <c>ChartXScale</c>. Leaving <c>ChartOptions.XScale</c> unset means
/// <c>Category</c>, which is also the default. <c>Temporal</c> declares the
/// column carries canonical ISO-8601 dates and puts the axis on a continuous
/// day-scale; the pre-emit validator refuses the declaration over a non-date
/// column (FUARAN097) rather than drawing a wrong picture.
/// </summary>
public enum ChartXScale
{
    Category,
    Temporal,
}

/// <summary>
/// Which direction of a metric's trend is GOOD (Phase 867) — maps to the F#
/// <c>TrendPolarity</c>. <c>HigherIsBetter</c> is the default and is omitted on
/// the wire. There is deliberately no <c>Neutral</c> member: the wire RESERVES
/// that spelling and does not accept it, and an enum that could spell it would
/// advertise a value every conformant host refuses.
/// </summary>
public enum TrendPolarity
{
    HigherIsBetter,
    LowerIsBetter,
}

/// <summary>
/// Sort direction on a <see cref="DefaultSort"/> (Phase 801) — maps to the F#
/// <c>SortDirection</c>. Lower-case on the wire: <c>"asc"</c> / <c>"desc"</c>.
/// </summary>
public enum SortDirection
{
    Asc,
    Desc,
}

/// <summary>
/// The declared TEXT SHAPE a field's value must take (Phase 864) — maps to the
/// F# <c>TextFormat</c>. A format is a semantic declaration, not a regular
/// expression: use <see cref="FieldRule.Pattern"/> for a shape the vocabulary
/// does not name.
/// </summary>
public enum TextFormat
{
    Email,
    Url,
    Tel,
}

/// <summary>
/// The comparison a cross-field rule makes (Phase 864) — maps to the F#
/// <c>CompareOp</c>.
/// </summary>
public enum CompareOp
{
    Eq,
    Neq,
    Lt,
    Lte,
    Gt,
    Gte,
}

/// <summary>Icon display size (Phase 821) — <c>Medium</c> is the default.</summary>
public enum IconSize
{
    Small,
    Medium,
    Large,
}

/// <summary>The single translation seam between the C# enums and the F# DUs.</summary>
internal static class EnumMap
{
    internal static FsGen.ImageVariant ToFs(this ImageVariant v) =>
        v switch
        {
            ImageVariant.Default => FsGen.ImageVariant.Default,
            ImageVariant.Avatar => FsGen.ImageVariant.Avatar,
            ImageVariant.Rounded => FsGen.ImageVariant.Rounded,
            _ => FsGen.ImageVariant.Default,
        };

    internal static FsGen.ImageFit ToFs(this ImageFit f) =>
        f switch
        {
            ImageFit.Cover => FsGen.ImageFit.Cover,
            ImageFit.Contain => FsGen.ImageFit.Contain,
            _ => FsGen.ImageFit.Natural,
        };

    internal static FsGen.ImageAspect ToFs(this ImageAspect a) =>
        a switch
        {
            ImageAspect.Square => FsGen.ImageAspect.Square,
            ImageAspect.FourThree => FsGen.ImageAspect.FourThree,
            ImageAspect.ThreeTwo => FsGen.ImageAspect.ThreeTwo,
            ImageAspect.SixteenNine => FsGen.ImageAspect.SixteenNine,
            _ => FsGen.ImageAspect.Natural,
        };

    internal static FsGen.ImageLoading ToFs(this ImageLoading l) =>
        l == ImageLoading.Lazy ? FsGen.ImageLoading.Lazy : FsGen.ImageLoading.Eager;

    internal static FsGen.TrackKind ToFs(this TrackKind k) =>
        k switch
        {
            TrackKind.Subtitles => FsGen.TrackKind.Subtitles,
            TrackKind.Captions => FsGen.TrackKind.Captions,
            TrackKind.Descriptions => FsGen.TrackKind.Descriptions,
            TrackKind.Chapters => FsGen.TrackKind.Chapters,
            _ => FsGen.TrackKind.Captions,
        };

    internal static FsGen.EmbedPermission ToFs(this EmbedPermission p) =>
        p switch
        {
            EmbedPermission.AllowSameOrigin => FsGen.EmbedPermission.AllowSameOrigin,
            EmbedPermission.AllowForms => FsGen.EmbedPermission.AllowForms,
            EmbedPermission.AllowFullscreen => FsGen.EmbedPermission.AllowFullscreen,
            _ => FsGen.EmbedPermission.AllowScripts,
        };

    internal static FsGen.ScrollOrientation ToFs(this ScrollOrientation o) =>
        o switch
        {
            ScrollOrientation.Vertical => FsGen.ScrollOrientation.Vertical,
            ScrollOrientation.Horizontal => FsGen.ScrollOrientation.Horizontal,
            ScrollOrientation.Both => FsGen.ScrollOrientation.Both,
            _ => FsGen.ScrollOrientation.Vertical,
        };

    internal static FsGen.ModalityKind ToFs(this Modality m) =>
        m == Modality.Popover ? FsGen.ModalityKind.Popover : FsGen.ModalityKind.Modal;

    internal static FsGen.MathDisplay ToFs(this MathDisplay d) =>
        d == MathDisplay.Inline ? FsGen.MathDisplay.Inline : FsGen.MathDisplay.Block;

    internal static FsGen.IconSize ToFs(this IconSize s) =>
        s switch
        {
            IconSize.Small => FsGen.IconSize.Small,
            IconSize.Large => FsGen.IconSize.Large,
            _ => FsGen.IconSize.Medium,
        };

    internal static FsGen.TrendPolarity ToFs(this TrendPolarity p) =>
        p == TrendPolarity.LowerIsBetter
            ? FsGen.TrendPolarity.LowerIsBetter
            : FsGen.TrendPolarity.HigherIsBetter;

    internal static FsGen.SortDirection ToFs(this SortDirection d) =>
        d == SortDirection.Desc ? FsGen.SortDirection.Desc : FsGen.SortDirection.Asc;

    internal static FsGen.TextFormat ToFs(this TextFormat f) =>
        f switch
        {
            TextFormat.Url => FsGen.TextFormat.Url,
            TextFormat.Tel => FsGen.TextFormat.Tel,
            _ => FsGen.TextFormat.Email,
        };

    internal static FsGen.CompareOp ToFs(this CompareOp o) =>
        o switch
        {
            CompareOp.Neq => FsGen.CompareOp.Neq,
            CompareOp.Lt => FsGen.CompareOp.Lt,
            CompareOp.Lte => FsGen.CompareOp.Lte,
            CompareOp.Gt => FsGen.CompareOp.Gt,
            CompareOp.Gte => FsGen.CompareOp.Gte,
            _ => FsGen.CompareOp.Eq,
        };

    internal static FsGen.DateStyle ToFs(this DateStyle d) =>
        d switch
        {
            DateStyle.Short => FsGen.DateStyle.Short,
            DateStyle.Medium => FsGen.DateStyle.Medium,
            DateStyle.Long => FsGen.DateStyle.Long,
            DateStyle.Full => FsGen.DateStyle.Full,
            _ => FsGen.DateStyle.Medium,
        };

    internal static FsGen.RelativeTimeUnit ToFs(this RelativeTimeUnit u) =>
        u switch
        {
            RelativeTimeUnit.Second => FsGen.RelativeTimeUnit.Second,
            RelativeTimeUnit.Minute => FsGen.RelativeTimeUnit.Minute,
            RelativeTimeUnit.Hour => FsGen.RelativeTimeUnit.Hour,
            RelativeTimeUnit.Day => FsGen.RelativeTimeUnit.Day,
            RelativeTimeUnit.Week => FsGen.RelativeTimeUnit.Week,
            RelativeTimeUnit.Month => FsGen.RelativeTimeUnit.Month,
            RelativeTimeUnit.Year => FsGen.RelativeTimeUnit.Year,
            _ => FsGen.RelativeTimeUnit.Day,
        };

    internal static FsGen.ChartKind ToFs(this ChartKind k) =>
        k switch
        {
            ChartKind.Line => FsGen.ChartKind.Line,
            ChartKind.Bar => FsGen.ChartKind.Bar,
            ChartKind.Area => FsGen.ChartKind.Area,
            ChartKind.Pie => FsGen.ChartKind.Pie,
            ChartKind.Scatter => FsGen.ChartKind.Scatter,
            ChartKind.Heatmap => FsGen.ChartKind.Heatmap,
            _ => FsGen.ChartKind.Line,
        };

    internal static FsGen.ChartLegendPosition ToFs(this ChartLegendPosition p) =>
        p switch
        {
            ChartLegendPosition.Top => FsGen.ChartLegendPosition.Top,
            ChartLegendPosition.Right => FsGen.ChartLegendPosition.Right,
            ChartLegendPosition.Bottom => FsGen.ChartLegendPosition.Bottom,
            ChartLegendPosition.None => FsGen.ChartLegendPosition.None,
            _ => FsGen.ChartLegendPosition.Right,
        };

    internal static FsGen.ChartDataLabels ToFs(this ChartDataLabels d) =>
        d switch
        {
            ChartDataLabels.Ends => FsGen.ChartDataLabels.Ends,
            _ => FsGen.ChartDataLabels.Off,
        };

    internal static FsGen.ChartXScale ToFs(this ChartXScale x) =>
        x switch
        {
            ChartXScale.Temporal => FsGen.ChartXScale.Temporal,
            _ => FsGen.ChartXScale.Category,
        };

    internal static FsGen.ToneVariant ToFs(this Tone t) =>
        t switch
        {
            Tone.Default => FsGen.ToneVariant.Default,
            Tone.Subdued => FsGen.ToneVariant.Subdued,
            Tone.Brand => FsGen.ToneVariant.Brand,
            Tone.Success => FsGen.ToneVariant.Success,
            Tone.Warning => FsGen.ToneVariant.Warning,
            Tone.Critical => FsGen.ToneVariant.Critical,
            Tone.Info => FsGen.ToneVariant.Info,
            _ => FsGen.ToneVariant.Default,
        };

    internal static FsGen.StyleWeight ToFs(this Weight w) =>
        w switch
        {
            Weight.Compact => FsGen.StyleWeight.Compact,
            Weight.Standard => FsGen.StyleWeight.Standard,
            Weight.Spacious => FsGen.StyleWeight.Spacious,
            _ => FsGen.StyleWeight.Standard,
        };

    internal static FsGen.Emphasis ToFs(this Emphasis e) =>
        e switch
        {
            Emphasis.Quiet => FsGen.Emphasis.Quiet,
            Emphasis.Normal => FsGen.Emphasis.Normal,
            Emphasis.Loud => FsGen.Emphasis.Loud,
            _ => FsGen.Emphasis.Normal,
        };

    internal static FsGen.ButtonVariant ToFs(this ButtonVariant v) =>
        v switch
        {
            ButtonVariant.Primary => FsGen.ButtonVariant.Primary,
            ButtonVariant.Secondary => FsGen.ButtonVariant.Secondary,
            ButtonVariant.Tertiary => FsGen.ButtonVariant.Tertiary,
            ButtonVariant.Destructive => FsGen.ButtonVariant.Destructive,
            _ => FsGen.ButtonVariant.Secondary,
        };

    internal static FsGen.BadgeVariant ToFs(this BadgeVariant v) =>
        v switch
        {
            BadgeVariant.Neutral => FsGen.BadgeVariant.Neutral,
            BadgeVariant.Brand => FsGen.BadgeVariant.Brand,
            BadgeVariant.Success => FsGen.BadgeVariant.Success,
            BadgeVariant.Warning => FsGen.BadgeVariant.Warning,
            BadgeVariant.Critical => FsGen.BadgeVariant.Critical,
            BadgeVariant.Info => FsGen.BadgeVariant.Info,
            _ => FsGen.BadgeVariant.Neutral,
        };

    internal static FsGen.TextDirection ToFs(this TextDirection d) =>
        d switch
        {
            TextDirection.Ltr => FsGen.TextDirection.Ltr,
            TextDirection.Rtl => FsGen.TextDirection.Rtl,
            _ => FsGen.TextDirection.Auto,
        };

    internal static FsGen.Orientation ToFs(this Orientation o) =>
        o == Orientation.Horizontal ? FsGen.Orientation.Horizontal : FsGen.Orientation.Vertical;

    internal static FsGen.HeadingVariant ToFs(this HeadingVariant v) =>
        v switch
        {
            HeadingVariant.Standard => FsGen.HeadingVariant.Standard,
            HeadingVariant.Eyebrow => FsGen.HeadingVariant.Eyebrow,
            HeadingVariant.Caption => FsGen.HeadingVariant.Caption,
            HeadingVariant.Lead => FsGen.HeadingVariant.Lead,
            _ => FsGen.HeadingVariant.Standard,
        };
}
