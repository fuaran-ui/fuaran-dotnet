using System.Collections.Generic;
using System.Linq;
using FsFactory = global::Fuaran.UI.Fuaran;
using FsTypes = Fuaran.UI.Types;
using FsGen = Fuaran.UI.Generated;

namespace Fuaran.UI.CSharp;

// Phase 305 — the remaining Display kinds (Heading / Markdown / Metric ship in the
// foundation). Badge / Sparkline / Spacer have no F# smart constructor, so they
// build bare (their ARIA default is `none`, so bare == smart-ctor output == corpus).
public static partial class Fuaran
{
    /// <summary>A coloured badge / pill. (Built bare — no F# smart ctor; ARIA default is none.)</summary>
    public static FuaranNode Badge(BadgeOptions options) =>
        BuildBare(
            options.Id,
            // Phase 692 — the category wrapper is gone; the kind factories are flat.
            // NodeKind's declaring type is Fuaran.UI.Generated since the 4b swap
            // (the Types.fs name is an erased abbreviation, invisible to C#).
            FsGen.NodeKind<object>.NewBadge(new FsGen.BadgeSpec(options.Label.Inner, options.Variant.ToFs())));

    /// <summary>A sparkline. (Built bare — no F# smart ctor.)</summary>
    public static FuaranNode Sparkline(SparklineOptions options) =>
        BuildBare(
            options.Id,
            FsGen.NodeKind<object>.NewSparkline(
                new FsGen.SparklineSpec(
                    // Generated SparklineSpec.Source binds an F# `float list` — re-type the
                    // facade's IEnumerable binding at the seam.
                    Fs.MapBinding(
                        (options.Source ?? Binding.Static(Enumerable.Empty<double>())).Inner,
                        xs => Fs.List(xs)))));

    // Phase 459 — Spacer retired: inter-child space is now a Box layout `Gap`
    // (see BoxOptions), not a node.

    /// <summary>A callout / banner.</summary>
    public static FuaranNode Callout(CalloutOptions options) =>
        new(FsFactory.callout<object>(
            options.Id,
            // Generated CalloutSpec ctor is Generated.fs declaration order (Body,
            // Dismissable, Tone, Heading, Icon), not the old hand order.
            new FsGen.CalloutSpec(
                options.Body.Inner,
                options.Dismissable,
                options.Tone.ToFs(),
                options.Heading is { } h ? Fs.Some(h.Inner) : Fs.None<FsGen.TextSource>(),
                Icon(options.Icon))));

    /// <summary>A progress indicator.</summary>
    public static FuaranNode Progress(ProgressOptions options) =>
        new(FsFactory.progress<object>(
            options.Id,
            // Generated ProgressSpec ctor is Generated.fs declaration order (Fraction,
            // Indeterminate, Tone, Label, Caveat), not the old hand order.
            new FsGen.ProgressSpec(
                (options.Fraction ?? 0.0).Inner,
                options.Indeterminate,
                options.Tone.ToFs(),
                options.Label is { } l ? Fs.Some(l.Inner) : Fs.None<FsGen.TextSource>(),
                options.Caveat is { } c ? Fs.Some(c.Inner) : Fs.None<FsGen.TextSource>())));

    /// <summary>A loading-skeleton placeholder of <c>Rows</c> rows.</summary>
    public static FuaranNode Skeleton(SkeletonOptions options) =>
        new(FsFactory.skeleton<object>(options.Id, options.Rows));

    /// <summary>A standalone icon (Phase 821) — decorative by default
    /// (no <c>Label</c> → <c>aria-hidden</c>); meaningful with a <c>Label</c>
    /// (<c>role="img"</c> + <c>aria-label</c>).</summary>
    public static FuaranNode Icon(IconOptions options) =>
        new(FsFactory.iconSpec<object>(
            options.Id,
            // Generated IconSpec ctor is Generated.fs declaration order
            // (Icon, Size, Tone, Label).
            new FsGen.IconSpec(
                options.Icon,
                options.Size.ToFs(),
                options.Tone.ToFs(),
                Icon(options.Label))));

    /// <summary>A single label-left / value-right row.</summary>
    public static FuaranNode LabelValueRow(LabelValueRowOptions options) =>
        new(FsFactory.labelValueRow<object>(
            options.Id,
            // Generated LabelValueRowSpec ctor is Generated.fs declaration order
            // (Emphasis, Format, Label, Value, Help) — Emphasis leads, not Label.
            new FsGen.LabelValueRowSpec(
                options.Emphasis,
                options.Format.Inner,
                options.Label.Inner,
                options.Value.Inner,
                options.Help is { } h ? Fs.Some(h.Inner) : Fs.None<FsGen.TextSource>())));

