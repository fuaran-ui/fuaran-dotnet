module Fuaran.UI.Tests.FragmentExpansionMemo

// ============================================================================
//  Phase 1151 — the fragment-expansion memo's SOUNDNESS proof.
//
//  Phase 207 declined to install this cache because the shape it was handed was
//  both unsound (keyed on the fragment NAME, which a differently-declared tree
//  reuses) and unable to hit (keyed on a per-render context whose every key is
//  distinct within one pass). This phase keys on the BODY INSTANCE and owns the
//  cache outside any render, so both halves have to be proven rather than
//  asserted — and a memo is OUTPUT-IDENTICAL by construction, so no behavioural
//  test elsewhere in this suite can see it wrong.
//
//  The suite is deliberately in two parts, because the two claims have different
//  falsifiers:
//
//   A. THE PRIMITIVE, on a PRIVATE cache instance — hit/miss accounting,
//      body-instance keying, prefix keying, bound + eviction, byte-identical
//      recompute after a clear, and concurrent access from many threads. Private
//      because the process-global cache is shared with every other test in this
//      assembly that renders a `FragmentRef`, so an exact-count assertion
//      against it would be a race, and a racy test is worse than none.
//
//   B. THE ROUTING, on the process-global path — that `Render.expandFragment`
//      namespaces correctly, that two different bodies under one fragment name
//      never share an expansion, and that the renderer's `FragmentRef` arm
//      actually calls the named primitive (a source-shape guard with its own
//      go-red proof, the Phase 207 `HotPathVocabularyTests` discipline: an
//      output-identical optimisation needs a STRUCTURAL defence, because a
//      cleanup pass that reverts it breaks nothing a behavioural test watches).
//
//  On the Fable leg: the only difference between the pipelines is the `lock`,
//  which `FragmentExpansion` compiles out under `#if FABLE_COMPILER` and which
//  is not on the result path at all. So the byte-identity proven here — cached
//  expansion vs recomputed expansion, through the canonical encoder — carries to
//  the browser by construction, and the gate's `dotnet fable` leg proves the
//  same source compiles there.
// ============================================================================

open System
open System.Collections.Concurrent
open System.IO
open System.Text.RegularExpressions
open System.Threading.Tasks
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

module CanonicalJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

type private Msg = NoOp

// ─── Fixtures ──────────────────────────────────────────────────────────────

let private metric (id: string) (label: string) : Node<Msg> =
    Fuaran.metric
        id
        { Defaults.metric with
            Label = TextSource.Literal label }

let private stack (id: string) (children: Node<Msg> list) : Node<Msg> =
    Fuaran.stack
        id
        { Defaults.stack with
            Children = children }

/// A fragment body of a few interior ids — the thing an expansion deep-copies.
let private body (marker: string) : Node<Msg> =
    stack "card" [ metric "title" $"Title {marker}"; metric "count" $"Count {marker}" ]

/// Canonical bytes of a tree — the comparison every value assertion here uses.
/// Structural `=` on a `Node` is not available in general (a tree may carry
/// handler closures), and the canonical encoder is also exactly the "byte
/// identical" the acceptance criterion asks about.
let private bytes (node: Node<Msg>) : string = CanonicalJson.encodeNode node

/// The renderer's own namespacing walk is private to `Render.fs`; this mirrors
/// only what the assertions need to know INDEPENDENTLY of it — that an
/// expansion under `prefix` prefixes every interior id. Deliberately not a
/// re-implementation of `namespaceNode`: it reads ids off the result rather
/// than predicting the whole tree, so it cannot pass by making the same mistake.
let rec private ids (node: Node<Msg>) : string list =
    match node.Kind with
    | NodeKind.Box spec -> node.Id :: (spec.Children |> List.collect ids)
    | _ -> [ node.Id ]

// ─── A. The primitive, on a private cache ──────────────────────────────────

let private privateCache () =
    FragmentExpansion.FragmentExpansionCache(
        FragmentExpansion.defaultBodyCapacity,
        FragmentExpansion.defaultPrefixCapacity
    )

/// A compute that records how many times it ran — the hit proof. A memo that
/// silently recomputes on every call is indistinguishable from no memo by
/// output alone, which is the whole difficulty this phase's tests face.
let private countingCompute (calls: int ref) (prefix: string) =
    fun (b: Node<Msg>) ->
        calls.Value <- calls.Value + 1
        Render.expandFragmentUncached prefix b

