// The FSharp.Core bridge — internal-only. Every place authoring an F#-shaped
// value "fights C#" is concentrated here, so the public facade above reads as
// plain C# with no FSharp.Core type on any signature:
//
//   * F# `'a option`  -> FSharpOption<T>  (Some is a wrapper, None is null).
//   * F# `'a list`    -> FSharpList<T>    (an immutable cons list).
//   * F# function     -> FSharpFunc<T,R>  (a C# lambda bridged via FuncConvert).
//   * F# `Map`        -> FSharpMap<K,V>.
//
// FSharp.Core ships null-oblivious metadata, so nullable-reference analysis over
// this seam is noise rather than signal — the file scopes `#nullable disable` so
// the rest of the package can compile clean under TreatWarningsAsErrors while the
// public facade stays fully null-annotated (Phase 304's "restore <Nullable>enable
// once the seam is annotated" — the seam is annotated by isolating it here).
#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Fuaran.UI.CSharp;

/// <summary>
/// The thin FSharp.Core interop seam. <c>internal</c> — nothing here is part of
/// the public surface. Named tersely (<c>Fs</c>) because call sites in the
/// factory internals read better as <c>Fs.Some(x)</c> / <c>Fs.List(xs)</c>.
/// </summary>
internal static class Fs
{
    /// <summary>F# <c>Some x</c>.</summary>
    public static FSharpOption<T> Some<T>(T value) => FSharpOption<T>.Some(value);

    /// <summary>F# <c>None</c> (a typed <c>null</c>).</summary>
    public static FSharpOption<T> None<T>() => FSharpOption<T>.None;

    /// <summary>Project a C# nullable reference to an F# <c>option</c>.</summary>
    public static FSharpOption<T> OfNullable<T>(T value) where T : class =>
        value is null ? FSharpOption<T>.None : FSharpOption<T>.Some(value);

    /// <summary>Project a C# <see cref="Nullable{T}"/> to an F# <c>option</c>.</summary>
    public static FSharpOption<T> OfNullable<T>(T? value) where T : struct =>
        value.HasValue ? FSharpOption<T>.Some(value.Value) : FSharpOption<T>.None;

    /// <summary>Project a nullable string to an F# <c>string option</c> (oblivious return type
    /// avoids the nullable-mismatch the generic overload's inferred <c>string?</c> triggers).</summary>
    public static FSharpOption<string> OptStr(string value) =>
        value is null ? FSharpOption<string>.None : FSharpOption<string>.Some(value);

    /// <summary>F# <c>[ a; b; c ]</c> from a C# sequence.</summary>
    public static FSharpList<T> List<T>(IEnumerable<T> items) => ListModule.OfSeq(items);

    /// <summary>F# <c>[ a; b; c ]</c> from a C# params array.</summary>
    public static FSharpList<T> ListOf<T>(params T[] items) => ListModule.OfArray(items);

    /// <summary>The empty F# list <c>[]</c>.</summary>
    public static FSharpList<T> Empty<T>() => FSharpList<T>.Empty;

    /// <summary>Bridge a unary C# lambda to an F# function value.</summary>
    public static FSharpFunc<TArg, TResult> Func<TArg, TResult>(Func<TArg, TResult> f) =>
        FuncConvert.FromFunc(f);

    /// <summary>The empty F# <c>Map</c>.</summary>
    public static FSharpMap<TKey, TValue> EmptyMap<TKey, TValue>() where TKey : notnull =>
        MapModule.Empty<TKey, TValue>();

    /// <summary>Build an F# <c>Map</c> from C# key/value pairs.</summary>
    public static FSharpMap<TKey, TValue> Map<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> pairs)
        where TKey : notnull =>
        MapModule.OfSeq(pairs.Select(kv => Tuple.Create(kv.Key, kv.Value)));

    /// <summary>Read an F# <c>option</c> to a C# nullable reference (or null).</summary>
    public static T ToNullable<T>(FSharpOption<T> opt) where T : class =>
        FSharpOption<T>.get_IsSome(opt) ? opt.Value : null;
}
