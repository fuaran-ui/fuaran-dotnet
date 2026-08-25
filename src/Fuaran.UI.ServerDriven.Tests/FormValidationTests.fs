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
    let s = n.Id
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
              let session = init (DriverServices.createPermissive stubRender) update view 0

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
              let session = init (DriverServices.createPermissive stubRender) update view 0

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
              let session = init (DriverServices.createPermissive stubRender) update view 0

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

              let session = init (DriverServices.createPermissive stubRender) update view 0

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
                  LiveConnection("c1", init (DriverServices.createPermissive stubRender) update view 0, channel)

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
                  LiveConnection("c2", init (DriverServices.createPermissive stubRender) update view 0, channel)
              // No validator — a submit with a missing required field still mutates,
              // exactly as before Phase 156 (validation is strictly opt-in).
              channel.Send(submitEv "c2" [ "age", LiveValue.Num 30.0 ])
              Expect.equal conn.Session.Model 1 "unvalidated submit mutates (152 behaviour preserved)"
          } ]

// ─── Phase 864: declared field rules (`FormField.Rule`) ──────────────────────
//
// WIRE_FORMAT gives this tier the non-bypassable half of the obligation: a
// declared rule "is not a security boundary" and client enforcement "is an
// affordance, not a gate", so a host that accepts submissions re-checks every
// declared constraint server-side. Every slot below therefore has a go-red
// partner asserting the SAME rule passes when the value satisfies it — a check
// that only ever fires is indistinguishable from one that always fires.

/// A rule constraining nothing — the base every case below narrows. (A `rule`
/// with every constraint slot absent is a DECODE error per WIRE_FORMAT; this is
/// a test fixture assembled in memory, not a decoded tree, so it never reaches
/// that gate — it exists only so each test names the one slot it is about.)
let private noRule: FieldRule =
    { Compare = None
      Format = None
      MaxLength = None
      Message = None
      MinLength = None
      Pattern = None }

/// A two-field form: `value` carries the rule under test, `other` is the
/// unconstrained sibling a `compare` rule reads by its `Binding.State` key.
let private ruleForm (rule: FieldRule) : FormSpec<Msg> =
    { Defaults.form<Msg> with
        OnSubmit = Action.Dispatch Submit
        Fields =
            [ { Defaults.formField<Msg> with
                  Id = "value"
                  Label = TextSource.Literal "Value"
                  Rule = Some rule }
              { Defaults.formField<Msg> with
                  Id = "other"
                  Label = TextSource.Literal "Other" } ] }

let private ruleSubmission (rule: FieldRule) (values: (string * LiveValue) list) : FormSubmission<Msg> =
    { FormNodeId = "f"
      Form = ruleForm rule
      Values = Map.ofList values }

let private unmetMessages (rule: FieldRule) (values: (string * LiveValue) list) : string list =
    enforceDeclared (ruleSubmission rule values) |> List.map (fun e -> e.Message)

