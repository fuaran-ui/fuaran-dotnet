module Fuaran.UI.ServerDriven.AspNetCore.Tests.ReconnectTests

// ─── Phase 1928: sessions survive reconnect and concurrency ────────────────
//
// Driven through the SSE backend's own GET and POST `RequestDelegate`s against
// `DefaultHttpContext`s, so what is under test is the shipped endpoint glue, not
// a re-statement of it. Every wait is bounded.

#nowarn "44" // `Action.Dispatch` is in-process-only (Phase 1152); this suite is in-process.

open System
open System.IO
open System.Diagnostics
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

type Msg = | Click

let private bound = TimeSpan.FromSeconds 10.0

let private eventually (cond: unit -> bool) : bool =
    let sw = Stopwatch.StartNew()
    let mutable ok = cond ()

    while not ok && sw.Elapsed < bound do
        Thread.Sleep 10
        ok <- cond ()

    ok

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

/// A write-only response body the handler writes on its thread and the test
/// reads on its own. `failWrites` makes every write throw, as a socket to a
/// dropped client does.
type private SharedBody(failWrites: bool) =
    inherit Stream()
    let buffer = new MemoryStream()
    let gate = obj ()

    let append (bytes: byte[]) offset count =
        if failWrites then
            raise (IOException "the client is gone")

        lock gate (fun () -> buffer.Write(bytes, offset, count))

    member _.Text = lock gate (fun () -> Encoding.UTF8.GetString(buffer.ToArray()))

    override _.CanRead = false
    override _.CanSeek = false
    override _.CanWrite = true
    override _.Length = lock gate (fun () -> buffer.Length)

    override _.Position
        with get () = 0L
        and set _ = raise (NotSupportedException())

    override _.Flush() = ()
    override _.FlushAsync(_: CancellationToken) = Task.CompletedTask
    override _.Read(_: byte[], _: int, _: int) : int = raise (NotSupportedException())
    override _.Seek(_: int64, _: SeekOrigin) : int64 = raise (NotSupportedException())
    override _.SetLength(_: int64) = raise (NotSupportedException())
    override _.Write(bytes: byte[], offset: int, count: int) = append bytes offset count

    override _.WriteAsync(bytes: byte[], offset: int, count: int, _: CancellationToken) =
        append bytes offset count
        Task.CompletedTask

    override _.WriteAsync(bytes: ReadOnlyMemory<byte>, _: CancellationToken) =
        let copy = bytes.ToArray()
        append copy 0 copy.Length
        ValueTask()

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
/// send back, `tab` the tab id, `lastEventId` what `EventSource` resends.
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

    let running = Task.Run(fun () -> l.Stream.Invoke ctx)
    ctx, body, running

/// The correlation token the stream handler set.
let private tokenOf (l: Live) (ctx: HttpContext) =
    let cookie = string ctx.Response.Headers["Set-Cookie"]
    let prefix = l.Config.CookieName + "="
    Uri.UnescapeDataString(cookie.Substring(prefix.Length).Split(';').[0])

/// A POST of one click on the event path, as the shim sends it.
let private click (l: Live) (token: string) (tab: string option) : int =
    let ctx = DefaultHttpContext()
    ctx.RequestServices <- l.App.Services
    ctx.Request.Method <- "POST"
    ctx.Request.Headers["Cookie"] <- $"{l.Config.CookieName}={token}"
    tab |> Option.iter (fun t -> ctx.Request.Headers[TabHeader] <- t)
    ctx.Request.Body <- new MemoryStream(Encoding.UTF8.GetBytes """{"nodeId":"go","event":"click"}""")
    (l.Event.Invoke ctx).Wait bound |> ignore
    ctx.Response.StatusCode

