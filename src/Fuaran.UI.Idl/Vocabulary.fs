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

let private dateVariant =
    Declare.enumOf "DateVariant" [ "Date"; "Time"; "DateTime" ]

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

let private relativeTimeUnit =
    Declare.enumOf "RelativeTimeUnit" [ "Second"; "Minute"; "Hour"; "Day"; "Week"; "Month"; "Year" ]

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
      Annotations = Annotations.Empty }

let private opt (name: string) (t: IdlType) : IdlField =
    { Name = name
      Type = t
      Opt = Optional
      Annotations = Annotations.Empty }

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
      Annotations = Annotations.Empty }

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
      Annotations = Annotations.Empty }

/// `TextSource` — `Literal` (the corpus's only Display case) + `Bound`
/// (`Binding<string>`). The `I18n` case (a `Map<string, JsonValue>` arg bag)
/// rides a later slice — the IDL has no map type yet and no Display fixture uses it.
let private textSource =
    { Name = "TextSource"
      Params = []
      Cases =
        [ { Tag = "Literal"
            Fields = [ req "text" TStr ]
            Annotations = Annotations.Empty }
          { Tag = "Bound"
            Fields = [ req "binding" (TUnion("Binding", [ TStr ])) ]
            Annotations = Annotations.Empty }
          // i18n catalog lookup (Phase 692 swap-prep — the last TextSource case;
          // modelled with no corpus fixture behind it, because the hand-written
          // encoder emits it and the generated union had to hold it for the
          // swap). Phase 1078 closed that gap: `image-caption-i18n-1` carries an
          // `I18n` caption, so the case is now certified rather than merely
          // declared. `args` is a name-keyed JVal bag, always emitted (matching
          // the hand-written arm).
          { Tag = "I18n"
            Fields = [ req "key" TStr; req "args" (TMap TJson) ]
            Annotations = Annotations.Empty } ] }

/// `Binding<'T>` — the real recursive binding union, now at full case parity with
/// the hand-written tier (the Phase 692 gap-closure): every case the hand-written
/// encoder can emit is modelled — `Static` / `Query` / `Filter` / `Selection` /
/// `State` / `Computed` / `I18n` / `Local` / `Format` / `Transform` / `Invoke`.
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
            Annotations = Annotations.Empty }
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
            Annotations = Annotations.Empty }
          // `defaultValue` (Fuaran-UI 0.2.0) rides the wire when present, omitted
          // when None — the value the resolver yields before the filter is first
          // written.
          { Tag = "Filter"
            Fields = [ req "name" TStr; opt "defaultValue" (TVar "T") ]
            Annotations = Annotations.Empty }
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
            Annotations = Annotations.Empty }
          { Tag = "State"
            Fields = [ req "key" TStr; opt "defaultValue" (TVar "T") ]
            Annotations = Annotations.Empty }
          // Phase 765 — the environment "now" binding: the host furnishes the
          // instant, so the wire carries NOTHING beside the `$type` tag, and the
          // accessor is a HOST-ONLY slot restored to the identity projection on
          // decode (the Phase 427 Selection fix replayed — the host-furnished
          // instant is already the wire-shaped string, so a value-discarding
          // placeholder would make every decoded `Now` resolve to nothing).
          { Tag = "Now"
            Fields = [ hostOnly "accessor" "obj -> 'T" "(fun (raw: obj) -> unbox raw)" ]
            Annotations = Annotations.Empty }
          { Tag = "Computed"
            // `BindingContext -> 'T`. `BindingContext` is a HOST type (it carries a
            // `TryGetState<'T>` member), so the argument erases to `obj` here.
            Fields = [ req "fn" (projOf "obj" "'T" "(ctx: unknown) => T" "(fun _ -> Unchecked.defaultof<'T>)") ]
            Annotations = Annotations.Empty }
          // A controlled-input local buffer. `initialFrom` recurses at the same
          // `'T`; `format` / `onCommit` / `parse` are closures; `flushOn` is a DU.
          { Tag = "Local"
            Fields =
              [ req "flushOn" (TUnion("LocalFlushTrigger", []))
                req "format" (projOf "'T" "string" "(v: T) => string" "(fun _ -> \"\")")
                req "initialFrom" (TUnion("Binding", [ TVar "T" ]))
                opt "onCommit" (projOf "'T" "obj" "(v: T) => unknown" "(fun _ -> (\"<closure>\" :> obj))")
                req "parse" (projOf "string" "Result<'T, string>" "(s: string) => T" "(fun _ -> Error \"<closure>\")") ]
            Annotations = Annotations.Empty }
          // Locale-aware formatted string. `source` is ALWAYS `Binding<float>`
          // (independent of `'T`); `format` / `locale` are bounded DUs.
          { Tag = "Format"
            Fields =
              [ req "source" (TUnion("Binding", [ TFloat ]))
                req "format" (TUnion("Format", []))
                req "locale" (TUnion("LocaleSource", [])) ]
            Annotations = Annotations.Empty }
          // i18n catalog lookup. `args` is a name-keyed bag of `Binding<obj>`
          // placeholder sources, omitted when None. The obj-erased positions
          // (here and `Transform.params`) instantiate at `JVal` — the typed
          // verbatim carrier — because `TOpaque` would erase real defaultValues
          // to a sentinel and lose bytes.
          { Tag = "I18n"
            Fields = [ req "key" TStr; opt "args" (TMap(TUnion("Binding", [ TJson ]))) ]
            Annotations = Annotations.Empty }
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
                          Decode = "decTransformSource" })
                req
                    "pipeline"
                    (TList(
                        THosted
                            { FSharp = "Fuaran.Core.Transform"
                              Encode = "Fuaran.Core.DataFrameCodec.encodeTransform"
                              Decode =
                                "(fun __j -> Fuaran.Core.DataFrameCodec.decodeTransform __j |> Result.mapError string)" }
                    ))
                opt "params" (TList(TRecord "TransformParam")) ]
            Annotations = Annotations.Empty }
          // Host-registered capability value. Same wire shape as `Action.Invoke`.
          { Tag = "Invoke"
            Fields = [ req "capabilityId" TStr; req "args" (TList(TRecord "InvokeArg")) ]
            Annotations = Annotations.Empty } ] }

