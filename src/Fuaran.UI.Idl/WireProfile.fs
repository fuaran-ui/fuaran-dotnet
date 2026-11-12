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
// ── Why the profile step is computed HERE and not read off the diff ─────────
//
// Core's classifier learned this profile's decode rule in fuaran-core Phase 587
// (`Versioning.DecodeRule.IgnoresUnknownMembers` and `Diff.bumpProfileUnder`),
// and that is where the step belongs. It is still computed here for one reason:
// the Core this repository pins does not carry it, and no released Core does
// yet. The pin moves with the cohort raise after Core's next release, and this
// module's `step` is then deleted for a call to Core's (`docs/DECISIONS.md` D28).
//
// Until then this copy answers what Core's does, case for case, with ONE
// recorded exception: a wire break beside an undecided row is a major here and
// undecided there (D28, case 2). Phase 2509 measured the other difference the
// two had. A required member loosened to optional, or an `int` slot widened to
// `float`, read as no step here, and Core was right: a behind consumer refuses
// the new documents under §2 rule 2, so the minor moves (D28, case 1).
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
    /// A new TAG in a closed discriminated vocabulary — a behind consumer's
    /// decoder cannot map it and needs §15.3 tolerance plus a profile that names
    /// the gap. `core@1.N` → `core@1.(N+1)`.
    | Minor
    /// A document that was valid is not, or its bytes moved: the `/vN/`
    /// incompatibility boundary. `core@1.x` → `core@2.0`.
    | Major
    /// At least one change crosses an erased slot the artifact deliberately does
    /// not describe, so the artifact cannot decide it.
    | Undecided

/// A change whose new documents a behind consumer cannot read, though every old
/// document still decodes: the rows that move the minor.
///
/// The four §15.4 TAG names are the first half: a tag arriving in a slot a
/// behind decoder already dispatches on. The second half is lag on a member
/// (Phase 2509, `docs/DECISIONS.md` D28): a required member loosened to
/// optional, or an `int` slot widened to `float`. The substrate grades both
/// `Additive`, and a behind consumer refuses a new document that omits the
/// member or carries `2.5`, whatever its decoder does with an unknown key, so
/// must-ignore absorbs neither.
///
/// A new union, enum or record TYPE is not on this list and that is not an
/// oversight: a type nothing points at is unreachable from the wire, and a type
/// reached through a newly-ADDED optional field is absorbed by the same
/// must-ignore rule the field itself is.
let private behindConsumerRefuses (c: Classification) =
    match c.Change with
    | KindAdded _
    | OpAdded _
    | UnionCaseAdded _
    | EnumCaseAdded _ -> true
    | FieldOptionalityChanged _
    | FieldTypeChanged _ -> c.Severity = Additive
    | _ -> false

/// The §15.4 step, plus the sentence that justifies it. Ordered by severity: a
/// breaking change is never softened by an additive one sitting beside it.
let step (cs: Classification list) : ProfileStep * string =
    let sev s =
        cs |> List.exists (fun c -> c.Severity = s)

    let lagging = cs |> List.filter behindConsumerRefuses

    let names =
        lagging
        |> List.map (fun c ->
            match c.Change with
            | KindAdded t -> "kind " + t
            | OpAdded t -> "op " + t
            | UnionCaseAdded(u, k) -> u + "." + k
            | EnumCaseAdded(e, w) -> e + "." + w
            | FieldOptionalityChanged(o, n, _, _) -> o.Describe + "." + n + " loosened to optional"
            | FieldTypeChanged(o, n, _, _) -> o.Describe + "." + n + " widened from int to float"
            | _ -> "?")
        |> String.concat ", "

    if List.isEmpty cs then
        NoStep, "no vocabulary change — the artifact is identical."
    elif sev BreakingWire then
        Major,
        "a document that was valid is not, or its bytes moved — §15.4's removal/rename row. The `/vN/` major "
        + "segment and the schema `$id` move with it, and every behind consumer reads the artifact as `Foreign` "
        + "(§15.2) and hard-refuses."
    elif sev Unclassifiable then
        Undecided,
        "at least one change crosses an ERASED slot (`hosted` / `json` / `opaque`) whose admitted values the "
        + "artifact does not state, so the delta cannot be classified from the artifact alone. Resolve the rows "
        + "marked UNCLASSIFIABLE below by hand before declaring anything."
    elif not (List.isEmpty lagging) then
        Minor,
        "a behind consumer cannot read every new document ("
        + names
        + "). A new TAG is a discriminator it cannot map, so it needs §15.3's transport-only `Unknown`; a member "
        + "loosened to optional, or an `int` slot widened to `float`, carries a value its decoder refuses. Either "
        + "way the profile names what it is missing: `core@1.N` → `core@1.(N+1)`."
        + (if sev BreakingForEmitters then
               " AND at least one change breaks EMITTERS, which the minor understates — see the rows below."
           else
               "")
    elif sev BreakingForEmitters then
        NoStep,
        "NO profile step — but at least one change breaks EMITTERS (an authoring default was withdrawn or moved; "
        + "since Fuaran.Core 0.34.0 a required field arriving is graded a wire break instead, because the documents "
        + "written before it stop decoding). A behind CONSUMER is unaffected, which is all the profile counter measures, so "
        + "stepping the minor would say nothing true; every downstream EMITTER nonetheless needs a coordinated "
        + "change. Treat this as the more expensive of the two, not the cheaper."
    else
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
        let cs = Diff.changes b a |> List.map Diff.classify
        let st, why = step cs

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
