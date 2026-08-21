module Fuaran.UI.Ops.Types

// ============================================================================
//  Fuaran tree-op apply engine — type contract (§4g of the Fuaran design specification).
//
//  Ships the typed in-memory apply
//  surface. The op vocabulary is canonical per §4g; the error
//  shape is canonical per §4d.
//
//  Two encoding decisions worth recording here:
//
//   (1) `TreeOp` is parameterised by `'Msg`. §4g's literal DU lists
//       unparameterised `NodeKind` / `Node<obj>` / `Binding<obj>` /
//       `StateBehaviour<obj>` — that's the *storage* shape (JSONL on disk;
//       wire form to / from the AI consumer). The *apply* shape this
//       engine ships is the typed in-memory one — a downstream
//       AI consumer hands the engine a typed `Node<'Msg>`
//       and gets back a typed `Node<'Msg>` or a structured error, with
//       no decoder round-trip in the hot path. The storage layer
//       (`Node<obj>` + `moduleMsgDecoder: JVal -> 'Msg`) lands with
//       the per-turn JSONL persistence substrate — explicitly out of v1
//       scope and called out in `README.md`.
//
//   (2) `ReplaceBinding` carries `Binding<obj>` even in the typed op DU
//       (matches §4g's slot-erased signature). Each slot has its own
//       expected `'T` (Metric.Source: Binding<float>; Grid.Source:
//       Binding<obj seq>; Stepper.ActiveStep: Binding<int>; etc.). The
//       apply engine casts per-slot via Binding-aware coercion; the op
//       caller's job is to ship a Binding whose underlying values match
//       the slot's expected type. Mis-typed bindings surface at apply
//       time as `KindMismatch` rather than at compile time, intentional
//       since the AI emits these via the wire path.
//
//   (3) `UpdateProp`'s payload is the two-case `PropValue` — the two honest
//       populations of a property update, typed. `Wire` carries the canonical
//       `JVal` form: it is what the wire decoder produces, it applies via the
//       per-field spec coercers, and it persists / content-hashes faithfully
//       (byte-identical across hosts). `Native` carries an in-process F# value
//       of the target field's exact type: it applies without loss (closures
//       survive — e.g. a `Binding.Computed` reached via a nested path), but it
//       is NOT wire-representable. Its persistence shape, precisely:
//         - a scalar (string / bool / int / int64 / float / date) encodes
//           natively and replays — with one latent shape-shift: a DateTime /
//           DateTimeOffset encodes as Unix-seconds, which replays as a plain
//           number (a boxed float) and no Coerce path reconstructs a date, so
//           a future date-typed UpdateProp field would silently change type
//           across the log boundary (no current field is date-typed; ship a
//           date coercer before one is);
//         - any other non-null value collapses to the "<opaque>" sentinel,
//           which the op decoder REJECTS by name at replay (the sentinel is
//           reserved vocabulary — never silently re-ingested as data);
//         - a boxed `None` is a CLR null and would encode as JSON null, which
//           replay also rejects — which is why `TreeOpDiff` never emits a
//           Native `None` (a `Some → None` transition takes the EditNode
//           floor instead: an option cannot be UNSET via UpdateProp).
//       So a non-scalar Native op is a loud replay error, never corruption.
//       Producers that feed the op-stream (the wire decoder, TreeOpDiff) emit
//       `Wire`; `Native` is for in-process programmatic edits and tests.
// ============================================================================

open Fuaran.Core
open Fuaran.UI.Types

/// The value payload of `TreeOp.UpdateProp` — see encoding decision (3) above.
[<RequireQualifiedAccess>]
type PropValue =
    /// In-process exact: a native F# value of the target field's type
    /// (boxed `TextSource`, `ToneVariant`, `Binding<bool>`, …). Applies
    /// without loss; persists best-effort — scalars natively, everything
    /// else as the "<opaque>" sentinel the op decoder REJECTS at replay
    /// (loud error, never re-ingested as data). Never box a `None` here
    /// (a CLR null → JSON null → replay rejection); see decision (3).
    /// Not produced by the wire decoder.
    | Native of obj
    /// Canonical wire form: applies via the per-field spec coercers,
    /// persists and content-hashes faithfully, cross-host identical.
    | Wire of JVal

