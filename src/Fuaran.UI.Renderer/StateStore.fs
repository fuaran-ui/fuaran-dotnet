module Fuaran.UI.Renderer.StateStore

// ============================================================================
//  Persisted + observable backing for Fuaran's app-level State channel.
//
//  This is the substrate the diagnostic runtime warned was "not wired"
//  (Runtime.fs `SetState` no-op). It connects the two halves of the State
//  channel that previously dangled:
//
//    - WRITE: `Action.SetState(key, value)` -> `IFuaranRuntime.SetState` ->
//      `StateStore.set` (here), which persists string values to localStorage
//      and notifies subscribers.
//    - READ:  `Binding.State(key, default)` reads `BindingSources.State`; the
//      host merges `StateStore.snapshot ()` into that map per render.
//    - REACT: `useStateValue key default` subscribes a rendered surface so it
//      re-paints when the value changes (e.g. a global Cash/Real toggle).
//
//  Browser builds persist string values to localStorage so the value
//  survives reloads; the .NET build keeps an in-memory map (enough for
//  tests). Non-string state is in-memory only for v1 — string keys cover the
//  global-control use case (terms mode, theme, locale) the channel exists for.
// ============================================================================

open System.Collections.Generic

//  Host-reserved key namespace (Phase 782): the `host.` prefix a tree-originated
//  write cannot address lives in `Fuaran.UI.Renderer.StateKeys` (the
//  emission-agnostic core), because three separate paths enforce it — the client
//  renderer's `runAction`, its control write-back default, and the bounded
//  server-driven interpreter, which does not depend on this module. Writes made
//  through the module functions below are HOST writes and are deliberately
//  unrestricted: the host owns its own store.

// ── Single-process / single-threaded assumption (Phase 128) ─────────────────
//  The process-global default store + its subscriber lists are deliberately
//  unlocked mutable state. This matches the single-threaded browser-JS
//  execution model the channel exists for (one Fable runtime, one event loop)
//  and the renderer's registry-isolation rationale (per-scope isolation is
//  opt-in, not forced, because the common host is one app on one thread).
//
//  CONSEQUENCE on the .NET pipeline (SSR, the Expecto runner, any
//  multi-instance host): all Fuaran trees in the process share ONE default
//  store and ONE subscriber list. There is no per-thread isolation, so:
//    - tests must use distinct keys and unsubscribe what they subscribe (or
//      call `reset ()` between cases) to avoid cross-bleed;
//    - a multi-tenant / SSR host that serves many trees in one process should
//      mint an isolated store per scope via `forScope` (Phase 128, task 3) so
//      its `Binding.State` keys never collide across trees and a `reset` of
//      one scope never touches another. The scope registry is itself
//      process-global mutable state under this same single-threaded
//      assumption — a host needing OS-thread isolation guards its own access.
// ============================================================================

#if FABLE_COMPILER
open Fable.Core
open Fable.Core.JsInterop
open Feliz

[<Emit("(typeof localStorage !== 'undefined') ? localStorage.getItem($0) : null")>]
let private lsGet (key: string) : string = jsNative

[<Emit("(typeof localStorage !== 'undefined') ? (localStorage.setItem($0, $1), undefined) : undefined")>]
let private lsSet (key: string) (value: string) : unit = jsNative

// `storageKey` is the FULL localStorage key (prefix already applied by the
// instance) so scoped stores namespace beneath their own prefix.
let private persistTo (storageKey: string) (value: obj) : unit =
    match value with
    | :? string as s -> lsSet storageKey s
    | _ -> () // non-string state is in-memory only for v1

let private hydrateFrom (storageKey: string) : obj option =
    match lsGet storageKey with
    | null -> None
    | "" -> None
    | s -> Some(box s)
#else
let private persistTo (_storageKey: string) (_value: obj) : unit = ()
let private hydrateFrom (_storageKey: string) : obj option = None
#endif

