module Fuaran.UI.JsonDecode.Tests.BindingStaticPresence

// ============================================================================
//  The `Static` payload's PRESENCE, stated by the schema (Phase 1140) — the
//  second axis of the instantiation Phase 1068 typed, and the residue 1068
//  left.
//
//  1068 typed every `Binding` slot's payload: ten instantiations
//  (`Binding_float`, `Binding_int`, `Binding_str`, `Binding_bool`, the typed
//  collections, and two named any-JSON abstentions), each slot `$ref`ing its
//  own. What it did not type is whether the payload is THERE. `bindingDef`
//  emitted the `Static` arm with `value` OPTIONAL for all ten — Phase 677's
//  "absence is structural" — so `{"$type":"Static"}` validated against
//  `Binding_float` exactly as it validated against `Binding_json`, while the
//  decoder routes the missing key through the slot's OWN parser and the answer
//  differs per slot: a scalar parser refuses the resulting `JNull`, a
//  collection parser normalises it to the empty collection, and a choice slot
//  reads it as "no selection".
//
//  That gap was not cosmetic. A schema-driven emitter that fills a kind's
//  REQUIRED fields and nothing else — the shape every such walker takes,
//  because required-ness is the only signal the dialect gives it — emitted the
//  valueless form at every scalar `Binding` slot and the decoder then refused
//  the node. Nine display/input kinds were unsynthesisable for exactly this
//  reason, and no care on the emitting side recovers a presence rule the schema
//  does not state.
//
//  ── Why closing it was a SPLIT and not a tightening ────────────────────────
//
//  "Make `value` required in the scalar instantiations" would have broken the
//  wire. The schema pointed `Choice.value`, `SegmentedChoice.value`,
//  `Combobox.value` and `Select.value` at the SAME `#/$defs/Binding_str` as
//  `TextArea.value`, `Link.href` and `Image.src` — but the decoder splits them.
//  The control slots go through `decodeBindingChoiceValue`, where an absent
//  `Static` payload is first-class ("no selection", the typed `Static None`);
//  the rest go through the scalar string path, which refuses it. One `$def`,
//  two contracts, and four shipped accept fixtures (`controls-closure`,
//  `form-segmented`, `multiselect-1`, `multiselect-chip-list-param`) are what a
//  required-key edit would have started refusing.
//
//  So the four control slots now point at `#/$defs/Binding_str_choice`, whose
//  `Static.value` is optional and whose `description` says what the absence
//  MEANS, and `#/$defs/Binding_str` requires its payload like the other three
//  scalars.
//
//  ── The shape of the pin ──────────────────────────────────────────────────
//
//  Three lists, one per contract, each asserting BOTH sides of the seam — what
//  the schema says and what the decoder does — so a change that moves one
//  without the other fails here rather than in whatever consumes the artefact:
//
//    * `scalarSlotsRefuseValuelessStatic` — schema INVALID, decoder ERROR.
//      Phase 1068's residue, closed. Each entry also carries the same node with
//      a payload present, asserted valid + decodable, so a probe cannot pass by
//      being malformed for some unrelated reason.
//    * `choiceSlotsAcceptValuelessStatic` — schema VALID, decoder OK. The four
//      slots the split exists for; each is go-red evidence for its repointing.
//    * `absentablePayloadsStayLegal` — schema VALID, decoder OK at the
//      collection and any-JSON instantiations, where absence normalises rather
//      than refuses. Without this list a future over-tightening — "make every
//      `Static.value` required" — would look like an improvement.
//
//  Every probe is a minimal node built inline: no corpus fixture is added or
//  read for it, so the pin costs the shared corpus nothing.
// ============================================================================

open System.Text.Json
open Expecto
open Json.Schema
open Fuaran.UI.Ops

/// The canonical `$id` the emitter stamps on the document, and the suite-local
/// one this file registers under instead.
///
/// JsonSchema.Net registers every built document in a PROCESS-GLOBAL registry
/// keyed by `$id`, and refuses a second registration of the same URI.
/// `SchemaConformanceTests` already builds this exact document, so building it
/// again verbatim here throws — and because that throw happens in a module
/// initialiser it takes the WHOLE assembly's test discovery with it, reported
/// against whichever of the two modules happened to lose the race. It did,
/// once, before these two lines existed. Re-keying is the whole fix: every
/// internal reference in the document is `#/$defs/…`, resolved within the
/// document, so nothing about what is evaluated changes.
[<Literal>]
let private canonicalId = "https://fuaran.dev/wire-format/v1/schema.json"

[<Literal>]
let private suiteLocalId =
    "https://fuaran.dev/wire-format/v1/binding-static-presence.schema.json"

