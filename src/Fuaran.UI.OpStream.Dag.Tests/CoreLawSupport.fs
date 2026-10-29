module Fuaran.UI.OpStream.Dag.Tests.CoreLawSupport

// ============================================================================
//  Phase 1476 — the witnesses the Core conformance kit's multi-writer families
//  are instantiated over in THIS project.
//
//  `Fuaran.UI.Tests/CoreAdoptionTests.fs` already builds a Core `NodeWitness` /
//  `IdWitness` / `OpGen` / `StreamWitness` over the same `Fuaran.UI` types, but
//  test projects cannot reference each other, so the minimal pieces are rebuilt
//  here rather than a second, differently-shaped witness being invented. Where
//  a construction is copied, it is copied verbatim so the two projects certify
//  the SAME witness — a divergence between them would be two tiers, not one.
//
//  Two wrappers carry the whole impedance mismatch, and both exist for the same
//  reason: `Node<'Msg>` embeds message-handler closures in its spec records
//  (`TabsSpec.OnSelect`, `LocalBinding.OnCommit`, ...), so neither `Node<'Msg>`
//  nor `TreeOp<'Msg>` satisfies F#'s `: equality` constraint, and the law
//  families compare trees and ops with `=`.
//
//    * `EqNode` — the wrapper `CoreAdoptionTests` already uses: two trees are
//      equal iff they serialise identically under the canonical wire encoder,
//      the same notion the wire-format round-trip corpus uses.
//    * `EqOp`   — the same trick one level up, needed here and not there
//      because `Conformance.dagLaws`' JSONL round-trip law compares two
//      `Map<string, DagNode<'Op>>` values, which constrains `'Op : equality`.
//
//  `Fuaran.Core` stays equality-agnostic in both cases; the comparison seam is
//  a domain concern.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions

module UiApply = Fuaran.UI.Ops.Apply
module Introspect = Fuaran.UI.Ops.Introspect
module JsonDecode = Fuaran.UI.Ops.JsonDecode
module CoreRng = Fuaran.Core.ConfRng

// ---------------------------------------------------------------------------
//  canonical-encoding equality wrappers
// ---------------------------------------------------------------------------

[<CustomEquality; NoComparison>]
type EqNode =
    { Node: Node<obj> }

    override this.Equals(o: obj) =
        match o with
        | :? EqNode as other -> CanonicalJson.encodeNode this.Node = CanonicalJson.encodeNode other.Node
        | _ -> false

    override this.GetHashCode() =
        (CanonicalJson.encodeNode this.Node).GetHashCode()

[<CustomEquality; NoComparison>]
type EqOp =
    { Op: TreeOp<obj> }

    override this.Equals(o: obj) =
        match o with
        | :? EqOp as other -> CanonicalJson.encodeOp this.Op = CanonicalJson.encodeOp other.Op
        | _ -> false

    override this.GetHashCode() =
        (CanonicalJson.encodeOp this.Op).GetHashCode()

let wrap (n: Node<obj>) : EqNode = { Node = n }
let unwrap (e: EqNode) : Node<obj> = e.Node
let wrapOp (o: TreeOp<obj>) : EqOp = { Op = o }

// ---------------------------------------------------------------------------
//  the Core witnesses over Fuaran.UI's tree
// ---------------------------------------------------------------------------

let idw: Fuaran.Core.IdWitness<NodeId> =
    { ToString = fun (NodeId s) -> s
      OfString = NodeId
      Equals = (=) }

/// A coarse, top-level kind tag — stable under `ReplaceChildren` (a Box stays a Layout),
/// which is all the conformance envelopes / addressing need. Copied from
/// `CoreAdoptionTests.fs`; the two projects must tag identically or they are certifying
/// two different witnesses.
let kindTag (k: NodeKind<obj>) : string =
    match k with
    | NodeKind.Custom _ -> "Custom"
    | NodeKind.ErrorBoundary _ -> "ErrorBoundary"
    | NodeKind.Switch _ -> "Switch"
    | NodeKind.FragmentDecl _ -> "FragmentDecl"
    | NodeKind.FragmentRef _ -> "FragmentRef"
    | NodeKind.Mount _ -> "Mount"
    | k ->
        match Kind.category k with
        | NodeCategory.Layout -> "Layout"
        | NodeCategory.Display -> "Display"
        | NodeCategory.Input -> "Input"
        | NodeCategory.Visualisation -> "Visualisation"
        | NodeCategory.Structural -> "Structural"

