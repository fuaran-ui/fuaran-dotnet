module Fuaran.UI.Renderer.Server.Tests.InputVisPlanServerTests

// ============================================================================
//  Phase 2290 — the input and visualisation kinds move onto the element plan.
//
//  (1) The attachment points each planned input / visualisation kind names
//  are pinned, so a handler dropped from a plan is red rather than a silent
//  no-op on the client. (2) The served markup the rulings moved (R2–R18,
//  STABILITY 0.98.0) is pinned byte for byte, and stays inert.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server

let private contains (needle: string) (haystack: string) =
    haystack.Contains(needle, System.StringComparison.Ordinal)

let private inert (html: string) =
    Expect.isFalse
        (System.Text.RegularExpressions.Regex.IsMatch(html, " on[a-z]+=\""))
        "the server emits no handler attribute"

let private md (id: string) (text: string) : Node<obj> = Fuaran.markdown id text

let private withKind (id: string) (kind: NodeKind<obj>) : Node<obj> = { md id "" with Kind = kind }

let private pc: ElementPlan.PlanContext<obj, string> =
    { Sources = BindingResolver.empty
      Csp = Csp.CspMode.Permissive
      Collector = None
      Egress = Sanitize.permissiveEgress
      NodeId = "n"
      Child = fun child -> ElementPlan.Plan.TextNode child.Id }

let private lit (s: string) = TextSource.Literal s

let private textField (id: string) (value: string) (required: bool) : FormField<obj> =
    { Defaults.formField with
        Id = id
        Label = lit id
        Required = required
        Kind = FormFieldKind.Text(Some(FieldValue.ofText (Binding.Static(Some value))), None) }

let private options: Binding<SelectOption list> =
    Binding.Static(Some [ { Value = "a"; Label = "A" }; { Value = "b"; Label = "B" } ])

let private servedForm (fields: FormField<obj> list) : string =
    Render.renderStatic (Fuaran.form "f" { Defaults.form with Fields = fields })

/// The reconciliation keys of every planned element whose class is `cls`, in
/// document order — the repeated children's identities (Phase 2290 moved
/// these arms' keys from the client's `prop.key` sites into the plan).
let rec private keysOf (cls: string) (p: ElementPlan.Plan<string>) : string option list =
    match p with
    | ElementPlan.Plan.Element spec ->
        let mine =
            spec.Props
            |> List.exists (function
                | ElementPlan.Prop.Attr("class", c) -> c.Split(' ') |> Array.contains cls
                | _ -> false)

        (if mine then [ spec.Key ] else [])
        @ (match spec.Content with
           | ElementPlan.Content.Children cs -> cs |> List.collect (keysOf cls)
           | _ -> [])
    | _ -> []

