// Phase 2134 — the turn stage keeps an unknown label distinct from a terminal one.
//
// THE PARITY TABLE. `fuaran-ts/packages/client/test/turnStage.test.ts` carries the
// same rows, in the same order, against `parseTurnResponse` / `isKnownTurnStage` /
// `isRepairable`. The two clients live in separate repositories, so the table is
// held twice; a row changed in one is changed in the other in the same
// change-set, or the hosts stop agreeing on what a refusal means.
//
// Each row: the label the endpoint sent, the stage label the client reads it as,
// whether that stage is one of the four the client knows, and whether the repair
// loop retries it.

module Fuaran.UI.Client.Tests.TurnStageTests

open System.Text.Json
open Expecto

open Fuaran.UI.Client

let private stageTable: (string * string * bool * bool) list =
    [
      // The canonical labels.
      "access-token", "access-token", true, false
      "provider", "provider", true, false
      "parse", "parse", true, true
      "apply", "apply", true, true
      // The retired PascalCase spellings.
      "AccessToken", "access-token", true, false
      "Provider", "provider", true, false
      "Parse", "parse", true, true
      "Apply", "apply", true, true
      // Case, `-`, `_` and ASCII whitespace are ignored.
      "ACCESS_TOKEN", "access-token", true, false
      " apply\t", "apply", true, true
      // Unknown: kept exactly as sent, never read as a known stage, never retried.
      "validate", "validate", false, false
      "Telepathy", "Telepathy", false, false
      "unknown", "unknown", false, false
      "", "", false, false
      "   ", "   ", false, false
      "parse ", "parse ", false, false ]

let private refusal (stage: string) =
    $"""{{"error":{{"stage":{JsonSerializer.Serialize stage},"code":"C","message":"m"}}}}"""

let private stageOf (status: int) (body: string) : TurnStage =
    match Wire.parseTurnResponse status body with
    | TurnResult.TurnFailed err -> err.Stage
    | other -> failtestf "expected TurnFailed, got %A" other

let private isKnown (stage: TurnStage) =
    match stage with
    | TurnStage.Unknown _ -> false
    | _ -> true

[<Tests>]
let turnStageTests =
    testList
        "TurnStage"
        [ testList
              "the turn-stage parity table"
              [ for sent, read, known, repairable in stageTable ->
                    test $"reads {JsonSerializer.Serialize sent} as {JsonSerializer.Serialize read}" {
                        for status in [ 422; 500 ] do
                            let stage = stageOf status (refusal sent)
                            Expect.equal (TurnStage.label stage) read "the stage's label"
                            Expect.equal (isKnown stage) known "known"
                            Expect.equal (Repair.isRepairable stage) repairable "repairable"
                            Expect.equal (TurnStage.ofLabel sent) stage "ofLabel agrees with the wire read"
                    } ]

          test "never reads an unknown label as provider" {
              for sent, _, known, _ in stageTable do
                  if not known then
                      Expect.notEqual (stageOf 422 (refusal sent)) TurnStage.Provider sent
                      Expect.equal (stageOf 422 (refusal sent)) (TurnStage.Unknown sent) "kept as sent"
          }

          test "an absent stage is not a label: the provider fallback stays" {
              for body in
                  [ """{"error":{}}"""
                    """{"error":{"stage":7}}"""
                    """{"error":{"stage":null}}"""
                    "" ] do
                  Expect.equal (stageOf 422 body) TurnStage.Provider body
          }

          test "a retired spelling built by hand as Unknown is not a known stage" {
              Expect.isFalse (Repair.isRepairable (TurnStage.Unknown "Apply")) "not repairable"
              Expect.equal (TurnStage.label (TurnStage.Unknown "Apply")) "Apply" "label kept"
          } ]
