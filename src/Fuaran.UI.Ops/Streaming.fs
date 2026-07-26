module Fuaran.UI.Ops.Streaming

// ============================================================================
//  Streaming / progressive partial-tree decode + apply (Phase 104).
//
//  LLMs stream tokens, but the established path decodes + renders only once a
//  complete wire-JSON tree has arrived. This module adds the substrate for
//  *progressive* emission — establish a skeleton (the root shell), then fill
//  subtrees as their wire JSON completes — while preserving the op-stream
//  replay invariant (FGP 5): a streamed emission must replay byte-identically
//  to the same emission applied as one batch.
//
//  The load-bearing design decision that makes FGP 5 hold *by construction*:
//
//    Streaming changes WHEN the UI repaints, not WHAT ops are emitted.
//
//  Both the batch path and the streaming path lower the decoded tree to the
//  SAME ordered `TreeOp.InsertChild` sequence via `lowerTree`. The streaming
//  consumer reveals those ops as each subtree's wire frame arrives (rendering
//  between them); the batch consumer reveals them all at once. Identical ops in
//  identical order ⇒ identical `OpRecord` hash chain (same sequence + timestamp
//  per record) ⇒ byte-identical op-stream + final tree. The
//  `Fuaran.UI.JsonDecode.Tests` `StreamingProperties` suite proves this over
//  the corpus + generated trees.
//
//  This module is PURELY ADDITIVE — it introduces new functions and touches no
//  existing decoder / apply code, so the established batch decode + apply path
//  is provably unchanged (Phase 104 Task 5 "no regression"). The streaming wire
//  frame (`StreamFrame`) is a transport envelope, NOT a wire-format addition:
//  its `NodeJson` payload is canonical `Node` wire, so no `WIRE_FORMAT.md` /
//  corpus / cross-host forward-coupling is incurred.
//
//  Lowering builds on `Introspect.getChildren` / `withChildren` — the SAME
//  shape every structural op traverses — so `lowerTree`'s `InsertChild` ops are
//  exactly what `Apply.apply` consumes, closing the loop within one consistent
//  introspection surface.
// ============================================================================

open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types

// ─── Streaming capability flag (Task 5) ─────────────────────────────────────
//
// A host / provider selects the decode path. `Batch` is the default and leaves
// the established whole-tree decode path untouched; `Progressive` consumes the
// frame stream. Non-streaming providers + hosts stay on `Batch`, so there is no
// behaviour change for them — the flag IS the no-regression guarantee.
[<RequireQualifiedAccess>]
type StreamingMode =
    | Batch
    | Progressive

/// The batch decode path — identical to the established whole-tree decode.
/// Surfaced here so the streaming module is the single seam a host consults for
/// either mode; `decodeBatch` is literally `JsonDecode.decodeNode`, so a host
/// that picks `StreamingMode.Batch` gets the same wire-marked `WireTree` a
/// direct `decodeNode` yields.
let decodeBatch (wire: string) : Result<WireTree, JsonDecode.DecodeError> = JsonDecode.decodeNode wire

// ─── Shell of a node ────────────────────────────────────────────────────────
//
// A node is "decomposable" (streamable into) iff its kind accepts an
// empty-children shell — i.e. `Introspect.withChildren kind []` succeeds. That
// is exactly the layout kinds. Every other kind (Display / Input /
// Visualisation leaves, plus `Custom` / `ErrorBoundary` / `FragmentDecl`, whose
// interior subtrees are not structurally addressable as positional children) is
// a streaming leaf: inserted whole, never filled further. This rule keeps
// lowering and the apply engine in lock-step — `Apply.InsertChild` rejects a
// childless parent with `ChildlessKind`, and `lowerTree` never targets one.

