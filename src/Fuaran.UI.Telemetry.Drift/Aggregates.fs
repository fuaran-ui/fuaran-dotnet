module Fuaran.UI.Telemetry.Drift.Aggregates

open Fuaran.UI.Telemetry.Abstractions

// ============================================================================
//  Per-OpKind aggregate metrics — success rate, p50/p95 latency, top
//  decoder-rejection reasons, top NodeNotFound NodeIds.
// ============================================================================

/// One per-OpKind aggregate. Counts + percentiles are computed across the
/// records passed to `compute`; `topRejectionReasons` / `topMissingNodeIds`
/// are the most frequent values, capped at the `topN` passed to `compute`.
type OpKindAggregate =
    { OpKind: OpKind
      SampleCount: int
      AppliedCount: int
      SuccessRate: float
      LatencyP50Ms: float
      LatencyP95Ms: float
      TopRejectionReasons: (string * int) list
      TopMissingNodeIds: (string * int) list }

let private percentile (sorted: float array) (p: float) : float =
    if sorted.Length = 0 then
        0.0
    else
        let rank = p * float (sorted.Length - 1)
        let lower = int (floor rank)
        let upper = int (ceil rank)

        if lower = upper then
            sorted[lower]
        else
            let weight = rank - float lower
            sorted[lower] * (1.0 - weight) + sorted[upper] * weight

let private topNBy<'T when 'T: equality> (n: int) (values: 'T seq) : ('T * int) list =
    values
    |> Seq.countBy id
    |> Seq.sortByDescending snd
    |> Seq.truncate n
    |> Seq.toList

let private rejectionReason (outcome: OpOutcome) : string option =
    match outcome with
    | OpOutcome.DecoderRejected reason -> Some reason
    | _ -> None

let private missingNodeId (outcome: OpOutcome) : string option =
    match outcome with
    | OpOutcome.NodeNotFound id -> Some id
    | _ -> None

/// Aggregate `records` by OpKind, computing per-kind metrics. `topN`
/// caps the `TopRejectionReasons` / `TopMissingNodeIds` lists; pass `3`
/// for "top 3" reporting.
let compute (topN: int) (records: OpApplyTelemetry seq) : OpKindAggregate list =
    records
    |> Seq.groupBy _.OpKind
    |> Seq.map (fun (kind, group) ->
        let group = group |> Seq.toArray
        let count = group.Length

        let appliedCount =
            group
            |> Array.filter (fun r ->
                match r.Outcome with
                | OpOutcome.Applied -> true
                | _ -> false)
            |> Array.length

        let sortedLatencies = group |> Array.map _.TimeToApplyMs |> Array.sort

        let p50 = percentile sortedLatencies 0.50
        let p95 = percentile sortedLatencies 0.95

        let rejections =
            group |> Array.choose (fun r -> rejectionReason r.Outcome) |> topNBy topN

        let missing =
            group |> Array.choose (fun r -> missingNodeId r.Outcome) |> topNBy topN

        { OpKind = kind
          SampleCount = count
          AppliedCount = appliedCount
          SuccessRate = (if count = 0 then 0.0 else float appliedCount / float count)
          LatencyP50Ms = p50
          LatencyP95Ms = p95
          TopRejectionReasons = rejections
          TopMissingNodeIds = missing })
    |> Seq.sortBy (fun a -> OpKind.name a.OpKind)
    |> Seq.toList
