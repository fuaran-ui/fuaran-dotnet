module Fuaran.UI.OpStream.Dag.Tests.MergeCorpus

open System.IO
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Merge
open Fuaran.UI.OpStream.Dag.Tests.TestSupport

// ============================================================================
//  Merge-conformance corpus (Phase 179, additive).
//
//  Each fixture is a `(base, A, B) → expected merged tree + outcome hash` triad
//  exercising the DETERMINISTIC tree-merge primitive `TreeMerge.merge3Way`
//  (auto-merge cases only — disjoint edits, SemanticStyle sub-field blend, and
//  the NodeId-byte structural-insert tie-break). The committed `expected` bytes
//  + outcome hash ARE the F# merge output; both conformant hosts (F# + the TS
//  `@fuaran-ui/ops` merge port) must reproduce them byte-for-byte. The
//  recursive-base (criss-cross) reduction is a fold of this same primitive
//  (covered F#-side by the order-independence test), so a host-identical
//  tree-merge makes the recursive-base host-identical by construction.
//
//  Regenerate (from fuaran-dotnet/):
//      dotnet run --project src/Fuaran.UI.OpStream.Dag.Tests -- --emit-merge-corpus ..\wire-format-fixtures
// ============================================================================

let private style (f: SemanticStyle -> SemanticStyle) (id: NodeId) : TreeOp<TestMsg> =
    TreeOp.UpdateStyle(id, f Defaults.style)

/// `(id, description, base, a, b)` — closure-free trees so the corpus payloads
/// round-trip through the canonical encoder.
let fixtures: (string * string * Node<TestMsg> * Node<TestMsg> * Node<TestMsg>) list =
    let baseTree = buildDashboard ()

    // 1. Disjoint edits to different nodes.
    let disjointA =
        baseTree
        |> applyOk (style (fun s -> { s with Tone = ToneVariant.Brand }) leftChildId)

    let disjointB =
        baseTree
        |> applyOk (style (fun s -> { s with Tone = ToneVariant.Success }) rightChildId)

    // 2. SemanticStyle sub-field blend on the SAME node.
    let blendA =
        baseTree
        |> applyOk (style (fun s -> { s with Tone = ToneVariant.Brand }) leftChildId)

    let blendB =
        baseTree
        |> applyOk (style (fun s -> { s with Voice = FontVoice.Display }) leftChildId)

    // 3. Disjoint structural inserts (NodeId-byte tie-break).
    let insA =
        baseTree |> applyOk (TreeOp.InsertChild(dashboardId, Fuaran.markdown "zzz" "Z"))

    let insB =
        baseTree |> applyOk (TreeOp.InsertChild(dashboardId, Fuaran.markdown "aaa" "A"))

    [ "merge-disjoint", "Disjoint edits to different nodes (left tone vs right tone)", baseTree, disjointA, disjointB
      "merge-style-blend", "SemanticStyle sub-field blend (A tone + B voice on the same node)", baseTree, blendA, blendB
      "merge-insert-tiebreak", "Disjoint structural inserts, NodeId-byte tie-break", baseTree, insA, insB ]

/// The merged tree for a fixture (auto-merge — all corpus fixtures are
/// conflict-free by construction).
let mergedOf (baseT: Node<TestMsg>) (a: Node<TestMsg>) (b: Node<TestMsg>) : Node<TestMsg> =
    match TreeMerge.merge3Way baseT a b with
    | Ok merged -> merged
    | Error conflicts -> failwithf "merge-corpus fixture is not conflict-free: %A" conflicts

// ── validator-gated fixtures (Phase 184) ────────────────────────────────────
//
// A structurally-clean, NodeId-disjoint merge that nonetheless INTRODUCES a
// domain-validity defect is a semantic conflict. The deterministic artifact is
// the VERDICT — the introduced-defect set canonically encoded — not a merged
// tree. A conformant host (the TS `@fuaran-ui/ops` merge port, Leg B) ports the
// sample validator below + `encodeVerdict` and must reproduce the verdict bytes
// + hash. The validator invariant is intentionally tiny + host-portable.

