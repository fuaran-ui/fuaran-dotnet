module Fuaran.UI.Tests.CodeHighlightTests

// Phase 1854 — byte-for-byte cover for `Fuaran.UI.Renderer.CodeHighlight`, the
// deterministic CodeBlock highlighting tier. This is the F# half of the shared
// fixture-table oracle in `docs/CODE-HIGHLIGHT.md` §5; the TS port
// (`fuaran-ts/packages/renderer/test/codeHighlight.test.ts`) pins the SAME
// strings, so the two implementations cannot silently diverge.

open System
open System.IO
open System.Text.RegularExpressions
open Expecto
open Fuaran.UI.Renderer

/// The normative fixture table (docs/CODE-HIGHLIGHT.md §5), row for row.
let fixtures: (int * string * string * string option) list =
    [ 1,
      "fstar",
      "val f : x:nat -> Tot nat",
      Some
          "<span class=\"tok-kw\">val</span> f <span class=\"tok-op\">:</span> x<span class=\"tok-op\">:</span><span class=\"tok-ty\">nat</span> <span class=\"tok-op\">-&gt;</span> <span class=\"tok-ty\">Tot</span> <span class=\"tok-ty\">nat</span>"
      2,
      "fstar",
      "assume val lemma_pos : n:nat -> Lemma (requires n > 0) (ensures n >= 1)",
      Some
          "<span class=\"tok-kw\">assume</span> <span class=\"tok-kw\">val</span> lemma_pos <span class=\"tok-op\">:</span> n<span class=\"tok-op\">:</span><span class=\"tok-ty\">nat</span> <span class=\"tok-op\">-&gt;</span> <span class=\"tok-ty\">Lemma</span> (<span class=\"tok-kw\">requires</span> n <span class=\"tok-op\">&gt;</span> <span class=\"tok-num\">0</span>) (<span class=\"tok-kw\">ensures</span> n <span class=\"tok-op\">&gt;=</span> <span class=\"tok-num\">1</span>)"
      3,
      "fstar",
      "p ==> q <==> r /\\ s \\/ t =!= u",
      Some
          "p <span class=\"tok-op\">==&gt;</span> q <span class=\"tok-op\">&lt;==&gt;</span> r <span class=\"tok-op\">/\\</span> s <span class=\"tok-op\">\\/</span> t <span class=\"tok-op\">=!=</span> u"
      4,
      "fstar",
      "(* outer (* inner *) still outer *) let",
      Some "<span class=\"tok-com\">(* outer (* inner *) still outer *)</span> <span class=\"tok-kw\">let</span>"
      5,
      "fstar",
      "(* one\n   two *)\nval x : int",
      Some
          "<span class=\"tok-com\">(* one</span>\n<span class=\"tok-com\">   two *)</span>\n<span class=\"tok-kw\">val</span> x <span class=\"tok-op\">:</span> <span class=\"tok-ty\">int</span>"
      6,
      "fsharp",
      "let s = \"open\nlet t = 1",
      Some
          "<span class=\"tok-kw\">let</span> s <span class=\"tok-op\">=</span> <span class=\"tok-str\">\"open</span>\n<span class=\"tok-str\">let t = 1</span>"
      7, "python", "if a < b: pass", None
      8,
      "fsharp",
      "/// doc\nlet x = 0x1Fu + 1.5e-3 // tail",
      Some
          "<span class=\"tok-com\">/// doc</span>\n<span class=\"tok-kw\">let</span> x <span class=\"tok-op\">=</span> <span class=\"tok-num\">0x1Fu</span> <span class=\"tok-op\">+</span> <span class=\"tok-num\">1.5e-3</span> <span class=\"tok-com\">// tail</span>"
      9,
      "fsharp",
      "@\"C:\\dir\" + \"\"\"say \"hi\" \"\"\" + \"a\\\"b\"",
      Some
          "<span class=\"tok-str\">@\"C:\\dir\"</span> <span class=\"tok-op\">+</span> <span class=\"tok-str\">\"\"\"say \"hi\" \"\"\"</span> <span class=\"tok-op\">+</span> <span class=\"tok-str\">\"a\\\"b\"</span>"
      10,
      "fsharp",
      "let c = 'x' in List.fold (*) 1 [ 'a'; '\\n' ]",
      Some
          "<span class=\"tok-kw\">let</span> c <span class=\"tok-op\">=</span> <span class=\"tok-str\">'x'</span> <span class=\"tok-kw\">in</span> List.fold <span class=\"tok-op\">(*)</span> <span class=\"tok-num\">1</span> [ <span class=\"tok-str\">'a'</span>; <span class=\"tok-str\">'\\n'</span> ]"
      11,
      "fsharp",
      "let id<'a> (x: 'a) = x <> \"<b>\" && true",
      Some
          "<span class=\"tok-kw\">let</span> id<span class=\"tok-op\">&lt;</span>'a<span class=\"tok-op\">&gt;</span> (x<span class=\"tok-op\">:</span> 'a) <span class=\"tok-op\">=</span> x <span class=\"tok-op\">&lt;&gt;</span> <span class=\"tok-str\">\"&lt;b&gt;\"</span> <span class=\"tok-op\">&amp;&amp;</span> <span class=\"tok-kw\">true</span>"
      12, "fstar", "", Some ""
      13, "FST", "open FStar.Mul", Some "<span class=\"tok-kw\">open</span> FStar.Mul"
      14, "F#", "module M", Some "<span class=\"tok-kw\">module</span> M"
      15,
      "fstar",
      "(* a (* b *)\nlet",
      Some "<span class=\"tok-com\">(* a (* b *)</span>\n<span class=\"tok-com\">let</span>"
      16, "fsharp", "x+// c", Some "x<span class=\"tok-op\">+</span><span class=\"tok-com\">// c</span>"
      17, "fsharp", "[1..10]", Some "[<span class=\"tok-num\">1</span>..<span class=\"tok-num\">10</span>]"
      18,
      "fsharp",
      "let π = \"∀\"",
      Some "<span class=\"tok-kw\">let</span> π <span class=\"tok-op\">=</span> <span class=\"tok-str\">\"∀\"</span>" ]

