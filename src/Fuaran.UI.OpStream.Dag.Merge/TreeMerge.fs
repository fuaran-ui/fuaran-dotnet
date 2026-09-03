namespace Fuaran.UI.OpStream.Dag.Merge

open System
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.Introspect
open Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  TreeMerge — facet-refined structural 3-way merge (M1 floor + M2 refinement).
//
//  A node decomposes into independent FACETS, each 3-way-merged on its own:
//
//   - "kind"            — the node's own kind-fields (spec + discriminator),
//                         children / style / state / accessibility excluded.
//   - "style.{tone,weight,emphasis,role,voice}" — the SemanticStyle sub-fields,
//                         merged INDEPENDENTLY (M2: A's Tone + B's Voice
//                         auto-blend instead of conflicting on whole-style).
//   - "state"           — the StateBehaviour block.
//   - "accessibility"   — the Accessibility block.
//   - "tooltip"         — the node-level tooltip trait (Phase 1112).
//   - "children"        — the ordered child-id list (structural).
//
//  When a facet changed on at most one side, take that side's value. When both
//  sides changed it to different values, it is a CONFLICT — surfaced as a
//  `MergeConflict` recovery envelope (three-up base/primary/secondary), NOT a
//  silent pick. Under PRECEDENCE (one side classified `Primary` by the host's
//  author classifier), the conflict lists `KeepPrimary` first and is
//  `PrimacyHeld`; with both sides `Secondary`, no pin is held. The one
//  structural case auto-merged across both sides is
//  disjoint pure inserts into the same parent, ordered by NodeId canonical
//  bytes (the deterministic, wall-clock-free tie-break, inherited from M1).
//
//  All equality is `CanonicalJson` bytes (closure-safe), never F# structural
//  equality — except the closure-free `SemanticStyle` sub-fields, compared
//  directly.
// ============================================================================

