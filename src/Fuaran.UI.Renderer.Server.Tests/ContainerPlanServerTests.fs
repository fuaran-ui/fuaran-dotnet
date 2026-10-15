module Fuaran.UI.Renderer.Server.Tests.ContainerPlanServerTests

// ============================================================================
//  Phase 2278 — the remaining kinds move onto the element plan.
//
//  Two properties per moved family. (1) The behaviour attachment field: a
//  handler-bearing kind's plan NAMES its attachment points, and the set each
//  kind declares is pinned here, so a handler dropped from a plan is a red
//  test rather than a silent no-op on the client. (2) The server emits the
//  same markup INERT: the served bytes are the pre-plan bytes, attribute order
//  included, and carry no handler attribute.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server

let private contains (needle: string) (haystack: string) =
    haystack.Contains(needle, System.StringComparison.Ordinal)

/// No `on*=` attribute anywhere in the served HTML.
let private inert (html: string) =
    Expect.isFalse
        (System.Text.RegularExpressions.Regex.IsMatch(html, " on[a-z]+=\""))
        "the server emits no handler attribute"

let private md (id: string) (text: string) : Node<obj> = Fuaran.markdown id text

/// A plan context whose child is a marker text node — enough for the points.
let private pc: ElementPlan.PlanContext<obj, string> =
    { Sources = BindingResolver.empty
      Csp = Csp.CspMode.Permissive
      Collector = None
      NodeId = "n"
      Child = fun child -> ElementPlan.Plan.TextNode child.Id }

let private noop: Action<obj> = Action.Chain []

let private tabsSpec: TabsSpec<obj> =
    { Defaults.tabs with
        Children = [ md "c0" "zero"; md "c1" "one" ]
        TabHeaders =
            Some
                [ { Defaults.tabHeader with
                      Label = TextSource.Literal "A"
                      Icon = Some "star" }
                  { Defaults.tabHeader with
                      Label = TextSource.Literal "B"
                      Disabled = Some(Binding.Static(Some true)) } ] }

let private stepperSpec (onSelect: bool) : StepperSpec<obj> =
    { Defaults.stepper with
        Children = [ md "s0" "zero"; md "s1" "one" ]
        OnSelect = (if onSelect then Some(fun _ -> noop) else None) }

let private modalSpec (modality: ModalityKind) : ModalSpec<obj> =
    { Defaults.modal with
        Children = [ md "m0" "body" ]
        Modality = modality
        Anchor = (if modality = ModalityKind.Popover then Some "a1" else None) }

let private disclosureSpec: DisclosureSpec<obj> =
    { Defaults.disclosure with
        Heading = TextSource.Literal "More"
        Open = Binding.Static(Some true)
        Children = [ md "d0" "body" ] }

