module Fuaran.UI.ServerDriven.AspNetCore.Tests.AsyncFactoryTests

// ─── Phase 2170: the SSE+POST transport awaits its session factory ─────────
//
// `mapFuaranLiveAsync` beside `mapFuaranLive`: the stream handler awaits an
// asynchronous factory before the connection exists, and runs the effects an
// effect-returning update hands back. Driven through the endpoint's own
// `RequestDelegate` against a `DefaultHttpContext`, so the glue under test is the
// shipped handler and not a re-statement of it. Every wait is a bounded,
// awaited signal that holds no thread (`TestSupport`, Phase 1928 carrying the
// Phase 2497 rule here): a synchronous wait on a pool thread goes red on a
// loaded machine by starving the very continuations it waits for.

#nowarn "44" // `Action.Dispatch` is in-process-only (Phase 1152); this suite is in-process.

open System
open System.Threading
open System.Threading.Tasks
open Expecto
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.ServerDriven.Driver
open Fuaran.UI.ServerDriven.AspNetCore
open Fuaran.UI.ServerDriven.AspNetCore.Endpoints
open Fuaran.UI.ServerDriven.AspNetCore.Tests.TestSupport

type Msg =
    | Click
    | Loaded of string

let private view (m: string) : Node<Msg> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.button
                      "go"
                      { Defaults.button<Msg> with
                          OnClick = Action.Dispatch Click }
                  Fuaran.markdown "status" m ] }

let private render (n: Node<Msg>) = $"<f id='{n.Id}'/>"

/// Map with `map`, and hand back the stream endpoint's own delegate.
let private streamDelegate (map: IEndpointRouteBuilder -> unit) (path: string) : WebApplication * RequestDelegate =
    let app = WebApplication.CreateBuilder().Build()
    map app

    let endpoint =
        (app :> IEndpointRouteBuilder).DataSources
        |> Seq.collect (fun source -> source.Endpoints)
        |> Seq.pick (fun e ->
            match e with
            | :? RouteEndpoint as r when r.RoutePattern.RawText = path -> Option.ofObj r.RequestDelegate
            | _ -> None)

    app, endpoint

let private context (app: WebApplication) (aborted: CancellationToken) =
    let ctx = DefaultHttpContext()
    ctx.RequestServices <- app.Services
    ctx.RequestAborted <- aborted
    let body = new SharedBody()
    ctx.Response.Body <- body
    ctx, body

