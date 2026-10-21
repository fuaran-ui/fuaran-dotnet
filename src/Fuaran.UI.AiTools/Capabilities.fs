module Fuaran.UI.AiTools.Capabilities

// ============================================================================
//  Phase 283 — the capability discovery + dispatch surface (the Compute layer's
//  hard-stuff seam). The compute analogue of the ai-tools node-introspection:
//  an agent enumerates "what compute may I invoke, with what typed args" exactly
//  as it enumerates what nodes it may emit.
//
//  The registry, arg-validation (default-deny by shape, FGP 3), the invocation
//  contract, and the Phase-27 replay laws all live in `Fuaran.Core` (the
//  `Capability` / `Registry` surface, certified by `capabilityLaws`). This module
//  is the thin host-side glue that (1) surfaces the registry for discovery and
//  (2) builds the renderer's `BindingResolver.CapabilityInvoker` from a registry
//  + a host-body resolver, mapping the outcome to the `Deferred` async envelope
//  that renders through the existing `StateBehaviour` surface. Phase 2311: the invoker
//  dispatches through the registry's own `dispatch`, so its policy gate runs.
// ============================================================================

open Fuaran.UI.Types

/// Core's settled/pending/failed envelope, qualified: `Failed` alone also names a resolver fault.
type private CoreDeferred = Fuaran.Core.Deferred<obj>

/// Enumerate the registry for discovery: each capability's id paired with the JSON-Schema
/// projection of its `Signature` (the typed args an agent may pass), in the registry's stable id
/// order. This is the compute analogue of node-introspection — an agent reads it to learn the
/// invokable surface, just as it reads the node schema to learn the emittable surface.
let discover (registry: Fuaran.Core.CapabilityRegistry) : (string * Fuaran.Core.JVal) list =
    Fuaran.Core.CapabilityRegistry.enumerate registry
    |> List.map (fun cap -> cap.Id, Fuaran.Core.Function.toJsonSchema cap.Signature)

/// The host's invocation path: dispatch through the registry's own `CapabilityRegistry.dispatch`, so
/// the registry's whole admission runs before the body does — the id resolves (default-deny), the
/// arguments validate, and then the registry's POLICY gates decide (a `Deny` is `PolicyRefused`, a
/// `NeedsApproval` is `ApprovalRequired`, and either reaches every `onDenied` observer). The
/// host `body` runs only past all of that, and its `Deferred` is settled by the registry (a body
/// failure is the typed `BodyFailed`). This is the path the `capabilityLawsAt` family certifies
/// (`CapabilitySeamWitness.Dispatch`), kept as the one function `makeInvoker` and the witness share.
let invoke
    (registry: Fuaran.Core.CapabilityRegistry)
    (capabilityId: string)
    (args: (string * string) list)
    (body: Fuaran.Core.Capability -> (string * string) list -> Deferred<obj>)
    : Result<Fuaran.Core.Deferred<obj>, Fuaran.Core.InvokeError> =
    Fuaran.Core.CapabilityRegistry.dispatch registry capabilityId args (fun cap () ->
        match body cap args with
        | Deferred.Pending -> CoreDeferred.Pending
        | Deferred.Ready v -> CoreDeferred.Ready v
        | Deferred.Error m -> CoreDeferred.Failed m)

/// Build a `BindingResolver.CapabilityInvoker`-shaped function (`id -> args -> Deferred<obj>`) from
/// the registry + a host-body resolver. The call goes through `invoke`, so a refusal — an unknown id,
/// an ill-typed argument, or the registry's policy denying or parking it — is a `Deferred.Error`
/// carrying `InvokeError.describe`'s sentence (rendered via the node's `onError`) and the body never
/// runs; otherwise the host body's result maps to `Deferred` (`Ready` → the value, `Error` → the
/// body's own message → `onError`). A non-`Deterministic` capability's realized value
/// should be journaled by the host through the Phase-27 `OpStream.captureEffect` seam keyed by
/// `Fuaran.Core.Capability.invocationKey` (so the invocation replays exactly — `capabilityLaws`
/// certifies this); a genuinely-async host returns `Deferred.Pending` from its own body resolver
/// until the realized value arrives, surfacing the node's `onLoading` subtree meanwhile.
let makeInvoker
    (registry: Fuaran.Core.CapabilityRegistry)
    (body: Fuaran.Core.Capability -> (string * string) list -> Deferred<obj>)
    : string -> (string * string) list -> Deferred<obj> =
    fun capabilityId args ->
        match invoke registry capabilityId args body with
        | Ok(CoreDeferred.Ready v) -> Deferred.Ready v
        | Ok CoreDeferred.Pending -> Deferred.Pending
        | Ok(CoreDeferred.Failed m)
        | Error(Fuaran.Core.BodyFailed m) -> Deferred.Error m
        | Error e -> Deferred.Error(Fuaran.Core.InvokeError.describe e)

/// The host's active EMISSION budget as a capability report (Phase 1817), so a model can ASK how much
/// tree this host takes before it emits, rather than learn it by refusal.
///
/// Shape: `{"identity": <declaration name>, "limits": [{"limit": "maxNodes", "value": 400,
/// "code": "FUARAN158"}, …]}` — one entry per DECLARED limit, in `HostLimits` record order; a host
/// that declares none reports an empty list, which means "no stated limit", not "zero". Each entry
/// names the code a breach of it is refused with, projected through `PreEmitValidate.describe` so
/// the report and the refusal cannot disagree.
///
/// Wire-neutral: this is the HOST describing itself to an emitter through the tools surface, the
/// same standing as the capability registry above. Nothing here travels in the document, and the
/// decode-side `WireLimits` are unaffected by whatever a host declares.
let emissionLimits (limits: Fuaran.UI.HostLimits) : Fuaran.Core.JVal =
    let entries =
        Fuaran.UI.HostLimits.declared limits
        |> List.map (fun (kind, value) ->
            Fuaran.Core.JObj
                [ "limit", Fuaran.Core.JStr(Fuaran.UI.HostLimits.name kind)
                  "value", Fuaran.Core.JInt value
                  "code", Fuaran.Core.JStr(Fuaran.UI.PreEmitValidate.hostLimitCode kind) ])

    Fuaran.Core.JObj
        [ "identity", Fuaran.Core.JStr limits.Identity
          "limits", Fuaran.Core.JArr entries ]
