module Fuaran.UI.Tests.MathMlTests

// Phase 658 — byte-for-byte cover for `Fuaran.UI.Renderer.MathMl.translate`, the
// deterministic LaTeX→MathML translator for the closed `Math` subset. This is the
// F# half of the shared fixture-table oracle in `docs/MATH-DEGRADATION.md`; the TS
// port (`fuaran-ts/packages/renderer/test/mathMl.test.ts`) pins the SAME strings,
// so the two implementations cannot silently diverge.

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Renderer

let private mathTag (disp: string) =
    sprintf "<math xmlns=\"http://www.w3.org/1998/Math/MathML\" display=\"%s\">" disp

[<Tests>]
let mathMlTranslateTests =
    testList
        "MathMl.translate (Phase 658)"
        [ // ── In-subset → exact MathML (the design-doc fixture table) ──────────
          test "1. x^2 (inline) → msup" {
              Expect.equal
                  (MathMl.translate "x^2" MathDisplay.Inline)
                  (Some(mathTag "inline" + "<msup><mi>x</mi><mn>2</mn></msup></math>"))
                  "single superscript"
          }

          test "2. a^2 + b^2 = c^2 (block) → the pythagorean, real superscripts" {
              Expect.equal
                  (MathMl.translate "a^2 + b^2 = c^2" MathDisplay.Block)
                  (Some(
                      mathTag "block"
                      + "<msup><mi>a</mi><mn>2</mn></msup><mo>+</mo><msup><mi>b</mi><mn>2</mn></msup><mo>=</mo><msup><mi>c</mi><mn>2</mn></msup></math>"
                  ))
                  "a^2 + b^2 = c^2"
          }

          test "3. x^2 + y^2 = z^2 (block) — the wire-format corpus node math-1" {
              Expect.equal
                  (MathMl.translate "x^2 + y^2 = z^2" MathDisplay.Block)
                  (Some(
                      mathTag "block"
                      + "<msup><mi>x</mi><mn>2</mn></msup><mo>+</mo><msup><mi>y</mi><mn>2</mn></msup><mo>=</mo><msup><mi>z</mi><mn>2</mn></msup></math>"
                  ))
                  "corpus math-1"
          }

          test "4. x_i (inline) → msub" {
              Expect.equal
                  (MathMl.translate "x_i" MathDisplay.Inline)
                  (Some(mathTag "inline" + "<msub><mi>x</mi><mi>i</mi></msub></math>"))
                  "single subscript"
          }

          test "5. x_i^2 (inline) → msubsup" {
              Expect.equal
                  (MathMl.translate "x_i^2" MathDisplay.Inline)
                  (Some(mathTag "inline" + "<msubsup><mi>x</mi><mi>i</mi><mn>2</mn></msubsup></math>"))
                  "sub and super on one base"
          }

          test "6. \\frac{a}{b} (block) → mfrac" {
              Expect.equal
                  (MathMl.translate "\\frac{a}{b}" MathDisplay.Block)
                  (Some(mathTag "block" + "<mfrac><mi>a</mi><mi>b</mi></mfrac></math>"))
                  "fraction"
          }

          test "7. \\alpha + \\beta (inline) → Greek identifiers" {
              Expect.equal
                  (MathMl.translate "\\alpha + \\beta" MathDisplay.Inline)
                  (Some(mathTag "inline" + "<mi>α</mi><mo>+</mo><mi>β</mi></math>"))
                  "Greek letters"
          }

          test "8. (a + b)^2 (block) → mrow group with superscript" {
              Expect.equal
                  (MathMl.translate "(a + b)^2" MathDisplay.Block)
                  (Some(
                      mathTag "block"
                      + "<msup><mrow><mo>(</mo><mi>a</mi><mo>+</mo><mi>b</mi><mo>)</mo></mrow><mn>2</mn></msup></math>"
                  ))
                  "parenthesised base of a superscript"
          }

          test "9. 3.14 (inline) → mn with decimal" {
              Expect.equal
                  (MathMl.translate "3.14" MathDisplay.Inline)
                  (Some(mathTag "inline" + "<mn>3.14</mn></math>"))
                  "decimal number"
          }

          test "10. E = mc^2 (block) → mixed identifiers + superscript" {
              Expect.equal
                  (MathMl.translate "E = mc^2" MathDisplay.Block)
                  (Some(
                      mathTag "block"
                      + "<mi>E</mi><mo>=</mo><mi>m</mi><msup><mi>c</mi><mn>2</mn></msup></math>"
                  ))
                  "mass-energy"
          }

          test "11. a / b (inline) → division operator" {
              Expect.equal
                  (MathMl.translate "a / b" MathDisplay.Inline)
                  (Some(mathTag "inline" + "<mi>a</mi><mo>/</mo><mi>b</mi></math>"))
                  "division"
          }

          test "12. 2 * x (inline) → dot-operator multiplication (U+22C5)" {
              Expect.equal
                  (MathMl.translate "2 * x" MathDisplay.Inline)
                  (Some(mathTag "inline" + "<mn>2</mn><mo>⋅</mo><mi>x</mi></math>"))
                  "multiplication maps to the dot operator"
          }

          test "13. n - 1 (inline) → minus-sign subtraction (U+2212)" {
              Expect.equal
                  (MathMl.translate "n - 1" MathDisplay.Inline)
                  (Some(mathTag "inline" + "<mi>n</mi><mo>−</mo><mn>1</mn></math>"))
                  "subtraction maps to the minus sign"
          }

          // ── Out-of-subset → None (the renderer falls back to the source span) ─
          test "14. \\sqrt{2} → None (unknown command)" {
              Expect.equal (MathMl.translate "\\sqrt{2}" MathDisplay.Block) None "\\sqrt out of subset"
          }

          test "15. x < y → None (< not in the alphabet)" {
              Expect.equal (MathMl.translate "x < y" MathDisplay.Inline) None "< out of subset"
          }

          test "16. \\int_0^1 x \\, dx → None" {
              Expect.equal (MathMl.translate "\\int_0^1 x \\, dx" MathDisplay.Block) None "\\int / \\, out of subset"
          }

          test "17. empty / whitespace → None" {
              Expect.equal (MathMl.translate "" MathDisplay.Inline) None "empty source"
              Expect.equal (MathMl.translate "   " MathDisplay.Inline) None "whitespace-only source"
          }

          test "18. f(x) = \\sin(x) → None (unknown command)" {
              Expect.equal (MathMl.translate "f(x) = \\sin(x)" MathDisplay.Block) None "\\sin out of subset"
          }

          test "19. a^ → None (dangling superscript)" {
              Expect.equal (MathMl.translate "a^" MathDisplay.Inline) None "missing script atom"
          }

          test "20. {a + b → None (unbalanced brace)" {
              Expect.equal (MathMl.translate "{a + b" MathDisplay.Inline) None "unbalanced group"
          }

          // never-crash: a spread of hostile inputs must all return None, never throw
          test "never crashes on hostile input" {
              for s in [ "^"; "_"; ")"; "}"; "\\"; "\\frac{a}"; "((("; "a__b"; "1.2.3"; "\\frac" ] do
                  Expect.equal
                      (MathMl.translate s MathDisplay.Inline)
                      None
                      (sprintf "'%s' is out of subset, not an error" s)
          } ]