    /// <summary>A labeled TEXT fact tile — the complementary kind to the numeric
    /// <see cref="Fuaran.Metric"/> (0.2.0 wave: text values belong in Fact, not a
    /// Metric's numeric value slot).</summary>
    public static FuaranNode Fact(FactOptions options) =>
        new(FsFactory.factSpec<object>(
            options.Id,
            // Generated FactSpec ctor is Generated.fs declaration order (Emphasis, Help,
            // Icon, Label, Tone, Value), not the old hand order.
            new FsGen.FactSpec(
                options.Emphasis,
                options.Help is { } h ? Fs.Some(h.Inner) : Fs.None<FsGen.TextSource>(),
                Icon(options.Icon),
                options.Label.Inner,
                options.Tone.ToFs(),
                options.Value.Inner)));

    /// <summary>A crawlable hyperlink (a real <c>&lt;a href&gt;</c>).</summary>
    public static FuaranNode Link(LinkOptions options) =>
        new(FsFactory.linkSpec<object>(
            options.Id,
            // Generated LinkSpec ctor is Generated.fs declaration order (Href, Label,
            // Download, Rel, Target) — Download moved ahead of Rel/Target.
            new FsGen.LinkSpec(
                options.Href.Inner,
                options.Label.Inner,
                options.Download,
                Fs.OptStr(options.Rel),
                Fs.OptStr(options.Target),
                // Phase 812 — email protection stays opt-in via the F# surface for
                // now; the veneer emits the unprotected default.
                Microsoft.FSharp.Core.FSharpOption<FsGen.LinkProtection>.None)));

    /// <summary>A standalone image.</summary>
    public static FuaranNode Image(ImageOptions options) =>
        new(FsFactory.imageSpec<object>(
            options.Id,
            // Generated ImageSpec ctor is Generated.fs declaration order (Alt, Src,
            // Variant) — Alt leads, the old hand order led with Src.
            new FsGen.ImageSpec(options.Alt.Inner, options.Src.Inner, options.Variant.ToFs())));

    /// <summary>A structured item list.</summary>
    public static FuaranNode List(ListOptions options) =>
        new(FsFactory.listSpec<object>(
            options.Id,
            new FsGen.ListSpec(
                Fs.List((options.Items ?? Enumerable.Empty<Text>()).Select(t => t.Inner)),
                options.Ordered)));

    /// <summary>A separator — a plain horizontal rule (Phase 459: the retired
    /// <c>Divider</c>, now a <c>Box</c> <c>Separator</c> role). For a labelled or
    /// vertical separator, author a <see cref="Fuaran.Box"/> with
    /// <c>Role = Separator</c> directly.</summary>
    public static FuaranNode Divider(DividerOptions options) => new(FsFactory.divider<object>(options.Id));

    /// <summary>A transient in-tree toast notification.</summary>
    public static FuaranNode Toast(ToastOptions options) =>
        new(FsFactory.toast<object>(
            options.Id,
            // Generated ToastSpec ctor is Generated.fs declaration order (Dismissable,
            // Message, Open, Tone), not the old hand order.
            new FsGen.ToastSpec(options.Dismissable, options.Message.Inner, (options.Open ?? false).Inner, options.Tone.ToFs())));

    /// <summary>A deterministic code block.</summary>
    public static FuaranNode CodeBlock(CodeBlockOptions options) =>
        new(FsFactory.codeBlockSpec<object>(
            options.Id,
            // Generated CodeBlockSpec ctor is Generated.fs declaration order (Code,
            // Copyable, HighlightLines, Language, LineNumbers), not the old hand order.
            new FsGen.CodeBlockSpec(
                options.Code,
                options.Copyable,
                Fs.List(options.HighlightLines ?? Enumerable.Empty<int>()),
                options.Language,
                options.LineNumbers)));

    /// <summary>A LaTeX math block.</summary>
    public static FuaranNode Math(MathOptions options) =>
        new(FsFactory.mathSpec<object>(
            options.Id,
            new FsGen.MathSpec(options.Source, options.Display.ToFs())));