/// The connId the stream handler bound into its correlation cookie.
let private connIdOf (config: LiveAppConfig<_, _>) (ctx: HttpContext) =
    let cookie = string ctx.Response.Headers["Set-Cookie"]
    let prefix = config.CookieName + "="
    let token = cookie.Substring(prefix.Length).Split(';').[0]
    ConnToken.verify config.Secret "" (Uri.UnescapeDataString token)

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Phase 2170 — mapFuaranLiveAsync"
        [ testTask "a factory that never answers holds only its own request, and ends with it" {
              let config = defaultAsyncConfig<string, Msg> ()
              let factoryCalls = ref 0

              // The first call to ENTER the factory is the one that never answers, and
              // `firstEntered` is set from inside that call. The test starts the second
              // request only after this fires, so which request is stuck is fixed by
              // construction, not by the order two threads happened to reach the factory
              // (the race that let the free request take the blocking call under load).
              let firstEntered = signal ()
              // Built before the race: the bound times the seam, not the first
              // construction of a session in this process.
              let ready =
                  init (DriverServices.createPermissive render) (fun _ m -> m) view "ready"

              let factory: AsyncSessionFactory<string, Msg> =
                  fun () ->
                      async {
                          if Interlocked.Increment factoryCalls = 1 then
                              firstEntered.TrySetResult() |> ignore
                              let! _ = Async.AwaitTask(TaskCompletionSource<unit>().Task)
                              ()

                          return ready, None
                      }

              let mutable registry = Unchecked.defaultof<ConnectionRegistry>

              let app, stream =
                  streamDelegate (fun r -> registry <- Endpoints.mapFuaranLiveAsync r config factory) config.StreamPath

              // No reconnect grace (Phase 1928): a stream that ends evicts its
              // connection at once, so "deregisters" below needs no wait.
              registry.ReconnectGrace <- TimeSpan.Zero
              let stuckAbort = new CancellationTokenSource()
              let freeAbort = new CancellationTokenSource()
              let stuckCtx, _ = context app stuckAbort.Token
              let freeCtx, freeBody = context app freeAbort.Token

              // Invoked on its own thread and bounded: a handler that blocked on the
              // factory would not RETURN, and this fails at the bound instead of
              // hanging the suite.
              let invoked = offPool (fun () -> stream.Invoke stuckCtx)
              // Which request owns the blocking call is asserted before anything about
              // headers is, so a wrong assignment fails on its cause, not on a symptom.
              let! entered = within firstEntered.Task
              Expect.isTrue entered "the stuck request entered the factory"
              Expect.equal factoryCalls.Value 1 "and it was the first call: no other request had reached the factory"
              let! returned = within invoked
              Expect.isTrue returned "the handler returned without blocking on the factory"
              let stuck = invoked.Result
              let free = stream.Invoke freeCtx

              let! opened = within freeBody.Flushed
              Expect.isTrue opened "the other connection opened meanwhile"
              Expect.equal registry.Count 1 "and registered"
              Expect.equal factoryCalls.Value 2 "through the factory's second call, which answered"
              Expect.isFalse stuck.IsCompleted "the stuck request is still waiting for its session"
              Expect.isFalse (stuckCtx.Response.Headers.ContainsKey "Set-Cookie") "and has written nothing"

              stuckAbort.Cancel()
              let! stuckEnded = within stuck
              Expect.isTrue stuckEnded "the stuck request ends with its client"
              freeAbort.Cancel()
              let! freeEnded = within free
              Expect.isTrue freeEnded "the open stream ends with its client"
              Expect.equal registry.Count 0 "and deregisters"
          }

          testTask "an effect-returning update's effects are awaited and their frames streamed" {
              let config = defaultAsyncConfig<string, Msg> ()
              let release = TaskCompletionSource<unit>()

              let update msg (m: string) =
                  match msg with
                  | Click ->
                      "loading",
                      [ async {
                            do! Async.AwaitTask release.Task
                            return Some(Loaded "done")
                        } ]
                  | Loaded s -> s, []

              let factory: AsyncSessionFactory<string, Msg> =
                  fun () ->
                      async {
                          // The host's async state read, in the factory.
                          do! Async.Sleep 1

                          let session, outbox =
                              initWithEffects (DriverServices.createPermissive render) update view "idle"

                          return session, Some outbox
                      }

              let mutable registry = Unchecked.defaultof<ConnectionRegistry>

              let app, stream =
                  streamDelegate (fun r -> registry <- Endpoints.mapFuaranLiveAsync r config factory) config.StreamPath

              let abort = new CancellationTokenSource()
              let ctx, body = context app abort.Token
              let running = stream.Invoke ctx
              let! opened = within body.Flushed
              Expect.isTrue opened "the connection opened"

              let connId =
                  match connIdOf config ctx with
                  | Some id -> id
                  | None -> failtest "the stream bound no connId"

              let conn = (registry.TryGet connId).Value

              conn.Handle
                  { ConnId = connId
                    NodeId = "go"
                    Event = "click"
                    Payload = Map.empty
                    LastSeq = 0 }

              let! first = within (body.Contains "id: 1")
              Expect.isTrue first "the click's own frame streamed"
              Expect.isFalse (body.Text.Contains "id: 2") "the effect has not answered yet"
              release.SetResult()
              let! second = within (body.Contains "id: 2")
              Expect.isTrue second "the effect's message streamed a frame"

              abort.Cancel()
              let! ended = within running
              Expect.isTrue ended "the stream ends with its client"
          }

          test "a config built for the async map refuses the synchronous one loudly" {
              let config = defaultAsyncConfig<string, Msg> ()
              Expect.throwsT<InvalidOperationException> (fun () -> config.MakeSession() |> ignore) "names the async map"
          } ]
