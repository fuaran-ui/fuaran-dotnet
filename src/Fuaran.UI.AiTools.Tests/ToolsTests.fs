module Fuaran.UI.AiTools.Tests.Tools

// ============================================================================
//  Runtime-introspection AI tools acceptance set.
//
//  The deterministic-contract acceptance criterion (verbatim from the phase
//  body):
//
//    The runtime introspection AI-tool surface (`fuaran.getNodeState` /
//    `getBindingValue` / `getRenderedDom`) returns deterministic
//    observable state from a rendered tree.
//
//  Each test pins one slice — same inputs, exact-shape output, no hidden
//  randomness. The fixture is the §4i example shape (dashboard with Metric
//  + Grid) so the tests anchor on the canonical worked example in
//  the Fuaran design specification.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.AiTools.Types
open Fuaran.UI.AiTools.Seams
open Fuaran.UI.AiTools

type Msg =
    | SelectRow of int
    | DismissBanner

// Renamed from `Row` (fuaran#665): `Fuaran.UI.Types.Row` is now the typed
// wire row, so the fixture's domain row carries its own name.
type ChannelRow = { RowId: int; Channel: string }

let private idOf (NodeId rawId) = rawId

// F# 10 `box _` types as `obj | null`; the BindingProbeSources contract and
// the fuaran#665 `Row` cell maps require nonnull-obj values. Same workaround
// Fuaran.UI.Ops.Tests's `nn` helper uses.
let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

// ─── Fixture tree ──────────────────────────────────────────────────────────

// F# anonymous records are nominal at the IL level — `{| amount: float |}`
// is a different runtime type from `{| amount: float; trendPct: float |}`,
// even when one is a structural subset of the other. Both binding
// accessors must therefore declare the SAME anonymous-record shape, and
// the registered query result must match exactly.
type TotalRevenueRow = {| amount: float; trendPct: float |}

let private revenueMetric: Node<Msg> =
    Fuaran.metric
        "revenue-metric"
        { Defaults.metric with
            Label = TextSource.Literal "Revenue"
            Value = binding.query "totalRevenue" (fun (r: TotalRevenueRow) -> r.amount)
            Format = format.currency "GBP"
            Tone = ToneVariant.Brand
            Trend = Some(binding.query "totalRevenue" (fun (r: TotalRevenueRow) -> r.trendPct)) }

let private channelGrid: Node<Msg> =
    // fuaran#665 — the required `toRow` projection; accessors read the
    // projected `Row` by name.
    Fuaran.grid
        "channel-grid"
        (fun (r: ChannelRow) -> Map.ofList [ "rowId", nn r.RowId; "channel", nn r.Channel ])
        { Defaults.grid<ChannelRow, Msg> with
            Source = binding.query "channelRows" id
            RowKey = (fun r -> defaultArg (Map.tryFind "rowId" r |> Option.map string) "")
            Columns = [ Column.text "Channel" (fun r -> defaultArg (Map.tryFind "channel" r |> Option.map string) "") ]
            OnRowClick =
                Some(fun r ->
                    Action.dispatch (
                        SelectRow(
                            match Map.tryFind "rowId" r with
                            | Some v -> unbox<int> v
                            | None -> 0
                        )
                    )) }

let private dashboard: Node<Msg> =
    Fuaran.dashboard
        "channel-analysis"
        { Defaults.dashboard<Msg> with
            Children = [ revenueMetric; channelGrid ] }

// ─── Seam fixtures ─────────────────────────────────────────────────────────

// `freshContext ()` carries a mutable in-memory sink as a record field —
// sharing it across tests would leak runtime-error entries between unrelated
// tests. `freshContext ()` swaps in a brand-new sink each call so
// order-of-execution doesn't matter. Every other seam-default is immutable
// (FixedClock, NoGeometryProbe, NoCurrentStateProbe) and safe to share.
let private freshContext () : IntrospectionContext =
    { Sources = Seams.emptyContext.Sources
      Geometry = Seams.noGeometry
      CurrentState = Seams.noCurrentState
      Errors = Seams.createInMemorySink ()
      Clock = Seams.fixedClock }

let private contextWithSources (sources: BindingProbeSources) : IntrospectionContext =
    { (freshContext ()) with
        Sources = sources }

let private withQuery (name: string) (value: obj) : BindingProbeSources =
    { (freshContext ()).Sources with
        QueryResults = Map.ofList [ name, value ] }

