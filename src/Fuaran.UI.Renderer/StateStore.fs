module Fuaran.UI.Renderer.StateStore

// ============================================================================
//  Persisted + observable backing for Fuaran's app-level State channel.
//
//  This is the substrate the diagnostic runtime warned was "not wired"
//  (Runtime.fs `SetState` no-op). It connects the two halves of the State
//  channel that previously dangled:
//
//    - WRITE: `Action.SetState(key, value)` -> `IFuaranRuntime.SetState` ->
//      `StateStore.set` (here), which notifies subscribers and — for a string
//      value under a key the host declared persistent — writes localStorage.
//    - READ:  `Binding.State(key, default)` reads `BindingSources.State`; the
//      host merges `StateStore.snapshot ()` into that map per render.
//    - REACT: `useStateValue key default` subscribes a rendered surface so it
//      re-paints when the value changes (e.g. a global Cash/Real toggle).
//
//  Browser builds persist string values to localStorage so the value
//  survives reloads; the .NET build persists nothing by default. Non-string
//  state is in-memory only for v1 — string keys cover the global-control use
//  case (terms mode, theme, locale) the channel exists for.
//
//  ── WHAT PERSISTS IS DECLARED, NOT INFERRED (Phase 1532) ───────────────────
//  A key persists only when the HOST has declared it persistent
//  (`declarePersistent`). Everything else — which is everything, until a host
//  says otherwise — lives for the session and no longer.
//
//  This NARROWS an existing path rather than adding one: `Action.SetState`
//  still reaches `set` through the same Phase 782 dispatch gate, and the
//  in-memory write, the notification and the `Binding.State` read are all
//  unchanged. What changed is the far side of that write. A tree-originated
//  `SetState` used to put a string in localStorage forever — no expiry, no
//  budget, and no way for the host to say which of its keys were meant to
//  outlive the tab — and a tree is not the thing that can know whether a value
//  should survive a reload on someone else's machine. The host knows; now it
//  says.
//
//  Two consequences follow from the declaration, and both are the point.
//  HYDRATION is gated by it too, so a key the host has stopped declaring stops
//  being read back rather than serving a value from a previous release
//  forever. And `Remove` / `Reset` CLEAR the persisted value rather than
//  leaving it for the next reload to resurrect — the old behaviour meant a
//  cleared filter came back on refresh, which reads as the clear not having
//  worked.
// ============================================================================

open System.Collections.Generic

//  Host-reserved key namespace (Phase 782): the `host.` prefix a tree-originated
//  write cannot address lives in `Fuaran.UI.Renderer.StateKeys` (the
//  emission-agnostic core), because three separate paths enforce it — the client
//  renderer's `runAction`, its control write-back default, and the bounded
//  server-driven interpreter, which does not depend on this module. Writes made
//  through the module functions below are HOST writes and are deliberately
//  unrestricted: the host owns its own store.
//
//  Phase 1550 adds the host-facing `declareReserved` / `isReserved` /
//  `reservedKeys` beside `declarePersistent`, so one host says what it owns in
//  one place. They are module-level and delegate: reservation is process-wide
//  policy, not per-instance state — see the block beside them.

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
#endif

/// The persistent half of a state store, as a PORT rather than a hard-wired
/// `localStorage` call.
///
/// Every function takes the FULL storage key — an instance's `persistPrefix`
/// is already applied — so scoped stores namespace beneath their own prefix and
/// `RemovePrefix` can evict a whole scope in one call.
///
/// A port rather than only a compile-time platform split, because the readers
/// want different backings and one of them cannot be reached otherwise: the
/// browser wants `localStorage`, a .NET build wants nothing (the default
/// below), and a test — or a server host that means to persist somewhere real
/// — wants to say. The quota arm in particular is a browser fact with no .NET
/// twin, so without a port it is a path no test can enter.
type StatePersistence =
    {
        /// The value stored under `storageKey`, or `None` when absent or empty.
        Read: string -> string option
        /// Store `value` under `storageKey`. MAY THROW — a browser at its
        /// storage quota does exactly that, and the caller treats it as a
        /// diagnostic rather than as a failure of the write it was asked for.
        Write: string -> string -> unit
        /// Drop `storageKey`, if present.
        Remove: string -> unit
        /// Drop every stored key beginning with `prefix` — how a disposed
        /// scope's whole namespace leaves the backing store.
        RemovePrefix: string -> unit
    }

