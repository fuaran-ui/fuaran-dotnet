module Fuaran.UI.ServerDriven.AspNetCore.Tests.ReconnectTests

// ─── Phase 1928: sessions survive reconnect and concurrency ────────────────
//
// Driven through the SSE backend's own GET and POST `RequestDelegate`s against
// `DefaultHttpContext`s, so what is under test is the shipped endpoint glue, not
// a re-statement of it. Every wait is a bounded, awaited signal that holds no
// thread (`TestSupport`).

#nowarn "44" // `Action.Dispatch` is in-process-only (Phase 1152); this suite is in-process.

open System
open System.IO
open System.Text
open System.Threading
open System.Threading.Tasks
open Expecto
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Driver
open Fuaran.UI.ServerDriven.AspNetCore
open Fuaran.UI.ServerDriven.AspNetCore.Endpoints
open Fuaran.UI.ServerDriven.AspNetCore.Tests.TestSupport

type Msg = | Click

let private view (count: int) : Node<Msg> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.button
                      "go"
                      { Defaults.button<Msg> with
                          OnClick = Action.Dispatch Click }
                  Fuaran.markdown "status" (string count) ] }

let private render (n: Node<Msg>) = $"<f id='{n.Id}'/>"

/// The mapped live endpoints, the registry, every connection the endpoints built
/// (through `ConfigureConnection`) and how many sessions the factory minted.
type private Live =
    { App: WebApplication
      Stream: RequestDelegate
      Event: RequestDelegate
      Registry: ConnectionRegistry
      Config: LiveAppConfig<int, Msg>
      Connections: ResizeArray<LiveConnection<int, Msg>>
      SessionsMade: int ref }

let private live (update: Msg -> int -> int) : Live =
    let made = ref 0
    let connections = ResizeArray<LiveConnection<int, Msg>>()

    let config =
        { defaultConfig (fun () ->
              Interlocked.Increment made |> ignore
              init (DriverServices.createPermissive render) update view 0) with
            ConfigureConnection = fun _ conn -> lock connections (fun () -> connections.Add conn) }

    let app = WebApplication.CreateBuilder().Build()
    let registry = mapFuaranLive app config

    let delegateAt (path: string) =
        (app :> IEndpointRouteBuilder).DataSources
        |> Seq.collect (fun source -> source.Endpoints)
        |> Seq.pick (fun e ->
            match e with
            | :? RouteEndpoint as r when r.RoutePattern.RawText = path -> Option.ofObj r.RequestDelegate
            | _ -> None)

    { App = app
      Stream = delegateAt config.StreamPath
      Event = delegateAt config.EventPath
      Registry = registry
      Config = config
      Connections = connections
      SessionsMade = made }

let private increment (_: Msg) (count: int) = count + 1

/// A GET on the stream path: `cookie` is the correlation token a browser would
/// send back, `tab` the tab id, `lastEventId` what `EventSource` resends. The
/// handler returns at its first real await, so the request runs on as a task.
let private openStream
    (l: Live)
    (aborted: CancellationToken)
    (cookie: string option)
    (tab: string option)
    (lastEventId: int option)
    (failWrites: bool)
    =
    let ctx = DefaultHttpContext()
    ctx.RequestServices <- l.App.Services
    ctx.RequestAborted <- aborted
    let body = new SharedBody(failWrites)
    ctx.Response.Body <- body

    cookie
    |> Option.iter (fun c -> ctx.Request.Headers["Cookie"] <- $"{l.Config.CookieName}={c}")

    tab
    |> Option.iter (fun t -> ctx.Request.QueryString <- QueryString($"?tab={t}"))

    lastEventId
    |> Option.iter (fun id -> ctx.Request.Headers["Last-Event-ID"] <- string id)

    ctx, body, l.Stream.Invoke ctx

/// The correlation token the stream handler set.
let private tokenOf (l: Live) (ctx: HttpContext) =
    let cookie = string ctx.Response.Headers["Set-Cookie"]
    let prefix = l.Config.CookieName + "="
    Uri.UnescapeDataString(cookie.Substring(prefix.Length).Split(';').[0])

/// A POST of one click on the event path, as the shim sends it; its status.
let private click (l: Live) (token: string) (tab: string option) : Task<int> =
    task {
        let ctx = DefaultHttpContext()
        ctx.RequestServices <- l.App.Services
        ctx.Request.Method <- "POST"
        ctx.Request.Headers["Cookie"] <- $"{l.Config.CookieName}={token}"
        tab |> Option.iter (fun t -> ctx.Request.Headers[TabHeader] <- t)
        ctx.Request.Body <- new MemoryStream(Encoding.UTF8.GetBytes """{"nodeId":"go","event":"click"}""")
        let handled = l.Event.Invoke ctx
        let! answered = within handled
        Expect.isTrue answered "the POST answers"
        return ctx.Response.StatusCode
    }

