module Fuaran.UI.Ops.ActionInvocation

// ============================================================================
//  `ActionInvocation` (Phase 889) — what the USER did, not what the AI authored.
//
//  The op stream records the AUTHORING channel: `TreeOp`s the AI emitted, hash-
//  chained, replayable. Nothing durably recorded that a user TRIGGERED anything.
//  `InteractionTelemetry` is the closest existing shape and it is counts-only,
//  payload-free and in-memory — not alignable even in principle.
//
//  ── Why this is NOT a `TreeOp`, and therefore not an `OpRecord` ────────────
//  Phase 866's charter settles it: a trigger may point at the `Action`
//  vocabulary and NOTHING else — not the `TreeOp` algebra, not the op-stream's
//  inverses, not a new host seam. A user action is therefore never expressible
//  as a `TreeOp`, so it cannot ride `IOpStreamSink`'s `OpRecord` and needs its
//  own append-only sink. That is why `IActionInvocationSink` exists rather than
//  a seventh `IFuaranTelemetrySink` member: durability lives on the op-stream
//  tier, the record is not an op, and a member-add on that interface costs
//  every direct implementer in the estate (Phase 330 measured 8 + 11 stubs).
//
//  ── The affordance has no wire identity, deliberately ─────────────────────
//  866 DECLINED an affordance id: the renderer synthesises the affordance and
//  the wire names only the capability, so a gesture has no wire name to record.
//  The honest replacement is the `(NodeId, DOM event name)` pair — both fields
//  below — plus `Provenance`, which says whether the dispatched `Action` came
//  from the authored tree or was synthesised by the renderer (the grid sort
//  header and the pager are the shipped instances, and 866 admits two more). A
//  corpus that cannot tell those apart attributes renderer behaviour to the
//  emitter.
//
//  ── No timestamp on the record. Read this before adding one ───────────────
//  The `ServerDriven` core is deliberately wall-clock-free ("the driver is
//  deterministic — same input bounds identically"), and the correction-signal
//  export pins a fixed chain instant so a session is content-addressed. A
//  timestamp minted inside the record would make the driver non-replayable and
//  an exported log non-verifying. So the instant is stamped by the HOST at the
//  sink boundary: `ActionInvocationEntry` pairs an invocation with the `At` the
//  host observed. The record itself stays a pure description of the gesture.
//
//  ── Privacy: the default captures the KEY and never the VALUE ─────────────
//  A user-action log is a different privacy class from an AI-authoring op
//  stream. `ActionCaptureMode.Redacted` is the default and captures
//  `describe`-grade text only — the `Action` CONSTRUCTOR, never payload values
//  — with `Navigate` additionally reduced to its path (a query string carries
//  user data). `PayloadBearing` is a per-host OPT-IN. This deliberately inverts
//  the phase's own goal wording: payload-bearing is the opt-in mode, or the
//  first durable write is a keystroke log. The opt-in party is the HOST, at
//  wiring; where the host is user-facing, obtaining the USER's consent is the
//  host's obligation and this record does not discharge it.
//
//  Retention is likewise not modelled here: an append-only sink is the
//  retention boundary, so retention is the host's. A policy baked into a wire
//  record is one every host inherits and none can change.
// ============================================================================

open Fuaran.Core
open Fuaran.UI.Generated

/// How much of a dispatched `Action` a sink is permitted to see.
///
/// `Redacted` is the shipped default everywhere. `PayloadBearing` is a
/// deliberate per-host opt-in and is never reached by omission — a sink
/// declares it, so wiring a sink and opting into payload values are two
/// separate acts.
[<RequireQualifiedAccess>]
type ActionCaptureMode =
    /// The action CONSTRUCTOR only — `describe`-grade. No payload value ever
    /// reaches the record.
    | Redacted
    /// The constructor PLUS the decoded payload value where one exists. Opt-in.
    | PayloadBearing

