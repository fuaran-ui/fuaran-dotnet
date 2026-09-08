module Fuaran.UI.AiTools.Types

// ============================================================================
//  Fuaran runtime-introspection AI tools — typed payload shapes (§4i of
//  the Fuaran design specification).
//
//  Ships the four read-only introspection
//  tools — `getNodeState` / `getBindingValue` / `getRenderedDom` /
//  `getRuntimeErrors` — that let a downstream AI consumer observe what
//  the renderer did with its emitted tree.
//
//  Three encoding decisions worth recording:
//
//   (1) Typed payloads, JSON-rendered separately. The §4i contract example
//       shows snake_case JSON (`{"current_state": "Normal", ...}`); we keep
//       idiomatic PascalCase F# field names in this module and emit
//       snake_case at the `ResponseRender` boundary, same split
//       `Fuaran.UI.Ops` uses (`ApplyError` is PascalCase; `ErrorRender`
//       lowercases at the wire boundary).
//
//   (2) `ResolvedValue` is `obj`. The introspection surface is a
//       cross-cutting consumer of every NodeKind's binding-typed slots — a
//       Metric returns a `float`, a Stepper returns an `int`, a Grid returns
//       `obj seq`, a Map returns `MapMarker seq`. A single typed union
//       across all of them would need a case per Binding<'T> instantiation,
//       which doesn't generalise. The orchestrator decodes per-slot using
//       its schema knowledge (it knows the slot's expected `'T`). The
//       `TypeHint` field carries the F# typeof<'T>.Name string when
//       available (NULL under Fable's erased generics — same constraint
//       Renderer's BindingResolver works around).
//
//   (3) Expression strings are `"$queries.<name>"` for `Binding.Query`,
//       NOT `"$queries.<name>.<accessor-dot-path>"`. Recovering the
//       accessor's dot-path from the captured `obj -> 'T` closure requires
//       either reflection (server-only) or source-side metadata (a
//       manifest substrate). v1 ships the short form; closed-loop
//       orchestrator prompt context still narrows enough on schema +
//       structural assertions to disambiguate which field of which query
//       failed.
// ============================================================================

open System
open Fuaran.UI.Types

// ─── Include filter (§4i "five include keys") ──────────────────────────────

/// The five filters `getNodeState`'s `options.include` accepts. v1 supports
/// exact-key inclusion; an empty list defaults to "include all five". The
/// orchestrator polls cheaply with `[ IncludeKey.Props ]` and grabs a full
/// snapshot with `[]` when a failure surfaces.
[<RequireQualifiedAccess>]
type IncludeKey =
    | Props
    | Bindings
    | CurrentState
    | StateDetail
    | Geometry

// ─── Binding-resolution wire shape (§4i lines 1160–1175) ───────────────────

/// One resolved binding slot. `Expression` is the wire-shape canonical form
/// (`$queries.<name>` for Query, `$state.<key>` for State, `$filters.<name>`
/// for Filter, `$selection.<nodeId>` for Selection, `$static` for Static,
/// `$computed` for Computed); `ResolvedValue` is the typed payload boxed back
/// to obj at the cross-cutting introspection boundary.
type ResolvedBinding =
    {
        /// The slot's wire-form expression (per `Binding<'T>` case).
        Expression: string
        /// The resolved typed value, obj-boxed at the cross-cutting boundary.
        /// `None` when the binding resolved to a Defaults sentinel (e.g.
        /// `Binding.Query(NotProvidedSentinel, _)` → "no override yet"),
        /// distinct from `BindingError` which signals an actual failure.
        ResolvedValue: obj option
        /// Best-effort type hint — `typeof<'T>.Name` server-side, `None`
        /// under Fable (generics erased). The orchestrator's schema gives
        /// it the authoritative `'T`; this is for log-readability only.
        TypeHint: string option
        /// Source-of-truth tag — which `Binding` case the value came from.
        /// Lets the orchestrator know whether a missing value is "filter
        /// not set" vs "query pending" vs "state default applied".
        Source: BindingSource
    }

/// Discriminator that classifies which `Binding<'T>` case produced a
/// `ResolvedBinding`. Distinct from the wire `Expression` because the
/// orchestrator's error-recovery branches on case, not on string parsing.
and [<RequireQualifiedAccess>] BindingSource =
    | Static
    | Query of name: string
    | Filter of name: string
    | Selection of nodeId: NodeId
    | State of key: string
    | Computed
    /// Localised string binding (`Binding.I18n`). `key` is the
    /// i18n catalog key; resolved at runtime via `BindingSources.I18nResolver`.
    | I18n of key: string

