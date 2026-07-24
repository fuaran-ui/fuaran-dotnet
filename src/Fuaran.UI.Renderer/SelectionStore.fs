module Fuaran.UI.Renderer.SelectionStore

// ============================================================================
//  Reactive backing for Fuaran's app-level Selection channel (Phase 427).
//
//  The third instantiation of the `StateStore` machinery (Phase 106; the
//  Filter twin is Phase 423), kept in a DISTINCT `$selection` namespace keyed
//  by the selection-producing node's `NodeId` (unwrapped to its raw string —
//  the `BindingSources.Selections` map re-wraps at the merge seam).
//
//    - WRITE: a data-bearing grid whose `OnRowClick` is `None` (the
//      declarative / AI-authored shape) writes the clicked row here under its
//      own `NodeId` — zero host code. A `Some` closure dispatches exactly as
//      before and never touches this store.
//    - READ:  `Binding.Selection (nodeId, accessor)` reads
//      `BindingSources.Selections`; the renderer merges
//      `SelectionStore.snapshot ()` over that map per render (store wins).
//    - REACT: `useSelectionKeys` subscribes a rendered surface so every
//      `Binding.Selection` reader re-paints when a row is clicked — mirroring
//      `StateStore.useStateKeys` / `FilterStore.useFilterKeys`.
//
//  Selections are session-local UI state, so the persist prefix exists only to
//  keep the namespace disjoint — rows are non-string values and the shared
//  machinery persists strings only (a selection never survives a reload).
//  Same single-process / single-threaded assumption as `StateStore` (see its
//  header); .NET tests use distinct node ids + `reset ()` between cases.
// ============================================================================

open Fuaran.UI.Renderer.StateStore

#if FABLE_COMPILER
open Feliz
#endif

[<Literal>]
let private SelectionPersistPrefix = "fuaran.selection."

let private defaultInstance = StateStoreInstance(SelectionPersistPrefix)

/// Current selection for the node id, if any.
let get (nodeId: string) : obj option = defaultInstance.Get nodeId

/// Write the selected row for `nodeId` and notify subscribers.
let set (nodeId: string) (row: obj) : unit = defaultInstance.Set(nodeId, row)

/// Remove a node's selection, then notify its watchers so `Binding.Selection`
/// readers fall back to `BindingSources.Selections` (or the not-resolved state).
let clear (nodeId: string) : unit = defaultInstance.Remove nodeId

/// Subscribe to any selection change; returns an unsubscribe thunk.
let subscribe (callback: unit -> unit) : unit -> unit = defaultInstance.Subscribe callback

/// Subscribe to changes of a specific set of node ids only; the callback fires
/// when `set` writes a node id in the set and stays silent otherwise. The
/// selection twin of `StateStore.subscribeKeys`.
let subscribeKeys (nodeIds: Set<string>) (callback: unit -> unit) : unit -> unit =
    defaultInstance.SubscribeKeys(nodeIds, callback)

/// Snapshot the live selection store, keyed by raw node-id string (the caller
/// re-wraps `NodeId` when merging into `BindingSources.Selections`).
let snapshot () : Map<string, obj> = defaultInstance.Snapshot()

/// Clear the process-global default selection store and subscriber list.
/// Test-isolation seam mirroring `StateStore.reset` / `FilterStore.reset`.
let reset () : unit = defaultInstance.Reset()

#if FABLE_COMPILER
/// React hook: subscribe a rendered surface to a *set* of selection node ids
/// and force a re-render whenever any of them changes — the reactivity
/// primitive behind the renderer's `Binding.Selection` re-render opt-in, so a
/// row click re-paints every `Binding.Selection` reader of that grid. Returns
/// a monotonically-increasing tick whose only purpose is to drive the
/// re-render.
let useSelectionKeys (nodeIds: Set<string>) : int =
    let depKey = nodeIds |> Set.toSeq |> String.concat " "
    let tick, setTick = React.useState 0

    React.useEffect ((fun () -> subscribeKeys nodeIds (fun () -> setTick (tick + 1))), [| box depKey; box tick |])

    tick
#endif
