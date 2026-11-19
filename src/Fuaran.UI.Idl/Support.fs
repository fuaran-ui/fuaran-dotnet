module Fuaran.UI.VocabularySupport

// ---------------------------------------------------------------------------
// Phase 945 (fuaran-ui workspace) — the declared-support record for the UI
// vocabulary's generated layer.
//
// Everything here USED to be a hand edit to the generated artefact — the
// "hand-added ahead of the IDL backfill" regions that Phases 812/818/819/821
// left in `Fuaran-UI/fuaran-dotnet/src/Fuaran.UI/Generated.fs` while the IDL
// caught up. The catch-up left the two copies diverged in both directions, and
// the tier sync (WIRE_FORMAT §11 step 1) regressed the tier whenever it ran.
// Phase 945 moved the content HERE, beside the IDL, as data the generator
// emits: the docs land on the generated declarations, the splices join the
// generated recursion groups verbatim, and the `Switch` host projection keeps
// the tier's merged `On: Binding<string>` API while the IDL keeps describing
// the wire's two optional keys. The emission is reproducible again, and the
// sync is a byte-copy again.
//
// The one semantic seam to understand before editing: `TransformSource` is a
// DISCRIMINATED-BY-INSPECTION union (a binding-shaped `$type` versus a plain
// columnar shape) — not a `$type`-tagged union the IDL could model — so its
// type and codecs are verbatim splices, and the Transform `source` slot is a
// `THosted` pointing at them by name.
// ---------------------------------------------------------------------------

open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types

