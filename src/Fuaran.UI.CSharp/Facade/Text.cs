using System.Collections.Generic;
using System.Linq;
using FsGen = Fuaran.UI.Generated;
using FsJVal = Fuaran.Core.JVal;

namespace Fuaran.UI.CSharp;

/// <summary>
/// A text source — the authoring facade over the F# <c>TextSource</c>. A plain
/// <see cref="string"/> converts to a literal and a <see cref="Binding{T}"/> of
/// string converts to a bound value, so <c>Label = "Revenue"</c> and
/// <c>Label = Binding.Query&lt;string&gt;("caption")</c> both bind with no helper.
/// </summary>
public readonly struct Text
{
    internal FsGen.TextSource Inner { get; }

    private Text(FsGen.TextSource fs) => Inner = fs;

    /// <summary>A literal string.</summary>
    public static implicit operator Text(string literal) =>
        new(FsGen.TextSource.NewLiteral(literal));

    /// <summary>A bound string value.</summary>
    public static implicit operator Text(Binding<string> bound) =>
        new(FsGen.TextSource.NewBound(bound.Inner));

    /// <summary>An explicit literal (identical to the implicit <see cref="string"/> conversion).</summary>
    public static Text Literal(string value) => value;

    /// <summary>A bound string value.</summary>
    public static Text Bound(Binding<string> binding) => binding;

    /// <summary>An i18n-key text source (no placeholder arguments).</summary>
    public static Text I18n(string key) =>
        new(FsGen.TextSource.NewI18n(key, Fs.EmptyMap<string, FsGen.Binding<FsJVal>>()));

    /// <summary>
    /// An i18n-key text source with arguments — each <c>{name}</c> placeholder in
    /// the catalogue's template substituted by the named value.
    /// </summary>
    /// <remarks>
    /// Phase 1661 — the arguments are BINDINGS, exactly as
    /// <see cref="Binding.I18n(string, ValueTuple{string, Binding{Payload}}[])"/>'s
    /// are, which is the asymmetry that phase closed: "3 items left" takes its
    /// count from the same reactive slot the list reads. A literal argument is
    /// <c>Binding.Static</c> of a payload and encodes as the bare JSON value, so
    /// the bytes of a literal-args caption are what they always were.
    /// </remarks>
    public static Text I18n(string key, params (string Name, Binding<Payload> Value)[] args) =>
        new(FsGen.TextSource.NewI18n(
            key,
            Fs.Map(args.Select(a =>
                new KeyValuePair<string, FsGen.Binding<FsJVal>>(
                    a.Name,
                    Fs.MapBinding(a.Value.Inner, (Payload p) => p.Inner))))));
}
