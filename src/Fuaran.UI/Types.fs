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

type Orientation =
    | Vertical
    | Horizontal

type IconSource = IconSource of string


[<RequireQualifiedAccess>]
type BadgeVariant =
    | Neutral
    | Brand
    | Success
    | Warning
    | Critical
    | Info

[<RequireQualifiedAccess>]
type ButtonVariant =
    | Primary
    | Secondary
    | Tertiary
    | Destructive

/// Presentation shape for `NodeKind.Image` (Phase 287). `Default` is a
/// plain in-flow `<img>`; `Avatar` is a circular crop (the "user picture"
/// shape that was previously impossible without a Custom escape); `Rounded`
/// is a soft-cornered rectangle. Bounded by design — the renderer maps each
/// to a `fuaran-image-{variant}` class; no free-form CSS escape.
[<RequireQualifiedAccess>]
type ImageVariant =
    | Default
    | Avatar
    | Rounded

/// Scroll axis for `NodeKind.ScrollArea` (Phase 289). Selects which
/// overflow axis the container clips + scrolls: `Vertical` → `overflow-y`,
/// `Horizontal` → `overflow-x`, `Both` → both. The renderer maps each to a
/// `fuaran-scrollarea-{axis}` class.
[<RequireQualifiedAccess>]
type ScrollOrientation =
    | Vertical
    | Horizontal
    | Both

/// Temporal breadth for `FormFieldKind.Date` (Phase 288). Selects the native
/// HTML control the renderer emits: `Date` → `<input type=date>`, `Time` →
/// `<input type=time>`, `DateTime` → `<input type=datetime-local>`. The bound
/// value is always an ISO-8601 string on the wire (`YYYY-MM-DD` /
/// `HH:MM` / `YYYY-MM-DDTHH:MM`) regardless of variant.
[<RequireQualifiedAccess>]
type DateVariant =
    | Date
    | Time
    | DateTime

/// Presentation mode for `NodeKind.Math` (Phase 293). `Inline` flows the
/// equation within surrounding text (a `<span>`); `Block` is a centred display
/// equation on its own line (a `<div>`). The renderer's deterministic fallback
/// emits the raw LaTeX source in the matching container; KaTeX upgrades it
/// client-side post-hydration (outside the parity output).
[<RequireQualifiedAccess>]
type MathDisplay =
    | Inline
    | Block

[<RequireQualifiedAccess>]
type ColumnWidth =
    | Auto
    | Fixed of pixels: int
    | Flex of weight: float

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

type ErrorKind =
    | NotFound
    | Forbidden
    | Server
    | Network
    | Timeout
    /// Client-side: the renderer encountered a `Binding` it could not
    /// resolve (accessor threw, value did not unbox to expected type,
    /// computed binding threw). Distinct from `Server` — the failure
    /// did not cross the wire. Downstream observability filters keyed
    /// on `Server` should NOT fire for these.
    | BindingResolution

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

[<RequireQualifiedAccess>]
type HashStrictness =
    /// Op-stream replay raises on hash mismatch; the renderer routes
    /// through `OnError` and writes a `FuaranCustomHashMismatch`
    /// structured payload via `IFuaranRuntime.Warn`.
    | StrictReplay
    /// Replay + renderer log through `IFuaranRuntime.Warn` and continue.
    /// Use when hash drift is informative but not fatal.
    | AdvisoryWarning
    /// Build-time enforcement (Phase 134). The validator computes a
    /// deterministic SHA-256 over the Custom body's *declared shape*
    /// (props schema + exposedNodeIds + moduleId / componentId — never
    /// runtime values) and surfaces FUARAN062 as an *Error* when the
    /// hand-set `Hash` disagrees, failing the build. This is the
    /// mechanical upgrade from the `AdvisoryWarning` posture — flip to
    /// `Enforced` once the body shape has stabilised. At render / replay
    /// time `Enforced` behaves like `StrictReplay` (the build gate is the
    /// primary enforcement; the runtime arm is the defensive floor).
    | Enforced

/// Content-identity envelope for a `NodeKind.Custom` body. `Algorithm`
/// is `"SHA256"` for v1 (forward-compatible — a future `"BLAKE3"` etc.
/// is additive); `Hash` is the hex-encoded digest of the renderer's
/// source. Callers that opt out leave the Custom field `None`.
type ContentHash =
    { Algorithm: string
      Hash: string
      Strictness: HashStrictness }

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

[<RequireQualifiedAccess>]
type AriaRole =
    | Button
    | Link
    | Dialog
    | Alert
    | Status
    | Banner
    | Navigation
    | Main
    | Form
    | Region
    | Heading
    | Progressbar
    | Tab
    | Tablist
    | Tabpanel
    | Custom of role: string

[<RequireQualifiedAccess>]
type LiveRegionKind =
    | Polite
    | Assertive
    | Off

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

[<RequireQualifiedAccess>]
type Motion =
    | None
    | PulseDuringLoad
    | FadeInOnMount
    | SlideInFromBelow
    | ShakeOnError
    | RotateOnRefresh
    | SlideInFromRight
    | ExpandCollapse

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
[<RequireQualifiedAccess>]
type HoleValueSpace =
    | IntRange of min: int * max: int
    | FloatRange of min: float * max: float
    | StringLen of minLen: int * maxLen: int
    | Enum of choices: string list
    | AnyString

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
[<RequireQualifiedAccess>]
type HoleDecl =
    | Value of name: string * space: HoleValueSpace * defaultValue: obj option
    | Slot of name: string * kindConstraint: string option
    | Repeat of name: string * countSpace: HoleValueSpace

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
[<RequireQualifiedAccess>]
type HostEffect =
    | Pure
    | ReadsHost
    | WritesHost

/// The determinism-source axis (mirrors Calc's `Volatility`): is the output a
/// pure function of its inputs, or does it depend on a clock / randomness / the
/// network?
[<RequireQualifiedAccess>]
type DeterminismSource =
    | Deterministic
    | Clock
    | Random
    | Network

/// A total two-axis effect class. Defaults to pure-deterministic for a
/// value-only fragment. Joined componentwise through composition: the wider
/// (more-effecting / less-deterministic) value wins on each axis (pure ∘ impure
/// = impure; deterministic ∘ clock = clock).
type EffectClass =
    { HostEffect: HostEffect
      Determinism: DeterminismSource }

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

type Node<'Msg> =
    {
        Id: NodeId
        Kind: NodeKind<'Msg>
        State: StateBehaviour<'Msg>
        Style: SemanticStyle
        /// Per-Node ARIA metadata. `None` (the default) emits no
        /// `aria-*` attributes; `Some` populates the matching `aria-label` /
        /// `aria-labelledby` / `role` / `aria-live` / `aria-hidden` per the
        /// renderer's emission rules. Smart constructors (`Fuaran.button`,
        /// `Fuaran.callout`, etc.) populate sensible defaults from
        /// `Defaults.Accessibility.X` so authors don't have to author this trait
        /// for the common cases.
        Accessibility: Accessibility option
        /// Per-Node animation token. `None` (the default) emits no
        /// `fuaran-motion-*` class. Set to `Some Motion.X` to opt into one of the
        /// 8 canonical motion tokens; the renderer projects to a
        /// `fuaran-motion-{token}` class on the outer wrapper and the reference
        /// CSS supplies keyframes for the four most common cases.
        Motion: Motion option
        /// AI-opaque consumer-side hatch for
        /// `data-*` / `aria-*` attributes (test hooks, analytics tags). `None`
        /// (the default) emits no extra attributes; `Some map` emits each
        /// entry as a DOM attribute via `prop.custom (key, value)` on the
        /// outer wrapper. The AI authoring guide explicitly forbids the AI
        /// populating this; the §4d JSON wire shape omits it on emit. Use
        /// `Node.withExtraAttribute` for validator-checked authoring.
        ExtraAttributes: Map<string, string> option
    }

