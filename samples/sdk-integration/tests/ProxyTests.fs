// The server-proxied BYOK route, tested through the transport seam.
//
// `Proxy.handle` is driven against a recording transport that replays the
// endpoint's canonical responses, so the route's real behaviour — parsing, the
// credential-ignoring rule, the three spend guards, and the response shaping —
// is asserted without a socket or a live credential. (The same route is
// additionally exercised against the running mock end to end; see the sample
// README.)

module Fuaran.Sample.SdkIntegration.Tests.ProxyTests

open System
open Expecto

open Fuaran.UI.Client
open Fuaran.Sample.SdkIntegration.Server

/// The endpoint's canonical 200 for a metric prompt: the tree as an OBJECT and
/// `opsApplied` as a count, exactly as the deployed surface serves it.
let private producedBody =
    """{"version":"1.6.0","tree":{"id":"metric-1","kind":{"$type":"Badge","label":"A","variant":"Info"}},"opsApplied":0,"provider":"openai","servedModel":"gpt-4o-2024-11-20","snapshot":{"state":"warm"}}"""

/// A transport that records the outbound body and replays a scripted reply —
/// the seam `Fuaran.UI.Client` exposes for exactly this.
type private RecordingTransport(status: int, body: string) =
    let mutable sent: string option = None
    let mutable headers: Map<string, string> = Map.empty
    let mutable calls = 0
    member _.Sent = sent
    member _.Headers = headers
    member _.Calls = calls

    interface IFuaranTransport with
        member _.Send(_endpoint, headers', body') =
            async {
                sent <- Some body'
                headers <- headers'
                calls <- calls + 1
                return { Status = status; Body = body }
            }

/// A client whose credentials are the SERVER's, wired over the recording seam.
let private serverClient (transport: IFuaranTransport) =
    FuaranClient(
        { FuaranClientConfig.create "https://endpoint.example/api" with
            AccessToken = Some "server-token"
            ProviderKey = Some "server-key"
            Transport = Some transport }
    )

/// A caller the tests can name, and a policy that admits it.
let private caller = Proxy.CallerId "test-caller"
let private openPolicy = Proxy.Policy.create Proxy.Policy.anonymous

let private handle transport body =
    Proxy.handle openPolicy caller (serverClient transport) body

[<Tests>]
let proxyTests =
    testList
        "SdkIntegration.Proxy — the server-proxied BYOK route"
        [ testAsync "a bare prompt yields the produced tree in the endpoint's own shape" {
              let transport = RecordingTransport(200, producedBody)
              let! status, payload = handle transport """{"prompt":"a metric strip"}"""
              Expect.equal status 200 "200"
              Expect.stringContains payload "\"tree\"" "returns the tree under the deployed key"
              Expect.stringContains payload "\"version\"" "echoes the surface version"
              Expect.stringContains payload "\"opsApplied\"" "and how much changed"

              Expect.stringContains
                  payload
                  "gpt-4o-2024-11-20"
                  "the served model is forwarded rather than dropped at the proxy hop"
          }

          testAsync "the prompt and the tree under repair reach the endpoint" {
              let transport = RecordingTransport(200, producedBody)

              let! _ = handle transport """{"prompt":"tweak it","currentTree":"{\"id\":\"held\"}"}"""

              let sent = Option.get transport.Sent
              Expect.stringContains sent "tweak it" "prompt forwarded"
              Expect.stringContains sent "currentTree" "repair diff forwarded"
          }

          testAsync "the retired PascalCase request from an older browser client still parses" {
              let transport = RecordingTransport(200, producedBody)
              let! status, _ = handle transport """{"Prompt":"a metric strip"}"""
              Expect.equal status 200 "an old page against a new proxy keeps working"
          }

          testAsync "client-supplied credentials are IGNORED, never merged" {
              // The security-critical rule: a caller cannot supply, override, or
              // probe the server's credentials.
              let transport = RecordingTransport(200, producedBody)

              let! _, payload =
                  handle transport """{"prompt":"x","AccessToken":"attacker-token","ByokKey":"sk-attacker"}"""

              let sent = Option.get transport.Sent
              Expect.isFalse (sent.Contains "attacker-token") "the attacker's token never goes upstream"
              Expect.isFalse (sent.Contains "sk-attacker") "the attacker's key never goes upstream"

              // The server's own credentials go up as HEADERS, and the body the
              // endpoint receives carries no secret at all.
              Expect.equal
                  (Map.tryFind "x-fuaran-provider-key" transport.Headers)
                  (Some "server-key")
                  "the server's own key is used, in its header"

              Expect.isFalse (sent.Contains "server-token") "and no secret is in the forwarded body"
              Expect.isFalse (sent.Contains "server-key") "neither of them"
              // …and nothing secret comes back down either.
              Expect.isFalse (payload.Contains "server-token") "no credential echoed to the browser"
              Expect.isFalse (payload.Contains "server-key") "no credential echoed to the browser"
          }

          testAsync "an empty prompt is refused without calling the endpoint" {
              let transport = RecordingTransport(200, producedBody)
              let! status, _ = handle transport """{"prompt":"   "}"""
              Expect.equal status 400 "400 BAD_REQUEST"
              Expect.isNone transport.Sent "the endpoint was never called — no token spent"
          }

          testAsync "a malformed body is refused, never a crash" {
              let transport = RecordingTransport(200, producedBody)
              let! status, _ = handle transport "not json{{"
              Expect.equal status 400 "400"
          }

          testAsync "access denied maps to 401 in the endpoint's envelope" {
              let transport =
                  RecordingTransport(401, """{"error":{"code":"ACCESS_DENIED","message":"token expired"}}""")

              let! status, payload = handle transport """{"prompt":"x"}"""
              Expect.equal status 401 "401"
              Expect.stringContains payload "token expired" "carries the reason"
          }

          testAsync "a recoverable failure maps to 422 with the envelope" {
              let transport =
                  RecordingTransport(
                      422,
                      """{"error":{"code":"APPLY_REJECTED","message":"no node #x","stage":"apply"}}"""
                  )

              let! status, payload = handle transport """{"prompt":"x"}"""
              Expect.equal status 422 "422"
              Expect.stringContains payload "apply" "carries the stage"
              Expect.stringContains payload "APPLY_REJECTED" "carries the code"
              Expect.stringContains payload "no node #x" "carries the model-facing hint"
          } ]

