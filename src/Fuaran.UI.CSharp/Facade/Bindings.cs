using System;
using System.Collections.Generic;
using System.Linq;
using FsTypes = Fuaran.UI.Types;
using FsGen = Fuaran.UI.Generated;
using FsJVal = global::Fuaran.Core.JVal;
using FsCore = global::Fuaran.Core;

namespace Fuaran.UI.CSharp;

// SPEC-CONSTRUCTION-TRIPWIRE — the `new FsGen.<X>(…)` calls below (Phase 1532:
// `InvokeArg`, `TransformParam`) are positional on purpose. C# has no
// copy-and-update over an F# record, so an additive slot on one of those records
// lands here as CS7036, at the one site that decides whether the veneer exposes
// the new slot or passes the F# default explicitly. That is the mechanism, not
// churn to be routed around; the VB tier authors through this veneer, so it is
// the tripwire for both languages. Pinned in both directions, this marker
// included, by src/Fuaran.UI.Tests/SpecConstructionTests.fs ("The C# authoring
// veneer").

/// <summary>
/// A C#-native binding — the authoring facade over the F# <c>Binding&lt;'T&gt;</c>.
/// A plain value implicitly converts to a static binding, so
/// <c>Source = 1234.5</c> and <c>Href = "https://…"</c> bind with no helper call
/// (the C# edge over the TS union-via-helper shape). Reach for the
/// <see cref="Binding"/> factory for the data-bound cases (<c>Query</c> /
/// <c>State</c> / …).
/// </summary>
/// <typeparam name="T">The bound value type at the author surface.</typeparam>
public sealed class Binding<T>
{
    internal FsGen.Binding<T> Inner { get; }

    internal Binding(FsGen.Binding<T> fs) => Inner = fs;

    /// <summary>A literal, non-bound value (<c>Binding.Static</c>).</summary>
    public static implicit operator Binding<T>(T value) =>
        new(FsGen.Binding<T>.NewStatic(Microsoft.FSharp.Core.FSharpOption<T>.Some(value)));
}

/// <summary>
/// Factory entry points for the data-bound <c>Binding&lt;'T&gt;</c> cases. Mirrors
/// the F# <c>binding.*</c> surface. A static value needs no factory — assign it
/// directly (<see cref="Binding{T}"/>'s implicit conversion).
/// </summary>
public static class Binding
{
    /// <summary>An explicit static binding (rarely needed — a bare value converts implicitly).</summary>
    public static Binding<T> Static<T>(T value) =>
        new(FsGen.Binding<T>.NewStatic(Microsoft.FSharp.Core.FSharpOption<T>.Some(value)));

    /// <summary>
    /// A query binding. The name is what rides the wire; the accessor is a
    /// closure (never serialised) the host runtime uses to project the query
    /// result. The convenience overload supplies an identity projection — sound
    /// because the wire carries only the name.
    /// </summary>
    public static Binding<T> Query<T>(string name) =>
        new(FsGen.Binding<T>.NewQuery(
            name,
            Fs.Func<object, T>(o => (T)o),
            Microsoft.FSharp.Core.FSharpOption<Microsoft.FSharp.Collections.FSharpList<string>>.None));

    /// <summary>A query binding with an explicit typed projection from the query result.</summary>
    public static Binding<T> Query<TResult, T>(string name, Func<TResult, T> accessor) =>
        new(FsGen.Binding<T>.NewQuery(
            name,
            Fs.Func<object, T>(o => accessor((TResult)o)),
            Microsoft.FSharp.Core.FSharpOption<Microsoft.FSharp.Collections.FSharpList<string>>.None));

    /// <summary>A filter-source binding (resolves against the host filter store).</summary>
    public static Binding<T> Filter<T>(string name) =>
        new(FsGen.Binding<T>.NewFilter(name, Microsoft.FSharp.Core.FSharpOption<T>.None));

