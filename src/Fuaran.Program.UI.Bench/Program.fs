module Fuaran.Program.Bench.Program

// The `ServerEffect.RunQuery` benchmark (Phase 1896).
//
// What is timed: one `Handler.run` over a handler whose only stage is a
// `RunQuery` — evaluate the source, run the pipeline, land the table in the
// store. The table is built once, outside the timed region, and handed to the
// evaluator through the source resolver, so the figure is the evaluator plus
// the handler loop around it and nothing else.
//
// Method: each size is warmed, then run in `samples` timed batches; a batch
// repeats the call until it has run for at least `minBatchMs`, and reports the
// mean time per call. The median batch is the headline; min and max give the
// spread. A size whose single warm call already takes longer than a second is
// sampled less (one warm call, `slowSamples` single-call batches), because a
// quadratic evaluator at 100k rows costs minutes per call and the spread is
// then stated over fewer samples rather than not measured at all.
// Deterministic data (no RNG), so two runs time the same work.

open System
open System.Diagnostics
open Fuaran.Core
open Fuaran.Compute
open Fuaran.Program.Server
open Fuaran.Program.Server.UI

let private table (rows: int) : Table =
    { Schema = [ "n", IntType; "g", StringType; "x", FloatType ]
      Columns =
        [ { Name = "n"
            Type = IntType
            Cells = [ for i in 0 .. rows - 1 -> Int i ] }
          { Name = "g"
            Type = StringType
            Cells = [ for i in 0 .. rows - 1 -> Str(sprintf "g%d" (i % 17)) ] }
          { Name = "x"
            Type = FloatType
            Cells = [ for i in 0 .. rows - 1 -> Float(float ((i * 7919) % 1000) / 10.0) ] } ] }

/// Filter half the rows away, derive a column, sort on it, keep the top 100.
let private pipeline (rows: int) : Transform list =
    [ Filter(Binary(Gt, Col "n", Lit(Int(rows / 2))))
      Derive("y", Binary(Mul, Col "x", Lit(Float 2.0)))
      Sort [ Slot.Lit "y", Desc ]
      Limit(Slot.Lit 100, Slot.Lit 0) ]

let private handler (rows: int) : Handler =
    { Name = "bench"
      Stages = [ Effect(ServerEffect.RunQuery("rows", Ref "rows", pipeline rows)) ] }

let private store: ServerStore =
    { Tree = Fuaran.UI.Fuaran.markdown "root" "bench"
      Bindings = Fuaran.UI.Renderer.BindingResolver.empty }

let private runOnce (registry: ServerEffectRegistry) (resolve: string -> Result<Table, EvalError>) (h: Handler) =
    let outcome = Handler.run registry resolve "bench" h store

    if not outcome.Committed then
        failwithf "the benchmark handler did not commit: %A" outcome.Diagnostics

let private slowSamples = 5

let private measure (rows: int) (samples: int) (minBatchMs: float) =
    let t = table rows

    let resolve (name: string) : Result<Table, EvalError> =
        if name = "rows" then Ok t else DataFrame.noResolve name

    let registry = ServerEffectRegistry.permissive ServerEffectRegistry.denyAll
    let h = handler rows

    let warm = Stopwatch.StartNew()
    runOnce registry resolve h
    let slow = warm.Elapsed.TotalSeconds > 1.0

    if not slow then
        for _ in 1..2 do
            runOnce registry resolve h

    let samples = if slow then min samples slowSamples else samples

    let perCall =
        [ for _ in 1..samples ->
              let sw = Stopwatch.StartNew()
              let mutable n = 0

              while sw.Elapsed.TotalMilliseconds < minBatchMs || n = 0 do
                  runOnce registry resolve h
                  n <- n + 1

              sw.Elapsed.TotalMilliseconds / float n ]
        |> List.sort

    let median = perCall.[List.length perCall / 2]
    rows, median, List.head perCall, List.last perCall, List.length perCall

[<EntryPoint>]
let main argv =
    let full = argv |> Array.contains "--full"

    let requested =
        argv
        |> Array.choose (fun a ->
            match Int32.TryParse a with
            | true, n -> Some n
            | _ -> None)
        |> List.ofArray

    let sizes, samples, minBatchMs =
        if full then
            (if List.isEmpty requested then
                 [ 1_000; 10_000; 100_000 ]
             else
                 requested),
            15,
            200.0
        else
            [ 1_000 ], 3, 1.0

    printfn
        "RunQuery benchmark (%s) — %s, %s"
        (if full then "full" else "smoke")
        (Environment.Version.ToString())
        Environment.OSVersion.VersionString

    printfn "  DataFrame assembly: %s" (typeof<Transform>.Assembly.GetName().Version.ToString())
    printfn "  %8s  %12s  %12s  %12s" "rows" "median ms" "min ms" "max ms"

    for size in sizes do
        let rows, median, lo, hi, n = measure size samples minBatchMs
        printfn "  %8d  %12.3f  %12.3f  %12.3f  (%d samples)" rows median lo hi n

    0
