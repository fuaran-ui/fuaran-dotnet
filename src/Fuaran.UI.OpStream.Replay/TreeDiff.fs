namespace Fuaran.UI.OpStream.Replay

open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.Introspect
open Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  TreeDiff — structural diff over two `Node<'Msg>` snapshots, keyed by
//  NodeId.
//
//  Closes the situational-awareness loop opened by the op-stream
//  (hash-chained truth) and per-op telemetry events.
//  The op stream tells you what was applied; the
//  telemetry tells you what each op cost; the layout observer tells you
//  what rendered. What's missing is what *changed*. TreeDiff is the
//  primitive: given two snapshots, classify every node by NodeId into
//  Added / Removed / Moved / KindChanged / PropChanged / TextChanged.
//
//  Pairs with `Replay.applyTo`: the per-step replay-diff (`stepDiffs`)
//  drives a start snapshot through an OpRecord sequence and surfaces the
//  diff between the tree before and after each applied op. Hash-chain
//  integrity is verified up front (via `HashChain.Verify.chain`) so a
//  forked / corrupt segment surfaces an explicit IntegrityError instead
//  of producing a diff against a broken replay.
//
//  Design choices:
//
//   1. Node identity is `NodeId` — the same key the apply engine uses for
//      structural ops. A node "moved" if its parent or position changed;
//      "added" if its id is new; "removed" if its old id has no successor.
//      No fuzzy matching across renames — by design, since renamed nodes
//      are structurally a Remove + Add per the §4g uniqueness contract.
//
//   2. Shell comparison uses `CanonicalJson.encodeNode` on a children-
//      stripped clone. Two nodes whose shells differ at the JSON level
//      are PropChanged. Closure-bearing slots (`Action`, `Binding.Query`
//      accessors) render as the `"<closure>"` sentinel — two nodes
//      differing only in closure identity are treated as equal. This is
//      the same v1 limitation declared in `CanonicalJson.fs`'s preamble.
//
//   3. `TextChanged` is a narrow specialisation of `PropChanged` for
//      `Heading` / `Markdown` whose only structural change is the
//      `TextSource.Literal` text. It surfaces a `fromText` / `toText`
//      pair so the inspector can show the actual character delta.
//      Anything else with a text change routes through `PropChanged`
//      with the children-stripped JSON shells.
//
//   4. `Moved` and `PropChanged` are NOT mutually exclusive — a node
//      that was both reparented AND had a prop change emits two
//      `NodeChange` entries with the same NodeId. Callers that want a
//      flat "what happened to id X" rollup can group by NodeId.
//
//   5. The integrity verifier (`HashChain.Verify.chain`) is .NET-only
//      (`SHA256` lives behind a `#if !FABLE_COMPILER` guard). The diff
//      module itself is Fable-clean; the `stepDiffs` overload that does
//      NOT take an integrity check is exposed for browser-side consumers
//      that have verified the chain on the server.
//
//  FGP 2: depends on FSharp.Core + Fuaran.UI + Fuaran.UI.Ops +
//  Fuaran.UI.OpStream.Abstractions only. No reference to the
//  orchestration-private tier — the diff is a public-tier capability
//  the eventual OSS spin keeps.
//
//  FGP 5: TreeDiff is a *consumer* of the op stream. It does not emit
//  new op-stream records or telemetry events — the stream stays the
//  source of truth and the diff is a derived view of it.
// ============================================================================

/// Per-node classification. Multiple changes per NodeId are permitted —
/// `Moved` + `PropChanged` is the common compound case for a node that
/// was both reparented and had its content updated within the same op
/// segment.
[<RequireQualifiedAccess>]
type NodeChangeKind =
    /// The node's id was not in the old snapshot. `parent` is `None` when
    /// the added node IS the root of the new snapshot; `position` is its
    /// index in its new parent's `Children` (or `0` for a new root).
    | Added of parent: NodeId option * position: int
    /// The node's id was in the old snapshot but not in the new. `parent`
    /// names the old parent (or `None` if the removed node was the root).
    | Removed of parent: NodeId option
    /// The node's id is present in both snapshots but its `(parent,
    /// position)` differs. Reparented + reindexed both surface as `Moved`.
    | Moved of fromParent: NodeId option * fromPosition: int * toParent: NodeId option * toPosition: int
    /// The node's NodeKind discriminator changed (e.g. Heading → Markdown,
    /// or Layout.Stack → Layout.Grid). Reported via the structural
    /// `kindName` from `Fuaran.UI.Ops.Introspect`. PropChanged on the
    /// same id is suppressed when KindChanged fires — a wholesale kind
    /// swap subsumes per-prop drift.
    | KindChanged of fromKind: string * toKind: string
    /// The node's children-stripped canonical-JSON shell changed but its
    /// NodeKind discriminator stayed the same. `fromJson` / `toJson` are
    /// the shell encodings (closure payloads as `"<closure>"`).
    | PropChanged of fromJson: string * toJson: string
    /// Narrow specialisation: the node is a `Heading` or `Markdown`
    /// whose `Text` literal changed. Reported in addition to the
    /// `PropChanged` shell drift so the inspector can show a character-
    /// level text delta without re-parsing the JSON shell.
    | TextChanged of fromText: string * toText: string

