namespace Fuaran.UI.ServerDriven.AspNetCore

open System.Collections.Concurrent
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
// ============================================================================

/// The type-erased connection handle the registry stores.
type ILiveConnection =
    /// Route an inbound event into the connection's driver.
    abstract Handle: LiveEvent -> unit
    /// Replay frames newer than the client's last-applied sequence (reconnect).
    abstract Resync: int -> unit
    /// The connection's channel (for the stream handler to drain + close).
    abstract Channel: SseChannel

[<AutoOpen>]
module LiveConnectionHandle =
    /// Erase a typed `LiveConnection` + its `SseChannel` to an `ILiveConnection`.
    let toHandle (conn: LiveConnection<'Model, 'Msg>) (channel: SseChannel) : ILiveConnection =
        { new ILiveConnection with
            member _.Handle ev = conn.Handle ev
            member _.Resync lastSeq = conn.Resync lastSeq |> ignore
            member _.Channel = channel }

/// Thread-safe `connId → ILiveConnection` map.
type ConnectionRegistry() =
    let conns = ConcurrentDictionary<string, ILiveConnection>()

    member _.Add(connId: string, conn: ILiveConnection) = conns[connId] <- conn

    member _.TryGet(connId: string) : ILiveConnection option =
        match conns.TryGetValue connId with
        | true, c -> Some c
        | _ -> None

    member _.Remove(connId: string) = conns.TryRemove connId |> ignore

    /// Number of live connections.
    member _.Count = conns.Count
