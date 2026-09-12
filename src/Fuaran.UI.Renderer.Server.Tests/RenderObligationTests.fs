module Fuaran.UI.Renderer.Server.Tests.RenderObligationTests

// ============================================================================
//  Executable render-obligation conformance (Phase 1105) — the reference host's
//  adoption.
//
//  Codec conformance is byte-parity and strong. Render obligations were prose:
//  §3.6.5 and §3.6.6 state, in sentences, that an accessible name is always
//  emitted, that `autoplay` never appears without `muted`, that an audio
//  transport has no autoplay pathway at all, that a refused source emits no
//  affordance. A host can pass every fixture in the corpus and silently fail
//  every one of those — the one media defect the Rust compiler could not catch
//  was exactly this shape, a boolean site rather than a missing match arm.
//
//  So the manifest carries them now, and this suite asserts FROM the manifest
//  rather than from a hand list beside it. The consequences, which are the whole
//  point:
//
//    * The ENUMERATION is the corpus artefact's. A new obligation declared on a
//      kind this host renders arrives here as a claim with no checker, and the
//      gate goes RED until someone asserts it — not as a paragraph a future
//      reader may or may not re-read.
//
//    * NOT CHECKED IS NOT PASSED. Every claim this host does not assert is
//      printed by name, with the section that states it, and fails the gate
//      unless it carries a declared exemption. Silence is never available as an
//      answer.
//
//    * The go-red property is PROVEN, not asserted. `statusOf` is exercised
//      against a claim no checker covers, and must report it as unchecked — the
//      shape a newly-declared obligation takes on the day it lands.
//
//  Every checker asserts in EMITTED HTML through `Render.render`, the ordinary
//  entry point. A checker that inspected the typed tree would be re-stating the
//  type system; the obligations are claims about output.
// ============================================================================

open System
open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Expecto

open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Feliz.ViewEngine
open Fuaran.UI.Renderer.Server
open Fuaran.UI.RenderFidelity

let private contains (needle: string) (haystack: string) =
    haystack.Contains(needle, StringComparison.Ordinal)

let private render (node: Node<obj>) =
    Render.render BindingResolver.empty node

/// A destination that is safe by the scheme floor and entirely undeclared, so
/// the ambient egress policy (Phase 1026) refuses it. This is the input the two
/// "refused" obligations are about.
let private refusedUrl = "https://collector.example/asset.jpg"

// ─── The manifest is the enumeration ─────────────────────────────────────────

/// Walk up to the workspace corpus — the same degrade-to-skip posture
/// `A11yCorpusParityTests` records, and for the same reason: a missing input is
/// a statement about the checkout, not about the code.
/// Phase 1647 — the root comes from the ONE resolver; the artefact's own
/// existence stays a separate statement (a corpus that carries no
/// `render-fidelity.json` is a corpus defect, not an absent checkout).
let private tryCorpusArtifact () : string option =
    Fuaran.Tests.CorpusRoot.tryFind ()
    |> Option.map (fun root -> Path.Combine(root, "render-fidelity.json"))
    |> Option.filter File.Exists

/// One obligation as the manifest declares it, before this host has resolved the
/// claim id against its own vocabulary.
type private DeclaredObligation =
    { Kind: string
      ClaimId: string
      Section: string }

/// The obligations this host must answer for, read from the GENERATED artefact
/// where the corpus is present.
///
/// The fallback to the shipped declaration is not a shortcut: `Fuaran.UI` is
/// where the artefact is generated FROM, and the stale-artefact guard in
/// `Fuaran.UI.JsonDecode.Tests` pins the two byte-for-byte, so in a bare clone
/// the declaration is the same set by construction. Reading the artefact where
/// it exists is what makes this host answer the same question a non-F# host
/// answers, from the same bytes.
let private declaredObligations: DeclaredObligation list =
    match tryCorpusArtifact () with
    | Some path ->
        use doc = JsonDocument.Parse(File.ReadAllText path)

        [ for kind in doc.RootElement.GetProperty("kinds").EnumerateArray() do
              let kindName = kind.GetProperty("kind").GetString()

              match kind.TryGetProperty "obligations" with
              | true, arr when arr.ValueKind = JsonValueKind.Array ->
                  for o in arr.EnumerateArray() ->
                      { Kind = kindName
                        ClaimId = o.GetProperty("id").GetString()
                        Section = o.GetProperty("section").GetString() }
              | _ -> ()

          // Phase 1696 - the TRAIT obligations, the other SUBJECT population. A
          // trait rides the node envelope, so its claims are owed by every kind
          // this host renders and belong to none of them; the subject is the
          // trait id, which carries a dot no kind name can.
          match doc.RootElement.TryGetProperty "traits" with
          | true, traits when traits.ValueKind = JsonValueKind.Array ->
              for t in traits.EnumerateArray() do
                  let traitId = t.GetProperty("trait").GetString()

                  for o in t.GetProperty("obligations").EnumerateArray() ->
                      { Kind = traitId
                        ClaimId = o.GetProperty("id").GetString()
                        Section = o.GetProperty("section").GetString() }
          | _ -> () ]
    | None ->
        [ for (kind, o) in allObligations ->
              { Kind = kind
                ClaimId = claimId o.Claim
                Section = o.Section }
          for (traitId, o) in allTraitObligations ->
              { Kind = traitId
                ClaimId = claimId o.Claim
                Section = o.Section } ]

// ─── The checkers ────────────────────────────────────────────────────────────
//
// One per (kind, claim). Each is an assertion over emitted HTML, and each pins
// BOTH directions where the obligation has two — an emission test alone cannot
// tell a renderer that honours a conditional from one that emits unconditionally.

let private mediaVideo id src label =
    Fuaran.mediaSpec
        id
        { Defaults.media with
            Src = Binding.Static(Some src)
            Label = TextSource.Literal label }

let private image id (mutate: ImageSpec -> ImageSpec) =
    Fuaran.imageSpec id (mutate Defaults.image)

let private checkAccessibleNameAlways () =
    // Both variants, because the label is mandatory on the wire for the KIND and
    // not for one arm of it. A renderer that emitted the label only on `<video>`
    // would pass a video-only assertion.
    let video = render (mediaVideo "mv" "/walkthrough.mp4" "Studio walkthrough")

    let audio =
        render (
            Fuaran.mediaSpec
                "ma"
                { Defaults.media with
                    Src = Binding.Static(Some "/commentary.mp3")
                    Label = TextSource.Literal "Curator commentary"
                    Kind = MediaKind.Audio }
        )

    Expect.isTrue (contains "aria-label=\"Studio walkthrough\"" video) "a video emits the resolved label as aria-label"

    Expect.isTrue (contains "aria-label=\"Curator commentary\"" audio) "an audio emits the resolved label as aria-label"

let private checkAutoplayMutedPairing () =
    let autoplaying =
        render (
            Fuaran.mediaSpec
                "mva"
                { Defaults.media with
                    Src = Binding.Static(Some "/ambient.mp4")
                    Label = TextSource.Literal "Ambient loop"
                    Kind = MediaKind.Video(true, None) }
        )

    Expect.isTrue (contains "autoplay=" autoplaying) "a declared autoplay is emitted"

    Expect.isTrue
        (contains "muted=" autoplaying)
        "…and never without muted — an unmuted autoplay is blocked and means nothing"

    // The pairing runs one way, and this is the half a one-sided assertion
    // misses: `muted` unasked silences a video the reader started themselves.
    let plain = render (mediaVideo "mv" "/walkthrough.mp4" "Studio walkthrough")

    Expect.isFalse (contains "autoplay" plain) "autoplay is not declared, so it must not be emitted"
    Expect.isFalse (contains "muted" plain) "muted rides autoplay; unasked it is a behaviour change, not a default"

