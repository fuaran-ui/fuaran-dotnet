module Fuaran.UI.ServerDriven.Tests.ActionLogCensus

open Expecto

open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.ServerDriven

// ============================================================================
//  The census's server-driven leg: the describers this tier prints, and the
//  rejection strings that quote them.
//
//  `docs/ACTION-LOG-PRIVACY.md` is the census these tests enforce. The rule
//  there is that any site which logs, traces or reports an `Action` emits the
//  CONSTRUCTOR and the author-declared NAME, never a payload VALUE — and that
//  `Navigate` and `SetState` are the two arms where the difference is easy to
//  lose, a route carrying a query string and a state key naming a slot whose
//  value is whatever a text control captured.
//
//  Why here as well as on the record's own tier: this tier's denial strings
//  reach an always-on host log by default, and they are composed rather than
//  merely forwarded — `RejectReason.describe` wraps `describeAction`'s output
//  in a sentence. A composition is exactly where a safe ingredient stops being
//  a safe result, so the composed string is what gets asserted, not the
//  ingredient.
//
//  Deliberately crude and deliberately narrow, matching the posture the
//  no-network scan adopted rather than inventing a second one.
// ============================================================================

type private Msg = Poke of string

/// The poison rides every payload position. The author-declared names — the
/// endpoint, channel, state key, tool, capability and node id — deliberately
/// carry none of it, because those are precisely what a log-safe description is
/// supposed to keep.
let private poison = "PoIsOn-uSeR-tYpEd-53cr3t"

let private allActionCases: (string * Action<Msg>) list =
    [ "Chain", Action.Chain [ Action.WriteToClipboard poison; Action.Navigate("/a?q=" + poison) ]
      "WriteToClipboard", Action.WriteToClipboard poison
      "Dispatch", Action.Dispatch(Poke poison)
      // Fully qualified: `System.Action`'s instance `Invoke` wins the name
      // resolution otherwise.
      "Invoke", Fuaran.UI.Generated.Action.Invoke("cap.publish", [])
      "ReadFileBody", Action.ReadFileBody(poison, None, FileReadEncoding.Text, None)
      "Call", Action.Call("/api/save", None, None)
      "Navigate", Action.Navigate("/orders?email=" + poison + "#" + poison)
      "CommitLocal", Action.CommitLocal "field-1"
      "Notify", Action.Notify("toast", JStr poison)
      "SetState", Action.SetState("draft.body", Some(JStr poison), None)
      "AiTool", Action.AiTool("summarise", JObj [ "text", JStr poison ])
      // Phase 1124 — the one case with no payload position at all, so it cannot
      // carry the poison. It is in the fixture anyway, and deliberately: a
      // payload-free case is the shape most likely to be left out of a coverage
      // list on the grounds that there is nothing to check, and the describer
      // still composes a string that reaches an always-on host log.
      "Print", Action.Print ]

[<Tests>]
let tests =
    testList
        "the action-log census — the server-driven describers"
        [ test "the fixture covers every Action case exactly once" {
              // A case the fixture forgot is a case whose redaction nobody
              // checked, and the shortfall is invisible without this.
              let names = allActionCases |> List.map fst
              Expect.equal (List.length (List.distinct names)) (List.length names) "each named once"

              let unionCases =
                  Reflection.FSharpType.GetUnionCases(typeof<Action<Msg>>)
                  |> Array.map _.Name
                  |> Array.sort

              Expect.equal (List.sort names |> Array.ofList) unionCases "the fixture matches the DU exactly"
          }

          test "POISON: describeAction leaks no payload value, in any case" {
              for name, action in allActionCases do
                  let described = Validation.describeAction action

                  Expect.isFalse
                      (described.Contains poison)
                      (sprintf "%s: a payload value reached this tier's describer. Got: %s" name described)
          }

          test "POISON: the composed rejection string leaks no payload value either" {
              // The string a host actually logs. `describeAction` being safe is
              // necessary and not sufficient — what reaches the log is this.
              for name, action in allActionCases do
                  let rendered: string =
                      Validation.RejectReason.describe (
                          Validation.RejectReason.DispatchDenied("node-1", Validation.describeAction action)
                      )

                  Expect.isFalse
                      (rendered.Contains poison)
                      (sprintf "%s: a payload value reached the logged rejection. Got: %s" name rendered)
          }

          test "Navigate keeps its PATH and SetState keeps its KEY" {
              // The two arms the census names specifically, asserted here rather
              // than left to "it delegates, so it must be fine".
              Expect.equal
                  (Validation.describeAction (Action.Navigate("/orders?email=" + poison): Action<Msg>))
                  "Navigate(/orders)"
                  "the query string is gone and the path is kept"

              Expect.equal
                  (Validation.describeAction (Action.SetState("draft.body", Some(JStr poison), None): Action<Msg>))
                  "SetState(draft.body)"
                  "the key is kept and the written value never appears"
          }

          test "go-red check: the fixture really does carry the poison" {
              // Proving the scan can fail. Without this, every assertion above
              // passes for the wrong reason the day the fixture stops carrying
              // any payload — the classic vacuous green. The same actions are
              // rendered by a describer that is NOT redacting (F#'s structural
              // `%A`), and the poison must be found.
              let leaking =
                  allActionCases
                  |> List.filter (fun (_, action) -> (sprintf "%A" action).Contains poison)
                  |> List.map fst

              Expect.isGreaterThan
                  (List.length leaking)
                  5
                  "an unredacted rendering of the same fixture finds the poison in most cases — so the assertions above are discriminating, not matching nothing"

              Expect.contains leaking "Navigate" "including the route arm the census singles out"
              Expect.contains leaking "SetState" "and the state-value arm beside it"
          } ]
