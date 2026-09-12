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
//   - A KEY THAT COSTS LESS THAN THE WORK IT SAVES (Phase 210). The structural
//     key's body half is memoised per `ParamFragment` reference, so a probe
//     hashes only the ref id + slot args. Before this, a cache HIT paid a
//     whole-tree canonical encode + SHA-256 just to build the key that told it
//     the tree was reusable — for a small fragment, more than the substitution
//     it was avoiding — and a value-only `Reapply` re-hashed the entire tree to
//     establish that nothing structural had changed.
//
//   - THE EFFECT GATE (invariant 3). `FragmentMemo.isCacheable` gates admission:
//     a pure-deterministic fragment is always cached; an effecting / clock /
//     random / network fragment BYPASSES the cache entirely (re-derived every
//     call, never stored), so a host-effecting result is never served stale.
//
//   - THE REPRESENTABILITY GATE (Phase 1525), beside the effect gate and on the
//     same footing. The key is a hash of a CANONICAL ENCODING, and that encoding
//     stands a sentinel in for what it cannot represent (`"<closure>"` for a
//     function-typed payload, `"<opaque>"` for an unrecognised CLR value) — so
//     two fragments differing only there key identically, and the store serves
//     one where the other was asked for, closures included. Such an application
//     is never keyed and never stored. Whether it is DERIVED-AND-BYPASSED or
//     REFUSED is the caller's declaration (`StoreReach`): a process-local memo is
//     a private optimisation and quietly declines to optimise; a shared or
//     persisted store is a contract with another session or machine, and its
//     owner is told rather than left with an entry that outlives the process
//     that mis-keyed it.
//
//   - OBSERVABILITY. Every call emits one `CacheStatTelemetry` (hit / miss /
//     incremental / bypass + the cache's size against its bound) to the
//     configured `IFuaranTelemetrySink`.
//
//  THREADING: one `Engine` is single-threaded by contract (the `BoundedLru` it
//  wraps is). The browser authoring loop is single-threaded; a server host that
//  shares an engine across threads serialises access externally.
// ============================================================================

/// How far a fragment store's entries travel beyond the derivation that put
/// them there (Phase 1525). This is the discrimination the unrepresentable-value
/// admission rule turns on, and it is a TYPE rather than a convention because
/// getting it wrong is silent: a store that outlives or escapes its process
/// serves the wrong fragment, and nothing about the wrong fragment says so.
///
/// It is DECLARED by whoever supplies the store and cannot be inferred:
/// `IFragmentStore` is an interface any host may implement, and an adapter over
/// a network cache looks exactly like one over a local dictionary.
[<RequireQualifiedAccess>]
type StoreReach =
    /// The engine owns the store, its entries never leave this process, and it
    /// dies with the engine. An application the key machinery cannot
    /// content-address still DERIVES — it bypasses the store, exactly as an
    /// effecting fragment does — so the optimisation is declined rather than the
    /// work refused. It is NOT served from the store either: a false hit is
    /// possible within one process too (two fragments whose canonical encodings
    /// collide are two fragments, wherever they live), and this is the reason
    /// `ProcessLocal` bypasses rather than stores.
    | ProcessLocal
    /// The store is shared with another engine, another session or another
    /// machine, or persisted for a later one. An application the key machinery
    /// cannot content-address is REFUSED here, not silently un-memoised: the
    /// caller chose to share or persist, so the caller is the party that needs
    /// to learn its fragment cannot travel — and a wrongly-keyed entry written
    /// to such a store outlives the process that wrote it.
    | SharedOrPersisted

/// A recorded derivation — the application result plus the keys/args the engine
/// needs to re-derive incrementally against a later arg-set. `ValueArgs` is
/// retained for the binding diff.
type Derivation<'Msg> =
    {
        Result: FragmentApplication<'Msg>
        /// The content address of (body, refId, slot args) — or `None` when the
        /// application could not be content-addressed at all (Phase 1525: its
        /// canonical encoding carried an unrepresentable-value sentinel, so a
        /// `ProcessLocal` engine derived it and bypassed the store). `None` never
        /// equals a key, so `Reapply` declines to reuse a tree it cannot prove is
        /// the same tree — which is the behaviour an option forces and a magic
        /// string would have left to a comment.
        StructuralKey: string option
        ValueArgs: Map<string, obj>
    }

