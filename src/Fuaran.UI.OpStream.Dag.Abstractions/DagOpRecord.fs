namespace Fuaran.UI.OpStream.Dag.Abstractions

open System
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  DagOpRecord — a node of the UI op-stream DAG, which IS Core's DAG
//  (Phase 2645, the cut; see `docs/DAG_STRATEGY.md`).
//
//  A node is a `Fuaran.Core.DagNode<StreamEntry<'Msg>>`: its op is the linear
//  chain's provenance envelope (`StreamEntry` — the tree op, the timestamp, the
//  prompt id and the result envelope), encoded by `StreamEntry.encode`, the same
//  bytes the linear chain hashes. Its address is Core's `Dag.nodeId` under the
//  tier's SHA-256 (`StreamEntry.hashFn`) and Core's canonical config:
//
//      Id = sha256( <sorted parents joined by ","> | <Actor.encode actor> "|" <StreamEntry.encode entry> )
//
//  There is no second address function, no merge-outcome identity and no third
//  hash. A merge node is an ordinary node with two parents whose op is a
//  `StreamEntry` of its reconciliation SCRIPT (a `TreeOp.Batch`, empty when the
//  fold needs no script): Core DERIVES a merge's state by folding the head's
//  closure, so the node records only what the fold cannot decide itself.
//
//  `StreamId` is the sink's partition key and rides beside the node, outside the
//  address, exactly as the linear `OpRecord.StreamId` rides outside its chain
//  hash. The read members (`Hash`, `Parents`, `Op`, …) project the node; they
//  define nothing.
//
//  Every stored address moved once, at this cut (STABILITY.md 0.104.0); a store
//  written before it is re-addressed by the one-release re-mint tool, never
//  dual-read.
// ============================================================================