/// The sample DOMAIN validator the gated fixtures certify against: "at most one
/// `Brand`-toned pane per dashboard". Each offending child is a defect on its
/// `style.tone` cell. A host MUST port this exact invariant to reproduce the
/// verdict.
let gatedValidator: MergeValidator<TestMsg> =
    fun tree ->
        match tree.Kind with
        | NodeKind.Box(spec) ->
            let brandKids =
                spec.Children
                |> List.filter (fun c -> (c.Style |> Option.defaultValue Defaults.style).Tone = ToneVariant.Brand)

            if List.length brandKids > 1 then
                brandKids
                |> List.map (fun c ->
                    { Code = "TESTBRAND001"
                      NodeId = c.Id
                      Facet = "style.tone"
                      Message =
                        sprintf
                            "Pane '%s' shares Brand tone with a sibling — at most one Brand pane per dashboard."
                            c.Id })
            else
                []
        | _ -> []

/// `(id, description, base, a, b)` triads whose disjoint structural merge
/// INTRODUCES a defect under `gatedValidator` (present in the merged tree but in
/// neither parent).
let gatedFixtures: (string * string * Node<TestMsg> * Node<TestMsg> * Node<TestMsg>) list =
    let baseTree = buildDashboard ()

    // base: neither pane Brand. A makes LEFT Brand, B makes RIGHT Brand — each
    // branch alone is legal (one Brand pane); the merge has two (the invariant
    // violation the merge introduced).
    let brandA =
        baseTree
        |> applyOk (style (fun s -> { s with Tone = ToneVariant.Brand }) leftChildId)

    let brandB =
        baseTree
        |> applyOk (style (fun s -> { s with Tone = ToneVariant.Brand }) rightChildId)

    [ "merge-validator-gated-brand",
      "Disjoint Brand-tone edits to sibling panes — the merge introduces a duplicate-Brand sibling-invariant violation",
      baseTree,
      brandA,
      brandB ]

/// The introduced-defect VERDICT for a gated fixture: auto-merge structurally,
/// then diff the merged tree's defects against both parents'.
let verdictOf (baseT: Node<TestMsg>) (a: Node<TestMsg>) (b: Node<TestMsg>) : MergeDefect list =
    match TreeMerge.merge3Way baseT a b with
    | Ok merged -> ValidatorGate.introducedDefects gatedValidator a b merged
    | Error conflicts -> failwithf "gated merge-corpus fixture is not structurally clean: %A" conflicts

// ── refusal fixtures (Phase 1497) ───────────────────────────────────────────
//
// Until 1497 the corpus committed only what a merge PRODUCED. What a merge
// REFUSES is equally a cross-host contract — a host that resolves a conflict is
// resolving against the envelope's contents — and it was pinned nowhere. It
// could not usefully be pinned before, either: the envelope recorded one side's
// value and which one depended on the argument order, so any committed bytes
// would have been a fixture for one arrival order.
//
// The deterministic artefact is the ENVELOPE — the refusal set canonically
// encoded by `MergeConflict.encodeEnvelope` — plus its hash, exactly as the
// gated family's artefact is the verdict.

