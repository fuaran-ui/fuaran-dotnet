using System.Collections.Generic;
using System.Linq;
using FsGen = Fuaran.UI.Generated;
using FsJVal = global::Fuaran.Core.JVal;
using FsAction = Fuaran.UI.Generated.Action<object>;

namespace Fuaran.UI.CSharp;

// Phase 1153 — the Action vocabulary. Until now the veneer exposed none of it:
// `ButtonOptions` had no `OnClick` and the factory hardwired an empty chain, so a
// host round trip was not authorable from C# (nor from the VB dialect, which
// translates through this surface). Both actions this file admits are
// wire-representable with no `'Msg` payload, so closing the gap stays inside the
// design doc's §4e.1 baseline posture: `Node<obj>`, wire-faithful veneer, typed-`Msg`
// builder still deferred.
//
// The vocabulary is DELIBERATELY PARTIAL, and the boundary is the wire rather than
// convenience: an action the veneer can author must survive `encodeNodeForTransport`
// intact. That admits `Notify`, `Call` with an `into:` target (or with neither an
// `into:` nor a result closure), `Print` (Phase 1124 — payload-free, so trivially
// wire-faithful), and a `Chain` of those. It excludes `Dispatch`, whose message is a
// host closure — see the note on `FuaranAction`.

/// <summary>
/// A JSON value — the payload a <see cref="FuaranAction.Notify"/> carries. The
/// authoring facade over the wire's JSON model, on the same pattern as
/// <see cref="Text"/> and <see cref="Binding{T}"/>: a plain <see cref="string"/>,
/// <see cref="int"/>, <see cref="double"/> or <see cref="bool"/> converts
/// implicitly, so <c>Notify("saved", 7)</c> needs no helper call.
/// </summary>
/// <remarks>
/// There is no null case, because the wire's JSON model has none: an absent value
/// is an absent object member, not a member bound to null. Object members are
/// emitted in canonical (sorted) order by the shared encoder, so the order you
/// author them in does not reach the wire.
/// </remarks>
public sealed class Payload
{
    internal FsJVal Inner { get; }

    private Payload(FsJVal inner) => Inner = inner;

    /// <summary>A JSON string.</summary>
    public static implicit operator Payload(string value) => new(FsJVal.NewJStr(value));

    /// <summary>A JSON integer.</summary>
    public static implicit operator Payload(int value) => new(FsJVal.NewJInt(value));

    /// <summary>A JSON number.</summary>
    public static implicit operator Payload(double value) => new(FsJVal.NewJFloat(value));

    /// <summary>A JSON boolean.</summary>
    public static implicit operator Payload(bool value) => new(FsJVal.NewJBool(value));

    /// <summary>An explicit JSON string (identical to the implicit conversion).</summary>
    public static Payload Str(string value) => value;

    /// <summary>An explicit JSON integer (identical to the implicit conversion).</summary>
    public static Payload Int(int value) => value;

    /// <summary>An explicit JSON number (identical to the implicit conversion).</summary>
    public static Payload Number(double value) => value;

    /// <summary>An explicit JSON boolean (identical to the implicit conversion).</summary>
    public static Payload Bool(bool value) => value;

    /// <summary>A JSON array.</summary>
    public static Payload Array(params Payload[] items) =>
        new(FsJVal.NewJArr(Fs.List(items.Select(i => i.Inner))));

    /// <summary>
    /// A JSON object. Member order is not significant — the canonical encoder sorts
    /// keys, so two objects differing only in authoring order produce the same bytes.
    /// </summary>
    public static Payload Object(params (string Key, Payload Value)[] members) =>
        new(FsJVal.NewJObj(Fs.List(members.Select(m => System.Tuple.Create(m.Key, m.Value.Inner)))));
}

