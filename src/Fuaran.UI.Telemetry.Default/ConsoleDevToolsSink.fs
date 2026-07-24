namespace Fuaran.UI.Telemetry.Default

open System
open System.Globalization
open Fuaran.UI.Telemetry.Abstractions

// ============================================================================
//  ConsoleDevToolsSink — the "passive narration" half of the live-debug console
//  (Phase 91). Pretty-prints every already-emitted telemetry beacon — op-apply
//  outcomes, authorizer denials, render failures — as a grouped/severity-tagged
//  record as it fires, so a developer watching a live Fuaran app sees a running,
//  human-scannable log of what the AI (or an in-page `apply`) just did and
//  whether it was allowed. The pull counterpart is Phase 90's `window.__fuaran`
//  REPL; this is the push side.
//
//  Zero new contracts (FGP 5): it is a pure read-projection of the canonical
//  telemetry stream — it renders only the existing `OpApplyTelemetry` /
//  `DenyTelemetry` / `RenderFailureTelemetry` records that already ship through
//  `IFuaranTelemetrySink`, and never gates dispatch.
//
//  Output goes through an injectable `IDevToolsConsoleWriter` seam, never raw
//  `Console.*` from the sink (FGP 4). The default `.NET` writer renders an
//  indented, severity-tagged group to stdout (and, under Fable's `System.Console`
//  shim, to `console.log`). A browser host that wants true `console.group` /
//  `console.table` collapsible records injects its own writer mapping a group to
//  those browser APIs — keeping this `.NET` package free of any Fable/browser
//  dependency while leaving the grouped browser rendering pluggable (the
//  `fuaran-live` Console tab, Phase 94, is the intended injector).
// ============================================================================

/// Severity classifier for a rendered record. `Applied` op-applies are `Info`;
/// failed applies + authorizer denials are `Warn`; render failures are `Error`.
/// Drives the `MinSeverity` filter + the writer's per-level styling.
[<RequireQualifiedAccess>]
type DevToolsLevel =
    | Info
    | Warn
    | Error

[<RequireQualifiedAccess>]
module DevToolsLevel =
    /// Ascending rank — `Info` < `Warn` < `Error` — for the `MinSeverity` gate.
    let rank (level: DevToolsLevel) : int =
        match level with
        | DevToolsLevel.Info -> 0
        | DevToolsLevel.Warn -> 1
        | DevToolsLevel.Error -> 2

    /// Stable short name for log filters / browser-writer styling.
    let name (level: DevToolsLevel) : string =
        match level with
        | DevToolsLevel.Info -> "info"
        | DevToolsLevel.Warn -> "warn"
        | DevToolsLevel.Error -> "error"

/// The injectable console-group writer seam. One `Group` call renders one
/// telemetry record: a `header` line plus its detail `rows` (key/value pairs),
/// tagged with the record's `level`. The sink writes ONLY through this seam, so
/// a host swaps the rendering (stdout, `console.group`/`console.table`, a test
/// shim) without touching the sink's record-to-format logic.
type IDevToolsConsoleWriter =
    abstract member Group: level: DevToolsLevel * header: string * rows: (string * string) list -> unit

/// Default `.NET` writer — renders a group as a severity-tagged header line
/// followed by indented `key: value` rows, all prefixed `[fuaran.devtools]` so
/// log filters can target it without colliding with unrelated `Console` callers.
/// Best-effort: a transiently-unwritable stdout never poisons the apply path.
type ConsoleDevToolsWriter() =
    interface IDevToolsConsoleWriter with
        member _.Group(level: DevToolsLevel, header: string, rows: (string * string) list) : unit =
            try
                let tag =
                    match level with
                    | DevToolsLevel.Info -> "▸"
                    | DevToolsLevel.Warn -> "⚠"
                    | DevToolsLevel.Error -> "✖"

                Console.WriteLine(sprintf "[fuaran.devtools] %s %s" tag header)

                for key, value in rows do
                    Console.WriteLine(sprintf "    %s: %s" key value)
            with _ ->
                ()

