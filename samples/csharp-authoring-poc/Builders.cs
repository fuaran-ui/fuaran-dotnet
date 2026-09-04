using System.Collections.Generic;
using System.Linq;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using static Fuaran.UI.Types;

// Stage-4b swap: the tree envelope (Node / NodeKind / the Spec records /
// StateBehaviour / LayoutMode) is IDL-generated too — the Types names are
// abbreviations, erased in metadata, so import the generated declarations
// directly alongside the surviving Types imports.
using static Fuaran.UI.Generated;

// The F# `Action<'Msg>` clashes by name with `System.Action<T>`; alias the
// closed `Action<object>` we always use so the builder surface stays unambiguous
// without dropping the convenient `using static`. (A friction point recorded in
// the findings note.)
using FsAction = Fuaran.UI.Generated.Action<object>;

// Stage-3 swap (Phase 692 family): these vocabulary types are now IDL-generated
// (`Fuaran.UI.Generated.*`). The F# tier re-exports them as type ABBREVIATIONS,
// which are erased in metadata — C# cannot see them through `using static Types`,
// so alias each moved name at the generated declaration directly. (Aliases take
// precedence over `using static` imports, so the surviving Types imports — Node,
// NodeKind, the Spec records — keep resolving as before.)
using FsGen = Fuaran.UI.Generated;
using TextSource = Fuaran.UI.Generated.TextSource;
using CellFormat = Fuaran.UI.Generated.CellFormat;
using ToneVariant = Fuaran.UI.Generated.ToneVariant;
using StyleWeight = Fuaran.UI.Generated.StyleWeight;
using Emphasis = Fuaran.UI.Generated.Emphasis;
using StyleRole = Fuaran.UI.Generated.StyleRole;
using FontVoice = Fuaran.UI.Generated.FontVoice;
using Orientation = Fuaran.UI.Generated.Orientation;
using BoxRole = Fuaran.UI.Generated.BoxRole;
using BadgeVariant = Fuaran.UI.Generated.BadgeVariant;
using ButtonVariant = Fuaran.UI.Generated.ButtonVariant;
using HeadingVariant = Fuaran.UI.Generated.HeadingVariant;
using ChartKind = Fuaran.UI.Generated.ChartKind;
using SelectOption = Fuaran.UI.Generated.SelectOption;
using Motion = Fuaran.UI.Generated.Motion;
using SemanticStyle = Fuaran.UI.Generated.SemanticStyle;
using Accessibility = Fuaran.UI.Generated.Accessibility;
using MetricSpec = Fuaran.UI.Generated.MetricSpec;
using HeadingSpec = Fuaran.UI.Generated.HeadingSpec;
using BadgeSpec = Fuaran.UI.Generated.BadgeSpec;
using MarkdownSpec = Fuaran.UI.Generated.MarkdownSpec;

namespace Fuaran.UI.CSharp.Poc;

// ============================================================================
//  Fluent builders — the §4e "sealed records + fluent builder" authoring shape,
//  rendered in C#.
//
//  Design choices, and the F#-shape mappings the §4e sketch calls for:
//
//   * DUs → static factory methods. F# `[<RequireQualifiedAccess>]` unions
//     compile to `NewCase(...)` factories (`NodeKind<object>.NewLayout`,
//     `Binding<T>.NewStatic`); fieldless cases compile to singleton properties
//     (`ToneVariant.Default`, `CellFormat.None`). The value-helper statics
//     (`Txt`, `Bind`, `Fmt`, `Act`, `Tone`) wrap those so the builders read in
//     idiomatic C#.
//
//   * options → nullable-wrapper helpers (`Fs.Some` / `Fs.None`), surfaced on
//     the builder as either "set it / don't" fluent methods (the optional spec
//     fields default to `None`).
//
//   * `'Msg` → the wire-level `Node<obj>` posture (§4g). Every tree is a
//     `Node<object>`; message payloads (`Action.Dispatch`, form `onChange`
//     handlers) are opaque to the encoder (`"<closure>"` / `"<opaque>"`), so a
//     C# author never needs to name a message type to author a wire-faithful
//     tree.
//
//  The builders construct the `Node<object>` record DIRECTLY (default style,
//  empty state, `None` accessibility) — mirroring the canonical corpus
//  fixtures rather than the F# `Fuaran.*` smart constructors, which layer
//  per-component ARIA defaults. See the findings note for why a supportable
//  package would have to make that a deliberate, documented choice.
// ============================================================================

