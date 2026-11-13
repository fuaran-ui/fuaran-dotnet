namespace Fuaran.UI.OpStream.Dag.Merge

open System
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions

// ============================================================================
//  DagMerge — the merge of two DAG heads (Phase 2645, the cut: the UI DAG is
//  Core's DAG, and its merge is the reconciliation policy over Core's fold).
//
//  `DagMerge.mergeDag` is the ONE merge. Over Core's DAG of the stream's nodes:
//
//   1. Equal heads are `AlreadyMerged`; a head that is the other's ancestor is
//      a `FastForward` (Core's `Dag.mergeBase` names the base).
//   2. Otherwise the two branch deltas are Core's `Dag.betweenOps` from the
//      base, and the base tree is Core's closure fold (`DagFold.replayTo`, or
//      `DagFold.replayFrom` above the context's checkpoint).
//   3. The policy (`DagReconcile.reconcileWith`, WIRE_FORMAT §34) derives the
//      merged tree and the script, or refuses with a typed fault; the context's
//      `Precedence` side, when named, re-asserts its ops of each pair it settles
//      as script, and an edit beside a removal of its node refuses under it as
//      without it (§34.4).
//   4. The validator gate (`ValidatorGate.verdict`, Core's introduced-findings
//      gate) runs over the two branch trees and the derived one.
//   5. The merge node is minted by Core (`Dag.merge`): a node with the two heads
//      as parents whose op is a `StreamEntry` of the script (`TreeOp.Batch`).
//   6. The node is CHECKED before it is handed back: Core's closure fold of the
//      new head must reach the derived tree, byte for byte. Core folds both
//      branches in its drain order rather than A-then-B, so a merge the policy
//      derived in both sequential orders but that does not replay in Core's is
//      refused as `DeltaNotReplayable`, never recorded.
//
//  Every ancestry question above is asked through ONE body (Phase 2692):
//  `mergeDag` answers it with Core's unindexed walks over a `Dag.T`;
//  `mergeIndexed` answers it from Core's reachability index (`Dag.Reach`) and
//  mints through `Dag.mergeIndexed`, handing back the index extended with the
//  merge node, so a caller that carries the index never rebuilds it. The tier
//  holds no ancestry code of its own: both are Core's, held equal by Core's
//  `Conformance.reachLaws`.
//
//  There is no tree-level 3-way merge, no merge-outcome identity, no replay
//  spine and no virtual base (`TreeMerge`, `DagReplay`, `DagPrimacy` and the
//  recursive base retired at the cut). A criss-cross takes Core's `mergeBase`;
//  history both heads hold above it is in both deltas and folds as agreement.
// ============================================================================

/// Why a merge could not replay a tree it needed.
[<RequireQualifiedAccess>]
type MergeReplayFault =
    /// Core's replay of a head or the base faulted (from genesis, or above the
    /// context's checkpoint).
    | Replay of Fuaran.Core.Dag.CheckpointFault<ApplyError>
    /// The context's checkpoint does not verify against the DAG. Tamper
    /// evidence: never fallen back from.
    | Checkpoint of Fuaran.Core.Dag.CheckpointBreak

/// A merge whose derived tree its own node does not replay to (Phase 1526's
/// refusal, re-stated over Core's fold at Phase 2645).
type MergeDeltaMismatch =
    {
        /// The canonical hash of the tree the policy derived.
        DerivedHash: string
        /// The canonical hash of the tree Core's fold of the minted node reached;
        /// `None` when the fold faulted.
        ReplayedHash: string option
        /// The fold's fault, when it faulted.
        Fault: MergeReplayFault option
    }