    /// <summary>A filter-source binding with a declared default — the value the
    /// resolver yields before the filter is first written (0.2.0).</summary>
    public static Binding<T> Filter<T>(string name, T defaultValue) =>
        new(FsGen.Binding<T>.NewFilter(name, Microsoft.FSharp.Core.FSharpOption<T>.Some(defaultValue)));

    /// <summary>A module-state binding keyed by <paramref name="key"/> with a default value.</summary>
    public static Binding<T> State<T>(string key, T defaultValue) =>
        new(FsGen.Binding<T>.NewState(key, Microsoft.FSharp.Core.FSharpOption<T>.Some(defaultValue)));

    /// <summary>
    /// A module-state binding whose slot carries NO default — it resolves to nothing
    /// until the key is first written (the wire's <c>{"$type":"State","key":…}</c> form,
    /// with <c>defaultValue</c> omitted). The F# twin is <c>binding.stateNoDefault</c>.
    /// </summary>
    /// <remarks>
    /// This is a DIFFERENT wire document from <see cref="State{T}(string, T)"/>, not a
    /// convenience over it: a declared default encodes a <c>defaultValue</c> key and an
    /// undeclared one omits it, and the corpus carries both (a defaulted disclosure
    /// <c>open</c>, an undefaulted select <c>value</c>). Either is writable, so both take
    /// the control write-back default when the handler is omitted.
    /// </remarks>
    public static Binding<T> State<T>(string key) =>
        new(FsGen.Binding<T>.NewState(key, Microsoft.FSharp.Core.FSharpOption<T>.None));

    /// <summary>A selection binding reading the current selection of node <paramref name="nodeId"/>.</summary>
    public static Binding<T> Selection<TRow, T>(string nodeId, Func<TRow, T> accessor) =>
        new(FsGen.Binding<T>.NewSelection(
            nodeId,
            Fs.Func<object, T>(o => accessor((TRow)o)),
            Microsoft.FSharp.Core.FSharpOption<T>.None,
            Microsoft.FSharp.Core.FSharpOption<string>.None));

    /// <summary>A selection binding with a declared default — the value the
    /// resolver yields until the user first selects a row (0.2.9, the
    /// Filter-default convention).</summary>
    public static Binding<T> Selection<TRow, T>(string nodeId, Func<TRow, T> accessor, T defaultValue) =>
        new(FsGen.Binding<T>.NewSelection(
            nodeId,
            Fs.Func<object, T>(o => accessor((TRow)o)),
            Microsoft.FSharp.Core.FSharpOption<T>.Some(defaultValue),
            Microsoft.FSharp.Core.FSharpOption<string>.None));

    /// <summary>A declarative row-field selection binding (0.2.10, Phase 632):
    /// projects <paramref name="field"/> off the clicked row — the
    /// wire-expressible twin of a typed accessor. <paramref name="defaultValue"/>
    /// (the projected scalar) yields until the user first selects a row.</summary>
    public static Binding<T> SelectionField<T>(string nodeId, string field, T defaultValue) =>
        new(FsGen.Binding<T>.NewSelection(
            nodeId,
            Fs.Func<object, T>(o => FsTypes.Binding.projectSelectionField<T>(field, o)),
            Microsoft.FSharp.Core.FSharpOption<T>.Some(defaultValue),
            Microsoft.FSharp.Core.FSharpOption<string>.Some(field)));

    /// <summary>
    /// A locale-aware formatted string (Phase 102). Projects a numeric
    /// <paramref name="source"/> to a localised display <see cref="string"/> via the
    /// bounded <see cref="LocaleFormat"/> intent + <see cref="Locale"/> selector — drop
    /// it into any string slot. Distinct from <see cref="CellFormat"/> (grid/metric display).
    /// </summary>
    public static Binding<string> Format(Binding<double> source, LocaleFormat format, Locale? locale = null) =>
        new(FsGen.Binding<string>.NewFormat(source.Inner, format.Inner, (locale ?? Locale.Ambient).Inner));

