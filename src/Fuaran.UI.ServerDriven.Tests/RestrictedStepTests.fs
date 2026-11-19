module Fuaran.UI.ServerDriven.Tests.RestrictedStepTests

// ============================================================================
//  Phase 1762 — where a server-driven state write spends its time.
//
//  The phase asked for a measurement before any mechanism: on a generated
//  24-site live board at 1,000 and 10,000 rows, how much of one `Driver` step
//  is VIEW, DIFF, binding RESOLUTION and LOWERING, and is resolution where the
//  time goes. Only if it is would a step restricted to the binding graph's
//  dirty set (`BindingGraph.dirty`) be the right work.
//
//  This file holds both halves of that answer:
//
//    * `measure`, the timing harness behind `docs/SERVER-DRIVEN-STEP-COST.md`.
//      It runs only when `FUARAN_STEP_COST` is set (timings in an ordinary test
//      run are noise and would make the suite slower for no assertion), and it
//      prints the table the document records.
//    * the structural legs below, which run on every gate. They pin the facts
//      the go / no-go stands on, so the decision goes red if the step changes
//      under it: a `Driver` step renders — and so resolves, and consults a
//      session's live-Transform store for — only the nodes its diff changed; a
//      bound state write the view does not reflect produces no op at all; and
//      on the board a one-key write's binding cone is the one reader of that
//      key while a write to the shared table is every site.
// ============================================================================

// `Action.Dispatch` is the in-process arm this tier's hand-authored driver
// exists to run; its deprecation note is about the wire, which no leg crosses.
#nowarn "44"

open System
open System.Diagnostics
open Expecto
open Fuaran.Core
open Fuaran.Compute
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Driver

// ─── The generated board ────────────────────────────────────────────────────
//
// Twenty-four sites over ONE shared orders table, the shape the live-dashboards
// target names: each site shows its target (a one-key read, `target-<i>`) and a
// league table, the top ten of its own orders by value (a live `Transform` over
// `$state.orders`). Deterministic data, so two runs time the same work.

let private siteCount = 24
let private topN = 10
let private ordersKey = "orders"
let private targetKey (site: int) = sprintf "target-%02d" site

let private valueOf (row: int) = float ((row * 7919) % 10000) / 10.0

let private ordersTable (rows: int) (edits: Map<int, float>) : Table =
    { Schema =
        [ Field.create "id" IntType
          Field.create "site" IntType
          Field.create "value" FloatType ]
      Columns =
        [ Column.ofInts "id" (Vector.init rows id) AllValid
          Column.ofInts "site" (Vector.init rows (fun i -> i % siteCount)) AllValid
          Column.ofFloats
              "value"
              (Vector.init rows (fun i -> Map.tryFind i edits |> Option.defaultWith (fun () -> valueOf i)))
              AllValid ] }

/// One site's league table: its own orders, highest value first, top ten.
let private sitePipeline (site: int) : Transform list =
    [ Filter(Binary(Eq, Col "site", Lit(Int site)))
      Sort [ Slot.Lit "value", Desc ]
      Limit(Slot.Lit topN, Slot.Lit 0) ]

let private siteBinding (site: int) : Binding<Row seq> =
    Binding.Transform(
        TransformSource.Live(Binding.State(ordersKey, None), Embedded { Schema = []; Columns = [] }),
        sitePipeline site,
        None
    )

type private Board =
    { Rows: int
      Edits: Map<int, float>
      Orders: Prepared
      Targets: Map<int, float> }

