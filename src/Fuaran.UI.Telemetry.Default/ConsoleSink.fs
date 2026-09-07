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
//
//  Phase 1532 — REDACTED BY DEFAULT. These lines carried the reader's user id in
//  clear and, in the failure-detail fields, unbounded free text taken from the
//  tree and the apply engine: a rejected decode's reason, an apply error's
//  detail, a render failure's message. All three quote node content. "Dev-only"
//  was a claim in the header above and nothing in the code, and a sink is a
//  two-line wiring change away from a production composition root.
//
//  So the default redacts the user id and truncates free text, and a host that
//  genuinely wants the whole record asks for it. The choice is a CONSTRUCTOR
//  argument rather than an ambient environment read, so what a deployment
//  discloses is decided where the sink is composed and is visible in that source.
// ============================================================================

/// How much of a telemetry record `ConsoleSink` writes.
///
/// A type rather than a bare `bool` at the seam, so a call site reads as what it
/// discloses rather than as `true`.
[<RequireQualifiedAccess>]
type ConsoleDisclosure =
    /// The default. The user id is replaced by a fixed marker and free-text
    /// detail is truncated. Every STRUCTURAL field — stream, sequence, op kind,
    /// node id, outcome class, timings, correlation and prompt ids — is written
    /// in full, so the lines stay as useful for diagnosis as they were.
    | Redacted
    /// Everything, verbatim — for a developer watching their own machine. Never
    /// the default, and never reached by accident: a host asks for it by name.
    | Verbose

[<RequireQualifiedAccess>]
module private Format =

    /// What a redacted user id reads as. A fixed marker rather than a hash: the
    /// records already carry prompt and correlation ids for correlation, so a
    /// per-user pseudonym would disclose linkability without being needed for it.
    [<Literal>]
    let RedactedUser = "<redacted>"

    /// The ceiling on a free-text field under `Redacted`. Long enough to carry a
    /// validator code and the shape of a message; short enough that a quoted node
    /// body cannot ride out on it.
    [<Literal>]
    let FreeTextLimit = 120

    let private isoTimestamp (t: DateTimeOffset) : string =
        t.UtcDateTime.ToString("O", Globalization.CultureInfo.InvariantCulture)

    let private opt (value: string option) : string =
        match value with
        | Some v -> v
        | None -> "-"

    /// Truncate a free-text field, MARKING that it was truncated. The marker is
    /// load-bearing: a silently-cut message reads as a complete one, and someone
    /// diagnosing from these lines would take the truncation for the message.
    let private freeText (disclosure: ConsoleDisclosure) (s: string) : string =
        match disclosure with
        | ConsoleDisclosure.Verbose -> s
        | ConsoleDisclosure.Redacted ->
            if s.Length <= FreeTextLimit then
                s
            else
                s.Substring(0, FreeTextLimit) + "…<truncated>"

    let private user (disclosure: ConsoleDisclosure) (userId: string) : string =
        match disclosure with
        | ConsoleDisclosure.Verbose -> userId
        | ConsoleDisclosure.Redacted -> RedactedUser

    let private outcome (disclosure: ConsoleDisclosure) (o: OpOutcome) : string =
        // The outcome CLASS is never redacted — it is the diagnostic content of
        // the line. Only the free-text payloads are; `NodeNotFound` carries a node
        // id, which is a tree ADDRESS rather than tree content.
        match o with
        | OpOutcome.Applied -> "applied"
        | OpOutcome.DecoderRejected reason -> sprintf "decoder-rejected:%s" (freeText disclosure reason)
        | OpOutcome.NodeNotFound nodeId -> sprintf "node-not-found:%s" nodeId
        | OpOutcome.ApplyEngineError detail -> sprintf "apply-engine-error:%s" (freeText disclosure detail)
        // Phase 1525 — the apply succeeded and the durable append did not.
        | OpOutcome.PersistLost reason -> sprintf "persist-lost:%s" (freeText disclosure reason)

    let opApply (disclosure: ConsoleDisclosure) (t: OpApplyTelemetry) : string =
        sprintf
            "[fuaran.telemetry] op-apply stream=%s seq=%d kind=%s nodeId=%s outcome=%s ms=%.3f prompt=%s user=%s ts=%s"
            t.StreamId
            t.Sequence
            (OpKind.name t.OpKind)
            (opt t.NodeId)
            (outcome disclosure t.Outcome)
            t.TimeToApplyMs
            (opt t.PromptId)
            (user disclosure t.UserId)
            (isoTimestamp t.Timestamp)

    let deny (disclosure: ConsoleDisclosure) (t: DenyTelemetry) : string =
        sprintf
            "[fuaran.telemetry] deny tool=%s reason=%s module=%s page=%s prompt=%s user=%s ts=%s"
            t.ToolName
            (freeText disclosure t.Reason)
            (opt t.ActiveModule)
            (opt t.ActivePage)
            (opt t.PromptId)
            (user disclosure t.UserId)
            (isoTimestamp t.Timestamp)

    let private tokens (usage: ProviderTokenUsage option) : string =
        match usage with
        | Some u -> sprintf "%d/%d" u.InputTokens u.OutputTokens
        | None -> "-"

    let providerCall (disclosure: ConsoleDisclosure) (t: ProviderCallTelemetry) : string =
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
            (user disclosure t.UserId)
            (isoTimestamp t.Timestamp)

    let renderFailure (disclosure: ConsoleDisclosure) (t: RenderFailureTelemetry) : string =
        sprintf
            "[fuaran.telemetry] render-failure nodeId=%s kind=%s source=%s message=%s correlation=%s prompt=%s user=%s ts=%s"
            t.NodeId
            t.NodeKindName
            (RenderFailureSource.name t.CaughtBy)
            (freeText disclosure t.ErrorMessage)
            t.CorrelationId
            (opt t.PromptId)
            // The one record whose user id is already optional; an absent one
            // stays `-` rather than becoming the redaction marker, so "no user was
            // attached" and "a user was attached and withheld" still read apart.
            (t.UserId |> Option.map (user disclosure) |> opt)
            (isoTimestamp t.Timestamp)

    let private recomputed (o: CacheOutcome) : string =
        match o with
        | CacheOutcome.Incremental n -> string n
        | _ -> "-"

    /// Phase 330 — the runtime-validate leg. `findings=?` for a `NotRun`
    /// outcome, never `0`: "we could not check" and "we checked and found
    /// nothing" must not read alike on a console someone is scanning.
    let validateOutcome (t: ValidateOutcomeTelemetry) : string =
        let findings =
            match ValidateOutcome.findingCount t.Outcome with
            | Some n -> string n
            | None -> "?"

        let detail =
            match t.Outcome with
            | ValidateOutcome.NotRun reason -> sprintf " reason=%s" reason
            | _ ->
                match t.TopCodes with
                | [] -> ""
                | codes -> " codes=" + String.concat "," codes

        sprintf
            "[fuaran.telemetry] validate outcome=%s findings=%s%s prompt=%s ts=%s"
            (ValidateOutcome.name t.Outcome)
            findings
            detail
            (Option.defaultValue "-" t.PromptId)
            (isoTimestamp t.Timestamp)

    let cacheStat (t: CacheStatTelemetry) : string =
        sprintf
            "[fuaran.telemetry] cache-stat cache=%s outcome=%s recomputed=%s size=%d/%d ts=%s"
            t.CacheName
            (CacheOutcome.name t.Outcome)
            (recomputed t.Outcome)
            t.Size
            t.Capacity
            (isoTimestamp t.Timestamp)

