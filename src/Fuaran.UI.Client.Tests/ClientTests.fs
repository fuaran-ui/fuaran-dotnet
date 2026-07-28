// Fuaran.UI.Client tests — the wire round-trip, the three-way result parsing,
// the transport-failure path, and the session tree-carry loop. Driven against a
// scripted MockTransport, so no live endpoint is touched. Parity-checked against
// the TypeScript @fuaran-ui/client behaviour (same PascalCase wire keys, same
// camelCase read-tolerance, same status→case map, same session semantics).

module Fuaran.UI.Client.Tests.ClientTests

open System.Text.Json
open Expecto

open Fuaran.UI.Types
open Fuaran.UI.Client
open Fuaran.UI.Client.Tests

/// Parse a request body and read a top-level property, or None when absent.
let private field (body: string) (name: string) : JsonElement option =
    let doc = JsonDocument.Parse body

    match doc.RootElement.TryGetProperty name with
    | true, v -> Some v
    | _ -> None

let private cfg = FuaranClientConfig.create "https://endpoint.example/api/fuaran"

/// A minimal canonical wire node from the shared conformance corpus (nodes/badge-1).
let private validTreeJson =
    """{"id":"badge-1","kind":{"$type":"Badge","label":"Beta","variant":"Info"}}"""

[<Tests>]
let wireTests =
    testList
        "Wire.toWireBody"
        [ test "prompt-only omits every optional field" {
              let body =
                  Wire.toWireBody
                      (GenerateArgs.prompt "hello")
                      { ProviderKey = None
                        AccessToken = None }

              Expect.equal (field body "Prompt" |> Option.map (fun e -> e.GetString())) (Some "hello") "Prompt present"
              Expect.isNone (field body "CurrentTreeJson") "no CurrentTreeJson"
              Expect.isNone (field body "ByokKey") "no ByokKey"
              Expect.isNone (field body "AccessToken") "no AccessToken"
              Expect.isNone (field body "DisableCorpusRead") "no DisableCorpusRead"
              Expect.isNone (field body "ContributeCorpus") "no ContributeCorpus"
          }

          test "full args emit canonical PascalCase keys with the right JSON kinds" {
              let args =
                  { Prompt = "edit it"
                    CurrentTreeJson = Some validTreeJson
                    ProviderKey = None
                    AccessToken = None
                    DisableCorpusRead = Some true
                    ContributeCorpus = Some false }

              let body =
                  Wire.toWireBody
                      args
                      { ProviderKey = Some "sk-key"
                        AccessToken = Some "tok" }

              Expect.equal (field body "Prompt" |> Option.map (fun e -> e.GetString())) (Some "edit it") "Prompt"

              Expect.equal
                  (field body "CurrentTreeJson" |> Option.map (fun e -> e.GetString()))
                  (Some validTreeJson)
                  "CurrentTreeJson"

              Expect.equal
                  (field body "ByokKey" |> Option.map (fun e -> e.GetString()))
                  (Some "sk-key")
                  "ByokKey from secrets"

              Expect.equal
                  (field body "AccessToken" |> Option.map (fun e -> e.GetString()))
                  (Some "tok")
                  "AccessToken from secrets"

              Expect.equal
                  (field body "DisableCorpusRead" |> Option.map (fun e -> e.GetBoolean()))
                  (Some true)
                  "DisableCorpusRead boolean"

              Expect.equal
                  (field body "ContributeCorpus" |> Option.map (fun e -> e.GetBoolean()))
                  (Some false)
                  "ContributeCorpus boolean"
          } ]

