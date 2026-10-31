module Fuaran.UI.Renderer.Benchmarks.Bench

// ============================================================================
//  The measurement (Phase 2529). Runs in a real browser against React's
//  production profiling build; `drive.mjs` loads the bundle and calls
//  `window.__runBench`. README.md states the method and what each stage means.
//
//  It touches no renderer source. What the renderer does not expose in place is
//  measured over the surface's own inputs, off the render path, and reported
//  beside the in-place figures rather than added to them.
// ============================================================================

#if FABLE_COMPILER
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Benchmarks.Surfaces

[<Import("createRoot", "react-dom/client")>]
let private createRoot (container: Browser.Types.Element) : obj = jsNative

[<Import("flushSync", "react-dom")>]
let private flushSync (work: unit -> unit) : unit = jsNative

[<Import("createElement", "react")>]
let private createElement (kind: obj) (props: obj) (child: ReactElement) : ReactElement = jsNative

[<Import("Profiler", "react")>]
let private profilerKind: obj = jsNative

[<Emit("performance.now()")>]
let private now () : float = jsNative

[<Emit("(globalThis.gc ? globalThis.gc() : undefined)")>]
let private collectGarbage () : unit = jsNative

[<Emit("(_id, _phase, actual) => $0(actual)")>]
let private onRenderOf (record: float -> unit) : obj = jsNative

let mutable private renderPhaseMs = 0.0
let mutable private commitCount = 0

let private profiled (element: ReactElement) : ReactElement =
    let onRender =
        onRenderOf (fun actual ->
            renderPhaseMs <- renderPhaseMs + actual
            commitCount <- commitCount + 1)

    createElement profilerKind (createObj [ "id" ==> "bench"; "onRender" ==> onRender ]) element

let private resetProfile () =
    renderPhaseMs <- 0.0
    commitCount <- 0

let private dispatch: unit -> unit = ignore

let private write (surface: Surface) (value: obj) : unit =
    match surface.Channel with
    | StateWrite -> StateStore.set surface.Key value
    | FilterWrite -> FilterStore.set surface.Key value

let private liveSources () : BindingResolver.BindingSources =
    { BindingSources.empty with
        State = StateStore.overlayOnto Map.empty
        Filters = FilterStore.overlayOnto Map.empty }

let private newContainer () : Browser.Types.HTMLElement =
    let el = Browser.Dom.document.createElement "div"
    Browser.Dom.document.body.appendChild el |> ignore
    el

let private mount (element: ReactElement) : obj * Browser.Types.HTMLElement =
    let container = newContainer ()
    let root = createRoot container
    flushSync (fun () -> root?render (profiled element))
    // Mount effects, the store subscriptions among them, are settled before a sample.
    flushSync ignore
    root, container

let private unmount (root: obj, container: Browser.Types.HTMLElement) : unit =
    flushSync (fun () -> root?unmount ())
    container.remove ()

let private elementCount (container: Browser.Types.HTMLElement) : int = (container.querySelectorAll "*").length

/// Raw samples per column; `drive.mjs` summarises them.
let private sampled (samples: int) (body: unit -> (string * float) list) : obj =
    let columns = System.Collections.Generic.Dictionary<string, ResizeArray<float>>()

    // Warm-up: the JIT is a one-off cost no update pays.
    for _ in 1 .. max 10 (samples / 2) do
        body () |> ignore

    for _ in 1..samples do
        collectGarbage ()

        for (name, value) in body () do
            if not (columns.ContainsKey name) then
                columns[name] <- ResizeArray()

            columns[name].Add value

    createObj [ for kv in columns -> kv.Key, box (kv.Value.ToArray()) ]

/// One render off the page: what constructing `tree` costs by itself.
let private construct (sources: BindingResolver.BindingSources) (tree: Node<unit>) : float =
    let t0 = now ()
    Render.renderWithSources sources dispatch tree |> ignore
    now () - t0

// ─── The four operations ───────────────────────────────────────────────────

/// How much DOM one update touches: a separate unsampled pass, because a
/// MutationObserver is itself a cost on the path it watches.
[<Emit("new MutationObserver(() => {})")>]
let private newObserver () : obj = jsNative

let private observeOptions =
    createObj
        [ "subtree" ==> true
          "childList" ==> true
          "attributes" ==> true
          "characterData" ==> true ]