/// Box a value the type system cannot see is non-null.
let private nonNull (x: 'a) : obj = Unchecked.nonNull (box x)

type private BoardMsg =
    | SetTarget of site: int * value: float
    | EditRow of row: int * value: float

let private board (rows: int) : Board =
    { Rows = rows
      Edits = Map.empty
      Orders = DataFrame.prepare (ordersTable rows Map.empty)
      Targets = Map.ofList [ for i in 0 .. siteCount - 1 -> i, 100.0 ] }

let private update (msg: obj) (b: Board) : Board =
    match unbox<BoardMsg> msg with
    | SetTarget(site, v) ->
        { b with
            Targets = Map.add site v b.Targets }
    | EditRow(row, v) ->
        let edits = Map.add row v b.Edits

        { b with
            Edits = edits
            Orders = DataFrame.prepare (ordersTable b.Rows edits) }

/// The sources a board resolves against: the orders table as a prepared version
/// (the resolver reads a `Prepared` state value as is) and each site's target.
let private sourcesOf (store: Fuaran.UI.ILiveTransformStore option) (b: Board) : BindingSources =
    { BindingSources.empty with
        State =
            Map.ofList
                [ yield ordersKey, nonNull b.Orders
                  for KeyValue(site, v) in b.Targets do
                      yield targetKey site, nonNull v ]
        LiveTransforms = store }

/// RESOLUTION: evaluate every site's league table through the renderer's own
/// resolver — the work a step does to turn the board's bindings into values.
let private resolveSites (sources: BindingSources) : Row list array =
    Array.init siteCount (fun site ->
        match BindingResolver.resolve<Row seq> sources (siteBinding site) with
        | BindingResolver.Resolved rows -> List.ofSeq rows
        | other -> failwithf "site %d did not resolve: %A" site other)

let private cellText (row: Row) (field: string) : TextSource =
    TextSource.Literal(Map.tryFind field row |> Option.map string |> Option.defaultValue "")

let private metricNode (id: string) (value: Binding<float>) : Node<obj> =
    let node = Fuaran.markdown id ""

    { node with
        Kind =
            NodeKind.Metric
                { Defaults.metric with
                    Label = TextSource.Literal "Target"
                    Value = value } }

let private gridNode (id: string) (source: Binding<Row seq>) : Node<obj> =
    let node = Fuaran.table id Defaults.table

    match node.Kind with
    | NodeKind.DataGrid grid ->
        { node with
            Kind =
                NodeKind.DataGrid
                    { grid with
                        Source = source
                        StaticRows = None
                        Columns = [ TableColumn.text "id"; TableColumn.text "value" ] } }
    | _ -> node

let private siteCard (site: int) (target: Node<obj>) (table: Node<obj>) : Node<obj> =
    Fuaran.card
        (sprintf "site-%02d" site)
        { Defaults.card with
            Children =
                [ Fuaran.markdown (sprintf "site-%02d-title" site) (sprintf "Site %02d" site)
                  target
                  table ] }

/// VIEW, the half that is not resolution: the board's structure around values
/// already resolved. A hand-authored host bakes its values into the tree, so
/// the diff can see a change (`docs/SERVER_DRIVEN.md`, the binding-blind diff).
let private bakedView (b: Board) (resolved: Row list array) : Node<obj> =
    Fuaran.dashboard
        "board"
        { Defaults.dashboard with
            Children =
                [ for site in 0 .. siteCount - 1 ->
                      siteCard
                          site
                          (metricNode (sprintf "site-%02d-target" site) (Binding.Static(Some b.Targets[site])))
                          (Fuaran.table
                              (sprintf "site-%02d-top" site)
                              { Defaults.table with
                                  Headers = [ TextSource.Literal "id"; TextSource.Literal "value" ]
                                  Rows = [ for row in resolved[site] -> [ cellText row "id"; cellText row "value" ] ] }) ] }

/// The same board with its bindings LEFT in the tree — the shape the binding
/// graph reads, and the shape a host that keeps state in its sources holds.
let private boundView (_: Board) : Node<obj> =
    Fuaran.dashboard
        "board"
        { Defaults.dashboard with
            Children =
                [ for site in 0 .. siteCount - 1 ->
                      siteCard
                          site
                          (metricNode (sprintf "site-%02d-target" site) (Binding.State(targetKey site, None)))
                          (gridNode (sprintf "site-%02d-top" site) (siteBinding site)) ] }

/// The two writes a live board takes: one site's target (a one-key write read by
/// one site) and one order's value (a write to the table every site reads). The
/// edited row is site 5's best order, raised, so the write moves what site 5
/// shows and nothing else.
let private targetWrite: BoardMsg = SetTarget(3, 250.0)

let private rowWrite (rows: int) : BoardMsg =
    let site5 = [ 5..siteCount .. rows - 1 ]
    let best = site5 |> List.maxBy valueOf
    EditRow(best, valueOf best + 0.5)

// ─── The binding cone of a write ─────────────────────────────────────────────

/// The channel a board write lands on, as the binding graph names it.
let private inputsOf (msg: BoardMsg) : BindingGraph.Input list =
    match msg with
    | SetTarget(site, _) -> [ BindingGraph.Input.State(targetKey site) ]
    | EditRow _ -> [ BindingGraph.Input.State ordersKey ]

/// The reader node ids a write makes stale on the BOUND board.
let private coneReaders (msg: BoardMsg) (b: Board) : Set<string> =
    BindingGraph.dirty (BindingGraph.ofTree (boundView b)) (inputsOf msg)
    |> BindingGraph.sitesOf
    |> BindingGraph.readersOf

/// The site a board node id belongs to (`site-07-top` → 7).
let private siteOfId (id: string) : int = int (id.Substring(5, 2))

let private siteCards (tree: Node<obj>) : Node<obj> list =
    Fuaran.UI.NodeChildren.children Fuaran.UI.NodeChildren.Reach.kindHeld tree

// ─── The timing harness (FUARAN_STEP_COST=1) ────────────────────────────────

let private median (xs: float list) =
    let s = List.sort xs
    s[List.length s / 2]

let private warm, samples = 25, 51

/// Median wall time of `f` in milliseconds over `samples` runs after `warm`.
let private time (f: unit -> 'a) : float =
    for _ in 1..warm do
        f () |> ignore

    [ for _ in 1..samples ->
          let sw = Stopwatch.StartNew()
          f () |> ignore
          sw.Elapsed.TotalMilliseconds ]
    |> median

type private StageCost =
    { ResolveFull: float
      ResolveStore: float
      View: float
      Diff: float
      Lower: float
      Step: float
      Ops: int
      Patches: int
      Consulted: int
      Skipped: int
      SubtreesDiffed: int
      ResolveCone: float
      DiffCone: float
      Graph: float
      Identical: bool }

let private renderWith (sources: BindingSources) (n: Node<obj>) : string =
    Fuaran.UI.Renderer.Server.Render.render sources n

let private measureWrite (rows: int) (msg: BoardMsg) : StageCost =
    let before = board rows
    let after = update (nonNull msg) before

    // A session store primed over the board BEFORE the write, as a live session
    // holds one. Each sample advances a store primed the same way, primed
    // outside the timed region.
    let primedStore () =
        let store = LiveTransformStore(LiveTransformDefaults.Capacity, "id")

        resolveSites (sourcesOf (Some(store :> Fuaran.UI.ILiveTransformStore)) before)
        |> ignore

        store

    let resolveStore =
        [ for i in 1 .. warm + samples ->
              let store = primedStore ()
              let sources = sourcesOf (Some(store :> Fuaran.UI.ILiveTransformStore)) after
              let sw = Stopwatch.StartNew()
              resolveSites sources |> ignore
              i, sw.Elapsed.TotalMilliseconds ]
        |> List.filter (fun (i, _) -> i > warm)
        |> List.map snd
        |> median

    let resolvedBefore = resolveSites (sourcesOf None before)
    let resolvedAfter = resolveSites (sourcesOf None after)
    let oldTree = bakedView before resolvedBefore
    let newTree = bakedView after resolvedAfter
    let ops = TreeOpDiff.diff oldTree newTree
    let render = renderWith BindingSources.empty

    let session: LiveSession<Board, obj> =
        { Model = before
          Tree = oldTree
          Update = update
          View = fun b -> bakedView b (resolveSites (sourcesOf None b))
          Services = DriverServices.createPermissive render }

    let _, out = applyResolvedActions session "board" [ Action.Dispatch(nonNull msg) ]

    // The counterfactual restricted step: resolve only the league tables in the
    // write's cone, diff only the site cards holding a cone reader. Measured,
    // not built — and checked against the full path, so a saving read off a
    // restriction that changed the answer cannot be reported as one.
    let cone = coneReaders msg before
    let coneSites = cone |> Set.map siteOfId
    let coneTables = cone |> Set.filter (fun r -> r.EndsWith "-top") |> Set.map siteOfId

    let resolveCone () =
        let sources = sourcesOf None after

        [ for site in coneTables ->
              match BindingResolver.resolve<Row seq> sources (siteBinding site) with
              | BindingResolver.Resolved rows -> List.ofSeq rows
              | other -> failwithf "site %d did not resolve: %A" site other ]

    let oldCards = siteCards oldTree
    let newCards = siteCards newTree

    let diffCone () =
        [ for site in coneSites -> TreeOpDiff.diff oldCards[site] newCards[site] ]
        |> List.concat

    let identical =
        Lowering.lowerScript render oldTree (diffCone ()) = Lowering.lowerScript render oldTree ops

    { ResolveFull = time (fun () -> resolveSites (sourcesOf None after))
      ResolveStore = resolveStore
      View = time (fun () -> bakedView after resolvedAfter)
      Diff = time (fun () -> TreeOpDiff.diff oldTree newTree)
      Lower = time (fun () -> Lowering.lowerScript render oldTree ops)
      Step = time (fun () -> applyResolvedActions session "board" [ Action.Dispatch(nonNull msg) ])
      Ops = List.length ops
      Patches = List.length out.Patches
      Consulted = Set.count coneTables
      Skipped = siteCount - Set.count coneTables
      SubtreesDiffed = Set.count coneSites
      ResolveCone = time resolveCone
      DiffCone = time diffCone
      Graph = time (fun () -> coneReaders msg before)
      Identical = identical }

/// Print the breakdown `docs/SERVER-DRIVEN-STEP-COST.md` records. Not a test:
/// the timings it prints are evidence for the document, never an assertion.
let measure () =
    printfn
        "| rows | write | resolve (full) | resolve (session store) | view | diff | lower | step | ops / patches | tables consulted / skipped | cards diffed | resolve (cone) | diff (cone) | graph + cone | cone patches identical |"

    printfn "|---:|---|---:|---:|---:|---:|---:|---:|---|---|---:|---:|---:|---:|---|"

    for rows in [ 1000; 10000 ] do
        for name, msg in [ "one key (target-03)", targetWrite; "one order (site 05)", rowWrite rows ] do
            let c = measureWrite rows msg

            printfn
                "| %d | %s | %.3f | %.3f | %.3f | %.3f | %.3f | %.3f | %d / %d | %d / %d | %d | %.3f | %.3f | %.3f | %b |"
                rows
                name
                c.ResolveFull
                c.ResolveStore
                c.View
                c.Diff
                c.Lower
                c.Step
                c.Ops
                c.Patches
                c.Consulted
                c.Skipped
                c.SubtreesDiffed
                c.ResolveCone
                c.DiffCone
                c.Graph
                c.Identical

// ─── The structural legs (every gate) ───────────────────────────────────────

/// A board whose league tables stay BOUND in the tree and are resolved by the
/// session's render, through the session's own live-Transform store, while
/// each site's target is baked by the view — the mixed shape a host composing
/// with `LiveTransform.initSession` holds.
let private mixedView (b: Board) : Node<obj> =
    Fuaran.dashboard
        "board"
        { Defaults.dashboard with
            Children =
                [ for site in 0 .. siteCount - 1 ->
                      siteCard
                          site
                          (metricNode (sprintf "site-%02d-target" site) (Binding.Static(Some b.Targets[site])))
                          (gridNode (sprintf "site-%02d-top" site) (siteBinding site)) ] }

/// Compose a mixed-board session over the host's own state cell, with a render
/// that records every node the lowering asks it for.
let private mixedSession (cell: Board ref) =
    let rendered = ResizeArray<string>()

    let render (sources: BindingSources) (n: Node<obj>) : string =
        rendered.Add n.Id
        renderWith sources n

    let session, store =
        LiveTransform.initSession
            (LiveTransformOptions.identifiedBy "id")
            (fun () -> sourcesOf None cell.Value)
            render
            DriverServices.createPermissive
            update
            mixedView
            cell.Value

    // Prime as a live session is primed: the first full render consults the
    // store once per league table.
    session.Services.RenderFragment session.Tree |> ignore
    rendered.Clear()
    session, store, rendered

let private consults (store: LiveTransformStore) = store.Hits + store.Misses

[<Tests>]
let restrictedStepTests =
    testList
        "Phase 1762 — the server-driven step and the binding cone"
        [ test "a one-key write's cone is the one reader of that key, and nothing else" {
              let readers = coneReaders targetWrite (board 1000)
              Expect.equal readers (Set.singleton "site-03-target") "target-03 is read by site 3's metric alone"

              match BindingGraph.dirty (BindingGraph.ofTree (boundView (board 1000))) (inputsOf targetWrite) with
              | BindingGraph.Dirty.Exact _ -> ()
              | other -> failtestf "the board holds no opaque read, so the verdict is exact: %A" other
          }

          test "a write to the shared table puts every league table in the cone" {
              // The dominant write on a live board is a data write, and every
              // site reads the one orders table, so the cone is the whole board
              // even though only site 5's top ten moves. The graph is keyed by
              // channel, not by row: this is why restricting by the cone buys
              // nothing on the write a live board takes most.
              let readers = coneReaders (rowWrite 1000) (board 1000)

              Expect.equal
                  readers
                  (Set.ofList [ for site in 0 .. siteCount - 1 -> sprintf "site-%02d-top" site ])
                  "every site's league table reads $state.orders"
          }

          test "a baked board carries no binding the cone can name" {
              // A hand-authored view bakes what it resolved into the tree (the
              // diff is binding-blind), so the tree the Driver holds names no
              // state channel: the cone of every write over it is EMPTY. A step
              // restricted by that cone would skip the very site that changed.
              let b = board 1000
              let baked = bakedView b (resolveSites (sourcesOf None b))
              let graph = BindingGraph.ofTree baked

              for msg in [ targetWrite; rowWrite 1000 ] do
                  Expect.isEmpty
                      (BindingGraph.dirty graph (inputsOf msg) |> BindingGraph.sitesOf)
                      (sprintf "no site of the baked board reads the channel %A writes" msg)
          }

          test "a Driver step renders, and consults the store for, only the nodes its diff changed" {
              // The restriction the phase proposed — clean sites skip their
              // store consult — is what the hand-authored step already does: the
              // lowering renders a fragment per CHANGED node, and a league table
              // whose node did not change is never rendered, so never resolved.
              let cell = ref (board 1000)
              let session, store, rendered = mixedSession cell
              let primed = consults store
              Expect.equal primed siteCount "priming consulted the store once per league table"

              cell.Value <- update (nonNull targetWrite) cell.Value

              let _, out =
                  applyResolvedActions session "board" [ Action.Dispatch(nonNull targetWrite) ]

              Expect.equal (List.ofSeq rendered) [ "site-03-target" ] "the step rendered the one node it changed"
              Expect.hasLength out.Patches 1 "one patch, for that node"
              Expect.equal (consults store) primed "and consulted the store for no league table"
          }

          test "a bound write the view does not reflect produces no op" {
              // The other half of why the cone cannot drive this step: a write
              // that lands only in the host's sources moves no node of the tree
              // the view returns, so the diff — binding-blind — emits nothing and
              // nothing is rendered. A host that wants the table to move bakes it.
              let cell = ref (board 1000)
              let session, store, rendered = mixedSession cell
              let primed = consults store
              let msg = rowWrite 1000

              cell.Value <- update (nonNull msg) cell.Value
              let _, out = applyResolvedActions session "board" [ Action.Dispatch(nonNull msg) ]

              Expect.isEmpty out.Patches "the step patched nothing"
              Expect.isEmpty rendered "rendered nothing"
              Expect.equal (consults store) primed "and consulted the store for nothing"
          }

          test "the step cost harness runs when asked" {
              match Environment.GetEnvironmentVariable "FUARAN_STEP_COST" with
              | null
              | "" -> skiptest "set FUARAN_STEP_COST=1 to print the step cost breakdown"
              | _ -> measure ()
          } ]
