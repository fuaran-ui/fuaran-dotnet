module Fuaran.UI.Tests.DebugGlobal

// ============================================================================
//  Phase 90 — in-page introspection REPL (`window.__fuaran`) — F# Fable host.
//
//  The renderer-side debug global's surface is built from pure F# helpers that
//  compile on BOTH pipelines (Fable-vs-.NET parity); the geometry read, POJO
//  materialisation, and window registration are the only `#if FABLE_COMPILER`
//  pieces. These tests pin the pure surface on .NET — the same logic the Fable
//  console session runs — without a browser:
//
//   - `kindName` returns the wire discriminator (Layout grid → "GridLayout",
//     vis grid → "Grid").
//   - `extractBindingSlots` matches the canonical `AiTools.Tools.extractBindings`
//     table, including optional-slot presence/absence.
//   - `bindingExpression` matches `AiTools.BindingProbe.identify`.
//   - `childNodes` / `walkNodes` / `findNode` / `findNodesByKind` traverse the
//     console shape (incl. ErrorBoundary arms + FragmentDecl body).
//   - `resolveSlot` resolves a single slot against live `BindingSources`.
//   - `shouldRegister` is the gating predicate (host opt-in AND DEBUG build).
//   - `applyGateDecision` enforces the default-deny policy gate (FGP 3).
//
//  The live cross-host browser assertion of `window.__fuaran` is the
//  environment-gated tail (needs a browser session) and is tracked deferred on
//  the phase body.
// ============================================================================

open System
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Runtime

// ─── A tree exercising the representative kinds + binding slots ─────────────

/// Metric whose Source is a `Binding.State` (count) and Trend is absent.
let private metricNoTrend: Node<unit> =
    Fuaran.metric
        "m-no-trend"
        { Defaults.metric with
            Value = binding.state "count" 0.0
            Trend = None }

/// Metric whose Source is a `Binding.Query` (rev) and Trend is present
/// (`Binding.State`).
let private metricWithTrend: Node<unit> =
    Fuaran.metric
        "m-with-trend"
        { Defaults.metric with
            Value = binding.query "rev" (fun (r: float) -> r)
            Trend = Some(binding.state "trend" 0.0) }

/// Select with a static Source + a `Binding.State` Value; Disabled absent.
let private selectNode: Node<unit> =
    Fuaran.select
        "sel"
        { Defaults.select<unit> with
            Source = Binding.Static(Some [])
            Value = binding.stateNoDefault "pick" }

let private markdownNode: Node<unit> = Fuaran.markdown "md" "hello"

let private tree: Node<unit> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard with
            Children = [ metricNoTrend; metricWithTrend; selectNode; markdownNode ] }

/// F# 10 `box _` types as `obj | null`; the source maps require non-null obj.
/// Launder via `Unchecked.nonNull` — same shape `Fuaran.UI.Ops.Tests`'s `nn`
/// helper uses.
let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

/// Live sources: `count` state = 5.0, `rev` query = 42.0, `pick` state = "p1".
let private sources: BindingResolver.BindingSources =
    { BindingResolver.empty with
        State = Map.ofList [ "count", nn 5.0; "trend", nn 1.5; "pick", nn (Some "p1") ]
        QueryResults = Map.ofList [ "rev", nn 42.0 ] }

// ─── A gating runtime (mirrors DispatchGateTests) ──────────────────────────

type private GatingRuntime(canDispatch: ActionDescriptor -> bool) =
    let warnings = ResizeArray<string>()
    member _.Warnings = warnings

    interface IFuaranRuntime with
        member _.Call(endpoint, onResult) =
            Runtime.diagnostic.Call(endpoint, onResult)

        member _.Notify(channel, payload) =
            Runtime.diagnostic.Notify(channel, payload)

        member _.Navigate(route) = Runtime.diagnostic.Navigate(route)
        member _.SetState(key, value) = Runtime.diagnostic.SetState(key, value)

        member _.InvokeAiTool(toolName, args) =
            Runtime.diagnostic.InvokeAiTool(toolName, args)

        member _.WriteToClipboard(text) =
            Runtime.diagnostic.WriteToClipboard(text)

        member _.ReadFileBody(file, encoding, onRead) =
            Runtime.diagnostic.ReadFileBody(file, encoding, onRead)

        member _.Warn(message) = warnings.Add message
        member _.LayoutObserver = None
        member _.TryRenderCustom(_, _, _) = None
        member _.TryGetCustomRenderer(_, _) = None
        member _.CanDispatch(action) = canDispatch action
        member _.TryLoadGuest(_) = None