/// The `Switch` host projection: the wire has TWO optional keys (`on`, the
/// compact `stateKey`), the F# API has ONE required `On: Binding<string>` —
/// Phase 768's merge, kept while the IDL keeps describing the wire.
let private switchProjection: KindProjection =
    { SpecDecl =
        """SwitchSpec<'Msg> =
    {
      /// The ordered cases, each a `match` string and the child it renders; the first match wins.
      Cases: SwitchCase<'Msg> list
      /// The node rendered when no case matches, and before the selector first resolves.
      Default: Node<'Msg>
      // Phase 768 — the branch SELECTOR is any Binding, not only a StateStore
      // key: `on: {"$type":"Selection",…}` lets the branch follow the clicked
      // row with no writer at all, which is what closes 032/c6 (the failing
      // emissions wired a Switch to a stateKey nothing emittable could write).
      // The state form keeps its compact spelling on the wire — see the
      // encoder's collapse rule.
      /// The binding whose string value selects the case. On the wire a `State` selector is
      /// written in its compact form, `stateKey`, and any other binding as `on`.
      On: Binding<string>
      // Fuaran-UI Phase 1122 — the timed-advance interval, in milliseconds.
      // `None` is the only spelling of "does not advance": the renderer starts
      // no timer, and a switch authored before this release is unchanged in the
      // type, on the wire and on the screen.
      //
      // A duration rather than a flag, because "advances" with no interval is
      // not renderable and two hosts inventing a period is the divergence the
      // corpus exists to prevent. Non-positive values are refused at decode
      // rather than read as "off" — absence already means off.
      /// The interval, in milliseconds, at which the switch advances to its next case on its own;
      /// must be positive. `None` means it does not advance.
      AutoAdvanceMs: int option
    }"""
      Encoder =
        """and private encSwitchSpec<'Msg> (s: SwitchSpec<'Msg>) : JVal =
    Canonical.typed "Switch" ([ Some("cases", JArr(List.map encSwitchCase s.Cases)); Some("default", encNode s.Default); (match s.On with | Binding.State (key, None) -> Some("stateKey", JStr key) | on -> Some("on", (encBinding JStr) on)); (s.AutoAdvanceMs |> Option.map (fun v -> "autoAdvanceMs", JInt v)) ] |> List.choose id)"""
      Decoder =
        """and private decSwitchSpec (j: JVal) : Result<SwitchSpec<obj>, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "cases" __fs (dList decSwitchCase) |> Result.bind (fun cases ->
    dReq "default" __fs decNode |> Result.bind (fun ``default`` ->
    // Phase 768 — `on` (any Binding) or the compact `stateKey` (State form).
    // When both are absent the stateKey requirement carries the MISSING_FIELD,
    // keeping the existing reject fixture's error byte-identical.
    (match dOpt "on" __fs (decBinding dStr) with
     | Ok (Some on) -> Ok on
     | Ok None -> dReq "stateKey" __fs dStr |> Result.map (fun key -> Binding.State(key, None))
     | Error e -> Error e) |> Result.bind (fun on ->
    // Phase 1122 — `autoAdvanceMs` is optional, and a PRESENT value must be a
    // positive integer. `0` and negatives are refused rather than read as
    // "off", on the `Masonry.cols` ruling: absence is already the spelling of
    // off, so a rewrite would make two spellings mean one thing and hide the
    // emitter's misunderstanding of the slot.
    (match dOpt "autoAdvanceMs" __fs dInt with
     | Ok (Some ms) when ms > 0 -> Ok (Some ms)
     | Ok (Some _) -> dFail DecodeCode.OutOfRange "positive integer" "autoAdvanceMs must be a positive integer" |> dUnder (PathSegment.Key "autoAdvanceMs")
     | Ok None -> Ok None
     | Error e -> Error e) |> Result.bind (fun autoAdvanceMs ->
    Ok { Cases = cases; Default = ``default``; On = on; AutoAdvanceMs = autoAdvanceMs })))))"""
      // A projected kind supplies its OWN `mk`, so the node envelope is spelled
      // here by hand rather than derived from `Idl.NodeFields` the way the
      // forty-one generated constructors' envelope is. A new envelope field
      // therefore has to be added here too — Fuaran-UI Phase 1112's `tooltip` is
      // the first one to land since this projection seam did, and it arrived as
      // `FS0764` on this literal alone. The coupling is left as it is
      // deliberately: the compiler names the one site, in the same build that
      // adds the field, which is a stronger guarantee than a convention nobody
      // re-reads. What is added is this note, so the next envelope field is
      // expected here rather than discovered here.
      Mk =
        Some
            """let mkSwitch (id: string) (cases: SwitchCase<'Msg> list) (``default``: Node<'Msg>) (stateKey: string) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Switch { Cases = cases; Default = ``default``; On = Binding.State(stateKey, None); AutoAdvanceMs = None }; Accessibility = None; ExtraAttributes = None; Fallback = None; Motion = None; State = None; Style = None; Tooltip = None; Visible = None }"""
      // Core 0.37.0 (fuaran-core Phase 403): a projection carries its own message map, because the
      // generator cannot construct the projected record. Phase 2188 supplies it, so the derived
      // `mapMsg` (see `derivations` below) covers `Switch` with the rest: the cases and the default
      // branch are trees and map through the derived mappers; `On` is a `Binding`, which carries no
      // message, and `AutoAdvanceMs` is an integer, so both copy across.
      MapMsg =
        Some
            """and private mapMsgSwitchSpec<'Msg, 'Msg2> (f: 'Msg -> 'Msg2) (v: SwitchSpec<'Msg>) : SwitchSpec<'Msg2> =
    ({ Cases = List.map (fun __x -> mapMsgSwitchCase f __x) v.Cases; Default = mapMsg f v.Default; On = v.On; AutoAdvanceMs = v.AutoAdvanceMs }: SwitchSpec<'Msg2>)"""
      // The projected record's fields at their HOST shapes (fuaran-core Phase 403), in the IDL's
      // order, so `StructuralAccess` reads `SwitchSpec` as it exists: `on` is the REQUIRED merged
      // binding, and the wire's compact `stateKey` has no field here.
      RecordFields =
        let field name t opt =
            { Name = name
              Type = t
              Opt = opt
              Annotations = Annotations.Empty
              Aliases = []
              Presence = None
              Keyed = false }

        Some
            [ field "autoAdvanceMs" TInt Optional
              field "cases" (TList(TRecord "SwitchCase")) Required
              // Phase 2640 — the default branch is a keyed ARM, as the wire declaration says.
              { field "default" TNode Required with
                  Keyed = true }
              field "on" (TUnion("Binding", [ TStr ])) Required ] }

