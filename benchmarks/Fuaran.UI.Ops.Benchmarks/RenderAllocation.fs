module Fuaran.UI.Ops.Benchmarks.RenderAllocation

open System
open System.Diagnostics
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

// ============================================================================
//  RenderAllocation (Phase 207) — the RENDER-SPINE micro-case family, off the
//  BenchmarkDotNet hot path.
//
//  Phase 207 removed three per-node / per-frame allocators from the renderers:
//  the `sprintf` class + ARIA-id construction (now the `Css` / `Ids` inline
//  vocabulary), the bottom-up `Set.union` reactive key walk (now one `HashSet`
//  DFS), and the four throwaway snapshot `Map`s the reactive path built per
//  render to merge the live stores (now each store's `overlayOnto` read view).
//
//  All three edits are OUTPUT-IDENTICAL, so nothing in the test suite can see a
//  regression in them. The `HotPathVocabularyTests` guard catches a revert of
//  the SHAPE; this family gives the Phase 201 perf gate a NUMBER for the cost,
//  so a change that keeps the named primitives but makes them expensive is
//  visible too.
//
//  Measured HERE rather than in a BenchmarkDotNet class, for the `AppendRate`
//  reason: what matters is allocated bytes per operation over a realistic tree,
//  which `GC.GetAllocatedBytesForCurrentThread` reports directly and a
//  repeat-the-same-call hot loop reports awkwardly.
//
//  THE FULL RENDER is deliberately NOT measured here. `Render.render` returns a
//  Feliz `ReactElement`, and on .NET that is a description rather than a
//  rendered surface — its allocation profile is Feliz's, not the render spine's,
//  and it would swamp exactly the costs this phase moved. What is measured is
//  the spine: the vocabulary a node's chrome is built from, the reactive walk a
//  frame pays before it renders anything, and the store merge that precedes it.
// ============================================================================

/// Boxed-value helper — the `Corpus` / `FragmentMemoTests` `Unchecked.nonNull`
/// discipline, so the `obj`-typed store values type-check under nullable
/// reference types.
let private v (x: obj | null) : obj = Unchecked.nonNull x

/// One render-spine scenario: a tree at a representative size, with a live
/// store populated to match. Sizes mirror `Corpus`'s (a card / a metric panel /
/// a dense dashboard region) so the two families' curves are readable together.
type Scenario =
    {
        Name: string
        /// Nodes in `Tree`, including the root.
        NodeCount: int
        /// Distinct `Binding.State` keys the tree reads.
        KeyCount: int
        Tree: Node<unit>
    }

/// A dashboard of `leafCount` state-bound metrics — the shape the reactive
/// re-render path walks on every frame. Every metric reads a DISTINCT key, so
/// the key walk does real collection work rather than deduplicating one key
/// `leafCount` times.
let private mkTree (leafCount: int) : Node<unit> =
    let children =
        [ for i in 0 .. leafCount - 1 ->
              Fuaran.metric
                  $"metric-{i}"
                  { Defaults.metric with
                      Label = TextSource.Literal $"Metric {i}"
                      Value = Binding.State($"metric.{i}", Option.None)
                      Tone = if i % 2 = 0 then ToneVariant.Brand else ToneVariant.Subdued } ]

    Fuaran.dashboard
        "render-root"
        { Defaults.dashboard<unit> with
            Children = children }

let private scenario (name: string) (leafCount: int) : Scenario =
    { Name = name
      NodeCount = leafCount + 1
      KeyCount = leafCount
      Tree = mkTree leafCount }

/// ≈ a single card.
let small = scenario "Small" 4

/// ≈ a metric panel.
let medium = scenario "Medium" 16

/// ≈ a dense dashboard region.
let large = scenario "Large" 64

/// All three sizes, smallest-first.
let all = [ small; medium; large ]

// ─── Measurement ───────────────────────────────────────────────────────────

