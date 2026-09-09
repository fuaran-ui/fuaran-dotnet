module Fuaran.UI.Renderer.CustomHash

// ============================================================================
//  Fuaran — `NodeKind.Custom` content-hash verification policy (Phase 783).
//
//  `ContentHash` is DRIFT DETECTION between a registered renderer and a
//  replayed tree. It is not, and cannot be, authentication of the tree: the
//  tree supplies its own hash record, so a hash that matches proves only that
//  whoever wrote the tree knew the registered renderer's hash. That distinction
//  was not stated anywhere before Phase 783, and the implementation quietly
//  assumed the stronger reading.
//
//  Two concrete bypasses followed from that assumption:
//
//    1. **Omit the hash.** `treeHash = None` classified as `NoTreeHash`, which
//       shared a render branch with `Match` and rendered SILENTLY. The cheapest
//       way past verification was to skip it.
//    2. **Declare a lenient strictness.** Strictness was read from the TREE's own
//       `ContentHash` record, so an attacker who did declare a hash simply chose
//       `AdvisoryWarning` and got warn-then-render on a mismatch.
//
//  The fix is a HOST-CONFIGURED FLOOR that a tree may only tighten:
//
//    - the host installs a minimum strictness (`installCustomHashFloor`);
//    - a tree's declared strictness raises it, never lowers it;
//    - under an ENFORCING floor, a hash that cannot be verified — because the
//      tree declared none, or the registry recorded none — is a REFUSAL rather
//      than a render.
//
//  The default floor stays `AdvisoryWarning`, i.e. today's behaviour: a tree
//  with no hash is the common legitimate case, and an enforcing default would
//  refuse most existing `Custom` nodes on upgrade. What Phase 783 changes is
//  that a host CAN enforce, and that a tree cannot talk its way underneath the
//  host's choice.
//
//  ── THE DEFAULT FLIPS TO ENFORCED (Phase 1550) ─────────────────────────────
//
//  That paragraph is the posture the dispatch gate held until 0.14.0, and the
//  argument that flipped it applies here unchanged: the SHIPPED DEFAULT is what
//  an unconfigured host receives, so a default that allows is the gate for
//  exactly the hosts that never configured one. `DefaultCustomHashFloor` is now
//  `Enforced`, and the permissive posture is reached by name.
//
//  The upgrade objection above is answered rather than overruled, by separating
//  the two things the old default conflated:
//
//    - the floor governs MISMATCH. A tree that declares a hash which disagrees
//      with the registered renderer's is refused by an unconfigured host.
//    - the floor does NOT govern tree-side ABSENCE. A tree that declares no hash
//      renders exactly as it did, because there is nothing to mismatch and
//      "most existing `Custom` nodes" are that shape. Only `StrictReplay` — the
//      floor whose NAME says the tree must replay exactly, and which cannot be
//      satisfied by a tree carrying no hash at all — refuses it. So nothing a
//      783-hardened host had is lost; it moves from "any enforcing floor" to
//      "the strictly-replaying one".
//
//  Registry-side absence is untouched: a tree that DID declare a hash the
//  registry cannot verify has made a claim that cannot be checked, which is a
//  verification failure rather than an absence, and stays `Unverifiable` under
//  any enforcing floor.
//
//  This module lives in the emission-agnostic core because BOTH renderers need
//  the same verdict from the same floor — the client `Custom` arm and the
//  server one. One definition, no drift.
// ============================================================================

open Fuaran.UI.Types

/// The verdict for one `Custom` node's hash position.
[<RequireQualifiedAccess>]
type CustomHashOutcome =
    /// The tree declared no hash and the floor is not enforcing — render.
    | NoTreeHash
    /// Declared and registered hashes agree — render.
    | Match
    /// The tree declared a hash, the registry recorded none, and the floor is
    /// not enforcing — warn, then render.
    | RegistryNoHash
    /// Mismatch under a non-enforcing effective strictness — warn, then render.
    | MismatchAdvisory
    /// Mismatch under an enforcing effective strictness — refuse.
    | MismatchStrict
    /// Verification could not be performed at all AND the floor is enforcing —
    /// refuse. Covers both "the tree declared no hash" and "the registry
    /// recorded none": under enforcement an unverifiable render is a failure,
    /// not a default.
    | Unverifiable

// The order of the raise-only lattice: a floor may be replaced only by one that
// refuses at least as much.
//
// `Enforced` and `StrictReplay` shared rank 1 until Phase 1550, on the reading
// that `Enforced` is primarily a build-time gate (validator FUARAN062) and that
// reaching a renderer the two were equally strict. That was true while both
// arms of the verdict agreed. It stopped being true when the floor's job
// narrowed to MISMATCH: `StrictReplay` now refuses a strict SUPERSET — every
// mismatch `Enforced` refuses, plus the tree that declares no hash at all — so
// the two must be ordered, or a render context declaring `StrictReplay` cannot
// raise above the shipped `Enforced` default and its declaration is silently
// discarded.
let private strictnessRank (s: HashStrictness) : int =
    match s with
    | HashStrictness.AdvisoryWarning -> 0
    | HashStrictness.Enforced -> 1
    | HashStrictness.StrictReplay -> 2

