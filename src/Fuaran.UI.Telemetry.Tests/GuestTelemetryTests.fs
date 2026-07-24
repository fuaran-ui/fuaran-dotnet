module Fuaran.UI.Telemetry.Tests.GuestTelemetryTests

open System
open Expecto
open Fuaran.UI.Telemetry.Abstractions
open Fuaran.UI.Telemetry.Default

// ============================================================================
//  GuestTelemetry — per-guest-scope sink resolution + scope-attributed
//  diagnostics (Phase 270, §4o).
//
//  The isolation contract under test: a guest's records land in ITS sink and
//  its warn/trace lines in ITS buffer — never the host's, unless the host
//  opts into the rollup / echo. The channel is a pure buffer (no console
//  call on any path), so the behaviour is pipeline-identical by construction
//  (FGP 4) — this .NET suite exercises the same code Fable ships.
// ============================================================================

let private sampleRenderFailure: RenderFailureTelemetry =
    { NodeId = "node-1"
      NodeKindName = "Markdown"
      ErrorMessage = "boom"
      CaughtBy = RenderFailureSource.PerNodeGuard
      CorrelationId = "corr-1"
      PromptId = None
      UserId = None
      Timestamp = DateTimeOffset.FromUnixTimeSeconds 1L }

let private sampleDeny: DenyTelemetry =
    { ToolName = "fuaran.setField"
      Reason = "not allowlisted"
      ActiveModule = None
      ActivePage = None
      PromptId = None
      UserId = "u1"
      Timestamp = DateTimeOffset.FromUnixTimeSeconds 2L }

[<Tests>]
let guestTelemetryTests =
    // The registry is process-global state — sequence the cases and reset
    // at each case start (the orchestration-registry test idiom).
    testSequenced
    <| testList
        "Phase270.GuestTelemetry"
        [ test "a guest warning is scope-attributed and absent from every other scope" {
              GuestTelemetry.__resetForTest ()

              GuestTelemetry.warn "guest-a" "something happened"

              let a = GuestTelemetry.diagnosticsFor "guest-a"
              Expect.hasLength a 1 "guest A holds exactly its own warning"
              Expect.equal a.Head.ScopeId "guest-a" "the diagnostic carries guest A's scope id structurally"
              Expect.equal a.Head.Level GuestDiagnosticLevel.Warning "warn buffers at Warning level"
              Expect.equal a.Head.Message "something happened" "the message carries over verbatim"

              Expect.isEmpty (GuestTelemetry.diagnosticsFor "guest-b") "guest B's buffer never sees guest A's warning"
          }

          test "sink resolution is isolated by default: a registered scope's records land in its own sink only" {
              GuestTelemetry.__resetForTest ()

              let guestSink = InMemorySink()
              let hostSink = InMemorySink() // the host's sink — never registered for a guest scope
              GuestTelemetry.registerSinkFor "guest-a" guestSink

              (GuestTelemetry.sinkFor "guest-a").RecordRenderFailure sampleRenderFailure

              Expect.hasLength guestSink.RenderFailureRecords 1 "guest A's sink holds the record"
              Expect.isEmpty hostSink.RenderFailureRecords "the host sink never sees a guest record"

              // An unregistered scope resolves to the isolated no-op — dropped, not thrown, not host-routed.
              (GuestTelemetry.sinkFor "guest-unregistered").RecordRenderFailure sampleRenderFailure
              Expect.isEmpty hostSink.RenderFailureRecords "an unregistered guest's record reaches no aggregate"
          }

          test "registering a sink after resolution takes effect on the already-resolved handle" {
              GuestTelemetry.__resetForTest ()

              let handle = GuestTelemetry.sinkFor "guest-late"
              handle.RecordDeny sampleDeny // pre-registration: isolated no-op

              let guestSink = InMemorySink()
              GuestTelemetry.registerSinkFor "guest-late" guestSink
              handle.RecordDeny sampleDeny

              Expect.hasLength guestSink.DenyRecords 1 "the stable facade re-reads registration per record"
          }

          test "opt-in rollup tees guest records into the aggregate sink; disabling restores isolation" {
              GuestTelemetry.__resetForTest ()

              let guestSink = InMemorySink()
              let aggregate = InMemorySink()
              GuestTelemetry.registerSinkFor "guest-a" guestSink

              GuestTelemetry.enableRollup aggregate
              (GuestTelemetry.sinkFor "guest-a").RecordDeny sampleDeny

              Expect.hasLength guestSink.DenyRecords 1 "the guest's own sink still gets the record"
              Expect.hasLength aggregate.DenyRecords 1 "the rollup aggregate gets it too (opt-in)"

              GuestTelemetry.disableRollup ()
              (GuestTelemetry.sinkFor "guest-a").RecordDeny sampleDeny

              Expect.hasLength guestSink.DenyRecords 2 "the guest sink keeps recording"
              Expect.hasLength aggregate.DenyRecords 1 "the aggregate stops at disable — isolation is the default"
          }

          test "allDiagnostics is the scope-attributed aggregate view, grouped per guest" {
              GuestTelemetry.__resetForTest ()

              GuestTelemetry.warn "guest-b" "b warned"
              GuestTelemetry.warn "guest-a" "a warned"
              GuestTelemetry.trace "guest-a" "a traced"

              let grouped = GuestTelemetry.allDiagnostics ()

              Expect.equal (grouped |> List.map fst) [ "guest-a"; "guest-b" ] "groups are sorted by scope id"

              let aMessages =
                  grouped |> List.find (fst >> (=) "guest-a") |> snd |> List.map _.Message

              Expect.equal aMessages [ "a warned"; "a traced" ] "each group is insertion-ordered"
          }

          test "the diagnostic echo is opt-in and scope-attributed; clearing it restores buffered isolation" {
              GuestTelemetry.__resetForTest ()

              let echoed = ResizeArray<GuestDiagnostic>()

              GuestTelemetry.warn "guest-a" "before echo"
              Expect.isEmpty echoed "no echo before opt-in — the buffer is the only destination"

              GuestTelemetry.setDiagnosticEcho echoed.Add
              GuestTelemetry.warn "guest-a" "during echo"
              Expect.hasLength echoed 1 "the opted-in echo seam receives the line"
              Expect.equal echoed[0].ScopeId "guest-a" "echoed lines stay scope-attributed"

              GuestTelemetry.clearDiagnosticEcho ()
              GuestTelemetry.warn "guest-a" "after clear"
              Expect.hasLength echoed 1 "clearing the echo restores isolation"
          }

          test "scopes enumerates sink-registered and diagnostic-bearing guests, sorted" {
              GuestTelemetry.__resetForTest ()

              GuestTelemetry.registerSinkFor "guest-c" (InMemorySink() :> IFuaranTelemetrySink)
              GuestTelemetry.warn "guest-a" "hello"

              Expect.equal (GuestTelemetry.scopes ()) [ "guest-a"; "guest-c" ] "union of both sources, sorted"
          } ]
