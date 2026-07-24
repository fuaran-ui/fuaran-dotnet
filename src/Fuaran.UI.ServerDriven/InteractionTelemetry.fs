namespace Fuaran.UI.ServerDriven

// ============================================================================
//  Per-interaction telemetry (Phase 158, QW7).
//
//  A near-free observability hook over the server-driven loop: each handled
//  interaction emits the driver-side metrics that tell an operator WHICH
//  surfaces actually need WebSocket vs SSE — patch size (the wire cost), op count
//  (the diff churn), effect count, and whether the interaction was rejected.
//
//  Why a ServerDriven-local seam (not `IFuaranTelemetrySink`): the shipped
//  `Fuaran.UI.Telemetry` sink records `OpApply` / `Deny` / `RenderFailure` — the
//  apply-engine's vocabulary, not a per-interaction (node + event + patch-size)
//  shape. Forcing this in would be a breaking widening of that abstraction; a
//  small local seam keeps the server-driven concern self-contained (and a host
//  that wants both just fans out in its sink impl).
//
//  Round-trip LATENCY is deliberately absent from the driver-emitted record: the
//  driver is deterministic (no wall-clock — same input bounds identically), so
//  latency is a transport/host measurement. A host that wants it wraps the sink
//  and stamps the timing it measured at the request boundary.
//
//  Pure + Fable-clean: the record + seam carry no transport / renderer dep.
// ============================================================================

/// One handled interaction's driver-side metrics.
type InteractionTelemetry =
    {
        /// The originating node id (the event's `NodeId`; `""` for a `popstate`).
        NodeId: string
        /// The DOM event name (`click` / `change` / `submit` / `popstate` / …).
        Event: string
        /// `TreeOp`s applied this interaction (the diff churn). `0` for a rejected
        /// or no-op interaction.
        OpCount: int
        /// `DomPatch`es shipped to the client.
        PatchCount: int
        /// Encoded size of the patch payload in bytes — the WebSocket-vs-SSE
        /// signal (a surface that ships large patches per interaction wants a
        /// persistent socket).
        PatchBytes: int
        /// `ClientEffect`s shipped (navigation / clipboard / file-read / …).
        EffectCount: int
        /// `true` when the interaction was rejected at the G1 / dispatch boundary
        /// (no state change reached the client).
        Rejected: bool
    }

/// The per-interaction telemetry seam. A host wires a sink to fan the metrics
/// into its observability stack (or to drive the WS-vs-SSE decision per surface).
type IInteractionTelemetrySink =
    abstract member RecordInteraction: telemetry: InteractionTelemetry -> unit

module InteractionTelemetry =
    /// The do-nothing sink — telemetry off.
    let noop: IInteractionTelemetrySink =
        { new IInteractionTelemetrySink with
            member _.RecordInteraction _ = () }

    /// A collecting sink — records every interaction for inspection / tests.
    type Collector() =
        let items = ResizeArray<InteractionTelemetry>()

        /// Every interaction recorded, in order.
        member _.Recorded: InteractionTelemetry list = List.ofSeq items

        interface IInteractionTelemetrySink with
            member _.RecordInteraction telemetry = items.Add telemetry