// ─── Value-helper statics (the typed vocabulary, C#-named) ──────────────────

internal static class Txt
{
    public static TextSource Literal(string s) => TextSource.NewLiteral(s);
    public static TextSource Bound(global::Fuaran.UI.Generated.Binding<string> b) => TextSource.NewBound(b);
}

internal static class Bind
{
    public static global::Fuaran.UI.Generated.Binding<T> Static<T>(T v) =>
        global::Fuaran.UI.Generated.Binding<T>.NewStatic(FSharpOption<T>.Some(v));
    public static global::Fuaran.UI.Generated.Binding<T> State<T>(string key, T def) =>
        global::Fuaran.UI.Generated.Binding<T>.NewState(key, FSharpOption<T>.Some(def));
}

internal static class Fmt
{
    public static CellFormat Currency(string code) => CellFormat.NewCurrency(code);
    public static CellFormat Percent(int decimals) => CellFormat.NewPercent(Fs.Some(decimals));
    public static CellFormat Number(int decimals) => CellFormat.NewNumber(Fs.Some(decimals));
    public static CellFormat None => CellFormat.None;
}

internal static class Act
{
    public static FsAction Chain(params FsAction[] actions) => FsAction.NewChain(Fs.List(actions));
    public static FsAction WriteToClipboard(string text) =>
        // Phase 1126 — the payload is a `TextSource`; a literal wraps in `Literal`.
        FsAction.NewWriteToClipboard(TextSource.NewLiteral(text));
    /// <summary>
    /// Carry a typed host message. <b>IN-PROCESS ONLY</b> — read this before putting
    /// one on a tree you intend to serialise (Phase 1152).
    /// </summary>
    /// <remarks>
    /// The payload has no wire projection: <c>{"$type":"Dispatch"}</c> is the whole
    /// encoding, and the canonical decoder restores the payload as the
    /// <c>"&lt;closure&gt;"</c> sentinel, so a host fed canonical wire JSON observes
    /// THAT the affordance fired and can never receive the message. To reach a host
    /// across the wire use <c>Notify</c> (channel + JSON payload) or <c>Call</c> with
    /// an <c>into:</c> target. Two shipped surfaces make this checkable rather than
    /// remembered: the transport encoder refuses a tree carrying one, and the
    /// pre-emit validator reports it as FUARAN112.
    /// <para>
    /// The generated case now carries the IDL's <c>inProcessOnly</c> annotation, which
    /// reaches C# as <c>CS0618</c>. It is suppressed for this one expression — the
    /// F#-side <c>Action.dispatch</c> helper is scoped the same way, for the same
    /// reason: a helper whose whole job is to build the case cannot warn about
    /// building it, and what the caller gets instead is this paragraph.
    /// </para>
    /// </remarks>
#pragma warning disable CS0618 // in-process only — see the remarks above
    public static FsAction Dispatch(object msg) => FsAction.NewDispatch(msg);
#pragma warning restore CS0618
}

// ─── Shared default fragments (mirror Fixtures.fs `node` helper) ────────────

internal static class Defaults
{
    // Stage-4b swap: `State = None` / `Style = None` are the canonical
    // empty-state / default-style shapes — the old always-present empty
    // StateBehaviour / default SemanticStyle fragments are retired.
    public static readonly FsAction PlaceholderChain = Act.Chain();
}

// ─── Builder base ───────────────────────────────────────────────────────────

/// <summary>Common spine for every node builder: identity + the default
/// style/state/accessibility fragments, plus <see cref="Build"/> which folds in
/// the kind-specific payload.</summary>
internal abstract class NodeBuilder
{
    protected readonly string Id;

    protected NodeBuilder(string id) => Id = id;

    protected abstract NodeKind<object> BuildKind();

    // Generated Node ctor is Generated.fs declaration order (Id, Kind,
    // Accessibility, ExtraAttributes, Motion, State, Style, Tooltip); `Id` is a
    // bare string since the swap.
    public Node<object> Build() =>
        new(
            Id,
            BuildKind(),
            Fs.None<Accessibility>(),
            Fs.None<FSharpMap<string, string>>(),
            Fs.None<Motion>(),
            Fs.None<StateBehaviour<object>>(),
            Fs.None<SemanticStyle>(),
            Fs.None<TextSource>());
}

