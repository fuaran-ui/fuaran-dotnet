module Fuaran.UI.OpStream.Dag.Tests.TestSupport

open System
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.Merge

// ============================================================================
//  Shared fixtures + helpers for the DAG op-stream suite.
//
//  `TestMsg` is the smallest viable 'Msg. The DAG tests exercise structural +
//  style ops against a small dashboard, plus the branch/merge topology over
//  the content-addressed record DAG.
// ============================================================================

type TestMsg =
    | NoOp
    | Selected of int

let ts (unix: int64) : DateTimeOffset = DateTimeOffset.FromUnixTimeSeconds unix

let dashboardId = NodeId "dash"
let leftChildId = NodeId "left"
let rightChildId = NodeId "right"

/// Genesis tree: a dashboard with two markdown panes.
let buildDashboard () : Node<TestMsg> =
    Fuaran.dashboard
        "dash"
        { Defaults.dashboard<TestMsg> with
            Children = [ Fuaran.markdown "left" "Left pane"; Fuaran.markdown "right" "Right pane" ] }

let canonical (n: Node<TestMsg>) : string = CanonicalJson.encodeNode n

/// Fold an op over a tree, failing loudly on apply error (test ops are always
/// valid against their tree).
let applyOk (op: TreeOp<TestMsg>) (tree: Node<TestMsg>) : Node<TestMsg> =
    match Apply.apply op tree with
    | Ok t -> t
    | Error e -> failwithf "apply failed for %A: %A" op e

/// Append `op` as a child of `parent` (single-parent linear step) and return
/// the freshly-content-addressed DAG record. `parent = None` is a genesis node.
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

    DagOpRecord.create streamId parents op None "tester" (ts unix) OpResultEnvelope.Success

/// Add a record to the sink synchronously (tests are single-threaded).
let add (sink: IDagOpStreamSink<TestMsg>) (r: DagOpRecord<TestMsg>) : unit = sink.Add r |> Async.RunSynchronously

/// The host-side precedence classifier the DAG suite wires into `DagMerge` /
/// `DagOverlay`: a record with no `PromptId` is `Primary` (the human pin in this
/// host's chosen semantics), otherwise it is a `Secondary` op carrying its
/// prompt id as the opaque provenance tag. The merge layer NO LONGER bakes this
/// interpretation in (Phase 331) — the host supplies it here.
let recordAuthor (r: DagOpRecord<TestMsg>) : MergeAuthor =
    if Option.isNone r.PromptId then
        MergeAuthor.Primary
    else
        MergeAuthor.Secondary r.PromptId

/// Replay a sink head to its tree by folding ops along the PRIMARY-parent
/// spine (`Parents[0]`). A merge node's op is the replay delta from its
/// primary parent, so this reconstructs the merged tree. Tombstoned nodes on
/// the spine are a defect (they should never be a live head's ancestor) — fail
/// loudly if encountered.
let replaySpine
    (sink: IDagOpStreamSink<TestMsg>)
    (streamId: string)
    (initial: Node<TestMsg>)
    (head: string)
    : Node<TestMsg> =
    let rec collect (hash: string) (acc: DagOpRecord<TestMsg> list) : DagOpRecord<TestMsg> list =
        match sink.TryGet(streamId, hash) |> Async.RunSynchronously with
        | None -> failwithf "replaySpine: unknown hash %s" hash
        | Some r ->
            if r.Tombstoned then
                failwithf "replaySpine: tombstoned node %s on a live spine" hash

            match r.Parents with
            | [] -> r :: acc
            | primary :: _ -> collect primary (r :: acc)

    collect head [] |> List.fold (fun t r -> applyOk r.Op t) initial

// ─── Minimal op codec for the Sqlite DAG sink tests ───────────────────────
//
// Covers only the closure-free op shapes the Sqlite tests exercise:
// UpdateStyle, RemoveNode, and the empty Batch (the tombstone placeholder the
// sink writes). Compact single-letter discriminator, test-only.

module private DagTestOpJson =
    let private toneStr (t: ToneVariant) =
        match t with
        | ToneVariant.Brand -> "Brand"
        | ToneVariant.Success -> "Success"
        | ToneVariant.Critical -> "Critical"
        | _ -> "Default"

    let private toneOf (s: string) =
        match s with
        | "Brand" -> ToneVariant.Brand
        | "Success" -> ToneVariant.Success
        | "Critical" -> ToneVariant.Critical
        | _ -> ToneVariant.Default

    let encode (op: TreeOp<TestMsg>) : string =
        match op with
        | TreeOp.RemoveNode(NodeId raw) -> sprintf "R|%s" raw
        | TreeOp.UpdateStyle(NodeId raw, style) -> sprintf "S|%s|%s" raw (toneStr style.Tone)
        | TreeOp.Batch [] -> "B"
        | other -> failwithf "DagTestOpJson: unsupported op shape %A" other

    let decode (json: string) : Result<TreeOp<TestMsg>, string> =
        match json.Split('|') with
        | [| "R"; id |] -> Ok(TreeOp.RemoveNode(NodeId id))
        | [| "S"; id; tone |] ->
            Ok(
                TreeOp.UpdateStyle(
                    NodeId id,
                    { Defaults.style with
                        Tone = toneOf tone }
                )
            )
        | [| "B" |] -> Ok(TreeOp.Batch [])
        | _ -> Error(sprintf "DagTestOpJson: unrecognised payload '%s'" json)

let dagTestCodec: IOpJsonCodec<TestMsg> =
    { new IOpJsonCodec<TestMsg> with
        member _.EncodeOp op = DagTestOpJson.encode op
        member _.DecodeOp json = DagTestOpJson.decode json }
