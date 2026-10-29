module Fuaran.UI.Renderer.BindingResolver

// ============================================================================
//  Fuaran — binding resolution (§4b Binding<'T>, §4c idioms lines 591–602)
//
//  Resolves a typed `Binding<'T>` to its current `'T` value.  Authors write
//  `binding.query "totalRevenue" _.amount` and the renderer must produce a
//  current `float` to feed the Metric.  This file is the typed surface; the
//  data sources (query result cache, module state store, filter / selection
//  registry) live consumer-side and are passed in via `BindingSources`.
//
//  Session 3a scope: `Static`, `State`, and a stub `Query` that pulls from a
//  caller-supplied `Map<string, obj>`.  `Selection` and `Filter` need the
//  renderer to track which nodes have selections / filters active — that's
//  session 3b once the renderer has a runtime registry.  `Computed` is the
//  F#-only escape that doesn't serialise; sessions 3+ leave it best-effort.
//
//  Per-Defect (2) note: `Binding.Query` / `Binding.Selection` carry obj-erased
//  accessors at the tree level.  The resolver unboxes the captured value back
//  to `'T` via the stored closure.  Authors never see the obj boundary.
// ============================================================================

open Fuaran.UI.Types
open Fuaran.Core
open Fuaran.Compute

// This module lives at the typed-tree's obj-erasure boundary: a `Binding<'T>`
// resolves through boxed `obj` cells, and `null` is a first-class value there
// (an absent query result, a `Cell.Null`, a JSON null). The `isNull` tests and
// `null` match arms below are therefore load-bearing, and F# 10's nullness
// checker rejects them on a bare `obj` (FS3261). The project-wide
// `<Nullable>disable</Nullable>` does not travel under Fable — there the ENTRY
// project's setting governs the whole source graph — so the suppression is
// file-scoped here, per the obj-erasure `#nowarn` precedents in
// Fuaran.UI/Types.fs (`module Binding`) and Fuaran.fs. See `Sanitize.fs`.
#nowarn "3261"

/// The default `II18nResolver`. Pass-through identity:
/// returns the debug placeholder `[i18n:<key>]` for every key so missing
/// translations stay visually loud in dev. Apps wire a real resolver
/// (a platform host's i18n resolver, or a hand-rolled
/// gettext / ICU / template-string resolver) by replacing
/// `BindingSources.I18nResolver`.
///
/// Phase 213: the value itself moved to `Fuaran.UI.BindingSources` alongside
/// the record whose default it is; this is a re-export so
/// `BindingResolver.passthroughI18nResolver` keeps resolving for every
/// consumer that names it.
let passthroughI18nResolver: II18nResolver =
    Fuaran.UI.BindingSources.passthroughI18nResolver

/// Build an `II18nResolver` that consults a `Map<string, string>` catalog
/// and substitutes `{argName}` placeholders with values from the args map.
/// Convenience constructor for tests + the simple single-language case;
/// real apps typically plug in a richer resolver. Missing keys fall through
/// to the `[i18n:<key>]` debug shape.
let makeI18nResolver (catalog: Map<string, string>) : II18nResolver =
    { new II18nResolver with
        member _.Resolve(key, argsOpt) =
            match Map.tryFind key catalog with
            | Some template ->
                match argsOpt with
                | Some args ->
                    args
                    |> Map.fold
                        (fun (acc: string) (k: string) (v: obj) ->
                            let needle = "{" + k + "}"
                            let replacement = if isNull v then "" else string v
                            acc.Replace(needle, replacement))
                        template
                | None -> template
            | None -> sprintf "[i18n:%s]" key }

/// Data sources the renderer consults when it encounters a binding.
/// Consumers (consumer apps, a future AI consumer) provide
/// their own implementation; session 3a ships only the in-memory variant.
///
/// Phase 213: the record itself was PROMOTED to `Fuaran.UI` (data-only,
/// FSharp.Core only) so the renderer and `Fuaran.UI.AiTools` reference one
/// field set rather than two hand-duplicated ones — the duplication that had
/// already dropped `Locale` from the introspection probe. This abbreviation is
/// deliberate rather than a rename: `BindingResolver.BindingSources` is the
/// spelling every consumer annotates with, so the promotion is source- and
/// binary-compatible. Resolver logic stays here; ADD A FIELD in `Fuaran.UI`.
type BindingSources = Fuaran.UI.BindingSources

/// The empty `BindingSources` — useful for tests and for the renderer
/// scaffolding before consumer data plumbing lands. Re-exported from
/// `Fuaran.UI.BindingSources.empty` (Phase 213) so every
/// `BindingResolver.empty` call site is untouched.
let empty: BindingSources = Fuaran.UI.BindingSources.empty

/// The evaluation budget one `Binding.Transform` / `Binding.Expr` may spend
/// (Phase 1532).
///
/// A decoded tree declares its own pipeline: `Join`, `Window`, `Pivot`, an
/// arbitrary chain of steps, over a source the tree also names. On the client
/// that pipeline is evaluated in the reader's browser on every render AND on
/// every store notification that touches its channel keys — so a tree that
/// arrived over the wire decided how much work the reader's machine does, per
/// keystroke, with nothing between the declaration and the evaluator.
///
/// A budget is the thing between them. It is deliberately measured in the two
/// quantities a tree DECLARES — rows in, steps in the pipeline — rather than in
/// wall-clock time: elapsed time is a property of the machine, so a
/// time-budgeted evaluator refuses different trees on a phone and a workstation
/// and no author can predict which. Rows and steps are the same numbers
/// everywhere, so a refusal is reproducible and an author can act on it.
type TransformBudget =
    {
        /// Rows admitted into the pipeline.
        MaxRows: int
        /// Steps in the pipeline.
        MaxSteps: int
        /// `rows x steps` — the product, which is what actually bounds the work
        /// a chain does. Two limits alone would admit 100,000 rows through 64
        /// steps; the product is what refuses that while still admitting both a
        /// wide-and-shallow and a narrow-and-deep pipeline.
        MaxRowSteps: int
    }

[<RequireQualifiedAccess>]
module TransformBudget =

    /// The marker every budget refusal message begins with, so a host can
    /// recognise one without parsing prose. Named here rather than spelled at
    /// the raise site, because a message a caller matches on is a contract.
    [<Literal>]
    let RefusalMarker = "Transform evaluation budget exceeded"

    /// The shipped budget.
    ///
    /// Sized against what a UI can usefully SHOW rather than against what an
    /// evaluator can survive: a grid a reader scrolls is thousands of rows, not
    /// hundreds of thousands, and a declared pipeline in a rendered tree is a
    /// handful of steps. Every fixture in the estate's own corpus is orders of
    /// magnitude inside it, which is the property that matters — a budget that
    /// refuses real trees is one a host turns off.
    let defaults: TransformBudget =
        { MaxRows = 50_000
          MaxSteps = 64
          MaxRowSteps = 500_000 }

    /// A budget that admits everything — the pre-Phase-1532 behaviour, for a
    /// host evaluating trees it authored itself. Reached BY NAME, so a grep
    /// finds every host that opted out.
    let unbounded: TransformBudget =
        { MaxRows = System.Int32.MaxValue
          MaxSteps = System.Int32.MaxValue
          MaxRowSteps = System.Int32.MaxValue }

    /// Refuse, naming the quantity that overran and the limit it overran —
    /// `Ok ()` when the work is within budget.
    ///
    /// Total and pure over its three arguments, so every combination is
    /// pinnable without an evaluator.
    let check (budget: TransformBudget) (rows: int) (steps: int) : Result<unit, string> =
        let refuse (what: string) (observed: int) (limit: int) =
            Error(sprintf "%s: %s %d exceeds the limit of %d" RefusalMarker what observed limit)

        if rows > budget.MaxRows then
            refuse "input rows" rows budget.MaxRows
        elif steps > budget.MaxSteps then
            refuse "pipeline steps" steps budget.MaxSteps
        // The product is computed in int64 because the operands are ints a tree
        // supplies: `50_000 * 50_000` overflows `int` to a NEGATIVE number, and a
        // negative product passes every `>` comparison. A bound that overflows is
        // an absent bound with the shape of a present one.
        elif int64 rows * int64 steps > int64 budget.MaxRowSteps then
            Error(
                sprintf
                    "%s: %d input rows through %d pipeline steps is %d row-steps, over the limit of %d"
                    RefusalMarker
                    rows
                    steps
                    (int64 rows * int64 steps)
                    budget.MaxRowSteps
            )
        else
            Ok()

/// Resolution result.  Renderer code treats `NotResolved` as the trigger for
/// the `OnLoading` state behaviour; `Resolved` flows into the component body;
/// `Error` flows into `OnError`.
type Resolution<'T> =
    | Resolved of 'T
    | NotResolved
    | Errored of message: string
    /// `Binding.I18n` resolution returned the debug-placeholder
    /// shape (`[i18n:<key>]`), signalling the resolver had no translation for
    /// the key. Distinct from `NotResolved` (a data-source absence) so the
    /// renderer + Catalog can surface missing translations specifically.
    | I18nUnresolved of key: string

// ─── Transform cell bridging (Phase 282/424; module-level since Phase 632 so the
//     rows path and the scalar path share one vocabulary) ─────────────────────

/// Unbox a `Fuaran.Core.Cell` to the boxed row-cell `obj` shape rows carry.
let private cellToObj (c: Fuaran.Core.Cell) : obj =
    match c with
    | Fuaran.Core.Int i -> box i
    | Fuaran.Core.Float f -> box f
    | Fuaran.Core.Cell.Bool b -> box b
    | Fuaran.Core.Str s -> box s
    | Fuaran.Core.Cell.Date s -> box s
    | Fuaran.Core.Cell.Timestamp s -> box s
    | Fuaran.Core.Cell.Null -> null
    // Fuaran.Core 0.33.0 (D72): a decimal cell carries canonical decimal text, which is
    // also its wire form (a JSON string). A slot with no coercion of its own takes that
    // text; a ROW keeps the type (`cellToRowObj`).
    | Fuaran.Core.Cell.Decimal s -> box s

/// A result cell as a `Row` carries it. Phase 2006: the Transform schema typed this column
/// decimal, so the row keeps that type as a `DecimalCell` rather than a bare string, and the
/// grid, the formatter and the chart read an exact number where they used to read text.
/// Every other cell is `cellToObj`'s.
let private cellToRowObj (c: Fuaran.Core.Cell) : obj =
    match c with
    | Fuaran.Core.Cell.Decimal s -> box ({ Text = s }: Fuaran.UI.HostPrelude.DecimalCell)
    | other -> cellToObj other

/// A table's rows as a `Row` list — each row the `Map<string,obj>` keyed by column name, each
/// cell through [[cellToRowObj]]. The ONE materialisation: a Transform's result surfaces as rows
/// through it, and so (Phase 1891) does a prepared version a host wrote to State, so a grid
/// reading the key and a Transform reading it see the same rows.
///
/// Each column is looked up ONCE by name, and each cell read through `Column.cell`, which is O(1)
/// over the typed vectors (Core 0.37.0) and answers `Null` for an absent row and past a short
/// column's end, so the rows cost O(rows × columns). (Phase 1761 read each cell list into an array
/// first, because `cell` used to walk the list from its head.)
let private rowsOfTable (table: Fuaran.Core.Table) : Row list =
    let names = Fuaran.Core.Table.columnNames table
    let rowCount = Fuaran.Core.Table.rowCount table
    let columns = names |> List.map (fun n -> n, Fuaran.Core.Table.tryColumn n table)

    [ for i in 0 .. rowCount - 1 ->
          columns
          |> List.map (fun (n, column) ->
              let cell =
                  match column with
                  | Some c -> Fuaran.Core.Column.cell i c
                  | None -> Fuaran.Core.Cell.Null

              n, cellToRowObj cell)
          |> Map.ofList ]

/// Phase 1891 — a State value as the resolver reads it: a prepared version (an edited grid's
/// live table, written whole by a host's edit log) reads as its rows, the shape every other
/// writer of a grid's key stores; every other value is itself.
let private stateValue (raw: obj) : obj =
    match raw with
    | :? Fuaran.Compute.Prepared as version -> box (Seq.ofList (rowsOfTable (Fuaran.Compute.DataFrame.toTable version)))
    | other -> other

