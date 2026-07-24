module Fuaran.UI.Renderer.RenderTiming

open System
open Fuaran.UI.Types

// ============================================================================
//  RenderTiming (Phase 202) — render-latency / time-to-first-paint probe.
//
//  The renderer-side instrumentation that produces the latency evidence
//  Phase 104 (streaming progressive partial-tree render) is deferred pending —
//  "does progressive render materially beat whole-tree render, and above what
//  tree size?". This module is the measuring instrument; the `fuaran-live`
//  measurement page is the real-browser harness that drives it.
//
//  ── Dual-pipeline (FGP 4) ──────────────────────────────────────────────────
//  The PURE surface — the timing model, the node-count helper, and the
//  aggregation (min / mean / p50 / p95 / max) — is plain F# over
//  `Fuaran.UI.Types`, so it compiles on BOTH the Fable-browser pipeline and the
//  pure-.NET Expecto runner, and the .NET runner pins it without a browser. The
//  only pipeline-specific surface is the MONOTONIC CLOCK (`nowMs`): under Fable
//  it reads `performance.now()` (high-resolution, monotonic) via `[<Emit>]` —
//  the same self-contained-JS posture `BrowserLayoutObserver` / `DebugGlobal`
//  use, so it never reaches through the `Browser.Dom` typed surface — and under
//  .NET it reads a process `Stopwatch`. Neither path writes `console.*`: every
//  member returns a value (FGP 4).
// ============================================================================

#if FABLE_COMPILER
open Fable.Core

/// Monotonic wall-clock in milliseconds. Browser: `performance.now()` (high-
/// resolution + monotonic), falling back to `Date.now()` where `performance` is
/// absent (e.g. a non-DOM worker). Self-contained JS — no `Browser.Dom` reach-
/// through.
[<Emit("(typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now()")>]
let nowMs () : float = jsNative
#else
// A process-lifetime Stopwatch is the .NET monotonic source — immune to wall-
// clock adjustments, unlike DateTime.UtcNow. `let`-bound, not a module-level
// `mutable`, so it carries no re-entrancy hazard.
let private clock = System.Diagnostics.Stopwatch.StartNew()

/// Monotonic wall-clock in milliseconds (process Stopwatch on .NET).
let nowMs () : float = clock.Elapsed.TotalMilliseconds
#endif

/// One render's captured timings (milliseconds) for a labelled tree. A `NaN`
/// field is a mark that was never taken (e.g. `FirstPaintMs` on a synchronous
/// whole-tree render that never reported an intermediate paint).
type RenderTimingSample =
    {
        Label: string
        NodeCount: int
        /// Render-start → first paint (the first subtree committed to the DOM).
        FirstPaintMs: float
        /// Render-start → full-tree render complete.
        FullRenderMs: float
    }

/// A single-render timing recorder. Construct at render start, mark first paint
/// and full render as they happen, then read `Sample`. The marks are monotonic
/// deltas from construction, so the sample is host-clock-offset-free.
type Probe(label: string, nodeCount: int) =
    let started = nowMs ()
    let mutable firstPaintMs = nan
    let mutable fullRenderMs = nan

    /// Record first paint. Idempotent — only the FIRST call counts (a progressive
    /// render commits many subtrees; first-paint is the earliest).
    member _.MarkFirstPaint() : unit =
        if Double.IsNaN firstPaintMs then
            firstPaintMs <- nowMs () - started

    /// Record full-tree render completion. The last call wins.
    member _.MarkFullRender() : unit = fullRenderMs <- nowMs () - started

    /// The captured sample.
    member _.Sample: RenderTimingSample =
        { Label = label
          NodeCount = nodeCount
          FirstPaintMs = firstPaintMs
          FullRenderMs = fullRenderMs }

/// The node count of a tree — the size axis the Phase 104 decision is a function
/// of. Reuses the `DebugGlobal` structural walk (Layout children + ErrorBoundary
/// arms + FragmentDecl body), so it counts exactly the addressable nodes the
/// renderer commits.
let countNodes (tree: Node<'Msg>) : int =
    DebugGlobal.walkNodes tree |> List.length

/// A summary over many samples of one metric — the shape a baseline row carries.
type RenderStat =
    { Metric: string
      Samples: int
      Min: float
      Mean: float
      P50: float
      P95: float
      Max: float }

/// The `p`-th percentile (0.0–1.0) of an already-sorted, non-empty array, by
/// nearest-rank. Pure; both pipelines.
let private percentileOfSorted (p: float) (sorted: float[]) : float =
    if sorted.Length = 0 then
        nan
    else
        let rank = int (ceil (p * float sorted.Length)) - 1
        let i = max 0 (min (sorted.Length - 1) rank)
        sorted[i]

/// Summarise a metric's values (NaN values are dropped — they are un-taken
/// marks, not zeros). Returns an all-NaN stat with `Samples = 0` when nothing
/// usable was captured.
let aggregate (metric: string) (values: float list) : RenderStat =
    let usable = values |> List.filter (fun v -> not (Double.IsNaN v)) |> List.toArray

    if usable.Length = 0 then
        { Metric = metric
          Samples = 0
          Min = nan
          Mean = nan
          P50 = nan
          P95 = nan
          Max = nan }
    else
        let sorted = Array.sort usable

        { Metric = metric
          Samples = usable.Length
          Min = sorted[0]
          Mean = Array.average sorted
          P50 = percentileOfSorted 0.5 sorted
          P95 = percentileOfSorted 0.95 sorted
          Max = sorted[sorted.Length - 1] }