// ─── Layout builders ────────────────────────────────────────────────────────

internal sealed class CardBuilder : NodeBuilder
{
    private FSharpOption<TextSource> _heading = Fs.None<TextSource>();
    private readonly List<NodeBuilder> _children = new();

    public CardBuilder(string id) : base(id) { }

    public CardBuilder Heading(string text) { _heading = Fs.Some(Txt.Literal(text)); return this; }
    public CardBuilder Children(params NodeBuilder[] kids) { _children.AddRange(kids); return this; }

    // Generated BoxSpec ctor is Generated.fs declaration order (Children,
    // Heading, Layout, Role, KeepTogether, BreakBefore); LayoutMode cases are
    // positional since the swap.
    protected override NodeKind<object> BuildKind() =>
        NodeKind<object>.NewBox(
            new BoxSpec<object>(
                Fs.List(_children.Select(c => c.Build()).ToArray()),
                _heading,
                LayoutMode.NewFlex(Orientation.Vertical, false, Fs.None<int>()),
                BoxRole.Card,
                // Phase 1473 - the print-break declarations; a builder declares neither.
                false,
                false));
}

internal sealed class StackBuilder : NodeBuilder
{
    private Orientation _orientation = Orientation.Vertical;
    private bool _wrap;
    private readonly List<NodeBuilder> _children = new();

    public StackBuilder(string id) : base(id) { }

    public StackBuilder Horizontal() { _orientation = Orientation.Horizontal; return this; }
    public StackBuilder Wrap(bool wrap = true) { _wrap = wrap; return this; }
    public StackBuilder Children(params NodeBuilder[] kids) { _children.AddRange(kids); return this; }

    protected override NodeKind<object> BuildKind() =>
        NodeKind<object>.NewBox(
            new BoxSpec<object>(
                Fs.List(_children.Select(c => c.Build()).ToArray()),
                Fs.None<TextSource>(),
                LayoutMode.NewFlex(_orientation, _wrap, Fs.None<int>()),
                BoxRole.Group,
                // Phase 1473 - the print-break declarations; a builder declares neither.
                false,
                false));
}

internal sealed class GridBuilder : NodeBuilder
{
    private int _cols = 12;
    private readonly List<NodeBuilder> _children = new();

    public GridBuilder(string id) : base(id) { }

    public GridBuilder Cols(int cols) { _cols = cols; return this; }
    public GridBuilder Children(params NodeBuilder[] kids) { _children.AddRange(kids); return this; }

    protected override NodeKind<object> BuildKind() =>
        NodeKind<object>.NewBox(
            new BoxSpec<object>(
                Fs.List(_children.Select(c => c.Build()).ToArray()),
                Fs.None<TextSource>(),
                LayoutMode.NewGrid(_cols, Fs.None<string>(), Fs.None<int>()),
                BoxRole.Group,
                // Phase 1473 - the print-break declarations; a builder declares neither.
                false,
                false));
}

internal sealed class DashboardBuilder : NodeBuilder
{
    private readonly List<NodeBuilder> _children = new();

    public DashboardBuilder(string id) : base(id) { }

    public DashboardBuilder Children(params NodeBuilder[] kids) { _children.AddRange(kids); return this; }

    protected override NodeKind<object> BuildKind() =>
        NodeKind<object>.NewBox(
            new BoxSpec<object>(
                Fs.List(_children.Select(c => c.Build()).ToArray()),
                Fs.None<TextSource>(),
                LayoutMode.Auto,
                BoxRole.Dashboard,
                // Phase 1473 - the print-break declarations; a builder declares neither.
                false,
                false));
}

// ─── Display builders ───────────────────────────────────────────────────────

