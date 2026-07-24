module Fuaran.UI.QuerySource

// ============================================================================
//  Client query-source seam + typed resolve→check→render gate (Phase 323 task 1
//  + the Phase 324 typed core). The async, UI-facing half of the BYO-data thread:
//  a host-pluggable source resolves a request into a typed result (schema ± rows),
//  and the dashboard is gated against that schema by `QueryBinding.check` BEFORE
//  render — a type-mismatched dashboard never reaches the DOM.
//
//  RELATION TO `Fuaran.Core.Query` (fuaran-core#46). The Core ships the
//  declarative data-acquisition contract — `Query` (with `ResultSchema`),
//  `QueryResult`, `QueryError`, the default-deny `QueryRegistry`, typed param
//  validation, and `Query.invoke (resolve: Query -> Result<QueryResult,string>)`.
//  Its header states the **async envelope is not yet shipped — the host wraps the
//  synchronous Core resolver in its own async at the boundary** (and points here,
//  fuaran#323). This module is that async wrapper + the UI gate. It is expressed
//  over the already-pinned `Fuaran.Core.Column` (`Schema` / `Table` / `DataSource`)
//  + `Fuaran.Core.DataFrame` (`Transform`) types, NOT `Fuaran.Core.Query` — so it
//  takes no new pin and stays the interim, portability-clean seam the phase
//  sanctions. Wiring a concrete `IClientQuerySource` into `QueryRegistry.dispatch`
//  (param validation + Phase 27 capture keying) is the follow-on when real drivers
//  (HTTP serverless DB / DuckDB-WASM) land — see [Phase 324].
//
//  PORTABILITY (the 323 acceptance — fuaran-core portability audit). The seam is
//  FSharp.Core + `Fuaran.Core.Column`/`DataFrame`-only, Fable-portable,
//  async-at-the-boundary, stateless, identity-by-value — so HTTP / WASM /
//  paired-device implementations all conform without touching the abstraction.
//  No `System.*`, no reflection: the same gate runs under Fable (the fuaran-live
//  fable-host) and .NET (these Expecto tests) identically (FGP 4).
//
//  PRIVACY MODE (Phase 324). `QueryResolution.SchemaOnly` types a complete
//  dashboard from the `(name, ColumnType)` schema alone — no row value ever
//  crosses to the typing path. `resolveAndCheck … schemaOnly:true` is the
//  no-data governance path: the UI is proven sound against structure, never data.
// ============================================================================

open Fuaran.UI.Types
open Fuaran.Core

/// What the host resolves into rows. `Transform` is the serialisable query the
/// LLM emits (a `Fuaran.Core.DataFrame` pipeline over a columnar `DataSource`,
/// no closure on the wire); `Dialect` is a raw dialect string a pass-through
/// HTTP-driver source (Neon / Turso / PlanetScale / D1) executes verbatim. A
/// thinner, UI-facing sibling to `Fuaran.Core.Query`'s registrable declaration —
/// the concrete-driver path generalises onto that registry (see header).
[<RequireQualifiedAccess>]
type QueryRequest =
    | Transform of source: Fuaran.Core.DataSource * pipeline: Fuaran.Core.Transform list
    | Dialect of query: string

/// What a resolve returns. `SchemaOnly` is the privacy path — the typed result
/// schema with no rows (nothing but `(name, ColumnType)` pairs leaves the
/// source); `WithRows` carries the realised `Table` (which carries its own
/// `Schema`). Either way the UI types against `schema` below.
[<RequireQualifiedAccess>]
type QueryResolution =
    | SchemaOnly of Schema
    | WithRows of Table

module QueryResolution =

    /// The result schema to type the UI against — present in both modes (a
    /// `WithRows` table carries its `Schema`).
    let schema (r: QueryResolution) : Schema =
        match r with
        | QueryResolution.SchemaOnly s -> s
        | QueryResolution.WithRows t -> t.Schema

    /// The realised rows, or `None` in privacy (schema-only) mode.
    let rows (r: QueryResolution) : Table option =
        match r with
        | QueryResolution.SchemaOnly _ -> None
        | QueryResolution.WithRows t -> Some t

