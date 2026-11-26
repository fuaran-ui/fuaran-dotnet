module Fuaran.UI.ServerDriven.Tests.RefreshMarksTests

// ============================================================================
//  Phase 2751 — the row delta of a live refresh reaches the renderer.
//
//  A session store's refresh is computed against a typed row delta; the store
//  now keeps that delta, read against the table the refresh produced, as the
//  site's `RefreshMarks`, and the renderer marks what it names under
//  `RenderPolicy.RefreshMarking`. The legs here pin the four claims the phase
//  makes:
//
//    1. Over the conformance corpus's edit streams, the cells a grid marks are
//       exactly the cells the refresh's delta names: the named rows' cells and
//       the invalidated columns' cells, located in the produced table. The
//       expected set is computed here from the corpus's own source and changed
//       tables (`Delta.diff`), not read back from the marks.
//    2. A refresh the seam declined, a reference-path refresh and a first prime
//       mark nothing. The corpus carries three declining vectors.
//    3. With the policy off, the server render is byte-identical to the render
//       without the setting, and nothing is planned.
//    4. The marks are not colour alone, say what changed in a polite live
//       region, and stop animating under reduced motion.
//
//  The corpus is READ (`LiveTransformCorpusTests.corpusCases`); nothing here
//  writes a fixture.
// ============================================================================

open System
open System.IO
open Expecto
open Fuaran.Core
open Fuaran.Compute
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.ServerDriven

let private stateKey = "orders"

let private liveState (t: Table) : Map<string, obj> =
    Map.ofList [ stateKey, (ColumnCodec.encodeJson (Embedded t) :> obj) ]

let private liveBinding<'T> (pipeline: Transform list) : Binding<'T> =
    Binding.Transform(
        TransformSource.Live(Binding.State(stateKey, None), Embedded { Schema = []; Columns = [] }),
        pipeline,
        None
    )

let private sourcesOver (store: ILiveTransformStore option) (state: Map<string, obj>) : BindingSources =
    { BindingSources.empty with
        State = state
        LiveTransforms = store }

let private rowsOf (sources: BindingSources) (binding: Binding<Row seq>) : Row list =
    match BindingResolver.resolve<Row seq> sources binding with
    | BindingResolver.Resolved rows -> List.ofSeq rows
    | other -> failtestf "the live Transform did not resolve: %A" other

let private marking =
    { RenderPolicy.defaults with
        RefreshMarking = RefreshMarkingPolicy.Mark }

let private column (name: string) (ty: ColumnType) (cells: Cell list) : Column =
    match Column.ofCells name ty cells with
    | Ok c -> c
    | Error e -> failwithf "column %s: %A" name e

let private orders (amounts: float list) : Table =
    { Schema = [ Field.create "id" StringType; Field.create "amount" FloatType ]
      Columns =
        [ column "id" StringType (amounts |> List.mapi (fun i _ -> Str(sprintf "o%d" i)))
          column "amount" FloatType (amounts |> List.map Float) ] }

/// The cells a grid marks: (result row, column) for every resolved row and every
/// column of the produced table, through the planner the client grid calls.
let private markedCells (sources: BindingSources) (binding: Binding<Row seq>) (columns: string list) =
    let rows = rowsOf sources binding

    let marker =
        ElementPlan.RefreshPlan.cellMarker
            (ElementPlan.RefreshPlan.forBinding marking sources binding |> Option.flatten)
            rows

    set
        [ for i, row in List.indexed rows do
              for c in columns do
                  if marker row (Some c) then
                      yield i, c ]

