// The Elmish adapter's turn-loop, unit-tested on .NET.
//
// `Turn.fs` is compile-linked from ../client/ (the house pattern for keeping a
// sample's logic honest without a second .fsproj in the graph). Because the
// module is portable — no Fable, no Elmish, no HTTP — the whole decision surface
// is exercised here, and the browser edge stays a thin adapter over it.

module Fuaran.Sample.SdkIntegration.Tests.TurnTests

open Expecto

open Fuaran.Sample.SdkIntegration.Client
open Fuaran.Sample.SdkIntegration.Client.Turn

let private tree1 =
    """{"id":"a","kind":{"$type":"Badge","label":"A","variant":"Info"}}"""

let private tree2 =
    """{"id":"b","kind":{"$type":"Badge","label":"B","variant":"Info"}}"""

/// Drive a list of messages through `update`, collecting every turn request the
/// loop asked the host to run.
let private run (messages: Msg list) (start: Model) : Model * TurnRequest list =
    messages
    |> List.fold
        (fun (model, requests) msg ->
            let next, request = Turn.update msg model

            next,
            (match request with
             | Some r -> requests @ [ r ]
             | None -> requests))
        (start, [])

[<Tests>]
let turnTests =
    testList
        "SdkIntegration.Turn — the Elmish turn-loop"
        [ test "starts idle with no tree" {
              let model = Turn.init ()
              Expect.equal model.Status Status.Idle "idle"
              Expect.isNone model.TreeJson "no tree held"
          }

          test "the first Submit is a fresh generation" {
              let model, requests = run [ SetPrompt "a metric strip"; Submit ] (Turn.init ())
              Expect.equal model.Status Status.Generating "in flight"
              Expect.equal (List.length requests) 1 "one turn requested"
              Expect.equal requests[0].Prompt "a metric strip" "carries the prompt"
              Expect.isNone requests[0].CurrentTreeJson "fresh — no tree carried"
          }

          test "the second Submit carries the held tree — a repair diff" {
              let model, requests =
                  run [ SetPrompt "one"; Submit; Produced tree1; SetPrompt "two"; Submit ] (Turn.init ())

              Expect.equal (List.length requests) 2 "two turns"
              Expect.isNone requests[0].CurrentTreeJson "turn 1 fresh"
              Expect.equal requests[1].CurrentTreeJson (Some tree1) "turn 2 repairs the held tree"
              Expect.equal model.Status Status.Generating "second turn in flight"
          }

          test "Produced advances the held tree" {
              let model, _ =
                  run [ SetPrompt "x"; Submit; Produced tree1; SetPrompt "y"; Submit; Produced tree2 ] (Turn.init ())

              Expect.equal model.TreeJson (Some tree2) "advanced to the newest tree"
              Expect.equal model.Status Status.Ready "ready"
          }

          test "a failed turn leaves the held tree untouched" {
              let model, _ =
                  run
                      [ SetPrompt "x"
                        Submit
                        Produced tree1
                        SetPrompt "y"
                        Submit
                        TurnFailed "boom" ]
                      (Turn.init ())

              Expect.equal model.TreeJson (Some tree1) "last good tree survives the failure"

              match model.Status with
              | Status.Error message -> Expect.equal message "boom" "carries the message"
              | other -> failtestf "expected Error, got %A" other
          }

          test "an empty or whitespace prompt issues no turn" {
              let _, requests = run [ Submit; SetPrompt "   "; Submit ] (Turn.init ())
              Expect.isEmpty requests "nothing requested"
          }

          test "a second Submit while a turn is in flight is ignored" {
              let _, requests = run [ SetPrompt "x"; Submit; Submit; Submit ] (Turn.init ())
              Expect.equal (List.length requests) 1 "only the first turn is issued"
          }

          test "Reset drops the held tree so the next turn is fresh again" {
              let model, requests =
                  run [ SetPrompt "x"; Submit; Produced tree1; Reset; SetPrompt "y"; Submit ] (Turn.init ())

              Expect.isNone model.TreeJson "tree forgotten"
              Expect.equal (List.length requests) 2 "two turns"
              Expect.isNone requests[1].CurrentTreeJson "the post-reset turn is fresh"
          } ]
