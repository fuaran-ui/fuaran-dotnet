using System.Collections.Generic;
using FsFactory = global::Fuaran.UI.Fuaran;
using FsTypes = Fuaran.UI.Types;
using FsAction = Fuaran.UI.Types.Action<object>;

namespace Fuaran.UI.CSharp;

// Phase 305 — the remaining Layout kinds (Card / Stack / Grid / Dashboard ship in
// the foundation). Each couples to its F# smart constructor, inheriting Defaults +
// ARIA. Handler slots (onSelect / onToggle / onDismiss) are opaque to the wire, so
// they default to no-ops here — author intent survives via the tree shape + bound
// state (activeIndex / open), which do ride the wire.
public static partial class Fuaran
{
    private static FsAction NoAction { get; } = FsAction.NewChain(Fs.Empty<FsAction>());

    private static Microsoft.FSharp.Core.FSharpFunc<T, FsAction> NoHandler<T>() =>
        Fs.Func<T, FsAction>(_ => NoAction);

    /// <summary>A split panel — two regions sized by <c>Weight</c>.</summary>
    public static FuaranNode SplitPanel(SplitPanelOptions options) =>
        new(FsFactory.splitPanel<object>(
            options.Id,
            new FsTypes.SplitPanelSpec<object>(options.Weight, Kids(options.Children))));

    /// <summary>A tab group. The active tab is driven by <c>ActiveIndex</c> (a bound value that rides the wire).</summary>
    public static FuaranNode Tabs(TabsOptions options) =>
        new(FsFactory.tabs<object>(
            options.Id,
            new FsTypes.TabsSpec<object>(
                options.Orientation.ToFs(),
                Kids(options.Children),
                (options.ActiveIndex ?? 0).Inner,
                NoHandler<int>(),
                Fs.None<Microsoft.FSharp.Collections.FSharpList<FsTypes.TabHeader>>(),
                Fs.None<Microsoft.FSharp.Collections.FSharpList<string>>(),
                Fs.None<FsTypes.Binding<string>>(),
                Fs.None<Microsoft.FSharp.Core.FSharpFunc<string, FsAction>>())));

    /// <summary>A stepper. The active step is driven by <c>ActiveStep</c>.</summary>
    public static FuaranNode Stepper(StepperOptions options) =>
        new(FsFactory.stepper<object>(
            options.Id,
            new FsTypes.StepperSpec<object>((options.ActiveStep ?? 0).Inner, Kids(options.Children), NoHandler<int>())));

    /// <summary>A single-card list of label/value rows.</summary>
    public static FuaranNode SummaryList(SummaryListOptions options) =>
        new(FsFactory.summaryList<object>(
            options.Id,
            new FsTypes.SummaryListSpec<object>(
                options.Heading is { } h ? Fs.Some(h.Inner) : Fs.None<FsTypes.TextSource>(),
                Kids(options.Children))));

    /// <summary>A collapsible disclosure (<c>&lt;details&gt;</c>).</summary>
    public static FuaranNode Disclosure(DisclosureOptions options) =>
        new(FsFactory.disclosure<object>(
            options.Id,
            new FsTypes.DisclosureSpec<object>(
                options.Heading.Inner,
                (options.Open ?? false).Inner,
                Fs.Func<bool, FsAction>(_ => NoAction),
                Kids(options.Children),
                options.DefaultOpen)));

    /// <summary>An out-of-flow modal dialog.</summary>
    public static FuaranNode Modal(ModalOptions options) =>
        new(FsFactory.modal<object>(
            options.Id,
            new FsTypes.ModalSpec<object>(
                (options.Open ?? false).Inner,
                options.Heading is { } h ? Fs.Some(h.Inner) : Fs.None<FsTypes.TextSource>(),
                options.Dismissable,
                Kids(options.Children),
                NoAction)));

