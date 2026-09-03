module Fuaran.UI.JsonDecode.Tests.Fixtures

// ============================================================================
//  Round-trip fixture corpus.
//
//  Every fixture is a `Node<obj>` (storage-shape erasure target) so the
//  encoder-decoder round-trip stays type-compatible (`encodeNode` returns
//  a string regardless of 'Msg; `decodeNode` always returns `Node<obj>`).
//
//  Coverage gate. The fixtures here MUST cover every shipped NodeKind
//  case (and every Spec record under it). If a future phase ships a new
//  NodeKind / Spec, both this file and the round-trip suite update in
//  the same commit per the forward-coupling rule in JsonDecode.fs.
//  The current floor (5 NodeKind cases × N specs):
//   - Layout: Dashboard, Stack, Grid, SplitPanel, Tabs, Card, Stepper, SummaryList,
//     Disclosure
//   - Display: Heading, Markdown, Metric, Badge, Sparkline, Spacer, Callout,
//     Progress, Skeleton, LabelValueRow
//   - Input: Form (Text + Number + RangedNumber + Checkbox + Choice + TextArea
//     fields), Filters (TextFilter + ChoiceFilter), Button, FileUpload, Select
//   - Visualisation: Grid (one Text column), Chart (Line + xField + yFields),
//     Table (one header row + two body rows), Map (one MapMarker)
//   - Custom (single moduleId+componentId+props)
//   - Composite (Dashboard containing a Card containing a Metric — covers
//     the recursive Children path through layoutKindAppender)
// ============================================================================

#nowarn "3261"

open System
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI
open Fuaran.UI.Ops.Types

// ─── Shared default fragments ────────────────────────────────────────────

let private defaultStyle: SemanticStyle = Defaults.style

let private node (id: string) (kind: NodeKind<obj>) (accessibility: Accessibility option) : Node<obj> =
    // `State = None` / `Style = None` (Phase 692–694 swap): the pre-swap
    // required empty-state / default-style records were omitted by the encoder,
    // and omission is the `None` shape post-swap — byte-identical corpus.
    { Id = id
      Kind = kind
      State = None
      Style = None
      Accessibility = accessibility
      Motion = None
      ExtraAttributes = None
      Tooltip = None }

/// Phase 695 — a sample child reused in two slots of one composite fixture needs
/// a distinct id in each: NodeIds are unique WITHIN a tree (`WIRE_FORMAT.md` §8),
/// and `PreEmitValidate` refuses a tree that repeats one. Sharing the node value
/// is still the point — only the identity varies.
let private withId (id: string) (n: Node<obj>) : Node<obj> = { n with Id = id }

// ─── Display fixtures ────────────────────────────────────────────────────

let private metricSpec: MetricSpec =
    { Defaults.metric with
        Label = TextSource.Literal "Revenue"
        Value = Binding.Static(Some 1234.5)
        Format = CellFormat.Currency "GBP"
        Tone = ToneVariant.Brand
        Trend = Some(Binding.Static(Some 0.07))
        TrendFormat = Some(CellFormat.Percent(Some 1))
        Icon = Some "trending-up"
        Subtext = Some(TextSource.Literal "vs last month") }

let metric: Node<obj> = node "metric-1" (NodeKind.Metric(metricSpec)) None

// ─── Numeric divergence-zone fixtures (Phase 117) ────────────────────────
//
// Every other float fixture in the corpus is a small plain-decimal value
// (1234.5, 0.42, 0.07) — exactly the int53 range where JS `String(n)` and
// .NET `Double.ToString("R")` happen to agree. These four push a float through
// the `Binding.Static` / Metric `source` path in the zone where the two
// formats diverge in LAYOUT (the shortest digits are shared; the spelling is
// not), so the cross-host conformance gate + the property fuzzer actually
// exercise the .NET-"R" numeric contract WIRE_FORMAT.md §2 rule 5 mandates:
//   - exp-pos:  1e21                  → "1E+21"                 (high-magnitude scientific)
//   - exp-neg:  1e-7                  → "1E-07"                 (small-magnitude scientific, padded exponent)
//   - 17sig:    0.1 + 0.2             → "0.30000000000000004"   (17 significant digits, fixed)
//   - bigint:   123456789012345680.0  → "1.2345678901234568E+17" (integer > 2^53; JS renders fixed, .NET scientific — the nastiest divergence)
let private metricFloat (id: string) (value: float) : Node<obj> =
    node
        id
        (NodeKind.Metric(
            { metricSpec with
                Value = Binding.Static(Some value)
                Trend = None
                TrendFormat = None }
        ))
        None

let metricFloatExpPos: Node<obj> = metricFloat "metric-float-exp-pos" 1e21
let metricFloatExpNeg: Node<obj> = metricFloat "metric-float-exp-neg" 1e-7

let metricFloat17Sig: Node<obj> =
    metricFloat "metric-float-17sig" 0.30000000000000004

let metricFloatBigInt: Node<obj> =
    metricFloat "metric-float-bigint" 123456789012345680.0

let heading: Node<obj> =
    node
        "heading-1"
        (NodeKind.Heading(
            { Level = 2
              Text = TextSource.Literal "Channel performance"
              Variant = HeadingVariant.Standard }
        ))
        None

/// Phase 147 — a node carrying the new bounded `Role` / `Voice` style
/// vocabulary (non-default), exercising the optional-emit wire path: the
/// `role` / `voice` keys appear on the `style` object only because they
/// differ from `StyleRole.None` / `FontVoice.Default`.
let styleRoleVoice: Node<obj> =
    { node "style-role-voice-1" (NodeKind.Markdown({ Text = TextSource.Literal "Q3 revenue" })) None with
        Style =
            Some
                { defaultStyle with
                    Role = StyleRole.Data
                    Voice = FontVoice.Display } }

let markdown: Node<obj> =
    node "markdown-1" (NodeKind.Markdown({ Text = TextSource.Literal "Updated hourly." })) None

/// Phase 1472 — a `SemanticStyle` carrying `Direction` and NOTHING ELSE, so the
/// emitted `style` object is exactly `{"direction":"ltr"}`. One member is the
/// point: a fixture whose style also set a tone or a weight could decode with
/// `direction` silently dropped and still round-trip, and the vector would
/// prove nothing about the slot it was written for.
let styleDirectionLtrRun: Node<obj> =
    { node "style-direction-ltr-1" (NodeKind.Markdown({ Text = TextSource.Literal "RR123456789IL" })) None with
        Style =
            Some
                { defaultStyle with
                    Direction = TextDirection.Ltr } }

let badge: Node<obj> =
    node
        "badge-1"
        (NodeKind.Badge(
            { Label = TextSource.Literal "Beta"
              Variant = BadgeVariant.Info }
        ))
        None

let link: Node<obj> =
    node
        "link-1"
        (NodeKind.Link(
            { Defaults.link with
                Href = Binding.Static(Some "/about")
                Label = TextSource.Literal "About us"
                Rel = Some "noopener"
                Target = Some "_blank" }
        ))
        None

let linkProtected: Node<obj> =
    // Phase 812 — the optional `protection` field ("email" on the wire),
    // omitted when absent so every pre-812 tree stays byte-identical.
    node
        "link-protected-1"
        (NodeKind.Link(
            { Defaults.link with
                Href = Binding.Static(Some "mailto:contact@example.com")
                Label = TextSource.Literal "Email us"
                Protection = Some LinkProtection.Email }
        ))
        None

let image: Node<obj> =
    // Phase 287 — Avatar variant exercises the variant DU; Src round-trips a
    // Binding<string> (sanitised at render, not at wire).
    //
    // Phase 1077 — this fixture deliberately carries NONE of the three
    // presentation slots, and its committed bytes are unchanged by that phase.
    // That is the acceptance criterion made executable: a pre-phase document
    // still encodes and decodes to exactly what it did, and the proof is a
    // fixture nobody had to touch.
    //
    // Phase 1078 carries no `Caption` either, for the same reason and with the
    // same evidential weight: the bytes below were emitted before the field
    // existed and are unchanged by its arrival.
    node
        "image-1"
        (NodeKind.Image(
            { Defaults.image with
                Src = Binding.Static(Some "/avatar.png")
                Alt = TextSource.Literal "User avatar"
                Variant = ImageVariant.Avatar }
        ))
        None

let imagePresentation: Node<obj> =
    // Phase 1077 — all three presentation slots at NON-default values, so the
    // fixture pins the emitted spellings (`fit` / `aspectRatio` / `loading`)
    // and their key order rather than only their absence. `Variant` is left at
    // `Default` on purpose: the presentation slots are orthogonal to the
    // variant, and a fixture that moved both at once could not tell a host
    // that conflated them from one that did not.
    node
        "image-presentation-1"
        (NodeKind.Image(
            { Defaults.image with
                Src = Binding.Static(Some "/hero.jpg")
                Alt = TextSource.Literal "The harbour at dawn"
                Fit = ImageFit.Cover
                AspectRatio = ImageAspect.SixteenNine
                Loading = ImageLoading.Lazy }
        ))
        None

let imageCaption: Node<obj> =
    // Phase 1078 — the caption at its simplest: a `Literal`. Everything else on
    // the record is left where `image-1` has it, so the diff between the two
    // fixtures' bytes is exactly one key. A fixture that also moved the variant
    // or a presentation slot would round-trip just as well and would prove less,
    // because a host that conflated `caption` with something else could still
    // pass it.
    node
        "image-caption-1"
        (NodeKind.Image(
            { Defaults.image with
                Src = Binding.Static(Some "/harbour.jpg")
                Alt = TextSource.Literal "Fishing boats moored at first light"
                Caption = Some(TextSource.Literal "The harbour at dawn, 1908. Oil on canvas.") }
        ))
        None

let imageCaptionI18n: Node<obj> =
    // Phase 1078 — the caption as an `I18n` TextSource. This is the fixture
    // that makes "i18n-capable" a corpus fact rather than a design intention:
    // the slot is a `TextSource`, so every case of that DU rides it, and a host
    // that quietly narrowed the slot to a string (the obvious shortcut, since
    // captions read like strings) fails HERE rather than in a user's locale.
    // The arg bag is populated on purpose — an empty one would not distinguish
    // a host that dropped `args` from one that carried it.
    node
        "image-caption-i18n-1"
        (NodeKind.Image(
            { Defaults.image with
                Src = Binding.Static(Some "/harbour.jpg")
                Alt = TextSource.Literal "Fishing boats moored at first light"
                Caption = Some(TextSource.I18n("gallery.caption.harbour", Map.ofList [ "year", JInt 1908 ])) }
        ))
        None

let imageSrcset: Node<obj> =
    // Phase 1080 — a three-candidate `srcSet`, authored DESCENDING by width.
    //
    // The order is the point of the fixture, not an accident of typing. The wire
    // preserves authored array order (a JSON array is ordered data; the canonical
    // encoder sorts object KEYS only), and the RENDERERS sort ascending when they
    // emit the `srcset` attribute. A fixture authored already-ascending would
    // round-trip identically whether the codec canonicalised order or left it
    // alone, and so could not tell the two designs apart. This one can: a codec
    // that sorted would fail the round-trip byte comparison here.
    //
    // Everything else is left where `image-caption-1` has it, so the byte
    // difference from that fixture is exactly the one slot.
    node
        "image-srcset-1"
        (NodeKind.Image(
            { Defaults.image with
                Src = Binding.Static(Some "/harbour.jpg")
                Alt = TextSource.Literal "Fishing boats moored at first light"
                SrcSet =
                    [ { Src = Binding.Static(Some "/harbour-1600.jpg")
                        Width = 1600 }
                      { Src = Binding.Static(Some "/harbour-800.jpg")
                        Width = 800 }
                      { Src = Binding.Static(Some "/harbour-400.jpg")
                        Width = 400 } ] }
        ))
        None

let imageExpandable: Node<obj> =
    // Phase 1079 — `expandable` alone, everything else where `image-caption-1`
    // has it, so the byte difference from that fixture is exactly one key. The
    // fixture is deliberately MINIMAL: what it certifies is that a host reads
    // and re-emits one boolean, and a fixture that also carried a caption and a
    // candidate list would let a host that conflated the three still pass.
    //
    // The renderers' obligation is not certifiable here and is not meant to be
    // — the corpus pins BYTES. That an `expandable` image emits a working
    // `<a href>` is pinned by the SSR-parity corpus, and the split is
    // deliberate: a wire fixture that asserted markup would be asserting one
    // host's rendering as the contract.
    node
        "image-expandable-1"
        (NodeKind.Image(
            { Defaults.image with
                Src = Binding.Static(Some "/harbour.jpg")
                Alt = TextSource.Literal "Fishing boats moored at first light"
                Expandable = true }
        ))
        None

let imageExpandableFigure: Node<obj> =
    // Phase 1079 — the COMPOSITION fixture: `expandable` + `caption` + `srcSet`
    // on one node, which is the gallery thumbnail the whole ImageSpec cluster
    // was built to express. It exists because the three slots interact at
    // render time in a way no single-slot fixture states:
    //
    //   - the `srcSet` candidates are renditions of the THUMBNAIL, sized for
    //     the layout box, while the primary `src` behind the expansion is the
    //     FULL asset — so the candidate widths here are deliberately all
    //     smaller than a full-size image would be;
    //   - the caption sits OUTSIDE the link target, which is the nesting the
    //     renderers implement (`<figure>` wraps `<a>` wraps `<img>`).
    //
    // On the wire all three are independent keys and the fixture proves only
    // that they round-trip together. That is worth a fixture anyway: three
    // omit-at-default slots present at once is the case where a host that
    // built its encoder as a chain of `if`s gets the key ORDER wrong, and
    // canonical ordering is what the byte comparison catches.
    node
        "image-expandable-figure-1"
        (NodeKind.Image(
            { Defaults.image with
                Src = Binding.Static(Some "/harbour.jpg")
                Alt = TextSource.Literal "Fishing boats moored at first light"
                Fit = ImageFit.Cover
                AspectRatio = ImageAspect.FourThree
                Loading = ImageLoading.Lazy
                SrcSet =
                    [ { Src = Binding.Static(Some "/harbour-400.jpg")
                        Width = 400 }
                      { Src = Binding.Static(Some "/harbour-800.jpg")
                        Width = 800 } ]
                Expandable = true
                Caption = Some(TextSource.Literal "The harbour at dawn, 1908. Oil on canvas.") }
        ))
        None

// ─── Media (Phase 1076) ─────────────────────────────────────────────────────
//
// Four fixtures, and between them they pin every polarity this kind carries.
// The wire cannot express the RENDER obligations — that autoplay never emits
// without muted, that Audio has no autoplay pathway — so those live in the
// SSR-parity corpus and the spec's normative text. What the bytes pin is the
// omit rules, which is the half a second host gets wrong silently.

/// The minimum: a video with only the mandatory slots. `controls` is at its
/// TRUE default and `loop` at its false one, so NEITHER appears, and the case
/// payload is the bare `{"$type":"Video"}` because `autoplay` omits at false
/// and `poster` is absent. A host that inverted either polarity produces
/// different bytes for this one fixture and for no other.
let mediaVideo: Node<obj> =
    node
        "media-video-1"
        (NodeKind.Media(
            { Defaults.media with
                Label = TextSource.Literal "Studio walkthrough"
                Src = Binding.Static(Some "/walkthrough.mp4") }
        ))
        None

/// The poster frame — the one video-only slot that is a URL. Everything else
/// sits where `media-video-1` has it, so the byte difference is exactly one
/// key, and that key is INSIDE the case object rather than beside it: a host
/// that hoisted the payload's members onto the spec would round-trip its own
/// emission perfectly and fail here.
let mediaVideoPoster: Node<obj> =
    node
        "media-video-poster-1"
        (NodeKind.Media(
            { Defaults.media with
                Kind = MediaKind.Video(false, Some(Binding.Static(Some "/walkthrough-poster.jpg")))
                Label = TextSource.Literal "Studio walkthrough"
                Src = Binding.Static(Some "/walkthrough.mp4") }
        ))
        None

/// All three booleans OFF their defaults at once, which is the case where a
/// host that built its encoder as a chain of `if`s gets the key ORDER wrong.
/// It also pins the two polarities against each other: `controls` emits
/// because it is FALSE, `loop` and `autoplay` because they are TRUE.
let mediaVideoAutoplay: Node<obj> =
    node
        "media-video-autoplay-1"
        (NodeKind.Media(
            { Defaults.media with
                Controls = false
                Kind = MediaKind.Video(true, None)
                Label = TextSource.Literal "Ambient loop"
                Loop = true
                Src = Binding.Static(Some "/ambient.mp4") }
        ))
        None

/// The Audio variant. Its payload is the discriminator and nothing else, which
/// is the point of the variant shape rather than a poverty of it: there is no
/// autoplay key to omit here because the slot does not exist on this case.
let mediaAudio: Node<obj> =
    node
        "media-audio-1"
        (NodeKind.Media(
            { Defaults.media with
                Kind = MediaKind.Audio
                Label = TextSource.Literal "Curator's commentary"
                Src = Binding.Static(Some "/commentary.mp3") }
        ))
        None

// ─── Media text tracks (Phase 1110) ─────────────────────────────────────────
//
// Three more, and they pin the two things the four above could not: that a
// REPEATED structured slot on this record encodes in authored order with its
// own key order, and that an optional `TextSource` on the spec is absent by
// default. The RENDER obligations these fixtures exist to be measured against —
// authored track order, one default per kind, the transcript disclosure — are
// again not expressible in bytes and live in the SSR corpus and the spec.

/// One captions track, elected as the default. `default` is off its `false`
/// identity so the key appears, and every other slot of the entry is required,
/// so this one fixture pins the whole `TrackEntry` key order in a single line.
let mediaVideoCaptions: Node<obj> =
    node
        "media-video-captions-1"
        (NodeKind.Media(
            { Defaults.media with
                Label = TextSource.Literal "Studio walkthrough"
                Src = Binding.Static(Some "/walkthrough.mp4")
                Tracks =
                    [ { Default = true
                        Kind = TrackKind.Captions
                        Label = TextSource.Literal "English captions"
                        Src = Binding.Static(Some "/walkthrough.en.vtt")
                        SrcLang = "en" } ] }
        ))
        None

/// Three tracks, authored in an order no sort would produce, with TWO captions
/// entries both electing themselves default. The wire carries the array exactly
/// as authored — a JSON array is ordered data and the canonical encoder sorts
/// object KEYS only — so this is the fixture that tells a host preserving the
/// authored order from one re-sorting it. `image-srcset-1` makes the opposite
/// point, and the pair is what makes each rule legible on its own.
///
/// The double default election is legal bytes: the decoder does not refuse it,
/// because a lenient host would render it anyway, and the render obligation
/// resolves it first-wins. A reject fixture here would have made the wire
/// stricter than every host that reads it.
let mediaVideoTracks2: Node<obj> =
    node
        "media-video-tracks-2"
        (NodeKind.Media(
            { Defaults.media with
                Label = TextSource.Literal "Harbour restoration, part two"
                Src = Binding.Static(Some "/restoration-2.mp4")
                Tracks =
                    [ { Default = false
                        Kind = TrackKind.Subtitles
                        Label = TextSource.Literal "Gàidhlig"
                        Src = Binding.Static(Some "/restoration-2.gd.vtt")
                        SrcLang = "gd" }
                      { Default = true
                        Kind = TrackKind.Captions
                        Label = TextSource.Literal "English captions"
                        Src = Binding.Static(Some "/restoration-2.en.vtt")
                        SrcLang = "en" }
                      { Default = true
                        Kind = TrackKind.Captions
                        Label = TextSource.Literal "English captions (verbose)"
                        Src = Binding.Static(Some "/restoration-2.en-verbose.vtt")
                        SrcLang = "en" } ] }
        ))
        None

/// The audio transcript floor. An `Audio` surface has no visual channel to hang
/// captions on, so a transcript IS its accessibility affordance — which is why
/// the slot sits on the spec rather than on the `Video` case. Every other media
/// fixture pins its omission at the same time.
let mediaAudioTranscript: Node<obj> =
    node
        "media-audio-transcript-1"
        (NodeKind.Media(
            { Defaults.media with
                Kind = MediaKind.Audio
                Label = TextSource.Literal "Curator's commentary"
                Src = Binding.Static(Some "/commentary.mp3")
                Transcript =
                    Some(
                        TextSource.Literal
                            "The harbour was rebuilt twice: once after the storm of 1908, and again in 1953."
                    ) }
        ))
        None


// ─── Sandboxed third-party embed (Phase 1111) ───────────────────────────────
//
// Three, and between them they pin every omit polarity this record has. The
// RENDER obligations they exist to be measured against — the always-emitted
// empty `sandbox`, the declaration-order token set, the omitted `src` on a
// refused source — are again not expressible in bytes and live in the SSR
// corpus and the spec.

/// The minimum: the two required slots and nothing else. `aspectRatio` is at
/// its `Natural` identity and `permissions` is EMPTY, so neither appears — and
/// the empty permission list is TOTAL DENIAL, which makes the wire-cheapest
/// embed also the most locked-down one. A host that emitted `"permissions":[]`
/// produces different bytes for this fixture and for no other.
let embedMinimal: Node<obj> =
    node
        "embed-1"
        (NodeKind.Embed(
            { Defaults.embed with
                Src = Binding.Static(Some "https://player.example/embed/harbour")
                Title = TextSource.Literal "Harbour restoration, part two" }
        ))
        None

/// The declared aspect ratio — the one slot this kind shares with `Image`, and
/// it shares the ENUM as well as the polarity. That reuse is what this fixture
/// pins: a host that minted a parallel `EmbedAspect` with the same case names
/// round-trips its own emission perfectly and diverges from the schema, where
/// the slot `$ref`s `ImageAspect`.
let embedAspect: Node<obj> =
    node
        "embed-aspect-1"
        (NodeKind.Embed(
            { Defaults.embed with
                AspectRatio = ImageAspect.SixteenNine
                Src = Binding.Static(Some "https://player.example/embed/harbour")
                Title = TextSource.Literal "Harbour restoration, part two" }
        ))
        None

/// One declared permission. Deliberately `AllowFullscreen` rather than
/// `AllowScripts`: it is the case that does NOT ride the `sandbox` attribute,
/// so a host that mapped the whole enum onto sandbox tokens passes every other
/// fixture and fails its render obligation on this one. It is also the single
/// permission that raises no pre-emit warning, so the fixture is a document an
/// author can ship unaltered.
let embedPermissions: Node<obj> =
    node
        "embed-permissions-1"
        (NodeKind.Embed(
            { Defaults.embed with
                Permissions = [ EmbedPermission.AllowFullscreen ]
                Src = Binding.Static(Some "https://player.example/embed/harbour")
                Title = TextSource.Literal "Harbour restoration, part two" }
        ))
        None


// ─── Tree — recursive disclosure with tree semantics (Phase 1120) ─────────
//
// Three, and between them they pin the recursion, both omit polarities and both
// State keys. The BEHAVIOUR these exist to be measured against — the roving
// tabindex, the six key bindings, the two-press `Right` — is not expressible in
// bytes at all, which is the ordinary shape for a kind whose irreducibility is
// behavioural: the corpus pins the document, the render-obligation corpus and
// the spec pin what a host must DO with it.

/// A two-level hierarchy, and the shape most trees actually are: no State key
/// named at all, so nothing toggles and every row is shown.
///
/// It pins the leaf omission, which is the byte-level decision this record's
/// design turns on. `Cocoa` and `Yarn` carry no `children` key, because the
/// slot omits at the empty list — a host that emitted `"children":[]` on a leaf
/// produces different bytes for most of a real file listing.
let treeStatic: Node<obj> =
    node
        "tree-1"
        (NodeKind.Tree(
            { Defaults.tree with
                Items =
                    [ { Defaults.treeItem with
                          Children =
                              [ { Defaults.treeItem with
                                    Id = "cocoa"
                                    Label = TextSource.Literal "Cocoa" }
                                { Defaults.treeItem with
                                    Id = "yarn"
                                    Label = TextSource.Literal "Yarn" } ]
                          Id = "goods"
                          Label = TextSource.Literal "Goods" }
                      { Defaults.treeItem with
                          Id = "ledger"
                          Label = TextSource.Literal "Ledger" } ] }
        ))
        None

/// The expansion affordance, which is a NAMED KEY and not a flag. Pins the
/// grid-behaviour cluster's governing ruling at the wire level: there is no
/// `expandable` member to omit here, because none exists — the key IS the
/// affordance, and a host looking for a boolean will not find one to read.
///
/// Three levels, so the recursion is exercised past the depth a single nesting
/// would prove, and one row carries an `icon` so the third optional slot is not
/// left unpinned by the family.
let treeExpandedKeyed: Node<obj> =
    node
        "tree-expanded-1"
        (NodeKind.Tree(
            { Defaults.tree with
                ExpandedStateKey = Some "openRows"
                Items =
                    [ { Defaults.treeItem with
                          Children =
                              [ { Defaults.treeItem with
                                    Children =
                                        [ { Defaults.treeItem with
                                              Id = "manifest"
                                              Label = TextSource.Literal "Manifest" } ]
                                    Id = "1823"
                                    Label = TextSource.Literal "1823" } ]
                          Icon = Some "folder"
                          Id = "archive"
                          Label = TextSource.Literal "Archive" } ] }
        ))
        None

/// The selection affordance, alongside a handler. Both are declared, which is
/// the shape the renderer treats as TWO effects rather than a choice: the key is
/// written and the handler runs. `onSelect` rides as the `"<closure>"` sentinel,
/// so this fixture also pins that a decoded tree keeps a working selection
/// through the key alone — the behaviour a `Partial` survivability verdict
/// promises.
let treeSelectionKeyed: Node<obj> =
    node
        "tree-selection-1"
        (NodeKind.Tree(
            { Defaults.tree with
                Items =
                    [ { Defaults.treeItem with
                          Id = "harbour"
                          Label = TextSource.Literal "Harbour" }
                      { Defaults.treeItem with
                          Id = "quay"
                          Label = TextSource.Literal "Quay" } ]
                OnSelect = Some(fun _ -> Action.Chain [])
                SelectionStateKey = Some "selectedRow" }
        ))
        None

let listDisplay: Node<obj> =
    // Phase 287 — ordered list with two items.
    node
        "list-1"
        (NodeKind.List(
            { Items = [ TextSource.Literal "First"; TextSource.Literal "Second" ]
              Ordered = true }
        ))
        None

let toast: Node<obj> =
    // Phase 289 — Success tone, open + dismissable. Open round-trips a
    // Binding<bool>.
    node
        "toast-1"
        (NodeKind.Toast(
            { Defaults.toast with
                Message = TextSource.Literal "Saved"
                Tone = ToneVariant.Success
                Open = Binding.Static(Some true) }
        ))
        None

let codeBlock: Node<obj> =
    // Phase 290 — line numbers on, two highlight lines, copyable. Exercises the
    // int-array `highlightLines` field.
    node
        "code-1"
        (NodeKind.CodeBlock(
            { Defaults.codeBlock with
                Code = "let x = 1\nlet y = 2"
                Language = "fsharp"
                LineNumbers = true
                HighlightLines = [ 1; 2 ] }
        ))
        None

let math: Node<obj> =
    // Phase 293 — block display LaTeX.
    node
        "math-1"
        (NodeKind.Math(
            { Source = "x^2 + y^2 = z^2"
              Display = MathDisplay.Block }
        ))
        None

let sparkline: Node<obj> =
    node "spark-1" (NodeKind.Sparkline({ Source = Binding.Static(Some [ 1.0; 2.0; 3.0; 2.0; 4.0 ]) })) None

let private bareDrawStyle: DrawStyle = Defaults.drawStyle

let private styledDraw (fill: string) (stroke: string) : DrawStyle =
    { bareDrawStyle with
        Fill = Some(Binding.Static(Some fill))
        Stroke = Some(Binding.Static(Some stroke))
        StrokeWidth = Some(Binding.Static(Some 1.5))
        Opacity = Some(Binding.Static(Some 0.9)) }

/// A `Label` text style exercising the Phase 528.1 fields (anchor / size /
/// weight / font-family) — pins them cross-host in the corpus.
let private labelTextStyle: DrawStyle =
    { bareDrawStyle with
        Fill = Some(Binding.Static(Some "#111111"))
        TextAnchor = Some TextAnchor.Middle
        FontSize = Some 14.0
        Emphasis = Some Emphasis.Loud
        FontFamily = Some "system-ui, sans-serif" }

