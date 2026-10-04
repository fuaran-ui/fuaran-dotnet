namespace Fuaran.UI.Program

open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.Program.Bounded
open Fuaran.UI.Program
open Fuaran.UI.Program.BoundedDriver

// ============================================================================
//  BoundedConnection — the bounded placement's channel glue.
//
//  The hand-authored server driver binds a `(Model, update, view)` session to a
//  transport through `LiveConnection`. This is the same binding for the bounded
//  loop: one connection = one `BoundedSession` + one `IFuaranLiveChannel`. On
//  each inbound event it steps the driver, advances the per-connection op
//  sequence, and pushes a `Frame` when the step produced any patch or effect.
//
//  Nothing here is new algebra. `BoundedDriver.step` is already transport-shaped
//  — event in, patches + effects out, session unchanged on a refusal — so this
//  file is sequencing, buffering and refusal routing, and the transport seam
//  itself (`IFuaranLiveChannel`, `Frame`) is consumed unchanged from the UI
//  tier. That is why a bounded page can now be served over any backend that
//  implements the seam, with no per-transport bounded code.
//
//  ── Out-of-band edits ────────────────────────────────────────────────────────
//  A tree editor outside the interaction loop — a browser extension speaking the
//  published relay contract is the motivating case — wants to submit a `TreeOp`
//  against the session's tree rather than an interaction. That is coherent on
//  THIS loop and only on this loop: the bounded session's `BaseTree` is fixed
//  structural state, so an op applied to it re-resolves, diffs and lowers
//  through the existing path with no new machinery. It is NOT coherent on a
//  model-backed loop, where the tree is a projection of the model and the next
//  step's diff reverts the edit — so no such entry point exists there, and none
//  should be added.
//
//  `ApplyOutOfBand` is therefore the only way an op reaches `BaseTree`, and it
//  is **off until a host installs a grant policy** (`EnableOutOfBandApply`). An
//  absent policy refuses every submission. That default is the same one
//  `BoundedServices.create` takes for dispatch and for the same reason: this
//  loop exists to run trees it does not trust, on infrastructure it shares.
//
//  Attribution is carried and never believed (it is a claim made by the
//  submitter, over a channel the session did not authenticate). It is passed to
//  the grant policy and echoed on the refusal so a host can record it beside
//  the principal it *did* authenticate — never in place of it.
// ============================================================================

/// One out-of-band edit submission: the op, and the submitter's own claim about
/// who authored it. `Actor` is advisory text — a host records it, a gate may
/// read it, and neither may treat it as identity.
type OutOfBandRequest =
    { ConnId: string
      Actor: string
      Op: TreeOp<obj> }

/// The host's grant policy verdict over one submission. `Deny` carries the
/// host's own reason so the refusal that reaches the submitter says something
/// more useful than "no".
type OutOfBandDecision =
    | Grant
    | Deny of reason: string

/// Why an out-of-band edit did not reach `BaseTree`. Deliberately a type of its
/// own rather than a case on `BoundedReject`: that DU is documented as the
/// EVENT-level refusal and nothing else, and an out-of-band edit is not an
/// event. Keeping them apart also keeps `BoundedReject` closed — a host
/// matching it exhaustively is not broken by this file existing.
type OutOfBandRefusal =
    /// No grant policy is installed, or the installed one declined. The detail
    /// is the host's reason (or the default-closed explanation).
    | NotGranted of detail: string
    /// The op did not apply to the current `BaseTree` — an unknown node, a
    /// field the target's spec does not carry, and so on. The detail is the
    /// apply engine's own structured message.
    | Unapplicable of detail: string
    /// G2: the edited tree's render cost exceeds `MaxNodes`. The same ceiling
    /// the interaction path enforces, applied to the edit — an out-of-band
    /// author must not be able to buy unbounded work that an interacting one
    /// cannot.
    | OverBudget of detail: string

/// The outcome of one out-of-band submission: the patches pushed to the client
/// (empty on a refusal), and the refusal if there was one.
type OutOfBandOutcome =
    { Patches: DomPatch list
      Refused: OutOfBandRefusal option }

