module FableLaws.Laws

// ============================================================================
//  Phase 1488 — the laws the Fable harness certifies.
//
//  TWO, and they are different kinds of claim.
//
//  1. THE BROWSER MERGE. `TreeMerge.merge3Way` is the tier's structural three-way
//     merge, and it ships in a package that transpiles: it runs in a browser, on
//     the JavaScript the client tier is compiled to. Its order-independence — the
//     property the NodeId-canonical-byte tie-break exists to give it — was
//     demonstrated by two examples in the .NET `MergeTests.fs` and certified
//     nowhere, on either pipeline. Here it is a law over GENERATED three-way
//     edits, run on both.
//
//     Order-independence is not a nicety of the merge; it is the whole promise.
//     Two replicas that merge the same pair of branches in the opposite order
//     must reach the same tree, or they have diverged while both believing they
//     converged — and, because the merge node commits to the canonical encoding
//     of the resulting tree, they would then also disagree about the outcome hash
//     they each recorded as proof that they agreed.
//
//  2. THE PINNED KIT, UNDER FABLE. `FoldConfluence.laneFoldLaws` is run over this
//     tier's own reducer, op codec and footprint projection — the same
//     instantiation Phase 1476 certified on .NET (`CoreDagLawTests.fs`), through
//     the same witnesses (`TestSupport.fs` copies them verbatim). What the second
//     port adds is not a second opinion about Core: the kit is written to be
//     value-identical under Fable (see `ConfRng.intBelow`'s comment on why it uses
//     no 32-bit multiply), and this is the first thing in this repo that CHECKS
//     that claim over a real consumer witness rather than trusting it.
//
//  Everything printed is COUNTS and fixed vocabulary. The runner beside this file
//  executes the same program on .NET and under Node and compares the two outputs
//  line by line, so a divergence that leaves both pipelines internally lawful
//  still shows up. A law-only probe would report two disagreeing pipelines as two
//  green runs — the lesson `tests/pure-tier-laws` records, applying unchanged.
// ============================================================================

open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Merge
open FableLaws.TestSupport

module FoldConfluence = Fuaran.Core.FoldConfluence
module UiApply = Fuaran.UI.Ops.Apply

// ---------------------------------------------------------------------------
//  the merge outcome, canonicalised so two arrival orders are comparable
// ---------------------------------------------------------------------------

/// Spelled out rather than derived from `%A` or `string`: the conflict class is part of the
/// compared line, and a runtime's rendering of a DU case is not a contract either pipeline owes
/// the other. A new class fails to compile here, which is the right place to notice it.
let private classString (c: MergeConflictClass) : string =
    match c with
    | MergeConflictClass.ConcurrentEdit -> "ConcurrentEdit"
    | MergeConflictClass.ConcurrentMove -> "ConcurrentMove"
    | MergeConflictClass.DeleteModify -> "DeleteModify"
    | MergeConflictClass.KindSwapOrphansPin -> "KindSwapOrphansPin"
    | MergeConflictClass.ReorderVsStructural -> "ReorderVsStructural"
    | MergeConflictClass.CombinedCycle -> "CombinedCycle"

/// The CONTENDED CELLS of a refusal — `nodeId|facet|class`, deduplicated and sorted. This is the
/// part of a refusal that IS arrival-order-independent, and it is the same part the .NET
/// `MergeTests.fs` asserts ("contended (left, style.tone) cell named").
let private conflictCells (conflicts: MergeConflict list) : string =
    conflicts
    |> List.map (fun c -> c.NodeId + "|" + c.Facet + "|" + classString c.Class)
    |> List.distinct
    |> List.sort
    |> String.concat ";"

/// The refusal's recorded VALUES, canonicalised as far as they can be: swapping the branches
/// swaps which side is reported `Primary` and which `Secondary`, so the pair is compared
/// UNORDERED — the canonicalisation Core's `LaneHalted` performs, for the same reason (comparing
/// raw reports would fail every trial for a reason that is presentation, not divergence).
///
/// It is measured SEPARATELY from the cell set above, and its divergences are counted rather than
/// refuted, because on this engine it genuinely is order-dependent — see `ValueAsymmetries`.
let private conflictValues (conflicts: MergeConflict list) : string =
    conflicts
    |> List.map (fun c ->
        let p = defaultArg c.Primary "-"
        let s = defaultArg c.Secondary "-"
        let lo = if p <= s then p else s
        let hi = if p <= s then s else p

        c.NodeId
        + "|"
        + c.Facet
        + "|"
        + classString c.Class
        + "|"
        + c.Base
        + "|"
        + lo
        + "|"
        + hi)
    |> List.distinct
    |> List.sort
    |> String.concat ";"

