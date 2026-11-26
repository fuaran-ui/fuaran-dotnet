module Fuaran.UI.Renderer.Server.Tests.BoundGridSsrTests

// ============================================================================
//  Phase 2775 — a pipeline-bound grid is a real table on the server.
//
//  A `DataGrid` whose rows come from a binding used to be served as a
//  "hydrates client-side" placeholder, even where the server held the rows (a
//  pipeline over an embedded table). It is now the table the first-party
//  client grid draws before any interaction — `ElementPlan.VisPlan.boundGrid` —
//  and the placeholder is kept, naming its reason, for a source that does not
//  resolve here.
//
//  The claims, in order:
//
//    1. Over an embedded table, the rows drawn are the rows the reference
//       evaluation (`BindingResolver.resolveRowsWithFields`, the certified
//       evaluator) produces, in the declared sort, each cell through its
//       declared format.
//    2. A declared page is the first page with the renderer-owned pager; past
//       `BoundGridRowLimit` the first rows are drawn and the total stated.
//    3. An unresolved source keeps the placeholder, and it says why; the state
//       slots render as the client's do.
//    4. PARITY with the client's first render. The Feliz client's
//       `ReactElement` is opaque on .NET, so the two cannot be diffed byte for
//       byte here (`RendererAgreementTests` makes the same point). The client
//       HYDRATES this markup in place, and an attribute the two disagree about
//       survives hydration, so the parity test pins the server's markup to a
//       transcription of the client's `renderGrid` / `renderGridCell`
//       (`Fuaran.UI.Renderer/Render.fs`) element for element: a change to
//       either side that is not made to both fails here, or in review of the
//       client arm this golden quotes.
// ============================================================================

open System
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server
open Fuaran.UI.Ops

let private contains (needle: string) (haystack: string) =
    haystack.Contains(needle, StringComparison.Ordinal)

let private count (needle: string) (haystack: string) =
    let rec go (from: int) (n: int) =
        match haystack.IndexOf(needle, from, StringComparison.Ordinal) with
        | -1 -> n
        | i -> go (i + needle.Length) (n + 1)

    go 0 0

let private decode (json: string) : Node<obj> =
    match JsonDecode.decodeNodeObj json with
    | Ok node -> node
    | Error e -> failwithf "decode failed: %A" e

let private render (node: Node<obj>) =
    Render.render BindingResolver.empty node

/// The substring from the first `open` to the end of the element it opens.
let private elementFrom (openTag: string) (closeTag: string) (html: string) : string =
    let start = html.IndexOf(openTag, StringComparison.Ordinal)

    if start < 0 then
        failtestf "%s not found in %s" openTag html

    let stop = html.LastIndexOf(closeTag, StringComparison.Ordinal)
    html.Substring(start, stop + closeTag.Length - start)

/// A pipeline over an embedded table: spend summed by department.
let private spendSource =
    """{"$type":"Transform","pipeline":[{"$type":"groupBy","aggs":[{"fn":"sum","name":"total","of":"amount"}],"keys":["dept"]}],"source":{"columns":{"amount":{"validity":[true,true,true,true],"values":[100,120,95,40]},"dept":{"validity":[true,true,true,true],"values":["eng","eng","sales","ops"]}},"schema":[{"name":"dept","type":"string"},{"name":"amount","type":"int"}]}}"""

let private gridJson (extra: string) (source: string) =
    sprintf
        """{"id":"spend-grid","kind":{"$type":"DataGrid","columns":[{"field":"dept","kind":{"$type":"Text"},"label":"Department"},{"field":"total","format":{"$type":"Currency","code":"GBP","decimals":0,"locale":{"$type":"Explicit","tag":"en-GB"}},"kind":{"$type":"Numeric"},"label":"Spend"}],"rowKeyField":"dept"%s,"source":%s}}"""
        extra
        source

/// `n` rows `{ "i": k }` as an embedded single-column table.
let private numberedSource (n: int) =
    let values = String.Join(",", [ for k in 1..n -> string k ])
    let validity = String.Join(",", [ for _ in 1..n -> "true" ])

    sprintf
        """{"$type":"Transform","pipeline":[],"source":{"columns":{"i":{"validity":[%s],"values":[%s]}},"schema":[{"name":"i","type":"int"}]}}"""
        validity
        values

