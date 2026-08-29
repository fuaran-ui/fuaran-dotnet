module Fuaran.UI.Tests.CostBoundsTests

#nowarn "3261" // `box` over a test cell value is legitimately nullable here.

// ============================================================================
//  Phase 790 — render-cache eviction + chart-lowering cost bounds.
//
//  The class pinned here is "a well-formed emission exhausts the host slowly":
//  caches that never evict, and a lowering whose cost is superlinear in data
//  the tree-size budget cannot see (a Chart is ONE node). Each test is written
//  so that removing the guard it covers turns it red — a bound nothing can fail
//  is not a bound.
//
//  The SVG output ceiling is pinned beside the rest of the DrawingSvg contract
//  (Fuaran.UI.Renderer.Server.Tests/DrawingSvgTests.fs); the render-cache bound
//  beside its host adapter (Fuaran.UI.Giraffe.Tests/CachingTests.fs).
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Memo

// ─── Fixtures ────────────────────────────────────────────────────────────────

let private leaf (id: string) : Node<unit> = Fuaran.markdown id ("body " + id)

/// `n` rows carrying a string x label and `m` numeric series.
let private rowsOf (n: int) (m: int) : Row list =
    [ for i in 1..n ->
          let ys = [ for j in 1..m -> (sprintf "y%d" j), box (float (i * j)) ]
          Map.ofList (("x", box (sprintf "c%d" i)) :: ys) ]

let private specOf (kind: ChartKind) (m: int) (stacked: bool) : ChartSpec<obj> =
    { Defaults.chart with
        Source = Binding.Static None
        Kind = kind
        XField = "x"
        YFields = [ for j in 1..m -> sprintf "y%d" j ]
        Stacked = stacked }

/// Wall-clock milliseconds for one uncapped lowering, taken as the MINIMUM of
/// three runs. Minimum, not mean or median: scheduling and GC only ever add time,
/// so the fastest observation is the least contaminated estimate of what the
/// code costs — and the noise this test must not fail on is upward noise.
let private lowerMs (spec: ChartSpec<obj>) (rows: Row list) : float =
    let once () =
        let sw = System.Diagnostics.Stopwatch.StartNew()

        let drawing = Charts.lowerWith Charts.ChartLimits.unlimited spec (Seq.ofList rows)

        List.length drawing.Shapes |> ignore
        sw.Elapsed.TotalMilliseconds

    [ for _ in 1..3 -> once () ] |> List.min

// ─── Tests ───────────────────────────────────────────────────────────────────

