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

// Phase 1853 — the subset grows logic, relations and named terms. Rows 21–49 of
// the design-doc fixture table, pinned byte-for-byte here and in the TS port.
let private inSubset1853: (int * string * MathDisplay * string) list =
    [ 21,
      "\\forall x.\\ x \\in S \\Rightarrow f(x) \\le c",
      MathDisplay.Block,
      "<mo>∀</mo><mi>x</mi><mo>.</mo><mspace width=\"0.3333em\"></mspace><mi>x</mi><mo>∈</mo><mi>S</mi><mo>⇒</mo><mi>f</mi><mrow><mo>(</mo><mi>x</mi><mo>)</mo></mrow><mo>≤</mo><mi>c</mi>"
      22,
      "\\exists n, n \\ge 0",
      MathDisplay.Inline,
      "<mo>∃</mo><mi>n</mi><mo separator=\"true\">,</mo><mi>n</mi><mo>≥</mo><mn>0</mn>"
      23,
      "p \\land q \\lor \\neg r \\iff \\top",
      MathDisplay.Inline,
      "<mi>p</mi><mo>∧</mo><mi>q</mi><mo>∨</mo><mo>¬</mo><mi>r</mi><mo>⇔</mo><mi>⊤</mi>"
      24, "\\Gamma \\vdash e \\mapsto v", MathDisplay.Inline, "<mi>Γ</mi><mo>⊢</mo><mi>e</mi><mo>↦</mo><mi>v</mi>"
      25,
      "A \\subseteq B \\cup C \\cap D",
      MathDisplay.Inline,
      "<mi>A</mi><mo>⊆</mo><mi>B</mi><mo>∪</mo><mi>C</mi><mo>∩</mo><mi>D</mi>"
      26,
      "x \\notin \\emptyset, a \\ne b, a \\equiv b",
      MathDisplay.Inline,
      "<mi>x</mi><mo>∉</mo><mi>∅</mi><mo separator=\"true\">,</mo><mi>a</mi><mo>≠</mo><mi>b</mi><mo separator=\"true\">,</mo><mi>a</mi><mo>≡</mo><mi>b</mi>"
      27, "a \\lt b \\gt c", MathDisplay.Inline, "<mi>a</mi><mo>&lt;</mo><mi>b</mi><mo>&gt;</mo><mi>c</mi>"
      28, "2 \\times 3 \\cdot 4", MathDisplay.Inline, "<mn>2</mn><mo>×</mo><mn>3</mn><mo>⋅</mo><mn>4</mn>"
      29,
      "\\mathit{unregistered\\_refused}(k) \\to \\bot",
      MathDisplay.Inline,
      "<mi mathvariant=\"italic\">unregistered_refused</mi><mrow><mo>(</mo><mi>k</mi><mo>)</mo></mrow><mo>→</mo><mi>⊥</mi>"
      30,
      "\\mathrm{Dom}(f) \\subset \\text{Keys}",
      MathDisplay.Inline,
      "<mi mathvariant=\"normal\">Dom</mi><mrow><mo>(</mo><mi>f</mi><mo>)</mo></mrow><mo>⊂</mo><mtext>Keys</mtext>"
      31,
      "a\\,b\\:c\\;d\\quad e",
      MathDisplay.Inline,
      "<mi>a</mi><mspace width=\"0.1667em\"></mspace><mi>b</mi><mspace width=\"0.2222em\"></mspace><mi>c</mi><mspace width=\"0.2778em\"></mspace><mi>d</mi><mspace width=\"1em\"></mspace><mi>e</mi>"
      32,
      "[a, b]^2",
      MathDisplay.Inline,
      "<msup><mrow><mo>[</mo><mi>a</mi><mo separator=\"true\">,</mo><mi>b</mi><mo>]</mo></mrow><mn>2</mn></msup>"
      33,
      "\\{a, b\\}",
      MathDisplay.Inline,
      "<mrow><mo>{</mo><mi>a</mi><mo separator=\"true\">,</mo><mi>b</mi><mo>}</mo></mrow>"
      34, "|x| \\le 1", MathDisplay.Inline, "<mo>|</mo><mi>x</mi><mo>|</mo><mo>≤</mo><mn>1</mn>"
      35, "x^{n+1}", MathDisplay.Inline, "<msup><mi>x</mi><mrow><mi>n</mi><mo>+</mo><mn>1</mn></mrow></msup>"
      36, "\\frac{a+b}{2}", MathDisplay.Block, "<mfrac><mrow><mi>a</mi><mo>+</mo><mi>b</mi></mrow><mn>2</mn></mfrac>" ]

