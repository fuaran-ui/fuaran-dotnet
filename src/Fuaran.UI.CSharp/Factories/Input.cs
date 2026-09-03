using System.Collections.Generic;
using System.Linq;
using FsFactory = global::Fuaran.UI.Fuaran;
using FsTypes = Fuaran.UI.Types;
using FsGen = Fuaran.UI.Generated;
using FsAction = Fuaran.UI.Generated.Action<object>;

namespace Fuaran.UI.CSharp;

// Phase 305 — the Input kinds (Button ships in the foundation). Change/submit
// handlers are opaque to the wire (§4), so they default to no-ops; the bound
// state (field values, select value) is what rides the wire.
//
// SPEC-CONSTRUCTION-TRIPWIRE — the `new FsGen.<X>(…)` calls below are positional on
// purpose. An additive spec slot lands here as CS7036, at the one site that decides
// whether the veneer exposes it or passes the F# default; that is the mechanism, not
// churn. See src/Fuaran.UI.Tests/SpecConstructionTests.fs ("The C# authoring veneer").
public static partial class Fuaran
{
    // Generated SelectSpec.Value is a plain `Binding<string>` (the old
    // `Binding<string option>` double option flattened): "no selection" is
    // `Static None`, a selection is `Static (Some v)`.
    internal static global::Fuaran.UI.Generated.Binding<string> OptStrValue(string? selected) =>
        global::Fuaran.UI.Generated.Binding<string>.NewStatic(Fs.OptStr(selected!));

    // F# `string option` projects to C# as the nullable-annotated `FSharpOption<string>?`
    // (F# 10 nullness); the no-op handler carries the matching annotation so the
    // smart-ctor call sites stay CS8620-clean.
    internal static Microsoft.FSharp.Core.FSharpFunc<Microsoft.FSharp.Core.FSharpOption<string>?, FsAction> NoOptStrHandler() =>
        Fs.Func<Microsoft.FSharp.Core.FSharpOption<string>?, FsAction>(_ => NoAction);

    internal static global::Fuaran.UI.Generated.Binding<Microsoft.FSharp.Collections.FSharpList<FsGen.SelectOption>> OptionSource(
        IEnumerable<(string Value, string Label)>? options) =>
        global::Fuaran.UI.Generated.Binding<Microsoft.FSharp.Collections.FSharpList<FsGen.SelectOption>>.NewStatic(
            Microsoft.FSharp.Core.FSharpOption<Microsoft.FSharp.Collections.FSharpList<FsGen.SelectOption>>.Some(
                Fs.List((options ?? Enumerable.Empty<(string, string)>())
                    // Generated SelectOption declares (Label, Value); Label is a bare string now.
                    .Select(o => new FsGen.SelectOption(o.Label, o.Value)))));

    // FormFieldKind.Choice's value slot is `Binding<string> option` (the old
    // `Binding<string option>` double-option flattened): no selection is an
    // absent binding, not a binding of None.
    internal static Microsoft.FSharp.Core.FSharpOption<global::Fuaran.UI.Generated.Binding<string>> ChoiceValue(
        string? selected) =>
        selected is null
            ? Microsoft.FSharp.Core.FSharpOption<global::Fuaran.UI.Generated.Binding<string>>.None
            : Microsoft.FSharp.Core.FSharpOption<global::Fuaran.UI.Generated.Binding<string>>.Some(
                global::Fuaran.UI.Generated.Binding<string>.NewStatic(Fs.Some(selected)));

    /// <summary>A form — an ordered list of fields plus a submit action.
    /// <c>OnSubmit</c> takes a wire-representable <see cref="FuaranAction"/>
    /// (Phase 1153) — unset raises nothing.</summary>
    public static FuaranNode Form(FormOptions options) =>
        new(FsFactory.form<object>(
            options.Id,
            // Generated FormSpec ctor is Generated.fs declaration order (Fields,
            // OnSubmit, SubmitLabel, Disabled).
            new FsGen.FormSpec<object>(
                Fs.List((options.Fields ?? Enumerable.Empty<FormField>()).Select(f => f.Inner)),
                (options.OnSubmit ?? FuaranAction.Empty).Inner,
                options.SubmitLabel.Inner,
                Fs.None<global::Fuaran.UI.Generated.Binding<bool>>())));

