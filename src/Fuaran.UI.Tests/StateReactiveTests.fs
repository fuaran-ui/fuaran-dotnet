module Fuaran.UI.Tests.StateReactive

// ============================================================================
//  Phase 106 — state-channel-driven live re-render of all `Binding.State`
//  readers.
//
//  Acceptance criteria pinned by these tests (the .NET-testable pieces; the
//  React re-render itself is browser-only and exercised by the catalog
//  fixture under Playwright):
//
//   1. `Render.collectStateKeys` finds every `Binding.State` key a tree reads
//      — across layout children, disclosure / Metric / label-value-row
//      bindings, and accessibility — and DEDUPES two readers of one key into
//      a single set entry (the "two surfaces, one key" shape).
//   2. State-free trees collect the empty set (zero-cost: `renderStateReactive`
//      subscribes to nothing, so the opt-in stays free for trees that read no
//      state).
//   3. `StateStore.subscribeKeys`: TWO subscribers registered for the same
//      key BOTH fire on a single `set` of that key — the .NET proxy for "two
//      surfaces both re-render on one global Action.SetState". A subscriber
//      keyed on K stays silent when an unrelated key J changes.
//   4. Opt-out path: a value written with NO subscriber registered notifies
//      nobody (legacy per-component-subscription behaviour is preserved — the
//      auto-subscription only fires when a surface opts in).
//   5. Subscription lifecycle: the unsubscribe thunk stops further
//      notifications (no leak across mount/unmount); the empty-key-set
//      subscription is a no-op.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

type private Msg = NoOp

// F# 10 `box _` types as `obj | null`; `StateStore.set` takes a non-null
// `obj`. Same `nn` workaround the other test modules use.
let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

// `StateStore` is a process-wide singleton; tests use distinct keys and
// always unsubscribe what they subscribe so they don't cross-talk.

let private metricBoundTo (id: string) (key: string) : Node<Msg> =
    Fuaran.metric
        id
        { Defaults.metric with
            Label = TextSource.Literal "Revenue"
            Value = binding.state key 0.0 }

let private labelValueBoundTo (id: string) (key: string) : Node<Msg> =
    Fuaran.labelValueRow
        id
        { Defaults.labelValueRow with
            Label = TextSource.Literal "Net"
            Value = binding.state key 0.0 }

let private stack (id: string) (children: Node<Msg> list) : Node<Msg> =
    Fuaran.stack
        id
        { Defaults.stack with
            Children = children }

