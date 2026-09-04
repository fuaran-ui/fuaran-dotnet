module FableLaws.TestSupport

// ============================================================================
//  Phase 1488 — the witnesses the law harness runs on, in a FABLE-COMPILABLE
//  project.
//
//  `Fuaran.UI.OpStream.Dag.Tests/CoreLawSupport.fs` (Phase 1476) already builds
//  exactly these witnesses for the .NET run, and `Fuaran.UI.Tests/CoreAdoptionTests.fs`
//  built the first of them before that. Test projects cannot reference each
//  other, so the minimal pieces are COPIED here rather than a third,
//  differently-shaped witness being invented — copied verbatim where the
//  construction is shared, so the .NET and Fable ports certify the SAME witness.
//  A divergence between them would be two tiers, not one port of one tier.
//
//  What is deliberately NOT copied: `CoreLawSupport.assertAllPassed`, which
//  raises through Expecto. Expecto does not transpile, so the verdict shape here
//  is a value (`renderResults`) that both pipelines print identically and the
//  runner compares.
//
//  Two wrappers carry the whole impedance mismatch, for the reason 1476 records:
//  `Node<'Msg>` embeds message-handler closures in its spec records, so neither
//  `Node<'Msg>` nor `TreeOp<'Msg>` satisfies F#'s `: equality` constraint, and the
//  law families compare trees and ops with `=`. Equality is therefore the
//  canonical wire encoding — the same notion the round-trip corpus uses.
// ============================================================================

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

    // `hash`, not `.GetHashCode()`: Fable does not support `System.String.GetHashCode`, and this
    // file is compiled to JavaScript. The two ports need consistent hashing WITHIN a run, never
    // equal hashes ACROSS runtimes — nothing printed here is derived from a hash, and every
    // `List.distinct` in the path preserves first-occurrence order.
    override this.GetHashCode() =
        hash (CanonicalJson.encodeNode this.Node)

[<CustomEquality; NoComparison>]
type EqOp =
    { Op: TreeOp<obj> }

    override this.Equals(o: obj) =
        match o with
        | :? EqOp as other -> CanonicalJson.encodeOp this.Op = CanonicalJson.encodeOp other.Op
        | _ -> false

    override this.GetHashCode() = hash (CanonicalJson.encodeOp this.Op)

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

/// A coarse, top-level kind tag — stable under `ReplaceChildren`. Copied from
/// `CoreLawSupport.kindTag`; the ports must tag identically or they are certifying two
/// different witnesses.
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

/// The per-node content encoder the content-hash laws key on. The canonical wire encoding is
/// injective over the node space, which is that function's stated precondition.
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

/// A leaf carrying CONTENT (Phase 1497). Two branches that insert the same id
/// must be distinguishable by what they inserted, or the same-id-different-content
/// refusal the shared-children guard reaches can never be sampled — the generator
/// would only ever produce agreeing inserts and the law would certify one branch
/// of the new code.
let mkLeafText (id: string) (text: string) : EqNode =
    wrap
        { Id = id
          Kind = NodeKind.Markdown({ Text = TextSource.Literal text })
          State = None
          Style = None
          Accessibility = Option.None
          Motion = Option.None
          ExtraAttributes = Option.None
          Tooltip = None }

let mkLeaf (id: string) : EqNode = mkLeafText id ""

// ---------------------------------------------------------------------------
//  the tier's own footprint projection over its own op algebra
// ---------------------------------------------------------------------------
//
//  Copied from `CoreLawSupport.footprintOfTreeOp` — the address set of one `TreeOp`, total over
//  the DU: the structural five delegate to `Fuaran.Core.Ops.footprint`, and a vertical op is a
//  content write on the node it rewrites plus a read of it.
//
//  It is not asserted directly. `FoldConfluence.laneFoldLaws` is instantiated with it below, so
//  the fold-confluence claim is made about THIS function.

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

/// `ReplaceRoot` is the one case with no honest finite address set — it rewrites the whole tree,
/// so declaring anything for it would let the reconciler call it independent of an op it certainly
/// interferes with. Outside this projection's vocabulary and outside every generator here.
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

