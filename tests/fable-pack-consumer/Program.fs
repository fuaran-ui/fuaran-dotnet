/// The packed-consumer leg (Phase 2128): a Fable consumer of the published telemetry packages.
///
/// Every call below hands a sink built by `Fuaran.UI.Telemetry.Default` to code typed against
/// `Fuaran.UI.Telemetry.Abstractions`. Before Phase 2128 the Default package shipped no Fable
/// sources, so Fable read it as a compiled assembly whose types point at the Abstractions ASSEMBLY,
/// which Fable had replaced with that package's sources: each crossing below was FS0074
/// ("IFuaranTelemetrySink is defined in an assembly that is not referenced").
module FablePackConsumer.Program

open Fuaran.UI.Telemetry.Abstractions
open Fuaran.UI.Telemetry.Default

/// A host seam typed against the abstraction, as a hosting tier installs a sink.
let install (sink: IFuaranTelemetrySink) : IFuaranTelemetrySink = sink

let sinks: IFuaranTelemetrySink list =
    [ install (NoOpSink.create ())
      install (InMemorySink() :> IFuaranTelemetrySink)
      install (ConsoleDevToolsSink.create ()) ]

let sinkCount = List.length sinks
