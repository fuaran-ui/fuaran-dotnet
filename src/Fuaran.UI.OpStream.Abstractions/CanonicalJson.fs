module Fuaran.UI.OpStream.Abstractions.CanonicalJson

// ============================================================================
//  Canonical-JSON encoder.
//
//  Pinned algorithm in docs/migrations/12-Z-op-stream.md. Two consumers:
//
//   1. Hash chain (server-side). `HashChain.computeHash` calls `encodeOp`
//      to produce the canonical string that feeds SHA-256.
//
//   2. AI pre-emit self-check (Fable-side). Authors call `encodeNode tree`
//      and pipe the result through `ArgsJsonContract.validate` to catch
//      wire-shape violations cheaper than the apply-engine envelope.
//
//  One design (Phase 2061): every payload is built as a `JVal` by the
//  GENERATED structural layer (`Fuaran.UI.Generated.encode*Json`), the op
//  envelope is a `JVal` built here, and the whole is rendered once by
//  `Canonical.write Rendering.CrossHost` — Ordinal-sorted keys, the canonical
//  string escape and the pinned float layout, the same rendering a node gets.
//  This file owns only the op codec's POLICY: which op cases exist, the §16
//  projection of the Node-bearing payloads, and how an obj-erased payload is
//  spelled.
//
//  Fable-compatible: no reflection, no `System.Security.*`.
//
//  Closure rendering: function-typed payloads (Binding accessors, Action
//  callbacks, FormFieldKind onChange handlers, CellKindErased actions,
//  Column.Value projections, etc.) render as the sentinel `"<closure>"`.
//  This is the v1 limitation called out in the migration doc — two ops
//  differing only in opaque `'Msg` / closure payloads hash identically.
//
//  obj-typed values: best-effort dispatch. Recognised primitives (string,
//  bool, integer, float, timestamps) encode as their JSON value; anything else
//  renders as the sentinel `"<opaque>"`. No reflection over arbitrary types.
// ============================================================================

open System
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types

let private opaqueSentinel = "<opaque>"

// ─── The obj-erased payload ───────────────────────────────────────────────
//
// `PropValue.Native` and `ReplaceBinding`'s `Binding<obj>` payload positions
// carry in-process values. The recognised primitives spell as their JSON value;
// anything else — a host domain type, a list, a `JVal` boxed as `obj`, a null
// reference — collapses to `"<opaque>"`, which preserves canonicalisation without
// inventing content the encoder cannot see (the residual-opaque boundary,
// WIRE_FORMAT.md §"Typed Static payloads").
//
// Two spellings follow from the `JVal` model having no `null` and one integer
// width, each the bytes a reader of the old spelling would re-encode to (Phase
// 2061's rulings, recorded in its outcome): an `int64` outside Int32 rides as a
// double — exact, with the same digits, up to 2^53, which is all a reader keeps
// of a longer integer token anyway — and a null reference is opaque.

let private floatJson (f: float) : JVal =
    // A non-finite double rides as its quoted sentinel string (WIRE_FORMAT §5),
    // built here so the value stays renderable by the guarded writer.
    if Double.IsNaN f then JStr "NaN"
    elif Double.IsPositiveInfinity f then JStr "Infinity"
    elif Double.IsNegativeInfinity f then JStr "-Infinity"
    else JFloat f

let private int64Json (n: int64) : JVal =
    if n >= int64 Int32.MinValue && n <= int64 Int32.MaxValue then
        JInt(int n)
    else
        JFloat(float n)

let private payloadJson (v: obj | null) : JVal =
    match v with
    | null -> JStr opaqueSentinel
    | :? string as s -> JStr s
    | :? bool as b -> JBool b
    | :? int as n -> JInt n
    | :? int64 as n -> int64Json n
    | :? float as f -> floatJson f
    | :? float32 as f -> floatJson (float f)
    | :? DateTimeOffset as t -> int64Json (t.ToUnixTimeSeconds())
    | :? DateTime as t -> int64Json (DateTimeOffset(t.ToUniversalTime(), TimeSpan.Zero).ToUnixTimeSeconds())
    | _ -> JStr opaqueSentinel

