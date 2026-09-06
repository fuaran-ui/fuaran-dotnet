// Fuaran.UI.Client — the client over the Fuaran generation endpoint.
//
// `client.Generate(GenerateArgs.prompt "…")` collapses the integration to one
// call: it builds the request, sends it over an injectable transport, and
// returns the typed three-way result. No hand-rolled HTTP, no JSON wrangling,
// no token plumbing. The transport is a seam (`IFuaranTransport`) so a test
// drives it against a scripted mock with no live endpoint — the F# analogue of
// the TypeScript client's injectable `fetch`.
//
// THE ENDPOINT MUST BE https, OR LOOPBACK, OR EXPLICITLY OPTED OUT OF. Both
// credentials ride headers on every call, so a plaintext hop hands them to
// anyone on the path. `http://127.0.0.1` and `http://localhost` are admitted
// because that is where the offline mock and a local proxy live and no packet
// leaves the machine; anything else plaintext is refused as
// `INSECURE_ENDPOINT` unless `AllowInsecureEndpoint` is set — an opt-in that
// has to be written down, not a default that has to be noticed. A relative
// path (`/api/fuaran`, the server-proxied pattern) carries no scheme and is
// admitted: its security is the page's own origin.
//
// NO UPSTREAM EXCEPTION TEXT REACHES THE CALLER. A transport failure and a
// timeout are `NETWORK` with a FIXED message. This result is routinely rendered
// straight into a browser, and an exception string can quote a URL, a header
// name, or a proxy's internal hostname. The detail belongs in the host's log,
// which is why the transport is a seam the host owns.

namespace Fuaran.UI.Client

open System
open System.Net.Http
open System.Text
open System.Threading

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
/// (including `Authorization` and the BYOK key) is added to the request message.
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
        /// Paid access token, sent as `Authorization: Bearer <token>`. Omit in
        /// the server-proxied pattern (the proxy adds it). A per-call
        /// `GenerateArgs.AccessToken` overrides this.
        AccessToken: string option
        /// BYOK provider key, sent as `X-Fuaran-Provider-Key`. Memory-only —
        /// never bundle a key into shipped code (see the README). Omit in the
        /// server-proxied pattern. A per-call `GenerateArgs.ProviderKey`
        /// overrides this.
        ProviderKey: string option
        /// Which allowlisted provider id to ask for (`X-Fuaran-Provider`).
        /// `None` lets the deployment choose its own default.
        Provider: string option
        /// Injectable transport (tests / custom runtimes). `None` uses the shared
        /// `HttpClientTransport`.
        Transport: IFuaranTransport option
        /// Extra headers merged into every request (e.g. a proxy auth header).
        Headers: Map<string, string>
        /// Send the access token as `Authorization: Bearer <token>` — the
        /// endpoint's ONLY auth channel, since a body-carried token is refused.
        /// Default `true`. Set `false` only when pointing at a proxy that
        /// authenticates some other way, and supply that header via `Headers`.
        SendBearerHeader: bool
        /// Wall-clock ceiling on one turn. `None` waits as long as the transport
        /// does. An elapsed timeout is `TurnFailed` / `NETWORK`, never an
        /// exception — the same shape as any other transport failure, because
        /// from the caller's side it is one.
        Timeout: TimeSpan option
        /// Permit a plaintext, non-loopback endpoint. Off by default; see the
        /// module header for why this is an opt-in rather than a warning.
        AllowInsecureEndpoint: bool
    }

[<RequireQualifiedAccess>]
module FuaranClientConfig =

    /// A config from just an endpoint — no client-side secrets (the
    /// server-proxied pattern) and every default in place.
    let create (endpoint: string) : FuaranClientConfig =
        { Endpoint = endpoint
          AccessToken = None
          ProviderKey = None
          Provider = None
          Transport = None
          Headers = Map.empty
          SendBearerHeader = true
          Timeout = None
          AllowInsecureEndpoint = false }

