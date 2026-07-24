namespace Fuaran.UI.Memo

open System
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.FragmentMemo
open Fuaran.UI.Renderer
open Fuaran.UI.Telemetry.Abstractions

// ============================================================================
//  IncrementalApply — the memoised incremental re-derivation engine (Phase 183).
//
//  A transparent cache + incremental layer over the shipped Phase 180
//  `FragmentApply.apply`. It changes no wire shape and no apply semantics — an
//  application memoised here produces the SAME `FragmentApplication` the bare
//  apply would. What it adds is cheapness:
//
//   - WHOLE-APPLICATION MEMO (`Apply`). The substituted tree is content-keyed
//     (`FragmentKey.structural`) and stored in a bounded LRU. An identical
//     application is a cache HIT — no re-substitution. This is also the path
//     op-stream replay resolves through: replaying a recorded application hits
//     the cache and yields a byte-identical tree (FGP 5 — the both-sinks
//     emission upstream is unaffected; the cache sits purely on the read/derive
//     side).
//
//   - INCREMENTAL RE-DERIVATION (`Reapply`). Given a previous derivation and a
//     new arg-set, the engine exploits the structural fact that the substituted
//     tree depends only on (body, refId, slot args): a VALUE-parameter change
//     leaves the tree untouched, so the prior tree object is reused (reference-
//     identical) and only the hole-addressed (`<refId>.<holeName>`) bindings
//     whose value changed are recomputed. Disjoint value changes don't
//     invalidate each other — an unchanged hole keeps its prior binding object.
//     A SLOT (structural) change falls back to a full `Apply` (the tree genuinely
//     differs). This is the "edit a decision → the chart re-derives" property
//     made concrete and cheap.
//
//   - THE EFFECT GATE (invariant 3). `FragmentMemo.isCacheable` gates admission:
//     a pure-deterministic fragment is always cached; an effecting / clock /
//     random / network fragment BYPASSES the cache entirely (re-derived every
//     call, never stored), so a host-effecting result is never served stale.
//
//   - OBSERVABILITY. Every call emits one `CacheStatTelemetry` (hit / miss /
//     incremental / bypass + the cache's size against its bound) to the
//     configured `IFuaranTelemetrySink`.
//
//  THREADING: one `Engine` is single-threaded by contract (the `BoundedLru` it
//  wraps is). The browser authoring loop is single-threaded; a server host that
//  shares an engine across threads serialises access externally.
// ============================================================================

/// A recorded derivation — the application result plus the keys/args the engine
/// needs to re-derive incrementally against a later arg-set. `StructuralKey`
/// fingerprints (body, refId, slot args); `ValueArgs` is retained for the
/// binding diff.
type Derivation<'Msg> =
    { Result: FragmentApplication<'Msg>
      StructuralKey: string
      ValueArgs: Map<string, obj> }

