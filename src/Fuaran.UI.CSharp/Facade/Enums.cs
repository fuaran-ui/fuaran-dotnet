namespace Fuaran.UI.CSharp;

// C#-native enum mirrors of the F# bounded vocabularies. Wrapping them (rather
// than re-exporting the F# DUs) keeps the public surface free of F# runtime
// types — the IDL-bridge rule (Phase 304): an engine swap must not touch
// consumer code, so no `Fuaran.UI.Types.*` name appears on a public signature.
// Each maps 1:1 to its F# case. The translation seam (`EnumMap.ToFs`) is GENERATED
// from the vocabulary (Phase 2241, EnumMap.Generated.cs beside this file), one arm
// per vocabulary case — so a case added to the vocabulary and missing here fails the
// compile there, naming this declaration. Add the case here; never edit the seam.
// A value outside the declared cases (a cast integer) has no arm: the seam throws
// ArgumentOutOfRangeException rather than choosing a case for it (Phase 1930).

/// <summary>Semantic tone — maps to the F# <c>ToneVariant</c>.</summary>
public enum Tone
{
    /// <summary>The <c>Default</c> value of <see cref="Tone"/>.</summary>
    Default,
    /// <summary>The <c>Subdued</c> value of <see cref="Tone"/>.</summary>
    Subdued,
    /// <summary>The <c>Brand</c> value of <see cref="Tone"/>.</summary>
    Brand,
    /// <summary>The <c>Success</c> value of <see cref="Tone"/>.</summary>
    Success,
    /// <summary>The <c>Warning</c> value of <see cref="Tone"/>.</summary>
    Warning,
    /// <summary>The <c>Critical</c> value of <see cref="Tone"/>.</summary>
    Critical,
    /// <summary>The <c>Info</c> value of <see cref="Tone"/>.</summary>
    Info,
}

/// <summary>Density weight — maps to the F# <c>StyleWeight</c>.</summary>
public enum Weight
{
    /// <summary>The <c>Compact</c> value of <see cref="Weight"/>.</summary>
    Compact,
    /// <summary>The <c>Standard</c> value of <see cref="Weight"/>.</summary>
    Standard,
    /// <summary>The <c>Spacious</c> value of <see cref="Weight"/>.</summary>
    Spacious,
}

/// <summary>Visual emphasis — maps to the F# <c>Emphasis</c>.</summary>
public enum Emphasis
{
    /// <summary>The <c>Quiet</c> value of <see cref="Emphasis"/>.</summary>
    Quiet,
    /// <summary>The <c>Normal</c> value of <see cref="Emphasis"/>.</summary>
    Normal,
    /// <summary>The <c>Loud</c> value of <see cref="Emphasis"/>.</summary>
    Loud,
}

/// <summary>Button presentation — maps to the F# <c>ButtonVariant</c>.</summary>
public enum ButtonVariant
{
    /// <summary>The <c>Primary</c> value of <see cref="ButtonVariant"/>.</summary>
    Primary,
    /// <summary>The <c>Secondary</c> value of <see cref="ButtonVariant"/>.</summary>
    Secondary,
    /// <summary>The <c>Tertiary</c> value of <see cref="ButtonVariant"/>.</summary>
    Tertiary,
    /// <summary>The <c>Destructive</c> value of <see cref="ButtonVariant"/>.</summary>
    Destructive,
}

/// <summary>Badge presentation — maps to the F# <c>BadgeVariant</c>.</summary>
public enum BadgeVariant
{
    /// <summary>The <c>Neutral</c> value of <see cref="BadgeVariant"/>.</summary>
    Neutral,
    /// <summary>The <c>Brand</c> value of <see cref="BadgeVariant"/>.</summary>
    Brand,
    /// <summary>The <c>Success</c> value of <see cref="BadgeVariant"/>.</summary>
    Success,
    /// <summary>The <c>Warning</c> value of <see cref="BadgeVariant"/>.</summary>
    Warning,
    /// <summary>The <c>Critical</c> value of <see cref="BadgeVariant"/>.</summary>
    Critical,
    /// <summary>The <c>Info</c> value of <see cref="BadgeVariant"/>.</summary>
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
    /// <summary>The <c>Vertical</c> value of <see cref="Orientation"/>.</summary>
    Vertical,
    /// <summary>The <c>Horizontal</c> value of <see cref="Orientation"/>.</summary>
    Horizontal,
}

