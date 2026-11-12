module Fuaran.UI.WireProfile

// ---------------------------------------------------------------------------
// Phase 1670 — `WIRE_FORMAT.md` §15.4's evolution policy, WIRED.
//
// §15.4 has claimed since Phase 319 that the classification of a vocabulary
// change is "derivable, not hand-disciplined" — an IDL diff over two capability
// snapshots, with `Fuaran.Core.Wire.Versioning.classify` / `bump` as the
// host-neutral primitives. The claim was true of the MACHINERY and false of the
// PRACTICE: `Fuaran.Core.Idl.Diff` exists, classifies, and is exercised by the
// substrate's own suite, and nothing in this repository or in the corpus ever
// ran it over `idl.json`. A derivability claim nothing derives is the same
// defect class as a rule nobody follows — it makes a reviewer who enforces it
// look wrong.
//
// This module is the missing half: the §15.4 rule applied to THIS vocabulary's
// artifact, reported at the moment the vocabulary changes (the regeneration
// command in `Fuaran.UI.Idl.Tests`, which is the one gesture a deliberate
// vocabulary change goes through).
//
// ── The step is Core's ──────────────────────────────────────────────────────
//
// Core's classifier learned this profile's decode rule in fuaran-core Phase 587:
// `Diff.bumpProfileUnder Versioning.DecodeRule.IgnoresUnknownMembers` absorbs an
// added optional member (§2 rule 2) and the rows no document carries, and moves
// the minor for every other additive row. Since Phase 2626 this module computes
// no step of its own: it maps that answer onto the four words below and words
// the report (`docs/DECISIONS.md` D28).
//
// One answer changed with the re-point, and it is Core's to change back: a wire
// break beside an undecided row is UNDECIDED, because the substrate names no
// profile while a row is undecided (D28, case 2; fuaran-core D242's open
// question). The report prints the substrate's own advice, which says major
// there, so the break is never hidden.
//
// ── Advisory, exactly as the diff is ────────────────────────────────────────
//
// Nothing here edits a file, bumps a profile, or fails a build. It prints, and a
// phase author reads it and argues with it. A classifier that auto-applied would
// make the hand-declared classification unfalsifiable, which is the property
// that lets the two be compared at all.
// ---------------------------------------------------------------------------

open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types

/// The wire-profile movement a vocabulary delta implies, under `WIRE_FORMAT.md`
/// §15.4 as amended by Phase 1670.
type ProfileStep =
    /// Nothing a consumer negotiates over. The `core@1.x` profile does not move.
    | NoStep
    /// A new document a behind consumer cannot read: a new TAG in a closed
    /// discriminated vocabulary (it needs §15.3 tolerance), or a member loosened
    /// to optional or widened from `int` to `float`. The profile names the gap:
    /// `core@1.N` → `core@1.(N+1)`.
    | Minor
    /// A document that was valid is not, or its bytes moved: the `/vN/`
    /// incompatibility boundary. `core@1.x` → `core@2.0`.
    | Major
    /// At least one change crosses an erased slot the artifact deliberately does
    /// not describe, so the artifact cannot decide it.
    | Undecided

/// This format's decode rule: §2 rule 2 ignores an object member the vocabulary
/// does not declare.
let decodeRule = Fuaran.Core.Versioning.DecodeRule.IgnoresUnknownMembers

/// The profile the step is measured from. Only the movement is reported, so any
/// base reads the same; `core@1.0` is the wire's current profile (§15.1).
let private baseProfile = Fuaran.Core.Versioning.Capability.coreV1

/// How a row that moved the minor is named in the report.
let private nameOf (c: Classification) =
    match c.Change with
    | KindAdded t -> "kind " + t
    | OpAdded t -> "op " + t
    | UnionCaseAdded(u, k) -> u + "." + k
    | EnumCaseAdded(e, w) -> e + "." + w
    | FieldOptionalityChanged(o, n, _, _) -> o.Describe + "." + n + " loosened to optional"
    | FieldTypeChanged(o, n, _, _) -> o.Describe + "." + n + " widened from int to float"
    | other -> sprintf "%A" other