/// One classification result entry. `NodeId` is the addressed node;
/// `Change` is the per-id kind. A single NodeId can appear in multiple
/// entries of one `TreeDiff` (see `NodeChangeKind` notes).
type NodeChange =
    { NodeId: NodeId
      Change: NodeChangeKind }

/// The full diff between two snapshots. Entries are in stable order:
/// Added / Removed / Moved / KindChanged / PropChanged / TextChanged
/// groups concatenated, each group internally sorted by NodeId for
/// determinism (helps the inspector's snapshot tests).
type TreeDiff = { Changes: NodeChange list }

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

module TreeDiff =

    // ─── Internal: tree indexing ─────────────────────────────────────────

    /// Per-node location entry: where the node sits in its tree (parent +
    /// 0-based position) plus the node itself. Root nodes have `Parent =
    /// None` and `Position = 0`.
    type private NodeLocation<'Msg> =
        { Parent: NodeId option
          Position: int
          Node: Node<'Msg> }

    /// Walk a tree producing `(rawIdString, NodeLocation)` pairs. Uses
    /// the unwrapped string id form so the resulting Map keys are
    /// orderable for deterministic Map-merging without an `NodeId`
    /// IComparer.
    let private indexTree<'Msg> (root: Node<'Msg>) : Map<string, NodeLocation<'Msg>> =
        let rec walk
            (acc: Map<string, NodeLocation<'Msg>>)
            (parent: NodeId option)
            (position: int)
            (node: Node<'Msg>)
            : Map<string, NodeLocation<'Msg>> =
            let (NodeId raw) = node.Id

            let acc' =
                acc
                |> Map.add
                    raw
                    { Parent = parent
                      Position = position
                      Node = node }

            match getChildren node.Kind with
            | None -> acc'
            | Some children ->
                children
                |> List.indexed
                |> List.fold (fun a (i, child) -> walk a (Some node.Id) i child) acc'

        walk Map.empty None 0 root

    // ─── Internal: shell encoding ────────────────────────────────────────

    /// Produce a children-stripped clone of `node` so the canonical-JSON
    /// shell encoding compares ONLY this node's own props — drift in
    /// descendants is attributed to those descendants by id, not to
    /// every ancestor. Leaves (kinds with `getChildren = None`) are
    /// passed through unchanged.
    let private stripChildren<'Msg> (node: Node<'Msg>) : Node<'Msg> =
        match withChildren node.Kind [] with
        | Some newKind -> { node with Kind = newKind }
        | None -> node

    /// Canonical-JSON shell of `node` (children stripped). Two nodes
    /// whose shells differ are PropChanged candidates.
    let private encodeShell<'Msg> (node: Node<'Msg>) : string =
        CanonicalJson.encodeNode (stripChildren node)

    // ─── Internal: text-source extraction ────────────────────────────────

    /// Extract the literal-text payload from a `TextSource`. Returns
    /// `None` for `Bound` / `I18n` — those route through PropChanged
    /// since their resolved text isn't known at diff time.
    let private literalText (ts: TextSource) : string option =
        match ts with
        | TextSource.Literal raw -> Some raw
        | TextSource.Bound _ -> None
        | TextSource.I18n(_, _) -> None

    /// When both old and new are Heading / Markdown with `Literal` text,
    /// return the `(fromText, toText)` pair if it changed; else `None`.
    let private textChange<'Msg> (oldNode: Node<'Msg>) (newNode: Node<'Msg>) : (string * string) option =
        let extract (n: Node<'Msg>) : string option =
            match n.Kind with
            | NodeKind.Heading(s) -> literalText s.Text
            | NodeKind.Markdown(s) -> literalText s.Text
            | _ -> None

        match extract oldNode, extract newNode with
        | Some a, Some b when a <> b -> Some(a, b)
        | _ -> None

    // ─── Public surface ──────────────────────────────────────────────────

    /// Compute the structural diff between two `Node<'Msg>` snapshots.
    /// `oldTree` is the pre-state; `nextTree` is the post-state.
    /// Returned `TreeDiff.Changes` is deterministic in entry order:
    /// Added → Removed → Moved → KindChanged → PropChanged →
    /// TextChanged groups, each internally sorted by raw NodeId string.
    let diff<'Msg> (oldTree: Node<'Msg>) (nextTree: Node<'Msg>) : TreeDiff =
        let oldIndex = indexTree oldTree
        let newIndex = indexTree nextTree

        let mutable addedAcc: NodeChange list = []
        let mutable removedAcc: NodeChange list = []
        let mutable movedAcc: NodeChange list = []
        let mutable kindChangedAcc: NodeChange list = []
        let mutable propChangedAcc: NodeChange list = []
        let mutable textChangedAcc: NodeChange list = []

        for KeyValue(rawId, newLoc) in newIndex do
            match Map.tryFind rawId oldIndex with
            | None ->
                addedAcc <-
                    { NodeId = NodeId rawId
                      Change = NodeChangeKind.Added(newLoc.Parent, newLoc.Position) }
                    :: addedAcc
            | Some oldLoc ->
                let oldKindName = kindName oldLoc.Node.Kind
                let newKindName = kindName newLoc.Node.Kind

                if oldKindName <> newKindName then
                    // Wholesale kind swap — suppress per-prop drift; the
                    // shell will differ by construction. Still surface Move
                    // separately if structurally relocated.
                    kindChangedAcc <-
                        { NodeId = NodeId rawId
                          Change = NodeChangeKind.KindChanged(oldKindName, newKindName) }
                        :: kindChangedAcc

                    if oldLoc.Parent <> newLoc.Parent || oldLoc.Position <> newLoc.Position then
                        movedAcc <-
                            { NodeId = NodeId rawId
                              Change =
                                NodeChangeKind.Moved(oldLoc.Parent, oldLoc.Position, newLoc.Parent, newLoc.Position) }
                            :: movedAcc
                else
                    // Same kind discriminator — shell compare for prop drift,
                    // location compare for move, narrow text-change check.
                    let oldShell = encodeShell oldLoc.Node
                    let newShell = encodeShell newLoc.Node

                    if oldShell <> newShell then
                        propChangedAcc <-
                            { NodeId = NodeId rawId
                              Change = NodeChangeKind.PropChanged(oldShell, newShell) }
                            :: propChangedAcc

                        match textChange oldLoc.Node newLoc.Node with
                        | Some(fromText, toText) ->
                            textChangedAcc <-
                                { NodeId = NodeId rawId
                                  Change = NodeChangeKind.TextChanged(fromText, toText) }
                                :: textChangedAcc
                        | None -> ()

                    if oldLoc.Parent <> newLoc.Parent || oldLoc.Position <> newLoc.Position then
                        movedAcc <-
                            { NodeId = NodeId rawId
                              Change =
                                NodeChangeKind.Moved(oldLoc.Parent, oldLoc.Position, newLoc.Parent, newLoc.Position) }
                            :: movedAcc

        for KeyValue(rawId, oldLoc) in oldIndex do
            if not (Map.containsKey rawId newIndex) then
                removedAcc <-
                    { NodeId = NodeId rawId
                      Change = NodeChangeKind.Removed oldLoc.Parent }
                    :: removedAcc

        let sortByRawId (xs: NodeChange list) : NodeChange list =
            xs
            |> List.sortWith (fun a b ->
                let (NodeId ra) = a.NodeId
                let (NodeId rb) = b.NodeId
                System.String.CompareOrdinal(ra, rb))

        let ordered =
            (sortByRawId addedAcc)
            @ (sortByRawId removedAcc)
            @ (sortByRawId movedAcc)
            @ (sortByRawId kindChangedAcc)
            @ (sortByRawId propChangedAcc)
            @ (sortByRawId textChangedAcc)

        { Changes = ordered }

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
