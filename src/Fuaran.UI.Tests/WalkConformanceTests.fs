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

// ============================================================================
//  Phase 2381 — the walk records every member in DOCUMENT order.
//
//  "The first declaration" of a repetition (the seed that takes effect, the
//  FUARAN106 / FUARAN107 declaration a later one repeats) is the document's
//  first only if `BindingWalk` visits members in the canonical document's
//  member order (WIRE_FORMAT §27.8.2). Each document below gives every member
//  the walk records its own `State` key with a default. The oracle is the order
//  of those keys in the CANONICAL bytes, so no expected order is written here.
// ============================================================================

module private DocumentOrder =
    let mutable private counter = 0

    /// A `State` binding with a default, under a key no other binding uses.
    let private st (defaultValue: string) =
        counter <- counter + 1
        sprintf """{"$type":"State","defaultValue":%s,"key":"k%03d"}""" defaultValue counter

    let private txt () =
        sprintf """{"$type":"Bound","binding":%s}""" (st "\"t\"")

    let private flag () = st "true"
    let private num () = st "1"
    let private str () = st "\"s\""
    let private colour () = st "\"#111111\""

    let private metric (id: string) =
        sprintf """{"id":"%s","kind":{"$type":"Metric","label":"m","value":%s}}""" id (num ())

    let private node (kind: string) = sprintf """{"id":"n","kind":%s}""" kind

    let private reference =
        "9f04f29b27c86926a1c3e5d7b8f0a2c4e6d8f0a1b3c5d7e9f1a3b5c7d9e1f3a5"

    let documents: (string * string) list =
        [ "the node envelope: accessibility, fallback, kind, state, tooltip, visible",
          sprintf
              """{"accessibility":{"hidden":%s,"label":%s},"fallback":%s,"id":"root","kind":{"$type":"Box","children":[%s],"heading":%s,"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"},"state":{"onEmpty":%s,"onLoading":%s},"tooltip":%s,"visible":%s}"""
              (flag ())
              (str ())
              (metric "fb")
              (metric "c1")
              (txt ())
              (metric "se")
              (metric "sl")
              (txt ())
              (flag ())
          "Disclosure",
          node (
              sprintf
                  """{"$type":"Disclosure","children":[%s],"heading":%s,"open":%s}"""
                  (metric "c1")
                  (txt ())
                  (flag ())
          )
          "Tabs",
          node (
              sprintf
                  """{"$type":"Tabs","activeIndex":%s,"activeTag":%s,"children":[%s],"tabHeaders":[{"disabled":%s,"label":%s}],"tabTags":["s"]}"""
                  (st "0")
                  (str ())
                  (metric "c1")
                  (flag ())
                  (txt ())
          )
          "Modal",
          node (sprintf """{"$type":"Modal","children":[%s],"heading":%s,"open":%s}""" (metric "c1") (txt ()) (flag ()))
          "SummaryList",
          node (sprintf """{"$type":"SummaryList","children":[%s],"heading":%s}""" (metric "c1") (txt ()))
          "Stepper", node (sprintf """{"$type":"Stepper","activeStep":%s,"children":[%s]}""" (st "0") (metric "c1"))
          "Markdown",
          node (
              sprintf
                  """{"$type":"Markdown","citations":[{"label":%s,"name":"c","reference":"%s"}],"text":%s}"""
                  (txt ())
                  reference
                  (txt ())
          )
          "Metric",
          node (
              sprintf
                  """{"$type":"Metric","comparisons":[{"label":%s,"value":%s}],"label":%s,"subtext":%s,"trend":%s,"value":%s}"""
                  (txt ())
                  (num ())
                  (txt ())
                  (txt ())
                  (num ())
                  (num ())
          )
          "Callout", node (sprintf """{"$type":"Callout","body":%s,"heading":%s}""" (txt ()) (txt ()))
          "Progress",
          node (sprintf """{"$type":"Progress","caveat":%s,"fraction":%s,"label":%s}""" (txt ()) (st "0.5") (txt ()))
          "LabelValueRow",
          node (sprintf """{"$type":"LabelValueRow","help":%s,"label":%s,"value":%s}""" (txt ()) (txt ()) (num ()))
          "Fact", node (sprintf """{"$type":"Fact","help":%s,"label":%s,"value":%s}""" (txt ()) (txt ()) (txt ()))
          "Image",
          node (
              sprintf
                  """{"$type":"Image","alt":%s,"caption":%s,"src":%s,"srcSet":[{"src":%s,"width":400}]}"""
                  (txt ())
                  (txt ())
                  (str ())
                  (str ())
          )
          "Media",
          node (
              sprintf
                  """{"$type":"Media","kind":{"$type":"Video","poster":%s},"label":%s,"src":%s,"tracks":[{"kind":"Captions","label":%s,"src":%s,"srcLang":"en"}],"transcript":%s}"""
                  (str ())
                  (txt ())
                  (str ())
                  (txt ())
                  (str ())
                  (txt ())
          )
          "Embed", node (sprintf """{"$type":"Embed","src":%s,"title":%s}""" (str ()) (txt ()))
          "Link", node (sprintf """{"$type":"Link","href":%s,"label":%s}""" (str ()) (txt ()))
          "Tree",
          node (
              sprintf
                  """{"$type":"Tree","items":[{"children":[{"id":"i2","label":%s}],"id":"i1","label":%s}]}"""
                  (txt ())
                  (txt ())
          )
          "Toast", node (sprintf """{"$type":"Toast","message":%s,"open":%s}""" (txt ()) (flag ()))
          "Drawing",
          node (
              sprintf
                  """{"$type":"Drawing","description":%s,"shapes":[{"$type":"Group","children":[{"$type":"Label","style":{"fill":%s},"text":%s,"x":0,"y":0}],"style":{"fill":%s,"opacity":%s,"stroke":%s,"strokeWidth":%s}}],"style":{"fill":%s,"opacity":%s,"stroke":%s,"strokeWidth":%s},"title":%s,"viewBox":{"height":10,"minX":0,"minY":0,"width":10}}"""
                  (txt ())
                  (colour ())
                  (txt ())
                  (colour ())
                  (num ())
                  (colour ())
                  (num ())
                  (colour ())
                  (num ())
                  (colour ())
                  (num ())
                  (txt ())
          )
          "Button",
          node (
              sprintf
                  """{"$type":"Button","disabled":%s,"label":%s,"onClick":{"$type":"Chain","ops":[]}}"""
                  (flag ())
                  (txt ())
          )
          "FileUpload",
          node (sprintf """{"$type":"FileUpload","accept":["image/*"],"disabled":%s,"label":%s}""" (flag ()) (txt ()))
          "Select",
          node (
              sprintf
                  """{"$type":"Select","disabled":%s,"label":%s,"placeholder":%s,"source":{"$type":"Static","value":[{"label":%s,"value":"a"}]},"value":%s}"""
                  (flag ())
                  (txt ())
                  (txt ())
                  (txt ())
                  (str ())
          )
          "Form",
          node (
              sprintf
                  """{"$type":"Form","disabled":%s,"fields":[{"help":%s,"id":"f1","kind":{"$type":"Text","value":%s},"label":%s}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":%s}"""
                  (flag ())
                  (txt ())
                  (str ())
                  (txt ())
                  (txt ())
          )
          "Filters",
          node (
              sprintf
                  """{"$type":"Filters","items":[{"kind":{"$type":"Text","value":%s},"label":%s,"name":"n1"}]}"""
                  (str ())
                  (txt ())
          )
          "DataGrid (a bound source)",
          node (
              sprintf
                  """{"$type":"DataGrid","columns":[{"field":"a","kind":{"$type":"Text"},"label":"a"}],"rowKeyField":"a","rowTotal":%s,"source":%s}"""
                  (st "1")
                  (st """[{"a":"x"}]""")
          )
          "DataGrid (static rows)",
          node (
              sprintf
                  """{"$type":"DataGrid","columns":[{"field":"a","kind":{"$type":"Text"},"label":"a"}],"source":{"$type":"Static","value":[]},"staticRows":{"citations":[{"column":0,"label":%s,"reference":"%s","row":0}],"headers":[%s],"rows":[[%s]]}}"""
                  (txt ())
                  reference
                  (txt ())
                  (txt ())
          )
          "Chart",
          node (
              sprintf
                  """{"$type":"Chart","kind":"Bar","source":%s,"title":%s,"xField":"q","yFields":["r"]}"""
                  (st """[{"q":"a","r":1}]""")
                  (txt ())
          )
          "Switch",
          node (
              sprintf
                  """{"$type":"Switch","cases":[{"child":%s,"when":%s},{"child":%s,"match":"b"}],"default":%s,"on":%s}"""
                  (metric "w1")
                  (flag ())
                  (metric "w2")
                  (metric "w3")
                  (str ())
          )
          "ErrorBoundary",
          node (sprintf """{"$type":"ErrorBoundary","child":%s,"fallback":%s}""" (metric "c1") (metric "c2")) ]