    // Phase 1532 — the declarative primitives below. The veneer could author none
    // of them, so a C# (or VB, which translates through this surface) author had
    // no spelling for "now", for a translated string with arguments, for a form
    // field's local buffer, or for a host capability — four of the things the
    // language leads with. Each is wire-representable in full, which is the
    // boundary this facade is drawn on.

    /// <summary>
    /// The HOST-furnished instant (Phase 765) — <c>{"$type":"Now"}</c>. Not a clock
    /// read: the host resolves one instant for the whole render pass, which is what
    /// makes a replayed op-stream reproduce its original render instead of drifting
    /// to replay-time "now".
    /// </summary>
    /// <param name="grain">
    /// Truncates the instant before any slot reads it (Phase 1533). Omitted means
    /// <see cref="TimeGrain.Second"/>, which is the identity.
    /// </param>
    /// <remarks>
    /// A host that furnishes no instant renders the slot's placeholder rather than a
    /// plausible wrong date — deliberately loud, and the reason this is a binding
    /// rather than something an author hardcodes.
    /// </remarks>
    public static Binding<string> Now(TimeGrain? grain = null) =>
        new(FsGen.Binding<string>.NewNow(
            Fs.Func<object, string>(o => o as string ?? string.Empty),
            grain.HasValue
                ? Microsoft.FSharp.Core.FSharpOption<FsGen.TimeGrain>.Some(grain.Value.ToFs())
                : Microsoft.FSharp.Core.FSharpOption<FsGen.TimeGrain>.None));

    /// <summary>
    /// A translated string, resolved by the host's i18n catalogue. A key the
    /// catalogue does not hold renders as the loud <c>[i18n:key]</c> placeholder, so
    /// an unregistered key is caught at sight rather than rendering as nothing.
    /// </summary>
    public static Binding<string> I18n(string key) =>
        new(FsGen.Binding<string>.NewI18n(
            key,
            Microsoft.FSharp.Core.FSharpOption<Microsoft.FSharp.Collections.FSharpMap<string, FsGen.Binding<FsJVal>>>.None));

    /// <summary>
    /// A translated string with arguments — each <c>{name}</c> placeholder in the
    /// catalogue's template substituted by the named value.
    /// </summary>
    /// <remarks>
    /// The arguments are BINDINGS, not literals, which is the point: "3 items left"
    /// takes its count from the same reactive slot the list reads.
    /// </remarks>
    public static Binding<string> I18n(string key, params (string Name, Binding<Payload> Value)[] args) =>
        new(FsGen.Binding<string>.NewI18n(
            key,
            Microsoft.FSharp.Core.FSharpOption<Microsoft.FSharp.Collections.FSharpMap<string, FsGen.Binding<FsJVal>>>.Some(
                Fs.Map(args.Select(a =>
                    new System.Collections.Generic.KeyValuePair<string, FsGen.Binding<FsJVal>>(
                        a.Name,
                        Fs.MapBinding(a.Value.Inner, (Payload p) => p.Inner)))))));

