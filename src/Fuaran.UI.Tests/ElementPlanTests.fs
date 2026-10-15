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
                } ] ]
