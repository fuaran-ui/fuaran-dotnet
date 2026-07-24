module Fuaran.UI.Telemetry.Tests.SinkTests

open System
open Expecto
open Fuaran.UI.Telemetry.Abstractions
open Fuaran.UI.Telemetry.Default

// ============================================================================
//  Default IFuaranTelemetrySink implementations — NoOp, InMemory, Console.
// ============================================================================

let private sampleOpApply: OpApplyTelemetry =
    { StreamId = "stream-1"
      Sequence = 1
      OpKind = OpKind.EditNode
      NodeId = Some "k"
      Outcome = OpOutcome.Applied
      TimeToApplyMs = 0.5
      PromptId = Some "prompt-A"
      UserId = "user-1"
      Timestamp = DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero) }

let private sampleDeny: DenyTelemetry =
    { ToolName = "_test.tool"
      Reason = "outside-allowlist"
      ActiveModule = Some "ModuleA"
      ActivePage = Some "/page"
      PromptId = Some "prompt-A"
      UserId = "user-1"
      Timestamp = DateTimeOffset(2026, 5, 26, 12, 0, 1, TimeSpan.Zero) }

let private sampleProviderCall: ProviderCallTelemetry =
    { ProviderId = "claude"
      ModelId = "claude-opus-4-8"
      Operation = ProviderOperation.Emit
      Outcome = ProviderCallOutcome.Cancelled
      LatencyMs = 1200.0
      TokenUsage = Some { InputTokens = 10; OutputTokens = 20 }
      SessionId = Some "session-1"
      PromptId = Some "prompt-A"
      UserId = "user-1"
      Timestamp = DateTimeOffset(2026, 5, 26, 12, 0, 2, TimeSpan.Zero) }