/// True when `s` refuses rather than warns.
let isEnforcing (s: HashStrictness) : bool = strictnessRank s > 0

/// True when `s` refuses a `Custom` node whose hash cannot be verified because
/// THE TREE DECLARED NONE — as distinct from refusing a mismatch (Phase 1550).
///
/// Only `StrictReplay` does. The distinction is the whole of what makes an
/// enforcing default shippable: a tree with no hash is the common legitimate
/// case, so a default that refused it would refuse most existing `Custom` nodes
/// the moment a host upgraded, and the floor's job is to catch a hash that
/// DISAGREES. `StrictReplay` keeps the stronger reading because its name is the
/// stronger claim — a tree that must replay exactly cannot do so carrying no
/// hash — so the posture Phase 783 gave an enforcing host is still reachable,
/// by that name.
///
/// This is the arm `strictnessRank` was re-ordered FOR (see the note there):
/// because `StrictReplay` refuses a strict superset of what `Enforced` refuses,
/// it now outranks it, so a declaration of `StrictReplay` raises a process or a
/// context above the shipped default rather than being discarded as no
/// stricter. The predicate stays a predicate because it is what the verdict
/// reads — `isEnforcing` answers "does this floor refuse a MISMATCH", this one
/// answers "does it also refuse an ABSENCE", and a rank comparison at each call
/// site would say neither.
let refusesUnverifiable (s: HashStrictness) : bool =
    match s with
    | HashStrictness.StrictReplay -> true
    | HashStrictness.AdvisoryWarning
    | HashStrictness.Enforced -> false

/// The floor an unconfigured host receives (Phase 1550): **enforcing**. A
/// mismatch between a tree's declared hash and the registered renderer's is
/// refused with no host configuration at all; a tree carrying no hash renders
/// as it always did (see [[refusesUnverifiable]]).
let DefaultCustomHashFloor: HashStrictness = HashStrictness.Enforced

// The host's own DECLARATION, distinct from the default above. `None` is "no
// host has said anything", which resolves to `DefaultCustomHashFloor`; `Some s`
// is a declaration, and declarations compose raise-only (Phase 1532).
//
// A `HashStrictness option` rather than a plain mutable seeded with the default,
// because the two are different facts and the flip made the difference matter.
// With a raise-only setter over a plain mutable, an ENFORCING default would make
// `AdvisoryWarning` unreachable — the permissive posture would exist in the type
// and in no reachable program — so the opt-back this phase promises would not
// exist. Separating them keeps every Phase 1532 property (a second, weaker
// declaration cannot lower an installed floor; the strictest declaration wins
// whatever order they arrive in) while leaving a FIRST declaration free to name
// the permissive posture.
let mutable private declaredFloor: HashStrictness option = None

/// Install the host's MINIMUM `Custom` content-hash strictness. A tree's own
/// declared strictness may raise this, never lower it. Under an enforcing floor
/// a `Custom` node whose declared hash MISMATCHES the registered renderer's is
/// refused rather than rendered.
///
/// **The shipped default is `Enforced` since Phase 1550**, so a host calls this
/// to CHANGE the posture, not to obtain one. Two directions, both by name:
/// `StrictReplay` additionally refuses a tree that declares no hash at all
/// (see [[refusesUnverifiable]]), and `AdvisoryWarning` is the permissive
/// opt-back — a mismatch warns and renders, exactly as every host received it
/// before the flip. One `grep` for `installCustomHashFloor` therefore
/// enumerates every place the permissive posture is in force, which is the
/// property the Phase 782 `permissive` runtimes were named for.
///
/// Process-global, like `StateStore`'s default instance and the renderer's
/// guest seam, and for the same reason: the installing host lives in another
/// assembly and there is one policy per process.
///
/// **RAISE-ONLY since Phase 1532, and this is the point of the mechanism rather
/// than a refinement of it.** The floor exists so a TREE cannot talk its way
/// underneath the host's choice; until this phase the SETTER could, because it
/// assigned. On a tier that serves more than one tenant from one process — which
/// is every SSR deployment — a second `installCustomHashFloor AdvisoryWarning`
/// anywhere in the process lowered the floor for everyone, and the enforcing
/// tenant's `Custom` nodes went back to rendering on an unverifiable hash with
/// nothing logged. A monotone install cannot do that: the strictest declaration
/// made in this process wins, whatever order the declarations arrive in, which
/// is also what makes the result independent of host initialisation order.
///
/// A host that needs a NARROWER floor for one render declares it on that
/// render's context instead (`RenderContext.CustomHashFloor`) — where it can
/// only raise, and where it cannot reach another tenant.
///
/// Raise-only applies among DECLARATIONS (Phase 1550). A first declaration is
/// free to name the permissive posture — that is the opt-back, and the only way
/// to reach it — but it cannot undo an enforcement another tenant has already
/// declared, which is the multi-tenant property Phase 1532 closed.
let installCustomHashFloor (strictness: HashStrictness) : unit =
    match declaredFloor with
    | Some current when strictnessRank strictness <= strictnessRank current -> ()
    | _ -> declaredFloor <- Some strictness

