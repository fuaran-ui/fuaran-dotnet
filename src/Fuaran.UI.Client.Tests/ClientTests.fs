// Fuaran.UI.Client tests — the wire round-trip against the DEPLOYED endpoint
// shape, the three-way result parsing, the transport-failure path, the session
// tree-carry loop, and the four hardening properties (timeout, endpoint scheme,
// malformed 200, no upstream exception text). Driven against a scripted
// MockTransport, so no live endpoint is touched. Parity-checked against the
// TypeScript @fuaran-ui/client and the Python fuaran_ui.client, which speak the
// same wire and synthesise the same codes.

module Fuaran.UI.Client.Tests.ClientTests

open System
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

/// A 200 in the shape the deployed endpoint serves: the tree as an OBJECT,
/// `opsApplied` as a COUNT, and the deployment facts beside them.
let private producedBody (opsApplied: int) =
    $"""{{"version":"1.6.0","tree":{validTreeJson},"opsApplied":{opsApplied},"provider":"openai","servedModel":"gpt-4o-2024-11-20","snapshot":{{"state":"warm","version":"7","contentHash":"sha256:abc"}}}}"""

[<Tests>]
let wireTests =
    testList
        "Wire.toWireBody"
        [ test "prompt-only writes the camelCase body and omits every optional field" {
              let body = Wire.toWireBody (GenerateArgs.prompt "hello")

              Expect.equal (field body "prompt" |> Option.map _.GetString()) (Some "hello") "prompt present"
              Expect.isNone (field body "currentTree") "no currentTree"
              Expect.isNone (field body "disableCorpusRead") "no disableCorpusRead"
              Expect.isNone (field body "contributeCorpus") "no contributeCorpus"
              Expect.isNone (field body "interactionId") "no interactionId"
          }

          test "no secret is expressible in the body, at any argument" {
              // The endpoint REFUSES a body carrying `ByokKey` / `AccessToken`
              // and this module gives no way to write one. Asserted over a
              // fully-populated request, because "the field is absent" is only
              // interesting when every other field is present.
              let args =
                  { GenerateArgs.prompt "edit it" with
                      CurrentTreeJson = Some validTreeJson
                      ProviderKey = Some "sk-key"
                      AccessToken = Some "tok"
                      DisableCorpusRead = Some true
                      ContributeCorpus = Some false
                      InteractionId = Some "corr-1" }

              let body = Wire.toWireBody args

              for secret in [ "ByokKey"; "byokKey"; "AccessToken"; "accessToken" ] do
                  Expect.isNone (field body secret) $"no {secret} member"

              Expect.isFalse (body.Contains "sk-key") "the provider key is nowhere in the body text"
              Expect.isFalse (body.Contains "tok") "nor is the access token"
          }

          test "full args emit the deployed camelCase keys with the right JSON kinds" {
              let args =
                  { GenerateArgs.prompt "edit it" with
                      CurrentTreeJson = Some validTreeJson
                      DisableCorpusRead = Some true
                      ContributeCorpus = Some false
                      InteractionId = Some "corr-1" }

              let body = Wire.toWireBody args

              Expect.equal (field body "prompt" |> Option.map _.GetString()) (Some "edit it") "prompt"

              Expect.equal
                  (field body "currentTree" |> Option.map _.GetString())
                  (Some validTreeJson)
                  "currentTree, as the caller's canonical bytes"

              Expect.equal
                  (field body "disableCorpusRead" |> Option.map _.GetBoolean())
                  (Some true)
                  "disableCorpusRead boolean"

              Expect.equal
                  (field body "contributeCorpus" |> Option.map _.GetBoolean())
                  (Some false)
                  "contributeCorpus boolean"

              Expect.equal (field body "interactionId" |> Option.map _.GetString()) (Some "corr-1") "interactionId"
          } ]

