module Fuaran.UI.Tests.AuthoringControlTests

// ============================================================================
//  Phase 2055 — one form-field constructor per control, with the handler
//  optional, and one `Box` builder under the convenience constructors.
//
//  Each `<control>Control` member must build exactly the case its two older
//  curried helpers built: with `onChange` left out it is the handler-free
//  `*Declarative` shape, and with it it is the handler-bearing shape. The
//  expected values below are written as the DIRECT union-case construction
//  the helpers used before they became shims, so a member that drifts from
//  the case it names fails here rather than in a fixture. Bytes are compared
//  through the canonical encoder, which renders every closure as
//  `"<closure>"`, so "has a handler" and "has none" encode differently.
// ============================================================================

#nowarn "44" // Action.Dispatch carries the IDL's inProcessOnly marking (Phase 1152).

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.OpStream.Abstractions

type private Msg = | Changed

let private encode (kind: FormFieldKind<Msg>) : string =
    Fuaran.form
        "form"
        { Defaults.form with
            Fields =
                [ { Defaults.formField with
                      Id = "field"
                      Kind = kind } ] }
    |> CanonicalJson.encodeNode

let private same (label: string) (actual: FormFieldKind<Msg>) (expected: FormFieldKind<Msg>) =
    test label { Expect.equal (encode actual) (encode expected) label }

let private dispatch _ = Action.Dispatch Changed

let private text = Binding.State("t", Some "")
let private number = Binding.State("n", Some 0.0)
let private flag = Binding.State("b", Some false)
let private options = Binding.Static(Some [])
let private tokens = Binding.State("k", Some [])

let private range =
    Binding.State("r", Some Defaults.ControlValueDefaults.dateTimeRange)