    /// <summary>A bounded, typed vector-graphics drawing (Phase 524) — the shared
    /// render target charts lower to and the substrate for maps/diagrams. The
    /// closed <c>Shape</c> DU carries no raw SVG. This low-level veneer takes a
    /// viewBox + F# <c>Shape</c> values; the root style defaults to inherited.</summary>
    public static FuaranNode Drawing(DrawingOptions options) =>
        new(FsFactory.drawingSpec<object>(
            options.Id,
            // Generated DrawingSpec ctor is Generated.fs declaration order (Description,
            // Shapes, Style, Title, ViewBox), not the old hand order (ViewBox-first).
            new FsGen.DrawingSpec(
                options.Description is { } dsc ? Fs.Some(dsc.Inner) : Fs.None<FsGen.TextSource>(),
                Fs.List(options.Shapes ?? Enumerable.Empty<FsGen.Shape>()),
                // Generated DrawStyle declaration order: Emphasis, Fill, FontFamily, FontSize,
                // MarkId (Phase 642 — inherited default: none), Opacity, Rotation (Phase 877 —
                // Label text rotation in degrees; inherited default: upright), Stroke,
                // StrokeWidth, TextAnchor, Tip (Phase 883 — the hover readout emitted as an SVG
                // <title> child; inherited default: untipped).
                new FsGen.DrawStyle(
                    Fs.None<FsGen.Emphasis>(),
                    Fs.None<global::Fuaran.UI.Generated.Binding<string>>(),
                    Fs.None<string>(),
                    Fs.None<double>(),
                    Fs.None<string>(),
                    Fs.None<global::Fuaran.UI.Generated.Binding<double>>(),
                    Fs.None<double>(),
                    Fs.None<global::Fuaran.UI.Generated.Binding<string>>(),
                    Fs.None<global::Fuaran.UI.Generated.Binding<double>>(),
                    Fs.None<FsGen.TextAnchor>(),
                    Fs.None<FsGen.TextSource>()),
                options.Title is { } t ? Fs.Some(t.Inner) : Fs.None<FsGen.TextSource>(),
                // Generated ViewBox declares (Height, MinX, MinY, Width) — ctor is declaration order.
                new FsGen.ViewBox(options.Height, options.MinX, options.MinY, options.Width))));
}

/// <summary>Options for <see cref="Fuaran.Badge"/>.</summary>
public sealed record BadgeOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The badge label.</summary>
    public required Text Label { get; init; }

    /// <summary>The badge variant (default neutral).</summary>
    public BadgeVariant Variant { get; init; } = BadgeVariant.Neutral;
}

/// <summary>Options for <see cref="Fuaran.Sparkline"/>.</summary>
public sealed record SparklineOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The bound data series.</summary>
    public Binding<IEnumerable<double>>? Source { get; init; }
}

/// <summary>Options for <see cref="Fuaran.Callout"/>.</summary>
public sealed record CalloutOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The callout body.</summary>
    public required Text Body { get; init; }

    /// <summary>An optional heading.</summary>
    public Text? Heading { get; init; }

    /// <summary>Semantic tone (default info).</summary>
    public Tone Tone { get; init; } = Tone.Info;

    /// <summary>An optional leading icon name.</summary>
    public string? Icon { get; init; }

    /// <summary>Whether a dismiss affordance is shown.</summary>
    public bool Dismissable { get; init; }
}

/// <summary>Options for <see cref="Fuaran.Progress"/>.</summary>
public sealed record ProgressOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The completion fraction 0..1 (bound; default 0).</summary>
    public Binding<double>? Fraction { get; init; }

    /// <summary>An optional label.</summary>
    public Text? Label { get; init; }

    /// <summary>An optional caveat (for indeterminate progress).</summary>
    public Text? Caveat { get; init; }

    /// <summary>Whether progress is indeterminate.</summary>
    public bool Indeterminate { get; init; }

    /// <summary>Semantic tone (default default).</summary>
    public Tone Tone { get; init; } = Tone.Default;
}

/// <summary>Options for <see cref="Fuaran.Icon"/>.</summary>
public sealed record IconOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The glyph name from the icon vocabulary (the <c>data-icon</c> hook).</summary>
    public required string Icon { get; init; }

    /// <summary>Display size (default <see cref="IconSize.Medium"/>).</summary>
    public IconSize Size { get; init; } = IconSize.Medium;

    /// <summary>Tone (default <see cref="Tone.Default"/>).</summary>
    public Tone Tone { get; init; } = Tone.Default;

    /// <summary>Accessible label — omit for a decorative icon.</summary>
    public string? Label { get; init; }
}

/// <summary>Options for <see cref="Fuaran.Skeleton"/>.</summary>
public sealed record SkeletonOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The number of skeleton rows (default 3).</summary>
    public int Rows { get; init; } = 3;
}

/// <summary>Options for <see cref="Fuaran.LabelValueRow"/>.</summary>
public sealed record LabelValueRowOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The row label.</summary>
    public required Text Label { get; init; }

    /// <summary>The row value (bound).</summary>
    public required Binding<double> Value { get; init; }

    /// <summary>Value display format.</summary>
    public CellFormat Format { get; init; } = CellFormat.None;

    /// <summary>Whether the row is emphasised (a total / highlight).</summary>
    public bool Emphasis { get; init; }

    /// <summary>Optional help text under the label.</summary>
    public Text? Help { get; init; }
}