/// Construction-time filter knobs. Toggle whole record types off, and/or raise
/// the `MinSeverity` floor so the sink runs noisily in dev (`defaults`) and
/// quietly in a near-prod diagnostic build (`denialsAndFailuresOnly`).
type ConsoleDevToolsOptions =
    { ShowOpApply: bool
      ShowDeny: bool
      ShowRenderFailure: bool
      ShowProviderCall: bool
      ShowCacheStat: bool
      MinSeverity: DevToolsLevel }

[<RequireQualifiedAccess>]
module ConsoleDevToolsOptions =
    /// Show everything at every severity — the dev default.
    let defaults: ConsoleDevToolsOptions =
        { ShowOpApply = true
          ShowDeny = true
          ShowRenderFailure = true
          ShowProviderCall = true
          ShowCacheStat = true
          MinSeverity = DevToolsLevel.Info }

    /// Quiet diagnostic mode — successful applies suppressed (both by the
    /// `ShowOpApply = false` toggle and the `Warn` floor); only denials +
    /// failures narrate. Provider calls stay on: a successful call is `Info`
    /// (suppressed by the `Warn` floor), a failed call is `Warn` (narrated).
    let denialsAndFailuresOnly: ConsoleDevToolsOptions =
        { ShowOpApply = false
          ShowDeny = true
          ShowRenderFailure = true
          ShowProviderCall = true
          // Cache stats are pure-performance signal (Info); the `Warn` floor
          // suppresses them in this quiet mode without needing the toggle off.
          ShowCacheStat = true
          MinSeverity = DevToolsLevel.Warn }

[<RequireQualifiedAccess>]
module private Render =

    let isoTimestamp (t: DateTimeOffset) : string =
        t.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)

    let opt (value: string option) : string =
        match value with
        | Some v -> v
        | None -> "-"

    /// `(level, short outcome word)` for an op-apply outcome. `Applied` is the
    /// only `Info` outcome; every failure mode is `Warn`.
    let outcome (o: OpOutcome) : DevToolsLevel * string =
        match o with
        | OpOutcome.Applied -> DevToolsLevel.Info, "applied"
        | OpOutcome.DecoderRejected reason -> DevToolsLevel.Warn, sprintf "decoder-rejected:%s" reason
        | OpOutcome.NodeNotFound nodeId -> DevToolsLevel.Warn, sprintf "node-not-found:%s" nodeId
        | OpOutcome.ApplyEngineError detail -> DevToolsLevel.Warn, sprintf "apply-engine-error:%s" detail

    let opApply (t: OpApplyTelemetry) : DevToolsLevel * string * (string * string) list =
        let level, word = outcome t.Outcome

        let header =
            sprintf "op-apply seq=%d %s → %s (%.3fms)" t.Sequence (OpKind.name t.OpKind) word t.TimeToApplyMs

        let rows =
            [ "stream", t.StreamId
              "nodeId", opt t.NodeId
              "prompt", opt t.PromptId
              "user", t.UserId
              "ts", isoTimestamp t.Timestamp ]

        level, header, rows

    let deny (t: DenyTelemetry) : DevToolsLevel * string * (string * string) list =
        let header = sprintf "deny tool=%s — %s" t.ToolName t.Reason

        let rows =
            [ "module", opt t.ActiveModule
              "page", opt t.ActivePage
              "prompt", opt t.PromptId
              "user", t.UserId
              "ts", isoTimestamp t.Timestamp ]

        DevToolsLevel.Warn, header, rows

    let tokens (usage: ProviderTokenUsage option) : string =
        match usage with
        | Some u -> sprintf "%d in / %d out" u.InputTokens u.OutputTokens
        | None -> "-"

    let providerCall (t: ProviderCallTelemetry) : DevToolsLevel * string * (string * string) list =
        // A successful call narrates at Info; any failure outcome is Warn.
        let level =
            if ProviderCallOutcome.isSuccess t.Outcome then
                DevToolsLevel.Info
            else
                DevToolsLevel.Warn

        let header =
            sprintf
                "provider-call %s/%s %s → %s (%.3fms)"
                t.ProviderId
                t.ModelId
                (ProviderOperation.name t.Operation)
                (ProviderCallOutcome.name t.Outcome)
                t.LatencyMs

        let rows =
            [ "tokens", tokens t.TokenUsage
              "session", opt t.SessionId
              "prompt", opt t.PromptId
              "user", t.UserId
              "ts", isoTimestamp t.Timestamp ]

        level, header, rows

    let renderFailure (t: RenderFailureTelemetry) : DevToolsLevel * string * (string * string) list =
        let header =
            sprintf "render-failure nodeId=%s (%s) [%s]" t.NodeId t.NodeKindName (RenderFailureSource.name t.CaughtBy)

        let rows =
            [ "message", t.ErrorMessage
              "correlation", t.CorrelationId
              "prompt", opt t.PromptId
              "user", opt t.UserId
              "ts", isoTimestamp t.Timestamp ]

        DevToolsLevel.Error, header, rows

    let cacheStat (t: CacheStatTelemetry) : DevToolsLevel * string * (string * string) list =
        // Cache behaviour is pure-performance signal — always Info (the
        // `MinSeverity` floor suppresses it in quiet diagnostic modes).
        let recomputedRow =
            match t.Outcome with
            | CacheOutcome.Incremental n -> [ "recomputed", string n ]
            | _ -> []

        let header =
            sprintf "cache-stat %s %s (%d/%d)" t.CacheName (CacheOutcome.name t.Outcome) t.Size t.Capacity

        let rows = recomputedRow @ [ "ts", isoTimestamp t.Timestamp ]

        DevToolsLevel.Info, header, rows

