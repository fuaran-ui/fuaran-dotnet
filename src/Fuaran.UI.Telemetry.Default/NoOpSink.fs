namespace Fuaran.UI.Telemetry.Default

open Fuaran.UI.Telemetry.Abstractions

// ============================================================================
//  NoOpSink — the explicit "I don't care" sink.
//
//  The default a host installs when it does not supply a real sink. Every
//  member is a unit-returning no-op; the call site pays a single virtual
//  dispatch per event and nothing else.
//
//  Picking NoOpSink as the install default (rather than `None : IFuaranTelemetrySink option`)
//  keeps the call shape `sink.RecordOpApply tel` uniform — a host's dispatch
//  points and the apply-engine dispatch point don't need to wrap in
//  `Option.iter`.
// ============================================================================

type NoOpSink() =
    interface IFuaranTelemetrySink with
        member _.RecordOpApply _ = ()
        member _.RecordDeny _ = ()
        member _.RecordRenderFailure _ = ()
        member _.RecordProviderCall _ = ()
        member _.RecordCacheStat _ = ()
        member _.RecordValidateOutcome _ = ()

[<RequireQualifiedAccess>]
module NoOpSink =
    /// Convenience factory returning a fresh sink as the abstraction interface.
    let create () : IFuaranTelemetrySink = upcast NoOpSink()
