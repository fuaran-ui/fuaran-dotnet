module Fuaran.UI.Tests.ErrorBoundary

// ============================================================================
//  Render-time error boundaries + fallback nodes.
//
//  Acceptance criteria pinned by these tests:
//
//   1. The smart-constructor + Defaults wire `NodeKind.ErrorBoundary`
//      with the expected fields (Child + Fallback).
//   2. The renderer's `nodeKindName` projection emits a stable
//      discriminator string for every NodeKind — feeds the orchestrator's
//      drift-detector aggregates.
//   3. A structured `RenderFailureTelemetry` event reaches the
//      `IFuaranTelemetrySink.RecordRenderFailure` sink with the right
//      fields (NodeId / NodeKindName / ErrorMessage / CaughtBy /
//      CorrelationId / Timestamp).
//   4. A throwing telemetry sink does not poison the emit path — the
//      `emitRenderFailure` helper swallows sink failures per the
//      `IFuaranTelemetrySink` best-effort contract.
//   5. The `Defaults.Accessibility.none` shape is preserved on the
//      smart-ctor's output (the boundary is structural — no role
//      override).
//
//  Feliz' .NET-side ReactElement is opaque (the same constraint
//  AccessibilityTests / CustomRendererTests document); the per-node
//  guard's actual DOM emission is asserted by the catalog axis under
//  Fable, NOT here. These tests pin the failure-channel shape + the
//  helper-surface contract that the renderer's internal try/with
//  composes from.
// ============================================================================

open System.Collections.Generic
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Telemetry.Abstractions

/// In-memory recording sink — captures every render-failure event so
/// tests can assert against the fields without standing up the full
/// `Fuaran.UI.Telemetry.Default.InMemorySink` (the Tests project doesn't
/// reference `Telemetry.Default`).
type private RecordingSink() =
    let renderFailures = ResizeArray<RenderFailureTelemetry>()

    member _.RenderFailures: IReadOnlyList<RenderFailureTelemetry> = renderFailures :> _
    member _.Clear() = renderFailures.Clear()

    interface IFuaranTelemetrySink with
        member _.RecordOpApply _ = ()
        member _.RecordDeny _ = ()
        member _.RecordRenderFailure tel = renderFailures.Add tel
        member _.RecordProviderCall _ = ()
        member _.RecordCacheStat _ = ()
        member _.RecordValidateOutcome _ = ()

/// Sink whose `RecordRenderFailure` throws — pins the "best-effort"
/// contract: `emitRenderFailure` must not propagate sink failures into
/// the render path.
type private ThrowingRenderFailureSink() =
    interface IFuaranTelemetrySink with
        member _.RecordOpApply _ = ()
        member _.RecordDeny _ = ()

        member _.RecordRenderFailure _ =
            failwith "deliberately throwing render-failure sink"

        member _.RecordProviderCall _ = ()
        member _.RecordCacheStat _ = ()
        member _.RecordValidateOutcome _ = ()

type private Msg = NoOp

