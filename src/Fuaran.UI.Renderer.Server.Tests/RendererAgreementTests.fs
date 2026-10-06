module Fuaran.UI.Renderer.Server.Tests.RendererAgreementTests

// ============================================================================
//  Phase 2041 — the two .NET renderers stop disagreeing.
//
//  The server renderer's half of five agreement fixtures. Each pins a place the
//  server renderer did something the client renderer does not:
//
//    1. Two refs to one fragment: the server expanded the body with its ids
//       untouched, so both copies carried the same DOM ids and neither matched
//       the client's `ref.`-namespaced ids.
//    2. A fragment reference cycle: the server had no guard but the depth limit.
//    3. A host closure that throws: the server had no guard at all, so one
//       throwing `ServerCustomRenderer` (or `CellFormat.Custom`) failed the whole
//       request, inside an `ErrorBoundary` or outside one.
//    4. A chart whose source is loading or errored: the server ignored the
//       node's `OnLoading` / `OnError` slots.
//    5. A tree past the depth limit: the server's marker text is the one the
//       client now emits too.
//
//  The client renderer's half is `Fuaran.UI.Tests/RendererAgreementTests.fs`.
//  The two cannot be diffed byte for byte on .NET (the Feliz client's
//  `ReactElement` is opaque here), so both halves assert against the ONE shared
//  definition each renderer now emits from — `FragmentExpansion.namespaceIds`
//  and `RenderParity` in `Fuaran.UI.Renderer.Core` — which is what makes the
//  agreement observable rather than asserted by two literals kept in step.
// ============================================================================

open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server

let private contains (needle: string) (haystack: string) =
    haystack.Contains(needle, System.StringComparison.Ordinal)

/// Occurrences of `needle` in `haystack`.
let private count (needle: string) (haystack: string) =
    let rec go (from: int) (n: int) =
        match haystack.IndexOf(needle, from, System.StringComparison.Ordinal) with
        | -1 -> n
        | i -> go (i + needle.Length) (n + 1)

    go 0 0

/// Text as the server's HTML writer escapes it (Feliz.ViewEngine writes `'` as
/// `&apos;`).
let private textOf (s: string) =
    s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("'", "&apos;")

/// The `id` attribute alone — not the `data-fuaran-node-id` that ends the same way.
let private idAttr (id: string) = sprintf " id=\"%s\"" id

let private md (id: string) (text: string) : Node<obj> = Fuaran.markdown id text

let private page (children: Node<obj> list) : Node<obj> =
    Fuaran.dashboard
        "page"
        { Defaults.dashboard<obj> with
            Children = children }

let private withState (state: StateBehaviour<obj>) (node: Node<obj>) : Node<obj> = { node with State = Some state }

/// Every node id in a tree, in walk order.
let rec private ids (node: Node<obj>) : string list =
    node.Id
    :: (NodeChildren.children NodeChildren.Reach.kindHeld node |> List.collect ids)

// ─── 1 + 2. Fragments ───────────────────────────────────────────────────────

let private fragmentBody: Node<obj> =
    Fuaran.dashboard
        "card"
        { Defaults.dashboard<obj> with
            Children = [ md "btn" "FRAGMENT-TEXT" ] }

let private twoRefs: Node<obj> =
    page
        [ Fuaran.fragmentDecl
              "decl"
              { Defaults.fragmentDecl<obj> with
                  Name = "shared"
                  Body = fragmentBody }
          Fuaran.fragmentRef "r1" "shared"
          Fuaran.fragmentRef "r2" "shared" ]

let private cyclic: Node<obj> =
    page
        [ Fuaran.fragmentDecl
              "decl"
              { Defaults.fragmentDecl<obj> with
                  Name = "loop"
                  Body =
                      Fuaran.dashboard
                          "inner"
                          { Defaults.dashboard<obj> with
                              Children = [ Fuaran.fragmentRef "again" "loop" ] } }
          Fuaran.fragmentRef "r" "loop" ]

// ─── 3. Host closures ───────────────────────────────────────────────────────

let private throwingRegistry =
    Registry.empty
    |> Registry.register "host" "widget" (fun _ -> failwith "host renderer exploded")

let private throwingCustom (id: string) : Node<obj> =
    Fuaran.custom id "host" "widget" Map.empty Option.None []

let private throwingFormat: CellFormat =
    CellFormat.Custom(fun _ -> failwith "format closure exploded")

// ─── The client half's tree shapes ──────────────────────────────────────────
//
// On .NET the Feliz client throws at its first element, so the client half
// descends through `Switch` defaults (which render their child before building
// any element). These are the same trees, rendered here, so one fixture is
// observed on both renderers.

let private switchTo (id: string) (held: Node<obj> list) (child: Node<obj>) : Node<obj> =
    Fuaran.switch
        id
        { Defaults.switch<obj> with
            Cases =
                held
                |> List.map (fun n ->
                    { Child = n
                      Match = Some "never-selected"
                      When = None })
            Default = child }

