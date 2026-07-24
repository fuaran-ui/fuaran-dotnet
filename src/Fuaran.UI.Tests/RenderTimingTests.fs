module Fuaran.UI.Tests.RenderTimingTests

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

// ============================================================================
//  Phase 202 — RenderTiming pure-surface tests (.NET / Expecto).
//
//  Pins the both-pipelines surface of the render-latency probe: the monotonic
//  clock is non-decreasing, the probe's first-paint mark is idempotent and
//  ordered before full-render, node-count matches the structural walk, and the
//  aggregation computes min / mean / p50 / p95 / max with NaN (un-taken mark)
//  values dropped. The browser `performance.now()` path is exercised only in
//  fuaran-live's measurement harness; everything here is host-free.
// ============================================================================

let private tree: Node<unit> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<unit> with
            Children =
                [ Fuaran.markdown "a" "A"
                  Fuaran.markdown "b" "B"
                  Fuaran.dashboard
                      "inner"
                      { Defaults.dashboard<unit> with
                          Children = [ Fuaran.markdown "c" "C" ] } ] }

[<Tests>]
let tests =
    testList
        "RenderTiming (Phase 202 render-latency probe)"
        [ test "nowMs is monotonic non-decreasing" {
              let t0 = RenderTiming.nowMs ()
              // Spin briefly so the clock can advance without a sleep.
              let mutable acc = 0

              for i in 1..100000 do
                  acc <- acc + i

              let t1 = RenderTiming.nowMs ()
              Expect.isGreaterThanOrEqual t1 t0 "the monotonic clock never goes backwards"
              Expect.notEqual acc 0 "keep the spin loop from being optimised away"
          }

          test "countNodes matches the structural walk (root + 3 leaves + 1 inner)" {
              Expect.equal (RenderTiming.countNodes tree) 5 "root + a + b + inner + c"
          }

          test "probe: first-paint is idempotent and ordered before full-render" {
              let probe = RenderTiming.Probe("Small", RenderTiming.countNodes tree)
              probe.MarkFirstPaint()
              let firstSample = probe.Sample
              // A second MarkFirstPaint must NOT move the recorded first paint.
              probe.MarkFirstPaint()
              probe.MarkFullRender()
              let s = probe.Sample

              Expect.equal s.FirstPaintMs firstSample.FirstPaintMs "first-paint mark is idempotent (first call wins)"
              Expect.isGreaterThanOrEqual s.FirstPaintMs 0.0 "first-paint delta is non-negative"
              Expect.isGreaterThanOrEqual s.FullRenderMs s.FirstPaintMs "full render is at or after first paint"
              Expect.equal s.Label "Small" "the label round-trips"
              Expect.equal s.NodeCount 5 "the node count round-trips"
          }

          test "an un-taken mark surfaces as NaN, not zero" {
              let probe = RenderTiming.Probe("NoPaint", 1)
              probe.MarkFullRender()
              let s = probe.Sample
              Expect.isTrue (System.Double.IsNaN s.FirstPaintMs) "first-paint was never marked → NaN"
              Expect.isFalse (System.Double.IsNaN s.FullRenderMs) "full render was marked → a real number"
          }

          test "aggregate computes min / mean / p50 / p95 / max, dropping NaN" {
              let values = [ 10.0; 20.0; 30.0; 40.0; nan; 50.0 ]
              let stat = RenderTiming.aggregate "render.full.test.ms" values
              Expect.equal stat.Samples 5 "the NaN value is dropped from the sample count"
              Expect.equal stat.Min 10.0 "min"
              Expect.equal stat.Max 50.0 "max"
              Expect.equal stat.Mean 30.0 "mean of 10..50"
              Expect.equal stat.P50 30.0 "nearest-rank p50 of [10;20;30;40;50]"
              Expect.equal stat.P95 50.0 "nearest-rank p95"
          }

          test "aggregate of all-NaN yields an empty (Samples=0) stat" {
              let stat = RenderTiming.aggregate "render.ttfp.empty.ms" [ nan; nan ]
              Expect.equal stat.Samples 0 "nothing usable was captured"
              Expect.isTrue (System.Double.IsNaN stat.Mean) "mean of nothing is NaN"
          } ]
