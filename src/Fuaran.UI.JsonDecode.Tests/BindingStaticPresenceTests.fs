module Fuaran.UI.JsonDecode.Tests.BindingStaticPresence

// ============================================================================
//  The published schema admits a VALUELESS `Static` where the decoder refuses
//  one — the residue Phase 1068 left, pinned inversely (Phase 1140).
//
//  Phase 1068 typed every `Binding` slot's payload: ten instantiations
//  (`Binding_float`, `Binding_int`, `Binding_str`, `Binding_bool`, the typed
//  collections, and two named any-JSON abstentions), each slot `$ref`ing its
//  own. What it did NOT type is the payload's PRESENCE. `bindingDef` emits the
//  `Static` arm with `value` OPTIONAL — Phase 677's "absence is structural" —
//  so `{"$type":"Static"}` validates against `Binding_float` exactly as it
//  validates against `Binding_json`, while the decoder routes the missing key
//  through the slot's own parser as `JNull` and a scalar parser refuses it.
//
//  That is not a cosmetic gap. A schema-driven emitter that fills a kind's
//  REQUIRED fields and nothing else — the shape every such walker takes,
//  because required-ness is the only signal the dialect gives it — emits the
//  valueless form at every scalar `Binding` slot, and the decoder then refuses
//  the node. Nine display/input kinds are unsynthesisable for exactly this
//  reason, and no care on the emitting side recovers a presence rule the
//  schema does not state.
//
//  ── Why the obvious fix is wrong, which is the point of the second list ────
//
//  "Make `value` required in the scalar instantiations" would break the wire.
//  The schema points `Choice.value`, `SegmentedChoice.value` and `Select.value`
//  at the SAME `#/$defs/Binding_str` as `TextArea.value`, `Link.href` and
//  `Image.src` — but the decoder splits them. The control slots go through the
//  choice-value path, where an absent `Static` payload is first-class ("no
//  selection", the typed `Static None`); the rest go through the scalar string
//  path, which refuses it. One `$def`, two contracts. Closing the gap therefore
//  means a DISTINCT option-payload instantiation for the control slots, not a
//  required-key edit to `Binding_str` — and three shipped accept-fixture
//  families (`controls-*`, `form-segmented`, `multiselect-1`) are what a
//  required-key edit would start refusing.
//
//  ── The shape of the pin ──────────────────────────────────────────────────
//
//  Both lists are asserted in the direction that is TRUE TODAY and FALSE once
//  the schema is fixed properly, so neither can outlive its reason:
//
//    * `schemaAdmitsValuelessStatic` — schema VALID, decoder ERROR. The gap
//      itself. When the schema learns to refuse one of these, this test fails
//      and the entry is deleted deliberately rather than the exemption going
//      quiet. Same device as Phase 1064's `schemaTypeErasedBindingRejects`,
//      which is what made 1068 land.
//    * `controlSlotAcceptsValuelessStatic` — schema VALID, decoder OK. The
//      converse, and the reason the first list cannot be closed by tightening
//      `Binding_str`. It fails if a change makes the control slots strict.
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
        Wire: string
    }

/// Scalar slots: the schema admits the valueless form, the decoder refuses it.
/// One probe per scalar element type — the four instantiations whose `Static`
/// payload parser has no reading for an absent value.
let private schemaAdmitsValuelessStatic: Probe list =
    [ { Id = "metric-value-float"
        Slot = "Metric.value"
        Def = "Binding_float"
        Wire = """{"id":"n1","kind":{"$type":"Metric","label":"L","value":{"$type":"Static"}}}""" }
      { Id = "stepper-activestep-int"
        Slot = "Stepper.activeStep"
        Def = "Binding_int"
        Wire = """{"id":"n1","kind":{"$type":"Stepper","activeStep":{"$type":"Static"},"children":[]}}""" }
      { Id = "link-href-str"
        Slot = "Link.href"
        Def = "Binding_str"
        Wire = """{"id":"n1","kind":{"$type":"Link","download":false,"href":{"$type":"Static"},"label":"L"}}""" }
      { Id = "toast-open-bool"
        Slot = "Toast.open"
        Def = "Binding_bool"
        Wire = """{"id":"n1","kind":{"$type":"Toast","message":"M","open":{"$type":"Static"}}}""" } ]