/// Why a client-side resolve failed. Named + total (GP5). Distinct from
/// `Fuaran.Core.Query.QueryError` (the registry/param surface) — this is the
/// resolver-boundary error a UI host surfaces.
[<RequireQualifiedAccess>]
type QuerySourceError =
    /// The configured source could not be reached (no driver, bad config, offline).
    | SourceUnavailable of detail: string
    /// The query ran but failed (bad SQL, permission, malformed transform).
    | ExecutionFailed of detail: string
    /// The source exceeded its time budget.
    | Timeout
    /// A by-reference `DataSource.Ref` had no host resolution.
    | NotResolved of ref: string

module QuerySourceError =

    /// A stable human string for a resolver error.
    let message (e: QuerySourceError) : string =
        match e with
        | QuerySourceError.SourceUnavailable d -> "source unavailable: " + d
        | QuerySourceError.ExecutionFailed d -> "query execution failed: " + d
        | QuerySourceError.Timeout -> "query timed out"
        | QuerySourceError.NotResolved r -> "unresolved source reference: " + r

/// The host-pluggable client query-source seam. Async-at-the-boundary, stateless,
/// identity-by-value — an HTTP-driver source, a DuckDB-WASM engine, a
/// paired-device peer, or an in-memory mock all conform without touching this
/// abstraction. `schemaOnly = true` requests the privacy path: return
/// `SchemaOnly`, never fetch rows.
type IClientQuerySource =
    abstract member Resolve:
        request: QueryRequest * schemaOnly: bool -> Async<Result<QueryResolution, QuerySourceError>>

/// In-memory / mock sources — the fully-offline, zero-network proof of the seam
/// (the shape a DuckDB-WASM engine takes) and the test seam.
module InMemoryQuerySource =

    /// A source backed by a synchronous resolver (wrapped at the async boundary).
    /// Honours `schemaOnly` by projecting the resolved table's schema.
    let ofResolver (resolve: QueryRequest -> Result<Table, QuerySourceError>) : IClientQuerySource =
        { new IClientQuerySource with
            member _.Resolve(request, schemaOnly) =
                async {
                    match resolve request with
                    | Error e -> return Error e
                    | Ok t ->
                        return
                            Ok(
                                if schemaOnly then
                                    QueryResolution.SchemaOnly t.Schema
                                else
                                    QueryResolution.WithRows t
                            )
                } }

    /// A source that resolves every request to the same fixed table — the
    /// minimal offline proof + the canonical test seam.
    let ofTable (t: Table) : IClientQuerySource = ofResolver (fun _ -> Ok t)

// ─── The typed gate (resolve → check → render-or-reject) ─────────────────────

/// Why the portal refused to render a dashboard: the source failed, or the
/// dashboard mis-typed against the resolved schema (default-deny — the typed
/// defects, never a wrong render).
[<RequireQualifiedAccess>]
type PortalError =
    | Source of QuerySourceError
    | TypeMismatch of QueryBinding.Defect list

/// The **schema-only** gate (pure, no rows, no async): type `dashboard` against a
/// resolved `schema`. `Ok tree` ⇒ the dashboard is sound, render it; `Error
/// defects` ⇒ at least one binding mis-types — surface the typed defects (FUARAN066
/// / FUARAN067) and do NOT render. This is the whole privacy-mode proof: a complete
/// dashboard is validated against structure alone (Phase 324).
let checkDashboard (schema: Schema) (dashboard: Node<'Msg>) : Result<Node<'Msg>, QueryBinding.Defect list> =
    match QueryBinding.check schema dashboard with
    | [] -> Ok dashboard
    | defects -> Error defects

/// The full path: resolve `request` through `src`, then gate `dashboard` against
/// the resolved schema. On success returns the validated tree paired with the
/// resolution (so the caller renders the tree with the rows); a type-mismatched
/// dashboard **never renders** (default-deny before render). `schemaOnly` threads
/// the privacy mode through to the source.
let resolveAndCheck
    (src: IClientQuerySource)
    (request: QueryRequest)
    (schemaOnly: bool)
    (dashboard: Node<'Msg>)
    : Async<Result<Node<'Msg> * QueryResolution, PortalError>> =
    async {
        let! resolved = src.Resolve(request, schemaOnly)

        match resolved with
        | Error e -> return Error(PortalError.Source e)
        | Ok resolution ->
            match checkDashboard (QueryResolution.schema resolution) dashboard with
            | Ok tree -> return Ok(tree, resolution)
            | Error defects -> return Error(PortalError.TypeMismatch defects)
    }
