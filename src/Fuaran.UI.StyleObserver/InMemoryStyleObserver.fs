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
// **Tree walk via parent pointers.** Browser-side `ObserveTree`
// queries the DOM; this implementation walks an explicit
// `NodeId -> children` map built from the fixture's parent
// declarations (BFS-ordered, same shape as `InMemoryLayoutObserver`).
//
// **Subscribe semantics.** `RegisterFixture` always emits an initial
// observation; `Update` honours `EmitOnFlagChangeOnly`. Debounce
// timing doesn't enter the in-memory path — the change-detection
// contract is what the cost-model test exercises.

open System
open System.Collections.Generic
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
    let registry = Dictionary<string, StyleFixture>()
    let lastFlagSet = Dictionary<string, StyleFlag list>()
    let subscribers = ResizeArray<string * StyleObservation -> unit>()

    let toObservation (nodeId: string) (fixture: StyleFixture) : StyleObservation =
        let baseObs = Flags.toObservation options nodeId fixture.Input

        match manifest with
        | Some m ->
            { baseObs with
                Flags = baseObs.Flags @ ManifestFlags.perNodeFlags m baseObs }
        | None -> baseObs

    let emit (nodeId: string) (observation: StyleObservation) =
        for subscriber in subscribers do
            try
                subscriber (nodeId, observation)
            with ex ->
                // A subscriber throwing must not poison sibling
                // subscribers. In-memory path keeps it silent (tests
                // assert directly).
                ignore ex

    /// Phase 144 constructor — no manifest, manifest-free flags only.
    new(options: StyleObserverOptions) = InMemoryStyleObserver(options, None)

    /// Register or replace a fixture for `nodeId`. Fires an initial
    /// emission for the registered node regardless of
    /// `EmitOnFlagChangeOnly` (initial-emission rule).
    member _.RegisterFixture(nodeId: string, fixture: StyleFixture) : unit =
        registry[nodeId] <- fixture
        let observation = toObservation nodeId fixture
        lastFlagSet[nodeId] <- observation.Flags
        emit nodeId observation

    /// Replace the existing fixture's input. Honours
    /// `EmitOnFlagChangeOnly` — fires the subscriber callback only
    /// when the derived flag set differs from the previous emission
    /// (or always, when the option is false). No-op if `nodeId` isn't
    /// registered.
    member _.Update(nodeId: string, input: Flags.StyleInput) : unit =
        match registry.TryGetValue(nodeId) with
        | false, _ -> ()
        | true, existing ->
            let next = { existing with Input = input }
            registry[nodeId] <- next
            let observation = toObservation nodeId next

            let previousFlags =
                match lastFlagSet.TryGetValue(nodeId) with
                | true, flags -> flags
                | false, _ -> []

            lastFlagSet[nodeId] <- observation.Flags

            let shouldEmit =
                if options.EmitOnFlagChangeOnly then
                    observation.Flags <> previousFlags
                else
                    true

            if shouldEmit then
                emit nodeId observation

    interface IStyleObserver with
        member _.Observe(nodeId: string) : StyleObservation option =
            match registry.TryGetValue(nodeId) with
            | true, fixture -> Some(toObservation nodeId fixture)
            | false, _ -> None

        member _.ObserveTree(rootNodeId: string) : StyleObservation list =
            // Walk children via the fixture's parent pointers. BFS-shaped
            // to keep the result deterministic by tree level then by
            // NodeId insertion order (same shape as InMemoryLayoutObserver).
            match registry.TryGetValue(rootNodeId) with
            | false, _ -> []
            | true, _ ->
                let children =
                    registry
                    |> Seq.choose (fun kvp ->
                        match kvp.Value.Parent with
                        | Some p -> Some(p, kvp.Key)
                        | None -> None)
                    |> Seq.groupBy fst
                    |> Seq.map (fun (parent, pairs) -> parent, pairs |> Seq.map snd |> Seq.toList)
                    |> Map.ofSeq

                let rec walk (acc: StyleObservation list) (queue: string list) : StyleObservation list =
                    match queue with
                    | [] -> List.rev acc
                    | nodeId :: rest ->
                        let observation =
                            match registry.TryGetValue(nodeId) with
                            | true, fixture -> [ toObservation nodeId fixture ]
                            | false, _ -> []

                        let kids = Map.tryFind nodeId children |> Option.defaultValue []
                        walk (List.rev observation @ acc) (rest @ kids)

                walk [] [ rootNodeId ]

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
            if not (registry.ContainsKey(nodeId)) then
                this.RegisterFixture(
                    nodeId,
                    { Input = Flags.StyleInput.baseline
                      Parent = None }
                )

        member _.Unregister(nodeId: string) : unit =
            registry.Remove(nodeId) |> ignore
            lastFlagSet.Remove(nodeId) |> ignore

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