/// Structural lowering of a `JVal` payload to the legacy decoded-`obj` shapes
/// (every number boxed as `float`, objects as `Map<string, obj>`, arrays as
/// `obj list`) — the exact representation the wire decoder's obj path
/// historically produced, which the apply engine's per-field coercers and the
/// server-driven bounded state store consume.
[<RequireQualifiedAccess>]
module JValObj =
    // `box` of these payloads is never null (every JVal case carries a
    // non-null value), so the F# 10 `obj | null` widening is collapsed here.
    let rec toObj (j: JVal) : obj =
        match j with
        | JStr s -> Unchecked.nonNull (box s)
        | JInt i -> Unchecked.nonNull (box (float i))
        | JBool b -> Unchecked.nonNull (box b)
        | JFloat f -> Unchecked.nonNull (box f)
        | JArr xs -> Unchecked.nonNull (box (xs |> List.map toObj))
        | JObj fields -> Unchecked.nonNull (box (fields |> List.map (fun (k, v) -> k, toObj v) |> Map.ofList))

[<RequireQualifiedAccess>]
module PropValue =
    /// Lower either population to the coercer-facing `obj` (`Native` passes
    /// its value through untouched; `Wire` lowers structurally).
    let toObj (v: PropValue) : obj =
        match v with
        | PropValue.Native o -> o
        | PropValue.Wire j -> JValObj.toObj j

[<RequireQualifiedAccess>]
type TreeOp<'Msg> =
    /// Wholesale kind swap of the addressed node. Every other Node field is
    /// preserved — the apply is `{ n with Kind = newKind }`, so Id / State /
    /// Style / Accessibility / Motion / ExtraAttributes all carry over;
    /// only `Kind` (and thus the spec record) is replaced. Use when the AI
    /// has decided a Form becomes a Filters bank, a Metric becomes a
    /// Sparkline, etc.
    | EditNode of NodeId * NodeKind<'Msg>

    /// Granular property update against the addressed node's spec record.
    /// `path` is dot-separated with optional 0-based list indices per the
    /// WIRE_FORMAT.md §3.4 grammar (Phase 364): a single segment addresses a
    /// top-level field (`"Label"`); nested segments traverse the per-kind
    /// typed surface (`"Columns[0].Label"`, `"YFields[1]"`,
    /// `"TabHeaders[1].Disabled"`, `"Fields[0].Required"`). Paths outside a
    /// kind's typed-traversal surface surface `PathNotSupportedYet` with the
    /// supported paths in the hint so an AI consumer recovers via structural
    /// ops instead.
    | UpdateProp of NodeId * path: string * value: PropValue

    /// Replace a single Binding-typed slot on the addressed node. `slot`
    /// names the field (`"Source"`, `"Trend"`, `"Fraction"`, etc.); the
    /// `Binding<obj>` is cast per-slot to the expected typed `Binding<'T>`
    /// by the apply engine.
    | ReplaceBinding of NodeId * slot: string * binding: Binding<obj>

    /// Replace the addressed node's `Style` block.
    | UpdateStyle of NodeId * SemanticStyle

    /// Replace the addressed node's `State` block (OnLoading / OnEmpty /
    /// OnError slots).
    | UpdateState of NodeId * StateBehaviour<'Msg>

    /// Append a child to the addressed parent's `Children`. The op carries
    /// no ordinal — the 681–687 wave removed `position` from every
    /// structural op — so insertion is always an append; ordering is
    /// `ReorderChildren`'s job, and expressing "insert at index n" means
    /// appending and then reordering. Parents without a `Children` field
    /// (every Display / Input / Visualisation kind) surface `ChildlessKind`.
    | InsertChild of parentId: NodeId * child: Node<'Msg>

    /// Remove the addressed node from its parent's `Children`. Cannot
    /// remove the root.
    | RemoveNode of NodeId

    /// Move the addressed node, appending it to the new parent's
    /// `Children`. Positionless like `InsertChild` above: there is no
    /// `newPosition` field, so the node lands last and a required order is
    /// expressed with a following `ReorderChildren`. Source and target
    /// parents must both exist and both have a `Children` field. Moving a
    /// node inside its own subtree surfaces `KindMismatch` (would create a
    /// cycle).
    | MoveNode of NodeId * newParentId: NodeId

    /// Reorder the addressed parent's children. `newOrder` must list
    /// exactly the current child IDs as a permutation; missing / extra /
    /// unknown IDs surface `OrderingMismatch` with the expected ID list.
    | ReorderChildren of parentId: NodeId * newOrder: NodeId list

    /// Replace the entire tree with `node` — the only op that legally changes
    /// the root node. Use for a whole-app swap (a cutscene, a scene change,
    /// restoring a snapshot) where a `Batch` of remove/insert would be O(tree)
    /// of noise and lose the intent.
    | ReplaceRoot of Node<'Msg>

    /// All-or-nothing apply of an inner op list. The first failing inner
    /// op aborts the batch; the tree returned in `Error` is the
    /// pre-batch state, and the error carries the inner-op index in the
    /// `BatchAborted` code.
    | Batch of TreeOp<'Msg> list