/// Defaults for the engine's key machinery (Phase 210).
module KeyMemoDefaults =

    /// How many distinct `ParamFragment` instances one engine memoises body
    /// digests for. This bounds a DIFFERENT population from the substituted-tree
    /// store's capacity, and a much smaller one: the store is keyed by content,
    /// so it grows once per distinct (fragment, ref site, slot-arg) application,
    /// whereas this grows once per distinct fragment DECLARATION in play on the
    /// surface. Bounded (rather than unbounded-because-small) because the memo
    /// holds a strong reference to every fragment in it, and a bound is what
    /// makes that safe to say. A surface with more live declarations than this
    /// degrades to the pre-210 cost on the overflow — never to a wrong key.
    [<Literal>]
    let bodyDigestCapacity = 64

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
///
/// `reach` declares how far the store's entries travel (Phase 1525) and decides
/// what happens to an application whose canonical encoding is incomplete — see
/// `StoreReach`. The shipped `Engine(capacity, sink)` constructor builds its own
/// process-local LRU and therefore declares `ProcessLocal` on its own authority.
/// The injected-store constructor `Engine(store, sink)` declares
/// `SharedOrPersisted`, because injection exists to make the store portable and
/// a store of unknown provenance must be assumed to be one; a host injecting a
/// genuinely process-local store says so with `Engine(store, reach, sink)`.
type Engine<'Msg>(store: IFragmentStore<'Msg>, reach: StoreReach, sink: IFuaranTelemetrySink, ?cacheName: string) =
    let cacheName = defaultArg cacheName "fragment.apply"

    // Phase 210 — the IMMUTABLE half of the structural key, memoised by fragment
    // REFERENCE IDENTITY. `pf.Name` + `pf.Body` cannot change for a given
    // `ParamFragment` instance, so their digest is constant across every
    // application of it; without this memo a structural cache HIT still paid a
    // whole-tree canonical encode + SHA-256 merely to build the key that
    // discovered the tree was reusable, and a value-only `Reapply` re-hashed the
    // entire tree to discover that nothing structural had changed.
    //
    // Owned by the ENGINE, deliberately, rather than hidden inside `FragmentKey`:
    // this type already declares a single-threaded contract (see the header) and
    // a bounded lifetime, so the memo inherits both. A module-level static in the
    // key module would be shared across every thread and every engine in the
    // process and would retain every fragment body it ever saw.
    //
    // A fragment instance the memo has not seen (or has evicted) simply costs
    // what it cost before — the digest is recomputed, never guessed.
    //
    // Phase 1525 — the memoised value is the ADMISSION RESULT, not a bare
    // digest. A body whose canonical encoding carries an unrepresentable-value
    // sentinel cannot key anything, and that verdict is as constant per fragment
    // instance as the digest is, so it is memoised beside it rather than
    // recomputed on every probe.
    let bodyDigests =
        BoundedRefMemo<ParamFragment<'Msg>, Result<string, FragmentKey.Unrepresentable list>>(
            KeyMemoDefaults.bodyDigestCapacity
        )

    /// The structural key for one application, with the body half served from
    /// `bodyDigests` and only the ref id + slot args hashed per probe. Identical
    /// in value to `FragmentKey.structural` — the memo changes what it costs, not
    /// what it says.
    ///
    /// `Error` means the application cannot be content-addressed at all: the
    /// canonical encoding of the body or of a slot argument carried a sentinel
    /// standing in for a value it could not represent, so this key would not
    /// distinguish this application from a different one (Phase 1525).
    let structuralKey
        (pf: ParamFragment<'Msg>)
        (refId: string)
        (slotArgs: Map<string, Node<'Msg>>)
        : Result<string, FragmentKey.Unrepresentable list> =
        bodyDigests.GetOrAdd(pf, FragmentKey.tryBodyDigest)
        |> Result.bind (fun body -> FragmentKey.tryStructuralOf body refId slotArgs)

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
    /// Declares `ProcessLocal` on its own authority: the engine builds this store
    /// itself, holds the only reference to it, and discards it with itself.
    new(capacity: int, sink: IFuaranTelemetrySink, ?cacheName: string) =
        let s = InProcessStore<'Msg>(capacity) :> IFragmentStore<'Msg>

        match cacheName with
        | Some n -> Engine<'Msg>(s, StoreReach.ProcessLocal, sink, n)
        | None -> Engine<'Msg>(s, StoreReach.ProcessLocal, sink)

    /// The injected-store constructor (Phase 360), which reads as
    /// `SharedOrPersisted` (Phase 1525). Injection exists so a store CAN outlive
    /// or escape the process — that is what `MemoCacheStore.Snapshot` is for — so a
    /// store whose provenance the engine cannot see is assumed to be one of
    /// those. The asymmetry is deliberate: assuming shared costs a declined
    /// optimisation and says so out loud, while assuming local costs a wrong
    /// fragment served silently from someone else's cache.
    ///
    /// A host whose injected store is genuinely process-local says so with
    /// `Engine(store, StoreReach.ProcessLocal, sink)`.
    new(store: IFragmentStore<'Msg>, sink: IFuaranTelemetrySink, ?cacheName: string) =
        match cacheName with
        | Some n -> Engine<'Msg>(store, StoreReach.SharedOrPersisted, sink, n)
        | None -> Engine<'Msg>(store, StoreReach.SharedOrPersisted, sink)

    /// How far this engine's store reaches, and therefore what it does with an
    /// application it cannot content-address (Phase 1525).
    member _.Reach = reach

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

    /// The body-digest memo (Phase 210) — exposed for observability on the same
    /// footing as `Cache` (`Hits` / `Misses` / `Count` / `Capacity`). A hit here
    /// is one whole-subtree canonical encode + SHA-256 not paid. Its hit rate is
    /// also the regression signal: a change that reverted the engine to hashing
    /// the whole body per probe would show up as this counter going flat.
    /// Mutating it directly is not part of the contract.
    member _.KeyMemo = bodyDigests

    /// Apply with whole-application memoisation, served from the caller-supplied
    /// content-addressed store (Phase 360). A pure-deterministic fragment's
    /// substituted tree is content-keyed (`FragmentKey.structural`) + cached in
    /// the store (HIT on an identical application, MISS + store otherwise). An
    /// effecting fragment BYPASSES the store — re-derived every call, never stored
    /// (the effect gate, Fork 3: `FragmentMemo.isCacheable`, equal to
    /// `Fuaran.Core.Memo.isMemoisable`). Returns the derivation, or the bare
    /// apply's error verbatim (validation / totality failures are NOT cached).
    ///
    /// A SECOND admission gate sits beside the effect gate (Phase 1525), on the
    /// same footing and for the same class of reason. Where the canonical
    /// encoding of the body or of a slot argument carries an
    /// unrepresentable-value sentinel, the key does not distinguish this
    /// application from a different one, so serving from the store would serve
    /// the WRONG fragment — the cached subtree carries the first application's
    /// closures. What happens then is `StoreReach`'s to decide: `ProcessLocal`
    /// derives and bypasses the store, `SharedOrPersisted` refuses with a message
    /// naming what was unrepresentable. Neither ever stores under such a key.
    member _.Apply
        (pf: ParamFragment<'Msg>, refId: string, valueArgs: Map<string, obj>, slotArgs: Map<string, Node<'Msg>>)
        : Result<Derivation<'Msg>, string> =
        // Derive without consulting or populating the store, recording the
        // bypass — the shape the effect gate and the un-keyable case share.
        let deriveBypassing (sk: string option) : Result<Derivation<'Msg>, string> =
            match FragmentApply.apply pf refId valueArgs slotArgs with
            | Ok app ->
                store.NoteBypass()
                emit CacheOutcome.Bypass

                Ok
                    { Result = app
                      StructuralKey = sk
                      ValueArgs = valueArgs }
            | Error e -> Error e

        // Serve / store under a real content address — the shipped path.
        let keyed (sk: string) : Result<Derivation<'Msg>, string> =
            if not (isCacheable pf.Effect) then
                // Effecting / non-deterministic — never consult or populate the store.
                deriveBypassing (Some sk)
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
                          StructuralKey = Some sk
                          ValueArgs = valueArgs }
                | None ->
                    match FragmentApply.apply pf refId valueArgs slotArgs with
                    | Ok app ->
                        store.Set(sk, app.Tree)
                        emit CacheOutcome.Miss

                        Ok
                            { Result = app
                              StructuralKey = Some sk
                              ValueArgs = valueArgs }
                    | Error e -> Error e

        match structuralKey pf refId slotArgs with
        | Ok sk -> keyed sk
        | Error unrepresentable ->
            // Un-keyable. The store is neither consulted nor populated under
            // EITHER reach; what differs is whether the caller is told.
            match reach with
            | StoreReach.ProcessLocal -> deriveBypassing None
            | StoreReach.SharedOrPersisted -> Error(FragmentKey.describeUnrepresentable unrepresentable)

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
            // Phase 210 — the body digest is served from the engine's memo, so a
            // value-only re-derive hashes only the (small) slot-arg portion to
            // establish that the structural key is unchanged.
            // An un-keyable application (Phase 1525) yields `Error` here and
            // `None` on `prev`, and neither is ever equal to a content address —
            // so the incremental path is taken only where a real key proves the
            // tree is the same tree, and every other case falls through to a full
            // `Apply`, which applies the reach rule.
            let sk =
                match structuralKey pf refId newSlotArgs with
                | Ok k -> Some k
                | Error _ -> None

            if sk.IsSome && sk = prev.StructuralKey then
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
