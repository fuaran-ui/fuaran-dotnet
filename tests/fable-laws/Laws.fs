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

let private sideString (side: MergeSide option) : string =
    match side with
    | None -> "-"
    | Some s -> s.Value + "~" + defaultArg s.Tag "-"

/// The refusal's recorded VALUES, rendered EXACTLY as the two-sided envelope stands (Phase 1497):
/// `nodeId|facet|class|base|A|B`, entries ordered by the cell key so the rendering never depends on
/// the fold's emission order.
///
/// Until 1497 this had to compare the pair UNORDERED, because the engine recorded exactly ONE
/// branch's value and which one depended on the argument order — so a faithful rendering would have
/// diverged on every refusal for a reason that was the envelope's defect rather than a merge
/// divergence. The envelope now carries both sides, so the faithful rendering is comparable and the
/// claim below is the strong one: swapping the branches TRANSPOSES the envelope and changes nothing
/// else in it.
let private conflictValues (transpose: bool) (conflicts: MergeConflict list) : string =
    conflicts
    |> List.map (fun c ->
        let a = sideString c.A
        let b = sideString c.B

        (c.NodeId + "|" + c.Facet + "|" + classString c.Class),
        (c.Base + "|" + (if transpose then b else a) + "|" + (if transpose then a else b)))
    |> List.distinct
    |> List.sortBy fst
    |> List.map (fun (key, body) -> key + "|" + body)
    |> String.concat ";"

type private Outcome =
    | Merged of canonical: string
    | Refused of cells: string * values: string * transposed: string

let private mergeOutcome (b: Node<obj>) (x: Node<obj>) (y: Node<obj>) : Outcome =
    match TreeMerge.merge3Way b x y with
    | Ok tree -> Merged(CanonicalJson.encodeNode tree)
    | Error conflicts -> Refused(conflictCells conflicts, conflictValues false conflicts, conflictValues true conflicts)

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

/// The CONTENT an insert carries. Drawn independently of the id, so two branches that pick the same
/// fresh id agree about it sometimes and disagree about it sometimes — the two branches of the
/// shared-children guard (Phase 1497). Before 1497 every generated insert carried the same empty
/// text, so a same-id collision could only ever be an agreement and the refusal path was unsampled.
let private freshTexts = [| ""; "alpha"; "beta" |]

let private genEdit () : TreeOp<obj> list =
    let shape = PortableRng.below 4
    let k = PortableRng.below 3
    let t = PortableRng.below 3
    let f = PortableRng.below 6
    let x = PortableRng.below 3

    match shape with
    | 0 -> [ TreeOp.InsertChild(NodeId "root", unwrap (mkLeafText freshIds[f] freshTexts[x])) ]
    | 1 -> [ styleOp childIds[k] tones[t] ]
    | 2 -> [ TreeOp.RemoveNode childIds[k] ]
    | _ ->
        [ styleOp childIds[k] tones[t]
          TreeOp.InsertChild(NodeId "root", unwrap (mkLeafText freshIds[f] freshTexts[x])) ]

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
        /// The two fixed shared-insert claims (Phase 1497), refuted.
        SharedInsertFailures: int
        // ---- the two Phase 1488 FINDINGS, now claims: each must be 0 ----------
        /// Both orders refused over the same cells, but the swapped run's envelope was not the
        /// transposition of the forward run's. Phase 1488 measured 101 of 118 refusals here and
        /// counted them; Phase 1497 widened the envelope so the count is a refutation.
        ValueAsymmetries: int
        /// `merge3Way base x x` REFUSED rather than returning `x`. Phase 1488 measured 223 of 300
        /// and counted them; Phase 1497's shared-children guard makes the count a refutation.
        SelfMergeRefusals: int
        // ---- adequacy ---------------------------------------------------------
        /// Self-merge trials whose branch actually CHANGED some node's children — the case the
        /// shared-children guard exists for. A run with none of these would assert the self-merge
        /// law only over branches that never reached the repaired path.
        SelfMergeStructural: int
        /// The first refutation seen, as a reproducer.
        First: string option
    }

