namespace Fuaran.UI.ServerDriven.AspNetCore

open System
open System.Collections.Generic
open System.Threading.Tasks
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation

// ============================================================================
//  Connection registry (Phase 152, Track D — SSE+POST backend).
//
//  The multiplexing layer above the per-connection `IFuaranLiveChannel` seam:
//  a thread-safe `connId → connection` map so the POST endpoint can route an
//  inbound event to the right live session, and the GET-stream handler can
//  deregister on disconnect. `ILiveConnection` erases the connection's
//  `'Model` / `'Msg` so connections of different app shapes share one registry.
//
//  This is the **single-instance** boundary made concrete: the map lives in this
//  process's memory. A multi-node host needs sticky sessions (route a connId's
//  events to the owning node) or a shared store (rehydrate the session on any
//  node) — see `docs/SERVER_DRIVEN.md`.
//
//  ── Stream attachment (Phase 1928) ──────────────────────────────────────────
//  A connection outlives the SSE stream that opened it. When a stream ends —
//  the client dropped, a write failed, or the bounded queue closed it on
//  purpose (Phase 212) — the connection is DETACHED, not removed, and held for
//  `ReconnectGrace`, so the `EventSource` reconnect that follows (carrying
//  `Last-Event-ID`) re-attaches to the session the client had and replays what
//  it missed. A connection still detached when the grace runs out is evicted:
//  removed, its channel closed and its lifetime ended. The registry records
//  which stream holds each connection, so a stream that ends detaches only its
//  OWN attachment and never tears down a reconnect that replaced it.
// ============================================================================

/// The type-erased connection handle the registry stores.
type ILiveConnection =
    /// Route an inbound event into the connection's driver.
    abstract Handle: LiveEvent -> unit
    /// Replay frames newer than the client's last-applied sequence (reconnect).
    abstract Resync: int -> unit
    /// The connection's channel (for the stream handler to drain + close).
    abstract Channel: SseChannel

/// Phase 1928 — what the stream handler needs to re-attach a new stream to a
/// registered connection. Internal: hosts see `ILiveConnection` unchanged.
type internal IReattachable =
    /// Point the connection at a fresh frame queue and catch the client up from
    /// its last-applied sequence (replay, or re-baseline — never a partial
    /// replay). Returns that queue, for the new stream to drain.
    abstract Reattach: lastSeq: int -> SseChannel

/// Phase 1928 — the channel a re-attachable SSE connection is built over. Every
/// frame is relayed to the `SseChannel` of the stream currently attached; a
/// re-attach swaps in a fresh queue and closes the old one, which ends any
/// stream still draining it.
type internal RelayChannel() =
    let sync = obj ()
    let mutable current = SseChannel()

    /// The queue the attached stream drains.
    member _.Current = lock sync (fun () -> current)

    /// Close the current queue and start a fresh one for a new stream; the
    /// fresh queue.
    member _.Swap() =
        lock sync (fun () ->
            (current :> IFuaranLiveChannel).Close()
            current <- SseChannel()
            current)

    interface IFuaranLiveChannel with
        member _.Push(frame) =
            lock sync (fun () -> (current :> IFuaranLiveChannel).Push frame)

        member _.Receive(h) =
            lock sync (fun () -> (current :> IFuaranLiveChannel).Receive h)

        member _.Close() =
            lock sync (fun () -> (current :> IFuaranLiveChannel).Close())

[<AutoOpen>]
module LiveConnectionHandle =
    /// Erase a typed `LiveConnection` + its `SseChannel` to an `ILiveConnection`.
    let toHandle (conn: LiveConnection<'Model, 'Msg>) (channel: SseChannel) : ILiveConnection =
        { new ILiveConnection with
            member _.Handle ev = conn.Handle ev
            member _.Resync lastSeq = conn.Resync lastSeq |> ignore
            member _.Channel = channel }

    /// Phase 1928 — erase a connection built over a `RelayChannel` to a handle a
    /// reconnecting stream can re-attach to.
    let internal toReattachableHandle (conn: LiveConnection<'Model, 'Msg>) (relay: RelayChannel) : ILiveConnection =
        { new ILiveConnection with
            member _.Handle ev = conn.Handle ev
            member _.Resync lastSeq = conn.Resync lastSeq |> ignore
            member _.Channel = relay.Current
          interface IReattachable with
              member _.Reattach lastSeq =
                  // The queue is taken inside the gate, as it is swapped: a later
                  // re-attach swaps again and ends this stream, never shares it.
                  let fresh = ref None
                  conn.Reattach(lastSeq, (fun () -> fresh.Value <- Some(relay.Swap()))) |> ignore
                  Option.get fresh.Value }

/// One registered connection and the stream (if any) holding it.
type internal RegistryEntry(conn: ILiveConnection, onEvict: unit -> unit) =
    member _.Conn = conn
    member _.OnEvict = onEvict
    /// The attached stream's identity; `None` while detached.
    member val Stream: obj option = None with get, set
    /// Bumped on every attach / detach, so a grace timer armed for an earlier
    /// detachment cannot evict a connection that has been re-attached since.
    member val Generation = 0L with get, set