let private checkNoAutoplayPathway () =
    let audio =
        render (
            Fuaran.mediaSpec
                "ma"
                { Defaults.media with
                    Src = Binding.Static(Some "/commentary.mp3")
                    Label = TextSource.Literal "Curator commentary"
                    Kind = MediaKind.Audio }
        )

    Expect.isFalse (contains "autoplay" audio) "an <audio> must never carry an autoplay attribute"
    Expect.isFalse (contains "muted" audio) "an <audio> has no autoplay, so it has nothing to mute"

let private checkRefusedSourceDropped () =
    let refused =
        render (
            Fuaran.mediaSpec
                "mvp"
                { Defaults.media with
                    Src = Binding.Static(Some "/walkthrough.mp4")
                    Label = TextSource.Literal "Studio walkthrough"
                    Kind = MediaKind.Video(false, Some(Binding.Static(Some refusedUrl))) }
        )

    Expect.isFalse (contains "collector.example" refused) "a refused poster's destination is never emitted"

    Expect.isFalse
        (contains "poster=" refused)
        "a refused poster is DROPPED, not emitted at the refusal URL — a poster at the refusal URL is a broken image over the player, where no poster shows the first frame"

    // The allow twin. Without it, a renderer that dropped EVERY poster would
    // pass the refusal assertion above and this obligation would guard nothing.
    let allowed =
        render (
            Fuaran.mediaSpec
                "mvp2"
                { Defaults.media with
                    Src = Binding.Static(Some "/walkthrough.mp4")
                    Label = TextSource.Literal "Studio walkthrough"
                    Kind = MediaKind.Video(false, Some(Binding.Static(Some "/walkthrough-poster.jpg"))) }
        )

    Expect.isTrue (contains "poster=\"/walkthrough-poster.jpg\"" allowed) "a local poster still renders"

// Phase 1110 — the three track/transcript checkers. Each pins BOTH directions,
// on the same reasoning the four above do: an emission assertion alone cannot
// tell a renderer that honours a rule from one that emits unconditionally.

let private track kind src srcLang label isDefault : TrackEntry =
    { Default = isDefault
      Kind = kind
      Label = TextSource.Literal label
      Src = Binding.Static(Some src)
      SrcLang = srcLang }

let private mediaTracks id (tracks: TrackEntry list) =
    Fuaran.mediaSpec
        id
        { Defaults.media with
            Src = Binding.Static(Some "/walkthrough.mp4")
            Label = TextSource.Literal "Studio walkthrough"
            Tracks = tracks }

let private checkAuthoredChildOrder () =
    // Authored in an order no sort produces: a `gd` subtitles track before two
    // `en` captions ones. Alphabetical by srclang, by label, or by kind would
    // all move the first entry, so any re-sort shows up here.
    let html =
        render (
            mediaTracks
                "mvt"
                [ track TrackKind.Subtitles "/restoration-2.gd.vtt" "gd" "Gaidhlig" false
                  track TrackKind.Captions "/restoration-2.en.vtt" "en" "English captions" true
                  track TrackKind.Descriptions "/restoration-2.ad.vtt" "en" "Audio description" false ]
        )

    let posOf (needle: string) =
        html.IndexOf(needle, System.StringComparison.Ordinal)

    let gd = posOf "/restoration-2.gd.vtt"
    let en = posOf "/restoration-2.en.vtt"
    let ad = posOf "/restoration-2.ad.vtt"

    Expect.isGreaterThan gd -1 "the first authored track is emitted"
    Expect.isLessThan gd en "the authored order is preserved — the gd track precedes the en one"
    Expect.isLessThan en ad "…and the whole list, not merely its head"

let private checkSingleDefaultPerKind () =
    // Two captions tracks both electing themselves default, plus a subtitles one
    // that also does. First-wins is PER KIND, so the subtitles election survives
    // beside the captions one and only the SECOND captions election is dropped.
    let html =
        render (
            mediaTracks
                "mvd"
                [ track TrackKind.Captions "/a.en.vtt" "en" "English captions" true
                  track TrackKind.Captions "/b.en.vtt" "en" "English captions (verbose)" true
                  track TrackKind.Subtitles "/c.gd.vtt" "gd" "Gaidhlig" true ]
        )

    let defaults =
        html.Split([| "default=" |], System.StringSplitOptions.None).Length - 1

    Expect.equal
        defaults
        2
        "one default survives per KIND — the captions duplicate loses, the subtitles election does not"

    Expect.isTrue (contains "/b.en.vtt" html) "the losing track is still EMITTED; only its claim on the menu is dropped"

    // The twin without which a renderer that dropped every `default` would pass.
    let single =
        render (mediaTracks "mvd1" [ track TrackKind.Captions "/a.en.vtt" "en" "English captions" true ])

    Expect.isTrue (contains "default=" single) "a lone election is honoured"

let private checkTranscriptDisclosureNamed () =
    let withTranscript =
        render (
            Fuaran.mediaSpec
                "mat"
                { Defaults.media with
                    Src = Binding.Static(Some "/commentary.mp3")
                    Label = TextSource.Literal "Curator commentary"
                    Kind = MediaKind.Audio
                    Transcript = Some(TextSource.Literal "The harbour was rebuilt twice.") }
        )

    Expect.isTrue (contains "<details" withTranscript) "a declared transcript renders as a disclosure"

    Expect.isTrue
        (contains "fuaran-media-transcript" withTranscript)
        "…carrying the media-scoped class, not the Disclosure kind's"

    Expect.isTrue
        (contains "The harbour was rebuilt twice." withTranscript)
        "…and the transcript text itself, which is the document's content"

    Expect.isTrue
        (contains "aria-label=\"Curator commentary\"" withTranscript)
        "the disclosure carries the media's resolved label as its accessible name"

    let transcriptIndex =
        withTranscript.IndexOf("<details", System.StringComparison.Ordinal)

    let audioIndex = withTranscript.IndexOf("<audio", System.StringComparison.Ordinal)

    Expect.isLessThan
        audioIndex
        transcriptIndex
        "the disclosure sits BESIDE the transport, after it — inside a media element a browser treats it as fallback content and never shows it"

    // The absent twin: no transcript, no disclosure and no wrapper. Without it a
    // renderer that always emitted the group would pass everything above.
    let without =
        render (
            Fuaran.mediaSpec
                "mat0"
                { Defaults.media with
                    Src = Binding.Static(Some "/commentary.mp3")
                    Label = TextSource.Literal "Curator commentary"
                    Kind = MediaKind.Audio }
        )

    Expect.isFalse (contains "<details" without) "no transcript, no disclosure"
    Expect.isFalse (contains "fuaran-media-group" without) "…and no group wrapper either"

let private checkAltAlwaysEmitted () =
    let named =
        render (
            image "img" (fun s ->
                { s with
                    Src = Binding.Static(Some "/harbour.jpg")
                    Alt = TextSource.Literal "Fishing boats moored at first light" })
        )

    Expect.isTrue (contains "alt=\"Fishing boats moored at first light\"" named) "the alt text is emitted"

    // The decorative case is the one that matters. An omitted `alt` and an empty
    // one are different claims to assistive technology: omitted means "nobody
    // said", empty means "this is decorative, skip it".
    let decorative =
        render (
            image "imgd" (fun s ->
                { s with
                    Src = Binding.Static(Some "/rule.png")
                    Alt = TextSource.Literal "" })
        )

    Expect.isTrue (contains "alt=\"\"" decorative) "a decorative image emits an EMPTY alt, never no alt at all"