    /// <summary>
    /// A form field's LOCAL buffer over a re-sync <paramref name="initialFrom"/>
    /// source: the reader's keystrokes live in the field until
    /// <paramref name="flushOn"/> fires, at which point the value commits.
    /// </summary>
    /// <remarks>
    /// Restricted to a text field. The F# case carries a <c>format</c> and a
    /// <c>parse</c> closure so a typed field can render and re-read its own type;
    /// both are <c>&lt;closure&gt;</c> sentinels on the wire, and a veneer whose
    /// trees ARE serialised supplies the identity pair rather than minting host
    /// behaviour that cannot survive the wire.
    /// <para>
    /// The case's two WIRE-CARRIED slots — a declared <c>codec</c> and a
    /// <c>commitTo</c> destination (Phase 1538) — are passed absent here, which
    /// is the string field's own shape: no re-parse to declare, and the commit
    /// goes where the field's own binding says. A veneer member that exposed
    /// them would be a different member, taking them as arguments, and is not
    /// this one. This construction is positional on purpose (see the
    /// SPEC-CONSTRUCTION-TRIPWIRE note in <c>Facade/Actions.cs</c>): a further
    /// slot on the case lands here as CS7036, at the site that decides.
    /// </para>
    /// </remarks>
    public static Binding<string> Local(Binding<string> initialFrom, LocalFlush flushOn) =>
        new(FsGen.Binding<string>.NewLocal(
            flushOn.Inner,
            Fs.Func<string, string>(s => s),
            initialFrom.Inner,
            Fs.None<Microsoft.FSharp.Core.FSharpFunc<string, object>>(),
            Fs.Func<string, Microsoft.FSharp.Core.FSharpResult<string, string>>(
                Microsoft.FSharp.Core.FSharpResult<string, string>.NewOk),
            Fs.None<FsGen.Format>(),
            Fs.None<string>()));

    /// <summary>
    /// A value produced by a host-registered CAPABILITY (Phase 283) —
    /// <c>{"$type":"Invoke","capabilityId":…}</c>. The host resolves
    /// <paramref name="capabilityId"/> and its arguments to a value; until it does,
    /// the node shows its loading surface, and a failure shows its error surface.
    /// </summary>
    /// <param name="capabilityId">The id the host registered the capability under.</param>
    /// <param name="args">
    /// Argument address/value pairs. Both halves are strings on the wire — the
    /// capability's own declared signature is what types them.
    /// </param>
    public static Binding<T> Invoke<T>(string capabilityId, params (string Addr, string Value)[] args) =>
        new(FsGen.Binding<T>.NewInvoke(
            capabilityId,
            Fs.List(args.Select(a => new FsGen.InvokeArg(a.Addr, a.Value)))));

    /// <summary>
    /// A declarative dataframe PIPELINE evaluated as data (Phase 282) — filter, project,
    /// derive, group, join, window, sort, limit — over
    /// <paramref name="source"/>. In a rows slot the binding is the result rows; in a
    /// scalar slot it is the lone cell of an exactly-1×1 result.
    /// </summary>
    /// <param name="parameters">
    /// Named values the pipeline reads as <c>param</c> / <c>in</c>, each fed by its own
    /// binding — this is how a filter chip or a selected row re-parameterises the
    /// pipeline without the tree being rewritten.
    /// </param>
    /// <remarks>
    /// The pipeline steps are <c>Fuaran.Core</c>'s own algebra, carried rather than
    /// restated. That is deliberate: <c>Transform</c> and <c>ColExpr</c> are a
    /// twenty-six-case vocabulary with their own specification and their own reference
    /// evaluator, and a second C#-shaped spelling of them would be two vocabularies for
    /// one algebra — the near-synonym pair the language's vocabulary charter exists to
    /// forbid — with the drift between them landing on authors as pipelines that
    /// evaluate differently from the ones the corpus certifies.
    /// </remarks>
    public static Binding<T> Transform<T>(
        TransformSource source,
        IEnumerable<FsCore.Transform> pipeline,
        IEnumerable<(string Name, Binding<Payload> From)>? parameters = null) =>
        new(FsGen.Binding<T>.NewTransform(
            source.Inner,
            Fs.List(pipeline),
            parameters is null
                ? Fs.None<Microsoft.FSharp.Collections.FSharpList<FsGen.TransformParam>>()
                : Fs.Some(Fs.List(parameters.Select(TransformParameter)))));

