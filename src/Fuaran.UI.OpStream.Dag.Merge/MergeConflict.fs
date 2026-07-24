namespace Fuaran.UI.OpStream.Dag.Merge

open Fuaran.UI.Ops.Types

// ============================================================================
//  MergeConflict — the M2 conflict-as-recovery-envelope (Phase 179).
//
//  An M2 merge that cannot auto-merge a cell surfaces a `MergeConflict` rather
//  than failing opaquely. The envelope is **three-up** (base / primary / secondary
//  — the LCA value plus each side's value), carries the secondary side's opaque
//  provenance tag, names whether a primacy pin is held, enumerates the resolution
//  `Choices` (KeepPrimary first when a pin is held), and embeds the existing
//  `ApplyHint` so the consumer's recovery pattern-match applies unchanged.
//
//  Conflict CLASSES map onto the existing `ApplyErrorCode` surface where one
//  fits (delete/modify → NodeNotFound/ParentNotFound; kind-swap-orphans-pin →
//  FieldNotFound; reorder-vs-structural → OrderingMismatch; combined cycle →
//  KindMismatch); only two genuinely-new classes (`ConcurrentEdit`,
//  `ConcurrentMove`) have no existing code. To keep the light/linear path
//  paying nothing (the rung-4 posture), the new classes live HERE in the DAG
//  merge package as a `MergeConflictClass` DU that *maps to* `ApplyErrorCode`,
//  rather than widening the shared `ApplyErrorCode` DU (which would ripple an
//  FS0025 incomplete-match warning across every light-path match site).
// ============================================================================

/// The class of an M2 merge conflict. `ToApplyErrorCode` projects onto the
/// existing apply-error surface where one fits; `ConcurrentEdit` /
/// `ConcurrentMove` are the two genuinely-new classes with no existing code.
[<RequireQualifiedAccess>]
type MergeConflictClass =
    /// Both sides edited the same cell to different values (the canonical case).
    | ConcurrentEdit
    /// Both sides moved the same node to different parents.
    | ConcurrentMove
    /// One side deleted a node the other modified.
    | DeleteModify
    /// An `EditNode` kind swap destroyed a (human-)pinned cell.
    | KindSwapOrphansPin
    /// One side reordered a parent the other structurally changed.
    | ReorderVsStructural
    /// Post-merge whole-tree validation found a combined illegality (e.g. an
    /// A-move + B-move cycle) no single op produced.
    | CombinedCycle

module MergeConflictClass =
    /// Project a conflict class onto the nearest existing `ApplyErrorCode`, so
    /// the AI's recovery pattern-match reuses its known strategies. The two
    /// genuinely-concurrent classes have no existing code and map to
    /// `KindMismatch` as the closest "the ops together are incompatible" code.
    let toApplyErrorCode (c: MergeConflictClass) : ApplyErrorCode =
        match c with
        | MergeConflictClass.DeleteModify -> ApplyErrorCode.NodeNotFound
        | MergeConflictClass.KindSwapOrphansPin -> ApplyErrorCode.FieldNotFound
        | MergeConflictClass.ReorderVsStructural -> ApplyErrorCode.OrderingMismatch
        | MergeConflictClass.CombinedCycle -> ApplyErrorCode.KindMismatch
        | MergeConflictClass.ConcurrentEdit
        | MergeConflictClass.ConcurrentMove -> ApplyErrorCode.KindMismatch

/// The merge-precedence class of a side, as the merge layer sees it — an OPAQUE
/// two-valued tag the layer never decodes from any record field. `Primary` wins
/// a contested cell; `Secondary` carries an opaque host-supplied provenance
/// `tag` the layer round-trips but does not interpret. WHICH records are
/// `Primary` is the host's decision, supplied via the author classifier passed
/// to `merge` — the merge layer derives precedence from nothing on the wire.
[<RequireQualifiedAccess>]
type MergeAuthor =
    | Primary
    | Secondary of tag: string option

/// An enumerated resolution choice for a conflict. `KeepPrimary` is listed first
/// (the default) when a primacy pin is held; the M2 engine applies it under the
/// precedence policy, and an unresolved proposal leaves the trunk unchanged.
[<RequireQualifiedAccess>]
type MergeChoice =
    | KeepPrimary
    | KeepSecondary
    | KeepBase
    /// Re-stamp the primary side's pinned value onto the new kind's name+type-
    /// compatible field (the R1 migration for `KindSwapOrphansPin`).
    | ReassertPinOntoNewKind
    /// Reject the secondary side's kind swap, keeping the old kind + pinned value.
    | KeepOldKind

/// A merge conflict as a recovery envelope. `Cell` is `"<nodeId>:<facet>"`;
/// `Base` / `Primary` / `Secondary` are the canonical-JSON values of the three
/// sides (three-way, never two-way); `PrimacyHeld` marks a primacy-pinned cell;
/// `Choices` is the enumerated resolution menu (KeepPrimary first when pinned);
/// `Hint` embeds the existing `ApplyHint`.
type MergeConflict =
    { NodeId: string
      Facet: string
      Class: MergeConflictClass
      Base: string
      Primary: string option
      Secondary: string option
      SecondaryTag: string option
      PrimacyHeld: bool
      Choices: MergeChoice list
      Hint: ApplyHint }

module MergeConflict =
    /// `"<nodeId>:<facet>"` — the cell key.
    let cell (c: MergeConflict) : string = c.NodeId + ":" + c.Facet
