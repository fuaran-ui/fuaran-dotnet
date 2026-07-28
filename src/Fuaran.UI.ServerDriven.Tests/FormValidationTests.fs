module Fuaran.UI.ServerDriven.Tests.FormValidationTests

// ─── Phase 156 (Wave 18): runtime form validation — validate→error-patch ──────
//
// A server-driven form submit runs a server-side validator over the submitted
// values BEFORE any mutation. Declared constraints (Required / RangedNumber
// bounds) are enforced server-side regardless of the host (the trust floor,
// composing with 152 G1); the host adds business rules on top. Field errors
// lower to closure-free `data-fuaran-field-error` DomPatches; an invalid submit
// suppresses the state mutation.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.ServerDriven.Driver
open Fuaran.UI.ServerDriven.FormValidation

type Msg = Submit

type Model = int

let private update Submit (m: Model) : Model = m + 1

// A form: a required "name" + a 0..120 ranged "age". The submit dispatches Submit.
let private formSpec: FormSpec<Msg> =
    { Defaults.form<Msg> with
        OnSubmit = Action.Dispatch Submit
        Fields =
            [ { Defaults.formField<Msg> with
                  Id = "name"
                  Label = TextSource.Literal "Name"
                  Required = true }
              { Defaults.formField<Msg> with
                  Id = "age"
                  Label = TextSource.Literal "Age"
                  Kind =
                      FormFieldKind.rangedNumber
                          (Binding.Static(Some 0.0))
                          (fun _ -> Action.Dispatch Submit)
                          (Some 0.0)
                          (Some 120.0)
                          None } ] }

let private view (m: Model) : Node<Msg> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<Msg> with
            Children = [ Fuaran.form "f" formSpec; Fuaran.markdown "status" (string m) ] }

let private stubRender (n: Node<Msg>) : string =
    let (NodeId s) = n.Id
    $"<f id='{s}'/>"

let private submission (values: (string * LiveValue) list) : FormSubmission<Msg> =
    { FormNodeId = "f"
      Form = formSpec
      Values = Map.ofList values }

let private submitEv connId (values: (string * LiveValue) list) : LiveEvent =
    { ConnId = connId
      NodeId = "f"
      Event = "submit"
      Payload = Map.ofList values
      LastSeq = 0 }

