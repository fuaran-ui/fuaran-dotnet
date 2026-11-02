module Fuaran.UI.Tests.ReaderBoundary

// ============================================================================
//  Phase 2530 — a state write under `renderStateReactive` re-renders only its
//  readers.
//
//  The .NET-testable half: the key-to-reader map (`BindingWalk.reactiveRenderFacts`
//  + `readerMap`), the boundaries the reactive entry places
//  (`ReaderBoundaries.plan`) and the boundaries one write re-renders
//  (`ReaderBoundaries.rerenderedBy`). The browser half — the boundary components
//  themselves and the instrumented count `ReaderBoundaries.renderCount` — runs
//  under Fable in the renderer benchmark's `writeReactive` operation, which
//  refuses a run whose measured count differs from `rerenderedBy`.
// ============================================================================

open Expecto
open Fuaran.Compute
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Render

type private Msg = NoOp

let private metric (id: string) (value: Binding<float>) : Node<Msg> =
    Fuaran.metric
        id
        { Defaults.metric with
            Label = TextSource.Literal id
            Value = value }

let private stack (id: string) (children: Node<Msg> list) : Node<Msg> =
    Fuaran.stack
        id
        { Defaults.stack<Msg> with
            Children = children }

let private card (id: string) (children: Node<Msg> list) : Node<Msg> =
    Fuaran.card
        id
        { Defaults.card<Msg> with
            Children = children }

let private stateOf (key: string) : Binding<float> = Binding.State(key, None)

let private stateReaders (node: Node<Msg>) : Map<string, Set<string>> =
    BindingWalk.reactiveRenderFacts node
    |> snd
    |> BindingWalk.readerMap (fun k -> k.State)

let private ids (boundaries: ReaderBoundary<Msg> list) : string list =
    boundaries |> List.map (fun b -> b.Node.Id)

// ─── The 24-site tree (Phase 1761's shape, as the benchmark builds it) ──────

let private chips = [ "region"; "genre"; "month"; "floor" ]

let private emptyTable: Fuaran.Core.Table = { Schema = []; Columns = [] }

/// A site chart READS one chip and DECLARES all four: the generator's env binds
/// more than a pipeline reads.
let private siteChart (i: int) : Node<Msg> =
    let chip = chips[i % chips.Length]

    let declared =
        chips
        |> List.map (fun c ->
            ({ Name = c
               From = Binding.Filter(c, None) }
            : TransformParam))

    Fuaran.chart
        $"site-{i}-chart"
        { Defaults.chart<Msg> with
            Source =
                Binding.Transform(
                    TransformSource.Data(Fuaran.Core.Embedded emptyTable),
                    [ Filter(Binary(Eq, Col chip, Param chip)) ],
                    Some declared
                )
            XField = "region"
            YFields = [ "n" ] }

let private sites24: Node<Msg> =
    let legend =
        Fuaran.fragmentDecl
            "decl-site-legend"
            { Defaults.fragmentDecl<Msg> with
                Name = "site-legend"
                Body = metric "site-legend-a" (Binding.Static(Some 2000.0)) }

    stack
        "sites"
        (legend
         :: [ for i in 0..23 -> card $"site-{i}" [ Fuaran.fragmentRef $"site-{i}-legend" "site-legend"; siteChart i ] ])