    /// <summary>
    /// A scalar EXPRESSION over the binding's own parameters (Phase 1534) — the
    /// single-cell twin of <see cref="Transform{T}"/>, for a slot that wants one derived
    /// value rather than a frame.
    /// </summary>
    /// <remarks>
    /// The expression is <c>Fuaran.Core.ColExpr</c> for the reason
    /// <see cref="Transform{T}"/>'s pipeline is Core's: one algebra, one spelling.
    /// </remarks>
    public static Binding<T> Expr<T>(
        FsCore.ColExpr expr,
        IEnumerable<(string Name, Binding<Payload> From)>? parameters = null) =>
        new(FsGen.Binding<T>.NewExpr(
            expr,
            parameters is null
                ? Fs.None<Microsoft.FSharp.Collections.FSharpList<FsGen.TransformParam>>()
                : Fs.Some(Fs.List(parameters.Select(TransformParameter)))));

    private static FsGen.TransformParam TransformParameter((string Name, Binding<Payload> From) p) =>
        new FsGen.TransformParam(Fs.MapBinding(p.From.Inner, (Payload v) => v.Inner), p.Name);
}

/// <summary>
/// Where a <see cref="Binding.Transform{T}"/> reads its rows from — the authoring
/// facade over the F# <c>TransformSource</c>.
/// </summary>
public readonly struct TransformSource
{
    internal FsGen.TransformSource Inner { get; }

    private TransformSource(FsGen.TransformSource inner) => Inner = inner;

    /// <summary>
    /// A fixed source: an embedded table, or a named reference the host resolves.
    /// </summary>
    public static TransformSource Data(FsCore.DataSource source) =>
        new(FsGen.TransformSource.NewData(source));

    /// <summary>
    /// A LIVE source (Phase 818): the rows come from a binding, so a state write or a
    /// completed query re-evaluates the pipeline. <paramref name="initial"/> is what the
    /// pipeline reads before that binding first resolves.
    /// </summary>
    public static TransformSource Live(Binding<Payload> from, FsCore.DataSource initial) =>
        new(FsGen.TransformSource.NewLive(Fs.MapBinding(from.Inner, (Payload p) => p.Inner), initial));
}

/// <summary>
/// When a <see cref="Binding.Local"/> buffer commits its value — the authoring
/// facade over the F# <c>LocalFlushTrigger</c>.
/// </summary>
/// <remarks>
/// A struct rather than an enum because one member carries a payload
/// (<see cref="OnDebounce"/>'s interval), which is the same reason
/// <see cref="LocaleFormat"/> is one.
/// </remarks>
public readonly struct LocalFlush
{
    internal FsGen.LocalFlushTrigger Inner { get; }

    private LocalFlush(FsGen.LocalFlushTrigger inner) => Inner = inner;

    /// <summary>Commit when the field loses focus.</summary>
    public static LocalFlush OnBlur { get; } = new(FsGen.LocalFlushTrigger.OnBlur);

    /// <summary>Commit when the enclosing form is submitted.</summary>
    public static LocalFlush OnSubmit { get; } = new(FsGen.LocalFlushTrigger.OnSubmit);

    /// <summary>Commit when an explicit <see cref="FuaranAction.CommitLocal"/> is raised.</summary>
    public static LocalFlush OnCommitAction { get; } = new(FsGen.LocalFlushTrigger.OnCommitAction);

    /// <summary>Commit once the reader has stopped typing for <paramref name="milliseconds"/>.</summary>
    public static LocalFlush OnDebounce(int milliseconds) =>
        new(FsGen.LocalFlushTrigger.NewOnDebounce(milliseconds));
}

/// <summary>Date-presentation breadth for <see cref="LocaleFormat.Date"/> — maps to the F# <c>DateStyle</c>.</summary>
public enum DateStyle
{
    Short,
    Medium,
    Long,
    Full,
}

/// <summary>Relative-time grain for <see cref="LocaleFormat.RelativeTime"/> — maps to the F# <c>RelativeTimeUnit</c>.</summary>
public enum RelativeTimeUnit
{
    Second,
    Minute,
    Hour,
    Day,
    Week,
    Month,
    Year,
}

