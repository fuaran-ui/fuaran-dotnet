module Fuaran.UI.Ops.Benchmarks.Capture

open System
open System.IO
open System.Text.Json

// ============================================================================
//  Baseline CAPTURE (Phase 1613).
//
//  Until this module, refreshing a `pending` baseline to `captured` was a
//  hand-transcription step: run BenchmarkDotNet, read the summary table, type
//  eighteen numbers into JSON, stamp the runtime by hand. The README documents
//  it as "the operator step", and the Phase 201 CI workflow carries a commented
//  placeholder where the adapter would go — so the gate has never once been fed
//  a fresh measurement by a machine.
//
//  This is that adapter. It reads BenchmarkDotNet's own full JSON export
//  (`*-report-full-compressed.json`, written by `--exporters json`), maps
//  (Method, Params) onto the declared metric catalogue in `Baseline.fs`, folds
//  in the off-hot-path measurements (`HitRate`, `AppendRate`, `RenderAllocation`)
//  and writes the artifact in the `PERF_BASELINE_SCHEMA.md` envelope with the
//  RUNTIME STAMPED FROM THE RUN rather than from a human's memory of it.
//
//  Why the runtime stamp matters enough to automate: the gate compares two
//  artifacts, and a wall-time number is a property of the machine as much as of
//  the code. The `op-append` baseline standing in this repo before Phase 1613
//  was captured on an ARM64 Snapdragon; every x64 run compared against it would
//  have reported a delta that measured the two laptops. Phase 1613's gate now
//  refuses that comparison — which only works if the producer states the host,
//  every time, without depending on anyone remembering to.
//
//  It writes the same shape whether the destination is the committed BASELINE or
//  a throwaway CURRENT measurement: one code path, so a fresh measurement cannot
//  drift from the baseline it is compared to.
// ============================================================================

/// One row of a BenchmarkDotNet report: the benchmark method, its parameter
/// string (`"Size=Large"`), the mean in nanoseconds, and the allocated bytes per
/// operation (`None` when the run carried no `MemoryDiagnoser`).
type private BdnRow =
    { Method: string
      Parameters: string
      MeanNs: float
      AllocB: float option }

let private str (el: JsonElement) : string =
    match el.GetString() with
    | null -> ""
    | s -> s

let private tryProp (el: JsonElement) (name: string) : JsonElement option =
    match el.TryGetProperty name with
    | true, v when v.ValueKind <> JsonValueKind.Null -> Some v
    | _ -> None

/// The single parameter VALUE out of BenchmarkDotNet's `"Size=Large"` /
/// `"Shape=Batch16"` rendering. These classes carry exactly one `[<Params>]`
/// member each; a row with none yields `""`, which matches no catalogue id and
/// is therefore reported as unfilled rather than silently mapped somewhere.
let private paramValue (parameters: string) : string =
    match parameters.Split '=' with
    | [| _; v |] -> v.Trim()
    | _ -> ""

/// The CPU identity the artifact is stamped with, and the field the gate keys
/// its host comparability on — so a blank or placeholder value is not a cosmetic
/// gap, it silently reads as "host unknown" and withholds every wall-time axis.
///
/// BenchmarkDotNet 0.14 reports `"Unknown processor"` on hardware whose model it
/// does not recognise (this reference host, an Intel Core Ultra, is one), and
/// leaves the core counts `null` with it. `PROCESSOR_IDENTIFIER` is the honest
/// fallback: family/model/stepping plus vendor, which is a stable identity for
/// the machine even though it is not a marketing name. Never fabricate one from
/// the architecture alone — `X64` names a hundred million machines.
let cpuStamp (processorName: string) (architecture: string) : string =
    let name =
        if
            processorName = ""
            || processorName.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase)
        then
            match Environment.GetEnvironmentVariable "PROCESSOR_IDENTIFIER" with
            | null
            | "" -> ""
            | id -> id
        else
            processorName

    let cores = $"{Environment.ProcessorCount}lp"

    match name, architecture with
    | "", "" -> $"unknown CPU, {cores}"
    | "", a -> $"unknown CPU ({a}), {cores}"
    | n, "" -> $"{n}, {cores}"
    | n, a -> $"{n} ({a}), {cores}"