/// Thread-safe `connId → ILiveConnection` map, with the Phase 1928 stream
/// attachment and reconnect grace (see the file header).
type ConnectionRegistry() =
    let sync = obj ()
    let entries = Dictionary<string, RegistryEntry>()
    let mutable grace = ConnectionRegistry.DefaultReconnectGrace

    /// Register `entry` under `connId`; a connection it replaces is evicted.
    let put (connId: string) (entry: RegistryEntry) =
        let replaced =
            lock sync (fun () ->
                let previous =
                    match entries.TryGetValue connId with
                    | true, e when not (obj.ReferenceEquals(e.Conn, entry.Conn)) -> Some e
                    | _ -> None

                entries[connId] <- entry
                previous)

        replaced |> Option.iter (fun e -> e.OnEvict())

    /// Evict `connId` if it is still detached from the detachment `generation`.
    let evictIfDetached (connId: string) (generation: int64) =
        let evicted =
            lock sync (fun () ->
                match entries.TryGetValue connId with
                | true, e when e.Stream.IsNone && e.Generation = generation ->
                    entries.Remove connId |> ignore
                    Some e
                | _ -> None)

        evicted |> Option.iter (fun e -> e.OnEvict())

    /// Default `ReconnectGrace`: 30 seconds — comfortably past `EventSource`'s
    /// ~3 s reconnect delay and a brief network loss, short enough that an
    /// abandoned connection's memory is returned promptly.
    static member DefaultReconnectGrace = TimeSpan.FromSeconds 30.0

    /// Register (or replace) a connection. A connection it replaces is evicted.
    member _.Add(connId: string, conn: ILiveConnection) =
        put connId (RegistryEntry(conn, ignore))

    member _.TryGet(connId: string) : ILiveConnection option =
        lock sync (fun () ->
            match entries.TryGetValue connId with
            | true, e -> Some e.Conn
            | _ -> None)

    /// Deregister a connection now, ending its stream (if one is attached) and
    /// its lifetime.
    member _.Remove(connId: string) =
        let removed =
            lock sync (fun () ->
                match entries.TryGetValue connId with
                | true, e ->
                    entries.Remove connId |> ignore
                    Some e
                | _ -> None)

        removed |> Option.iter (fun e -> e.OnEvict())

    /// Number of registered connections — attached, or detached and inside
    /// their reconnect grace.
    member _.Count = lock sync (fun () -> entries.Count)

    /// Phase 1928 — how long a connection whose stream ended is held for a
    /// reconnect to re-attach to it (default `DefaultReconnectGrace`).
    /// `TimeSpan.Zero` evicts a connection the moment its stream ends. Applies
    /// to detachments from the time it is set.
    member _.ReconnectGrace
        with get () = grace
        and set (value: TimeSpan) =
            if value < TimeSpan.Zero then
                raise (ArgumentOutOfRangeException("value", "ReconnectGrace cannot be negative"))

            grace <- value

    /// Register a fresh connection with `stream` attached to it. `onEvict` runs
    /// once, when the connection is removed for good.
    member internal _.AddAttached(connId: string, conn: ILiveConnection, stream: obj, onEvict: unit -> unit) =
        let entry = RegistryEntry(conn, onEvict)
        entry.Stream <- Some stream
        put connId entry

    /// Attach `stream` to the re-attachable connection registered under
    /// `connId`. A connection another stream still holds is taken over only when
    /// `allowTakeover` (the request named its own tab, so it cannot be another
    /// tab's stream); otherwise `None` and the caller opens a fresh connection.
    member internal _.TryReattach(connId: string, stream: obj, allowTakeover: bool) : IReattachable option =
        lock sync (fun () ->
            match entries.TryGetValue connId with
            | true, e when e.Stream.IsNone || allowTakeover ->
                match e.Conn with
                | :? IReattachable as r ->
                    e.Stream <- Some stream
                    e.Generation <- e.Generation + 1L
                    Some r
                | _ -> None
            | _ -> None)

    /// `stream` ended. If it still holds `connId`, the connection is detached
    /// and evicted after `ReconnectGrace` unless a reconnect re-attaches first.
    /// A stream that no longer holds it (taken over, replaced, removed) detaches
    /// nothing.
    member internal _.Detach(connId: string, stream: obj) =
        let detached =
            lock sync (fun () ->
                match entries.TryGetValue connId with
                | true, e when e.Stream |> Option.exists (fun s -> obj.ReferenceEquals(s, stream)) ->
                    e.Stream <- None
                    e.Generation <- e.Generation + 1L
                    Some(e.Generation, grace)
                | _ -> None)

        match detached with
        | Some(generation, wait) when wait <= TimeSpan.Zero -> evictIfDetached connId generation
        | Some(generation, wait) ->
            Task.Delay(wait).ContinueWith(fun (_: Task) -> evictIfDetached connId generation)
            |> ignore
        | None -> ()