let private refToThrowingFragment (refId: string) : Node<obj> =
    switchTo
        "root"
        [ Fuaran.fragmentDecl
              "decl"
              { Defaults.fragmentDecl<obj> with
                  Name = "shared"
                  Body = switchTo "card" [] (throwingCustom "btn") } ]
        (Fuaran.fragmentRef refId "shared")

// ─── 4. Chart state slots ───────────────────────────────────────────────────

let private chart (onError: (ErrorPayload -> Node<obj>) option) : Node<obj> =
    Fuaran.chart
        "ch"
        { Defaults.chart<obj> with
            Source = Binding.State("rows", None)
            XField = "x"
            YFields = [ "y" ] }
    |> withState
        { Defaults.stateBehaviour<obj> with
            OnLoading = Some(md "ld" "CHART-LOADING")
            OnError = onError }

let private errorSlot: ErrorPayload -> Node<obj> =
    fun e -> md "er" ("CHART-ERROR " + e.CorrelationId)

[<Tests>]
let tests =
    testList
        "Phase 2041 — the server renderer agrees with the client"
        [ testList
              "fragment ids"
              [ test "two refs to one fragment emit unique, ref-namespaced ids" {
                    let html = Render.renderStatic twoRefs

                    Expect.equal (count "FRAGMENT-TEXT" html) 2 "both refs expand"
                    Expect.isTrue (contains (idAttr "r1.btn") html) "the first copy is namespaced under its ref"
                    Expect.isTrue (contains (idAttr "r2.btn") html) "the second copy is namespaced under its ref"
                    Expect.isFalse (contains (idAttr "btn") html) "no copy keeps the bare interior id"
                    Expect.equal (count (idAttr "r1.card") html) 1 "each namespaced id appears once"
                }

                test "the SSR ids are exactly the shared walk's — the walk the client expands through" {
                    let html = Render.renderStatic twoRefs

                    for prefix in [ "r1."; "r2." ] do
                        for id in ids (FragmentExpansion.namespaceIds prefix fragmentBody) do
                            Expect.equal (count (idAttr id) html) 1 (sprintf "%s is emitted once" id)
                }

                test "a reference cycle renders the client's cycle placeholder" {
                    let html = Render.renderStatic cyclic

                    Expect.isTrue (contains "data-fuaran-fragment-cycle=\"loop\"" html) "the cycle is marked"

                    Expect.isTrue
                        (contains (textOf (RenderParity.fragmentCycleText "loop")) html)
                        "with the shared text"

                    Expect.isFalse
                        (contains "data-fuaran-depth-exceeded" html)
                        "refused by the guard, not by recursing to the depth limit"
                } ]

          testList
              "host closures"
              [ test "a throwing custom renderer degrades its own node and nothing else" {
                    let tree = page [ throwingCustom "c1"; md "sib" "SIBLING-TEXT" ]
                    let html = Render.renderWith throwingRegistry BindingResolver.empty tree
                    let kindName = RenderParity.nodeKindName (throwingCustom "c1").Kind

                    Expect.isTrue (contains "SIBLING-TEXT" html) "the sibling still renders"
                    Expect.isTrue (contains "data-fuaran-render-failed=\"true\"" html) "the failed node is marked"

                    Expect.isTrue
                        (contains
                            (sprintf
                                "data-fuaran-render-correlation=\"%s\""
                                (RenderParity.renderFailureCorrelationId "c1" kindName))
                            html)
                        "with the correlation id the client stamps on the same failure"

                    Expect.isTrue
                        (contains (textOf (RenderParity.renderFailureText "c1" kindName "host renderer exploded")) html)
                        "and the client's fallback text"

                    Expect.isTrue (contains "data-fuaran-node-id=\"c1\"" html) "the node's wrapper survives"
                }

                test "inside an ErrorBoundary the throw reaches the boundary and its Fallback renders" {
                    let tree =
                        page
                            [ Fuaran.errorBoundary
                                  "eb"
                                  { Child =
                                      Fuaran.dashboard
                                          "inside"
                                          { Defaults.dashboard<obj> with
                                              Children = [ throwingCustom "c2"; md "lost" "LOST-SIBLING" ] }
                                    Fallback = md "fb" "BOUNDARY-FALLBACK" }
                              md "sib" "SIBLING-TEXT" ]

                    let html = Render.renderWith throwingRegistry BindingResolver.empty tree

                    Expect.isTrue (contains "BOUNDARY-FALLBACK" html) "the boundary's fallback renders"
                    Expect.isFalse (contains "LOST-SIBLING" html) "in place of the whole child subtree"
                    Expect.isFalse (contains "data-fuaran-render-failed" html) "not the per-node placeholder"
                    Expect.isTrue (contains "SIBLING-TEXT" html) "and the boundary's own sibling renders"
                }

                test "a fallback that throws as well degrades node by node, under the guard again" {
                    let tree =
                        page
                            [ Fuaran.errorBoundary
                                  "eb"
                                  { Child = throwingCustom "c3"
                                    Fallback = throwingCustom "c4" } ]

                    let html = Render.renderWith throwingRegistry BindingResolver.empty tree
                    let kindName = RenderParity.nodeKindName (throwingCustom "c4").Kind

                    Expect.isTrue
                        (contains
                            (sprintf
                                "data-fuaran-render-correlation=\"%s\""
                                (RenderParity.renderFailureCorrelationId "c4" kindName))
                            html)
                        "the fallback's own node degrades, as on the client"
                }

                test "a throwing CellFormat.Custom closure degrades its own node" {
                    let metric =
                        Fuaran.metric
                            "m1"
                            { Defaults.metric with
                                Value = Binding.Static(Some 1.0)
                                Format = throwingFormat }

                    let html = Render.renderStatic (page [ metric; md "sib" "SIBLING-TEXT" ])

                    Expect.isTrue (contains "SIBLING-TEXT" html) "the request still renders"

                    Expect.isTrue
                        (contains
                            (sprintf
                                "data-fuaran-render-correlation=\"%s\""
                                (RenderParity.renderFailureCorrelationId "m1" (RenderParity.nodeKindName metric.Kind)))
                            html)
                        "the metric is the one node that failed"
                } ]

          testList
              "the client half's fixtures, rendered here"
              [ test "each ref's throwing leaf degrades under its namespaced id and the client's correlation id" {
                    for refId in [ "r1"; "r2" ] do
                        let html =
                            Render.renderWith throwingRegistry BindingResolver.empty (refToThrowingFragment refId)

                        let leaf = refId + ".btn"
                        let kindName = RenderParity.nodeKindName (throwingCustom leaf).Kind

                        Expect.isTrue (contains (idAttr leaf) html) "the leaf carries the namespaced id"

                        Expect.isTrue
                            (contains
                                (sprintf
                                    "data-fuaran-render-correlation=\"%s\""
                                    (RenderParity.renderFailureCorrelationId leaf kindName))
                                html)
                            "and the correlation id the client reports for the same leaf"
                }

                test "a Switch chain past MaxDepth is truncated at the node the client reports" {
                    let mutable tree: Node<obj> = md "leaf" "deep-leaf-marker"

                    for i in 1 .. WireLimits.MaxDepth do
                        tree <- switchTo (sprintf "d%d" i) [] tree

                    let html = Render.renderStatic tree

                    Expect.isFalse (contains "deep-leaf-marker" html) "the content past the limit is not rendered"

                    Expect.isTrue
                        (contains "data-fuaran-depth-exceeded" html
                         && contains "data-fuaran-node-id=\"leaf\"" html)
                        "the marker stands where the client's warning names"
                } ]

          testList
              "chart state slots"
              [ test "an unresolved chart source renders the OnLoading node" {
                    let html = Render.renderStatic (chart (Some errorSlot))

                    Expect.isTrue (contains "CHART-LOADING" html) "the loading slot renders"

                    Expect.isFalse
                        (contains "fuaran-chart-ssr-placeholder" html)
                        "in place of the hydration placeholder"
                }

                test "an errored chart source renders the OnError node with the client's payload" {
                    let sources =
                        { BindingResolver.empty with
                            State = Map.ofList [ "rows", box 42 ] }

                    let html = Render.render sources (chart (Some errorSlot))

                    Expect.isTrue
                        (contains ("CHART-ERROR " + Ids.deterministicCorrelationId "ch") html)
                        "the error slot renders, carrying the correlation id the client passes"
                }

                test "an errored chart source with no OnError slot keeps the placeholder" {
                    let sources =
                        { BindingResolver.empty with
                            State = Map.ofList [ "rows", box 42 ] }

                    let html = Render.render sources (chart None)

                    Expect.isTrue (contains "fuaran-chart-ssr-placeholder" html) "the existing placeholder is unchanged"
                } ]

          testList
              "depth"
              [ test "the depth marker carries the shared text the client emits" {
                    let mutable tree: Node<obj> = md "leaf" "deep-leaf-marker"

                    for i in 1 .. WireLimits.MaxDepth do
                        tree <-
                            Fuaran.dashboard
                                (sprintf "d%d" i)
                                { Defaults.dashboard<obj> with
                                    Children = [ tree ] }

                    let html = Render.renderStatic tree

                    Expect.isTrue (contains (textOf RenderParity.depthExceededText) html) "the shared marker text"
                    Expect.isTrue (contains "data-fuaran-node-id=\"leaf\"" html) "on the node one past the limit"
                } ] ]
