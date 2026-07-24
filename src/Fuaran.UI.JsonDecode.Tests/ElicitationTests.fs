module Fuaran.UI.JsonDecode.Tests.Elicitation

// ============================================================================
//  Elicitation envelope conformance (WIRE_FORMAT §18) — corpus-driven.
//
//  The F# host re-proves at test time exactly what the emitter proved: a
//  round-trip fixture decodes + re-encodes byte-identically through its
//  `decoder`-named entry point; a reject surfaces the manifest's code + path;
//  an answer-conformance document validates / refuses as declared. The TS
//  host re-proves the same from the same corpus, so §18 is corpus-certified
//  cross-host, not host-private.
//
//  Plus in-repo value-level suites the corpus cannot express: outcome
//  totality (every case of the closed DU round-trips through the codec) and
//  the typed decode surface.
// ============================================================================

open Expecto
open Fuaran.Core
open Fuaran.UI.OpStream.Abstractions

let private corpusRoot, corpusEntries = Corpus.load ()

let private roundTripEntries =
    corpusEntries |> List.filter (fun e -> e.Kind = "elicitation-round-trip")

let private rejectEntries =
    corpusEntries |> List.filter (fun e -> e.Kind = "elicitation-reject")

let private answerAcceptEntries =
    corpusEntries |> List.filter (fun e -> e.Kind = "elicitation-answer-accept")

let private answerRejectEntries =
    corpusEntries |> List.filter (fun e -> e.Kind = "elicitation-answer-reject")

let private roundTripCase (e: Corpus.FixtureEntry) : Test =
    testCase (sprintf "Elicitation — %s (%s) — decode/encode matches corpus" e.Description e.Id) (fun () ->
        let wire = Corpus.readPayload corpusRoot e.InputFile

        match ElicitationFixtures.decodeReencode e.Decoder wire with
        | Ok reencoded -> Expect.equal reencoded wire "round-trip preserves canonical-JSON byte form"
        | Error err -> failtestf "decode failed: %A\n  on input: %s" err wire)

let private rejectCase (e: Corpus.FixtureEntry) : Test =
    testCase (sprintf "Elicitation — %s (%s) — refuses with the canonical error" e.Description e.Id) (fun () ->
        let wire = Corpus.readPayload corpusRoot e.InputFile

        match ElicitationFixtures.decodeReencode e.Decoder wire with
        | Error err ->
            Expect.equal err.Code (Option.defaultValue "" e.ExpectedErrorCode) "reject code matches the manifest"

            Expect.isTrue
                (err.Path.StartsWith(Option.defaultValue "$" e.ExpectedPath))
                "reject path starts with the manifest prefix"
        | Ok _ -> failtest "expected a refusal; decode accepted the artifact")

let private answerCase (e: Corpus.FixtureEntry) : Test =
    testCase (sprintf "Elicitation answer — %s (%s)" e.Description e.Id) (fun () ->
        let wire = Corpus.readPayload corpusRoot e.InputFile

        match e.Kind, Elicitation.validateAnswerDocument wire with
        | "elicitation-answer-accept", Ok() -> ()
        | "elicitation-answer-accept", Error err ->
            failtestf "a conformant answer was refused: %s at %s" err.Code err.Path
        | _, Error err ->
            Expect.equal err.Code (Option.defaultValue "" e.ExpectedErrorCode) "refusal code matches the manifest"

            Expect.isTrue
                (err.Path.StartsWith(Option.defaultValue "$" e.ExpectedPath))
                "refusal path starts with the manifest prefix"
        | _, Ok() -> failtest "expected an answer-conformance refusal; validation accepted it")

[<Tests>]
let elicitationRoundTrips =
    roundTripEntries
    |> List.map roundTripCase
    |> testList "Fuaran.UI.OpStream.Abstractions.Elicitation — round-trip (corpus, §18)"

[<Tests>]
let elicitationRejects =
    rejectEntries
    |> List.map rejectCase
    |> testList "Fuaran.UI.OpStream.Abstractions.Elicitation — reject (corpus, §18)"

[<Tests>]
let elicitationAnswerConformance =
    (answerAcceptEntries @ answerRejectEntries)
    |> List.map answerCase
    |> testList "Fuaran.UI.OpStream.Abstractions.Elicitation — answer conformance (corpus, §18.4)"

