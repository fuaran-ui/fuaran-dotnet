namespace Fuaran.UI.OpStream.Dag.Merge

open System.Text
open Fuaran.UI.Ops.Types

// ============================================================================
//  MergeConflict — the M2 conflict-as-recovery-envelope (Phase 179).
//
//  An M2 merge that cannot auto-merge a cell surfaces a `MergeConflict` rather
//  than failing opaquely. Since Phase 1497 the envelope is **two-sided**: the LCA
//  value plus BOTH branches' values (`A` / `B`, each with that branch's own
//  opaque provenance tag), populated on every two-sided refusal — with
//  `Primary` / `Secondary` / `SecondaryTag` the precedence view on top, populated
//  exactly when a primacy pin is held. It also enumerates the resolution
//  `Choices` (KeepPrimary first when a pin is held) and embeds the existing
//  `ApplyHint` so the consumer's recovery pattern-match applies unchanged.
//
//  Until 1497 the envelope was described as three-up but populated only ONE
//  branch's value when no pin was held, chosen by argument position — so two
//  replicas refusing the same merge in opposite orders disagreed about what the
//  other side had wanted. `encodeEnvelope` below is the byte-stable rendering
//  that makes a refusal a cross-host artefact, as `ValidatorGate.encodeVerdict`
//  is for a gated one.
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
///
/// **Every offered choice names a POPULATED slot of the envelope it is offered
/// in.** `KeepPrimary` / `KeepSecondary` name the precedence view, which is
/// populated exactly when `PrimacyHeld` is `true`; `KeepA` / `KeepB` name the
/// sides view, populated on every two-sided refusal. Offering `KeepSecondary`
/// under a plain (unpinned) `merge3Way` refusal — which the menu did until this
/// change — named a slot that is `None` there, so a resolver applying it had
/// nothing to keep or had to pick a side itself: the argument-order dependence
/// Phase 1497 removed from the envelope, coming back through the resolver.
[<RequireQualifiedAccess>]
type MergeChoice =
    | KeepPrimary
    | KeepSecondary
    | KeepBase
    /// Keep the FIRST-argument branch's value — the envelope's `A` side. The
    /// side-addressed keep for a refusal with no primacy pin, where `Primary` /
    /// `Secondary` are both `None` and the two values live in `A` / `B` alone.
    | KeepA
    /// Keep the SECOND-argument branch's value — the envelope's `B` side.
    | KeepB
    /// Re-stamp the primary side's pinned value onto the new kind's name+type-
    /// compatible field (the R1 migration for `KindSwapOrphansPin`).
    | ReassertPinOntoNewKind
    /// Reject the secondary side's kind swap, keeping the old kind + pinned value.
    | KeepOldKind

/// One SIDE of a two-sided refusal: the branch's value for the contended cell,
/// plus that branch's own opaque provenance tag (Phase 1497).
///
/// The tag is per-side because `SecondaryTag` cannot be: it names the tag of
/// the side that lost to a pin, so with no pin held there is no such side, and
/// populating it from the A-side branch — which is what the layer did until
/// 1497 — made the envelope depend on the order the caller passed its branches.
type MergeSide = { Value: string; Tag: string option }

/// A merge conflict as a recovery envelope. `Cell` is `"<nodeId>:<facet>"`.
///
/// **Two views of the same refusal, and they answer different questions.**
///
/// * `A` / `B` are the SIDES view (Phase 1497): the first- and second-argument
///   branches' values for the contended cell, populated on EVERY two-sided
///   refusal whether or not a pin is held. This is what a host needs in order to
///   show a human what each side wanted, and what a second replica merging the
///   same pair in the opposite order must agree with — swapping the branches
///   TRANSPOSES `A` and `B` and changes nothing else in the envelope.
/// * `Base` / `Primary` / `Secondary` are the PRECEDENCE view: the LCA value,
///   the pinned winner and the side that lost to it. `Primary` and `Secondary`
///   are populated exactly when `PrimacyHeld` is `true` — a value in either slot
///   IS a precedence claim, so with two `Secondary` sides (the `merge3Way`
///   shape) both are `None` and the values live in `A` / `B` alone. Before 1497
///   `Secondary` was populated in that case from whichever branch arrived first,
///   which read as a precedence claim that no pin supported.
///
/// `Choices` is the enumerated resolution menu — `KeepPrimary` first when
/// pinned, and `KeepA` / `KeepB` (never `KeepSecondary`) when not, so that every
/// offered choice names a slot this envelope actually populated. `Hint` embeds
/// the existing `ApplyHint`.
type MergeConflict =
    { NodeId: string
      Facet: string
      Class: MergeConflictClass
      Base: string
      A: MergeSide option
      B: MergeSide option
      Primary: string option
      Secondary: string option
      SecondaryTag: string option
      PrimacyHeld: bool
      Choices: MergeChoice list
      Hint: ApplyHint }