[<Tests>]
let documentOrder =
    testList
        "Phase 2381 — the walk records every member in document order"
        [ for name, json in DocumentOrder.documents ->
              test name {
                  match Fuaran.UI.Ops.JsonDecode.decodeNodeObj json with
                  | Error e -> failtestf "the document does not decode: %A at %s" e.Code e.Path
                  | Ok tree ->
                      let canonical = Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode tree

                      // The keys in the order the canonical bytes hold them.
                      let declared =
                          System.Text.RegularExpressions.Regex.Matches(canonical, "\"key\":\"(k[0-9]{3})\"")
                          |> Seq.map _.Groups[1].Value
                          |> List.ofSeq

                      Expect.isNonEmpty declared "the document declares keys"
                      let facts = BindingWalk.collect tree

                      Expect.equal
                          (facts.StateKeys.Seeds |> List.map _.Key)
                          declared
                          "every declaration is recorded once, in the document's order"

                      let read =
                          facts.Uses
                          |> List.choose (fun u ->
                              match u.Use with
                              | BindingWalk.BindingUse.State k when List.contains k declared -> Some k
                              | _ -> None)
                          |> List.distinct

                      Expect.equal
                          read
                          (declared |> List.filter (fun k -> List.contains k read))
                          "reads follow the document's order"
              } ]