[<Tests>]
let parseTests =
    testList
        "Wire.parseTurnResponse"
        [ test "200 → Produced from the deployed object-tree reply" {
              match Wire.parseTurnResponse 200 (producedBody 1) with
              | TurnResult.Produced(treeJson, _, version) ->
                  Expect.equal version "1.6.0" "version echo"

                  // The tree comes back as the object's own raw text, which is
                  // what makes the next repair turn send back what was served.
                  match Render.decodeTreeJson treeJson with
                  | Ok node -> Expect.equal node.Id "badge-1" "the produced tree decodes"
                  | Error e -> failtestf "the produced tree did not decode: %s" e.Message
              | other -> failtestf "expected Produced, got %A" other
          }

          test "200 still parses a proxy's stringified tree and op list" {
              // A same-origin proxy or the offline mock may hand back the tree
              // as a JSON string with a full `ops` array. Reads stay tolerant;
              // writes do not.
              let body =
                  $"""{{"tree":{JsonSerializer.Serialize validTreeJson},"ops":[{{"opId":"o1","opJson":"{{}}"}}],"version":"1.6.0"}}"""

              match Wire.parseTurnResponse 200 body with
              | TurnResult.Produced(treeJson, ops, version) ->
                  Expect.equal treeJson validTreeJson "stringified tree"
                  Expect.equal version "1.6.0" "version"
                  Expect.equal ops [ { OpId = "o1"; OpJson = "{}" } ] "ops parsed when the reply carries them"
              | other -> failtestf "expected Produced, got %A" other
          }

          test "200 tolerates the retired PascalCase reply" {
              let body =
                  $"""{{"TreeJson":{JsonSerializer.Serialize validTreeJson},"Ops":[],"Version":"1.2.0"}}"""

              match Wire.parseTurnResponse 200 body with
              | TurnResult.Produced(treeJson, _, version) ->
                  Expect.equal treeJson validTreeJson "treeJson"
                  Expect.equal version "1.2.0" "version"
              | other -> failtestf "expected Produced, got %A" other
          }

          test "401 → AccessDenied from the nested envelope" {
              let body =
                  """{"error":{"code":"ACCESS_DENIED","message":"token expired — the request was refused before any provider call"}}"""

              match Wire.parseTurnResponse 401 body with
              | TurnResult.AccessDenied reason -> Expect.stringContains reason "token expired" "reason"
              | other -> failtestf "expected AccessDenied, got %A" other
          }

          test "401 still reads the retired bare `Reason` member" {
              match Wire.parseTurnResponse 401 """{"Reason":"expired"}""" with
              | TurnResult.AccessDenied reason -> Expect.equal reason "expired" "reason"
              | other -> failtestf "expected AccessDenied, got %A" other
          }

          test "422 nested envelope → TurnFailed with stage/code/message" {
              let body =
                  """{"error":{"code":"APPLY_REJECTED","message":"no such node","stage":"apply"}}"""

              match Wire.parseTurnResponse 422 body with
              | TurnResult.TurnFailed err ->
                  Expect.equal err.Stage TurnStage.Apply "stage parsed from the wire label"
                  Expect.equal err.Code "APPLY_REJECTED" "code"
                  Expect.equal err.Message "no such node" "message"
              | other -> failtestf "expected TurnFailed, got %A" other
          }

          test "422 still reads the retired flat PascalCase envelope" {
              let body = """{"Stage":"parse","Code":"BAD","Message":"nope"}"""

              match Wire.parseTurnResponse 422 body with
              | TurnResult.TurnFailed err ->
                  Expect.equal err.Stage TurnStage.Parse "stage"
                  Expect.equal err.Code "BAD" "code"
              | other -> failtestf "expected TurnFailed, got %A" other
          }

          test "a 400 refusal keeps the endpoint's OWN code, not HTTP_400" {
              // The whole reason the endpoint codes its refusals: a key sent in
              // the body must be rotated, and a missing key header must be
              // supplied. `HTTP_400` says neither.
              let body =
                  """{"error":{"code":"SECRETS_IN_BODY","message":"the request body carries ByokKey; rotate it"}}"""

              match Wire.parseTurnResponse 400 body with
              | TurnResult.TurnFailed err ->
                  Expect.equal err.Code "SECRETS_IN_BODY" "the endpoint's code survives"
                  Expect.stringContains err.Message "rotate" "and its message"
              | other -> failtestf "expected TurnFailed, got %A" other
          }

          test "405 / 500 / 503 all arrive through the same envelope" {
              for status, code in [ 405, "METHOD_NOT_ALLOWED"; 500, "TURN_FAULTED"; 503, "HOST_NOT_CONFIGURED" ] do
                  let body = $"""{{"error":{{"code":"{code}","message":"m"}}}}"""

                  match Wire.parseTurnResponse status body with
                  | TurnResult.TurnFailed err -> Expect.equal err.Code code $"{status} keeps its code"
                  | other -> failtestf "expected TurnFailed at %d, got %A" status other
          }

          test "empty body on an unexpected status → synthesised detail" {
              match Wire.parseTurnResponse 502 "" with
              | TurnResult.TurnFailed err ->
                  Expect.equal err.Code "HTTP_502" "code"
                  Expect.stringContains err.Message "502" "synthesised message names the status"
              | other -> failtestf "expected TurnFailed, got %A" other
          }

          test "parseProducedDetail reads the deployment facts off a 200" {
              match Wire.parseProducedDetail 200 (producedBody 3) with
              | Some detail ->
                  Expect.equal detail.OpsApplied 3 "opsApplied is the count on the wire"
                  Expect.equal detail.Provider (Some "openai") "provider"
                  Expect.equal detail.ServedModel (Some "gpt-4o-2024-11-20") "servedModel"
                  Expect.equal (detail.Snapshot |> Option.map _.State) (Some "warm") "snapshot state"
              | None -> failtest "expected a detail for a produced reply"
          }

          test "an absent servedModel reads as unreported, never as the asked-for model" {
              let body =
                  $"""{{"version":"1.6.0","tree":{validTreeJson},"opsApplied":0,"provider":"openai","snapshot":{{"state":"cold"}}}}"""

              match Wire.parseProducedDetail 200 body with
              | Some detail ->
                  Expect.isNone detail.ServedModel "absence is read as absence"
                  Expect.equal detail.Provider (Some "openai") "the provider is still named"
              | None -> failtest "expected a detail"
          }

          test "an op list without a count still yields an opsApplied" {
              let body =
                  $"""{{"tree":{JsonSerializer.Serialize validTreeJson},"ops":[{{"opId":"o1","opJson":"{{}}"}}],"version":"1.6.0"}}"""

              match Wire.parseProducedDetail 200 body with
              | Some detail -> Expect.equal detail.OpsApplied 1 "counted from the list a proxy sent"
              | None -> failtest "expected a detail"
          }

          test "no detail for a refusal, and none for a malformed 200" {
              Expect.isNone (Wire.parseProducedDetail 422 """{"error":{"code":"X","message":"m"}}""") "no detail on 422"

              Expect.isNone
                  (Wire.parseProducedDetail 200 """{"version":"1.6.0","opsApplied":0}""")
                  "no detail on a 200 the turn itself rejects"
          } ]

