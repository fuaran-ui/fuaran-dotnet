module Fuaran.UI.Tests.QueryDependsOn

// ============================================================================
//  Phase 421 — `Binding.Query` declarative filter dependency (`dependsOn`) +
//  the decoded-accessor identity fix.
//
//  Asserts: a decoded `Query` (identity accessor) resolves to the host's
//  `queryResults` value rather than a value-discarding sentinel; a `Query`'s
//  `dependsOn` names surface on the Filter reactivity channel (so a filter
//  change re-resolves the query) and NOT on the State channel.
// ============================================================================

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Renderer

let private nn (v: 'T) : obj = box v |> Unchecked.nonNull

// The shape `JsonDecode` produces for a decoded `Query` (Phase 421): an identity accessor.
let private decodedQuery: Binding<obj> =
    Binding.Query("orders", (fun (raw: obj) -> raw), Some [ "status"; "region" ])

[<Tests>]
let tests =
    testList
        "Phase 421 — Binding.Query dependsOn + identity accessor"
        [ test "a decoded Query resolves to the host's queryResults value (identity accessor)" {
              let sources =
                  { BindingResolver.empty with
                      QueryResults = Map.ofList [ "orders", nn 42.0 ] }

              match BindingResolver.resolve sources decodedQuery with
              | BindingResolver.Resolved v -> Expect.equal v (nn 42.0) "the host value flows through the decoded Query"
              | other -> failtestf "expected Resolved, got %A" other
          }

          test "an unregistered Query name is NotResolved (drives OnLoading)" {
              match BindingResolver.resolve BindingResolver.empty decodedQuery with
              | BindingResolver.NotResolved -> ()
              | other -> failtestf "expected NotResolved, got %A" other
          }

          test "Query.dependsOn surfaces on the Filter reactivity channel (the invalidation edge)" {
              Expect.equal
                  (Render.filterKeysOfBinding decodedQuery)
                  [ "status"; "region" ]
                  "dependsOn names are the query's filter subscription keys"
          }

          test "Query.dependsOn contributes NO state key (channel isolation)" {
              Expect.isEmpty (Render.stateKeysOfBinding decodedQuery) "dependsOn is a filter edge, not a state key"
          } ]