/// <summary>Heading presentation — maps to the F# <c>HeadingVariant</c>.</summary>
public enum HeadingVariant
{
    /// <summary>The <c>Standard</c> value of <see cref="HeadingVariant"/>.</summary>
    Standard,
    /// <summary>The <c>Eyebrow</c> value of <see cref="HeadingVariant"/>.</summary>
    Eyebrow,
    /// <summary>The <c>Caption</c> value of <see cref="HeadingVariant"/>.</summary>
    Caption,
    /// <summary>The <c>Lead</c> value of <see cref="HeadingVariant"/>.</summary>
    Lead,
}

/// <summary>Image presentation — maps to the F# <c>ImageVariant</c>.</summary>
public enum ImageVariant
{
    /// <summary>The <c>Default</c> value of <see cref="ImageVariant"/>.</summary>
    Default,
    /// <summary>The <c>Avatar</c> value of <see cref="ImageVariant"/>.</summary>
    Avatar,
    /// <summary>The <c>Rounded</c> value of <see cref="ImageVariant"/>.</summary>
    Rounded,
}

/// <summary>How an image fills its box — maps to the F# <c>ImageFit</c>.</summary>
public enum ImageFit
{
    /// <summary>The <c>Natural</c> value of <see cref="ImageFit"/>.</summary>
    Natural,
    /// <summary>The <c>Cover</c> value of <see cref="ImageFit"/>.</summary>
    Cover,
    /// <summary>The <c>Contain</c> value of <see cref="ImageFit"/>.</summary>
    Contain,
}

/// <summary>The box an image reserves before it loads — maps to the F# <c>ImageAspect</c>.</summary>
public enum ImageAspect
{
    /// <summary>The <c>Natural</c> value of <see cref="ImageAspect"/>.</summary>
    Natural,
    /// <summary>The <c>Square</c> value of <see cref="ImageAspect"/>.</summary>
    Square,
    /// <summary>The <c>FourThree</c> value of <see cref="ImageAspect"/>.</summary>
    FourThree,
    /// <summary>The <c>ThreeTwo</c> value of <see cref="ImageAspect"/>.</summary>
    ThreeTwo,
    /// <summary>The <c>SixteenNine</c> value of <see cref="ImageAspect"/>.</summary>
    SixteenNine,
}

/// <summary>Image fetch timing — maps to the F# <c>ImageLoading</c>.</summary>
public enum ImageLoading
{
    /// <summary>The <c>Eager</c> value of <see cref="ImageLoading"/>.</summary>
    Eager,
    /// <summary>The <c>Lazy</c> value of <see cref="ImageLoading"/>.</summary>
    Lazy,
}

/// <summary>What a media text track carries — maps to the F# <c>TrackKind</c>.
/// Closed at four; there is deliberately no metadata case, whose cues no user agent
/// renders.</summary>
public enum TrackKind
{
    /// <summary>The <c>Subtitles</c> value of <see cref="TrackKind"/>.</summary>
    Subtitles,
    /// <summary>The <c>Captions</c> value of <see cref="TrackKind"/>.</summary>
    Captions,
    /// <summary>The <c>Descriptions</c> value of <see cref="TrackKind"/>.</summary>
    Descriptions,
    /// <summary>The <c>Chapters</c> value of <see cref="TrackKind"/>.</summary>
    Chapters,
}

/// <summary>One deliberate relaxation of an embed's sandbox — maps to the F#
/// <c>EmbedPermission</c>. Closed at four, and the DEFAULT is to declare none, which is
/// total denial. What is deliberately absent is as much the design as what is here:
/// there is no top-level-navigation case (a framed document navigating the page that
/// framed it is the drive-by redirect) and no downloads case.
/// <para>The values are explicit, and zero is <see cref="None"/> (Phase 2346):
/// <c>default(EmbedPermission)</c>, an uninitialised field or array element and
/// <c>(EmbedPermission)0</c> all read as zero, so zero must be the value that grants
/// nothing. Every relaxation is a positive value.</para></summary>
public enum EmbedPermission
{
    /// <summary>No relaxation — the zero value, and it grants nothing. It has no wire
    /// case: the wire's no-grant form is absence from the list, so the factory drops
    /// it.</summary>
    None = 0,

    /// <summary>The framed document may run script.</summary>
    AllowScripts = 1,

    /// <summary>The framed document keeps its own origin rather than being given an
    /// opaque one. Together with <see cref="AllowScripts"/> this is the documented
    /// sandbox escape on a SAME-origin frame, and FUARAN116 warns about the pair; it is
    /// also what every real cross-origin embed needs, which is why it is a warning
    /// rather than a refusal.</summary>
    AllowSameOrigin = 2,

    /// <summary>The framed document may submit forms.</summary>
    AllowForms = 3,

