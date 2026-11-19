module Fuaran.UI.ServerDriven.WebSocket.Tests.AsyncFactoryTests

// ─── Phase 2170: the WebSocket transport awaits its session factory ────────
//
// `mapFuaranLiveWebSocketAsync` beside `mapFuaranLiveWebSocket`: the upgrade
// handler awaits an asynchronous factory BEFORE it accepts the socket. Driven
// through the endpoint's own `RequestDelegate` against a `DefaultHttpContext`
// carrying a WebSocket feature that records whether the upgrade was accepted.
//
// Phase 2497 — every wait is SIGNAL-driven and holds no thread. The bound is
// only how long a missing signal is waited for before the test fails; nothing
// polls a wall clock, and nothing blocks a pool thread while it waits, so a
// pool pinned small or a contended CPU delays the signals without the test
// itself standing between them and the threads they need.

open System
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

/// Await `signal` without holding a thread: true when it completes (in any
/// state) before the bound, false when the bound expires first.
let private within (signal: Task) : Task<bool> =
    task {
        use expiry = new CancellationTokenSource()
        let! first = Task.WhenAny(signal, Task.Delay(bound, expiry.Token))
        expiry.Cancel()
        return obj.ReferenceEquals(first, signal)
    }

let private signal () =
    TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)

/// Run `invoke` on its own thread, so a bounded check that it RETURNS takes no
/// pool thread: a handler that blocked on its factory would not return, and the
/// check fails at the bound instead of hanging the suite.
let private offPool (invoke: unit -> 'T) : Task<'T> =
    Task.Factory.StartNew(
        Func<'T> invoke,
        CancellationToken.None,
        TaskCreationOptions.LongRunning,
        TaskScheduler.Default
    )

let private session () : LiveSession<int, int> =
    Driver.init
        (DriverServices.create (fun _ -> ""))
        (fun (_: int) (m: int) -> m)
        (fun (_: int) -> Fuaran.UI.Fuaran.markdown "n" "")
        0

/// A WebSocket request whose upgrade is recorded and then refused (the test
/// asserts WHETHER the handler reached the accept, not what a socket does).
/// `Reached` completes at the accept, so a test awaits the upgrade rather than
/// polling for it.
type private RecordingUpgrade() =
    let reached = signal ()

    member _.Accepted = reached.Task.IsCompleted
    member _.Reached: Task = reached.Task

    interface IHttpWebSocketFeature with
        member _.IsWebSocketRequest = true

        member _.AcceptAsync(_: WebSocketAcceptContext) : Task<WebSocket> =
            reached.TrySetResult() |> ignore
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

/// The Phase 2170 property, as one scenario: a factory that never answers
/// holds only its own upgrade, and that upgrade ends with its client.
let private holdsOnlyItsOwnUpgrade () : Task =
    task {
        let config = defaultWsAsyncConfig<int, int> ()
        let calls = ref 0
        let otherAnswered = ref false
        // The first call to ENTER the factory is the one that never answers,
        // and `stuckEntered` fires from inside that call. The other request
        // is started only after it fires, so which request is stuck is fixed
        // by construction (Phase 2497: the handler QUEUES its factory, so
        // starting the second request as soon as the first handler returned
        // let the second reach the factory first under load, and the
        // assertions then read the right behaviour as the wrong one).
        let stuckEntered = signal ()
        // Built before the race: the bound times the seam, not the first
        // construction of a session in this process.
        let ready = session ()

        let factory: AsyncSessionFactory<int, int> =
            fun () ->
                async {
                    if Interlocked.Increment calls = 1 then
                        stuckEntered.TrySetResult() |> ignore
                        let! _ = Async.AwaitTask(TaskCompletionSource<unit>().Task)
                        ()
                    else
                        otherAnswered.Value <- true

                    return ready, None
                }

        let handler = socketDelegate config factory
        use stuckAbort = new CancellationTokenSource()
        let stuckCtx, stuckUpgrade = request config stuckAbort.Token
        let freeCtx, freeUpgrade = request config CancellationToken.None

        let invoked = offPool (fun () -> handler.Invoke stuckCtx)

        let! returned = within invoked
        Expect.isTrue returned "the handler returned without blocking on the factory"
        let stuck = invoked.Result
        let! entered = within stuckEntered.Task
        Expect.isTrue entered "the stuck request entered the factory"
        Expect.equal calls.Value 1 "and it was the first call: no other request had reached the factory"

        let free = handler.Invoke freeCtx

        let! reached = within freeUpgrade.Reached
        Expect.isTrue reached "the other request reached its upgrade"
        Expect.isTrue otherAnswered.Value "after its own factory answered"
        let! settled = within free
        Expect.isTrue settled "the refused upgrade settles the other request"
        Expect.isFalse stuckUpgrade.Accepted "the stuck request accepted NO socket while it waits"
        Expect.isFalse stuck.IsCompleted "and is still waiting for its session"

        stuckAbort.Cancel()
        let! ended = within stuck
        Expect.isTrue ended "the stuck request ends with its client"
        Expect.isFalse stuckUpgrade.Accepted "having never upgraded"
    }
    :> Task

/// Phase 2497 — the same property at a scale that a seam holding a pool thread
/// per waiting request could not meet: four stuck factories per core, then one
/// free request. If each stuck wait held a thread, the stuck requests alone
/// would exhaust the pool's eager threads and the free factory would wait on
/// thread injection, which is slower than the bound.
let private manyStuckHoldNoThread () : Task =
    task {
        let config = defaultWsAsyncConfig<int, int> ()
        let stuckCount = 4 * Environment.ProcessorCount
        let calls = ref 0
        let entered = ref 0
        let allEntered = signal ()
        let ready = session ()

        let factory: AsyncSessionFactory<int, int> =
            fun () ->
                async {
                    if Interlocked.Increment calls <= stuckCount then
                        if Interlocked.Increment entered = stuckCount then
                            allEntered.TrySetResult() |> ignore

                        let! _ = Async.AwaitTask(TaskCompletionSource<unit>().Task)
                        ()

                    return ready, None
                }

        let handler = socketDelegate config factory
        use stuckAbort = new CancellationTokenSource()

        let stuckUpgrades, stuckContexts =
            List.init stuckCount (fun _ ->
                let ctx, upgrade = request config stuckAbort.Token
                upgrade, ctx)
            |> List.unzip

        let freeCtx, freeUpgrade = request config CancellationToken.None

        let invoked = offPool (fun () -> stuckContexts |> List.map handler.Invoke)

        let! returned = within invoked
        Expect.isTrue returned "every stuck handler returned without blocking on its factory"
        let stuck = invoked.Result
        let! everyEntered = within allEntered.Task
        Expect.isTrue everyEntered $"all {stuckCount} stuck requests entered the factory"

        let free = handler.Invoke freeCtx
        let! reached = within freeUpgrade.Reached
        Expect.isTrue reached "the free request reached its upgrade past every stuck one"
        let! settled = within free
        Expect.isTrue settled "and settled"
        Expect.isFalse (stuckUpgrades |> List.exists _.Accepted) "no stuck request accepted a socket"

        stuckAbort.Cancel()
        let! ended = within (Task.WhenAll stuck)
        Expect.isTrue ended "every stuck request ends with its client"
    }
    :> Task

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Phase 2170 — mapFuaranLiveWebSocketAsync"
        [ testTask "a factory that never answers holds only its own upgrade, and ends with it" {
              do! holdsOnlyItsOwnUpgrade ()
          }

          testTask "stuck factories hold no thread between them: a free upgrade passes four per core" {
              do! manyStuckHoldNoThread ()
          }

          test "a config built for the async map refuses the synchronous one loudly" {
              let config = defaultWsAsyncConfig<int, int> ()
              Expect.throwsT<InvalidOperationException> (fun () -> config.MakeSession() |> ignore) "names the async map"
          } ]