[<Tests>]
let tests =
    testList
        "default sinks"
        [ test "NoOpSink swallows every record kind without throwing" {
              let sink = NoOpSink.create ()
              sink.RecordOpApply sampleOpApply
              sink.RecordDeny sampleDeny
              sink.RecordProviderCall sampleProviderCall
              // Reaching this line is the assertion — no call throws.
              Expect.isTrue true "NoOpSink Record* completed without throwing"
          }

          test "InMemorySink round-trips provider-call records" {
              let sink = InMemorySink()
              (sink :> IFuaranTelemetrySink).RecordProviderCall sampleProviderCall
              let recorded = sink.ProviderCallRecords
              Expect.equal recorded.Length 1 "one provider-call record buffered"
              Expect.equal recorded[0].ProviderId "claude" "provider id round-trips"
              Expect.equal recorded[0].Outcome ProviderCallOutcome.Cancelled "outcome round-trips"
              Expect.equal recorded[0].SessionId (Some "session-1") "session id round-trips"
              Expect.equal recorded[0].PromptId (Some "prompt-A") "prompt id round-trips"

              Expect.equal
                  recorded[0].TokenUsage
                  (Some { InputTokens = 10; OutputTokens = 20 })
                  "token usage round-trips"
          }

          // Acceptance criterion (Phase 171): a provider outcome lands ONLY on
          // the provider-call channel — it produces NO DenyTelemetry. The two
          // channels are physically distinct buffers, so the denial-rate signal
          // (computed over DenyRecords) can never see a provider failure.
          test "a provider-call record never lands on the deny channel" {
              let sink = InMemorySink()

              (sink :> IFuaranTelemetrySink).RecordProviderCall
                  { sampleProviderCall with
                      Outcome = ProviderCallOutcome.Cancelled }

              Expect.equal sink.ProviderCallRecords.Length 1 "provider call recorded on its own channel"
              Expect.equal sink.DenyRecords.Length 0 "no DenyTelemetry produced by a provider failure"
              Expect.equal sink.OpApplyRecords.Length 0 "no OpApplyTelemetry produced by a provider failure"
          }

          test "InMemorySink round-trips op-apply records in insertion order" {
              let sink = InMemorySink()

              let r1 = sampleOpApply

              let r2 =
                  { sampleOpApply with
                      Sequence = 2
                      NodeId = Some "k2" }

              (sink :> IFuaranTelemetrySink).RecordOpApply r1
              (sink :> IFuaranTelemetrySink).RecordOpApply r2

              let recorded = sink.OpApplyRecords
              Expect.equal recorded.Length 2 "two records buffered"
              Expect.equal recorded[0].Sequence 1 "first record by insertion order"
              Expect.equal recorded[1].Sequence 2 "second record by insertion order"
              Expect.equal recorded[1].NodeId (Some "k2") "second record carries its NodeId"
          }

          test "InMemorySink round-trips deny records" {
              let sink = InMemorySink()
              (sink :> IFuaranTelemetrySink).RecordDeny sampleDeny
              let recorded = sink.DenyRecords
              Expect.equal recorded.Length 1 "one deny record buffered"
              Expect.equal recorded[0].ToolName "_test.tool" "tool name round-trips"
              Expect.equal recorded[0].Reason "outside-allowlist" "reason round-trips"
              Expect.equal recorded[0].PromptId (Some "prompt-A") "prompt id round-trips"
          }

          test "InMemorySink ring buffer drops oldest record beyond capacity" {
              let sink = InMemorySink(capacity = 3)

              for i in 1..5 do
                  (sink :> IFuaranTelemetrySink).RecordOpApply { sampleOpApply with Sequence = i }

              let recorded = sink.OpApplyRecords
              Expect.equal recorded.Length 3 "buffer holds at most capacity records"

              let seqs = recorded |> List.map _.Sequence
              Expect.equal seqs [ 3; 4; 5 ] "the three most recent records remain; oldest two dropped"
          }

          test "InMemorySink.Clear empties all buffers" {
              let sink = InMemorySink()
              (sink :> IFuaranTelemetrySink).RecordOpApply sampleOpApply
              (sink :> IFuaranTelemetrySink).RecordDeny sampleDeny
              (sink :> IFuaranTelemetrySink).RecordProviderCall sampleProviderCall

              sink.Clear()

              Expect.equal sink.OpApplyRecords.Length 0 "op-apply buffer cleared"
              Expect.equal sink.DenyRecords.Length 0 "deny buffer cleared"
              Expect.equal sink.ProviderCallRecords.Length 0 "provider-call buffer cleared"
          }

          // Sequenced: this test redirects the *global* `Console.Out`, which
          // races with any other test doing the same under Expecto's parallel
          // runner (see ConsoleDevToolsSinkTests' stdout test). `testSequenced`
          // pins both into the serial phase so they never overlap.
          testSequenced (
              test "ConsoleSink writes to stdout without throwing" {
                  // Redirect Console.Out to a StringWriter so the assertion can
                  // confirm the [fuaran.telemetry] prefix lands and stdout I/O
                  // didn't throw.
                  let originalOut = Console.Out
                  use writer = new IO.StringWriter()
                  Console.SetOut writer

                  try
                      let sink = ConsoleSink.create ()
                      sink.RecordOpApply sampleOpApply
                      sink.RecordDeny sampleDeny
                      sink.RecordProviderCall sampleProviderCall
                  finally
                      Console.SetOut originalOut

                  let output = writer.ToString()
                  Expect.stringContains output "[fuaran.telemetry] op-apply" "op-apply line emitted"
                  Expect.stringContains output "[fuaran.telemetry] deny" "deny line emitted"
                  Expect.stringContains output "[fuaran.telemetry] provider-call" "provider-call line emitted"
                  Expect.stringContains output "stream-1" "op-apply carries StreamId"
                  Expect.stringContains output "_test.tool" "deny carries ToolName"
                  Expect.stringContains output "outcome=cancelled" "provider-call carries the outcome token"
              }
          ) ]
