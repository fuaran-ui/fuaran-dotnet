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

    [ "merge-refusal-concurrent-tone",
      "Both sides retone the same pane — a two-sided ConcurrentEdit refusal with no primacy pin",
      baseTree,
      toneA,
      toneB
      "merge-refusal-same-id-insert",
      "Both sides insert the same NodeId with different content — refused naming the id, never an A-side default",
      baseTree,
      sameIdA,
      sameIdB ]

/// The refusal ENVELOPE for a fixture. Fails loudly if the triad merges: a
/// refusal fixture that stopped refusing would otherwise be committed as an
/// empty envelope, which is a green fixture asserting nothing.
let envelopeOf (baseT: Node<TestMsg>) (a: Node<TestMsg>) (b: Node<TestMsg>) : MergeConflict list =
    match TreeMerge.merge3Way baseT a b with
    | Error conflicts -> conflicts
    | Ok merged -> failwithf "refusal merge-corpus fixture auto-merged: %s" (canonical merged)

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

    let manifest =
        "{\n  \"version\": 1,\n  \"description\": \"Fuaran merge-conformance corpus (Phase 179 + 184, additive). merge-3way: decode base/a/b, run the deterministic 3-way tree merge, assert byte-equal to expectedFile and sha256(expected) == outcomeHash. merge-validator-gated (Phase 184): run the documented sample validator over the auto-merge, diff introduced defects vs both parents, assert encodeVerdict(introduced) byte-equal to verdictFile and sha256(verdict) == verdictHash. See fuaran-dotnet/docs/WIRE_FORMAT.md.\",\n  \"fixtures\": [\n"
        + System.String.Join(",\n", entries)
        + "\n  ],\n  \"refusalDescription\": \"merge-refusal (Phase 1497, additive, SEPARATE key): decode base/a/b, run the 3-way merge, assert it REFUSES, and assert the canonically-encoded two-sided conflict envelope is byte-equal to envelopeFile with sha256(envelope) == envelopeHash. Swapping a and b must transpose each entry's 'a' and 'b' and change nothing else. Held under its own key because a host that iterates 'fixtures' expecting every entry to auto-merge is correct to do so. A side's 'value' is the contended cell's canonical encoding, EXCEPT for the style.* sub-facets, whose value is the sub-field's case name — that coincides with its wire spelling because every style sub-field is enum-shaped, and a host must not generalise it to a compound cell.\",\n  \"refusalFixtures\": [\n"
        + System.String.Join(",\n", refusalEntries)
        + "\n  ]\n}\n"

    File.WriteAllText(Path.Combine(dir, "manifest.json"), manifest)
