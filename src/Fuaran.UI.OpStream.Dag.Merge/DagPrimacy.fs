namespace Fuaran.UI.OpStream.Dag.Merge

open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Dag.Abstractions

// ============================================================================
//  DagPrimacy — PER-CELL precedence attribution (Phase 179).
//
//  Primacy is "default-deny applied to merge": a cell is primacy-pinned iff the
//  LAST WRITER of that cell on the branch was classified `Primary` by the
//  HOST-SUPPLIED author classifier (`recordAuthor : DagOpRecord -> MergeAuthor`).
//  The merge layer never inspects any record field to decide precedence — the
//  classifier is the only source of a record's `MergeAuthor`. The M1 floor
//  derived authorship from the branch TIP — but a branch whose tip is a
//  `Secondary` op editing cell D mislabels an earlier `Primary` edit to cell C,
//  dropping C's pin. The fix is a backward DAG walk: for each `(nodeId, facet)`
//  cell, find its most-recent writer on the branch and take THAT record's
//  classification.
//
//  `cellsOf` maps an op to the cells it writes (facets match `TreeMerge`'s
//  vocabulary). Ops whose affected parent is not carried in the op itself
//  (`RemoveNode`; `MoveNode`'s OLD parent) under-attribute — the lookup falls
//  back to the per-branch-tip author for any cell the walk did not attribute,
//  so this is a strict refinement of the M1 behaviour, never a regression: a
//  cell the walk attributes is now correct per-cell; a cell it cannot is no
//  worse than the tip-based default it already used.
//
//  Determinism: precedence is read from the classifier + DAG topology only — NO
//  wall-clock anywhere (clocks are confined to leases / retention grace, never
//  the merge-identity path).
// ============================================================================

module DagPrimacy =

    let private rawId (NodeId s) : string = s

    /// The style sub-field cells one `UpdateStyle` establishes a writer for —
    /// EVERY sub-field `TreeMerge` merges independently, because the op carries
    /// a whole `SemanticStyle` record and therefore writes all of them.
    ///
    /// `style.direction` joined this list in Phase 1526, three phases after
    /// Phase 1472 made `direction` an independently-merged sub-field. Until
    /// then a `Primary` edit to a text direction was attributed to whatever the
    /// branch TIP happened to be, so a pin on it was dropped by any later
    /// secondary op — the exact defect the per-cell walk exists to prevent,
    /// reintroduced one facet at a time by growth. The roster test in
    /// `Fuaran.UI.OpStream.Dag.Tests` is what makes the next one fail loudly
    /// instead.
    let private styleCells (i: string) : (string * string) list =
        [ i, "style.tone"
          i, "style.weight"
          i, "style.emphasis"
          i, "style.role"
          i, "style.voice"
          i, "style.direction" ]

    /// The `(nodeId, facet)` cells a single op establishes a writer for. `Batch`
    /// unions its members. See the module header for the under-attribution note.
    let rec cellsOf<'Msg> (op: TreeOp<'Msg>) : (string * string) list =
        match op with
        // EditNode swaps the whole NodeKind — both the kind-own fields and the
        // child structure of the target.
        | TreeOp.EditNode(id, _) -> [ rawId id, "kind"; rawId id, "children" ]
        | TreeOp.UpdateProp(id, _, _) -> [ rawId id, "kind" ]
        | TreeOp.ReplaceBinding(id, _, _) -> [ rawId id, "kind" ]
        | TreeOp.UpdateStyle(id, _) -> styleCells (rawId id)
        | TreeOp.UpdateState(id, _) -> [ rawId id, "state" ]
        // The parent's child list changed, and the inserted node itself is the
        // subject of the `insert` cell — the one a same-id insert on both sides
        // contends (Phase 1497). The op carries the child, so unlike the
        // structural ops below there is nothing to fall back for.
        | TreeOp.InsertChild(parentId, child) -> [ rawId parentId, "children"; child.Id, "insert" ]
        | TreeOp.ReorderChildren(parentId, _) -> [ rawId parentId, "children" ]
        // The destination parent's child list changed; the SOURCE parent's also
        // did, but the op does not carry it — fall back for that cell. The moved
        // node's own `move` cell IS carried (Phase 1526).
        | TreeOp.MoveNode(id, newParentId) -> [ rawId newParentId, "children"; rawId id, "move" ]
        // The affected PARENT is not in the op — fall back to the tip author for
        // its `children` cell. The removed node itself is carried, and it is the
        // subject of the `node` cell a delete/modify contends (Phase 1526).
        | TreeOp.RemoveNode id -> [ rawId id, "node" ]
        // ReplaceRoot returns the supplied node outright, so it writes EVERY
        // facet of the new root — not merely its kind and children. It is the
        // only op that writes `accessibility` or `tooltip` at all, which is the
        // `TreeOpDiff` expressiveness gap seen from the attribution side: a
        // merge of those facets has no op that could have written them, so the
        // merge node that carries it is refused at mint rather than attributed
        // to a writer that does not exist (see `DagMerge.buildMergeRecord`).
        | TreeOp.ReplaceRoot node ->
            [ node.Id, "kind"
              node.Id, "children"
              node.Id, "state"
              node.Id, "accessibility"
              node.Id, "tooltip"
              yield! styleCells node.Id ]
        | TreeOp.Batch ops -> ops |> List.collect cellsOf

    /// Walk back from `head` along the PRIMARY-parent spine, stopping at `stopAt`
    /// (the base hash, exclusive) or genesis, recording for each cell the FIRST
    /// writer encountered — which, walking newest→oldest, is the MOST RECENT
    /// writer. Maps each cell to that record's `MergeAuthor`, as classified by
    /// the host-supplied `recordAuthor` (the merge layer reads no record field).
    let cellAuthors<'Msg>
        (recordAuthor: DagOpRecord<'Msg> -> MergeAuthor)
        (getRec: string -> DagOpRecord<'Msg> option)
        (stopAt: string option)
        (head: string)
        : Map<string * string, MergeAuthor> =
        let rec walk (hash: string) (acc: Map<string * string, MergeAuthor>) =
            match stopAt with
            | Some s when s = hash -> acc
            | _ ->
                match getRec hash with
                | None -> acc
                | Some r ->
                    let author = recordAuthor r

                    let acc' =
                        cellsOf r.Op
                        |> List.fold
                            (fun (m: Map<_, _>) cell -> if Map.containsKey cell m then m else Map.add cell author m)
                            acc

                    match r.Parents with
                    | [] -> acc'
                    | primary :: _ -> walk primary acc'

        walk head Map.empty

    /// A per-cell author lookup for a branch: the most-recent writer of
    /// `(nodeId, facet)` since the base, or `tipAuthor` when the walk did not
    /// attribute the cell (an under-attributed structural op, or a cell the
    /// branch did not touch). The fallback is what makes this a strict refinement.
    let cellAuthorFn<'Msg>
        (recordAuthor: DagOpRecord<'Msg> -> MergeAuthor)
        (getRec: string -> DagOpRecord<'Msg> option)
        (stopAt: string option)
        (head: string)
        (tipAuthor: MergeAuthor)
        : string -> string -> MergeAuthor =
        let map = cellAuthors recordAuthor getRec stopAt head

        fun nodeId facet ->
            match Map.tryFind (nodeId, facet) map with
            | Some a -> a
            | None -> tipAuthor
