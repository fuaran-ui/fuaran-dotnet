module Fuaran.UI.Telemetry.Tests.ConsoleDevToolsSinkTests

open System
open Expecto
open Fuaran.UI.Telemetry.Abstractions
open Fuaran.UI.Telemetry.Default

// ============================================================================
//  ConsoleDevToolsSink — Phase 91. Asserts each record type renders one
//  severity-tagged group with the expected header/rows (captured via an
//  IDevToolsConsoleWriter shim), that the filter knobs suppress correctly, that
//  the default .NET writer reaches stdout without throwing, and that a throwing
//  writer never poisons the sink.
// ============================================================================

/// In-test writer shim — records every Group call so assertions can inspect the
/// rendered (level, header, rows) without touching stdout.
type private CaptureWriter() =
    let groups = ResizeArray<DevToolsLevel * string * (string * string) list>()
    member _.Groups = groups |> List.ofSeq

    interface IDevToolsConsoleWriter with
        member _.Group(level, header, rows) = groups.Add(level, header, rows)

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

let private sampleRenderFailure: RenderFailureTelemetry =
    { NodeId = "metric-1"
      NodeKindName = "Metric"
      ErrorMessage = "binding resolution threw"
      CaughtBy = RenderFailureSource.PerNodeGuard
      CorrelationId = "corr-1"
      PromptId = Some "prompt-A"
      UserId = Some "user-1"
      Timestamp = DateTimeOffset(2026, 5, 26, 12, 0, 2, TimeSpan.Zero) }

let private rowValue (rows: (string * string) list) (key: string) : string option =
    rows |> List.tryPick (fun (k, v) -> if k = key then Some v else None)