let drawing: Node<obj> =
    // Phase 524 — exercises Group nesting + every Shape case + all CurveCommand
    // variants + typed DrawStyle Bindings (Static colours/widths) + Title/Desc.
    node
        "drawing-1"
        (NodeKind.Drawing(
            { Defaults.drawing with
                ViewBox =
                    { MinX = 0.0
                      MinY = 0.0
                      Width = 200.0
                      Height = 100.0 }
                Shapes =
                    [ Shape.Rectangle(10.0, 10.0, 80.0, 40.0, Some 4.0, styledDraw "#3366cc" "#102040")
                      Shape.Line(0.0, 0.0, 200.0, 100.0, bareDrawStyle)
                      Shape.Polyline(
                          [ { X = 0.0; Y = 0.0 }; { X = 10.0; Y = 20.0 }; { X = 20.0; Y = 5.0 } ],
                          bareDrawStyle
                      )
                      Shape.Polygon(
                          [ { X = 100.0; Y = 10.0 }; { X = 120.0; Y = 30.0 }; { X = 90.0; Y = 40.0 } ],
                          styledDraw "#cc6633" "#402010"
                      )
                      Shape.Curve(
                          [ CurveCommand.MoveTo { X = 0.0; Y = 0.0 }
                            CurveCommand.LineTo { X = 10.0; Y = 10.0 }
                            CurveCommand.CubicTo({ X = 20.0; Y = 0.0 }, { X = 30.0; Y = 20.0 }, { X = 40.0; Y = 10.0 })
                            CurveCommand.QuadraticTo({ X = 50.0; Y = 0.0 }, { X = 60.0; Y = 10.0 })
                            CurveCommand.Close ],
                          bareDrawStyle
                      )
                      Shape.Circle(150.0, 50.0, 20.0, styledDraw "#33aa55" "#0a2010")
                      Shape.Ellipse(50.0, 80.0, 30.0, 15.0, bareDrawStyle)
                      Shape.Label(100.0, 90.0, TextSource.Literal "Revenue", labelTextStyle)
                      Shape.Group(
                          [ Shape.Circle(5.0, 5.0, 2.0, bareDrawStyle)
                            Shape.Line(0.0, 0.0, 10.0, 10.0, bareDrawStyle) ],
                          styledDraw "#999999" "#333333"
                      ) ]
                Style = styledDraw "#ffffff" "#000000"
                Title = Some(TextSource.Literal "Quarterly revenue chart")
                Description = Some(TextSource.Literal "A bar and line chart of revenue by quarter.") }
        ))
        None

let drawingMinimal: Node<obj> =
    // The degenerate drawing — empty shape list, all-default root style, no
    // title/description (the byte-minimal Drawing wire shape).
    node
        "drawing-empty"
        (NodeKind.Drawing(
            { Defaults.drawing with
                Style = bareDrawStyle }
        ))
        None

let drawingRotatedLabels: Node<obj> =
    // Phase 877 — `Label` rotation. Pins the optional `rotation` field
    // cross-host over the three demands that motivated it, each with a
    // different sign / magnitude / anchor pairing, plus the cases where the
    // encoding is easy to get subtly wrong:
    //
    //   * a tilted category label      — -30°, Middle-anchored (the legibility
    //                                    default of the chart-style doctrine);
    //   * a vertical escalation        — -90°, End-anchored (the crowded case);
    //   * a rotated y-axis title       — +90° (opposite sign — pins that the
    //                                    convention is clockwise, not absolute);
    //   * a 2-dp fractional angle      — 12.34°, exercising the canonical
    //                                    rounding form on the wire (a whole
    //                                    number would let a host that truncates
    //                                    to integers pass by accident);
    //   * an explicit 0°               — PRESENT-and-zero is not the same wire
    //                                    shape as ABSENT, and a host that
    //                                    conflates them (omit-when-falsy) round-
    //                                    trips to different bytes. The whole
    //                                    "absent = byte-unchanged" guarantee
    //                                    rests on that distinction;
    //   * a negative fractional angle  — -0.5°, the sign × fraction combination.
    //
    // The unrotated label at the end keeps an omit-the-field shape in the same
    // fixture, so a host that emits `"rotation":null` fails here rather than
    // silently in some later chart.
    let rot (deg: float) (anchor: TextAnchor) : DrawStyle =
        { labelTextStyle with
            TextAnchor = Some anchor
            Rotation = Some deg }

    node
        "drawing-rotated-labels"
        (NodeKind.Drawing(
            { Defaults.drawing with
                ViewBox =
                    { MinX = 0.0
                      MinY = 0.0
                      Width = 200.0
                      Height = 120.0 }
                Shapes =
                    [ Shape.Label(30.0, 100.0, TextSource.Literal "Q1 2026", rot -30.0 TextAnchor.Middle)
                      Shape.Label(70.0, 100.0, TextSource.Literal "Q2 2026", rot -90.0 TextAnchor.End)
                      Shape.Label(8.0, 60.0, TextSource.Literal "Revenue", rot 90.0 TextAnchor.Middle)
                      Shape.Label(110.0, 100.0, TextSource.Literal "Fractional", rot 12.34 TextAnchor.Start)
                      Shape.Label(150.0, 100.0, TextSource.Literal "Explicit zero", rot 0.0 TextAnchor.Middle)
                      Shape.Label(180.0, 100.0, TextSource.Literal "Hairline", rot -0.5 TextAnchor.End)
                      Shape.Label(100.0, 20.0, TextSource.Literal "Upright", labelTextStyle) ]
                Style = bareDrawStyle
                Title = Some(TextSource.Literal "Rotated axis labels") }
        ))
        None

let drawingTippedShapes: Node<obj> =
    // Phase 883 — `DrawStyle.Tip`, the per-mark hover readout an emitter turns
    // into an SVG `<title>` CHILD of the shape's own element. This fixture pins
    // the field cross-host over the things that are easy to get subtly wrong:
    //
    //   * EVERY SHAPE, not just `Label`. Unlike the Phase 528.1 text cluster (and
    //     unlike `Rotation`, which would move geometry off `Label`), a tip is
    //     meaningful and inert on every shape — the marks a reader hovers are
    //     bars, wedges and points. Rectangle / Circle / Curve / Polyline / Group
    //     / Label all carry one here, so a host that wired the field into the
    //     Label arm alone fails on the second shape rather than silently in some
    //     later chart.
    //   * A LITERAL AND A BOUND tip. The slot is a full `TextSource`, so the
    //     canonical bare-string `Literal` form AND the tagged `Bound` envelope
    //     both have to survive; a host that special-cased "tip is a string"
    //     fails on the second.
    //   * THE MIDDLE-DOT SEPARATOR (U+00B7) in the chart lowering's own
    //     "Series · Category · value" shape — a non-ASCII character in a slot
    //     every host escapes and re-encodes.
    //   * HOSTILE TEXT. The tip is written into XML TEXT CONTENT and its source
    //     is a category string straight off an untrusted data feed, so the
    //     fixture carries a would-be script tag with all five escapable
    //     characters (`& < > " '`). This is a CODEC fixture, so what it pins is
    //     that the round-trip does not mangle those characters; the ESCAPING is
    //     the emitter's obligation and is tested in each host's renderer suite.
    //   * AN EXPLICITLY EMPTY tip. Present-and-empty is not the same wire shape
    //     as ABSENT, and `if (tip)` / `if tip:` is the natural — and wrong — test
    //     in the JavaScript and Python hosts. A host that omits it round-trips to
    //     different bytes, exactly as an omitted explicit `rotation: 0` would.
    //
    // The untipped shape at the end keeps an omit-the-field shape in the same
    // fixture, so a host that emits `"tip":null` fails here.
    let tipped (t: string) : DrawStyle =
        { bareDrawStyle with
            Fill = Some(Binding.Static(Some "#3366cc"))
            Tip = Some(TextSource.Literal t) }

    node
        "drawing-tipped-shapes"
        (NodeKind.Drawing(
            { Defaults.drawing with
                ViewBox =
                    { MinX = 0.0
                      MinY = 0.0
                      Width = 200.0
                      Height = 120.0 }
                Shapes =
                    [ Shape.Rectangle(10.0, 40.0, 30.0, 60.0, None, tipped "revenue · Q1 2026 · 1,234,567.89")
                      Shape.Circle(70.0, 60.0, 5.0, tipped "revenue · Q2 2026 · -0.5%")
                      Shape.Curve(
                          [ CurveCommand.MoveTo { X = 100.0; Y = 60.0 }
                            CurveCommand.LineTo { X = 130.0; Y = 60.0 }
                            CurveCommand.Close ],
                          tipped "share · Other · £42.00"
                      )
                      Shape.Polyline(
                          [ { X = 140.0; Y = 20.0 }; { X = 160.0; Y = 80.0 } ],
                          { bareDrawStyle with
                              Stroke = Some(Binding.Static(Some "#cc6633"))
                              // A SERIES-level mark names the series and nothing
                              // else: one element carries the whole line, so a
                              // single `<title>` cannot honestly report one point.
                              Tip = Some(TextSource.Literal "revenue") }
                      )
                      Shape.Group(
                          [ Shape.Circle(170.0, 100.0, 3.0, bareDrawStyle) ],
                          { bareDrawStyle with
                              Tip = Some(TextSource.Bound(Binding.Static(Some "resolved at render time"))) }
                      )
                      Shape.Label(
                          100.0,
                          110.0,
                          TextSource.Literal "Hover me",
                          { labelTextStyle with
                              Tip = Some(TextSource.Literal "<script>alert(\"xss\") & 'done'</script>") }
                      )
                      Shape.Ellipse(
                          30.0,
                          110.0,
                          6.0,
                          3.0,
                          { bareDrawStyle with
                              Tip = Some(TextSource.Literal "") }
                      )
                      Shape.Line(0.0, 0.0, 200.0, 0.0, bareDrawStyle) ]
                Style = bareDrawStyle
                Title = Some(TextSource.Literal "Tipped marks") }
        ))
        None


// ─── Non-finite sentinel fixtures (Phase 1063) ───────────────────────────
//
// `WIRE_FORMAT.md` §5 requires every host to EMIT the quoted `"NaN"` /
// `"Infinity"` / `"-Infinity"` sentinels for a non-finite number, and §7
// requires a decoder to ACCEPT them back **at a float slot**. Until Phase 1062
// that property held on some hosts and not others, so `decode → encode →
// decode` did not close on a document carrying a non-finite number and one
// host's canonical output was undecodable on another. §20's matrix note called
// it a round-trip hole; a Go fuzz leg then reached it unprompted within ~1,500
// generated inputs. These three fixtures are what stops it reopening silently.
//
// THREE fixtures, not one, and the reason is the per-slot-class measurement
// Phase 1062 took: the two lagging hosts each accepted the sentinels at SOME
// float slots and not others (Go's `floatStatic` accepted where its
// `expectNumber` refused), so "the host accepts sentinels" was never a
// well-formed claim. One fixture per distinct decoder path:
//
//   * a TYPED FLOAT SCALAR, including one inside a nested shape — the path most
//     hosts route through a single plain-number choke point serving ~35 slots;
//   * a float SEQUENCE ELEMENT — the same choke point on most hosts, a separate
//     one on some, and the case a scalar fixture cannot reach;
//   * a float behind a BINDING's `Static` ENVELOPE — the one class every host
//     already handled, kept precisely so a regression there is visible rather
//     than assumed impossible.
//
// These are ACCEPT cases. The counterexample policy's usual "land a reject
// fixture" does not apply: the defect was a conformant document being REFUSED,
// so the pin is that it decodes. The integer boundary is pinned separately by
// the corpus's two existing integer controls, which must keep refusing.

/// All three sentinels at typed float scalars (the `viewBox`), plus one at a
/// nested shape coordinate (`Circle.cx`). A host whose float guard sits at the
/// spec-record level but not inside `shapes` passes the first and fails the
/// second, which is why both are in one fixture.
let drawingNonfiniteSentinels: Node<obj> =
    node
        "drawing-nonfinite-sentinels"
        (NodeKind.Drawing(
            { Defaults.drawing with
                ViewBox =
                    { MinX = System.Double.NegativeInfinity
                      MinY = System.Double.NaN
                      Width = System.Double.PositiveInfinity
                      Height = 120.0 }
                Shapes = [ Shape.Circle(System.Double.NaN, 50.0, 20.0, bareDrawStyle) ]
                Style = bareDrawStyle
                Title = Some(TextSource.Literal "Non-finite sentinels at typed float slots") }
        ))
        None

/// A float SEQUENCE element. All three sentinels sit among finite neighbours,
/// so a host that special-cases a wholly-non-finite array — or that decides the
/// element type from the first element — is caught rather than flattered.
let sparklineNonfiniteSentinel: Node<obj> =
    node
        "spark-nonfinite-sentinel"
        (NodeKind.Sparkline(
            { Source =
                Binding.Static(
                    Some
                        [ 1.0
                          System.Double.NaN
                          3.0
                          System.Double.PositiveInfinity
                          System.Double.NegativeInfinity
                          5.0 ]
                ) }
        ))
        None

/// A float behind a `Binding.Static` envelope, at two slots of one spec
/// (`value` and `trend`). The envelope path is the one class that was already
/// conformant on all five hosts when the hole was measured — pinned so that
/// stays a fact rather than an assumption.
let metricNonfiniteSentinel: Node<obj> =
    node
        "metric-nonfinite-sentinel"
        (NodeKind.Metric(
            { metricSpec with
                Value = Binding.Static(Some System.Double.PositiveInfinity)
                Trend = Some(Binding.Static(Some System.Double.NaN)) }
        ))
        None

let skeleton: Node<obj> = node "skel-1" (NodeKind.Skeleton({ Rows = 3 })) None

let callout: Node<obj> =
    node
        "callout-1"
        (NodeKind.Callout(
            { Defaults.callout with
                Tone = ToneVariant.Warning
                Heading = Some(TextSource.Literal "Heads up")
                Body = TextSource.Literal "Live data is delayed."
                Icon = Some "alert"
                Dismissable = true }
        ))
        None

let progress: Node<obj> =
    node
        "progress-1"
        (NodeKind.Progress(
            { Defaults.progress with
                Fraction = Binding.Static(Some 0.42)
                Label = Some(TextSource.Literal "Loading...")
                Tone = ToneVariant.Brand }
        ))
        None

let labelValueRow: Node<obj> =
    node
        "lvr-1"
        (NodeKind.LabelValueRow(
            { Defaults.labelValueRow with
                Label = TextSource.Literal "Total"
                Value = Binding.Static(Some 42.0)
                Format = CellFormat.Number(Some 2)
                Emphasis = true
                Help = Some(TextSource.Literal "Last 30 days") }
        ))
        None

let fact: Node<obj> =
    // Exercises the full optional surface (tone / emphasis / help / icon) —
    // the minimal {label,value} form is pinned by the lenient fixture family.
    node
        "fact-1"
        (NodeKind.Fact(
            { Defaults.fact with
                Label = TextSource.Literal "Patient"
                Value = TextSource.Literal "Alice Smith"
                Icon = Some "user"
                Tone = ToneVariant.Brand
                Emphasis = true
                Help = Some(TextSource.Literal "Primary insured") }
        ))
        None

/// Phase 819 — a duration Metric: the raw value counts MINUTES (80 → "1h
/// 20m" under Compact), and the trend slot carries the cell-vocabulary
/// RelativeTime parity case (a signed count of the unit).
let metricDuration: Node<obj> =
    node
        "metric-duration-1"
        (NodeKind.Metric(
            { Defaults.metric with
                Label = TextSource.Literal "Avg wait"
                Value = Binding.Static(Some 80.0)
                Format = CellFormat.Duration(DurationUnit.Minutes, DurationStyle.Compact)
                Trend = Some(Binding.Static(Some(-3.0)))
                TrendFormat = Some(CellFormat.RelativeTime RelativeTimeUnit.Minute)
                // Phase 867 - left at the default DELIBERATELY. A wait time is the
                // canonical `LowerIsBetter` quantity, so flipping it here would be
                // the natural edit - and it would move a pre-867 fixture's bytes,
                // which is the one thing the byte-unchanged assertion exists to
                // catch. The inverted case gets its OWN fixture below.
                TrendPolarity = TrendPolarity.HigherIsBetter }
        ))
        None

/// Phase 867 — the inverted-polarity metric: a falling wait time is an
/// IMPROVEMENT, and this is the only fixture in the corpus that says so. It
/// pins the one byte the slot exists to carry (`"trendPolarity":
/// "LowerIsBetter"`), so a host that decodes the field into nothing, or drops
/// it on re-encode, fails here rather than in a renderer nobody ran.
///
/// Its neighbour `metric-duration-1` is the same quantity WITHOUT the
/// declaration, deliberately: the pair is the whole demand — the same falling
/// number, read as an improvement under the declaration and as a regression
/// without it, with the numeric text identical in both.
let metricInvertedPolarity: Node<obj> =
    node
        "metric-inverted-polarity"
        (NodeKind.Metric(
            { Defaults.metric with
                Label = TextSource.Literal "Avg wait"
                Value = Binding.Static(Some 80.0)
                Format = CellFormat.Duration(DurationUnit.Minutes, DurationStyle.Compact)
                Tone = ToneVariant.Warning
                Trend = Some(Binding.Static(Some(-0.0734)))
                TrendFormat = Some(CellFormat.Percent(Some 2))
                TrendPolarity = TrendPolarity.LowerIsBetter
                // `tone` says the reading STANDS badly; the polarity says the
                // quantity is IMPROVING. Both at once, on one node, is exactly
                // the case a single `tone` slot could never express.
                Subtext = Some(TextSource.Literal "still above target") }
        ))
        None

/// Phase 821 — the standalone icon-only display kind, decorative form: no
/// label (renderers emit `aria-hidden="true"`), non-default `size` so the
/// optional key appears on the wire, default tone omitted.
let iconDecorative: Node<obj> =
    node
        "icon-1"
        (NodeKind.Icon(
            { Icon = "sparkles"
              Size = IconSize.Large
              Tone = ToneVariant.Default
              Label = None }
        ))
        None

// ─── Layout fixtures ─────────────────────────────────────────────────────

let dashboardEmpty: Node<obj> =
    node
        "dash-empty"
        (NodeKind.Box(
            { Layout = BoxLayout.Auto
              Role = BoxRole.Dashboard
              Heading = None
              Children = []
              KeepTogether = false
              BreakBefore = false }
        ))
        None

/// Phase 1472 — the motivating document, whole: a right-to-left container
/// holding prose in that direction and ONE value that reads the other way.
///
/// The reference number is not styled — it is DECLARED. Without the
/// declaration the bidirectional algorithm resolves the run from the
/// surrounding context and the reader reads its digits back in the wrong
/// order, which is a correctness failure rather than a presentational one
/// (WCAG 1.3.2, Meaningful Sequence). It is also a statement only this
/// document can make: a host is handed the string and cannot know that it is
/// an opaque identifier rather than more prose.
///
/// Every `style` here carries exactly one member, for the reason above.
let styleDirectionIsolatedValue: Node<obj> =
    { node
          "style-direction-isolated-1"
          (NodeKind.Box(
              { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
                Role = BoxRole.Group
                Heading = None
                Children =
                  [ node
                        "style-direction-prose-1"
                        (NodeKind.Markdown({ Text = TextSource.Literal "מספר האסמכתא שלך הוא" }))
                        None
                    { node
                          "style-direction-reference-1"
                          (NodeKind.Badge(
                              { Label = TextSource.Literal "RR123456789IL"
                                Variant = BadgeVariant.Neutral }
                          ))
                          None with
                        Style =
                            Some
                                { defaultStyle with
                                    Direction = TextDirection.Ltr } } ]
                KeepTogether = false
                BreakBefore = false }
          ))
          None with
        Style =
            Some
                { defaultStyle with
                    Direction = TextDirection.Rtl } }

/// Phase 1473 — the motivating document for `keepTogether`: a totals block that
/// reads wrong when halved.
///
/// Exactly ONE new member is set, and that is the point. A fixture that also
/// declared `breakBefore` could decode with `keepTogether` silently dropped and
/// still round-trip, proving nothing about the slot it was written for. The
/// fact declared here is not available to the host any other way: a formatter
/// laying out pages sees three boxes, and nothing in the rendering says these
/// three lines are one thing.
let boxKeepTogether: Node<obj> =
    node
        "box-keep-together-1"
        (NodeKind.Box(
            { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
              Role = BoxRole.Card
              Heading = Some(TextSource.Literal "Totals")
              Children =
                [ node "box-keep-together-net-1" (NodeKind.Markdown({ Text = TextSource.Literal "Net 1,200.00" })) None
                  node "box-keep-together-vat-1" (NodeKind.Markdown({ Text = TextSource.Literal "VAT 240.00" })) None
                  node
                      "box-keep-together-gross-1"
                      (NodeKind.Markdown({ Text = TextSource.Literal "Gross 1,440.00" }))
                      None ]
              KeepTogether = true
              BreakBefore = false }
        ))
        None

/// Phase 1473 — `breakBefore`, alone, on its own vector for the same
/// one-member reason. A section that starts on a fresh page; nothing here names
/// the page, only the boundary before this subtree.
let boxBreakBefore: Node<obj> =
    node
        "box-break-before-1"
        (NodeKind.Box(
            { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
              Role = BoxRole.Group
              Heading = None
              Children =
                [ node "box-break-before-body-1" (NodeKind.Markdown({ Text = TextSource.Literal "Appendix A" })) None ]
              KeepTogether = false
              BreakBefore = true }
        ))
        None

let stack: Node<obj> =
    node
        "stack-1"
        (NodeKind.Box(
            { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
              Role = BoxRole.Group
              Heading = None
              Children = [ metric; markdown ]
              KeepTogether = false
              BreakBefore = false }
        ))
        None

let gridLayout: Node<obj> =
    node
        "glayout-1"
        (NodeKind.Box(
            { Layout = BoxLayout.Grid(12, None, None)
              Role = BoxRole.Group
              Heading = None
              Children = [ metric ]
              KeepTogether = false
              BreakBefore = false }
        ))
        None

// Irregular-grid round-trip fixture exercising
// the additive `TemplateColumns` escape. Three sibling fixtures cover
// the canonical CSS-grammar shapes the migration doc names: simple ratio
// `1fr 2fr`, fixed-plus-flex `100px repeat(3, minmax(30px, 1fr))`, and
// auto-fit `repeat(auto-fit, minmax(150px, 1fr))`.
let gridLayoutTemplatedRatio: Node<obj> =
    node
        "glayout-tpl-ratio"
        (NodeKind.Box(
            { Layout = BoxLayout.Grid(2, Some "1fr 2fr", None)
              Role = BoxRole.Group
              Heading = None
              Children = [ metric ]
              KeepTogether = false
              BreakBefore = false }
        ))
        None

let gridLayoutTemplatedFixedPlusFlex: Node<obj> =
    node
        "glayout-tpl-fixed"
        (NodeKind.Box(
            { Layout = BoxLayout.Grid(4, Some "100px repeat(3, minmax(30px, 1fr))", None)
              Role = BoxRole.Group
              Heading = None
              Children = [ metric ]
              KeepTogether = false
              BreakBefore = false }
        ))
        None

let gridLayoutTemplatedAutoFit: Node<obj> =
    node
        "glayout-tpl-autofit"
        (NodeKind.Box(
            { Layout = BoxLayout.Grid(1, Some "repeat(auto-fit, minmax(150px, 1fr))", None)
              Role = BoxRole.Group
              Heading = None
              Children = [ metric ]
              KeepTogether = false
              BreakBefore = false }
        ))
        None

// Phase 1082 — column-fill. Two children rather than the grid fixtures' one:
// masonry is a statement about how children DISTRIBUTE between columns, and a
// single-child container cannot exhibit it. The `Masonry` case carries `cols`
// and `gap` and no `templateColumns`, so these two fixtures are its complete
// field surface — the second exists because `gap` is omitted-when-None and
// would otherwise never appear on the wire, which is exactly the gap the IDL's
// own comment records against the `Flex` and `Grid` cases.
let masonryLayout: Node<obj> =
    node
        "masonry-1"
        (NodeKind.Box(
            { Layout = BoxLayout.Masonry(3, None)
              Role = BoxRole.Group
              Heading = None
              Children = [ metric; markdown ]
              KeepTogether = false
              BreakBefore = false }
        ))
        None

let masonryLayoutGap: Node<obj> =
    node
        "masonry-gap"
        (NodeKind.Box(
            { Layout = BoxLayout.Masonry(4, Some 16)
              Role = BoxRole.Group
              Heading = None
              Children = [ metric; markdown ]
              KeepTogether = false
              BreakBefore = false }
        ))
        None

let splitPanel: Node<obj> =
    node
        "split-1"
        (NodeKind.SplitPanel(
            { Weight = 0.6
              Children = [ metric; markdown ] }
        ))
        None

let tabs: Node<obj> =
    node
        "tabs-1"
        (NodeKind.Tabs(
            { Defaults.tabs with
                Children = [ metric ]
                // `Some` (Phase 426) — keeps the `"onSelect":"<closure>"`
                // sentinel on the wire, so this fixture stays byte-identical
                // to the pre-426 corpus (the closure-authored shape).
                OnSelect = Some(fun _ -> Action.Chain []) }
        ))
        None

// Tabs with TabHeaders + TabTags + ActiveTag wire
// surface. Exercises the additive optional fields end-to-end. The two
// children are simple Markdown leaves; the headers carry mixed icon /
// disabled state to exercise the full TabHeader record.
let tabsExplicitHeaders: Node<obj> =
    node
        "tabs-explicit-1"
        (NodeKind.Tabs(
            { Defaults.tabs with
                Children = [ markdown; sparkline ]
                // Non-zero ActiveIndex (Phase 126) — exercises the
                // now-carried selected-tab binding round-tripping a value
                // other than the default 0.
                ActiveIndex = Binding.Static(Some 1)
                OnSelect = Some(fun _ -> Action.Chain [])
                TabHeaders =
                    Some
                        [ { Label = TextSource.Literal "Overview"
                            Icon = Some "overview-glyph"
                            Disabled = Option.None }
                          { Label = TextSource.Literal "Detail"
                            Icon = Option.None
                            Disabled = Some(Binding.Static(Some false)) } ]
                TabTags = Some [ "overview"; "detail" ]
                ActiveTag = Some(Binding.Static(Some "overview")) }
        ))
        None

/// Phase 841 — the composite structural-wrap fixture.
///
/// The rule→fixture coverage matrix (`docs/tools/coverage-matrix.json`) found
/// `idiom:container-in-wrapper` exercised by ZERO fixtures in the whole corpus: no
/// tree anywhere puts a container INSIDE a wrapper, so the commonest screen shape
/// there is — a tabbed view whose panels are cards — had nothing an author could copy,
/// and every wrapper fixture held bare leaves.
///
/// It is deliberately dense rather than minimal: one tree absorbs three thinly-covered
/// classes so it can replace several sparse singles. Containers nested inside a `Tabs`
/// wrapper; an explicit `Grid` box layout beside a `Flex` one; and controls arriving
/// PRE-FILLED — a `Static` value on both the text and the choice field, which is the
/// shape a prompt naming a default asks for and which no existing wrapper fixture
/// shows. Every optional handler slot is omitted: this is the self-wiring emission
/// shape, not the closure-authored one.
let compositeTabsPanels: Node<obj> =
    let nameField: FormField<obj> =
        { Defaults.formField with
            Id = "displayName"
            Label = TextSource.Literal "Display name"
            Kind = FormFieldKind.Text(Some(Binding.Static(Some "Ada Lovelace")), Option.None)
            Required = true }

    let themeField: FormField<obj> =
        { Defaults.formField with
            Id = "theme"
            Label = TextSource.Literal "Theme"
            Kind =
                FormFieldKind.Choice(
                    Binding.Static(Some [ { Value = "light"; Label = "Light" }; { Value = "dark"; Label = "Dark" } ]),
                    Some(Binding.Static(Some "dark")),
                    Option.None
                )
            Required = true }

    let preferencesForm: Node<obj> =
        node
            "preferences-form"
            (NodeKind.Form(
                { Defaults.form with
                    Fields = [ nameField; themeField ]
                    OnSubmit = Action.Call("/api/preferences", Option.None, Option.None)
                    SubmitLabel = TextSource.Literal "Save preferences" }
            ))
            None

    let overviewPanel: Node<obj> =
        node
            "overview-panel"
            (NodeKind.Box(
                { Layout = BoxLayout.Grid(2, Option.None, Some 16)
                  Role = BoxRole.Card
                  Heading = Some(TextSource.Literal "This month")
                  // Sparkline + Badge rather than the fat shared `metric`: this tree is
                  // meant to SUPERSEDE `tabs-explicit-1` as the pack's Tabs exemplar, so
                  // it has to carry that fixture's leaf vocabulary, and Metric is already
                  // exemplified several times over while Sparkline is exemplified once.
                  Children = [ sparkline; badge ]
                  KeepTogether = false
                  BreakBefore = false }
            ))
            None

    let settingsPanel: Node<obj> =
        node
            "settings-panel"
            (NodeKind.Box(
                { Layout = BoxLayout.Flex(Orientation.Vertical, false, Some 12)
                  Role = BoxRole.Card
                  Heading = Some(TextSource.Literal "Preferences")
                  Children = [ preferencesForm ]
                  KeepTogether = false
                  BreakBefore = false }
            ))
            None

    node
        "composite-tabs-panels"
        (NodeKind.Tabs(
            { Defaults.tabs with
                Children = [ overviewPanel; settingsPanel ]
                TabHeaders =
                    Some
                        [ { Label = TextSource.Literal "Overview"
                            Icon = Some "chart-glyph"
                            Disabled = Option.None }
                          { Label = TextSource.Literal "Settings"
                            Icon = Option.None
                            Disabled = Some(Binding.Static(Some false)) } ]
                TabTags = Some [ "overview"; "settings" ]
                ActiveTag = Some(Binding.Static(Some "overview")) }
        ))
        None

