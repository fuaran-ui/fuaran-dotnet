module Fuaran.UI.Telemetry.Drift.Detect

open Fuaran.UI.Telemetry.Abstractions

// ============================================================================
//  Window-over-window regression detector.
//
//  Compares per-OpKind success rate between a `baseline` window (typically
//  the prior week) and a `current` window (typically the latest week).
//  A regression is "the current success rate is N percentage points
//  below baseline, where N >= threshold". The detector returns one
//  finding per OpKind whose drop exceeds the threshold.
//
//  Insufficient-data branch: when the baseline window has fewer than
//  `MinBaselineSamples` records (default 50), the comparison is too
//  noisy to act on — the detector emits a `InsufficientData` outcome
//  rather than a regression warning. Operators running test harnesses
//  lower the threshold via `DriftThresholds.MinBaselineSamples`.
// ============================================================================

/// One finding per OpKind. The detector emits at most one finding per
/// kind per `run`.
type DriftFinding =
    | NoRegression of opKind: OpKind * baselineSuccessRate: float * currentSuccessRate: float
    | Regression of opKind: OpKind * baselineSuccessRate: float * currentSuccessRate: float * dropPct: float
    | InsufficientData of opKind: OpKind * baselineSampleCount: int

/// Compare `current` against `baseline` per OpKind, returning one
/// finding per kind that appears in either window. OpKinds present in
/// only one window are reported as InsufficientData against the other.
let run
    (thresholds: DriftThresholds)
    (baseline: OpApplyTelemetry seq)
    (current: OpApplyTelemetry seq)
    : DriftFinding list =
    let baselineByKind = baseline |> Seq.groupBy _.OpKind |> Map.ofSeq

    let currentByKind = current |> Seq.groupBy _.OpKind |> Map.ofSeq

    let allKinds =
        Set.union (baselineByKind.Keys |> Set.ofSeq) (currentByKind.Keys |> Set.ofSeq)

    let successRate (records: OpApplyTelemetry seq) : int * float =
        let arr = records |> Seq.toArray
        let count = arr.Length

        let applied =
            arr
            |> Array.filter (fun r ->
                match r.Outcome with
                | OpOutcome.Applied -> true
                | _ -> false)
            |> Array.length

        let rate = if count = 0 then 0.0 else float applied / float count

        count, rate

    allKinds
    |> Seq.map (fun kind ->
        let baselineRecords =
            baselineByKind |> Map.tryFind kind |> Option.defaultValue Seq.empty

        let currentRecords =
            currentByKind |> Map.tryFind kind |> Option.defaultValue Seq.empty

        let baselineCount, baselineRate = successRate baselineRecords
        let _, currentRate = successRate currentRecords

        if baselineCount < thresholds.MinBaselineSamples then
            InsufficientData(kind, baselineCount)
        else
            let drop = baselineRate - currentRate

            if drop >= thresholds.SuccessRateRegressionPct then
                Regression(kind, baselineRate, currentRate, drop)
            else
                NoRegression(kind, baselineRate, currentRate))
    |> Seq.sortBy (fun finding ->
        let kind =
            match finding with
            | NoRegression(k, _, _) -> k
            | Regression(k, _, _, _) -> k
            | InsufficientData(k, _) -> k

        OpKind.name kind)
    |> Seq.toList

/// Format one finding as a human-readable line.
let formatFinding (finding: DriftFinding) : string =
    match finding with
    | NoRegression(kind, baseline, current) ->
        sprintf
            "  %-18s OK              baseline=%.1f%% current=%.1f%%"
            (OpKind.name kind)
            (baseline * 100.0)
            (current * 100.0)
    | Regression(kind, baseline, current, drop) ->
        sprintf
            "  %-18s REGRESSION      baseline=%.1f%% current=%.1f%% drop=%.1fpp"
            (OpKind.name kind)
            (baseline * 100.0)
            (current * 100.0)
            (drop * 100.0)
    | InsufficientData(kind, count) -> sprintf "  %-18s insufficient    baseline-samples=%d" (OpKind.name kind) count
