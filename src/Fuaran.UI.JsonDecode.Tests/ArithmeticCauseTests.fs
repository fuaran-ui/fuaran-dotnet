module Fuaran.UI.JsonDecode.Tests.ArithmeticCauseTests

// ============================================================================
//  INVALID_JSON arithmetic cause-sniffing (2026-08-09).
//
//  A syntax failure used to carry only an offset — the one error class with no
//  didactic cause, and the class a model repairing from the envelope cannot
//  act on. The observed emission behind it is inline arithmetic
//  (`"value": 178 / 180`); the decoder now sniffs the failure site and names
//  the cause. Host-local message tests, deliberately NOT reject-corpus
//  fixtures: the conformance contract pins Code + Path only, and the didactic
//  message text is a host-quality invariant, not a cross-host one.
// ============================================================================

open Expecto
open Fuaran.UI.Ops.JsonDecode

let private decodeError (json: string) : DecodeError =
    match decodeNode json with
    | Ok _ -> failtestf "expected decode to fail: %s" json
    | Error e -> e

[<Tests>]
let arithmeticCauseTests =
    testList
        "Fuaran.UI.Ops.JsonDecode — INVALID_JSON arithmetic cause-sniffing"
        [ testCase "division in a value position names the cause and the expression"
          <| fun () ->
              let e = decodeError """{"value": 178 / 180}"""
              Expect.equal e.Code "INVALID_JSON" "still the syntax-failure class"
              Expect.stringContains e.Message "arithmetic expression" "the cause is named"
              Expect.stringContains e.Message "178 / 180" "the offending expression is quoted"
              Expect.stringContains e.Message "compute the value" "the repair instruction is present"

          testCase "the fraction shape observed in the wild (nested, multi-line)"
          <| fun () ->
              let e =
                  decodeError
                      "{\n  \"kind\": {\n    \"$type\": \"Progress\",\n    \"fraction\": {\n      \"$type\": \"Static\",\n      \"value\": 120 / 180\n    }\n  }\n}"

              Expect.stringContains e.Message "120 / 180" "the expression is extracted across the real emission shape"

          testCase "multiplication and addition sniff too"
          <| fun () ->
              Expect.stringContains (decodeError """{"a": 3 * 4}""").Message "3 * 4" "multiplication is the same class"

              Expect.stringContains (decodeError """{"a": 3 + 4}""").Message "3 + 4" "addition is the same class"

          testCase "a subtraction against a negative right operand still extracts"
          <| fun () ->
              Expect.stringContains
                  (decodeError """{"a": 5 - -3}""").Message
                  "5 - -3"
                  "optional leading '-' on the right operand"

          testCase "ordinary syntax failures do NOT claim an arithmetic cause"
          <| fun () ->
              Expect.isFalse
                  ((decodeError """{"id": }""").Message.Contains "arithmetic")
                  "a bare syntax error stays a bare syntax error"

              Expect.isFalse
                  ((decodeError "this is not json").Message.Contains "arithmetic")
                  "garbage input is not misdiagnosed"

              Expect.isFalse
                  ((decodeError """{"a": 1"b": 2}""").Message.Contains "arithmetic")
                  "a missing comma between members is not an expression"

          testCase "Phase 810 — the offset wrapper appears exactly once per envelope"
          <| fun () ->
              // Deeply nested failure: pre-810 this message carried the
              // "parse error at offset N:" prefix once per unwind level.
              let e =
                  decodeError
                      """{"id":"g","kind":{"$type":"Box","children":[{"id":"x","kind":{"$type":"Progress","fraction":{"$type":"Static","value":1 / 2}}}]}}"""

              let occurrences =
                  System.Text.RegularExpressions.Regex.Matches(e.Message, "parse error at offset").Count

              Expect.equal occurrences 1 "one wrap, the innermost offset"
              Expect.stringContains e.Message "arithmetic expression" "the didactic tail is unchanged"

          testCase "expressions inside strings are legal JSON and still parse past the parser"
          <| fun () ->
              // `"178 / 180"` is a perfectly valid JSON string; the payload
              // fails later for shape reasons, never with an arithmetic cause.
              let e = decodeError """{"note": "178 / 180"}"""
              Expect.isFalse (e.Message.Contains "arithmetic") "string content is never sniffed" ]