/// The declared support for `Gen.fsharpModuleWith` — see the module doc above.
let support: GenSupport =
    { GenSupport.Empty with
        Docs =
            Map.ofList
                [ "dec:DurationUnit", [ "// Phase 819 — Duration format enums." ]
                  "dec:Icon", [ "// Phase 821 — Icon display kind." ]
                  "dec:IconSize", [ "// Phase 821 — Icon size class." ]
                  "decarm:Binding.Now",
                  [ "// Identity accessor (the Phase 427 Selection fix replayed): the"
                    "// host-furnished instant is already the wire-shaped string, so a"
                    "// decoded reader receives it as-is; a value-discarding placeholder"
                    "// would make every decoded `Now` resolve to nothing." ]
                  "decarm:Binding.Transform",
                  [ "// Phase 818 — a binding-shaped source (State / Selection / Query"
                    "// `$type`) is preserved as `TransformSource.Live`; the initial"
                    "// snapshot derives from the binding's carried default data via the"
                    "// host-prelude helpers (a State source must carry data — the"
                    "// Phase-815 posture; Selection/Query fall back to the empty"
                    "// table). Anything else decodes through Core's columnar codec as"
                    "// before, byte-identical." ]
                  "enc:DurationUnit", [ "// Phase 819 — Duration format enums." ]
                  "enc:Icon",
                  [ "// Phase 821 — Icon display kind."
                    "// `size` omitted-when-`Medium`, `tone` omitted-when-`Default`, `label`"
                    "// omitted-when-`None` (decorative)." ]
                  "enc:IconSize", [ "// Phase 821 — Icon size class." ]
                  "encarm:Action.SetState",
                  [ "// Phase 818 — `value` / `valueFrom` are XOR siblings; each is emitted only"
                    "// when present (Canon sorts keys, so the field order stays alphabetical)." ] ]
        TypeSplice =
            Some
                """/// Phase 818 — a `Binding.Transform`'s source slot. `Data` is the
/// canonical columnar / `ref` source (the pre-818 shape, byte-identical on the
/// wire). `Live` preserves a binding-shaped source (State / Selection / Query)
/// verbatim so a runtime re-evaluates the Transform with subscription
/// semantics when the binding's channel changes; `initial` is the decode-time
/// snapshot table derived from the binding's carried default data (never
/// encoded — the binding IS the wire form), which SSR / diagnostic evaluation
/// reads, byte-identical to the Phase-815 snapshot for the same input.
and [<RequireQualifiedAccess>] TransformSource =
    /// A columnar table or `ref` source, decoded once.
    | Data of source: Fuaran.Core.DataSource
    /// A binding-shaped source kept live, with the snapshot table decoded from its carried data.
    | Live of binding: Binding<JVal> * initial: Fuaran.Core.DataSource"""
        EncodeSplice =
            Some
                """// Phase 818 — a `Data` source keeps the Core columnar encoding byte-identical; a
// `Live` source re-encodes the preserved binding itself (one wire dialect — the
// State/Selection/Query-shaped source round-trips byte-for-byte; the derived
// `initial` snapshot is never encoded).
and private encTransformSource (s: TransformSource) : JVal =
    match s with
    | TransformSource.Data ds -> Fuaran.Core.ColumnCodec.encodeJson ds
    | TransformSource.Live (b, _) -> (encBinding id) b

// Phase 1661 — one `TextSource.I18n` argument. A `Static` argument carrying a
// value emits the BARE value: that is the whole reason the widening from a
// `JVal` bag to a `Binding<JVal>` bag moves no shipped byte, because every
// literal argument ever emitted decodes to exactly that arm. Every other arm
// emits its own `$type` object, and `Static` carrying NO value emits
// `{"$type":"Static"}` — absence is structural (Phase 677) and there is no bare
// spelling of it.
//
// The tagged `{"$type":"Static","value":v}` spelling is therefore decode-accepted
// and normalises DOWN to the bare form on re-encode, which is `TextSource.Literal`'s
// own bare-string rule one level in (§16).
and private encI18nArg (arg: Binding<JVal>) : JVal =
    match arg with
    | Binding.Static (Some v) -> v
    | other -> (encBinding id) other"""
        DecodeSplice =
            Some
                """// Phase 818 — the Transform source slot. A `$type` of State / Selection / Query preserves the binding as
// `TransformSource.Live` with the initial snapshot derived from its carried
// default data (`Fuaran.UI.HostPrelude.TransformLive`). Every other shape
// decodes through Core's columnar codec unchanged.
//
// Phase 1085 — a State source carrying NO data decodes to a live source over
// the EMPTY initial snapshot, exactly as a Selection / Query source already
// did. It used to be refused through the columnar codec (the Phase-815
// posture, correct when nothing could fill the slot), which made the most
// direct way to say "I read this key and carry no data of my own"
// unspellable: under the Phase-1075 seeding rule a SIBLING reader's
// declaration fills the slot, and FUARAN106's own remedy text tells an author
// to write precisely this shape. `"defaultValue": []` remains legal and means
// the same thing; the bare form is no longer a second answer to one question.
and private decTransformSource (j: JVal) : Result<TransformSource, string> =
    let asData (v: JVal) : Result<TransformSource, string> =
        Fuaran.Core.ColumnCodec.decodeJson v |> Result.map TransformSource.Data |> Result.mapError string

    match j with
    | JObj fields ->
        match fields |> List.tryFind (fun (k, _) -> k = "$type") with
        | Some(_, JStr(("State" | "Selection" | "Query") as tag)) ->
            decBinding dJson j |> Result.mapError DecodeError.describe |> Result.bind (fun b ->
                let carried =
                    match b with
                    | Binding.State(_, dv) -> dv
                    | Binding.Selection(_, _, dv, _) -> dv
                    | _ -> None

                match carried, tag with
                | Some data, "State" ->
                    // A State source's carried data IS the initial snapshot —
                    // it must decode as a table (the Phase-815 posture).
                    Fuaran.UI.HostPrelude.TransformLive.initialSource data
                    |> Result.map (fun initial -> TransformSource.Live(b, initial))
                    |> Result.mapError Fuaran.Core.ColumnCodec.errorString
                | Some data, _ ->
                    // A Selection default may legitimately be a scalar / row
                    // shape rather than a table; fall back to the empty
                    // initial (the runtime evaluation stays loud on mismatch).
                    match Fuaran.UI.HostPrelude.TransformLive.initialSource data with
                    | Ok initial -> Ok(TransformSource.Live(b, initial))
                    | Error _ -> Ok(TransformSource.Live(b, Fuaran.UI.HostPrelude.TransformLive.emptySource))
                // Phase 1085 — no carried data, on ANY of the three tags: the
                // binding is preserved live over the empty initial snapshot.
                // The State arm used to fall through to `asData` here.
                | None, _ -> Ok(TransformSource.Live(b, Fuaran.UI.HostPrelude.TransformLive.emptySource)))
        | _ -> asData j
    | _ -> asData j

// Phase 1661 — the `TextSource.I18n` argument slot, discriminated BY INSPECTION.
// An object carrying a `$type` member is a binding and decodes as one; ANY other
// JSON value is the literal argument and decodes to `Static` carrying it.
//
// Two consequences worth stating where the rule is implemented. A literal
// argument that is ITSELF a JSON object carrying a `$type` member is not
// expressible — the inspection reads it as a binding — and an unrecognised
// `$type` is a refusal rather than a fallback to the literal reading, because a
// document that names a binding case this host does not know is a document the
// host cannot honour, and reading it as an object literal would substitute the
// discriminator's own text into a caption.
and private decI18nArg (j: JVal) : Result<Binding<JVal>, string> =
    match j with
    | JObj fields when fields |> List.exists (fun (k, _) -> k = "$type") -> decBinding dJson j |> Result.mapError DecodeError.describe
    | literal -> dJson literal |> Result.map (Some >> Binding.Static) |> Result.mapError DecodeError.describe"""
        AccessorSplice =
            Some
                """// Phase 2286 — the two record accessors the generator emitted for itself until
// Fuaran.Core 0.40 (Phase 524 removed them); the op notation, the canonical op
// codec and the merge engine read them, so the vocabulary declares them here.
let encodeStateBehaviourJson (s: StateBehaviour<'Msg>) : JVal = encStateBehaviour s
let encodeSemanticStyleJson (s: SemanticStyle) : JVal = encSemanticStyle s
let encodeAccessibilityJson (a: Accessibility) : JVal = encAccessibility a

// Phase 818 — JVal-level accessor for a data-shaped Action (the
// `encodeNodeKindJson` precedent): the server resume script re-encodes a
// `SetState` whose payload is a `valueFrom` Binding through the canonical
// encoder rather than growing a second hand-rolled binding encoder.
let encodeActionJson (a: Action<'Msg>) : JVal = encAction a

// Phase 1126 — the same accessor one level down, for the two hand-written
// codecs that encode a `TextSource` slot OUTSIDE a node: the op-stream's
// canonical-JSON action encoder and the action-log's payload projection. Both
// grew a `JStr` where the clipboard payload used to be a bare string; neither
// should grow a second hand-rolled `TextSource` encoder to replace it, because
// `TextSource.Literal`'s bare-string canonical form (§3.6) is exactly the kind
// of rule that two copies drift on.
let encodeTextSourceJson (t: TextSource) : JVal = encTextSource t

// Phase 2061 — a `Binding` slot OUTSIDE a node: the op-stream's `ReplaceBinding`
// encodes through the same generated binding encoder a node's slots do, with
// the caller supplying the encoding of the `'T` payload positions (`Static.value`,
// the `defaultValue`s, recursively through `Local.initialFrom`) — the op codec's
// own obj-erased payload policy rather than a second binding encoder.
let encodeBindingJson (encodePayload: 'T -> JVal) (b: Binding<'T>) : JVal = encBinding encodePayload b"""
        CaseRefines =
            Map.ofList
                [ "Action.SetState",
                  """// Phase 818 — value XOR valueFrom (a literal, or a Binding
            // evaluated at dispatch time); exactly one must be present.
            match value, valueFrom with
            | Some _, Some _ -> Error "SetState carries both 'value' and 'valueFrom' — exactly one is allowed ('value' is a literal; 'valueFrom' derives the written value from a Binding at dispatch time)"
            | None, None -> Error "SetState requires 'value' (a literal JSON value) or 'valueFrom' (a Binding evaluated at dispatch time)"
            | _ -> Ok(Action.SetState(key, value, valueFrom))"""
                  // Phase 1130 — the scale's LOWER BOUND is a decode refusal,
                  // and the value's bounds are not. A `max` of zero or less is
                  // not a rating with a bad number in it: there is no control
                  // to draw, no `aria-valuemax` to announce and no keystroke
                  // that could change anything, so the document is refused
                  // where it is read — the `Switch.autoAdvanceMs` line. A
                  // `value` outside the scale is the other half of that split
                  // and belongs to the validator and the server-driven floor,
                  // because a bound value is invisible from here and a rule
                  // enforced only on literals would be two rules.
                  "FormFieldKind.Rating",
                  """if max < 1 then
                Error (sprintf "Rating 'max' must be at least 1 — a scale with %d positions cannot be rendered or announced" max)
            else
                Ok(FormFieldKind.Rating(allowHalf, max, onChange, value))"""
                  // Phase 1130 — the STATIC half of the colour-format rule.
                  // `<input type="color">` can hold exactly `#rrggbb`, so a
                  // literal that is not that shape names a colour this control
                  // could never carry, and refusing it here gives the emitter
                  // the error at the earliest point anything can see it.
                  //
                  // RECORDED SPLIT — only a `Static` literal is checked, because
                  // it is the only text a decoder HAS. A `State` / `Query` /
                  // `Selection` binding carries its value from outside the
                  // document; the pre-emit validator re-states the rule for an
                  // author, and `FormValidation` enforces it on submission,
                  // which is the trust boundary that actually matters. One rule,
                  // checked wherever the value becomes visible — never a
                  // coercion, at any of the three.
                  "FormFieldKind.Color",
                  """match value with
            | Some(Binding.Static(Some(JStr __text))) when not (Fuaran.UI.HostPrelude.HexColor.isValid __text) ->
                Error (sprintf "Color 'value' must be a '#rrggbb' hex colour — got %s" __text)
            | _ -> Ok(FormFieldKind.Color(onChange, value))"""
                  // Phase 1121 — the ONE cross-member refusal on `Tokens`, and the
                  // `Rating.max < 1` line applied to a pair of members rather
                  // than to one. A field that admits no free text AND declares
                  // no suggestion source can never hold a token: there is no
                  // gesture that adds one, so the document names a control that
                  // cannot exist rather than a control with a bad value in it.
                  //
                  // It is reachable only DELIBERATELY, which is what makes
                  // refusing it right. `allowFreeText` omits at TRUE on this
                  // case (see the vocabulary comment), so the shortest document
                  // `{"$type":"Tokens"}` is the open token input and passes; only
                  // an emitter that wrote `"allowFreeText":false` and then
                  // omitted `suggestions` lands here.
                  //
                  // What is NOT refused: duplicates and membership. Those are
                  // properties of the VALUE, invisible to a decoder whenever the
                  // slot is bound, and they are held by the validator and the
                  // server-driven floor instead — the split `Rating` records.
                  "FormFieldKind.Tokens",
                  """if not allowFreeText && Option.isNone suggestions then
                Error "Tokens declares 'allowFreeText' false and no 'suggestions' source — the field could admit no token by any gesture. Give it a suggestion source, or leave 'allowFreeText' at its default of true"
            else
                Ok(FormFieldKind.Tokens(allowFreeText, onChange, suggestions, value))""" ]
        KindProjections = Map.ofList [ "Switch", switchProjection ] }

