module Fuaran.UI.JsonDecode.Tests.RejectFixtures

// ============================================================================
//  Reject-fixture corpus (data form).
//
//  Each entry is a hand-authored bad-shape JSON blob that must fail decoding
//  with a specific `DecodeError` Code + Path prefix. They live in this
//  data table so a single source feeds BOTH the corpus emitter
//  (`Corpus.emit` writes one `reject/<id>.json` per entry + manifest
//  metadata) AND the reject-fixture test suite (which loads the corpus).
//
//  The `Id` doubles as the corpus filename stem (`reject/<id>.json`) and the
//  manifest `id`. The `ExpectedCode` / `ExpectedPath` become the manifest's
//  `expectedErrorCode` / `expectedPath` — the conformance contract a second
//  host (e.g. the Wave 9 TS decoder) asserts against without reading F#.
// ============================================================================

open Fuaran.UI.Ops.JsonDecode

type RejectFixture =
    {
        /// Corpus filename stem + manifest id.
        Id: string
        /// The malformed wire payload.
        Json: string
        /// Expected `DecodeError.Code` (manifest `expectedErrorCode`).
        ExpectedCode: DecodeErrorCode
        /// Expected `DecodeError.Path` prefix (manifest `expectedPath`).
        ExpectedPath: string
        /// `true` ⇒ decode via `decodeOp`; `false` ⇒ `decodeNode`.
        IsOp: bool
        /// Human-readable corpus/manifest description.
        Description: string
    }

