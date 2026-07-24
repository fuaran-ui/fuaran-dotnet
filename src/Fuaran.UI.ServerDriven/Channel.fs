namespace Fuaran.UI.ServerDriven

open Fuaran.UI.Types
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.ServerDriven.Driver
#if !FABLE_COMPILER
open System
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
#endif

// ============================================================================
//  IFuaranLiveChannel — the transport seam (Phase 152, Track D).
//
//  The driver pushes server→client `Frame`s through this seam and receives
//  client→server `LiveEvent`s from it. NO transport type leaks into the core —
//  the driver only ever sees this interface (the "six portability rules" posture
//  applied to the live channel). This is what makes a WebSocket backend a
//  drop-in swap rather than a fork: SSE+POST and WebSocket each implement this
//  one interface; the diff → lower → patch → DOM loop above is transport-blind.
//
//  The seam is **per-connection** (one channel = one live connection). A
//  multiplexing backend (the ASP.NET SSE+POST / WebSocket packages) owns the
//  connection registry — `Map<connId, IFuaranLiveChannel>` — above this seam.
//
//  `Frame.Seq` is the per-connection op sequence — the reconnect-replay hook:
//  the SSE backend sets the `id:` field to it (EventSource replays
//  `Last-Event-ID` for free); the WebSocket backend resequences against it. The
//  full journal-replay-on-reconnect (replaying `OpStream` `OpRecord`s since the
//  client's last-applied `Seq`) is the documented follow-on; this increment
//  ships the seam, the in-memory reference channel, and the `LiveConnection`
//  glue that drives one session through one channel.
// ============================================================================

/// A server→client push frame: the patches + effects produced by one driver
/// step, tagged with the per-connection op sequence (the reconnect-replay key).
type Frame =
    { Seq: int
      Patches: DomPatch list
      Effects: ClientEffect list }

/// The transport seam. A backend implements `Push` (server→client) and surfaces
/// inbound `LiveEvent`s to the handler registered via `Receive`.
type IFuaranLiveChannel =
    /// Send a frame to the connected client.
    abstract Push: frame: Frame -> unit
    /// Register the handler the transport invokes for each inbound client event.
    abstract Receive: handler: (LiveEvent -> unit) -> unit
    /// Tear the connection down.
    abstract Close: unit -> unit

/// In-memory reference channel — the headlessly-testable implementation the
/// SSE+POST / WebSocket backends specialise. `Pushed` records every frame sent
/// to the client (assert against it); `Send` simulates an inbound client event.
type InMemoryChannel() =
    let pushed = ResizeArray<Frame>()
    let mutable handler: (LiveEvent -> unit) option = None
    let mutable closed = false

    /// Every frame pushed to the client, in order.
    member _.Pushed: Frame list = List.ofSeq pushed

    /// True once `Close` has been called.
    member _.Closed = closed

    /// Simulate an inbound client→server event (what a real transport's POST /
    /// WS-message receiver would deliver to the registered handler).
    member _.Send(ev: LiveEvent) =
        match handler with
        | Some h -> h ev
        | None -> ()

    interface IFuaranLiveChannel with
        member _.Push(frame) = pushed.Add frame
        member _.Receive(h) = handler <- Some h
        member _.Close() = closed <- true

/// Documented defaults for the connection's resource bounds (Phase 212).
[<RequireQualifiedAccess>]
module LiveConnectionDefaults =
    /// Default cap on the per-connection reconnect-replay buffer, in frames.
    /// At capacity the OLDEST frame is evicted, so a never-reconnecting client
    /// cannot grow server memory without limit. A client reconnecting from
    /// behind the retained window gets a partial replay (the durable journal
    /// path — Phase 155 — is the complete recovery for that case).
    [<Literal>]
    let ReplayBufferCapacity = 512