/// True when `node`'s kind accepts an empty-children shell (the layout kinds).
let isDecomposable (node: Node<'Msg>) : bool =
    Introspect.withChildren node.Kind [] |> Option.isSome

/// The shell of `node`: a decomposable node with its children emptied; any
/// other node unchanged (inserted whole as a streaming leaf).
let shellOf (node: Node<'Msg>) : Node<'Msg> =
    match Introspect.withChildren node.Kind [] with
    | Some shellKind -> { node with Kind = shellKind }
    | None -> node

/// The children to fill for a node, or `None` when the node is a streaming leaf
/// (childless kind, kept-whole kind, or a decomposable kind that is already
/// empty — nothing to stream into).
let private fillableChildren (node: Node<'Msg>) : Node<'Msg> list option =
    match Introspect.getChildren node.Kind with
    | Some children when isDecomposable node && not (List.isEmpty children) -> Some children
    | _ -> None

// ─── Canonical lowering (Task 1 contract + the shared batch/stream plan) ─────

/// The skeleton root for a tree: the root with its children emptied (a
/// decomposable root), or the whole root unchanged (a leaf / kept-whole root).
/// This is the genesis tree both the batch and streaming op-streams build from.
let skeletonRoot (root: Node<'Msg>) : Node<'Msg> = shellOf root

/// Lower a fully-decoded tree into the canonical, deterministic
/// `TreeOp.InsertChild` sequence that reconstructs it from its own
/// `skeletonRoot`. Pre-order: each child is inserted at its position as its OWN
/// shell, then recursively filled. Applying the result to `skeletonRoot root`
/// yields `root` exactly.
///
/// This single lowering is the shared plan: the batch path emits these ops at
/// once; the streaming path emits the same ops as each subtree frame arrives.
/// Their op-streams are therefore byte-identical (FGP 5).
let lowerTree (root: Node<'Msg>) : TreeOp<'Msg> list =
    let rec fill (node: Node<'Msg>) : TreeOp<'Msg> list =
        match fillableChildren node with
        | None -> []
        | Some children ->
            children
            |> List.map (fun child -> TreeOp.InsertChild(node.Id, shellOf child) :: fill child)
            |> List.concat

    fill root

// ─── Wire frame protocol (Task 1) ───────────────────────────────────────────
//
// The streaming contract: a streamed emission is an ordered sequence of frames,
// each carrying the canonical wire of ONE shell node (whole-subtree boundaries,
// never arbitrary byte splits — every frame's payload is a valid `Node`). The
// first frame (ParentId = None) is the skeleton root; each subsequent frame
// (ParentId = Some pid) is an `InsertChild(pid, Position, decode NodeJson)` op,
// delivered in `lowerTree` pre-order.

/// One streaming frame — the canonical wire of a single shell node plus its
/// insertion target. `ParentId = None` marks the skeleton-root frame (its
/// `Position` is unused); `ParentId = Some pid` is an `InsertChild` op.
type StreamFrame =
    { ParentId: NodeId option
      Position: int
      NodeJson: string }

/// Emitter side: lower `root` to the ordered frame sequence a streaming
/// transport ships. `encodeNode` is injected (the canonical encoder lives in
/// `Fuaran.UI.OpStream.Abstractions`, above this package, so it is passed in
/// rather than referenced — keeping this module dependency-clean and
/// Fable-portable). Frame 0 is the skeleton root; frames 1..n mirror
/// `lowerTree root` one-for-one.
let framesOf (encodeNode: Node<'Msg> -> string) (root: Node<'Msg>) : StreamFrame list =
    let skeletonFrame =
        { ParentId = None
          Position = 0
          NodeJson = encodeNode (skeletonRoot root) }

    let childFrames =
        lowerTree root
        |> List.map (fun op ->
            match op with
            | TreeOp.InsertChild(parentId, child) ->
                { ParentId = Some parentId
                  // The FRAME protocol keeps its ordinal: frames stream in order and a
                  // progressive client may place them as they arrive. It is a separate
                  // wire from the TreeOp and is deliberately not changed here — only the
                  // op stops carrying one. Reconstruction below ignores it, because the
                  // op appends and the frames are already in document order.
                  Position = 0
                  NodeJson = encodeNode child }
            // `lowerTree` only ever produces `InsertChild`; this arm is
            // unreachable, kept total so a future lowering change is caught at
            // the call site rather than silently dropped.
            | other -> failwithf "Streaming.framesOf: lowerTree produced a non-InsertChild op: %A" other)

    skeletonFrame :: childFrames

// ─── Incremental decode (Task 2): frames → (skeleton, ops) ───────────────────

/// The result of decoding a streamed frame sequence: the genesis skeleton tree
/// plus the ordered `InsertChild` ops that fill it.
/// `applyFold Ops (WireTree.reify Skeleton)` rebuilds the full tree; the ops
/// feed the op-stream byte-identically to the batch path. The skeleton is a
/// `WireTree` — the SAME wire-origin marker `decodeBatch` returns — because the
/// incremental path is the one a progressive-render consumer is most likely to
/// hand straight to a renderer, exactly the "decode then render live" mistake
/// the marker exists to make un-writable (its closures are inert placeholders).
type StreamDecode =
    { Skeleton: WireTree
      Ops: TreeOp<obj> list }

let private streamError (code: string) (message: string) : JsonDecode.DecodeError =
    { Code = code
      Path = "$"
      Message = message
      ExpectedShape = None }

/// Consumer side: decode an ordered frame sequence into the genesis skeleton +
/// the fill ops. Each frame's `NodeJson` is decoded with the established
/// `JsonDecode.decodeNode` (whole-subtree boundaries guarantee each is a valid
/// `Node`). The first frame must be the skeleton root (`ParentId = None`); every
/// later frame must carry a parent. Returns the structured `DecodeError` on a
/// malformed frame or a protocol violation.
let decodeStream (frames: StreamFrame list) : Result<StreamDecode, JsonDecode.DecodeError> =
    match frames with
    | [] -> Error(streamError "STREAM_EMPTY" "A streamed emission must carry at least the skeleton-root frame.")
    | skeletonFrame :: childFrames ->
        match skeletonFrame.ParentId with
        | Some _ ->
            Error(
                streamError "STREAM_NO_SKELETON" "The first streamed frame must be the skeleton root (ParentId = None)."
            )
        | None ->
            match JsonDecode.decodeNodeObj skeletonFrame.NodeJson with
            | Error e -> Error e
            | Ok skeleton ->
                let rec loop acc remaining =
                    match remaining with
                    | [] -> Ok(List.rev acc)
                    | (frame: StreamFrame) :: rest ->
                        match frame.ParentId with
                        | None ->
                            Error(
                                streamError
                                    "STREAM_ORPHAN_SKELETON"
                                    "Only the first streamed frame may omit a ParentId; a later frame did."
                            )
                        | Some parentId ->
                            match JsonDecode.decodeNodeObj frame.NodeJson with
                            | Error e -> Error e
                            | Ok child -> loop (TreeOp.InsertChild(parentId, child) :: acc) rest

                match loop [] childFrames with
                | Error e -> Error e
                | Ok ops ->
                    Ok
                        { Skeleton = WireTree.ofDecoded skeleton
                          Ops = ops }

/// Fold an ordered op list against an initial tree, stopping at the first
/// apply failure. The streaming + batch replay paths both fold their ops this
/// way; a `lowerTree`-derived sequence never fails (pre-order guarantees each
/// parent exists before its child is inserted), so the `Error` arm is the
/// honest surface for a corrupted stream.
let applyFold (ops: TreeOp<'Msg> list) (initial: Node<'Msg>) : Result<Node<'Msg>, ApplyError> =
    ops
    |> List.fold (fun acc op -> acc |> Result.bind (Apply.apply op)) (Ok initial)

// ─── Skeleton placeholders for pending subtrees (Task 4) ─────────────────────
//
// While a subtree is in flight, the renderer shows a skeleton placeholder in
// its slot. The placeholder reuses the canonical `DisplayKind.Skeleton` loading
// primitive under a render-time `ErrorBoundary` (Phase 60 graceful
// degradation), with the `Motion.PulseDuringLoad` hook (Phase 12.F) signalling
// "filling". Placeholders are a PURE RENDER concern: they never enter the op
// stream (which builds the real tree), so they cannot perturb the FGP 5 replay
// invariant.

/// A render-time skeleton placeholder for a not-yet-arrived subtree, carrying
/// the pending node's id so a `withSkeletons` pass can substitute it in/out.
let pendingPlaceholder<'Msg> (nodeId: NodeId) : Node<'Msg> =
    let (NodeId raw) = nodeId

    let pulsing =
        { Fuaran.skeleton (raw + "::loading") 2 with
            Motion = Some Motion.PulseDuringLoad }

    let fallback = Fuaran.skeleton (raw + "::loading-fallback") 1
    Fuaran.errorBoundary raw { Child = pulsing; Fallback = fallback }

/// Render-prep: substitute every node whose id is in `pending` with a
/// `pendingPlaceholder`, recursing through the structural children of the
/// rest. Hosts maintain the `pending` set as frames arrive and re-render the
/// substituted tree; once a subtree's frame lands, its id leaves the set and
/// the real node renders. This never touches the op stream.
let rec withSkeletons (pending: Set<NodeId>) (node: Node<'Msg>) : Node<'Msg> =
    if Set.contains node.Id pending then
        pendingPlaceholder node.Id
    else
        match Introspect.getChildren node.Kind with
        | Some children ->
            let mapped = children |> List.map (withSkeletons pending)

            match Introspect.withChildren node.Kind mapped with
            | Some newKind -> { node with Kind = newKind }
            | None -> node
        | None -> node