let private checkAnchorAffordanceOnExpandable () =
    let html =
        render (
            image "imge" (fun s ->
                { s with
                    Src = Binding.Static(Some "/harbour.jpg")
                    Alt = TextSource.Literal "Harbour"
                    Expandable = true })
        )

    // The ELEMENT is pinned, not only the class: the whole no-JS claim is that
    // this is an `<a href>`, and a `<span class="fuaran-image-expand">` carrying
    // the data attribute would pass a class-only assertion while giving a
    // scriptless reader nothing.
    Expect.isTrue
        (contains "<a class=\"fuaran-image-expand\" href=\"/harbour.jpg\" data-fuaran-expandable=\"\">" html)
        "expandable emits a real anchor to the asset the image already names"

    let notExpandable =
        render (
            image "imgp" (fun s ->
                { s with
                    Src = Binding.Static(Some "/harbour.jpg")
                    Alt = TextSource.Literal "Harbour" })
        )

    Expect.isFalse (contains "fuaran-image-expand" notExpandable) "an undeclared expansion emits no anchor"

let private checkRefusedSrcNoAffordance () =
    let html =
        render (
            image "imgr" (fun s ->
                { s with
                    Src = Binding.Static(Some refusedUrl)
                    Alt = TextSource.Literal "Harbour"
                    Expandable = true })
        )

    Expect.isFalse
        (contains "fuaran-image-expand" html)
        "a src the egress floor refused emits NO expand anchor — an affordance that cannot be honoured is worse than none"

    // The image itself still renders, at the refusal URL. Without this leg a
    // renderer that dropped the whole node would pass the assertion above, and
    // this obligation would be satisfied by a worse bug than the one it guards.
    Expect.isTrue
        (contains Sanitize.egressRefusalUrl html)
        "the img is still emitted, with the marked refusal URL as its src"

    Expect.isFalse
        (contains "href=\"https://collector.example" html)
        "and the refused destination never becomes a navigable href"

let private checkFigureCaptionOutsideLink () =
    let html =
        render (
            image "imgef" (fun s ->
                { s with
                    Src = Binding.Static(Some "/harbour.jpg")
                    Alt = TextSource.Literal "Harbour"
                    Expandable = true
                    Caption = Some(TextSource.Literal "The harbour at dawn") })
        )

    // Asserting the two opening tags IN ORDER is what catches the inversion
    // (anchor outside figure), which would carry every one of the same classes.
    Expect.isTrue
        (contains
            "<figure class=\"fuaran-image-figure\"><a class=\"fuaran-image-expand\" href=\"/harbour.jpg\" data-fuaran-expandable=\"\">"
            html)
        "the figure wraps the anchor, not the other way round"

    Expect.isTrue
        (contains "</a><figcaption class=\"fuaran-image-figure-caption\">The harbour at dawn</figcaption></figure>" html)
        "the figcaption is the anchor's SIBLING — the caption is prose a reader quotes, not a second click surface"

let private checkSrcSetAscendingByWidth () =
    // Authored DESCENDING, so the assertion pins the renderer's SORT and not
    // merely its spelling: a renderer emitting authored order would produce a
    // srcset containing all the same URLs and fail here.
    let html =
        render (
            image "imgs" (fun s ->
                { s with
                    Src = Binding.Static(Some "/harbour.jpg")
                    Alt = TextSource.Literal "Harbour"
                    SrcSet =
                        [ { Src = Binding.Static(Some "/harbour-1600.jpg")
                            Width = 1600 }
                          { Src = Binding.Static(Some "/harbour-800.jpg")
                            Width = 800 }
                          { Src = Binding.Static(Some "/harbour-400.jpg")
                            Width = 400 } ] })
        )

    Expect.isTrue
        (contains "srcset=\"/harbour-400.jpg 400w, /harbour-800.jpg 800w, /harbour-1600.jpg 1600w\"" html)
        "candidates are emitted ascending by width"

    // The second half of the same obligation: a refused candidate is DROPPED, so
    // the primary src remains the fallback rather than the list carrying a
    // destination the floor refused.
    let withRefused =
        render (
            image "imgs2" (fun s ->
                { s with
                    Src = Binding.Static(Some "/harbour.jpg")
                    Alt = TextSource.Literal "Harbour"
                    SrcSet =
                        [ { Src = Binding.Static(Some "/harbour-400.jpg")
                            Width = 400 }
                          { Src = Binding.Static(Some refusedUrl)
                            Width = 1600 } ] })
        )

    Expect.isFalse (contains "collector.example" withRefused) "a refused candidate's destination is never emitted"
    Expect.isTrue (contains "/harbour-400.jpg 400w" withRefused) "…while the candidates that pass the floor still are"

// ─── Phase 1108 — the unregistered-degradation obligation ────────────────────

/// A contract card for a component this host has NO renderer for. That is the
/// whole premise: the store and the renderer registry are independent, and the
/// case the obligation is about is the one where only the first is populated.
let private sparklineCard: CustomKindCard =
    { ModuleId = "analytics"
      ComponentId = "sparkline"
      Props =
        [ { Name = "series"
            Type = "string"
            Required = true
            PayloadLanguage = Some "chartspec"
            PayloadGate = Some "chartspec-gate:1.2" }
          { Name = "title"
            Type = "string"
            Required = false
            PayloadLanguage = None
            PayloadGate = None } ]
      Hash =
        { Algorithm = "SHA256"
          Hash = "a94a8fe5ccb19ba61c4c0873d391e987982fbbd3" }
      Summary = Some "A compact trend line with a period-over-period delta." }

let private customNode (contentHash: ContentHash option) : Node<obj> =
    Fuaran.custom
        "cust"
        "analytics"
        "sparkline"
        (Map.ofList [ "series", Fuaran.Core.JStr "{\"points\":[1,2,3]}" ])
        contentHash
        []