let nodew: Fuaran.Core.NodeWitness<EqNode, NodeId> =
    { Id = fun e -> NodeId e.Node.Id
      KindTag = fun e -> kindTag e.Node.Kind
      Children = fun e -> Introspect.getChildren e.Node.Kind |> Option.defaultValue [] |> List.map wrap
      ReplaceChildren =
        fun e cs ->
            match Introspect.withChildren e.Node.Kind (cs |> List.map unwrap) with
            | Some k -> wrap { e.Node with Kind = k }
            | None -> e }

/// A Box holds children; a Markdown leaf does not. `withChildren` is a no-op on a leaf, so
/// `CanHold` routes the kit through `applyContained` — an insert under a leaf is a typed
/// `NotAContainer`, not a silent drop.
let canHold (e: EqNode) =
    Introspect.getChildren e.Node.Kind |> Option.isSome

/// The per-node content encoder the content-hash laws key on (`Tree.encodeHash`). The
/// canonical wire encoding is injective over the node space, which is that function's stated
/// precondition.
let encodeNode (e: EqNode) : string = CanonicalJson.encodeNode e.Node

// ---------------------------------------------------------------------------
//  node builders — data-only, so the canonical encoding is faithful
// ---------------------------------------------------------------------------

let mkBox (id: string) (kids: EqNode list) : EqNode =
    wrap
        { Id = id
          Kind =
            NodeKind.Box(
                { Defaults.box with
                    Children = kids |> List.map unwrap }
            )
          State = None
          Style = None
          Accessibility = Option.None
          Motion = Option.None
          Fallback = None
          ExtraAttributes = Option.None
          Tooltip = None
          Visible = None }

let mkLeaf (id: string) : EqNode =
    wrap
        { Id = id
          Kind = NodeKind.Markdown({ Text = TextSource.Literal "" })
          State = None
          Style = None
          Accessibility = Option.None
          Motion = Option.None
          Fallback = None
          ExtraAttributes = Option.None
          Tooltip = None
          Visible = None }

/// A Metric leaf — the kind every vertical op's corpus fixture (`ops/op-*.json`) targets, so each
/// of the five has a payload this leaf accepts. Decoded from the corpus's own node shape
/// (`nodes/metric-1.json`) rather than assembled field by field.
let mkMetric (id: string) : EqNode =
    let json =
        """{"id":"%ID%","kind":{"$type":"Metric","format":{"$type":"Currency","code":"GBP"},"icon":"trending-up","label":"Revenue","subtext":"vs last month","tone":"Brand","trend":{"$type":"Static","value":0.07},"trendFormat":{"$type":"Percent","decimals":1},"value":{"$type":"Static","value":1234.5}}}"""
            .Replace("%ID%", id)

    match JsonDecode.decodeNodeObj json with
    | Ok n -> wrap n
    | Error e -> failwithf "mkMetric %s: %A" id e

/// Phase 2004 — the five VERTICAL ops, the cases Core's skeleton algebra has no image for, each
/// aimed at `target`. The payloads are the corpus fixtures' (`ops/op-editnode.json`,
/// `op-updateprop.json`, `op-updatestyle.json`, `op-updatestate.json`, `op-replacebinding.json`),
/// decoded through the shipped op decoder, so every op here round-trips through the canonical
/// codec the lane fold content-addresses with. `UpdateState`'s skeleton id is derived from the
/// target so two of them never insert one id twice.
let verticalOps (NodeId target) : (string * TreeOp<obj>) list =
    [ "EditNode", """{"$type":"EditNode","newKind":{"$type":"Markdown","text":"Edited"},"target":"%T%"}"""
      "UpdateProp", """{"$type":"UpdateProp","path":"Label","target":"%T%","value":"Updated revenue"}"""
      "UpdateStyle",
      """{"$type":"UpdateStyle","style":{"emphasis":"Loud","tone":"Success","weight":"Spacious"},"target":"%T%"}"""
      "UpdateState",
      """{"$type":"UpdateState","state":{"onLoading":{"id":"skel-%T%","kind":{"$type":"Skeleton","rows":3}}},"target":"%T%"}"""
      "ReplaceBinding",
      """{"$type":"ReplaceBinding","binding":{"$type":"Static","value":99.5},"slot":"Value","target":"%T%"}""" ]
    |> List.map (fun (name, json) ->
        match JsonDecode.decodeOp (json.Replace("%T%", target)) with
        | Ok op -> name, op
        | Error e -> failwithf "verticalOps %s on %s: %A" name target e)

