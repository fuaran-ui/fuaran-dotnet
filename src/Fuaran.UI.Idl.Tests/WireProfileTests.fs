module Fuaran.UI.WireProfileTests

// ============================================================================
//  Phase 1670 — the go-red proof for `WIRE_FORMAT.md` §15.4's WIRING.
//
//  `Fuaran.Core.Idl.Diff`'s own suite proves the CLASSIFIER: that a removed tag
//  reads as breaking, that a required field added to an existing owner breaks
//  emitters, and the rest. None of that says anything about whether §15.4's rule
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
//                                         own `profileBump` says `minor` here,
//                                         which is why the step is computed in
//                                         this repository and not read off it)
//    * a new closed-vocabulary tag → MINOR
//
//  A classifier that cannot be made to disagree with itself is not evidence, so
//  the perturbations are real and the expectations are written out rather than
//  derived from the same function under test.
// ============================================================================

open Expecto
open Fuaran.Core.Idl
open Fuaran.UI.WireProfile

let private baseIdl = Fuaran.UI.Vocabulary.uiIdl
let private baseText = Artifact.render baseIdl

let private stepOf (before: string) (after: string) : ProfileStep =
    match reportBetween before after with
    | Error e -> failtestf "the §15.4 report refused a well-formed pair: %s" e
    | Ok _ ->
        match Diff.parse before, Diff.parse after with
        | Ok b, Ok a -> Diff.changes b a |> List.map Diff.classify |> step |> fst
        | Error e, _
        | _, Error e -> failtestf "artifact did not parse: %s" e

/// The vocabulary with one extra field on the FIRST declared kind. `opt` is the
/// case §15.4 now exempts; `Required` is the case that breaks emitters without
/// buying a profile step.
let private withExtraField (opt: Optionality) : Idl =
    let field =
        { Name = "phase1670ProbeField"
          Type = TStr
          Opt = opt
          Annotations = Annotations.Empty }

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
                        Annotations = Annotations.Empty } ]
                  Annotations = Annotations.Empty } ] }

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
          testCase "a new REQUIRED field moves no profile either — but for the opposite reason" (fun _ ->
              Expect.equal
                  (stepOf baseText (Artifact.render (withExtraField Required)))
                  NoStep
                  "a required field is not a CONSUMER-side event, so the profile counter still says nothing about \
                   it; the report is expected to name the EMITTER break instead of stepping the minor")

          testCase "a new required field's report names the emitter break" (fun _ ->
              match reportBetween baseText (Artifact.render (withExtraField Required)) with
              | Error e -> failtestf "the report refused a well-formed pair: %s" e
              | Ok r ->
                  Expect.stringContains
                      r
                      "EMITTERS"
                      "a required-field addition reported NO STEP without saying why — silence here is the \
                       0.2.0 / orchestration-0.1.3 lesson repeating")

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

          // A malformed artifact is not a classification, and must not read as
          // "no change" — the failure mode a bare `try … with` would have.
          testCase "an unparseable artifact is refused, never read as no-change" (fun _ ->
              match reportBetween "{ not an artifact" baseText with
              | Ok r -> failtestf "a malformed BEFORE artifact produced a classification: %s" r
              | Error e -> Expect.stringContains e "BEFORE" "the refusal does not say WHICH side failed to parse") ]
