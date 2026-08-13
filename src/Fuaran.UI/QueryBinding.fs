module Fuaran.UI.QueryBinding

// ============================================================================
//  Query-result schema → Binding<'T> typed-thread check (Phase 323).
//
//  "The query result schema *is* the UI contract." A query column typed
//  `string` must not be bound into a numeric UI sink (a Metric, a chart
//  Y-series, a numeric axis); the mismatch is a typed, named defect surfaced
//  *before render*. An AI can lie in prose — it must not be able to emit a
//  chart that plots a `string` column on a numeric scale.
//
//  RUNTIME, NOT STATIC (the de-risked design pivot — Phase 323).
//  The phase prose first imagined a "default-deny validator rule". But
//  `Fuaran.UI.Validator` is a *static* F# AST walker over source files (FCS):
//  it cannot see a runtime query schema, and does not need to for the static
//  case — F#'s own type system already enforces `Binding<float> ≠ string` at
//  compile time (a `Binding.Query "x" _.amount` only type-checks if `.amount`
//  is a float). The real gap is a column type known **only when a dynamic
//  query resolves at runtime**. So this is a runtime relation in `Fuaran.UI`:
//  a pure `ColumnType → BindingSinkClass` compatibility function + a runtime
//  check that, given a resolved query `Schema` and a `Node` tree's query-bound
//  slots, returns typed defects. No `*Check.fs` AST walker.
//
//  SCOPE / SCHEMA MODEL. `check schema node` validates every query-bound
//  *column reference* in `node` against ONE resolved query result `Schema`
//  (`Fuaran.Core.Schema` = `(name * ColumnType) list`). The references it reads:
//   - a scalar `Binding.Query(name, _)` sitting in a typed sink slot — here the
//     binding's `name` is resolved as the column key in `schema` (the typed
//     accessor `obj -> 'T` is an opaque closure the runtime cannot introspect,
//     so the *named* query/column is the addressable handle the schema keys on);
//   - a `NodeKind.Chart` whose `Source` is itself a `Binding.Query` — its
//     `XField` (axis) and each `YField` (series) name columns of that query's
//     result, checked directly as column strings.
//  A multi-query dashboard calls `check` once per (query-scoped subtree, schema).
//
//  DEFAULT-DENY BY SHAPE (FGP 3). A binding that references a column ABSENT
//  from the schema is a defect, not a silent pass; an unknown `ColumnType` ×
//  sink pairing is incompatible (`false`), never assumed-safe.
//
//  FABLE + .NET PARITY (FGP 4). FSharp.Core + `Fuaran.Core.Column` only — no
//  `System.*` that breaks Fable, no reflection, no `obj` peek-through. The
//  relation runs byte-identically under both pipelines, exactly like
//  `PreEmitValidate`.
//
//  AI-RECOVERY DISCIPLINE (§4d). Each `Defect` carries a stable `Code`
//  (FUARAN066 / FUARAN067), the offending node + column, and recovery fields
//  (`AvailableFields` + `Suggestion`) — the same shape `Fuaran.UI.Validator`'s
//  `Findings.withRecovery` populates. `Findings` lives in the downstream
//  Validator package (it references `Fuaran.UI`), so the runtime defect type is
//  *defined here* rather than imported, mirroring the discipline without the
//  circular dependency.
// ============================================================================

open Fuaran.UI.Types
open Fuaran.Core

/// The scalar *class* a query-bindable UI sink accepts. The taxonomy IS the
/// relation: each query-bindable slot in the typed tree (`Types.fs`) maps to
/// exactly one class, fixed by the slot's position — a `Metric.Source` is
/// `Numeric`, a chart `XField` is `Categorical`, and so on. Closed by intent
/// (a new class is an additive case, never an open extension point).
[<RequireQualifiedAccess>]
type BindingSinkClass =
    /// Plots / measures on a numeric scale — Metric value + trend, LabelValueRow
    /// value, Progress fraction, Sparkline series, chart Y-series, numeric form
    /// fields. Accepts `int` / `float` columns only.
    | Numeric
    /// Reads a point in time — a time axis, a date filter, a date/time field.
    /// Accepts `date` / `timestamp` columns.
    | Temporal
    /// A label / grouping / facet / category axis. Accepts `string` (and a
    /// `date` / `timestamp` rendered *as a label*).
    | Categorical
    /// A toggle / flag. Accepts `bool` columns only.
    | Boolean