let card: Node<obj> =
    node
        "card-1"
        (NodeKind.Box(
            { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
              Role = BoxRole.Card
              Heading = Some(TextSource.Literal "Insights")
              Children = [ metric ]
              KeepTogether = false
              BreakBefore = false }
        ))
        None

let stepper: Node<obj> =
    node
        "step-1"
        (NodeKind.Stepper(
            { ActiveStep = Binding.Static(Some 1)
              Children = [ markdown; withId "markdown-2" markdown ]
              // `Some` (Phase 692–694 swap) — the slot is optional now; Some
              // keeps the `"onSelect":"<closure>"` sentinel on the wire.
              OnSelect = Some(fun _ -> Action.Chain []) }
        ))
        None

let summaryList: Node<obj> =
    node
        "summary-1"
        (NodeKind.SummaryList(
            { Heading = Some(TextSource.Literal "Stats")
              Children = [ labelValueRow ] }
        ))
        None

let disclosure: Node<obj> =
    // Typed accordion. Round-trips heading + Open binding +
    // DefaultOpen + Children — every encoder-side wire field. `OnToggle = None`
    // (Phase 426): the pre-426 encoder never emitted the handler, so the
    // handler-free shape keeps this fixture byte-identical (a `Some` closure
    // would now add the `"onToggle":"<closure>"` sentinel).
    node
        "discl-1"
        (NodeKind.Disclosure(
            { Defaults.disclosure with
                Heading = TextSource.Literal "Additional entitlements"
                Children = [ markdown ]
                DefaultOpen = true }
        ))
        None

let modal: Node<obj> =
    // Phase 289 — overlay dialog. Heading (Some) + child + OnDismiss action +
    // Open binding all round-trip; OnDismiss is a wire-survivable Action
    // (Chain []), unlike the renderer-only closures (Tabs.OnSelect). `Some`
    // (Phase 426) keeps the encoded action on the wire, byte-identical.
    node
        "modal-1"
        (NodeKind.Modal(
            { Defaults.modal with
                Heading = Some(TextSource.Literal "Confirm")
                Children = [ markdown ]
                OnDismiss = Some(Action.Chain []) }
        ))
        None

/// Phase 1119 — the anchored popover: `modality` PRESENT (the non-default), an
/// `anchor` naming the control it opens from, and an `open` bound to state so a
/// host has something to toggle. This is the interactive shape.
///
/// The heading and the dismiss affordance are kept deliberately: a popover is a
/// dialog, not a tooltip, and a vector without them would leave the whole
/// `fuaran-popover-heading` / `-dismiss` half of the class family unexercised.
let popoverAnchored: Node<obj> =
    node
        "popover-anchored-1"
        (NodeKind.Modal(
            { Defaults.modal with
                Heading = Some(TextSource.Literal "Choose a colour")
                Children = [ markdown ]
                Open = Binding.State("swatchOpen", Some false)
                Modality = ModalityKind.Popover
                Anchor = Some "swatch" }
        ))
        None

/// Phase 1119 — the SSR-floor vector: a popover whose `open` is a STATIC true,
/// so a no-script host renders it. It is the executable form of the normative
/// statement in `WIRE_FORMAT.md` §3.6.11 — a statically-open popover renders in
/// flow at the position the node occupies, with no scrim and no `aria-modal` —
/// and it is why the `open` binding differs from the vector above rather than
/// being a second copy of it.
let popoverOpen: Node<obj> =
    node
        "popover-open-1"
        (NodeKind.Modal(
            { Defaults.modal with
                Children = [ markdown ]
                Open = Binding.Static(Some true)
                Modality = ModalityKind.Popover
                Anchor = Some "help-trigger" }
        ))
        None

let scrollArea: Node<obj> =
    // Phase 289 — vertical scroll container with a maxHeight bound present and
    // maxWidth absent (exercises the omit-when-None path).
    node
        "scroll-1"
        (NodeKind.ScrollArea(
            { Defaults.scrollArea with
                Children = [ markdown ]
                MaxHeight = Some 320 }
        ))
        None

// ─── Input fixtures ──────────────────────────────────────────────────────

let private placeholderChain: Action<obj> = Action.Chain []

let formAllFields: Node<obj> =
    let textField: FormField<obj> =
        { Defaults.formField with
            Id = "name"
            Label = TextSource.Literal "Name"
            Kind = FormFieldKind.Text(Some(Binding.Static(Some "")), Some(fun _ -> placeholderChain))
            Required = true
            Help = Some(TextSource.Literal "Full legal name") }

    let numberField: FormField<obj> =
        { Defaults.formField with
            Id = "age"
            Label = TextSource.Literal "Age"
            Kind = FormFieldKind.Number(Some(Binding.Static(Some 0.0)), Some(fun _ -> placeholderChain)) }

    let checkboxField: FormField<obj> =
        { Defaults.formField with
            Id = "agree"
            Label = TextSource.Literal "I agree"
            Kind = FormFieldKind.Checkbox(Some(Binding.Static(Some false)), Some(fun _ -> placeholderChain))
            Required = true }

    let choiceField: FormField<obj> =
        { Defaults.formField with
            Id = "tier"
            Label = TextSource.Literal "Tier"
            Kind =
                FormFieldKind.Choice(
                    Binding.Static(Some [ { Value = "basic"; Label = "Basic" }; { Value = "pro"; Label = "Pro" } ]),
                    Some(Binding.Static(Some "basic")),
                    Some(fun _ -> placeholderChain)
                ) }

    let textareaField: FormField<obj> =
        { Defaults.formField with
            Id = "notes"
            Label = TextSource.Literal "Notes"
            Kind = FormFieldKind.TextArea(Some(Binding.Static(Some "")), Some(fun _ -> placeholderChain), 5) }

    node
        "form-1"
        (NodeKind.Form(
            { Defaults.form with
                Fields = [ textField; numberField; checkboxField; choiceField; textareaField ]
                OnSubmit = placeholderChain
                SubmitLabel = TextSource.Literal "Save"
                // Phase 130: bound form-level disabled-state (the
                // interactive-state class-fix); exercises the new optional
                // slot in the corpus round-trip.
                Disabled = Some(Binding.State("formBusy", Some false)) }
        ))
        None

/// Round-trip cover for the parallel-additive
/// `FormFieldKind.RangedNumber` case. Exercises every combination of
/// present/absent Min, Max, Step so the canonical-JSON encoder's
/// omit-when-None discipline and the decoder's optional-field branch
/// stay in lockstep.
let formRangedNumber: Node<obj> =
    let allBoundsField: FormField<obj> =
        { Defaults.formField with
            Id = "year"
            Label = TextSource.Literal "Year"
            Kind =
                FormFieldKind.RangedNumber(
                    Some(Binding.Static(Some 2024.0)),
                    Some(fun _ -> placeholderChain),
                    Some 1979.0,
                    Some 2028.0,
                    Some 1.0
                )
            Required = true }

    let minOnlyField: FormField<obj> =
        { Defaults.formField with
            Id = "years"
            Label = TextSource.Literal "Years contributed"
            Kind =
                FormFieldKind.RangedNumber(
                    Some(Binding.Static(Some 10.0)),
                    Some(fun _ -> placeholderChain),
                    Some 0.0,
                    None,
                    None
                ) }

    let noBoundsField: FormField<obj> =
        { Defaults.formField with
            Id = "amount"
            Label = TextSource.Literal "Amount"
            Kind =
                FormFieldKind.RangedNumber(
                    Some(Binding.Static(Some 100.0)),
                    Some(fun _ -> placeholderChain),
                    None,
                    None,
                    None
                ) }

    node
        "form-ranged"
        (NodeKind.Form(
            { Defaults.form with
                Fields = [ allBoundsField; minOnlyField; noBoundsField ]
                OnSubmit = placeholderChain
                SubmitLabel = TextSource.Literal "Save" }
        ))
        None

let filtersBoth: Node<obj> =
    // `Some` closures — the F#-authored shape, byte-identical to pre-Phase-423 (`"onChange":"<closure>"`).
    let textFilter: FilterSpec<obj> =
        { Name = "q"
          Label = TextSource.Literal "Search"
          Kind = FormFieldKind.Text(Some(Binding.Static(Some "")), Some(fun _ -> placeholderChain)) }

    let choiceFilter: FilterSpec<obj> =
        { Name = "tier"
          Label = TextSource.Literal "Tier"
          Kind =
            FormFieldKind.Choice(
                Binding.Static(Some [ { Value = "all"; Label = "All" } ]),
                Some(Binding.Static(Some "all")),
                Some(fun _ -> placeholderChain)
            ) }

    node "filters-1" (NodeKind.Filters { Items = [ textFilter; choiceFilter ] }) None

/// Declarative chips (Phase 423): every `FilterKind` case with `onChange = None` — the AI-authored
/// shape whose `onChange` field is omitted on the wire, `value` self-reads its own `$filters.<name>`,
/// and `RangeFilter` carries typed `{min,max}` bounds. Proves the omitted-onChange + typed-range wire.
let filtersDeclarative: Node<obj> =
    let textFilter: FilterSpec<obj> =
        { Name = "q"
          Label = TextSource.Literal "Search"
          Kind = FormFieldKind.Text(Some(Binding.Filter("q", None)), None) }

    let choiceFilter: FilterSpec<obj> =
        { Name = "tier"
          Label = TextSource.Literal "Tier"
          Kind =
            FormFieldKind.Choice(
                Binding.Static(Some [ { Value = "all"; Label = "All" } ]),
                Some(Binding.Filter("tier", None)),
                None
            ) }

    let rangeFilter: FilterSpec<obj> =
        { Name = "age"
          Label = TextSource.Literal "Age"
          Kind = FormFieldKind.Range(Some(Binding.Static(Some { Min = 0.0; Max = 100.0 })), None, None, None, None) }

    node "filters-declarative" (NodeKind.Filters { Items = [ textFilter; choiceFilter; rangeFilter ] }) None

/// Round-trip cover for the parallel-additive
/// `FormFieldKind.SegmentedChoice` + `FilterKind.SegmentedFilter` cases.
/// Exercises both orientations so the canonical-JSON encoder's
/// orientation field and the decoder's required-field branch stay in
/// lockstep.
let formSegmentedChoice: Node<obj> =
    let opts: SelectOption list =
        [ { Value = "effective"
            Label = "Effective" }
          { Value = "marginal"
            Label = "Marginal" }
          { Value = "takeHome"
            Label = "Take-home" } ]

    let horizontalField: FormField<obj> =
        { Defaults.formField with
            Id = "metric"
            Label = TextSource.Literal "Metric"
            Kind =
                FormFieldKind.SegmentedChoice(
                    Binding.Static(Some opts),
                    Some(Binding.Static(Some "effective")),
                    Some(fun _ -> placeholderChain),
                    Orientation.Horizontal
                ) }

    let verticalField: FormField<obj> =
        { Defaults.formField with
            Id = "tier"
            Label = TextSource.Literal "Tier"
            Kind =
                FormFieldKind.SegmentedChoice(
                    Binding.Static(Some [ { Value = "low"; Label = "Low" }; { Value = "high"; Label = "High" } ]),
                    Some(Binding.Static None),
                    Some(fun _ -> placeholderChain),
                    Orientation.Vertical
                )
            Required = true }

    node
        "form-segmented"
        (NodeKind.Form(
            { Defaults.form with
                Fields = [ horizontalField; verticalField ]
                OnSubmit = placeholderChain
                SubmitLabel = TextSource.Literal "Save" }
        ))
        None

/// Phase 1113 — `FormFieldKind.Combobox` with a STATIC option source and the
/// default (omitted) `allowFreeText`. The omission is the point of the vector:
/// the shortest combobox document is the CONSTRAINED one, so an emitter that
/// says nothing gets the searchable-select shape and has to ask for anything
/// looser.
let formComboboxStatic: Node<obj> =
    let opts: SelectOption list =
        [ { Value = "gbr"
            Label = "United Kingdom" }
          { Value = "fra"; Label = "France" }
          { Value = "deu"; Label = "Germany" } ]

    let field: FormField<obj> =
        { Defaults.formField with
            Id = "country"
            Label = TextSource.Literal "Country"
            Required = true
            Kind =
                FormFieldKind.Combobox(
                    false,
                    Some(fun _ -> placeholderChain),
                    Binding.Static(Some opts),
                    Some(Binding.Static(Some "fra"))
                ) }

    node
        "form-combobox-static"
        (NodeKind.Form(
            { Defaults.form with
                Fields = [ field ]
                OnSubmit = placeholderChain
                SubmitLabel = TextSource.Literal "Save" }
        ))
        None

/// Phase 1113 — the asynchronous suggestion source, which is the shape the
/// control exists for and the one that needed NO new coordination vocabulary: a
/// `Binding.Query` in the ordinary options slot. Declarative (no `onChange`) and
/// with the value slot OMITTED, so the field auto-binds `State("city", None)`
/// exactly as a `Choice` would.
let formComboboxQuery: Node<obj> =
    let field: FormField<obj> =
        { Defaults.formField with
            Id = "city"
            Label = TextSource.Literal "City"
            Kind =
                FormFieldKind.Combobox(
                    false,
                    None,
                    Binding.Query("cities", (fun (raw: obj) -> unbox raw), Some [ "country" ]),
                    None
                ) }

    node
        "form-combobox-query"
        (NodeKind.Form(
            { Defaults.form with
                Fields = [ field ]
                OnSubmit = placeholderChain
                SubmitLabel = TextSource.Literal "Search" }
        ))
        None

/// Phase 1113 — `allowFreeText = true`: the option list is a SUGGESTION and a
/// value outside it is admitted. Carries the value as free text that matches no
/// option, which is the state a constrained combobox can never be in.
let formComboboxFreeText: Node<obj> =
    let field: FormField<obj> =
        { Defaults.formField with
            Id = "tag"
            Label = TextSource.Literal "Tag"
            Kind =
                FormFieldKind.Combobox(
                    true,
                    None,
                    Binding.Static(
                        Some
                            [ { Value = "urgent"; Label = "Urgent" }
                              { Value = "blocked"; Label = "Blocked" } ]
                    ),
                    Some(Binding.Static(Some "needs-a-second-look"))
                ) }

    node
        "form-combobox-freetext"
        (NodeKind.Form(
            { Defaults.form with
                Fields = [ field ]
                OnSubmit = placeholderChain
                SubmitLabel = TextSource.Literal "Save" }
        ))
        None

/// Round-trip cover for `FilterKind.SegmentedFilter`. Parallel
/// to `filtersBoth`'s ChoiceFilter; uses Horizontal orientation.
let filtersSegmented: Node<obj> =
    let segmentedFilter: FilterSpec<obj> =
        { Name = "view"
          Label = TextSource.Literal "View"
          Kind =
            FormFieldKind.SegmentedChoice(
                Binding.Static(Some [ { Value = "table"; Label = "Table" }; { Value = "chart"; Label = "Chart" } ]),
                Some(Binding.Static(Some "table")),
                Some(fun _ -> placeholderChain),
                Orientation.Horizontal
            ) }

    node "filters-segmented" (NodeKind.Filters { Items = [ segmentedFilter ] }) None

/// Round-trip cover for the additive `FormFieldKind.Date` case (Phase 288).
/// Exercises all three variants (Date / Time / DateTime) and every
/// present/absent combination of the optional Min / Max / Step constraints so
/// the encoder's omit-when-None discipline and the decoder's optional-field
/// branch stay in lockstep. Values are ISO-8601 strings.
let formDate: Node<obj> =
    let dateField: FormField<obj> =
        { Defaults.formField with
            Id = "checkIn"
            Label = TextSource.Literal "Check in"
            Kind =
                FormFieldKind.Date(
                    Some(Binding.Static(Some "2026-01-15")),
                    Some(fun _ -> placeholderChain),
                    DateVariant.Date,
                    Some "2026-01-01",
                    Some "2026-12-31",
                    None
                )
            Required = true }

    let timeField: FormField<obj> =
        { Defaults.formField with
            Id = "alarm"
            Label = TextSource.Literal "Alarm"
            Kind =
                FormFieldKind.Date(
                    Some(Binding.Static(Some "08:30")),
                    Some(fun _ -> placeholderChain),
                    DateVariant.Time,
                    None,
                    None,
                    Some 60.0
                ) }

    let dateTimeField: FormField<obj> =
        { Defaults.formField with
            Id = "meeting"
            Label = TextSource.Literal "Meeting"
            Kind =
                FormFieldKind.Date(
                    Some(Binding.Static(Some "2026-03-01T14:00")),
                    Some(fun _ -> placeholderChain),
                    DateVariant.DateTime,
                    None,
                    None,
                    None
                ) }

    node
        "form-date"
        (NodeKind.Form(
            { Defaults.form with
                Fields = [ dateField; timeField; dateTimeField ]
                OnSubmit = placeholderChain
                SubmitLabel = TextSource.Literal "Book" }
        ))
        None

/// Round-trip cover for `FormFieldKind.DateRange` (Phase 725) — the
/// single-control date range. Exercises all three variants and the
/// present/absent constraint combinations, plus the Phase 426 handler-free
/// shape, so the encoder's omit-when-None discipline and the decoder's
/// optional-field branch stay in lockstep. The Static pair rides as the bare
/// `{from, to}` object (the `Range` posture, no `Static` envelope).
let formDateRange: Node<obj> =
    let stayField: FormField<obj> =
        { Defaults.formField with
            Id = "stay"
            Label = TextSource.Literal "Stay"
            Kind =
                FormFieldKind.DateRange(
                    Some(
                        Binding.Static(
                            Some
                                { From = "2026-03-01"
                                  To = "2026-03-08" }
                        )
                    ),
                    Some(fun _ -> placeholderChain),
                    DateVariant.Date,
                    Some "2026-01-01",
                    Some "2026-12-31",
                    None
                )
            Required = true }

    // Handler-free (Phase 426) — `onChange` is omitted on the wire and the
    // renderer writes the changed pair back to the value slot.
    let shiftField: FormField<obj> =
        { Defaults.formField with
            Id = "shift"
            Label = TextSource.Literal "Shift"
            Kind =
                FormFieldKind.DateRange(
                    Some(Binding.State("shift", Some { From = "08:00"; To = "17:00" })),
                    None,
                    DateVariant.Time,
                    None,
                    None,
                    Some 900.0
                ) }

    let windowField: FormField<obj> =
        { Defaults.formField with
            Id = "window"
            Label = TextSource.Literal "Window"
            Kind =
                FormFieldKind.DateRange(
                    Some(
                        Binding.Static(
                            Some
                                { From = "2026-03-01T09:00"
                                  To = "2026-03-01T17:00" }
                        )
                    ),
                    Some(fun _ -> placeholderChain),
                    DateVariant.DateTime,
                    None,
                    None,
                    None
                ) }

    node
        "form-date-range"
        (NodeKind.Form(
            { Defaults.form with
                Fields = [ stayField; shiftField; windowField ]
                OnSubmit = placeholderChain
                SubmitLabel = TextSource.Literal "Book" }
        ))
        None

/// Filter-context cover for `FormFieldKind.DateRange` (Phase 725). The chip's
/// `value` is the exact auto-binding (`Filter(name)`), so it is OMITTED on the
/// wire per the FilterSpec auto-bind rule — and the pair binds ONE filter
/// param, not two, which is the case's reason to exist.
let filtersDateRange: Node<obj> =
    let stayChip: FilterSpec<obj> =
        { Name = "stay"
          Label = TextSource.Literal "Stay"
          Kind = FormFieldKind.DateRange(Some(Binding.Filter("stay", None)), None, DateVariant.Date, None, None, None) }

    node "filters-date-range" (NodeKind.Filters { Items = [ stayChip ] }) None

let button: Node<obj> =
    node
        "btn-1"
        (NodeKind.Button(
            { Defaults.button with
                Label = TextSource.Literal "Refresh"
                OnClick = placeholderChain
                Variant = ButtonVariant.Primary
                Icon = Some "refresh"
                // Phase 129: bound disabled-state — the canonical
                // "disabled while a calc is in flight" shape.
                Disabled = Some(Binding.State("loading", Some false)) }
        ))
        None

// Action.WriteToClipboard fixture for the round-trip suite.
// The canonical share-link copy-button shape: a button whose
// OnClick chains a clipboard write with a follow-on dispatch the model
// listens to. Confirms the encoder/decoder forward-coupling holds for
// the new DU case AND that Chain composes cleanly across renderer-
// native (WriteToClipboard) and renderer-substrate (Dispatch) cases.
let buttonClipboard: Node<obj> =
    node
        "btn-copy-link"
        (NodeKind.Button(
            { Defaults.button with
                Label = TextSource.Literal "Copy share link"
                OnClick =
                    Action.Chain
                        [ Action.WriteToClipboard "https://example.com/share/abc123"
                          Action.Dispatch(box "ClipboardCopied") ] }
        ))
        None

// Phase 676 — the JSON-payload actions, with payloads worth testing.
//
// `Notify` / `SetState` / `AiTool` each carry a `JVal` of arbitrary JSON, and
// until now the corpus exercised them ONLY in the reject family (the null cases).
// Nothing pinned what a real payload's bytes look like, so a codec that got
// nesting, key order, escaping or float layout wrong would have passed.
//
// The payload is deliberately awkward, because that is the whole point of the
// fixture: nested object inside an array inside an object, keys authored OUT of
// order (rule 2 sorts them Ordinal on the way out), an empty object and an empty
// array (neither is absence), a whole-valued float and one needing scientific
// layout (rule 5), and strings carrying the two characters JSON must escape plus
// a control character and an astral-plane codepoint (rule 6).
let buttonJsonPayloads: Node<obj> =
    let awkward =
        JObj
            [ "zeta", JStr "last key authored first"
              "alpha", JInt 1
              "nested",
              JArr
                  [ JObj [ "b", JBool true; "a", JFloat 1e-7 ]
                    JArr [ JInt 0; JFloat 3.0 ]
                    JObj []
                    JArr [] ]
              "escapes", JStr "quote\" back\\slash  astral-\U0001F600"
              "float-whole", JFloat 2.0 ]

    node
        "btn-json-payloads"
        (NodeKind.Button(
            { Defaults.button with
                Label = TextSource.Literal "Fire the JSON-payload actions"
                OnClick =
                    Action.Chain
                        [ Action.Notify("audit.channel", awkward)
                          Action.SetState("draft", Some(awkward), None)
                          Action.AiTool("summarise", awkward) ]
                Variant = ButtonVariant.Primary }
        ))
        None

// Action.ReadFileBody fixture for the round-trip suite (Phase 136). The
// canonical "read the selected workbook as base64, then dispatch it to the
// model" shape — placed on a Button.OnClick because OnSelect is a closure
// (its body never serialises). Confirms the encoder/decoder forward-coupling
// holds for the new DU case: only `fileRef` (the opaque id) + `encoding`
// cross the wire; the blob handle is absent and `onRead` is the closure
// sentinel.
let buttonReadFile: Node<obj> =
    node
        "btn-read-workbook"
        (NodeKind.Button(
            { Defaults.button with
                Label = TextSource.Literal "Load workbook"
                OnClick =
                    Action.ReadFileBody(
                        "workbook-upload:0",
                        None,
                        FileReadEncoding.Base64,
                        Some(fun _ -> box "WorkbookLoaded")
                    )
                Variant = ButtonVariant.Primary }
        ))
        None

/// `Action.Call` result targets (Phase 428): a closure-authored `Call` (the
/// `"onResult":"<closure>"` sentinel — previously only generator-covered, now
/// corpus-pinned), a declarative `Call … into State` with a `$state` reader,
/// and a declarative `Call … into Query` with a `Binding.Query` reader — the
/// AI-authorable fetch loop end-to-end on the wire.
let callInto: Node<obj> =
    let closureButton =
        node
            "btn-call-closure"
            (NodeKind.Button(
                { Defaults.button with
                    Label = TextSource.Literal "Refresh (closure)"
                    OnClick = Action.Call("/api/refresh", Some(fun _ -> placeholderChain), None) }
            ))
            None

    let intoStateButton =
        node
            "btn-fetch-total"
            (NodeKind.Button(
                { Defaults.button with
                    Label = TextSource.Literal "Fetch total"
                    OnClick = Action.Call("/api/total", None, Some(CallResultTarget.State "total"))
                    Variant = ButtonVariant.Primary }
            ))
            None

    let stateReader =
        node
            "total-metric"
            (NodeKind.Metric(
                { Fuaran.UI.Defaults.metric with
                    Label = TextSource.Literal "Total"
                    Value = Binding.State("total", Some 0.0) }
            ))
            None

    let intoQueryButton =
        node
            "btn-fetch-orders"
            (NodeKind.Button(
                { Defaults.button with
                    Label = TextSource.Literal "Fetch orders"
                    OnClick = Action.Call("/api/orders", None, Some(CallResultTarget.Query "orders"))
                    Variant = ButtonVariant.Primary }
            ))
            None

    let queryReader =
        node
            "orders-metric"
            (NodeKind.Metric(
                { Fuaran.UI.Defaults.metric with
                    Label = TextSource.Literal "Orders"
                    Value = Binding.Query("orders", (fun (raw: obj) -> unbox raw), None) }
            ))
            None

    node
        "call-into"
        (NodeKind.Box(
            { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
              Role = BoxRole.Group
              Heading = None
              Children = [ closureButton; intoStateButton; stateReader; intoQueryButton; queryReader ]
              KeepTogether = false
              BreakBefore = false }
        ))
        None

// Phase 283 — the Compute layer's invocable-capability seam. A Metric whose Source is a
// `Binding.Invoke` (a host-registered capability dispatched for a value), and a Button whose
// OnClick is an `Action.Invoke` (the effectful sibling). Both carry a capabilityId + scalar
// `(addr, value)` args; the body is never on the wire. Exercises the round-trip of the new
// Binding/Action cases under the canonical `$type` discipline.
let metricInvoke: Node<obj> =
    node
        "metric-invoke"
        (NodeKind.Metric(
            { metricSpec with
                Value =
                    Binding.Invoke(
                        "forecast.revenue",
                        [ { Addr = "horizon"; Value = "12" }; { Addr = "scenario"; Value = "base" } ]
                    )
                Trend = None
                TrendFormat = None }
        ))
        None

let buttonInvoke: Node<obj> =
    node
        "btn-invoke"
        (NodeKind.Button(
            { Defaults.button with
                Label = TextSource.Literal "Run model"
                // qualified — `Action.Invoke` alone resolves to `System.Action.Invoke`.
                OnClick = Fuaran.UI.Types.Action.Invoke("model.score", [ { Addr = "rows"; Value = "all" } ])
                Variant = ButtonVariant.Primary }
        ))
        None

let fileUpload: Node<obj> =
    node
        "upload-1"
        (NodeKind.FileUpload(
            { Defaults.fileUpload with
                Label = TextSource.Literal "Upload CSV"
                Accept = [ ".csv"; "text/csv" ]
                OnSelect = Some(fun _ -> placeholderChain)
                // Phase 130: bound disabled-state corpus coverage.
                Disabled = Some(Binding.State("uploadBusy", Some false)) }
        ))
        None

/// Phase 1115 — a drop-target upload. `dropTarget` is present and
/// `acceptPaste` is OMITTED, which is what makes this vector pin the polarity
/// rather than merely exercise the field: a host reading an absent `acceptPaste`
/// as "paste admitted" round-trips these bytes perfectly and is wrong about what
/// the document permits. `accept` is populated deliberately — the same filter the
/// picker applies has to apply to a drop, and a vector with no filter would say
/// nothing about that.
let fileUploadDrop: Node<obj> =
    node
        "upload-drop-1"
        (NodeKind.FileUpload(
            { Defaults.fileUpload with
                Label = TextSource.Literal "Drop a spreadsheet"
                Accept = [ ".csv"; "text/csv" ]
                Multiple = true
                OnSelect = Some(fun _ -> placeholderChain)
                DropTarget = true }
        ))
        None

/// Phase 1115 — a paste-accepting upload, the mirror vector: `acceptPaste`
/// present, `dropTarget` omitted. Single-file and image-filtered, because
/// pasting a screenshot into an avatar field is the shape this route exists for
/// and it is the one that exercises the wildcard-MIME arm of the accept filter.
let fileUploadPaste: Node<obj> =
    node
        "upload-paste-1"
        (NodeKind.FileUpload(
            { Defaults.fileUpload with
                Label = TextSource.Literal "Paste an image"
                Accept = [ "image/*" ]
                OnSelect = Some(fun _ -> placeholderChain)
                AcceptPaste = true }
        ))
        None

