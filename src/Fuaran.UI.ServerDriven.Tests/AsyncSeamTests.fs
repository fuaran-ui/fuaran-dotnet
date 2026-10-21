module Fuaran.UI.ServerDriven.Tests.AsyncSeamTests

// ─── Phase 2170: the server-driven session seams can await ─────────────────
//
// Two seams, each beside its synchronous form: an update that returns awaited
// effects (`Driver.initWithEffects` + `LiveConnection.EnableEffects`), and a
// session factory that awaits (`AsyncSessionFactory.start`, which the two
// transports' async maps call). What is pinned here is the claim the phase
// makes about each — work that never completes blocks neither the caller's
// thread nor another session, and awaited effects keep their order.
//
// Every wait is BOUNDED: a seam that does block fails here with a timeout, it
// does not hang the suite.

#nowarn "44" // `Action.Dispatch` is in-process-only (Phase 1152); this suite is in-process.

open System
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.ServerDriven.Driver

type Msg =
    | Click of string
    | Loaded of string

type Model = string list

let private bound = TimeSpan.FromSeconds 10.0

/// Poll `cond` until it holds or the bound passes; false on timeout.
let private eventually (cond: unit -> bool) : bool =
    let sw = Stopwatch.StartNew()
    let mutable ok = cond ()

    while not ok && sw.Elapsed < bound do
        Thread.Sleep 10
        ok <- cond ()

    ok

let private view (m: Model) : Node<Msg> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.button
                      "a"
                      { Defaults.button<Msg> with
                          OnClick = Action.Dispatch(Click "a") }
                  Fuaran.button
                      "b"
                      { Defaults.button<Msg> with
                          OnClick = Action.Dispatch(Click "b") }
                  // One gesture whose fold dispatches two messages: the second
                  // is the one a test's update refuses.
                  Fuaran.button
                      "ab"
                      { Defaults.button<Msg> with
                          OnClick = Action.Chain [ Action.Dispatch(Click "a"); Action.Dispatch(Click "x") ] }
                  Fuaran.markdown "log" (String.concat "," m) ] }

let private render (n: Node<Msg>) = $"<f id='{n.Id}'/>"

let private click connId nodeId =
    { ConnId = connId
      NodeId = nodeId
      Event = "click"
      Payload = Map.empty
      LastSeq = 0 }

/// Deliver an inbound event from a worker, bounded: a connection that awaited an
/// effect inside `Handle` fails here at the bound instead of hanging the suite.
let private send (channel: InMemoryChannel) (ev: LiveEvent) =
    if not (Task.Run(fun () -> channel.Send ev).Wait bound) then
        failtest "Handle did not return within the bound"

/// An effect that never completes (nothing ever sets its source).
let private never () : Async<Msg option> =
    async {
        let! _ = Async.AwaitTask(TaskCompletionSource<unit>().Task)
        return None
    }

/// A connection over `update`, with the effect runner on and faults collected.
let private connect connId (update: EffectfulUpdate<Model, Msg>) =
    let session, outbox =
        initWithEffects (DriverServices.createPermissive render) update view []

    let channel = InMemoryChannel()
    let conn = LiveConnection(connId, session, channel)
    let faults = Collections.Concurrent.ConcurrentQueue<exn>()
    conn.EnableEffects(outbox, faults.Enqueue)
    channel, conn, faults

