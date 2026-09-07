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

let private strictnessRank (s: HashStrictness) : int =
    match s with
    | HashStrictness.AdvisoryWarning -> 0
    // `Enforced` is primarily a build-time gate (validator FUARAN062); reaching
    // a renderer it is as strict as `StrictReplay`.
    | HashStrictness.StrictReplay
    | HashStrictness.Enforced -> 1

/// True when `s` refuses rather than warns.
let isEnforcing (s: HashStrictness) : bool = strictnessRank s > 0

let mutable private floor: HashStrictness = HashStrictness.AdvisoryWarning

/// Install the host's MINIMUM `Custom` content-hash strictness. A tree's own
/// declared strictness may raise this, never lower it. Under an enforcing floor
/// a `Custom` node whose hash cannot be verified is refused rather than
/// rendered. Default `AdvisoryWarning` — the pre-0.15.0 behaviour.
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
let installCustomHashFloor (strictness: HashStrictness) : unit =
    if strictnessRank strictness > strictnessRank floor then
        floor <- strictness

/// The installed floor. Read-only introspection.
let currentCustomHashFloor () : HashStrictness = floor

/// Restore the default (`AdvisoryWarning`) floor.
///
/// **Test isolation only, and the name says so since Phase 1532.** It was
/// `clearCustomHashFloor`, sitting on the public surface beside the installer
/// and reading like ordinary host teardown — so the raise-only install above
/// would have been trivially defeated by the function next to it. There is no
/// legitimate host use: a process does not stop needing its strictest declared
/// floor, and a host that wants a narrower one for one render declares it on
/// that render's context. A suite that installs a floor restores it through
/// this; nothing else calls it.
let clearCustomHashFloorForTests () : unit = floor <- HashStrictness.AdvisoryWarning

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
    match contextFloor with
    | None -> floor
    | Some declared ->
        if strictnessRank declared > strictnessRank floor then
            declared
        else
            floor

/// Classify a `Custom` node's hash position under an explicit floor. Total and
/// pure, so every combination is pinnable in tests without a render.
let classifyUnder
    (hostFloor: HashStrictness)
    (treeHash: ContentHash option)
    (registryHash: ContentHash option)
    : CustomHashOutcome =
    match treeHash, registryHash with
    | None, _ ->
        if isEnforcing hostFloor then
            CustomHashOutcome.Unverifiable
        else
            CustomHashOutcome.NoTreeHash
    | Some _, None ->
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
    classifyUnder floor treeHash registryHash

/// Classify under the effective floor for one render — the strictest of the
/// process floor and the render context's own declaration (Phase 1532).
let classifyForRender
    (contextFloor: HashStrictness option)
    (treeHash: ContentHash option)
    (registryHash: ContentHash option)
    : CustomHashOutcome =
    classifyUnder (effectiveFloor contextFloor) treeHash registryHash
