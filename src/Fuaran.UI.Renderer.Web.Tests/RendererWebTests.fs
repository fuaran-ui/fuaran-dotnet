module Fuaran.UI.Renderer.Web.Tests.RendererWebTests

open System
open System.Text
open Expecto
open Fuaran.UI.Renderer.Web

// ============================================================================
//  Phase 577 — the embedded browser renderer for .NET hosts.
//
//  Two of these tests are load-bearing and the rest pin shapes.
//
//   * "the embedded assets are present and describe themselves" is the one that
//     fails when the package is packaged wrong. Every other assertion here
//     passes on an assembly carrying no assets at all, because they exercise
//     pure functions.
//
//   * "the sidecar the sync script writes round-trips" is what keeps TWO
//     writers of one format honest. `scripts/sync-renderer-web.ps1` generates
//     the fingerprint; `Fingerprint.parse` reads it. Neither can see the other,
//     so the committed artefact is the only place they meet.
// ============================================================================

/// The vocabulary fingerprint this build's renderer emits — the authoring half
/// of the drift comparison. Read from the shipped constant rather than
/// duplicated, so a renderer change moves this test's input with it.
let private authoringVocabulary = Fuaran.UI.Renderer.Theme.vocabularyFingerprint

let private sampleFingerprint =
    { Fingerprint.RendererPackage = "@fuaran-ui/renderer"
      Fingerprint.RendererVersion = "0.17.0"
      Fingerprint.BundleVersion = "0.1.0"
      Fingerprint.WireProfile = Fingerprint.AuthoringWireProfile
      Fingerprint.VocabularyFingerprint = authoringVocabulary
      Fingerprint.BundleSha256 = "0F875A5B43D328AC29531C1CC23EB8ECA120E588464B0C947E87790C5E4836BB" }