/// `CellFormat` — the column / `Metric` display-format vocabulary. `Number` /
/// `Percent` carry an *optional* `decimals` (omitted on `None`, rule 4); `Custom`
/// carries a closure fn.
let private cellFormat =
    { Name = "CellFormat"
      Params = []
      Cases =
        [ { Tag = "None"
            Fields = []
            Annotations = Annotations.Empty }
          { Tag = "Number"
            Fields = [ opt "decimals" TInt ]
            Annotations = Annotations.Empty }
          { Tag = "Currency"
            Fields = [ req "code" TStr ]
            Annotations = Annotations.Empty }
          { Tag = "Percent"
            Fields = [ opt "decimals" TInt ]
            Annotations = Annotations.Empty }
          { Tag = "SignificantDigits"
            Fields = [ req "digits" TInt ]
            Annotations = Annotations.Empty }
          { Tag = "Date"
            Fields = [ req "format" TStr ]
            Annotations = Annotations.Empty }
          // Phase 819 — trendable duration cells: the raw float counts `unit`s,
          // rendered per `style`.
          { Tag = "Duration"
            Fields = [ req "unit" (TEnum "DurationUnit"); req "style" (TEnum "DurationStyle") ]
            Annotations = Annotations.Empty }
          // Phase 819 — cell-vocabulary parity with `Format.RelativeTime`: the raw
          // float is a signed count of `unit`.
          { Tag = "RelativeTime"
            Fields = [ req "unit" (TEnum "RelativeTimeUnit") ]
            Annotations = Annotations.Empty }
          { Tag = "Custom"
            // `CellValue -> string`; `CellValue` is a host-prelude DU (stage 4b) —
            // the stage-3 obj erasure un-erased now the prelude hosts the type.
            Fields =
              [ req "fn" (projOf "Fuaran.UI.HostPrelude.CellValue" "string" "(v: unknown) => string" "(fun _ -> \"\")") ]
            Annotations = Annotations.Empty } ] }

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
            Annotations = Annotations.Empty }
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
            Annotations = Annotations.Empty }
          // Fuaran-UI 0.2.x: the dispatch msg closure is omitted entirely (no wire key).
          // `Dispatch of 'Msg`. The payload is a host value with NO wire projection —
          // `{"$type":"Dispatch"}` is the whole encoding, before and after. Declaring it
          // host-only is what lets the generated `Action` be the authoring `Action`.
          { Tag = "Dispatch"
            Fields = [ hostOnly "msg" "'Msg" "((\"<dispatch>\" :> obj))" ]
            Annotations = Annotations.Empty }
          { Tag = "Invoke"
            Fields = [ req "capabilityId" TStr; req "args" (TList(TRecord "InvokeArg")) ]
            Annotations = Annotations.Empty }
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
            Annotations = Annotations.Empty }
          // `ApiEndpoint` is a bare string on the wire; `into` is the declarative
          // result target, omitted when None; `onResult` rides only when present.
          { Tag = "Call"
            Fields =
              [ req "endpoint" TStr
                opt "onResult" (fn "obj -> 'Msg" "(r: unknown) => Msg" "(fun (_: obj) -> (\"<closure>\" :> obj))")
                opt "into" (TUnion("CallResultTarget", [])) ]
            Annotations = Annotations.Empty }
          { Tag = "Navigate"
            Fields = [ req "route" TStr ]
            Annotations = Annotations.Empty }
          { Tag = "CommitLocal"
            Fields = [ req "nodeId" TStr ]
            Annotations = Annotations.Empty }
          // Phase 676 — the three JSON-payload actions. `TJson`, never `TOpaque`:
          // these carry real data in both directions (`Notify` is the estate's
          // cross-host data primitive), so erasing them to a sentinel would be
          // silent data loss.
          { Tag = "Notify"
            Fields = [ req "channel" TStr; req "payload" TJson ]
            Annotations = Annotations.Empty }
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
            Annotations = Annotations.Empty }
          { Tag = "AiTool"
            Fields = [ req "toolName" TStr; req "args" TJson ]
            Annotations = Annotations.Empty }
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
            Annotations = Annotations.Empty } ] }

/// Where a `Call`'s result lands, declaratively. NOTE the wire tags are `State` /
/// `Query`, not the F# case names `IntoState` / `IntoQuery`.
let private callResultTarget =
    { Name = "CallResultTarget"
      Params = []
      Cases =
        [ { Tag = "State"
            Fields = [ req "key" TStr ]
            Annotations = Annotations.Empty }
          { Tag = "Query"
            Fields = [ req "name" TStr ]
            Annotations = Annotations.Empty } ] }

