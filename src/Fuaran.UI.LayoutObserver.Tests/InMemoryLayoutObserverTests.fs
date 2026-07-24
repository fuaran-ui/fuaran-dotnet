module Fuaran.UI.LayoutObserver.Tests.InMemoryLayoutObserverTests

// ─── InMemoryLayoutObserver round-trip + subscriber semantics ───
//
// Acceptance criteria covered:
//   - Observe / ObserveTree round-trip.
//   - Cost model — 1000 update events with no flag change emit ≤ 10
//     callbacks (`EmitOnFlagChangeOnly = true`). Modelled in-memory
//     by calling Update 1000 times against an unchanged fixture and
//     asserting the subscriber callback count.
//   - Subscribe IDisposable correctly removes the handler.

open Expecto
open Fuaran.UI.LayoutObserver

let private fixture (input: Flags.LayoutInput) : LayoutFixture = { Input = input; Parent = None }

let private childFixture (parent: string) (input: Flags.LayoutInput) : LayoutFixture =
    { Input = input; Parent = Some parent }

[<Tests>]
let tests =
    testList
        "InMemoryLayoutObserver"
        [ test "Observe returns the registered observation" {
              let observer = InMemoryLayoutObserver.create ()
              observer.RegisterFixture("panel-1", fixture (Flags.LayoutInput.empty 200.0 100.0))

              let read = (observer :> ILayoutObserver).Observe("panel-1")
              Expect.isSome read "observation should be present"
              Expect.equal (read |> Option.map _.Width) (Some 200.0) "width round-trips"
          }

          test "Observe returns None for unregistered node" {
              let observer = InMemoryLayoutObserver.create ()
              let read = (observer :> ILayoutObserver).Observe("ghost")
              Expect.equal read None "unregistered node yields None"
          }

          test "ObserveTree walks parent pointers in BFS order" {
              let observer = InMemoryLayoutObserver.create ()
              observer.RegisterFixture("root", fixture (Flags.LayoutInput.empty 800.0 600.0))

              observer.RegisterFixture("child-a", childFixture "root" (Flags.LayoutInput.empty 400.0 600.0))

              observer.RegisterFixture("child-b", childFixture "root" (Flags.LayoutInput.empty 400.0 600.0))

              observer.RegisterFixture("grandchild", childFixture "child-a" (Flags.LayoutInput.empty 200.0 600.0))

              let tree = (observer :> ILayoutObserver).ObserveTree("root")
              let ids = tree |> List.map _.NodeId

              Expect.equal (List.length ids) 4 "all four nodes observed"
              Expect.equal (List.head ids) "root" "root comes first"
              // BFS layer 1 (children) before layer 2 (grandchildren).
              Expect.contains (ids |> List.take 3) "child-a" "child-a in layer 1"
              Expect.contains (ids |> List.take 3) "child-b" "child-b in layer 1"
              Expect.equal (List.last ids) "grandchild" "grandchild last (layer 2)"
          }

          test "Subscribe handler fires on initial RegisterFixture" {
              let observer = InMemoryLayoutObserver.create ()
              let received = ResizeArray<string * LayoutObservation>()

              use _sub = (observer :> ILayoutObserver).Subscribe(fun pair -> received.Add(pair))

              observer.RegisterFixture("panel-1", fixture (Flags.LayoutInput.empty 100.0 50.0))

              Expect.equal received.Count 1 "initial registration always emits"
              Expect.equal (fst received[0]) "panel-1" "nodeId round-trips"
          }

          test "1000-event burst with no flag change emits ≤ 10 (EmitOnFlagChangeOnly cost-model)" {
              // The acceptance criterion describes a browser-side
              // 1000-event burst coalescing via rAF + debounce. The
              // in-memory observer doesn't have debounce timing, but
              // it DOES honour the EmitOnFlagChangeOnly contract —
              // and an unchanged fixture produces an unchanged flag
              // set, which means every Update past the first is a
              // no-emission. So the same 1000-Update burst against a
              // stable fixture produces exactly ONE subscriber
              // emission (the initial Register).
              let observer = InMemoryLayoutObserver.create ()
              let received = ResizeArray<string * LayoutObservation>()

              use _sub = (observer :> ILayoutObserver).Subscribe(fun pair -> received.Add(pair))

              // Initial register (emits once).
              observer.RegisterFixture("panel-1", fixture (Flags.LayoutInput.empty 100.0 50.0))

              // Fire 1000 updates with the same input — no flag
              // change, no emission.
              let stableInput = Flags.LayoutInput.empty 100.0 50.0

              for _ in 1..1000 do
                  observer.Update("panel-1", stableInput)

              Expect.isLessThanOrEqual
                  received.Count
                  10
                  $"acceptance: 1000 stable updates -> ≤ 10 emissions, got {received.Count}"

              Expect.equal received.Count 1 "in-memory observer should produce exactly the initial emission"
          }

          test "Update emits when flag set changes" {
              let observer = InMemoryLayoutObserver.create ()
              let received = ResizeArray<string * LayoutObservation>()

              use _sub = (observer :> ILayoutObserver).Subscribe(fun pair -> received.Add(pair))

              observer.RegisterFixture("panel-1", fixture (Flags.LayoutInput.empty 100.0 50.0))

              // First update — same flags (none), no emission.
              observer.Update("panel-1", Flags.LayoutInput.empty 110.0 55.0)

              // Second update — introduce OverflowHorizontal, flag set changes, emit.
              let overflowed =
                  { Flags.LayoutInput.empty 100.0 50.0 with
                      ScrollWidth = Some 250.0
                      ClientWidth = Some 100.0
                      OverflowX = Some "hidden" }

              observer.Update("panel-1", overflowed)

              Expect.equal received.Count 2 "initial + flag-change emission"

              let lastFlags = (snd received[received.Count - 1]).Flags
              Expect.contains lastFlags LayoutFlag.OverflowHorizontal "flag surfaced"
          }

          test "Subscribe IDisposable removes the handler" {
              let observer = InMemoryLayoutObserver.create ()
              let received = ResizeArray<string * LayoutObservation>()

              let sub = (observer :> ILayoutObserver).Subscribe(fun pair -> received.Add(pair))
              observer.RegisterFixture("panel-1", fixture (Flags.LayoutInput.empty 100.0 50.0))
              Expect.equal received.Count 1 "handler fired once"

              sub.Dispose()

              observer.RegisterFixture("panel-2", fixture (Flags.LayoutInput.empty 200.0 100.0))
              Expect.equal received.Count 1 "disposed handler does not fire again"
          }

          test "Multiple subscribers all receive emissions" {
              let observer = InMemoryLayoutObserver.create ()
              let countA = ref 0
              let countB = ref 0

              use _subA =
                  (observer :> ILayoutObserver).Subscribe(fun _ -> countA.Value <- countA.Value + 1)

              use _subB =
                  (observer :> ILayoutObserver).Subscribe(fun _ -> countB.Value <- countB.Value + 1)

              observer.RegisterFixture("panel-1", fixture (Flags.LayoutInput.empty 100.0 50.0))

              Expect.equal countA.Value 1 "subscriber A received initial emission"
              Expect.equal countB.Value 1 "subscriber B received initial emission"
          }

          test "EmitOnFlagChangeOnly = false fires on every update" {
              let opts =
                  { LayoutObserverOptions.defaults with
                      EmitOnFlagChangeOnly = false }

              let observer = InMemoryLayoutObserver.createWith opts
              let received = ResizeArray<string * LayoutObservation>()

              use _sub = (observer :> ILayoutObserver).Subscribe(fun pair -> received.Add(pair))

              observer.RegisterFixture("panel-1", fixture (Flags.LayoutInput.empty 100.0 50.0))

              for _ in 1..5 do
                  observer.Update("panel-1", Flags.LayoutInput.empty 100.0 50.0)

              Expect.equal received.Count 6 "initial + 5 update emissions"
          }

          test "Unregister drops the node" {
              let observer = InMemoryLayoutObserver.create ()
              observer.RegisterFixture("panel-1", fixture (Flags.LayoutInput.empty 100.0 50.0))

              (observer :> ILayoutObserver).Unregister("panel-1")

              Expect.equal ((observer :> ILayoutObserver).Observe("panel-1")) None "node gone"
          } ]