let private occurrences (needle: string) (hay: string) =
    (hay.Length - hay.Replace(needle, "").Length) / needle.Length

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Phase 1928 — server-driven sessions survive reconnect and concurrency"
        [ test "a reconnect with Last-Event-ID replays the missed frames into the session the client had" {
              let l = live increment
              use firstAbort = new CancellationTokenSource()
              let first, firstBody, firstRun = openStream l firstAbort.Token None None None false
              Expect.isTrue (eventually (fun () -> l.Registry.Count = 1)) "the stream opened its connection"
              let token = tokenOf l first

              Expect.equal (click l token None) 204 "the first click is accepted"
              Expect.equal (click l token None) 204 "the second click is accepted"
              Expect.isTrue (eventually (fun () -> firstBody.Text.Contains "id: 2")) "both frames streamed"

              // The client applied frame 1 and then lost the stream.
              firstAbort.Cancel()
              Expect.isTrue (firstRun.Wait bound) "the dropped stream ends"
              Expect.equal l.Registry.Count 1 "its connection is held for the reconnect, not removed"
              Expect.equal (click l token None) 204 "a click while detached still reaches the session"

              use secondAbort = new CancellationTokenSource()

              let _, secondBody, secondRun =
                  openStream l secondAbort.Token (Some token) None (Some 1) false

              Expect.isTrue (eventually (fun () -> secondBody.Text.Contains "id: 3")) "the missed frames arrive"
              Expect.isFalse (secondBody.Text.Contains "id: 1") "and only the missed ones"
              Expect.stringContains secondBody.Text "id: 2" "frame 2, which the client never applied, is replayed"
              Expect.equal l.SessionsMade.Value 1 "no session was minted for the reconnect"
              Expect.equal l.Connections.Count 1 "nor a connection"
              Expect.equal l.Connections[0].Session.Model 3 "the model is the one the client had, every click in it"

              Expect.equal (click l token None) 204 "a click after the reconnect"
              Expect.isTrue (eventually (fun () -> secondBody.Text.Contains "id: 4")) "streams on the new stream"
              secondAbort.Cancel()
              Expect.isTrue (secondRun.Wait bound) "the reconnected stream ends with its client"
          }

          test "a reconnect from behind the evicted buffer is re-baselined on its session, not silently reset" {
              let l = live increment
              use firstAbort = new CancellationTokenSource()
              let first, _, firstRun = openStream l firstAbort.Token None None None false
              Expect.isTrue (eventually (fun () -> l.Registry.Count = 1)) "the stream opened its connection"
              let token = tokenOf l first
              firstAbort.Cancel()
              Expect.isTrue (firstRun.Wait bound) "the stream drops"

              // More frames than the replay buffer keeps arrive while detached.
              let steps = LiveConnectionDefaults.ReplayBufferCapacity + 8

              for _ in 1..steps do
                  click l token None |> ignore

              use secondAbort = new CancellationTokenSource()

              let _, body, secondRun =
                  openStream l secondAbort.Token (Some token) None (Some 1) false

              let rebaselineId = $"id: {steps + 1}"
              Expect.isTrue (eventually (fun () -> body.Text.Contains rebaselineId)) "one re-baseline frame arrives"
              Expect.equal (occurrences "id: " body.Text) 1 "and nothing else: no partial replay"
              Expect.stringContains body.Text "ReplaceFragment" "it replaces the whole document"
              Expect.stringContains body.Text "\"nodeId\":\"root\"" "from the root"
              Expect.equal l.SessionsMade.Value 1 "the session was kept, not reset"
              Expect.equal l.Connections[0].Session.Model steps "and the document is its CURRENT model"
              secondAbort.Cancel()
              Expect.isTrue (secondRun.Wait bound) "the stream ends with its client"
          }

          test "two concurrent POSTs on one connection get two Seq values and one consistent session" {
              // A slow update widens the window two unserialised steps would race in.
              let slow (msg: Msg) (count: int) =
                  Thread.Sleep 50
                  increment msg count

              let l = live slow
              use abort = new CancellationTokenSource()
              let ctx, body, running = openStream l abort.Token None None None false
              Expect.isTrue (eventually (fun () -> l.Registry.Count = 1)) "the stream opened its connection"
              let token = tokenOf l ctx

              let posts = [| for _ in 1..2 -> Task.Run(fun () -> click l token None) |]
              Expect.isTrue (Task.WaitAll(posts |> Array.map (fun t -> t :> Task), bound)) "both POSTs answer"
              Expect.equal (posts |> Array.map (fun t -> t.Result)) [| 204; 204 |] "both are accepted"

              Expect.isTrue (eventually (fun () -> body.Text.Contains "id: 2")) "two frames streamed"
              Expect.equal (occurrences "id: 1\n" body.Text) 1 "Seq 1 once"
              Expect.equal (occurrences "id: 2\n" body.Text) 1 "Seq 2 once"
              Expect.equal l.Connections[0].Session.Model 2 "neither step's transition was lost"
              Expect.equal l.Connections[0].Sequence 2 "and the sequence counts both"
              abort.Cancel()
              Expect.isTrue (running.Wait bound) "the stream ends with its client"
          }

          test "a second tab does not steal the first tab's stream" {
              let l = live increment
              use abortA = new CancellationTokenSource()
              use abortB = new CancellationTokenSource()
              let ctxA, bodyA, runA = openStream l abortA.Token None (Some "tab-a") None false
              Expect.isTrue (eventually (fun () -> l.Registry.Count = 1)) "tab A opened its connection"
              let token = tokenOf l ctxA

              // Tab B shares the browser's cookie, as every tab of one origin does.
              let ctxB, bodyB, runB =
                  openStream l abortB.Token (Some token) (Some "tab-b") None false

              Expect.isTrue (eventually (fun () -> l.Registry.Count = 2)) "tab B opened a connection of its own"
              Expect.equal (tokenOf l ctxB) token "the cookie keeps binding the same principal-bound id"

              Expect.equal (click l token (Some "tab-a")) 204 "tab A clicks"
              Expect.isTrue (eventually (fun () -> bodyA.Text.Contains "id: 1")) "tab A's frame reaches tab A"
              Expect.isFalse (bodyB.Text.Contains "id: ") "and not tab B"
              Expect.equal l.Connections[0].Session.Model 1 "tab A's session moved"
              Expect.equal l.Connections[1].Session.Model 0 "tab B's did not"

              Expect.equal (click l token (Some "tab-b")) 204 "tab B clicks"
              Expect.isTrue (eventually (fun () -> bodyB.Text.Contains "id: 1")) "tab B's frame reaches tab B"
              Expect.equal (occurrences "id: " bodyA.Text) 1 "tab A's stream carries only its own frame"
              Expect.isFalse runA.IsCompleted "tab A's stream is still open"

              Expect.equal (click l token (Some "not a tab!")) 400 "a malformed tab id is refused"
              abortA.Cancel()
              abortB.Cancel()
              Expect.isTrue (runA.Wait bound && runB.Wait bound) "both streams end with their clients"
          }

          test "a write failure leaves the registry empty" {
              let l = live increment
              l.Registry.ReconnectGrace <- TimeSpan.FromSeconds 2.0
              use abort = new CancellationTokenSource()
              let ctx, _, running = openStream l abort.Token None None None true
              Expect.isTrue (eventually (fun () -> l.Registry.Count = 1)) "the stream opened its connection"

              // The first frame's write throws, as a write to a dropped client does.
              click l (tokenOf l ctx) None |> ignore
              Expect.isTrue (eventually (fun () -> running.IsCompleted)) "the stream ends on the failed write"
              Expect.isTrue running.IsFaulted "with the write's own failure, not swallowed"
              Expect.equal l.Registry.Count 1 "the connection is held for the reconnect grace"
              Expect.isTrue (eventually (fun () -> l.Registry.Count = 0)) "and then evicted: nothing leaks"

              // With no grace the stream's `finally` evicts at once.
              l.Registry.ReconnectGrace <- TimeSpan.Zero
              let ctx2, _, running2 = openStream l abort.Token None None None true
              Expect.isTrue (eventually (fun () -> l.Registry.Count = 1)) "a second stream opened"
              click l (tokenOf l ctx2) None |> ignore
              Expect.isTrue (eventually (fun () -> running2.IsCompleted)) "it ends on the failed write"
              Expect.equal l.Registry.Count 0 "and the registry is empty when it has"
          } ]
