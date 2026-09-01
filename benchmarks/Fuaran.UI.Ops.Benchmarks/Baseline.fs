module Fuaran.UI.Ops.Benchmarks.Baseline

open System
open System.Text

// ============================================================================
//  Perf baseline artifact (Phase 200) — the committed JSON the Phase 201
//  perf release-gate consumes.
//
//  This module owns the CANONICAL SHAPE of a perf baseline. It is shared (by
//  schema, not by code reference) with the render-latency producer (Phase 202)
//  and the perf-gate consumer (Phase 201, which lives in a separate downstream
//  repo and re-declares a matching reader). The single source of truth for the
//  shape is `PERF_BASELINE_SCHEMA.md` alongside this file; this module is its F#
//  realisation on the producer side.
//
//  A baseline has two lifecycle states:
//   - `pending`  — the metric IDs + units are declared, but no numbers have been
//                  captured yet (the capture is a benchmark RUN, gated separately).
//                  The gate treats a `pending` baseline as "no baseline" and fails
//                  loudly (Phase 201's no-silent-pass discipline).
//   - `captured` — a real run has filled `Value` for every declared metric and
//                  stamped `CapturedAtUtc` + `Runtime`.
//
//  The committed `apply-rederivation-baseline.json` ships in the `pending` state;
//  refreshing it to `captured` is the deferred run step (see `README.md`).
// ============================================================================

/// One measured (or declared-but-pending) metric.
type PerfMetric =
    {
        Id: string
        /// The captured value, or `NaN` when the baseline is still `pending`.
        Value: float
        Unit: string
        Note: string
    }

/// The runtime the baseline was captured on — load-bearing for honest
/// comparison (a baseline captured on a different CPU is not comparable).
type RuntimeInfo =
    { Dotnet: string
      Os: string
      Cpu: string }

/// A full perf baseline artifact.
type PerfBaseline =
    {
        SchemaVersion: int
        /// `"apply-rederivation"` (Phase 200) or `"render-latency"` (Phase 202).
        Artifact: string
        /// `"pending"` or `"captured"`.
        Status: string
        /// ISO-8601 UTC, or `""` while `pending`.
        CapturedAtUtc: string
        Runtime: RuntimeInfo
        Metrics: PerfMetric list
    }

