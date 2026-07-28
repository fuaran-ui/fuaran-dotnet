module Fuaran.UI.Types

open Fuaran.Core

// ============================================================================
//  Fuaran — canonical type contract (§4b of the Fuaran design specification, dated 2026-05-21)
//
//  Resolves the four flagged design
//  defects and lifts Children into per-LayoutKind spec records per the §4b
//  amendment (lines 467–468). Smart constructors + Defaults.X live in
//  Fuaran.fs / Defaults.fs — this file owns the type contract only.
//
//  DESIGN-DEFECT RESOLUTIONS (session 2)
//
//   (1) Row-encoding for GridSpec — resolved 2026-05-21 via "(c) runtime obj
//       boundary, typed at the smart-constructor layer". GridSpec<'Msg>'s
//       row-typed fields stay `obj`-typed at the tree level (Source =
//       Binding<obj seq>, RowKey = obj -> string, Columns = obj list,
//       OnRowClick = (obj -> Action<'Msg>) option). The typed surface is the
//       smart-ctor `Fuaran.grid<'row,'Msg>` which accepts a typed `GridSpec<'row,
//       'Msg>` facade record (`GridSpecOf<'row,'Msg>`) and boxes its accessors
//       internally. Authors never see `obj`. The renderer (session 3+) trusts
//       the per-Kind type-tag invariant. See §4c lines 504–542 — the author
//       surface is unchanged under this encoding. Fallback (a) IGridSpec
//       visitor remains in the back pocket if (c)'s invariant proves leaky.
//
//   (2) Free type vars in Action.Call / Binding.Query / Binding.Selection —
//       resolved 2026-05-21 via the same smart-ctor boundary. Wire-level
//       payloads stay `obj`-typed (Action.Call: obj -> 'Msg; Binding.Query:
//       obj -> 'T; Binding.Selection: obj -> 'T). Typed entry points
//       (`Action.call`, `binding.query`, `binding.selection`) take typed
//       accessor functions (`'a -> 'Msg`, `'result -> 'T`, `'row -> 'T`) and
//       wrap them in obj-erasing closures.
//
//   (3) Children-as-positional vs §4b amendment ("Children are a record
//       field") — resolved 2026-05-21 by lifting Children into per-LayoutKind
//       spec records. NodeKind.Layout is now of LayoutKind (single argument);
//       each LayoutKind case carries its own spec record with a typed
//       `Children: Node<'Msg> list` field. Seven layouts: Dashboard, Stack,
//       Grid, SplitPanel, Tabs, Card, Stepper. Tree-ops apply uniformly
//       (`UpdateProp(id, "Children", ...)` is the same op shape as any other
//       field update) per the amendment rationale.
//
//   (4) Not actually a defect — `[<RequireQualifiedAccess>]` per DU is the
//       right answer (matches §4c examples: Action.Dispatch, Binding.Query,
//       CellFormat.Currency). Kept; "defect" framing removed.
// ============================================================================

// ─── Placeholders for types §4b references but does not define inline ──────
//     Session 2 fleshes out the bodies the canonical seed needs (MetricSpec,
//     ButtonVariant, CalloutSpec, ProgressSpec). The rest stay as TODO
//     single-case placeholders until session 3+ wires the renderer.

// JSON-valued payloads (NodeKind.Custom props, CellKind.Custom, Action.Notify /
// SetState / AiTool, TextSource.I18n args) carry the structured `Fuaran.Core.JVal`
// AST — the same canonical wire model the whole substrate encodes and decodes.
// The model has no `null` (omit the field instead, per WIRE_FORMAT.md), so a
// null-valued payload is unrepresentable by construction, and nested objects /
// arrays round-trip faithfully through the canonical codec. `TreeOp.UpdateProp`'s
// value payload is NOT a JVal — it is the two-case `Fuaran.UI.Ops.Types.PropValue`
// (in-process `Native` vs canonical `Wire`); see the op contract.

type Orientation = Generated.Orientation
type IconSource = IconSource of string


type BadgeVariant = Generated.BadgeVariant
type ButtonVariant = Generated.ButtonVariant
/// Presentation shape for `NodeKind.Image` (Phase 287). `Default` is a
/// plain in-flow `<img>`; `Avatar` is a circular crop (the "user picture"
/// shape that was previously impossible without a Custom escape); `Rounded`
/// is a soft-cornered rectangle. Bounded by design — the renderer maps each
/// to a `fuaran-image-{variant}` class; no free-form CSS escape.
type ImageVariant = Generated.ImageVariant
/// Scroll axis for `NodeKind.ScrollArea` (Phase 289). Selects which
/// overflow axis the container clips + scrolls: `Vertical` → `overflow-y`,
/// `Horizontal` → `overflow-x`, `Both` → both. The renderer maps each to a
/// `fuaran-scrollarea-{axis}` class.
type ScrollOrientation = Generated.ScrollOrientation
/// Temporal breadth for `FormFieldKind.Date` (Phase 288). Selects the native
/// HTML control the renderer emits: `Date` → `<input type=date>`, `Time` →
/// `<input type=time>`, `DateTime` → `<input type=datetime-local>`. The bound
/// value is always an ISO-8601 string on the wire (`YYYY-MM-DD` /
/// `HH:MM` / `YYYY-MM-DDTHH:MM`) regardless of variant.
type DateVariant = Generated.DateVariant
/// Presentation mode for `NodeKind.Math` (Phase 293). `Inline` flows the
/// equation within surrounding text (a `<span>`); `Block` is a centred display
/// equation on its own line (a `<div>`). The renderer's deterministic fallback
/// emits the raw LaTeX source in the matching container; KaTeX upgrades it
/// client-side post-hydration (outside the parity output).
type MathDisplay = Generated.MathDisplay
type ColumnWidth = Generated.ColumnWidth
// ─── Locale-aware formatting vocabulary (Phase 102) ─────────────────────
//
// `Binding.Format` (below) projects a numeric source to a localised display
// string via the browser `Intl` API (Fable) / a documented `System.Globalization`
// fallback (.NET). The `Format` DU is the BOUNDED, SEMANTIC intent the AI
// emits ("format as GBP currency") — NOT an arbitrary `Intl` option bag. This
// is the same discipline as `CellFormat` (no raw-format-string escape on the
// typed surface, FGP 1) but locale-aware end-to-end: distinct from `CellFormat`,
// which is the column / Metric display-format vocabulary keyed to a `CellValue`
// projection and has no locale dimension. Phase 12.I shipped i18n *string*
// bindings (`Binding.I18n`); this closes the locale-correct *number / date /
// currency* formatting gap a data-heavy consumer needs.

// Stage 1 of the 692-694 swap: the locale-aware format vocabulary is the
// IDL-GENERATED type set now — these abbreviations point the established names
// at `Fuaran.UI.Generated`. Shapes are identical (RQA, same cases, same field
// names); the semantic documentation lives on the IDL declarations in
// Fuaran-Core's `UiIdl.fs` and in `docs/` (Format semantics: the numeric cases
// read the source as a plain number, `Date` as whole Unix-epoch seconds,
// `RelativeTime` as a signed count of its unit; no raw `Intl` option-bag
// escape — FGP 1).

/// Date-presentation breadth for `Format.Date` (generated — see `Fuaran.UI.Generated`).
type DateStyle = Generated.DateStyle

/// Relative-time grain for `Format.RelativeTime` (generated).
type RelativeTimeUnit = Generated.RelativeTimeUnit

/// Bounded, semantic locale-aware formatting intent carried by `Binding.Format` (generated).
type Format = Generated.Format

/// Locale selector for `Binding.Format` — `Ambient` defers to the host-supplied
/// ambient locale; `Explicit` pins a BCP-47 tag (generated).
type LocaleSource = Generated.LocaleSource

/// TODO P12 s3+: ApiEndpoint shape (likely a Fable.Remoting handle).
type ApiEndpoint = ApiEndpoint of string