/// The declared-support DOCUMENT — the record above plus the host-prelude
/// declaration: members 2 and 3 of the regeneration triple, as one document
/// (`support.json`) beside member 1 (`idl.json`).
///
/// `Path` is relative to the document that declares it, so the triple resolves
/// wherever the domain is checked out. It names `src/Fuaran.UI/HostPrelude.fs`
/// because that is where the prelude has to live: it is compiled into `Fuaran.UI`
/// AHEAD of the generated module, so the generated code can reference the host
/// types and codecs the vocabulary's `THosted` slots and `TFn` placeholders name.
/// The generator never reads the prelude's text — knowing that one exists, what
/// module it declares and where it is, is the whole statement.
let document: SupportDocument =
    { Support = support
      HostPrelude =
        Some
            { Module = "Fuaran.UI.HostPrelude"
              Path = "../Fuaran.UI/HostPrelude.fs" } }

// ---------------------------------------------------------------------------
//  The SAMPLED-VECTOR NARROWING the host projections above necessitate
//  (Phase 1668; recovered from `Fuaran-Core@ccead29^`'s `UiIdlSupport.fs`).
//
//  Two of this vocabulary's shapes carry a CROSS-FIELD rule the IDL's type
//  language cannot state, so `Sample.trySampleNodes` — which samples every
//  combination the field declarations allow — draws vectors OFF the canonical
//  wire:
//
//    * `Switch` has two optional keys (`on`, the compact `stateKey`) of which
//      the wire carries exactly one, and the projection's encoder implements
//      the collapse while its decoder implements the fallback. The sampler
//      happily draws both, neither, or an `on` the canonical encoder would
//      respell.
//    * `Action.SetState` carries `value` XOR `valueFrom`, refused by the host
//      refine in both the both-present and neither-present directions.
//
//  On such a vector the host projection is honestly lossy or refusing while the
//  IDL interpreter simply echoes it, so a cross-leg comparison reports a
//  divergence that is an artefact of the sampler rather than of either codec.
//  This rewrite narrows a sampled vector into the canonical space, in the same
//  deterministic way for every leg.
//
//  It is declared HERE, beside the projections that necessitate it, rather than
//  inside the certifying test project: the narrowing is visible support-data
//  for those projections, and retiring a projection should retire it. It is
//  NOT part of the support DOCUMENT — `support.json` renders from `document`
//  above and is unchanged by these declarations, which the regeneration triple
//  in `Fuaran.UI.Idl.Tests` proves on every run.
// ---------------------------------------------------------------------------