/// Outcome of a merge attempt.
[<RequireQualifiedAccess>]
type MergeResult<'Msg> =
    /// The two heads are identical — nothing to merge.
    | AlreadyMerged of head: string
    /// One head is an ancestor of the other; advance the trunk to `resultHead`.
    /// No new node.
    | FastForward of resultHead: string
    /// The policy derived the merge. `record` is the new merge node (add it, then
    /// `TryAdvanceHead` to `record.Hash`); `tree` is the merged tree.
    | Merged of record: DagOpRecord<'Msg> * tree: Node<'Msg>
    /// The policy refused (WIRE_FORMAT §34.5), or the validator gate did.
    | Refused of fault: ReconcileFault<'Msg>
    /// The policy derived `tree`, but Core's fold of the minted node does not
    /// reach it, so no node is handed back.
    | DeltaNotReplayable of tree: Node<'Msg> * mismatch: MergeDeltaMismatch
    /// Core refused to mint the merge node (an address it holds for different content).
    | MintRefused of fault: Fuaran.Core.DagAppendFault
    /// The two heads share no common ancestor.
    | NoCommonBase
    /// A base or branch tree could not be replayed.
    | ReplayFailed of MergeReplayFault

/// The outcome of a merge under a validator policy (Phase 184). `Diagnostics`
/// carries the merge-INTRODUCED defects: on a gated refusal they are the
/// fault's; on a `Diagnostic` policy they accompany a clean `Merged`. Empty when
/// no validator ran or the merge introduced nothing.
type GatedMergeOutcome<'Msg> =
    { Result: MergeResult<'Msg>
      Diagnostics: MergeDefect list }

