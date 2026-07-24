namespace Fuaran.UI.Telemetry.Default

open Fuaran.UI.Telemetry.Abstractions

// ============================================================================
//  NoOpSink — the explicit "I don't care" sink.
//
//  Default at `Orchestration.installWithTelemetry` when the host doesn't
//  supply a real sink. Both `RecordOpApply` and `RecordDeny` are unit-
//  returning no-ops; the call site pays a single virtual dispatch per
//  event and nothing else.
//
//  Picking NoOpSink as the install default (rather than `None : IFuaranTelemetrySink option`)
//  keeps the call shape `sink.RecordOpApply tel` uniform — the
//  AuthorizingRuntime deny branch and the apply-engine dispatch point
//  don't need to wrap in `Option.iter`.
// ============================================================================

type NoOpSink() =
    interface IFuaranTelemetrySink with
        member _.RecordOpApply _ = ()
        member _.RecordDeny _ = ()
        member _.RecordRenderFailure _ = ()
        member _.RecordProviderCall _ = ()
        member _.RecordCacheStat _ = ()

[<RequireQualifiedAccess>]
module NoOpSink =
    /// Convenience factory returning a fresh sink as the abstraction interface.
    let create () : IFuaranTelemetrySink = upcast NoOpSink()