type FixedGeometry(rect: Geometry) =
    interface IGeometryProbe with
        member _.TryGetGeometry(_) = Some rect

type PerNodeGeometry(table: Map<string, Geometry>) =
    interface IGeometryProbe with
        member _.TryGetGeometry(NodeId rawId) = Map.tryFind rawId table

type FixedCurrentState(state: CurrentState, detail: StateDetail option) =
    interface ICurrentStateProbe with
        member _.TryGetCurrentState(_) = Some(state, detail)

[<Tests>]
let tests =
    testList
        "Fuaran.UI.AiTools introspection surface"
        [
          // ─── getNodeState: deterministic shape ─────────────────────────────

          test "getNodeState returns kind + id + props + bindings for the §4i example Metric" {
              let sources = withQuery "totalRevenue" (nn {| amount = 1234.56; trendPct = 0.07 |})

              let ctx = contextWithSources sources

              match Tools.getNodeState ctx [] dashboard (NodeId "revenue-metric") with
              | Ok state ->
                  Expect.equal (idOf state.Id) "revenue-metric" "Id echoed"
                  Expect.equal state.Kind "Metric" "Kind name from Introspect.kindName"

                  match state.Bindings with
                  | Some bindings ->
                      Expect.isTrue (Map.containsKey "Value" bindings) "Value slot present"
                      Expect.isTrue (Map.containsKey "Trend" bindings) "Trend slot present"

                      match Map.find "Value" bindings with
                      | ResolvedBindingResult.Resolved rb ->
                          Expect.equal rb.Expression "$queries.totalRevenue" "v1 short-form expression"

                          match rb.ResolvedValue with
                          | Some v -> Expect.equal (unbox<float> v) 1234.56 "Resolved float from the query accessor"
                          | None -> failtest "Expected ResolvedValue Some"
                      | other -> failtestf "Expected Resolved, got %A" other
                  | None -> failtest "Bindings block expected when no include filter"
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          test "getNodeState returns NodeNotFound for an unknown id" {
              match Tools.getNodeState (freshContext ()) [] dashboard (NodeId "ghost") with
              | Ok _ -> failtest "Expected Error"
              | Error err ->
                  Expect.equal err.Code IntrospectErrorCode.NodeNotFound "NodeNotFound code"
                  Expect.stringContains err.Message "ghost" "Error message names the id"
          }

          test "getNodeState filter [Props] omits the other four blocks" {
              match Tools.getNodeState (freshContext ()) [ IncludeKey.Props ] dashboard (NodeId "revenue-metric") with
              | Ok state ->
                  Expect.isSome state.Props "Props included"
                  Expect.isNone state.Bindings "Bindings omitted"
                  Expect.isNone state.CurrentState "CurrentState omitted"
                  Expect.isNone state.StateDetail "StateDetail omitted"
                  Expect.isNone state.Geometry "Geometry omitted"
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          test "getNodeState surfaces CurrentState from the probe" {
              let ctx =
                  { (freshContext ()) with
                      CurrentState = FixedCurrentState(CurrentState.Loading, Some { Detail = "query pending" }) }

              match
                  Tools.getNodeState
                      ctx
                      [ IncludeKey.CurrentState; IncludeKey.StateDetail ]
                      dashboard
                      (NodeId "revenue-metric")
              with
              | Ok state ->
                  Expect.equal state.CurrentState (Some CurrentState.Loading) "Loading propagated"
                  Expect.equal state.StateDetail (Some { Detail = "query pending" }) "Detail propagated"
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          test "getNodeState defaults to CurrentState.Normal when no probe is wired" {
              match
                  Tools.getNodeState (freshContext ()) [ IncludeKey.CurrentState ] dashboard (NodeId "revenue-metric")
              with
              | Ok state ->
                  Expect.equal state.CurrentState (Some CurrentState.Normal) "Default Normal"
                  Expect.isNone state.StateDetail "No detail under default probe"
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          // ─── getNodeState: determinism (same inputs → same outputs) ────────

          test "getNodeState produces byte-identical JSON across two invocations" {
              let sources = withQuery "totalRevenue" (nn {| amount = 100.0; trendPct = 0.1 |})

              let ctx = contextWithSources sources

              let stateA =
                  Tools.getNodeState ctx [] dashboard (NodeId "revenue-metric")
                  |> Result.toOption
                  |> Option.get

              let stateB =
                  Tools.getNodeState ctx [] dashboard (NodeId "revenue-metric")
                  |> Result.toOption
                  |> Option.get

              let jsonA = ResponseRender.renderNodeState stateA
              let jsonB = ResponseRender.renderNodeState stateB

              Expect.equal jsonA jsonB "Byte-identical JSON for identical inputs (determinism contract)"
          }

          // ─── getBindingValue ───────────────────────────────────────────────

          test "getBindingValue returns the resolved float for Metric.Source" {
              let sources = withQuery "totalRevenue" (nn {| amount = 42.0; trendPct = 0.0 |})

              let ctx = contextWithSources sources

              match Tools.getBindingValue ctx dashboard (NodeId "revenue-metric") "Value" with
              | Ok(ResolvedBindingResult.Resolved rb) ->
                  Expect.equal rb.Expression "$queries.totalRevenue" "v1 short-form expression"
                  Expect.equal (rb.ResolvedValue |> Option.map unbox<float>) (Some 42.0) "Resolved float payload"

                  match rb.Source with
                  | BindingSource.Query "totalRevenue" -> ()
                  | other -> failtestf "Expected Query 'totalRevenue', got %A" other
              | other -> failtestf "Expected Resolved, got %A" other
          }

          test "getBindingValue returns SlotNotFound with available_fields hint" {
              match Tools.getBindingValue (freshContext ()) dashboard (NodeId "revenue-metric") "Columns" with
              | Ok _ -> failtest "Expected Error"
              | Error err ->
                  Expect.equal err.Code IntrospectErrorCode.SlotNotFound "SlotNotFound code"
                  Expect.equal err.Hint.NodeKind (Some "Metric") "Node kind populated"
                  Expect.contains err.Hint.AvailableFields "Value" "Source slot enumerated"
                  Expect.contains err.Hint.AvailableFields "Trend" "Trend slot enumerated"
          }

          test "getBindingValue surfaces SourceUnregistered when the query has no entry" {
              match Tools.getBindingValue (freshContext ()) dashboard (NodeId "revenue-metric") "Value" with
              | Ok(ResolvedBindingResult.Failed err) ->
                  Expect.equal err.Code BindingErrorCode.SourceUnregistered "Unregistered code"
              | other -> failtestf "Expected Failed SourceUnregistered, got %A" other
          }

          // ─── getRenderedDom ────────────────────────────────────────────────

          test "getRenderedDom produces a structurally-complete tree under the NoGeometry probe" {
              match Tools.getRenderedDom (freshContext ()) dashboard (NodeId "channel-analysis") with
              | Ok tree ->
                  Expect.equal (idOf tree.Id) "channel-analysis" "Root id"
                  Expect.equal tree.Kind "Box" "Root kind"
                  Expect.equal tree.Geometry None "No geometry under NoGeometry probe"
                  Expect.equal tree.Children.Length 2 "Two children"
                  Expect.equal (idOf tree.Children[0].Id) "revenue-metric" "Metric first"
                  Expect.equal (idOf tree.Children[1].Id) "channel-grid" "Grid second"
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          test "getRenderedDom surfaces per-node geometry from the probe" {
              let table =
                  Map.ofList
                      [ "channel-analysis",
                        { X = 0.0
                          Y = 0.0
                          Width = 800.0
                          Height = 600.0
                          Overflowing = false }
                        "revenue-metric",
                        { X = 10.0
                          Y = 10.0
                          Width = 200.0
                          Height = 80.0
                          Overflowing = false } ]

              let ctx =
                  { (freshContext ()) with
                      Geometry = PerNodeGeometry table }

              match Tools.getRenderedDom ctx dashboard (NodeId "channel-analysis") with
              | Ok tree ->
                  Expect.equal (tree.Geometry |> Option.map _.Width) (Some 800.0) "Root width"

                  let metric = tree.Children |> List.find (fun c -> idOf c.Id = "revenue-metric")
                  Expect.equal (metric.Geometry |> Option.map _.Width) (Some 200.0) "Metric width"

                  let grid = tree.Children |> List.find (fun c -> idOf c.Id = "channel-grid")
                  Expect.equal grid.Geometry None "Grid had no entry → None"
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          // ─── getRuntimeErrors ──────────────────────────────────────────────

          test "getRuntimeErrors drains the in-memory sink with no sinceTurn filter" {
              let ctx = freshContext ()

              Tools.recordError ctx "BindingResolution" "trend accessor threw" (Some(NodeId "revenue-metric")) (Some 1)

              Tools.recordError ctx "UnwiredAction" "Button.OnClick reached the diagnostic runtime" None (Some 2)

              let drained = Tools.getRuntimeErrors ctx None

              Expect.equal drained.Length 2 "Both entries drained"
              Expect.equal drained[0].Code "BindingResolution" "First entry"
              Expect.equal drained[1].Code "UnwiredAction" "Second entry"
          }

          test "getRuntimeErrors filters by sinceTurn (strict greater-than)" {
              let ctx = freshContext ()

              Tools.recordError ctx "A" "turn 1" None (Some 1)
              Tools.recordError ctx "B" "turn 2" None (Some 2)
              Tools.recordError ctx "C" "turn 3" None (Some 3)
              Tools.recordError ctx "Untagged" "no turn" None None

              let drained = Tools.getRuntimeErrors ctx (Some 1)

              Expect.equal drained.Length 2 "Turns > 1 only (B + C); untagged filtered out"
              Expect.equal drained[0].Code "B" "First survivor"
              Expect.equal drained[1].Code "C" "Second survivor"
          }

          test "Fixed clock makes ErrorEntry.RecordedAt deterministic across runs" {
              let ctx = freshContext ()

              Tools.recordError ctx "Det" "deterministic timestamp" None (Some 1)

              let drained = Tools.getRuntimeErrors ctx None
              Expect.equal drained[0].RecordedAt Seams.defaultEpoch "Fixed epoch"
          }

          // ─── ResponseRender shape ──────────────────────────────────────────

          test "renderError emits the §4d-shaped envelope (tool + error + hint)" {
              let err =
                  Tools.getNodeState (freshContext ()) [] dashboard (NodeId "ghost")
                  |> function
                      | Error e -> e
                      | Ok _ -> failtest "Expected Error"

              let json =
                  ResponseRender.renderError "fuaran.getNodeState" [ "nodeId", "ghost" ] err

              Expect.stringContains json "\"tool\"" "tool block present"
              Expect.stringContains json "\"fuaran.getNodeState\"" "tool name echoed"
              Expect.stringContains json "\"nodeId\":\"ghost\"" "args echoed"
              Expect.stringContains json "\"code\":\"NodeNotFound\"" "code emitted"
              Expect.stringContains json "\"hint\"" "hint block present"
          }

          test "renderNodeState emits camelCase per §4i example" {
              // The §4i camelCase assertion for `resolvedValue` only fires
              // when a binding actually resolves — register the query
              // result so Source produces a `Resolved` rather than a
              // `BindingError` (which uses the `error` shape, not
              // `resolvedValue`).
              let ctx =
                  { contextWithSources (withQuery "totalRevenue" (nn {| amount = 1.0; trendPct = 0.0 |})) with
                      CurrentState = FixedCurrentState(CurrentState.Normal, None) }

              let state =
                  Tools.getNodeState ctx [] dashboard (NodeId "revenue-metric")
                  |> function
                      | Ok s -> s
                      | Error e -> failtestf "Expected Ok, got %A" e

              let json = ResponseRender.renderNodeState state

              Expect.stringContains json "\"currentState\":" "camelCase currentState"
              Expect.stringContains json "\"resolvedValue\":" "camelCase resolvedValue"
              Expect.stringContains json "\"kind\":\"Metric\"" "kind echoed"
          } ]

// ─── Binding-slot cross-table consistency (Phase 118) ───────────────────────
//
// The tier maintains three hand-written per-NodeKind Binding-slot tables that
// must agree about which slots exist on each kind:
//
//   1. `Fuaran.UI.Ops.Introspect.availableBindingSlots` — the declared registry
//      (drives the §4d ReplaceBinding error hint).
//   2. `Fuaran.UI.Ops.Apply.dispatchReplaceBinding` — the ReplaceBinding apply
//      dispatch (reached here via the public `Apply.apply`).
//   3. `Fuaran.UI.AiTools.Tools.extractSlot` — the introspection resolver
//      (reached here via the public `getBindingValue`).
//
// Treating (1) as canonical, the property is: every slot it lists must be
// honoured by (2) and (3) — i.e. neither returns SlotNotFound — and a slot it
// does NOT list must be SlotNotFound on both. A future slot added to one table
// but not the others fails this test. The fixture list below is the registry
// of binding-bearing kinds; a new kind with Binding-typed slots must be added
// here too.

/// Construct a bare Node directly (no smart-ctor) so kinds without a public
/// constructor — Sparkline — can still be exercised. Mirrors `Fuaran.buildNode`.
let private bareNode (id: string) (kind: NodeKind<Msg>) : Node<Msg> =
    { Id = id
      Kind = kind
      State = None
      Style = None
      Accessibility = Option.None
      Motion = Defaults.Motion.none
      ExtraAttributes = Option.None }

/// One representative node per binding-bearing NodeKind. Kinds with no
/// Binding-typed slot (every other Layout / Display / Input / Visualisation /
/// Custom) are deliberately omitted — their `availableBindingSlots` is empty,
/// so the per-slot consistency loop has nothing to assert.
let private bindingBearingFixtures: Node<Msg> list =
    [ Fuaran.metric "n-metric" Defaults.metric
      bareNode "n-sparkline" (NodeKind.Sparkline(Defaults.sparkline))
      Fuaran.progress "n-progress" Defaults.progress
      Fuaran.labelValueRow "n-lvr" Defaults.labelValueRow
      Fuaran.stepper "n-stepper" Defaults.stepper<Msg>
      Fuaran.tabs "n-tabs" Defaults.tabs<Msg>
      Fuaran.disclosure "n-disclosure" Defaults.disclosure<Msg>
      Fuaran.button "n-button" Defaults.button<Msg>
      Fuaran.select "n-select" Defaults.select<Msg>
      // Phase 130: Form + FileUpload gained a node-level `Disabled` binding
      // slot (the interactive-state class-fix), so they now join the
      // binding-bearing fixture set.
      Fuaran.form "n-form" Defaults.form<Msg>
      Fuaran.fileUpload "n-fileupload" Defaults.fileUpload<Msg>
      Fuaran.chart "n-chart" Defaults.chart<Msg>
      Fuaran.map "n-map" Defaults.map<Msg>
      Fuaran.grid "n-grid" (fun (r: ChannelRow) -> Map.ofList [ "rowId", nn r.RowId ]) Defaults.grid<ChannelRow, Msg> ]

// `Binding.Filter` is a type-agnostic ReplaceBinding probe: `castBinding`
// passes it through for any 'T without an unbox, so a recognised slot yields
// Ok and an unrecognised one yields SlotNotFound — isolating "does the table
// know this slot" from any inner-type-cast concern.
let private probeBinding: Binding<obj> = Binding.Filter("probe", None)

[<Tests>]
let bindingSlotConsistencyTests =
    testList
        "Fuaran binding-slot cross-table consistency (Phase 118)"
        [ test "every availableBindingSlots slot is ReplaceBinding-able AND getBindingValue-resolvable" {
              let ctx = freshContext ()

              for fixture in bindingBearingFixtures do
                  let kindLabel = Fuaran.UI.Ops.Introspect.kindName fixture.Kind
                  let slots = Fuaran.UI.Ops.Introspect.availableBindingSlots fixture.Kind

                  Expect.isNonEmpty
                      slots
                      (sprintf "%s is in the binding-bearing fixture set but declares no slots" kindLabel)

                  for slot in slots do
                      match
                          Fuaran.UI.Ops.Apply.apply
                              (Fuaran.UI.Ops.Types.TreeOp.ReplaceBinding(NodeId fixture.Id, slot, probeBinding))
                              fixture
                      with
                      | Ok _ -> ()
                      | Error e ->
                          Expect.notEqual
                              e.Code
                              Fuaran.UI.Ops.Types.ApplyErrorCode.SlotNotFound
                              (sprintf
                                  "%s.%s is in availableBindingSlots but dispatchReplaceBinding returns SlotNotFound"
                                  kindLabel
                                  slot)

                      match Tools.getBindingValue ctx fixture (NodeId fixture.Id) slot with
                      | Ok _ -> ()
                      | Error e ->
                          Expect.notEqual
                              e.Code
                              IntrospectErrorCode.SlotNotFound
                              (sprintf
                                  "%s.%s is in availableBindingSlots but extractSlot returns SlotNotFound"
                                  kindLabel
                                  slot)
          }

          test "a slot absent from availableBindingSlots is SlotNotFound on both surfaces" {
              let ctx = freshContext ()
              let bogus = "ZzNotARealSlot"

              for fixture in bindingBearingFixtures do
                  let kindLabel = Fuaran.UI.Ops.Introspect.kindName fixture.Kind

                  match
                      Fuaran.UI.Ops.Apply.apply
                          (Fuaran.UI.Ops.Types.TreeOp.ReplaceBinding(NodeId fixture.Id, bogus, probeBinding))
                          fixture
                  with
                  | Ok _ -> failtestf "%s accepted a ReplaceBinding against the bogus slot %s" kindLabel bogus
                  | Error e ->
                      Expect.equal
                          e.Code
                          Fuaran.UI.Ops.Types.ApplyErrorCode.SlotNotFound
                          (sprintf "%s bogus ReplaceBinding -> SlotNotFound" kindLabel)

                  match Tools.getBindingValue ctx fixture (NodeId fixture.Id) bogus with
                  | Ok _ -> failtestf "%s resolved getBindingValue against the bogus slot %s" kindLabel bogus
                  | Error e ->
                      Expect.equal
                          e.Code
                          IntrospectErrorCode.SlotNotFound
                          (sprintf "%s bogus getBindingValue -> SlotNotFound" kindLabel)
          }

          test
              "the Phase 118 slots (Tabs.ActiveIndex/ActiveTag, Disclosure.Open, Select.Source/Value) resolve end-to-end" {
              // Explicit per-slot pins so a regression that drops exactly one of
              // the phase's additions is named, not just caught by the loop.
              let ctx = freshContext ()

              let expectReplaceable (node: Node<Msg>) (slot: string) =
                  match
                      Fuaran.UI.Ops.Apply.apply
                          (Fuaran.UI.Ops.Types.TreeOp.ReplaceBinding(NodeId node.Id, slot, probeBinding))
                          node
                  with
                  | Ok _ -> ()
                  | Error e -> failtestf "ReplaceBinding %A.%s failed: %A" node.Id slot e

              let expectResolvable (node: Node<Msg>) (slot: string) =
                  match Tools.getBindingValue ctx node (NodeId node.Id) slot with
                  | Ok _ -> ()
                  | Error e -> failtestf "getBindingValue %A.%s failed: %A" node.Id slot e

              let tabs = Fuaran.tabs "n-tabs" Defaults.tabs<Msg>
              let disclosure = Fuaran.disclosure "n-disclosure" Defaults.disclosure<Msg>
              let select = Fuaran.select "n-select" Defaults.select<Msg>

              for slot in [ "ActiveIndex"; "ActiveTag" ] do
                  expectReplaceable tabs slot
                  expectResolvable tabs slot

              expectReplaceable disclosure "Open"
              expectResolvable disclosure "Open"

              for slot in [ "Source"; "Value" ] do
                  expectReplaceable select slot
                  expectResolvable select slot
          }

          test "Button.Disabled (Phase 129) resolves end-to-end — optional-slot synthetic-None when absent" {
              // Default button has `Disabled = None`; the optional slot must still
              // be ReplaceBinding-able + getBindingValue-resolvable (synthetic-None
              // posture, mirroring Tabs.ActiveTag / Metric.Trend).
              let ctx = freshContext ()
              let button = Fuaran.button "n-button" Defaults.button<Msg>

              match
                  Fuaran.UI.Ops.Apply.apply
                      (Fuaran.UI.Ops.Types.TreeOp.ReplaceBinding(
                          NodeId button.Id,
                          "Disabled",
                          Binding.Filter("probe", None)
                      ))
                      button
              with
              | Ok _ -> ()
              | Error e -> failtestf "ReplaceBinding Button.Disabled failed: %A" e

              match Tools.getBindingValue ctx button (NodeId button.Id) "Disabled" with
              | Ok _ -> ()
              | Error e -> failtestf "getBindingValue Button.Disabled failed: %A" e
          }

          // ── Phase 130: interactive-state-coverage lock ───────────────────────
          //
          // `Introspect.interactiveStateSlots` is the canonical registry of the
          // renderer-honoured interactive runtime states (disabled / busy /
          // readonly / …) each interactive NodeKind exposes. This test pins the
          // class contract: every slot it lists MUST be present in
          // `availableBindingSlots` AND honoured by both the ReplaceBinding apply
          // dispatch and the getBindingValue resolver. A future interactive kind
          // that gains a renderer-honoured state (and so a row in
          // `interactiveStateSlots`) without the matching bindable slot in all
          // three tables fails here — no "renderer-only" interactive state can
          // re-open the gap Phase 129/130 closed.
          test "Phase 130 interactive-state coverage — every interactiveStateSlots entry is bindable in all tables" {
              let ctx = freshContext ()

              // One representative node per interactive NodeKind that exposes a
              // node-level interactive-state slot.
              let interactiveFixtures: Node<Msg> list =
                  [ Fuaran.button "i-button" Defaults.button<Msg>
                    Fuaran.select "i-select" Defaults.select<Msg>
                    Fuaran.form "i-form" Defaults.form<Msg>
                    Fuaran.fileUpload "i-fileupload" Defaults.fileUpload<Msg> ]

              for fixture in interactiveFixtures do
                  let kindLabel = Fuaran.UI.Ops.Introspect.kindName fixture.Kind
                  let stateSlots = Fuaran.UI.Ops.Introspect.interactiveStateSlots fixture.Kind

                  Expect.isNonEmpty
                      stateSlots
                      (sprintf "%s is in the interactive fixture set but interactiveStateSlots is empty" kindLabel)

                  let declared = Fuaran.UI.Ops.Introspect.availableBindingSlots fixture.Kind

                  for slot in stateSlots do
                      // (1) the renderer-honoured state is a declared binding slot.
                      Expect.contains
                          declared
                          slot
                          (sprintf
                              "%s.%s is renderer-honoured (interactiveStateSlots) but absent from availableBindingSlots"
                              kindLabel
                              slot)

                      // (2) ReplaceBinding honours it.
                      match
                          Fuaran.UI.Ops.Apply.apply
                              (Fuaran.UI.Ops.Types.TreeOp.ReplaceBinding(NodeId fixture.Id, slot, probeBinding))
                              fixture
                      with
                      | Ok _ -> ()
                      | Error e ->
                          Expect.notEqual
                              e.Code
                              Fuaran.UI.Ops.Types.ApplyErrorCode.SlotNotFound
                              (sprintf
                                  "%s.%s interactiveStateSlots but dispatchReplaceBinding -> SlotNotFound"
                                  kindLabel
                                  slot)

                      // (3) getBindingValue resolves it (synthetic-None when absent).
                      match Tools.getBindingValue ctx fixture (NodeId fixture.Id) slot with
                      | Ok _ -> ()
                      | Error e ->
                          Expect.notEqual
                              e.Code
                              IntrospectErrorCode.SlotNotFound
                              (sprintf "%s.%s interactiveStateSlots but extractSlot -> SlotNotFound" kindLabel slot)
          }

          test "Phase 130 Select/Form/FileUpload.Disabled resolve end-to-end — optional-slot synthetic-None when absent" {
              // Defaults carry `Disabled = None`; the optional slot must still be
              // ReplaceBinding-able + getBindingValue-resolvable on each kind
              // (synthetic-None posture, mirroring Button.Disabled / Tabs.ActiveTag).
              let ctx = freshContext ()

              let expectReplaceable (node: Node<Msg>) (slot: string) =
                  match
                      Fuaran.UI.Ops.Apply.apply
                          (Fuaran.UI.Ops.Types.TreeOp.ReplaceBinding(NodeId node.Id, slot, probeBinding))
                          node
                  with
                  | Ok _ -> ()
                  | Error e -> failtestf "ReplaceBinding %A.%s failed: %A" node.Id slot e

              let expectResolvable (node: Node<Msg>) (slot: string) =
                  match Tools.getBindingValue ctx node (NodeId node.Id) slot with
                  | Ok _ -> ()
                  | Error e -> failtestf "getBindingValue %A.%s failed: %A" node.Id slot e

              let nodes =
                  [ Fuaran.select "p-select" Defaults.select<Msg>
                    Fuaran.form "p-form" Defaults.form<Msg>
                    Fuaran.fileUpload "p-fileupload" Defaults.fileUpload<Msg> ]

              for node in nodes do
                  expectReplaceable node "Disabled"
                  expectResolvable node "Disabled"
          } ]
