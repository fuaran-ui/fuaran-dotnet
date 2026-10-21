module Fuaran.UI.ServerDriven.WebSocket.Tests.AsyncFactoryTests

// ─── Phase 2170: the WebSocket transport awaits its session factory ────────
//
// `mapFuaranLiveWebSocketAsync` beside `mapFuaranLiveWebSocket`: the upgrade
// handler awaits an asynchronous factory BEFORE it accepts the socket. Driven
// through the endpoint's own `RequestDelegate` against a `DefaultHttpContext`
// carrying a WebSocket feature that records whether the upgrade was accepted.
// Every wait is bounded.

open System
open System.Diagnostics
open System.Net.WebSockets
open System.Threading
open System.Threading.Tasks
open Expecto
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Features
open Microsoft.AspNetCore.Routing
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Driver
open Fuaran.UI.ServerDriven.WebSocket
open Fuaran.UI.ServerDriven.WebSocket.Endpoints

let private bound = TimeSpan.FromSeconds 10.0

let private eventually (cond: unit -> bool) : bool =
    let sw = Stopwatch.StartNew()
    let mutable ok = cond ()

    while not ok && sw.Elapsed < bound do
        Thread.Sleep 10
        ok <- cond ()

    ok

let private session () : LiveSession<int, int> =
    Driver.init
        (DriverServices.create (fun _ -> ""))
        (fun (_: int) (m: int) -> m)
        (fun (_: int) -> Fuaran.UI.Fuaran.markdown "n" "")
        0

/// A WebSocket request whose upgrade is recorded and then refused (the test
/// asserts WHETHER the handler reached the accept, not what a socket does).
type private RecordingUpgrade() =
    member val Accepted = false with get, set

    interface IHttpWebSocketFeature with
        member _.IsWebSocketRequest = true

        member this.AcceptAsync(_: WebSocketAcceptContext) : Task<WebSocket> =
            this.Accepted <- true
            Task.FromException<WebSocket>(OperationCanceledException "recorded, not upgraded")

let private socketDelegate (config: LiveWsConfig<int, int>) (factory: AsyncSessionFactory<int, int>) =
    let app = WebApplication.CreateBuilder().Build()
    Endpoints.mapFuaranLiveWebSocketAsync app config factory

    (app :> IEndpointRouteBuilder).DataSources
    |> Seq.collect (fun source -> source.Endpoints)
    |> Seq.pick (fun e ->
        match e with
        | :? RouteEndpoint as r when r.RoutePattern.RawText = config.Path -> Option.ofObj r.RequestDelegate
        | _ -> None)

let private request (config: LiveWsConfig<int, int>) (aborted: CancellationToken) =
    let ctx = DefaultHttpContext()
    let upgrade = RecordingUpgrade()
    ctx.Features.Set<IHttpWebSocketFeature> upgrade
    ctx.RequestAborted <- aborted
    let token = ConnToken.sign config.Secret "" (Guid.NewGuid().ToString "N")
    ctx.Request.Headers["Cookie"] <- $"{config.CookieName}={Uri.EscapeDataString token}"
    ctx, upgrade

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Phase 2170 — mapFuaranLiveWebSocketAsync"
        [ test "a factory that never answers holds only its own upgrade, and ends with it" {
              let config = defaultWsAsyncConfig<int, int> ()
              let calls = ref 0
              let otherAnswered = ref false

              let factory: AsyncSessionFactory<int, int> =
                  fun () ->
                      async {
                          if Interlocked.Increment calls = 1 then
                              let! _ = Async.AwaitTask(TaskCompletionSource<unit>().Task)
                              ()
                          else
                              otherAnswered.Value <- true

                          return session (), None
                      }

              let handler = socketDelegate config factory
              use stuckAbort = new CancellationTokenSource()
              let stuckCtx, stuckUpgrade = request config stuckAbort.Token
              let freeCtx, freeUpgrade = request config CancellationToken.None

              // Invoked from a worker and bounded: a handler that blocked on the
              // factory would not RETURN, and this fails at the bound instead of
              // hanging the suite.
              let invoked = Task.Factory.StartNew(fun () -> handler.Invoke stuckCtx)
              Expect.isTrue (invoked.Wait bound) "the handler returned without blocking on the factory"
              let stuck = invoked.Result
              let free = handler.Invoke freeCtx

              Expect.isTrue (eventually (fun () -> freeUpgrade.Accepted)) "the other request reached its upgrade"
              Expect.isTrue otherAnswered.Value "after its own factory answered"
              Expect.isTrue (eventually (fun () -> free.IsCompleted)) "the refused upgrade settles the other request"
              Expect.isFalse stuckUpgrade.Accepted "the stuck request accepted NO socket while it waits"
              Expect.isFalse stuck.IsCompleted "and is still waiting for its session"

              stuckAbort.Cancel()
              Expect.isTrue (stuck.Wait bound) "the stuck request ends with its client"
              Expect.isFalse stuckUpgrade.Accepted "having never upgraded"
          }

          test "a config built for the async map refuses the synchronous one loudly" {
              let config = defaultWsAsyncConfig<int, int> ()
              Expect.throwsT<InvalidOperationException> (fun () -> config.MakeSession() |> ignore) "names the async map"
          } ]