/// The locale-aware `Binding.Format` intent union (distinct from [[cellFormat]] —
/// this one carries `isoCode` / `dateStyle` / `unit`, not `code`).
let private formatUnion =
    { Name = "Format"
      Params = []
      Cases =
        [ { Tag = "Number"
            Fields = [ opt "decimals" TInt ]
            Annotations = Annotations.Empty }
          { Tag = "Currency"
            Fields = [ req "isoCode" TStr ]
            Annotations = Annotations.Empty }
          { Tag = "Percent"
            Fields = [ opt "decimals" TInt ]
            Annotations = Annotations.Empty }
          { Tag = "Date"
            Fields = [ req "dateStyle" (TEnum "DateStyle") ]
            Annotations = Annotations.Empty }
          { Tag = "RelativeTime"
            Fields = [ req "unit" (TEnum "RelativeTimeUnit") ]
            Annotations = Annotations.Empty }
          // Phase 819 — locale-independent duration formatting: the numeric source
          // counts `unit`s, rendered per `style`.
          { Tag = "Duration"
            Fields = [ req "unit" (TEnum "DurationUnit"); req "style" (TEnum "DurationStyle") ]
            Annotations = Annotations.Empty } ] }

let private localeSource =
    { Name = "LocaleSource"
      Params = []
      Cases =
        [ { Tag = "Ambient"
            Fields = []
            Annotations = Annotations.Empty }
          { Tag = "Explicit"
            Fields = [ req "tag" TStr ]
            Annotations = Annotations.Empty } ] }

let private localFlushTrigger =
    { Name = "LocalFlushTrigger"
      Params = []
      Cases =
        [ { Tag = "OnBlur"
            Fields = []
            Annotations = Annotations.Empty }
          { Tag = "OnSubmit"
            Fields = []
            Annotations = Annotations.Empty }
          { Tag = "OnDebounce"
            Fields = [ req "milliseconds" TInt ]
            Annotations = Annotations.Empty }
          { Tag = "OnCommitAction"
            Fields = []
            Annotations = Annotations.Empty } ] }

