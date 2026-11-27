module Fuaran.UI.Renderer.Server.Tests.ChartTableSsrTests

// ============================================================================
//  Phase 2776 — a chart carries a data-table alternative, in server-rendered
//  HTML with no script.
//
//  Four claims, over the cases in `ChartTableCases.fs`:
//
//    1. OFF IS THE PAST, BYTE FOR BYTE. With `RenderPolicy.ChartTable = Off`
//       every case renders to the bytes the server renderer produced before
//       this phase. The digests below were taken by rendering exactly these
//       nodes on the pre-phase tree (its `Render.render`, whose policy had no
//       such field); they are a frozen claim about the past, not a recording of
//       the present, so a change that moves one is a change to the off path.
//    2. ON BY DEFAULT. `Render.render` offers the disclosure for every case
//       with rows, and its table holds the lowering's table model cell for
//       cell (`Charts.tableModel`), caption included; a refused chart and a
//       chart with no rows offer none, and render as they did.
//    3. THE CHART NAMES ITS TABLE. The drawing's `role="img"` root carries
//       `aria-details` naming the disclosure's id, which occurs once.
//    4. THE CONTRACT'S RULES. `a11y-contract.json`'s `chartTable` section is
//       read from the corpus and every rule it names is checked over every
//       case's markup; a rule this suite has no check for fails, so the
//       contract and this tier cannot drift apart.
// ============================================================================

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Xml.Linq
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server

/// The pre-phase server render of each case, as SHA-256 over its UTF-8 bytes.
let private prePhaseDigests: Map<string, string> =
    Map.ofList
        [ "bar-single", "c6123aa053d2232bcb6a9eb82340c1be85a681818c56d2f4f1a7e00f4c8deddd"
          "bar-stacked-currency", "4db349babb5d7bc129486b7fe62a0cfa786863a660c27c99bc8c8ce6fdbbc213"
          "line-temporal", "081f3b920bef6cc244838b21321bd462306b2c3278aa94b91696b9fdc20e6330"
          "pie-percent", "620dd41763c0df6c01667276ffbf61b5b182cba93e58b6d1063ed456ec9459a1"
          "scatter-channels", "2d7a5842dc3c40e507437342582da5d3ebc965e0c524f8d870372d8ddaebb2b1"
          "bar-withheld-sorted", "943ec1b8a1b86a18d37521837beccf54c2b6b2cbf065344a32787c44d3493661"
          "heatmap-withheld", "fb64f167e9960295faf943e0ebfbb23d9de16e5144e0633e0911bac829d5baf1"
          "bar-derived-series", "b599865032bfbbf1e0b13e96bfd98507eb917d89e8026f7fe38fa0e73c98f2b7"
          "bar-no-rows", "2a35ada3fe4e020523a3d23d359d1299e866cb87653f084145dadd6cdc3bcbdd"
          "bar-refused-series", "4d2ab9df168d29b5283c167ef513e82ba913be33a7d03aa77dd1db2a53196d33" ]

let private sha256 (text: string) : string =
    SHA256.HashData(Encoding.UTF8.GetBytes text)
    |> Array.map (fun b -> b.ToString "x2")
    |> String.concat ""

let private off: RenderPolicy =
    { RenderPolicy.defaults with
        ChartTable = ChartTablePolicy.Off }

let private renderOff (node: Node<obj>) : string =
    Render.renderWithPolicy off Registry.empty BindingResolver.empty node

let private renderOn (node: Node<obj>) : string =
    Render.render BindingResolver.empty node

/// The case's spec and its static rows.
let private chartOf (node: Node<obj>) : ChartSpec<obj> * Map<string, obj> list =
    match node.Kind with
    | NodeKind.Chart spec ->
        match spec.Source with
        | Binding.Static(Some rows) -> spec, List.ofSeq rows
        | other -> failtestf "a case's source is static rows, not %A" other
    | other -> failtestf "a case is a Chart, not %A" other

/// The disclosure's markup, or `None` when the render offers none.
let private disclosureOf (html: string) : XElement option =
    let start =
        html.IndexOf("<details class=\"fuaran-chart-table\"", StringComparison.Ordinal)

    if start < 0 then
        None
    else
        let stop = html.IndexOf("</details>", start, StringComparison.Ordinal)
        Some(XElement.Parse(html.Substring(start, stop + "</details>".Length - start)))

/// The `aria-details` of the drawing root, if any.
let private drawingDetails (html: string) : string option =
    let m =
        Regex.Match(html, "<svg class=\"fuaran-drawing\" role=\"img\"(?: aria-details=\"([^\"]*)\")?")

    if m.Success && m.Groups[1].Success then
        Some m.Groups[1].Value
    else
        None