    /// <summary>A single-select dropdown.</summary>
    public static FuaranNode Select(SelectOptions options) =>
        new(FsFactory.select<object>(
            options.Id,
            // Generated SelectSpec ctor is Generated.fs declaration order (Label,
            // OnChange, OnChangeMulti, Source, Value, Placeholder, Disabled, Multiple,
            // Values); Multiple is now `bool option` (single-select = None).
            new FsGen.SelectSpec<object>(
                options.Label.Inner,
                Fs.Some(NoOptStrHandler()),
                Fs.None<Microsoft.FSharp.Core.FSharpFunc<Microsoft.FSharp.Collections.FSharpList<string>, FsAction>>(),
                OptionSource(options.Options),
                OptStrValue(options.Value),
                options.Placeholder is { } p ? Fs.Some(p.Inner) : Fs.None<FsGen.TextSource>(),
                Fs.None<global::Fuaran.UI.Generated.Binding<bool>>(),
                Fs.None<bool>(),
                Fs.None<global::Fuaran.UI.Generated.Binding<Microsoft.FSharp.Collections.FSharpList<string>>>())));

    /// <summary>A multi-select (<c>&lt;select multiple&gt;</c>).</summary>
    public static FuaranNode MultiSelect(MultiSelectOptions options) =>
        new(FsFactory.multiSelect<object>(
            options.Id,
            options.Label.Inner,
            OptionSource(options.Options),
            global::Fuaran.UI.Generated.Binding<Microsoft.FSharp.Collections.FSharpList<string>>.NewStatic(
                Fs.Some(Fs.List(options.Values ?? Enumerable.Empty<string>()))),
            NoHandler<Microsoft.FSharp.Collections.FSharpList<string>>()));

    /// <summary>A filter strip.</summary>
    public static FuaranNode Filters(FiltersOptions options) =>
        new(FsFactory.filters<object>(
            options.Id,
            Fs.List((options.Filters ?? Enumerable.Empty<Filter>()).Select(f => f.Inner))));

    /// <summary>A file-upload control.</summary>
    public static FuaranNode FileUpload(FileUploadOptions options) =>
        new(FsFactory.fileUpload<object>(
            options.Id,
            // Generated FileUploadSpec ctor is Generated.fs declaration order (Accept,
            // Label, Multiple, OnSelect, Disabled); OnSelect is optional now and
            // FileSelection lives in HostPrelude — the facade keeps its no-op handler
            // (Some-wrapped) so the wire shape is unchanged.
            new FsGen.FileUploadSpec<object>(
                Fs.List(options.Accept ?? Enumerable.Empty<string>()),
                options.Label.Inner,
                options.Multiple,
                Fs.Some(
                    Fs.Func<Microsoft.FSharp.Collections.FSharpList<global::Fuaran.UI.HostPrelude.FileSelection>, FsAction>(
                        _ => NoAction)),
                Fs.None<global::Fuaran.UI.Generated.Binding<bool>>())));
}

/// <summary>A form field — build with the static factories (<see cref="Text"/> / <see cref="Number"/> / …).</summary>
public sealed class FormField
{
    internal FsGen.FormField<object> Inner { get; }

    private FormField(FsGen.FormField<object> inner) => Inner = inner;

    private static FormField Make(string id, Text label, FsGen.FormFieldKind<object> kind, bool required, Text? help, FieldRule? rule = null) =>
        // Generated FormField declares (Id, Kind, Label, Required, Help, Rule) — ctor is
        // declaration order. Phase 864 added the Rule slot and recorded that no §11
        // step-6 gate binds a spec-record FIELD (Coverage.cs reflects NodeKind cases; the
        // VB analyzer pins Vocabulary.Kinds), so the veneer passed None and nothing was
        // red. Phase 873 is the follow-up that ruling named: a rule is DATA, so unlike
        // the field handlers beside it, it survives this veneer intact.
        new(new FsGen.FormField<object>(
            id,
            kind,
            label.Inner,
            required,
            help is { } h ? Fs.Some(h.Inner) : Fs.None<FsGen.TextSource>(),
            rule is { } r ? Fs.Some(r.Inner) : Fs.None<FsGen.FieldRule>()));

