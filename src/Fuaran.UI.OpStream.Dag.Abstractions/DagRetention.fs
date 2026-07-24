namespace Fuaran.UI.OpStream.Dag.Abstractions

open System
open Fuaran.UI.Types

// ============================================================================
//  DagRetention — the retention MECHANISM (not policy).
//
//  Three primitives, all mechanism — grace periods, auto-pin rules, and purge
//  triggers are host/domain POLICY layered over these and are out of scope:
//
//   1. Reachability GC roots. The live heads, the checkpoints, and any live
//      resume-coordinate / permalink are the GC roots. A record reachable from
//      a root (it is the root or one of its ancestors) MUST be kept live; a
//      record reachable from NO root is prunable.
//
//   2. Tombstone-don't-excise pruning. Pruning drops a record's payload while
//      preserving its hash + parent links, so the Merkle chain still verifies
//      (`DagVerify` accepts a tombstoned node's stored hash). The sink method
//      `Tombstone` performs the in-place mutation; this module decides WHICH
//      records are prunable.
//
//   3. Checkpoint-bounded replay. `DagCheckpoint` generalises the linear
//      `Checkpoint` primitive from "head at op-index N" to "tree at DAG node
//      H": a materialised snapshot of the apply-engine tree AT a content-
//      addressed node, so replay starts from the nearest checkpoint rather than
//      from genesis. (The checkpoint-bounded replay fold lives in
//      `Fuaran.UI.OpStream.Dag.Merge.DagReplay`, which has the apply engine.)
//
//  A resume coordinate is content-addressed-forward-compatible: it is EITHER a
//  linear op-index (the Phase-177 shape, against the linear stream) OR a DAG
//  node hash, behind ONE envelope shape — so a linear resume envelope upgrades
//  its coordinate type from `int` to `hash` with no envelope-shape change.
// ============================================================================

/// A resume anchor that survives the linear→DAG upgrade without changing the
/// envelope shape carrying it. `LinearOpIndex` is the Phase-177 shape (resume
/// from op N of a linear stream); `DagNodeHash` is the content-addressed
/// generalisation (resume from a DAG node). A host serialises whichever variant
/// applies; the envelope field type is the same DU either way.
[<RequireQualifiedAccess>]
type ResumeCoordinate =
    | LinearOpIndex of index: int
    | DagNodeHash of hash: string

module ResumeCoordinate =
    /// The DAG hash a coordinate pins as a GC root, if any. A `LinearOpIndex`
    /// contributes none (the linear stream's GC is index-based, not hash-based).
    let dagRoot (coord: ResumeCoordinate) : string option =
        match coord with
        | ResumeCoordinate.DagNodeHash h -> Some h
        | ResumeCoordinate.LinearOpIndex _ -> None

/// A materialised snapshot of the apply-engine tree AT a content-addressed DAG
/// node — the DAG generalisation of the linear `Checkpoint`. `SnapshotHash` is
/// `HashChain.sha256Hex (CanonicalJson.encodeNode Snapshot)`. Replay can resume
/// from `Snapshot` and fold only the tail descendants of `AtHash`.
type DagCheckpoint<'Msg> =
    { StreamId: string
      AtHash: string
      Snapshot: Node<'Msg>
      SnapshotHash: string }

/// The GC roots that keep records live: branch heads, checkpoint nodes, and
/// live resume coordinates / permalinks.
type GcRoots =
    { Heads: string list
      Checkpoints: string list
      ResumeCoordinates: ResumeCoordinate list }

module DagRetention =

    /// The distinct DAG hashes named by a `GcRoots` (heads + checkpoint nodes +
    /// the hash-bearing resume coordinates).
    let rootHashes (roots: GcRoots) : Set<string> =
        let resumeHashes = roots.ResumeCoordinates |> List.choose ResumeCoordinate.dagRoot

        Set.ofList (roots.Heads @ roots.Checkpoints @ resumeHashes)

    /// The set of records that MUST be kept live: the reachability closure of
    /// every GC root over `getParents`. Anything outside this set is prunable.
    let liveSet (getParents: string -> string list) (roots: GcRoots) : Set<string> =
        rootHashes roots
        |> Set.fold (fun acc r -> Set.union acc (DagTopology.reachable getParents r)) Set.empty

    /// The hashes in `allHashes` that are reachable from NO GC root — the
    /// prune candidates. (Pruning tombstones them; it never excises, so the
    /// chain stays verifiable.)
    let prunable (allHashes: string seq) (getParents: string -> string list) (roots: GcRoots) : Set<string> =
        let live = liveSet getParents roots
        allHashes |> Seq.filter (fun h -> not (live.Contains h)) |> Set.ofSeq

    /// `true` when `hash` is protected by a GC root (reachable from one) — the
    /// "a branch referenced by a live resume-coordinate/permalink is
    /// GC-protected" invariant, as a direct predicate.
    let isProtected (getParents: string -> string list) (roots: GcRoots) (hash: string) : bool =
        (liveSet getParents roots).Contains hash

