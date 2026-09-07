module Fuaran.UI.Giraffe.Tests.RenderInputsTests

// ============================================================================
//  Phase 1532 — the SSR validator covers every render INPUT.
//
//  The ETag hashed the tree, the theme CSS, the egress policy, the locale and
//  the shell. It did not hash `Sources` or `Customs`, and both change the
//  emitted document: every query-bound value, every rendered `Custom`. A host
//  building `FuaranGiraffeOptions` per request with per-user `Query` results
//  and sharing one `IFuaranRenderCache` — the shape the shipped SSR sample
//  demonstrates and the docs describe — rendered user A's document, stored it
//  under a key that could not tell A from B, and served it to B. With the cache
//  off, B's browser still received a 304 against A's validator, so the leak
//  outlived the mitigation an operator would reach for first.
//
//  These tests are written against the two shapes that distinguish a
//  source-blind key from a source-aware one:
//
//    * two option sets differing ONLY in their sources must not share a
//      validator; and
//    * where the sources cannot be projected into bytes at all and the host has
//      named no variant, there must be NO validator — not a wrong one.
//
//  The second is why `optionsSig` returns an option. A strong ETag is a promise
//  a browser keeps; minting one that cannot separate two documents is worse
//  than minting none.
// ============================================================================

open System.IO
open System.Threading.Tasks
open Expecto
open Microsoft.AspNetCore.Http
open Giraffe
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Giraffe

module ServerRegistry = Fuaran.UI.Renderer.Server.Registry

// ─── Fixtures ────────────────────────────────────────────────────────────────

/// A tree whose rendered text comes from a QUERY source, so what it renders is
/// decided entirely by `Sources` — the shape whose document must not be shared.
let private queryTree: Node<obj> =
    Fuaran.card
        "root"
        { Defaults.card<obj> with
            Heading = Some(TextSource.Bound(Binding.Query("account", (fun (r: obj) -> string r), None)))
            Children = [ Fuaran.markdown "body" "static body" ] }

let private shell = DocumentShell.create "Account"

let private sourcesFor (account: string) : BindingSources =
    { BindingSources.empty with
        QueryResults = Map.ofList [ "account", box account ] }

// ─── HttpContext harness (mirrors HandlerTests) ──────────────────────────────

let private runHandler (handler: HttpHandler) (configure: HttpContext -> unit) : HttpContext =
    let ctx = DefaultHttpContext()
    ctx.Response.Body <- new MemoryStream()
    configure ctx
    let next: HttpFunc = fun c -> Task.FromResult(Some c)
    handler next ctx |> Async.AwaitTask |> Async.RunSynchronously |> ignore
    ctx

let private bodyString (ctx: HttpContext) : string =
    ctx.Response.Body.Seek(0L, SeekOrigin.Begin) |> ignore
    use reader = new StreamReader(ctx.Response.Body)
    reader.ReadToEnd()

let private etagOf (ctx: HttpContext) : string = string ctx.Response.Headers.ETag

