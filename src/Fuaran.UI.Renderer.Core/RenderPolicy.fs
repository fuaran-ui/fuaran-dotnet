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

/// Whether a pipeline-bound value offers its derivation trace (Phase 2749).
/// The affordance is host chrome, never wire: a document says nothing about it,
/// and with it `Off` every render is byte-identical to one without the setting.
[<RequireQualifiedAccess>]
type DerivationTracePolicy =
    /// No affordance. The default.
    | Off
    /// A `Metric` and a data-bound `DataGrid` whose binding carries a pipeline
    /// offer a disclosure that lists each step, its schema, and the table after
    /// it, at most `rowLimit` rows of each.
    | Show of rowLimit: int

/// Whether a live refresh's row delta marks what it changed (Phase 2751). Host
/// chrome, never wire, like the derivation affordance: with it `Off` every render
/// is byte-identical to one without the setting.
[<RequireQualifiedAccess>]
type RefreshMarkingPolicy =
    /// No marking. The default.
    | Off
    /// A data-bound `DataGrid` marks the cells its refresh's delta names and a
    /// `Metric` marks a value its refresh recomputed from named rows, for that
    /// one refresh, each beside a polite live region that says what changed.
    /// Only a refresh the incremental seam RESTRICTED carries a delta; a
    /// declined or full one marks nothing.
    | Mark

/// Whether a lowered chart offers its data-table alternative (Phase 2776). Host
/// chrome, never wire. Unlike the two settings above it is ON by default: the
/// table is the route to a chart's values for a reader who cannot see the
/// marks, and a host that supplies its own alternative turns it `Off`, which
/// renders byte-identically to a render without the setting.
[<RequireQualifiedAccess>]
type ChartTablePolicy =
    /// Every lowered chart whose rows resolved offers a "View as table"
    /// disclosure holding the values its marks were drawn from. The default.
    | Offer
    /// No table.
    | Off

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
        /// Whether a pipeline-bound `Metric` or grid offers its derivation
        /// trace (Phase 2749); `Off` emits nothing.
        DerivationTrace: DerivationTracePolicy
        /// Whether a live refresh marks the cells and values it changed
        /// (Phase 2751); `Off` emits nothing.
        RefreshMarking: RefreshMarkingPolicy
        /// Whether a lowered chart offers its data-table alternative
        /// (Phase 2776); `Off` emits nothing.
        ChartTable: ChartTablePolicy
    }

/// Builders over `RenderPolicy`.
[<RequireQualifiedAccess>]
module RenderPolicy =

    /// The posture every builder starts from: default-deny egress
    /// (`Sanitize.denyNonLocalEgress`), the process hash floor, `Permissive`
    /// CSP, the root scope, no derivation affordance, no refresh marking, and a
    /// chart's data table offered. Every wider or stricter posture is reached by
    /// name with `with`, so a grep finds each host that chose one.
    let defaults: RenderPolicy =
        { Egress = Sanitize.denyNonLocalEgress
          CustomHashFloor = None
          Csp = Csp.Permissive
          Scope = None
          DerivationTrace = DerivationTracePolicy.Off
          RefreshMarking = RefreshMarkingPolicy.Off
          ChartTable = ChartTablePolicy.Offer }