[<Tests>]
let tests =
    testList
        "Phase 2290 — the input and visualisation kinds are element plans"
        [ testList
              "the behaviour attachment points each planned kind declares"
              [ test "Button: one click point" {
                    let plan: ElementPlan.Plan<string> =
                        ElementPlan.InputPlan.button BindingResolver.empty Defaults.button []

                    Expect.equal (ElementPlan.Attach.points plan) [ ElementPlan.Attach.ButtonClick, "" ] "click"
                }
                test "Select: one change point" {
                    let plan: ElementPlan.Plan<string> =
                        ElementPlan.InputPlan.select BindingResolver.empty Defaults.select

                    Expect.equal (ElementPlan.Attach.points plan) [ ElementPlan.Attach.SelectChange, "" ] "change"
                }
                test "Form: the submit point, fields are the walk's" {
                    let plan: ElementPlan.Plan<string> = ElementPlan.InputPlan.form pc Defaults.form []
                    Expect.equal (ElementPlan.Attach.points plan) [ ElementPlan.Attach.FormSubmit, "" ] "submit"
                }
                test "a plain field control: one change point; a range: one per end" {
                    let text: ElementPlan.Plan<string> =
                        ElementPlan.InputPlan.fieldControl BindingResolver.empty (textField "t" "" false)

                    Expect.equal (ElementPlan.Attach.points text) [ ElementPlan.Attach.FieldChange, "" ] "text"

                    let range: ElementPlan.Plan<string> =
                        ElementPlan.InputPlan.fieldControl
                            BindingResolver.empty
                            { textField "r" "" false with
                                Kind =
                                    FormFieldKind.Range(
                                        Some(FieldValue.ofRange (Binding.Static(Some { Min = 1.0; Max = 2.0 }))),
                                        None,
                                        None,
                                        None,
                                        None
                                    ) }

                    Expect.equal
                        (ElementPlan.Attach.points range)
                        [ ElementPlan.Attach.FieldChange, "min"; ElementPlan.Attach.FieldChange, "max" ]
                        "range"
                }
                test "SegmentedChoice: the group's keyboard walk and one select point per option" {
                    let plan: ElementPlan.Plan<string> =
                        ElementPlan.InputPlan.segmentedChoice
                            BindingResolver.empty
                            "s"
                            "data-fuaran-field"
                            None
                            options
                            (Binding.Static(Some "a"))
                            Orientation.Horizontal

                    Expect.equal
                        (ElementPlan.Attach.points plan)
                        [ ElementPlan.Attach.SegmentKeyDown, ""
                          ElementPlan.Attach.SegmentSelect, "a"
                          ElementPlan.Attach.SegmentSelect, "b" ]
                        "horizontal"

                    let vertical: ElementPlan.Plan<string> =
                        ElementPlan.InputPlan.segmentedChoice
                            BindingResolver.empty
                            "s"
                            "data-fuaran-field"
                            None
                            options
                            (Binding.Static(Some "a"))
                            Orientation.Vertical

                    Expect.equal
                        (ElementPlan.Attach.points vertical)
                        [ ElementPlan.Attach.SegmentRadio, "a"; ElementPlan.Attach.SegmentRadio, "b" ]
                        "vertical"
                }
                test "FileUpload: one selection point" {
                    let plan: ElementPlan.Plan<string> =
                        ElementPlan.InputPlan.fileUpload BindingResolver.empty Defaults.fileUpload

                    Expect.equal (ElementPlan.Attach.points plan) [ ElementPlan.Attach.FileSelect, "" ] "select"
                }
                test "Tree: a keyboard point and a label-click point per row, keyed by item id" {
                    let spec =
                        { Defaults.tree with
                            Items =
                                [ { Defaults.treeItem with
                                      Id = "r0"
                                      Label = lit "Row" } ] }

                    let plan: ElementPlan.Plan<string> =
                        ElementPlan.VisPlan.tree
                            BindingResolver.empty
                            spec
                            (ElementPlan.VisPlan.treeState BindingResolver.empty spec)

                    Expect.equal
                        (ElementPlan.Attach.points plan)
                        [ ElementPlan.Attach.TreeKeyDown, "r0"
                          ElementPlan.Attach.TreeLabelClick, "r0" ]
                        "row"
                }
                test "a lowered chart: the delegated point click" {
                    Expect.equal
                        (ElementPlan.Attach.points (ElementPlan.VisPlan.loweredChart "<svg/>": ElementPlan.Plan<string>))
                        [ ElementPlan.Attach.ChartPointClick, "" ]
                        "click"
                }
                test "Map: one click point per marker" {
                    let spec =
                        { Defaults.map with
                            Source =
                                Binding.Static(
                                    Some
                                        [ { Label = "L"
                                            Latitude = 1.0
                                            Longitude = 2.0 } ]
                                ) }

                    Expect.equal
                        (ElementPlan.Attach.points (ElementPlan.VisPlan.map pc None spec))
                        [ ElementPlan.Attach.MarkerClick, "0" ]
                        "marker"
                } ]
          testList
              "every repeated child the plans emit states its identity"
              [ test "form field, segmented option and row, select options, filter chip" {
                    let field = textField "name" "" false

                    Expect.equal
                        (keysOf
                            Css.Class.FormField
                            (ElementPlan.InputPlan.formField BindingResolver.empty field ElementPlan.Plan.Nothing))
                        [ Some "name" ]
                        "a form field is keyed by its id"

                    let seg orientation : ElementPlan.Plan<string> =
                        ElementPlan.InputPlan.segmentedChoice
                            BindingResolver.empty
                            "s"
                            "data-fuaran-field"
                            None
                            options
                            (Binding.Static(Some "a"))
                            orientation

                    Expect.equal
                        (keysOf Css.Class.SegmentedOption (seg Orientation.Horizontal))
                        [ Some "a"; Some "b" ]
                        "options by value"

                    Expect.equal
                        (keysOf Css.Class.SegmentedRow (seg Orientation.Vertical))
                        [ Some "a"; Some "b" ]
                        "rows by value"

                    let select: ElementPlan.Plan<string> =
                        ElementPlan.InputPlan.select
                            BindingResolver.empty
                            { Defaults.select with
                                Source = options }

                    let rec optionKeys (p: ElementPlan.Plan<string>) =
                        match p with
                        | ElementPlan.Plan.Element spec ->
                            (if spec.Tag = "option" then [ spec.Key ] else [])
                            @ (match spec.Content with
                               | ElementPlan.Content.Children cs -> cs |> List.collect optionKeys
                               | _ -> [])
                        | _ -> []

                    Expect.equal (optionKeys select) [ Some "a"; Some "b" ] "select options by value"

                    let chip: FilterSpec<obj> =
                        { Defaults.filter with
                            Name = "region"
                            Label = lit "Region" }

                    Expect.equal
                        (keysOf
                            "fuaran-filter"
                            (ElementPlan.InputPlan.filter BindingResolver.empty chip ElementPlan.Plan.Nothing))
                        [ Some "region" ]
                        "a filter chip is keyed by its name"
                }
                test "tree item, static-table header / row / cell, map marker" {
                    let spec =
                        { Defaults.tree with
                            Items =
                                [ { Defaults.treeItem with
                                      Id = "r0"
                                      Label = lit "Row" } ] }

                    Expect.equal
                        (keysOf
                            Css.Class.TreeItem
                            (ElementPlan.VisPlan.tree
                                BindingResolver.empty
                                spec
                                (ElementPlan.VisPlan.treeState BindingResolver.empty spec)))
                        [ Some(ElementPlan.reconciliationKey (Some "r0") 0) ]
                        "tree rows by item id"

                    let grid: ElementPlan.Plan<string> =
                        ElementPlan.VisPlan.staticGrid
                            BindingResolver.empty
                            "t"
                            Defaults.dataGrid
                            { Headers = [ lit "H" ]
                              Rows = [ [ lit "c" ]; [ lit "d" ] ]
                              Sortable = None
                              DefaultSort = None }

                    let positional n =
                        [ for i in 0 .. n - 1 -> Some(ElementPlan.reconciliationKey None i) ]

                    Expect.equal (keysOf Css.Class.TableHeader grid) (positional 1) "headers by position"
                    Expect.equal (keysOf Css.Class.TableRow grid) (positional 2) "rows by position"
                    Expect.equal (keysOf Css.Class.TableCell grid) (positional 1 @ positional 1) "cells by position"

                    let map: ElementPlan.Plan<string> =
                        ElementPlan.VisPlan.map
                            pc
                            None
                            { Defaults.map with
                                Source =
                                    Binding.Static(
                                        Some
                                            [ { Label = "L"
                                                Latitude = 1.0
                                                Longitude = 2.0 } ]
                                    ) }

                    Expect.equal
                        (keysOf "fuaran-map-marker" map)
                        [ Some(ElementPlan.reconciliationKey (Some "L") 0) ]
                        "markers by label"
                } ]
          testList
              "the served markup the rulings set"
              [ test "R15 — an optional field serves no `required` attribute at all; a required one serves it" {
                    let html = servedForm [ textField "opt" "" false; textField "req" "" true ]
                    Expect.isFalse (contains "required=\"false\"" html) "no false presence attribute"

                    Expect.isTrue
                        (contains
                            "<input class=\"fuaran-form-input\" id=\"opt\" data-fuaran-field=\"opt\" type=\"text\" value=\"\">"
                            html)
                        html

                    Expect.isTrue
                        (contains
                            "<input class=\"fuaran-form-input\" id=\"req\" data-fuaran-field=\"req\" type=\"text\" required=\"true\" value=\"\">"
                            html)
                        html
                }
                test "R16 — a textarea serves its bound text as escaped content, never a value attribute" {
                    let html =
                        servedForm
                            [ { textField "notes" "" false with
                                  Kind =
                                      FormFieldKind.TextArea(
                                          Some(FieldValue.ofText (Binding.Static(Some "a</textarea>&b"))),
                                          None,
                                          3
                                      ) } ]

                    Expect.isTrue
                        (contains
                            "<textarea class=\"fuaran-form-textarea\" id=\"notes\" data-fuaran-field=\"notes\" rows=\"3\">a&lt;/textarea&gt;&amp;b</textarea>"
                            html)
                        html

                    Expect.isFalse (contains "value=" html) "no value attribute on the textarea"
                }
                test "R4 — the field wrapper is the label-for shape, the control carries id and the shim marker" {
                    let html = servedForm [ textField "name" "Ada" true ]

                    Expect.isTrue
                        (contains
                            "<div class=\"fuaran-form-field\"><label class=\"fuaran-form-label\" for=\"name\">name *</label><input class=\"fuaran-form-input\" id=\"name\" data-fuaran-field=\"name\" type=\"text\" required=\"true\" value=\"Ada\"></div>"
                            html)
                        html

                    inert html
                }
                test "R2 — a single select marks its chosen option, never a value attribute on the select" {
                    let html =
                        Render.renderStatic (
                            Fuaran.select
                                "s"
                                { Defaults.select with
                                    Source = options
                                    Value = Some(Binding.Static(Some "b")) }
                        )

                    Expect.isTrue
                        (contains
                            "<select class=\"fuaran-select-control\"><option value=\"a\">A</option><option value=\"b\" selected=\"selected\">B</option></select>"
                            html)
                        html
                }
                test "R3 — a multi-select serves every chosen option and no placeholder" {
                    let html =
                        Render.renderStatic (
                            Fuaran.select
                                "s"
                                { Defaults.select with
                                    Source = options
                                    Value = None
                                    Placeholder = Some(lit "pick")
                                    Multiple = Some true
                                    Values = Some(Binding.Static(Some [ "a"; "b" ])) }
                        )

                    Expect.isTrue
                        (contains
                            "<select class=\"fuaran-select-control\" multiple=\"\"><option value=\"a\" selected=\"selected\">A</option><option value=\"b\" selected=\"selected\">B</option></select>"
                            html)
                        html
                }
                test "R1 — data-fuaran-commit is served; the unwired cue is not" {
                    let html =
                        Render.renderStatic (
                            Fuaran.button
                                "b"
                                { Defaults.button with
                                    Label = lit "Apply"
                                    OnClick = Action.CommitLocal "q" }
                        )

                    Expect.isTrue (contains "data-fuaran-commit=\"q\"" html) html

                    let unwired =
                        Render.renderStatic (
                            Fuaran.button
                                "b"
                                { Defaults.button with
                                    Label = lit "Go"
                                    OnClick = Action.SetState("k", Some(Fuaran.Core.JStr "v"), None) }
                        )

                    Expect.isFalse (contains "unwired" unwired) unwired
                    Expect.isFalse (contains "title=" unwired) unwired
                }
                test "R13 — a FileUpload serves multiple and disabled" {
                    let html =
                        Render.renderStatic (
                            Fuaran.fileUpload
                                "u"
                                { Defaults.fileUpload with
                                    Label = lit "Files"
                                    Multiple = true
                                    Disabled = Some(Binding.Static(Some true)) }
                        )

                    Expect.isTrue
                        (contains
                            "<input class=\"fuaran-file-upload-input\" type=\"file\" multiple=\"\" disabled=\"true\">"
                            html)
                        html
                }
                test "R10 — an exportable static grid serves the inert export button" {
                    let html =
                        Render.renderStatic (
                            withKind
                                "g"
                                (NodeKind.DataGrid
                                    { Defaults.dataGrid with
                                        Exportable = true
                                        StaticRows =
                                            Some
                                                { Headers = [ lit "H" ]
                                                  Rows = [ [ lit "c" ]; [ lit "d" ] ]
                                                  Sortable = None
                                                  DefaultSort = None } })
                        )

                    Expect.isTrue
                        (contains
                            "</table><button class=\"fuaran-grid-export\" type=\"button\" aria-label=\"Export 2 rows as CSV\" title=\"Export 2 rows as CSV\">Export CSV</button></div>"
                            html)
                        html

                    Expect.isTrue (contains "<div class=\"fuaran-grid-exportable\"><table" html) html
                    inert html
                }
                test "R12 — a Map serves its marker list" {
                    let html =
                        Render.renderStatic (
                            Fuaran.map
                                "m"
                                { Defaults.map with
                                    Source =
                                        Binding.Static(
                                            Some
                                                [ { Label = "London"
                                                    Latitude = 51.5
                                                    Longitude = -0.12 } ]
                                        ) }
                        )

                    Expect.isTrue
                        (contains
                            "[Map: 1 markers — hydrates client-side]<ul class=\"fuaran-map-marker-list\"><li class=\"fuaran-map-marker\">London @ (51.5000, -0.1200)</li></ul>"
                            html)
                        html
                }
                test "R18 — a vertical segmented choice labels its radios with `for`" {
                    let html =
                        servedForm
                            [ { textField "tier" "" false with
                                  Kind =
                                      FormFieldKind.SegmentedChoice(
                                          options,
                                          Some(FieldValue.ofText (Binding.Static(Some "a"))),
                                          None,
                                          Orientation.Vertical
                                      ) } ]

                    Expect.isTrue (contains "<label for=\"tier-opt-0\">A</label>" html) html
                    Expect.isFalse (contains "htmlFor" html) html
                }
                test "R17 — no reconciliation key leaks into the served rating" {
                    let html =
                        servedForm
                            [ { textField "score" "" false with
                                  Kind =
                                      FormFieldKind.Rating(
                                          false,
                                          5,
                                          None,
                                          Some(FieldValue.ofNumber (Binding.Static(Some 3.0)))
                                      ) } ]

                    Expect.isFalse (contains " key=" html) html
                    Expect.isTrue (contains "fuaran-rating-star" html) html
                } ] ]
