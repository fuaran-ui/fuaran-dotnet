using FsCanonicalJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson;
using FsNode = Fuaran.UI.Types.Node<object>;

namespace Fuaran.UI.CSharp;

/// <summary>
/// An authored Fuaran UI node — the opaque public handle a C# author works with.
/// It wraps the F# <c>Node&lt;obj&gt;</c> tree the factories build, keeping that
/// runtime type an implementation detail: no consumer signature names it, so the
/// engine can later swap from the F#-veneer to an IDL-generated host without
/// touching consumer code (Phase 304's IDL-bridge rule).
/// </summary>
/// <remarks>
/// The <c>Node&lt;obj&gt;</c> baseline is correct by construction: the wire carries
/// no <c>'Msg</c> (§4g), so a C# author never names a message type. Message
/// payloads are opaque to the encoder (the <c>"&lt;closure&gt;"</c> sentinel).
/// </remarks>
public sealed class FuaranNode
{
    internal FsNode Inner { get; }

    internal FuaranNode(FsNode inner) => Inner = inner;

    /// <summary>The node's id.</summary>
    public string Id => Inner.Id.Item;

    /// <summary>
    /// Encode this node to its canonical wire JSON, via the shared F# encoder
    /// (<c>CanonicalJson.encodeNode</c>). There is no parallel C# encoder — the
    /// veneer is wire-faithful by construction because the same encoder produces
    /// the bytes the F# host produces.
    /// </summary>
    public string Encode() => FsCanonicalJson.encodeNode(Inner);
}

/// <summary>Top-level convenience entry points on the veneer.</summary>
public static class Wire
{
    /// <summary>Encode a node to its canonical wire JSON.</summary>
    public static string Encode(FuaranNode node) => node.Encode();
}
