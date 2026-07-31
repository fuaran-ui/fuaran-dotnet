module Fuaran.Demo.Main

// ============================================================================
//  Fuaran demo — browser entry point
//
//  Constructs the §4c canonical authoring example (revenue Metric + editable
//  grid) via Fuaran's smart constructors, mounts it via Fable.Elmish.React
//  through the session-3a/3b renderer. Adds a second pass exercising every
//  new session-3b component (Select / Form / Filters / FileUpload / Chart /
//  Table / Map / GridLayout / SplitPanel / Tabs / Stepper / Sparkline /
//  Markdown) so the visible page covers the full kind dispatch.
//
//  Mount target is `#fuaran-demo-root` per index.html.  Elmish + Fable.Elmish.React
//  drive the loop; the renderer's `Render.render` projects each `Node<'Msg>`
//  to a React element each update.
// ============================================================================

open Fable.Core.JsInterop
open Elmish
open Elmish.React
open Fuaran.UI
open Fuaran.UI.LayoutObserver
open Fuaran.UI.Types
open Fuaran.UI.Renderer

importSideEffects "./index.css"
// AG Grid + AG Charts adapter CSS — Vite
// resolves these from node_modules; the renderer's AgGridAdapter / AgChartAdapter
// emit DOM expecting the alpine theme + ag-charts default styles.
importSideEffects "ag-grid-community/styles/ag-grid.css"
importSideEffects "ag-grid-community/styles/ag-theme-alpine.css"

// ─── Domain shape used by the canonical example ────────────────────────────

type Channel =
    { RowId: int
      Channel: string
      MediaType: string
      RefGrp: float
      AudienceShare: float }

type TotalRevenue = { amount: float; trendPct: float }

// ─── Model + Msg ───────────────────────────────────────────────────────────

type Model =
    { Channels: Channel list
      SelectedRow: int option
      ContributorPick: string option
      TextFilter: string
      FormText: string
      FormChoice: string option
      FormSubmitted: bool
      ActiveTab: int
      Step: int }

type Msg =
    | UpdateRefGrp of rowId: int * v: float
    | SelectRow of int
    | PickContributor of string option
    | SetTextFilter of string
    | SetFormText of string
    | SetFormChoice of string option
    | SubmitForm
    | FilesSelected of FileSelection list
    | SetTab of int
    | NextStep
    | PrevStep

let private initialChannels =
    [ { RowId = 1
        Channel = "Search"
        MediaType = "Digital"
        RefGrp = 18.4
        AudienceShare = 0.42 }
      { RowId = 2
        Channel = "Display"
        MediaType = "Digital"
        RefGrp = 12.1
        AudienceShare = 0.18 }
      { RowId = 3
        Channel = "TV — peak"
        MediaType = "Linear"
        RefGrp = 35.6
        AudienceShare = 0.27 }
      { RowId = 4
        Channel = "TV — offpeak"
        MediaType = "Linear"
        RefGrp = 22.3
        AudienceShare = 0.09 }
      { RowId = 5
        Channel = "OOH"
        MediaType = "Out of home"
        RefGrp = 7.8
        AudienceShare = 0.04 } ]

let init () : Model * Cmd<Msg> =
    { Channels = initialChannels
      SelectedRow = None
      ContributorPick = None
      TextFilter = ""
      FormText = ""
      FormChoice = None
      FormSubmitted = false
      ActiveTab = 0
      Step = 0 },
    Cmd.none

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | UpdateRefGrp(rowId, v) ->
        let channels =
            model.Channels
            |> List.map (fun c -> if c.RowId = rowId then { c with RefGrp = v } else c)

        { model with Channels = channels }, Cmd.none
    | SelectRow rowId -> { model with SelectedRow = Some rowId }, Cmd.none
    | PickContributor v -> { model with ContributorPick = v }, Cmd.none
    | SetTextFilter v -> { model with TextFilter = v }, Cmd.none
    | SetFormText v -> { model with FormText = v }, Cmd.none
    | SetFormChoice v -> { model with FormChoice = v }, Cmd.none
    | SubmitForm -> { model with FormSubmitted = true }, Cmd.none
    | FilesSelected files ->
        Browser.Dom.console.info ("[demo] files selected:", box files)
        model, Cmd.none
    | SetTab i -> { model with ActiveTab = i }, Cmd.none
    | NextStep ->
        { model with
            Step = min 3 (model.Step + 1) },
        Cmd.none
    | PrevStep ->
        { model with
            Step = max 0 (model.Step - 1) },
        Cmd.none