/// The records tallied by kind: "childList:DIV", "attributes:style", ...
[<Emit("$0.reduce((tally, r) => { const k = r.type + ':' + (r.attributeName ?? r.target.nodeName); tally[k] = (tally[k] ?? 0) + 1; return tally; }, { total: $0.length })")>]
let private tallied (records: obj array) : obj = jsNative

let private mutationsOf (container: Browser.Types.HTMLElement) (update: unit -> unit) : obj =
    let observer = newObserver ()
    observer?observe (container, observeOptions)
    update ()
    let records: obj array = observer?takeRecords ()
    observer?disconnect ()
    tallied records

let private fullRender (surface: Surface) (samples: int) : obj =
    let sources = liveSources ()

    sampled samples (fun () ->
        FragmentExpansion.clear ()
        let container = newContainer ()
        let root = createRoot container
        resetProfile ()
        let t0 = now ()
        let element = Render.renderWithSources sources dispatch surface.Tree
        let t1 = now ()
        flushSync (fun () -> root?render (profiled element))
        let t2 = now ()
        container.offsetHeight |> ignore
        let t3 = now ()
        let phase = renderPhaseMs
        let commits = commitCount
        let elements = elementCount container
        unmount (root, container)

        [ "total", t3 - t0
          "construct", t1 - t0
          "reactRender", phase
          "commit", t2 - t1 - phase
          "layout", t3 - t2
          "commits", float commits
          "elements", float elements ])

let private writeOperation (surface: Surface) (samples: int) (element: ReactElement) : obj =
    let first, second = surface.Values
    let mounted = mount element
    let container = snd mounted
    let mutable useSecond = true

    let pick () =
        let value = if useSecond then second else first
        useSecond <- not useSecond
        value

    let result =
        sampled samples (fun () ->
            let value = pick ()
            let before = container.textContent
            resetProfile ()
            let mutable fannedOut = 0.0
            let t0 = now ()

            flushSync (fun () ->
                write surface value
                fannedOut <- now ())

            let t1 = now ()
            container.offsetHeight |> ignore
            let t2 = now ()
            let phase = renderPhaseMs
            let commits = commitCount
            let moved = container.textContent <> before
            // Off the render path, after the commit: this write's construction, by itself.
            let constructed = construct (liveSources ()) surface.Tree

            [ "total", t2 - t0
              "fanOut", fannedOut - t0
              "reactRender", phase
              "commit", t1 - fannedOut - phase
              "layout", t2 - t1
              "constructOffPath", constructed
              "commits", float commits
              "domChanged", (if moved then 1.0 else 0.0) ])

    let mutations =
        mutationsOf container (fun () ->
            let value = pick ()
            flushSync (fun () -> write surface value))

    unmount mounted
    createObj [ "samples" ==> result; "mutations" ==> mutations ]

let private structural (surface: Surface) (samples: int) : obj =
    let sources = liveSources ()
    let mounted = mount (Render.renderWithSources sources dispatch surface.Tree)
    let root, container = mounted
    let mutable grow = true

    let next () =
        let tree = if grow then surface.Grown else surface.Tree
        grow <- not grow
        tree

    let result =
        sampled samples (fun () ->
            let tree = next ()
            let before = elementCount container
            resetProfile ()
            let t0 = now ()
            let element = Render.renderWithSources sources dispatch tree
            let t1 = now ()
            flushSync (fun () -> root?render (profiled element))
            let t2 = now ()
            container.offsetHeight |> ignore
            let t3 = now ()
            let phase = renderPhaseMs
            let commits = commitCount

            [ "total", t3 - t0
              "construct", t1 - t0
              "reactRender", phase
              "commit", t2 - t1 - phase
              "layout", t3 - t2
              "commits", float commits
              "elementsMoved", float (abs (elementCount container - before)) ])

    let mutations =
        mutationsOf container (fun () ->
            let element = Render.renderWithSources sources dispatch (next ())
            flushSync (fun () -> root?render (profiled element)))

    unmount mounted
    createObj [ "samples" ==> result; "mutations" ==> mutations ]

// ─── The stages the renderer does not expose in place ──────────────────────

let rec private nodesOf (node: Node<unit>) : Node<unit> list =
    node :: (StructuralQuery.children node |> List.collect nodesOf)