/// `Box.layout` — the container-layout mode (Fuaran-UI 0.2.0 Box unification). `Auto`
/// (was `Dashboard`), `Flex` (was `Stack`, carries `direction` + `wrap`), `Grid` (was
/// `GridLayout`, carries `cols` + an optional `templateColumns`), `Masonry` (column-fill).
let private layoutMode =
    { Name = "LayoutMode"
      Params = []
      Cases =
        [ { Tag = "Auto"
            Fields = []
            Annotations = Annotations.Empty }
          // `gap` (the px spacing knob, omitted-when-None) is wire vocabulary
          // on BOTH layout cases — no corpus fixture carries it; found by the
          // stage-3 swap reading the hand-written encoder.
          { Tag = "Flex"
            Fields = [ req "direction" (TEnum "Orientation"); req "wrap" TBool; opt "gap" TInt ]
            Annotations = Annotations.Empty }
          { Tag = "Grid"
            Fields = [ req "cols" TInt; opt "templateColumns" TStr; opt "gap" TInt ]
            Annotations = Annotations.Empty }
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
            Annotations = Annotations.Empty } ] }

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
              [ opt "value" (TUnion("Binding", [ TStr ]))
                opt "onChange" (handlerOf "string" "string") ]
            Annotations = Annotations.Empty }
          { Tag = "Number"
            Fields =
              [ opt "value" (TUnion("Binding", [ TFloat ]))
                opt "onChange" (handlerOf "float" "number") ]
            Annotations = Annotations.Empty }
          { Tag = "Checkbox"
            Fields =
              [ opt "value" (TUnion("Binding", [ TBool ]))
                opt "onToggle" (handlerOf "bool" "boolean") ]
            Annotations = Annotations.Empty }
          // Phase 766 — the boolean TOGGLE control: the same value / onToggle pair
          // as `Checkbox`, a distinct affordance rather than a styling of one.
          { Tag = "Toggle"
            Fields =
              [ opt "value" (TUnion("Binding", [ TBool ]))
                opt "onToggle" (handlerOf "bool" "boolean") ]
            Annotations = Annotations.Empty }
          { Tag = "Choice"
            Fields =
              [ req "options" (TUnion("Binding", [ TList(TRecord "SelectOption") ]))
                opt "value" (TUnion("Binding", [ TStr ]))
                opt "onChange" (handlerOf "string option" "string | null") ]
            Annotations = Annotations.Empty }
          { Tag = "TextArea"
            Fields =
              [ opt "value" (TUnion("Binding", [ TStr ]))
                opt "onChange" (handlerOf "string" "string")
                req "rows" TInt ]
            Annotations = Annotations.Empty }
          { Tag = "RangedNumber"
            Fields =
              [ opt "value" (TUnion("Binding", [ TFloat ]))
                opt "onChange" (handlerOf "float" "number")
                opt "min" TFloat
                opt "max" TFloat
                opt "step" TFloat ]
            Annotations = Annotations.Empty }
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
                        { FSharp = "Binding<RangePair>"
                          Encode =
                            "(fun (v: Binding<RangePair>) -> match v with | Binding.Static(Some p) -> encRangePair p | __other -> encBinding encRangePair __other)"
                          Decode =
                            "(fun (j: JVal) -> match j with | JObj __rf when not (__rf |> List.exists (fun (k, _) -> k = \"$type\")) -> decRangePair j |> Result.map (fun p -> Binding.Static(Some p)) | __other -> decBinding decRangePair __other)" })
                opt "onChange" (handlerOf "float * float" "[number, number]")
                opt "min" TFloat
                opt "max" TFloat
                opt "step" TFloat ]
            Annotations = Annotations.Empty }
          { Tag = "SegmentedChoice"
            Fields =
              [ req "options" (TUnion("Binding", [ TList(TRecord "SelectOption") ]))
                opt "value" (TUnion("Binding", [ TStr ]))
                opt "onChange" (handlerOf "string option" "string | null")
                req "orientation" (TEnum "Orientation") ]
            Annotations = Annotations.Empty }
          { Tag = "Date"
            Fields =
              [ opt "value" (TUnion("Binding", [ TStr ]))
                opt "onChange" (handlerOf "string option" "string | null")
                req "variant" (TEnum "DateVariant")
                opt "min" TStr
                opt "max" TStr
                opt "step" TFloat ]
            Annotations = Annotations.Empty }
          // Single-control date range (Fuaran-UI Phase 725) — `Range`'s pair
          // mechanics with `Date`'s value conventions. The value slot carries
          // the same transparent-Static posture as `Range`: a `Static` pair
          // rides as the BARE `{from, to}` object (no `Static` envelope), any
          // other binding rides enveloped; both directions via the slot codec.
          { Tag = "DateRange"
            Fields =
              [ opt
                    "value"
                    (THosted
                        { FSharp = "Binding<DateRangePair>"
                          Encode =
                            "(fun (v: Binding<DateRangePair>) -> match v with | Binding.Static(Some p) -> encDateRangePair p | __other -> encBinding encDateRangePair __other)"
                          Decode =
                            "(fun (j: JVal) -> match j with | JObj __rf when not (__rf |> List.exists (fun (k, _) -> k = \"$type\")) -> decDateRangePair j |> Result.map (fun p -> Binding.Static(Some p)) | __other -> decBinding decDateRangePair __other)" })
                opt "onChange" (handlerOf "string * string" "[string, string]")
                req "variant" (TEnum "DateVariant")
                opt "min" TStr
                opt "max" TStr
                opt "step" TFloat ]
            Annotations = Annotations.Empty }
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
                opt "onChange" (handlerOf "string option" "string | null")
                req "options" (TUnion("Binding", [ TList(TRecord "SelectOption") ]))
                opt "value" (TUnion("Binding", [ TStr ])) ]
            Annotations = Annotations.Empty }
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
                opt "onChange" (handlerOf "float" "number")
                opt "value" (TUnion("Binding", [ TFloat ])) ]
            Annotations = Annotations.Empty }
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
              [ opt "onChange" (handlerOf "string" "string")
                opt "value" (TUnion("Binding", [ TStr ])) ]
            Annotations = Annotations.Empty }
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
                opt "onChange" (handlerOf "string list" "string[]")
                opt "suggestions" (TUnion("Binding", [ TList(TRecord "SelectOption") ]))
                opt "value" (TUnion("Binding", [ TList TStr ])) ]
            Annotations = Annotations.Empty } ] }

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
            Annotations = Annotations.Empty }
          { Tag = "Audio"
            Fields = []
            Annotations = Annotations.Empty } ] }

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
            Annotations = Annotations.Empty }
          { Tag = "Fixed"
            Fields = [ req "pixels" TInt ]
            Annotations = Annotations.Empty }
          { Tag = "Flex"
            Fields = [ req "weight" TFloat ]
            Annotations = Annotations.Empty } ] }

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
            Annotations = Annotations.Empty }
          { Tag = "Numeric"
            Fields = []
            Annotations = Annotations.Empty }
          { Tag = "Date"
            Fields = []
            Annotations = Annotations.Empty }
          { Tag = "Editable"
            // `(Row * CellValue) -> Action<'Msg>`; `CellValue` is a host-prelude DU
            // (stage 4b) — the typed edit payload survives the swap. Row closures
            // take `Fuaran.Core.Row` since fuaran#665 (the rows slot is typed, so
            // the accessors' argument is the name-addressable row, not `obj`).
            Fields =
              [ opt "onEdit" (handlerOf "Fuaran.Core.Row * Fuaran.UI.HostPrelude.CellValue" "[unknown, unknown]") ]
            Annotations = Annotations.Empty }
          { Tag = "Checkbox"
            Fields =
              [ req "get" (projOf "Fuaran.Core.Row" "bool" "(row: unknown) => boolean" "(fun _ -> false)")
                opt "onToggle" (handlerOf "Fuaran.Core.Row * bool" "[unknown, boolean]") ]
            Annotations = Annotations.Empty }
          { Tag = "Button"
            Fields =
              [ req "label" (TUnion("TextSource", []))
                opt "onClick" (handlerOf "Fuaran.Core.Row" "unknown") ]
            Annotations = Annotations.Empty }
          { Tag = "ButtonGroup"
            Fields = [ req "buttons" (TList(TRecord "ButtonGroupItem")) ]
            Annotations = Annotations.Empty }
          { Tag = "Link"
            Fields =
              [ req "hrefFn" (projOf "Fuaran.Core.Row" "string" "(row: unknown) => string" "(fun _ -> \"\")")
                req
                    "labelFn"
                    (projOf
                        "Fuaran.Core.Row"
                        "TextSource"
                        "(row: unknown) => TextSource"
                        "(fun _ -> TextSource.Literal \"\")") ]
            Annotations = Annotations.Empty }
          { Tag = "Pill"
            Fields =
              [ req
                    "labelFn"
                    (projOf
                        "Fuaran.Core.Row"
                        "TextSource"
                        "(row: unknown) => TextSource"
                        "(fun _ -> TextSource.Literal \"\")")
                req
                    "toneFn"
                    (projOf
                        "Fuaran.Core.Row"
                        "ToneVariant"
                        "(row: unknown) => ToneVariant"
                        "(fun _ -> ToneVariant.Default)") ]
            Annotations = Annotations.Empty }
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
            Annotations = Annotations.Empty }
          { Tag = "Progress"
            Fields =
              [ req "fractionFn" (projOf "Fuaran.Core.Row" "float" "(row: unknown) => number" "(fun _ -> 0.0)")
                // The hand-written tier's label is genuinely optional (a progress
                // cell with no label) — `opt`, stage 4b. The hand encoder emitted
                // an unconditional sentinel; omit-when-None is the honest form and
                // no fixture pins the None-label emission.
                opt
                    "labelFn"
                    (projOf
                        "Fuaran.Core.Row"
                        "TextSource"
                        "(row: unknown) => TextSource"
                        "(fun _ -> TextSource.Literal \"\")") ]
            Annotations = Annotations.Empty }
          { Tag = "Custom"
            // `(Row -> JVal) -> Node<'Msg>` — a cell renderer over the row projector.
            Fields =
              [ req
                    "fn"
                    (fn
                        "(Fuaran.Core.Row -> JVal) -> Node<'Msg>"
                        "(proj: (row: unknown) => unknown) => Node"
                        "(fun _ -> Unchecked.defaultof<Node<obj>>)") ]
            Annotations = Annotations.Empty } ] }