// ─── §4c canonical example (revenue Metric + editable grid) ───────────────────

// fuaran#665 — terse named-cell reads off the projected `Row`.
let private cellText (field: string) (r: Row) : string =
    defaultArg (Map.tryFind field r |> Option.map string) ""

let private cellFloat (field: string) (r: Row) : float =
    match Map.tryFind field r with
    | Some v -> unbox<float> v
    | None -> 0.0

let private cellInt (field: string) (r: Row) : int =
    match Map.tryFind field r with
    | Some v -> unbox<int> v
    | None -> 0

let private canonicalSection (model: Model) : Node<Msg> =
    Fuaran.dashboard
        "channel-analysis"
        { Defaults.dashboard with
            Children =
                [ Fuaran.metric
                      "revenue-metric"
                      { Defaults.metric with
                          Label = TextSource.Literal "Revenue"
                          Value = binding.query "totalRevenue" (fun (r: TotalRevenue) -> r.amount)
                          Format = format.currency "GBP"
                          Tone = ToneVariant.Brand
                          Trend = Some(binding.query "totalRevenue" (fun (r: TotalRevenue) -> r.trendPct))
                          TrendFormat = Some(format.percent (Some 1)) }
                  |> Node.onEmpty (Fuaran.markdown "no-data" "No revenue data yet.")

                  // fuaran#665 — `toRow` projects each Channel to the wire-expressible
                  // Row; accessors and handlers read the projected cells by name.
                  Fuaran.grid
                      "channel-grid"
                      (fun (r: Channel) ->
                          Map.ofList
                              [ "rowId", box r.RowId
                                "channel", box r.Channel
                                "mediaType", box r.MediaType
                                "refGrp", box r.RefGrp
                                "audienceShare", box r.AudienceShare ])
                      { Defaults.grid<Channel, Msg> with
                          Source = binding.query "channelRows" id
                          RowKey = cellText "rowId"
                          Columns =
                              [ Column.text "Channel" (cellText "channel")
                                Column.text "Media type" (cellText "mediaType")
                                Column.numeric "GRPs" (cellFloat "refGrp")
                                |> Column.withFormat (format.number (Some 1))
                                |> Column.editable (fun row v ->
                                    match v with
                                    | CellValue.Numeric n -> Action.dispatch (UpdateRefGrp(cellInt "rowId" row, n))
                                    | _ -> Action.Chain [])
                                Column.numeric "Audience share" (cellFloat "audienceShare")
                                |> Column.withFormat (format.percent (Some 1)) ]
                          OnRowClick = Some(fun row -> Action.dispatch (SelectRow(cellInt "rowId" row))) }
                  |> Node.onEmpty (Fuaran.markdown "no-channels" "Load a fitted-parameters file to see channels.")
                  |> Node.onLoading (Fuaran.skeleton "loading" 5) ] }

// ─── Showcase of session-3b components ────────────────────────────────────

let private contributorOptions: SelectOption list =
    [ { Value = "buyer-peer"
        Label = "Buyer peer" }
      { Value = "seller-peer"
        Label = "Seller peer" }
      { Value = "portal-peer"
        Label = "Portal peer" } ]