// ============================================================================
//  Retention POLICY tier (Phase 179) — the decision/content split layered over
//  the Phase 178 reachability + tombstone MECHANISM above.
//
//  The MECHANISM (`DagRetention.prunable`) answers "is this record reachable
//  from a GC root?" — a binary, time-free, deterministic question. The POLICY
//  tier adds the two host/domain-configurable axes the phase calls for:
//
//   - **Decision/content split.** When a human rejects an AI proposal, the
//     lightweight DECISION ("AI proposed X at node H; human rejected; reason R")
//     is recorded as a permanent `RejectionTombstone` — the durable audit trail
//     that OUTLIVES the content. The bulky/sensitive proposal CONTENT it points
//     at (a DAG node) becomes GC-eligible (tombstone-able via the mechanism's
//     `Tombstone` sink method) once it is unreachable AND a grace window has
//     elapsed AND it is not auto-pinned. The two halves are different artefacts:
//     decisions are a host-persisted side-store of `RejectionTombstone`s, never
//     DAG nodes — so `contentGcEligible` (which ranges over DAG-node hashes)
//     structurally cannot return a decision, enforcing the split by type.
//
//   - **Grace / auto-pin / purge triggers** are host/domain `RetentionPolicy`
//     config, not language-tier constants. CLOCKS appear ONLY here (the grace
//     window since rejection) — NEVER in the hash/merge-identity path, per the
//     phase determinism rule. Crypto-shredding is flagged domain-tier and is
//     deliberately NOT built here (a host needing it overrides the purge step).
// ============================================================================

/// The permanent, lightweight "decision" half of the decision/content split: a
/// record that an AI proposal was rejected, kept FOREVER as the audit trail.
/// `ProposalHash` is the (bulky) DAG node whose CONTENT is GC-eligible once the
/// grace window elapses and it is unpinned; `ProposedAtHash` is the node the
/// proposal branched from; `Reason` + `RejectedAt` are the durable provenance.
type RejectionTombstone =
    { ProposalHash: string
      ProposedAtHash: string
      Reason: string
      RejectedAt: DateTimeOffset }

/// Host/domain-configurable retention POLICY over the mechanism. `Grace` holds
/// a rejected proposal's content live for a window after `RejectedAt` (clocks
/// are fine HERE — never in the hash path). `AutoPin` is an extra liveness
/// predicate protecting a hash from content GC regardless of reachability (e.g.
/// "always keep human-authored content for audit"). `PurgeWhen` is the
/// purge-TRIGGER hook: given the GC-eligible set, whether to actually sweep now
/// (a host threshold / schedule gate).
type RetentionPolicy =
    { Grace: TimeSpan
      AutoPin: string -> bool
      PurgeWhen: Set<string> -> bool }

module RetentionPolicy =
    /// No grace, no auto-pin, purge whenever anything is eligible — content is
    /// GC-eligible the instant it is unreachable (the mechanism's raw behaviour
    /// with an immediate sweep).
    let immediate: RetentionPolicy =
        { Grace = TimeSpan.Zero
          AutoPin = (fun _ -> false)
          PurgeWhen = (fun s -> not (Set.isEmpty s)) }

    /// A grace window with no auto-pin; purge whenever anything is eligible.
    let withGrace (grace: TimeSpan) : RetentionPolicy = { immediate with Grace = grace }

module DagRetentionPolicy =

    /// The CONTENT hashes GC-eligible under `policy` at `now`: unreachable from
    /// every GC root (the mechanism's `prunable`), AND — for a rejected proposal
    /// — past the grace window since its `RejectedAt`, AND not auto-pinned.
    /// Content with no `RejectionTombstone` has no grace clock to start, so
    /// reachability + auto-pin alone gate it. Decisions (`RejectionTombstone`s)
    /// are a separate side-store and never appear here — the permanent audit
    /// half of the split.
    let contentGcEligible
        (policy: RetentionPolicy)
        (now: DateTimeOffset)
        (rejections: RejectionTombstone list)
        (allHashes: string seq)
        (getParents: string -> string list)
        (roots: GcRoots)
        : Set<string> =
        let rejectedAt =
            rejections |> List.map (fun r -> r.ProposalHash, r.RejectedAt) |> Map.ofList

        DagRetention.prunable allHashes getParents roots
        |> Set.filter (fun h ->
            not (policy.AutoPin h)
            && (match Map.tryFind h rejectedAt with
                | Some rejAt -> now - rejAt >= policy.Grace
                | None -> true))

    /// The content hashes to tombstone in THIS sweep — `contentGcEligible`
    /// gated by the policy's `PurgeWhen` trigger. Returns the eligible set when
    /// the trigger fires, else empty (the candidates wait for a later sweep).
    /// The caller tombstones each returned hash via the sink's `Tombstone`
    /// method; the matching `RejectionTombstone` decision is retained regardless.
    let purgeBatch
        (policy: RetentionPolicy)
        (now: DateTimeOffset)
        (rejections: RejectionTombstone list)
        (allHashes: string seq)
        (getParents: string -> string list)
        (roots: GcRoots)
        : Set<string> =
        let eligible = contentGcEligible policy now rejections allHashes getParents roots
        if policy.PurgeWhen eligible then eligible else Set.empty
