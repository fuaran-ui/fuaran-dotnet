namespace Fuaran.UI.StyleObserver

// ─── InMemoryStyleObserver — .NET-side test + eval substrate ────
//
// The headless counterpart to `BrowserStyleObserver` and the
// resolved-style twin of `InMemoryLayoutObserver`. Accepts
// hand-authored `StyleInput` fixtures keyed by NodeId, runs the
// shared `Flags.derive` / `Flags.toObservation` logic, surfaces
// results via the same `IStyleObserver` interface the browser
// observer satisfies.
//
// **Why a separate concrete?** Tests + future eval gates need to
// drive flag derivation without browser substrate; mocking
// `getComputedStyle` + the ancestor-compositing walk from .NET-side
// Expecto would be a leaky abstraction. The in-memory observer is
// the substrate-free path: write a fixture, register it, assert the
// derived flags / contrast / subscriber emission pattern.
//
// **A domain instance of `Fuaran.Core.Observer`'s witness (Phase
// 2307).** The registry, the change-gated emission, the initial
// emission on register and the parent-pointer tree walk are Core's
// `ObserverWitness` functions over an `ObserverState` value this type
// threads — the engine this file used to carry is gone. The UI
// supplies the flag content: `Flags.derive`, plus the manifest-aware
// `ManifestFlags.perNodeFlags` when a manifest is wired, and projects
// each generic observation back into the richer `StyleObservation`.
// Core's walk carries a visited set, so a cyclic parent declaration
// walks each node once and ends, where the walk this file used to
// carry looped forever. Subscription is host state, so the subscriber
// list lives here, beside the state. Debounce timing doesn't enter
// the in-memory path — the change-detection contract is what the
// cost-model test exercises.

open System
open Fuaran.Core
open Fuaran.UI.ThemeManifest

/// One in-memory test fixture entry: the `StyleInput` to drive
/// derivation, plus an optional parent NodeId to support tree walks.
type StyleFixture =
    { Input: Flags.StyleInput
      Parent: string option }

/// In-memory observer. Construct with the desired options (+ an optional
/// `ThemeManifest` for the Phase 146 manifest-aware flags); drive
/// `RegisterFixture` + `Update` from tests; assert via `Observe` /
/// `ObserveTree` / subscriber callbacks. When no manifest is supplied
/// only the manifest-free (Phase 144) flags fire (graceful degradation).
type InMemoryStyleObserver(options: StyleObserverOptions, manifest: ThemeManifest option) =
    let witness = InMemoryStyleObserver.Witness(options, manifest)
    let mutable state = ObserverWitness.empty<Flags.StyleInput, StyleFlag>
    let subscribers = ResizeArray<string * StyleObservation -> unit>()

    /// Project a generic observation back into the UI's resolved-style
    /// shape; the flags are the witness's, manifest-aware ones included.
    let project (o: Observation<Flags.StyleInput, StyleFlag>) : StyleObservation =
        { Flags.toObservation options o.NodeId o.Input with
            Flags = o.Flags }

    // Delivery iterates a SNAPSHOT of the subscriber list, so a subscriber
    // that disposes itself or subscribes another during its callback changes
    // the list for the NEXT emission, never the one in flight; and a
    // subscriber throwing must not poison its siblings (tests assert
    // directly).
    let emit (observation: Observation<Flags.StyleInput, StyleFlag>) =
        let payload = observation.NodeId, project observation

        for subscriber in subscribers.ToArray() do
            try
                subscriber payload
            with ex ->
                ignore ex

    /// Phase 144 constructor — no manifest, manifest-free flags only.
    new(options: StyleObserverOptions) = InMemoryStyleObserver(options, None)

    /// The witness this observer runs on — `Flags.derive` followed, when a
    /// manifest is wired, by its per-node (Phase 146) flags, under the
    /// options' emit policy (structural flag-set equality). The flags do not
    /// depend on the node id. What `Conformance.observerLawsAt` certifies.
    static member Witness
        (options: StyleObserverOptions, manifest: ThemeManifest option)
        : ObserverWitness<Flags.StyleInput, StyleFlag> =
        let derive (input: Flags.StyleInput) =
            match manifest with
            | Some m ->
                let baseObs = Flags.toObservation options "" input
                baseObs.Flags @ ManifestFlags.perNodeFlags m baseObs
            | None -> Flags.derive options input

        ObserverWitness.createWith
            derive
            { EmitOnFlagChangeOnly = options.EmitOnFlagChangeOnly
              FlagsEqual = (=) }

    /// Register or replace a fixture for `nodeId`. Fires an initial
    /// emission for the registered node regardless of
    /// `EmitOnFlagChangeOnly` (initial-emission rule).
    member _.RegisterFixture(nodeId: string, fixture: StyleFixture) : unit =
        let next, observation =
            ObserverWitness.register witness nodeId fixture.Input fixture.Parent state

        state <- next
        emit observation

    /// Replace the existing fixture's input. Honours
    /// `EmitOnFlagChangeOnly` — fires the subscriber callback only
    /// when the derived flag set differs from the previous emission
    /// (or always, when the option is false). No-op if `nodeId` isn't
    /// registered.
    member _.Update(nodeId: string, input: Flags.StyleInput) : unit =
        let next, emission = ObserverWitness.update witness nodeId input state
        state <- next
        emission |> Option.iter emit

    interface IStyleObserver with
        member _.Observe(nodeId: string) : StyleObservation option =
            ObserverWitness.snapshot state nodeId |> Option.map project

        member _.ObserveTree(rootNodeId: string) : StyleObservation list =
            // Breadth-first by level, then by registration order — and total
            // over a cyclic parent declaration (Core's visited set).
            ObserverWitness.observeTree state rootNodeId |> List.map project

        member _.Subscribe(handler: string * StyleObservation -> unit) : IDisposable =
            subscribers.Add(handler)

            { new IDisposable with
                member _.Dispose() = subscribers.Remove(handler) |> ignore }

        member this.Register(nodeId: string, _element: obj) : unit =
            // Bare Register with no fixture creates a baseline entry
            // (opaque-black text on the implicit white canvas) so calls
            // from the renderer's mount hook don't crash on the in-memory
            // observer. Test authors normally call RegisterFixture with a
            // populated input.
            if not (ObserverWitness.isRegistered state nodeId) then
                this.RegisterFixture(
                    nodeId,
                    { Input = Flags.StyleInput.baseline
                      Parent = None }
                )

        member _.Unregister(nodeId: string) : unit =
            state <- ObserverWitness.unregister nodeId state

module InMemoryStyleObserver =
    /// Construct with the v1 default options.
    let create () : InMemoryStyleObserver =
        InMemoryStyleObserver(StyleObserverOptions.defaults)

    /// Construct with host-tunable options.
    let createWith (options: StyleObserverOptions) : InMemoryStyleObserver = InMemoryStyleObserver(options)

    /// Construct with a wired `ThemeManifest` — the per-node
    /// manifest-aware (Phase 146) flags are appended to each observation.
    let createWithManifest (options: StyleObserverOptions) (manifest: ThemeManifest) : InMemoryStyleObserver =
        InMemoryStyleObserver(options, Some manifest)