// `StateStore` is a process-wide singleton; Expecto parallelises test lists
// by default, so these must run sequentially relative to each other — the
// keyless-subscribe + subscribeKeys counters would otherwise see `set`s from
// sibling Phase 106 tests interleaving on the shared store.
[<Tests>]
let tests =
    testSequenced
    <| testList
        "Phase 106 — state-channel live re-render"
        [
          // ── collectStateKeys ─────────────────────────────────────────────
          test "collectStateKeys returns empty for a state-free tree" {
              let tree =
                  stack
                      "root"
                      [ Fuaran.metric
                            "k"
                            { Defaults.metric with
                                Label = TextSource.Literal "Static" } ]

              Expect.isEmpty (Render.collectStateKeys tree |> Set.toList) "no Binding.State readers ⇒ empty set"
          }

          test "collectStateKeys finds a single Binding.State key on a Metric source" {
              let tree = metricBoundTo "k" "terms"
              Expect.equal (Render.collectStateKeys tree) (Set.ofList [ "terms" ]) "the Metric's state key is collected"
          }

          test "collectStateKeys DEDUPES two surfaces reading the same key" {
              // The canonical "two readers, one key" shape: a Metric and a
              // label-value row in the same stack both bound to "terms".
              let tree =
                  stack "root" [ metricBoundTo "metric" "terms"; labelValueBoundTo "lvr" "terms" ]

              Expect.equal
                  (Render.collectStateKeys tree)
                  (Set.ofList [ "terms" ])
                  "two readers of one key collapse to a single set entry"
          }

          test "collectStateKeys collects distinct keys across nested layout children" {
              let inner = stack "inner" [ metricBoundTo "metric" "terms" ]

              let disclosure =
                  Fuaran.disclosure
                      "disc"
                      { Defaults.disclosure with
                          Heading = TextSource.Literal "More"
                          Open = binding.state "panelOpen" false
                          Children = [ inner ] }

              Expect.equal
                  (Render.collectStateKeys disclosure)
                  (Set.ofList [ "panelOpen"; "terms" ])
                  "the disclosure's Open key + the nested Metric's source key are both found"
          }

          test "collectStateKeys reads keys out of an accessibility binding" {
              let node =
                  { metricBoundTo "k" "terms" with
                      Accessibility =
                          Some
                              { Label = Some(binding.state "ariaLabelKey" "")
                                LabelledBy = None
                                DescribedBy = None
                                Role = None
                                LiveRegion = None
                                Hidden = None } }

              Expect.equal
                  (Render.collectStateKeys node)
                  (Set.ofList [ "terms"; "ariaLabelKey" ])
                  "accessibility Label binding contributes its state key"
          }

          // ── StateStore.subscribeKeys — the re-render trigger ─────────────
          test "two subscribers for the same key BOTH fire on a single set" {
              let mutable a = 0
              let mutable b = 0

              let unsubA =
                  StateStore.subscribeKeys (Set.ofList [ "p106-shared" ]) (fun () -> a <- a + 1)

              let unsubB =
                  StateStore.subscribeKeys (Set.ofList [ "p106-shared" ]) (fun () -> b <- b + 1)

              try
                  StateStore.set "p106-shared" (nn 1.0)
                  Expect.equal a 1 "first surface re-rendered"
                  Expect.equal b 1 "second surface re-rendered on the same set"
              finally
                  unsubA ()
                  unsubB ()
          }

          test "a key-scoped subscriber stays silent for unrelated keys" {
              let mutable fired = 0

              let unsub =
                  StateStore.subscribeKeys (Set.ofList [ "p106-watched" ]) (fun () -> fired <- fired + 1)

              try
                  StateStore.set "p106-unrelated" (nn 2.0)
                  Expect.equal fired 0 "a write to a different key does not re-render this surface"
                  StateStore.set "p106-watched" (nn 3.0)
                  Expect.equal fired 1 "a write to the watched key does re-render"
              finally
                  unsub ()
          }

          test "opt-out: a set with no subscriber notifies nobody (legacy preserved)" {
              // No subscribeKeys call — the pre-Phase-106 model. The write
              // simply lands in the store; no surface is forced to re-render.
              StateStore.set "p106-optout" (nn 4.0)

              Expect.equal
                  (StateStore.get "p106-optout")
                  (Some(nn 4.0))
                  "value is stored without any subscription wiring"
          }

          test "unsubscribe stops further notifications (no leak)" {
              let mutable fired = 0

              let unsub =
                  StateStore.subscribeKeys (Set.ofList [ "p106-leak" ]) (fun () -> fired <- fired + 1)

              StateStore.set "p106-leak" (nn 5.0)
              Expect.equal fired 1 "fires while subscribed"
              unsub ()
              StateStore.set "p106-leak" (nn 6.0)
              Expect.equal fired 1 "does not fire after unsubscribe — subscription cleaned up"
          }

          test "subscribeKeys with an empty key set is a no-op subscription" {
              let mutable fired = 0
              let unsub = StateStore.subscribeKeys Set.empty (fun () -> fired <- fired + 1)
              StateStore.set "p106-empty" (nn 7.0)
              Expect.equal fired 0 "an empty key set never fires"
              unsub () // returned thunk is safe to call
          }

          test "keyless subscribe still fires on any key (backward compat)" {
              let mutable fired = 0
              let unsub = StateStore.subscribe (fun () -> fired <- fired + 1)

              try
                  StateStore.set "p106-anyA" (nn 8.0)
                  StateStore.set "p106-anyB" (nn 9.0)
                  Expect.equal fired 2 "the keyless subscriber fires on every set regardless of key"
              finally
                  unsub ()
          } ]