/// `(id, description, base, a, b)` triads that REFUSE. Both arrival orders are
/// asserted by the conformance leg; the committed bytes are the forward order.
let refusalFixtures: (string * string * Node<TestMsg> * Node<TestMsg> * Node<TestMsg>) list =
    let baseTree = buildDashboard ()

    // 1. The canonical concurrent edit: both sides retone the SAME pane.
    let toneA =
        baseTree
        |> applyOk (style (fun s -> { s with Tone = ToneVariant.Brand }) leftChildId)

    let toneB =
        baseTree
        |> applyOk (style (fun s -> { s with Tone = ToneVariant.Critical }) leftChildId)

    // 2. Both sides insert the SAME id with DIFFERENT content. Before Phase 1497
    //    the disjointness test made this unreachable — it fell out as a
    //    whole-parent `ReorderVsStructural` refusal — and the shared-children
    //    guard reaches it, so the content check beneath that guard is what keeps
    //    it a refusal instead of an arrival-order-dependent A-side tree.
    let sameIdA =
        baseTree
        |> applyOk (TreeOp.InsertChild(dashboardId, Fuaran.markdown "new" "A wrote this"))

    let sameIdB =
        baseTree
        |> applyOk (TreeOp.InsertChild(dashboardId, Fuaran.markdown "new" "B wrote this"))

    // 3. Phase 1647 — the ONE style sub-facet whose case name is not its wire
    //    spelling. `TextDirection` is `Auto` / `Ltr` / `Rtl` in the vocabulary
    //    and `auto` / `ltr` / `rtl` on the wire; every other style sub-field's
    //    two spellings coincide. The manifest's `refusalDescription` asserted
    //    that coincidence as a RULE, which was true of five sub-fields and false
    //    of this one — and no fixture reached it, so both the Rust and the
    //    TypeScript hosts matched the reference's case-name spelling from the
    //    reference's own source and each recorded the guess in a comment. Three
    //    implementations agreeing because they read one another is not a pinned
    //    contract. This fixture is what pins it: whatever the envelope's side
    //    values spell for `style.direction`, the committed bytes now say so, and
    //    a host that spells it the other way fails here rather than agreeing
    //    with everyone about something nobody checked.
    let directionA =
        baseTree
        |> applyOk (style (fun s -> { s with Direction = TextDirection.Ltr }) leftChildId)

    let directionB =
        baseTree
        |> applyOk (style (fun s -> { s with Direction = TextDirection.Rtl }) leftChildId)

    [ "merge-refusal-concurrent-tone",
      "Both sides retone the same pane — a two-sided ConcurrentEdit refusal with no primacy pin",
      baseTree,
      toneA,
      toneB
      "merge-refusal-same-id-insert",
      "Both sides insert the same NodeId with different content — refused naming the id, never an A-side default",
      baseTree,
      sameIdA,
      sameIdB
      "merge-refusal-concurrent-direction",
      "Both sides set a different text direction on the same pane — the one style sub-facet whose case name (Ltr/Rtl) is not its wire spelling (ltr/rtl), so the envelope's side values are pinned rather than described",
      baseTree,
      directionA,
      directionB ]

/// The refusal ENVELOPE for a fixture. Fails loudly if the triad merges: a
/// refusal fixture that stopped refusing would otherwise be committed as an
/// empty envelope, which is a green fixture asserting nothing.
let envelopeOf (baseT: Node<TestMsg>) (a: Node<TestMsg>) (b: Node<TestMsg>) : MergeConflict list =
    match TreeMerge.merge3Way baseT a b with
    | Error conflicts -> conflicts
    | Ok merged -> failwithf "refusal merge-corpus fixture auto-merged: %s" (canonical merged)

// ── totality fixtures (Phase 1526) ──────────────────────────────────────────
//
// Merge behaviour a host can silently OMIT and still look conformant, pinned so
// it cannot. Three pairs:
//
//  - `DeleteModify` — one side edits a node the other REMOVES. Declared since
//    Phase 179 with a documented projection onto `ApplyErrorCode`, constructed
//    by nothing; the merge auto-merged and the edit went with the node.
//  - `ConcurrentMove` — a node relocated by one side and moved or edited by the
//    other. Same story: the mover's subtree was adopted wholesale in silence.
//  - `tooltip` — the node-level trait the reference merges as a facet of its own
//    (Phase 1112). No fixture reached it in any family, and at least one host's
//    merge constructs its nodes with the trait hardcoded empty, so it drops an
//    uncontested hint and no corpus entry notices.
//
// Each is committed as a PAIR: the triad that must refuse, and a corrected twin
// that must still auto-merge. The twin is not decoration. A host can pass the
// refusal by refusing everything structural, and can pass an auto-merge suite by
// never growing the arm at all; only the pair pins the boundary between them.
// The tooltip pair makes the point most sharply: its twin is where a host that
// drops the trait fails, because the merged tree it produces is a DIFFERENT tree.
//
// Held under a THIRD top-level manifest key for the reason `refusalFixtures` is
// held under a second one: a host iterating `fixtures` and asserting every entry
// auto-merges is right to, and a host iterating `refusalFixtures` and asserting
// every entry refuses is right too. A pair belongs to neither. A new key is
// invisible to every host that does not read it, so each adopts the wider
// contract when it ports the arm, and until then its merge leg is exactly as
// green as it was.

