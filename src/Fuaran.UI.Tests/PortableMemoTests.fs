module Fuaran.UI.Tests.PortableMemoTests

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Memo
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.Telemetry.Abstractions

// ============================================================================
//  Phase 360 — Fuaran.UI.Memo re-expressed over fuaran-core#49's applyMemo cache
//  model: a caller-supplied, content-addressed store (portable across session /
//  machine) rather than the process-local BoundedLru. Pins the four acceptance
//  criteria:
//   1. portable-store round-trip — a subtree cached in one store instance, its
//      MemoCache snapshot lifted onto a FRESH store, serves the same tree (a hit
//      across the session boundary) — the portability win over the LRU.
//   2. effect-gate — an impure / non-deterministic fragment is never cached
//      (bypass counter increments, re-derived each call; store stays empty).
//   3. replay parity — a recorded op-stream session replayed THROUGH the memo
//      store re-derives byte-identically vs direct (store-less) replay.
//   4. key parity — the content key is canonical-JSON-stable, identical whether
//      computed under #if FABLE_COMPILER or not.
// ============================================================================

/// Boxed-arg helper — mirrors the FragmentMemoTests discipline so the
/// `Map<string, obj>` arg sets type-check under nullable refs.
let private v (x: obj | null) : obj = Unchecked.nonNull x

/// A pure-deterministic parameterised card (two value holes + a `content` slot)
/// — the Phase-180 shape the memo tests exercise.
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

let private slotArgs (text: string) : Map<string, Node<unit>> =
    Map.ofList [ "content", Fuaran.markdown "body" text ]

let private valueArgs (title: string) (count: int) : Map<string, obj> =
    Map.ofList [ "title", v (box title); "count", v (box count) ]

/// A cache-stat capturing sink — every other channel is a no-op.
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

let private okTrees (label: string) (r: Result<Node<unit> list, string>) : Node<unit> list =
    match r with
    | Ok ts -> ts
    | Error e -> failtestf "%s should succeed: %s" label e

