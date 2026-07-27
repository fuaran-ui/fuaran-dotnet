namespace Fuaran.UI.Telemetry.Abstractions

open System

// ============================================================================
//  ValidateOutcomeTelemetry — one structured record per RUNTIME validation of
//  a tree (Phase 330).
//
//  The fourth leg of the correlation spine. A change driven by an AI emission
//  can already be followed from the op-stream record (`OpRecord.PromptId`)
//  through the apply outcome (`OpApplyTelemetry`) and the render outcome
//  (`RenderFailureTelemetry`) — but there was no validate leg at all, so
//  "did the emitted tree pass validation" was unanswerable per interaction.
//
//  WHY THIS IS NET-NEW RATHER THAN A WIRING FIX. `Fuaran.UI.Validator` is a
//  BUILD-TIME AST walker over source: it cannot see a tree an emitter produced
//  at runtime. This record is the seam for the runtime counterpart — a host
//  that validates an emitted tree reports the outcome here. The build-time
//  validator is a different job and is untouched by this.
//
//  THE SEAM, NOT A VALIDATOR. Nothing in this package validates anything. The
//  record carries a SUMMARY — how many findings, at what severity, and the
//  first few codes — deliberately, not the findings themselves: a telemetry
//  sink is an aggregation surface, and a full finding list per interaction is
//  the sort of payload that makes a sink the wrong shape for its own data.
//  A host that wants every finding has them at the call site.
//
//  `PromptId` IS OPAQUE. It is a correlation token this package neither
//  interprets nor constructs: a host stamps whatever id ties its own
//  interaction together, and every other leg of the spine carries the same
//  field with the same posture. Nothing here knows or cares where it came
//  from — which is exactly the property that lets a private layer above
//  supply a meaningful id without this public surface naming it.
//
//  FGP 4: the record shape is Fable-compatible (no closures, no reflection);
//  `DateTimeOffset` rounds through Fable cleanly per the `OpApplyTelemetry`
//  precedent.
// ============================================================================

/// Closed, host-neutral classification of a runtime validation outcome.
/// Structural — mirrors `ProviderCallOutcome` / `CacheOutcome` — so a host sink
/// encodes it without a host-error-type-aware codec.
[<RequireQualifiedAccess>]
type ValidateOutcome =
    /// The tree validated with no findings at all.
    | Clean
    /// The tree validated with findings that do not block use — a host may
    /// still apply and render it. Carries the finding count.
    | Warnings of count: int
    /// The tree carries findings a host treats as blocking. Carries the
    /// finding count.
    | Errors of count: int
    /// Validation could not run (no validator wired, a validator threw, a
    /// tree shape it could not walk). Carries a stable reason token — NOT a
    /// count, because "we do not know" is not "zero findings", and a host
    /// aggregate that conflated them would read a broken validator as a
    /// perfectly clean session.
    | NotRun of reason: string

[<RequireQualifiedAccess>]
module ValidateOutcome =
    /// Stable, key-free classification token for op-stream filters + host
    /// aggregates.
    let name (outcome: ValidateOutcome) : string =
        match outcome with
        | ValidateOutcome.Clean -> "clean"
        | ValidateOutcome.Warnings _ -> "warnings"
        | ValidateOutcome.Errors _ -> "errors"
        | ValidateOutcome.NotRun _ -> "not-run"

    /// The finding count, or `None` when validation did not run. `None` rather
    /// than `0` for the same reason `NotRun` carries no count.
    let findingCount (outcome: ValidateOutcome) : int option =
        match outcome with
        | ValidateOutcome.Clean -> Some 0
        | ValidateOutcome.Warnings count
        | ValidateOutcome.Errors count -> Some count
        | ValidateOutcome.NotRun _ -> None

    /// True when the outcome is evidence the tree validated cleanly. A
    /// `NotRun` is NOT clean — it is unknown.
    let isClean (outcome: ValidateOutcome) : bool =
        match outcome with
        | ValidateOutcome.Clean -> true
        | ValidateOutcome.Warnings _
        | ValidateOutcome.Errors _
        | ValidateOutcome.NotRun _ -> false

/// One runtime validation's worth of telemetry, surfaced to the configured
/// `IFuaranTelemetrySink` by whatever tier validated the tree.
///
/// `PromptId` is the opaque correlation token that joins this record to the
/// op-stream record, the apply telemetry, and the render telemetry of the same
/// interaction. `None` for a validation with no interaction context (an
/// operator-initiated check, a batch sweep).
type ValidateOutcomeTelemetry =
    {
        Outcome: ValidateOutcome
        /// The first few finding codes, for a host aggregate that groups by
        /// code without carrying every finding. Bounded by the producer;
        /// order-preserving. Empty for a clean or not-run outcome.
        TopCodes: string list
        PromptId: string option
        UserId: string option
        Timestamp: DateTimeOffset
    }