let private container (id: string) (children: Node<TestMsg> list) : Node<TestMsg> =
    Fuaran.dashboard
        id
        { Defaults.dashboard<TestMsg> with
            Children = children }

/// `dash → [ boxa → [ m ], boxb ]` — the shape a MOVE needs. The flat two-pane
/// genesis the other families use cannot express one: a node has to have
/// somewhere else to go.
let private nestedTree () : Node<TestMsg> =
    container "dash" [ container "boxa" [ Fuaran.markdown "m" "Movable" ]; container "boxb" [] ]

let private tone (t: ToneVariant) (id: NodeId) : TreeOp<TestMsg> = style (fun s -> { s with Tone = t }) id

/// Set a node's `Tooltip` by id. There is no `TreeOp` that sets the trait — the
/// vocabulary gap `TreeOpDiff` documents — so a fixture that exercises it has to
/// build its trees directly rather than by applying ops.
let private withTooltip (targetId: string) (hint: string) (root: Node<TestMsg>) : Node<TestMsg> =
    let rec go (n: Node<TestMsg>) : Node<TestMsg> =
        let self =
            if n.Id = targetId then
                { n with
                    Tooltip = Some(TextSource.Literal hint) }
            else
                n

        match Fuaran.UI.Ops.Introspect.getChildren self.Kind with
        | None -> self
        | Some kids ->
            match Fuaran.UI.Ops.Introspect.withChildren self.Kind (kids |> List.map go) with
            | Some k -> { self with Kind = k }
            | None -> self

    go root

/// `(refusalId, refusalDescription, twinId, twinDescription, base, refusalA,
/// refusalB, twinA, twinB)`. The refusal and its twin share a base tree, so the
/// only thing that differs between "refuses" and "merges" is the one edit the
/// class is about.
let totalityPairs
    : (string * string * string * string * Node<TestMsg> * Node<TestMsg> * Node<TestMsg> * Node<TestMsg> * Node<TestMsg>) list =
    let flat = buildDashboard ()
    let nested = nestedTree ()

    [ "merge-totality-delete-modify",
      "One side restyles the right pane while the other removes it — a DeleteModify refusal naming the removed node, where the pre-1526 merge dropped the edit with the node",
      "merge-totality-delete-modify-twin",
      "The same removal with the edit moved to a pane nobody removed — auto-merges, so a host cannot pass the pair by refusing every removal",
      flat,
      flat |> applyOk (tone ToneVariant.Success rightChildId),
      flat |> applyOk (TreeOp.RemoveNode rightChildId),
      flat |> applyOk (tone ToneVariant.Success leftChildId),
      flat |> applyOk (TreeOp.RemoveNode rightChildId)

      "merge-totality-concurrent-move",
      "One side moves a node to another parent while the other restyles it in place — a ConcurrentMove refusal carrying both POSITIONS (the `move` facet) and both SUBTREES (the `node` facet), where the pre-1526 merge adopted the mover's copy and discarded the edit",
      "merge-totality-concurrent-move-twin",
      "The same move with the edit moved to the source container itself — auto-merges, so a host cannot pass the pair by refusing every move",
      nested,
      nested |> applyOk (TreeOp.MoveNode(NodeId "m", NodeId "boxb")),
      nested |> applyOk (tone ToneVariant.Brand (NodeId "m")),
      nested |> applyOk (TreeOp.MoveNode(NodeId "m", NodeId "boxb")),
      nested |> applyOk (tone ToneVariant.Brand (NodeId "boxa"))

      "merge-totality-tooltip",
      "Both sides set a different tooltip on the same pane — a ConcurrentEdit refusal on the `tooltip` facet, which the reference merges independently (Phase 1112) and which no fixture in any other family reaches",
      "merge-totality-tooltip-twin",
      "One side sets a tooltip while the other restyles a different pane — auto-merges, and the merged tree CARRIES the hint. This is where a host whose merge constructs its nodes with the trait hardcoded empty fails: it produces a different tree, not a different envelope",
      flat,
      flat |> withTooltip "left" "A hint",
      flat |> withTooltip "left" "B hint",
      flat |> withTooltip "left" "A hint",
      flat |> applyOk (tone ToneVariant.Success rightChildId) ]