let private checkUnregisteredCustomLabelled () =
    let cards = CustomCardStore.ofCards [ sparklineCard ]

    // (1) NO CARD, NO RENDERER — the pre-1108 path, byte-for-byte. This leg is
    // first because it is the one the obligation must NOT have changed: an
    // obligation that quietly rewrote every existing host's output would be a
    // breaking change wearing a conformance claim.
    let bare = Render.render BindingResolver.empty (customNode None)

    Expect.isTrue
        (contains "[fuaran:custom analytics.sparkline]" bare)
        "the identity-only placeholder still names the component"

    Expect.isFalse
        (contains "data-fuaran-custom-card" bare)
        "a host with no card claims nothing about a card — the marker is absent, not empty"

    Expect.isFalse (contains "trend line" bare) "and it invents no description it does not have"

    // (2) CARD, NO RENDERER, NO DECLARED HASH — the common case. The description
    // is shown, and the claim is marked unverified rather than asserted.
    let unverified =
        Render.renderWithCards cards Registry.empty BindingResolver.empty (customNode None)

    Expect.isTrue
        (contains "data-fuaran-custom-card=\"unverified\"" unverified)
        "the verdict marker is machine-readable"

    Expect.isTrue (contains "[fuaran:custom analytics.sparkline]" unverified) "the identity is still emitted"

    Expect.isTrue
        (contains "A compact trend line with a period-over-period delta." unverified)
        "the card's summary is emitted — this is the whole legibility gain"

    Expect.isTrue
        (contains "series: string (required) [chartspec (gate chartspec-gate:1.2)]" unverified)
        "the declared prop rows are emitted, payload language included"

    // Never a prop VALUE. The host was not asked to interpret the node's props,
    // and spilling them into a placeholder is an information leak that buys no
    // legibility at all.
    Expect.isFalse (contains "points" unverified) "no prop value reaches the placeholder"

    // (3) CARD, MATCHING HASH — the strongest claim available.
    let matching =
        Render.renderWithCards
            cards
            Registry.empty
            BindingResolver.empty
            (customNode (
                Some
                    { Algorithm = "SHA256"
                      Hash = "a94a8fe5ccb19ba61c4c0873d391e987982fbbd3"
                      Strictness = HashStrictness.AdvisoryWarning }
            ))

    Expect.isTrue (contains "data-fuaran-custom-card=\"described\"" matching) "a verified card says so"

    Expect.isTrue (contains "A compact trend line" matching) "and shows the description"

    // (4) CARD, CONTRADICTED HASH — the card describes a different shape at the
    // same address, so its description is WITHHELD. Without this leg the
    // obligation would be satisfied by a renderer that showed any card matching
    // by name, which is the guess it exists to forbid.
    let mismatched =
        Render.renderWithCards
            cards
            Registry.empty
            BindingResolver.empty
            (customNode (
                Some
                    { Algorithm = "SHA256"
                      Hash = "0000000000000000000000000000000000000000"
                      Strictness = HashStrictness.AdvisoryWarning }
            ))

    Expect.isTrue
        (contains "data-fuaran-custom-card=\"hash-mismatch\"" mismatched)
        "the contradiction is stated, not hidden"

    Expect.isFalse
        (contains "trend line" mismatched)
        "a description of a different shape is withheld — a confident wrong description is worse than none"

    Expect.isTrue
        (contains "[fuaran:custom analytics.sparkline]" mismatched)
        "the identity survives; only the description is withheld"

    // (5) A RENDERER WINS. A registered renderer renders; the card is never
    // consulted. Without this leg a renderer that ignored its own registry in
    // favour of a card would pass every assertion above.
    let rendered =
        Render.renderWithCards
            cards
            (Registry.empty
             |> Registry.register "analytics" "sparkline" (fun _ ->
                 Html.div [ prop.className "host-sparkline"; prop.text "rendered" ]))
            BindingResolver.empty
            (customNode None)

    Expect.isTrue (contains "host-sparkline" rendered) "the registered renderer runs"

    Expect.isFalse
        (contains "data-fuaran-custom-card" rendered)
        "and no card marker is emitted for a node that rendered"

// Phase 1111 - the three embed checkers.
//
// These are the only checkers in this file that cannot use the bare `render`
// entry point for their POSITIVE direction, and the reason is the obligation
// itself rather than a convenience. The `embed` egress class admits `https` and
// nothing else, so the local-path trick every other allow-twin here uses
// (`/walkthrough-poster.jpg`) is refused by the scheme floor before policy is
// consulted - and the ambient default-deny then refuses every https origin,
// because none is declared. So a positive assertion needs BOTH: an https source
// and a policy that declares its origin. `renderEmbedAllowing` is that, named so
// the opt-out is greppable, and the REFUSAL directions still go through the
// ordinary `render` so the default is what is being measured.

let private embedOrigin =
    Sanitize.allowOrigin
        (Sanitize.ExactHost "player.example")
        [ Sanitize.EgressClass.Embed ]
        Sanitize.denyNonLocalEgress

let private renderEmbedAllowing (node: Node<obj>) =
    Render.renderWithEgress embedOrigin Registry.empty BindingResolver.empty node

let private embed id src title permissions =
    Fuaran.embedSpec
        id
        { Defaults.embed with
            Src = Binding.Static(Some src)
            Title = TextSource.Literal title
            Permissions = permissions }

let private checkEmbedAccessibleNameAlways () =
    let named =
        renderEmbedAllowing (embed "e1" "https://player.example/embed/harbour" "Harbour restoration" [])

    Expect.isTrue
        (contains "title=\"Harbour restoration\"" named)
        "an embed emits the resolved title as the frame's title"

    // The twin without which a renderer emitting `title` only when the frame
    // ALSO got a source would pass: the title is a property of the node, not of
    // whether its destination survived the floor.
    let refusedSource =
        render (embed "e2" "http://player.example/embed/harbour" "Harbour restoration" [])

    Expect.isTrue
        (contains "title=\"Harbour restoration\"" refusedSource)
        "...and still emits it when the source was refused - the name is the node's, not the destination's"

let private checkEmbedSandboxAlwaysExactlyDeclared () =
    let bare =
        renderEmbedAllowing (embed "e3" "https://player.example/embed/harbour" "Harbour restoration" [])

    Expect.isTrue
        (contains "sandbox=\"\"" bare)
        "an embed granting nothing still emits sandbox, EMPTY - omitting it is the same markup as no sandbox at all"

    Expect.isFalse
        (contains "allow=" bare)
        "...and emits no `allow` attribute, because an empty allow is not the same statement as an absent one"

    // Declared out of the enum's order, and with a duplicate, so a renderer that
    // echoed the authored list would emit `allow-same-origin allow-scripts
    // allow-scripts` and fail here.
    let granted =
        renderEmbedAllowing (
            embed
                "e4"
                "https://player.example/embed/harbour"
                "Harbour restoration"
                [ EmbedPermission.AllowSameOrigin
                  EmbedPermission.AllowScripts
                  EmbedPermission.AllowScripts
                  EmbedPermission.AllowFullscreen ]
        )

    Expect.isTrue
        (contains "sandbox=\"allow-scripts allow-same-origin\"" granted)
        "the declared tokens are emitted in the vocabulary's declaration order, de-duplicated"

    Expect.isFalse (contains "allow-forms" granted) "a permission the document did not declare is never emitted"

    Expect.isTrue
        (contains "allow=\"fullscreen\"" granted)
        "fullscreen rides the permissions-policy attribute, NOT the sandbox token list"

let private checkRefusedEmbedSourceOmitted () =
    // `http` is refused by the embed scheme floor even though the ORIGIN is
    // declared, which is what tells this class apart from the ordinary URL floor.
    let refused =
        renderEmbedAllowing (embed "e5" "http://player.example/embed/harbour" "Harbour restoration" [])

    Expect.isFalse (contains "player.example" refused) "a refused destination is never emitted"
    Expect.isFalse (contains "src=" refused) "...and the src attribute is DROPPED, not pointed at the refusal URL"

    Expect.isTrue
        (contains "data-fuaran-egress-refused" refused)
        "...while the refusal is still recorded in the document"

    // A relative reference is refused too, and by the SCHEME floor rather than
    // the policy - the one place this class is stricter than every other.
    let relative =
        renderEmbedAllowing (embed "e6" "/local/player.html" "Local player" [])

    Expect.isFalse (contains "src=" relative) "a same-origin relative reference is refused by the embed class"

    // The allow twin. Without it a renderer that dropped EVERY src would pass
    // both refusals and this obligation would guard nothing.
    let allowed =
        renderEmbedAllowing (embed "e7" "https://player.example/embed/harbour" "Harbour restoration" [])

    Expect.isTrue
        (contains "src=\"https://player.example/embed/harbour\"" allowed)
        "a declared https origin still renders"

