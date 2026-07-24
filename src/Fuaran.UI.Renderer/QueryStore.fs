module Fuaran.UI.Renderer.QueryStore

// ============================================================================
//  Reactive backing for `Action.Call`'s `IntoQuery` result target (Phase 428).
//
//  The fourth instantiation of the `StateStore` machinery (Phase 106; Filter
//  423, Selection 427), keyed by QUERY NAME in its own `$queries` namespace.
//  Distinct from the host-supplied `BindingSources.QueryResults` map: the host
//  bag stays the render-time input; this store carries the results a
//  declarative `Call … into Query <name>` wrote at run time, merged over the
//  host bag per render (store wins) exactly like the State/Filter/Selection
//  live-snapshot merges.
//
//    - WRITE: a `Call` whose `into = IntoQuery name` writes the decoded
//      response here on completion — zero host code.
//    - READ:  `Binding.Query (name, accessor, _)` reads `QueryResults`; the
//      renderer merges `QueryStore.snapshot ()` over that map per render
//      (data-preserving on the decoded path per the Phase 421 identity
//      accessor).
//    - REACT: `useQueryKeys` subscribes a rendered surface so every
//      `Binding.Query` reader re-paints when its slot is written.
//
//  Query results are session-local data, so the persist prefix exists only to
//  keep the namespace disjoint — results are non-string values and the shared
//  machinery persists strings only (a result never survives a reload). Same
//  single-process / single-threaded assumption as `StateStore` (see its
//  header); .NET tests use distinct names + `reset ()` between cases.
// ============================================================================

open Fuaran.UI.Renderer.StateStore

#if FABLE_COMPILER
open Feliz
#endif

[<Literal>]
let private QueryPersistPrefix = "fuaran.queryresults."

let private defaultInstance = StateStoreInstance(QueryPersistPrefix)

/// Current written result for the query name, if any.
let get (name: string) : obj option = defaultInstance.Get name

/// Write the result for `name` and notify subscribers.
let set (name: string) (result: obj) : unit = defaultInstance.Set(name, result)

/// Remove a query's written result, then notify its watchers so
/// `Binding.Query` readers fall back to the host `QueryResults` bag.
let clear (name: string) : unit = defaultInstance.Remove name

/// Subscribe to any written-result change; returns an unsubscribe thunk.
let subscribe (callback: unit -> unit) : unit -> unit = defaultInstance.Subscribe callback

/// Subscribe to changes of a specific set of query names only. The query twin
/// of `StateStore.subscribeKeys`.
let subscribeKeys (names: Set<string>) (callback: unit -> unit) : unit -> unit =
    defaultInstance.SubscribeKeys(names, callback)

/// Snapshot the written results, keyed by query name (merged over the host
/// `BindingSources.QueryResults` bag by the caller; store wins).
let snapshot () : Map<string, obj> = defaultInstance.Snapshot()

/// Clear the process-global default query store and subscriber list.
/// Test-isolation seam mirroring `StateStore.reset`.
let reset () : unit = defaultInstance.Reset()

#if FABLE_COMPILER
/// React hook: subscribe a rendered surface to a *set* of query names and
/// force a re-render whenever any of them is written — so a declarative
/// `Call … into Query <name>` re-paints every `Binding.Query name` reader.
/// Returns a monotonically-increasing tick whose only purpose is to drive the
/// re-render.
let useQueryKeys (names: Set<string>) : int =
    let depKey = names |> Set.toSeq |> String.concat " "
    let tick, setTick = React.useState 0

    React.useEffect ((fun () -> subscribeKeys names (fun () -> setTick (tick + 1))), [| box depKey; box tick |])

    tick
#endif