/// Phase 2381 — four shapes where a node's own `visible` or `tooltip` declares a
/// key that a document-earlier member (its kind's child, its state alternative)
/// also declares. The earlier one seeds the key, and FUARAN106 sits on the later.
[<Tests>]
let documentOrderShapes =
    let container (children: string) =
        sprintf
            """{"$type":"Box","children":[%s],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}"""
            children

    let metric (id: string) (value: int) =
        sprintf
            """{"id":"%s","kind":{"$type":"Metric","label":"%s","value":{"$type":"State","defaultValue":%d,"key":"k"}}}"""
            id
            id
            value

    let markdown = """{"id":"c","kind":{"$type":"Markdown","text":"t"}}"""

    let visible (value: string) =
        sprintf """"visible":{"$type":"State","defaultValue":%s,"key":"k"}""" value

    let shapes: (string * string * string * string) list =
        [ "a node's own visible after its child",
          sprintf """{"id":"root","kind":%s,%s}""" (container (metric "a2" 2)) (visible "true"),
          "2",
          "$"
          "a node's own visible after its state alternative",
          sprintf
              """{"id":"root","kind":%s,"state":{"onEmpty":%s},%s}"""
              (container markdown)
              (metric "a1" 1)
              (visible "false"),
          "1",
          "$"
          "a fallback's own visible after its child",
          sprintf
              """{"fallback":{"id":"fb","kind":%s,%s},"id":"root","kind":%s}"""
              (container (metric "a2" 2))
              (visible "true")
              (container markdown),
          "2",
          "$.fallback"
          "a node's own tooltip after its child",
          sprintf
              """{"id":"root","kind":%s,"tooltip":{"$type":"Bound","binding":{"$type":"State","defaultValue":"x","key":"k"}}}"""
              (container (metric "a2" 2)),
          "2",
          "$" ]

    testList
        "Phase 2381 — a node's tooltip and visible come after its kind and state"
        [ for name, json, seed, at in shapes ->
              test name {
                  match Fuaran.UI.Ops.JsonDecode.decodeNodeObj json with
                  | Error e -> failtestf "the document does not decode: %A at %s" e.Code e.Path
                  | Ok tree ->
                      let facts = BindingWalk.renderFacts tree

                      Expect.equal
                          (string (facts.StateSeeds |> Map.find "k"))
                          seed
                          "the document's first declaration seeds k"

                      Expect.equal
                          (string (BindingWalk.stateSeeds tree |> Map.find "k"))
                          seed
                          "one seed map, two passes"

                      let located =
                          PreEmitValidate.validateWithPaths tree
                          |> List.filter (fun (d, _) -> (PreEmitValidate.findingOf d).Code = "FUARAN106")
                          |> List.map snd

                      Expect.equal located [ Some at ] "FUARAN106 sits on the later declaration"
              } ]