/// Ordinal prefix test, written out rather than taken from the BCL: this file
/// is Fable-compiled, and the culture-carrying `StartsWith` overloads are not
/// worth the portability question for two lines.
let private hasPrefix (prefix: string) (s: string) : bool =
    s.Length >= prefix.Length && s.Substring(0, prefix.Length) = prefix

#if FABLE_COMPILER
[<Emit("(typeof localStorage !== 'undefined') ? localStorage.getItem($0) : null")>]
let private lsGet (key: string) : string = jsNative

[<Emit("(typeof localStorage !== 'undefined') ? (localStorage.setItem($0, $1), undefined) : undefined")>]
let private lsSet (key: string) (value: string) : unit = jsNative

[<Emit("(typeof localStorage !== 'undefined') ? (localStorage.removeItem($0), undefined) : undefined")>]
let private lsRemove (key: string) : unit = jsNative

// Backwards, because `removeItem` renumbers the indices ahead of it: a forward
// loop skips the key that slides into the slot just vacated.
[<Emit("(function(p){ if (typeof localStorage === 'undefined') { return; } for (var i = localStorage.length - 1; i >= 0; i--) { var k = localStorage.key(i); if (k !== null && k.indexOf(p) === 0) { localStorage.removeItem(k); } } })($0)")>]
let private lsRemovePrefix (prefix: string) : unit = jsNative

/// The browser default: `localStorage`, which is what "survives a reload"
/// means. `Write` is left free to throw — a browser at its quota does, and the
/// store above turns that into a diagnostic.
let defaultPersistence: StatePersistence =
    { Read =
        fun key ->
            match lsGet key with
            | null -> None
            | "" -> None
            | s -> Some s
      Write = lsSet
      Remove = lsRemove
      RemovePrefix = lsRemovePrefix }
#else
/// The .NET default: nothing is persisted and nothing is read back. There is no
/// localStorage off the browser, and inventing a process-lifetime map here
/// would make a server host's "persistent" key mean something it did not ask
/// for. A server host that wants real persistence supplies its own port.
let defaultPersistence: StatePersistence =
    { Read = fun _ -> None
      Write = fun _ _ -> ()
      Remove = ignore
      RemovePrefix = ignore }
#endif

