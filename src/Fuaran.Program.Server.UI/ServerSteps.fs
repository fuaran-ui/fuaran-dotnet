namespace Fuaran.Program.Server.UI

// ============================================================================
//  The server placement's UI EVENT STEP — the part of the session that is the
//  UI tier's transport rather than the algebra (K8, docs/generic-tier.md §4
//  part (c)).
//
//  An inbound `LiveEvent` meets the UI tier's G1 gate (`Validation.validate`);
//  what it admits is an already-chosen action, which the generic core's
//  `ServerSession.dispatchWith` runs through the G2 budget, the shared fold,
//  the handler commit, re-resolution and the diff. So this file holds the gate
//  and nothing after it, and a WireTree is reified here before the core ever
//  sees a tree.
//
//  Part of the `Fuaran.Program.Server.UI` adapter package (Phase 1896 wrote
//  it, Phase 1897 made it a package).
// ============================================================================

open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.Renderer.BindingResolver
open Fuaran.Program.Bounded
open Fuaran.Program.Server
open Fuaran.Program.UI

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ServerSession =

    /// Build a session from a decoded `WireTree` + its initial store.
    let init (services: ServerServices) (store: BindingSources) (wire: WireTree) : ServerSession =
        Fuaran.Program.Server.ServerSession.init services store (WireTree.reify wire)

    /// The static query-schema report for a decoded tree.
    let querySchemaReport (services: ServerServices) (wire: WireTree) : QuerySchemaReport =
        Fuaran.Program.Server.ServerSession.querySchemaReport services (WireTree.reify wire)

    /// Build a session only when the host covers everything the tree and its
    /// reachable handlers can ask for.
    let initStrict
        (coverage: HostCoverage)
        (services: ServerServices)
        (store: BindingSources)
        (wire: WireTree)
        : Result<ServerSession, ServerStrictFinding list> =
        Fuaran.Program.Server.ServerSession.initStrict coverage services store (WireTree.reify wire)

    /// Step the session with one untrusted inbound event, with `arm` deciding
    /// what a call action MEANS here.
    ///
    /// On a G1 rejection or a G2 budget breach the session is returned UNCHANGED
    /// with `Rejected = Some _` — default-deny by shape, no hang, no partial
    /// state, exactly as at the other placements.
    let stepWith
        (arm: HandlerArm<HandlerTally>)
        (session: ServerSession)
        (ev: LiveEvent)
        : ServerSession * ServerStepOutput =
        match Validation.validate session.Services.CanDispatch session.Resolved ev with
        | Error reason ->
            session,
            Fuaran.Program.Server.ServerSession.rejected session (Fuaran.Program.Server.ServerReject.Gate reason)
        | Ok { Action = None } -> session, Fuaran.Program.Server.ServerSession.inert session
        | Ok { Action = Some action } -> Fuaran.Program.Server.ServerSession.dispatchWith arm session ev.NodeId action

    /// `stepWith` at the plain handler arm.
    let step (session: ServerSession) (ev: LiveEvent) : ServerSession * ServerStepOutput =
        stepWith (Fuaran.Program.Server.ServerSession.directArm session.Services) session ev

module Durable =

    let run
        (services: DurableServices)
        (invocation: string)
        (registry: ServerEffectRegistry)
        (resolve: string -> Result<Fuaran.Core.Table, Fuaran.Compute.EvalError>)
        (nodeId: string)
        (handler: Handler)
        (store: ServerStore)
        : DurableOutcome =
        Fuaran.Program.Server.Durable.run UiWitness.witness services invocation registry resolve nodeId handler store

    /// Step a server session with the durable interpreter behind its call
    /// actions, through the UI tier's event step.
    let step
        (services: DurableServices)
        (invocation: string)
        (session: ServerSession)
        (ev: LiveEvent)
        : ServerSession * ServerStepOutput =
        Fuaran.Program.Server.Durable.stepVia (fun arm s -> ServerSession.stepWith arm s ev) services invocation session

module DurableControls =

    let run
        (services: DurableServices)
        (controls: ControlServices)
        (invocation: string)
        (registry: ServerEffectRegistry)
        (resolve: string -> Result<Fuaran.Core.Table, Fuaran.Compute.EvalError>)
        (nodeId: string)
        (handler: Handler)
        (store: ServerStore)
        : ControlledOutcome =
        Fuaran.Program.Server.DurableControls.run
            UiWitness.witness
            services
            controls
            invocation
            registry
            resolve
            nodeId
            handler
            store

    /// Step a server session with the controls in force, through the UI tier's
    /// event step. A suspended session refuses the dispatch through its own G1
    /// gate and leaves the session value untouched.
    let step
        (services: DurableServices)
        (controls: ControlServices)
        (invocation: string)
        (session: ServerSession)
        (ev: LiveEvent)
        : ControlledStep =
        Fuaran.Program.Server.DurableControls.stepVia
            (fun arm s -> ServerSession.stepWith arm s ev)
            services
            controls
            invocation
            session