/// The canonical schema, read from the EMITTER rather than the committed
/// artefact — this suite is a statement about what `SchemaGen` produces, and
/// the artefact's own staleness is `SchemaConformanceTests`' business.
///
/// `lazy`, and forced inside the test bodies, for the reason above: a failure
/// here must be a failing test, never an assembly that will not load.
let private schema =
    lazy
        (let text = SchemaGen.wireFormatSchema

         if not (text.Contains canonicalId) then
             failtestf
                 "the emitted schema no longer carries the `$id` this suite re-keys (%s) — re-key it to whatever it carries now, or this suite collides with SchemaConformanceTests in the global schema registry"
                 canonicalId

         JsonSchema.FromText(text.Replace(canonicalId, suiteLocalId)))

/// Evaluate a wire payload against the canonical schema. `None` ⇒ not parseable
/// JSON (which no probe here is, and the assertions say so rather than reading
/// it as a rejection).
let private schemaValid (wire: string) : bool option =
    let parsed =
        try
            Some(JsonDocument.Parse(wire, Corpus.wireJsonOptions))
        with _ ->
            None

    match parsed with
    | None -> None
    | Some doc ->
        use doc = doc
        Some((schema.Value.Evaluate(doc.RootElement, EvaluationOptions())).IsValid)

/// One probe: a minimal node whose named slot carries the valueless
/// `{"$type":"Static"}` form.
type private Probe =
    {
        Id: string
        /// The slot under test, as `Kind.field` — the subject of the claim.
        Slot: string
        /// The `$defs` name that slot `$ref`s, so a reader can go straight to it.
        Def: string
        /// The node with the slot's `Static` payload ABSENT.
        Wire: string
        /// The same node with the payload PRESENT. Asserted valid + decodable on
        /// every probe, so a refusal above is attributable to the absence and
        /// not to some other defect in the hand-built node.
        WireWithPayload: string
    }

/// Scalar slots: the schema refuses the valueless form and so does the decoder.
/// One probe per scalar element type — the four instantiations whose `Static`
/// payload parser has no reading for an absent value.
let private scalarSlotsRefuseValuelessStatic: Probe list =
    [ { Id = "metric-value-float"
        Slot = "Metric.value"
        Def = "Binding_float"
        Wire = """{"id":"n1","kind":{"$type":"Metric","label":"L","value":{"$type":"Static"}}}"""
        WireWithPayload = """{"id":"n1","kind":{"$type":"Metric","label":"L","value":{"$type":"Static","value":1.5}}}""" }
      { Id = "stepper-activestep-int"
        Slot = "Stepper.activeStep"
        Def = "Binding_int"
        Wire = """{"id":"n1","kind":{"$type":"Stepper","activeStep":{"$type":"Static"},"children":[]}}"""
        WireWithPayload =
          """{"id":"n1","kind":{"$type":"Stepper","activeStep":{"$type":"Static","value":0},"children":[]}}""" }
      { Id = "link-href-str"
        Slot = "Link.href"
        Def = "Binding_str"
        Wire = """{"id":"n1","kind":{"$type":"Link","download":false,"href":{"$type":"Static"},"label":"L"}}"""
        WireWithPayload =
          """{"id":"n1","kind":{"$type":"Link","download":false,"href":{"$type":"Static","value":"/x"},"label":"L"}}""" }
      { Id = "toast-open-bool"
        Slot = "Toast.open"
        Def = "Binding_bool"
        Wire = """{"id":"n1","kind":{"$type":"Toast","message":"M","open":{"$type":"Static"}}}"""
        WireWithPayload =
          """{"id":"n1","kind":{"$type":"Toast","message":"M","open":{"$type":"Static","value":true}}}""" } ]

