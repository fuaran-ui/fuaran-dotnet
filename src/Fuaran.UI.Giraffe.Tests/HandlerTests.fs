module Fuaran.UI.Giraffe.Tests.HandlerTests

open System.IO
open System.Threading.Tasks
open Expecto
open Microsoft.AspNetCore.Http
open Giraffe
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Giraffe

// ─── A small Node<obj> fixture (the SSR tier is dispatch-less) ───────────────

let private tree: Node<obj> =
    Fuaran.card
        "root"
        { Defaults.card<obj> with
            Heading = Some(TextSource.Literal "Pricing")
            Children = [ Fuaran.markdown "body" "Hello **world**" ] }

let private shell = DocumentShell.create "Pricing"

// ─── HttpContext test harness ────────────────────────────────────────────────

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
        "Giraffe handlers"
        [ test "fuaranPage renders a full document with a strong ETag" {
              let opts = FuaranGiraffeOptions.create
              let ctx = runHandler (fuaranPage opts shell tree) ignore
              let body = bodyString ctx
              Expect.equal ctx.Response.StatusCode 200 "200 OK"
              Expect.stringStarts body "<!DOCTYPE html>" "full document"
              Expect.stringContains body "data-fuaran-node-id=\"root\"" "the tree's body fragment is present"
              Expect.stringContains body "<title>Pricing</title>" "the shell title"
              Expect.stringStarts (etagOf ctx) "\"" "a strong (quoted) ETag header is set"
          }

          test "If-None-Match with the matching ETag serves 304 with no body" {
              let opts = FuaranGiraffeOptions.create
              // First request to learn the ETag.
              let first = runHandler (fuaranPage opts shell tree) ignore
              let etag = etagOf first
              // Second request echoing the ETag.
              let second =
                  runHandler (fuaranPage opts shell tree) (fun ctx -> ctx.Request.Headers.IfNoneMatch <- etag)

              Expect.equal second.Response.StatusCode 304 "304 Not Modified"
              Expect.equal (bodyString second) "" "no body on 304"
              Expect.equal (etagOf second) etag "the ETag is re-sent on 304"
          }

          test "the render cache is consulted before render (hit serves the cached document)" {
              let cache = RenderCache.inMemory ()

              let opts =
                  { FuaranGiraffeOptions.create with
                      Cache = cache }
              // Learn the ETag without poisoning, via a throwaway no-cache render.
              let etag =
                  etagOf (runHandler (fuaranPage FuaranGiraffeOptions.create shell tree) ignore)
              // Poison the cache under that ETag — a hit must serve the sentinel.
              cache.Set(etag, "SENTINEL")
              let ctx = runHandler (fuaranPage opts shell tree) ignore
              Expect.equal (bodyString ctx) "SENTINEL" "cache hit short-circuits the render"
          }

          test "the render cache is populated after a miss" {
              let cache = RenderCache.inMemory ()

              let opts =
                  { FuaranGiraffeOptions.create with
                      Cache = cache }

              let ctx = runHandler (fuaranPage opts shell tree) ignore
              let etag = etagOf ctx
              Expect.isSome (cache.TryGet etag) "the rendered document was stored under its ETag"
          }

          test "fuaranHydratablePage embeds the Phase 143 hydrate payload" {
              let opts = FuaranGiraffeOptions.create
              let ctx = runHandler (fuaranHydratablePage opts shell tree) ignore
              let body = bodyString ctx
              Expect.stringContains body "<!DOCTYPE html>" "full document"

              Expect.stringContains
                  body
                  "id=\"fuaran-hydrate-root\""
                  "the embedded wire-tree script keyed to the root id"

              Expect.stringContains body "application/json" "the hydrate payload is a JSON script"
          }

          test "fuaranFragment emits the body only (no document shell)" {
              let opts = FuaranGiraffeOptions.create
              let ctx = runHandler (fuaranFragment opts tree) ignore
              let body = bodyString ctx
              Expect.isFalse (body.Contains "<!DOCTYPE") "no doctype — fragment only"
              Expect.isFalse (body.Contains "<html") "no html element"
              Expect.stringContains body "data-fuaran-node-id=\"root\"" "the tree fragment is present"
          }

          test "fuaranIslandsPage wraps islands + embeds per-island hydrate scripts" {
              let opts = FuaranGiraffeOptions.create

              let islandsTree: Node<obj> =
                  Fuaran.dashboard
                      "p"
                      { Defaults.dashboard<obj> with
                          Children =
                              [ Fuaran.markdown "static" "# Static"
                                Fuaran.markdown "i" "interactive" |> Node.asIsland "widget" ] }

              let ctx = runHandler (fuaranIslandsPage opts shell islandsTree) ignore
              let body = bodyString ctx
              Expect.stringContains body "<!DOCTYPE html>" "full document"
              Expect.stringContains body "data-fuaran-island=\"widget\"" "island boundary marker"
              Expect.stringContains body "id=\"fuaran-hydrate-island-widget\"" "per-island hydrate script"
          }

          test "a hydratable render gets a distinct ETag from a static render of the same tree" {
              let opts = FuaranGiraffeOptions.create
              let staticEtag = etagOf (runHandler (fuaranPage opts shell tree) ignore)

              let hydratableEtag =
                  etagOf (runHandler (fuaranHydratablePage opts shell tree) ignore)

              Expect.notEqual staticEtag hydratableEtag "render mode folds into the cache key"
          } ]
