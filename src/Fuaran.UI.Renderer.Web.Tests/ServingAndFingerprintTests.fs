module Fuaran.UI.Renderer.Web.Tests.ServingAndFingerprint

// ============================================================================
//  Phase 1532 — the serving path, and the sidecar reader's escapes.
//
//  Two facts about bytes that cannot change and were treated as though they
//  could: the embedded assets, re-read and re-hashed on every request; and the
//  sidecar, written with six escapes and read with two.
// ============================================================================

open System
open Expecto
open Fuaran.UI.Renderer.Web

[<Tests>]
let tests =
    testList
        "Phase 1532 — serving and the fingerprint reader"
        [ test "an asset is read and hashed once per process" {
              let firstBytes, firstTag = Assets.content Assets.rendererScript
              let secondBytes, secondTag = Assets.content Assets.rendererScript

              Expect.isTrue
                  (Object.ReferenceEquals(firstBytes, secondBytes))
                  "the SAME array comes back — the serving path called this per request, so every hit on the bundle copied half a megabyte out of the manifest stream and SHA-256'd it again to answer a question whose answer is compiled in"

              Expect.equal secondTag firstTag "and the ETag with it"
          }

          test "the cached content is what a fresh read would give" {
              // The cache must not be the only witness to its own correctness.
              let cached, tag = Assets.content Assets.fingerprintDocument
              let fresh = Assets.read Assets.fingerprintDocument

              Expect.equal (List.ofArray cached) (List.ofArray fresh) "same bytes as an uncached read"
              Expect.equal tag (Assets.etag fresh) "and the ETag is the one those bytes hash to"
          }

          test "every asset caches independently" {
              let bundle, bundleTag = Assets.content Assets.rendererScript
              let css, cssTag = Assets.content Assets.referenceStylesheet

              Expect.isFalse (Object.ReferenceEquals(bundle, css)) "two assets, two entries"

              Expect.notEqual
                  cssTag
                  bundleTag
                  "and two ETags — one entry serving both would be a cache keyed on nothing"
          }

          test "the sidecar round-trips a value carrying a quote and a backslash" {
              // Nothing in the sidecar carries either TODAY. That is why the
              // reader's gap was invisible: every field is fed from somewhere
              // else, and the first that ever carries one is the first that
              // finds out.
              let hostile =
                  { Fingerprint.RendererPackage = "@scope/name\"quoted"
                    Fingerprint.RendererVersion = "1.0.0"
                    Fingerprint.BundleVersion = "back\\slash"
                    Fingerprint.WireProfile = "1"
                    Fingerprint.VocabularyFingerprint = "fv1:abc"
                    Fingerprint.BundleSha256 = "DEADBEEF" }

              match Fingerprint.parse (Fingerprint.toJson hostile) with
              | Ok round -> Expect.equal round hostile "what the writer wrote is what the reader reads"
              | Error e -> failtestf "the sidecar this package wrote did not parse: %s" e
          }

          test "the sidecar round-trips a value carrying a control character" {
              // The writer escapes it in the six-character backslash-u form.
              // The reader dropped the backslash and kept the rest, so the
              // value came back as five literal characters — neither refused
              // nor read, and then compared against the authoring surface,
              // which reports drift about a value nobody wrote.
              let withControl =
                  { Fingerprint.RendererPackage = "@fuaran-ui/renderer"
                    Fingerprint.RendererVersion = "1.0.0"
                    Fingerprint.BundleVersion = "0.1.0"
                    Fingerprint.WireProfile = "1"
                    Fingerprint.VocabularyFingerprint = "fv1:" + string (char 1) + "abc"
                    Fingerprint.BundleSha256 = "tab\there" }

              let json = Fingerprint.toJson withControl
              Expect.stringContains json "\\u0001" "the writer escaped it"

              match Fingerprint.parse json with
              | Ok round ->
                  Expect.equal
                      round.VocabularyFingerprint
                      withControl.VocabularyFingerprint
                      "and the reader decoded it back to the one character it encoded"

                  Expect.equal round.BundleSha256 withControl.BundleSha256 "the tab too"
              | Error e -> failtestf "the sidecar this package wrote did not parse: %s" e
          }

          test "a malformed escape is refused rather than half-read" {
              // A hand-edited or truncated sidecar. Reading it as something
              // else and then reporting drift against the result is the worst
              // of the three available outcomes.
              let json =
                  """{
  "rendererPackage": "@fuaran-ui/renderer",
  "rendererVersion": "1.0.0",
  "bundleVersion": "0.1.0",
  "wireProfile": "1",
  "vocabularyFingerprint": "fv1:\uZZZZ",
  "bundleSha256": "DEADBEEF"
}
"""

              match Fingerprint.parse json with
              | Ok fp -> failtestf "a corrupt escape parsed anyway, as %A" fp.VocabularyFingerprint
              | Error message ->
                  Expect.stringContains
                      message
                      "vocabularyFingerprint"
                      "and the refusal names the field, which is the whole diagnosis"
          } ]
