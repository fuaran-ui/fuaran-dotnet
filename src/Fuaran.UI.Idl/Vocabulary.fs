module Fuaran.UI.Vocabulary

open Fuaran.Core.Idl

// ---------------------------------------------------------------------------
// Phase 317 — the real-tier migration. ALL FIVE families, ~40 kinds.
//
// The Phase 316 spike (`Fuaran.Core.Idl.Spike`) proved the IDL drives a codec
// byte-identical to the wire over an 8-kind *mini* vocabulary. This file grew the
// IDL to the FULL real `Fuaran.UI` vocabulary — the kinds + value-unions + enums
// + records + maps whose canonical encoder is
// `Fuaran.UI.OpStream.Abstractions.CanonicalJson` — and proves the schema-driven
// encoder reproduces the live `Fuaran-UI/wire-format-fixtures` corpus
// byte-for-byte, one kind-family at a time (the staged migration the phase
// mandates). Now covers Display (17 kinds) + Layout (11) + Input (5) +
// Visualisation (4) + Meta (4) = 41 kinds.
//
// Why byte-identity is *already* guaranteed for modelled shapes: the spike's
// encoder renders through `Fuaran.Core.Canon.render`, which Ordinal-sorts object
// keys recursively, escapes control chars as `\u00xx` (no `\n`/`\r`/`\t`
// shortcuts), and pins the `ToString("R")` float layout — byte-for-byte the rules
// `CanonicalJson.appendObject` / `appendRawString` / `appendFloat` apply. So the
// only work to reach the real tier was *modelling the field classes* the mini IDL
// lacked, all landed in `Idl.fs`:
//   * closure-typed fields (`TClosure` → `"<closure>"`) — `Binding.Query`'s
//     accessor, `Action.Dispatch`'s msg, every `onChange` / `onSelect` / `onRead`;
//   * obj-erased fields (`TOpaque` → `"<opaque>"`) — `Sparkline.source`,
//     `Select.source` / `.value`, choice `options`;
//   * non-discriminated *records* (`TRecord`, no `$type`) — `FormField`,
//     `FilterSpec`, `TabHeader`, `ColumnErased`, `ContentHash`, `EffectClass`;
//   * string-keyed *maps* (`TMap`) — `Custom.props`, `FragmentRef.args`;
//   * the real recursive `TextSource` / `Binding<'T>` (now at FULL case parity
//     with the hand-written tier — Static/Query/Filter/Selection/State/Computed/
//     I18n/Local/Format/Transform/Invoke, the Phase 692 gap-closure) / `Action`
//     / `CellFormat` / `HoleDecl` unions;
//   * HOSTED slots (`THosted`) — `Binding.Transform`'s `source` / `pipeline`
//     delegate to `Fuaran.Core.ColumnCodec` / `DataFrameCodec`, and the `Range`
//     control's transparent-Static value carries a slot-specific codec.
//
// The earlier exclusions have all since landed: the node envelope (Phase 690),
// `'Msg` threading via `TFn` (Phase 691), `grid-transform` + the whole Transform
// family and the Phase 596 auto-bind omissions (the Phase 692 gap-closure — the
// full 85-fixture node corpus now round-trips through the generated layer, the
// tier-side `GeneratedLayerTests` pin). Still out of scope: multi-param generic
// specs (`GridSpecOf<'row,'Msg>` — a typed *author* facade, not wire-visible)
// and the `Types.fs` switch-over itself (Phase 692's remaining work).
// ---------------------------------------------------------------------------

// ─── Enums (bare-string DUs on the wire) ───────────────────────────────────
//
// Phase 1691 — THIS IS THE ONE HOME OF THE CASE-TO-WIRE-TOKEN MAPPING, and it
// is now pinned rather than merely stated. `Declare.enumOf` declares an
// identity mapping (the token IS the case name); `Declare.enumWith` declares a
// non-identity one as `(case, wire)` pairs. Six enums use the latter —
// `LinkProtection`, `TextDirection`, `TextFormat`, `CompareOp`,
// `SortDirection`, `LiveRegionKind` — and an emitter that derives a token from
// a case name COMPILES and writes `"Polite"` where the corpus says `"polite"`.
//
// A SEVENTH non-identity mapping is a declaration, not a discovery: switching
// an enum here from `enumOf` to `enumWith` changes `idl.json`, the corpus's
// `enum-tokens.json` (co-emitted by `--emit-corpus`) and `WIRE_FORMAT.md` 3.5,
// and reddens `EnumTokenTests` until the corpus is regenerated and the "exactly
// six" assertion there is updated to acknowledge it.



let private headingVariant =
    Declare.enumOf "HeadingVariant" [ "Standard"; "Eyebrow"; "Caption"; "Lead" ]

/// Phase 812 — the anti-scraper render strategy for a `Link`. `Email` marks a
/// `mailto:` link whose address must not appear in plaintext in emitted HTML.
/// The wire string is lower-case (`"email"`), so the enum declares an explicit
/// case-to-wire mapping rather than relying on the identity default.
let private linkProtection = Declare.enumWith "LinkProtection" [ "Email", "email" ]

let private badgeVariant =
    Declare.enumOf "BadgeVariant" [ "Neutral"; "Brand"; "Success"; "Warning"; "Critical"; "Info" ]

let private orientation = Declare.enumOf "Orientation" [ "Vertical"; "Horizontal" ]

/// `Box.role` — the container-role enum (Fuaran-UI 0.2.0 Box unification: Dashboard /
/// Card / Stack / GridLayout collapsed into one `Box` kind with a `role` + `layout`).
let private boxRole =
    // `Separator` (the divider role) is wire vocabulary the hand-written
    // encoder emits with no corpus fixture — found by the stage-3 swap.
    Declare.enumOf "BoxRole" [ "Dashboard"; "Card"; "Group"; "Separator" ]

let private mathDisplay = Declare.enumOf "MathDisplay" [ "Inline"; "Block" ]

let private imageVariant =
    Declare.enumOf "ImageVariant" [ "Default"; "Avatar"; "Rounded" ]

/// Phase 1077 — `Image.fit`: how the decoded pixels fill the box the layout
/// gives the element. `Natural` is the pre-phase behaviour (intrinsic aspect,
/// `height: auto` — no `object-fit` rule at all); `Cover` fills and crops;
/// `Contain` fits the whole image inside and letterboxes. A closed vocabulary
/// mapped to a class, never a free-form CSS value — the `ImageVariant`
/// precedent.
let private imageFit = Declare.enumOf "ImageFit" [ "Natural"; "Cover"; "Contain" ]

/// Phase 1077 — `Image.aspectRatio`: the box the element reserves BEFORE the
/// image arrives. This is the cumulative-layout-shift fix: with `Natural` the
/// browser learns the shape only once the bytes land and everything below
/// jumps; a declared ratio reserves the space in the first layout pass. The
/// vocabulary is the four ratios a page actually asks for and no more —
/// admitting arbitrary ratios would mean a numeric slot reaching a style
/// attribute, which is the free-form escape this language does not have.
let private imageAspect =
    Declare.enumOf "ImageAspect" [ "Natural"; "Square"; "FourThree"; "ThreeTwo"; "SixteenNine" ]

/// Phase 1077 — `Image.loading`: whether the browser fetches this image during
/// the initial load or defers it until it approaches the viewport. `Eager` is
/// the pre-phase behaviour and stays the default, because deferring an
/// above-the-fold image is a REGRESSION, not an optimisation — the choice
/// belongs to the author, who knows where the image sits.
let private imageLoading = Declare.enumOf "ImageLoading" [ "Eager"; "Lazy" ]

/// Phase 1111 — `Embed.permissions`: the closed set of deliberate relaxations of
/// an otherwise fully-sandboxed third-party browsing context. The default is the
/// EMPTY list, which is total denial, and every case here is a named step away
/// from it — the inverse of a capability list that starts full and gets pruned.
///
/// Four cases, each earning its place by being jointly necessary for one of the
/// three embed classes a page actually asks for: a video player (`AllowScripts`
/// + `AllowSameOrigin` + `AllowFullscreen`), a map (`AllowScripts` +
/// `AllowSameOrigin`), an embedded form (those two plus `AllowForms`). Three map
/// to HTML `sandbox` tokens and the fourth to a permissions-policy directive;
/// the enum names the RELAXATION, never the attribute, so a host maps each to
/// whatever its own surface expresses it with.
///
/// The exclusions are the design rather than an oversight. `allow-top-navigation`
/// lets a framed document navigate the TOP window, which is the drive-by
/// redirect, and no ubiquitous embed needs it — excluded, not reserved.
/// `allow-downloads` puts a file-save prompt in a third party's hands, likewise.
/// `allow-popups`, `allow-modals`, `allow-pointer-lock`, `allow-presentation`
/// and `allow-orientation-lock` have no recorded demand and are RESERVED as the
/// names a later admission would take — which is the whole reason this is an
/// enum rather than a bag of booleans (the `TrendPolarity` precedent: a fifth
/// case is then a bare-string addition, not a type replacement).
let private embedPermission =
    Declare.enumOf "EmbedPermission" [ "AllowScripts"; "AllowSameOrigin"; "AllowForms"; "AllowFullscreen" ]

let private toneVariant =
    Declare.enumOf "ToneVariant" [ "Default"; "Subdued"; "Brand"; "Success"; "Warning"; "Critical"; "Info" ]

let private styleWeight =
    Declare.enumOf "StyleWeight" [ "Compact"; "Standard"; "Spacious" ]

let private emphasis = Declare.enumOf "Emphasis" [ "Quiet"; "Normal"; "Loud" ]

/// `Metric.trendPolarity` — which direction of the measured quantity is GOOD.
/// A falling wait time, error rate, cost, churn or latency is an improvement; a
/// falling revenue is not, and until this slot existed the wire could not tell
/// the two apart. It is a property of the QUANTITY (permanent, every reading),
/// where `tone` is a property of THIS READING — so the two are never the same
/// statement and a host derives neither from the other.
///
/// `Neutral` (a quantity with no better direction) is deliberately NOT a case:
/// there is no demand evidence for it. It is reserved as the name a third case
/// would take, which is the whole reason this is an enum rather than a boolean
/// — a later admission is then a bare-string addition, not a type replacement.
let private trendPolarity =
    Declare.enumOf "TrendPolarity" [ "HigherIsBetter"; "LowerIsBetter" ]

// ─── Phase 690: the node envelope (WIRE_FORMAT.md §3.1) ────────────────────
//
// `style` / `state` / `accessibility` sit on the NODE, beside `id` and `kind`,
// and each is omitted when empty. Excluded from the IDL since Phase 671 on the
// stated grounds that no corpus fixture carried one — which Phase 674 found to be
// false (`style-role-voice-1` does, and the generated layer was corrupting it).

let private styleRole =
    Declare.enumOf "StyleRole" [ "None"; "Eyebrow"; "Data"; "Lede"; "Caption" ]

let private fontVoice =
    Declare.enumOf "FontVoice" [ "Default"; "Display"; "Structural" ]

/// The base direction of ONE authored value — `SemanticStyle.direction`, and
/// the run a `TextSource` carries. `Auto` is the identity: the value has no
/// declared direction of its own and the bidirectional algorithm resolves it
/// from its own characters, which is what every document said before this slot
/// existed, so `Auto` is omitted on the wire.
///
/// It says nothing about the DOCUMENT's direction, the reader's locale, or
/// which side the layout runs from — those are the host's, and none of them is
/// nameable here. What it declares is that this one value reads left-to-right
/// (or right-to-left) whatever surrounds it, so a renderer isolates it and the
/// surrounding prose cannot reorder it. Lower-case on the wire, matching the
/// values the isolation is ultimately expressed in.
let private textDirection =
    Declare.enumWith "TextDirection" [ "Auto", "auto"; "Ltr", "ltr"; "Rtl", "rtl" ]

/// Phase 691 — the per-node animation token. NEVER on the wire (`WIRE_FORMAT.md`
/// §9: motion is consumer-authored, not AI-authored), and declared only so the
/// host-only `Node.motion` field has a type to name.
///
/// Fuaran-UI Phase 1122 — `CrossFade` and `SlideBetween` join the eight, and they
/// are the first two tokens whose subject is a TRANSITION BETWEEN two renderings
/// rather than the arrival or the state of one. The other eight answer "what does
/// this node do when it appears, loads, errors or refreshes"; these two answer
/// "what happens when the thing standing here is REPLACED by another", which is
/// what a `Switch` does every time its bound selector moves.
///
/// They are declared here rather than as a `SwitchSpec` member for the reason
/// the enum exists at all: motion is the consumer's, not the document's. A
/// between-children transition is a look, and putting it on the spec would have
/// made it a fact the wire carries and every host must reproduce — which is the
/// opposite of what `Node.motion` being host-only decided. The cost of the
/// placement is stated plainly so it is not rediscovered: because `motion` is
/// wire-omitted (§9), NO corpus fixture can carry either token, and these two
/// cases take no §11 forward-coupling cost on the codec at all.
let private motion =
    Declare.enumOf
        "Motion"
        [ "None"
          "PulseDuringLoad"
          "FadeInOnMount"
          "SlideInFromBelow"
          "ShakeOnError"
          "RotateOnRefresh"
          "SlideInFromRight"
          "ExpandCollapse"
          "CrossFade"
          "SlideBetween" ]

/// `LayoutKind.ScrollArea`'s scroll-axis enum (distinct from `Orientation` — it
/// adds `Both`).
let private scrollOrientation =
    Declare.enumOf "ScrollOrientation" [ "Vertical"; "Horizontal"; "Both" ]

let private buttonVariant =
    Declare.enumOf "ButtonVariant" [ "Primary"; "Secondary"; "Tertiary"; "Destructive" ]

/// Fuaran-UI Phase 1119 — WHICH overlay a `Modal` node is. `Modal` is the
/// blocking task surface (scrim, focus trap, `aria-modal`); `Popover` is the
/// transient anchored one (no scrim, no trap, light-dismiss). Omitted at
/// `Modal` on the wire, so every document written before this release keeps
/// its bytes and its behaviour.
///
/// Deliberately TWO cases. A third ("sheet", "drawer", "menu") names a
/// PRESENTATION of one of these two, not a third modality: the axis this enum
/// declares is whether the surface blocks the page, and that question has two
/// answers.
let private modalityKind = Declare.enumOf "ModalityKind" [ "Modal"; "Popover" ]

let private fileReadEncoding =
    Declare.enumOf "FileReadEncoding" [ "Text"; "Base64"; "DataUrl" ]

/// Fuaran-UI Phase 1536 — `Action.Navigate.target`: which browsing context the
/// navigation lands in. Omitted at `Self` on the wire, so every document written
/// before this release keeps its bytes and its behaviour.
///
/// A CLOSED enum, deliberately, and NOT the free string `LinkSpec.target` is —
/// that field takes any `_blank` / `_parent` / `_top` / named-frame token a web
/// author might type, which is a vocabulary a decoded tree gets to invent. The
/// axis declared here has two answers: this context, or a fresh one. `_parent`
/// and `_top` are frame-busting gestures a hosted tree must not be able to ASK
/// for, and a named frame is an addressing scheme this language does not have.
///
/// `Blank` carries a security obligation rather than only a presentation one:
/// every host opens it with `noopener,noreferrer`, so the opened context cannot
/// reach back through `window.opener` and the destination is not told where the
/// reader came from. That obligation is discharged by the RENDERER on each host,
/// never delegated to a host-supplied navigation seam — a property only holds if
/// something owns it, and a seam every host must remember to get right is not
/// owned by anybody.
let private navigateTarget = Declare.enumOf "NavigateTarget" [ "Self"; "Blank" ]

/// Fuaran-UI Phase 1116 — `FileUpload.capture`: which of the reader's own
/// recording devices this upload asks the platform to open, instead of the
/// ordinary file picker. It is the HTML `capture` attribute's semantics and
/// nothing more: a REQUEST the user agent may honour or ignore, mediated by the
/// same picker permission the control already has, and carrying no stream, no
/// live preview and no standing grant. On a handset the OS camera or recorder
/// opens directly; on a desktop with no such device the input degrades to the
/// picker it already was, which is a working control rather than a dead one.
///
/// An ENUM rather than a boolean, on the `TrendPolarity` precedent: the two
/// devices are not one capability with a flag, and a third source (a screen, a
/// user-facing camera) is then an ADDITION to a closed set rather than the
/// replacement of a boolean that could only ever have meant one of them.
///
/// The DEVICE is what the document knows; the FACING is not. `Camera` projects
/// to the environment-facing keyword because a document asking for a photo is
/// asking for a photo of something, and `Microphone` to the user-facing one
/// because a recording of the reader is by construction the reader's own side —
/// but the keyword only ever constrains a camera, and which device actually
/// opens is decided by `accept`. That is why the pairing has a validator rule
/// (FUARAN134) rather than a renderer default: see `Renderer/Render.fs`.
///
/// Deliberately NOT here: screen capture, live recording, `getUserMedia` and any
/// stream vocabulary. The HTML attribute cannot express them, and the charter's
/// `ScreenCapture` / `CameraInput` row rules them Host chrome on trust grounds —
/// this enum is only the half a file picker already mediates.
let private captureSource =
    Declare.enumOf "CaptureSource" [ "Camera"; "Microphone" ]

/// Phase 1811 — the temporal breadth of a `DateTime` / `DateTimeRange` form
/// field. Renamed from `DateVariant` so the type says what the fields already
/// accept (a date, a time of day, or both); the three cases did not move.
let private dateTimeVariant =
    Declare.enumOf "DateTimeVariant" [ "Date"; "Time"; "DateTime" ]

/// Fuaran-UI Phase 864 — the named input format a `FieldRule` accepts. Lower-case
/// on the wire (the `LinkProtection` posture), so the enum declares an explicit
/// case-to-wire mapping. The set is deliberately three: `password` / `search` /
/// `number` / `color` are HTML input types with no demand evidence behind them,
/// and `number` would collide with `RangedNumber` and re-open the reuse rule.
///
/// Not to be confused with the `Format` union, which is a `Binding` case about
/// OUTPUT presentation. This enum is about which values are ACCEPTED on input.
let private textFormat =
    Declare.enumWith "TextFormat" [ "Email", "email"; "Url", "url"; "Tel", "tel" ]

/// Fuaran-UI Phase 864 — the comparison a cross-field `FieldRule` makes. Six
/// operators, one operand, and deliberately nothing else: no boolean
/// combinators, no arithmetic, no nesting. An expression language on the wire is
/// an evaluator every host must agree on to the bit, forever, and that remains
/// the standing rejection.
let private compareOp =
    Declare.enumWith "CompareOp" [ "Eq", "eq"; "Neq", "neq"; "Lt", "lt"; "Lte", "lte"; "Gt", "gt"; "Gte", "gte" ]

let private dateStyle =
    Declare.enumOf "DateStyle" [ "Short"; "Medium"; "Long"; "Full" ]

/// Phase 1810 — the time-of-day half of the platform formatter's
/// `dateStyle` / `timeStyle` pair (`Intl.DateTimeFormat`'s own model). The same
/// four cases as `DateStyle`, deliberately a SEPARATE enum: the two name
/// breadths of different things, and a host maps each to its own platform
/// option.
let private timeStyle =
    Declare.enumOf "TimeStyle" [ "Short"; "Medium"; "Long"; "Full" ]

let private relativeTimeUnit =
    Declare.enumOf "RelativeTimeUnit" [ "Second"; "Minute"; "Hour"; "Day"; "Week"; "Month"; "Year" ]

/// Fuaran-UI Phase 1533 — the resolution a `Binding.Now` declares for the
/// host-furnished instant.
///
/// FOUR members, not `RelativeTimeUnit`'s seven, and the omission is the
/// declaration: this is a TRUNCATION of a calendar instant, and a "week" or a
/// "month" has no truncation every host agrees on (which weekday starts a week;
/// which calendar). The four here truncate the canonical ISO-8601 instant by
/// prefix and nothing else, so five hosts reach the same string by the same
/// arithmetic-free route.
let private timeGrain =
    Declare.enumOf "TimeGrain" [ "Second"; "Minute"; "Hour"; "Day" ]

/// Phase 819 — the unit a `Format.Duration` / `CellFormat.Duration` numeric
/// source counts.
let private durationUnit =
    Declare.enumOf "DurationUnit" [ "Seconds"; "Minutes"; "Hours" ]

/// Phase 819 — the presentation style for a duration: `Compact` "1h 20m",
/// `Clock` "1:20:00", `Long` "1 hour 20 minutes".
let private durationStyle =
    Declare.enumOf "DurationStyle" [ "Compact"; "Clock"; "Long" ]

/// Phase 821 — the size class for the standalone `Icon` display kind. `Medium`
/// is the default and is omitted on the wire (the omit-at-default rule lives on
/// the kind field; the enum itself is a plain bare-string DU).
let private iconSize = Declare.enumOf "IconSize" [ "Small"; "Medium"; "Large" ]

let private chartKind =
    Declare.enumOf "ChartKind" [ "Line"; "Bar"; "Area"; "Pie"; "Scatter"; "Heatmap" ]

/// Phase 880 — which edge of the chart the series legend occupies, or `None`,
/// which suppresses it entirely.
let private chartLegendPosition =
    Declare.enumOf "ChartLegendPosition" [ "Top"; "Right"; "Bottom"; "None" ]

/// Phase 881 — whether a chart writes its values onto the picture, and where.
/// Deliberately two cases: there is no all-points case, because a number on
/// every interior point is the clutter the vocabulary exists to avoid.
let private chartDataLabels = Declare.enumOf "ChartDataLabels" [ "Off"; "Ends" ]

/// Phase 882 — what the chart's x axis MEANS: discrete categories, or dates on
/// a continuous temporal scale. Declared, never inferred.
let private chartXScale = Declare.enumOf "ChartXScale" [ "Category"; "Temporal" ]

let private hashStrictness =
    Declare.enumOf "HashStrictness" [ "StrictReplay"; "AdvisoryWarning"; "Enforced" ]

let private hostEffect =
    Declare.enumOf "HostEffect" [ "Pure"; "ReadsHost"; "WritesHost" ]

let private determinismSource =
    Declare.enumOf "DeterminismSource" [ "Deterministic"; "Clock"; "Random"; "Network" ]

// ─── Value-unions ──────────────────────────────────────────────────────────

let private req (name: string) (t: IdlType) : IdlField =
    { Name = name
      Type = t
      Opt = Required
      Annotations = Annotations.Empty
      Aliases = []
      Presence = None
      Keyed = false }

/// Phase 2640 — a node slot the KIND holds in a named arm rather than in its ordered child list:
/// a declared KEYED position (`IdlField.Keyed`, Core Phase 494). By the derived rule a node a kind
/// always carries is a structural child; these slots are arms (`NodeChildren.Position.Arm`), which
/// the structural ops never edit, so the generated `children` lists only the ordered positions and
/// the generated `keyedWitness` holds the arm — the pair `Introspect` declares by hand. A
/// declaration, not an encoding: no wire byte moves.
let private keyedNode (name: string) : IdlField = { req name TNode with Keyed = true }

let private opt (name: string) (t: IdlType) : IdlField =
    { Name = name
      Type = t
      Opt = Optional
      Annotations = Annotations.Empty
      Aliases = []
      Presence = None
      Keyed = false }

// ─── Phase 691: function-typed slots carry their HOST signature ────────────
//
// Wire behaviour is identical to `TClosure` — the fixed `"<closure>"` sentinel,
// presence-only decode. What `TFn` adds is the declaration, which is what lets
// the generated layer be the authoring type rather than a projection of it (D2).
//
// Signatures are read from `Fuaran.UI/Types.fs`, NOT inferred from the field
// name. Two shapes dominate: a handler returning `Action<'Msg>` (every `on*` on a
// spec) and a pure projection returning a value (the `DataGrid` column functions).
//
// Where an argument's host type is not IDL-declared — `BindingContext`,
// `ErrorPayload`, `CellValue`, `FileSelection` — the slot takes `obj` in that
// position and says so at the site. That is a real fidelity loss against the
// hand-written type, and it is the one thing standing between this phase and a
// generated layer that could be authored against directly; Phase 692 resolves it
// when it reconciles the two authoring surfaces.

/// A function-typed slot: the F# declaration, the TypeScript one, and the
/// expression the decoder puts in the slot (written at `'Msg = obj`).
let private hostOnly (name: string) (fs: string) (ph: string) : IdlField =
    { Name = name
      Type =
        TFn
            { FSharp = fs
              TypeScript = "never"
              Placeholder = ph }
      Opt = HostOnly
      Annotations = Annotations.Empty
      Aliases = []
      Presence = None
      Keyed = false }

let private fn (fs: string) (ts: string) (ph: string) : IdlType =
    TFn
        { FSharp = fs
          TypeScript = ts
          Placeholder = ph }

/// The common shape — an event handler `arg -> Action<'Msg>`. The placeholder is
/// `Action.Chain []`: a decoded tree has no behaviour, and "do nothing" is the
/// honest stand-in for a handler the wire could not carry.
let private handlerOf (arg: string) (tsArg: string) : IdlType =
    fn (arg + " -> Action<'Msg>") ("(v: " + tsArg + ") => Action") ("(fun (_: " + arg + ") -> Action.Chain [])")

/// Phase 2177 — a `FormFieldKind` VALUE slot. Every case declares it at ONE host
/// type, `Binding<JVal>`, so the generator can project it (`FormFieldKind.value`);
/// the payload is the wire value itself. The wire is UNCHANGED: the declared wire
/// form (`Binding<wire>`) is checked first, so a value of the wrong JSON type is
/// refused exactly as the typed slot refused it, and the codec stores the payload
/// as the JVal the typed encoder would have written (`enc`), so a decoded value
/// re-encodes to the same bytes. Typed readers and writers go through the
/// `FieldValue` accessors in `Types.fs`.
let private fieldValue (wire: IdlType) (dec: string) (enc: string) : IdlType =
    THosted
        { FSharp = "Binding<JVal>"
          Encode = "(encBinding id)"
          Decode =
            sprintf
                "(fun (j: JVal) -> decBinding (fun __p -> %s __p |> Result.map %s) j |> Result.mapError DecodeError.describe)"
                dec
                enc
          Wire = Some(TUnion("Binding", [ wire ]))
          Format = None
          Refusal = HostedRefusal.Sentence }

/// Phase 2177 — a `FormFieldKind` CHANGE handler: one name (`onChange`) and one
/// host type on every case, so the generator can project it
/// (`FormFieldKind.onChange`). The argument is the new value as the wire would
/// carry it; the `FieldChange` accessors in `Types.fs` adapt a typed handler.
/// `tsArg` keeps the per-kind TypeScript signature the other hosts declare.
let private fieldChange (tsArg: string) : IdlType = handlerOf "JVal" tsArg

/// A pure projection `arg -> result` (no `'Msg`) — the `DataGrid` column
/// functions and `Binding`'s accessors.
let private projOf (arg: string) (result: string) (tsSig: string) (ph: string) : IdlType =
    fn (arg + " -> " + result) tsSig ph

/// A field omitted on the wire when it equals its identity default `dflt`, restored
/// on absence (Fuaran-UI Phase 460 omit-when-default: tone/weight/emphasis/format/width).
let private omit (name: string) (t: IdlType) (dflt: IdlValue) : IdlField =
    { Name = name
      Type = t
      Opt = OmitDefault dflt
      Annotations = Annotations.Empty
      Aliases = []
      Presence = None
      Keyed = false }

/// `TextSource` — `Literal` (the corpus's only Display case) + `Bound`
/// (`Binding<string>`) + `I18n` (a name-keyed `Binding<JVal>` argument bag,
/// discriminated by inspection — Phase 1661; see the case comment below).
let private textSource =
    { Name = "TextSource"
      Params = []
      Cases =
        [ { Tag = "Literal"
            Fields = [ req "text" TStr ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Bound"
            Fields = [ req "binding" (TUnion("Binding", [ TStr ])) ]
            Annotations = Annotations.Empty
            Hosted = None }
          // i18n catalog lookup (Phase 692 swap-prep — the last TextSource case;
          // modelled with no corpus fixture behind it, because the hand-written
          // encoder emits it and the generated union had to hold it for the
          // swap). Phase 1078 closed that gap: `image-caption-i18n-1` carries an
          // `I18n` caption, so the case is now certified rather than merely
          // declared. `args` is a name-keyed bag, always emitted (matching the
          // hand-written arm).
          //
          // Phase 1661 — an argument is a `Binding<JVal>`, not a bare `JVal`, so
          // "3 items left" can take its count from the same slot the list reads.
          // The bag's element is a DISCRIMINATED-BY-INSPECTION slot (the
          // `TransformSource` precedent): the wire has no `$type` tag saying
          // "literal" versus "binding", the codec inspects the shape, and a
          // `Static` argument carrying a value emits the BARE value — which is
          // why every literal-args document ever emitted is byte-identical
          // across this widening. So it cannot be a `TUnion`; the two codecs are
          // support splices (`Support.fs`), reached by name.
          { Tag = "I18n"
            Fields =
              [ req "key" TStr
                req
                    "args"
                    (TMap(
                        THosted
                            { FSharp = "Binding<JVal>"
                              Encode = "encI18nArg"
                              Decode = "decI18nArg"
                              Wire = None
                              Format = None
                              Refusal = HostedRefusal.Sentence }
                    )) ]
            Annotations = Annotations.Empty
            Hosted = None } ]
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.CaseName
      Discrimination = UnionDiscrimination.Tagged }

/// `Binding<'T>` — the real recursive binding union, now at full case parity with
/// the hand-written tier (the Phase 692 gap-closure): every case the hand-written
/// encoder can emit is modelled — `Static` / `Query` / `Filter` / `Selection` /
/// `State` / `Computed` / `I18n` / `Local` / `Format` / `Transform` / `Expr` /
/// `Invoke` (`Expr` is Fuaran-UI Phase 1534's, and has no hand-written twin —
/// it was minted after the swap).
///
/// **Case-field ORDER matches the hand-written tier's positional order, not the
/// alphabetical convention** (Phase 692 swap-prep). The order is wire-free — the
/// canonical renderer Ordinal-sorts keys and the decoder reads by name — but it
/// IS the generated DU's positional shape, so matching the hand-written order
/// lets every existing construction/match site compile unchanged at the swap.
/// The `Deferred<'T>` trio (`Pending` / `Ready` / `Error`) is deliberately NOT
/// here: it is not a `Binding` case at all but a separate runtime-only envelope
/// ("a runtime value (the resolver produces it); not wire-serialised" — the
/// resolver's async view of an `Invoke`), and the corpus carries no occurrence.
let private binding =
    { Name = "Binding"
      Params = [ "T" ]
      Cases =
        // Phase 677 — absence is STRUCTURAL: a binding carrying no value omits the
        // key rather than emitting JSON null, for which the wire model has no case.
        [ { Tag = "Static"
            Fields = [ opt "value" (TVar "T") ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Phase 671 step 2 — the direct byte-diff caught this: the wire has NOT
          // carried `accessor` since 0.2.0 (the encoder renders `dependsOn` +
          // `name` only). The closure survives as a HOST-ONLY slot (never encoded,
          // restored to the identity projection on decode) so the generated case
          // can hold everything the hand-written one holds. `dependsOn` rides as a
          // string array, omitted when empty.
          { Tag = "Query"
            Fields =
              [ req "name" TStr
                hostOnly "accessor" "obj -> 'T" "(fun (raw: obj) -> unbox raw)"
                opt "dependsOn" (TList TStr) ]
            Annotations = Annotations.Empty
            Hosted = None }
          // `defaultValue` (Fuaran-UI 0.2.0) rides the wire when present, omitted
          // when None — the value the resolver yields before the filter is first
          // written.
          { Tag = "Filter"
            Fields = [ req "name" TStr; opt "defaultValue" (TVar "T") ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Row selection on `nodeId` (Fuaran-UI 0.2.9/0.2.10). `defaultValue` and
          // `field` (the declarative row-field projection) ride when present; the
          // accessor closure is host-only — the hand-written POLICY decoder
          // synthesises `projectSelectionField field` when `field` is present, a
          // context-dependent restoration the structural placeholder (identity)
          // deliberately does not attempt.
          { Tag = "Selection"
            Fields =
              [ req "nodeId" TStr
                hostOnly "accessor" "obj -> 'T" "(fun (raw: obj) -> unbox raw)"
                opt "defaultValue" (TVar "T")
                opt "field" TStr ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "State"
            Fields = [ req "key" TStr; opt "defaultValue" (TVar "T") ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Phase 765 — the environment "now" binding: the host furnishes the
          // instant, so the wire carries NOTHING beside the `$type` tag, and the
          // accessor is a HOST-ONLY slot restored to the identity projection on
          // decode (the Phase 427 Selection fix replayed — the host-furnished
          // instant is already the wire-shaped string, so a value-discarding
          // placeholder would make every decoded `Now` resolve to nothing).
          // Fuaran-UI Phase 1533 — `grain` is the one thing the wire DOES carry
          // beside the tag, and only when it is not `Second`: the resolution the
          // document declares for the host's instant. Omitted at its default, so
          // every 0.66.0-era `{"$type":"Now"}` is byte-identical before and
          // after, and the host truncates BEFORE the accessor sees the value —
          // a `Day`-grain `Now` is the `YYYY-MM-DD` that Core's `DateDiffDays`
          // accepts, with no host-side truncation left unspecified.
          { Tag = "Now"
            Fields =
              [ hostOnly "accessor" "obj -> 'T" "(fun (raw: obj) -> unbox raw)"
                opt "grain" (TEnum "TimeGrain") ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Computed"
            // `BindingContext -> 'T`. `BindingContext` is a HOST type (it carries a
            // `TryGetState<'T>` member), so the argument erases to `obj` here.
            //
            // Fuaran-UI Phase 1538 — the decoded stand-in RAISES. It used to be
            // `(fun _ -> Unchecked.defaultof<'T>)`, which meant a decoded
            // `Computed` handed the resolver `0` / `""` / `false` and the slot
            // rendered it as though the computation had run: a wrong answer
            // indistinguishable from a right one, with no warning anywhere. The
            // case has no wire projection and never will (the `fn` is the whole
            // payload and it encodes as `"<closure>"`), so the honest decoded
            // behaviour is to fail loudly at the point of use and name the cases
            // that DO cross — which is what `HostPrelude.decodedComputed` does.
            Fields = [ req "fn" (projOf "obj" "'T" "(ctx: unknown) => T" "Fuaran.UI.HostPrelude.decodedComputed") ]
            Annotations = Annotations.Empty
            Hosted = None }
          // A controlled-input local buffer. `initialFrom` recurses at the same
          // `'T`; `format` / `onCommit` / `parse` are closures; `flushOn` is a DU.
          //
          // Fuaran-UI Phase 1538 — the three closure slots gained wire-representable
          // DEFAULTS and ALTERNATIVES, so a wire-authored buffer round-trips a value
          // instead of emptying the field:
          //
          //  * `format` / `parse` restore to the IDENTITY rather than to
          //    `(fun _ -> "")` / `(fun _ -> Error "<closure>")`. The parse
          //    placeholder is written against `decT`, the decoder the surrounding
          //    `decBinding<'T>` already carries — type-directed with no reflection,
          //    so a text slot takes the string verbatim and a numeric slot takes the
          //    number the text denotes. It is the only placeholder that reads a
          //    generated local, and it is spelled that way deliberately: the
          //    alternative is a runtime type dispatch each host would get subtly
          //    different.
          //  * `codec` is the declarative twin the spec named — a locale-free
          //    edit-buffer codec. The STRUCTURAL decoder here only carries it into
          //    the case; synthesising `format` / `parse` FROM it is the hand-written
          //    POLICY decoder's job, exactly as `Selection.field` is (a
          //    context-dependent restoration the structural placeholder deliberately
          //    does not attempt).
          //  * `commitTo` is the State-key sibling of the `onCommit` closure — the
          //    Phase 818 `valueFrom` shape. The policy decoder enforces the sentinel
          //    XOR `commitTo`; a document that declares both has said the same thing
          //    two ways and only one of them can be honoured.
          //
          // Both new fields are appended rather than sorted into place: the wire
          // order is `Canon.typed`'s Ordinal sort and is unaffected either way, so
          // appending is the change that leaves every existing positional
          // construction and pattern in the estate arity-broken but position-stable.
          { Tag = "Local"
            Fields =
              [ req "flushOn" (TUnion("LocalFlushTrigger", []))
                req
                    "format"
                    (projOf
                        "'T"
                        "string"
                        "(v: T) => string"
                        "(fun (v: 'T) -> Fuaran.UI.HostPrelude.LocalCodec.identityFormat (box v))")
                req "initialFrom" (TUnion("Binding", [ TVar "T" ]))
                opt "onCommit" (projOf "'T" "obj" "(v: T) => unknown" "(fun _ -> (\"<closure>\" :> obj))")
                req
                    "parse"
                    (projOf
                        "string"
                        "Result<'T, string>"
                        "(s: string) => T"
                        "(Fuaran.UI.HostPrelude.LocalCodec.identityParse (decT >> Result.mapError DecodeError.describe))")
                opt "codec" (TUnion("Format", []))
                opt "commitTo" TStr ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Locale-aware formatted string. `source` is ALWAYS `Binding<float>`
          // (independent of `'T`); `format` / `locale` are bounded DUs.
          { Tag = "Format"
            Fields =
              [ req "source" (TUnion("Binding", [ TFloat ]))
                req "format" (TUnion("Format", []))
                req "locale" (TUnion("LocaleSource", [])) ]
            Annotations = Annotations.Empty
            Hosted = None }
          // i18n catalog lookup. `args` is a name-keyed bag of `Binding<obj>`
          // placeholder sources, omitted when None. The obj-erased positions
          // (here and `Transform.params`) instantiate at `JVal` — the typed
          // verbatim carrier — because `TOpaque` would erase real defaultValues
          // to a sentinel and lose bytes.
          { Tag = "I18n"
            Fields = [ req "key" TStr; opt "args" (TMap(TUnion("Binding", [ TJson ]))) ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Declarative dataframe transform (Fuaran-UI Phase 282/424 — the Compute
          // layer). `source` / `pipeline` are HOSTED slots: real `Fuaran.Core`
          // types rendered by Core's own codecs under the same `Canon` discipline,
          // so the composite splices in canonical and byte-stable ($type < params
          // < pipeline < source after the Ordinal sort). `params` binds pipeline
          // `ColExpr.Param` names to scalar binding sources, omitted when empty.
          { Tag = "Transform"
            Fields =
              [ req
                    "source"
                    // Phase 818/945 - the source slot is the host `TransformSource` DU
                    // (Data = the columnar/ref shape; Live = a binding-shaped source
                    // preserved verbatim). A discriminated-BY-INSPECTION union - the
                    // wire has no `$type: "Data"|"Live"` tag, the decode inspects the
                    // shape - so it cannot be a TUnion; the type and both codecs are
                    // Phase 945 support splices (UiIdlSupport.fs), reached by name.
                    (THosted
                        { FSharp = "TransformSource"
                          Encode = "encTransformSource"
                          Decode = "decTransformSource"
                          Wire = None
                          Format = None
                          Refusal = HostedRefusal.Sentence })
                req
                    "pipeline"
                    (TList(
                        THosted
                            { FSharp = "Fuaran.Compute.Transform"
                              Encode = "Fuaran.Compute.DataFrameCodec.encodeTransform"
                              Decode =
                                "(fun __j -> Fuaran.Compute.DataFrameCodec.decodeTransform __j |> Result.mapError string)"
                              Wire = None
                              Format = None
                              Refusal = HostedRefusal.Sentence }
                    ))
                opt "params" (TList(TRecord "TransformParam")) ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Fuaran-UI Phase 1534 — scalar logic over bound values. `Transform`'s
          // sibling with the ROW removed: one `ColExpr` evaluated against the
          // param environment alone, yielding one cell.
          //
          // It mints NO operator. `Binary` / `Not` / `Coalesce` / `Case` /
          // `ApplyFn concat` / `IsNull` / `InList` / `InParam` are already the
          // pipeline's vocabulary, and `expr` reuses `Fuaran.Core`'s own
          // encoding of them VERBATIM — so an expression means here exactly what
          // it means inside a `derive`, and there is one algebra to specify,
          // certify and teach rather than two that drift apart.
          //
          // Two refusals at decode, both because an `Expr` has no row (they are
          // enforced in the POLICY decoder, where a `$`-rooted path and a code
          // can be attached to each): a `Col` reference — whose remedy is
          // `Binding.Transform`, the case that has the frame `Col` reads from —
          // and a `Param` this binding's own `params` list does not bind. The
          // second is decidable statically HERE where it is not for `Transform`
          // (whose unbound filter params are PRUNED, the deliberate "unset chip
          // ⇒ no constraint" leniency); an `Expr` has no step to prune, so an
          // unbound param can only ever be an error, and saying so at decode
          // beats discovering it once per render.
          { Tag = "Expr"
            Fields =
              [ req
                    "expr"
                    (THosted
                        { FSharp = "Fuaran.Compute.ColExpr"
                          Encode = "Fuaran.Compute.DataFrameCodec.encodeExpr"
                          Decode = "(fun __j -> Fuaran.Compute.DataFrameCodec.decodeExpr __j |> Result.mapError string)"
                          Wire = None
                          Format = None
                          Refusal = HostedRefusal.Sentence })
                opt "params" (TList(TRecord "TransformParam")) ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Host-registered capability value. Same wire shape as `Action.Invoke`.
          { Tag = "Invoke"
            Fields = [ req "capabilityId" TStr; req "args" (TList(TRecord "InvokeArg")) ]
            Annotations = Annotations.Empty
            Hosted = None } ]
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.CaseName
      Discrimination = UnionDiscrimination.Tagged }

/// `CellFormat` — the column / `Metric` display-format vocabulary. `Number` /
/// `Percent` carry an *optional* `decimals` (omitted on `None`, rule 4); `Custom`
/// carries a closure fn.
///
/// Phase 2490 — the three figure cases (`Number`, `Currency`, `Percent`) carry an
/// *optional* `locale`. Absent, the figure is the locale-free projection it has
/// always been, on every host; present, it renders through the same locale
/// formatter `Binding.Format` uses for the matching `Format` case (Phase 102).
///
/// Phase 2499 — `Currency` carries an *optional* `decimals` too, so every figure
/// case can set its fraction digits (operator ruling B, 2026-10-28). Absent, the
/// currency renders exactly as before; present, it is the fixed digit count on the
/// locale-free projection and the locale formatter's fraction digits alike.
let private cellFormat =
    { Name = "CellFormat"
      Params = []
      Cases =
        [ { Tag = "None"
            Fields = []
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Number"
            Fields = [ opt "decimals" TInt; opt "locale" (TUnion("LocaleSource", [])) ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Currency"
            Fields =
              [ req "code" TStr
                opt "decimals" TInt
                opt "locale" (TUnion("LocaleSource", [])) ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Percent"
            Fields = [ opt "decimals" TInt; opt "locale" (TUnion("LocaleSource", [])) ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "SignificantDigits"
            Fields = [ req "digits" TInt ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Phase 1811 — `DateTime` (was `Date`): the .NET format string it
          // carries renders a date, a time, or both, and the name now says so.
          { Tag = "DateTime"
            Fields = [ req "format" TStr ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Phase 819 — trendable duration cells: the raw float counts `unit`s,
          // rendered per `style`.
          { Tag = "Duration"
            Fields = [ req "unit" (TEnum "DurationUnit"); req "style" (TEnum "DurationStyle") ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Phase 819 — cell-vocabulary parity with `Format.RelativeTime`: the raw
          // float is a signed count of `unit`.
          { Tag = "RelativeTime"
            Fields = [ req "unit" (TEnum "RelativeTimeUnit") ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Custom"
            // `CellValue -> string`; `CellValue` is a host-prelude DU (stage 4b) —
            // the stage-3 obj erasure un-erased now the prelude hosts the type.
            Fields =
              [ req "fn" (projOf "Fuaran.UI.HostPrelude.CellValue" "string" "(v: unknown) => string" "(fun _ -> \"\")") ]
            Annotations = Annotations.Empty
            Hosted = None } ]
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.CaseName
      Discrimination = UnionDiscrimination.Tagged }

/// `Action<'Msg>` — the effect-typed action union. `Chain` recurses; `Dispatch`
/// / `onRead` payloads are closures; `Invoke` / `ReadFileBody` carry data. The
/// data cases not in the corpus (`Navigate` / `Notify` / `SetState` / `Call` /
/// `CommitLocal`) are omitted until a fixture exercises them.
let private action =
    { Name = "Action"
      Params = []
      Cases =
        [ { Tag = "Chain"
            Fields = [ req "ops" (TList(TUnion("Action", []))) ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Phase 1126 — the payload is a `TextSource`, not a bare string, so
          // the thing a reader actually copies (a bound value, a computed
          // reference) has a spelling. Widening the existing case was chosen
          // over a sibling `WriteToClipboardBound`, which would have minted the
          // permanent near-synonym pair the vocabulary charter exists to
          // forbid.
          //
          // THE WIRE DOES NOT MOVE for a literal payload: `TextSource.Literal`
          // is canonically the BARE JSON STRING (§3.6, §16), so
          // `{"$type":"WriteToClipboard","text":"…"}` is emitted and accepted
          // exactly as before. What is new is a `text` carrying a `Bound` /
          // `I18n` object, and the §16 normalisation of the explicit
          // `{"$type":"Literal","text":…}` envelope at this slot.
          { Tag = "WriteToClipboard"
            Fields = [ req "text" (TUnion("TextSource", [])) ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Fuaran-UI 0.2.x: the dispatch msg closure is omitted entirely (no wire key).
          // `Dispatch of 'Msg`. The payload is a host value with NO wire projection —
          // `{"$type":"Dispatch"}` is the whole encoding, before and after. Declaring it
          // host-only is what lets the generated `Action` be the authoring `Action`.
          //
          // Phase 1152 — `InProcessOnly` is the ANNOTATION half of that fact, and it is
          // declared here rather than written onto the generated file because the
          // generated file is a byte-pinned projection: a hand-added attribute is erased
          // by the next regeneration. `hostOnly` above already states the wire behaviour
          // to the CODEC; this states it to the READER of the declaration, which is a
          // different audience and the one that keeps putting a `Dispatch` on a tree it
          // means to serialise. The generator emits two doc lines and one
          // `[<System.Obsolete(…, false)>]` — FS0044, a WARNING, deliberately not an
          // error: every in-process construction site in this repo and in every
          // downstream Fable host is CORRECT, and a marking that fails their builds
          // would be a claim about them that is not true. Hosts that serialise escalate
          // it (`--warnaserror:44`); hosts that do not scope it away (`NoWarn 44`) with
          // a comment saying why. See `STABILITY.md` and Support.fs's
          // `case:Action.Dispatch` doc block for the remedies.
          { Tag = "Dispatch"
            Fields = [ hostOnly "msg" "'Msg" "((\"<dispatch>\" :> obj))" ]
            Annotations =
              { Annotations.Empty with
                  InProcessOnly = true }
            Hosted = None }
          { Tag = "Invoke"
            Fields = [ req "capabilityId" TStr; req "args" (TList(TRecord "InvokeArg")) ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Phase 1117 — `ReadFileBody` is the SMALL-PAYLOAD path, and this is
          // the statement of that rather than an aside. It reads a selected
          // file's whole body into a string and hands it to a closure, so the
          // body travels the message loop and lands in whatever durable record
          // the host keeps of it; under `Base64` / `DataUrl` it is inflated by a
          // third on the way. That is exactly right for a small text file, a
          // configuration blob, a signature capture — and structurally wrong for
          // a video, an archive or a photograph. Above roughly a few hundred
          // kilobytes the answer is `FileUploadSpec.destination` and the host's
          // upload sink, which streams the bytes to a registered destination and
          // returns a REFERENCE the op stream can carry.
          //
          // Neither replaces the other and this member is not deprecated: one
          // ingests a body a handler needs in hand, the other moves bytes to a
          // place and names them. What is deprecated is using this one for size.
          { Tag = "ReadFileBody"
            Fields =
              [ req "fileRef" TStr
                // The runtime file handle (the boxed browser `File` blob) rides
                // BESIDE the wire id as a host-only slot (Phase 692 stage 2) —
                // never encoded, restored to `None` on decode, exactly the
                // hand-written `FileRef.Handle` semantics ("only Ref.Id ever
                // serialises"). Without it the generated case could name a file
                // it can no longer read.
                hostOnly "fileHandle" "obj option" "None"
                req "encoding" (TEnum "FileReadEncoding")
                opt "onRead" (fn "string -> 'Msg" "(body: string) => Msg" "(fun (_: string) -> (\"<closure>\" :> obj))") ]
            Annotations = Annotations.Empty
            Hosted = None }
          // `ApiEndpoint` is a bare string on the wire; `into` is the declarative
          // result target, omitted when None; `onResult` rides only when present.
          { Tag = "Call"
            Fields =
              [ req "endpoint" TStr
                opt "onResult" (fn "obj -> 'Msg" "(r: unknown) => Msg" "(fun (_: obj) -> (\"<closure>\" :> obj))")
                opt "into" (TUnion("CallResultTarget", [])) ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Phase 1536 — `route` is a `TextSource`, not a bare string, so
          // "open the selected order" (`/orders/{selection.id}`) has a
          // spelling. The precedent is exact and deliberate: Phase 1126 widened
          // `WriteToClipboard`'s payload the same way, for the same reason, and
          // widening the existing case was again chosen over a sibling
          // `NavigateBound` — two cases for one intent is the permanent
          // near-synonym pair the vocabulary charter exists to forbid.
          //
          // THE WIRE DOES NOT MOVE for a literal route: `TextSource.Literal` is
          // canonically the BARE JSON STRING (§3.6, §16), so
          // `{"$type":"Navigate","route":"/x"}` is emitted and accepted exactly
          // as before. What is new is a `route` carrying a `Bound` / `I18n`
          // object, RESOLVED AT DISPATCH TIME and gated on its resolved value —
          // never on its template, which would gate a string nobody navigates
          // to. The §16 `href` / `url` / `to` aliases still normalise onto this
          // one canonical field, so there remains no spelling that reaches a
          // router around the check.
          //
          // `target` is omitted at `Self`. See `navigateTarget` for why it is a
          // closed enum rather than `LinkSpec.target`'s free string.
          { Tag = "Navigate"
            Fields =
              [ req "route" (TUnion("TextSource", []))
                omit "target" (TEnum "NavigateTarget") (VEnum "Self") ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "CommitLocal"
            Fields = [ req "nodeId" TStr ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Phase 676 — the three JSON-payload actions. `TJson`, never `TOpaque`:
          // these carry real data in both directions (`Notify` is the estate's
          // cross-host data primitive), so erasing them to a sentinel would be
          // silent data loss.
          { Tag = "Notify"
            Fields = [ req "channel" TStr; req "payload" TJson ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Phase 818 — `valueFrom` (a Binding evaluated at dispatch time) is a
          // SIBLING of the literal `value`, and `value` became optional in the same
          // change so the valueFrom-only wire shape is representable. Both are
          // declared Optional because that is what the SHAPE is; the "exactly one"
          // rule is decoder policy (`reject-setstate-value-and-valuefrom`), which
          // the IDL states no more than it states path addressing.
          { Tag = "SetState"
            Fields =
              [ req "key" TStr
                opt "value" TJson
                opt "valueFrom" (TUnion("Binding", [ TJson ])) ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "AiTool"
            Fields = [ req "toolName" TStr; req "args" TJson ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Phase 1124 — the first PAYLOAD-FREE `Action` case. "Print this
          // invoice": ask the host to open the reader's own print dialogue.
          // There is nothing to carry — no page size, no margin, no sheet
          // range, no target subtree — and that emptiness is the ruling as much
          // as the case is. The paged MEDIUM is Host chrome (the ratified
          // `PrintLayout` / `PageBreak` charter row); what a document may say is
          // *print now*, and every parameter of *how* belongs to the host and
          // the reader's own dialogue.
          //
          // `Fields = []` is therefore the whole declaration, and it makes
          // `{"$type":"Print"}` the complete encoding — the `LocaleSource.Ambient`
          // / `CurveCommand.Close` shape, reached for the first time on this
          // union. Any member on the wire beside `$type` is refused rather than
          // ignored (`reject/action-print-with-payload`), because a member the
          // decoder drops silently is a parameter an emitter believes it sent.
          { Tag = "Print"
            Fields = []
            Annotations = Annotations.Empty
            Hosted = None }
          // Phase 1537 — "delete — are you sure?" as ONE case rather than as a
          // composition. Today it is spelled `Modal.Open` + `SetState` + a
          // second button: a state key, a writer and a control the emitter must
          // invent, for an intent every host already owns as a dialogue.
          //
          // `onConfirm` / `onCancel` are `Action`s, so this case RECURSES — the
          // `Chain` shape, and the second recursive case on this union. The
          // recursion is bounded at DEPTH ONE by the policy decoder, which
          // refuses a `Confirm` anywhere inside either continuation (under a
          // `Chain` included): a dialogue that answers a dialogue is a modal
          // stack a reader cannot escape, and there is no intent it expresses
          // that a single question does not.
          //
          // The continuation is NOT a second path around the dispatch gate.
          // `Confirm` itself is gated — a host rendering untrusted trees must be
          // able to refuse an unbidden dialogue, the `Print` reasoning — and on
          // acceptance the continuation re-enters the ORDINARY dispatch entry,
          // so a `Navigate` inside it meets its own egress check and its own
          // gate exactly as it would outside one.
          //
          // `prompt` is a `TextSource` (the 1126 / 1536 spelling), so the
          // question can name what the reader selected; a literal prompt is the
          // bare JSON string, so nothing about that spelling is new.
          { Tag = "Confirm"
            Fields =
              [ req "prompt" (TUnion("TextSource", []))
                req "onConfirm" (TUnion("Action", []))
                opt "onCancel" (TUnion("Action", [])) ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Phase 1537 — move keyboard focus to an addressed node. The
          // server-driven tier has carried `ClientEffect.Focus of nodeId` since
          // Phase 152 with no `Action` counterpart, which Phase 1124's outcome
          // recorded in passing; this closes that asymmetry rather than opening
          // a new capability.
          //
          // `nodeId` is a bare string and not a `TextSource`: it addresses a
          // node in THIS document, which the author wrote, so there is nothing
          // for a binding to compute that the author does not already know —
          // the `CommitLocal` precedent exactly.
          //
          // Gated, on the `Print` reasoning: moving the reader's caret (and,
          // with it, the viewport) is host-observable even though no
          // `IFuaranRuntime` member backs it.
          { Tag = "Focus"
            Fields = [ req "nodeId" TStr ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Phase 2107 — control flow, spelled as the bounded core's own arms
          // (WIRE_FORMAT §3.6.34, §30.1). `Choose` is the core's SELECTION:
          // `condition` resolves at DISPATCH time through the store, as a
          // `SetState.valueFrom` does; the boolean `true` runs `whenTrue`, any
          // other value runs `whenFalse` (absent is the empty sequence), and an
          // unresolved or errored condition halts the gesture before either arm
          // runs. `exit` is the core's optional exit assertion, checked after
          // the arm. The names are the core's, so the §30 row is an identity
          // and nothing is re-spelled.
          //
          // Both arms add CONTROL, never an effect: every effect they reach is
          // an existing arm meeting its own gate. Neither is gated itself.
          { Tag = "Choose"
            Fields =
              [ req "condition" (TUnion("Binding", [ TJson ]))
                req "whenTrue" (TUnion("Action", []))
                opt "whenFalse" (TUnion("Action", []))
                opt "exit" (TUnion("Binding", [ TJson ])) ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Phase 2107 — the core's halting GUARD. `condition` resolves at
          // dispatch; the boolean `true` holds and changes nothing, anything
          // else halts the gesture, so nothing after the guard runs. This is
          // the arm no composition reaches: no existing case stops a `Chain`.
          { Tag = "Require"
            Fields = [ req "condition" (TUnion("Binding", [ TJson ])) ]
            Annotations = Annotations.Empty
            Hosted = None } ]
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.CaseName
      Discrimination = UnionDiscrimination.Tagged }

/// Where a `Call`'s result lands, declaratively. NOTE the wire tags are `State` /
/// `Query`, not the F# case names `IntoState` / `IntoQuery`.
let private callResultTarget =
    { Name = "CallResultTarget"
      Params = []
      Cases =
        [ { Tag = "State"
            Fields = [ req "key" TStr ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Query"
            Fields = [ req "name" TStr ]
            Annotations = Annotations.Empty
            Hosted = None } ]
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.CaseName
      Discrimination = UnionDiscrimination.Tagged }

/// The locale-aware `Binding.Format` intent union (distinct from [[cellFormat]] —
/// this one carries `isoCode` / `dateStyle` / `unit`, not `code`).
let private formatUnion =
    { Name = "Format"
      Params = []
      Cases =
        [ { Tag = "Number"
            Fields = [ opt "decimals" TInt ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Phase 2499 — an optional `decimals`, the formatter's minimum and
          // maximum fraction digits, overriding the currency's (and on the .NET
          // fallback the locale's) default digits. Absent, unchanged.
          { Tag = "Currency"
            Fields = [ req "isoCode" TStr; opt "decimals" TInt ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Percent"
            Fields = [ opt "decimals" TInt ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Phase 1810 — `dateStyle` / `timeStyle` are BOTH optional so that a
          // time of day can be displayed alone (`timeStyle` only) and a
          // date-time with both. Neither present is a defect the validator
          // refuses (FUARAN155), not a decode refusal: the wire shape is
          // structurally legal and the rule is a semantic one.
          // Phase 1811 — `DateTime` (was `Date`): an honest name once 1810 made
          // the formatter render a time of day. `DateStyle` keeps its name — it
          // still styles only the date part, beside `TimeStyle`.
          { Tag = "DateTime"
            Fields = [ opt "dateStyle" (TEnum "DateStyle"); opt "timeStyle" (TEnum "TimeStyle") ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "RelativeTime"
            Fields = [ req "unit" (TEnum "RelativeTimeUnit") ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Phase 819 — locale-independent duration formatting: the numeric source
          // counts `unit`s, rendered per `style`.
          { Tag = "Duration"
            Fields = [ req "unit" (TEnum "DurationUnit"); req "style" (TEnum "DurationStyle") ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Fuaran-UI Phase 1533 — the INSTANT-reading twin of `RelativeTime`.
          // `RelativeTime`'s source is a signed COUNT of its unit, already
          // computed by whoever produced it; `Since`'s source is an INSTANT in
          // whole Unix-epoch seconds (`Date`'s convention), and the count is the
          // delta the host takes against its own furnished instant. The two are
          // deliberately separate cases: widening `RelativeTime` to mean either
          // would silently re-read every shipped document that uses it.
          //
          // `unit` is OPTIONAL and its absence is not a default — it is the
          // auto-selection request, resolved from the fixed threshold table in
          // the spec (§4b), so "3 hours ago" and "2 days ago" come from one
          // authored slot.
          { Tag = "Since"
            Fields = [ opt "unit" (TEnum "RelativeTimeUnit") ]
            Annotations = Annotations.Empty
            Hosted = None } ]
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.CaseName
      Discrimination = UnionDiscrimination.Tagged }

let private localeSource =
    { Name = "LocaleSource"
      Params = []
      Cases =
        [ { Tag = "Ambient"
            Fields = []
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Explicit"
            Fields = [ req "tag" TStr ]
            Annotations = Annotations.Empty
            Hosted = None } ]
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.CaseName
      Discrimination = UnionDiscrimination.Tagged }

let private localFlushTrigger =
    { Name = "LocalFlushTrigger"
      Params = []
      Cases =
        [ { Tag = "OnBlur"
            Fields = []
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "OnSubmit"
            Fields = []
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "OnDebounce"
            Fields = [ req "milliseconds" TInt ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "OnCommitAction"
            Fields = []
            Annotations = Annotations.Empty
            Hosted = None } ]
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.CaseName
      Discrimination = UnionDiscrimination.Tagged }

/// `Box.layout` — the container-layout mode (Fuaran-UI 0.2.0 Box unification). `Auto`
/// (was `Dashboard`), `Flex` (was `Stack`, carries `direction` + `wrap`), `Grid` (was
/// `GridLayout`, carries `cols` + an optional `templateColumns`), `Masonry` (column-fill).
let private boxLayout =
    { Name = "BoxLayout"
      Params = []
      Cases =
        [ { Tag = "Auto"
            Fields = []
            Annotations = Annotations.Empty
            Hosted = None }
          // `gap` (the px spacing knob, omitted-when-None) is wire vocabulary
          // on BOTH layout cases — no corpus fixture carries it; found by the
          // stage-3 swap reading the hand-written encoder.
          { Tag = "Flex"
            // Phase 2300 — `wrap` omits at its declared default `false` (the
            // declared-default rule, WIRE_FORMAT §3.6).
            Fields =
              [ req "direction" (TEnum "Orientation")
                omit "wrap" TBool (VBool false)
                opt "gap" TInt ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Grid"
            Fields = [ req "cols" TInt; opt "templateColumns" TStr; opt "gap" TInt ]
            Annotations = Annotations.Empty
            Hosted = None }
          // `Masonry` — column-FILL, where `Grid` is row-fill. A separate case
          // rather than a field on `Grid` for the reason the vocabulary charter
          // §2.1 names: widening `Grid` changes its arity and stops every
          // pattern match on it compiling in every host, where a new case raises
          // an exhaustiveness error only where a match is exhaustive.
          //
          // It carries `cols` (spelled as `Grid` spells it — the same quantity,
          // deliberately not a second name) and `gap`, and NOT `templateColumns`:
          // that field is a verbatim CSS sizing function for the row-fill track
          // model, and masonry is realised through the multi-column property
          // family, which has no track list for it to name. Omitting it is what
          // keeps this case BOUNDED — it reaches only known CSS properties, so it
          // opens no escape hatch (Phase 900).
          { Tag = "Masonry"
            Fields = [ req "cols" TInt; opt "gap" TInt ]
            Annotations = Annotations.Empty
            Hosted = None } ]
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.CaseName
      Discrimination = UnionDiscrimination.Tagged }

/// `FormFieldKind<'Msg>` — the per-field input-shape union, shared by `Form`
/// fields AND `Filters` chips (the 0.2.0 filters-unification — the separate
/// `FilterKind` union this file carried until the Phase 692 gap-closure was
/// pre-unification drift).
///
/// **Every `value` slot is Optional (Phase 596 auto-bind).** The wire contract is
/// that a control may omit `value` entirely: a filter chip auto-binds
/// `Filter(name)`, a form field `State(field id, typed placeholder)`. That
/// synthesis is CONTEXT-dependent — it turns on the enclosing record's `name` /
/// `id` — so it is policy, owned by the hand-written decoder above this layer;
/// the structural layer carries absence as absence (`None` ⇔ no key), which is
/// what makes the round-trip byte-exact without expressing the context rule.
let private formFieldKind =
    { Name = "FormFieldKind"
      Params = []
      Cases =
        [ { Tag = "Text"
            Fields =
              [ opt "value" (fieldValue TStr "dStr" "JStr")
                opt "onChange" (fieldChange "string") ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Number"
            Fields =
              [ opt "value" (fieldValue TFloat "dFloat" "encFloat")
                opt "onChange" (fieldChange "number") ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Checkbox"
            Fields =
              [ opt "value" (fieldValue TBool "dBool" "JBool")
                opt "onChange" (fieldChange "boolean") ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Phase 766 — the boolean TOGGLE control: the same value / handler pair
          // as `Checkbox`, a distinct affordance rather than a styling of one.
          { Tag = "Toggle"
            Fields =
              [ opt "value" (fieldValue TBool "dBool" "JBool")
                opt "onChange" (fieldChange "boolean") ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Choice"
            Fields =
              [ req "options" (TUnion("Binding", [ TList(TRecord "SelectOption") ]))
                opt "value" (fieldValue TStr "dStr" "JStr")
                opt "onChange" (fieldChange "string | null") ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "TextArea"
            Fields =
              [ opt "value" (fieldValue TStr "dStr" "JStr")
                opt "onChange" (fieldChange "string")
                req "rows" TInt ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "RangedNumber"
            Fields =
              [ opt "value" (fieldValue TFloat "dFloat" "encFloat")
                opt "onChange" (fieldChange "number")
                opt "min" TFloat
                opt "max" TFloat
                opt "step" TFloat ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Dual-thumb numeric range (0.2.0 — absorbed FilterKind.RangeFilter).
          // The value slot is HOSTED because its Static case is TRANSPARENT on
          // the wire: `Binding.Static (Some pair)` encodes as the bare
          // `{"max":…,"min":…}` object (no `$type`), while every other binding
          // case keeps its tagged form with a RangePair static payload. That is
          // a property of this SLOT, not of the Binding union, so the slot
          // carries its own codec over the generated `encBinding` / `decBinding`
          // + `RangePair` record codecs.
          { Tag = "Range"
            Fields =
              [ opt
                    "value"
                    (THosted
                        { FSharp = "Binding<JVal>"
                          Encode =
                            "(fun (v: Binding<JVal>) -> match v with | Binding.Static(Some p) -> p | __other -> encBinding id __other)"
                          Decode =
                            "(fun (j: JVal) -> (match j with | JObj __rf when not (__rf |> List.exists (fun (k, _) -> k = \"$type\")) -> decRangePair j |> Result.map (fun p -> Binding.Static(Some(encRangePair p))) | __other -> decBinding (fun __p -> decRangePair __p |> Result.map encRangePair) __other) |> Result.mapError DecodeError.describe)"
                          Wire = None
                          Format = None
                          Refusal = HostedRefusal.Sentence })
                opt "onChange" (fieldChange "[number, number]")
                opt "min" TFloat
                opt "max" TFloat
                opt "step" TFloat ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "SegmentedChoice"
            Fields =
              [ req "options" (TUnion("Binding", [ TList(TRecord "SelectOption") ]))
                opt "value" (fieldValue TStr "dStr" "JStr")
                opt "onChange" (fieldChange "string | null")
                omit "orientation" (TEnum "Orientation") (VEnum "Horizontal") ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Phase 1811 — `DateTime` (was `Date`): the field already accepted a
          // date, a time of day or a date-time via `variant`; the name now says
          // so, and `Date{variant:"Time"}` is no longer a spelling nobody finds.
          { Tag = "DateTime"
            Fields =
              [ opt "value" (fieldValue TStr "dStr" "JStr")
                opt "onChange" (fieldChange "string | null")
                req "variant" (TEnum "DateTimeVariant")
                opt "min" TStr
                opt "max" TStr
                opt "step" TFloat ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Single-control date range (Fuaran-UI Phase 725; `DateTimeRange` since
          // Phase 1811) — `Range`'s pair mechanics with `DateTime`'s value conventions. The value slot carries
          // the same transparent-Static posture as `Range`: a `Static` pair
          // rides as the BARE `{from, to}` object (no `Static` envelope), any
          // other binding rides enveloped; both directions via the slot codec.
          { Tag = "DateTimeRange"
            Fields =
              [ opt
                    "value"
                    (THosted
                        { FSharp = "Binding<JVal>"
                          Encode =
                            "(fun (v: Binding<JVal>) -> match v with | Binding.Static(Some p) -> p | __other -> encBinding id __other)"
                          Decode =
                            "(fun (j: JVal) -> (match j with | JObj __rf when not (__rf |> List.exists (fun (k, _) -> k = \"$type\")) -> decDateTimeRangePair j |> Result.map (fun p -> Binding.Static(Some(encDateTimeRangePair p))) | __other -> decBinding (fun __p -> decDateTimeRangePair __p |> Result.map encDateTimeRangePair) __other) |> Result.mapError DecodeError.describe)"
                          Wire = None
                          Format = None
                          Refusal = HostedRefusal.Sentence })
                opt "onChange" (fieldChange "[string, string]")
                req "variant" (TEnum "DateTimeVariant")
                opt "min" TStr
                opt "max" TStr
                opt "step" TFloat ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Fuaran-UI Phase 1113 — the typeahead / autocomplete control. A
          // `Choice` is a bounded menu the reader scans; a `Combobox` is a
          // searchable one it FILTERS, which is what makes a two-hundred-option
          // source usable rather than merely valid.
          //
          // The option source is an ordinary `Binding<SelectOption list>`, so a
          // `Query`-bound source gives asynchronous suggestions through binding
          // machinery that already exists — no coordination vocabulary is minted
          // for it.
          //
          // `allowFreeText` omits at `false`, which makes the SHORTEST document
          // the constrained one: an emitter that says nothing gets the shape a
          // `Select` would have had, and admitting values outside the option set
          // is the thing it has to ask for.
          //
          // The value slot and the handler are `Choice`'s, deliberately: the
          // constrained combobox IS a searchable select, so the two must not
          // differ at a call site that migrates between them. With free text an
          // empty entry is genuinely no value, so collapsing it to `None` gives
          // one fact one spelling rather than two.
          //
          // Nothing here names a keystroke. Arrow / Enter / Escape, the listbox
          // popup and `aria-activedescendant` are the renderer's affordance
          // under the affordance→op charter.
          { Tag = "Combobox"
            Fields =
              [ omit "allowFreeText" TBool (VBool false)
                opt "onChange" (fieldChange "string | null")
                req "options" (TUnion("Binding", [ TList(TRecord "SelectOption") ]))
                opt "value" (fieldValue TStr "dStr" "JStr") ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Fuaran-UI Phase 1130 — the subjective SCORE control. `RangedNumber`
          // is a numeric QUANTITY the reader types or drags; `Rating` is a
          // judgement it expresses on a small ordinal scale, and the two are
          // kept apart by that sentence rather than by their shapes, which are
          // deliberately similar.
          //
          // `value` is `Binding<float>` and NOT `Binding<int>`, and the reason
          // is display rather than entry: the commonest rating a reader sees is
          // an AVERAGE — 4.3 of 5 over three hundred reviews, arriving through a
          // `Query` binding — and an integer slot cannot carry it. So the float
          // is load-bearing even where nobody can type a fraction.
          //
          // THE INTEGER-ONLY QUESTION, DECIDED (Phase 1130). ENTRY is whole
          // units unless `allowHalf` asks otherwise; DISPLAY is continuous
          // always, because the value type says so. Those are two questions and
          // the field separates them. `allowHalf` takes `Combobox.allowFreeText`'s
          // shape exactly — an omit-at-`false` bool, so the shortest document is
          // the constrained one and the wider granularity is what an emitter has
          // to ask for.
          //
          // It is a BOOL and not a `step`, deliberately, though `step` is the
          // reuse the vocabulary would ordinarily reach for. A `step` slot
          // admits `0.3`, which is a valid document naming an interaction no
          // rating control has ever had — so the decoder would owe a refusal
          // enumerating exactly {1, 0.5}, at which point the float is a boolean
          // wearing a wider type. It would also widen the `Rating ↔
          // RangedNumber` confusion pair that this row's charter entry names as
          // the one to watch, by giving the two controls a third slot in common.
          //
          // `max` is a REQUIRED int spelled as `TextArea.rows` is — the
          // authoring surface defaults it to 5, the wire always carries it.
          // Its lower bound is refused at DECODE (`Support.fs`, `CaseRefines`):
          // a scale with no positions is not a control with a bad value in it,
          // it is a document that cannot be rendered at all, and that is the
          // `Switch.autoAdvanceMs` line. A `value` OUTSIDE the scale is the
          // other side of that split and belongs to the validator and the
          // server-driven floor, because a bound value is invisible here.
          //
          // Nothing here names a keystroke or a role. Arrow / Home / End and
          // the slider-versus-image announcement are the renderer's affordance
          // under the affordance→op charter.
          { Tag = "Rating"
            Fields =
              [ omit "allowHalf" TBool (VBool false)
                req "max" TInt
                opt "onChange" (fieldChange "number")
                opt "value" (fieldValue TFloat "dFloat" "encFloat") ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Fuaran-UI Phase 1130 — the colour control, projecting to the
          // platform's own `<input type="color">`.
          //
          // Note what this is NOT: the charter's `EmailField` row declined
          // `color` as a `rule.format` for want of §1.1 evidence, and that
          // decline STANDS. A `format` constrains the text a reader types into a
          // text box; this case is a different affordance entirely — a swatch
          // that opens the operating system's colour picker, which no `format`
          // on a `Text` field can produce. The row declined a VALIDATION
          // spelling; this admits a CONTROL, and the two do not overlap.
          //
          // `value` is `Binding<string>` carrying `#rrggbb` — the only form
          // `<input type="color">` can hold or return, so it is the wire form
          // too rather than a wider colour syntax the control would silently
          // narrow. A `Static` literal that is not that shape is refused at
          // DECODE (`Support.fs`, `CaseRefines`); every other binding shape
          // carries its text from somewhere the decoder cannot see, so the
          // format is re-checked by the pre-emit validator and enforced
          // server-side by the `FormValidation` floor. That split is recorded
          // rather than hidden: one rule, checked wherever the value becomes
          // visible.
          //
          // Case is PRESERVED, not normalised — `#FFFFFF` is a hex colour and
          // round-trips byte-identically. The browser normalises to lower case
          // on its own; the wire does not, because a codec that rewrote the
          // author's bytes would be the one thing the round-trip corpus exists
          // to forbid.
          { Tag = "Color"
            Fields =
              [ opt "onChange" (fieldChange "string")
                opt "value" (fieldValue TStr "dStr" "JStr") ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Fuaran-UI Phase 1121 — the MULTI-TOKEN input. `Combobox` commits ONE
          // value from a searchable set; `Tokens` accumulates SEVERAL, each
          // shown as a removable chip. Recipients, labels, skills: today
          // expressible only as a multi-`Select` over a closed set, which cannot
          // admit a token nobody listed in advance.
          //
          // THE CASE IS `Tokens` AND NOT `Tags`, AND THE REASON IS THE F#
          // COMPILER RATHER THAN A PREFERENCE. `Tags` is a RESERVED union-case
          // name in F#: the compiler generates a nested static class `Tags` in
          // every discriminated union to hold the case-tag constants, so a case
          // spelled that way is `FS1219` — "the union case named 'Tags'
          // conflicts with the generated type 'Tags'" — in ANY F# union, not
          // merely in this one. The phase was chartered as `FormFieldKind.Tags`
          // and the charter's reserved NAME is still `Tags`, which is what a
          // reader searches for; the SPELLING is corrected here on exactly the
          // ceremony `Color` took when it arrived as `ColorPicker`.
          //
          // The alternative — wire token `"Tags"`, F# case `Tokens` — was
          // considered and DECLINED. This IDL has no case↔wire split for union
          // cases at all (`enumWith` gives enums one; unions have none), so it
          // would be new machinery through the generator, the schema, the TS
          // emitter and the sampler, to produce exactly ONE case in the whole
          // vocabulary whose wire token no host's own source can spell. The
          // reference host generates the corpus; a token it cannot name is a
          // trap for every other host author and for every emitter that reads
          // this file as documentation. One name on both sides is worth more
          // than the domain word.
          //
          // `value` is `Binding<string list>`, which is not a new shape — the
          // `Select.values` multi-select slot has carried exactly that type
          // since Phase 291, through the same resolver and the same write-back.
          // So the ORDERED LIST is the wire form, and order is the reader's:
          // chips appear where they were added, and a codec that sorted them
          // would rewrite a fact the reader can see.
          //
          // `suggestions` is `Binding<SelectOption list>` and shares 1113's
          // machinery exactly — a `Query`-bound source IS the asynchronous
          // suggestion feed, resolved by binding machinery that already exists.
          // No coordination vocabulary is minted for it, and none is needed.
          //
          // THE POLARITY, AND WHY IT IS THE OPPOSITE OF `Combobox`'s. This is
          // the one decision in the case that a reader will stop at, so it is
          // recorded where it binds. `Combobox.allowFreeText` omits at `false`:
          // that case's `options` is REQUIRED, so a combobox ALWAYS has a set
          // and "constrained" is its resting state. Here `suggestions` is
          // OPTIONAL — a plain tag box with nothing to suggest is not a
          // degenerate `Tokens`, it is the COMMONEST one — so the resting state
          // is open, and `{"$type":"Tokens"}` is a complete, useful document
          // rather than a control that admits nothing. The default follows the
          // REQUIRED-NESS OF THE SET, which is one rule rather than two habits.
          //
          // What that buys, and what it costs. It buys a shortest document that
          // works: every open tag input in the world would otherwise have to
          // carry `"allowFreeText":true` as ceremony. It costs one member name
          // defaulting differently on two cases — visible in the omit-at-default
          // table, and stated normatively in the spec — which is the price of
          // the shortest document being the useful one on BOTH cases rather
          // than on one.
          //
          // `allowFreeText = false` WITH NO `suggestions` is refused at DECODE
          // (`Support.fs`, `CaseRefines`): the field could admit nothing at all,
          // by any gesture, ever — a document naming a control that cannot
          // exist, which is the `Rating.max < 1` line. Under this polarity it is
          // reachable only DELIBERATELY, which is what makes refusing it right
          // rather than hostile.
          //
          // What is NOT refused at decode: duplicates in the token list, and
          // membership of a token in the suggestion set. Both are properties of
          // the VALUE, and a bound value is invisible to a decoder — a rule
          // enforced only on literals would be two rules wearing one name. They
          // are held where the value becomes visible instead: FUARAN135 /
          // FUARAN134 for an author, and the server-driven submission floor for
          // a client, which is the only one that is a trust boundary. Exactly
          // the split `Rating`'s value bounds take.
          //
          // Nothing here names a keystroke. Enter, Backspace, Delete, the chip
          // row and the suggestion popup are the renderer's affordance under
          // the affordance→op charter.
          { Tag = "Tokens"
            Fields =
              [ omit "allowFreeText" TBool (VBool true)
                opt "onChange" (fieldChange "string[]")
                opt "suggestions" (TUnion("Binding", [ TList(TRecord "SelectOption") ]))
                opt "value" (fieldValue (TList TStr) "(dList dStr)" "(fun __l -> JArr(List.map JStr __l))") ]
            Annotations = Annotations.Empty
            Hosted = None } ]
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.CaseName
      Discrimination = UnionDiscrimination.Tagged }

// _(The separate `FilterKind` union this file carried until the Phase 692
// gap-closure was pre-unification drift: the hand-written tier's `FilterSpec`
// holds a `FormFieldKind` — one control vocabulary for forms and filter strips
// since the 0.2.0 filters-unification. `filters-declarative`'s Range chip is
// what surfaced it.)_

/// `MediaKind` — WHICH media surface a `Media` node is (Fuaran-UI Phase 1076).
///
/// One kind with a variant union, never two kinds: the vocabulary charter's
/// Appendix A `Media` row pre-ruled the shape, and the reasoning is visible in
/// the declaration itself. Everything a video and an audio element share —
/// the source, the accessible label, the transport, the repeat — lives ONCE on
/// `MediaSpec`; the only slots that differ are video-only, and they live here,
/// on the `Video` case. An `Audio` case with no fields is not a poorer twin of
/// `Video`, it is the honest statement that an audio surface has nothing extra
/// to say.
///
/// `autoplay` is video-only BY CONSTRUCTION, and its absence from `Audio` is
/// the design rather than an omission: an audio surface that starts itself is a
/// defect shape, so the knob does not exist rather than defaulting to off. It
/// is an omit-at-default bool on `Video`, so a video that does not ask for it
/// costs no key — the `Toast.dismissable` / `Image.expandable` precedent, with
/// the polarity of the latter.
///
/// `poster` is a full `Binding<string>` for the same reason `MediaSpec.src` is,
/// and it routes through the same render-time URL floor: a poster frame is a
/// URL the browser fetches with no user act, which is the whole of the `Media`
/// egress class.
let private mediaKind =
    { Name = "MediaKind"
      Params = []
      Cases =
        [ { Tag = "Video"
            Fields =
              [ omit "autoplay" TBool (VBool false)
                opt "poster" (TUnion("Binding", [ TStr ])) ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Audio"
            Fields = []
            Annotations = Annotations.Empty
            Hosted = None } ]
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.CaseName
      Discrimination = UnionDiscrimination.Tagged }

/// `TrackKind` — what a `Media` timed-text track IS (Fuaran-UI Phase 1110).
///
/// Four kinds, and deliberately only four: they are the set a user agent already
/// distinguishes, each with its own presentation and its own place in the track
/// menu. `Subtitles` translate dialogue for a reader who cannot follow the
/// language; `Captions` transcribe dialogue AND the non-speech sound a reader who
/// cannot hear it would otherwise lose — which is why the two are separate kinds
/// rather than one kind carrying a flag. `Descriptions` narrate what is visible
/// for a reader who cannot see it; `Chapters` name the navigable sections.
///
/// Deliberately NOT modelled: a `metadata` kind. Its cues are rendered by no user
/// agent and read only by script, so a declarative document naming it would state
/// an intent no host can honour without leaving the vocabulary — the `srcSet`
/// x-descriptor ruling, applied to a track menu.
let private trackKind =
    Declare.enumOf "TrackKind" [ "Subtitles"; "Captions"; "Descriptions"; "Chapters" ]

/// `ColumnWidth` — a `DataGrid` column's sizing intent.
let private columnWidth =
    { Name = "ColumnWidth"
      Params = []
      Cases =
        [ { Tag = "Auto"
            Fields = []
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Fixed"
            Fields = [ req "pixels" TInt ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Flex"
            Fields = [ req "weight" TFloat ]
            Annotations = Annotations.Empty
            Hosted = None } ]
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.CaseName
      Discrimination = UnionDiscrimination.Tagged }

/// `CellKindErased<'Msg>` — the row-erased grid-cell shape union. Non-interactive
/// cases (`Text` / `Numeric` / `Date`) are field-less; the interactive ones carry
/// closure accessors (`get` / `onEdit` / `onClick` / `fractionFn` …). `ButtonGroup`
/// carries a list of `ButtonGroupItem` records. `TonedPill` (Fuaran-UI Phase 750)
/// is the one WIRE-EXPRESSIBLE interactive-ish case — all data, no closure.
let private cellKindErased =
    { Name = "CellKindErased"
      Params = []
      Cases =
        [ { Tag = "Text"
            Fields = []
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Numeric"
            Fields = []
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Date"
            Fields = []
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Editable"
            // `(Row * CellValue) -> Action<'Msg>`; `CellValue` is a host-prelude DU
            // (stage 4b) — the typed edit payload survives the swap. Row closures
            // take `Map<string, obj>` since fuaran#665 (the rows slot is typed, so
            // the accessors' argument is the name-addressable row, not `obj`).
            Fields =
              [ opt "onEdit" (handlerOf "Map<string, obj> * Fuaran.UI.HostPrelude.CellValue" "[unknown, unknown]") ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Checkbox"
            Fields =
              [ req "get" (projOf "Map<string, obj>" "bool" "(row: unknown) => boolean" "(fun _ -> false)")
                opt "onToggle" (handlerOf "Map<string, obj> * bool" "[unknown, boolean]") ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Button"
            Fields =
              [ req "label" (TUnion("TextSource", []))
                opt "onClick" (handlerOf "Map<string, obj>" "unknown") ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "ButtonGroup"
            Fields = [ req "buttons" (TList(TRecord "ButtonGroupItem")) ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Link"
            Fields =
              [ req "hrefFn" (projOf "Map<string, obj>" "string" "(row: unknown) => string" "(fun _ -> \"\")")
                req
                    "labelFn"
                    (projOf
                        "Map<string, obj>"
                        "TextSource"
                        "(row: unknown) => TextSource"
                        "(fun _ -> TextSource.Literal \"\")") ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Pill"
            Fields =
              [ req
                    "labelFn"
                    (projOf
                        "Map<string, obj>"
                        "TextSource"
                        "(row: unknown) => TextSource"
                        "(fun _ -> TextSource.Literal \"\")")
                req
                    "toneFn"
                    (projOf
                        "Map<string, obj>"
                        "ToneVariant"
                        "(row: unknown) => ToneVariant"
                        "(fun _ -> ToneVariant.Default)") ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Fuaran-UI Phase 750 — the WIRE-EXPRESSIBLE pill. `Pill` above is a pair
          // of closures, so its whole meaning erases to two `"<closure>"` sentinels
          // and "distinguish the delayed rows" is inexpressible in canonical JSON —
          // an author with no host code cannot say it at all. `TonedPill` says the
          // same thing as DATA: `field` names the row property that is both the
          // pill's label and the map key, `map` carries value → `ToneVariant`, and
          // `default` tones a value the map does not mention (omitted at
          // `ToneVariant.Default`, the Phase 460 discipline). The closure case stays
          // — the two coexist exactly as a hosted row feed coexists with
          // `StaticRows`, and a host that already projects a tone keeps doing so.
          { Tag = "TonedPill"
            Fields =
              [ req "field" TStr
                req "map" (TMap(TEnum "ToneVariant"))
                omit "default" (TEnum "ToneVariant") (VEnum "Default") ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Progress"
            Fields =
              [ req "fractionFn" (projOf "Map<string, obj>" "float" "(row: unknown) => number" "(fun _ -> 0.0)")
                // The hand-written tier's label is genuinely optional (a progress
                // cell with no label) — `opt`, stage 4b. The hand encoder emitted
                // an unconditional sentinel; omit-when-None is the honest form and
                // no fixture pins the None-label emission.
                opt
                    "labelFn"
                    (projOf
                        "Map<string, obj>"
                        "TextSource"
                        "(row: unknown) => TextSource"
                        "(fun _ -> TextSource.Literal \"\")") ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Custom"
            // `(Row -> JVal) -> Node<'Msg>` — a cell renderer over the row projector.
            Fields =
              [ req
                    "fn"
                    (fn
                        "(Map<string, obj> -> JVal) -> Node<'Msg>"
                        "(proj: (row: unknown) => unknown) => Node"
                        "(fun _ -> Unchecked.defaultof<Node<obj>>)") ]
            Annotations = Annotations.Empty
            Hosted = None } ]
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.CaseName
      Discrimination = UnionDiscrimination.Tagged }

// ─── Meta-family unions (parameterised fragments) ───────────────────────────

/// A hole's value-space (bind-time validation domain) on a `FragmentDecl`.
let private holeValueSpace =
    { Name = "HoleValueSpace"
      Params = []
      Cases =
        // Hand-written positional order (min before max) — wire-free.
        [ { Tag = "IntRange"
            Fields = [ req "min" TInt; req "max" TInt ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "FloatRange"
            Fields = [ req "min" TFloat; req "max" TFloat ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "StringLen"
            Fields = [ req "minLen" TInt; req "maxLen" TInt ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Enum"
            Fields = [ req "choices" (TList TStr) ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "AnyString"
            Fields = []
            Annotations = Annotations.Empty
            Hosted = None } ]
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.CaseName
      Discrimination = UnionDiscrimination.Tagged }

/// A boxed scalar — a hole default or a `FragmentRef` value arg. Self-describing
/// (`$type` pins the CLR shape).
let private scalar =
    { Name = "Scalar"
      Params = []
      Cases =
        [ { Tag = "Int"
            Fields = [ req "value" TInt ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Float"
            Fields = [ req "value" TFloat ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Bool"
            Fields = [ req "value" TBool ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Str"
            Fields = [ req "value" TStr ]
            Annotations = Annotations.Empty
            Hosted = None } ]
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.CaseName
      Discrimination = UnionDiscrimination.Tagged }

/// A declared hole on a parameterised fragment (`FragmentDecl.holes`).
let private holeDecl =
    { Name = "HoleDecl"
      Params = []
      Cases =
        // Hand-written positional order (name first) — wire-free.
        [ { Tag = "Value"
            Fields =
              [ req "name" TStr
                req "space" (TUnion("HoleValueSpace", []))
                opt "default" (TUnion("Scalar", [])) ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Slot"
            Fields = [ req "name" TStr; opt "kindConstraint" TStr ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Repeat"
            Fields = [ req "name" TStr; req "countSpace" (TUnion("HoleValueSpace", [])) ]
            Annotations = Annotations.Empty
            Hosted = None } ]
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.CaseName
      Discrimination = UnionDiscrimination.Tagged }

/// A bound argument at a `FragmentRef` — a scalar value or a slot subtree. Shares
/// the scalar tags with [[scalar]] plus `SlotArg` (a `Node`-bearing tree).
let private fragmentArg =
    { Name = "FragmentArg"
      Params = []
      Cases =
        [ { Tag = "Int"
            Fields = [ req "value" TInt ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Float"
            Fields = [ req "value" TFloat ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Bool"
            Fields = [ req "value" TBool ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Str"
            Fields = [ req "value" TStr ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "SlotArg"
            Fields = [ req "tree" TNode ]
            Annotations = Annotations.Empty
            Hosted = None } ]
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.CaseName
      Discrimination = UnionDiscrimination.Tagged }

// ─── Records (non-discriminated objects — no `$type`) ───────────────────────

let private invokeArgRecord =
    { Name = "InvokeArg"
      Fields = [ req "addr" TStr; req "value" TStr ]
      Annotations = Annotations.Empty
      Inline = false }

/// Phase 2517 — the AFFORDANCE a field-bearing node declares: how an agent-initiated
/// instruction may address it, over and above what `Operability.derive` reads off the
/// node itself (its name, label and value space). Every member is omit-at-default, so
/// `{}` is the "declares nothing extra" affordance and a node with no affordance at all
/// is still derived from its label. `patterns` are SET forms (a `{value}` slot names
/// where the instruction's value goes); `readPatterns` are question forms that resolve
/// only to a `Read`; `aliases` map a spoken token onto a canonical option value.
/// `neverControllable` withdraws the node from the operable surface entirely — the
/// derivation yields nothing for it. WIRE_FORMAT §31.
let private affordanceAliasRecord =
    { Name = "AffordanceAlias"
      Fields = [ req "alias" TStr; req "value" TStr ]
      Annotations = Annotations.Empty
      Inline = false }

let private affordanceRecord =
    { Name = "Affordance"
      Fields =
        [ omit "patterns" (TList TStr) (VList [])
          omit "readPatterns" (TList TStr) (VList [])
          omit "aliases" (TList(TRecord "AffordanceAlias")) (VList [])
          opt "description" TStr
          omit "clearable" TBool (VBool false)
          omit "neverControllable" TBool (VBool false) ]
      Annotations = Annotations.Empty
      Inline = false }

/// An option in a `Select` / `Choice` / `SegmentedChoice` payload (Fuaran-UI 0.2.x
/// typed-Static: the choice `source` / `options` carry a real `SelectOption` list).
let private selectOptionRecord =
    { Name = "SelectOption"
      Fields = [ req "label" TStr; req "value" TStr ]
      Annotations = Annotations.Empty
      Inline = false }

/// Phase 2571 — a named group of a `Select`'s options (WIRE_FORMAT §3.6.30). The
/// grouping lives on the `Select` spec and names options by `value`, never on
/// `SelectOption`: `SelectOption` is built as a full record literal across the
/// estate, so a member there would break every construction.
let private selectOptionGroupRecord =
    { Name = "SelectOptionGroup"
      Fields = [ req "label" TStr; req "values" (TList TStr) ]
      Annotations = Annotations.Empty
      Inline = false }

/// A `Map.source` marker (Fuaran-UI 0.2.x typed-Static: the map source carries a real
/// marker list instead of the opaque sentinel).
let private mapMarkerRecord =
    { Name = "MapMarker"
      Fields = [ req "label" TStr; req "latitude" TFloat; req "longitude" TFloat ]
      Annotations = Annotations.Empty
      Inline = false }

/// Sort direction on a static table's declared initial order — closed, and
/// lower-case on the wire (Fuaran-UI Phase 801). Case↔wire mapping for the same
/// reason `LiveRegionKind` carries one: the wire vocabulary is lower-case and the
/// F# case names are not.
let private sortDirection =
    Declare.enumWith "SortDirection" [ "Asc", "asc"; "Desc", "desc" ]

/// `{ "column": <header index>, "direction": "asc" | "desc" }` — a static table's
/// declared INITIAL order (Fuaran-UI Phase 801). Both fields are required *within*
/// the record; the record itself is an optional slot on `StaticRows`, so a table
/// that declares no initial order carries no `defaultSort` key at all.
///
/// `column` indexes `StaticRows.headers`. The IDL cannot state the non-negativity
/// bound (there is no refined-integer type), so the decode-side rejection of a
/// negative index lives in the policy decoder and the published JSON Schema.
let private defaultSortRecord =
    { Name = "DefaultSort"
      Fields = [ req "column" TInt; req "direction" (TEnum "SortDirection") ]
      Annotations = Annotations.Empty
      Inline = false }

/// A `DataGrid.staticRows` payload — the header/row grid a legacy `Table` decode-upgrades
/// into (Fuaran-UI Phase 393: `Table` retired, becomes a static `DataGrid`). Cells are
/// `TextSource`, NOT bare strings: the hand codec encodes each cell via `encodeTextSource`
/// (a `Literal` IS the bare string on the wire — 0.2.0) and the decoder accepts `Bound` /
/// `I18n` objects per cell, so a `TStr` here would narrow live wire fidelity (stage 4b).
///
/// Phase 801 adds two OPTIONAL sort-intent slots — `sortable` (this table invites
/// interactive column sorting) and `defaultSort` (its initial order). Both are
/// `Optional` rather than `OmitDefault`, so absence is absence: a table authored
/// before the addition encodes byte-identically, which is the phase's hard
/// constraint. The declaration is INTENT, not a behaviour guarantee — a host
/// honours it with whatever sorting affordance it has.
///
/// Phase 2592 adds `citations`, the cells whose figures name their Facts (WIRE_FORMAT.md
/// §3.6.33). Omitted when empty, so a table citing nothing is byte-unchanged.
let private staticRowsRecord =
    { Name = "StaticRows"
      Fields =
        [ omit "citations" (TList(TRecord "TableCitation")) (VList [])
          opt "defaultSort" (TRecord "DefaultSort")
          req "headers" (TList(TUnion("TextSource", [])))
          req "rows" (TList(TList(TUnion("TextSource", []))))
          opt "sortable" TBool ]
      Annotations = Annotations.Empty
      Inline = false }

/// The comparison operand of a cross-field `FieldRule` (Fuaran-UI Phase 864).
///
/// `against` is a `Binding` at `JVal` — the typed verbatim carrier, the same
/// instantiation `TransformParam.from` uses for a slot whose value type is
/// whatever the compared control holds. That it is a Binding at all is the whole
/// cross-field mechanism: the reactive-derivation rule (any read slot may take a
/// Binding) plus the auto-bind rule (a form field's absent `value` binds
/// `State(<field id>)`) means `{"$type":"State","key":"<sibling id>"}` reads the
/// sibling field's live value with no coordination vocabulary at all.
///
/// The slot has no literal form ON PURPOSE. A literal-only operand would be
/// `Date.min` again, and the charter's reuse rule forbids the rule slot
/// duplicating a bound the control already carries.
let private compareRuleRecord =
    { Name = "CompareRule"
      Fields = [ req "against" (TUnion("Binding", [ TJson ])); req "op" (TEnum "CompareOp") ]
      Annotations = Annotations.Empty
      Inline = false }

/// A `FormField`'s declared constraint (Fuaran-UI Phase 864) — the accepted SET,
/// where `FormFieldKind` names the CONTROL. Every slot is `Optional`, so a form
/// authored before the addition encodes byte-identically: absence is absence.
///
/// **No numeric or temporal bound lives here.** `RangedNumber` already carries
/// `min`/`max` and `DateTime` already carries `min`/`max`; the charter's reuse rule
/// is that the rule slot never duplicates a bound the control carries. What is
/// left is format, pattern, length, and the cross-field operand.
///
/// A rule with EVERY slot absent is refused by the tier's policy decoder — a
/// rule that constrains nothing is a defect, not a no-op — as is a `minLength`
/// above its `maxLength` (the `DateTimeRangePair` ordered-pair rule applied to a
/// length pair). Both are decoder POLICY, not structure, so they live in the
/// tier's reject layer and not here, exactly as the `from <= to` rule does.
let private fieldRuleRecord =
    { Name = "FieldRule"
      Fields =
        [ opt "compare" (TRecord "CompareRule")
          opt "format" (TEnum "TextFormat")
          opt "maxLength" TInt
          opt "message" (TUnion("TextSource", []))
          opt "minLength" TInt
          opt "pattern" TStr ]
      Annotations = Annotations.Empty
      Inline = false }

/// Phase 864 adds one OPTIONAL `rule` slot. `required` stays where it is: it is
/// the pre-existing degenerate rule, and moving it under `rule` would be a
/// breaking change to a field every existing fixture carries.
let private formFieldRecord =
    { Name = "FormField"
      Fields =
        [ req "id" TStr
          req "kind" (TUnion("FormFieldKind", []))
          req "label" (TUnion("TextSource", []))
          omit "required" TBool (VBool false)
          opt "help" (TUnion("TextSource", []))
          opt "rule" (TRecord "FieldRule")
          // Phase 2517 — appended, absent-at-`None`: every form written before it
          // encodes to the bytes it always did. WIRE_FORMAT §31.
          opt "affordance" (TRecord "Affordance") ]
      Annotations = Annotations.Empty
      Inline = false }

let private filterSpecRecord =
    { Name = "FilterSpec"
      Fields =
        [ req "kind" (TUnion("FormFieldKind", []))
          req "label" (TUnion("TextSource", []))
          req "name" TStr ]
      Annotations = Annotations.Empty
      Inline = false }

/// One `Binding.Transform` parameter — binds a pipeline `ColExpr.Param` name to a
/// scalar binding source. `from` instantiates `Binding` at `JVal` (the typed
/// verbatim carrier for obj-erased positions).
let private transformParamRecord =
    { Name = "TransformParam"
      Fields = [ req "from" (TUnion("Binding", [ TJson ])); req "name" TStr ]
      Annotations = Annotations.Empty
      Inline = false }

/// The `{max, min}` payload of a `Range` control's value — the wire shape of the
/// hand-written tier's `(min, max)` float pair (the IDL has no tuple type; the
/// record IS the wire object, so nothing is lost in the trade).
let private rangePairRecord =
    { Name = "RangePair"
      Fields = [ req "max" TFloat; req "min" TFloat ]
      Annotations = Annotations.Empty
      Inline = false }

/// The `{from, to}` payload of a `DateTimeRange` control's value (Fuaran-UI
/// Phase 725; renamed from `DateRangePair` by Phase 1811) — the ordered
/// ISO-8601 pair, `RangePair`'s record-IS-the-wire-object trade for the
/// hand-written tier's `(from, to)` string pair. The ordering rule (`from` ≤
/// `to`, ordinal) is decoder POLICY, not structure — it lives in the tier's
/// lenient/reject layer, not here.
let private dateTimeRangePairRecord =
    { Name = "DateTimeRangePair"
      Fields = [ req "from" TStr; req "to" TStr ]
      Annotations = Annotations.Empty
      Inline = false }

let private tabHeaderRecord =
    { Name = "TabHeader"
      Fields =
        [ req "label" (TUnion("TextSource", []))
          opt "icon" TStr
          opt "disabled" (TUnion("Binding", [ TBool ])) ]
      Annotations = Annotations.Empty
      Inline = false }

/// Fuaran-UI Phase 2472 — one further comparison on a `Metric`: a figure measured
/// against a second (or third) period at once — yesterday against the same weekday
/// last week AND last year. `Metric.trend` stays the primary comparison and is
/// untouched; each entry here renders as one more trend line under it, with its
/// own glyph, sentiment and polarity, and its own LABEL — which the primary trend
/// has never needed, since a tile with one comparison names it in `subtext`.
///
/// Irreducible under the charter's §1.2: two `Metric`s side by side repeat the
/// headline figure as two KPIs; a `Badge` beside the tile carries a static tone
/// that cannot be a function of a bound value's sign (the Phase 867 ground); and
/// `subtext` over a formatted binding is text, so it loses the glyph, the
/// sentiment and the polarity — which is exactly what the pilot lost. A record
/// FIELD on the kind the author already chose is the §2.1 tier: no new choice.
///
/// `label` is REQUIRED: an unlabelled second comparison is ambiguous against the
/// first. `format` omits at `None` (the `Metric.format` spelling) and `polarity`
/// at `HigherIsBetter` (the `Metric.trendPolarity` default), so a comparison
/// declared with only what it must say is three keys on the wire.
let private metricComparisonRecord =
    { Name = "MetricComparison"
      Fields =
        [ req "label" (TUnion("TextSource", []))
          req "value" (TUnion("Binding", [ TFloat ]))
          omit "format" (TUnion("CellFormat", [])) (VUnion("None", []))
          omit "polarity" (TEnum "TrendPolarity") (VEnum "HigherIsBetter") ]
      Annotations = Annotations.Empty
      Inline = false }

/// Phase 2591 — one figure a `Markdown` text cites. `name` is what the text's `cite:<name>`
/// link destination says; `reference` is the same opaque value `Fact.reference` carries
/// (WIRE_FORMAT.md §3.6.31); `label` says what the figure is, for a screen reader, and is
/// never printed. The figure's display text is the link's own text, so it lives in the
/// sentence. The name's grammar and uniqueness are refused at DECODE (the IDL has no refined
/// string type), which is why those reject fixtures sit in the policy-only list.
let private markdownCitationRecord =
    { Name = "MarkdownCitation"
      Fields =
        [ opt "label" (TUnion("TextSource", []))
          req "name" TStr
          req "reference" TStr ]
      Annotations = Annotations.Empty
      Inline = false }

/// Phase 2592 — one cell of a static table that cites its figure. `row` and `column` are
/// zero-based coordinates into `StaticRows.rows`, so the cell's text stays exactly as `rows`
/// holds it and no placeholder is needed; `reference` and `label` are the members of the same
/// name on `MarkdownCitation`, with no record shared, since one would carry both a name and
/// coordinates with one always absent. Range and uniqueness are refused at DECODE (the IDL has
/// no relation across fields), which is why those reject fixtures sit in the policy-only list.
let private tableCitationRecord =
    { Name = "TableCitation"
      Fields =
        [ req "column" TInt
          opt "label" (TUnion("TextSource", []))
          req "reference" TStr
          req "row" TInt ]
      Annotations = Annotations.Empty
      Inline = false }

/// A `DataGrid` column, row-erased. Fuaran-UI Phase 425: `value` (the projection
/// closure) and `field` (the declarative row-property name) are SIBLING optional
/// slots, each omitted-when-None — a closure-authored column keeps
/// `"value":"<closure>"` byte-stable, a decoded/field-named column carries
/// `"field":"…"` instead. `format` / `width` omitted-when-default (Phase 460).
let private columnErasedRecord =
    { Name = "ColumnErased"
      Fields =
        [ opt "field" TStr
          // Phase 861 — per-column sort NARROWING on the bound path (the Phase 860
          // charter rule: a column flag narrows a behaviour, never widens it).
          // Absent = inherit; `false` opts this column out; `true` is the inherited
          // default made explicit and is an error where the grid declares no
          // `sortStateKey`. That grounding is a DECODER-POLICY rule, not a shape
          // rule, so it stays hand-written above this layer.
          opt "sortable" TBool
          // Phase 863 — per-column EDITABILITY narrowing, the same rule on the
          // write side. Absent = inherit the grid-level `editable`.
          opt "editable" TBool
          omit "format" (TUnion("CellFormat", [])) (VUnion("None", []))
          req "kind" (TUnion("CellKindErased", []))
          req "label" TStr
          // `Row -> CellValue`; `CellValue` is a host DU declared in the host
          // prelude (stage 4b) — the typed cell surface survives the swap; the
          // row argument is typed `Map<string, obj>` since fuaran#665.
          opt
              "value"
              (projOf
                  "Map<string, obj>"
                  "Fuaran.UI.HostPrelude.CellValue"
                  "(row: unknown) => unknown"
                  "(fun _ -> Fuaran.UI.HostPrelude.CellValue.Empty)")
          omit "width" (TUnion("ColumnWidth", [])) (VUnion("Auto", [])) ]
      Annotations = Annotations.Empty
      Inline = false }

/// One button of a `CellKindErased.ButtonGroup` (`onClick` is a closure over the row).
let private buttonGroupItemRecord =
    { Name = "ButtonGroupItem"
      Fields =
        [ req "label" (TUnion("TextSource", []))
          opt "onClick" (handlerOf "Map<string, obj>" "unknown") ]
      Annotations = Annotations.Empty
      Inline = false }

/// A `Custom` node's content-identity envelope (`strictness` is a bare-string DU).
let private contentHashRecord =
    { Name = "ContentHash"
      Fields =
        [ req "algorithm" TStr
          req "hash" TStr
          req "strictness" (TEnum "HashStrictness") ]
      Annotations = Annotations.Empty
      Inline = false }

/// Phase 1080 — one candidate source of a responsive `Image`. `width` is the
/// intrinsic pixel width of THIS candidate (the `w` descriptor a browser picks
/// from), and `src` is a full `Binding<string>` for the same reason the primary
/// `src` is: a candidate can come from a query or a computed value, not only a
/// literal path.
///
/// `width` must be POSITIVE. The IDL has no refined-integer type (the
/// `DefaultSort.column` precedent above), so that floor lives in the policy
/// decoder and the published JSON Schema, and the corpus carries a reject vector
/// for it — which is what makes it a wire rule rather than one host's opinion.
///
/// Deliberately NOT modelled: an `x`-descriptor (device-pixel-ratio) form, and a
/// per-entry media condition. Both are alternative candidate-selection algebras,
/// and admitting either alongside `w` would make a `srcset` list heterogeneous —
/// a browser refuses a mixed list outright, so the wire would be able to state a
/// document no host can render.
let private srcSetEntryRecord =
    { Name = "SrcSetEntry"
      Fields = [ req "src" (TUnion("Binding", [ TStr ])); req "width" TInt ]
      Annotations = Annotations.Empty
      Inline = false }

/// Phase 1110 — one timed-text track of a `Media` node. The list-of-records shape
/// follows the `SrcSetEntry` precedent above, and for the same reason: a repeated
/// structured slot is a record list, never a parallel family of flat fields.
///
/// `srcLang` is REQUIRED, and that is the one place this record is stricter than
/// the element it renders to. HTML makes `srclang` mandatory on a subtitles track
/// and optional elsewhere; making it mandatory here for every kind costs an
/// author one attribute and buys a track menu whose entries a user agent can
/// order, a speech engine can pronounce, and a reader can tell apart. A track
/// with no language is one nothing downstream can route.
///
/// `label` is a `TextSource` rather than a bare string because it is CONTENT — it
/// is the text a user agent puts in its track menu, so it is i18n-capable on the
/// same terms as every other authored string (the `Image.caption` ruling). It is
/// required for the same reason `MediaSpec.label` is: an unlabelled track is
/// announced by its kind alone, which tells a reader that a captions track exists
/// and nothing about which one it is.
///
/// `src` is a full `Binding<string>` and routes through the same render-time URL
/// floor `MediaSpec.src` and `MediaKind.Video.poster` do — a track file is
/// fetched by the browser with no user act, which is the whole of the `Media`
/// egress class.
///
/// `default` omits at `false`, the ordinary polarity. It is a per-KIND election
/// rather than a per-node one, and that constraint is a render obligation rather
/// than a decode rule: a document electing two default captions tracks is legal
/// bytes that no user agent can honour, so the host resolves it deterministically
/// (first wins) instead of the decoder refusing a shape a lenient host would
/// simply render.
let private trackEntryRecord =
    { Name = "TrackEntry"
      Fields =
        [ omit "default" TBool (VBool false)
          req "kind" (TEnum "TrackKind")
          req "label" (TUnion("TextSource", []))
          req "src" (TUnion("Binding", [ TStr ]))
          req "srcLang" TStr ]
      Annotations = Annotations.Empty
      Inline = false }

/// Phase 1120 — one row of a `Tree`, and the vocabulary's first SELF-REFERENTIAL
/// record: `children` is a list of the record being declared. The IDL's record
/// references are nominal (`TRecord "TreeItem"` names a record, it does not
/// inline one), and every generated type, encoder and decoder lands in one
/// mutually-recursive group, so the recursion needs no new machinery on either
/// side — it needed only to be the shape the tree actually has.
///
/// `id` is REQUIRED and is the row's identity in the two State keys the spec
/// carries: the expanded set names ids, and the selection names an id. An
/// item with no id is a row nothing can name, which is a row that cannot be
/// expanded, selected or restored — so unlike `TabHeader` (identified by
/// position) this record cannot make identity optional. Uniqueness within one
/// `Tree` is a validator rule rather than a decode rule, on `NodeId`'s own
/// §8.1 precedent: the decoder judges shape, the pre-emit gate judges sense.
///
/// `label` is a `TextSource` and not a bare string for `TrackEntry.label`'s
/// reason exactly — it is CONTENT: authored, translated, and possibly bound.
///
/// `children` omits at the EMPTY LIST, so a leaf carries no `children` key at
/// all. That is the ordinary polarity for a list slot (`srcSet`, `tracks`,
/// `permissions`) and it matters more here than in any of them, because a tree
/// is mostly leaves: the omission is the difference between a leaf costing two
/// keys and costing three, multiplied by every row of a file listing.
///
/// **There is deliberately no `expandable` boolean and no per-item `expanded`
/// flag.** Whether a row can be opened is derivable — a row with children can —
/// and whether it IS open is the grid-behaviour cluster's governing ruling: a
/// behaviour the reader drives is a named State key the host both reads and
/// writes, never a per-node shadow copy that the key and the tree are then free
/// to disagree about.
///
/// `icon` is the same optional string slot every other icon-bearing record
/// carries, and it is presentational: a host with no icon set renders the row
/// without one and loses nothing a reader needs.
let private treeItemRecord =
    { Name = "TreeItem"
      Fields =
        [ omit "children" (TList(TRecord "TreeItem")) (VList [])
          opt "icon" TStr
          req "id" TStr
          req "label" (TUnion("TextSource", [])) ]
      Annotations = Annotations.Empty
      Inline = false }

/// A `FragmentDecl`'s two-axis effect class (omitted on the wire when pure +
/// deterministic — since Phase 1670 that is the KIND'S OMIT-AT-DEFAULT rather
/// than an optional field the author is trusted to leave alone).
let private effectClassRecord =
    { Name = "EffectClass"
      Fields =
        [ req "determinism" (TEnum "DeterminismSource")
          req "hostEffect" (TEnum "HostEffect") ]
      Annotations = Annotations.Empty
      Inline = false }

// ─── Type aliases for the field shapes (readability) ───────────────────────

let private TS = TUnion("TextSource", [])
let private bindingOf (t: IdlType) = TUnion("Binding", [ t ])
let private CF = TUnion("CellFormat", [])
/// `IconSource` is a bare string on the wire (`"icon":"trending-up"`).
let private icon = TStr
/// Phase 2535 — the citation slot: the reference naming the Fact behind a figure, a bare
/// string on the wire. The language carries it and never parses or resolves it; a renderer
/// stamps it as `data-fact`. Its form and the reasoning are WIRE_FORMAT.md §3.6.31. A kind
/// that cites a figure takes this member, so every citation shares one documented value.
let private factReference = opt "reference" TStr

// ─── The node envelope records (Phase 690, WIRE_FORMAT.md §3.1) ────────────
//
// Field ORDER is Ordinal throughout — the TS backend emits in declared order and
// does not sort, so a declaration out of Ordinal order diverges the two hosts.

/// `{ "direction"?, "emphasis"?, "role"?, "tone"?, "voice"?, "weight"? }` — every
/// field individually omit-when-default (Fuaran-UI Phase 147 role/voice, Phase 460
/// the other three, Phase 1472 `direction`), and the whole object omitted when all
/// six are default.
///
/// `direction` is the odd one out among these fields and the difference is worth
/// naming: the other five are PRESENTATION — a host that ignores every one of them
/// still renders a document that says the same thing. `direction` is CORRECTNESS.
/// A value declared `Ltr` inside right-to-left prose is reordered by the
/// bidirectional algorithm unless the run is isolated, and the reader then reads
/// the digits back in the wrong order. It lives here rather than as a sixth trait
/// on the node envelope because it is a property OF the value the envelope wraps,
/// the same tier `emphasis` and `voice` occupy, and because `style` already
/// reaches every node and every `UpdateStyle` op.
let private semanticStyleRecord =
    { Name = "SemanticStyle"
      Fields =
        // A `VEnum` names the WIRE string (Fuaran.Core 0.34.0 resolves it to the host case
        // through the enum's mapping and refuses a string the enum does not admit).
        [ omit "direction" (TEnum "TextDirection") (VEnum "auto")
          omit "emphasis" (TEnum "Emphasis") (VEnum "Normal")
          omit "role" (TEnum "StyleRole") (VEnum "None")
          omit "tone" (TEnum "ToneVariant") (VEnum "Default")
          omit "voice" (TEnum "FontVoice") (VEnum "Default")
          omit "weight" (TEnum "StyleWeight") (VEnum "Standard") ]
      Annotations = Annotations.Empty
      Inline = false }

/// Phase 2291 — the `PatchStyle` payload. The same six members as
/// `SemanticStyle`, each OPTIONAL with no default: absence means "leave this
/// field as it is", so a present default token is a real edit and is never
/// omitted. Reachable from no kind and no envelope field — only from the op —
/// which is why the authoring mirrors do not see it. That a delta names at
/// least one field is decode POLICY above the IDL (the shape here admits `{}`).
let private styleDeltaRecord =
    { Name = "StyleDelta"
      Fields =
        [ opt "direction" (TEnum "TextDirection")
          opt "emphasis" (TEnum "Emphasis")
          opt "role" (TEnum "StyleRole")
          opt "tone" (TEnum "ToneVariant")
          opt "voice" (TEnum "FontVoice")
          opt "weight" (TEnum "StyleWeight") ]
      Annotations = Annotations.Empty
      Inline = false }

/// `{ "onEmpty"?: Node, "onError"?: "<closure>", "onLoading"?: Node }`.
/// `onError` is the `ErrorPayload -> Node` callback — unobservable, so the
/// sentinel, and its PRESENCE is the only thing the wire carries. The arg type
/// is the HOSTED `ErrorPayload` (defined in the consuming host's prelude — the
/// tier's `Fuaran.UI.HostPrelude`, stubbed identically in this test assembly)
/// so the swap does not erode the renderer-called closure to `obj`.
let private stateBehaviourRecord =
    { Name = "StateBehaviour"
      Fields =
        [ opt "onEmpty" TNode
          opt
              "onError"
              (fn
                  "Fuaran.UI.HostPrelude.ErrorPayload -> Node<'Msg>"
                  "(e: unknown) => Node"
                  "(fun _ -> Unchecked.defaultof<Node<obj>>)")
          opt "onLoading" TNode ]
      Annotations = Annotations.Empty
      Inline = false }

/// `aria-live` politeness — closed, and lower-case on the wire. Phase 707: the
/// lower-case half of the old "doesn't fit `TEnum`" excuse is gone, so this is a
/// declared enum with a case↔wire mapping rather than a host-owned codec. Case
/// order matches the tier's DU so the generated declaration lands identically.
let private liveRegionKind =
    Declare.enumWith "LiveRegionKind" [ "Polite", "polite"; "Assertive", "assertive"; "Off", "off" ]

/// `{ "describedBy"?, "hidden"?, "label"?, "labelledBy"?, "liveRegion"?, "role"?, "speak"? }`.
///
/// `speak` (Fuaran-UI Phase 1812) is the node's SPOKEN rendering for a voice
/// surface — a `TextSource`, because it is authored content that is translated,
/// exactly as `tooltip` is. It is inert to every visual renderer: it never
/// reaches `aria-label` (the accessible NAME is `label`, and a speech line that
/// leaked into it would rename the node for every screen reader), and it changes
/// no visual or ARIA output. Its consumer is the speech projection.
///
/// `role` stays `THosted`: `AriaRole` carries a `Custom of string` case that emits
/// its payload verbatim, so the wire position genuinely admits any string — the
/// set is OPEN, which no `TEnum` can model however its cases are spelled. The host
/// declares the DU + its codec in `Fuaran.UI.HostPrelude`, and everywhere else
/// (interpreter, TS, schema, sampler) the slot behaves as verbatim JSON exactly as
/// `TStr` did.
///
/// `liveRegion` was `THosted` for a DIFFERENT reason — its set is closed, and only
/// its lower-case wire strings were unspellable as `IdlEnum` cases. Phase 707 split
/// case name from wire string, so it is now a real `TEnum`: same bytes, and the
/// closed set is visible to the schema, the TS decoder and the sampler, which a
/// host-owned codec kept opaque to all three.
let private accessibilityRecord =
    { Name = "Accessibility"
      Fields =
        [ opt "describedBy" TStr
          opt "hidden" (bindingOf TBool)
          opt "label" (bindingOf TStr)
          opt "labelledBy" TStr
          opt "liveRegion" (TEnum "LiveRegionKind")
          opt
              "role"
              (THosted
                  { FSharp = "Fuaran.UI.HostPrelude.AriaRole"
                    Encode = "Fuaran.UI.HostPrelude.encAriaRole"
                    Decode = "Fuaran.UI.HostPrelude.decAriaRole"
                    Wire = None
                    Format = None
                    Refusal = HostedRefusal.Sentence })
          opt "speak" TS ]
      Annotations = Annotations.Empty
      Inline = false }

// ─── Display kinds (flat `$type`-discriminated) ────────────────────────────

let displayKinds: IdlKind list =
    [ { Tag = "Heading"
        Category = "Display"
        Annotations = Annotations.Empty
        // Phase 2300 — the declared-default rule (WIRE_FORMAT §3.6): every
        // `variant` whose authoring default is one of its cases omits at that
        // case. The declared default IS the `Defaults.fs` value, so a
        // default-authored node and an absent member mean the same thing.
        Fields =
          [ req "level" TInt
            req "text" TS
            omit "variant" (TEnum "HeadingVariant") (VEnum "Standard") ] }
      { Tag = "Badge"
        Category = "Display"
        Annotations = Annotations.Empty
        Fields = [ req "label" TS; omit "variant" (TEnum "BadgeVariant") (VEnum "Neutral") ] }
      { Tag = "Markdown"
        Category = "Display"
        Annotations = Annotations.Empty
        // Phase 2591 — the figures the text cites, each placed by a `cite:<name>` link
        // destination (WIRE_FORMAT.md §3.6.32). Omitted when empty, so a `Markdown` with
        // nothing to cite is byte-unchanged.
        Fields =
          [ omit "citations" (TList(TRecord "MarkdownCitation")) (VList [])
            req "text" TS ] }
      { Tag = "Math"
        Category = "Display"
        Annotations = Annotations.Empty
        Fields = [ req "source" TStr; req "display" (TEnum "MathDisplay") ] }
      { Tag = "Skeleton"
        Category = "Display"
        Annotations = Annotations.Empty
        Fields = [ req "rows" TInt ] }
      // Phase 821 — the standalone icon-only display kind: a decorative or
      // labelled glyph with no Button / Image envelope. `size` / `tone` carry
      // their defaults and are omitted at them; `label` absent is decorative
      // (`aria-hidden`), present is meaningful (`role="img"` + `aria-label`).
      { Tag = "Icon"
        Category = "Display"
        Annotations = Annotations.Empty
        Fields =
          [ req "icon" icon
            omit "size" (TEnum "IconSize") (VEnum "Medium")
            omit "tone" (TEnum "ToneVariant") (VEnum "Default")
            opt "label" TStr ] }
      { Tag = "List"
        Category = "Display"
        Annotations = Annotations.Empty
        Fields = [ req "items" (TList TS); omit "ordered" TBool (VBool false) ] }
      // Phase 1120 — the recursive-disclosure kind, admitted on the SEMANTICS
      // ground. It sits here beside `List`, in the Display family, because the
      // family line is drawn on whether a kind bears `Node` children: `Tree`
      // bears `TreeItem` ROWS, exactly as `List` bears `TextSource` items, so
      // it is `List`'s recursive sibling and not a second `Disclosure`.
      //
      // Both reader-driven behaviours are named State keys, per the
      // grid-behaviour cluster's governing ruling — the key IS the affordance.
      // There is no `expandable` and no `selectable` boolean, because a flag
      // with no key behind it is a decorative control writing state nothing
      // reads.
      { Tag = "Tree"
        Category = "Display"
        Annotations = Annotations.Empty
        Fields =
          [ opt "expandedStateKey" TStr
            req "items" (TList(TRecord "TreeItem"))
            opt "onSelect" (handlerOf "string" "string")
            opt "selectionStateKey" TStr ] }
      // Phase 1077 — the three presentation slots. Every one is
      // omitted-at-default on BOTH boundaries, so a pre-phase document (which
      // carries none of them) decodes and renders exactly as it did.
      //
      // Phase 1078 — `caption` is the fourth addition and the only one that is
      // NOT a presentation token: it is content, so it is an ordinary optional
      // field (omitted when absent, rule 4) rather than an identity default,
      // and it is a `TextSource` so a caption is i18n-capable on the same terms
      // as every other authored string. Present, it makes the renderers emit
      // `<figure>` / `<figcaption>`; absent, the emission is the bare `<img>`
      // it always was.
      //
      // Phase 1080 — `srcSet` is the fifth addition and the first REPEATED slot
      // on this record. It is omitted-at-default like the presentation tokens,
      // but its default is the EMPTY LIST rather than a token: an absent list
      // and an empty one denote the same document, so a slot that is empty on
      // almost every image must not cost a key on almost every image. That
      // makes it the missing-list-field decode class — absent decodes to `[]`,
      // never to a null or an undefined, and the corpus pins the absent case
      // explicitly so no host has to guess.
      //
      // Phase 1079 — `expandable` is the sixth addition and the only one that
      // declares an INTERACTION rather than a picture. It is a plain
      // omit-at-default bool (`false` is the identity, so it costs no key on
      // any image that does not ask for it), and what it declares is that the
      // full-size asset is reachable from the rendered image. Its whole design
      // lives in what a host must emit for it: a REAL `<a href>` to the asset,
      // so the affordance works with no script at all, plus a
      // `data-fuaran-expandable` marker an enhancement tier reads to upgrade
      // that link into an in-page overlay. The wire says the intent; it says
      // nothing about lightboxes, because a declaration whose only honest
      // rendering needed JavaScript would be a dead control on every host that
      // does not run any.
      { Tag = "Image"
        Category = "Display"
        Annotations = Annotations.Empty
        Fields =
          [ req "alt" TS
            req "src" (bindingOf TStr)
            omit "variant" (TEnum "ImageVariant") (VEnum "Default")
            omit "fit" (TEnum "ImageFit") (VEnum "Natural")
            omit "aspectRatio" (TEnum "ImageAspect") (VEnum "Natural")
            omit "loading" (TEnum "ImageLoading") (VEnum "Eager")
            omit "srcSet" (TList(TRecord "SrcSetEntry")) (VList [])
            omit "expandable" TBool (VBool false)
            opt "caption" TS ] }
      // Phase 1076 — `Media`: the playback surface. ONE kind carrying a
      // `MediaKind` variant (Video / Audio), which is the shape the vocabulary
      // charter's Appendix A pre-ruled and this declaration honours rather than
      // overrules. Shared invariants sit here; the video-only slots sit in the
      // case payload above.
      //
      // `label` is REQUIRED and is the a11y floor — `ImageSpec.alt`'s rule
      // applied to a control surface. A `<video>` / `<audio>` element with no
      // accessible name is announced by its element type alone, which tells a
      // screen-reader user that a media player exists and nothing about what it
      // plays; unlike an image there is no decorative case, because a transport
      // is always an interactive control.
      //
      // `controls` omits when TRUE — the `Toast.dismissable` polarity, and for
      // the same kind of reason: a media element with no transport is a surface
      // a keyboard user cannot pause, so the default is the accessible one and
      // the DECLARATION is the deviation. `loop` omits at false, the ordinary
      // polarity.
      //
      // Phase 1110 — `tracks` and `transcript`, the two additions that make the
      // charter's "captioning a11y no existing kind expresses" claim TRUE rather
      // than merely asserted. Both are field-tier (§2.1): no new kind, no new
      // case, so the confusion delta is structurally zero and a host that has
      // never met either renders exactly what it rendered before.
      //
      // `tracks` is the second REPEATED structured slot in the vocabulary (after
      // `Image.srcSet`) and takes the same omit-at-EMPTY-LIST rule: an absent
      // list and an empty one denote the same document, so a slot that is empty
      // on most media must not cost a key on most media. Absent decodes to `[]`,
      // never to a null.
      //
      // `transcript` is the AUDIO floor and is an ordinary optional `TextSource`
      // rather than an omit-at-default one, because it is CONTENT (the
      // `Image.caption` ruling): absent means the document offers no transcript,
      // which is a different statement from offering an empty one. It lives on
      // the SPEC rather than on `MediaKind.Video`, because a transcript is the
      // one accessibility affordance an audio surface needs MORE than a video
      // one — captions ride the timeline a video already has, while a recording
      // with no visual channel has nowhere else to put its words.
      { Tag = "Media"
        Category = "Display"
        Annotations = Annotations.Empty
        Fields =
          [ omit "controls" TBool (VBool true)
            req "kind" (TUnion("MediaKind", []))
            req "label" TS
            omit "loop" TBool (VBool false)
            req "src" (bindingOf TStr)
            omit "tracks" (TList(TRecord "TrackEntry")) (VList [])
            opt "transcript" TS ] }
      // Phase 1111 — `Embed`: a third-party document rendered inside a
      // maximally-sandboxed browsing context. A genuinely new kind rather than a
      // `Mount` variant, and the charter's Appendix A row moved from "Covered by
      // Mount" to ADMITTED in the same change-set on exactly that point: `Mount`
      // composes a COOPERATING guest — a scope id, a declared message channel, a
      // capability request list, a host-side loader — none of which a YouTube
      // page has or could have. Widening `Mount` to admit an uncooperative third
      // party would weaken every guarantee `Mount` currently makes; the two
      // contracts are opposites (bidirectional cooperation vs default-deny
      // isolation) and they get separate kinds.
      //
      // `title` is REQUIRED and is the a11y floor, on `MediaSpec.label`'s
      // argument one kind over: a frame with no accessible name is announced as
      // "frame" and nothing else, and there is no decorative embed — a frame is
      // a focus container a reader tabs into. FUARAN111 refuses the empty
      // literal that satisfies the requirement while meaning nothing.
      //
      // `permissions` is a LIST over the closed `EmbedPermission` enum, omitted
      // at EMPTY — and empty is total denial, so the wire-cheapest document is
      // also the safest one. That polarity is the point: `Image.srcSet`'s
      // omit-at-empty rule reused for a security slot, where the default a lazy
      // author gets is the locked one.
      //
      // `aspectRatio` REUSES `ImageAspect` rather than minting a parallel enum.
      // The cases are pure layout ratios with nothing image-specific in them,
      // the wire carries bare strings so the type name reaches no document, and
      // two closed sets with identical cases that must be kept in step is the
      // defect a rule-of-three would be protecting against, not the reuse. It is
      // omit-at-`Natural` rather than an option for the same reason every other
      // slot in this vocabulary is: an option over an enum already containing
      // `Natural` would give one fact two spellings.
      //
      // `src` does NOT ride the ordinary §19 accept set. An embed's class is
      // fetch-and-EXECUTE, where `Image`/`Media` are fetch-and-display, so the
      // spec names a separate `embed` egress class admitting `https` and
      // NOTHING else — no other scheme and no schemeless reference either. A
      // relative reference names a same-origin document, which is precisely the
      // shape where `AllowSameOrigin` + `AllowScripts` lets the framed document
      // reach its own sandbox attribute; a host composing its own content has
      // `Mount`. One accepted scheme, no positional tests, so the class cannot
      // inherit §19 rule 5's evasion surface.
      { Tag = "Embed"
        Category = "Display"
        Annotations = Annotations.Empty
        Fields =
          [ omit "aspectRatio" (TEnum "ImageAspect") (VEnum "Natural")
            omit "permissions" (TList(TEnum "EmbedPermission")) (VList [])
            req "src" (bindingOf TStr)
            req "title" TS ] }
      { Tag = "Link"
        Category = "Display"
        Annotations = Annotations.Empty
        Fields =
          [ req "href" (bindingOf TStr)
            req "label" TS
            omit "download" TBool (VBool false)
            opt "rel" TStr
            opt "target" TStr
            // Phase 812 — the anti-scraper render strategy. Omitted when absent.
            opt "protection" (TEnum "LinkProtection") ] }
      { Tag = "Callout"
        Category = "Display"
        Annotations = Annotations.Empty
        Fields =
          [ req "body" TS
            omit "dismissable" TBool (VBool false)
            omit "tone" (TEnum "ToneVariant") (VEnum "Default")
            opt "heading" TS
            opt "icon" icon ] }
      { Tag = "Progress"
        Category = "Display"
        Annotations = Annotations.Empty
        Fields =
          [ req "fraction" (bindingOf TFloat)
            omit "indeterminate" TBool (VBool false)
            omit "tone" (TEnum "ToneVariant") (VEnum "Default")
            opt "label" TS
            opt "caveat" TS ] }
      { Tag = "Metric"
        Category = "Display"
        Annotations = Annotations.Empty
        Fields =
          [ req "label" TS
            // Fuaran-UI 0.2.x renamed Metric's binding slot `source` → `value`.
            req "value" (bindingOf TFloat)
            omit "format" CF (VUnion("None", []))
            omit "tone" (TEnum "ToneVariant") (VEnum "Default")
            omit "weight" (TEnum "StyleWeight") (VEnum "Standard")
            omit "emphasis" (TEnum "Emphasis") (VEnum "Normal")
            opt "trend" (bindingOf TFloat)
            opt "trendFormat" CF
            // Phase 867 — which direction of this quantity is GOOD. Omitted at
            // its `HigherIsBetter` default, which is the reading every existing
            // document already has, so no pre-existing fixture moves a byte.
            // Says nothing about `value`: a Metric with no `trend` that declares
            // a polarity is legal-but-inert.
            omit "trendPolarity" (TEnum "TrendPolarity") (VEnum "HigherIsBetter")
            // Phase 2472 — further comparisons, each its own trend line. Omitted
            // when empty, so every existing document keeps its bytes.
            omit "comparisons" (TList(TRecord "MetricComparison")) (VList [])
            opt "icon" icon
            opt "subtext" TS ] }
      { Tag = "LabelValueRow"
        Category = "Display"
        Annotations = Annotations.Empty
        Fields =
          [ omit "emphasis" TBool (VBool false)
            omit "format" CF (VUnion("None", []))
            req "label" TS
            // Fuaran-UI 0.2.x renamed the binding slot `source` → `value`.
            req "value" (bindingOf TFloat)
            opt "help" TS ] }
      // `Fact` — a labelled TEXT fact (LabelValueRow's sibling: that one's `value`
      // is a numeric binding, this one's is a TextSource). `emphasis` is the
      // behavioural bool, emitted only when true; `tone` omits at Default.
      { Tag = "Fact"
        Category = "Display"
        Annotations = Annotations.Empty
        Fields =
          [ omit "emphasis" TBool (VBool false)
            opt "help" TS
            opt "icon" icon
            req "label" TS
            omit "tone" (TEnum "ToneVariant") (VEnum "Default")
            req "value" TS
            factReference ] }
      { Tag = "Sparkline"
        Category = "Display"
        Annotations = Annotations.Empty
        // Fuaran-UI 0.2.x typed-Static: a real numeric list on the wire. FLOAT, not
        // int — the hand tier's source is `Binding<float …>` and canonical JNum
        // rendering emits whole floats in integer form, so the corpus's `[1,2,3]`
        // bytes are unchanged while fractional samples stay representable.
        Fields = [ req "source" (bindingOf (TList TFloat)) ] }
      { Tag = "CodeBlock"
        Category = "Display"
        Annotations = Annotations.Empty
        Fields =
          // Phase 2300 — the three flags omit at their declared defaults. NOTE
          // the polarity of `copyable`: it omits when TRUE, because a code block
          // is copyable unless it says otherwise (`Defaults.codeBlock`), the
          // `Toast.dismissable` posture; `lineNumbers` and `highlightLines`
          // omit at `false` / `[]`.
          [ req "code" TStr
            omit "copyable" TBool (VBool true)
            omit "highlightLines" (TList TInt) (VList [])
            req "language" TStr
            omit "lineNumbers" TBool (VBool false) ] }
      // Phase 679 — `Toast`. NOTE the polarity: `dismissable` omits when TRUE
      // (a toast defaults dismissable), the opposite of `Callout`'s, which omits
      // when false. Same field name, same type, inverted default.
      { Tag = "Toast"
        Category = "Display"
        Annotations = Annotations.Empty
        Fields =
          [ omit "dismissable" TBool (VBool true)
            req "message" TS
            req "open" (bindingOf TBool)
            omit "tone" (TEnum "ToneVariant") (VEnum "Default") ] }
      // Phase 679 — `Drawing`. Its closure (Shape / DrawStyle / DrawPoint /
      // CurveCommand / ViewBox / TextAnchor) is declared above the meta kinds.
      { Tag = "Drawing"
        Category = "Display"
        Annotations = Annotations.Empty
        Fields =
          [ opt "description" TS
            req "shapes" (TList(TUnion("Shape", [])))
            req "style" (TRecord "DrawStyle")
            opt "title" TS
            req "viewBox" (TRecord "ViewBox") ] } ]

// ─── Layout kinds (child-bearing; `children : Node list` recurses via TNode) ─
//
// The category that proves recursive nesting across families (a `Card` holding a
// `Metric` + a `LabelValueRow`). New shape classes vs Display: node-list children
// (`TList TNode`), `Binding<int>` / `Binding<bool>` controlled-state slots, a
// wire-encoded `Action` (`Modal.OnDismiss`), and closure-sentinel dispatch slots
// (`Tabs`/`Stepper` `onSelect` → `"<closure>"`). Some closures are *omitted*
// entirely (`Disclosure.OnToggle` has no wire key) — modelled by simply not
// declaring the field. `tabs-explicit-1` (TabHeader records + tabTags/activeTag
// overlays) rides the Input-family slice, where the non-discriminated record type
// is the dominant new class.
let layoutKinds: IdlKind list =
    [ // Fuaran-UI 0.2.0 Box unification: Dashboard / Card / Stack / GridLayout collapsed
      // into one `Box` kind carrying `role` (BoxRole) + `layout` (BoxLayout). The other
      // container kinds (SplitPanel / SummaryList / Disclosure / Modal / ScrollArea /
      // Tabs / Stepper) were NOT unified.
      { Tag = "Box"
        Category = "Layout"
        Annotations = Annotations.Empty
        // Fuaran-UI Phase 1473 — the two print-break declarations, APPENDED so no
        // existing constructor position moves, and both omit-at-`false` so every
        // box written before this release encodes to the bytes it always did.
        //
        // They live on `Box` and on no other layout kind, for the charter row's
        // own §1.2 reason: a `SplitPanel`, a `Disclosure` or a `Tabs` that must
        // stay whole is reachable by wrapping it in a `Box`, so a slot on each of
        // them would be composition-reducible and fail irreducibility. What no
        // wrapper reaches is a GRID'S ROWS, which is why `DataGrid` carries its
        // own pair and nothing else does.
        Fields =
          [ req "children" (TList TNode)
            opt "heading" TS
            req "layout" (TUnion("BoxLayout", []))
            req "role" (TEnum "BoxRole")
            omit "keepTogether" TBool (VBool false)
            omit "breakBefore" TBool (VBool false) ] }
      { Tag = "SplitPanel"
        Category = "Layout"
        Annotations = Annotations.Empty
        Fields = [ req "children" (TList TNode); req "weight" TFloat ] }
      { Tag = "SummaryList"
        Category = "Layout"
        Annotations = Annotations.Empty
        Fields = [ req "children" (TList TNode); opt "heading" TS ] }
      { Tag = "Disclosure"
        Category = "Layout"
        Annotations = Annotations.Empty
        // Phase 671 step 2 — this comment used to read "OnToggle is a closure that
        // is NOT on the wire — no field declared", which was true before Phase 426
        // and false after: `onToggle` now rides as the `"<closure>"` sentinel when
        // present. The direct byte-diff caught the drift (`controls-closure`).
        Fields =
          [ req "children" (TList TNode)
            req "defaultOpen" TBool
            req "heading" TS
            opt "onToggle" (handlerOf "bool" "boolean")
            req "open" (bindingOf TBool) ] }
      { Tag = "Modal"
        Category = "Layout"
        Annotations = Annotations.Empty
        // `onDismiss` optional since Fuaran-UI Phase 426: `Some action` encodes
        // exactly as before; `None` omits the key and arms the renderer's `Open`
        // write-back default. The IDL carried it Required until the Phase 692
        // gap-closure (`controls-declarative` omits it).
        // Fuaran-UI Phase 1119 — `modality` + `anchor` appended at the END of
        // the field list, which is the additive position: `mkModal`'s existing
        // parameters are the REQUIRED fields and neither of these is one, so no
        // constructor position moves. `modality` omits at `Modal`, so every
        // pre-1119 modal document is byte-unchanged; `anchor` is a NodeId and is
        // meaningful for `Popover` only (a `Modal` carrying one is a dead
        // declaration the tier's validator reports, not a decode refusal).
        Fields =
          [ req "children" (TList TNode)
            // Phase 2300 — omits when TRUE: a modal is dismissable unless it says
            // otherwise (`Defaults.modal`; every corpus modal carried `true`).
            omit "dismissable" TBool (VBool true)
            opt "onDismiss" (TUnion("Action", []))
            req "open" (bindingOf TBool)
            opt "heading" TS
            omit "modality" (TEnum "ModalityKind") (VEnum "Modal")
            opt "anchor" TStr ] }
      { Tag = "ScrollArea"
        Category = "Layout"
        Annotations = Annotations.Empty
        Fields =
          [ req "children" (TList TNode)
            req "orientation" (TEnum "ScrollOrientation")
            opt "maxHeight" TInt
            opt "maxWidth" TInt ] }
      { Tag = "Tabs"
        Category = "Layout"
        Annotations = Annotations.Empty
        // onSelect is a closure that IS on the wire as the "<closure>" sentinel.
        // The tabHeaders / tabTags / activeTag overlays are optional (omitted in
        // tabs-1, present in tabs-explicit-1 — the TabHeader record slice).
        // `orientation` is omit-when-Horizontal (0.2.0) — the previous note here
        // ("0.2.x dropped Tabs.orientation") was wrong: the hand encoder emits it
        // for Vertical and the decoder restores the Horizontal default on absence.
        // No corpus fixture is Vertical, which is how the byte gate missed it
        // (found by the stage-4b swap, the stage-3b BoxRole.Separator class).
        Fields =
          // Phase 1585 — omit-when-`Binding.Static (Some 0)`, the identity default
          // the hand decoder has restored on absence since Phase 126 and every
          // conformant host tolerates. It could not take the posture in 1585's own
          // pass: the default is a union case CARRYING a field, and the codegen
          // then rendered a union default only for a NULLARY case, so declaring it
          // was refused outright as `UnsupportedDefault (TUnion ("Binding", [TInt]),
          // VUnion ("Static", [("value", VInt 0)]))` — and a default the generator
          // cannot render degrades to always-emit plus `dReq`, which would be a
          // declaration contradicting the layer generated from it. The value model
          // moved instead of the posture bending: the codegen renders the
          // value-carrying union case now, in an expression AND a pattern position
          // (`Fuaran.Core.Idl.Codegen` 0.21.0), so the encoder's omit test is
          // `match … with | Binding.Static(Some(0)) -> None | _ -> …` and the
          // decoder's restore is `dDef`. The parity guard's `decoderTolerantOfAbsence`
          // residue is empty as a result — the divergence is GONE rather than named.
          [ omit "activeIndex" (bindingOf TInt) (VUnion("Static", [ "value", VInt 0 ]))
            req "children" (TList TNode)
            omit "orientation" (TEnum "Orientation") (VEnum "Horizontal")
            opt "onSelect" (handlerOf "int" "number")
            // Phase 671 step 2 — also caught by the direct diff: present in
            // `controls-closure`, absent from the IDL, so it was silently dropped.
            opt "onSelectTag" (handlerOf "string" "string")
            opt "tabHeaders" (TList(TRecord "TabHeader"))
            opt "tabTags" (TList TStr)
            opt "activeTag" (bindingOf TStr) ] }
      { Tag = "Stepper"
        Category = "Layout"
        Annotations = Annotations.Empty
        Fields =
          // Phase 2300 — `activeStep` omits at `Static{value=0}`, the
          // `Tabs.activeIndex` precedent.
          [ omit "activeStep" (bindingOf TInt) (VUnion("Static", [ "value", VInt 0 ]))
            req "children" (TList TNode)
            opt "onSelect" (handlerOf "int" "number") ] } ]

// ─── Input kinds (interactive; the richest Binding / Action surface) ────────
//
// New shape classes vs Display/Layout: the full `Action` union (`Button.onClick`
// — Chain / WriteToClipboard / Dispatch / Invoke / ReadFileBody), the recursive
// `Binding.Local` / `Binding.Format` / `Binding.Invoke` cases, the closure-heavy
// `FormFieldKind` / `FilterKind` unions, and — the headline — **non-discriminated
// record** fields (`FormField` / `FilterSpec` / `InvokeArg` / `TabHeader`, all
// `TRecord`). _(The old multiselect-1 / form-segmented deferral is closed: Phase
// 677 removed null from the wire — absence omits the key — so both round-trip.)_
let inputKinds: IdlKind list =
    [ { Tag = "Button"
        Category = "Input"
        Annotations = Annotations.Empty
        Fields =
          [ req "label" (TUnion("TextSource", []))
            req "onClick" (TUnion("Action", []))
            omit "variant" (TEnum "ButtonVariant") (VEnum "Secondary")
            opt "icon" TStr
            // WIRE_FORMAT.md §10.1 — `ButtonSpec.Tooltip` is typed surface but
            // NOT wire vocabulary: never emitted, restored to `None` on decode.
            // Modelling it `opt` made the generated encoder emit a field the
            // spec forbids — invisible to the corpus (no fixture carries it),
            // caught by the Phase 101 idempotence fuzz at the 694 collapse.
            hostOnly "tooltip" "TextSource option" "None"
            opt "disabled" (bindingOf TBool)
            // Phase 2517 — appended, absent-at-`None`. WIRE_FORMAT §31.
            opt "affordance" (TRecord "Affordance") ] }
      { Tag = "Select"
        Category = "Input"
        Annotations = Annotations.Empty
        // Fuaran-UI 0.2.x typed-Static: source is a real SelectOption list, value a real
        // string; onChange is a closure sentinel; multiple omitted when false. (`values`
        // rides multiselect-1, deferred — a `Static None` renders JSON null.)
        Fields =
          [ req "label" (TUnion("TextSource", []))
            opt "onChange" (handlerOf "string option" "string | null")
            // Phase 671 step 2 — the multi-select handler, present in
            // `controls-closure` and absent from the IDL until the direct
            // byte-diff found it silently dropped.
            opt "onChangeMulti" (handlerOf "string list" "string[]")
            req "source" (bindingOf (TList(TRecord "SelectOption")))
            // Phase 1962 — `value` is the SINGLE-select binding: required when
            // `multiple` is not `true`, absent when it is (a multi-select carries
            // `values`). The IDL's per-field optionality cannot say "required
            // unless a sibling is true", so the slot is `opt` here and the rule is
            // enforced where a field's siblings are visible: the decoder (absent on
            // a single-select is MISSING_FIELD; a `value` on a multi-select is a
            // lenient accept that normalises away) and the `select` / `multiSelect`
            // constructors, with FUARAN164 naming a constructed multi-select that
            // still carries one. WIRE_FORMAT §3.2.
            opt "value" (bindingOf TStr)
            opt "placeholder" (TUnion("TextSource", []))
            opt "disabled" (bindingOf TBool)
            opt "multiple" TBool
            opt "values" (bindingOf (TList TStr))
            // Phase 2517 — appended, absent-at-`None`. WIRE_FORMAT §31.
            opt "affordance" (TRecord "Affordance")
            // Phase 2571 — appended, omit-at-default (absent and `[]` are one
            // fact: no grouping). WIRE_FORMAT §3.6.30.
            omit "groups" (TList(TRecord "SelectOptionGroup")) (VList []) ] }
      { Tag = "FileUpload"
        Category = "Input"
        Annotations = Annotations.Empty
        Fields =
          [ req "accept" (TList TStr)
            req "label" (TUnion("TextSource", []))
            omit "multiple" TBool (VBool false)
            // The handler arg is the hosted browser-file metadata record (prelude
            // type; closure args never serialise, so no codec is needed).
            opt "onSelect" (handlerOf "Fuaran.UI.HostPrelude.FileSelection list" "unknown[]")
            opt "disabled" (bindingOf TBool)
            // Phase 1115 — the two ingress gestures, both omit-at-`false`. The
            // wire names a CAPABILITY on the node that hosts the gesture and
            // consumes its effect (the affordance→op charter's governing
            // sentence); the drag, the drop, the paste and the drop-state
            // styling are the renderer's own. Appended, so the generated
            // constructor's existing positions do not move, and omit-at-`false`
            // so an upload that says nothing encodes to the bytes it always did.
            omit "acceptPaste" TBool (VBool false)
            omit "dropTarget" TBool (VBool false)
            // Phase 1116 — the third ingress route, and the one that produces a
            // file rather than moving one that already exists. An OPTION rather
            // than an omit-at-default enum, because "say nothing" is a real and
            // distinct state here: an upload with no `capture` asks for the
            // ordinary picker, which is not one of the two devices wearing a
            // default. Appended, so no generated constructor position moves, and
            // absent-at-`None`, so every upload document written before this
            // release encodes to the bytes it always did.
            opt "capture" (TEnum "CaptureSource")
            // Phase 1117 — the HOST-REGISTERED destination this upload streams
            // to. A NAME, never a URL: the string is an id the host has
            // registered with its own upload sink, and a host resolves it
            // against that sink's declared set. An id the sink does not name
            // refuses; there is no fallback, and nothing on this member could
            // ever be fetched. That is the whole point of a name — a URL here
            // would let a decoded tree from an arbitrary emitter choose where a
            // reader's file goes, which is the one thing this member exists to
            // make impossible.
            //
            // Absent — the default — is the pre-1117 control exactly: the
            // selection reaches `onSelect` and nothing leaves the client. The
            // member is an OPTION rather than an omit-at-default string for the
            // reason `capture` is: "say nothing" is a real state (no upload
            // destination at all), not a default value of the same kind, and an
            // empty string is refused at decode rather than read as absence.
            //
            // Appended, so no generated constructor position moves.
            opt "destination" TStr
            // Phase 1548 — the two DECLARED CEILINGS, and the reason they are on
            // the node rather than in a host setting. Without them the
            // server-driven gate has nothing to refuse a `file-read` against
            // before the body reaches the message loop, and a client renderer
            // has nothing to check a selection against at all — so the limit
            // that every deployment already has lives only in configuration,
            // invisible to the document and to every other host that reads it.
            // Declared here it travels with the tree: default-deny by shape,
            // where it was default-deny by configuration.
            //
            // `maxBytes` is PER FILE, not per selection. That is what makes it
            // the ceiling the `file-read` route can actually be checked against
            // — that route reads one file — and a per-selection total would be
            // a different quantity wearing the same name. A multiple upload
            // that wants a total bounds it as `maxBytes × maxFiles`.
            //
            // `maxFiles` is meaningful only alongside `multiple`. A single-file
            // upload admits one file by construction, so a ceiling there is
            // inert rather than wrong; it is documented in §4b and deliberately
            // NOT a decode refusal, because the bytes describe a control every
            // host renders identically with or without the member.
            //
            // Both are POSITIVE-ONLY, and the floor is a DECODE RULE rather
            // than a type — the `SrcSetEntry.width` precedent, and for the same
            // reason: the IDL has no refined-integer type, so the floor lives
            // in the policy decoder and the published schema, with a corpus
            // reject vector per field to make it a wire rule rather than one
            // host's opinion. A ceiling of `0` or below describes a control
            // that can accept nothing, which is the `Rating.max < 1` line at a
            // third slot: a control that cannot exist, not a control with a bad
            // value in it.
            //
            // Both are OPTIONS rather than omit-at-default integers, because
            // "say nothing" is a real and distinct state here — an upload with
            // no declared ceiling is unbounded by the document and bounded only
            // by whatever the host already enforces, which is the pre-1548
            // control exactly. Appended, so no generated constructor position
            // moves, and absent-at-`None`, so every upload document written
            // before this release encodes to the bytes it always did.
            //
            // 32-BIT, per WIRE_FORMAT.md §7.1: every typed integer slot this
            // format declares is a signed 32-bit integer, so `maxBytes` tops
            // out at 2 147 483 647 bytes (~2 GiB). A wider ceiling would be a
            // new slot width for the whole format, ratified against §7.1 and
            // §20 — not a member on one node.
            opt "maxBytes" TInt
            opt "maxFiles" TInt ] }
      { Tag = "Form"
        Category = "Input"
        Annotations = Annotations.Empty
        Fields =
          [ req "fields" (TList(TRecord "FormField"))
            req "onSubmit" (TUnion("Action", []))
            req "submitLabel" (TUnion("TextSource", []))
            opt "disabled" (bindingOf TBool) ] }
      { Tag = "Filters"
        Category = "Input"
        Annotations = Annotations.Empty
        Fields = [ req "items" (TList(TRecord "FilterSpec")) ] } ]

// ─── Visualisation kinds (data-bound; erased-row grid + chart/table/map) ────
//
// New shape classes vs Input: nested list-of-lists (`Table.rows : TList (TList
// TS)`), the erased `ColumnErased` record holding a `CellKindErased` union + a
// `ColumnWidth` union, and closure-projection fields (`rowKey` / column `value`).
// `DataGrid.source` / `Chart.source` carry TYPED rows on the wire (fuaran#665 —
// `Map<string, obj> seq`, rendered by this tier's `RowCodec`; the `"<opaque>"` sentinel
// is decode-accepted read-compat only), `Map.source` a typed `MapMarker` list; a
// `Binding.Transform`'s `source` / `pipeline` are HOSTED slots rendered by Core's
// `ColumnCodec` / `DataFrameCodec` under the same `Canon` discipline (the Phase
// 692 gap-closure; the old grid-transform deferral is closed).
let visKinds: IdlKind list =
    [ { Tag = "DataGrid"
        Category = "Visualisation"
        Annotations = Annotations.Empty
        // Fuaran-UI 0.2.x: `editable` omit-when-false, `rowKey` optional (absent on a
        // static grid), + `staticRows` (the retired `Table` decode-upgrades into a static
        // DataGrid carrying its header/row grid).
        // Phase 425 — `rowKey` (closure) + `rowKeyField` (declarative) are
        // sibling optional slots, mirroring the column-level `value` / `field`.
        Fields =
          [ req "columns" (TList(TRecord "ColumnErased"))
            omit "editable" TBool (VBool false)
            opt "rowKey" (projOf "Map<string, obj>" "string" "(row: unknown) => string" "(fun _ -> \"\")")
            opt "rowKeyField" TStr
            // Phase 818 — the grid-sort header affordance for a DATA-BOUND grid:
            // the State key carrying the sort descriptor
            // `{"column": <index>, "direction": "asc"|"desc"}`.
            opt "sortStateKey" TStr
            // Phase 862 — declarative pagination: `pageStateKey` carries
            // `{"page": <1-based int>}`, `pageSize` is how many rows a page holds.
            opt "pageSize" TInt
            opt "pageStateKey" TStr
            // Phase 861 — the bound path's declared INITIAL order, reusing the same
            // `DefaultSort` record and field name `staticRows` carries (Phase 801).
            opt "defaultSort" (TRecord "DefaultSort")
            // Phase 863 — the DECLARED edit destination: the State key an edited
            // cell's whole updated rows value is committed to.
            opt "editStateKey" TStr
            // Phase 934 — declarative row reorder. Omit-when-false, matching its
            // nearest sibling `editable` rather than being an optional bool: for an
            // affordance flag "not stated" and "explicitly off" are the same state, so
            // an option would carry a distinction the renderer cannot act on. The
            // reordered rows commit to `editStateKey` above — a reorder IS a write of
            // the whole updated rows value, so it needs no destination of its own.
            omit "reorderable" TBool (VBool false)
            // Fuaran-UI Phase 1123 — cross-container transfer. The affordance→op
            // charter's governing sentence names "the node that both hosts the
            // gesture and consumes its effect", singular; a transfer has TWO
            // nodes and neither alone consumes it, so the sentence is extended:
            // where a gesture spans two nodes, the wire names the capability on
            // BOTH ENDS as a shared KEY each declares its own side of, and the
            // effect is one record written to that key.
            //
            // `transferOutKey` — this grid may RELEASE rows to the named State
            // key. `transferInKey` — this grid ACCEPTS rows arriving on it. Both
            // name the SAME key, from the two sides; a grid declaring both with
            // one key does both. Two fields rather than one symmetric key
            // because the one-way ends are ordinary: an archive column that
            // accepts and never releases, a Done column that releases nothing
            // back.
            //
            // Deliberately NOT `-StateKey`, against the sortStateKey /
            // pageStateKey / editStateKey convention: that suffix marks a key a
            // node both writes AND READS to change its own presentation, and
            // neither end reads this one for its own presentation.
            //
            // The pairing is the whole of what is added, and it is the fact only
            // the tree knows — a host cannot infer that two grids on a page are
            // one board rather than two unrelated tables.
            opt "transferInKey" TStr
            opt "transferOutKey" TStr
            // Phase 1473 — the two print-break declarations a grid can make and
            // that nothing else can make FOR it. `keepRowsTogether` says a row is
            // one thing and must not be split across a page boundary;
            // `repeatHeader` says the column headers repeat at the top of every
            // page the grid continues onto. Both omit-at-`false`, matching
            // `reorderable` and `editable`: for a declaration of this shape "not
            // stated" and "explicitly off" are the same state, so an option would
            // carry a distinction no renderer can act on.
            //
            // Neither reduces to a wrapper. A `Box` around the grid keeps the
            // WHOLE grid together — which is why there is no grid-level
            // keep-together slot here — but no arrangement of existing kinds
            // reaches a row boundary or a repeated header, because only the grid
            // knows where its rows and its header are.
            omit "keepRowsTogether" TBool (VBool false)
            omit "repeatHeader" TBool (VBool false)
            // Fuaran-UI Phase 1125 — the export affordance. The grid may be TAKEN
            // AWAY as a file: its renderer draws an export control and, on
            // activation, serialises the rows the client holds to CSV and hands
            // them to the reader.
            //
            // It sits under the affordance charter's governing sentence
            // UNAMENDED — the wire names a capability on the node that both
            // hosts the gesture and consumes its effect, and the renderer owns
            // the affordance. The grid is both ends here: only it holds its
            // resolved rows, its column set, its declared formats and the order
            // the reader has sorted them into, and nothing outside it can reach
            // any of that. A free-standing export button beside the grid would
            // be the decorative-pager shape at a third slot.
            //
            // NOT the `Export` / `DownloadAs` row of the vocabulary charter's
            // Appendix A, which is a different question with a different answer.
            // That row rules the EFFECT axis — a document asking a host to
            // produce a file — and rules it Covered by `Action.Invoke`, needing
            // no vocabulary. This is the grid-behaviour axis: it declares that
            // this grid's own rows are the reader's to take, which is a fact
            // about the document that no host capability can be told from
            // outside.
            //
            // A `bool` and not a state key, which is what distinguishes it from
            // the grid-level `sortable` / `pageable` flags the charter REFUSES
            // BY NAME. Those are refused because sorting and paging write state
            // the grid reads back, so the key IS the affordance and a flag with
            // no key behind it drives nothing. An export writes no state at all
            // — it produces a file and returns nothing to the tree — so there is
            // no key it could name, and the boolean is the whole declaration.
            //
            // Omitted on the wire at `false`.
            omit "exportable" TBool (VBool false)
            // Fuaran-UI Phase 1892 — the row window. `windowStateKey` names the
            // State key carrying `{"offset": <int ≥ 0>, "count": <int ≥ 1>}`,
            // which the renderer writes as the viewport moves and the grid reads
            // back — the fourth instance of the Phase-860 state-key rule, after
            // sort, page and edit. `rowTotal` is the DECLARED size of the whole
            // result set, read only where the host slices (a `Query` whose
            // `dependsOn` names the window or page key): a host that returns a
            // window or a page knows how many rows matched, and a grid holding
            // its whole set counts them itself. Both options, emitted only when
            // present, so a grid declaring neither is byte-identical to before.
            opt "windowStateKey" TStr
            opt "rowTotal" (bindingOf TInt)
            // The row feed is HOSTED `Map<string, obj> seq` (fuaran#665 — typed rows):
            // a Static/State rows payload IS wire-representable (a JSON array of row
            // objects, scalar cells, rendered by this tier's `RowCodec` under the `Canon`
            // discipline), and decode accepts the legacy `"<opaque>"` sentinel
            // indefinitely (read-compat → the empty feed).
            req
                "source"
                (bindingOf (
                    THosted
                        { FSharp = "Map<string, obj> seq"
                          Encode = "Fuaran.UI.RowCodec.encodeRows"
                          Decode = "Fuaran.UI.RowCodec.decodeRows"
                          Wire = None
                          Format = None
                          Refusal = HostedRefusal.Sentence }
                ))
            opt "staticRows" (TRecord "StaticRows")
            opt "onRowClick" (handlerOf "Map<string, obj>" "unknown") ] }
      { Tag = "Chart"
        Category = "Visualisation"
        Annotations = Annotations.Empty
        Fields =
          [ req "kind" (TEnum "ChartKind")
            // The row feed is HOSTED `Map<string, obj> seq` (fuaran#665 — typed rows,
            // same `RowCodec` + read-compat sentinel acceptance as `DataGrid.source`).
            req
                "source"
                (bindingOf (
                    THosted
                        { FSharp = "Map<string, obj> seq"
                          Encode = "Fuaran.UI.RowCodec.encodeRows"
                          Decode = "Fuaran.UI.RowCodec.decodeRows"
                          Wire = None
                          Format = None
                          Refusal = HostedRefusal.Sentence }
                ))
            // Phase 1585 — omit-when-`false`. Every decoder in the estate already
            // reads absence as `false` (the F# hand decoder since Phase 126, and
            // the TypeScript, Python, Rust and Go hosts), so the tolerance was a
            // courtesy five hosts happened to share rather than a stated contract,
            // and the generated structural decoder — the one reader that followed
            // the IDL literally — refused what every other reader accepted. The
            // posture states it: the encoder omits at the default, the decoder
            // restores it, and the schema is optional by construction.
            omit "stacked" TBool (VBool false)
            req "xField" TStr
            // Phase 2568 — the list may be EMPTY: the series are then every numeric column of the
            // bound rows but `xField`, Ordinal-ordered and named by column (WIRE_FORMAT §3.6.29).
            // A list already admits empty, so the projection does not move; the rule that an empty
            // list is admitted only over a `Query` source is the validator's (FUARAN168).
            req "yFields" (TList TStr)
            opt "title" TS
            // Phase 876 — the VALUE axis's number format, reusing the existing
            // `Format` vocabulary rather than minting a parallel formatting DU.
            opt "valueFormat" (TUnion("Format", []))
            // Phase 878 — the axis NAMES and the subtitle. Semantic wire fields for
            // the same reason `title` is one and `ChartStyle` is not (D8).
            opt "xTitle" TS
            opt "yTitle" TS
            opt "subtitle" TS
            // Phase 880 — WHERE the legend sits, and whether it sits anywhere at
            // all. Absent means the style's default (`Right`), never "no legend".
            opt "legendPosition" (TEnum "ChartLegendPosition")
            // Phase 881 — whether the values are written onto the picture. Absent
            // means `Off`, which is also the shipped default.
            opt "dataLabels" (TEnum "ChartDataLabels")
            // Phase 882 — what the x column MEANS. Absent means `Category`.
            opt "xScale" (TEnum "ChartXScale")
            // Phase 1490 (§4l) — the data-addressed annotations: reference
            // lines today, event markers and range bands as further cases on
            // the same closed union. Absent OMITS on the wire, so every
            // pre-1490 fixture is byte-unchanged.
            opt "annotations" (TList(TUnion("ChartAnnotation", [])))
            opt "onPointClick" (handlerOf "Map<string, obj>" "unknown") ] }
      { Tag = "Map"
        Category = "Visualisation"
        Annotations = Annotations.Empty
        // Fuaran-UI 0.2.x typed-Static: the map source is a real MapMarker list.
        Fields =
          [ req "centreLatitude" TFloat
            req "centreLongitude" TFloat
            req "source" (bindingOf (TList(TRecord "MapMarker")))
            req "zoom" TInt
            opt "onMarkerClick" (handlerOf "MapMarker" "MapMarker") ] } ]

// ─── Meta kinds (the escape hatches + parameterised fragments) ──────────────
//
// These sit directly on `NodeKind` (no behavioural category). New shape classes:
// string-keyed *maps* (`TMap` — `Custom.props`, `FragmentRef.args`), node-bearing
// fields on non-layout kinds (`ErrorBoundary.child`/`fallback`, `FragmentDecl.body`,
// `SlotArg.tree`), and the `HoleDecl`/`Scalar`/`HoleValueSpace`/`FragmentArg`
// unions. `props` is `Map<string, JsonValue>` — empty in every corpus fixture, so
// its value-type is `TOpaque` (non-empty props with real JsonValue best-effort is
// a later refinement). Completing `Custom` + these kinds is what unblocks
// Phase 321 tasks 2 + 3 (the Custom allowlist + codegen-time sanitisation).
// ─── Phase 679: the `Drawing` sub-vocabulary ───────────────────────────────
//
// One kind, but the largest closure in the IDL: a 9-case RECURSIVE shape union
// (`Group` nests `Shape list`), an all-optional style record, a point record, a
// 5-case path-command union, a viewBox record and a text-anchor enum. Modelled
// together because a half-modelled `Shape` is worse than none — the drift would
// be silent (a dropped case) rather than a loud missing-kind error.

let private textAnchor = Declare.enumOf "TextAnchor" [ "Start"; "Middle"; "End" ]

let private drawPoint =
    { Name = "DrawPoint"
      Fields = [ req "x" TFloat; req "y" TFloat ]
      Annotations = Annotations.Empty
      Inline = false }

let private viewBoxRecord =
    { Name = "ViewBox"
      Fields =
        [ req "height" TFloat
          req "minX" TFloat
          req "minY" TFloat
          req "width" TFloat ]
      Annotations = Annotations.Empty
      Inline = false }

/// Every field optional — an empty `{}` is a legitimate style (see `drawing-empty`).
let private drawStyle =
    { Name = "DrawStyle"
      Fields =
        [ opt "emphasis" (TEnum "Emphasis")
          opt "fill" (bindingOf TStr)
          opt "fontFamily" TStr
          opt "fontSize" TFloat
          // Phase 642 — the derivation-based mark identity for a data-bearing
          // shape (`series-field|category-key`, emitted as `data-fuaran-mark`).
          // Wire-visible when present (omitted-when-None, rule 4); the corpus
          // carries no occurrence, which is why the Phase 692 gap-closure sweep
          // missed it until the stage-3 swap read the hand-written encoder.
          opt "markId" TStr
          opt "opacity" (bindingOf TFloat)
          // Phase 883 — the mark's rotation in degrees (a rotated axis label, a
          // tilted category tick). Omitted when absent.
          opt "rotation" TFloat
          opt "stroke" (bindingOf TStr)
          opt "strokeWidth" (bindingOf TFloat)
          opt "textAnchor" (TEnum "TextAnchor")
          // Phase 883 — the mark's hover tip. A TextSource, so it carries the
          // same literal / bound / formatted vocabulary every other label does.
          opt "tip" TS ]
      Annotations = Annotations.Empty
      Inline = false }

let private curveCommand =
    { Name = "CurveCommand"
      Params = []
      Cases =
        // The destination point is `to` on every command — NOT the F# case-field
        // names (`point` / `endpoint`), which is what the first cut of this
        // modelled and why `drawing-1` failed to decode. Read the wire.
        [ { Tag = "MoveTo"
            Fields = [ req "to" (TRecord "DrawPoint") ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "LineTo"
            Fields = [ req "to" (TRecord "DrawPoint") ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "CubicTo"
            Fields =
              [ req "control1" (TRecord "DrawPoint")
                req "control2" (TRecord "DrawPoint")
                req "to" (TRecord "DrawPoint") ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "QuadraticTo"
            Fields = [ req "control" (TRecord "DrawPoint"); req "to" (TRecord "DrawPoint") ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Close"
            Fields = []
            Annotations = Annotations.Empty
            Hosted = None } ]
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.CaseName
      Discrimination = UnionDiscrimination.Tagged }

/// Recursive: `Group` carries `Shape list`. Every case carries a `style`.
let private shape =
    { Name = "Shape"
      Params = []
      Cases =
        [ { Tag = "Group"
            Fields =
              [ req "children" (TList(TUnion("Shape", [])))
                req "style" (TRecord "DrawStyle") ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Case-field order matches the hand-written positional order (the
          // stage-0 swap-prep convention — wire-free, the renderer sorts keys).
          { Tag = "Rectangle"
            Fields =
              [ req "x" TFloat
                req "y" TFloat
                req "width" TFloat
                req "height" TFloat
                opt "cornerRadius" TFloat
                req "style" (TRecord "DrawStyle") ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Line"
            Fields =
              [ req "x1" TFloat
                req "y1" TFloat
                req "x2" TFloat
                req "y2" TFloat
                req "style" (TRecord "DrawStyle") ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Polyline"
            Fields = [ req "points" (TList(TRecord "DrawPoint")); req "style" (TRecord "DrawStyle") ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Polygon"
            Fields = [ req "points" (TList(TRecord "DrawPoint")); req "style" (TRecord "DrawStyle") ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Curve"
            Fields =
              [ req "commands" (TList(TUnion("CurveCommand", [])))
                req "style" (TRecord "DrawStyle") ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Circle"
            Fields =
              [ req "cx" TFloat
                req "cy" TFloat
                req "r" TFloat
                req "style" (TRecord "DrawStyle") ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Ellipse"
            Fields =
              [ req "cx" TFloat
                req "cy" TFloat
                req "rx" TFloat
                req "ry" TFloat
                req "style" (TRecord "DrawStyle") ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Label"
            Fields =
              [ req "x" TFloat
                req "y" TFloat
                req "text" TS
                req "style" (TRecord "DrawStyle") ]
            Annotations = Annotations.Empty
            Hosted = None } ]
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.CaseName
      Discrimination = UnionDiscrimination.Tagged }

/// Phase 1490 — a chart's data-addressed annotations, §4l of
/// `docs/CHARTS-DRAWING-PRIMITIVE-DESIGN.md`. ONE closed union rather than one
/// optional `ChartSpec` field per member: a fourth member is then a case on a
/// union whose `$type` dispatch already exists, where three fields would be a
/// fourth widening of `ChartSpec` and a fourth FS0764 break for every consumer
/// building the record without the smart constructors. The list pays the
/// record-widening cost ONCE, for the family.
///
/// An annotation carries an ADDRESS and, optionally, a LABEL — never geometry
/// and never ink (§4l "Never geometry, never style"): the plot rectangle, the
/// value mapping and the band pitch are the lowering's, and the ink is
/// `ChartStyle`'s at a per-role opacity. That is what makes an annotation
/// survive a data change, a theme flip, a restyle and a resize, where a
/// pixel-placed overlay is correct for exactly one dataset on one canvas.
///
/// `ReferenceLine` is the first case: a horizontal line at a `value` in the
/// VALUE axis's own units, with an optional `TextSource` label carried
/// unresolved (the Phase 1143 text contract).
/// [Phase 1491](event marker) and [Phase 1492](range band) add a case each.
/// Phase 1491 — an annotation's X ADDRESS: the two forms the x axis already
/// distinguishes (§4l "The three addressing forms", taking §4h's split
/// wholesale). A `Category` key names a band on a band axis; a `Date` names an
/// instant under `XScale = Temporal`. DECLARED, never sniffed — a `Category`
/// address under a temporal scale, or a `Date` under a band axis, is a mismatch
/// the validator refuses rather than a coercion.
///
/// Its own type rather than two inline fields on `EventMarker`, because
/// [Phase 1492](the range band) addresses an x-axis band with a PAIR of exactly
/// these, and a pair of an inline shape has nothing to be a pair of.
let private chartAnnotationX =
    { Name = "ChartAnnotationX"
      Params = []
      Cases =
        [ { Tag = "Category"
            Fields = [ req "key" TStr ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "Date"
            Fields = [ req "iso" TStr ]
            Annotations = Annotations.Empty
            Hosted = None } ]
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.CaseName
      Discrimination = UnionDiscrimination.Tagged }

/// Phase 1492 — a range band's PAIR (§4l "The three addressing forms", the
/// third row). Two of the same address form, on one axis.
///
/// THE AXIS IS THE CASE, not a field beside the pair. §4l says a `RangeBand`
/// declares which axis it is on, because a band is the one member legible on
/// either; the shard spelled that as `axis: X | Y` alongside an untyped
/// `from`/`to`, which cannot type the pair as "the axis's address form" — the
/// same parenthetical demands it. A union tag carries the axis AND types the
/// pair with it, so an x pair under a declared value axis is not a state the
/// validator has to police: it cannot be written.
///
/// `ValueRange` is two `float`s in the VALUE axis's own units; `XRange` is two
/// `ChartAnnotationX`, which is the type [Phase 1491](event marker) declared
/// separately for exactly this — a pair of an inline shape has nothing to be a
/// pair of.
let private chartAnnotationRange =
    { Name = "ChartAnnotationRange"
      Params = []
      Cases =
        [ { Tag = "ValueRange"
            Fields = [ req "from" TFloat; req "to" TFloat ]
            Annotations = Annotations.Empty
            Hosted = None }
          { Tag = "XRange"
            Fields =
              [ req "from" (TUnion("ChartAnnotationX", []))
                req "to" (TUnion("ChartAnnotationX", [])) ]
            Annotations = Annotations.Empty
            Hosted = None } ]
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.CaseName
      Discrimination = UnionDiscrimination.Tagged }

let private chartAnnotation =
    { Name = "ChartAnnotation"
      Params = []
      Cases =
        [ { Tag = "ReferenceLine"
            Fields = [ req "value" TFloat; opt "label" TS ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Phase 1491 — the EVENT MARKER: a vertical line at an x address,
          // with an optional label. The mirror of `ReferenceLine` across the
          // axes, and §4l rule 2 is why there is exactly one of each: a vertical
          // line at a date has ONE spelling, so the near-synonym pair that would
          // be this family's worst confusion risk does not exist to be taught
          // against.
          { Tag = "EventMarker"
            Fields = [ req "at" (TUnion("ChartAnnotationX", [])); opt "label" TS ]
            Annotations = Annotations.Empty
            Hosted = None }
          // Phase 1492 — the RANGE BAND: a shaded interval on either axis, the
          // one member that draws BEHIND the series. The pair carries the axis
          // (see `ChartAnnotationRange` above), so the case has an address and a
          // label and — §4l — nothing else.
          { Tag = "RangeBand"
            Fields = [ req "range" (TUnion("ChartAnnotationRange", [])); opt "label" TS ]
            Annotations = Annotations.Empty
            Hosted = None } ]
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.CaseName
      Discrimination = UnionDiscrimination.Tagged }

/// Phase 679 — a `Switch` case: the match string plus the node it selects. The
/// tier holds this as a `(string * Node) tuple list`, which the IDL has no type
/// for; on the wire it is a two-field record, so that is what is modelled.
///
/// Fuaran-UI Phase 1535 — `when` (a `Binding<bool>` evaluated at render time) is
/// a SIBLING of the string `match`, and `match` became optional in the same
/// change so the when-only wire shape is representable. This is the Phase 818
/// `value` / `valueFrom` shape exactly: both are declared Optional because that
/// is what the SHAPE is, and the "exactly one" rule is decoder policy
/// (`reject-switch-case-match-and-when` / `-neither`), which the IDL states no
/// more than it states path addressing.
let private switchCase =
    { Name = "SwitchCase"
      Fields = [ req "child" TNode; opt "match" TStr; opt "when" (bindingOf TBool) ]
      Annotations = Annotations.Empty
      Inline = false }

/// Phase 679 — `Mount`'s guest channel. `messageShape` rides only on `TwoWay`
/// in practice but is optional in the shape, not conditional on direction.
let private guestChannel =
    { Name = "GuestChannel"
      Fields = [ req "direction" (TEnum "ChannelDirection"); opt "messageShape" TStr ]
      Annotations = Annotations.Empty
      Inline = false }

let private channelDirection =
    Declare.enumOf "ChannelDirection" [ "OutOnly"; "TwoWay" ]

let metaKinds: IdlKind list =
    [ { Tag = "Custom"
        Category = "Meta"
        Annotations = Annotations.Empty
        Fields =
          [ req "moduleId" TStr
            req "componentId" TStr
            // The prop bag is verbatim JSON on the wire (the tier's Map<string, JVal>)
            // — `TJson`, not `TOpaque`: the hand encoder emits real values, and the
            // generated record must be constructible with them.
            req "props" (TMap TJson)
            opt "contentHash" (TRecord "ContentHash")
            opt "exposedNodeIds" (TList TStr) ] }
      { Tag = "ErrorBoundary"
        Category = "Meta"
        Annotations = Annotations.Empty
        Fields = [ keyedNode "child"; keyedNode "fallback" ] }
      { Tag = "FragmentDecl"
        Category = "Meta"
        Annotations = Annotations.Empty
        // Phase 1670 — `holes` and `effect` are OMIT-AT-DEFAULT, not Optional, and
        // the difference is the whole of that phase's encoder ruling.
        //
        // The spec has always said a zero-hole declaration omits `holes` and a
        // pure-deterministic one omits `effect`; the `EffectClass` record's own
        // doc comment in this file said so too ("omitted on the wire when pure +
        // deterministic"). Modelling both as Optional made that a statement about
        // what an author SHOULD write rather than about what this host CAN emit,
        // and the F# encoder duly emitted `"holes":[]` and the redundant
        // `{"determinism":"Deterministic","hostEffect":"Pure"}` whenever an author
        // spelled them — bytes the TypeScript and Rust hosts normalise away, so
        // the same value round-tripped to different documents on different hosts.
        // The F# round-trip gate compares `encode(decode(bytes))` against bytes F#
        // itself produced, so it was structurally incapable of noticing.
        //
        // Omit-at-default makes the redundant form UNREPRESENTABLE rather than
        // merely unwritten, which is the estate's one-canonical-form-per-pattern
        // doctrine applied to the host that owns the encoder. The accepted cost is
        // named in the ruling: `Some pure` and `None` stop being distinguishable,
        // because they never did mean different things — every consumer in the tree
        // read an absent effect as `EffectClass.pureDeterministic`.
        //
        // `FragmentRef.args` is the third member of this class and is NOT converted
        // — see its own note below.
        Fields =
          [ req "body" TNode
            req "name" TStr
            omit "holes" (TList(TUnion("HoleDecl", []))) (VList [])
            omit
                "effect"
                (TRecord "EffectClass")
                (VRecord [ "determinism", VEnum "Deterministic"; "hostEffect", VEnum "Pure" ]) ] }
      { Tag = "FragmentRef"
        Category = "Meta"
        Annotations = Annotations.Empty
        // args omitted for the degenerate name-only ref.
        //
        // Phase 1670 — `args` is the twin of `FragmentDecl`'s two fields above and
        // stays OPTIONAL, for a generator reason rather than a contract one: the
        // codegen's default-literal renderer (`Codegen.fsDefaultLit`) has arms for
        // a record, a union, a list and the scalars, and NONE for a map — so
        // `omit "args" … (VMap [])` is refused as an unsupported default and the
        // whole vocabulary fails to generate. Adding that arm is a change to the
        // substrate's IDL engine, which is a different repository and a different
        // release; recorded here rather than silently left as an asymmetry.
        //
        // What that costs today: `Args = Some Map.empty` still encodes as
        // `"args":{}` where a normalising host omits it. It is the smaller half of
        // the class — `Fuaran.UI.Fragments.Stdlib` builds every ref canonically and
        // its suite pins that — but it is the same defect, and it is open.
        Fields = [ req "name" TStr; opt "args" (TMap(TUnion("FragmentArg", []))) ] }
      // Phase 679 — `Switch`: declarative branch selection. Phase 768 widened the
      // selector from a StateStore key to ANY binding, so the wire now carries
      // `stateKey` (the compact State form) OR `on` (the general form), and BOTH
      // are Optional here because that is what the wire says — `stateKey` was
      // declared Required until Phase 802 and that statement was simply false
      // (`switch-on-selection.json` carries `on` and no `stateKey`, and the
      // schema leg rejected it as a result). "Exactly one of the two" is a
      // cross-field rule Draft 2020-12 cannot state, so it stays DECODER policy
      // alongside `reject-setstate-value-and-valuefrom` — its exact mirror image
      // — and `reject-missing-switch-statekey` moves into that same set.
      //
      // Fuaran-UI Phase 1122 — `autoAdvanceMs`. The one fact a host cannot
      // recover from the tree: that this switch is meant to MOVE ON ITS OWN, and
      // how often. Every other half of a carousel is already composable — the
      // stage is a `Box`, the branches are the cases, the position is the bound
      // key, the arrows and dots are ordinary controls writing that key — and
      // nothing in any arrangement of those says a timer exists. Optional, so an
      // absent key is the only spelling of "does not advance" and every switch
      // written before this release encodes to the bytes it always did.
      //
      // It is a DURATION and not a boolean because "advances" without an interval
      // is not renderable: a host would have to invent a period, and two hosts
      // inventing different ones is exactly the divergence the corpus exists to
      // prevent. Milliseconds on the `DurationLiteral` precedent.
      //
      // NON-POSITIVE IS REFUSED at decode, not canonicalised — the `Masonry.cols`
      // ruling one file over: `0` reads as "off" to an emitter that has not read
      // the spec, and the language already HAS a spelling for off (absence), so a
      // silent rewrite would make two spellings mean one thing and hide the
      // emitter's misunderstanding.
      { Tag = "Switch"
        Category = "Meta"
        Annotations = Annotations.Empty
        Fields =
          [ opt "autoAdvanceMs" TInt
            req "cases" (TList(TRecord "SwitchCase"))
            keyedNode "default"
            opt "on" (bindingOf TStr)
            opt "stateKey" TStr ] }
      // Phase 679 — `Mount`: a guest fragment host. `inputs` is omitted when
      // empty; `onBubble` is the closure sentinel.
      { Tag = "Mount"
        Category = "Meta"
        Annotations = Annotations.Empty
        Fields =
          [ req "capabilities" (TList TStr)
            req "channel" (TRecord "GuestChannel")
            opt "inputs" (TMap(TUnion("FragmentArg", [])))
            opt "onBubble" (handlerOf "obj" "unknown")
            req "scopeId" TStr ] } ]

/// The real-tier IDL as grown so far: the Display + Layout + Input + Visualisation
/// + meta families over the shared value-unions + enums + records + maps. Children
/// resolve within this one IDL, so any node can nest any kind.
// ─── The op vocabulary (WIRE_FORMAT.md §3.4) ───────────────────────────────
//
// Phase 703. The wire's SECOND ROOT: a payload is a Node or a TreeOp. Modelled as
// `IdlKind`s because an op is structurally what a node kind is — a flat
// `$type`-discriminated object over the same field + optionality model — so every
// leg that walks a kind walks an op unchanged. `Category` is metadata, never
// serialised.
//
// SHAPES ONLY. Apply semantics — §3.4's error mapping, what `UpdateProp`'s dotted
// `path` addresses, whether a `target` resolves, what happens when it does not —
// stay hand-written above the IDL, exactly as decode POLICY does for nodes. The
// IDL states what is on the wire, never what applying it does.
//
// Read from the corpus bytes, not from prose: `InsertChild` carries no `position`
// (removed by Phase 681), and `MoveNode` carries no index either.
let private treeOps: IdlKind list =
    [ { Tag = "Batch"
        Category = "op"
        Annotations = Annotations.Empty
        // The op vocabulary's only recursion, and the only reason `TOp` exists.
        Fields = [ req "ops" (TList TOp) ] }
      { Tag = "EditNode"
        Category = "op"
        Annotations = Annotations.Empty
        // `newKind` is a BARE kind — `{"$type":"Markdown",…}`, no `id` envelope —
        // which is why `TKind` is distinct from `TNode`.
        Fields = [ req "newKind" TKind; req "target" TStr ] }
      { Tag = "InsertChild"
        Category = "op"
        Annotations = Annotations.Empty
        Fields = [ req "child" TNode; req "parentId" TStr ] }
      { Tag = "MoveNode"
        Category = "op"
        Annotations = Annotations.Empty
        Fields = [ req "newParentId" TStr; req "target" TStr ] }
      { Tag = "RemoveNode"
        Category = "op"
        Annotations = Annotations.Empty
        Fields = [ req "target" TStr ] }
      { Tag = "ReorderChildren"
        Category = "op"
        Annotations = Annotations.Empty
        Fields = [ req "newOrder" (TList TStr); req "parentId" TStr ] }
      { Tag = "ReplaceBinding"
        Category = "op"
        Annotations = Annotations.Empty
        // The binding's value type is erased at this position — the op replaces a
        // slot whose type the op itself does not name — so `Binding<Json>`.
        Fields =
          [ req "binding" (TUnion("Binding", [ TJson ]))
            req "slot" TStr
            req "target" TStr ] }
      { Tag = "ReplaceRoot"
        Category = "op"
        Annotations = Annotations.Empty
        Fields = [ req "node" TNode ] }
      { Tag = "UpdateProp"
        Category = "op"
        Annotations = Annotations.Empty
        // `value` is genuinely any JSON: the corpus carries a bare string, a
        // number, and a `$type`-tagged object (`Currency`) at this position,
        // because the target slot's type is whatever `path` addresses.
        Fields = [ req "path" TStr; req "target" TStr; req "value" TJson ] }
      { Tag = "UpdateState"
        Category = "op"
        Annotations = Annotations.Empty
        Fields = [ req "state" (TRecord "StateBehaviour"); req "target" TStr ] }
      { Tag = "UpdateStyle"
        Category = "op"
        Annotations = Annotations.Empty
        Fields = [ req "style" (TRecord "SemanticStyle"); req "target" TStr ] }
      { Tag = "PatchStyle"
        Category = "op"
        Annotations = Annotations.Empty
        Fields = [ req "style" (TRecord "StyleDelta"); req "target" TStr ] }
      { Tag = "UpdateAccessibility"
        Category = "op"
        Annotations = Annotations.Empty
        Fields = [ req "accessibility" (TRecord "Accessibility"); req "target" TStr ] }
      // The one optional op field: an absent `tooltip` is the edit that clears it.
      { Tag = "UpdateTooltip"
        Category = "op"
        Annotations = Annotations.Empty
        Fields = [ req "target" TStr; opt "tooltip" TS ] } ]

/// The vocabulary as DECLARED, before its documentation is laid over it (Phase 1869) — see [[uiIdl]].
let private declared: Idl =
    { Kinds = displayKinds @ layoutKinds @ inputKinds @ visKinds @ metaKinds
      Unions =
        [ textSource
          binding
          cellFormat
          action
          callResultTarget
          formatUnion
          localeSource
          localFlushTrigger
          boxLayout
          formFieldKind
          mediaKind
          columnWidth
          cellKindErased
          holeValueSpace
          scalar
          holeDecl
          fragmentArg
          curveCommand
          shape
          chartAnnotationX
          chartAnnotationRange
          chartAnnotation ]
      Enums =
        [ headingVariant
          linkProtection
          badgeVariant
          orientation
          boxRole
          mathDisplay
          imageVariant
          imageFit
          imageAspect
          imageLoading
          embedPermission
          toneVariant
          styleWeight
          emphasis
          trendPolarity
          scrollOrientation
          buttonVariant
          modalityKind
          fileReadEncoding
          navigateTarget
          captureSource
          dateTimeVariant
          textFormat
          compareOp
          dateStyle
          timeStyle
          relativeTimeUnit
          timeGrain
          durationUnit
          durationStyle
          iconSize
          chartKind
          chartLegendPosition
          chartDataLabels
          chartXScale
          hashStrictness
          hostEffect
          determinismSource
          channelDirection
          textAnchor
          styleRole
          fontVoice
          textDirection
          motion
          liveRegionKind
          sortDirection
          trackKind ]
      Records =
        [ semanticStyleRecord
          styleDeltaRecord
          stateBehaviourRecord
          accessibilityRecord
          switchCase
          guestChannel
          drawPoint
          viewBoxRecord
          drawStyle
          invokeArgRecord
          affordanceAliasRecord
          affordanceRecord
          selectOptionRecord
          selectOptionGroupRecord
          mapMarkerRecord
          defaultSortRecord
          staticRowsRecord
          compareRuleRecord
          fieldRuleRecord
          formFieldRecord
          filterSpecRecord
          transformParamRecord
          rangePairRecord
          dateTimeRangePairRecord
          tabHeaderRecord
          columnErasedRecord
          buttonGroupItemRecord
          contentHashRecord
          srcSetEntryRecord
          trackEntryRecord
          metricComparisonRecord
          markdownCitationRecord
          tableCitationRecord
          treeItemRecord
          effectClassRecord ]
      Defaults = []
      // Phase 690 — the node envelope, Ordinal-ordered like every other field list.
      //
      // All three are `Optional` here, where the hand-written tier stores `state` and
      // `style` as NON-option records and omits them when empty / all-default. Both
      // shapes produce identical wire — absent is absent — but they are different
      // AUTHORING types, and reconciling them is Phase 692's job, not a difference to
      // paper over. `Optional` is chosen because it is what the wire actually says
      // (§3.1: "omitted when empty"), and because an all-default `Some` is a shape the
      // encoder should never be handed rather than one it must silently absorb.
      NodeFields =
        [ opt "accessibility" (TRecord "Accessibility")
          // `WIRE_FORMAT.md` §9 — consumer-authored, deliberately NOT AI-visible, and
          // never emitted. They are on the node because the generated type has to be
          // able to hold everything the authoring type holds (Phase 694), not because
          // the wire has anything to say about them.
          hostOnly "extraAttributes" "Map<string, string> option" "None"
          // Fuaran-UI Phase 1812 — the AUTHOR-DECLARED FALLBACK. A full `Node`
          // that a reader BEHIND this node's kind (WIRE_FORMAT.md §15.3: the kind
          // decodes as a transport-only `Unknown`) renders IN PLACE of its labelled
          // placeholder. It sits on the envelope, not in the kind's spec, for the
          // one reason that decides it: a reader that does not understand the kind
          // cannot open the spec, and the envelope is the only place it can still
          // find something to show.
          //
          // A CURRENT reader decodes it, preserves it (it re-encodes byte-for-byte
          // like any other field) and never renders it — it has the real node. A
          // behind reader lifts it out of the preserved payload without removing
          // it from the bytes, so must-ignore-but-preserve is untouched.
          //
          // The validator (FUARAN156 / FUARAN157) refuses a fallback that carries
          // the kind it stands in for — a fallback the behind reader also cannot
          // read is no fallback — and a fallback nested inside a fallback; NodeId
          // uniqueness (§8.1) holds across the subtree, which is what lets a
          // `TreeOp` address a node inside it like any child position (§3.1).
          opt "fallback" TNode
          hostOnly "motion" "Motion option" "None"
          opt "state" (TRecord "StateBehaviour")
          opt "style" (TRecord "SemanticStyle")
          // Fuaran-UI Phase 1112 — the node-level tooltip trait: a supplementary
          // HINT about the node this envelope wraps, omitted when `None`.
          //
          // It sits here, beside `accessibility`, rather than as a field on the
          // 41 spec records, for the reason the trait tier exists: a hint is
          // uniform across kinds — nothing about "a short supplementary
          // description of this thing" varies with whether the thing is a button
          // or a metric — and 41 per-spec fields would be 41 independently
          // driftable decisions about one concept. It is a `TextSource` and not a
          // `Binding<string>` because a hint is CONTENT: it is authored, it is
          // translated, and `TextSource` is this vocabulary's word for exactly
          // that (`Literal` / `I18n` / `Bound`, so the runtime case is covered
          // too).
          //
          // The GESTURE that reveals it is deliberately absent from the wire.
          // Hover, focus, long-press and touch reveal are the renderer's
          // affordance under the affordance→op charter, so no event name and no
          // placement token is minted here: a document says WHAT the hint is and
          // never HOW it appears.
          opt "tooltip" TS
          // Fuaran-UI Phase 1535 — CONDITIONAL PRESENCE. A `Binding<bool>` whose
          // resolved `false` removes this node from the rendered output
          // entirely: no element, no placeholder, no `aria-hidden`, nothing in
          // the layout and nothing in the accessibility tree.
          //
          // It sits on the envelope, beside `accessibility`, for the reason the
          // trait tier exists: "should this be here at all" is uniform across
          // every kind, and 41 per-spec fields would be 41 independently
          // driftable decisions about one concept.
          //
          // It is deliberately NOT `accessibility.hidden`, and the two are not
          // interchangeable: `hidden` is `aria-hidden` over a node that IS
          // rendered and DOES occupy layout — the right spelling for decorative
          // content a screen reader should skip — while `visible` decides
          // whether the node exists in the output at all. §3.1 states the rule
          // normatively.
          //
          // Absence and failure render the node. A binding that does not resolve
          // is NOT a false: a missing source silently hiding content is the one
          // failure mode a reader cannot see, cannot report and cannot work
          // around, so the unresolved and errored cases both render.
          opt "visible" (bindingOf TBool) ]
      Ops = treeOps
      Wire = WireShape.Default
      // The hardening vocabulary (fuaran-core#116), declared TOKEN BY TOKEN
      // rather than taken from the IDL engine's `HardenPolicy.Default`.
      //
      // Taking the default declared the FIELD and not the TOKENS: this
      // vocabulary said nothing about how IT spells the gated kind, the inert
      // placeholder or the two literal cases, and inherited whatever the engine
      // happened to mean by them — a constant that exists to reproduce
      // pre-fuaran-core#116 behaviour for vocabularies that have not spoken, not
      // a declaration by one that has. Spelled out, a rename on either side is a
      // one-line edit at the site that owns the names, and fuaran-core#180 can
      // retire the default without changing what this vocabulary means.
      //
      // Every token below is the name this vocabulary already uses, so the
      // rendered `idl.json` is byte-for-byte the artifact the default produced.
      // That unchanged artifact IS the evidence the substitution is faithful —
      // `VocabularyTests` renders the vocabulary and fails on any byte that
      // moves, so a token mis-spelled here is a red gate rather than a silent
      // re-meaning of the published document.
      Harden =
        { GatedKind = "Custom"
          PlaceholderKind = "Markdown"
          PlaceholderField = "text"
          TextLiteralCase = "Literal"
          TextLiteralField = "text"
          ValueLiteralCase = "Static"
          ValueLiteralField = "value"
          TransparentUnions = [ "TextSource", "Literal" ] } }

// ─── Phase 1869 — the vocabulary's documentation ───────────────────────────
//
// The IDL is the ONE source of the vocabulary's docs. What a kind, field,
// union, case, enum or record IS is authored here, rides `idl.json` as that
// declaration's `doc` annotation (Fuaran.Core Phase 255 for members, Phase 542
// for types), and every layer generated from the artefact prints it — the
// `///` summaries of `Generated.fs` first, the other hosts as they read it.
//
// It is a TABLE keyed by declaration rather than a `doc` at each declaration
// site, for one reason: a doc moves no wire byte, and laying eleven hundred of
// them inline would rewrite nearly every line of the declarations above, the
// lines every vocabulary change edits. Keyed here, a new declaration costs one
// line, and `VocabularyTests` refuses a declaration with no entry and an entry
// naming no declaration, so the table can neither fall behind the vocabulary
// nor outlive a removal.
//
// Keys: `kind:<Tag>`, `kind-field:<Tag>.<field>`, `op:<Tag>`,
// `op-field:<Tag>.<field>`, `node:<field>`, `union:<Name>`,
// `case:<Union>.<Case>`, `case-field:<Union>.<Case>.<field>`, `enum:<Name>`,
// `enum-case:<Enum>.<HostCase>`, `record:<Name>`, `record-field:<Record>.<field>`.
// A doc is written for the author hovering over the generated member: what it
// is, in a sentence. A kind's doc names its §4b family first.

/// A doc of several paragraphs, one string per line; an empty string is a paragraph break.
let private lines (ls: string list) : string = String.concat "\n" ls

let private docs: (string * string) list =
    [
      // Kinds and their fields
      "kind:Badge", "Display kind (§4b): a short inline status label coloured by a variant."
      "kind-field:Badge.label", "The badge text."
      "kind-field:Badge.variant", "The badge's colour role. Omitted means `Neutral`."
      "kind:Box",
      "Layout kind (§4b): the general container, whose `layout` says how its children arrange and whose `role` says what the container means."
      "kind-field:Box.children", "The child nodes the box arranges, in order."
      "kind-field:Box.heading", "An optional heading rendered above the children. Omitted means no heading."
      "kind-field:Box.layout",
      "How the children arrange: `Flex` flow, an explicit `Grid`, `Masonry` columns or the responsive `Auto` tile."
      "kind-field:Box.role",
      "What the container means (`Dashboard`, `Card`, `Group`, `Separator`), which selects the emitted element, ARIA landmark and chrome."
      "kind-field:Box.keepTogether",
      "When the rendering is paged, keep this box and everything under it on one page; a continuous medium ignores it. Omitted on the wire at `false`."
      "kind-field:Box.breakBefore",
      "When the rendering is paged, start this box at the top of a fresh page; a continuous medium ignores it. Omitted on the wire at `false`."
      "kind:Button", "Input kind (§4b): a clickable control that dispatches an `Action`."
      "kind-field:Button.label", "The visible button text."
      "kind-field:Button.onClick", "The `Action` dispatched when the button is activated."
      "kind-field:Button.variant", "The button's visual emphasis. Omitted means `Secondary`."
      "kind-field:Button.icon", "An optional icon name rendered with the label. Omitted means no icon."
      "kind-field:Button.tooltip",
      "Superseded host-only hint that never reaches the wire (§10.1); use the node-level `Tooltip` trait instead."
      "kind-field:Button.disabled",
      "A binding that disables the button while it resolves to `true`. Omitted means always enabled."
      "kind-field:Button.affordance",
      "The operability `Affordance` declaring how an agent-initiated instruction may address this control (§31). Omitted means none beyond what the node already says."
      "kind:Callout",
      "Display kind (§4b): an inline banner for a status, tier or mode message, sized for content blocks."
      "kind-field:Callout.body", "The callout's message text."
      "kind-field:Callout.dismissable",
      "Whether the callout offers a close affordance. Omitted means `false` (the opposite default to `Toast.dismissable`)."
      "kind-field:Callout.tone", "The status colour of the banner. Omitted means `Default`."
      "kind-field:Callout.heading", "An optional title shown above the body. Omitted means no heading."
      "kind-field:Callout.icon", "An optional icon name shown beside the message. Omitted means no icon."
      "kind:Chart", "Visualisation kind (§4b): a chart of bound rows, plotting `yFields` against `xField`."
      "kind-field:Chart.kind", "The chart type: line, bar, area, pie, scatter or heatmap."
      "kind-field:Chart.source",
      "The binding that resolves to the rows the chart plots, each row a map of column name to value."
      "kind-field:Chart.stacked", "Whether series are stacked rather than drawn side by side. Omitted means `false`."
      "kind-field:Chart.xField", "The row column plotted on the x axis."
      "kind-field:Chart.yFields",
      "The row columns plotted as series. An empty list, admitted only over a `Query` source, plots every numeric column but `xField` (§3.6.29)."
      "kind-field:Chart.title", "An optional chart title. Omitted means no title."
      "kind-field:Chart.valueFormat",
      "The number format of the value axis and its labels, in the `Format` vocabulary. Absent means the chart's default rendering (thousands separators and step-derived decimals)."
      "kind-field:Chart.xTitle",
      "The x axis's name. Absent falls back to the capitalised x field name, so an axis is never nameless."
      "kind-field:Chart.yTitle", "An optional name for the value (y) axis. Omitted means the axis is unlabelled."
      "kind-field:Chart.subtitle",
      "A muted line under the title, the natural place to state units; when present the chart does not add its own display-unit note."
      "kind-field:Chart.legendPosition",
      "Where the legend sits, or `None` for no legend. Absent means the host style's default position (the right edge), not \"no legend\"."
      "kind-field:Chart.dataLabels", "Whether each value is written onto the plot beside its mark. Absent means `Off`."
      "kind-field:Chart.xScale",
      "What the x column means: discrete categories, or dates on a continuous temporal scale. Absent means `Category`; `Temporal` over a non-date column is refused before emission (FUARAN097)."
      "kind-field:Chart.annotations",
      "Data-addressed annotations drawn over the chart, such as reference lines. Omitted means none."
      "kind-field:Chart.onPointClick",
      "An in-process handler called with the clicked point's row; it travels on the wire only as a `\"<closure>\"` sentinel."
      "kind:CodeBlock",
      "Display kind (§4b): a block of source code rendered as an escaped `<pre><code>`, never parsed as markdown."
      "kind-field:CodeBlock.code", "The raw source text, HTML-escaped at render."
      "kind-field:CodeBlock.copyable",
      "Whether a copy affordance is rendered. Omitted means `true`: a code block is copyable unless it says otherwise."
      "kind-field:CodeBlock.highlightLines", "The 1-based line numbers to mark as highlighted. Omitted means none."
      "kind-field:CodeBlock.language",
      "The language hint, emitted as a `language-<x>` class for highlighting (F* and F# get a deterministic highlighting tier in the reference renderers)."
      "kind-field:CodeBlock.lineNumbers", "Whether a line-number gutter is shown. Omitted means `false`."
      "kind:Custom",
      "Meta kind (§4b): the host-registered escape hatch that renders a component the host supplies, with an opaque prop bag."
      "kind-field:Custom.moduleId", "The host module that registers the component."
      "kind-field:Custom.componentId", "The component's id within its module."
      "kind-field:Custom.props",
      "The component's props as verbatim JSON, opaque to the wire and validated by the host renderer."
      "kind-field:Custom.contentHash",
      "The `ContentHash` derived from the component's declared shape, checked by the host at render time. Omitted opts out of the check."
      "kind-field:Custom.exposedNodeIds",
      "Interior node ids the component exposes for addressing. Omitted means the renderer does not introspect its interior."
      "kind:DataGrid",
      "Visualisation kind (§4b): a data grid of columns over bound rows, or over static rows for a read-only table."
      "kind-field:DataGrid.columns", "The grid's column definitions, in display order."
      "kind-field:DataGrid.editable", "Whether cells may be edited. Omitted means `false`."
      "kind-field:DataGrid.rowKey",
      "An in-process closure computing each row's stable key, overriding `rowKeyField`; it travels on the wire only as a sentinel."
      "kind-field:DataGrid.rowKeyField",
      "The row column that gives each row its stable identity. Omitted means no declared key."
      "kind-field:DataGrid.sortStateKey",
      "The State key holding the sort descriptor `{\"column\": <index>, \"direction\": \"asc\"|\"desc\"}`; when set, column headers sort the rows and a header click writes the toggled descriptor. Omitted when absent."
      "kind-field:DataGrid.pageSize",
      "How many rows one page holds; with `pageStateKey` set the grid renders a pager and shows one page at a time. Omitted when absent."
      "kind-field:DataGrid.pageStateKey",
      "The State key carrying the current page as `{\"page\": <1-based int>}`, enabling declarative pagination with `pageSize`. Omitted means the grid is not paged."
      "kind-field:DataGrid.defaultSort",
      "The initial row order, applied while the sort state key holds nothing; once the reader sorts, the state wins. Valid with or without `sortStateKey`."
      "kind-field:DataGrid.editStateKey",
      "The State key an edited cell's whole updated rows value is committed to. Absent writes back to the grid's own `source` when it is a direct `Binding.State`, and is display-only otherwise."
      "kind-field:DataGrid.reorderable",
      "Let the reader reorder rows by dragging; the reordered rows commit to `editStateKey`. Omitted on the wire at `false`."
      "kind-field:DataGrid.transferInKey",
      "The State key on which this grid accepts rows dragged from another grid that names the same key as its `transferOutKey`. Omitted when absent."
      "kind-field:DataGrid.transferOutKey",
      "The State key to which this grid may release rows, for a grid naming the same key as its `transferInKey`. Omitted when absent."
      "kind-field:DataGrid.keepRowsTogether",
      "When the rendering is paged, never split a row across a page boundary; a continuous medium ignores it. Omitted on the wire at `false`."
      "kind-field:DataGrid.repeatHeader",
      "When the rendering is paged, repeat the column headers at the top of every page the grid continues onto. Omitted on the wire at `false`."
      "kind-field:DataGrid.exportable",
      "Offer the reader a control that downloads the rows the client holds, sorted as shown, as an RFC 4180 CSV file; nothing is written back to the tree. Omitted on the wire at `false`."
      "kind-field:DataGrid.windowStateKey",
      "The State key holding the row window `{\"offset\": <int>, \"count\": <int>}` the renderer writes as the viewport moves; the grid presents that slice of its rows. Absent or malformed means every row is presented."
      "kind-field:DataGrid.rowTotal",
      "The declared size of the whole result set, for a grid whose host returns it a page or window at a time, so the pager and scroll extent can show the total. A value that is not an integer of zero or more declares no total."
      "kind-field:DataGrid.source",
      "The binding that resolves to the grid's rows, each row a map of column name to value."
      "kind-field:DataGrid.staticRows",
      "Fixed header and row text for a static read-only grid. Omitted means rows come from `source`."
      "kind-field:DataGrid.onRowClick",
      "An in-process handler called with the clicked row; it travels on the wire only as a `\"<closure>\"` sentinel."
      "kind:Disclosure",
      "Layout kind (§4b): a collapsible section with a heading that the reader can expand and collapse."
      "kind-field:Disclosure.children", "The content shown while the section is open."
      "kind-field:Disclosure.defaultOpen", "Whether the section starts open on first mount, before `open` resolves."
      "kind-field:Disclosure.heading", "The always-visible heading that toggles the section."
      "kind-field:Disclosure.onToggle",
      "An in-process handler called with the new open state. Omitted means a toggle is written back to `open` when it is bound to State or a filter."
      "kind-field:Disclosure.open", "The controlled open state of the section."
      "kind:Drawing",
      "Display kind (§4b): a resolved vector drawing of shapes in a user-space coordinate box, the render target charts lower to."
      "kind-field:Drawing.description",
      "An optional long description, emitted as the drawing's `<desc>`. Omitted means none."
      "kind-field:Drawing.shapes", "The shapes to draw, in painter's order (later shapes paint over earlier ones)."
      "kind-field:Drawing.style",
      "The root `DrawStyle`, inherited by shapes that omit their own; an empty style is legitimate."
      "kind-field:Drawing.title",
      "An optional accessible name, emitted as the drawing's `<title>` with `role=img`. Omitted means no title."
      "kind-field:Drawing.viewBox", "The user-space coordinate box the shapes are drawn in, mirroring SVG's `viewBox`."
      "kind:Embed",
      "Display kind (§4b): a third-party document rendered in a maximally sandboxed frame, for content that cannot cooperate with the host (use `Mount` for a cooperating guest)."
      "kind-field:Embed.aspectRatio", "The box ratio the frame reserves before it loads. Omitted means `Natural`."
      "kind-field:Embed.permissions",
      "The sandbox relaxations granted to the framed document. Omitted (the empty list) means total denial."
      "kind-field:Embed.src", "The URL of the document to frame; only `https` is admitted, with no relative references."
      "kind-field:Embed.title", "The frame's accessible name; required, and an empty literal is refused (FUARAN111)."
      "kind:ErrorBoundary",
      "Meta kind (§4b): renders `child`, and renders `fallback` in its place if the child fails to render."
      "kind-field:ErrorBoundary.child", "The subtree rendered normally."
      "kind-field:ErrorBoundary.fallback", "The subtree rendered instead when `child` fails."
      "kind:Fact",
      "Display kind (§4b): a labelled text fact such as Patient: Alice Smith, the text counterpart of the numeric `LabelValueRow`."
      "kind-field:Fact.emphasis", "Whether the value gets KPI-tile prominence. Omitted means `false`."
      "kind-field:Fact.help", "Optional small print shown under the label. Omitted means none."
      "kind-field:Fact.icon", "An optional icon name shown with the fact. Omitted means no icon."
      "kind-field:Fact.label", "The fact's label."
      "kind-field:Fact.tone", "The status colour of the fact. Omitted means `Default`."
      "kind-field:Fact.value", "The fact's text value, which may be literal, translated or bound."
      "kind-field:Fact.reference",
      "The citation naming the fact behind this value, a bare string the language never parses or resolves; a renderer stamps it as `data-fact` (§3.6.31). Omitted means no citation."
      "kind:FileUpload", "Input kind (§4b): a file picker, with optional drop, paste and device-capture gestures."
      "kind-field:FileUpload.accept",
      "The file types the picker offers, as MIME types or extensions; an empty list accepts any file."
      "kind-field:FileUpload.label", "The control's visible label."
      "kind-field:FileUpload.multiple", "Whether more than one file may be selected. Omitted means `false`."
      "kind-field:FileUpload.onSelect",
      "An in-process handler called with the selected files' metadata; it travels on the wire only as a `\"<closure>\"` sentinel."
      "kind-field:FileUpload.disabled",
      "A binding that disables the control while it resolves to `true`. Omitted means always enabled."
      "kind-field:FileUpload.acceptPaste",
      "Whether files pasted from the clipboard are accepted. Omitted means `false`."
      "kind-field:FileUpload.dropTarget",
      "Whether files may be dragged and dropped onto the control. Omitted means `false`."
      "kind-field:FileUpload.capture",
      "A device (`Camera` or `Microphone`) the control captures a new file from. Omitted means the ordinary file picker."
      "kind-field:FileUpload.destination",
      "The name of a host-registered upload destination the selection streams to; a name the host does not register is refused. Omitted means nothing leaves the client."
      "kind-field:FileUpload.maxBytes",
      "The largest accepted size of each file, in bytes; must be positive. Omitted means no declared ceiling."
      "kind-field:FileUpload.maxFiles",
      "The most files one selection may hold, meaningful only with `multiple`; must be positive. Omitted means no declared ceiling."
      "kind:Filters", "Input kind (§4b): a strip of filter controls whose values feed the filter store."
      "kind-field:Filters.items", "The filter controls, each a `FilterSpec` whose `name` is its filter-store key."
      "kind:Form", "Input kind (§4b): an ordered list of fields with a submit control."
      "kind-field:Form.fields", "The form's fields, in display order."
      "kind-field:Form.onSubmit", "The `Action` dispatched when the form is submitted."
      "kind-field:Form.submitLabel", "The submit control's text."
      "kind-field:Form.disabled",
      "A binding that disables the form while it resolves to `true`. Omitted means always enabled."
      "kind:FragmentDecl",
      "Meta kind (§4b): declares a named, reusable fragment with typed holes that a `FragmentRef` expands."
      "kind-field:FragmentDecl.body", "The fragment's node tree."
      "kind-field:FragmentDecl.name",
      "The name a `FragmentRef` addresses this fragment by; non-empty with no whitespace."
      "kind-field:FragmentDecl.holes",
      "The fragment's typed parameters: value holes and tree slots. Omitted means a fixed body with no holes."
      "kind-field:FragmentDecl.effect",
      "The fragment's declared `EffectClass` (determinism and host effect). Omitted means pure and deterministic."
      "kind:FragmentRef",
      "Meta kind (§4b): renders a declared fragment by name, binding its holes; an isolation boundary whose interior ids are namespaced (§8.1)."
      "kind-field:FragmentRef.name", "The name of the `FragmentDecl` to render."
      "kind-field:FragmentRef.args",
      "The hole bindings by hole name, each a scalar value or a slot subtree. Omitted means a name-only reference."
      "kind:Heading", "Display kind (§4b): a section heading rendered as `<h1>`–`<h6>`."
      "kind-field:Heading.level", "The heading rank, rendered as the matching `<h1>`–`<h6>` element."
      "kind-field:Heading.text", "The heading text."
      "kind-field:Heading.variant",
      "A presentation variant (eyebrow, caption, lead) added as a class without changing the heading element. Omitted means `Standard`."
      "kind:Icon",
      lines
          [ "Phase 821 — the standalone icon-only display kind: a decorative or"
            "labelled glyph with no Button / Image envelope. `Icon` names a"
            "glyph from the existing icon vocabulary (the `data-icon` hook); `Label ="
            "None` is decorative (`aria-hidden=\"true\"`), `Some` is meaningful"
            "(`role=\"img\"` + `aria-label`)." ]
      "kind-field:Icon.icon", "The icon name to render."
      "kind-field:Icon.size", "The glyph size. Omitted means `Medium`."
      "kind-field:Icon.tone", "The glyph colour. Omitted means `Default`."
      "kind-field:Icon.label",
      "The icon's accessible name. Omitted means the icon is decorative and hidden from assistive technology."
      "kind:Image",
      "Display kind (§4b): an image whose source is routed through the §19 URL-scheme floor at render time."
      "kind-field:Image.alt", "The image's text alternative; required."
      "kind-field:Image.src", "The image URL, static or bound, sanitised at render time."
      "kind-field:Image.variant", "The crop treatment (plain, avatar or rounded). Omitted means `Default`."
      "kind-field:Image.fit",
      "How the image fills its box: intrinsic size, cover-and-crop or contain-and-letterbox. Omitted means `Natural`."
      "kind-field:Image.aspectRatio",
      "The box ratio reserved before the image loads, which prevents layout shift. Omitted means `Natural`."
      "kind-field:Image.loading",
      "Whether the image is fetched at initial load or deferred until near the viewport. Omitted means `Eager`."
      "kind-field:Image.srcSet", "Alternate renditions the browser may choose between. Omitted means none."
      "kind-field:Image.expandable",
      "Whether the full-size asset is reachable from the rendered image, via a real link a script tier may upgrade to an overlay. Omitted means `false`."
      "kind-field:Image.caption",
      "An optional caption; present, the image renders as a `<figure>` with `<figcaption>`. Omitted means a bare image."
      "kind:LabelValueRow",
      "Display kind (§4b): a label beside a formatted numeric value, such as one line of a summary."
      "kind-field:LabelValueRow.emphasis", "Whether the row is emphasised, as for a total. Omitted means `false`."
      "kind-field:LabelValueRow.format",
      "How the number is formatted for display. Omitted means `None` (no formatting)."
      "kind-field:LabelValueRow.label", "The row's label."
      "kind-field:LabelValueRow.value", "The numeric value, static or bound."
      "kind-field:LabelValueRow.help", "Optional small print shown under the label. Omitted means none."
      "kind:Link", "Display kind (§4b): a hyperlink whose destination is sanitised at render time."
      "kind-field:Link.href", "The link destination, static or bound."
      "kind-field:Link.label", "The link text."
      "kind-field:Link.download", "Whether the link carries a `download` attribute. Omitted means `false`."
      "kind-field:Link.rel", "An optional `rel` attribute value, such as `noopener`. Omitted means none."
      "kind-field:Link.target", "An optional `target` attribute value, such as `_blank`. Omitted means none."
      "kind-field:Link.protection",
      "An optional anti-scraper render strategy; `Email` keeps a `mailto:` address out of plaintext markup without altering the href. Omitted means an ordinary link."
      "kind:List", "Display kind (§4b): a bulleted or numbered list of text items."
      "kind-field:List.items", "The list items, in order."
      "kind-field:List.ordered", "Whether the list is numbered. Omitted means `false` (bulleted)."
      "kind:Map", "Visualisation kind (§4b): a geographic map showing bound markers."
      "kind-field:Map.centreLatitude", "The latitude of the map's centre, in WGS-84 decimal degrees."
      "kind-field:Map.centreLongitude", "The longitude of the map's centre, in WGS-84 decimal degrees."
      "kind-field:Map.source", "The binding that resolves to the markers shown on the map."
      "kind-field:Map.zoom", "The map's zoom level."
      "kind-field:Map.onMarkerClick",
      "An in-process handler called with the clicked marker; it travels on the wire only as a `\"<closure>\"` sentinel."
      "kind:Markdown", "Display kind (§4b): a block of markdown text rendered by a deterministic renderer."
      "kind-field:Markdown.citations",
      "The figures the text cites, each placed by a `cite:<name>` link destination (§3.6.32). Omitted means none."
      "kind-field:Markdown.text", "The markdown source."
      "kind:Math",
      "Display kind (§4b): a LaTeX formula, rendered as escaped source with optional client-side typesetting."
      "kind-field:Math.source", "The LaTeX source of the formula."
      "kind-field:Math.display", "Whether the formula renders inline or as a block."
      "kind:Media", "Display kind (§4b): a video or audio player."
      "kind-field:Media.controls",
      "Whether the playback controls are shown. Omitted means `true`, so a keyboard user can always pause."
      "kind-field:Media.kind", "Whether this is a video (with its video-only options) or an audio player."
      "kind-field:Media.label", "The player's accessible name; required, because a transport is never decorative."
      "kind-field:Media.loop", "Whether playback restarts at the end. Omitted means `false`."
      "kind-field:Media.src", "The media URL, static or bound, routed through the §19 URL-scheme floor at render time."
      "kind-field:Media.tracks",
      "Timed-text tracks such as captions and subtitles, in authored order. Omitted means none."
      "kind-field:Media.transcript",
      "An optional text transcript, rendered as a disclosure beside the player. Omitted means no transcript is offered."
      "kind:Metric", "Display kind (§4b): a KPI tile showing a labelled, formatted number with optional trend lines."
      "kind-field:Metric.label", "The metric's label."
      "kind-field:Metric.value", "The headline number, static or bound."
      "kind-field:Metric.format", "How the headline number is formatted. Omitted means `None` (no formatting)."
      "kind-field:Metric.tone", "The colour of the tile, saying how this reading stands. Omitted means `Default`."
      "kind-field:Metric.weight", "The tile's density. Omitted means `Standard`."
      "kind-field:Metric.emphasis", "The tile's visual prominence. Omitted means `Normal`."
      "kind-field:Metric.trend", "An optional change value shown as the primary trend line. Omitted means no trend."
      "kind-field:Metric.trendFormat",
      "An optional format for the trend value. Omitted means the trend is not given its own format."
      "kind-field:Metric.trendPolarity",
      "Which direction of this quantity is good, used to colour the trend's sentiment without changing its text. Omitted means `HigherIsBetter`; inert when there is no `trend`."
      "kind-field:Metric.comparisons",
      "Further comparisons, each rendered as one more trend line under the primary trend. Omitted means none."
      "kind-field:Metric.icon", "An optional icon name shown on the tile. Omitted means no icon."
      "kind-field:Metric.subtext", "Optional supporting text shown under the value. Omitted means none."
      "kind:Modal",
      "Layout kind (§4b): an overlay shown while `open` is true, either a blocking dialog or an anchored popover."
      "kind-field:Modal.children", "The overlay's content."
      "kind-field:Modal.dismissable",
      "Whether the reader may dismiss the overlay (close button, backdrop, Esc). Omitted means `true`."
      "kind-field:Modal.onDismiss",
      "The `Action` dispatched on dismissal. Omitted means `false` is written back to `open` when it is bound to State or a filter."
      "kind-field:Modal.open", "The controlled visibility of the overlay."
      "kind-field:Modal.heading", "An optional dialog title. Omitted means no title."
      "kind-field:Modal.modality",
      "Which overlay this is: a blocking `Modal` dialog or an anchored `Popover`. Omitted means `Modal`."
      "kind-field:Modal.anchor",
      "The id of the node a `Popover` is positioned against; meaningful for `Popover` only. Omitted means unanchored."
      "kind:Mount",
      "Meta kind (§4b): an isolation boundary that hosts a guest in its own scope, produced host-side and never inlined (§8.1)."
      "kind-field:Mount.capabilities",
      "The capability tags the guest requests, which the host's gate consults; an empty list denies every host-affecting action."
      "kind-field:Mount.channel", "The declared host-guest message channel and its direction."
      "kind-field:Mount.inputs",
      "The guest's configuration and initial state, bound by hole name. Omitted means no inputs."
      "kind-field:Mount.onBubble",
      "An in-process handler for actions the guest bubbles to the host; it travels on the wire only as a `\"<closure>\"` sentinel."
      "kind-field:Mount.scopeId", "The id of the guest's runtime scope."
      "kind:Progress", "Display kind (§4b): a progress indicator for a long-running task."
      "kind-field:Progress.fraction", "The completed fraction, from 0 to 1, static or bound."
      "kind-field:Progress.indeterminate",
      "Whether the indicator shows activity with no measurable bound; pair it with a `caveat`. Omitted means `false`."
      "kind-field:Progress.tone", "The indicator's colour. Omitted means `Default`."
      "kind-field:Progress.label", "An optional label describing the task. Omitted means none."
      "kind-field:Progress.caveat",
      "An optional note qualifying the progress, such as why no honest bound exists. Omitted means none."
      "kind:ScrollArea", "Layout kind (§4b): a scrollable region around its children."
      "kind-field:ScrollArea.children", "The content that scrolls."
      "kind-field:ScrollArea.orientation", "The scroll axis: vertical, horizontal or both."
      "kind-field:ScrollArea.maxHeight", "The viewport's maximum height in pixels. Omitted means no height ceiling."
      "kind-field:ScrollArea.maxWidth", "The viewport's maximum width in pixels. Omitted means no width ceiling."
      "kind:Select", "Input kind (§4b): a dropdown choosing one option, or several when `multiple` is true."
      "kind-field:Select.label", "The control's visible label."
      "kind-field:Select.onChange",
      "An in-process handler called with the new single selection; it travels on the wire only as a `\"<closure>\"` sentinel."
      "kind-field:Select.onChangeMulti",
      "An in-process handler called with the new multi-selection; it travels on the wire only as a `\"<closure>\"` sentinel."
      "kind-field:Select.source", "The binding that resolves to the selectable options."
      "kind-field:Select.value",
      "The single-select's selected value. Required unless `multiple` is true, and absent when it is (§3.2)."
      "kind-field:Select.placeholder", "Text shown while nothing is selected. Omitted means no placeholder."
      "kind-field:Select.disabled",
      "A binding that disables the control while it resolves to `true`. Omitted means always enabled."
      "kind-field:Select.multiple",
      "Whether several options may be selected, carried in `values`. Omitted means a single-select."
      "kind-field:Select.values", "The multi-select's selected values. Used when `multiple` is true."
      "kind-field:Select.affordance",
      "The operability `Affordance` declaring how an agent-initiated instruction may address this control (§31). Omitted means none beyond what the node already says."
      "kind-field:Select.groups",
      "Named groups of the options, by option value; options no group names render after them, ungrouped. Omitted means no grouping."
      "kind:Skeleton", "Display kind (§4b): a loading placeholder of blank rows shown before content arrives."
      "kind-field:Skeleton.rows", "The number of placeholder rows, bounded by `MaxSkeletonRows` (§21.9)."
      "kind:Sparkline", "Display kind (§4b): a small inline line chart of a numeric series."
      "kind-field:Sparkline.source", "The numeric series to plot, static or bound."
      "kind:SplitPanel", "Layout kind (§4b): two side-by-side panes, the first child in one and the rest in the other."
      "kind-field:SplitPanel.children",
      "The panes' content: the first child fills the first pane and the remaining children the second."
      "kind-field:SplitPanel.weight",
      "The first pane's share of the width, from 0 to 1 (clamped); the second pane takes the remainder."
      "kind:Stepper", "Layout kind (§4b): a step sequence whose children are the steps, one of them active."
      "kind-field:Stepper.activeStep", "The index of the active step. Omitted means a static `0`."
      "kind-field:Stepper.children", "The steps, in order."
      "kind-field:Stepper.onSelect",
      "An in-process handler called with the clicked step's index. Omitted means clicking a step dispatches nothing."
      "kind:SummaryList", "Layout kind (§4b): a list of summary rows under an optional heading."
      "kind-field:SummaryList.children", "The list's rows, in order."
      "kind-field:SummaryList.heading", "An optional heading above the list. Omitted means no heading."
      "kind:Switch",
      "Meta kind (§4b): renders the first case whose `match` equals the selector's value, or `default` when none does."
      "kind-field:Switch.autoAdvanceMs",
      "The interval, in milliseconds, at which the switch advances to its next case on its own; must be positive. Omitted means it does not advance."
      "kind-field:Switch.cases",
      "The ordered cases, each a `match` string and the child it renders; the first match wins."
      "kind-field:Switch.default", "The node rendered when no case matches, and before the selector first resolves."
      "kind-field:Switch.on",
      "The general binding whose string value selects the case. Exactly one of `on` and `stateKey` is present."
      "kind-field:Switch.stateKey",
      "The State key whose value selects the case, the compact form of `on`. Exactly one of `on` and `stateKey` is present."
      "kind:Tabs", "Layout kind (§4b): a tabbed container showing one child at a time."
      "kind-field:Tabs.activeIndex", "The index of the active tab. Omitted means a static `0`."
      "kind-field:Tabs.children", "The tab panels, one per tab, in order."
      "kind-field:Tabs.orientation",
      "Whether the tab strip runs horizontally or vertically. Omitted means `Horizontal`."
      "kind-field:Tabs.onSelect",
      "An in-process handler called with the clicked tab's index. Omitted means the index is written back to `activeIndex` when it is bound to State or a filter."
      "kind-field:Tabs.onSelectTag",
      "An in-process handler called with the clicked tab's tag. Omitted means the tag is written back to `activeTag` when it is bound."
      "kind-field:Tabs.tabHeaders",
      "Per-tab headers (label, icon, disabled), one per child. Omitted means headings are inferred from the children."
      "kind-field:Tabs.tabTags",
      "A stable string tag for each tab, one per child. Omitted means tabs are addressed by index only."
      "kind-field:Tabs.activeTag",
      "The tag of the active tab, an alternative to `activeIndex` when `tabTags` are declared. Omitted means selection follows `activeIndex`."
      "kind:Toast",
      "Display kind (§4b): an in-tree notification announced through a live region and shown while its `open` binding is true."
      "kind-field:Toast.dismissable",
      "Whether the toast offers a close affordance. Omitted means `true` (the opposite default to `Callout.dismissable`)."
      "kind-field:Toast.message", "The notification text."
      "kind-field:Toast.open", "Whether the toast is shown; resolving `true` shows it."
      "kind-field:Toast.tone", "The status colour of the toast. Omitted means `Default`."
      "kind:Tree",
      "Display kind (§4b): a hierarchy of `TreeItem` rows the reader walks with one focus, opening and closing rows, following the WAI-ARIA tree pattern."
      "kind-field:Tree.expandedStateKey",
      "An optional State key holding the array of open row ids. Omitted means the tree renders fully expanded and does not toggle."
      "kind-field:Tree.items", "The top-level rows; row ids must be unique within the tree."
      "kind-field:Tree.onSelect",
      "An optional handler called with the selected item's id; it is a closure, so canonical wire carries only the `<closure>` sentinel. Omitted means no dispatch on selection."
      "kind-field:Tree.selectionStateKey",
      "An optional State key holding the selected row id. Omitted means the tree does not select and emits no `aria-selected`."
      // Tree ops and their fields
      "op:Batch",
      "Tree op (§3.4): applies a list of ops all-or-nothing, in order; the first failing op aborts the batch and leaves the tree as it was."
      "op-field:Batch.ops", "The ops to apply, in order; each may itself be a `Batch`."
      "op:EditNode",
      "Tree op (§3.4): swaps the addressed node's kind, keeping its id and every envelope trait such as style, state and accessibility."
      "op-field:EditNode.newKind", "The replacement kind, written as a bare kind object with no `id` envelope."
      "op-field:EditNode.target", "The id of the node whose kind is replaced."
      "op:InsertChild",
      "Tree op (§3.4): appends a new child node to the addressed parent; to place it elsewhere, follow it with a `ReorderChildren`."
      "op-field:InsertChild.child", "The node to insert, appended as the parent's last child."
      "op-field:InsertChild.parentId",
      "The id of the parent that receives the child; it must be a kind that has children."
      "op:MoveNode",
      "Tree op (§3.4): moves an existing node under a new parent, appending it as that parent's last child."
      "op-field:MoveNode.newParentId",
      "The id of the parent the node moves under; moving a node into its own subtree is refused."
      "op-field:MoveNode.target", "The id of the node to move."
      "op:PatchStyle",
      "Tree op (§3.4): sets only the style fields the delta names on the addressed node, leaving every other style field as it is."
      "op-field:PatchStyle.style",
      "The style fields to set; an absent member is left unchanged and a present one is set, a default token included. An empty delta is refused."
      "op-field:PatchStyle.target", "The id of the node whose style is patched."
      "op:RemoveNode",
      "Tree op (§3.4): removes the addressed node and its subtree from its parent; the root cannot be removed."
      "op-field:RemoveNode.target", "The id of the node to remove."
      "op:ReorderChildren", "Tree op (§3.4): reorders a parent's children by listing their ids in the new order."
      "op-field:ReorderChildren.newOrder",
      "The parent's current child ids in their new order; it must be an exact permutation of them."
      "op-field:ReorderChildren.parentId", "The id of the parent whose children are reordered."
      "op:ReplaceBinding", "Tree op (§3.4): replaces one binding-typed slot on the addressed node."
      "op-field:ReplaceBinding.binding",
      "The new binding; its value type is checked against the slot when the op is applied."
      "op-field:ReplaceBinding.slot", "The name of the binding-typed field to replace, such as `Source` or `Trend`."
      "op-field:ReplaceBinding.target", "The id of the node whose binding is replaced."
      "op:ReplaceRoot",
      "Tree op (§3.4): replaces the whole tree with a new root node; the only op that can change the root id."
      "op-field:ReplaceRoot.node", "The new root node."
      "op:UpdateAccessibility",
      "Tree op (§3.4): replaces the addressed node's whole `Accessibility` record; a record setting no member removes the trait."
      "op-field:UpdateAccessibility.accessibility",
      "The new accessibility record; a member it leaves absent is cleared."
      "op-field:UpdateAccessibility.target", "The id of the node whose accessibility is replaced."
      "op:UpdateProp", "Tree op (§3.4): sets one field of the addressed node's spec, addressed by a dotted path."
      "op-field:UpdateProp.path",
      "The field to set, as a dotted path with optional 0-based indices such as `Label` or `Columns[0].Label`; the grammar is checked when the op is applied, not at decode."
      "op-field:UpdateProp.target", "The id of the node whose field is set."
      "op-field:UpdateProp.value",
      "The new field value as JSON; it is read by the addressed field's own decode rules, so a mistyped value is refused rather than coerced."
      "op:UpdateState",
      "Tree op (§3.4): replaces the addressed node's `StateBehaviour`, its loading, empty and error surfaces."
      "op-field:UpdateState.state", "The new state behaviour for the node."
      "op-field:UpdateState.target", "The id of the node whose state behaviour is replaced."
      "op:UpdateStyle",
      "Tree op (§3.4): replaces the addressed node's whole `SemanticStyle`; use `PatchStyle` to change only some fields."
      "op-field:UpdateStyle.style",
      "The new style record; a field left at its default resets the node's value to that default."
      "op-field:UpdateStyle.target", "The id of the node whose style is replaced."
      "op:UpdateTooltip", "Tree op (§3.4): sets or clears the addressed node's tooltip."
      "op-field:UpdateTooltip.target", "The id of the node whose tooltip is set."
      "op-field:UpdateTooltip.tooltip", "The new tooltip text. Omitted clears the tooltip."
      // The node envelope
      "node:accessibility",
      "Optional ARIA metadata for this node (accessible name, role, hidden, live region, spoken text). Omitted means none."
      "node:extraAttributes",
      "Host-only extra attributes (such as `data-*` test hooks) for the rendered element; never emitted on the wire."
      "node:fallback",
      "An optional node a reader that does not understand this node's kind renders in its place (§15.3); it must not itself use that kind or nest another fallback. Omitted means a labelled placeholder."
      "node:motion", "Host-only animation token for this node; never emitted on the wire. Omitted means no animation."
      "node:state",
      "Optional nodes to render while this node is loading or empty, and a handler for its error state. Omitted means none."
      "node:style",
      "Optional semantic style for this node: direction, emphasis, role, tone, voice and weight. Omitted means every default."
      "node:tooltip",
      "An optional supplementary hint about this node, linked with `aria-describedby`; it describes the node and never names it, and the renderer chooses how it appears. Omitted means no tooltip."
      "node:visible",
      "An optional condition; resolving `false` removes this node from the output entirely, unlike `accessibility.hidden`. Omitted, unresolved or errored, the node renders."
      // Value unions, their cases and the cases' members
      "union:Action",
      "What an interaction does: navigate, write state, call or notify the host, open a dialogue, or run a chain of these, each a `$type`-tagged case."
      "case:Action.Chain", "Runs several actions in order."
      "case-field:Action.Chain.ops", "The actions to run, in order."
      "case:Action.WriteToClipboard",
      lines
          [ "Write `text` to the reader's clipboard."
            ""
            "Phase 1126 — the payload is a `TextSource`, so what a reader copies may"
            "be a bound value or a computed reference rather than only a literal the"
            "author typed. The case was WIDENED rather than joined by a"
            "`WriteToClipboardBound` sibling: two cases for one intent is the"
            "permanent near-synonym pair the vocabulary charter exists to forbid, and"
            "a source break that the compiler names once is cheaper than a"
            "vocabulary that stays ambiguous forever."
            ""
            "**The wire does not move for a literal payload.** `TextSource.Literal`"
            "is canonically the bare JSON string, so"
            "`{\"$type\":\"WriteToClipboard\",\"text\":\"…\"}` is emitted and accepted"
            "exactly as it was before this release. Construction sites are what"
            "break, and they break at compile time: wrap the old argument in"
            "`TextSource.Literal`."
            ""
            "A `Bound` payload resolves at DISPATCH time, through the same binding"
            "resolver the surrounding tree renders through — never at decode time,"
            "so the copied text is what the reader was looking at when they asked."
            ""
            "There is deliberately no clipboard *read*: a tree that could read the"
            "clipboard without a paste gesture is a keylogger-adjacent capability."
            "Paste is user-initiated by construction, and that is the boundary." ]
      "case-field:Action.WriteToClipboard.text",
      "The text to copy, literal or bound; a literal is the bare JSON string on the wire."
      "case:Action.Dispatch",
      lines
          [ "Carry a typed host message. **IN-PROCESS ONLY.**"
            ""
            "Phase 1152 — the declaration says this now, so it no longer depends on"
            "the reader having found `Action.dispatch`'s doc comment first. `msg` is"
            "a host value with no wire projection: `{\"$type\":\"Dispatch\"}` is the"
            "whole encoding, and the canonical decoder restores the payload as the"
            "`\"<closure>\"` sentinel. A browser fed canonical wire JSON therefore"
            "observes THAT the affordance fired and can never receive the message —"
            "documented behaviour, not a defect. Full Fable is the one tier where"
            "this survives, because there the tree is never serialised."
            ""
            "**The two wire-representable remedies.** `Action.Notify` carries a"
            "channel and a JSON payload; `Action.Call` with an `into:` target writes"
            "its result into state or a query (`onResult` is itself a closure and"
            "does not survive either). Typed dispatch is then recovered host-side by"
            "binding a handler table to the artifact's declared action holes, which"
            "is uniform across hosts and needs no per-language mechanism."
            ""
            "**Two shipped surfaces make this checkable rather than remembered:**"
            "`CanonicalJson.encodeNodeForTransport` REFUSES a tree carrying one, and"
            "`PreEmitValidate.validateForTransport` reports it as **FUARAN112**."
            "Neither fires on the plain in-process paths, because there is nothing"
            "wrong with them — which is also why the `Obsolete` attribute below is"
            "a warning (FS0044) and not an error." ]
      "case-field:Action.Dispatch.msg",
      "The host message to dispatch; in-process only, it is never encoded, so a decoded `Dispatch` cannot deliver it."
      "case:Action.Invoke", "Invokes a host-registered capability for its effect."
      "case-field:Action.Invoke.capabilityId", "The id of the capability, as the host's capability registry names it."
      "case-field:Action.Invoke.args",
      "The arguments, as address/value pairs the host checks against the capability's signature before dispatch."
      "case:Action.ReadFileBody",
      "Reads a selected file's whole body into a string and hands it to `onRead`; meant for small payloads, while large files belong to an upload destination."
      "case-field:Action.ReadFileBody.fileRef", "The id of the selected file to read."
      "case-field:Action.ReadFileBody.fileHandle",
      "The runtime file handle the host reads from; host-only, never encoded, and `None` after decode."
      "case-field:Action.ReadFileBody.encoding", "How the body is turned into a string: `Text`, `Base64` or `DataUrl`."
      "case-field:Action.ReadFileBody.onRead",
      "The handler that receives the body; a closure, so it does not survive encoding."
      "case:Action.Call", "Calls a host endpoint, optionally writing the result into state or a query."
      "case-field:Action.Call.endpoint",
      "The host endpoint to call; the host's dispatch gate decides whether it may be called."
      "case-field:Action.Call.onResult",
      "The handler that receives the result; a closure, so it does not survive encoding, and it takes precedence when present."
      "case-field:Action.Call.into",
      "Where the result lands, a state key or a query name. Omitted, with no `onResult`, makes this a fire-and-forget call."
      "case:Action.Navigate",
      lines
          [ "Navigate the reader to `route`, in the browsing context `target` names."
            ""
            "Phase 1536 — the route is a `TextSource`, so \"open the selected"
            "order\" (`/orders/{selection.id}`) has a spelling. The case was WIDENED"
            "rather than joined by a `NavigateBound` sibling, on exactly the"
            "reasoning Phase 1126 applied to `WriteToClipboard`: two cases for one"
            "intent is the permanent near-synonym pair the vocabulary charter exists"
            "to forbid."
            ""
            "**The wire does not move for a literal route.** `TextSource.Literal` is"
            "canonically the bare JSON string, so"
            "`{\"$type\":\"Navigate\",\"route\":\"/x\"}` is emitted and accepted exactly"
            "as it was before this release, and the §16 `href` / `url` / `to`"
            "aliases still normalise onto this one field. Construction sites are"
            "what break, and they break at compile time: wrap the old argument in"
            "`TextSource.Literal`."
            ""
            "**Resolve, then gate — in that order.** A bound route resolves at"
            "DISPATCH time through the surrounding tree's binding sources, and the"
            "egress check and the dispatch gate then judge the RESOLVED string. A"
            "template is not a destination: gating `/orders/{id}` would consult the"
            "policy about a URL nobody navigates to. A route that does not resolve"
            "warns and navigates nowhere."
            ""
            "`target = Blank` opens a fresh browsing context with"
            "`noopener,noreferrer` — the renderer's obligation on every host, never"
            "a host seam's." ]
      "case-field:Action.Navigate.route",
      "The route to open, literal or bound; a bound route is resolved when the action fires."
      "case-field:Action.Navigate.target", "Where the route opens. Omitted means `Self`, the current view."
      "case:Action.CommitLocal",
      "Flushes the edit buffer of the addressed input, for a `Binding.Local` whose `flushOn` is `OnCommitAction`."
      "case-field:Action.CommitLocal.nodeId", "The id of the input node whose buffer is committed."
      "case:Action.Notify", "Sends a JSON payload to the host on a named channel."
      "case-field:Action.Notify.channel", "The channel name the host listens on."
      "case-field:Action.Notify.payload", "The JSON data sent with the notification."
      "case:Action.SetState",
      "Write a value to the document's reactive state under `key`: either the literal `value` or the `valueFrom` binding evaluated at dispatch time, and exactly one of the two."
      "case-field:Action.SetState.key", "The state key to write."
      "case-field:Action.SetState.value", "A literal value to write; exactly one of `value` and `valueFrom` is present."
      "case-field:Action.SetState.valueFrom",
      "A binding resolved when the action fires, whose value is written; exactly one of `value` and `valueFrom` is present."
      "case:Action.AiTool", "Asks the host to run a named AI tool with JSON arguments."
      "case-field:Action.AiTool.toolName", "The name of the tool to run."
      "case-field:Action.AiTool.args", "The tool's arguments, as JSON."
      "case:Action.Print",
      lines
          [ "Phase 1124 — open the reader's own print dialogue. The first"
            "PAYLOAD-FREE `Action` case, and the emptiness is the ruling: the"
            "paged MEDIUM is Host chrome, so a document may say *print now* and"
            "nothing about how. No page size, no margin, no sheet range, no"
            "target subtree — `{\"$type\":\"Print\"}` is the whole encoding."
            ""
            "Not a hatch. `window.print()` opens a dialogue the reader operates"
            "and can cancel, hands the page to no third party, and returns"
            "nothing the tree can read — so it discloses less than the clipboard"
            "write beside it. It is gated all the same"
            "(`ActionDescriptor.Print`), because a host that renders untrusted"
            "trees must be able to refuse an unbidden dialogue." ]
      "case:Action.Confirm",
      lines
          [ "Phase 1537 — ask the reader `prompt`, then dispatch `onConfirm` on"
            "acceptance or `onCancel` (when present) on refusal."
            ""
            "\"Delete — are you sure?\" is composable today only as `Modal.Open` +"
            "`SetState` + a second button: a state key, a writer and a control the"
            "emitter must invent, for an intent every host already owns as a"
            "dialogue."
            ""
            "**One dispatch path, gated twice.** `Confirm` itself is gated"
            "(`ActionDescriptor.Confirm`) — a host rendering untrusted trees must"
            "be able to refuse an unbidden dialogue, exactly the `Print` reasoning."
            "On acceptance the continuation re-enters the ORDINARY dispatch entry,"
            "so a `Navigate` inside it meets its own egress check and a `SetState`"
            "its own host-reserved-key guard. Nothing here is a second path around"
            "a gate; a confirm cannot smuggle an action a host would refuse."
            ""
            "**Bounded at depth one.** A `Confirm` anywhere inside either"
            "continuation — under a `Chain` included — is refused at decode. A"
            "dialogue that answers a dialogue is a modal stack a reader cannot"
            "escape, and it expresses no intent a single question does not."
            ""
            "**What this does not claim.** A confirmation is a courtesy to the"
            "reader, never an authorisation: the answer comes from the client, and"
            "a hostile client answers yes without asking anyone. Anything that must"
            "not happen without permission is refused by the gate, not by the"
            "dialogue." ]
      "case-field:Action.Confirm.prompt", "The question shown to the reader, literal or bound."
      "case-field:Action.Confirm.onConfirm",
      "The action dispatched when the reader accepts; it may not itself contain a `Confirm`."
      "case-field:Action.Confirm.onCancel",
      "The action dispatched when the reader declines. Omitted means declining does nothing."
      "case:Action.Focus",
      lines
          [ "Phase 1537 — move keyboard focus to the node addressed by `nodeId`."
            ""
            "The server-driven tier has carried `ClientEffect.Focus of nodeId` since"
            "Phase 152 with no `Action` counterpart; this closes that asymmetry"
            "rather than opening a new capability."
            ""
            "Gated (`ActionDescriptor.Focus`) on the `Print` reasoning: moving the"
            "reader's caret — and, with it, the viewport — is host-observable even"
            "though no `IFuaranRuntime` member backs it. A node id that addresses"
            "nothing warns and moves nothing."
            ""
            "**What this does not claim.** Nothing about scrolling: a host may"
            "scroll as a consequence of focusing, and this case does not ask it to"
            "and cannot stop it. Nothing about selection: focus is not a caret"
            "position, a text range, or a grid cell selection. And nothing about"
            "WHICH node — \"focus the first invalid field\" is this case naming a"
            "node the author chose; choosing the first invalid one is a"
            "renderer-owned affordance, not an argument here." ]
      "case-field:Action.Focus.nodeId", "The id of the node in this document that receives keyboard focus."
      "case:Action.Choose",
      lines
          [ "Phase 2107 — run `whenTrue` when `condition` resolves to the boolean"
            "`true` at dispatch time, otherwise `whenFalse` (absent is the empty"
            "sequence). The bounded program core's SELECTION arm, spelled with its"
            "own name: an unresolved or errored condition halts the gesture before"
            "either arm runs, and `exit`, when present, must hold after the true"
            "arm and fail after the false one."
            ""
            "Control only: every effect an arm reaches is an existing case meeting"
            "its own gate. A host without the bounded loop refuses a gesture that"
            "carries this case, whole, rather than evaluating it a second way." ]
      "case-field:Action.Choose.condition",
      "The binding resolved at dispatch time; the boolean `true` selects `whenTrue`."
      "case-field:Action.Choose.whenTrue", "The action run when the condition is `true`."
      "case-field:Action.Choose.whenFalse",
      "The action run when the condition resolves to anything else. Omitted means nothing runs."
      "case-field:Action.Choose.exit",
      "An optional assertion checked after the arm: it must hold after `whenTrue` and fail after `whenFalse`."
      "case:Action.Require",
      lines
          [ "Phase 2107 — the halting guard. `condition` resolves at dispatch time;"
            "the boolean `true` holds and changes nothing, and anything else halts"
            "the gesture, so nothing after the guard runs. The bounded program"
            "core's `Require`, spelled with its own name." ]
      "case-field:Action.Require.condition", "The binding that must resolve to `true` for the gesture to continue."
      "union:Binding",
      "Where a slot's value comes from: a literal, host data, a filter, a selection, state, the current instant, a computation or a formatted value, each a `$type`-tagged case."
      "case:Binding.Static", "A literal value written into the document."
      "case-field:Binding.Static.value", "The value. Omitted means the binding carries no value."
      "case:Binding.Query", "Reads a host-computed query result by name."
      "case-field:Binding.Query.name", "The name of the query the host resolves."
      "case-field:Binding.Query.accessor",
      "Projects the host's raw result to the slot's type; host-only, never encoded, and the identity after decode."
      "case-field:Binding.Query.dependsOn",
      "The names of the filters that scope this query, which is re-resolved when one of them changes. Omitted means it depends on no filter."
      "case:Binding.Filter", "Reads the current value of a named filter."
      "case-field:Binding.Filter.name", "The filter's name."
      "case-field:Binding.Filter.defaultValue",
      "The value yielded, and seeded into the filter, before the filter is first written. Omitted means the binding is unresolved until then."
      "case:Binding.Selection", "Reads the current selection of the addressed node, such as a grid's selected row."
      "case-field:Binding.Selection.nodeId", "The id of the node whose selection is read."
      "case-field:Binding.Selection.accessor",
      "Projects the selection to the slot's type; host-only and never encoded, so a decoded binding projects through `field` instead."
      "case-field:Binding.Selection.defaultValue", "The value yielded before anything is selected."
      "case-field:Binding.Selection.field",
      "The row field to read from the selection. Omitted means the selection itself."
      "case:Binding.State", "Reads a value from the document's reactive state by key."
      "case-field:Binding.State.key", "The state key to read."
      "case-field:Binding.State.defaultValue", "The value yielded before the key is first written."
      "case:Binding.Now", "Reads the current instant, which the host supplies; the document never carries it."
      "case-field:Binding.Now.accessor",
      "Projects the host's instant to the slot's type; host-only, never encoded, and the identity after decode."
      "case-field:Binding.Now.grain",
      "The resolution the host truncates the instant to before it is read. Omitted means `Second`."
      "case:Binding.Computed",
      "Derives a value in process with a host function; it has no wire form, and a decoded `Computed` fails when read."
      "case-field:Binding.Computed.fn",
      "The function that computes the value from the binding context; it encodes only as a closure placeholder."
      "case:Binding.Local",
      "A controlled input's edit buffer, seeded from `initialFrom` and written back when `flushOn` fires."
      "case-field:Binding.Local.flushOn", "When the buffered value is written back."
      "case-field:Binding.Local.format",
      "Renders the value as the text shown in the input; a closure, so a decoded buffer uses `codec` or the identity rendition."
      "case-field:Binding.Local.initialFrom", "The binding the buffer is seeded and re-synced from."
      "case-field:Binding.Local.onCommit",
      "Runs when the buffer flushes, receiving the parsed value; a closure, and mutually exclusive with `commitTo`."
      "case-field:Binding.Local.parse",
      "Reads the typed text back as a value, or an error the input shows; a closure, so a decoded buffer uses `codec` or the identity parse."
      "case-field:Binding.Local.codec",
      "A locale-independent codec for the buffer text, replacing the identity in both directions; only `Format.Number` is admitted. Omitted means the identity codec."
      "case-field:Binding.Local.commitTo",
      "The state key the flushed value is written to; mutually exclusive with `onCommit`."
      "case:Binding.Format", "A number rendered as locale-aware display text."
      "case-field:Binding.Format.source", "The numeric binding to format."
      "case-field:Binding.Format.format",
      "How the number is rendered, such as a currency, a percentage, a date-time or a duration."
      "case-field:Binding.Format.locale", "The locale it is rendered in, the host's ambient locale or an explicit tag."
      "case:Binding.I18n", "Looks up a localised message by key."
      "case-field:Binding.I18n.key", "The message key in the host's catalogue."
      "case-field:Binding.I18n.args", "Named bindings that fill the message's placeholders. Omitted means no arguments."
      "case:Binding.Transform",
      "A table derived by running `pipeline` over `source`; a binding-shaped source (State, Selection or Query) stays live, so the result re-evaluates when that source changes."
      "case-field:Binding.Transform.source",
      "The data the pipeline runs over: an embedded table, a host-resolved named source, or a live binding."
      "case-field:Binding.Transform.pipeline",
      "The ordered transform steps, such as filter, derive, group and sort, evaluated over the source."
      "case-field:Binding.Transform.params",
      "Binds the pipeline's named params to binding sources; a filter step whose param is unset is skipped. Omitted means no params."
      "case:Binding.Expr", "Evaluates one scalar expression over its bound params, yielding a single value."
      "case-field:Binding.Expr.expr",
      "The expression, in the same algebra a transform pipeline uses; it may not reference a column."
      "case-field:Binding.Expr.params",
      "Binds the expression's named params to binding sources; a param the expression uses but this list does not bind is refused at decode. Omitted means no params."
      "case:Binding.Invoke",
      "Calls a host-registered capability for a value, which arrives asynchronously and renders through the node's loading and error surfaces."
      "case-field:Binding.Invoke.capabilityId", "The id of the capability, as the host's capability registry names it."
      "case-field:Binding.Invoke.args",
      "The arguments, as address/value pairs the host checks against the capability's signature before dispatch."
      "union:BoxLayout",
      "How a `Box` arranges its children: the responsive auto-tile, a flex row or column, a row-fill grid, or a column-fill masonry."
      "case:BoxLayout.Auto",
      "Responsive auto-tile: the renderer chooses the column count, and the author declares none."
      "case:BoxLayout.Flex", "A single flex line of children along `direction`, optionally wrapping."
      "case-field:BoxLayout.Flex.direction",
      "The main axis: `Vertical` stacks the children, `Horizontal` lays them in a row."
      "case-field:BoxLayout.Flex.wrap",
      "Whether children wrap onto further lines when they overflow the main axis. Defaults to `false`, which is omitted on the wire."
      "case-field:BoxLayout.Flex.gap",
      "Spacing between children, in pixels. Omitted means the renderer's default spacing."
      "case:BoxLayout.Grid",
      "A grid that fills by row, with `cols` columns or an explicit `templateColumns` track list."
      "case-field:BoxLayout.Grid.cols", "The number of columns. A present `templateColumns` supersedes it."
      "case-field:BoxLayout.Grid.templateColumns",
      "A verbatim CSS `grid-template-columns` sizing function that supersedes `cols`. Omitted means `cols` equal columns."
      "case-field:BoxLayout.Grid.gap",
      "Spacing between grid cells, in pixels. Omitted means the renderer's default spacing."
      "case:BoxLayout.Masonry",
      "A column-fill layout (§3.6.7): children flow down `cols` columns in turn, realised with CSS multi-column layout."
      "case-field:BoxLayout.Masonry.cols",
      "The number of columns. Must be a positive integer; zero or a negative value is refused by the decoder."
      "case-field:BoxLayout.Masonry.gap",
      "Spacing between columns, in pixels. Omitted means the renderer's default spacing."
      "union:CallResultTarget", "Where an `Action.Call` writes its result."
      "case:CallResultTarget.State", "Writes the result to a state key, re-rendering its `Binding.State` readers."
      "case-field:CallResultTarget.State.key", "The state key written."
      "case:CallResultTarget.Query",
      "Writes the result as a named query result, re-rendering its `Binding.Query` readers."
      "case-field:CallResultTarget.Query.name", "The query name written."
      "union:CellFormat",
      "How a numeric or date value is displayed in a grid column, a `Metric` or a comparison, while the underlying value keeps its type for sorting and trends."
      "case:CellFormat.None", "No formatting: the value is shown as it is."
      "case:CellFormat.Number", "A plain number, with optional fraction digits and an optional locale."
      "case-field:CellFormat.Number.decimals",
      "The number of fraction digits to show. Omitted means the formatter's default precision."
      "case-field:CellFormat.Number.locale",
      "The locale the figure renders in, through the same formatter `Binding.Format` uses. Omitted means the locale-free rendering, identical on every host."
      "case:CellFormat.Currency", "A money figure in the currency named by `code`."
      "case-field:CellFormat.Currency.code", "The ISO 4217 currency code, such as `GBP` or `USD`."
      "case-field:CellFormat.Currency.decimals",
      "The number of fraction digits to show. Omitted means two places without a locale, or the currency's own digits with one."
      "case-field:CellFormat.Currency.locale",
      "The locale the figure renders in, giving its grouping and symbol. Omitted means the locale-free rendering, the code followed by the amount."
      "case:CellFormat.Percent", "A percentage, with optional fraction digits and an optional locale."
      "case-field:CellFormat.Percent.decimals",
      "The number of fraction digits to show. Omitted means the formatter's default precision."
      "case-field:CellFormat.Percent.locale",
      "The locale the figure renders in, through the same formatter `Binding.Format` uses. Omitted means the locale-free rendering, identical on every host."
      "case:CellFormat.SignificantDigits",
      "A number rounded to a fixed count of significant digits, so its fraction digits move with its magnitude."
      "case-field:CellFormat.SignificantDigits.digits", "The number of significant digits to keep."
      "case:CellFormat.DateTime", "A date, a time, or both, rendered through a host-formatter pattern."
      "case-field:CellFormat.DateTime.format",
      "The format pattern, such as `yyyy-MM-dd`, `HH:mm` or `dd MMM yyyy HH:mm`."
      "case:CellFormat.Duration",
      lines
          [ "Phase 819 — trendable duration cells: the raw float counts `unit`s,"
            "rendered per `style`." ]
      "case-field:CellFormat.Duration.unit", "The unit the raw number counts: seconds, minutes or hours."
      "case-field:CellFormat.Duration.style",
      "How the duration is written: `Compact` (1h 20m), `Clock` (1:20:00) or `Long` (1 hour 20 minutes)."
      "case:CellFormat.RelativeTime",
      lines
          [ "Phase 819 — cell-vocabulary parity with `Format.RelativeTime`: the"
            "raw float is a signed count of `unit`." ]
      "case-field:CellFormat.RelativeTime.unit", "The unit the raw number counts, as a signed offset from now."
      "case:CellFormat.Custom", "A host closure that formats the cell value itself. It does not survive the wire."
      "case-field:CellFormat.Custom.fn", "The function that turns a `CellValue` into its display text."
      "union:CellKindErased",
      "What a `DataGrid` cell is and does, with any row accessors taking the row as a name-addressable map."
      "case:CellKindErased.Text", "A plain, non-interactive text cell."
      "case:CellKindErased.Numeric",
      "A non-interactive numeric cell, which keeps numeric sorting; pair it with a `CellFormat` to style the figure."
      "case:CellKindErased.Date", "A non-interactive date cell."
      "case:CellKindErased.Editable", "An editable cell whose edits are reported through `onEdit`."
      "case-field:CellKindErased.Editable.onEdit",
      "Called with the row and the new `CellValue` when the reader edits the cell. Omitted means no edit handler."
      "case:CellKindErased.Checkbox", "A checkbox cell whose state is read from the row."
      "case-field:CellKindErased.Checkbox.get", "Reads whether the checkbox is ticked for the row."
      "case-field:CellKindErased.Checkbox.onToggle",
      "Called with the row and the new state when the reader toggles the checkbox. Omitted means no handler."
      "case:CellKindErased.Button", "A button in the cell."
      "case-field:CellKindErased.Button.label", "The button's text."
      "case-field:CellKindErased.Button.onClick",
      "Called with the row when the button is clicked. Omitted means no handler."
      "case:CellKindErased.ButtonGroup", "Several buttons side by side in the cell."
      "case-field:CellKindErased.ButtonGroup.buttons", "The buttons, in display order, each a `ButtonGroupItem`."
      "case:CellKindErased.Link", "A hyperlink whose target and text are computed from the row."
      "case-field:CellKindErased.Link.hrefFn", "Computes the link target from the row."
      "case-field:CellKindErased.Link.labelFn", "Computes the link text from the row."
      "case:CellKindErased.Pill",
      "A toned pill whose label and tone are computed by host closures; use `TonedPill` for a tone rule that survives the wire."
      "case-field:CellKindErased.Pill.labelFn", "Computes the pill's label from the row."
      "case-field:CellKindErased.Pill.toneFn", "Computes the pill's `ToneVariant` from the row."
      "case:CellKindErased.TonedPill",
      "A toned pill declared as data: the row's `field` value is the label, and `map` picks its tone."
      "case-field:CellKindErased.TonedPill.field",
      "The row property that supplies both the pill's label and its key into `map`."
      "case-field:CellKindErased.TonedPill.map", "Maps a field value to the `ToneVariant` the pill takes."
      "case-field:CellKindErased.TonedPill.default",
      "The tone for a value `map` does not mention. Defaults to `Default`, which is omitted on the wire."
      "case:CellKindErased.Progress", "A progress bar computed from the row, with an optional label."
      "case-field:CellKindErased.Progress.fractionFn", "Computes the completed fraction, from 0 to 1, from the row."
      "case-field:CellKindErased.Progress.labelFn",
      "Computes the bar's label from the row. Omitted means an unlabelled bar."
      "case:CellKindErased.Custom",
      "A bespoke cell renderer supplied by the host; the last-resort escape when no other cell kind fits."
      "case-field:CellKindErased.Custom.fn",
      "Builds the cell's node, given a projector that reads the cell value from the row."
      "union:ChartAnnotation",
      "A data-addressed chart annotation in `ChartSpec.annotations`: it carries an address in the data's own coordinates and an optional label, never geometry or style, so it survives data changes, restyles and resizes."
      "case:ChartAnnotation.ReferenceLine", "A horizontal line across the plot at a value on the value axis."
      "case-field:ChartAnnotation.ReferenceLine.value",
      "The line's position in the value axis's own units. Must be finite; a non-finite value is refused by the decoder."
      "case-field:ChartAnnotation.ReferenceLine.label", "The line's label text. Omitted means an unlabelled line."
      "case:ChartAnnotation.EventMarker",
      "A vertical line at an x-axis address (a category or a date), with an optional label."
      "case-field:ChartAnnotation.EventMarker.at",
      "Where on the x axis the marker stands, as a `ChartAnnotationX` address."
      "case-field:ChartAnnotation.EventMarker.label", "The marker's label text. Omitted means an unlabelled marker."
      "case:ChartAnnotation.RangeBand", "A shaded interval on either axis, drawn behind the series."
      "case-field:ChartAnnotation.RangeBand.range",
      "The band's extent; the `ChartAnnotationRange` case chooses the axis and types the pair of ends."
      "case-field:ChartAnnotation.RangeBand.label", "The band's label text. Omitted means an unlabelled band."
      "union:ChartAnnotationRange",
      "The pair of ends of a `RangeBand`; the case names the axis the band lies on, so both ends share that axis's address form."
      "case:ChartAnnotationRange.ValueRange", "A band between two values on the value axis."
      "case-field:ChartAnnotationRange.ValueRange.from",
      "The band's lower end in the value axis's own units. Must be finite and not greater than `to`."
      "case-field:ChartAnnotationRange.ValueRange.to",
      "The band's upper end in the value axis's own units. Must be finite and not less than `from`."
      "case:ChartAnnotationRange.XRange", "A band between two x-axis addresses, both categories or both dates."
      "case-field:ChartAnnotationRange.XRange.from",
      "The band's starting x address. A date pair must be in order; a category pair's order is checked against the rows at authoring time."
      "case-field:ChartAnnotationRange.XRange.to", "The band's ending x address, in the same address form as `from`."
      "union:ChartAnnotationX",
      "An x-axis address for a chart annotation: a category key on a band axis or a date on a temporal axis. The form is declared, never inferred, and a form that does not match the chart's x scale is refused."
      "case:ChartAnnotationX.Category", "Addresses a category band on a band x axis."
      "case-field:ChartAnnotationX.Category.key", "The category key naming the band."
      "case:ChartAnnotationX.Date", "Addresses an instant on a temporal x axis (`XScale = Temporal`)."
      "case-field:ChartAnnotationX.Date.iso",
      "The instant as an ISO-8601 date string. An unparseable date is refused by the decoder."
      "union:ColumnWidth", "How a `DataGrid` column is sized."
      "case:ColumnWidth.Auto", "The renderer sizes the column to its content."
      "case:ColumnWidth.Fixed", "A fixed width in pixels."
      "case-field:ColumnWidth.Fixed.pixels", "The column width, in pixels."
      "case:ColumnWidth.Flex", "A share of the remaining width, in proportion to `weight`."
      "case-field:ColumnWidth.Flex.weight", "The column's relative share of the remaining width."
      "union:CurveCommand", "One command of a `Curve` shape's path, in the drawing's coordinate space."
      "case:CurveCommand.MoveTo", "Starts a new subpath at a point without drawing."
      "case-field:CurveCommand.MoveTo.to", "The point the pen moves to."
      "case:CurveCommand.LineTo", "Draws a straight line from the current point."
      "case-field:CurveCommand.LineTo.to", "The line's end point."
      "case:CurveCommand.CubicTo", "Draws a cubic Bezier curve from the current point."
      "case-field:CurveCommand.CubicTo.control1", "The first control point."
      "case-field:CurveCommand.CubicTo.control2", "The second control point."
      "case-field:CurveCommand.CubicTo.to", "The curve's end point."
      "case:CurveCommand.QuadraticTo", "Draws a quadratic Bezier curve from the current point."
      "case-field:CurveCommand.QuadraticTo.control", "The control point."
      "case-field:CurveCommand.QuadraticTo.to", "The curve's end point."
      "case:CurveCommand.Close", "Closes the current subpath with a straight line back to its start."
      "union:FormFieldKind",
      "The input control a `Form` field or a `Filters` chip uses. On every case an omitted `value` auto-binds the field's own state or filter, and an omitted `onChange` writes changes back to a writable value binding."
      "case:FormFieldKind.Text", "A single-line text input."
      "case-field:FormFieldKind.Text.value",
      "The text shown in the input, as a `Binding` to a string. Omitted means the field auto-binds its own state or filter slot."
      "case-field:FormFieldKind.Text.onChange",
      "Called with the new text. Omitted means the change is written back to the `value` binding when it is writable."
      "case:FormFieldKind.Number", "A numeric input."
      "case-field:FormFieldKind.Number.value",
      "The number shown in the input, as a `Binding` to a number. Omitted means the field auto-binds its own state or filter slot."
      "case-field:FormFieldKind.Number.onChange",
      "Called with the new number. Omitted means the change is written back to the `value` binding when it is writable."
      "case:FormFieldKind.Checkbox", "A boolean checkbox."
      "case-field:FormFieldKind.Checkbox.value",
      "Whether the box is ticked, as a `Binding` to a bool. Omitted means the field auto-binds its own state or filter slot."
      "case-field:FormFieldKind.Checkbox.onChange",
      "Called with the new ticked state. Omitted means the change is written back to the `value` binding when it is writable."
      "case:FormFieldKind.Toggle", "A boolean on/off switch: the same value as `Checkbox`, shown as a toggle."
      "case-field:FormFieldKind.Toggle.value",
      "Whether the switch is on, as a `Binding` to a bool. Omitted means the field auto-binds its own state or filter slot."
      "case-field:FormFieldKind.Toggle.onChange",
      "Called with the new on/off state. Omitted means the change is written back to the `value` binding when it is writable."
      "case:FormFieldKind.Choice", "A single choice from a bounded list of options."
      "case-field:FormFieldKind.Choice.options", "The options to choose from, each a `SelectOption`."
      "case-field:FormFieldKind.Choice.value",
      "The chosen option's value, as a `Binding` to a string. Omitted means the field auto-binds its own state or filter slot."
      "case-field:FormFieldKind.Choice.onChange",
      "Called with the chosen value, or null when the choice is cleared. Omitted means the change is written back to the `value` binding when it is writable."
      "case:FormFieldKind.TextArea", "A multi-line text input."
      "case-field:FormFieldKind.TextArea.value",
      "The text shown in the input, as a `Binding` to a string. Omitted means the field auto-binds its own state or filter slot."
      "case-field:FormFieldKind.TextArea.onChange",
      "Called with the new text. Omitted means the change is written back to the `value` binding when it is writable."
      "case-field:FormFieldKind.TextArea.rows", "The number of visible text rows."
      "case:FormFieldKind.RangedNumber", "A numeric quantity input with optional bounds and step."
      "case-field:FormFieldKind.RangedNumber.value",
      "The current number, as a `Binding` to a number. Omitted means the field auto-binds its own state or filter slot."
      "case-field:FormFieldKind.RangedNumber.onChange",
      "Called with the new number. Omitted means the change is written back to the `value` binding when it is writable."
      "case-field:FormFieldKind.RangedNumber.min", "The lowest allowed value. Omitted means no lower bound."
      "case-field:FormFieldKind.RangedNumber.max", "The highest allowed value. Omitted means no upper bound."
      "case-field:FormFieldKind.RangedNumber.step",
      "The increment between allowed values. Omitted means the control's default step."
      "case:FormFieldKind.Range", "A dual-thumb numeric range selecting a low and a high value."
      "case-field:FormFieldKind.Range.value",
      "The selected pair, as a `Binding` to a `RangePair`; a static pair is written as the bare `{\"max\":…,\"min\":…}` object. Omitted means the field auto-binds its own state or filter slot."
      "case-field:FormFieldKind.Range.onChange",
      "Called with the new pair. Omitted means the change is written back to the `value` binding when it is writable."
      "case-field:FormFieldKind.Range.min", "The lowest value either thumb can take. Omitted means no lower bound."
      "case-field:FormFieldKind.Range.max", "The highest value either thumb can take. Omitted means no upper bound."
      "case-field:FormFieldKind.Range.step",
      "The increment between allowed values. Omitted means the control's default step."
      "case:FormFieldKind.SegmentedChoice", "A single choice shown as a row or column of segmented buttons."
      "case-field:FormFieldKind.SegmentedChoice.options", "The options to choose from, each a `SelectOption`."
      "case-field:FormFieldKind.SegmentedChoice.value",
      "The chosen option's value, as a `Binding` to a string. Omitted means the field auto-binds its own state or filter slot."
      "case-field:FormFieldKind.SegmentedChoice.onChange",
      "Called with the chosen value, or null when the choice is cleared. Omitted means the change is written back to the `value` binding when it is writable."
      "case-field:FormFieldKind.SegmentedChoice.orientation",
      "Whether the segments run in a row or a column. Defaults to `Horizontal`, which is omitted on the wire."
      "case:FormFieldKind.DateTime", "A date, time-of-day or date-time input, chosen by `variant`."
      "case-field:FormFieldKind.DateTime.value",
      "The value as an ISO-8601 string in the `variant`'s shape, as a `Binding`. Omitted means the field auto-binds its own state or filter slot."
      "case-field:FormFieldKind.DateTime.onChange",
      "Called with the new ISO-8601 string, or null when cleared. Omitted means the change is written back to the `value` binding when it is writable."
      "case-field:FormFieldKind.DateTime.variant", "Whether the field takes a `Date`, a `Time` or a `DateTime`."
      "case-field:FormFieldKind.DateTime.min",
      "The earliest allowed value, as an ISO-8601 string. Omitted means no lower bound."
      "case-field:FormFieldKind.DateTime.max",
      "The latest allowed value, as an ISO-8601 string. Omitted means no upper bound."
      "case-field:FormFieldKind.DateTime.step",
      "The increment between allowed values, in seconds. Omitted means the control's default step."
      "case:FormFieldKind.DateTimeRange",
      "A single control selecting an ordered from/to pair of dates, times or date-times."
      "case-field:FormFieldKind.DateTimeRange.value",
      "The selected pair, as a `Binding` to a `DateTimeRangePair`; a static pair is written as the bare `{\"from\":…,\"to\":…}` object and must have `from` no later than `to`. Omitted means the field auto-binds its own state or filter slot."
      "case-field:FormFieldKind.DateTimeRange.onChange",
      "Called with the new from/to pair. Omitted means the change is written back to the `value` binding when it is writable."
      "case-field:FormFieldKind.DateTimeRange.variant", "Whether both ends are a `Date`, a `Time` or a `DateTime`."
      "case-field:FormFieldKind.DateTimeRange.min",
      "The earliest value either end can take, as an ISO-8601 string. Omitted means no lower bound."
      "case-field:FormFieldKind.DateTimeRange.max",
      "The latest value either end can take, as an ISO-8601 string. Omitted means no upper bound."
      "case-field:FormFieldKind.DateTimeRange.step",
      "The increment between allowed values, in seconds. Omitted means the control's default step."
      "case:FormFieldKind.Combobox", "A searchable single choice that filters its options as the reader types."
      "case-field:FormFieldKind.Combobox.allowFreeText",
      "Whether the reader may enter a value outside the options. Defaults to `false`, which is omitted on the wire."
      "case-field:FormFieldKind.Combobox.onChange",
      "Called with the chosen or entered value, or null when cleared. Omitted means the change is written back to the `value` binding when it is writable."
      "case-field:FormFieldKind.Combobox.options",
      "The options to search, each a `SelectOption`; a `Query` binding supplies them asynchronously."
      "case-field:FormFieldKind.Combobox.value",
      "The current value, as a `Binding` to a string. Omitted means the field auto-binds its own state or filter slot."
      "case:FormFieldKind.Rating", "A score on a small ordinal scale, such as one to five stars."
      "case-field:FormFieldKind.Rating.allowHalf",
      "Whether the reader may enter half steps. Defaults to `false` (whole steps only), which is omitted on the wire."
      "case-field:FormFieldKind.Rating.max",
      "The top of the scale. Must be at least 1; a smaller value is refused by the decoder."
      "case-field:FormFieldKind.Rating.onChange",
      "Called with the new score. Omitted means the change is written back to the `value` binding when it is writable."
      "case-field:FormFieldKind.Rating.value",
      "The current score, as a `Binding` to a number, which may be fractional for display such as an average. Omitted means the field auto-binds its own state or filter slot."
      "case:FormFieldKind.Color", "A colour swatch that opens the platform's colour picker."
      "case-field:FormFieldKind.Color.onChange",
      "Called with the new colour as a `#rrggbb` string. Omitted means the change is written back to the `value` binding when it is writable."
      "case-field:FormFieldKind.Color.value",
      "The colour as a `#rrggbb` string, as a `Binding`; a static value of any other shape is refused by the decoder. Omitted means the field auto-binds its own state or filter slot."
      "case:FormFieldKind.Tokens", "A multi-value input that collects several tokens, each shown as a removable chip."
      "case-field:FormFieldKind.Tokens.allowFreeText",
      "Whether the reader may add tokens not among the suggestions. Defaults to `true`, which is omitted on the wire; `false` with no `suggestions` is refused by the decoder."
      "case-field:FormFieldKind.Tokens.onChange",
      "Called with the new token list. Omitted means the change is written back to the `value` binding when it is writable."
      "case-field:FormFieldKind.Tokens.suggestions",
      "Suggested tokens, each a `SelectOption`; a `Query` binding supplies them asynchronously. Omitted means a plain token box with nothing to suggest."
      "case-field:FormFieldKind.Tokens.value",
      "The tokens in the order they were added, as a `Binding` to a string list. Omitted means the field auto-binds its own state or filter slot."
      "union:Format",
      "A locale-aware rendering of a number, used by `Binding.Format` and, for `Number` only, as a `Binding.Local` codec."
      "case:Format.Number", "A plain number."
      "case-field:Format.Number.decimals",
      "The fixed number of fraction digits. Omitted leaves the digit count to the formatter."
      "case:Format.Currency", "A currency amount."
      "case-field:Format.Currency.isoCode", "The ISO 4217 currency code, such as `GBP`."
      "case-field:Format.Currency.decimals",
      "The number of fraction digits, overriding the currency's default. Omitted keeps the default."
      "case:Format.Percent", "A fraction rendered as a percentage."
      "case-field:Format.Percent.decimals",
      "The fixed number of fraction digits. Omitted leaves the digit count to the formatter."
      "case:Format.DateTime", "A date, a time of day, or both; set at least one of `dateStyle` and `timeStyle`."
      "case-field:Format.DateTime.dateStyle", "How the date part is rendered. Omitted renders no date."
      "case-field:Format.DateTime.timeStyle", "How the time-of-day part is rendered. Omitted renders no time."
      "case:Format.RelativeTime",
      "A signed count of a unit rendered as relative time, such as in 3 days or 2 hours ago."
      "case-field:Format.RelativeTime.unit", "The unit the source counts."
      "case:Format.Duration",
      lines
          [ "Phase 819 — locale-independent duration formatting: the numeric"
            "source counts `unit`s, rendered per `style`." ]
      "case-field:Format.Duration.unit", "The unit the source counts."
      "case-field:Format.Duration.style", "How the duration is rendered: `Compact`, `Clock` or `Long`."
      "case:Format.Since",
      "The time elapsed from an instant in Unix-epoch seconds to the host's current instant, rendered as relative time."
      "case-field:Format.Since.unit",
      "The unit to render in. Omitted means the unit is chosen automatically from the elapsed time."
      "union:FragmentArg",
      "An argument bound to a hole at a `FragmentRef`: a scalar for a value hole, or a subtree (`SlotArg`) for a slot hole."
      "case:FragmentArg.Int", "An integer value argument."
      "case-field:FragmentArg.Int.value", "The integer bound to the hole."
      "case:FragmentArg.Float", "A floating-point value argument."
      "case-field:FragmentArg.Float.value", "The number bound to the hole."
      "case:FragmentArg.Bool", "A boolean value argument."
      "case-field:FragmentArg.Bool.value", "The boolean bound to the hole."
      "case:FragmentArg.Str", "A string value argument."
      "case-field:FragmentArg.Str.value", "The string bound to the hole."
      "case:FragmentArg.SlotArg", "A subtree argument for a `Slot` or `Repeat` hole."
      "case-field:FragmentArg.SlotArg.tree", "The node bound into the slot."
      "union:HoleDecl", "A typed hole declared on a parameterised `FragmentDecl`, bound by name at each `FragmentRef`."
      "case:HoleDecl.Value", "A hole that takes a typed scalar value, validated against its value-space at bind time."
      "case-field:HoleDecl.Value.name", "The hole's name, which a `FragmentRef` uses as the key in `args`."
      "case-field:HoleDecl.Value.space", "The value-space an argument must fall within."
      "case-field:HoleDecl.Value.default",
      "The value used when no argument is bound. Omitted means the hole is required."
      "case:HoleDecl.Slot", "A hole that takes a subtree. Always required."
      "case-field:HoleDecl.Slot.name", "The hole's name, which a `FragmentRef` uses as the key in `args`."
      "case-field:HoleDecl.Slot.kindConstraint",
      "The node kind name the bound subtree must have (matched against the kind-constraint vocabulary, e.g. `Heading`). Omitted means any kind is accepted."
      "case:HoleDecl.Repeat", "A hole that takes a subtree expanded a bounded number of times. Always required."
      "case-field:HoleDecl.Repeat.name", "The hole's name, which a `FragmentRef` uses as the key in `args`."
      "case-field:HoleDecl.Repeat.countSpace",
      "The value-space of the repeat count. Must be a bounded `IntRange`; any other space is refused."
      "union:HoleValueSpace",
      "The domain a value argument must fall within, checked when the argument is bound to a hole."
      "case:HoleValueSpace.IntRange", "An integer between `min` and `max`, inclusive."
      "case-field:HoleValueSpace.IntRange.min", "The smallest accepted integer (inclusive)."
      "case-field:HoleValueSpace.IntRange.max", "The largest accepted integer (inclusive)."
      "case:HoleValueSpace.FloatRange", "A number between `min` and `max`, inclusive."
      "case-field:HoleValueSpace.FloatRange.min", "The smallest accepted number (inclusive)."
      "case-field:HoleValueSpace.FloatRange.max", "The largest accepted number (inclusive)."
      "case:HoleValueSpace.StringLen", "A string whose length is between `minLen` and `maxLen`, inclusive."
      "case-field:HoleValueSpace.StringLen.minLen", "The shortest accepted length (inclusive)."
      "case-field:HoleValueSpace.StringLen.maxLen", "The longest accepted length (inclusive)."
      "case:HoleValueSpace.Enum", "A string drawn from a fixed set of choices."
      "case-field:HoleValueSpace.Enum.choices", "The accepted strings."
      "case:HoleValueSpace.AnyString", "Any string, unconstrained."
      "union:LocalFlushTrigger", "When a `Binding.Local` edit buffer writes its value back."
      "case:LocalFlushTrigger.OnBlur", "When the input loses focus."
      "case:LocalFlushTrigger.OnSubmit", "When the enclosing form is submitted."
      "case:LocalFlushTrigger.OnDebounce", "After the reader pauses typing for a set delay."
      "case-field:LocalFlushTrigger.OnDebounce.milliseconds", "The pause, in milliseconds, before the buffer flushes."
      "case:LocalFlushTrigger.OnCommitAction", "When an `Action.CommitLocal` names the input."
      "union:LocaleSource", "The locale a `Binding.Format` renders in."
      "case:LocaleSource.Ambient", "The host's ambient locale."
      "case:LocaleSource.Explicit", "A fixed locale named by a tag."
      "case-field:LocaleSource.Explicit.tag", "The BCP 47 language tag, such as `en-GB`."
      "union:MediaKind", "Which media surface a `Media` node is: a video or an audio player."
      "case:MediaKind.Video", "A video player, with an optional poster frame and autoplay."
      "case-field:MediaKind.Video.autoplay",
      "Whether the video starts playing by itself. Defaults to `false`, which is omitted on the wire."
      "case-field:MediaKind.Video.poster",
      "The URL of the image shown before playback starts. Omitted means no poster frame."
      "case:MediaKind.Audio", "An audio player; it carries no extra members and never autoplays."
      "union:Scalar", "A self-describing scalar value, used as a `Value` hole's default."
      "case:Scalar.Int", "An integer scalar."
      "case-field:Scalar.Int.value", "The integer value."
      "case:Scalar.Float", "A floating-point scalar."
      "case-field:Scalar.Float.value", "The numeric value."
      "case:Scalar.Bool", "A boolean scalar."
      "case-field:Scalar.Bool.value", "The boolean value."
      "case:Scalar.Str", "A string scalar."
      "case-field:Scalar.Str.value", "The string value."
      "union:Shape",
      "A shape in a `Drawing`, positioned in the drawing's coordinate space; every case carries a `DrawStyle`."
      "case:Shape.Group", "A group of shapes sharing one style."
      "case-field:Shape.Group.children", "The shapes in the group, drawn in order."
      "case-field:Shape.Group.style", "The style applied to the group."
      "case:Shape.Rectangle", "An axis-aligned rectangle, optionally with rounded corners."
      "case-field:Shape.Rectangle.x", "The x coordinate of the top-left corner."
      "case-field:Shape.Rectangle.y", "The y coordinate of the top-left corner."
      "case-field:Shape.Rectangle.width", "The rectangle's width."
      "case-field:Shape.Rectangle.height", "The rectangle's height."
      "case-field:Shape.Rectangle.cornerRadius", "The radius of the rounded corners. Omitted means square corners."
      "case-field:Shape.Rectangle.style", "The rectangle's fill, stroke and other style."
      "case:Shape.Line", "A straight line segment between two points."
      "case-field:Shape.Line.x1", "The x coordinate of the start point."
      "case-field:Shape.Line.y1", "The y coordinate of the start point."
      "case-field:Shape.Line.x2", "The x coordinate of the end point."
      "case-field:Shape.Line.y2", "The y coordinate of the end point."
      "case-field:Shape.Line.style", "The line's stroke and other style."
      "case:Shape.Polyline", "An open path of straight segments through a list of points."
      "case-field:Shape.Polyline.points", "The points the polyline passes through, in order."
      "case-field:Shape.Polyline.style", "The polyline's stroke and other style."
      "case:Shape.Polygon", "A closed shape of straight edges through a list of points."
      "case-field:Shape.Polygon.points", "The polygon's vertices, in order."
      "case-field:Shape.Polygon.style", "The polygon's fill, stroke and other style."
      "case:Shape.Curve", "A path built from a sequence of `CurveCommand`s."
      "case-field:Shape.Curve.commands", "The path commands, applied in order."
      "case-field:Shape.Curve.style", "The path's fill, stroke and other style."
      "case:Shape.Circle", "A circle given by its centre and radius."
      "case-field:Shape.Circle.cx", "The x coordinate of the centre."
      "case-field:Shape.Circle.cy", "The y coordinate of the centre."
      "case-field:Shape.Circle.r", "The radius."
      "case-field:Shape.Circle.style", "The circle's fill, stroke and other style."
      "case:Shape.Ellipse", "An axis-aligned ellipse given by its centre and two radii."
      "case-field:Shape.Ellipse.cx", "The x coordinate of the centre."
      "case-field:Shape.Ellipse.cy", "The y coordinate of the centre."
      "case-field:Shape.Ellipse.rx", "The horizontal radius."
      "case-field:Shape.Ellipse.ry", "The vertical radius."
      "case-field:Shape.Ellipse.style", "The ellipse's fill, stroke and other style."
      "case:Shape.Label", "A text label placed at a point."
      "case-field:Shape.Label.x",
      "The x coordinate of the label's anchor point; `DrawStyle.textAnchor` sets how the text aligns to it."
      "case-field:Shape.Label.y", "The y coordinate of the label's anchor point."
      "case-field:Shape.Label.text", "The label's text."
      "case-field:Shape.Label.style", "The label's font, fill and other style."
      "union:TextSource", "Text for a slot: a literal string, a bound value, or a localised message."
      "case:TextSource.Literal", "Literal text; it encodes as the bare JSON string."
      "case-field:TextSource.Literal.text", "The text."
      "case:TextSource.Bound", "Text read from a string binding."
      "case-field:TextSource.Bound.binding", "The string binding the text comes from."
      "case:TextSource.I18n", "A localised message looked up by key."
      "case-field:TextSource.I18n.key", "The message key in the host's catalogue."
      "case-field:TextSource.I18n.args",
      "Named arguments that fill the message's placeholders, each a literal value or a binding."
      // Enums and their cases
      "enum:BadgeVariant", "The visual tone of a `Badge`, from neutral through brand to the status colours."
      "enum-case:BadgeVariant.Neutral", "A plain, untinted badge; the default."
      "enum-case:BadgeVariant.Brand", "Tinted with the primary brand accent."
      "enum-case:BadgeVariant.Success", "Tinted as a success state."
      "enum-case:BadgeVariant.Warning", "Tinted as a warning state."
      "enum-case:BadgeVariant.Critical", "Tinted as an error or critical state."
      "enum-case:BadgeVariant.Info", "Tinted as an informational accent."
      "enum:BoxRole", "What a `Box` container is for: a responsive dashboard, a card, a plain group, or a separator."
      "enum-case:BoxRole.Dashboard", "A dashboard surface whose children tile like cards."
      "enum-case:BoxRole.Card", "A card: a bounded surface, usually with a `heading`."
      "enum-case:BoxRole.Group", "A plain grouping container with no surface of its own."
      "enum-case:BoxRole.Separator",
      "A divider; a childless `Box` with this role renders as a separator line, its `heading` as the label."
      "enum:ButtonVariant", "The visual weight of a `Button`, from the primary call to action to a destructive one."
      "enum-case:ButtonVariant.Primary", "The main call to action."
      "enum-case:ButtonVariant.Secondary", "A standard, lower-emphasis action; the default."
      "enum-case:ButtonVariant.Tertiary", "The lightest-weight action."
      "enum-case:ButtonVariant.Destructive", "An action that deletes or destroys something."
      "enum:CaptureSource",
      "Which of the reader's recording devices a `FileUpload` asks the platform to open instead of the file picker; a request the user agent may ignore."
      "enum-case:CaptureSource.Camera", "Ask for the environment-facing camera."
      "enum-case:CaptureSource.Microphone", "Ask for the microphone or recorder."
      "enum:ChannelDirection", "Which way messages may flow on a `Mount`'s guest channel."
      "enum-case:ChannelDirection.OutOnly",
      "The guest may send messages to the host, but the host cannot push into the guest."
      "enum-case:ChannelDirection.TwoWay",
      "The host may also push messages into the guest; a host may clamp a rendered mount back to `OutOnly` unless it grants two-way itself."
      "enum:ChartDataLabels",
      lines
          [ "Whether a chart writes its values directly onto the picture, and where"
            "(Phase 881)."
            ""
            "A WIRE vocabulary, on the D8 line's semantic side: whether the reader is"
            "meant to READ THE NUMBERS or read the shape is the author's meaning; the"
            "type size, the offsets and the fit rule that realise it stay the host's, in"
            "`ChartStyle`."
            ""
            "THE CASE SET IS TWO, AND THAT IS THE POINT. There is deliberately no"
            "\"all points\" case: a number on every interior point is the clutter this"
            "vocabulary exists to avoid, so no shape of this API can request one. `Ends`"
            "names the selective placements that read — a bar's cap, a line's last point"
            "— and the set is closed there. Adding an all-points case later would not be"
            "an extension; it would retract the guarantee." ]
      "enum-case:ChartDataLabels.Off", "No data labels (the shipped default, and what an absent field means)."
      "enum-case:ChartDataLabels.Ends",
      lines
          [ "Label the ENDS only: every bar's cap (a stacked bar's TOTAL at the stack"
            "cap, never its interior segments) and the last point of every line or"
            "area edge." ]
      "enum:ChartKind", "The chart family a `Chart` draws its series as."
      "enum-case:ChartKind.Line", "A line chart."
      "enum-case:ChartKind.Bar", "A bar chart."
      "enum-case:ChartKind.Area", "An area chart."
      "enum-case:ChartKind.Pie", "A pie chart."
      "enum-case:ChartKind.Scatter", "A scatter plot."
      "enum-case:ChartKind.Heatmap", "A heatmap."
      "enum:ChartLegendPosition",
      lines
          [ "Which edge of the chart the series legend occupies — or `None`, which"
            "suppresses it entirely (Phase 880)."
            ""
            "A WIRE vocabulary, on the same side of the D8 line as `ChartSpec.Title`:"
            "WHERE an author wants the legend is their meaning; the geometry that puts it"
            "there — column widths, pitches, how the plot shrinks — stays the host's, in"
            "`ChartStyle`. `ChartStyle.LegendPosition` carries the DEFAULT (`Right`); an"
            "explicit `ChartSpec.LegendPosition` beats it."
            ""
            "The case set is the four an author can mean. `Left` was declared by Phase 885"
            "alongside the reserved field and is RETIRED here without ever having been"
            "consumed by a lowering path: the left edge is the y axis's, so a legend there"
            "competes with the tick column and the rotated axis title for the same band,"
            "and the vocabulary charter's demand gate found no evidence for it. Retiring"
            "an unconsumed case is cheaper than shipping a wire value that renders as a"
            "guess." ]
      "enum-case:ChartLegendPosition.Top", "A horizontal row in the top margin, under the title (the pre-880 shape)."
      "enum-case:ChartLegendPosition.Right",
      lines
          [ "A vertical column on the right — one row per series, the plot shrinking"
            "by the column's width. The shipped default." ]
      "enum-case:ChartLegendPosition.Bottom", "The same horizontal row, mirrored below the x-axis title."
      "enum-case:ChartLegendPosition.None", "No legend box at all."
      "enum:ChartXScale",
      lines
          [ "What a chart's x axis MEANS — the scale its x column is read on (Phase 882)."
            ""
            "A WIRE vocabulary on the D8 line's semantic side: whether a column is a set"
            "of CATEGORIES or a run of DATES is a fact about the data the author is"
            "declaring, not an appearance choice. Where the ticks land, how they are"
            "formatted and how much margin they need are the host's, in `ChartStyle`."
            ""
            "DECLARED, NOT INFERRED, and that is the point of the field. The chart's data"
            "schema is statically known only for an embedded table with an empty pipeline,"
            "so an inferred axis would make one wire tree draw a band axis or a temporal"
            "one depending on where its rows came from; and sniffing the cell strings for"
            "an ISO-8601 shape is a guess dressed as a rule. Declaring it lets the"
            "pre-emit validator GROUND the claim instead (FUARAN097 refuses a temporal"
            "axis over a non-date column) — the author says what the column is, and the"
            "language refuses to be wrong about it quietly." ]
      "enum-case:ChartXScale.Category",
      lines
          [ "Discrete categories, one band per row, in row order (the shipped"
            "default, and what an absent field means)." ]
      "enum-case:ChartXScale.Temporal",
      lines
          [ "Dates: the x column carries canonical ISO-8601 dates (`YYYY-MM-DD`, or a"
            "timestamp whose time-of-day is discarded) and the axis is CONTINUOUS —"
            "points sit at their date, ticks land on calendar boundaries, and the"
            "tick labels adapt to the data's granularity." ]
      "enum:CompareOp", "The comparison a cross-field `FieldRule` makes against one operand; lower-case on the wire."
      "enum-case:CompareOp.Eq", "Equal to."
      "enum-case:CompareOp.Neq", "Not equal to."
      "enum-case:CompareOp.Lt", "Less than."
      "enum-case:CompareOp.Lte", "Less than or equal to."
      "enum-case:CompareOp.Gt", "Greater than."
      "enum-case:CompareOp.Gte", "Greater than or equal to."
      "enum:DateStyle",
      "How much of a date the platform formatter writes, from short numeric to the full spelled-out form."
      "enum-case:DateStyle.Short", "The shortest form, typically numeric."
      "enum-case:DateStyle.Medium", "A medium-length form, typically with an abbreviated month."
      "enum-case:DateStyle.Long", "A long form with the month spelled out."
      "enum-case:DateStyle.Full", "The fullest form, including the weekday."
      "enum:DateTimeVariant",
      "The temporal breadth a `DateTime` or `DateTimeRange` field accepts: a date, a time of day, or both."
      "enum-case:DateTimeVariant.Date", "A calendar date only."
      "enum-case:DateTimeVariant.Time", "A time of day only."
      "enum-case:DateTimeVariant.DateTime", "A date together with a time of day."
      "enum:DeterminismSource",
      "The determinism axis of an `EffectClass`: whether the output is a pure function of its inputs or also depends on a clock, randomness or the network."
      "enum-case:DeterminismSource.Deterministic", "The output depends only on the inputs."
      "enum-case:DeterminismSource.Clock", "The output depends on the current time."
      "enum-case:DeterminismSource.Random", "The output depends on randomness."
      "enum-case:DeterminismSource.Network", "The output depends on the network."
      "enum:DurationStyle",
      lines
          [ "Phase 819 — presentation style for a duration: `Compact` \"1h 20m\","
            "`Clock` \"1:20:00\", `Long` \"1 hour 20 minutes\"." ]
      "enum-case:DurationStyle.Compact", "A compact form, such as \"1h 20m\"."
      "enum-case:DurationStyle.Clock", "A clock form, such as \"1:20:00\"."
      "enum-case:DurationStyle.Long", "A spelled-out form, such as \"1 hour 20 minutes\"."
      "enum:DurationUnit",
      lines
          [ "Phase 819 — how `Format.Duration` / `CellFormat.Duration` interpret the"
            "numeric source: the unit the raw float counts." ]
      "enum-case:DurationUnit.Seconds", "The source value counts seconds."
      "enum-case:DurationUnit.Minutes", "The source value counts minutes."
      "enum-case:DurationUnit.Hours", "The source value counts hours."
      "enum:EmbedPermission",
      "One named relaxation of an `Embed`'s otherwise fully sandboxed context; an empty list grants none."
      "enum-case:EmbedPermission.AllowScripts", "Lets the embedded content run scripts."
      "enum-case:EmbedPermission.AllowSameOrigin",
      "Lets the embedded content keep its own origin rather than an opaque one."
      "enum-case:EmbedPermission.AllowForms", "Lets the embedded content submit forms."
      "enum-case:EmbedPermission.AllowFullscreen", "Lets the embedded content enter fullscreen."
      "enum:Emphasis", "How strongly a node's text is weighted relative to its surroundings."
      "enum-case:Emphasis.Quiet", "De-emphasised, rendered lighter."
      "enum-case:Emphasis.Normal", "Ordinary emphasis; the default."
      "enum-case:Emphasis.Loud", "Emphasised, rendered bolder."
      "enum:FileReadEncoding", "How the contents of a file read from a `fileRef` are handed to the `onRead` handler."
      "enum-case:FileReadEncoding.Text", "As decoded text."
      "enum-case:FileReadEncoding.Base64", "As a base64 string of the raw bytes."
      "enum-case:FileReadEncoding.DataUrl", "As a `data:` URL."
      "enum:FontVoice",
      "The typographic voice of a node: the host's default, a display voice, or a structural one; rendered as a `fuaran-voice-*` class the host styles."
      "enum-case:FontVoice.Default", "The host's ordinary text voice; the default."
      "enum-case:FontVoice.Display", "A bold, tightly set display voice for prominent text."
      "enum-case:FontVoice.Structural", "A plain voice for structural, reading text."
      "enum:HashStrictness",
      "What happens when a `Custom` node's declared `ContentHash` does not match its registered renderer."
      "enum-case:HashStrictness.StrictReplay",
      "A mismatch is routed through the node's error path instead of rendering."
      "enum-case:HashStrictness.AdvisoryWarning", "A mismatch renders normally and logs a warning."
      "enum-case:HashStrictness.Enforced", "A mismatch is also a build-time error from the validator."
      "enum:HeadingVariant", "The typographic treatment of a `Heading`; the heading level is kept whatever the variant."
      "enum-case:HeadingVariant.Standard", "An ordinary heading; the default."
      "enum-case:HeadingVariant.Eyebrow", "A small, upper-case kicker shown above a title."
      "enum-case:HeadingVariant.Caption", "A small, muted caption-style heading."
      "enum-case:HeadingVariant.Lead", "A larger, prominent lead heading."
      "enum:HostEffect",
      "The host-effect axis of an `EffectClass`: whether a fragment leaves the host alone, reads from it, or writes to it."
      "enum-case:HostEffect.Pure", "Touches no host state."
      "enum-case:HostEffect.ReadsHost", "Reads host state."
      "enum-case:HostEffect.WritesHost", "Writes host state."
      "enum:IconSize",
      lines
          [ "Phase 821 — size class for the standalone `Icon` display kind; `Medium`"
            "is the default and is omitted on the wire." ]
      "enum-case:IconSize.Small", "A small icon."
      "enum-case:IconSize.Medium", "A medium icon; the default."
      "enum-case:IconSize.Large", "A large icon."
      "enum:ImageAspect",
      "The aspect ratio an `Image` reserves before its pixels arrive, so the layout does not shift when it loads."
      "enum-case:ImageAspect.Natural", "No reserved box; the image's own aspect applies once it loads."
      "enum-case:ImageAspect.Square", "A 1:1 box."
      "enum-case:ImageAspect.FourThree", "A 4:3 box."
      "enum-case:ImageAspect.ThreeTwo", "A 3:2 box."
      "enum-case:ImageAspect.SixteenNine", "A 16:9 box."
      "enum:ImageFit", "How an `Image`'s pixels fill the box the layout gives it."
      "enum-case:ImageFit.Natural", "The image keeps its intrinsic aspect at automatic height."
      "enum-case:ImageFit.Cover", "The image fills the box and is cropped to fit."
      "enum-case:ImageFit.Contain", "The whole image fits inside the box, letterboxed."
      "enum:ImageLoading",
      "Whether an `Image` is fetched during the initial load or deferred until it nears the viewport."
      "enum-case:ImageLoading.Eager", "Fetched with the initial load; the default."
      "enum-case:ImageLoading.Lazy",
      "Deferred until the image approaches the viewport; avoid for above-the-fold images."
      "enum:ImageVariant", "The presentation of an `Image`."
      "enum-case:ImageVariant.Default", "Rendered as-is; the default."
      "enum-case:ImageVariant.Avatar", "Rendered as a rounded avatar crop."
      "enum-case:ImageVariant.Rounded", "Rendered with rounded corners."
      "enum:LinkProtection",
      lines
          [ "Phase 812 — anti-scraper render strategy for a `Link`. `Email` marks a"
            "`mailto:` link whose address must not appear in plaintext in emitted HTML"
            "(the renderers own the emission strategy)." ]
      "enum-case:LinkProtection.Email", "A `mailto:` link whose address must not appear as plaintext in emitted HTML."
      "enum:LiveRegionKind",
      "How politely assistive technology announces changes to a node; the `aria-live` value, lower-case on the wire."
      "enum-case:LiveRegionKind.Polite", "Announced when the reader is idle."
      "enum-case:LiveRegionKind.Assertive", "Announced immediately, interrupting the reader."
      "enum-case:LiveRegionKind.Off", "Changes are not announced."
      "enum:MathDisplay", "Whether a `Math` expression is set inline with text or as its own block."
      "enum-case:MathDisplay.Inline", "Set inline within the surrounding text."
      "enum-case:MathDisplay.Block", "Set as a separate display block."
      "enum:ModalityKind", "Which overlay a `Modal` node is: a blocking dialogue or a transient popover."
      "enum-case:ModalityKind.Modal", "A blocking surface with a scrim and a focus trap; the default."
      "enum-case:ModalityKind.Popover",
      "A transient, anchored surface with no scrim or focus trap that dismisses on an outside click."
      "enum:Motion", "The animation token for a node's host-only `motion` field; it is never written to the wire."
      "enum-case:Motion.None", "No animation."
      "enum-case:Motion.PulseDuringLoad", "Pulses while the node is loading."
      "enum-case:Motion.FadeInOnMount", "Fades in when the node appears."
      "enum-case:Motion.SlideInFromBelow", "Slides in from below when the node appears."
      "enum-case:Motion.ShakeOnError", "Shakes when the node enters an error state."
      "enum-case:Motion.RotateOnRefresh", "Rotates while the node refreshes."
      "enum-case:Motion.SlideInFromRight", "Slides in from the right when the node appears."
      "enum-case:Motion.ExpandCollapse", "Animates expanding and collapsing."
      "enum-case:Motion.CrossFade",
      "Cross-fades when the content standing here is replaced, as when a `Switch` changes case."
      "enum-case:Motion.SlideBetween",
      "Slides between the old and new content when it is replaced, as when a `Switch` changes case."
      "enum:NavigateTarget",
      lines
          [ "Phase 1536 — which browsing context an `Action.Navigate` lands in."
            "Omitted at `Self` on the wire, so documents written before this release"
            "keep their bytes."
            ""
            "A closed enum, and NOT the free string `LinkSpec.target` is: that field"
            "takes any `_blank` / `_parent` / `_top` / named-frame token a web author"
            "might type, which is a vocabulary a decoded tree gets to invent. The"
            "axis here has two answers — this context, or a fresh one. `_parent` and"
            "`_top` are frame-busting gestures a hosted tree must not be able to ask"
            "for, and a named frame is an addressing scheme this language does not"
            "have." ]
      "enum-case:NavigateTarget.Self", "Navigate in the current browsing context; the default."
      "enum-case:NavigateTarget.Blank", "Open a fresh browsing context, always without an opener or referrer."
      "enum:Orientation", "A layout axis: vertical or horizontal."
      "enum-case:Orientation.Vertical", "Laid out top to bottom."
      "enum-case:Orientation.Horizontal", "Laid out side by side."
      "enum:RelativeTimeUnit", "The unit a relative time is expressed in, such as \"3 days ago\"."
      "enum-case:RelativeTimeUnit.Second", "Seconds."
      "enum-case:RelativeTimeUnit.Minute", "Minutes."
      "enum-case:RelativeTimeUnit.Hour", "Hours."
      "enum-case:RelativeTimeUnit.Day", "Days."
      "enum-case:RelativeTimeUnit.Week", "Weeks."
      "enum-case:RelativeTimeUnit.Month", "Months."
      "enum-case:RelativeTimeUnit.Year", "Years."
      "enum:ScrollOrientation", "The axis or axes a scroll area scrolls along."
      "enum-case:ScrollOrientation.Vertical", "Scrolls vertically."
      "enum-case:ScrollOrientation.Horizontal", "Scrolls horizontally."
      "enum-case:ScrollOrientation.Both", "Scrolls along both axes."
      "enum:SortDirection", "The direction of a table's declared initial sort; lower-case on the wire."
      "enum-case:SortDirection.Asc", "Ascending."
      "enum-case:SortDirection.Desc", "Descending."
      "enum:StyleRole", "The semantic content role of a node, rendered as a `fuaran-role-*` class the host styles."
      "enum-case:StyleRole.None", "No declared role; the default."
      "enum-case:StyleRole.Eyebrow", "A small, upper-case kicker above other content."
      "enum-case:StyleRole.Data", "Numeric data, set with tabular figures."
      "enum-case:StyleRole.Lede", "An introductory lede paragraph, set larger and muted."
      "enum-case:StyleRole.Caption", "A small, muted caption."
      "enum:StyleWeight", "The layout density of a node; this is spacing, not font weight."
      "enum-case:StyleWeight.Compact", "Tighter spacing."
      "enum-case:StyleWeight.Standard", "Ordinary spacing; the default."
      "enum-case:StyleWeight.Spacious", "Looser spacing."
      "enum:TextAnchor", "The horizontal alignment of a drawing `Label` about its anchor point."
      "enum-case:TextAnchor.Start", "The text starts at the anchor point."
      "enum-case:TextAnchor.Middle", "The text is centred on the anchor point."
      "enum-case:TextAnchor.End", "The text ends at the anchor point."
      "enum:TextDirection",
      "The base direction of one authored value, so it reads correctly whatever text surrounds it; lower-case on the wire."
      "enum-case:TextDirection.Auto",
      "No declared direction; resolved from the value's own characters, and the default."
      "enum-case:TextDirection.Ltr", "Left to right."
      "enum-case:TextDirection.Rtl", "Right to left."
      "enum:TextFormat", "The named input format a `FieldRule` accepts; lower-case on the wire."
      "enum-case:TextFormat.Email", "An email address."
      "enum-case:TextFormat.Url", "A URL."
      "enum-case:TextFormat.Tel", "A telephone number."
      "enum:TimeGrain", "The resolution `Binding.Now` truncates the host-furnished instant to."
      "enum-case:TimeGrain.Second", "Truncated to the second."
      "enum-case:TimeGrain.Minute", "Truncated to the minute."
      "enum-case:TimeGrain.Hour", "Truncated to the hour."
      "enum-case:TimeGrain.Day", "Truncated to the day."
      "enum:TimeStyle", "How much of a time of day the platform formatter writes, from short to the full form."
      "enum-case:TimeStyle.Short", "The shortest form, typically hours and minutes."
      "enum-case:TimeStyle.Medium", "A medium form, typically including seconds."
      "enum-case:TimeStyle.Long", "A long form, typically with the time zone."
      "enum-case:TimeStyle.Full", "The fullest form, with the full time zone."
      "enum:ToneVariant", "The semantic colour tone of a node, drawn from the theme's seven-tone palette."
      "enum-case:ToneVariant.Default", "The neutral surface; the default."
      "enum-case:ToneVariant.Subdued", "A muted surface."
      "enum-case:ToneVariant.Brand", "The primary brand accent."
      "enum-case:ToneVariant.Success", "A success state."
      "enum-case:ToneVariant.Warning", "A warning state."
      "enum-case:ToneVariant.Critical", "An error or critical state."
      "enum-case:ToneVariant.Info", "An informational accent."
      "enum:TrackKind", "What a media text track carries."
      "enum-case:TrackKind.Subtitles", "Translated dialogue for a reader who cannot follow the language."
      "enum-case:TrackKind.Captions", "Transcribed dialogue and non-speech sound for a reader who cannot hear it."
      "enum-case:TrackKind.Descriptions", "Narration of what is visible for a reader who cannot see it."
      "enum-case:TrackKind.Chapters", "Names for the media's navigable sections."
      "enum:TrendPolarity",
      "Which direction of a `Metric`'s measured quantity is good; a property of the quantity, not of one reading."
      "enum-case:TrendPolarity.HigherIsBetter", "A rising value is an improvement; the default."
      "enum-case:TrendPolarity.LowerIsBetter", "A falling value is an improvement, as for cost, latency or error rate."
      // Records and their fields
      "record:Accessibility",
      "The node envelope's accessibility overrides: accessible name, ARIA references, role, live-region politeness, hiding and the spoken rendering. Omitted entirely when the node declares none."
      "record-field:Accessibility.describedBy",
      "The id of the node that describes this one, rendered as `aria-describedby`. Omitted means no description reference."
      "record-field:Accessibility.hidden",
      "Whether the node is hidden from assistive technology (`aria-hidden`), possibly bound. Omitted means not hidden."
      "record-field:Accessibility.label",
      "The node's accessible name (`aria-label`), possibly bound. Omitted means the name is derived from the node's own content."
      "record-field:Accessibility.labelledBy",
      "The id of the node whose text names this one, rendered as `aria-labelledby`."
      "record-field:Accessibility.liveRegion",
      "How politely assistive technology announces changes to this node (`aria-live`). Omitted means the node is not a live region."
      "record-field:Accessibility.role",
      "The ARIA role to render, from the open `AriaRole` set; a custom role string is emitted verbatim. Omitted means the kind's default role."
      "record-field:Accessibility.speak",
      "The node's spoken rendering for a voice surface, as translatable `TextSource` content. It never becomes the accessible name and changes no visual or ARIA output."
      "record:Affordance",
      "How an agent-initiated instruction may address a `Select`, `Button` or form field beyond what the node already says (§31). Every member omits at its default, so `{}` declares nothing extra."
      "record-field:Affordance.patterns",
      "Further SET phrasings for a select or field, or CLICK phrasings for a button; a `{value}` slot marks where the instruction's value goes. Empty by default."
      "record-field:Affordance.readPatterns",
      "Further question phrasings, which resolve only to a read of the node's value. Empty by default."
      "record-field:Affordance.aliases", "Spoken tokens mapped onto canonical option values. Empty by default."
      "record-field:Affordance.description",
      "Free text describing the node that a reader may render and must not parse. Omitted means no description."
      "record-field:Affordance.clearable",
      "Whether the node may be cleared by an instruction such as `clear <label>`. Defaults to `false`."
      "record-field:Affordance.neverControllable",
      "When `true`, withdraws the node from the operable surface entirely, so no instruction can address it. Defaults to `false`."
      "record:AffordanceAlias", "One spoken alias in an `Affordance`, resolving a token onto a canonical option value."
      "record-field:AffordanceAlias.alias", "The spoken token an instruction may use."
      "record-field:AffordanceAlias.value", "The canonical option `value` the alias resolves to."
      "record:ButtonGroupItem",
      "One button in a `ButtonGroup` grid cell, with a label and a click handler over the row."
      "record-field:ButtonGroupItem.label", "The button's text."
      "record-field:ButtonGroupItem.onClick",
      "The handler called with the cell's row when the button is clicked; in-process only, it crosses the wire as the closure sentinel."
      "record:ColumnErased",
      "A `DataGrid` column: its header, cell kind, format, width, and where each row's cell value comes from (a row field name or a projection closure)."
      "record-field:ColumnErased.field",
      "The row property whose value this column shows, the declarative alternative to `value`. Omitted when the column uses a `value` projection."
      "record-field:ColumnErased.sortable",
      "Narrows sorting for this column: `false` opts it out. Absent inherits (sortable when the column has a `field` and the grid declares `sortStateKey`); `true` without a `sortStateKey` is an error."
      "record-field:ColumnErased.editable",
      "Narrows the grid's editability for this column: `false` makes it read-only under an editable grid. Absent inherits the grid; `true` on a grid that is not editable is an error."
      "record-field:ColumnErased.format",
      "How the column's figures are displayed. Defaults to `None`, meaning no formatting."
      "record-field:ColumnErased.kind", "How each cell in the column renders, from the `CellKindErased` vocabulary."
      "record-field:ColumnErased.label", "The column header text."
      "record-field:ColumnErased.value",
      "A projection from a row to its `CellValue`; in-process only, it crosses the wire as the closure sentinel. Omitted when the column names a `field` instead."
      "record-field:ColumnErased.width", "The column's width. Defaults to `Auto`."
      "record:CompareRule",
      "The cross-field comparison of a `FieldRule`: the field's value must stand in relation `op` to the bound `against` value."
      "record-field:CompareRule.against",
      "The value to compare with, always a `Binding`; binding `State` to a sibling field's id compares against that field's live value."
      "record-field:CompareRule.op", "The comparison the field's value must satisfy against `against`."
      "record:ContentHash",
      "A `Custom` node's content-identity envelope: a digest of the component's declared shape and what a host should do when it does not match."
      "record-field:ContentHash.algorithm", "The digest algorithm, e.g. `SHA256`."
      "record-field:ContentHash.hash", "The hex-encoded digest."
      "record-field:ContentHash.strictness",
      "What happens on a hash mismatch: fail the render, warn and render, or also fail the build."
      "record:DateTimeRangePair",
      "The `{from, to}` value of a `DateTimeRange` control: an ordered pair of ISO-8601 strings. The decoder refuses a `from` later than `to`."
      "record-field:DateTimeRangePair.from", "The range start, as an ISO-8601 string."
      "record-field:DateTimeRangePair.to", "The range end, as an ISO-8601 string; must not precede `from`."
      "record:DefaultSort", "A static table's declared initial order: the column to sort by and the direction."
      "record-field:DefaultSort.column",
      "The zero-based index into `StaticRows.headers` of the column to sort by; a negative index is refused at decode."
      "record-field:DefaultSort.direction", "Ascending or descending."
      "record:DrawPoint", "A point in a `Drawing`'s user-space coordinate system."
      "record-field:DrawPoint.x", "The horizontal coordinate, in `ViewBox` user space."
      "record-field:DrawPoint.y", "The vertical coordinate, in `ViewBox` user space."
      "record:DrawStyle",
      "The style of one `Drawing` shape: fill, stroke, opacity, text settings, rotation, mark identity and hover tip. Every member is optional, so `{}` is a valid style."
      "record-field:DrawStyle.emphasis", "The mark's semantic emphasis. Omitted means the default."
      "record-field:DrawStyle.fill", "The fill colour, possibly bound. Omitted means the host default."
      "record-field:DrawStyle.fontFamily", "The font family for text shapes. Omitted means the host default."
      "record-field:DrawStyle.fontSize",
      "The font size for text shapes, in user-space units. Omitted means the host default."
      "record-field:DrawStyle.markId",
      "The mark identity of a data-bearing shape (`series-field|category-key`), emitted as `data-fuaran-mark`. Omitted means no mark identity."
      "record-field:DrawStyle.opacity", "The shape's opacity, possibly bound. Omitted means fully opaque."
      "record-field:DrawStyle.rotation",
      "The shape's rotation in degrees, e.g. for a tilted axis label. Omitted means no rotation."
      "record-field:DrawStyle.stroke", "The stroke colour, possibly bound. Omitted means the host default."
      "record-field:DrawStyle.strokeWidth", "The stroke width, possibly bound. Omitted means the host default."
      "record-field:DrawStyle.textAnchor",
      "How a text shape aligns to its position: start, middle or end. Omitted means the host default."
      "record-field:DrawStyle.tip",
      "The mark's hover tip, rendered as the native tooltip and accessible name of the shape. Omitted means no tip."
      "record:EffectClass",
      "A `FragmentDecl`'s two-axis effect class: what it does to the host and how deterministic it is. Omitted on the wire when pure and deterministic."
      "record-field:EffectClass.determinism",
      "Whether the fragment's output depends only on its inputs, or also on a clock, randomness or the network."
      "record-field:EffectClass.hostEffect", "Whether the fragment is pure, reads host state, or writes host state."
      "record:FieldRule",
      "A form field's declared constraint on accepted values: format, pattern, length bounds and a cross-field comparison. The decoder refuses a rule with every slot absent, and a `minLength` above `maxLength`."
      "record-field:FieldRule.compare",
      "A comparison against another value, typically a sibling field's. Omitted means no comparison."
      "record-field:FieldRule.format", "A named text format the value must match. Omitted means no format check."
      "record-field:FieldRule.maxLength", "The maximum length of the value. Omitted means no upper bound."
      "record-field:FieldRule.message",
      "The message shown when the rule fails. Omitted means the host's default message."
      "record-field:FieldRule.minLength", "The minimum length of the value. Omitted means no lower bound."
      "record-field:FieldRule.pattern", "A pattern the value must match. Omitted means no pattern check."
      "record:FilterSpec",
      "One chip in a `Filters` strip: a form control, its label, and the filter name its value is stored under."
      "record-field:FilterSpec.kind",
      "The control the chip renders, from the `FormFieldKind` vocabulary; with no `value` it binds the filter named `name`."
      "record-field:FilterSpec.label", "The chip's label."
      "record-field:FilterSpec.name", "The filter's name, which is also the store key its value is written to."
      "record:FormField",
      "One field of a `Form`: its id, control, label, required flag, help text, constraint and agent affordance."
      "record-field:FormField.id",
      "The field's identifier; with no explicit `value`, the control binds `State` under this id."
      "record-field:FormField.kind", "The control the field renders, from the `FormFieldKind` vocabulary."
      "record-field:FormField.label", "The field's label."
      "record-field:FormField.required",
      "Whether the field must be filled before the form submits. Defaults to `false`."
      "record-field:FormField.help", "Help text shown with the field. Omitted means none."
      "record-field:FormField.rule",
      "The field's constraint on accepted values. Omitted means no constraint beyond `required` and the control's own bounds."
      "record-field:FormField.affordance",
      "How an agent instruction may address this field beyond its label (§31). Omitted means the field is addressed by its label alone."
      "record:GuestChannel",
      "The declared message channel of a `Mount`: which way messages may cross, and optionally the guest's message shape. The direction is a request; the renderer clamps every mount to `OutOnly` unless the host grants two-way."
      "record-field:GuestChannel.direction", "Which way messages may cross: guest-to-host only, or both ways."
      "record-field:GuestChannel.messageShape",
      "The name of the guest's message shape, used for validation and the capability gate. Omitted means none is declared."
      "record:InvokeArg",
      "One scalar argument of an `Invoke`, validated by the host against the capability's signature before dispatch."
      "record-field:InvokeArg.addr", "The argument's address in the capability's signature."
      "record-field:InvokeArg.value", "The argument's value, as a string."
      "record:MapMarker", "A labelled marker on a `Map`, at a latitude and longitude."
      "record-field:MapMarker.label", "The marker's label."
      "record-field:MapMarker.latitude", "The marker's latitude, in degrees."
      "record-field:MapMarker.longitude", "The marker's longitude, in degrees."
      "record:MarkdownCitation",
      "One figure a `Markdown` text cites: the name its `cite:<name>` link uses and the reference to the fact behind it."
      "record-field:MarkdownCitation.label",
      "What the figure is, for a screen reader; never printed. Omitted means no accessible description."
      "record-field:MarkdownCitation.name",
      "The name the text's `cite:<name>` link destination uses; its grammar and uniqueness are checked at decode."
      "record-field:MarkdownCitation.reference", "The opaque reference naming the fact behind the figure (§3.6.31)."
      "record:MetricComparison",
      "A further comparison on a `Metric`, rendered as one more labelled trend line under the primary trend."
      "record-field:MetricComparison.label", "What the comparison is against, e.g. the same day last year."
      "record-field:MetricComparison.value", "The comparison's change value, possibly bound."
      "record-field:MetricComparison.format", "How the value is displayed. Defaults to `None`, meaning no formatting."
      "record-field:MetricComparison.polarity",
      "Whether a rise is good or bad, which sets the line's sentiment. Defaults to `HigherIsBetter`."
      "record:RangePair", "The `{min, max}` value of a `Range` control."
      "record-field:RangePair.max", "The upper end of the selected range."
      "record-field:RangePair.min", "The lower end of the selected range."
      "record:SelectOption", "One option of a choice control: the text shown and the value it selects."
      "record-field:SelectOption.label", "The text shown for the option."
      "record-field:SelectOption.value", "The value the option selects."
      "record:SelectOptionGroup", "A named group of a `Select`'s options, naming its members by value."
      "record-field:SelectOptionGroup.label", "The group's heading."
      "record-field:SelectOptionGroup.values", "The `value`s of the options in this group."
      "record:SemanticStyle",
      "The node envelope's semantic style: direction, emphasis, role, tone, voice and weight. Each member omits at its default, and the whole object is omitted when all are default."
      "record-field:SemanticStyle.direction",
      "The text direction of the node's value; a declared direction isolates the run from surrounding bidirectional text. Defaults to `auto`."
      "record-field:SemanticStyle.emphasis", "The node's emphasis. Defaults to `Normal`."
      "record-field:SemanticStyle.role", "The node's semantic style role. Defaults to `None`."
      "record-field:SemanticStyle.tone", "The node's tone. Defaults to `Default`."
      "record-field:SemanticStyle.voice", "The node's font voice. Defaults to `Default`."
      "record-field:SemanticStyle.weight", "The node's weight. Defaults to `Standard`."
      "record:SrcSetEntry", "One candidate source of a responsive `Image`, with its intrinsic width."
      "record-field:SrcSetEntry.src", "The candidate's URL, possibly bound."
      "record-field:SrcSetEntry.width", "The candidate's intrinsic pixel width (the `w` descriptor); must be positive."
      "record:StateBehaviour",
      "The node envelope's async-state renderings: what to show while loading, when empty, and on error."
      "record-field:StateBehaviour.onEmpty",
      "The node rendered in place of this one when its data is empty. Omitted means the host default."
      "record-field:StateBehaviour.onError",
      "A callback building the node rendered on error from the error payload; in-process only, the wire carries only its presence."
      "record-field:StateBehaviour.onLoading",
      "The node rendered in place of this one while its data is loading. Omitted means the host default."
      "record:StaticRows", "A `DataGrid`'s static header-and-row content, with optional sort intent and cell citations."
      "record-field:StaticRows.citations", "The cells whose figures cite a fact. Empty by default."
      "record-field:StaticRows.defaultSort", "The table's initial order. Omitted means rows are shown as given."
      "record-field:StaticRows.headers", "The column headers."
      "record-field:StaticRows.rows", "The table's rows, each a list of cells in header order."
      "record-field:StaticRows.sortable",
      "Whether the table invites interactive column sorting; a host honours it with whatever sorting it has. Omitted means no sorting is declared."
      "record:StyleDelta",
      "The payload of a `PatchStyle` op: the semantic style members to change, each absent member left as it is."
      "record-field:StyleDelta.direction", "The new text direction. Omitted leaves it unchanged."
      "record-field:StyleDelta.emphasis", "The new emphasis. Omitted leaves it unchanged."
      "record-field:StyleDelta.role", "The new style role. Omitted leaves it unchanged."
      "record-field:StyleDelta.tone", "The new tone. Omitted leaves it unchanged."
      "record-field:StyleDelta.voice", "The new font voice. Omitted leaves it unchanged."
      "record-field:StyleDelta.weight", "The new weight. Omitted leaves it unchanged."
      "record:SwitchCase",
      "One branch of a `Switch`: the node it selects and when. Exactly one of `match` and `when` must be present."
      "record-field:SwitchCase.child", "The node rendered when this case is selected."
      "record-field:SwitchCase.match", "The selector value that picks this case."
      "record-field:SwitchCase.when", "A condition evaluated at render time that picks this case."
      "record:TabHeader", "The header of one tab in a `Tabs` container: label, icon and disabled state."
      "record-field:TabHeader.label", "The tab's label."
      "record-field:TabHeader.icon", "An icon shown in the tab header. Omitted means no icon."
      "record-field:TabHeader.disabled", "Whether the tab is disabled, possibly bound. Omitted means enabled."
      "record:TableCitation", "One cell of a static table whose figure cites a fact, addressed by row and column."
      "record-field:TableCitation.column", "The zero-based column index of the cell."
      "record-field:TableCitation.label",
      "What the figure is, for a screen reader; never printed. Omitted means no accessible description."
      "record-field:TableCitation.reference", "The opaque reference naming the fact behind the figure (§3.6.31)."
      "record-field:TableCitation.row", "The zero-based row index of the cell in `StaticRows.rows`."
      "record:TrackEntry", "One timed-text track of a `Media` node, such as captions or subtitles."
      "record-field:TrackEntry.default",
      "Whether this track is the default of its kind; if several are, the first wins. Defaults to `false`."
      "record-field:TrackEntry.kind", "The kind of timed text the track carries."
      "record-field:TrackEntry.label", "The track's name in the player's track menu."
      "record-field:TrackEntry.src",
      "The track file's URL, possibly bound; it passes the same URL checks as the media source."
      "record-field:TrackEntry.srcLang", "The track's language tag, required for every kind."
      "record:TransformParam",
      "One parameter of a `Transform` binding, binding a pipeline parameter name to a value source."
      "record-field:TransformParam.from", "Where the parameter's value comes from."
      "record-field:TransformParam.name", "The pipeline parameter this value binds."
      "record:TreeItem", "One row of a `Tree`, with an id, a label, an optional icon and nested child rows."
      "record-field:TreeItem.children", "The row's child rows. Empty means the row is a leaf."
      "record-field:TreeItem.icon", "An icon shown on the row. Omitted means no icon."
      "record-field:TreeItem.id",
      "The row's identity, used by the tree's expanded and selection state; unique within one `Tree`."
      "record-field:TreeItem.label", "The row's text."
      "record:ViewBox", "A `Drawing`'s user-space coordinate box, as in SVG's `viewBox`."
      "record-field:ViewBox.height", "The box's height in user-space units."
      "record-field:ViewBox.minX", "The box's left edge in user space."
      "record-field:ViewBox.minY", "The box's top edge in user space."
      "record-field:ViewBox.width", "The box's width in user-space units." ]

/// Every documentable declaration of an IDL, by its doc key (the table's key forms above), in
/// declaration order.
let docKeys (idl: Idl) : string list =
    [ for k in idl.Kinds do
          "kind:" + k.Tag

          for f in k.Fields do
              "kind-field:" + k.Tag + "." + f.Name
      for o in idl.Ops do
          "op:" + o.Tag

          for f in o.Fields do
              "op-field:" + o.Tag + "." + f.Name
      for f in idl.NodeFields do
          "node:" + f.Name
      for u in idl.Unions do
          "union:" + u.Name

          for c in u.Cases do
              "case:" + u.Name + "." + c.Tag

              for f in c.Fields do
                  "case-field:" + u.Name + "." + c.Tag + "." + f.Name
      for e in idl.Enums do
          "enum:" + e.Name

          for c in e.Cases do
              "enum-case:" + e.Name + "." + c
      for r in idl.Records do
          "record:" + r.Name

          for f in r.Fields do
              "record-field:" + r.Name + "." + f.Name ]

/// The doc table's keys in table order, duplicates kept, so a test can refuse a duplicate, a key
/// naming no declaration, and a declaration the table does not document.
let docTableKeys: string list = docs |> List.map fst

/// `idl` with every table entry laid on its declaration as the `doc` annotation; a declaration's
/// other annotations are untouched.
let private documented (idl: Idl) : Idl =
    let table = Map.ofList docs

    let doc (key: string) (a: Annotations) : Annotations =
        match Map.tryFind key table with
        | Some d -> { a with Doc = Some d }
        | None -> a

    let fields (prefix: string) (fs: IdlField list) =
        fs
        |> List.map (fun f ->
            { f with
                Annotations = doc (prefix + f.Name) f.Annotations })

    let kind (kindKey: string) (fieldKey: string) (k: IdlKind) =
        { k with
            Annotations = doc (kindKey + k.Tag) k.Annotations
            Fields = fields (fieldKey + k.Tag + ".") k.Fields }

    { idl with
        Kinds = idl.Kinds |> List.map (kind "kind:" "kind-field:")
        Ops = idl.Ops |> List.map (kind "op:" "op-field:")
        NodeFields = fields "node:" idl.NodeFields
        Unions =
            idl.Unions
            |> List.map (fun u ->
                { u with
                    Annotations = doc ("union:" + u.Name) u.Annotations
                    Cases =
                        u.Cases
                        |> List.map (fun c ->
                            { c with
                                Annotations = doc ("case:" + u.Name + "." + c.Tag) c.Annotations
                                Fields = fields ("case-field:" + u.Name + "." + c.Tag + ".") c.Fields }) })
        Enums =
            idl.Enums
            |> List.map (fun e ->
                { e with
                    Annotations = doc ("enum:" + e.Name) e.Annotations
                    CaseAnnotations =
                        e.Cases
                        |> List.map (fun c -> c, doc ("enum-case:" + e.Name + "." + c) (e.AnnotationsOf c))
                        |> List.filter (fun (_, a) -> not a.IsEmpty) })
        Records =
            idl.Records
            |> List.map (fun r ->
                { r with
                    Annotations = doc ("record:" + r.Name) r.Annotations
                    Fields = fields ("record-field:" + r.Name + ".") r.Fields }) }

/// The UI wire vocabulary: the declarations above, documented.
let uiIdl: Idl = documented declared

/// The published artifact's identity (Phase 2286). Until Fuaran.Core 0.40 `Artifact.render` wrote this
/// `description` by default; since Core Phase 527 (D40 amended) a vocabulary that wants one declares it,
/// so the UI vocabulary declares the text its `idl.json` has always carried, and the artifact's bytes
/// do not move. Render the artifact with `Artifact.renderWith uiIdlIdentity`.
let uiIdlIdentity: Artifact.Identity =
    { Name = None
      Description =
        Some
            "Canonical data rendering of the Fuaran UI wire vocabulary — kinds, unions, enums, records, field defaults and the node envelope. This is the STRUCTURAL source: it states what the vocabulary is, including optionality classes and omit-at-default values that a JSON Schema cannot express. schema.json beside it is the VALIDATION surface, derived from the same contract. Keys marked hostSurface are host-language declarations, not wire spec, and carry nothing observable on the wire. See WIRE_FORMAT.md section 13." }
