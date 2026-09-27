module Fuaran.UI.Tests.BindingCheckCorpus

// ============================================================================
//  Phase 1889 — charts and grids checked against their data, over the corpus.
//
//  `nodes/binding-check-*.json` are six legal documents that round-trip
//  byte-identically, so the node-round-trip family certifies the codec and
//  says nothing about any of this. What they pin is what a host's pre-emit
//  validator DOES with them, which is visible only in that host's own suite —
//  here, this one. The TypeScript host asserts the same expectations over its
//  bundled snapshot of the same files.
//
//  Three claims per negative, one per control:
//
//   1. The control is clean OUTRIGHT and both its readers grade CHECKED — so a
//      negative's finding is attributable to the one reference it breaks.
//   2. Each negative raises exactly its one diagnostic code (FUARAN114's two
//      arms: two findings, one code), located at the JSONPath of the slot the
//      author wrote, and carrying the schema the pipeline does produce.
//   3. The unchecked document is REFUSED NOTHING, and each reader's grade says
//      why it could not be judged — stated, not silent.
//
//  Probed in both directions: a reference renamed to one the pipeline produces
//  clears the finding, so the assertions cannot pass on a report that finds
//  everything.
// ============================================================================

open System.IO
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.PreEmitValidate

let private nodesDir () : string option =
    Fuaran.Tests.CorpusRoot.tryFind ()
    |> Option.map (fun r -> Path.Combine(r, "nodes"))
    |> Option.filter Directory.Exists

let private decode (dir: string) (stem: string) : Node<obj> =
    match Fuaran.UI.Ops.JsonDecode.decodeNodeObj (File.ReadAllText(Path.Combine(dir, stem + ".json"))) with
    | Ok node -> node
    | Error e -> failtestf "%s failed to decode: %s at %s" stem e.Code e.Path

let private codes (tree: Node<obj>) : string list =
    match validate tree with
    | Ok() -> []
    | Error defects -> defects |> List.map (fun d -> let c, _, _ = describe d in c)

/// (code, path) for every diagnostic the report carries, in report order.
let private located (tree: Node<obj>) : (string * string) list =
    bindingChecks tree
    |> List.collect (fun c -> c.Diagnostics |> List.map (fun d -> d.Code, d.Path))

/// The produced schema of the CHECKED control's pipeline: `groupBy dept, sum(amount) as total`.
let private producedByGroupBy: ProducedColumn list =
    [ { Name = "dept"; Type = Some "string" }
      { Name = "total"; Type = Some "int" } ]

let private withCorpus (f: string -> unit) =
    match nodesDir () with
    | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
    | Some d -> f d

[<Tests>]
let tests =
    testList
        "Phase 1889 — charts and grids checked against their data (corpus)"
        [ test "the control is clean outright and both readers grade CHECKED, with the produced schema" {
              withCorpus (fun d ->
                  let tree = decode d "binding-check-control"
                  Expect.isEmpty (codes tree) "the control reads only what its pipeline produces"
                  let checks = bindingChecks tree

                  Expect.equal
                      (checks |> List.map (fun c -> c.NodeId, c.Reader, c.Path, c.Grade))
                      [ "spend-chart", "Chart", "$.kind.children[0].kind.source", BindingGrade.Checked
                        "spend-grid", "DataGrid", "$.kind.children[1].kind.source", BindingGrade.Checked ]
                      "both readers are judged, and located"

                  for c in checks do
                      Expect.equal c.Produced producedByGroupBy "the produced schema is the groupBy's, typed"
                      Expect.isEmpty c.Diagnostics "and nothing is found")
          }

          test "FUARAN086 — a yFields entry the pipeline renamed away, at its slot" {
              withCorpus (fun d ->
                  let tree = decode d "binding-check-chart-ungrounded"
                  Expect.equal (codes tree) [ "FUARAN086" ] "exactly one finding"
                  Expect.equal (located tree) [ "FUARAN086", "$.kind.yFields[0]" ] "located at the yFields entry"

                  let check = bindingChecks tree |> List.exactlyOne
                  Expect.equal check.Produced producedByGroupBy "the finding carries what IS produced"

                  match (List.exactlyOne check.Diagnostics).Defect with
                  | PreEmitDefect.ChartFieldUngrounded(_, field, cols) ->
                      Expect.equal field "amount" "the reference the author wrote"
                      Expect.equal cols [ "dept"; "total" ] "the produced column set"
                  | other -> failtestf "expected ChartFieldUngrounded, got %A" other)
          }

          test "FUARAN087 — a string column plotted as a value series, at its slot" {
              withCorpus (fun d ->
                  let tree = decode d "binding-check-chart-not-numeric"
                  Expect.equal (codes tree) [ "FUARAN087" ] "exactly one finding"
                  Expect.equal (located tree) [ "FUARAN087", "$.kind.yFields[0]" ] "located at the yFields entry")
          }

          test "FUARAN097 — a temporal x-axis over a non-date column, at the xField" {
              withCorpus (fun d ->
                  let tree = decode d "binding-check-chart-temporal-not-date"
                  Expect.equal (codes tree) [ "FUARAN097" ] "exactly one finding"
                  Expect.equal (located tree) [ "FUARAN097", "$.kind.xField" ] "located at the xField")
          }

          test "FUARAN114 — both arms, located through a container" {
              withCorpus (fun d ->
                  let tree = decode d "binding-check-grid-ungrounded"
                  Expect.equal (codes tree) [ "FUARAN114"; "FUARAN114" ] "a column field and the rowKeyField"

                  Expect.equal
                      (located tree)
                      [ "FUARAN114", "$.kind.children[0].kind.columns[1].field"
                        "FUARAN114", "$.kind.children[0].kind.rowKeyField" ]
                      "each finding at its own slot")
          }

          test "the unchecked document is refused nothing, and each grade says why" {
              withCorpus (fun d ->
                  let tree = decode d "binding-check-unchecked"
                  Expect.isEmpty (codes tree) "no source here has a schema a negative verdict could stand on"

                  match bindingChecks tree with
                  | [ chart; grid ] ->
                      Expect.equal chart.NodeId "spend-chart" "the chart first, in document order"

                      match chart.Grade with
                      | BindingGrade.Unchecked(UncheckedReason.OpenSchema why) ->
                          Expect.stringContains why "spend" "the walk names the Ref it could not resolve"
                      | other -> failtestf "the chart over a Ref should grade OpenSchema, got %A" other

                      Expect.equal
                          grid.Grade
                          (BindingGrade.Unchecked(UncheckedReason.NoStaticSchema "Query"))
                          "the grid over a Query has no static schema at all"

                      Expect.isEmpty grid.Produced "and so produces no known column"
                  | other -> failtestf "expected two readers, got %A" other)
          }

          test "the probe runs the other way: renaming the broken reference clears the finding" {
              withCorpus (fun d ->
                  let fixedJson =
                      File
                          .ReadAllText(Path.Combine(d, "binding-check-chart-ungrounded.json"))
                          .Replace("\"yFields\":[\"amount\"]", "\"yFields\":[\"total\"]")

                  match Fuaran.UI.Ops.JsonDecode.decodeNodeObj fixedJson with
                  | Error e -> failtestf "the repaired document failed to decode: %s" e.Code
                  | Ok tree ->
                      Expect.isEmpty (codes tree) "the repair is recognised"

                      Expect.equal
                          (bindingChecks tree |> List.map (fun c -> c.Grade))
                          [ BindingGrade.Checked ]
                          "and the reader is still judged, not skipped")
          } ]