let private categoryOptions: SelectOption list =
    [ { Value = "audience"
        Label = "Audience" }
      { Value = "performance"
        Label = "Performance" }
      { Value = "attribution"
        Label = "Attribution" } ]

let private session3bShowcase (model: Model) : Node<Msg> =
    Fuaran.dashboard
        "session3b-showcase"
        { Defaults.dashboard with
            Children =
                [ Fuaran.markdown
                      "showcase-intro"
                      "## Session 3b — new components\n\nEvery component below was a placeholder in session 3a and lights up in 3b."

                  // Filters + Select
                  Fuaran.filters
                      "showcase-filters"
                      [ { Defaults.filter<Msg> with
                            Name = "text-filter"
                            Label = TextSource.Literal "Search channels"
                            Kind =
                                FormFieldKind.Text(
                                    Some(binding.state "textFilter" ""),
                                    Some(fun v -> Action.dispatch (SetTextFilter v))
                                ) } ]

                  Fuaran.select
                      "contributor-peer"
                      { Defaults.select<Msg> with
                          Label = TextSource.Literal "Contributor peer"
                          Source = Binding.Static(Some contributorOptions)
                          Value = binding.stateNoDefault "contributorPick"
                          OnChange = Some(fun v -> Action.dispatch (PickContributor v))
                          Placeholder = Some(TextSource.Literal "Choose a peer…") }
                  |> Node.onEmpty (Fuaran.markdown "no-contributors" "No Contributor peers configured.")

                  // GridLayout (two columns) wrapping Form + a Card with badges
                  Fuaran.gridLayout
                      "showcase-row"
                      { Defaults.gridLayout<Msg> with
                          Cols = 2
                          Children =
                              [ Fuaran.form
                                    "showcase-form"
                                    { Defaults.form<Msg> with
                                        SubmitLabel = TextSource.Literal "Submit"
                                        OnSubmit = Action.dispatch SubmitForm
                                        Fields =
                                            [ { Defaults.formField<Msg> with
                                                  Id = "form-text"
                                                  Label = TextSource.Literal "Cohort name"
                                                  Required = true
                                                  Kind =
                                                      FormFieldKind.Text(
                                                          Some(binding.state "formText" ""),
                                                          Some(fun v -> Action.dispatch (SetFormText v))
                                                      ) }
                                              { Defaults.formField<Msg> with
                                                  Id = "form-choice"
                                                  Label = TextSource.Literal "Category"
                                                  Kind =
                                                      FormFieldKind.Choice(
                                                          Binding.Static(Some categoryOptions),
                                                          Some(binding.stateNoDefault "formChoice"),
                                                          Some(fun v -> Action.dispatch (SetFormChoice v))
                                                      )
                                                  Help = Some(TextSource.Literal "Pick the dominant cohort dimension.") } ] }

                                Fuaran.card
                                    "badge-card"
                                    { Defaults.card<Msg> with
                                        Heading = Some(TextSource.Literal "Tier badges")
                                        Children =
                                            [ Fuaran.markdown
                                                  "badge-intro"
                                                  "Semantic tone variants — same shape, themed at the shell."
                                              Fuaran.dashboard
                                                  "badge-row"
                                                  { Defaults.dashboard with
                                                      Children =
                                                          [ Fuaran.markdown
                                                                "_b1"
                                                                "**Default** · **Brand** · **Success** · **Warning** · **Critical**" ] } ] } ] }

                  // SplitPanel demonstration
                  Fuaran.splitPanel
                      "showcase-split"
                      { Defaults.splitPanel<Msg> with
                          Weight = 0.65
                          Children =
                              [ Fuaran.card
                                    "split-left"
                                    { Defaults.card<Msg> with
                                        Heading = Some(TextSource.Literal "Wider pane — 65%")
                                        Children =
                                            [ Fuaran.markdown
                                                  "_sp_left"
                                                  "The split panel takes a `Weight` between 0 and 1; the first child gets that share." ] }
                                Fuaran.card
                                    "split-right"
                                    { Defaults.card<Msg> with
                                        Heading = Some(TextSource.Literal "Narrower pane — 35%")
                                        Children = [ Fuaran.markdown "_sp_right" "Right-pane content." ] } ] }

                  // Tabs
                  Fuaran.tabs
                      "showcase-tabs"
                      { Defaults.tabs<Msg> with
                          Orientation = Orientation.Horizontal
                          Children =
                              [ Fuaran.card
                                    "tab-overview"
                                    { Defaults.card<Msg> with
                                        Heading = Some(TextSource.Literal "Overview")
                                        Children = [ Fuaran.markdown "_tab1" "Tabs use Card heading as the tab label." ] }
                                Fuaran.card
                                    "tab-details"
                                    { Defaults.card<Msg> with
                                        Heading = Some(TextSource.Literal "Details")
                                        Children =
                                            [ Fuaran.markdown
                                                  "_tab2"
                                                  "Each child is a tab pane; bodies render below the tab bar." ] } ] }

                  // Stepper
                  Fuaran.stepper
                      "showcase-stepper"
                      { Defaults.stepper<Msg> with
                          ActiveStep = binding.state "stepperIndex" model.Step
                          Children =
                              [ Fuaran.markdown "step-1" "Step 1 — define your cohort."
                                Fuaran.markdown "step-2" "Step 2 — choose dimensions."
                                Fuaran.markdown "step-3" "Step 3 — review and run." ] }

                  // FileUpload
                  Fuaran.fileUpload
                      "showcase-upload"
                      { Defaults.fileUpload<Msg> with
                          Label = TextSource.Literal "Upload fitted-parameters file"
                          Accept = [ ".csv"; ".json" ]
                          Multiple = false
                          OnSelect = Some(fun files -> Action.dispatch (FilesSelected files)) }

                  // Chart (falls back to labelled placeholder — no AG Charts adapter wired)
                  Fuaran.chart
                      "showcase-chart"
                      { Defaults.chart<Msg> with
                          Source =
                              // fuaran#665 — the chart source is a typed Row feed; project each
                              // Channel to the named cells the XField/YFields address.
                              binding.query "channelRows" (fun (rs: Channel list) ->
                                  rs
                                  |> Seq.map (fun r -> Map.ofList [ "Channel", box r.Channel; "RefGrp", box r.RefGrp ]))
                          Kind = ChartKind.Bar
                          XField = "Channel"
                          YFields = [ "RefGrp" ]
                          Title = Some(TextSource.Literal "GRPs by channel") }

                  // Table (static, non-bound)
                  Fuaran.table
                      "showcase-table"
                      { Defaults.table<Msg> with
                          Headers =
                              [ TextSource.Literal "Privacy tier"
                                TextSource.Literal "Description"
                                TextSource.Literal "Status" ]
                          Rows =
                              [ [ TextSource.Literal "Tier 0"
                                  TextSource.Literal "Plaintext federation"
                                  TextSource.Literal "Default" ]
                                [ TextSource.Literal "Tier 1"
                                  TextSource.Literal "HMAC pseudonymisation"
                                  TextSource.Literal "Available" ]
                                [ TextSource.Literal "Tier 2"
                                  TextSource.Literal "Homomorphic χ²"
                                  TextSource.Literal "Beta" ] ] }

                  // Map placeholder
                  let mapMarkers: MapMarker list =
                      [ { Latitude = 51.5074
                          Longitude = -0.1278
                          Label = "London" }
                        { Latitude = 40.7128
                          Longitude = -74.006
                          Label = "New York" } ]

                  Fuaran.map
                      "showcase-map"
                      { Defaults.map<Msg> with
                          Source = Binding.Static(Some mapMarkers)
                          CentreLatitude = 30.0
                          CentreLongitude = -30.0
                          Zoom = 3 } ] }

