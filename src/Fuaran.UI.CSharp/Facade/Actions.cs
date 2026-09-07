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
// wire-faithful), `Confirm` and `Focus` (Phase 1537 — both wire-representable, and
// `Confirm` is faithful exactly as far as its continuations are), and a `Chain` of
// those. It excludes `Dispatch`, whose message is a host closure — see the note on
// `FuaranAction`.
//
// SPEC-CONSTRUCTION-TRIPWIRE — the `new FsGen.InvokeArg(…)` call below (Phase
// 1532) is positional on purpose. C# has no copy-and-update over an F# record,
// so an additive slot on that record lands here as CS7036, at the one site that
// decides whether the veneer exposes the new slot or passes the F# default
// explicitly. That is the mechanism, not churn to be routed around; the VB tier
// authors through this veneer, so it is the tripwire for both languages. Pinned
// in both directions, this marker included, by
// src/Fuaran.UI.Tests/SpecConstructionTests.fs ("The C# authoring veneer").

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

    /// <summary>
    /// Write <paramref name="text"/> to the reader's clipboard —
    /// <c>Action.WriteToClipboard</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The payload is a <see cref="Text"/> rather than a <see cref="string"/> since
    /// Phase 1126, so what a reader copies may be a bound value — a figure in the
    /// grid they are looking at, a reference the tree computed — and not only a
    /// literal the author typed. A <see cref="string"/> converts implicitly, so the
    /// literal case reads no differently than it would with a string parameter.
    /// A bound payload resolves at the moment the reader asks, through the same
    /// binding resolver the surrounding tree renders through.
    /// </para>
    /// <para>
    /// Wire-representable in full, which is the boundary this facade is drawn on: the
    /// payload survives serialisation exactly, so a decoding browser copies what a
    /// full-Fable tree would. There is no clipboard <em>read</em> here or anywhere in
    /// the language — a tree that could read the clipboard without a paste gesture is
    /// a keylogger-adjacent capability, and paste is user-initiated by construction.
    /// </para>
    /// </remarks>
    public static FuaranAction WriteToClipboard(Text text) =>
        new(FsAction.NewWriteToClipboard(text.Inner));

    /// <summary>
    /// Navigate the reader to <paramref name="route"/>, in the browsing context
    /// <paramref name="target"/> names (Phase 1536).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The route is a <see cref="Text"/> rather than a string, so it may be a value the
    /// tree computed — the id of the row the reader has selected, say — and not only a
    /// literal typed at authoring time. A bound route is resolved when the reader raises
    /// the action, and the host's URL floor and dispatch policy then judge the RESOLVED
    /// destination; a route that does not resolve navigates nowhere.
    /// </para>
    /// <para>
    /// <see cref="NavigateTarget.Blank"/> opens a fresh browsing context with
    /// <c>noopener,noreferrer</c>. That is the renderer's obligation on every host, not
    /// a per-host setting, so it holds wherever this tree is rendered.
    /// </para>
    /// </remarks>
    public static FuaranAction Navigate(Text route, NavigateTarget target) =>
        new(FsAction.NewNavigate(route.Inner, target.ToFs()));

    /// <summary>
    /// Navigate the reader to <paramref name="route"/> in the current browsing context —
    /// the shortest spelling of the commonest intent. The general form, including a
    /// bound route and a target, is
    /// <see cref="Navigate(Text, NavigateTarget)"/>.
    /// </summary>
    public static FuaranAction Navigate(string route) =>
        new(FsAction.NewNavigate(Text.Literal(route).Inner, FsGen.NavigateTarget.Self));

    // Phase 1532 — the rest of the wire-representable vocabulary. Phase 1153 opened
    // this surface with `Notify` / `Call` / `Print`; everything below was still
    // unauthorable from C# (and so from the VB dialect, which translates through
    // here), which left the veneer unable to write a reactive slot, commit a form
    // field, reach a registered capability, read an uploaded file, or raise a tool
    // call — the whole of the language's declarative host vocabulary bar the pieces
    // 1153 admitted. Each is wire-representable in full, which is the boundary this
    // facade is drawn on.

    /// <summary>
    /// Write <paramref name="value"/> to the reactive
    /// <c>$state.<paramref name="key"/></c> slot. Every <c>Binding.State(key)</c>
    /// reader re-renders.
    /// </summary>
    public static FuaranAction SetState(string key, Payload value) =>
        new(FsAction.NewSetState(key, Fs.Some(value.Inner), Fs.None<FsGen.Binding<FsJVal>>()));

    /// <summary>
    /// Write the value a BINDING resolves to at dispatch time into
    /// <c>$state.<paramref name="key"/></c> (Phase 818) — "copy what that other slot
    /// holds into this one", with no literal to keep in step.
    /// </summary>
    /// <remarks>
    /// The wire enforces <c>value</c> XOR <c>valueFrom</c>: an action carries a
    /// literal or a source, never both, so there is never a question of which won.
    /// </remarks>
    public static FuaranAction SetStateFrom(string key, Binding<Payload> valueFrom) =>
        new(FsAction.NewSetState(
            key,
            Fs.None<FsJVal>(),
            Fs.Some(Fs.MapBinding(valueFrom.Inner, (Payload p) => p.Inner))));

    /// <summary>
    /// Commit the LOCAL buffer of the field on node <paramref name="nodeId"/> — the
    /// explicit counterpart of <see cref="LocalFlush.OnCommitAction"/>.
    /// </summary>
    public static FuaranAction CommitLocal(string nodeId) =>
        new(FsAction.NewCommitLocal(nodeId));

    /// <summary>
    /// Dispatch a host-registered CAPABILITY (Phase 283) for its effect.
    /// <see cref="Binding.Invoke{T}"/> is the twin that dispatches one for a VALUE.
    /// </summary>
    public static FuaranAction Invoke(string capabilityId, params (string Addr, string Value)[] args) =>
        new(FsAction.NewInvoke(capabilityId, Fs.List(args.Select(a => new FsGen.InvokeArg(a.Addr, a.Value)))));

    /// <summary>
    /// Read the body of an uploaded file, in the given <paramref name="encoding"/>.
    /// </summary>
    /// <remarks>
    /// The F# tier's <c>onRead</c> callback is absent here for the same reason
    /// <c>Dispatch</c> is: it is a host closure with no wire projection. The
    /// closure-free form is what the wire carries, and a host binds its handler to
    /// the artifact's declared action hole.
    /// </remarks>
    public static FuaranAction ReadFileBody(string fileRef, FileEncoding encoding) =>
        new(FsAction.NewReadFileBody(
            fileRef,
            Fs.None<object>(),
            encoding.ToFs(),
            Fs.None<Microsoft.FSharp.Core.FSharpFunc<string, object>>()));

    /// <summary>
    /// Raise a named AI tool call with a JSON argument payload — the language's own
    /// hole for "ask the model to do this", carried as data like every other action.
    /// </summary>
    public static FuaranAction AiTool(string toolName, Payload args) =>
        new(FsAction.NewAiTool(toolName, args.Inner));

    /// <summary>
    /// Ask the reader <paramref name="prompt"/>, then raise
    /// <paramref name="onConfirm"/> if they accept — <c>Action.Confirm</c>
    /// (Phase 1537).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The prompt is a <see cref="Text"/> rather than a string, so the question may
    /// name what the reader has selected — "Delete the 3 selected orders?" — and not
    /// only a sentence typed at authoring time. It resolves at the moment the reader
    /// raises the action.
    /// </para>
    /// <para>
    /// The continuation is dispatched through the SAME gate every other action meets,
    /// so a host that refuses navigation refuses it here too: a confirmation is not a
    /// way to reach an effect the host declines. Nor is it an authorisation — it is a
    /// courtesy to the reader, and anything that must not happen without permission is
    /// refused by the host's policy rather than by the question.
    /// </para>
    /// <para>
    /// A confirm inside a confirm's continuation is refused when the tree is decoded:
    /// confirmation is bounded at one question.
    /// </para>
    /// </remarks>
    public static FuaranAction Confirm(Text prompt, FuaranAction onConfirm) =>
        new(FsAction.NewConfirm(prompt.Inner, onConfirm.Inner, Fs.None<FsAction>()));

    /// <summary>
    /// Ask the reader <paramref name="prompt"/>, then raise
    /// <paramref name="onConfirm"/> if they accept or <paramref name="onCancel"/> if
    /// they decline.
    /// </summary>
    /// <remarks>
    /// An author who wants nothing to happen on a decline uses
    /// <see cref="Confirm(Text, FuaranAction)"/> — an absent cancel branch is how the
    /// language spells "nothing happens", and passing an empty chain here would say
    /// the same thing less clearly.
    /// </remarks>
    public static FuaranAction Confirm(Text prompt, FuaranAction onConfirm, FuaranAction onCancel) =>
        new(FsAction.NewConfirm(prompt.Inner, onConfirm.Inner, Fs.Some(onCancel.Inner)));

    /// <summary>
    /// Move keyboard focus to the node with id <paramref name="nodeId"/> —
    /// <c>Action.Focus</c> (Phase 1537).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The id is a plain string, never a binding: it addresses a node in this document,
    /// which the author wrote, so there is nothing here for the tree to compute.
    /// </para>
    /// <para>
    /// What this does not claim: nothing about scrolling (a host may scroll as a
    /// consequence of focusing, and this neither asks it to nor prevents it) and
    /// nothing about selection (focus is not a caret position or a text range). A node
    /// id that addresses nothing warns and moves nothing.
    /// </para>
    /// </remarks>
    public static FuaranAction Focus(string nodeId) => new(FsAction.NewFocus(nodeId));

    /// <summary>Raise several actions in order.</summary>
    public static FuaranAction Chain(params FuaranAction[] actions) =>
        new(FsAction.NewChain(Fs.List(actions.Select(a => a.Inner))));

    /// <summary>Raise several actions in order.</summary>
    public static FuaranAction Chain(IEnumerable<FuaranAction> actions) =>
        new(FsAction.NewChain(Fs.List(actions.Select(a => a.Inner))));
}
