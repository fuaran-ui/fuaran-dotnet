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
//  that renders through the existing `StateBehaviour` surface.
// ============================================================================

open Fuaran.UI.Types

/// Enumerate the registry for discovery: each capability's id paired with the JSON-Schema
/// projection of its `Signature` (the typed args an agent may pass), in the registry's stable id
/// order. This is the compute analogue of node-introspection — an agent reads it to learn the
/// invokable surface, just as it reads the node schema to learn the emittable surface.
let discover (registry: Fuaran.Core.CapabilityRegistry) : (string * Fuaran.Core.JVal) list =
    Fuaran.Core.Registry.enumerate registry
    |> List.map (fun cap -> cap.Id, Fuaran.Core.Function.toJsonSchema cap.Signature)

/// Validate a typed invocation against the registry (default-deny by shape, FGP 3): resolve the
/// `capabilityId` (an unregistered id is `NoSuchCapability`) and validate the scalar `(addr, value)`
/// args against the capability's `Signature` (an ill-typed arg is a named `InvokeError`, never a
/// throw). Re-exposes the `Fuaran.Core` contract; the conformance is `capabilityLaws`.
let validate
    (registry: Fuaran.Core.CapabilityRegistry)
    (capabilityId: string)
    (args: (string * string) list)
    : Result<Fuaran.Core.Capability, Fuaran.Core.InvokeError> =
    match Fuaran.Core.Registry.tryFind capabilityId registry with
    | None ->
        Error(Fuaran.Core.NoSuchCapability(capabilityId, Fuaran.Core.Registry.enumerate registry |> List.map _.Id))
    | Some cap -> Fuaran.Core.Capability.validateArgs cap args |> Result.map (fun () -> cap)

/// Build a `BindingResolver.CapabilityInvoker`-shaped function (`id -> args -> Deferred<obj>`) from
/// the registry + a host-body resolver. Validates the invocation (a mismatch → `Deferred.Error`,
/// rendered via the node's `onError`), then runs the host body and maps its result to `Deferred`
/// (`Ready` → the value, `Error` → `onError`). A non-`Deterministic` capability's realized value
/// should be journaled by the host through the Phase-27 `OpStream.captureEffect` seam keyed by
/// `Fuaran.Core.Capability.invocationKey` (so the invocation replays exactly — `capabilityLaws`
/// certifies this); a genuinely-async host returns `Deferred.Pending` from its own body resolver
/// until the realized value arrives, surfacing the node's `onLoading` subtree meanwhile.
let makeInvoker
    (registry: Fuaran.Core.CapabilityRegistry)
    (body: Fuaran.Core.Capability -> (string * string) list -> Deferred<obj>)
    : string -> (string * string) list -> Deferred<obj> =
    fun capabilityId args ->
        match validate registry capabilityId args with
        | Error e -> Deferred.Error(sprintf "capability invocation rejected: %A" e)
        | Ok cap -> body cap args

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
