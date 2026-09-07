module Fuaran.UI.Renderer.Web.Tests.SnippetHygiene

// ============================================================================
//  Phase 1532 — what the mount snippet emits, and what it reads.
//
//  Three defects, one theme: a string was handled as though it were a
//  different kind of string.
//
//   * `NotifyEndpoint` and `ElementId` were HTML-escaped INSIDE a JavaScript
//     string literal. `&` became `&amp;`, so a fetch went to a URL nobody
//     routes; a backslash and a quote were left alone, so either ended the
//     literal and took the whole inline script — the mount included — with it.
//   * The notify `fetch` had no failure path at all: a rejected promise landed
//     as an unhandled rejection somewhere else, and a 4xx/5xx did not reject,
//     so a refused notify looked exactly like an accepted one.
//   * One `window.fuaranHandle` per page, so the second of two mounts replaced
//     the first's handle and left that tree unreachable from host script.
//
//  THE BYTE PIN below is the load-bearing case. Every other assertion here
//  looks for a substring, and a substring assertion cannot see a change to the
//  bytes around it — which is exactly where an escaping defect lives.
// ============================================================================

open System
open Expecto
open Fuaran.UI.Renderer.Web

/// Line endings are normalised on both sides of the byte pin. `.gitattributes`
/// pins LF in the repo, but Fantomas writes CRLF, so a formatted checkout can
/// disagree with itself between two files that were both correct. Every other
/// byte is pinned exactly, which is where the escaping lives.
let private lf (s: string) = s.Replace("\r\n", "\n")