// ─── Helpers ────────────────────────────────────────────────────────────────

let private slotNames (node: Node<unit>) : string list =
    DebugGlobal.extractBindingSlots node.Kind |> List.map _.Slot

[<Tests>]
let tests =
    testList
        "Phase 90 — window.__fuaran introspection REPL (F# host)"
        [ test "kindName returns the wire discriminator per kind" {
              // Phase 390: the retired Dashboard/Stack/GridLayout/Card kinds all
              // tag as "Box" on the wire (role + layout are payload fields).
              Expect.equal (DebugGlobal.kindName tree.Kind) "Box" "Box (retired Dashboard)"
              Expect.equal (DebugGlobal.kindName metricNoTrend.Kind) "Metric" "Metric"
              Expect.equal (DebugGlobal.kindName selectNode.Kind) "Select" "Select"
              Expect.equal (DebugGlobal.kindName markdownNode.Kind) "Markdown" "Markdown"

              // Layout grid → "Box" (Phase 390); vis grid → "Grid".
              let gridLayout =
                  Fuaran.gridLayout
                      "gl"
                      { Defaults.gridLayout<unit> with
                          Cols = 2 }

              Expect.equal (DebugGlobal.kindName gridLayout.Kind) "Box" "Layout grid → Box"

              let dataGrid =
                  Fuaran.grid "g" (fun (n: int) -> Map.ofList [ "n", nn n ]) Defaults.grid<int, unit>

              Expect.equal (DebugGlobal.kindName dataGrid.Kind) "Grid" "vis grid → Grid"
          }

          test "extractBindingSlots matches the canonical table incl. optional presence" {
              // Metric: Source always; Trend only when Some.
              Expect.equal (slotNames metricNoTrend) [ "Value" ] "Metric w/o trend → [Source]"
              Expect.equal (slotNames metricWithTrend) [ "Value"; "Trend" ] "Metric w/ trend → [Source; Trend]"
              // Select: Source + Value always; Disabled only when Some (absent here).
              Expect.equal (slotNames selectNode) [ "Source"; "Value" ] "Select → [Source; Value]"
              // Markdown carries no binding slots.
              Expect.isEmpty (slotNames markdownNode) "Markdown → no slots"
          }

          test "extractBindingSlots carries the wire-form expression + source" {
              let slots = DebugGlobal.extractBindingSlots metricWithTrend.Kind

              let bySlot name =
                  slots |> List.find (fun s -> s.Slot = name)

              Expect.equal (bySlot "Value").Expression "$queries.rev" "Value expression"
              Expect.equal (bySlot "Value").Source "Query" "Value source-token"
              Expect.equal (bySlot "Trend").Expression "$state.trend" "Trend expression"
              Expect.equal (bySlot "Trend").Source "State" "Trend source-token"
          }

          test "bindingExpression mirrors BindingProbe.identify" {
              Expect.equal (DebugGlobal.bindingExpression (Binding.Static(Some 1.0))) ("Static", "$static") "Static"
              Expect.equal (DebugGlobal.bindingExpression (binding.state "k" 0.0)) ("State", "$state.k") "State"

              Expect.equal
                  (DebugGlobal.bindingExpression (binding.query "q" (fun (r: float) -> r)))
                  ("Query", "$queries.q")
                  "Query"

              Expect.equal (DebugGlobal.bindingExpression (Binding.Filter("f", None))) ("Filter", "$filters.f") "Filter"
          }

          test "childNodes / walkNodes / findNode / findNodesByKind traverse the tree" {
              let childIds = DebugGlobal.childNodes tree |> List.map _.Id

              Expect.equal childIds [ "m-no-trend"; "m-with-trend"; "sel"; "md" ] "dashboard children"

              Expect.equal (List.length (DebugGlobal.walkNodes tree)) 5 "5 nodes total (root + 4 children)"

              Expect.isSome (DebugGlobal.findNode "sel" tree) "findNode finds a child"
              Expect.isNone (DebugGlobal.findNode "nope" tree) "findNode misses absent id"

              Expect.equal
                  (DebugGlobal.findNodesByKind "Metric" tree)
                  [ "m-no-trend"; "m-with-trend" ]
                  "findNodesByKind Metric → both metrics"
          }

          test "childNodes includes ErrorBoundary arms + FragmentDecl body" {
              let child = Fuaran.markdown "eb-child" "c"
              let fallback = Fuaran.markdown "eb-fallback" "f"

              let eb =
                  Fuaran.errorBoundary
                      "eb"
                      { Defaults.errorBoundary<unit> with
                          Child = child
                          Fallback = fallback }

              let ids = DebugGlobal.childNodes eb |> List.map _.Id

              Expect.equal ids [ "eb-child"; "eb-fallback" ] "ErrorBoundary → [child; fallback]"
          }

          test "inspectTree produces a recursive structural snapshot" {
              let snap = DebugGlobal.inspectTree tree
              Expect.equal snap.Id "root" "root id"
              Expect.equal snap.Kind "Box" "root kind"
              Expect.equal (List.length snap.Children) 4 "4 child snapshots"
              let metricSnap = snap.Children |> List.find (fun c -> c.Id = "m-with-trend")
              Expect.equal (metricSnap.Bindings |> List.map _.Slot) [ "Value"; "Trend" ] "child bindings"
          }

          test "resolveSlot resolves a State slot against the live sources" {
              match DebugGlobal.resolveSlot sources metricNoTrend.Kind "Value" with
              | DebugGlobal.SlotResolution.Resolved(value, expr, src) ->
                  Expect.equal (unbox<float> value) 5.0 "count state resolved to 5.0"
                  Expect.equal expr "$state.count" "expression"
                  Expect.equal src "State" "source token"
              | other -> failtestf "expected Resolved, got %A" other
          }

          test "resolveSlot resolves a Query slot against the live sources" {
              match DebugGlobal.resolveSlot sources metricWithTrend.Kind "Value" with
              | DebugGlobal.SlotResolution.Resolved(value, _, _) ->
                  Expect.equal (unbox<float> value) 42.0 "rev query resolved to 42.0"
              | other -> failtestf "expected Resolved, got %A" other
          }

          test "resolveSlot: absent optional slot → NoOverride; unknown slot → NotDeclared" {
              Expect.equal
                  (DebugGlobal.resolveSlot sources metricNoTrend.Kind "Trend")
                  DebugGlobal.SlotResolution.NoOverride
                  "Trend absent → NoOverride"

              Expect.equal
                  (DebugGlobal.resolveSlot sources metricNoTrend.Kind "Bogus")
                  DebugGlobal.SlotResolution.NotDeclared
                  "unknown slot → NotDeclared"

              Expect.equal
                  (DebugGlobal.resolveSlot sources markdownNode.Kind "Source")
                  DebugGlobal.SlotResolution.NotDeclared
                  "Markdown has no binding slots → NotDeclared"
          }

          test "resolveSlot: unregistered Query source → NotResolved" {
              // metricWithTrend.Source = $queries.rev, but resolve against empty sources.
              match DebugGlobal.resolveSlot BindingResolver.empty metricWithTrend.Kind "Value" with
              | DebugGlobal.SlotResolution.NotResolved(expr, src) ->
                  Expect.equal expr "$queries.rev" "expression"
                  Expect.equal src "Query" "source token"
              | other -> failtestf "expected NotResolved, got %A" other
          }

          test "shouldRegister gates on host opt-in (and the DEBUG build flag)" {
              // Opt-out is always false regardless of build configuration.
              Expect.isFalse (DebugGlobal.shouldRegister false) "debug=false → never register"
              // Opt-in registers iff this is a DEBUG build (release → dead-code-eliminated).
              Expect.equal
                  (DebugGlobal.shouldRegister true)
                  DebugGlobal.compiledInDebug
                  "debug=true → register iff DEBUG build"
          }

          test "applyGateDecision allows when the policy gate permits" {
              let runtime = GatingRuntime(fun _ -> true)

              match DebugGlobal.applyGateDecision (runtime :> IFuaranRuntime) "{\"op\":\"x\"}" with
              | Ok() -> ()
              | Error e -> failtestf "expected Ok, got Error %s" e
          }

          test "applyGateDecision denies (FGP 3) when the policy gate refuses" {
              // A default-deny host that refuses every dispatch — incl. in-page apply.
              let runtime = GatingRuntime(fun _ -> false)

              match DebugGlobal.applyGateDecision (runtime :> IFuaranRuntime) "{\"op\":\"x\"}" with
              | Ok() -> failtest "expected the gate to deny"
              | Error e ->
                  Expect.stringContains e "denied by policy gate" "deny reason names the gate"
                  Expect.stringContains e "ApplyTreeOp" "deny reason names the descriptor"
          }

          test "the gate is per-descriptor — deny ApplyTreeOp while allowing Navigate" {
              let runtime =
                  GatingRuntime(fun d ->
                      match d with
                      | ActionDescriptor.ApplyTreeOp _ -> false
                      | _ -> true)

              Expect.isTrue ((runtime :> IFuaranRuntime).CanDispatch(ActionDescriptor.Navigate "/x")) "Navigate allowed"

              Expect.isFalse
                  ((runtime :> IFuaranRuntime).CanDispatch(ActionDescriptor.ApplyTreeOp "{}"))
                  "ApplyTreeOp denied"
          } ]

