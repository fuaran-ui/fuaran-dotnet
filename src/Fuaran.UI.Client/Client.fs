// Fuaran.UI.Client — the HTTPS client over the Fuaran generation endpoint.
//
// `client.Generate(GenerateArgs.prompt "…")` collapses the integration to one
// call: it builds the request, sends it over an injectable transport, and
// returns the typed three-way result. No hand-rolled HTTP, no JSON wrangling,
// no token plumbing. The transport is a seam (`IFuaranTransport`) so a test
// drives it against a scripted mock with no live endpoint — the F# analogue of
// the TypeScript client's injectable `fetch`.

namespace Fuaran.UI.Client

open System
open System.Net.Http
open System.Text

open Fuaran.UI.Types
open Fuaran.UI.Ops

/// One HTTP reply: the status code and the raw body text. The status selects
/// the `TurnResult` case; the body supplies the payload (see `Wire.fs`).
type HttpResult = { Status: int; Body: string }

/// The minimal HTTP shape the client needs — an async POST that returns a
/// (status, body) pair. Satisfied by the default `HttpClientTransport`, and
/// trivial to mock in a test (return a scripted `HttpResult`; throw to exercise
/// the transport-failure path).
type IFuaranTransport =
    abstract member Send: endpoint: string * headers: Map<string, string> * body: string -> Async<HttpResult>

/// The default transport over a shared `System.Net.Http.HttpClient`. Reuses one
/// client across calls (the framework's guidance — a client per call exhausts
/// sockets). `content-type` is applied to the request body; every other header
/// (including `Authorization`) is added to the request message.
type HttpClientTransport(?httpClient: HttpClient) =
    static let shared = lazy (new HttpClient())
    let client = defaultArg httpClient shared.Value

    interface IFuaranTransport with
        member _.Send(endpoint, headers, body) =
            async {
                let contentType =
                    headers
                    |> Map.tryPick (fun k v ->
                        if String.Equals(k, "content-type", StringComparison.OrdinalIgnoreCase) then
                            Some v
                        else
                            None)
                    |> Option.defaultValue "application/json"

                use content = new StringContent(body, Encoding.UTF8, contentType)
                use request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                request.Content <- content

                headers
                |> Map.iter (fun k v ->
                    if not (String.Equals(k, "content-type", StringComparison.OrdinalIgnoreCase)) then
                        request.Headers.TryAddWithoutValidation(k, v) |> ignore)

                let! response = client.SendAsync(request) |> Async.AwaitTask
                let! text = response.Content.ReadAsStringAsync() |> Async.AwaitTask

                return
                    { Status = int response.StatusCode
                      Body = text }
            }

/// Construction-time configuration. The secrets are optional: in the
/// server-proxied pattern the client targets your own proxy path and the proxy
/// injects the access token + BYOK key, so neither is set client-side.
type FuaranClientConfig =
    {
        /// The Fuaran generation endpoint URL, or a same-origin proxy path
        /// (e.g. `/api/fuaran`) in the server-proxied pattern.
        Endpoint: string
        /// Paid access token. Omit in the server-proxied pattern (the proxy adds
        /// it). A per-call `GenerateArgs.AccessToken` overrides this.
        AccessToken: string option
        /// BYOK provider key. Memory-only — never bundle a key into shipped code
        /// (see the README). Omit in the server-proxied pattern. A per-call
        /// `GenerateArgs.ProviderKey` overrides this.
        ProviderKey: string option
        /// Injectable transport (tests / custom runtimes). `None` uses the shared
        /// `HttpClientTransport`.
        Transport: IFuaranTransport option
        /// Extra headers merged into every request (e.g. a proxy auth header).
        Headers: Map<string, string>
        /// Also send the access token as `Authorization: Bearer <token>` (in
        /// addition to the request body) — many edge gateways gate on it.
        /// Default `true`; set `false` for a deployment that reads the token from
        /// the body only.
        SendBearerHeader: bool
    }

[<RequireQualifiedAccess>]
module FuaranClientConfig =

    /// A config from just an endpoint — no client-side secrets (the
    /// server-proxied pattern) and every default in place.
    let create (endpoint: string) : FuaranClientConfig =
        { Endpoint = endpoint
          AccessToken = None
          ProviderKey = None
          Transport = None
          Headers = Map.empty
          SendBearerHeader = true }

/// A thin, typed client over the Fuaran generation endpoint. Construct once with
/// the endpoint (+ credentials, in the browser-BYOK pattern) and reuse it across
/// turns; `FuaranSession` wraps it with the tree-carrying loop.
type FuaranClient(config: FuaranClientConfig) =

    do
        if String.IsNullOrWhiteSpace config.Endpoint then
            invalidArg "config" "Fuaran.UI.Client: `Endpoint` is required."

    let transport =
        config.Transport
        |> Option.defaultWith (fun () -> HttpClientTransport() :> IFuaranTransport)

    /// The configuration this client was constructed with.
    member _.Config = config

    /// Run one generation turn. Resolves to a typed `TurnResult`
    /// (`Produced` / `AccessDenied` / `TurnFailed`); it never raises for an
    /// endpoint-level outcome — a transport error surfaces as a `TurnFailed`
    /// with a `Provider`-stage `NETWORK` envelope.
    member _.Generate(args: GenerateArgs) : Async<TurnResult> =
        async {
            let accessToken = args.AccessToken |> Option.orElse config.AccessToken
            let providerKey = args.ProviderKey |> Option.orElse config.ProviderKey

            let secrets: ResolvedSecrets =
                { ProviderKey = providerKey
                  AccessToken = accessToken }

            let headers =
                let baseHeaders = config.Headers |> Map.add "content-type" "application/json"

                match accessToken with
                | Some token when config.SendBearerHeader -> baseHeaders |> Map.add "authorization" $"Bearer {token}"
                | _ -> baseHeaders

            let body = Wire.toWireBody args secrets

            try
                let! response = transport.Send(config.Endpoint, headers, body)
                return Wire.parseTurnResponse response.Status response.Body
            with ex ->
                return
                    TurnResult.TurnFailed
                        { Stage = TurnStage.Provider
                          Code = "NETWORK"
                          Message = ex.Message }
        }

/// Renderer glue — decode a produced tree's canonical wire JSON into a typed
/// `Node<obj>` ready to hand to `Fuaran.UI.Renderer` in a browser/Fable host.
/// Decode lives in this .NET package; the render call itself is one line in the
/// caller's Fable host (see the README), mirroring how the TypeScript client
/// splits its Node-safe core from its `./render` subpath.
[<RequireQualifiedAccess>]
module Render =

    /// Decode the canonical wire JSON of a produced tree into a `Node<obj>`.
    let decodeTreeJson (treeJson: string) : Result<Node<obj>, JsonDecode.DecodeError> =
        JsonDecode.decodeNodeObj treeJson

    /// Decode a `TurnResult` when it is `Produced`; `None` for the non-produced
    /// cases (the caller has already branched on those). A one-liner from a turn
    /// to a renderable tree.
    let decodeProduced (result: TurnResult) : Result<Node<obj>, JsonDecode.DecodeError> option =
        match result with
        | TurnResult.Produced(treeJson, _, _) -> Some(decodeTreeJson treeJson)
        | _ -> None
