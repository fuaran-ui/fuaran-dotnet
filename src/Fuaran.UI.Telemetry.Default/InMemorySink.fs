namespace Fuaran.UI.Telemetry.Default

open System.Collections.Generic
open Fuaran.UI.Telemetry.Abstractions

// ============================================================================
//  InMemorySink — per-process ring buffer for tests + ad-hoc inspection.
//
//  Two ResizeArray buffers (op-apply, deny) guarded by one lock. The
//  buffers are bounded — once `Capacity` records are stored, the oldest
//  is dropped on the next `Record*` call (ring semantics). The default
//  capacity (`Defaults.capacity`) is 10_000 per stream which is large
//  enough for the test suite + a multi-thousand-op authoring session
//  without runaway growth.
//
//  Buffer reads are exposed via `Snapshot()` / `OpApplyRecords` /
//  `DenyRecords` properties — NOT on the `IFuaranTelemetrySink` interface
//  (the interface is sink-only by contract; readers are a host-side
//  concern). Tests + the drift detector consume the typed properties
//  directly.
// ============================================================================

[<RequireQualifiedAccess>]
module Defaults =
    /// Default ring-buffer capacity per sink instance.
    let capacity = 10_000

type InMemorySink(?capacity: int) =

    let capacity = defaultArg capacity Defaults.capacity
    let opApplies = ResizeArray<OpApplyTelemetry>(capacity)
    let denies = ResizeArray<DenyTelemetry>(capacity)
    let renderFailures = ResizeArray<RenderFailureTelemetry>(capacity)
    let providerCalls = ResizeArray<ProviderCallTelemetry>(capacity)
    let cacheStats = ResizeArray<CacheStatTelemetry>(capacity)
    // Phase 330: the fourth leg of the interaction-correlation spine.
    let validateOutcomes = ResizeArray<ValidateOutcomeTelemetry>(capacity)
    let lockObj = obj ()

    let appendBounded (buf: ResizeArray<_>) (record: _) =
        // Ring semantics: when the buffer is full, drop the oldest.
        // RemoveAt(0) is O(n) but n is bounded by `capacity` and the
        // sink is fire-and-forget — the cost is amortised across all
        // ops between resizes.
        if buf.Count >= capacity then
            buf.RemoveAt 0

        buf.Add record

    interface IFuaranTelemetrySink with
        member _.RecordOpApply telemetry =
            lock lockObj (fun () -> appendBounded opApplies telemetry)

        member _.RecordDeny telemetry =
            lock lockObj (fun () -> appendBounded denies telemetry)

        member _.RecordRenderFailure telemetry =
            lock lockObj (fun () -> appendBounded renderFailures telemetry)

        member _.RecordProviderCall telemetry =
            lock lockObj (fun () -> appendBounded providerCalls telemetry)

        member _.RecordCacheStat telemetry =
            lock lockObj (fun () -> appendBounded cacheStats telemetry)

        member _.RecordValidateOutcome telemetry =
            lock lockObj (fun () -> appendBounded validateOutcomes telemetry)

    /// Snapshot of every op-apply record currently in the buffer, in
    /// insertion order. Returns a fresh list so callers can hold it
    /// outside the lock.
    member _.OpApplyRecords: OpApplyTelemetry list =
        lock lockObj (fun () -> List.ofSeq opApplies)

    /// Snapshot of every deny record currently in the buffer, in
    /// insertion order. Fresh list, lock-free for callers.
    member _.DenyRecords: DenyTelemetry list = lock lockObj (fun () -> List.ofSeq denies)

    /// Snapshot of every render-failure record currently in the
    /// buffer, in insertion order. Fresh list, lock-free for callers.
    member _.RenderFailureRecords: RenderFailureTelemetry list =
        lock lockObj (fun () -> List.ofSeq renderFailures)

    /// Snapshot of every provider-call record currently in the buffer, in
    /// insertion order. Fresh list, lock-free for callers.
    member _.ProviderCallRecords: ProviderCallTelemetry list =
        lock lockObj (fun () -> List.ofSeq providerCalls)

    /// Snapshot of every cache-stat record currently in the buffer, in
    /// insertion order. Fresh list, lock-free for callers.
    member _.CacheStatRecords: CacheStatTelemetry list =
        lock lockObj (fun () -> List.ofSeq cacheStats)

    /// Snapshot of every runtime-validate record currently in the buffer, in
    /// insertion order. Fresh list, lock-free for callers.
    member _.ValidateOutcomeRecords: ValidateOutcomeTelemetry list =
        lock lockObj (fun () -> List.ofSeq validateOutcomes)

    /// Reset all buffers — for test-isolation between cases.
    member _.Clear() : unit =
        lock lockObj (fun () ->
            opApplies.Clear()
            denies.Clear()
            renderFailures.Clear()
            providerCalls.Clear()
            cacheStats.Clear()
            validateOutcomes.Clear())

[<RequireQualifiedAccess>]
module InMemorySink =
    /// Convenience factory returning a fresh sink as the abstraction interface.
    /// Tests that need to read the buffers should construct `InMemorySink()`
    /// directly so the typed properties are accessible.
    let create () : IFuaranTelemetrySink = upcast InMemorySink()
