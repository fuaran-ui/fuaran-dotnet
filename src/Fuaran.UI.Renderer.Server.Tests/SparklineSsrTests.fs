module Fuaran.UI.Renderer.Server.Tests.SparklineSsrTests

// ============================================================================
//  Phase 1098 — the SSR half of the `Sparkline → Drawing` lowering.
//
//  Before this phase the server emitted an em-dash placeholder for EVERY
//  `Sparkline` and never read the series, which is why `render-fidelity.json`
//  declared the kind `"class": "clientOnly"` — the only geometry-bearing kind in
//  the vocabulary excluded from parity by contract. It now lowers and draws.
//
//  HOW THE "identical bytes on the client and the SSR path" CLAIM IS PROVEN
//  HERE, and what that proof is worth. The Feliz client renderer cannot render
//  to an HTML string on .NET — its `ReactElement` is opaque, which is the whole
//  reason `Feliz.ViewEngine` exists as a separate backend — so a literal
//  client-vs-server byte diff is not expressible in any .NET suite (the same
//  limitation `SsrParityTests` opens by stating). What IS expressible, and what
//  these tests assert, is the property that makes the two sides equal: the
//  server's bytes are EXACTLY `DrawingSvg.render` over `Charts.tryLowerSparkline`
//  — the identical pair the client arm calls — wrapped in the declared hook
//  element. One builder, one lowering, and the server's output recomputed here
//  from that pair rather than copied from an expectation string.
//
//  So a divergence can only arise by one arm ceasing to call the pair, which is
//  a change to the call site the reviewer sees, rather than by the two hand-
//  written copies drifting silently — which is the failure mode Phase 644 §4k
//  measured across three hosts and this phase exists to remove.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server

let private contains (needle: string) (haystack: string) =
    haystack.Contains(needle, System.StringComparison.Ordinal)

/// A `Sparkline` node over a statically-bound series — the shape a resolved
/// server render sees.
let private sparkline (series: float list option) : Node<obj> =
    { Id = "spark"
      Kind = NodeKind.Sparkline { Source = Binding.Static series }
      Accessibility = Option.None
      ExtraAttributes = Option.None
      Tooltip = Option.None
      Motion = Option.None
      State = Option.None
      Style = Option.None }

let private renderSpark (series: float list option) : string =
    Render.render BindingResolver.empty (sparkline series)

/// The em-dash fallback element, spelled once.
[<Literal>]
let private emptyElement = "class=\"fuaran-sparkline fuaran-sparkline-empty\""

[<Tests>]
let sparklineSsrTests =
    testList
        "Sparkline SSR (Phase 1098)"
        [ test "a resolved series renders the lowered drawing, not the placeholder" {
              let html = renderSpark (Some [ 1.0; 2.0; 3.0; 2.0; 4.0 ])

              Expect.isFalse (contains "—" html) "the em-dash placeholder must not survive a resolved series"
              Expect.isTrue (contains "class=\"fuaran-sparkline\"" html) "the declared hook element"
              Expect.isTrue (contains "<svg class=\"fuaran-drawing\"" html) "the shared builder's root"
              Expect.isTrue (contains "<polyline class=\"fuaran-drawing-polyline\"" html) "the lowered geometry"
          }

          test "the server's bytes ARE the shared lowering through the shared builder" {
              // The load-bearing assertion. Recomputed from the same pair the
              // client arm calls, so it cannot pass by agreeing with a stale
              // expectation string.
              let series = [ 1.0; 2.0; 3.0; 2.0; 4.0 ]

              let expectedSvg =
                  Charts.tryLowerSparkline { Source = Binding.Static(Some series) } series
                  |> Option.defaultWith (fun () -> failwith "expected a drawing")
                  |> DrawingSvg.render BindingResolver.empty (fun _ -> "")

              Expect.isTrue (contains expectedSvg (renderSpark (Some series))) "the server emitted different bytes"
          }

          test "the shipped geometry is what the server draws" {
              let html = renderSpark (Some [ 1.0; 2.0; 3.0; 2.0; 4.0 ])

              Expect.isTrue (contains "viewBox=\"0 0 100 30\"" html) "the shipped 100x30 canvas"
              Expect.isTrue (contains "stroke=\"currentColor\"" html) "the D8 chrome"
              Expect.isTrue (contains "stroke-width=\"1.5\"" html) "the shipped stroke width"

              Expect.isTrue
                  (contains "points=\"0,29 25,19.67 50,10.33 75,19.67 100,1\"" html)
                  "the pre-1098 coordinates, unmoved"
          }

          test "an UNRESOLVED series keeps the em-dash placeholder" {
              let html = renderSpark None
              Expect.isTrue (contains emptyElement html) "the declared fallback element"
              Expect.isTrue (contains "—" html) "the em-dash stand-in"
              Expect.isFalse (contains "fuaran-drawing" html) "nothing to draw, so nothing is drawn"
          }

          test "an EMPTY series keeps the em-dash placeholder" {
              let html = renderSpark (Some [])
              Expect.isTrue (contains emptyElement html) "the declared fallback element"
              Expect.isTrue (contains "—" html) "the em-dash stand-in"
              Expect.isFalse (contains "fuaran-drawing" html) "an empty series must not lower to an empty canvas"
          }

          test "a single-point series draws, centred — a degenerate series is still a series" {
              let html = renderSpark (Some [ 42.0 ])
              Expect.isTrue (contains "<polyline class=\"fuaran-drawing-polyline\"" html) "the lone point is drawn"
              Expect.isTrue (contains "points=\"50,29\"" html) "the shipped lone-point placement"
          } ]
