namespace Fuaran.UI.OpStream.Replay

open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  Replay — fold an OpRecord sequence through the apply engine.
//
//  Given `initialTree` and a sequence of `OpRecord<'Msg>`, walks records in
//  order applying each via `Fuaran.UI.Ops.Apply.apply` and returns the final
//  tree or the first failure with context.
//
//  Failures during replay are surfaced verbatim — replay is the diagnostic
//  surface for the downstream AI consumer and the AI-emission eval suite
//  "Replay diverged at record N: <ApplyError>" is the
//  diagnostic shape both consumers want.
//
//  Note: replay does NOT verify the hash chain. Use
//  `Fuaran.UI.OpStream.Abstractions.Verify.chain` for that pass; the two
//  concerns are orthogonal (replay drives the apply engine; hash-chain
//  verification checks the stream was not corrupted, truncated or reordered
//  in storage — the chain is unkeyed, so it says nothing about a writer who
//  edited the records and re-chained them; see CRYPTO.md).
// ============================================================================

[<RequireQualifiedAccess>]
type ReplayError =
    /// The record at this sequence failed to apply against the in-flight tree.
    | ApplyFailed of sequence: int * applyError: ApplyError

module Replay =

    /// Apply every record in `records` to `initialTree` in source order,
    /// returning the final tree on success or the first apply failure.
    /// Records are consumed once; `records` may be lazy.
    let applyTo<'Msg> (initialTree: Node<'Msg>) (records: OpRecord<'Msg> seq) : Result<Node<'Msg>, ReplayError> =
        let mutable tree: Node<'Msg> = initialTree
        let mutable failure: ReplayError option = None

        use enumerator = records.GetEnumerator()
        let mutable stop = false

        while not stop && enumerator.MoveNext() do
            let record = enumerator.Current

            match Apply.apply record.Op tree with
            | Ok updated -> tree <- updated
            | Error applyError ->
                failure <- Some(ReplayError.ApplyFailed(record.Sequence, applyError))
                stop <- true

        match failure with
        | Some err -> Error err
        | None -> Ok tree
