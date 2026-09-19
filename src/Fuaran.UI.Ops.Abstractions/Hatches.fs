module Fuaran.UI.Ops.Hatches

// ============================================================================
//  Phase 1743 — the escape-hatch report: "hatches open: none" as a COMPUTED
//  statement rather than an asserted one.
//
//  This project's escape-hatch inventory enumerates, in prose, the places
//  arbitrary behaviour can enter the stack. Which of them are actually OPEN in a
//  given deployment is a different question, and it is decidable — but until now
//  a reader inferred the answer from the prose, which is exactly the shape of
//  reading that turns an undisclosed door into a surprise.
//
//  What this file holds is the REPORT VOCABULARY and nothing else: three states,
//  a finding, a section of findings, and the canonical document they render as.
//  It holds no predicate, because a predicate belongs beside the thing it
//  observes — a renderer registration is observed in the renderer, and a
//  composition's registrations are observed by whatever walks a composition.
//
//  ── Three states, and the third is the point ────────────────────────────────
//  `Open` and `Closed` are the easy half. `Undecided` is why this is a three-
//  valued vocabulary rather than a bool: a walk that cannot see a door must say
//  so. A report that rendered "cannot see" as "closed" would be worse than no
//  report at all, because it would put the estate's own name behind a claim
//  nobody checked. Every consumer that reads this document reads three states or
//  it has not read it.
//
//  An `Open` finding carries WHAT ADMITTED IT in `Account`; a `Closed` one
//  carries what was checked, so the document is a positive statement rather than
//  an absence of findings; an `Undecided` one carries why it could not be
//  decided and, where there is one, what a host would supply to decide it.
//
//  ── The document is a WIRE, and two independent producers emit it ───────────
//  A runtime section is produced by a running host, here. A composition section
//  is produced by whatever walks a composition, in another tier entirely, which
//  reads this document BY SHAPE and never by taking a type dependency on this
//  package. So the shape is stated once, here, in the form the reader parses:
//
//      { "kind": "hatchSection", "version": 1, "section": "<name>",
//        "findings": [ { "predicate": "<id>", "hatch": <n>,
//                        "state": "open" | "closed" | "undecided",
//                        "account": "<prose>" } ] }
//
//  `hatch` is the inventory entry the finding mechanises, carried as its NUMBER
//  rather than as a title: the number is the stable handle, and a title copied
//  into code is a second copy of a sentence that will move.
//
//  Fable-clean, like everything else in this project: no IO, no clock, no
//  reflection. The report is a value.
// ============================================================================

open Fuaran.Core

/// Whether a door is open, in a report whose whole value is that it cannot say
/// "closed" when it means "I could not see".
[<RequireQualifiedAccess>]
type HatchState =
    /// Something admitted behaviour through this door, and `Account` names what.
    | Open
    /// The walk looked and found nothing. A POSITIVE statement — it is what
    /// makes the document worth reading when every finding is this one.
    | Closed
    /// The walk could not decide. Never read as closed, never rendered as
    /// closed, and never omitted: an omitted finding and a closed one are
    /// indistinguishable to a reader, which is the failure this state exists to
    /// prevent.
    | Undecided

/// One predicate's answer about one inventory entry.
type HatchFinding =
    {
        /// The predicate's own stable name — what the finding calls itself.
        Predicate: string
        /// The inventory entry this predicate mechanises, by number.
        Hatch: int
        State: HatchState
        /// What admitted it, what was checked, or why it could not be decided.
        Account: string
    }

/// One producer's findings. `Section` names the walk that produced them
/// (`composition`, `runtime`) because a reader must be able to tell which
/// question was asked: no composition walk can see a host's startup, and no
/// host report can see a registration the composition never made.
type HatchSection =
    { Section: string
      Findings: HatchFinding list }

/// The document's own kind, carried IN it: a consumer that finds one on disk can
/// tell what it is without knowing who wrote it.
[<Literal>]
let Kind = "hatchSection"

[<Literal>]
let Version = 1

/// The runtime section's name, spelled once so the producer and every reader
/// cannot spell it differently.
[<Literal>]
let RuntimeSection = "runtime"

/// The composition section's name. Spelled here beside the runtime one even
/// though nothing in this package produces it, because the two are one
/// vocabulary and a second spelling of it elsewhere is how a wire drifts.
[<Literal>]
let CompositionSection = "composition"

/// The wire token for a state. Lowercase, closed, and total over the DU.
let stateWire (state: HatchState) : string =
    match state with
    | HatchState.Open -> "open"
    | HatchState.Closed -> "closed"
    | HatchState.Undecided -> "undecided"

/// Read a state token back. `None` for anything else — an unrecognised state is
/// refused by the reader rather than defaulted, because every plausible default
/// is a lie in one direction.
let tryStateOfWire (token: string) : HatchState option =
    match token with
    | "open" -> Some HatchState.Open
    | "closed" -> Some HatchState.Closed
    | "undecided" -> Some HatchState.Undecided
    | _ -> None

/// The findings in a given state, in the order the producer emitted them.
let inState (state: HatchState) (section: HatchSection) : HatchFinding list =
    section.Findings |> List.filter (fun f -> f.State = state)

/// The one-line statement, and the sentence this whole phase exists to make
/// computable: **"hatches open: none"**.
///
/// The undecided count rides on the same line whenever it is non-zero, so the
/// phrase can never be read as a clean bill of health over a walk that did not
/// see everything. A section with nothing open and nothing undecided renders as
/// the bare phrase, which is then a claim about the whole section.
let summary (section: HatchSection) : string =
    let name (f: HatchFinding) =
        f.Predicate + " (hatch " + string f.Hatch + ")"

    let opened =
        match inState HatchState.Open section with
        | [] -> "hatches open: none"
        | xs -> "hatches open: " + (xs |> List.map name |> String.concat ", ")

    match inState HatchState.Undecided section with
    | [] -> opened
    | xs -> opened + "; undecided: " + (xs |> List.map name |> String.concat ", ")

/// One finding as canonical wire JSON.
let encodeFinding (finding: HatchFinding) : JVal =
    JObj
        [ "predicate", JStr finding.Predicate
          "hatch", JInt finding.Hatch
          "state", JStr(stateWire finding.State)
          "account", JStr finding.Account ]

/// A section as the canonical `hatchSection` document — the shape a consumer in
/// another tier reads by shape rather than by type.
let encodeSection (section: HatchSection) : JVal =
    Json.kindObj
        Kind
        [ "version", JInt Version
          "section", JStr section.Section
          "findings", JArr(section.Findings |> List.map encodeFinding) ]

/// The section's canonical JSON text.
let renderSection (section: HatchSection) : string = Json.render (encodeSection section)
