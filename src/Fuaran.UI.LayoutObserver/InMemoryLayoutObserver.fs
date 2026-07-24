namespace Fuaran.UI.LayoutObserver

// ─── InMemoryLayoutObserver — .NET-side test + eval substrate ───
//
// The headless counterpart to `BrowserLayoutObserver`.
// Accepts hand-authored `LayoutInput` fixtures keyed by NodeId,
// runs the shared `Flags.derive` logic, surfaces results via the
// same `ILayoutObserver` interface the browser observer satisfies.
//
// **Phase 322 — a domain instance of `Fuaran.Core.Observer`.** The
// registry / change-gated emission / parent-pointer tree walk that
// used to live here is now the generic `Fuaran.Core.Observer`
// engine; this type is a thin domain adapter that supplies the UI
// flag vocabulary (`LayoutFlag`) + metric envelope (`Flags.LayoutInput`)
// + the pure `Flags.derive` derivation, and projects the generic
// `Observation<LayoutInput, LayoutFlag>` back into the UI's richer
// `LayoutObservation` (which reads width / height / viewport
// coordinates off the input). Behaviour is unchanged — the engine is
// the faithful lift of what this file used to do — and the existing
// Expecto suite is the parity test.
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

open Fuaran.Core.Observer

/// One in-memory test fixture entry: the `LayoutInput` to drive
/// derivation, plus an optional parent NodeId to support tree
/// walks.
type LayoutFixture =
    { Input: Flags.LayoutInput
      Parent: string option }

/// In-memory observer. Construct with the desired options; drive
/// `Register` + `Update` from tests; assert via `Observe` /
/// `ObserveTree` / subscriber callbacks. A domain instance of the
/// generic `Fuaran.Core.Observer` seam (Phase 322).
type InMemoryLayoutObserver(options: LayoutObserverOptions) =
    // The generic engine carries the registry, the change-gated
    // emission policy, and the parent-pointer tree walk. The UI flag
    // vocabulary + derivation are supplied here; the emit policy maps
    // straight across (structural flag-set equality, change-only per
    // the UI default).
    let engine =
        InMemoryObserver.createWith
            (Flags.derive options)
            { EmitOnFlagChangeOnly = options.EmitOnFlagChangeOnly
              FlagsEqual = (=) }

    let asObserver = engine :> IObserver<Flags.LayoutInput, LayoutFlag>

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

    /// Register or replace a fixture for `nodeId`. `element` is
    /// ignored — present only to satisfy the `ILayoutObserver`
    /// contract. Fires an initial emission for the registered node
    /// regardless of `EmitOnFlagChangeOnly` (initial-emission rule).
    member this.RegisterFixture(nodeId: string, fixture: LayoutFixture) : unit =
        match fixture.Parent with
        | Some parent -> engine.RegisterNode(nodeId, fixture.Input, parent)
        | None -> engine.RegisterNode(nodeId, fixture.Input)

    /// Replace the existing fixture's input. Honours the
    /// `EmitOnFlagChangeOnly` option — fires the subscriber
    /// callback only when the derived flag set differs from the
    /// previous emission (or always, when the option is false).
    /// No-op if `nodeId` isn't registered.
    member this.Update(nodeId: string, input: Flags.LayoutInput) : unit = engine.Update(nodeId, input)

    interface ILayoutObserver with
        member _.Observe(nodeId: string) : LayoutObservation option =
            asObserver.Observe(nodeId) |> Option.map project

        member _.ObserveTree(rootNodeId: string) : LayoutObservation list =
            asObserver.ObserveTree(rootNodeId) |> List.map project

        member _.Subscribe(handler: string * LayoutObservation -> unit) : System.IDisposable =
            asObserver.Subscribe(fun (nodeId, observation) -> handler (nodeId, project observation))

        member this.Register(nodeId: string, _element: obj) : unit =
            // Default-fixture registration — the test author would
            // normally call RegisterFixture with a populated input.
            // Bare Register with no fixture creates an empty
            // baseline (0x0 element rect) so calls from the
            // renderer's mount hook don't crash on the in-memory
            // observer. Idempotent — only register if absent.
            if (asObserver.Observe nodeId).IsNone then
                let baselineInput = Flags.LayoutInput.empty 0.0 0.0
                this.RegisterFixture(nodeId, { Input = baselineInput; Parent = None })

        member _.Unregister(nodeId: string) : unit = asObserver.Unregister(nodeId)

module InMemoryLayoutObserver =
    /// Construct with the v1 default options.
    let create () : InMemoryLayoutObserver =
        InMemoryLayoutObserver(LayoutObserverOptions.defaults)

    /// Construct with hosting-tunable options.
    let createWith (options: LayoutObserverOptions) : InMemoryLayoutObserver = InMemoryLayoutObserver(options)
