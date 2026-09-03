using System.Collections.Generic;

namespace Fuaran.UI.CSharp;

// ============================================================================
//  Options records — one per node kind. `required` init-only members make a
//  missing mandatory field a COMPILE error ("you forgot Id / Label"); optional
//  members default so the author writes only what differs (the same
//  emit-only-what-differs discipline the F# `Defaults.X` records give).
//  C# 12 collection expressions author `Children = [a, b]`.
//
//  Foundation set (Phase 304); Phase 305 adds the remaining kinds' records.
// ============================================================================

// ─── Layout ─────────────────────────────────────────────────────────────────

/// <summary>Options for <see cref="Fuaran.Dashboard"/>.</summary>
public sealed record DashboardOptions
{
    /// <summary>The node id (must be unique within the tree).</summary>
    public required string Id { get; init; }

    /// <summary>The child nodes.</summary>
    public IEnumerable<FuaranNode>? Children { get; init; }
}

/// <summary>Options for <see cref="Fuaran.Stack"/>.</summary>
public sealed record StackOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>Stacking axis (default vertical).</summary>
    public Orientation Orientation { get; init; } = Orientation.Vertical;

    /// <summary>Whether children wrap at narrow widths.</summary>
    public bool Wrap { get; init; }

    /// <summary>The child nodes.</summary>
    public IEnumerable<FuaranNode>? Children { get; init; }
}

/// <summary>Options for <see cref="Fuaran.Grid"/>.</summary>
public sealed record GridOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>Column count (default 12).</summary>
    public int Cols { get; init; } = 12;

    /// <summary>The child nodes.</summary>
    public IEnumerable<FuaranNode>? Children { get; init; }
}

/// <summary>Options for <see cref="Fuaran.Card"/>.</summary>
public sealed record CardOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>An optional card heading.</summary>
    public Text? Heading { get; init; }

    /// <summary>The child nodes.</summary>
    public IEnumerable<FuaranNode>? Children { get; init; }
}

/// <summary>Layout mode for <see cref="Fuaran.Box"/> (Phase 390).</summary>
public enum BoxLayoutMode
{
    /// <summary>Flex flow (the retired Stack) — uses <c>Orientation</c> + <c>Wrap</c>.</summary>
    Flex,
    /// <summary>Explicit grid (the retired GridLayout) — uses <c>Cols</c>.</summary>
    Grid,
    /// <summary>Responsive auto-tile (the retired Dashboard).</summary>
    Auto,
}

/// <summary>Semantic role for <see cref="Fuaran.Box"/> (Phase 390) — drives the
/// emitted element, ARIA landmark, and <c>fuaran-*</c> chrome.</summary>
public enum BoxRoleKind
{
    /// <summary>Plain grouping container (the retired Stack / GridLayout default).</summary>
    Group,
    /// <summary>Card chrome with optional heading (the retired Card).</summary>
    Card,
    /// <summary>Dashboard region landmark (the retired Dashboard).</summary>
    Dashboard,
    /// <summary>Separator (<c>&lt;hr&gt;</c>) — reserved; no children.</summary>
    Separator,
}

/// <summary>Options for <see cref="Fuaran.Box"/> — the unified container
/// (Phase 390). <see cref="Fuaran.Stack"/> / <see cref="Fuaran.Grid"/> /
/// <see cref="Fuaran.Dashboard"/> / <see cref="Fuaran.Card"/> remain as
/// Box-emitting conveniences over this.</summary>
public sealed record BoxOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>Layout mode (default Flex).</summary>
    public BoxLayoutMode Layout { get; init; } = BoxLayoutMode.Flex;

    /// <summary>Semantic role (default Group).</summary>
    public BoxRoleKind Role { get; init; } = BoxRoleKind.Group;

    /// <summary>Flex axis (default vertical) — used when <c>Layout = Flex</c>.</summary>
    public Orientation Orientation { get; init; } = Orientation.Vertical;

    /// <summary>Flex wrap at narrow widths — used when <c>Layout = Flex</c>.</summary>
    public bool Wrap { get; init; }

    /// <summary>Grid column count (default 12) — used when <c>Layout = Grid</c>.</summary>
    public int Cols { get; init; } = 12;

    /// <summary>Optional container heading (typically for <c>Role = Card</c>).</summary>
    public Text? Heading { get; init; }

    /// <summary>The child nodes.</summary>
    public IEnumerable<FuaranNode>? Children { get; init; }
}