[<Tests>]
let hardeningTests =
    testList
        "FuaranClient hardening"
        [ test "a 200 with no tree is MALFORMED_RESPONSE, not Produced \"\"" {
              // Red before 0.77.0: this returned `Produced("")`, and the
              // session then held "" as the current tree and repaired nothing
              // on every subsequent turn — a fault that surfaces one turn later
              // than the reply that caused it.
              for body in [ """{"version":"1.6.0","opsApplied":0}"""; "{}"; "not json at all"; "" ] do
                  match Wire.parseTurnResponse 200 body with
                  | TurnResult.TurnFailed err ->
                      Expect.equal err.Code ClientCode.MalformedResponse $"MALFORMED_RESPONSE for {body}"
                  | other -> failtestf "expected TurnFailed for %s, got %A" body other
          }

          testAsync "a malformed 200 leaves the session's held tree alone" {
              let transport =
                  ScriptedTransport(
                      [ { Status = 200; Body = producedBody 0 }
                        { Status = 200
                          Body = """{"version":"1.6.0","opsApplied":0}""" } ]
                  )

              let client =
                  FuaranClient(
                      { cfg with
                          Transport = Some(transport :> IFuaranTransport) }
                  )

              let session = FuaranSession(client)
              let! _ = session.Next "make a badge"
              Expect.equal session.CurrentTreeJson (Some validTreeJson) "the good turn advanced the session"

              let! second = session.Next "break it"

              match second with
              | TurnResult.TurnFailed err -> Expect.equal err.Code ClientCode.MalformedResponse "the second turn failed"
              | other -> failtestf "expected TurnFailed, got %A" other

              Expect.equal session.CurrentTreeJson (Some validTreeJson) "and the held tree is untouched"
          }

          testAsync "a plaintext non-loopback endpoint is refused before anything is sent" {
              let transport = MockTransport({ Status = 200; Body = producedBody 0 })

              let client =
                  FuaranClient(
                      { FuaranClientConfig.create "http://api.example.com/generate" with
                          AccessToken = Some "tok"
                          ProviderKey = Some "sk-key"
                          Transport = Some(transport :> IFuaranTransport) }
                  )

              let! result = client.Generate(GenerateArgs.prompt "x")

              match result with
              | TurnResult.TurnFailed err -> Expect.equal err.Code ClientCode.InsecureEndpoint "INSECURE_ENDPOINT"
              | other -> failtestf "expected TurnFailed, got %A" other

              Expect.isNone transport.Captured "the transport was never reached, so no credential left the process"
          }

          testAsync "loopback, https and a relative proxy path are all admitted" {
              for endpoint in
                  [ "http://127.0.0.1:8123"
                    "http://localhost:8123/generate"
                    "https://api.example.com/generate"
                    "/api/fuaran" ] do
                  let transport = MockTransport({ Status = 200; Body = producedBody 0 })

                  let client =
                      FuaranClient(
                          { FuaranClientConfig.create endpoint with
                              Transport = Some(transport :> IFuaranTransport) }
                      )

                  let! result = client.Generate(GenerateArgs.prompt "x")

                  match result with
                  | TurnResult.Produced _ -> ()
                  | other -> failtestf "expected %s to be admitted, got %A" endpoint other
          }

          testAsync "AllowInsecureEndpoint is the deliberate opt-out" {
              let transport = MockTransport({ Status = 200; Body = producedBody 0 })

              let client =
                  FuaranClient(
                      { FuaranClientConfig.create "http://api.example.com/generate" with
                          Transport = Some(transport :> IFuaranTransport)
                          AllowInsecureEndpoint = true }
                  )

              let! result = client.Generate(GenerateArgs.prompt "x")

              match result with
              | TurnResult.Produced _ -> Expect.isSome transport.Captured "the request was sent"
              | other -> failtestf "expected Produced, got %A" other
          }

          testAsync "an elapsed Timeout is NETWORK with a fixed message" {
              let slow =
                  { new IFuaranTransport with
                      member _.Send(_endpoint, _headers, _body) =
                          async {
                              do! Async.Sleep 5000
                              return { Status = 200; Body = producedBody 0 }
                          } }

              let client =
                  FuaranClient(
                      { cfg with
                          Transport = Some slow
                          Timeout = Some(TimeSpan.FromMilliseconds 50.0) }
                  )

              let! result = client.Generate(GenerateArgs.prompt "x")

              match result with
              | TurnResult.TurnFailed err ->
                  Expect.equal err.Code ClientCode.Network "NETWORK"
                  Expect.stringContains err.Message "timed out" "a fixed message naming the timeout"
              | other -> failtestf "expected TurnFailed, got %A" other
          }

          testAsync "a cancelled call is reported, not raised" {
              use cts = new Threading.CancellationTokenSource()

              let slow =
                  { new IFuaranTransport with
                      member _.Send(_endpoint, _headers, _body) =
                          async {
                              do! Async.Sleep 5000
                              return { Status = 200; Body = producedBody 0 }
                          } }

              let client = FuaranClient({ cfg with Transport = Some slow })

              cts.CancelAfter 50
              let! result = client.Generate(GenerateArgs.prompt "x", cts.Token)

              match result with
              | TurnResult.TurnFailed err -> Expect.equal err.Code ClientCode.Network "NETWORK"
              | other -> failtestf "expected TurnFailed, got %A" other
          }

          testAsync "no upstream exception text reaches the caller" {
              // Red before 0.77.0: the message was `ex.Message` verbatim,
              // and this result is routinely rendered into a browser.
              let transport =
                  MockTransport(
                      { Status = 200; Body = "" },
                      Exception "connection to https://internal-proxy.corp:9443 reset by peer"
                  )

              let client =
                  FuaranClient(
                      { cfg with
                          Transport = Some(transport :> IFuaranTransport) }
                  )

              let! result = client.Generate(GenerateArgs.prompt "x")

              match result with
              | TurnResult.TurnFailed err ->
                  Expect.equal err.Code ClientCode.Network "NETWORK code"
                  Expect.isFalse (err.Message.Contains "internal-proxy") "the upstream hostname does not leak"
                  Expect.isFalse (err.Message.Contains "reset by peer") "nor the upstream text"
              | other -> failtestf "expected TurnFailed, got %A" other
          } ]

