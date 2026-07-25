// A scripted mock transport for driving FuaranClient without a live endpoint —
// the F# analogue of the TypeScript client's injectable `fetch` mock. Records
// every request it sees and replies from a caller-supplied script.

namespace Fuaran.UI.Client.Tests

open Fuaran.UI.Client

/// One captured request — what the client actually put on the wire.
type CapturedRequest =
    { Endpoint: string
      Headers: Map<string, string>
      Body: string }

/// A transport that returns a fixed `HttpResult` and records the request. Pass
/// `throwWith` to exercise the transport-failure (NETWORK) path instead.
type MockTransport(reply: HttpResult, ?throwWith: exn) =
    let mutable captured: CapturedRequest option = None

    /// The most recent request the client sent through this transport.
    member _.Captured = captured

    interface IFuaranTransport with
        member _.Send(endpoint, headers, body) =
            async {
                captured <-
                    Some
                        { Endpoint = endpoint
                          Headers = headers
                          Body = body }

                match throwWith with
                | Some ex -> return raise ex
                | None -> return reply
            }

/// A transport that replays a scripted sequence of replies (one per call), so a
/// repair loop can be driven fail → fail → produced. Records every request.
type ScriptedTransport(replies: HttpResult list) =
    let mutable remaining = replies
    let mutable requests: CapturedRequest list = []

    /// Every request seen, in call order.
    member _.Requests = List.rev requests

    /// How many turns the transport served.
    member _.CallCount = List.length requests

    interface IFuaranTransport with
        member _.Send(endpoint, headers, body) =
            async {
                requests <-
                    { Endpoint = endpoint
                      Headers = headers
                      Body = body }
                    :: requests

                match remaining with
                | reply :: rest ->
                    remaining <- rest
                    return reply
                | [] ->
                    return
                        { Status = 200
                          Body = """{"TreeJson":"t","Ops":[],"Version":"1.2.0"}""" }
            }

[<RequireQualifiedAccess>]
module MockTransport =

    /// Build a client whose transport replies with (status, body) and records
    /// the request. Returns the client + the transport (for request assertions).
    let client (config: FuaranClientConfig) (status: int) (body: string) : FuaranClient * MockTransport =
        let transport = MockTransport({ Status = status; Body = body })

        FuaranClient(
            { config with
                Transport = Some(transport :> IFuaranTransport) }
        ),
        transport