/// Coerce a resolved scalar to a `Cell`. Every numeric arm yields `Float` (int/float are
/// indistinguishable under Fable, so this stays cross-host-deterministic); DataFrame
/// comparison treats `Int`/`Float` as numbers, so a float-carried param compares correctly.
/// A `DecimalCell` (a decimal field of a selected Transform row) goes back as the exact
/// `Decimal` it came from (Phase 2006), never through a float.
let private objToCell (v: obj) : Fuaran.Core.Cell option =
    match v with
    | :? Fuaran.UI.HostPrelude.DecimalCell as d -> Some(Fuaran.Core.Cell.Decimal d.Text)
    | :? string as s -> Some(Fuaran.Core.Str s)
    | :? bool as b -> Some(Fuaran.Core.Cell.Bool b)
    | :? float as f -> Some(Fuaran.Core.Float f)
    | :? int as i -> Some(Fuaran.Core.Float(float i))
    | :? int64 as i -> Some(Fuaran.Core.Float(float i))
    | null -> Some Fuaran.Core.Cell.Null
    | _ -> None

/// The `JVal` twin of [[objToCell]] — a Transform param's `from` source is
/// `Binding<JVal>` since the swap. Same numeric policy (every number yields
/// `Float`); an array / object is non-scalar (`None`).
let private jvalToCell (v: JVal) : Fuaran.Core.Cell option =
    // `JVal` has no null case (D1 — absence is structural, never a value).
    match v with
    | JStr s -> Some(Fuaran.Core.Str s)
    | JBool b -> Some(Fuaran.Core.Cell.Bool b)
    | JFloat f -> Some(Fuaran.Core.Float f)
    | JInt i -> Some(Fuaran.Core.Float(float i))
    | JArr _
    | JObj _ -> None

/// Project a resolved i18n arg's `JVal` to the `obj` shape the
/// `II18nResolver` contract carries (scalars box; structures stringify
/// canonically — the resolver's template substitution stringifies anyway).
let private jvalToArgObj (v: JVal) : obj =
    match v with
    | JStr s -> box s
    | JBool b -> box b
    | JFloat f -> box f
    | JInt i -> box i
    | other -> box (Fuaran.Core.Canonical.write Fuaran.Core.Rendering.CrossHost other)

// ─── The Binding<JVal> → Binding<obj> erasure for STORE-READING sources ──────
//
// A `Transform` param / `I18n` arg source is `Binding<JVal>` since the swap
// (the typed verbatim carrier, D3) — but the Filter / State / Selection stores
// hold RAW host values (a chip write stores `box "eng"`, a grid click the raw
// row), exactly as before the swap. Resolving such a source AT `JVal` would
// unbox-cast a raw primitive to a union — a throw on .NET and, worse, a silent
// mis-match under Fable erasure. So these paths resolve at `obj` through this
// erasure and COERCE afterwards: a genuine `JVal` payload (an authored Static,
// a decoded defaultValue) passes through as itself; a raw store value stays raw.
let rec private objOfJValBinding (b: Binding<JVal>) : Binding<obj> =
    match b with
    | Binding.Static v -> Binding.Static(v |> Option.map box)
    | Binding.Query(name, accessor, dependsOn) -> Binding.Query(name, accessor >> box, dependsOn)
    | Binding.Filter(name, dv) -> Binding.Filter(name, dv |> Option.map box)
    | Binding.Selection(nodeId, _, dv, fld) ->
        // The JVal-typed accessor is bypassed on this path: project the RAW
        // row field (the store holds raw cells), matching the pre-swap
        // `Binding<obj>` param behaviour byte-for-byte at resolution time.
        let accessor: obj -> obj =
            match fld with
            | Some f -> Binding.projectSelectionField<obj> f
            | None -> id

        Binding.Selection(nodeId, accessor, dv |> Option.map box, fld)
    | Binding.State(key, dv) -> Binding.State(key, dv |> Option.map box)
    | Binding.Computed f -> Binding.Computed(f >> box)
    // Phase 765 — the JVal-typed accessor is bypassed here for the same
    // reason `Selection`'s is: the host furnishes a RAW string, not a
    // `JVal`, so resolving at `JVal` would unbox-cast a primitive to a
    // union (a throw on .NET, a silent mismatch under Fable erasure).
    // Phase 1533 — the GRAIN is carried through: it is a property of the
    // document, not of the slot type it is being erased to, and dropping it here
    // would make the same `Now` resolve at two resolutions depending on which
    // slot read it.
    | Binding.Now(_, grain) -> Binding.Now(id, grain)
    | Binding.I18n(key, args) -> Binding.I18n(key, args)
    // Fuaran-UI Phase 1538 — wire data, preserved across the erasure.
    | Binding.Local(flushOn, format, initialFrom, onCommit, parse, codec, commitTo) ->
        Binding.Local(
            flushOn,
            (fun (o: obj) -> format (unbox<JVal> o)),
            objOfJValBinding initialFrom,
            onCommit |> Option.map (fun oc -> fun (o: obj) -> oc (unbox<JVal> o)),
            (fun s -> parse s |> Result.map box),
            codec,
            commitTo
        )
    | Binding.Format(source, format, locale) -> Binding.Format(source, format, locale)
    | Binding.Transform(source, pipeline, parameters) -> Binding.Transform(source, pipeline, parameters)
    // Fuaran-UI Phase 1534 — the expression and its params carry no 'T payload of
    // their own (the params are themselves `Binding<JVal>` sources, like I18n's
    // args), so the case passes through the erasure unchanged.
    | Binding.Expr(expr, parameters) -> Binding.Expr(expr, parameters)
    | Binding.Invoke(capabilityId, args) -> Binding.Invoke(capabilityId, args)

/// Coerce a param source's resolved `obj` to a `Cell`: a genuine `JVal`
/// (authored/decoded payload) via [[jvalToCell]]; anything else raw via
/// [[objToCell]].
let private resolvedToCell (v: obj) : Fuaran.Core.Cell option =
    match v with
    | :? JVal as jv -> jvalToCell jv
    | other -> objToCell other

// ─── Store-value → JVal lift (Phase 818 — the reactive-derivation first cut) ─
//
// A LIVE Transform source and a `SetState.valueFrom` both read a store value
// and need it as structured data. What a store actually holds differs by host:
// the browser StateStore holds PLAIN JS values (the runtime lowers a SetState
// JVal through the JSON bridge before storing); the bounded server store holds
// the `JValObj` lowering (numbers as floats, objects as `Map<string, obj>`,
// arrays as `obj list`); a decoded binding's carried defaultValue resolves as
// a genuine `JVal` instance on both. The lift re-raises each to `JVal`
// structurally; a value with no faithful lift is `None` — every caller
// surfaces that LOUDLY (the Phase-427 mismatch posture, never a silent wrong
// value). Number policy matches the wire bridge (`jsonToJVal`): an integral
// in-int32-range number lifts to `JInt`, everything else to `JFloat`.

#if FABLE_COMPILER
module private JsLift =
    open Fable.Core

    [<Emit("typeof $0")>]
    let jsTypeof (_: obj) : string = jsNative

    [<Emit("Array.isArray($0)")>]
    let jsIsArray (_: obj) : bool = jsNative

    [<Emit("$0 !== null && typeof $0 === 'object' && ($0.constructor === Object || Object.getPrototypeOf($0) === null)")>]
    let jsIsPlainObject (_: obj) : bool = jsNative

    [<Emit("Object.keys($0)")>]
    let jsKeys (_: obj) : string[] = jsNative

    [<Emit("$0[$1]")>]
    let jsGet (_: obj) (_: string) : obj = jsNative

    // Phase 1085 — the F# COLLECTION shapes, discriminated STRUCTURALLY.
    //
    // An F# `Map` and an F# `list` compile to library classes, not to a JS
    // object or array, so neither the `typeof` dispatch nor `jsIsPlainObject`
    // sees them and the lift returned `None` for both. A TYPE TEST is not the
    // way back: `:? Map<string, obj>` is refused outright by the compiler
    // ("Cannot type test (evals to false)"), which is how this was found.
    //
    // `Symbol.toStringTag` is the map's own declared tag — a string literal the
    // library returns, so it survives minification where a constructor name
    // does not. The iterable check is deliberately BROADER than a list: it
    // covers a lazily-built sequence too, which the .NET branch's `seq<Row>`
    // arm reads and no earlier Fable arm could. Native `Map`/`Set` are excluded
    // because `Array.from` on them yields entry PAIRS, which would lift as an
    // array of two-element arrays — a plausible-looking wrong answer, and the
    // one thing `None` exists to prevent.
    [<Emit("Object.prototype.toString.call($0) === '[object FSharpMap]'")>]
    let jsIsFSharpMap (_: obj) : bool = jsNative

    [<Emit("Array.from($0)")>]
    let jsEntries (_: obj) : obj[][] = jsNative

    [<Emit("$0 != null && typeof $0[Symbol.iterator] === 'function' && !($0 instanceof Map) && !($0 instanceof Set)")>]
    let jsIsPlainIterable (_: obj) : bool = jsNative

    [<Emit("Array.from($0)")>]
    let jsToArray (_: obj) : obj[] = jsNative
#endif

let private liftNumber (f: float) : JVal =
    if
        not (System.Double.IsNaN f)
        && not (System.Double.IsInfinity f)
        && floor f = f
        && abs f <= 2147483647.0
    then
        JInt(int f)
    else
        JFloat f

/// An array lifts only when EVERY element does — a partial lift would be a
/// silently truncated table, which is the failure `None` exists to prevent.
let private liftAll (items: JVal option list) : JVal option =
    if items |> List.forall Option.isSome then
        Some(JArr(items |> List.map Option.get))
    else
        None

/// The object twin of `liftAll`, on the same all-or-nothing rule.
let private liftFields (fields: (string * JVal option) list) : JVal option =
    if fields |> List.forall (fun (_, fv) -> Option.isSome fv) then
        Some(JObj(fields |> List.map (fun (k, fv) -> k, Option.get fv)))
    else
        None

/// Lift a store-resolved value to a `JVal` (see the section note above).
/// `None` when the value has no faithful structural lift — callers surface
/// that loudly, never silently.
let rec jvalOfResolved (v: obj) : JVal option =
#if FABLE_COMPILER
    match v with
    | :? JVal as jv -> Some jv
    // Phase 2006: a Transform row's decimal cell lifts as the canonical text it crosses the wire as.
    | :? Fuaran.UI.HostPrelude.DecimalCell as d -> Some(JStr d.Text)
    | _ when isNull v -> None
    // Phase 1085 — the F# COLLECTION shapes, on the Fable pipeline too.
    //
    // MEASURED, not reasoned: a Fable build of the Phase-1075 charter pair (a
    // grid seeding `$state.members` beside a badge deriving a count over it)
    // rendered the count on .NET and rendered NOTHING in the browser, because
    // an F# list compiles to `FSharpList` and a `Map` to `FSharpMap` and
    // neither is a JS array or a plain object — so the `typeof` dispatch below
    // returned `None` for exactly the value the seeding pass puts in the store.
    // The .NET branch has carried these arms since 818; this is the same lift
    // on the other pipeline, not a new leniency, and the parity it restores is
    // the point (a store holds typed F# values on BOTH hosts — a host-furnished
    // `Row seq` and an editable grid's write-back are the standing examples).
    //
    // The arms themselves are in `JsLift` below the `typeof` dispatch, because a
    // TYPE TEST cannot express them here — see the note on `jsIsFSharpMap`.
    | _ ->
        match JsLift.jsTypeof v with
        | "string" -> Some(JStr(unbox<string> v))
        | "boolean" -> Some(JBool(unbox<bool> v))
        | "number" -> Some(liftNumber (unbox<float> v))
        | _ ->
            if JsLift.jsIsArray v then
                liftAll (unbox<obj[]> v |> Array.toList |> List.map jvalOfResolved)
            elif JsLift.jsIsPlainObject v then
                JsLift.jsKeys v
                |> Array.toList
                |> List.sortWith (fun a b -> System.String.CompareOrdinal(a, b))
                |> List.map (fun k -> k, jvalOfResolved (JsLift.jsGet v k))
                |> liftFields
            elif JsLift.jsIsFSharpMap v then
                // The `Row` / `JValObj`-object shape. Sorted ordinally like the
                // plain-object arm above rather than trusting the map's own
                // comparer, so one wire value cannot depend on which host lifted it.
                JsLift.jsEntries v
                |> Array.toList
                |> List.map (fun kv -> unbox<string> kv[0], kv[1])
                |> List.sortWith (fun (a, _) (b, _) -> System.String.CompareOrdinal(a, b))
                |> List.map (fun (k, fv) -> k, jvalOfResolved fv)
                |> liftFields
            elif JsLift.jsIsPlainIterable v then
                // An F# `list` or `seq` — the `Row seq` a Phase-1075 seed and an
                // editable grid's write-back both carry.
                liftAll (JsLift.jsToArray v |> Array.toList |> List.map jvalOfResolved)
            else
                None