[<Tests>]
let tests =
    testList
        "Fuaran.UI.Renderer.Web"
        [ // ── The assets ────────────────────────────────────────────────

          test "the embedded assets are present and describe themselves" {
              // The packaging assertion. If the .fsproj stops embedding one of
              // the three, this is where it surfaces — at build time in this
              // repo, rather than as a 404 in a consumer's browser.
              for asset in Assets.all do
                  let bytes = Assets.read asset
                  Expect.isGreaterThan bytes.Length 0 (sprintf "%s is embedded and non-empty" asset.Path)

              let script = Assets.read Assets.rendererScript |> Encoding.UTF8.GetString

              Expect.stringContains
                  script
                  "FuaranRenderer"
                  "the bundle defines the global the mount snippet calls — an embedded file that is not the renderer would pass a size check"

              let css = Assets.read Assets.referenceStylesheet |> Encoding.UTF8.GetString

              Expect.stringContains
                  css
                  Fuaran.UI.Renderer.Theme.vocabularyFingerprintMarker
                  "the embedded stylesheet is the stamped canonical sheet, not some other CSS"
          }

          test "the embedded fingerprint parses and agrees with this build" {
              match Assets.fingerprint () with
              | Error message -> failtestf "the sidecar did not parse: %s" message
              | Ok fp ->
                  Expect.equal fp.RendererPackage "@fuaran-ui/renderer" "the package the bundle came from"

                  // The runtime half of the drift guard, run against the real
                  // committed artefact. The build-time half is
                  // `Build.fsproj -- RendererWebCheck`; this is the same
                  // question asked of the assembly rather than of the repo.
                  Expect.isEmpty
                      (Fingerprint.check authoringVocabulary fp)
                      "the embedded assets agree with this build's authoring surface"
          }

          test "the fingerprint records the bytes it sits beside" {
              // A hand-replaced bundle is the one drift no version comparison
              // catches, so the digest is checked rather than merely carried.
              match Assets.fingerprint () with
              | Error message -> failtestf "the sidecar did not parse: %s" message
              | Ok fp ->
                  let actual =
                      Convert.ToHexString(Security.Cryptography.SHA256.HashData(Assets.read Assets.rendererScript))

                  Expect.equal fp.BundleSha256 actual "the recorded digest is of the embedded bundle"
          }

          test "the fingerprint is served uncached; the assets are immutable" {
              Expect.isTrue Assets.rendererScript.Immutable "the bundle changes only with the package version"
              Expect.isTrue Assets.referenceStylesheet.Immutable "so does the stylesheet"

              Expect.isFalse
                  Assets.fingerprintDocument.Immutable
                  "the fingerprint is the drift ORACLE — a proxy answering it from last year answers about last year"
          }

          // ── The fingerprint contract ──────────────────────────────────

          test "the sidecar the sync script writes round-trips" {
              // Not a round trip through `toJson` alone, which would only prove
              // this module agrees with itself. The input is the COMMITTED
              // artefact the PowerShell generator wrote, and the assertion is
              // that re-emitting it from the parsed record reproduces it byte
              // for byte — which is what pins the two writers together.
              let onDisk = Assets.read Assets.fingerprintDocument |> Encoding.UTF8.GetString

              match Fingerprint.parse onDisk with
              | Error message -> failtestf "the generator's output did not parse: %s" message
              | Ok fp ->
                  Expect.equal
                      (Fingerprint.toJson fp)
                      (onDisk.Replace("\r\n", "\n"))
                      "the F# writer reproduces the PowerShell writer's bytes — two writers of one format, held together by the committed artefact"
          }

          test "a missing field is refused, naming the field" {
              let truncated =
                  """{ "rendererPackage": "@fuaran-ui/renderer", "rendererVersion": "0.17.0" }"""

              match Fingerprint.parse truncated with
              | Ok _ -> failtest "Expected a refusal — a half-parsed fingerprint is worse than none"
              | Error message -> Expect.stringContains message "bundleVersion" "names the first field it could not find"
          }

          test "a wire-profile mismatch is reported, and names the repair" {
              let drifted =
                  { sampleFingerprint with
                      Fingerprint.WireProfile = "2" }

              match Fingerprint.check authoringVocabulary drifted with
              | [ Fingerprint.WireProfileMismatch(embedded, authoring) ] ->
                  Expect.equal embedded "2" "what the bundle decodes"
                  Expect.equal authoring Fingerprint.AuthoringWireProfile "what this package emits"

                  Expect.stringContains
                      (Fingerprint.describe (Fingerprint.WireProfileMismatch(embedded, authoring)))
                      "sync-renderer-web"
                      "the message names the command that repairs it"
              | other -> failtestf "Expected one WireProfileMismatch, got %A" other
          }

          test "a vocabulary mismatch is reported separately from a profile one" {
              // Both at once, because two disagreements are two facts and
              // collapsing them to a boolean is how a consumer repairs the
              // wrong one.
              let drifted =
                  { sampleFingerprint with
                      Fingerprint.WireProfile = "2"
                      Fingerprint.VocabularyFingerprint = "fv1:0000000000000000" }

              let mismatches = Fingerprint.check authoringVocabulary drifted
              Expect.equal (List.length mismatches) 2 "two independent axes, two findings"
          }

          // ── The snippet ───────────────────────────────────────────────

          test "the asset tags point at the mounted prefix" {
              let tags = Snippet.assetTags "/assets/fuaran"
              Expect.stringContains tags "/assets/fuaran/fuaran-reference.css" "the stylesheet"
              Expect.stringContains tags "/assets/fuaran/fuaran-renderer.js" "the bundle"
          }

          test "a trailing slash on the prefix does not double up" {
              Expect.stringContains
                  (Snippet.scriptTag "/_fuaran/")
                  "\"/_fuaran/fuaran-renderer.js\""
                  "one slash, not two — a host that types the prefix the other way gets working URLs"
          }

          test "the tree JSON cannot close the script element" {
              // The injection that matters here. A `</script` anywhere in the
              // payload — inside a string literal is enough — ends the element
              // for the HTML tokenizer, and the rest of the tree lands in the
              // document as markup.
              let hostile = """{"id":"root","text":"</script><img src=x onerror=alert(1)>"}"""

              let html = Snippet.mount Snippet.defaults authoringVocabulary hostile

              Expect.isFalse (html.Contains "</script><img") "the payload did not close the element"
              Expect.stringContains html "\\u003c/script\\u003e" "it was escaped instead"
          }

          test "the escaped JSON is still the same JSON" {
              // The escape must be value-preserving: `\u003c` and `<` parse to
              // the same character, so the browser decodes exactly the tree the
              // host encoded. An escape that changed the value would be a
              // second wire format hiding in a helper.
              let json = """{"id":"a<b>c&d"}"""
              let html = Snippet.mount Snippet.defaults authoringVocabulary json

              let payload =
                  let openTag = html.IndexOf("-tree\">", StringComparison.Ordinal) + 7
                  let closeTag = html.IndexOf("</script>", openTag, StringComparison.Ordinal)
                  html.Substring(openTag, closeTag - openTag)

              Expect.notEqual payload json "it was escaped"

              Expect.equal
                  (payload.Replace("\\u003c", "<").Replace("\\u003e", ">").Replace("\\u0026", "&"))
                  json
                  "and unescaping recovers the original bytes"
          }

          test "the mount wires onNotify and leaves dispatch alone" {
              let html =
                  Snippet.mount
                      { Snippet.defaults with
                          Snippet.NotifyEndpoint = Some "/api/fuaran/notify" }
                      authoringVocabulary
                      """{"id":"root"}"""

              Expect.stringContains html "onNotify" "the wire-representable signal is wired"
              Expect.stringContains html "/api/fuaran/notify" "to the endpoint the host named"

              // `dispatch` receives the "<closure>" sentinel, never a message.
              // Wiring it would invite a host to treat a diagnostic as data.
              Expect.isFalse (html.Contains "dispatch:") "the closure callback is not wired as a message channel"
          }

          test "no notify endpoint means no fetch at all" {
              let html = Snippet.mount Snippet.defaults authoringVocabulary """{"id":"root"}"""
              Expect.isFalse (html.Contains "fetch(") "a read-only page posts nothing"
              Expect.stringContains html "id=\"fuaran-root\"" "but still emits its mount element"
          }

          test "a nonce reaches every inline script the snippet emits" {
              let html =
                  Snippet.mount
                      { Snippet.defaults with
                          Snippet.Nonce = Some "n0nc3"
                          Snippet.Development = true }
                      "fv1:deliberately-wrong"
                      """{"id":"root"}"""

              // Two inline scripts under a drifted fingerprint: the diagnostic
              // and the mount. A nonce on one and not the other is a CSP
              // failure that only appears once something has gone wrong, which
              // is the worst time to discover it.
              let occurrences = html.Split("nonce=\"n0nc3\"", StringSplitOptions.None).Length - 1

              Expect.equal occurrences 2 "both inline scripts carry the nonce"
          }

          test "the drift diagnostic is development-only" {
              let drifted = "fv1:deliberately-wrong"

              let dev =
                  Snippet.mount
                      { Snippet.defaults with
                          Snippet.Development = true }
                      drifted
                      """{"id":"root"}"""

              let prod = Snippet.mount Snippet.defaults drifted """{"id":"root"}"""

              Expect.stringContains dev "console.warn" "a developer is told"
              Expect.stringContains dev "class vocabulary" "and told what disagrees"
              Expect.isFalse (prod.Contains "console.warn") "a visitor is not shown internal version state"
          }

          test "development mode is silent when nothing has drifted" {
              // The go-red guard for the test above: a diagnostic that always
              // fires says nothing.
              let html =
                  Snippet.mount
                      { Snippet.defaults with
                          Snippet.Development = true }
                      authoringVocabulary
                      """{"id":"root"}"""

              Expect.isFalse (html.Contains "console.warn") "nothing disagrees, so nothing is said"
          } ]
