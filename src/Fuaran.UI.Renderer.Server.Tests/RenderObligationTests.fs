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
let private tryCorpusArtifact () : string option =
    let rec walk (dir: DirectoryInfo) =
        if isNull dir then
            None
        else
            let candidate =
                Path.Combine(dir.FullName, "wire-format-fixtures", "render-fidelity.json")

            if File.Exists candidate then
                Some candidate
            else
                walk dir.Parent

    walk (DirectoryInfo(AppContext.BaseDirectory))

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
              | _ -> () ]
    | None ->
        allObligations
        |> List.map (fun (kind, o) ->
            { Kind = kind
              ClaimId = claimId o.Claim
              Section = o.Section })

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
      ("Modal", "aria-modal-only-when-blocking"), checkAriaModalOnlyWhenBlocking ]

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
