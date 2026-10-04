namespace Fuaran.UI.Program

// ============================================================================
//  The pre-Phase-1896 names, at the UI witness.
//
//  Part of the `Fuaran.UI.Program` adapter package (Phase 1896 wrote it, Phase
//  1897 made it a package). docs/generic-tier.md §5.1: the adapter keeps the old names
//  as closed aliases and partially applied modules, so a consumer migrates by
//  changing a package reference and adding one `open` — AFTER
//  `open Fuaran.Program.Bounded`, because a module here and a core module of
//  the same name are searched latest-opened first, and the names below are the
//  core's own names applied to the UI witness.
// ============================================================================

open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Renderer.BindingResolver
open Fuaran.UI.ServerDriven
open Fuaran.UI.OpStream.Abstractions
open Fuaran.Program.Bounded

/// The per-connection bounded store — the UI tier's `BindingSources`.
type BoundedStore = BindingSources

type BoundedOutcome = Fuaran.Program.Bounded.BoundedOutcome<BindingSources, ClientEffect>

type HandlerAnswer<'Placement> = Fuaran.Program.Bounded.HandlerAnswer<BindingSources, ClientEffect, 'Placement>

type HandlerArm<'Placement> = Fuaran.Program.Bounded.HandlerArm<BindingSources, ClientEffect, 'Placement>

module BoundedActions =

    /// `BoundedActions.run` at the UI witness.
    let runBoundedActionWith
        (arm: HandlerArm<'Placement>)
        (nodeId: string)
        (action: Action<obj>)
        (s: BindingSources)
        (placement: 'Placement)
        : BoundedOutcome * 'Placement =
        Fuaran.Program.Bounded.BoundedActions.run UiWitness.witness arm nodeId action s placement

    /// `BoundedActions.runInert` at the UI witness.
    let runBoundedAction (nodeId: string) (action: Action<obj>) (s: BindingSources) : BoundedOutcome =
        Fuaran.Program.Bounded.BoundedActions.runInert UiWitness.witness nodeId action s

module Budget =

    let actionCascadeCost (a: Action<obj>) : int =
        Fuaran.Program.Bounded.Budget.actionCascadeCost UiWitness.witness a

    let treeCost (ceiling: int) (node: Node<obj>) : int =
        Fuaran.Program.Bounded.Budget.treeCost UiWitness.witness ceiling node

module Resolve =

    let resolveTree (sources: BindingSources) (node: Node<obj>) : Node<obj> =
        Fuaran.Program.Bounded.Resolve.resolveTree UiWitness.witness sources node

module QuerySchema =

    let readersOfTree (root: Node<obj>) : QueryReader list =
        Fuaran.Program.Bounded.QuerySchema.readersOfTree UiWitness.witness root

module Demanded =

    let ofAction (action: Action<obj>) : DemandedProjection =
        Fuaran.Program.Bounded.Demanded.ofAction UiWitness.witness action

    let ofTree (root: Node<obj>) : DemandedProjection =
        Fuaran.Program.Bounded.Demanded.ofTree UiWitness.witness root

    let check (coverage: HostCoverage) (tree: Node<obj>) : CoverageFinding list =
        Fuaran.Program.Bounded.Demanded.check UiWitness.witness coverage tree

module ProgramWire =

    let encodeAction (action: Action<obj>) : JVal =
        Fuaran.Program.Bounded.ProgramWire.encodeAction UiWitness.witness action

    let decodeAction (value: JVal) : Result<Action<obj>, WireRefusal> =
        Fuaran.Program.Bounded.ProgramWire.decodeAction UiWitness.witness value

    let encodeOp (op: TreeOp<obj>) : Result<JVal, WireRefusal> =
        Fuaran.Program.Bounded.ProgramWire.encodeOp UiWitness.witness op

    let decodeOp (value: JVal) : Result<TreeOp<obj>, WireRefusal> =
        Fuaran.Program.Bounded.ProgramWire.decodeOp UiWitness.witness value

    let encodeClientEffect (effect: ClientEffect) : string =
        Fuaran.Program.Bounded.ProgramWire.encodeClientEffect UiWitness.witness effect

    let decodeClientEffect (value: JVal) : Result<ClientEffect, WireRefusal> =
        Fuaran.Program.Bounded.ProgramWire.decodeClientEffect UiWitness.witness value

module SignedEnvelope =

    let treeHash (root: Node<obj>) : string =
        Fuaran.Program.Bounded.SignedEnvelope.treeHash UiWitness.state root

    let sign
        (sink: IAttestationSink)
        (project: Node<obj> -> DemandedProjection)
        (root: Node<obj>)
        : Result<SignedEnvelope, SignRefusal> =
        Fuaran.Program.Bounded.SignedEnvelope.sign UiWitness.state sink project root

    let verify
        (crypto: IClaimSignatureVerifier)
        (key: KeyDirectoryEntry option)
        (project: Node<obj> -> DemandedProjection)
        (root: Node<obj>)
        (signed: SignedEnvelope)
        : Async<Result<VerifiedEnvelope, VerifyRefusal>> =
        Fuaran.Program.Bounded.SignedEnvelope.verify
            UiWitness.state
            (UiWitness.claimVerifier crypto)
            key
            project
            root
            signed

/// The client effect registry at the UI tier's effects.
type EffectRegistry = Fuaran.Program.Runtime.EffectRegistry<ClientEffect>

module EffectRegistry =

    /// The registry that performs nothing — `EffectRegistry.denyAll` at the UI
    /// tier's effects.
    let denyAll: EffectRegistry = Fuaran.Program.Runtime.EffectRegistry.denyAll

    let decide (registry: EffectRegistry) (effect: ClientEffect) : Fuaran.Program.Runtime.EffectDenial option =
        Fuaran.Program.Runtime.EffectRegistry.decide UiWitness.effects registry effect

    let perform (registry: EffectRegistry) (effect: ClientEffect) : unit =
        Fuaran.Program.Runtime.EffectRegistry.perform UiWitness.effects registry effect

    let performAll (registry: EffectRegistry) (effects: ClientEffect list) : Fuaran.Program.Runtime.EffectDenial list =
        Fuaran.Program.Runtime.EffectRegistry.performAll UiWitness.effects registry effects