/// What became of the gesture. A denial is as recordable as a success — that
/// is the whole point of recording at the gate rather than at the effect.
[<RequireQualifiedAccess>]
type ActionOutcome =
    /// The action reached its effect.
    | Dispatched
    /// A gate refused it: the dispatch-policy gate, the host-reserved-key
    /// guard, or the route sanitiser. `reason` is log-safe text.
    | Denied of reason: string
    /// The effect threw. The throw is re-raised at the emission point — this
    /// records it, it does not swallow it.
    | Failed of message: string

/// Which of the two dispatch paths produced the record. They differ in what
/// they can observe (the client path has no DOM event name in scope; the
/// server-driven path has no `Failed` leg), so a record that did not say which
/// one produced it could not be read correctly.
[<RequireQualifiedAccess>]
type DispatchPath =
    | ClientRenderer
    | ServerDriven

/// Whether the dispatched `Action` was declared in the authored tree or
/// SYNTHESISED by the renderer under Phase 866's rule (the wire names a
/// capability; the renderer owns the affordance). Both are genuinely "what the
/// user did"; only one is "what the emitter authored".
[<RequireQualifiedAccess>]
type AffordanceProvenance =
    | TreeDeclared
    | RendererSynthesised

/// One user gesture, as recorded at the dispatch gate.
type ActionInvocation =
    {
        /// The log-safe action description — the constructor, never a payload
        /// value (`ActionInvocation.describe`).
        Action: string
        /// The node the gesture was attached to. `None` for chrome-originated
        /// gestures with no node (the `popstate` shape `InteractionTelemetry`
        /// already documents as `""`).
        NodeId: string option
        /// The DOM event name, where the path has one in scope. With `NodeId`
        /// this is the affordance's identity — 866 declined to give a gesture a
        /// wire name, so there is nothing else to record.
        Event: string option
        Outcome: ActionOutcome
        Provenance: AffordanceProvenance
        Path: DispatchPath
        /// The Phase 330 interaction id, read from the host's opaque
        /// correlation context PER DISPATCH (never captured — a captured id
        /// stamps the first interaction's id onto every later one, a
        /// correlation worse than none because it looks right). Opaque: this
        /// tier never interprets it.
        InteractionId: string option
        /// The decoded payload — populated ONLY under
        /// `ActionCaptureMode.PayloadBearing`. `None` in the default mode, and
        /// `None` for `Action.Dispatch` in EVERY mode: its `'Msg` is a closure
        /// with no wire payload by construction (`"msg":"<closure>"`), so a
        /// test asserting "every case carries a payload" would be wrong.
        Payload: JVal option
    }

/// One durable log entry: an invocation plus the instant the HOST stamped it
/// at. The pairing is the point — see the timestamp note in this file's header.
type ActionInvocationEntry =
    { At: System.DateTimeOffset
      Invocation: ActionInvocation }

/// The user-action sink seam. Deliberately NOT a member on
/// `IFuaranTelemetrySink` — see this file's header.
///
/// Sync fire-and-forget, same contract as the telemetry sinks: implementations
/// must not throw, and a host needing durability buffers internally.
type IActionInvocationSink =
    /// How much this sink is permitted to see. The opt-in lives HERE rather
    /// than at each emission point so that wiring a sink and opting into
    /// payload values cannot be conflated.
    abstract member CaptureMode: ActionCaptureMode
    /// Record one gesture. Called AFTER the outcome is known.
    abstract member RecordActionInvocation: invocation: ActionInvocation -> unit

