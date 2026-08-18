module Fuaran.UI.Tests.SelectionLoop

// ============================================================================
//  Phase 427 — the selection loop: reactive `SelectionStore` + the decoded
//  `Binding.Selection` identity-accessor fix + the Selection reactivity
//  channel.
//
//  Asserts: a decoded `Selection` (identity accessor) resolves to the stored
//  row rather than a value-discarding placeholder (the 421 `Query` fix
//  replayed); the `SelectionStore` write/subscribe/snapshot triple works and
//  merges over `BindingSources.Selections`; a `Binding.Selection` reader
//  surfaces its producer id on the Selection reactivity channel and NOT on
//  the State/Filter channels.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
// Phase 933 leg 3 asserts a Chart draws no FUARAN071, which needs the defect DU.
open Fuaran.UI.PreEmitValidate

let private nn (v: 'T) : obj = box v |> Unchecked.nonNull

// The shape `JsonDecode` produces for a decoded `Selection` (Phase 427): an identity accessor.
let private decodedSelection: Binding<obj> =
    Binding.Selection("orders-grid", (fun (raw: obj) -> raw), None, None)

[<Tests>]
let tests =
    // Sequenced: this list writes the process-global `SelectionStore`, as does
    // the Phase 933 list below. Expecto runs lists in parallel by default, so
    // the `reset ()` here would otherwise race a sibling list mid-assertion.
    testSequenced
    <| testList
        "Phase 427 — selection loop"
        [ test "a decoded Selection resolves to the stored row (identity accessor)" {
              let sources =
                  { BindingResolver.empty with
                      Selections = Map.ofList [ NodeId "orders-grid", nn "row-7" ] }

              match BindingResolver.resolve sources decodedSelection with
              | BindingResolver.Resolved v ->
                  Expect.equal v (nn "row-7") "the stored row flows through the decoded Selection"
              | other -> failtestf "expected Resolved, got %A" other
          }

          test "an unwritten Selection is NotResolved (drives OnLoading / empty detail)" {
              match BindingResolver.resolve BindingResolver.empty decodedSelection with
              | BindingResolver.NotResolved -> ()
              | other -> failtestf "expected NotResolved, got %A" other
          }

          test "SelectionStore write → snapshot → keyed subscription round-trip" {
              SelectionStore.reset ()

              let mutable notified = 0

              let unsubscribe =
                  SelectionStore.subscribeKeys (Set.ofList [ "orders-grid" ]) (fun () -> notified <- notified + 1)

              SelectionStore.set "orders-grid" (nn 7)
              SelectionStore.set "other-grid" (nn 1)

              Expect.equal notified 1 "only the watched node id notifies"
              Expect.equal (SelectionStore.get "orders-grid") (Some(nn 7)) "the written row reads back"

              Expect.equal
                  (Map.tryFind "orders-grid" (SelectionStore.snapshot ()))
                  (Some(nn 7))
                  "the snapshot carries the written row"

              SelectionStore.clear "orders-grid"
              Expect.equal notified 2 "clearing notifies watchers"
              Expect.isNone (SelectionStore.get "orders-grid") "the cleared selection is gone"

              unsubscribe ()
              SelectionStore.reset ()
          }

          test "a Selection reader surfaces its producer id on the Selection channel only" {
              Expect.equal
                  (Render.selectionKeysOfBinding decodedSelection)
                  [ "orders-grid" ]
                  "the producer node id is the selection subscription key"

              Expect.isEmpty (Render.stateKeysOfBinding decodedSelection) "no State key"
              Expect.isEmpty (Render.filterKeysOfBinding decodedSelection) "no Filter key"
          }

          test "collectSelectionKeys finds a detail reader's producer id across the tree" {
              let detail =
                  Fuaran.metric
                      "detail"
                      { Defaults.metric with
                          Label = TextSource.Literal "Selected"
                          Value = Binding.Selection("orders-grid", (fun (raw: obj) -> unbox raw), None, None) }

              let tree =
                  Fuaran.dashboard
                      "root"
                      { Defaults.dashboard with
                          Children = [ detail ] }

              Expect.equal
                  (Render.collectSelectionKeys tree)
                  (Set.ofList [ "orders-grid" ])
                  "the tree walk collects the Selection producer id"
          } ]

// ============================================================================
//  Phase 933 — the chart point-selection default.
//
//  866 admitted census #22 as a REDIRECT rather than as vocabulary: a chart
//  whose `OnPointClick` is `None` publishes the clicked datum under its OWN
//  `NodeId` — Phase 427's grid default with `DataGrid` replaced by `Chart`, and
//  no wire field anywhere. `OnPointClick` cannot cross the wire
//  (`WireSurvivability`: `NodeKind.Chart -> None`), so before this every DECODED
//  chart's point click was inert.
//
//  The renderer's first-party chart path emits the lowered Drawing as a RAW SVG
//  STRING, so the click is delegated off Phase 642's `data-fuaran-mark` — which
//  makes `Charts.rowOfMarkId` (mark id -> row) and `Render.chartPointClick`
//  (row -> closure or store write) the whole of the behaviour, and both are
//  reachable without a browser. Asserted below: the mark-id resolver agrees
//  with what the lowering ACTUALLY emits, per arm (the anti-drift pin); a
//  decoded chart publishes; an explicit closure still wins and leaves the store
//  alone; and a second node reads the published selection with no extra wiring.
// ============================================================================

/// Rows carrying three x candidates, so ONE fixture exercises every key form the
/// lowering stamps: a string category (the band arms), a plain numeric (Scatter,
/// whose marks carry the canonical numeric form), and an ISO date (a TEMPORAL
/// scatter, whose marks carry the DAY NUMBER instead of the projected cell).
let private chartRows: Row list =
    [ Map.ofList
          [ "region", nn "North"
            "qty", nn 10.0
            "day", nn "2026-01-05"
            "sales", nn 120.0 ]
      Map.ofList
          [ "region", nn "South"
            "qty", nn 20.0
            "day", nn "2026-02-10"
            "sales", nn 80.0 ]
      Map.ofList
          [ "region", nn "East"
            "qty", nn 30.0
            "day", nn "2026-03-20"
            "sales", nn 45.0 ] ]

let private chartSpec (kind: ChartKind) (xField: string) (onPointClick: (Row -> Action<obj>) option) : ChartSpec<obj> =
    { Defaults.chart with
        Kind = kind
        Source = Binding.Static(Some(Seq.ofList chartRows))
        XField = xField
        YFields = [ "sales" ]
        OnPointClick = onPointClick }

let private styleOfShape (sh: Shape) : DrawStyle =
    match sh with
    | Shape.Group(_, s) -> s
    | Shape.Rectangle(_, _, _, _, _, s) -> s
    | Shape.Line(_, _, _, _, s) -> s
    | Shape.Polyline(_, s) -> s
    | Shape.Polygon(_, s) -> s
    | Shape.Curve(_, s) -> s
    | Shape.Circle(_, _, _, s) -> s
    | Shape.Ellipse(_, _, _, _, s) -> s
    | Shape.Label(_, _, _, s) -> s

/// The mark ids the lowering ACTUALLY stamps for a spec — read back off the
/// emitted drawing rather than predicted, so these tests pin the shipped
/// emission and not a second description of it.
let private emittedMarkIds (spec: ChartSpec<obj>) : string list =
    (Charts.lower spec chartRows).Shapes
    |> List.choose (fun sh -> (styleOfShape sh).MarkId)
    |> List.distinct

/// A `Selection` reader of the shape `JsonDecode` produces on the decoded path.
let private selectionReaderOf (producerId: string) : Binding<obj> =
    Binding.Selection(producerId, (fun (raw: obj) -> raw), None, None)

/// What the reactive host does each render (`Render.fs` — the Phase 427
/// "selection twin"): the live store snapshot merged into `BindingSources`.
let private sourcesFromStore () : BindingResolver.BindingSources =
    { BindingResolver.empty with
        Selections =
            SelectionStore.snapshot ()
            |> Map.toList
            |> List.map (fun (k, v) -> NodeId k, v)
            |> Map.ofList }

let private regionOf (v: obj) : string =
    BindingResolver.projectRowFieldString (unbox<Row> v) "region"

[<Tests>]
let chartPointSelectionTests =
    // Sequenced: `SelectionStore` is process-global and Expecto's default config
    // runs test lists in parallel — a sibling list's `reset ()` landing between a
    // write and its read here would fail a correct implementation.
    testSequenced
    <| testList
        "Phase 933 — chart point-selection default"
        [

          // ── The resolver agrees with the emission, per arm ─────────────────
          //
          // The anti-drift pin. `Charts.markCategoryKeys` MIRRORS four bindings
          // local to `lowerRows` that cannot be called from outside it, so the
          // mirror is held here rather than by a comment: change either
          // derivation and this goes red, instead of the chart silently ceasing
          // to select.
          test "every per-datum mark the lowering emits resolves back to its own row" {
              // Bar and Pie key by the PROJECTED x cell; Scatter keys by the
              // canonical numeric x instead.
              //
              // The TEMPORAL scatter is the case that makes the Scatter arm
              // testable at all, and it was added after the go-red proof caught
              // the omission: for a plain numeric x the projected form and the
              // canonical form COINCIDE (`string 10.0` and `formatNum 10.0` are
              // both "10"), so deleting the Scatter arm of `markCategoryKeys`
              // changed no result and the arm's own test proved nothing. On a
              // temporal x-scale the mark carries the DAY NUMBER while the band
              // arms label with the ISO string, and the two forms finally part.
              for label, spec in
                  [ "Bar", chartSpec ChartKind.Bar "region" None
                    "Pie", chartSpec ChartKind.Pie "region" None
                    "Scatter", chartSpec ChartKind.Scatter "qty" None
                    "Scatter (temporal)",
                    { chartSpec ChartKind.Scatter "day" None with
                        XScale = Some ChartXScale.Temporal } ] do
                  let perDatum = emittedMarkIds spec |> List.filter (fun m -> m.Contains "|")

                  Expect.isNonEmpty perDatum (sprintf "%s emits per-datum marks" label)

                  // Every emitted mark resolves, and each to a DISTINCT row — a
                  // resolver that always returned row 0 would pass "resolves to
                  // something" and fails this.
                  let resolved =
                      perDatum
                      |> List.map (fun m ->
                          match Charts.rowOfMarkId spec chartRows m with
                          | Some row -> row
                          | None -> failtestf "%s mark '%s' resolved to no row" label m)

                  Expect.equal
                      (List.length (List.distinct resolved))
                      (List.length chartRows)
                      (sprintf "%s: each of the three rows is reachable from its own mark" label)
          }

          test "a series-level mark resolves to no row rather than guessing one" {
              // Line and Area draw ONE polyline per series, so their marks carry
              // the series field alone — there is no datum under the pointer and
              // the honest answer is `None`, not row 0.
              for kind in [ ChartKind.Line; ChartKind.Area ] do
                  let spec = chartSpec kind "region" None
                  let ids = emittedMarkIds spec

                  Expect.isNonEmpty ids (sprintf "%A stamps series marks" kind)

                  Expect.isTrue
                      (ids |> List.forall (fun m -> not (m.Contains "|")))
                      (sprintf "%A emits series-level marks only" kind)

                  for m in ids do
                      Expect.isNone (Charts.rowOfMarkId spec chartRows m) "a series mark names no datum"
          }

          // ── The default write (the phase's whole point) ────────────────────
          test "a decoded chart's point click publishes the row under its own NodeId" {
              let nodeId = "p933-decoded-chart"
              SelectionStore.clear nodeId

              // The DECODED shape: `OnPointClick` arrives `None` because a
              // closure cannot cross the wire.
              let spec = chartSpec ChartKind.Bar "region" None
              let mutable dispatched = 0

              Render.chartPointClick (fun _ -> dispatched <- dispatched + 1) nodeId spec chartRows "sales|South"

              Expect.equal dispatched 0 "the default is a store write, not an action dispatch"

              match SelectionStore.get nodeId with
              | Some v -> Expect.equal (regionOf v) "South" "the CLICKED row is published, under the chart's own id"
              | None -> failtest "the decoded chart's point click published nothing — the affordance is still inert"

              SelectionStore.clear nodeId
          }

          test "an explicit OnPointClick still wins and leaves the store untouched" {
              let nodeId = "p933-closure-chart"
              SelectionStore.clear nodeId

              let mutable dispatchedActions: Action<obj> list = []

              let spec =
                  chartSpec ChartKind.Bar "region" (Some(fun (r: Row) -> Action.Navigate(regionOf (nn r))))

              Render.chartPointClick
                  (fun a -> dispatchedActions <- a :: dispatchedActions)
                  nodeId
                  spec
                  chartRows
                  "sales|East"

              match dispatchedActions with
              | [ Action.Navigate route ] -> Expect.equal route "East" "the closure saw the clicked row"
              | other -> failtestf "expected exactly one Navigate from the closure, got %A" other

              Expect.isNone
                  (SelectionStore.get nodeId)
                  "closure wins — a host-supplied OnPointClick never touches the store"

              SelectionStore.clear nodeId
          }

          test "a mark naming no row publishes nothing at all" {
              let nodeId = "p933-unmatched-chart"
              SelectionStore.clear nodeId

              let spec = chartSpec ChartKind.Bar "region" None

              // Stale markup, or a series mark: no datum, so no write.
              Render.chartPointClick ignore nodeId spec chartRows "sales|Nowhere"
              Render.chartPointClick ignore nodeId spec chartRows "sales"

              Expect.isNone (SelectionStore.get nodeId) "an unresolvable mark writes nothing"
          }

          // ── Crossfilter: does it FALL OUT, or does it need wiring? ─────────
          //
          // 933 claims a second node reads the chart's selection with no extra
          // wiring, via 818's read rule. Asserted here rather than assumed —
          // three legs, and all three are pre-existing machinery keyed by
          // `NodeId`, never by producer KIND.
          test "a second node reads the chart's selection with no extra wiring" {
              let chartId = "p933-crossfilter-chart"
              SelectionStore.clear chartId

              let spec = chartSpec ChartKind.Bar "region" None
              Render.chartPointClick ignore chartId spec chartRows "sales|North"

              // Leg 1 — the READ resolves, through the ordinary decoded
              // `Binding.Selection` accessor and the ordinary store merge.
              match BindingResolver.resolve (sourcesFromStore ()) (selectionReaderOf chartId) with
              | BindingResolver.Resolved v ->
                  Expect.equal (regionOf v) "North" "the detail node reads the chart's published row"
              | other -> failtestf "expected Resolved, got %A" other

              // Leg 2 — the REACTIVITY walk subscribes the reader to the chart,
              // so the click re-renders it. `selectionKeysOfBinding` keys off the
              // producer's NodeId and never inspects its kind.
              let detail =
                  Fuaran.metric
                      "p933-detail"
                      { Defaults.metric with
                          Label = TextSource.Literal "Selected region"
                          // The metric reads a float, so its accessor unboxes — the
                          // ordinary decoded shape, unchanged from 427.
                          Value = Binding.Selection(chartId, (fun (raw: obj) -> unbox raw), None, None) }

              let tree =
                  Fuaran.dashboard
                      "p933-root"
                      { Defaults.dashboard with
                          Children = [ Fuaran.chart chartId spec; detail ] }

              Expect.isTrue
                  (Set.contains chartId (Render.collectSelectionKeys tree))
                  "the reader subscribes to the chart's selection key"

              // Leg 3 — the VALIDATOR already treats a Chart as a selection
              // producer (`NodeCategory.Visualisation`), so the reader draws no
              // FUARAN071. Had it not, "no extra wiring" would have been false:
              // every crossfilter tree would emit a warning.
              match PreEmitValidate.validate tree with
              | Ok() -> ()
              | Error defects ->
                  Expect.isFalse
                      (defects
                       |> List.exists (fun d ->
                           match d with
                           | PreEmitDefect.SelectionOverNonProducer(_, target) -> target = chartId
                           | PreEmitDefect.DanglingSelection(_, target) -> target = chartId
                           | _ -> false))
                      (sprintf "a Chart is a selection producer; got %A" defects)

              SelectionStore.clear chartId
          } ]