type private Outcome =
    | Merged of canonical: string
    | Refused of cells: string * values: string

let private mergeOutcome (b: Node<obj>) (x: Node<obj>) (y: Node<obj>) : Outcome =
    match TreeMerge.merge3Way b x y with
    | Ok tree -> Merged(CanonicalJson.encodeNode tree)
    | Error conflicts -> Refused(conflictCells conflicts, conflictValues conflicts)

// ---------------------------------------------------------------------------
//  the branch generator
// ---------------------------------------------------------------------------
//
//  Both adequacy classes must be reachable or the law is a statement about a constant:
//
//    * DISJOINT edits (different children, or two inserts of DIFFERENT fresh ids under the same
//      parent) must auto-merge — and the second of those is the case the NodeId-byte tie-break
//      decides, so it is the case a perturbed tie-break reddens;
//    * COLLIDING edits (the same child restyled to two different tones, a remove against a
//      restyle) must refuse — identically under both orders.
//
//  The fresh-id pool is deliberately tiny (six ids), so two independently drawn branches insert
//  the same id often enough for that case to be sampled too.

let private freshIds = [| "i0"; "i1"; "i2"; "i3"; "i4"; "i5" |]

let private genEdit () : TreeOp<obj> list =
    let shape = PortableRng.below 4
    let k = PortableRng.below 3
    let t = PortableRng.below 3
    let f = PortableRng.below 6

    match shape with
    | 0 -> [ TreeOp.InsertChild(NodeId "root", unwrap (mkLeaf freshIds[f])) ]
    | 1 -> [ styleOp childIds[k] tones[t] ]
    | 2 -> [ TreeOp.RemoveNode childIds[k] ]
    | _ ->
        [ styleOp childIds[k] tones[t]
          TreeOp.InsertChild(NodeId "root", unwrap (mkLeaf freshIds[f])) ]

/// Apply an edit script to the base. `None` when the script does not apply at all — counted and
/// skipped rather than silently treated as "no edit", which would inflate the fast-forward cases.
let private applyEdit (ops: TreeOp<obj> list) (start: Node<obj>) : Node<obj> option =
    ops
    |> List.fold
        (fun acc op ->
            match acc with
            | None -> None
            | Some node ->
                match UiApply.apply op node with
                | Ok next -> Some next
                | Error _ -> None)
        (Some start)

// ---------------------------------------------------------------------------
//  law 1 — merge3Way order-independence
// ---------------------------------------------------------------------------

type MergeVerdict =
    {
        Trials: int
        Applied: int
        Merged: int
        Refused: int
        // ---- refutations: each of these being non-zero means the law is FALSE ----
        /// The two arrival orders reached different outcome CLASSES (one merged, one refused).
        ClassDivergences: int
        /// Both merged, to different trees. This is the tie-break claim: a perturbation of the
        /// NodeId-canonical-byte ordering of disjoint inserts lands here.
        TreeDivergences: int
        /// Both refused, over different contended CELL SETS.
        CellSetDivergences: int
        /// `merge3Way base x x` returned a tree that was not `x`.
        SelfMergeFailures: int
        /// `merge3Way base base x` (or `base x base`) did not return `x`.
        FastForwardFailures: int
        // ---- measured findings: reported, NOT refuted (see the block below) ----
        /// Both orders refused over the same cells, but recorded different VALUES.
        ValueAsymmetries: int
        /// `merge3Way base x x` REFUSED rather than returning `x`.
        SelfMergeRefusals: int
        /// The first refutation seen, as a reproducer.
        First: string option
    }

