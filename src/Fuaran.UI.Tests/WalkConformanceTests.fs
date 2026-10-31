module Fuaran.UI.Tests.WalkConformance

// ============================================================================
//  Phase 936 — analysis and subscription agree, and a test says so.
//
//  SINCE PHASE 2069 THERE IS ONE SLOT ENUMERATION: `Render.collectKeys` is a
//  projection of `BindingWalk`'s uses, and `kindKeys` is gone. This census now
//  pins the projection: per slot class, what `BindingWalk.collect` must read and
//  what the reactive projection must subscribe, plus the two-sided containment
//  between them over the fixture set.
//
//  Phase 936's verdict went the other way, keeping two walks because the census
//  found at least FOUR distinct per-slot semantics. Phase 2069 keeps those four
//  as the projection's scope instead of as flags on every slot: an action slot is
//  outside the render pass, and a `SlotArg` or `fallback` subtree is walked with
//  subscription off. The four — subscribed read; dispatch-time read (real to
//  analysis, deliberately NOT subscribed: it resolves when the action fires,
//  not at render); boundary read (`SlotArg` trees: counted conservatively by
//  analysis, deliberately not subscribed — a `Mount` re-render would re-mount
//  the guest, and `FragmentRef` expansion subscribes post-expansion); and
//  write-destination (`editStateKey`: read by NEITHER, by design). A new FIELD on
//  an existing spec still lands green until a census fixture exists for it (a new
//  KIND is compiler-forced by the walk's exhaustive match), and the recorded
//  follow-up for that residue is deriving the slot inventory from the IDL
//  (`Generated`), which knows every spec field.
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
                          (fun _ -> Ok 0.0),
                          None,
                          None
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
      // ── Phase 1812: the envelope `fallback` — read by analysis, NOT subscribed ──
      { Slot = "Node.fallback subtree"
        Key = "cw-fallback"
        Tree =
          { Fuaran.markdown "body" "b" with
              Fallback = Some(metricOn "f" "cw-fallback") }
        ExpectRead = true
        ExpectSubscribe = false
        Asymmetry =
          "Phase 1812 — the author-declared fallback renders only on a reader BEHIND \
           this node's kind (WIRE_FORMAT §3.1). Its readers are real to analysis, \
           because a dangling reference inside it is the defect the behind reader \
           meets first; a CURRENT reader never renders it, so subscribing its keys \
           would re-render a surface for a node that is not on it. The behind reader \
           that lifts it subscribes the lifted node like any other." }
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

      // The third instance of this file's own founding shape, and the oldest:
      // the REACTIVE walk has subscribed a `Binding.Transform`'s live source
      // since Phase 818 (a `SetState` on the source key re-evaluates the
      // pipeline and re-renders every reader), while the ANALYSIS walk recorded
      // it as `BindingUse.TransformStateSource` and deliberately kept it out of
      // `StateKeyFacts.Reads`. So FUARAN098 could report "nothing in the tree
      // reads this key" about a key the renderer beside it demonstrably
      // subscribes — the drift running in the `PageStateKey` direction, in a
      // slot no census row covered. No row existed, so the containment
      // assertion could not see it: it quantifies over the census trees, and
      // none of them carried a Transform.
      { Slot = "Binding.Transform live State source"
        Key = "cw-transform-src"
        Tree =
          Fuaran.badge
              "tsrc"
              { Defaults.badge with
                  Label =
                      TextSource.Bound(
                          Binding.Transform(
                              TransformSource.Live(
                                  Binding.State("cw-transform-src", None),
                                  HostPrelude.TransformLive.emptySource
                              ),
                              [ Fuaran.Compute.Transform.GroupBy(
                                    [ "team" ],
                                    [ { Name = "n"
                                        Fn = Fuaran.Core.AggFn.Count
                                        Of = "team" } ]
                                ) ],
                              None
                          )
                      ) }
        ExpectRead = true
        ExpectSubscribe = true
        Asymmetry = "" }

      // ── Phase 2069: slots one walk had and the other did not ──
      { Slot = "Media.Tracks Src (only the deleted reactive walk had it)"
        Key = "cw-track"
        Tree =
          { Fuaran.video "vid" "/a.mp4" "Clip" with
              Kind =
                  NodeKind.Media
                      { Defaults.media with
                          Tracks =
                              [ { Src = binding.state "cw-track" "/a.vtt"
                                  Kind = TrackKind.Captions
                                  SrcLang = "en"
                                  Label = TextSource.Literal "English"
                                  Default = false } ] } }
        ExpectRead = true
        ExpectSubscribe = true
        Asymmetry = "" }
      { Slot = "Media.Transcript (only the deleted reactive walk had it)"
        Key = "cw-transcript"
        Tree =
          { Fuaran.video "vidt" "/a.mp4" "Clip" with
              Kind =
                  NodeKind.Media
                      { Defaults.media with
                          Transcript = Some(TextSource.Bound(binding.state "cw-transcript" "t")) } }
        ExpectRead = true
        ExpectSubscribe = true
        Asymmetry = "" }
      { Slot = "Tree.ExpandedStateKey (only the deleted reactive walk had it)"
        Key = "cw-tree-expanded"
        Tree =
          { Fuaran.tree "tree" [] with
              Kind =
                  NodeKind.Tree
                      { Defaults.tree with
                          ExpandedStateKey = Some "cw-tree-expanded" } }
        ExpectRead = true
        ExpectSubscribe = true
        Asymmetry = "" }
      { Slot = "Node.Visible (only analysis had it, so a predicate never re-rendered)"
        Key = "cw-visible"
        Tree =
          { Fuaran.markdown "vis" "text" with
              Visible = Some(binding.state "cw-visible" true) }
        ExpectRead = true
        ExpectSubscribe = true
        Asymmetry = "" }
      { Slot = "Node.Tooltip (only analysis had it)"
        Key = "cw-tooltip"
        Tree =
          { Fuaran.markdown "tip" "text" with
              Tooltip = Some(TextSource.Bound(binding.state "cw-tooltip" "hint")) }
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