let private mapTests =
    testList
        "the key-to-reader map"
        [ testCase "every Binding.State reader is reachable from its key, and nothing else is"
          <| fun () ->
              let button =
                  Fuaran.button
                      "copy"
                      { Defaults.button<Msg> with
                          Label = TextSource.Literal "Copy"
                          OnClick = Action.SetState("target", None, Some(Binding.State("source", None))) }

              let hidden =
                  { metric "maybe" (Binding.Static(Some 1.0)) with
                      Visible = Some(Binding.State("show", None)) }

              let tree =
                  stack
                      "root"
                      [ metric "a" (stateOf "k1")
                        metric "b" (stateOf "k1")
                        card
                            "c"
                            [ metric "c-inner" (stateOf "k2")
                              metric "c-static" (Binding.Static(Some 3.0)) ]
                        hidden
                        button
                        metric "plain" (Binding.Static(Some 4.0)) ]

              let expected =
                  Map.ofList
                      [ "k1", set [ "a"; "b" ]
                        "k2", set [ "c-inner" ]
                        "show", set [ "maybe" ]
                        "source", set [ "copy" ] ]

              Expect.equal (stateReaders tree) expected "each key maps to exactly the nodes that read it"

              // Against the analysis walk: every `State` use it records is in the
              // map under its key, and the map holds no reader the analysis lacks.
              let analysed =
                  (BindingWalk.collect tree).Uses
                  |> List.choose (fun u ->
                      match u.Use with
                      | BindingWalk.BindingUse.State k -> Some(k, u.Reader)
                      | _ -> None)
                  |> Set.ofList

              let mapped =
                  stateReaders tree
                  |> Map.toList
                  |> List.collect (fun (k, readers) -> readers |> Set.toList |> List.map (fun r -> k, r))
                  |> Set.ofList

              Expect.equal mapped analysed "the map and the analysis name the same (key, reader) pairs"

          testCase "a Transform param the pipeline does not reference reads nothing"
          <| fun () ->
              let filterReaders =
                  BindingWalk.reactiveRenderFacts sites24
                  |> snd
                  |> BindingWalk.readerMap (fun k -> k.Filter)

              Expect.equal
                  filterReaders["region"]
                  (set
                      [ for i in 0..23 do
                            if i % 4 = 0 then
                                $"site-{i}-chart" ])
                  "the region chip reaches the six charts whose pipeline reads it"

              // The declared walk still subscribes every site to every chip: that is
              // the reading Phase 2529 refuted the first acceptance line against.
              Expect.equal
                  (BindingWalk.renderFacts sites24).Keys.Filter
                  (set chips)
                  "renderFacts keeps the declared projection"

          testCase "the reactive walk keeps every seed a param declares"
          <| fun () ->
              let seededParam =
                  Fuaran.chart
                      "seeded"
                      { Defaults.chart<Msg> with
                          Source =
                              Binding.Transform(
                                  TransformSource.Data(Fuaran.Core.Embedded emptyTable),
                                  [],
                                  Some
                                      [ ({ Name = "unused"
                                           From = Binding.State("floor", Some(Fuaran.Core.JVal.JFloat 3.0)) }
                                        : TransformParam) ]
                              ) }

              let tree = stack "root" [ seededParam; metric "reader" (stateOf "floor") ]
              let reactive, _ = BindingWalk.reactiveRenderFacts tree

              Expect.equal
                  reactive.StateSeeds
                  (BindingWalk.renderFacts tree).StateSeeds
                  "an unreferenced param's seed still seeds"

              Expect.equal (stateReaders tree) (Map.ofList [ "floor", set [ "reader" ] ]) "and still reads nothing"

          testCase "a fragment reference carries its body's reads, through nested references"
          <| fun () ->
              let decl name body =
                  Fuaran.fragmentDecl
                      $"decl-{name}"
                      { Defaults.fragmentDecl<Msg> with
                          Name = name
                          Body = body }

              let tree =
                  stack
                      "root"
                      [ decl "inner" (metric "inner-body" (stateOf "deep"))
                        decl
                            "outer"
                            (stack
                                "outer-body"
                                // fuaran-validator: disable-next-line FUARAN057 — the declarations come from the local `decl` helper, which the source scan cannot follow
                                [ metric "outer-own" (stateOf "shallow"); Fuaran.fragmentRef "nested" "inner" ])
                        // fuaran-validator: disable-next-line FUARAN057 — the declarations come from the local `decl` helper, which the source scan cannot follow
                        Fuaran.fragmentRef "use-outer" "outer"
                        // fuaran-validator: disable-next-line FUARAN057 — negative case: an undeclared reference is what the reader boundary is measured against
                        Fuaran.fragmentRef "use-static" "none-declared" ]

              let readers = stateReaders tree

              Expect.isTrue
                  (readers["deep"].Contains "use-outer")
                  "the outer reference re-renders on the inner body's key"

              Expect.isTrue (readers["shallow"].Contains "use-outer") "and on its own body's key"

              Expect.isFalse
                  (readers |> Map.exists (fun _ r -> r.Contains "use-static"))
                  "a reference to nothing reads nothing"

          testCase "an ErrorBoundary carries its child's reads; a Computed reader is opaque"
          <| fun () ->
              let boundary =
                  Fuaran.errorBoundary
                      "guard"
                      { Defaults.errorBoundary<Msg> with
                          Child = metric "guarded" (stateOf "risky") }

              let computed = metric "computed" (Binding.Computed(fun _ -> 1.0))
              let tree = stack "root" [ boundary; computed; metric "other" (stateOf "elsewhere") ]
              let facts, entries = BindingWalk.reactiveRenderFacts tree
              let plan = ReaderBoundaries.plan facts.Keys entries

              Expect.containsAll
                  (ids (ReaderBoundaries.rerenderedBy StateChannel "risky" plan))
                  [ "guard"; "guarded" ]
                  "the boundary node re-renders its child"

              Expect.contains
                  (ids (ReaderBoundaries.rerenderedBy StateChannel "elsewhere" plan))
                  "computed"
                  "an opaque reader subscribes every key the surface reads" ]

let private boundaryTests =
    testList
        "the boundaries a reactive render places"
        [ testCase "on the 24-site tree one chip write re-renders the six charts that read it"
          <| fun () ->
              let _, plan = Render.prepareReactive sites24
              let rerendered = ReaderBoundaries.rerenderedBy FilterChannel "region" plan

              Expect.equal
                  (ids rerendered)
                  [ for i in 0..23 do
                        if i % 4 = 0 then
                            $"site-{i}-chart" ]
                  "six boundaries, and no card, legend or surface"

              // Before Phase 2530 the surface was the one subscriber, so a write
              // re-rendered all 24 sites (Phase 2529's reading).
              Expect.equal plan.Length 24 "every chart is a boundary and nothing else is"

          testCase "a node with several entries is ONE boundary, its keys unioned"
          <| fun () ->
              let shared = metric "shared" (stateOf "x")

              let entries: BindingWalk.ReaderEntry<Msg> list =
                  [ { Node = shared
                      Keys = (BindingWalk.renderFacts shared).Keys
                      Opaque = false }
                    { Node = shared
                      Keys =
                        { (BindingWalk.renderFacts shared).Keys with
                            State = set [ "y" ] }
                      Opaque = false } ]

              let plan = ReaderBoundaries.plan (BindingWalk.renderFacts shared).Keys entries
              Expect.equal plan.Length 1 "one boundary per node identity"
              Expect.equal plan.Head.Keys.State (set [ "x"; "y" ]) "subscribed to both entries' keys"

          testCase "a static tree places no boundary"
          <| fun () ->
              let _, plan =
                  Render.prepareReactive (stack "root" [ metric "s" (Binding.Static(Some 1.0)) ])

              Expect.isEmpty plan "nothing reads, so nothing re-renders"

          testCase "the instrumented count reads zero on .NET, where nothing subscribes"
          <| fun () -> Expect.equal (ReaderBoundaries.renderCount ()) 0 "boundary components run only in a browser" ]

[<Tests>]
let tests = testList "Phase 2530 reader boundaries" [ mapTests; boundaryTests ]