let select: Node<obj> =
    node
        "select-1"
        (NodeKind.Select(
            { Defaults.select with
                Label = TextSource.Literal "Region"
                Source = Binding.Static(Some [ { Value = "uk"; Label = "UK" } ])
                Value = Binding.Static(Some "uk")
                // `Some` (Phase 426) — keeps `"onChange":"<closure>"` on the
                // wire, byte-identical to the pre-426 corpus.
                OnChange = Some(fun _ -> placeholderChain)
                Placeholder = Some(TextSource.Literal "Choose one")
                // Phase 130: bound disabled-state corpus coverage.
                Disabled = Some(Binding.State("selectBusy", Some false))
                // Phase 291: single-select — Multiple/Values/OnChangeMulti
                // omitted on the wire (the degenerate case stays byte-stable).
                Multiple = None }
        ))
        None

/// Round-trip cover for the Phase 291 multi-select `Select`. `Multiple = true`
/// + a `Values` `Binding<string list>` (non-empty Static → the `<opaque>`
/// sentinel, mirroring `Source`). `OnChangeMulti = None` (Phase 426): the
/// pre-426 encoder never emitted the multi handler, so the handler-free shape
/// keeps this fixture byte-identical (a `Some` closure would now add the
/// `"onChangeMulti":"<closure>"` sentinel — covered by `multiSelectClosure`).
let multiSelect: Node<obj> =
    node
        "multiselect-1"
        (NodeKind.Select(
            { Defaults.select with
                Label = TextSource.Literal "Tags"
                Source = Binding.Static(Some [ { Value = "red"; Label = "Red" }; { Value = "green"; Label = "Green" } ])
                OnChange = Some(fun _ -> placeholderChain)
                Multiple = Some true
                Values = Some(Binding.Static(Some [ "red"; "green" ])) }
        ))
        None

/// Handler-free form (Phase 426 — the control write-back default): every
/// handler is `None` (omitted on the wire) and every value binding is directly
/// `Binding.State`, the shape an AI author emits — the renderer writes each
/// typed change back to the field's own `$state.<key>` slot with zero host
/// code. Proves the omitted-handler wire across Text / Number / Checkbox /
/// Choice.
let formDeclarative: Node<obj> =
    let textField: FormField<obj> =
        { Defaults.formField with
            Id = "profile-name"
            Label = TextSource.Literal "Name"
            Kind = FormFieldKind.Text(Some(Binding.State("profileName", Some "")), Option.None)
            Required = true }

    let numberField: FormField<obj> =
        { Defaults.formField with
            Id = "profile-age"
            Label = TextSource.Literal "Age"
            Kind = FormFieldKind.Number(Some(Binding.State("profileAge", Some 0.0)), Option.None) }

    let checkboxField: FormField<obj> =
        { Defaults.formField with
            Id = "profile-agree"
            Label = TextSource.Literal "I agree"
            Kind = FormFieldKind.Checkbox(Some(Binding.State("profileAgree", Some false)), Option.None)
            Required = true }

    let choiceField: FormField<obj> =
        { Defaults.formField with
            Id = "profile-tier"
            Label = TextSource.Literal "Tier"
            Kind =
                FormFieldKind.Choice(
                    Binding.Static(Some [ { Value = "basic"; Label = "Basic" }; { Value = "pro"; Label = "Pro" } ]),
                    Some(Binding.State("profileTier", None)),
                    Option.None
                ) }

    node
        "form-declarative"
        (NodeKind.Form(
            { Defaults.form with
                Fields = [ textField; numberField; checkboxField; choiceField ]
                OnSubmit = placeholderChain
                SubmitLabel = TextSource.Literal "Save" }
        ))
        None

/// Phase 596 — the symmetric-auto-bind minimal form: every field's value IS
/// the exact auto-binding `State(field.Id, typed placeholder)`, so the
// Phase 767 — the CANONICAL empty state with a call to action, authored to
// settle a question rather than to add vocabulary.
//
// 020/c4 (MUST, x2) failed on "the parts read as a single unified empty-state
// element, not three disconnected pieces". The emissions put a `Callout` and a
// `Button` side by side inside a Card `Box`, and the judge was right about the
// render: a Callout carries its OWN chrome, so nesting one inside a Card gives a
// bordered region inside a bordered region with the button outside the inner
// border — two elements, visually.
//
// The fix is NOT a `Callout.actions` field or an `EmptyState` kind. `Box` with
// `role: "Card"` already carries `heading` + `children`, so ONE bordered region
// holding heading, prose and action is expressible today — which is exactly the
// vocabulary charter's §1.2 irreducibility gate ("a shape no combination of
// existing kinds, roles and variants can express"). This composition is that
// combination. A `Callout` is the right kind for a *banner* — a message with no
// action; it is the wrong kind for an actionable empty state.
let emptyStateCard: Node<obj> =
    node
        "empty-state-card"
        (NodeKind.Box(
            { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
              Role = BoxRole.Card
              Heading = Some(TextSource.Literal "No saved searches yet")
              Children =
                [ node
                      "empty-state-note"
                      (NodeKind.Markdown(
                          { Text =
                              TextSource.Literal
                                  "Searches you save will appear here so you can re-run them with one click." }
                      ))
                      None
                  node
                      "empty-state-cta"
                      (NodeKind.Button(
                          { Defaults.button with
                              Label = TextSource.Literal "Browse jobs"
                              Variant = ButtonVariant.Primary
                              Icon = Some "search" }
                      ))
                      None ]
              KeepTogether = false
              BreakBefore = false }
        ))
        None

// Phase 766 — the boolean TOGGLE affordance, beside a Checkbox so the corpus
// records the distinction the vocabulary now draws. Same data (a bool, the same
// write-back), different control and a11y contract: the toggle renders
// `role="switch"` + `aria-checked`, which a screen reader announces as on/off
// rather than checked.
//
// The demand: 017/c2 x3 + 043/c3 x3 across two unrelated tasks, every sighting
// substituting a `Select` or a `Checkbox` because no switch existed — the
// `contains`/Core#90 fingerprint. `NodeKind.Switch` is the state-bound
// CONDITIONAL and always was; the widget had no spelling until now.
let formToggle: Node<obj> =
    let toggleField: FormField<obj> =
        { Defaults.formField with
            Id = "irrigation-running"
            Label = TextSource.Literal "Irrigation"
            Kind =
                FormFieldKind.Toggle(
                    Some(Binding.State("irrigation-running", Some Fuaran.UI.Defaults.ControlValueDefaults.checkbox)),
                    Option.None
                ) }

    // The contrast case: consent stays a Checkbox. Both in one fixture so the
    // corpus shows a reader WHEN each applies, not merely that both exist.
    let consentField: FormField<obj> =
        { Defaults.formField with
            Id = "accept-terms"
            Label = TextSource.Literal "I accept the terms"
            Kind =
                FormFieldKind.Checkbox(
                    Some(Binding.State("accept-terms", Some Fuaran.UI.Defaults.ControlValueDefaults.checkbox)),
                    Option.None
                )
            Required = true }

    node
        "form-toggle"
        (NodeKind.Form(
            { Defaults.form with
                Fields = [ toggleField; consentField ]
                SubmitLabel = TextSource.Literal "Save" }
        ))
        None

/// Phase 864 — the declared constraint vocabulary, in the shape the charter's
/// §7 worked examples name. Four fields carry the four ways a rule can arrive:
/// a `format` alone, a `pattern` with a `message`, a length pair, and the
/// CROSS-FIELD predicate — a `compare` whose operand is `Binding.State` reading
/// the sibling field by its own id, which is the whole cross-field mechanism
/// (the Phase 596 auto-bind already stores every field's value under that key,
/// so no addressing vocabulary is minted).
///
/// The fifth field is the control that carries NO rule, and it is here on
/// purpose: `rule` is `Optional` rather than omit-default, so a field without
/// one must encode with no `rule` key at all — which is what makes every
/// pre-864 form fixture byte-unchanged.
let formFieldRules: Node<obj> =
    let emailField: FormField<obj> =
        { Defaults.formField with
            Id = "work-email"
            Label = TextSource.Literal "Work email"
            Kind =
                FormFieldKind.Text(
                    Some(Binding.State("work-email", Some Fuaran.UI.Defaults.ControlValueDefaults.text)),
                    Option.None
                )
            Required = true
            Rule =
                Some
                    { Compare = None
                      Format = Some TextFormat.Email
                      MaxLength = None
                      Message = None
                      MinLength = None
                      Pattern = None } }

    // `pattern` beside a `message`: the prose does not disappear when the rule
    // is declared, it moves inside the rule, where a host shows it AT the moment
    // the rule is unmet rather than permanently.
    let postcodeField: FormField<obj> =
        { Defaults.formField with
            Id = "postcode"
            Label = TextSource.Literal "Postcode"
            Kind =
                FormFieldKind.Text(
                    Some(Binding.State("postcode", Some Fuaran.UI.Defaults.ControlValueDefaults.text)),
                    Option.None
                )
            Required = true
            Rule =
                Some
                    { Compare = None
                      Format = None
                      MaxLength = None
                      Message = Some(TextSource.Literal "Enter a UK postcode, e.g. EH1 1YZ")
                      MinLength = None
                      Pattern = Some "[A-Z]{1,2}[0-9][A-Z0-9]? ?[0-9][A-Z]{2}" } }

    let usernameField: FormField<obj> =
        { Defaults.formField with
            Id = "username"
            Label = TextSource.Literal "Username"
            Kind =
                FormFieldKind.Text(
                    Some(Binding.State("username", Some Fuaran.UI.Defaults.ControlValueDefaults.text)),
                    Option.None
                )
            Required = true
            Rule =
                Some
                    { Compare = None
                      Format = None
                      MaxLength = Some 24
                      Message = None
                      MinLength = Some 3
                      Pattern = None } }

    let startDateField: FormField<obj> =
        { Defaults.formField with
            Id = "hire-start-date"
            Label = TextSource.Literal "Start date"
            Kind =
                FormFieldKind.Date(
                    Some(Binding.State("hire-start-date", Some Fuaran.UI.Defaults.ControlValueDefaults.date)),
                    Option.None,
                    DateVariant.Date,
                    None,
                    None,
                    None
                )
            Required = true }

    // The cross-field predicate, and the reason it needed almost no new
    // vocabulary. `gte` against `State("hire-start-date")` is the exact intent
    // the ×10 `stress-004/c3` sighting cluster restated as help text every time.
    let endDateField: FormField<obj> =
        { Defaults.formField with
            Id = "hire-end-date"
            Label = TextSource.Literal "End date"
            Kind =
                FormFieldKind.Date(
                    Some(Binding.State("hire-end-date", Some Fuaran.UI.Defaults.ControlValueDefaults.date)),
                    Option.None,
                    DateVariant.Date,
                    None,
                    None,
                    None
                )
            Required = true
            Rule =
                Some
                    { Compare =
                        Some
                            { Against = Binding.State("hire-start-date", Option.None)
                              Op = CompareOp.Gte }
                      Format = None
                      MaxLength = None
                      Message = Some(TextSource.Literal "End date must be on or after the start date")
                      MinLength = None
                      Pattern = None } }

    node
        "form-field-rules"
        (NodeKind.Form(
            { Defaults.form with
                Fields = [ emailField; postcodeField; usernameField; startDateField; endDateField ]
                SubmitLabel = TextSource.Literal "Save" }
        ))
        None

/// canonical bytes carry NO `value` key on any field (mirror of the
/// filters-declarative minimal chip). Pins the round-trip: decode
/// synthesises the same bindings back, encode omits them again.
let formDeclarativeMinimal: Node<obj> =
    let textField: FormField<obj> =
        { Defaults.formField with
            Id = "guest-name"
            Label = TextSource.Literal "Name"
            Kind =
                FormFieldKind.Text(
                    Some(Binding.State("guest-name", Some Fuaran.UI.Defaults.ControlValueDefaults.text)),
                    Option.None
                )
            Required = true }

    let numberField: FormField<obj> =
        { Defaults.formField with
            Id = "party-size"
            Label = TextSource.Literal "Party size"
            Kind =
                FormFieldKind.Number(
                    Some(Binding.State("party-size", Some Fuaran.UI.Defaults.ControlValueDefaults.number)),
                    Option.None
                ) }

    let choiceField: FormField<obj> =
        { Defaults.formField with
            Id = "seating"
            Label = TextSource.Literal "Seating"
            Kind =
                FormFieldKind.Choice(
                    Binding.Static(
                        Some
                            [ { Value = "indoor"; Label = "Indoor" }
                              { Value = "terrace"; Label = "Terrace" } ]
                    ),
                    Some(Binding.State("seating", Fuaran.UI.Defaults.ControlValueDefaults.choice)),
                    Option.None
                ) }

    let dateField: FormField<obj> =
        { Defaults.formField with
            Id = "visit-date"
            Label = TextSource.Literal "Date"
            Kind =
                FormFieldKind.Date(
                    Some(Binding.State("visit-date", Some Fuaran.UI.Defaults.ControlValueDefaults.date)),
                    Option.None,
                    DateVariant.Date,
                    None,
                    None,
                    None
                )
            Required = true }

    node
        "form-declarative-minimal"
        (NodeKind.Form(
            { Defaults.form with
                Fields = [ textField; numberField; choiceField; dateField ]
                OnSubmit = placeholderChain
                SubmitLabel = TextSource.Literal "Book" }
        ))
        None

/// Handler-free interactive layouts + select (Phase 426): tabs whose
/// `ActiveIndex` is State-bound with `onSelect` omitted (a click writes the
/// index back), a dismissable modal whose `Open` is State-bound with
/// `onDismiss` omitted (dismiss writes `false`), a disclosure whose `Open` is
/// State-bound with `onToggle` omitted, and a single-select whose `Value` is
/// State-bound with `onChange` omitted — the full declarative-floor wire.
let controlsDeclarative: Node<obj> =
    let tabsNode =
        node
            "decl-tabs"
            (NodeKind.Tabs(
                { Defaults.tabs with
                    Children = [ markdown ]
                    ActiveIndex = Binding.State("activePane", Some 0) }
            ))
            None

    let modalNode =
        node
            "decl-modal"
            (NodeKind.Modal(
                { Defaults.modal with
                    Open = Binding.State("modalOpen", Some false)
                    Heading = Some(TextSource.Literal "Confirm")
                    Children = [ withId "markdown-2" markdown ] }
            ))
            None

    let disclosureNode =
        node
            "decl-disclosure"
            (NodeKind.Disclosure(
                { Defaults.disclosure with
                    Heading = TextSource.Literal "Advanced"
                    Open = Binding.State("advancedOpen", Some false)
                    Children = [ withId "markdown-3" markdown ] }
            ))
            None

    let selectNode =
        node
            "decl-select"
            (NodeKind.Select(
                { Defaults.select with
                    Label = TextSource.Literal "Region"
                    Source = Binding.Static(Some [ { Value = "uk"; Label = "UK" } ])
                    Value = Binding.State("region", None)
                    Placeholder = Some(TextSource.Literal "Choose one") }
            ))
            None

    node
        "controls-declarative"
        (NodeKind.Box(
            { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
              Role = BoxRole.Group
              Heading = None
              Children = [ tabsNode; modalNode; disclosureNode; selectNode ]
              KeepTogether = false
              BreakBefore = false }
        ))
        None

/// Closure-authored counterparts for the Phase 426 sentinel keys that were
/// previously never encoded: tabs with a tag-overlay `onSelectTag` closure, a
/// disclosure with an `onToggle` closure, and a multi-select with an
/// `onChangeMulti` closure — each now rides the wire as its own `"<closure>"`
/// sentinel and decodes to the inert `Some` placeholder.
let multiSelectClosure: Node<obj> =
    let tabsNode =
        node
            "closure-tabs"
            (NodeKind.Tabs(
                { Defaults.tabs with
                    Children = [ markdown; sparkline ]
                    OnSelect = Some(fun _ -> placeholderChain)
                    TabTags = Some [ "overview"; "detail" ]
                    ActiveTag = Some(Binding.Static(Some "overview"))
                    OnSelectTag = Some(fun _ -> placeholderChain) }
            ))
            None

    let disclosureNode =
        node
            "closure-disclosure"
            (NodeKind.Disclosure(
                { Defaults.disclosure with
                    Heading = TextSource.Literal "Advanced"
                    OnToggle = Some(fun _ -> placeholderChain)
                    Children = [ withId "markdown-2" markdown ] }
            ))
            None

    let multiNode =
        node
            "closure-multiselect"
            (NodeKind.Select(
                { Defaults.select with
                    Label = TextSource.Literal "Tags"
                    Source = Binding.Static(Some [ { Value = "red"; Label = "Red" } ])
                    OnChange = Some(fun _ -> placeholderChain)
                    Multiple = Some true
                    Values = Some(Binding.Static(Some [ "red" ]))
                    OnChangeMulti = Some(fun _ -> placeholderChain) }
            ))
            None

    node
        "controls-closure"
        (NodeKind.Box(
            { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
              Role = BoxRole.Group
              Heading = None
              Children = [ tabsNode; disclosureNode; multiNode ]
              KeepTogether = false
              BreakBefore = false }
        ))
        None

// ─── Visualisation fixtures ──────────────────────────────────────────────

let gridVis: Node<obj> =
    let col: ColumnErased<obj> =
        { Label = "Channel"
          Value = Some(fun _ -> CellValue.Empty)
          Field = None
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = CellKindErased.Text
          Width = ColumnWidth.Auto }

    node
        "grid-1"
        (NodeKind.DataGrid(
            { SortStateKey = None
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort = None
              Source =
                // fuaran#665 — typed rows: a Static rows payload IS wire-representable
                // (int cells ride rule 5's integer form). Mirrors Fuaran-Core's
                // authored `gridNode` sample byte-for-byte.
                Binding.Static(
                    Some(
                        Seq.ofList
                            [ (Map.ofList [ "channel", box "Direct"; "revenue", box 1200 ]: Row)
                              Map.ofList [ "channel", box "Referral"; "revenue", box 830 ] ]
                    )
                )
              RowKey = Some(fun _ -> "<closure>")
              RowKeyField = None
              Columns = [ col ]
              OnRowClick = None
              Editable = false
              Reorderable = false
              TransferInKey = None
              TransferOutKey = None
              StaticRows = None
              KeepRowsTogether = false
              RepeatHeader = false }
        ))
        None

/// Phase 1473 — a grid declaring that its ROWS are not split across a page
/// boundary. One new member, for the reason `boxKeepTogether` states.
///
/// This is the half no wrapper reaches: a box around the grid keeps the whole
/// grid together, but nothing outside the grid knows where a row ends, so a
/// wrapped cell torn across the boundary can only be prevented from here.
let gridKeepRowsTogether: Node<obj> =
    let col: ColumnErased<obj> =
        { Label = "Note"
          Value = None
          Field = Some "note"
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = CellKindErased.Text
          Width = ColumnWidth.Auto }

    node
        "grid-keep-rows-together-1"
        (NodeKind.DataGrid(
            { SortStateKey = None
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort = None
              Source = Binding.Query("notes", (fun _ -> Seq.empty), None)
              RowKey = None
              RowKeyField = Some "note"
              Columns = [ col ]
              OnRowClick = None
              Editable = false
              Reorderable = false
              TransferInKey = None
              TransferOutKey = None
              StaticRows = None
              KeepRowsTogether = true
              RepeatHeader = false }
        ))
        None

/// Phase 1473 — a grid declaring that its column headers REPEAT at the top of
/// every page it continues onto. One new member again.
///
/// Irreducible for the same reason: the header is the grid's own row group, and
/// nothing outside the grid can name it. The projection is
/// `display: table-header-group`, which makes the repetition the paged
/// formatter's job — so it holds with no script at all.
let gridRepeatHeader: Node<obj> =
    let col: ColumnErased<obj> =
        { Label = "Line"
          Value = None
          Field = Some "line"
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = CellKindErased.Text
          Width = ColumnWidth.Auto }

    node
        "grid-repeat-header-1"
        (NodeKind.DataGrid(
            { SortStateKey = None
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort = None
              Source = Binding.Query("lines", (fun _ -> Seq.empty), None)
              RowKey = None
              RowKeyField = Some "line"
              Columns = [ col ]
              OnRowClick = None
              Editable = false
              Reorderable = false
              TransferInKey = None
              TransferOutKey = None
              StaticRows = None
              KeepRowsTogether = false
              RepeatHeader = true }
        ))
        None

/// Phase 750 — `CellKindErased.TonedPill`, the wire-expressible conditional tone.
/// The whole point of the fixture is that EVERY part of it rides the wire: the rows
/// are embedded columnar data, the two display columns project row properties by
/// `field` (no `value` closure), and the status column's tone comes from a declared
/// value→tone map. Nothing here erases to a sentinel, so a decoded tree renders the
/// delayed rows visually distinguished with zero host code — which is exactly what a
/// hosted `Pill` cannot express.
///
/// Deliberately exercises both `default` postures in one document: the status column
/// declares `Subdued` for a value the map does not mention (emitted), the carrier
/// column leaves it at `ToneVariant.Default` (omitted-when-default, so the key is
/// absent). One fixture, both branches of the omit rule.
let gridTonedPill: Node<obj> =
    let source =
        Fuaran.Core.Embedded
            { Schema =
                [ "id", Fuaran.Core.StringType
                  "carrier", Fuaran.Core.StringType
                  "status", Fuaran.Core.StringType ]
              Columns =
                [ Fuaran.Core.Column.create
                      "id"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "SHP-1001"
                        Fuaran.Core.Str "SHP-1002"
                        Fuaran.Core.Str "SHP-1003" ]
                  Fuaran.Core.Column.create
                      "carrier"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "Northwind"
                        Fuaran.Core.Str "Meridian"
                        Fuaran.Core.Str "Northwind" ]
                  Fuaran.Core.Column.create
                      "status"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "On time"
                        Fuaran.Core.Str "Delayed"
                        Fuaran.Core.Str "Cancelled" ] ] }

    let declarative (label: string) (field: string) (kind: CellKindErased<obj>) : ColumnErased<obj> =
        { Label = label
          // Phase 425 — no `value` closure: `Field` alone drives the projection.
          Value = None
          Field = Some field
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = kind
          Width = ColumnWidth.Auto }

    node
        "grid-toned-pill"
        (NodeKind.DataGrid(
            { SortStateKey = None
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort = None
              Source = Binding.Transform(TransformSource.Data(source), [], None)
              RowKey = None
              RowKeyField = Some "id"
              Columns =
                [ declarative "Shipment" "id" CellKindErased.Text
                  declarative
                      "Carrier"
                      "carrier"
                      (CellKindErased.TonedPill(
                          "carrier",
                          Map [ "Meridian", ToneVariant.Info ],
                          // Left at the identity default — the `default` key is OMITTED.
                          ToneVariant.Default
                      ))
                  declarative
                      "Status"
                      "status"
                      (CellKindErased.TonedPill(
                          "status",
                          Map
                              [ "On time", ToneVariant.Success
                                "Delayed", ToneVariant.Warning
                                "Cancelled", ToneVariant.Critical ],
                          // Non-identity — the `default` key IS emitted.
                          ToneVariant.Subdued
                      )) ]
              OnRowClick = None
              Editable = false
              Reorderable = false
              TransferInKey = None
              TransferOutKey = None
              StaticRows = None
              KeepRowsTogether = false
              RepeatHeader = false }
        ))
        None

let chart: Node<obj> =
    node
        "chart-1"
        (NodeKind.Chart(
            { Defaults.chart with
                Source =
                    // fuaran#665 — typed rows (see grid-1). Mirrors Fuaran-Core's
                    // authored `chartNode` sample byte-for-byte.
                    Binding.Static(
                        Some(
                            Seq.ofList
                                [ (Map.ofList [ "cost", box 420; "month", box "Jan"; "revenue", box 980 ]: Row)
                                  Map.ofList [ "cost", box 390; "month", box "Feb"; "revenue", box 1105 ] ]
                        )
                    )
                XField = "month"
                YFields = [ "revenue"; "cost" ]
                Title = Some(TextSource.Literal "Channel mix")
                // Absent (Phase 876) — the ordinary shape: no declared value
                // meaning, so the lowering's canonical default rendering applies.
                ValueFormat = None
                // Absent (Phase 878) — likewise the ordinary shape: both axis
                // titles fall back to their capitalised field names and no
                // subtitle draws. `chart-axis-titles` pins the present half.
                XTitle = None
                // Stacked = true (Phase 126) — exercises the now-carried
                // stacked-vs-grouped chart intent round-tripping.
                Stacked = true }
        ))
        None

/// Phase 876 — `valueFormat`: the value axis's declared number format, reusing
/// the existing `Format` vocabulary (no parallel formatting DU was minted). The
/// PRESENT half of the pair (`chart-1` pins the absent half).
let chartValueFormat: Node<obj> =
    node
        "chart-value-format"
        (NodeKind.Chart(
            { Defaults.chart with
                Source =
                    Binding.Static(
                        Some(
                            Seq.ofList
                                [ (Map.ofList [ "month", box "Jan"; "revenue", box 12500000 ]: Row)
                                  Map.ofList [ "month", box "Feb"; "revenue", box 15200000 ] ]
                        )
                    )
                Kind = ChartKind.Bar
                XField = "month"
                YFields = [ "revenue" ]
                Title = Some(TextSource.Literal "Revenue")
                ValueFormat = Some(Format.Currency "GBP") }
        ))
        None

/// Phase 878 — `xTitle` / `yTitle` / `subtitle`: the axis NAMES and the muted
/// line under the title. The PRESENT half of the pair (`chart-1` pins the
/// absent half, where both axes fall back to their capitalised field names).
/// The subtitle states the unit, which is also the shape that exercises the
/// dedupe rule: an explicit subtitle suppresses the lowering's own
/// display-unit slot.
let chartAxisTitles: Node<obj> =
    node
        "chart-axis-titles"
        (NodeKind.Chart(
            { Defaults.chart with
                Source =
                    Binding.Static(
                        Some(
                            Seq.ofList
                                [ (Map.ofList [ "quarter", box "Q1"; "revenue", box 12500000 ]: Row)
                                  Map.ofList [ "quarter", box "Q2"; "revenue", box 15200000 ] ]
                        )
                    )
                Kind = ChartKind.Bar
                XField = "quarter"
                YFields = [ "revenue" ]
                Title = Some(TextSource.Literal "Revenue by quarter")
                ValueFormat = Some(Format.Currency "GBP")
                XTitle = Some(TextSource.Literal "Quarter")
                YTitle = Some(TextSource.Literal "Revenue")
                Subtitle = Some(TextSource.Literal "Millions of £") }
        ))
        None

/// Phase 880 — `legendPosition`: WHERE the legend sits. The PRESENT half of the
/// pair (every other chart fixture pins the absent half, which means "the host
/// style's default", not "no legend"). Two series, so the chart genuinely has a
/// legend to place, and `Bottom` is chosen deliberately over the default
/// `Right`: a fixture that names the value the style would have picked anyway
/// cannot show that the wire field was read at all.
let chartLegendPosition: Node<obj> =
    node
        "chart-legend-position"
        (NodeKind.Chart(
            { Defaults.chart with
                Source =
                    Binding.Static(
                        Some(
                            Seq.ofList
                                [ (Map.ofList [ "region", box "North"; "sales", box 80; "target", box 100 ]: Row)
                                  Map.ofList [ "region", box "South"; "sales", box 130; "target", box 110 ] ]
                        )
                    )
                Kind = ChartKind.Bar
                XField = "region"
                YFields = [ "sales"; "target" ]
                Title = Some(TextSource.Literal "Sales vs target")
                LegendPosition = Some ChartLegendPosition.Bottom }
        ))
        None

/// Phase 881 — `dataLabels`: whether the values are written onto the picture.
/// The PRESENT half of the pair; every other chart fixture pins the absent
/// half, which means `Off` — and `Off` is also the default, so an absent field
/// lowers to the pre-881 picture byte-for-byte. `Ends` is the only other value
/// there is: the vocabulary carries no all-points case, by design.
let chartDataLabels: Node<obj> =
    node
        "chart-data-labels"
        (NodeKind.Chart(
            { Defaults.chart with
                Source =
                    Binding.Static(
                        Some(
                            Seq.ofList
                                [ (Map.ofList [ "quarter", box "Q1"; "revenue", box 120 ]: Row)
                                  Map.ofList [ "quarter", box "Q2"; "revenue", box 150 ] ]
                        )
                    )
                Kind = ChartKind.Bar
                XField = "quarter"
                YFields = [ "revenue" ]
                Title = Some(TextSource.Literal "Revenue by quarter")
                DataLabels = Some ChartDataLabels.Ends }
        ))
        None