/// Binding-resolution failure. `Expression` matches the `ResolvedBinding`
/// shape so the orchestrator can pattern-match on a single envelope; the
/// `Hint` block carries the §4d AI-recovery fields the orchestrator uses
/// to pick a recovery strategy.
type BindingError =
    {
        Expression: string
        Source: BindingSource
        /// Short stable identifier, parallel to `ApplyErrorCode`. The
        /// orchestrator branches on these.
        Code: BindingErrorCode
        /// Human / AI-readable failure message.
        Message: string
        /// §4d-shape hint payload. Same fields populated as `ApplyHint`
        /// (re-using the parallel `BindingResolutionHint` here rather than
        /// importing `Fuaran.UI.Ops.ApplyHint` to keep the binding-resolution
        /// envelope independent of the apply-engine error shape).
        Hint: BindingResolutionHint
    }

and [<RequireQualifiedAccess>] BindingErrorCode =
    /// The Query / Filter / Selection / State data source was not registered
    /// at all (Renderer's `BindingSources.QueryResults` had no entry for the
    /// key, etc.).
    | SourceUnregistered
    /// The data source is registered but the value has not arrived yet
    /// (`Query`'s entry pending, etc.). v1 collapses "registered + pending"
    /// and "registered + value = None" into the same case.
    | NotResolvedYet
    /// The accessor closure threw on the registered value (typed accessor
    /// expected `'a`, got something else).
    | AccessorThrew
    /// The Static / State default value did not unbox to the expected type
    /// at the renderer boundary.
    | TypeMismatch

and BindingResolutionHint =
    {
        /// Short kind name of the addressed node, populated when reachable.
        NodeKind: string option
        /// Suggested next action — "Wait for query to complete", "Check the
        /// filter store has a value for this key", "Verify the accessor's
        /// expected type matches the source value's shape", etc.
        Suggestion: string option
        /// Available alternatives the orchestrator could swap to — e.g.
        /// other binding slots on the same node, or the static-fallback
        /// shape the node's spec record allows.
        AvailableAlternatives: string list
    }

/// Top-level resolution outcome — either a value, or a typed failure.
/// Parallel to `BindingResolver.Resolution<'T>` in the renderer but with
/// the typed `'T` projected through the introspection boundary's `obj`
/// erasure.
[<RequireQualifiedAccess>]
type ResolvedBindingResult =
    | Resolved of ResolvedBinding
    | Failed of BindingError

// ─── Text provenance (Phase 1547) ──────────────────────────────────────────

/// Where a text value came from. Binding slots have carried a
/// `BindingSource` token since v1, so an agent can tell a declared value
/// from a resolved one; text slots did not, and a heading authored as a
/// literal was indistinguishable from one resolved out of a query result.
/// This is that mark, over the same vocabulary rather than a second one:
/// `Bound` carries the very `BindingSource` and wire expression
/// `BindingProbe.identify` already produces.
[<RequireQualifiedAccess>]
type TextProvenance =
    /// A string the tree's author wrote.
    | Literal
    /// A catalogue lookup. `key` is the i18n key; the resolved string is
    /// the host catalogue's, not the tree's.
    | I18n of key: string
    /// Text resolved from a binding. `source` is the binding-slot
    /// vocabulary's own token; `expression` is its canonical wire form
    /// (`$queries.<name>`, `$state.<key>`, …).
    | Bound of source: BindingSource * expression: string

module TextProvenance =
    /// Whether a consumer must treat the text as content the interface
    /// displays rather than as anything addressed to it.
    ///
    /// True for text resolved from a `Query`, `Selection`, `State` or
    /// `Computed` binding: every one of those reaches the tree from data
    /// the tree's author did not write, so its bytes are as
    /// attacker-influenced as the data behind them. Literal and i18n text
    /// carry no flag, because the author and the catalogue are the same trust
    /// domain as the tree itself. `Static` and `Filter` are likewise
    /// unflagged: a static default is authored, and a filter value is the
    /// operator's own selection from a bounded set the author declared.
    ///
    /// It is derived rather than stored so a consumer needs no table:
    /// reading the flag is enough to act on.
    let isUntrusted (provenance: TextProvenance) : bool =
        match provenance with
        | TextProvenance.Literal
        | TextProvenance.I18n _ -> false
        | TextProvenance.Bound(source, _) ->
            match source with
            | BindingSource.Query _
            | BindingSource.Selection _
            | BindingSource.State _
            | BindingSource.Computed -> true
            | BindingSource.Static
            | BindingSource.Filter _
            | BindingSource.I18n _ -> false

