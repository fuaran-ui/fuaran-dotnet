module Fuaran.UI.Tests.FormFieldWriteBackHarness

// ============================================================================
//  Phase 1141 — the render-level form-field write-back harness.
//
//  WHAT WAS UNCOVERED, and why a passing suite did not say so. Every client
//  control's write-back shared one shape: a correct dispatch fed by a payload
//  hand-built at the call site. `FormFieldWriteBackTests` pins the DISPATCH
//  (`Render.pairFieldChange`) by handing it a payload the test builds itself —
//  so a call site that boxed the wrong record, put the new value in the wrong
//  slot, or passed the write-back CLEAR would leave that suite entirely green.
//  And two whole re-implementations of the same dispatch were unreachable from
//  any test BY CONSTRUCTION: `renderFormField`'s local `handle` (9 call sites)
//  and `renderFilterSpec`'s eight locals (11 call sites) — 20 of the client
//  renderer's 24 write-back call sites, on the same risk with none of the
//  coverage.
//
//  WHY THE HARNESS IS SHAPED THIS WAY. Feliz's .NET-side `ReactElement` is
//  opaque — the constraint `RenderEntrySeamTests` / `AccessibilityTests` /
//  `ErrorBoundaryTests` / `StateStoreScopingTests` each document — so a
//  `prop.onChange` handler cannot be pulled off a rendered tree and fired, and
//  a literal DOM-driven harness is not constructible on this pipeline. What IS
//  reachable is the closure the rendered control CARRIES, provided the site
//  builds it through a named module-level function rather than inline over a
//  local. Phase 1141 made that true: the render sites now obtain their change
//  closures from `Render.rangeInputHandlers` / `Render.dateRangeInputHandlers`
//  and their scalar dispatch from `Render.fieldChange`. The cases below drive
//  exactly those closures — the same objects the inputs receive, minus React's
//  event plumbing.
//
//  GO-RED. Each site class states its contract as a PREDICATE, then runs it
//  twice: once against the real construction (must hold) and once against a
//  deliberately perturbed one (must not). Three perturbation classes, each a
//  defect the shipped call sites could silently have carried — the slot swap,
//  the `None` payload (the historical `Range` clear regression), and boxing the
//  tuple instead of the record (a write that stores fine and then resolves as a
//  miss, silently resetting the control on the next render). A one-sided
//  assertion cannot tell a working harness from an inert one.
//
//  The census at the end pins the unification itself: a later local
//  re-implementation inside either render function puts those call sites back
//  out of reach, and no behavioural test can see that happen.
// ============================================================================

open System
open System.IO
open System.Text.RegularExpressions
open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Renderer

type private Msg =
    | RangeChanged of float * float
    | DatesChanged of string * string
    | TextChanged of string

// F# 10 `box _` types as `obj | null`; the store surfaces take a non-null
// `obj`. Same `nn` workaround the other test modules use.
let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

// Annotated constructors, not bare record literals: `{ Min = _; Max = _ }`
// otherwise infers as `NumberFieldConstraints` (same labels, `float option`
// fields), and `DateRangePair`'s labels need the annotation to resolve through
// the `Generated` abbreviation — exactly the form Render.fs uses.
let private rp (minV: float) (maxV: float) : RangePair = { Min = minV; Max = maxV }

let private dp (fromV: string) (toV: string) : DateRangePair = { From = fromV; To = toV }

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
      ActionSink = None
      CurrentNodeId = None
      EgressPolicy = Sanitize.permissiveEgress
      // Phase 1117 — no upload sink: this surface performs no uploads.
      UploadSink = None }

// ─── Site classes ──────────────────────────────────────────────────────────
//
// The four pair-control site classes, each named by the render arm it stands
// for and carrying the WRITE binding that arm passes. A form field writes its
// own value slot (`Binding.State` in the auto-bind form); a filter chip writes
// `Binding.Filter(spec.Name, None)` — the destination its declarative write has
// always had.