    /// <summary>A text field. <paramref name="rule"/> declares a constraint the host
    /// must enforce at submit (Phase 864).</summary>
    public static FormField Text(string id, Text label, string initial = "", bool required = false, Text? help = null, FieldRule? rule = null) =>
        Make(id, label, FsGen.FormFieldKind<object>.NewText(Fs.Some(global::Fuaran.UI.Generated.Binding<string>.NewStatic(Fs.Some(initial))), Fs.Some(NoFieldHandler<string>())), required, help, rule);

    /// <summary>A number field. A numeric RANGE is <see cref="Fuaran.RangedNumber"/>'s
    /// job, not <paramref name="rule"/>'s — the rule vocabulary deliberately does not
    /// restate a bound that already had a spelling (FUARAN101 refuses one that does).</summary>
    public static FormField Number(string id, Text label, double initial = 0.0, bool required = false, Text? help = null, FieldRule? rule = null) =>
        Make(id, label, FsGen.FormFieldKind<object>.NewNumber(Fs.Some(global::Fuaran.UI.Generated.Binding<double>.NewStatic(Fs.Some(initial))), Fs.Some(NoFieldHandler<double>())), required, help, rule);

    /// <summary>A checkbox field.</summary>
    public static FormField Checkbox(string id, Text label, bool initial = false, bool required = false, Text? help = null, FieldRule? rule = null) =>
        Make(id, label, FsGen.FormFieldKind<object>.NewCheckbox(Fs.Some(global::Fuaran.UI.Generated.Binding<bool>.NewStatic(Fs.Some(initial))), Fs.Some(NoFieldHandler<bool>())), required, help, rule);

    /// <summary>A multi-line text-area field.</summary>
    public static FormField TextArea(string id, Text label, int rows = 4, string initial = "", bool required = false, Text? help = null, FieldRule? rule = null) =>
        Make(id, label, FsGen.FormFieldKind<object>.NewTextArea(Fs.Some(global::Fuaran.UI.Generated.Binding<string>.NewStatic(Fs.Some(initial))), Fs.Some(NoFieldHandler<string>()), rows), required, help, rule);

    /// <summary>A single-choice (dropdown) field.</summary>
    public static FormField Choice(string id, Text label, string? selected, IEnumerable<(string Value, string Label)> options, bool required = false, Text? help = null, FieldRule? rule = null) =>
        Make(
            id,
            label,
            FsGen.FormFieldKind<object>.NewChoice(
                Fuaran.OptionSource(options),
                Fuaran.ChoiceValue(selected),
                Fs.Some(Fuaran.NoOptStrHandler())),
            required,
            help,
            rule);

    /// <summary>A typeahead / autocomplete field (Phase 1113) — the searchable form of
    /// <see cref="Choice"/>. Reach for it when the option set is large, searchable or
    /// arrives asynchronously, or when a value outside the list is admissible; reach for
    /// <see cref="Choice"/> for a bounded known set the reader can scan.
    /// <paramref name="allowFreeText"/> defaults to <c>false</c> — the constrained form —
    /// so the shortest call is the one that admits only the listed values.</summary>
    public static FormField Combobox(string id, Text label, string? selected, IEnumerable<(string Value, string Label)> options, bool allowFreeText = false, bool required = false, Text? help = null, FieldRule? rule = null) =>
        Make(
            id,
            label,
            FsGen.FormFieldKind<object>.NewCombobox(
                allowFreeText,
                Fs.Some(Fuaran.NoOptStrHandler()),
                Fuaran.OptionSource(options),
                Fuaran.ChoiceValue(selected)),
            required,
            help,
            rule);

    private static Microsoft.FSharp.Core.FSharpFunc<T, FsAction> NoFieldHandler<T>() =>
        Fs.Func<T, FsAction>(_ => FsAction.NewChain(Fs.Empty<FsAction>()));
}

/// <summary>A filter chip — build with the static factories.</summary>
public sealed class Filter
{
    internal FsGen.FilterSpec<object> Inner { get; }

    private Filter(FsGen.FilterSpec<object> inner) => Inner = inner;