// ─── Current-state classification (§4i `currentState` line 1172) ───────────

/// What state the renderer believes the node is in. The four canonical
/// states map to the `StateBehaviour` slots: `Loading` ↔ `OnLoading`,
/// `Empty` ↔ `OnEmpty`, `Error` ↔ `OnError`, `Normal` for the happy
/// path. The orchestrator probes this to know whether its last
/// `applyOps` cleared a Loading or simply changed the spinner's shape.
[<RequireQualifiedAccess>]
type CurrentState =
    | Normal
    | Loading
    | Empty
    | Error

/// Free-form detail payload describing why the current state is what it
/// is — typically the `ErrorPayload` when in Error state, or a
/// pending-query name when in Loading. v1 ships as opaque text; future
/// versions may shape it per state class.
type StateDetail = { Detail: string }

// ─── Geometry (§4i lines 1174 + `getRenderedDom` lines 1125) ───────────────

/// A node's rendered geometry — the bounding box plus overflow flag.
/// `Overflowing = true` when the node's contents exceed the box (Grid
/// rows overflowing the panel, Stack overflowing on the cross-axis,
/// etc.). All values are in CSS pixels; coordinates are document-relative
/// (matches `Element.getBoundingClientRect` after window-scroll
/// compensation).
type Geometry =
    { X: float
      Y: float
      Width: float
      Height: float
      Overflowing: bool }

/// Recursive geometry tree — the addressed node + every layout-descendant
/// it carries. Childless kinds carry an empty `Children` list. The §4i
/// example shows a flat per-id lookup; the recursive shape preserves
/// containment relationships the orchestrator needs to compute
/// "child overflowing parent" patterns directly.
type GeometryTree =
    { Id: NodeId
      Kind: string
      Geometry: Geometry option
      Children: GeometryTree list }

// ─── Live-Transform sites (Phase 1615) ─────────────────────────────────────

/// Phase 1615 — one live-`Transform` site the addressed node holds: a slot
/// whose source is a `Binding.Transform` over a LIVE binding, so a write to
/// that binding's channel makes this reader recompute.
///
/// The question it answers is the one a host previously had to mirror the
/// analysis walk to ask: *when something writes `$state.k`, which readers
/// re-evaluate, and what do they run?* `Fuaran.UI.BindingWalk` has enumerated
/// this since Phase 1615; this record is that enumeration projected to the
/// introspection envelope, so an orchestrator reads it off `getNodeState`
/// rather than reimplementing a walk it cannot keep in step.
///
/// It carries no table. The site's SOURCE data is a runtime fact the walk sees
/// only as a decode-time snapshot, and reporting a snapshot as if it were the
/// live rows is exactly the false statement the language tier's schema rules
/// decline to make.
type LiveTransformSite =
    {
        /// The state key the source reads, when its channel is `$state`.
        /// `None` when the source reads another channel (a query, a
        /// selection) — such a site recomputes, but not on a state write.
        StateKey: string option
        /// The key a session-held live-`Transform` store keeps this site's
        /// primed state under. `None` when the walk declines it — a
        /// parameterised pipeline evaluates an effective form decided at
        /// render time, and a key derived from the pipeline as carried would
        /// name a site the store never sees.
        SiteKey: string option
        /// The reader's slot, when the node's own arm named one (`"source"`
        /// on a grid / chart / map row feed).
        Slot: string option
        /// The row-identity column the reader declares — a grid's
        /// `rowKeyField`. `None` on a reader that declares none.
        IdentityColumn: string option
        /// The pipeline's verbs in order, in their canonical wire spellings
        /// (`filter`, `groupBy`, …) — what this site recomputes.
        Pipeline: string list
    }

// ─── Top-level NodeState envelope (§4i lines 1145–1175) ────────────────────