/// Mean wall-time (ns) and allocation (bytes) per invocation of `work`, over
/// `count` iterations, after a warm-up pass that takes the JIT out of the
/// measurement. `work` returns an `int` derived from its result, accumulated
/// into a sink so the call cannot be optimised away — an `int` rather than the
/// result itself so the harness's own boxing never enters the allocation count
/// it is reporting.
let private measureOp (count: int) (work: unit -> int) : float * float =
    let mutable sink = 0

    for _ in 1 .. min count 256 do
        sink <- sink + work ()

    let allocBefore = GC.GetAllocatedBytesForCurrentThread()
    let sw = Stopwatch.StartNew()

    for _ in 1..count do
        sink <- sink + work ()

    sw.Stop()
    let allocAfter = GC.GetAllocatedBytesForCurrentThread()

    if sink = Int32.MinValue then
        // Unreachable in practice; keeps `sink` live so the loop is not elided.
        printfn "sink=%d" sink

    let meanNs = sw.Elapsed.TotalMilliseconds * 1_000_000.0 / float count
    let allocB = float (allocAfter - allocBefore) / float count
    meanNs, allocB

/// The reactive subscription walk one frame pays before it renders anything:
/// `collectStateKeys` over the whole tree. Per TREE, not per node.
let measureStateKeys (s: Scenario) (count: int) : float * float =
    measureOp count (fun () -> (Render.collectStateKeys s.Tree).Count)

/// The live-store merge the reactive render path performs per frame, once per
/// channel. Measured on a PRIVATE store instance holding one value per key the
/// tree reads, so the number is a function of the scenario and not of whatever
/// else a process-global store happens to hold.
let measureLiveStateMerge (s: Scenario) (count: int) : float * float =
    let store = StateStore.StateStoreInstance $"fuaran.bench.{s.Name}."

    for i in 0 .. s.KeyCount - 1 do
        store.Set($"metric.{i}", v (box (float i)))

    let hostSources: Map<string, obj> = Map.ofList [ "host.locale", v (box "en-GB") ]

    measureOp count (fun () -> (store.OverlayOnto(hostSources, id)).Count)

/// The per-node class + ARIA-id vocabulary, once per node of the tree: the
/// `Theme.nodeClassName` composition plus the `Css` / `Ids` builders a node's
/// chrome reaches for. Reported per TREE so it scales with `NodeCount` like the
/// other two, keeping the three metrics comparable at a glance.
let measureClassVocabulary (s: Scenario) (count: int) : float * float =
    let kind = NodeKind.Metric Defaults.metric

    let style =
        { Defaults.style with
            Tone = ToneVariant.Brand }

    measureOp count (fun () ->
        let mutable acc = 0

        for i in 0 .. s.NodeCount - 1 do
            acc <-
                acc
                + (Theme.nodeClassName kind style).Length
                + (Css.metric (Theme.toneVar ToneVariant.Brand)).Length
                + (Css.fact (Theme.toneVar ToneVariant.Subdued) "").Length
                + (Ids.tab "render-root" i).Length

        acc)

// ─── Fragment expansion (Phase 1151) ───────────────────────────────────────
//
//  The fourth render-spine family, and the one this harness exists to make
//  visible: a `FragmentRef` expands by deep-copying the whole fragment body with
//  every interior id rewritten, and before Phase 1151 that copy was paid again
//  on every render of every ref. The memo is OUTPUT-IDENTICAL, so nothing in the
//  test suite can see it removed and nothing but a number can see it made
//  expensive — which is exactly the gap the Phase 201 gate is for.
//
//  Both sides are measured against the SAME tree and the SAME prefix, so the
//  pair reads as a before/after on one line: `uncached` is the walk as it stood
//  after Phase 207, `memo` is the same walk through the cache. Per the estate's
//  pre-publication framing these are absolute numbers for a gate to hold, not a
//  regression assertion this phase makes.

/// A fragment body at the scenario's size — the subtree an expansion copies.
let private fragmentBody (s: Scenario) : Node<unit> = mkTree (s.NodeCount - 1)

/// The uncached expansion: the id-rewriting walk itself, once per call. This is
/// what a re-render paid per ref before the memo.
let measureFragmentExpansionUncached (s: Scenario) (count: int) : float * float =
    let b = fragmentBody s
    measureOp count (fun () -> (Render.expandFragmentUncached "ref1." b).Id.Length)

/// The memoised expansion: the same call through the process-global cache, with
/// the body instance and prefix stable across calls — the reactive-re-render
/// case, where a tree held in state is re-rendered against the same body. The
/// cache is cleared first so the first call is an honest miss.
let measureFragmentExpansionMemo (s: Scenario) (count: int) : float * float =
    let b = fragmentBody s
    FragmentExpansion.clear ()
    measureOp count (fun () -> (Render.expandFragment "ref1." b).Id.Length)
