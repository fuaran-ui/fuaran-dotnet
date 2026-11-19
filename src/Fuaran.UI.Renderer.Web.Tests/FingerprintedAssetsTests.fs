module Fuaran.UI.Renderer.Web.Tests.FingerprintedAssets

// ============================================================================
//  Phase 1929 — `immutable` is earned by a content-hashed URL.
//
//  The bundle and the stylesheet were served `immutable` for a year at fixed
//  URLs, on the premise that they change only when the package version does —
//  the one case in which a fixed URL does not change. After an upgrade a
//  returning browser ran the old renderer for up to a year and never sent the
//  conditional request the ETag would have answered.
//
//  The routes are exercised through the REAL routing pipeline (`UseRouting` +
//  `MapFuaranRenderer` + `UseEndpoints`), driven with a `DefaultHttpContext`
//  rather than a socket, so the route templates — including the complex
//  `fuaran-renderer.{hash}.js` segment — are what is under test, not a handler
//  called by hand.
// ============================================================================

open System
open System.IO
open System.Text
open Expecto
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Primitives
open Fuaran.UI.Renderer.Web

/// A routing pipeline with the renderer mounted at `/_fuaran`. The slim
/// builder on purpose: it omits the `regex` route constraint, so a template
/// that needed one would fail here rather than in a consumer's slim host.
let private pipeline =
    lazy
        (let app = WebApplication.CreateSlimBuilder().Build()
         app.UseRouting() |> ignore
         app.MapFuaranRenderer() |> ignore
         app.UseEndpoints(fun _ -> ()) |> ignore
         (app :> IApplicationBuilder).Build(), app.Services)

/// GET `path`, optionally conditional, and return the finished context.
let private get (ifNoneMatch: string option) (path: string) : HttpContext =
    let requestDelegate, services = pipeline.Force()
    let ctx = DefaultHttpContext()
    ctx.RequestServices <- services
    ctx.Request.Method <- HttpMethods.Get
    ctx.Request.Path <- PathString path

    ifNoneMatch
    |> Option.iter (fun tag -> ctx.Request.Headers.IfNoneMatch <- StringValues tag)

    ctx.Response.Body <- new MemoryStream()
    (requestDelegate.Invoke ctx).GetAwaiter().GetResult()
    ctx

let private cacheControl (ctx: HttpContext) =
    ctx.Response.Headers.CacheControl.ToString()

let private body (ctx: HttpContext) =
    (ctx.Response.Body :?> MemoryStream).ToArray()

/// The `src` / `href` the snippet emitted.
let private urlIn (attribute: string) (tag: string) =
    let start =
        tag.IndexOf(attribute + "=\"", StringComparison.Ordinal) + attribute.Length + 2

    tag.Substring(start, tag.IndexOf('"', start) - start)

let private fingerprinted =
    [ Assets.rendererScript
      Assets.referenceStylesheet
      Assets.referenceTablesScript
      Assets.imageExpandScript ]