/// The full per-node observable state envelope. Field-by-field optionality
/// matches `IncludeKey` filtering — when the caller asks for only `[Props]`
/// the other four blocks are `None`. `Id` and `Kind` are always populated.
///
/// Phase 1615 added `LiveTransforms`, gated on the EXISTING
/// `IncludeKey.Bindings` rather than on a sixth include key. A live-Transform
/// site is a fact about a binding slot, so it belongs in the binding block;
/// and the five include keys are §4i's own enumeration, which a language-tier
/// phase does not widen on its own authority.
type NodeState =
    {
        Id: NodeId
        Kind: string
        Props: PropEntry list option
        Bindings: Map<string, ResolvedBindingResult> option
        CurrentState: CurrentState option
        StateDetail: StateDetail option
        Geometry: Geometry option
        /// Phase 1615 — the live-`Transform` sites the addressed node's own spec
        /// holds, in walk order. Populated under `IncludeKey.Bindings`; an empty
        /// list means the node holds none, `None` means the caller did not ask.
        LiveTransforms: LiveTransformSite list option
    }

/// A single prop-block entry. `Value` is `obj`-boxed at the introspection
/// boundary; the orchestrator's schema knows the typed `'T` expected per
/// `(kind, name)` pair. The list ordering follows
/// `Fuaran.UI.Ops.Introspect.availableFields` so two introspect calls on
/// structurally-identical nodes produce identical PropEntry lists
/// (determinism contract).
and PropEntry =
    {
        Name: string
        Value: obj option
        /// Best-effort type hint, same constraint as `ResolvedBinding.TypeHint`.
        TypeHint: string option
        /// Phase 1547 — set on every text-valued entry (a slot whose spec
        /// field is a `TextSource`), `None` on every other prop. The
        /// value rendering is unchanged either way; this is the mark
        /// beside it.
        Provenance: TextProvenance option
    }

// ─── Runtime-error stream (§4i lines 1126 + line 1313) ─────────────────────

/// One entry on the renderer's runtime-error stream. The renderer writes
/// to an `IRuntimeErrorSink` (see `Seams.fs`); `getRuntimeErrors` drains
/// it with optional `sinceTurn` filtering.
type ErrorEntry =
    {
        /// Stable identifier the AI can quote when discussing the error
        /// (`"err-2026-05-24-13-00-00-abc"` shape). Monotonically increasing
        /// per-sink so `sinceTurn` ordering is well-defined.
        Id: string
        /// Turn the error was recorded against. `None` for errors recorded
        /// outside a debug session (orchestrator's `sinceTurn` filter still
        /// works: `Some _` strictly after `Some n`; `None` rows are filtered
        /// out of `sinceTurn`-filtered drains).
        TurnId: int option
        /// Short classifier: `"BindingResolution"`, `"UnwiredAction"`,
        /// `"ApplyError"`, etc. Stable per renderer release; not localised.
        Code: string
        /// Free-text message body — what the renderer wrote at the time.
        Message: string
        /// NodeId the error was attributed to, when known. The orchestrator
        /// uses this to correlate the error with a tree position.
        NodeId: NodeId option
        /// When the renderer recorded the entry, per the introspection
        /// clock seam. Default seam returns a fixed epoch so byte-stable
        /// snapshots are possible across replays.
        RecordedAt: DateTimeOffset
    }

// ─── Tool-level introspection error (§4d-shaped envelope) ──────────────────

/// Surfaces when a tool call cannot proceed — typically `NodeNotFound`.
/// Shape parallels `Fuaran.UI.Ops.Types.ApplyError`; `ResponseRender` emits
/// the same `{ "op": {...}, "error": {...} }` envelope keyed on the
/// tool name + arguments.
[<RequireQualifiedAccess>]
type IntrospectErrorCode =
    /// The addressed `NodeId` is not present anywhere in the tree.
    | NodeNotFound
    /// The addressed `slot` is not a Binding-typed slot on the node's kind.
    /// Hint enumerates the supported slot names.
    | SlotNotFound
    /// `IncludeKey` filter list contained a key the engine doesn't know.
    /// v1 cannot trigger this (the F# enum constrains it) but reserved
    /// for the wire-form ingest path follow-on.
    | UnknownIncludeKey
    /// Renderer-state probe (current-state / geometry) is not wired —
    /// the IGeometryProbe / IRuntimeErrorSink seam is the diagnostic
    /// default. Returned only for fields explicitly asked for via
    /// `IncludeKey` — `getNodeState` with no `include` filter omits the
    /// unwired field rather than erroring.
    | ProbeUnwired

type IntrospectErrorHint =
    { NodeKind: string option
      AvailableFields: string list
      Suggestion: string option }

type IntrospectError =
    { Code: IntrospectErrorCode
      Message: string
      Hint: IntrospectErrorHint }

module IntrospectErrorHint =
    let empty: IntrospectErrorHint =
        { NodeKind = None
          AvailableFields = []
          Suggestion = None }