module TreeMerge =

    let private rawId (NodeId s) : string = s

    let private childrenOf<'Msg> (node: Node<'Msg>) : Node<'Msg> list =
        getChildren node.Kind |> Option.defaultValue []

    let private childlessKind<'Msg> (node: Node<'Msg>) : NodeKind<'Msg> =
        match withChildren node.Kind [] with
        | Some k -> k
        | None -> node.Kind

    let private childIds<'Msg> (node: Node<'Msg>) : string list = childrenOf node |> List.map _.Id

    let private byId<'Msg> (nodes: Node<'Msg> list) : Map<string, Node<'Msg>> =
        nodes |> List.map (fun n -> n.Id, n) |> Map.ofList

    let private styleOf<'Msg> (n: Node<'Msg>) : SemanticStyle =
        n.Style |> Option.defaultValue Defaults.style

    // ── facet isolation probes (closure-safe canonical bytes) ───────────────
    //
    // Each probe holds every OTHER facet fixed so only the named facet varies,
    // letting a closure-bearing facet be compared by canonical JSON.

    /// Kind-own canonical (children + style + state + accessibility + tooltip
    /// neutralised).
    let private kindCanonical<'Msg> (n: Node<'Msg>) : string =
        CanonicalJson.encodeNode
            { n with
                Kind = childlessKind n
                Style = None
                State = None
                Accessibility = None
                Tooltip = None }

    /// State canonical (kind/style/accessibility/tooltip neutralised to a fixed shell).
    let private stateCanonical<'Msg> (shellKind: NodeKind<'Msg>) (n: Node<'Msg>) : string =
        CanonicalJson.encodeNode
            { n with
                Kind = shellKind
                Style = None
                Accessibility = None
                Tooltip = None }

    /// Accessibility canonical (kind/style/state/tooltip neutralised to a fixed shell).
    let private accessibilityCanonical<'Msg> (shellKind: NodeKind<'Msg>) (n: Node<'Msg>) : string =
        CanonicalJson.encodeNode
            { n with
                Kind = shellKind
                Style = None
                State = None
                Tooltip = None }

    /// Tooltip canonical (kind/style/state/accessibility neutralised to a fixed
    /// shell) — Phase 1112.
    ///
    /// Isolating it is not bookkeeping. Without a probe of its own a tooltip-only
    /// edit varies the bytes of the KIND probe, so two branches that changed
    /// nothing but the hint would be reported as a concurrent edit to the node's
    /// kind; and without a pick of its own the rebuild below would take the base
    /// node's hint on every merge, discarding an uncontested edit on either side
    /// in silence. Both are the failure this facet decomposition exists to
    /// prevent, so a node-level trait joins it in the same change as the trait.
    let private tooltipCanonical<'Msg> (shellKind: NodeKind<'Msg>) (n: Node<'Msg>) : string =
        CanonicalJson.encodeNode
            { n with
                Kind = shellKind
                Style = None
                State = None
                Accessibility = None }

    /// `true` when `headIds` is `baseIds` with zero removals and zero reorders.
    let private isPureAddition (baseIds: string list) (headIds: string list) : bool =
        let headSet = Set.ofList headIds
        let baseSurvive = baseIds |> List.filter headSet.Contains

        baseSurvive = baseIds
        && (headIds |> List.filter (fun i -> List.contains i baseIds)) = baseIds

    // ── authorship / primacy ────────────────────────────────────────────────

    /// Which side wins a conflicted facet under precedence, and whether a pin is
    /// held. Returns `(aIsPrimary, pinHeld, choices, secondaryTag)`.
    let private resolveAuthor
        (authorA: MergeAuthor)
        (authorB: MergeAuthor)
        : (bool * bool * MergeChoice list * (string option)) =
        // (aIsPrimary, bIsPrimary)
        match authorA, authorB with
        | MergeAuthor.Primary, MergeAuthor.Secondary r ->
            true, true, [ MergeChoice.KeepPrimary; MergeChoice.KeepSecondary; MergeChoice.KeepBase ], r
        | MergeAuthor.Secondary r, MergeAuthor.Primary ->
            false, true, [ MergeChoice.KeepPrimary; MergeChoice.KeepSecondary; MergeChoice.KeepBase ], r
        | MergeAuthor.Secondary r, MergeAuthor.Secondary _ ->
            false, false, [ MergeChoice.KeepBase; MergeChoice.KeepSecondary ], r
        | MergeAuthor.Primary, MergeAuthor.Primary ->
            // Two primary sides — no precedence pin; surface for host decision.
            false, false, [ MergeChoice.KeepBase ], None

    /// Merge a single SemanticStyle sub-field; record a conflict on divergence.
    /// `cellAuthor nodeId facet` yields the per-cell `(A-side, B-side)` authorship
    /// (the last writer of THIS cell on each branch — see `DagPrimacy`).
    let private mergeStyleField<'T when 'T: equality>
        (conflicts: ResizeArray<MergeConflict>)
        (nodeId: string)
        (facet: string)
        (cellAuthor: string -> string -> MergeAuthor * MergeAuthor)
        (baseV: 'T)
        (aV: 'T)
        (bV: 'T)
        : 'T =
        let aChanged = aV <> baseV
        let bChanged = bV <> baseV

        if aChanged && bChanged && aV <> bV then
            let authorA, authorB = cellAuthor nodeId facet
            let aPrimary, pinHeld, choices, secondaryTag = resolveAuthor authorA authorB

            conflicts.Add
                { NodeId = nodeId
                  Facet = facet
                  Class = MergeConflictClass.ConcurrentEdit
                  Base = sprintf "%A" baseV
                  Primary =
                    (if pinHeld then
                         Some(sprintf "%A" (if aPrimary then aV else bV))
                     else
                         None)
                  Secondary = Some(sprintf "%A" (if aPrimary then bV else aV))
                  SecondaryTag = secondaryTag
                  PrimacyHeld = pinHeld
                  Choices = choices
                  Hint = ApplyHint.empty }
            // Precedence: keep the primary side's field; else base.
            if pinHeld then (if aPrimary then aV else bV) else baseV
        elif aChanged then
            aV
        elif bChanged then
            bV
        else
            baseV

    /// Merge a canonical-compared facet (kind / state / accessibility): pick the
    /// changed side, or record a conflict. Returns which node to source the
    /// facet value from (`Choose.A` / `B` / `Base`).
    let private mergeCanonicalFacet<'Msg>
        (conflicts: ResizeArray<MergeConflict>)
        (nodeId: string)
        (facet: string)
        (cls: MergeConflictClass)
        (cellAuthor: string -> string -> MergeAuthor * MergeAuthor)
        (baseC: string)
        (aC: string)
        (bC: string)
        : int =
        // returns 0=base, 1=a, 2=b
        let aChanged = aC <> baseC
        let bChanged = bC <> baseC

        if aChanged && bChanged && aC <> bC then
            let authorA, authorB = cellAuthor nodeId facet
            let aPrimary, pinHeld, choices, secondaryTag = resolveAuthor authorA authorB

            conflicts.Add
                { NodeId = nodeId
                  Facet = facet
                  Class = cls
                  Base = baseC
                  Primary = (if pinHeld then Some(if aPrimary then aC else bC) else None)
                  Secondary = Some(if aPrimary then bC else aC)
                  SecondaryTag = secondaryTag
                  PrimacyHeld = pinHeld
                  Choices = choices
                  Hint = ApplyHint.empty }

            if pinHeld then (if aPrimary then 1 else 2) else 0
        elif aChanged then
            1
        elif bChanged then
            2
        else
            0

    /// Merge the KIND facet with the `KindSwapOrphansPin` refinement (the F1-drift
    /// defence). Returns the node whose childless kind the rebuild should adopt.
    ///
    /// A kind-facet conflict where one side is `Primary`, the primary side KEPT
    /// the base discriminator (a field-pin), and the `Secondary` side SWAPPED it
    /// is not a plain `ConcurrentEdit` — the swap *destroys* the primary side's
    /// pinned cell. It is classed `KindSwapOrphansPin`, the orphaned primary kind
    /// + secondary tag are surfaced, and `ReassertPinOntoNewKind` is offered first
    /// WHEN a name+type-compatible field exists on the new kind (else
    /// `KeepOldKind`). The built tree keeps the primary side's kind — never a
    /// silent loss — and the host applies the chosen migration via
    /// `ReassertPin.tryReassert`.
    let private mergeKindFacet<'Msg>
        (conflicts: ResizeArray<MergeConflict>)
        (cellAuthor: string -> string -> MergeAuthor * MergeAuthor)
        (baseN: Node<'Msg>)
        (a: Node<'Msg>)
        (b: Node<'Msg>)
        : Node<'Msg> =
        let id = baseN.Id
        let baseKC = kindCanonical baseN
        let aKC = kindCanonical a
        let bKC = kindCanonical b
        let aChanged = aKC <> baseKC
        let bChanged = bKC <> baseKC

        if aChanged && bChanged && aKC <> bKC then
            let authorA, authorB = cellAuthor id "kind"
            let aPrimary, pinHeld, choices, secondaryTag = resolveAuthor authorA authorB
            let primaryNode = if aPrimary then a else b
            let secondaryNode = if aPrimary then b else a
            let baseName = kindName baseN.Kind
            let primaryName = kindName primaryNode.Kind
            let secondaryName = kindName secondaryNode.Kind

            if pinHeld && primaryName = baseName && secondaryName <> baseName then
                // Secondary-side kind-swap orphans the primary side's field-pin.
                let migrated = ReassertPin.tryReassert baseN primaryNode secondaryNode

                let swapChoices =
                    match migrated with
                    | Some _ ->
                        [ MergeChoice.ReassertPinOntoNewKind
                          MergeChoice.KeepOldKind
                          MergeChoice.KeepSecondary ]
                    | None -> [ MergeChoice.KeepOldKind; MergeChoice.KeepSecondary ]

                conflicts.Add
                    { NodeId = id
                      Facet = "kind"
                      Class = MergeConflictClass.KindSwapOrphansPin
                      Base = baseKC
                      Primary = Some(kindCanonical primaryNode)
                      Secondary = Some(kindCanonical secondaryNode)
                      SecondaryTag = secondaryTag
                      PrimacyHeld = true
                      Choices = swapChoices
                      Hint = ApplyHint.empty }

                primaryNode // never silent loss — keep the primary side's kind + pin
            else
                conflicts.Add
                    { NodeId = id
                      Facet = "kind"
                      Class = MergeConflictClass.ConcurrentEdit
                      Base = baseKC
                      Primary =
                        (if pinHeld then
                             Some(if aPrimary then aKC else bKC)
                         else
                             None)
                      Secondary = Some(if aPrimary then bKC else aKC)
                      SecondaryTag = secondaryTag
                      PrimacyHeld = pinHeld
                      Choices = choices
                      Hint = ApplyHint.empty }

                if pinHeld then (if aPrimary then a else b) else baseN
        elif aChanged then
            a
        elif bChanged then
            b
        else
            baseN

    let rec private merge3<'Msg>
        (conflicts: ResizeArray<MergeConflict>)
        (cellAuthor: string -> string -> MergeAuthor * MergeAuthor)
        (baseN: Node<'Msg>)
        (aOpt: Node<'Msg> option)
        (bOpt: Node<'Msg> option)
        : Node<'Msg> =
        let a = Option.defaultValue baseN aOpt
        let b = Option.defaultValue baseN bOpt
        let id = baseN.Id
        let shellKind = childlessKind baseN

        // kind facet (KindSwapOrphansPin-aware — the F1-drift defence)
        let kindSource = mergeKindFacet conflicts cellAuthor baseN a b

        // style sub-fields (independent) — read default-through (`None` = all-default)
        let baseS = styleOf baseN
        let aS = styleOf a
        let bS = styleOf b

        let mergedStyle: SemanticStyle =
            { Tone = mergeStyleField conflicts id "style.tone" cellAuthor baseS.Tone aS.Tone bS.Tone
              Weight = mergeStyleField conflicts id "style.weight" cellAuthor baseS.Weight aS.Weight bS.Weight
              Emphasis = mergeStyleField conflicts id "style.emphasis" cellAuthor baseS.Emphasis aS.Emphasis bS.Emphasis
              Role = mergeStyleField conflicts id "style.role" cellAuthor baseS.Role aS.Role bS.Role
              Voice = mergeStyleField conflicts id "style.voice" cellAuthor baseS.Voice aS.Voice bS.Voice
              // Phase 1472 — `direction` merges as an independent sub-field like
              // every other style slot: two lanes declaring different directions
              // for one value is a genuine concurrent edit, not a mergeable pair.
              Direction =
                mergeStyleField conflicts id "style.direction" cellAuthor baseS.Direction aS.Direction bS.Direction }

        // state facet
        let statePick =
            mergeCanonicalFacet
                conflicts
                id
                "state"
                MergeConflictClass.ConcurrentEdit
                cellAuthor
                (stateCanonical shellKind baseN)
                (stateCanonical shellKind a)
                (stateCanonical shellKind b)

        let mergedState =
            match statePick with
            | 1 -> a.State
            | 2 -> b.State
            | _ -> baseN.State

        // accessibility facet
        let accPick =
            mergeCanonicalFacet
                conflicts
                id
                "accessibility"
                MergeConflictClass.ConcurrentEdit
                cellAuthor
                (accessibilityCanonical shellKind baseN)
                (accessibilityCanonical shellKind a)
                (accessibilityCanonical shellKind b)

        let mergedAcc =
            match accPick with
            | 1 -> a.Accessibility
            | 2 -> b.Accessibility
            | _ -> baseN.Accessibility

        // tooltip facet (Phase 1112)
        let tooltipPick =
            mergeCanonicalFacet
                conflicts
                id
                "tooltip"
                MergeConflictClass.ConcurrentEdit
                cellAuthor
                (tooltipCanonical shellKind baseN)
                (tooltipCanonical shellKind a)
                (tooltipCanonical shellKind b)

        let mergedTooltip =
            match tooltipPick with
            | 1 -> a.Tooltip
            | 2 -> b.Tooltip
            | _ -> baseN.Tooltip

        // children facet (structural)
        let baseIds = childIds baseN
        let aIds = childIds a
        let bIds = childIds b
        let aStruct = aIds <> baseIds
        let bStruct = bIds <> baseIds
        let baseMap = byId (childrenOf baseN)
        let aMap = byId (childrenOf a)
        let bMap = byId (childrenOf b)

        let recurseChild (cid: string) : Node<'Msg> =
            match Map.tryFind cid baseMap with
            | Some bc -> merge3 conflicts cellAuthor bc (Map.tryFind cid aMap) (Map.tryFind cid bMap)
            | None ->
                match Map.tryFind cid aMap, Map.tryFind cid bMap with
                | Some ac, _ -> ac
                | _, Some bc -> bc
                | None, None -> failwithf "merge3: child id %s vanished" cid

        let mergedChildren: Node<'Msg> list =
            match aStruct, bStruct with
            | false, false -> baseIds |> List.map recurseChild
            | true, false -> aIds |> List.map recurseChild
            | false, true -> bIds |> List.map recurseChild
            | true, true ->
                let aNew = Set.difference (Set.ofList aIds) (Set.ofList baseIds)
                let bNew = Set.difference (Set.ofList bIds) (Set.ofList baseIds)

                let pureInsertsDisjoint =
                    isPureAddition baseIds aIds
                    && isPureAddition baseIds bIds
                    && Set.isEmpty (Set.intersect aNew bNew)

                if pureInsertsDisjoint then
                    let survivors = baseIds |> List.map recurseChild

                    let newIds =
                        Set.union aNew bNew
                        |> Set.toList
                        |> List.sortWith (fun x y -> String.CompareOrdinal(x, y))

                    survivors @ (newIds |> List.map recurseChild)
                else
                    // both structurally changed the same parent differently
                    let authorA, authorB = cellAuthor id "children"
                    let _, pinHeld, choices, secondaryTag = resolveAuthor authorA authorB

                    conflicts.Add
                        { NodeId = id
                          Facet = "children"
                          Class = MergeConflictClass.ReorderVsStructural
                          Base = String.Join(",", baseIds)
                          Primary = (if pinHeld then Some(String.Join(",", aIds)) else None)
                          Secondary = Some(String.Join(",", bIds))
                          SecondaryTag = secondaryTag
                          PrimacyHeld = pinHeld
                          Choices = choices
                          Hint = ApplyHint.empty }

                    baseIds |> List.map recurseChild

        // rebuild from merged facets
        let mergedKind =
            match withChildren (childlessKind kindSource) mergedChildren with
            | Some k -> k
            | None -> childlessKind kindSource

        { baseN with
            Kind = mergedKind
            Style =
                (if mergedStyle = Defaults.style then
                     None
                 else
                     Some mergedStyle)
            State = mergedState
            Accessibility = mergedAcc
            Tooltip = mergedTooltip }

    /// 3-way merge under **per-cell** authorship: `cellAuthor nodeId facet` gives
    /// the `(A-side, B-side)` authorship of THAT cell — the last writer of the
    /// cell on each branch (a backward DAG walk, `DagPrimacy`), not the branch
    /// tip. This is the precedence refinement: a primacy pin survives a later
    /// secondary edit to a *different* cell on the same branch. Returns the merged tree
    /// on full auto-merge, or the `MergeConflict` recovery envelopes. Deterministic
    /// + host-reproducible (NodeId-byte tie-break, no wall-clock).
    let merge3WayWithCellAuthor<'Msg>
        (cellAuthor: string -> string -> MergeAuthor * MergeAuthor)
        (baseTree: Node<'Msg>)
        (a: Node<'Msg>)
        (b: Node<'Msg>)
        : Result<Node<'Msg>, MergeConflict list> =
        let conflicts = ResizeArray<MergeConflict>()
        let merged = merge3 conflicts cellAuthor baseTree (Some a) (Some b)

        if conflicts.Count = 0 then
            Ok merged
        else
            Error(List.ofSeq conflicts)

    /// 3-way merge under a single per-side authorship pair (the per-branch-tip
    /// shape). Equivalent to `merge3WayWithCellAuthor` with a constant author
    /// function — retained for callers that have no per-cell provenance.
    let merge3WayWithAuthor<'Msg>
        (authorA: MergeAuthor)
        (authorB: MergeAuthor)
        (baseTree: Node<'Msg>)
        (a: Node<'Msg>)
        (b: Node<'Msg>)
        : Result<Node<'Msg>, MergeConflict list> =
        merge3WayWithCellAuthor (fun _ _ -> (authorA, authorB)) baseTree a b

    /// Author-agnostic 3-way merge (both sides `Secondary` — no precedence pin).
    /// The M1-shaped entry point: conflicts carry no `KeepPrimary` default.
    let merge3Way<'Msg>
        (baseTree: Node<'Msg>)
        (a: Node<'Msg>)
        (b: Node<'Msg>)
        : Result<Node<'Msg>, MergeConflict list> =
        merge3WayWithAuthor (MergeAuthor.Secondary None) (MergeAuthor.Secondary None) baseTree a b

    /// Lenient 3-way merge — always returns a tree, resolving any conflict to
    /// the BASE value (the merge fold already picks base on a non-pinned
    /// conflict). Deterministic + host-reproducible. Used to build a SYNTHETIC
    /// virtual-ancestor tree for recursive-base (criss-cross) merge, where a
    /// conflict must never block — the virtual base only feeds the real,
    /// conflict-surfacing merge above it.
    let merge3WayLenient<'Msg> (baseTree: Node<'Msg>) (a: Node<'Msg>) (b: Node<'Msg>) : Node<'Msg> =
        let conflicts = ResizeArray<MergeConflict>()

        merge3
            conflicts
            (fun _ _ -> (MergeAuthor.Secondary None, MergeAuthor.Secondary None))
            baseTree
            (Some a)
            (Some b)