/// The read-only context handed to a `Binding.Computed (fun ctx -> …)` closure
/// (§4b — the host-side derived-value escape). Phase 137 gives it a real shape:
/// typed read accessors over the module-state bag the renderer projects from
/// the live `StateStore` (Phase 106). A `Computed` closure can therefore read a
/// `Binding.State` slot and derive a value from it — e.g. label text that
/// depends on whether a `"busy"` flag is set.
///
/// The accessor never throws: a missing key or a runtime-type mismatch returns
/// `None`, so a `Computed` closure stays total. `State` is `obj`-erased (the
/// same erasure `BindingSources.State` carries); `TryGetState<'T>` unboxes.
///
/// Reactivity caveat: a `Computed` closure's state reads are opaque to the
/// renderer's static `stateKeysOfBinding` analysis, so a `Computed`-over-state
/// binding does NOT auto-re-render when the state changes (unlike a direct
/// `Binding.State` reader). For reactive text, prefer `binding.state`; reach
/// for a `Computed` projection only when the derivation can't be expressed as a
/// direct read. See `AI_AUTHORING_GUIDE.md`.
type BindingContext =
    { State: Map<string, obj> }

    /// Read the value at `key` from the module-state bag, unboxed to `'T`.
    /// Returns `None` for a missing key or a value that doesn't unbox to `'T`
    /// (never throws — a `Computed` closure stays total).
    member this.TryGetState<'T>(key: string) : 'T option =
        match Map.tryFind key this.State with
        | Some raw ->
            try
                Some(unbox<'T> raw)
            with _ ->
                None
        | None -> None

/// Companion helpers for `BindingContext`. `tryGetState` is the pipeline-style
/// form of the `TryGetState` member:
/// `ctx |> BindingContext.tryGetState<bool> "busy"`.
[<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module BindingContext =
    /// The empty context — no state visible. The renderer replaces this with a
    /// context projected over the live `BindingSources.State` when it resolves a
    /// `Binding.Computed`; the empty value is the resolver default + a test seam.
    let empty: BindingContext = { State = Map.empty }

    /// Build a context over a module-state bag.
    let ofState (state: Map<string, obj>) : BindingContext = { State = state }

    /// Pipeline-style typed read: `ctx |> BindingContext.tryGetState<bool> "busy"`.
    let tryGetState<'T> (key: string) (ctx: BindingContext) : 'T option = ctx.TryGetState<'T>(key)

/// Moved to `HostPrelude.fs` with the swap (the IDL's `THosted` slots reference
/// it from `Generated.fs`, which compiles first); re-exposed here unchanged.
type ErrorKind = HostPrelude.ErrorKind

// ─── Identity ───────────────────────────────────────────────────────

type NodeId = NodeId of string

// ─── Content-hash ──────────────────────────────────────────────────────
//
// `NodeKind.Custom` is the language's principled escape hatch for
// components that don't fit the typed surface. Originally it was
// opaque to op-stream replay, the layout observer, and the validator —
// the contract stopped at the wrapper. The bounded escape turns the unbounded
// escape into a *bounded* one: a Custom node MAY declare an optional
// `ContentHash` that the orchestrator and renderer verify against the
// registered renderer's hash before dispatch. `StrictReplay` routes
// mismatches through the `OnError` slot; `AdvisoryWarning` renders
// normally and logs through `IFuaranRuntime.Warn`; `Enforced` adds a
// *build-time* gate — the validator computes a SHA-256 over the Custom
// body's declared shape (props schema + exposedNodeIds + moduleId /
// componentId) and fails the build (FUARAN062, Error) when the hand-set
// hash has drifted (Phase 134). The hash itself is just bytes —
// `Strictness` governs what happens on mismatch (anti-pattern: don't
// conflate the two).

type HashStrictness = Generated.HashStrictness
/// Content-identity envelope for a `NodeKind.Custom` body. `Algorithm`
/// is `"SHA256"` for v1 (forward-compatible — a future `"BLAKE3"` etc.
/// is additive); `Hash` is the hex-encoded digest of the renderer's
/// source. Callers that opt out leave the Custom field `None`.
type ContentHash = Generated.ContentHash
// ─── First-class extension registry: the declared prop schema ─────────────────
//
// A `NodeKind.Custom` component's props are a `Map<string, JVal>`. A `PropSchema`
// makes that bag a DECLARED, typed, checkable contract — so a third-party widget
// is emittable-by-an-AI and validated the same way a built-in kind is: the schema
// projects into the AI's available-kinds prompt context AND drives runtime
// prop validation. See `CustomContract` / `CustomRegistry`.

/// The type a `NodeKind.Custom` prop is declared to hold — the closed vocabulary
/// an AI reads (to emit the prop) and the validator checks a `JVal` prop against.
/// `PJson` is the escape: any structured JSON value (the pre-schema behaviour).
[<RequireQualifiedAccess>]
type PropType =
    | PString
    | PInt
    | PFloat
    | PBool
    | PEnum of choices: string list
    | PObject
    | PArray
    | PJson

/// One declared prop of a Custom component: its wire key, its type, and whether
/// an author must supply it. A `PropSchema` is the ordered list of these.
type PropDecl =
    { Name: string
      Type: PropType
      Required: bool }

/// The declared prop contract of a Custom component — the ordered prop list a
/// `CustomContract` carries. Its key set is the same set the content hash folds
/// (so a schema and hash never disagree on WHICH props exist); the `PropType` +
/// `Required` are the added, checkable detail.
type PropSchema = PropDecl list

// ─── Accessibility ──────────────────────────────────────────────────
//
// Per-Node ARIA metadata. Defaults to `None` on every Node (no aria-*
// emission); per-component `Defaults.Accessibility.X` populates sensible
// defaults for interactive / notification shapes. The renderer reads this
// field and emits matching `aria-*` / `role` / `aria-live` attributes.
// `LabelledBy` / `DescribedBy` reference other Nodes by `NodeId`; the
// renderer resolves them to the target's stable HTML `id` attribute.

// `AriaRole` / `LiveRegionKind` moved to `HostPrelude.fs` with the swap — the
// Accessibility record's role/liveRegion slots are `THosted` in the IDL, so
// `Generated.fs` (compiled first) references the prelude definitions + codecs.
type AriaRole = HostPrelude.AriaRole

type LiveRegionKind = HostPrelude.LiveRegionKind

// ─── i18n resolver ──────────────────────────────────────────────────
//
// Apps wire a concrete `II18nResolver` (e.g. a host platform's I18n
// implementation, or a hand-rolled gettext / ICU / template-string resolver).
// `Binding.I18n` (below) consults the resolver via `BindingSources.I18nResolver`.
// The default resolver lives in `Fuaran.UI.Renderer.BindingResolver` and is a
// pass-through identity (`key -> "[i18n:" + key + "]"`) so missing-translation
// cases stay loud in dev.

type II18nResolver =
    /// Resolve an i18n key to a localised string. `args` carries pre-resolved
    /// values for `{argName}`-style placeholders (or any ICU shape the resolver
    /// implements). Implementations decide the substitution format; the interface
    /// is deliberately format-agnostic.
    abstract member Resolve: key: string * args: Map<string, obj> option -> string

// ─── Motion vocabulary ──────────────────────────────────────────────
//
// The 8-token motion DU §4h declares as the typed escape valve for
// AI-emittable animation. Renderer emits `fuaran-motion-{token}` as an
// outer-wrapper class; the reference CSS implements 4 tokens via
// `@keyframes` and leaves the remaining 4 as no-op class hooks for
// host extension.
//
// NB: The §4h design-doc enumeration
// uses `SpinDuringLoad` / `ScaleOnHover` where this DU uses
// `RotateOnRefresh` / `SlideInFromRight`. This DU is authoritative —
// §4h should be reconciled to match before public release.

type Motion = Generated.Motion
// ─── Parameterised-fragment hole + effect surface (Phase 180) ────────
//
// The wire-coupled `FragmentDeclSpec` / `FragmentRefSpec` carry these so a
// saved tree behaves as a FUNCTION of declared holes (the artifact-function
// abstraction). Defined here (ahead of the `Node` chain) because the specs in
// that chain reference them; the pure laws (signature derivation, currying,
// the effect join, arg validation) live in `Fuaran.UI.Fragment`. A zero-hole
// decl + a pure-deterministic effect is exactly the degenerate fixed-body
// fragment (Phase 61), which encodes byte-identically (both fields omitted).

/// A hole's value-space — the type domain of a value argument, validated at
/// bind time. A small bounded vocabulary (the Phase 53 `Bounded` kit projected
/// onto holes); `AnyString` is the unconstrained escape.
type HoleValueSpace = Generated.HoleValueSpace

module HoleValueSpace =
    /// Validate a boxed argument against a value-space. Returns the validated
    /// value (unchanged) or a human/AI-readable rejection.
    let validate (space: HoleValueSpace) (arg: obj) : Result<obj, string> =
        match space, arg with
        | HoleValueSpace.IntRange(lo, hi), (:? int as n) ->
            if n >= lo && n <= hi then
                Ok arg
            else
                Error(sprintf "value %d outside [%d, %d]" n lo hi)
        | HoleValueSpace.IntRange _, _ -> Error "expected an int argument"
        | HoleValueSpace.FloatRange(lo, hi), (:? float as f) ->
            if f >= lo && f <= hi then
                Ok arg
            else
                Error(sprintf "value %g outside [%g, %g]" f lo hi)
        | HoleValueSpace.FloatRange _, _ -> Error "expected a float argument"
        | HoleValueSpace.StringLen(lo, hi), (:? string as s) ->
            if s.Length >= lo && s.Length <= hi then
                Ok arg
            else
                Error(sprintf "string length %d outside [%d, %d]" s.Length lo hi)
        | HoleValueSpace.StringLen _, _ -> Error "expected a string argument"
        | HoleValueSpace.Enum choices, (:? string as s) ->
            if List.contains s choices then
                Ok arg
            else
                Error(sprintf "'%s' not in {%s}" s (String.concat ", " choices))
        | HoleValueSpace.Enum _, _ -> Error "expected a string (enum) argument"
        | HoleValueSpace.AnyString, (:? string) -> Ok arg
        | HoleValueSpace.AnyString, _ -> Error "expected a string argument"

/// A declared hole on a parameterised fragment. A `Value` hole binds a typed
/// value; a `Slot` hole binds a subtree (a tree-typed parameter); a `Repeat`
/// slot binds a subtree expanded a bounded number of times — the count
/// value-space MUST be bounded (totality, invariant 1).
/// A boxed-scalar hole default / fragment value arg (generated; typed `Scalar`,
/// the old `obj option` default payload).
type Scalar = Generated.Scalar

type HoleDecl = Generated.HoleDecl

module HoleDecl =
    let name (h: HoleDecl) : string =
        match h with
        | HoleDecl.Value(n, _, _) -> n
        | HoleDecl.Slot(n, _) -> n
        | HoleDecl.Repeat(n, _) -> n

    /// A hole is REQUIRED (must be bound at a complete application) when it has
    /// no usable default. Value holes with a default are optional; slots and
    /// repeats are always required.
    let isRequired (h: HoleDecl) : bool =
        match h with
        | HoleDecl.Value(_, _, Some _) -> false
        | _ -> true

    /// TOTALITY (invariant 1): a `Repeat` count must be a bounded value-space —
    /// never `AnyString` (an unbounded count is divergence). `true` when the
    /// hole satisfies the finiteness bound.
    let isTotal (h: HoleDecl) : bool =
        match h with
        | HoleDecl.Repeat(_, HoleValueSpace.IntRange _) -> true
        | HoleDecl.Repeat _ -> false // unbounded repeat count — refused
        | _ -> true

// ─── Effect / determinism signature (invariant 3) ──────────────────────────

/// The host-effect axis: does the fragment read or write host state?
type HostEffect = Generated.HostEffect
/// The determinism-source axis (mirrors Calc's `Volatility`): is the output a
/// pure function of its inputs, or does it depend on a clock / randomness / the
/// network?
type DeterminismSource = Generated.DeterminismSource
/// A total two-axis effect class. Defaults to pure-deterministic for a
/// value-only fragment. Joined componentwise through composition: the wider
/// (more-effecting / less-deterministic) value wins on each axis (pure ∘ impure
/// = impure; deterministic ∘ clock = clock).
type EffectClass = Generated.EffectClass

module EffectClass =
    let private hostRank =
        function
        | HostEffect.Pure -> 0
        | HostEffect.ReadsHost -> 1
        | HostEffect.WritesHost -> 2

    let private detRank =
        function
        | DeterminismSource.Deterministic -> 0
        | DeterminismSource.Clock -> 1
        | DeterminismSource.Random -> 2
        | DeterminismSource.Network -> 3

    /// The default for a value-only fragment — pure + deterministic.
    let pureDeterministic: EffectClass =
        { HostEffect = HostEffect.Pure
          Determinism = DeterminismSource.Deterministic }

    /// Componentwise join — the wider value wins on each axis. Associative +
    /// commutative (a lattice join), so composition order is irrelevant.
    let join (a: EffectClass) (b: EffectClass) : EffectClass =
        { HostEffect =
            (if hostRank a.HostEffect >= hostRank b.HostEffect then
                 a.HostEffect
             else
                 b.HostEffect)
          Determinism =
            (if detRank a.Determinism >= detRank b.Determinism then
                 a.Determinism
             else
                 b.Determinism) }

    /// `true` when `declared` is at least as wide as `actual` on both axes — the
    /// understatement check (a declared class narrower than the body's actual
    /// effects is a defect the validator flags).
    let covers (declared: EffectClass) (actual: EffectClass) : bool =
        hostRank declared.HostEffect >= hostRank actual.HostEffect
        && detRank declared.Determinism >= detRank actual.Determinism

// ─── The tree ───────────────────────────────────────────────────────

/// Stage 4b of the 692-694 swap: the Node envelope IS the generated record.
/// Deltas vs the pre-swap hand record: `Id` is a bare `string` (the `NodeId`
/// wrapper survives above as the ops/API-boundary type — wrap with
/// `NodeId n.Id` where a wrapped value is needed); `State` / `Style` are
/// OPTIONS — `None` is the canonical empty-state / default-style shape (the
/// encoder omitted an empty `StateBehaviour` / default `SemanticStyle` and
/// the decoder restored the defaults, so absence-as-`None` is byte-identical
/// on the wire). `Accessibility` (per-Node ARIA metadata), `Motion` (the
/// 8-token animation vocabulary) and `ExtraAttributes` (the AI-opaque
/// consumer-side `data-*` hatch) carry their prior semantics unchanged.
type Node<'Msg> = Generated.Node<'Msg>

and Accessibility = Generated.Accessibility

// Phase 692: the vocabulary is FLAT — the four behavioural categories
// (Layout / Display / Input / Visualisation) were a host-side envelope over
// these cases; `WIRE_FORMAT.md` §3.2 already called them "a host-side
// classification recovered on decode". Each is still recoverable from the
// case (see `Kind.category`), which is all the renderer and AiTools ever
// used them for.
//
// Stage 4b of the 692-694 swap: NodeKind IS the generated union. Case-payload
// deltas vs the pre-swap hand DU (per-case semantics live on the spec types
// below and in the IDL):
//   - `Filters of FiltersSpec<'Msg>` (was a bare `FilterSpec<'Msg> list`) —
//     the list is the record's `Items` field.
//   - `Custom of CustomSpec` (was 5 inline fields) — `ExposedNodeIds` is
//     `string list option` (`None` ≡ the old `[]`; the `NodeId` wrapper
//     erases inside the record).
//   - `DataGrid of DataGridSpec<'Msg>` (`GridSpec` survives as an alias).
and [<RequireQualifiedAccess>] NodeKind<'Msg> = Generated.NodeKind<'Msg>

and ErrorBoundarySpec<'Msg> = Generated.ErrorBoundarySpec<'Msg>

/// See `NodeKind.Switch` (Phase 392). The state-bound conditional-child spec.
/// `StateKey` names the reactive StateStore key whose value selects the case;
/// `Cases` is an ordered list of `SwitchCase` records — the renderer
/// resolves the state value's string form and renders the first case whose
/// `Match` equals it (first-match-wins, mirroring `FragmentDecl`'s
/// first-declaration-wins on name collision); `Default` renders when no case
/// matches (and is the SSR/first-paint surface before any `SetState`). Each
/// case encodes on the wire as a `{ "child": <Node>, "match": <string> }`
/// object (the generated `SwitchCase` record IS that wire object — the old
/// `(matchValue, child)` tuple shape); the whole kind is `{ "$type":"Switch",
/// "cases":[…], "default":<Node>, "stateKey":<string> }`. Duplicate `Match`es
/// are a validator error (FUARAN082) — the decoder accepts them
/// (first-match-wins keeps decode structural), the validator flags them.
and SwitchSpec<'Msg> = Generated.SwitchSpec<'Msg>

/// One `Switch` case — `{ Child; Match }` (generated; the old
/// `(matchValue, child)` tuple).
and SwitchCase<'Msg> = Generated.SwitchCase<'Msg>

/// The stable wire-shaped name a fragment is
/// addressed by. The string is opaque to the renderer + apply engine —
/// no parsing, no dotted-path semantics. The validator (FUARAN056)
/// enforces non-empty + no whitespace so the namespaced node-id
/// (`<refId>.<innerId>`) the renderer produces stays unambiguous.
and FragmentId = FragmentId of string

/// See `NodeKind.FragmentDecl`. Phase 180 — the wire-coupled artifact-function
/// carrier. `Holes` declares typed parameters (value params + tree slots) and
/// `Effect` the two-axis effect class. A zero-hole decl with a pure-deterministic
/// `Effect` is exactly the Phase 61 fixed-body fragment (the degenerate case) —
/// both fields are omitted on the wire so that shape stays byte-identical. The
/// `ParamFragment<'Msg>` alias in `Fuaran.UI.Fragment` names this same record.
/// Generated deltas: `Name` is a bare `string` (the `FragmentId` wrapper
/// survives above — wrap/unwrap at boundaries); `Holes` / `Effect` are OPTIONS
/// (`None` ≡ the old `[]` / pure-deterministic degenerate shape, matching the
/// wire omission).
and FragmentDeclSpec<'Msg> = Generated.FragmentDeclSpec<'Msg>

/// One bound argument at a `FragmentRef` / `Mount` (generated): a typed scalar
/// (`Int` / `Float` / `Bool` / `Str` — validated against its hole's value-space
/// at apply time) or a `SlotArg` subtree the resolver substitutes hygienically.
/// (The pre-swap hand DU carried `Value of obj | Slot`; the generated union
/// types the scalar cases and names the subtree case `SlotArg`.)
and FragmentArg<'Msg> = Generated.FragmentArg<'Msg>

/// See `NodeKind.FragmentRef`. Phase 180 — `Args` binds holes by name (value
/// scalars + slot subtrees). An empty `Args` is the Phase 61 name-only ref (the
/// degenerate case), omitted on the wire so that shape stays byte-identical.
/// Generated deltas: `Name` is a bare `string`; `Args` is an OPTION (`None` ≡
/// the old empty map, matching the wire omission).
and FragmentRefSpec<'Msg> = Generated.FragmentRefSpec<'Msg>

/// See `NodeKind.Mount`. Phase 265 — the isolation/embedding boundary carrier (§4o). The runtime,
/// *stateful*, *scoped* counterpart to `FragmentRefSpec`: `Inputs` reuses the parameterised-fragment
/// hole-binding shape (`FragmentArg` — the config + initial host→guest state), `ScopeId` names the
/// guest runtime scope, `Channel` declares the out-channel (host↔guest coupling + optional message
/// shape), `OnBubble` is the host handler for a guest-bubbled `Action<obj>` (a closure — it renders
/// the `"<closure>"` sentinel on the wire and decodes to a no-op action), and `Capabilities` are the
/// per-mount default-deny policy tags the boundary's gate consults. The guest's own render tree is
/// NOT carried here — only a scope reference + serialised hole bindings (opaque interior, like
/// `Custom`); the guest `Node<obj>` is produced host-side by the orchestration guest loader.
/// Generated deltas: `Inputs` / `OnBubble` are OPTIONS (`None` ≡ the old empty
/// map / no-op decoded closure); `Capabilities` is a bare `string list` (the
/// `CapabilityTag` wrapper survives below — wrap/unwrap at boundaries).
and MountSpec<'Msg> = Generated.MountSpec<'Msg>

/// The declared out-channel of a `Mount` (§4o.4). `Direction` bounds host↔guest coupling; the optional
/// `MessageShape` names the guest's message shape for validation + the capability gate.
and GuestChannel = Generated.GuestChannel
/// `OutOnly` (the default, safe for untrusted guests) lets the guest bubble to the host but forbids the
/// host pushing messages into the guest; `TwoWay` additionally permits host→guest push (which couples
/// the two lifecycles — memo open Q2, resolved: OutOnly default, TwoWay opt-in per-mount).
and ChannelDirection = Generated.ChannelDirection
// Phase 694 — the `CapabilityTag` wrapper is DELETED: `MountSpec.Capabilities`
// is a bare `string list` since the swap and nothing constructed or matched the
// wrapper any more (its last reference was a comment). A per-`Mount` capability
// tag (§4o.4) is the bare string, e.g. `"notify"`, `"call:reports.*"`; an empty
// list is default-deny of every host-affecting action.

// ─── Layout — semantic intent, not pixel-pushing ────────────────────
//
// Per Defect (3) resolution: each LayoutKind case carries its own spec
// record with `Children: Node<'Msg> list` as a regular record field.

/// §4b (Phase 390) — the unified container spec. `Layout` names how children
/// arrange; `Role` names what the container means (drives the emitted element,
/// ARIA landmark, and `fuaran-*` chrome). `Heading` carries the retired `Card`
/// heading (emitted only when `Some`). See `docs/BOX-CONTAINER-UNIFICATION.md`.
and BoxSpec<'Msg> = Generated.BoxSpec<'Msg>

/// How a `Box` arranges its children (generated — `LayoutMode`; `BoxLayout`
/// survives as the established host name for the same union). Cases are
/// POSITIONAL since the swap (the hand `FlexLayout` / `GridTemplate` payload
/// records are retired):
///  - `Flex(direction, wrap, gap)` — flex flow, the retired `Stack`. `gap`
///    (`None` ⇒ omitted on the wire ⇒ byte-identical for existing trees) is
///    the mechanism that obsoleted the retired `Spacer` node (Phase 459).
///  - `Grid(cols, templateColumns, gap)` — explicit grid, the retired
///    `GridLayout`. `Some templateColumns` emits verbatim and `cols` is ignored.
///  - `Auto` — responsive auto-tile, the retired `Dashboard` behaviour.
and BoxLayout = Generated.LayoutMode

/// The generated name for `BoxLayout` (both names are the same union).
and LayoutMode = Generated.LayoutMode

/// What a `Box` means — drives the emitted element, ARIA landmark, and chrome.
and BoxRole = Generated.BoxRole
and DashboardSpec<'Msg> = { Children: Node<'Msg> list }

and StackSpec<'Msg> =
    {
        Orientation: Orientation
        Children: Node<'Msg> list
        /// Feliz-parity additive: when `true`, the
        /// renderer emits the `fuaran-stack-wrap` class so children wrap at
        /// narrow viewport widths (CSS `flex-wrap: wrap`). Defaults to
        /// `false` so existing `Stack` authors see no behavioural change.
        /// Use for horizontal stacks whose child count × intrinsic width
        /// can exceed the viewport (chip strips, button rows, badge clusters).
        Wrap: bool
    }

and GridLayoutSpec<'Msg> =
    {
        Cols: int
        Children: Node<'Msg> list
        /// Typed escape hatch for arbitrary
        /// `grid-template-columns` sizing functions. When `Some s`, the
        /// renderer emits `s` verbatim and `Cols` is ignored; when `None`
        /// (the default), the renderer emits
        /// `repeat({Cols}, 1fr)` as before. Use this only when the typed
        /// `Cols: int` shape can't express the required sizing — irregular
        /// columns (`1fr 2fr`), fixed-plus-flex mixes
        /// (`100px repeat(5, 1fr)`), content-driven sizing
        /// (`min-content max-content`), or auto-fit
        /// (`repeat(auto-fit, minmax(150px, 1fr))`). The string is emitted
        /// without parsing — authors are responsible for valid CSS. The
        /// FUARAN046 advisory catches the most common
        /// equivalent-to-Cols-int regression. Validator's structural
        /// detection is intentionally conservative; the migration doc
        /// covers the rule-of-thumb decision tree.
        TemplateColumns: string option
    }

and SplitPanelSpec<'Msg> = Generated.SplitPanelSpec<'Msg>

/// Tabbed container spec (generated). `ActiveIndex` is the currently-active
/// tab binding (typically `binding.state "tabIndex" 0` for model-driven
/// deep-linkable tabs). `OnSelect` is the optional click dispatch (Phase 426
/// — `None`, the decoded / AI-authored shape, arms the write-back default:
/// an `ActiveIndex` bound directly to `Binding.State`/`Binding.Filter` has
/// the clicked index written to that slot). `TabHeaders` (optional,
/// FUARAN047 1:1 with `Children`) drives label / icon / disabled emission;
/// when `None` the Card-heading-inference path runs. `TabTags` / `ActiveTag`
/// / `OnSelectTag` are the typed tag overlay (FUARAN048 / FUARAN049; the
/// tag-channel write-back default when `OnSelectTag` is `None`). Generated
/// delta: the hand record's `Orientation` field is gone — the wire never
/// carried it, and the generated record follows the wire.
and TabsSpec<'Msg> = Generated.TabsSpec<'Msg>

/// Per-tab header declaration consumed by the tabs
/// renderer when `TabsSpec.TabHeaders` is populated. `Label` is the visible
/// tab text (resolves through the usual `TextSource` path — Literal / Bound /
/// I18n). `Icon` is an optional leading icon. `Disabled` is an optional
/// per-tab disabled binding (the renderer emits `aria-disabled` + skips
/// keyboard activation when resolved to `true`). Non-generic — the disabled
/// binding's resolution path does not need to flow `'Msg` through.
and TabHeader = Generated.TabHeader

and CardSpec<'Msg> =
    { Heading: TextSource option
      Children: Node<'Msg> list }

/// Step-sequence container spec (generated). `ActiveStep` is the
/// model-driven active-step binding; `OnSelect` fires with the clicked step
/// index. Generated delta: `OnSelect` is an OPTION — `None` (≡ the old no-op
/// `Action.Chain []` default) renders steps whose active styling tracks
/// `ActiveStep` with no dispatch on click. Mirrors `TabsSpec.OnSelect`.
and StepperSpec<'Msg> = Generated.StepperSpec<'Msg>

/// See `NodeKind.SummaryList`.
and SummaryListSpec<'Msg> = Generated.SummaryListSpec<'Msg>

/// See `NodeKind.Disclosure`.
///
/// `Open` is the controlled-state binding: when it resolves, the renderer
/// reflects its value onto the `<details>` element's `open` attribute. Hosts
/// that want pure renderer-side state pass `Binding.Static false` (or omit
/// the field — `Defaults.disclosure` ships a `Binding.Static false`); hosts
/// that want model-driven state (e.g. URL deep-linkable open-by-default)
/// pass `binding.state "additionalEntitlementsOpen" false` and pair it with
/// an `OnToggle` that dispatches into the consumer's `update`.
///
/// `OnToggle` fires when the user toggles the `<details>` element. The
/// renderer passes the new open value (true when the user just expanded it,
/// false when they just collapsed it). Optional since Phase 426 (the control
/// write-back default): `Some` dispatches as before (an
/// `"onToggle":"<closure>"` sentinel on the wire); `None` — the default, and
/// the decoded / AI-authored shape — arms the write-back default, so an
/// `Open` bound directly to `Binding.State`/`Binding.Filter` has the new
/// open value written to that slot (the HTML-native toggle still works
/// either way).
///
/// `DefaultOpen` is the initial-mount open value: independent of the `Open`
/// binding (which may resolve to either shape on first render). The
/// renderer uses this only for the initial mount before the binding
/// resolves — it maps to the `<details>` element's `defaultOpen` React
/// prop, which controls the uncontrolled-mode initial state.
and DisclosureSpec<'Msg> = Generated.DisclosureSpec<'Msg>

/// See `NodeKind.Modal` (Phase 289). `Open` is the controlled visibility
/// binding; `OnDismiss` is the action fired when the user dismisses (backdrop
/// click / close button / Esc) — wire-survivable like `FormSpec.OnSubmit`.
/// Optional since Phase 426 (the control write-back default): `Some action`
/// dispatches as before (encoded as the action value); `None` — the default,
/// and the decoded shape when the wire omits it — arms the write-back
/// default, so an `Open` bound directly to `Binding.State`/`Binding.Filter`
/// has `false` written to that slot on dismiss (a decoded dismissable modal
/// closes itself with zero host code). `Heading` is an optional dialog
/// title; `Dismissable` toggles the close affordance.
and ModalSpec<'Msg> = Generated.ModalSpec<'Msg>

/// See `NodeKind.ScrollArea` (Phase 289). `Orientation` selects the scroll
/// axis; `MaxHeight` / `MaxWidth` (pixels, optional) bound the scroll viewport
/// — the renderer emits the matching `max-height` / `max-width` inline style
/// when set.
and ScrollAreaSpec<'Msg> = Generated.ScrollAreaSpec<'Msg>

// ─── Display — pure presentation, no Msg ────────────────────────────

/// `Emphasis` bolds the row when it represents a total / highlight; `Help`
/// renders as small-print under the label when set.
and LabelValueRowSpec = Generated.LabelValueRowSpec

/// See `NodeKind.Fact`. `Emphasis` gives the value KPI-tile prominence
/// (inside a `Dashboard` role it tiles like a `Metric` card); `Help` is
/// small-print under the label, mirroring `LabelValueRow`.
and FactSpec = Generated.FactSpec

/// Heading variants beyond
/// the `<h{Level}>` Standard. Variants emit the same `<h{Level}>` tag but
/// add a `fuaran-heading-{variant}` class so the consumer's design tokens
/// can pick out eyebrow / caption / lead text without overriding the
/// `<h1..h6>` semantics.
and HeadingVariant = Generated.HeadingVariant

and HeadingSpec = Generated.HeadingSpec

and MarkdownSpec = Generated.MarkdownSpec

and BadgeSpec = Generated.BadgeSpec

/// §4b — `NodeKind.Link`'s typed spec (Phase 139). `Href` is a
/// `Binding<string>` so the destination can be static or data-bound;
/// the renderer routes it through `Sanitize.sanitizeUrlOrBlank` (blocks
/// `javascript:` / `vbscript:` / raw `data:` before it reaches the DOM).
/// `Rel` / `Target` emit the matching anchor attributes when set
/// (`rel="noopener"`, `target="_blank"`, …); `Download` emits a bare
/// `download` attribute. All but `Href` / `Label` default off in
/// `Defaults.link`, so the AI emits only what differs.
and LinkSpec = Generated.LinkSpec

/// §4b — `NodeKind.Image`'s typed spec (Phase 287). `Src` is a
/// `Binding<string>` routed through `Sanitize.sanitizeUrlOrBlank` at render
/// time (blocks `javascript:` / `vbscript:` / `file:` and unknown schemes).
/// `Alt` is mandatory — the accessibility floor for a non-decorative image
/// (pass an empty `Literal ""` only for a purely decorative one). `Variant`
/// defaults to `Default` in `Defaults.image`.
and ImageSpec = Generated.ImageSpec

/// §4b — `NodeKind.List`'s typed spec (Phase 287). `Items` is the ordered
/// list of item texts; `Ordered` selects `<ol>` (true) vs `<ul>` (false).
and ListSpec = Generated.ListSpec

/// §4n — `NodeKind.Toast`'s typed spec (Phase 289). The declarative,
/// in-tree, SSR-rendered notification surface. `Open` is the controlled
/// visibility binding (resolves `true` → shown); `Tone` selects the status
/// colour; `Dismissable` adds a close affordance. Renders inline (no portal),
/// positioned by CSS, with `role="status"` + an `aria-live` region — see the
/// overlay render-fidelity contract (docs/SSR.md). Defaults in `Defaults.toast`.
and ToastSpec = Generated.ToastSpec

/// §4b — `NodeKind.CodeBlock`'s typed spec (Phase 290). `Code` is the raw
/// source (HTML-escaped at render, never markdown-parsed); `Language` is the
/// highlight hint emitted as `language-{Language}` on the `<code>` element;
/// `LineNumbers` toggles a CSS-counter gutter; `HighlightLines` is the set of
/// 1-based line numbers to mark (emitted as a deterministic
/// `data-highlight-lines` attribute the client enhancement reads); `Copyable`
/// renders a copy affordance. All default in `Defaults.codeBlock`.
and CodeBlockSpec = Generated.CodeBlockSpec

/// §4b — `NodeKind.Math`'s typed spec (Phase 293). `Source` is the LaTeX
/// string (escaped in the deterministic fallback render); `Display` selects an
/// inline `<span>` vs a block `<div>`. Defaults in `Defaults.math`.
and MathSpec = Generated.MathSpec

and SparklineSpec = Generated.SparklineSpec

/// §4b — a 2-D user-space coordinate box for `NodeKind.Drawing` (Phase
/// 524). Mirrors SVG's `viewBox` (`minX minY width height`); the renderer
/// (Phase 525) maps it to the rendered `<svg viewBox>`. Plain floats — a
/// `Drawing` is a *resolved* geometric artefact (a chart lowers to concrete
/// coordinates), so geometry is static; only `DrawStyle` carries bindings.
and ViewBox = Generated.ViewBox
/// A point in a `Drawing`'s user-space coordinate system (Phase 524).
and DrawPoint = Generated.DrawPoint

/// A typed drawing command for `Shape.Curve` (Phase 524) — the closed, typed
/// replacement for a raw SVG `d` path string. There is deliberately NO `Path`
/// shape and NO `d` string: `path` collides with the apply engine's
/// path-addressing and with binding paths, and a raw `d` string would
/// reintroduce an untyped escape hatch. A curve is an ordered list of these
/// typed commands instead. See docs/CHARTS-DRAWING-PRIMITIVE-DESIGN.md §3.
and CurveCommand = Generated.CurveCommand
/// §4b — the fill/stroke style bindings shared by every `Shape` (Phase 524).
/// Each field is optional and omitted from the wire when `None` (rule 4), so a
/// shape emits only what differs from the renderer's inherited default. `Fill`
/// / `Stroke` are colour strings (theme tokens or literals) carried as
/// `Binding<string>` so a chart re-themes without re-lowering; `StrokeWidth` /
/// `Opacity` are `Binding<float>`. Bindings (not plain values) are the one
/// place a `Drawing` stays reactive — geometry is static, colour is bindable.
/// §4b — text alignment for `Shape.Label` (Phase 528.1). Maps to SVG
/// `text-anchor`: `Start` (left) / `Middle` (centred) / `End` (right). Chosen
/// for real chart labels — right-aligned y-tick columns, centred x-categories,
/// left-aligned titles. Bare-string enum on the wire.
and TextAnchor = Generated.TextAnchor
/// §4b — the fill/stroke (+ text) style shared by every `Shape` (Phase 524; text
/// fields Phase 528.1). Each field is optional and omitted from the wire when
/// `None` (rule 4), so a shape emits only what differs from the renderer's
/// inherited default. `Fill` / `Stroke` are colour strings carried as
/// `Binding<string>` so a chart re-themes without re-lowering; `StrokeWidth` /
/// `Opacity` are `Binding<float>`. `TextAnchor` / `FontSize` / `Emphasis` /
/// `FontFamily` apply only to `Label` text (ignored on other shapes): the
/// alignment, the size in user-space px, the weight (reusing the `Emphasis`
/// vocabulary — `Loud` → bold, `Quiet` → light), and the font-family stack (so a
/// chart carries its own font — self-contained, no host CSS needed). Bindings
/// (colour) are the one place a `Drawing` stays reactive — geometry + text
/// metrics are static.
and DrawStyle = Generated.DrawStyle
/// §4b — the closed, typed vector-graphics shape vocabulary for
/// `NodeKind.Drawing` (Phase 524). Every case is wire-survivable and
/// introspectable — the opposite of `NodeKind.Custom`: no raw SVG markup, no
/// arbitrary attribute bag, no `Path`/`d` escape hatch (see `CurveCommand`).
/// Coordinates are user-space floats in the drawing's `ViewBox`; `Group` nests
/// shapes under a shared style. Naming is chosen for the data-science
/// audience: `Rectangle` (not the abbreviated `Rect`), `Label` (not `Text`,
/// which overloads `FormFieldKind.Text` / `TextSource`).
and Shape = Generated.Shape

/// §4b — `NodeKind.Drawing`'s typed spec (Phase 524). `ViewBox` is the
/// user-space coordinate box; `Shapes` is the ordered draw list (painter's
/// order); `Style` is the root style inherited by shapes that omit their own;
/// `Title` / `Description` are the optional accessible name / long description
/// the renderer (Phase 525) emits as `role="img"` + `<title>` / `<desc>`. This
/// is the shared render target every chart lowers to (Phase 526). Defaults in
/// `Defaults.drawing`.
and DrawingSpec = Generated.DrawingSpec

and SkeletonSpec = Generated.SkeletonSpec

/// §4b lines 473–487 — Metric's typed spec record. All fields default in
/// `Defaults.metric`; AI emits only what differs.
and MetricSpec = Generated.MetricSpec

/// §4k Q3.4 — tier/mode/status banner; sized for content blocks.
/// Toasts are separate (§4n overlay surfaces, session 3+).
and CalloutSpec = Generated.CalloutSpec

/// §4k Q3.4 — long-running async indicator. Use `Indeterminate = true`
/// with a `Caveat` when no honest 0..1 bound exists, per the §4k indeterminate-progress guidance.
and ProgressSpec = Generated.ProgressSpec

// ─── Input — interactive, carries Msg via Action<'Msg> ──────────────

and ButtonSpec<'Msg> = Generated.ButtonSpec<'Msg>

/// §4c idiom — filtered pickers. Authors filter inside the binding accessor,
/// not via a `Filter` field on the spec. The component is shape-stable across
/// "tier ≥ 1" / "recently active" / "owned by me" use cases.
and SelectSpec<'Msg> = Generated.SelectSpec<'Msg>

/// A single Select option. `Value` is the wire id (stable, ASCII-safe);
/// `Label` is the displayed text.
and SelectOption = Generated.SelectOption

/// Minimal viable FormSpec for session 3b. Per the §4k worked example: a
/// Form is an ordered list of fields + a submit Action. `FormField` carries
/// per-field `Kind` rather than a stringly-typed cell-type discriminator —
/// the renderer pattern-matches Kind to choose the input element + wire the
/// `onChange` handler back into typed `Action<'Msg>`.
and FormSpec<'Msg> = Generated.FormSpec<'Msg>

and FormField<'Msg> = Generated.FormField<'Msg>
// Every value-carrying event handler is optional (Phase 426 — the control write-back
// default, generalising `FilterKind.onChange`'s Phase 423 mechanics). A `Some` closure
// dispatches on change exactly as before (F#-authored apps unchanged; `"<closure>"`
// sentinel on the wire, byte-identical). `None` — the shape an AI author emits, and the
// shape a decoded field takes when the wire omits the handler — arms the renderer's
// write-back default: when the field's `value` binding is directly `Binding.State(key,_)`
// the renderer writes the changed value to the reactive `StateStore` under `key`
// (`Binding.Filter(name, None)` ⇒ `FilterStore`); any other binding shape means no write (the
// FUARAN069 inert-control check covers it). Wire: `Some` → `"onChange":"<closure>"`
// (byte-stable); `None` → the field is omitted.
and FormFieldKind<'Msg> = Generated.FormFieldKind<'Msg>

/// Optional date/time-field bounds (Phase 288). `Min` / `Max` are ISO-8601
/// strings (matching the field's bound value); `Step` is in seconds. All
/// default to `None` (`Defaults.dateFieldConstraints`); an absent field means
/// the renderer omits the corresponding HTML attribute.
and DateFieldConstraints =
    { Min: string option
      Max: string option
      Step: float option }

/// Optional numeric-field bounds. All three fields default to
/// `None` (see `Defaults.numberFieldConstraints`); an absent field means
/// the renderer omits the corresponding HTML attribute and the validator
/// skips the corresponding range check.
and NumberFieldConstraints =
    { Min: float option
      Max: float option
      Step: float option }

/// A single filter chip (0.2.0 filters-unification): the control is an
/// ordinary `FormFieldKind` — ONE control vocabulary for forms and filter
/// strips (the retired parallel `FilterKind` family measurably confused
/// AI authors: Select-vs-ChoiceFilter was a top judge-failure class).
/// The declarative floor extends here: a control whose `value` binding is
/// omitted on the wire decodes to `Binding.Filter(Name, None)` — the item's
/// declared `Name` IS the filter-store key, so control, store and every
/// `dependsOn` edge agree by construction (the old dual-write of the name
/// could silently diverge). An explicit binding is still honoured.
and FilterSpec<'Msg> = Generated.FilterSpec<'Msg>
/// Requested encoding for `Action.ReadFileBody` (Phase 136; generated). The
/// renderer's default browser impl maps `Text` → `readAsText`, `Base64` → the
/// data-URL payload with the header stripped, `DataUrl` → the full
/// `data:<mime>;base64,…` string. Encodes as a bare-string enum on the wire (§3.5).
and FileReadEncoding = Generated.FileReadEncoding

/// An opaque, host-held reference to a user-selected file's blob (Phase 136).
/// `Id` is a renderer-assigned stable token — the **only** part that
/// serialises (a blob cannot cross the wire). `Handle` carries the actual
/// browser `File` object (boxed) on browser hosts so `Action.ReadFileBody`
/// can read it without the consumer hand-rolling `FileReader`; it is `None`
/// on non-browser hosts and on every decoded tree. Boxing the host blob
/// behind `obj option` keeps `Fuaran.UI` standalone (FGP 2 — no `Browser.*`
/// dependency leaks into the typed-tree package); the `box`/`unbox` round-trip
/// is a sanctioned host-blob boundary (the renderer's `FileReader` arm
/// unboxes it), in the same family as the Custom-renderer prop bag.
and FileRef = HostPrelude.FileRef

/// FileSelection is the metadata the browser exposes for an `<input type=file>`
/// change event. The actual `File` blob stays browser-side; `Ref` carries an
/// opaque handle to it (Phase 136) so `OnSelect` can chain
/// `Action.ReadFileBody` to ingest the body — the metadata + handle, never
/// the blob, are what the spec hands the author.
and FileUploadSpec<'Msg> = Generated.FileUploadSpec<'Msg>

and FileSelection = HostPrelude.FileSelection

/// AG Charts-shaped chart spec. `Source` resolves to a row sequence; `XField`
/// and `YFields` name the row's property keys to plot. AG Charts adapter
/// (session 3b) feeds typed `Action<'Msg>` callbacks back; falling back to
/// plain `<canvas>` rendering for the demo when no adapter is wired.
and ChartSpec<'Msg> = Generated.ChartSpec<'Msg>

and ChartKind = Generated.ChartKind

/// Author-facing carrier for a **static read-only table** (Phase 393). No longer a
/// `VisKind` case of its own — `Fuaran.table` lowers it into the read-only mode of
/// `NodeKind.DataGrid` (`GridSpec.StaticRows`), so one tabular kind owns both the
/// static and the data-bound surface. Use it for static reference tables, glossary
/// definitions, structured help content; cells carry `TextSource` so i18n +
/// binding-substitution still apply. `OnRowClick` is retained for source
/// compatibility but the read-only mode is non-interactive (it was host-only on the
/// wire — see [[Fuaran.UI.WireSurvivability]]).
and TableSpec<'Msg> =
    { Headers: TextSource list
      Rows: TextSource list list
      OnRowClick: (int -> Action<'Msg>) option }

/// Minimal viable Map spec for session 3b. Coordinates are WGS-84 latitude /
/// longitude in decimal degrees. The renderer defers to a host-provided map
/// library (Leaflet via Fable interop); falls back to a labelled placeholder
/// when no adapter is wired.
and MapSpec<'Msg> = Generated.MapSpec<'Msg>

and MapMarker = Generated.MapMarker
// ─── Visualisation — data-bound, complex ─────────────────────────────

/// Per Defect (1) resolution: row-typed fields are `obj`-erased at the
/// tree level. Authors construct a typed `GridSpecOf<'row,'Msg>` facade
/// (below) and `Fuaran.grid` boxes it into this shape. The renderer trusts
/// the per-Kind type-tag invariant.
///
/// Generated (the IDL name is `DataGridSpec`; `GridSpec` survives as the
/// established host alias). `RowKey` is the optional closure *override*
/// (Phase 425); `RowKeyField` names the row property a decoded grid projects
/// as the key. `StaticRows` (Phase 393 — the static read-only mode folded in
/// from the retired `NodeKind.Table`) is the generated `StaticRows` record
/// (`{ Headers: TextSource list; Rows: TextSource list list }` — full
/// `TextSource` cells, same fidelity as the old tuple shape).
and GridSpec<'Msg> = Generated.DataGridSpec<'Msg>

/// The generated name for `GridSpec` (both names are the same record).
and DataGridSpec<'Msg> = Generated.DataGridSpec<'Msg>

/// `NodeKind.Filters`' payload record (generated) — `Items` carries what was
/// the case's bare `FilterSpec<'Msg> list`.
and FiltersSpec<'Msg> = Generated.FiltersSpec<'Msg>

/// `NodeKind.Custom`'s payload record (generated) — the old 5 inline case
/// fields. `ExposedNodeIds` is `string list option` (`None` ≡ the old `[]`;
/// the `NodeId` wrapper erases inside the record — wrap at consumers).
and CustomSpec = Generated.CustomSpec

/// The static read-only rows of a `DataGrid` (generated — Phase 393's folded
/// `Table`): `TextSource` header / cell text (the old `(headers, rows)` tuple
/// as a record).
and StaticRows = Generated.StaticRows

/// Typed author-facing facade for GridSpec. Smart-ctor `Fuaran.grid` boxes
/// the row accessors and stores a `GridSpec<'Msg>` in the tree.
and GridSpecOf<'row, 'Msg> =
    { Source: Binding<'row seq>
      RowKey: 'row -> string
      Columns: Column<'row, 'Msg> list
      OnRowClick: ('row -> Action<'Msg>) option
      Editable: bool }

/// §4k Q3.2 — Column carries a typed `Kind`, not a nullable `OnEdit`.
and Column<'row, 'Msg> =
    { Label: string
      Value: 'row -> CellValue
      Format: CellFormat
      Kind: CellKind<'row, 'Msg>
      Width: ColumnWidth }

/// Tree-level row-erased Column (generated). `Fuaran.grid` boxes a
/// `Column<'row,'Msg>` into this shape via `Column.erase`. `Value` is the
/// optional closure *override* (Phase 425); `Field` names the row property a
/// decoded grid projects to the cell. `Value` returns the typed `CellValue`
/// (a host-prelude DU since stage 4b — no box/unbox ceremony survives).
and ColumnErased<'Msg> = Generated.ColumnErased<'Msg>

/// What a single cell of data is, after the `Value` projection runs.
/// Pre-formatted strings break AG Grid's numeric sort (§4c idiom 3) — use
/// `Numeric` + a `CellFormat.Percent` / `Currency` / `Number` instead.
/// Declared in the host prelude (stage 4b) so the generated closure slots
/// (`ColumnErased.Value`, `CellKindErased.Editable`, `CellFormat.Custom`)
/// keep the typed surface; this alias is the author-facing name.
and CellValue = HostPrelude.CellValue

/// §4k Q3.2 — typed cell-shape enum. Non-interactive kinds (Text / Numeric /
/// Date) have no action surface; `Custom` is the last-resort escape for
/// bespoke cell renderers.
and [<RequireQualifiedAccess>] CellKind<'row, 'Msg> =
    | Text
    | Numeric
    | Date
    | Editable of (('row * CellValue) -> Action<'Msg>)
    | Checkbox of get: ('row -> bool) * onToggle: ('row * bool -> Action<'Msg>)
    | Button of label: TextSource * onClick: ('row -> Action<'Msg>)
    | ButtonGroup of (TextSource * ('row -> Action<'Msg>)) list
    | Link of href: ('row -> string) * label: ('row -> TextSource)
    | Pill of label: ('row -> TextSource) * tone: ('row -> ToneVariant)
    | Progress of fraction: ('row -> float) * label: (('row -> TextSource) option)
    | Custom of (('row -> JVal) -> Node<'Msg>)

/// Row-erased twin of CellKind (generated). Smart-ctor `Column.erase` boxes
/// the row accessors before placing into the tree. Generated deltas vs the
/// pre-swap hand DU:
///  - `Editable of onEdit: (obj * CellValue -> Action<'Msg>) option` — the
///    closure is optional; its `CellValue` argument stays typed (the
///    host-prelude DU, stage 4b);
///  - `Checkbox` / `Button` carry OPTIONAL `onToggle` / `onClick`;
///  - `ButtonGroup of ButtonGroupItem<'Msg> list` — the old
///    `(TextSource * (obj -> Action<'Msg>)) list` tuples are
///    `{ Label; OnClick: option }` records;
///  - `Pill` / `Link` / `Progress` unchanged apart from field-name spelling
///    (`Progress.labelFn` keeps its option).
and CellKindErased<'Msg> = Generated.CellKindErased<'Msg>

/// One `CellKindErased.ButtonGroup` button (generated) — the old
/// `(TextSource * (obj -> Action<'Msg>))` tuple as a `{ Label; OnClick }`
/// record with an optional handler.
and ButtonGroupItem<'Msg> = Generated.ButtonGroupItem<'Msg>

/// §4k Q3.3 — typed formatters; compound / derived strings stay in
/// `Column.Value`. Keeps numeric sort intact when a column displays numbers.
and CellFormat = Generated.CellFormat
// ─── State behaviours ────────────────────────────────────────────────
//
// Generated. Since the swap the Node envelope carries `State` as an OPTION —
// `None` is the canonical empty shape (all three slots absent; the encoder
// omitted an empty record and the decoder restored it, so `None` is the
// byte-identical successor of the old always-present empty record).

and StateBehaviour<'Msg> = Generated.StateBehaviour<'Msg>

and ErrorPayload = HostPrelude.ErrorPayload

// ─── Style — semantic, not CSS ───────────────────────────────────────

and SemanticStyle = Generated.SemanticStyle

/// §4b (Phase 147) — the bounded, additive-only semantic content-role
/// vocabulary. The AI emits a role as *intent*; the renderer projects a
/// stable `fuaran-role-{role}` class and the host CSS owns the pixels — no
/// raw style escape reaches the typed tree. Generalises the
/// `HeadingVariant.Eyebrow`/`Caption`/`Lead` precedent to any node.
/// **Additive-only post-ship** (like `LayoutFlag`/`StyleFlag`): adding a case
/// is a minor bump (existing matches gain an `FS0025` warning); redefining a
/// case breaks every prompt cache that pattern-matched it.
and StyleRole = Generated.StyleRole
/// §4b (Phase 147) — the bounded, additive-only font-voice vocabulary: the
/// display-vs-structural split. Projects a `fuaran-voice-{voice}` class.
/// Additive-only post-ship, same discipline as `StyleRole`.
and FontVoice = Generated.FontVoice
and ToneVariant = Generated.ToneVariant
and StyleWeight = Generated.StyleWeight
and Emphasis = Generated.Emphasis
// ─── Bindings — typed at author, stringly-typed at wire ──────────────
//
// §4k Q3.1 — `Binding.Query` names are MODULE-SCOPED (unqualified name
// resolves against the current module's typed API surface). Cross-module
// reads use the fully-qualified `ModuleId.name` form.
//
// Per Defect (2) resolution: `Query` / `Selection` payloads stay obj-erased
// at the tree level; typed entry points (`binding.query`, `binding.selection`
// in Fuaran.fs) wrap typed accessors in obj-erasing closures.
//
// Stage 1 of the 692-694 swap: `Binding<'T>` IS the IDL-generated union now
// (full case parity since the Phase 692 gap-closure; case-field order matches
// the old hand-written positional order, so sites read unchanged). The deltas
// against the pre-swap type, recorded in DECISIONS.md D3 and the swap plan:
// `Static`/`State` payloads are `option` (absence is structural), `Query.dependsOn`
// is `string list option`, `Selection.nodeId` is a bare `string` (the `NodeId`
// wrapper erases at this seam), `Local` is positional (the `LocalBinding` record
// is retired; order: flushOn, format, initialFrom, onCommit option, parse),
// `I18n` args / `Transform` params carry `Binding<JVal>` sources, and
// `Invoke`/`Transform` tuple lists became `InvokeArg` / `TransformParam` records.

and Binding<'T> = Generated.Binding<'T>

/// The async value envelope of a `Binding.Invoke` (Phase 283). Rendered through the existing
/// `StateBehaviour` surface — `Pending` → `onLoading`, `Error` → `onError`, `Ready` → the value —
/// so it adds no new node concept. A runtime value (the resolver produces it); not wire-serialised.
and [<RequireQualifiedAccess>] Deferred<'T> =
    | Pending
    | Ready of 'T
    | Error of message: string

/// When a `Binding<'T>.Local` flushes its buffered value back
/// to the model. Free-text inputs typically use `OnBlur` (the canonical
/// Salary-style shape); form-scoped flushing uses `OnSubmit`; live-
/// validating inputs use `OnDebounce` with a millisecond delay; explicit
/// "Apply" buttons use `OnCommitAction` paired with `Action.CommitLocal`.
and LocalFlushTrigger = Generated.LocalFlushTrigger

// _(The `LocalBinding<'T>` record is retired with the swap — `Binding.Local` is
// positional in the generated union: flushOn, format, initialFrom, onCommit
// option, parse. The renderer dispatches `onCommit` when the buffer flushes;
// `format`/`parse` are the display-format hook + reverse-format pair.)_

/// One `Binding.Transform` parameter — binds a pipeline `ColExpr.Param` name to a
/// scalar binding source (generated; `From` carries `Binding<JVal>`).
and TransformParam = Generated.TransformParam

/// A capability-invoke argument (generated; `Addr`/`Value` — was `(string * string)`).
and InvokeArg = Generated.InvokeArg

/// The `{max, min}` payload of a `Range` control's value (generated; the old
/// `float * float` pair — the record IS the wire object).
and RangePair = Generated.RangePair

// ─── Actions — effect-typed ──────────────────────────────────────────
//
// Per Defect (2) resolution: `Call`'s onResult payload stays obj-erased
// at the tree level; typed `Action.call` in Fuaran.fs wraps `'a -> 'Msg`
// in an obj-erasing closure.
//
// Stage 2 of the 692-694 swap: `Action<'Msg>` IS the IDL-generated union now.
// Deltas against the pre-swap type (D3 + the swap plan): `Call` carries a bare
// `endpoint: string` (the `ApiEndpoint` wrapper erases at this seam);
// `ReadFileBody` splits the old `FileRef` record into the wire `fileRef: string`
// + a HOST-ONLY `fileHandle: obj option` (never encoded — the boxed browser
// `File` blob the runtime reads), and `onRead` is an option; `Invoke` args are
// `InvokeArg` records; `CallResultTarget`'s case names are the WIRE tags
// (`State` / `Query`, the old `IntoState` / `IntoQuery`).

and Action<'Msg> = Generated.Action<'Msg>

/// `Action.Call`'s declarative result target (Phase 428): where the endpoint
/// response lands so consumers can read it (generated — case names are the
/// wire tags `State` / `Query`). `State` writes the reactive `$state.<key>`
/// slot (`Binding.State` readers); `Query` writes the `queryResults` slot
/// `<name>` (`Binding.Query` readers). Omitted from the wire when `None`.
and CallResultTarget = Generated.CallResultTarget

// ─── Text sources — bindable, i18n-aware ────────────────────────────

and TextSource = Generated.TextSource
// ─── Theme ──────────────────────────────────────────────────────────
//
// `Theme` is the typed F# record consumers compose to drive the
// renderer's CSS-variable bundle. The bundle was once emitted as
// a static reference stylesheet (`fuaran-reference.css`) and
// consumers re-bound `--fuaran-*` variables at their app shell — a
// convention, not a contract. Promoting the bundle into a typed
// record lets apps compose Themes the way they compose Nodes, Portal-
// emitted apps get themable shapes by construction, and the eval suite
// can assert visual output against a known Theme.
//
// Shape coverage rationale: every record field below mirrors a variable
// in `fuaran-reference.css`'s `:root` block (the byte-for-byte
// target).
//
// `ColorVar` is the DU consumers use to write a colour value. `Hex` is
// the common case (matches the reference CSS verbatim); `OKLCH` is
// preferred for new theme authoring; `CssRaw` is the escape hatch for
// arbitrary CSS expressions (gradients, `color-mix(...)`, etc.). The
// renderer (`Fuaran.UI.Renderer.Theme.colorVarToCss`) projects each case
// to its CSS string form.

and [<RequireQualifiedAccess>] ColorVar =
    /// Hex colour literal, e.g. `Hex "#ffffff"`. The string is emitted
    /// verbatim — authors include the `#` prefix.
    | Hex of string
    /// OKLCH colour space — perceptually-uniform. Emitted as
    /// `oklch(L C H / alpha)`. Use `1.0` for fully opaque.
    | OKLCH of l: float * c: float * h: float * alpha: float
    /// Escape hatch for arbitrary CSS — gradients, `color-mix(...)`,
    /// `currentColor`, custom calc expressions. Emitted verbatim, so
    /// authors are responsible for the value being a valid CSS
    /// expression.
    | CssRaw of string

/// One tone's three slots — background, foreground, border. The same
/// shape applies to every tone in [[Tones]].
and ToneStops =
    { Background: ColorVar
      Foreground: ColorVar
      Border: ColorVar }

/// The 7-tone palette, mirroring the `--fuaran-tone-{name}-{slot}` surface
/// from `fuaran-reference.css` and `HOST-STYLING-CHECKLIST.md` §1.1.
/// `Default` = neutral surface, `Subdued` = muted surface, `Brand` =
/// primary accent, `Success` / `Warning` / `Critical` = state colours,
/// `Info` = informational accent.
and Tones =
    { Default: ToneStops
      Subdued: ToneStops
      Brand: ToneStops
      Success: ToneStops
      Warning: ToneStops
      Critical: ToneStops
      Info: ToneStops }

/// Spacing scale — padding / gap / margin (xs..xl). Values are CSS
/// dimension strings so consumers can pick units (`"4px"`, `"0.25rem"`,
/// `"4mm"`). Mirrors `--fuaran-space-{xs..xl}`.
and Spacing =
    { Xs: string
      Sm: string
      Md: string
      Lg: string
      Xl: string }

/// Typography size scale. Mirrors `--fuaran-text-{xs..3xl}`. Values are
/// CSS dimension strings.
and FontScale =
    { Xs: string
      Sm: string
      Base: string
      Lg: string
      Xl: string
      XXl: string
      XXXl: string }

/// Font-weight values (400 / 500 / 600 / 700 by convention). Mirrors
/// `--fuaran-font-weight-{regular..bold}`.
and FontWeight =
    { Regular: int
      Medium: int
      Semibold: int
      Bold: int }

/// Line-height multipliers (unitless). Mirrors
/// `--fuaran-line-height-{tight,normal,relaxed}`.
and LineHeight =
    { Tight: float
      Normal: float
      Relaxed: float }

/// Border-radius scale. Mirrors `--fuaran-radius-{sm,md,lg,full}`. CSS
/// dimension strings; `Full` is typically `"9999px"` for pill / badge
/// rounding.
and Radius =
    { Sm: string
      Md: string
      Lg: string
      Full: string }

/// Button vertical-padding × horizontal-padding × font-size — the
/// Critical-C-ii axis closing the gap where a compact-
/// chip button couldn't be expressed without monkey-patching
/// `.fuaran-button`. Reference defaults route through the spacing /
/// font-scale variables (`var(--fuaran-space-sm)`, etc.).
and ButtonSize =
    { PadY: string
      PadX: string
      FontSize: string }

/// One state's value for the 7-tone × 3-slot matrix — same shape as
/// [[Tones]] but semantically distinct. Each tone holds the bg / fg /
/// border colour the surface adopts when in this interaction state
/// (hover, focus, active, disabled). Visual-parity audit follow-on to
/// `HOST-STYLING-CHECKLIST.md` §1.
and ToneStateMatrix =
    { Default: ToneStops
      Subdued: ToneStops
      Brand: ToneStops
      Success: ToneStops
      Warning: ToneStops
      Critical: ToneStops
      Info: ToneStops }

/// Focus-ring shape — colour / width / offset / style. Consumes
/// `--fuaran-focus-ring-{color,width,offset,style}`. The reference CSS
/// uses `:focus-visible` so keyboard navigation gets the ring but mouse
/// clicks don't.
and FocusRing =
    { Color: ColorVar
      Width: string
      Offset: string
      Style: string }

/// Per-state × per-tone × per-slot interaction-token matrix plus the
/// global focus-ring shape. 7 tones × 4 states × 3 slots = 84 tokens +
/// 4 focus-ring globals = 88. Mirrors the
/// `--fuaran-tone-{tone}-{state}-{slot}` + `--fuaran-focus-ring-*` surface
/// from `HOST-STYLING-CHECKLIST.md` §1.6.
and Interaction =
    { FocusRing: FocusRing
      Hover: ToneStateMatrix
      Focus: ToneStateMatrix
      Active: ToneStateMatrix
      Disabled: ToneStateMatrix }

/// Tab-bar token surface — visual parity with the
/// SDK `Layout.Tabs.tabGroup` shape. The reference CSS defaults the colour
/// vars to `var(--fuaran-tone-brand-*)` references so themes that override
/// the brand stops carry through to the tab-bar automatically; explicit
/// overrides are also supported per the typed record. 7 vars total.
and TabBar =
    { PaddingY: string
      PaddingX: string
      IndicatorColor: ColorVar
      IndicatorHeight: string
      TextColor: ColorVar
      TextActiveColor: ColorVar
      TextHoverColor: ColorVar }

/// Segmented-control / radio-group token surface.
/// Drives the `FormFieldKind.SegmentedChoice` + `FilterKind.SegmentedFilter`
/// renderer emission. Defaults route through the tone palette so themes
/// that override `Tones.Brand` / `Tones.Subdued` carry through automatically
/// (matching the TabBar precedent). 4 vars total.
and Segmented =
    { Background: ColorVar
      ActiveBackground: ColorVar
      ActiveForeground: ColorVar
      DividerColor: ColorVar }

/// Responsive breakpoint thresholds (Phase 58) — the min-width boundaries
/// the renderer's reference CSS collapses layout at. Values are CSS
/// dimension strings (`"640px"`). Emitted as
/// `--fuaran-breakpoint-{sm|md|lg}` so consumer JS / container queries can
/// read the same thresholds the renderer uses. **CSS caveat:** a media
/// query condition cannot reference a custom property
/// (`@media (max-width: var(--x))` is invalid), so the reference-CSS
/// `@media` rules mirror these px values literally — the typed record is
/// the source of truth those rules are kept in sync with (the
/// `Defaults.theme` ↔ reference-CSS byte-mirror discipline). See
/// `docs/migrations/phase-58-mobile-responsive.md`.
and Breakpoints = { Sm: string; Md: string; Lg: string }

/// The full theme record. Apps construct one and pass it to
/// `Fuaran.UI.Renderer.Render.render` via the optional `?theme` parameter
/// (default: [[Defaults.theme]]); the renderer emits the variable bundle
/// from it into a `<style>` element it maintains.
///
/// `Fuaran.UI.Renderer.Theme.toCss` projects this record to the
/// `:root { --fuaran-...: ...; ... }` block. `Fuaran.UI.Renderer.Theme.toJson`
/// / `fromJson` serialise it to / from the same flat-JSON shape Nodes
/// use, so Portal-emitted Themes traverse the AI / wire pipeline.
and Theme =
    {
        Tones: Tones
        Spacing: Spacing
        FontScale: FontScale
        FontWeight: FontWeight
        LineHeight: LineHeight
        Radius: Radius
        ButtonSize: ButtonSize
        BorderWidth: string
        Interaction: Interaction
        /// Tab-bar shape tokens.
        TabBar: TabBar
        /// Segmented-control / radio-group tokens.
        Segmented: Segmented
        /// Responsive breakpoint thresholds (Phase 58).
        Breakpoints: Breakpoints
    }

/// Companion helpers for `Binding<'T>`. Merges with the RequireQualifiedAccess
/// DU under the `Binding.` prefix (the Option-module pattern), so call sites
/// read `Binding.projectSelectionField` beside `Binding.Selection`.
module Binding =

    /// Phase 632 — the declarative row-field projection a decoded `Selection`
    /// carries when its wire form names a `field`. The grid's default
    /// row-click writes the FULL row (a `Map<string, obj>`, the
    /// Transform-produced shape) to the SelectionStore; this accessor projects
    /// the named field off it so the binding stays scalar after a real click
    /// (the pre-632 identity accessor yielded the row itself — a loud
    /// non-scalar mismatch in scalar slots and Transform params). A missing
    /// field or a non-row value THROWS with a didactic — surfaced as the
    /// resolver's loud `Selection … accessor threw` (the Phase-427 mismatch
    /// posture, never a silent wrong value).
    ///
    /// Fable erases the `Map<_,_>` instantiation, so the Fable leg unboxes +
    /// reads directly (on the decoded path the row is always the
    /// Transform-produced `Map<string, obj>` — the same reasoning as the
    /// renderer's `projectRowFieldValue`); the .NET leg type-tests, and
    /// coerces the projected cell to `'T` via invariant `Convert.ChangeType`
    /// when the boxed representation differs (an `int` cell read into a
    /// `Binding<float>` slot, a numeric field shown in a text slot).
    let projectSelectionField<'T> (field: string) : obj -> 'T =
        fun (raw: obj) ->
#if FABLE_COMPILER
            if isNull raw then
                failwithf "Selection field '%s': the selected value is null, not a row" field
            else
                match Map.tryFind field (unbox<Map<string, obj>> raw) with
                | Some v -> unbox<'T> v
                | None -> failwithf "Selection field '%s' is not present on the selected row" field
#else
            match raw with
            | :? Map<string, obj> as row ->
                match Map.tryFind field row with
                | Some v ->
                    (try
                        unbox<'T> v
                     with :? System.InvalidCastException ->
                         unbox<'T> (
                             System.Convert.ChangeType(v, typeof<'T>, System.Globalization.CultureInfo.InvariantCulture)
                         ))
                | None -> failwithf "Selection field '%s' is not present on the selected row" field
            | _ -> failwithf "Selection field '%s': the selected value is not a row (Map<string, obj>)" field
#endif

/// The behavioural category a kind belongs to. Phase 692 unnested these from
/// `NodeKind` — they were a host-side envelope over a wire that has never had
/// them (`WIRE_FORMAT.md` §3.2: "a host-side classification recovered on decode").
/// What the renderer, AiTools and the theme bridge actually wanted was the
/// classification, not the nesting, so it is derived here instead.
[<RequireQualifiedAccess>]
type NodeCategory =
    | Layout
    | Display
    | Input
    | Visualisation
    /// `Custom` / `ErrorBoundary` / `Switch` / `FragmentDecl` / `FragmentRef` /
    /// `Mount` — the cases that never had a behavioural category.
    | Structural

/// Kind-tag introspection over `NodeKind` — the canonical bare-string name of
/// a node's kind ("Heading", "Stack", "Metric", …). Lives in the base package
/// (alongside `NodeKind`) so every tier can name a kind without a downstream
/// dependency: `Fuaran.UI.Ops.Introspect.kindName` delegates here, the renderer's
/// fragment applier enforces `HoleDecl.Slot` kind-constraints with it, and the AI
/// tool surface reads it. The tag vocabulary is a wire contract — `HoleDecl.Slot`'s
/// `kindConstraint` string is matched against exactly these names, so a new
/// `NodeKind` case adds its arm here in the same change that adds the case.
/// RequireQualifiedAccess: `Kind` is a very generic module name whose bare
/// `name` would otherwise leak into scope on `open Fuaran.UI.Types`.
[<RequireQualifiedAccess>]
module Kind =
    /// The canonical kind-tag string of a node's kind. Total over `NodeKind`;
    /// `DataGrid` intentionally tags as `"Grid"` (its wire name).
    let name (kind: NodeKind<'Msg>) : string =
        match kind with
        | NodeKind.Box _ -> "Box"
        | NodeKind.SplitPanel _ -> "SplitPanel"
        | NodeKind.Tabs _ -> "Tabs"
        | NodeKind.Stepper _ -> "Stepper"
        | NodeKind.SummaryList _ -> "SummaryList"
        | NodeKind.Disclosure _ -> "Disclosure"
        | NodeKind.Modal _ -> "Modal"
        | NodeKind.ScrollArea _ -> "ScrollArea"
        | NodeKind.Heading _ -> "Heading"
        | NodeKind.Markdown _ -> "Markdown"
        | NodeKind.Metric _ -> "Metric"
        | NodeKind.Badge _ -> "Badge"
        | NodeKind.Sparkline _ -> "Sparkline"
        | NodeKind.Callout _ -> "Callout"
        | NodeKind.Progress _ -> "Progress"
        | NodeKind.Skeleton _ -> "Skeleton"
        | NodeKind.LabelValueRow _ -> "LabelValueRow"
        | NodeKind.Fact _ -> "Fact"
        | NodeKind.Link _ -> "Link"
        | NodeKind.Image _ -> "Image"
        | NodeKind.List _ -> "List"
        | NodeKind.Toast _ -> "Toast"
        | NodeKind.CodeBlock _ -> "CodeBlock"
        | NodeKind.Math _ -> "Math"
        | NodeKind.Drawing _ -> "Drawing"
        | NodeKind.Form _ -> "Form"
        | NodeKind.Filters _ -> "Filters"
        | NodeKind.Button _ -> "Button"
        | NodeKind.FileUpload _ -> "FileUpload"
        | NodeKind.Select _ -> "Select"
        | NodeKind.DataGrid _ -> "Grid"
        | NodeKind.Chart _ -> "Chart"
        | NodeKind.Map _ -> "Map"
        | NodeKind.Custom _ -> "Custom"
        | NodeKind.ErrorBoundary _ -> "ErrorBoundary"
        | NodeKind.Switch _ -> "Switch"
        | NodeKind.FragmentDecl _ -> "FragmentDecl"
        | NodeKind.FragmentRef _ -> "FragmentRef"
        | NodeKind.Mount _ -> "Mount"

    /// The behavioural category of a kind — derived, not stored (Phase 692).
    let category (kind: NodeKind<'Msg>) : NodeCategory =
        match kind with
        | NodeKind.Box _
        | NodeKind.SplitPanel _
        | NodeKind.Tabs _
        | NodeKind.Stepper _
        | NodeKind.SummaryList _
        | NodeKind.Disclosure _
        | NodeKind.Modal _
        | NodeKind.ScrollArea _ -> NodeCategory.Layout
        | NodeKind.Heading _
        | NodeKind.Markdown _
        | NodeKind.Metric _
        | NodeKind.Badge _
        | NodeKind.Sparkline _
        | NodeKind.Callout _
        | NodeKind.Progress _
        | NodeKind.Skeleton _
        | NodeKind.LabelValueRow _
        | NodeKind.Fact _
        | NodeKind.Link _
        | NodeKind.Image _
        | NodeKind.List _
        | NodeKind.Toast _
        | NodeKind.CodeBlock _
        | NodeKind.Math _
        | NodeKind.Drawing _ -> NodeCategory.Display
        | NodeKind.Form _
        | NodeKind.Filters _
        | NodeKind.Button _
        | NodeKind.FileUpload _
        | NodeKind.Select _ -> NodeCategory.Input
        | NodeKind.DataGrid _
        | NodeKind.Chart _
        | NodeKind.Map _ -> NodeCategory.Visualisation
        | NodeKind.Custom _
        | NodeKind.ErrorBoundary _
        | NodeKind.Switch _
        | NodeKind.FragmentDecl _
        | NodeKind.FragmentRef _
        | NodeKind.Mount _ -> NodeCategory.Structural