/// The endpoint-scheme policy, exposed so a host can apply the same rule to a
/// URL it is about to configure rather than discovering it on the first turn.
[<RequireQualifiedAccess>]
module EndpointPolicy =

    let private isLoopback (uri: Uri) =
        uri.IsLoopback
        || String.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)

    /// `true` when credentials may be sent to this endpoint. A relative path
    /// (no scheme) is same-origin by construction and admitted; an absolute
    /// `https` URL is admitted; plaintext is admitted only to loopback.
    let isSecure (endpoint: string) : bool =
        match Uri.TryCreate(endpoint, UriKind.Absolute) with
        | true, parsed ->
            match Option.ofObj parsed with
            | Some uri ->
                String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || isLoopback uri
            | None -> true
        | _ -> true

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

    let networkFailure (message: string) : TurnResult =
        TurnResult.TurnFailed
            { Stage = TurnStage.Provider
              Code = ClientCode.Network
              Message = message }

    let insecureEndpoint: TurnResult =
        TurnResult.TurnFailed
            { Stage = TurnStage.Provider
              Code = ClientCode.InsecureEndpoint
              Message =
                "the endpoint is plaintext http and is not loopback; the access token and BYOK key would travel in "
                + "the clear. Use https, or set AllowInsecureEndpoint if you genuinely mean it." }

    /// The configuration this client was constructed with.
    member _.Config = config

    /// Run one generation turn, returning the typed result AND the deployment
    /// facts the reply carried (`opsApplied`, `provider`, `servedModel`, the
    /// snapshot state). The detail is `None` for every non-produced outcome.
    member _.GenerateDetailed(args: GenerateArgs) : Async<TurnResult * ProducedDetail option> =
        async {
            if not (config.AllowInsecureEndpoint || EndpointPolicy.isSecure config.Endpoint) then
                // Refused BEFORE the request is built, so neither credential is
                // ever handed to the transport.
                return insecureEndpoint, None
            else
                let accessToken = args.AccessToken |> Option.orElse config.AccessToken
                let providerKey = args.ProviderKey |> Option.orElse config.ProviderKey

                let headers =
                    config.Headers
                    |> Map.add "content-type" "application/json"
                    |> fun h ->
                        match accessToken with
                        | Some token when config.SendBearerHeader -> h |> Map.add "authorization" $"Bearer {token}"
                        | _ -> h
                    |> fun h ->
                        match providerKey with
                        | Some key -> h |> Map.add "x-fuaran-provider-key" key
                        | None -> h
                    |> fun h ->
                        match config.Provider with
                        | Some provider -> h |> Map.add "x-fuaran-provider" provider
                        | None -> h

                let body = Wire.toWireBody args

                let send =
                    async {
                        let! response = transport.Send(config.Endpoint, headers, body)
                        return response
                    }

                try
                    let! response =
                        match config.Timeout with
                        | None -> send
                        | Some timeout ->
                            async {
                                // `StartChild` cancels the child when the window
                                // elapses, so a timed-out turn stops waiting AND
                                // stops the work, rather than merely stopping
                                // waiting.
                                let! child = Async.StartChild(send, int timeout.TotalMilliseconds)
                                return! child
                            }

                    return
                        Wire.parseTurnResponse response.Status response.Body,
                        Wire.parseProducedDetail response.Status response.Body
                with
                | :? TimeoutException -> return networkFailure "the request to the generation endpoint timed out", None
                | :? OperationCanceledException ->
                    return networkFailure "the request to the generation endpoint was cancelled", None
                | _ ->
                    // Deliberately no exception text — see the module header.
                    return networkFailure "the request to the generation endpoint did not complete", None
        }

    /// Run one generation turn. Resolves to a typed `TurnResult`
    /// (`Produced` / `AccessDenied` / `TurnFailed`); it never raises for an
    /// endpoint-level outcome — a transport error or an elapsed `Timeout`
    /// surfaces as a `TurnFailed` with a `Provider`-stage `NETWORK` envelope.
    member this.Generate(args: GenerateArgs) : Async<TurnResult> =
        async {
            let! result, _ = this.GenerateDetailed args
            return result
        }

    /// Run one generation turn under a caller's cancellation token. Cancellation
    /// is reported, not raised: the client's contract is that an endpoint-level
    /// outcome never throws, and from the caller's side a cancelled call is a
    /// call that did not complete.
    member this.Generate(args: GenerateArgs, cancellationToken: CancellationToken) : Async<TurnResult> =
        async {
            try
                return! Async.AwaitTask(Async.StartAsTask(this.Generate args, cancellationToken = cancellationToken))
            with
            | :? OperationCanceledException
            | :? AggregateException -> return networkFailure "the request to the generation endpoint was cancelled"
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