#else
    match v with
    | :? JVal as jv -> Some jv
    | :? Fuaran.UI.HostPrelude.DecimalCell as d -> Some(JStr d.Text)
    | :? string as s -> Some(JStr s)
    | :? bool as b -> Some(JBool b)
    | :? int as i -> Some(JInt i)
    | :? int64 as i -> Some(liftNumber (float i))
    | :? float as f -> Some(liftNumber f)
    | :? (obj list) as xs ->
        // The `JValObj` array lowering (the bounded server store's shape).
        liftAll (xs |> List.map jvalOfResolved)
    | :? Map<string, obj> as m ->
        // The `JValObj` object lowering / a single `Row`.
        liftFields (m |> Map.toList |> List.map (fun (k, fv) -> k, jvalOfResolved fv))
    | :? seq<Row> as rows ->
        // The editable-grid write-back shape: a `Row seq` written whole to a
        // State key. Rows lift element-wise (each `Row` is the Map arm above).
        liftAll (rows |> Seq.toList |> List.map (box >> jvalOfResolved))
    | _ -> None
#endif

/// Phase 818 — materialise a LIVE Transform source's resolved store value as
/// the input table: lift to `JVal`, normalise (row-major rows transpose to
/// canonical columnar — the same rule the decode-time snapshot applies), then
/// decode through Core's columnar codec. Loud on every unliftable /
/// non-tabular value.
let private liveValueToTable (v: obj) : Result<Fuaran.Core.Table, string> =
    match jvalOfResolved v with
    | None ->
        Error
            "Transform live source resolved to a value that cannot be read as data — expected rows (an array of row objects) or canonical columnar data"
    // Fuaran-UI Phase 1534 — a SCALAR store value stays LOUD, and the message
    // now names where a scalar belongs.
    //
    // This phase was written to LIFT a scalar live source to a 1×1 frame, so a
    // `State`-sourced number could feed a pipeline without an array wrapper.
    // Implementing that falsified Phase 818's own guarantee — "a live source
    // resolving to a NON-tabular value errors loudly (never a silent wrong
    // value)" — because a lifted scalar makes a trailing `groupBy [] [count]`
    // answer 1 where it used to refuse, and a count of 1 over a slot holding a
    // scalar is precisely the silently-wrong answer that rule exists to prevent.
    //
    // The lift was dropped rather than the rule weakened, and the reason is this
    // phase's own subject: a scalar bound value belongs in `Binding.Expr`, whose
    // params take any binding and are scalar by construction. Lifting a scalar
    // into a frame so a DATAFRAME pipeline can consume it is the detour `Expr`
    // exists to remove — building it here would have added the detour in the
    // same change that replaced it. What survives of the intent is the remedy
    // text: an author who reached for `Transform` holding a scalar is told which
    // case to reach for instead.
    | Some(JStr _ | JInt _ | JFloat _ | JBool _) ->
        Error
            "Transform live source resolved to a SCALAR value, and a Transform reads rows — use Binding.Expr, whose params bind a scalar source (State / Selection / Query / Filter) directly; or, if this really is a one-row feed, store it as an array of row objects"
    | Some jv ->
        match Fuaran.UI.HostPrelude.TransformLive.initialSource jv with
        | Ok(Fuaran.Core.Embedded t) -> Ok t
        | Ok(Fuaran.Core.Ref name) ->
            Error(sprintf "Transform live source resolved to a 'ref' source ('%s'), which is not host-resolved" name)
        | Error e -> Error("Transform live source: " + Fuaran.Core.ColumnCodec.errorString e)

/// The one-row frame a `Binding.Expr` evaluates over (Phase 1534; see
/// `evalExprCell`). One immutable value for the process, so it is prepared once
/// per render however many `Expr` slots the render holds (Phase 1890).
let private exprUnitFrame: Fuaran.Core.Table =
    { Schema = [ Fuaran.Core.Field.create "__unit" Fuaran.Core.BoolType ]
      Columns = [ Fuaran.Core.Column.ofBools "__unit" (Fuaran.Core.Vector.ofList [ true ]) Fuaran.Core.AllValid ] }

