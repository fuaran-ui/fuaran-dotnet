using System;
using System.Collections.Generic;
using System.Linq;
using FsCanonicalJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson;
using FsGen = Fuaran.UI.Generated;
using FsNode = Fuaran.UI.Generated.Node<object>;

// SPEC-CONSTRUCTION-TRIPWIRE — the `new FsGen.<X>(…)` call in `WithDirection` below is
// positional on purpose, and must stay so. C# has no copy-and-update over an F# record, so
// an additive `SemanticStyle` slot lands here as CS7036 — at the one site that has to
// decide whether the veneer exposes the new slot or passes the F# default explicitly. That
// is the mechanism, not churn to be routed around; the VB tier authors through this veneer,
// so it is the tripwire for both languages. Pinned in both directions, this marker included,
// by src/Fuaran.UI.Tests/SpecConstructionTests.fs ("The C# authoring veneer").

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
    public string Id => Inner.Id;

    /// <summary>
    /// Encode this node to its canonical wire JSON, via the shared F# encoder
    /// (<c>CanonicalJson.encodeNode</c>). There is no parallel C# encoder — the
    /// veneer is wire-faithful by construction because the same encoder produces
    /// the bytes the F# host produces.
    /// </summary>
    public string Encode() => FsCanonicalJson.encodeNode(Inner);

    /// <summary>
    /// Encode this node for TRANSPORT — the same canonical bytes as
    /// <see cref="Encode"/>, but throwing when the tree carries interaction that
    /// serialisation would lose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three <c>Action</c> cases carry host closures — <c>Dispatch</c>'s message,
    /// <c>Call</c>'s <c>onResult</c>, <c>ReadFileBody</c>'s <c>onRead</c>. The
    /// canonical encoder emits the case discriminator and DROPS the payload, and
    /// the decoder rebuilds it as the <c>"&lt;closure&gt;"</c> sentinel, so a
    /// decoding host receives an affordance that renders, fires, and does
    /// nothing. The emitted bytes carry no trace of the loss, which is why the
    /// encoding side is the last place the question is answerable.
    /// </para>
    /// <para>
    /// <see cref="Encode"/> is unchanged and does not refuse: its
    /// closure-blindness feeds the op-stream hash chain, where two ops differing
    /// only in an opaque message hash identically by design. Which method you
    /// call is how intent is declared.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The tree carries a closure-bearing action. The message names every
    /// offending node and slot, so the tree is repaired in one pass.
    /// </exception>
    public string EncodeForTransport()
    {
        if (TryEncodeForTransport(out var json, out var lossy))
        {
            return json!;
        }

        var slots = string.Join(", ", lossy.Select(p => $"{p.NodeId} ({p.Slot})"));

        throw new InvalidOperationException(
            $"This tree carries interaction that would not survive serialisation: {slots}. "
            + "Replace it with a wire-representable action, or render the tree in process "
            + "where the closure never crosses a wire.");
    }

    /// <summary>
    /// Encode this node for transport, reporting every lossy slot instead of
    /// throwing. <c>false</c> leaves <paramref name="json"/> null and fills
    /// <paramref name="lossy"/>.
    /// </summary>
    public bool TryEncodeForTransport(out string? json, out IReadOnlyList<LossySlot> lossy)
    {
        var result = FsCanonicalJson.encodeNodeForTransport(Inner);

        if (result.IsOk)
        {
            json = result.ResultValue;
            lossy = Array.Empty<LossySlot>();
            return true;
        }

        json = null;
        lossy = result.ErrorValue.Select(p => new LossySlot(p.NodeId, p.Slot)).ToArray();
        return false;
    }

    /// <summary>
    /// Attach the node-level tooltip trait (Phase 1112) — a supplementary hint
    /// ABOUT this node, revealed by the renderer's own hover / focus /
    /// long-press affordance and announced through <c>aria-describedby</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A hint DESCRIBES; it does not NAME. On a control whose own text is empty
    /// — an icon-only button is the case this was built for — the node still
    /// needs an accessible name of its own, or it reaches a screen reader with
    /// a description and nothing to attach it to.
    /// </para>
    /// <para>
    /// A decoration on a built node rather than an option on the factory that
    /// built it: the trait is uniform across every kind, so an
    /// <c>Options.Tooltip</c> on each of the forty-one option records would be
    /// forty-one spellings of one thing. Returns a new handle; the receiver is
    /// unchanged.
    /// </para>
    /// </remarks>
    public FuaranNode WithTooltip(Text hint) =>
        new(new FsNode(
            Inner.Id,
            Inner.Kind,
            Inner.Accessibility,
            Inner.ExtraAttributes,
            Inner.Motion,
            Inner.State,
            Inner.Style,
            Fs.Some(hint.Inner)));

    /// <summary>Declare the base direction of the value this node carries
    /// (Fuaran-UI Phase 1472). Returns a new handle; the receiver is unchanged.</summary>
    /// <remarks>
    /// <para>
    /// A CORRECTNESS statement rather than a presentational one, which is why it
    /// is the one member of the style record this surface spells. An account or
    /// reference number declared <see cref="TextDirection.Ltr"/> inside
    /// right-to-left prose is otherwise reordered by the bidirectional
    /// algorithm and read back with its digits out of order; the renderer emits
    /// the matching <c>dir</c> and isolates the run.
    /// </para>
    /// <para>
    /// <see cref="TextDirection.Auto"/> clears the declaration, back to the
    /// byte-identical default. A decoration on a built node rather than an
    /// option on the factory that built it, for the reason
    /// <see cref="WithTooltip"/> gives: the slot is uniform across every kind.
    /// </para>
    /// </remarks>
    public FuaranNode WithDirection(TextDirection direction)
    {
        var style = Fs.ToNullable(Inner.Style) ?? global::Fuaran.UI.Defaults.style;
        var updated = new FsGen.SemanticStyle(
            direction.ToFs(),
            style.Emphasis,
            style.Role,
            style.Tone,
            style.Voice,
            style.Weight);

        return new(new FsNode(
            Inner.Id,
            Inner.Kind,
            Inner.Accessibility,
            Inner.ExtraAttributes,
            Inner.Motion,
            Inner.State,
            updated.Equals(global::Fuaran.UI.Defaults.style)
                ? Fs.None<FsGen.SemanticStyle>()
                : Fs.Some(updated),
            Inner.Tooltip));
    }
}

/// <summary>
/// One place a tree would lose behaviour on the way to the wire — the node
/// carrying the closure, and which slot holds it.
/// </summary>
/// <param name="NodeId">The node the author must repair.</param>
/// <param name="Slot">
/// The slot, spelled as the estate spells it elsewhere:
/// <c>Action.Dispatch.msg</c>, <c>Action.Call.onResult</c>,
/// <c>Action.ReadFileBody.onRead</c>.
/// </param>
public sealed record LossySlot(string NodeId, string Slot);

/// <summary>Top-level convenience entry points on the veneer.</summary>
public static class Wire
{
    /// <summary>Encode a node to its canonical wire JSON.</summary>
    public static string Encode(FuaranNode node) => node.Encode();
}