    /// <summary>The framed document may enter fullscreen. NOT a sandbox token — it is a
    /// permissions-policy directive, and the renderers emit it on a separate
    /// attribute.</summary>
    AllowFullscreen = 4,
}

/// <summary>Scroll axis — maps to the F# <c>ScrollOrientation</c>.</summary>
public enum ScrollOrientation
{
    /// <summary>The <c>Vertical</c> value of <see cref="ScrollOrientation"/>.</summary>
    Vertical,
    /// <summary>The <c>Horizontal</c> value of <see cref="ScrollOrientation"/>.</summary>
    Horizontal,
    /// <summary>The <c>Both</c> value of <see cref="ScrollOrientation"/>.</summary>
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
    /// <summary>The <c>Modal</c> value of <see cref="Modality"/>.</summary>
    Modal,
    /// <summary>The <c>Popover</c> value of <see cref="Modality"/>.</summary>
    Popover,
}

/// <summary>
/// Which of the reader's own recording devices a <see cref="Fuaran.FileUpload"/>
/// asks the platform to open instead of the file browser (Phase 1116) — maps to
/// the F# <c>CaptureSource</c>. It is the HTML <c>capture</c> attribute and
/// nothing more: a request the user agent may ignore, mediated by the picker
/// permission the control already has, with no stream and no standing grant. A
/// device that has no camera or microphone simply shows the file browser.
/// </summary>
public enum CaptureSource
{
    /// <summary>The platform camera — stills or short clips.</summary>
    Camera,

    /// <summary>The platform audio recorder.</summary>
    Microphone,
}

/// <summary>
/// Which browsing context an <see cref="FuaranAction.Navigate(Text, NavigateTarget)"/>
/// lands in (Phase 1536) — maps to the F# <c>NavigateTarget</c>. <c>Self</c> is the
/// default and is omitted on the wire; <c>Blank</c> opens a fresh context, which every
/// host opens with <c>noopener,noreferrer</c>.
/// </summary>
/// <remarks>
/// Closed at two members where an HTML <c>target</c> attribute takes any token: the
/// axis declared here is whether the reader stays in this context or gets a new one,
/// and that question has two answers. <c>_parent</c> and <c>_top</c> are frame-busting
/// gestures a hosted tree must not be able to ask for, and a named frame is an
/// addressing scheme this language does not have.
/// </remarks>
public enum NavigateTarget
{
    /// <summary>This browsing context — the default, omitted on the wire.</summary>
    Self,

    /// <summary>A fresh browsing context, opened with <c>noopener,noreferrer</c>.</summary>
    Blank,
}

/// <summary>Math presentation — maps to the F# <c>MathDisplay</c>.</summary>
public enum MathDisplay
{
    /// <summary>The <c>Inline</c> value of <see cref="MathDisplay"/>.</summary>
    Inline,
    /// <summary>The <c>Block</c> value of <see cref="MathDisplay"/>.</summary>
    Block,
}

/// <summary>Chart kind — maps to the F# <c>ChartKind</c>.</summary>
public enum ChartKind
{
    /// <summary>The <c>Line</c> value of <see cref="ChartKind"/>.</summary>
    Line,
    /// <summary>The <c>Bar</c> value of <see cref="ChartKind"/>.</summary>
    Bar,
    /// <summary>The <c>Area</c> value of <see cref="ChartKind"/>.</summary>
    Area,
    /// <summary>The <c>Pie</c> value of <see cref="ChartKind"/>.</summary>
    Pie,
    /// <summary>The <c>Scatter</c> value of <see cref="ChartKind"/>.</summary>
    Scatter,
    /// <summary>The <c>Heatmap</c> value of <see cref="ChartKind"/>.</summary>
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
    /// <summary>The <c>Top</c> value of <see cref="ChartLegendPosition"/>.</summary>
    Top,
    /// <summary>The <c>Right</c> value of <see cref="ChartLegendPosition"/>.</summary>
    Right,
    /// <summary>The <c>Bottom</c> value of <see cref="ChartLegendPosition"/>.</summary>
    Bottom,
    /// <summary>The <c>None</c> value of <see cref="ChartLegendPosition"/>.</summary>
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
    /// <summary>The <c>Off</c> value of <see cref="ChartDataLabels"/>.</summary>
    Off,
    /// <summary>The <c>Ends</c> value of <see cref="ChartDataLabels"/>.</summary>
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
    /// <summary>The <c>Category</c> value of <see cref="ChartXScale"/>.</summary>
    Category,
    /// <summary>The <c>Temporal</c> value of <see cref="ChartXScale"/>.</summary>
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
    /// <summary>The <c>HigherIsBetter</c> value of <see cref="TrendPolarity"/>.</summary>
    HigherIsBetter,
    /// <summary>The <c>LowerIsBetter</c> value of <see cref="TrendPolarity"/>.</summary>
    LowerIsBetter,
}

