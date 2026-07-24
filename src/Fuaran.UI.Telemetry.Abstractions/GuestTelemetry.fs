namespace Fuaran.UI.Telemetry.Abstractions

open System
open System.Collections.Generic

// ============================================================================
//  GuestTelemetry — per-guest-scope sink resolution + scope-attributed
//  diagnostics (Phase 270, §4o).
//
//  A `NodeKind.Mount` guest is an isolated region: its diagnostics must stay
//  guest-local so a region's noise never pollutes the host's feedback loop,
//  and so each region can be evaluated, debugged, and audited independently.
//  Two seams, both keyed by the mount's guest scope id:
//
//   1. Sink resolution — `sinkFor scopeId` resolves the guest's OWN
//      `IFuaranTelemetrySink`. Isolation is by shape: an unregistered scope
//      resolves to a silent no-op sink, NEVER the host's sink — guest records
//      reach an aggregate sink only through the opt-in rollup below.
//
//   2. Warn/trace channel — `warn` / `trace` buffer scope-attributed
//      diagnostic lines per guest scope. Nothing is written to a console:
//      the bounded buffer is the destination, so the channel behaves
//      identically under the Fable and .NET pipelines by construction
//      (FGP 4). Readers (an inspector, a devtools surface, a test) pull
//      `diagnosticsFor` / `allDiagnostics`.
//
//  Host rollup is OPT-IN — the default is per-guest isolation:
//   - `enableRollup aggregate` — every guest-resolved sink additionally
//     tees its records into the aggregate sink (the "see all guests at
//     once" operator view).
//   - `setDiagnosticEcho emit` — every guest warn/trace line is also handed
//     to the host's emit seam (e.g. a console writer), scope-attributed.
//
//  This module holds mutable registry state, which is unusual for the
//  abstractions package — deliberate: the seams that must resolve a guest's
//  sink (a scoped runtime facade, an apply wrapper, a render guard) see only
//  this package, and the resolution must be shared by every tier that
//  touches a guest boundary. Concrete sinks stay in companion packages.
// ============================================================================

/// Severity of a guest-attributed diagnostic line. `Warning` is the guest
/// counterpart of a host console warn; `Trace` is informational.
[<RequireQualifiedAccess>]
type GuestDiagnosticLevel =
    | Warning
    | Trace

/// One scope-attributed diagnostic line emitted from inside a mounted guest.
/// `ScopeId` is the mount's guest scope id — attribution is structural, not
/// a message-prefix convention.
type GuestDiagnostic =
    { ScopeId: string
      Level: GuestDiagnosticLevel
      Message: string
      Timestamp: DateTimeOffset }

