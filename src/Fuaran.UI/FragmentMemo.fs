module Fuaran.UI.FragmentMemo

open System.Collections.Generic
open Fuaran.UI.Types

// ============================================================================
//  FragmentMemo — the generic, Fable-clean memo core (Phase 183).
//
//  The two reusable, dependency-free building blocks of the incremental
//  re-derivation engine:
//
//   1. `isCacheable` — the EFFECT-CLASS CACHE GATE (invariant 3). Only a pure +
//      deterministic effect class is memoisable; an effecting / clock / random /
//      network-sourced result must be re-derived every time, never served stale
//      from a cache. This is the admission decision the wiring engine consults
//      before touching the cache.
//
//   2. `BoundedLru` — a size-bounded least-recently-used cache over string keys,
//      with hit/miss counters. The engine stores substituted trees here; the
//      bound keeps a long authoring session from growing the cache without
//      limit.
//
//  Both live here (in `Fuaran.UI`, `FSharp.Core` + `Fuaran.UI.Types` only) so
//  they are Fable-clean and reusable — the same gate + bound will lift onto the
//  generic `Fuaran.Core.Function` contract when Phase 181's rule-of-three gate
//  trips. The CanonicalJson-keyed wiring + the `FragmentApply` call + the
//  telemetry emission live one tier up (`Fuaran.UI.Fragment.Memo`), which can
//  reference the renderer + op-stream + telemetry packages this one cannot.
//
//  THREADING: `BoundedLru` is single-threaded by contract (no `lock` —
//  `lock` is not Fable-portable). The browser authoring loop is single-
//  threaded, so that is the default. A server host sharing one cache across
//  threads serialises access externally.
// ============================================================================

/// The cache-admission gate (invariant 3): `true` only when the fragment's
/// effect class is pure AND deterministic — the single shape whose output is a
/// total function of its inputs and is therefore safe to memoise. Equivalent to
/// `e = EffectClass.pureDeterministic`, written as an explicit predicate so the
/// gate reads at the call site.
let isCacheable (e: EffectClass) : bool =
    e.HostEffect = HostEffect.Pure
    && e.Determinism = DeterminismSource.Deterministic

/// One cache slot: the value plus a monotonic access stamp for recency.
type private LruEntry<'V> =
    { mutable Value: 'V
      mutable Stamp: int64 }

/// A size-bounded LRU cache over string keys. Eviction is least-recently-used
/// once `Count` would exceed `Capacity`. Tracks cumulative hit/miss counts for
/// observability. Single-threaded by contract (Fable-safe: `Dictionary`, no
/// `lock` / `LinkedList`).
///
/// Recency is an O(1) per-access stamp bump (a monotonic counter), NOT the old
/// O(n)-per-access `ResizeArray.IndexOf` + `RemoveAt`; only eviction pays an
/// O(n) min-stamp scan, and only on the rare insert-when-full. Eviction order is
/// identical to the prior list-based LRU — strictly-monotonic stamps never tie,
/// so the min-stamp entry is exactly the one the list would have held at index 0.
///
/// `capacity` is clamped to at least 1 — a zero/negative bound would evict every
/// entry immediately, defeating the cache.
type BoundedLru<'V>(capacity: int) =
    let capacity = max 1 capacity
    let store = Dictionary<string, LruEntry<'V>>()
    let mutable clock = 0L
    let mutable hits = 0
    let mutable misses = 0

    let nextStamp () =
        clock <- clock + 1L
        clock

    /// The configured (clamped) capacity bound.
    member _.Capacity = capacity

    /// The number of entries currently held.
    member _.Count = store.Count

    /// Cumulative cache hits since construction (or the last `Clear`).
    member _.Hits = hits

    /// Cumulative cache misses since construction (or the last `Clear`).
    member _.Misses = misses

    /// Look a key up. A hit promotes the key to most-recently-used and bumps the
    /// hit counter; a miss bumps the miss counter. Returns `None` on miss.
    member _.TryGet(key: string) : 'V option =
        match store.TryGetValue key with
        | true, entry ->
            hits <- hits + 1
            entry.Stamp <- nextStamp () // O(1) recency bump
            Some entry.Value
        | _ ->
            misses <- misses + 1
            None

    /// Insert or update a key. Inserting a NEW key when the cache is full first
    /// evicts the least-recently-used entry. Updating an existing key never
    /// evicts. Either way the key becomes most-recently-used.
    member _.Set(key: string, value: 'V) : unit =
        match store.TryGetValue key with
        | true, entry ->
            // Update existing — never evicts; refresh value + recency.
            entry.Value <- value
            entry.Stamp <- nextStamp ()
        | _ ->
            // New key: evict the least-recently-used (min stamp) when at capacity.
            // O(n) scan, but only here — not on every access like the old touch.
            if store.Count >= capacity then
                let mutable lruKey = ""
                let mutable lruStamp = System.Int64.MaxValue
                let mutable found = false

                for kv in store do
                    if kv.Value.Stamp < lruStamp then
                        lruStamp <- kv.Value.Stamp
                        lruKey <- kv.Key
                        found <- true

                if found then
                    store.Remove lruKey |> ignore

            store[key] <- { Value = value; Stamp = nextStamp () }

    /// `true` when the key is currently cached (does not affect recency or the
    /// hit/miss counters).
    member _.ContainsKey(key: string) : bool = store.ContainsKey key

    /// Drop every entry and reset the hit/miss counters.
    member _.Clear() : unit =
        store.Clear()
        clock <- 0L
        hits <- 0
        misses <- 0