/// The cells the refresh's delta names, computed from the vector's two tables:
/// the source delta (`Delta.diff`, the delta the store refreshes a re-decoded
/// table against), and the oracle store's own account of whether the seam
/// restricted the refresh. A restricted refresh names the rows the delta adds or
/// changes, located in the produced table by their identity cell, and every cell
/// of a column the delta invalidates; a one-row result whose one group was
/// recomputed names its row. Anything else names nothing.
let private namedCells (key: string) (pipeline: Transform list) (source: Table) (changed: Table) =
    let oracle = LiveTransformStore(64, key)

    oracle.Evaluate("oracle", key, pipeline, source)
    |> Result.mapError failwith
    |> ignore

    let e =
        match oracle.Evaluate("oracle", key, pipeline, changed) with
        | Ok e -> e
        | Error m -> failtestf "the oracle refused: %s" m

    let idw = RowIdentity.byColumn key
    let result = e.Result
    let columns = Table.columnNames result
    let n = Table.rowCount result

    let restricted =
        match e.Footprint.Recompute with
        | RowsRecomputed _
        | GroupsRecomputed _ -> true
        | _ -> false

    let cells =
        match Delta.diff idw source changed with
        | Ok(RowSet rs) when restricted ->
            Expect.equal e.Delta (Diffed(RowSet rs)) "the store refreshed against the delta the tables carry"

            let named =
                rs.Rows
                |> List.choose (fun (r, c) ->
                    match r, c with
                    | ByKey k, (RowAdded | RowChanged) -> Some k
                    | _ -> None)
                |> Set.ofList

            let rowNamed i =
                if List.contains key columns then
                    idw.KeyOf result i
                    |> Option.exists (fun id -> Set.contains (idw.KeyString id) named)
                else
                    match e.Footprint.Recompute with
                    | GroupsRecomputed(_, g) -> g > 0 && n = 1 && not (Set.isEmpty named)
                    | _ -> false

            set
                [ for i in 0 .. n - 1 do
                      for c in columns do
                          if rowNamed i || List.contains c rs.InvalidatedColumns then
                              yield i, c ]
        | _ -> Set.empty

    cells, columns, restricted

[<Tests>]
let corpusTests =
    testList
        "Phase 2751 — the marked cells are the delta's cells over the corpus edit streams"
        [ let cases = LiveTransformCorpusTests.corpusCases ()

          yield!
              cases
              |> List.map (fun (name, pipeline, source, changed, key) ->
                  test name {
                      let store = LiveTransformStore(64, key)
                      let binding = liveBinding<Row seq> pipeline

                      let first = sourcesOver (Some store) (liveState source)
                      rowsOf first binding |> ignore

                      Expect.equal
                          (BindingResolver.refreshMarks first binding)
                          (Some None)
                          "a first prime is a reporting site that marks nothing"

                      let expected, columns, _ = namedCells key pipeline source changed
                      let second = sourcesOver (Some store) (liveState changed)
                      Expect.equal (markedCells second binding columns) expected "the marked cells are the named cells"
                  })

          test "the corpus marks cells, and a vector the seam declines would mark none" {
              let outcomes =
                  cases
                  |> List.map (fun (name, pipeline, source, changed, key) ->
                      let cells, _, restricted = namedCells key pipeline source changed
                      name, restricted, Set.count cells)

              Expect.isTrue
                  (outcomes |> List.exists (fun (_, _, n) -> n > 0))
                  "at least one vector marks cells, so the equality above is not over empty sets alone"

              // At this tier's Compute pin the seam restricts every corpus vector
              // over a store keyed by the vector's identity column — the three the
              // corpus records as declining (`join`, `window`, `ordinal`) are
              // answered incrementally here, which the corpus reader's own header
              // allows for. The rule still binds every vector.
              for name, restricted, n in outcomes do
                  if not restricted then
                      Expect.equal n 0 (sprintf "%s: a declined refresh names no cell" name)
          }

          test "a refresh the seam declines marks nothing, though the store held a delta" {
              // A second aggregating step: the seam's plan is the reference
              // evaluator, so the refresh falls back to a full evaluation.
              let pipeline =
                  [ GroupBy(
                        [ "id" ],
                        [ { Name = "amount"
                            Fn = Sum
                            Of = "amount" } ]
                    )
                    GroupBy(
                        [],
                        [ { Name = "total"
                            Fn = Sum
                            Of = "amount" } ]
                    ) ]

              match (Incremental.plan pipeline).Strategy with
              | ReferenceOnly _ -> ()
              | other -> failtestf "the probe pipeline must decline, and plans %A" other

              let source = orders [ 1.0; 2.0; 3.0 ]
              let changed = orders [ 1.0; 5.0; 3.0 ]
              let store = LiveTransformStore(64, "id")

              store.Evaluate("s", "id", pipeline, source) |> ignore

              match store.Evaluate("s", "id", pipeline, changed) with
              | Ok e ->
                  match e.Footprint.Recompute, e.Delta with
                  | FullRecompute _, Diffed(RowSet rs) when not (List.isEmpty rs.Rows) -> ()
                  | r, d -> failtestf "expected a declined refresh over a named delta, got %A / %A" r d
              | Error m -> failtestf "the store refused: %s" m

              Expect.isNone (store.LastRefresh "s") "the decline carries no marks"

              let binding = liveBinding<Row seq> pipeline
              let rendering = LiveTransformStore(64, "id")
              rowsOf (sourcesOver (Some rendering) (liveState source)) binding |> ignore
              let second = sourcesOver (Some rendering) (liveState changed)
              rowsOf second binding |> ignore
              Expect.equal (BindingResolver.refreshMarks second binding) (Some None) "and the renderer reads none"
              Expect.isEmpty (markedCells second binding [ "total" ]) "no cell is marked"
          }

          test "a store with no row identity declared refreshes by the reference path and marks nothing" {
              let name, pipeline, source, changed, _ =
                  cases |> List.find (fun (n, _, _, _, _) -> n = "point-edit-row-local")

              let store = LiveTransformStore(64, "")
              let binding = liveBinding<Row seq> pipeline
              rowsOf (sourcesOver (Some store) (liveState source)) binding |> ignore
              let second = sourcesOver (Some store) (liveState changed)
              rowsOf second binding |> ignore
              Expect.equal (BindingResolver.refreshMarks second binding) (Some None) name
          }

          test "a store that does not report is not a reporting site" {
              let _, pipeline, source, _, key =
                  cases |> List.find (fun (n, _, _, _, _) -> n = "point-edit-row-local")

              let inner = LiveTransformStore(64, key)

              let plain =
                  { new ILiveTransformStore with
                      member _.Evaluate(site, p, t) =
                          inner.Evaluate(site, key, p, t) |> Result.map _.Result }

              let sources = sourcesOver (Some plain) (liveState source)
              let binding = liveBinding<Row seq> pipeline
              rowsOf sources binding |> ignore
              Expect.isNone (BindingResolver.refreshMarks sources binding) "nothing to read"
              Expect.isNone (ElementPlan.RefreshPlan.forBinding marking sources binding) "nothing planned"
          } ]