[<Tests>]
let parseTests =
    testList
        "Wire.parseTurnResponse"
        [ test "200 → Produced with treeJson, ops, version" {
              let body =
                  """{"TreeJson":"<t>","Ops":[{"OpId":"o1","OpJson":"{}"}],"Version":"1.4.0"}"""

              match Wire.parseTurnResponse 200 body with
              | TurnResult.Produced(treeJson, ops, version) ->
                  Expect.equal treeJson "<t>" "treeJson"
                  Expect.equal version "1.4.0" "version echo"
                  Expect.equal ops [ { OpId = "o1"; OpJson = "{}" } ] "ops parsed"
              | other -> failtestf "expected Produced, got %A" other
          }

          test "200 tolerates camelCase keys" {
              let body =
                  """{"treeJson":"<t>","ops":[{"opId":"o1","opJson":"{}"}],"version":"1.4.0"}"""

              match Wire.parseTurnResponse 200 body with
              | TurnResult.Produced(treeJson, ops, version) ->
                  Expect.equal treeJson "<t>" "treeJson"
                  Expect.equal version "1.4.0" "version"
                  Expect.equal (List.length ops) 1 "one op"
              | other -> failtestf "expected Produced, got %A" other
          }

          test "401 → AccessDenied reason" {
              match Wire.parseTurnResponse 401 """{"Reason":"expired"}""" with
              | TurnResult.AccessDenied reason -> Expect.equal reason "expired" "reason"
              | other -> failtestf "expected AccessDenied, got %A" other
          }

          test "422 flat envelope → TurnFailed with stage/code/message" {
              let body = """{"Stage":"apply","Code":"APPLY_REJECTED","Message":"no such node"}"""

              match Wire.parseTurnResponse 422 body with
              | TurnResult.TurnFailed err ->
                  Expect.equal err.Stage TurnStage.Apply "stage parsed from label"
                  Expect.equal err.Code "APPLY_REJECTED" "code"
                  Expect.equal err.Message "no such node" "message"
              | other -> failtestf "expected TurnFailed, got %A" other
          }

          test "422 envelope nested under Error → TurnFailed" {
              let body = """{"Error":{"Stage":"parse","Code":"BAD","Message":"nope"}}"""

              match Wire.parseTurnResponse 422 body with
              | TurnResult.TurnFailed err ->
                  Expect.equal err.Stage TurnStage.Parse "nested stage"
                  Expect.equal err.Code "BAD" "nested code"
              | other -> failtestf "expected TurnFailed, got %A" other
          }

          test "unexpected status → TurnFailed provider HTTP_<status>" {
              match Wire.parseTurnResponse 500 """{"Message":"boom"}""" with
              | TurnResult.TurnFailed err ->
                  Expect.equal err.Stage TurnStage.Provider "provider stage"
                  Expect.equal err.Code "HTTP_500" "HTTP_ code"
                  Expect.equal err.Message "boom" "detail from body"
              | other -> failtestf "expected TurnFailed, got %A" other
          }

          test "empty body on unexpected status → synthesised detail" {
              match Wire.parseTurnResponse 503 "" with
              | TurnResult.TurnFailed err ->
                  Expect.equal err.Code "HTTP_503" "code"
                  Expect.stringContains err.Message "503" "synthesised message names the status"
              | other -> failtestf "expected TurnFailed, got %A" other
          } ]

[<Tests>]
let clientTests =
    testList
        "FuaranClient.Generate"
        [ testAsync "sends Authorization Bearer + body token when configured" {
              let client, transport =
                  MockTransport.client
                      { cfg with AccessToken = Some "tok" }
                      200
                      """{"TreeJson":"t","Ops":[],"Version":"1.2.0"}"""

              let! _ = client.Generate(GenerateArgs.prompt "x")
              let captured = Option.get transport.Captured
              Expect.equal (Map.tryFind "authorization" captured.Headers) (Some "Bearer tok") "bearer header"
              Expect.stringContains captured.Body "\"AccessToken\":\"tok\"" "token also in body"
              Expect.equal captured.Endpoint cfg.Endpoint "endpoint targeted"
          }

          testAsync "SendBearerHeader=false suppresses the Authorization header" {
              let client, transport =
                  MockTransport.client
                      { cfg with
                          AccessToken = Some "tok"
                          SendBearerHeader = false }
                      200
                      """{"TreeJson":"t","Ops":[],"Version":"1.2.0"}"""

              let! _ = client.Generate(GenerateArgs.prompt "x")
              let captured = Option.get transport.Captured
              Expect.isNone (Map.tryFind "authorization" captured.Headers) "no bearer header"
          }

          testAsync "a transport throw surfaces as a NETWORK provider failure" {
              let transport =
                  MockTransport({ Status = 200; Body = "" }, System.Exception "socket reset")

              let client =
                  FuaranClient(
                      { cfg with
                          Transport = Some(transport :> IFuaranTransport) }
                  )

              let! result = client.Generate(GenerateArgs.prompt "x")

              match result with
              | TurnResult.TurnFailed err ->
                  Expect.equal err.Stage TurnStage.Provider "provider stage"
                  Expect.equal err.Code "NETWORK" "NETWORK code"
                  Expect.equal err.Message "socket reset" "carries the exception message"
              | other -> failtestf "expected TurnFailed, got %A" other
          } ]

