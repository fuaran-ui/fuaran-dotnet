module Fuaran.UI.Tests.WalkConformance

// ============================================================================
//  Phase 936 — the two binding walks agree, and a test says so.
//
//  The estate maintains TWO walks over the same node vocabulary:
//   - `BindingWalk.collect` (Fuaran.UI) — the analysis walk validation runs on;
//   - `Render.collectKeys`  (Fuaran.UI.Renderer) — the reactive walk that
//     decides which State keys a rendered surface SUBSCRIBES.
//  Their only coupling was a comment, and it failed in both directions
//  (`PageStateKey` was a live reactivity bug that sat undetected across four
//  phases). This file is the chosen coupling mechanism: an EXECUTABLE CENSUS —
//  one fixture per slot class, each declaring what the analysis walk must see
//  and what the reactive walk must subscribe — plus the two-sided containment
//  assertion between the walks over the whole fixture set.
//
//  THE VERDICT (task 3 of the phase, recorded here so the next reader finds
//  it): the conformance-test mechanism was chosen over a single shared slot
//  enumeration both walks consume. The census below found at least FOUR
//  distinct per-slot semantics — subscribed read; dispatch-time read (real to
//  analysis, deliberately NOT subscribed: it resolves when the action fires,
//  not at render); boundary read (`SlotArg` trees: counted conservatively by
//  analysis, deliberately not subscribed — a `Mount` re-render would re-mount
//  the guest, and `FragmentRef` expansion subscribes post-expansion); and
//  write-destination (`editStateKey`: read by NEITHER, by design). A shared
//  enumeration would have to encode all four as per-slot flags that both walks
//  interpret — a third artefact that can be wrong in BOTH walks at once,
//  forfeiting the independent redundancy that caught the historical drifts.
//  The test keeps the walks independent and makes divergence red. What it
//  costs: a new FIELD on an existing spec lands green until a census fixture
//  exists for it (new KINDS are compiler-forced in both walks — each holds an
//  exhaustive match). The recorded follow-up for that residue is deriving the
//  slot inventory from the IDL (`Generated`), which knows every spec field.
//
//  Go-red proofs (run at authoring, per the phase's evidence rule):
//   - reverting the Phase 936 `collectKeys` StateBehaviour descent reddens the
//     OnEmpty/OnLoading fixtures AND the containment assertion;
//   - reverting the Drawing arm reddens the drawing fixtures the same way;
//   - dropping a fixture's key from the exemption set reddens containment.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

type private Msg = | NoOp

/// One census row: a tree embedding `Binding.State(Key, _)` in exactly one
/// slot, with the expected verdict of each walk and (where the walks are
/// ALLOWED to disagree) the documented reason.
type private CensusRow =
    {
        Slot: string
        Key: string
        Tree: Node<Msg>
        /// The analysis walk must count the key as a state READ.
        ExpectRead: bool
        /// The reactive walk must SUBSCRIBE the key.
        ExpectSubscribe: bool
        /// Non-empty exactly when ExpectRead <> ExpectSubscribe — the recorded
        /// reason the asymmetry is deliberate. An asymmetry without a reason is
        /// the drift this file exists to catch.
        Asymmetry: string
    }

let private metricOn (id: string) (key: string) : Node<Msg> =
    Fuaran.metric
        id
        { Defaults.metric with
            Label = TextSource.Literal "M"
            Value = binding.state key 0.0 }