// Phase 1115 — the picker-always-present checker. The obligation is that a
// declared ingress gesture is ADDITIONAL, so both directions have to be pinned
// and the interesting one is the DECLARED case: a host that replaced the picker
// with a drop zone would pass an emission test on the plain upload and ship a
// pointer-only control on the declared one.
let private uploadWith dropTarget acceptPaste : Node<obj> =
    Fuaran.fileUpload
        "up"
        { Defaults.fileUpload with
            Label = TextSource.Literal "Attach a file"
            Accept = [ ".csv" ]
            DropTarget = dropTarget
            AcceptPaste = acceptPaste }

let private checkPickerAlwaysPresent () =
    let plain = render (uploadWith false false)

    Expect.isTrue (contains "type=\"file\"" plain) "the plain upload emits the picker"

    Expect.isTrue (contains "Attach a file" plain) "…and its label"

    for label, declared in
        [ "a drop target", uploadWith true false
          "a paste target", uploadWith false true
          "both routes", uploadWith true true ] do
        let html = render declared

        Expect.isTrue
            (contains "type=\"file\"" html)
            (label
             + " still emits the picker — the gesture is an additional route, never a replacement")

        Expect.isTrue (contains "Attach a file" html) (label + " still emits the label that names it")

    // The static floor is the plain picker, so the declared and undeclared
    // markup differ ONLY by the recording marker. Without this the obligation
    // above would also pass on a host that emitted an inert drop zone here.
    let declaredHtml = render (uploadWith true true)

    Expect.isTrue
        (contains "data-fuaran-upload-drop=\"declared\"" declaredHtml
         && contains "data-fuaran-upload-paste=\"declared\"" declaredHtml)
        "each declared route is recorded, so the declaration is visibly read rather than dropped"

    Expect.isFalse
        (contains "data-fuaran-upload-drop" plain)
        "…and an upload that declares neither carries neither marker"

// Phase 1548 — the ceiling-recorded-never-enforced checker. Two claims in one,
// and the second is the one a marker-emission test alone would miss: the marker
// records only THAT a ceiling was declared, so a host that put the NUMBER in the
// markup would pass an emission assertion while telling a reader this tier
// enforces a bound it cannot enforce at all.
let private uploadWithCeilings maxBytes maxFiles : Node<obj> =
    Fuaran.fileUpload
        "up-ceiling"
        { Defaults.fileUpload with
            Label = TextSource.Literal "Attach a scan"
            Accept = [ "application/pdf" ]
            MaxBytes = maxBytes
            MaxFiles = maxFiles }

let private checkCeilingRecordedNeverEnforced () =
    let bytes = render (uploadWithCeilings (Some 5242880) None)

    Expect.isTrue
        (contains "data-fuaran-upload-max-bytes=\"declared\"" bytes)
        "a declared byte ceiling is recorded, so the declaration is visibly read rather than dropped"

    Expect.isFalse
        (contains "5242880" bytes)
        "…and its VALUE is nowhere in the markup — nothing in this tier can act on the number, so carrying it would claim an enforcement that is not there"

    Expect.isFalse
        (contains "data-fuaran-upload-max-files" bytes)
        "…and the count marker is absent when the count member is, so the two are recorded independently"

    let files = render (uploadWithCeilings None (Some 3))

    Expect.isTrue
        (contains "data-fuaran-upload-max-files=\"declared\"" files)
        "a declared count ceiling is recorded on the same terms"

    Expect.isFalse
        (contains "data-fuaran-upload-max-bytes" files)
        "…and the byte marker is absent when the byte member is"

    // The polarity, which is what makes the members additive: an upload
    // declaring neither is byte-identical in render to what it always was.
    let plain = render (uploadWithCeilings None None)

    Expect.isFalse
        (contains "data-fuaran-upload-max-" plain)
        "an upload declaring no ceiling carries no ceiling marker at all"

    Expect.isTrue
        (contains "type=\"file\"" plain && contains "type=\"file\"" bytes)
        "and a declared ceiling changes nothing about the control itself — the picker is emitted either way"

// Phase 1119 — the aria-modal-only-when-blocking checker. Both directions are
// pinned, and the second is the one that matters: a host that implemented the
// popover by re-styling the modal would emit `aria-modal="true"` on a surface
// that blocks nothing, telling a screen-reader user the page behind is inert
// when it is fully interactive. Nothing else in the suite would report that —
// the classes would be right and the bytes would round-trip.
let private overlayWith modality anchor : Node<obj> =
    Fuaran.modal
        "surface"
        { Defaults.modal with
            Heading = Some(TextSource.Literal "Choose a colour")
            Open = Binding.Static(Some true)
            Modality = modality
            Anchor = anchor }

let private checkAriaModalOnlyWhenBlocking () =
    let blocking = render (overlayWith ModalityKind.Modal None)

    Expect.isTrue (contains "aria-modal=\"true\"" blocking) "a blocking modal claims inertness"

    Expect.isTrue (contains "role=\"dialog\"" blocking) "…and carries the dialog role"

    for label, anchor in [ "an anchored popover", Some "swatch"; "an unanchored popover", None ] do
        let html = render (overlayWith ModalityKind.Popover anchor)

        Expect.isFalse
            (contains "aria-modal" html)
            (label
             + " makes no inertness claim — the attribute is absent entirely, not emitted as \"false\"")

        Expect.isTrue
            (contains "role=\"dialog\"" html)
            (label
             + " keeps the dialog role — what changes is the claim, not the kind of thing it is")

        Expect.isFalse
            (contains "fuaran-modal-overlay" html)
            (label + " emits no scrim element — the page behind it is genuinely still there")

    // The anchor declaration is READ rather than dropped, and its absence is
    // visible too. Without this pair the obligation above would pass on a host
    // that ignored `anchor` altogether.
    Expect.isTrue
        (contains "data-fuaran-popover-anchor=\"swatch\"" (render (overlayWith ModalityKind.Popover (Some "swatch"))))
        "a declared anchor rides the static render, recording that the id was read"

    Expect.isFalse
        (contains "data-fuaran-popover-anchor" (render (overlayWith ModalityKind.Popover None)))
        "…and a popover that declares none carries no marker"

/// Phase 1120 — every `Tree` row carries a STATED accessible name.
///
/// The obligation is `accessible-name-always` at a third slot, and it means
/// something slightly different here than it does on `Media` or `Embed`: those
/// name an element that has no text of its own, where a tree row DOES have text
/// and the name is stated anyway. That is the point of the claim on this kind —
/// a `treeitem` OWNS its child group, so a name computed from contents would
/// read the whole branch out as the row's own name, and a reader arrowing onto
/// "Goods" would hear "Goods Cocoa Yarn".
let private checkTreeAccessibleNameAlways () =
    let item id label children : TreeItem =
        { Defaults.treeItem with
            Children = children
            Id = id
            Label = TextSource.Literal label }

    let tree: Node<obj> =
        Fuaran.tree
            "t1"
            [ item "goods" "Goods" [ item "cocoa" "Cocoa" []; item "yarn" "Yarn" [] ]
              item "ledger" "Ledger" [] ]

    let html = render tree

    Expect.isTrue (contains "aria-label=\"Goods\"" html) "a parent row states its own label as its accessible name"

    Expect.isTrue (contains "aria-label=\"Cocoa\"" html) "...and so does a row nested inside it"

    // The twin without which a renderer that stated the name only on LEAVES
    // would pass: the parent is precisely the row whose computed name would be
    // wrong, so a check that only looked at leaves would guard nothing.
    Expect.isTrue
        (contains "role=\"treeitem\"" html && contains "role=\"group\"" html)
        "...and the parent genuinely owns a nested group, which is what makes the stated name necessary rather than decorative"