/// <summary>Options for <see cref="Fuaran.Fact"/>.</summary>
public sealed record FactOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The fact label ("Patient").</summary>
    public required Text Label { get; init; }

    /// <summary>The fact's TEXT value ("Alice Smith") — literal, bound, or i18n.</summary>
    public required Text Value { get; init; }

    /// <summary>Optional leading icon name.</summary>
    public string? Icon { get; init; }

    /// <summary>Semantic tone (default neutral).</summary>
    public Tone Tone { get; init; } = Tone.Default;

    /// <summary>Whether the fact is emphasised.</summary>
    public bool Emphasis { get; init; }

    /// <summary>Optional help text under the label.</summary>
    public Text? Help { get; init; }
}

/// <summary>Options for <see cref="Fuaran.Link"/>.</summary>
public sealed record LinkOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The destination href (bound or literal).</summary>
    public required Binding<string> Href { get; init; }

    /// <summary>The link label.</summary>
    public required Text Label { get; init; }

    /// <summary>Optional <c>rel</c> attribute.</summary>
    public string? Rel { get; init; }

    /// <summary>Optional <c>target</c> attribute.</summary>
    public string? Target { get; init; }

    /// <summary>Whether to emit a <c>download</c> attribute.</summary>
    public bool Download { get; init; }
}

/// <summary>Options for <see cref="Fuaran.Image"/>.</summary>
public sealed record ImageOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The image source (bound or literal).</summary>
    public required Binding<string> Src { get; init; }

    /// <summary>The alt text (the accessibility floor — mandatory).</summary>
    public required Text Alt { get; init; }

    /// <summary>The presentation variant (default plain).</summary>
    public ImageVariant Variant { get; init; } = ImageVariant.Default;
}

/// <summary>Options for <see cref="Fuaran.List"/>.</summary>
public sealed record ListOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The list items.</summary>
    public IEnumerable<Text>? Items { get; init; }

    /// <summary>Whether the list is ordered (<c>&lt;ol&gt;</c>).</summary>
    public bool Ordered { get; init; }
}

/// <summary>Options for <see cref="Fuaran.Divider"/>.</summary>
public sealed record DividerOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The rule orientation (default horizontal).</summary>
    public Orientation Orientation { get; init; } = Orientation.Horizontal;

    /// <summary>An optional centred caption.</summary>
    public Text? Label { get; init; }
}

/// <summary>Options for <see cref="Fuaran.Toast"/>.</summary>
public sealed record ToastOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The toast message.</summary>
    public required Text Message { get; init; }

    /// <summary>Semantic tone (default info).</summary>
    public Tone Tone { get; init; } = Tone.Info;

    /// <summary>The controlled visibility (bound; default hidden).</summary>
    public Binding<bool>? Open { get; init; }

    /// <summary>Whether a dismiss affordance is shown (default true).</summary>
    public bool Dismissable { get; init; } = true;
}

/// <summary>Options for <see cref="Fuaran.CodeBlock"/>.</summary>
public sealed record CodeBlockOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The raw source code.</summary>
    public required string Code { get; init; }

    /// <summary>The language hint (default "text").</summary>
    public string Language { get; init; } = "text";

    /// <summary>Whether to show a line-number gutter.</summary>
    public bool LineNumbers { get; init; }

    /// <summary>1-based line numbers to highlight.</summary>
    public IEnumerable<int>? HighlightLines { get; init; }

    /// <summary>Whether to show a copy affordance (default true).</summary>
    public bool Copyable { get; init; } = true;
}

/// <summary>Options for <see cref="Fuaran.Math"/>.</summary>
public sealed record MathOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The LaTeX source.</summary>
    public required string Source { get; init; }

    /// <summary>Inline or block display (default block).</summary>
    public MathDisplay Display { get; init; } = MathDisplay.Block;
}

/// <summary>Options for <see cref="Fuaran.Drawing"/> (Phase 524).</summary>
public sealed record DrawingOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>ViewBox min-x (user-space coordinate origin).</summary>
    public double MinX { get; init; }

    /// <summary>ViewBox min-y (user-space coordinate origin).</summary>
    public double MinY { get; init; }

    /// <summary>ViewBox width (user-space; default 100).</summary>
    public double Width { get; init; } = 100.0;

    /// <summary>ViewBox height (user-space; default 100).</summary>
    public double Height { get; init; } = 100.0;

    /// <summary>The ordered draw list (F# <c>Shape</c> DU values); empty when null.</summary>
    public IEnumerable<FsGen.Shape>? Shapes { get; init; }

    /// <summary>Optional accessible name (rendered as <c>&lt;title&gt;</c> in Phase 525).</summary>
    public Text? Title { get; init; }

    /// <summary>Optional long description (rendered as <c>&lt;desc&gt;</c> in Phase 525).</summary>
    public Text? Description { get; init; }
}