// ─── Meta-family unions (parameterised fragments) ───────────────────────────

/// A hole's value-space (bind-time validation domain) on a `FragmentDecl`.
let private holeValueSpace =
    { Name = "HoleValueSpace"
      Params = []
      Cases =
        // Hand-written positional order (min before max) — wire-free.
        [ { Tag = "IntRange"
            Fields = [ req "min" TInt; req "max" TInt ]
            Annotations = Annotations.Empty }
          { Tag = "FloatRange"
            Fields = [ req "min" TFloat; req "max" TFloat ]
            Annotations = Annotations.Empty }
          { Tag = "StringLen"
            Fields = [ req "minLen" TInt; req "maxLen" TInt ]
            Annotations = Annotations.Empty }
          { Tag = "Enum"
            Fields = [ req "choices" (TList TStr) ]
            Annotations = Annotations.Empty }
          { Tag = "AnyString"
            Fields = []
            Annotations = Annotations.Empty } ] }

/// A boxed scalar — a hole default or a `FragmentRef` value arg. Self-describing
/// (`$type` pins the CLR shape).
let private scalar =
    { Name = "Scalar"
      Params = []
      Cases =
        [ { Tag = "Int"
            Fields = [ req "value" TInt ]
            Annotations = Annotations.Empty }
          { Tag = "Float"
            Fields = [ req "value" TFloat ]
            Annotations = Annotations.Empty }
          { Tag = "Bool"
            Fields = [ req "value" TBool ]
            Annotations = Annotations.Empty }
          { Tag = "Str"
            Fields = [ req "value" TStr ]
            Annotations = Annotations.Empty } ] }

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
            Annotations = Annotations.Empty }
          { Tag = "Slot"
            Fields = [ req "name" TStr; opt "kindConstraint" TStr ]
            Annotations = Annotations.Empty }
          { Tag = "Repeat"
            Fields = [ req "name" TStr; req "countSpace" (TUnion("HoleValueSpace", [])) ]
            Annotations = Annotations.Empty } ] }

/// A bound argument at a `FragmentRef` — a scalar value or a slot subtree. Shares
/// the scalar tags with [[scalar]] plus `SlotArg` (a `Node`-bearing tree).
let private fragmentArg =
    { Name = "FragmentArg"
      Params = []
      Cases =
        [ { Tag = "Int"
            Fields = [ req "value" TInt ]
            Annotations = Annotations.Empty }
          { Tag = "Float"
            Fields = [ req "value" TFloat ]
            Annotations = Annotations.Empty }
          { Tag = "Bool"
            Fields = [ req "value" TBool ]
            Annotations = Annotations.Empty }
          { Tag = "Str"
            Fields = [ req "value" TStr ]
            Annotations = Annotations.Empty }
          { Tag = "SlotArg"
            Fields = [ req "tree" TNode ]
            Annotations = Annotations.Empty } ] }

// ─── Records (non-discriminated objects — no `$type`) ───────────────────────

let private invokeArgRecord =
    { Name = "InvokeArg"
      Fields = [ req "addr" TStr; req "value" TStr ] }

/// An option in a `Select` / `Choice` / `SegmentedChoice` payload (Fuaran-UI 0.2.x
/// typed-Static: the choice `source` / `options` carry a real `SelectOption` list).
let private selectOptionRecord =
    { Name = "SelectOption"
      Fields = [ req "label" TStr; req "value" TStr ] }

/// A `Map.source` marker (Fuaran-UI 0.2.x typed-Static: the map source carries a real
/// marker list instead of the opaque sentinel).
let private mapMarkerRecord =
    { Name = "MapMarker"
      Fields = [ req "label" TStr; req "latitude" TFloat; req "longitude" TFloat ] }

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
      Fields = [ req "column" TInt; req "direction" (TEnum "SortDirection") ] }

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
let private staticRowsRecord =
    { Name = "StaticRows"
      Fields =
        [ opt "defaultSort" (TRecord "DefaultSort")
          req "headers" (TList(TUnion("TextSource", [])))
          req "rows" (TList(TList(TUnion("TextSource", []))))
          opt "sortable" TBool ] }

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
      Fields = [ req "against" (TUnion("Binding", [ TJson ])); req "op" (TEnum "CompareOp") ] }

