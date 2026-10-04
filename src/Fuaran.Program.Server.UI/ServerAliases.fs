namespace Fuaran.Program.Server.UI

// ============================================================================
//  The server placement's pre-Phase-1896 names, at the UI witness.
//
//  Part of the `Fuaran.Program.Server.UI` adapter package (Phase 1896 wrote
//  it, Phase 1897 made it a package). As in the client-side
//  aliases: open this namespace AFTER `Fuaran.Program.Server`, because a module
//  here and a core module of the same name are searched latest-opened first.
// ============================================================================

open Fuaran.Core
open Fuaran.Compute
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Renderer.BindingResolver
open Fuaran.UI.ServerDriven
open Fuaran.UI.OpStream.Abstractions
open Fuaran.Program.Bounded
open Fuaran.Program.Server
open Fuaran.Program.UI

type ServerStore = Fuaran.Program.Server.ServerStore<Node<obj>, BindingSources>

type HandlerStage = Fuaran.Program.Server.HandlerStage<Action<obj>, TreeOp<obj>>

type Handler = Fuaran.Program.Server.Handler<Action<obj>, TreeOp<obj>>

type HandlerOutcome = Fuaran.Program.Server.HandlerOutcome<Node<obj>, BindingSources, TreeOp<obj>, ClientEffect>

type HandlerTally = Fuaran.Program.Server.HandlerTally<Node<obj>, TreeOp<obj>>

type HandlerReport = Fuaran.Program.Server.HandlerReport<TreeOp<obj>>

type ServerServices =
    Fuaran.Program.Server.ServerServices<
        Node<obj>,
        Action<obj>,
        Binding<JVal>,
        BindingSources,
        TreeOp<obj>,
        ClientEffect
     >

type ServerSession =
    Fuaran.Program.Server.ServerSession<Node<obj>, Action<obj>, Binding<JVal>, BindingSources, TreeOp<obj>, ClientEffect>

type ServerReject = Fuaran.Program.Server.ServerReject<Validation.RejectReason>

type ServerStepOutput =
    Fuaran.Program.Server.ServerStepOutput<Node<obj>, TreeOp<obj>, ClientEffect, Validation.RejectReason>

type DurableOutcome = Fuaran.Program.Server.DurableOutcome<Node<obj>, BindingSources, TreeOp<obj>, ClientEffect>

type ControlledOutcome = Fuaran.Program.Server.ControlledOutcome<Node<obj>, BindingSources, TreeOp<obj>, ClientEffect>

type ControlledStep =
    Fuaran.Program.Server.ControlledStep<
        Node<obj>,
        Action<obj>,
        Binding<JVal>,
        BindingSources,
        TreeOp<obj>,
        ClientEffect,
        Validation.RejectReason
     >

module Handler =

    let run
        (registry: ServerEffectRegistry)
        (resolve: string -> Result<Table, EvalError>)
        (nodeId: string)
        (handler: Handler)
        (store: ServerStore)
        : HandlerOutcome =
        Fuaran.Program.Server.Handler.run UiWitness.witness registry resolve nodeId handler store

module HandlerWire =

    let encodeEffect (effect: ServerEffect<TreeOp<obj>>) : Result<JVal, WireRefusal> =
        Fuaran.Program.Server.HandlerWire.encodeEffect UiWitness.witness effect

    let decodeEffect (value: JVal) : Result<ServerEffect<TreeOp<obj>>, WireRefusal> =
        Fuaran.Program.Server.HandlerWire.decodeEffect UiWitness.witness value

    let encodeStage (stage: HandlerStage) : Result<JVal, WireRefusal> =
        Fuaran.Program.Server.HandlerWire.encodeStage UiWitness.witness stage

    let decodeStage (value: JVal) : Result<HandlerStage, WireRefusal> =
        Fuaran.Program.Server.HandlerWire.decodeStage UiWitness.witness value

    let encodeHandlerJson (handler: Handler) : Result<JVal, WireRefusal> =
        Fuaran.Program.Server.HandlerWire.encodeHandlerJson UiWitness.witness handler

    let encodeHandler (handler: Handler) : Result<string, WireRefusal> =
        Fuaran.Program.Server.HandlerWire.encodeHandler UiWitness.witness handler

    let decodeHandlerJson (value: JVal) : Result<Handler, WireRefusal> =
        Fuaran.Program.Server.HandlerWire.decodeHandlerJson UiWitness.witness value

    let decodeHandler (json: string) : Result<Handler, WireRefusal> =
        Fuaran.Program.Server.HandlerWire.decodeHandler UiWitness.witness json

    let replayReasons (handler: Handler) : ReplayReason list =
        Fuaran.Program.Server.HandlerWire.replayReasons UiWitness.witness handler

    let replaySafety (handler: Handler) : ReplaySafety =
        Fuaran.Program.Server.HandlerWire.replaySafety UiWitness.witness handler

    let encodeReportJson (report: HandlerReport) : Result<JVal, WireRefusal> =
        Fuaran.Program.Server.HandlerWire.encodeReportJson UiWitness.witness report

    let encodeReport (report: HandlerReport) : Result<string, WireRefusal> =
        Fuaran.Program.Server.HandlerWire.encodeReport UiWitness.witness report

    let decodeReportJson (value: JVal) : Result<HandlerReport, WireRefusal> =
        Fuaran.Program.Server.HandlerWire.decodeReportJson UiWitness.witness value

    let decodeReport (json: string) : Result<HandlerReport, WireRefusal> =
        Fuaran.Program.Server.HandlerWire.decodeReport UiWitness.witness json