// Strip the spans and undo the `& < >` escape: what is left must be the source.
let private textOf (markup: string) : string =
    Regex.Replace(markup, "</?span[^>]*>", "").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&")

// A deterministic pseudo-random source over the characters every rule keys on.
// The lone surrogate U+D800 is spliced in with `char`, because the F# compiler
// replaces a lone-surrogate `\u` escape in a string literal with U+FFFD.
let private alphabet =
    "(*)/\\\"'@\n 0123456789abcdefxXeE_.+-=<>&|!:;[]{}#λ∀"
    + string (char 0xD800)
    + "\r\tletvalopenmodule"

let private generated (count: int) : string list =
    let mutable seed = 1854u

    let next () =
        seed <- seed * 1664525u + 1013904223u
        int (seed >>> 8)

    [ for _ in 1..count ->
          let len = next () % 40
          String(Array.init len (fun _ -> alphabet[next () % alphabet.Length])) ]

[<Tests>]
let codeHighlightTests =
    testList
        "CodeHighlight (Phase 1854)"
        [ for (n, lang, src, expected) in fixtures do
              test (sprintf "fixture %d (%s)" n lang) {
                  Expect.equal (CodeHighlight.highlight lang src) expected (sprintf "fixture %d" n)
              }

          test "the language tag resolves by alias, ASCII case-insensitively" {
              for tag in [ "fstar"; "fst"; "FStar"; "FST" ] do
                  Expect.equal (CodeHighlight.grammarFor tag |> Option.map _.Name) (Some "fstar") tag

              for tag in [ "fsharp"; "fs"; "f#"; "FSharp"; "F#" ] do
                  Expect.equal (CodeHighlight.grammarFor tag |> Option.map _.Name) (Some "fsharp") tag

              for tag in [ ""; "python"; "fsx"; "fsi"; "fstar "; "text" ] do
                  Expect.isNone (CodeHighlight.grammarFor tag) (sprintf "'%s' has no grammar" tag)
          }

          test "total and text-preserving: no input throws, and the markup's text IS the source" {
              for g in [ CodeHighlight.fstar; CodeHighlight.fsharp ] do
                  for src in generated 3000 do
                      let markup = CodeHighlight.highlightWith g src
                      Expect.equal (textOf markup) src (sprintf "%s: %A" g.Name src)
          }

          test "no span ever contains a line break" {
              for g in [ CodeHighlight.fstar; CodeHighlight.fsharp ] do
                  for src in generated 3000 do
                      let markup = CodeHighlight.highlightWith g src

                      for m in Regex.Matches(markup, "<span class=\"tok-[a-z]+\">([^<]*)</span>") do
                          Expect.isFalse (m.Groups[1].Value.Contains '\n') (sprintf "%s: %A" g.Name src)

                          Expect.isTrue (m.Groups[1].Value <> "") "no empty span"
          } ]