/// <summary>
/// The bounded, semantic locale-aware formatting intent carried by
/// <see cref="Binding.Format"/> — the authoring facade over the F# <c>Format</c> DU.
/// No raw <c>Intl</c> option-bag escape (FGP 1).
/// </summary>
public readonly struct LocaleFormat
{
    internal FsGen.Format Inner { get; }

    private LocaleFormat(FsGen.Format inner) => Inner = inner;

    /// <summary>A plain localised number; <paramref name="decimals"/> pins the fraction-digit count.</summary>
    public static LocaleFormat Number(int? decimals = null) =>
        new(FsGen.Format.NewNumber(Fs.OfNullable(decimals)));

    /// <summary>A currency amount; <paramref name="isoCode"/> is the ISO-4217 code (e.g. "GBP").</summary>
    public static LocaleFormat Currency(string isoCode) =>
        new(FsGen.Format.NewCurrency(isoCode));

    /// <summary>A percentage of a ratio source (<c>0.42</c> → "42%").</summary>
    public static LocaleFormat Percent(int? decimals = null) =>
        new(FsGen.Format.NewPercent(Fs.OfNullable(decimals)));

    /// <summary>An absolute date/time (source read as whole Unix-epoch seconds).</summary>
    public static LocaleFormat Date(DateStyle style) =>
        new(FsGen.Format.NewDate(style.ToFs()));

    /// <summary>A relative time ("3 days ago"), source read as a signed count of <paramref name="unit"/>.</summary>
    public static LocaleFormat RelativeTime(RelativeTimeUnit unit) =>
        new(FsGen.Format.NewRelativeTime(unit.ToFs()));

    /// <summary>
    /// An ELAPSED duration (Phase 819) — the numeric source counts
    /// <paramref name="unit"/>s, rendered per <paramref name="style"/> ("1:23:45",
    /// "1h 23m", "1 hour 23 minutes").
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="RelativeTime"/>, which places a moment relative to
    /// now. A duration has no "now" in it: 90 minutes is 90 minutes whenever it is
    /// read.
    /// </remarks>
    public static LocaleFormat Duration(DurationUnit unit, DurationStyle style) =>
        new(FsGen.Format.NewDuration(unit.ToFs(), style.ToFs()));

    /// <summary>
    /// Time elapsed SINCE the source instant, against the host-furnished "now"
    /// (Phase 1533) — the source is read as whole Unix-epoch seconds.
    /// </summary>
    /// <param name="unit">
    /// Pins the unit the delta is expressed in; omitted lets the host pick the
    /// natural one.
    /// </param>
    /// <remarks>
    /// The one <see cref="LocaleFormat"/> whose rendering depends on the host instant
    /// as well as on its source, so a host that furnishes none renders the slot's
    /// placeholder rather than a relative time computed against an invented now.
    /// </remarks>
    public static LocaleFormat Since(RelativeTimeUnit? unit = null) =>
        new(FsGen.Format.NewSince(
            unit.HasValue
                ? Microsoft.FSharp.Core.FSharpOption<FsGen.RelativeTimeUnit>.Some(unit.Value.ToFs())
                : Microsoft.FSharp.Core.FSharpOption<FsGen.RelativeTimeUnit>.None));
}

/// <summary>The locale selector for <see cref="Binding.Format"/> — the facade over the F# <c>LocaleSource</c>.</summary>
public readonly struct Locale
{
    internal FsGen.LocaleSource Inner { get; }

    private Locale(FsGen.LocaleSource inner) => Inner = inner;

    /// <summary>Defer to the host-supplied ambient locale (the default).</summary>
    public static Locale Ambient { get; } = new(FsGen.LocaleSource.Ambient);

    /// <summary>Pin an explicit BCP-47 locale tag (e.g. "en-GB").</summary>
    public static Locale Explicit(string tag) => new(FsGen.LocaleSource.NewExplicit(tag));
}
