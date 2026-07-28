using System.Collections.Generic;
using System.Linq;
using FsFactory = global::Fuaran.UI.Fuaran;
using FsTypes = Fuaran.UI.Types;
using FsGen = Fuaran.UI.Generated;
using FsNode = Fuaran.UI.Types.Node<object>;

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

    internal static Microsoft.FSharp.Core.FSharpOption<FsTypes.IconSource> Icon(string? icon) =>
        icon is null
            ? Microsoft.FSharp.Core.FSharpOption<FsTypes.IconSource>.None
            : Microsoft.FSharp.Core.FSharpOption<FsTypes.IconSource>.Some(FsTypes.IconSource.NewIconSource(icon));

    // The three display kinds without an F# smart constructor (Badge / Sparkline /
    // Spacer) are built directly here with the same defaults `buildNode` applies —
    // Accessibility=None (their smart-ctor default would be `none` anyway), so the
    // output is byte-identical to what a smart ctor would produce (and to the bare
    // corpus). Every other kind couples to its smart ctor.
    internal static FuaranNode BuildBare(string id, FsTypes.NodeKind<object> kind) =>
        new(new FsNode(
            FsTypes.NodeId.NewNodeId(id),
            kind,
            new FsTypes.StateBehaviour<object>(
                Fs.None<FsNode>(),
                Fs.None<FsNode>(),
                Fs.None<Microsoft.FSharp.Core.FSharpFunc<global::Fuaran.UI.HostPrelude.ErrorPayload, FsNode>>()),
            new FsTypes.SemanticStyle(
                FsGen.ToneVariant.Default,
                FsGen.StyleWeight.Standard,
                FsGen.Emphasis.Normal,
                FsGen.StyleRole.None,
                FsGen.FontVoice.Default),
            Fs.None<FsTypes.Accessibility>(),
            Fs.None<FsGen.Motion>(),
            Fs.None<Microsoft.FSharp.Collections.FSharpMap<string, string>>()));

    // ─── Layout ─────────────────────────────────────────────────────────────

    /// <summary>The unified container (Phase 390) — layout mode + semantic role.
    /// <see cref="Stack"/> / <see cref="Grid"/> / <see cref="Dashboard"/> /
    /// <see cref="Card"/> are Box-emitting conveniences over this.</summary>
    public static FuaranNode Box(BoxOptions options)
    {
        FsTypes.BoxLayout layout = options.Layout switch
        {
            BoxLayoutMode.Grid =>
                FsTypes.BoxLayout.NewGrid(new FsTypes.GridTemplate(options.Cols, Fs.None<string>(), Fs.None<int>())),
            BoxLayoutMode.Auto => FsTypes.BoxLayout.Auto,
            _ => FsTypes.BoxLayout.NewFlex(new FsTypes.FlexLayout(options.Orientation.ToFs(), options.Wrap, Fs.None<int>())),
        };

        FsGen.BoxRole role = options.Role switch
        {
            BoxRoleKind.Card => FsGen.BoxRole.Card,
            BoxRoleKind.Dashboard => FsGen.BoxRole.Dashboard,
            BoxRoleKind.Separator => FsGen.BoxRole.Separator,
            _ => FsGen.BoxRole.Group,
        };

        var heading = options.Heading is { } h ? Fs.Some(h.Inner) : Fs.None<FsGen.TextSource>();

        return new(FsFactory.box<object>(
            options.Id,
            new FsTypes.BoxSpec<object>(layout, role, heading, Kids(options.Children))));
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
            new FsTypes.MetricSpec(
                options.Label.Inner,
                options.Value.Inner,
                options.Format.Inner,
                options.Tone.ToFs(),
                options.Weight.ToFs(),
                options.Emphasis.ToFs(),
                options.Trend is { } t ? Fs.Some(t.Inner) : Fs.None<global::Fuaran.UI.Generated.Binding<double>>(),
                options.TrendFormat is { } tf ? Fs.Some(tf.Inner) : Fs.None<FsGen.CellFormat>(),
                Icon(options.Icon),
                options.Subtext is { } s ? Fs.Some(s.Inner) : Fs.None<FsGen.TextSource>())));

    /// <summary>A heading (<c>&lt;h1&gt;</c>…<c>&lt;h6&gt;</c>).</summary>
    public static FuaranNode Heading(HeadingOptions options) =>
        new(FsFactory.heading<object>(
            options.Id,
            new FsTypes.HeadingSpec(options.Level, options.Text.Inner, options.Variant.ToFs())));

    /// <summary>A markdown block.</summary>
    public static FuaranNode Markdown(MarkdownOptions options) =>
        new(FsFactory.markdownSpec<object>(options.Id, new FsTypes.MarkdownSpec(options.Text.Inner)));

    // ─── Input ────────────────────────────────────────────────────────────────

    /// <summary>A button. The <c>OnClick</c> handler is opaque to the wire; author intent survives via the tree shape.</summary>
    public static FuaranNode Button(ButtonOptions options) =>
        new(FsFactory.button<object>(
            options.Id,
            new FsTypes.ButtonSpec<object>(
                options.Label.Inner,
                global::Fuaran.UI.Generated.Action<object>.NewChain(Fs.Empty<global::Fuaran.UI.Generated.Action<object>>()),
                options.Variant.ToFs(),
                Icon(options.Icon),
                Fs.None<FsGen.TextSource>(),
                Fs.None<global::Fuaran.UI.Generated.Binding<bool>>())));
}