let private occurrences (needle: string) (hay: string) =
    (hay.Length - hay.Replace(needle, "").Length) / needle.Length

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Phase 1928 — server-driven sessions survive reconnect and concurrency"
        [ testTask "a reconnect with Last-Event-ID replays the missed frames into the session the client had" {
              let l = live increment
              let firstAbort = new CancellationTokenSource()
              let first, firstBody, firstRun = openStream l firstAbort.Token None None None false
              let! opened = within firstBody.Flushed
              Expect.isTrue opened "the stream opened its connection"
              let token = tokenOf l first

              let! c1 = click l token None
              Expect.equal c1 204 "the first click is accepted"
              let! c2 = click l token None
              Expect.equal c2 204 "the second click is accepted"
              let! streamed = within (firstBody.Contains "id: 2")
              Expect.isTrue streamed "both frames streamed"

              // The client applied frame 1 and then lost the stream.
              firstAbort.Cancel()
              let! dropped = within firstRun
              Expect.isTrue dropped "the dropped stream ends"
              Expect.equal l.Registry.Count 1 "its connection is held for the reconnect, not removed"
              let! c3 = click l token None
              Expect.equal c3 204 "a click while detached still reaches the session"

              let secondAbort = new CancellationTokenSource()

              let _, secondBody, secondRun =
                  openStream l secondAbort.Token (Some token) None (Some 1) false

              let! replayed = within (secondBody.Contains "id: 3")
              Expect.isTrue replayed "the missed frames arrive"
              Expect.isFalse (secondBody.Text.Contains "id: 1") "and only the missed ones"
              Expect.stringContains secondBody.Text "id: 2" "frame 2, which the client never applied, is replayed"
              Expect.equal l.SessionsMade.Value 1 "no session was minted for the reconnect"
              Expect.equal l.Connections.Count 1 "nor a connection"
              Expect.equal l.Connections[0].Session.Model 3 "the model is the one the client had, every click in it"

              let! c4 = click l token None
              Expect.equal c4 204 "a click after the reconnect"
              let! next = within (secondBody.Contains "id: 4")
              Expect.isTrue next "streams on the new stream"
              secondAbort.Cancel()
              let! ended = within secondRun
              Expect.isTrue ended "the reconnected stream ends with its client"
          }

          testTask "a reconnect from behind the evicted buffer is re-baselined on its session, not silently reset" {
              let l = live increment
              let firstAbort = new CancellationTokenSource()
              let first, firstBody, firstRun = openStream l firstAbort.Token None None None false
              let! opened = within firstBody.Flushed
              Expect.isTrue opened "the stream opened its connection"
              let token = tokenOf l first
              firstAbort.Cancel()
              let! dropped = within firstRun
              Expect.isTrue dropped "the stream drops"

              // More frames than the replay buffer keeps arrive while detached.
              let steps = LiveConnectionDefaults.ReplayBufferCapacity + 8

              for _ in 1..steps do
                  let! _ = click l token None
                  ()

              let secondAbort = new CancellationTokenSource()

              let _, body, secondRun =
                  openStream l secondAbort.Token (Some token) None (Some 1) false

              let! rebaselined = within (body.Contains $"id: {steps + 1}")
              Expect.isTrue rebaselined "one re-baseline frame arrives"
              Expect.equal (occurrences "id: " body.Text) 1 "and nothing else: no partial replay"
              Expect.stringContains body.Text "ReplaceFragment" "it replaces the whole document"
              Expect.stringContains body.Text "\"nodeId\":\"root\"" "from the root"
              Expect.equal l.SessionsMade.Value 1 "the session was kept, not reset"
              Expect.equal l.Connections[0].Session.Model steps "and the document is its CURRENT model"
              secondAbort.Cancel()
              let! ended = within secondRun
              Expect.isTrue ended "the stream ends with its client"
          }

          testTask "two concurrent POSTs on one connection get two Seq values and one consistent session" {
              // A slow update widens the window two unserialised steps would race in.
              let slow (msg: Msg) (count: int) =
                  Thread.Sleep 50
                  increment msg count

              let l = live slow
              let abort = new CancellationTokenSource()
              let ctx, body, running = openStream l abort.Token None None None false
              let! opened = within body.Flushed
              Expect.isTrue opened "the stream opened its connection"
              let token = tokenOf l ctx

              // Each POST on its own thread, so they genuinely overlap.
              let posts = [| for _ in 1..2 -> offPool (fun () -> click l token None) |]
              let! statuses = Task.WhenAll(posts |> Array.map (fun t -> t.Unwrap()))
              Expect.equal statuses [| 204; 204 |] "both are accepted"

              let! streamed = within (body.Contains "id: 2")
              Expect.isTrue streamed "two frames streamed"
              Expect.equal (occurrences "id: 1\n" body.Text) 1 "Seq 1 once"
              Expect.equal (occurrences "id: 2\n" body.Text) 1 "Seq 2 once"
              Expect.equal l.Connections[0].Session.Model 2 "neither step's transition was lost"
              Expect.equal l.Connections[0].Sequence 2 "and the sequence counts both"
              abort.Cancel()
              let! ended = within running
              Expect.isTrue ended "the stream ends with its client"
          }

          testTask "a second tab does not steal the first tab's stream" {
              let l = live increment
              let abortA = new CancellationTokenSource()
              let abortB = new CancellationTokenSource()
              let ctxA, bodyA, runA = openStream l abortA.Token None (Some "tab-a") None false
              let! openedA = within bodyA.Flushed
              Expect.isTrue openedA "tab A opened its connection"
              let token = tokenOf l ctxA

              // Tab B shares the browser's cookie, as every tab of one origin does.
              let ctxB, bodyB, runB =
                  openStream l abortB.Token (Some token) (Some "tab-b") None false

              let! openedB = within bodyB.Flushed
              Expect.isTrue openedB "tab B opened a connection of its own"
              Expect.equal l.Registry.Count 2 "two connections"
              Expect.equal (tokenOf l ctxB) token "the cookie keeps binding the same principal-bound id"

              let! a = click l token (Some "tab-a")
              Expect.equal a 204 "tab A clicks"
              let! reachedA = within (bodyA.Contains "id: 1")
              Expect.isTrue reachedA "tab A's frame reaches tab A"
              Expect.isFalse (bodyB.Text.Contains "id: ") "and not tab B"
              Expect.equal l.Connections[0].Session.Model 1 "tab A's session moved"
              Expect.equal l.Connections[1].Session.Model 0 "tab B's did not"

              let! b = click l token (Some "tab-b")
              Expect.equal b 204 "tab B clicks"
              let! reachedB = within (bodyB.Contains "id: 1")
              Expect.isTrue reachedB "tab B's frame reaches tab B"
              Expect.equal (occurrences "id: " bodyA.Text) 1 "tab A's stream carries only its own frame"
              Expect.isFalse runA.IsCompleted "tab A's stream is still open"

              let! malformed = click l token (Some "not a tab!")
              Expect.equal malformed 400 "a malformed tab id is refused"
              abortA.Cancel()
              abortB.Cancel()
              let! endedA = within runA
              let! endedB = within runB
              Expect.isTrue (endedA && endedB) "both streams end with their clients"
          }

          testTask "a write failure leaves the registry empty" {
              let l = live increment
              l.Registry.ReconnectGrace <- TimeSpan.FromSeconds 2.0
              let abort = new CancellationTokenSource()
              let ctx, body, running = openStream l abort.Token None None None true
              let! opened = within body.Flushed
              Expect.isTrue opened "the stream opened its connection"

              // The first frame's write throws, as a write to a dropped client does.
              let! _ = click l (tokenOf l ctx) None
              let! ended = within running
              Expect.isTrue ended "the stream ends on the failed write"
              Expect.isTrue running.IsFaulted "with the write's own failure, not swallowed"
              Expect.equal l.Registry.Count 1 "the connection is held for the reconnect grace"
              // The eviction runs on the registry's own timer: no signal to await.
              let! evicted = eventuallyAsync (fun () -> l.Registry.Count = 0)
              Expect.isTrue evicted "and then evicted: nothing leaks"

              // With no grace the stream's `finally` evicts at once.
              l.Registry.ReconnectGrace <- TimeSpan.Zero
              let ctx2, body2, running2 = openStream l abort.Token None None None true
              let! opened2 = within body2.Flushed
              Expect.isTrue opened2 "a second stream opened"
              let! _ = click l (tokenOf l ctx2) None
              let! ended2 = within running2
              Expect.isTrue ended2 "it ends on the failed write"
              Expect.equal l.Registry.Count 0 "and the registry is empty when it has"
          } ]