    // 0.2.0 filters-unification: a chip's control is an ordinary FormFieldKind
    // auto-bound to its own filter key (Binding.Filter(name, None)); the
    // declarative shape carries no handler — the renderer's write-back default
    // writes $filters.<name>. Mirror of the F# FilterField module.
    // Generated FilterSpec declares (Kind, Label, Name) — ctor is declaration order —
    // and the old `Field` slot is renamed `Kind`.

    /// <summary>A free-text filter chip bound to its own filter key.</summary>
    public static Filter Text(string name, Text label) =>
        new(new FsGen.FilterSpec<object>(
            FsGen.FormFieldKind<object>.NewText(
                Fs.Some(global::Fuaran.UI.Generated.Binding<string>.NewFilter(name, Microsoft.FSharp.Core.FSharpOption<string>.None)),
                null),
            label.Inner,
            name));

    /// <summary>A typeahead filter chip bound to its own filter key (Phase 1113) — the
    /// searchable form of <see cref="Choice(string, Text, IEnumerable{ValueTuple{string, string}})"/>.</summary>
    public static Filter Combobox(string name, Text label, IEnumerable<(string Value, string Label)> options, bool allowFreeText = false) =>
        new(new FsGen.FilterSpec<object>(
            FsGen.FormFieldKind<object>.NewCombobox(
                allowFreeText,
                null,
                Fuaran.OptionSource(options),
                Fs.Some(global::Fuaran.UI.Generated.Binding<string>.NewFilter(
                    name,
                    Microsoft.FSharp.Core.FSharpOption<string>.None))),
            label.Inner,
            name));

    /// <summary>A choice filter chip bound to its own filter key.</summary>
    public static Filter Choice(string name, Text label, IEnumerable<(string Value, string Label)> options) =>
        new(new FsGen.FilterSpec<object>(
            FsGen.FormFieldKind<object>.NewChoice(
                Fuaran.OptionSource(options),
                // The Choice value slot is `Binding<string> option` now (double-option flattened).
                Fs.Some(global::Fuaran.UI.Generated.Binding<string>.NewFilter(
                    name,
                    Microsoft.FSharp.Core.FSharpOption<string>.None)),
                null),
            label.Inner,
            name));
}

/// <summary>Options for <see cref="Fuaran.Form"/>.</summary>
public sealed record FormOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The form fields.</summary>
    public IEnumerable<FormField>? Fields { get; init; }

    /// <summary>The submit-button label (default "Submit").</summary>
    public Text SubmitLabel { get; init; } = "Submit";

    /// <summary>
    /// What the form raises on submit (Phase 1153). Unset is an empty chain — the
    /// shape this veneer authored before the <see cref="FuaranAction"/> vocabulary
    /// existed, so it is byte-unchanged for an author who does not set it.
    /// </summary>
    public FuaranAction? OnSubmit { get; init; }
}

/// <summary>Options for <see cref="Fuaran.Select"/>.</summary>
public sealed record SelectOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The select label.</summary>
    public required Text Label { get; init; }

    /// <summary>The (value, label) options.</summary>
    public IEnumerable<(string Value, string Label)>? Options { get; init; }

    /// <summary>The currently-selected value (or null).</summary>
    public string? Value { get; init; }

    /// <summary>An optional placeholder.</summary>
    public Text? Placeholder { get; init; }
}

/// <summary>Options for <see cref="Fuaran.MultiSelect"/>.</summary>
public sealed record MultiSelectOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The select label.</summary>
    public required Text Label { get; init; }

    /// <summary>The (value, label) options.</summary>
    public IEnumerable<(string Value, string Label)>? Options { get; init; }

    /// <summary>The currently-selected values.</summary>
    public IEnumerable<string>? Values { get; init; }
}

/// <summary>Options for <see cref="Fuaran.Filters"/>.</summary>
public sealed record FiltersOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The filter chips.</summary>
    public IEnumerable<Filter>? Filters { get; init; }
}

/// <summary>Options for <see cref="Fuaran.FileUpload"/>.</summary>
public sealed record FileUploadOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The control label.</summary>
    public required Text Label { get; init; }

    /// <summary>Accepted MIME types / extensions.</summary>
    public IEnumerable<string>? Accept { get; init; }

    /// <summary>Whether multiple files may be selected.</summary>
    public bool Multiple { get; init; }
}