/// The §15.4 step, plus the sentence that justifies it. The step is Core's
/// `Diff.bumpProfileUnder` under this format's decode rule; this function only
/// reads which counter moved and words the answer.
let step (v: Verdict) : ProfileStep * string =
    let wireBreak = v.Changes |> List.exists (fun c -> c.Severity = BreakingWire)

    match Diff.bumpProfileUnder decodeRule baseProfile v with
    | Bump.Undecided _ ->
        Undecided,
        "at least one change crosses an ERASED slot (`hosted` / `json` / `opaque`) whose admitted values the "
        + "artifact does not state, so the delta cannot be classified from the artifact alone. Resolve the rows "
        + "marked UNCLASSIFIABLE below by hand before declaring anything."
        + (if wireBreak then
               " A decided row already breaks the wire; the substrate's advice: "
               + v.ProfileAdvice
           else
               "")
    | Bump.Bumped p when p.Major <> baseProfile.Major ->
        Major,
        "a document that was valid is not, or its bytes moved — §15.4's removal/rename row. The `/vN/` major "
        + "segment and the schema `$id` move with it, and every behind consumer reads the artifact as `Foreign` "
        + "(§15.2) and hard-refuses."
    | Bump.Bumped p when p.Minor <> baseProfile.Minor ->
        let names =
            v.Changes
            |> List.filter (fun c ->
                (c.Severity = Additive || c.Severity = BreakingForEmitters)
                && not (Diff.absorbedWhenUnknownMembersIgnored c))
            |> List.map nameOf
            |> String.concat ", "

        Minor,
        "a behind consumer cannot read every new document ("
        + names
        + "). A new TAG is a discriminator it cannot map, so it needs §15.3's transport-only `Unknown`; a member "
        + "loosened to optional, or an `int` slot widened to `float`, carries a value its decoder refuses. Either "
        + "way the profile names what it is missing: `core@1.N` → `core@1.(N+1)`."
        + (if v.BreaksEmitters then
               " AND at least one change breaks EMITTERS, which the minor understates — see the rows below."
           else
               "")
    | Bump.Bumped _ when List.isEmpty v.Changes -> NoStep, "no vocabulary change — the artifact is identical."
    | Bump.Bumped _ when v.BreaksEmitters ->
        NoStep,
        "NO profile step — but at least one change breaks EMITTERS (an authoring default was withdrawn or moved; "
        + "since Fuaran.Core 0.34.0 a required field arriving is graded a wire break instead, because the documents "
        + "written before it stop decoding). A behind CONSUMER is unaffected, which is all the profile counter measures, so "
        + "stepping the minor would say nothing true; every downstream EMITTER nonetheless needs a coordinated "
        + "change. Treat this as the more expensive of the two, not the cheaper."
    | Bump.Bumped _ ->
        NoStep,
        "NO profile step. Every change here is absorbed without negotiation — §15.4's optional-field exemption "
        + "(§2 rule 2 ignores an unknown key on decode, so a behind consumer never consults the profile), a "
        + "host-surface-only move, or an annotation. Bumping the minor here buys a reader nothing and spends a "
        + "number that means something."

/// The one-word form, for a report header.
let render (s: ProfileStep) : string =
    match s with
    | NoStep -> "NO STEP"
    | Minor -> "MINOR (core@1.N -> core@1.(N+1))"
    | Major -> "MAJOR (/vN/ moves)"
    | Undecided -> "UNDECIDED"

/// Classify `before` → `after` (two `idl.json` texts) and render the §15.4
/// report. `Error` only when a snapshot will not parse — a malformed artifact is
/// not a classification.
let reportBetween (before: string) (after: string) : Result<string, string> =
    match Diff.parse before None, Diff.parse after None with
    | Error e, _ -> Error("the BEFORE artifact does not parse: " + e)
    | _, Error e -> Error("the AFTER artifact does not parse: " + e)
    | Ok b, Ok a ->
        let v = Diff.verdict b a
        let cs = v.Changes
        let st, why = step v

        let rows =
            cs
            |> List.map (fun c -> sprintf "  - [%A] %s" c.Severity c.Rationale)
            |> String.concat "\n"

        Ok(
            sprintf
                "WIRE_FORMAT.md 15.4 - wire-profile classification of this vocabulary delta\n\
                 =========================================================================\n\
                 changes: %d\n\
                 profile: %s\n\
                 because: %s\n%s%s"
                (List.length cs)
                (render st)
                why
                (if List.isEmpty cs then "" else "\n")
                rows
        )