[<RequireQualifiedAccess>]
module ActionInvocation =

    /// The well-known key whose value is the Phase 330 interaction id inside a
    /// host's opaque correlation context. Byte-identical to the renderer's
    /// `Render.promptIdKey`, and deliberately duplicated rather than referenced:
    /// this tier must not take a dependency on the Feliz renderer, and the
    /// server-driven driver must not either. `ActionInvocationTests` pins the
    /// two spellings equal so the duplication cannot drift silently.
    [<Literal>]
    let interactionIdKey = "promptId"

    /// Strip the query string and fragment from a route, keeping the path.
    /// `Navigate` is the one `describe` arm whose argument is not author-fixed
    /// vocabulary: a route carries a query string and a query string carries
    /// user data.
    let routePath (route: string) : string =
        let cutAt (c: char) (s: string) =
            match s.IndexOf c with
            | -1 -> s
            | i -> s.Substring(0, i)

        route |> cutAt '?' |> cutAt '#'

    /// A short, LOG-SAFE description of an `Action`: the constructor, and the
    /// author-declared name that identifies it (endpoint / channel / state key
    /// / tool / capability / node id), never a payload VALUE.
    ///
    /// Two arms differ from a naive constructor dump and both are deliberate:
    /// `Navigate` prints its PATH only (see `routePath`), and `SetState` prints
    /// its KEY only — the free-text value a text control writes back through it
    /// is exactly the user-typed content this must never carry.
    ///
    /// `Chain` prints as `Chain` with no contents: a chain is ONE gesture, and
    /// enumerating its constituents here would put every nested payload
    /// position back into the default-mode string.
    let describe (a: Action<'Msg>) : string =
        match a with
        | Action.Dispatch _ -> "Dispatch"
        | Action.Call(ep, _, _) -> sprintf "Call(%s)" ep
        | Action.Notify(ch, _) -> sprintf "Notify(%s)" ch
        | Action.Navigate r -> sprintf "Navigate(%s)" (routePath r)
        | Action.SetState(k, _, _) -> sprintf "SetState(%s)" k
        | Action.AiTool(t, _) -> sprintf "AiTool(%s)" t
        | Action.Chain _ -> "Chain"
        | Action.CommitLocal id -> sprintf "CommitLocal(%s)" id
        | Action.WriteToClipboard _ -> "WriteToClipboard"
        | Action.Print -> "Print"
        | Action.ReadFileBody(_, _, _, _) -> "ReadFileBody"
        | Action.Invoke(c, _) -> sprintf "Invoke(%s)" c

    /// The decoded payload for `mode`. `Redacted` yields `None` for every one
    /// of the twelve cases — that is the invariant the poison test pins.
    ///
    /// Under `PayloadBearing`, four cases still yield `None` and each for a
    /// structural reason rather than a policy one: `Dispatch` carries a closure
    /// with no wire payload; `Call` has no payload slot on the wire at all
    /// (Phase 820 routes a submit body through a host seam, not the action);
    /// `Chain` is one gesture whose constituents are not enumerated here; and
    /// `Print` (Phase 1124) is payload-free on the wire.
    let payloadFor (mode: ActionCaptureMode) (a: Action<'Msg>) : JVal option =
        match mode with
        | ActionCaptureMode.Redacted -> None
        | ActionCaptureMode.PayloadBearing ->
            match a with
            | Action.Dispatch _ -> None
            | Action.Call _ -> None
            | Action.Chain _ -> None
            | Action.Notify(_, payload) -> Some payload
            | Action.AiTool(_, args) -> Some args
            | Action.SetState(_, value, _) -> value
            | Action.Navigate route -> Some(JStr route)
            | Action.WriteToClipboard text -> Some(JStr text)
            | Action.CommitLocal nodeId -> Some(JStr nodeId)
            | Action.ReadFileBody(fileRef, _, _, _) -> Some(JStr fileRef)
            | Action.Invoke(capabilityId, _) -> Some(JStr capabilityId)
            // Phase 1124 — a FOURTH structural `None`, and the reason is the
            // case itself rather than a redaction: `Print` has no wire slot at
            // all, so there is no payload to decode. `Some JNull` would record
            // an absent value as a present one.
            | Action.Print -> None

    /// Where a gesture was observed. Grouped so the emission points pass one
    /// value rather than five positional arguments that are trivial to
    /// transpose.
    type Site =
        { Path: DispatchPath
          Provenance: AffordanceProvenance
          NodeId: string option
          Event: string option
          InteractionId: string option }

    /// A client-renderer site. The client path has no DOM event name in scope —
    /// the handler is a closure the renderer attached, not a delegated event.
    let clientSite (provenance: AffordanceProvenance) (nodeId: string option) (interactionId: string option) : Site =
        { Path = DispatchPath.ClientRenderer
          Provenance = provenance
          NodeId = nodeId
          Event = None
          InteractionId = interactionId }

    /// A server-driven site. Every gesture here arrives as an inbound
    /// `LiveEvent`, so both the node and the event name are known, and every
    /// action is tree-declared (the server-driven path synthesises none).
    let serverSite (nodeId: string) (event: string) (interactionId: string option) : Site =
        { Path = DispatchPath.ServerDriven
          Provenance = AffordanceProvenance.TreeDeclared
          NodeId = Some nodeId
          Event = Some event
          InteractionId = interactionId }

    /// Build the record for one gesture.
    let record
        (mode: ActionCaptureMode)
        (site: Site)
        (outcome: ActionOutcome)
        (action: Action<'Msg>)
        : ActionInvocation =
        { Action = describe action
          NodeId = site.NodeId
          Event = site.Event
          Outcome = outcome
          Provenance = site.Provenance
          Path = site.Path
          InteractionId = site.InteractionId
          Payload = payloadFor mode action }

    /// Build the record for a gesture whose `Action` is no longer in hand — the
    /// server-driven `RejectReason.DispatchDenied` carries only the
    /// already-log-safe description the gate produced. There is no payload to
    /// capture: the action never ran.
    let recordDescribed (site: Site) (outcome: ActionOutcome) (description: string) : ActionInvocation =
        { Action = description
          NodeId = site.NodeId
          Event = site.Event
          Outcome = outcome
          Provenance = site.Provenance
          Path = site.Path
          InteractionId = site.InteractionId
          Payload = None }

    /// Emit through an optional sink, in the sink's own capture mode. `None`
    /// records nothing and costs nothing — the shipped default at every entry
    /// point, so an unconfigured host records no user action at all.
    ///
    /// A throwing sink is swallowed: recording must never break a dispatch,
    /// same best-effort contract the telemetry sinks carry.
    let emit (sink: IActionInvocationSink option) (site: Site) (outcome: ActionOutcome) (action: Action<'Msg>) : unit =
        match sink with
        | None -> ()
        | Some s ->
            try
                s.RecordActionInvocation(record s.CaptureMode site outcome action)
            with _ ->
                ()

    /// `emit` for a gesture whose `Action` is no longer in hand. See
    /// `recordDescribed`.
    let emitDescribed
        (sink: IActionInvocationSink option)
        (site: Site)
        (outcome: ActionOutcome)
        (description: string)
        : unit =
        match sink with
        | None -> ()
        | Some s ->
            try
                s.RecordActionInvocation(recordDescribed site outcome description)
            with _ ->
                ()

[<RequireQualifiedAccess>]
module ActionInvocationSink =

    /// The do-nothing sink — recording off. Redacted, because a no-op sink that
    /// declared `PayloadBearing` would hand payload values to whatever wrapped
    /// it.
    let noop: IActionInvocationSink =
        { new IActionInvocationSink with
            member _.CaptureMode = ActionCaptureMode.Redacted
            member _.RecordActionInvocation _ = () }

    /// A collecting sink — every invocation, in order. For tests and for a host
    /// that wants an in-process buffer to flush itself.
    type Collector(mode: ActionCaptureMode) =
        let items = ResizeArray<ActionInvocation>()

        /// Defaults to the redacted mode: opting in is always an explicit act.
        new() = Collector(ActionCaptureMode.Redacted)

        member _.Recorded: ActionInvocation list = List.ofSeq items
        member _.Clear() = items.Clear()

        interface IActionInvocationSink with
            member _.CaptureMode = mode
            member _.RecordActionInvocation invocation = items.Add invocation