/// Control slots: the schema admits the valueless form and the decoder ACCEPTS
/// it — absence is the binding there ("no selection"). Note `Def` is the same
/// `Binding_str` two entries above refuse it under; that collision is the
/// finding.
let private controlSlotAcceptsValuelessStatic: Probe list =
    // A control kind is only wire-legal inside a `Form`/`Filters` field list, so
    // the probe carries the smallest form that holds one. `form-segmented` in
    // the corpus is the shipped instance of exactly this shape.
    [ { Id = "segmented-choice-value-str"
        Slot = "SegmentedChoice.value (in a Form field)"
        Def = "Binding_str"
        Wire =
          """{"id":"n1","kind":{"$type":"Form","fields":[{"id":"f1","kind":{"$type":"SegmentedChoice","options":{"$type":"Static","value":[]},"orientation":"Horizontal","value":{"$type":"Static"}},"label":"L","required":false}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":"Save"}}""" } ]

let private assertSchemaAdmits (p: Probe) : unit =
    match schemaValid p.Wire with
    | None -> failtestf "probe '%s' is not parseable JSON — fix the probe, it proves nothing\n  %s" p.Id p.Wire
    | Some false ->
        failtestf
            "the schema now REFUSES the valueless `Static` at %s (#/$defs/%s) — delete '%s' from this list; the exemption has outlived its reason\n  %s"
            p.Slot
            p.Def
            p.Id
            p.Wire
    | Some true -> ()

[<Tests>]
let valuelessStaticGap =
    testList
        "SchemaGen — the valueless `Static` the schema admits and the decoder refuses"
        [ testList
              "scalar slots — schema-legal, decoder-refused (INVERSE pin)"
              (schemaAdmitsValuelessStatic
               |> List.map (fun p ->
                   testCase (sprintf "%s (%s) — #/$defs/%s" p.Slot p.Id p.Def) (fun () ->
                       assertSchemaAdmits p

                       match JsonDecode.decodeNode p.Wire with
                       | Ok _ ->
                           failtestf
                               "the decoder now ACCEPTS the valueless `Static` at %s — the asymmetry this pin records is gone; delete '%s'\n  %s"
                               p.Slot
                               p.Id
                               p.Wire
                       | Error e ->
                           Expect.equal
                               e.Code
                               "WRONG_TYPE"
                               (sprintf
                                   "%s refuses the valueless `Static`, but not as a type failure — the pin describes the wrong mechanism"
                                   p.Slot))))

          testList
              "control slots — schema-legal AND decoder-accepted (the converse pin)"
              (controlSlotAcceptsValuelessStatic
               |> List.map (fun p ->
                   testCase (sprintf "%s (%s) — #/$defs/%s" p.Slot p.Id p.Def) (fun () ->
                       assertSchemaAdmits p

                       match JsonDecode.decodeNode p.Wire with
                       | Ok _ -> ()
                       | Error e ->
                           failtestf
                               "%s no longer accepts the valueless `Static` (%s at %s) — if that is deliberate, the scalar list above can be closed by tightening #/$defs/%s and this entry goes; if it is not, a control slot just lost 'no selection'\n  %s"
                               p.Slot
                               e.Code
                               e.Path
                               p.Def
                               p.Wire)))

          // Guard the guard: an empty list would pass while pinning nothing,
          // which is the failure mode every inverse pin is one edit away from.
          testCase "both lists are non-empty" (fun () ->
              Expect.isNonEmpty
                  schemaAdmitsValuelessStatic
                  "the scalar-slot inverse pin has no probes left — if the gap closed, delete this suite with the reason; do not leave it passing vacuously"

              Expect.isNonEmpty
                  controlSlotAcceptsValuelessStatic
                  "the control-slot converse pin has no probes left — without it nothing records why the scalar list cannot be closed by a required-key edit")

          // The two lists must keep naming a SHARED `$def`, because that
          // collision is the whole finding: one definition, two decoder
          // contracts. If they ever stop overlapping, the split has happened
          // (or the slots were re-pointed) and this suite's premise is spent.
          testCase "the two lists still collide on at least one `$def`" (fun () ->
              let scalarDefs = schemaAdmitsValuelessStatic |> List.map _.Def |> Set.ofList
              let controlDefs = controlSlotAcceptsValuelessStatic |> List.map _.Def |> Set.ofList

              Expect.isNonEmpty
                  (Set.intersect scalarDefs controlDefs)
                  "no `$def` is now shared between a refusing scalar slot and an accepting control slot — the option-payload split this suite says is needed may already have landed; re-read both lists rather than widening them") ]