// ---------------------------------------------------------------------------
//  the two Phase 1488 findings, now asserted (Phase 1497)
// ---------------------------------------------------------------------------
//
//  1488 MEASURED both of these and deliberately did not fix either, because both touch the public
//  conflict contract of a shipped package. It printed `MERGEFINDING valueAsymmetries=101
//  selfMergeRefusals=223` on every gate run so the numbers lived in the log rather than in prose.
//  1497 repaired both and the SAME two counters are now refutations: the line is still printed,
//  under the same names, and it must read zero.
//
//  1. `ValueAsymmetries` — the envelope was documented three-up (base / primary / secondary) but
//     populated `Primary` only under a primacy pin, so with two `Secondary` sides exactly ONE
//     branch's value was recorded and which one depended on the argument order. Two replicas
//     refusing the same merge agreed about every contended cell and disagreed about what the other
//     side wanted. The envelope now carries `A` and `B` unconditionally, and the precedence slots
//     are populated exactly when a pin is held — so the claim is the strong one: swapping the
//     branches TRANSPOSES the envelope and changes nothing else in it.
//
//  2. `SelfMergeRefusals` — `merge3Way base a a` refused whenever `a` changed any node's children,
//     because the children facet lacked the "both sides changed it to the same thing" guard every
//     other facet has. The guard is in, and the case it opens — two branches inserting the SAME id
//     with DIFFERENT content, where `recurseChild` used to take the A side unconditionally — is a
//     refusal naming that id rather than an arrival-order-dependent tree. Both halves are asserted:
//     the self-merge law here, and the two fixed claims in `sharedInsertFailures` below.

