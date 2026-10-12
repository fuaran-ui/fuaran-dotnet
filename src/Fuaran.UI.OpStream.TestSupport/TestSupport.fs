module Fuaran.UI.OpStream.TestSupport

open System
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.Merge

// ============================================================================
//  The ONE test-support module of the op-stream suites (Phase 2066).
//
//  The linear suite, the DAG suite and the DAG inspector suite each carried a
//  copy of what is below — the message type, the two-pane dashboard, the child
//  ids, the record builders, the host precedence classifier — and the first two
//  each carried a test-only op codec of their own: compact single-letter
//  formats covering four and three op shapes respectively, the DAG one lossy
//  (it kept three tones and dropped weight and emphasis). The sinks are now
//  exercised through the SHIPPED reference codec, `OpJsonCodec.canonical`, so
//  every SQLite test reads back what a host's stream would hold.
// ============================================================================

/// The smallest viable `'Msg`. Structural ops never construct a message, so a
/// richer DU would test nothing more.
type TestMsg =
    | NoOp
    | Selected of int

/// A whole-second timestamp. Whole seconds because the SQLite sinks store unix
/// seconds (the contract suite pins that difference by name); a fixture that
/// wants every store to read back the same record uses these.
let timestamp (unix: int64) : DateTimeOffset = DateTimeOffset.FromUnixTimeSeconds unix

let dashboardId = NodeId "dash"
let leftChildId = NodeId "left"
let rightChildId = NodeId "right"

/// A dashboard `id` holding `children` — the container the move and merge
/// families build their own shapes in.
let container (id: string) (children: Node<TestMsg> list) : Node<TestMsg> =
    Fuaran.dashboard
        id
        { Defaults.dashboard<TestMsg> with
            Children = children }

/// The genesis tree: a dashboard with two markdown panes.
let buildDashboard () : Node<TestMsg> =
    container "dash" [ Fuaran.markdown "left" "Left pane"; Fuaran.markdown "right" "Right pane" ]

let canonical (n: Node<TestMsg>) : string = CanonicalJson.encodeNode n

/// Fold an op over a tree, failing loudly on apply error (test ops are always
/// valid against their tree).
let applyOk (op: TreeOp<TestMsg>) (tree: Node<TestMsg>) : Node<TestMsg> =
    match Apply.apply op tree with
    | Ok t -> t
    | Error e -> failwithf "apply failed for %A: %A" op e

// ─── The op codec ────────────────────────────────────────────────────────────

/// The shipped reference codec at `TestMsg`. Encoding is `'Msg`-invariant; on
/// decode every erased payload (the `"<closure>"` sentinel) maps to `NoOp`.
let testCodec: IOpJsonCodec<TestMsg> = OpJsonCodec.canonical (fun _ -> Some NoOp)

/// The node codec the checkpoint-bearing SQLite sink needs to read a snapshot
/// back: the canonical encoder, and the tier's decoder retyped onto `TestMsg`.
let testNodeCodec: INodeJsonCodec<TestMsg> =
    { new INodeJsonCodec<TestMsg> with
        member _.EncodeNode node = CanonicalJson.encodeNode node

        member _.DecodeNode json =
            JsonDecode.decodeNodeObj json
            |> Result.map (NodeMap.mapMsg (fun _ -> NoOp))
            |> Result.mapError CodecError.Decode }

// ─── The linear stream ───────────────────────────────────────────────────────

/// The linear record at `sequence` linking to `previousHash`, its chain hash
/// computed (actor `Human "tester"`, no prompt id, a `Success` envelope).
let linearRecord
    (streamId: string)
    (sequence: int)
    (previousHash: string)
    (op: TreeOp<TestMsg>)
    (timestamp: DateTimeOffset)
    : OpRecord<TestMsg> =
    let actor = Actor.Human "tester"

    let hash =
        HashChain.computeHash previousHash op sequence timestamp actor None OpResultEnvelope.Success

    { StreamId = streamId
      Sequence = sequence
      PreviousHash = previousHash
      Hash = hash
      Op = op
      PromptId = None
      Actor = actor
      Timestamp = timestamp
      ResultEnvelope = OpResultEnvelope.Success }

/// `linearRecord` after `previous` — `None` is the genesis.
let buildRecord
    (streamId: string)
    (sequence: int)
    (op: TreeOp<TestMsg>)
    (previous: OpRecord<TestMsg> option)
    (timestamp: DateTimeOffset)
    : OpRecord<TestMsg> =
    let previousHash =
        match previous with
        | None -> HashChain.genesisPreviousHash
        | Some prev -> prev.Hash

    linearRecord streamId sequence previousHash op timestamp

// ─── The DAG stream ──────────────────────────────────────────────────────────

/// Append `op` as a child of `parent` (a single-parent linear step) and return
/// the content-addressed DAG record. `parent = None` is a genesis node.
let stepRecord
    (streamId: string)
    (parent: DagOpRecord<TestMsg> option)
    (op: TreeOp<TestMsg>)
    (unix: int64)
    : DagOpRecord<TestMsg> =
    let parents =
        match parent with
        | None -> []
        | Some p -> [ p.Hash ]

    DagOpRecord.create streamId parents op None (Actor.Human "tester") (timestamp unix) OpResultEnvelope.Success

/// Add a record to the sink synchronously (the suites are single-threaded).
let add (sink: IDagOpStreamSink<TestMsg>) (r: DagOpRecord<TestMsg>) : unit = sink.Add r |> Async.RunSynchronously

/// The host-side precedence classifier the suites wire into `DagMerge` /
/// `DagOverlay`: a record with no `PromptId` is `Primary` (the human pin in this
/// host's chosen semantics), otherwise it is `Secondary`, carrying its prompt id
/// as the opaque provenance tag. The merge layer does not bake this
/// interpretation in (Phase 331); the host supplies it.
let recordAuthor (r: DagOpRecord<TestMsg>) : MergeAuthor =
    if Option.isNone r.PromptId then
        MergeAuthor.Primary
    else
        MergeAuthor.Secondary r.PromptId

/// Replay a sink head to its tree through the production fold, `DagReplay.replay`
/// — the PRIMARY-parent spine, each merge node checked against the outcome hash
/// it committed to — failing loudly on any replay error. The DAG suite kept its
/// own copy of the spine walk until Phase 2043; a test oracle that is a second
/// implementation of the code under test drifts from it silently, and that one
/// had: it never checked a merge node's `OutcomeHash`.
let replaySpine
    (sink: IDagOpStreamSink<TestMsg>)
    (streamId: string)
    (initial: Node<TestMsg>)
    (head: string)
    : Node<TestMsg> =
    let getRecord (hash: string) =
        sink.TryGet(streamId, hash) |> Async.RunSynchronously

    match DagReplay.replay getRecord initial head with
    | Ok tree -> tree
    | Error e -> failwithf "replaySpine: %A" e