[<Tests>]
let tests =
    testList
        "Phase 2278 — the container kinds are element plans"
        [ testList
              "the behaviour attachment points each planned kind declares"
              [ test "Tabs: the tab bar's keyboard model, and one select point per ENABLED tab" {
                    let model = ElementPlan.TabsModel.ofSpec BindingResolver.empty tabsSpec

                    Expect.equal
                        (ElementPlan.Attach.points (ElementPlan.LayoutPlan.tabs pc tabsSpec model))
                        [ ElementPlan.Attach.TabsKeyDown, ""; ElementPlan.Attach.TabSelect, "0" ]
                        "a disabled tab attaches nothing"
                }
                test "Disclosure: the toggle" {
                    Expect.equal
                        (ElementPlan.Attach.points (ElementPlan.LayoutPlan.disclosure pc disclosureSpec))
                        [ ElementPlan.Attach.DisclosureToggle, "" ]
                        "one toggle point"
                }
                test "Stepper: one select point per step, only when OnSelect is declared" {
                    Expect.equal
                        (ElementPlan.Attach.points (ElementPlan.LayoutPlan.stepper pc (stepperSpec true)))
                        [ ElementPlan.Attach.StepSelect, "0"; ElementPlan.Attach.StepSelect, "1" ]
                        "every step"

                    Expect.isEmpty
                        (ElementPlan.Attach.points (ElementPlan.LayoutPlan.stepper pc (stepperSpec false)))
                        "no OnSelect, no point"
                }
                test "Modal: overlay and dismiss button; Popover: the dismiss button" {
                    Expect.equal
                        (ElementPlan.Attach.points (ElementPlan.LayoutPlan.modal pc (modalSpec ModalityKind.Modal)))
                        [ ElementPlan.Attach.ModalOverlay, ""; ElementPlan.Attach.ModalDismiss, "" ]
                        "modal"

                    Expect.equal
                        (ElementPlan.Attach.points (ElementPlan.LayoutPlan.modal pc (modalSpec ModalityKind.Popover)))
                        [ ElementPlan.Attach.ModalDismiss, "" ]
                        "popover"

                    Expect.isEmpty
                        (ElementPlan.Attach.points (
                            ElementPlan.LayoutPlan.modal
                                pc
                                { modalSpec ModalityKind.Modal with
                                    Dismissable = false }
                        ))
                        "an undismissable modal attaches nothing"
                }
                test "repeated children carry their reconciliation keys in the plan" {
                    let keysOf (plan: ElementPlan.Plan<string>) =
                        let rec walk p =
                            match p with
                            | ElementPlan.Plan.Element e ->
                                (e.Key |> Option.toList)
                                @ (match e.Content with
                                   | ElementPlan.Content.Children cs -> cs |> List.collect walk
                                   | _ -> [])
                            | _ -> []

                        walk plan

                    let model = ElementPlan.TabsModel.ofSpec BindingResolver.empty tabsSpec

                    Expect.equal
                        (keysOf (ElementPlan.LayoutPlan.tabs pc tabsSpec model))
                        [ "n-tab-0"; "n-tab-1" ]
                        "a tab is keyed by its DOM id"

                    Expect.equal
                        (keysOf (ElementPlan.LayoutPlan.stepper pc (stepperSpec false)))
                        [ "#0"; "#1" ]
                        "a step IS its position"
                }
                test "the handler-free layout kinds declare no point" {
                    let children = [ md "x" "x" ]

                    for plan in
                        [ ElementPlan.LayoutPlan.box
                              pc
                              { Defaults.box with
                                  Children = children }
                          ElementPlan.LayoutPlan.splitPanel
                              pc
                              { Defaults.splitPanel with
                                  Children = children }
                          ElementPlan.LayoutPlan.summaryList
                              pc
                              { Defaults.summaryList with
                                  Children = children }
                          ElementPlan.LayoutPlan.scrollArea
                              pc
                              { Defaults.scrollArea with
                                  Children = children } ] do
                        Expect.isEmpty (ElementPlan.Attach.points plan) "no attachment point"
                } ]
          testList
              "the server emits the same markup inert"
              [ test "Tabs: roving tabindex, a disabled tab in the element library's boolean spelling" {
                    let html = Render.renderStatic (Fuaran.tabs "t" tabsSpec)
                    inert html

                    Expect.isTrue
                        (contains
                            (sprintf
                                "<div class=\"%s\" role=\"tablist\" aria-orientation=\"horizontal\"><button id=\"t-tab-0\" class=\"%s %s\" role=\"tab\" aria-selected=\"true\" aria-controls=\"t-panel-0\" tabIndex=\"0\" data-tab-index=\"0\"><span class=\"%s\" data-icon=\"star\" aria-hidden=\"true\"></span><span class=\"%s\">A</span></button>"
                                Css.Class.TabsBar
                                Css.Class.Tab
                                Css.Class.TabActive
                                (Css.iconPlacement Css.Class.TabIcon)
                                Css.Class.TabLabel)
                            html)
                        "the active tab"

                    Expect.isTrue
                        (contains
                            (sprintf
                                "<button id=\"t-tab-1\" class=\"%s %s\" role=\"tab\" aria-selected=\"false\" aria-controls=\"t-panel-1\" tabIndex=\"-1\" data-tab-index=\"1\" aria-disabled=\"true\" disabled=\"true\"><span class=\"%s\">B</span></button>"
                                Css.Class.Tab
                                Css.Class.TabDisabled
                                Css.Class.TabLabel)
                            html)
                        "the disabled tab"

                    Expect.isTrue
                        (contains
                            (sprintf
                                "<div class=\"%s\"><div id=\"t-panel-0\" role=\"tabpanel\" aria-labelledby=\"t-tab-0\" tabIndex=\"0\" class=\"%s\">"
                                Css.Class.TabsPanels
                                Css.Class.TabsPanel)
                            html)
                        "the active panel"
                }
                test "Disclosure: open as a bare attribute" {
                    let html = Render.renderStatic (Fuaran.disclosure "d" disclosureSpec)
                    inert html

                    Expect.isTrue
                        (contains
                            (sprintf
                                "<details class=\"%s\" open=\"\"><summary class=\"%s\">More</summary>"
                                Css.Class.LayoutDisclosure
                                Css.Class.DisclosureSummary)
                            html)
                        "details"
                }
                test "Stepper: each step carries its index, with or without OnSelect" {
                    for onSelect in [ true; false ] do
                        let html = Render.renderStatic (Fuaran.stepper "s" (stepperSpec onSelect))
                        inert html

                        Expect.isTrue
                            (contains
                                (sprintf
                                    "<li class=\"%s\" data-step-index=\"0\">1</li><li class=\"%s\" data-step-index=\"1\">2</li>"
                                    Css.Class.StepperStepActive
                                    Css.Class.StepperStep)
                                html)
                            "steps"
                }
                test "Modal: a closed dismissable modal is hidden, its dismiss button inert" {
                    let html = Render.renderStatic (Fuaran.modal "m" (modalSpec ModalityKind.Modal))
                    inert html

                    Expect.isTrue
                        (contains
                            (sprintf
                                "<div class=\"%s\" hidden=\"\"><div class=\"%s\" role=\"dialog\" aria-modal=\"true\"><button class=\"%s\" type=\"button\" aria-label=\"Close\">×</button><div class=\"%s\">"
                                Css.Class.ModalOverlay
                                Css.Class.ModalDialog
                                Css.Class.ModalDismiss
                                Css.Class.ModalBody)
                            html)
                        "overlay"
                }
                test "Popover: hidden, anchored, its surface a dialog" {
                    let html = Render.renderStatic (Fuaran.modal "p" (modalSpec ModalityKind.Popover))
                    inert html

                    Expect.isTrue
                        (contains
                            (sprintf
                                "<div class=\"%s\" hidden=\"\" data-fuaran-popover-anchor=\"a1\"><div class=\"%s\" role=\"dialog\"><button class=\"%s\" type=\"button\" aria-label=\"Close\">×</button>"
                                Css.Class.Popover
                                Css.Class.PopoverSurface
                                Css.Class.PopoverDismiss)
                            html)
                        "popover"
                } ] ]