let private outOfSubset1853: (int * string * string) list =
    [ 37, "x > y", "bare > is not in the alphabet"
      38, "a & b", "bare & is not in the alphabet"
      39, "\\mathit{a b}", "a named-term argument containing a space"
      40, "\\mathit{}", "an empty named-term argument"
      41, "\\text{a-b}", "a named-term argument outside [A-Za-z0-9] and \\_"
      42, "\\forallx", "unknown command (a command name is the whole letter run)"
      43, ".5", "a dot directly followed by a digit outside a number"
      44, "[a, b)", "mismatched fences"
      45, "\\{a}", "an unclosed set brace"
      46, "x^{}", "an empty brace group"
      47, "|x|^2", "a script on the bare | operator"
      48, "\\qquad", "\\qquad is not in the spacing table"
      49, "\\mathit{a_b}", "a bare _ in a named-term argument (a subscript in LaTeX; write \\_)" ]

// every alias pair of the operator table maps to the same <mo>
let private aliases1853: (string * string) list =
    [ "\\neg", "\\lnot"
      "\\land", "\\wedge"
      "\\lor", "\\vee"
      "\\Rightarrow", "\\implies"
      "\\Leftrightarrow", "\\iff"
      "\\to", "\\rightarrow"
      "\\le", "\\leq"
      "\\ge", "\\geq"
      "\\ne", "\\neq" ]

[<Tests>]
let mathMlSubset1853Tests =
    testList
        "MathMl.translate (Phase 1853 — logic, relations, named terms)"
        [ for (n, src, disp, body) in inSubset1853 do
              test (sprintf "%d. %s → exact MathML" n src) {
                  let d =
                      match disp with
                      | MathDisplay.Block -> "block"
                      | MathDisplay.Inline -> "inline"

                  Expect.equal (MathMl.translate src disp) (Some(mathTag d + body + "</math>")) src
              }

          for (n, src, why) in outOfSubset1853 do
              test (sprintf "%d. %s → None (%s)" n src why) {
                  Expect.equal (MathMl.translate src MathDisplay.Inline) None why
              }

          test "the operator aliases translate identically" {
              for (a, b) in aliases1853 do
                  let ta = MathMl.translate ("x " + a + " y") MathDisplay.Inline
                  Expect.isSome ta (sprintf "%s is in subset" a)
                  Expect.equal (MathMl.translate ("x " + b + " y") MathDisplay.Inline) ta (sprintf "%s = %s" a b)
          }

          // The escaping-floor closure: every emitted payload survives the renderer's
          // sanitising floor byte-for-byte (the floor the Trusted Types policy runs),
          // including the pre-escaped `&lt;` / `&gt;` entries and the new
          // `separator` / `mathvariant` / `width` attributes.
          test "every new in-subset payload is invariant under the sanitising floor" {
              for (_, src, disp, _) in inSubset1853 do
                  match MathMl.translate src disp with
                  | Some markup -> Expect.equal (Sanitize.sanitizeMarkdownHtml markup) markup src
                  | None -> failtest (sprintf "%s must translate" src)
          }

          // The only `&` ever emitted starts one of the two fixed entities.
          test "the only character references emitted are &lt; and &gt;" {
              for (_, src, disp, _) in inSubset1853 do
                  match MathMl.translate src disp with
                  | Some markup ->
                      let stripped = markup.Replace("&lt;", "").Replace("&gt;", "")
                      Expect.isFalse (stripped.Contains "&") (sprintf "%s emits no other &" src)
                  | None -> failtest (sprintf "%s must translate" src)
          }

          test "never crashes on hostile input around the new constructs" {
              for s in
                  [ "\\"
                    "\\ "
                    "\\{"
                    "\\}"
                    "\\mathit"
                    "\\mathit{"
                    "\\mathit{a"
                    "\\text{}"
                    "["
                    "]"
                    "[a"
                    "a]"
                    "\\{a\\}\\}"
                    "."
                    ","
                    "|"
                    "\\lt"
                    "\\forall" ] do
                  // total: a string or None, never an exception
                  MathMl.translate s MathDisplay.Inline |> ignore
          } ]