// ─── The op-derived path ────────────────────────────────────────────────────

[<Tests>]
let opDerivedTests =
    testList
        "Phase 2751 — an edit recorded as a column op marks the row it edited"
        [ test "the edited row is marked, and only it" {
              let table = orders [ 10.0; 20.0; 30.0 ]
              let store = LiveTransformStore(64, "id")
              let log0 = GridEditLog.create (Actor.Human "refresh-marks-tests") 10 table
              let pipeline = [ Filter(Binary(Gt, Col "amount", Lit(Float 0.0))) ]
              let binding = liveBinding<Row seq> pipeline

              let sourcesAt (log: GridEditLog) =
                  sourcesOver (Some store) (Map.ofList [ stateKey, (GridEditLog.current log :> obj) ])

              rowsOf (sourcesAt log0) binding |> ignore

              let log1 =
                  match
                      LiveTransform.applyEdit
                          store
                          { Key = stateKey
                            RowKeyField = Some "id"
                            Op = SetCell("amount", 1, Float 25.0) }
                          log0
                  with
                  | Ok(log, _) -> log
                  | Error e -> failtestf "the edit refused: %A" e

              let second = sourcesAt log1

              Expect.equal
                  (markedCells second binding [ "id"; "amount" ])
                  (set [ 1, "id"; 1, "amount" ])
                  "the row the op named, and its cells"

              match BindingResolver.refreshMarks second binding |> Option.flatten with
              | Some m ->
                  Expect.equal (m.Added, m.Changed, m.Removed) (0, 1, 0) "one row changed"

                  Expect.equal
                      (BindingResolver.RefreshMarks.describe m)
                      "Updated: 1 row changed"
                      "the region's sentence"
              | None -> failtest "an op-derived restricted refresh carries marks"
          }

          test "the marks last for the refresh that produced the table, and the next refresh replaces them" {
              let table = orders [ 10.0; 20.0; 30.0 ]
              let store = LiveTransformStore(64, "id")
              let pipeline = [ Filter(Binary(Gt, Col "amount", Lit(Float 0.0))) ]
              let binding = liveBinding<Row seq> pipeline
              let at (t: Table) = sourcesOver (Some store) (liveState t)

              rowsOf (at table) binding |> ignore
              let edited = orders [ 10.0; 21.0; 30.0 ]
              let second = at edited
              rowsOf second binding |> ignore
              Expect.equal (markedCells second binding [ "id"; "amount" ]) (set [ 1, "id"; 1, "amount" ]) "row 1"

              // The same table again — an unrelated render — refreshes against
              // the quiet delta: it changed nothing, so it marks nothing.
              let third = at edited
              rowsOf third binding |> ignore
              Expect.equal (BindingResolver.refreshMarks third binding) (Some None) "a quiet refresh marks nothing"
          } ]