let emit (root: string) : unit =
    let dir = Path.Combine(root, "merge-conformance")
    Directory.CreateDirectory dir |> ignore
    let entries = ResizeArray<string>()

    for (id, description, baseT, a, b) in fixtures do
        let merged = mergedOf baseT a b
        let outcomeHash = CanonicalJson.encodeNode merged |> HashChain.sha256Hex
        File.WriteAllText(Path.Combine(dir, id + ".base.json"), CanonicalJson.encodeNode baseT)
        File.WriteAllText(Path.Combine(dir, id + ".a.json"), CanonicalJson.encodeNode a)
        File.WriteAllText(Path.Combine(dir, id + ".b.json"), CanonicalJson.encodeNode b)
        File.WriteAllText(Path.Combine(dir, id + ".expected.json"), CanonicalJson.encodeNode merged)

        entries.Add(
            sprintf
                "    {\n      \"id\": \"%s\",\n      \"kind\": \"merge-3way\",\n      \"baseFile\": \"%s.base.json\",\n      \"aFile\": \"%s.a.json\",\n      \"bFile\": \"%s.b.json\",\n      \"expectedFile\": \"%s.expected.json\",\n      \"outcomeHash\": \"%s\",\n      \"description\": \"%s\"\n    }"
                id
                id
                id
                id
                id
                outcomeHash
                description
        )

    // validator-gated (Phase 184): the deterministic artifact is the verdict
    // (the introduced-defect set canonically encoded) + its hash.
    for (id, description, baseT, a, b) in gatedFixtures do
        let verdictJson = verdictOf baseT a b |> ValidatorGate.encodeVerdict
        let verdictHash = verdictJson |> HashChain.sha256Hex
        File.WriteAllText(Path.Combine(dir, id + ".base.json"), CanonicalJson.encodeNode baseT)
        File.WriteAllText(Path.Combine(dir, id + ".a.json"), CanonicalJson.encodeNode a)
        File.WriteAllText(Path.Combine(dir, id + ".b.json"), CanonicalJson.encodeNode b)
        File.WriteAllText(Path.Combine(dir, id + ".verdict.json"), verdictJson)

        entries.Add(
            sprintf
                "    {\n      \"id\": \"%s\",\n      \"kind\": \"merge-validator-gated\",\n      \"baseFile\": \"%s.base.json\",\n      \"aFile\": \"%s.a.json\",\n      \"bFile\": \"%s.b.json\",\n      \"verdictFile\": \"%s.verdict.json\",\n      \"verdictHash\": \"%s\",\n      \"description\": \"%s\"\n    }"
                id
                id
                id
                id
                id
                verdictHash
                description
        )

    // Refusal envelopes (Phase 1497) live under their OWN manifest key rather
    // than beside the auto-merge triads in `fixtures`. Deliberate, and the reason
    // is what a host does with an unknown entry: the Go leg iterates every
    // `fixtures` entry and asserts the merge SUCCEEDS before it looks at `kind`,
    // so a refusal triad added there would turn a conformant host red for
    // modelling the corpus correctly. A new top-level key is invisible to every
    // host that does not read it, so each host adopts the wider envelope when it
    // ports it, and until then its merge leg is exactly as green as it was.
    let refusalEntries = ResizeArray<string>()

    for (id, description, baseT, a, b) in refusalFixtures do
        let envelopeJson = envelopeOf baseT a b |> MergeConflict.encodeEnvelope
        let envelopeHash = envelopeJson |> HashChain.sha256Hex
        File.WriteAllText(Path.Combine(dir, id + ".base.json"), CanonicalJson.encodeNode baseT)
        File.WriteAllText(Path.Combine(dir, id + ".a.json"), CanonicalJson.encodeNode a)
        File.WriteAllText(Path.Combine(dir, id + ".b.json"), CanonicalJson.encodeNode b)
        File.WriteAllText(Path.Combine(dir, id + ".envelope.json"), envelopeJson)

        refusalEntries.Add(
            sprintf
                "    {\n      \"id\": \"%s\",\n      \"kind\": \"merge-refusal\",\n      \"baseFile\": \"%s.base.json\",\n      \"aFile\": \"%s.a.json\",\n      \"bFile\": \"%s.b.json\",\n      \"envelopeFile\": \"%s.envelope.json\",\n      \"envelopeHash\": \"%s\",\n      \"description\": \"%s\"\n    }"
                id
                id
                id
                id
                id
                envelopeHash
                description
        )

    // Totality PAIRS (Phase 1526) — each a refusal entry and the corrected twin
    // that must still auto-merge, emitted adjacently so a reader sees the
    // boundary rather than two unrelated fixtures.
    let totalityEntries = ResizeArray<string>()

    for (rid, rdesc, tid, tdesc, baseT, ra, rb, ta, tb) in totalityPairs do
        let envelopeJson = envelopeOf baseT ra rb |> MergeConflict.encodeEnvelope
        let envelopeHash = envelopeJson |> HashChain.sha256Hex
        File.WriteAllText(Path.Combine(dir, rid + ".base.json"), CanonicalJson.encodeNode baseT)
        File.WriteAllText(Path.Combine(dir, rid + ".a.json"), CanonicalJson.encodeNode ra)
        File.WriteAllText(Path.Combine(dir, rid + ".b.json"), CanonicalJson.encodeNode rb)
        File.WriteAllText(Path.Combine(dir, rid + ".envelope.json"), envelopeJson)

        let twinMerged = mergedOf baseT ta tb
        let twinHash = CanonicalJson.encodeNode twinMerged |> HashChain.sha256Hex
        File.WriteAllText(Path.Combine(dir, tid + ".base.json"), CanonicalJson.encodeNode baseT)
        File.WriteAllText(Path.Combine(dir, tid + ".a.json"), CanonicalJson.encodeNode ta)
        File.WriteAllText(Path.Combine(dir, tid + ".b.json"), CanonicalJson.encodeNode tb)
        File.WriteAllText(Path.Combine(dir, tid + ".expected.json"), CanonicalJson.encodeNode twinMerged)

        totalityEntries.Add(
            sprintf
                "    {\n      \"id\": \"%s\",\n      \"kind\": \"merge-refusal\",\n      \"baseFile\": \"%s.base.json\",\n      \"aFile\": \"%s.a.json\",\n      \"bFile\": \"%s.b.json\",\n      \"envelopeFile\": \"%s.envelope.json\",\n      \"envelopeHash\": \"%s\",\n      \"twin\": \"%s\",\n      \"description\": \"%s\"\n    }"
                rid
                rid
                rid
                rid
                rid
                envelopeHash
                tid
                rdesc
        )

        totalityEntries.Add(
            sprintf
                "    {\n      \"id\": \"%s\",\n      \"kind\": \"merge-3way\",\n      \"baseFile\": \"%s.base.json\",\n      \"aFile\": \"%s.a.json\",\n      \"bFile\": \"%s.b.json\",\n      \"expectedFile\": \"%s.expected.json\",\n      \"outcomeHash\": \"%s\",\n      \"refusal\": \"%s\",\n      \"description\": \"%s\"\n    }"
                tid
                tid
                tid
                tid
                tid
                twinHash
                rid
                tdesc
        )

    let manifest =
        "{\n  \"version\": 1,\n  \"description\": \"Fuaran merge-conformance corpus (Phase 179 + 184, additive). merge-3way: decode base/a/b, run the deterministic 3-way tree merge, assert byte-equal to expectedFile and sha256(expected) == outcomeHash. merge-validator-gated (Phase 184): run the documented sample validator over the auto-merge, diff introduced defects vs both parents, assert encodeVerdict(introduced) byte-equal to verdictFile and sha256(verdict) == verdictHash. See fuaran-dotnet/docs/WIRE_FORMAT.md.\",\n  \"fixtures\": [\n"
        + System.String.Join(",\n", entries)
        + "\n  ],\n  \"refusalDescription\": \"merge-refusal (Phase 1497, additive, SEPARATE key): decode base/a/b, run the 3-way merge, assert it REFUSES, and assert the canonically-encoded two-sided conflict envelope is byte-equal to envelopeFile with sha256(envelope) == envelopeHash. Swapping a and b must transpose each entry's 'a' and 'b' and change nothing else. Held under its own key because a host that iterates 'fixtures' expecting every entry to auto-merge is correct to do so. A side's 'value' is the contended cell's canonical WIRE encoding, and that holds for the style.* sub-facets too — a sub-facet's value is the sub-field's wire token, never its vocabulary case name. Until Phase 1647 this said the value was the CASE NAME, adding that the two coincide 'because every style sub-field is enum-shaped'. Both halves were wrong, and the second hid the first: five sub-fields do spell the two identically, so the description read as true against every fixture that existed, while style.direction spells them differently (cases Auto/Ltr/Rtl, wire auto/ltr/rtl) and no fixture reached it. Two hosts had matched a case-name spelling read from the reference's source rather than from any committed byte. merge-refusal-concurrent-direction is the fixture that decides it, and it emits 'ltr'/'rtl'. A host must not generalise a sub-facet value to a compound cell either.\",\n  \"refusalFixtures\": [\n"
        + System.String.Join(",\n", refusalEntries)
        + "\n  ],\n  \"totalityDescription\": \"merge-totality (Phase 1526, additive, SEPARATE key): each PAIR is a triad that must REFUSE, immediately followed by a corrected twin that must AUTO-MERGE. The refusal entry carries an envelopeFile + envelopeHash and is checked exactly as a `merge-refusal` entry is; the twin carries an expectedFile + outcomeHash and is checked exactly as a `merge-3way` entry is; `twin` and `refusal` cross-reference the two. The pair is the point: a host can pass the refusal by refusing every structural merge, and can pass an auto-merge suite by never growing the arm, and only the pair pins the boundary between them. Three cases are covered, each one merge behaviour a host can silently OMIT and still look conformant: DeleteModify (one side edits a node the other REMOVES) and ConcurrentMove (a node relocated by one side and moved or edited by the other), both declared since Phase 179 and constructed by nothing until 1526 — so a host that reproduced the reference exactly reproduced a silent loss; and the node-level `tooltip` trait, which the reference merges as a facet of its own since Phase 1112 and which no fixture in any other family reaches, so a host whose merge constructs its nodes with the trait hardcoded empty drops an uncontested hint and nothing notices. The tooltip pair fails on its TWIN rather than its refusal, because a dropped trait is a different TREE and not a different envelope. Two envelope spellings are specific to these classes and a host must not generalise either: a side that holds NO value for the cell — the removing side of a DeleteModify — carries the EMPTY STRING, which no node canonical-encodes to and which `base` already uses for a same-id insert; and a ConcurrentMove is TWO entries on one node, `move` whose side value is the parent id that side holds the node under (empty string at the root) and `node` whose side value is that side's canonical subtree, never one entry compounding position and content. Held under its own key for the reason refusalFixtures is: a host that iterates `fixtures` expecting every entry to auto-merge is correct to do so, and one that iterates `refusalFixtures` expecting every entry to refuse is correct too.\",\n  \"totalityFixtures\": [\n"
        + System.String.Join(",\n", totalityEntries)
        + "\n  ]\n}\n"

    File.WriteAllText(Path.Combine(dir, "manifest.json"), manifest)
