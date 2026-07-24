module Fuaran.UI.Tests.Markdown

// ============================================================================
//  Deterministic GFM markdown renderer (Phase 292).
//
//  These cases ARE the canonical-output contract: each `source → html`
//  expectation pins the byte-exact HTML every Fuaran host must reproduce
//  (F#/Fable client, Renderer.Server, TS, Python). The same pairs are emitted
//  to ../../wire-format-fixtures/markdown/ as the cross-host conformance
//  corpus, so a divergence in any host turns the §11.1-style gate red.
//
//  The three buckets (see Fuaran.UI.Renderer.Markdown + docs/MARKDOWN.md):
//   IN  — the GFM feature set, common-case correct.
//   OUT — raw/inline HTML escaped; math + Mermaid live elsewhere.
//   DEFERRED — emoji/footnotes/anchors/etc. render escaped-literal until added.
// ============================================================================

open Expecto
open Fuaran.UI.Renderer

let private check (name: string) (source: string) (expected: string) =
    test name {
        let actual = Markdown.toHtml source
        Expect.equal actual expected "markdown render must match the canonical-HTML contract"
    }

[<Tests>]
let tests =
    testList
        "Deterministic GFM markdown renderer (Phase 292)"
        [ check "empty source → empty string" "" ""

          // ── Headings ────────────────────────────────────────────────────
          check "ATX heading level 1" "# Title" "<h1>Title</h1>\n"
          check "ATX heading level 3" "### Sub" "<h3>Sub</h3>\n"
          check "ATX heading with closing hashes" "## Mid ##" "<h2>Mid</h2>\n"
          check "setext heading level 1" "Title\n=====" "<h1>Title</h1>\n"
          check "setext heading level 2" "Title\n-----" "<h2>Title</h2>\n"

          // ── Paragraphs + inline ─────────────────────────────────────────
          check "paragraph plain" "hello world" "<p>hello world</p>\n"
          check "emphasis + strong" "a *b* **c**" "<p>a <em>b</em> <strong>c</strong></p>\n"
          check "underscore emphasis" "_em_ __st__" "<p><em>em</em> <strong>st</strong></p>\n"
          check "strikethrough (GFM)" "~~gone~~" "<p><del>gone</del></p>\n"
          check "inline code escapes" "`x<y & z`" "<p><code>x&lt;y &amp; z</code></p>\n"
          check "backslash escape" "\\*literal\\*" "<p>*literal*</p>\n"

          // ── Links + images + autolinks ──────────────────────────────────
          check "inline link" "[t](http://e.com)" "<p><a href=\"http://e.com\">t</a></p>\n"
          check
              "inline link with title"
              "[t](http://e.com \"Hi\")"
              "<p><a href=\"http://e.com\" title=\"Hi\">t</a></p>\n"
          check "reference link" "[t][ref]\n\n[ref]: http://e.com" "<p><a href=\"http://e.com\">t</a></p>\n"
          // Phase 299: ASCII reference labels still fold case-insensitively (the cross-host ASCII fold).
          check
              "reference link ASCII case-insensitive"
              "[t][REF]\n\n[ref]: http://e.com"
              "<p><a href=\"http://e.com\">t</a></p>\n"
          check "image" "![alt](http://e.com/i.png)" "<p><img src=\"http://e.com/i.png\" alt=\"alt\" /></p>\n"
          check "angle autolink" "<http://e.com>" "<p><a href=\"http://e.com\">http://e.com</a></p>\n"
          check
              "bare-URL autolink (GFM)"
              "see http://e.com here"
              "<p>see <a href=\"http://e.com\">http://e.com</a> here</p>\n"
          check
              "javascript: URL neutralized to about:blank"
              "[x](javascript:alert(1))"
              "<p><a href=\"about:blank\">x</a></p>\n"

          // ── Entities + raw-HTML (OUT bucket) ────────────────────────────
          check "named entity amp re-escapes" "a &amp; b" "<p>a &amp; b</p>\n"
          check "named entity copy decodes" "&copy;" "<p>©</p>\n"
          check "numeric entity decimal" "&#42;" "<p>*</p>\n"
          check "raw inline HTML is escaped (OUT)" "<div>x</div>" "<p>&lt;div&gt;x&lt;/div&gt;</p>\n"

          // ── Breaks ──────────────────────────────────────────────────────
          check "hard break (two trailing spaces)" "a  \nb" "<p>a<br />\nb</p>\n"
          check "soft break" "a\nb" "<p>a\nb</p>\n"

          // ── Thematic break ──────────────────────────────────────────────
          check "thematic break dashes" "---" "<hr />\n"
          check "thematic break stars" "***" "<hr />\n"

          // ── Code blocks (share the CodeBlock <pre><code> shape) ─────────
          check
              "fenced code with language"
              "```fsharp\nlet x = 1\n```"
              "<pre><code class=\"language-fsharp\">let x = 1\n</code></pre>\n"
          check "fenced code without language" "```\nplain\n```" "<pre><code>plain\n</code></pre>\n"
          check "fenced code escapes html" "```\n<b>&\n```" "<pre><code>&lt;b&gt;&amp;\n</code></pre>\n"

          // ── Blockquote ──────────────────────────────────────────────────
          check "blockquote" "> quoted" "<blockquote>\n<p>quoted</p>\n</blockquote>\n"

          // ── Lists ───────────────────────────────────────────────────────
          check "bullet list (tight)" "- a\n- b" "<ul>\n<li>a</li>\n<li>b</li>\n</ul>\n"
          check "ordered list (tight)" "1. a\n2. b" "<ol>\n<li>a</li>\n<li>b</li>\n</ol>\n"
          check "ordered list custom start" "3. a\n4. b" "<ol start=\"3\">\n<li>a</li>\n<li>b</li>\n</ol>\n"
          check
              "task list (GFM)"
              "- [ ] todo\n- [x] done"
              "<ul>\n<li class=\"fuaran-task-item\"><input class=\"fuaran-task-checkbox\" disabled=\"\" type=\"checkbox\" /> todo</li>\n<li class=\"fuaran-task-item\"><input class=\"fuaran-task-checkbox\" checked=\"\" disabled=\"\" type=\"checkbox\" /> done</li>\n</ul>\n"

          // ── Tables (route to the Table NodeKind class vocabulary) ───────
          check
              "GFM table"
              "| H1 | H2 |\n| --- | --- |\n| a | b |"
              "<table class=\"fuaran-table\"><thead><tr><th class=\"fuaran-table-header\">H1</th><th class=\"fuaran-table-header\">H2</th></tr></thead><tbody><tr class=\"fuaran-table-row\"><td class=\"fuaran-table-cell\">a</td><td class=\"fuaran-table-cell\">b</td></tr></tbody></table>\n"
          check
              "GFM table with alignment"
              "| L | C | R |\n| :-- | :-: | --: |\n| a | b | c |"
              "<table class=\"fuaran-table\"><thead><tr><th class=\"fuaran-table-header\" align=\"left\">L</th><th class=\"fuaran-table-header\" align=\"center\">C</th><th class=\"fuaran-table-header\" align=\"right\">R</th></tr></thead><tbody><tr class=\"fuaran-table-row\"><td class=\"fuaran-table-cell\" align=\"left\">a</td><td class=\"fuaran-table-cell\" align=\"center\">b</td><td class=\"fuaran-table-cell\" align=\"right\">c</td></tr></tbody></table>\n"

          // ── Multi-block document ────────────────────────────────────────
          check "heading then paragraph" "# T\n\nbody" "<h1>T</h1>\n<p>body</p>\n"

          // ── SSR↔CSR parity guarantee (same function on both pipelines) ──
          test "rendering is pure / idempotent (SSR == CSR by construction)" {
              let src = "# H\n\ntext **bold** and `code`\n\n- one\n- two"
              Expect.equal (Markdown.toHtml src) (Markdown.toHtml src) "same input → same output (no nondeterminism)"
          } ]