/// <summary>
/// Sort direction on a <see cref="DefaultSort"/> (Phase 801) — maps to the F#
/// <c>SortDirection</c>. Lower-case on the wire: <c>"asc"</c> / <c>"desc"</c>.
/// </summary>
public enum SortDirection
{
    /// <summary>The <c>Asc</c> value of <see cref="SortDirection"/>.</summary>
    Asc,
    /// <summary>The <c>Desc</c> value of <see cref="SortDirection"/>.</summary>
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
    /// <summary>The <c>Email</c> value of <see cref="TextFormat"/>.</summary>
    Email,
    /// <summary>The <c>Url</c> value of <see cref="TextFormat"/>.</summary>
    Url,
    /// <summary>The <c>Tel</c> value of <see cref="TextFormat"/>.</summary>
    Tel,
}

/// <summary>
/// The comparison a cross-field rule makes (Phase 864) — maps to the F#
/// <c>CompareOp</c>.
/// </summary>
public enum CompareOp
{
    /// <summary>The <c>Eq</c> value of <see cref="CompareOp"/>.</summary>
    Eq,
    /// <summary>The <c>Neq</c> value of <see cref="CompareOp"/>.</summary>
    Neq,
    /// <summary>The <c>Lt</c> value of <see cref="CompareOp"/>.</summary>
    Lt,
    /// <summary>The <c>Lte</c> value of <see cref="CompareOp"/>.</summary>
    Lte,
    /// <summary>The <c>Gt</c> value of <see cref="CompareOp"/>.</summary>
    Gt,
    /// <summary>The <c>Gte</c> value of <see cref="CompareOp"/>.</summary>
    Gte,
}

/// <summary>Icon display size (Phase 821) — <c>Medium</c> is the default.</summary>
public enum IconSize
{
    /// <summary>The <c>Small</c> value of <see cref="IconSize"/>.</summary>
    Small,
    /// <summary>The <c>Medium</c> value of <see cref="IconSize"/>.</summary>
    Medium,
    /// <summary>The <c>Large</c> value of <see cref="IconSize"/>.</summary>
    Large,
}

// ── Phase 1532 ────────────────────────────────────────────────────────────────

/// <summary>
/// The grain a <see cref="Binding.Now"/> instant is truncated to before any slot
/// reads it (Phase 1533) — maps to the F# <c>TimeGrain</c>.
/// </summary>
/// <remarks>
/// Truncation happens BEFORE the projection, not after: the grain is the
/// document's declaration about the instant, so it has to be upstream of every
/// reading of it.
/// </remarks>
public enum TimeGrain
{
    /// <summary>Whole seconds — the identity, and what an omitted grain means.</summary>
    Second,

    /// <summary>Whole minutes.</summary>
    Minute,

    /// <summary>Whole hours.</summary>
    Hour,

    /// <summary>Whole days.</summary>
    Day,
}

/// <summary>
/// The unit a <see cref="LocaleFormat.Duration"/> / <see cref="CellFormat.Duration"/>
/// source counts (Phase 819) — maps to the F# <c>DurationUnit</c>.
/// </summary>
public enum DurationUnit
{
    /// <summary>The <c>Seconds</c> value of <see cref="DurationUnit"/>.</summary>
    Seconds,
    /// <summary>The <c>Minutes</c> value of <see cref="DurationUnit"/>.</summary>
    Minutes,
    /// <summary>The <c>Hours</c> value of <see cref="DurationUnit"/>.</summary>
    Hours,
}

/// <summary>
/// How a duration renders (Phase 819) — maps to the F# <c>DurationStyle</c>.
/// </summary>
public enum DurationStyle
{
    /// <summary>"1h 23m".</summary>
    Compact,

    /// <summary>"1:23:45".</summary>
    Clock,

    /// <summary>"1 hour 23 minutes".</summary>
    Long,
}

/// <summary>
/// How a <see cref="FuaranAction.ReadFileBody"/> hands the file's bytes back —
/// maps to the F# <c>FileReadEncoding</c>.
/// </summary>
public enum FileEncoding
{
    /// <summary>Decoded text.</summary>
    Text,

    /// <summary>Base64 of the raw bytes.</summary>
    Base64,

    /// <summary>A <c>data:</c> URL carrying the bytes and their media type.</summary>
    DataUrl,
}