/// <summary>
/// An action a control raises — the authoring facade over the F# <c>Action</c>.
/// Assign one to <see cref="ButtonOptions.OnClick"/>,
/// <see cref="FormOptions.OnSubmit"/> or <see cref="ModalOptions.OnDismiss"/>; those
/// are the three slots the wire carries an action document in, and the three the
/// conformance corpus exercises.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is deliberately no <c>Dispatch</c>.</b> That case carries a host closure
/// as its message. The canonical encoder emits the case discriminator and drops the
/// payload, and a decoding host rebuilds it as the <c>"&lt;closure&gt;"</c> sentinel
/// — so a serialised <c>Dispatch</c> arrives as an affordance that renders, fires,
/// and does nothing. Full Fable is the one tier where it survives, because there the
/// tree is never serialised; a veneer whose trees ARE serialised must not be able to
/// mint one. <see cref="FuaranNode.EncodeForTransport"/> refuses such a tree, and
/// this type is the other half of that answer: the refusal cannot fire on a tree this
/// surface authored.
/// </para>
/// <para>
/// Typed host behaviour is reached the other way round — the host binds a handler
/// table to the artifact's declared action holes, which is uniform across hosts and
/// needs no per-language mechanism. Reach the host from a tree with
/// <see cref="Notify"/> (a channel plus a JSON payload) or
/// <see cref="CallIntoState"/> / <see cref="CallIntoQuery"/> (a call whose RESULT is
/// written to a reactive slot rather than handed to a closure). <see cref="Print"/>
/// reaches no host at all — it asks the browser.
/// </para>
/// </remarks>
public sealed class FuaranAction
{
    internal FsAction Inner { get; }

    private FuaranAction(FsAction inner) => Inner = inner;

    /// <summary>The empty chain — an affordance that raises nothing. This is what an
    /// unset action slot carries, so it is the default rather than an author's choice.</summary>
    internal static FuaranAction Empty { get; } = new(FsAction.NewChain(Fs.Empty<FsAction>()));

    /// <summary>
    /// Notify the host on <paramref name="channel"/> with a JSON
    /// <paramref name="payload"/>. Wire-representable in full: both the channel and
    /// the payload survive serialisation, so a decoding browser can raise it and a
    /// host can act on it.
    /// </summary>
    public static FuaranAction Notify(string channel, Payload payload) =>
        new(FsAction.NewNotify(channel, payload.Inner));

    /// <summary>
    /// Call <paramref name="endpoint"/> and discard the response. The
    /// result-carrying forms are <see cref="CallIntoState"/> and
    /// <see cref="CallIntoQuery"/>; the F# tier's closure-taking <c>onResult</c> form
    /// is absent here for the same reason <c>Dispatch</c> is.
    /// </summary>
    public static FuaranAction Call(string endpoint) =>
        new(FsAction.NewCall(endpoint, Fs.None<Microsoft.FSharp.Core.FSharpFunc<object, object>>(), Fs.None<FsGen.CallResultTarget>()));

    /// <summary>
    /// Call <paramref name="endpoint"/> and write the response to the reactive
    /// <c>$state.<paramref name="key"/></c> slot — every <c>Binding.State(key)</c>
    /// reader re-renders on completion. The closure-free declarative fetch.
    /// </summary>
    public static FuaranAction CallIntoState(string endpoint, string key) =>
        new(FsAction.NewCall(
            endpoint,
            Fs.None<Microsoft.FSharp.Core.FSharpFunc<object, object>>(),
            Fs.Some(FsGen.CallResultTarget.NewState(key))));

    /// <summary>
    /// Call <paramref name="endpoint"/> and write the response to the query-results
    /// slot <paramref name="name"/> — every <c>Binding.Query(name)</c> reader
    /// re-renders on completion.
    /// </summary>
    public static FuaranAction CallIntoQuery(string endpoint, string name) =>
        new(FsAction.NewCall(
            endpoint,
            Fs.None<Microsoft.FSharp.Core.FSharpFunc<object, object>>(),
            Fs.Some(FsGen.CallResultTarget.NewQuery(name))));

    /// <summary>
    /// Open the reader's own print dialogue — <c>Action.Print</c> (Phase 1124).
    /// Payload-free, so it is a property rather than a method: there is no page
    /// size, margin, sheet range or target subtree to pass, the paged medium
    /// belonging to the host and every parameter of the printing to the reader.
    /// </summary>
    /// <remarks>
    /// Wire-representable in full, which is the boundary this facade is drawn on:
    /// <c>{"$type":"Print"}</c> survives serialisation exactly, so a decoding
    /// browser raises the same dialogue a full-Fable tree would.
    /// </remarks>
    public static FuaranAction Print { get; } = new(FsAction.Print);

    /// <summary>Raise several actions in order.</summary>
    public static FuaranAction Chain(params FuaranAction[] actions) =>
        new(FsAction.NewChain(Fs.List(actions.Select(a => a.Inner))));

    /// <summary>Raise several actions in order.</summary>
    public static FuaranAction Chain(IEnumerable<FuaranAction> actions) =>
        new(FsAction.NewChain(Fs.List(actions.Select(a => a.Inner))));
}
