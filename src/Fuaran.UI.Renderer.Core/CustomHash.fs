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
let installCustomHashFloor (strictness: HashStrictness) : unit = floor <- strictness

/// The installed floor. Read-only introspection.
let currentCustomHashFloor () : HashStrictness = floor

/// Restore the default (`AdvisoryWarning`) floor — host teardown and test
/// isolation, mirroring `Render.clearGuestSeam`.
let clearCustomHashFloor () : unit = floor <- HashStrictness.AdvisoryWarning

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

/// Classify under the installed floor.
let classify (treeHash: ContentHash option) (registryHash: ContentHash option) : CustomHashOutcome =
    classifyUnder floor treeHash registryHash