module BindingSinkClass =

    /// A short human name for a sink class (for defect messages).
    let label (s: BindingSinkClass) : string =
        match s with
        | BindingSinkClass.Numeric -> "numeric"
        | BindingSinkClass.Temporal -> "temporal"
        | BindingSinkClass.Categorical -> "categorical"
        | BindingSinkClass.Boolean -> "boolean"

/// The single source of truth for query-column-type ↔ sink compatibility.
/// Pure, total, and DEFAULT-DENY: every pairing not explicitly allowed is
/// `false` (the trailing wildcard), so an unknown / future pairing is treated
/// as incompatible rather than silently accepted (FGP 3).
///
///   numeric sink   ← int | float
///   temporal sink  ← date | timestamp
///   categorical    ← string | date | timestamp   (date/timestamp render as a label)
///   boolean sink   ← bool
let compatible (col: ColumnType) (sink: BindingSinkClass) : bool =
    match col, sink with
    | IntType, BindingSinkClass.Numeric -> true
    | FloatType, BindingSinkClass.Numeric -> true
    | DateType, BindingSinkClass.Temporal -> true
    | TimestampType, BindingSinkClass.Temporal -> true
    | StringType, BindingSinkClass.Categorical -> true
    | DateType, BindingSinkClass.Categorical -> true
    | TimestampType, BindingSinkClass.Categorical -> true
    | BoolType, BindingSinkClass.Boolean -> true
    | _ -> false

/// The column types a sink accepts — the closed-set companion to `compatible`,
/// derived from it (one source of truth) over `ColumnType.all`.
let compatibleColumnTypes (sink: BindingSinkClass) : ColumnType list =
    ColumnType.all |> List.filter (fun t -> compatible t sink)

/// A query-bound column reference discovered in a tree: the column name the
/// binding addresses, the sink class its slot fixes, and the host node's id
/// (for the defect's location). Surfaced by `queryBoundRefs`; consumed by
/// `check`.
type QueryBoundRef =
    { Column: string
      Sink: BindingSinkClass
      NodeId: string }

/// A typed query-binding defect. Mirrors `Fuaran.UI.Validator.Findings.Finding`
/// (+ `withRecovery`) without importing it (that type is downstream of this
/// package). `AvailableFields` is the §4d AI-recovery hint — for a mismatch,
/// the schema columns that WOULD type-check into this sink; for an absent
/// column, the schema columns that exist.
type Defect =
    { Code: string
      NodeId: string
      Column: string
      Sink: BindingSinkClass
      Message: string
      AvailableFields: string list
      Suggestion: string option }

/// **FUARAN066 (Error)** — a query column is bound into a sink whose scalar
/// class it is not compatible with (e.g. a `string` column into a `numeric`
/// Metric / chart-Y).
[<Literal>]
let TypeMismatchCode = "FUARAN066"

/// **FUARAN067 (Error)** — a query-bound slot references a column ABSENT from
/// the resolved schema (default-deny by shape, FGP 3 — an unresolved column is
/// a defect, not a silent pass).
[<Literal>]
let ColumnAbsentCode = "FUARAN067"

// ─── Query-bound-ref extraction (the walk) ──────────────────────────────────
//
// Mirrors `PreEmitValidate.validate`'s depth-first pre-order walk over
// `Node<'Msg>.Kind` — direct pattern match, no reflection, Fable-clean. Each
// query-bindable sink slot contributes a `QueryBoundRef` tagged with the sink
// class its *position* fixes; non-query bindings (Static / State / Filter / …)
// contribute nothing.
//
// Sinks covered (the taxonomy from Types.fs):
//   Numeric     — Metric.Source, Metric.Trend, LabelValueRow.Source,
//                 Progress.Fraction, Sparkline.Source, chart YFields,
//                 form Number / RangedNumber values.
//   Temporal    — form Date field value.
//   Categorical — chart XField (axis), form Text / TextArea / Choice /
//                 SegmentedChoice values.
//   Boolean     — form Checkbox value.
//
// Deliberately NOT covered (documented): `TextSource.Bound` label sinks (any
// column renders as a label string — universally compatible, no restrictive
// class to enforce) and the `Disabled : Binding<bool> option` slots on
// Button / Select / Form / FileUpload (rarely query-bound). See roadmap
// TIDY-UP if the Disabled coverage is later wanted.