// ---------------------------------------------------------------------------
//  the skeleton-op generator (footprint / conflict / reconcile / concurrency /
//  arbitration families)
// ---------------------------------------------------------------------------

let private genTree (rng: CoreRng.T) : EqNode * CoreRng.T =
    let mutable counter = 0
    let mutable r = rng

    let freshId () =
        let s = sprintf "n%d" counter
        counter <- counter + 1
        s

    let rec build depth =
        let id = freshId ()
        let leafRoll, r1 = CoreRng.intBelow 2 r
        r <- r1

        if depth <= 0 || leafRoll = 0 then
            mkLeaf id
        else
            let nKids, r2 = CoreRng.intBelow 3 r
            r <- r2
            mkBox id [ for _ in 1..nKids -> build (depth - 1) ]

    let rootId = freshId ()
    let nKids, r2 = CoreRng.intBelow 3 r
    r <- r2
    mkBox rootId [ for _ in 1..nKids -> build 1 ], r

let private genFresh (existing: Set<string>) (rng: CoreRng.T) : EqNode * CoreRng.T =
    let mutable r = rng

    let rec pick () =
        let v, r' = CoreRng.next r
        r <- r'
        let id = sprintf "f%d" (v % 100000)
        if existing.Contains id then pick () else id

    mkLeaf (pick ()), r

let opGen: Fuaran.Core.OpGen<EqNode, NodeId> =
    { Tree = genTree
      FreshNode = genFresh
      CanHold = Some canHold }

// ---------------------------------------------------------------------------
//  the tier's own footprint projection over its own op algebra
// ---------------------------------------------------------------------------
//
//  RE-EXPORTED, not defined (Phase 2292). The projection is shipped:
//  `Fuaran.UI.Ops.TreeOpFootprint.ofOp`, total over `TreeOp`, the structural five
//  through Core's keyed `Ops.footprintKeyed` and a vertical op as Core's
//  `UpdateNode` footprint with its unknown-parent write (Phase 2004). Every law
//  family below that takes a footprint is instantiated with THIS binding, so the
//  confluence, footprint and fold claims are made about the function a consumer
//  of `Fuaran.UI.Ops` actually gets, not about a test copy of it.
//
//  The unknown-parent term costs what Core's update pays: a vertical op is
//  independent only of a structure-free script, every other `TreeOp` writes
//  structure, and so two vertical edits — of different nodes, or of different
//  fields of one node — are reported dependent, ordered rather than merged.
//  The base-aware form (`footprintInOfEqOp`, Phase 2327) is what frees them:
//  read at the tree, a located vertical op needs no unknown-parent write.
//  `CoreDagLawTests` pins the ancestor-removal pair, its erased-term falsifier,
//  and the verdicts the shipped projection gives.

let private emptyFp: Fuaran.Core.Footprint = Fuaran.Core.Footprint.empty

let private unionFp: Fuaran.Core.Footprint -> Fuaran.Core.Footprint -> Fuaran.Core.Footprint =
    Fuaran.Core.Footprint.union

let private skeletonFp (op: Fuaran.Core.SkeletonOp<EqNode, NodeId>) : Fuaran.Core.Footprint =
    Fuaran.Core.Ops.footprint Fuaran.Core.Policy.none (Fuaran.Core.OpScope.plain nodew idw) [ op ]

/// The tier's shipped footprint projection.
let footprintOfTreeOp (op: TreeOp<obj>) : Fuaran.Core.Footprint = Fuaran.UI.Ops.TreeOpFootprint.ofOp op

let footprintOfEqOp (e: EqOp) : Fuaran.Core.Footprint = footprintOfTreeOp e.Op

/// The tier's shipped BASE-AWARE projection (Phase 2327), `TreeOpFootprint.ofOpIn`: the op read at
/// the tree it applies to, its parents resolved through Core's `Ops.footprintIn` and a located
/// vertical op declared at its slot. The projection `Dag.conflictsIn` / `reconcileIn` fold with,
/// and the one `Conformance.footprintInLawsAt` certifies against `footprintOfEqOp`.
let footprintInOfEqOp (state: EqNode) (e: EqOp) : Fuaran.Core.Footprint =
    Fuaran.UI.Ops.TreeOpFootprint.ofOpIn state.Node e.Op

