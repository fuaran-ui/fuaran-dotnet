namespace Fuaran.UI.Memo

open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.FragmentMemo
open Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  PortableStore — the caller-supplied, content-addressed fragment store
//  (Phase 360). Lifts the shipped Phase-183 process-local `BoundedLru` onto
//  `Fuaran.Core.Function`'s `applyMemo` cache model: the SAME content-hash key
//  the LRU already uses (`FragmentKey.structural`), but the backing store is
//  INJECTED so it can be shared across a session / machine boundary rather than
//  living process-locally.
//
//  Three pieces:
//
//   1. `IFragmentStore<'Msg>` — the portable-cache abstraction. A store maps a
//      content-hash key to a substituted `Node<'Msg>` subtree, with hit / miss /
//      bypass counters (mirroring `Fuaran.Core.MemoCache`). Any host can supply
//      one — a bounded in-process LRU, a `MemoCache` lifted from disk, a shared
//      network cache — and the engine routes every memoisable application through
//      it. The key is machine-independent (canonical-JSON content hash), so a
//      store populated on machine A is a hit on machine B (portability).
//
//   2. `InProcessStore<'Msg>` — the DEFAULT store, a thin adapter over the
//      Phase-183 `BoundedLru<Node<'Msg>>`. Injecting it preserves the exact
//      pre-360 behaviour (bounded, single-process, LRU eviction). Additive
//      (GP 11): the shipped `Engine` constructor still yields this.
//
//   3. `MemoCacheStore<'Msg>` — the PORTABLE store, backed BY VALUE by
//      `Fuaran.Core.MemoCache<Node<'Msg>>` (the `applyMemo` cache type). It is
//      threaded through a single mutable cell holding the immutable cache record,
//      exactly as `Fuaran.Core.Function.applyMemo` threads its `MemoCache`; the
//      cache is serialisable (its `Entries` are `(content-hash, subtree)` pairs),
//      so `.Snapshot` on machine A can seed a `fromCache` store on machine B —
//      the same result tree served without re-derivation, a hit "all the way
//      down". This is the concrete adoption of `fuaran-core#49`.
//
//  THE EFFECT GATE rides on the ENGINE (`FragmentMemo.isCacheable`, the two-axis
//  pure-&-deterministic predicate — identical to `Fuaran.Core.Memo.isMemoisable`):
//  a non-memoisable fragment never touches the store (never served, never
//  stored), so an impure / non-deterministic result is never cached (Fork 3).
//  A store's `Bypasses` counter records those the engine routed AROUND it.
// ============================================================================

/// A caller-supplied, content-addressed store for substituted fragment subtrees
/// (Phase 360 — the `applyMemo` cache model in the UI tier). Keyed by the
/// canonical-JSON content hash (`FragmentKey.structural`), which is
/// machine-independent, so a store populated in one session is a cache hit in
/// another — the portability win over the process-local `BoundedLru`. The
/// counters make the "unchanged subtree served, not re-derived" property
/// observable (mirroring `Fuaran.Core.MemoCache.{Hits,Misses,Bypasses}`);
/// `NoteBypass` records an application the engine's effect gate routed AROUND
/// the store (never served, never stored — the soundness guard).
type IFragmentStore<'Msg> =
    /// Look a content-hash key up. A hit returns the cached subtree and bumps the
    /// store's hit counter; a miss returns `None` and bumps the miss counter.
    abstract member TryGet: key: string -> Node<'Msg> option
    /// Store (or refresh) a subtree under its content-hash key.
    abstract member Set: key: string * value: Node<'Msg> -> unit
    /// Record that the engine's effect gate bypassed the store for one
    /// application (an impure / non-deterministic fragment, never cached).
    abstract member NoteBypass: unit -> unit
    /// Cumulative store hits.
    abstract member Hits: int
    /// Cumulative store misses.
    abstract member Misses: int
    /// Cumulative bypasses (applications routed around the store by the gate).
    abstract member Bypasses: int
    /// The number of distinct cached subtrees currently held.
    abstract member Count: int
    /// The store's declared bound. A bounded (LRU) store returns its capacity; an
    /// unbounded portable store returns its live `Count` (no fixed bound).
    abstract member Capacity: int

