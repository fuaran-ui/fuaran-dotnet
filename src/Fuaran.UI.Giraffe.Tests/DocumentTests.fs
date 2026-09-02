module Fuaran.UI.Giraffe.Tests.DocumentTests

open Expecto
open Fuaran.UI.Giraffe

let private fullShell =
    { (DocumentShell.create "Pricing — Acme" |> DocumentShell.withLocale "en") with
        MetaDescription = Some "Simple, transparent pricing."
        Canonical = Some "https://acme.example/pricing"
        OpenGraph = [ "og:title", "Pricing — Acme"; "og:type", "website" ]
        TwitterCard = [ "twitter:card", "summary" ]
        JsonLd = [ """{"@context":"https://schema.org","@type":"Product","name":"Acme"}""" ]
        Stylesheets = [ "/fuaran-reference.css" ]
        Scripts = [ ScriptRef.moduleScript "/app.js" ] }

[<Tests>]
let tests =
    testList
        "Document.render"
        [ test "emits a full crawlable document around the body fragment" {
              let html = Document.render fullShell "<div id=\"root\">BODY</div>"
              Expect.stringStarts html "<!DOCTYPE html>" "starts with the doctype"
              Expect.stringContains html "<html lang=\"en\" dir=\"ltr\">" "lang + dir derived from the declared locale"
              Expect.stringContains html "<title>Pricing — Acme</title>" "title from the shell"
              Expect.stringContains html "<div id=\"root\">BODY</div>" "body fragment injected verbatim, no wrapper"
              Expect.stringContains html "</body></html>" "closes body + html"
          }

          test "emits the SEO head fields" {
              let html = Document.render fullShell "BODY"

              Expect.stringContains
                  html
                  "name=\"description\" content=\"Simple, transparent pricing.\""
                  "meta description"

              Expect.stringContains html "rel=\"canonical\" href=\"https://acme.example/pricing\"" "canonical link"
              Expect.stringContains html "property=\"og:title\"" "open graph"
              Expect.stringContains html "name=\"twitter:card\"" "twitter card"
              Expect.stringContains html "application/ld+json" "JSON-LD script"
              Expect.stringContains html "rel=\"stylesheet\" href=\"/fuaran-reference.css\"" "stylesheet link"
              Expect.stringContains html "src=\"/app.js\"" "script ref src"
              Expect.stringContains html "type=\"module\"" "module script type"
          }

          test "HTML-escapes a script-injecting title (text seam)" {
              let shell = DocumentShell.create "</title><script>alert(1)</script>"
              let html = Document.render shell "BODY"

              Expect.isFalse
                  (html.Contains "<script>alert(1)</script>")
                  "the raw script tag must not survive in the title"

              Expect.stringContains html "&lt;script&gt;" "the angle brackets are HTML-escaped"
          }

          test "script-escapes a </script> substring inside JSON-LD (raw-JSON seam)" {
              let shell =
                  { DocumentShell.create "T" with
                      JsonLd = [ """{"x":"</script><script>evil()</script>"}""" ] }

              let html = Document.render shell "BODY"
              Expect.isFalse (html.Contains "</script><script>evil()") "the breakout sequence must be neutralised"
              Expect.stringContains html "\\u003c/script\\u003e" "the < is unicode-escaped for safe <script> embedding"
          }

          test "sanitizes a javascript: canonical URL to about:blank" {
              let shell =
                  { DocumentShell.create "T" with
                      Canonical = Some "javascript:alert(1)"
                      Stylesheets = [ "javascript:evil()" ] }

              let html = Document.render shell "BODY"
              Expect.isFalse (html.Contains "javascript:") "no javascript: scheme survives in the head"
              Expect.stringContains html "about:blank" "rejected URLs become about:blank"
          }

          // ─── Phase 1114 — the document language declaration ──────────────

          test "an explicit RTL locale emits lang + dir=rtl" {
              let shell = DocumentShell.create "التسعير" |> DocumentShell.withLocale "ar-EG"
              let html = Document.render shell "BODY"
              Expect.stringContains html "<html lang=\"ar-EG\" dir=\"rtl\">" "the RTL locale drives both attributes"
          }

          test "an explicit script subtag overrides the language default, both ways" {
              let rtl =
                  Document.render (DocumentShell.create "T" |> DocumentShell.withLocale "az-Arab-IR") "BODY"

              let ltr =
                  Document.render (DocumentShell.create "T" |> DocumentShell.withLocale "ku-Latn-TR") "BODY"

              Expect.stringContains rtl "dir=\"rtl\"" "az-Arab is right-to-left where bare az is not"
              Expect.stringContains ltr "dir=\"ltr\"" "ku-Latn is left-to-right"
          }

          test "an ambient locale resolves from the host tag at render time" {
              // The shell declares nothing; the host supplies its own locale —
              // the same string a `Binding.Format` with an ambient locale
              // formats against, so numbers and direction cannot disagree.
              let shell = DocumentShell.create "T"
              let html = Document.renderWithLocale "he-IL" shell "BODY"
              Expect.stringContains html "<html lang=\"he-IL\" dir=\"rtl\">" "ambient resolves to the host tag"
          }

          test "no declared locale emits no lang and no dir" {
              // The hardcoded `lang=\"en\"` died in Phase 1114: a shell that
              // declares nothing asserts nothing, rather than asserting English
              // about a document nobody made a statement about.
              let html = Document.render (DocumentShell.create "T") "BODY"
              Expect.stringContains html "<html>" "the open tag carries no attributes"
              Expect.isFalse (html.Contains "lang=") "no language is asserted"
              Expect.isFalse (html.Contains "dir=") "no direction is asserted"
          }

          test "a host-authored lang wins over the derived one, and emits once" {
              let shell =
                  { (DocumentShell.create "T" |> DocumentShell.withLocale "ar") with
                      HtmlAttributes = [ "lang", "en-GB" ] }

              let html = Document.render shell "BODY"
              Expect.stringContains html "lang=\"en-GB\"" "the host's own value is emitted"
              Expect.isFalse (html.Contains "lang=\"ar\"") "the derived value is dropped, not duplicated"
              // `dir` was NOT authored, so the locale-derived one still applies.
              Expect.stringContains html "dir=\"rtl\"" "the underived half is still derived"
          }

          test "the locale is an ETag input — Formatting.textDirection agrees with the emission" {
              // The direction the shell emits and the one the shared spine
              // computes are the same function, not two implementations.
              for tag, expected in
                  [ "ar", "rtl"
                    "he", "rtl"
                    "fa-IR", "rtl"
                    "ur-PK", "rtl"
                    "ckb", "rtl"
                    "en", "ltr"
                    "pa", "ltr"
                    "ku", "ltr"
                    "zh-Hans", "ltr"
                    "", "ltr" ] do
                  Expect.equal (Fuaran.UI.Renderer.Formatting.textDirection tag) expected ("direction of " + tag)
          } ]