and Accessibility =
    {
        /// Human-readable label for screen readers (`aria-label`). When `None`
        /// AND the Node's structural label field (e.g. `ButtonSpec.Label`) is
        /// non-empty, the renderer's per-Kind fallback uses the structural label.
        Label: Binding<string> option
        /// Reference to another Node whose text labels this one (`aria-labelledby`).
        /// Mutually exclusive with `Label` in practice — set one OR the other.
        LabelledBy: NodeId option
        /// Reference to another Node whose text describes this one (`aria-describedby`).
        DescribedBy: NodeId option
        /// ARIA role override; `None` lets the renderer pick a default based on
        /// the NodeKind (or omit `role` entirely for purely structural nodes).
        Role: AriaRole option
        /// `aria-live` politeness for dynamically-updating regions
        /// (notifications, toasts, async-loading regions).
        LiveRegion: LiveRegionKind option
        /// When `true`, emits `aria-hidden="true"` so screen readers skip this
        /// subtree. Bound to support conditional hiding (modal backdrop, etc.).
        Hidden: Binding<bool> option
    }

and [<RequireQualifiedAccess>] NodeKind<'Msg> =
    // ─── Phase 692: the vocabulary is FLAT ─────────────────────────────
    //
    // The four behavioural categories (Layout / Display / Input / Visualisation)
    // were a host-side envelope over these cases. `WIRE_FORMAT.md` §3.2 already
    // called them "a host-side classification recovered on decode" — the wire has
    // never had them, and the decoder rebuilt them from the flat `$type`. Keeping
    // them cost a level of nesting at every construction and match site, and put
    // the host tree one shape away from the IDL-generated one.
    //
    // The categories are not lost, only unnested: each is still recoverable from
    // the case (see `NodeKind.category`), which is all the renderer and AiTools
    // ever used them for.

    // ── Layout ──
    /// Phase 390 — the unified container primitive; absorbs the retired
    /// `Stack` / `GridLayout` / `Dashboard` / `Card` near-synonyms. Layout mode
    /// = how children arrange; Role = what the container means (emitted element
    /// + ARIA landmark + `fuaran-*` chrome). The author-facing `StackSpec` /
    /// `GridLayoutSpec` / `DashboardSpec` / `CardSpec` records survive as
    /// smart-ctor inputs that translate into `Box` — the wire consolidates, the
    /// authoring surface does not. See `docs/BOX-CONTAINER-UNIFICATION.md`.
    | Box of BoxSpec<'Msg>
    | SplitPanel of SplitPanelSpec<'Msg>
    | Tabs of TabsSpec<'Msg>
    | Stepper of StepperSpec<'Msg>
    /// Feliz-parity additive: single-card container
    /// of label/value rows (typically `NodeKind.LabelValueRow` children).
    /// Distinct shape from `Card` — no internal per-child padding, divider
    /// rules between children, optional section heading. Closes the
    /// "list-of-stats-in-one-card" gap that Feliz expresses
    /// as a hand-rolled `<div class="flex justify-between">` per row.
    | SummaryList of SummaryListSpec<'Msg>
    /// Typed accordion / collapsible primitive.
    /// Renders as HTML-native `<details>` / `<summary>` so the open/closed
    /// toggle works without React state; the `Open` binding overlays
    /// controlled-mode semantics for hosts that need model-driven state.
    /// Closes the "long itemised disclosure list" Feliz
    /// escape — see `DisclosureSpec` for the field-level rationale.
    | Disclosure of DisclosureSpec<'Msg>
    /// Out-of-flow overlay container (Phase 289) — the
    /// single most-missed primitive. Carries an `Open` `Binding<bool>`, an
    /// optional heading, a `Dismissable` flag, an `OnDismiss` action, and a
    /// child subtree. Renders inline (no React portal) with `role="dialog"` +
    /// `aria-modal`, positioned + z-indexed by CSS so SSR and CSR emit
    /// byte-identical structure (no hydration mismatch). Focus management is an
    /// additive client-only enhancement that does not alter the hydrated DOM —
    /// see the overlay render-fidelity contract in docs/SSR.md.
    | Modal of ModalSpec<'Msg>
    /// Overflow / scroll container (Phase 289) — the
    /// in-flow container the fidelity check still lacked. `Orientation` selects
    /// the scroll axis; optional `MaxHeight` / `MaxWidth` (pixels) bound the
    /// viewport. Overflow clipping + scrollbar are a genuine cross-host
    /// divergence point pinned by the SSR-parity corpus.
    | ScrollArea of ScrollAreaSpec<'Msg>


    // ── Display ──
    | Heading of HeadingSpec
    | Markdown of MarkdownSpec
    | Metric of MetricSpec
    | Badge of BadgeSpec
    | Sparkline of SparklineSpec
    | Callout of CalloutSpec
    | Progress of ProgressSpec
    // Skeleton is a renderer-emitted state placeholder; authors compose it
    // via `Fuaran.skeleton` for the OnLoading slot. §4c lines 504–542.
    | Skeleton of SkeletonSpec
    /// A single
    /// label-left / value-right row, baseline-aligned. The primitive
    /// `SummaryList` consumes; can stand alone too. Honours `StateBehaviour`
    /// `OnLoading` / `OnError` slots the same way `Metric` does (the resolver
    /// runs against `Source`).
    | LabelValueRow of LabelValueRowSpec
    /// A labeled TEXT fact — "Patient: Alice Smith", "Policy: POL-99382-X".
    /// The complementary kind to `Metric` (numeric-only, trendable) and
    /// `LabelValueRow` (numeric row): `Fact` is where a text-valued fact
    /// lives. Added 2026-07-17 after the launch eval showed every frontier
    /// model forcing text facts into `Metric.Source: Binding<float>`
    /// (~130 cells, 6 tasks at 0%): the type error was correct — the missing
    /// kind was the defect. `Value` is a `TextSource`, so static text,
    /// host-bound values (`Bound`), and i18n all ride the vocabulary models
    /// already use for labels; no new binding surface.
    | Fact of FactSpec
    /// A crawlable hyperlink primitive (Phase 139). Renders a real
    /// `<a href>` in both the client and server renderers — followable
    /// with JavaScript disabled and visible to search-engine crawlers,
    /// unlike `NodeKind.Button` + `Action.Navigate` (which is the SPA
    /// client-routing gesture, wired as an onClick through the runtime).
    /// Reach for `Link` for real destinations (SEO, no-JS); reach for
    /// `Button` + `Action.Navigate` for stateful in-app routing.
    | Link of LinkSpec
    /// A standalone image primitive (Phase 287). `IconSource`
    /// is a *field* on other specs, but there was no image *node* — embedding
    /// one meant a Markdown escape or a Custom. Renders a real `<img>` in both
    /// renderers with `src` routed through `Sanitize.sanitizeUrlOrBlank` and a
    /// mandatory `Alt` text (accessibility floor). The `Variant` covers the
    /// Avatar (circular) case the audit flagged.
    | Image of ImageSpec
    /// A structured item list (Phase 287) — distinct
    /// from `Table` (tabular), `SummaryList` (label/value rows), and Markdown
    /// bullets (free text). Renders `<ul>` / `<ol>` of `<li>` per item.
    | List of ListSpec
    /// A transient overlay notification surface (Phase
    /// 289). The DECLARATIVE, in-tree, SSR-rendered counterpart to the
    /// imperative `Action.Notify` (which a host maps to ephemeral chrome with no
    /// tree node). Reach for `Toast` when the notification is model-driven +
    /// bound to an `Open` state that hydrates cleanly; reach for `Action.Notify`
    /// for fire-and-forget host chrome. Renders inline (no portal) with
    /// `role="status"` / `aria-live`, positioned by CSS — see the overlay
    /// render-fidelity contract in docs/SSR.md.
    | Toast of ToastSpec
    /// A first-class code-display primitive (Phase 290).
    /// Owns a DETERMINISTIC `<pre><code>` structure (HTML-escaped, no markdown
    /// library) byte-identical across all hosts + SSR. Syntax highlighting is a
    /// client-only post-hydration enhancement (targets the `language-{x}`
    /// class), explicitly OUTSIDE the parity byte-diff. Carries a `Language`
    /// tag, optional line numbers + highlight ranges, and a copy affordance.
    | CodeBlock of CodeBlockSpec
    /// A LaTeX math primitive (Phase 293). `Source` is
    /// deterministic + parity-clean as data; the parity-checked render is the
    /// raw escaped source in a known container. KaTeX upgrades it client-only
    /// post-hydration, OUTSIDE the byte-diff — the no-JS / SSR reader sees the
    /// source fallback, the JS reader sees rendered math, no parity hazard.
    | Math of MathSpec
    /// A bounded, typed vector-graphics primitive (Phase 524) — the shared
    /// render target every `Chart` lowers to (Phase 526) and the reusable
    /// substrate for maps/diagrams. Carries a closed, typed `Shape` DU (no raw
    /// SVG markup, no `Path`/`d` escape hatch — the opposite of `Custom`), a
    /// `ViewBox` coordinate space, and style bindings. Naming is chosen for the
    /// data-science audience: `Drawing` (not `Vector`, a numeric array), and
    /// its shapes spell out `Rectangle` (not `Rect`) / `Label` (not `Text`).
    /// The first-party inline-SVG renderer arrives in Phase 525; 524 is the
    /// wire vocabulary + codec + corpus only. See
    /// docs/CHARTS-DRAWING-PRIMITIVE-DESIGN.md (D1, §3, §5).
    | Drawing of DrawingSpec


    // ── Input ──
    | Form of FormSpec<'Msg>
    | Filters of FilterSpec<'Msg> list
    | Button of ButtonSpec<'Msg>
    | FileUpload of FileUploadSpec<'Msg>
    | Select of SelectSpec<'Msg>

    // ── Vis ──
    | DataGrid of GridSpec<'Msg>
    | Chart of ChartSpec<'Msg>
    // Phase 393 — the `Table` case is retired; static read-only tables are the
    // `GridSpec.StaticRows` mode of `DataGrid`. A legacy `Table` wire tag
    // decode-upgrades into a read-only `DataGrid` (never re-encodes as `Table`).
    | Map of MapSpec<'Msg>

    /// The existing escape gains two additive
    /// optional safety surfaces. `contentHash` lets op-stream replay +
    /// renderer verify the body's identity hasn't drifted from its
    /// registered renderer; `exposedNodeIds` declares which interior
    /// NodeIds the Custom body exposes so the layout observer / AiTools
    /// introspection / structural ops can still address them. Callers that
    /// opt out pass `None` + `[]` — see `Fuaran.custom` smart-ctor.
    | Custom of
        moduleId: string *
        componentId: string *
        props: Map<string, JVal> *
        contentHash: ContentHash option *
        exposedNodeIds: NodeId list
    /// Render-time error boundary. Wraps a child
    /// subtree and renders the `Fallback` subtree when the child throws
    /// during render. Pairs with the per-node render guard (`Render.fs`)
    /// that catches throws inside individual leaves — the boundary is the
    /// explicit, AI-emittable shape for "if this subtree breaks, render
    /// this other thing instead", while the per-node guard is the always-on
    /// floor that keeps one bad leaf from blanking the page. Nested
    /// boundaries are permitted (inner boundaries swallow their own subtree
    /// failures; outer boundaries see the failure only if the inner
    /// boundary's fallback itself throws). See `Fuaran.errorBoundary`
    /// smart-ctor.
    | ErrorBoundary of ErrorBoundarySpec<'Msg>
    /// State-bound conditional-child primitive (Phase 392). Holds a
    /// `StateKey` (a reactive StateStore key — the Phase 106 substrate), an
    /// ordered list of `(matchValue, child)` `Cases`, and a `Default` child.
    /// At render time the renderer resolves the state value at `StateKey`,
    /// compares its string form against each case's `matchValue` in order
    /// (first match wins — the `FragmentDecl` name-collision precedent), and
    /// renders that case's child; if none match it renders `Default`. State
    /// transitions arrive as ordinary typed `Action.SetState` actions through
    /// the existing default-deny policy gate (FGP 3 — no new dispatch path);
    /// the client re-renders the matching case when the key changes, the
    /// server/SSR renders the initial match. This is the kernel-power piece
    /// that completes the expressive floor — conditional regions, wizard
    /// panes, empty-state alternatives, mode toggles — and the minimalist
    /// path's own inflation control: many "a container that shows X or Y"
    /// requests are `Switch` compositions rather than new vocabulary (see
    /// `docs/VOCABULARY.md` §1.2). See `Fuaran.switch` smart-ctor.
    | Switch of SwitchSpec<'Msg>
    /// Named reusable-subtree primitive — the
    /// declaration half. Registers `Body` under the fragment name `Name`
    /// for any `FragmentRef` reachable in the same tree to expand. The
    /// decl itself renders nothing (`Body` is the *template*; visible
    /// output happens at the ref site, not the decl site). Choose this
    /// over re-emitting a 200-node subtree when the same shape appears
    /// in two or more places — emission cost is one decl + N short refs
    /// instead of N full bodies. See `Fuaran.fragmentDecl` smart-ctor.
    ///
    /// Scoping: the resolver walks the whole tree once before render,
    /// collecting `FragmentDecl` Bodies into a `Map<FragmentId, _>`.
    /// First-declaration-wins on name collision; the validator
    /// (FUARAN056) flags repeated names. Refs resolve against the same
    /// map; unresolved refs render a labelled placeholder and FUARAN057
    /// flags them at build time. Cyclic references — fragment A's body
    /// transitively references A — render a labelled placeholder and
    /// FUARAN058 flags them.
    | FragmentDecl of FragmentDeclSpec<'Msg>
    /// Named reusable-subtree primitive — the
    /// reference half. At render time, the resolver substitutes the
    /// referenced fragment's Body with interior `data-fuaran-node-id`
    /// values namespaced by the ref's own NodeId (so multiple refs to
    /// the same fragment produce DOM-unique addressable ids:
    /// `ref1.btn` / `ref2.btn` rather than the bare `btn`).
    | FragmentRef of FragmentRefSpec<'Msg>
    /// Isolation/embedding boundary (Phase 265, §4o) — mounts an
    /// isolated guest subtree at this point with its own message space,
    /// scoped state + runtime, op-stream, and per-mount default-deny policy
    /// gate (iframe-like composition). The guest's `'GuestMsg` lives *behind*
    /// the mount (resolved by the orchestration tier) so the host `'Msg` stays
    /// monomorphic; the guest reaches the host as an `Action<obj>` via
    /// `MountSpec.OnBubble`. The canonical mechanism is the scope tier — NOT a
    /// `Node<'Host,'Guest>` generics change. The guest's own render tree is not
    /// carried here (opaque interior, like `Custom`); the guest `Node<obj>` is
    /// produced host-side by the orchestration guest loader from the scope.
    /// `Fuaran.embed` is the typed same-`'Msg` same-process convenience that
    /// lowers to a `Mount`. See `Fuaran.mount` smart-ctor.
    | Mount of MountSpec<'Msg>