[<Tests>]
let spendGuardTests =
    testList
        "SdkIntegration.Proxy — the route is not an open spend endpoint"
        [ testAsync "an unauthorised caller is refused before the endpoint is called" {
              // Red before the guards existed: this route answered anyone who
              // could reach it, with the operator's BYOK key.
              let transport = RecordingTransport(200, producedBody)

              let policy =
                  Proxy.Policy.create (Proxy.Policy.sharedSecret "s3cret" (fun _ -> Some "wrong"))

              let! status, payload = Proxy.handle policy caller (serverClient transport) """{"prompt":"x"}"""

              Expect.equal status 401 "401"
              Expect.stringContains payload "ACCESS_DENIED" "the coded refusal"
              Expect.isNone transport.Sent "no turn ran, so nothing was spent"
          }

          testAsync "the right secret is admitted" {
              let transport = RecordingTransport(200, producedBody)

              let policy =
                  Proxy.Policy.create (Proxy.Policy.sharedSecret "s3cret" (fun _ -> Some "s3cret"))

              let! status, _ = Proxy.handle policy caller (serverClient transport) """{"prompt":"x"}"""
              Expect.equal status 200 "200"
          }

          testAsync "a prompt past the cap is refused before the endpoint is called" {
              let transport = RecordingTransport(200, producedBody)
              let long = String.replicate (Proxy.Policy.DefaultMaxPromptLength + 1) "x"
              let! status, payload = handle transport $"""{{"prompt":"{long}"}}"""

              Expect.equal status 400 "400"
              Expect.stringContains payload "PROMPT_TOO_LONG" "the coded refusal"
              Expect.isNone transport.Sent "prompt length is the input-token bill; none of it was paid"
          }

          testAsync "a caller past the window is rate limited, and the window resets" {
              let transport = RecordingTransport(200, producedBody)
              let mutable now = DateTimeOffset.UnixEpoch

              let policy =
                  { Proxy.Policy.create Proxy.Policy.anonymous with
                      RateLimiter =
                          Some(
                              Proxy.RateLimiter(
                                  { MaxTurns = 2
                                    Window = TimeSpan.FromMinutes 1.0 },
                                  (fun () -> now)
                              )
                          ) }

              let client = serverClient transport

              let turn () =
                  Proxy.handle policy caller client """{"prompt":"x"}"""

              let! first = turn ()
              let! second = turn ()
              let! third = turn ()

              Expect.equal (fst first) 200 "the first turn is served"
              Expect.equal (fst second) 200 "so is the second"
              Expect.equal (fst third) 429 "the third is rate limited"
              Expect.stringContains (snd third) "RATE_LIMITED" "the coded refusal"
              Expect.equal transport.Calls 2 "and the refused turn cost nothing"

              // The window is a window, not a permanent ban.
              now <- now.AddMinutes 2.0
              let! afterWindow = turn ()
              Expect.equal (fst afterWindow) 200 "the window reset"
          }

          testAsync "one caller's spend does not exhaust another's" {
              let transport = RecordingTransport(200, producedBody)

              let policy =
                  { Proxy.Policy.create Proxy.Policy.anonymous with
                      RateLimiter =
                          Some(
                              Proxy.RateLimiter(
                                  { MaxTurns = 1
                                    Window = TimeSpan.FromMinutes 1.0 },
                                  fun () -> DateTimeOffset.UtcNow
                              )
                          ) }

              let client = serverClient transport
              let! _ = Proxy.handle policy (Proxy.CallerId "a") client """{"prompt":"x"}"""
              let! secondForA = Proxy.handle policy (Proxy.CallerId "a") client """{"prompt":"x"}"""
              let! firstForB = Proxy.handle policy (Proxy.CallerId "b") client """{"prompt":"x"}"""

              Expect.equal (fst secondForA) 429 "a is limited"
              Expect.equal (fst firstForB) 200 "b is not"
          } ]
