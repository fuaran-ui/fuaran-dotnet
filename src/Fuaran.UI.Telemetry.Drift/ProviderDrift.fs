module Fuaran.UI.Telemetry.Drift.ProviderDrift

open Fuaran.UI.Telemetry.Abstractions

// ============================================================================
//  Provider-reliability drift detector (Phase 171).
//
//  The success-rate twin of `Detect` (op-apply drift), but over the dedicated
//  `ProviderCallTelemetry` stream rather than `OpApplyTelemetry`. Keyed by
//  provider id, it compares the provider-call success rate between a `baseline`
//  window and a `current` window and flags a regression when the drop exceeds
//  the operator-tunable threshold — the SAME `DriftThresholds` mechanics the
//  op-apply detector uses (default 10pp week-on-week, `MinBaselineSamples`
//  noise floor).
//
//  This is the "separate stream" the Phase 171 channel split unlocks: provider
//  transport failures (timeouts / 429s / malformed-200s) drive THIS signal,
//  not the denial-rate signal. The denial-rate signal is computed over
//  `DenyTelemetry` (policy denials) only — provider outcomes never reach it,
//  because the engine emits them through `RecordProviderCall`, never the deny
//  channel (the open `CONFLICTS-AND-GAPS.md` tension this resolves).
// ============================================================================

/// One finding per provider id. The detector emits at most one finding per
/// provider per `run`.
type ProviderDriftFinding =
    | NoRegression of providerId: string * baselineSuccessRate: float * currentSuccessRate: float
    | Regression of providerId: string * baselineSuccessRate: float * currentSuccessRate: float * dropPct: float
    | InsufficientData of providerId: string * baselineSampleCount: int

/// Compare `current` against `baseline` per provider id, returning one finding
/// per provider that appears in either window. Provider ids present in only one
/// window are reported as `InsufficientData` against the other. Mirrors
/// `Detect.run`'s shape exactly so operators read both signals identically.
let run
    (thresholds: DriftThresholds)
    (baseline: ProviderCallTelemetry seq)
    (current: ProviderCallTelemetry seq)
    : ProviderDriftFinding list =
    let baselineByProvider = baseline |> Seq.groupBy _.ProviderId |> Map.ofSeq

    let currentByProvider = current |> Seq.groupBy _.ProviderId |> Map.ofSeq

    let allProviders =
        Set.union (baselineByProvider.Keys |> Set.ofSeq) (currentByProvider.Keys |> Set.ofSeq)

    let successRate (records: ProviderCallTelemetry seq) : int * float =
        let arr = records |> Seq.toArray
        let count = arr.Length

        let succeeded =
            arr
            |> Array.filter (fun r -> ProviderCallOutcome.isSuccess r.Outcome)
            |> Array.length

        let rate = if count = 0 then 0.0 else float succeeded / float count

        count, rate

    allProviders
    |> Seq.map (fun providerId ->
        let baselineRecords =
            baselineByProvider |> Map.tryFind providerId |> Option.defaultValue Seq.empty

        let currentRecords =
            currentByProvider |> Map.tryFind providerId |> Option.defaultValue Seq.empty

        let baselineCount, baselineRate = successRate baselineRecords
        let _, currentRate = successRate currentRecords

        if baselineCount < thresholds.MinBaselineSamples then
            InsufficientData(providerId, baselineCount)
        else
            let drop = baselineRate - currentRate

            if drop >= thresholds.SuccessRateRegressionPct then
                Regression(providerId, baselineRate, currentRate, drop)
            else
                NoRegression(providerId, baselineRate, currentRate))
    |> Seq.sortBy (fun finding ->
        match finding with
        | NoRegression(p, _, _) -> p
        | Regression(p, _, _, _) -> p
        | InsufficientData(p, _) -> p)
    |> Seq.toList

/// Format one finding as a human-readable line, matching `Detect.formatFinding`.
let formatFinding (finding: ProviderDriftFinding) : string =
    match finding with
    | NoRegression(providerId, baseline, current) ->
        sprintf "  %-18s OK              baseline=%.1f%% current=%.1f%%" providerId (baseline * 100.0) (current * 100.0)
    | Regression(providerId, baseline, current, drop) ->
        sprintf
            "  %-18s REGRESSION      baseline=%.1f%% current=%.1f%% drop=%.1fpp"
            providerId
            (baseline * 100.0)
            (current * 100.0)
            (drop * 100.0)
    | InsufficientData(providerId, count) -> sprintf "  %-18s insufficient    baseline-samples=%d" providerId count
