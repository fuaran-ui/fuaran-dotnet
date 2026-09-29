module Fuaran.UI.Tests.GridWindow

// ============================================================================
//  Phase 1892 — the DataGrid row window (`windowStateKey`) and the declared
//  total (`rowTotal`).
//
//  Two halves. The corpus half runs the `grid-window/` behaviour vectors — whose
//  expected answers the corpus computes from the specification's rules, not from
//  this host — through THIS host's own descriptor reader, grid sort, page slice
//  and window function, so the reference agrees with the specification rather
//  than the specification being read off the reference. The unit half pins the
//  pieces the vectors cannot reach: the State-store read, the declared-total
//  resolution over each binding shape, the who-slices test, the ARIA
//  annotations, and the page rule's new use of a declared total.
// ============================================================================

open System.IO
open System.Text.Json
open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

let private nn (x: 'a) : obj = box x |> Unchecked.nonNull

let private field (label: string) (name: string) : ColumnErased<obj> =
    { Label = label
      Value = None
      Field = Some name
      Sortable = None
      Editable = None
      Format = CellFormat.None
      Kind = CellKindErased.Text
      Width = ColumnWidth.Auto }

let private rowOf (el: JsonElement) : Row =
    el.EnumerateObject()
    |> Seq.map (fun p ->
        let v: obj =
            match p.Value.ValueKind with
            | JsonValueKind.String -> nn (p.Value.GetString() |> Option.ofObj |> Option.defaultValue "")
            | JsonValueKind.Number -> nn (p.Value.GetDouble())
            | _ -> nn (p.Value.GetRawText())

        p.Name, v)
    |> Map.ofSeq

let private idsOf (rows: Row list) : string list =
    rows |> List.map (fun r -> string r["id"])

let private jvalOf (el: JsonElement) : JVal =
    match Json.parse (el.GetRawText()) with
    | Ok jv -> jv
    | Error e -> failtestf "vector JSON did not parse as a JVal: %s" e

/// Run one vector through this host's own pieces, exactly as the family's
/// description prescribes.
let private run (input: JsonElement) : BindingResolver.PresentedWindow<Row> =
    let slicing = input.GetProperty("slicing").GetString()

    let columns =
        [ for c in input.GetProperty("columns").EnumerateArray() -> field "" (c.GetString() |> string) ]

    let rows = [ for r in input.GetProperty("rows").EnumerateArray() -> rowOf r ]

    let sorted =
        match input.TryGetProperty "sort" with
        | true, s when s.ValueKind = JsonValueKind.Object ->
            let dir =
                if s.GetProperty("direction").GetString() = "desc" then
                    SortDirection.Desc
                else
                    SortDirection.Asc

            BindingResolver.sortRowsByDescriptor columns (Some(s.GetProperty("column").GetInt32(), dir)) rows
        | _ -> rows

    let range =
        match slicing, input.TryGetProperty "page" with
        | "client", (true, p) when p.ValueKind = JsonValueKind.Object ->
            BindingResolver.sliceRowsToPage (p.GetProperty("size").GetInt32()) (p.GetProperty("page").GetInt32()) sorted
        | _ -> sorted

    // The raw descriptor goes through the STORE read, as the renderer's does.
    let windowKey = "vector-window"
    let totalKey = "vector-total"

    let state =
        [ match input.TryGetProperty "window" with
          | true, w -> windowKey, nn (jvalOf w)
          | _ -> ()
          match input.TryGetProperty "rowTotal" with
          | true, t -> totalKey, nn (jvalOf t)
          | _ -> () ]
        |> Map.ofList

    let sources =
        { BindingResolver.empty with
            State = state }

    let hostWindows = slicing = "hostWindows"

    let declared =
        if hostWindows then
            BindingResolver.resolveRowTotal sources (Some(Binding.State(totalKey, None)))
        else
            None

    BindingResolver.presentWindow hostWindows declared (BindingResolver.readWindowDescriptor sources windowKey) range

let private corpusVectors () : (string * JsonElement * JsonElement) list =
    match Fuaran.Tests.CorpusRoot.tryFind () with
    | None -> []
    | Some root ->
        let path = Path.Combine(root, "grid-window", "grid-window-vectors.json")

        if not (File.Exists path) then
            failtestf
                "the corpus at %s holds no grid-window/grid-window-vectors.json — a behaviour family cannot be certified by reading nothing"
                root

        let doc = JsonDocument.Parse(File.ReadAllText path)

        [ for v in doc.RootElement.GetProperty("vectors").EnumerateArray() ->
              v.GetProperty("id").GetString() |> string,
              v.GetProperty("input").Clone(),
              v.GetProperty("expected").Clone() ]

/// The erased grid the renderer reads, built through the typed facade — which is
/// also what shows the two new slots survive `Fuaran.grid`'s erasure.
let private erased (facade: GridSpecOf<Row, obj>) : DataGridSpec<obj> =
    match (Fuaran.grid "g" id facade).Kind with
    | NodeKind.DataGrid spec -> spec
    | other -> failtestf "Fuaran.grid built %A, not a DataGrid" other

let private queryGrid
    (dependsOn: string list)
    (window: string option)
    (total: Binding<int> option)
    : DataGridSpec<obj> =
    erased
        { Defaults.grid<Row, obj> with
            Source = Binding.Query("orders", unbox, Some dependsOn)
            WindowStateKey = window
            RowTotal = total }

[<Tests>]
let tests =
    testList
        "Fuaran.UI.GridWindow"
        [ testCase "the grid-window corpus vectors agree with this host's window function"
          <| fun () ->
              match corpusVectors () with
              | [] -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | vectors ->
                  Expect.isGreaterThanOrEqual vectors.Length 20 "the family carries its full vector set"

                  for (id, input, expected) in vectors do
                      let got = run input
                      Expect.equal got.Windowed (expected.GetProperty("windowed").GetBoolean()) $"{id}: windowed"
                      Expect.equal got.Offset (expected.GetProperty("offset").GetInt32()) $"{id}: offset"

                      Expect.equal
                          (idsOf got.Rows)
                          [ for r in expected.GetProperty("rowIds").EnumerateArray() -> r.GetString() |> string ]
                          $"{id}: presented rows"

                      let total =
                          match expected.GetProperty("total") with
                          | t when t.ValueKind = JsonValueKind.Null -> None
                          | t -> Some(t.GetInt32())

                      Expect.equal got.Total total $"{id}: total"

          testList
              "descriptor"
              [ test "an absent key reads as no window" {
                    Expect.isNone (BindingResolver.readWindowDescriptor BindingResolver.empty "w") "absent"
                }
                test "a descriptor stored as a JSON element string-free object reads through the store lift" {
                    let sources =
                        { BindingResolver.empty with
                            State = Map.ofList [ "w", nn (JObj [ "offset", JInt 3; "count", JInt 7 ]) ] }

                    Expect.equal
                        (BindingResolver.readWindowDescriptor sources "w")
                        (Some
                            { BindingResolver.RowWindow.Offset = 3
                              BindingResolver.RowWindow.Count = 7 })
                        "a usable descriptor"
                }
                test "an F# map written by the renderer's SetState path reads the same" {
                    let sources =
                        { BindingResolver.empty with
                            State = Map.ofList [ "w", nn (Map.ofList [ "offset", nn 4; "count", nn 2 ]) ] }

                    Expect.equal
                        (BindingResolver.readWindowDescriptor sources "w")
                        (Some
                            { BindingResolver.RowWindow.Offset = 4
                              BindingResolver.RowWindow.Count = 2 })
                        "the store lift is total over the spellings a store holds"
                } ]

          testList
              "rowTotal"
              [ test "a Static total resolves" {
                    Expect.equal
                        (BindingResolver.resolveRowTotal BindingResolver.empty (Some(Binding.Static(Some 42))))
                        (Some 42)
                        "static"
                }
                test "a Query total populated as a float resolves as an integer" {
                    let sources =
                        { BindingResolver.empty with
                            QueryResults = Map.ofList [ "orders.total", nn 1200.0 ] }

                    Expect.equal
                        (BindingResolver.resolveRowTotal sources (Some(Binding.Query("orders.total", unbox, None))))
                        (Some 1200)
                        "an integral float is an integer"
                }
                test "a negative or fractional total is no total" {
                    let sources =
                        { BindingResolver.empty with
                            State = Map.ofList [ "neg", nn -1; "frac", nn 2.5 ] }

                    Expect.isNone
                        (BindingResolver.resolveRowTotal sources (Some(Binding.State("neg", None))))
                        "negative"

                    Expect.isNone
                        (BindingResolver.resolveRowTotal sources (Some(Binding.State("frac", None))))
                        "fractional"
                }
                test "an unpopulated total is unknown, and no binding is no total" {
                    Expect.isNone
                        (BindingResolver.resolveRowTotal BindingResolver.empty (Some(Binding.Query("t", unbox, None))))
                        "unpopulated"

                    Expect.isNone (BindingResolver.resolveRowTotal BindingResolver.empty None) "absent"
                } ]

          testList
              "who slices"
              [ test "a Query depending on the window key windows host-side" {
                    Expect.isTrue (BindingResolver.gridHostWindows (queryGrid [ "w" ] (Some "w") None)) "host windows"
                }
                test "a Query depending on the page key only does not window host-side" {
                    Expect.isFalse
                        (BindingResolver.gridHostWindows (queryGrid [ "p" ] (Some "w") None))
                        "client windows"
                }
                test "a grid naming no window key is untouched" {
                    let spec = queryGrid [] None None
                    Expect.isFalse (BindingResolver.gridHostWindows spec) "no window key"
                    Expect.isNone (BindingResolver.gridWindow BindingResolver.empty spec [ 1; 2; 3 ]) "no window at all"
                }
                test "the declared total is read only where the host windows" {
                    let sources =
                        { BindingResolver.empty with
                            State = Map.ofList [ "w", nn (JObj [ "offset", JInt 0; "count", JInt 2 ]) ] }

                    let clientSpec =
                        erased
                            { Defaults.grid<Row, obj> with
                                Source = Binding.State("rows", None)
                                WindowStateKey = Some "w"
                                RowTotal = Some(Binding.Static(Some 999)) }

                    let client = BindingResolver.gridWindow sources clientSpec [ 1; 2; 3; 4 ]
                    Expect.equal (client |> Option.map _.Total) (Some(Some 4)) "a client-sliced grid counts itself"

                    let host =
                        BindingResolver.gridWindow
                            sources
                            (queryGrid [ "w" ] (Some "w") (Some(Binding.Static(Some 999))))
                            [ 1; 2 ]

                    Expect.equal
                        (host |> Option.map _.Total)
                        (Some(Some 999))
                        "a host-windowed grid reads the declared total"
                } ]

          testList
              "aria"
              [ test "a windowed grid annotates the slice; an unwindowed one does not" {
                    let w =
                        Some(
                            BindingResolver.presentWindow
                                false
                                None
                                (Some
                                    { BindingResolver.RowWindow.Offset = 10
                                      BindingResolver.RowWindow.Count = 5 })
                                [ 1..40 ]
                        )

                    Expect.equal (BindingResolver.windowRowCount w) (Some 41) "total plus the header row"
                    Expect.equal (BindingResolver.windowRowIndex w 0) (Some 12) "offset 10, row 0, header is row 1"

                    let none = Some(BindingResolver.presentWindow false None None [ 1..40 ])
                    Expect.isNone (BindingResolver.windowRowCount none) "no window, no attribute"
                    Expect.isNone (BindingResolver.windowRowIndex none 0) "no window, no attribute"
                }
                test "an unknown total is aria-rowcount -1" {
                    let w =
                        Some(
                            BindingResolver.presentWindow
                                true
                                None
                                (Some
                                    { BindingResolver.RowWindow.Offset = 0
                                      BindingResolver.RowWindow.Count = 5 })
                                [ 1..5 ]
                        )

                    Expect.equal (BindingResolver.windowRowCount w) (Some -1) "unknown"
                } ]

          testList
              "pre-emit"
              [ test "a host-slicing Query naming the grid's own window or page key is not a dangling filter" {
                    let grid =
                        Fuaran.grid
                            "orders-grid"
                            id
                            { Defaults.grid<Row, obj> with
                                Source =
                                    Binding.Query("orders", unbox, Some [ "orders-window"; "orders-page"; "region" ])
                                RowKeyField = Some "id"
                                WindowStateKey = Some "orders-window"
                                PageStateKey = Some "orders-page" }

                    let dangling =
                        match PreEmitValidate.validate grid with
                        | Ok() -> []
                        | Error defects ->
                            defects
                            |> List.choose (function
                                | PreEmitValidate.PreEmitDefect.DanglingFilterReference(_, name) -> Some name
                                | _ -> None)

                    Expect.equal
                        dangling
                        [ "region" ]
                        "the grid's own window and page keys are the re-run edge; an undeclared filter still fires"
                } ]

          testList
              "host-paged total"
              [ test "a host-paged grid with a declared total names and clamps to its last page" {
                    let spec =
                        erased
                            { Defaults.grid<Row, obj> with
                                Source = Binding.Query("orders", unbox, Some [ "p" ])
                                PageStateKey = Some "p"
                                PageSize = Some 10
                                RowTotal = Some(Binding.Static(Some 45)) }

                    let sources =
                        { BindingResolver.empty with
                            State = Map.ofList [ "p", nn (JObj [ "page", JInt 9 ]) ] }

                    Expect.equal
                        (BindingResolver.gridPage sources spec 10)
                        (Some("p", 10, 5, true, Some 5))
                        "page 9 clamps to 5 of 5"
                }
                test "without a declared total the host-paged grid keeps to previous/next" {
                    let spec =
                        erased
                            { Defaults.grid<Row, obj> with
                                Source = Binding.Query("orders", unbox, Some [ "p" ])
                                PageStateKey = Some "p"
                                PageSize = Some 10 }

                    let sources =
                        { BindingResolver.empty with
                            State = Map.ofList [ "p", nn (JObj [ "page", JInt 9 ]) ] }

                    Expect.equal
                        (BindingResolver.gridPage sources spec 10)
                        (Some("p", 10, 9, true, None))
                        "no last page"
                } ] ]

// ============================================================================
//  Phase 1911 — the viewport's WRITE half on the reference host. The Fable
//  viewport in `Render.fs` runs `BindingResolver.stepWindowWriter` on every
//  scroll, resize and render; these tests drive the same function with a
//  scripted sequence of measurements, reflecting each write back into State the
//  way `SetState` does, so the descriptor sequence a scroll produces is pinned
//  on .NET without a DOM.
// ============================================================================

/// A measurement at `scrollTop` over 32px rows under a 40px header, in a
/// viewport `height` pixels tall.
let private at (scrollTop: float) (height: float) : BindingResolver.ViewportMeasure =
    { ScrollTop = scrollTop
      HeaderHeight = 40.0
      RowHeight = 32.0
      ViewportHeight = height }

/// Run a scripted scroll: each measurement steps the writer, and each write is
/// reflected into the window State holds, as the renderer's `SetState` does.
/// Returns every write in order.
let private script
    (windowKey: string option)
    (seeded: BindingResolver.RowWindow option)
    (measures: BindingResolver.ViewportMeasure list)
    : (string * JVal) list =
    let folder (current, lastWritten, writes) measure =
        let written, write =
            BindingResolver.stepWindowWriter windowKey current lastWritten measure

        match write with
        | Some(key, descriptor) ->
            let held =
                BindingResolver.readWindowDescriptor
                    { BindingResolver.empty with
                        State = Map.ofList [ key, nn descriptor ] }
                    key

            held, written, writes @ [ key, descriptor ]
        | None -> current, written, writes

    let _, _, writes = List.fold folder (seeded, None, []) measures
    writes

let private descriptor (offset: int) (count: int) : JVal =
    JObj [ "offset", JInt offset; "count", JInt count ]

[<Tests>]
let writerTests =
    testList
        "Fuaran.UI.GridWindow.writer"
        [ test "a scripted scroll writes the expected descriptor sequence, and nothing for an unchanged window" {
              let writes =
                  script
                      (Some "w")
                      None
                      [ at 0.0 320.0 // mount: rows 0..9 visible
                        at 10.0 320.0 // still row 0 at the top — unchanged
                        at 40.0 320.0 // the header scrolled away, row 0 still first — unchanged
                        at 72.0 320.0 // row 1 first
                        at 72.0 320.0 // a re-render at rest — unchanged
                        at 400.0 320.0 // row 11 first
                        at 1000.0 320.0 // row 30 first
                        at 1000.0 160.0 ] // a resize halves the visible count

              Expect.equal
                  writes
                  [ "w", descriptor 0 10
                    "w", descriptor 1 10
                    "w", descriptor 11 10
                    "w", descriptor 30 10
                    "w", descriptor 30 5 ]
                  "one write per changed window, in scroll order"
          }
          test "a grid without a window key never writes" {
              let writes =
                  script None None [ at 0.0 320.0; at 72.0 320.0; at 1000.0 160.0; at 5000.0 0.0 ]

              Expect.isEmpty writes "no key, no write"
          }
          test "a window State already holds is not written again" {
              let writes =
                  script (Some "w") (Some { Offset = 1; Count = 10 }) [ at 72.0 320.0; at 90.0 320.0 ]

              Expect.isEmpty writes "the seeded window is the measured one"
          }
          test "an unmeasurable viewport falls back to the TS viewport's defaults" {
              let measured =
                  BindingResolver.measureWindow
                      { ScrollTop = 64.0
                        HeaderHeight = 0.0
                        RowHeight = 0.0
                        ViewportHeight = 0.0 }

              Expect.equal
                  measured
                  { BindingResolver.RowWindow.Offset = 2
                    BindingResolver.RowWindow.Count = 15 }
                  "32px rows in a 480px viewport"
          }
          test "the written descriptor reads back through the renderer's own reader and moves the window" {
              let _, write =
                  BindingResolver.stepWindowWriter (Some "w") None None (at 400.0 320.0)

              match write with
              | None -> failtest "a first measurement writes"
              | Some(key, jv) ->
                  let sources =
                      { BindingResolver.empty with
                          State = Map.ofList [ key, nn jv ] }

                  let spec =
                      erased
                          { Defaults.grid<Row, obj> with
                              Source = Binding.State("rows", None)
                              WindowStateKey = Some "w" }

                  let rows = [ for i in 0..99 -> Map.ofList [ "id", nn (string i) ] ]

                  match BindingResolver.gridWindow sources spec rows with
                  | None -> failtest "a windowed grid presents a window"
                  | Some presented ->
                      Expect.equal presented.Offset 11 "the next window starts at the written offset"
                      Expect.equal (idsOf presented.Rows) [ for i in 11..20 -> string i ] "and holds the written count"
          } ]