/// Phase 882 — `xScale`: what the x column MEANS. The PRESENT half of the pair
/// (`Temporal`); every other chart fixture pins the absent half, which means
/// `Category` — also the default, so an absent field lowers to the pre-882
/// picture byte-for-byte. The x cells are canonical ISO-8601 dates, which is
/// what makes the declaration groundable: a `Temporal` axis over a non-date
/// column is a FUARAN097 refusal, not a silent coercion.
let chartTemporalX: Node<obj> =
    node
        "chart-temporal-x"
        (NodeKind.Chart(
            { Defaults.chart with
                Source =
                    Binding.Static(
                        Some(
                            Seq.ofList
                                [ (Map.ofList [ "day", box "2026-01-05"; "sessions", box 1200 ]: Row)
                                  Map.ofList [ "day", box "2026-01-12"; "sessions", box 1450 ]
                                  Map.ofList [ "day", box "2026-01-19"; "sessions", box 1310 ]
                                  Map.ofList [ "day", box "2026-01-26"; "sessions", box 1580 ] ]
                        )
                    )
                XField = "day"
                YFields = [ "sessions" ]
                Title = Some(TextSource.Literal "Sessions by week")
                XScale = Some ChartXScale.Temporal }
        ))
        None

// ─── fuaran#665 — the Phase 663 editable-grid anchor (grid + chart on ONE state key) ──
//
// The corpus carried NO `editable: true` fixture at all, so the cross-host
// parity harness had nothing to certify the editable-grid write-back floor
// against. This pair is the canonical Phase 663 shape: `editable: true` over a
// DIRECT `Binding.State` rows source (the one write-back-capable shape), typed
// rows riding the wire in `defaultValue`, field-named Text/Numeric columns
// (closure-free — the declarative floor), and a Chart sourced on the SAME state
// key, so an edit committed by the grid re-renders the chart.

let private planRows: Row list =
    [ Map.ofList [ "month", box "Jan"; "revenue", box 980 ]
      Map.ofList [ "month", box "Feb"; "revenue", box 1105 ] ]

let gridEditableState: Node<obj> =
    let col (label: string) (field: string) (kind: CellKindErased<obj>) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = kind
          Width = ColumnWidth.Auto }

    node
        "grid-editable-state"
        (NodeKind.DataGrid(
            { SortStateKey = None
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort = None
              Source = Binding.State("planRows", Some(Seq.ofList planRows))
              RowKey = None
              RowKeyField = Some "month"
              Columns =
                [ col "Month" "month" CellKindErased.Text
                  col "Revenue" "revenue" CellKindErased.Numeric ]
              OnRowClick = None
              Editable = true
              Reorderable = false
              TransferInKey = None
              TransferOutKey = None
              StaticRows = None
              KeepRowsTogether = false
              RepeatHeader = false }
        ))
        None

let chartStateRows: Node<obj> =
    node
        "chart-state-rows"
        (NodeKind.Chart(
            { Defaults.chart with
                Source = Binding.State("planRows", Some(Seq.ofList planRows))
                Kind = ChartKind.Bar
                XField = "month"
                YFields = [ "revenue" ] }
        ))
        None

// Phase 282 — the Compute layer. A DataGrid whose Source is a `Binding.Transform`: a declarative
// `Fuaran.Core.DataFrame` pipeline (filter → groupBy → sort, with a null cell + a typed source)
// over an embedded columnar `DataSource`. Exercises the round-trip of the compute sub-tree through
// the shared `Canon` `$type` discipline (the codecs bridge to `Fuaran.Core.ColumnCodec` /
// `DataFrameCodec`).
let gridTransform: Node<obj> =
    let source =
        Fuaran.Core.Embedded
            { Schema = [ "dept", Fuaran.Core.StringType; "amount", Fuaran.Core.IntType ]
              Columns =
                [ Fuaran.Core.Column.create
                      "dept"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "eng"; Fuaran.Core.Str "eng"; Fuaran.Core.Str "sales" ]
                  Fuaran.Core.Column.create
                      "amount"
                      Fuaran.Core.IntType
                      [ Fuaran.Core.Int 100; Fuaran.Core.Int 120; Fuaran.Core.Null ] ] }

    let pipeline: Fuaran.Core.Transform list =
        [ Fuaran.Core.Filter(
              Fuaran.Core.Binary(Fuaran.Core.Gt, Fuaran.Core.Col "amount", Fuaran.Core.Lit(Fuaran.Core.Int 0))
          )
          Fuaran.Core.GroupBy(
              [ "dept" ],
              [ ({ Name = "total"
                   Fn = Fuaran.Core.Sum
                   Of = "amount" }
                : Fuaran.Core.Agg) ]
          )
          Fuaran.Core.Sort [ "total", Fuaran.Core.Desc ] ]

    node
        "grid-transform"
        (NodeKind.DataGrid(
            { SortStateKey = None
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort = None
              Source = Binding.Transform(TransformSource.Data(source), pipeline, None)
              RowKey = Some(fun _ -> "<closure>")
              RowKeyField = None
              Columns = []
              OnRowClick = None
              Editable = false
              Reorderable = false
              TransferInKey = None
              TransferOutKey = None
              StaticRows = None
              KeepRowsTogether = false
              RepeatHeader = false }
        ))
        None

/// Phase 424 — a parameterised `Binding.Transform`: the pipeline's `filter` step compares a `col`
/// against a `param` sourced from a `Binding.Filter` chip, so the grid is scoped by a live filter
/// with zero host code. Proves the `params` wire (omitted-when-empty elsewhere).
let gridTransformParam: Node<obj> =
    let source =
        Fuaran.Core.Embedded
            { Schema = [ "dept", Fuaran.Core.StringType; "amount", Fuaran.Core.IntType ]
              Columns =
                [ Fuaran.Core.Column.create
                      "dept"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "eng"; Fuaran.Core.Str "sales" ]
                  Fuaran.Core.Column.create "amount" Fuaran.Core.IntType [ Fuaran.Core.Int 100; Fuaran.Core.Int 90 ] ] }

    let pipeline: Fuaran.Core.Transform list =
        [ Fuaran.Core.Filter(Fuaran.Core.Binary(Fuaran.Core.Eq, Fuaran.Core.Col "dept", Fuaran.Core.Param "dept")) ]

    node
        "grid-transform-param"
        (NodeKind.DataGrid(
            { SortStateKey = None
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort = None
              Source =
                Binding.Transform(
                    TransformSource.Data(source),
                    pipeline,
                    Some
                        [ { From = Binding.Filter("dept", None)
                            Name = "dept" } ]
                )
              RowKey = Some(fun _ -> "<closure>")
              RowKeyField = None
              Columns = []
              OnRowClick = None
              Editable = false
              Reorderable = false
              TransferInKey = None
              TransferOutKey = None
              StaticRows = None
              KeepRowsTogether = false
              RepeatHeader = false }
        ))
        None

/// Phase 610 — the multi-select chip idiom end to end: a `<select multiple>` whose `values` binding
/// IS `$filters.depts` (handler omitted, so its write-back stores the selection there) beside a
/// `DataGrid` whose `Transform` scopes rows with an `in`/`param` membership test over that same
/// name. The list param resolves by SUBSTITUTION (`InParam` -> `InList`), and an empty selection is
/// unbound, so deselecting everything shows the unfiltered table. Proves the list-valued `param`
/// wire (`{"$type":"in","expr":…,"param":…}`) at node level, where the literal `items` form and the
/// scalar `param` form were both already pinned.
let multiselectChipListParam: Node<obj> =
    let source =
        Fuaran.Core.Embedded
            { Schema = [ "dept", Fuaran.Core.StringType; "amount", Fuaran.Core.IntType ]
              Columns =
                [ Fuaran.Core.Column.create
                      "dept"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "eng"; Fuaran.Core.Str "sales"; Fuaran.Core.Str "ops" ]
                  Fuaran.Core.Column.create
                      "amount"
                      Fuaran.Core.IntType
                      [ Fuaran.Core.Int 100; Fuaran.Core.Int 90; Fuaran.Core.Int 70 ] ] }

    let fieldCol (label: string) (field: string) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = CellKindErased.Text
          Width = ColumnWidth.Auto }

    node
        "multiselect-chip-list-param"
        (NodeKind.Box(
            { Layout = BoxLayout.Auto
              Role = BoxRole.Dashboard
              Heading = Some(TextSource.Literal "Spend by department")
              Children =
                [ node
                      "dept-chip"
                      (NodeKind.Select(
                          { Defaults.select with
                              Label = TextSource.Literal "Departments"
                              Source =
                                  Binding.Static(
                                      Some
                                          [ { Value = "eng"; Label = "Engineering" }
                                            { Value = "sales"; Label = "Sales" }
                                            { Value = "ops"; Label = "Operations" } ]
                                  )
                              Multiple = Some true
                              Values = Some(Binding.Filter("depts", None)) }
                      ))
                      None
                  node
                      "dept-grid"
                      (NodeKind.DataGrid(
                          { SortStateKey = None
                            PageSize = None
                            PageStateKey = None
                            EditStateKey = None
                            DefaultSort = None
                            Source =
                              Binding.Transform(
                                  TransformSource.Data(source),
                                  [ Fuaran.Core.Filter(Fuaran.Core.InParam(Fuaran.Core.Col "dept", "depts")) ],
                                  Some
                                      [ { From = Binding.Filter("depts", None)
                                          Name = "depts" } ]
                              )
                            RowKey = None
                            RowKeyField = Some "dept"
                            Columns = [ fieldCol "Department" "dept"; fieldCol "Spend" "amount" ]
                            OnRowClick = None
                            Editable = false
                            Reorderable = false
                            TransferInKey = None
                            TransferOutKey = None
                            StaticRows = None
                            KeepRowsTogether = false
                            RepeatHeader = false }
                      ))
                      None ]
              KeepTogether = false
              BreakBefore = false }
        ))
        None

/// Phase 425 — a fully field-named `DataGrid`: columns carry `Field` (no closure), the spec carries
/// `RowKeyField`, and the source is a `Transform`. A decoded grid renders data + stable identity with
/// zero host code. Proves the `field` / `rowKeyField` wire (omitted-when-None elsewhere).
let gridFieldNamed: Node<obj> =
    let source =
        Fuaran.Core.Embedded
            { Schema = [ "dept", Fuaran.Core.StringType; "amount", Fuaran.Core.IntType ]
              Columns =
                [ Fuaran.Core.Column.create "dept" Fuaran.Core.StringType [ Fuaran.Core.Str "eng" ]
                  Fuaran.Core.Column.create "amount" Fuaran.Core.IntType [ Fuaran.Core.Int 100 ] ] }

    let fieldCol (label: string) (field: string) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = CellKindErased.Text
          Width = ColumnWidth.Auto }

    node
        "grid-field-named"
        (NodeKind.DataGrid(
            { SortStateKey = None
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort = None
              Source = Binding.Transform(TransformSource.Data(source), [], None)
              RowKey = None
              RowKeyField = Some "dept"
              Columns = [ fieldCol "Dept" "dept"; fieldCol "Amount" "amount" ]
              OnRowClick = None
              Editable = false
              Reorderable = false
              TransferInKey = None
              TransferOutKey = None
              StaticRows = None
              KeepRowsTogether = false
              RepeatHeader = false }
        ))
        None

// The MASTER-DETAIL-PRESELECTED composition: a grid beside a detail card whose
// slots bind `State` with a `defaultValue` — the selected-row id lives in state,
// pre-selected on load. The 2026-07-19 eval campaign's second judge-layer
// cluster (task 040 criterion c3): models INVENT grid selection properties
// (`defaultSelection`, `initialSelection`, `selectedRowKey` — none exist) or
// omit selection entirely; the passing emissions all used exactly this State
// idiom. Row-click wiring is host-side (`OnRowClick` is a closure); the
// declaratively-emittable half — the default + the detail binding — is what
// this fixture teaches.
let masterDetailPreselected: Node<obj> =
    let source =
        Fuaran.Core.Embedded
            { Schema = [ "id", Fuaran.Core.StringType; "priority", Fuaran.Core.StringType ]
              Columns =
                [ Fuaran.Core.Column.create
                      "id"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "TCK-2041"; Fuaran.Core.Str "TCK-2042" ]
                  Fuaran.Core.Column.create
                      "priority"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "high"; Fuaran.Core.Str "low" ] ] }

    let fieldCol (label: string) (field: string) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = CellKindErased.Text
          Width = ColumnWidth.Auto }

    node
        "master-detail-preselected"
        (NodeKind.Box(
            { Layout = BoxLayout.Auto
              Role = BoxRole.Dashboard
              Heading = None
              Children =
                [ node
                      "ticket-grid"
                      (NodeKind.DataGrid(
                          { SortStateKey = None
                            PageSize = None
                            PageStateKey = None
                            EditStateKey = None
                            DefaultSort = None
                            Source = Binding.Transform(TransformSource.Data(source), [], None)
                            RowKey = None
                            RowKeyField = Some "id"
                            Columns = [ fieldCol "Ticket" "id"; fieldCol "Priority" "priority" ]
                            OnRowClick = None
                            Editable = false
                            Reorderable = false
                            TransferInKey = None
                            TransferOutKey = None
                            StaticRows = None
                            KeepRowsTogether = false
                            RepeatHeader = false }
                      ))
                      None
                  node
                      "ticket-detail"
                      (NodeKind.Box(
                          { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
                            Role = BoxRole.Card
                            Heading = Some(TextSource.Literal "Ticket detail")
                            Children =
                              [ node
                                    "detail-ticket"
                                    (NodeKind.Fact(
                                        { Defaults.fact with
                                            Label = TextSource.Literal "Selected ticket"
                                            // 0.2.9 (Phase 629): the defaulted-Selection form —
                                            // the composition models emit naturally, now
                                            // expressible (was the State workaround pre-629).
                                            // 0.2.10 (Phase 632): + `field` — the projected
                                            // row-key stays scalar after a real click (the
                                            // identity form yielded the whole row).
                                            Value =
                                                TextSource.Bound(
                                                    Binding.Selection(
                                                        "ticket-grid",
                                                        Binding.projectSelectionField<string> "id",
                                                        Some "TCK-2041",
                                                        Some "id"
                                                    )
                                                )
                                            Emphasis = true }
                                    ))
                                    None ]
                            KeepTogether = false
                            BreakBefore = false }
                      ))
                      None
                  // 2026-07-20 demand pin: `Selection` as a `Transform.params`
                  // source (the sol 040 r42 emission's composition) — the
                  // related-items grid filters the embedded rows by the
                  // ticket-grid's selection, defaulted. Wire-confirms the
                  // param `from` slot accepts any Binding, Selection included.
                  node
                      "related-grid"
                      (NodeKind.DataGrid(
                          { SortStateKey = None
                            PageSize = None
                            PageStateKey = None
                            EditStateKey = None
                            DefaultSort = None
                            Source =
                              Binding.Transform(
                                  TransformSource.Data(source),
                                  [ Fuaran.Core.Filter(
                                        Fuaran.Core.Binary(
                                            Fuaran.Core.Eq,
                                            Fuaran.Core.Col "id",
                                            Fuaran.Core.Param "ticketId"
                                        )
                                    ) ],
                                  Some
                                      [ { From =
                                            // 0.2.10 (Phase 632): `field` keeps the param SCALAR
                                            // after a real click — the identity form handed the
                                            // whole row to `objToCell` (a loud non-scalar error).
                                            Binding.Selection(
                                                "ticket-grid",
                                                Binding.projectSelectionField<JVal> "id",
                                                Some(JStr "TCK-2041"),
                                                Some "id"
                                            )
                                          Name = "ticketId" } ]
                              )
                            RowKey = None
                            RowKeyField = Some "id"
                            Columns = [ fieldCol "Ticket" "id"; fieldCol "Priority" "priority" ]
                            OnRowClick = None
                            Editable = false
                            Reorderable = false
                            TransferInKey = None
                            TransferOutKey = None
                            StaticRows = None
                            KeepRowsTogether = false
                            RepeatHeader = false }
                      ))
                      None ]
              KeepTogether = false
              BreakBefore = false }
        ))
        None

// The MULTI-FIELD twin of `masterDetailPreselected`. Every existing Selection
// fixture projects the SAME field (`id`) into every slot, so "one selection can
// feed N slots, each projecting a DIFFERENT column" is nowhere demonstrated —
// and that is exactly the shape models miss. The 2026-08-01 n=3 review found
// 032/c6 + 036/c8 (×6, two tasks) emitting a correct `Selection` + `defaultValue`
// for ONE slot and then HARD-CODING every sibling in the same detail card:
//   "selected-flight Fact is bound via Selection … with defaultValue UA451, but
//    crew names and route are static hard-coded values not driven by selection."
// `Binding.Selection(nodeId, accessor, defaultValue, field)` has carried `field`
// since 0.2.10 (Phase 632), so the composition was expressible the whole time —
// the models learned `defaultValue` (Phase 629) and never learned `field`. This
// fixture teaches the projection by showing three sibling Facts off ONE grid,
// each naming a different column.
let masterDetailMultiField: Node<obj> =
    let source =
        Fuaran.Core.Embedded
            { Schema =
                [ "id", Fuaran.Core.StringType
                  "priority", Fuaran.Core.StringType
                  "assignee", Fuaran.Core.StringType ]
              Columns =
                [ Fuaran.Core.Column.create
                      "id"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "TCK-2041"; Fuaran.Core.Str "TCK-2042" ]
                  Fuaran.Core.Column.create
                      "priority"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "high"; Fuaran.Core.Str "low" ]
                  Fuaran.Core.Column.create
                      "assignee"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "R. Okafor"; Fuaran.Core.Str "M. Lindqvist" ] ] }

    let fieldCol (label: string) (field: string) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = CellKindErased.Text
          Width = ColumnWidth.Auto }

    // One slot of the detail card: a Fact projecting ONE named column off the
    // shared selection. The only thing that varies across the three is `field`.
    let projectedFact (nodeId: string) (label: string) (field: string) : Node<obj> =
        node
            nodeId
            (NodeKind.Fact(
                { Defaults.fact with
                    Label = TextSource.Literal label
                    Value =
                        TextSource.Bound(
                            Binding.Selection(
                                "ticket-grid",
                                Binding.projectSelectionField<string> field,
                                Some "TCK-2041",
                                Some field
                            )
                        ) }
            ))
            None

    node
        "master-detail-multi-field"
        (NodeKind.Box(
            { Layout = BoxLayout.Auto
              Role = BoxRole.Dashboard
              Heading = None
              Children =
                [ node
                      "ticket-grid"
                      (NodeKind.DataGrid(
                          { SortStateKey = None
                            PageSize = None
                            PageStateKey = None
                            EditStateKey = None
                            DefaultSort = None
                            Source = Binding.Transform(TransformSource.Data(source), [], None)
                            RowKey = None
                            RowKeyField = Some "id"
                            Columns =
                              [ fieldCol "Ticket" "id"
                                fieldCol "Priority" "priority"
                                fieldCol "Assignee" "assignee" ]
                            OnRowClick = None
                            Editable = false
                            Reorderable = false
                            TransferInKey = None
                            TransferOutKey = None
                            StaticRows = None
                            KeepRowsTogether = false
                            RepeatHeader = false }
                      ))
                      None
                  node
                      "ticket-detail"
                      (NodeKind.Box(
                          { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
                            Role = BoxRole.Card
                            Heading = Some(TextSource.Literal "Ticket detail")
                            Children =
                              [ projectedFact "detail-ticket" "Selected ticket" "id"
                                projectedFact "detail-priority" "Priority" "priority"
                                projectedFact "detail-assignee" "Assignee" "assignee"
                                // A PROSE slot on the same selection. The 2026-08-02
                                // flip-4 shakedown found models wiring the Facts
                                // correctly and leaving the narration literal, so the
                                // panel's numbers follow the click and its sentence
                                // does not (036/c8, 5/6). `Callout.Body` is a
                                // TextSource like any other — this is the SIMPLE
                                // bound-body form; the only other bound body in the
                                // corpus is the heavy Transform composition in
                                // `scalar-transform-composition`, which is not a
                                // shape a model reaches for to write one sentence.
                                node
                                    "detail-note"
                                    (NodeKind.Callout(
                                        { Defaults.callout with
                                            Heading = Some(TextSource.Literal "Assigned to")
                                            Body =
                                                TextSource.Bound(
                                                    Binding.Selection(
                                                        "ticket-grid",
                                                        Binding.projectSelectionField<string> "assignee",
                                                        Some "R. Okafor",
                                                        Some "assignee"
                                                    )
                                                ) }
                                    ))
                                    None ]
                            KeepTogether = false
                            BreakBefore = false }
                      ))
                      None ]
              KeepTogether = false
              BreakBefore = false }
        ))
        None

// The NON-FIRST-ROW twin of `masterDetailPreselected`, and a deliberate
// near-clone of it: same composition, one different default. Every existing
// Selection fixture defaults to the FIRST row, which makes prune-vs-seed
// UNOBSERVABLE — a host that *prunes* an unbound-param filter (the "unset
// choice filter ⇒ no constraint" rule, WIRE_FORMAT §"Binding.Transform params")
// instead of *seeding* the param from `defaultValue` still shows row 1's data,
// because row 1 is what an unfiltered pipeline surfaces first. Defaulting to
// `TCK-2042` — index 1 of 3, neither first nor last, so a wrong-FIRST and a
// wrong-LAST are both caught — makes the two behaviours diverge visibly.
//
// The third column is per-row-DISTINCT (`note`) so the scalar leg diverges by
// VALUE, not merely by row count: a pruning host renders "Payment gateway
// timeout" where a seeding host renders "Search index stale". A count-only
// divergence can be mistaken for a fixture-shape difference; a wrong string
// cannot.
//
// Four nodes, three of them observing the SAME default through different
// machinery, so a host that gets one leg right and another wrong is caught:
//   - `ticket-grid`   — the master and the Selection's `nodeId` target. The
//                       control: unaffected by the default either way.
//   - `detail-ticket` — the plain-Selection SCALAR leg, no Transform at all.
//                       A pruning host has no filter to prune here, so this
//                       isolates `defaultValue` resolution itself (NotResolved
//                       when the default is ignored).
//   - `related-grid`  — the ROW-CONTEXT leg: Selection feeding a Transform
//                       param. 1 row when seeded, all 3 when pruned.
//   - `detail-note`   — the MASKING-KILLER: the exact `filter -> project ->
//                       limit 1` shape a first-row default hides, terminating
//                       in a scalar Callout body. This is the leg that reports
//                       a wrong VALUE rather than a wrong count.
let masterDetailPreselectedSecondRow: Node<obj> =
    let source =
        Fuaran.Core.Embedded
            { Schema =
                [ "id", Fuaran.Core.StringType
                  "priority", Fuaran.Core.StringType
                  "note", Fuaran.Core.StringType ]
              Columns =
                [ Fuaran.Core.Column.create
                      "id"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "TCK-2041"
                        Fuaran.Core.Str "TCK-2042"
                        Fuaran.Core.Str "TCK-2043" ]
                  Fuaran.Core.Column.create
                      "priority"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "high"; Fuaran.Core.Str "medium"; Fuaran.Core.Str "low" ]
                  Fuaran.Core.Column.create
                      "note"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "Payment gateway timeout"
                        Fuaran.Core.Str "Search index stale"
                        Fuaran.Core.Str "Avatar upload fails" ] ] }

    let fieldCol (label: string) (field: string) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = CellKindErased.Text
          Width = ColumnWidth.Auto }

    /// The one `Transform.params` entry every non-control leg shares: the
    /// ticket-grid's selection, defaulted to the SECOND row, projected through
    /// `field` so the param stays scalar after a real click (Phase 632).
    let ticketIdParam: TransformParam =
        { From =
            Binding.Selection("ticket-grid", Binding.projectSelectionField<JVal> "id", Some(JStr "TCK-2042"), Some "id")
          Name = "ticketId" }

    let filterById =
        Fuaran.Core.Filter(Fuaran.Core.Binary(Fuaran.Core.Eq, Fuaran.Core.Col "id", Fuaran.Core.Param "ticketId"))

    node
        "master-detail-preselected-second-row"
        (NodeKind.Box(
            { Layout = BoxLayout.Auto
              Role = BoxRole.Dashboard
              Heading = None
              Children =
                [ node
                      "ticket-grid"
                      (NodeKind.DataGrid(
                          { SortStateKey = None
                            PageSize = None
                            PageStateKey = None
                            EditStateKey = None
                            DefaultSort = None
                            Source = Binding.Transform(TransformSource.Data(source), [], None)
                            RowKey = None
                            RowKeyField = Some "id"
                            Columns =
                              [ fieldCol "Ticket" "id"
                                fieldCol "Priority" "priority"
                                fieldCol "Note" "note" ]
                            OnRowClick = None
                            Editable = false
                            Reorderable = false
                            TransferInKey = None
                            TransferOutKey = None
                            StaticRows = None
                            KeepRowsTogether = false
                            RepeatHeader = false }
                      ))
                      None
                  node
                      "ticket-detail"
                      (NodeKind.Box(
                          { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
                            Role = BoxRole.Card
                            Heading = Some(TextSource.Literal "Ticket detail")
                            Children =
                              [ node
                                    "detail-ticket"
                                    (NodeKind.Fact(
                                        { Defaults.fact with
                                            Label = TextSource.Literal "Selected ticket"
                                            Value =
                                                TextSource.Bound(
                                                    Binding.Selection(
                                                        "ticket-grid",
                                                        Binding.projectSelectionField<string> "id",
                                                        Some "TCK-2042",
                                                        Some "id"
                                                    )
                                                )
                                            Emphasis = true }
                                    ))
                                    None ]
                            KeepTogether = false
                            BreakBefore = false }
                      ))
                      None
                  node
                      "related-grid"
                      (NodeKind.DataGrid(
                          { SortStateKey = None
                            PageSize = None
                            PageStateKey = None
                            EditStateKey = None
                            DefaultSort = None
                            Source =
                              Binding.Transform(TransformSource.Data(source), [ filterById ], Some [ ticketIdParam ])
                            RowKey = None
                            RowKeyField = Some "id"
                            Columns =
                              [ fieldCol "Ticket" "id"
                                fieldCol "Priority" "priority"
                                fieldCol "Note" "note" ]
                            OnRowClick = None
                            Editable = false
                            Reorderable = false
                            TransferInKey = None
                            TransferOutKey = None
                            StaticRows = None
                            KeepRowsTogether = false
                            RepeatHeader = false }
                      ))
                      None
                  node
                      "detail-note"
                      (NodeKind.Callout(
                          { Defaults.callout with
                              Heading = Some(TextSource.Literal "Ticket note")
                              Body =
                                  TextSource.Bound(
                                      Binding.Transform(
                                          TransformSource.Data(source),
                                          [ filterById
                                            Fuaran.Core.Project [ "note", "note" ]
                                            Fuaran.Core.Limit(1, 0) ],
                                          Some [ ticketIdParam ]
                                      )
                                  ) }
                      ))
                      None ]
              KeepTogether = false
              BreakBefore = false }
        ))
        None

// The SCALAR-TRANSFORM composition (0.2.10, Phase 632): Transform in SCALAR
// slots — the strongest 2026-07-20 demand cluster, emitted unprompted by the
// tier-a-040 sol r42 cell and scored 1.000 by the judge while the language
// refused the slot. Two canonical scalar terminals, one fixture:
//   - a Callout body derived from the SELECTED row (`Selection`-fed param →
//     `filter` → `project` one column → `limit` 1 — the r42 shape verbatim,
//     with the Phase-632 `field` projection keeping the param scalar
//     post-click);
//   - a Badge count over the same data (`filter` → `groupBy(keys: [],
//     aggs: [one count])` — the global-aggregate terminal; empty ⇒ 0).
// Phase 765 — the host-furnished instant, in BOTH the positions the demand
// evidence asked for:
//
//   - `today-fact`   — `Now` straight into a text slot ("the current date in a
//                      header", the pilot-5 row dispositioned "the strongest
//                      new capability demand"; models hardcoded a date and were
//                      judged PARTIAL because nothing else was expressible).
//   - `overdue-grid` — `Now` as a `Transform` PARAM feeding `dateDiffDays`, so
//                      "days overdue" is derived rather than baked. This is the
//                      leg that proves the composition: the verbs already
//                      shipped (Core's `DateDiffDays` reads the leading
//                      `YYYY-MM-DD`, so the ISO-8601 instant works unchanged) —
//                      only the operand was missing.
//
// No clock appears on the wire: `{"$type":"Now"}` has no fields. The host
// resolves it once per render into `BindingSources.Now`, which is what keeps a
// replayed op-stream reproducing its ORIGINAL render instead of drifting to
// replay-time "now".
let nowEnvironmentBinding: Node<obj> =
    let source =
        Fuaran.Core.Embedded
            { Schema = [ "id", Fuaran.Core.StringType; "due", Fuaran.Core.StringType ]
              Columns =
                [ Fuaran.Core.Column.create
                      "id"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "INV-1001"; Fuaran.Core.Str "INV-1002" ]
                  Fuaran.Core.Column.create
                      "due"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "2026-07-01"; Fuaran.Core.Str "2026-07-28" ] ] }

    let fieldCol (label: string) (field: string) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = CellKindErased.Text
          Width = ColumnWidth.Auto }

    node
        "now-environment-binding"
        (NodeKind.Box(
            { Layout = BoxLayout.Auto
              Role = BoxRole.Dashboard
              Heading = None
              Children =
                [ node
                      "today-fact"
                      (NodeKind.Fact(
                          { Defaults.fact with
                              Label = TextSource.Literal "Today"
                              Value = TextSource.Bound(Binding.Now(fun (o: obj) -> unbox<string> o)) }
                      ))
                      None
                  node
                      "overdue-grid"
                      (NodeKind.DataGrid(
                          { SortStateKey = None
                            PageSize = None
                            PageStateKey = None
                            EditStateKey = None
                            DefaultSort = None
                            Source =
                              Binding.Transform(
                                  TransformSource.Data(source),
                                  // days overdue = dateDiffDays(today, due) — the
                                  // param is the ONLY new part; the verb is Core's.
                                  [ Fuaran.Core.Derive(
                                        "daysOverdue",
                                        Fuaran.Core.ApplyFn(
                                            Fuaran.Core.DateDiffDays,
                                            [ Fuaran.Core.Param "today"; Fuaran.Core.Col "due" ]
                                        )
                                    ) ],
                                  Some
                                      [ { From = Binding.Now(fun (o: obj) -> JStr(unbox<string> o))
                                          Name = "today" } ]
                              )
                            RowKey = None
                            RowKeyField = Some "id"
                            Columns =
                              [ fieldCol "Invoice" "id"
                                fieldCol "Due" "due"
                                fieldCol "Days overdue" "daysOverdue" ]
                            OnRowClick = None
                            Editable = false
                            Reorderable = false
                            TransferInKey = None
                            TransferOutKey = None
                            StaticRows = None
                            KeepRowsTogether = false
                            RepeatHeader = false }
                      ))
                      None ]
              KeepTogether = false
              BreakBefore = false }
        ))
        None

