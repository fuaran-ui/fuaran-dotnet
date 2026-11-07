module Fuaran.UI.Renderer.Server.Tests.StaticTableCitationTests

// ============================================================================
//  Phase 2592 — a table cell can cite its figure.
//
//  `DataGrid.staticRows.citations` names cells by coordinate (WIRE_FORMAT.md
//  §3.6.33). These pins hold that a cited cell stamps `data-fact` on its `<td>`
//  with the label as the cell's first child, that the table is otherwise what
//  it was, cell class and all, that a toned cell keeps its pill, and that a
//  table citing nothing renders byte-for-byte as its uncited twin.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer.Server

let private lit (s: string) = TextSource.Literal s

let private report: TableSpec<obj> =
    { Defaults.table<obj> with
        Headers = List.map lit [ "Channel"; "Spend" ]
        Rows = [ [ lit "Online"; lit "£412k" ]; [ lit "Retail"; lit "£1.2m" ] ] }

let private count (needle: string) (html: string) =
    (html.Length - html.Replace(needle, "").Length) / needle.Length

[<Tests>]
let tests =
    testList
        "Phase 2592 — a table cell can cite its figure"
        [ test "a cited cell stamps data-fact on its td, with the label as first child" {
              let html =
                  Fuaran.table "t" report
                  |> Table.withCitation 0 1 "fact:spend" (Some "Online spend")
                  |> Render.renderStatic

              Expect.stringContains
                  html
                  "<td class=\"fuaran-table-cell\" data-fact=\"fact:spend\"><span class=\"fuaran-cited-label\">Online spend </span>£412k</td>"
                  "the td carries the reference and the label is its first child"

              Expect.stringContains html "<td class=\"fuaran-table-cell\">Online</td>" "a row header cites nothing"
              Expect.stringContains html "<td class=\"fuaran-table-cell\">£1.2m</td>" "an uncited figure is as it was"
              Expect.equal (count "<table" html) 1 "one semantic table"
              Expect.isFalse (html.Contains "fuaran-fact") "no tile is drawn"
          }

          test "a citation with no label is the attribute alone" {
              let html =
                  Fuaran.table "t" report
                  |> Table.withCitation 1 1 "fact:retail" None
                  |> Render.renderStatic

              Expect.stringContains
                  html
                  "<td class=\"fuaran-table-cell\" data-fact=\"fact:retail\">£1.2m</td>"
                  "the cell's text is unwrapped"

              Expect.isFalse (html.Contains "fuaran-cited-label") "no label, no span"
          }

          test "the reference goes through the attribute encoder" {
              let html =
                  Fuaran.table "t" report
                  |> Table.withCitation 0 1 "fact:\"a\"&b" None
                  |> Render.renderStatic

              Expect.stringContains html "data-fact=\"fact:&quot;a&quot;&amp;b\"" "encoded as any attribute"
          }

          test "a cited toned cell keeps its pill, after the label" {
              let html =
                  Fuaran.tableWithColumns
                      "t"
                      [ TableColumn.text "channel"
                        TableColumn.tonedPill "change" (Map.ofList [ "£412k", ToneVariant.Success ]) ToneVariant.Default ]
                      report
                  |> Table.withCitation 0 1 "fact:spend" (Some "Online spend")
                  |> Render.renderStatic

              Expect.stringContains
                  html
                  "data-fact=\"fact:spend\"><span class=\"fuaran-cited-label\">Online spend </span><span class=\"fuaran-grid-cell-pill fuaran-pill-success\">£412k</span></td>"
                  "the label comes first and the pill is untouched"
          }

          test "stripping the citation leaves the uncited twin, byte for byte" {
              let uncited = Fuaran.table "t" report |> Render.renderStatic

              let cited =
                  Fuaran.table "t" report
                  |> Table.withCitation 0 1 "fact:spend" (Some "Online spend")
                  |> Render.renderStatic

              Expect.equal
                  (cited
                      .Replace(" data-fact=\"fact:spend\"", "")
                      .Replace("<span class=\"fuaran-cited-label\">Online spend </span>", ""))
                  uncited
                  "a citation adds the attribute and the span and nothing else"
          }

          test "the record form rides through Fuaran.table, and refuses what decode refuses" {
              let citation: TableCitation =
                  { Column = 1
                    Label = None
                    Reference = "fact:spend"
                    Row = 0 }

              let html =
                  Fuaran.table "t" { report with Citations = [ citation ] } |> Render.renderStatic

              Expect.stringContains html "data-fact=\"fact:spend\"" "the record form stamps too"

              Expect.throws
                  (fun () ->
                      Fuaran.table
                          "t"
                          { report with
                              Citations = [ { citation with Row = 2 } ] }
                      |> ignore)
                  "a coordinate naming no cell is refused where the table is built"

              Expect.throws
                  (fun () ->
                      Fuaran.table
                          "t"
                          { report with
                              Citations = [ citation; citation ] }
                      |> ignore)
                  "two citations for one cell are refused where the table is built"
          } ]