/// Phase 2004's erased-term falsifier: the shipped projection with the vertical arms' unknown-parent
/// write taken out and nothing else changed. No law runs over it except to show that it goes red: a
/// projection the families accept with this term erased would mean they cannot see the pair the
/// term exists for.
let rec footprintErasingVerticalTerm (op: TreeOp<obj>) : Fuaran.Core.Footprint =
    match op with
    | TreeOp.EditNode _
    | TreeOp.UpdateProp _
    | TreeOp.ReplaceBinding _
    | TreeOp.UpdateStyle _
    | TreeOp.PatchStyle _
    | TreeOp.UpdateState _ ->
        { footprintOfTreeOp op with
            UnknownParentWrites = Set.empty }
    | TreeOp.Batch inner ->
        inner
        |> List.fold (fun acc o -> unionFp acc (footprintErasingVerticalTerm o)) emptyFp
    | _ -> footprintOfTreeOp op

/// The same projection lifted to Core's skeleton algebra, by the mapping the tier's own apply
/// engine uses in the other direction (Phase 379): the structural five ARE the same four cases
/// plus `Batch`. Passing this to `Conformance.concurrencyLawsWith` is what puts the tier's
/// function — not Core's — under the confluence law.
let rec private skeletonToTreeOp (op: Fuaran.Core.SkeletonOp<EqNode, NodeId>) : TreeOp<obj> =
    match op with
    | Fuaran.Core.SkeletonOp.InsertChild(parent, child) -> TreeOp.InsertChild(parent, unwrap child)
    | Fuaran.Core.SkeletonOp.RemoveNode target -> TreeOp.RemoveNode target
    | Fuaran.Core.SkeletonOp.MoveNode(target, newParent) -> TreeOp.MoveNode(target, newParent)
    | Fuaran.Core.SkeletonOp.ReorderChildren(parent, order) -> TreeOp.ReorderChildren(parent, order)
    | Fuaran.Core.SkeletonOp.Batch inner -> TreeOp.Batch(inner |> List.map skeletonToTreeOp)
    // Core 0.32.0's in-place op (Phase 1874 raise). It has no image in the tier's algebra: the
    // nearest case, `EditNode`, swaps the node's whole `Kind` — children included — where
    // `UpdateNode` keeps the children the tree holds. Mapping it there would put a different
    // op's footprint under the law, so it is outside this
    // projection's vocabulary. Core's generator draws one from Core 0.33.0 (fuaran-core Phase
    // 297: `genOp` draws every op kind), so `uiFootprintOfSkeleton` below answers it with Core's
    // own footprint rather than reaching here; inside a `Batch` (which Core fills with structural
    // ops only) it still fails by name.
    | Fuaran.Core.SkeletonOp.UpdateNode _ ->
        failwith
            "skeletonToTreeOp: Core's UpdateNode keeps the target's children and the tier's TreeOp has no in-place op that does, so it is outside this projection's vocabulary"

/// The tier's footprint of a Core op script. A top-level `UpdateNode` has no image in the tier's
/// `TreeOp` (see `skeletonToTreeOp`), so it is given Core's own footprint (`skeletonFp`): the
/// tier claims nothing about an op it does not have, and the law stays about the tier's
/// projection over every op it does.
let uiFootprintOfSkeleton (ops: Fuaran.Core.SkeletonOp<EqNode, NodeId> list) : Fuaran.Core.Footprint =
    ops
    |> List.fold
        (fun acc op ->
            match op with
            | Fuaran.Core.SkeletonOp.UpdateNode _ -> unionFp acc (skeletonFp op)
            | _ -> unionFp acc (footprintOfTreeOp (skeletonToTreeOp op)))
        emptyFp

// ---------------------------------------------------------------------------
//  the stream witness (dagLaws / laneFoldLawsAt)
// ---------------------------------------------------------------------------

/// The two-seam `StreamWitness`: `Fuaran.UI.Ops.Apply.apply` as the reducer, and the tier's
/// shipped canonical-JSON op codec as the persistence seam. That codec is the one BOTH DAG
/// ports go through — the Sqlite sink round-trips ops through a host `IOpJsonCodec`, and a
/// host builds that from exactly this pair, while the in-memory sink's export path
/// (`DagWire`) calls it directly.
let coreSw: Fuaran.Core.StreamWitness<EqOp, EqNode, ApplyError> =
    { Apply = fun op e -> UiApply.apply op.Op e.Node |> Result.map wrap
      Encode = fun op -> CanonicalJson.encodeOp op.Op
      Decode = fun s -> JsonDecode.decodeOp s |> Result.map wrapOp |> Result.mapError (sprintf "%A") }

