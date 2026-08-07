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
    { Source = Binding.Static None
      Kind = kind
      XField = "x"
      YFields = [ for j in 1..m -> sprintf "y%d" j ]
      Title = None
      OnPointClick = None
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
              // A wall-clock RATIO across two sizes cannot pin "linear, not
              // quadratic": cache-hierarchy effects make genuinely linear code
              // look superlinear at an 8x size step, so a ratio bar tight enough
              // to catch the quadratic also fails the fix. The pin is therefore
              // absolute, at a size where the two implementations are an order
              // of magnitude apart: 40 000 points x 6 stacked series costs
              // ~0.3s when the nested loops index arrays and ~13s when they
              // index lists, so a 3s ceiling has ~10x headroom above the fix
              // (a much slower machine still passes) and ~4x clearance below
              // the defect (it cannot pass).
              //
              // Above the shipped point cap deliberately: this is a property of
              // the lowering core, which is worth holding whatever the caps
              // admit.
              let points = 40_000
              let spec = specOf ChartKind.Bar 6 true
              let rows = rowsOf points 6

              // Warm the JIT so the timed run is not paying for it.
              Charts.lowerWith Charts.ChartLimits.unlimited spec (Seq.ofList (rowsOf 100 6))
              |> ignore

              let elapsed = lowerMs spec rows

              Expect.isLessThan
                  elapsed
                  3000.0
                  (sprintf "lowering %d points x 6 stacked series took %.0fms" points elapsed)
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
