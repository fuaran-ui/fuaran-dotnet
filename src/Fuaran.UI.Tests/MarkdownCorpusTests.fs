module Fuaran.UI.Tests.MarkdownCorpus

#nowarn "3261" // DirectoryInfo.Parent + JsonElement.GetString() are legitimately nullable here.

// ============================================================================
//  Cross-host markdown-render conformance gate — F# leg (Phase 292).
//
//  Reads the workspace-root corpus ../../wire-format-fixtures/markdown/corpus.json
//  and asserts the F# reference renderer (Fuaran.UI.Renderer.Markdown.toHtml)
//  reproduces every `source → html` pair byte-for-byte. This pins the corpus
//  to the F# renderer (Leg A: `F# == corpus`); the TS (@fuaran-ui/renderer) and
//  Python (fuaran_py.renderer) hosts run the same corpus (Leg B: `TS == corpus`,
//  `Py == corpus`) — together proving `F# == TS == Py`, the §11.1-style
//  byte-parity gate applied to markdown rendering.
//
//  Skips gracefully when the corpus is absent (a standalone fuaran-dotnet/ checkout
//  without the workspace sibling) — the inline MarkdownTests still pin the
//  contract in that case.
// ============================================================================

open System
open System.IO
open System.Text.Json
open Expecto
open Fuaran.UI.Renderer

let private tryFindCorpus () : string option =
    let rec climb (dir: DirectoryInfo) =
        if isNull (box dir) then
            None
        else
            let candidate =
                Path.Combine(dir.FullName, "wire-format-fixtures", "markdown", "corpus.json")

            if File.Exists candidate then
                Some candidate
            else
                climb dir.Parent

    climb (DirectoryInfo(AppContext.BaseDirectory))

[<Tests>]
let tests =
    match tryFindCorpus () with
    | None ->
        testList
            "Markdown render corpus (cross-host gate)"
            [ test "corpus absent — skipped (standalone checkout)" {
                  Expect.isTrue
                      true
                      "wire-format-fixtures/markdown/corpus.json not found; inline MarkdownTests still apply"
              } ]
    | Some path ->
        let doc = JsonDocument.Parse(File.ReadAllText path)

        let cases =
            [ for el in doc.RootElement.GetProperty("fixtures").EnumerateArray() ->
                  el.GetProperty("id").GetString(),
                  el.GetProperty("source").GetString(),
                  el.GetProperty("html").GetString() ]

        testList
            "Markdown render corpus (cross-host gate)"
            [ test "corpus is non-empty" {
                  Expect.isGreaterThan (List.length cases) 0 "corpus.json must contain fixtures"
              }

              for (id, source, html) in cases do
                  test (sprintf "%s — F# render is byte-identical to the corpus" id) {
                      Expect.equal (Markdown.toHtml source) html "renderer must reproduce the canonical corpus HTML"
                  } ]