/// A presentational `IFuaranTelemetrySink` that narrates the telemetry stream as
/// grouped, severity-tagged DevTools-console records. Construct with the default
/// `.NET` writer (`create` / `createWith`) or inject a browser/console-group
/// writer (`createWithWriter`). Never throws — telemetry is best-effort by
/// contract.
type ConsoleDevToolsSink(options: ConsoleDevToolsOptions, writer: IDevToolsConsoleWriter) =

    let passesSeverity (level: DevToolsLevel) : bool =
        DevToolsLevel.rank level >= DevToolsLevel.rank options.MinSeverity

    let emit (enabled: bool) (level: DevToolsLevel, header: string, rows: (string * string) list) : unit =
        if enabled && passesSeverity level then
            try
                writer.Group(level, header, rows)
            with _ ->
                // The writer is best-effort; a misbehaving writer must never
                // poison the apply / dispatch path it observes.
                ()

    interface IFuaranTelemetrySink with
        member _.RecordOpApply(telemetry: OpApplyTelemetry) : unit =
            emit options.ShowOpApply (Render.opApply telemetry)

        member _.RecordDeny(telemetry: DenyTelemetry) : unit =
            emit options.ShowDeny (Render.deny telemetry)

        member _.RecordRenderFailure(telemetry: RenderFailureTelemetry) : unit =
            emit options.ShowRenderFailure (Render.renderFailure telemetry)

        member _.RecordProviderCall(telemetry: ProviderCallTelemetry) : unit =
            emit options.ShowProviderCall (Render.providerCall telemetry)

        member _.RecordCacheStat(telemetry: CacheStatTelemetry) : unit =
            emit options.ShowCacheStat (Render.cacheStat telemetry)

[<RequireQualifiedAccess>]
module ConsoleDevToolsSink =
    /// Fresh sink with the dev defaults + the default `.NET` console writer.
    let create () : IFuaranTelemetrySink =
        upcast ConsoleDevToolsSink(ConsoleDevToolsOptions.defaults, ConsoleDevToolsWriter())

    /// Fresh sink with host-tuned filter options + the default `.NET` writer.
    let createWith (options: ConsoleDevToolsOptions) : IFuaranTelemetrySink =
        upcast ConsoleDevToolsSink(options, ConsoleDevToolsWriter())

    /// Fresh sink with host-tuned options + an injected writer (e.g. a browser
    /// `console.group` / `console.table` writer, or a test-capture shim).
    let createWithWriter (options: ConsoleDevToolsOptions, writer: IDevToolsConsoleWriter) : IFuaranTelemetrySink =
        upcast ConsoleDevToolsSink(options, writer)