/// A `FormField`'s declared constraint (Fuaran-UI Phase 864) — the accepted SET,
/// where `FormFieldKind` names the CONTROL. Every slot is `Optional`, so a form
/// authored before the addition encodes byte-identically: absence is absence.
///
/// **No numeric or temporal bound lives here.** `RangedNumber` already carries
/// `min`/`max` and `Date` already carries `min`/`max`; the charter's reuse rule
/// is that the rule slot never duplicates a bound the control carries. What is
/// left is format, pattern, length, and the cross-field operand.
///
/// A rule with EVERY slot absent is refused by the tier's policy decoder — a
/// rule that constrains nothing is a defect, not a no-op — as is a `minLength`
/// above its `maxLength` (the `DateRangePair` ordered-pair rule applied to a
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
          opt "pattern" TStr ] }

/// Phase 864 adds one OPTIONAL `rule` slot. `required` stays where it is: it is
/// the pre-existing degenerate rule, and moving it under `rule` would be a
/// breaking change to a field every existing fixture carries.
let private formFieldRecord =
    { Name = "FormField"
      Fields =
        [ req "id" TStr
          req "kind" (TUnion("FormFieldKind", []))
          req "label" (TUnion("TextSource", []))
          req "required" TBool
          opt "help" (TUnion("TextSource", []))
          opt "rule" (TRecord "FieldRule") ] }

let private filterSpecRecord =
    { Name = "FilterSpec"
      Fields =
        [ req "kind" (TUnion("FormFieldKind", []))
          req "label" (TUnion("TextSource", []))
          req "name" TStr ] }

/// One `Binding.Transform` parameter — binds a pipeline `ColExpr.Param` name to a
/// scalar binding source. `from` instantiates `Binding` at `JVal` (the typed
/// verbatim carrier for obj-erased positions).
let private transformParamRecord =
    { Name = "TransformParam"
      Fields = [ req "from" (TUnion("Binding", [ TJson ])); req "name" TStr ] }

/// The `{max, min}` payload of a `Range` control's value — the wire shape of the
/// hand-written tier's `(min, max)` float pair (the IDL has no tuple type; the
/// record IS the wire object, so nothing is lost in the trade).
let private rangePairRecord =
    { Name = "RangePair"
      Fields = [ req "max" TFloat; req "min" TFloat ] }

/// The `{from, to}` payload of a `DateRange` control's value (Fuaran-UI Phase
/// 725) — the ordered ISO-8601 pair, `RangePair`'s record-IS-the-wire-object
/// trade for the hand-written tier's `(from, to)` string pair. The ordering
/// rule (`from` ≤ `to`, ordinal) is decoder POLICY, not structure — it lives in
/// the tier's lenient/reject layer, not here.
let private dateRangePairRecord =
    { Name = "DateRangePair"
      Fields = [ req "from" TStr; req "to" TStr ] }

let private tabHeaderRecord =
    { Name = "TabHeader"
      Fields =
        [ req "label" (TUnion("TextSource", []))
          opt "icon" TStr
          opt "disabled" (TUnion("Binding", [ TBool ])) ] }

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
          // row argument is typed `Fuaran.Core.Row` since fuaran#665.
          opt
              "value"
              (projOf
                  "Fuaran.Core.Row"
                  "Fuaran.UI.HostPrelude.CellValue"
                  "(row: unknown) => unknown"
                  "(fun _ -> Fuaran.UI.HostPrelude.CellValue.Empty)")
          omit "width" (TUnion("ColumnWidth", [])) (VUnion("Auto", [])) ] }

/// One button of a `CellKindErased.ButtonGroup` (`onClick` is a closure over the row).
let private buttonGroupItemRecord =
    { Name = "ButtonGroupItem"
      Fields =
        [ req "label" (TUnion("TextSource", []))
          opt "onClick" (handlerOf "Fuaran.Core.Row" "unknown") ] }

/// A `Custom` node's content-identity envelope (`strictness` is a bare-string DU).
let private contentHashRecord =
    { Name = "ContentHash"
      Fields =
        [ req "algorithm" TStr
          req "hash" TStr
          req "strictness" (TEnum "HashStrictness") ] }

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
      Fields = [ req "src" (TUnion("Binding", [ TStr ])); req "width" TInt ] }

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
          req "srcLang" TStr ] }

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
          req "label" (TUnion("TextSource", [])) ] }

/// A `FragmentDecl`'s two-axis effect class (omitted on the wire when pure +
/// deterministic — modelled here as an optional field on the kind).
let private effectClassRecord =
    { Name = "EffectClass"
      Fields =
        [ req "determinism" (TEnum "DeterminismSource")
          req "hostEffect" (TEnum "HostEffect") ] }

// ─── Type aliases for the field shapes (readability) ───────────────────────

let private TS = TUnion("TextSource", [])
let private bindingOf (t: IdlType) = TUnion("Binding", [ t ])
let private CF = TUnion("CellFormat", [])
/// `IconSource` is a bare string on the wire (`"icon":"trending-up"`).
let private icon = TStr

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
        [ omit "direction" (TEnum "TextDirection") (VEnum "Auto")
          omit "emphasis" (TEnum "Emphasis") (VEnum "Normal")
          omit "role" (TEnum "StyleRole") (VEnum "None")
          omit "tone" (TEnum "ToneVariant") (VEnum "Default")
          omit "voice" (TEnum "FontVoice") (VEnum "Default")
          omit "weight" (TEnum "StyleWeight") (VEnum "Standard") ] }

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
          opt "onLoading" TNode ] }