/// One bounded live connection: binds a `BoundedSession` to a channel. Owns the
/// connection's mutable session (a connection is inherently stateful — one
/// evolving store) and its op sequence. `replayBufferCapacity` bounds the
/// reconnect-replay buffer (default `LiveConnectionDefaults.ReplayBufferCapacity`;
/// oldest evicted), so a never-reconnecting client cannot grow server memory
/// without limit.
type BoundedConnection(connId: string, initial: BoundedSession, channel: IFuaranLiveChannel, ?replayBufferCapacity: int) as this
    =
    let mutable session = initial
    let mutable seq = 0
    let mutable lastDiagnostics: BoundedDiagnostic list = []
    let mutable rejectSink: (BoundedReject -> unit) option = None
    let mutable grantPolicy: (OutOfBandRequest -> OutOfBandDecision) option = None

    let bufferCap =
        defaultArg replayBufferCapacity LiveConnectionDefaults.ReplayBufferCapacity

    // Per-connection frame buffer — the replay log for reconnect, bounded
    // exactly as the hand-authored connection's is. A client reconnecting from
    // behind the retained window gets the retained tail only.
    let buffer = ResizeArray<Frame>()

    let bufferFrame (frame: Frame) =
        if buffer.Count >= bufferCap && buffer.Count > 0 then
            buffer.RemoveAt 0

        buffer.Add frame

    /// Advance the sequence and push, but only when there is something to push —
    /// an empty frame would consume a sequence number the client would then wait
    /// to reconcile against nothing.
    let push (patches: DomPatch list) (effects: ClientEffect list) =
        if not (List.isEmpty patches && List.isEmpty effects) then
            seq <- seq + 1

            let frame =
                { Seq = seq
                  Patches = patches
                  Effects = effects }

            bufferFrame frame
            channel.Push frame

    do
        channel.Receive(fun ev ->
            if ev.ConnId = connId then
                this.Handle ev)

    /// The current server-held bounded session (base tree + store + resolved).
    member _.Session = session

    /// The current op sequence pushed to this connection.
    member _.Sequence = seq

    /// The bounded interpreter's readable no-op signals from the most recent
    /// step — "this action is inert on the generated-app path". Observability for
    /// emission debugging, never behaviour. Surfaced as a property rather than a
    /// sink because the driver already produces them per step and dropping them
    /// on the floor would lose signal the loop paid for.
    member _.LastDiagnostics = lastDiagnostics

    /// Route G1 / G2 refusals to `sink`. Off until called: a connection without
    /// it behaves exactly as `BoundedDriver.step` does, silently leaving the
    /// session unchanged. (The structured error frame back to the client needs a
    /// correlated response leg the channel does not have.)
    member _.EnableRejectSink(sink: BoundedReject -> unit) = rejectSink <- Some sink

    /// Install the host's grant policy for out-of-band tree edits. **Until this
    /// is called every submission is refused**, so the capability is opt-in per
    /// connection and a host that never wires it cannot be reached through it.
    member _.EnableOutOfBandApply(policy: OutOfBandRequest -> OutOfBandDecision) = grantPolicy <- Some policy

    /// Re-push every RETAINED buffered frame newer than `lastSeq` — the
    /// transport-agnostic reconnect replay. Returns the number of frames
    /// replayed.
    member _.Resync(lastSeq: int) : int =
        let missed = buffer |> Seq.filter (fun f -> f.Seq > lastSeq) |> List.ofSeq
        missed |> List.iter channel.Push
        List.length missed

    /// Step the connection with one inbound event: drive the bounded session,
    /// advance the op sequence, and push a `Frame` when the step produced any
    /// patch / effect. A refused step pushes nothing and leaves the session
    /// unchanged.
    member _.Handle(ev: LiveEvent) =
        let s2, out = BoundedDriver.step session ev
        session <- s2
        lastDiagnostics <- out.Diagnostics

        match out.Rejected with
        | Some reject ->
            match rejectSink with
            | Some sink -> sink reject
            | None -> ()
        | None -> push out.Patches out.Effects

    /// Submit one out-of-band `TreeOp` against this session's `BaseTree`.
    ///
    /// Granted, it applies to the base tree, re-prices the tree against `MaxNodes`,
    /// re-resolves the store bindings, diffs against the current resolved tree and
    /// pushes the lowered patches on the ordinary frame stream — so the edit reaches
    /// the client through the same subscription every server push uses, and every
    /// LATER interaction resolves against the edited tree. Refused, the session is
    /// returned untouched and nothing is pushed.
    ///
    /// The refusal is RETURNED rather than pushed. The channel is push-frames
    /// outbound and fire-and-forget inbound, with no correlation id and no
    /// response envelope, so there is no honest way to deliver a refusal to the
    /// submitter from here — routing it into a patch frame would be smuggling a
    /// response through a broadcast. The caller that submitted the op is the one
    /// holding the correlation, and it gets the typed value.
    member _.ApplyOutOfBand(request: OutOfBandRequest) : OutOfBandOutcome =
        let refuse (r: OutOfBandRefusal) = { Patches = []; Refused = Some r }

        let decision =
            match grantPolicy with
            | None -> Deny "out-of-band apply is not enabled on this connection"
            | Some policy -> policy request

        match decision with
        | Deny reason -> refuse (NotGranted reason)
        | Grant ->
            match Apply.apply request.Op session.BaseTree with
            | Error err -> refuse (Unapplicable err.Message)
            | Ok newBase ->
                let ceiling = session.Services.Budget.MaxNodes
                let cost = Budget.treeCost ceiling newBase

                if cost > ceiling then
                    refuse (OverBudget(sprintf "edited tree cost %d exceeds MaxNodes %d" cost ceiling))
                else
                    let newResolved = Resolve.resolveTree session.Store newBase
                    let ops = TreeOpDiff.diff session.Resolved newResolved
                    session.Services.OnApply ops
                    let patches = Lowering.lower session.Services.RenderFragment newResolved ops

                    session <-
                        { session with
                            BaseTree = newBase
                            Resolved = newResolved
                            NodeCount = cost }

                    push patches []
                    { Patches = patches; Refused = None }
