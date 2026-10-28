module Fuaran.UI.Renderer.Server.Tests.StaticTableSelectionTests

// ============================================================================
//  Phase 2474 — selectable static-table rows.
//
//  The drill (select a row, see its detail beside the table) could not be said
//  over a static table: the static leg honoured no click, so a `Selection` on
//  the table only ever read its own default, and the server render had no
//  selected row. The ruling (VOCABULARY.md §1.2): no new vocabulary. A table
//  whose `rowKeyField` names a declared column publishes the clicked row's
//  record under its own id (the Phase 427 default on the static leg), and the
//  existing designed-selection spelling — a `Selection` projecting the key with
//  a `defaultValue` — is laid under `Selections` by the seeding pass both
//  renderers run, so the server marks the row and the pane reads its cells.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server

let private lit (s: string) = TextSource.Literal s

let private countries: TableSpec<obj> =
    { Defaults.table<obj> with
        Headers = [ lit "Country"; lit "GDP" ]
        Rows =
            [ [ lit "Germany"; lit "4.5T" ]
              [ lit "France"; lit "3.1T" ]
              [ lit "Italy"; lit "2.3T" ] ] }

let private columns = [ TableColumn.text "country"; TableColumn.text "gdp" ]

let private table = Fuaran.selectableTable "countries" "country" columns countries

/// A detail slot reading one field of the table's selection.
let private detail (id: string) (field: string) (designed: string option) : Node<obj> =
    Fuaran.callout
        id
        { Defaults.callout with
            Body =
                TextSource.Bound(
                    Binding.Selection("countries", Binding.projectSelectionField<string> field, designed, Some field)
                ) }

let private drillOf (t: Node<obj>) (keyDefault: string option) (gdpDefault: string option) : Node<obj> =
    Fuaran.splitPanel
        "drill"
        { Defaults.splitPanel with
            Children =
                [ t
                  detail "detail-country" "country" keyDefault
                  detail "detail-gdp" "gdp" gdpDefault ] }

let private drill = drillOf table

let private selectedFrance =
    "<tr class=\"fuaran-table-row fuaran-grid-row-selected\" aria-selected=\"true\">"
    + "<td class=\"fuaran-table-cell\">France</td>"

[<Tests>]
let tests =
    testList
        "Phase 2474 — selectable static-table rows"
        [ test "a designed selection marks its row on the server render, and the pane reads that row" {
              // The GDP slot declares NO default of its own: the designed selection
              // is a selection of the France row, so it reads France's cell.
              let html = Render.renderStatic (drill (Some "France") None)

              Expect.stringContains html selectedFrance "the France row is marked selected"
              Expect.stringContains html "3.1T" "the pane reads the selected row's GDP cell"

              Expect.stringContains
                  html
                  "<tr class=\"fuaran-table-row\"><td class=\"fuaran-table-cell\">Germany</td>"
                  "an unselected row is exactly what it was"

              Expect.equal (html.Split("aria-selected").Length - 1) 1 "one row, and only one, carries aria-selected"
          }

          test "no designed selection marks no row, and readers keep their own defaults (Phase 629)" {
              let html = Render.renderStatic (drill None (Some "n/a"))
              Expect.isFalse (html.Contains "aria-selected") "nothing is selected before a click"
              Expect.isFalse (html.Contains "fuaran-grid-row-selected") "no selected class"
              Expect.stringContains html "n/a" "the GDP slot reads its own default"
          }

          test "a designed key that names no row seeds nothing" {
              let html = Render.renderStatic (drill (Some "Spain") (Some "n/a"))
              Expect.isFalse (html.Contains "aria-selected") "no row is marked"
              Expect.stringContains html "n/a" "the GDP slot keeps its own default"
          }

          test "a furnished selection wins over the designed one" {
              let italy: Row = Map.ofList [ "country", box "Italy"; "gdp", box "2.3T" ]

              let sources =
                  { BindingResolver.empty with
                      Selections = Map.ofList [ NodeId "countries", box italy ] }

              let html = Render.render sources (drill (Some "France") None)

              Expect.stringContains
                  html
                  "<tr class=\"fuaran-table-row fuaran-grid-row-selected\" aria-selected=\"true\"><td class=\"fuaran-table-cell\">Italy</td>"
                  "the host's selection is the selected row"

              Expect.isFalse (html.Contains selectedFrance) "the designed row is not marked over it"
          }

          test "a table is selectable only when its rowKeyField is a declared column" {
              let unkeyed = Fuaran.tableWithColumns "countries" columns countries
              let wrongKey = Fuaran.selectableTable "countries" "iso" columns countries

              for t in [ unkeyed; wrongKey ] do
                  let html = Render.renderStatic (drillOf t (Some "France") None)
                  Expect.isFalse (html.Contains "aria-selected") "an unselectable table marks no row"
          }

          test "an unselected selectable table renders the plain static table's markup" {
              Expect.equal
                  (Render.renderStatic table)
                  (Render.renderStatic (Fuaran.tableWithColumns "countries" columns countries))
                  "selectability adds nothing to the markup until a row is selected"
          }

          test "the static row record is each declared field to its cell's text" {
              let row = GridModel.staticRow columns [ "France"; "3.1T"; "extra" ]
              Expect.equal row (Map.ofList [ "country", box "France"; "gdp", box "3.1T" ]) "record"
              Expect.equal (GridModel.staticKeyColumn columns (Some "gdp")) (Some 1) "key position"
              Expect.equal (GridModel.staticKeyColumn columns (Some "iso")) None "no such column"
              Expect.equal (GridModel.staticKeyColumn columns None) None "no key declared"
          }

          test "each row of a selectable table declares the row-select attachment, keyed by index" {
              let grid =
                  match table.Kind with
                  | NodeKind.DataGrid g -> g
                  | _ -> failwith "a table is a DataGrid"

              let plan: ElementPlan.Plan<string> =
                  ElementPlan.VisPlan.staticGrid
                      BindingResolver.empty
                      "countries"
                      grid
                      { Headers = countries.Headers
                        Rows = countries.Rows
                        Sortable = None
                        DefaultSort = None }

              let rec behaviours (p: ElementPlan.Plan<string>) : (string * string) list =
                  match p with
                  | ElementPlan.Plan.Element e ->
                      (e.Props
                       |> List.choose (function
                           | ElementPlan.Prop.Behaviour(point, key) -> Some(point, key)
                           | _ -> None))
                      @ (match e.Content with
                         | ElementPlan.Content.Children cs -> List.collect behaviours cs
                         | _ -> [])
                  | _ -> []

              Expect.equal
                  (behaviours plan)
                  [ for i in 0..2 -> ElementPlan.Attach.GridRowSelect, string i ]
                  "one per row"
          }

          test "the drill validates with no row-identity, inert-control or selection defect" {
              let defects =
                  match PreEmitValidate.validate (drill (Some "France") (Some "3.1T")) with
                  | Ok() -> []
                  | Error ds -> ds |> List.map (fun d -> sprintf "%A" d)

              for name in
                  [ "UnstableRowIdentity"
                    "InertControl"
                    "SelectionOverNonProducer"
                    "DanglingSelection" ] do
                  Expect.isFalse
                      (defects |> List.exists (fun d -> d.StartsWith name))
                      (sprintf "no %s in %A" name defects)
          } ]
