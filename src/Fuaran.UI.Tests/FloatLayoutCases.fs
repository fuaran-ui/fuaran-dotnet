module Fuaran.UI.Tests.FloatLayoutCases

// ============================================================================
//  Phase 2318 — the tier's float layouts are Core's, on both pipelines.
//
//  `JsonHost.serialize` (the AI wire) and `DrawingSvg.formatNum` (an SVG
//  coordinate) each lay a non-integral double out through
//  `Fuaran.Core.FloatLayout.finite`. Before that, the AI wire wrote `string f`,
//  which is `1E-07` on .NET and `1e-7` under Fable, and the SVG path kept a
//  hand copy of the Fable re-lay. The .NET suite (`FloatLayoutTests.fs`) and
//  the Fable law harness (`tests/fable-laws/FloatLayoutLaw.fs`, which links
//  this file) run the SAME cases against the SAME expected bytes, because the
//  defect showed on one pipeline only.
//
//  NOTHING HERE MAY USE A CONSTRUCT FABLE CANNOT LOWER: no Expecto, no
//  reflection, no `StringComparer`.
// ============================================================================

open Fuaran.UI.AiWire

/// (name, value, the bytes `JsonHost.serialize (JNumber value)` writes on every host).
let jsonNumberCases: (string * float * string) list =
    [ "small fraction", 1e-7, "1E-07"
      "below the JS fixed band", 1e-5, "1E-05"
      "past the int64 shortcut", 1.5e15, "1500000000000000"
      "past the JS fixed band", 1e17, "1E+17"
      "large exponent", 1e21, "1E+21"
      "ordinary fraction", 0.1, "0.1"
      "negative fraction", -2.5, "-2.5"
      "integral", 42.0, "42"
      "NaN is null", nan, "null"
      "infinity is null", infinity, "null" ]

/// (name, value, the bytes `DrawingSvg.formatNum value` writes on every host).
let svgNumberCases: (string * float * string) list =
    [ "small fraction", 1e-7, "1E-07"
      "below the JS fixed band", 1e-5, "1E-05"
      "past the whole-value shortcut", 1.5e15, "1500000000000000"
      "large exponent", 1e21, "1E+21"
      "half", 0.5, "0.5"
      "negative fraction", -0.25, "-0.25"
      "whole", 10.0, "10"
      "NaN is 0", nan, "0" ]

/// Every case whose output differs from its pinned bytes, as `surface: name: got <bytes>`.
let refutations () : string list =
    [ for name, v, expected in jsonNumberCases do
          let got = JsonHost.serialize (JNumber v)

          if got <> expected then
              yield "JsonHost: " + name + ": got " + got

      for name, v, expected in svgNumberCases do
          let got = Fuaran.UI.Renderer.DrawingSvg.formatNum v

          if got <> expected then
              yield "DrawingSvg: " + name + ": got " + got ]