let scalarTransformComposition: Node<obj> =
    let source =
        Fuaran.Core.Embedded
            { Schema =
                [ "id", Fuaran.Core.StringType
                  "alert", Fuaran.Core.StringType
                  "severity", Fuaran.Core.StringType ]
              Columns =
                [ Fuaran.Core.Column.create
                      "id"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "TCK-2041"
                        Fuaran.Core.Str "TCK-2042"
                        Fuaran.Core.Str "TCK-2043" ]
                  Fuaran.Core.Column.create
                      "alert"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "TCK-2041 breaches SLA in 2 hours"
                        Fuaran.Core.Str "TCK-2042 breaches SLA in 5 hours"
                        Fuaran.Core.Str "TCK-2043 breaches SLA in 9 hours" ]
                  Fuaran.Core.Column.create
                      "severity"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "critical"
                        Fuaran.Core.Str "high"
                        Fuaran.Core.Str "critical" ] ] }

    node
        "scalar-transform-composition"
        (NodeKind.Box(
            { Layout = BoxLayout.Auto
              Role = BoxRole.Dashboard
              Heading = None
              Children =
                [ node
                      "scalar-ticket-grid"
                      (NodeKind.DataGrid(
                          { SortStateKey = None
                            PageSize = None
                            PageStateKey = None
                            EditStateKey = None
                            DefaultSort = None
                            Source = Binding.Transform(TransformSource.Data(source), [], None)
                            RowKey = None
                            RowKeyField = Some "id"
                            Columns =
                              [ { Label = "Ticket"
                                  Value = None
                                  Field = Some "id"
                                  Sortable = None
                                  Editable = None
                                  Format = CellFormat.None
                                  Kind = CellKindErased.Text
                                  Width = ColumnWidth.Auto }
                                { Label = "Severity"
                                  Value = None
                                  Field = Some "severity"
                                  Sortable = None
                                  Editable = None
                                  Format = CellFormat.None
                                  Kind = CellKindErased.Text
                                  Width = ColumnWidth.Auto } ]
                            OnRowClick = None
                            Editable = false
                            Reorderable = false
                            TransferInKey = None
                            TransferOutKey = None
                            StaticRows = None
                            KeepRowsTogether = false
                            RepeatHeader = false }
                      ))
                      None
                  node
                      "critical-count-badge"
                      (NodeKind.Badge(
                          { Label =
                              TextSource.Bound(
                                  Binding.Transform(
                                      TransformSource.Data(source),
                                      [ Fuaran.Core.Filter(
                                            Fuaran.Core.Binary(
                                                Fuaran.Core.Eq,
                                                Fuaran.Core.Col "severity",
                                                Fuaran.Core.Lit(Fuaran.Core.Str "critical")
                                            )
                                        )
                                        Fuaran.Core.GroupBy(
                                            [],
                                            [ { Name = "n"
                                                Fn = Fuaran.Core.AggFn.Count
                                                Of = "id" } ]
                                        ) ],
                                      None
                                  )
                              )
                            Variant = BadgeVariant.Critical }
                      ))
                      None
                  node
                      "sla-warning"
                      (NodeKind.Callout(
                          { Defaults.callout with
                              Tone = ToneVariant.Warning
                              Heading = Some(TextSource.Literal "SLA breach imminent")
                              Body =
                                  TextSource.Bound(
                                      Binding.Transform(
                                          TransformSource.Data(source),
                                          [ Fuaran.Core.Filter(
                                                Fuaran.Core.Binary(
                                                    Fuaran.Core.Eq,
                                                    Fuaran.Core.Col "id",
                                                    Fuaran.Core.Param "ticketId"
                                                )
                                            )
                                            Fuaran.Core.Project [ "alert", "alert" ]
                                            Fuaran.Core.Limit(1, 0) ],
                                          Some
                                              [ { From =
                                                    Binding.Selection(
                                                        "scalar-ticket-grid",
                                                        Binding.projectSelectionField<JVal> "id",
                                                        Some(JStr "TCK-2041"),
                                                        Some "id"
                                                    )
                                                  Name = "ticketId" } ]
                                      )
                                  ) }
                      ))
                      None ]
              KeepTogether = false
              BreakBefore = false }
        ))
        None

// The FILTERABLE-STATIC composition: a Filters node whose named params drive
// Transform consumers over embedded data. The 2026-07-19 eval campaign found the
// judge-layer failure every model family repeats on this intent (task 035
// criterion c7): filters declared, consumers fed unwired static arrays. The
// wiring is `params` pulling the live filter values by name + pipeline steps
// applying them — declaring filters without it leaves consumers inert. This
// fixture is the canonical worked example the pack teaches the composition from.
let filterableStaticDashboard: Node<obj> =
    let source =
        Fuaran.Core.Embedded
            { Schema =
                [ "region", Fuaran.Core.StringType
                  "genre", Fuaran.Core.StringType
                  "month", Fuaran.Core.StringType
                  "retention", Fuaran.Core.FloatType ]
              Columns =
                [ Fuaran.Core.Column.create
                      "region"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "emea"; Fuaran.Core.Str "amer" ]
                  Fuaran.Core.Column.create
                      "genre"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "drama"; Fuaran.Core.Str "docs" ]
                  Fuaran.Core.Column.create
                      "month"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "jan"; Fuaran.Core.Str "jan" ]
                  Fuaran.Core.Column.create
                      "retention"
                      Fuaran.Core.FloatType
                      [ Fuaran.Core.Float 0.62; Fuaran.Core.Float 0.55 ] ] }

    // Both consumers share the same wiring; a fresh Binding per consumer.
    let filteredSource () =
        Binding.Transform(
            TransformSource.Data(source),
            [ Fuaran.Core.Filter(
                  Fuaran.Core.Binary(Fuaran.Core.Eq, Fuaran.Core.Col "region", Fuaran.Core.Param "region")
              )
              Fuaran.Core.Filter(Fuaran.Core.Binary(Fuaran.Core.Eq, Fuaran.Core.Col "genre", Fuaran.Core.Param "genre")) ],
            Some
                [ { From = Binding.Filter("region", None)
                    Name = "region" }
                  { From = Binding.Filter("genre", None)
                    Name = "genre" } ]
        )

    let choice (name: string) (label: string) (options: (string * string) list) : FilterSpec<obj> =
        { Name = name
          Label = TextSource.Literal label
          Kind =
            FormFieldKind.Choice(
                Binding.Static(Some [ for value, optLabel in options -> { Value = value; Label = optLabel } ]),
                Some(Binding.Filter(name, None)),
                None
            ) }

    let fieldCol (label: string) (field: string) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = CellKindErased.Text
          Width = ColumnWidth.Auto }

    node
        "filterable-static-dashboard"
        (NodeKind.Box(
            { Layout = BoxLayout.Auto
              Role = BoxRole.Dashboard
              Heading = Some(TextSource.Literal "Content performance")
              Children =
                [ node
                      "content-filters"
                      (NodeKind.Filters(
                          { Items =
                              [ choice "region" "Region" [ "emea", "EMEA"; "amer", "Americas" ]
                                choice "genre" "Genre" [ "drama", "Drama"; "docs", "Documentary" ] ] }
                      ))
                      None
                  node
                      "retention-chart"
                      (NodeKind.Chart(
                          { Defaults.chart with
                              Source = filteredSource ()
                              XField = "month"
                              YFields = [ "retention" ]
                              Title = Some(TextSource.Literal "Retention") }
                      ))
                      None
                  node
                      "episode-grid"
                      (NodeKind.DataGrid(
                          { SortStateKey = None
                            PageSize = None
                            PageStateKey = None
                            EditStateKey = None
                            DefaultSort = None
                            Source = filteredSource ()
                            RowKey = None
                            RowKeyField = Some "month"
                            Columns = [ fieldCol "Month" "month"; fieldCol "Retention" "retention" ]
                            OnRowClick = None
                            Editable = false
                            Reorderable = false
                            TransferInKey = None
                            TransferOutKey = None
                            StaticRows = None
                            KeepRowsTogether = false
                            RepeatHeader = false }
                      ))
                      None ]
              KeepTogether = false
              BreakBefore = false }
        ))
        None

/// Phase 421 — a `Metric` whose `Source` is a host-computed `Query` that declares its filter
/// dependency edge (`dependsOn`). Proves the `dependsOn` wire (omitted-when-empty elsewhere) — the
/// tree owns the edge, the host closure owns the predicate.
let queryDependsOn: Node<obj> =
    node
        "query-dependson"
        (NodeKind.Metric(
            { metricSpec with
                Value = Binding.Query("orders", (fun _ -> 0.0), Some [ "status"; "region" ])
                Trend = None
                TrendFormat = None }
        ))
        None

let table: Node<obj> =
    node
        "table-1"
        (
        // Phase 393 — the static read-only table is now the `StaticRows` mode of `DataGrid`.
        NodeKind.DataGrid
            { SortStateKey = None
              Reorderable = false
              TransferInKey = None
              TransferOutKey = None
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort = None
              Source = Binding.Static(Some Seq.empty)
              RowKey = None
              RowKeyField = None
              Columns = []
              OnRowClick = None
              Editable = false
              StaticRows =
                Some
                    // Phase 801 — the sort-intent slots stay ABSENT here deliberately.
                    // `table-1.json` is the byte-identity anchor for the pre-801 wire:
                    // if this fixture's payload ever moves, the addition was not additive.
                    { DefaultSort = None
                      Headers = [ TextSource.Literal "Term"; TextSource.Literal "Definition" ]
                      Rows =
                        [ [ TextSource.Literal "MVU"; TextSource.Literal "Model-View-Update" ]
                          [ TextSource.Literal "DSL"; TextSource.Literal "Domain-specific language" ] ]
                      Sortable = None }
              KeepRowsTogether = false
              RepeatHeader = false })
        None

/// Phase 801 — the same static table DECLARING sort intent: `sortable: true` plus a
/// `defaultSort` naming the second column descending. The round-trip leg proves both
/// optional slots survive canonical encode/decode in every host; sitting beside
/// `table-1` it also proves the two forms are distinguishable on the wire, which is
/// the whole point of modelling absence as absence.
let tableSortable: Node<obj> =
    node
        "table-sortable-1"
        (NodeKind.DataGrid
            { SortStateKey = None
              Reorderable = false
              TransferInKey = None
              TransferOutKey = None
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort = None
              Source = Binding.Static(Some Seq.empty)
              RowKey = None
              RowKeyField = None
              Columns = []
              OnRowClick = None
              Editable = false
              StaticRows =
                Some
                    { DefaultSort =
                        Some
                            { Column = 1
                              Direction = SortDirection.Desc }
                      Headers = [ TextSource.Literal "Region"; TextSource.Literal "Revenue" ]
                      Rows =
                        [ [ TextSource.Literal "North"; TextSource.Literal "1200" ]
                          [ TextSource.Literal "South"; TextSource.Literal "980" ] ]
                      Sortable = Some true }
              KeepRowsTogether = false
              RepeatHeader = false })
        None

let mapVis: Node<obj> =
    node
        "map-1"
        (NodeKind.Map(
            { Defaults.map with
                Source =
                    Binding.Static(
                        Some(
                            [ { Latitude = 51.5
                                Longitude = -0.12
                                Label = "London" } ]
                            : MapMarker list
                        )
                    )
                CentreLatitude = 51.5
                CentreLongitude = -0.12
                Zoom = 6 }
        ))
        None

// ─── Custom + composite ─────────────────────────────────────────────────

let custom: Node<obj> =
    node
        "custom-1"
        (NodeKind.Custom(
            { ModuleId = "analytics"
              ComponentId = "trend-card"
              Props = Map.empty
              ContentHash = None
              // `None` ≡ the old `[]` — the key stays off the wire.
              ExposedNodeIds = None }
        ))
        None

// Custom with the bounded-escape additive
// fields populated. Exercises the wire-shape lock: contentHash + exposed-
// NodeIds round-trip through canonical JSON without precision loss.
let customBounded: Node<obj> =
    node
        "custom-bounded-1"
        (NodeKind.Custom(
            { ModuleId = "deal-flow"
              ComponentId = "QualityRing"
              Props = Map.empty
              ContentHash =
                Some
                    { Algorithm = "SHA256"
                      Hash = "abc123def456"
                      Strictness = HashStrictness.StrictReplay }
              ExposedNodeIds = Some [ "quality-ring-segment-1"; "quality-ring-segment-2" ] }
        ))
        None

let customBoundedAdvisory: Node<obj> =
    node
        "custom-bounded-advisory"
        (NodeKind.Custom(
            { ModuleId = "deal-flow"
              ComponentId = "TrendCard"
              Props = Map.empty
              ContentHash =
                Some
                    { Algorithm = "SHA256"
                      Hash = "fedcba654321"
                      Strictness = HashStrictness.AdvisoryWarning }
              // `None` ≡ the old `[]` — the key stays off the wire.
              ExposedNodeIds = None }
        ))
        None

// ErrorBoundary fixture pinning the wire-form
// round-trip. The canonical-JSON encoder emits `{ "$type": "ErrorBoundary",
// "child": <node>, "fallback": <node> }`; the decoder reverses it back
// to `NodeKind.ErrorBoundary { Child = ...; Fallback = ... }`.
let errorBoundary: Node<obj> =
    node
        "boundary-1"
        (NodeKind.ErrorBoundary
            { Child = node "boundary-child" (NodeKind.Markdown({ Text = TextSource.Literal "Child body" })) None
              Fallback =
                node
                    "boundary-fallback"
                    (NodeKind.Callout(
                        { Defaults.callout with
                            Tone = ToneVariant.Warning
                            Heading = Some(TextSource.Literal "Couldn't render")
                            Body = TextSource.Literal "Fallback rendered" }
                    ))
                    None })
        None

// Switch fixture (Phase 392) pinning the wire-form round-trip. The encoder
// emits `{ "$type":"Switch", "cases":[{"child":<node>,"match":<string>},…],
// "default":<node>, "stateKey":<string> }`; the decoder reverses it to
// `NodeKind.Switch { StateKey = …; Cases = …; Default = … }`. Two cases + a
// distinct default exercise the case-array round-trip and the fallback leg.
// Phase 768 — the Switch selector widened to any Binding: `on` takes a
// Selection, so the branch FOLLOWS THE CLICKED ROW with no writer at all.
//
// This is the 032/c6 shape done right. The failing emissions wired a Switch to
// a stateKey nothing emittable could write (SetState.value is a literal; a
// grid's onRowClick is a host closure) — the models had the right intent
// against a dead end. Moving the READ side is what closes it: the selector
// resolves the selected row's `status` field (defaulted pre-click per the
// Phase 629 law), and first-match-wins picks the branch.
//
// The State form keeps its compact `stateKey` spelling on the wire — this
// fixture is the `on` spelling's coverage; `switch-1` pins the compact form.
let switchOnSelection: Node<obj> =
    let source =
        Fuaran.Core.Embedded
            { Schema = [ "id", Fuaran.Core.StringType; "status", Fuaran.Core.StringType ]
              Columns =
                [ Fuaran.Core.Column.create
                      "id"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "WARD-A"; Fuaran.Core.Str "WARD-B" ]
                  Fuaran.Core.Column.create
                      "status"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "steady"; Fuaran.Core.Str "critical" ] ] }

    let fieldCol (label: string) (field: string) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = CellKindErased.Text
          Width = ColumnWidth.Auto }

    node
        "switch-on-selection"
        (NodeKind.Box(
            { Layout = BoxLayout.Auto
              Role = BoxRole.Dashboard
              Heading = None
              Children =
                [ node
                      "ward-grid"
                      (NodeKind.DataGrid(
                          { SortStateKey = None
                            PageSize = None
                            PageStateKey = None
                            EditStateKey = None
                            DefaultSort = None
                            Source = Binding.Transform(TransformSource.Data(source), [], None)
                            RowKey = None
                            RowKeyField = Some "id"
                            Columns = [ fieldCol "Ward" "id"; fieldCol "Status" "status" ]
                            OnRowClick = None
                            Editable = false
                            Reorderable = false
                            TransferInKey = None
                            TransferOutKey = None
                            StaticRows = None
                            KeepRowsTogether = false
                            RepeatHeader = false }
                      ))
                      None
                  node
                      "ward-status-panel"
                      (NodeKind.Switch(
                          { Defaults.switch with
                              On =
                                  Binding.Selection(
                                      "ward-grid",
                                      Binding.projectSelectionField<string> "status",
                                      Some "steady",
                                      Some "status"
                                  )
                              Cases =
                                  [ { Match = "critical"
                                      Child =
                                        node
                                            "ward-critical"
                                            (NodeKind.Callout(
                                                { Defaults.callout with
                                                    Tone = ToneVariant.Critical
                                                    Heading = Some(TextSource.Literal "Ward at capacity")
                                                    Body =
                                                        TextSource.Literal "Escalate admissions to the on-call manager." }
                                            ))
                                            None } ]
                              Default =
                                  node
                                      "ward-steady"
                                      (NodeKind.Markdown({ Text = TextSource.Literal "Occupancy within normal range." }))
                                      None }
                      ))
                      None ]
              KeepTogether = false
              BreakBefore = false }
        ))
        None

/// Phase 1122 — the motivating document for `autoAdvanceMs`: a carousel. Three
/// panels over one index key, advancing every five seconds.
///
/// Exactly ONE new member is set, and that is the point — the `switchBasic`
/// fixture beside it declares none, so the pair proves the omit-at-`None`
/// polarity in both directions: this one carries the key, that one comes back
/// byte-unchanged without it.
///
/// The fact declared here is not available to the host any other way. Every
/// other half of a carousel is already composable — the stage is a `Box`, the
/// panels are the cases, the position is the bound key, the arrows and dots are
/// ordinary controls writing that key — and nothing in any arrangement of those
/// says a timer exists.
///
/// Note what the fixture does NOT carry, because each absence is the charter's
/// ruling rather than an oversight: no gesture, no threshold, no pause policy,
/// no transition name, and no `motion` (which is host-only, §9, and could not
/// ride here even if the document declared one).
let switchAutoAdvance: Node<obj> =
    node
        "switch-carousel-1"
        (NodeKind.Switch
            { Defaults.switch with
                On = Binding.State("slide", None)
                AutoAdvanceMs = Some 5000
                Cases =
                    [ { Match = "one"
                        Child =
                          node
                              "switch-carousel-panel-one"
                              (NodeKind.Markdown({ Text = TextSource.Literal "Built for the long term" }))
                              None }
                      { Match = "two"
                        Child =
                          node
                              "switch-carousel-panel-two"
                              (NodeKind.Markdown({ Text = TextSource.Literal "Typed all the way down" }))
                              None }
                      { Match = "three"
                        Child =
                          node
                              "switch-carousel-panel-three"
                              (NodeKind.Markdown({ Text = TextSource.Literal "One wire, many hosts" }))
                              None } ]
                Default =
                    node
                        "switch-carousel-default"
                        (NodeKind.Markdown({ Text = TextSource.Literal "Built for the long term" }))
                        None })
        None

let switchBasic: Node<obj> =
    node
        "switch-1"
        (NodeKind.Switch
            { Defaults.switch with
                On = Binding.State("view", None)
                Cases =
                    [ { Match = "details"
                        Child =
                          node "switch-details" (NodeKind.Markdown({ Text = TextSource.Literal "Details view" })) None }
                      { Match = "summary"
                        Child =
                          node "switch-summary" (NodeKind.Markdown({ Text = TextSource.Literal "Summary view" })) None } ]
                Default =
                    node
                        "switch-default"
                        (NodeKind.Callout(
                            { Defaults.callout with
                                Heading = Some(TextSource.Literal "Pick a view")
                                Body = TextSource.Literal "No view selected" }
                        ))
                        None })
        None

// Fragment fixtures. `fragmentDecl` carries a labelled body
// (Markdown so the round-trip exercises a leaf inside the body too);
// `fragmentRef` carries only the target name (the body lives at the
// decl site, not duplicated on the wire).
let fragmentDecl: Node<obj> =
    node
        "frag-decl-1"
        (NodeKind.FragmentDecl
            // `Holes` and `Effect` stay where `Defaults.fragmentDecl` has them —
            // the zero-holes / pure-deterministic shape, so both keys stay off
            // the wire.
            { Defaults.fragmentDecl with
                Name = "card-template"
                Body = node "frag-body" (NodeKind.Markdown({ Text = TextSource.Literal "Template body" })) None })
        None

let fragmentRef: Node<obj> =
    node
        "frag-ref-1"
        (NodeKind.FragmentRef
            { Name = "card-template"
              // `None` ≡ the old empty Map — the key stays off the wire.
              Args = None })
        None

// Parameterised-fragment fixtures (Phase 180). The decl exercises every hole
// shape (a defaulted value hole, a bounded value hole, a slot with a kind
// constraint, a bounded Repeat) plus a non-pure effect class; the ref binds a
// value scalar + a slot subtree. These are additive — the two fixed-body
// fixtures above stay byte-identical (zero holes / zero args ⇒ fields omitted).
let fragmentDeclParam: Node<obj> =
    node
        "frag-decl-param"
        (NodeKind.FragmentDecl
            { Defaults.fragmentDecl with
                Name = "stat-card"
                Body = node "param-body" (NodeKind.Markdown({ Text = TextSource.Literal "Parameterised body" })) None
                Holes =
                    Some
                        [ HoleDecl.Value("title", HoleValueSpace.StringLen(1, 40), Some(Scalar.Str "Untitled"))
                          HoleDecl.Value("count", HoleValueSpace.IntRange(0, 100), None)
                          HoleDecl.Slot("content", Some "Display")
                          HoleDecl.Repeat("rows", HoleValueSpace.IntRange(1, 12)) ]
                Effect =
                    Some
                        { HostEffect = HostEffect.ReadsHost
                          Determinism = DeterminismSource.Clock } })
        None

let fragmentRefArgs: Node<obj> =
    node
        "frag-ref-args"
        (NodeKind.FragmentRef
            { Name = "stat-card"
              Args =
                Some(
                    Map.ofList
                        [ "count", FragmentArg.Int 7
                          "content",
                          FragmentArg.SlotArg(
                              node "slot-tree" (NodeKind.Markdown({ Text = TextSource.Literal "Bound slot" })) None
                          ) ]
                ) })
        None

// Mount fixtures (Phase 265, §4o — the isolation/embedding boundary).
// `mountMinimal` is the default-deny, out-only, zero-input degenerate shape
// (empty capabilities → the explicit `[]` default-deny posture on the wire, no
// `inputs`, no `messageShape`); `mountFull` exercises a capability list, a
// declared TwoWay channel + message shape, and both a value + slot input
// (reusing the FragmentArg encoding). `onBubble` is a closure → the
// `"<closure>"` sentinel on the wire; the guest interior is a scope reference,
// never an inlined tree.
let mountMinimal: Node<obj> =
    node
        "mount-1"
        (NodeKind.Mount
            { ScopeId = "guest-sidebar"
              // `None` ≡ the old empty Map — the key stays off the wire.
              Inputs = None
              Channel =
                { Direction = ChannelDirection.OutOnly
                  MessageShape = None }
              // `Some` — keeps the `"onBubble":"<closure>"` sentinel on the wire.
              OnBubble = Some(fun _ -> Action.Chain [])
              Capabilities = [] })
        None

let mountFull: Node<obj> =
    node
        "mount-2"
        (NodeKind.Mount
            { ScopeId = "guest-metrics"
              Inputs =
                Some(
                    Map.ofList
                        [ "title", FragmentArg.Str "Metrics"
                          "seed",
                          FragmentArg.SlotArg(
                              node
                                  "seed-tree"
                                  (NodeKind.Markdown({ Text = TextSource.Literal "Initial guest state" }))
                                  None
                          ) ]
                )
              Channel =
                { Direction = ChannelDirection.TwoWay
                  MessageShape = Some "MetricsMsg" }
              OnBubble = Some(fun _ -> Action.Chain [])
              Capabilities = [ "notify"; "call:reports.*" ] })
        None

let composite: Node<obj> =
    node
        "composite-root"
        (NodeKind.Box(
            { Layout = BoxLayout.Auto
              Role = BoxRole.Dashboard
              Heading = None
              Children =
                [ node
                      "composite-card"
                      (NodeKind.Box(
                          { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
                            Role = BoxRole.Card
                            Heading = Some(TextSource.Literal "Composite")
                            Children = [ withId "metric-2" metric; labelValueRow ]
                            KeepTogether = false
                            BreakBefore = false }
                      ))
                      None
                  stack ]
              KeepTogether = false
              BreakBefore = false }
        ))
        None

// ─── TreeOp fixtures (10 cases) ─────────────────────────────────────────

let opEditNode: TreeOp<obj> =
    TreeOp.EditNode(NodeId "metric-1", NodeKind.Markdown({ Text = TextSource.Literal "Edited" }))

let opUpdateProp: TreeOp<obj> =
    TreeOp.UpdateProp(NodeId "metric-1", "Label", PropValue.Wire(JStr "Updated revenue"))

let opReplaceBinding: TreeOp<obj> =
    TreeOp.ReplaceBinding(NodeId "metric-1", "Value", Binding.Static(Some(box 99.5)))

let opUpdateStyle: TreeOp<obj> =
    TreeOp.UpdateStyle(
        NodeId "metric-1",
        { Defaults.style with
            Tone = ToneVariant.Success
            Weight = StyleWeight.Spacious
            Emphasis = Emphasis.Loud }
    )

/// Phase 1472 — the `direction` slot on the SECOND decoder arm. `UpdateStyle`
/// reaches `SemanticStyle` through a path of its own in every host, so the
/// node-envelope vectors say nothing about it; the accept side is pinned here
/// beside the two op-arm rejects.
let opUpdateStyleDirection: TreeOp<obj> =
    TreeOp.UpdateStyle(
        NodeId "metric-1",
        { Defaults.style with
            Direction = TextDirection.Ltr }
    )

/// Phase 147 — an `UpdateStyle` op whose `SemanticStyle` carries a non-default
/// `Role` / `Voice`, exercising the optional-emit wire path at the op level.
let opUpdateStyleRoleVoice: TreeOp<obj> =
    TreeOp.UpdateStyle(
        NodeId "metric-1",
        { Defaults.style with
            Role = StyleRole.Eyebrow
            Voice = FontVoice.Structural }
    )

let opUpdateState: TreeOp<obj> =
    TreeOp.UpdateState(
        NodeId "metric-1",
        { OnLoading = Some skeleton
          OnEmpty = None
          OnError = None }
    )

let opInsertChild: TreeOp<obj> = TreeOp.InsertChild(NodeId "dash-empty", metric)

let opRemoveNode: TreeOp<obj> = TreeOp.RemoveNode(NodeId "metric-1")

let opMoveNode: TreeOp<obj> = TreeOp.MoveNode(NodeId "metric-1", NodeId "card-1")

let opReorderChildren: TreeOp<obj> =
    TreeOp.ReorderChildren(NodeId "stack-1", [ NodeId "markdown-1"; NodeId "metric-1" ])

let opBatch: TreeOp<obj> = TreeOp.Batch [ opUpdateStyle; opRemoveNode ]

