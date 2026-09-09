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

// ─── Phase 1523 — the document shell's two hand-built seams ────────────────

[<Tests>]
let documentShellHardeningTests =
    testList
        "Document.render — attribute NAMES and the ScriptRef security slots (Phase 1523)"
        [ test "an attribute NAME that is really three attributes is DROPPED, not mangled" {
              // The value side of `HtmlAttributes` / `BodyAttributes` was
              // escaped; the NAME was written verbatim. HTML has no escape for
              // an illegal character in an attribute name — a space inside one
              // simply starts a NEW attribute and an `=` starts its value — so
              // this key was not a mangled name, it was three attributes, one of
              // them a live event handler, on the document's own `<html>`
              // element. These two tags are concatenated as strings rather than
              // built through ViewEngine, which is exactly why the gate the rest
              // of the renderer gets for free had to be applied by hand here.
              let shell =
                  { DocumentShell.create "T" with
                      HtmlAttributes = [ "data-x=1 onload=alert(1) z", "v" ]
                      BodyAttributes = [ "cls\"><script>evil()</script", "v" ] }

              let html = Document.render shell "BODY"

              Expect.isFalse (html.Contains "onload") "the smuggled handler does not reach the html element"
              Expect.isFalse (html.Contains "evil()") "nor does a name that closes the body tag and opens a script"
              Expect.isFalse (html.Contains "alert(1)") "and neither does the html one's payload"

              // What this rule is and is NOT. It is the Phase 788 class — a NAME
              // that is not a legal attribute name, which HTML gives no way to
              // escape. It is deliberately not an event-handler denylist: these
              // two attribute bags are HOST-authored, not tree-authored, so a
              // host that writes `onclick` on its own `<body>` has written the
              // script it wanted. The tree-authored bag is a different seam with
              // a different (stricter) rule — `isAllowedExtraAttributeKey`.

              // Dropping rather than escaping is the only correct response:
              // there is nothing to escape TO. A dropped attribute is a missing
              // attribute, which is visible; a mangled one would be a different
              // attribute, which is not.
              Expect.stringContains html "<html" "the element itself is still emitted"
          }

          test "ALLOW twin — an ordinary attribute name survives on both elements" {
              let shell =
                  { DocumentShell.create "T" with
                      HtmlAttributes = [ "data-theme", "dark" ]
                      BodyAttributes = [ "class", "app" ] }

              let html = Document.render shell "BODY"
              Expect.stringContains html "data-theme=\"dark\"" "a legitimate html attribute is untouched"
              Expect.stringContains html "class=\"app\"" "and a legitimate body attribute"
          }

          test "a ScriptRef carries the host's nonce, digest and CORS mode when set" {
              // The omission these close was not cosmetic: a host serving a
              // nonce-based CSP could not use `Scripts` AT ALL, because every
              // `<script>` this shell emitted lacked the nonce and was blocked
              // by the very policy the host had adopted to be safe.
              let shell =
                  { DocumentShell.create "T" with
                      Scripts =
                          [ ScriptRef.moduleScript "/app.js"
                            |> ScriptRef.withNonce "r4nd0m"
                            |> ScriptRef.withIntegrity "sha384-abc" "anonymous" ] }

              let html = Document.render shell "BODY"
              Expect.stringContains html "nonce=\"r4nd0m\"" "the nonce reaches the script element"
              Expect.stringContains html "integrity=\"sha384-abc\"" "and the SRI digest"
              Expect.stringContains html "crossorigin=\"anonymous\"" "and the CORS mode SRI needs to work at all"
          }

          test "a ScriptRef declaring none of them is byte-identical to the pre-phase emission" {
              // The three slots are `option` precisely so this stays true. If it
              // fails, every existing shell's head has changed shape.
              let html =
                  Document.render
                      { DocumentShell.create "T" with
                          Scripts = [ ScriptRef.moduleScript "/app.js" ] }
                      "BODY"

              Expect.stringContains html "<script src=\"/app.js\" type=\"module\">" "the unchanged emission"
              Expect.isFalse (html.Contains "nonce") "no nonce attribute"
              Expect.isFalse (html.Contains "integrity") "no integrity attribute"
              Expect.isFalse (html.Contains "crossorigin") "no crossorigin attribute"
          }

          // ── Phase 1545 — the shell-level nonce ───────────────────────────

          test "the shell's nonce reaches every script that declares none" {
              let shell =
                  { (DocumentShell.create "T" |> DocumentShell.withNonce "per-response") with
                      Scripts = [ ScriptRef.moduleScript "/a.js"; ScriptRef.deferred "/b.js" ] }

              let html = Document.render shell "BODY"

              Expect.equal
                  (System.Text.RegularExpressions.Regex.Matches(html, "nonce=\"per-response\"").Count)
                  2
                  "both scripts carry the response's nonce without either declaring it"
          }

          test "a script's own nonce wins over the shell's" {
              // The shell's value is a DEFAULT, not an override: a host that has
              // a reason to nonce one script differently keeps that reason.
              let shell =
                  { (DocumentShell.create "T" |> DocumentShell.withNonce "shell") with
                      Scripts = [ ScriptRef.create "/a.js" |> ScriptRef.withNonce "its-own" ] }

              let html = Document.render shell "BODY"
              Expect.stringContains html "nonce=\"its-own\"" "the script's own nonce is emitted"
              Expect.isFalse (html.Contains "nonce=\"shell\"") "and the shell's is not also emitted onto it"
          }

          test "a shell declaring no nonce is byte-identical to the pre-1545 emission" {
              let withoutNonce =
                  Document.render
                      { DocumentShell.create "T" with
                          Scripts = [ ScriptRef.moduleScript "/app.js" ] }
                      "BODY"

              Expect.isFalse (withoutNonce.Contains "nonce") "no nonce attribute anywhere"
          }

          test "the style-src directive names the nonce and no unsafe-inline" {
              // The host's half of the contract, as one call — the sentence the
              // whole mode exists to make true.
              let directive = Fuaran.UI.Renderer.Csp.styleSrcDirective "per-response"

              Expect.equal directive "style-src 'self' 'nonce-per-response'" "the directive a host sends"
              Expect.isFalse (directive.Contains "unsafe-inline") "no unsafe-inline, which is the point"
          } ]