let private numberedGrid (extra: string) (n: int) =
    decode (
        sprintf
            """{"id":"n-grid","kind":{"$type":"DataGrid","columns":[{"field":"i","kind":{"$type":"Numeric"},"label":"I"}]%s,"source":%s}}"""
            extra
            (numberedSource n)
    )

[<Tests>]
let tests =
    testList
        "Phase 2775 — a pipeline-bound grid is a real table on the server"
        [ test "the rows are the reference evaluation's, each cell through its declared format" {
              let node = decode (gridJson "" spendSource)
              let html = render node

              Expect.isFalse (contains "fuaran-grid-ssr-placeholder" html) "no placeholder for a resolved source"
              Expect.isFalse (contains "hydrates client-side" html) "and no hydration caption"

              // The reference evaluation, read independently of the renderer.
              let spec =
                  match node.Kind with
                  | NodeKind.DataGrid spec -> spec
                  | _ -> failtest "a DataGrid"

              let expected =
                  match fst (BindingResolver.resolveRowsWithFields BindingResolver.empty spec.Source) with
                  | BindingResolver.Resolved rows ->
                      [ for row in rows ->
                            [ for col in spec.Columns ->
                                  Formatting.formatCellValueIn
                                      BindingResolver.empty.Locale
                                      col.Format
                                      (BindingResolver.projectRowFieldValue row (Option.get col.Field)) ] ]
                  | other -> failtestf "the evaluator resolves the embedded pipeline: %A" other

              Expect.equal
                  expected
                  [ [ "eng"; "£220" ]; [ "sales"; "£95" ]; [ "ops"; "£40" ] ]
                  "the evaluator's rows, formatted (a hand check of the oracle itself)"

              let drawn =
                  [ for row in expected ->
                        String.Join(
                            "",
                            [ for cell in row -> sprintf "<td class=\"fuaran-grid-cell\"><span>%s</span></td>" cell ]
                        ) ]

              let tbody = elementFrom "<tbody>" "</tbody>" html

              Expect.equal
                  tbody
                  ("<tbody>"
                   + String.Join("", [ for cells in drawn -> "<tr class=\"fuaran-grid-row\">" + cells + "</tr>" ])
                   + "</tbody>")
                  "the server draws exactly those rows, in evaluation order"

              Expect.stringContains
                  html
                  "<th class=\"fuaran-grid-header\">Department</th><th class=\"fuaran-grid-header\">Spend</th>"
                  "the declared columns, in order"
          }

          test "the declared default sort orders the rows, and the sorted header says so" {
              let html =
                  render (
                      decode (
                          gridJson
                              ""","defaultSort":{"column":1,"direction":"desc"},"sortStateKey":"spend-sort" """
                              spendSource
                      )
                  )

              let order =
                  [ "eng"; "sales"; "ops" ]
                  |> List.map (fun d -> d, html.IndexOf("<span>" + d + "</span>", StringComparison.Ordinal))

              Expect.isTrue (order |> List.forall (fun (_, i) -> i > 0)) "every row is drawn"

              Expect.equal
                  (order |> List.sortBy snd |> List.map fst)
                  [ "eng"; "sales"; "ops" ]
                  "descending by spend: 220, 95, 40"

              let ascending =
                  render (
                      decode (
                          gridJson
                              ""","defaultSort":{"column":1,"direction":"asc"},"sortStateKey":"spend-sort" """
                              spendSource
                      )
                  )

              Expect.isLessThan
                  (ascending.IndexOf("<span>ops</span>", StringComparison.Ordinal))
                  (ascending.IndexOf("<span>eng</span>", StringComparison.Ordinal))
                  "ascending puts the smallest first"

              Expect.stringContains
                  html
                  "<th class=\"fuaran-grid-header\" data-sortable=\"\" tabindex=\"0\" aria-sort=\"descending\">Spend</th>"
                  "the active sort column carries aria-sort, as the client's header does"

              Expect.stringContains
                  html
                  "<th class=\"fuaran-grid-header\" data-sortable=\"\" tabindex=\"0\">Department</th>"
                  "a sortable inactive column carries the affordance without aria-sort"
          }

          test "a declared page is the first page, with the pager and the total stated" {
              let html = render (numberedGrid ""","pageSize":4,"pageStateKey":"n-page" """ 10)

              Expect.equal (count "<tr class=\"fuaran-grid-row\">" html) 4 "one page of rows"
              Expect.stringContains html "<span>1</span>" "starting at the first row"
              Expect.isFalse (contains "<span>5</span>" html) "and stopping at the page"

              Expect.stringContains
                  html
                  ("<nav class=\"fuaran-grid-pager\" aria-label=\"Pagination\">"
                   + "<button class=\"fuaran-grid-pager-step\" type=\"button\" disabled=\"true\">Previous</button>"
                   + "<span class=\"fuaran-grid-pager-status\" aria-live=\"polite\">Page 1 of 3</span>"
                   + "<button class=\"fuaran-grid-pager-step\" type=\"button\">Next</button></nav>")
                  "the client's pager: Previous disabled on page 1, the total pages stated"

              Expect.stringContains html "<div class=\"fuaran-grid-paged\"><table" "inside the paged wrapper"
          }

          test "past the row limit the first rows are drawn and the total stated" {
              let n = ElementPlan.VisPlan.BoundGridRowLimit + 500
              let html = render (numberedGrid "" n)

              Expect.equal
                  (count "<tr class=\"fuaran-grid-row\"" html)
                  ElementPlan.VisPlan.BoundGridRowLimit
                  "never the whole table"

              Expect.stringContains
                  html
                  (sprintf "<table class=\"fuaran-grid\" aria-rowcount=\"%d\">" (n + 1))
                  "the table tells assistive technology its rows are a slice of the total"

              Expect.stringContains
                  html
                  (sprintf
                      "<tr class=\"fuaran-grid-row\" aria-rowindex=\"%d\">"
                      (ElementPlan.VisPlan.BoundGridRowLimit + 1))
                  "each row its index in the whole"

              Expect.stringContains
                  html
                  (sprintf
                      "<span class=\"fuaran-grid-pager-status\" aria-live=\"polite\">Rows 1–%d of %d</span>"
                      ElementPlan.VisPlan.BoundGridRowLimit
                      n)
                  "and the count, in words"

              let atLimit = render (numberedGrid "" ElementPlan.VisPlan.BoundGridRowLimit)

              Expect.isFalse (contains "aria-rowcount" atLimit) "a grid exactly at the limit is not cut"
              Expect.isFalse (contains "fuaran-grid-pager" atLimit) "and states nothing extra"
          }

          test "an unresolved source keeps the placeholder, and it names its reason" {
              let query = render (decode (gridJson "" """{"$type":"Query","name":"spend"}"""))

              Expect.stringContains
                  query
                  "data-fuaran-ssr-placeholder=\"DataGrid\" data-fuaran-row-count=\"0\">[Grid: the host supplied no rows for query &quot;spend&quot; — hydrates client-side]"
                  "a host query with no rows supplied"

              let state = render (decode (gridJson "" """{"$type":"State","key":"spend-rows"}"""))

              Expect.stringContains
                  state
                  "[Grid: state &quot;spend-rows&quot; has no value — hydrates client-side]"
                  "a state source with no default"

              let seeded =
                  render (
                      decode (
                          gridJson
                              ""
                              """{"$type":"State","key":"spend-rows","defaultValue":[{"dept":"eng","total":7}]}"""
                      )
                  )

              Expect.stringContains seeded "<span>£7</span>" "a state source WITH a default resolves and is drawn"
          }

          test "the state slots render as the client's: loading for an unresolved source, empty for no rows" {
              let loadingNode =
                  decode """{"id":"loading","kind":{"$type":"Text","text":"Loading spend"}}"""

              let emptyNode =
                  decode """{"id":"empty","kind":{"$type":"Text","text":"No spend yet"}}"""

              let withState (source: Binding<Row seq>) =
                  let node = decode (gridJson "" """{"$type":"Query","name":"spend"}""")

                  match node.Kind with
                  | NodeKind.DataGrid spec ->
                      { node with
                          Kind = NodeKind.DataGrid { spec with Source = source } }
                      |> Node.onLoading loadingNode
                      |> Node.onEmpty emptyNode
                  | _ -> failtest "a DataGrid"

              let loading =
                  render (withState (Binding.Query("spend", unbox<Row seq>, Option.None)))

              Expect.stringContains loading "Loading spend" "the loading node in place of the grid"
              Expect.isFalse (contains "fuaran-grid-ssr-placeholder" loading) "not the placeholder"

              let empty = render (withState (Binding.Static(Some Seq.empty)))
              Expect.stringContains empty "No spend yet" "the empty node for a source that resolved to no rows"
              Expect.isFalse (contains "<table" empty) "in place of an empty table"
          }

          test "parity: the server's markup is the client's first render, transcribed" {
              // A grid exercising every first-render branch the client arm takes:
              // a sortable header with an active sort, a selected row (a designed
              // selection seed), a row action, a toned pill, a formatted number.
              let json =
                  """{"id":"orders","kind":{"$type":"DataGrid","columns":[{"field":"ref","kind":{"$type":"Text"},"label":"Order"},{"field":"status","kind":{"$type":"TonedPill","field":"status","map":{"late":"Critical"}},"label":"Status"},{"field":"amount","format":{"$type":"Number","decimals":1,"locale":{"$type":"Explicit","tag":"en-GB"}},"kind":{"$type":"Numeric"},"label":"Amount"}],"defaultSort":{"column":2,"direction":"asc"},"onRowClick":"<closure>","rowKeyField":"ref","sortStateKey":"orders-sort","source":{"$type":"Transform","pipeline":[],"source":{"columns":{"amount":{"validity":[true,true],"values":[1250.5,30]},"ref":{"validity":[true,true],"values":["A-2","A-1"]},"status":{"validity":[true,true],"values":["late","ok"]}},"schema":[{"name":"ref","type":"string"},{"name":"status","type":"string"},{"name":"amount","type":"float"}]}}}}"""

              let node = decode json

              let sources =
                  { BindingResolver.empty with
                      Selections = Map.ofList [ NodeId "orders", box (Map.ofList [ "ref", box "A-1" ]) ] }

              let table = elementFrom "<table" "</table>" (Render.render sources node)

              // Transcribed from `renderGrid`'s `gridTableWith`, `sortableHeader`
              // and `renderGridCell` (the `None` commit arms): the client draws a
              // row's class with the selection marker before the interactive one
              // (Phase 1701), a TonedPill as `Css.gridCellPill`, a display cell
              // as a bare `<span>`.
              let expected =
                  "<table class=\"fuaran-grid\"><thead><tr>"
                  + "<th class=\"fuaran-grid-header\" data-sortable=\"\" tabindex=\"0\">Order</th>"
                  + "<th class=\"fuaran-grid-header\" data-sortable=\"\" tabindex=\"0\">Status</th>"
                  + "<th class=\"fuaran-grid-header\" data-sortable=\"\" tabindex=\"0\" aria-sort=\"ascending\">Amount</th>"
                  + "</tr></thead><tbody>"
                  + "<tr class=\"fuaran-grid-row fuaran-grid-row-selected fuaran-grid-row-interactive\">"
                  + "<td class=\"fuaran-grid-cell\"><span>A-1</span></td>"
                  + "<td class=\"fuaran-grid-cell\"><span class=\""
                  + Css.gridCellPill (Theme.toneVar ToneVariant.Default)
                  + "\">ok</span></td>"
                  + "<td class=\"fuaran-grid-cell\"><span>30.0</span></td></tr>"
                  + "<tr class=\"fuaran-grid-row fuaran-grid-row-interactive\">"
                  + "<td class=\"fuaran-grid-cell\"><span>A-2</span></td>"
                  + "<td class=\"fuaran-grid-cell\"><span class=\""
                  + Css.gridCellPill (Theme.toneVar ToneVariant.Critical)
                  + "\">late</span></td>"
                  + "<td class=\"fuaran-grid-cell\"><span>1,250.5</span></td></tr>"
                  + "</tbody></table>"

              Expect.equal table expected "the server's table is the client's first render"
          }

          test "with both render policies off, the grid is the bare table: no host element around it" {
              let html = render (decode (gridJson "" spendSource))
              Expect.isFalse (contains "fuaran-refresh-host" html) "no refresh host"
              Expect.isFalse (contains "fuaran-derivation-host" html) "no derivation host"
          } ]
