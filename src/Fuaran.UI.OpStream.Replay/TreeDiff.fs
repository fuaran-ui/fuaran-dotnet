namespace Fuaran.UI.OpStream.Replay

open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.Introspect
open Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  TreeDiff — the per-id change classification between two `Node<'Msg>`
//  snapshots, keyed by NodeId.
//
//  Closes the situational-awareness loop opened by the op-stream
//  (hash-chained truth) and per-op telemetry events. The op stream tells you
//  what was applied; the telemetry tells you what each op cost; the layout
//  observer tells you what rendered. What's missing is what *changed*.
//
//  **A projection of the one diff (Phase 2304).** The classification is
//  Fuaran.Core's `Diff.changes` over the tier's witnesses and the canonical
//  node encoder — the same witnesses and encoder `TreeOpDiff` hands Core's
//  `Diff.toOpsContainedWith` — and its vocabulary is Core's `Diff.ChangeKind`,
//  with no second definition here. Core holds the two to each other
//  (`Conformance.changeLaws`, its D112): the `Added` ids are the script's
//  insert grafts, the `Moved` ids its `MoveNode` targets, the `KindChanged`
//  and `Changed` ids its content updates, and every reordered parent is
//  `Reordered`. So:
//
//   - `Added` / `Removed` — the id is in one tree only.
//   - `Moved(fromParent, toParent)` — a survivor whose PARENT changed. A
//     change of position under one parent is not a move of the child; it is a
//     `Reordered` entry on the PARENT.
//   - `KindChanged(fromKind, toKind)` — the kind tag changed (reported instead
//     of `Changed`, which it subsumes).
//   - `Changed` — the survivor's own content (its childless canonical shell)
//     changed. Closure-bearing slots render as the `"<closure>"` sentinel, so
//     two nodes differing only in closure identity are equal.
//   - `Reordered` — the children both trees place under this node stand in a
//     different relative order.
//
//  One id may carry several entries (`Moved` and `Changed` together). Entries
//  are in Core's canonical order: ascending id, then the declaration order
//  above. Two snapshots whose roots carry different ids — or a tree carrying
//  an id twice — have no id-addressed diff; `TreeOpDiff` answers them with
//  `ReplaceRoot`, and this classification reads that script: every id of the
//  old tree `Removed`, every id of the new one `Added`.
//
//  Pairs with `Replay.applyTo`: the per-step replay-diff (`stepDiffs`)
//  drives a start snapshot through an OpRecord sequence and surfaces the
//  diff between the tree before and after each applied op. Hash-chain
//  integrity is verified up front (via `HashChain.Verify.chain`) so a
//  forked / corrupt segment surfaces an explicit IntegrityError instead
//  of producing a diff against a broken replay. The integrity verifier is
//  .NET-only; the classification itself is Fable-clean.
//
//  FGP 5: TreeDiff is a *consumer* of the op stream. It does not emit
//  new op-stream records or telemetry events — the stream stays the
//  source of truth and the diff is a derived view of it.
// ============================================================================

/// The classification between two snapshots: Core's `Diff.Change` entries, in
/// Core's canonical order.
type TreeDiff = { Changes: Diff.Change<NodeId> list }

/// One step in a per-step replay-diff. `Record` is the op that was
/// applied; `Diff` is the diff between the tree-before and tree-after
/// for that single op; `TreeAfter` is the post-apply tree (useful for
/// the inspector to render the visible state at that step). Failures
/// during replay surface as `Error` from `stepDiffs`; partial step
/// results before the failure are preserved in `StepDiffsError`.
type StepDiff<'Msg> =
    { Record: OpRecord<'Msg>
      Diff: TreeDiff
      TreeAfter: Node<'Msg> }

/// Errors surfaced from `stepDiffs`. `IntegrityFailed` fires before any
/// step replays — a corrupt / forked chain produces no per-step output.
/// `ReplayFailed` carries the partial successful steps so the inspector
/// can still show what got through before the apply engine balked.
[<RequireQualifiedAccess>]
type StepDiffsError<'Msg> =
    /// The op-stream segment failed `Verify.chain`. The inspector
    /// surfaces this as a top-level banner rather than rendering a
    /// misleading diff against tampered records.
    | IntegrityFailed of VerificationError
    /// The apply engine refused an op mid-segment. `CompletedSteps`
    /// carries the per-step diffs that succeeded before the failure;
    /// `FailedAt` is the offending record's sequence and the wrapped
    /// `ReplayError`.
    | ReplayFailed of completedSteps: StepDiff<'Msg> list * failedAt: int * replayError: ReplayError

/// The childless-shell encoding the classification and `TreeOpDiff` share
/// (Phase 2063): the canonical encoding of a node with its children emptied,
/// which is the content Core's `Diff` compares.
module internal NodeIndex =

    /// Produce a children-stripped clone of `node` so the canonical-JSON
    /// shell encoding compares ONLY this node's own props — drift in
    /// descendants is attributed to those descendants by id, not to
    /// every ancestor. Leaves (kinds with `getChildren = None`) are
    /// passed through unchanged.
    let stripChildren<'Msg> (node: Node<'Msg>) : Node<'Msg> =
        match withChildren node.Kind [] with
        | Some newKind -> { node with Kind = newKind }
        | None -> node

    /// Canonical-JSON shell of `node` (children stripped). Two nodes
    /// whose shells differ differ in their own content.
    let encodeShell<'Msg> (node: Node<'Msg>) : string =
        CanonicalJson.encodeNode (stripChildren node)

    /// `true` for a kind whose children are a FREE ordered list — one it can
    /// hold at any length, empty included. That is every container but
    /// `FragmentDecl`, whose one-element list is its `body`: the structural ops
    /// address it, but no list script reaches a new body, because swapping it
    /// passes through a two-child or a childless state the kind cannot hold.
    let holdsList<'Msg> (node: Node<'Msg>) : bool =
        withChildren node.Kind [] |> Option.isSome

    /// A node's children as the diff reads them: its free list, or none — so a
    /// `FragmentDecl`'s body is part of its own content (its shell) and a
    /// change below it rewrites the declaration whole, as it always has.
    let children<'Msg> (node: Node<'Msg>) : Node<'Msg> list =
        if holdsList node then
            getChildren node.Kind |> Option.defaultValue []
        else
            []

    /// The witness the diff hands Fuaran.Core: the tier's structural witness
    /// with `children` above in place of the raw ordered list.
    let witness<'Msg> : NodeWitness<Node<'Msg>, NodeId> =
        { nodeWitness<'Msg> with
            Children = children }