let private census: CensusRow list =
    [
      // ── Both-covered slots (the common case, sampled per slot family) ──
      { Slot = "spec body binding (Metric.Value)"
        Key = "cw-body"
        Tree = metricOn "m" "cw-body"
        ExpectRead = true
        ExpectSubscribe = true
        Asymmetry = "" }
      { Slot = "Accessibility.Label"
        Key = "cw-a11y"
        Tree =
          { Fuaran.markdown "md" "text" with
              Accessibility =
                  Some
                      { Defaults.Accessibility.empty with
                          Label = Some(binding.state "cw-a11y" "label") } }
        ExpectRead = true
        ExpectSubscribe = true
        Asymmetry = "" }
      { Slot = "Switch selector (SwitchSpec.On)"
        Key = "cw-switch"
        Tree =
          Fuaran.switch
              "sw"
              { Defaults.switch with
                  On = Binding.State("cw-switch", None) }
        ExpectRead = true
        ExpectSubscribe = true
        Asymmetry = "" }
      { Slot = "Tabs.ActiveIndex"
        Key = "cw-tabs"
        Tree =
          Fuaran.tabs
              "tabs"
              { Defaults.tabs with
                  ActiveIndex = binding.state "cw-tabs" 0 }
        ExpectRead = true
        ExpectSubscribe = true
        Asymmetry = "" }
      { Slot = "Binding.Local initialFrom recursion"
        Key = "cw-local"
        Tree =
          Fuaran.metric
              "lm"
              { Defaults.metric with
                  Label = TextSource.Literal "L"
                  Value =
                      Binding.Local(
                          LocalFlushTrigger.OnBlur,
                          string,
                          binding.state "cw-local" 0.0,
                          None,
                          (fun _ -> Ok 0.0)
                      ) }
        ExpectRead = true
        ExpectSubscribe = true
        Asymmetry = "" }

      // ── The Phase 936 closures: StateBehaviour subtrees ──
      { Slot = "StateBehaviour.OnEmpty subtree"
        Key = "cw-onempty"
        Tree =
          { Fuaran.markdown "body" "b" with
              State =
                  Some
                      { OnEmpty = Some(metricOn "e" "cw-onempty")
                        OnError = None
                        OnLoading = None } }
        ExpectRead = true
        ExpectSubscribe = true
        Asymmetry = "" }
      { Slot = "StateBehaviour.OnLoading subtree"
        Key = "cw-onloading"
        Tree =
          { Fuaran.markdown "body" "b" with
              State =
                  Some
                      { OnEmpty = None
                        OnError = None
                        OnLoading = Some(metricOn "l" "cw-onloading") } }
        ExpectRead = true
        ExpectSubscribe = true
        Asymmetry = "" }

      // ── The Phase 936 closures: Drawing reactive slots ──
      { Slot = "Drawing DrawStyle.Fill"
        Key = "cw-fill"
        Tree =
          Fuaran.drawingSpec
              "d"
              { Defaults.drawing with
                  Shapes =
                      [ Shape.Rectangle(
                            0.0,
                            0.0,
                            10.0,
                            10.0,
                            None,
                            { Defaults.drawStyle with
                                Fill = Some(binding.state "cw-fill" "#fff") }
                        ) ] }
        ExpectRead = true
        ExpectSubscribe = true
        Asymmetry = "" }
      { Slot = "Drawing Shape.Label text"
        Key = "cw-shapelabel"
        Tree =
          Fuaran.drawingSpec
              "d2"
              { Defaults.drawing with
                  Shapes =
                      [ Shape.Label(1.0, 1.0, TextSource.Bound(binding.state "cw-shapelabel" "t"), Defaults.drawStyle) ] }
        ExpectRead = true
        ExpectSubscribe = true
        Asymmetry = "" }
      // Phase 1079 — the census row that closes the residue this file's own
      // header predicted: "a new FIELD on an existing spec lands green until a
      // census fixture exists for it". `ImageSpec.Caption` arrived at Phase
      // 1078, was collected by the ANALYSIS walk (which descends the spec
      // structurally) and missed by the REACTIVE one (whose `Image` arm
      // enumerates slots by hand and was not extended), so a bound caption was
      // validated as a state read and never subscribed — the picture's alt text
      // beside it re-rendered and the caption did not. Exactly the
      // `PageStateKey` shape the file was written for, in a new slot, four
      // phases later. The fix is one `keysOfTextOpt` call in `Render.kindKeys`;
      // this row is what makes reverting it red.
      { Slot = "Image.Caption (optional TextSource)"
        Key = "cw-imgcaption"
        Tree =
          Fuaran.imageSpec
              "imgcap"
              { Defaults.image with
                  Src = Binding.Static(Some "/a.png")
                  Alt = TextSource.Literal "Alt"
                  Caption = Some(TextSource.Bound(binding.state "cw-imgcaption" "c")) }
        ExpectRead = true
        ExpectSubscribe = true
        Asymmetry = "" }
      // …and its sibling, found by the same row. The reactive walk collected
      // the `SrcSet` candidates from Phase 1080 while the ANALYSIS walk did
      // not, so the asymmetry ran the other way and no fixture existed to say
      // so. A candidate resolved from a State key is a read on exactly the
      // terms the primary `Src` is.
      { Slot = "Image.SrcSet candidate Src"
        Key = "cw-imgsrcset"
        Tree =
          Fuaran.imageSpec
              "imgss"
              { Defaults.image with
                  Src = Binding.Static(Some "/a.png")
                  Alt = TextSource.Literal "Alt"
                  SrcSet =
                      [ { Src = binding.state "cw-imgsrcset" "/a-400.png"
                          Width = 400 } ] }
        ExpectRead = true
        ExpectSubscribe = true
        Asymmetry = "" }

      // ── Documented asymmetries ──
      { Slot = "Action.SetState valueFrom (Button.OnClick)"
        Key = "cw-dispatch"
        Tree =
          Fuaran.button
              "b"
              { Defaults.button with
                  Label = TextSource.Literal "Go"
                  OnClick = Action.SetState("cw-dispatch-dest", None, Some(Binding.State("cw-dispatch", None))) }
        ExpectRead = true
        ExpectSubscribe = false
        Asymmetry =
          "dispatch-time read: `valueFrom` resolves when the action FIRES, not at render, \
           so there is nothing on screen for a subscription to refresh" }
      { Slot = "FragmentRef.Args SlotArg subtree"
        Key = "cw-slotarg"
        Tree =
          { Fuaran.fragmentRef "fr" "frag" with
              Kind =
                  NodeKind.FragmentRef
                      { Name = "frag"
                        Args = Some(Map.ofList [ "slot", FragmentArg.SlotArg(metricOn "sa" "cw-slotarg") ]) } }
        ExpectRead = true
        ExpectSubscribe = false
        Asymmetry =
          "boundary read: analysis counts a host-authored SlotArg tree conservatively \
           (over-counting costs a missed finding, under-counting a false accusation); the \
           reactive walk subscribes the EXPANDED tree at render time instead, and `Mount` \
           inputs must not be host-subscribed at all — a re-render would re-mount the guest. \
           Recorded, not drifted." } ]