internal sealed class MetricBuilder : NodeBuilder
{
    private TextSource _label = Txt.Literal("");
    private global::Fuaran.UI.Generated.Binding<double> _source = Bind.Static(0.0);
    private CellFormat _format = Fmt.None;
    private ToneVariant _tone = ToneVariant.Default;
    private StyleWeight _weight = StyleWeight.Standard;
    private Emphasis _emphasis = Emphasis.Normal;
    private FSharpOption<global::Fuaran.UI.Generated.Binding<double>> _trend =
        Fs.None<global::Fuaran.UI.Generated.Binding<double>>();
    private FSharpOption<CellFormat> _trendFormat = Fs.None<CellFormat>();
    // Generated MetricSpec.Icon is a bare `string option` (no IconSource wrap).
    private FSharpOption<string> _icon = Fs.None<string>();
    private FSharpOption<TextSource> _subtext = Fs.None<TextSource>();

    public MetricBuilder(string id) : base(id) { }

    public MetricBuilder Label(string text) { _label = Txt.Literal(text); return this; }
    public MetricBuilder Source(double value) { _source = Bind.Static(value); return this; }
    public MetricBuilder Format(CellFormat f) { _format = f; return this; }
    public MetricBuilder Tone(ToneVariant t) { _tone = t; return this; }
    public MetricBuilder Trend(double value) { _trend = Fs.Some(Bind.Static(value)); return this; }
    public MetricBuilder TrendFormat(CellFormat f) { _trendFormat = Fs.Some(f); return this; }
    public MetricBuilder Icon(string icon) { _icon = Fs.Some(icon); return this; }
    public MetricBuilder Subtext(string text) { _subtext = Fs.Some(Txt.Literal(text)); return this; }

    protected override NodeKind<object> BuildKind() =>
        NodeKind<object>.NewMetric(
            // Phase 867 - the generated record's positional ctor gained
            // `trendPolarity`. Authoring it from this veneer is a deliberate
            // follow-up (charter S7); the default is the pre-867 reading.
            new MetricSpec(_label, _source, _format, _tone, _weight, _emphasis, _trend, _trendFormat,
                TrendPolarity.HigherIsBetter, _icon, _subtext));
}

internal sealed class HeadingBuilder : NodeBuilder
{
    private int _level = 2;
    private TextSource _text = Txt.Literal("");

    public HeadingBuilder(string id) : base(id) { }

    public HeadingBuilder Level(int level) { _level = level; return this; }
    public HeadingBuilder Text(string text) { _text = Txt.Literal(text); return this; }

    protected override NodeKind<object> BuildKind() =>
        NodeKind<object>.NewHeading(
            new HeadingSpec(_level, _text, HeadingVariant.Standard));
}

internal sealed class BadgeBuilder : NodeBuilder
{
    private TextSource _label = Txt.Literal("");
    private BadgeVariant _variant = BadgeVariant.Neutral;

    public BadgeBuilder(string id) : base(id) { }

    public BadgeBuilder Label(string text) { _label = Txt.Literal(text); return this; }
    public BadgeBuilder Variant(BadgeVariant v) { _variant = v; return this; }

    protected override NodeKind<object> BuildKind() =>
        NodeKind<object>.NewBadge(new BadgeSpec(_label, _variant));
}

internal sealed class MarkdownBuilder : NodeBuilder
{
    private TextSource _text = Txt.Literal("");

    public MarkdownBuilder(string id) : base(id) { }

    public MarkdownBuilder Text(string text) { _text = Txt.Literal(text); return this; }

    protected override NodeKind<object> BuildKind() =>
        NodeKind<object>.NewMarkdown(new MarkdownSpec(_text));
}

// ─── Input builders ─────────────────────────────────────────────────────────

internal sealed class ButtonBuilder : NodeBuilder
{
    private TextSource _label = Txt.Literal("");
    private FsAction _onClick = Act.Chain();
    private ButtonVariant _variant = ButtonVariant.Primary;
    // Generated ButtonSpec.Icon is a bare `string option` (no IconSource wrap).
    private FSharpOption<string> _icon = Fs.None<string>();
    private FSharpOption<TextSource> _tooltip = Fs.None<TextSource>();
    private FSharpOption<global::Fuaran.UI.Generated.Binding<bool>> _disabled =
        Fs.None<global::Fuaran.UI.Generated.Binding<bool>>();

    public ButtonBuilder(string id) : base(id) { }