and ErrorBoundarySpec<'Msg> =
    { Child: Node<'Msg>
      Fallback: Node<'Msg> }

/// See `NodeKind.Switch` (Phase 392). The state-bound conditional-child spec.
/// `StateKey` names the reactive StateStore key whose value selects the case;
/// `Cases` is an ordered list of `(matchValue, child)` pairs — the renderer
/// resolves the state value's string form and renders the first case whose
/// `matchValue` equals it (first-match-wins, mirroring `FragmentDecl`'s
/// first-declaration-wins on name collision); `Default` renders when no case
/// matches (and is the SSR/first-paint surface before any `SetState`). Each
/// case encodes on the wire as a `{ "child": <Node>, "match": <string> }`
/// object; the whole kind is `{ "$type":"Switch", "cases":[…], "default":<Node>,
/// "stateKey":<string> }`. Duplicate `matchValue`s are a validator error
/// (FUARAN082) — the decoder accepts them (first-match-wins keeps decode
/// structural), the validator flags them. See `docs/VOCABULARY.md` for why
/// conditional regions compose over `Switch` rather than growing new kinds.
and SwitchSpec<'Msg> =
    { StateKey: string
      Cases: (string * Node<'Msg>) list
      Default: Node<'Msg> }

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
and FragmentDeclSpec<'Msg> =
    { Name: FragmentId
      Body: Node<'Msg>
      Holes: HoleDecl list
      Effect: EffectClass }

/// One bound argument at a `FragmentRef` (Phase 180). A `Value` binds a typed
/// scalar (validated against its hole's value-space at apply time); a `Slot`
/// binds a subtree the resolver substitutes hygienically.
and [<RequireQualifiedAccess>] FragmentArg<'Msg> =
    | Value of obj
    | Slot of Node<'Msg>

/// See `NodeKind.FragmentRef`. Phase 180 — `Args` binds holes by name (value
/// scalars + slot subtrees). An empty `Args` is the Phase 61 name-only ref (the
/// degenerate case), omitted on the wire so that shape stays byte-identical.
and FragmentRefSpec<'Msg> =
    { Name: FragmentId
      Args: Map<string, FragmentArg<'Msg>> }

/// See `NodeKind.Mount`. Phase 265 — the isolation/embedding boundary carrier (§4o). The runtime,
/// *stateful*, *scoped* counterpart to `FragmentRefSpec`: `Inputs` reuses the parameterised-fragment
/// hole-binding shape (`FragmentArg` — the config + initial host→guest state), `ScopeId` names the
/// guest runtime scope, `Channel` declares the out-channel (host↔guest coupling + optional message
/// shape), `OnBubble` is the host handler for a guest-bubbled `Action<obj>` (a closure — it renders
/// the `"<closure>"` sentinel on the wire and decodes to a no-op action), and `Capabilities` are the
/// per-mount default-deny policy tags the boundary's gate consults. The guest's own render tree is
/// NOT carried here — only a scope reference + serialised hole bindings (opaque interior, like
/// `Custom`); the guest `Node<obj>` is produced host-side by the orchestration guest loader.
and MountSpec<'Msg> =
    { ScopeId: string
      Inputs: Map<string, FragmentArg<'Msg>>
      Channel: GuestChannel
      OnBubble: obj -> Action<'Msg>
      Capabilities: CapabilityTag list }

/// The declared out-channel of a `Mount` (§4o.4). `Direction` bounds host↔guest coupling; the optional
/// `MessageShape` names the guest's message shape for validation + the capability gate.
and GuestChannel =
    { Direction: ChannelDirection
      MessageShape: string option }

/// `OutOnly` (the default, safe for untrusted guests) lets the guest bubble to the host but forbids the
/// host pushing messages into the guest; `TwoWay` additionally permits host→guest push (which couples
/// the two lifecycles — memo open Q2, resolved: OutOnly default, TwoWay opt-in per-mount).
and [<RequireQualifiedAccess>] ChannelDirection =
    | OutOnly
    | TwoWay

/// A per-`Mount` capability tag (§4o.4) — e.g. `CapabilityTag "notify"`, `CapabilityTag "call:reports.*"`.
/// The boundary's policy gate consults these before letting a guest `Action<obj>` reach the host tree; an
/// empty list is default-deny of every host-affecting action.
and CapabilityTag = CapabilityTag of string

// ─── Layout — semantic intent, not pixel-pushing ────────────────────
//
// Per Defect (3) resolution: each LayoutKind case carries its own spec
// record with `Children: Node<'Msg> list` as a regular record field.

/// §4b (Phase 390) — the unified container spec. `Layout` names how children
/// arrange; `Role` names what the container means (drives the emitted element,
/// ARIA landmark, and `fuaran-*` chrome). `Heading` carries the retired `Card`
/// heading (emitted only when `Some`). See `docs/BOX-CONTAINER-UNIFICATION.md`.
and BoxSpec<'Msg> =
    { Layout: BoxLayout
      Role: BoxRole
      Heading: TextSource option
      Children: Node<'Msg> list }

/// How a `Box` arranges its children.
and [<RequireQualifiedAccess>] BoxLayout =
    /// Flex flow — the retired `Stack`. `Direction` = main axis; `Wrap` allows
    /// children to wrap at narrow widths. `Gap` is the canonical inter-child
    /// spacing control (`None` ⇒ omitted on the wire ⇒ byte-identical for
    /// existing trees); it is the mechanism that obsoleted the retired `Spacer`
    /// node (Phase 459).
    | Flex of FlexLayout
    /// Explicit grid — the retired `GridLayout`. `Cols` fixed count, or
    /// `TemplateColumns` verbatim `grid-template-columns` (`Some` ⇒ `Cols`
    /// ignored).
    | Grid of GridTemplate
    /// Responsive auto-tile — the retired `Dashboard`'s defining behaviour. The
    /// renderer owns the tiling via the `fuaran-dashboard` class; no
    /// author-supplied column count.
    | Auto

and FlexLayout =
    { Direction: Orientation
      Wrap: bool
      Gap: int option }

and GridTemplate =
    { Cols: int
      TemplateColumns: string option
      Gap: int option }

/// What a `Box` means — drives the emitted element, ARIA landmark, and chrome.
and [<RequireQualifiedAccess>] BoxRole =
    /// Plain grouping container — a bare `<div>`, no landmark. The retired
    /// `Stack` / `GridLayout` default.
    | Group
    /// Card chrome — `<section class="fuaran-card">` with optional heading. The
    /// retired `Card`.
    | Card
    /// Dashboard region — landmark `<section>`/`role="region"` with the
    /// auto-tiling `fuaran-dashboard` class. The retired `Dashboard`.
    | Dashboard
    /// Separator — the renderer emits `<hr class="fuaran-layout-separator">`
    /// with an implicit `role="separator"`. The canonical successor to the
    /// retired `Divider` node (Phase 459).
    | Separator

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

and SplitPanelSpec<'Msg> =
    { Weight: float
      Children: Node<'Msg> list }

and TabsSpec<'Msg> =
    {
        Orientation: Orientation
        Children: Node<'Msg> list
        /// The index of the currently-active tab. Driven via a `Binding<int>`
        /// (typically `binding.state "tabIndex" 0` for model-driven URL deep-
        /// linkable tabs, or `Binding.Static 0` for renderer-side-only state).
        /// Mirrors `StepperSpec.ActiveStep`'s shape.
        ActiveIndex: Binding<int>
        /// Optional dispatch when the user clicks a tab (Phase 426 — the
        /// control write-back default). `Some f` is called with the clicked
        /// index; the host typically dispatches a `SetActiveTab i` message and
        /// the model-driven `ActiveIndex` binding picks up the new value on
        /// the next render. `None` (the default, and the decoded / AI-authored
        /// shape) arms the write-back default: an `ActiveIndex` bound directly
        /// to `Binding.State`/`Binding.Filter` has the clicked index written
        /// to that slot, so decoded tabs switch panes with zero host code. A
        /// static `ActiveIndex` with no handler renders but never switches.
        OnSelect: (int -> Action<'Msg>) option
        /// Explicit per-tab header declarations. When
        /// `Some headers`, the renderer uses these for tab-label / icon /
        /// disabled emission and the header list must align 1:1 with
        /// `Children` by index (FUARAN047). When `None`, the
        /// Card-heading-inference path runs unchanged — back-compat preserved
        /// for authors who wrapped tab bodies in `Fuaran.card` with a
        /// `Heading`. New authoring should populate this field.
        TabHeaders: TabHeader list option
        /// Typed tab-tag overlay (parallel to
        /// `Children` by index). When `Some tags` AND `ActiveTag = Some _`,
        /// the renderer resolves the tag binding to a string, finds its
        /// position in `tags`, and uses that as the active index. The
        /// integer-indexed shape (`ActiveIndex` / `OnSelect`) stays the
        /// canonical wire form; the tag overlay is consumer ergonomics for
        /// model-side DU-typed active-tab state. FUARAN048 enforces
        /// `TabTags.Length = Children.Length`.
        TabTags: string list option
        /// Typed tab-tag overlay binding. Resolves to
        /// the currently-active tag string; the renderer maps it back to an
        /// integer index against `TabTags`. FUARAN049 warns when `Some` but
        /// `TabTags` is `None` (tag-binding has nothing to resolve against).
        ActiveTag: Binding<string> option
        /// Tag-overlay click dispatch. When `Some f`,
        /// the renderer fires `f tag` on tab click instead of (or in addition
        /// to) `OnSelect i`. Pairs with `TabTags` + `ActiveTag`. Since Phase
        /// 426 a `Some` closure rides the wire as an `"onSelectTag":"<closure>"`
        /// sentinel; when `None` and the tag overlay is populated, a writable
        /// `ActiveTag` binding has the clicked tag written to its slot (the
        /// tag-channel write-back default).
        OnSelectTag: (string -> Action<'Msg>) option
    }

/// Per-tab header declaration consumed by the tabs
/// renderer when `TabsSpec.TabHeaders` is populated. `Label` is the visible
/// tab text (resolves through the usual `TextSource` path — Literal / Bound /
/// I18n). `Icon` is an optional leading icon. `Disabled` is an optional
/// per-tab disabled binding (the renderer emits `aria-disabled` + skips
/// keyboard activation when resolved to `true`). Non-generic — the disabled
/// binding's resolution path does not need to flow `'Msg` through.
and TabHeader =
    { Label: TextSource
      Icon: IconSource option
      Disabled: Binding<bool> option }

and CardSpec<'Msg> =
    { Heading: TextSource option
      Children: Node<'Msg> list }

and StepperSpec<'Msg> =
    {
        ActiveStep: Binding<int>
        Children: Node<'Msg> list
        /// Dispatch when the user clicks a step header. The renderer calls
        /// this with the clicked step index; the host typically dispatches a
        /// `SelectStep i` message and the model-driven `ActiveStep` binding
        /// picks up the new value on the next render. Defaults to a no-op
        /// `Action.Chain []` — steps render and the active styling tracks
        /// `ActiveStep`, but no dispatch fires on click. Mirrors
        /// `TabsSpec.OnSelect`.
        OnSelect: int -> Action<'Msg>
    }

/// See `NodeKind.SummaryList`.
and SummaryListSpec<'Msg> =
    { Heading: TextSource option
      Children: Node<'Msg> list }

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
and DisclosureSpec<'Msg> =
    { Heading: TextSource
      Open: Binding<bool>
      OnToggle: (bool -> Action<'Msg>) option
      Children: Node<'Msg> list
      DefaultOpen: bool }

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
and ModalSpec<'Msg> =
    { Open: Binding<bool>
      Heading: TextSource option
      Dismissable: bool
      Children: Node<'Msg> list
      OnDismiss: Action<'Msg> option }

/// See `NodeKind.ScrollArea` (Phase 289). `Orientation` selects the scroll
/// axis; `MaxHeight` / `MaxWidth` (pixels, optional) bound the scroll viewport
/// — the renderer emits the matching `max-height` / `max-width` inline style
/// when set.
and ScrollAreaSpec<'Msg> =
    { Orientation: ScrollOrientation
      Children: Node<'Msg> list
      MaxHeight: int option
      MaxWidth: int option }

// ─── Display — pure presentation, no Msg ────────────────────────────

/// `Emphasis` bolds the row when it represents a total / highlight; `Help`
/// renders as small-print under the label when set.
and LabelValueRowSpec =
    { Label: TextSource
      Value: Binding<float>
      Format: CellFormat
      Emphasis: bool
      Help: TextSource option }

/// See `NodeKind.Fact`. `Emphasis` gives the value KPI-tile prominence
/// (inside a `Dashboard` role it tiles like a `Metric` card); `Help` is
/// small-print under the label, mirroring `LabelValueRow`.
and FactSpec =
    { Label: TextSource
      Value: TextSource
      Icon: IconSource option
      Tone: ToneVariant
      Emphasis: bool
      Help: TextSource option }

/// Heading variants beyond
/// the `<h{Level}>` Standard. Variants emit the same `<h{Level}>` tag but
/// add a `fuaran-heading-{variant}` class so the consumer's design tokens
/// can pick out eyebrow / caption / lead text without overriding the
/// `<h1..h6>` semantics.
and [<RequireQualifiedAccess>] HeadingVariant =
    | Standard
    | Eyebrow
    | Caption
    | Lead

and HeadingSpec =
    {
        Level: int
        Text: TextSource
        /// Defaults to `Standard` (no class fragment
        /// change, no behavioural change for default callers).
        Variant: HeadingVariant
    }

and MarkdownSpec = { Text: TextSource }

and BadgeSpec =
    { Label: TextSource
      Variant: BadgeVariant }

/// §4b — `NodeKind.Link`'s typed spec (Phase 139). `Href` is a
/// `Binding<string>` so the destination can be static or data-bound;
/// the renderer routes it through `Sanitize.sanitizeUrlOrBlank` (blocks
/// `javascript:` / `vbscript:` / raw `data:` before it reaches the DOM).
/// `Rel` / `Target` emit the matching anchor attributes when set
/// (`rel="noopener"`, `target="_blank"`, …); `Download` emits a bare
/// `download` attribute. All but `Href` / `Label` default off in
/// `Defaults.link`, so the AI emits only what differs.
and LinkSpec =
    { Href: Binding<string>
      Label: TextSource
      Rel: string option
      Target: string option
      Download: bool }

/// §4b — `NodeKind.Image`'s typed spec (Phase 287). `Src` is a
/// `Binding<string>` routed through `Sanitize.sanitizeUrlOrBlank` at render
/// time (blocks `javascript:` / `vbscript:` / `file:` and unknown schemes).
/// `Alt` is mandatory — the accessibility floor for a non-decorative image
/// (pass an empty `Literal ""` only for a purely decorative one). `Variant`
/// defaults to `Default` in `Defaults.image`.
and ImageSpec =
    { Src: Binding<string>
      Alt: TextSource
      Variant: ImageVariant }

/// §4b — `NodeKind.List`'s typed spec (Phase 287). `Items` is the ordered
/// list of item texts; `Ordered` selects `<ol>` (true) vs `<ul>` (false).
and ListSpec =
    { Items: TextSource list
      Ordered: bool }

/// §4n — `NodeKind.Toast`'s typed spec (Phase 289). The declarative,
/// in-tree, SSR-rendered notification surface. `Open` is the controlled
/// visibility binding (resolves `true` → shown); `Tone` selects the status
/// colour; `Dismissable` adds a close affordance. Renders inline (no portal),
/// positioned by CSS, with `role="status"` + an `aria-live` region — see the
/// overlay render-fidelity contract (docs/SSR.md). Defaults in `Defaults.toast`.
and ToastSpec =
    { Message: TextSource
      Tone: ToneVariant
      Open: Binding<bool>
      Dismissable: bool }

/// §4b — `NodeKind.CodeBlock`'s typed spec (Phase 290). `Code` is the raw
/// source (HTML-escaped at render, never markdown-parsed); `Language` is the
/// highlight hint emitted as `language-{Language}` on the `<code>` element;
/// `LineNumbers` toggles a CSS-counter gutter; `HighlightLines` is the set of
/// 1-based line numbers to mark (emitted as a deterministic
/// `data-highlight-lines` attribute the client enhancement reads); `Copyable`
/// renders a copy affordance. All default in `Defaults.codeBlock`.
and CodeBlockSpec =
    { Code: string
      Language: string
      LineNumbers: bool
      HighlightLines: int list
      Copyable: bool }

/// §4b — `NodeKind.Math`'s typed spec (Phase 293). `Source` is the LaTeX
/// string (escaped in the deterministic fallback render); `Display` selects an
/// inline `<span>` vs a block `<div>`. Defaults in `Defaults.math`.
and MathSpec =
    { Source: string; Display: MathDisplay }

and SparklineSpec = { Source: Binding<float seq> }

/// §4b — a 2-D user-space coordinate box for `NodeKind.Drawing` (Phase
/// 524). Mirrors SVG's `viewBox` (`minX minY width height`); the renderer
/// (Phase 525) maps it to the rendered `<svg viewBox>`. Plain floats — a
/// `Drawing` is a *resolved* geometric artefact (a chart lowers to concrete
/// coordinates), so geometry is static; only `DrawStyle` carries bindings.
and ViewBox =
    { MinX: float
      MinY: float
      Width: float
      Height: float }

/// A point in a `Drawing`'s user-space coordinate system (Phase 524).
and DrawPoint = { X: float; Y: float }

/// A typed drawing command for `Shape.Curve` (Phase 524) — the closed, typed
/// replacement for a raw SVG `d` path string. There is deliberately NO `Path`
/// shape and NO `d` string: `path` collides with the apply engine's
/// path-addressing and with binding paths, and a raw `d` string would
/// reintroduce an untyped escape hatch. A curve is an ordered list of these
/// typed commands instead. See docs/CHARTS-DRAWING-PRIMITIVE-DESIGN.md §3.
and [<RequireQualifiedAccess>] CurveCommand =
    | MoveTo of DrawPoint
    | LineTo of DrawPoint
    | CubicTo of control1: DrawPoint * control2: DrawPoint * endpoint: DrawPoint
    | QuadraticTo of control: DrawPoint * endpoint: DrawPoint
    | Close

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
and [<RequireQualifiedAccess>] TextAnchor =
    | Start
    | Middle
    | End

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
and DrawStyle =
    {
        Fill: Binding<string> option
        Stroke: Binding<string> option
        StrokeWidth: Binding<float> option
        Opacity: Binding<float> option
        TextAnchor: TextAnchor option
        FontSize: float option
        Emphasis: Emphasis option
        FontFamily: string option
        /// Phase 642 — a derivation-based mark identity for a data-bearing shape
        /// (object constancy): the chart lowering stamps `series-field|category-key`
        /// so a mark's identity survives row reorder, data refresh, and the wire —
        /// the anchor every future attachment addresses, never an ordinal index.
        /// Renderers emit it as `data-fuaran-mark`. Omitted-when-None (rule 4):
        /// chrome shapes and hand-authored drawings are byte-unchanged.
        MarkId: string option
    }

/// §4b — the closed, typed vector-graphics shape vocabulary for
/// `NodeKind.Drawing` (Phase 524). Every case is wire-survivable and
/// introspectable — the opposite of `NodeKind.Custom`: no raw SVG markup, no
/// arbitrary attribute bag, no `Path`/`d` escape hatch (see `CurveCommand`).
/// Coordinates are user-space floats in the drawing's `ViewBox`; `Group` nests
/// shapes under a shared style. Naming is chosen for the data-science
/// audience: `Rectangle` (not the abbreviated `Rect`), `Label` (not `Text`,
/// which overloads `FormFieldKind.Text` / `TextSource`).
and [<RequireQualifiedAccess>] Shape =
    | Group of children: Shape list * style: DrawStyle
    | Rectangle of x: float * y: float * width: float * height: float * cornerRadius: float option * style: DrawStyle
    | Line of x1: float * y1: float * x2: float * y2: float * style: DrawStyle
    | Polyline of points: DrawPoint list * style: DrawStyle
    | Polygon of points: DrawPoint list * style: DrawStyle
    | Curve of commands: CurveCommand list * style: DrawStyle
    | Circle of cx: float * cy: float * r: float * style: DrawStyle
    | Ellipse of cx: float * cy: float * rx: float * ry: float * style: DrawStyle
    | Label of x: float * y: float * text: TextSource * style: DrawStyle

/// §4b — `NodeKind.Drawing`'s typed spec (Phase 524). `ViewBox` is the
/// user-space coordinate box; `Shapes` is the ordered draw list (painter's
/// order); `Style` is the root style inherited by shapes that omit their own;
/// `Title` / `Description` are the optional accessible name / long description
/// the renderer (Phase 525) emits as `role="img"` + `<title>` / `<desc>`. This
/// is the shared render target every chart lowers to (Phase 526). Defaults in
/// `Defaults.drawing`.
and DrawingSpec =
    { ViewBox: ViewBox
      Shapes: Shape list
      Style: DrawStyle
      Title: TextSource option
      Description: TextSource option }

and SkeletonSpec = { Rows: int }

/// §4b lines 473–487 — Metric's typed spec record. All fields default in
/// `Defaults.metric`; AI emits only what differs.
and MetricSpec =
    { Label: TextSource
      // 0.2.0 rename (pre-launch clean break): the scalar displayed value is
      // `value` across Metric / LabelValueRow / Fact; `source` is reserved
      // for collection data feeds (grids/charts/options). No legacy alias.
      Value: Binding<float>
      Format: CellFormat
      Tone: ToneVariant
      Weight: StyleWeight
      Emphasis: Emphasis
      Trend: Binding<float> option
      TrendFormat: CellFormat option
      Icon: IconSource option
      Subtext: TextSource option }

/// §4k Q3.4 — tier/mode/status banner; sized for content blocks.
/// Toasts are separate (§4n overlay surfaces, session 3+).
and CalloutSpec =
    { Tone: ToneVariant
      Heading: TextSource option
      Body: TextSource
      Icon: IconSource option
      Dismissable: bool }

/// §4k Q3.4 — long-running async indicator. Use `Indeterminate = true`
/// with a `Caveat` when no honest 0..1 bound exists, per the §4k indeterminate-progress guidance.
and ProgressSpec =
    { Fraction: Binding<float>
      Label: TextSource option
      Caveat: TextSource option
      Indeterminate: bool
      Tone: ToneVariant }

// ─── Input — interactive, carries Msg via Action<'Msg> ──────────────

and ButtonSpec<'Msg> =
    {
        Label: TextSource
        OnClick: Action<'Msg>
        Variant: ButtonVariant
        Icon: IconSource option
        /// Hover tooltip surfaced via the browser's native `title`
        /// attribute. `None` means no tooltip (the v1 default). The text
        /// goes through the same `TextSource` resolution path as `Label`
        /// — i18n keys + bound expressions both work. The native `title`
        /// rendering is intentionally minimal (browser default styling);
        /// design-system-styled tooltip surfaces are a separate component
        /// concern and stay outside `ButtonSpec`.
        Tooltip: TextSource option
        /// Optional bound disabled-state. `None` (the default) means the
        /// button is always enabled; `Some binding` disables the button
        /// whenever the bound `bool` resolves `true` — the canonical
        /// "disabled while a calc is in flight" shape. The renderer emits
        /// the HTML `disabled` attribute when the binding resolves `true`
        /// and omits it otherwise. As an optional `Binding<bool>` slot it
        /// is ReplaceBinding-able + introspectable under the slot name
        /// `Disabled` (mirrors `MetricSpec.Trend` / `TabsSpec.ActiveTag`).
        Disabled: Binding<bool> option
    }

/// §4c idiom — filtered pickers. Authors filter inside the binding accessor,
/// not via a `Filter` field on the spec. The component is shape-stable across
/// "tier ≥ 1" / "recently active" / "owned by me" use cases.
and SelectSpec<'Msg> =
    {
        Label: TextSource
        Source: Binding<SelectOption list>
        Value: Binding<string option>
        /// Optional single-select change handler (Phase 426 — the control
        /// write-back default). `Some` dispatches on change exactly as before
        /// (`"onChange":"<closure>"` on the wire, byte-stable); `None` — the
        /// AI-authored / decoded shape — arms the renderer's write-back
        /// default: a `Value` bound directly to `Binding.State`/`Binding.Filter`
        /// has the chosen option written to that slot on change.
        OnChange: (string option -> Action<'Msg>) option
        Placeholder: TextSource option
        /// Optional bound disabled-state (Phase 130 — the interactive-state
        /// class-fix generalising `ButtonSpec.Disabled`). `None` (the default)
        /// means the select is always enabled; `Some binding` disables the
        /// `<select>` whenever the bound `bool` resolves `true`. The renderer
        /// emits the HTML `disabled` attribute when it resolves `true` and omits
        /// it otherwise. As an optional `Binding<bool>` slot it is
        /// ReplaceBinding-able + introspectable under the slot name `Disabled`
        /// (mirrors `ButtonSpec.Disabled` / `MetricSpec.Trend`).
        Disabled: Binding<bool> option
        /// Multi-select flag (Phase 291). `false` (the
        /// default) is single-select — `Value` / `OnChange` carry the chosen
        /// option, and the field is omitted on the wire (the degenerate case
        /// stays byte-identical to pre-multi-select fixtures). When `true`, the
        /// renderer emits a `<select multiple>` and the selection is carried by
        /// `Values` / `OnChangeMulti` (a list) instead of `Value` / `OnChange`.
        Multiple: bool
        /// The multi-select value binding (Phase 291).
        /// `None` for single-select (omitted on the wire); `Some binding` when
        /// `Multiple` — resolves to the list of selected option values.
        /// ReplaceBinding-able + introspectable under the slot name `Values`.
        Values: Binding<string list> option
        /// The multi-select change handler (Phase 291).
        /// Fires with the full selected-value list. `None` for single-select.
        /// Since Phase 426 a `Some` closure rides the wire as its own
        /// `"onChangeMulti":"<closure>"` sentinel; a multi-select whose
        /// handler is `None` falls to the write-back default against `Values`.
        OnChangeMulti: (string list -> Action<'Msg>) option
    }

/// A single Select option. `Value` is the wire id (stable, ASCII-safe);
/// `Label` is the displayed text.
and SelectOption = { Value: string; Label: TextSource }

/// Minimal viable FormSpec for session 3b. Per the §4k worked example: a
/// Form is an ordered list of fields + a submit Action. `FormField` carries
/// per-field `Kind` rather than a stringly-typed cell-type discriminator —
/// the renderer pattern-matches Kind to choose the input element + wire the
/// `onChange` handler back into typed `Action<'Msg>`.
and FormSpec<'Msg> =
    {
        Fields: FormField<'Msg> list
        OnSubmit: Action<'Msg>
        SubmitLabel: TextSource
        /// Optional bound disabled-state (Phase 130 — the interactive-state
        /// class-fix generalising `ButtonSpec.Disabled`). `None` (the default)
        /// leaves the form enabled; `Some binding` disables the whole form
        /// whenever the bound `bool` resolves `true` — the canonical "disable
        /// every field + submit while a calc is in flight" shape. The renderer
        /// wraps the fields + submit in a `<fieldset disabled>` (native HTML
        /// cascade), so every descendant control is disabled at once. As an
        /// optional `Binding<bool>` slot it is ReplaceBinding-able +
        /// introspectable under the slot name `Disabled`.
        Disabled: Binding<bool> option
    }

and FormField<'Msg> =
    { Id: string
      Label: TextSource
      Kind: FormFieldKind<'Msg>
      Required: bool
      Help: TextSource option }

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
and [<RequireQualifiedAccess>] FormFieldKind<'Msg> =
    | Text of value: Binding<string> * onChange: (string -> Action<'Msg>) option
    | Number of value: Binding<float> * onChange: (float -> Action<'Msg>) option
    | Checkbox of value: Binding<bool> * onToggle: (bool -> Action<'Msg>) option
    | Choice of
        options: Binding<SelectOption list> *
        value: Binding<string option> *
        onChange: (string option -> Action<'Msg>) option
    | TextArea of value: Binding<string> * onChange: (string -> Action<'Msg>) option * rows: int
    /// Parallel-additive Number case carrying optional `Min` /
    /// `Max` / `Step` constraints projected to HTML `min` / `max` / `step`
    /// attributes by the renderer (and consumed by the FUARAN051 validator
    /// rule to range-check a `Binding.Static` literal value). The existing
    /// `Number` case stays as-is so pattern matches and
    /// authors see no behavioural change.
    | RangedNumber of
        value: Binding<float> *
        onChange: (float -> Action<'Msg>) option *
        constraints: NumberFieldConstraints
    /// Parallel-additive Choice case rendering as a
    /// segmented control (Horizontal) or a vertical radio-button list
    /// (Vertical). Same `options` / `value` / `onChange` triple as
    /// `Choice`; the orientation field chooses the visual surface. The
    /// existing `Choice` case stays as-is — it remains the dropdown shape.
    /// Use `SegmentedChoice` when ≤5 options should be visible at once;
    /// reach for `Choice` otherwise. FUARAN045 warns on a static
    /// SegmentedChoice with > 7 options.
    | SegmentedChoice of
        options: Binding<SelectOption list> *
        value: Binding<string option> *
        onChange: (string option -> Action<'Msg>) option *
        orientation: Orientation
    /// Dual-thumb numeric range (0.2.0 — absorbs the retired
    /// `FilterKind.RangeFilter` in the filters-unification: one control
    /// vocabulary for forms and filter strips). Value is the (min, max) pair.
    | Range of
        value: Binding<float * float> *
        onChange: (float * float -> Action<'Msg>) option *
        constraints: NumberFieldConstraints option
    /// Date / time / datetime field (Phase 288) — the
    /// conspicuous hole in the Form vocabulary before Wave 43. The bound value
    /// is an ISO-8601 string (`YYYY-MM-DD` for `Date`, `HH:MM` for `Time`,
    /// `YYYY-MM-DDTHH:MM` for `DateTime`); `variant` chooses the native control;
    /// optional `Min` / `Max` (ISO strings) + `Step` (seconds) constraints
    /// mirror `RangedNumber` and project to the HTML `min` / `max` / `step`
    /// attributes.
    | Date of
        value: Binding<string> *
        onChange: (string -> Action<'Msg>) option *
        variant: DateVariant *
        constraints: DateFieldConstraints

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
and FilterSpec<'Msg> =
    { Name: string
      Label: TextSource
      Field: FormFieldKind<'Msg> }

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
and FileRef = { Id: string; Handle: obj option }

/// FileSelection is the metadata the browser exposes for an `<input type=file>`
/// change event. The actual `File` blob stays browser-side; `Ref` carries an
/// opaque handle to it (Phase 136) so `OnSelect` can chain
/// `Action.ReadFileBody` to ingest the body — the metadata + handle, never
/// the blob, are what the spec hands the author.
and FileUploadSpec<'Msg> =
    {
        Label: TextSource
        Accept: string list
        Multiple: bool
        OnSelect: FileSelection list -> Action<'Msg>
        /// Optional bound disabled-state (Phase 130 — the interactive-state
        /// class-fix generalising `ButtonSpec.Disabled`). `None` (the default)
        /// leaves the upload control enabled; `Some binding` disables the
        /// `<input type=file>` whenever the bound `bool` resolves `true`. The
        /// renderer emits the HTML `disabled` attribute when it resolves `true`.
        /// ReplaceBinding-able + introspectable under the slot name `Disabled`.
        Disabled: Binding<bool> option
    }

and FileSelection =
    {
        Name: string
        Size: int64
        MimeType: string
        /// Phase 136 — opaque handle to the selected file's blob, so `OnSelect`
        /// can chain `Action.ReadFileBody Ref encoding onRead` to ingest the
        /// body with no consumer-side `FileReader` interop. Carries the boxed
        /// browser `File` on browser hosts; only `Ref.Id` ever serialises.
        /// Constructed by the renderer when the change event fires.
        Ref: FileRef
    }

/// AG Charts-shaped chart spec. `Source` resolves to a row sequence; `XField`
/// and `YFields` name the row's property keys to plot. AG Charts adapter
/// (session 3b) feeds typed `Action<'Msg>` callbacks back; falling back to
/// plain `<canvas>` rendering for the demo when no adapter is wired.
and ChartSpec<'Msg> =
    {
        Source: Binding<obj seq>
        Kind: ChartKind
        XField: string
        YFields: string list
        Title: TextSource option
        OnPointClick: (obj -> Action<'Msg>) option
        /// When `true` and `Kind` is `Bar` or `Area`, the renderer's AG
        /// Charts adapter sets `series[i].stacked = true` so multiple
        /// `YFields` stack on top of each other (a single shared `xKey`
        /// stack group). Defaults to `false` (the renderer ships unstacked
        /// bars / areas if the field is omitted). For chart kinds that
        /// don't have a meaningful stacked notion (Line / Pie / Scatter /
        /// Heatmap), the adapter ignores the field.
        Stacked: bool
    }

and [<RequireQualifiedAccess>] ChartKind =
    | Line
    | Bar
    | Area
    | Pie
    | Scatter
    /// First-class heatmap chart kind. `XField` maps to the X-axis
    /// category (e.g. "Year"), `YFields` to the Y-axis category (one
    /// field, the row-key dimension); cell colour is derived from the
    /// remaining numeric field by the adapter's gradient logic. The
    /// AG Charts community-tier doesn't ship a native heatmap series;
    /// the renderer's adapter falls back to AG Charts Enterprise when
    /// available (declared as an `optionalDependency` in the consumer's
    /// package.json), else renders a labelled placeholder so the missing
    /// dependency is visible rather than silently broken.
    | Heatmap

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
and MapSpec<'Msg> =
    { Source: Binding<MapMarker seq>
      CentreLatitude: float
      CentreLongitude: float
      Zoom: int
      OnMarkerClick: (MapMarker -> Action<'Msg>) option }

and MapMarker =
    { Latitude: float
      Longitude: float
      Label: TextSource }

// ─── Visualisation — data-bound, complex ─────────────────────────────

/// Per Defect (1) resolution: row-typed fields are `obj`-erased at the
/// tree level. Authors construct a typed `GridSpecOf<'row,'Msg>` facade
/// (below) and `Fuaran.grid` boxes it into this shape. The renderer trusts
/// the per-Kind type-tag invariant.
and GridSpec<'Msg> =
    { Source: Binding<obj seq>
      // `RowKey` is optional (Phase 425) — the closure is an *override*, not the floor. `RowKeyField`
      // names a row property to project as the key; a decoded grid (no closure rides the wire) uses
      // it for stable row identity with zero host code. Closure wins when both present.
      RowKey: (obj -> string) option
      RowKeyField: string option
      Columns: ColumnErased<'Msg> list
      OnRowClick: (obj -> Action<'Msg>) option
      Editable: bool
      // Phase 393 — the static read-only mode. `Some (headers, rows)` marks this grid
      // as a static text table folded in from the retired `NodeKind.Table`: the
      // renderer emits semantic `<table>` markup from these `TextSource` cells and
      // ignores `Source` / `Columns`. `None` is the ordinary data-bound grid. A
      // legacy `Table` document decode-upgrades into a grid with this field set.
      StaticRows: (TextSource list * TextSource list list) option }

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

/// Tree-level row-erased Column. `Fuaran.grid` boxes a `Column<'row,'Msg>`
/// into this shape via `Column.erase`.
and ColumnErased<'Msg> =
    { Label: string
      // `Value` is optional (Phase 425) — the closure is an *override*, not the floor. `Field` names
      // a row property projected to the cell (via the row-field projection contract); a decoded grid
      // (no closure rides the wire) renders its cells from `Field` with zero host code. Closure wins
      // when both present.
      Value: (obj -> CellValue) option
      Field: string option
      Format: CellFormat
      Kind: CellKindErased<'Msg>
      Width: ColumnWidth }

/// What a single cell of data is, after the `Value` projection runs.
/// Pre-formatted strings break AG Grid's numeric sort (§4c idiom 3) — use
/// `Numeric` + a `CellFormat.Percent` / `Currency` / `Number` instead.
and [<RequireQualifiedAccess>] CellValue =
    | Numeric of float
    | Text of string
    | Bool of bool
    | Date of System.DateTimeOffset
    | Empty

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

/// Row-erased twin of CellKind. Smart-ctor `Column.erase` boxes the row
/// accessors before placing into the tree.
and [<RequireQualifiedAccess>] CellKindErased<'Msg> =
    | Text
    | Numeric
    | Date
    | Editable of ((obj * CellValue) -> Action<'Msg>)
    | Checkbox of get: (obj -> bool) * onToggle: (obj * bool -> Action<'Msg>)
    | Button of label: TextSource * onClick: (obj -> Action<'Msg>)
    | ButtonGroup of (TextSource * (obj -> Action<'Msg>)) list
    | Link of href: (obj -> string) * label: (obj -> TextSource)
    | Pill of label: (obj -> TextSource) * tone: (obj -> ToneVariant)
    | Progress of fraction: (obj -> float) * label: ((obj -> TextSource) option)
    | Custom of ((obj -> JVal) -> Node<'Msg>)

/// §4k Q3.3 — typed formatters; compound / derived strings stay in
/// `Column.Value`. Keeps numeric sort intact when a column displays numbers.
and [<RequireQualifiedAccess>] CellFormat =
    | None
    | Number of decimals: int option
    | Currency of code: string
    | Percent of decimals: int option
    | SignificantDigits of digits: int
    | Date of format: string
    | Custom of (CellValue -> string)

// ─── State behaviours — required slots, AI can't forget ──────────────

and StateBehaviour<'Msg> =
    { OnLoading: Node<'Msg> option
      OnEmpty: Node<'Msg> option
      OnError: (ErrorPayload -> Node<'Msg>) option }

and ErrorPayload =
    { Kind: ErrorKind
      Message: string
      CorrelationId: string }

// ─── Style — semantic, not CSS ───────────────────────────────────────

and SemanticStyle =
    {
        Tone: ToneVariant
        Weight: StyleWeight
        Emphasis: Emphasis
        /// §4b (Phase 147) — the node's named semantic *content role*, an
        /// additive bounded vocabulary the AI emits as intent
        /// (`Role = StyleRole.Data`). Projects a `fuaran-role-{role}` class
        /// fragment the host CSS owns; `StyleRole.None` (the default) emits
        /// nothing, so a tree authored before the field existed renders
        /// byte-identically. Distinct from `HeadingVariant` (which owns the
        /// heading-text eyebrow/caption/lead variants) — `StyleRole` tags the
        /// content role of *any* node.
        Role: StyleRole
        /// §4b (Phase 147) — the node's font *voice*: the display-voice
        /// (large, expressive, cover/hero) vs structural-voice (body, UI
        /// chrome) split the narrow `Tone/Weight/Emphasis` triple can't name.
        /// Projects a `fuaran-voice-{voice}` class fragment; `FontVoice.Default`
        /// (the default) emits nothing (byte-identical for existing trees).
        Voice: FontVoice
    }

/// §4b (Phase 147) — the bounded, additive-only semantic content-role
/// vocabulary. The AI emits a role as *intent*; the renderer projects a
/// stable `fuaran-role-{role}` class and the host CSS owns the pixels — no
/// raw style escape reaches the typed tree. Generalises the
/// `HeadingVariant.Eyebrow`/`Caption`/`Lead` precedent to any node.
/// **Additive-only post-ship** (like `LayoutFlag`/`StyleFlag`): adding a case
/// is a minor bump (existing matches gain an `FS0025` warning); redefining a
/// case breaks every prompt cache that pattern-matched it.
and [<RequireQualifiedAccess>] StyleRole =
    /// No declared role — the default; emits no class fragment.
    | None
    /// Small kicker / overline label (the named-scale `eyebrow` role) on a
    /// non-heading node. On a `Heading`, prefer `HeadingVariant.Eyebrow`.
    | Eyebrow
    /// Tabular / numeric data voice — figures, metrics, monospaced data.
    | Data
    /// Lead paragraph / standfirst — the intro voice above body copy.
    | Lede
    /// Small supporting caption / footnote on a non-heading node. On a
    /// `Heading`, prefer `HeadingVariant.Caption`.
    | Caption

/// §4b (Phase 147) — the bounded, additive-only font-voice vocabulary: the
/// display-vs-structural split. Projects a `fuaran-voice-{voice}` class.
/// Additive-only post-ship, same discipline as `StyleRole`.
and [<RequireQualifiedAccess>] FontVoice =
    /// No declared voice — the default; emits no class fragment.
    | Default
    /// Display voice — large, expressive headline / cover / hero type.
    | Display
    /// Structural voice — body copy + UI chrome (the workhorse voice).
    | Structural

and ToneVariant =
    | Default
    | Subdued
    | Brand
    | Success
    | Warning
    | Critical
    | Info

and StyleWeight =
    | Compact
    | Standard
    | Spacious

and Emphasis =
    | Quiet
    | Normal
    | Loud

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

and [<RequireQualifiedAccess>] TextSource =
    | Literal of string
    | Bound of Binding<string>
    | I18n of key: string * args: Map<string, JVal>

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
        | NodeKind.Custom(_, _, _, _, _) -> "Custom"
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