/// The query name a binding addresses, or `None` for a non-query binding.
/// Generic over `'T`, so it reads any typed sink slot uniformly.
let private queryRef (b: Binding<'T>) : string option =
    match b with
    | Binding.Query(name, _, _) -> Some name
    | _ -> None

let private queryBoundRefsOfNode (n: Node<'Msg>) : QueryBoundRef list =
    // `Node.Id` is a bare string since the swap.
    let nid = n.Id
    let acc = ResizeArray<QueryBoundRef>()

    let add (sink: BindingSinkClass) (b: Binding<'T>) =
        match queryRef b with
        | Some col ->
            acc.Add
                { Column = col
                  Sink = sink
                  NodeId = nid }
        | None -> ()

    let addOpt (sink: BindingSinkClass) (b: Binding<'T> option) =
        match b with
        | Some binding -> add sink binding
        | None -> ()

    // This walk was already selective (each category ended in `| _ -> ()`), so
    // the flat form keeps the same posture: named sinks, then a catch-all.
    match n.Kind with
    | NodeKind.Metric spec ->
        add BindingSinkClass.Numeric spec.Value
        addOpt BindingSinkClass.Numeric spec.Trend
    | NodeKind.LabelValueRow spec -> add BindingSinkClass.Numeric spec.Value
    | NodeKind.Progress spec -> add BindingSinkClass.Numeric spec.Fraction
    | NodeKind.Sparkline spec -> add BindingSinkClass.Numeric spec.Source
    | NodeKind.Chart spec ->
        // The axis/series field names resolve against the schema only when
        // the chart's data IS the resolved query (Source = Binding.Query).
        // A static / transform-sourced chart names columns of a different
        // source the passed schema does not describe — skip it.
        match spec.Source with
        | Binding.Query _ ->
            acc.Add
                { Column = spec.XField
                  Sink = BindingSinkClass.Categorical
                  NodeId = nid }

            for yf in spec.YFields do
                acc.Add
                    { Column = yf
                      Sink = BindingSinkClass.Numeric
                      NodeId = nid }
        | _ -> ()
    | NodeKind.Form spec ->
        // Value slots are OPTIONS since Phase 596 (the symmetric auto-bind):
        // an omitted slot binds `$state.<field id>` — never a query column —
        // so an absent value contributes no query-bound ref to the walk.
        for field in spec.Fields do
            match field.Kind with
            | FormFieldKind.Number(value, _) -> addOpt BindingSinkClass.Numeric value
            | FormFieldKind.RangedNumber(value, _, _, _, _) -> addOpt BindingSinkClass.Numeric value
            | FormFieldKind.Range(value, _, _, _, _) -> addOpt BindingSinkClass.Numeric value
            | FormFieldKind.Checkbox(value, _) -> addOpt BindingSinkClass.Boolean value
            | FormFieldKind.Toggle(value, _) -> addOpt BindingSinkClass.Boolean value
            | FormFieldKind.Date(value, _, _, _, _, _) -> addOpt BindingSinkClass.Temporal value
            | FormFieldKind.DateRange(value, _, _, _, _, _) -> addOpt BindingSinkClass.Temporal value
            | FormFieldKind.Text(value, _) -> addOpt BindingSinkClass.Categorical value
            | FormFieldKind.TextArea(value, _, _) -> addOpt BindingSinkClass.Categorical value
            | FormFieldKind.Choice(_, value, _) -> addOpt BindingSinkClass.Categorical value
            | FormFieldKind.SegmentedChoice(_, value, _, _) -> addOpt BindingSinkClass.Categorical value
    | _ -> ()

    List.ofSeq acc

/// Walk `node` (depth-first, pre-order) and surface every query-bound column
/// reference, each tagged with the sink class its slot fixes. Mirrors
/// `PreEmitValidate`'s child enumeration so coverage tracks the tree shape.
let queryBoundRefs (node: Node<'Msg>) : QueryBoundRef list =
    let acc = ResizeArray<QueryBoundRef>()

    let rec walk (n: Node<'Msg>) =
        acc.AddRange(queryBoundRefsOfNode n)

        match n.Kind with
        // -- Layout --
        | NodeKind.Box spec -> spec.Children |> List.iter walk
        | NodeKind.SplitPanel spec -> spec.Children |> List.iter walk
        | NodeKind.Tabs spec -> spec.Children |> List.iter walk
        | NodeKind.Stepper spec -> spec.Children |> List.iter walk
        | NodeKind.SummaryList spec -> spec.Children |> List.iter walk
        | NodeKind.Disclosure spec -> spec.Children |> List.iter walk
        | NodeKind.Modal spec -> spec.Children |> List.iter walk
        | NodeKind.ScrollArea spec -> spec.Children |> List.iter walk
        | NodeKind.ErrorBoundary spec ->
            walk spec.Child
            walk spec.Fallback
        | NodeKind.Switch spec ->
            spec.Cases |> List.iter (fun c -> walk c.Child)
            walk spec.Default
        | NodeKind.FragmentDecl spec -> walk spec.Body
        // Every childless kind — Display / Input / Visualisation leaves.
        | NodeKind.Heading _
        | NodeKind.Markdown _
        | NodeKind.Metric _
        | NodeKind.Badge _
        | NodeKind.Sparkline _
        | NodeKind.Callout _
        | NodeKind.Progress _
        | NodeKind.Skeleton _
        | NodeKind.Icon _
        | NodeKind.LabelValueRow _
        | NodeKind.Fact _
        | NodeKind.Link _
        | NodeKind.Image _
        | NodeKind.List _
        | NodeKind.Toast _
        | NodeKind.CodeBlock _
        | NodeKind.Math _
        | NodeKind.Drawing _
        | NodeKind.Form _
        | NodeKind.Filters _
        | NodeKind.Button _
        | NodeKind.FileUpload _
        | NodeKind.Select _
        | NodeKind.DataGrid _
        | NodeKind.Chart _
        | NodeKind.Map _
        | NodeKind.Custom _
        | NodeKind.FragmentRef _
        // Mount (§4o) is an opaque isolation boundary — the guest carries its
        // own scoped query-coverage in its own scope; host-level query-bound
        // ref collection stops at the boundary (same posture as FragmentRef).
        | NodeKind.Mount _ -> ()

    walk node
    List.ofSeq acc

// ─── The check ───────────────────────────────────────────────────────────────

let private columnNames (schema: Schema) : string list = schema |> List.map fst

let private mismatchDefect (schema: Schema) (colType: ColumnType) (r: QueryBoundRef) : Defect =
    let compatibleCols =
        schema |> List.filter (fun (_, t) -> compatible t r.Sink) |> List.map fst

    let suggestion =
        match compatibleCols with
        | [] ->
            Some(
                sprintf
                    "no column in the resolved schema is %s-compatible — bind a %s column or change the sink"
                    (BindingSinkClass.label r.Sink)
                    (BindingSinkClass.label r.Sink)
            )
        | first :: _ ->
            Some(sprintf "bind a %s-compatible column instead, e.g. '%s'" (BindingSinkClass.label r.Sink) first)

    { Code = TypeMismatchCode
      NodeId = r.NodeId
      Column = r.Column
      Sink = r.Sink
      Message =
        sprintf
            "column '%s' is typed %s but is bound into a %s sink (incompatible)"
            r.Column
            (ColumnType.tag colType)
            (BindingSinkClass.label r.Sink)
      AvailableFields = compatibleCols
      Suggestion = suggestion }

let private absentDefect (schema: Schema) (r: QueryBoundRef) : Defect =
    let available = columnNames schema

    { Code = ColumnAbsentCode
      NodeId = r.NodeId
      Column = r.Column
      Sink = r.Sink
      Message =
        sprintf
            "column '%s' is bound into a %s sink but is absent from the resolved query schema"
            r.Column
            (BindingSinkClass.label r.Sink)
      AvailableFields = available
      Suggestion =
        match available with
        | [] -> Some "the resolved schema has no columns — the query returned no schema"
        | first :: _ -> Some(sprintf "bind a column that exists in the schema, e.g. '%s'" first) }

/// Check every query-bound column reference in `node` against the resolved
/// query result `schema`. Returns `[]` when every binding types cleanly, else
/// one `Defect` per incompatible / absent binding (NOT short-circuited — every
/// defect surfaces so the AI repairs the whole tree in one turn).
///
/// SCHEMA-ONLY (no-rows) TYPING. The check reads only the schema's
/// `(name, ColumnType)` pairs — never any row data — so it types a complete
/// dashboard against a schema with **zero rows** (the privacy-mode path,
/// Phase 324). A schema-only fetch is sufficient to prove the UI contract.
let check (schema: Schema) (node: Node<'Msg>) : Defect list =
    queryBoundRefs node
    |> List.choose (fun r ->
        match schema |> List.tryFind (fun (name, _) -> name = r.Column) with
        | None -> Some(absentDefect schema r)
        | Some(_, colType) ->
            if compatible colType r.Sink then
                None
            else
                Some(mismatchDefect schema colType r))
