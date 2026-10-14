namespace Fuaran.UI.Renderer

open Fuaran.UI.Types

// ============================================================================
//  Fuaran — the render POLICY both renderers carry (Phase 2067).
//
//  Four of a render's settings decide what the emitted document may do rather
//  than what it shows: where its destinations may point, how strictly a
//  `Custom` node's content hash is verified, which Content-Security-Policy
//  posture its styles are emitted under, and which runtime scope it renders in.
//  The client `RenderContext` and the server `ServerRenderContext` both carry
//  them, with the same defaults and the same reached-by-name opt-outs, so they
//  are ONE record that both contexts hold as `Policy`. A setting added here
//  reaches both tiers at once, which is what keeps the two tiers' emission
//  parity-locked (`docs/SSR.md`) when a posture moves.
//
//  The field history (which phase introduced each setting, and why its default
//  is what it is) is recorded in `STABILITY.md` under 0.94.0, Phase 2067.
// ============================================================================

/// What a render may do, as opposed to what it shows. Held by both renderers'
/// contexts as `Policy`; `RenderPolicy.defaults` is the posture every builder
/// starts from.
type RenderPolicy =
    {
        /// The destination policy every emitted `href`, `src` and route is
        /// checked against; a refused destination renders as a refusal.
        Egress: Sanitize.EgressPolicy
        /// This render's own `Custom` content-hash floor, or `None` for the
        /// process floor; raise-only (`CustomHash.effectiveFloor` takes the
        /// stricter of the two).
        CustomHashFloor: HashStrictness option
        /// The Content-Security-Policy posture: `Permissive` emits inline
        /// styles as always, `Strict nonce` emits generated classes instead.
        Csp: Csp.CspMode
        /// The runtime scope this subtree renders under, or `None` for the root
        /// scope: on the client it isolates `Binding.State` and
        /// `Action.SetState`, on the server it constrains `Custom` lookup.
        Scope: string option
    }

/// Builders over `RenderPolicy`.
[<RequireQualifiedAccess>]
module RenderPolicy =

    /// The posture every builder starts from: default-deny egress
    /// (`Sanitize.denyNonLocalEgress`), the process hash floor, `Permissive`
    /// CSP and the root scope. Every wider or stricter posture is reached by
    /// name with `with`, so a grep finds each host that chose one.
    let defaults: RenderPolicy =
        { Egress = Sanitize.denyNonLocalEgress
          CustomHashFloor = None
          Csp = Csp.Permissive
          Scope = None }
