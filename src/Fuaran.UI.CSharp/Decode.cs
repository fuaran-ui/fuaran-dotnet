using System.Diagnostics.CodeAnalysis;
using FsDecode = Fuaran.UI.Ops.JsonDecode;
using FsCanon = Fuaran.UI.OpStream.Abstractions.CanonicalJson;
using FsDecodeError = Fuaran.UI.Ops.JsonDecode.DecodeError;
using FsNode = Fuaran.UI.Types.Node<object>;
using FsOp = Fuaran.UI.Ops.Types.TreeOp<object>;

namespace Fuaran.UI.CSharp;

// ============================================================================
//  Phase 309 — C#-native decode ergonomics. A C# consumer reading wire JSON never
//  touches an FSharp.Core result type: the decoder's
//  `FSharpResult<Node<obj>, DecodeError>` is projected to a C# try-pattern +
//  result object, and the F# `DecodeError` to a C#-native record carrying the
//  canonical code (as both a string and a parsed enum) + path + message.
//
//  There is no C# decoder — this wraps the shared F# `JsonDecode` (the wire is
//  decoded once, canonically), so it stays byte-faithful by construction.
// ============================================================================

/// <summary>The six canonical decode-error codes (§6 of the wire format).</summary>
public enum DecodeErrorCode
{
    /// <summary>The input is not syntactically valid JSON.</summary>
    InvalidJson,

    /// <summary>A required key is absent.</summary>
    MissingField,

    /// <summary>A value is present but the wrong JSON kind.</summary>
    WrongType,

    /// <summary>A <c>$type</c> discriminator / bare-enum string is unrecognised.</summary>
    UnknownDuCase,

    /// <summary>The top-level <c>kind.$type</c> is not a recognised node kind.</summary>
    WrongNodeKind,

    /// <summary>An <c>id</c> is present but empty.</summary>
    EmptyNodeId,

    /// <summary>A code outside the canonical six (forward-compat guard).</summary>
    Unknown,
}

/// <summary>A structured, C#-native decode error — no FSharp.Core result type on the surface.</summary>
public sealed record DecodeError
{
    /// <summary>The canonical code string (e.g. <c>"MISSING_FIELD"</c>).</summary>
    public required string Code { get; init; }

    /// <summary>The canonical code as a C# enum.</summary>
    public required DecodeErrorCode Kind { get; init; }

    /// <summary>A <c>$</c>-rooted dotted path to the failure (e.g. <c>$.kind.$type</c>).</summary>
    public required string Path { get; init; }

    /// <summary>A human/AI-readable description.</summary>
    public required string Message { get; init; }

    /// <summary>An optional expected-shape hint (populated on node-side rejects).</summary>
    public string? ExpectedShape { get; init; }

    internal static DecodeError FromFs(FsDecodeError e) =>
        new()
        {
            Code = e.Code,
            Kind = e.Code switch
            {
                "INVALID_JSON" => DecodeErrorCode.InvalidJson,
                "MISSING_FIELD" => DecodeErrorCode.MissingField,
                "WRONG_TYPE" => DecodeErrorCode.WrongType,
                "UNKNOWN_DU_CASE" => DecodeErrorCode.UnknownDuCase,
                "WRONG_NODE_KIND" => DecodeErrorCode.WrongNodeKind,
                "EMPTY_NODE_ID" => DecodeErrorCode.EmptyNodeId,
                _ => DecodeErrorCode.Unknown,
            },
            Path = e.Path,
            Message = e.Message,
            ExpectedShape = Fs.ToNullable(e.ExpectedShape),
        };
}

/// <summary>The outcome of a decode — exactly one of <see cref="Value"/> / <see cref="Error"/> is set.</summary>
/// <typeparam name="T">The decoded handle type.</typeparam>
public sealed class DecodeResult<T> where T : class
{
    private DecodeResult(T? value, DecodeError? error)
    {
        Value = value;
        Error = error;
    }

    /// <summary>Whether the decode succeeded.</summary>
    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsOk => Error is null;

    /// <summary>The decoded value, or null on failure.</summary>
    public T? Value { get; }

    /// <summary>The decode error, or null on success.</summary>
    public DecodeError? Error { get; }

    internal static DecodeResult<T> Ok(T value) => new(value, null);

    internal static DecodeResult<T> Fail(DecodeError error) => new(null, error);
}

/// <summary>A decoded tree edit — the opaque public handle over the F# <c>TreeOp&lt;obj&gt;</c>.</summary>
public sealed class FuaranOp
{
    internal FsOp Inner { get; }

    internal FuaranOp(FsOp inner) => Inner = inner;

    /// <summary>Encode this op back to its canonical wire JSON (via the shared F# encoder).</summary>
    public string Encode() => FsCanon.encodeOp(Inner);
}

/// <summary>C#-native decode entry points over the shared F# <c>JsonDecode</c>.</summary>
public static class Decode
{
    /// <summary>Decode a wire-JSON node to the re-encodable <see cref="FuaranNode"/> handle.
    /// Uses the raw <c>decodeNodeObj</c> path (not the F# <c>WireTree</c> marker): the C# surface
    /// authors, decodes, encodes, and server-renders, but has no live client dispatch loop, so the
    /// "render a decoded tree with inert closures live" footgun the F# <c>WireTree</c> guards does
    /// not arise here — a decoded <see cref="FuaranNode"/> is only ever re-encoded or re-authored.</summary>
    public static DecodeResult<FuaranNode> Node(string json)
    {
        var result = FsDecode.decodeNodeObj(json);
        return result.IsOk
            ? DecodeResult<FuaranNode>.Ok(new FuaranNode(result.ResultValue))
            : DecodeResult<FuaranNode>.Fail(DecodeError.FromFs(result.ErrorValue));
    }

    /// <summary>Decode a wire-JSON tree op.</summary>
    public static DecodeResult<FuaranOp> Op(string json)
    {
        var result = FsDecode.decodeOp(json);
        return result.IsOk
            ? DecodeResult<FuaranOp>.Ok(new FuaranOp(result.ResultValue))
            : DecodeResult<FuaranOp>.Fail(DecodeError.FromFs(result.ErrorValue));
    }

    /// <summary>Try-pattern node decode: returns true + <paramref name="node"/> on success, false + <paramref name="error"/> on failure.</summary>
    public static bool TryNode(string json, [NotNullWhen(true)] out FuaranNode? node, [NotNullWhen(false)] out DecodeError? error)
    {
        var r = Node(json);
        node = r.Value;
        error = r.Error;
        return r.IsOk;
    }

    /// <summary>Try-pattern op decode.</summary>
    public static bool TryOp(string json, [NotNullWhen(true)] out FuaranOp? op, [NotNullWhen(false)] out DecodeError? error)
    {
        var r = Op(json);
        op = r.Value;
        error = r.Error;
        return r.IsOk;
    }

    /// <summary>Decode then re-encode a node, returning whether the bytes round-trip identically.</summary>
    public static bool NodeRoundTrips(string json) =>
        TryNode(json, out var node, out _) && node.Encode() == json;

    /// <summary>Decode then re-encode an op, returning whether the bytes round-trip identically.</summary>
    public static bool OpRoundTrips(string json) =>
        TryOp(json, out var op, out _) && op.Encode() == json;
}