/// Resolve a typed `Binding<'T>` against the supplied sources.
///
/// Per-Defect (2) note: `Query` / `Selection` accessors are obj-typed at the
/// tree level.  The resolver hands the obj-typed source value straight to
/// the captured accessor; the accessor closure was built by `binding.query` /
/// `binding.selection` and knows how to unbox internally.  No reflection.
let rec resolve<'T> (sources: BindingSources) (binding: Binding<'T>) : Resolution<'T> =
    match binding with
    // Since the swap the payload is `'T option`; the absent form resolves to
    // the slot's default representation — exactly the value the pre-swap
    // `Static` carried in the absent case (`null` / inner `None`), so an
    // option-typed slot still reads "no selection" and renders its placeholder.
    | Binding.Static(Some value) -> Resolved value
    | Binding.Static None -> Resolved Unchecked.defaultof<'T>
    | Binding.Query(name, _, _) when name = Fuaran.UI.Defaults.NotProvidedSentinel ->
        // `Defaults.noBinding<'T>` encodes "Source = is mandatory but the
        // author hasn't overridden the default yet" as a Query against this
        // sentinel name. Short-circuit before the accessor closure
        // (which would return `Unchecked.defaultof<'T>`) is ever called.
        NotResolved
    // `dependsOn` (Phase 421) is a re-render/invalidation edge, not a resolution input — the accessor
    // still owns the predicate. The renderer subscribes on those filter keys (Render.fs `keysOfBinding`),
    // so a filter-store change re-resolves this Query; resolution itself is unchanged.
    | Binding.Query(name, accessor, _) ->
        match Map.tryFind name sources.QueryResults with
        | Some raw ->
            try
                Resolved(accessor raw)
            with ex ->
                Errored(sprintf "Query '%s' accessor threw: %s" name ex.Message)
        | None -> NotResolved
    | Binding.Filter(name, defaultValue) ->
        match Map.tryFind name sources.Filters with
        | Some raw ->
            try
                Resolved(unbox<'T> raw)
            with ex ->
                // Fable erases generic type info at runtime so we can't quote
                // `typeof<'T>.Name` here — keep the message Fable-compatible.
                Errored(sprintf "Filter '%s' value did not unbox to expected type: %s" name ex.Message)
        | None ->
            // 0.2.0 — the pre-selected-filter gap: an unwritten filter key
            // resolves to the binding's declared default (the renderer also
            // seeds the store from it on first write-back).
            match defaultValue with
            | Some d -> Resolved d
            | None -> NotResolved
    | Binding.Selection(nodeId, accessor, defaultValue, _) ->
        match Map.tryFind (NodeId nodeId) sources.Selections with
        | Some raw ->
            try
                Resolved(accessor raw)
            with ex ->
                Errored(sprintf "Selection on %A accessor threw: %s" nodeId ex.Message)
        | None ->
            // 0.2.9 (Phase 629) — the pre-selected-row gap, the Filter law
            // replayed for the selection channel: no row selected yet resolves
            // to the binding's declared default; the first real selection
            // (a SelectionStore write) takes over and the default never
            // reapplies while the entry exists.
            match defaultValue with
            | Some d -> Resolved d
            | None -> NotResolved
    | Binding.State(key, defaultValue) ->
        match Map.tryFind key sources.State with
        | Some raw ->
            try
                Resolved(unbox<'T> (stateValue raw))
            with ex ->
                Errored(sprintf "State '%s' value did not unbox to expected type: %s" key ex.Message)
        | None ->
            // `Binding.State` declares its own default, so an unwritten slot is
            // not a `NotResolved` condition when a default is DECLARED — it is
            // the "no override yet" steady state and resolves to the default.
            //
            // Phase 1690 — a BARE `State` (no declared default) at an unwritten,
            // unseeded slot is UNRESOLVED, at every slot type. This arm used to
            // answer `Resolved Unchecked.defaultof<'T>`, which is not a decision
            // but a CLR artefact: it means `null` at a reference-typed slot (and
            // the `Transform` live arm below reads that very null back as
            // ABSENCE) and a fabricated `0` / `false` at a value-typed one. A
            // `Metric` labelled "Revenue" then reads `0` where the truth is that
            // no figure was furnished, and a reader cannot tell the two apart.
            //
            // `Filter` and `Selection` — the two arms §24.1 calls this one's
            // mirrors — already answer `NotResolved` for exactly this case, so
            // the format's own mirror argument (§24.2) runs in this direction
            // too. WIRE_FORMAT.md §24.8 is the normative statement.
            match defaultValue with
            | Some d -> Resolved d
            | None -> NotResolved
    | Binding.Now(accessor, grain) ->
        // Phase 765 — the host-furnished instant. The clock is NOT read here:
        // `sources.Now` was resolved once, host-side, for the whole render pass,
        // which is what makes a replayed op-stream reproduce its original render
        // instead of drifting to replay-time "now".
        //
        // An unset instant is `NotResolved`, so the node shows its
        // `onLoading`/placeholder surface. That is deliberately loud — a host
        // that forgets to furnish the clock must not silently render a
        // plausible wrong date, which is exactly the failure the models were
        // producing by hardcoding one.
        //
        // Phase 1533 — the declared GRAIN truncates the instant BEFORE the
        // accessor sees it. Before, not after: the accessor is the document's
        // projection onto its slot type, and a `Transform` param projecting to
        // `JStr` would otherwise carry a full datetime into `DateDiffDays`,
        // which reads only the leading `YYYY-MM-DD` — so the truncation has to
        // be upstream of every projection or it is not the document's
        // declaration at all. Absent grain is `Second`, which is the identity.
        if System.String.IsNullOrEmpty sources.Now then
            NotResolved
        else
            let instant =
                match grain with
                | Some g -> Formatting.truncateToGrain g sources.Now
                | None -> sources.Now

            Resolved(accessor (box instant))
    | Binding.Computed f ->
        // Phase 137: hand the closure a context with typed read access to the
        // live module-state bag. `sources.State` is authoritative (the same map
        // `Binding.State` resolves against); any keys explicitly injected via
        // `ComputedContext` are merged underneath, live state winning on a clash.
        let ctx =
            let injected = sources.ComputedContext.State

            let merged =
                if Map.isEmpty injected then
                    sources.State
                else
                    sources.State |> Map.fold (fun acc k v -> Map.add k v acc) injected

            { sources.ComputedContext with
                State = merged }

        try
            Resolved(f ctx)
        with
        // Fuaran-UI Phase 1538 — the DECODED `Computed`. Its `fn` is the whole
        // payload of the case and it encodes as `"<closure>"`, so a decoded
        // `Computed` has nothing to compute with; the stand-in raises this
        // instead of returning the slot's zero, and the message is surfaced
        // VERBATIM rather than wrapped in "Computed binding threw" — the reader
        // needs the remedy, not the mechanism, and the sentence already names
        // the cases that do cross the wire.
        | Fuaran.UI.HostPrelude.WireSurvivabilityError msg -> Errored msg
        | ex -> Errored(sprintf "Computed binding threw: %s" ex.Message)
    | Binding.Local(_, _, initialFrom, _, _, _, _) ->
        // A `Binding.Local` is structurally a re-sync source +
        // local-buffer overlay. Pure resolution returns the InitialFrom-
        // side value — the per-NodeId React.useState slot is mounted by
        // the renderer's `LocalBindings` module and overrides this read
        // at the form-field render layer. Bindings used outside that
        // context (e.g. accidentally placed on a Metric Source) resolve to
        // the underlying source, which is the principled behaviour: the
        // validator's FUARAN044 catches the misplacement at build time,
        // but if it slips to runtime the read is still meaningful.
        resolve<'T> sources initialFrom
    | Binding.I18n(key, argsOpt) ->
        // Resolve args (each `Binding<obj>`) into a `Map<string, obj>`
        // for the resolver. Failed arg resolutions substitute an empty string;
        // the resolver still gets called so the key-level template substitution
        // proceeds. The resolver returns a string; cast to 'T via unbox — only
        // valid when 'T = string (same shape as `Binding.Query` accessor's cast).
        let resolvedArgs =
            argsOpt
            |> Option.map (fun args ->
                args
                |> Map.map (fun _ b ->
                    // Resolve at obj through the store-reading erasure (see
                    // objOfJValBinding); a genuine JVal payload projects to the
                    // resolver-contract obj shape, a raw store value stays raw.
                    match resolve<obj> sources (objOfJValBinding b) with
                    | Resolved v ->
                        (match v with
                         | :? JVal as jv -> jvalToArgObj jv
                         | other -> other)
                    | NotResolved
                    | Errored _
                    | I18nUnresolved _ -> box ""))

        let resolved = sources.I18nResolver.Resolve(key, resolvedArgs)
        // Detect debug-placeholder shape (the passthrough resolver's
        // `[i18n:<key>]` and the map-backed resolver's miss-fallback)
        // so the renderer can branch on `I18nUnresolved` for missing
        // translations. A real resolver that genuinely returns a string
        // that happens to match this shape is technically conflated; the
        // signal is intentional (an i18n catalog should never contain a
        // translation of the form `[i18n:something-else]`).
        let placeholderShape = sprintf "[i18n:%s]" key

        if resolved = placeholderShape then
            I18nUnresolved key
        else
            try
                Resolved(unbox<'T> (box resolved))
            with ex ->
                Errored(
                    sprintf
                        "I18n '%s' resolved string did not unbox to expected type (Binding.I18n is constrained to Binding<string>): %s"
                        key
                        ex.Message
                )
    | Binding.Format(source, fmt, localeSource) ->
        // Resolve the numeric source, then project to a localised
        // string via the Intl (Fable) / Globalization (.NET) formatter. The
        // resolved string is cast to 'T via unbox — only valid when 'T =
        // string (same constraint + failure shape as Binding.I18n).
        match resolve<float> sources source with
        | Resolved n ->
            let localeTag =
                match localeSource with
                | LocaleSource.Explicit tag -> tag
                | LocaleSource.Ambient -> sources.Locale

            // Phase 1533 — `Since` is the one `Format` case whose rendering is a
            // function of the HOST instant as well as of its source, so the
            // delta is taken here, where the instant lives, and `Formatting`
            // stays a pure projection of its arguments. The source is read as an
            // instant in whole Unix-epoch seconds (`Date`'s convention) and the
            // sign follows `Intl.RelativeTimeFormat`'s: negative is the past.
            //
            // An unset or unreadable host instant is `NotResolved`, for exactly
            // the reason `Binding.Now` gives above: a relative time computed
            // against an invented "now" is a plausible wrong answer, which is
            // worse than a visible placeholder.
            let projected =
                match fmt with
                | Format.Since _ ->
                    match Formatting.epochSecondsOfInstant sources.Now with
                    | Some nowEpoch -> Ok(Formatting.format localeTag fmt (n - nowEpoch))
                    | None -> Error()
                | _ -> Ok(Formatting.format localeTag fmt n)

            match projected with
            | Error() -> NotResolved
            | Ok formatted ->

                try
                    Resolved(unbox<'T> (box formatted))
                with ex ->
                    Errored(
                        sprintf
                            "Format binding produced a string that did not unbox to the expected type (Binding.Format is constrained to Binding<string>): %s"
                            ex.Message
                    )
        | NotResolved -> NotResolved
        | Errored m -> Errored m
        | I18nUnresolved k -> I18nUnresolved k
    | Binding.Transform(source, pipeline, parameters) ->
        // Phase 282 — the declarative Compute layer. Evaluate the serialisable dataframe pipeline
        // via the `Fuaran.Compute.DataFrame` reference evaluator (the same evaluator `transformLaws`
        // certifies — the param/prune/eval machinery lives in `evalTransformFrame`, shared with the
        // Phase-632 scalar path), then surface the result rows as `Row seq` — each row the
        // `Map<string,obj>` keyed by column name, the cell scalar boxed (fuaran#665 named the
        // shape; the representation is unchanged). Constrained to `Binding<Row seq>` use at a
        // data-bearing node (same 'T-constraint posture as `Binding.Format` / `Binding.I18n`);
        // a SCALAR slot (TextSource, Metric/LabelValueRow values) resolves through
        // `resolveScalarWith` instead (Phase 632).
        match evalTransformFrame sources source pipeline (defaultArg parameters []) with
        | Error m -> Errored m
        | Ok result ->
            let rows = rowsOfTable result

            try
                Resolved(unbox<'T> (box (Seq.ofList rows)))
            with ex ->
                Errored(
                    sprintf
                        "Transform binding produced rows that did not unbox to the expected type (Binding.Transform is constrained to Binding<Row seq>): %s"
                        ex.Message
                )
    // Fuaran-UI Phase 1534 — the scalar expression. This is the GENERIC arm, for
    // a slot that reached the resolver without a coercion (`Binding<string>`
    // read directly, a `Query`'s accessor path): the cell is unboxed to `'T` the
    // way `Binding.Transform`'s rows are, and a mismatch is loud. A slot that
    // has a coercion — every text and numeric slot — goes through
    // `resolveScalarWith` below and gets `cellToText` / `cellToFloat` /
    // `cellToBool` instead, which is where the string/float/bool conversions
    // live. A NULL result is `NotResolved`, matching what the scalar Transform
    // path already does with a null cell: the slot's empty state, not an error.
    | Binding.Expr(expr, parameters) ->
        match evalExprCell sources expr (defaultArg parameters []) with
        | Error m -> Errored m
        | Ok Fuaran.Core.Cell.Null -> NotResolved
        | Ok cell ->
            try
                Resolved(unbox<'T> (cellToObj cell))
            with ex ->
                Errored(
                    sprintf
                        "Expr binding produced a %s cell that did not unbox to this slot's type — an Expr resolves through the slot's own coercion in a text or numeric slot; here the value is taken as-is: %s"
                        (match cell with
                         | Fuaran.Core.Int _ -> "int"
                         | Fuaran.Core.Float _ -> "float"
                         | Fuaran.Core.Cell.Bool _ -> "bool"
                         | Fuaran.Core.Str _ -> "string"
                         | Fuaran.Core.Cell.Date _ -> "date"
                         | Fuaran.Core.Cell.Timestamp _ -> "timestamp"
                         | Fuaran.Core.Cell.Null -> "null"
                         | Fuaran.Core.Cell.Decimal _ -> "decimal")
                        ex.Message
                )
    | Binding.Invoke(capabilityId, args) ->
        // Phase 283 — dispatch a host-registered capability for a value. The host invoker resolves
        // (capabilityId, args) to a `Deferred<obj>`; map `Pending` → `NotResolved` (the node's
        // `onLoading`), `Ready` → `Resolved`, `Error` → `Errored` (its `onError`) — reusing the
        // `StateBehaviour` surface, no new node. The default invoker is `Pending` until a host
        // (the AiTools registry) wires real dispatch + Phase-27 replay.
        match sources.CapabilityInvoker capabilityId (args |> List.map (fun (a: InvokeArg) -> a.Addr, a.Value)) with
        | Fuaran.UI.Types.Deferred.Pending -> NotResolved
        | Fuaran.UI.Types.Deferred.Error m -> Errored m
        | Fuaran.UI.Types.Deferred.Ready v ->
            try
                Resolved(unbox<'T> v)
            with ex ->
                Errored(sprintf "Invoke '%s' result did not unbox to the expected type: %s" capabilityId ex.Message)

/// The shared Transform evaluation (Phase 282/424 machinery, extracted in Phase 632 so the
/// rows path and the scalar path evaluate identically): resolve each `parameters` entry's
/// scalar `from` binding to a `Cell` and build the evaluation env; a param whose source is
/// `NotResolved` (an unset choice filter) is *unbound* — every `filter` step referencing an
/// unbound param is PRUNED (the one lenient "unset filter ⇒ no constraint" rule — Core stays
/// strict), while a *non-filter* step referencing an unbound param surfaces Core's
/// `UnboundParam` loudly (never silent). A non-scalar or `Errored` param source, a `Ref`
/// source (deferred — Phase 282 evaluates Embedded), and an evaluator error are all `Error`.
///
/// Phase 610 — a param source that resolves to a LIST (a multi-select chip's selection) is a
/// LIST param. It never enters the scalar env: it resolves by SUBSTITUTION through
/// `Transform.substituteListParams` (`InParam(x, n)` -> `InList(x, <items as literals>)`) before
/// the prune below, which is how Core specifies a list param (fuaran-core#91) rather than a second
/// evaluation env. An EMPTY selection is treated as UNBOUND rather than substituted to
/// `InList(x, [])`: "nothing selected" is the absence of a constraint, not a constraint no row
/// satisfies — the same lenient rule an unset scalar chip already gets, so deselecting everything
/// shows the unfiltered table rather than an empty one. A list bound to a name the pipeline reads
/// as a SCALAR `param` (or a scalar bound to one it reads as an `in`/`param`) substitutes nothing
/// and reaches Core's strict `UnboundParam` — loud, never a silent wrong scoping.
and private evalTransformFrame
    (sources: BindingSources)
    (source: TransformSource)
    (pipeline: Fuaran.Compute.Transform list)
    (parameters: TransformParam list)
    : Result<Fuaran.Core.Table, string> =
    evalTransformFrameWithin TransformBudget.defaults sources sources source pipeline parameters

/// `evalTransformFrame` under an EXPLICIT budget (Phase 1532). The budgeted form
/// is the real one; the arity above is it at `TransformBudget.defaults`, which
/// is what keeps "the default is a real limit" a property of the code rather
/// than a claim about it — there is one evaluation path, entered at two arities.
///
/// `render` is the render whose prepared sources the input is shared through
/// (Phase 1890, `PreparedSources`); it is `sources` itself except where a caller
/// evaluates under a narrowed copy of the render's sources, as `evalExprCell`
/// does, and must not open a second scope for it.
and private evalTransformFrameWithin
    (budget: TransformBudget)
    (render: BindingSources)
    (sources: BindingSources)
    (source: TransformSource)
    (pipeline: Fuaran.Compute.Transform list)
    (parameters: TransformParam list)
    : Result<Fuaran.Core.Table, string> =
    // Phase 610 — coerce a resolved LIST source to `Cell list`. Routed through
    // [[jvalOfResolved]], the total store-value lift both pipelines already share (Phase
    // 818/1085), so a browser array, an F# list under Fable, the server store's `obj list`
    // lowering and a genuine `JArr` all land on one shape rather than on four type tests that
    // would drift apart per host. A non-array, or an array holding a non-scalar item, is `None`
    // and keeps the pre-existing loud "non-scalar value" error.
    let resolvedToCells (v: obj) : Fuaran.Core.Cell list option =
        match jvalOfResolved v with
        | Some(JArr items) ->
            let cells = items |> List.map jvalToCell

            if cells |> List.forall Option.isSome then
                Some(cells |> List.map Option.get)
            else
                None
        | _ -> None

    let rec resolveParams
        (env: Map<string, Fuaran.Core.Cell>)
        (listEnv: Map<string, Fuaran.Core.Cell list>)
        (unbound: Set<string>)
        remaining
        =
        match remaining with
        | [] -> Ok(env, listEnv, unbound)
        | (p: TransformParam) :: rest ->
            let name = p.Name

            match resolve<obj> sources (objOfJValBinding p.From) with
            | Resolved v ->
                match resolvedToCell v with
                | Some cell -> resolveParams (Map.add name cell env) listEnv unbound rest
                | None ->
                    match resolvedToCells v with
                    // The empty selection is UNBOUND, not an empty membership set (see above).
                    | Some [] -> resolveParams env listEnv (Set.add name unbound) rest
                    | Some cells -> resolveParams env (Map.add name cells listEnv) unbound rest
                    | None -> Error(sprintf "Transform param '%s' resolved to a non-scalar value" name)
            | NotResolved -> resolveParams env listEnv (Set.add name unbound) rest
            | Errored m -> Error(sprintf "Transform param '%s' source errored: %s" name m)
            | I18nUnresolved k -> Error(sprintf "Transform param '%s' source is an unresolved i18n key '%s'" name k)

    match resolveParams Map.empty Map.empty Set.empty parameters with
    | Error m -> Error m
    | Ok(env, listEnv, unbound) ->

        // Phase 610 — bound LIST params resolve by substitution BEFORE the prune. A substituted
        // `InParam` becomes an `InList` and so names no param at all, while an unbound one
        // survives as `InParam` and is caught by the prune below under its own name — which is
        // why one `paramsOf`-driven prune covers both param kinds with no second rule.
        let pipeline =
            if Map.isEmpty listEnv then
                pipeline
            else
                Fuaran.Compute.Transform.substituteListParams listEnv pipeline

        // Prune every `filter` step whose params include an unbound name (unset filter ⇒ no constraint).
        let pipeline =
            pipeline
            |> List.filter (fun step ->
                match step with
                | Fuaran.Compute.Filter pred ->
                    Fuaran.Compute.ColExpr.paramsOf pred
                    |> List.forall (fun p -> not (Set.contains p unbound))
                | _ -> true)

        // Phase 1532 — the budget is checked HERE, between the resolved input and
        // the evaluator, because that is the only place both quantities are
        // known: the pipeline has been pruned (an unbound filter is gone, so the
        // step count is the one that will actually run) and the input table has
        // been resolved (so the row count is the real one, not the tree's claim
        // about it). Checking earlier would measure a pipeline that is not the
        // one evaluated; checking inside the evaluator is not available, because
        // the evaluator is a separately published package this tier consumes.
        //
        // Phase 1890 — the input arrives PREPARED, once per source per render
        // (`PreparedSources`), and evaluates through `evalPrepared`: the same
        // resolver, env and pipeline as `evalPipelineInEnv` over the table it
        // stands for, the same cells and the same errors (Compute's own
        // contract, and this tier's corpus parity suite). Its row count is the
        // table's, so the budget reads exactly what it read before.
        let evalTable (input: Fuaran.Compute.Prepared) : Result<Fuaran.Core.Table, string> =
            let inputTable = Fuaran.Compute.DataFrame.toTable input

            match TransformBudget.check budget (Fuaran.Core.Table.rowCount inputTable) (List.length pipeline) with
            | Error refusal -> Error refusal
            | Ok() ->
                match Fuaran.Compute.DataFrame.evalPrepared Fuaran.Compute.DataFrame.noResolve env pipeline input with
                | Error e -> Error("Transform evaluation failed: " + Fuaran.Compute.DataFrame.errorString e)
                | Ok result -> Ok result

        // Phase 1761 — the effective pipeline CLOSED over its environment: every
        // bound scalar param substituted as a literal. It is the same question
        // `evalTable` asks — `evalPipelineInEnv env p ≡ evalPipeline
        // (Transform.substitute env p)` is Core's certified substitution law,
        // for expression params and slot params alike — asked in the EMPTY
        // environment the store's seam evaluates in. So a site under bound
        // params is a site the store can serve, keyed by the pipeline it will
        // actually run, parameter values included.
        //
        // Only a param the pipeline READS is substituted (the env may bind
        // more, and `substitute` touches nothing it does not name), which is
        // the whole reuse rule: a write to a param this pipeline never reads
        // leaves the closed pipeline — and so its site key — unchanged, and the
        // store hands back the evaluation it holds; a write to one it reads
        // moves the key and the store evaluates in full. A parameter change is
        // never approximated, because nothing is compared but the pipeline
        // itself.
        //
        // `closed` is the guard, and it is exact rather than cautious: a param
        // still standing after substitution (unbound in a `derive`, or a slot
        // param bound to a cell of the wrong shape, which `substitute` leaves
        // for the evaluator to refuse by name) takes `evalTable`, so its error
        // is the one it always was.
        let closedPipeline =
            if Map.isEmpty env then
                pipeline
            else
                Fuaran.Compute.Transform.substitute env pipeline

        let closed = List.isEmpty (Fuaran.Compute.Transform.paramsOf closedPipeline)

        // The store path, shared by both source arms (Phase 1586 for `Live`,
        // Phase 1761 for `Data`). The budget is checked BEFORE the store for
        // the Phase-1532 reason: this is the only point where the pruned
        // pipeline and the resolved input are both known, and the store path
        // must not be a way to spend past a refusal.
        let evalSite (siteKey: string) (input: Fuaran.Compute.Prepared) : Result<Fuaran.Core.Table, string> =
            match sources.LiveTransforms with
            | Some store when closed ->
                let inputTable = Fuaran.Compute.DataFrame.toTable input

                match TransformBudget.check budget (Fuaran.Core.Table.rowCount inputTable) (List.length pipeline) with
                | Error refusal -> Error refusal
                | Ok() ->
                    store.Evaluate(siteKey, closedPipeline, inputTable)
                    |> Result.mapError (fun e -> "Transform evaluation failed: " + e)
            | _ -> evalTable input

        // Phase 1890 — every site over one source object shares one prepared
        // input for the render `sources` stands for. The key is the object the
        // table is read FROM (the embedded table, the resolved store value),
        // by reference, so a scoped rebinding prepares its own.
        let prepared (from: obj) (toTable: unit -> Result<Fuaran.Core.Table, string>) =
            PreparedSources.prepare render from toTable

        match source with
        | TransformSource.Data(Fuaran.Core.Ref name) ->
            Error(
                sprintf
                    "Transform 'Ref' source '%s' is not host-resolved yet (Phase 282 evaluates Embedded sources)"
                    name
            )
        // Phase 1761 — a STATIC source consults the store too. Its rows never
        // change, so what moves between renders is the environment, and the
        // closed pipeline above is what carries it: an unchanged key over an
        // unchanged table is the store's `ReusedPrior`, evaluating nothing.
        | TransformSource.Data(Fuaran.Core.Embedded inputTable) ->
            prepared (box inputTable) (fun () -> Ok inputTable)
            |> Result.bind (evalSite (Fuaran.UI.BindingWalk.dataSiteKey inputTable closedPipeline))
        // Phase 818 — the LIVE source: resolve the preserved binding against
        // the reactive stores (through the store-reading erasure, so raw store
        // values stay raw) and evaluate over the CURRENT data; an unwritten
        // store falls back to the decode-time initial snapshot, which is what
        // makes SSR byte-identical to the Phase-815 snapshot semantics. The
        // renderer's reactive key walk subscribes the source binding's channel
        // keys, so a store write re-evaluates every reader.
        | TransformSource.Live(binding, initial) ->
            let fromInitial () =
                match initial with
                | Fuaran.Core.Embedded t -> prepared (box t) (fun () -> Ok t)
                | Fuaran.Core.Ref name ->
                    Error(sprintf "Transform live source initial snapshot is a non-host-resolved 'ref' ('%s')" name)

            // Phase 1891 — a State slot holding a prepared VERSION (an edited
            // grid's live table, written whole by a host's edit log) is the input
            // itself, read before the resolver lifts it to rows for every other
            // reader of the key: nothing to lift, and no second preparation that
            // would lose the chunks the version shares with the one before it,
            // which is what lets a session store refresh the site from the edit
            // rather than by diffing.
            let heldVersion =
                match binding with
                | Binding.State(key, _) ->
                    match Map.tryFind key sources.State with
                    | Some(:? Fuaran.Compute.Prepared as version) -> Some version
                    | _ -> None
                | _ -> None

            let inputR =
                match heldVersion with
                | Some version -> Ok version
                | None ->
                    match resolve<obj> sources (objOfJValBinding binding) with
                    // Phase 1085 — an ABSENT resolved value is the initial snapshot,
                    // not an error. `Binding.State(key, None)` on a slot nothing has
                    // seeded or written resolves to the slot's default
                    // representation (`null`) rather than to `NotResolved`, so
                    // without this arm the bare wire spelling
                    // `{"$type":"State","key":k}` — the one this phase makes
                    // decodable, and the one FUARAN106's own remedy tells an author
                    // to write — would refuse where `"defaultValue": []` renders the
                    // empty table. Two spellings of "I read this key and carry no
                    // data of my own" must resolve alike.
                    //
                    // DELIBERATELY the quiet arm, and the choice is FUARAN105's:
                    // that rule's own contract already says a default-less source
                    // "decodes to `TransformLive.emptySource`" and names the
                    // resulting zero as a pre-emit WARNING. Making the resolver loud
                    // instead would put the resolver and the shipped validator in
                    // flat contradiction inside one binary — the exact defect class
                    // 1075 closed when it widened FUARAN105 rather than leaving it.
                    // The loudness lives at authoring time, where it can name the
                    // key and the remedy; a raw `null` at render time can name
                    // neither.
                    | Resolved v when isNull v -> fromInitial ()
                    | Resolved v -> prepared v (fun () -> liveValueToTable v)
                    | NotResolved -> fromInitial ()
                    | Errored m -> Error(sprintf "Transform live source errored: %s" m)
                    | I18nUnresolved k -> Error(sprintf "Transform live source is an unresolved i18n key '%s'" k)

            // Phase 1586 — the live path consults the session-held store before
            // it evaluates. Phase 1179 shipped the incremental seam behind a
            // store but wired it only in the server-driven tier, so a
            // client-rendered tree with a live source still paid the whole
            // pipeline on every render — the cost 1179 was built to remove.
            //
            // TWO CONDITIONS, and the second is the load-bearing one.
            //
            // A store must be furnished: absence is not a degraded mode, it is
            // today's path unchanged, which is what makes the slot additive in
            // behaviour and not only in shape.
            //
            // And the pipeline must be CLOSED. The seam evaluates in the empty
            // environment (`Incremental.primeOn` / `refreshOn` are `prime` /
            // `refresh` at `Map.empty`), so handed the pipeline with its scalar
            // params still standing it would answer a different question from
            // the one `evalTable` asks. Until Phase 1761 a live source under a
            // bound param therefore evaluated in full whether a store was
            // furnished or not; since 1761 the store is handed the CLOSED
            // pipeline (`closedPipeline` above — every bound scalar substituted
            // under Core's certified substitution law), which is the same
            // question asked in the seam's own environment. LIST params resolve
            // by substitution INTO the pipeline before either, as they always
            // did.
            //
            // The site key is the renderer's ONE rule (Phase 1586) over that
            // closed pipeline; the store's own key rule is the `site` string it
            // is handed, exactly as the server-driven tier hands it. With no
            // params bound the closed pipeline IS the effective one, so every
            // unparameterised site keeps the key it had.
            inputR
            |> Result.bind (evalSite (Fuaran.UI.BindingWalk.liveSiteKey binding closedPipeline))

/// Fuaran-UI Phase 1534 — evaluate a `Binding.Expr` to ONE cell.
///
/// It is `evalTransformFrame` over a one-row frame and a two-step pipeline, and
/// that is the whole implementation on purpose: param resolution, the list-param
/// substitution, the unbound-name handling and the evaluator are then literally
/// the same code the pipeline runs, so an expression cannot mean one thing
/// inside a `derive` and another inside an `Expr`. A second evaluator here would
/// be a second thing to specify, certify on five hosts, and keep in step.
///
/// The frame carries one column of one row so `derive` has a row to produce; the
/// expression never reads it (a `Col` reference is refused at decode), and the
/// trailing `project` drops it so what comes back is 1×1 by construction rather
/// than by inspection.
and private evalExprCell
    (sources: BindingSources)
    (expr: Fuaran.Compute.ColExpr)
    (parameters: TransformParam list)
    : Result<Fuaran.Core.Cell, string> =
    let pipeline =
        [ Fuaran.Compute.Derive("__value", expr)
          Fuaran.Compute.Project [ "__value", "__value" ] ]

    // Phase 1761 — the one-row frame is not a SITE, and is kept out of the
    // store the `Data` arm now consults: a 1x1 evaluation costs less than the
    // key that would name it, and a session's bounded store is for the tables
    // that are worth keeping.
    //
    // Phase 1890 — the frame is ONE value for the process (`exprUnitFrame`), so
    // every `Expr` of a render shares one prepared input, scoped by the
    // render's own sources rather than by this narrowed copy of them.
    let narrowed = { sources with LiveTransforms = None }

    match
        evalTransformFrameWithin
            TransformBudget.defaults
            sources
            narrowed
            (TransformSource.Data(Fuaran.Core.Embedded exprUnitFrame))
            pipeline
            parameters
    with
    | Error m ->
        // `evalTransformFrame`'s messages say "Transform"; an author looking at
        // an `Expr` binding has no Transform to look for.
        Error(m.Replace("Transform ", "Expr ").Replace("Transform evaluation", "Expr evaluation"))
    | Ok result ->
        // The column is there by construction — `derive` writes it and `project`
        // keeps only it — so its ABSENCE is a broken invariant in this function,
        // never a null result. Defaulting to `Null` here would render the
        // invariant break as the slot's ordinary empty state and hide it
        // completely; it is exactly the kind of quiet default that surfaces
        // months later as a different bug.
        match Fuaran.Core.Table.tryColumn "__value" result with
        | Some col -> Ok(Fuaran.Core.Column.cell 0 col)
        | None ->
            Error(
                sprintf
                    "Expr evaluation produced no result column (columns present: %s) — a host defect, not a document one"
                    (Fuaran.Core.Table.columnNames result |> String.concat ", ")
            )

/// Phase 818 — resolve a `Binding<JVal>` (a `SetState.valueFrom` source) against
/// the stores at `obj` through the store-reading erasure, lifting the resolved
/// raw store value back to `JVal`. An unliftable value is `Errored` — loud, the
/// Phase-427 mismatch posture — never a silent wrong write.
let resolveJVal (sources: BindingSources) (binding: Binding<JVal>) : Resolution<JVal> =
    match resolve<obj> sources (objOfJValBinding binding) with
    | Resolved v ->
        (match jvalOfResolved v with
         | Some jv -> Resolved jv
         | None ->
             Errored
                 "the binding resolved to a value with no wire representation (a host object) — it cannot be written as a derived state value")
    | NotResolved -> NotResolved
    | Errored m -> Errored m
    | I18nUnresolved k -> I18nUnresolved k

/// Best-effort `tryResolve` — returns `Some` for `Resolved`, otherwise `None`.
/// Convenience for renderer call sites that treat NotResolved + Errored
/// identically (e.g. simple fall-through to the empty-state placeholder).
let tryResolve<'T> (sources: BindingSources) (binding: Binding<'T>) : 'T option =
    match resolve<'T> sources binding with
    | Resolved value -> Some value
    | NotResolved
    | Errored _
    | I18nUnresolved _ -> None

// ─── Scalar-slot resolution (Phase 632) ──────────────────────────────────────
//
// A `Binding.Transform` in a SCALAR slot (a `TextSource.Bound`, a Metric /
// LabelValueRow value) resolves to the lone cell of an exactly-1×1 pipeline
// result — the r42 shape (`filter → project [col] → limit 1`) and the global
// aggregate (`groupBy(keys: [], aggs: [one agg])`) are the two canonical
// terminals; no new wire vocabulary. The rule is LOUD on ambiguity (Phase-427
// posture — never a silent first cell) and renders absence for an empty
// result, except a trailing global single-`count` groupBy which resolves 0
// (the count of nothing is 0 — the host completes the SQL global-aggregate
// semantic Core's strict fold leaves empty).
//
// This is a DISTINCT entry point wired at the scalar call sites rather than a
// `typeof<'T>` dispatch inside `resolve` — type-directed detection does not
// survive Fable erasure (an `unbox<'T>` of the rows list would "succeed" for
// every 'T on Fable and silently render the rows array in a text slot).

/// Coerce a result cell to a text-slot string. Numbers format invariantly
/// (F# `string` is invariant-culture for IFormattable on .NET and the JS
/// number-to-string on Fable — the same digits for the wire-legal cell range).
let cellToText (c: Fuaran.Core.Cell) : Result<string, string> =
    match c with
    | Fuaran.Core.Str s -> Ok s
    | Fuaran.Core.Int i -> Ok(string i)
    | Fuaran.Core.Float f -> Ok(string f)
    | Fuaran.Core.Cell.Bool b -> Ok(if b then "true" else "false")
    | Fuaran.Core.Cell.Date s -> Ok s
    | Fuaran.Core.Cell.Timestamp s -> Ok s
    | Fuaran.Core.Cell.Decimal s -> Ok s
    | Fuaran.Core.Cell.Null -> Error "Transform yielded a null cell in a text slot"

/// Coerce a result cell to a numeric-slot float.
let cellToFloat (c: Fuaran.Core.Cell) : Result<float, string> =
    match c with
    | Fuaran.Core.Int i -> Ok(float i)
    | Fuaran.Core.Float f -> Ok f
    | Fuaran.Core.Str s ->
        Error(
            sprintf
                "Transform yielded a text cell ('%s') in a numeric slot — project a numeric column, or aggregate with count / sum / mean"
                s
        )
    | Fuaran.Core.Cell.Bool _ -> Error "Transform yielded a bool cell in a numeric slot"
    | Fuaran.Core.Cell.Date s
    | Fuaran.Core.Cell.Timestamp s ->
        Error(
            sprintf
                "Transform yielded a date cell ('%s') in a numeric slot — project a numeric column, or aggregate with count / sum / mean"
                s
        )
    | Fuaran.Core.Cell.Null -> Error "Transform yielded a null cell in a numeric slot"
    // Fuaran.Core 0.33.0 (D72): a decimal column is numeric; a numeric slot reads it at its
    // nearest float, as Core's own float readings of a decimal do.
    | Fuaran.Core.Cell.Decimal s ->
        match Fuaran.Core.DecimalText.tryToFloat s with
        | Some f -> Ok f
        | None -> Error(sprintf "Transform yielded a decimal cell ('%s') outside the float range in a numeric slot" s)

/// Fuaran-UI Phase 1534 — the BOOLEAN coercion, the third of the trio beside
/// `cellToText` / `cellToFloat`. Strict: only a `Bool` cell is a boolean.
///
/// No truthiness. `0`, `""` and `"false"` are all refused rather than read as
/// `false`, because every language that has guessed at this has guessed
/// differently, and five hosts agreeing on a rendering is the whole point of the
/// corpus. The vocabulary already has the total spellings — `isNull` for
/// presence, a `=` comparison for a value, `not` for negation — so refusing here
/// costs an author nothing but the explicit operator.
let cellToBool (c: Fuaran.Core.Cell) : Result<bool, string> =
    match c with
    | Fuaran.Core.Cell.Bool b -> Ok b
    | Fuaran.Core.Int _
    | Fuaran.Core.Float _
    | Fuaran.Core.Cell.Decimal _ ->
        Error
            "a numeric cell is not a boolean — compare it (`=`, `>`, `isNull`) rather than relying on a truthiness rule the hosts do not share"
    | Fuaran.Core.Str s ->
        Error(
            sprintf
                "a text cell ('%s') is not a boolean — compare it (`=`, `isNull`) rather than relying on a truthiness rule the hosts do not share"
                s
        )
    | Fuaran.Core.Cell.Date s
    | Fuaran.Core.Cell.Timestamp s -> Error(sprintf "a date cell ('%s') is not a boolean" s)
    | Fuaran.Core.Cell.Null -> Error "a null cell is not a boolean — test presence with `isNull`"

/// Resolve a binding in a SCALAR slot, and name the field the value was read
/// from: `Binding.Transform` evaluates through the shared frame machinery and
/// interprets the result as one cell (see the section note above), and its
/// result's one schema entry is the BOUND FIELD (Phase 2209, WIRE_FORMAT
/// §3.6.27) — read from the same evaluation, so a slot that shows the field's
/// unit evaluates the pipeline once. Every other binding case resolves exactly as
/// `resolve` does and has no bound field.
let resolveScalarWithField<'T>
    (coerce: Fuaran.Core.Cell -> Result<'T, string>)
    (sources: BindingSources)
    (binding: Binding<'T>)
    : Resolution<'T> * Fuaran.Core.Field option =
    match binding with
    // Fuaran-UI Phase 1534 — the scalar expression in a slot that has a
    // coercion. `Null` is `NotResolved` (the slot's empty state, exactly as a
    // null cell out of a scalar `Transform` is); an unbound param or a type
    // error is `Errored`, never a substituted default — a wrong number rendered
    // confidently is worse than a slot that says it could not be computed.
    | Binding.Expr(expr, parameters) ->
        let resolution =
            match evalExprCell sources expr (defaultArg parameters []) with
            | Error m -> Errored m
            | Ok Fuaran.Core.Cell.Null -> NotResolved
            | Ok cell ->
                match coerce cell with
                | Ok v -> Resolved v
                | Error m -> Errored m

        resolution, None
    | Binding.Transform(source, pipeline, parameters) ->
        match evalTransformFrame sources source pipeline (defaultArg parameters []) with
        | Error m -> Errored m, None
        | Ok result ->
            let names = Fuaran.Core.Table.columnNames result
            let rowCount = Fuaran.Core.Table.rowCount result
            let colCount = List.length names

            let field =
                match result.Schema with
                | [ only ] -> Some only
                | _ -> None

            let resolution =
                if rowCount = 1 && colCount = 1 then
                    let cell =
                        Fuaran.Core.Table.tryColumn (List.head names) result
                        |> Option.map (Fuaran.Core.Column.cell 0)
                        |> Option.defaultValue Fuaran.Core.Cell.Null

                    match cell with
                    | Fuaran.Core.Cell.Null -> NotResolved
                    | c ->
                        match coerce c with
                        | Ok v -> Resolved v
                        | Error m -> Errored m
                elif rowCount = 0 then
                    // The count of nothing is 0: Core's strict groupBy fold yields zero
                    // groups over an empty frame; a trailing global single-`count`
                    // aggregate completes to 0 here. Every other empty result is the
                    // slot's empty state ("filter matched nothing" renders as absence).
                    match List.tryLast pipeline with
                    | Some(Fuaran.Compute.GroupBy([], [ agg ])) when agg.Fn = Fuaran.Core.AggFn.Count ->
                        match coerce (Fuaran.Core.Int 0) with
                        | Ok v -> Resolved v
                        | Error m -> Errored m
                    | _ -> NotResolved
                else
                    Errored(
                        sprintf
                            "Transform in a scalar slot must yield exactly one row × one column (got %d×%d) — end the pipeline with `project` to one column + `limit` 1 (a row-field lookup), or aggregate with `groupBy` keys [] + one agg (count / sum / mean / first)"
                            rowCount
                            colCount
                    )

            resolution, field
    | other -> resolve<'T> sources other, None

/// Resolve a binding in a SCALAR slot: `Binding.Transform` evaluates through the
/// shared frame machinery and interprets the result as one cell (see the section
/// note above); every other binding case resolves exactly as `resolve` does.
let resolveScalarWith<'T>
    (coerce: Fuaran.Core.Cell -> Result<'T, string>)
    (sources: BindingSources)
    (binding: Binding<'T>)
    : Resolution<'T> =
    fst (resolveScalarWithField coerce sources binding)

/// Scalar-slot resolution for a text slot (`TextSource.Bound` and friends).
let resolveScalarText (sources: BindingSources) (binding: Binding<string>) : Resolution<string> =
    resolveScalarWith cellToText sources binding

/// Scalar-slot resolution for a numeric slot (Metric / LabelValueRow values).
let resolveScalarFloat (sources: BindingSources) (binding: Binding<float>) : Resolution<float> =
    resolveScalarWith cellToFloat sources binding

/// Fuaran-UI Phase 1534 — scalar-slot resolution for a BOOLEAN slot. Completes
/// the trio so an `Expr` reaches a boolean slot through the same coercion seam a
/// text or numeric one does, rather than through the generic unbox.
let resolveScalarBool (sources: BindingSources) (binding: Binding<bool>) : Resolution<bool> =
    resolveScalarWith cellToBool sources binding

/// Resolve a ROWS slot (a `DataGrid` / `Chart` `source`) together with the
/// result schema its columns are read from (Phase 2209). A `Binding.Transform`
/// evaluates ONCE, through the same frame machinery `resolve` uses, and its
/// result's schema rides beside the rows; every other binding case resolves
/// exactly as `resolve` does and has no fields — so a slot that is not a
/// `Transform` has no bound field and renders as before.
let resolveRowsWithFields
    (sources: BindingSources)
    (binding: Binding<Row seq>)
    : Resolution<Row seq> * Fuaran.Core.Field list =
    match binding with
    | Binding.Transform(source, pipeline, parameters) ->
        match evalTransformFrame sources source pipeline (defaultArg parameters []) with
        | Error m -> Errored m, []
        | Ok result -> Resolved(Seq.ofList (rowsOfTable result)), result.Schema
    | other -> resolve<Row seq> sources other, []

// ─── A bound field's metadata at the read sites (Phase 2209) ─────────────────
//
// WIRE_FORMAT §3.6.27, stated once for both renderers and both tiers of each:
// where a slot reads a value from a field that states a `label` or a `unit`
// (§3.6.26), the renderer shows them — a header reads "Payload mass (kg)", an
// axis names its unit, a KPI value carries it, and an accessible name says it.
// The tree's own text always wins; the field supplies only what the tree left
// unstated. Every clause fires only on a STATED label or unit, so a document
// whose fields state none renders byte-for-byte as it did before.
//
// These are pure functions over a resolved schema. Resolution itself stays in
// `resolveRowsWithFields` / `resolveScalarWithField`, which read the field from
// the evaluation the slot already performs.
module FieldDisplay =

    /// The unit text a renderer shows: the field's canonical unit text, unless
    /// it states none or states the dimensionless `1` — a pure number, where
    /// "(1)" beside it would tell a reader nothing.
    ///
    /// The canonical text is `UnitOfMeasure`'s own rendering — the value Core's
    /// `Unit.render` returns, which is defined as exactly this override. It is
    /// called directly because under Fable the module path `Fuaran.Core.Unit`
    /// resolves to the union case `FieldChange.Unit` instead of the module.
    let unitText (field: Fuaran.Core.Field) : string option =
        field.Unit
        |> Option.map (fun (u: Fuaran.Core.UnitOfMeasure) -> u.ToString())
        |> Option.filter (fun u -> u <> "1")

    /// The label when stated and non-empty, else the column name.
    let displayName (field: Fuaran.Core.Field) : string =
        match field.Label with
        | Some label when label <> "" -> label
        | _ -> field.Name

    /// The column title — `Payload mass (kg)` — which EXISTS only when the
    /// field states a non-empty label or has a unit text. `None` is the
    /// renderer's cue to draw exactly what it drew before §3.6.27.
    let columnTitle (field: Fuaran.Core.Field) : string option =
        match unitText field, field.Label with
        | Some unit, _ -> Some(displayName field + " (" + unit + ")")
        | None, Some label when label <> "" -> Some label
        | None, _ -> None

    let private titleOf (fields: Fuaran.Core.Field list) (name: string) : string option =
        fields |> List.tryFind (fun f -> f.Name = name) |> Option.bind columnTitle

    /// A `DataGrid` with each EMPTY header of a data column replaced by its bound
    /// field's column title. A non-empty `label` is the author's header and is
    /// kept verbatim; an action column reads no field (`GridColumn.dataField`).
    /// With no fields — a source that is not a `Transform` — the spec is
    /// returned unchanged.
    let withColumnHeaders (fields: Fuaran.Core.Field list) (spec: DataGridSpec<'Msg>) : DataGridSpec<'Msg> =
        if List.isEmpty fields then
            spec
        else
            let header (col: ColumnErased<'Msg>) =
                if col.Label <> "" then
                    col
                else
                    match Fuaran.UI.GridColumn.dataField col |> Option.bind (titleOf fields) with
                    | Some title -> { col with Label = title }
                    | None -> col

            { spec with
                Columns = List.map header spec.Columns }

    /// A `Chart` with each ABSENT axis title filled from its bound field's
    /// column title: x from `XField`, y from the first of `YFields` (the field
    /// the capitalised fallback names). A filled title is a STATED one, so it
    /// draws on a temporal x axis too. A declared title, whatever its arm, wins.
    let withAxisTitles (fields: Fuaran.Core.Field list) (spec: ChartSpec<'Msg>) : ChartSpec<'Msg> =
        if List.isEmpty fields then
            spec
        else
            let fill (declared: TextSource option) (field: string option) =
                match declared with
                | Some _ -> declared
                | None -> field |> Option.bind (titleOf fields) |> Option.map TextSource.Literal

            { spec with
                XTitle = fill spec.XTitle (Some spec.XField)
                YTitle = fill spec.YTitle (List.tryHead spec.YFields) }

    /// The unit a read VALUE carries: the bound field's unit text, where the
    /// slot's format does not state a unit of its own. `Currency`, `Percent`,
    /// `Duration`, `RelativeTime` and `DateTime` already say what the figure
    /// is, and `Custom` is the host's — a second unit would contradict or repeat.
    let valueUnit (format: CellFormat) (field: Fuaran.Core.Field option) : string option =
        match format with
        | CellFormat.None
        | CellFormat.Number _
        | CellFormat.SignificantDigits _ -> field |> Option.bind unitText
        | CellFormat.Currency _
        | CellFormat.Percent _
        | CellFormat.DateTime _
        | CellFormat.Duration _
        | CellFormat.RelativeTime _
        | CellFormat.Custom _ -> None

    /// `text` followed by a space and `unit`, when there is one.
    let withUnit (unit: string option) (text: string) : string =
        match unit with
        | Some u -> text + " " + u
        | None -> text

    /// A caption the tree left EMPTY, filled with the bound field's display
    /// name when the field states metadata (it has a column title). A non-empty
    /// caption is the author's and is returned unchanged.
    let caption (field: Fuaran.Core.Field option) (explicitCaption: string) : string =
        if explicitCaption <> "" then
            explicitCaption
        else
            match field with
            | Some f when Option.isSome (columnTitle f) -> displayName f
            | _ -> explicitCaption

// ─── Conditional presence and predicate branching (Phase 1535) ───────────────
//
// Two decisions a renderer takes BEFORE it draws anything, and both are here
// rather than in either renderer because the two renderers must agree
// byte-for-byte on them: a node the client draws and the server omits is a
// hydration mismatch, and the whole point of SSR parity is that neither host
// gets to have its own opinion.

/// Fuaran-UI Phase 1535 — the resolution of a node's `visible` predicate, or
/// `None` when the node declares none.
///
/// `None` is NOT the same as an unresolved predicate. A node with no `visible`
/// slot has nothing to say and nothing to warn about; a node whose predicate
/// could not be resolved has a broken document behind it, and a host may want to
/// say so.
let nodeVisibility<'Msg> (sources: BindingSources) (node: Node<'Msg>) : Resolution<bool> option =
    node.Visible |> Option.map (resolveScalarBool sources)

/// Fuaran-UI Phase 1535 — THE rule for whether a node reaches the output at all.
///
/// A node is removed ONLY on a resolved `false`. Absent, `NotResolved`,
/// `Errored` and `I18nUnresolved` all render, and that asymmetry is the whole
/// design rather than a leniency:
///
///   - A `false` is an author saying "not now", and removal is what they asked
///     for — no element, no placeholder, no `aria-hidden`, nothing in the layout
///     and nothing in the accessibility tree. (That is what makes `visible`
///     different from `accessibility.hidden`, which is `aria-hidden` over a node
///     that IS drawn and DOES occupy space.)
///   - Every other outcome is the renderer failing to answer the question, and
///     content that vanishes because a source was missing is the one failure a
///     reader cannot see, cannot report and cannot work around. Rendering the
///     node leaves the failure visible to somebody.
let isNodeVisible<'Msg> (sources: BindingSources) (node: Node<'Msg>) : bool =
    match nodeVisibility sources node with
    | Some(Resolved v) -> v
    | Some NotResolved
    | Some(Errored _)
    | Some(I18nUnresolved _)
    | None -> true

/// Fuaran-UI Phase 1535 — first-match-wins case selection for a `Switch`,
/// over BOTH kinds of case: a literal `match` compared against the already
/// resolved selector, and a `when` predicate evaluated here.
///
/// `selector` is the switch's resolved `on` value (`None` when it did not
/// resolve). A predicate case ignores it entirely — which is why a switch whose
/// cases are all predicates needs no selector at all.
///
/// A predicate case is taken ONLY on a resolved `true`; `false`, `NotResolved`
/// and `Errored` all fall through to the next case and ultimately to `default`.
/// Note this is the OPPOSITE default from `isNodeVisible` above, and deliberately
/// so: falling through here lands on a `default` branch the author wrote, so no
/// content disappears — whereas a node with no `visible` verdict has no fallback
/// to land on.
///
/// A case carrying neither `match` nor `when` can never be selected. The decoder
/// refuses that shape and FUARAN142 reports it pre-emit, so it is unreachable
/// from the wire; it is handled here rather than asserted away because a tree
/// built in-process can still hold one and a renderer is total by signature.
let selectSwitchCase<'Msg>
    (sources: BindingSources)
    (selector: string option)
    (cases: SwitchCase<'Msg> list)
    : Node<'Msg> option =
    cases
    |> List.tryPick (fun c ->
        match c.Match, c.When with
        | Some m, _ -> (if selector = Some m then Some c.Child else None)
        | None, Some predicate ->
            match resolveScalarBool sources predicate with
            | Resolved true -> Some c.Child
            | Resolved false
            | NotResolved
            | Errored _
            | I18nUnresolved _ -> None
        | None, None -> None)

/// Best-effort scalar text resolution — the `tryResolve` twin for text slots.
let tryResolveScalarText (sources: BindingSources) (binding: Binding<string>) : string option =
    match resolveScalarText sources binding with
    | Resolved value -> Some value
    | NotResolved
    | Errored _
    | I18nUnresolved _ -> None

/// Fuaran-UI Phase 1661 — the display string ONE `TextSource.I18n` argument
/// substitutes into its template.
///
/// A LITERAL argument — `Binding.Static (Some v)`, which is what the bare wire
/// form decodes to and what every pre-1661 document carries — projects through
/// exactly the scalar/composite rule the slot used before it was widened, so no
/// existing caption changes a character. Any other arm resolves through the same
/// store-reading erasure `Binding.I18n`'s own args take (`objOfJValBinding` and
/// `resolve<obj>`), and an unresolvable one substitutes the empty string rather
/// than leaving `{name}` visible in the sentence — the `Bound` arm's degradation
/// one level down.
///
/// `bool` is projected explicitly rather than through `string`, because `string`
/// disagrees with itself across the two pipelines (`"True"` on .NET, `"true"`
/// under Fable) and this slot's own literal arm has always spelled it lower-case.
let private i18nArgText (sources: BindingSources) (arg: Binding<JVal>) : string =
    let display (v: JVal) =
        match v with
        | JStr s -> s
        | JInt i -> string i
        | JFloat f -> string f
        | JBool b -> (if b then "true" else "false")
        | composite -> Json.render composite

    match arg with
    | Binding.Static(Some v) -> display v
    | other ->
        match resolve<obj> sources (objOfJValBinding other) with
        | Resolved v ->
            match v with
            | :? JVal as jv -> display jv
            | :? string as s -> s
            | :? bool as b -> (if b then "true" else "false")
            | raw -> string raw
        | NotResolved
        | Errored _
        | I18nUnresolved _ -> ""

/// Resolve a whole `TextSource` to the string a reader sees — THE one
/// definition of that dispatch in the estate.
///
/// Phase 1126 put a `TextSource` in a third place (`Action.WriteToClipboard`'s
/// payload, resolved at dispatch time rather than at render time), and the
/// dispatch had until then been written out twice: once in the client renderer's
/// `renderText` and once, byte-identically by hand, in the SSR renderer's. A
/// third hand-written copy is how the two would eventually disagree about what a
/// bound label says — which is precisely the divergence `ScalarSsrParityTests`
/// exists to catch, and it should not need to catch a copy nobody had to write.
/// Both renderers now call this; so does the server-driven driver.
///
/// The three arms are the pre-existing semantics, unchanged: a literal is
/// itself; a `Bound` binding resolves through the SCALAR path (so a
/// `Binding.Transform` yields its 1x1 result cell, never the rows list) and an
/// unresolvable one reads as the empty string; an `I18n` key missing from the
/// catalogue reads as the loud `[i18n:<key>]` sentinel rather than as nothing,
/// so an unregistered key is caught at sight. Args substitute `{name}`
/// placeholders — scalars as their display string, a composite as compact
/// canonical JSON. Full ICU-shape interpolation remains an ergonomic upgrade.
///
/// Fuaran-UI Phase 1661 — an arg is a `Binding<JVal>`, so a placeholder can take
/// its value from the same slot the surrounding page reads; `i18nArgText` above
/// is the per-argument projection and keeps the literal arm's characters
/// unchanged.
let resolveTextSource (sources: BindingSources) (text: TextSource) : string =
    match text with
    | TextSource.Literal s -> s
    | TextSource.Bound binding -> tryResolveScalarText sources binding |> Option.defaultValue ""
    | TextSource.I18n(key, args) ->
        match Map.tryFind key sources.I18n with
        | Some template ->
            args
            |> Map.fold
                (fun (acc: string) (k: string) (arg: Binding<JVal>) ->
                    let needle = "{" + k + "}"
                    acc.Replace(needle, i18nArgText sources arg))
                template
        | None -> sprintf "[i18n:%s]" key

/// Phase 1536 — resolve a `TextSource` for a slot where an UNRESOLVED source
/// must not degrade into a value.
///
/// `resolveTextSource` above is the RENDERING dispatch, and its degradations are
/// right for rendering: a bound source that does not resolve renders as the
/// empty string (an empty label, not a broken page), and a missing translation
/// renders as the loud `[i18n:<key>]` sentinel so it is visible in the UI rather
/// than silently blank. Both are the wrong answer for a DESTINATION. Navigating
/// to `""` is navigating to the current document with its query and fragment
/// stripped — a real navigation the author never asked for — and `[i18n:route]`
/// is a relative path that would resolve against the host's origin and, on a
/// permissive policy, actually be fetched.
///
/// So this returns `None` for exactly those two cases and `Some` otherwise,
/// leaving the caller to warn and do nothing. It deliberately does NOT judge the
/// resolved string: whether a destination is permitted is `Sanitize.checkDestination`'s
/// question, asked after this one and never instead of it.
let tryResolveTextSource (sources: BindingSources) (text: TextSource) : string option =
    match text with
    | TextSource.Literal s -> Some s
    | TextSource.Bound binding -> tryResolveScalarText sources binding
    | TextSource.I18n(key, _) ->
        if Map.containsKey key sources.I18n then
            Some(resolveTextSource sources text)
        else
            None

/// Best-effort scalar float resolution — the `tryResolve` twin for numeric slots.
let tryResolveScalarFloat (sources: BindingSources) (binding: Binding<float>) : float option =
    match resolveScalarFloat sources binding with
    | Resolved value -> Some value
    | NotResolved
    | Errored _
    | I18nUnresolved _ -> None

// ─── Row-field projection contract (Phase 425) ───────────────────────────────
//
// The declarative-grid floor: a decoded `DataGrid` column names a row property
// (`Field`) / the spec names a row-key property (`RowKeyField`) instead of a
// host closure. The projection reads that property off a row `obj` and coerces
// it to a `CellValue`. A `Binding.Transform` produces rows as `Map<string,obj>`
// (each cell scalar boxed), the canonical embedded-data shape; a `Static obj
// seq` may carry any boxed row. Missing field ⇒ `CellValue.Empty` (never a
// throw). Parity-locked with the TS renderer's `projectRowField`.

/// Coerce a boxed row-cell value to a `CellValue`. Numeric int/float both map to
/// `Numeric` (cross-host-deterministic — JS erases the int/float distinction).
///
/// Phase 1491 — the body moved to `Fuaran.UI.HostPrelude.RowProjection`, which compiles
/// ahead of the pre-emit validator; this stays as the name every caller here
/// already uses. One definition, two readers: a validator rule that grounds an
/// authored key against the labels a chart will draw must classify a cell
/// exactly as the renderer does, or it refuses keys the picture goes on to draw.
let objToCellValue (v: obj) : CellValue =
    Fuaran.UI.HostPrelude.RowProjection.ofObj v

/// Project a named field off a `Row` to a `CellValue`; a missing key is
/// `CellValue.Empty`. The row-field display floor for a decoded grid column.
///
/// fuaran#665 — the rows slot is statically `Row = Map<string,obj>` end to end
/// (typed wire payload, typed `Transform` output, typed facade projection), so
/// the old Fable `#if`-split unbox hack — needed when the slot was `obj` and a
/// `:? Map<string,obj>` type-test evaluated false under Fable — is gone: no
/// runtime test exists to get wrong.
let projectRowFieldValue (row: Row) (field: string) : CellValue =
    Fuaran.UI.HostPrelude.RowProjection.value row field

/// Project a named field off a `Row` to a string (the row-key floor). Empty
/// string when the field is missing (the caller may fall back to the row index).
let projectRowFieldString (row: Row) (field: string) : string =
    Fuaran.UI.HostPrelude.RowProjection.string_ row field

// ─── Grid and tree: the State reads, and the names that moved (Phase 2070) ───
//
// The grid and tree MODELS — sort order, pages, the row window and its
// viewport writer, the toned pill, the write destinations, the row move, tree
// expansion and focus — are `GridModel` and `TreeModel`: none of it is binding
// resolution, so none of it lives here. What stays is the part that IS: the
// reads of a `State` key (and of a declared `rowTotal` binding) that feed those
// models, each validating what it read through the model's own decoder.
//
// The names that moved are not re-exported: callers use `GridModel` / `TreeModel`
// directly (Phase 2235).

/// The `JVal` a `State` key holds, or `None` when the key is unset or its value
/// is not a JSON value.
let private stateJVal (sources: BindingSources) (key: string) : JVal option =
    Map.tryFind key sources.State |> Option.bind jvalOfResolved

/// Phase 818 — read the sort descriptor carried at `key` in the State store,
/// validated by `GridModel.sortDescriptorOfJVal`: a malformed descriptor reads
/// as "no sort", so the authored order stands (never an arbitrary one).
let readSortDescriptor (sources: BindingSources) (key: string) : (int * Fuaran.UI.Types.SortDirection) option =
    stateJVal sources key |> Option.bind GridModel.sortDescriptorOfJVal

/// Phase 861 — what the sort state key is SAYING, as three cases rather than
/// two. `readSortDescriptor` above collapses "nothing written" and "written but
/// not a sort" into `None`, which was right while the only alternative to a
/// sort was the authored order. It stops being right once a grid can declare an
/// initial order: the declared order applies when the user has not sorted, and
/// the AUTHORED order must still be reachable by cycling past descending
/// (Phase 801's ascending → descending → authored cycle, adopted for the bound
/// path by operator decision 2026-08-16). Those are different states and the
/// wire already distinguishes them — an absent key versus a key holding an
/// object with no usable descriptor.
type SortSlot =
    /// The key holds nothing — the user has not sorted, so a declared
    /// `defaultSort` applies.
    | NotSorted
    /// The key holds a usable descriptor.
    | SortedBy of column: int * direction: Fuaran.UI.Types.SortDirection
    /// The key holds something that is not a usable descriptor — which the
    /// header cycle writes deliberately (an empty object) to mean "back to the
    /// order the emitter wrote". A malformed value a host seeded lands here
    /// too, and reading it as the authored order is the same
    /// validate-rather-than-trust posture: never an arbitrary order.
    | Cleared

/// Read the sort state key as a three-way slot. Kept beside
/// `readSortDescriptor` rather than replacing it: that function is the
/// shipped 818 surface and other callers read it, so this widens the
/// vocabulary without moving anyone's floor.
let readSortSlot (sources: BindingSources) (key: string) : SortSlot =
    match Map.tryFind key sources.State with
    | None -> NotSorted
    | Some _ ->
        match readSortDescriptor sources key with
        | Some(c, d) -> SortedBy(c, d)
        | None -> Cleared

/// The effective sort for a bound grid: the state slot decides, and a declared
/// initial order fills only the not-yet-sorted case. A grid with no sort state
/// key at all still honours its declared order — an initial presentation order
/// without interactive re-sorting is a legitimate shape, exactly as it is for a
/// static table.
let effectiveSortDescriptor
    (sortStateKey: string option)
    (defaultSort: DefaultSort option)
    (sources: BindingSources)
    : (int * Fuaran.UI.Types.SortDirection) option =
    let declared = defaultSort |> Option.map (fun d -> d.Column, d.Direction)

    match sortStateKey with
    | None -> declared
    | Some key ->
        match readSortSlot sources key with
        | NotSorted -> declared
        | SortedBy(c, d) -> Some(c, d)
        | Cleared -> None

/// Phase 1120 — the rows a `Tree` currently has OPEN, read off the named State
/// key and decoded by `TreeModel.expandedOfJVal` (every non-conforming shape is
/// the EMPTY SET). No key named, or an unset key, is the empty set too.
let readExpandedItems (sources: BindingSources) (key: string option) : Set<string> =
    key
    |> Option.bind (stateJVal sources)
    |> Option.map TreeModel.expandedOfJVal
    |> Option.defaultValue Set.empty

/// Phase 1120 — the row a `Tree` currently has SELECTED, read off the named
/// State key and decoded by `TreeModel.selectedOfJVal` (a bare, non-empty row-id
/// string; anything else is `None`).
let readSelectedItem (sources: BindingSources) (key: string option) : string option =
    key |> Option.bind (stateJVal sources) |> Option.bind TreeModel.selectedOfJVal

/// Phase 862 — read the page descriptor carried at `key` in the State store:
/// `{"page": N}`, 1-based, validated by `GridModel.pageOfJVal`. A malformed or
/// absent descriptor reads as page 1, which is the honest default (never an
/// arbitrary offset).
let readPageDescriptor (sources: BindingSources) (key: string) : int =
    stateJVal sources key
    |> Option.bind GridModel.pageOfJVal
    |> Option.defaultValue 1

/// Phase 1892 — read the window descriptor carried at `key` in the State store,
/// validated by `GridModel.windowOfJVal`.
let readWindowDescriptor (sources: BindingSources) (key: string) : GridModel.RowWindow option =
    stateJVal sources key |> Option.bind GridModel.windowOfJVal

/// Resolve a declared `rowTotal`: an integer >= 0, or `None` — no declared total,
/// so the total is unknown rather than guessed. Every store-held spelling goes
/// through [[jvalOfResolved]] and `GridModel.jvalInt`, so a total written as
/// `120.0`, as an `int64` or as a JSON element reads the same, and a fractional
/// or negative one is refused on every host — including under Fable, where an
/// `unbox<int>` of `2.5` would succeed and smuggle a fraction into a count. A
/// `Transform` / `Expr` total resolves through the scalar-slot path and must
/// yield an integral cell.
let resolveRowTotal (sources: BindingSources) (rowTotal: Binding<int> option) : int option =
    let lifted (raw: obj) : int option =
        jvalOfResolved raw |> Option.bind GridModel.jvalInt

    let integralCell (c: Fuaran.Core.Cell) : Result<int, string> =
        match cellToFloat c with
        | Ok f when
            floor f = f
            && f <= float System.Int32.MaxValue
            && f >= float System.Int32.MinValue
            ->
            Ok(int f)
        | Ok f -> Error(sprintf "a row total must be an integer (got %g)" f)
        | Error m -> Error m

    rowTotal
    |> Option.bind (fun binding ->
        match binding with
        | Binding.Static(Some n) -> Some n
        | Binding.Static None -> None
        | Binding.State(key, fallback) ->
            match Map.tryFind key sources.State with
            | Some raw -> lifted raw
            | None -> fallback
        | Binding.Query(name, _, _) ->
            // The accessor first — a host-authored Query may project the total out
            // of a richer response — then the raw value, which is what the decoded
            // identity accessor would have handed back anyway.
            match resolve sources binding with
            | Resolved v -> lifted (box v)
            | _ -> Map.tryFind name sources.QueryResults |> Option.bind lifted
        | Binding.Filter(name, fallback) ->
            match Map.tryFind name sources.Filters with
            | Some raw -> lifted raw
            | None -> fallback
        | other ->
            match resolveScalarWith integralCell sources other with
            | Resolved n -> Some n
            | _ -> None)
    |> Option.filter (fun n -> n >= 0)

/// Phase 1892 — the page a paged grid shows and the last page it can name, off
/// the page rule plus the declared total. A client-paged grid counts its own
/// rows; a HOST-paged grid clamps and states a page count only when the document
/// declares `rowTotal`, and otherwise keeps to "page N" with previous/next —
/// which is what it did before 1892. `None` for a grid that does not page.
/// Returns `(pageKey, pageSize, page, hostPages, lastPage)`.
let gridPage
    (sources: BindingSources)
    (spec: DataGridSpec<'Msg>)
    (rowCount: int)
    : (string * int * int * bool * int option) option =
    match spec.PageStateKey, spec.PageSize with
    | Some key, Some size when size > 0 ->
        let hostPages = GridModel.sourceHostPagesOn spec.Source key
        let requested = readPageDescriptor sources key

        let lastPage =
            if hostPages then
                GridModel.declaredPageCount size (resolveRowTotal sources spec.RowTotal)
            else
                Some(GridModel.pageCountOf size rowCount)

        let page =
            match lastPage with
            | Some last -> min (max 1 requested) last
            | None -> max 1 requested

        Some(key, size, page, hostPages, lastPage)
    | _ -> None

/// Phase 1892 — the window a grid presents over `range` (the rows it holds after
/// sort and page), or `None` for a grid naming no window key, which renders
/// exactly as it did before 1892. The declared total is read only where the host
/// windows; everywhere else the range is the grid's own and counts itself.
let gridWindow
    (sources: BindingSources)
    (spec: DataGridSpec<'Msg>)
    (range: 'row list)
    : GridModel.PresentedWindow<'row> option =
    spec.WindowStateKey
    |> Option.map (fun key ->
        let hostWindows = GridModel.sourceHostWindowsOn spec.Source key

        let declared =
            if hostWindows then
                resolveRowTotal sources spec.RowTotal
            else
                None

        GridModel.presentWindow hostWindows declared (readWindowDescriptor sources key) range)

// ─── Options resolution + the `<opaque>` non-array source contract ─────────
//
// Phase 131: a non-array `Static` options source the encoder could not
// serialise round-trips to the `"<opaque>"` sentinel (WIRE_FORMAT.md §5). The
// decoder rebuilds it as a single-element placeholder whose value and label are
// both the sentinel — a NON-NULL ref, so re-encode stays byte-stable. That
// placeholder must NOT reach the DOM: it is an erased source, not a real
// option. The cross-host contract is **render no concrete options** for an
// opaque / non-array options source (the TS renderer's `asArray` coercion), so
// every options-bearing control on both .NET renderers strips it here
// (Phase 2108: one definition where each renderer carried a copy).

let private opaqueOptionsSentinel = "<opaque>"

let private isOpaqueOptionPlaceholder (option: SelectOption) : bool =
    option.Value = opaqueOptionsSentinel && option.Label = opaqueOptionsSentinel

/// The concrete options an options source resolves to, the opaque placeholder
/// removed.
let resolveOptions (sources: BindingSources) (binding: Binding<SelectOption list>) : SelectOption list =
    let resolved = tryResolve sources binding |> Option.defaultValue []

    // Per-render hot path (Phase 207): `List.filter` allocates a fresh list even
    // when it removes nothing, and the placeholder is a decode-time rarity. Ask
    // first, copy only when there is something to drop. Result-identical; do
    // NOT "simplify" back to a bare `List.filter`.
    if resolved |> List.exists isOpaqueOptionPlaceholder then
        resolved |> List.filter (isOpaqueOptionPlaceholder >> not)
    else
        resolved