/// Writes every telemetry record to stdout. `disclosure` decides whether the
/// user id and the free-text failure detail are written verbatim; the
/// parameterless constructor is `Redacted`, so the safe posture is what a host
/// gets by writing `ConsoleSink()`.
type ConsoleSink(disclosure: ConsoleDisclosure) =
    /// The redacting sink — the default, and the only one reachable without
    /// naming what it discloses.
    new() = ConsoleSink(ConsoleDisclosure.Redacted)

    /// What this sink writes verbatim. Readable so a host (or a test) can assert
    /// the posture it composed rather than infer it from the output.
    member _.Disclosure = disclosure

    interface IFuaranTelemetrySink with
        member _.RecordOpApply telemetry =
            // Best-effort — swallow any I/O failure so a transiently
            // unwritable stdout (closed pipe, redirected to /dev/null on
            // a host that's gone away) doesn't poison the apply path.
            try
                Console.WriteLine(Format.opApply disclosure telemetry)
            with _ ->
                ()

        member _.RecordDeny telemetry =
            try
                Console.WriteLine(Format.deny disclosure telemetry)
            with _ ->
                ()

        member _.RecordRenderFailure telemetry =
            try
                Console.WriteLine(Format.renderFailure disclosure telemetry)
            with _ ->
                ()

        member _.RecordProviderCall telemetry =
            try
                Console.WriteLine(Format.providerCall disclosure telemetry)
            with _ ->
                ()

        member _.RecordCacheStat telemetry =
            try
                Console.WriteLine(Format.cacheStat telemetry)
            with _ ->
                ()

        member _.RecordValidateOutcome telemetry =
            try
                Console.WriteLine(Format.validateOutcome telemetry)
            with _ ->
                ()

[<RequireQualifiedAccess>]
module ConsoleSink =
    /// Convenience factory returning a fresh REDACTED sink as the abstraction
    /// interface — the user id withheld, free-text detail truncated.
    let create () : IFuaranTelemetrySink = upcast ConsoleSink()

    /// A sink that writes every record verbatim — user ids and untruncated
    /// failure detail included. For a developer watching their own machine.
    /// Composing this into a deployment that serves other people's readers is a
    /// disclosure decision, which is why it has to be written out.
    let createVerbose () : IFuaranTelemetrySink =
        upcast ConsoleSink(ConsoleDisclosure.Verbose)
