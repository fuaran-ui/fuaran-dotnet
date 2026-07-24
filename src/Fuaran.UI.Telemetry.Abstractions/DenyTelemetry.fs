namespace Fuaran.UI.Telemetry.Abstractions

open System

// ============================================================================
//  DenyTelemetry — one structured record per AuthorizingRuntime denial.
//
//  Emitted from the AuthorizingRuntime's `Deny` branch in the downstream
//  orchestration tier — BEFORE the deny envelope is returned, AFTER the
//  policy decision (FGP 3 — sink never gates
//  dispatch). The executor body does not run for denied calls; this is
//  audit evidence that the policy is enforced and a signal source for
//  the drift detector ("AI is requesting this tool often; should we
//  allowlist it?").
//
//  `ActiveModule` / `ActivePage` come from the `ClientToolContext` the
//  AI runtime supplied at invoke time; `PromptId` was added to the
//  context (additive). All three are `option` for the same
//  reason the OpRecord's `PromptId` is — operator-initiated and
//  non-module-bound calls legitimately have no value.
// ============================================================================

type DenyTelemetry =
    { ToolName: string
      Reason: string
      ActiveModule: string option
      ActivePage: string option
      PromptId: string option
      UserId: string
      Timestamp: DateTimeOffset }
