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
                { Layout = LayoutMode.Flex(Orientation.Vertical, false, None)
                  Role = BoxRole.Group
                  Heading = None
                  Children = kids |> List.map unwrap
                  KeepTogether = false
                  BreakBefore = false }
            )
          State = None
          Style = None
          Accessibility = Option.None
          Motion = Option.None
          ExtraAttributes = Option.None
          Tooltip = None }

let mkLeaf (id: string) : EqNode =
    wrap
        { Id = id
          Kind = NodeKind.Markdown({ Text = TextSource.Literal "" })
          State = None
          Style = None
          Accessibility = Option.None
          Motion = Option.None
          ExtraAttributes = Option.None
          Tooltip = None }

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
//  `Fuaran.Core.Ops.footprint` is defined over `SkeletonOp`, the four structural
//  cases. `Fuaran.UI`'s `TreeOp` is wider: the structural five (which the runtime
//  apply already delegates to Core, Phase 379) plus a per-kind vertical
//  (`UpdateProp` / `EditNode` / `UpdateStyle` / `UpdateState` / `ReplaceBinding`)
//  Core never sees. Multi-writer reconciliation needs an address set for EVERY
//  op the tier can emit, so this is the tier's projection: structural ops
//  delegate to Core's, and a vertical op is a content write on the node it
//  rewrites plus a read of it (it authors that node's own content and depends on
//  its existence, but touches no parent's child list).
//
//  It is not asserted — `Conformance.concurrencyLawsWith` is instantiated with it
//  in `CoreDagLawTests.fs`, so the confluence claim is made about THIS function
//  rather than about Core's.

let private emptyFp: Fuaran.Core.Footprint =
    { Reads = Set.empty
      StructureWrites = Set.empty
      ContentWrites = Set.empty
      UnknownParentWrites = Set.empty }

let private unionFp (a: Fuaran.Core.Footprint) (b: Fuaran.Core.Footprint) : Fuaran.Core.Footprint =
    { Reads = Set.union a.Reads b.Reads
      StructureWrites = Set.union a.StructureWrites b.StructureWrites
      ContentWrites = Set.union a.ContentWrites b.ContentWrites
      UnknownParentWrites = Set.union a.UnknownParentWrites b.UnknownParentWrites }

let private skeletonFp (op: Fuaran.Core.SkeletonOp<EqNode, NodeId>) : Fuaran.Core.Footprint =
    Fuaran.Core.Ops.footprint nodew idw [ op ]

/// A vertical op rewrites one node's own content in place.
let private contentWrite (NodeId raw) : Fuaran.Core.Footprint =
    { emptyFp with
        Reads = Set.singleton raw
        ContentWrites = Set.singleton raw }

/// The address set of one `TreeOp`. Total over the DU. `ReplaceRoot` is the one case with no
/// honest finite address set — it rewrites the whole tree, so declaring anything for it would
/// let the reconciler call it independent of an op it certainly interferes with. It is
/// therefore outside this projection's vocabulary and outside every generator here, and
/// reaching it fails loudly rather than returning a footprint that would understate it.
let rec footprintOfTreeOp (op: TreeOp<obj>) : Fuaran.Core.Footprint =
    match op with
    | TreeOp.InsertChild(parent, child) -> skeletonFp (Fuaran.Core.SkeletonOp.InsertChild(parent, wrap child))
    | TreeOp.RemoveNode target -> skeletonFp (Fuaran.Core.SkeletonOp.RemoveNode target)
    | TreeOp.MoveNode(target, newParent) -> skeletonFp (Fuaran.Core.SkeletonOp.MoveNode(target, newParent))
    | TreeOp.ReorderChildren(parent, order) -> skeletonFp (Fuaran.Core.SkeletonOp.ReorderChildren(parent, order))
    | TreeOp.EditNode(id, _)
    | TreeOp.UpdateProp(id, _, _)
    | TreeOp.ReplaceBinding(id, _, _)
    | TreeOp.UpdateStyle(id, _)
    | TreeOp.UpdateState(id, _) -> contentWrite id
    | TreeOp.Batch inner -> inner |> List.fold (fun acc o -> unionFp acc (footprintOfTreeOp o)) emptyFp
    | TreeOp.ReplaceRoot _ ->
        failwith
            "footprintOfTreeOp: ReplaceRoot rewrites the whole tree and has no finite address set — it is outside this projection's vocabulary, and no generator here emits it"

let footprintOfEqOp (e: EqOp) : Fuaran.Core.Footprint = footprintOfTreeOp e.Op

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

let uiFootprintOfSkeleton (ops: Fuaran.Core.SkeletonOp<EqNode, NodeId> list) : Fuaran.Core.Footprint =
    ops
    |> List.fold (fun acc op -> unionFp acc (footprintOfTreeOp (skeletonToTreeOp op))) emptyFp

// ---------------------------------------------------------------------------
//  the stream witness (dagLaws / laneFoldLaws)
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
    fun prev payload -> HashChain.sha256Hex (prev + "|" + payload)

/// The state fingerprint the lane fold compares folded states by.
let hashState (e: EqNode) : string = CanonicalJson.encodeNode e.Node

// ---- the DAG stream generator -------------------------------------------

/// The base tree the DAG / lane families fold from: a root with three leaf children, small
/// enough that independently-generated lanes collide often (which is what makes the halt
/// branch reachable) and disjoint often (which is what makes the fold branch reachable).
let baseTree = mkBox "root" [ mkLeaf "c0"; mkLeaf "c1"; mkLeaf "c2" ]

let private childIds = [| NodeId "c0"; NodeId "c1"; NodeId "c2" |]

let private tones =
    [| ToneVariant.Brand; ToneVariant.Success; ToneVariant.Critical |]

let private styleOp (id: NodeId) (tone: ToneVariant) : TreeOp<obj> =
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

/// One writer's lane off the shared base. Four shapes, chosen so that two independently drawn
/// lanes reach BOTH adequacy classes the fold-confluence pack demands:
///
///   * two lanes styling the SAME child, or removing it, or both appending under `root`, have
///     colliding footprints and must HALT identically under every arrival order;
///   * two lanes touching different children fold cleanly and must FOLD to one state hash.
///
/// Every op applies against the base, so a `LaneRejected` outcome (which counts towards
/// neither class) is rare rather than the common case — a lane set that mostly rejects would
/// starve both coverage guards.
let private genLane (rng: CoreRng.T) : TreeOp<obj> list * CoreRng.T =
    let shape, r1 = CoreRng.intBelow 4 rng
    let k, r2 = CoreRng.intBelow 3 r1
    let t, r3 = CoreRng.intBelow 3 r2

    match shape with
    | 0 -> [ styleOp childIds[k] tones[t] ], r3
    | 1 ->
        // two ops on the SAME node: a lane is applied in order, so this stays applyable while
        // still colliding with any other lane that touches that node.
        let t2, r4 = CoreRng.intBelow 3 r3
        [ styleOp childIds[k] tones[t]; styleOp childIds[k] tones[t2] ], r4
    | 2 ->
        let v, r4 = CoreRng.next r3
        [ TreeOp.InsertChild(NodeId "root", unwrap (mkLeaf (sprintf "l%d" (v % 1000)))) ], r4
    | _ -> [ TreeOp.RemoveNode childIds[k] ], r3

let laneGen: Fuaran.Core.LaneGen<EqOp, EqNode> =
    { State0 = baseTree
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