/// `aria-live` politeness — closed, and lower-case on the wire. Phase 707: the
/// lower-case half of the old "doesn't fit `TEnum`" excuse is gone, so this is a
/// declared enum with a case↔wire mapping rather than a host-owned codec. Case
/// order matches the tier's DU so the generated declaration lands identically.
let private liveRegionKind =
    Declare.enumWith "LiveRegionKind" [ "Polite", "polite"; "Assertive", "assertive"; "Off", "off" ]

/// `{ "describedBy"?, "hidden"?, "label"?, "labelledBy"?, "liveRegion"?, "role"? }`.
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
                    Decode = "Fuaran.UI.HostPrelude.decAriaRole" }) ] }

// ─── Display kinds (flat `$type`-discriminated) ────────────────────────────

let displayKinds: IdlKind list =
    [ { Tag = "Heading"
        Category = "Display"
        Annotations = Annotations.Empty
        Fields = [ req "level" TInt; req "text" TS; req "variant" (TEnum "HeadingVariant") ] }
      { Tag = "Badge"
        Category = "Display"
        Annotations = Annotations.Empty
        Fields = [ req "label" TS; req "variant" (TEnum "BadgeVariant") ] }
      { Tag = "Markdown"
        Category = "Display"
        Annotations = Annotations.Empty
        Fields = [ req "text" TS ] }
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
        Fields = [ req "items" (TList TS); req "ordered" TBool ] }
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
            req "variant" (TEnum "ImageVariant")
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
            req "download" TBool
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
            req "value" TS ] }
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
          [ req "code" TStr
            req "copyable" TBool
            req "highlightLines" (TList TInt)
            req "language" TStr
            req "lineNumbers" TBool ] }
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
      // into one `Box` kind carrying `role` (BoxRole) + `layout` (LayoutMode). The other
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
            req "layout" (TUnion("LayoutMode", []))
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
            req "dismissable" TBool
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
          [ req "activeIndex" (bindingOf TInt)
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
          [ req "activeStep" (bindingOf TInt)
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
            req "variant" (TEnum "ButtonVariant")
            opt "icon" TStr
            // WIRE_FORMAT.md §10.1 — `ButtonSpec.Tooltip` is typed surface but
            // NOT wire vocabulary: never emitted, restored to `None` on decode.
            // Modelling it `opt` made the generated encoder emit a field the
            // spec forbids — invisible to the corpus (no fixture carries it),
            // caught by the Phase 101 idempotence fuzz at the 694 collapse.
            hostOnly "tooltip" "TextSource option" "None"
            opt "disabled" (bindingOf TBool) ] }
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
            req "value" (bindingOf TStr)
            opt "placeholder" (TUnion("TextSource", []))
            opt "disabled" (bindingOf TBool)
            opt "multiple" TBool
            opt "values" (bindingOf (TList TStr)) ] }
      { Tag = "FileUpload"
        Category = "Input"
        Annotations = Annotations.Empty
        Fields =
          [ req "accept" (TList TStr)
            req "label" (TUnion("TextSource", []))
            req "multiple" TBool
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
            opt "destination" TStr ] }
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
// `Fuaran.Core.Row seq`, rendered by Core's `RowCodec`; the `"<opaque>"` sentinel
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
            opt "rowKey" (projOf "Fuaran.Core.Row" "string" "(row: unknown) => string" "(fun _ -> \"\")")
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
            // The row feed is HOSTED `Fuaran.Core.Row seq` (fuaran#665 — typed rows):
            // a Static/State rows payload IS wire-representable (a JSON array of row
            // objects, scalar cells, rendered by Core's `RowCodec` under the `Canon`
            // discipline), and decode accepts the legacy `"<opaque>"` sentinel
            // indefinitely (read-compat → the empty feed).
            req
                "source"
                (bindingOf (
                    THosted
                        { FSharp = "Fuaran.Core.Row seq"
                          Encode = "Fuaran.Core.RowCodec.encodeRows"
                          Decode = "Fuaran.Core.RowCodec.decodeRows" }
                ))
            opt "staticRows" (TRecord "StaticRows")
            opt "onRowClick" (handlerOf "Fuaran.Core.Row" "unknown") ] }
      { Tag = "Chart"
        Category = "Visualisation"
        Annotations = Annotations.Empty
        Fields =
          [ req "kind" (TEnum "ChartKind")
            // The row feed is HOSTED `Fuaran.Core.Row seq` (fuaran#665 — typed rows,
            // same `RowCodec` + read-compat sentinel acceptance as `DataGrid.source`).
            req
                "source"
                (bindingOf (
                    THosted
                        { FSharp = "Fuaran.Core.Row seq"
                          Encode = "Fuaran.Core.RowCodec.encodeRows"
                          Decode = "Fuaran.Core.RowCodec.decodeRows" }
                ))
            req "stacked" TBool
            req "xField" TStr
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
            opt "onPointClick" (handlerOf "Fuaran.Core.Row" "unknown") ] }
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
      Fields = [ req "x" TFloat; req "y" TFloat ] }

let private viewBoxRecord =
    { Name = "ViewBox"
      Fields =
        [ req "height" TFloat
          req "minX" TFloat
          req "minY" TFloat
          req "width" TFloat ] }

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
          opt "tip" TS ] }