/// One live connection: binds a `LiveSession` to a channel. On each inbound
/// event it steps the driver, advances the op sequence, and pushes a `Frame`
/// when the step produced any patch / effect. A G1-rejected step pushes nothing
/// and leaves the session unchanged — the reject is emitted through the
/// always-on `DriverServices.OnReject` sink (Phase 212; the structured-error
/// frame to the client is the documented follow-on). The connection owns the
/// connection's mutable session (a connection is inherently stateful — one
/// evolving model). `replayBufferCapacity` bounds the reconnect-replay buffer
/// (default `LiveConnectionDefaults.ReplayBufferCapacity`; oldest evicted).
type LiveConnection<'Model, 'Msg>
    (connId: string, initial: LiveSession<'Model, 'Msg>, channel: IFuaranLiveChannel, ?replayBufferCapacity: int) as this
    =
    let mutable session = initial
    let mutable seq = 0

    let bufferCap =
        defaultArg replayBufferCapacity LiveConnectionDefaults.ReplayBufferCapacity

    // Per-connection frame buffer — the replay log for reconnect. The SSE /
    // WebSocket backends call `Resync` with the client's last-applied `Seq`
    // (SSE's `Last-Event-ID`) to re-push the frames it missed across a
    // transport drop. Bounded (Phase 212): at `bufferCap` the oldest frame is
    // evicted — memory per connection is capped; a reconnect from behind the
    // retained window replays partially (Phase 155 durability is the complete
    // recovery). A switch to replaying `OpStream` `OpRecord`s from a
    // checkpoint remains the documented follow-on.
    let buffer = ResizeArray<Frame>()

    /// Append to the replay buffer, evicting the oldest frame at capacity.
    let bufferFrame (frame: Frame) =
        if buffer.Count >= bufferCap && buffer.Count > 0 then
            buffer.RemoveAt 0

        buffer.Add frame

    // Phase 156 — optional runtime form validation. Off (None) until
    // `EnableFormValidation`, so a connection without it behaves exactly as 152.
    // When on, a form `submit` is validated server-side BEFORE any mutation; an
    // invalid submit pushes field-error patches and suppresses the state change.
    let mutable formValidator: FormValidation.FormValidator<'Msg> option = None

    // Phase 157 — optional navigation routing. Off (None) until `EnableRouting`.
    // When on, a `popstate` event + a legitimate `Action.Navigate` to a resolver-
    // known route swap the tree in place (+ `PushState`); unknown routes fall
    // through to a full-reload `ClientEffect.Navigate`.
    let mutable router: Navigation.RouteResolver<'Msg> option = None

    // Phase 158 QW7 — optional per-interaction telemetry. Off (None) until
    // `EnableTelemetry`. `lastOpCount` captures the step's applied-op count via a
    // wrapped `OnApply` (reset per Handle, so a rejected step records 0).
    let mutable telemetrySink: IInteractionTelemetrySink option = None
    let mutable lastOpCount = 0

#if !FABLE_COMPILER
    // Phase 155 durability — off until `EnableDurability` is called, so a
    // connection constructed the 152 way behaves EXACTLY as before (AC4). When
    // on, every applied step is journaled (hash-chained) to `durStore` and the
    // rendered tree is checkpointed on the op-count cadence; reconnect-after-
    // restart reconstructs via `SessionReplay.reconstruct` + `ResumeFrom`.
    let mutable durStore: IFuaranSessionStore<'Msg> option = None
    let mutable durSessionId = connId
    let mutable durHead = HashChain.genesisPreviousHash
    let mutable durOpSeq = 0
    let mutable durSinceCheckpoint = 0
    let mutable durCheckpointEvery = 0
    let mutable durUserId = "server-driven"
    let mutable durClock: unit -> DateTimeOffset = fun () -> DateTimeOffset.UtcNow
