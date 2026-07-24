using FsTypes = Fuaran.UI.Types;

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

/// <summary>The single translation seam between the C# enums and the F# DUs.</summary>
internal static class EnumMap
{
    internal static FsTypes.ImageVariant ToFs(this ImageVariant v) =>
        v switch
        {
            ImageVariant.Default => FsTypes.ImageVariant.Default,
            ImageVariant.Avatar => FsTypes.ImageVariant.Avatar,
            ImageVariant.Rounded => FsTypes.ImageVariant.Rounded,
            _ => FsTypes.ImageVariant.Default,
        };

    internal static FsTypes.ScrollOrientation ToFs(this ScrollOrientation o) =>
        o switch
        {
            ScrollOrientation.Vertical => FsTypes.ScrollOrientation.Vertical,
            ScrollOrientation.Horizontal => FsTypes.ScrollOrientation.Horizontal,
            ScrollOrientation.Both => FsTypes.ScrollOrientation.Both,
            _ => FsTypes.ScrollOrientation.Vertical,
        };

    internal static FsTypes.MathDisplay ToFs(this MathDisplay d) =>
        d == MathDisplay.Inline ? FsTypes.MathDisplay.Inline : FsTypes.MathDisplay.Block;

    internal static FsTypes.DateStyle ToFs(this DateStyle d) =>
        d switch
        {
            DateStyle.Short => FsTypes.DateStyle.Short,
            DateStyle.Medium => FsTypes.DateStyle.Medium,
            DateStyle.Long => FsTypes.DateStyle.Long,
            DateStyle.Full => FsTypes.DateStyle.Full,
            _ => FsTypes.DateStyle.Medium,
        };

    internal static FsTypes.RelativeTimeUnit ToFs(this RelativeTimeUnit u) =>
        u switch
        {
            RelativeTimeUnit.Second => FsTypes.RelativeTimeUnit.Second,
            RelativeTimeUnit.Minute => FsTypes.RelativeTimeUnit.Minute,
            RelativeTimeUnit.Hour => FsTypes.RelativeTimeUnit.Hour,
            RelativeTimeUnit.Day => FsTypes.RelativeTimeUnit.Day,
            RelativeTimeUnit.Week => FsTypes.RelativeTimeUnit.Week,
            RelativeTimeUnit.Month => FsTypes.RelativeTimeUnit.Month,
            RelativeTimeUnit.Year => FsTypes.RelativeTimeUnit.Year,
            _ => FsTypes.RelativeTimeUnit.Day,
        };

    internal static FsTypes.ChartKind ToFs(this ChartKind k) =>
        k switch
        {
            ChartKind.Line => FsTypes.ChartKind.Line,
            ChartKind.Bar => FsTypes.ChartKind.Bar,
            ChartKind.Area => FsTypes.ChartKind.Area,
            ChartKind.Pie => FsTypes.ChartKind.Pie,
            ChartKind.Scatter => FsTypes.ChartKind.Scatter,
            ChartKind.Heatmap => FsTypes.ChartKind.Heatmap,
            _ => FsTypes.ChartKind.Line,
        };

    internal static FsTypes.ToneVariant ToFs(this Tone t) =>
        t switch
        {
            Tone.Default => FsTypes.ToneVariant.Default,
            Tone.Subdued => FsTypes.ToneVariant.Subdued,
            Tone.Brand => FsTypes.ToneVariant.Brand,
            Tone.Success => FsTypes.ToneVariant.Success,
            Tone.Warning => FsTypes.ToneVariant.Warning,
            Tone.Critical => FsTypes.ToneVariant.Critical,
            Tone.Info => FsTypes.ToneVariant.Info,
            _ => FsTypes.ToneVariant.Default,
        };

    internal static FsTypes.StyleWeight ToFs(this Weight w) =>
        w switch
        {
            Weight.Compact => FsTypes.StyleWeight.Compact,
            Weight.Standard => FsTypes.StyleWeight.Standard,
            Weight.Spacious => FsTypes.StyleWeight.Spacious,
            _ => FsTypes.StyleWeight.Standard,
        };

    internal static FsTypes.Emphasis ToFs(this Emphasis e) =>
        e switch
        {
            Emphasis.Quiet => FsTypes.Emphasis.Quiet,
            Emphasis.Normal => FsTypes.Emphasis.Normal,
            Emphasis.Loud => FsTypes.Emphasis.Loud,
            _ => FsTypes.Emphasis.Normal,
        };

    internal static FsTypes.ButtonVariant ToFs(this ButtonVariant v) =>
        v switch
        {
            ButtonVariant.Primary => FsTypes.ButtonVariant.Primary,
            ButtonVariant.Secondary => FsTypes.ButtonVariant.Secondary,
            ButtonVariant.Tertiary => FsTypes.ButtonVariant.Tertiary,
            ButtonVariant.Destructive => FsTypes.ButtonVariant.Destructive,
            _ => FsTypes.ButtonVariant.Secondary,
        };

    internal static FsTypes.BadgeVariant ToFs(this BadgeVariant v) =>
        v switch
        {
            BadgeVariant.Neutral => FsTypes.BadgeVariant.Neutral,
            BadgeVariant.Brand => FsTypes.BadgeVariant.Brand,
            BadgeVariant.Success => FsTypes.BadgeVariant.Success,
            BadgeVariant.Warning => FsTypes.BadgeVariant.Warning,
            BadgeVariant.Critical => FsTypes.BadgeVariant.Critical,
            BadgeVariant.Info => FsTypes.BadgeVariant.Info,
            _ => FsTypes.BadgeVariant.Neutral,
        };

    internal static FsTypes.Orientation ToFs(this Orientation o) =>
        o == Orientation.Horizontal ? FsTypes.Orientation.Horizontal : FsTypes.Orientation.Vertical;

    internal static FsTypes.HeadingVariant ToFs(this HeadingVariant v) =>
        v switch
        {
            HeadingVariant.Standard => FsTypes.HeadingVariant.Standard,
            HeadingVariant.Eyebrow => FsTypes.HeadingVariant.Eyebrow,
            HeadingVariant.Caption => FsTypes.HeadingVariant.Caption,
            HeadingVariant.Lead => FsTypes.HeadingVariant.Lead,
            _ => FsTypes.HeadingVariant.Standard,
        };
}