[<Tests>]
let tests =
    testList
        "Form validation (Phase 156)"
        [ test "enforceDeclared flags a missing required field" {
              let errors = enforceDeclared (submission [ "age", LiveValue.Num 30.0 ])

              Expect.equal
                  errors
                  [ { FieldId = "name"
                      Message = "This field is required." } ]
                  "the required 'name' field is missing"
          }

          test "enforceDeclared flags a RangedNumber above its max" {
              let errors =
                  enforceDeclared (submission [ "name", LiveValue.Str "Ada"; "age", LiveValue.Num 200.0 ])

              Expect.equal
                  errors
                  [ { FieldId = "age"
                      Message = "Must be at most 120." } ]
                  "age 200 exceeds the declared max of 120"
          }

          test "enforceDeclared flags a RangedNumber below its min" {
              let errors =
                  enforceDeclared (submission [ "name", LiveValue.Str "Ada"; "age", LiveValue.Num -5.0 ])

              Expect.equal
                  errors
                  [ { FieldId = "age"
                      Message = "Must be at least 0." } ]
                  "age -5 is below the declared min of 0"
          }

          test "enforceDeclared accepts a valid submission" {
              let errors =
                  enforceDeclared (submission [ "name", LiveValue.Str "Ada"; "age", LiveValue.Num 30.0 ])

              Expect.isEmpty errors "name present + age in range → no declared errors"
          }

          test "enforceDeclared range-checks a numeric value that arrives as a string (HTML5 bypass)" {
              // A hostile client posts the number as a string to dodge the spinner
              // bounds — the server still range-checks it.
              let errors =
                  enforceDeclared (submission [ "name", LiveValue.Str "Ada"; "age", LiveValue.Str "999" ])

              Expect.equal (List.map (fun e -> e.FieldId) errors) [ "age" ] "string-encoded 999 still rejected"
          }

          test "lower emits a set for the erroring field and a clear for the rest" {
              let patches =
                  lower
                      formSpec
                      [ { FieldId = "name"
                          Message = "Required." } ]

              Expect.equal
                  patches
                  [ DomPatch.SetAttr("name", "data-fuaran-field-error", "Required.")
                    DomPatch.RemoveAttr("age", "data-fuaran-field-error") ]
                  "erroring field marked; valid field cleared (idempotent full state)"
          }

          test "lower with no errors clears every field" {
              let patches = lower formSpec []

              Expect.equal
                  patches
                  [ DomPatch.RemoveAttr("name", "data-fuaran-field-error")
                    DomPatch.RemoveAttr("age", "data-fuaran-field-error") ]
                  "a now-valid form drops all stale field errors"
          }

          test "stepWithValidation suppresses the mutation on an invalid submit" {
              let session = init (DriverServices.create stubRender) update view 0

              let s2, out =
                  stepWithValidation declaredOnly session (submitEv "c" [ "age", LiveValue.Num 30.0 ])

              Expect.equal s2.Model 0 "state mutation suppressed — model unchanged"
              Expect.isNone out.Rejected "a failed business validation is not a G1 boundary breach"

              Expect.contains
                  out.Patches
                  (DomPatch.SetAttr("name", "data-fuaran-field-error", "This field is required."))
                  "the missing-required field error is surfaced inline"
          }

          test "stepWithValidation applies the mutation + clears errors on a valid submit" {
              let session = init (DriverServices.create stubRender) update view 0

              let s2, out =
                  stepWithValidation
                      declaredOnly
                      session
                      (submitEv "c" [ "name", LiveValue.Str "Ada"; "age", LiveValue.Num 30.0 ])

              Expect.equal s2.Model 1 "valid submit applied the mutation"

              Expect.contains
                  out.Patches
                  (DomPatch.RemoveAttr("name", "data-fuaran-field-error"))
                  "field-error attributes cleared on a valid submit"
          }

          test "declared constraints are enforced even when the host validator passes (trust floor)" {
              // The host says everything is fine, but the declared 0..120 bound is
              // non-bypassable — it runs first regardless.
              let permissiveHost: FormValidator<Msg> = fun _ -> []
              let session = init (DriverServices.create stubRender) update view 0

              let s2, out =
                  stepWithValidation
                      permissiveHost
                      session
                      (submitEv "c" [ "name", LiveValue.Str "Ada"; "age", LiveValue.Num 200.0 ])

              Expect.equal s2.Model 0 "out-of-range submit rejected despite the permissive host"

              Expect.contains
                  out.Patches
                  (DomPatch.SetAttr("age", "data-fuaran-field-error", "Must be at most 120."))
                  "the declared-bound error is surfaced"
          }

          test "a host business rule composes on top of declared enforcement" {
              // Reject the name "admin" as a business rule; declared still runs too.
              let host: FormValidator<Msg> =
                  fun sub ->
                      match Map.tryFind "name" sub.Values with
                      | Some(LiveValue.Str "admin") ->
                          [ { FieldId = "name"
                              Message = "Reserved name." } ]
                      | _ -> []

              let session = init (DriverServices.create stubRender) update view 0

              let s2, out =
                  stepWithValidation
                      host
                      session
                      (submitEv "c" [ "name", LiveValue.Str "admin"; "age", LiveValue.Num 30.0 ])

              Expect.equal s2.Model 0 "the business-rule rejection suppressed the mutation"

              Expect.contains
                  out.Patches
                  (DomPatch.SetAttr("name", "data-fuaran-field-error", "Reserved name."))
                  "the host business-rule error is surfaced"
          }

          test "LiveConnection.EnableFormValidation gates submits through the channel" {
              let channel = InMemoryChannel()

              let conn =
                  LiveConnection("c1", init (DriverServices.create stubRender) update view 0, channel)

              conn.EnableFormValidation declaredOnly

              // Invalid submit — missing the required name.
              channel.Send(submitEv "c1" [ "age", LiveValue.Num 30.0 ])
              Expect.equal conn.Session.Model 0 "invalid submit did not mutate"

              match channel.Pushed with
              | [ frame ] ->
                  Expect.contains
                      frame.Patches
                      (DomPatch.SetAttr("name", "data-fuaran-field-error", "This field is required."))
                      "a field-error frame was pushed"
              | other -> failtestf "expected one field-error frame, got %A" other

              // Valid submit — mutation applies.
              channel.Send(submitEv "c1" [ "name", LiveValue.Str "Ada"; "age", LiveValue.Num 30.0 ])
              Expect.equal conn.Session.Model 1 "valid submit applied"
          }

          test "a connection without EnableFormValidation submits as the 152 path (AC: no behaviour change)" {
              let channel = InMemoryChannel()

              let conn =
                  LiveConnection("c2", init (DriverServices.create stubRender) update view 0, channel)
              // No validator — a submit with a missing required field still mutates,
              // exactly as before Phase 156 (validation is strictly opt-in).
              channel.Send(submitEv "c2" [ "age", LiveValue.Num 30.0 ])
              Expect.equal conn.Session.Model 1 "unvalidated submit mutates (152 behaviour preserved)"
          } ]