/// `UpdateProp`'s two-population payload: `Wire` is already a `JVal`; `Native`
/// keeps the best-effort scalar encode above.
let private propValueJson (v: PropValue) : JVal =
    match v with
    | PropValue.Native o -> payloadJson o
    | PropValue.Wire j -> j

// ─── The ReplaceBinding payload ───────────────────────────────────────────
//
// The binding encodes through the generated layer, so an op's binding and a
// node's binding are one encoder. Ahead of it, this projection spells two
// in-process constructions as the canonical ABSENCE the format requires:
//
//   - a null payload in an optional position (`Static.value`, `State.` /
//     `Filter.` / `Selection.defaultValue`) — absence is structural and `null`
//     appears nowhere in a canonical emission (WIRE_FORMAT §2, Phase 677);
//   - an EMPTY `Query.dependsOn` / `Transform.params` / `Expr.params` list —
//     each is "omitted when empty" (§3.3).
//
// Identity on every binding a decoder produces, so decoded ops pay nothing.

let private present (v: 'T option) : 'T option =
    match v with
    | Some p when isNull (box p) -> None
    | other -> other

let private nonEmpty (xs: 'a list option) : 'a list option =
    match xs with
    | Some [] -> None
    | other -> other

let rec private canonicalBinding<'T> (b: Binding<'T>) : Binding<'T> =
    let parameters (ps: TransformParam list option) : TransformParam list option =
        nonEmpty ps
        |> Option.map (
            List.map (fun p ->
                { p with
                    From = canonicalBinding<JVal> p.From })
        )

    match b with
    | Binding.Static v -> Binding.Static(present v)
    | Binding.Query(name, accessor, dependsOn) -> Binding.Query(name, accessor, nonEmpty dependsOn)
    | Binding.Filter(name, defaultValue) -> Binding.Filter(name, present defaultValue)
    | Binding.Selection(nodeId, accessor, defaultValue, field) ->
        Binding.Selection(nodeId, accessor, present defaultValue, field)
    | Binding.State(key, defaultValue) -> Binding.State(key, present defaultValue)
    | Binding.Local(flushOn, format, initialFrom, onCommit, parse, codec, commitTo) ->
        Binding.Local(flushOn, format, canonicalBinding<'T> initialFrom, onCommit, parse, codec, commitTo)
    | Binding.Format(source, format, locale) -> Binding.Format(canonicalBinding<float> source, format, locale)
    | Binding.I18n(key, args) ->
        Binding.I18n(key, args |> Option.map (Map.map (fun _ arg -> canonicalBinding<JVal> arg)))
    | Binding.Transform(source, pipeline, ps) ->
        let source =
            match source with
            | TransformSource.Live(live, initial) -> TransformSource.Live(canonicalBinding<JVal> live, initial)
            | data -> data

        Binding.Transform(source, pipeline, parameters ps)
    | Binding.Expr(expr, ps) -> Binding.Expr(expr, parameters ps)
    | Binding.Now _
    | Binding.Computed _
    | Binding.Invoke _ -> b

let private bindingJson (b: Binding<obj>) : JVal =
    Fuaran.UI.Generated.encodeBindingJson payloadJson (canonicalBinding b)

// ─── Node / kind / envelope-record encoding — the GENERATED encoder ────────
//
// Phase 694: `Fuaran.UI.Generated` owns node / kind / state / style encoding
// (proven byte-identical over the wire corpus).
//
// The §16 canonical-form projection (`Introspect.canonicalForm`) runs ABOVE
// the structural encoder — the policy half (an explicit auto-binding value, an
// all-default style, an all-None state each re-encode as the canonical
// ABSENCE). Identity on already-canonical trees, so decoded trees pay nothing.
//
// THE INVARIANT: every payload which can CONTAIN a `Node` routes through the
// projection. Three do — `nodeJson`, `nodeKindJson`, `stateBehaviourJson` — and
// the third only since the cross-host fuzz exchange caught it bypassing them
// (see its own note below). The rest carry no `Node` and so have nothing to
// project: `SemanticStyle` is bare enum fields (its omit-when-default is
// structural, owned by the generated encoder); `PropValue` is a scalar /
// `"<opaque>"` `Native` or an already-canonical `Wire` JVal; `Binding` resolves
// to values. Adding a Node-bearing payload to any of them means adding the
// projection in the same change.

let private nodeJson<'Msg> (n: Node<'Msg>) : JVal =
    Fuaran.UI.Generated.encodeNodeJson (Fuaran.UI.Ops.Introspect.canonicalForm n)

let private nodeKindJson<'Msg> (k: NodeKind<'Msg>) : JVal =
    Fuaran.UI.Generated.encodeNodeKindJson (Fuaran.UI.Ops.Introspect.canonicalFormKind k)