[<RequireQualifiedAccess>]
module PerfBaseline =

    /// The current schema version. Bump only on a breaking shape change; the
    /// gate refuses a baseline whose `SchemaVersion` it does not understand.
    let schemaVersion = 1

    // A hand-rolled writer keeps the artifact dependency-free on both sides: the
    // producer (this project + Phase 202) only ever WRITES; the perf-gate
    // consumer (Phase 201, a separate downstream repo) reads via JsonDocument. No
    // F#-aware converter package is pulled into either. `NaN` (a `pending` value)
    // is emitted as JSON `null` — JSON has no NaN literal, and `null` reads
    // cleanly as "no number".

    let private esc (s: string) : string =
        let sb = StringBuilder()

        for c in s do
            match c with
            | '"' -> sb.Append "\\\"" |> ignore
            | '\\' -> sb.Append "\\\\" |> ignore
            | '\n' -> sb.Append "\\n" |> ignore
            | '\r' -> sb.Append "\\r" |> ignore
            | '\t' -> sb.Append "\\t" |> ignore
            | c when c < ' ' -> sb.AppendFormat("\\u{0:x4}", int c) |> ignore
            | c -> sb.Append c |> ignore

        sb.ToString()

    let private num (x: float) : string =
        if Double.IsNaN x || Double.IsInfinity x then
            "null"
        else
            x.ToString(System.Globalization.CultureInfo.InvariantCulture)

    /// Serialise a baseline to canonical (snake_case, indented) JSON.
    let toJson (b: PerfBaseline) : string =
        let sb = StringBuilder()

        let line (indent: int) (s: string) =
            sb.Append(String(' ', indent)).AppendLine s |> ignore

        line 0 "{"
        line 2 $"\"schema_version\": {b.SchemaVersion},"
        line 2 $"\"artifact\": \"{esc b.Artifact}\","
        line 2 $"\"status\": \"{esc b.Status}\","
        line 2 $"\"captured_at_utc\": \"{esc b.CapturedAtUtc}\","
        line 2 "\"runtime\": {"
        line 4 $"\"dotnet\": \"{esc b.Runtime.Dotnet}\","
        line 4 $"\"os\": \"{esc b.Runtime.Os}\","
        line 4 $"\"cpu\": \"{esc b.Runtime.Cpu}\""
        line 2 "},"
        line 2 "\"metrics\": ["
        let n = List.length b.Metrics

        b.Metrics
        |> List.iteri (fun i m ->
            let comma = if i = n - 1 then "" else ","
            line 4 "{"
            line 6 $"\"id\": \"{esc m.Id}\","
            line 6 $"\"value\": {num m.Value},"
            line 6 $"\"unit\": \"{esc m.Unit}\","
            line 6 $"\"note\": \"{esc m.Note}\""
            line 4 ("}" + comma))

        line 2 "]"
        sb.Append("}").ToString()

    /// The declared metric catalogue for the apply / re-derivation baseline.
    /// Each entry is `(id, unit, note)`; the IDs are the contract the Phase 201
    /// budget table keys against. `mean_ns` = mean wall-time per operation;
    /// `alloc_b` = managed bytes allocated per operation; `ratio` = a unit-free
    /// fraction (memo hit-rate).
    let applyMetricCatalogue: (string * string * string) list =
        [ for s in Corpus.all do
              yield
                  $"apply.full.{s.Name}.mean_ns",
                  "ns",
                  "Bare FragmentApply.apply (no cache) — the cold-derivation cost."

              yield
                  $"apply.memoised_hit.{s.Name}.mean_ns",
                  "ns",
                  "Engine.Apply on a warm cache — structural HIT, tree reused."

              yield
                  $"apply.incremental.{s.Name}.mean_ns",
                  "ns",
                  "Engine.Reapply with one value edit — incremental recompute of one hole-address."

              yield $"apply.full.{s.Name}.alloc_b", "B", "Allocated bytes per bare apply."
              yield $"apply.incremental.{s.Name}.alloc_b", "B", "Allocated bytes per incremental reapply."
          yield "memo.hit_rate.edit_session", "ratio", "Memo reuse fraction over a representative value-edit session." ]

    /// The committed PENDING template for the apply / re-derivation baseline:
    /// every metric ID declared, every value `NaN`, status `pending`. Capturing
    /// real numbers (the benchmark RUN) refreshes this in place.
    let applyPendingTemplate: PerfBaseline =
        { SchemaVersion = schemaVersion
          Artifact = "apply-rederivation"
          Status = "pending"
          CapturedAtUtc = ""
          Runtime = { Dotnet = ""; Os = ""; Cpu = "" }
          Metrics =
            [ for id, unit, note in applyMetricCatalogue ->
                  { Id = id
                    Value = nan
                    Unit = unit
                    Note = note } ] }

    /// The declared metric catalogue for the op-stream WRITE-path baseline
    /// (Wave-T op-append follow-on). The encode + hash metrics come from the
    /// `OpStreamBenchmarks` BenchmarkDotNet class; the `append` metrics from the
    /// off-hot-path `AppendRate.measure`. IDs are keyed per op shape
    /// `{UpdateProp, InsertChild, Batch16}` so the cost curve from a granular
    /// edit → a structural insert → a wide batch is legible.
    let opMetricCatalogue: (string * string * string) list =
        [ for s in OpCorpus.all do
              yield
                  $"opstream.encode.{s.Name}.mean_ns",
                  "ns",
                  "CanonicalJson.encodeOp — canonical JSON encode of the op (the hash pre-image source)."

              yield
                  $"opstream.hash.{s.Name}.mean_ns",
                  "ns",
                  "HashChain.computeHash — encode + genesis-pre-image SHA-256 (the actor-in-hash write cost)."

              yield
                  $"opstream.build_record.{s.Name}.mean_ns",
                  "ns",
                  "Compute the hash AND materialise the OpRecord the sink appends."

              yield
                  $"opstream.append.{s.Name}.mean_ns",
                  "ns",
                  "InMemorySink.Append per op — Dictionary insert + per-stream lock + async-state-machine cost."

              yield $"opstream.encode.{s.Name}.alloc_b", "B", "Allocated bytes per encodeOp."
              yield $"opstream.hash.{s.Name}.alloc_b", "B", "Allocated bytes per computeHash."
              yield $"opstream.build_record.{s.Name}.alloc_b", "B", "Allocated bytes per record build."
              yield $"opstream.append.{s.Name}.alloc_b", "B", "Allocated bytes per durable append." ]

    /// The declared metric catalogue for the RENDER-SPINE baseline (Phase 207).
    /// The three families are the three allocators that phase removed from the
    /// per-node / per-frame path, each measured by `RenderAllocation` over the
    /// same three tree sizes: the reactive subscription walk a frame pays before
    /// it renders anything, the live-store merge that precedes it, and the
    /// per-node class + ARIA-id vocabulary a node's chrome is built from.
    ///
    /// `alloc_b` is the metric that matters here and `mean_ns` the corroborating
    /// one: every Phase 207 edit is output-identical, so a regression shows up as
    /// bytes before it shows up as time.
    let renderMetricCatalogue: (string * string * string) list =
        [ for s in RenderAllocation.all do
              yield
                  $"render.state_keys.{s.Name}.mean_ns",
                  "ns",
                  "Render.collectStateKeys over the whole tree — the reactive subscription walk, once per frame."

              yield
                  $"render.live_state_merge.{s.Name}.mean_ns",
                  "ns",
                  "StateStore overlayOnto — the live-store read view merged over the host sources, once per channel per frame."

              yield
                  $"render.class_vocabulary.{s.Name}.mean_ns",
                  "ns",
                  "Theme.nodeClassName + the Css / Ids builders, once per node of the tree."

              yield
                  $"render.state_keys.{s.Name}.alloc_b",
                  "B",
                  "Allocated bytes per key walk. The pre-207 walk allocated a Set per node and unioned bottom-up."

              yield
                  $"render.live_state_merge.{s.Name}.alloc_b",
                  "B",
                  "Allocated bytes per store merge. The pre-207 path materialised a whole snapshot Map first."

              yield
                  $"render.class_vocabulary.{s.Name}.alloc_b",
                  "B",
                  "Allocated bytes per tree's worth of class/id construction. The pre-207 path parsed a format string per node." ]

    /// The committed PENDING template for the render-spine baseline: every
    /// metric ID declared, every value `NaN`, status `pending`.
    let renderPendingTemplate: PerfBaseline =
        { SchemaVersion = schemaVersion
          Artifact = "render-allocation"
          Status = "pending"
          CapturedAtUtc = ""
          Runtime = { Dotnet = ""; Os = ""; Cpu = "" }
          Metrics =
            [ for id, unit, note in renderMetricCatalogue ->
                  { Id = id
                    Value = nan
                    Unit = unit
                    Note = note } ] }

    /// The committed PENDING template for the op-stream WRITE-path baseline:
    /// every metric ID declared, every value `NaN`, status `pending`.
    let opPendingTemplate: PerfBaseline =
        { SchemaVersion = schemaVersion
          Artifact = "op-append"
          Status = "pending"
          CapturedAtUtc = ""
          Runtime = { Dotnet = ""; Os = ""; Cpu = "" }
          Metrics =
            [ for id, unit, note in opMetricCatalogue ->
                  { Id = id
                    Value = nan
                    Unit = unit
                    Note = note } ] }
