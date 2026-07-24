using System.Collections.Generic;
using System.Linq;
using FsFactory = global::Fuaran.UI.Fuaran;
using FsTypes = Fuaran.UI.Types;
using FsAction = Fuaran.UI.Types.Action<object>;

namespace Fuaran.UI.CSharp;

// Phase 305 — the Input kinds (Button ships in the foundation). Change/submit
// handlers are opaque to the wire (§4), so they default to no-ops; the bound
// state (field values, select value) is what rides the wire.
public static partial class Fuaran
{
    // F# `string option` projects to C# as the nullable-annotated `FSharpOption<string>?`
    // (F# 10 nullness). These helpers instantiate the option-value binding + no-op handler
    // with the matching annotation, so the smart-ctor call sites stay CS8620-clean.
    internal static FsTypes.Binding<Microsoft.FSharp.Core.FSharpOption<string>?> OptStrValue(string? selected) =>
        FsTypes.Binding<Microsoft.FSharp.Core.FSharpOption<string>?>.NewStatic(Fs.OptStr(selected!));

    internal static Microsoft.FSharp.Core.FSharpFunc<Microsoft.FSharp.Core.FSharpOption<string>?, FsAction> NoOptStrHandler() =>
        Fs.Func<Microsoft.FSharp.Core.FSharpOption<string>?, FsAction>(_ => NoAction);

    internal static FsTypes.Binding<Microsoft.FSharp.Collections.FSharpList<FsTypes.SelectOption>> OptionSource(
        IEnumerable<(string Value, string Label)>? options) =>
        FsTypes.Binding<Microsoft.FSharp.Collections.FSharpList<FsTypes.SelectOption>>.NewStatic(
            Fs.List((options ?? Enumerable.Empty<(string, string)>())
                .Select(o => new FsTypes.SelectOption(o.Value, FsTypes.TextSource.NewLiteral(o.Label)))));

    /// <summary>A form — an ordered list of fields plus a submit action.</summary>
    public static FuaranNode Form(FormOptions options) =>
        new(FsFactory.form<object>(
            options.Id,
            new FsTypes.FormSpec<object>(
                Fs.List((options.Fields ?? Enumerable.Empty<FormField>()).Select(f => f.Inner)),
                NoAction,
                options.SubmitLabel.Inner,
                Fs.None<FsTypes.Binding<bool>>())));

    /// <summary>A single-select dropdown.</summary>
    public static FuaranNode Select(SelectOptions options) =>
        new(FsFactory.select<object>(
            options.Id,
            new FsTypes.SelectSpec<object>(
                options.Label.Inner,
                OptionSource(options.Options),
                OptStrValue(options.Value),
                NoOptStrHandler(),
                options.Placeholder is { } p ? Fs.Some(p.Inner) : Fs.None<FsTypes.TextSource>(),
                Fs.None<FsTypes.Binding<bool>>(),
                false,
                Fs.None<FsTypes.Binding<Microsoft.FSharp.Collections.FSharpList<string>>>(),
                Fs.None<Microsoft.FSharp.Core.FSharpFunc<Microsoft.FSharp.Collections.FSharpList<string>, FsAction>>())));

    /// <summary>A multi-select (<c>&lt;select multiple&gt;</c>).</summary>
    public static FuaranNode MultiSelect(MultiSelectOptions options) =>
        new(FsFactory.multiSelect<object>(
            options.Id,
            options.Label.Inner,
            OptionSource(options.Options),
            FsTypes.Binding<Microsoft.FSharp.Collections.FSharpList<string>>.NewStatic(
                Fs.List(options.Values ?? Enumerable.Empty<string>())),
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
            new FsTypes.FileUploadSpec<object>(
                options.Label.Inner,
                Fs.List(options.Accept ?? Enumerable.Empty<string>()),
                options.Multiple,
                Fs.Func<Microsoft.FSharp.Collections.FSharpList<FsTypes.FileSelection>, FsAction>(_ => NoAction),
                Fs.None<FsTypes.Binding<bool>>())));
}