/// The tier's shipped hash — the pure Fable-safe SHA-256 the op-stream's chain actually runs
/// on, supplied host-side per Core GP3 and already certified under `Conformance.hashFnLaws` in
/// `Fuaran.UI.Tests`. The kit defaults to FNV-1a; the families here are run over the real one.
let uiHashFn: Fuaran.Core.HashFn =
    fun prev payload -> Fuaran.Core.Hash.sha256Hex (prev + "|" + payload)

/// The state fingerprint the lane fold compares folded states by.
let hashState (e: EqNode) : string = CanonicalJson.encodeNode e.Node

// ---- the DAG stream generator -------------------------------------------

/// The base tree the DAG stream family and the port leg replay from: a root with three leaf
/// children. The lane families fold from `laneBase` below, which is deeper.
let baseTree = mkBox "root" [ mkLeaf "c0"; mkLeaf "c1"; mkLeaf "c2" ]

let private childIds = [| NodeId "c0"; NodeId "c1"; NodeId "c2" |]

let private tones =
    [| ToneVariant.Brand; ToneVariant.Success; ToneVariant.Critical |]

let styleOp (id: NodeId) (tone: ToneVariant) : TreeOp<obj> =
    TreeOp.UpdateStyle(id, { Defaults.style with Tone = tone })

/// A mix of accepted ops and typed rejections, so the reducer's totality is exercised as well
/// as its happy path.
let genStreamOp (rng: CoreRng.T) : EqOp * CoreRng.T =
    let pick, r1 = CoreRng.intBelow 4 rng

    match pick with
    | 0 ->
        let v, r2 = CoreRng.next r1
        wrapOp (TreeOp.InsertChild(NodeId "root", unwrap (mkLeaf (sprintf "g%d" (v % 50))))), r2
    | 1 ->
        let k, r2 = CoreRng.intBelow 3 r1
        let t, r3 = CoreRng.intBelow 3 r2
        wrapOp (styleOp childIds[k] tones[t]), r3
    | 2 -> wrapOp (TreeOp.RemoveNode(NodeId "c0")), r1
    // a typed rejection: the tree has no such node
    | _ -> wrapOp (TreeOp.RemoveNode(NodeId "ghost")), r1

let streamGen: Fuaran.Core.StreamGen<EqOp, EqNode> =
    { State0 = baseTree; Op = genStreamOp }

// ---- the lane generator --------------------------------------------------

/// The base tree the lane families fold from (Phase 2004): two containers under the root, one
/// holding a further container, so every leaf has a parent a lane can remove and `ab0` has a
/// grandparent too. Over the flat `baseTree` no lane could remove an ANCESTOR of a node another
/// lane edits, so the families certified nothing about the pair Core's Phase 250 found unsound.
let laneBase =
    mkBox
        "root"
        [ mkBox "a" [ mkMetric "a0"; mkBox "ab" [ mkMetric "ab0" ] ]
          mkBox "b" [ mkMetric "b0" ] ]

let private laneLeaves = [| NodeId "a0"; NodeId "ab0"; NodeId "b0" |]

/// Every container: an insert may land under any of them.
let private laneParents = [| NodeId "root"; NodeId "a"; NodeId "ab"; NodeId "b" |]

/// The removable containers — each is an ancestor of at least one leaf.
let private laneAncestors = [| NodeId "a"; NodeId "ab"; NodeId "b" |]

/// One writer's lane off `laneBase`. Four shapes, weighted so that three independently drawn
/// lanes reach BOTH adequacy classes the fold-confluence pack demands, and so that a vertical
/// edit lands beside a removal of its ancestor often enough to be seen:
///
///   * a vertical edit (any of the five) of a leaf, or that edit followed by a restyle of the
///     same leaf — 3 in 8;
///   * an insert of a fresh leaf under any container — 4 in 8. Inserts under DIFFERENT parents
///     are the fold class: every other pair of tier ops writes a parent the other cannot rule
///     out, because a vertical op carries Core's unknown-parent write;
///   * a removal of a container, an ancestor of one or two leaves — 1 in 8.
///
/// Every op applies against the base, so a `LaneRejected` outcome (which counts towards
/// neither class) is rare rather than the common case — a lane set that mostly rejects would
/// starve both coverage guards.
let private genLane (rng: CoreRng.T) : TreeOp<obj> list * CoreRng.T =
    let shape, r1 = CoreRng.intBelow 8 rng
    let k, r2 = CoreRng.intBelow 3 r1
    let e, r3 = CoreRng.intBelow 5 r2

    let edit () =
        verticalOps laneLeaves[k] |> List.item e |> snd

    match shape with
    | 0
    | 1 -> [ edit () ], r3
    | 2 ->
        // two ops on the SAME node: a lane is applied in order, so this stays applyable (a
        // restyle applies whatever the first edit did to the leaf's kind) while still colliding
        // with any other lane that touches that node.
        let t, r4 = CoreRng.intBelow 3 r3
        [ edit (); styleOp laneLeaves[k] tones[t] ], r4
    | 3
    | 4
    | 5
    | 6 ->
        let p, r4 = CoreRng.intBelow 4 r3
        let v, r5 = CoreRng.next r4
        [ TreeOp.InsertChild(laneParents[p], unwrap (mkLeaf (sprintf "l%d" (v % 1000)))) ], r5
    | _ -> [ TreeOp.RemoveNode laneAncestors[k] ], r3

