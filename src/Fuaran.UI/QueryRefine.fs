module Fuaran.UI.QueryRefine

// ============================================================================
//  Client-side refinement = local data, not round-trips (Phase 324 task 4).
//
//  Once a query has resolved to a `Table` (the `QuerySource` `WithRows` path),
//  follow-on tweaks — "sort descending", "filter to region X", "regroup by
//  month" — apply as a `Fuaran.Core.DataFrame` pipeline over the columns ALREADY
//  IN HAND, through the pinned reference evaluator. No re-query, no LLM call:
//  the LLM writes the coarse query once, interaction is native-speed local
//  algebra. This is the "fluent" feel the portal promises.
//
//  TIED BACK TO THE 323 TYPE-THREAD. A refinement can change the result schema
//  (a `GroupBy` collapses columns, a `Project` drops one), which can invalidate
//  a dashboard binding (a Metric now references an absent / re-typed column). So
//  after evaluating the refinement, the dashboard is RE-TYPED against the refined
//  schema via `QueryBinding.check` — a refinement that would render wrong is a
//  typed defect (FUARAN066/067), never a broken render.
//
//  FABLE + .NET PARITY (FGP 4). FSharp.Core + `Fuaran.Core.Column`/`DataFrame`
//  only — `DataFrame.evalPipeline` is the pinned cross-host evaluator (certified
//  byte-identical by `Conformance.transformLaws`), so the refinement resolves
//  identically in the fuaran-live fable-host and under these .NET tests.
// ============================================================================

open Fuaran.UI.Types
open Fuaran.Core

/// Why a local refinement could not be applied.
[<RequireQualifiedAccess>]
type RefineError =
    /// The refinement pipeline failed to evaluate over the fetched rows (unknown
    /// column, type error, arithmetic overflow, unresolved join source, …).
    | Eval of EvalError
    /// The refinement changed the result schema such that the dashboard no longer
    /// types against it — a binding references a now-absent / re-typed column.
    /// Caught by the 323 thread BEFORE re-render.
    | TypeMismatch of QueryBinding.Defect list

module RefineError =

    /// A stable human string for a refinement error.
    let message (e: RefineError) : string =
        match e with
        | RefineError.Eval(UnknownColumn(name, available)) ->
            "refinement references unknown column '"
            + name
            + "'; available: "
            + String.concat ", " available
        | RefineError.Eval _ -> "refinement failed to evaluate over the fetched rows"
        | RefineError.TypeMismatch defects ->
            "refinement changed the schema and the dashboard no longer types ("
            + string (List.length defects)
            + " defect(s))"

/// Apply a local refinement `pipeline` to the ALREADY-FETCHED `rows` via the
/// `Fuaran.Core.DataFrame` reference evaluator, then RE-TYPE `dashboard` against
/// the refined result schema. Returns the refined rows paired with the (still
/// type-sound) dashboard, or a typed `RefineError`.
///
/// THE FAST-PATH (Phase 324). This takes NO `IClientQuerySource` — it is pure
/// local compute over columns already in hand, so a follow-on tweak costs ZERO
/// re-query and ZERO LLM tokens by construction. A refinement the `DataFrame`
/// algebra cannot express is the caller's signal to fall back to a fresh
/// NL→query emission (the slow path); everything the algebra covers stays local.
///
/// Schema-only (privacy-mode) resolutions carry no `rows` to refine — refine
/// over a `WithRows` table (re-fetch with rows first if you are in schema-only
/// mode and need local interaction).
let refineLocally
    (rows: Table)
    (pipeline: Transform list)
    (dashboard: Node<'Msg>)
    : Result<Table * Node<'Msg>, RefineError> =
    match DataFrame.evalPipeline pipeline rows with
    | Error e -> Error(RefineError.Eval e)
    | Ok refined ->
        match QueryBinding.check refined.Schema dashboard with
        | [] -> Ok(refined, dashboard)
        | defects -> Error(RefineError.TypeMismatch defects)