[<Tests>]
let tests =
    testList
        "render-time error boundaries + per-node guard"
        [
          // ── Smart-ctor + Defaults shape ───────────────────────────────────
          test "Fuaran.errorBoundary builds NodeKind.ErrorBoundary with the supplied child + fallback" {
              let child: Node<Msg> =
                  Fuaran.metric
                      "child-id"
                      { Defaults.metric with
                          Label = TextSource.Literal "Child Metric" }

              let fallback: Node<Msg> =
                  Fuaran.heading
                      "fallback-id"
                      { Defaults.heading with
                          Text = TextSource.Literal "Fallback heading" }

              let boundary: Node<Msg> =
                  Fuaran.errorBoundary "boundary-id" { Child = child; Fallback = fallback }

              Expect.equal boundary.Id "boundary-id" "smart-ctor wires the supplied NodeId"

              match boundary.Kind with
              | NodeKind.ErrorBoundary spec ->
                  Expect.equal spec.Child.Id "child-id" "child field threaded through"
                  Expect.equal spec.Fallback.Id "fallback-id" "fallback field threaded through"
              | other -> failtestf "expected NodeKind.ErrorBoundary, got %A" other
          }

          test "Defaults.errorBoundary supplies a structurally inert placeholder for both halves" {
              let spec: ErrorBoundarySpec<Msg> = Defaults.errorBoundary<Msg>

              // Both halves default to the same Skeleton placeholder shape —
              // authors override at the smart-ctor call site. The point is
              // that constructing `{ Defaults.errorBoundary with Child = ... }`
              // doesn't leave Fallback in an unauthored state.
              match spec.Child.Kind, spec.Fallback.Kind with
              | NodeKind.Skeleton(_), NodeKind.Skeleton(_) -> ()
              | other -> failtestf "expected both halves to default to Skeleton, got %A" other
          }

          test "Fuaran.errorBoundary inherits the decorative Accessibility default (None)" {
              let boundary: Node<Msg> =
                  Fuaran.errorBoundary
                      "boundary-id"
                      { Child = Fuaran.markdown "child" "body"
                        Fallback = Fuaran.markdown "fallback" "fallback body" }

              // Accessibility carries `Binding<_> option` fields with
              // function-typed payloads — no structural equality. Pattern-
              // match instead, mirroring the AccessibilityTests precedent.
              match boundary.Accessibility with
              | None -> ()
              | Some _ -> failtest "boundary is structural — no aria-* emission by default"
          }

          // ── nodeKindName projection ──────────────────────────────────────
          test "nodeKindName emits stable discriminators for every NodeKind family" {
              let layoutNode = Fuaran.dashboard "x" { Children = [] }
              let displayNode = Fuaran.metric "x" Defaults.metric
              let inputNode = Fuaran.button "x" Defaults.button<Msg>
              let visNode = Fuaran.chart "x" Defaults.chart<Msg>

              let customNode: Node<Msg> = Fuaran.custom "x" "mymod" "MyComp" Map.empty None []

              let boundaryNode: Node<Msg> =
                  Fuaran.errorBoundary
                      "x"
                      { Child = Fuaran.markdown "c" ""
                        Fallback = Fuaran.markdown "f" "" }

              Expect.equal (Render.nodeKindName layoutNode.Kind) "Layout.Dashboard" "Layout dispatch"
              Expect.equal (Render.nodeKindName displayNode.Kind) "Display.Metric" "Display dispatch"
              Expect.equal (Render.nodeKindName inputNode.Kind) "Input.Button" "Input dispatch"
              Expect.equal (Render.nodeKindName visNode.Kind) "Visualisation.Chart" "Visualisation dispatch"

              Expect.equal
                  (Render.nodeKindName customNode.Kind)
                  "Custom.mymod.MyComp"
                  "Custom carries moduleId + componentId so drift aggregates on the specific component"

              Expect.equal (Render.nodeKindName boundaryNode.Kind) "ErrorBoundary" "ErrorBoundary discriminator"
          }

          // ── emitRenderFailure → sink ─────────────────────────────────────
          test "emitRenderFailure delivers a fully-populated RenderFailureTelemetry to the sink" {
              let sink = RecordingSink()

              let kindNode: Node<Msg> = Fuaran.metric "throwing-metric" Defaults.metric

              let corrId =
                  Render.emitRenderFailure
                      (Some(sink :> IFuaranTelemetrySink))
                      "throwing-metric"
                      (Render.nodeKindName kindNode.Kind)
                      "boom — accessor blew up"
                      RenderFailureSource.PerNodeGuard

              Expect.isFalse (System.String.IsNullOrEmpty corrId) "correlation id is populated"
              Expect.equal sink.RenderFailures.Count 1 "sink received exactly one event"

              let tel = sink.RenderFailures[0]
              Expect.equal tel.NodeId "throwing-metric" "NodeId threaded through"
              Expect.equal tel.NodeKindName "Display.Metric" "kind discriminator pinned"
              Expect.equal tel.ErrorMessage "boom — accessor blew up" "error message threaded through verbatim"
              Expect.equal tel.CaughtBy RenderFailureSource.PerNodeGuard "caught-by source pinned"
              Expect.equal tel.CorrelationId corrId "correlation id matches the helper's return"
              Expect.equal tel.PromptId None "no correlation context supplied ⇒ prompt id stays None"
              Expect.equal tel.UserId None "no correlation context supplied ⇒ user id stays None"
          }

          // ── Phase 330: the opaque correlation-context slot ────────────────
          test "emitRenderFailureWithContext stamps the host's opaque ids onto the telemetry" {
              let sink = RecordingSink()

              let context =
                  Map.ofList
                      [ Render.promptIdKey, "interaction-42"
                        Render.userIdKey, "user-7"
                        // An unknown key is carried by the context and simply
                        // ignored by the renderer — the slot is opaque, so a
                        // host may put whatever it correlates on in there.
                        "somethingElse", "ignored" ]

              Render.emitRenderFailureWithContext
                  (Some(sink :> IFuaranTelemetrySink))
                  context
                  "throwing-metric"
                  "Display.Metric"
                  "boom"
                  RenderFailureSource.PerNodeGuard
              |> ignore

              let tel = sink.RenderFailures[0]
              Expect.equal tel.PromptId (Some "interaction-42") "the interaction id is stamped"
              Expect.equal tel.UserId (Some "user-7") "the user id is stamped"
          }

          test "an empty correlation context reproduces the pre-330 behaviour exactly" {
              let sink = RecordingSink()

              let withEmptyMap =
                  Render.emitRenderFailureWithContext
                      (Some(sink :> IFuaranTelemetrySink))
                      Map.empty
                      "n"
                      "Display.Metric"
                      "boom"
                      RenderFailureSource.PerNodeGuard

              let viaOldArity =
                  Render.emitRenderFailure
                      (Some(sink :> IFuaranTelemetrySink))
                      "n"
                      "Display.Metric"
                      "boom"
                      RenderFailureSource.PerNodeGuard

              Expect.equal viaOldArity withEmptyMap "the correlation id is unchanged"
              Expect.equal sink.RenderFailures.Count 2 "both emitted"
              Expect.isNone sink.RenderFailures[0].PromptId "empty map ⇒ None"
              Expect.isNone sink.RenderFailures[1].PromptId "the pre-330 arity ⇒ None"
          }

          test "the correlation id is independent of the interaction id" {
              // Two failures in ONE interaction on DIFFERENT nodes share the
              // interaction id and must still be distinguishable within the
              // frame — which is the whole reason the node-hash correlation id
              // stays alongside it rather than being replaced by it.
              let sink = RecordingSink()
              let context = Map.ofList [ Render.promptIdKey, "one-interaction" ]

              let a =
                  Render.emitRenderFailureWithContext
                      (Some(sink :> IFuaranTelemetrySink))
                      context
                      "node-a"
                      "Display.Metric"
                      "boom"
                      RenderFailureSource.PerNodeGuard

              let b =
                  Render.emitRenderFailureWithContext
                      (Some(sink :> IFuaranTelemetrySink))
                      context
                      "node-b"
                      "Display.Metric"
                      "boom"
                      RenderFailureSource.PerNodeGuard

              Expect.notEqual b a "different nodes ⇒ different intra-frame correlation ids"

              Expect.equal
                  (sink.RenderFailures[0].PromptId)
                  (sink.RenderFailures[1].PromptId)
                  "while both carry the same interaction id"
          }

          test "emitRenderFailure with CaughtBy = ErrorBoundary tags the source correctly" {
              let sink = RecordingSink()

              Render.emitRenderFailure
                  (Some(sink :> IFuaranTelemetrySink))
                  "boundary-id"
                  "ErrorBoundary"
                  "child subtree threw"
                  RenderFailureSource.ErrorBoundary
              |> ignore

              Expect.equal sink.RenderFailures.Count 1 "one event captured"
              Expect.equal sink.RenderFailures[0].CaughtBy RenderFailureSource.ErrorBoundary "boundary source marked"
          }

          test "emitRenderFailure with no sink wired is a structural no-op" {
              // The renderer's `renderWithSources` default leaves TelemetrySink
              // = None; the helper must still return a correlation id (used
              // by the fallback placeholder's data-* attribute) but emit no
              // record because there's no sink to receive it. Asserting "no
              // exception escapes" is the load-bearing pin.
              let corrId =
                  Render.emitRenderFailure None "any-node" "Display.Metric" "boom" RenderFailureSource.PerNodeGuard

              Expect.isFalse (System.String.IsNullOrEmpty corrId) "correlation id populated even without sink"
          }

          test "Throwing sink does not poison the render emit path" {
              // The IFuaranTelemetrySink contract: sinks must not throw, but if
              // they do, the renderer swallows the failure (telemetry is best-
              // effort). Mirrors the `ApplyTests.fs` "Throwing sink does not
              // poison the apply result" pin for the apply-side seam.
              let throwingSink = ThrowingRenderFailureSink() :> IFuaranTelemetrySink

              // The expression below MUST NOT throw — the helper's internal
              // try/with absorbs the sink failure.
              let corrId =
                  Render.emitRenderFailure
                      (Some throwingSink)
                      "node-id"
                      "Display.Metric"
                      "render error"
                      RenderFailureSource.PerNodeGuard

              Expect.isFalse
                  (System.String.IsNullOrEmpty corrId)
                  "helper completes + returns a correlation id even when the sink throws"
          }

          // ── PreEmitValidate awareness ────────────────────────────────────
          test "PreEmitValidate walks the boundary's child + fallback subtrees for NodeId uniqueness" {
              // The boundary's child + fallback both participate in the
              // tree-wide NodeId uniqueness check. Construct a tree where
              // the duplicate id lives INSIDE the boundary's fallback — the
              // walker should still surface it.
              let dup = "dup-id"

              let boundary: Node<Msg> =
                  Fuaran.errorBoundary
                      "boundary"
                      { Child = Fuaran.markdown dup "child"
                        Fallback = Fuaran.markdown dup "fallback" }

              let root: Node<Msg> = Fuaran.dashboard "root" { Children = [ boundary ] }

              match PreEmitValidate.validate root with
              | Ok _ -> failtest "expected DuplicateNodeId, got Ok"
              | Error defects ->
                  let hasDuplicate =
                      defects
                      |> List.exists (function
                          | PreEmitValidate.PreEmitDefect.DuplicateNodeId(id, _) -> id = dup
                          | _ -> false)

                  Expect.isTrue
                      hasDuplicate
                      "DuplicateNodeId fires for the id that appears in both child + fallback subtrees"
          }

          test "PreEmitValidate accepts a structurally-clean boundary" {
              let boundary: Node<Msg> =
                  Fuaran.errorBoundary
                      "boundary"
                      { Child = Fuaran.markdown "child" "body"
                        Fallback = Fuaran.markdown "fallback" "fallback body" }

              let root: Node<Msg> = Fuaran.dashboard "root" { Children = [ boundary ] }

              match PreEmitValidate.validate root with
              | Ok _ -> ()
              | Error defects -> failtestf "expected Ok, got defects %A" defects
          } ]
