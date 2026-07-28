module Fuaran.UI.Tests.SelectionLoop

// ============================================================================
//  Phase 427 — the selection loop: reactive `SelectionStore` + the decoded
//  `Binding.Selection` identity-accessor fix + the Selection reactivity
//  channel.
//
//  Asserts: a decoded `Selection` (identity accessor) resolves to the stored
//  row rather than a value-discarding placeholder (the 421 `Query` fix
//  replayed); the `SelectionStore` write/subscribe/snapshot triple works and
//  merges over `BindingSources.Selections`; a `Binding.Selection` reader
//  surfaces its producer id on the Selection reactivity channel and NOT on
//  the State/Filter channels.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

let private nn (v: 'T) : obj = box v |> Unchecked.nonNull

// The shape `JsonDecode` produces for a decoded `Selection` (Phase 427): an identity accessor.
let private decodedSelection: Binding<obj> =
    Binding.Selection("orders-grid", (fun (raw: obj) -> raw), None, None)

[<Tests>]
let tests =
    testList
        "Phase 427 — selection loop"
        [ test "a decoded Selection resolves to the stored row (identity accessor)" {
              let sources =
                  { BindingResolver.empty with
                      Selections = Map.ofList [ NodeId "orders-grid", nn "row-7" ] }

              match BindingResolver.resolve sources decodedSelection with
              | BindingResolver.Resolved v ->
                  Expect.equal v (nn "row-7") "the stored row flows through the decoded Selection"
              | other -> failtestf "expected Resolved, got %A" other
          }

          test "an unwritten Selection is NotResolved (drives OnLoading / empty detail)" {
              match BindingResolver.resolve BindingResolver.empty decodedSelection with
              | BindingResolver.NotResolved -> ()
              | other -> failtestf "expected NotResolved, got %A" other
          }

          test "SelectionStore write → snapshot → keyed subscription round-trip" {
              SelectionStore.reset ()

              let mutable notified = 0

              let unsubscribe =
                  SelectionStore.subscribeKeys (Set.ofList [ "orders-grid" ]) (fun () -> notified <- notified + 1)

              SelectionStore.set "orders-grid" (nn 7)
              SelectionStore.set "other-grid" (nn 1)

              Expect.equal notified 1 "only the watched node id notifies"
              Expect.equal (SelectionStore.get "orders-grid") (Some(nn 7)) "the written row reads back"

              Expect.equal
                  (Map.tryFind "orders-grid" (SelectionStore.snapshot ()))
                  (Some(nn 7))
                  "the snapshot carries the written row"

              SelectionStore.clear "orders-grid"
              Expect.equal notified 2 "clearing notifies watchers"
              Expect.isNone (SelectionStore.get "orders-grid") "the cleared selection is gone"

              unsubscribe ()
              SelectionStore.reset ()
          }

          test "a Selection reader surfaces its producer id on the Selection channel only" {
              Expect.equal
                  (Render.selectionKeysOfBinding decodedSelection)
                  [ "orders-grid" ]
                  "the producer node id is the selection subscription key"

              Expect.isEmpty (Render.stateKeysOfBinding decodedSelection) "no State key"
              Expect.isEmpty (Render.filterKeysOfBinding decodedSelection) "no Filter key"
          }

          test "collectSelectionKeys finds a detail reader's producer id across the tree" {
              let detail =
                  Fuaran.metric
                      "detail"
                      { Defaults.metric with
                          Label = TextSource.Literal "Selected"
                          Value = Binding.Selection("orders-grid", (fun (raw: obj) -> unbox raw), None, None) }

              let tree =
                  Fuaran.dashboard
                      "root"
                      { Defaults.dashboard with
                          Children = [ detail ] }

              Expect.equal
                  (Render.collectSelectionKeys tree)
                  (Set.ofList [ "orders-grid" ])
                  "the tree walk collects the Selection producer id"
          } ]