// --- The `style.direction` trait (Phase 1696, WIRE_FORMAT.md 3.1) ------------
//
// A trait rides the node ENVELOPE, so these five checkers are written against a
// kind chosen for being uninteresting: the claims are about the wrapper, and a
// checker leaning on some kind's own markup would be asserting that kind.
//
// Two of the five are COMPARISONS rather than emission assertions, and that is
// what makes them checkable at all. Rule 4 says `auto` is the absence of a
// declaration, and the only honest test is that the two emissions are
// byte-identical: this host DOES emit `dir="auto"` for a bidi-isolated display
// leaf under the Phase 1114 heuristic, so an "emits nothing" assertion would be
// false here and true on a host with no heuristic, and a claim that means
// different things per host is not a conformance claim. Rule 5 says nothing
// else is derived, and the test is that a declared emission differs from the
// undeclared one by the direction and its isolation ALONE - a subtraction no
// single-node assertion can express.

let private directionLeaf (id: string) (direction: TextDirection) (text: string) : Node<obj> =
    { Id = id
      Kind =
        NodeKind.Badge(
            { Defaults.badge with
                Label = TextSource.Literal text }
        )
      State = None
      Style =
        (if direction = TextDirection.Auto then
             None
         else
             Some
                 { Defaults.style with
                     Direction = direction })
      Accessibility = None
      Motion = None
      ExtraAttributes = None
      Tooltip = None
      Visible = None }

/// The same leaf with `direction` DECLARED at its identity rather than omitted.
/// The pair is what rule 4 is about.
let private directionLeafExplicitAuto (id: string) (text: string) : Node<obj> =
    { directionLeaf id TextDirection.Auto text with
        Style =
            Some
                { Defaults.style with
                    Direction = TextDirection.Auto } }

/// A declaring container holding one child, so the two claims a single leaf
/// cannot carry - inheritance and descendant emission - have a tree to act on.
let private directionBlock (child: Node<obj>) : Node<obj> =
    { Fuaran.stack
          "block"
          { Defaults.stack<obj> with
              Children = [ child ] } with
        Style =
            Some
                { Defaults.style with
                    Direction = TextDirection.Rtl } }

let private checkDeclaredDirectionEmitted () =
    let ltr = render (directionLeaf "d" TextDirection.Ltr "RR123456789IL")
    let rtl = render (directionLeaf "d" TextDirection.Rtl "\u05E9\u05DC\u05D5\u05DD")

    Expect.isTrue (contains "dir=\"ltr\"" ltr) "a declared ltr direction is emitted on the node's own wrapper"
    Expect.isTrue (contains "dir=\"rtl\"" rtl) "...and so is a declared rtl one"

    // The twin. Without it a renderer emitting `dir="ltr"` on every node would
    // pass both assertions above while saying nothing true.
    let undeclared = render (directionLeaf "d" TextDirection.Auto "plain")

    Expect.isFalse (contains "dir=\"ltr\"" undeclared) "an undeclared node must not carry a direction it never declared"

    Expect.isFalse (contains "dir=\"rtl\"" undeclared) "...in either direction"

let private checkDeclaredRunIsolated () =
    // The ISOLATION is the class, whose stylesheet rule is `unicode-bidi:
    // isolate`. `dir` alone states a direction and leaves the text AROUND the
    // run reordered, which is the half that is invisible when you look only at
    // the value itself.
    let ltr = render (directionLeaf "d" TextDirection.Ltr "RR123456789IL")
    let rtl = render (directionLeaf "d" TextDirection.Rtl "\u05E9\u05DC\u05D5\u05DD")

    Expect.isTrue (contains "fuaran-dir-ltr" ltr) "a declared ltr run carries the isolating class"
    Expect.isTrue (contains "fuaran-dir-rtl" rtl) "...and so does a declared rtl one"

    let undeclared = render (directionLeaf "d" TextDirection.Auto "plain")

    Expect.isFalse
        (contains "fuaran-dir-" undeclared)
        "an undeclared node is isolated by nothing, because it declared nothing"

let private checkDeclarationWinsOverInference () =
    // An `ltr` reference INSIDE an `rtl` block - the case the whole member
    // exists for. The nested node must carry its OWN direction rather than
    // inheriting the container's, and the container must keep its own.
    let html =
        render (directionBlock (directionLeaf "ref" TextDirection.Ltr "RR123456789IL"))

    Expect.isTrue (contains "dir=\"rtl\"" html) "the declaring container keeps its own direction"

    Expect.isTrue
        (contains "dir=\"ltr\"" html)
        "the nested declaration did not win over the inherited direction - the inference exists for values whose direction is unknown, the declaration for the ones it gets wrong"

let private checkAutoIsNoDeclaration () =
    // Rule 4 as a BYTE COMPARISON, for the reason in the block above.
    let omitted = render (directionLeaf "d" TextDirection.Auto "plain")
    let explicitAuto = render (directionLeafExplicitAuto "d" "plain")

    Expect.equal
        explicitAuto
        omitted
        "a node declaring `auto` must render identically to the same node omitting the member - `auto` IS the absence of a declaration"

let private checkNoDerivedDirectionBehaviour () =
    // The SUBTRACTION: a declared emission differs from the undeclared one by
    // the direction attribute and its isolation class, and by nothing else. A
    // renderer that also flipped an alignment, swapped a layout side or pushed a
    // direction onto descendants fails here and passes every assertion above.
    let undeclared = render (directionLeaf "d" TextDirection.Auto "RR123456789IL")
    let declared = render (directionLeaf "d" TextDirection.Rtl "RR123456789IL")

    let stripped =
        declared
            .Replace(" dir=\"rtl\"", "", StringComparison.Ordinal)
            .Replace(" fuaran-dir-rtl", "", StringComparison.Ordinal)

    Expect.equal
        stripped
        undeclared
        "a declared direction changed something other than the direction and its isolation - no layout side, locale, alignment or descendant direction may be derived from it"

    // ...and the descendant half, stated separately because a single leaf cannot
    // carry it: an undeclared child inside a declaring parent emits no direction
    // of its own. Inheritance is the receiving surface's, not a second emission.
    let html =
        render (directionBlock (directionLeaf "child" TextDirection.Auto "plain"))

    Expect.equal
        (Regex.Matches(html, "dir=\"rtl\"").Count)
        1
        "exactly one element declared a direction, so exactly one may carry it - a direction pushed onto descendants is a derived behaviour rule 5 forbids"

// --- DataGrid: the interactive-row class (Phase 1701) ---------------------

/// A data-bound grid, with or without a declared row action. The source is a
/// `Query` rather than a `Static` payload because this host renders the bound
/// leg as a hydration placeholder either way, which is the point of the first
/// assertion below.
let private boundGrid (nodeId: string) (rowAction: bool) : Node<obj> =
    Fuaran.grid
        nodeId
        id
        { Defaults.grid<Row, obj> with
            Columns = [ Column.text "Reference" (fun _ -> "") ]
            Source = Binding.Query("settlements", (fun _ -> Seq.empty), None)
            RowKeyField = Some "reference"
            OnRowClick =
                (if rowAction then
                     Some(fun (_: Row) -> Action.Chain [])
                 else
                     Option.None) }