/// The two FIXED claims the shared-children guard has to satisfy, asserted directly rather than
/// sampled: a generated run that happened to draw neither would report the repaired path green
/// having exercised nothing. Returns the refutation count.
///
///   * two branches inserting the same id with the SAME content auto-merge, and the merged tree
///     carries that child exactly once;
///   * two branches inserting the same id with DIFFERENT content REFUSE, and the refusal NAMES the
///     id — the outcome the A-side default silently produced a tree for.
let sharedInsertFailures () : string list =
    let b = unwrap baseTree

    let ins (text: string) =
        TreeOp.InsertChild(NodeId "root", unwrap (mkLeafText "shared" text))

    let branch (text: string) =
        match UiApply.apply (ins text) b with
        | Ok node -> Some node
        | Error _ -> None

    match branch "alpha", branch "beta" with
    | Some alpha1, Some beta ->
        // The same insert applied twice off the base — two branches that AGREE.
        let alpha2 = alpha1

        [ (match TreeMerge.merge3Way b alpha1 alpha2 with
           | Error _ -> [ "SHAREDINSERT agreeing same-id inserts refused instead of taking the shared value" ]
           | Ok tree ->
               let ids =
                   Fuaran.UI.Ops.Introspect.getChildren tree.Kind
                   |> Option.defaultValue []
                   |> List.filter (fun n -> n.Id = "shared")

               if List.length ids = 1 then
                   []
               else
                   [ "SHAREDINSERT agreeing same-id inserts produced "
                     + string (List.length ids)
                     + " copies of the shared child" ])
          (match TreeMerge.merge3Way b alpha1 beta with
           | Ok _ -> [ "SHAREDINSERT differing same-id inserts merged instead of refusing" ]
           | Error conflicts ->
               if conflicts |> List.exists (fun c -> c.NodeId = "shared") then
                   []
               else
                   [ "SHAREDINSERT differing same-id inserts refused without naming the contended id" ])
          // The refusal must also be order-independent, in both halves: the same cells, and an
          // envelope that is the transposition of the forward one.
          (match TreeMerge.merge3Way b alpha1 beta, TreeMerge.merge3Way b beta alpha1 with
           | Error f, Error r ->
               [ if conflictCells f <> conflictCells r then
                     "SHAREDINSERT differing same-id inserts named different cells by arrival order"
                 if conflictValues false f <> conflictValues true r then
                     "SHAREDINSERT differing same-id inserts recorded a non-transposed envelope by arrival order" ]
           | _ -> [ "SHAREDINSERT differing same-id inserts did not refuse under both arrival orders" ]) ]
        |> List.concat
    | _ -> [ "SHAREDINSERT the fixed shared-insert scenario did not apply to the base tree" ]

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
    let mutable selfStructural = 0
    // A ref rather than a `let mutable`, because the first-refutation note is written from more
    // than one place and F# does not let a closure capture a mutable local.
    let first: string option ref = ref None

    let note (why: string) =
        if first.Value.IsNone then
            first.Value <- Some why

    let sharedInsert = sharedInsertFailures ()

    match sharedInsert with
    | msg :: _ -> note msg
    | [] -> ()

    /// The root-level child ids of a tree — the generator only inserts and removes under `root`, so
    /// this is exactly "did this branch change any node's children".
    let rootChildIds (n: Node<obj>) =
        Fuaran.UI.Ops.Introspect.getChildren n.Kind
        |> Option.defaultValue []
        |> List.map (fun c -> c.Id)

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
            | Refused(fc, fv, _), Refused(rc, _, rt) ->
                refused <- refused + 1

                if fc <> rc then
                    cellDiv <- cellDiv + 1

                    note (
                        "trial "
                        + string i
                        + ": refusals name different contended cells by arrival order"
                    )

                // The swapped run's envelope, transposed, must BE the forward run's — the whole of
                // finding 1, stated as an equality rather than as a canonicalised near-miss.
                if fv <> rt then
                    valueAsym <- valueAsym + 1

                    note (
                        "trial "
                        + string i
                        + ": the swapped refusal envelope is not the transposition of the forward one"
                    )
            | _ ->
                classDiv <- classDiv + 1
                note ("trial " + string i + ": one order merged and the other refused")

            // Self-merge. UNCONDITIONAL since Phase 1497's shared-children guard: merging a branch
            // with itself must RETURN that branch, whatever the branch did — a refusal is now a
            // refutation, not a counted finding. `selfStructural` records how many of these trials
            // actually reached the repaired path, so a generator that stopped producing structural
            // edits could not report this green having asserted nothing about it.
            if rootChildIds a <> rootChildIds b then
                selfStructural <- selfStructural + 1

            match TreeMerge.merge3Way b a a with
            | Ok tree ->
                if CanonicalJson.encodeNode tree <> CanonicalJson.encodeNode a then
                    selfFail <- selfFail + 1
                    note ("trial " + string i + ": merge3Way base a a merged to a tree that is not a")
            | Error _ ->
                selfRefused <- selfRefused + 1
                note ("trial " + string i + ": merge3Way base a a refused instead of returning a")

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
      SharedInsertFailures = List.length sharedInsert
      ValueAsymmetries = valueAsym
      SelfMergeRefusals = selfRefused
      SelfMergeStructural = selfStructural
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
        + " sharedinsert="
        + string v.SharedInsertFailures

    // The Phase 1488 finding line, kept VERBATIM under its own names. It carried the measurement of
    // two defects on every gate run; it now carries the claim that both are repaired, and both
    // counters feed `mergeViolations`. Keeping the line rather than renaming it is the point: the
    // same log line that read `101` / `223` reads `0`.
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
    + v.SharedInsertFailures
    // Phase 1497: the two Phase 1488 measurements are refutations now.
    + v.ValueAsymmetries
    + v.SelfMergeRefusals

/// A run that never auto-merged, or never refused, proves nothing about the case it missed. The
/// kit guards its own families this way; the tier-shaped law beside them is held to it too.
let mergeAdequacyFailures (v: MergeVerdict) : string list =
    [ if v.Merged = 0 then
          "ADEQUACY merge: no trial auto-merged — the tie-break case was never sampled"
      if v.Refused = 0 then
          "ADEQUACY merge: no trial refused — the conflict path was never sampled"
      // Phase 1497: the self-merge law is unconditional now, so the guard that mattered moved. What
      // it has to rule out is a run whose branches never touched children at all — the self-merge
      // claim would then hold trivially, over exactly the branches that never reach the
      // shared-children guard the claim exists to certify.
      if v.SelfMergeStructural = 0 then
          "ADEQUACY merge: no self-merge trial changed any children — the shared-children guard was never sampled" ]

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