/// The floor in force: the host's declaration, or [[DefaultCustomHashFloor]]
/// when no host has made one. Read-only introspection.
let currentCustomHashFloor () : HashStrictness =
    match declaredFloor with
    | Some s -> s
    | None -> DefaultCustomHashFloor

/// Restore the shipped default floor by withdrawing the host's declaration.
///
/// **Test isolation only, and the name says so since Phase 1532.** It was
/// `clearCustomHashFloor`, sitting on the public surface beside the installer
/// and reading like ordinary host teardown — so the raise-only install above
/// would have been trivially defeated by the function next to it. There is no
/// legitimate host use: a process does not stop needing its strictest declared
/// floor, and a host that wants a narrower one for one render declares it on
/// that render's context. A suite that installs a floor restores it through
/// this; nothing else calls it.
///
/// It restores [[DefaultCustomHashFloor]] since Phase 1550 — it withdraws the
/// declaration rather than assigning a value, so there is no function anywhere
/// on this surface that puts the process below the shipped default. Reaching
/// the permissive posture is `installCustomHashFloor AdvisoryWarning`, and it
/// reads as the deliberate act it is.
let clearCustomHashFloorForTests () : unit = declaredFloor <- None

/// The effective floor for one render: the strictest of the process floor and
/// whatever that render's context declared. `None` — the default at every
/// convenience entry point — is the process floor unchanged.
///
/// RAISE-ONLY on this axis too, and for the same reason it is raise-only on the
/// installer: a per-render context is per-REQUEST on an SSR tier, so a context
/// that could lower would hand every tenant the ability to disable the host's
/// verification for its own documents, which is precisely the tree-side bypass
/// the floor exists to close.
let effectiveFloor (contextFloor: HashStrictness option) : HashStrictness =
    let processFloor = currentCustomHashFloor ()

    match contextFloor with
    | None -> processFloor
    | Some declared ->
        if strictnessRank declared > strictnessRank processFloor then
            declared
        else
            processFloor

/// Classify a `Custom` node's hash position under an explicit floor. Total and
/// pure, so every combination is pinnable in tests without a render.
let classifyUnder
    (hostFloor: HashStrictness)
    (treeHash: ContentHash option)
    (registryHash: ContentHash option)
    : CustomHashOutcome =
    match treeHash, registryHash with
    | None, _ ->
        // TREE-SIDE ABSENCE. Refused only under `StrictReplay` since Phase 1550
        // — the floor governs mismatch, and a tree that declares no hash has
        // nothing to mismatch. See [[refusesUnverifiable]] for why this arm and
        // the registry-side one below now answer to different predicates.
        if refusesUnverifiable hostFloor then
            CustomHashOutcome.Unverifiable
        else
            CustomHashOutcome.NoTreeHash
    | Some _, None ->
        // REGISTRY-SIDE ABSENCE, and deliberately not the same question. The
        // tree made a CLAIM the registry cannot check — a verification failure,
        // not an absence — so any enforcing floor still refuses it.
        if isEnforcing hostFloor then
            CustomHashOutcome.Unverifiable
        else
            CustomHashOutcome.RegistryNoHash
    | Some t, Some r ->
        if t.Algorithm = r.Algorithm && t.Hash = r.Hash then
            CustomHashOutcome.Match
        else
            // TIGHTEN-ONLY: the stricter of the host floor and the tree's own
            // declaration wins.
            let effective =
                if strictnessRank t.Strictness >= strictnessRank hostFloor then
                    t.Strictness
                else
                    hostFloor

            if isEnforcing effective then
                CustomHashOutcome.MismatchStrict
            else
                CustomHashOutcome.MismatchAdvisory

/// Classify under the installed process floor.
let classify (treeHash: ContentHash option) (registryHash: ContentHash option) : CustomHashOutcome =
    classifyUnder (currentCustomHashFloor ()) treeHash registryHash

/// Classify under the effective floor for one render — the strictest of the
/// process floor and the render context's own declaration (Phase 1532).
let classifyForRender
    (contextFloor: HashStrictness option)
    (treeHash: ContentHash option)
    (registryHash: ContentHash option)
    : CustomHashOutcome =
    classifyUnder (effectiveFloor contextFloor) treeHash registryHash