[<Tests>]
let tests =
    testList
        "Phase 790 — cost bounds"
        [

          // ── The portable fragment store evicts ──────────────────────────────

          test "the portable memo store evicts under sustained varied input" {
              let store = FragmentStore.portableWithCapacity<unit> 8 :> IFragmentStore<unit>

              for i in 1..500 do
                  store.Set(sprintf "key-%d" i, leaf (sprintf "n%d" i))

              Expect.equal store.Count 8 "the store holds exactly its capacity, not one entry per distinct key"

              Expect.equal store.Capacity 8 "the bound is observable"
              // The policy is LRU, so the most recent writes are the survivors.
              Expect.isSome (store.TryGet "key-500") "the most recent entry is retained"
              Expect.isNone (store.TryGet "key-1") "the oldest entry was evicted"
          }

          test "the portable store's DEFAULT bound is finite (the pre-790 defect)" {
              let store = FragmentStore.portable<unit> () :> IFragmentStore<unit>

              Expect.isLessThan
                  store.Capacity
                  System.Int32.MaxValue
                  "an unbounded default is exactly the defect this phase closed"
          }

          test "refreshing an existing key never evicts" {
              let store = FragmentStore.portableWithCapacity<unit> 2 :> IFragmentStore<unit>
              store.Set("a", leaf "a")
              store.Set("b", leaf "b")
              store.Set("a", leaf "a2")

              Expect.equal store.Count 2 "an update is not an insert"
              Expect.isSome (store.TryGet "b") "the other entry survived the update"
          }

          test "a snapshot still round-trips through a fresh store (the bound costs portability nothing)" {
              let source = FragmentStore.portable<unit> ()
              (source :> IFragmentStore<unit>).Set("k", leaf "carried")

              let restored = FragmentStore.fromSnapshot<unit> source.Snapshot

              Expect.isSome
                  ((restored :> IFragmentStore<unit>).TryGet "k")
                  "the carried entry is a hit in the fresh store"
          }

          // ── Chart lowering caps ─────────────────────────────────────────────

          test "a chart with more series than the cap is refused" {
              let limits =
                  { Charts.ChartLimits.defaults with
                      MaxSeries = 3 }

              match Charts.tryLowerWith limits (specOf ChartKind.Bar 4 false) (Seq.ofList (rowsOf 5 4)) with
              | Error(Charts.TooManySeries(observed, limit)) ->
                  Expect.equal observed 4 "the observed series count is reported"
                  Expect.equal limit 3 "the breached limit is reported"
              | other -> failtestf "expected TooManySeries, got %A" other
          }

          test "a chart with more points than the cap is refused" {
              let limits =
                  { Charts.ChartLimits.defaults with
                      MaxPointsPerSeries = 10 }

              match Charts.tryLowerWith limits (specOf ChartKind.Bar 1 false) (Seq.ofList (rowsOf 50 1)) with
              | Error(Charts.TooManyPoints(atLeast, limit)) ->
                  Expect.equal limit 10 "the breached limit is reported"
                  Expect.isGreaterThan atLeast limit "the observed count exceeds the limit"
              | other -> failtestf "expected TooManyPoints, got %A" other
          }

          test "a chart exactly at the cap still lowers (the cap is not off by one)" {
              let limits =
                  { Charts.ChartLimits.defaults with
                      MaxPointsPerSeries = 10 }

              match Charts.tryLowerWith limits (specOf ChartKind.Bar 1 false) (Seq.ofList (rowsOf 10 1)) with
              | Ok drawing -> Expect.isNonEmpty drawing.Shapes "a chart exactly at the cap renders"
              | Error e -> failtestf "expected Ok at exactly the cap, got %A" e
          }

          test "a refused chart yields a bounded refusal drawing, not a blank one" {
              let limits =
                  { Charts.ChartLimits.defaults with
                      MaxPointsPerSeries = 10 }

              let drawing =
                  Charts.lowerWith limits (specOf ChartKind.Bar 1 false) (Seq.ofList (rowsOf 50 1))

              Expect.isEmpty drawing.Shapes "the refusal emits no geometry"

              match drawing.Description with
              | Some(TextSource.Literal text) ->
                  Expect.stringContains text "not rendered" "the refusal says it did not render"
              | other -> failtestf "expected a refusal description, got %A" other
          }

          test "an over-budget row source is never read past the cap + 1" {
              // The point of refusing rather than truncating: the refusal costs
              // one row more than the cap, not the whole feed — an unbounded
              // source must not be materialised just to discover it is too big.
              // A LAZY source with a pull counter makes that observable, and a
              // FINITE one (100x the cap) means removing the guard reds this
              // test rather than hanging it.
              let mutable pulled = 0

              let limits =
                  { Charts.ChartLimits.defaults with
                      MaxPointsPerSeries = 25 }

              let lazySource: Row seq =
                  Seq.init (limits.MaxPointsPerSeries * 100) (fun i ->
                      pulled <- pulled + 1
                      Map.ofList [ "x", box (sprintf "c%d" i); "y1", box (float i) ])

              match Charts.tryLowerWith limits (specOf ChartKind.Bar 1 false) lazySource with
              | Error(Charts.TooManyPoints _) -> Expect.equal pulled 26 "exactly cap + 1 rows were read"
              | other -> failtestf "expected TooManyPoints, got %A" other
          }

          test "the shipped default caps are finite" {
              Expect.isLessThan
                  Charts.ChartLimits.defaults.MaxSeries
                  System.Int32.MaxValue
                  "the default series cap is finite"

              Expect.isLessThan
                  Charts.ChartLimits.defaults.MaxPointsPerSeries
                  System.Int32.MaxValue
                  "the default point cap is finite"
          }

          // ── The worst case the caps admit is cheap ──────────────────────────

          test "a large stacked lowering stays cheap — the quadratic is gone" {
              // The pin is a RATIO between two sizes of the same workload, not a
              // wall-clock ceiling. It was a ceiling (3000ms at 40 000 points),
              // and that ceiling is a time bomb rather than a bound: it measures
              // the machine as much as the code, so it passed on a quiet box and
              // failed at 4182ms on a loaded one, with nothing about the lowering
              // changed. A bound that reports the machine's other tenants is not
              // reporting a regression.
              //
              // The ratio survives that because sustained contention scales BOTH
              // legs. What it costs is the older comment's objection, which was
              // right on its own terms and is answered by the size step rather
              // than dismissed: at a small step, cache-hierarchy effects make
              // genuinely linear code look superlinear, so a bar tight enough to
              // catch the quadratic also fails the fix. At an 8x step the two
              // implementations are nowhere near each other — array indexing
              // scales ~8x, list indexing ~64x, since each of 8x more rows also
              // costs 8x more to reach.
              //
              // Measured rather than assumed, on the 12-core machine this was
              // rewritten on: 8.9x / 9.3x / 9.7x idle, and 6.5x / 7.8x / 11.2x
              // with all twelve cores held busy — the load moves the ABSOLUTE
              // cost and leaves the ratio where it was, which is the whole
              // claim. The bar sits at 24x, roughly the geometric midpoint
              // between the worst of those and the ~55x a list-indexing lowering
              // must produce at this step: 2x of headroom on each side, where
              // the absolute form only ever had whatever the machine that ran it
              // happened to leave.
              //
              // Above the shipped point cap deliberately: this is a property of
              // the lowering core, which is worth holding whatever the caps
              // admit.
              let series = 6
              let smallPoints = 5_000
              let largePoints = 40_000 // an 8x step
              let spec = specOf ChartKind.Bar series true
              let smallRows = rowsOf smallPoints series
              let largeRows = rowsOf largePoints series

              // Warm the JIT so neither timed leg is paying for it.
              Charts.lowerWith Charts.ChartLimits.unlimited spec (Seq.ofList (rowsOf 100 series))
              |> ignore

              let smallMs = lowerMs spec smallRows
              let largeMs = lowerMs spec largeRows

              // A denominator near the timer floor would turn the ratio into
              // noise and fail for the one reason this test must not: nothing to
              // do with the lowering. Assert it is measurable rather than
              // dividing and hoping.
              Expect.isGreaterThan
                  smallMs
                  1.0
                  (sprintf
                      "the reference leg (%d points x %d stacked series) took %.2fms — too close to the timer floor to divide by. Raise `smallPoints`."
                      smallPoints
                      series
                      smallMs)

              let ratio = largeMs / smallMs

              Expect.isLessThan
                  ratio
                  24.0
                  (sprintf
                      "lowering cost scaled %.1fx across an 8x size step (%d pts %.0fms -> %d pts %.0fms). Linear is ~8x; the pre-790 list-indexing lowering is ~64x. This is a SHAPE regression, not a slow machine — the ratio is taken from two legs measured moments apart under the same load."
                      ratio
                      smallPoints
                      smallMs
                      largePoints
                      largeMs)
          }

          test "the de-quadratified lowering emits the same geometry" {
              // The array rewrite is an access-cost fix; the emitted geometry is
              // the contract, and the corpus goldens (ChartLoweringTests) pin it
              // byte-for-byte. This is the cheap local restatement: a stacked bar
              // still produces one rect per (category x series), plus the legend
              // swatches.
              let drawing = Charts.lower (specOf ChartKind.Bar 3 true) (Seq.ofList (rowsOf 5 3))

              let rects =
                  drawing.Shapes
                  |> List.filter (fun s ->
                      match s with
                      | Shape.Rectangle _ -> true
                      | _ -> false)

              Expect.equal (List.length rects) (5 * 3 + 3) "one segment per (category x series), plus legend swatches"
          } ]
