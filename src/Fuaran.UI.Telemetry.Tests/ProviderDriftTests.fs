module Fuaran.UI.Telemetry.Tests.ProviderDriftTests

open System
open Expecto
open Fuaran.UI.Telemetry.Abstractions
open Fuaran.UI.Telemetry.Drift
open Fuaran.UI.Telemetry.Drift.ProviderDrift

// ============================================================================
//  Provider-reliability drift detector (Phase 171) — the success-rate twin of
//  the op-apply detector, over the dedicated ProviderCallTelemetry stream.
// ============================================================================

let private call (providerId: string) (outcome: ProviderCallOutcome) : ProviderCallTelemetry =
    { ProviderId = providerId
      ModelId = "test-model"
      Operation = ProviderOperation.Emit
      Outcome = outcome
      LatencyMs = 100.0
      TokenUsage = None
      SessionId = Some "s"
      PromptId = Some "p"
      UserId = "test"
      Timestamp = DateTimeOffset(2026, 6, 14, 12, 0, 0, TimeSpan.Zero) }

[<Tests>]
let tests =
    testList
        "provider-reliability drift detector"
        [ test "ProviderCallOutcome.name mirrors the Phase 123 taxonomy tokens" {
              Expect.equal (ProviderCallOutcome.name ProviderCallOutcome.Success) "success" "success token"
              Expect.equal (ProviderCallOutcome.name ProviderCallOutcome.Transport) "transport" "transport token"

              Expect.equal
                  (ProviderCallOutcome.name (ProviderCallOutcome.ProviderError 429))
                  "provider-error:429"
                  "429 token"

              Expect.equal (ProviderCallOutcome.name ProviderCallOutcome.Malformed) "malformed" "malformed token"
              Expect.equal (ProviderCallOutcome.name ProviderCallOutcome.Cancelled) "cancelled" "cancelled token"

              Expect.equal
                  (ProviderCallOutcome.name ProviderCallOutcome.EmptyCompletion)
                  "empty-completion"
                  "empty-completion token"
          }

          test "isSuccess / isFailure partition the outcome space" {
              Expect.isTrue (ProviderCallOutcome.isSuccess ProviderCallOutcome.Success) "success is a success"
              Expect.isFalse (ProviderCallOutcome.isFailure ProviderCallOutcome.Success) "success is not a failure"
              Expect.isTrue (ProviderCallOutcome.isFailure ProviderCallOutcome.Cancelled) "cancelled is a failure"

              Expect.isFalse
                  (ProviderCallOutcome.isSuccess (ProviderCallOutcome.ProviderError 500))
                  "500 is not a success"
          }

          test "ProviderDrift.run flags a reliability regression beyond the threshold" {
              // 90 % baseline success; 70 % current → 20pp drop > 10pp default.
              let baseline =
                  [ for _ in 1..90 -> call "claude" ProviderCallOutcome.Success ]
                  @ [ for _ in 1..10 -> call "claude" ProviderCallOutcome.Cancelled ]

              let current =
                  [ for _ in 1..70 -> call "claude" ProviderCallOutcome.Success ]
                  @ [ for _ in 1..30 -> call "claude" (ProviderCallOutcome.ProviderError 429) ]

              let findings = ProviderDrift.run Defaults.thresholds baseline current
              Expect.equal findings.Length 1 "one finding per provider"

              match findings[0] with
              | Regression(providerId, baselineRate, currentRate, drop) ->
                  Expect.equal providerId "claude" "provider id carried"
                  Expect.floatClose Accuracy.medium baselineRate 0.90 "baseline success rate"
                  Expect.floatClose Accuracy.medium currentRate 0.70 "current success rate"
                  Expect.floatClose Accuracy.medium drop 0.20 "20pp drop"
              | other -> failtestf "Expected Regression, got %A" other
          }

          test "ProviderDrift.run reports NoRegression below the threshold" {
              let baseline = [ for _ in 1..100 -> call "openai" ProviderCallOutcome.Success ]

              let current =
                  [ for _ in 1..95 -> call "openai" ProviderCallOutcome.Success ]
                  @ [ for _ in 1..5 -> call "openai" ProviderCallOutcome.Transport ]

              match (ProviderDrift.run Defaults.thresholds baseline current)[0] with
              | NoRegression(p, b, c) ->
                  Expect.equal p "openai" "provider id"
                  Expect.floatClose Accuracy.medium b 1.0 "baseline 100%"
                  Expect.floatClose Accuracy.medium c 0.95 "current 95%"
              | other -> failtestf "Expected NoRegression, got %A" other
          }

          test "ProviderDrift.run emits InsufficientData under the baseline floor" {
              let baseline = [ for _ in 1..10 -> call "gemini" ProviderCallOutcome.Success ]
              let current = [ for _ in 1..10 -> call "gemini" ProviderCallOutcome.Success ]

              match (ProviderDrift.run Defaults.thresholds baseline current)[0] with
              | InsufficientData(p, count) ->
                  Expect.equal p "gemini" "provider id"
                  Expect.equal count 10 "baseline sample count reported"
              | other -> failtestf "Expected InsufficientData, got %A" other
          }

          // The provider-drift detector reads ProviderCallTelemetry; the
          // op-apply drift detector reads OpApplyTelemetry; the denial-rate
          // signal reads DenyTelemetry. The three streams are type-disjoint, so
          // a provider failure can NEVER inflate the denial-rate signal — the
          // Phase 171 separation, pinned at the type level.
          test "the provider-drift signal is computed over provider calls, not denials" {
              let baseline =
                  [ for _ in 1..60 -> call "claude" ProviderCallOutcome.Success ]
                  @ [ for _ in 1..40 -> call "claude" ProviderCallOutcome.Cancelled ]

              let current = [ for _ in 1..100 -> call "claude" ProviderCallOutcome.Cancelled ]

              // run takes ProviderCallTelemetry seq — it is impossible to pass a
              // DenyTelemetry into it. The denial-rate aggregate (Detect/Deny
              // side) is computed over a different record type entirely.
              let findings = ProviderDrift.run Defaults.thresholds baseline current
              Expect.equal findings.Length 1 "one provider finding"

              match findings[0] with
              | Regression(_, _, currentRate, _) ->
                  Expect.floatClose Accuracy.medium currentRate 0.0 "current window is all failures"
              | other -> failtestf "Expected Regression, got %A" other
          } ]