// ─── Error shape (§4d lines 737–761) ───────────────────────────────────────

/// Short stable identifier for an apply-time failure. The AI's closed-loop
/// orchestrator pattern-matches on this to pick a recovery strategy.
[<RequireQualifiedAccess>]
type ApplyErrorCode =
    /// The addressed node could not be found anywhere in the tree.
    | NodeNotFound
    /// The addressed parent could not be found (InsertChild / MoveNode /
    /// ReorderChildren).
    | ParentNotFound
    /// The addressed field does not exist on the target node's spec
    /// record. `Hint.AvailableFields` enumerates the supported names.
    | FieldNotFound
    /// The addressed Binding slot does not exist on the target node's
    /// spec record. `Hint.AvailableFields` enumerates the supported
    /// slot names.
    | SlotNotFound
    /// The op targets a node whose `NodeKind` is incompatible with the
    /// op (e.g. MoveNode that would create a cycle; ReplaceBinding's
    /// payload type mismatches the slot's expected `'T`).
    | KindMismatch
    /// The addressed kind has no `Children` field (every Display / Input
    /// / Visualisation node is childless). InsertChild / RemoveNode (of
    /// a leaf parent) / MoveNode / ReorderChildren cannot target it.
    | ChildlessKind
    /// A nested DATA path's list index is outside its list
    /// (`0 ≤ i < list.Length` — e.g. `Columns[5].Label` on a 3-column grid);
    /// `Hint.Suggestion` carries the valid range. NOT a structural-op
    /// position: the 681–687 wave removed `position` from `InsertChild` and
    /// `MoveNode`, so a structural insert has no ordinal to be wrong about.
    /// Contained collections whose items have no identity keep their
    /// ordinals, which is why this case remains live and normative
    /// (WIRE_FORMAT.md §3.4).
    | PositionOutOfRange
    /// `newOrder` is not a permutation of the parent's current child IDs
    /// (missing / extra / unknown IDs).
    | OrderingMismatch
    /// InsertChild would introduce a NodeId already present elsewhere in
    /// the tree, breaking the §4g uniqueness contract.
    | DuplicateNodeId
    /// `path` violates the WIRE_FORMAT.md §3.4 grammar (empty, malformed
    /// dot-segment or `[i]` index, a list segment addressed without an
    /// index, the reserved `#` id-keyed form). `Hint.AvailableFields`
    /// enumerates the target kind's addressable paths when the node
    /// resolved.
    | PathInvalid
    /// The path is grammatical but the target kind/field has no typed
    /// traversal in this engine version (nested addressing covers the §3.4
    /// surface; closure-bearing sub-fields never traverse).
    /// `Hint.AvailableFields` enumerates what the engine *does* support for
    /// the target kind — top-level fields + nested patterns.
    | PathNotSupportedYet
    /// An inner op of a Batch failed; the batch was reverted to its
    /// pre-batch state. Carries the 0-based index of the failing inner op.
    | BatchAborted of innerIndex: int