/// A `StatePersistence` over a fresh in-memory map. Every call returns an
/// independent one, so two of them cannot see each other's keys.
///
/// This is what a .NET test drives the persistence contract with — the default
/// on that leg stores nothing, so the contract would otherwise be observable
/// only in a browser — and it is a serviceable backing for a host that wants
/// process-lifetime persistence and no more.
let inMemoryPersistence () : StatePersistence =
    let map = Dictionary<string, string>()

    { Read =
        fun key ->
            match map.TryGetValue key with
            | true, "" -> None
            | true, v -> Some v
            | _ -> None
      Write = fun key value -> map[key] <- value
      Remove = fun key -> map.Remove key |> ignore
      RemovePrefix =
        fun prefix ->
            let doomed =
                [ for kv in map do
                      if hasPrefix prefix kv.Key then
                          kv.Key ]

            for k in doomed do
                map.Remove k |> ignore }

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
///
/// `persistence` is the backing store the declared keys reach (Phase 1532);
/// the one-argument constructor takes `defaultPersistence`, so every existing
/// construction site compiles and behaves unchanged.
type StateStoreInstance(persistPrefix: string, persistence: StatePersistence) =
    let store = Dictionary<string, obj>()

    // The host's declared persistent-key allow-list. EMPTY by default, so a
    // store persists nothing until a host names what it meant to keep — the
    // same posture as the Phase 782 dispatch gate, for the same reason: the
    // safe default is the one a host has to opt out of, not into.
    let mutable declared = Set.empty<string>

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

    /// Persist one declared string value. An undeclared key, and any non-string
    /// value, never reaches the backing store at all.
    ///
    /// A refusal from the backing store is a DIAGNOSTIC, not an exception. The
    /// browser throws `QuotaExceededError` from `setItem` when its origin
    /// budget is full (and in Safari's private mode from the first write), and
    /// that throw used to leave the action interpreter mid-chain: the value was
    /// already in memory and already notified, so the visible result was a
    /// half-run action chain caused by something no part of the tree can see or
    /// influence. The write it was asked for — the in-memory one — succeeded;
    /// what failed is the extra durability, and saying so on the renderer's
    /// diagnostic line is the whole of the correct response.
    let persistDeclared (key: string) (value: obj) : unit =
        if declared.Contains key then
            match value with
            | :? string as s ->
                try
                    persistence.Write (persistPrefix + key) s
                with e ->
                    Diagnostics.warn
                        (sprintf
                            "state key '%s' could not be persisted; its value stands for this session but will not survive a reload (the usual cause is the browser's storage quota)"
                            key)
                        e
            | _ -> () // non-string state is in-memory only for v1

    /// Current value for `key`, hydrating from persistent storage on first read.
    ///
    /// Hydration is gated by the same declaration as the write: a key the host
    /// does not declare is not read back, so retiring a key retires its stored
    /// value rather than leaving it to be served indefinitely.
    member _.Get(key: string) : obj option =
        match store.TryGetValue key with
        | true, v -> Some v
        | _ ->
            if declared.Contains key then
                match persistence.Read(persistPrefix + key) with
                | Some v ->
                    let boxed = box v
                    store[key] <- boxed
                    Some boxed
                | None -> None
            else
                None

    /// Write `key`, persist it when the host declared it persistent, and notify
    /// subscribers. The in-memory write and the notification are unconditional
    /// — the declaration governs durability, never visibility.
    member _.Set(key: string, value: obj) : unit =
        store[key] <- value
        persistDeclared key value
        notify key

    /// Remove `key` from the in-memory store AND from persistent storage, then
    /// notify its watchers so readers fall back to their default/host source
    /// (Phase 423 — a cleared `ChoiceFilter` choice removes the key rather than
    /// writing an empty value).
    ///
    /// The persisted value goes too (Phase 1532). Leaving it meant a cleared
    /// value came back on the next reload, which is indistinguishable from the
    /// clear never having happened.
    member _.Remove(key: string) : unit =
        let hadValue = store.Remove key

        let hadPersisted =
            if declared.Contains key then
                let storageKey = persistPrefix + key
                let existed = (persistence.Read storageKey).IsSome
                persistence.Remove storageKey
                existed
            else
                false

        if hadValue || hadPersisted then
            notify key

    /// Declare which keys this store may persist. Additive and idempotent — a
    /// host states its whole set once at startup, or names keys as the surfaces
    /// that own them are composed.
    ///
    /// Declaring a key does not read it; the next `Get` does that.
    member _.DeclarePersistent(keys: seq<string>) : unit =
        for k in keys do
            declared <- Set.add k declared

    /// The declared persistent keys, for a host that wants to show or check its
    /// own declaration.
    member _.PersistentKeys: Set<string> = declared

    /// Whether `key` is declared persistent on this store.
    member _.IsPersistent(key: string) : bool = declared.Contains key

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

    /// `true` when this instance holds no loaded values — the question
    /// `withLiveState` asks per render before deciding whether a merge can
    /// change anything. Answering it directly costs nothing; answering it as
    /// `Map.isEmpty (Snapshot ())` built a whole `Map` first (Phase 207).
    member _.IsEmpty: bool = store.Count = 0

    /// Overlay this store's live values onto `target` (store wins), keying each
    /// entry with `keyOf` — the per-render READ VIEW behind `withLiveState`.
    ///
    /// Same result as `Snapshot() |> Map.fold (fun acc k v -> Map.add (keyOf k) v acc) target`,
    /// with the intermediate snapshot `Map` removed: that map was built once per
    /// store per render purely to be folded away again. An empty store returns
    /// `target` unchanged (the same reference), so a store-free tree allocates
    /// nothing here at all.
    ///
    /// Perf primitive (Phase 207): the `mutable` accumulator is deliberate. Do
    /// NOT "simplify" it back through `Snapshot()` — the result is identical, so
    /// no behavioural test can see the regression.
    member _.OverlayOnto(target: Map<'K, obj>, keyOf: string -> 'K) : Map<'K, obj> =
        if store.Count = 0 then
            target
        else
            let mutable acc = target

            for kv in store do
                acc <- Map.add (keyOf kv.Key) kv.Value acc

            acc

    /// Clear this instance's in-memory store, its live subscriber lists, the
    /// persisted value of every key it was told to persist, and the declaration
    /// itself.
    ///
    /// The persisted half goes too (Phase 1532), which is what makes this a
    /// usable isolation seam: a `Reset` that left storage populated left the
    /// next case's first `Get` hydrating the previous case's value. Only
    /// DECLARED keys are removed — exact keys, never a prefix sweep, because
    /// the default store's prefix is a prefix of every scope's and a sweep from
    /// here would take their values with it.
    member _.Reset() : unit =
        for k in declared do
            persistence.Remove(persistPrefix + k)

        declared <- Set.empty
        store.Clear()
        keylessSubscribers.Clear()
        keyedSubscribers.Clear()

    /// Drop every persisted value under this instance's prefix, declared or
    /// not — how a disposed scope's namespace leaves the backing store
    /// (`disposeScope`). Safe as a prefix sweep because a scope prefix ends in
    /// its own separator, so no scope's prefix is a prefix of another's.
    member _.EvictPersisted() : unit = persistence.RemovePrefix persistPrefix

    /// The persistence-defaulted constructor: every pre-1532 construction site
    /// compiles unchanged and gets the platform default.
    new(persistPrefix: string) = StateStoreInstance(persistPrefix, defaultPersistence)

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