// ─── The metric, through the server renderer ───────────────────────────────

let private metricNode (binding: Binding<float>) : Node<obj> =
    let node = Fuaran.markdown "total" ""

    { node with
        Kind =
            NodeKind.Metric
                { Defaults.metric with
                    Label = TextSource.Literal "Total"
                    Value = binding } }

let private totalPipeline =
    [ GroupBy(
          [],
          [ { Name = "total"
              Fn = Sum
              Of = "amount" } ]
      ) ]

let private renderUnder (policy: RenderPolicy) (sources: BindingSources) (node: Node<obj>) : string =
    Fuaran.UI.Renderer.Server.Render.renderWithPolicy policy Fuaran.UI.Renderer.Server.Registry.empty sources node

[<Tests>]
let metricTests =
    testList
        "Phase 2751 — a metric marks a value its refresh recomputed"
        [ test "off, the render is byte-identical to the ordinary one, with a store furnished" {
              let store = LiveTransformStore(64, "id")
              let node = metricNode (liveBinding<float> totalPipeline)

              for t in [ orders [ 1.0; 2.0 ]; orders [ 1.0; 5.0 ] ] do
                  let sources = sourcesOver (Some store) (liveState t)
                  let off = renderUnder RenderPolicy.defaults sources node

                  Expect.equal
                      off
                      (Fuaran.UI.Renderer.Server.Render.render sources node)
                      "the default is the ordinary render"

                  Expect.isFalse (off.Contains("fuaran-refresh", StringComparison.Ordinal)) "no refresh class"

              Expect.equal RenderPolicy.defaults.RefreshMarking RefreshMarkingPolicy.Off "off by default"
          }

          test "on, a refresh that recomputed the value marks it and says so; the prime marks nothing" {
              let store = LiveTransformStore(64, "id")
              let node = metricNode (liveBinding<float> totalPipeline)
              let at (t: Table) = sourcesOver (Some store) (liveState t)

              let first = renderUnder marking (at (orders [ 1.0; 2.0; 3.0 ])) node

              Expect.stringContains
                  first
                  "<div class=\"fuaran-refresh-host\">"
                  "a reporting site renders its host from the first render"

              Expect.stringContains
                  first
                  "<span class=\"fuaran-refresh-status\" role=\"status\" aria-live=\"polite\"></span>"
                  "the live region exists, empty, before anything changes"

              Expect.isFalse
                  (first.Contains("fuaran-refresh-moved", StringComparison.Ordinal))
                  "the prime marks nothing"

              let second = renderUnder marking (at (orders [ 1.0; 7.0; 3.0 ])) node

              Expect.stringContains second "<div class=\"fuaran-refresh-host fuaran-refresh-moved\">" "the value moved"

              Expect.stringContains
                  second
                  "<span class=\"fuaran-refresh-status\" role=\"status\" aria-live=\"polite\">Value updated</span>"
                  "and the region says so"

              let off =
                  Fuaran.UI.Renderer.Server.Render.render (at (orders [ 1.0; 7.0; 3.0 ])) node

              Expect.equal
                  (second
                      .Replace("<div class=\"fuaran-refresh-host fuaran-refresh-moved\">", "")
                      .Replace(
                          "<span class=\"fuaran-refresh-status\" role=\"status\" aria-live=\"polite\">Value updated</span></div>",
                          ""
                      ))
                  off
                  "cut out, what remains is the metric's own markup, byte for byte"
          }

          test "on, a store with no row identity marks nothing: the reference path carries no delta" {
              let store = LiveTransformStore(64, "")
              let node = metricNode (liveBinding<float> totalPipeline)
              let at (t: Table) = sourcesOver (Some store) (liveState t)
              renderUnder marking (at (orders [ 1.0; 2.0 ])) node |> ignore
              let second = renderUnder marking (at (orders [ 1.0; 9.0 ])) node
              Expect.stringContains second "<div class=\"fuaran-refresh-host\">" "the site reports"
              Expect.isFalse (second.Contains("fuaran-refresh-moved", StringComparison.Ordinal)) "and marks nothing"
          } ]

// ─── The grid's live region and the stylesheet ─────────────────────────────

