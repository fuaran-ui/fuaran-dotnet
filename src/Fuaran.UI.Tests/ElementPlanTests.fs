module Fuaran.UI.Tests.ElementPlanTests

// ============================================================================
//  Phase 2108 — one per-kind element plan, emitted by both renderers.
//
//  The plan half. Both renderers emit `Fuaran.UI.Renderer.ElementPlan` values
//  through a thin per-tier `Emit`; the server half
//  (`Fuaran.UI.Renderer.Server.Tests/ElementPlanServerTests.fs`) pins that the
//  server's HTML is exactly the plan. This half pins the plan VALUES — the one
//  definition the client renderer now emits from too, since its Feliz element
//  is opaque on .NET and cannot be read back here.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.ElementPlan

/// The top-level element of a plan, or a failure naming what it was instead.
let private elementOf (plan: Plan<unit>) : ElementSpec<unit> =
    match plan with
    | Plan.Element spec -> spec
    | other -> failwithf "expected an element, got %A" other

let private attrsOf (spec: ElementSpec<unit>) : (string * string) list =
    spec.Props
    |> List.choose (function
        | Prop.Attr(k, v) -> Some(k, v)
        | Prop.Style _ -> None)

[<Tests>]
let tests =
    testList
        "Phase 2108 — the element plan both renderers emit"
        [ testList
              "stage 1 — the per-node wrapper"
              [ test "the wrapper is id, node id, class, then the routed attributes, around the body" {
                    let node =
                        { (Fuaran.markdown "n" "x": Node<unit>) with
                            ExtraAttributes = Some(Map.ofList [ "data-test", "x" ]) }

                    let routing = NodePlan.route BindingResolver.empty node
                    let wrapper = elementOf (NodePlan.wrapper "n" routing (Plan.Host()))

                    Expect.equal wrapper.Tag "div" "a div"

                    Expect.equal
                        (attrsOf wrapper)
                        ([ "id", "n"; "data-fuaran-node-id", "n"; "class", routing.ClassName ]
                         @ routing.WrapperAttrs)
                        "attribute order is load-bearing"

                    Expect.equal wrapper.Content (Content.Children [ Plan.Host() ]) "the body alone"
                }

                test "a hint rides as the body's sibling and its describedby takes the focus stop on the wrapper" {
                    let node =
                        { (Fuaran.markdown "n" "x": Node<unit>) with
                            Tooltip = Some(TextSource.Literal "hint") }

                    let routing = NodePlan.route BindingResolver.empty node

                    Expect.equal routing.TooltipText (Some "hint") "resolved"
                    Expect.contains routing.WrapperAttrs ("tabindex", "0") "the wrapper is the focus stop"

                    match (elementOf (NodePlan.wrapper "n" routing (Plan.Host()))).Content with
                    | Content.Children [ Plan.Host(); Plan.Element hint ] ->
                        Expect.contains (attrsOf hint) ("role", "tooltip") "the hint element"
                        Expect.equal hint.Content (Content.Text "hint") "its text"
                    | other -> failtestf "unexpected wrapper content %A" other
                }

                test "a semantic-element kind takes the a11y and aria-* extras; the wrapper keeps data-*" {
                    let link =
                        { (Fuaran.link "l" "https://example.org" "go": Node<unit>) with
                            ExtraAttributes = Some(Map.ofList [ "data-k", "1"; "aria-describedby", "d" ]) }

                    let routing = NodePlan.route BindingResolver.empty link

                    Expect.contains routing.WrapperAttrs ("data-k", "1") "data-* on the wrapper"
                    Expect.contains routing.SemanticAttrs ("aria-describedby", "d") "aria-* on the anchor"
                    Expect.isFalse (List.contains ("aria-describedby", "d") routing.WrapperAttrs) "and only there"
                }

                test "parity fix (e) — an extra attribute name no serialiser can write is dropped on both tiers" {
                    // The allowlist admits the prefix; the name gate is what drops
                    // the space. The server always filtered here; the client relied
                    // on React rejecting the name, and now reads the same rule.
                    let node =
                        { (Fuaran.markdown "n" "x": Node<unit>) with
                            ExtraAttributes = Some(Map.ofList [ "data-ok", "1"; "data-bad name", "2" ]) }

                    let pairs = NodePlan.extraAttributePairs node

                    Expect.contains pairs ("data-ok", "1") "the writable name stays"
                    Expect.isFalse (pairs |> List.exists (fun (k, _) -> k.Contains " ")) "the unwritable one goes"
                }

                test "the render-failure element carries the correlation id it is given" {
                    let spec = elementOf (NodePlan.renderFailure "n" "Display.Badge" "boom" "corr-1")

                    Expect.contains (attrsOf spec) ("data-fuaran-render-correlation", "corr-1") "the id"
                    Expect.contains (attrsOf spec) ("data-fuaran-render-failed", "true") "the marker"

                    Expect.equal
                        spec.Content
                        (Content.Text(RenderParity.renderFailureText "n" "Display.Badge" "boom"))
                        "the shared text"
                }

                test "the depth marker is the shared element" {
                    let spec = elementOf (NodePlan.depthExceeded "deep")

                    Expect.contains
                        (attrsOf spec)
                        ("data-fuaran-depth-exceeded", string WireLimits.MaxDepth)
                        "carries the limit"

                    Expect.equal
                        spec.Content
                        (Content.Children [ Plan.TextNode RenderParity.depthExceededText ])
                        "and the shared text"
                } ]

          testList
              "emission helpers"
              [ test "canonical CSS property names camel-case for React's style object" {
                    Expect.equal (camelCaseProperty "grid-template-columns") "gridTemplateColumns" "three parts"
                    Expect.equal (camelCaseProperty "width") "width" "one part"
                    Expect.equal (camelCaseProperty "--fuaran-x") "--fuaran-x" "custom properties pass"
                }

                test "a style site is inline under Permissive and a generated class under Strict" {
                    let decls = Csp.Declarations.progressFill 0.5

                    Expect.equal
                        (styleSite Csp.Permissive None "n" "progress-fill" decls)
                        ("", [ Prop.Style decls ])
                        "permissive: inline, no class"

                    let collector = Csp.StyleCollector()

                    let suffix, props =
                        styleSite (Csp.Strict "x") (Some collector) "n" "progress-fill" decls

                    Expect.equal suffix (" " + Csp.generatedClass "n" "progress-fill" decls) "strict: the class"
                    Expect.isEmpty props "and no inline style"
                } ]

          testList
              "stage 2 — the leaf display kinds"
              [ test "skeleton rows and list items carry their positional keys in the plan" {
                    let rows =
                        match (DisplayPlan.skeleton ({ Rows = 2 }: SkeletonSpec): Plan<unit>) with
                        | Plan.Element { Content = Content.Children rows } -> rows
                        | other -> failtestf "unexpected %A" other

                    Expect.equal
                        (rows
                         |> List.map (function
                             | Plan.Element e -> e.Key
                             | _ -> None))
                        [ Some "#1"; Some "#2" ]
                        "keyed by position, from 1"

                    let items =
                        match
                            (DisplayPlan.list
                                BindingResolver.empty
                                { Defaults.list with
                                    Items = [ TextSource.Literal "a"; TextSource.Literal "b" ] }
                            : Plan<unit>)
                        with
                        | Plan.Element { Content = Content.Children items } -> items
                        | other -> failtestf "unexpected %A" other

                    Expect.equal
                        (items
                         |> List.map (function
                             | Plan.Element e -> e.Key
                             | _ -> None))
                        [ Some "#0"; Some "#1" ]
                        "list items keyed by position, from 0"
                }

                test "parity fix (f) — the progress fill's inline style is the canonical declaration on both tiers" {
                    let plan: Plan<unit> =
                        DisplayPlan.progress
                            BindingResolver.empty
                            (styleSite Csp.Permissive None "p")
                            (BindingResolver.Resolved 0.5)
                            { Defaults.progress with
                                Fraction = Binding.Static(Some 0.5) }

                    let rec styles (p: Plan<unit>) : (string * string) list list =
                        match p with
                        | Plan.Element e ->
                            (e.Props
                             |> List.choose (function
                                 | Prop.Style d -> Some d
                                 | _ -> None))
                            @ (match e.Content with
                               | Content.Children cs -> cs |> List.collect styles
                               | _ -> [])
                        | _ -> []

                    Expect.equal (styles plan) [ Csp.Declarations.progressFill 0.5 ] "one fill style, canonical"
                }

                test "(d) the state-slot rule: OnLoading while unresolved, OnError with the deterministic id" {
                    let loading: Node<unit> = Fuaran.markdown "ld" "loading"

                    let state: StateBehaviour<unit> =
                        { Defaults.stateBehaviour<unit> with
                            OnLoading = Some loading
                            OnError = Some(fun e -> Fuaran.markdown "er" e.CorrelationId) }

                    Expect.equal
                        (DisplayPlan.stateSlot "k" state BindingResolver.NotResolved |> Option.map _.Id)
                        (Some "ld")
                        "loading"

                    match
                        DisplayPlan.stateSlot "k" state (BindingResolver.Errored "x": BindingResolver.Resolution<float>)
                    with
                    | Some node -> Expect.equal node.Id "er" "the error slot"
                    | None -> failtest "expected the error slot"

                    Expect.isNone
                        (DisplayPlan.stateSlot "k" state (BindingResolver.Resolved 1.0))
                        "a resolved value renders the body"
                }

                test "the scalar text is the formatted value or its loud sentinel" {
                    Expect.equal
                        (DisplayPlan.scalarText (CellFormat.Number(Some 1)) (BindingResolver.Resolved 2.0))
                        "2.0"
                        "value"

                    Expect.equal (DisplayPlan.scalarText CellFormat.None BindingResolver.NotResolved) "—" "pending"

                    Expect.equal
                        (DisplayPlan.scalarText CellFormat.None (BindingResolver.Errored "e"))
                        "(error: e)"
                        "error"
                } ] ]