[<Tests>]
let tests =
    testList
        "fragment expansion memo (Phase 1151)"
        [
          // ── A. The primitive ─────────────────────────────────────────────
          test "a second expansion of the same (body, prefix) is served from the cache, not recomputed" {
              let cache = privateCache ()
              let calls = ref 0
              let b = body "a"

              let first = cache.Expand(b, "ref1.", countingCompute calls "ref1.")
              let second = cache.Expand(b, "ref1.", countingCompute calls "ref1.")

              Expect.equal calls.Value 1 "the expansion ran exactly once across two calls"
              Expect.isTrue (Object.ReferenceEquals(first, second)) "the hit returned the cached tree itself"
              Expect.equal cache.Hits 1 "one hit"
              Expect.equal cache.Misses 1 "one miss"
          }

          test "an ABSENT slot and a PRESENT slot both round-trip through the erased store" {
              // The slot's NULLABILITY, made falsifiable. Expansions are held
              // erased (`objnull`, because `box` is typed that way), so absence
              // and a stored value travel the same channel and can be confused:
              // a store that minted a slot with `Unchecked.defaultof<_>` and read
              // that back as a value would serve a null tree, and one that read a
              // null as a hit would stop recomputing. Neither is visible to the
              // hit/miss counters alone — this walks both states end to end.
              let cache = privateCache ()
              let calls = ref 0
              let b = body "slot"

              // ABSENT — nothing has ever been stored under this (body, prefix),
              // so the probe misses, `compute` runs, and the result is written in.
              let fromAbsent = cache.Expand(b, "ref1.", countingCompute calls "ref1.")

              Expect.equal calls.Value 1 "the absent slot missed and computed"
              Expect.equal cache.Misses 1 "and was accounted a miss"
              Expect.equal cache.Hits 0 "with no hit yet"

              Expect.equal
                  (ids fromAbsent)
                  [ "ref1.card"; "ref1.title"; "ref1.count" ]
                  "the miss produced the namespaced expansion, not an empty read-back"

              Expect.stringContains (bytes fromAbsent) "Title slot" "carrying this body's own content"

              // PRESENT — the same key now holds a value, so the probe hits and
              // the erased value comes back out as the very tree that went in.
              let fromPresent = cache.Expand(b, "ref1.", countingCompute calls "ref1.")

              Expect.equal calls.Value 1 "the present slot hit, so nothing recomputed"
              Expect.equal cache.Hits 1 "and was accounted a hit"
              Expect.isTrue (Object.ReferenceEquals(fromAbsent, fromPresent)) "the stored tree itself came back out"
              Expect.equal (bytes fromPresent) (bytes fromAbsent) "byte-identical through the canonical encoder"

              // Still ABSENT — a prefix the slot has never held stays a miss even
              // though the body's slot now exists, so absence is the probe's
              // `None` rather than a null sitting in a freshly-minted neighbour.
              let other = cache.Expand(b, "ref2.", countingCompute calls "ref2.")

              Expect.equal calls.Value 2 "an unheld prefix in an existing slot is still a miss"

              Expect.equal
                  (ids other)
                  [ "ref2.card"; "ref2.title"; "ref2.count" ]
                  "and computes its own expansion under its own prefix"
          }

          test "the same fragment NAME with a DIFFERENT BODY never returns the stale expansion" {
              // The soundness criterion. Two bodies are constructed under the
              // same fragment name and the same ref prefix — a name-keyed cache
              // would serve the first to the second, which is the defect this
              // keying exists to make impossible.
              let cache = privateCache ()
              let calls = ref 0
              let bodyA = body "A"
              let bodyB = body "B"

              let expandedA = cache.Expand(bodyA, "ref1.", countingCompute calls "ref1.")
              let expandedB = cache.Expand(bodyB, "ref1.", countingCompute calls "ref1.")

              Expect.equal calls.Value 2 "a different body is a miss, so the expansion ran again"
              Expect.notEqual (bytes expandedB) (bytes expandedA) "the second ref got ITS OWN body, not the first's"
              Expect.stringContains (bytes expandedA) "Title A" "A's expansion carries A's content"
              Expect.stringContains (bytes expandedB) "Title B" "B's expansion carries B's content"

              // …and the first key is still intact afterwards: a second body
              // must not displace the first's entry, only sit beside it.
              let againA = cache.Expand(bodyA, "ref1.", countingCompute calls "ref1.")
              Expect.equal calls.Value 2 "A is still cached"
              Expect.equal (bytes againA) (bytes expandedA) "and still expands to A"
          }

          test "a different PREFIX under the same body is a distinct entry" {
              let cache = privateCache ()
              let calls = ref 0
              let b = body "a"

              let one = cache.Expand(b, "ref1.", countingCompute calls "ref1.")
              let two = cache.Expand(b, "ref2.", countingCompute calls "ref2.")

              Expect.equal calls.Value 2 "a different prefix is a distinct key, so both computed"
              Expect.notEqual (bytes two) (bytes one) "sibling refs to one fragment stay DOM-unique"
              Expect.contains (ids one) "ref1.title" "the first ref's interior ids are namespaced under it"
              Expect.contains (ids two) "ref2.title" "the second ref's are namespaced under it"
          }

          test "a cleared cache recomputes a BYTE-IDENTICAL expansion" {
              // The "identical output with and without the cache" criterion,
              // made falsifiable: the second tree is a genuinely fresh object
              // (so the cache really was dropped) whose canonical bytes are
              // equal to the cached one's.
              let cache = privateCache ()
              let calls = ref 0
              let b = body "a"

              let cached = cache.Expand(b, "ref1.", countingCompute calls "ref1.")
              cache.Clear()
              let recomputed = cache.Expand(b, "ref1.", countingCompute calls "ref1.")

              Expect.equal calls.Value 2 "the clear really dropped the entry"
              Expect.isFalse (Object.ReferenceEquals(cached, recomputed)) "and the second tree is freshly computed"
              Expect.equal (bytes recomputed) (bytes cached) "with byte-identical canonical output"
              Expect.equal cache.Count 1 "the cleared cache holds only the new entry"
          }

          test "the bound holds: distinct bodies past capacity evict rather than accumulate" {
              let cache = FragmentExpansion.FragmentExpansionCache(4, 2)
              let calls = ref 0

              for i in 1..20 do
                  cache.Expand(body (string i), "ref1.", countingCompute calls "ref1.") |> ignore

              Expect.equal calls.Value 20 "twenty distinct bodies are twenty misses"
              Expect.equal cache.BodyCapacity 4 "the configured bound"
              Expect.isTrue (cache.Count <= 4) "and the cache never grew past it"
          }

          test "concurrent expansion on .NET neither corrupts the cache nor serves a wrong answer" {
              // The .NET concurrency criterion. Eight-way parallelism over a
              // small key set, so threads genuinely contend for the same
              // entries rather than each working a private one.
              let cache = privateCache ()
              let bodies = [| for i in 0..7 -> body (string i) |]
              let results = ConcurrentBag<int * string * string>()

              let options = ParallelOptions(MaxDegreeOfParallelism = 8)

              let expandOne (n: int) =
                  let i = n % bodies.Length
                  let prefix = $"ref{n % 3}."
                  let expanded = cache.Expand(bodies[i], prefix, Render.expandFragmentUncached prefix)
                  results.Add(i, prefix, bytes expanded)

              Parallel.For(0, 4096, options, Action<int>(expandOne)) |> ignore

              Expect.equal (results.Count) 4096 "every call returned"

              // Every result must be the expansion of ITS OWN body under ITS OWN
              // prefix — a torn or cross-wired cache shows up here as a body
              // served under the wrong key.
              for (i, prefix, actual) in results do
                  let expected = bytes (Render.expandFragmentUncached prefix bodies[i])
                  Expect.equal actual expected $"body {i} under {prefix} expanded correctly"

              Expect.isTrue (cache.Count <= cache.BodyCapacity) "the cache stayed within its bound"
          }

          // ── B. The routing ───────────────────────────────────────────────
          test "Render.expandFragment namespaces every interior id under the ref prefix" {
              let expanded = Render.expandFragment "ref1." (body "a")

              Expect.equal
                  (ids expanded)
                  [ "ref1.card"; "ref1.title"; "ref1.count" ]
                  "root and both leaves are namespaced"
          }

          test "the global path keeps two bodies of one fragment name apart" {
              // The same soundness claim as the primitive's, but through the
              // function the renderer actually calls — a regression that wired
              // the arm to a name-keyed cache would pass part A and fail here.
              let bodyA = body "A"
              let bodyB = body "B"

              let expandedA = Render.expandFragment "ref1." bodyA
              let expandedB = Render.expandFragment "ref1." bodyB

              Expect.stringContains (bytes expandedA) "Title A" "A's expansion is A's"
              Expect.stringContains (bytes expandedB) "Title B" "B's expansion is B's"

              Expect.equal
                  (bytes (Render.expandFragment "ref1." bodyA))
                  (bytes expandedA)
                  "and A is stable on re-expansion"
          }

          test "the FragmentRef arm routes through the named primitive" {
              // The structural defence. `expandFragment` is output-identical to
              // the bare walk it replaces, so a cleanup pass that inlines it
              // back breaks no behavioural test in this suite — exactly the
              // class Phase 207's vocabulary guard exists for.
              let path =
                  Path.Combine(AppContext.BaseDirectory, "renderer-sources", "client", "Render.fs")

              if not (File.Exists path) then
                  failwithf
                      "renderer source not found at %s — the Fuaran.UI.Tests project copies the renderer sources into its output; a shape scan with no source to scan reports every call site as clean."
                      path

              let source = File.ReadAllText path
              let code = Regex.Replace(source, @"(?m)//.*$", "")

              Expect.isTrue
                  (code.Contains "let namespaced = expandFragment prefix body")
                  "the FragmentRef arm expands through the memo primitive"

              Expect.isFalse
                  (code.Contains "let namespaced = namespaceNode prefix body")
                  "and not through the bare uncached walk"

              // Go-red proof of the detector itself: the same scan over a
              // perturbed copy — the pre-1151 call site restored — must fail
              // both assertions. A guard that cannot be shown to fire is not
              // evidence.
              let reverted =
                  code.Replace(
                      "let namespaced = expandFragment prefix body",
                      "let namespaced = namespaceNode prefix body"
                  )

              Expect.isFalse
                  (reverted.Contains "let namespaced = expandFragment prefix body")
                  "the detector sees the memo call gone in the perturbed copy"

              Expect.isTrue
                  (reverted.Contains "let namespaced = namespaceNode prefix body")
                  "and sees the bare walk restored"
          } ]