/// The canonical stylesheet, found by walking up from the test binary.
let private referenceCss () : string =
    let rec up (dir: DirectoryInfo | null) =
        match dir with
        | null -> failtest "the reference stylesheet is not above the test binary"
        | d ->
            let candidate =
                Path.Combine(d.FullName, "src", "Fuaran.UI.Renderer", "content", "fuaran-reference.css")

            if File.Exists candidate then
                File.ReadAllText candidate
            else
                up d.Parent

    up (DirectoryInfo AppContext.BaseDirectory)

/// The bodies of every `@media (prefers-reduced-motion: reduce)` block.
let private reducedMotionBlocks (css: string) : string list =
    let opener = "@media (prefers-reduced-motion: reduce) {"

    let rec blocks (from: int) acc =
        let i = css.IndexOf(opener, from, StringComparison.Ordinal)

        if i < 0 then
            List.rev acc
        else
            let start = i + opener.Length
            let mutable depth = 1
            let mutable j = start

            while depth > 0 && j < css.Length do
                match css.[j] with
                | '{' -> depth <- depth + 1
                | '}' -> depth <- depth - 1
                | _ -> ()

                j <- j + 1

            blocks j (css.Substring(start, j - start) :: acc)

    blocks 0 []

[<Tests>]
let presentationTests =
    testList
        "Phase 2751 — the marks are announced, not colour alone, and still under reduced motion"
        [ test "the grid's live region says what the delta named" {
              let marks: BindingResolver.RefreshMarks =
                  { Rows = set [ 0; 2 ]
                    Columns = Set.empty
                    Added = 1
                    Changed = 2
                    Removed = 1 }

              Expect.equal
                  (BindingResolver.RefreshMarks.describe marks)
                  "Updated: 2 rows changed, 1 row added, 1 row removed"
                  "the counts, in words"

              let inner: ElementPlan.Plan<unit> =
                  ElementPlan.Plan.classText "table" "fuaran-grid" ""

              match ElementPlan.RefreshPlan.aroundGrid (Some(Some marks)) inner with
              | ElementPlan.Plan.Element host ->
                  Expect.equal host.Props [ ElementPlan.Prop.Attr("class", "fuaran-refresh-host") ] "the host"

                  match host.Content with
                  | ElementPlan.Content.Children [ grid; ElementPlan.Plan.Element status ] ->
                      Expect.isTrue (obj.ReferenceEquals(grid, inner)) "the grid's own element, first"

                      Expect.equal
                          status.Props
                          [ ElementPlan.Prop.Attr("class", "fuaran-refresh-status")
                            ElementPlan.Prop.Attr("role", "status")
                            ElementPlan.Prop.Attr("aria-live", "polite") ]
                          "a polite status region"

                      Expect.equal
                          status.Content
                          (ElementPlan.Content.Text "Updated: 2 rows changed, 1 row added, 1 row removed")
                          "saying what changed"
                  | other -> failtestf "unexpected host content: %A" other
              | other -> failtestf "unexpected plan: %A" other

              Expect.isTrue
                  (obj.ReferenceEquals(ElementPlan.RefreshPlan.aroundGrid None inner, inner))
                  "a site that does not report is the grid itself"
          }

          test "a marked cell and a moved value are outlined, not only tinted" {
              let css = referenceCss ()

              let i =
                  css.IndexOf(".fuaran-grid-cell.fuaran-refresh-changed,", StringComparison.Ordinal)

              Expect.isGreaterThanOrEqual i 0 "the rule exists"
              let rule = css.Substring(i, css.IndexOf('}', i) - i)
              Expect.stringContains rule ".fuaran-refresh-moved .fuaran-metric-value" "it covers the moved value"
              Expect.stringContains rule "outline:" "an outline, which is a shape and not a colour alone"
              Expect.stringContains rule "animation: fuaran-refresh-fade" "the tint fades once"
          }

          test "under reduced motion the marks do not animate" {
              let blocks = reducedMotionBlocks (referenceCss ())
              Expect.isNonEmpty blocks "the probe finds the sheet's reduced-motion blocks"

              let ours =
                  blocks
                  |> List.filter (fun b -> b.Contains(".fuaran-refresh-changed", StringComparison.Ordinal))

              match ours with
              | [ block ] ->
                  Expect.stringContains block ".fuaran-refresh-moved .fuaran-metric-value" "the moved value too"
                  Expect.stringContains block "animation: none" "no animation"
              | other -> failtestf "expected one reduced-motion block for the marks, found %d" (List.length other)
          } ]