module TreeDiff =

    /// The reading of a wholesale `ReplaceRoot`: every id of `oldTree`
    /// removed, every id of `nextTree` added, in Core's canonical order.
    let private replaced<'Msg> (oldTree: Node<'Msg>) (nextTree: Node<'Msg>) : TreeDiff =
        let ids (root: Node<'Msg>) =
            Tree.preorder NodeIndex.witness<'Msg> root |> List.map _.Id |> List.distinct

        let rank (k: Diff.ChangeKind<NodeId>) =
            match k with
            | Diff.ChangeKind.Added -> 0
            | _ -> 1

        let entries =
            [ for id in ids nextTree ->
                  ({ Id = NodeId id
                     Kind = Diff.ChangeKind.Added }
                  : Diff.Change<NodeId>)
              for id in ids oldTree ->
                  ({ Id = NodeId id
                     Kind = Diff.ChangeKind.Removed }
                  : Diff.Change<NodeId>) ]

        { Changes =
            entries
            |> List.sortWith (fun x y ->
                let (NodeId rx), (NodeId ry) = x.Id, y.Id

                match System.String.CompareOrdinal(rx, ry) with
                | 0 -> compare (rank x.Kind) (rank y.Kind)
                | c -> c) }

    /// The per-id classification between two `Node<'Msg>` snapshots —
    /// `oldTree` the pre-state, `nextTree` the post-state — as Core's
    /// `Diff.changes` reads it (see the preamble).
    let diff<'Msg> (oldTree: Node<'Msg>) (nextTree: Node<'Msg>) : TreeDiff =
        match Diff.changes CanonicalJson.encodeNode NodeIndex.witness<'Msg> idWitness oldTree nextTree with
        | Ok changes -> { Changes = changes }
        | Error _ -> replaced oldTree nextTree

// ─── Per-step replay-diff ────────────────────────────────────────────

#if !FABLE_COMPILER

    /// Drive `initialTree` through `records` one op at a time. For each
    /// successful apply, emit a `StepDiff` carrying the OpRecord, the
    /// diff between the tree-before and tree-after, and the tree-after.
    /// The hash chain is verified up front via `Verify.chain` — a
    /// corrupt / forked chain surfaces `IntegrityFailed` and no per-step
    /// output is produced. Apply failures surface `ReplayFailed` with
    /// the successful step diffs preserved so the inspector can render
    /// what got through.
    ///
    /// .NET-only (`Verify.chain` lives behind `#if !FABLE_COMPILER`).
    /// Browser-side consumers that have verified the chain server-side
    /// can compose `diff` + `Replay.applyTo` directly to produce the
    /// same per-step shape without the integrity preflight.
    let stepDiffs<'Msg>
        (initialTree: Node<'Msg>)
        (records: OpRecord<'Msg> seq)
        : Result<StepDiff<'Msg> list, StepDiffsError<'Msg>> =
        let recordList = records |> List.ofSeq

        match Verify.chain recordList with
        | Error verr -> Error(StepDiffsError.IntegrityFailed verr)
        | Ok() ->
            let mutable tree: Node<'Msg> = initialTree
            let mutable stepsAcc: StepDiff<'Msg> list = []
            let mutable failure: StepDiffsError<'Msg> option = None
            let mutable stop = false

            use enumerator = (recordList :> OpRecord<'Msg> seq).GetEnumerator()

            while not stop && enumerator.MoveNext() do
                let record = enumerator.Current

                match Apply.apply record.Op tree with
                | Ok updated ->
                    let stepDiff = diff tree updated

                    stepsAcc <-
                        { Record = record
                          Diff = stepDiff
                          TreeAfter = updated }
                        :: stepsAcc

                    tree <- updated
                | Error applyError ->
                    failure <-
                        Some(
                            StepDiffsError.ReplayFailed(
                                List.rev stepsAcc,
                                record.Sequence,
                                ReplayError.ApplyFailed(record.Sequence, applyError)
                            )
                        )

                    stop <- true

            match failure with
            | Some err -> Error err
            | None -> Ok(List.rev stepsAcc)

#endif