/// The DEFAULT store (Phase 360) — a thin adapter over the Phase-183
/// process-local `BoundedLru<Node<'Msg>>`, preserving the exact pre-360
/// behaviour (bounded capacity, LRU eviction, single process). This is what the
/// shipped `Engine(capacity, sink)` constructor yields, so existing callers see
/// no change; the portability upgrade is opt-in by injecting a different store.
type InProcessStore<'Msg>(capacity: int) =
    let lru = BoundedLru<Node<'Msg>>(capacity)
    let mutable bypasses = 0

    /// The underlying bounded LRU — exposed for observability (Capacity / Count).
    member _.Lru = lru

    interface IFragmentStore<'Msg> with
        member _.TryGet(key: string) : Node<'Msg> option = lru.TryGet key
        member _.Set(key: string, value: Node<'Msg>) : unit = lru.Set(key, value)
        member _.NoteBypass() : unit = bypasses <- bypasses + 1
        member _.Hits = lru.Hits
        member _.Misses = lru.Misses
        member _.Bypasses = bypasses
        member _.Count = lru.Count
        member _.Capacity = lru.Capacity

/// The PORTABLE store (Phase 360) — backed by `Fuaran.Core.MemoCache<Node<'Msg>>`
/// (the `Fuaran.Core.Function.applyMemo` cache type), threaded BY VALUE through a
/// single mutable cell holding the immutable cache record. Because the cache is a
/// pure `Map<content-hash, subtree>` (+ counters), `.Snapshot` can be persisted
/// and lifted onto a fresh `MemoCacheStore.fromCache` on another machine / after a
/// restart — the same subtree served as a hit without re-derivation. This is the
/// content-addressed-application cache of `fuaran-core#49`, unbounded (portability
/// over eviction) — a host that wants a bound wraps `InProcessStore` instead.
type MemoCacheStore<'Msg>(initial: Fuaran.Core.MemoCache<Node<'Msg>>) =
    let mutable cache = initial

    /// An empty portable store.
    new() = MemoCacheStore<'Msg>(Fuaran.Core.Memo.empty)

    /// Seed a portable store from a cache snapshot (the machine-B / post-restart
    /// path — the entries carried over from another session).
    static member FromCache(c: Fuaran.Core.MemoCache<Node<'Msg>>) : MemoCacheStore<'Msg> = MemoCacheStore<'Msg>(c)

    /// The current cache value — the serialisable snapshot to persist / ship to
    /// another machine (its `Entries` are `(content-hash, subtree)` pairs).
    member _.Snapshot: Fuaran.Core.MemoCache<Node<'Msg>> = cache

    interface IFragmentStore<'Msg> with
        member _.TryGet(key: string) : Node<'Msg> option =
            match Map.tryFind key cache.Entries with
            | Some hit ->
                cache <- { cache with Hits = cache.Hits + 1 }
                Some hit
            | None ->
                cache <- { cache with Misses = cache.Misses + 1 }
                None

        member _.Set(key: string, value: Node<'Msg>) : unit =
            cache <-
                { cache with
                    Entries = Map.add key value cache.Entries }

        member _.NoteBypass() : unit =
            cache <-
                { cache with
                    Bypasses = cache.Bypasses + 1 }

        member _.Hits = cache.Hits
        member _.Misses = cache.Misses
        member _.Bypasses = cache.Bypasses
        member _.Count = Map.count cache.Entries
        member _.Capacity = Map.count cache.Entries

/// Helpers for building the default / portable stores and for the effect gate.
module FragmentStore =

    /// The default in-process store over a bounded LRU (the pre-360 behaviour).
    let inProcess<'Msg> (capacity: int) : IFragmentStore<'Msg> =
        InProcessStore<'Msg>(capacity) :> IFragmentStore<'Msg>

    /// An empty portable (`MemoCache`-backed) store.
    let portable<'Msg> () : MemoCacheStore<'Msg> = MemoCacheStore<'Msg>()

    /// A portable store seeded from a cache snapshot — the cross-session /
    /// cross-machine restore path.
    let fromSnapshot<'Msg> (c: Fuaran.Core.MemoCache<Node<'Msg>>) : MemoCacheStore<'Msg> =
        MemoCacheStore<'Msg>.FromCache c

    /// The effect gate (Fork 3) — a fragment is store-eligible only when its
    /// declared effect is pure AND deterministic. Identical to
    /// `Fuaran.Core.Memo.isMemoisable` on the two axes; delegates to the shipped
    /// `FragmentMemo.isCacheable`, the single admission predicate. An impure /
    /// non-deterministic fragment is bypassed — never served, never stored.
    let isStoreEligible (e: EffectClass) : bool = isCacheable e