// ---------------------------------------------------------------------------
//  the two measured findings, and why each is a COUNT rather than a refutation
// ---------------------------------------------------------------------------
//
//  Both were found by running this law, and neither is fixed here: `TreeMerge` is a shipped
//  package whose conflict envelopes are a public contract, and both repairs are behaviour
//  changes with more than one defensible shape. They are counted and printed so the gate log
//  carries the measurement rather than leaving it to prose, and reported as successor work.
//
//  1. `ValueAsymmetries` — the recovery envelope is documented as three-up (base / primary /
//     secondary), but `Primary` is populated only when a primacy pin is held. Under `merge3Way`
//     BOTH sides are `Secondary`, so exactly one branch's value is recorded, in the `Secondary`
//     slot — and WHICH one depends on the argument order (`mergeCanonicalFacet` writes
//     `if aPrimary then bC else aC`, and `aPrimary` is false for two secondaries). Two replicas
//     refusing the same merge therefore agree about every contended cell and disagree about what
//     the other side wanted. The cell set — what the .NET `MergeTests.fs` asserts, and what a
//     host resolves against — is order-independent, which is why that is the law here.
//
//  2. `SelfMergeRefusals` — `merge3Way base a a` refuses whenever `a` changed the children of
//     any node. Every other facet takes the shared value when both sides changed it to the SAME
//     thing (`mergeCanonicalFacet`'s `aC <> bC` guard); the children facet has no such guard, and
//     its auto-merge path additionally demands `Set.isEmpty (Set.intersect aNew bNew)` — which
//     two IDENTICAL branches maximally violate. The file's own header states the general rule
//     ("when both sides changed it to different values, it is a CONFLICT"), so this reads as a
//     gap rather than a decision. It is not repaired here because the obvious one-line guard
//     (`aIds = bIds` ⇒ take it) opens a case the disjointness test currently makes unreachable:
//     two branches inserting the SAME id with DIFFERENT content, where `recurseChild` takes the
//     A-side unconditionally — which would be an order-dependent tree, i.e. it would trade this
//     finding for a refutation of the law above.

let mergeOrderLaws (seed: int) (iterations: int) : MergeVerdict =
    let b = unwrap baseTree
    PortableRng.reseed seed
    let mutable applied = 0
    let mutable merged = 0
    let mutable refused = 0
    let mutable classDiv = 0
    let mutable treeDiv = 0
    let mutable cellDiv = 0
    let mutable selfFail = 0
    let mutable ffFail = 0
    let mutable valueAsym = 0
    let mutable selfRefused = 0
    // A ref rather than a `let mutable`, because the first-refutation note is written from more
    // than one place and F# does not let a closure capture a mutable local.
    let first: string option ref = ref None

    let note (why: string) =
        if first.Value.IsNone then
            first.Value <- Some why

    for i in 0 .. iterations - 1 do
        let opsA = genEdit ()
        let opsB = genEdit ()

        match applyEdit opsA b, applyEdit opsB b with
        | Some a, Some bb ->
            applied <- applied + 1

            match mergeOutcome b a bb, mergeOutcome b bb a with
            | Merged f, Merged r ->
                merged <- merged + 1

                if f <> r then
                    treeDiv <- treeDiv + 1
                    note ("trial " + string i + ": merged trees differ by arrival order")
            | Refused(fc, fv), Refused(rc, rv) ->
                refused <- refused + 1

                if fc <> rc then
                    cellDiv <- cellDiv + 1

                    note (
                        "trial "
                        + string i
                        + ": refusals name different contended cells by arrival order"
                    )

                if fv <> rv then
                    valueAsym <- valueAsym + 1
            | _ ->
                classDiv <- classDiv + 1
                note ("trial " + string i + ": one order merged and the other refused")

            // Self-merge. The law is CONDITIONAL — when the engine merges a branch with itself it
            // must return that branch — because the engine refuses this outright whenever the
            // branch touched children (finding 2 above). Refusals are counted, not refuted; the
            // trials that DO merge (the pure-restyle branches) are where the claim bites.
            match TreeMerge.merge3Way b a a with
            | Ok tree ->
                if CanonicalJson.encodeNode tree <> CanonicalJson.encodeNode a then
                    selfFail <- selfFail + 1
                    note ("trial " + string i + ": merge3Way base a a merged to a tree that is not a")
            | Error _ -> selfRefused <- selfRefused + 1

            // Fast-forward, both ways round: a side that did not move contributes nothing, so the
            // merge is the side that did. Checked in BOTH positions, because a merge that
            // fast-forwarded in one argument order only would be exactly the order-dependence
            // this law is about, arriving through the degenerate case.
            match TreeMerge.merge3Way b b a, TreeMerge.merge3Way b a b with
            | Ok l, Ok r when
                CanonicalJson.encodeNode l = CanonicalJson.encodeNode a
                && CanonicalJson.encodeNode r = CanonicalJson.encodeNode a
                ->
                ()
            | _ ->
                ffFail <- ffFail + 1

                note (
                    "trial "
                    + string i
                    + ": merge3Way against an unchanged side did not fast-forward"
                )
        | _ -> ()

    { Trials = iterations
      Applied = applied
      Merged = merged
      Refused = refused
      ClassDivergences = classDiv
      TreeDivergences = treeDiv
      CellSetDivergences = cellDiv
      SelfMergeFailures = selfFail
      FastForwardFailures = ffFail
      ValueAsymmetries = valueAsym
      SelfMergeRefusals = selfRefused
      First = first.Value }