// ─── Nested-path UpdateProp fixtures (Phase 364 — WIRE_FORMAT.md §3.4) ────
//
// Six primitive-valued round-trips + one OBJECT-valued round-trip + three
// canonical apply-rejects. The `value` payload is a structured JSON position
// (rule 12): an object-valued `UpdateProp.value` (a `$type` object such as a
// `TextSource`) round-trips byte-identically — the object-valued fixture
// pins exactly the class the pre-`PropValue` encoder collapsed to
// `"<opaque>"`. Targets (`grid-1` / `chart-1` / `form-1` / `tabs-1`) live in
// the apply-parity base tree, so each fixture's apply outcome is also pinned
// cross-pipeline by the Phase 192 golden — the three reject fixtures land
// there as ERR:PositionOutOfRange / ERR:FieldNotFound / ERR:PathInvalid.

let opUpdatePropNestedColumn0Label: TreeOp<obj> =
    TreeOp.UpdateProp(NodeId "grid-1", "Columns[0].Label", PropValue.Wire(JStr "Channel name"))

let opUpdatePropNestedColumn1Label: TreeOp<obj> =
    TreeOp.UpdateProp(NodeId "grid-1", "Columns[1].Label", PropValue.Wire(JStr "Spend (GBP)"))

let opUpdatePropNestedYField0: TreeOp<obj> =
    TreeOp.UpdateProp(NodeId "chart-1", "YFields[0]", PropValue.Wire(JStr "sales"))

let opUpdatePropNestedYField1: TreeOp<obj> =
    TreeOp.UpdateProp(NodeId "chart-1", "YFields[1]", PropValue.Wire(JStr "profit"))

let opUpdatePropNestedField0Required: TreeOp<obj> =
    TreeOp.UpdateProp(NodeId "form-1", "Fields[0].Required", PropValue.Wire(JBool true))

let opUpdatePropNestedField1Required: TreeOp<obj> =
    TreeOp.UpdateProp(NodeId "form-1", "Fields[1].Required", PropValue.Wire(JBool false))

/// Object-valued nested UpdateProp (rule 12): a `CellFormat` `$type` object
/// as the value — the class the pre-`PropValue` encoder collapsed to
/// `"<opaque>"` on re-encode. Byte-stable round-trip AND a clean apply
/// (grid-1's `Columns[0].Format` takes a `CellFormat`) are the pinned claims.
let opUpdatePropNestedObjectValue: TreeOp<obj> =
    TreeOp.UpdateProp(
        NodeId "grid-1",
        "Columns[0].Format",
        PropValue.Wire(JObj [ "$type", JStr "Currency"; "code", JStr "GBP" ])
    )

let opUpdatePropNestedBadIndex: TreeOp<obj> =
    TreeOp.UpdateProp(NodeId "grid-1", "Columns[9].Label", PropValue.Wire(JStr "Out of range"))

let opUpdatePropNestedBadField: TreeOp<obj> =
    TreeOp.UpdateProp(NodeId "grid-1", "Columns[0].Nope", PropValue.Wire(JStr "No such sub-field"))

let opUpdatePropNestedMalformed: TreeOp<obj> =
    TreeOp.UpdateProp(NodeId "grid-1", "Columns[x].Label", PropValue.Wire(JStr "Bad index literal"))

// ─── Binding.Local fixture ───────────────────────────────────
//
// Form whose single Text field is bound to a `Binding.Local` carrying:
//   - InitialFrom = State "salary" default 50000.0
//   - FlushOn = OnBlur
//   - OnCommit = a closure returning a placeholder Action.Chain []
//   - Format = Some <closure>
//   - Parse = <closure>
//
// The encoder writes Format / Parse / OnCommit as <closure> sentinels,
// the decoder rebuilds Format = None / Parse = placeholder-error / OnCommit
// returning the sentinel. The round-trip stays clean because re-encoding
// the decoded shape produces the same <closure> sentinel string in each
// slot.

let formLocalText: Node<obj> =
    let localFloat: Binding<string> =
        Binding.Local(
            LocalFlushTrigger.OnBlur,
            (fun (s: string) -> s),
            Binding.State("salary", Some ""),
            Some(fun _ -> box (Action.Chain []: Action<obj>)),
            (fun (raw: string) -> Ok raw)
        )

    let textField: FormField<obj> =
        { Defaults.formField with
            Id = "salary-input"
            Label = TextSource.Literal "Salary"
            Kind = FormFieldKind.Text(Some localFloat, Some(fun _ -> placeholderChain)) }

    node
        "form-local-1"
        (NodeKind.Form(
            { Defaults.form with
                Fields = [ textField ]
                OnSubmit = placeholderChain
                SubmitLabel = TextSource.Literal "Save" }
        ))
        None

// Form whose single Text field uses `Binding.Local` with FlushOn =
// OnDebounce 250 — exercises the payload-carrying flush trigger.
let formLocalDebounce: Node<obj> =
    let localDebounce: Binding<string> =
        Binding.Local(
            LocalFlushTrigger.OnDebounce 250,
            id,
            Binding.Static(Some "draft@example.com"),
            Some(fun _ -> box (Action.Chain []: Action<obj>)),
            (fun raw -> Ok raw)
        )

    let textField: FormField<obj> =
        { Defaults.formField with
            Id = "email-input"
            Label = TextSource.Literal "Email"
            Kind = FormFieldKind.Text(Some localDebounce, Some(fun _ -> placeholderChain))
            Required = true }

    node
        "form-local-debounce"
        (NodeKind.Form(
            { Defaults.form with
                Fields = [ textField ]
                OnSubmit = placeholderChain
                SubmitLabel = TextSource.Literal "Save" }
        ))
        None

// Action.CommitLocal fixture for the TreeOp round-trip suite.
let opUpdatePropCommitLocal: TreeOp<obj> =
    // A typical "Apply" button shape: an UpdateStyle on a button whose
    // OnClick = Action.CommitLocal "salary-input" — encoded as a typed
    // ButtonSpec carrying the action. Routes through UpdateProp's
    // Binding/style/state slots rather than introducing a fresh op kind;
    // the Action surfaces via the canonical ButtonSpec.OnClick path.
    TreeOp.UpdateStyle(
        NodeId "btn-apply",
        { Defaults.style with
            Tone = ToneVariant.Brand }
    )

// Binding.Format (Phase 102) — a Stack of Markdown nodes whose Text is a
// `TextSource.Bound(Binding.Format(...))`, exercising every Format case
// (Number / Currency / Percent / Date / RelativeTime) and both LocaleSource
// variants (Explicit + Ambient) across locales. The numeric source is a
// `Binding.Static` so the round-trip is fully faithful.
let formatBindings: Node<obj> =
    let md (id: string) (b: Binding<string>) : Node<obj> =
        node id (NodeKind.Markdown({ Text = TextSource.Bound b })) None

    node
        "format-bindings"
        (NodeKind.Box(
            { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
              Role = BoxRole.Group
              Heading = None
              Children =
                [ md
                      "fmt-number"
                      (Binding.Format(Binding.Static(Some 1234.5), Format.Number(Some 2), LocaleSource.Explicit "en-US"))
                  md
                      "fmt-currency"
                      (Binding.Format(Binding.Static(Some 1234.5), Format.Currency "GBP", LocaleSource.Explicit "en-GB"))
                  md
                      "fmt-percent"
                      (Binding.Format(Binding.Static(Some 0.42), Format.Percent None, LocaleSource.Ambient))
                  md
                      "fmt-date"
                      (Binding.Format(
                          Binding.Static(Some 1700000000.0),
                          Format.Date DateStyle.Medium,
                          LocaleSource.Explicit "fr-FR"
                      ))
                  md
                      "fmt-relative"
                      (Binding.Format(
                          Binding.Static(Some(-3.0)),
                          Format.RelativeTime RelativeTimeUnit.Day,
                          LocaleSource.Explicit "en-US"
                      )) ]
              KeepTogether = false
              BreakBefore = false }
        ))
        None

// ─── Phase 818 — the reactive-derivation first cut ────────────────────────
//
// Four wire shapes, one rule (any read slot may take a Binding; subscription
// semantics; the Transform verbs stay the only computation vocabulary):
//   1. a LIVE State-sourced Transform — the Tier-D count badge done right
//      (the 815 snapshot upgraded: the source binding is preserved, its
//      carried defaultValue is the initial snapshot);
//   2. `SetState.valueFrom` — a derived state write (value XOR valueFrom);
//   3. `sortStateKey` — the data-bound grid-sort header affordance.
// (`Switch.on` shipped with Phase 768 — `switchOnSelection` above pins it.)

/// The Tier-D count badge, live: a Badge whose label derives from a
/// State-carried request log (initial rows in the binding's defaultValue), so
/// a `SetState("request-log", …)` re-derives the count.
let badgeTransformLive: Node<obj> =
    let defaultRows =
        JArr
            [ JObj [ "medication", JStr "Amoxicillin"; "quantity", JInt 20 ]
              JObj [ "medication", JStr "Ibuprofen"; "quantity", JInt 50 ] ]

    let source: Binding<JVal> = Binding.State("request-log", Some defaultRows)

    let initial =
        match Fuaran.UI.HostPrelude.TransformLive.initialSource defaultRows with
        | Ok ds -> ds
        | Error e -> failwithf "badgeTransformLive initial snapshot failed: %A" e

    let pipeline =
        [ Fuaran.Core.GroupBy(
              [],
              [ { Name = "n"
                  Fn = Fuaran.Core.AggFn.Count
                  Of = "medication" } ]
          ) ]

    node
        "badge-transform-live"
        (NodeKind.Badge(
            { Label = TextSource.Bound(Binding.Transform(TransformSource.Live(source, initial), pipeline, None))
              Variant = BadgeVariant.Info }
        ))
        None

/// Phase 1075 — the shared-data-source charter's §3.1 pair, on the wire: ONE
/// declared table, TWO readers.
///
/// A `DataGrid` bound to `$state.members` carries the rows on its own
/// `defaultValue`; a `Badge` beside it derives a `groupBy`/`count` over the
/// SAME key and carries no data of its own. Under the seeding rule the grid's
/// declaration seeds the slot and the badge counts the grid's rows — before it,
/// `defaultValue` was a per-reader fallback, the badge's live source started
/// from `TransformLive.emptySource`, and the pair rendered a derived value that
/// was silently wrong with nothing red anywhere.
///
/// **Why the badge's source carries NO `defaultValue` at all.** It spelled
/// `"defaultValue":[]` until the polyglot adoption completed, and the reason was
/// a host-parity one rather than a preference: the corpus is a shared gate, and
/// two of the polyglot hosts still refused the bare form when fuaran#1085 landed
/// the leniency on the reference hosts. Every host now accepts
/// `{"$type":"State","key":k}` as a live source over the empty initial snapshot,
/// so the corpus is respelled to the bare form — which is the spelling that says
/// what the badge means. The badge declares no data; the empty ARRAY is a
/// declaration of an empty table, and the empty-declaration rule should stay the
/// answer for a genuinely empty live collection rather than double as the
/// workaround for a source that declares nothing. Either spelling seeds nothing
/// — an unseeded slot already resolves to the empty table — so the pair reads
/// the same whichever order the two nodes appear in.
///
/// This fixture is also the pin for the empty-array leniency itself, which the
/// F# host has read this way since 0.23.1 and this corpus never carried: the
/// TypeScript host refused it until Phase 1075, so ONE document decoded on one
/// reference host and not the other.
let sharedSourceSeededPair: Node<obj> =
    let rows: Fuaran.Core.Row seq =
        Seq.ofList
            [ (Map.ofList [ "team", Unchecked.nonNull (box "Ops") ]: Fuaran.Core.Row)
              (Map.ofList [ "team", Unchecked.nonNull (box "Research") ]: Fuaran.Core.Row) ]

    let grid =
        node
            "member-grid"
            (NodeKind.DataGrid(
                { SortStateKey = None
                  PageSize = None
                  PageStateKey = None
                  EditStateKey = None
                  DefaultSort = None
                  Source = Binding.State("members", Some rows)
                  RowKey = None
                  RowKeyField = Some "team"
                  Columns =
                    [ { Label = "Team"
                        Value = None
                        Field = Some "team"
                        Sortable = None
                        Editable = None
                        Format = CellFormat.None
                        Kind = CellKindErased.Text
                        Width = ColumnWidth.Auto } ]
                  OnRowClick = None
                  Editable = false
                  Reorderable = false
                  TransferInKey = None
                  TransferOutKey = None
                  StaticRows = None
                  KeepRowsTogether = false
                  RepeatHeader = false }
            ))
            None

    let derivedSource: Binding<JVal> = Binding.State("members", None)

    let badge =
        node
            "member-count"
            (NodeKind.Badge(
                { Label =
                    TextSource.Bound(
                        Binding.Transform(
                            TransformSource.Live(derivedSource, Fuaran.UI.HostPrelude.TransformLive.emptySource),
                            [ Fuaran.Core.GroupBy(
                                  [],
                                  [ { Name = "n"
                                      Fn = Fuaran.Core.AggFn.Count
                                      Of = "team" } ]
                              ) ],
                            None
                        )
                    )
                  Variant = BadgeVariant.Info }
            ))
            None

    node
        "shared-source-seeded-pair"
        (NodeKind.Box(
            { Layout = BoxLayout.Auto
              Role = BoxRole.Dashboard
              Heading = None
              Children = [ grid; badge ]
              KeepTogether = false
              BreakBefore = false }
        ))
        None

/// Phase 861 — sort on a DATA-BOUND grid: per-column `sortable` narrowing plus
/// a declared initial order, reusing the `defaultSort` record and field name the
/// `staticRows` path already carries (Phase 801). The middle column opts OUT —
/// the declaration "implied by omission" could not previously express.
let gridBoundSort: Node<obj> =
    let col (label: string) (field: string) (sortable: bool option) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = sortable
          Editable = None
          Format = CellFormat.None
          Kind = CellKindErased.Text
          Width = ColumnWidth.Auto }

    node
        "grid-bound-sort"
        (NodeKind.DataGrid(
            { SortStateKey = Some "ledger-sort"
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort =
                Some
                    { Column = 1
                      Direction = SortDirection.Desc }
              Source = Binding.State("ledger", Some(Seq.ofList planRows))
              RowKey = None
              RowKeyField = Some "month"
              Columns =
                [ col "Month" "month" None
                  col "Revenue" "revenue" None
                  col "Note" "note" (Some false) ]
              OnRowClick = None
              Editable = false
              Reorderable = false
              TransferInKey = None
              TransferOutKey = None
              StaticRows = None
              KeepRowsTogether = false
              RepeatHeader = false }
        ))
        None

/// Phase 863 — the grid's write side, declared: `editStateKey` names where an
/// edit commits, and the third column is explicitly read-only under a
/// grid-level `editable: true`. Both are declarations that previously had no
/// spelling — the destination was a closure erasing to `"<closure>"`, and
/// read-only-ness was implied by omission.
let gridDeclaredEdit: Node<obj> =
    let col (label: string) (field: string) (editable: bool option) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = None
          Editable = editable
          Format = CellFormat.None
          Kind = CellKindErased.Text
          Width = ColumnWidth.Auto }

    node
        "grid-declared-edit"
        (NodeKind.DataGrid(
            { SortStateKey = None
              PageSize = None
              PageStateKey = None
              EditStateKey = Some "stock-adjustments"
              DefaultSort = None
              Source = Binding.Query("stock", (fun _ -> Seq.ofList planRows), None)
              RowKey = None
              RowKeyField = Some "month"
              Columns =
                [ col "Month" "month" None
                  col "Revenue" "revenue" None
                  col "Note" "note" (Some false) ]
              OnRowClick = None
              Editable = true
              Reorderable = false
              TransferInKey = None
              TransferOutKey = None
              StaticRows = None
              KeepRowsTogether = false
              RepeatHeader = false }
        ))
        None

/// Phase 934 — declarative row reorder: `reorderable` (omit-when-false, as
/// `editable`) asks the renderer for its drag + keyboard row-move affordance,
/// and the moved rows commit as the WHOLE updated rows value to the same
/// destination an edit uses — `editStateKey` here, the Phase-663 State-source
/// floor otherwise. One collection, one destination; a reorder IS an edit of
/// the row order.
let gridReorderable: Node<obj> =
    let col (label: string) (field: string) (kind: CellKindErased<obj>) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = kind
          Width = ColumnWidth.Auto }

    let sprintRows: Row list =
        [ Map.ofList [ "task", box "Design"; "rank", box 1 ]
          Map.ofList [ "task", box "Build"; "rank", box 2 ]
          Map.ofList [ "task", box "Verify"; "rank", box 3 ] ]

    node
        "grid-reorderable"
        (NodeKind.DataGrid(
            { SortStateKey = None
              PageSize = None
              PageStateKey = None
              EditStateKey = Some "sprint-order"
              DefaultSort = None
              Source = Binding.State("sprint-order", Some(Seq.ofList sprintRows))
              RowKey = None
              RowKeyField = Some "task"
              Columns =
                [ col "Task" "task" CellKindErased.Text
                  col "Rank" "rank" CellKindErased.Numeric ]
              OnRowClick = None
              Editable = false
              Reorderable = true
              TransferInKey = None
              TransferOutKey = None
              StaticRows = None
              KeepRowsTogether = false
              RepeatHeader = false }
        ))
        None

/// Phase 1123 — cross-container transfer: THE canonical corner, and the first
/// fixture in this corpus whose subject is a RELATION BETWEEN TWO NODES rather
/// than one node's declaration. A single grid could not carry it: `transferOutKey`
/// and `transferInKey` name one shared State key from the two sides, and a
/// document holding only one side declares a capability with no counterpart
/// (which is what FUARAN129 reports).
///
/// The board shape, deliberately: two columns each declaring BOTH ends of the key
/// `board`, so cards move in either direction, plus an `archive` column that
/// accepts and never releases — the one-way end that is the whole reason the pair
/// is two fields rather than one symmetric key. Every column names `rowKeyField`,
/// because the record a drop writes identifies the moved row and a closure erases
/// to `"<closure>"` on the wire.
///
/// The transfer record itself is NOT in this fixture and cannot be: it is a STATE
/// value a reader's gesture writes, not a member of any node. `WIRE_FORMAT.md`
/// §3.6.11 fixes its shape normatively, exactly as it fixes the sort descriptor's.
let gridTransferBoard: Node<obj> =
    let col (label: string) (field: string) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = CellKindErased.Text
          Width = ColumnWidth.Auto }

    let cards (titles: string list) : Row list =
        titles |> List.map (fun t -> Map.ofList [ "card", box t ])

    let column (id: string) (key: string) (outKey: string option) (inKey: string option) (titles: string list) =
        node
            id
            (NodeKind.DataGrid(
                { SortStateKey = None
                  PageSize = None
                  PageStateKey = None
                  EditStateKey = None
                  DefaultSort = None
                  Source = Binding.State(key, Some(Seq.ofList (cards titles)))
                  RowKey = None
                  RowKeyField = Some "card"
                  Columns = [ col "Card" "card" ]
                  OnRowClick = None
                  Editable = false
                  Reorderable = true
                  TransferInKey = inKey
                  TransferOutKey = outKey
                  StaticRows = None
                  KeepRowsTogether = false
                  RepeatHeader = false }
            ))
            None

    node
        "transfer-board"
        (NodeKind.Box
            { Children =
                [ column "todo" "board-todo" (Some "board") (Some "board") [ "Draft the brief"; "Size the work" ]
                  column "doing" "board-doing" (Some "board") (Some "board") [ "Write the walk" ]
                  // Accepts and never releases: a card filed here stays filed.
                  column "archive" "board-archive" None (Some "board") [] ]
              Heading = Some(TextSource.Literal "Sprint board")
              Layout = BoxLayout.Auto
              Role = BoxRole.Group
              KeepTogether = false
              BreakBefore = false })
        None

/// Phase 862 — declarative pagination: `pageStateKey` names the State slot
/// carrying `{"page": N}` (1-based) and `pageSize` how many rows a page holds.
/// The pager that writes the key is renderer-owned, so the tree names the
/// behaviour and never a control — which is why there is no pager node here to
/// pair the grid with.
let gridPaged: Node<obj> =
    let col (label: string) (field: string) (kind: CellKindErased<obj>) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = kind
          Width = ColumnWidth.Auto }

    node
        "grid-paged"
        (NodeKind.DataGrid(
            { SortStateKey = None
              PageSize = Some 20
              PageStateKey = Some "members-page"
              EditStateKey = None
              DefaultSort = None
              Source = Binding.State("members", Some(Seq.ofList planRows))
              RowKey = None
              RowKeyField = Some "month"
              Columns =
                [ col "Month" "month" CellKindErased.Text
                  col "Revenue" "revenue" CellKindErased.Numeric ]
              OnRowClick = None
              Editable = false
              Reorderable = false
              TransferInKey = None
              TransferOutKey = None
              StaticRows = None
              KeepRowsTogether = false
              RepeatHeader = false }
        ))
        None

/// Phase 862 — paging and sorting compose on one grid: two behaviours, two
/// state keys, one rule. Present as a fixture because the pair is the shape the
/// charter's "one rule, three instances" claim is actually cashed in, and a
/// host that special-cased either would round-trip this one wrongly.
let gridPagedSorted: Node<obj> =
    let col (label: string) (field: string) (kind: CellKindErased<obj>) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = kind
          Width = ColumnWidth.Auto }

    node
        "grid-paged-sorted"
        (NodeKind.DataGrid(
            { SortStateKey = Some "ledger-sort"
              PageSize = Some 10
              PageStateKey = Some "ledger-page"
              EditStateKey = None
              DefaultSort = None
              Source = Binding.State("ledger", Some(Seq.ofList planRows))
              RowKey = None
              RowKeyField = Some "month"
              Columns =
                [ col "Month" "month" CellKindErased.Text
                  col "Revenue" "revenue" CellKindErased.Numeric ]
              OnRowClick = None
              Editable = false
              Reorderable = false
              TransferInKey = None
              TransferOutKey = None
              StaticRows = None
              KeepRowsTogether = false
              RepeatHeader = false }
        ))
        None

/// A derived state write: clicking the button writes the SELECTED row's `id`
/// field to the `chosen-id` state slot — `valueFrom` evaluated at dispatch
/// time, no closure, no literal.
let buttonSetStateValueFrom: Node<obj> =
    let selectedId: Binding<JVal> =
        Binding.Selection("orders-grid", Binding.projectSelectionField<JVal> "id", None, Some "id")

    node
        "button-setstate-valuefrom"
        (NodeKind.Button(
            { Defaults.button with
                Label = TextSource.Literal "Track this order"
                OnClick = Action.SetState("chosen-id", None, Some selectedId) }
        ))
        None

/// Phase 1124 — `Action.Print`, the vocabulary's first PAYLOAD-FREE action, and
/// the fixture exists as much for the SHAPE as for the case: `{"$type":"Print"}`
/// is the entire encoding, so this is the corpus's proof that a discriminator on
/// its own round-trips through every host with nothing else to carry.
///
/// The chain is deliberate rather than decorative. A bare `Print` would exercise
/// the case; a `Print` INSIDE a `Chain` exercises the thing a payload-free member
/// can plausibly break — an encoder that emits an object with no members, or a
/// decoder that requires one, fails differently in a list than alone, and "print
/// this invoice, then tell the host it was printed" is the shape a real document
/// reaches for.
///
/// What is NOT here, and cannot be: any statement about HOW to print. No page
/// size, no margin, no sheet range, no copies, no target subtree. The paged
/// medium is host chrome under the ratified `PrintLayout` charter row, and every
/// remaining parameter belongs to the dialogue the reader operates — which is
/// why the sibling reject vector refuses a member here rather than dropping one.
let buttonPrint: Node<obj> =
    node
        "button-print"
        (NodeKind.Button(
            { Defaults.button with
                Label = TextSource.Literal "Print this invoice"
                OnClick = Action.Chain [ Action.Print; Action.Notify("printed", JStr "invoice") ] }
        ))
        None

/// The data-bound grid-sort affordance: `sortStateKey` names the State slot
/// carrying `{column, direction}`; the runtime renders sortable headers
/// (field-named columns only) and sorts resolved rows by the descriptor.
let gridSortStateKey: Node<obj> =
    let col (label: string) (field: string) (kind: CellKindErased<obj>) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = kind
          Width = ColumnWidth.Auto }

    node
        "grid-sort-state-key"
        (NodeKind.DataGrid(
            { SortStateKey = Some "inventory-sort"
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort = None
              Source = Binding.State("inventory", Some(Seq.ofList planRows))
              RowKey = None
              RowKeyField = Some "month"
              Columns =
                [ col "Month" "month" CellKindErased.Text
                  col "Revenue" "revenue" CellKindErased.Numeric ]
              OnRowClick = None
              Editable = false
              Reorderable = false
              TransferInKey = None
              TransferOutKey = None
              StaticRows = None
              KeepRowsTogether = false
              RepeatHeader = false }
        ))
        None

// ─── Accessibility-trait family (Phase 955) ─────────────────────────────
//
// The `accessibility` node slot (WIRE_FORMAT §3.1) had NO corpus fixture at
// all before this family: every `"role"` in the corpus was a `BoxSpec` role,
// so the trait's six slots were exercised nowhere and the wire-to-host leg of
// the ARIA projection was host-invented rather than corpus-certified. Three
// independent findings converged on the same hole — an entire projection slot
// (`hidden` → `aria-hidden`) was missing from two hosts with every conformance
// gate green, and both native surfaces were covered only by hand-built nodes.
//
// What the family pins, and why each piece is here rather than folded into a
// smaller set:
//
//  * **All six slots in one payload** (`a11y-wrapper-all-slots`) — slot
//    independence and canonical key order are only assertable when every slot
//    is present at once. `labelledBy` / `describedBy` name REAL sibling ids in
//    the same tree, so a host that resolves the reference has something to
//    resolve; a dangling id would certify the string and not the reference.
//  * **Both `Binding` forms of `label` and `hidden`** — `Static` here, `State`
//    on the state-bound wrapper. The two are the same slot and different
//    decode paths, and the defect that raised this family was precisely a host
//    reading one form only.
//  * **Both role classes** — a named lower-case role (`region` / `alert` /
//    `button`, which decode to closed `AriaRole` cases) and a deliberately
//    MIXED-CASE custom role (`doc-pageFooter`). The custom arm is verbatim
//    passthrough, so a host that lower-cases or folds it silently rewrites an
//    author's role — the exact fold bug that motivated naming this case.
//  * **All three `liveRegion` tokens** — `polite`, `off`, `assertive`.
//  * **Both placement shapes** — the trait on an ordinary wrapper kind (Box)
//    and on the semantic-element kinds (`Link`, `Button`, `Image`), because a
//    placement-sensitive consumer projects them differently and needs both.

/// The maximal shape: every one of the six slots populated at once, on an
/// ordinary wrapper. `hidden` is an explicit `Static false` — distinct on the
/// wire from an omitted `hidden`, and a host that collapses the two loses the
/// author's explicit "this is NOT hidden".
let a11yWrapperAllSlots: Node<obj> =
    node
        "a11y-wrapper-all-slots"
        (NodeKind.Box(
            { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
              Role = BoxRole.Group
              Heading = None
              Children = [ withId "a11y-wrapper-heading" heading; withId "a11y-wrapper-note" markdown ]
              KeepTogether = false
              BreakBefore = false }
        ))
        (Some
            { Label = Some(Binding.Static(Some "Channel performance summary"))
              LabelledBy = Some "a11y-wrapper-heading"
              DescribedBy = Some "a11y-wrapper-note"
              Role = Some AriaRole.Region
              LiveRegion = Some LiveRegionKind.Polite
              Hidden = Some(Binding.Static(Some false)) })

/// The bound shape: `label` and `hidden` as `State` bindings rather than
/// literals, plus the mixed-case custom role and the `off` live-region token.
/// A collapsed footer whose accessible name and hidden-ness both follow host
/// state is the ordinary reason these slots are bindings at all.
let a11yWrapperStateBound: Node<obj> =
    node
        "a11y-wrapper-state-bound"
        (NodeKind.Box(
            { Layout = BoxLayout.Flex(Orientation.Horizontal, false, None)
              Role = BoxRole.Group
              Heading = None
              Children = [ withId "a11y-footer-note" markdown ]
              KeepTogether = false
              BreakBefore = false }
        ))
        (Some
            { Label = Some(Binding.State("footerLabel", Some "Site footer"))
              LabelledBy = None
              DescribedBy = None
              // Mixed case on purpose: `AriaRole.Custom` is verbatim
              // passthrough, so this fixture goes red on any host that folds
              // case anywhere along the wire-to-attribute path.
              Role = Some(AriaRole.Custom "doc-pageFooter")
              LiveRegion = Some LiveRegionKind.Off
              Hidden = Some(Binding.State("footerCollapsed", Some false)) })

/// The announcement shape: a named role plus the `assertive` token, the
/// combination a host must project as `role="alert"` + `aria-live="assertive"`.
let a11yAlertAssertive: Node<obj> =
    node
        "a11y-alert-assertive"
        (NodeKind.Callout(
            { Defaults.callout with
                Tone = ToneVariant.Critical
                Heading = Some(TextSource.Literal "Upload failed")
                Body = TextSource.Literal "The file exceeded the size limit."
                Icon = Some "alert"
                Dismissable = true }
        ))
        (Some
            { Label = None
              LabelledBy = None
              DescribedBy = None
              Role = Some AriaRole.Alert
              LiveRegion = Some LiveRegionKind.Assertive
              Hidden = None })