[<Tests>]
let tests =
    testList
        "ConsoleDevToolsSink"
        [ test "renders a successful op-apply as a single Info group" {
              let capture = CaptureWriter()

              let sink =
                  ConsoleDevToolsSink.createWithWriter (ConsoleDevToolsOptions.defaults, capture)

              sink.RecordOpApply sampleOpApply

              match capture.Groups with
              | [ (level, header, rows) ] ->
                  Expect.equal level DevToolsLevel.Info "successful apply is Info severity"
                  Expect.stringContains header "op-apply seq=1" "header carries the sequence"
                  Expect.stringContains header "applied" "header carries the outcome word"
                  Expect.equal (rowValue rows "stream") (Some "stream-1") "rows carry the stream id"
                  Expect.equal (rowValue rows "nodeId") (Some "k") "rows carry the node id"
              | other -> failtestf "expected exactly one group, got %d" other.Length
          }

          test "renders a failed op-apply as a Warn group" {
              let capture = CaptureWriter()

              let sink =
                  ConsoleDevToolsSink.createWithWriter (ConsoleDevToolsOptions.defaults, capture)

              sink.RecordOpApply
                  { sampleOpApply with
                      Outcome = OpOutcome.ApplyEngineError "boom" }

              match capture.Groups with
              | [ (level, header, _) ] ->
                  Expect.equal level DevToolsLevel.Warn "a failed apply is Warn severity"
                  Expect.stringContains header "apply-engine-error:boom" "header carries the failure detail"
              | other -> failtestf "expected exactly one group, got %d" other.Length
          }

          test "renders a deny as a Warn group with tool + reason" {
              let capture = CaptureWriter()

              let sink =
                  ConsoleDevToolsSink.createWithWriter (ConsoleDevToolsOptions.defaults, capture)

              sink.RecordDeny sampleDeny

              match capture.Groups with
              | [ (level, header, rows) ] ->
                  Expect.equal level DevToolsLevel.Warn "a denial is Warn severity"
                  Expect.stringContains header "_test.tool" "header carries the tool name"
                  Expect.stringContains header "outside-allowlist" "header carries the reason"
                  Expect.equal (rowValue rows "module") (Some "ModuleA") "rows carry the active module"
              | other -> failtestf "expected exactly one group, got %d" other.Length
          }

          test "renders a render-failure as an Error group" {
              let capture = CaptureWriter()

              let sink =
                  ConsoleDevToolsSink.createWithWriter (ConsoleDevToolsOptions.defaults, capture)

              sink.RecordRenderFailure sampleRenderFailure

              match capture.Groups with
              | [ (level, header, rows) ] ->
                  Expect.equal level DevToolsLevel.Error "a render failure is Error severity"
                  Expect.stringContains header "metric-1" "header carries the node id"
                  Expect.stringContains header "Metric" "header carries the node kind"
                  Expect.stringContains header "per-node-guard" "header carries the caught-by source"
                  Expect.equal (rowValue rows "message") (Some "binding resolution threw") "rows carry the message"
              | other -> failtestf "expected exactly one group, got %d" other.Length
          }

          test "ShowOpApply = false suppresses applies but keeps denials + failures" {
              let capture = CaptureWriter()

              let sink =
                  ConsoleDevToolsSink.createWithWriter (ConsoleDevToolsOptions.denialsAndFailuresOnly, capture)

              sink.RecordOpApply sampleOpApply
              sink.RecordDeny sampleDeny
              sink.RecordRenderFailure sampleRenderFailure

              let levels = capture.Groups |> List.map (fun (l, _, _) -> l)
              Expect.equal levels [ DevToolsLevel.Warn; DevToolsLevel.Error ] "only the deny + render-failure narrate"
          }

          test "MinSeverity = Warn suppresses an Info apply even when ShowOpApply is true" {
              let capture = CaptureWriter()

              let options =
                  { ConsoleDevToolsOptions.defaults with
                      MinSeverity = DevToolsLevel.Warn }

              let sink = ConsoleDevToolsSink.createWithWriter (options, capture)
              sink.RecordOpApply sampleOpApply // Info → below the Warn floor

              sink.RecordOpApply
                  { sampleOpApply with
                      Outcome = OpOutcome.NodeNotFound "ghost" } // Warn → passes

              let levels = capture.Groups |> List.map (fun (l, _, _) -> l)
              Expect.equal levels [ DevToolsLevel.Warn ] "the Info apply is filtered out; the Warn apply passes"
          }

          // Sequenced: redirects the *global* `Console.Out`. Under Expecto's
          // parallel runner this races with SinkTests' equivalent stdout test
          // (the StringWriter capture comes back empty when the two overlap).
          // `testSequenced` pins both into the serial phase so they never run
          // concurrently.
          testSequenced (
              test "default .NET writer reaches stdout without throwing" {
                  let originalOut = Console.Out
                  use writer = new IO.StringWriter()
                  Console.SetOut writer

                  try
                      let sink = ConsoleDevToolsSink.create ()
                      sink.RecordOpApply sampleOpApply
                      sink.RecordDeny sampleDeny
                  finally
                      Console.SetOut originalOut

                  let output = writer.ToString()
                  Expect.stringContains output "[fuaran.devtools]" "the devtools prefix lands on stdout"
                  Expect.stringContains output "op-apply seq=1" "the op-apply group header is written"
                  Expect.stringContains output "deny tool=_test.tool" "the deny group header is written"
              }
          )

          test "a throwing writer never poisons the sink" {
              let throwingWriter =
                  { new IDevToolsConsoleWriter with
                      member _.Group(_, _, _) =
                          raise (InvalidOperationException "writer is broken") }

              let sink =
                  ConsoleDevToolsSink.createWithWriter (ConsoleDevToolsOptions.defaults, throwingWriter)

              // Reaching the assertion is the test — the throw is swallowed.
              sink.RecordOpApply sampleOpApply
              sink.RecordDeny sampleDeny
              sink.RecordRenderFailure sampleRenderFailure
              Expect.isTrue true "sink swallowed the writer's throw"
          } ]