[<Tests>]
let clientTests =
    testList
        "FuaranClient.Generate"
        [ testAsync "the secrets travel as headers and nothing else" {
              let client, transport =
                  MockTransport.client
                      { cfg with
                          AccessToken = Some "tok"
                          ProviderKey = Some "sk-key"
                          Provider = Some "openai" }
                      200
                      (producedBody 0)

              let! _ = client.Generate(GenerateArgs.prompt "x")
              let captured = Option.get transport.Captured

              Expect.equal (Map.tryFind "authorization" captured.Headers) (Some "Bearer tok") "bearer header"

              Expect.equal
                  (Map.tryFind "x-fuaran-provider-key" captured.Headers)
                  (Some "sk-key")
                  "the BYOK key is a header"

              Expect.equal (Map.tryFind "x-fuaran-provider" captured.Headers) (Some "openai") "the provider selector"
              Expect.isFalse (captured.Body.Contains "sk-key") "and the body carries neither secret"
              Expect.isFalse (captured.Body.Contains "tok") "neither the token"
              Expect.equal captured.Endpoint cfg.Endpoint "endpoint targeted"
          }

          testAsync "SendBearerHeader=false suppresses the Authorization header" {
              let client, transport =
                  MockTransport.client
                      { cfg with
                          AccessToken = Some "tok"
                          SendBearerHeader = false }
                      200
                      (producedBody 0)

              let! _ = client.Generate(GenerateArgs.prompt "x")
              let captured = Option.get transport.Captured
              Expect.isNone (Map.tryFind "authorization" captured.Headers) "no bearer header"
          }

          testAsync "GenerateDetailed hands back the deployment facts beside the result" {
              let client, _ = MockTransport.client cfg 200 (producedBody 2)
              let! result, detail = client.GenerateDetailed(GenerateArgs.prompt "x")

              match result with
              | TurnResult.Produced _ -> ()
              | other -> failtestf "expected Produced, got %A" other

              Expect.equal (detail |> Option.map _.OpsApplied) (Some 2) "opsApplied"
              Expect.equal (detail |> Option.bind _.ServedModel) (Some "gpt-4o-2024-11-20") "servedModel"
          }

          testAsync "a transport throw surfaces as a NETWORK provider failure" {
              let transport = MockTransport({ Status = 200; Body = "" }, Exception "socket reset")

              let client =
                  FuaranClient(
                      { cfg with
                          Transport = Some(transport :> IFuaranTransport) }
                  )

              let! result = client.Generate(GenerateArgs.prompt "x")

              match result with
              | TurnResult.TurnFailed err ->
                  Expect.equal err.Stage TurnStage.Provider "provider stage"
                  Expect.equal err.Code ClientCode.Network "NETWORK code"
              | other -> failtestf "expected TurnFailed, got %A" other
          } ]

[<Tests>]
let sessionTests =
    testList
        "FuaranSession"
        [ testAsync "first turn is a fresh generation; a produced turn advances the held tree" {
              let client, transport = MockTransport.client cfg 200 (producedBody 0)

              let session = FuaranSession(client)
              Expect.isNone session.CurrentTreeJson "no tree before the first turn"

              let! _ = session.Next "make a badge"
              // first turn omits currentTree (fresh generation)
              Expect.isFalse
                  ((Option.get transport.Captured).Body.Contains "currentTree")
                  "fresh turn omits currentTree"

              Expect.equal session.CurrentTreeJson (Some validTreeJson) "session advanced to the produced tree"

              let! _ = session.Next "tweak it"
              // second turn sends the held tree as a repair diff
              Expect.stringContains
                  (Option.get transport.Captured).Body
                  "currentTree"
                  "repair turn carries the held tree"
          }

          testAsync "a failed turn leaves the held tree unchanged" {
              // Seed a tree, then a turn that fails must not clobber it.
              let transport =
                  MockTransport(
                      { Status = 422
                        Body = """{"error":{"code":"X","message":"m","stage":"apply"}}""" }
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
              let produced = TurnResult.Produced(validTreeJson, [], "1.6.0")

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