let private root (model: Model) : Node<Msg> =
    Fuaran.dashboard
        "demo-root"
        { Defaults.dashboard with
            Children = [ canonicalSection model; session3bShowcase model ] }

// ─── BindingSources construction ──────────────────────────────────────────
//
// The renderer consults `BindingSources` for `binding.query` / `binding.state`
// / `binding.filter`. We update it from the Elmish model each render so the
// queries see the live data.

let private buildSources (model: Model) : BindingResolver.BindingSources =
    let stateMap =
        Map.ofList
            [ "textFilter", box model.TextFilter
              "formText", box model.FormText
              "formChoice", box model.FormChoice
              "contributorPick", box model.ContributorPick
              "stepperIndex", box model.Step ]

    let queryMap =
        Map.ofList
            [ "totalRevenue", box { amount = 142_500.0; trendPct = 0.083 }
              "channelRows",
              box (
                  model.Channels
                  |> List.filter (fun c ->
                      model.TextFilter = ""
                      || c.Channel.ToLowerInvariant().Contains(model.TextFilter.ToLowerInvariant()))
              ) ]

    { BindingResolver.empty with
        QueryResults = queryMap
        State = stateMap
        I18n = Map.empty }

// ─── View — Render the Fuaran tree through the renderer ─────────────────────