/// <summary>A form field — build with the static factories (<see cref="Text"/> / <see cref="Number"/> / …).</summary>
public sealed class FormField
{
    internal FsTypes.FormField<object> Inner { get; }

    private FormField(FsTypes.FormField<object> inner) => Inner = inner;

    private static FormField Make(string id, Text label, FsTypes.FormFieldKind<object> kind, bool required, Text? help) =>
        new(new FsTypes.FormField<object>(
            id,
            label.Inner,
            kind,
            required,
            help is { } h ? Fs.Some(h.Inner) : Fs.None<FsTypes.TextSource>()));

    /// <summary>A text field.</summary>
    public static FormField Text(string id, Text label, string initial = "", bool required = false, Text? help = null) =>
        Make(id, label, FsTypes.FormFieldKind<object>.NewText(FsTypes.Binding<string>.NewStatic(initial), NoFieldHandler<string>()), required, help);

    /// <summary>A number field.</summary>
    public static FormField Number(string id, Text label, double initial = 0.0, bool required = false, Text? help = null) =>
        Make(id, label, FsTypes.FormFieldKind<object>.NewNumber(FsTypes.Binding<double>.NewStatic(initial), NoFieldHandler<double>()), required, help);

    /// <summary>A checkbox field.</summary>
    public static FormField Checkbox(string id, Text label, bool initial = false, bool required = false, Text? help = null) =>
        Make(id, label, FsTypes.FormFieldKind<object>.NewCheckbox(FsTypes.Binding<bool>.NewStatic(initial), NoFieldHandler<bool>()), required, help);

    /// <summary>A multi-line text-area field.</summary>
    public static FormField TextArea(string id, Text label, int rows = 4, string initial = "", bool required = false, Text? help = null) =>
        Make(id, label, FsTypes.FormFieldKind<object>.NewTextArea(FsTypes.Binding<string>.NewStatic(initial), NoFieldHandler<string>(), rows), required, help);

    /// <summary>A single-choice (dropdown) field.</summary>
    public static FormField Choice(string id, Text label, string? selected, IEnumerable<(string Value, string Label)> options, bool required = false, Text? help = null) =>
        Make(
            id,
            label,
            FsTypes.FormFieldKind<object>.NewChoice(
                Fuaran.OptionSource(options),
                Fuaran.OptStrValue(selected),
                Fuaran.NoOptStrHandler()),
            required,
            help);

    private static Microsoft.FSharp.Core.FSharpFunc<T, FsAction> NoFieldHandler<T>() =>
        Fs.Func<T, FsAction>(_ => FsAction.NewChain(Fs.Empty<FsAction>()));
}

/// <summary>A filter chip — build with the static factories.</summary>
public sealed class Filter
{
    internal FsTypes.FilterSpec<object> Inner { get; }

    private Filter(FsTypes.FilterSpec<object> inner) => Inner = inner;

    // 0.2.0 filters-unification: a chip's control is an ordinary FormFieldKind
    // auto-bound to its own filter key (Binding.Filter(name, None)); the
    // declarative shape carries no handler — the renderer's write-back default
    // writes $filters.<name>. Mirror of the F# FilterField module.

    /// <summary>A free-text filter chip bound to its own filter key.</summary>
    public static Filter Text(string name, Text label) =>
        new(new FsTypes.FilterSpec<object>(
            name,
            label.Inner,
            FsTypes.FormFieldKind<object>.NewText(
                FsTypes.Binding<string>.NewFilter(name, Microsoft.FSharp.Core.FSharpOption<string>.None),
                null)));

    /// <summary>A choice filter chip bound to its own filter key.</summary>
    public static Filter Choice(string name, Text label, IEnumerable<(string Value, string Label)> options) =>
        new(new FsTypes.FilterSpec<object>(
            name,
            label.Inner,
            FsTypes.FormFieldKind<object>.NewChoice(
                Fuaran.OptionSource(options),
                FsTypes.Binding<Microsoft.FSharp.Core.FSharpOption<string>?>.NewFilter(
                    name,
                    Microsoft.FSharp.Core.FSharpOption<Microsoft.FSharp.Core.FSharpOption<string>?>.None),
                null)));
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