[<Tests>]
let tests =
    testList
        "Phase 1929 — fingerprinted asset URLs"
        [ test "the hash sits before the extension" {
              Expect.equal
                  (Assets.fingerprintedPath Assets.rendererScript "0123abcd")
                  "fuaran-renderer.0123abcd.js"
                  "the URL still ends in .js for every tool keyed off the extension"

              Expect.equal
                  (Assets.fingerprintedPath Assets.referenceStylesheet "0123abcd")
                  "fuaran-reference.0123abcd.css"
                  "and in .css for the stylesheet"
          }

          test "two builds with different bundle bytes produce different snippet URLs" {
              // The snippet's URL is a function of the embedded bytes; this
              // pins that function, then shows two byte strings one byte apart
              // land on two URLs.
              let thisBuild = Assets.read Assets.rendererScript
              let nextBuild = Array.append thisBuild [| byte '\n' |]

              let urlFor bytes =
                  "/_fuaran/"
                  + Assets.fingerprintedPath Assets.rendererScript (Assets.contentHash bytes)

              Expect.equal
                  (urlIn "src" (Snippet.scriptTag "/_fuaran"))
                  (urlFor thisBuild)
                  "the snippet emits the content-hashed URL of the bytes this build embeds"

              Expect.notEqual
                  (urlFor nextBuild)
                  (urlFor thisBuild)
                  "a build whose bundle differs serves the page a different script URL, so no browser runs the old one under the new page"

              let css = Assets.read Assets.referenceStylesheet

              Expect.equal
                  (urlIn "href" (Snippet.styleLink "/_fuaran"))
                  ("/_fuaran/"
                   + Assets.fingerprintedPath Assets.referenceStylesheet (Assets.contentHash css))
                  "the stylesheet link is hashed on the same terms"
          }

          test "the URL hash is the ETag's digest, so one hash names the bytes" {
              for asset in fingerprinted do
                  let bytes, tag = Assets.content asset

                  Expect.equal (Assets.embeddedHash asset) (Assets.contentHash bytes) asset.Path

                  Expect.isTrue
                      (tag.StartsWith("\"" + (Assets.embeddedHash asset).ToUpperInvariant(), StringComparison.Ordinal))
                      (sprintf "%s: the URL hash is the ETag's prefix — one SHA-256, two cuts" asset.Path)
          }

          test "the hashed path carries immutable; the plain path carries no-cache and the ETag" {
              for asset in fingerprinted do
                  let bytes, tag = Assets.content asset
                  let hashed = get None ("/_fuaran/" + Assets.versionedPath asset)
                  let plain = get None ("/_fuaran/" + asset.Path)

                  Expect.equal hashed.Response.StatusCode 200 (sprintf "%s: the hashed path is routed" asset.Path)

                  Expect.equal
                      (cacheControl hashed)
                      "public, max-age=31536000, immutable"
                      (sprintf "%s: a URL that changes with the bytes may be immutable" asset.Path)

                  Expect.equal (List.ofArray (body hashed)) (List.ofArray bytes) "the hashed path serves the bytes"

                  Expect.equal plain.Response.StatusCode 200 (sprintf "%s: the plain path stays served" asset.Path)

                  Expect.equal
                      (cacheControl plain)
                      "no-cache"
                      (sprintf
                          "%s: a URL that does NOT change with the bytes revalidates, or an upgrade is invisible for a year"
                          asset.Path)

                  Expect.equal (plain.Response.Headers.ETag.ToString()) tag "the plain path carries the strong ETag"
          }

          test "a stale browser on the plain path revalidates to a 304" {
              let _, tag = Assets.content Assets.rendererScript
              let revalidated = get (Some tag) "/_fuaran/fuaran-renderer.js"

              Expect.equal revalidated.Response.StatusCode 304 "an unchanged bundle costs a 304, not a body"
              Expect.equal (body revalidated).Length 0 "and sends nothing"

              let stale = get (Some "\"0000\"") "/_fuaran/fuaran-renderer.js"
              Expect.equal stale.Response.StatusCode 200 "a browser holding other bytes gets the current ones"
          }

          test "another build's hash is served revalidating, never immutable" {
              // A page rendered by the previous build, met mid-rollout or from a
              // cached document. A 404 would leave it with no renderer at all;
              // `immutable` would bind the old name to the new bytes for a year.
              let other =
                  if Assets.embeddedHash Assets.rendererScript = "00000000" then
                      "11111111"
                  else
                      "00000000"

              let ctx =
                  get None ("/_fuaran/" + Assets.fingerprintedPath Assets.rendererScript other)

              Expect.equal ctx.Response.StatusCode 200 "the page still gets a renderer"
              Expect.equal (cacheControl ctx) "no-cache" "and it is told to ask again"
          }

          test "a hashed route answers for hashes only" {
              let ctx = get None "/_fuaran/fuaran-renderer.not-a-hash.js"
              Expect.equal ctx.Response.StatusCode 404 "the template does not answer for arbitrary names"
          }

          test "the fingerprint has no hashed path and stays no-cache" {
              Expect.equal
                  (Assets.versionedPath Assets.fingerprintDocument)
                  Assets.fingerprintDocument.Path
                  "the drift oracle is never cached immutably, so it has nothing to fingerprint"

              let ctx = get None "/_fuaran/fingerprint.json"
              Expect.equal (cacheControl ctx) "no-cache" "the oracle revalidates"

              let hashed =
                  get
                      None
                      ("/_fuaran/"
                       + Assets.fingerprintedPath
                           Assets.fingerprintDocument
                           (Assets.embeddedHash Assets.fingerprintDocument))

              Expect.equal hashed.Response.StatusCode 404 "and no hashed route exists for it"
          }

          test "assetUrl names the hashed path for the enhancement scripts" {
              // No helper emits their tags; a host writing its own gets the
              // fingerprinted URL from here.
              Expect.equal
                  (Snippet.assetUrl "/_fuaran/" Assets.referenceTablesScript)
                  ("/_fuaran/" + Assets.versionedPath Assets.referenceTablesScript)
                  "trailing slash trimmed, hashed path appended"

              Expect.notEqual
                  (Assets.versionedPath Assets.imageExpandScript)
                  Assets.imageExpandScript.Path
                  "the image-expand script is fingerprinted too"
          } ]
