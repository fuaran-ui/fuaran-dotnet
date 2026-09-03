module Fuaran.UI.Tests.SparklineLoweringTests

#nowarn "3261" // DirectoryInfo.Parent + box are legitimately nullable here.

// ============================================================================
//  Phase 1098 — `Sparkline → Drawing` lowering certification.
//
//  Phase 644's D7, executed on the reference host: `Sparkline` keeps its wire
//  kind and its `$type`; its per-host hand-written polyline is retired for a
//  bounded lowering emitted through the shared `DrawingSvg` builder. This suite
//  is what makes that claim falsifiable, in three layers:
//
//   1. **The goldens** — `wire-format-fixtures/sparkline-lowering/*` on the
//      `chart-lowering/*` pattern: an `<name>.input.json` carrying the neutral
//      series contract, and an `<name>.expected.json` carrying the canonical
//      `Drawing` wire JSON the lowering must produce (or `null` where there is
//      nothing to draw). Phase 1099's whole contract is this family.
//
//   2. **The byte-compatibility proof** — the geometry that already ships must
//      not move. The pre-1098 renderer's formula is re-implemented HERE, from
//      the deleted `renderSparkline`, and every case is asserted against it.
//      That is deliberately a SECOND implementation rather than a call into the
//      lowering: a proof written in terms of the thing it is proving cannot
//      fail. Perturb either the lowering or `legacyPoints` and this goes red.
//
//   3. **The emitted markup** — the viewBox, the stroke width, the
//      `currentColor` chrome and the 2-dp coordinates, read back out of the SVG
//      the shared builder produces.
//
//  Regenerate the goldens with `FUARAN_EMIT_SPARKLINE_LOWERING=1`, then commit
//  them in the corpus repo alongside the change that prompted them.
// ============================================================================

open System
open System.IO
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions

/// One lowering case: a name and the RESOLVED series the host hands the
/// lowering. `Sparkline` has exactly one field, so there is nothing else to
/// vary — which is the authoring cheapness Phase 644 declined to trade away.
type private Case = { Name: string; Series: float list }

/// The corpus cases. The five the phase names, plus the two boundary vectors a
/// `i/(n-1)` term and a magic `1e-9` threshold each earn — the precedent is
/// `chart-lowering/bar-flat-boundary`.
let private cases: Case list =
    [ { Name = "normal"
        // `nodes/spark-1.json`'s series — the geometry that already ships, and
        // the case the byte-compatibility claim is made about.
        Series = [ 1.0; 2.0; 3.0; 2.0; 4.0 ] }
      { Name = "two-points"
        // n = 2: the smallest series the `i/(n-1)` term is defined for.
        Series = [ 10.0; 4.0 ] }
      { Name = "single-point"
        // n = 1: `i/(n-1)` would divide by zero, so the shipped rule centres the
        // lone point at x = 50.
        Series = [ 42.0 ] }
      { Name = "flat"
        // max - min = 0: the flat guard puts a constant series on its own
        // mid-line rather than dividing by zero.
        Series = [ 7.0; 7.0; 7.0; 7.0 ] }
      { Name = "flat-boundary"
        // A range just INSIDE the 1e-9 guard, so the guard is what decides the
        // picture rather than the arithmetic happening to agree.
        Series = [ 1.0; 1.0 + 5e-10; 1.0 ] }
      { Name = "empty"
        // Nothing to draw. The lowering reports it in the type and each renderer
        // emits its declared `fuaran-sparkline-empty` fallback element.
        Series = [] }
      { Name = "nonfinite-sentinel"
        // `nodes/spark-nonfinite-sentinel.json`'s shape. The sentinels are NOT
        // special-cased: they propagate through the arithmetic exactly as they
        // did in the hand-written builder, reach the wire as the canonical
        // string sentinels, and render as `0`. Pinning that is the point — the
        // two recorded cross-host decode divergences live in this input class.
        Series = [ 1.0; nan; 3.0; infinity; -infinity; 5.0 ] } ]

// ─── The neutral input contract ──────────────────────────────────────────────

/// A series element in canonical wire JSON. A non-finite float is the STRING
/// sentinel the wire format spells it as, which is what
/// `nodes/spark-nonfinite-sentinel.json` already carries — so a host reads this
/// family with the decoder it already has.
let private seriesElementJson (v: float) : string =
    if Double.IsNaN v then "\"NaN\""
    elif Double.IsPositiveInfinity v then "\"Infinity\""
    elif Double.IsNegativeInfinity v then "\"-Infinity\""
    else DrawingSvg.formatNum v

let private inputJson (case: Case) : string =
    "{\"series\":["
    + (case.Series |> List.map seriesElementJson |> String.concat ",")
    + "]}"

// ─── The expected output ─────────────────────────────────────────────────────