let private children (name: string) (e: XElement) =
    e.Elements(XName.Get name) |> List.ofSeq

let private text (e: XElement) : string = e.Value

/// One check per rule id the contract's `chartTable` section names. Each takes
/// the whole render and its disclosure, and returns the failures it found.
let private checks: Map<string, string -> XElement -> string list> =
    let table (d: XElement) = children "table" d |> List.tryHead

    let headerCells (d: XElement) =
        match table d with
        | Some t ->
            children "thead" t
            |> List.collect (children "tr")
            |> List.tryHead
            |> Option.map (children "th")
            |> Option.defaultValue []
        | None -> []

    let bodyRows (d: XElement) =
        match table d with
        | Some t -> children "tbody" t |> List.collect (children "tr")
        | None -> []

    Map.ofList
        [ "disclosure-summary",
          fun _ d ->
              match d.Elements() |> Seq.tryHead with
              | Some s when s.Name.LocalName = "summary" && text s <> "" -> []
              | _ -> [ "the disclosure's first child is not a summary with text" ]
          "referenced-from-chart",
          fun html d ->
              let id = d.Attribute(XName.Get "id")

              if isNull id || id.Value = "" then
                  [ "the disclosure carries no id" ]
              else
                  let occurrences = Regex.Matches(html, "id=\"" + Regex.Escape id.Value + "\"").Count

                  [ if occurrences <> 1 then
                        sprintf "the id %s occurs %d times" id.Value occurrences
                    if drawingDetails html <> Some id.Value then
                        sprintf "the drawing's aria-details is %A, not %s" (drawingDetails html) id.Value ]
          "one-table",
          fun _ d ->
              match children "table" d with
              | [ _ ] -> []
              | ts -> [ sprintf "the disclosure holds %d tables" ts.Length ]
          "caption",
          fun _ d ->
              match table d |> Option.bind (fun t -> t.Elements() |> Seq.tryHead) with
              | Some c when c.Name.LocalName = "caption" && text c <> "" -> []
              | _ -> [ "the table's first child is not a caption with text" ]
          "column-headers",
          fun _ d ->
              match headerCells d with
              | [] -> [ "the table has no header row" ]
              | cells ->
                  cells
                  |> List.choose (fun th ->
                      let scope = th.Attribute(XName.Get "scope")

                      if isNull scope || scope.Value <> "col" || text th = "" then
                          Some(sprintf "header cell %A is not a named th scope=col" (text th))
                      else
                          None)
          "row-headers",
          fun _ d ->
              bodyRows d
              |> List.collect (fun tr ->
                  tr.Elements()
                  |> List.ofSeq
                  |> List.mapi (fun i cell ->
                      let scope = cell.Attribute(XName.Get "scope")

                      match i, cell.Name.LocalName with
                      | 0, "th" when not (isNull scope) && scope.Value = "row" -> None
                      | 0, _ -> Some "a body row's first cell is not a th scope=row"
                      | _, "td" -> None
                      | _, other -> Some(sprintf "a body row's later cell is a %s" other))
                  |> List.choose id)
          "rectangular",
          fun _ d ->
              let width = (headerCells d).Length

              bodyRows d
              |> List.choose (fun tr ->
                  let n = Seq.length (tr.Elements())

                  if n <> width then
                      Some(sprintf "a body row has %d cells where the header has %d" n width)
                  else
                      None)
          "no-empty-body",
          fun _ d ->
              if List.isEmpty (bodyRows d) then
                  [ "the table has no body rows" ]
              else
                  [] ]

/// The rule ids the contract's `chartTable` section names, or `None` without a corpus.
let private contractRules () : string list option =
    Fuaran.Tests.CorpusRoot.tryFind ()
    |> Option.map (fun root ->
        use doc =
            JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "a11y-contract.json")))

        [ for r in doc.RootElement.GetProperty("chartTable").GetProperty("rules").EnumerateArray() ->
              r.GetProperty("id").GetString() ])

