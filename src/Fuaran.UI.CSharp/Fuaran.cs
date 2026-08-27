using System.Collections.Generic;
using System.Linq;
using FsFactory = global::Fuaran.UI.Fuaran;
using FsTypes = Fuaran.UI.Types;
using FsGen = Fuaran.UI.Generated;
using FsNode = Fuaran.UI.Generated.Node<object>;

namespace Fuaran.UI.CSharp;

// ============================================================================
//  Fuaran — the C# authoring surface. Static factory + options-object per node
//  kind (matching the TS/Python hosts), each calling the corresponding F# smart
//  constructor (`Fuaran.metric`, …) so the veneer inherits `Defaults` +
//  per-component ARIA by construction — the smart-ctor coupling is an internal
//  detail, never on the public surface.
//
//  This file carries the foundation set (Phase 304). Phase 305 completes the
//  ~40-shape vocabulary under Factories/ by extending this same partial class.
// ============================================================================

/// <summary>The C# authoring entry points for Fuaran UI trees.</summary>
public static partial class Fuaran
{
    // ─── Internal helpers (the FSharp.Core-facing seam stays here) ──────────

    internal static Microsoft.FSharp.Collections.FSharpList<FsNode> Kids(IEnumerable<FuaranNode>? children) =>
        Fs.List((children ?? Enumerable.Empty<FuaranNode>()).Select(c => c.Inner));

    // Phase 692/694 swap: the generated spec records carry `Icon: string option`
    // directly (no `IconSource` wrap).
    internal static Microsoft.FSharp.Core.FSharpOption<string> Icon(string? icon) =>
        icon is null
            ? Microsoft.FSharp.Core.FSharpOption<string>.None
            : Microsoft.FSharp.Core.FSharpOption<string>.Some(icon);

    // The three display kinds without an F# smart constructor (Badge / Sparkline /
    // Spacer) are built directly here with the same defaults `buildNode` applies —
    // Accessibility=None (their smart-ctor default would be `none` anyway), so the
    // output is byte-identical to what a smart ctor would produce (and to the bare
    // corpus). Every other kind couples to its smart ctor.
    // Generated Node ctor is Generated.fs declaration order (Id, Kind,
    // Accessibility, ExtraAttributes, Motion, State, Style); `Id` is a bare
    // string, and `State = None` / `Style = None` are the canonical
    // empty-state / default-style shapes since the swap.
    internal static FuaranNode BuildBare(string id, FsGen.NodeKind<object> kind) =>
        new(new FsNode(
            id,
            kind,
            Fs.None<FsGen.Accessibility>(),
            Fs.None<Microsoft.FSharp.Collections.FSharpMap<string, string>>(),
            Fs.None<FsGen.Motion>(),
            Fs.None<FsGen.StateBehaviour<object>>(),
            Fs.None<FsGen.SemanticStyle>()));

    // ─── Layout ─────────────────────────────────────────────────────────────

    /// <summary>The unified container (Phase 390) — layout mode + semantic role.
    /// <see cref="Stack"/> / <see cref="Grid"/> / <see cref="Dashboard"/> /
    /// <see cref="Card"/> are Box-emitting conveniences over this.</summary>
    public static FuaranNode Box(BoxOptions options)
    {
        // LayoutMode cases are positional since the swap: Flex(direction, wrap, gap)
        // / Grid(cols, templateColumns, gap) — the FlexLayout / GridTemplate
        // payload records are retired.
        FsGen.LayoutMode layout = options.Layout switch
        {
            BoxLayoutMode.Grid => FsGen.LayoutMode.NewGrid(options.Cols, Fs.None<string>(), Fs.None<int>()),
            BoxLayoutMode.Auto => FsGen.LayoutMode.Auto,
            _ => FsGen.LayoutMode.NewFlex(options.Orientation.ToFs(), options.Wrap, Fs.None<int>()),
        };

        FsGen.BoxRole role = options.Role switch
        {
            BoxRoleKind.Card => FsGen.BoxRole.Card,
            BoxRoleKind.Dashboard => FsGen.BoxRole.Dashboard,
            BoxRoleKind.Separator => FsGen.BoxRole.Separator,
            _ => FsGen.BoxRole.Group,
        };

        var heading = options.Heading is { } h ? Fs.Some(h.Inner) : Fs.None<FsGen.TextSource>();

        // Generated BoxSpec ctor is Generated.fs declaration order (Children,
        // Heading, Layout, Role).
        return new(FsFactory.box<object>(
            options.Id,
            new FsGen.BoxSpec<object>(Kids(options.Children), heading, layout, role)));
    }