/// Control slots: the schema admits the valueless form and the decoder ACCEPTS
/// it — absence is the binding there ("no selection"). All four are the slots
/// repointed onto `Binding_str_choice`; each entry is the go-red evidence for
/// its own repointing, which is why they are enumerated rather than sampled.
let private choiceSlotsAcceptValuelessStatic: Probe list =
    // Three of the four are only wire-legal inside a `Form` field list, so those
    // probes carry the smallest form that holds one. `form-segmented` and
    // `multiselect-1` in the corpus are the shipped instances of both shapes.
    let inForm (field: string) =
        sprintf
            """{"id":"n1","kind":{"$type":"Form","fields":[{"id":"f1","kind":%s,"label":"L","required":false}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":"Save"}}"""
            field

    let options = """{"$type":"Static","value":[{"label":"A","value":"a"}]}"""

    [ { Id = "choice-value-str"
        Slot = "Choice.value (in a Form field)"
        Def = "Binding_str_choice"
        Wire = inForm (sprintf """{"$type":"Choice","options":%s,"value":{"$type":"Static"}}""" options)
        WireWithPayload =
          inForm (sprintf """{"$type":"Choice","options":%s,"value":{"$type":"Static","value":"a"}}""" options) }
      { Id = "segmented-choice-value-str"
        Slot = "SegmentedChoice.value (in a Form field)"
        Def = "Binding_str_choice"
        Wire =
          inForm (
              sprintf
                  """{"$type":"SegmentedChoice","options":%s,"orientation":"Horizontal","value":{"$type":"Static"}}"""
                  options
          )
        WireWithPayload =
          inForm (
              sprintf
                  """{"$type":"SegmentedChoice","options":%s,"orientation":"Horizontal","value":{"$type":"Static","value":"a"}}"""
                  options
          ) }
      { Id = "combobox-value-str"
        Slot = "Combobox.value (in a Form field)"
        Def = "Binding_str_choice"
        Wire = inForm (sprintf """{"$type":"Combobox","options":%s,"value":{"$type":"Static"}}""" options)
        WireWithPayload =
          inForm (sprintf """{"$type":"Combobox","options":%s,"value":{"$type":"Static","value":"a"}}""" options) }
      { Id = "select-value-str"
        Slot = "Select.value"
        Def = "Binding_str_choice"
        Wire =
          sprintf """{"id":"n1","kind":{"$type":"Select","label":"L","source":%s,"value":{"$type":"Static"}}}""" options
        WireWithPayload =
          sprintf
              """{"id":"n1","kind":{"$type":"Select","label":"L","source":%s,"value":{"$type":"Static","value":"a"}}}"""
              options } ]

/// The instantiations whose payload stays OPTIONAL because their parser has a
/// reading for absence: the collections normalise it to the empty collection,
/// and the two any-JSON abstentions pass it through. One probe per family —
/// enough that "require every `Static.value`" cannot pass as a simplification.
let private absentablePayloadsStayLegal: Probe list =
    [ { Id = "select-source-options"
        Slot = "Select.source"
        Def = "Binding_list_SelectOption"
        // `Select.value` is REQUIRED by the spec record, so it is carried (in
        // its own valueless choice-slot form) rather than omitted — this probe
        // is about `source`, and a missing required sibling would refuse the
        // node for a reason that has nothing to do with the claim.
        Wire =
          """{"id":"n1","kind":{"$type":"Select","label":"L","source":{"$type":"Static"},"value":{"$type":"Static"}}}"""
        WireWithPayload =
          """{"id":"n1","kind":{"$type":"Select","label":"L","source":{"$type":"Static","value":[{"label":"A","value":"a"}]},"value":{"$type":"Static"}}}""" }
      { Id = "sparkline-source-floats"
        Slot = "Sparkline.source"
        Def = "Binding_list_float"
        Wire = """{"id":"n1","kind":{"$type":"Sparkline","source":{"$type":"Static"}}}"""
        WireWithPayload = """{"id":"n1","kind":{"$type":"Sparkline","source":{"$type":"Static","value":[1.0,2.0]}}}""" } ]

let private assertSchema (expected: bool) (wire: string) (id: string) (context: string) : unit =
    match schemaValid wire with
    | None -> failtestf "probe '%s' is not parseable JSON — fix the probe, it proves nothing\n  %s" id wire
    | Some actual when actual = expected -> ()
    | Some actual ->
        failtestf
            "probe '%s': the schema %s this payload; %s\n  %s"
            id
            (if actual then "ACCEPTS" else "REFUSES")
            context
            wire

/// The control assertion every probe carries: with its payload present the node
/// is schema-valid and decodable. A probe whose positive form fails is broken,
/// and its negative result proves nothing.
let private assertPayloadFormIsGood (p: Probe) : unit =
    assertSchema
        true
        p.WireWithPayload
        p.Id
        "but the SAME node with its payload PRESENT must be valid — this probe is malformed for some reason unrelated to presence"

    match JsonDecode.decodeNode p.WireWithPayload with
    | Ok _ -> ()
    | Error e ->
        failtestf
            "probe '%s': the decoder refuses %s even with its payload present (%s at %s) — this probe is malformed for some reason unrelated to presence\n  %s"
            p.Id
            p.Slot
            e.Code
            e.Path
            p.WireWithPayload