/// The lowered drawing as the canonical wire JSON of a `Drawing` node — the
/// `chart-lowering/*` shape, so a host's runner needs no second reader. A case
/// with nothing to draw emits the JSON literal `null`: that is the wire image of
/// `tryLowerSparkline` returning `None`, and it invents no vocabulary.
let private loweredJson (case: Case) : string =
    match Charts.tryLowerSparkline Defaults.sparkline case.Series with
    | None -> "null"
    | Some ds ->
        let node: Node<obj> = Fuaran.drawingSpec ("sparkline-" + case.Name) ds
        CanonicalJson.encodeNode node

// ─── The pre-1098 formula, re-implemented ────────────────────────────────────

/// The geometry the deleted `Renderer/Render.fs` `renderSparkline` emitted,
/// written out here from that arm. A SECOND implementation on purpose: layer 2
/// of this suite is a byte-compatibility claim, and a claim checked against the
/// code it is about is not a check at all.
///
/// The only translation is the number FORM. The old arm formatted with
/// `sprintf "%.2f"`, which spells a whole value `29.00`; the shared builder
/// spells it `29`. The VALUES are what the claim is about, so they are compared
/// as values, and the spelling difference is stated here rather than hidden by
/// comparing strings.
let private legacyPoints (series: float list) : (float * float) list =
    let values = List.toArray series
    let n = values.Length
    let minV = Array.min values
    let maxV = Array.max values
    let range = if maxV - minV < 1e-9 then 1.0 else maxV - minV

    let round2 (x: float) = floor (x * 100.0 + 0.5) / 100.0

    [ for i in 0 .. n - 1 ->
          let v = values[i]
          let x = if n <= 1 then 50.0 else float i / float (n - 1) * 100.0
          let y = 30.0 - (v - minV) / range * 28.0 - 1.0
          round2 x, round2 y ]

/// Value equality that treats `NaN` against `NaN` as agreement — the sentinel
/// case carries `NaN` coordinates by construction, and `nan = nan` is `false`,
/// so a plain comparison would report the one thing it exists to certify as a
/// mismatch.
let private sameNum (a: float) (b: float) =
    (Double.IsNaN a && Double.IsNaN b) || a = b

let private polylineOf (ds: DrawingSpec) : DrawPoint list * DrawStyle =
    match ds.Shapes with
    | [ Shape.Polyline(points, style) ] -> points, style
    | other -> failwithf "expected exactly one Polyline, got %d shape(s)" (List.length other)

let private noSources = BindingResolver.empty

let private svgOf (ds: DrawingSpec) : string =
    DrawingSvg.render noSources (fun _ -> "") ds

let private tryFindFixtures () : string option =
    let rec climb (dir: DirectoryInfo) =
        if isNull (box dir) then
            None
        else
            let candidate = Path.Combine(dir.FullName, "wire-format-fixtures")

            if Directory.Exists candidate then
                Some candidate
            else
                climb dir.Parent

    climb (DirectoryInfo(AppContext.BaseDirectory))

let private lowered (case: Case) : DrawingSpec =
    Charts.tryLowerSparkline Defaults.sparkline case.Series
    |> Option.defaultWith (fun () -> failwithf "%s: expected a drawing" case.Name)

// ─── Layers 2 + 3 — independent of the corpus being present ──────────────────

let private drawnCases = cases |> List.filter (fun c -> not c.Series.IsEmpty)