[<Tests>]
let fieldRuleTests =
    testList
        "Declared field rules (Phase 864)"
        [ // ── minLength ───────────────────────────────────────────────────────
          test "minLength below the bound is unmet" {
              Expect.equal
                  (unmetMessages { noRule with MinLength = Some 5 } [ "value", LiveValue.Str "abc" ])
                  [ "Must be at least 5 characters." ]
                  "a 3-character value under a minLength of 5"
          }

          test "minLength AT the bound passes (go-red partner)" {
              Expect.isEmpty
                  (unmetMessages { noRule with MinLength = Some 5 } [ "value", LiveValue.Str "abcde" ])
                  "exactly 5 characters satisfies a minLength of 5 — the bound is inclusive"
          }

          // ── maxLength ───────────────────────────────────────────────────────
          test "maxLength above the bound is unmet" {
              Expect.equal
                  (unmetMessages { noRule with MaxLength = Some 3 } [ "value", LiveValue.Str "abcd" ])
                  [ "Must be at most 3 characters." ]
                  "a 4-character value over a maxLength of 3"
          }

          test "maxLength AT the bound passes (go-red partner)" {
              Expect.isEmpty
                  (unmetMessages { noRule with MaxLength = Some 3 } [ "value", LiveValue.Str "abc" ])
                  "exactly 3 characters satisfies a maxLength of 3"
          }

          // ── format ──────────────────────────────────────────────────────────
          test "format email rejects a value with no @" {
              Expect.equal
                  (unmetMessages
                      { noRule with
                          Format = Some TextFormat.Email }
                      [ "value", LiveValue.Str "ada" ])
                  [ "Enter an email address." ]
                  "a bare word is not the shape `<input type=email>` accepts"
          }

          test "format email accepts an ordinary address (go-red partner)" {
              Expect.isEmpty
                  (unmetMessages
                      { noRule with
                          Format = Some TextFormat.Email }
                      [ "value", LiveValue.Str "ada@example.com" ])
                  "one @, something either side, no whitespace"
          }

          test "format url rejects a scheme-less value" {
              Expect.equal
                  (unmetMessages
                      { noRule with
                          Format = Some TextFormat.Url }
                      [ "value", LiveValue.Str "example.com" ])
                  [ "Enter a URL." ]
                  "`<input type=url>` demands an ABSOLUTE URL"
          }

          test "format url accepts an absolute URL (go-red partner)" {
              Expect.isEmpty
                  (unmetMessages
                      { noRule with
                          Format = Some TextFormat.Url }
                      [ "value", LiveValue.Str "https://example.com/x" ])
                  "a scheme and a body"
          }

          test "format tel accepts anything non-empty (the native check enforces nothing)" {
              // Deliberate: `<input type=tel>` has no native constraint, because
              // telephone formats vary worldwide. Inventing one here would make
              // this floor refuse submissions the client tier accepts.
              Expect.isEmpty
                  (unmetMessages
                      { noRule with
                          Format = Some TextFormat.Tel }
                      [ "value", LiveValue.Str "(0) 123-456" ])
                  "no shape is claimed for tel"
          }

          // ── pattern ─────────────────────────────────────────────────────────
          test "pattern rejects a non-matching value" {
              Expect.equal
                  (unmetMessages
                      { noRule with
                          Pattern = Some "[0-9]{3}" }
                      [ "value", LiveValue.Str "12" ])
                  [ "This value is not in the required format." ]
                  "two digits do not match a three-digit pattern"
          }

          test "pattern accepts a matching value (go-red partner)" {
              Expect.isEmpty
                  (unmetMessages
                      { noRule with
                          Pattern = Some "[0-9]{3}" }
                      [ "value", LiveValue.Str "123" ])
                  "three digits match"
          }

          test "pattern is anchored to the WHOLE value (HTML `pattern` semantics)" {
              // The specification chose HTML `pattern` semantics — implicitly
              // anchored — so the browser, a static projection and this re-check
              // agree without a second definition. An unanchored .NET regex would
              // find "123" inside "x123x" and pass.
              Expect.equal
                  (unmetMessages
                      { noRule with
                          Pattern = Some "[0-9]{3}" }
                      [ "value", LiveValue.Str "x123x" ])
                  [ "This value is not in the required format." ]
                  "a substring match is not a whole-value match"
          }

          test "a top-level alternation cannot escape the anchoring" {
              // `a|b` wrapped as `\A(?:a|b)\z` — not `\Aa|b\z`, which would make
              // the second branch unanchored and admit any value ending in "b".
              Expect.equal
                  (unmetMessages { noRule with Pattern = Some "a|b" } [ "value", LiveValue.Str "zzb" ])
                  [ "This value is not in the required format." ]
                  "the author's source is wrapped, so the alternation stays inside the anchors"
          }

          test "a MALFORMED pattern is met, not unmet (deliberate)" {
              // A rule that is not a regex constrains nothing. Turning an
              // author's typo into a form nobody can submit is worse than the
              // slot not binding; refusing a malformed pattern is a decode-time
              // job, not this tier's.
              Expect.isEmpty
                  (unmetMessages { noRule with Pattern = Some "(" } [ "value", LiveValue.Str "anything" ])
                  "an unparseable pattern does not block the submission"
          }

          // ── compare ─────────────────────────────────────────────────────────
          test "compare lt is unmet when the value is not below its sibling" {
              let rule =
                  { noRule with
                      Compare =
                          Some
                              { Against = Binding.State("other", None)
                                Op = CompareOp.Lt } }

              Expect.equal
                  (unmetMessages rule [ "value", LiveValue.Num 10.0; "other", LiveValue.Num 5.0 ])
                  [ "This value does not match the field it is compared against." ]
                  "10 is not < 5"
          }

          test "compare lt passes when the value IS below its sibling (go-red partner)" {
              let rule =
                  { noRule with
                      Compare =
                          Some
                              { Against = Binding.State("other", None)
                                Op = CompareOp.Lt } }

              Expect.isEmpty (unmetMessages rule [ "value", LiveValue.Num 3.0; "other", LiveValue.Num 5.0 ]) "3 < 5"
          }

          test "compare over same-variant ISO-8601 strings is an ordinal compare" {
              // WIRE_FORMAT borrows the `DateRange` ordering wholesale: same-
              // variant ISO-8601 strings compare lexicographically in
              // chronological order — no parsing, no locale.
              let rule =
                  { noRule with
                      Compare =
                          Some
                              { Against = Binding.State("other", None)
                                Op = CompareOp.Gt } }

              Expect.isEmpty
                  (unmetMessages rule [ "value", LiveValue.Str "2026-08-25"; "other", LiveValue.Str "2026-01-01" ])
                  "a later date sorts after an earlier one under an ordinal compare"

              Expect.equal
                  (unmetMessages rule [ "value", LiveValue.Str "2026-01-01"; "other", LiveValue.Str "2026-08-25" ])
                  [ "This value does not match the field it is compared against." ]
                  "and an earlier one does not"
          }

          test "compare between values of DIFFERENT SHAPES is unmet, not an error" {
              // The specification is explicit: a half-filled form is a normal
              // state, so a shape mismatch resolves to UNMET rather than
              // throwing. Note the direction that makes for a trust floor — a
              // mismatch a hostile client manufactures refuses the submission.
              let rule =
                  { noRule with
                      Compare =
                          Some
                              { Against = Binding.State("other", None)
                                Op = CompareOp.Eq } }

              Expect.equal
                  (unmetMessages rule [ "value", LiveValue.Str "5"; "other", LiveValue.Num 5.0 ])
                  [ "This value does not match the field it is compared against." ]
                  "a string and a number are not comparable"
          }

          test "compare with a MISSING sibling value is unmet" {
              let rule =
                  { noRule with
                      Compare =
                          Some
                              { Against = Binding.State("other", None)
                                Op = CompareOp.Eq } }

              Expect.equal
                  (unmetMessages rule [ "value", LiveValue.Str "x" ])
                  [ "This value does not match the field it is compared against." ]
                  "an operand that was not submitted cannot satisfy the comparison"
          }

          test "compare against a non-State binding is NOT evaluated (recorded known limit)" {
              // Only a `State`-keyed operand names something inside the
              // submission, and the submission is all this tier holds. Refusing
              // over an operand nobody read would claim a check that was not
              // performed; a host that can resolve it enforces it in its own
              // `FormValidator`, which composes ON TOP of this floor.
              let rule =
                  { noRule with
                      Compare =
                          Some
                              { Against = Binding.Static None
                                Op = CompareOp.Eq } }

              Expect.isEmpty
                  (unmetMessages rule [ "value", LiveValue.Str "x" ])
                  "an unreadable operand does not block the submission"
          }

          // ── message + emptiness ─────────────────────────────────────────────
          test "an authored Literal message replaces the generated sentence" {
              let rule =
                  { noRule with
                      MinLength = Some 5
                      Message = Some(TextSource.Literal "Names are at least five letters.") }

              Expect.equal
                  (unmetMessages rule [ "value", LiveValue.Str "abc" ])
                  [ "Names are at least five letters." ]
                  "the authored prose wins over the generated sentence"
          }

          test "a non-Literal message falls back to the generated sentence (the seam)" {
              // `FieldRule.Message` is a `TextSource`; resolving `Bound` / `I18n`
              // needs a render context, and this tier has none by design. The
              // fallback is a real message rather than a rendered key.
              let rule =
                  { noRule with
                      MinLength = Some 5
                      Message = Some(TextSource.I18n("field.tooShort", Map.empty)) }

              Expect.equal
                  (unmetMessages rule [ "value", LiveValue.Str "abc" ])
                  [ "Must be at least 5 characters." ]
                  "an unresolvable TextSource does not become an empty or key-shaped message"
          }

          test "an EMPTY value is not length-, format- or pattern-checked" {
              // Exactly what the browser does with `minlength` / `type=email` /
              // `pattern` on a control the author did not mark required. This
              // floor must refuse the same submissions the projected attributes
              // do, not more — `required` is the slot that makes emptiness fail.
              let rule =
                  { noRule with
                      MinLength = Some 5
                      Format = Some TextFormat.Email
                      Pattern = Some "[0-9]{3}" }

              Expect.isEmpty
                  (unmetMessages rule [ "value", LiveValue.Str "" ])
                  "an empty optional field is not constrained by its rule's shape slots"
          }

          test "a field with no rule at all is unaffected" {
              Expect.isEmpty
                  (enforceDeclared
                      { FormNodeId = "f"
                        Form =
                          { Defaults.form<Msg> with
                              Fields =
                                  [ { Defaults.formField<Msg> with
                                        Id = "value"
                                        Label = TextSource.Literal "Value" } ] }
                        Values = Map.ofList [ "value", LiveValue.Str "anything" ] }
                   |> List.map (fun e -> e.Message))
                  "a pre-864 form validates exactly as it did"
          }

          // ── the trust floor, end to end ─────────────────────────────────────
          test "a rule violation is non-bypassable even when the host validator passes" {
              let permissiveHost: FormValidator<Msg> = fun _ -> []

              let ruledView (m: Model) : Node<Msg> =
                  Fuaran.dashboard
                      "root"
                      { Defaults.dashboard<Msg> with
                          Children =
                              [ Fuaran.form "f" (ruleForm { noRule with MinLength = Some 5 })
                                Fuaran.markdown "status" (string m) ] }

              let session = init (DriverServices.createPermissive stubRender) update ruledView 0

              let s2, out =
                  stepWithValidation permissiveHost session (submitEv "c" [ "value", LiveValue.Str "abc" ])

              Expect.equal s2.Model 0 "the declared rule suppressed the mutation despite the permissive host"

              Expect.contains
                  out.Patches
                  (DomPatch.SetAttr("value", "data-fuaran-field-error", "Must be at least 5 characters."))
                  "the unmet rule is surfaced on the offending field"
          } ]
