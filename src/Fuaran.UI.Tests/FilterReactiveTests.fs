module Fuaran.UI.Tests.FilterReactive

// ============================================================================
//  Phase 423 — filter-channel-driven live re-render of all `Binding.Filter`
//  readers. The twin of the Phase 106 state-walk pack (`StateReactiveTests`):
//  the reactive filter loop reuses the `StateStore` machinery in a distinct
//  `$filters` namespace, so the .NET-testable pieces mirror it exactly.
//
//  Acceptance criteria pinned by these tests:
//
//   1. `Render.filterKeysOfBinding` finds a `Binding.Filter(name, None)`'s name and
//      recurses through `Local` / `Format` / `I18n`-arg sub-bindings exactly as
//      the state walk does — and channel isolation holds: a `Binding.State`
//      contributes NO filter key (and a `Binding.Filter` contributes no state
//      key), so the two subscription sets never cross-bleed.
//   2. `Render.collectFilterKeys` collects every `Binding.Filter` name a tree
//      reads (across layout children + accessibility) and DEDUPES two readers
//      of one filter into a single set entry.
//   3. `FilterStore` (the `$filters` twin of `StateStore`): set / get /
//      snapshot round-trip; `clear` removes a key; `subscribeKeys` fires the
//      watchers of a written key and stays silent for unrelated keys.
// ============================================================================

open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

type private Msg = NoOp

// F# 10 `box _` types as `obj | null`; `FilterStore.set` takes a non-null `obj`.
let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

let private metricFilterBoundTo (id: string) (name: string) : Node<Msg> =
    Fuaran.metric
        id
        { Defaults.metric with
            Label = TextSource.Literal "Revenue"
            Value = binding.filter name }

let private stack (id: string) (children: Node<Msg> list) : Node<Msg> =
    Fuaran.stack
        id
        { Defaults.stack with
            Children = children }

// `FilterStore` is a process-wide singleton; Expecto parallelises test lists by
// default, so the subscribeKeys counters must run sequentially relative to each
// other (a sibling `set` would otherwise interleave on the shared store).
[<Tests>]
let tests =
    testSequenced
    <| testList
        "Phase 423 — filter-channel live re-render"
        [
          // ── filterKeysOfBinding (the binding-level walk) ──────────────────
          test "filterKeysOfBinding returns the name of a Binding.Filter" {
              Expect.equal (Render.filterKeysOfBinding (binding.filter "status")) [ "status" ] "the filter name"
          }

          test "channel isolation: a Binding.State contributes NO filter key" {
              Expect.isEmpty (Render.filterKeysOfBinding (binding.state "terms" 0.0)) "state key is not a filter key"
          }

          test "channel isolation: a Binding.Filter(contributes, None) NO state key" {
              Expect.isEmpty (Render.stateKeysOfBinding (binding.filter "status")) "filter name is not a state key"
          }

          test "filterKeysOfBinding recurses through a Format binding's numeric source" {
              let b: Binding<string> =
                  Binding.Format(binding.filter "revenue", Format.Number(Some 1), LocaleSource.Ambient)

              Expect.equal (Render.filterKeysOfBinding b) [ "revenue" ] "Format reads through to its filter source"
          }

          test "filterKeysOfBinding recurses through I18n `{arg}` sub-bindings" {
              let args = Map.ofList [ "n", (binding.filter "count": Binding<JVal>) ]
              let b: Binding<string> = Binding.I18n("items.count", Some args)
              Expect.equal (Render.filterKeysOfBinding b) [ "count" ] "an I18n arg filter is collected"
          }

          test "filterKeysOfBinding recurses through a Local binding's InitialFrom" {
              let local: Binding<string> =
                  Binding.Local(
                      LocalFlushTrigger.OnBlur,
                      (fun v -> string (box v)),
                      binding.filter "draft",
                      Some(fun s -> nn s),
                      Ok
                  )

              Expect.equal (Render.filterKeysOfBinding local) [ "draft" ] "Local reads its re-sync source"
          }

          // ── collectFilterKeys (the tree walk) ─────────────────────────────
          test "collectFilterKeys returns empty for a filter-free tree" {
              // a metric bound to a Static source reads no filter
              let staticMetric =
                  Fuaran.metric
                      "m0"
                      { Defaults.metric with
                          Label = TextSource.Literal "X"
                          Value = Binding.Static(Some 0.0) }

              Expect.isEmpty (Render.collectFilterKeys (stack "s0" [ staticMetric ]) |> Set.toList) "no Filter readers"
          }

          test "collectFilterKeys finds a single Binding.Filter(name, None) on a Metric source" {
              let tree = metricFilterBoundTo "m" "region"
              Expect.equal (Render.collectFilterKeys tree) (Set.ofList [ "region" ]) "the Metric's filter is collected"
          }

          test "collectFilterKeys DEDUPES two surfaces reading the same filter" {
              let tree =
                  stack "s" [ metricFilterBoundTo "m1" "tier"; metricFilterBoundTo "m2" "tier" ]

              Expect.equal (Render.collectFilterKeys tree) (Set.ofList [ "tier" ]) "two readers → one set entry"
          }

          test "collectFilterKeys collects distinct filter names across nested children" {
              let tree =
                  stack
                      "outer"
                      [ metricFilterBoundTo "a" "region"
                        stack "inner" [ metricFilterBoundTo "b" "tier" ] ]

              Expect.equal (Render.collectFilterKeys tree) (Set.ofList [ "region"; "tier" ]) "both names, deduped"
          }

          // ── FilterStore (the $filters twin of StateStore) ─────────────────
          test "FilterStore set / get / snapshot round-trip" {
              FilterStore.reset ()
              FilterStore.set "flt.a" (nn "north")
              Expect.equal (FilterStore.get "flt.a") (Some(nn "north")) "get reads the written value"
              Expect.equal (FilterStore.snapshot () |> Map.tryFind "flt.a") (Some(nn "north")) "snapshot carries it"
              FilterStore.reset ()
          }

          test "FilterStore clear removes a key from the snapshot" {
              FilterStore.reset ()
              FilterStore.set "flt.b" (nn "x")
              FilterStore.clear "flt.b"
              Expect.isNone (FilterStore.get "flt.b") "cleared key is gone"
              Expect.isFalse (FilterStore.snapshot () |> Map.containsKey "flt.b") "snapshot no longer carries it"
              FilterStore.reset ()
          }

          test "FilterStore.subscribeKeys fires watchers of a written key, silent for others" {
              FilterStore.reset ()
              let mutable hits = 0

              let unsub =
                  FilterStore.subscribeKeys (Set.ofList [ "flt.k" ]) (fun () -> hits <- hits + 1)

              FilterStore.set "flt.k" (nn "1")
              Expect.equal hits 1 "watcher of the written key fired"
              FilterStore.set "flt.other" (nn "2")
              Expect.equal hits 1 "an unrelated key does not fire the watcher"
              unsub ()
              FilterStore.set "flt.k" (nn "3")
              Expect.equal hits 1 "the unsubscribe thunk stops further notifications"
              FilterStore.reset ()
          } ]