    /// <summary>An overflow / scroll container.</summary>
    public static FuaranNode ScrollArea(ScrollAreaOptions options) =>
        new(FsFactory.scrollArea<object>(
            options.Id,
            new FsTypes.ScrollAreaSpec<object>(
                options.Orientation.ToFs(),
                Kids(options.Children),
                Fs.OfNullable(options.MaxHeight),
                Fs.OfNullable(options.MaxWidth))));
}

/// <summary>Options for <see cref="Fuaran.SplitPanel"/>.</summary>
public sealed record SplitPanelOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The first region's weight (0..1); default 0.5.</summary>
    public double Weight { get; init; } = 0.5;

    /// <summary>The child nodes.</summary>
    public IEnumerable<FuaranNode>? Children { get; init; }
}

/// <summary>Options for <see cref="Fuaran.Tabs"/>.</summary>
public sealed record TabsOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>Tab-bar orientation (default horizontal).</summary>
    public Orientation Orientation { get; init; } = Orientation.Horizontal;

    /// <summary>The active-tab index (bound; default 0).</summary>
    public Binding<int>? ActiveIndex { get; init; }

    /// <summary>The tab bodies.</summary>
    public IEnumerable<FuaranNode>? Children { get; init; }

    // FORWARD-COUPLING TRIPWIRE: this options record intentionally exposes NO explicit
    // TabHeaders / TabTags — tab labels are inferred from each child's heading. If you
    // add TabHeaders/TabTags authoring here, you also make the header/tag-vs-children
    // count mismatch authorable, which the F# validator's FUARAN047/048 catch but the
    // veneer analyzer does NOT (deferred in Phase 314/315 because there is no surface
    // today). Port FUARAN047/048 into Fuaran.UI.Analyzers (C# call-site + VB XML) and
    // add tests in the same change — the F# AST validator cannot see veneer code, so
    // an uncaught mismatch would surface only as a broken tab strip at runtime.
}

/// <summary>Options for <see cref="Fuaran.Stepper"/>.</summary>
public sealed record StepperOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The active-step index (bound; default 0).</summary>
    public Binding<int>? ActiveStep { get; init; }

    /// <summary>The step bodies.</summary>
    public IEnumerable<FuaranNode>? Children { get; init; }
}

/// <summary>Options for <see cref="Fuaran.SummaryList"/>.</summary>
public sealed record SummaryListOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>An optional section heading.</summary>
    public Text? Heading { get; init; }

    /// <summary>The rows.</summary>
    public IEnumerable<FuaranNode>? Children { get; init; }
}

/// <summary>Options for <see cref="Fuaran.Disclosure"/>.</summary>
public sealed record DisclosureOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The disclosure summary heading.</summary>
    public required Text Heading { get; init; }

    /// <summary>The controlled open state (bound; default closed).</summary>
    public Binding<bool>? Open { get; init; }

    /// <summary>The initial-mount open value.</summary>
    public bool DefaultOpen { get; init; }

    /// <summary>The disclosed content.</summary>
    public IEnumerable<FuaranNode>? Children { get; init; }
}

/// <summary>Options for <see cref="Fuaran.Modal"/>.</summary>
public sealed record ModalOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The controlled visibility (bound; default hidden).</summary>
    public Binding<bool>? Open { get; init; }

    /// <summary>An optional dialog heading.</summary>
    public Text? Heading { get; init; }

    /// <summary>Whether a dismiss affordance is shown (default true).</summary>
    public bool Dismissable { get; init; } = true;

    /// <summary>The dialog content.</summary>
    public IEnumerable<FuaranNode>? Children { get; init; }
}

/// <summary>Options for <see cref="Fuaran.ScrollArea"/>.</summary>
public sealed record ScrollAreaOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The scroll axis (default vertical).</summary>
    public ScrollOrientation Orientation { get; init; } = ScrollOrientation.Vertical;

    /// <summary>Optional max height in pixels.</summary>
    public int? MaxHeight { get; init; }

    /// <summary>Optional max width in pixels.</summary>
    public int? MaxWidth { get; init; }

    /// <summary>The scrollable content.</summary>
    public IEnumerable<FuaranNode>? Children { get; init; }
}