/// Read a slot back the way the renderer does: it re-reads state per render by
/// merging the live snapshot into `Sources`, so resolving against a stale
/// `empty` would only ever see the binding default.
let private resolveState (binding: Binding<'T>) : 'T option =
    BindingResolver.tryResolve
        { BindingResolver.empty with
            State = StateStore.snapshot () }
        binding

let private resolveFilter (binding: Binding<'T>) : 'T option =
    BindingResolver.tryResolve
        { BindingResolver.empty with
            Filters = FilterStore.snapshot () }
        binding

// ─── The numeric-pair contract ─────────────────────────────────────────────

/// What a rendered numeric-pair control's two closures must do, stated once so
/// it can be run against a perturbed construction as well as the real one:
/// driving the MIN input to `v` must leave `{ Min = v; Max = <untouched> }` in
/// the slot, resolvable as a `RangePair`; driving the MAX input must be the
/// mirror image. `current` is what the control rendered from.
let private rangeContractHolds
    (readBack: unit -> RangePair option)
    (current: RangePair)
    (handlers: (float -> unit) * (float -> unit))
    : bool =
    let onMin, onMax = handlers

    onMin 3.5
    let afterMin = readBack ()

    onMax 9.25
    let afterMax = readBack ()

    // The max drive rebuilds from the SAME rendered `current`, not from what
    // the min drive stored — a real control re-renders between the two — so the
    // expected second value carries `current.Min`, not 3.5.
    afterMin = Some(rp 3.5 current.Max) && afterMax = Some(rp current.Min 9.25)

let private dateRangeContractHolds
    (readBack: unit -> DateRangePair option)
    (current: DateRangePair)
    (handlers: (string -> unit) * (string -> unit))
    : bool =
    let onFrom, onTo = handlers

    onFrom "2026-02-01"
    let afterFrom = readBack ()

    onTo "2026-11-30"
    let afterTo = readBack ()

    afterFrom = Some(dp "2026-02-01" current.To)
    && afterTo = Some(dp current.From "2026-11-30")

// ─── The three perturbations (the go-red constructions) ────────────────────

/// Perturbation A — the slots are swapped: each input writes its value into the
/// OTHER end of the pair. Every payload is a well-formed `RangePair` and every
/// write lands, so nothing but a slot-aware assertion can see it.
let private swappedSlotHandlers
    (ctx: Render.RenderContext<Msg>)
    (binding: Binding<'T>)
    (current: RangePair)
    : (float -> unit) * (float -> unit) =
    let write (pair: RangePair) =
        Render.pairFieldChange ctx None binding (nn pair) (pair.Min, pair.Max)

    (fun v -> write (rp current.Min v)), (fun v -> write (rp v current.Max))

/// Perturbation B — the historical `Range` clear regression: the write-back
/// payload is `None`, which `writeBackTo` reads as CLEAR, so every change
/// ERASES the slot instead of storing the new pair. Note this construction has
/// to reach past `pairFieldChange` to `fieldChange`: the pair dispatch's plain
/// `obj` payload makes the defect unrepresentable, which is exactly the claim
/// its doc comment makes and this case is the evidence for.
let private clearingHandlers
    (ctx: Render.RenderContext<Msg>)
    (binding: Binding<'T>)
    (current: RangePair)
    : (float -> unit) * (float -> unit) =
    (fun v -> Render.fieldChange ctx None binding None (v, current.Max)),
    (fun v -> Render.fieldChange ctx None binding None (current.Min, v))

/// Perturbation C — the payload is the TUPLE rather than the pair record. The
/// write succeeds and the slot is non-empty, so a "did anything get written"
/// assertion passes; the value then fails to resolve as a `RangePair`, which in
/// a real render silently resets the control to its default.
let private tupleBoxingHandlers
    (ctx: Render.RenderContext<Msg>)
    (binding: Binding<'T>)
    (current: RangePair)
    : (float -> unit) * (float -> unit) =
    let write (pair: float * float) =
        Render.pairFieldChange ctx None binding (nn pair) pair

    (fun v -> write (v, current.Max)), (fun v -> write (current.Min, v))

// ─── Cases ─────────────────────────────────────────────────────────────────
//
// `StateStore` / `FilterStore` are process-wide singletons; these run
// sequentially relative to each other, use distinct keys, and clean up.

let private stateKey (suffix: string) = "ffwbh-" + suffix

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Render-level form-field write-back harness (Phase 1141)"
        [

          // ── Site class 1: the form field's `Range` arm ────────────────────
          test "form Range: each input writes its own slot and preserves the other" {
              let key = stateKey "form-range"
              let binding: Binding<RangePair> = Binding.State(key, Some(rp 0.0 0.0))
              let current = rp 1.0 8.0

              try
                  Expect.isTrue
                      (rangeContractHolds
                          (fun () -> resolveState binding)
                          current
                          (Render.rangeInputHandlers (makeCtx ()) None binding current))
                      "the closures the rendered min/max inputs carry write the pair slot-correctly"
              finally
                  StateStore.remove key
          }

          test "form Range go-red: each perturbed construction fails the same contract" {
              let current = rp 1.0 8.0

              let runAgainst (suffix: string) build =
                  let key = stateKey suffix
                  let binding: Binding<RangePair> = Binding.State(key, Some(rp 0.0 0.0))

                  try
                      rangeContractHolds (fun () -> resolveState binding) current (build (makeCtx ()) binding current)
                  finally
                      StateStore.remove key

              Expect.isFalse
                  (runAgainst "gr-swap" swappedSlotHandlers)
                  "A — swapped slots: every write is a well-formed pair, so only a slot-aware contract catches it"

              Expect.isFalse
                  (runAgainst "gr-clear" clearingHandlers)
                  "B — the `None` payload is the write-back CLEAR (the historical Range regression)"

              Expect.isFalse
                  (runAgainst "gr-tuple" tupleBoxingHandlers)
                  "C — boxing the tuple stores fine and then resolves as a miss"
          }

          // ── Site class 2: the form field's `DateRange` arm ────────────────
          test "form DateRange: each input writes its own end of the date pair" {
              let key = stateKey "form-dates"
              let binding: Binding<DateRangePair> = Binding.State(key, Some(dp "" ""))
              let current = dp "2026-01-01" "2026-12-31"

              try
                  Expect.isTrue
                      (dateRangeContractHolds
                          (fun () -> resolveState binding)
                          current
                          (Render.dateRangeInputHandlers (makeCtx ()) None binding current))
                      "the date pair's two closures are the numeric pair's shape over DateRangePair"
              finally
                  StateStore.remove key
          }

          test "form DateRange go-red: swapped ends fail the contract" {
              let key = stateKey "gr-dates"
              let binding: Binding<DateRangePair> = Binding.State(key, Some(dp "" ""))
              let current = dp "2026-01-01" "2026-12-31"
              let ctx = makeCtx ()

              let write (pair: DateRangePair) =
                  Render.pairFieldChange ctx None binding (nn pair) (pair.From, pair.To)

              let swapped: (string -> unit) * (string -> unit) =
                  (fun v -> write (dp current.From v)), (fun v -> write (dp v current.To))

              try
                  Expect.isFalse
                      (dateRangeContractHolds (fun () -> resolveState binding) current swapped)
                      "from/to swapped: both writes land, both are DateRangePairs, and the ends are wrong"
              finally
                  StateStore.remove key
          }

          // ── Site class 3: the filter chip's `Range` arm ───────────────────
          //
          // The chip passes `Binding.Filter(spec.Name, None)` as its write
          // destination — the auto-bind binding its own local `handleRange`
          // wrote to before Phase 1141 unified them, so this case pins the
          // substitution as well as the slot construction.
          test "filter Range: the chip's pair write-back lands in its filter slot" {
              let name = "ffwbh-filter-range"
              let binding: Binding<RangePair> = Binding.Filter(name, None)
              let current = rp 2.0 6.0

              try
                  Expect.isTrue
                      (rangeContractHolds
                          (fun () -> resolveFilter binding)
                          current
                          (Render.rangeInputHandlers (makeCtx ()) None binding current))
                      "the filter chip and the form field now share one pair write-back"

                  Expect.equal
                      (FilterStore.get name)
                      (Some(nn (rp current.Min 9.25)))
                      "the raw filter slot holds the boxed RangePair record, as `$filters.<name>` readers expect"
              finally
                  FilterStore.clear name
          }

          test "filter Range go-red: swapped slots fail on the filter path too" {
              let name = "ffwbh-filter-range-gr"
              let binding: Binding<RangePair> = Binding.Filter(name, None)
              let current = rp 2.0 6.0

              try
                  Expect.isFalse
                      (rangeContractHolds
                          (fun () -> resolveFilter binding)
                          current
                          (swappedSlotHandlers (makeCtx ()) binding current))
                      "the perturbation is caught on the destination the chip actually writes"
              finally
                  FilterStore.clear name
          }

          // ── Site class 4: the filter chip's `DateRange` arm ───────────────
          test "filter DateRange: the chip's date pair lands in its filter slot" {
              let name = "ffwbh-filter-dates"
              let binding: Binding<DateRangePair> = Binding.Filter(name, None)
              let current = dp "2026-03-01" "2026-03-31"

              try
                  Expect.isTrue
                      (dateRangeContractHolds
                          (fun () -> resolveFilter binding)
                          current
                          (Render.dateRangeInputHandlers (makeCtx ()) None binding current))
                      "Phase 725's chip and the form's DateRange field are one construction"
              finally
                  FilterStore.clear name
          }

          // ── A present handler wins, on the seam every site now shares ─────
          test "a present handler dispatches the whole pair and touches no store" {
              let key = stateKey "handled"
              let binding: Binding<RangePair> = Binding.State(key, Some(rp 0.0 0.0))
              let current = rp 1.0 8.0
              let mutable dispatched = None

              let ctx =
                  { makeCtx () with
                      Dispatch = fun msg -> dispatched <- Some msg }

              try
                  let onMin, onMax =
                      Render.rangeInputHandlers
                          ctx
                          (Some(fun pair -> Action.Dispatch(RangeChanged pair)))
                          binding
                          current

                  onMin 3.5
                  Expect.equal dispatched (Some(RangeChanged(3.5, 8.0))) "the min input emits the WHOLE pair"

                  onMax 9.25
                  Expect.equal dispatched (Some(RangeChanged(1.0, 9.25))) "the max input emits the whole pair too"

                  Expect.equal (StateStore.get key) None "no store write while a handler is present"
              finally
                  StateStore.remove key
          }

          test "a present date handler dispatches the whole date pair" {
              let key = stateKey "handled-dates"
              let binding: Binding<DateRangePair> = Binding.State(key, Some(dp "" ""))
              let current = dp "2026-01-01" "2026-12-31"
              let mutable dispatched = None

              let ctx =
                  { makeCtx () with
                      Dispatch = fun msg -> dispatched <- Some msg }

              try
                  let onFrom, _ =
                      Render.dateRangeInputHandlers
                          ctx
                          (Some(fun pair -> Action.Dispatch(DatesChanged pair)))
                          binding
                          current

                  onFrom "2026-02-01"

                  Expect.equal
                      dispatched
                      (Some(DatesChanged("2026-02-01", "2026-12-31")))
                      "the from input emits the whole pair"

                  Expect.equal (StateStore.get key) None "no store write while a handler is present"
              finally
                  StateStore.remove key
          }

          // ── The scalar seam: `fieldChange`, the other 20 call sites ───────
          //
          // Every scalar form field and filter chip now dispatches here. The
          // four cases are the four behaviours those sites depend on.
          test "fieldChange writes a State-bound scalar" {
              let key = stateKey "scalar-state"
              let binding: Binding<string> = Binding.State(key, Some "")

              try
                  Render.fieldChange (makeCtx ()) None binding (Some(nn "typed")) "typed"
                  Expect.equal (resolveState binding) (Some "typed") "the field's own State slot holds the new value"
              finally
                  StateStore.remove key
          }

          test "fieldChange writes a Filter-bound scalar (the chip's declarative path)" {
              let name = "ffwbh-scalar-filter"
              let binding: Binding<float> = Binding.Filter(name, None)

              try
                  Render.fieldChange (makeCtx ()) None binding (Some(nn 42.0)) 42.0
                  Expect.equal (resolveFilter binding) (Some 42.0) "the chip's filter slot holds the new value"
              finally
                  FilterStore.clear name
          }

          test "fieldChange with a None payload CLEARS — the cleared-choice contract" {
              // This is the behaviour the pair dispatch deliberately cannot
              // express, and the reason `pairFieldChange` takes `obj` rather
              // than `obj option`. A choice control needs it; a pair never does.
              let name = "ffwbh-scalar-clear"
              let binding: Binding<string> = Binding.Filter(name, None)

              try
                  Render.fieldChange (makeCtx ()) None binding (Some(nn "chosen")) (Some "chosen")
                  Expect.equal (FilterStore.get name) (Some(nn "chosen")) "seeded"

                  Render.fieldChange (makeCtx ()) None binding None (None: string option)
                  Expect.equal (FilterStore.get name) None "a cleared choice REMOVES the key, it does not store empty"
              finally
                  FilterStore.clear name
          }

          test "fieldChange with a present handler dispatches and writes nothing" {
              let key = stateKey "scalar-handled"
              let binding: Binding<string> = Binding.State(key, Some "")
              let mutable dispatched = None

              let ctx =
                  { makeCtx () with
                      Dispatch = fun msg -> dispatched <- Some msg }

              try
                  Render.fieldChange
                      ctx
                      (Some(fun v -> Action.Dispatch(TextChanged v)))
                      binding
                      (Some(nn "typed"))
                      "typed"

                  Expect.equal dispatched (Some(TextChanged "typed")) "the closure wins"
                  Expect.equal (StateStore.get key) None "and the store is untouched"
              finally
                  StateStore.remove key
          }

          // ── The census: the unification cannot be silently undone ─────────
          //
          // Reads the renderer sources the test project copies into its own
          // output (the HotPathVocabularyTests / ChartProvenanceTests
          // precedent: copied rather than resolved by climbing, so the scan
          // cannot read a different checkout's sources than the ones this build
          // compiled).
          test "census: the two render functions hold no write-back implementation of their own" {
              let path =
                  Path.Combine(AppContext.BaseDirectory, "renderer-sources", "client", "Render.fs")

              if not (File.Exists path) then
                  failwithf
                      "renderer source not found at %s — Fuaran.UI.Tests copies the renderer sources into its output; check the Content items. A shape scan with no source to scan reports every call site as clean."
                      path

              let source = File.ReadAllText path

              // The region is the two render functions this phase unified,
              // bounded by the `and private` declarations either side of them.
              // Locating by declaration keeps the scan correct across the line
              // drift the phase body has already had to re-cite twice.
              let indexOf (marker: string) =
                  let i = source.IndexOf(marker, StringComparison.Ordinal)

                  if i < 0 then
                      failwithf
                          "census marker %s not found in Render.fs — the scan cannot report on a region it did not locate."
                          marker

                  i

              let regionStart = indexOf "and private renderFormField"
              let regionEnd = indexOf "and private renderSegmentedChoiceCore"

              Expect.isLessThan
                  regionStart
                  regionEnd
                  "renderFormField must still precede renderSegmentedChoiceCore for the region to be well-formed"

              let region = source.Substring(regionStart, regionEnd - regionStart)

              // Comments narrate the old locals by name, so the scan reads
              // code only — otherwise this module's own explanatory prose in
              // Render.fs would fail it.
              let code =
                  region.Split('\n')
                  |> Array.map (fun line ->
                      let trimmed = line.TrimStart()

                      if trimmed.StartsWith("//", StringComparison.Ordinal) then
                          ""
                      else
                          line)
                  |> String.concat "\n"

              let forbidden =
                  [ "FilterStore.set", "a direct filter write — route it through `fieldChange`"
                    "FilterStore.clear", "a direct filter clear — the `None` payload to `fieldChange` is the clear"
                    "StateStore.set", "a direct state write — route it through `fieldChange`"
                    "StateStore.remove", "a direct state clear — route it through `fieldChange`"
                    "writeBackTo ctx", "a second call into the write-back primitive; `fieldChange` is the only caller" ]

              for token, why in forbidden do
                  Expect.isFalse
                      (code.Contains(token, StringComparison.Ordinal))
                      (sprintf
                          "renderFormField/renderFilterSpec must not contain `%s` — %s. A local write-back is untestable by construction, which is the gap Phase 1141 closed."
                          token
                          why)

              // Positive half: the region must still ROUTE somewhere, so the
              // scan cannot pass by the region having become empty.
              let routed =
                  Regex
                      .Matches(code, @"\b(fieldChange|pairFieldChange|rangeInputHandlers|dateRangeInputHandlers)\b")
                      .Count

              Expect.isGreaterThan
                  routed
                  15
                  "the region must still route its ~24 write-back call sites through the module-level seam"
          } ]
