namespace Fuaran.UI.OpStream.Dag.Merge

open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions

// ============================================================================
//  DagFold — the tier's binding of Core's replay, checkpoints and compaction
//  (Phase 2645, the cut).
//
//  Replay is Core's closure fold (`Dag.tryReplayTo`): a head's whole ancestor
//  closure, each node's op applied once, in Core's deterministic drain order.
//  Checkpoints are Core's (`Dag.Checkpoint`: a state sealed at a node with the
//  linear snapshot's pre-image), and so are the bounded replay above one
//  (`Dag.replayFrom`) and the compaction behind one (`Dag.compactAt`). This
//  module defines none of them: it fixes the three things the tier supplies —
//
//   - the WITNESS: `StreamEntry.coreWitness` with `Apply` the merge policy's
//     `DagReconcile.applyFold`, so an agreeing pair and a rebased reorder replay
//     in Core's order exactly as the policy derived them (its `Encode` is the
//     envelope's, so a node's address is the one `DagOpRecord.nodeId` mints);
//   - the HASH: `StreamEntry.hashFn`, the tier's SHA-256;
//   - the STATE ENCODING a checkpoint seals: `CanonicalJson.encodeNode`.
//
//  It replaced `DagReplay` (a primary-spine replay of asserted merge deltas)
//  and `DagCheckpoint` (a second snapshot seal), both deleted at the cut.
// ============================================================================

[<RequireQualifiedAccess>]
module DagFold =

    /// Core's witness over the envelope with the policy's apply.
    let witness<'Msg> () : Fuaran.Core.StreamWitness<StreamEntry<'Msg>, Node<'Msg>, ApplyError> =
        { StreamEntry.coreWitness () with
            Apply = fun entry tree -> DagReconcile.applyFold entry.Op tree }

    let private encodeTree (tree: Node<'Msg>) : string = CanonicalJson.encodeNode tree

    /// The tree at `head`: Core's closure fold from `initial`.
    let replayTo<'Msg>
        (initial: Node<'Msg>)
        (dag: Fuaran.Core.Dag.T<StreamEntry<'Msg>>)
        (head: string)
        : Result<Node<'Msg>, Fuaran.Core.Dag.ReplayFault<ApplyError>> =
        Fuaran.Core.Dag.tryReplayTo (witness ()) initial dag head

    /// `replayTo` over a set of records.
    let replayRecords<'Msg>
        (initial: Node<'Msg>)
        (records: DagOpRecord<'Msg> seq)
        (head: string)
        : Result<Node<'Msg>, Fuaran.Core.Dag.ReplayFault<ApplyError>> =
        replayTo initial (DagOpRecord.toDag records) head

    /// A checkpoint at `node`: Core's `Dag.checkpointAt`, the tree folded from
    /// `initial` and sealed.
    let checkpointAt<'Msg>
        (initial: Node<'Msg>)
        (dag: Fuaran.Core.Dag.T<StreamEntry<'Msg>>)
        (node: string)
        : Result<Fuaran.Core.Dag.Checkpoint<Node<'Msg>>, Fuaran.Core.Dag.CheckpointFault<ApplyError>> =
        Fuaran.Core.Dag.checkpointAt StreamEntry.hashFn encodeTree (witness ()) initial dag node

    /// Seal `tree` at `node` without replaying (Core's `Dag.sealAt`): the state is
    /// the caller's word, the seal makes it tamper-evident from here on.
    let sealAt<'Msg> (node: string) (tree: Node<'Msg>) : Fuaran.Core.Dag.Checkpoint<Node<'Msg>> =
        Fuaran.Core.Dag.sealAt StreamEntry.hashFn encodeTree (witness ()) node tree

    /// Recompute a checkpoint's seal (Core's `Dag.verifyCheckpoint`).
    let verifyCheckpoint<'Msg>
        (checkpoint: Fuaran.Core.Dag.Checkpoint<Node<'Msg>>)
        (dag: Fuaran.Core.Dag.T<StreamEntry<'Msg>>)
        : Result<unit, Fuaran.Core.Dag.CheckpointBreak> =
        Fuaran.Core.Dag.verifyCheckpoint StreamEntry.hashFn encodeTree (witness ()) checkpoint dag

    /// The tree at `head`, folding only the history above `checkpoint` (Core's
    /// `Dag.replayFrom`). The seal is not checked here — `verifyCheckpoint` is.
    let replayFrom<'Msg>
        (checkpoint: Fuaran.Core.Dag.Checkpoint<Node<'Msg>>)
        (dag: Fuaran.Core.Dag.T<StreamEntry<'Msg>>)
        (head: string)
        : Result<Node<'Msg>, Fuaran.Core.Dag.CheckpointFault<ApplyError>> =
        Fuaran.Core.Dag.replayFrom (witness ()) checkpoint dag head

    /// Compact at `node` (Core's `Dag.compactAt`): the checkpoint taken there and
    /// the DAG truncated behind it.
    let compactAt<'Msg>
        (initial: Node<'Msg>)
        (dag: Fuaran.Core.Dag.T<StreamEntry<'Msg>>)
        (node: string)
        : Result<Fuaran.Core.Dag.Compaction<Node<'Msg>, StreamEntry<'Msg>>, Fuaran.Core.Dag.CheckpointFault<ApplyError>> =
        Fuaran.Core.Dag.compactAt StreamEntry.hashFn encodeTree (witness ()) initial dag node

    /// Verify a DAG that begins at a checkpoint (Core's `Dag.firstBreakFrom`).
    let firstBreakFrom<'Msg>
        (checkpoint: Fuaran.Core.Dag.Checkpoint<Node<'Msg>>)
        (dag: Fuaran.Core.Dag.T<StreamEntry<'Msg>>)
        : Fuaran.Core.Dag.CheckpointBreak option =
        Fuaran.Core.Dag.firstBreakFrom StreamEntry.hashFn encodeTree (witness ()) checkpoint dag
