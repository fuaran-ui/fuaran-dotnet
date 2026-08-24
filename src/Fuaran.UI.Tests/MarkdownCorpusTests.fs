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
//
//  Phase 1032: a fixture may carry a `policy` naming the destination policy the
//  render is performed under (WIRE_FORMAT §14.1). The name is mapped to a policy
//  this host CONSTRUCTS — the corpus never carries one as data, because a policy
//  that can arrive as data is one a hostile emission can widen. An UNKNOWN name
//  fails rather than falling back: a silent fallback to the permissive policy
//  would turn a fixture the host cannot yet evaluate into one it appears to pass.
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

        /// The named policies of WIRE_FORMAT §14.1, CONSTRUCTED here.
        let policyByName (name: string) : Sanitize.EgressPolicy =
            match name with
            | "permissive" -> Sanitize.permissiveEgress
            | "denyNonLocal" -> Sanitize.denyNonLocalEgress
            | "declaredExample" ->
                Sanitize.denyNonLocalEgress
                |> Sanitize.allowOrigin (Sanitize.ExactHost "cdn.example") [ Sanitize.EgressClass.Media ]
                |> Sanitize.allowOrigin (Sanitize.HostSuffix "docs.example") [ Sanitize.EgressClass.Hyperlink ]
            | other -> failwithf "markdown corpus names a policy this host does not construct: '%s'" other

        let policyName (el: JsonElement) =
            match el.TryGetProperty "policy" with
            | true, v -> v.GetString()
            | _ -> "permissive"

        let cases =
            [ for el in doc.RootElement.GetProperty("fixtures").EnumerateArray() ->
                  el.GetProperty("id").GetString(),
                  policyName el,
                  el.GetProperty("source").GetString(),
                  el.GetProperty("html").GetString() ]

        testList
            "Markdown render corpus (cross-host gate)"
            [ test "corpus is non-empty" {
                  Expect.isGreaterThan (List.length cases) 0 "corpus.json must contain fixtures"
              }

              test "the corpus exercises the destination policy" {
                  // A guard on the CORPUS rather than the renderer: without a
                  // policied fixture every assertion below runs on the permissive
                  // path, and the gate would be green on a host that never
                  // implemented §14.1 at all.
                  let policied =
                      cases |> List.filter (fun (_, p, _, _) -> p <> "permissive") |> List.length

                  Expect.isGreaterThan policied 0 "corpus.json must carry policied fixtures (WIRE_FORMAT §14.1)"
              }

              for (id, policy, source, html) in cases do
                  test (sprintf "%s — F# render is byte-identical to the corpus" id) {
                      Expect.equal
                          (Markdown.toHtmlWithEgress (policyByName policy) source)
                          html
                          "renderer must reproduce the canonical corpus HTML"
                  }

              for (id, policy, source, html) in cases do
                  if policy = "permissive" then
                      test (sprintf "%s — the pure toHtml IS the permissive case" id) {
                          Expect.equal
                              (Markdown.toHtml source)
                              html
                              "toHtml must equal toHtmlWithEgress permissiveEgress, byte-for-byte"
                      } ]