// ─── Display ────────────────────────────────────────────────────────────────

/// <summary>Options for <see cref="Fuaran.Metric"/>.</summary>
public sealed record MetricOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The metric label.</summary>
    public required Text Label { get; init; }

    /// <summary>The metric value (bind a literal or a data source).</summary>
    public required Binding<double> Value { get; init; }

    /// <summary>Display format for the value.</summary>
    public CellFormat Format { get; init; } = CellFormat.None;

    /// <summary>Semantic tone.</summary>
    public Tone Tone { get; init; } = Tone.Default;

    /// <summary>Density weight.</summary>
    public Weight Weight { get; init; } = Weight.Standard;

    /// <summary>Visual emphasis.</summary>
    public Emphasis Emphasis { get; init; } = Emphasis.Normal;

    /// <summary>An optional trend value.</summary>
    public Binding<double>? Trend { get; init; }

    /// <summary>Display format for the trend value.</summary>
    public CellFormat? TrendFormat { get; init; }

    /// <summary>
    /// Which DIRECTION of movement is good (Phase 867). The default,
    /// <see cref="TrendPolarity.HigherIsBetter"/>, is omitted on the wire.
    /// <see cref="TrendPolarity.LowerIsBetter"/> inverts the SENTIMENT only —
    /// sentiment = sign(trend) × polarity — so a falling error rate reads as an
    /// improvement while the numeric text and its sign are untouched. This is
    /// NOT <see cref="Tone"/>: tone says how the metric stands NOW, polarity
    /// says which way is better. One slot could never have said both.
    /// </summary>
    public TrendPolarity TrendPolarity { get; init; } = TrendPolarity.HigherIsBetter;

    /// <summary>An optional leading icon name.</summary>
    public string? Icon { get; init; }

    /// <summary>Optional supporting subtext.</summary>
    public Text? Subtext { get; init; }
}

/// <summary>Options for <see cref="Fuaran.Heading"/>.</summary>
public sealed record HeadingOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The heading text.</summary>
    public required Text Text { get; init; }

    /// <summary>The heading level 1..6 (default 2).</summary>
    public int Level { get; init; } = 2;

    /// <summary>The heading variant (default standard).</summary>
    public HeadingVariant Variant { get; init; } = HeadingVariant.Standard;
}

/// <summary>Options for <see cref="Fuaran.Markdown"/>.</summary>
public sealed record MarkdownOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The markdown source text.</summary>
    public required Text Text { get; init; }
}

// ─── Input ──────────────────────────────────────────────────────────────────

/// <summary>Options for <see cref="Fuaran.Button"/>.</summary>
public sealed record ButtonOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The button label.</summary>
    public required Text Label { get; init; }

    /// <summary>The button variant (default secondary — matching the F# default).</summary>
    public ButtonVariant Variant { get; init; } = ButtonVariant.Secondary;

    /// <summary>An optional leading icon name.</summary>
    public string? Icon { get; init; }

    /// <summary>
    /// What the button raises when clicked (Phase 1153). Unset is an empty chain —
    /// a button that renders and raises nothing, which is the shape this veneer
    /// authored before the <see cref="FuaranAction"/> vocabulary existed and is
    /// therefore byte-unchanged for an author who does not set it.
    /// </summary>
    public FuaranAction? OnClick { get; init; }
}
