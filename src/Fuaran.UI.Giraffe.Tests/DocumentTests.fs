module Fuaran.UI.Giraffe.Tests.DocumentTests

open Expecto
open Fuaran.UI.Giraffe

let private fullShell =
    { DocumentShell.create "Pricing — Acme" with
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
              Expect.stringContains html "<html lang=\"en\">" "html lang attribute from the shell default"
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
          } ]
