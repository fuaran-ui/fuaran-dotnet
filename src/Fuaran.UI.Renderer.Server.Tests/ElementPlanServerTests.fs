module Fuaran.UI.Renderer.Server.Tests.ElementPlanServerTests

// ============================================================================
//  Phase 2108 — one per-kind element plan, emitted by both renderers.
//
//  The server half. The client renderer's Feliz element is opaque on .NET, so
//  the two tiers cannot be diffed byte for byte here; what makes them agree is
//  that both emit the SAME plan from `Fuaran.UI.Renderer.ElementPlan`. These
//  fixtures pin (1) that the server's HTML is exactly the plan it was given —
//  the wrapper's bytes, attribute order included — and (2) every place the
//  two tiers used to disagree and now emit one behaviour. The client half,
//  which asserts the plan VALUES each tier emits, is
//  `Fuaran.UI.Tests/ElementPlanTests.fs`.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server

let private contains (needle: string) (haystack: string) =
    haystack.Contains(needle, System.StringComparison.Ordinal)

let private md (id: string) (text: string) : Node<obj> = Fuaran.markdown id text

let private throwingRegistry =
    Registry.empty
    |> Registry.register "host" "widget" (fun _ -> failwith "host renderer exploded")

let private throwingCustom (id: string) : Node<obj> =
    Fuaran.custom id "host" "widget" Map.empty Option.None []

[<Tests>]
let tests =
    testList
        "Phase 2108 — the server emits the shared element plan"
        [ testList
              "stage 1 — the per-node wrapper"
              [ test "the wrapper's bytes are the plan's: id, node id, class, routed attributes, body, hint" {
                    let node =
                        { md "n" "body" with
                            Tooltip = Some(TextSource.Literal "the hint")
                            ExtraAttributes = Some(Map.ofList [ "data-test", "x" ]) }

                    let routing = ElementPlan.NodePlan.route BindingResolver.empty node
                    let html = Render.renderStatic node

                    let expectedOpen =
                        sprintf
                            "<div id=\"n\" data-fuaran-node-id=\"n\" class=\"%s\"%s>"
                            routing.ClassName
                            (routing.WrapperAttrs
                             |> List.map (fun (k, v) -> sprintf " %s=\"%s\"" k v)
                             |> String.concat "")

                    Expect.stringStarts html expectedOpen "the wrapper opens exactly as the plan routes it"

                    Expect.stringEnds
                        html
                        (sprintf
                            "<span id=\"%s\" class=\"fuaran-tooltip\" role=\"tooltip\">the hint</span></div>"
                            (Accessibility.tooltipHintId "n"))
                        "the hint is the body's last sibling inside the wrapper"
                }

                test "a blank hint emits nothing, on both tiers' shared rule" {
                    let node =
                        { md "n" "body" with
                            Tooltip = Some(TextSource.Literal "   ") }

                    Expect.isNone (ElementPlan.NodePlan.route BindingResolver.empty node).TooltipText "no hint"
                    Expect.isFalse (contains "fuaran-tooltip" (Render.renderStatic node)) "no hint element"
                }

                test "the island marker rides the boundary, never the node's own wrapper" {
                    let html = Render.renderStatic (Fuaran.UI.Node.asIsland "isl" (md "n" "body"))

                    Expect.stringStarts
                        html
                        "<div class=\"fuaran-island\" data-fuaran-island=\"isl\"><div id=\"n\""
                        "the boundary wraps the node"

                    Expect.equal
                        (html.Split("data-fuaran-island").Length - 1)
                        1
                        "the marker is written once, on the boundary"
                }

                test "a node that throws renders the shared fallback plan inside its wrapper" {
                    let html =
                        Render.renderWith
                            throwingRegistry
                            BindingResolver.empty
                            (Fuaran.dashboard
                                "page"
                                { Defaults.dashboard<obj> with
                                    Children = [ throwingCustom "w" ] })

                    let kindName = "Custom.host.widget"

                    Expect.isTrue
                        (contains
                            (sprintf
                                "data-fuaran-render-correlation=\"%s\""
                                (RenderParity.renderFailureCorrelationId "w" kindName))
                            html)
                        "the per-node guard stamps the failure's own correlation id"
                } ] ]
