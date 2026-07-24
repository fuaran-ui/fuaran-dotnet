namespace Fuaran.UI.OpStream.Dag.Merge

open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Dag.Abstractions

// ============================================================================
//  DagReplay — reconstruct a tree at a DAG head.
//
//  Replay follows the PRIMARY-parent spine (`Parents[0]`), folding each node's
//  `Op` through the apply engine over an initial tree. A merge node's `Op` is
//  the replay delta from its primary parent's tree to the merged tree, so the
//  spine fold reconstructs the merged tree without re-walking the secondary
//  branch (whose effect is already captured in the merge delta). A linear
//  (single-parent) chain is the degenerate case: the spine IS the chain.
// ============================================================================

[<RequireQualifiedAccess>]
type DagReplayError =
    /// A hash on the spine has no record in the supplied lookup.
    | UnknownHash of hash: string
    /// A spine node is tombstoned — its payload was pruned, so the tree can no
    /// longer be reconstructed past it. A live head must never have a
    /// tombstoned ancestor on its spine (that is a retention-policy defect).
    | TombstonedOnSpine of hash: string
    /// The apply engine rejected a spine op.
    | ApplyFailed of hash: string * error: ApplyError

module DagReplay =

    /// Replay `head` to its tree by folding ops along the primary-parent spine.
    /// `getRecord` resolves a hash to its record (typically a pre-loaded map of
    /// the stream's records). `initial` is the genesis tree.
    let replay<'Msg>
        (getRecord: string -> DagOpRecord<'Msg> option)
        (initial: Node<'Msg>)
        (head: string)
        : Result<Node<'Msg>, DagReplayError> =
        // Collect the spine head→genesis, then fold genesis→head.
        let rec collect (hash: string) (acc: DagOpRecord<'Msg> list) : Result<DagOpRecord<'Msg> list, DagReplayError> =
            match getRecord hash with
            | None -> Error(DagReplayError.UnknownHash hash)
            | Some r when r.Tombstoned -> Error(DagReplayError.TombstonedOnSpine hash)
            | Some r ->
                match r.Parents with
                | [] -> Ok(r :: acc)
                | primary :: _ -> collect primary (r :: acc)

        match collect head [] with
        | Error e -> Error e
        | Ok spine ->
            spine
            |> List.fold
                (fun (acc: Result<Node<'Msg>, DagReplayError>) (r: DagOpRecord<'Msg>) ->
                    match acc with
                    | Error _ -> acc
                    | Ok tree ->
                        match Apply.apply r.Op tree with
                        | Ok tree' -> Ok tree'
                        | Error e -> Error(DagReplayError.ApplyFailed(r.Hash, e)))
                (Ok initial)

    /// Checkpoint-bounded replay: reconstruct `head`'s tree starting from a
    /// `DagCheckpoint`'s snapshot rather than from genesis. The snapshot IS the
    /// tree AT `checkpoint.AtHash` (its op already applied), so only the spine
    /// TAIL — the nodes between `head` and `AtHash`, exclusive of `AtHash` — is
    /// folded over the snapshot. `head = AtHash` yields the snapshot unchanged.
    /// Errors if `AtHash` is not on `head`'s primary spine (the checkpoint does
    /// not bound this head). This is the DAG generalisation of the linear
    /// "replay from op-index N" — the cost bound is (checkpoint→head) tail
    /// length, not total history.
    let replayFromCheckpoint<'Msg>
        (getRecord: string -> DagOpRecord<'Msg> option)
        (checkpoint: DagCheckpoint<'Msg>)
        (head: string)
        : Result<Node<'Msg>, DagReplayError> =
        // Collect the spine tail head→…→(child of AtHash), stopping AT AtHash
        // (exclusive — its effect is already in the snapshot).
        let rec collect (hash: string) (acc: DagOpRecord<'Msg> list) : Result<DagOpRecord<'Msg> list, DagReplayError> =
            if hash = checkpoint.AtHash then
                Ok acc
            else
                match getRecord hash with
                | None -> Error(DagReplayError.UnknownHash hash)
                | Some r when r.Tombstoned -> Error(DagReplayError.TombstonedOnSpine hash)
                | Some r ->
                    match r.Parents with
                    | [] ->
                        // Reached a genesis without hitting AtHash — the
                        // checkpoint does not bound this head.
                        Error(DagReplayError.UnknownHash checkpoint.AtHash)
                    | primary :: _ -> collect primary (r :: acc)

        match collect head [] with
        | Error e -> Error e
        | Ok tail ->
            tail
            |> List.fold
                (fun (acc: Result<Node<'Msg>, DagReplayError>) (r: DagOpRecord<'Msg>) ->
                    match acc with
                    | Error _ -> acc
                    | Ok tree ->
                        match Apply.apply r.Op tree with
                        | Ok tree' -> Ok tree'
                        | Error e -> Error(DagReplayError.ApplyFailed(r.Hash, e)))
                (Ok checkpoint.Snapshot)