    public ButtonBuilder Label(string text) { _label = Txt.Literal(text); return this; }
    public ButtonBuilder OnClick(FsAction action) { _onClick = action; return this; }
    public ButtonBuilder Variant(ButtonVariant v) { _variant = v; return this; }
    public ButtonBuilder Icon(string icon) { _icon = Fs.Some(icon); return this; }
    public ButtonBuilder DisabledWhen(string stateKey, bool defaultValue = false)
    { _disabled = Fs.Some(Bind.State(stateKey, defaultValue)); return this; }

    protected override NodeKind<object> BuildKind() =>
        NodeKind<object>.NewButton(
            new FsGen.ButtonSpec<object>(_label, _onClick, _variant, _icon, _tooltip, _disabled));
}

/// <summary>A single form field. DU field-kinds surface as factory methods
/// (`Text` / `Number` / `Checkbox` / `Choice` / `TextArea`) — the "field
/// family" the §4e sketch asks the PoC to exercise.</summary>
internal sealed class FieldBuilder
{
    private readonly string _id;
    private TextSource _label;
    private FsGen.FormFieldKind<object> _kind;
    private bool _required;
    private FSharpOption<TextSource> _help = Fs.None<TextSource>();

    private FieldBuilder(string id, string label, FsGen.FormFieldKind<object> kind)
    {
        _id = id;
        _label = Txt.Literal(label);
        _kind = kind;
    }

    public FieldBuilder Required(bool required = true) { _required = required; return this; }
    public FieldBuilder Help(string text) { _help = Fs.Some(Txt.Literal(text)); return this; }

    // Generated FormField declares (Id, Kind, Label, Required, Help, Rule) — ctor is
    // declaration order. Phase 864's Rule slot is not authorable from this POC (the
    // charter rules §11 step 6 does not bind for a spec-record field), so it passes None.
    public FsGen.FormField<object> Build() =>
        new(_id, _kind, _label, _required, _help, Fs.None<FsGen.FieldRule>());

    private static FSharpFunc<TArg, FsAction> NoOp<TArg>() => Fs.Func<TArg, FsAction>(_ => Defaults.PlaceholderChain);

    // Stage-3 field-kind shape: value slots are `Binding<T> option` (Some-wrap) and the
    // change handlers are optional — keep the explicit no-op handlers so the wire shape
    // (the "<closure>" sentinel) matches the pre-swap fixtures.
    public static FieldBuilder Text(string id, string label, string initial = "") =>
        new(id, label, FsGen.FormFieldKind<object>.NewText(Fs.Some(Bind.Static(initial)), Fs.Some(NoOp<string>())));

    public static FieldBuilder Number(string id, string label, double initial = 0.0) =>
        new(id, label, FsGen.FormFieldKind<object>.NewNumber(Fs.Some(Bind.Static(initial)), Fs.Some(NoOp<double>())));

    public static FieldBuilder Checkbox(string id, string label, bool initial = false) =>
        new(id, label, FsGen.FormFieldKind<object>.NewCheckbox(Fs.Some(Bind.Static(initial)), Fs.Some(NoOp<bool>())));

    public static FieldBuilder TextArea(string id, string label, int rows, string initial = "") =>
        new(id, label, FsGen.FormFieldKind<object>.NewTextArea(Fs.Some(Bind.Static(initial)), Fs.Some(NoOp<string>()), rows));

    public static FieldBuilder Choice(string id, string label, string selected, params (string Value, string Label)[] options)
    {
        // Generated SelectOption declares (Label, Value); Label is a bare string now.
        var opts = Fs.List(options.Select(o => new SelectOption(o.Label, o.Value)).ToArray());
        return new FieldBuilder(
            id,
            label,
            FsGen.FormFieldKind<object>.NewChoice(
                Bind.Static(opts),
                // The value slot is `Binding<string> option` (the old double-option flattened).
                Fs.Some(Bind.Static(selected)),
                Fs.Some(NoOp<FSharpOption<string>>())));
    }
}

internal sealed class FormBuilder : NodeBuilder
{
    private readonly List<FieldBuilder> _fields = new();
    private FsAction _onSubmit = Act.Chain();
    private TextSource _submitLabel = Txt.Literal("Submit");
    private FSharpOption<global::Fuaran.UI.Generated.Binding<bool>> _disabled =
        Fs.None<global::Fuaran.UI.Generated.Binding<bool>>();

    public FormBuilder(string id) : base(id) { }