[<Tests>]
let tests =
    testList
        "AuthoringControl"
        [ testList
              "a member without onChange is the handler-free case"
              [ same "text" (FormFieldKind.textControl (text)) (FormFieldKind.Text(Some(FieldValue.ofText text), None))
                same
                    "number"
                    (FormFieldKind.numberControl (number))
                    (FormFieldKind.Number(Some(FieldValue.ofNumber number), None))
                same
                    "checkbox"
                    (FormFieldKind.checkboxControl (flag))
                    (FormFieldKind.Checkbox(Some(FieldValue.ofBool flag), None))
                same
                    "toggle"
                    (FormFieldKind.toggleControl (flag))
                    (FormFieldKind.Toggle(Some(FieldValue.ofBool flag), None))
                same
                    "choice"
                    (FormFieldKind.choiceControl (options, text))
                    (FormFieldKind.Choice(options, Some(FieldValue.ofText text), None))
                same
                    "textArea"
                    (FormFieldKind.textAreaControl (text, 4))
                    (FormFieldKind.TextArea(Some(FieldValue.ofText text), None, 4))
                same
                    "rangedNumber"
                    (FormFieldKind.rangedNumberControl (number, min = 1.0, step = 0.5))
                    (FormFieldKind.RangedNumber(Some(FieldValue.ofNumber number), None, Some 1.0, None, Some 0.5))
                same
                    "segmentedChoice defaults to Horizontal"
                    (FormFieldKind.segmentedChoiceControl (options, text))
                    (FormFieldKind.SegmentedChoice(options, Some(FieldValue.ofText text), None, Orientation.Horizontal))
                same
                    "combobox defaults to closed"
                    (FormFieldKind.comboboxControl (options, text))
                    (FormFieldKind.Combobox(false, None, options, Some(FieldValue.ofText text)))
                same
                    "rating defaults to Defaults.ratingMax, whole positions"
                    (FormFieldKind.ratingControl (number))
                    (FormFieldKind.Rating(false, Defaults.ratingMax, None, Some(FieldValue.ofNumber number)))
                same
                    "color"
                    (FormFieldKind.colorControl (text))
                    (FormFieldKind.Color(None, Some(FieldValue.ofText text)))
                same
                    "tokens defaults to free text, no suggestions"
                    (FormFieldKind.tokensControl (tokens))
                    (FormFieldKind.Tokens(true, None, None, Some(FieldValue.ofTokens tokens)))
                same
                    "dateTime"
                    (FormFieldKind.dateTimeControl (text, DateTimeVariant.Date, max = "2030-01-01"))
                    (FormFieldKind.DateTime(
                        Some(FieldValue.ofText text),
                        None,
                        DateTimeVariant.Date,
                        None,
                        Some "2030-01-01",
                        None
                    ))
                same
                    "dateTimeRange"
                    (FormFieldKind.dateTimeRangeControl (range, DateTimeVariant.Date))
                    (FormFieldKind.DateTimeRange(
                        Some(FieldValue.ofDateRange range),
                        None,
                        DateTimeVariant.Date,
                        None,
                        None,
                        None
                    )) ]
          testList
              "a member with onChange carries the one handler slot (Phase 2177)"
              [ same
                    "text"
                    (FormFieldKind.textControl (text, dispatch))
                    (FormFieldKind.Text(Some(FieldValue.ofText text), Some(FieldChange.ofText dispatch)))
                same
                    "checkbox carries onChange, not onToggle"
                    (FormFieldKind.checkboxControl (flag, dispatch))
                    (FormFieldKind.Checkbox(Some(FieldValue.ofBool flag), Some(FieldChange.ofBool dispatch)))
                same
                    "toggle carries onChange, not onToggle"
                    (FormFieldKind.toggleControl (flag, onChange = dispatch))
                    (FormFieldKind.Toggle(Some(FieldValue.ofBool flag), Some(FieldChange.ofBool dispatch)))
                same
                    "segmentedChoice, vertical"
                    (FormFieldKind.segmentedChoiceControl (options, text, dispatch, Orientation.Vertical))
                    (FormFieldKind.SegmentedChoice(
                        options,
                        Some(FieldValue.ofText text),
                        Some(FieldChange.ofChoice dispatch),
                        Orientation.Vertical
                    )) ]
          testList
              "the curried helpers are shims over the members"
              [ same
                    "rangedNumber = rangedNumberControl with onChange"
                    (FormFieldKind.rangedNumber number dispatch (Some 0.0) (Some 9.0) None)
                    (FormFieldKind.rangedNumberControl (number, dispatch, min = 0.0, max = 9.0))
                same
                    "numberStepped = rangedNumberControl with step only"
                    (FormFieldKind.numberStepped number dispatch 0.5)
                    (FormFieldKind.rangedNumberControl (number, dispatch, step = 0.5))
                same
                    "rangedNumberDeclarative = rangedNumberControl without onChange"
                    (FormFieldKind.rangedNumberDeclarative number None (Some 9.0) None)
                    (FormFieldKind.rangedNumberControl (number, max = 9.0))
                same
                    "tokens = tokensControl"
                    (FormFieldKind.tokens tokens dispatch (Some options) false)
                    (FormFieldKind.tokensControl (tokens, dispatch, options, false))
                same
                    "dateTime maps a cleared value through the same adapter"
                    (FormFieldKind.dateTime text dispatch DateTimeVariant.Time None None (Some 60.0))
                    (FormFieldKind.dateTimeControl (text, DateTimeVariant.Time, dispatch, step = 60.0)) ]
          testList
              "the convenience constructors lower through one Box builder"
              [ test "every convenience Box starts from Defaults.box" {
                    let boxOf (n: Node<Msg>) =
                        match n.Kind with
                        | NodeKind.Box spec -> spec
                        | other -> failwithf "expected a Box, got %A" other

                    let seeded =
                        [ Fuaran.dashboard "d" Defaults.dashboard
                          Fuaran.stack "s" Defaults.stack
                          Fuaran.gridLayout "g" Defaults.gridLayout
                          Fuaran.gridLayoutTemplated "gt" "1fr 2fr" Defaults.gridLayout
                          Fuaran.masonryLayout "m" Defaults.masonryLayout
                          Fuaran.card "c" Defaults.card
                          Fuaran.divider "hr" ]
                        |> List.map boxOf

                    for spec in seeded do
                        Expect.equal spec.KeepTogether Defaults.box<Msg>.KeepTogether "KeepTogether from the seed"
                        Expect.equal spec.BreakBefore Defaults.box<Msg>.BreakBefore "BreakBefore from the seed"
                }
                test "each convenience is the direct Box form its obsoletion names, plus its accessibility default" {
                    let direct (id: string) (spec: BoxSpec<Msg>) (accessibility: Accessibility option) : Node<Msg> =
                        { Fuaran.box id spec with
                            Accessibility = accessibility }

                    let pairs: (string * Node<Msg> * Node<Msg>) list =
                        [ "dashboard",
                          Fuaran.dashboard "d" Defaults.dashboard,
                          direct
                              "d"
                              { Defaults.box with
                                  Layout = BoxLayout.Auto
                                  Role = BoxRole.Dashboard }
                              Defaults.Accessibility.dashboard
                          "card",
                          Fuaran.card
                              "c"
                              { Defaults.card with
                                  Heading = Some(TextSource.Literal "H") },
                          direct
                              "c"
                              { Defaults.box with
                                  Role = BoxRole.Card
                                  Heading = Some(TextSource.Literal "H") }
                              Defaults.Accessibility.card
                          "stack", Fuaran.stack "s" Defaults.stack, direct "s" Defaults.box Defaults.Accessibility.none ]

                    for (name, convenience, boxed) in pairs do
                        Expect.equal (CanonicalJson.encodeNode convenience) (CanonicalJson.encodeNode boxed) name
                }
                test "gridLayoutTemplated is gridLayout over the templated spec" {
                    let templated: Node<Msg> =
                        Fuaran.gridLayoutTemplated "g" "1fr 2fr" Defaults.gridLayout

                    let direct: Node<Msg> =
                        Fuaran.gridLayout
                            "g"
                            { Defaults.gridLayout with
                                TemplateColumns = Some "1fr 2fr" }

                    Expect.equal (CanonicalJson.encodeNode templated) (CanonicalJson.encodeNode direct) "same bytes"
                } ] ]