// ---------------------------------------------------------------------------
//  the stream witness (laneFoldLaws)
// ---------------------------------------------------------------------------

/// The two-seam `StreamWitness`: `Fuaran.UI.Ops.Apply.apply` as the reducer, and the tier's
/// shipped canonical-JSON op codec as the persistence seam.
let coreSw: Fuaran.Core.StreamWitness<EqOp, EqNode, ApplyError> =
    { Apply = fun op e -> UiApply.apply op.Op e.Node |> Result.map wrap
      Encode = fun op -> CanonicalJson.encodeOp op.Op
      Decode =
        fun s ->
            JsonDecode.decodeOp s
            |> Result.map wrapOp
            |> Result.mapError (fun e -> e.Message) }

/// The tier's shipped hash — the pure Fable-safe SHA-256 the op-stream's chain runs on.
let uiHashFn: Fuaran.Core.HashFn =
    fun prev payload -> HashChain.sha256Hex (prev + "|" + payload)

/// The state fingerprint the lane fold compares folded states by.
let hashState (e: EqNode) : string = CanonicalJson.encodeNode e.Node

// ---------------------------------------------------------------------------
//  a Fable-reproducible pseudo-random source
// ---------------------------------------------------------------------------
//
//  THE KIT'S OWN GENERATOR CANNOT BE USED HERE, and finding that out is one of the things this
//  harness did. `Fuaran.Core.ConfRng` is a 32-bit LCG — `state * 1664525u + 1013904223u` — and a
//  32-bit unsigned multiply does not survive Fable: JavaScript multiplies as a double, so the
//  product loses precision and the state collapses. Measured at the pinned 0.18.0, seed 1488:
//
//      .NET   next1=1547650046  next2=642982293  next3=2003517255 ...
//      Fable  next1=1547650048  next2=0          next3=0          ...
//
//  Every draw after the first is 0 under Fable, so every family that draws from `ConfRng`
//  degenerates to one repeated sample. `ConfRng.intBelow`'s own comment states the constraint the
//  kit holds itself to — "no 32-bit multiply, which does not wrap identically on both pipelines,
//  so the kit stays value-identical under Fable". `intBelow` honours it; `next`, one function
//  above it, does not. Reported upstream; Core is not this repo's to change.
//
//  `LaneGen.Lanes` is the DOMAIN's generator — the kit hands it an rng as a convenience, not as a
//  requirement — so the tier supplies its own source and threads the kit's rng straight back
//  untouched. That is inside the kit's contract (at three lanes the family draws from the rng
//  nowhere else: 3! = 6 is under `permutationBound`, so arrival orders are enumerated rather than
//  sampled), and it is what keeps the Fable port of `laneFoldLaws` non-vacuous instead of
//  certifying one lane set a hundred times.
//
//  xorshift32: shifts and XOR only. `<<<` on `int` is JavaScript's `<<` (exactly 32-bit) and
//  `>>>` on `uint32` is JavaScript's `>>>`, so every step is bit-exact on both pipelines — which
//  the harness PROVES rather than assumes, by comparing the two runs' output line by line.

[<RequireQualifiedAccess>]
module PortableRng =

    let mutable private state = 1

    /// Fixed per law, so a refutation names a run anyone can reproduce on either pipeline. Zero is
    /// the one state xorshift can never leave, so it is mapped away.
    let reseed (s: int) = state <- (if s = 0 then 1 else s)

    /// A non-negative int.
    let next () : int =
        let mutable x = state
        x <- x ^^^ (x <<< 13)
        x <- x ^^^ int ((uint32 x) >>> 17)
        x <- x ^^^ (x <<< 5)
        state <- x
        x &&& 0x7FFFFFFF

    /// A value in `[0, n)` (0 when `n <= 0`). Modulo is safe here in a way it is not on an LCG:
    /// xorshift32's low bits carry the generator's full period, which is the property Core's
    /// `intBelow` had to reject modulo to obtain.
    let below (n: int) : int = if n <= 0 then 0 else next () % n