[<Tests>]
let elicitationCoverageGate =
    testList
        "Fuaran.UI.OpStream.Abstractions.Elicitation — corpus coverage gate"
        [ testCase "round-trip corpus has one entry per ElicitationFixtures.roundTrips fixture" (fun () ->
              Expect.equal
                  (List.length roundTripEntries)
                  (List.length ElicitationFixtures.roundTrips)
                  "elicitation-round-trip corpus count must equal ElicitationFixtures.roundTrips — regenerate with `--emit-corpus`")

          testCase "reject corpus has one entry per ElicitationFixtures.rejects fixture" (fun () ->
              Expect.equal
                  (List.length rejectEntries)
                  (List.length ElicitationFixtures.rejects)
                  "elicitation-reject corpus count must equal ElicitationFixtures.rejects — regenerate with `--emit-corpus`")

          testCase "answer-document corpus has one entry per ElicitationFixtures.answerDocs fixture" (fun () ->
              Expect.equal
                  (List.length answerAcceptEntries + List.length answerRejectEntries)
                  (List.length ElicitationFixtures.answerDocs)
                  "elicitation-answer-* corpus count must equal ElicitationFixtures.answerDocs — regenerate with `--emit-corpus`") ]

// ── Value-level suites (beyond what the corpus can express) ─────────────────

[<Tests>]
let outcomeTotality =
    // The outcome set is CLOSED: every case of the DU (including both
    // Superseded shapes) round-trips through the codec value-identically.
    // A new case added to the DU without a codec branch fails this list's
    // construction at compile time (incomplete match in encodeOutcome).
    let cases =
        [ "Answered",
          ElicitationOutcome.Answered(
              Map.ofList [ "a", AnswerValue.Int 1; "b", AnswerValue.Float 2.5; "c", AnswerValue.Str "x" ]
          )
          "Declined", ElicitationOutcome.Declined
          "TimedOut", ElicitationOutcome.TimedOut
          "Superseded (by)", ElicitationOutcome.Superseded(Some "elc-next")
          "Superseded (anonymous)", ElicitationOutcome.Superseded None ]

    cases
    |> List.map (fun (label, outcome) ->
        testCase (sprintf "outcome %s round-trips value-identically" label) (fun () ->
            let envelope =
                { ElicitationId = "elc-totality"
                  Outcome = outcome }

            match Elicitation.decodeOutcome (Elicitation.encodeOutcome envelope) with
            | Ok decoded -> Expect.equal decoded envelope "decode inverts encode over the closed outcome set"
            | Error e -> failtestf "outcome failed to round-trip: %s at %s" e.Code e.Path))
    |> testList "Fuaran.UI.OpStream.Abstractions.Elicitation — outcome totality"

[<Tests>]
let typedDecodeSurface =
    testList
        "Fuaran.UI.OpStream.Abstractions.Elicitation — typed decode surface"
        [ testCase "the full envelope decodes to the declared typed values" (fun () ->
              let wire = Corpus.readPayload corpusRoot "elicitation/elc-full.json"

              match Elicitation.decodeEnvelope wire with
              | Error e -> failtestf "decode failed: %s at %s" e.Code e.Path
              | Ok env ->
                  Expect.equal env.ElicitationId "elc-full" "id"
                  Expect.equal env.TimeoutMs (Some 30000) "timeout travels as data"
                  Expect.equal (List.length env.Contract.Fields) 5 "field count"

                  Expect.equal
                      (env.Contract.Fields |> List.map (fun f -> f.Name))
                      [ "salary"; "bonusRatio"; "note"; "grade"; "label" ]
                      "field order is declaration order"

                  match env.Default with
                  | Some d -> Expect.equal (Map.find "salary" d) (AnswerValue.Int 45000) "typed default"
                  | None -> failtest "expected a default answer")

          testCase "validateAnswer refuses a nonconforming answer before it reaches the agent" (fun () ->
              let wire = Corpus.readPayload corpusRoot "elicitation/elc-full.json"

              match Elicitation.decodeEnvelope wire with
              | Error e -> failtestf "decode failed: %s at %s" e.Code e.Path
              | Ok env ->
                  let nonconforming =
                      Map.ofList [ "grade", AnswerValue.Str "z"; "salary", AnswerValue.Int 1 ]

                  match Elicitation.validateAnswer env.Contract nonconforming with
                  | Error e ->
                      Expect.equal e.Code ElicitationErrorCode.ANSWER_OUT_OF_SPACE "enum violation surfaces typed"
                      Expect.equal e.Path "$.answer.grade" "at the offending field"
                  | Ok() -> failtest "a nonconforming answer must be refused") ]
