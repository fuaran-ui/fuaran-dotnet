// The server-proxied BYOK route, tested through the transport seam.
//
// `Proxy.handle` is driven against a recording transport that replays the local
// mock's canonical responses, so the route's real behaviour — parsing, the
// credential-ignoring rule, and the response shaping — is asserted without a
// socket or a live credential. (The same route is additionally exercised against
// the running mock end to end; see the sample README.)

module Fuaran.Sample.SdkIntegration.Tests.ProxyTests

open Expecto

open Fuaran.UI.Client
open Fuaran.Sample.SdkIntegration.Server

/// The mock's canonical reply for a metric prompt.
let private producedBody =
    """{"TreeJson":"{\"id\":\"metric-1\",\"kind\":{\"$type\":\"Badge\",\"label\":\"A\",\"variant\":\"Info\"}}","Ops":[],"Version":"1.2.0"}"""

/// A transport that records the outbound body and replays a scripted reply —
/// the seam `Fuaran.UI.Client` exposes for exactly this.
type private RecordingTransport(status: int, body: string) =
    let mutable sent: string option = None
    member _.Sent = sent

    interface IFuaranTransport with
        member _.Send(_endpoint, _headers, body') =
            async {
                sent <- Some body'
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

[<Tests>]
let proxyTests =
    testList
        "SdkIntegration.Proxy — the server-proxied BYOK route"
        [ testAsync "a bare prompt yields the produced tree" {
              let transport = RecordingTransport(200, producedBody)
              let! status, payload = Proxy.handle (serverClient transport) """{"Prompt":"a metric strip"}"""
              Expect.equal status 200 "200"
              Expect.stringContains payload "TreeJson" "returns the tree"
              Expect.stringContains payload "Version" "echoes the surface version"
          }

          testAsync "the prompt and the tree under repair reach the endpoint" {
              let transport = RecordingTransport(200, producedBody)

              let! _ =
                  Proxy.handle
                      (serverClient transport)
                      """{"Prompt":"tweak it","CurrentTreeJson":"{\"id\":\"held\"}"}"""

              let sent = Option.get transport.Sent
              Expect.stringContains sent "tweak it" "prompt forwarded"
              Expect.stringContains sent "CurrentTreeJson" "repair diff forwarded"
          }

          testAsync "client-supplied credentials are IGNORED, never merged" {
              // The security-critical rule: a caller cannot supply, override, or
              // probe the server's credentials.
              let transport = RecordingTransport(200, producedBody)

              let! _, payload =
                  Proxy.handle
                      (serverClient transport)
                      """{"Prompt":"x","AccessToken":"attacker-token","ByokKey":"sk-attacker"}"""

              let sent = Option.get transport.Sent
              Expect.isFalse (sent.Contains "attacker-token") "the attacker's token never goes upstream"
              Expect.isFalse (sent.Contains "sk-attacker") "the attacker's key never goes upstream"
              Expect.stringContains sent "server-token" "the server's own token is used"
              Expect.stringContains sent "server-key" "the server's own key is used"
              // …and nothing secret comes back down either.
              Expect.isFalse (payload.Contains "server-token") "no credential echoed to the browser"
              Expect.isFalse (payload.Contains "server-key") "no credential echoed to the browser"
          }

          testAsync "an empty prompt is refused without calling the endpoint" {
              let transport = RecordingTransport(200, producedBody)
              let! status, _ = Proxy.handle (serverClient transport) """{"Prompt":"   "}"""
              Expect.equal status 422 "422"
              Expect.isNone transport.Sent "the endpoint was never called — no token spent"
          }

          testAsync "a malformed body is refused, never a crash" {
              let transport = RecordingTransport(200, producedBody)
              let! status, _ = Proxy.handle (serverClient transport) "not json{{"
              Expect.equal status 422 "422"
          }

          testAsync "access denied maps to 401 with only the reason" {
              let transport = RecordingTransport(401, """{"Reason":"token expired"}""")
              let! status, payload = Proxy.handle (serverClient transport) """{"Prompt":"x"}"""
              Expect.equal status 401 "401"
              Expect.stringContains payload "token expired" "carries the reason"
          }

          testAsync "a recoverable failure maps to 422 with the envelope" {
              let transport =
                  RecordingTransport(422, """{"Stage":"apply","Code":"APPLY_REJECTED","Message":"no node #x"}""")

              let! status, payload = Proxy.handle (serverClient transport) """{"Prompt":"x"}"""
              Expect.equal status 422 "422"
              Expect.stringContains payload "apply" "carries the stage"
              Expect.stringContains payload "APPLY_REJECTED" "carries the code"
              Expect.stringContains payload "no node #x" "carries the model-facing hint"
          } ]