let private readRows (reportPath: string) : BdnRow list * (string * string * string) =
    use doc = JsonDocument.Parse(File.ReadAllText reportPath)
    let root = doc.RootElement

    let runtime =
        match tryProp root "HostEnvironmentInfo" with
        | Some h ->
            let f name =
                match tryProp h name with
                | Some v -> str v
                | None -> ""

            let dotnet =
                let cli = f "DotNetCliVersion"
                let rt = f "RuntimeVersion"

                if cli = "" && rt = "" then ""
                else if cli = "" then rt
                else if rt = "" then cli
                else $"SDK {cli}; {rt}"

            dotnet, f "OsVersion", cpuStamp (f "ProcessorName") (f "Architecture")
        | None -> "", "", ""

    let rows =
        match tryProp root "Benchmarks" with
        | Some bs ->
            bs.EnumerateArray()
            |> Seq.map (fun b ->
                let mean =
                    match tryProp b "Statistics" |> Option.bind (fun s -> tryProp s "Mean") with
                    | Some m -> m.GetDouble()
                    | None -> nan

                let alloc =
                    tryProp b "Memory"
                    |> Option.bind (fun m -> tryProp m "BytesAllocatedPerOperation")
                    |> Option.map (fun v -> v.GetDouble())

                { Method =
                    match tryProp b "Method" with
                    | Some m -> str m
                    | None -> ""
                  Parameters =
                    match tryProp b "Parameters" with
                    | Some p -> str p
                    | None -> ""
                  MeanNs = mean
                  AllocB = alloc })
            |> List.ofSeq
        | None -> []

    rows, runtime

/// Every `*-report-full-compressed.json` under a BenchmarkDotNet artifacts
/// directory (or the single file, when a file is named directly). The two
/// benchmark classes export one report each, so a capture that spans both reads
/// the directory.
let private reportFiles (path: string) : string list =
    if File.Exists path then
        [ path ]
    elif Directory.Exists path then
        let results = Path.Combine(path, "results")
        let root = if Directory.Exists results then results else path

        Directory.GetFiles(root, "*-report-full-compressed.json")
        |> Array.sort
        |> List.ofArray
    else
        failwithf "no BenchmarkDotNet report at '%s'" path

/// Read every report under `path` into one row list plus the first runtime any
/// of them names. The two classes run on the same machine in the same
/// invocation, so their `HostEnvironmentInfo` blocks agree by construction; the
/// first NAMED one is taken so an empty block cannot blank the stamp.
let private readAll (path: string) : BdnRow list * (string * string * string) =
    let read = reportFiles path |> List.map readRows

    let rows = read |> List.collect fst

    let runtime =
        read
        |> List.map snd
        |> List.tryFind (fun (_, _, cpu) -> cpu <> "")
        |> Option.defaultValue ("", "", "")

    rows, runtime

/// The measured values, keyed by metric id, that a BenchmarkDotNet report
/// contributes. `methodPrefix` maps a benchmark method name onto the catalogue's
/// family segment; a method it does not know is skipped (a new benchmark is not
/// silently mapped onto an existing metric).
let private fromBdn (methodPrefix: string -> string option) (rows: BdnRow list) : Map<string, float> =
    rows
    |> List.collect (fun r ->
        match methodPrefix r.Method, paramValue r.Parameters with
        | Some family, scenario when scenario <> "" ->
            [ yield $"{family}.{scenario}.mean_ns", r.MeanNs
              match r.AllocB with
              | Some a -> yield $"{family}.{scenario}.alloc_b", a
              | None -> () ]
        | _ -> [])
    |> Map.ofList

let private applyFamily =
    function
    | "FullApply" -> Some "apply.full"
    | "MemoisedApplyHit" -> Some "apply.memoised_hit"
    | "IncrementalReapply" -> Some "apply.incremental"
    | _ -> None

let private opFamily =
    function
    | "EncodeOp" -> Some "opstream.encode"
    | "ComputeHash" -> Some "opstream.hash"
    | "BuildRecord" -> Some "opstream.build_record"
    | _ -> None

/// Fill a declared catalogue from a measured map, in catalogue order. A metric
/// the run did not produce stays `NaN`, which serialises to `null` and leaves
/// the artifact `pending` — a partial capture is never dressed up as a full one.
let private fill
    (catalogue: (string * string * string) list)
    (measured: Map<string, float>)
    : Baseline.PerfMetric list =
    [ for id, unit, note in catalogue ->
          { Id = id
            Value = Map.tryFind id measured |> Option.defaultValue nan
            Unit = unit
            Note = note } ]

let private finish (artifact: string) (dotnet, os, cpu) (metrics: Baseline.PerfMetric list) : Baseline.PerfBaseline =
    let complete = metrics |> List.forall (fun m -> not (Double.IsNaN m.Value))

    { SchemaVersion = Baseline.PerfBaseline.schemaVersion
      Artifact = artifact
      Status = (if complete then "captured" else "pending")
      CapturedAtUtc =
        if complete then
            DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", Globalization.CultureInfo.InvariantCulture)
        else
            ""
      Runtime = { Dotnet = dotnet; Os = os; Cpu = cpu }
      Metrics = metrics }