[<Tests>]
let chartTableSsrTests =
    testList
        "Phase 2776 — a chart's data table, server-rendered"
        [ test "with the policy off, every case renders byte-identically to the pre-phase renderer" {
              Expect.equal
                  (ChartTableCases.cases |> List.map fst |> Set.ofList)
                  (prePhaseDigests |> Map.keys |> Set.ofSeq)
                  "every case has a pre-phase digest, and every digest a case"

              for name, node in ChartTableCases.cases do
                  Expect.equal (sha256 (renderOff node)) prePhaseDigests[name] (name + ": the off render moved")
          }

          test "the table is on by default and holds the lowering's table model, cell for cell" {
              Expect.equal RenderPolicy.defaults.ChartTable ChartTablePolicy.Offer "on by default"

              for name, node in ChartTableCases.cases do
                  let html = renderOn node
                  let spec, rows = chartOf node

                  match Charts.tableModel spec rows, disclosureOf html with
                  | None, None ->
                      Expect.equal html (renderOff node) (name + ": no table, so the render is the off render")
                  | Some model, Some d ->
                      let t = children "table" d |> List.exactlyOne

                      Expect.equal
                          (children "caption" t |> List.exactlyOne |> text)
                          (BindingResolver.resolveTextSource BindingResolver.empty model.Caption)
                          (name + ": caption")

                      Expect.equal
                          (children "thead" t
                           |> List.collect (children "tr")
                           |> List.collect (children "th")
                           |> List.map text)
                          model.Columns
                          (name + ": columns")

                      Expect.equal
                          (children "tbody" t
                           |> List.collect (children "tr")
                           |> List.map (fun tr -> tr.Elements() |> Seq.map text |> List.ofSeq))
                          model.Rows
                          (name + ": rows")
                  | model, d -> failtestf "%s: the model is %A but the render's disclosure is %A" name model d
          }

          test "a chart with rows offers the table, and one the caps refuse or with no rows offers none" {
              let offered =
                  ChartTableCases.cases
                  |> List.filter (fun (_, node) -> (disclosureOf (renderOn node)).IsSome)
                  |> List.map fst

              Expect.equal
                  offered
                  [ "bar-single"
                    "bar-stacked-currency"
                    "line-temporal"
                    "pie-percent"
                    "scatter-channels"
                    "bar-withheld-sorted"
                    "heatmap-withheld"
                    "bar-derived-series" ]
                  "every case with values to read offers the table"
          }

          test "the disclosure needs no script, and the drawing names it" {
              for name, node in ChartTableCases.cases do
                  let html = renderOn node
                  Expect.isFalse (html.Contains "<script") (name + ": no script")

                  match disclosureOf html with
                  | Some d ->
                      let id = d.Attribute(XName.Get "id").Value
                      Expect.equal id (Accessibility.chartTableId ("chart-" + name)) (name + ": the disclosure's id")
                      Expect.equal (drawingDetails html) (Some id) (name + ": aria-details names it")

                      Expect.equal
                          (children "summary" d |> List.map text)
                          [ ElementPlan.ChartTablePlan.toggleText ]
                          (name + ": the toggle")
                  | None -> Expect.equal (drawingDetails html) None (name + ": no table, no aria-details")
          }

          test "the values are escaped as text, never markup" {
              let html =
                  ChartTableCases.cases
                  |> List.find (fun (n, _) -> n = "bar-stacked-currency")
                  |> snd
                  |> renderOn

              Expect.stringContains html "<th scope=\"row\">South &lt;&amp;&gt;</th>" "an untrusted category"
              Expect.stringContains html "<td>£3,400.25</td>" "a currency value at its own precision"
          }

          test "the contract's chartTable rules pass on every case, and this tier checks each one" {
              match contractRules () with
              | None -> skiptest "wire-format-fixtures/ not found"
              | Some rules ->
                  Expect.equal
                      (Set.ofList rules)
                      (checks |> Map.keys |> Set.ofSeq)
                      "the contract's rules and this tier's checks are the same set"

                  for name, node in ChartTableCases.cases do
                      let html = renderOn node

                      match disclosureOf html with
                      | None -> ()
                      | Some d ->
                          for rule in rules do
                              let check = Map.find rule checks
                              Expect.isEmpty (check html d) (sprintf "%s: rule %s" name rule)
          }

          test "a table past the row limit holds the limit and states the count" {
              let rows =
                  [ for i in 1 .. Charts.tableRowLimit + 5 ->
                        Map.ofList [ "x", box (sprintf "r%d" i); "y", box (float i) ] ]

              let node =
                  Fuaran.chart
                      "long"
                      { Defaults.chart<obj> with
                          Kind = ChartKind.Line
                          XField = "x"
                          YFields = [ "y" ]
                          Source = Binding.Static(Some(Seq.ofList rows)) }

              let d = disclosureOf (renderOn node) |> Option.get
              let t = children "table" d |> List.exactlyOne
              Expect.equal (children "tbody" t |> List.collect (children "tr")).Length Charts.tableRowLimit "the limit"

              Expect.equal
                  (children "p" d |> List.map text)
                  [ ElementPlan.ChartTablePlan.limitText Charts.tableRowLimit (Charts.tableRowLimit + 5) ]
                  "the count stated"
          } ]