[<Tests>]
let sessionTests =
    testList
        "FuaranSession"
        [ testAsync "first turn is a fresh generation; a produced turn advances the held tree" {
              let client, transport =
                  MockTransport.client
                      cfg
                      200
                      $"""{{"TreeJson":{JsonSerializer.Serialize validTreeJson},"Ops":[],"Version":"1.2.0"}}"""

              let session = FuaranSession(client)
              Expect.isNone session.CurrentTreeJson "no tree before the first turn"

              let! _ = session.Next "make a badge"
              // first turn omits CurrentTreeJson (fresh generation)
              Expect.isFalse
                  ((Option.get transport.Captured).Body.Contains "CurrentTreeJson")
                  "fresh turn omits CurrentTreeJson"

              Expect.equal session.CurrentTreeJson (Some validTreeJson) "session advanced to the produced tree"

              let! _ = session.Next "tweak it"
              // second turn sends the held tree as a repair diff
              Expect.stringContains
                  (Option.get transport.Captured).Body
                  "CurrentTreeJson"
                  "repair turn carries the held tree"
          }

          testAsync "a failed turn leaves the held tree unchanged" {
              // Seed a tree, then a turn that fails must not clobber it.
              let transport =
                  MockTransport(
                      { Status = 422
                        Body = """{"Stage":"apply","Code":"X","Message":"m"}""" }
                  )

              let client =
                  FuaranClient(
                      { cfg with
                          Transport = Some(transport :> IFuaranTransport) }
                  )

              let session = FuaranSession(client, initialTreeJson = validTreeJson)

              let! result = session.Next "break it"
              Expect.equal session.CurrentTreeJson (Some validTreeJson) "held tree unchanged after failure"

              match result with
              | TurnResult.TurnFailed _ -> ()
              | other -> failtestf "expected TurnFailed, got %A" other
          } ]

[<Tests>]
let miscTests =
    testList
        "surface + render glue"
        [ test "isVersionCompatible compares only the major" {
              Expect.isTrue (SurfaceContract.isVersionCompatible "1.4.0") "same major (1.x)"
              Expect.isTrue (SurfaceContract.isVersionCompatible "1.99.3") "same major, higher minor"
              Expect.isFalse (SurfaceContract.isVersionCompatible "2.0.0") "different major"
              Expect.isFalse (SurfaceContract.isVersionCompatible "") "empty is incompatible"
          }

          test "decodeProduced decodes a produced tree and is None for other cases" {
              let produced = TurnResult.Produced(validTreeJson, [], "1.4.0")

              match Render.decodeProduced produced with
              | Some(Ok node) -> Expect.equal node.Id "badge-1" "decoded the node id"
              | Some(Error e) -> failtestf "expected Ok decode, got error %s" e.Message
              | None -> failtest "expected Some for a Produced result"

              Expect.isNone (Render.decodeProduced (TurnResult.AccessDenied "no")) "None for AccessDenied"

              Expect.isNone
                  (Render.decodeProduced (
                      TurnResult.TurnFailed
                          { Stage = TurnStage.Parse
                            Code = "c"
                            Message = "m" }
                  ))
                  "None for TurnFailed"
          } ]
