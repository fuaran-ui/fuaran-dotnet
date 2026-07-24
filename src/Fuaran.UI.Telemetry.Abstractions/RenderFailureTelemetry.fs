namespace Fuaran.UI.Telemetry.Abstractions

open System

// ============================================================================
//  RenderFailureTelemetry — one structured record per render-time failure.
//
//  Emitted from two render-side seams:
//
//   1. The per-node render guard in `Fuaran.UI.Renderer.Render.render`
//      catches a throw inside an individual node's body renderer.
//      `CaughtBy = PerNodeGuard`; the renderer rendered a `fuaran-node-
//      fallback` placeholder in place of the failing node so sibling
//      nodes stay live.
//
//   2. A `NodeKind.ErrorBoundary` catches a throw inside its child
//      subtree. `CaughtBy = ErrorBoundary`; the renderer rendered the
//      boundary's typed `Fallback` Node.
//
//  The record is sized to the orchestrator's self-correction loop: NodeId
//  + NodeKindName let the orchestrator pin which specific emission was
//  malformed; ErrorMessage carries the exception message verbatim;
//  CorrelationId disambiguates concurrent emissions in a frame; CaughtBy
//  marks the seam so observers can tell "leaf failed, contained by the
//  always-on floor" apart from "subtree failed, contained by an author-
//  chosen boundary". `PromptId` / `UserId` are optional — the renderer is
//  called from the consumer's React tree per frame and has no inherent
//  session identity; hosts that want attribution pass them through the
//  `RenderContext.SessionContext` slot. Timestamp is UTC.
//
//  FGP 4 (diagnostics under both pipelines): the record shape is Fable-
//  compatible (no closures, no `System.Security.Cryptography`); `DateTimeOffset`
//  rounds through Fable cleanly per the `OpApplyTelemetry`
//  precedent.
// ============================================================================

[<RequireQualifiedAccess>]
type RenderFailureSource =
    /// The per-node render guard caught a throw inside the node's body
    /// renderer. The renderer rendered a fallback placeholder in place
    /// of the failing node so siblings stay live.
    | PerNodeGuard
    /// A `NodeKind.ErrorBoundary` caught a throw inside its child
    /// subtree. The renderer rendered the boundary's `Fallback` Node.
    | ErrorBoundary

/// Surfaced to the configured `IFuaranTelemetrySink` from the renderer.
type RenderFailureTelemetry =
    { NodeId: string
      NodeKindName: string
      ErrorMessage: string
      CaughtBy: RenderFailureSource
      CorrelationId: string
      PromptId: string option
      UserId: string option
      Timestamp: DateTimeOffset }

[<RequireQualifiedAccess>]
module RenderFailureSource =
    /// Stable short name for the source — used as a string key in host
    /// aggregates and as a `printfn` formatter. Matches the names hosts
    /// pattern-match against in `Console` / `InMemory` sinks.
    let name (source: RenderFailureSource) : string =
        match source with
        | RenderFailureSource.PerNodeGuard -> "per-node-guard"
        | RenderFailureSource.ErrorBoundary -> "error-boundary"