/// Rewrite one sampled `Switch` field set to the canonical `on`/`stateKey`
/// spelling.
let private canonicaliseSwitchFields (fields: (string * IdlValue) list) : (string * IdlValue) list =
    // `VAbsent` is the sampler's spelling of an omitted optional — see the note
    // on `canonicaliseSetStateFields` below.
    let hasOn = fields |> List.exists (fun (n, v) -> n = "on" && v <> VAbsent)

    let hasKey = fields |> List.exists (fun (n, v) -> n = "stateKey" && v <> VAbsent)

    // 1. Both spellings present: no conformant encoder emits this; `on` wins,
    //    exactly as the shipped decoder resolves it.
    let fields =
        if hasOn && hasKey then
            fields |> List.filter (fun (n, _) -> n <> "stateKey")
        else
            fields

    // 2. `on` carrying a plain no-default State binding: the canonical encoder
    //    collapses it to `stateKey` — respell so re-encoding is the identity.
    //    Any `("stateKey", VAbsent)` shadow entry must go FIRST: leaving it
    //    alongside the rewrite makes a duplicate key, and in the TypeScript
    //    value literal the trailing `undefined` wins and silently swallows the
    //    real one (found the hard way — the interpreter leg agreed with itself
    //    while the TS leg dropped the field).
    let collapse =
        fields
        |> List.tryPick (fun (n, v) ->
            match n, v with
            | "on", VUnion("State", sf) when sf |> List.forall (fun (fn, fv) -> fn <> "defaultValue" || fv = VAbsent) ->
                sf |> List.tryFind (fun (fn, _) -> fn = "key") |> Option.map snd
            | _ -> None)

    let fields =
        match collapse with
        | Some key ->
            fields
            |> List.filter (fun (n, _) -> n <> "stateKey" && n <> "on")
            |> fun rest -> ("stateKey", key) :: rest
        | None -> fields

    // 3. Neither spelling: the wire requires one; supply the deterministic
    //    compact form (the decoder's own MISSING_FIELD didactic names it).
    if
        fields
        |> List.exists (fun (n, v) -> (n = "on" || n = "stateKey") && v <> VAbsent)
    then
        fields
    else
        ("stateKey", VStr "k")
        :: (fields |> List.filter (fun (n, _) -> n <> "stateKey"))

