namespace Fuaran.UI.Telemetry.Default

open System
open Fuaran.UI.Telemetry.Abstractions

// ============================================================================
//  ConsoleSink — log every record to stdout via `System.Console.WriteLine`.
//
//  Useful for dev environments and small CLI tools where wiring a real
//  observability backend is overkill. Each line is `[fuaran.telemetry]
//  <prefix> <key>=<value> ...` so log filters can target it without
//  collisions against unrelated `Console.WriteLine` callers.
//
//  Not Fable-compatible — `System.Console` does exist under Fable's
//  shim but lands at `console.log`, not the orchestration tier's
//  prefixed warn path. ConsoleSink lives in the Default package
//  (.NET-only) which keeps `Fuaran.UI.Telemetry.Abstractions`
//  Fable-clean.
//
//  Sync emission — fine for dev; not for production hot paths. A
//  production-grade sink (a platform host's telemetry adapter,
//  a Prometheus exporter, etc.) buffers + flushes asynchronously and
//  goes in a host-specific package.
// ============================================================================

[<RequireQualifiedAccess>]
module private Format =

    let private isoTimestamp (t: DateTimeOffset) : string =
        t.UtcDateTime.ToString("O", Globalization.CultureInfo.InvariantCulture)

    let private opt (value: string option) : string =
        match value with
        | Some v -> v
        | None -> "-"

    let private outcome (o: OpOutcome) : string =
        match o with
        | OpOutcome.Applied -> "applied"
        | OpOutcome.DecoderRejected reason -> sprintf "decoder-rejected:%s" reason
        | OpOutcome.NodeNotFound nodeId -> sprintf "node-not-found:%s" nodeId
        | OpOutcome.ApplyEngineError detail -> sprintf "apply-engine-error:%s" detail

    let opApply (t: OpApplyTelemetry) : string =
        sprintf
            "[fuaran.telemetry] op-apply stream=%s seq=%d kind=%s nodeId=%s outcome=%s ms=%.3f prompt=%s user=%s ts=%s"
            t.StreamId
            t.Sequence
            (OpKind.name t.OpKind)
            (opt t.NodeId)
            (outcome t.Outcome)
            t.TimeToApplyMs
            (opt t.PromptId)
            t.UserId
            (isoTimestamp t.Timestamp)

    let deny (t: DenyTelemetry) : string =
        sprintf
            "[fuaran.telemetry] deny tool=%s reason=%s module=%s page=%s prompt=%s user=%s ts=%s"
            t.ToolName
            t.Reason
            (opt t.ActiveModule)
            (opt t.ActivePage)
            (opt t.PromptId)
            t.UserId
            (isoTimestamp t.Timestamp)

    let private tokens (usage: ProviderTokenUsage option) : string =
        match usage with
        | Some u -> sprintf "%d/%d" u.InputTokens u.OutputTokens
        | None -> "-"

    let providerCall (t: ProviderCallTelemetry) : string =
        sprintf
            "[fuaran.telemetry] provider-call provider=%s model=%s op=%s outcome=%s ms=%.3f tokens=%s session=%s prompt=%s user=%s ts=%s"
            t.ProviderId
            t.ModelId
            (ProviderOperation.name t.Operation)
            (ProviderCallOutcome.name t.Outcome)
            t.LatencyMs
            (tokens t.TokenUsage)
            (opt t.SessionId)
            (opt t.PromptId)
            t.UserId
            (isoTimestamp t.Timestamp)

    let renderFailure (t: RenderFailureTelemetry) : string =
        sprintf
            "[fuaran.telemetry] render-failure nodeId=%s kind=%s source=%s message=%s correlation=%s prompt=%s user=%s ts=%s"
            t.NodeId
            t.NodeKindName
            (RenderFailureSource.name t.CaughtBy)
            t.ErrorMessage
            t.CorrelationId
            (opt t.PromptId)
            (opt t.UserId)
            (isoTimestamp t.Timestamp)

    let private recomputed (o: CacheOutcome) : string =
        match o with
        | CacheOutcome.Incremental n -> string n
        | _ -> "-"

    let cacheStat (t: CacheStatTelemetry) : string =
        sprintf
            "[fuaran.telemetry] cache-stat cache=%s outcome=%s recomputed=%s size=%d/%d ts=%s"
            t.CacheName
            (CacheOutcome.name t.Outcome)
            (recomputed t.Outcome)
            t.Size
            t.Capacity
            (isoTimestamp t.Timestamp)

type ConsoleSink() =
    interface IFuaranTelemetrySink with
        member _.RecordOpApply telemetry =
            // Best-effort — swallow any I/O failure so a transiently
            // unwritable stdout (closed pipe, redirected to /dev/null on
            // a host that's gone away) doesn't poison the apply path.
            try
                Console.WriteLine(Format.opApply telemetry)
            with _ ->
                ()

        member _.RecordDeny telemetry =
            try
                Console.WriteLine(Format.deny telemetry)
            with _ ->
                ()

        member _.RecordRenderFailure telemetry =
            try
                Console.WriteLine(Format.renderFailure telemetry)
            with _ ->
                ()

        member _.RecordProviderCall telemetry =
            try
                Console.WriteLine(Format.providerCall telemetry)
            with _ ->
                ()

        member _.RecordCacheStat telemetry =
            try
                Console.WriteLine(Format.cacheStat telemetry)
            with _ ->
                ()

[<RequireQualifiedAccess>]
module ConsoleSink =
    /// Convenience factory returning a fresh sink as the abstraction interface.
    let create () : IFuaranTelemetrySink = upcast ConsoleSink()