[<RequireQualifiedAccess>]
module GuestTelemetry =

    /// Bounded per-scope diagnostic buffer size — ring semantics past this
    /// (oldest dropped), mirroring the in-memory sink precedent.
    [<Literal>]
    let private diagnosticCapacity = 2_000

    // `lock` is a no-op under Fable (single-threaded browser JS) and guards
    // the .NET-side concurrent-registration case — the FuaranRuntimeScope
    // precedent.
    let private sync = obj ()
    let private sinks = Dictionary<string, IFuaranTelemetrySink>()
    let private diagnostics = Dictionary<string, ResizeArray<GuestDiagnostic>>()
    let mutable private rollup: IFuaranTelemetrySink option = None
    let mutable private echo: (GuestDiagnostic -> unit) option = None

    /// The isolated default for an unregistered scope: records are dropped,
    /// never routed to the host (default-deny by shape, FGP 3's posture
    /// applied to observability). A host that wants a guest's records
    /// registers a sink for that scope.
    let private noOpSink =
        { new IFuaranTelemetrySink with
            member _.RecordOpApply _ = ()
            member _.RecordDeny _ = ()
            member _.RecordRenderFailure _ = ()
            member _.RecordProviderCall _ = ()
            member _.RecordCacheStat _ = () }

    /// Deliver one record to the scope's current sink (registered or the
    /// isolated no-op) and, when rollup is enabled, to the aggregate sink.
    /// Registration + rollup state are re-read per record so wiring changes
    /// after resolution take effect immediately.
    let private deliver (scopeId: string) (record: IFuaranTelemetrySink -> unit) : unit =
        let guest, aggregate =
            lock sync (fun () ->
                (match sinks.TryGetValue scopeId with
                 | true, s -> s
                 | false, _ -> noOpSink),
                rollup)

        record guest
        aggregate |> Option.iter record

    // ─── Sink resolution ────────────────────────────────────────────

    /// Register (or replace) the telemetry sink for a guest scope. From then
    /// on `sinkFor scopeId` delivers the guest's records here — and nowhere
    /// else unless rollup is enabled.
    let registerSinkFor (scopeId: string) (sink: IFuaranTelemetrySink) : unit =
        lock sync (fun () -> sinks[scopeId] <- sink)

    /// The sink registered for a guest scope, if any.
    let trySinkFor (scopeId: string) : IFuaranTelemetrySink option =
        lock sync (fun () ->
            match sinks.TryGetValue scopeId with
            | true, s -> Some s
            | false, _ -> None)

    /// Resolve the effective sink for a guest scope. The returned facade is
    /// stable: it re-reads the registration + rollup state on every record,
    /// so a sink registered (or rollup toggled) after resolution is honoured
    /// by already-resolved handles. An unregistered scope's records go to the
    /// isolated no-op — never the host's sink.
    let sinkFor (scopeId: string) : IFuaranTelemetrySink =
        { new IFuaranTelemetrySink with
            member _.RecordOpApply t =
                deliver scopeId (fun s -> s.RecordOpApply t)

            member _.RecordDeny t =
                deliver scopeId (fun s -> s.RecordDeny t)

            member _.RecordRenderFailure t =
                deliver scopeId (fun s -> s.RecordRenderFailure t)

            member _.RecordProviderCall t =
                deliver scopeId (fun s -> s.RecordProviderCall t)

            member _.RecordCacheStat t =
                deliver scopeId (fun s -> s.RecordCacheStat t) }

    /// Opt into the host-rollup view: every guest-resolved sink additionally
    /// tees its records into `aggregate` (all guests at once). Guest records
    /// still land in each guest's own sink; the default (no rollup) is
    /// per-guest isolation.
    let enableRollup (aggregate: IFuaranTelemetrySink) : unit =
        lock sync (fun () -> rollup <- Some aggregate)

    /// Back to the default per-guest isolation — guest records stop reaching
    /// the aggregate sink.
    let disableRollup () : unit = lock sync (fun () -> rollup <- None)

    // ─── Warn/trace channel ─────────────────────────────────────────

    let private record (level: GuestDiagnosticLevel) (scopeId: string) (message: string) : unit =
        let entry =
            { ScopeId = scopeId
              Level = level
              Message = message
              Timestamp = DateTimeOffset.UtcNow }

        let echoSeam =
            lock sync (fun () ->
                let buffer =
                    match diagnostics.TryGetValue scopeId with
                    | true, b -> b
                    | false, _ ->
                        let b = ResizeArray<GuestDiagnostic>()
                        diagnostics[scopeId] <- b
                        b

                if buffer.Count >= diagnosticCapacity then
                    buffer.RemoveAt 0

                buffer.Add entry
                echo)

        // Echo outside the lock — the host's emit seam is arbitrary code.
        echoSeam |> Option.iter (fun emit -> emit entry)

    /// Buffer a scope-attributed warning for a guest. Never writes to a
    /// console; a host that wants guest lines mirrored opts in via
    /// `setDiagnosticEcho`.
    let warn (scopeId: string) (message: string) : unit =
        record GuestDiagnosticLevel.Warning scopeId message

    /// Buffer a scope-attributed trace line for a guest. Same isolation
    /// contract as `warn`.
    let trace (scopeId: string) (message: string) : unit =
        record GuestDiagnosticLevel.Trace scopeId message

    /// One guest's buffered diagnostics, insertion-ordered. Empty for a
    /// scope that never emitted.
    let diagnosticsFor (scopeId: string) : GuestDiagnostic list =
        lock sync (fun () ->
            match diagnostics.TryGetValue scopeId with
            | true, b -> List.ofSeq b
            | false, _ -> [])

    /// The opt-in aggregate read — every guest's diagnostics, grouped per
    /// scope (sorted by scope id; each group insertion-ordered). The
    /// "see all guests at once" view; per-guest isolation stays the default
    /// read via `diagnosticsFor`.
    let allDiagnostics () : (string * GuestDiagnostic list) list =
        lock sync (fun () ->
            diagnostics
            |> Seq.map (fun kv -> kv.Key, List.ofSeq kv.Value)
            |> Seq.sortBy fst
            |> List.ofSeq)

    /// Opt into mirroring every guest warn/trace line to the host's emit
    /// seam (scope-attributed, called after the line is buffered). One echo
    /// seam per process — the host owns it.
    let setDiagnosticEcho (emit: GuestDiagnostic -> unit) : unit = lock sync (fun () -> echo <- Some emit)

    /// Remove the echo seam — back to buffered isolation.
    let clearDiagnosticEcho () : unit = lock sync (fun () -> echo <- None)

    // ─── Enumeration ────────────────────────────────────────────────

    /// Every guest scope currently observable — the union of scopes with a
    /// registered sink and scopes that have buffered diagnostics. Sorted for
    /// stable selector rendering.
    let scopes () : string list =
        lock sync (fun () -> Seq.append sinks.Keys diagnostics.Keys |> Seq.distinct |> Seq.sort |> List.ofSeq)

    // ─── Test-only reset ────────────────────────────────────────────

    /// Clear every registration, buffer, and opt-in — for test isolation.
    /// Production deployments never call this.
    let __resetForTest () : unit =
        lock sync (fun () ->
            sinks.Clear()
            diagnostics.Clear()
            rollup <- None
            echo <- None)
