// Fuaran.UI.Client repair-loop tests — the typed error surface, the hint
// threading, bounded retries, and the terminal-vs-repairable classification.
// Parity-checked against fuaran-ts/packages/client/src/repair.ts.

module Fuaran.UI.Client.Tests.RepairTests

open Expecto

open Fuaran.UI.Client
open Fuaran.UI.Client.Tests

let private cfg = FuaranClientConfig.create "https://endpoint.example/api/fuaran"

let private client (transport: IFuaranTransport) =
    FuaranClient({ cfg with Transport = Some transport })

let private producedBody = """{"TreeJson":"t","Ops":[],"Version":"1.2.0"}"""

let private applyFailBody =
    """{"Stage":"apply","Code":"APPLY_REJECTED","Message":"no node #x"}"""

[<Tests>]
let repairTests =
    testList
        "Repair"
        [ test "isRepairable: Apply/Parse repairable, AccessToken/Provider terminal" {
              Expect.isTrue (Repair.isRepairable TurnStage.Apply) "apply"
              Expect.isTrue (Repair.isRepairable TurnStage.Parse) "parse"
              Expect.isFalse (Repair.isRepairable TurnStage.AccessToken) "access-token terminal"
              Expect.isFalse (Repair.isRepairable TurnStage.Provider) "provider terminal"
          }

          test "threadHint preserves prompt + tree and appends the marked hint" {
              let args = GenerateArgs.repair "make a form" "<tree>"

              let err =
                  { Stage = TurnStage.Apply
                    Code = "APPLY_REJECTED"
                    Message = "no node #x" }

              let threaded = Repair.threadHint args err
              Expect.stringContains threaded.Prompt "make a form" "keeps the original prompt"
              Expect.stringContains threaded.Prompt Repair.HintMarker "carries the repair marker"
              Expect.stringContains threaded.Prompt "apply" "names the stage"
              Expect.stringContains threaded.Prompt "no node #x" "carries the hint message"
              Expect.equal threaded.CurrentTreeJson (Some "<tree>") "current tree carried forward"
          }

          testAsync "generateWithRepair recovers: fail(apply) → produced within the bound" {
              let transport =
                  ScriptedTransport(
                      [ { Status = 422; Body = applyFailBody }
                        { Status = 200; Body = producedBody } ]
                  )

              let c = client transport

              let! result = Repair.generateWithRepair c (GenerateArgs.prompt "x") 2

              match result with
              | TurnResult.Produced _ -> ()
              | other -> failtestf "expected Produced after one repair, got %A" other

              Expect.equal transport.CallCount 2 "one failure + one repair"
              // the second request carried the threaded hint
              Expect.stringContains
                  (transport.Requests |> List.item 1).Body
                  Repair.HintMarker
                  "second turn threads the hint"
          }

          testAsync "generateWithRepair surfaces the final envelope when retries are exhausted" {
              let transport =
                  ScriptedTransport(
                      [ { Status = 422; Body = applyFailBody }
                        { Status = 422; Body = applyFailBody }
                        { Status = 422; Body = applyFailBody } ]
                  )

              let! result = Repair.generateWithRepair (client transport) (GenerateArgs.prompt "x") 1

              match result with
              | TurnResult.TurnFailed err -> Expect.equal err.Code "APPLY_REJECTED" "final envelope surfaced"
              | other -> failtestf "expected TurnFailed, got %A" other

              Expect.equal transport.CallCount 2 "initial attempt + 1 retry, then stop"
          }

          testAsync "generateWithRepair does not retry a terminal (provider) failure" {
              let transport =
                  ScriptedTransport([ { Status = 500; Body = "boom" }; { Status = 200; Body = producedBody } ])

              let c = client transport

              let! result = Repair.generateWithRepair c (GenerateArgs.prompt "x") 3

              match result with
              | TurnResult.TurnFailed err -> Expect.equal err.Stage TurnStage.Provider "provider is terminal"
              | other -> failtestf "expected terminal TurnFailed, got %A" other

              Expect.equal transport.CallCount 1 "no retry on a terminal failure"
          }

          testAsync "maxRetries = 0 is a single attempt" {
              let transport = ScriptedTransport([ { Status = 422; Body = applyFailBody } ])
              let! _ = Repair.generateWithRepair (client transport) (GenerateArgs.prompt "x") 0
              Expect.equal transport.CallCount 1 "no repair when the bound is zero"
          } ]
