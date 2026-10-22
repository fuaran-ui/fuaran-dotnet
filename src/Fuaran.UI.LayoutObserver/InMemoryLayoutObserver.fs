namespace Fuaran.UI.LayoutObserver

// ─── InMemoryLayoutObserver — .NET-side test + eval substrate ───
//
// The headless counterpart to `BrowserLayoutObserver`.
// Accepts hand-authored `LayoutInput` fixtures keyed by NodeId,
// runs the shared `Flags.derive` logic, surfaces results via the
// same `ILayoutObserver` interface the browser observer satisfies.
//
// **A domain instance of `Fuaran.Core.Observer`'s witness.** The
// registry, the change-gated emission and the parent-pointer tree
// walk are Core's `ObserverWitness` functions over an `ObserverState`
// value this type threads (Phase 322 lifted them out of this file;
// Phase 2307 moved this type off the OO adapter Core removed). The
// UI supplies the flag vocabulary (`LayoutFlag`), the metric
// envelope (`Flags.LayoutInput`) and the pure `Flags.derive`, and
// projects each generic `Observation<LayoutInput, LayoutFlag>` back
// into the UI's richer `LayoutObservation` (width / height /
// viewport coordinates read off the input). Subscription is host
// state, so the subscriber list lives here, beside the state.
//
// **Why a separate concrete?** Tests + future eval gates need
// to drive flag derivation without browser substrate; mocking
// `ResizeObserver` + `getBoundingClientRect` from .NET-side
// Expecto would be a leaky abstraction. The in-memory observer is
// the substrate-free path: write a fixture, register it, assert
// the derived flags or the subscriber emission pattern.
//
// **Tree walk via parent pointers.** Browser-side `ObserveTree`
// queries the DOM; the core engine walks an explicit parent-pointer
// graph built from the fixture's parent declarations. Sufficient for
// test purposes; the browser observer's DOM-based walk is the
// production path.

open Fuaran.Core

/// One in-memory test fixture entry: the `LayoutInput` to drive
/// derivation, plus an optional parent NodeId to support tree
/// walks.
type LayoutFixture =
    { Input: Flags.LayoutInput
      Parent: string option }

/// In-memory observer. Construct with the desired options; drive
/// `RegisterFixture` + `Update` from tests; assert via `Observe` /
/// `ObserveTree` / subscriber callbacks. A domain instance of Core's
/// `ObserverWitness` (Phase 322, Phase 2307).
type InMemoryLayoutObserver(options: LayoutObserverOptions) =
    let witness = InMemoryLayoutObserver.Witness options
    let mutable state = ObserverWitness.empty<Flags.LayoutInput, LayoutFlag>
    let subscribers = ResizeArray<string * LayoutObservation -> unit>()

    /// Project a generic observation back into the UI's geometric
    /// observation shape — width / height come straight off the
    /// input; viewport coordinates are the element-rect origin.
    let project (o: Observation<Flags.LayoutInput, LayoutFlag>) : LayoutObservation =
        let elL, elT, _, _ = o.Input.ElementRect

        { NodeId = o.NodeId
          Width = o.Input.Width
          Height = o.Input.Height
          ViewportX = elL
          ViewportY = elT
          Flags = o.Flags }

    // Delivery iterates a SNAPSHOT of the subscriber list, so a subscriber
    // that disposes itself or subscribes another during its callback changes
    // the list for the NEXT emission, never the one in flight; and a
    // subscriber throwing must not poison its siblings.
    let emit (observation: Observation<Flags.LayoutInput, LayoutFlag>) =
        let payload = observation.NodeId, project observation

        for subscriber in subscribers.ToArray() do
            try
                subscriber payload
            with ex ->
                ignore ex

    /// The witness this observer runs on — the UI's `Flags.derive` under
    /// the options' emit policy (structural flag-set equality). What
    /// `Conformance.observerLawsAt` certifies.
    static member Witness(options: LayoutObserverOptions) : ObserverWitness<Flags.LayoutInput, LayoutFlag> =
        ObserverWitness.createWith
            (Flags.derive options)
            { EmitOnFlagChangeOnly = options.EmitOnFlagChangeOnly
              FlagsEqual = (=) }

    /// Register or replace a fixture for `nodeId`. Fires an initial
    /// emission for the registered node regardless of
    /// `EmitOnFlagChangeOnly` (initial-emission rule).
    member _.RegisterFixture(nodeId: string, fixture: LayoutFixture) : unit =
        let next, observation =
            ObserverWitness.register witness nodeId fixture.Input fixture.Parent state

        state <- next
        emit observation

    /// Replace the existing fixture's input. Honours the
    /// `EmitOnFlagChangeOnly` option — fires the subscriber
    /// callback only when the derived flag set differs from the
    /// previous emission (or always, when the option is false).
    /// No-op if `nodeId` isn't registered.
    member _.Update(nodeId: string, input: Flags.LayoutInput) : unit =
        let next, emission = ObserverWitness.update witness nodeId input state
        state <- next
        emission |> Option.iter emit

    interface ILayoutObserver with
        member _.Observe(nodeId: string) : LayoutObservation option =
            ObserverWitness.snapshot state nodeId |> Option.map project

        member _.ObserveTree(rootNodeId: string) : LayoutObservation list =
            ObserverWitness.observeTree state rootNodeId |> List.map project

        member _.Subscribe(handler: string * LayoutObservation -> unit) : System.IDisposable =
            subscribers.Add(handler)

            { new System.IDisposable with
                member _.Dispose() = subscribers.Remove(handler) |> ignore }

        member this.Register(nodeId: string, _element: obj) : unit =
            // Default-fixture registration — the test author would
            // normally call RegisterFixture with a populated input.
            // Bare Register with no fixture creates an empty
            // baseline (0x0 element rect) so calls from the
            // renderer's mount hook don't crash on the in-memory
            // observer. Idempotent — only register if absent.
            if not (ObserverWitness.isRegistered state nodeId) then
                let baselineInput = Flags.LayoutInput.empty 0.0 0.0
                this.RegisterFixture(nodeId, { Input = baselineInput; Parent = None })

        member _.Unregister(nodeId: string) : unit =
            state <- ObserverWitness.unregister nodeId state

module InMemoryLayoutObserver =
    /// Construct with the v1 default options.
    let create () : InMemoryLayoutObserver =
        InMemoryLayoutObserver(LayoutObserverOptions.defaults)

    /// Construct with hosting-tunable options.
    let createWith (options: LayoutObserverOptions) : InMemoryLayoutObserver = InMemoryLayoutObserver(options)
