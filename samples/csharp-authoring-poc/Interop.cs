using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Fuaran.UI.CSharp.Poc;

/// <summary>
/// The thin F#-interop seam the fluent builders sit on top of. Everything that
/// "fights C#" when authoring an F#-shaped tree from C# is concentrated here so
/// the builder surface above can read as plain C#:
///
///   * F# <c>'a option</c> surfaces as <see cref="FSharpOption{T}"/> — <c>Some</c>
///     is a wrapper, <c>None</c> is <c>null</c>. C# has no literal for either.
///   * F# <c>'a list</c> surfaces as <see cref="FSharpList{T}"/> — an immutable
///     cons list with no collection-initialiser support.
///   * F# function values surface as <see cref="FSharpFunc{T, TResult}"/> — a
///     C# lambda must be bridged through <see cref="FuncConvert"/>.
///   * F# <c>Map</c> surfaces as <see cref="FSharpMap{TKey, TValue}"/>.
///
/// These are exactly the friction points the idiom audit (see
/// <c>docs/csharp-authoring-poc-findings.md</c>) records as the gap between this
/// PoC and a supportable <c>Fuaran.UI.CSharp</c> package, which would ship a
/// hand-written null-annotated facade instead of leaking FSharp.Core types.
/// </summary>
internal static class Fs
{
    /// <summary>F# <c>Some x</c>.</summary>
    public static FSharpOption<T> Some<T>(T value) => FSharpOption<T>.Some(value);

    /// <summary>F# <c>None</c> (a typed <c>null</c>).</summary>
    public static FSharpOption<T> None<T>() => FSharpOption<T>.None;

    /// <summary>F# <c>[ a; b; c ]</c> from a C# params array.</summary>
    public static FSharpList<T> List<T>(params T[] items) => ListModule.OfArray(items);

    /// <summary>The empty F# list <c>[]</c>.</summary>
    public static FSharpList<T> Empty<T>() => FSharpList<T>.Empty;

    /// <summary>Bridge a unary C# lambda to an F# function value.</summary>
    public static FSharpFunc<TArg, TResult> Func<TArg, TResult>(System.Func<TArg, TResult> f) =>
        FuncConvert.FromFunc(f);

    /// <summary>The empty F# <c>Map</c>.</summary>
    public static FSharpMap<TKey, TValue> EmptyMap<TKey, TValue>()
        where TKey : notnull =>
        MapModule.Empty<TKey, TValue>();
}