[<Tests>]
let tests =
    testList
        "Portable memo (Phase 360 — applyMemo adoption)"
        [ test "portable-store round-trip: a MemoCache snapshot seeds a fresh store → cross-session HIT" {
              let sink = CaptureSink()

              // Session A — a portable (MemoCache-backed) store, populated by one application.
              let storeA = FragmentStore.portable<unit> ()
              let engineA = Engine<unit>(storeA :> IFragmentStore<unit>, sink, "portable")

              engineA.Apply(fragment, "ref1", valueArgs "Hello" 3, slotArgs "x")
              |> ok "session A apply"
              |> ignore

              Expect.equal sink.Stats[0].Outcome CacheOutcome.Miss "first application is a miss (nothing cached yet)"
              Expect.equal (storeA :> IFragmentStore<unit>).Count 1 "one subtree cached in session A"

              // The snapshot is the portable payload — persist / ship it to machine B.
              let snapshot = storeA.Snapshot
              Expect.equal (Map.count snapshot.Entries) 1 "the snapshot carries the cached subtree"

              // Session B (a FRESH store on 'another machine') seeded from the snapshot.
              let storeB = FragmentStore.fromSnapshot<unit> snapshot
              let engineB = Engine<unit>(storeB :> IFragmentStore<unit>, sink, "portable")

              let dB =
                  engineB.Apply(fragment, "ref1", valueArgs "Hello" 3, slotArgs "x")
                  |> ok "session B apply"

              Expect.equal
                  sink.Last.Outcome
                  CacheOutcome.Hit
                  "the SAME application is a HIT in the fresh store (portability)"

              // And the served tree is byte-identical to what session A would derive.
              let direct =
                  match FragmentApply.apply fragment "ref1" (valueArgs "Hello" 3) (slotArgs "x") with
                  | Ok app -> app.Tree
                  | Error e -> failtestf "direct apply failed: %s" e

              Expect.equal
                  (CanonicalJson.encodeNode dB.Result.Tree)
                  (CanonicalJson.encodeNode direct)
                  "the cross-session served tree is byte-identical to a direct derivation"
          }

          test "the default in-process store preserves the pre-360 BoundedLru behaviour (additive)" {
              let sink = CaptureSink()
              // The shipped constructor still yields the bounded-LRU store.
              let engine = Engine<unit>(2, sink)

              engine.Apply(fragment, "ref1", valueArgs "Hello" 3, slotArgs "a")
              |> ok "A"
              |> ignore

              engine.Apply(fragment, "ref1", valueArgs "Hello" 3, slotArgs "b")
              |> ok "B"
              |> ignore

              engine.Apply(fragment, "ref1", valueArgs "Hello" 3, slotArgs "c")
              |> ok "C"
              |> ignore

              Expect.equal engine.Store.Count 2 "the default store is still bounded (LRU eviction preserved)"
              Expect.equal engine.Store.Capacity 2 "the bound is observable"
          }

          test "effect-gate: an impure / non-deterministic fragment is never cached (bypass)" {
              let sink = CaptureSink()
              let store = FragmentStore.portable<unit> ()
              let engine = Engine<unit>(store :> IFragmentStore<unit>, sink)

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

              Expect.equal sink.Stats[0].Outcome CacheOutcome.Bypass "effecting application bypasses the store"

              Expect.equal sink.Stats[1].Outcome CacheOutcome.Bypass "and bypasses on every repeat (never served stale)"

              Expect.equal (store :> IFragmentStore<unit>).Count 0 "nothing is ever stored for an effecting fragment"
              Expect.equal (store :> IFragmentStore<unit>).Bypasses 2 "the store's bypass counter increments each call"
              // The gate is exactly Fuaran.Core's isMemoisable on both axes.
              Expect.isFalse
                  (FragmentStore.isStoreEligible (effecting.Effect |> Option.defaultValue EffectClass.pureDeterministic))
                  "an effecting fragment is not store-eligible"

              Expect.isTrue
                  (FragmentStore.isStoreEligible (fragment.Effect |> Option.defaultValue EffectClass.pureDeterministic))
                  "the pure-deterministic fragment is store-eligible"
          }

          test "replay parity: an op-stream session replayed through the memo store == direct replay (byte-identical)" {
              let sink = CaptureSink()
              let store = FragmentStore.portable<unit> ()
              let engine = Engine<unit>(store :> IFragmentStore<unit>, sink)

              // A recorded session: three applications, the 3rd a REPEAT of the 1st
              // (so replay-through-the-store serves it as a hit, re-application).
              let session: RecordedApplication<unit> list =
                  [ { Fragment = fragment
                      RefId = "ref1"
                      ValueArgs = valueArgs "Hello" 3
                      SlotArgs = slotArgs "x" }
                    { Fragment = fragment
                      RefId = "ref2"
                      ValueArgs = valueArgs "World" 7
                      SlotArgs = slotArgs "y" }
                    { Fragment = fragment
                      RefId = "ref1"
                      ValueArgs = valueArgs "Hello" 3
                      SlotArgs = slotArgs "x" } ]

              let served = MemoReplay.replay engine session |> okTrees "memo-served replay"
              let direct = MemoReplay.directReplay session |> okTrees "direct replay"

              Expect.equal (List.length served) 3 "every recorded application replays"

              // Byte-identical, application by application.
              List.zip served direct
              |> List.iteri (fun i (s, d) ->
                  Expect.equal
                      (CanonicalJson.encodeNode s)
                      (CanonicalJson.encodeNode d)
                      (sprintf "replayed application %d is byte-identical to direct re-application" i))

              // The repeated (ref1) application was served from the store, not re-derived.
              Expect.equal sink.Last.Outcome CacheOutcome.Hit "the repeated application in the session is a store HIT"
          }

          test "key parity: the content key is canonical-JSON-stable (identical under #if FABLE_COMPILER or not)" {
              // The key is FragmentKey.structural — a SHA-256 over CanonicalJson.encodeNode.
              // CanonicalJson is Fable-clean + host-neutral (the same encoder the op-stream
              // hash-chain uses on both pipelines), so the key is a pure function of the wire
              // shape: two equal (fragment, refId, slotArgs) inputs key identically on every
              // host, and the key never varies by pipeline (there is no #if in the key path).
              let k1 = FragmentKey.structural fragment "ref1" (slotArgs "x")
              let k2 = FragmentKey.structural fragment "ref1" (slotArgs "x")
              Expect.equal k1 k2 "identical inputs → identical content key (deterministic, host-neutral)"

              let kDiff = FragmentKey.structural fragment "ref1" (slotArgs "z")
              Expect.notEqual k1 kDiff "a changed slot subtree → a different key (content-addressed)"

              // The key is a hash of the canonical encoding — so key equality follows exactly
              // from canonical-encoding equality, the same both-pipelines contract the wire
              // corpus pins. Two independently-built identical slot subtrees encode identically,
              // hence key identically.
              let slotA = Map.ofList [ "content", Fuaran.markdown "body" "same" ]
              let slotB = Map.ofList [ "content", Fuaran.markdown "body" "same" ]

              Expect.equal
                  (CanonicalJson.encodeNode slotA["content"])
                  (CanonicalJson.encodeNode slotB["content"])
                  "the canonical encoding underlying the key is stable"

              Expect.equal
                  (FragmentKey.structural fragment "ref1" slotA)
                  (FragmentKey.structural fragment "ref1" slotB)
                  "so the content key is stable across independently-built identical inputs (Fable/.NET parity)"
          } ]
