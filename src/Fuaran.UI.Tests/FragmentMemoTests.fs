module Fuaran.UI.Tests.FragmentMemoTests

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Memo
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.Telemetry.Abstractions

// ============================================================================
//  Phase 183 — incremental re-derivation engine + effect-aware memoisation
//  (Fuaran.UI.Memo). Pins the six acceptance criteria: cache hit on identical
//  args; miss on changed args; effecting-bypass; incremental recompute touches
//  only the changed hole-address; replay byte-identity; eviction under bound;
//  plus observability via the cache-stat telemetry channel.
// ============================================================================

/// Boxed-arg helper — mirrors the FragmentApplyTests `Unchecked.nonNull`
/// discipline so the `Map<string, obj>` arg sets type-check under nullable refs.
let private v (x: obj | null) : obj = Unchecked.nonNull x

/// A parameterised card with two value holes (`title`, `count`) + a `content`
/// slot, pure-deterministic. The value holes are not referenced in the body
/// (apply seeds them as `<refId>.<holeName>` bindings) — exactly the Phase 180
/// shape FragmentApplyTests exercises.
let private fragment: ParamFragment<unit> =
    let body =
        Fuaran.dashboard
            "card-root"
            { Defaults.dashboard<unit> with
                Children =
                    [ Fuaran.markdown "card-title" "Title"
                      { Id = "content"
                        Kind = NodeKind.FragmentRef { Name = "content"; Args = None }
                        State = None
                        Style = None
                        Accessibility = None
                        Motion = None
                        ExtraAttributes = None
                        Tooltip = None } ] }

    { Defaults.fragmentDecl with
        Name = "card"
        Holes =
            Some
                [ HoleDecl.Value("title", HoleValueSpace.StringLen(1, 40), None)
                  HoleDecl.Value("count", HoleValueSpace.IntRange(0, 100), None)
                  HoleDecl.Slot("content", None) ]
        Body = body }

/// The SAME fragment name over a DIFFERENT body (Phase 210) — the soundness
/// fixture. A name is not identity: a differently-declared tree can reuse one, so
/// this must never share a structural key, or a cached tree, with `fragment`.
let private sameNameOtherBody: ParamFragment<unit> =
    let body =
        Fuaran.dashboard
            "panel-root"
            { Defaults.dashboard<unit> with
                Children =
                    [ Fuaran.markdown "panel-title" "Other"
                      { Id = "content"
                        Kind = NodeKind.FragmentRef { Name = "content"; Args = None }
                        State = None
                        Style = None
                        Accessibility = None
                        Motion = None
                        ExtraAttributes = None
                        Tooltip = None } ] }

    { fragment with Body = body }

/// A DIFFERENT name over the SAME body object — the converse fixture. The body
/// alone is not identity either, which is why the name is folded into the body
/// digest rather than into the composition around it.
let private otherNameSameBody: ParamFragment<unit> =
    { fragment with Name = "card-alt" }

let private slotArgs (text: string) : Map<string, Node<unit>> =
    Map.ofList [ "content", Fuaran.markdown "body" text ]

let private valueArgs (title: string) (count: int) : Map<string, obj> =
    Map.ofList [ "title", v (box title); "count", v (box count) ]

/// A cache-stat capturing sink — every other channel is a no-op (the engine
/// only emits `RecordCacheStat`).
type private CaptureSink() =
    let stats = ResizeArray<CacheStatTelemetry>()
    member _.Stats: CacheStatTelemetry list = List.ofSeq stats
    member _.Last: CacheStatTelemetry = Seq.last stats

    interface IFuaranTelemetrySink with
        member _.RecordOpApply _ = ()
        member _.RecordDeny _ = ()
        member _.RecordRenderFailure _ = ()
        member _.RecordProviderCall _ = ()
        member _.RecordCacheStat t = stats.Add t
        member _.RecordValidateOutcome _ = ()

let private ok (label: string) (r: Result<Derivation<unit>, string>) : Derivation<unit> =
    match r with
    | Ok d -> d
    | Error e -> failtestf "%s should succeed: %s" label e