/// The outcome of a merge over Core's reachability index (Phase 2692): the
/// merge's outcome, and the index of the DAG after it — extended with the merge
/// node when the outcome is `Merged`, the index merged over otherwise.
type IndexedMergeOutcome<'Msg> =
    { Outcome: GatedMergeOutcome<'Msg>
      Reach: Fuaran.Core.Dag.Reach<StreamEntry<'Msg>> }

/// Who a minted merge node is attributed to, and the telemetry context it
/// carries (Phase 1525). The actor, the prompt and the result envelope are the
/// node's own, inside its address, like every other node's.
type MergeAttribution =
    {
        /// The typed author of the merge.
        Actor: Actor
        /// The prompt id the host attributes this merge to, when a prompt drove it.
        ///
        /// Named `Prompt` and not after the record field it lands in, deliberately:
        /// the open-core invariant proves by source scan that this package derives
        /// NOTHING from that field. This carries a value the HOST supplies straight
        /// through to the node it mints.
        Prompt: string option
        /// How the merge REPORTED.
        ResultEnvelope: OpResultEnvelope
    }

module MergeAttribution =
    /// The legacy `"merge"` actor, no prompt, `Success`.
    let anonymous: MergeAttribution =
        { Actor = Actor.ofLegacyString "merge"
          Prompt = None
          ResultEnvelope = OpResultEnvelope.Success }

    /// Attribute a merge to `actor`, no prompt, `Success`.
    let ofActor (actor: Actor) : MergeAttribution = { anonymous with Actor = actor }

/// The context a caller threads into a merge — the one merge's every knob.
type MergeContext<'Msg> =
    {
        /// Who the minted merge node is attributed to.
        Attribution: MergeAttribution
        /// A Core checkpoint to replay FROM (`DagFold.checkpointAt`). A head it
        /// does not bound is replayed in full; one that fails its seal is refused.
        Checkpoint: Fuaran.Core.Dag.Checkpoint<Node<'Msg>> option
        /// The validator gate over a derived merge (Phase 184).
        Policy: MergePolicy<'Msg>
        /// Precedence as a script (Phase 2645): the side whose ops of a refused
        /// pair the merge re-asserts (`A` is the first head named, `B` the
        /// second). An edit beside a removal of its node refuses as
        /// `DeleteModify` under either side, as it does under `None` (Phase
        /// 2695, WIRE_FORMAT §34.4). `None` refuses every pair the policy does
        /// not admit.
        Precedence: ReconcileSide option
    }

module MergeContext =
    /// Anonymous attribution, no checkpoint, no validator, no precedence.
    let defaults<'Msg> : MergeContext<'Msg> =
        { Attribution = MergeAttribution.anonymous
          Checkpoint = None
          Policy = MergePolicy.lenient
          Precedence = None }

/// What a `mergeIntoTrunkWith` attempt did (Phase 1525).
[<RequireQualifiedAccess>]
type TrunkMergeOutcome<'Msg> =
    /// The trunk now points at this head (a fast-forward, a committed merge
    /// node, or a branch that was already the trunk).
    | Advanced of head: string
    /// The merge refused; the caller resolves and re-merges.
    | Refused of fault: ReconcileFault<'Msg>
    /// The policy derived a tree its node does not replay to.
    | DeltaNotReplayable of tree: Node<'Msg> * mismatch: MergeDeltaMismatch
    /// Core refused to mint the merge node.
    | MintRefused of fault: Fuaran.Core.DagAppendFault
    /// The branch and the trunk share no common ancestor.
    | NoCommonBase
    /// A base or branch tree could not be replayed.
    | ReplayFailed of MergeReplayFault
    /// The trunk CAS lost its race `maxRetries` times over; retrying is correct.
    | Contended

module DagMerge =

    let private plain (r: MergeResult<'Msg>) : GatedMergeOutcome<'Msg> = { Result = r; Diagnostics = [] }

    let private canonicalHash (tree: Node<'Msg>) : string =
        Fuaran.Core.Hash.sha256Hex (CanonicalJson.encodeNode tree)

    /// The ancestry a merge asks about one DAG (Phase 2692): its merge base, a
    /// branch delta, the replays, and the mint of the merge node — answered by
    /// Core's unindexed walks over a `Dag.T` (`ofDag`) or by Core's
    /// reachability index (`ofReach`). The merge body below is written once over
    /// it, so the two entry points are one definition of a merge and differ only
    /// in what each question costs. Core's `Conformance.reachLaws` hold every
    /// indexed answer equal to the unindexed one.
    type private Ancestry<'Msg> =
        {
            Dag: Fuaran.Core.Dag.T<StreamEntry<'Msg>>
            MergeBase: string -> string -> string option
            BetweenOps: string -> string -> StreamEntry<'Msg> list
            ReplayTo: Node<'Msg> -> string -> Result<Node<'Msg>, Fuaran.Core.Dag.ReplayFault<ApplyError>>
            ReplayFrom:
                Fuaran.Core.Dag.Checkpoint<Node<'Msg>>
                    -> string
                    -> Result<Node<'Msg>, Fuaran.Core.Dag.CheckpointFault<ApplyError>>
            /// Mint the merge node of the two heads: what Core wrote, and the
            /// ancestry of the DAG holding it.
            Mint:
                Actor
                    -> StreamEntry<'Msg>
                    -> string
                    -> string
                    -> Result<Fuaran.Core.Dag.Written<StreamEntry<'Msg>> * Ancestry<'Msg>, Fuaran.Core.DagAppendFault>
            /// The index this ancestry answers from, when it is indexed.
            Reach: Fuaran.Core.Dag.Reach<StreamEntry<'Msg>> option
        }

    let rec private ofDag<'Msg> (dag: Fuaran.Core.Dag.T<StreamEntry<'Msg>>) : Ancestry<'Msg> =
        { Dag = dag
          MergeBase = Fuaran.Core.Dag.mergeBase dag
          BetweenOps = Fuaran.Core.Dag.betweenOps dag
          ReplayTo = fun initial head -> DagFold.replayTo initial dag head
          ReplayFrom = fun cp head -> DagFold.replayFrom cp dag head
          Mint =
            fun actor entry headA headB ->
                Fuaran.Core.Dag.merge (DagOpRecord.stream ()) actor entry headA headB dag
                |> Result.map (fun written -> written, ofDag written.Dag)
          Reach = None }

    let rec private ofReach<'Msg> (reach: Fuaran.Core.Dag.Reach<StreamEntry<'Msg>>) : Ancestry<'Msg> =
        { Dag = Fuaran.Core.Dag.Reach.dag reach
          MergeBase = Fuaran.Core.Dag.Reach.mergeBase reach
          BetweenOps = fun baseId head -> Fuaran.Core.Dag.Reach.between reach baseId head |> List.map _.Op
          ReplayTo = fun initial head -> DagFold.replayToWith initial reach head
          ReplayFrom = fun cp head -> DagFold.replayFromWith cp reach head
          Mint =
            fun actor entry headA headB ->
                Fuaran.Core.Dag.mergeIndexed (DagOpRecord.stream ()) actor entry headA headB reach
                |> Result.map (fun indexed -> indexed.Written, ofReach indexed.Reach)
          Reach = Some reach }

    /// The tree at `head`: above the context's checkpoint where it bounds the
    /// head, else Core's fold from `initial`.
    let private replayAt
        (checkpoint: Fuaran.Core.Dag.Checkpoint<Node<'Msg>> option)
        (initial: Node<'Msg>)
        (ancestry: Ancestry<'Msg>)
        (head: string)
        : Result<Node<'Msg>, MergeReplayFault> =
        let full () =
            ancestry.ReplayTo initial head
            |> Result.mapError (Fuaran.Core.Dag.CheckpointFault.Replay >> MergeReplayFault.Replay)

        match checkpoint with
        | None -> full ()
        | Some cp ->
            match ancestry.ReplayFrom cp head with
            | Ok tree -> Ok tree
            | Error(Fuaran.Core.Dag.CheckpointFault.Unreached _)
            | Error(Fuaran.Core.Dag.CheckpointFault.Uncovered _) -> full ()
            | Error f -> Error(MergeReplayFault.Replay f)

    /// The merge body both entry points share: the outcome, and the ancestry of
    /// the DAG after it (the merge node's, when one was handed back).
    let private mergeOver<'Msg>
        (context: MergeContext<'Msg>)
        (streamId: string)
        (initial: Node<'Msg>)
        (ancestry: Ancestry<'Msg>)
        (headA: string)
        (headB: string)
        (now: DateTimeOffset)
        : GatedMergeOutcome<'Msg> * Ancestry<'Msg> =
        let dag = ancestry.Dag
        let unchanged (outcome: GatedMergeOutcome<'Msg>) = outcome, ancestry

        let unknown =
            [ headA; headB ] |> List.tryFind (fun h -> not (dag.Nodes.ContainsKey h))

        // A checkpoint whose node the stream holds must verify; one it does not hold
        // bounds no head here and is ignored.
        let sealFault =
            context.Checkpoint
            |> Option.filter (fun cp -> dag.Nodes.ContainsKey cp.Node)
            |> Option.bind (fun cp ->
                match DagFold.verifyCheckpoint cp dag with
                | Ok() -> None
                | Error b -> Some b)

        if headA = headB then
            unchanged (plain (MergeResult.AlreadyMerged headA))
        else
            match unknown, sealFault with
            | Some h, _ ->
                unchanged (
                    plain (
                        MergeResult.ReplayFailed(
                            MergeReplayFault.Replay(
                                Fuaran.Core.Dag.CheckpointFault.Replay(Fuaran.Core.Dag.ReplayFault.UnknownHead h)
                            )
                        )
                    )
                )
            | None, Some b -> unchanged (plain (MergeResult.ReplayFailed(MergeReplayFault.Checkpoint b)))
            | None, None ->
                // The fast-forward test is the merge base's: a head that is the
                // other's ancestor is exactly the base the rule picks (its closure
                // is the largest common one), so one question answers both.
                match ancestry.MergeBase headA headB with
                | None -> unchanged (plain MergeResult.NoCommonBase)
                | Some b when b = headA -> unchanged (plain (MergeResult.FastForward headB))
                | Some b when b = headB -> unchanged (plain (MergeResult.FastForward headA))
                | Some baseId ->
                    let replay = replayAt context.Checkpoint initial ancestry

                    match replay baseId, replay headA, replay headB with
                    | Error f, _, _
                    | _, Error f, _
                    | _, _, Error f -> unchanged (plain (MergeResult.ReplayFailed f))
                    | Ok baseTree, Ok treeA, Ok treeB ->
                        let delta head =
                            ancestry.BetweenOps baseId head |> List.map _.Op

                        match DagReconcile.reconcileWith context.Precedence baseTree (delta headA) (delta headB) with
                        | Error fault -> unchanged (plain (MergeResult.Refused fault))
                        | Ok derived ->
                            let diagnostics, blocked =
                                ValidatorGate.verdict context.Policy treeA treeB derived.Tree

                            if blocked then
                                unchanged
                                    { Result = MergeResult.Refused(ReconcileFault.IntroducedDefects diagnostics)
                                      Diagnostics = diagnostics }
                            else
                                let entry =
                                    { Op = TreeOp.Batch derived.Script
                                      Timestamp = now
                                      PromptId = context.Attribution.Prompt
                                      ResultEnvelope = context.Attribution.ResultEnvelope }

                                let result, after =
                                    match ancestry.Mint context.Attribution.Actor entry headA headB with
                                    | Error fault -> MergeResult.MintRefused fault, ancestry
                                    | Ok(written, minted) ->
                                        let mismatch replayed fault =
                                            MergeResult.DeltaNotReplayable(
                                                derived.Tree,
                                                { DerivedHash = canonicalHash derived.Tree
                                                  ReplayedHash = replayed
                                                  Fault = fault }
                                            ),
                                            ancestry

                                        // Above the context's checkpoint where it bounds the new head,
                                        // so the check costs the divergence, not the whole history.
                                        match replayAt context.Checkpoint initial minted written.Id with
                                        | Error f -> mismatch None (Some f)
                                        | Ok replayed when
                                            CanonicalJson.encodeNode replayed <> CanonicalJson.encodeNode derived.Tree
                                            ->
                                            mismatch (Some(canonicalHash replayed)) None
                                        | Ok _ ->
                                            MergeResult.Merged(
                                                { StreamId = streamId
                                                  Node = written.Dag.Nodes[written.Id] },
                                                derived.Tree
                                            ),
                                            minted

                                { Result = result
                                  Diagnostics = diagnostics },
                                after

    /// THE merge (Phase 2645): two heads of `dag` merged under `context`, the
    /// merge node minted by Core into `streamId`. Pure — nothing is stored; the
    /// caller adds the record and advances its head (`commitMerge`). Asks Core's
    /// unindexed walks, so it builds nothing it does not use; a caller that
    /// holds Core's reachability index merges through `mergeIndexed` instead,
    /// with the same answer.
    let mergeDag<'Msg>
        (context: MergeContext<'Msg>)
        (streamId: string)
        (initial: Node<'Msg>)
        (dag: Fuaran.Core.Dag.T<StreamEntry<'Msg>>)
        (headA: string)
        (headB: string)
        (now: DateTimeOffset)
        : GatedMergeOutcome<'Msg> =
        mergeOver context streamId initial (ofDag dag) headA headB now |> fst

    /// `mergeDag` over Core's reachability index (Phase 2692): the merge base,
    /// the two deltas and the replays read from `reach` (`Reach.mergeBase`,
    /// `Reach.between`, `Dag.tryReplayToWith` / `Dag.replayFromWith`) and the
    /// merge node minted by `Dag.mergeIndexed`, so the index travels with the DAG
    /// and is never rebuilt. The outcome is `mergeDag`'s on `Reach.dag reach`,
    /// exactly. The answer's `Reach` is the index extended with the merge node
    /// when the outcome is `Merged` (the DAG a caller holds once it adds that
    /// record), and `reach` itself otherwise.
    let mergeIndexed<'Msg>
        (context: MergeContext<'Msg>)
        (streamId: string)
        (initial: Node<'Msg>)
        (reach: Fuaran.Core.Dag.Reach<StreamEntry<'Msg>>)
        (headA: string)
        (headB: string)
        (now: DateTimeOffset)
        : IndexedMergeOutcome<'Msg> =
        let outcome, after =
            mergeOver context streamId initial (ofReach reach) headA headB now

        { Outcome = outcome
          Reach = after.Reach |> Option.defaultValue reach }

    /// `mergeDag` over `streamId`'s live records in `sink`.
    let mergeWith<'Msg>
        (context: MergeContext<'Msg>)
        (sink: IDagOpStreamSink<'Msg>)
        (streamId: string)
        (initial: Node<'Msg>)
        (headA: string)
        (headB: string)
        (now: DateTimeOffset)
        : Async<GatedMergeOutcome<'Msg>> =
        async {
            if headA = headB then
                return plain (MergeResult.AlreadyMerged headA)
            else
                let! records = sink.Records streamId
                return mergeDag context streamId initial (DagOpRecord.toDag records) headA headB now
        }

    /// `mergeWith` under `MergeContext.defaults`.
    let merge<'Msg>
        (sink: IDagOpStreamSink<'Msg>)
        (streamId: string)
        (initial: Node<'Msg>)
        (headA: string)
        (headB: string)
        (now: DateTimeOffset)
        : Async<MergeResult<'Msg>> =
        async {
            let! outcome = mergeWith MergeContext.defaults sink streamId initial headA headB now
            return outcome.Result
        }

    /// Add a minted merge record and CAS the trunk to it.
    let commitMerge<'Msg>
        (sink: IDagOpStreamSink<'Msg>)
        (streamId: string)
        (expectedHead: string option)
        (record: DagOpRecord<'Msg>)
        : Async<bool> =
        async {
            do! sink.Add record
            return! sink.TryAdvanceHead(streamId, expectedHead, record.Hash)
        }

    /// Merge `branchHead` into the trunk of `streamId` and advance the trunk,
    /// retrying the CAS up to `maxRetries` times when another writer moves it.
    let mergeIntoTrunkWith<'Msg>
        (context: MergeContext<'Msg>)
        (sink: IDagOpStreamSink<'Msg>)
        (streamId: string)
        (initial: Node<'Msg>)
        (branchHead: string)
        (now: DateTimeOffset)
        (maxRetries: int)
        : Async<TrunkMergeOutcome<'Msg>> =
        let rec attempt (remaining: int) =
            async {
                let retry () =
                    if remaining > 0 then
                        attempt (remaining - 1)
                    else
                        async { return TrunkMergeOutcome.Contended }

                let! trunkOpt = sink.Head streamId

                match trunkOpt with
                | None ->
                    let! ok = sink.TryAdvanceHead(streamId, None, branchHead)

                    if ok then
                        return TrunkMergeOutcome.Advanced branchHead
                    else
                        return! retry ()
                | Some trunk when trunk = branchHead -> return TrunkMergeOutcome.Advanced trunk
                | Some trunk ->
                    let! outcome = mergeWith context sink streamId initial trunk branchHead now

                    match outcome.Result with
                    | MergeResult.AlreadyMerged h -> return TrunkMergeOutcome.Advanced h
                    | MergeResult.FastForward h ->
                        let! ok = sink.TryAdvanceHead(streamId, Some trunk, h)

                        if ok then
                            return TrunkMergeOutcome.Advanced h
                        else
                            return! retry ()
                    | MergeResult.Merged(record, _) ->
                        let! ok = commitMerge sink streamId (Some trunk) record

                        if ok then
                            return TrunkMergeOutcome.Advanced record.Hash
                        else
                            return! retry ()
                    | MergeResult.Refused fault -> return TrunkMergeOutcome.Refused fault
                    | MergeResult.DeltaNotReplayable(tree, mismatch) ->
                        return TrunkMergeOutcome.DeltaNotReplayable(tree, mismatch)
                    | MergeResult.MintRefused fault -> return TrunkMergeOutcome.MintRefused fault
                    | MergeResult.NoCommonBase -> return TrunkMergeOutcome.NoCommonBase
                    | MergeResult.ReplayFailed f -> return TrunkMergeOutcome.ReplayFailed f
            }

        attempt maxRetries

    /// `mergeIntoTrunkWith` under `MergeContext.defaults`: the new trunk head,
    /// or `None` for any outcome but `Advanced`.
    let mergeIntoTrunk<'Msg>
        (sink: IDagOpStreamSink<'Msg>)
        (streamId: string)
        (initial: Node<'Msg>)
        (branchHead: string)
        (now: DateTimeOffset)
        (maxRetries: int)
        : Async<string option> =
        async {
            match! mergeIntoTrunkWith MergeContext.defaults sink streamId initial branchHead now maxRetries with
            | TrunkMergeOutcome.Advanced h -> return Some h
            | _ -> return None
        }
