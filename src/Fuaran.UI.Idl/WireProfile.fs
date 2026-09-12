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
// `Fuaran.Core.Idl.Diff` carries its own `profileBump`, and it is deliberately
// not used. Two reasons, and the first is decisive on its own:
//
//   1. It is `internal` to the codegen assembly — public callers get `changes`
//      and `classify`, which are the general-purpose halves.
//
//   2. It would give the WRONG ANSWER under §15.4 as amended. That function
//      recommends `core@1.(x+1)` for any `Additive` severity, and an added
//      OPTIONAL FIELD classifies as `Additive` — which is precisely the case
//      Phase 1670's ruling exempts. The exemption is a fact about the Fuaran UI
//      wire profile (§2 rule 2 ignores unknown keys on decode, so a behind
//      consumer absorbs a new optional field without ever consulting the
//      profile), not a fact about IDL diffing in general, so it belongs beside
//      the vocabulary it governs rather than inside a substrate tool that
//      classifies IDLs for domains with different decode rules.
//
// ── Advisory, exactly as the diff is ────────────────────────────────────────
//
// Nothing here edits a file, bumps a profile, or fails a build. It prints, and a
// phase author reads it and argues with it. A classifier that auto-applied would
// make the hand-declared classification unfalsifiable, which is the property
// that lets the two be compared at all.
// ---------------------------------------------------------------------------

open Fuaran.Core.Idl

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

/// A change that introduces a TAG into a vocabulary an existing decoder already
/// dispatches on — the four §15.4 names, exactly.
///
/// A new union, enum or record TYPE is not on this list and that is not an
/// oversight: a type nothing points at is unreachable from the wire, and a type
/// reached through a newly-ADDED optional field is absorbed by the same
/// must-ignore rule the field itself is. What forces a minor is a tag arriving in
/// a slot a behind decoder is already reading.
let private introducesTag (c: Diff.Change) =
    match c with
    | Diff.KindAdded _
    | Diff.OpAdded _
    | Diff.UnionCaseAdded _
    | Diff.EnumCaseAdded _ -> true
    | _ -> false

/// The §15.4 step, plus the sentence that justifies it. Ordered by severity: a
/// breaking change is never softened by an additive one sitting beside it.
let step (cs: Diff.Classification list) : ProfileStep * string =
    let sev s =
        cs |> List.exists (fun c -> c.Severity = s)

    let tags = cs |> List.filter (fun c -> introducesTag c.Change)

    let names =
        tags
        |> List.map (fun c ->
            match c.Change with
            | Diff.KindAdded t -> "kind " + t
            | Diff.OpAdded t -> "op " + t
            | Diff.UnionCaseAdded(u, k) -> u + "." + k
            | Diff.EnumCaseAdded(e, w) -> e + "." + w
            | _ -> "?")
        |> String.concat ", "

    if List.isEmpty cs then
        NoStep, "no vocabulary change — the artifact is identical."
    elif sev Diff.BreakingWire then
        Major,
        "a document that was valid is not, or its bytes moved — §15.4's removal/rename row. The `/vN/` major "
        + "segment and the schema `$id` move with it, and every behind consumer reads the artifact as `Foreign` "
        + "(§15.2) and hard-refuses."
    elif sev Diff.Unclassifiable then
        Undecided,
        "at least one change crosses an ERASED slot (`hosted` / `json` / `opaque`) whose admitted values the "
        + "artifact does not state, so the delta cannot be classified from the artifact alone. Resolve the rows "
        + "marked UNCLASSIFIABLE below by hand before declaring anything."
    elif not (List.isEmpty tags) then
        Minor,
        "a new TAG lands in a vocabulary an existing decoder already dispatches on ("
        + names
        + "). A behind consumer meets a discriminator it cannot map, so it needs §15.3's transport-only `Unknown` "
        + "and a profile that names what it is missing: `core@1.N` → `core@1.(N+1)`."
        + (if sev Diff.BreakingForEmitters then
               " AND at least one change breaks EMITTERS, which the minor understates — see the rows below."
           else
               "")
    elif sev Diff.BreakingForEmitters then
        NoStep,
        "NO profile step — but at least one change breaks EMITTERS (a required field arrived, or an optional one "
        + "became required). A behind CONSUMER is unaffected, which is all the profile counter measures, so "
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
    match Diff.parse before, Diff.parse after with
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