[<Tests>]
let tests =
    testList
        "Fragment.Memo (incremental re-derivation)"
        [ test "identical (function, args) → cache hit, byte-identical reused tree (replay-as-reapplication)" {
              let sink = CaptureSink()
              let engine = Engine<unit>(64, sink)

              let d1 =
                  engine.Apply(fragment, "ref1", valueArgs "Hello" 3, slotArgs "x")
                  |> ok "first apply"
              // A second identical apply IS the op-stream-replay path: same key,
              // resolves through the cache.
              let d2 =
                  engine.Apply(fragment, "ref1", valueArgs "Hello" 3, slotArgs "x") |> ok "replay"

              Expect.equal sink.Stats[0].Outcome CacheOutcome.Miss "first application is a miss"
              Expect.equal sink.Stats[1].Outcome CacheOutcome.Hit "the replay is a hit"

              Expect.isTrue
                  (System.Object.ReferenceEquals(d1.Result.Tree, d2.Result.Tree))
                  "the substituted tree object is reused, not re-derived"

              Expect.equal
                  (CanonicalJson.encodeNode d2.Result.Tree)
                  (CanonicalJson.encodeNode d1.Result.Tree)
                  "the replayed tree is byte-identical"
          }

          test "a changed slot argument is a cache miss (structural change)" {
              let sink = CaptureSink()
              let engine = Engine<unit>(64, sink)

              engine.Apply(fragment, "ref1", valueArgs "Hello" 3, slotArgs "x")
              |> ok "first"
              |> ignore
              // Different slot subtree → different structural key → miss.
              engine.Apply(fragment, "ref1", valueArgs "Hello" 3, slotArgs "y")
              |> ok "second"
              |> ignore

              Expect.equal sink.Stats[0].Outcome CacheOutcome.Miss "first is a miss"
              Expect.equal sink.Stats[1].Outcome CacheOutcome.Miss "changed slot arg is a fresh miss"
          }

          test "an effecting function is never cached (bypass)" {
              let sink = CaptureSink()
              let engine = Engine<unit>(64, sink)

              let effecting =
                  { fragment with
                      Effect =
                          Some
                              { HostEffect = HostEffect.ReadsHost
                                Determinism = DeterminismSource.Clock } }

              engine.Apply(effecting, "ref1", valueArgs "Hello" 3, slotArgs "x")
              |> ok "first"
              |> ignore

              engine.Apply(effecting, "ref1", valueArgs "Hello" 3, slotArgs "x")
              |> ok "second"
              |> ignore

              Expect.equal sink.Stats[0].Outcome CacheOutcome.Bypass "effecting application bypasses the cache"
              Expect.equal sink.Stats[1].Outcome CacheOutcome.Bypass "and bypasses on every repeat"
              Expect.equal engine.Cache.Count 0 "nothing is ever stored for an effecting fragment"
          }

          test "incremental: a value-parameter change reuses the tree + recomputes only the changed hole-address" {
              let sink = CaptureSink()
              let engine = Engine<unit>(64, sink)

              let d0 =
                  engine.Apply(fragment, "ref1", valueArgs "Hello" 3, slotArgs "x")
                  |> ok "initial"
              // Only `count` changes (3 → 7); `title` + the slot are unchanged.
              let d1 =
                  engine.Reapply(d0, fragment, "ref1", valueArgs "Hello" 7, slotArgs "x")
                  |> ok "reapply"

              Expect.equal sink.Last.Outcome (CacheOutcome.Incremental 1) "exactly one hole-address recomputed"

              Expect.isTrue
                  (System.Object.ReferenceEquals(d0.Result.Tree, d1.Result.Tree))
                  "the structural tree is reused (a value change never restructures it)"

              Expect.isTrue
                  (System.Object.ReferenceEquals(
                      d0.Result.ValueBindings["ref1.title"],
                      d1.Result.ValueBindings["ref1.title"]
                  ))
                  "the unchanged 'title' binding object is reused, not recomputed"

              Expect.equal d1.Result.ValueBindings["ref1.count"] (v (box 7)) "the changed 'count' binding is updated"
          }

          test "disjoint value changes don't invalidate each other" {
              let sink = CaptureSink()
              let engine = Engine<unit>(64, sink)

              let d0 =
                  engine.Apply(fragment, "ref1", valueArgs "Hello" 3, slotArgs "x")
                  |> ok "initial"
              // Change ONLY title this time; count must stay reference-stable.
              let d1 =
                  engine.Reapply(d0, fragment, "ref1", valueArgs "Goodbye" 3, slotArgs "x")
                  |> ok "reapply"

              Expect.equal sink.Last.Outcome (CacheOutcome.Incremental 1) "only the title hole recomputed"

              Expect.isTrue
                  (System.Object.ReferenceEquals(
                      d0.Result.ValueBindings["ref1.count"],
                      d1.Result.ValueBindings["ref1.count"]
                  ))
                  "the disjoint 'count' binding is untouched"

              Expect.equal
                  d1.Result.ValueBindings["ref1.title"]
                  (v (box "Goodbye"))
                  "the changed 'title' binding is updated"
          }

          test "the cache is bounded — LRU eviction once capacity is exceeded" {
              let sink = CaptureSink()
              let engine = Engine<unit>(2, sink)
              // Three distinct structures (distinct slot subtrees).
              engine.Apply(fragment, "ref1", valueArgs "Hello" 3, slotArgs "a")
              |> ok "A"
              |> ignore

              engine.Apply(fragment, "ref1", valueArgs "Hello" 3, slotArgs "b")
              |> ok "B"
              |> ignore

              engine.Apply(fragment, "ref1", valueArgs "Hello" 3, slotArgs "c")
              |> ok "C"
              |> ignore

              Expect.equal engine.Cache.Count 2 "the cache never exceeds its bound"
              Expect.equal engine.Cache.Capacity 2 "the bound is observable"

              // A was the least-recently-used → evicted; re-deriving it is a miss.
              engine.Apply(fragment, "ref1", valueArgs "Hello" 3, slotArgs "a")
              |> ok "A again"
              |> ignore

              Expect.equal sink.Last.Outcome CacheOutcome.Miss "the evicted entry re-derives (LRU eviction confirmed)"
          }

          test "cache stats are observable — name, outcome, size + capacity surface on every call" {
              let sink = CaptureSink()
              let engine = Engine<unit>(8, sink, "music.chart")

              engine.Apply(fragment, "ref1", valueArgs "Hello" 3, slotArgs "x")
              |> ok "apply"
              |> ignore

              let rec0 = sink.Last
              Expect.equal rec0.CacheName "music.chart" "the engine's cache name tags the record"
              Expect.equal rec0.Capacity 8 "the bound is surfaced"
              Expect.equal rec0.Size 1 "the current size is surfaced"
              Expect.equal (CacheOutcome.name rec0.Outcome) "miss" "the outcome classification is surfaced"
          }

          // ==================================================================
          //  Phase 210 — the structural key's body half is memoised per
          //  `ParamFragment` reference, so a probe hashes only the ref id + slot
          //  args. Behaviour-preserving: every assertion below is about COST or
          //  about the key discriminating exactly what it discriminated before.
          // ==================================================================

          test "the key splits faithfully — body digest ⊕ slot args = the whole-fragment key" {
              let slots = slotArgs "x"

              Expect.equal
                  (FragmentKey.structuralOf (FragmentKey.bodyDigest fragment) "ref1" slots)
                  (FragmentKey.structural fragment "ref1" slots)
                  "composing from a precomputed body digest yields the same key as the one-call form"
          }

          test "the key still discriminates body, ref id and slot args" {
              let slots = slotArgs "x"
              let k = FragmentKey.structural fragment "ref1" slots

              Expect.notEqual (FragmentKey.structural fragment "ref2" slots) k "a different ref site keys apart"

              Expect.notEqual
                  (FragmentKey.structural fragment "ref1" (slotArgs "y"))
                  k
                  "a different slot argument keys apart"

              // The soundness case: the SAME fragment name with a DIFFERENT body
              // must never share a key. The name alone is not identity — a
              // differently-declared tree can reuse it.
              Expect.notEqual
                  (FragmentKey.structural sameNameOtherBody "ref1" slots)
                  k
                  "the same name with a different body keys apart"

              // And the name is not redundant either: two declarations sharing one
              // body object still key apart.
              Expect.notEqual
                  (FragmentKey.structural otherNameSameBody "ref1" slots)
                  k
                  "a different name over the same body keys apart"
          }

          test "BoundedRefMemo computes once per REFERENCE, not once per value" {
              let memo = FragmentMemo.BoundedRefMemo<ParamFragment<unit>, string>(8)
              let mutable computed = 0

              let digest (pf: ParamFragment<unit>) =
                  computed <- computed + 1
                  FragmentKey.bodyDigest pf

              let a = fragment
              // A record copy — structurally EQUAL to `a`, and a distinct object.
              let b = { fragment with Name = fragment.Name }

              let ka = memo.GetOrAdd(a, digest)
              let ka2 = memo.GetOrAdd(a, digest)
              let kb = memo.GetOrAdd(b, digest)

              Expect.equal computed 2 "the same reference computes once; a distinct object computes again"
              Expect.equal memo.Hits 1 "the repeat lookup is a hit"
              Expect.equal memo.Misses 2 "each distinct reference is a miss"
              Expect.equal ka ka2 "the memoised digest is returned unchanged"
              Expect.equal kb ka "a structurally-identical fragment digests identically (content-addressed)"
          }

          test "BoundedRefMemo is bounded — least-recently-used slots are evicted" {
              let memo = FragmentMemo.BoundedRefMemo<ParamFragment<unit>, string>(2)
              let mutable computed = 0

              let digest (pf: ParamFragment<unit>) =
                  computed <- computed + 1
                  FragmentKey.bodyDigest pf

              let a = { fragment with Name = "a" }
              let b = { fragment with Name = "b" }
              let c = { fragment with Name = "c" }

              memo.GetOrAdd(a, digest) |> ignore
              memo.GetOrAdd(b, digest) |> ignore
              // Touch `a` so `b` becomes the least-recently-used slot.
              memo.GetOrAdd(a, digest) |> ignore
              memo.GetOrAdd(c, digest) |> ignore

              Expect.equal memo.Count 2 "the memo never exceeds its bound"
              Expect.equal memo.Capacity 2 "the bound is observable"
              Expect.isTrue (memo.ContainsKey a) "the recently-used slot survives"
              Expect.isFalse (memo.ContainsKey b) "the least-recently-used slot was evicted"
              Expect.equal computed 3 "an evicted key simply recomputes — never a wrong digest"
          }

          test "a repeated apply digests the fragment body once, not once per probe" {
              let sink = CaptureSink()
              let engine = Engine<unit>(64, sink)

              for _ in 1..4 do
                  engine.Apply(fragment, "ref1", valueArgs "Hello" 3, slotArgs "x")
                  |> ok "apply"
                  |> ignore

              Expect.equal engine.KeyMemo.Misses 1 "the body is digested exactly once for the fragment"
              Expect.equal engine.KeyMemo.Hits 3 "every later probe reuses the digest instead of re-hashing the tree"
              Expect.equal sink.Last.Outcome CacheOutcome.Hit "the tree cache still hits — behaviour is unchanged"
          }

          test "a value-only reapply re-hashes only the slot args" {
              let sink = CaptureSink()
              let engine = Engine<unit>(64, sink)

              let d0 =
                  engine.Apply(fragment, "ref1", valueArgs "Hello" 3, slotArgs "x")
                  |> ok "base apply"

              let d1 =
                  engine.Reapply(d0, fragment, "ref1", valueArgs "Hello" 7, slotArgs "x")
                  |> ok "value-only reapply"

              Expect.equal engine.KeyMemo.Hits 1 "the reapply's key reuses the memoised body digest"
              Expect.equal engine.KeyMemo.Misses 1 "the body is never re-digested for the same fragment"
              Expect.equal d1.StructuralKey d0.StructuralKey "a value-only edit leaves the structural key unchanged"

              Expect.isTrue
                  (System.Object.ReferenceEquals(d0.Result.Tree, d1.Result.Tree))
                  "and the tree is still reused reference-identically"
          }

          test "a same-named fragment with a different body is never served the cached tree" {
              let sink = CaptureSink()
              let engine = Engine<unit>(64, sink)

              let d0 =
                  engine.Apply(fragment, "ref1", valueArgs "Hello" 3, slotArgs "x")
                  |> ok "first declaration"

              let d1 =
                  engine.Apply(sameNameOtherBody, "ref1", valueArgs "Hello" 3, slotArgs "x")
                  |> ok "second declaration, same name"

              Expect.notEqual d1.StructuralKey d0.StructuralKey "the two declarations key apart"
              Expect.equal sink.Last.Outcome CacheOutcome.Miss "the differently-bodied declaration re-derives"

              Expect.notEqual
                  (CanonicalJson.encodeNode d1.Result.Tree)
                  (CanonicalJson.encodeNode d0.Result.Tree)
                  "and it yields its OWN body, not the cached tree"
          } ]