// The cross-implementation differential lock: SHA-256 over the UTF-16LE code
// units of every output for the generated corpus (both grammars, each output
// followed by U+0001). The TS test pins the SAME digest over the SAME corpus, so
// any divergence between the two ports on ANY of those 6000 inputs fails both.
[<Literal>]
let corpusDigest =
    "6373b20a96f9a089d2e8c3bac8476ca013412668fd05a1bae7530849c3f713aa"

[<Tests>]
let codeHighlightDifferentialTests =
    testList
        "CodeHighlight differential (Phase 1854)"
        [ test "pins the corpus digest the TS port pins" {
              use sha = System.Security.Cryptography.SHA256.Create()

              let bytes =
                  [| for g in [ CodeHighlight.fstar; CodeHighlight.fsharp ] do
                         for src in generated 3000 do
                             for c in CodeHighlight.highlightWith g src + "\u0001" do
                                 yield byte (int c &&& 0xFF)
                                 yield byte (int c >>> 8) |]

              let digest = Convert.ToHexString(sha.ComputeHash bytes).ToLowerInvariant()
              Expect.equal digest corpusDigest "F# and TS agree on every generated input"
          } ]

// ─── The theme rules (§4): existing tokens only, over the contrast floor ─────

let private css =
    lazy (File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fuaran-reference.css")))

let private luminance (hex: string) =
    let channel (i: int) =
        let c = float (Convert.ToInt32(hex.Substring(i, 2), 16)) / 255.0

        if c <= 0.03928 then
            c / 12.92
        else
            Math.Pow((c + 0.055) / 1.055, 2.4)

    0.2126 * channel 1 + 0.7152 * channel 3 + 0.0722 * channel 5

let private contrast a b =
    let la, lb = luminance a, luminance b
    (max la lb + 0.05) / (min la lb + 0.05)

[<Tests>]
let codeHighlightThemeTests =
    testList
        "CodeHighlight theme rules (Phase 1854)"
        [ test "every tok-* class has a rule coloured by an existing theme token, clearing 4.5:1 on the code surface" {
              let sheet = css.Force()

              let surface =
                  Regex.Match(sheet, @"\.fuaran-codeblock \{[^}]*background: var\(--fuaran-code-bg, (#[0-9a-f]{6})\)")

              Expect.isTrue surface.Success "the code surface's background fallback is readable"
              let bg = surface.Groups[1].Value

              for cls in [ "tok-kw"; "tok-com"; "tok-str"; "tok-num"; "tok-op"; "tok-ty" ] do
                  let rule =
                      Regex.Match(
                          sheet,
                          @"\.fuaran-codeblock-code \."
                          + cls
                          + @" \{[^}]*color: var\((--fuaran-[a-z-]+), (#[0-9a-f]{6})\);"
                      )

                  Expect.isTrue rule.Success (sprintf "%s has a token-coloured rule" cls)
                  let token, fallback = rule.Groups[1].Value, rule.Groups[2].Value

                  let declared =
                      Regex.Match(sheet, @":root \{[^}]*?\n  " + Regex.Escape token + @": (#[0-9a-f]{6});")

                  Expect.isTrue declared.Success (sprintf "%s is an existing :root theme token" token)

                  Expect.equal
                      fallback
                      declared.Groups[1].Value
                      (sprintf "%s's fallback is the token's own value - no new colour literal" cls)

                  let ratio = contrast fallback bg
                  Expect.isGreaterThanOrEqual ratio 4.5 (sprintf "%s: %s on %s is %.2f:1" cls fallback bg ratio)
          } ]
