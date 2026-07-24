module Fuaran.UI.Telemetry.Drift.StyleDrift

open Fuaran.UI.StyleObserver
open Fuaran.UI.ThemeManifest

// ============================================================================
//  StyleFlag drift — semantic visual-regression across renders (Phase 151).
//
//  The op-apply drift detector (`Detect.run`) catches *behavioural*
//  regressions — "the apply success rate fell week-on-week". This module is
//  its resolved-style twin: it catches *visual* regressions — "this render
//  introduced legibility / budget violations that were absent in the prior
//  render". It is the Chromatic / Percy / Applitools idea made **semantic and
//  deterministic**: it diffs declared-invariant *flag sets* across two windows
//  of `StyleObservation` rather than screenshot pixels, so the result is
//  reproducible, cheap, CI-gateable, and reported in the manifest's own
//  vocabulary ("brand budget 9% → observed 22%") rather than "these pixels
//  changed".
//
//  **No new sink (FGP 5).** The style observations the detector windows over
//  are the ones `IStyleObserver.Subscribe` already emits (the observer's
//  existing emission path); a host collects a render's observations into a
//  window and feeds the prior + current windows here. This deliberately does
//  NOT extend `IFuaranTelemetrySink` with a style member — that interface is
//  implemented by downstream consumers (e.g. a no-op sink in a runtime tier),
//  so widening it would break implementers outside this repo.
//  The flag-set diff is a pure function; reuse beats a new sink.
//
//  **Deterministic by construction.** The diff is a set comparison keyed on
//  `(NodeId, StyleFlag.kind)`; identical render pairs produce identical
//  reports. No pixels, no VLM, no wall-clock.
// ============================================================================

/// Whether a `(NodeId, flag-kind)` violation appeared or disappeared between
/// the baseline and current windows.
[<RequireQualifiedAccess>]
type StyleDriftDirection =
    /// The violation is present in the current window and absent in the
    /// baseline — a newly-introduced regression.
    | Introduced
    /// The violation was present in the baseline and is absent in the
    /// current window — a fix.
    | Cleared

/// One `(NodeId, flag-kind)` transition between the two windows. `Flag` is the
/// payload-carrying flag from whichever window the transition is measured in
/// (current for `Introduced`, baseline for `Cleared`). `Weight` is the
/// severity weight — the violated invariant's `Invariant.Weight` (Phase 145)
/// when the manifest declares one for the flag, else the default `1.0`.
type StyleDriftEntry =
    { NodeId: string
      FlagKind: string
      Flag: StyleFlag
      Direction: StyleDriftDirection
      Weight: float }

/// The window-over-window style-drift aggregate — the resolved-style twin of
/// `OpKindAggregate`. `Introduced` is the regression set (the CI gate asserts
/// on it via `regressionDetected`); `Cleared` is the fix set; `WeightedSeverity`
/// is the severity-weighted count of introduced violations.
type StyleDriftReport =
    { Introduced: StyleDriftEntry list
      Cleared: StyleDriftEntry list
      AffectedNodeCount: int
      WeightedSeverity: float }

/// The severity weight to attach to a style flag — the violated invariant's
/// `Weight` (Phase 145) when the manifest declares a matching invariant, else
/// the default `1.0`. The manifest-free flags (the Phase 144 contrast trio +
/// the token / palette manifest flags with no weighted invariant) weight at
/// the default — they have no declared invariant to weight against.
let weightOfFlag (manifest: ThemeManifest option) (flag: StyleFlag) : float =
    match manifest with
    | None -> Invariant.DefaultWeight
    | Some m ->
        let invariantWeight (predicate: InvariantKind -> bool) =
            m.Invariants
            |> List.tryFind (fun inv -> predicate inv.Kind)
            |> Option.map _.Weight
            |> Option.defaultValue Invariant.DefaultWeight

        match flag with
        | StyleFlag.UsageBudgetExceeded(token, _, _) ->
            invariantWeight (fun k ->
                match k with
                | InvariantKind.UsageBudget(t, _, _) -> t = token
                | _ -> false)
        | StyleFlag.ContrastBelowDeclaredFloor(role, _, _) ->
            invariantWeight (fun k ->
                match k with
                | InvariantKind.ContrastFloor(r, _) -> r = role
                | _ -> false)
        | _ -> Invariant.DefaultWeight

/// Reduce a window to the latest observation per NodeId — a window may carry
/// several emissions for the same node (the observer emits on every
/// flag-set change); the most recent one is the node's authoritative state.
let private latestPerNode (window: StyleObservation seq) : Map<string, StyleObservation> =
    window |> Seq.fold (fun acc obs -> Map.add obs.NodeId obs acc) Map.empty