/// The memoised incremental re-derivation engine. One instance per logical
/// artifact surface (so its cache + telemetry name scope to that surface).
///
/// The substituted-tree cache is a caller-supplied `IFragmentStore<'Msg>`
/// (Phase 360 — the `Fuaran.Core.Function.applyMemo` cache model in the UI tier):
/// the SAME content-hash key (`FragmentKey.structural`) the Phase-183 engine
/// already used, but the store is INJECTED so it can be portable across a
/// session / machine boundary. The shipped `Engine(capacity, sink)` constructor
/// still yields the process-local bounded LRU (`InProcessStore`), preserving the
/// exact pre-360 behaviour; a host after portability injects a
/// `MemoCacheStore` (or any `IFragmentStore`) via `Engine(store, sink)`.
///
/// `sink` receives one `CacheStatTelemetry` per call; `cacheName` (default
/// `"fragment.apply"`) tags those records. The `Capacity` surfaced on a
/// telemetry record is the store's `Count` for an unbounded portable store (it
/// carries no fixed bound).
type Engine<'Msg>(store: IFragmentStore<'Msg>, sink: IFuaranTelemetrySink, ?cacheName: string) =
    let cacheName = defaultArg cacheName "fragment.apply"

    let emit (outcome: CacheOutcome) : unit =
        sink.RecordCacheStat
            { CacheName = cacheName
              Outcome = outcome
              Size = store.Count
              Capacity = store.Capacity
              Timestamp = DateTimeOffset.UtcNow }

    /// The host-seeded value bindings for an application — `<refId>.<holeName>`
    /// keyed, exactly as `FragmentApply.apply` produces them.
    let bindings (refId: string) (valueArgs: Map<string, obj>) : Map<string, obj> =
        valueArgs
        |> Map.toList
        |> List.map (fun (n, v) -> refId + "." + n, v)
        |> Map.ofList

    /// Recompute only the CHANGED hole-address bindings against `prevBindings`,
    /// reusing the prior boxed value object (reference-identical) for every
    /// unchanged hole. Returns the new binding map + the count of addresses
    /// actually recomputed (changed or newly-added). A dropped hole simply does
    /// not appear in `newValueArgs`, so it falls out.
    let incrementalBindings
        (refId: string)
        (prevBindings: Map<string, obj>)
        (newValueArgs: Map<string, obj>)
        : Map<string, obj> * int =
        let mutable recomputed = 0

        let m =
            newValueArgs
            |> Map.toList
            |> List.map (fun (holeName, v) ->
                let addr = refId + "." + holeName

                match Map.tryFind addr prevBindings with
                | Some old when Object.Equals(old, v) -> addr, old // unchanged — reuse, not recomputed
                | _ ->
                    recomputed <- recomputed + 1
                    addr, v)
            |> Map.ofList

        m, recomputed

    /// The shipped constructor — a process-local bounded LRU store (`capacity`
    /// bounds it), preserving the exact pre-360 behaviour. Additive (GP 11).
    new(capacity: int, sink: IFuaranTelemetrySink, ?cacheName: string) =
        let s = InProcessStore<'Msg>(capacity) :> IFragmentStore<'Msg>

        match cacheName with
        | Some n -> Engine<'Msg>(s, sink, n)
        | None -> Engine<'Msg>(s, sink)

    /// The configured cache name (one engine = one named cache surface).
    member _.CacheName = cacheName

    /// The caller-supplied content-addressed store (Phase 360) — exposed for
    /// observability (Count / Hits / Misses / Bypasses). Mutating it directly is
    /// not part of the contract. For the default `InProcessStore` this wraps the
    /// pre-360 `BoundedLru`; a portable engine holds a `MemoCacheStore`.
    member _.Store = store

    /// The underlying store's observable size + counters, in the shape the
    /// pre-360 `BoundedLru` exposed (`Count` / `Capacity` / `Hits` / `Misses`) so
    /// existing observability reads keep working. `Capacity` is the store's live
    /// `Count` for an unbounded portable store.
    member _.Cache = store

    /// Apply with whole-application memoisation, served from the caller-supplied
    /// content-addressed store (Phase 360). A pure-deterministic fragment's
    /// substituted tree is content-keyed (`FragmentKey.structural`) + cached in
    /// the store (HIT on an identical application, MISS + store otherwise). An
    /// effecting fragment BYPASSES the store — re-derived every call, never stored
    /// (the effect gate, Fork 3: `FragmentMemo.isCacheable`, equal to
    /// `Fuaran.Core.Memo.isMemoisable`). Returns the derivation, or the bare
    /// apply's error verbatim (validation / totality failures are NOT cached).
    member _.Apply
        (pf: ParamFragment<'Msg>, refId: string, valueArgs: Map<string, obj>, slotArgs: Map<string, Node<'Msg>>)
        : Result<Derivation<'Msg>, string> =
        let sk = FragmentKey.structural pf refId slotArgs

        if not (isCacheable pf.Effect) then
            // Effecting / non-deterministic — never consult or populate the store.
            match FragmentApply.apply pf refId valueArgs slotArgs with
            | Ok app ->
                store.NoteBypass()
                emit CacheOutcome.Bypass

                Ok
                    { Result = app
                      StructuralKey = sk
                      ValueArgs = valueArgs }
            | Error e -> Error e
        else
            match store.TryGet sk with
            | Some tree ->
                // Structural HIT — reuse the cached tree; the value bindings are
                // a pure function of (refId, valueArgs), rebuilt cheaply.
                emit CacheOutcome.Hit

                Ok
                    { Result =
                        { Tree = tree
                          ValueBindings = bindings refId valueArgs }
                      StructuralKey = sk
                      ValueArgs = valueArgs }
            | None ->
                match FragmentApply.apply pf refId valueArgs slotArgs with
                | Ok app ->
                    store.Set(sk, app.Tree)
                    emit CacheOutcome.Miss

                    Ok
                        { Result = app
                          StructuralKey = sk
                          ValueArgs = valueArgs }
                | Error e -> Error e

    /// Incrementally re-derive against a previous derivation. When only value
    /// parameters changed (the structural key is unchanged), the prior tree
    /// object is reused and ONLY the changed hole-address bindings are
    /// recomputed (emits `Incremental recomputed`). When a slot argument changed
    /// (the structural key differs), falls back to a full `Apply`. An effecting
    /// fragment always full-re-derives via `Apply` (bypass).
    member this.Reapply
        (
            prev: Derivation<'Msg>,
            pf: ParamFragment<'Msg>,
            refId: string,
            newValueArgs: Map<string, obj>,
            newSlotArgs: Map<string, Node<'Msg>>
        ) : Result<Derivation<'Msg>, string> =
        if not (isCacheable pf.Effect) then
            this.Apply(pf, refId, newValueArgs, newSlotArgs)
        else
            let sk = FragmentKey.structural pf refId newSlotArgs

            if sk = prev.StructuralKey then
                // Structural tree unchanged → reuse it; recompute only the
                // changed hole-address bindings.
                let newBindings, recomputed =
                    incrementalBindings refId prev.Result.ValueBindings newValueArgs

                emit (CacheOutcome.Incremental recomputed)

                Ok
                    { Result =
                        { prev.Result with
                            ValueBindings = newBindings }
                      StructuralKey = sk
                      ValueArgs = newValueArgs }
            else
                // Slot / structural change — full re-derive (and refresh the cache).
                this.Apply(pf, refId, newValueArgs, newSlotArgs)