#endif

    do
        channel.Receive(fun ev ->
            if ev.ConnId = connId then
                this.Handle ev)

    /// The current server-held session (model + tree).
    member _.Session = session

    /// The current op sequence pushed to this connection.
    member _.Sequence = seq

    /// Turn on Phase 156 runtime form validation. `validator` is the host's
    /// business-rule validator; declared constraints (`Required` / `RangedNumber`
    /// bounds) are enforced server-side regardless (the trust floor). On an
    /// invalid form submit the connection pushes field-error patches and
    /// suppresses the mutation; on a valid submit it clears the field-error
    /// attributes and steps normally. Pass `FormValidation.declaredOnly` for
    /// declared-constraint enforcement with no extra business rules.
    member _.EnableFormValidation(validator: FormValidation.FormValidator<'Msg>) = formValidator <- Some validator

    /// Turn on Phase 157 navigation routing. `resolver` is the host's route table
    /// (`route -> Node tree option`): a known route swaps in place (+ `PushState`),
    /// an unknown route falls through to a full-reload `ClientEffect.Navigate`.
    /// `popstate` events swap the tree to the popped route. Non-nav events flow
    /// through the normal (form-validating, if enabled) step.
    member _.EnableRouting(resolver: Navigation.RouteResolver<'Msg>) = router <- Some resolver

    /// Turn on Phase 158 QW7 per-interaction telemetry. Each handled interaction
    /// records `{ node, event, op/patch/effect counts, patch bytes, rejected }`
    /// to `sink` — the driver-side signal for which surfaces want WebSocket vs
    /// SSE. Non-breaking; off until called.
    member _.EnableTelemetry(sink: IInteractionTelemetrySink) =
        telemetrySink <- Some sink
        // Capture each step's applied-op count by wrapping `OnApply` (the host's
        // own sink still fires; composes with the durability wrap if present).
        let original = session.Services.OnApply

        session <-
            { session with
                Services =
                    { session.Services with
                        OnApply =
                            fun ops ->
                                original ops
                                lastOpCount <- List.length ops } }

    /// Re-push every RETAINED buffered frame newer than `lastSeq` — the
    /// transport-agnostic reconnect replay. The backend calls this when a client
    /// reconnects carrying its last-applied sequence, so a brief transport drop
    /// loses no state. The buffer is bounded (Phase 212): a client reconnecting
    /// from behind the retained window gets only the retained tail — the durable
    /// journal (Phase 155) is the complete recovery. Returns the number of
    /// frames replayed.
    member _.Resync(lastSeq: int) : int =
        let missed = buffer |> Seq.filter (fun f -> f.Seq > lastSeq) |> List.ofSeq
        missed |> List.iter channel.Push
        List.length missed

    /// Step the connection with one inbound event: drive the session, advance
    /// the op sequence, and push a `Frame` when the step produced any patch /
    /// effect. The channel's inbound handler routes here; hosts that pump events
    /// directly call it too.
    member this.Handle(ev: LiveEvent) =
        // The base step is the form buffer-flush protocol (Phase 152 form policy)
        // layered over `Driver.step`: it flushes buffered `Binding.Local` fields
        // on submit / `CommitLocal`, and — when form validation is enabled
        // (Phase 156) — validates the submit before mutating. `formValidator =
        // None` keeps the strictly-opt-in validation behaviour (declared
        // enforcement runs only with a validator present). Routing (if enabled)
        // layers over it, intercepting nav / popstate and delegating the rest.
        let baseStep s e = FormBuffer.step formValidator s e

        lastOpCount <- 0 // reset so a rejected step (no OnApply) records 0 ops

        let s2, out =
            match router with
            | Some resolver -> Navigation.stepWithRouting resolver baseStep session ev
            | None -> baseStep session ev

        session <- s2

        match telemetrySink with
        | Some sink ->
            sink.RecordInteraction
                { NodeId = ev.NodeId
                  Event = ev.Event
                  OpCount = lastOpCount
                  PatchCount = List.length out.Patches
                  PatchBytes = DomPatch.encodeList out.Patches |> String.length
                  EffectCount = List.length out.Effects
                  Rejected = out.Rejected.IsSome }
        | None -> ()

        match out.Rejected with
        | Some reason ->
            // Phase 212 — the always-on reject sink: a denial is recorded even
            // with interaction telemetry off. (The structured error frame back
            // to the client is the documented follow-on.)
            session.Services.OnReject reason
        | None ->
            if not (List.isEmpty out.Patches && List.isEmpty out.Effects) then
                seq <- seq + 1

                let frame =
                    { Seq = seq
                      Patches = out.Patches
                      Effects = out.Effects }

                bufferFrame frame
                channel.Push frame
#if !FABLE_COMPILER
        // The applied ops were journaled inside the wrapped `OnApply` (so the
        // chain advances exactly with `update`); the checkpoint snapshots the
        // NOW-current `session.Tree`, which is why it runs here, after `s2`.
        this.MaybeCheckpoint()
#endif

#if !FABLE_COMPILER
    /// Turn on Phase 155 durability for this connection. Journals every applied
    /// step's ops (hash-chained) to `store` and checkpoints the rendered tree
    /// every `checkpointEvery` ops (0 = checkpoint only on `CheckpointNow`). The
    /// in-memory `SessionStore.inMemory` keeps the single-node 152 behaviour;
    /// pass `SessionStore.overSink (sqliteSink)` (or a shared store) for restart /
    /// multi-node durability. Idempotent wrapping is the caller's concern — call
    /// once per connection, right after construction.
    member _.EnableDurability
        (
            store: IFuaranSessionStore<'Msg>,
            ?sessionId: string,
            ?checkpointEvery: int,
            ?userId: string,
            ?clock: unit -> DateTimeOffset
        ) =
        durStore <- Some store
        durSessionId <- defaultArg sessionId connId
        durCheckpointEvery <- defaultArg checkpointEvery 0
        durUserId <- defaultArg userId "server-driven"
        durClock <- defaultArg clock (fun () -> DateTimeOffset.UtcNow)

        // Wrap the host's `OnApply` (FGP 5 telemetry/journal seam) so the durable
        // journal advances in lock-step with the apply engine while the host's
        // own sink still fires. `Async.RunSynchronously` is the reference-glue
        // posture (the InMemory store is sync; a Sqlite host may pump async).
        let original = session.Services.OnApply

        let wrapped (ops: TreeOp<'Msg> list) =
            original ops

            if not (List.isEmpty ops) then
                let head, sequence =
                    SessionJournal.append store durSessionId durUserId (durClock ()) durHead durOpSeq ops
                    |> Async.RunSynchronously

                durHead <- head
                durOpSeq <- sequence
                durSinceCheckpoint <- durSinceCheckpoint + List.length ops

        session <-
            { session with
                Services =
                    { session.Services with
                        OnApply = wrapped } }

    /// The op-sequence the durable journal has reached (the head the next
    /// reconnecting client should report as its `LastSeq`).
    member _.DurableSequence = durOpSeq

    /// Materialise + persist a checkpoint of the current rendered tree NOW (the
    /// graceful-disconnect / explicit-session-end flush). No-op when durability
    /// is off.
    member _.CheckpointNow() =
        match durStore with
        | Some store ->
            let cp =
                SessionJournal.makeCheckpoint durSessionId durOpSeq durHead (durClock ()) session.Tree

            store.Checkpoint(durSessionId, cp) |> Async.RunSynchronously
            durSinceCheckpoint <- 0
        | None -> ()

    member private this.MaybeCheckpoint() =
        if
            durStore.IsSome
            && durCheckpointEvery > 0
            && durSinceCheckpoint >= durCheckpointEvery
        then
            this.CheckpointNow()

    /// Re-baseline this (freshly constructed) connection to a reconstructed tree
    /// after a restart / node move and push a single full-document resync frame
    /// to the client, so it adopts the exact current DOM. `tree` / `sequence` /
    /// `headHash` come from `SessionReplay.reconstruct`. The diff baseline becomes
    /// `tree`, so subsequent steps patch granularly from here. (The host re-binds
    /// the model — for the bounded driver the store is fully reconstructable; for
    /// the hand-authored `'Model` driver the model is host-owned, so the host
    /// supplies it; the client DOM is correct either way.)
    member _.ResumeFrom(tree: Node<'Msg>, sequence: int, headHash: string) =
        session <- { session with Tree = tree }
        durHead <- headHash
        durOpSeq <- sequence
        durSinceCheckpoint <- 0
        let (NodeId rootId) = tree.Id
        let html = session.Services.RenderFragment tree
        seq <- seq + 1

        let frame =
            { Seq = seq
              Patches = [ DomPatch.ReplaceFragment(rootId, html) ]
              Effects = [] }

        bufferFrame frame
        channel.Push frame
#endif