module MergeConflict =
    /// `"<nodeId>:<facet>"` — the cell key.
    let cell (c: MergeConflict) : string = c.NodeId + ":" + c.Facet

    /// Host-independent ordering key for a refusal — `(NodeId, Facet)` is unique
    /// within one merge (a facet of a node is merged once), so this totally
    /// orders an envelope regardless of the fold's internal emission order.
    let private orderKey (c: MergeConflict) = c.NodeId, c.Facet

    /// Order a refusal set deterministically.
    let sortCanonical (conflicts: MergeConflict list) : MergeConflict list = conflicts |> List.sortBy orderKey

    let private classString (c: MergeConflictClass) : string =
        match c with
        | MergeConflictClass.ConcurrentEdit -> "ConcurrentEdit"
        | MergeConflictClass.ConcurrentMove -> "ConcurrentMove"
        | MergeConflictClass.DeleteModify -> "DeleteModify"
        | MergeConflictClass.KindSwapOrphansPin -> "KindSwapOrphansPin"
        | MergeConflictClass.ReorderVsStructural -> "ReorderVsStructural"
        | MergeConflictClass.CombinedCycle -> "CombinedCycle"

    /// Mirror of `CanonicalJson`'s string escape, kept local for the same reason
    /// `ValidatorGate` keeps its own: the merge package takes no extra dependency
    /// for a codec.
    let private appendEscaped (sb: StringBuilder) (s: string) : unit =
        sb.Append '"' |> ignore

        for ch in s do
            match ch with
            | '"' -> sb.Append "\\\"" |> ignore
            | '\\' -> sb.Append "\\\\" |> ignore
            | c when c < ' ' -> sb.Append(sprintf "\\u%04x" (int c)) |> ignore
            | c -> sb.Append c |> ignore

        sb.Append '"' |> ignore

    let private appendSide (sb: StringBuilder) (side: MergeSide option) : unit =
        match side with
        | None -> sb.Append "null" |> ignore
        | Some s ->
            sb.Append "{\"tag\":" |> ignore

            match s.Tag with
            | None -> sb.Append "null" |> ignore
            | Some t -> appendEscaped sb t

            sb.Append ",\"value\":" |> ignore
            appendEscaped sb s.Value
            sb.Append '}' |> ignore

    /// Canonical JSON of a REFUSAL envelope: the conflict set as a sorted array of
    /// `{a,b,base,class,facet,nodeId,primacyHeld}` objects (object keys
    /// alphabetical, array entries in `(NodeId, Facet)` order). Byte-stable across
    /// hosts, so `HashChain.sha256Hex` over it is the cross-host refusal hash —
    /// the determinism artifact for a REFUSED structural merge, the analogue of
    /// the outcome hash for an auto-merge and of `ValidatorGate.encodeVerdict` for
    /// a gated one.
    ///
    /// The precedence view is deliberately projected as `primacyHeld` alone rather
    /// than as the `Primary` / `Secondary` strings: those are derivable from the
    /// sides plus the pin, and a corpus that committed both would pin the same
    /// value twice and go red on a host that agreed about the merge.
    let encodeEnvelope (conflicts: MergeConflict list) : string =
        let sb = StringBuilder()
        sb.Append '[' |> ignore

        conflicts
        |> sortCanonical
        |> List.iteri (fun i c ->
            if i > 0 then
                sb.Append ',' |> ignore

            sb.Append "{\"a\":" |> ignore
            appendSide sb c.A
            sb.Append ",\"b\":" |> ignore
            appendSide sb c.B
            sb.Append ",\"base\":" |> ignore
            appendEscaped sb c.Base
            sb.Append ",\"class\":" |> ignore
            appendEscaped sb (classString c.Class)
            sb.Append ",\"facet\":" |> ignore
            appendEscaped sb c.Facet
            sb.Append ",\"nodeId\":" |> ignore
            appendEscaped sb c.NodeId
            sb.Append ",\"primacyHeld\":" |> ignore
            sb.Append(if c.PrimacyHeld then "true" else "false") |> ignore
            sb.Append '}' |> ignore)

        sb.Append ']' |> ignore
        sb.ToString()
