module Fuaran.UI.StyleObserver.Tests.InMemoryStyleObserverTests

// ─── InMemoryStyleObserver round-trip + subscriber semantics ────
//
// Twin of InMemoryLayoutObserverTests:
//   - Observe / ObserveTree round-trip.
//   - Cost model — 1000 stable updates with no flag change emit ≤ 10
//     callbacks (EmitOnFlagChangeOnly).
//   - Subscribe IDisposable correctly removes the handler.

open Expecto
open Fuaran.UI.StyleObserver

let private fixture (input: Flags.StyleInput) : StyleFixture = { Input = input; Parent = None }

let private childFixture (parent: string) (input: Flags.StyleInput) : StyleFixture =
    { Input = input; Parent = Some parent }

/// A legible fixture — opaque black on white, no flags.
let private legible: Flags.StyleInput =
    { Flags.StyleInput.baseline with
        Foreground = Rgba.black
        BackgroundLayers = [ Rgba.white ] }

/// An invisible fixture — white on white.
let private invisible: Flags.StyleInput =
    { Flags.StyleInput.baseline with
        Foreground = Rgba.white
        BackgroundLayers = [ Rgba.white ] }

[<Tests>]
let tests =
    testList
        "InMemoryStyleObserver"
        [ test "Observe returns the registered observation with derived contrast" {
              let observer = InMemoryStyleObserver.create ()
              observer.RegisterFixture("metric-1", fixture legible)

              let read = (observer :> IStyleObserver).Observe("metric-1")
              Expect.isSome read "observation present"
              let obs = read.Value
              Expect.floatClose Accuracy.medium obs.ContrastRatio 21.0 "black-on-white contrast"
              Expect.isEmpty obs.Flags "legible → no flags"
          }

          test "Observe returns None for unregistered node" {
              let observer = InMemoryStyleObserver.create ()
              Expect.equal ((observer :> IStyleObserver).Observe("ghost")) None "unregistered → None"
          }

          test "Observe surfaces InvisibleText for white-on-white" {
              let observer = InMemoryStyleObserver.create ()
              observer.RegisterFixture("ghost-text", fixture invisible)

              let obs = ((observer :> IStyleObserver).Observe("ghost-text")).Value

              match obs.Flags with
              | [ StyleFlag.InvisibleText _ ] -> ()
              | other -> failwithf "expected [InvisibleText], got %A" other
          }

          test "ObserveTree walks parent pointers in BFS order" {
              let observer = InMemoryStyleObserver.create ()
              observer.RegisterFixture("root", fixture legible)
              observer.RegisterFixture("child-a", childFixture "root" legible)
              observer.RegisterFixture("child-b", childFixture "root" legible)
              observer.RegisterFixture("grandchild", childFixture "child-a" legible)

              let ids = (observer :> IStyleObserver).ObserveTree("root") |> List.map _.NodeId

              Expect.equal (List.length ids) 4 "all four nodes"
              Expect.equal (List.head ids) "root" "root first"
              Expect.contains (ids |> List.take 3) "child-a" "child-a in layer 1"
              Expect.contains (ids |> List.take 3) "child-b" "child-b in layer 1"
              Expect.equal (List.last ids) "grandchild" "grandchild last"
          }

          test "Subscribe handler fires on initial RegisterFixture" {
              let observer = InMemoryStyleObserver.create ()
              let received = ResizeArray<string * StyleObservation>()
              use _sub = (observer :> IStyleObserver).Subscribe(fun pair -> received.Add(pair))
              observer.RegisterFixture("metric-1", fixture legible)

              Expect.equal received.Count 1 "initial registration emits"
              Expect.equal (fst received[0]) "metric-1" "nodeId round-trips"
          }

          test "1000 stable updates with no flag change emit ≤ 10 (cost model)" {
              let observer = InMemoryStyleObserver.create ()
              let received = ResizeArray<string * StyleObservation>()
              use _sub = (observer :> IStyleObserver).Subscribe(fun pair -> received.Add(pair))

              observer.RegisterFixture("metric-1", fixture legible)

              for _ in 1..1000 do
                  observer.Update("metric-1", legible)

              Expect.isLessThanOrEqual received.Count 10 $"≤ 10 emissions, got {received.Count}"
              Expect.equal received.Count 1 "exactly the initial emission"
          }

          test "Update emits when the flag set changes" {
              let observer = InMemoryStyleObserver.create ()
              let received = ResizeArray<string * StyleObservation>()
              use _sub = (observer :> IStyleObserver).Subscribe(fun pair -> received.Add(pair))

              observer.RegisterFixture("metric-1", fixture legible)
              // Same flags (none) — no emission.
              observer.Update("metric-1", legible)
              // Flip to invisible — flag set changes, emit.
              observer.Update("metric-1", invisible)

              Expect.equal received.Count 2 "initial + flag-change emission"

              let lastFlags = (snd received[received.Count - 1]).Flags

              match lastFlags with
              | [ StyleFlag.InvisibleText _ ] -> ()
              | other -> failwithf "expected InvisibleText surfaced, got %A" other
          }

          test "Subscribe IDisposable removes the handler" {
              let observer = InMemoryStyleObserver.create ()
              let received = ResizeArray<string * StyleObservation>()
              let sub = (observer :> IStyleObserver).Subscribe(fun pair -> received.Add(pair))
              observer.RegisterFixture("metric-1", fixture legible)
              Expect.equal received.Count 1 "fired once"

              sub.Dispose()
              observer.RegisterFixture("metric-2", fixture legible)
              Expect.equal received.Count 1 "disposed handler does not fire"
          }

          test "EmitOnFlagChangeOnly = false fires on every update" {
              let observer =
                  InMemoryStyleObserver.createWith
                      { StyleObserverOptions.defaults with
                          EmitOnFlagChangeOnly = false }

              let received = ResizeArray<string * StyleObservation>()
              use _sub = (observer :> IStyleObserver).Subscribe(fun pair -> received.Add(pair))

              observer.RegisterFixture("metric-1", fixture legible)

              for _ in 1..5 do
                  observer.Update("metric-1", legible)

              Expect.equal received.Count 6 "initial + 5 updates"
          }

          test "Unregister drops the node" {
              let observer = InMemoryStyleObserver.create ()
              observer.RegisterFixture("metric-1", fixture legible)
              (observer :> IStyleObserver).Unregister("metric-1")
              Expect.equal ((observer :> IStyleObserver).Observe("metric-1")) None "node gone"
          }

          test "bare Register creates a legible baseline (no crash from mount hook)" {
              let observer = InMemoryStyleObserver.create ()
              (observer :> IStyleObserver).Register("mounted", System.Object())
              let obs = ((observer :> IStyleObserver).Observe("mounted")).Value
              Expect.isEmpty obs.Flags "baseline is legible black-on-white"
          } ]