[<Tests>]
let sparklineLoweringGeometryTests =
    testList
        "Sparkline lowering (geometry)"
        [ test "every drawn case reproduces the pre-1098 renderer's coordinates exactly" {
              for case in drawnCases do
                  let points, _ = polylineOf (lowered case)
                  let legacy = legacyPoints case.Series

                  Expect.equal (List.length points) (List.length legacy) (sprintf "%s: point count moved" case.Name)

                  for (p, (lx, ly)) in List.zip points legacy do
                      Expect.isTrue (sameNum p.X lx) (sprintf "%s: x moved (%f vs %f)" case.Name p.X lx)
                      Expect.isTrue (sameNum p.Y ly) (sprintf "%s: y moved (%f vs %f)" case.Name p.Y ly)
          }

          test "the shipped canvas and chrome are unchanged" {
              for case in drawnCases do
                  let ds = lowered case

                  Expect.equal ds.ViewBox Charts.sparklineViewBox (sprintf "%s: viewBox moved" case.Name)
                  Expect.equal ds.ViewBox.Width 100.0 (sprintf "%s: canvas width moved" case.Name)
                  Expect.equal ds.ViewBox.Height 30.0 (sprintf "%s: canvas height moved" case.Name)

                  let _, style = polylineOf ds

                  Expect.equal
                      (style.Stroke |> Option.bind (BindingResolver.tryResolve noSources))
                      (Some "currentColor")
                      (sprintf "%s: the D8 currentColor chrome moved" case.Name)

                  Expect.equal
                      (style.StrokeWidth |> Option.bind (BindingResolver.tryResolve noSources))
                      (Some 1.5)
                      (sprintf "%s: stroke width moved" case.Name)

                  // The open-shape default: a data polyline never fills.
                  Expect.isNone style.Fill (sprintf "%s: the polyline declared a fill" case.Name)
          }

          test "the emitted SVG carries the shipped viewBox, stroke width and 2-dp coordinates" {
              let svg = svgOf (lowered (cases |> List.find (fun c -> c.Name = "normal")))

              Expect.stringContains svg "viewBox=\"0 0 100 30\"" "the shipped 100x30 canvas"
              Expect.stringContains svg "stroke=\"currentColor\"" "the D8 chrome"
              Expect.stringContains svg "stroke-width=\"1.5\"" "the shipped stroke width"
              Expect.stringContains svg "fill=\"none\"" "an open shape does not fill"
              // The 2-dp values the pre-1098 arm computed, in the shared
              // builder's number form (a whole value drops its decimals).
              Expect.stringContains svg "points=\"0,29 25,19.67 50,10.33 75,19.67 100,1\"" "the shipped geometry"
          }

          test "an empty series has nothing to draw — the caller renders its fallback" {
              Expect.isNone
                  (Charts.tryLowerSparkline Defaults.sparkline [])
                  "an empty series must not lower to an empty canvas"

              // The total form stays total, and yields the empty canvas.
              let total = Charts.lowerSparkline Defaults.sparkline []
              Expect.isEmpty total.Shapes "the total form draws nothing"
              Expect.equal total.ViewBox Charts.sparklineViewBox "the total form keeps the canvas"
          }

          test "a lowered sparkline carries no title or description" {
              // Stated rather than assumed: Phase 921's generated summary is a
              // CHART artefact minted from a `ChartSpec`, and a `Sparkline` has
              // none. Adding one here would be new cross-host contract text that
              // Phase 644 §4k did not admit and Phase 1099 would have to
              // reproduce byte-for-byte.
              for case in drawnCases do
                  let ds = lowered case
                  Expect.isNone ds.Title (sprintf "%s: unexpected title" case.Name)
                  Expect.isNone ds.Description (sprintf "%s: unexpected description" case.Name)
          } ]

// ─── Layer 1 — the cross-host goldens ────────────────────────────────────────

[<Tests>]
let sparklineLoweringCorpusTests =
    match tryFindFixtures () with
    | None ->
        testList
            "Sparkline lowering (cross-host contract)"
            [ test "fixtures absent — skipped (standalone checkout)" {
                  Expect.isTrue true "wire-format-fixtures/ not found"
              } ]
    | Some fixturesRoot ->
        let dir = Path.Combine(fixturesRoot, "sparkline-lowering")

        // Emit mode — (re)generate the input + expected goldens.
        if Environment.GetEnvironmentVariable "FUARAN_EMIT_SPARKLINE_LOWERING" = "1" then
            Directory.CreateDirectory dir |> ignore

            for case in cases do
                File.WriteAllText(Path.Combine(dir, case.Name + ".input.json"), inputJson case)
                File.WriteAllText(Path.Combine(dir, case.Name + ".expected.json"), loweredJson case)

        testList
            "Sparkline lowering (cross-host contract)"
            [ test "every case lowers byte-identically to its committed golden" {
                  for case in cases do
                      let expectedFile = Path.Combine(dir, case.Name + ".expected.json")

                      Expect.isTrue
                          (File.Exists expectedFile)
                          (sprintf "%s: golden missing (regenerate with FUARAN_EMIT_SPARKLINE_LOWERING=1)" case.Name)

                      Expect.equal
                          (loweredJson case)
                          (File.ReadAllText expectedFile)
                          (sprintf "%s: lowering drifted from golden" case.Name)
              }

              test "every case's neutral input contract is byte-identical to its committed golden" {
                  for case in cases do
                      let inputFile = Path.Combine(dir, case.Name + ".input.json")

                      Expect.isTrue
                          (File.Exists inputFile)
                          (sprintf "%s: input missing (regenerate with FUARAN_EMIT_SPARKLINE_LOWERING=1)" case.Name)

                      Expect.equal
                          (inputJson case)
                          (File.ReadAllText inputFile)
                          (sprintf "%s: neutral input drifted from golden" case.Name)
              }

              test "lowering is deterministic — two independent runs are byte-identical" {
                  for case in cases do
                      Expect.equal (loweredJson case) (loweredJson case) (sprintf "%s: non-deterministic" case.Name)
              }

              test "the family covers the degenerate classes the phase names" {
                  // A coverage assertion rather than a count: the value of this
                  // family to Phase 1099 is that every branch of the lowering has
                  // a vector, and a bare count would pass while a branch went
                  // uncovered.
                  let names = cases |> List.map _.Name |> Set.ofList

                  for required in [ "normal"; "single-point"; "flat"; "empty"; "nonfinite-sentinel" ] do
                      Expect.isTrue (names.Contains required) (sprintf "the %s vector is missing" required)
              } ]