/// Rewrite one sampled `SetState` field set to the value-XOR-valueFrom wire:
/// the same cross-field class as `Switch` — the refine refuses both-present and
/// neither-present, which field-level optionality happily samples.
let private canonicaliseSetStateFields (fields: (string * IdlValue) list) : (string * IdlValue) list =
    // The sampler spells an OMITTED optional as an explicit `VAbsent` entry, so
    // presence is "named AND not VAbsent" — counting the name alone reads every
    // omission as a population (found the hard way: the first cut did).
    let has name =
        fields |> List.exists (fun (n, v) -> n = name && v <> VAbsent)

    if has "value" && has "valueFrom" then
        // No conformant encoder emits both; keep the literal (deterministic).
        fields |> List.filter (fun (n, _) -> n <> "valueFrom")
    elif not (has "value") && not (has "valueFrom") then
        // The wire requires one; supply the deterministic literal form.
        ("value", VJson(Fuaran.Core.JVal.JStr "v"))
        :: (fields |> List.filter (fun (n, _) -> n <> "value"))
    else
        fields

/// The kinds a sampled `fallback` (Phase 1812) is kept on. The sampler draws
/// every optional envelope field present 2-in-3 and recurses, so left alone a
/// `fallback` — a whole second node — lands on two nodes in three at every level:
/// the vectors balloon, and the generated-F# leg's domain (vectors populating no
/// `THosted` slot anywhere) collapses, because a fallback's own `accessibility.role`
/// is as hosted as the carrier's. Steered rather than dropped, on the language's
/// own rule: an emitter reaches for a fallback only when it is emitting a kind a
/// behind reader may lack, so the canonical space keeps it on ONE rich, visual
/// kind — what a fallback is FOR — and omits it elsewhere. One carrier is enough:
/// the coverage assertion that every wire envelope field is sampled present
/// holds on it, and every further carrier costs the leg's domain (measured: five
/// carriers left 643 of 4000 vectors hosted-free, under the 666 floor). The codec
/// of the slot itself is pinned by the corpus vectors
/// (`nodes/envelope-fallback.json`) rather than by the sweep's volume.
let private fallbackCarriers = set [ "Chart" ]