let private isolatedStages (surface: Surface) (samples: int) : obj =
    let nodes = nodesOf surface.Tree
    let sources = liveSources ()

    let fragments =
        nodes
        |> List.choose (fun n ->
            match n.Kind with
            | NodeKind.FragmentDecl spec -> Some(spec.Name, spec.Body)
            | _ -> None)
        |> Map.ofList

    let refs =
        nodes
        |> List.choose (fun n ->
            match n.Kind with
            | NodeKind.FragmentRef spec ->
                Map.tryFind spec.Name fragments
                |> Option.map (fun body -> FragmentExpansion.refPrefix n.Id, body)
            | _ -> None)

    let charts =
        nodes
        |> List.choose (fun n ->
            match n.Kind with
            | NodeKind.Chart spec -> Some spec
            | _ -> None)

    let rowsOf (spec: ChartSpec<unit>) : Row list =
        match BindingResolver.resolve<Row seq> sources spec.Source with
        | BindingResolver.Resolved rows -> List.ofSeq rows
        | other -> failwithf "chart %A did not resolve: %A" spec.Kind other

    // A reader subtree rendered alone still resolves its fragments against the whole tree.
    let wholeTreeContext = Render.RenderContext.create sources dispatch surface.Tree

    let resolved = charts |> List.map (fun spec -> spec, rowsOf spec)
    let textOf = BindingResolver.resolveTextSource sources

    let drawn =
        resolved
        |> List.map (fun (spec, rows) -> Fuaran.UI.Charts.renderSvg sources textOf spec rows)

    let timed (work: unit -> unit) : float =
        let t0 = now ()
        work ()
        now () - t0

    let baseline =
        sampled samples (fun () ->
            [ "keyWalks", timed (fun () -> Render.collectReactiveKeys surface.Tree |> ignore)
              "contextCreate", timed (fun () -> Render.RenderContext.create sources dispatch surface.Tree |> ignore)
              "fragmentExpansionCold",
              timed (fun () ->
                  for prefix, body in refs do
                      Render.expandFragmentUncached prefix body |> ignore)
              "fragmentExpansionMemo",
              timed (fun () ->
                  for prefix, body in refs do
                      Render.expandFragment prefix body |> ignore)
              "transformEvaluation",
              timed (fun () ->
                  for spec in charts do
                      rowsOf spec |> ignore)
              "chartLowering",
              timed (fun () ->
                  for spec, rows in resolved do
                      Fuaran.UI.Charts.renderSvg sources textOf spec rows |> ignore)
              "chartSinkSanitise",
              timed (fun () ->
                  for svg in drawn do
                      Sanitize.sanitizeMarkdownHtml svg |> ignore)
              "constructWhole", construct sources surface.Tree
              "constructReaders",
              timed (fun () ->
                  for reader in surface.Readers do
                      Render.render wholeTreeContext reader |> ignore)
              "constructWithoutCharts",
              (match surface.WithoutCharts with
               | Some tree -> construct sources tree
               | None -> nan) ])

    let keysRead (node: Node<unit>) : Set<string> =
        match surface.Channel with
        | StateWrite -> Render.collectStateKeys node
        | FilterWrite -> Render.collectFilterKeys node

    let topLevel = StructuralQuery.children surface.Tree

    createObj
        [ "samples" ==> baseline
          "nodes" ==> nodes.Length
          "fragmentRefs" ==> refs.Length
          "charts" ==> charts.Length
          "topLevelSubtrees" ==> topLevel.Length
          "subscribedSubtrees"
          ==> (topLevel
               |> List.filter (fun n -> keysRead n |> Set.contains surface.Key)
               |> List.length)
          "dependentSubtrees" ==> surface.Readers.Length
          "stateKeys" ==> (Render.collectStateKeys surface.Tree).Count
          "filterKeys" ==> (Render.collectFilterKeys surface.Tree).Count ]

let private seed (surface: Surface) : unit =
    StateStore.reset ()
    FilterStore.reset ()
    write surface (fst surface.Values)

let private runSurface (samples: int) (surface: Surface) : obj =
    seed surface
    let isolated = isolatedStages surface samples
    let full = fullRender surface samples

    let writeReactive =
        writeOperation surface samples (Render.renderStateReactive (liveSources ()) dispatch surface.Tree)

    let writePlain =
        writeOperation surface samples (Render.renderWithSources (liveSources ()) dispatch surface.Tree)

    let change = structural surface samples

    createObj
        [ "surface" ==> surface.Name
          "isolated" ==> isolated
          "fullRender" ==> full
          "writeReactive" ==> writeReactive
          "writePlain" ==> writePlain
          "structural" ==> change ]

let private runBench (samples: int) : obj =
    box (all |> List.map (runSurface samples) |> Array.ofList)

Browser.Dom.window?__runBench <- runBench
#endif
