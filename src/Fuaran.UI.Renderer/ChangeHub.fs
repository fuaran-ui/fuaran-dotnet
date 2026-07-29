module Fuaran.UI.Renderer.ChangeHub

// ============================================================================
//  Fuaran — the committed-tree-change signal (Phase 739).
//
//  The in-page introspection surface (`DebugGlobal`) is REBUILT on every
//  committed tree change: the host calls `DebugGlobal.register` from its render
//  path, so a new tree means a new `window.__fuaran` object. A change
//  subscription registered on one surface instance would therefore be silently
//  dropped by the very event it exists to report.
//
//  The hub is the fix: subscriptions live HERE, outside any one surface
//  instance, so a `subscribe` handle survives every rebuild. The hub also owns
//  the **tree revision** — the opaque staleness token of the DevTools relay
//  contract §5.4, which a reader compares for equality and never parses.
//
//  Coalescing. Notification is handed to a `schedule` callback (a microtask
//  under Fable) and collapses every commit made in the same turn into ONE
//  notification carrying the LATEST revision. A change is a staleness signal,
//  not a change log — a reader that needs the new state re-reads it. This also
//  collapses the natural double-fire of an in-page apply (the apply path
//  commits the post-apply tree, then the host's re-render re-registers a
//  surface over that same tree), with the `Apply` cause winning over `Host`
//  because it is the more specific answer.
//
//  Idempotent on tree IDENTITY: committing the same tree object twice bumps
//  nothing and notifies nobody, so a re-registration caused only by a change of
//  `sources` / `runtime` is not reported as a tree change. Identity — not
//  structural equality — is deliberate: `Node<'Msg>` carries handler closures
//  and is not an F# equality type, and a physical-identity check is O(1) where
//  a structural walk of a whole tree on every render would not be.
//
//  The scheduler is a parameter rather than an ambient effect so the .NET test
//  runner can pin the coalescing contract deterministically (Fable-vs-.NET
//  pipeline parity: the hub itself is pure F# and compiles on both).
// ============================================================================

/// Why the tree changed. `Apply` — an in-page or relayed `TreeOp` did it;
/// `Host` — the host changed its own tree. A peer that cannot distinguish the
/// two MUST report `Host` (relay contract §8.5).
[<RequireQualifiedAccess>]
type ChangeCause =
    | Apply
    | Host

[<RequireQualifiedAccess>]
module ChangeCause =

    /// The relay wire token for a cause (§8.5 `changed.cause`).
    let toWire (cause: ChangeCause) : string =
        match cause with
        | ChangeCause.Apply -> "apply"
        | ChangeCause.Host -> "host"

/// One committed tree change: the revision AFTER the change, and what caused it.
type TreeChange =
    { TreeRevision: string
      Cause: ChangeCause }

/// A change subscriber. Registered with `ChangeHub.Subscribe`.
type ChangeListener = TreeChange -> unit

/// The committed-tree-change hub — revision ownership plus the subscription
/// registry that outlives any single introspection-surface instance.
type ChangeHub =
    {
        /// The current tree-revision token. Opaque: compare for equality, never parse.
        Revision: unit -> string
        /// Register a listener for committed tree changes. Returns an unsubscribe
        /// handle; calling it twice is harmless. Never polls — the hub pushes.
        Subscribe: ChangeListener -> (unit -> unit)
        /// Record a committed tree change and return the resulting revision.
        /// Idempotent on tree identity (see the header).
        Commit: obj -> ChangeCause -> string
    }

/// Build an isolated hub whose notifications are handed to `schedule`. Hosts
/// (and tests) that want their own signal — or a deterministic one — use this.
let createWith (schedule: (unit -> unit) -> unit) : ChangeHub =
    let listeners = ResizeArray<int * ChangeListener>()
    let mutable nextToken = 0
    let mutable counter = 0
    let mutable current = "r-0"
    let mutable lastTree: obj option = None
    let mutable pendingCause: ChangeCause option = None
    let mutable scheduled = false

    let flush () =
        scheduled <- false

        match pendingCause with
        | None -> ()
        | Some cause ->
            pendingCause <- None

            let change =
                { TreeRevision = current
                  Cause = cause }

            // Snapshot the registry: a listener that unsubscribes (or
            // subscribes) during delivery must not perturb this delivery pass.
            for (_, listener) in List.ofSeq listeners do
                try
                    listener change
                with _ ->
                    // A failing subscriber never breaks the notification of the
                    // others — the hub is a signal, not a dispatch chain.
                    ()

    { Revision = fun () -> current
      Subscribe =
        fun listener ->
            let token = nextToken
            nextToken <- nextToken + 1
            listeners.Add(token, listener)

            fun () ->
                match listeners |> Seq.tryFindIndex (fun (t, _) -> t = token) with
                | Some index -> listeners.RemoveAt index
                | None -> ()
      Commit =
        fun tree cause ->
            match lastTree with
            | Some previous when LanguagePrimitives.PhysicalEquality previous tree -> current
            | _ ->
                lastTree <- Some tree
                counter <- counter + 1
                current <- "r-" + string counter

                // `Apply` is the more specific cause and wins the coalesced window.
                pendingCause <-
                    match pendingCause with
                    | Some ChangeCause.Apply -> Some ChangeCause.Apply
                    | _ -> Some cause

                if not scheduled then
                    scheduled <- true
                    schedule flush

                current }

#if FABLE_COMPILER
open Fable.Core

/// Defer `run` to the microtask queue. Same posture the rest of the renderer's
/// browser reads take — self-contained `[<Emit>]` JS rather than a typed-DOM
/// dependency.
[<Emit("(typeof queueMicrotask === 'function') ? queueMicrotask($0) : Promise.resolve().then($0)")>]
let private scheduleSoon (run: unit -> unit) : unit = jsNative

#else

/// Non-Fable hosts (the .NET test runner / SSR) have no microtask queue, so a
/// default-hub notification is synchronous. Coalescing is a browser-pipeline
/// property; the tests pin it by injecting an explicit scheduler into
/// `createWith` rather than relying on this default.
let private scheduleSoon (run: unit -> unit) : unit = run ()

#endif

/// Build an isolated hub notifying on the default schedule for this pipeline.
let create () : ChangeHub = createWith scheduleSoon

/// The page-wide hub the renderer's debug surface uses by default. One page,
/// one tree-revision sequence — so two renderers on one page do not hand a
/// reader two conflicting notions of "current".
let pageHub: ChangeHub = create ()
