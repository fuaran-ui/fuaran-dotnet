module Fuaran.UI.Giraffe.Tests.CachingTests

// ============================================================================
//  Phase 790 — the render cache is BOUNDED.
//
//  The ETag key is a full SHA-256 over the canonical tree wire plus theme, so
//  the keys are strong and there is no poisoning; the defect was growth. A
//  high-fan-out (or attacker-varied) tree stream mints a fresh key per request,
//  and a store that never evicts then grows once per distinct emission for the
//  process lifetime. These tests fail if the eviction is removed.
// ============================================================================

open Expecto
open Fuaran.UI.Giraffe

[<Tests>]
let tests =
    testList
        "Giraffe render cache (Phase 790)"
        [ test "the in-memory cache evicts under sustained varied keys" {
              let cache = RenderCache.bounded 8

              for i in 1..500 do
                  cache.Set(sprintf "\"etag-%d\"" i, sprintf "<html>%d</html>" i)

              // Nothing exposes a count on the seam (the interface is the host's
              // contract, not an observability surface), so the bound is read
              // where it matters: the oldest key is gone and the newest is not.
              Expect.isSome (cache.TryGet "\"etag-500\"") "the most recent entry is retained"
              Expect.isNone (cache.TryGet "\"etag-1\"") "the oldest entry was evicted"
              Expect.isNone (cache.TryGet "\"etag-100\"") "and so was everything outside the last 8"
          }

          test "an entry within the bound round-trips" {
              let cache = RenderCache.bounded 4
              cache.Set("\"a\"", "<html>A</html>")
              Expect.equal (cache.TryGet "\"a\"") (Some "<html>A</html>") "a stored document is served back"
              Expect.isNone (cache.TryGet "\"missing\"") "an unknown key misses"
          }

          test "the default in-memory cache is bounded, not unbounded" {
              // `RenderCache.inMemory` is what the samples and docs wire, so the
              // DEFAULT is the one that has to be finite. Writing well past the
              // default capacity must not retain everything.
              let cache = RenderCache.inMemory ()
              let overflow = RenderCache.defaultCapacity + 50

              for i in 1..overflow do
                  cache.Set(sprintf "\"etag-%d\"" i, "<html/>")

              Expect.isNone (cache.TryGet "\"etag-1\"") "the earliest entries are evicted past the default bound"
              Expect.isSome (cache.TryGet(sprintf "\"etag-%d\"" overflow)) "the most recent entry is retained"
          }

          test "a zero / negative capacity is clamped rather than defeating the cache" {
              let cache = RenderCache.bounded 0
              cache.Set("\"a\"", "<html>A</html>")
              Expect.equal (cache.TryGet "\"a\"") (Some "<html>A</html>") "at least one entry is held"
          }

          test "the no-op cache still never stores" {
              let cache = RenderCache.none
              cache.Set("\"a\"", "<html>A</html>")
              Expect.isNone (cache.TryGet "\"a\"") "the pass-through default is unchanged"
          } ]