/// An isolated state store: its own value map, subscriber structures, and
/// localStorage namespace. The process-global default (the `get` / `set` / …
/// module functions below) is one of these; SSR / multi-tenant hosts mint
/// additional isolated instances via `forScope` so `Binding.State` keys never
/// collide across trees sharing one process.
///
/// `persistPrefix` is prepended to every key before it reaches localStorage,
/// so the default store (`"fuaran.state."`) and a scoped store
/// (`"fuaran.state.<scopeId>."`) cannot collide in persistence either.
///
/// Notification is split (Phase 128, task 4): keyless subscribers (`Subscribe`)
/// fire on *every* `Set` — that is their contract — while keyed subscribers
/// (`SubscribeKeys`) are indexed by key and fire ONLY when a key they watch is
/// written. This replaces the prior single-list fan-out that invoked every
/// keyed subscriber on every write and let it filter internally.
type StateStoreInstance(persistPrefix: string) =
    let store = Dictionary<string, obj>()

    // Keyless subscribers: fire on every `Set` (backward-compatible contract,
    // pinned by an Expecto case). `useStateValue` registers here.
    let keylessSubscribers = ResizeArray<unit -> unit>()

    // Keyed subscribers: indexed by the key they watch, so a `Set` notifies
    // only the subscribers registered for that key — no per-write fan-out
    // across unrelated keyed subscribers. `useStateKeys` / `subscribeKeys`
    // register here.
    let keyedSubscribers = Dictionary<string, ResizeArray<unit -> unit>>()

    let notify (key: string) =
        // Iterate copies: a subscriber may unsubscribe during notification.
        for cb in List.ofSeq keylessSubscribers do
            cb ()

        match keyedSubscribers.TryGetValue key with
        | true, list ->
            for cb in List.ofSeq list do
                cb ()
        | _ -> ()

    /// Current value for `key`, hydrating from persistent storage on first read.
    member _.Get(key: string) : obj option =
        match store.TryGetValue key with
        | true, v -> Some v
        | _ ->
            match hydrateFrom (persistPrefix + key) with
            | Some v ->
                store[key] <- v
                Some v
            | None -> None

    /// Write `key`, persist (string values), and notify subscribers.
    member _.Set(key: string, value: obj) : unit =
        store[key] <- value
        persistTo (persistPrefix + key) value
        notify key

    /// Remove `key` from the in-memory store (if present) and notify its watchers,
    /// so readers fall back to their default/host source (Phase 423 — a cleared
    /// `ChoiceFilter` choice removes the key rather than writing an empty value).
    /// Persisted (localStorage) values are intentionally left intact, matching
    /// `Reset`'s persistence policy.
    member _.Remove(key: string) : unit =
        if store.Remove key then
            notify key

    /// Subscribe to any state change; returns an unsubscribe thunk. The
    /// callback fires on every `Set`, regardless of which key changed —
    /// preserved verbatim for `useStateValue`, which re-reads its own key.
    member _.Subscribe(callback: unit -> unit) : unit -> unit =
        keylessSubscribers.Add callback
        fun () -> keylessSubscribers.Remove callback |> ignore

    /// Subscribe to changes of a specific set of `keys` only; the callback
    /// fires when `Set` writes a key in the set and stays silent otherwise.
    /// Returns an unsubscribe thunk. Empty `keys` registers nothing and
    /// returns a no-op thunk so callers don't special-case the state-free
    /// tree.
    member _.SubscribeKeys(keys: Set<string>, callback: unit -> unit) : unit -> unit =
        if Set.isEmpty keys then
            ignore
        else
            for k in keys do
                match keyedSubscribers.TryGetValue k with
                | true, list -> list.Add callback
                | _ ->
                    let list = ResizeArray<unit -> unit>()
                    list.Add callback
                    keyedSubscribers[k] <- list

            fun () ->
                for k in keys do
                    match keyedSubscribers.TryGetValue k with
                    | true, list ->
                        list.Remove callback |> ignore
                        // Drop the bucket once empty so `keyedSubscribers`
                        // doesn't accumulate dead keys across mount/unmount.
                        if list.Count = 0 then
                            keyedSubscribers.Remove k |> ignore
                    | _ -> ()

    /// Snapshot the loaded store into a `BindingSources.State`-shaped map. The
    /// host merges this into `BindingSources.State` so `Binding.State` reads
    /// live store values.
    member _.Snapshot() : Map<string, obj> =
        [ for kv in store -> kv.Key, kv.Value ] |> Map.ofSeq

    /// Clear this instance's in-memory store + live subscriber lists. Persisted
    /// (localStorage) values are intentionally NOT cleared, so a reload
    /// re-hydrates them; `Reset` clears only the in-memory store + live
    /// subscriptions.
    member _.Reset() : unit =
        store.Clear()
        keylessSubscribers.Clear()
        keyedSubscribers.Clear()

// ── Process-global default store + module facade ────────────────────────────
//  The module-level functions delegate to a single process-global instance, so
//  existing callers (`Action.SetState` -> `set`, `withLiveState` -> `snapshot`,
//  the React hooks -> `subscribe` / `subscribeKeys`) keep byte-identical
//  behaviour. Scoped instances are an additive opt-in via `forScope`.

[<Literal>]
let private GlobalPersistPrefix = "fuaran.state."

let private defaultInstance = StateStoreInstance(GlobalPersistPrefix)

/// Current value for `key`, hydrating from persistent storage on first read.
let get (key: string) : obj option = defaultInstance.Get key

/// Write `key`, persist (string values), and notify subscribers.
let set (key: string) (value: obj) : unit = defaultInstance.Set(key, value)

/// Remove `key` from the default store and notify its watchers, so readers
/// fall back to their binding default (Phase 426 — the write-back default's
/// cleared-choice path; the State-channel twin of `FilterStore.clear`).
let remove (key: string) : unit = defaultInstance.Remove key

/// Subscribe to any state change; returns an unsubscribe thunk. The callback
/// fires on every `set`, regardless of which key changed.
let subscribe (callback: unit -> unit) : unit -> unit = defaultInstance.Subscribe callback