/// `StateBehaviour` carries `onLoading` / `onEmpty` **Node** payloads, so it
/// needs the §16 projection exactly as the two encoders above do — it once
/// spliced the raw record straight into the structural encoder, and a
/// `TreeOp.UpdateState` whose state node was (say) a `Filters` strip with an
/// explicit self-referential auto binding therefore emitted PRE-canonical
/// bytes on the one op path that skipped the projection. Found by the
/// cross-host fuzz exchange; pinned deterministically in
/// `Fuaran.UI.OpStream.Tests/CanonicalFormOpTests.fs`.
///
/// The projection maps over the two Node slots rather than routing a scratch
/// envelope through `canonicalForm` (the `canonicalFormKind` shape), because
/// `canonicalForm` collapses an all-`None` state to absence — correct for a
/// node's own `State` field, wrong for an op that explicitly SETS one, where
/// there is no absence to collapse to. `OnError` is `ErrorPayload -> Node`:
/// no node to project until it is applied. Nested state inside the spliced
/// nodes is reached by `canonicalForm`'s own recursion.
let private stateBehaviourJson<'Msg> (s: StateBehaviour<'Msg>) : JVal =
    Fuaran.UI.Generated.encodeStateBehaviourJson
        { s with
            OnLoading = s.OnLoading |> Option.map Fuaran.UI.Ops.Introspect.canonicalForm
            OnEmpty = s.OnEmpty |> Option.map Fuaran.UI.Ops.Introspect.canonicalForm }

/// Phase 2291 — the `PatchStyle` payload: exactly the members the delta sets,
/// each a bare token string. Unlike the `SemanticStyle` encoder a default token
/// is NOT omitted: in a delta it is the edit.
let private styleDeltaJson (d: StyleDelta) : JVal =
    JObj(StyleDelta.wireMembers d |> List.map (fun (k, v) -> k, JStr v))

// ─── TreeOp encoder ───────────────────────────────────────────────────────

let private nodeIdJson (NodeId raw) : JVal = JStr raw

