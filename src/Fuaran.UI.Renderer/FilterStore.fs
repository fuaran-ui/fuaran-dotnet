module Fuaran.UI.Renderer.FilterStore

// ============================================================================
//  Reactive backing for Fuaran's app-level Filter channel (Phase 423).
//
//  The twin of `StateStore` (Phase 106), reusing its `StateStoreInstance`
//  machinery verbatim but kept in a DISTINCT `$filters` namespace so filter
//  keys never collide with state keys and the two channels stay conceptually
//  separate (filters keep their typed vocabulary — `ChoiceFilter` options etc.
//  — the store only shares the write/subscribe/snapshot plumbing).
//
//    - WRITE: a chip whose `FilterKind.onChange` is `None` (the declarative /
//      AI-authored shape) writes its typed value here under its `FilterSpec.Name`
//      on change — zero host code. A `Some` closure dispatches exactly as before
//      and never touches this store.
//    - READ:  `Binding.Filter name` reads `BindingSources.Filters`; the renderer
//      merges `FilterStore.snapshot ()` over that map per render (store wins).
//    - REACT: `useFilterKeys` subscribes a rendered surface so every
//      `Binding.Filter` reader re-paints when a chip writes — mirroring
//      `StateStore.useStateKeys` for the State channel.
//
//  Same single-process / single-threaded assumption as `StateStore` (see its
//  header): the process-global default instance + subscriber lists are unlocked
//  mutable state matching the one-Fable-runtime browser model; .NET tests use
//  distinct keys + `reset ()` between cases.
// ============================================================================

open Fuaran.UI.Renderer.StateStore

#if FABLE_COMPILER
open Feliz
#endif

// ── Process-global default filter store + module facade ─────────────────────
//  Reuses `StateStoreInstance` (the shared machinery) with a `$filters`-scoped
//  persist prefix, so a filter key and a same-named state key persist to
//  disjoint localStorage slots and never collide.

[<Literal>]
let private FilterPersistPrefix = "fuaran.filters."

let private defaultInstance = StateStoreInstance(FilterPersistPrefix)

/// Current filter value for `name`, hydrating from persistent storage on first read.
let get (name: string) : obj option = defaultInstance.Get name

/// Write `name`, persist (string values), and notify subscribers.
let set (name: string) (value: obj) : unit = defaultInstance.Set(name, value)

/// Remove a filter key (a cleared `ChoiceFilter` choice), then notify its
/// watchers so `Binding.Filter` readers fall back to `BindingSources.Filters`
/// (or the not-resolved state). Implemented as a `Set` of a sentinel-cleared
/// marker is avoided — instead we reset the in-memory slot by writing `None`'s
/// absence via a dedicated clear, keeping the snapshot free of the key.
let clear (name: string) : unit = defaultInstance.Remove name

/// Subscribe to any filter change; returns an unsubscribe thunk.
let subscribe (callback: unit -> unit) : unit -> unit = defaultInstance.Subscribe callback

/// Subscribe to changes of a specific set of filter `keys` only; the callback
/// fires when `set` writes a key in the set and stays silent otherwise. The
/// filter twin of `StateStore.subscribeKeys` — the reactivity behind the
/// renderer's `Binding.Filter` re-render opt-in.
let subscribeKeys (keys: Set<string>) (callback: unit -> unit) : unit -> unit =
    defaultInstance.SubscribeKeys(keys, callback)

/// Snapshot the loaded default filter store into a `BindingSources.Filters`-shaped map.
let snapshot () : Map<string, obj> = defaultInstance.Snapshot()

/// Clear the process-global default filter store and subscriber list. Test-isolation
/// seam mirroring `StateStore.reset` (persisted localStorage values are left intact).
let reset () : unit = defaultInstance.Reset()

#if FABLE_COMPILER
/// React hook: subscribe a rendered surface to a *set* of filter keys and force
/// a re-render whenever any of them changes. The filter twin of
/// `StateStore.useStateKeys` — the reactivity primitive behind the renderer's
/// `Binding.Filter` re-render opt-in, so a chip write re-paints every
/// `Binding.Filter` reader of that name. Returns a monotonically-increasing
/// tick whose only purpose is to drive the re-render.
let useFilterKeys (keys: Set<string>) : int =
    let depKey = keys |> Set.toSeq |> String.concat " "
    let tick, setTick = React.useState 0

    React.useEffect ((fun () -> subscribeKeys keys (fun () -> setTick (tick + 1))), [| box depKey; box tick |])

    tick
#endif
