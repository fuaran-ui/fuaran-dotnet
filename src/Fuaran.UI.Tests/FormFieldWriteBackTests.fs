module Fuaran.UI.Tests.FormFieldWriteBack

// ============================================================================
//  Range / DateRange form-field write-back regression (Phase 725 follow-up).
//
//  A handler-free (declarative / AI-authored) pair field's change must WRITE
//  the new pair to its own value slot. The regressed `FormFieldKind.Range`
//  shape passed `None` as the write-back payload, and `None` is the write-back
//  CLEAR: every change on a `Range` bound to `Binding.State` erased the
//  field's own `$state` slot instead of storing the pair. `DateRange` never
//  had the defect — it was authored (Phase 725) passing the pair explicitly.
//
//  These tests pin `Render.pairFieldChange`, the module-level dispatch both
//  `Range` inputs and both `DateRange` inputs drive (the same
//  .NET-pins-the-exact-code-path shape as `Render.applyDispatchGate`).
//
//  Note the payload is the pair RECORD (`RangePair` / `DateRangePair`), not a
//  tuple: that is what `BindingResolver.tryResolve` reads back out of the
//  slot. The `onChange` closure still receives a tuple — the two shapes are
//  deliberately different since the 692-694 swap, and writing a tuple into the
//  slot would resolve back as a miss and silently reset the control.
// ============================================================================

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Renderer

type private Msg = PairChanged of float * float

// F# 10 `box _` types as `obj | null`; the store surfaces take a non-null
// `obj`. Same `nn` workaround the other test modules use.
let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

let private makeCtx () : Render.RenderContext<Msg> =
    { Sources = BindingResolver.empty
      Runtime = Runtime.diagnostic
      VisAdapter = VisAdapter.noOp<Msg>
      Dispatch = ignore
      TelemetrySink = None
      InErrorBoundary = false
      Fragments = Map.empty
      ExpandingFragments = Set.empty
      Scope = None
      SessionContext = Map.empty
      // Phase 889 — recording off, the default at every shipped entry point.
      ActionSink = None
      CurrentNodeId = None
      // Phase 1026 — hand-built test tree; the scheme floor is what these
      // cases exercise, so the policy is deliberately not the variable.
      EgressPolicy = Sanitize.permissiveEgress }

// Annotated constructors, not bare record literals: `{ Min = _; Max = _ }`
// otherwise infers as `NumberFieldConstraints` (same labels, `float option`
// fields), and `DateRangePair`'s labels need the annotation to resolve through
// the `Generated` abbreviation — exactly the form Render.fs uses at the call
// sites.
let private rp (minV: float) (maxV: float) : RangePair = { Min = minV; Max = maxV }

let private dp (fromV: string) (toV: string) : DateRangePair = { From = fromV; To = toV }

let private rangeBinding (key: string) : Binding<RangePair> = Binding.State(key, Some(rp 0.0 0.0))

// `StateStore` / `FilterStore` are process-wide singletons; Expecto
// parallelises test lists by default, so these run sequentially relative to
// each other, use distinct keys, and clean up what they write.
[<Tests>]
let tests =
    testSequenced
    <| testList
        "Range form-field write-back (declarative pair fields)"
        [ test "declarative Range change stores the new pair (regression: was a clear)" {
              let binding = rangeBinding "ffwb-seeded"
              StateStore.set "ffwb-seeded" (nn (rp 1.0 5.0))

              try
                  Render.pairFieldChange (makeCtx ()) None binding (nn (rp 3.0 5.0)) (3.0, 5.0)

                  Expect.equal
                      (StateStore.get "ffwb-seeded")
                      (Some(nn (rp 3.0 5.0)))
                      "the slot holds the NEW pair — a `None` payload here would have ERASED it"
              finally
                  StateStore.remove "ffwb-seeded"
          }

          test "declarative Range change writes an unseeded slot too" {
              let binding = rangeBinding "ffwb-unseeded"

              try
                  Render.pairFieldChange (makeCtx ()) None binding (nn (rp 2.0 8.0)) (2.0, 8.0)

                  Expect.equal
                      (StateStore.get "ffwb-unseeded")
                      (Some(nn (rp 2.0 8.0)))
                      "first change lands the pair in the previously-empty slot"
              finally
                  StateStore.remove "ffwb-unseeded"
          }

          test "the stored pair resolves back through the binding (shape round-trip)" {
              // The defect this guards is subtler than the clear: writing a
              // TUPLE would store fine and then fail to resolve, silently
              // resetting the control to its default on the next render.
              let binding = rangeBinding "ffwb-roundtrip"

              try
                  Render.pairFieldChange (makeCtx ()) None binding (nn (rp 7.0 9.0)) (7.0, 9.0)

                  // The renderer re-reads state per render by merging
                  // `StateStore.snapshot ()` into `Sources.State` (the
                  // `withLiveState` path); resolving against a stale `empty`
                  // would only ever see the binding default.
                  let sources =
                      { BindingResolver.empty with
                          State = StateStore.snapshot () }

                  let resolved: RangePair option = BindingResolver.tryResolve sources binding

                  Expect.equal resolved (Some(rp 7.0 9.0)) "the written slot resolves back as a RangePair, not a miss"
              finally
                  StateStore.remove "ffwb-roundtrip"
          }

          test "declarative DateRange change stores the date pair" {
              let binding: Binding<DateRangePair> = Binding.State("ffwb-dates", Some(dp "" ""))

              try
                  Render.pairFieldChange
                      (makeCtx ())
                      None
                      binding
                      (nn (dp "2026-01-01" "2026-12-31"))
                      ("2026-01-01", "2026-12-31")

                  Expect.equal
                      (StateStore.get "ffwb-dates")
                      (Some(nn (dp "2026-01-01" "2026-12-31")))
                      "the same dispatch covers DateRange's pair record"
              finally
                  StateStore.remove "ffwb-dates"
          }

          test "declarative Range change on a Filter binding writes the FilterStore" {
              let binding: Binding<RangePair> = Binding.Filter("ffwb-filter", None)

              try
                  Render.pairFieldChange (makeCtx ()) None binding (nn (rp 10.0 20.0)) (10.0, 20.0)

                  Expect.equal
                      (FilterStore.get "ffwb-filter")
                      (Some(nn (rp 10.0 20.0)))
                      "a Filter-bound pair field writes its filter slot"
              finally
                  FilterStore.clear "ffwb-filter"
          }

          test "a present handler wins: the pair dispatches and no store is written" {
              let binding = rangeBinding "ffwb-handled"
              let mutable dispatched = None

              let ctx =
                  { makeCtx () with
                      Dispatch = fun msg -> dispatched <- Some msg }

              try
                  Render.pairFieldChange
                      ctx
                      (Some(fun pair -> Action.Dispatch(PairChanged pair)))
                      binding
                      (nn (rp 4.0 6.0))
                      (4.0, 6.0)

                  Expect.equal dispatched (Some(PairChanged(4.0, 6.0))) "the closure receives the whole pair"
                  Expect.equal (StateStore.get "ffwb-handled") None "the store is untouched when a handler is present"
              finally
                  StateStore.remove "ffwb-handled"
          } ]
