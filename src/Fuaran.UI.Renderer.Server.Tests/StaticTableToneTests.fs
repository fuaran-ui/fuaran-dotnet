module Fuaran.UI.Renderer.Server.Tests.StaticTableToneTests

// ============================================================================
//  Phase 2473 — toned cells in static tables.
//
//  A static table's cells were text only, so a change column could not read a
//  rise as good and a fall as bad. The ruling (VOCABULARY.md §1.2): no new
//  vocabulary — a `DataGrid` already carries `columns` beside `staticRows`, and
//  the static leg now reads them POSITIONALLY. These pins hold that a
//  `TonedPill` column draws each cell as the toned pill (class plus the cell's
//  own text), that a cell no column describes stays plain, that the tone keys on
//  the row value at the pill's `field`, and that a table declaring no columns
//  renders byte-for-byte as before.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer.Server

let private lit (s: string) = TextSource.Literal s

let private spec (headers: string list) (rows: string list list) : TableSpec<obj> =
    { Defaults.table<obj> with
        Headers = List.map lit headers
        Rows = rows |> List.map (List.map lit) }

let private signTones =
    Map.ofList [ "+4.1%", ToneVariant.Success; "-2.3%", ToneVariant.Critical ]

let private segments =
    spec [ "Segment"; "Change" ] [ [ "Retail"; "+4.1%" ]; [ "Wholesale"; "-2.3%" ]; [ "Online"; "0.0%" ] ]

let private toned =
    Fuaran.tableWithColumns
        "seg"
        [ TableColumn.text "segment"
          TableColumn.tonedPill "change" signTones ToneVariant.Default ]
        segments

[<Tests>]
let tests =
    testList
        "Phase 2473 — toned cells in static tables"
        [ test "a TonedPill column draws each cell as the pill in its mapped tone, keeping the cell's text" {
              let html = Render.renderStatic toned

              Expect.stringContains
                  html
                  "<td class=\"fuaran-table-cell\"><span class=\"fuaran-grid-cell-pill fuaran-pill-success\">+4.1%</span></td>"
                  "a rise reads Success"

              Expect.stringContains
                  html
                  "<td class=\"fuaran-table-cell\"><span class=\"fuaran-grid-cell-pill fuaran-pill-critical\">-2.3%</span></td>"
                  "a fall reads Critical"

              Expect.stringContains
                  html
                  "<td class=\"fuaran-table-cell\"><span class=\"fuaran-grid-cell-pill fuaran-pill-default\">0.0%</span></td>"
                  "an unmapped value takes the default tone and keeps its text"
          }

          test "a Text column's cells, and cells beyond the declared columns, stay plain" {
              let html =
                  Render.renderStatic (
                      Fuaran.tableWithColumns
                          "t"
                          [ TableColumn.tonedPill "change" signTones ToneVariant.Default ]
                          (spec [ "Change"; "Segment" ] [ [ "+4.1%"; "Retail" ] ])
                  )

              Expect.stringContains html "<td class=\"fuaran-table-cell\">Retail</td>" "an undescribed cell is text"

              Expect.stringContains
                  (Render.renderStatic toned)
                  "<td class=\"fuaran-table-cell\">Retail</td>"
                  "Text column"
          }

          test "the tone keys on the row's value at the pill's field — another column's, when it names one" {
              let html =
                  Render.renderStatic (
                      Fuaran.tableWithColumns
                          "t"
                          // The Shipment column's pill names the `status` field.
                          [ { TableColumn.tonedPill
                                  "status"
                                  (Map.ofList [ "Late", ToneVariant.Warning ])
                                  ToneVariant.Default with
                                Field = Some "shipment" }
                            TableColumn.text "status" ]
                          (spec [ "Shipment"; "Status" ] [ [ "SHP-1"; "Late" ] ])
                  )

              Expect.stringContains
                  html
                  "<span class=\"fuaran-grid-cell-pill fuaran-pill-warning\">SHP-1</span>"
                  "the Shipment cell is toned by the row's status, and keeps its own text"
          }

          test "a table declaring no columns renders exactly as Fuaran.table" {
              Expect.equal
                  (Render.renderStatic (Fuaran.tableWithColumns "seg" [] segments))
                  (Render.renderStatic (Fuaran.table "seg" segments))
                  "no columns, no change"

              Expect.isFalse
                  ((Render.renderStatic (Fuaran.table "seg" segments)).Contains "fuaran-grid-cell-pill")
                  "a plain static table draws no pill"
          } ]