/// Subscribe to changes of a specific set of `keys` only; the callback fires
/// when `set` writes a key in the set and stays silent otherwise. Phase 106's
/// render-host opt-in uses this to re-render a whole surface when any
/// `Binding.State` key it reads changes — not just the control that owns the
/// value.
let subscribeKeys (keys: Set<string>) (callback: unit -> unit) : unit -> unit =
    defaultInstance.SubscribeKeys(keys, callback)

/// Snapshot the loaded default store into a `BindingSources.State`-shaped map.
let snapshot () : Map<string, obj> = defaultInstance.Snapshot()

/// Clear the process-global default store and subscriber list. Primarily a
/// test-isolation seam (Phase 128): the default store is a single-process
/// singleton (see the assumption note above), so a .NET test runner sharing
/// one process must reset between cases that assert on store contents or
/// notification counts to avoid cross-bleed. Not intended for the steady-state
/// browser path — persisted (localStorage) values are intentionally NOT cleared
/// here, so a reload re-hydrates them. Does NOT touch scoped instances created
/// via `forScope` — use `resetScope` / `resetAllScopes` for those.
let reset () : unit = defaultInstance.Reset()

// ── Scope-keyed instances (Phase 128, task 3) ───────────────────────────────
//  An SSR / multi-tenant host serving many Fuaran trees in one process can
//  mint an isolated store per scope (per request, per tenant) so their
//  `Binding.State` keys never collide and a `reset` of one scope never touches
//  another. Each scope also gets its own localStorage namespace
//  (`fuaran.state.<scopeId>.<key>`), distinct from the global default's
//  `fuaran.state.<key>`, so a scoped store on the browser path can't collide
//  with the global store in persistence either.
//
//  The registry is process-global mutable state under the same single-threaded
//  assumption as the default store. The common SSR host resolves one scope per
//  request on one logical flow; a host needing OS-thread isolation guards its
//  own access.

let private scopes = Dictionary<string, StateStoreInstance>()

/// Get (creating on first use) the isolated state store for `scopeId`. Repeated
/// calls with the same id return the same instance, so a host resolves the
/// scope's store wherever it renders that scope's tree. The scope id namespaces
/// both the in-memory map (a fresh instance) and the localStorage prefix, so
/// keys never collide with the global default or any other scope.
let forScope (scopeId: string) : StateStoreInstance =
    match scopes.TryGetValue scopeId with
    | true, inst -> inst
    | _ ->
        let inst = StateStoreInstance(GlobalPersistPrefix + scopeId + ".")
        scopes[scopeId] <- inst
        inst

/// Reset a single scope's instance (clear its in-memory store + live
/// subscriptions), if one exists. Like `reset ()`, persisted values are left
/// intact. No-op when the scope was never created.
let resetScope (scopeId: string) : unit =
    match scopes.TryGetValue scopeId with
    | true, inst -> inst.Reset()
    | _ -> ()

/// Drop every scoped instance (resetting each first). The process-global
/// default store is untouched — use `reset ()` for that. Primarily a
/// test-isolation seam for suites that exercise scope creation.
let resetAllScopes () : unit =
    for kv in scopes do
        kv.Value.Reset()

    scopes.Clear()

#if FABLE_COMPILER
/// React hook: subscribe a rendered surface to a string state key and
/// re-render whenever it changes. Returns the current value or `defaultValue`.
/// Encapsulates the only React needed for global-state reactivity, so
/// consumers author pure Fuaran and never write subscription glue themselves.
let useStateValue (key: string) (defaultValue: string) : string =
    let read () =
        get key |> Option.map string |> Option.defaultValue defaultValue

    let value, setValue = React.useState read

    React.useEffect (
        (fun () ->
            // Cleanup thunk (Feliz useEffect convention): unsubscribe on unmount.
            subscribe (fun () -> setValue (read ()))),
        [| box key |]
    )

    value

/// React hook: subscribe a rendered surface to a *set* of state keys and
/// force a re-render whenever any of them changes. Returns a monotonically-
/// increasing tick whose only purpose is to drive the re-render — callers
/// ignore the value. This is the reactivity primitive behind Phase 106's
/// `Render.renderStateReactive` opt-in: the surface re-paints (re-reading
/// every `Binding.State` reader against the live snapshot) on a single
/// global `Action.SetState`, not just the control that owns the value.
///
/// Subscription lifecycle: the `useEffect` cleanup thunk unsubscribes on
/// unmount and re-subscribes when the key set changes (so a surface that
/// stops reading a key drops its subscription — no leak). `tick` is folded
/// into the effect deps deliberately: each notification bumps it, which
/// re-runs the effect (unsub + resub) with a fresh closure, sidestepping a
/// stale-`tick` capture without a functional-update setter overload.
let useStateKeys (keys: Set<string>) : int =
    let depKey = keys |> Set.toSeq |> String.concat " "
    let tick, setTick = React.useState 0

    React.useEffect ((fun () -> subscribeKeys keys (fun () -> setTick (tick + 1))), [| box depKey; box tick |])

    tick
#endif
