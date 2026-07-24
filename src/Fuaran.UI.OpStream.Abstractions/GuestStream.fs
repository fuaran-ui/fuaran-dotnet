namespace Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  GuestStream — the `guest-<scopeId>` op-stream keying convention (Phase 267).
//
//  A mounted guest (`NodeKind.Mount`, Phase 265) gets its OWN op-stream, forked
//  from the host's: every op applied inside a guest scope appends under
//  `streamId = "guest-<scopeId>"` rather than the host stream. The host stream
//  therefore contains only host ops; a guest's interior is recoverable from its
//  own stream alone, and "which region did this?" is answerable by projecting
//  the scope id back out of the stream id.
//
//  This is a pure string convention — no DAG dependency, FSharp.Core only, and
//  Fable-safe (StartsWith / Substring only) — so it lives in the LINEAR
//  abstractions package alongside `OpRecord` / `IOpStreamSink`, and both the
//  linear sink and the opt-in rung-4 DAG layer key on it identically. The
//  causal-anchor + convergence machinery that makes the guest fork reconcilable
//  with the host lives in the DAG packages (`GuestFork`, `GuestConvergence`,
//  `GuestReplay`), which build on this convention.
// ============================================================================

/// The `guest-<scopeId>` stream-id convention (Phase 267, §4o). A guest scope's
/// applied ops append under `streamId scopeId`; `tryScopeOf` reverses it so a
/// record's provenance region resolves from its stream id alone.
module GuestStream =

    /// The reserved prefix marking a guest op-stream. A host stream never starts
    /// with it (host stream ids are app-chosen and MUST avoid this prefix).
    [<Literal>]
    let Prefix = "guest-"

    /// The op-stream id a guest scope's ops append under: `"guest-" + scopeId`.
    let streamId (scopeId: string) : string = Prefix + scopeId

    /// `true` when `streamId` names a guest stream (i.e. was produced by
    /// `streamId`). Host streams answer `false`.
    let isGuestStream (streamId: string) : bool = streamId.StartsWith Prefix

    /// The guest scope id carried by a guest stream id, or `None` for a host
    /// stream. `tryScopeOf (streamId s) = Some s` for every scope id `s`.
    let tryScopeOf (streamId: string) : string option =
        if streamId.StartsWith Prefix then
            Some(streamId.Substring Prefix.Length)
        else
            None