let mergeVerdictLines (v: MergeVerdict) : string list =
    let counts =
        "MERGE trials="
        + string v.Trials
        + " applied="
        + string v.Applied
        + " merged="
        + string v.Merged
        + " refused="
        + string v.Refused

    let laws =
        "MERGELAW class="
        + string v.ClassDivergences
        + " tree="
        + string v.TreeDivergences
        + " cells="
        + string v.CellSetDivergences
        + " self="
        + string v.SelfMergeFailures
        + " fastforward="
        + string v.FastForwardFailures

    // Printed beside the law line rather than folded into it, so the two open findings are
    // MEASURED on every run of the gate on both pipelines instead of living only in prose.
    let findings =
        "MERGEFINDING valueAsymmetries="
        + string v.ValueAsymmetries
        + " selfMergeRefusals="
        + string v.SelfMergeRefusals

    match v.First with
    | Some why -> [ counts; laws; findings; "MERGEFAIL " + sanitise why ]
    | None -> [ counts; laws; findings ]

/// Every way this law can be refuted, counted once.
let mergeViolations (v: MergeVerdict) : int =
    v.ClassDivergences
    + v.TreeDivergences
    + v.CellSetDivergences
    + v.SelfMergeFailures
    + v.FastForwardFailures

/// A run that never auto-merged, or never refused, proves nothing about the case it missed. The
/// kit guards its own families this way; the tier-shaped law beside them is held to it too.
let mergeAdequacyFailures (v: MergeVerdict) : string list =
    [ if v.Merged = 0 then
          "ADEQUACY merge: no trial auto-merged — the tie-break case was never sampled"
      if v.Refused = 0 then
          "ADEQUACY merge: no trial refused — the conflict path was never sampled"
      // The self-merge law is conditional on the engine merging at all, so a run in which every
      // self-merge refused would report it green having asserted nothing.
      if v.Applied - v.SelfMergeRefusals = 0 then
          "ADEQUACY merge: every self-merge refused — the conditional self-merge law asserted nothing" ]

// ---------------------------------------------------------------------------
//  law 2 — the kit's lane-fold family, under this pipeline
// ---------------------------------------------------------------------------

/// The census enrolment name for this family's FABLE port — `CoreConformanceCensus.fs` carries it
/// in an `AdoptedAcross` row beside the .NET one, and enrolment there is by NAME, checked by a
/// source scan over this project. So the name lives beside the code it describes: a census string
/// that matched nothing here would be exactly the silent drift that census exists to catch.
///
/// It is also the label `Program.fs` prints, on BOTH legs, because the two legs' output is
/// byte-compared and a label that differed between them would be a divergence in the reporting
/// rather than in the algebra. The claim it names is the harness's, not one pipeline's.
let laneFoldFablePort =
    "laneFoldLaws certifies through the Fable law harness over the tier's reducer and codec"

/// Three lanes off one base, folded under all 3! = 6 arrival orders per trial — the same
/// instantiation `CoreDagLawTests.fs` runs on .NET, over the same witnesses.
let laneFoldResults (seed: int) (iterations: int) : Fuaran.Core.LawResult list =
    // The tier's `LaneGen` draws from `PortableRng`, so the seed is set here rather than passed
    // through the kit's rng — see the block above `PortableRng` in `TestSupport.fs`. The kit's own
    // `seed` argument still names the run in its verdicts, so a refutation stays quotable.
    PortableRng.reseed seed
    FoldConfluence.laneFoldLaws coreSw footprintOfEqOp hashState laneGen 3 seed iterations