    /// <summary>A dashboard — the page's primary content region.</summary>
    public static FuaranNode Dashboard(DashboardOptions options) =>
        new(FsFactory.dashboard<object>(options.Id, new FsTypes.DashboardSpec<object>(Kids(options.Children))));

    /// <summary>A vertical (or horizontal) stack of children.</summary>
    public static FuaranNode Stack(StackOptions options) =>
        new(FsFactory.stack<object>(
            options.Id,
            new FsTypes.StackSpec<object>(options.Orientation.ToFs(), Kids(options.Children), options.Wrap)));

    /// <summary>A CSS-grid layout of children.</summary>
    public static FuaranNode Grid(GridOptions options) =>
        new(FsFactory.gridLayout<object>(
            options.Id,
            new FsTypes.GridLayoutSpec<object>(
                options.Cols,
                Kids(options.Children),
                Microsoft.FSharp.Core.FSharpOption<string>.None)));

    /// <summary>A card container with an optional heading.</summary>
    public static FuaranNode Card(CardOptions options) =>
        new(FsFactory.card<object>(
            options.Id,
            new FsTypes.CardSpec<object>(
                options.Heading is { } h ? Fs.Some(h.Inner) : Fs.None<FsGen.TextSource>(),
                Kids(options.Children))));

    // ─── Display ──────────────────────────────────────────────────────────────

    /// <summary>A labelled numeric metric.</summary>
    public static FuaranNode Metric(MetricOptions options) =>
        new(FsFactory.metric<object>(
            options.Id,
            // Generated MetricSpec ctor is Generated.fs declaration order (Label, Value,
            // Format, Tone, Weight, Emphasis, Trend, TrendFormat, TrendPolarity, Icon,
            // Subtext). Phase 867 added `trendPolarity`; a payload FIELD binds neither
            // the C# coverage reflection nor the VB analyzer vocabulary (both pin
            // NodeKind cases), but it DOES bind this positional construction, so the
            // default is passed here and surfacing it on MetricOptions is a deliberate
            // follow-up rather than an omission.
            new FsGen.MetricSpec(
                options.Label.Inner,
                options.Value.Inner,
                options.Format.Inner,
                options.Tone.ToFs(),
                options.Weight.ToFs(),
                options.Emphasis.ToFs(),
                options.Trend is { } t ? Fs.Some(t.Inner) : Fs.None<global::Fuaran.UI.Generated.Binding<double>>(),
                options.TrendFormat is { } tf ? Fs.Some(tf.Inner) : Fs.None<FsGen.CellFormat>(),
                FsGen.TrendPolarity.HigherIsBetter,
                Icon(options.Icon),
                options.Subtext is { } s ? Fs.Some(s.Inner) : Fs.None<FsGen.TextSource>())));

    /// <summary>A heading (<c>&lt;h1&gt;</c>…<c>&lt;h6&gt;</c>).</summary>
    public static FuaranNode Heading(HeadingOptions options) =>
        new(FsFactory.heading<object>(
            options.Id,
            new FsGen.HeadingSpec(options.Level, options.Text.Inner, options.Variant.ToFs())));

    /// <summary>A markdown block.</summary>
    public static FuaranNode Markdown(MarkdownOptions options) =>
        new(FsFactory.markdownSpec<object>(options.Id, new FsGen.MarkdownSpec(options.Text.Inner)));

    // ─── Input ────────────────────────────────────────────────────────────────

    /// <summary>A button. The <c>OnClick</c> handler is opaque to the wire; author intent survives via the tree shape.</summary>
    public static FuaranNode Button(ButtonOptions options) =>
        new(FsFactory.button<object>(
            options.Id,
            // Generated ButtonSpec ctor is Generated.fs declaration order (Label,
            // OnClick, Variant, Icon, Tooltip, Disabled).
            new FsGen.ButtonSpec<object>(
                options.Label.Inner,
                global::Fuaran.UI.Generated.Action<object>.NewChain(Fs.Empty<global::Fuaran.UI.Generated.Action<object>>()),
                options.Variant.ToFs(),
                Icon(options.Icon),
                Fs.None<FsGen.TextSource>(),
                Fs.None<global::Fuaran.UI.Generated.Binding<bool>>())));
}
