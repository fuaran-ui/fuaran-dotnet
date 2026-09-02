using System.Collections.Generic;
using FsFactory = global::Fuaran.UI.Fuaran;
using FsTypes = Fuaran.UI.Types;
using FsGen = Fuaran.UI.Generated;
using FsAction = Fuaran.UI.Generated.Action<object>;

namespace Fuaran.UI.CSharp;

// Phase 305 — the remaining Layout kinds (Card / Stack / Grid / Dashboard ship in
// the foundation). Each couples to its F# smart constructor, inheriting Defaults +
// ARIA. Handler slots (onSelect / onToggle / onDismiss) are opaque to the wire, so
// they default to no-ops here — author intent survives via the tree shape + bound
// state (activeIndex / open), which do ride the wire.
//
// SPEC-CONSTRUCTION-TRIPWIRE — the `new FsGen.<X>(…)` calls below are positional on
// purpose. An additive spec slot lands here as CS7036, at the one site that decides
// whether the veneer exposes it or passes the F# default; that is the mechanism, not
// churn. See src/Fuaran.UI.Tests/SpecConstructionTests.fs ("The C# authoring veneer").
public static partial class Fuaran
{
    private static FsAction NoAction { get; } = FsAction.NewChain(Fs.Empty<FsAction>());

    private static Microsoft.FSharp.Core.FSharpFunc<T, FsAction> NoHandler<T>() =>
        Fs.Func<T, FsAction>(_ => NoAction);

    /// <summary>A split panel — two regions sized by <c>Weight</c>.</summary>
    public static FuaranNode SplitPanel(SplitPanelOptions options) =>
        new(FsFactory.splitPanel<object>(
            options.Id,
            // Generated SplitPanelSpec ctor is Generated.fs declaration order
            // (Children, Weight), not the old Weight-first hand order.
            new FsGen.SplitPanelSpec<object>(Kids(options.Children), options.Weight)));

    /// <summary>A tab group. The active tab is driven by <c>ActiveIndex</c> (a bound value that rides the wire).</summary>
    public static FuaranNode Tabs(TabsOptions options) =>
        new(FsFactory.tabs<object>(
            options.Id,
            // Generated TabsSpec ctor is Generated.fs declaration order (ActiveIndex,
            // Children, Orientation, OnSelect, OnSelectTag, TabHeaders, TabTags,
            // ActiveTag), not the old hand order. `OnSelect = None` since the swap —
            // ≡ the old no-op handler (Phase 426: a bound ActiveIndex gets the
            // clicked index written back by the renderer).
            new FsGen.TabsSpec<object>(
                (options.ActiveIndex ?? 0).Inner,
                Kids(options.Children),
                options.Orientation.ToFs(),
                Fs.None<Microsoft.FSharp.Core.FSharpFunc<int, FsAction>>(),
                Fs.None<Microsoft.FSharp.Core.FSharpFunc<string, FsAction>>(),
                Fs.None<Microsoft.FSharp.Collections.FSharpList<FsGen.TabHeader>>(),
                Fs.None<Microsoft.FSharp.Collections.FSharpList<string>>(),
                Fs.None<global::Fuaran.UI.Generated.Binding<string>>())));

    /// <summary>A stepper. The active step is driven by <c>ActiveStep</c>.</summary>
    public static FuaranNode Stepper(StepperOptions options) =>
        new(FsFactory.stepper<object>(
            options.Id,
            // Generated StepperSpec ctor order (ActiveStep, Children, OnSelect)
            // matches the old hand order; OnSelect is newly optional (None ≡ the
            // old no-op handler).
            new FsGen.StepperSpec<object>(
                (options.ActiveStep ?? 0).Inner,
                Kids(options.Children),
                Fs.None<Microsoft.FSharp.Core.FSharpFunc<int, FsAction>>())));

    /// <summary>A single-card list of label/value rows.</summary>
    public static FuaranNode SummaryList(SummaryListOptions options) =>
        new(FsFactory.summaryList<object>(
            options.Id,
            // Generated SummaryListSpec ctor is Generated.fs declaration order
            // (Children, Heading), not the old Heading-first hand order.
            new FsGen.SummaryListSpec<object>(
                Kids(options.Children),
                options.Heading is { } h ? Fs.Some(h.Inner) : Fs.None<FsGen.TextSource>())));

    /// <summary>A collapsible disclosure (<c>&lt;details&gt;</c>).</summary>
    public static FuaranNode Disclosure(DisclosureOptions options) =>
        new(FsFactory.disclosure<object>(
            options.Id,
            // Generated DisclosureSpec ctor is Generated.fs declaration order
            // (Children, DefaultOpen, Heading, OnToggle, Open), not the old hand
            // order. `OnToggle = None` since the swap — ≡ the old no-op handler
            // (Phase 426: a bound Open gets the new value written back).
            new FsGen.DisclosureSpec<object>(
                Kids(options.Children),
                options.DefaultOpen,
                options.Heading.Inner,
                Fs.None<Microsoft.FSharp.Core.FSharpFunc<bool, FsAction>>(),
                (options.Open ?? false).Inner)));

    /// <summary>An out-of-flow modal dialog. <c>OnDismiss</c> takes a
    /// wire-representable <see cref="FuaranAction"/> (Phase 1153).</summary>
    public static FuaranNode Modal(ModalOptions options) =>
        new(FsFactory.modal<object>(
            options.Id,
            // Generated ModalSpec ctor is Generated.fs declaration order (Children,
            // Dismissable, OnDismiss, Open, Heading), not the old hand order.
            // OnDismiss stays ABSENT when unset rather than becoming an empty chain
            // (Phase 426: a bound Open gets false written back on dismiss, so the
            // slot is genuinely optional) — which is also what keeps this additive
            // on the wire for every author who does not set it.
            new FsGen.ModalSpec<object>(
                Kids(options.Children),
                options.Dismissable,
                options.OnDismiss is { } d ? Fs.Some(d.Inner) : Fs.None<FsAction>(),
                (options.Open ?? false).Inner,
                options.Heading is { } h ? Fs.Some(h.Inner) : Fs.None<FsGen.TextSource>())));

    /// <summary>An overflow / scroll container.</summary>
    public static FuaranNode ScrollArea(ScrollAreaOptions options) =>
        new(FsFactory.scrollArea<object>(
            options.Id,
            // Generated ScrollAreaSpec ctor is Generated.fs declaration order
            // (Children, Orientation, MaxHeight, MaxWidth), not the old
            // Orientation-first hand order.
            new FsGen.ScrollAreaSpec<object>(
                Kids(options.Children),
                options.Orientation.ToFs(),
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

    /// <summary>
    /// What the dialog raises when dismissed (Phase 1153). Unset leaves the slot
    /// ABSENT on the wire — a bound <see cref="Open"/> already gets <c>false</c>
    /// written back, so "no extra action" is the honest default rather than an
    /// empty chain.
    /// </summary>
    public FuaranAction? OnDismiss { get; init; }

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