[<Tests>]
let staticPayloadPresence =
    testList
        "SchemaGen — the `Static` payload's presence, stated per instantiation"
        [ testList
              "scalar slots — the schema refuses the valueless `Static`, as the decoder does"
              (scalarSlotsRefuseValuelessStatic
               |> List.map (fun p ->
                   testCase (sprintf "%s (%s) — #/$defs/%s" p.Slot p.Id p.Def) (fun () ->
                       assertPayloadFormIsGood p

                       assertSchema
                           false
                           p.Wire
                           p.Id
                           (sprintf
                               "#/$defs/%s must REQUIRE its `Static` payload — the decoder refuses this node with WRONG_TYPE, and a schema that admits it sends every schema-driven emitter straight into that refusal"
                               p.Def)

                       match JsonDecode.decodeNode p.Wire with
                       | Ok _ ->
                           failtestf
                               "the decoder now ACCEPTS the valueless `Static` at %s — if that is deliberate, #/$defs/%s should stop requiring the payload and this entry moves to the absentable list\n  %s"
                               p.Slot
                               p.Def
                               p.Wire
                       | Error e ->
                           Expect.equal
                               e.Code
                               "WRONG_TYPE"
                               (sprintf
                                   "%s refuses the valueless `Static`, but not as a type failure — the pin describes the wrong mechanism"
                                   p.Slot))))

          testList
              "choice slots — schema-legal AND decoder-accepted (absence is the value)"
              (choiceSlotsAcceptValuelessStatic
               |> List.map (fun p ->
                   testCase (sprintf "%s (%s) — #/$defs/%s" p.Slot p.Id p.Def) (fun () ->
                       assertPayloadFormIsGood p

                       assertSchema
                           true
                           p.Wire
                           p.Id
                           (sprintf
                               "#/$defs/%s must keep its `Static` payload OPTIONAL — an absent payload is 'no selection' here, and four shipped accept fixtures carry exactly this shape"
                               p.Def)

                       match JsonDecode.decodeNode p.Wire with
                       | Ok _ -> ()
                       | Error e ->
                           failtestf
                               "%s no longer accepts the valueless `Static` (%s at %s) — if that is deliberate this slot belongs on the scalar list and should point at #/$defs/Binding_str; if it is not, a control slot just lost 'no selection'\n  %s"
                               p.Slot
                               e.Code
                               e.Path
                               p.Wire)))

          testList
              "absentable payloads — absence normalises rather than refuses"
              (absentablePayloadsStayLegal
               |> List.map (fun p ->
                   testCase (sprintf "%s (%s) — #/$defs/%s" p.Slot p.Id p.Def) (fun () ->
                       assertPayloadFormIsGood p

                       assertSchema
                           true
                           p.Wire
                           p.Id
                           (sprintf
                               "#/$defs/%s must keep its `Static` payload OPTIONAL — its parser reads absence as the empty collection, so requiring the key would refuse documents the decoder accepts"
                               p.Def)

                       match JsonDecode.decodeNode p.Wire with
                       | Ok _ -> ()
                       | Error e ->
                           failtestf
                               "%s no longer accepts the valueless `Static` (%s at %s) — the schema still admits it, so schema and decoder have come apart at #/$defs/%s\n  %s"
                               p.Slot
                               e.Code
                               e.Path
                               p.Def
                               p.Wire)))

          // Guard the guard: an empty list would pass while pinning nothing,
          // which is the failure mode every such pin is one edit away from.
          testCase "every list is non-empty" (fun () ->
              Expect.isNonEmpty
                  scalarSlotsRefuseValuelessStatic
                  "nothing pins that a scalar slot's schema requires its `Static` payload — the Phase 1068 residue could reopen silently"

              Expect.isNonEmpty
                  choiceSlotsAcceptValuelessStatic
                  "nothing pins the choice-value contract — without it, 'require every `Static.value`' reads as a simplification rather than a wire break"

              Expect.isNonEmpty
                  absentablePayloadsStayLegal
                  "nothing pins the absentable instantiations — the same over-tightening, one family across")

          // The two string lists must keep naming DIFFERENT `$def`s. That
          // separation IS the split: one definition serving both contracts is
          // the state Phase 1140 left, and re-merging them would silently
          // restore it in whichever direction the merge went.
          testCase "the scalar and choice lists name disjoint `$def`s" (fun () ->
              let scalarDefs = scalarSlotsRefuseValuelessStatic |> List.map _.Def |> Set.ofList
              let choiceDefs = choiceSlotsAcceptValuelessStatic |> List.map _.Def |> Set.ofList

              Expect.isEmpty
                  (Set.intersect scalarDefs choiceDefs)
                  "a `$def` is shared between a payload-refusing scalar slot and a payload-accepting choice slot — one definition cannot state both contracts, which is the finding Phase 1140 closed by splitting `Binding_str`"

              Expect.isTrue
                  (choiceDefs |> Set.forall (fun d -> d <> "Binding_str"))
                  "a choice slot is back on #/$defs/Binding_str — that definition requires its payload, so the repointing has been undone") ]
