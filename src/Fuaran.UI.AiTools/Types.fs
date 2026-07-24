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

// ─── Top-level NodeState envelope (§4i lines 1145–1175) ────────────────────

/// The full per-node observable state envelope. Field-by-field optionality
/// matches `IncludeKey` filtering — when the caller asks for only `[Props]`
/// the other four blocks are `None`. `Id` and `Kind` are always populated.
type NodeState =
    { Id: NodeId
      Kind: string
      Props: PropEntry list option
      Bindings: Map<string, ResolvedBindingResult> option
      CurrentState: CurrentState option
      StateDetail: StateDetail option
      Geometry: Geometry option }

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
