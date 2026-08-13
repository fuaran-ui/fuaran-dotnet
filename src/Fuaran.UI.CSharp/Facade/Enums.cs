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

/// <summary>Scroll axis — maps to the F# <c>ScrollOrientation</c>.</summary>
public enum ScrollOrientation
{
    Vertical,
    Horizontal,
    Both,
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

    internal static FsGen.ScrollOrientation ToFs(this ScrollOrientation o) =>
        o switch
        {
            ScrollOrientation.Vertical => FsGen.ScrollOrientation.Vertical,
            ScrollOrientation.Horizontal => FsGen.ScrollOrientation.Horizontal,
            ScrollOrientation.Both => FsGen.ScrollOrientation.Both,
            _ => FsGen.ScrollOrientation.Vertical,
        };

    internal static FsGen.MathDisplay ToFs(this MathDisplay d) =>
        d == MathDisplay.Inline ? FsGen.MathDisplay.Inline : FsGen.MathDisplay.Block;

    internal static FsGen.IconSize ToFs(this IconSize s) =>
        s switch
        {
            IconSize.Small => FsGen.IconSize.Small,
            IconSize.Large => FsGen.IconSize.Large,
            _ => FsGen.IconSize.Medium,
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
