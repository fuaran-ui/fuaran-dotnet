namespace Fuaran.UI.LayoutObserver

// ─── What a repeated registration of one node id MEANS ──────────────────────
//
// The browser observer discovers addressable elements by re-walking
// `[data-fuaran-node-id]` on every DOM mutation, and re-registers everything it
// finds. That rescan is cheap and correct only if "register this id" can tell
// three different situations apart, and until this file it could tell two:
//
//   * the id is new                       — observe it, schedule a first read;
//   * the id is held, SAME element        — nothing happened, do nothing;
//   * the id is held, DIFFERENT element   — the node REMOUNTED.
//
// The old guard was `if not (registry.ContainsKey nodeId)`, which folds the
// last two together and answers "already registered" to both. A React remount
// under a stable id — a `Switch` case coming back, a keyed row re-created, a
// modal reopening — therefore left the registry pointing at the DETACHED
// element for as long as the id lived. `getBoundingClientRect` on a detached
// node is all zeroes, so every subsequent observation reported the node as 0×0
// and off-screen: an addressable element the AI channel could see, describing
// a box that had not existed since the remount.
//
// Identity, not equality. Two DOM elements are never `=` in any useful sense
// and Fable's structural equality on a browser object is not something to lean
// on, so the question asked is `ReferenceEquals` — is this the very object the
// registry holds? That is exactly the question, and it is the same one the
// `ResizeObserver` reverse-lookup already asks to map an entry back to its id.
//
// A separate compilation unit, outside the file's `#if FABLE_COMPILER` branch,
// so the rule is a fact the .NET suite can assert rather than one only a
// browser can. The style observer carries its own copy of this file: the two
// observer packages are deliberately independent — neither references the other
// and they version separately — and a shared package minted to hold one
// three-case DU would couple them for less than it cost.

/// What a `registerElement nodeId element` call means, given whatever the
/// registry already holds under that id.
[<RequireQualifiedAccess>]
type ElementRegistration =
    /// The registry does not hold this id. Observe the element and read it.
    | Fresh
    /// The registry holds this id and it names THE SAME element. A rescan
    /// re-offering what is already registered; nothing to do.
    | Unchanged
    /// The registry holds this id and it names a DIFFERENT element — the node
    /// remounted. The old element must be un-observed and dropped, the new one
    /// observed, and the change-detection caches cleared: they describe a node
    /// that no longer exists, and keeping them can suppress the first emission
    /// from the element that replaced it.
    | Remounted

module ElementRegistration =

    /// Classify a registration against what the registry currently holds.
    let classify (existing: obj option) (element: obj) : ElementRegistration =
        match existing with
        | None -> ElementRegistration.Fresh
        | Some current when System.Object.ReferenceEquals(current, element) -> ElementRegistration.Unchanged
        | Some _ -> ElementRegistration.Remounted