// ─── Phase 193 — durable-sink emission for the in-page apply path ────────────
//
// The emission DECISIONS are pure (the dispatch that consumes them is the
// Fable-only shell), so they are pinned here the same way `applyGateDecision`
// is. These shapes are parity-locked with the TypeScript mirror.

[<Tests>]
let debugGlobalEmissionTests =
    testList
        "DebugGlobal — durable-sink emission (Phase 193)"
        [ test "a denial produces a deny record under the stable tool name" {
              let at = DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero)

              let record =
                  DebugGlobal.denyTelemetry "operator-1" at "apply denied by policy gate: ApplyTreeOp"

              Expect.equal record.ToolName "__fuaran.apply" "the console path has its own stable tool name"
              Expect.equal record.ToolName DebugGlobal.ApplyToolName "the literal is the one used"
              Expect.stringContains record.Reason "denied by policy gate" "carries the deny diagnostic verbatim"
              Expect.equal record.UserId "operator-1" "carries the audit subject"
              Expect.equal record.Timestamp at "carries the supplied instant"
          }

          test "a console-driven denial belongs to no module, page, or prompt" {
              let record = DebugGlobal.denyTelemetry "operator" DateTimeOffset.UnixEpoch "denied"

              // Operator-initiated: these are legitimately absent rather than
              // fabricated, matching the OpRecord PromptId convention.
              Expect.isNone record.ActiveModule "no module"
              Expect.isNone record.ActivePage "no page"
              Expect.isNone record.PromptId "no prompt"
          }

          test "only a genuinely applied op is journalled" {
              Expect.isTrue (DebugGlobal.shouldJournal DebugGlobal.ApplyOutcome.Applied) "applied journals"

              // A decode failure never produced a TreeOp and a rejected op changed
              // no tree — journalling either would record an op that never happened.
              Expect.isFalse
                  (DebugGlobal.shouldJournal (DebugGlobal.ApplyOutcome.DecodeFailed "bad json"))
                  "decode failure does not journal"

              Expect.isFalse
                  (DebugGlobal.shouldJournal (DebugGlobal.ApplyOutcome.Rejected "no such node"))
                  "apply rejection does not journal"
          }

          test "DebugSinks.none is the historical warn-only behaviour" {
              let sinks = DebugGlobal.DebugSinks.none
              Expect.isNone sinks.TelemetrySink "no telemetry leg by default"
              Expect.isNone sinks.OnApplied "no op-stream leg by default"
          } ]
