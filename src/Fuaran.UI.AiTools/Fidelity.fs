module Fuaran.UI.AiTools.Fidelity

// ============================================================================
//  Phase 1591 — the kind-intrinsic ARIA question, on the AI-tools surface.
//
//  An emitter authoring a node has one accessibility decision to make: does
//  this node need an `Accessibility` trait? Half the answer was already
//  queryable — what a node DECLARES is the trait itself. The other half was
//  prose: `Toast` is emitted with `role="status"` and `aria-live="polite"`
//  whatever the trait says, and the only place that was written down was the
//  render-fidelity table's `Fallback` sentence. An agent that could not read
//  that sentence either authored a trait it did not need, or mirrored the
//  render arms — the hand-kept copy this tier has watched go stale three times.
//
//  These are QUERIES over the shipped `Fuaran.UI.RenderFidelity` declaration,
//  not a second copy of it: no table lives here, and every answer is projected
//  from `RenderFidelity.all`. That is the same posture the module beside this
//  one takes over the capability registry — thin host-side glue, with the
//  declaration owned upstream. A DTO of our own would be exactly the second
//  source of truth the declaration exists to remove.
//
//  Read-only and idempotent, like the other tools in this package: the answers
//  are a function of the shipped table alone and depend on no context, no
//  clock and no rendered tree.
// ============================================================================

open Fuaran.UI.RenderFidelity

/// Every kind-intrinsic emission the shipped renderers pin, paired with the
/// kind that pins it, in table order.
let all: (string * IntrinsicAria) list = allIntrinsics

/// The kinds that pin something, in table order — the shortlist an emitter
/// checks its node against.
let kinds: string list = announcedKinds

/// What the renderer announces for one wire kind (`kind.$type`) of its own
/// accord.
///
/// `[]` for a kind that pins nothing, AND for a kind the table does not carry.
/// The two are different facts and this function does not distinguish them:
/// ask `Fuaran.UI.RenderFidelity.tryFind` when the difference matters (an
/// unknown kind is `None` there, a silent one is a row with an empty list).
let forKind (wireKind: string) : IntrinsicAria list =
    match tryFind wireKind with
    | Some row -> row.Intrinsic
    | None -> []

/// The intrinsic emissions for a node's own kind.
let ofNode (node: Fuaran.UI.Types.Node<'Msg>) : IntrinsicAria list = forKind (wireNameOf node.Kind)

/// Is this kind announced whatever the trait says?
///
/// The question an emitter asks before authoring an `Accessibility` trait it
/// does not need. `true` does NOT mean a trait is forbidden — a `label` on an
/// announced node is still the emitter's to supply, and only `role` /
/// `liveRegion` are the ones the renderer would be restating.
let isAnnounced (wireKind: string) : bool = announcesIntrinsically wireKind

/// Would an authored `role` on this kind restate something the renderer already
/// emits — and if so, which token?
///
/// The answer is per-ELEMENT, so a kind pinning three roles returns three: a
/// `Tabs` trait role competes with `tablist` on the bar and with nothing on the
/// panel, and collapsing that to one token would answer a question nobody
/// asked. Ordering follows the declaration.
let rolesEmitted (wireKind: string) : (string * string) list =
    forKind wireKind
    |> List.choose (fun a -> a.Role |> Option.map (fun r -> a.Element, r))

/// The live-region politeness this kind is already announced with, per element.
let liveRegionsEmitted (wireKind: string) : (string * Fuaran.UI.Types.LiveRegionKind) list =
    forKind wireKind
    |> List.choose (fun a -> a.Live |> Option.map (fun l -> a.Element, l))

/// The human-readable lines for one kind — the same sentence every surface that
/// renders an intrinsic emission shows, so an agent's explanation of its own
/// decision matches the artefact a reviewer reads.
let describe (wireKind: string) : string list =
    forKind wireKind |> List.map (describeIntrinsic wireKind)
