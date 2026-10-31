module Fuaran.UI.Tests.RenderCost

#nowarn "3261" // `DirectoryInfo.Parent` is legitimately nullable here (the climb's terminator).

// ============================================================================
//  Phase 2069 - what a render pays once.
//
//   1. A producer's bytes reach the sink unchanged. Each fixture is an input the
//      sanitiser WOULD change, so the fixture is non-vacuous: on the old path the
//      client rewrote it and the server did not. The server half is
//      `Fuaran.UI.Renderer.Server.Tests/RendererAgreementTests.fs`, over the same
//      inputs through the same producers.
//   2. The SVG builders escape by construction: hostile strings parse back as
//      text and attribute values, never as elements or handlers.
//   3. One `useKeys`, one subscription per key set.
//   4. One walk for seeds, fragments and keys, agreeing with the walks it replaced.
// ============================================================================

open System
open System.IO
open System.Xml.Linq
open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Charts

// ─── Hostile inputs ──────────────────────────────────────────────────────────

/// Inside an attribute value both of these sit in a tag interior, which is where
/// the sanitiser's two sweeps look, so it rewrites an inert label.
let private inertHandler = "Sales onload=up"
let private inertScheme = "See javascript:void"

let private textOf (t: TextSource) : string =
    match t with
    | TextSource.Literal s -> s
    | _ -> "?"

/// A `Description` is what puts the title into the root's `aria-label`.
let private drawingOf (title: string) =
    { Defaults.drawing with
        Title = Some(TextSource.Literal title)
        Description = Some(TextSource.Literal "A figure") }

let private drawingSvg (title: string) : string =
    DrawingSvg.render BindingResolver.empty textOf (drawingOf title)

let private rows: Map<string, obj> list =
    [ Map.ofList [ "q", box "Q1 <script>alert(1)</script>"; "v", box 1.0 ]
      Map.ofList [ "q", box "Q2\" onclick=\"alert(1)"; "v", box 2.0 ] ]

let private chartOf (title: string) : ChartSpec<obj> =
    { Defaults.chart with
        Kind = ChartKind.Bar
        Source = Binding.Static(Some(rows :> Map<string, obj> seq))
        XField = "q"
        YFields = [ "v" ]
        Title = Some(TextSource.Literal title) }

let private chartSvg (title: string) : string =
    renderSvg BindingResolver.empty textOf (chartOf title) rows

// ─── A hook host for `useKeysWith` ───────────────────────────────────────────

/// The two hooks, as React runs them for one mounted component: state survives
/// re-renders, and an effect re-runs only when its deps move, running the previous
/// cleanup first.
type private HookHost() =
    let mutable tick = 0
    let mutable deps: obj array option = None
    let mutable cleanup: (unit -> unit) option = None

    member val Pending = false with get, set

    member this.UseTick(initial: int) : int * ((int -> int) -> unit) =
        ignore initial

        tick,
        fun update ->
            tick <- update tick
            this.Pending <- true

    member _.UseEffect (effect: unit -> (unit -> unit)) (next: obj array) : unit =
        if deps <> Some next then
            cleanup |> Option.iter (fun run -> run ())
            cleanup <- Some(effect ())
            deps <- Some next

let private repoRoot: string =
    let rec climb (dir: DirectoryInfo) =
        if isNull dir then
            failwith "RenderCostTests: could not locate the repo root."
        elif
            File.Exists(Path.Combine(dir.FullName, "Fuaran.sln"))
            && File.Exists(Path.Combine(dir.FullName, "SANITIZATION.md"))
        then
            dir.FullName
        else
            climb dir.Parent

    climb (DirectoryInfo(AppContext.BaseDirectory))

let private rendererSource (file: string) =
    File.ReadAllText(Path.Combine(repoRoot, "src", "Fuaran.UI.Renderer", file))

// ─── Trees for the walk ──────────────────────────────────────────────────────

let private md (id: string) : Node<obj> = Fuaran.markdown id "text"

let private seededMetric (id: string) (key: string) : Node<obj> =
    Fuaran.metric
        id
        { Defaults.metric with
            Label = TextSource.Literal "M"
            Value = Binding.State(key, Some 1.0) }

let private decl (name: string) (body: Node<obj>) : Node<obj> =
    Fuaran.fragmentDecl
        ("decl-" + name)
        { Defaults.fragmentDecl<obj> with
            Name = name
            Body = body }