/// Semantic element 1 of 3 — the accessible name OVERRIDING the visible text.
/// The canonical "Read more" link: the trait's `label` is what a screen reader
/// announces, and a host that renders the visible label into `aria-label` (or
/// drops the slot) is indistinguishable from a conformant one without this.
let a11yLinkLabelled: Node<obj> =
    node
        "a11y-link-labelled"
        (NodeKind.Link(
            { Defaults.link with
                Href = Binding.Static(Some "/reports/2026-annual.pdf")
                Label = TextSource.Literal "Read more" }
        ))
        (Some
            { Label = Some(Binding.Static(Some "Read the 2026 annual report (PDF)"))
              LabelledBy = None
              DescribedBy = None
              Role = None
              LiveRegion = None
              Hidden = None })

/// Semantic element 2 of 3 — an explicit named role on a kind that already has
/// a native one. Redundant by intent: the trait's `role` must win over whatever
/// element the host chose, and only a case where the two DISAGREE about which
/// wins would be observable, so pinning agreement is the honest first step.
let a11yButtonNamed: Node<obj> =
    node
        "a11y-button-named"
        (NodeKind.Button(
            { Defaults.button with
                Label = TextSource.Literal "Refresh"
                OnClick = placeholderChain
                Icon = Some "refresh" }
        ))
        (Some
            { Label = Some(Binding.Static(Some "Refresh revenue figures"))
              LabelledBy = None
              DescribedBy = None
              Role = Some AriaRole.Button
              LiveRegion = None
              Hidden = None })

/// Semantic element 3 of 3 — the decorative image: empty `alt` plus
/// `hidden: Static true`, the `aria-hidden` projection slot that was missing
/// from two hosts entirely. `Static true` is the second of the two `hidden`
/// binding forms this family pins (the State form is on the bound wrapper).
let a11yImageDecorative: Node<obj> =
    node
        "a11y-image-decorative"
        (NodeKind.Image(
            { Defaults.image with
                Src = Binding.Static(Some "/img/section-divider.svg")
                Alt = TextSource.Literal "" }
        ))
        (Some
            { Label = None
              LabelledBy = None
              DescribedBy = None
              Role = None
              LiveRegion = None
              Hidden = Some(Binding.Static(Some true)) })

// ─── Public collections ─────────────────────────────────────────────────

/// Phase 380 — the certified fragment library's wire fixtures: for every
/// fragment in `Fuaran.UI.Fragments.Stdlib.all`, its DECLARATION and one
/// representative APPLICATION.
///
/// Generated from the library rather than transcribed beside it, deliberately.
/// The library's promise is that every conformant host carries it identically,
/// and a hand-copied fixture makes that promise about a copy: a fragment whose
/// declaration changed would keep certifying against the new shape while the
/// corpus went on pinning the old one, and nothing would be red. Derived this
/// way, a change moves the fixture bytes in the same emit and the round-trip
/// gate is what notices.
///
/// These add no new wire vocabulary — they are ordinary `FragmentDecl` /
/// `FragmentRef` nodes over hole and argument shapes the `frag-decl-param` /
/// `frag-ref-args` pair already exercises in every host.
let stdlibFragments: (string * Node<obj>) list =
    Fuaran.UI.Fragments.Stdlib.all<obj>
    |> List.collect (fun f ->
        [ sprintf "FragmentDecl (stdlib '%s' — %s)" f.Name f.Summary, f.Decl
          sprintf "FragmentRef (stdlib '%s' — a representative application)" f.Name, f.Example ])

// ─── The node-level tooltip trait (Phase 1112) ──────────────────────────────
//
// Three, and between them they pin every placement decision the trait forces.
// The trait itself is one optional `TextSource` on the node envelope, so the
// BYTES are almost too simple to be worth a family — what is worth pinning is
// that the slot exists at the envelope, that it takes every `TextSource` arm,
// and that it composes with the `accessibility` trait beside it rather than
// competing with it.
//
// The RENDER obligations these exist to be measured against — `aria-describedby`
// on the element that takes focus, the hint as a CHILD of the hover target so it
// is hoverable, the wrapper focus stop where the body is not one — are not
// expressible in wire bytes and live in the SSR corpus and the spec.

/// A hint on an interactive kind whose a11y projection forwards: the
/// description rides the `<button>` itself, because that is the element the
/// keyboard lands on. `Literal`, the ordinary authored case.
let tooltipButton: Node<obj> =
    { node
          "tooltip-button-1"
          (NodeKind.Button(
              { Defaults.button with
                  Label = TextSource.Literal "Rebuild index"
                  OnClick = Action.Notify("rebuild", Fuaran.Core.JObj [])
                  Variant = ButtonVariant.Secondary }
          ))
          None with
        Tooltip = Some(TextSource.Literal "Re-reads every document; takes about a minute on this corpus.") }

/// A hint on a DISPLAY kind, which forwards nothing — so the description and
/// the focus stop both land on the wrapper, and a host that only implemented
/// the forwarding arm renders a hint no keyboard can reach.
///
/// `I18n` deliberately: a hint is CONTENT, so it is translated like any other
/// content, and a host that modelled the slot as a bare string round-trips the
/// literal arm perfectly and drops this one.
let tooltipMetric: Node<obj> =
    { node
          "tooltip-metric-1"
          (NodeKind.Metric(
              { Defaults.metric with
                  Label = TextSource.Literal "Median latency"
                  Value = Binding.Static(Some 128.0)
                  TrendPolarity = TrendPolarity.LowerIsBetter }
          ))
          None with
        Tooltip = Some(TextSource.I18n("metric.latency.hint", Map.empty)) }

/// The headline case the trait was admitted for: an icon-only button.
///
/// Both slots are present and they say DIFFERENT things — `accessibility.label`
/// NAMES the control (its own text is empty, so without one it is announced as
/// nothing at all), and the tooltip DESCRIBES it. A host that mapped the hint
/// onto the accessible name passes every other fixture here and turns this one
/// into a control with two competing names and no description; a host that
/// dropped the name renders a button announced as "button".
///
/// It also pins the `aria-describedby` MERGE: `describedBy` names a real
/// sibling in the same tree, so the emitted attribute has to carry both ids in
/// a list rather than whichever the renderer applied last.
let tooltipIconButton: Node<obj> =
    node
        "tooltip-icon-button-1"
        (NodeKind.Box(
            { Layout = BoxLayout.Flex(Orientation.Horizontal, false, None)
              Role = BoxRole.Group
              Heading = None
              Children =
                [ { node
                        "tooltip-icon-button-control"
                        (NodeKind.Button(
                            { Defaults.button with
                                Label = TextSource.Literal ""
                                OnClick = Action.Notify("export", Fuaran.Core.JObj [])
                                Variant = ButtonVariant.Tertiary
                                Icon = Some "download" }
                        ))
                        (Some
                            { Label = Some(Binding.Static(Some "Download CSV"))
                              LabelledBy = None
                              DescribedBy = Some "tooltip-icon-button-note"
                              Role = None
                              LiveRegion = None
                              Hidden = None }) with
                      Tooltip = Some(TextSource.Literal "Exports the rows currently shown, not the whole table.") }
                  withId "tooltip-icon-button-note" markdown ]
              KeepTogether = false
              BreakBefore = false }
        ))
        None


let allNodes: (string * Node<obj>) list =
    [ "Display/Heading", heading
      "Display/Markdown (Phase 147 Role=Data + Voice=Display)", styleRoleVoice
      "Display/Markdown (Phase 1472 Direction=Ltr, the only style member)", styleDirectionLtrRun
      "Layout/Box (Phase 1472 an Ltr value isolated inside Rtl prose)", styleDirectionIsolatedValue
      "Display/Markdown", markdown
      "Display/Metric", metric
      "Display/Metric (float divergence-zone — 1e21 scientific)", metricFloatExpPos
      "Display/Metric (float divergence-zone — 1e-7 scientific)", metricFloatExpNeg
      "Display/Metric (float divergence-zone — 17 significant digits)", metricFloat17Sig
      "Display/Metric (float divergence-zone — integer > 2^53)", metricFloatBigInt
      "Display/Badge", badge
      "Display/Link", link
      "Display/Link (protected email — Phase 812 protection field)", linkProtected
      "Display/Image (Avatar variant)", image
      "Display/Image (Phase 1077 — fit / aspectRatio / loading all off-default)", imagePresentation
      "Display/Image (Phase 1078 — literal caption)", imageCaption
      "Display/Image (Phase 1078 — i18n caption with args)", imageCaptionI18n
      "Display/Image (Phase 1080 — three-candidate srcSet, authored descending)", imageSrcset
      "Display/Image (Phase 1079 — expandable, the declaration alone)", imageExpandable
      "Display/Image (Phase 1079 — expandable + caption + srcSet, the gallery thumbnail)", imageExpandableFigure
      "Display/Media (Phase 1076 — video at its minimum, both bool defaults omitted)", mediaVideo
      "Display/Media (Phase 1076 — video with a poster frame in the case payload)", mediaVideoPoster
      "Display/Media (Phase 1076 — autoplay declared, both shared bools off-default)", mediaVideoAutoplay
      "Display/Media (Phase 1076 — the Audio variant, whose payload is the discriminator alone)", mediaAudio
      "Display/Media (Phase 1110 — one captions track, elected default)", mediaVideoCaptions
      "Display/Media (Phase 1110 — three tracks, authored order, two default elections)", mediaVideoTracks2
      "Display/Media (Phase 1110 — the audio transcript floor)", mediaAudioTranscript
      "Display/Embed (Phase 1111 — the minimum: no ratio, no permissions, total denial)", embedMinimal
      "Display/Embed (Phase 1111 — a declared aspect ratio, sharing Image's enum)", embedAspect
      "Display/Embed (Phase 1111 — one permission, the one that rides `allow` not `sandbox`)", embedPermissions
      "Display/Tree (Phase 1120 — two levels, no State key: static and fully expanded)", treeStatic
      "Display/Tree (Phase 1120 — three levels, the expansion key named; the key IS the affordance)", treeExpandedKeyed
      "Display/Tree (Phase 1120 — the selection key AND the handler, both declared)", treeSelectionKeyed
      "Display/List (ordered)", listDisplay
      "Display/Toast (Success tone, open)", toast
      "Display/CodeBlock (fsharp, line numbers + highlights)", codeBlock
      "Display/Math (block LaTeX)", math
      "Display/Drawing (all shapes + curve commands + styled bindings)", drawing
      "Display/Drawing (degenerate — empty)", drawingMinimal
      "Display/Drawing (Phase 877 — rotated Labels, incl. explicit 0 and 2-dp fraction)", drawingRotatedLabels
      "Display/Drawing (Phase 883 — tipped shapes, incl. hostile text, Bound tip and explicit empty)",
      drawingTippedShapes
      "Display/Sparkline", sparkline
      "Display/Drawing (Phase 1063 — §5/§7 non-finite sentinels at typed float scalars and a nested shape coordinate)",
      drawingNonfiniteSentinels
      "Display/Sparkline (Phase 1063 — §5/§7 non-finite sentinels as float SEQUENCE elements)",
      sparklineNonfiniteSentinel
      "Display/Metric (Phase 1063 — §5/§7 non-finite sentinels behind a Binding.Static envelope)",
      metricNonfiniteSentinel
      "Display/Skeleton", skeleton
      "Display/Callout", callout
      "Display/Progress", progress
      "Display/LabelValueRow", labelValueRow
      "Display/Fact", fact
      "Display/Metric (Phase 819 — CellFormat.Duration value + cell RelativeTime trend)", metricDuration
      "Display/Metric (Phase 867 — inverted trendPolarity: a falling wait time is an improvement)",
      metricInvertedPolarity
      "Display/Icon (Phase 821 — decorative, no label, Large)", iconDecorative
      "Layout/Dashboard (empty)", dashboardEmpty
      "Layout/Stack", stack
      "Layout/Grid", gridLayout
      "Layout/Grid (TemplateColumns 1fr 2fr ratio)", gridLayoutTemplatedRatio
      "Layout/Grid (TemplateColumns 100px + repeat fixed-plus-flex)", gridLayoutTemplatedFixedPlusFlex
      "Layout/Grid (TemplateColumns auto-fit minmax)", gridLayoutTemplatedAutoFit
      "Layout/Masonry (Phase 1082 — column-fill, three columns)", masonryLayout
      "Layout/Masonry (Phase 1082 — explicit gap)", masonryLayoutGap
      "Layout/SplitPanel", splitPanel
      "Layout/Tabs", tabs
      "Layout/Tabs (explicit headers + tags + activeTag)", tabsExplicitHeaders
      "Layout/Tabs (composite — containers inside a wrapper, grid panel, pre-filled controls)", compositeTabsPanels
      "Layout/Card", card
      "Layout/Stepper", stepper
      "Layout/SummaryList", summaryList
      "Layout/Disclosure", disclosure
      "Layout/Modal (heading + child + onDismiss)", modal
      "Layout/Modal (Phase 1119 — Popover modality, anchored, state-bound open)", popoverAnchored
      "Layout/Modal (Phase 1119 — Popover, statically open: the SSR floor)", popoverOpen
      "Layout/ScrollArea (vertical, maxHeight)", scrollArea
      "Input/Form (all fields)", formAllFields
      "Input/Form (Phase 766 — the Toggle switch affordance beside a Checkbox)", formToggle
      "Input/Form (RangedNumber — all/min-only/no bounds)", formRangedNumber
      "Input/Form (Local-bound text, OnBlur)", formLocalText
      "Input/Form (Local-bound text, OnDebounce 250)", formLocalDebounce
      "Input/Filters (text + choice)", filtersBoth
      "Input/Filters (declarative — omitted onChange + typed range bounds)", filtersDeclarative
      "Input/Form (SegmentedChoice horizontal + vertical)", formSegmentedChoice
      "Input/Form (Phase 1113 — Combobox: static options, allowFreeText omitted)", formComboboxStatic
      "Input/Form (Phase 1113 — Combobox: Query-bound option source, declarative, auto-bound value)", formComboboxQuery
      "Input/Form (Phase 1113 — Combobox: allowFreeText, value outside the option set)", formComboboxFreeText
      "Input/Form (Date — date/time/datetime variants + bounds)", formDate
      "Input/Form (Phase 725 — DateRange: single-control date range, bare {from,to} pair + bounds)", formDateRange
      "Input/Filters (Phase 725 — DateRange chip: one filter param carries the pair, value auto-bound)",
      filtersDateRange
      "Input/Filters (SegmentedFilter horizontal)", filtersSegmented
      "Input/Button", button
      "Input/Button (Action.WriteToClipboard chained with Dispatch)", buttonClipboard
      "Input/Button (Action.ReadFileBody base64)", buttonReadFile
      "Input/Button (Notify / SetState / AiTool — JSON payloads)", buttonJsonPayloads
      "Input/FileUpload", fileUpload
      "Input/FileUpload (Phase 1115 — drop target; acceptPaste OMITTED)", fileUploadDrop
      "Input/FileUpload (Phase 1115 — paste ingestion, image/* filtered; dropTarget OMITTED)", fileUploadPaste
      "Input/Select", select
      "Input/Select (multi-select — list value)", multiSelect
      "Input/Form (Phase 426 — handler-free write-back fields, State-bound)", formDeclarative
      "Input/Form (Phase 596 — symmetric auto-bind, omitted-value fields)", formDeclarativeMinimal
      "Input/Form (Phase 864 — declared field rules: format / pattern / length pair / cross-field compare)",
      formFieldRules
      "Layout/Stack (Phase 426 — declarative tabs + modal + disclosure + select, handlers omitted)", controlsDeclarative
      "Layout/Stack (Phase 426 — closure-authored onSelectTag / onToggle / onChangeMulti sentinels)", multiSelectClosure
      "Layout/Stack (Phase 428 — Action.Call result targets: closure / into State / into Query)", callInto
      "Layout/Box (Phase 1473 keepTogether, the only new member)", boxKeepTogether
      "Layout/Box (Phase 1473 breakBefore, the only new member)", boxBreakBefore
      "Visualisation/Grid (Phase 1473 keepRowsTogether, the only new member)", gridKeepRowsTogether
      "Visualisation/Grid (Phase 1473 repeatHeader, the only new member)", gridRepeatHeader
      "Visualisation/Grid", gridVis
      "Visualisation/Grid (Phase 282 — Binding.Transform compute source)", gridTransform
      "Visualisation/Grid (Phase 424 — parameterised Binding.Transform, filter param from a chip)", gridTransformParam
      "Layout/Box (Phase 610 — multi-select chip + DataGrid scoped by a list-valued in/param)", multiselectChipListParam
      "Display/Metric (Phase 421 — Binding.Query with a declared dependsOn filter edge)", queryDependsOn
      "Visualisation/Grid (Phase 425 — field-named columns + RowKeyField, closure-free)", gridFieldNamed
      "Visualisation/Grid (Phase 750 — TonedPill: value-conditional cell tone declared as a value→tone map)",
      gridTonedPill
      "Layout/Box (filterable-static dashboard — Filters params wired through Transform to chart + grid)",
      filterableStaticDashboard
      "Layout/Box (master-detail — grid + detail card State-bound with a pre-selected defaultValue)",
      masterDetailPreselected
      "Layout/Box (master-detail — Selection defaultValue naming a NON-FIRST row: prune-vs-seed is observable)",
      masterDetailPreselectedSecondRow
      "Layout/Box (Phase 767 — the canonical empty state with a CTA: one Card region, no nested Callout)",
      emptyStateCard
      "Layout/Box (master-detail — ONE selection feeding N slots, each projecting a DIFFERENT field)",
      masterDetailMultiField
      "Layout/Box (Phase 632 — Transform in scalar slots: selected-row Callout body + Badge count)",
      scalarTransformComposition
      "Binding/Now (Phase 765 — the host-furnished instant: a text slot + a Transform param feeding dateDiffDays)",
      nowEnvironmentBinding
      "Display/Metric (Phase 283 — Binding.Invoke capability source)", metricInvoke
      "Input/Button (Phase 283 — Action.Invoke capability effect)", buttonInvoke
      "Visualisation/Chart", chart
      "Visualisation/Grid (Phase 663/665 — editable State-sourced grid, typed rows on the wire)", gridEditableState
      "Visualisation/Chart (Phase 663/665 — chart on the editable grid's state key)", chartStateRows
      "Visualisation/Chart (Phase 876 — valueFormat: the value axis's declared number format)", chartValueFormat
      "Visualisation/Chart (Phase 878 — xTitle/yTitle/subtitle: the axis names + the muted subtitle)", chartAxisTitles
      "Visualisation/Chart (Phase 880 — legendPosition: the legend's declared edge)", chartLegendPosition
      "Visualisation/Chart (Phase 881 — dataLabels: values written onto the picture)", chartDataLabels
      "Visualisation/Chart (Phase 882 — xScale: a temporal x-axis over ISO-8601 date cells)", chartTemporalX
      "Visualisation/Grid (static-table mode — staticRows; absorbed the retired Table kind)", table
      "Visualisation/Grid (Phase 801 — static-table mode declaring sort intent: sortable + defaultSort)", tableSortable
      "Visualisation/Grid (Phase 818 — sortStateKey: the data-bound grid-sort header affordance)", gridSortStateKey
      "Visualisation/Grid (Phase 861 — bound-path sort: per-column sortable narrowing + a declared initial order)",
      gridBoundSort
      "Visualisation/Grid (Phase 863 — declared edit destination + per-column read-only narrowing)", gridDeclaredEdit
      "Visualisation/Grid (Phase 934 — declarative row reorder: omit-when-false flag; edits and reorders share one destination)",
      gridReorderable
      "Layout/Box (Phase 1123 — cross-container transfer: two two-way columns and a one-way archive on one shared key)",
      gridTransferBoard
      "Visualisation/Grid (Phase 862 — pageStateKey + pageSize: declarative pagination, renderer-owned pager)",
      gridPaged
      "Visualisation/Grid (Phase 862 — paging and sorting composed: two behaviours, two state keys, one rule)",
      gridPagedSorted
      "Display/Badge (Phase 818 — LIVE State-sourced Transform: the Tier-D count badge, preserved source + initial snapshot)",
      badgeTransformLive
      "Visualisation/Grid + Display/Badge (Phase 1075 — the seeded shared source: one declared table, two readers)",
      sharedSourceSeededPair
      "Input/Button (Phase 818 — SetState.valueFrom: a derived state write from the selected row's field)",
      buttonSetStateValueFrom
      "Input/Button (Phase 1124 — Action.Print: the payload-free action, inside a Chain)", buttonPrint
      "Visualisation/Map", mapVis
      "Custom", custom
      "Custom (bounded escape, StrictReplay hash + exposed-ids)", customBounded
      "Custom (bounded escape, AdvisoryWarning hash + no exposed-ids)", customBoundedAdvisory
      "ErrorBoundary (Markdown child + Callout fallback)", errorBoundary
      "Switch (view state → details/summary cases + info default)", switchBasic
      "Meta/Switch (Phase 1122 — an auto-advancing carousel: `autoAdvanceMs` over an index key)", switchAutoAdvance
      "Meta/Switch (Phase 768 — the selector widened: `on` takes a Selection, the branch follows the clicked row)",
      switchOnSelection
      "FragmentDecl (named template with Markdown body)", fragmentDecl
      "FragmentRef (name-only wire shape)", fragmentRef
      "FragmentDecl (parameterised — value/slot/repeat holes + effect class)", fragmentDeclParam
      "FragmentRef (parameterised — value + slot args)", fragmentRefArgs
      "Mount (§4o — out-only, default-deny, zero-input degenerate)", mountMinimal
      "Mount (§4o — capabilities + TwoWay message shape + value/slot inputs)", mountFull
      "Composite (Dashboard ⊃ Card ⊃ Metric + Stack)", composite
      "Binding.Format (number/currency/percent/date/relativeTime across locales)", formatBindings
      "Accessibility (Phase 955 — all six trait slots at once on a wrapper; Static label, named role, polite)",
      a11yWrapperAllSlots
      "Accessibility (Phase 955 — State-bound label + hidden, mixed-case custom role doc-pageFooter, off)",
      a11yWrapperStateBound
      "Accessibility (Phase 955 — the announcement pair: role alert + liveRegion assertive)", a11yAlertAssertive
      "Accessibility (Phase 955 — Link: the accessible name overriding the visible 'Read more' text)", a11yLinkLabelled
      "Accessibility (Phase 955 — Button: an explicit named role on a kind that already has a native one)",
      a11yButtonNamed
      "Accessibility (Phase 955 — Image: the decorative shape, empty alt + hidden Static true)", a11yImageDecorative
      "Tooltip (Phase 1112 — the trait on a forwarding interactive kind; the description rides the button)",
      tooltipButton
      "Tooltip (Phase 1112 — the trait on a display kind, I18n hint; description and focus stop on the wrapper)",
      tooltipMetric
      "Tooltip (Phase 1112 — the icon-only button: accessibility.label NAMES, tooltip DESCRIBES, describedBy merges)",
      tooltipIconButton ]
    @ stdlibFragments

let opReplaceRoot: TreeOp<obj> = TreeOp.ReplaceRoot composite

let allOps: (string * TreeOp<obj>) list =
    [ "EditNode", opEditNode
      "UpdateProp", opUpdateProp
      "ReplaceBinding", opReplaceBinding
      "UpdateStyle", opUpdateStyle
      "UpdateStyle (Phase 147 Role=Eyebrow + Voice=Structural)", opUpdateStyleRoleVoice
      "UpdateStyle (Phase 1472 Direction=Ltr, the only style member)", opUpdateStyleDirection
      "UpdateState", opUpdateState
      "InsertChild", opInsertChild
      "RemoveNode", opRemoveNode
      "MoveNode", opMoveNode
      "ReorderChildren", opReorderChildren
      "ReplaceRoot", opReplaceRoot
      "Batch", opBatch
      "UpdateProp-nested-column0-label", opUpdatePropNestedColumn0Label
      "UpdateProp-nested-column1-label", opUpdatePropNestedColumn1Label
      "UpdateProp-nested-yfield0", opUpdatePropNestedYField0
      "UpdateProp-nested-yfield1", opUpdatePropNestedYField1
      "UpdateProp-nested-field0-required", opUpdatePropNestedField0Required
      "UpdateProp-nested-field1-required", opUpdatePropNestedField1Required
      "UpdateProp-nested-object-value", opUpdatePropNestedObjectValue
      "UpdateProp-nested-badindex", opUpdatePropNestedBadIndex
      "UpdateProp-nested-badfield", opUpdatePropNestedBadField
      "UpdateProp-nested-malformed", opUpdatePropNestedMalformed ]

// ─── §21 shape-limit payloads (wire STRINGS, not `Node` values) ──────────────
//
// The WIRE_FORMAT §21 limit fixtures are built as text for the reason
// `LimitTests.fs` states at its head: assembling a `MaxDepth`-deep tree as an F#
// value proves nothing about the decoder, and one level PAST the bound overflows
// the stack while BUILDING the input. They also carry corpus ids that are not
// their root node's id (`limit-node-depth-at-max` vs `n0`), which `allNodes`
// cannot express — there the id is derived from the node.
//
// They live here so `Corpus.emit` OWNS them. Before this they were authored
// straight into the corpus, and since the emitter rewrites the payload
// directories wholesale, EVERY regeneration deleted the five payloads and
// dropped their manifest rows — leaving fixtures the corpus carried and no host
// could reproduce, and a `--emit-corpus` that silently shrank the shared
// conformance surface. `Fixtures.storedNodes` + the four `RejectFixtures`
// entries beside it close that by construction; the byte-parity pin in
// `RoundTripTests` holds the generators to the committed bytes, so a drift fails
// loudly rather than rewriting the corpus on the next regen.

/// A canonical-wire `Box` chain exactly `boxes` nodes deep: `boxes - 1` wrapper
/// boxes `n0`…`n{boxes-2}` around an empty `leaf` box. Byte-identical to
/// `CanonicalJson.encodeNode` on this shape (Ordinal-sorted keys,
/// omit-when-default), so a within-limit chain is a genuinely decodable tree and
/// not merely something the decoder tolerates.
let boxChain (boxes: int) : string =
    let box (id: string) (children: string) =
        "{\"id\":\""
        + id
        + "\",\"kind\":{\"$type\":\"Box\",\"children\":["
        + children
        + "],\"layout\":{\"$type\":\"Flex\",\"direction\":\"Vertical\",\"wrap\":false},\"role\":\"Group\"}}"

    let mutable acc = box "leaf" ""

    for i in boxes - 2 .. -1 .. 0 do
        acc <- box ("n" + string i) acc

    acc

/// Phase 1120 — `rows` levels of nested `TreeItem`, inside ONE `Tree` node.
///
/// The point of the shape: it consumes exactly ONE level of node depth however
/// deep it goes, so the node bound cannot see it, and at 25 rows it is roughly
/// 50 levels of JSON — nowhere near the 256 syntactic bound either. It is
/// `TreeOp.Batch`'s situation exactly (§21.5's implementers' note), which is why
/// the item axis is counted on its own.
let treeItemChain (rows: int) : string =
    let mutable acc = """{"id":"leaf","label":"Leaf"}"""

    for i in rows - 2 .. -1 .. 0 do
        acc <- "{\"children\":[" + acc + "],\"id\":\"r" + string i + "\",\"label\":\"Row\"}"

    "{\"id\":\"t\",\"kind\":{\"$type\":\"Tree\",\"items\":[" + acc + "]}}"

/// `batches` nested `TreeOp.Batch` ops around a single innermost `RemoveNode`,
/// so the document is `batches + 1` op levels deep and carries no nodes at all —
/// the op nesting axis on its own (§21.5).
let batchChain (batches: int) : string =
    let mutable acc = """{"$type":"RemoveNode","target":"x"}"""

    for _ in 1..batches do
        acc <- "{\"$type\":\"Batch\",\"ops\":[" + acc + "]}"

    acc

/// Node fixtures whose payload is a stored wire string rather than an encoded
/// `Node` value, keyed by an explicit corpus id. `(id, description, payload)` —
/// the description is the manifest row verbatim.
let storedNodes: (string * string * string) list =
    [ "limit-node-depth-at-max",
      "§21 max node depth — a tree at EXACTLY the limit (24 levels). Rule 1: every conformant host MUST decode this; refusing it is non-conformance, not conservatism",
      boxChain Fuaran.UI.WireLimits.MaxDepth
      "limit-tree-item-depth-at-max",
      "§21.5 the tree-item axis — a tree nesting rows at EXACTLY the limit (24 levels) inside ONE node. Rule 1: every conformant host MUST decode this. Its past-the-bound twin is `reject-limit-tree-item-depth`",
      treeItemChain Fuaran.UI.WireLimits.MaxDepth ]