let laneGen: Fuaran.Core.LaneGen<EqOp, EqNode> =
    { State0 = laneBase
      // Sits in the base closure — never applied, only content-addressed.
      BaseOp = wrapOp (TreeOp.Batch [])
      Lanes =
        fun n rng ->
            let mutable r = rng

            let lanes =
                [ for _ in 1..n ->
                      let ops, r' = genLane r
                      r <- r'
                      ops |> List.map wrapOp ]

            lanes, r }

// ---- the footprint generator (footprintLawsAt) ----------------------------

/// One tier op off `laneBase` and whatever the ops before it left (Phase 2292), for
/// `Conformance.footprintLawsAt` and `footprintInLawsAt` (Phase 2327), which thread single draws
/// through the reducer and pair the scripts. Under the base-aware form the vertical draws ARE the
/// newly independent pairs: two edits of one leaf at different slots (a kind edit beside a restyle,
/// a state edit beside a prop edit — six kinds over three leaves draw both), and edits of leaves
/// under different parents. Every op kind the projection has an arm for, weighted towards the two classes its
/// adequacy guard demands: inserts under the four containers (3 in 10), the pairs of which under
/// different parents are the INDEPENDENT class; and vertical edits of a leaf (4 in 10, all six
/// kinds, a field-level restyle included), removals of a container and moves of a leaf (1 in 10
/// each), which interfere with everything non-empty. A reorder of the root closes the set, and a
/// draw the reducer refuses (a removal or move of a node an earlier draw took away) is counted by
/// the kit, not hidden.
let private genFootprintOp (rng: CoreRng.T) : EqOp * CoreRng.T =
    let shape, r1 = CoreRng.intBelow 10 rng
    let k, r2 = CoreRng.intBelow 3 r1
    let e, r3 = CoreRng.intBelow 6 r2

    let op, r =
        match shape with
        | 0
        | 1
        | 2 ->
            let p, r4 = CoreRng.intBelow 4 r3
            let v, r5 = CoreRng.next r4
            TreeOp.InsertChild(laneParents[p], unwrap (mkLeaf (sprintf "f%d" (v % 1000)))), r5
        | 3
        | 4
        | 5
        | 6 ->
            let edit =
                if e = 5 then
                    TreeOp.PatchStyle(
                        laneLeaves[k],
                        { StyleDelta.empty with
                            Tone = Some tones[k] }
                    )
                else
                    verticalOps laneLeaves[k] |> List.item e |> snd

            edit, r3
        | 7 -> TreeOp.RemoveNode laneAncestors[k], r3
        | 8 -> TreeOp.MoveNode(laneLeaves[k], laneParents[e % 4]), r3
        | _ -> TreeOp.ReorderChildren(NodeId "root", [ NodeId "b"; NodeId "a" ]), r3

    wrapOp op, r

let footprintGen: Fuaran.Core.StreamGen<EqOp, EqNode> =
    { State0 = laneBase
      Op = genFootprintOp }

// ---------------------------------------------------------------------------
//  reporting
// ---------------------------------------------------------------------------

/// The kit's verdict shape, rendered the way `CoreAdoptionTests.fs` renders it: every failing
/// law named with its counterexample, so a refutation is a reproducer rather than a symptom.
let assertAllPassed (context: string) (results: Fuaran.Core.LawResult list) =
    let failures = results |> List.filter (fun r -> not r.Passed)

    if not (List.isEmpty failures) then
        failures
        |> List.map (fun r -> sprintf "  %s — %A" r.Law r.Counterexample)
        |> String.concat "\n"
        |> failtestf "%s failed:\n%s" context