/// Remove `key` from the default store — in memory and, when it was declared
/// persistent, in storage — and notify its watchers, so readers fall back to
/// their binding default (Phase 426 — the write-back default's cleared-choice
/// path; the State-channel twin of `FilterStore.clear`).
let remove (key: string) : unit = defaultInstance.Remove key

/// Declare which state keys the default store may persist (Phase 1532).
///
/// THE HOST'S CALL, and the only way a key outlives the session. A tree can
/// write any key it is allowed to dispatch; whether that write survives a
/// reload on the reader's machine is a decision about the reader's storage,
/// which the host makes and the tree cannot. Additive and idempotent.
///
/// A host with no such keys calls nothing: the default is that state is
/// session-lived.
let declarePersistent (keys: seq<string>) : unit = defaultInstance.DeclarePersistent keys

/// Whether `key` is declared persistent on the default store.
let isPersistent (key: string) : bool = defaultInstance.IsPersistent key

/// The default store's declared persistent keys.
let persistentKeys () : Set<string> = defaultInstance.PersistentKeys

// ── Host-RESERVED keys (Phase 1550) ─────────────────────────────────────────
//  The second thing a host declares about its own key namespace, beside the
//  persistent-key declaration above and deliberately in the same place: one
//  host, one place it says what it owns.
//
//  The two declarations answer different questions and are correctly
//  independent. `declarePersistent` says a key SURVIVES A RELOAD; this says a
//  RENDERED TREE MAY NOT WRITE IT. A host key is usually both and need not be
//  either.
//
//  MODULE-LEVEL ONLY, with no `StateStoreInstance` twin, because reservation is
//  process-wide policy rather than per-store state: the paths that enforce it
//  are tree-originated writes, which hold no store handle, and the bounded
//  server-driven interpreter does not depend on this module at all. Hence these
//  delegate to `Fuaran.UI.StateKeyPolicy` — the lowest tier any reader occupies,
//  where the `host.` prefix has lived since Phase 932 — rather than holding a
//  second copy of the list. One mechanism; this is the host-facing spelling of
//  it.

/// Declare state keys as HOST-OWNED by exact name, so no rendered tree can
/// write them (Phase 1550).
///
/// THE HOST'S CALL, like `declarePersistent`, and for the mirror reason: which
/// of a host's own key names are sensitive is not something a shipped default
/// can know. The `host.` prefix rule still reserves everything it always did —
/// this is for the slot that predates the convention and cannot cheaply be
/// renamed through every reader, seed and persisted value.
///
/// Additive, idempotent, process-wide, and there is no withdrawal: a function
/// that un-reserved a key would be reachable from any assembly in the process,
/// which is the fail-open shape the reservation exists to remove.
let declareReserved (keys: seq<string>) : unit = StateKeys.declareReserved keys

/// Whether `key` is host-owned — under the `host.` prefix, or declared by exact
/// name. The question every tree-originated write asks.
let isReserved (key: string) : bool = StateKeys.isReserved key