[<Tests>]
let tests =
    testList
        "Phase 936 — walk conformance (the executable census)"
        [ testList
              "per-slot census rows"
              (census
               |> List.map (fun row ->
                   test $"census: {row.Slot}" {
                       let reads = (BindingWalk.collect row.Tree).StateKeys.Reads
                       let subs = Render.collectStateKeys row.Tree

                       if row.ExpectRead then
                           Expect.contains (Set.toList reads) row.Key $"analysis walk must READ {row.Key}"
                       else
                           Expect.isFalse (Set.contains row.Key reads) $"analysis walk must NOT read {row.Key}"

                       if row.ExpectSubscribe then
                           Expect.contains (Set.toList subs) row.Key $"reactive walk must SUBSCRIBE {row.Key}"
                       else
                           Expect.isFalse (Set.contains row.Key subs) $"reactive walk must NOT subscribe {row.Key}"

                       if row.ExpectRead <> row.ExpectSubscribe then
                           Expect.isNotEmpty row.Asymmetry "an asymmetric row must record its reason"
                   }))

          test "two-sided containment: the walks disagree ONLY where the census says so" {
              // The structural half of the coupling. Over the union of every
              // census tree: every analysis-visible read is either subscribed
              // or an EXEMPTED (reason-carrying) asymmetry; and every
              // subscription is an analysis-visible read (the reactive walk
              // never invents a reader the validator cannot see). A slot wired
              // into one walk and not the other lands a key on exactly one
              // side with no exemption, and this fails.
              let exempted =
                  census
                  |> List.filter (fun r -> r.ExpectRead && not r.ExpectSubscribe)
                  |> List.map _.Key
                  |> Set.ofList

              for row in census do
                  let reads = (BindingWalk.collect row.Tree).StateKeys.Reads
                  let subs = Render.collectStateKeys row.Tree

                  Expect.isTrue
                      (Set.isSubset (Set.difference reads exempted) subs)
                      $"[{row.Slot}] non-exempt analysis reads must all be subscribed \
                        (reads={reads}, subs={subs}, exempted={exempted})"

                  Expect.isTrue
                      (Set.isSubset subs reads)
                      $"[{row.Slot}] every subscription must be an analysis-visible read \
                        (reads={reads}, subs={subs})"
          } ]