/// AI-recovery hint payload per §4d lines 745–759. Every field is
/// optional so a renderer can emit only what's populated for a given
/// error class.
type ApplyHint =
    {
        /// Short kind name of the target node ("Metric", "Grid", "Dashboard",
        /// ...). Populated when the engine could resolve the target node.
        NodeKind: string option
        /// Supported field / slot names on the target kind. Populated for
        /// FieldNotFound / SlotNotFound / PathNotSupportedYet.
        AvailableFields: string list
        /// `(fieldName, [nodeIds])` — nodes elsewhere in the tree whose
        /// kind has the field the failing op tried to address. Populated
        /// for FieldNotFound when the field exists on *some* other node,
        /// to let the AI pivot to a different target.
        NodesWithField: (string * NodeId list) option
        /// Free-text human / AI-readable next-step suggestion. Populated
        /// when the engine can name a likely fix.
        Suggestion: string option
    }

/// Top-level apply-time error. `Hint` is `ApplyHint.Empty` for failure
/// classes where no recovery hint exists (e.g. a malformed `PathInvalid`
/// against an unresolved node).
type ApplyError =
    { Code: ApplyErrorCode
      Message: string
      Hint: ApplyHint }

module ApplyHint =
    let empty: ApplyHint =
        { NodeKind = None
          AvailableFields = []
          NodesWithField = None
          Suggestion = None }

// ─── WireTree — a decoded tree's inert-closure marker ─────────────────────
//
// The load-bearing distinction the type system was missing: a tree that came
// FROM the wire (`JsonDecode.decodeNode`) versus one AUTHORED in-process.

/// A UI tree decoded from the canonical wire (`JsonDecode.decodeNode`). Its
/// interactive closures did **not** survive serialisation — every handler slot
/// is an inert `"<closure>"` sentinel (a `Button.OnClick`, `Tabs.OnSelect`, …
/// decodes to a no-op). A `WireTree` is therefore safe to persist, diff, apply
/// ops to, and **drive through a bounded program loop** (`Fuaran.Program.Bounded`
/// takes it directly),
/// where interactivity is re-derived from the store — never by invoking a
/// closure. But rendering it through the **live client renderer** would produce
/// dead interactivity (its buttons dispatch nothing), because the closures are
/// gone; the renderer accepts only an authored `Node<'Msg>`, so that mistake no
/// longer type-checks. To render a wire tree live anyway you must first
/// `WireTree.reify` it (an explicit, deliberately-named acknowledgement that
/// its closures are inert) and then re-attach handlers or accept dead controls.
[<Struct>]
type WireTree = private WireTree of Node<obj>

[<RequireQualifiedAccess>]
module WireTree =
    /// Wrap a freshly-decoded `Node<obj>` as a `WireTree`. Called by the wire
    /// decoder; wrapping is the *safe* direction (it can only make a tree's
    /// closures be treated as MORE inert than they are).
    let ofDecoded (node: Node<obj>) : WireTree = WireTree node

    /// The raw `Node<obj>` behind the marker. **The name is the warning:** the
    /// tree's closures are inert `"<closure>"` sentinels, so anything that
    /// invokes a handler (the live client renderer's `runAction`) will observe
    /// dead interactivity. Reify only to persist / re-encode / re-attach
    /// handlers, never to hand a wire tree to a live dispatch loop expecting
    /// authored closures.
    let reify (WireTree node) : Node<obj> = node