/// The `(NodeId, flag-kind) -> flag` violation set for one window — the
/// authoritative flag set per node, expanded to one entry per flag.
let private violations (window: StyleObservation seq) : Map<string * string, StyleFlag> =
    latestPerNode window
    |> Map.toSeq
    |> Seq.collect (fun (nodeId, obs) -> obs.Flags |> List.map (fun f -> (nodeId, StyleFlag.kind f), f))
    |> Map.ofSeq

/// Diff two windows with an explicit per-flag weight function. The general
/// form; `detect` is the manifest-aware convenience over it.
let detectWith
    (weightOf: StyleFlag -> float)
    (baseline: StyleObservation seq)
    (current: StyleObservation seq)
    : StyleDriftReport =
    let baselineSet = violations baseline
    let currentSet = violations current

    let entry direction ((nodeId, flagKind), flag) =
        { NodeId = nodeId
          FlagKind = flagKind
          Flag = flag
          Direction = direction
          Weight = weightOf flag }

    let introduced =
        currentSet
        |> Map.toList
        |> List.filter (fun (key, _) -> not (Map.containsKey key baselineSet))
        |> List.map (entry StyleDriftDirection.Introduced)
        |> List.sortBy (fun e -> e.NodeId, e.FlagKind)

    let cleared =
        baselineSet
        |> Map.toList
        |> List.filter (fun (key, _) -> not (Map.containsKey key currentSet))
        |> List.map (entry StyleDriftDirection.Cleared)
        |> List.sortBy (fun e -> e.NodeId, e.FlagKind)

    { Introduced = introduced
      Cleared = cleared
      AffectedNodeCount = introduced |> List.map _.NodeId |> List.distinct |> List.length
      WeightedSeverity = introduced |> List.sumBy _.Weight }

/// Diff two windows, weighting introduced violations by the manifest's
/// declared invariant weights (Phase 145). Pass `None` to weight every
/// violation equally (the manifest-free default).
let detect
    (manifest: ThemeManifest option)
    (baseline: StyleObservation seq)
    (current: StyleObservation seq)
    : StyleDriftReport =
    detectWith (weightOfFlag manifest) baseline current

/// True when the current window introduced at least one style violation
/// absent in the baseline — the predicate a CI gate / the eval suite asserts
/// on. A render that only *clears* violations is not a regression.
let regressionDetected (report: StyleDriftReport) : bool = not (List.isEmpty report.Introduced)

/// Phrase one flag in the manifest's declared vocabulary (invariant-culture
/// numbers, so a German-locale build doesn't emit "3,21"). Shared by the
/// report formatter and exposed for host log lines.
let describeFlag (flag: StyleFlag) : string =
    let inv = System.Globalization.CultureInfo.InvariantCulture
    let pct (n: float) = n.ToString("F1", inv) + "%"
    let r (n: float) = n.ToString("F2", inv)

    match flag with
    | StyleFlag.ContrastBelowAA ratio -> sprintf "ContrastBelowAA (ratio %s)" (r ratio)
    | StyleFlag.InvisibleText ratio -> sprintf "InvisibleText (ratio %s)" (r ratio)
    | StyleFlag.AccentIndistinct ratio -> sprintf "AccentIndistinct (ratio %s)" (r ratio)
    | StyleFlag.TokenResolutionFailed slot -> sprintf "TokenResolutionFailed (%s)" slot
    | StyleFlag.OffPaletteColour value -> sprintf "OffPaletteColour (%s)" value
    | StyleFlag.UsageBudgetExceeded(token, declaredPct, observedPct) ->
        sprintf "UsageBudgetExceeded %s (budget %s, observed %s)" token (pct declaredPct) (pct observedPct)
    | StyleFlag.ContrastBelowDeclaredFloor(role, ratio, floor) ->
        sprintf "ContrastBelowDeclaredFloor %s (ratio %s, floor %s)" role (r ratio) (r floor)

/// Render the report as a single human-readable / CI-log line, phrased in the
/// manifest's vocabulary. The "worst" introduced violation is the
/// highest-weight one (ties broken by the deterministic NodeId/flag-kind sort).
let formatReport (report: StyleDriftReport) : string =
    let inv = System.Globalization.CultureInfo.InvariantCulture

    match report.Introduced with
    | [] when List.isEmpty report.Cleared -> "no style drift: 0 violations introduced, 0 cleared"
    | [] -> sprintf "no regression: 0 introduced, %d cleared" report.Cleared.Length
    | introduced ->
        let worst = introduced |> List.maxBy _.Weight

        let clearedSuffix =
            if List.isEmpty report.Cleared then
                ""
            else
                sprintf "; %d cleared" report.Cleared.Length

        sprintf
            "introduced %d style violation(s) across %d node(s) (weighted severity %s; worst: %s on %s)%s"
            introduced.Length
            report.AffectedNodeCount
            (report.WeightedSeverity.ToString("F2", inv))
            (describeFlag worst.Flag)
            worst.NodeId
            clearedSuffix
