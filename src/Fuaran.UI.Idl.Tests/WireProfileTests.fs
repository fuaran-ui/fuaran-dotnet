module Fuaran.UI.WireProfileTests

// ============================================================================
//  Phase 1670 — the go-red proof for `WIRE_FORMAT.md` §15.4's WIRING.
//
//  `Fuaran.Core.Idl.Diff`'s own suite proves the CLASSIFIER: that a removed tag
//  reads as breaking, that a required field added to an existing owner is a
//  wire break (since Fuaran.Core 0.34.0), and the rest. None of that says anything about whether §15.4's rule
//  — the one this repository's wire profile actually follows — is applied to
//  this vocabulary's artifact, which is the half that was missing and the half
//  this phase added.
//
//  So these cases are deliberately NOT a second copy of the substrate's tests.
//  Each one perturbs the REAL vocabulary, renders both artifacts through the
//  same `Artifact.render` the committed `idl.json` comes from, and asserts the
//  §15.4 step. The two that matter most are the pair §15.4 used to conflate:
//
//    * a new optional field  → NO STEP   (Phase 1670's ruling; the substrate's
//                                         default rule says `minor` here, and
//                                         since Phase 2626 the step is read off
//                                         the substrate under THIS format's
//                                         decode rule, which absorbs it)
//    * a new closed-vocabulary tag → MINOR
//
//  A classifier that cannot be made to disagree with itself is not evidence, so
//  the perturbations are real and the expectations are written out rather than
//  derived from the same function under test.
// ============================================================================

open Expecto
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types
open Fuaran.UI.WireProfile

let private baseIdl = Fuaran.UI.Vocabulary.uiIdl
let private baseText = Artifact.render baseIdl

let private verdictOf (before: string) (after: string) : Verdict =
    match Diff.parse before None, Diff.parse after None with
    | Ok b, Ok a -> Diff.verdict b a
    | Error e, _
    | _, Error e -> failtestf "artifact did not parse: %s" e

let private stepOf (before: string) (after: string) : ProfileStep =
    match reportBetween before after with
    | Error e -> failtestf "the §15.4 report refused a well-formed pair: %s" e
    | Ok _ -> verdictOf before after |> step |> fst

/// The vocabulary with one extra field on the FIRST declared kind. `opt` is the
/// case §15.4 now exempts; `Required` is the case that breaks emitters without
/// buying a profile step.
let private withExtraField (opt: Optionality) : Idl =
    let field =
        { Name = "phase1670ProbeField"
          Type = TStr
          Opt = opt
          Annotations = Annotations.Empty
          Aliases = []
          Presence = None
          Keyed = false }

    match baseIdl.Kinds with
    | k :: rest ->
        { baseIdl with
            Kinds = { k with Fields = field :: k.Fields } :: rest }
    | [] -> failtest "the vocabulary declares no kinds"

/// The vocabulary with one extra NODE KIND — a tag a behind decoder cannot map.
let private withExtraKind: Idl =
    { baseIdl with
        Kinds =
            baseIdl.Kinds
            @ [ { Tag = "Phase1670Probe"
                  Category = "Display"
                  Fields =
                    [ { Name = "label"
                        Type = TStr
                        Opt = Required
                        Annotations = Annotations.Empty
                        Aliases = []
                        Presence = None
                        Keyed = false } ]
                  Annotations = Annotations.Empty } ] }

/// The vocabulary with the FIRST field matching `pick` (in declaration order, over
/// every kind) rewritten by `edit`, and the name it found — so a perturbation is a
/// real field of the real vocabulary, not one invented for the test.
let private withFirstField (pick: IdlField -> bool) (edit: IdlField -> IdlField) : Idl * string =
    let owner =
        baseIdl.Kinds
        |> List.tryFind (fun k -> k.Fields |> List.exists pick)
        |> Option.defaultWith (fun () -> failtest "the vocabulary declares no field the perturbation can reach")

    let field = owner.Fields |> List.find pick

    { baseIdl with
        Kinds =
            baseIdl.Kinds
            |> List.map (fun k ->
                if k.Tag = owner.Tag then
                    { k with
                        Fields = k.Fields |> List.map (fun f -> if f.Name = field.Name then edit f else f) }
                else
                    k) },
    owner.Tag + "." + field.Name