    public FormBuilder Fields(params FieldBuilder[] fields) { _fields.AddRange(fields); return this; }
    public FormBuilder SubmitLabel(string text) { _submitLabel = Txt.Literal(text); return this; }
    public FormBuilder DisabledWhen(string stateKey, bool defaultValue = false)
    { _disabled = Fs.Some(Bind.State(stateKey, defaultValue)); return this; }

    protected override NodeKind<object> BuildKind() =>
        NodeKind<object>.NewForm(
            // Generated FormSpec ctor is Generated.fs declaration order (Fields,
            // OnSubmit, SubmitLabel, Disabled).
            new FsGen.FormSpec<object>(
                Fs.List(_fields.Select(f => f.Build()).ToArray()),
                _onSubmit,
                _submitLabel,
                _disabled));
}

// ─── Visualisation builders ─────────────────────────────────────────────────

internal sealed class ChartBuilder : NodeBuilder
{
    private ChartKind _kind = ChartKind.Line;
    private string _xField = "";
    private FSharpList<string> _yFields = Fs.Empty<string>();
    private FSharpOption<TextSource> _title = Fs.None<TextSource>();
    private bool _stacked;
    private IEnumerable<FSharpMap<string, object>> _rows = Enumerable.Empty<FSharpMap<string, object>>();

    public ChartBuilder(string id) : base(id) { }

    public ChartBuilder Kind(ChartKind kind) { _kind = kind; return this; }
    public ChartBuilder XField(string field) { _xField = field; return this; }
    public ChartBuilder YFields(params string[] fields) { _yFields = Fs.List(fields); return this; }
    public ChartBuilder Title(string text) { _title = Fs.Some(Txt.Literal(text)); return this; }
    public ChartBuilder Stacked(bool stacked = true) { _stacked = stacked; return this; }

    /// <summary>fuaran#665 — static typed rows: each row is name→value cells.</summary>
    public ChartBuilder Rows(params (string Key, object Value)[][] rows)
    {
        _rows = rows
            .Select(r => Fs.Map(r.Select(c => new KeyValuePair<string, object>(c.Key, c.Value))))
            .ToList();
        return this;
    }

    protected override NodeKind<object> BuildKind()
    {
        // fuaran#665 — the rows slot is typed (`Row = FSharpMap<string, obj>`),
        // so a static feed encodes as a JSON array of row objects (empty → `[]`),
        // no longer the `"<opaque>"` sentinel.
        var source = Bind.Static(_rows);
        // Generated ChartSpec ctor is Generated.fs declaration order (Kind,
        // Source, Stacked, XField, YFields, Title, ValueFormat, XTitle, YTitle,
        // Subtitle, LegendPosition, OnPointClick).
        return NodeKind<object>.NewChart(
            new ChartSpec<object>(
                _kind,
                source,
                _stacked,
                _xField,
                _yFields,
                _title,
                Fs.None<Generated.Format>(),
                Fs.None<Generated.TextSource>(),
                Fs.None<Generated.TextSource>(),
                Fs.None<Generated.TextSource>(),
                Fs.None<Generated.ChartLegendPosition>(),
                Fs.None<Generated.ChartDataLabels>(),
                Fs.None<Generated.ChartXScale>(),
                Fs.None<FSharpFunc<FSharpMap<string, object>, FsAction>>()));
    }
}

// ─── Top-level factory entry points ─────────────────────────────────────────

/// <summary>The fluent entry points — <c>Fui.Card("id").Heading(...)...</c>.
/// Mirrors the F# `Fuaran.X` author surface, in C#.</summary>
internal static class Fui
{
    public static CardBuilder Card(string id) => new(id);
    public static StackBuilder Stack(string id) => new(id);
    public static GridBuilder Grid(string id) => new(id);
    public static DashboardBuilder Dashboard(string id) => new(id);
    public static MetricBuilder Metric(string id) => new(id);
    public static HeadingBuilder Heading(string id) => new(id);
    public static BadgeBuilder Badge(string id) => new(id);
    public static MarkdownBuilder Markdown(string id) => new(id);
    public static ButtonBuilder Button(string id) => new(id);
    public static FormBuilder Form(string id) => new(id);
    public static ChartBuilder Chart(string id) => new(id);
}
