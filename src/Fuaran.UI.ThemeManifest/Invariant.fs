namespace Fuaran.UI.ThemeManifest

// ─── Invariants — the declared, soft-weighted theme constraints ──
//
// The W3C Design Tokens (DTCG) format carries only a free-form
// `$description` — no constraints, no budgets, no verification
// surface. This DU is the first half of what `ThemeManifest` adds on
// top of DTCG: a machine-checkable invariant vocabulary the observer
// (Phase 146) verifies resolved style against.
//
// **Soft-weighted, not hard pass/fail.** Following Draco
// (`idl.uw.edu/draco`), each invariant carries a `Weight` so a
// consumer can rank violations by importance rather than treating
// every breach as equally fatal. v1 ships with author-supplied
// weights (default `1.0`); *learning* the weights from a violation
// corpus is deferred to Phase 150 — this phase only carries the slot.
//
// **`UsageBudget` formalises the 60-30-10 rule** — the established
// design-world colour-distribution heuristic — as a per-token
// `targetPct ± tolerancePct`, rather than inventing a new metric.
//
// **Additive-only post-ship** (parallel to `StyleFlag` / `LayoutFlag`):
// adding a new `InvariantKind` case is a minor bump (existing matches
// gain an `FS0025` warning); redefining an existing case breaks every
// manifest already authored against it and every AI prompt cache that
// pattern-matched it.

/// The motion-voice budget — the payload of `InvariantKind.MotionVoice`.
/// Declares the ceiling a theme's animations must stay under so the
/// "voice" (calm vs energetic) is a checkable property, not just prose.
/// Verification of this invariant is out of Phase 146's scope (which
/// covers the colour budget + contrast floors); the slot ships now so
/// the manifest contract is complete.
type MotionBudget =
    { MaxDurationMs: int
      Easing: string option }

/// The bounded, additive-only invariant vocabulary. Each value is
/// wrapped in an `Invariant` record that attaches the soft `Weight`.
[<RequireQualifiedAccess>]
type InvariantKind =
    /// A named role's resolved contrast must be at least `minRatio`.
    /// Stricter, per-role floors than the manifest-free WCAG AA default
    /// the `StyleObserver` applies without a manifest. `role` is a
    /// manifest role name (e.g. `"body-text"`).
    | ContrastFloor of role: string * minRatio: float
    /// A token's share of total visible surface area must stay within
    /// `targetPct ± tolerancePct` (the 60-30-10 formalisation). `token`
    /// is a `ManifestToken.Name`. Verified by Phase 146's area-weighted
    /// colour-distribution pass.
    | UsageBudget of token: string * targetPct: float * tolerancePct: float
    /// The theme's motion must stay within the declared `MotionBudget`.
    | MotionVoice of MotionBudget

/// One declared invariant + its soft weight. `Weight` defaults to
/// `1.0` (via `Invariant.create`); Phase 150 will learn weights from a
/// violation corpus, but the slot is carried from v1 so the contract
/// never has to change shape to accommodate it.
type Invariant = { Kind: InvariantKind; Weight: float }

module Invariant =
    /// The default invariant weight — equal importance.
    [<Literal>]
    let DefaultWeight = 1.0

    /// Construct an invariant with the default weight.
    let create (kind: InvariantKind) : Invariant = { Kind = kind; Weight = DefaultWeight }

    /// Construct an invariant with an explicit weight.
    let createWeighted (weight: float) (kind: InvariantKind) : Invariant = { Kind = kind; Weight = weight }

    /// Stable string discriminator for JSON emission — PascalCase-matched
    /// to the DU cases (same discipline as `StyleFlag.kind` / `LayoutFlag.kind`).
    let kind (inv: Invariant) : string =
        match inv.Kind with
        | InvariantKind.ContrastFloor _ -> "ContrastFloor"
        | InvariantKind.UsageBudget _ -> "UsageBudget"
        | InvariantKind.MotionVoice _ -> "MotionVoice"
