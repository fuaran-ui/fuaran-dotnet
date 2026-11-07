module Fuaran.UI.Program.Server.Tests.ClientEffectNonEmptyTests

// ─── the client-effect members §2 marks non-empty (Phase 2620, D29) ──────────
//
// PROGRAM_WIRE.md §2's table, client-effect row: `Navigate`'s and
// `PushState`'s `route`, `Focus`'s and `ReadFileBody`'s `nodeId`,
// `Download`'s `url` and `name`, and `Confirm`'s `prompt` and `token`. Until
// this phase `decodeClientEffect` read all eight with `requireString` and let
// the empty string through. The corpus pins each with a reject vector, which
// the conformance suite runs for its class; this pins, member by member:
//
//  1. the document with the member present and empty is refused, on
//     `missing-member`, and the refusal names the member and never the value;
//  2. the same document with the member filled round-trips, byte for byte, so
//     the refusal is the member's and not the document's.
//
// And the one string member the rule does NOT mark: `WriteToClipboard`'s
// `text` is content, and an empty one still decodes.

open Expecto
open Fuaran.Core
open Fuaran.Program.Bounded
open Fuaran.UI.Program

/// Each member, its arm's document with the member filled, and the same
/// document with it emptied.
let private members =
    [ "route", """{"kind":"Navigate","route":"/orders"}""", """{"kind":"Navigate","route":""}"""
      "route", """{"kind":"PushState","route":"/orders?page=2"}""", """{"kind":"PushState","route":""}"""
      "nodeId", """{"kind":"Focus","nodeId":"orders-search"}""", """{"kind":"Focus","nodeId":""}"""
      "nodeId",
      """{"kind":"ReadFileBody","nodeId":"upload-1","encoding":"Text"}""",
      """{"kind":"ReadFileBody","nodeId":"","encoding":"Text"}"""
      "url",
      """{"kind":"Download","url":"https://example.invalid/report.csv","name":"report.csv"}""",
      """{"kind":"Download","url":"","name":"report.csv"}"""
      "name",
      """{"kind":"Download","url":"https://example.invalid/report.csv","name":"report.csv"}""",
      """{"kind":"Download","url":"https://example.invalid/report.csv","name":""}"""
      "prompt",
      """{"kind":"Confirm","prompt":"Settle?","token":"btn-settle#0"}""",
      """{"kind":"Confirm","prompt":"","token":"btn-settle#0"}"""
      "token",
      """{"kind":"Confirm","prompt":"Settle?","token":"btn-settle#0"}""",
      """{"kind":"Confirm","prompt":"Settle?","token":""}""" ]

let private decode (document: string) =
    Fuaran.Program.Bounded.ProgramWire.parseDocument document
    |> Result.bind UiWitness.decodeClientEffect

[<Tests>]
let tests =
    testList
        "the client-effect members the specification marks non-empty (Phase 2620, D29)"
        [ for memberName, filled, empty in members do
              test $"an empty '{memberName}' in {empty} is refused on missing-member, naming the member" {
                  match decode empty with
                  | Ok effect -> failtestf "decoded to %A: %s" effect empty
                  | Error refusal ->
                      Expect.equal refusal.Class RefusalClass.MissingMember "the class §2's rule names"
                      Expect.equal refusal.Detail $"member '{memberName}' is empty" "the member named, no value echoed"
              }

              test $"a non-empty '{memberName}' in {filled} round-trips byte for byte" {
                  match decode filled with
                  | Error refusal -> failtestf "the filled document was refused: %A" refusal
                  | Ok effect -> Expect.equal (ProgramWire.encodeClientEffect effect) filled "byte for byte"
              }

          test "an empty clipboard text is content, and still decodes" {
              let document = """{"kind":"WriteToClipboard","text":""}"""

              match decode document with
              | Error refusal -> failtestf "refused: %A" refusal
              | Ok effect -> Expect.equal (ProgramWire.encodeClientEffect effect) document "byte for byte"
          } ]
