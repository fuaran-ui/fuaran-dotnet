module Fuaran.UI.ServerDriven.FormValidation

open Fuaran.UI.Types
open Fuaran.UI.Ops.Introspect
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.ServerDriven.Driver

// ============================================================================
//  Runtime form validation — validate → error-patch round-trip (Phase 156).
//
//  Fuaran's `Fuaran.UI.Validator` is BUILD-TIME (an F# AST walker). The forms /
//  wizard / configurator app class — a server-driven sweet spot — needs a
//  *runtime* per-field feedback loop: a user submits values, the server validates
//  them against business rules, and field-level errors come back to the form.
//
//  This module is that round-trip on the server-driven path. On a form `submit`
//  (or a per-field commit) the driver runs a server-side validator over the
//  submitted values; a non-empty `FieldError list` lowers to closure-free
//  `DomPatch`es that mark the offending fields (`data-fuaran-field-error` +
//  message) and the state mutation is SUPPRESSED — no full re-render, the rest of
//  the form keeps its values + focus. The validation logic lives server-side
//  (where the rules + data are) and is never shipped to the browser.
//
//  ── Two layers, declared is non-bypassable ─────────────────────────────────
//  `enforceDeclared` re-checks the build-time-declared constraints (`Required`,
//  `RangedNumber` Min/Max) SERVER-SIDE at runtime — the build-time validator
//  cannot catch a hostile client bypassing HTML5 validation, so "client
//  validation is not a trust boundary" is closed here, composing with the 152 G1
//  inbound gate. The host `FormValidator` adds business rules ON TOP; the driver
//  integration always runs declared enforcement first, so the trust floor holds
//  regardless of what the host supplies.
//
//  ── Per-field vs whole-form ────────────────────────────────────────────────
//  `validate` operates on a `Map<fieldId, LiveValue>`: one entry for a per-field
//  commit (validate-on-commit, as the user leaves a field), every field for a
//  whole-form submit (validate-on-submit). Both lower to the SAME field-error
//  patch shape, so the shim handles them identically.
//
//  Pure + Fable-clean: `FieldError` / `enforceDeclared` / `lower` are functions
//  of `(form, values)` → data, so they unit-test headlessly and carry no
//  transport / renderer dependency (the driver step wrapper reuses the shipped
//  `Driver.step`).
// ============================================================================

/// A per-field validation failure: the field to mark + the message to show.
type FieldError = { FieldId: string; Message: string }

/// The submitted form: the form node id, its spec, and the submitted field
/// values off the wire (keyed by field id). `Values` carries one entry on a
/// per-field commit, every field on a whole-form submit.
type FormSubmission<'Msg> =
    { FormNodeId: string
      Form: FormSpec<'Msg>
      Values: Map<string, LiveValue> }

/// Host-supplied business-rule validator. Runs server-side (where the rules +
/// data live); never shipped to the browser. Returns the field-level failures
/// (empty = the submission is valid as far as the host is concerned).
type FormValidator<'Msg> = FormSubmission<'Msg> -> FieldError list

// ─── declared-constraint enforcement (the trust-boundary floor) ───────────────

let private isEmptyValue (v: LiveValue option) : bool =
    match v with
    | None
    | Some LiveValue.Null
    | Some(LiveValue.Str "") -> true
    | _ -> false

let private asNumber (v: LiveValue option) : float option =
    match v with
    | Some(LiveValue.Num n) -> Some n
    | Some(LiveValue.Str s) ->
        match System.Double.TryParse s with
        | true, n -> Some n
        | _ -> None
    | _ -> None

let private checkField (values: Map<string, LiveValue>) (field: FormField<'Msg>) : FieldError option =
    let value = Map.tryFind field.Id values

    if field.Required && isEmptyValue value then
        Some
            { FieldId = field.Id
              Message = "This field is required." }
    else
        match field.Kind with
        | FormFieldKind.RangedNumber(_, _, constraints) ->
            match asNumber value with
            | Some n ->
                match constraints.Min, constraints.Max with
                | Some lo, _ when n < lo ->
                    Some
                        { FieldId = field.Id
                          Message = sprintf "Must be at least %g." lo }
                | _, Some hi when n > hi ->
                    Some
                        { FieldId = field.Id
                          Message = sprintf "Must be at most %g." hi }
                | _ -> None
            // Non-numeric / absent value on a non-required field: `Required`
            // already handled the empty case; nothing further to range-check.
            | None -> None
        | _ -> None

/// Re-check the build-time-declared constraints (`Required`, `RangedNumber`
/// Min/Max) over the submitted values, SERVER-SIDE. The trust floor: a client
/// bypassing HTML5 validation is still rejected. Composes with G1 (152).
let enforceDeclared (submission: FormSubmission<'Msg>) : FieldError list =
    submission.Form.Fields |> List.choose (checkField submission.Values)

/// Combine declared enforcement (first) with a host business-rule validator.
/// The result is the validator the driver integration runs.
let combine (host: FormValidator<'Msg>) : FormValidator<'Msg> =
    fun submission -> enforceDeclared submission @ host submission

/// The no-op host validator — declared enforcement only.
let declaredOnly<'Msg> : FormValidator<'Msg> = fun _ -> []

// ─── field-error lowering ─────────────────────────────────────────────────────

/// Lower the field-error state to closure-free `DomPatch`es. Emits the FULL
/// state for every field of the form: an erroring field gets
/// `data-fuaran-field-error="<message>"`; every other field has the attribute
/// REMOVED — so the lowering is idempotent + self-correcting (a field that just
/// became valid drops its stale error without a separate clear pass). The host
/// CSS keys off the attribute to surface the message inline.
let lower (form: FormSpec<'Msg>) (errors: FieldError list) : DomPatch list =
    let errorByField = errors |> List.map (fun e -> e.FieldId, e.Message) |> Map.ofList

    [ for field in form.Fields ->
          match Map.tryFind field.Id errorByField with
          | Some message -> DomPatch.SetAttr(field.Id, "data-fuaran-field-error", message)
          | None -> DomPatch.RemoveAttr(field.Id, "data-fuaran-field-error") ]

// ─── driver integration (whole-form submit) ──────────────────────────────────

/// Step a session with form-aware validation. On a form `submit` event the
/// declared constraints + the host `validator` run over the submitted values
/// (`ev.Payload`, keyed by field id) BEFORE any mutation:
///   • invalid → the session is UNCHANGED, the output carries only the
///     field-error patches (mutation suppressed; `Rejected = None` — this is a
///     legitimate, gated submit that failed business validation, not a G1
///     boundary breach).
///   • valid → the field-error attributes are cleared and the normal
///     `Driver.step` runs (interpret → update → diff → lower).
/// Every non-form event delegates straight to `Driver.step`.
let stepWithValidation
    (validator: FormValidator<'Msg>)
    (session: LiveSession<'Model, 'Msg>)
    (ev: LiveEvent)
    : LiveSession<'Model, 'Msg> * StepOutput =
    match findNode (NodeId ev.NodeId) session.Tree with
    | Some node ->
        match node.Kind with
        | NodeKind.Form( form) when ev.Event = "submit" ->
            let submission =
                { FormNodeId = ev.NodeId
                  Form = form
                  Values = ev.Payload }

            // Declared enforcement is non-bypassable — always first.
            let errors = enforceDeclared submission @ validator submission

            match errors with
            | [] ->
                let s2, out = step session ev

                s2,
                { out with
                    Patches = lower form [] @ out.Patches }
            | _ ->
                session,
                { Patches = lower form errors
                  Effects = []
                  Rejected = None }
        | _ -> step session ev
    | None -> step session ev