// ─── Layout observer wired at boot ─────────────────
//
// Single instance constructed once; the MutationObserver inside
// auto-discovers every `[data-fuaran-node-id]` element the renderer
// emits and binds a ResizeObserver to each. Drag the viewport
// narrow enough to squeeze the Stack-in-SplitPanel section and an
// OverflowHorizontal flag will surface — verify via devtools:
//   > window.__fuarandemo_layout.Observe('split-panel-demo')
// or the AI tool surface (when wired to a Fuaran host with an AI
// runtime). For the demo we don't ship the inspector panel — the
// observer's behaviour is verifiable via the live observations.

let private layoutObserver: ILayoutObserver =
    BrowserLayoutObserver.create () :> ILayoutObserver

// Expose on `window.__fuarandemo_layout` so the demo can be probed
// from devtools without an AI runtime.
let private exposeForDevtools () : unit =
    Fable.Core.JsInterop.emitJsStatement layoutObserver "globalThis.__fuarandemo_layout = $0"

let private fuaranRuntime: Runtime.IFuaranRuntime =
    BrowserRuntime.createWithLayoutObserver layoutObserver

let view (model: Model) (dispatch: Msg -> unit) =
    let tree = root model
    let sources = buildSources model

    let ctx: Render.RenderContext<Msg> =
        { Sources = sources
          Runtime = fuaranRuntime
          VisAdapter = AgAdapter.adapter<Msg>
          Dispatch = dispatch
          TelemetrySink = None
          InErrorBoundary = false
          Fragments = Map.empty
          ExpandingFragments = Set.empty
          Scope = None
          SessionContext = Map.empty }

    // Phase 90 — register the in-page introspection REPL over the live tree.
    // `debug = true` here means "opt in"; `DebugGlobal.shouldRegister` still
    // requires a DEBUG build, so a release Fable build dead-code-eliminates the
    // registration and `window.__fuaran` is `undefined`. Try it in DevTools:
    //   > __fuaran.getNodeState("revenue-metric")
    //   > __fuaran.getBindingValue("revenue-metric", "Source")
    //   > console.log(__fuaran.help())
    // No apply handler is wired (this demo's Elmish model is domain-shaped, not
    // a raw Node tree), so `__fuaran.apply(...)` returns the `unwired` envelope.
    DebugGlobal.register true tree sources fuaranRuntime None

    Render.render ctx tree

// ─── Boot ─────────────────────────────────────────────────────────────────

exposeForDevtools ()

Program.mkProgram init update view
|> Program.withReactSynchronous "fuaran-demo-root"
|> Program.run