// ---------------------------------------------------------------------------
//  the shared base tree + generators
// ---------------------------------------------------------------------------

/// Small enough that independently-generated lanes collide often (which is what makes the halt
/// branch reachable) and are disjoint often (which is what makes the fold branch reachable).
let baseTree = mkBox "root" [ mkLeaf "c0"; mkLeaf "c1"; mkLeaf "c2" ]

let childIds = [| NodeId "c0"; NodeId "c1"; NodeId "c2" |]

let tones = [| ToneVariant.Brand; ToneVariant.Success; ToneVariant.Critical |]

let styleOp (id: NodeId) (tone: ToneVariant) : TreeOp<obj> =
    TreeOp.UpdateStyle(id, { Defaults.style with Tone = tone })

/// One writer's lane off the shared base — the four shapes of `CoreLawSupport.genLane`, drawn from
/// the portable source above rather than the kit's. Chosen so that two independently drawn lanes
/// reach BOTH adequacy classes the fold-confluence pack demands: lanes that touch the same child
/// (or both append under `root`) must HALT identically under every arrival order, and lanes that
/// touch different children must FOLD to one state hash.
let private genLane () : TreeOp<obj> list =
    let shape = PortableRng.below 4
    let k = PortableRng.below 3
    let t = PortableRng.below 3

    match shape with
    | 0 -> [ styleOp childIds[k] tones[t] ]
    | 1 ->
        // Two ops on the SAME node: a lane applies in order, so this stays applyable while still
        // colliding with any other lane that touches that node.
        let t2 = PortableRng.below 3
        [ styleOp childIds[k] tones[t]; styleOp childIds[k] tones[t2] ]
    | 2 -> [ TreeOp.InsertChild(NodeId "root", unwrap (mkLeaf ("l" + string (PortableRng.below 1000)))) ]
    | _ -> [ TreeOp.RemoveNode childIds[k] ]

let laneGen: Fuaran.Core.LaneGen<EqOp, EqNode> =
    { State0 = baseTree
      // Sits in the base closure — never applied, only content-addressed.
      BaseOp = wrapOp (TreeOp.Batch [])
      Lanes =
        fun n rng ->
            let lanes = [ for _ in 1..n -> genLane () |> List.map wrapOp ]

            // The kit's rng is handed straight back, unadvanced: see the block above `PortableRng`.
            lanes, rng }

// ---------------------------------------------------------------------------
//  reporting — the Expecto-free half of `CoreLawSupport.assertAllPassed`
// ---------------------------------------------------------------------------

/// Newlines, tabs and stray control characters are folded to spaces and the result is capped,
/// because every line this harness prints is compared BETWEEN PIPELINES line by line. A
/// counterexample that spanned two lines on one side and three on the other would be reported as
/// a divergence in the algebra when it is a divergence in the formatter.
let sanitise (s: string) : string =
    let cleaned =
        s
        |> Seq.map (fun c -> if int c < 32 || int c > 126 then ' ' else c)
        |> Seq.toArray
        |> System.String

    if cleaned.Length > 160 then
        cleaned.Substring(0, 160)
    else
        cleaned

/// One line per family, plus one per failing law. Counts only — the same discipline
/// `tests/pure-tier-laws` states: nothing echoes tree CONTENT, because the two runtimes do not
/// agree about writing arbitrary text to a terminal and a probe that reported console encoding as
/// an algebra divergence would be worse than no probe.
let renderResults (family: string) (results: Fuaran.Core.LawResult list) : string list =
    let failures = results |> List.filter (fun r -> not r.Passed)

    let header =
        "KIT "
        + family
        + " laws="
        + string (List.length results)
        + " failed="
        + string (List.length failures)

    header
    :: (failures
        |> List.map (fun r ->
            "KITFAIL "
            + family
            + " "
            + r.Law
            + " "
            + sanitise (defaultArg r.Counterexample "(none)")))