/// Strip every `fallback` from a subtree — a fallback INSIDE a fallback is what
/// FUARAN157 refuses pre-emit, so no conformant encoder emits one and the
/// canonical space carries none.
let rec private stripFallbacks (v: IdlValue) : IdlValue =
    let inFields fields =
        fields |> List.map (fun (n, fv) -> n, stripFallbacks fv)

    match v with
    | VNode(id, kindTag, fields) -> VNode(id, kindTag, inFields fields)
    | VNodeEnv(id, envelope, kindTag, fields) ->
        let envelope =
            envelope
            |> List.map (fun (n, fv) -> if n = "fallback" then n, VAbsent else n, stripFallbacks fv)

        VNodeEnv(id, envelope, kindTag, inFields fields)
    | VUnion(tag, fields) -> VUnion(tag, inFields fields)
    | VRecord fields -> VRecord(inFields fields)
    | VList xs -> VList(xs |> List.map stripFallbacks)
    | VMap entries -> VMap(inFields entries)
    | _ -> v

/// Rewrite one sampled envelope's `fallback`: kept (one level deep, per
/// FUARAN157) on a carrier kind, absent elsewhere.
let private canonicaliseEnvelopeFields (kindTag: string) (envelope: (string * IdlValue) list) =
    envelope
    |> List.map (fun (n, fv) ->
        if n = "fallback" && fv <> VAbsent then
            (if fallbackCarriers.Contains kindTag then
                 n, stripFallbacks fv
             else
                 n, VAbsent)
        else
            n, fv)