let rec private treeOpJson<'Msg> (op: TreeOp<'Msg>) : JVal =
    match op with
    | TreeOp.EditNode(target, newKind) ->
        Canonical.typed "EditNode" [ "newKind", nodeKindJson newKind; "target", nodeIdJson target ]
    | TreeOp.UpdateProp(target, path, value) ->
        Canonical.typed "UpdateProp" [ "path", JStr path; "target", nodeIdJson target; "value", propValueJson value ]
    | TreeOp.ReplaceBinding(target, slot, binding) ->
        Canonical.typed
            "ReplaceBinding"
            [ "binding", bindingJson binding
              "slot", JStr slot
              "target", nodeIdJson target ]
    | TreeOp.UpdateStyle(target, style) ->
        Canonical.typed
            "UpdateStyle"
            [ "style", Fuaran.UI.Generated.encodeSemanticStyleJson style
              "target", nodeIdJson target ]
    | TreeOp.PatchStyle(target, delta) ->
        Canonical.typed "PatchStyle" [ "style", styleDeltaJson delta; "target", nodeIdJson target ]
    | TreeOp.UpdateState(target, state) ->
        Canonical.typed "UpdateState" [ "state", stateBehaviourJson state; "target", nodeIdJson target ]
    | TreeOp.UpdateAccessibility(target, accessibility) ->
        Canonical.typed
            "UpdateAccessibility"
            [ "accessibility", Fuaran.UI.Generated.encodeAccessibilityJson accessibility
              "target", nodeIdJson target ]
    | TreeOp.UpdateTooltip(target, tooltip) ->
        Canonical.typed
            "UpdateTooltip"
            [ "target", nodeIdJson target
              match tooltip with
              | Some text -> "tooltip", Fuaran.UI.Generated.encodeTextSourceJson text
              | None -> () ]
    | TreeOp.InsertChild(parentId, child) ->
        Canonical.typed "InsertChild" [ "child", nodeJson child; "parentId", nodeIdJson parentId ]
    | TreeOp.RemoveNode target -> Canonical.typed "RemoveNode" [ "target", nodeIdJson target ]
    | TreeOp.MoveNode(target, newParentId) ->
        Canonical.typed "MoveNode" [ "newParentId", nodeIdJson newParentId; "target", nodeIdJson target ]
    | TreeOp.ReorderChildren(parentId, newOrder) ->
        Canonical.typed
            "ReorderChildren"
            [ "newOrder", JArr(List.map nodeIdJson newOrder)
              "parentId", nodeIdJson parentId ]
    | TreeOp.ReplaceRoot node -> Canonical.typed "ReplaceRoot" [ "node", nodeJson node ]
    | TreeOp.Batch ops -> Canonical.typed "Batch" [ "ops", JArr(List.map treeOpJson ops) ]

let private render (v: JVal) : string = Canonical.write Rendering.CrossHost v

// ─── Public surface ───────────────────────────────────────────────────────

/// Encode a `Node<'Msg>` to its canonical JSON string. Fable-compatible —
/// no reflection, no `System.Security.*`. Closure-bearing payloads render
/// as the `"<closure>"` sentinel (v1 limitation, see migration doc).
///
/// Pairs with `ArgsJsonContract.validate` as the AI pre-emit self-check —
/// `encodeNode tree |> ArgsJsonContract.validate` catches wire-shape
/// violations cheaper than the apply-engine envelope.
///
/// **Which encoder a tool that emits wire JSON uses (Phase 1921).** THIS one:
/// the bytes an AI tool, a CLI verb or any other emitter hands out as a node's
/// wire JSON are `encodeNode`'s, the encoding the wire-format corpus certifies
/// and every other host's parity tables are cut against. `Generated.encodeNode`
/// is the STRUCTURAL encoder: it spells whatever tree it is given, so over a
/// tree the §16 projection would rewrite (an explicit auto-binding value, an
/// all-default style, an all-`None` state) it emits bytes no other host
/// produces. An emitter below this layer, which cannot reach the projection
/// (`SchemaForm.deriveWire` in `Fuaran.UI`), may use the structural encoder only
/// over trees it constructs canonically, and pins that with a test comparing
/// its bytes with this function's — `SchemaFormParityTests` is the instance.
let encodeNode<'Msg> (node: Node<'Msg>) : string = render (nodeJson node)

/// One place a tree would lose behaviour on the way to the wire (Phase 577):
/// the node carrying the closure, and which slot holds it.
///
/// `Slot` is spelled with `SlotCapability`'s `Type.slot` names
/// (`"Action.Dispatch.msg"`, `"Action.Call.onResult"`,
/// `"Action.ReadFileBody.onRead"`), so a report from here names the same slot
/// the survivability table, the validator's FUARAN112 and the §4 placeholder
/// enumeration name.
type LossyPath = { NodeId: string; Slot: string }