let private curveCommand =
    { Name = "CurveCommand"
      Params = []
      Cases =
        // The destination point is `to` on every command — NOT the F# case-field
        // names (`point` / `endpoint`), which is what the first cut of this
        // modelled and why `drawing-1` failed to decode. Read the wire.
        [ { Tag = "MoveTo"
            Fields = [ req "to" (TRecord "DrawPoint") ]
            Annotations = Annotations.Empty }
          { Tag = "LineTo"
            Fields = [ req "to" (TRecord "DrawPoint") ]
            Annotations = Annotations.Empty }
          { Tag = "CubicTo"
            Fields =
              [ req "control1" (TRecord "DrawPoint")
                req "control2" (TRecord "DrawPoint")
                req "to" (TRecord "DrawPoint") ]
            Annotations = Annotations.Empty }
          { Tag = "QuadraticTo"
            Fields = [ req "control" (TRecord "DrawPoint"); req "to" (TRecord "DrawPoint") ]
            Annotations = Annotations.Empty }
          { Tag = "Close"
            Fields = []
            Annotations = Annotations.Empty } ] }

/// Recursive: `Group` carries `Shape list`. Every case carries a `style`.
let private shape =
    { Name = "Shape"
      Params = []
      Cases =
        [ { Tag = "Group"
            Fields =
              [ req "children" (TList(TUnion("Shape", [])))
                req "style" (TRecord "DrawStyle") ]
            Annotations = Annotations.Empty }
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
            Annotations = Annotations.Empty }
          { Tag = "Line"
            Fields =
              [ req "x1" TFloat
                req "y1" TFloat
                req "x2" TFloat
                req "y2" TFloat
                req "style" (TRecord "DrawStyle") ]
            Annotations = Annotations.Empty }
          { Tag = "Polyline"
            Fields = [ req "points" (TList(TRecord "DrawPoint")); req "style" (TRecord "DrawStyle") ]
            Annotations = Annotations.Empty }
          { Tag = "Polygon"
            Fields = [ req "points" (TList(TRecord "DrawPoint")); req "style" (TRecord "DrawStyle") ]
            Annotations = Annotations.Empty }
          { Tag = "Curve"
            Fields =
              [ req "commands" (TList(TUnion("CurveCommand", [])))
                req "style" (TRecord "DrawStyle") ]
            Annotations = Annotations.Empty }
          { Tag = "Circle"
            Fields =
              [ req "cx" TFloat
                req "cy" TFloat
                req "r" TFloat
                req "style" (TRecord "DrawStyle") ]
            Annotations = Annotations.Empty }
          { Tag = "Ellipse"
            Fields =
              [ req "cx" TFloat
                req "cy" TFloat
                req "rx" TFloat
                req "ry" TFloat
                req "style" (TRecord "DrawStyle") ]
            Annotations = Annotations.Empty }
          { Tag = "Label"
            Fields =
              [ req "x" TFloat
                req "y" TFloat
                req "text" TS
                req "style" (TRecord "DrawStyle") ]
            Annotations = Annotations.Empty } ] }

/// Phase 679 — a `Switch` case: the match string plus the node it selects. The
/// tier holds this as a `(string * Node) tuple list`, which the IDL has no type
/// for; on the wire it is a two-field record, so that is what is modelled.
let private switchCase =
    { Name = "SwitchCase"
      Fields = [ req "child" TNode; req "match" TStr ] }

/// Phase 679 — `Mount`'s guest channel. `messageShape` rides only on `TwoWay`
/// in practice but is optional in the shape, not conditional on direction.
let private guestChannel =
    { Name = "GuestChannel"
      Fields = [ req "direction" (TEnum "ChannelDirection"); opt "messageShape" TStr ] }

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
        Fields = [ req "child" TNode; req "fallback" TNode ] }
      { Tag = "FragmentDecl"
        Category = "Meta"
        Annotations = Annotations.Empty
        // holes / effect are omitted for the degenerate fixed-body fragment.
        Fields =
          [ req "body" TNode
            req "name" TStr
            opt "holes" (TList(TUnion("HoleDecl", [])))
            opt "effect" (TRecord "EffectClass") ] }
      { Tag = "FragmentRef"
        Category = "Meta"
        Annotations = Annotations.Empty
        // args omitted for the degenerate name-only ref.
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
            req "default" TNode
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
        Fields = [ req "style" (TRecord "SemanticStyle"); req "target" TStr ] } ]

let uiIdl: Idl =
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
          layoutMode
          formFieldKind
          mediaKind
          columnWidth
          cellKindErased
          holeValueSpace
          scalar
          holeDecl
          fragmentArg
          curveCommand
          shape ]
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
          captureSource
          dateVariant
          textFormat
          compareOp
          dateStyle
          relativeTimeUnit
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
          stateBehaviourRecord
          accessibilityRecord
          switchCase
          guestChannel
          drawPoint
          viewBoxRecord
          drawStyle
          invokeArgRecord
          selectOptionRecord
          mapMarkerRecord
          defaultSortRecord
          staticRowsRecord
          compareRuleRecord
          fieldRuleRecord
          formFieldRecord
          filterSpecRecord
          transformParamRecord
          rangePairRecord
          dateRangePairRecord
          tabHeaderRecord
          columnErasedRecord
          buttonGroupItemRecord
          contentHashRecord
          srcSetEntryRecord
          trackEntryRecord
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
          opt "tooltip" TS ]
      Ops = treeOps
      Wire = WireShape.Default
      Harden = HardenPolicy.Default }
