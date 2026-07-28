module Fuaran.UI.Tests.CallResultTarget

// ============================================================================
//  Phase 428 — `Action.Call`'s declarative result target: the `QueryStore`
//  (fourth store twin) + the Query reactivity channel.
//
//  Asserts: the QueryStore write/subscribe/snapshot triple works; a
//  `Binding.Query` reader surfaces its own name on the Query channel (the
//  `Call … into Query <name>` re-render subscription) while keeping its
//  `dependsOn` names on the Filter channel (channel isolation).
// ============================================================================

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Renderer

let private nn (v: 'T) : obj = box v |> Unchecked.nonNull

let private decodedQuery: Binding<obj> =
    Binding.Query("orders", (fun (raw: obj) -> raw), Some [ "status" ])

[<Tests>]
let tests =
    testList
        "Phase 428 — Call result target"
        [ test "QueryStore write → snapshot → keyed subscription round-trip" {
              QueryStore.reset ()

              let mutable notified = 0

              let unsubscribe =
                  QueryStore.subscribeKeys (Set.ofList [ "orders" ]) (fun () -> notified <- notified + 1)

              QueryStore.set "orders" (nn 42)
              QueryStore.set "customers" (nn 1)

              Expect.equal notified 1 "only the watched query name notifies"
              Expect.equal (QueryStore.get "orders") (Some(nn 42)) "the written result reads back"

              Expect.equal
                  (Map.tryFind "orders" (QueryStore.snapshot ()))
                  (Some(nn 42))
                  "the snapshot carries the written result"

              QueryStore.clear "orders"
              Expect.isNone (QueryStore.get "orders") "the cleared result is gone"

              unsubscribe ()
              QueryStore.reset ()
          }

          test "a Query reader surfaces its own name on the Query channel only" {
              Expect.equal
                  (Render.queryKeysOfBinding decodedQuery)
                  [ "orders" ]
                  "the query name is the Query-channel subscription key"

              Expect.equal
                  (Render.filterKeysOfBinding decodedQuery)
                  [ "status" ]
                  "dependsOn stays the Filter-channel subscription (421)"

              Expect.isEmpty (Render.stateKeysOfBinding decodedQuery) "no State key"
              Expect.isEmpty (Render.selectionKeysOfBinding decodedQuery) "no Selection key"
          } ]