/// The same grid in `staticRows` mode. The mode honours no row action in any
/// tier - a static row is a `TextSource` list, not the row value a declared
/// action is applied to - so the declaration is readable at the point the rows
/// are built and must still reach no row.
///
/// Built by record-updating the erased spec rather than through `Fuaran.table`,
/// which pins `OnRowClick` to `None` by construction and so cannot express the
/// input this assertion is about.
let private staticGrid (nodeId: string) (rowAction: bool) : Node<obj> =
    let rows: StaticRows =
        { Headers = [ TextSource.Literal "Reference" ]
          Rows = [ [ TextSource.Literal "S-1" ] ]
          Sortable = Option.None
          DefaultSort = Option.None }

    let node = boundGrid nodeId rowAction

    match node.Kind with
    | NodeKind.DataGrid spec ->
        { node with
            Kind = NodeKind.DataGrid { spec with StaticRows = Some rows } }
    | _ -> failwith "boundGrid must build a DataGrid"

let private checkInteractiveRowOnlyWithAction () =
    // What this host can and cannot answer for, stated here because the
    // asymmetry is the claim's own (§3.6.24 rules 2 and 3) rather than a gap in
    // this suite. The reference SERVER renderer draws a bound grid as a
    // hydration placeholder, so it emits no bound row at all and the POSITIVE
    // half of rule 1 — "a marked row appears where the action is declared" — is
    // answered by the hosts that render bound rows, not here. What is answered
    // here, and is not vacuous, is every NEGATIVE half: no leg of this renderer
    // may emit the marker, and the static leg may not emit it even though the
    // declaration is in scope at exactly the point the rows are built.
    let marker = "fuaran-grid-row-interactive"

    // Rule 3 — the placeholder leg emits no row, so no marker, either way.
    for rowAction in [ true; false ] do
        let html = render (boundGrid "g" rowAction)

        Expect.isTrue
            (html |> contains "fuaran-grid-ssr-placeholder")
            "the bound leg is expected to render as a hydration placeholder here - the assertion below means nothing if it started rendering rows"

        Expect.isFalse
            (html |> contains marker)
            "a hydration placeholder emits no row, so it may emit no interactive-row marker - there is nothing on screen for the marker to be about"

    // Rule 2 — the static leg renders real rows AND can read the declaration,
    // and must still emit no marker: the mode honours no row action in any tier,
    // so a marked row there would promise a click nothing can deliver.
    let declaredHtml = render (staticGrid "s" true)

    Expect.isTrue
        (declaredHtml |> contains "fuaran-table-row")
        "the static leg is expected to render real rows here - the assertion below means nothing if it stopped"

    Expect.isFalse
        (declaredHtml |> contains marker)
        "a `staticRows` grid honours no row action in any tier, so its rows carry no interactive-row marker whatever the grid declares"

    // ...and the undeclared static grid, so the assertion above is known to be
    // about the DECLARATION rather than about the static leg emitting nothing
    // interesting.
    Expect.isFalse
        (render (staticGrid "s" false) |> contains marker)
        "a grid declaring no row action carries the interactive-row marker on no row"

// --- Sparkline float-sequence resolution (Phase 1704, WIRE_FORMAT.md 24.7) ---
//
// The claims are about a HOST-FED series, so these two checkers are the only
// ones in this file that render against a non-empty store. That is not an
// accident of convenience: a float-sequence slot TYPES its elements at decode,
// so `[1,"3.5",3]` is a `WRONG_TYPE` and no document can carry the case. The
// store is the only place a foreign element exists, which is exactly why §24.7
// is a render obligation and not a codec family.
//
// The observable is the emitted `<polyline points="...">`: the lowering yields
// one point per series element, so counting points counts readings. An
// assertion on the em-dash alone could not tell a host that read every element
// from one that read the first two.

/// A sparkline whose series comes from the store, not the document - the shape
/// the corpus carries at `nodes/state-absent-default.json`'s
/// `absent-default-sparkline`, reproduced here so the checker renders one node
/// rather than digging one out of a six-node composite.
let private boundSparkline: Node<obj> =
    { Id = "absent-default-sparkline"
      Kind = NodeKind.Sparkline({ Source = Binding.State("series", None) })
      State = None
      Style = None
      Accessibility = None
      Motion = None
      ExtraAttributes = None
      Tooltip = None
      Visible = None }

let private renderSeries (series: obj) : string =
    Render.render
        { BindingResolver.empty with
            State = Map.ofList [ "series", series ] }
        boundSparkline

