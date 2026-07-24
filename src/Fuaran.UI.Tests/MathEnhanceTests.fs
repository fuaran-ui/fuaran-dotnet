module Fuaran.UI.Tests.MathEnhanceTests

// Phase 293 — unit cover for the native F# `MathEnhance.parseSegments`, the
// pure (browser-free) half of the F# twin of the TS `@fuaran-ui/renderer`
// `enhanceMath`. Mirrors the TS `parseMathSegments` vitest cases so the two
// ports can't silently diverge. (The DOM-walking + KaTeX-interop half is
// `#if FABLE_COMPILER`-only and verified by transpilation + the TS tests.)

open Expecto
open Fuaran.UI.Renderer.MathEnhance

[<Tests>]
let mathEnhanceParserTests =
    testList
        "MathEnhance.parseSegments (Phase 293)"
        [ test "no math → a single text segment" {
              Expect.equal (parseSegments "plain text") [ Text, "plain text" ] "plain text is one Text segment"
          }

          test "inline $…$ splits into text / inline / text" {
              Expect.equal
                  (parseSegments "a $x^2$ b")
                  [ Text, "a "; Inline, "x^2"; Text, " b" ]
                  "inline math splits around the $…$ span"
          }

          test "display $$…$$ splits" {
              Expect.equal
                  (parseSegments "$$\\int_0^1 x$$")
                  [ Display, "\\int_0^1 x" ]
                  "display math is one Display segment"
          }

          test "escaped \\$ is a literal dollar, not a delimiter" {
              Expect.equal
                  (parseSegments "cost is \\$5 today")
                  [ Text, "cost is $5 today" ]
                  "\\$ collapses to a literal $"
          }

          test "an unterminated $ stays literal text" {
              Expect.equal
                  (parseSegments "a $ b with no close")
                  [ Text, "a $ b with no close" ]
                  "no closing delimiter ⇒ literal"
          }

          test "an empty $$ is not treated as math" {
              Expect.equal (parseSegments "a $$ b") [ Text, "a $$ b" ] "empty delimiters stay literal"
          }

          test "two inline spans in one run" {
              Expect.equal
                  (parseSegments "$a$ and $b$")
                  [ Inline, "a"; Text, " and "; Inline, "b" ]
                  "both inline spans are parsed"
          } ]