/// The apply / re-derivation artifact: the three apply paths from
/// BenchmarkDotNet, plus the memo hit-rate measured in-process.
///
/// The catalogue declares ONE hit-rate metric (`memo.hit_rate.edit_session`)
/// while `HitRate.measure` reports one per corpus size, so a choice has to be
/// made and stated rather than left to whichever size happened to be last: the
/// MEDIUM corpus is the representative edit session — a metric panel, the shape
/// the note describes — and Small/Large are its ends, not its summary.
let captureApply (reportPath: string) : Baseline.PerfBaseline =
    let rows, runtime = readAll reportPath

    let representative =
        Corpus.all
        |> List.tryFind (fun s -> s.Name = "Medium")
        |> Option.defaultValue (List.head Corpus.all)

    let measured =
        fromBdn applyFamily rows
        |> Map.add "memo.hit_rate.edit_session" (HitRate.measure representative 64 16)

    finish "apply-rederivation" runtime (fill Baseline.PerfBaseline.applyMetricCatalogue measured)

/// The op-stream write-path artifact: encode / hash / build_record from
/// BenchmarkDotNet, and the durable append from the off-hot-path `AppendRate`.
let captureOp (reportPath: string) (appendCount: int) : Baseline.PerfBaseline =
    let rows, runtime = readAll reportPath

    let appends =
        [ for s in OpCorpus.all do
              let meanNs, allocB = AppendRate.measure s appendCount
              yield $"opstream.append.{s.Name}.mean_ns", meanNs
              yield $"opstream.append.{s.Name}.alloc_b", allocB ]

    let measured =
        appends |> List.fold (fun m (k, v) -> Map.add k v m) (fromBdn opFamily rows)

    finish "op-append" runtime (fill Baseline.PerfBaseline.opMetricCatalogue measured)

/// The render-spine artifact. No BenchmarkDotNet input at all — every metric is
/// an off-hot-path `RenderAllocation` measurement — so the runtime is read from
/// the process rather than from a report.
let captureRender (count: int) : Baseline.PerfBaseline =
    let runtime =
        System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        cpuStamp "" (string System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture)

    let measured =
        [ for s in RenderAllocation.all do
              let keysNs, keysB = RenderAllocation.measureStateKeys s count
              let mergeNs, mergeB = RenderAllocation.measureLiveStateMerge s count
              let vocabNs, vocabB = RenderAllocation.measureClassVocabulary s count
              let rawNs, rawB = RenderAllocation.measureFragmentExpansionUncached s count
              let memoNs, memoB = RenderAllocation.measureFragmentExpansionMemo s count
              yield $"render.state_keys.{s.Name}.mean_ns", keysNs
              yield $"render.state_keys.{s.Name}.alloc_b", keysB
              yield $"render.live_state_merge.{s.Name}.mean_ns", mergeNs
              yield $"render.live_state_merge.{s.Name}.alloc_b", mergeB
              yield $"render.class_vocabulary.{s.Name}.mean_ns", vocabNs
              yield $"render.class_vocabulary.{s.Name}.alloc_b", vocabB
              yield $"render.fragment_expand_uncached.{s.Name}.mean_ns", rawNs
              yield $"render.fragment_expand_uncached.{s.Name}.alloc_b", rawB
              yield $"render.fragment_expand_memo.{s.Name}.mean_ns", memoNs
              yield $"render.fragment_expand_memo.{s.Name}.alloc_b", memoB ]
        |> Map.ofList

    finish "render-allocation" runtime (fill Baseline.PerfBaseline.renderMetricCatalogue measured)

/// Write an artifact, reporting whether it came out `captured` or (partially)
/// `pending` and which metrics are still unfilled.
let write (path: string) (b: Baseline.PerfBaseline) : int =
    File.WriteAllText(path, Baseline.PerfBaseline.toJson b)
    let unfilled = b.Metrics |> List.filter (fun m -> Double.IsNaN m.Value)

    printfn "Wrote %s baseline (%s): %s" b.Artifact b.Status path

    if not unfilled.IsEmpty then
        printfn "  %d metric(s) UNFILLED — the artifact stays pending:" unfilled.Length

        for m in unfilled do
            printfn "    · %s" m.Id

        1
    else
        printfn "  %d metric(s) captured on: %s / %s" b.Metrics.Length b.Runtime.Cpu b.Runtime.Os
        0