/// One node of the UI op-stream DAG: Core's node over the provenance envelope,
/// with the stream it is stored in. `Node.Id` is the content address
/// (`DagOpRecord.nodeId`); `Node.Parents` is `[]` at genesis, one parent for a
/// linear step and two for a merge; `Node.Op` is the envelope.
type DagOpRecord<'Msg> =
    { StreamId: string
      Node: Fuaran.Core.DagNode<StreamEntry<'Msg>> }

    /// The content address — `Node.Id`.
    member r.Hash: string = r.Node.Id
    /// The parent addresses, in author order — `Node.Parents`.
    member r.Parents: string list = r.Node.Parents
    /// The typed author — `Node.Actor`.
    member r.Actor: Actor = r.Node.Actor
    /// The provenance envelope — `Node.Op`.
    member r.Entry: StreamEntry<'Msg> = r.Node.Op
    /// The tree op the node records (a merge node's script, as a `Batch`).
    member r.Op: TreeOp<'Msg> = r.Node.Op.Op
    /// When the op was recorded.
    member r.Timestamp: DateTimeOffset = r.Node.Op.Timestamp
    /// The prompt behind the op, if any.
    member r.PromptId: string option = r.Node.Op.PromptId
    /// How the op reported.
    member r.ResultEnvelope: OpResultEnvelope = r.Node.Op.ResultEnvelope

module DagOpRecord =

    /// Core's stream over the envelope at the tier's SHA-256: the stream every
    /// UI DAG address is minted and verified under. Its witness is the
    /// verify-path one (`StreamEntry.coreWitness`); a replay takes the merge
    /// policy's witness, which encodes identically, so the addresses agree.
    let stream<'Msg> () : Fuaran.Core.Stream<StreamEntry<'Msg>, Fuaran.UI.Types.Node<'Msg>, ApplyError> =
        Fuaran.Core.OpStream.canonical StreamEntry.hashFn (StreamEntry.coreWitness ())

    /// The content address of a node of (`parents`, `actor`, `entry`): Core's
    /// `Dag.nodeId` over `stream ()`.
    let nodeId<'Msg> (parents: string list) (actor: Actor) (entry: StreamEntry<'Msg>) : string =
        Fuaran.Core.Dag.nodeId (stream ()) parents actor entry

    /// The address `record`'s own fields recompute to.
    let recomputeHash<'Msg> (record: DagOpRecord<'Msg>) : string =
        nodeId record.Node.Parents record.Node.Actor record.Node.Op

    /// A record of the node of (`parents`, `actor`, `entry`), addressed by `nodeId`.
    let ofEntry<'Msg> (streamId: string) (parents: string list) (actor: Actor) (entry: StreamEntry<'Msg>) =
        { StreamId = streamId
          Node =
            { Id = nodeId parents actor entry
              Parents = parents
              Actor = actor
              Op = entry } }

    /// Assemble a record for `op`, addressed by `nodeId`. A merge node is a
    /// record whose `parents` are the two heads and whose `op` is its script.
    let create<'Msg>
        (streamId: string)
        (parents: string list)
        (op: TreeOp<'Msg>)
        (promptId: string option)
        (actor: Actor)
        (timestamp: DateTimeOffset)
        (resultEnvelope: OpResultEnvelope)
        : DagOpRecord<'Msg> =
        ofEntry
            streamId
            parents
            actor
            { Op = op
              Timestamp = timestamp
              PromptId = promptId
              ResultEnvelope = resultEnvelope }

    /// Core's DAG over `records`' nodes. Keyed by each node's own `Id`, so it
    /// cannot refuse; a node whose id does not recompute is admitted, which is
    /// what `Dag.firstBreak` is asked about.
    let toDag<'Msg> (records: DagOpRecord<'Msg> seq) : Fuaran.Core.Dag.T<StreamEntry<'Msg>> =
        match
            records
            |> Seq.map (fun r -> r.Node.Id, r.Node)
            |> Map.ofSeq
            |> Fuaran.Core.Dag.ofNodes
        with
        | Ok dag -> dag
        | Error m -> invalidOp ("DagOpRecord.toDag: node filed under another id: " + m.Key)

    /// The one linear-to-DAG embedding fold: each linear record becomes a
    /// one-parent DAG node whose parent is the prior DAG node's address; the
    /// FIRST node's parents are `firstParents` (`[]` for a genesis, `[ anchor ]`
    /// for a branch hung off an existing node). The envelope is the linear
    /// record's own (`StreamEntry.ofRecord`), so a lifted node's op is
    /// byte-identical to the linear op. Shared by `ofLinear` and the verified
    /// lift (`DagLift`), so the two cannot embed differently.
    let internal embedLinear<'Msg> (firstParents: string list) (records: OpRecord<'Msg> list) : DagOpRecord<'Msg> list =
        records
        |> List.fold
            (fun (acc: DagOpRecord<'Msg> list) (r: OpRecord<'Msg>) ->
                let parents =
                    match acc with
                    | [] -> firstParents
                    | head :: _ -> [ head.Hash ]

                ofEntry r.StreamId parents r.Actor (StreamEntry.ofRecord r) :: acc)
            []
        |> List.rev

    /// Embed a *linear* `OpRecord<'Msg>` history as a single-parent DAG: each
    /// record becomes a one-parent node whose parent is the prior node's
    /// address (`[]` for the first). The DAG addresses are not the linear chain
    /// hashes (the linear pre-image folds the sequence and the previous hash, a
    /// DAG node's folds its parents), but each node's op is the linear record's
    /// envelope byte for byte.
    ///
    /// **The UNVERIFIED form.** It does not check the linear chain it embeds;
    /// the sanctioned route is `DagLift.ofLinearVerified` /
    /// `DagLift.ofLinearAnchored`, which refuse a broken segment and return a
    /// `LiftReceipt` anyone can recompute.
    let ofLinear<'Msg> (records: OpRecord<'Msg> list) : DagOpRecord<'Msg> list = embedLinear [] records