/// A required string field, loosened to optional (Phase 2509, case 1).
let private loosened =
    withFirstField (fun f -> f.Opt = Required && f.Type = TStr && f.Presence.IsNone) (fun f ->
        { f with Opt = Optional })

/// An `int` field, widened to `float` (Phase 2509, case 1).
let private widened =
    withFirstField (fun f -> f.Type = TInt && f.Presence.IsNone) (fun f -> { f with Type = TFloat })

[<Tests>]
let tests =
    testList
        "Phase 1670 — WIRE_FORMAT.md §15.4 is derived, not declared"
        [ testCase "an unchanged vocabulary moves no profile" (fun _ ->
              Expect.equal
                  (stepOf baseText baseText)
                  NoStep
                  "a delta of nothing classified as something — the diff is reporting phantom changes")

          // THE RULING, as a test. Before Phase 1670 §15.4 said this bumped the
          // minor; three separate phases (862, 863, 934) declined the bump and
          // the profile has stood at `core@1.0` throughout, so the rule and the
          // practice disagreed in a way nothing could see.
          testCase "a new OPTIONAL field moves no profile — §15.4's exemption" (fun _ ->
              Expect.equal
                  (stepOf baseText (Artifact.render (withExtraField Optional)))
                  NoStep
                  "an added optional field stepped the profile. §2 rule 2 ignores an unknown key on decode, so a \
                   behind consumer absorbs it without ever consulting the profile — the step buys nothing and \
                   spends a number that means something")

          // The other half of the same ruling: the exemption is about OPTIONAL,
          // and saying so is only worth anything if the neighbouring case is
          // known to be different.
          // Fuaran.Core 0.34.0 (fuaran-core Phase 304) grades a required field added to an
          // existing owner as a WIRE break: every document written before it lacks the member,
          // and the decoder refuses a document missing a required member. Before that it was an
          // emitter break, and this pair pinned NO STEP plus a report naming the emitters. The
          // step follows the substrate's classification, so it is now the major row's.
          testCase "a new REQUIRED field is a major - old documents stop decoding" (fun _ ->
              Expect.equal
                  (stepOf baseText (Artifact.render (withExtraField Required)))
                  Major
                  "a required-field addition did not read as a `/vN/` event, but every document written before \
                   it lacks the member and is refused")

          testCase "a new required field's report names the wire break" (fun _ ->
              match reportBetween baseText (Artifact.render (withExtraField Required)) with
              | Error e -> failtestf "the report refused a well-formed pair: %s" e
              | Ok r ->
                  Expect.stringContains
                      r
                      "a REQUIRED field added"
                      "a required-field addition stepped the profile without the row that says why")

          // What DOES bump the minor, stated as a test rather than as prose.
          testCase "a new node KIND bumps the minor" (fun _ ->
              Expect.equal
                  (stepOf baseText (Artifact.render withExtraKind))
                  Minor
                  "a new `$type` tag did not step the minor. A behind consumer meets a discriminator it cannot \
                   map and needs §15.3 tolerance plus a profile that names the gap")

          // And the reverse direction, which is the one §15.4's second row is for.
          testCase "REMOVING a node kind is a major" (fun _ ->
              Expect.equal
                  (stepOf (Artifact.render withExtraKind) baseText)
                  Major
                  "a removed tag did not read as a `/vN/` event. A document that was valid is not, and a behind \
                   consumer must read the artifact as `Foreign` and refuse it")

          // The report is a projection of the classification, so a run that
          // renders nothing has classified nothing — worth pinning separately
          // from the step, because a silently empty report reads as "clean".
          testCase "the report names the change count and the step" (fun _ ->
              match reportBetween baseText (Artifact.render withExtraKind) with
              | Error e -> failtestf "the report refused a well-formed pair: %s" e
              | Ok r ->
                  Expect.stringContains r "MINOR" "the rendered report does not name the step it computed"
                  Expect.stringContains r "Phase1670Probe" "the rendered report does not name the tag that caused it")

          // Phase 2509, case 1 (`docs/DECISIONS.md` D28). Both read as NO STEP before this phase,
          // which is where the host's copy of the step and fuaran-core Phase 587's classifier
          // disagreed. Every old document still decodes, which is why the substrate grades both
          // additive; a behind consumer refuses a NEW document that omits the member or carries
          // `2.5`, and must-ignore does not help it, so the profile has to name the gap.
          testCase "a required field LOOSENED to optional bumps the minor" (fun _ ->
              let idl, name = loosened

              Expect.equal
                  (stepOf baseText (Artifact.render idl))
                  Minor
                  (sprintf
                      "loosening %s did not step the minor. A behind consumer requires the member and refuses a \
                       new document that omits it"
                      name))

          testCase "an int field WIDENED to float bumps the minor" (fun _ ->
              let idl, name = widened

              Expect.equal
                  (stepOf baseText (Artifact.render idl))
                  Minor
                  (sprintf
                      "widening %s did not step the minor. A behind consumer reads an integer and refuses a new \
                       document carrying 2.5"
                      name))

          testCase "the loosening report names the member that moved" (fun _ ->
              let idl, name = loosened

              match reportBetween baseText (Artifact.render idl) with
              | Error e -> failtestf "the report refused a well-formed pair: %s" e
              | Ok r ->
                  Expect.stringContains r "MINOR" "the rendered report does not name the step it computed"
                  Expect.stringContains r "loosened to optional" (sprintf "the report does not say %s loosened" name))

          // Phase 2509, case 2 (`docs/DECISIONS.md` D28), as it stands after the Phase 2626
          // re-point. The host kept MAJOR here while it computed its own step; the step is now
          // Core's, and Core names no profile while a row is undecided (fuaran-core D242's open
          // question). Pinned so that a ruling there is a deliberate edit here, and so that the
          // break is still visible: the report carries the substrate's advice, which says major.
          testCase "a wire break beside an undecided row is undecided, and the report says major (D28 case 2)" (fun _ ->
              let erased, _ =
                  withFirstField (fun f -> f.Type = TStr && f.Presence.IsNone) (fun f -> { f with Type = TJson })

              let before, after = Artifact.render withExtraKind, Artifact.render erased
              let severities = (verdictOf before after).Changes |> List.map _.Severity

              Expect.contains severities BreakingWire "the probe carries no wire break"
              Expect.contains severities Unclassifiable "the probe carries no undecided row"

              Expect.equal
                  (stepOf before after)
                  Undecided
                  "a removed kind beside an erased-slot change did not read as undecided, which is Core's answer"

              match reportBetween before after with
              | Error e -> failtestf "the report refused a well-formed pair: %s" e
              | Ok r ->
                  Expect.stringContains
                      r
                      "A decided row already breaks the wire"
                      "the undecided report hides the wire break beside it"

                  Expect.stringContains r "MAJOR" "the undecided report does not carry the substrate's major advice")

          // Phase 2626, the go-red for the decode rule (fuaran-core#127's adoption task): the step
          // the host reads is Core's under THIS format's rule, and the rule is what absorbs an added
          // optional field. Under `RefusesUnknownMembers` the same verdict bumps the minor, so a host
          // that dropped the rule, or a Core that stopped honouring it, turns this red.
          testCase
              "an added optional field bumps the minor under RefusesUnknownMembers, and not under this format's rule"
              (fun _ ->
                  let v = verdictOf baseText (Artifact.render (withExtraField Optional))
                  let base' = Fuaran.Core.Versioning.Capability.coreV1

                  match Diff.bumpProfileUnder Fuaran.Core.Versioning.DecodeRule.RefusesUnknownMembers base' v with
                  | Bump.Bumped p ->
                      Expect.equal
                          (p.Major, p.Minor)
                          (base'.Major, base'.Minor + 1)
                          "a strict reader absorbed an added member it cannot read"
                  | Bump.Undecided _ -> failtest "an added string field read as undecided"

                  match Diff.bumpProfileUnder decodeRule base' v with
                  | Bump.Bumped p ->
                      Expect.equal p base' "this format's decode rule did not absorb an added optional field"
                  | Bump.Undecided _ -> failtest "an added string field read as undecided"

                  Expect.equal
                      decodeRule
                      Fuaran.Core.Versioning.DecodeRule.IgnoresUnknownMembers
                      "§2 rule 2 is must-ignore")

          // A malformed artifact is not a classification, and must not read as
          // "no change" — the failure mode a bare `try … with` would have.
          testCase "an unparseable artifact is refused, never read as no-change" (fun _ ->
              match reportBetween "{ not an artifact" baseText with
              | Ok r -> failtestf "a malformed BEFORE artifact produced a classification: %s" r
              | Error e -> Expect.stringContains e "BEFORE" "the refusal does not say WHICH side failed to parse") ]