/// Narrow a sampled vector into the canonical space — see the note above.
let rec canonicaliseVector (v: IdlValue) : IdlValue =
    let inFields fields =
        fields |> List.map (fun (n, fv) -> n, canonicaliseVector fv)

    match v with
    | VNode(id, kindTag, fields) ->
        let fields = inFields fields

        VNode(
            id,
            kindTag,
            (if kindTag = "Switch" then
                 canonicaliseSwitchFields fields
             else
                 fields)
        )
    | VNodeEnv(id, envelope, kindTag, fields) ->
        let fields = inFields fields

        VNodeEnv(
            id,
            inFields (canonicaliseEnvelopeFields kindTag envelope),
            kindTag,
            (if kindTag = "Switch" then
                 canonicaliseSwitchFields fields
             else
                 fields)
        )
    | VUnion(tag, fields) ->
        let fields = inFields fields

        VUnion(
            tag,
            (if tag = "SetState" then
                 canonicaliseSetStateFields fields
             else
                 fields)
        )
    | VRecord fields -> VRecord(inFields fields)
    | VList xs -> VList(xs |> List.map canonicaliseVector)
    | VMap entries -> VMap(inFields entries)
    // Every remaining case is a leaf (scalars, closures, raw JSON, opaque
    // markers): nothing to recurse into, nothing cross-field to rewrite.
    | _ -> v

/// Phase 2039 — the structural derivations the generated layer carries
/// (`Gen.fsharpModuleDerived`, fuaran-core#374): the members that follow
/// mechanically from the vocabulary and that the tier's consumers would
/// otherwise write by hand. Each request names what it replaces.
///
/// Phase 2188 adds `MapMsg`, which 2039 had to leave out: it reads the `Switch`
/// kind, a host projection, and is honoured since fuaran-core#403 because
/// `switchProjection` declares its own `MapMsg` member.
///
/// One request is still ABSENT: `SlotsOf "Binding"`. Since fuaran-core#403 the
/// generator reads `Switch` through its `RecordFields` and the emission is right,
/// but it does not compile in this tier, which builds with nullness checking on.
/// A slot enumerator over a generic union boxes each value into a `let ofKind`
/// that carries no type annotation, so the list is inferred at `objnull`, and
/// appending it under the declared `(string * obj) list` result is FS3261. The
/// fix is the generator's (annotate `ofKind` with the result's element type);
/// the request waits for a Core release that carries it.
let derivations: Derivation list =
    [
      // `wireTag` / `allWireTags` / `children` / `withChildren`, with
      // `nodeWitness` built on them — the wire-tag enumeration and the
      // DataGrid tag adaptation read these instead of a hand list.
      Derivation.StructuralAccess
      // `Action.fold` — the walks over the action union (`Chain` and
      // `Confirm` both nest actions) fold through it, so none can forget a
      // recursive case.
      Derivation.Fold "Action"
      // `default<Record>` for every record whose fields all have a value
      // without the caller — `Defaults.style`, `.stateBehaviour`,
      // `.drawStyle` and `.Accessibility.empty` read these.
      Derivation.DefaultRecords
      // `kindCategories`, `kindFieldNames`, `envelopeFieldNames` and
      // `opFieldNames` as `Set`s. The decoder's ORDERED kind groups and
      // `opWireFields` stay hand-declared: their order is the cross-host error
      // hint, and the op fields carry a required flag these sets do not.
      Derivation.VocabularyConstants
      // Phase 2177 — `FormFieldKind.value` and `FormFieldKind.onChange`. Every
      // case declares its value as `Binding<JVal>` and its change handler as
      // `onChange: JVal -> Action`, so both project to one type; the walks that
      // read a field's binding or ask whether a handler is present read these
      // instead of matching fifteen cases each.
      Derivation.Projections("FormFieldKind", [ "value"; "onChange" ])
      // Phase 2188 — `mapMsg`, the tree with its message type rewritten. It
      // replaces the hand-written structural map `NodeMap` carried, so a new
      // kind or field cannot leave the map behind.
      Derivation.MapMsg
      // Phase 2639 — the collecting decoders and the public per-spec entries
      // (Core's `CollectingDecoders` record). `JsonDecode` reads the kind specs
      // it has moved off its own structure through them, supplying its policy
      // as overrides of that record; the rest it still decodes by hand.
      Derivation.SpecDecoders
      // Phase 2640 — `keyedChildren` / `withKeyedChildren` / `keyedWitness`: every node position
      // the vocabulary declares that the structural `children` does not list — the keyed arms
      // (`ErrorBoundary.child` / `.fallback`, `Switch.default`, each `Switch` case's child), slot
      // arguments, and the envelope's `fallback` and `state` alternatives. Certified against
      // `Introspect`'s hand pair under Core's `witnessPairLawsAt` (GeneratedLayerTests).
      Derivation.KeyedPositions ]