[<Tests>]
let tests =
    testList
        "SSR render-input identity (Phase 1532)"
        [ test "two named source variants get different validators" {
              // THE regression. Both option sets declare a key, so both mint a
              // validator; the keys differ, so the validators must. Before this
              // phase the two ETags were EQUAL and the second request 304'd
              // against the first user's document.
              let optsFor account =
                  { FuaranGiraffeOptions.create with
                      Sources = sourcesFor account
                      SourcesKey = Some("acct:" + account) }

              let a = etagOf (runHandler (fuaranPage (optsFor "alice") shell queryTree) ignore)
              let b = etagOf (runHandler (fuaranPage (optsFor "bob") shell queryTree) ignore)

              Expect.stringStarts a "\"" "a strong validator is still emitted"
              Expect.notEqual a b "two source variants must not share a validator"
          }

          test "the documents those two validators name really do differ" {
              // Guards the test above against passing for the wrong reason: if
              // the two renders were identical there would be nothing to leak
              // and nothing to assert.
              let optsFor account =
                  { FuaranGiraffeOptions.create with
                      Sources = sourcesFor account
                      SourcesKey = Some("acct:" + account) }

              let a =
                  bodyString (runHandler (fuaranPage (optsFor "alice") shell queryTree) ignore)

              let b = bodyString (runHandler (fuaranPage (optsFor "bob") shell queryTree) ignore)

              Expect.stringContains a "alice" "the query source reached the document"
              Expect.stringContains b "bob" "and the other one reached the other document"
              Expect.notEqual a b "the two documents differ"
          }

          test "a shared cache cannot serve one source variant's document to another" {
              // The end-to-end shape of the leak, through the cache rather than
              // through the browser validator.
              let cache = RenderCache.inMemory ()

              let optsFor account =
                  { FuaranGiraffeOptions.create with
                      Sources = sourcesFor account
                      SourcesKey = Some("acct:" + account)
                      Cache = cache }

              runHandler (fuaranPage (optsFor "alice") shell queryTree) ignore |> ignore

              let second =
                  bodyString (runHandler (fuaranPage (optsFor "bob") shell queryTree) ignore)

              Expect.stringContains second "bob" "the second request got its own document"
              Expect.isFalse (second.Contains "alice") "and not the first request's"
          }

          test "a 304 cannot be won with another source variant's validator" {
              let optsFor account =
                  { FuaranGiraffeOptions.create with
                      Sources = sourcesFor account
                      SourcesKey = Some("acct:" + account) }

              let aliceEtag =
                  etagOf (runHandler (fuaranPage (optsFor "alice") shell queryTree) ignore)

              let bobResponse =
                  runHandler (fuaranPage (optsFor "bob") shell queryTree) (fun ctx ->
                      ctx.Request.Headers.IfNoneMatch <- aliceEtag)

              Expect.equal bobResponse.Response.StatusCode 200 "not 304"
              Expect.stringContains (bodyString bobResponse) "bob" "and the body is the second user's"
          }

          test "opaque, unnamed sources emit NO validator and are marked no-store" {
              // The honest answer where no identity exists. A validator here
              // would be a promise the adapter cannot keep.
              let opts =
                  { FuaranGiraffeOptions.create with
                      Sources = sourcesFor "alice" }

              let ctx = runHandler (fuaranPage opts shell queryTree) ignore

              Expect.equal ctx.Response.StatusCode 200 "the document is still served"
              Expect.stringContains (bodyString ctx) "alice" "and it is the right document"
              Expect.equal (etagOf ctx) "" "no ETag header"
              Expect.equal (string ctx.Response.Headers.CacheControl) "no-store" "and it says so"
          }

          test "an If-None-Match is ignored when there is no validator to match" {
              let opts =
                  { FuaranGiraffeOptions.create with
                      Sources = sourcesFor "alice" }

              let ctx =
                  runHandler (fuaranPage opts shell queryTree) (fun c ->
                      c.Request.Headers.IfNoneMatch <- "\"anything\"")

              Expect.equal ctx.Response.StatusCode 200 "no 304 without a validator"
          }

          test "a cache over opaque, unnamed sources is REFUSED where the handler is built" {
              let opts =
                  { FuaranGiraffeOptions.create with
                      Sources = sourcesFor "alice"
                      Cache = RenderCache.inMemory () }

              let raised =
                  Expect.throwsC (fun () -> fuaranPage opts shell queryTree |> ignore) (fun ex -> ex.Message)

              Expect.stringContains raised "SourcesKey" "the refusal names the field that fixes it"
          }

          test "declaring the key lifts the refusal" {
              let opts =
                  { FuaranGiraffeOptions.create with
                      Sources = sourcesFor "alice"
                      SourcesKey = Some "acct:alice"
                      Cache = RenderCache.inMemory () }

              let ctx = runHandler (fuaranPage opts shell queryTree) ignore
              Expect.stringStarts (etagOf ctx) "\"" "and the validator comes back"
          }

          test "sourcesOpaque: what the adapter can and cannot project" {
              Expect.isFalse (RenderInputs.sourcesOpaque BindingSources.empty) "the empty sources are projectable"

              Expect.isFalse
                  (RenderInputs.sourcesOpaque
                      { BindingSources.empty with
                          Locale = "fr-FR"
                          Now = "2026-09-06T00:00:00Z"
                          I18n = Map.ofList [ "greeting", "Bonjour" ] })
                  "locale / instant / catalog are plain strings and are canonicalised, not opaque"

              for name, sources in
                  [ "queries",
                    { BindingSources.empty with
                        QueryResults = Map.ofList [ "q", box 1 ] }
                    "state",
                    { BindingSources.empty with
                        State = Map.ofList [ "k", box 1 ] }
                    "filters",
                    { BindingSources.empty with
                        Filters = Map.ofList [ "f", box 1 ] }
                    "selections",
                    { BindingSources.empty with
                        Selections = Map.ofList [ NodeId "g", box 1 ] }
                    "computed context",
                    { BindingSources.empty with
                        ComputedContext = BindingContext.ofState (Map.ofList [ "k", box 1 ]) }
                    "a replaced i18n resolver",
                    { BindingSources.empty with
                        I18nResolver =
                            { new II18nResolver with
                                member _.Resolve(key, _) = key } }
                    "a replaced capability invoker",
                    { BindingSources.empty with
                        CapabilityInvoker = fun _ _ -> Deferred.Pending } ] do
                  Expect.isTrue
                      (RenderInputs.sourcesOpaque sources)
                      (name + " carries host data no adapter can project")
          }

          test "the canonicalisable half of the sources reaches the validator" {
              // Locale, instant and catalog need no host declaration, so a host
              // that changes one and nothing else still gets a distinct
              // validator — no `SourcesKey` required.
              let withNow now =
                  { FuaranGiraffeOptions.create with
                      Sources = { BindingSources.empty with Now = now } }

              let tree: Node<obj> = Fuaran.markdown "m" "body"

              let a =
                  etagOf (runHandler (fuaranPage (withNow "2026-01-01T00:00:00Z") shell tree) ignore)

              let b =
                  etagOf (runHandler (fuaranPage (withNow "2026-06-01T00:00:00Z") shell tree) ignore)

              Expect.notEqual a b "the host-furnished instant is a render input"
          }

          test "the Custom registry's identity reaches the validator" {
              let tree: Node<obj> = Fuaran.markdown "m" "body"

              let withCustoms customs =
                  { FuaranGiraffeOptions.create with
                      Customs = customs }

              let registered =
                  ServerRegistry.empty
                  |> ServerRegistry.register "mod" "widget" (fun _ -> Feliz.ViewEngine.Html.div [])

              let a =
                  etagOf (runHandler (fuaranPage (withCustoms ServerRegistry.empty) shell tree) ignore)

              let b = etagOf (runHandler (fuaranPage (withCustoms registered) shell tree) ignore)

              Expect.notEqual a b "a registered Custom renderer is a render input"
          }

          test "customsIdentity is order-independent and hash-aware" {
              let r1 =
                  ServerRegistry.empty
                  |> ServerRegistry.register "a" "x" (fun _ -> Feliz.ViewEngine.Html.div [])
                  |> ServerRegistry.register "b" "y" (fun _ -> Feliz.ViewEngine.Html.div [])

              let r2 =
                  ServerRegistry.empty
                  |> ServerRegistry.register "b" "y" (fun _ -> Feliz.ViewEngine.Html.div [])
                  |> ServerRegistry.register "a" "x" (fun _ -> Feliz.ViewEngine.Html.div [])

              Expect.equal
                  (RenderInputs.customsIdentity r1)
                  (RenderInputs.customsIdentity r2)
                  "registration order is not a render input"

              Expect.notEqual
                  (RenderInputs.customsIdentity r1)
                  (RenderInputs.customsIdentity ServerRegistry.empty)
                  "but what is registered is"
          } ]