/// Encode a `Node<'Msg>` for TRANSPORT — the same canonical bytes as
/// `encodeNode`, but refusing a tree whose interaction would not survive the
/// round trip.
///
/// **Why this is a second function rather than a flag on the first.**
/// `encodeNode` feeds the hash chain, and its closure-blindness there is
/// DELIBERATE: two ops differing only in an opaque `'Msg` payload hash
/// identically, by design, because the chain records the shape of a change and
/// not the identity of the host code that requested it. Making that path refuse
/// would break the property it exists to have. What was missing was a path
/// where the author's INTENT is known — and calling a function named for
/// transport is that intent, stated at the one moment it can be.
///
/// Refuses on the `Action` DU's three closure slots — `Dispatch`'s `msg`,
/// `Call`'s `onResult`, `ReadFileBody`'s `onRead` — each of which decodes to an
/// inert placeholder, so the affordance arrives able to fire and unable to do
/// anything. Every offending path is returned, not the first: an author repairs
/// a tree in one pass or discovers its defects one turn at a time.
///
/// **The loss leaves no trace in the bytes**, which is why it has to be caught
/// here. `encodeNode` emits the discriminator and DROPS the payload — a
/// `Dispatch` becomes `{"$type":"Dispatch"}` — and `"<closure>"` is the
/// DECODER's reconstruction. A downstream reader of the emission cannot tell a
/// `Dispatch` that lost a message from one that never carried a payload, so the
/// encoding side is the last place the question is answerable at all.
///
/// **What it does NOT claim.** Other slots erase too (a `FormFieldKind.onChange`,
/// a `TabsSpec.onSelect`) and are not refused, because the renderers'
/// write-back default reconstructs their behaviour from the control's own
/// writable binding — the closure is lost and the interaction is not.
/// `Binding.Computed` is likewise untouched here; it is FUARAN084's subject.
/// A tree this function accepts carries no *unrecoverable* interaction, which
/// is a narrower claim than "nothing about it erased".
///
/// The wire-representable way to reach a host is `Action.Notify` or
/// `Action.Call` with `into:`; typed dispatch is obtained by binding handlers
/// to the artifact's declared action holes, host-side.
let encodeNodeForTransport<'Msg> (node: Node<'Msg>) : Result<string, LossyPath list> =
    let facts = Fuaran.UI.BindingWalk.collect node

    // Deduplicated on (node, slot) so a `Chain` of two `Dispatch`es is one
    // repair rather than two reports of it. Ordered by the walk, which is
    // depth-first pre-order, so the paths read in tree order.
    let seen = System.Collections.Generic.HashSet<string>()

    let lossy =
        facts.Closures
        |> List.filter (fun c -> seen.Add(c.Reader + " " + c.Slot))
        |> List.map (fun c -> { NodeId = c.Reader; Slot = c.Slot })

    match lossy with
    | [] -> Ok(encodeNode node)
    | paths -> Error paths

/// Encode a `TreeOp<'Msg>` to its canonical JSON string. Server-side hash
/// chain calls this to feed SHA-256; the encoded form is also useful for
/// op-stream sinks that persist as JSON text (Sqlite `op_json` column).
let encodeOp<'Msg> (op: TreeOp<'Msg>) : string = render (treeOpJson op)

/// Encode an `OpResultEnvelope` to its canonical JSON string. Used by the
/// Sqlite sink for the `result_envelope_json` column.
let encodeResultEnvelopeShape (status: string) (errorCode: string option) (errorMessage: string option) : string =
    render (
        JObj(
            ("status", JStr status)
            :: ([ errorCode |> Option.map (fun c -> "errorCode", JStr c)
                  errorMessage |> Option.map (fun m -> "errorMessage", JStr m) ]
                |> List.choose id)
        )
    )