[<Tests>]
let tests =
    testList
        "Phase 1532 — mount snippet hygiene"
        [

          // ── The byte pin ──────────────────────────────────────────────────

          test "the emitted script is pinned, for an endpoint carrying & and a backslash" {
              let options =
                  { Snippet.defaults with
                      Snippet.ElementId = "grid"
                      Snippet.NotifyEndpoint = Some "/api/notify?a=1&b=2\\x"
                      Snippet.AntiforgeryHeader = Some("RequestVerificationToken", "tok&en") }

              let expected =
                  """<div id="grid"></div>
<script type="application/json" id="grid-tree">{"id":"root"}</script>
<script>
(function () {
  var id = "grid";
  var report = function (message) { console.error('[Fuaran] ' + message); };
  var el = document.getElementById(id);
  var json = document.getElementById(id + '-tree').textContent;
  window.fuaranHandles = window.fuaranHandles || {};
  window.fuaranHandles[id] = FuaranRenderer.mount(el, json, {
    onNotify: function (channel, payload) {
      fetch("/api/notify?a=1\u0026b=2\\x", {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', "RequestVerificationToken": "tok\u0026en" },
        body: JSON.stringify({ channel: channel, payload: payload })
      }).then(function (response) {
        if (!response.ok) {
          report('Notify(' + channel + ') was refused by the host: ' + response.status + ' ' + response.statusText);
        }
      }).catch(function (error) {
        report('Notify(' + channel + ') could not be delivered: ' + error);
      });
    },
    onError: report
  });
})();
</script>"""

              let actual = Snippet.mount options "fv1:whatever" """{"id":"root"}"""

              Expect.equal
                  (lf actual)
                  (lf expected)
                  "the emitted script, byte for byte — the `&` survives as a JSON-style escape that decodes back to `&`, and the backslash is doubled rather than escaping the character after it"
          }

          // ── The escaper, one property at a time ───────────────────────────

          test "an ampersand in the endpoint is not HTML-escaped" {
              let html =
                  Snippet.mount
                      { Snippet.defaults with
                          Snippet.NotifyEndpoint = Some "/notify?a=1&b=2" }
                      "fv1:whatever"
                      """{"id":"root"}"""

              Expect.isFalse
                  (html.Contains "&amp;")
                  "`&amp;` inside a JS string literal is five characters, so the fetch went to a URL the host does not route — and the page said nothing, because the request was well-formed"

              Expect.stringContains
                  html
                  "a=1\\u0026b=2"
                  "it is escaped the JavaScript way instead, which decodes back to `&`"
          }

          test "a backslash and a quote in the element id do not break the literal" {
              let html =
                  Snippet.mount
                      { Snippet.defaults with
                          Snippet.ElementId = "od\\d\"one" }
                      "fv1:whatever"
                      """{"id":"root"}"""

              Expect.stringContains
                  html
                  "var id = \"od\\\\d\\\"one\";"
                  "both are escaped — either one unescaped ends the string literal, and a SyntaxError takes the whole inline script including the mount"

              Expect.stringContains
                  html
                  "<div id=\"od\\d&quot;one\"></div>"
                  "while the HTML attribute keeps the HTML escaping, which is a different escaping for a different context"
          }

          test "a drift diagnostic message is escaped as a JS literal too" {
              // The message carries host-supplied version strings. It used to
              // be escaped by two `Replace` calls that left `<` alone.
              let html =
                  Snippet.mount
                      { Snippet.defaults with
                          Snippet.Development = true }
                      "fv1:</script><img src=x>"
                      """{"id":"root"}"""

              Expect.stringContains html "console.warn" "the diagnostic fired"

              match
                  html.Split('\n')
                  |> Array.tryFind (fun l -> l.StartsWith("console.warn", StringComparison.Ordinal))
              with
              | None -> failtest "no console.warn line, so there is nothing to check the escaping of"
              | Some line ->
                  Expect.isFalse
                      (line.Contains "</script>")
                      "the message cannot close the script element it sits in — a drift warning that breaks the page is worse than the drift it reports"

                  Expect.stringContains
                      line
                      "\\u003c/script\\u003e"
                      "it is escaped instead, the same way the tree payload beside it is"

              // The HTML COMMENT half carries the message unescaped, and that
              // is right: a comment ends at `-->`, never at `</script>`, and
              // the `--` substitution already guards the one sequence that
              // could end it early.
              Expect.stringContains html "<!--" "the comment half is still emitted"
          }

          // ── The antiforgery header ────────────────────────────────────────

          test "no antiforgery header by default" {
              let html =
                  Snippet.mount
                      { Snippet.defaults with
                          Snippet.NotifyEndpoint = Some "/notify" }
                      "fv1:whatever"
                      """{"id":"root"}"""

              Expect.stringContains html "headers: { 'Content-Type': 'application/json' }" "just the content type"

              Expect.isFalse
                  (html.Contains "RequestVerificationToken")
                  "nothing is guessed — a token this package invented would be one the host's validator has never seen, and would fail every request while looking like protection"
          }

          test "the antiforgery header is emitted when the host names one" {
              let html =
                  Snippet.mount
                      { Snippet.defaults with
                          Snippet.NotifyEndpoint = Some "/notify"
                          Snippet.AntiforgeryHeader = Some("X-CSRF", "abc123") }
                      "fv1:whatever"
                      """{"id":"root"}"""

              Expect.stringContains
                  html
                  "'Content-Type': 'application/json', \"X-CSRF\": \"abc123\""
                  "beside the content type, both halves through the JS escaper"
          }

          // ── The failure path ──────────────────────────────────────────────

          test "a notify that fails reaches the same channel as a render error" {
              let html =
                  Snippet.mount
                      { Snippet.defaults with
                          Snippet.NotifyEndpoint = Some "/notify" }
                      "fv1:whatever"
                      """{"id":"root"}"""

              Expect.stringContains
                  html
                  ".catch(function (error)"
                  "a rejected promise is caught rather than left unhandled"

              Expect.stringContains
                  html
                  "if (!response.ok)"
                  "and a response that ARRIVED and was refused is reported too — `fetch` resolves for a 500, so a refusal is otherwise indistinguishable from acceptance"

              Expect.stringContains html "onError: report" "both arms report through the snippet's own onError"
          }

          // ── One handle per mount ──────────────────────────────────────────

          test "two mounts on one page keep separate handles" {
              let mountOne id =
                  Snippet.mount
                      { Snippet.defaults with
                          Snippet.ElementId = id }
                      "fv1:whatever"
                      """{"id":"root"}"""

              let page = mountOne "dashboard" + mountOne "filters"

              Expect.stringContains page "var id = \"dashboard\";" "the first mount keys on its own id"
              Expect.stringContains page "var id = \"filters\";" "and the second on its own"

              Expect.equal
                  (page.Split("window.fuaranHandles[id] =", StringSplitOptions.None).Length - 1)
                  2
                  "each writes its own slot"

              Expect.isFalse
                  (page.Contains "window.fuaranHandle =")
                  "and nothing writes the single page-global handle the second mount used to overwrite, leaving the first tree rendered, interactive and unreachable"
          } ]