/// The keys declared reserved by exact name. The prefix rule is not an
/// enumerable set, so this is the declaration and not the whole reservation —
/// ask [[isReserved]] about a key.
let reservedKeys () : Set<string> = StateKeys.reservedKeys ()

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

/// `true` when the default store holds no loaded values.
let isEmpty () : bool = defaultInstance.IsEmpty

/// Overlay the default store's live values onto `target` (store wins) without
/// materialising an intermediate snapshot `Map` — the per-render read view
/// `withLiveState` uses. See `StateStoreInstance.OverlayOnto`.
let overlayOnto (target: Map<string, obj>) : Map<string, obj> = defaultInstance.OverlayOnto(target, id)

/// Clear the process-global default store and subscriber list. Primarily a
/// test-isolation seam (Phase 128): the default store is a single-process
/// singleton (see the assumption note above), so a .NET test runner sharing
/// one process must reset between cases that assert on store contents or
/// notification counts to avoid cross-bleed. Not intended for the steady-state
/// browser path — it also clears the persisted value of every DECLARED key and
/// the declaration itself (Phase 1532), so a host that resets at runtime
/// re-declares afterwards. Does NOT touch scoped instances created via
/// `forScope` — use `resetScope` / `disposeScope` / `resetAllScopes` for those.
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

/// Reset a single scope's instance (clear its in-memory store, live
/// subscriptions, declared keys and their persisted values), if one exists. The
/// instance stays in the registry — `disposeScope` is what retires it. No-op
/// when the scope was never created.
let resetScope (scopeId: string) : unit =
    match scopes.TryGetValue scopeId with
    | true, inst -> inst.Reset()
    | _ -> ()

/// Retire a scope: reset its instance, drop everything persisted under its
/// prefix, and REMOVE IT FROM THE REGISTRY (Phase 1532).
///
/// `forScope` creates on first use and nothing ever removed, so an SSR host
/// resolving one scope per request accumulated one `StateStoreInstance` — with
/// its value map and its subscriber lists — per request served, for the life of
/// the process. That is a leak whose size is the traffic, and no amount of
/// `resetScope` reclaims it, because reset empties an instance rather than
/// releasing it.
///
/// A later `forScope` with the same id mints a FRESH instance, which is the
/// right answer for a per-request scope and the reason the prefix is swept:
/// otherwise the new instance would hydrate the retired request's values.
let disposeScope (scopeId: string) : unit =
    match scopes.TryGetValue scopeId with
    | true, inst ->
        inst.Reset()
        inst.EvictPersisted()
        scopes.Remove scopeId |> ignore
    | _ -> ()

/// Drop every scoped instance, disposing each as `disposeScope` would (reset,
/// then evict its persisted prefix). The process-global default store is
/// untouched — use `reset ()` for that. Primarily a test-isolation seam for
/// suites that exercise scope creation.
let resetAllScopes () : unit =
    for kv in scopes do
        kv.Value.Reset()
        kv.Value.EvictPersisted()

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
/// stops reading a key drops its subscription — no leak).
///
/// SUBSCRIBED ONCE PER KEY SET. The effect deps are the key set alone. `tick`
/// used to sit beside it, so every notification re-ran the effect: unsubscribe,
/// re-subscribe, on every single write. That was churn rather than a leak — the
/// cleanup did run — but it is churn proportional to write volume on every
/// subscribed surface, and it made a subscription's lifetime depend on how often
/// the value changed rather than on what the surface reads.
///
/// `tick` was in the deps to sidestep a stale-closure capture: the effect's
/// `setTick (tick + 1)` closes over the `tick` of the render that created it, so
/// a subscription that outlived one notification would keep incrementing from a
/// stale base and stop advancing. `useStateWithUpdater` removes the capture
/// instead of refreshing it — the updater is handed the CURRENT value, so the
/// closure never reads a stale one and the subscription can live as long as the
/// key set does.
let useStateKeys (keys: Set<string>) : int =
    let depKey = keys |> Set.toSeq |> String.concat " "
    let tick, setTick = React.useStateWithUpdater 0

    React.useEffect ((fun () -> subscribeKeys keys (fun () -> setTick (fun current -> current + 1))), [| box depKey |])

    tick
#endif
