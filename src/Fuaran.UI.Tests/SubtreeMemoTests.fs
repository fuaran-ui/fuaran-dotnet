module Fuaran.UI.Tests.SubtreeMemo

// ============================================================================
//  Phase 2547 — the subtree memo. `render` returns the element it built last
//  time for a node it has already rendered under the same context and store
//  write generation, when the context opts in. The client renderer's elements
//  are Feliz's and do not build on .NET, so this suite pins the three keys on
//  the memo's own value-level pieces — the decision (`SubtreeMemo.hit`), the
//  table (`tryGet` / `set`, keyed by node identity), the generation every
//  store write advances — and the default (`create` leaves it off). The
//  element side, a kept subtree returning the identical element across host
//  renders, is exercised in a browser by the renderer benchmark's study arm.
// ============================================================================

open Expecto
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

let private tree () : Node<obj> =
    Fuaran.stack
        "root"
        { Defaults.stack with
            Children =
                [ Fuaran.heading
                      "h"
                      { Defaults.heading with
                          Text = TextSource.Literal "memoised text" } ] }

/// An element stand-in: the memo never inspects it, so its identity is all a
/// test needs. `Unchecked.defaultof` keeps Feliz's .NET surface out of the way.
let private element () : ReactElement = Unchecked.defaultof<ReactElement>

let private entry (context: obj) (generation: int) : Render.SubtreeMemo.Entry =
    { Context = context
      Generation = generation
      Element = element () }

[<Tests>]
let subtreeMemo =
    testList
        "Phase 2547 — the subtree memo"
        [ test "off by default: `create` leaves the memo off" {
              let node = tree ()
              let ctx = Render.RenderContext.create BindingResolver.empty ignore node
              Expect.isFalse ctx.SubtreeMemo "a host opts in with `with SubtreeMemo = true`"
          }

          test "the decision: the same context at the same generation hits" {
              let ctx = nn (tree ())

              Expect.isTrue
                  (Option.isSome (Render.SubtreeMemo.hit (Some(entry ctx 7)) ctx 7))
                  "a hit serves the entry's element"
          }

          test "the decision: no entry misses" {
              Expect.isTrue
                  (Option.isNone (Render.SubtreeMemo.hit None (nn (tree ())) 7))
                  "nothing built, nothing served"
          }

          test "the decision: a later generation misses" {
              let ctx = nn (tree ())

              Expect.isTrue
                  (Option.isNone (Render.SubtreeMemo.hit (Some(entry ctx 7)) ctx 8))
                  "an element built before a write is never served after one"
          }

          test "the decision: a structurally equal context that is a different value misses" {
              let a = nn (tree ())
              let b = nn (tree ())

              Expect.isTrue
                  (Option.isNone (Render.SubtreeMemo.hit (Some(entry a 7)) b 7))
                  "the context key is identity, not structure"
          }

          test "the table is keyed by node identity" {
              let node = tree ()
              let other = tree ()
              let ctx = nn (tree ())
              Render.SubtreeMemo.set (nn node) (entry ctx 1)
              Expect.isTrue (Option.isSome (Render.SubtreeMemo.tryGet (nn node))) "the node that was set is found"

              Expect.isTrue
                  (Option.isNone (Render.SubtreeMemo.tryGet (nn other)))
                  "a structurally equal node that is a different value is not"
          }

          test "the table serves the latest entry for a node" {
              let node = tree ()
              let first = nn (tree ())
              let second = nn (tree ())
              Render.SubtreeMemo.set (nn node) (entry first 1)
              Render.SubtreeMemo.set (nn node) (entry second 2)

              match Render.SubtreeMemo.tryGet (nn node) with
              | Some e ->
                  Expect.isTrue
                      (obj.ReferenceEquals(e.Context, second) && e.Generation = 2)
                      "a rebuild replaces the entry"
              | None -> failtest "the node was set"
          }

          test "a state write, removal and reset each advance the generation" {
              StateStore.reset ()
              let before = StateStore.generation ()
              StateStore.set "memo.probe" (nn 1.0)
              Expect.isGreaterThan (StateStore.generation ()) before "a write advances the generation"
              let afterSet = StateStore.generation ()
              StateStore.remove "memo.probe"
              Expect.isGreaterThan (StateStore.generation ()) afterSet "a removal advances it too"
              let afterRemove = StateStore.generation ()
              StateStore.reset ()
              Expect.isGreaterThan (StateStore.generation ()) afterRemove "a reset advances it too"
          }

          test "a filter, selection or query write advances the generation too" {
              let g0 = StateStore.generation ()
              FilterStore.set "memo.filter" (nn "x")
              let g1 = StateStore.generation ()
              SelectionStore.set "memo.node" (nn "row")
              let g2 = StateStore.generation ()
              QueryStore.set "memo.query" (nn "rows")
              let g3 = StateStore.generation ()
              Expect.isTrue (g0 < g1 && g1 < g2 && g2 < g3) "every wrapped store writes through the one generation"
              FilterStore.reset ()
              SelectionStore.reset ()
              QueryStore.reset ()
          }

          test "a scoped store's write advances the generation" {
              StateStore.resetAllScopes ()
              let before = StateStore.generation ()
              (StateStore.forScope "memo-scope").Set("k", nn 1.0)
              Expect.isGreaterThan (StateStore.generation ()) before "a scope is an instance of the same store"
              StateStore.resetAllScopes ()
          } ]