/// The number of readings the emission shows: one `x,y` pair per series element.
let private pointCount (html: string) : int =
    let m = Regex.Match(html, "points=\"([^\"]*)\"")

    if not m.Success then
        0
    else
        m.Groups.[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length

let private checkFloatSeqReadsElementWise () =
    // This host's store is TYPED, so the only element that is not a number and
    // can still reach the resolver is the sentinel itself. That is the case the
    // claim is about: a NaN keeps its POSITION and the series keeps its length.
    let finite = renderSeries (box ([ 1.0; 2.0; 3.0; 4.0 ]: float list))

    let withSentinel = renderSeries (box ([ 1.0; Double.NaN; 3.0; 4.0 ]: float list))

    Expect.equal (pointCount finite) 4 "a four-element series draws four readings"

    Expect.equal
        (pointCount withSentinel)
        4
        "a sentinel element neither shortens the series nor suppresses it - a series index is a position, so a dropped reading slides every later one left"

    Expect.isFalse
        (contains "fuaran-sparkline-empty" withSentinel)
        "one unreadable element must not throw away the readable ones - the em-dash is the UNRESOLVED case, not the partly-readable one"

let private checkFloatSeqAcceptSetClosed () =
    // The accept set is §7's and closed. This host never coerces, because its
    // store is typed and a decimal string is not a `float seq` at all - so the
    // assertion is that the value 3.5 is nowhere in the emission, and its twin
    // is that the genuine number 3.5 IS read. Without the twin a host that read
    // nothing at all would pass.
    let coerced = renderSeries (box ([| box 0.0; box "3.5"; box 7.0 |]: obj[]))

    let genuine = renderSeries (box ([ 0.0; 3.5; 7.0 ]: float list))

    // The twin FIRST, so the comparison below is against a real render rather
    // than against two em-dashes agreeing about nothing.
    Expect.equal (pointCount genuine) 3 "the genuine number is read - the closed set admits JSON numbers"

    // The comparison IS the claim, and it is the only formulation that reads the
    // same on every host: a host that coerced `"3.5"` would emit byte-identical
    // markup for the two, whatever its geometry. An assertion that the literal
    // characters `3.5` are absent would pass on a host that coerced and then
    // scaled the coordinate away.
    Expect.notEqual
        coerced
        genuine
        "a decimal string resolved to the number it spells - the decode path at this same slot refuses `\"3.5\"`, so a resolver that accepts it makes the two halves of one slot disagree about what a number is"

/// The registry: which (kind, claim) pairs this host asserts, and how.
///
/// Keyed by the claim's WIRE token rather than the DU case, because the
/// enumeration this is matched against comes from the artefact.
let private checkers: ((string * string) * (unit -> unit)) list =
    [ ("Media", "accessible-name-always"), checkAccessibleNameAlways
      ("Media", "autoplay-muted-pairing"), checkAutoplayMutedPairing
      ("Media", "no-autoplay-pathway"), checkNoAutoplayPathway
      ("Media", "refused-source-dropped"), checkRefusedSourceDropped
      ("Media", "authored-child-order"), checkAuthoredChildOrder
      ("Media", "single-default-per-kind"), checkSingleDefaultPerKind
      ("Media", "transcript-disclosure-named"), checkTranscriptDisclosureNamed
      ("Image", "alt-always-emitted"), checkAltAlwaysEmitted
      ("Image", "anchor-affordance-on-expandable"), checkAnchorAffordanceOnExpandable
      ("Image", "refused-src-no-affordance"), checkRefusedSrcNoAffordance
      ("Image", "figure-caption-outside-link"), checkFigureCaptionOutsideLink
      ("Image", "srcset-ascending-by-width"), checkSrcSetAscendingByWidth
      ("Custom", "unregistered-custom-labelled"), checkUnregisteredCustomLabelled
      ("Embed", "accessible-name-always"), checkEmbedAccessibleNameAlways
      ("Embed", "sandbox-always-exactly-declared"), checkEmbedSandboxAlwaysExactlyDeclared
      ("Embed", "refused-embed-source-omitted"), checkRefusedEmbedSourceOmitted
      ("FileUpload", "picker-always-present"), checkPickerAlwaysPresent
      ("FileUpload", "ceiling-recorded-never-enforced"), checkCeilingRecordedNeverEnforced
      ("Modal", "aria-modal-only-when-blocking"), checkAriaModalOnlyWhenBlocking
      ("Tree", "accessible-name-always"), checkTreeAccessibleNameAlways
      // Phase 1696 - the node-level trait, keyed by its id rather than a kind.
      ("style.direction", "declared-direction-emitted"), checkDeclaredDirectionEmitted
      ("style.direction", "declared-run-isolated"), checkDeclaredRunIsolated
      ("style.direction", "declaration-wins-over-inference"), checkDeclarationWinsOverInference
      ("style.direction", "auto-is-no-declaration"), checkAutoIsNoDeclaration
      ("style.direction", "no-derived-direction-behaviour"), checkNoDerivedDirectionBehaviour
      // Phase 1701 - the row-action affordance.
      ("DataGrid", "interactive-row-only-with-action"), checkInteractiveRowOnlyWithAction
      // Phase 1704 - the two float-sequence resolution claims (§24.7).
      ("Sparkline", "float-seq-reads-element-wise"), checkFloatSeqReadsElementWise
      ("Sparkline", "float-seq-accept-set-closed"), checkFloatSeqAcceptSetClosed ]

/// Obligations this host declares it does NOT check, each with a reason.
///
/// EMPTY is the correct state for the reference host: it renders every canonical
/// kind, so every declared obligation is one it owes. The list exists because
/// the alternative — an unchecked obligation silently absent from the registry —
/// is precisely the failure the manifest replaces. A host that genuinely cannot
/// check a claim (no player, no network loader, a decode-only surface) records
/// it here and its report says so out loud.
let private declaredExemptions: ((string * string) * string) list = []

/// This host's answer for one declared obligation.
let private statusOf (kind: string) (claimId: string) : ObligationOutcome =
    if checkers |> List.exists (fun ((k, c), _) -> k = kind && c = claimId) then
        ObligationOutcome.Asserted
    else
        match declaredExemptions |> List.tryFind (fun ((k, c), _) -> k = kind && c = claimId) with
        | Some(_, reason) -> ObligationOutcome.Unchecked reason
        | None ->
            ObligationOutcome.Unchecked
                "no checker registered in RenderObligationTests and no declared exemption — add one, or declare why this host cannot check it"

let private reportLine (o: DeclaredObligation) =
    let outcome = statusOf o.Kind o.ClaimId

    { Kind = o.Kind
      ClaimId = o.ClaimId
      Statement = ""
      Section = o.Section
      Outcome = outcome }

[<Tests>]
let renderObligationConformance =
    testList
        "Phase 1105 — executable render-obligation conformance"
        [

          // ── The gate ────────────────────────────────────────────────────────
          test "every obligation the manifest declares is asserted by this host" {
              let report = declaredObligations |> List.map reportLine

              Expect.isNonEmpty
                  report
                  "the manifest declares no obligations at all — either the artefact is stale or this suite is reading the wrong file, and either way it is asserting nothing"

              // NOT CHECKED IS NOT PASSED. Everything this host did not assert is
              // printed by name and section before the gate decides, so an
              // exempted claim is visible in the run rather than inferable from
              // its absence.
              let unmet = unasserted report

              for line in unmet do
                  printfn "  render obligation not asserted: %s" (describeReport line)

              let undeclared =
                  unmet
                  |> List.filter (fun l ->
                      not (
                          declaredExemptions
                          |> List.exists (fun ((k, c), _) -> k = l.Kind && c = l.ClaimId)
                      ))
                  |> List.map (fun l -> sprintf "%s/%s [%s]" l.Kind l.ClaimId l.Section)

              Expect.isEmpty
                  undeclared
                  "a render obligation this host owes has no checker: assert it in RenderObligationTests, or add a declared exemption saying why this host cannot"
          }

          // ── The go-red proof ────────────────────────────────────────────────
          test "an obligation with no checker is reported UNCHECKED (negative probe)" {
              // This is the shape a NEWLY-DECLARED obligation takes on the day it
              // lands: a kind/claim pair the registry does not cover. Without
              // this probe the gate above could be green because the
              // classification never reports anything, which is the completeness
              // check that cannot fail.
              match statusOf "Markdown" "accessible-name-always" with
              | ObligationOutcome.Unchecked reason ->
                  Expect.stringContains
                      reason
                      "no checker registered"
                      "an unregistered claim must say so, in words a reader can act on"
              | other ->
                  failtestf
                      "an unregistered (kind, claim) must be reported UNCHECKED, got %A — the gate cannot go red"
                      other

              // …and the gate's own filter must then classify it as undeclared,
              // which is what turns the suite red.
              let probe =
                  { Kind = "Markdown"
                    ClaimId = "accessible-name-always"
                    Statement = ""
                    Section = "probe"
                    Outcome = statusOf "Markdown" "accessible-name-always" }

              Expect.equal (unasserted [ probe ] |> List.length) 1 "the probe must survive the unasserted filter"
          }

          // ── The vocabulary seam ─────────────────────────────────────────────
          test "every claim id the manifest names is in this host's closed vocabulary" {
              // A manifest claim this package's `ObligationClaim` does not carry
              // means the corpus is AHEAD of the shipped declaration — a real
              // state on a host pinned to an older package, and one that must be
              // reported rather than skipped past. The corpus is the oracle; a
              // host that cannot name a claim cannot have checked it.
              let known = allClaims |> List.map claimId |> Set.ofList

              let unknown =
                  declaredObligations
                  |> List.map (fun o -> o.ClaimId)
                  |> List.distinct
                  |> List.filter (fun id -> not (Set.contains id known))

              Expect.isEmpty
                  unknown
                  "the corpus declares a render obligation this package's closed vocabulary does not carry — the shipped Fuaran.UI is behind the corpus artefact"
          }

          // ── The registry is not itself a second source of truth ─────────────
          test "no checker is registered for an obligation the manifest does not declare" {
              // The inverse direction. A checker for a claim no row declares is a
              // stale assertion: it passes forever and guards a contract that has
              // moved, which is exactly the drift the generated artefact exists
              // to remove.
              let declared =
                  declaredObligations |> List.map (fun o -> o.Kind, o.ClaimId) |> Set.ofList

              let orphans =
                  checkers
                  |> List.map fst
                  |> List.filter (fun pair -> not (Set.contains pair declared))
                  |> List.map (fun (k, c) -> sprintf "%s/%s" k c)

              Expect.isEmpty
                  orphans
                  "a checker asserts an obligation no manifest row declares — either the row was removed or the checker was never declared"
          }

          // ── The checkers themselves ─────────────────────────────────────────
          //
          // Registered above and run here by name, so a failing obligation names
          // the claim it broke rather than surfacing as one opaque red test.
          yield!
              checkers
              |> List.map (fun ((kind, claim), check) -> test (sprintf "%s owes %s" kind claim) { check () }) ]