let all: RejectFixture list =
    [
      // ─── INVALID_JSON ────────────────────────────────────────────────
      { Id = "reject-invalid-garbage"
        Json = "this is not json"
        ExpectedCode = DecodeErrorCode.INVALID_JSON
        ExpectedPath = "$"
        IsOp = false
        Description = "garbage input" }
      { Id = "reject-invalid-truncated"
        Json = "{\"id\":"
        ExpectedCode = DecodeErrorCode.INVALID_JSON
        ExpectedPath = "$"
        IsOp = false
        Description = "truncated object" }
      { Id = "reject-invalid-empty"
        Json = ""
        ExpectedCode = DecodeErrorCode.INVALID_JSON
        ExpectedPath = "$"
        IsOp = false
        Description = "empty string" }

      // ─── MISSING_FIELD ───────────────────────────────────────────────
      { Id = "reject-missing-node-id"
        Json =
          """{"kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.id"
        IsOp = false
        Description = "Node missing id" }
      { Id = "reject-missing-node-kind"
        Json = """{"id":"x","state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.kind"
        IsOp = false
        Description = "Node missing kind" }
      // Phase 460 retired `reject-missing-style-tone`: a `style` object missing
      // `tone` is now VALID (restore-on-absence to `ToneVariant.Default`, §3.6),
      // so the old reject fixture contradicts the shipped decoder. The read-compat
      // + omit-when-default behaviour is pinned by StyleOmitDefaultTests.fs.
      { Id = "reject-missing-markdown-text"
        Json =
          """{"id":"x","kind":{"$type":"Markdown"},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.kind.text"
        IsOp = false
        Description = "Markdown spec missing text" }
      { Id = "reject-missing-metric-value"
        Json =
          """{"id":"x","kind":{"$type":"Metric","label":{"$type":"Literal","text":"L"},"format":{"$type":"None"},"tone":"Default","weight":"Standard","emphasis":"Normal"},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.kind.value"
        IsOp = false
        Description = "Metric spec missing value" }
      { Id = "reject-missing-binding-type"
        Json =
          """{"id":"x","kind":{"$type":"Metric","label":{"$type":"Literal","text":"L"},"format":{"$type":"None"},"tone":"Default","weight":"Standard","emphasis":"Normal","value":{"value":1.0}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.kind.value.$type"
        IsOp = false
        Description = "Binding missing $type" }
      { Id = "reject-missing-custom-moduleid"
        Json =
          """{"id":"x","kind":{"$type":"Custom","componentId":"c","props":{}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.kind.moduleId"
        IsOp = false
        Description = "Custom missing moduleId" }
      // Switch (Phase 392) — each required field isolated (the decoder checks
      // stateKey, then cases, then default; duplicate `match` values are a
      // validator error FUARAN082, NOT a decode reject — first-match-wins keeps
      // decode structural, mirroring FragmentDecl name collisions).
      { Id = "reject-missing-switch-statekey"
        Json =
          """{"id":"x","kind":{"$type":"Switch","cases":[{"match":"a","child":{"id":"c","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"A"}}}}],"default":{"id":"d","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"D"}}}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.kind.stateKey"
        IsOp = false
        Description = "Switch missing stateKey" }
      { Id = "reject-missing-switch-cases"
        Json =
          """{"id":"x","kind":{"$type":"Switch","stateKey":"v","default":{"id":"d","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"D"}}}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.kind.cases"
        IsOp = false
        Description = "Switch missing cases" }
      { Id = "reject-missing-switch-default"
        Json =
          """{"id":"x","kind":{"$type":"Switch","stateKey":"v","cases":[{"match":"a","child":{"id":"c","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"A"}}}}]},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.kind.default"
        IsOp = false
        Description = "Switch missing default Node" }
      { Id = "reject-missing-mount-scopeid"
        Json =
          """{"id":"x","kind":{"$type":"Mount","channel":{"direction":"OutOnly"},"capabilities":[],"onBubble":"<closure>"},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.kind.scopeId"
        IsOp = false
        Description = "Mount missing scopeId (§4o)" }

      // ─── WRONG_TYPE ──────────────────────────────────────────────────
      { Id = "reject-wrongtype-id"
        Json =
          """{"id":42,"kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.id"
        IsOp = false
        Description = "Node id is integer not string" }
      { Id = "reject-wrongtype-kind"
        Json =
          """{"id":"x","kind":"Markdown","state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind"
        IsOp = false
        Description = "Node kind is a bare string not an object" }
      { Id = "reject-wrongtype-heading-level"
        Json =
          """{"id":"x","kind":{"$type":"Heading","level":"two","text":{"$type":"Literal","text":"H"},"variant":"Standard"},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.level"
        IsOp = false
        Description = "Heading level is string not number" }
      { Id = "reject-wrongtype-style-tone"
        Json =
          """{"id":"x","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"state":{},"style":{"emphasis":"Normal","tone":7,"weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.style.tone"
        IsOp = false
        Description = "Style.tone is integer not string" }
      { Id = "reject-wrongtype-box-children"
        Json =
          """{"id":"x","kind":{"$type":"Box","children":{},"layout":{"$type":"Auto"},"role":"Group"},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.children"
        IsOp = false
        Description = "Box children is object not array" }
      { Id = "reject-wrongtype-discriminator"
        Json =
          """{"id":"x","kind":{"$type":"Markdown","text":{"$type":42,"text":"x"}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.text.$type"
        IsOp = false
        Description = "Discriminator under Style is number" }
      { Id = "reject-wrongtype-static-sort-column"
        Json =
          """{"id":"x","kind":{"$type":"DataGrid","columns":[],"source":{"$type":"Static","value":[]},"staticRows":{"defaultSort":{"column":-1,"direction":"asc"},"headers":["A"],"rows":[["1"],["2"]]}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.staticRows.defaultSort.column"
        IsOp = false
        Description =
          "staticRows defaultSort column -1 — a header index is non-negative; the schema says the same with minimum:0 (Phase 801)" }
      { Id = "reject-wrongtype-grid-default-sort-column"
        Json =
          """{"id":"x","kind":{"$type":"DataGrid","columns":[],"defaultSort":{"column":-1,"direction":"asc"},"source":{"$type":"Static","value":[]}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.defaultSort.column"
        IsOp = false
        Description =
          "bound-grid defaultSort column -1 — the same non-negative bound the staticRows spelling carries, same record, same message (Phase 861)" }
      // Phase 863 — the grid-behaviour family's NEAR MISSES. Each of these
      // decoded silently before: rule 2 tolerates unknown keys, so a model that
      // reached for the wrong name got a tree that rendered while the
      // declaration did nothing. The didactic names the canonical form.
      { Id = "reject-nearmiss-grid-current-page"
        Json =
          """{"id":"x","kind":{"$type":"DataGrid","columns":[],"currentPage":1,"source":{"$type":"Static","value":[]}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.currentPage"
        IsOp = false
        Description =
          "grid 'currentPage' — the sharpest near miss: a LITERAL page number is not expressible, the position lives in State so a pager can move it (Phase 863)" }
      { Id = "reject-nearmiss-grid-sortable"
        Json =
          """{"id":"x","kind":{"$type":"DataGrid","columns":[],"sortable":true,"source":{"$type":"Static","value":[]}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.sortable"
        IsOp = false
        Description =
          "grid-level 'sortable' — the staticRows spelling reached for on the bound path, where sortability is sortStateKey + per-column narrowing (Phase 863)" }
      { Id = "reject-nearmiss-column-readonly"
        Json =
          """{"id":"x","kind":{"$type":"DataGrid","columns":[{"field":"note","kind":{"$type":"Text"},"label":"Note","readOnly":true}],"source":{"$type":"Static","value":[]}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.columns[0].readOnly"
        IsOp = false
        Description =
          "column 'readOnly' — named by the census row itself; deliberately NOT aliased to editable:false, because an inverting alias that guesses wrong makes a read-only column editable (Phase 863)" }
      { Id = "reject-nearmiss-grid-behaviour-record"
        Json =
          """{"id":"x","kind":{"$type":"DataGrid","behaviour":{"page":{}},"columns":[],"source":{"$type":"Static","value":[]}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.behaviour"
        IsOp = false
        Description =
          "grid 'behaviour' record — charter option O1-C, rejected by design: three optional sibling fields, not a nested record (Phase 860 charter / 863)" }
      { Id = "reject-wrongtype-grid-page-size-zero"
        Json =
          """{"id":"x","kind":{"$type":"DataGrid","columns":[],"pageSize":0,"pageStateKey":"p","source":{"$type":"Static","value":[]}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.pageSize"
        IsOp = false
        Description =
          "grid pageSize 0 — a page of no rows names no page; the schema says the same with minimum:1 (Phase 862)" }

      // ─── UNKNOWN_DU_CASE ─────────────────────────────────────────────
      { Id = "reject-unknown-tone"
        Json =
          """{"id":"x","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"state":{},"style":{"emphasis":"Normal","tone":"Magenta","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.style.tone"
        IsOp = false
        Description = "ToneVariant value 'Magenta'" }
      // Phase 1073 — the RESERVED case. Phase 867 admitted `trendPolarity` as a
      // two-case enum with `Neutral` reserved-not-admitted and authored no reject
      // vector, so five hosts each decided the question in their own test suite
      // rather than against the corpus. They happened to agree; this fixture is
      // what makes the agreement ARBITRATED rather than coincidental, and is what
      // a sixth host will be measured against. The path is the bare slot, per §6
      // and the Phase 1073 ruling: a bare enum carries no discriminator on the
      // wire, so there is no `.$type` to name.
      { Id = "reject-unknown-trend-polarity"
        Json =
          """{"id":"m","kind":{"$type":"Metric","label":"Avg wait","trend":{"$type":"Static","value":-0.0734},"trendPolarity":"Neutral","value":{"$type":"Static","value":80}}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.kind.trendPolarity"
        IsOp = false
        Description =
          "TrendPolarity value 'Neutral' — RESERVED, not admitted (§3.6.1 clause 5); refused exactly like a name nobody has proposed, so a later admission is an ADDITION rather than a re-meaning of shipped bytes" }
      // Phase 1077 — the near-miss an author will actually write. `aspectRatio`
      // is a closed TOKEN vocabulary, not a CSS value, so the ratio spelling the
      // stylesheet uses (`16 / 9`) is refused rather than parsed: admitting it
      // would put an arbitrary numeric pair on the wire and re-open the
      // free-form-CSS escape the token vocabulary exists to close. The path is
      // the bare slot with no `.$type` suffix, per §6 and the Phase 1073 ruling.
      { Id = "reject-unknown-image-aspect"
        Json =
          """{"id":"i","kind":{"$type":"Image","alt":{"$type":"Literal","text":"Hero"},"aspectRatio":"16/9","src":{"$type":"Static","value":"/hero.jpg"},"variant":"Default"}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.kind.aspectRatio"
        IsOp = false
        Description =
          "ImageAspect value '16/9' — the CSS ratio spelling, refused: the slot is a closed token vocabulary (Square | FourThree | ThreeTwo | SixteenNine), and admitting a numeric pair would reintroduce the free-form escape the tokens replace" }
      // Phase 1080 — the `srcSet` width floor. `width` is the `w` descriptor a
      // browser selects on, so a non-positive one names a candidate that can
      // never be chosen: the wire would be able to state a rendition no host can
      // render, which is the class of document a codec exists to refuse. Zero is
      // refused as firmly as a negative, and it is the interesting half — a
      // negative reads as a mistake, while `0` reads as "unspecified" to anyone
      // who has not read the spec, which is exactly why it must not decode.
      //
      // The path names the ENTRY by index and then the slot, because a
      // multi-candidate list needs to say WHICH candidate is wrong; a path of
      // `$.kind.srcSet.width` would be true of a list with one entry and useless
      // for any other.
      { Id = "reject-image-srcset-nonpositive-width"
        Json =
          """{"id":"i","kind":{"$type":"Image","alt":{"$type":"Literal","text":"Hero"},"src":{"$type":"Static","value":"/hero.jpg"},"srcSet":[{"src":{"$type":"Static","value":"/hero-800.jpg"},"width":800},{"src":{"$type":"Static","value":"/hero-0.jpg"},"width":0}],"variant":"Default"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.srcSet[1].width"
        IsOp = false
        Description =
          "SrcSetEntry width 0 — refused: a `w` descriptor names the intrinsic pixel width a browser selects on, so a non-positive value describes a candidate that can never be selected. The first entry is well-formed on purpose, so the path has to identify the second" }
      // Phase 1082 — the masonry column floor, the `srcSet` width precedent
      // applied to a layout. A column COUNT of zero or less names an
      // arrangement no renderer can realise: `column-count: 0` is invalid CSS,
      // and a container declaring it would fall back to whatever the host's
      // stylesheet last said, so the wire would carry a layout whose rendered
      // result is host-defined. Zero is the interesting half again — it reads
      // as "let the browser decide" to an emitter that has not read the spec,
      // and `Grid`'s auto-column leniency makes that reading actively plausible
      // here, which is precisely why `Masonry` must refuse it rather than
      // canonicalise: `Grid` has `Auto` to mean the browser's choice, and
      // masonry has nothing for the rewrite to land on.
      { Id = "reject-box-masonry-nonpositive-cols"
        Json =
          """{"id":"m","kind":{"$type":"Box","children":[],"layout":{"$type":"Masonry","cols":0},"role":"Group"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.layout.cols"
        IsOp = false
        Description =
          "Masonry cols 0 — refused: a column count names how many columns the children fill, so a non-positive one describes a layout no renderer can realise. Refused rather than canonicalised to `Auto`, unlike a column-less `Grid`: that leniency works because `Auto` already means the browser's own choice, and masonry has no such case for the rewrite to land on" }
      // Phase 1080 — `srcSet: null` is REFUSED, and this is the fixture that
      // makes the missing-list-field decode class a wire law rather than each
      // host's reading. An ABSENT `srcSet` is the empty list; a PRESENT `null`
      // is a host that had a spelling for absence and emitted a different one.
      // Accepting it would make three spellings mean one thing, and the first
      // host to round-trip `null` back out would emit bytes no other host
      // produces for the same document.
      { Id = "reject-image-srcset-null"
        Json =
          """{"id":"i","kind":{"$type":"Image","alt":{"$type":"Literal","text":"Hero"},"src":{"$type":"Static","value":"/hero.jpg"},"srcSet":null,"variant":"Default"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.srcSet"
        IsOp = false
        Description =
          "Image srcSet `null` — refused. Absent means the EMPTY LIST (the missing-list-field decode class); a present null is a second spelling for an absence that already has one, and admitting it would let two hosts disagree about the canonical bytes of the same document" }
      // Phase 1079 — `"expandable":"true"` is the near-miss an emitter actually
      // produces: a model or a templating layer that stringifies everything.
      // Refusing it rather than coercing is the point. A truthiness rule would
      // have to answer what `"false"`, `""` and `"no"` mean, and every answer
      // is a different host's answer — so `"expandable":"false"` would turn an
      // affordance ON in one host and leave it off in another, for bytes both
      // called valid. The slot declares an interaction; a document that is
      // ambiguous about whether it declares one is not a document.
      { Id = "reject-image-expandable-nonbool"
        Json =
          """{"id":"i","kind":{"$type":"Image","alt":{"$type":"Literal","text":"Hero"},"expandable":"true","src":{"$type":"Static","value":"/hero.jpg"},"variant":"Default"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.expandable"
        IsOp = false
        Description =
          "Image expandable `\"true\"` — the stringified boolean, refused rather than coerced. A truthiness rule would have to rule on `\"false\"` and `\"\"` too, and two hosts ruling differently would disagree about whether the document declares an affordance at all" }
      // Phase 1076 — the a11y floor, expressed where a decoder can enforce it.
      // `label` is REQUIRED on `Media` where `caption` is optional on `Image`,
      // and the difference is the whole rule: an image can be decorative and
      // say so with an empty alt, while a media element is a transport and is
      // never decorative. A document that omits the label describes a player a
      // screen-reader user is told exists and cannot identify, so it is refused
      // rather than defaulted — there is no honest value to default TO.
      { Id = "reject-media-missing-label"
        Json =
          """{"id":"m","kind":{"$type":"Media","kind":{"$type":"Video"},"src":{"$type":"Static","value":"/clip.mp4"}}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.kind.label"
        IsOp = false
        Description =
          "Media with no `label` — refused. The accessible name is mandatory because a transport has no decorative case, and there is no value to default to that would not be a fabricated name for someone else's recording" }
      // Phase 1076 — the structurally wrong PAYLOAD, the `Image` reject-vector
      // shape applied to a variant union. `MediaKind` is `$type`-discriminated,
      // so unlike the bare-enum rejects above the path DOES carry the `.$type`
      // suffix (§6, and the Phase 1073 ruling that distinguishes the two
      // positions). `"Stream"` is the near-miss an author actually writes: a
      // third surface that sounds like it should exist, refused exactly like a
      // name nobody has proposed, so admitting one later is an ADDITION rather
      // than a re-meaning of shipped bytes.
      { Id = "reject-unknown-media-kind"
        Json =
          """{"id":"m","kind":{"$type":"Media","kind":{"$type":"Stream"},"label":"Live feed","src":{"$type":"Static","value":"/live.m3u8"}}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.kind.kind.$type"
        IsOp = false
        Description =
          "MediaKind case 'Stream' — refused. The variant set is closed at Video | Audio; a third surface is an admission, not a spelling a decoder may guess at. The path carries `.$type` because MediaKind is discriminated, unlike the bare-enum slots" }
      // Phase 1076 — the stringified boolean, on the slot where coercing it
      // would be worst. `Image.expandable` refuses the same shape and the
      // reasoning is the same, but the CONSEQUENCE differs: a host that read
      // `"autoplay":"false"` as truthy would start playing a video the document
      // says not to, in a page the reader did not ask to make noise. The path
      // names the slot INSIDE the case payload, which is also what pins where
      // the slot lives.
      { Id = "reject-media-autoplay-nonbool"
        Json =
          """{"id":"m","kind":{"$type":"Media","kind":{"$type":"Video","autoplay":"true"},"label":"Ambient loop","src":{"$type":"Static","value":"/ambient.mp4"}}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.kind.autoplay"
        IsOp = false
        Description =
          "Media autoplay `\"true\"` — the stringified boolean, refused rather than coerced. A truthiness rule would have to rule on `\"false\"` and `\"\"` too, and two hosts ruling differently would disagree about whether a page starts playing by itself" }
      // Phase 1110 — `srcLang` is REQUIRED on every track kind, where HTML asks
      // for it only on subtitles. The strictness is what makes a track menu
      // routable: the language orders it, drives pronunciation, and is the only
      // thing telling two English-labelled tracks of different languages apart.
      // A default here would have to be invented, and an invented language tag
      // is worse than none — it makes a speech engine confidently wrong.
      { Id = "reject-media-track-missing-srclang"
        Json =
          """{"id":"m","kind":{"$type":"Media","kind":{"$type":"Video"},"label":"Studio walkthrough","src":{"$type":"Static","value":"/walkthrough.mp4"},"tracks":[{"kind":"Captions","label":"English captions","src":{"$type":"Static","value":"/walkthrough.en.vtt"}}]}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.kind.tracks[0].srcLang"
        IsOp = false
        Description =
          "a text track with no `srcLang` — refused. The language tag is what orders a track menu, drives pronunciation and tells two same-labelled tracks apart; there is no value to default to that would not be an invented claim about someone else's recording. The path carries the ARRAY INDEX, so a document with four tracks names the one at fault" }
      // Phase 1110 — the stringified boolean again, one level further in. The
      // `autoplay` fixture above refuses the same shape on a slot inside a case
      // payload; this one refuses it on a slot inside a LIST ELEMENT, which is
      // the position a host that decoded arrays with a looser walker than its
      // records would get wrong. A truthiness rule here would make two hosts
      // disagree about which caption track opens, which is a difference the
      // reader sees on the first frame.
      { Id = "reject-media-track-default-nonbool"
        Json =
          """{"id":"m","kind":{"$type":"Media","kind":{"$type":"Video"},"label":"Studio walkthrough","src":{"$type":"Static","value":"/walkthrough.mp4"},"tracks":[{"default":"true","kind":"Captions","label":"English captions","src":{"$type":"Static","value":"/walkthrough.en.vtt"},"srcLang":"en"}]}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.tracks[0].default"
        IsOp = false
        Description =
          "a track `default` of `\"true\"` — the stringified boolean, refused rather than coerced. Two hosts ruling differently on truthiness would disagree about which caption track opens, which is a difference the reader meets on the first frame" }
      // Phase 1111 — the frame a11y floor, expressed where a decoder can
      // enforce it. `title` is REQUIRED on `Embed` for the reason `label` is on
      // `Media`, one kind over: a frame is a focus container a reader tabs into,
      // so it is never decorative, and a document that omits the title describes
      // a browsing context a screen-reader user is told exists and cannot
      // identify. There is no honest value to default TO — an invented title is
      // a claim about somebody else's document.
      { Id = "reject-embed-missing-title"
        Json =
          """{"id":"e","kind":{"$type":"Embed","src":{"$type":"Static","value":"https://player.example/embed/harbour"}}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.kind.title"
        IsOp = false
        Description =
          "Embed with no `title` — refused. The accessible name is mandatory because a browsing context has no decorative case, and there is no value to default to that would not be a fabricated description of someone else's page" }
      // Phase 1111 — an unrecognised sandbox relaxation. A BARE enum inside a
      // LIST, so the path carries the array index and NO `.$type` suffix (§6,
      // and the Phase 1073 ruling): `permissions` holds plain strings, and a
      // suffix here would name a JSON member the document does not contain.
      //
      // The refusal is doing real work rather than merely being consistent.
      // `"allow-top-navigation"` is the HTML token an author reaches for from
      // memory, and it is one this vocabulary deliberately does not admit — a
      // decoder that silently dropped it would turn a document asking for
      // top-level navigation into a document asking for less, which reads as
      // success, and a decoder that guessed would grant it.
      { Id = "reject-embed-unknown-permission"
        Json =
          """{"id":"e","kind":{"$type":"Embed","permissions":["allow-top-navigation"],"src":{"$type":"Static","value":"https://player.example/embed/harbour"},"title":"Harbour restoration, part two"}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.kind.permissions[0]"
        IsOp = false
        Description =
          "EmbedPermission 'allow-top-navigation' — refused. The set is closed at four, and top-level navigation is excluded by design rather than reserved; the path carries the array index and no `.$type`, because the slot holds bare enum strings" }
      // Phase 1111 — a permission element of the WRONG JSON KIND, the position a
      // host decoding array elements with a looser walker than its records would
      // get wrong. `true` is refused rather than read as a present-and-enabled
      // flag: a host that coerced it would have to decide WHICH permission a
      // bare `true` names, and every answer to that is a host granting a sandbox
      // relaxation the document never spelled.
      // Phase 1112 — the node-level tooltip trait at the WRONG JSON KIND, and
      // the position where that matters most. `Literal` is `TextSource`'s
      // TRANSPARENT case, so a bare STRING here is the canonical encoding of an
      // ordinary authored hint — which is exactly why a bare NUMBER has to be
      // refused rather than coerced. The two are one character apart in a
      // document and one `toString` apart in a lenient decoder, and a host that
      // stringified would turn `42` into the hint `"42"`: a hint whose text is
      // an accident of a JSON type, which nothing downstream reports and which
      // reaches the reader as a confident wrong answer.
      //
      // The path is `$.tooltip` with no `.$type` suffix: the refusal is that the
      // POSITION is neither an object nor the accepted shorthand, so naming a
      // member the document does not contain would be a wrong claim about where
      // the defect is (the Phase 1073 ruling).
      { Id = "reject-tooltip-nonstring"
        Json = """{"id":"t","kind":{"$type":"Markdown","text":"Body"},"tooltip":42}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.tooltip"
        IsOp = false
        Description =
          "a node `tooltip` of `42` — refused rather than stringified. A bare STRING at this slot is the canonical encoding of a literal hint (TextSource's transparent case), so a number is one lenient `toString` away from minting hint text out of a JSON type — which no downstream check could ever catch" }
      { Id = "reject-embed-permission-nonstring"
        Json =
          """{"id":"e","kind":{"$type":"Embed","permissions":[true],"src":{"$type":"Static","value":"https://player.example/embed/harbour"},"title":"Harbour restoration, part two"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.permissions[0]"
        IsOp = false
        Description =
          "an Embed permission of `true` — refused rather than coerced. A host that read a bare boolean as a granted permission would have to invent which one it names, and every answer is a relaxation the document never spelled" }
      { Id = "reject-combobox-allowfreetext-nonbool"
        Json =
          """{"id":"f","kind":{"$type":"Form","fields":[{"id":"country","kind":{"$type":"Combobox","allowFreeText":"yes","options":{"$type":"Static","value":[{"label":"France","value":"fra"}]}},"label":"Country","required":false}],"onSubmit":"<closure>","submitLabel":"Save"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.fields[0].kind.allowFreeText"
        IsOp = false
        Description =
          "a Combobox `allowFreeText` of `\"yes\"` — refused rather than coerced. The slot decides whether values OUTSIDE the option set are admitted, so a lenient truthiness read is a constraint silently relaxed by a string: `\"yes\"`, `\"no\"` and `\"false\"` are all non-empty, and a host that coerced them would widen the field on two of the three. It also omits at `false`, so absence already spells the safe answer and a wrong-typed present value can only mean the emitter meant something it did not say" }
      { Id = "reject-upload-droptarget-nonbool"
        Json =
          """{"id":"up","kind":{"$type":"FileUpload","accept":[".csv"],"dropTarget":"true","label":"Upload","multiple":false,"onSelect":"<closure>"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.dropTarget"
        IsOp = false
        Description =
          "a FileUpload `dropTarget` of `\"true\"` — refused rather than coerced. The slot decides whether a whole INGRESS ROUTE exists, and it omits at `false`, so absence already spells the plain picker: a present wrong-typed value can only mean the emitter meant something it did not say. A lenient truthiness read would open a drop target on `\"no\"` and `\"false\"` alike, and the reader would find out by dropping a file into a control that never listened for it" }
      { Id = "reject-upload-acceptpaste-nonbool"
        Json =
          """{"id":"up","kind":{"$type":"FileUpload","accept":["image/*"],"acceptPaste":1,"label":"Upload","multiple":false,"onSelect":"<closure>"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.acceptPaste"
        IsOp = false
        Description =
          "a FileUpload `acceptPaste` of `1` — the second gesture slot, refused on the same reasoning and vectored separately because it is a separate decoder arm. A number is the shape a truthiness coercion is likeliest to admit, which is why this vector carries one where its `dropTarget` twin carries a string" }
      // Phase 1119 — the two `ModalSpec.modality` arms, vectored separately
      // because they ARE two arms: an unknown STRING is a discriminator outside
      // the closed pair, and a non-string is a value of the wrong shape. Both
      // matter more here than the usual enum, because absence spells `Modal` —
      // so a decoder that fell back on a value it could not read would silently
      // turn an intended popover into a page-blocking dialog with a scrim and an
      // inertness claim, which is the worst available answer.
      { Id = "reject-modal-modality-unknown"
        Json =
          """{"id":"m","kind":{"$type":"Modal","children":[],"dismissable":true,"modality":"Sheet","open":{"$type":"Static","value":false}}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.kind.modality"
        IsOp = false
        Description =
          "a Modal `modality` of `\"Sheet\"` — outside the closed pair `Modal | Popover`. A sheet, a drawer and a menu are all PRESENTATIONS of one of the two modalities rather than a third one, so the answer to this document is not to widen the enum but to say which of the two it meant. The path is the bare slot, per §6 and the Phase 1073 ruling: a bare enum carries no discriminator on the wire, so there is no `.$type` to name" }
      { Id = "reject-modal-modality-nonstring"
        Json =
          """{"id":"m","kind":{"$type":"Modal","children":[],"dismissable":true,"modality":3,"open":{"$type":"Static","value":false}}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.modality"
        IsOp = false
        Description =
          "a Modal `modality` of `3` — a separate decoder arm from the unknown-string vector above, and vectored separately for that reason. An ordinal is the shape a host generating from an enum's index is likeliest to emit, and reading one would make the wire's meaning depend on a declaration order the wire never carries" }
      // Phase 1120 — the tree row's two required slots, and both refusals are
      // the a11y floor rather than pedantry about shape.
      //
      // A row with no `label` is a row with no name: the tree renders it, a
      // reader arrows onto it, and assistive technology announces its level and
      // its position among its siblings and nothing else. There is no honest
      // value to default to — an invented label is a claim about content
      // nobody wrote.
      //
      // A row with no `id` is worse, because it is not merely unnamed but
      // UNADDRESSABLE: both State keys name rows by id, so a row without one
      // can never be expanded, selected, or restored after a reload. The
      // decoder refuses rather than synthesising a positional id, because a
      // synthesised id would change the moment a sibling is inserted, and the
      // reader's open branches would silently move.
      { Id = "reject-tree-item-missing-label"
        Json = """{"id":"t","kind":{"$type":"Tree","items":[{"id":"a"}]}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.kind.items[0].label"
        IsOp = false
        Description =
          "a Tree row with no `label` — refused. A row's label is the only thing a reader walking the hierarchy has, and there is no value to default to that would not be an invented description of somebody's content" }
      { Id = "reject-tree-item-missing-id"
        Json = """{"id":"t","kind":{"$type":"Tree","items":[{"label":"Goods"}]}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.kind.items[0].id"
        IsOp = false
        Description =
          "a Tree row with no `id` — refused. Both State keys address rows BY id, so an id-less row can never be expanded, selected or restored; a synthesised positional id would move the reader's open branches the moment a sibling was inserted" }
      // The same refusal one level DOWN, which is not the same test: a host
      // whose child walker is looser than its root walker passes the two above
      // and accepts this. The path carries the full `children[…]` chain, which
      // is what an author needs to find the row in a hierarchy they cannot see.
      { Id = "reject-tree-nested-item-missing-id"
        Json =
          """{"id":"t","kind":{"$type":"Tree","items":[{"children":[{"label":"Cocoa"}],"id":"goods","label":"Goods"}]}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.kind.items[0].children[0].id"
        IsOp = false
        Description =
          "a NESTED Tree row with no `id` — refused, and the path names the row inside the hierarchy. A host whose child walker is looser than its root walker accepts this while passing every top-level case" }
      { Id = "reject-rating-max-zero"
        Json =
          """{"id":"f","kind":{"$type":"Form","fields":[{"id":"score","kind":{"$type":"Rating","max":0},"label":"Score","required":false}],"onSubmit":"<closure>","submitLabel":"Save"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.fields[0].kind.max"
        IsOp = false
        Description =
          "a Rating `max` of `0` — refused rather than clamped. This is not a control with a bad number in it: a scale with no positions has nothing to draw, no `aria-valuemax` to announce and no keystroke that could change anything, so the document names a control that cannot exist. Note the asymmetry this vector pins: the SCALE is refused here and the VALUE is not, because a bound value is invisible to a decoder and a rule enforced only on literals would be two rules wearing one name" }
      { Id = "reject-color-value-not-hex"
        Json =
          """{"id":"f","kind":{"$type":"Form","fields":[{"id":"brand","kind":{"$type":"Color","value":{"$type":"Static","value":"rebeccapurple"}},"label":"Brand","required":false}],"onSubmit":"<closure>","submitLabel":"Save"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.fields[0].kind.value"
        IsOp = false
        Description =
          "a Color `value` of `\"rebeccapurple\"` — refused rather than coerced. `#rrggbb` is the one shape a native colour input can hold, so a literal outside it names a colour this control could never carry, and a host that narrowed it would show a colour the document did not choose. Only the STATIC case is judged, because it is the only text a decoder has: a bound value is re-checked by the pre-emit validator for an author and by the server-side submission floor for a client, which is one rule checked wherever the value becomes visible" }
      // Phase 1116 — an unrecognised capture device. The set is closed at the
      // two devices a file picker can stand in front of, and the near miss an
      // emitter will actually write is a THIRD one: a screen. It is refused
      // rather than reserved, because display capture is not a wider spelling of
      // this member — it is a standing grant over everything the reader has open,
      // ruled Host chrome on trust grounds, so a decoder that guessed at it would
      // be inventing the one capability this vocabulary deliberately does not
      // carry. The path is the bare slot with no `.$type` suffix, per §6 and the
      // Phase 1073 ruling: a bare enum carries no discriminator on the wire.
      { Id = "reject-unknown-capture-source"
        Json =
          """{"id":"up","kind":{"$type":"FileUpload","accept":["image/*"],"capture":"Screen","label":"Capture","multiple":false,"onSelect":"<closure>"}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.kind.capture"
        IsOp = false
        Description =
          "a FileUpload `capture` of `\"Screen\"` — outside the closed pair `Camera | Microphone`. The HTML capture attribute cannot ask for a display at all, and a screen capture is a standing grant reaching every window the reader has open rather than a third device behind the same picker permission, so it is refused exactly like a name nobody has proposed: a later admission would be an ADDITION, never a re-meaning of shipped bytes" }
      { Id = "reject-unknown-binding"
        Json =
          """{"id":"x","kind":{"$type":"Metric","label":{"$type":"Literal","text":"L"},"format":{"$type":"None"},"tone":"Default","weight":"Standard","emphasis":"Normal","value":{"$type":"Bogus"}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.kind.value.$type"
        IsOp = false
        Description = "Binding $type 'Bogus'" }
      { Id = "reject-unknown-textsource"
        Json =
          """{"id":"x","kind":{"$type":"Markdown","text":{"$type":"TemplateString","text":"x"}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.kind.text.$type"
        IsOp = false
        Description = "TextSource $type 'TemplateString'" }
      { Id = "reject-unknown-mount-direction"
        Json =
          """{"id":"x","kind":{"$type":"Mount","scopeId":"g","channel":{"direction":"Sideways"},"capabilities":[],"onBubble":"<closure>"},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.kind.channel.direction"
        IsOp = false
        Description = "Mount ChannelDirection value 'Sideways' (§4o)" }
      { Id = "reject-unknown-drawing-shape"
        Json =
          """{"id":"x","kind":{"$type":"Drawing","shapes":[{"$type":"Blob","style":{}}],"style":{},"viewBox":{"height":100,"minX":0,"minY":0,"width":100}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.kind.shapes[0].$type"
        IsOp = false
        Description = "Drawing Shape $type 'Blob' — the closed-shape default-deny (Phase 524)" }
      { Id = "reject-unknown-drawing-curve-command"
        Json =
          """{"id":"x","kind":{"$type":"Drawing","shapes":[{"$type":"Curve","commands":[{"$type":"ArcTo"}],"style":{}}],"style":{},"viewBox":{"height":100,"minX":0,"minY":0,"width":100}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.kind.shapes[0].commands[0].$type"
        IsOp = false
        Description = "Drawing CurveCommand $type 'ArcTo' — typed command list default-deny (Phase 524)" }
      { Id = "reject-unknown-link-protection"
        Json =
          """{"id":"x","kind":{"$type":"Link","download":false,"href":{"$type":"Static","value":"mailto:a@example.com"},"label":{"$type":"Literal","text":"Email"},"protection":"rot13"},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.kind.protection"
        IsOp = false
        Description = "LinkProtection value 'rot13' — the closed protection DU default-denies (Phase 812)" }
      // fuaran#1085 — `reject-transform-source-empty-wrapper` was RETIRED here.
      // fuaran#815 refused a Transform source spelled `{"$type":"State","key":k}`
      // because a State wrapper carrying neither `defaultValue` nor `value` had
      // nothing to unwrap, which was correct while nothing else could fill the
      // slot. Under fuaran#1075's seeding rule a SIBLING reader's declaration
      // fills it, so the refusal was rejecting the most direct spelling of "I
      // read this key and carry no data of my own" — the spelling FUARAN106's
      // own remedy text tells an author to write. The shape now decodes to a
      // live source over the empty initial snapshot, as Selection / Query
      // already did; `TransformSourceLeniencyTests` pins it as an ACCEPT.
      { Id = "reject-unknown-static-sort-direction"
        Json =
          """{"id":"x","kind":{"$type":"DataGrid","columns":[],"source":{"$type":"Static","value":[]},"staticRows":{"defaultSort":{"column":0,"direction":"sideways"},"headers":["A"],"rows":[["1"],["2"]]}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.kind.staticRows.defaultSort.direction"
        IsOp = false
        Description =
          "staticRows defaultSort direction 'sideways' — the closed asc|desc pair default-denies (Phase 801)" }

      // ─── Phase 818 — SetState value XOR valueFrom ────────────────────
      { Id = "reject-setstate-value-and-valuefrom"
        Json =
          """{"id":"b-both","kind":{"$type":"Button","label":{"$type":"Literal","text":"Go"},"onClick":{"$type":"SetState","key":"chosen","value":"literal","valueFrom":{"$type":"State","key":"other"}},"variant":"Primary"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.onClick.valueFrom"
        IsOp = false
        Description =
          "SetState carrying BOTH 'value' and 'valueFrom' — exactly one is allowed; the didactic names both fields and how each is used (Phase 818)" }

      // ─── Phase 1124 — Print carries no members ────────────────
      // The one Action arm that is STRICT about unrecognised members, and the
      // vector is what makes that strictness a shared obligation rather than
      // one host's habit. Everywhere else a member the decoder does not know is
      // one it has not learned yet; here there is nothing to learn, because the
      // ruling is that a document may say *print now* and nothing about how.
      // `pageRange` is the member an emitter is most likely to invent, which is
      // why it is the one the vector carries: dropping it silently would leave
      // the emitter believing it had constrained a printing it had not.
      { Id = "reject-action-print-with-payload"
        Json =
          """{"id":"b-print","kind":{"$type":"Button","label":{"$type":"Literal","text":"Print"},"onClick":{"$type":"Print","pageRange":"1-3"},"variant":"Primary"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.onClick.pageRange"
        IsOp = false
        Description =
          "Print carrying a member — the payload-free action takes none; page range, size, margins and copies are the host's page setup and the reader's dialogue, so a member here is refused rather than dropped (Phase 1124)" }

      // ─── WRONG_NODE_KIND ─────────────────────────────────────────────
      { Id = "reject-wrongnodekind-widget"
        Json =
          """{"id":"x","kind":{"$type":"Widget"},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_NODE_KIND
        ExpectedPath = "$.kind.$type"
        IsOp = false
        Description = "Top-level kind 'Widget' is not a recognised node kind" }
      { Id = "reject-wrongnodekind-sparkler"
        Json =
          """{"id":"x","kind":{"$type":"Sparkler"},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_NODE_KIND
        ExpectedPath = "$.kind.$type"
        IsOp = false
        Description =
          "Plausible-but-invalid kind 'Sparkler' — on the flat wire an unknown primitive is WRONG_NODE_KIND, not a nested DisplayKind miss" }

      // ─── EMPTY_NODE_ID ───────────────────────────────────────────────
      { Id = "reject-emptynodeid"
        Json =
          """{"id":"","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.EMPTY_NODE_ID
        ExpectedPath = "$.id"
        IsOp = false
        Description = "Empty top-level id" }

      // ─── TreeOp rejects ──────────────────────────────────────────────
      { Id = "reject-op-unknown-yeet"
        Json = """{"$type":"Yeet","target":"x"}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.$type"
        IsOp = true
        Description = "Unknown op discriminator 'Yeet'" }
      { Id = "reject-op-updateprop-missing-path"
        Json = """{"$type":"UpdateProp","target":"x","value":1}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.path"
        IsOp = true
        Description = "UpdateProp missing path" }
      { Id = "reject-op-insertchild-missing-parentid"
        Json =
          """{"$type":"InsertChild","child":{"id":"new","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.parentId"
        IsOp = true
        Description = "InsertChild missing parentId" }
      { Id = "reject-op-reorderchildren-wrongtype"
        Json = """{"$type":"ReorderChildren","parentId":"x","newOrder":"abc"}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.newOrder"
        IsOp = true
        Description = "ReorderChildren newOrder is string not array" }
      { Id = "reject-op-missing-type"
        Json = """{"target":"x"}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.$type"
        IsOp = true
        Description = "Op missing $type entirely" }
      { Id = "reject-op-insertchild-empty-childid"
        Json =
          """{"$type":"InsertChild","parentId":"x","child":{"id":"","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}}"""
        ExpectedCode = DecodeErrorCode.EMPTY_NODE_ID
        ExpectedPath = "$.child.id"
        IsOp = true
        Description = "InsertChild child carries empty id" }

      // ─── the RETIRED positional slot (Phase 687) ──────────────────────────
      //
      // Phase 681 removed `position` / `newPosition`; 681–686 left every
      // decoder accepting and ignoring it so the hosts could adopt
      // independently. These two fixtures are the window's close: the field is
      // now refused BY NAME, which is the only way to close it — the tolerance
      // was silence (these decoders read named fields and ignore the rest), so
      // there was never a read to delete.
      //
      // Both payloads are otherwise WELL-FORMED, deliberately. A fixture that
      // also lacked a required field would pass whether the host checked the
      // retired name first or merely happened to fail earlier, and would
      // certify nothing about the refusal.
      { Id = "reject-op-insertchild-retired-position"
        Json =
          """{"$type":"InsertChild","parentId":"x","position":0,"child":{"id":"new","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.position"
        IsOp = true
        Description = "InsertChild carries the retired 'position' — the migration window is closed (Phase 687)" }
      { Id = "reject-op-movenode-retired-newposition"
        Json = """{"$type":"MoveNode","newParentId":"q","newPosition":2,"target":"n"}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.newPosition"
        IsOp = true
        Description = "MoveNode carries the retired 'newPosition' — the migration window is closed (Phase 687)" }

      // ─── null in structured JVal positions (rule 12: no null on the wire) ──
      //
      // The wire model has no null — a JSON null in any structured payload
      // position rejects as WRONG_TYPE at the null's exact path. Before these
      // fixtures the rule lived only in per-host unit tests, so a host could
      // silently accept nulls while passing full corpus certification.
      { Id = "reject-null-custom-prop"
        Json =
          """{"id":"c1","kind":{"$type":"Custom","componentId":"trend-card","moduleId":"analytics","props":{"k":null}}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.props.k"
        IsOp = false
        Description = "null Custom prop value (structured JVal position)" }
      { Id = "reject-null-updateprop-value"
        Json = """{"$type":"UpdateProp","path":"Text","target":"m","value":null}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.value"
        IsOp = true
        Description = "null UpdateProp value (structured JVal position)" }
      { Id = "reject-null-action-setstate-value"
        Json =
          """{"id":"b1","kind":{"$type":"Button","label":{"$type":"Literal","text":"Go"},"onClick":{"$type":"SetState","key":"open","value":null},"variant":"Primary"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.onClick.value"
        IsOp = false
        Description = "null Action.SetState value (structured JVal position)" }
      { Id = "reject-null-action-notify-payload"
        Json =
          """{"id":"b2","kind":{"$type":"Button","label":{"$type":"Literal","text":"Go"},"onClick":{"$type":"Notify","channel":"toast","payload":null},"variant":"Primary"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.onClick.payload"
        IsOp = false
        Description = "null Action.Notify payload (structured JVal position)" }
      { Id = "reject-null-action-aitool-args"
        Json =
          """{"id":"b3","kind":{"$type":"Button","label":{"$type":"Literal","text":"Go"},"onClick":{"$type":"AiTool","args":null,"toolName":"fuaran.getNodeState"},"variant":"Primary"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.onClick.args"
        IsOp = false
        Description = "null Action.AiTool args (structured JVal position)" }
      { Id = "reject-null-i18n-arg"
        Json =
          """{"id":"m1","kind":{"$type":"Markdown","text":{"$type":"I18n","args":{"name":null},"key":"greeting"}}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.text.args.name"
        IsOp = false
        Description = "null I18n arg value (structured JVal position)" }

      // ─── DateRange ordering (Phase 725) ─────────────────────────────────
      //
      // A LITERAL date-range pair is ordered: `from <= to`. Same-variant
      // ISO-8601 strings sort lexicographically in chronological order, so the
      // check is an ordinal compare — no date parsing, no locale, total for
      // every variant. Didactic by design: the message names the rule and the
      // fix, because a reversed pair is the natural first mistake for a control
      // whose two ends look interchangeable.
      { Id = "reject-daterange-unordered"
        Json =
          """{"id":"f1","kind":{"$type":"Form","fields":[{"id":"stay","kind":{"$type":"DateRange","value":{"from":"2026-03-08","to":"2026-03-01"},"variant":"Date"},"label":"Stay","required":false}],"onSubmit":{"$type":"Dispatch"},"submitLabel":"Book"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.fields[0].kind.value"
        IsOp = false
        Description = "DateRange literal pair with start after end — the ordered-pair rule (Phase 725)" }

      // ─── TonedPill tone-map values (Phase 750) ───────────────────────────
      //
      // The declarative pill's `map` VALUES are `ToneVariant`s, and a tone name
      // is the one part of the shape an author has to know rather than infer:
      // "Urgent" / "Red" / "Error" are all plausible-sounding and all wrong. The
      // reject is deliberately routed through the same `decodeTone` every other
      // tone position uses, so the message enumerates the seven legal names — an
      // author who guesses gets the answer, not just a refusal.
      { Id = "reject-tonedpill-unknown-tone"
        Json =
          """{"id":"g1","kind":{"$type":"DataGrid","columns":[{"field":"status","kind":{"$type":"TonedPill","field":"status","map":{"Delayed":"Urgent"}},"label":"Status"}],"rowKeyField":"status","source":{"$type":"Transform","pipeline":[],"source":{"columns":{"status":{"validity":[true],"values":["Delayed"]}},"schema":[{"name":"status","type":"string"}]}}}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.kind.columns[0].kind.map.Delayed"
        IsOp = false
        Description =
          "TonedPill tone-map value outside ToneVariant — the message names the seven legal tones (Phase 750)" }

      // ─── FieldRule well-formedness (Phase 864) ───────────────────────────
      //
      // Three refusals, and each is a relation BETWEEN slots rather than a
      // shape, which is why none of them is expressible in the IDL and all
      // three live in the policy decoder beside the DateRange `from <= to`
      // check above.
      //
      // (1) A rule with every slot absent. A rule that constrains nothing is a
      // defect and not a no-op: it decodes, validates and renders while
      // declaring nothing, so the author believes a constraint is in force and
      // the field accepts anything. `message` alone does not rescue it — the
      // message is the prose shown when some OTHER slot is unmet, so a
      // message-only rule is precisely the help-text failure this phase exists
      // to fix, wearing the new vocabulary's clothes.
      { Id = "reject-fieldrule-empty"
        Json =
          """{"id":"f1","kind":{"$type":"Form","fields":[{"id":"email","kind":{"$type":"Text"},"label":"Work email","required":true,"rule":{"message":"Must be a valid email"}}],"onSubmit":{"$type":"Dispatch"},"submitLabel":"Save"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.fields[0].rule"
        IsOp = false
        Description = "FieldRule declaring no constraint — a message alone is not a rule (Phase 864)" }

      // (2) An inverted length pair. The DateRange ordered-pair rule above,
      // applied to a length bound: `minLength` over `maxLength` admits no value
      // at all, so the field can never be submitted and the form is dead on
      // arrival. Same didactic posture — the message names the two numbers and
      // says what the inversion costs.
      { Id = "reject-fieldrule-length-unordered"
        Json =
          """{"id":"f1","kind":{"$type":"Form","fields":[{"id":"username","kind":{"$type":"Text"},"label":"Username","required":true,"rule":{"minLength":24,"maxLength":3}}],"onSubmit":{"$type":"Dispatch"},"submitLabel":"Save"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.fields[0].rule"
        IsOp = false
        Description = "FieldRule minLength above maxLength — the ordered-pair rule on a length pair (Phase 864)" }

      // (3) The near miss. Rule 2's tolerance of unknown keys is right for a
      // field a future profile may add and wrong for a near miss of one that
      // exists: the tree decodes and renders while the constraint does nothing,
      // and silence is worse here than anywhere else in the vocabulary, because
      // the failure this phase exists to fix is authors putting the rule
      // somewhere a host cannot act on. `validation` / `constraints` /
      // `validate` are refused by name and pointed at `rule`.
      { Id = "reject-formfield-near-miss-validation"
        Json =
          """{"id":"f1","kind":{"$type":"Form","fields":[{"id":"email","kind":{"$type":"Text"},"label":"Work email","required":true,"validation":{"format":"email"}}],"onSubmit":{"$type":"Dispatch"},"submitLabel":"Save"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.fields[0].validation"
        IsOp = false
        Description = "'validation' near-miss on a FormField — the canonical key is 'rule' (Phase 864)" }

      // ─── Accessibility trait (Phase 955) ─────────────────────────────────
      //
      // Enumerated against the trait's DECLARATION in WIRE_FORMAT §3.1 — one
      // vector per shape the six slots can be malformed in — rather than
      // against any host's current behaviour, which is the whole reason the
      // family exists: two hosts read `label` as a bare string only, and their
      // own fixtures authored that shape, so nothing was red anywhere.
      //
      // The pair that carries the most weight is the last two. §3.1's 2026-08-25
      // ruling makes the trait's `Binding` slots ordinary `Binding` slots, so
      // the §3.6 bare-scalar coercion applies — `"hidden": true` is legal
      // shorthand for `{"$type":"Static","value":true}`. That leniency is about
      // SHAPE and not about TYPE, and the distinction is invisible until
      // something pins it: `"hidden": "yes"` takes the same lenient arm and must
      // still be refused, because a `Binding<bool>` slot's Static parser is
      // `requireBool`. Without this vector a host could implement "any scalar
      // becomes Static" and pass every fixture in the corpus.

      // (1) The closed token set. `liveRegion` is one of exactly three strings;
      // `aria-live`'s real HTML vocabulary is the same three, so a near miss
      // here is an author reaching for a token that does not exist rather than a
      // typo, and UNKNOWN_DU_CASE (which names the legal set) is the didactic
      // refusal rather than a bare WRONG_TYPE.
      { Id = "reject-a11y-liveregion-unknown"
        Json =
          """{"id":"n1","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"accessibility":{"liveRegion":"urgent"}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.accessibility.liveRegion"
        IsOp = false
        Description =
          "accessibility.liveRegion outside the closed set — the message names polite | assertive | off (Phase 955)" }

      // (2) The same slot, wrong JSON kind. Distinct from (1) on purpose: a
      // non-string is not a near miss of a token, so it is WRONG_TYPE and not
      // UNKNOWN_DU_CASE, and a host that collapses the two loses the difference
      // between "no such token" and "not a token at all".
      { Id = "reject-a11y-liveregion-nonstring"
        Json =
          """{"id":"n1","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"accessibility":{"liveRegion":true}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.accessibility.liveRegion"
        IsOp = false
        Description = "accessibility.liveRegion not a JSON string (Phase 955)" }

      // ─── Phase 1472 — `style.direction`, on BOTH decoder arms ─────────
      //
      // The slot reaches a decoder through two independent paths: the node
      // envelope's `style`, and the `UpdateStyle` op's. They are separate arms
      // in every host, so a vector on one proves nothing about the other, and a
      // host that hardened the envelope and left the op lenient would pass a
      // one-armed corpus while accepting a jumbled document over the wire.
      // Each token failure is therefore pinned twice, once per arm.
      //
      // (1) The closed token set, node arm. `direction` is one of exactly three
      // lower-case strings; a near miss is an author reaching for a token that
      // does not exist (`"LTR"`, `"left"`, `"leftToRight"`), so UNKNOWN_DU_CASE
      // — which names the legal set — is the didactic refusal.
      //
      // Refused rather than coerced to the default, and that is the whole
      // point of the vector: a document that meant `rtl` and misspelled it
      // would otherwise render as reordered digits with nothing said anywhere,
      // which is precisely the failure the slot exists to prevent.
      { Id = "reject-style-direction-unknown"
        Json =
          """{"id":"n1","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"style":{"direction":"LTR"}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.style.direction"
        IsOp = false
        Description =
          "style.direction outside the closed set — the message names auto | ltr | rtl, and the near miss is the upper-case spelling (Phase 1472)" }

      // (2) The same slot, wrong JSON kind. Distinct from (1) on purpose: a
      // non-string is not a near miss of a token, so it is WRONG_TYPE and not
      // UNKNOWN_DU_CASE, and a host that collapses the two loses the difference
      // between "no such token" and "not a token at all".
      { Id = "reject-style-direction-nonstring"
        Json =
          """{"id":"n1","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"style":{"direction":1}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.style.direction"
        IsOp = false
        Description = "style.direction not a JSON string (Phase 1472)" }

      // (3) The closed token set, OP arm — the second decoder arm.
      { Id = "reject-op-updatestyle-direction-unknown"
        Json = """{"$type":"UpdateStyle","target":"n1","style":{"direction":"rtl-isolate"}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.style.direction"
        IsOp = true
        Description =
          "UpdateStyle style.direction outside the closed set — the op arm's twin of the node vector above (Phase 1472)" }

      // (4) Wrong JSON kind, OP arm.
      { Id = "reject-op-updatestyle-direction-nonstring"
        Json = """{"$type":"UpdateStyle","target":"n1","style":{"direction":true}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.style.direction"
        IsOp = true
        Description = "UpdateStyle style.direction not a JSON string (Phase 1472)" }

      // (3) `role` is the OPEN slot — any string decodes, as `AriaRole.Custom`
      // verbatim — so a non-string is the only way to malform it, and pinning
      // that is what stops a host from stringifying a number and inventing a
      // role the author never wrote.
      { Id = "reject-a11y-role-nonstring"
        Json =
          """{"id":"n1","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"accessibility":{"role":42}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.accessibility.role"
        IsOp = false
        Description =
          "accessibility.role not a JSON string — the slot is open to any role NAME, not to any value (Phase 955)" }

      // (4) The NodeId-reference slots. One vector covers the class: `labelledBy`
      // and `describedBy` are the same decoder at two keys.
      { Id = "reject-a11y-labelledby-nonstring"
        Json =
          """{"id":"n1","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"accessibility":{"labelledBy":["h1"]}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.accessibility.labelledBy"
        IsOp = false
        Description = "accessibility.labelledBy not a NodeId string (Phase 955)" }

      // (5) The trait itself. `accessibility` is an object or absent; a bare
      // string there is the shape an author reaches for when they think the slot
      // IS the label ("accessibility": "Home"), which is why it earns a vector of
      // its own rather than being left to the per-slot cases above.
      { Id = "reject-a11y-not-object"
        Json =
          """{"id":"n1","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"accessibility":"Home"}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.accessibility"
        IsOp = false
        Description = "accessibility is not an object — the trait is a record, not a bare label (Phase 955)" }

      // (6) The lenient-shape / strict-type boundary, per the preamble above.
      { Id = "reject-a11y-hidden-nonbool"
        Json =
          """{"id":"n1","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"accessibility":{"hidden":"yes"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.accessibility.hidden"
        IsOp = false
        Description =
          "accessibility.hidden bare scalar of the wrong type — §3.6 leniency is about SHAPE, not type (Phase 955)" }

      // ─── The `Accessibility` trait's NEAR MISSES (Phase 959) ──────────────
      //
      //  Phase 955's six vectors above pin what happens to a MALFORMED trait.
      //  These four pin what happens to a well-formed trait under the wrong KEY,
      //  which until now was: nothing at all. Rule 2 tolerates unknown keys, so
      //  every one of these decoded silently, and — this being the one trait with
      //  no visible output — an author had no way to discover it in either
      //  direction.
      //
      //  Sixteen names are refused; four vectors sample the three families plus
      //  the evidence, on the Phase 863 precedent (eight names, four fixtures).
      //  Two of the four are MEASURED rather than derived: `live` and `ariaLabel`
      //  are spellings language-tier emissions actually carry (`live` ×6 against
      //  `liveRegion`'s ×12 across 12,722 emissions — a third of every live-region
      //  declaration silently discarded).
      { Id = "reject-nearmiss-a11y-arialabel"
        Json =
          """{"id":"n1","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"accessibility":{"ariaLabel":"Notifications"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.accessibility.ariaLabel"
        IsOp = false
        Description =
          "accessibility 'ariaLabel' — the camelCase JSX prior for the accessible name; MEASURED in the emission corpora, and the whole declaration vanished (Phase 959)" }
      { Id = "reject-nearmiss-a11y-live"
        Json =
          """{"id":"n1","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"accessibility":{"live":"polite"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.accessibility.live"
        IsOp = false
        Description =
          "accessibility 'live' — the sharpest of the set: six measured emissions against liveRegion's twelve, and the HTML idiom it comes from also spells a BOOLEAN, so it is not a safe alias (Phase 959)" }
      { Id = "reject-nearmiss-a11y-aria-hidden"
        Json =
          """{"id":"n1","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"accessibility":{"aria-hidden":true}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.accessibility.aria-hidden"
        IsOp = false
        Description =
          "accessibility 'aria-hidden' — the ARIA ATTRIBUTE name where the wire wants the slot name 'hidden'; the projection slot two hosts once dropped entirely (Phase 959)" }
      { Id = "reject-nearmiss-a11y-liveregion-case"
        Json =
          """{"id":"n1","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"accessibility":{"liveregion":"assertive"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.accessibility.liveregion"
        IsOp = false
        Description =
          "accessibility 'liveregion' — the canonical slot name un-cased to the ARIA attribute spelling; the wire is camelCase (Phase 959)" }

      // ─── The NUMERIC `Binding<'T>` slots (Phase 1064) ─────────────────────
      //
      //  Phase 955/956 closed the SCALAR half of a general defect: five hosts
      //  named a typed `Binding` decoder at every string / bool slot and checked
      //  nothing, so `{"href": 7}` decoded. The fix landed at the mechanism, and
      //  the six a11y vectors above are what pins it. The NUMERIC positions the
      //  reference host types the same way — `decodeBindingFloat` at
      //  `Metric.value` / `Metric.trend` / `LabelValueRow.value` /
      //  `Progress.fraction` / `Drawing.strokeWidth` / `Drawing.opacity` / the
      //  Number + RangedNumber control values, and `decodeBindingInt` at
      //  `Tabs.activeIndex` / `Stepper.activeStep` — had NO fixture at all, so a
      //  host tightening them had nothing to be measured against.
      //
      //  The family below is enumerated against §7's accept sets, not against
      //  any host's current behaviour, and the enumeration is the point:
      //
      //    a FLOAT slot accepts  { JSON number } ∪ { "NaN", "Infinity", "-Infinity" }
      //    an INT   slot accepts { JSON number }                     (§7, truncating)
      //
      //  So "a string at a numeric slot rejects" is FALSE at a float slot and
      //  TRUE at an int slot, and a vector written the naive way would have
      //  contradicted the three §5/§7 sentinel ACCEPT fixtures Phase 1063 landed
      //  (`drawing-nonfinite-sentinels`, `spark-nonfinite-sentinel`,
      //  `metric-nonfinite-sentinel`) on the same corpus days earlier. Each
      //  vector below is therefore a MEMBER OF THE COMPLEMENT of one of those two
      //  sets — the wrong JSON kind, a string outside the sentinel set, a
      //  correctly-spelled sentinel at the slot class that has none, and a
      //  sentinel whose CASE is wrong.
      //
      //  Both `Binding` shapes are covered on purpose: the `Static` envelope and
      //  the §3.6 bare scalar reach the slot's parser by different arms, and a
      //  host that types one and not the other passes half a family.

      // (1) FLOAT — a string outside the sentinel set, behind the `Static`
      // envelope. The plainest statement of the boundary §7 draws: the accept
      // set is three named tokens, not "strings that might parse".
      { Id = "reject-binding-float-string"
        Json =
          """{"id":"n1","kind":{"$type":"Metric","format":{"$type":"None"},"label":{"$type":"Literal","text":"L"},"value":{"$type":"Static","value":"lots"}}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.value"
        IsOp = false
        Description =
          "Binding<float> Static payload is a non-sentinel string — §7's float accept set is JSON number plus exactly \"NaN\" / \"Infinity\" / \"-Infinity\" (Phase 1064)" }

      // (2) FLOAT — a sentinel spelled in the wrong CASE, arriving through the
      // §3.6 bare-scalar arm. The sharpest vector in the family: it is one
      // `ToLowerInvariant` away from a fixture that must ACCEPT, so a host that
      // case-folds the sentinel comparison passes (1) and fails here. It also
      // proves the bare-scalar coercion routes through the slot's own parser
      // rather than round-tripping the scalar untouched.
      { Id = "reject-binding-float-sentinel-case"
        Json = """{"id":"n1","kind":{"$type":"Progress","fraction":"nan"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.fraction"
        IsOp = false
        Description =
          "Binding<float> bare scalar is a MIS-CASED sentinel (\"nan\") — §7's three tokens are exact, and a case-folding host would accept a document no encoder emits (Phase 1064)" }

      // (3) FLOAT — the wrong JSON kind entirely. A bool is not coercible to a
      // number under any rule in §7, and it is the value a host's untyped
      // pass-through would carry straight into a numeric render slot.
      { Id = "reject-binding-float-bool"
        Json =
          """{"id":"n1","kind":{"$type":"Metric","format":{"$type":"None"},"label":{"$type":"Literal","text":"L"},"trend":{"$type":"Static","value":true},"value":{"$type":"Static","value":1.0}}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.trend"
        IsOp = false
        Description =
          "Binding<float> Static payload is a JSON boolean — a second float slot on the same spec, so a host guarding `value` and not `trend` is caught (Phase 1064)" }

      // (4) INT — the wrong JSON kind, at the slot whose §3.6 bare-scalar arm the
      // lenient profile explicitly cites (`activeIndex: 1`). The leniency admits
      // the SHAPE; the slot's `Binding<int>` still governs the value.
      { Id = "reject-binding-int-bool"
        Json =
          """{"id":"n1","kind":{"$type":"Tabs","activeIndex":{"$type":"Static","value":true},"children":[{"id":"m1","kind":{"$type":"Markdown","text":"x"}}]}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.activeIndex"
        IsOp = false
        Description =
          "Binding<int> Static payload is a JSON boolean — §7 gives an integer slot no non-numeric form (Phase 1064)" }

      // (5) INT — a CORRECTLY-spelled non-finite sentinel at an integer slot.
      // The vector that makes the two accept sets distinguishable rather than
      // merely stated: §7 confines the sentinels to a float slot, an integer slot
      // truncates a parsed number and has no representation for NaN at all, and
      // Phase 1063's published schema widened only the float slot for exactly
      // this reason. A host that implements "accept the three tokens wherever a
      // number is expected" passes every fixture in the corpus except this one.
      { Id = "reject-binding-int-sentinel-string"
        Json =
          """{"id":"n1","kind":{"$type":"Stepper","activeStep":{"$type":"Static","value":"NaN"},"children":[{"id":"m1","kind":{"$type":"Markdown","text":"x"}}]}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.activeStep"
        IsOp = false
        Description =
          "Binding<int> Static payload is a §7 non-finite sentinel — the sentinels are FLOAT-slot only; an integer slot has no non-finite form (Phase 1064)" }

      // ─── LIMIT_EXCEEDED — the §21 shape bounds ───────────────────────
      //
      // Each bound is pinned from BOTH sides: these are the PAST-the-bound
      // halves, and their at-the-bound twins are `Fixtures.storedNodes`
      // (`limit-node-depth-at-max`) and `reject-limit-json-depth-at-max`
      // below — a guard that only ever refuses is indistinguishable from a
      // decoder that refuses everything.
      //
      // Payloads are BUILT (`Fixtures.boxChain` / `Fixtures.batchChain`), not
      // stored: they were authored straight into the corpus until fuaran#1094,
      // and every `--emit-corpus` then deleted them, because the emitter
      // rewrites the payload directories wholesale. Declared here they are
      // regenerated like every other reject fixture.
      { Id = "reject-limit-node-depth"
        Json = Fixtures.boxChain (Fuaran.UI.WireLimits.MaxDepth + 1)
        ExpectedCode = DecodeErrorCode.LIMIT_EXCEEDED
        ExpectedPath = "$"
        IsOp = false
        Description =
          "§21 max node depth — one level past the limit (25). Refused with LIMIT_EXCEEDED, never INVALID_JSON (rule 2) and never a throw (rule 3)" }
      { Id = "reject-limit-op-depth"
        Json = Fixtures.batchChain Fuaran.UI.WireLimits.MaxDepth
        ExpectedCode = DecodeErrorCode.LIMIT_EXCEEDED
        ExpectedPath = "$"
        IsOp = true
        Description =
          "§21.5 the op axis — 25 nested TreeOp.Batch. Counted separately from the node axis; the syntactic bound only LOOKS like cover for it" }
      { Id = "reject-limit-tree-item-depth"
        Json = Fixtures.treeItemChain (Fuaran.UI.WireLimits.MaxDepth + 1)
        ExpectedCode = DecodeErrorCode.LIMIT_EXCEEDED
        ExpectedPath =
          "$.kind.items[0].children[0].children[0].children[0].children[0].children[0].children[0].children[0].children[0].children[0].children[0].children[0].children[0].children[0].children[0].children[0].children[0].children[0].children[0].children[0].children[0].children[0].children[0].children[0]"
        IsOp = false
        Description =
          "§21.5 the tree-item axis — 25 nested TreeItem rows inside ONE node. Counted separately from the node axis, which cannot see it at all (the whole hierarchy is one node) and from the syntactic bound, which at ~50 levels is nowhere near reached. The `TreeOp.Batch` lesson on a third axis" }
      { Id = "reject-limit-json-depth"
        Json =
          String.replicate (Fuaran.UI.WireLimits.MaxJsonDepth + 1) "["
          + String.replicate (Fuaran.UI.WireLimits.MaxJsonDepth + 1) "]"
        ExpectedCode = DecodeErrorCode.LIMIT_EXCEEDED
        ExpectedPath = "$"
        IsOp = false
        Description =
          "§21 max JSON depth — 257 levels of bare array nesting, ONE past the limit. Well-formed and merely too deep, so LIMIT_EXCEEDED rather than INVALID_JSON (rule 2). Pins the boundary exactly; its at-the-limit twin below pins the other side" }
      // ─── Phase 1473 — the print-break flags, on BOTH decoder arms ─────
      //
      // The four declarations reach a decoder through two INDEPENDENT arms —
      // `Box`'s spec and `DataGrid`'s — and those are separate branches in
      // every host, so a vector on one proves nothing about the other. A host
      // that hardened the container arm and left the grid arm lenient would
      // pass a one-armed corpus while coercing a wrong-typed flag on the very
      // kind whose rows the flag is about. Both arms are therefore pinned, and
      // both flags on each arm, because each is read by its own `requireBool`
      // call and one of the four could be written to coerce without the other
      // three noticing.
      //
      // WRONG_TYPE and not UNKNOWN_DU_CASE: these are booleans, not tokens, so
      // a non-boolean is not a near miss of anything — there is no legal set to
      // name back at the author.
      //
      // Refused rather than coerced, and that is the whole point of the
      // vectors. `"true"` is the shape a stringly-typed emitter produces, and a
      // decoder that read it as `true` — or, worse, silently as the `false`
      // default — would leave the document's declaration and the rendering
      // disagreeing with nothing said anywhere. Under rule 1 the omit-at-false
      // convention makes ABSENT the only spelling of "not declared"; a present
      // key of the wrong kind is a defect, not a default.
      { Id = "reject-wrongtype-box-keep-together"
        Json =
          """{"id":"x","kind":{"$type":"Box","children":[],"keepTogether":"true","layout":{"$type":"Auto"},"role":"Group"},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.keepTogether"
        IsOp = false
        Description =
          "Box keepTogether is the STRING \"true\", not a boolean — the container arm of the print-break pair. Refused, never coerced: absence is the only spelling of \"not declared\" (Phase 1473)" }
      { Id = "reject-wrongtype-box-break-before"
        Json =
          """{"id":"x","kind":{"$type":"Box","breakBefore":1,"children":[],"layout":{"$type":"Auto"},"role":"Group"},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.breakBefore"
        IsOp = false
        Description =
          "Box breakBefore is the NUMBER 1, not a boolean — the truthy-integer spelling a JSON emitter reaches for, refused for the same reason as its sibling (Phase 1473)" }
      { Id = "reject-wrongtype-grid-keep-rows-together"
        Json =
          """{"id":"x","kind":{"$type":"DataGrid","columns":[],"keepRowsTogether":"yes","source":{"$type":"Static","value":[]}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.keepRowsTogether"
        IsOp = false
        Description =
          "DataGrid keepRowsTogether is a string — the GRID arm of the print-break pair, a decoder branch of its own that a container-arm vector says nothing about (Phase 1473)" }
      { Id = "reject-wrongtype-grid-repeat-header"
        Json =
          """{"id":"x","kind":{"$type":"DataGrid","columns":[],"repeatHeader":{},"source":{"$type":"Static","value":[]}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.repeatHeader"
        IsOp = false
        Description =
          "DataGrid repeatHeader is an object, not a boolean — the fourth flag, read by its own call and so pinned on its own (Phase 1473)" }
      // ─── Phase 1125 — the export flag, on BOTH decoder arms ───────────
      //
      // Two vectors and not one, for the reason the print-break block above
      // records at four: `exportable` is read by the GENERATED grid decoder and,
      // independently, by the policy decoder's own `decodeGridSpec`. Those are
      // separate branches, so a vector that only ever reaches one of them proves
      // nothing about the other — and it is the policy arm a constrained host
      // actually runs.
      //
      // WRONG_TYPE and not UNKNOWN_DU_CASE: a boolean is not a token, so a
      // non-boolean is a near miss of nothing and there is no legal set to name
      // back at the author.
      //
      // Refused rather than coerced, and here the coercion would be worse than
      // usual. Read as the `false` default, a wrong-typed `exportable` silently
      // withdraws an affordance the document declared and the reader is simply
      // never offered their data; read as `true`, a document that said nothing
      // about export grows a control that puts a file on the reader's disk.
      // Under rule 1 the omit-at-false convention makes ABSENT the only spelling
      // of "not declared", so a present key of the wrong kind is a defect and
      // never a default.
      { Id = "reject-wrongtype-grid-exportable"
        Json =
          """{"id":"x","kind":{"$type":"DataGrid","columns":[],"exportable":"true","source":{"$type":"Static","value":[]}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.exportable"
        IsOp = false
        Description =
          "DataGrid exportable is the STRING \"true\", not a boolean — the shape a stringly-typed emitter produces. Refused, never coerced: coerced to false it would silently withdraw a declared affordance, coerced to true it would grow one the document never asked for (Phase 1125)" }
      { Id = "reject-wrongtype-grid-exportable-number"
        Json =
          """{"id":"x","kind":{"$type":"DataGrid","columns":[],"exportable":1,"source":{"$type":"Static","value":[]}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.exportable"
        IsOp = false
        Description =
          "DataGrid exportable is the NUMBER 1 — the truthy-integer spelling, pinned separately because a decoder can reject one non-boolean shape and accept another (Phase 1125)" }
      // ─── Phase 1122 — the timed-advance interval's POSITIVE floor ─────
      //
      // Three vectors, and each pins a different way the slot can be got
      // wrong. `0` is the one that matters: it is what an emitter reaches for
      // to mean "off", and the language ALREADY has a spelling for off — an
      // absent key. Decoding it to a live zero-millisecond timer would be a
      // re-render loop; canonicalising it to `None` would make two document
      // shapes mean one thing and tell the emitter nothing about its
      // misunderstanding. So it is refused, exactly as `Masonry.cols` refuses
      // its own zero and for the same stated reason.
      //
      // WRONG_TYPE and not a code of its own: this is a number outside the
      // slot's value space, the class `Masonry.cols` and `srcSet`'s width
      // floor already occupy, and there is no legal SET to name back at the
      // author — only a bound, which the message carries.
      //
      // The fractional vector is here because `autoAdvanceMs` is an integer
      // slot and a JSON number is not: a decoder that read `1500.5` by
      // truncation would silently disagree with one that rounded, and two
      // hosts disagreeing about a document neither refused is the divergence
      // the corpus exists to prevent.
      { Id = "reject-switch-autoadvance-zero"
        Json =
          """{"id":"x","kind":{"$type":"Switch","autoAdvanceMs":0,"cases":[],"default":{"id":"d","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"d"}}},"stateKey":"slide"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.autoAdvanceMs"
        IsOp = false
        Description =
          "Switch autoAdvanceMs is 0 — refused, never canonicalised to absence. An absent key is already the only spelling of \"does not advance\", so accepting a zero would make two shapes mean one thing and hide the emitter's misreading of the slot (Phase 1122)" }
      { Id = "reject-switch-autoadvance-negative"
        Json =
          """{"id":"x","kind":{"$type":"Switch","autoAdvanceMs":-1000,"cases":[],"default":{"id":"d","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"d"}}},"stateKey":"slide"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.autoAdvanceMs"
        IsOp = false
        Description =
          "Switch autoAdvanceMs is negative — an interval names how long to wait, so a negative one describes a schedule no renderer can realise (Phase 1122)" }
      { Id = "reject-switch-autoadvance-fractional"
        Json =
          """{"id":"x","kind":{"$type":"Switch","autoAdvanceMs":1500.5,"cases":[],"default":{"id":"d","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"d"}}},"stateKey":"slide"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.autoAdvanceMs"
        IsOp = false
        Description =
          "Switch autoAdvanceMs is fractional — the slot is an integer count of milliseconds, and a decoder truncating where another rounded would leave two hosts disagreeing about a document neither refused (Phase 1122)" }
      // ─── Phase 1123 — the transfer pair is a KEY NAME, and only that ──
      //
      // Two vectors, one per side, because the two fields are read by two
      // independent decoder steps and a vector on one proves nothing about the
      // other — the asymmetry the pair exists for (an archive column declares
      // only `transferInKey`) is exactly the shape a single vector would leave
      // untested.
      //
      // A NUMBER is the wrong-shaped value worth pinning rather than an
      // arbitrary one: a state key looks like an identifier, and an emitter that
      // reaches for a column index or an ordinal to name "which list" is
      // reaching for a plausible thing. WRONG_TYPE and not a code of its own —
      // this is a value outside the slot's value space, the class every other
      // key-named slot on this kind already occupies, and there is no legal SET
      // to name back at the author.
      //
      // What is NOT rejected here, and cannot be: a key naming no counterpart.
      // Whether ANY OTHER grid declares the other end is a whole-tree question,
      // and a per-object codec sees one grid — so the dead-pairing case is
      // FUARAN129 at pre-emit rather than a reject vector, which is the same
      // split `pageSize` without `pageStateKey` already carries.
      { Id = "reject-wrongtype-grid-transfer-in-key"
        Json =
          """{"id":"x","kind":{"$type":"DataGrid","columns":[{"field":"card","format":{"$type":"None"},"kind":{"$type":"Text"},"label":"Card","width":{"$type":"Auto"}}],"rowKeyField":"card","source":{"$type":"State","key":"todo"},"transferInKey":2}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.transferInKey"
        IsOp = false
        Description =
          "DataGrid transferInKey is a number — the slot names the shared State key a transfer travels on, so it is a key name and never an ordinal; a grid identified by position could not be paired with by any other grid (Phase 1123)" }
      { Id = "reject-wrongtype-grid-transfer-out-key"
        Json =
          """{"id":"x","kind":{"$type":"DataGrid","columns":[{"field":"card","format":{"$type":"None"},"kind":{"$type":"Text"},"label":"Card","width":{"$type":"Auto"}}],"rowKeyField":"card","source":{"$type":"State","key":"todo"},"transferOutKey":true}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.transferOutKey"
        IsOp = false
        Description =
          "DataGrid transferOutKey is a boolean — the releasing side names a KEY, never declares a flag; there is deliberately no boolean spelling of \"this grid may release rows\", because a release with no key names no counterpart and would be an affordance with nowhere to go (Phase 1123)" }
      { Id = "reject-wrongtype-clipboard-payload"
        Json =
          """{"id":"x","kind":{"$type":"Button","label":"Copy","onClick":{"$type":"WriteToClipboard","text":42},"variant":"Primary"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.onClick.text"
        IsOp = false
        Description =
          "Action.WriteToClipboard's payload is a TextSource, so it is a string (the Literal shorthand) or a $type-tagged object — never a number. The refusal matters more here than at an ordinary text slot: the slot WIDENED in 1126 from a bare string, and a host that read the widening as \"anything goes\" would put a JSON literal on the reader's clipboard rather than refusing the document (Phase 1126)" }
      { Id = "reject-limit-json-depth-at-max"
        Json =
          String.replicate Fuaran.UI.WireLimits.MaxJsonDepth "["
          + String.replicate Fuaran.UI.WireLimits.MaxJsonDepth "]"
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$"
        IsOp = false
        Description =
          "§21 max JSON depth — 256 levels, EXACTLY at the limit. Not a valid node, so it must fail; the point is that it fails on SHAPE and not as a limit breach. Rule 1 in the one form the reject machinery can express for a syntactic bound: a host whose guard sits one level too tight answers LIMIT_EXCEEDED here and fails" } ]