[<Tests>]
let tests =
    // Sequenced: these tests time effects against the thread pool, so they must
    // not compete with each other for it.
    testSequenced
    <| testList
        "Phase 2170 — the session seams can await"
        [ test "an awaited effect's message is applied as its own step and pushed" {
              let gate = TaskCompletionSource<unit>()

              let update msg (m: Model) =
                  match msg with
                  | Click id ->
                      m @ [ "click:" + id ],
                      [ async {
                            do! Async.AwaitTask gate.Task
                            return Some(Loaded id)
                        } ]
                  | Loaded id -> m @ [ "loaded:" + id ], []

              let channel, conn, faults = connect "c1" update

              // From a worker, bounded: a runner that awaited inline would block
              // here until the effect answered, which it does not until below.
              Expect.isTrue
                  (Task.Run(fun () -> channel.Send(click "c1" "a")).Wait bound)
                  "Handle returned while its effect was still pending"

              // The synchronous step is done and pushed before the effect answers.
              Expect.equal conn.Session.Model [ "click:a" ] "the step applied without awaiting its effect"
              Expect.equal conn.Sequence 1 "the step's frame was pushed"

              gate.SetResult()
              Expect.isTrue (eventually (fun () -> conn.Sequence = 2)) "the fed-back message pushed a second frame"
              Expect.equal conn.Session.Model [ "click:a"; "loaded:a" ] "the effect's message was folded through update"
              Expect.isEmpty faults "no fault"
          }

          test "awaited effects keep their order, within a step and across steps" {
              let first = TaskCompletionSource<unit>()

              let update msg (m: Model) =
                  match msg with
                  | Click "a" ->
                      m,
                      [ async {
                            // The slow one is FIRST: an unordered runner would let
                            // the fast one overtake it.
                            do! Async.AwaitTask first.Task
                            return Some(Loaded "a1")
                        }
                        async { return Some(Loaded "a2") } ]
                  | Click _ -> m, [ async { return Some(Loaded "b1") } ]
                  | Loaded id -> m @ [ id ], []

              let channel, conn, _ = connect "c1" update
              send channel (click "c1" "a")
              send channel (click "c1" "b")
              // Long enough for an unordered runner to apply both fast effects.
              Thread.Sleep 300
              Expect.equal conn.Session.Model [] "nothing overtook the pending first effect"
              first.SetResult()

              Expect.isTrue
                  (eventually (fun () -> List.length conn.Session.Model = 3))
                  "every effect answered within the bound"

              Expect.equal conn.Session.Model [ "a1"; "a2"; "b1" ] "awaited in the order returned"
          }

          test "an effect that never completes blocks neither the session loop nor another session" {
              let stuckUpdate msg (m: Model) =
                  match msg with
                  | Click id -> m @ [ id ], [ never () ]
                  | Loaded _ -> m, []

              let freeUpdate msg (m: Model) =
                  match msg with
                  | Click id -> m @ [ id ], [ async { return Some(Loaded id) } ]
                  | Loaded id -> m @ [ "loaded:" + id ], []

              let stuckChannel, stuck, _ = connect "stuck" stuckUpdate
              let freeChannel, free, _ = connect "free" freeUpdate

              // Driven from a worker so a BLOCKING runner fails this test at the
              // bound instead of hanging the suite.
              let sends =
                  Task.Run(fun () ->
                      stuckChannel.Send(click "stuck" "a")
                      stuckChannel.Send(click "stuck" "b")
                      freeChannel.Send(click "free" "a"))

              Expect.isTrue (sends.Wait bound) "Handle returned without awaiting a never-completing effect"

              Expect.equal stuck.Session.Model [ "a"; "b" ] "the stuck session still steps inbound events"

              Expect.isTrue
                  (eventually (fun () -> free.Session.Model = [ "a"; "loaded:a" ]))
                  "the other session's effect ran while this one's never answers"
          }

          test "a faulting effect is reported and the runner moves on" {
              let update msg (m: Model) =
                  match msg with
                  | Click _ ->
                      m,
                      [ async { return failwith "store unreachable" }
                        async { return Some(Loaded "next") } ]
                  | Loaded id -> m @ [ id ], []

              let channel, conn, faults = connect "c1" update
              send channel (click "c1" "a")
              Expect.isTrue (eventually (fun () -> conn.Session.Model = [ "next" ])) "the next effect still ran"

              Expect.isTrue
                  (faults |> Seq.exists (fun ex -> ex.Message = "store unreachable"))
                  "the fault reached onFault"
          }

          test "a step whose update throws runs none of the effects it returned" {
              let ran = ref 0

              let update msg (m: Model) =
                  match msg with
                  | Click "a" ->
                      m @ [ "a" ],
                      [ async {
                            Interlocked.Increment ran |> ignore
                            return None
                        } ]
                  | Click _ -> failwith "update refused"
                  | Loaded _ -> m, []

              let channel, conn, _ = connect "c1" update

              // The fold's FIRST message returns an effect, its second throws:
              // the step did not happen, so the effect must die with it.
              Expect.throws (fun () -> channel.Send(click "c1" "ab")) "the throwing update propagates as before"
              Expect.equal conn.Session.Model [] "the session did not move"

              send channel (click "c1" "a")
              Expect.isTrue (eventually (fun () -> ran.Value = 1)) "the surviving step's effect ran"
              Thread.Sleep 50
              Expect.equal ran.Value 1 "and the discarded step's effect never did"
          }

          test "effects returned before EnableEffects wait, then run in order once it is called" {
              let update msg (m: Model) =
                  match msg with
                  | Click id -> m, [ async { return Some(Loaded id) } ]
                  | Loaded id -> m @ [ id ], []

              let session, outbox =
                  initWithEffects (DriverServices.createPermissive render) update view []

              let channel = InMemoryChannel()
              let conn = LiveConnection("c1", session, channel)
              send channel (click "c1" "a")
              send channel (click "c1" "b")
              Expect.equal outbox.Pending 2 "held until the runner is on"
              Expect.equal conn.Session.Model [] "nothing ran"

              conn.EnableEffects(outbox, ignore)
              Expect.isTrue (eventually (fun () -> conn.Session.Model = [ "a"; "b" ])) "ran in order once enabled"
              Expect.equal outbox.Pending 0 "the outbox drained"
          }

          test "the synchronous update and connection are unchanged beside the async forms" {
              let session =
                  init
                      (DriverServices.createPermissive render)
                      (fun msg (m: Model) ->
                          match msg with
                          | Click id -> m @ [ id ]
                          | Loaded _ -> m)
                      view
                      []

              let channel = InMemoryChannel()
              let conn = LiveConnection("c1", session, channel)
              send channel (click "c1" "a")
              Expect.equal conn.Session.Model [ "a" ] "a sync session steps exactly as before"
              Expect.equal conn.Sequence 1 "and pushes its frame"
          }

          testList
              "AsyncSessionFactory.start"
              [ test "a factory that never answers blocks neither the caller nor another factory" {
                    use cts = new CancellationTokenSource()

                    let hanging: AsyncSessionFactory<Model, Msg> =
                        fun () ->
                            async {
                                let! _ = Async.AwaitTask(TaskCompletionSource<unit>().Task)
                                return failwith "unreachable"
                            }

                    let ready: AsyncSessionFactory<Model, Msg> =
                        fun () ->
                            async {
                                do! Async.Sleep 1
                                let session = init (DriverServices.create render) (fun _ m -> m) view [ "loaded" ]
                                return session, None
                            }

                    let sw = Stopwatch.StartNew()
                    let stuck = AsyncSessionFactory.start cts.Token hanging
                    let other = AsyncSessionFactory.start CancellationToken.None ready
                    Expect.isLessThan sw.Elapsed bound "start returned without awaiting the factory"

                    Expect.isTrue (other.Wait bound) "the other factory answered within the bound"

                    match other.Result with
                    | Some(session, None) -> Expect.equal session.Model [ "loaded" ] "the awaited session"
                    | other -> failtestf "expected the session, got %A" other

                    Expect.isFalse stuck.IsCompleted "the hanging factory is still pending"
                    cts.Cancel()
                    Expect.isTrue (stuck.Wait bound) "the connection's end releases the wait within the bound"
                    Expect.isNone stuck.Result "None: the connection ended before its session existed"
                }

                test "a factory that throws faults the task rather than answering None" {
                    let broken: AsyncSessionFactory<Model, Msg> =
                        fun () -> async { return failwith "store down" }

                    let t = AsyncSessionFactory.start CancellationToken.None broken

                    let ex = Expect.throwsC (fun () -> t.Wait bound |> ignore) id

                    Expect.stringContains (string ex) "store down" "the factory's own failure"
                } ] ]
