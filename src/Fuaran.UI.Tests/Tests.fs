module Fuaran.UI.Tests.Construction

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

// ============================================================================
//  Session 2 construction tests — one per seed component (§4c lines 504–542)
//
//  Each test (a) constructs a Node<Msg> via the seed component's smart-ctor
//  using `{ Defaults.X with ... }`, (b) pattern-matches the resulting Node's
//  Id + Kind shape. (Node carries function-typed fields and so does not
//  support structural equality — pattern matching is the assertion shape.)
//  No renderer assertions yet (session 3+).
// ============================================================================

type SampleRow =
    { RowId: int
      Channel: string
      RefGrp: float }

type Msg =
    | SelectRow of int
    | UpdateRefGRP of int * float

let private idOf (node: Node<'Msg>) : string = node.Id

let private literalOf (text: TextSource) : string =
    match text with
    | TextSource.Literal s -> s
    | other -> failtestf "Expected TextSource.Literal, got %A" other

[<Tests>]
let tests =
    testList
        "Fuaran seed-component construction"
        [ test "Fuaran.dashboard wraps a DashboardSpec with the given id" {
              let node: Node<Msg> =
                  Fuaran.dashboard
                      "channel-analysis"
                      { Defaults.dashboard<Msg> with
                          Children = [] }

              Expect.equal (idOf node) "channel-analysis" "Id is set"

              match node.Kind with
              | NodeKind.Box(spec) -> Expect.equal spec.Children.Length 0 "Children is empty"
              | other -> failtestf "Expected NodeKind.Box, got %A" other
          }

          test "Fuaran.metric wraps a MetricSpec with Defaults.metric overrides applied" {
              let node: Node<Msg> =
                  Fuaran.metric
                      "revenue-metric"
                      { Defaults.metric with
                          Label = TextSource.Literal "Revenue"
                          Value = binding.query "totalRevenue" (fun (r: {| amount: float |}) -> r.amount)
                          Format = format.currency "GBP"
                          Tone = ToneVariant.Brand }

              Expect.equal (idOf node) "revenue-metric" "Id is set"

              match node.Kind with
              | NodeKind.Metric(spec) ->
                  Expect.equal (literalOf spec.Label) "Revenue" "Label set"
                  Expect.equal spec.Tone ToneVariant.Brand "Tone overridden"

                  match spec.Format with
                  | CellFormat.Currency code -> Expect.equal code "GBP" "Currency code"
                  | other -> failtestf "Expected CellFormat.Currency, got %A" other
              | other -> failtestf "Expected NodeKind.Metric, got %A" other
          }

          test "Fuaran.grid boxes a typed GridSpecOf<'row,'Msg> into a row-erased GridSpec" {
              let node: Node<Msg> =
                  Fuaran.grid
                      "channel-grid"
                      { Defaults.grid<SampleRow, Msg> with
                          Source = binding.query "channelRows" id
                          RowKey = (fun r -> string r.RowId)
                          Columns =
                              [ Column.text "Channel" (fun (r: SampleRow) -> r.Channel)
                                Column.numeric "GRPs" (fun (r: SampleRow) -> r.RefGrp)
                                |> Column.editable (fun row _ -> Action.dispatch (UpdateRefGRP(row.RowId, 0.0))) ]
                          OnRowClick = Some(fun row -> Action.dispatch (SelectRow row.RowId)) }

              Expect.equal (idOf node) "channel-grid" "Id is set"

              match node.Kind with
              | NodeKind.DataGrid(spec) ->
                  Expect.equal spec.Columns.Length 2 "Two columns boxed in"
                  Expect.equal spec.Editable false "Editable stays default"
                  Expect.isSome spec.OnRowClick "OnRowClick wired"
              | other -> failtestf "Expected NodeKind.DataGrid, got %A" other
          }

          test "Column.withPill sets CellKind.Pill reusing the column value as the label" {
              let statusTone (r: SampleRow) =
                  if r.RefGrp > 0.0 then
                      ToneVariant.Brand
                  else
                      ToneVariant.Default

              let col: Column<SampleRow, Msg> =
                  Column.text "Status" _.Channel |> Column.withPill statusTone

              match col.Kind with
              | CellKind.Pill(label, tone) ->
                  let row =
                      { RowId = 1
                        Channel = "Active"
                        RefGrp = 3.0 }

                  Expect.equal (literalOf (label row)) "Active" "Pill label reuses the column value"
                  Expect.equal (tone row) ToneVariant.Brand "Tone fn wired"

                  let zeroRow = { row with RefGrp = 0.0 }
                  Expect.equal (tone zeroRow) ToneVariant.Default "Tone fn maps the row"
              | other -> failtestf "Expected CellKind.Pill, got %A" other
          }

          test "Column.erase boxes a withPill column to CellKindErased.Pill (round-trip)" {
              let statusTone (r: SampleRow) =
                  if r.RefGrp > 0.0 then
                      ToneVariant.Brand
                  else
                      ToneVariant.Default

              let erased: ColumnErased<Msg> =
                  Column.text "Status" _.Channel |> Column.withPill statusTone |> Column.erase

              match erased.Kind with
              | CellKindErased.Pill(label, tone) ->
                  let row: obj =
                      box
                          { RowId = 1
                            Channel = "Active"
                            RefGrp = 3.0 }
                      |> Unchecked.nonNull

                  Expect.equal (literalOf (label row)) "Active" "Erased pill label unboxes the row"
                  Expect.equal (tone row) ToneVariant.Brand "Erased tone fn unboxes the row"
              | other -> failtestf "Expected CellKindErased.Pill, got %A" other
          }

          test "Fuaran.markdown positional-shorthand builds Display.Markdown with the body text" {
              let node: Node<Msg> = Fuaran.markdown "no-data" "No revenue data yet."

              Expect.equal (idOf node) "no-data" "Id is set"

              match node.Kind with
              | NodeKind.Markdown(spec) -> Expect.equal (literalOf spec.Text) "No revenue data yet." "Body text wired"
              | other -> failtestf "Expected NodeKind.Markdown, got %A" other
          }

          test "Fuaran.button wraps a ButtonSpec with the given OnClick action" {
              let node: Node<Msg> =
                  Fuaran.button
                      "submit-button"
                      { Defaults.button<Msg> with
                          Label = TextSource.Literal "Submit"
                          OnClick = Action.dispatch (SelectRow 0)
                          Variant = ButtonVariant.Primary }

              Expect.equal (idOf node) "submit-button" "Id is set"

              match node.Kind with
              | NodeKind.Button(spec) ->
                  Expect.equal (literalOf spec.Label) "Submit" "Label set"
                  Expect.equal spec.Variant ButtonVariant.Primary "Variant overridden"

                  match spec.OnClick with
                  | Action.Dispatch(SelectRow 0) -> ()
                  | other -> failtestf "Expected Action.Dispatch(SelectRow 0), got %A" other
              | other -> failtestf "Expected NodeKind.Button, got %A" other
          }

          test "Fuaran.callout wraps a CalloutSpec with tone + body" {
              let node: Node<Msg> =
                  Fuaran.callout
                      "tier-banner"
                      { Defaults.callout with
                          Tone = ToneVariant.Warning
                          Body = TextSource.Literal "Tier 1 — pseudonymised."
                          Dismissable = true }

              Expect.equal (idOf node) "tier-banner" "Id is set"

              match node.Kind with
              | NodeKind.Callout(spec) ->
                  Expect.equal spec.Tone ToneVariant.Warning "Tone overridden"
                  Expect.equal spec.Dismissable true "Dismissable overridden"
              | other -> failtestf "Expected NodeKind.Callout, got %A" other
          }

          test
              "Fuaran.progress wraps a ProgressSpec with indeterminate-with-caveat shape (§4k indeterminate-progress rule)" {
              let node: Node<Msg> =
                  Fuaran.progress
                      "joint-progress"
                      { Defaults.progress with
                          Label = Some(TextSource.Literal "Tree depth")
                          Caveat = Some(TextSource.Literal "Bar tracks depth, not completion %")
                          Indeterminate = true }

              Expect.equal (idOf node) "joint-progress" "Id is set"

              match node.Kind with
              | NodeKind.Progress(spec) ->
                  Expect.equal spec.Indeterminate true "Indeterminate set"
                  Expect.isSome spec.Caveat "Caveat present"
              | other -> failtestf "Expected NodeKind.Progress, got %A" other
          }

          test "Defaults.metric.Source resolves to NotResolved (un-overridden default sentinel)" {
              // Regression: previously `noBinding` encoded as
              // `Binding.Static Unchecked.defaultof<float>`, which the
              // resolver returned as `Resolved 0.0` — a forgotten
              // `Source =` override produced a `0`-valued Metric silently.
              // Now `noBinding` encodes as `Binding.Query NotProvidedSentinel`
              // and the resolver short-circuits to `NotResolved`, freeing
              // the renderer to substitute the `OnLoading` slot.
              let resolution = BindingResolver.resolve BindingResolver.empty Defaults.metric.Value

              Expect.equal
                  resolution
                  BindingResolver.NotResolved
                  "Defaults.metric.Source must resolve to NotResolved — a missing override must surface through the state-slot dispatch rather than silently formatting as 0"
          }

          test "Defaults.progress.Fraction resolves to Resolved 0.0 (explicit Static, not a sentinel)" {
              // Counter-test to the Metric one above: Progress's default
              // fraction IS legitimately `Binding.Static 0.0` (a 0%
              // progress bar is a valid display, not a missing-source
              // signal). The sentinel encoding must not over-fire.
              let resolution =
                  BindingResolver.resolve BindingResolver.empty Defaults.progress.Fraction

              Expect.equal
                  resolution
                  (BindingResolver.Resolved 0.0)
                  "Defaults.progress.Fraction is Static 0.0; resolves to Resolved 0.0"
          }

          test "Defaults.grid Source resolves to NotResolved (un-overridden default sentinel)" {
              let resolution =
                  BindingResolver.resolve BindingResolver.empty (Defaults.grid<SampleRow, Msg>).Source

              match resolution with
              | BindingResolver.NotResolved -> ()
              | other -> failtestf "Expected NotResolved, got %A" other
          }

          test "Sentinel-name Query short-circuits to NotResolved even if a consumer mistakenly registers it" {
              // Belt-and-braces: the resolver's short-circuit fires
              // before consulting QueryResults, so a hypothetical
              // (and forbidden) consumer-registered entry under the
              // sentinel name cannot accidentally resolve the default.
              // `obj()` sidesteps F# 10's conservative nullness on `box`
              // (which signals `obj | null` even for value types); the
              // short-circuit doesn't inspect the value, only the name.
              let pollutedSources =
                  { BindingResolver.empty with
                      QueryResults = Map.ofList [ Defaults.NotProvidedSentinel, obj () ] }

              let resolution = BindingResolver.resolve pollutedSources Defaults.metric.Value

              Expect.equal
                  resolution
                  BindingResolver.NotResolved
                  "Sentinel short-circuit must fire before QueryResults lookup"
          }

          // ── Phase 137: Binding.Computed reads live state via BindingContext ──

          test "Binding.Computed reads a State slot through ctx.TryGetState (busy = true)" {
              // The computed-from-state shape: a Computed closure projects a
              // `Binding.State` slot into derived label text. The resolver must
              // hand the closure a context populated from the live `State` bag.
              let computed: Binding<string> =
                  Binding.Computed(fun (o: obj) ->
                      let ctx = unbox<BindingContext> o

                      if ctx.TryGetState<bool> "busy" = Some true then
                          "Working…"
                      else
                          "Ready")

              let sources =
                  { BindingResolver.empty with
                      State = Map.ofList [ "busy", box true |> Unchecked.nonNull ] }

              Expect.equal
                  (BindingResolver.resolve sources computed)
                  (BindingResolver.Resolved "Working…")
                  "Computed closure must read the live 'busy' state slot as Some true"
          }

          test "Binding.Computed sees the State default branch when the key is absent" {
              let computed: Binding<string> =
                  Binding.Computed(fun (o: obj) ->
                      let ctx = unbox<BindingContext> o

                      if ctx.TryGetState<bool> "busy" = Some true then
                          "Working…"
                      else
                          "Ready")

              // Empty state bag — TryGetState returns None, the closure takes
              // the else branch.
              Expect.equal
                  (BindingResolver.resolve BindingResolver.empty computed)
                  (BindingResolver.Resolved "Ready")
                  "Absent state key must resolve to None inside the closure (no throw)"
          }

          test "BindingContext.tryGetState pipeline form resolves a typed slot" {
              let computed: Binding<string> =
                  binding.computed (fun ctx ->
                      match ctx |> BindingContext.tryGetState<int> "count" with
                      | Some n -> sprintf "count=%d" n
                      | None -> "count=?")

              let sources =
                  { BindingResolver.empty with
                      State = Map.ofList [ "count", box 7 |> Unchecked.nonNull ] }

              Expect.equal
                  (BindingResolver.resolve sources computed)
                  (BindingResolver.Resolved "count=7")
                  "ctx |> BindingContext.tryGetState<int> must unbox the live state slot"
          }

          test "BindingContext.TryGetState returns None on a runtime type mismatch (no throw)" {
              // `busy` is stored as an int but read as a bool — the unbox fails
              // under the .NET runner; TryGetState swallows it to None rather
              // than letting the Computed closure throw.
              let computed: Binding<string> =
                  Binding.Computed(fun (o: obj) ->
                      let ctx = unbox<BindingContext> o

                      match ctx.TryGetState<bool> "busy" with
                      | Some true -> "on"
                      | Some false -> "off"
                      | None -> "unknown")

              let sources =
                  { BindingResolver.empty with
                      State = Map.ofList [ "busy", box 1 |> Unchecked.nonNull ] }

              Expect.equal
                  (BindingResolver.resolve sources computed)
                  (BindingResolver.Resolved "unknown")
                  "A type mismatch must resolve to None, keeping the closure total"
          }

          test "Node.onLoading + Node.onEmpty postfix pipes populate the StateBehaviour slots" {
              let node: Node<Msg> =
                  Fuaran.metric
                      "revenue-metric"
                      { Defaults.metric with
                          Label = TextSource.Literal "Revenue" }
                  |> Node.onLoading (Fuaran.skeleton "loading" 5)
                  |> Node.onEmpty (Fuaran.markdown "no-data" "No revenue data yet.")

              Expect.isSome (node.State |> Option.bind _.OnLoading) "OnLoading wired"
              Expect.isSome (node.State |> Option.bind _.OnEmpty) "OnEmpty wired"
              Expect.isNone (node.State |> Option.bind _.OnError) "OnError stays default"
          }

          // ─── Feliz-parity additive shapes ───────────────────────

          test "Defaults.stack.Wrap is false (preserves legacy behaviour)" {
              Expect.equal Defaults.stack<Msg>.Wrap false "Stack.Wrap defaults to false"
          }

          test "Fuaran.stack honours Wrap = true override" {
              let node: Node<Msg> =
                  Fuaran.stack
                      "chip-strip"
                      { Defaults.stack<Msg> with
                          Orientation = Orientation.Horizontal
                          Wrap = true }

              match node.Kind with
              | NodeKind.Box(spec) ->
                  match spec.Layout with
                  | LayoutMode.Flex(_, wrap, _) -> Expect.equal wrap true "Wrap = true propagated"
                  | other -> failtestf "Expected LayoutMode.Flex, got %A" other
              | other -> failtestf "Expected NodeKind.Box, got %A" other
          }

          test "Defaults.heading.Variant is Standard (preserves legacy behaviour)" {
              Expect.equal Defaults.heading.Variant HeadingVariant.Standard "Heading.Variant defaults to Standard"
          }

          test "Fuaran.heading honours Variant override + smart-ctor wires the kind" {
              let node: Node<Msg> =
                  Fuaran.heading
                      "tax-year-banner"
                      { Defaults.heading with
                          Text = TextSource.Literal "Tax year 2025/26"
                          Variant = HeadingVariant.Eyebrow }

              Expect.equal (idOf node) "tax-year-banner" "Id is set"

              match node.Kind with
              | NodeKind.Heading(spec) ->
                  Expect.equal spec.Variant HeadingVariant.Eyebrow "Variant = Eyebrow propagated"
              | other -> failtestf "Expected NodeKind.Heading, got %A" other
          }

          test "Fuaran.labelValueRow wraps a LabelValueRowSpec with the source binding + format" {
              let node: Node<Msg> =
                  Fuaran.labelValueRow
                      "row-take-home"
                      { Defaults.labelValueRow with
                          Label = TextSource.Literal "Take-home"
                          Value = binding.``static`` 32500.0
                          Format = format.currency "GBP"
                          Emphasis = true }

              Expect.equal (idOf node) "row-take-home" "Id is set"

              match node.Kind with
              | NodeKind.LabelValueRow(spec) ->
                  Expect.equal spec.Emphasis true "Emphasis propagated"

                  match spec.Format with
                  | CellFormat.Currency code -> Expect.equal code "GBP" "Currency code set"
                  | other -> failtestf "Expected CellFormat.Currency, got %A" other
              | other -> failtestf "Expected NodeKind.LabelValueRow, got %A" other
          }

          test "Defaults.labelValueRow.Source resolves to NotResolved (same sentinel encoding as Metric)" {
              let resolution =
                  BindingResolver.resolve BindingResolver.empty Defaults.labelValueRow.Value

              Expect.equal
                  resolution
                  BindingResolver.NotResolved
                  "LabelValueRow's default Source must be the NotProvidedSentinel"
          }

          test "Fuaran.summaryList wraps a SummaryListSpec with optional heading + LabelValueRow children" {
              let node: Node<Msg> =
                  Fuaran.summaryList
                      "tax-breakdown"
                      { Defaults.summaryList<Msg> with
                          Heading = Some(TextSource.Literal "Tax breakdown")
                          Children =
                              [ Fuaran.labelValueRow
                                    "row-income-tax"
                                    { Defaults.labelValueRow with
                                        Label = TextSource.Literal "Income tax"
                                        Value = binding.``static`` 7486.0
                                        Format = format.currency "GBP" } ] }

              Expect.equal (idOf node) "tax-breakdown" "Id is set"

              match node.Kind with
              | NodeKind.SummaryList(spec) ->
                  Expect.equal spec.Children.Length 1 "One child wired"

                  match spec.Heading with
                  | Some(TextSource.Literal "Tax breakdown") -> ()
                  | other -> failtestf "Expected literal heading, got %A" other
              | other -> failtestf "Expected NodeKind.SummaryList, got %A" other
          }

          test "Fuaran.gridLayout default carries TemplateColumns = None (legacy shape preserved)" {
              // Existing callers see no behavioural change: the typed
              // `Cols: int` shape continues to drive the renderer's
              // `repeat({Cols}, 1fr)` emission. TemplateColumns must default
              // to None so existing fixtures encode byte-identical.
              let node: Node<Msg> =
                  Fuaran.gridLayout
                      "g1"
                      { Defaults.gridLayout<Msg> with
                          Cols = 3
                          Children = [] }

              match node.Kind with
              | NodeKind.Box(spec) ->
                  match spec.Layout with
                  | LayoutMode.Grid(cols, templateColumns, _) ->
                      Expect.equal cols 3 "Cols overridden"
                      Expect.equal templateColumns None "TemplateColumns defaults to None"
                  | other -> failtestf "Expected LayoutMode.Grid, got %A" other
              | other -> failtestf "Expected NodeKind.Box, got %A" other
          }

          test "Fuaran.gridLayoutTemplated pre-populates TemplateColumns (escape)" {
              // The smart-ctor sets `TemplateColumns = Some s` regardless of
              // what the supplied spec carries — the second positional arg
              // is the authoritative override. The renderer short-circuits
              // the Cols-based emission when TemplateColumns is Some.
              let node: Node<Msg> =
                  Fuaran.gridLayoutTemplated
                      "heatmap"
                      "100px repeat(3, minmax(30px, 1fr))"
                      { Defaults.gridLayout<Msg> with
                          Children = [] }

              match node.Kind with
              | NodeKind.Box(spec) ->
                  match spec.Layout with
                  | LayoutMode.Grid(_, templateColumns, _) ->
                      Expect.equal
                          templateColumns
                          (Some "100px repeat(3, minmax(30px, 1fr))")
                          "TemplateColumns wired verbatim"
                  | other -> failtestf "Expected LayoutMode.Grid, got %A" other
              | other -> failtestf "Expected NodeKind.Box, got %A" other
          }

          test "Fuaran.gridLayoutTemplated overrides a spec that already carried TemplateColumns" {
              // The explicit string arg wins over whatever the spec record
              // already carries — keeps the smart-ctor contract
              // unambiguous ("pre-populates" means "sets", not "merges").
              let node: Node<Msg> =
                  Fuaran.gridLayoutTemplated
                      "g2"
                      "1fr 2fr"
                      { Defaults.gridLayout<Msg> with
                          TemplateColumns = Some "auto 1fr auto"
                          Children = [] }

              match node.Kind with
              | NodeKind.Box(spec) ->
                  match spec.Layout with
                  | LayoutMode.Grid(_, templateColumns, _) ->
                      Expect.equal templateColumns (Some "1fr 2fr") "Explicit smart-ctor arg overrides the spec field"
                  | other -> failtestf "Expected LayoutMode.Grid, got %A" other
              | other -> failtestf "Expected NodeKind.Box, got %A" other
          } ]
