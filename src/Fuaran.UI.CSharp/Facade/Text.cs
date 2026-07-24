using FsTypes = Fuaran.UI.Types;

namespace Fuaran.UI.CSharp;

/// <summary>
/// A text source — the authoring facade over the F# <c>TextSource</c>. A plain
/// <see cref="string"/> converts to a literal and a <see cref="Binding{T}"/> of
/// string converts to a bound value, so <c>Label = "Revenue"</c> and
/// <c>Label = Binding.Query&lt;string&gt;("caption")</c> both bind with no helper.
/// </summary>
public readonly struct Text
{
    internal FsTypes.TextSource Inner { get; }

    private Text(FsTypes.TextSource fs) => Inner = fs;

    /// <summary>A literal string.</summary>
    public static implicit operator Text(string literal) =>
        new(FsTypes.TextSource.NewLiteral(literal));

    /// <summary>A bound string value.</summary>
    public static implicit operator Text(Binding<string> bound) =>
        new(FsTypes.TextSource.NewBound(bound.Inner));

    /// <summary>An explicit literal (identical to the implicit <see cref="string"/> conversion).</summary>
    public static Text Literal(string value) => value;

    /// <summary>A bound string value.</summary>
    public static Text Bound(Binding<string> binding) => binding;

    /// <summary>An i18n-key text source (no placeholder arguments).</summary>
    public static Text I18n(string key) =>
        new(FsTypes.TextSource.NewI18n(key, Fs.EmptyMap<string, global::Fuaran.Core.JVal>()));
}