let private page (id: string) (children: Node<obj> list) : Node<obj> =
    Fuaran.dashboard
        id
        { Defaults.dashboard<obj> with
            Children = children }

[<Tests>]
let tests =
    testList
        "Phase 2069 - render cost"
        [ testList
              "the sink changes no byte"
              [ for name, svg in
                    [ "drawing, handler-shaped title", drawingSvg inertHandler
                      "drawing, scheme-shaped title", drawingSvg inertScheme
                      "chart, handler-shaped title", chartSvg inertHandler
                      "chart, scheme-shaped title", chartSvg inertScheme ] do
                    test name {
                        Expect.notEqual
                            (Sanitize.sanitizeMarkdownHtml svg)
                            svg
                            "the fixture must be one the sanitiser would change, or it pins nothing"

                        Expect.equal (TrustedTypes.html svg) svg "a mint must leave the producer's bytes alone"
                    }

                test "markdown is sanitised once, by its producer" {
                    let html = Markdown.toHtml "[x](javascript:alert(1)) <script>alert(1)</script>"
                    Expect.equal (Sanitize.sanitizeMarkdownHtml html) html "the producer has already applied the floor"
                }

                test "a theme's stylesheet applies the floor itself, for both renderers" {
                    let theme =
                        { Defaults.theme with
                            Spacing =
                                { Defaults.theme.Spacing with
                                    Xs = "1px</style><script>alert(1)</script>" } }

                    Expect.isFalse
                        ((Theme.toCss theme).Contains("<script", StringComparison.OrdinalIgnoreCase))
                        "a raw `<style>` sink must not carry a host string's markup"
                } ]

          testList
              "the SVG builders escape by construction"
              [ for name, svg in
                    [ "drawing", drawingSvg "<script>alert(1)</script>\" onload=\"alert(1)"
                      "chart", chartSvg "<script>alert(1)</script>\" onload=\"alert(1)" ] do
                    test name {
                        let doc = XDocument.Parse svg

                        let names =
                            doc.Descendants() |> Seq.map (fun e -> e.Name.LocalName.ToLowerInvariant())

                        Expect.isFalse (Seq.contains "script" names) "hostile text must not become an element"

                        let handlers =
                            doc.Descendants()
                            |> Seq.collect (fun e -> e.Attributes())
                            |> Seq.filter (fun a ->
                                a.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase))

                        Expect.isEmpty handlers "hostile text must not become an attribute"
                    } ]

          testList
              "one keyed hook"
              [ test "a selection change keeps its one subscription" {
                    let key = "p2069-selection"
                    SelectionStore.reset ()
                    let host = HookHost()
                    let mutable opened = 0

                    let subscribe keys callback =
                        opened <- opened + 1
                        SelectionStore.subscribeKeys keys callback

                    let render () =
                        StateStore.useKeysWith host.UseTick host.UseEffect subscribe (Set.singleton key)

                    let first = render ()

                    for row in 1..3 do
                        SelectionStore.set key (box row |> Unchecked.nonNull)
                        Expect.isTrue host.Pending "a write to a subscribed key re-renders"
                        host.Pending <- false
                        render () |> ignore

                    Expect.equal opened 1 "three changes, one subscription"
                    Expect.equal (render ()) (first + 3) "every notification advanced the tick"
                    SelectionStore.reset ()
                }

                test "a new key set resubscribes" {
                    let host = HookHost()
                    let mutable opened = 0
                    let mutable closed = 0

                    let subscribe _ _ =
                        opened <- opened + 1
                        fun () -> closed <- closed + 1

                    StateStore.useKeysWith host.UseTick host.UseEffect subscribe (Set.singleton "a")
                    |> ignore

                    StateStore.useKeysWith host.UseTick host.UseEffect subscribe (Set.singleton "b")
                    |> ignore

                    Expect.equal (opened, closed) (2, 1) "the old subscription closes and the new one opens"
                }

                test "the four hooks are instances of `useKeys`" {
                    for file, hook in
                        [ "StateStore.fs", "useStateKeys"
                          "FilterStore.fs", "useFilterKeys"
                          "SelectionStore.fs", "useSelectionKeys"
                          "QueryStore.fs", "useQueryKeys" ] do
                        let source = rendererSource file

                        Expect.stringContains
                            source
                            (sprintf "let %s" hook)
                            (sprintf "%s must still declare %s" file hook)

                        Expect.stringContains source "useKeys subscribeKeys" (sprintf "%s must call `useKeys`" file)

                    for file in [ "FilterStore.fs"; "SelectionStore.fs"; "QueryStore.fs" ] do
                        Expect.isFalse
                            ((rendererSource file).Contains "React.use")
                            (sprintf "%s must hold no React hook of its own" file)
                } ]

          testList
              "one walk"
              [ test "`kindKeys` is gone" {
                    Expect.isFalse
                        ((rendererSource "Render.fs").Contains "kindKeys")
                        "the reactive keys are a projection of `BindingWalk`'s uses"
                }

                test "a tree that declares neither pays for neither" {
                    let tree = page "root" [ md "a"; md "b" ]
                    let facts = BindingWalk.renderFacts tree
                    let sources = BindingResolver.empty

                    Expect.isEmpty facts.StateSeeds "no seeds"
                    Expect.isEmpty facts.Fragments "no fragments"

                    Expect.isTrue
                        (obj.ReferenceEquals(StateSeeds.applyFacts facts tree sources, sources))
                        "no seeds, so the caller's sources come back by reference"

                    Expect.isEmpty (Render.RenderContext.prepare tree).Fragments "an empty registry"
                }

                test "seeds agree with the analysis walk, first declaration winning" {
                    let tree =
                        page
                            "root"
                            [ seededMetric "a" "k1"
                              seededMetric "b" "k2"
                              decl "f" (seededMetric "c" "k3") ]

                    let facts = BindingWalk.renderFacts tree
                    Expect.equal facts.StateSeeds (BindingWalk.stateSeeds tree) "one seed map, two passes"
                    Expect.equal facts.StateSeeds.Count 3 "the fixture seeds three keys"
                }

                test "the registry agrees with the canonical walk" {
                    let tree =
                        page
                            "root"
                            [ decl "one" (md "a")
                              Fuaran.errorBoundary
                                  "eb"
                                  { Child = decl "two" (md "b")
                                    Fallback = decl "three" (md "c") } ]

                    let viaPass = (Render.RenderContext.prepare tree).Fragments
                    let canonical = Render.collectFragments Map.empty tree
                    Expect.equal (viaPass |> Map.keys |> List.ofSeq) (canonical |> Map.keys |> List.ofSeq) "same names"
                    Expect.equal viaPass.Count 3 "the fixture declares three"
                }

                test "a repeated name keeps the canonical winner" {
                    // The walk reaches a state arm before the kind's children;
                    // the canonical order is the other way round.
                    let tree =
                        { page "root" [ decl "dup" (md "from-child") ] with
                            State =
                                Some
                                    { OnEmpty = Some(decl "dup" (md "from-state"))
                                      OnLoading = None
                                      OnError = None } }

                    Expect.equal
                        ((Render.RenderContext.prepare tree).Fragments |> Map.map (fun _ n -> n.Id))
                        (Render.collectFragments Map.empty tree |> Map.map (fun _ n -> n.Id))
                        "the winner of a repeated name does not move"
                }

                test "a fallback declares no fragment and opens no key" {
                    let tree =
                        { md "body" with
                            Fallback = Some(page "fb" [ decl "hidden" (seededMetric "m" "fb-key") ]) }

                    let facts = BindingWalk.renderFacts tree
                    Expect.isEmpty facts.Fragments "outside fragment scope"
                    Expect.isEmpty facts.Keys.State "a current reader never renders the fallback"

                    Expect.equal
                        facts.StateSeeds
                        (BindingWalk.stateSeeds tree)
                        "its seed still counts, as it does in the analysis walk"
                }

                test "a live source read through a `Local` is subscribed" {
                    let source: Binding<JVal> =
                        Binding.Local(
                            LocalFlushTrigger.OnBlur,
                            string,
                            Binding.Filter("p2069-through", None),
                            None,
                            (fun _ -> Ok(JStr "")),
                            None,
                            None
                        )

                    let reader: Binding<float> =
                        Binding.Transform(TransformSource.Live(source, HostPrelude.TransformLive.emptySource), [], None)

                    Expect.equal (Render.filterKeysOfBinding reader) [ "p2069-through" ] "the source's read opens a key"
                } ] ]