module ServerServices =

    let create: ServerServices =
        Fuaran.Program.Server.ServerServices.create UiWitness.witness

    let createPermissive: ServerServices =
        Fuaran.Program.Server.ServerServices.createPermissive UiWitness.witness

module ServerDemanded =

    let ofHandler (handler: Handler) : DemandedProjection =
        Fuaran.Program.Server.ServerDemanded.ofHandler UiWitness.witness handler

    let ofHandlers (handlers: Handler seq) : DemandedProjection =
        Fuaran.Program.Server.ServerDemanded.ofHandlers UiWitness.witness handlers

    let reachable (handlers: Map<string, Handler>) (root: Node<obj>) : Handler list =
        Fuaran.Program.Server.ServerDemanded.reachable UiWitness.witness handlers root

    let ofTreeAndHandlers (handlers: Map<string, Handler>) (root: Node<obj>) : DemandedProjection =
        Fuaran.Program.Server.ServerDemanded.ofTreeAndHandlers UiWitness.witness handlers root

    let ofTreeHandlersAndRegistry
        (registry: ServerEffectRegistry)
        (handlers: Map<string, Handler>)
        (root: Node<obj>)
        : DemandedProjection =
        Fuaran.Program.Server.ServerDemanded.ofTreeHandlersAndRegistry UiWitness.witness registry handlers root

    let sign
        (sink: IAttestationSink)
        (handlers: Map<string, Handler>)
        (root: Node<obj>)
        : Result<SignedEnvelope, SignRefusal> =
        Fuaran.Program.Server.ServerDemanded.sign UiWitness.witness sink handlers root

    let verify
        (crypto: IClaimSignatureVerifier)
        (key: KeyDirectoryEntry option)
        (handlers: Map<string, Handler>)
        (root: Node<obj>)
        (signed: SignedEnvelope)
        : Async<Result<VerifiedEnvelope, VerifyRefusal>> =
        Fuaran.Program.Server.ServerDemanded.verify
            UiWitness.witness
            (UiWitness.claimVerifier crypto)
            key
            handlers
            root
            signed

    let signWithRegistry
        (sink: IAttestationSink)
        (registry: ServerEffectRegistry)
        (handlers: Map<string, Handler>)
        (root: Node<obj>)
        : Result<SignedEnvelope, SignRefusal> =
        Fuaran.Program.Server.ServerDemanded.signWithRegistry UiWitness.witness sink registry handlers root

    let verifyWithRegistry
        (crypto: IClaimSignatureVerifier)
        (key: KeyDirectoryEntry option)
        (registry: ServerEffectRegistry)
        (handlers: Map<string, Handler>)
        (root: Node<obj>)
        (signed: SignedEnvelope)
        : Async<Result<VerifiedEnvelope, VerifyRefusal>> =
        Fuaran.Program.Server.ServerDemanded.verifyWithRegistry
            UiWitness.witness
            (UiWitness.claimVerifier crypto)
            key
            registry
            handlers
            root
            signed

module Replay =

    let admit (mode: ReplayMode) (policy: ReplayPolicy) (handler: Handler) : ReplayDecision =
        Fuaran.Program.Server.Replay.admit UiWitness.witness mode policy handler

    let admitAll (mode: ReplayMode) (policy: ReplayPolicy) (handlers: Handler seq) : (string * ReplayDecision) list =
        Fuaran.Program.Server.Replay.admitAll UiWitness.witness mode policy handlers

    let postureOf (handler: Handler) : ReplayPosture =
        Fuaran.Program.Server.Replay.postureOf UiWitness.witness handler

    let withPostures (handlers: Handler seq) (projection: DemandedProjection) : DemandedProjection =
        Fuaran.Program.Server.Replay.withPostures UiWitness.witness handlers projection

    let ofTreeAndHandlers (handlers: Map<string, Handler>) (root: Node<obj>) : DemandedProjection =
        Fuaran.Program.Server.Replay.ofTreeAndHandlers UiWitness.witness handlers root

module Harvest =

    let ofProgram (handlers: Map<string, Handler>) (root: Node<obj>) : HarvestedDemand =
        Fuaran.Program.Server.Harvest.ofProgram UiWitness.witness handlers root

    let ofRegistration (handlers: Handler seq) : HarvestedDemand =
        Fuaran.Program.Server.Harvest.ofRegistration UiWitness.witness handlers
