module Fuaran.UI.Validator.RowTypeCheck

// ============================================================================
//  Grid row-type match.
//
//  For each `Fuaran.grid` call whose `Source = binding.query "name" ...`, look
//  up the row type the manifest declares for `name` under `queryRowTypes`.
//  Three outcomes:
//
//    1. No manifest queryRowTypes entry for "name"        → Warning (FUARAN030)
//    2. Entry present + the toRow parameter annotation differs → Error (FUARAN031)
//    3. Entry present + no toRow annotation (or matches) → silent
//
//  The row type is read off `toRow` (`Fuaran.grid "id" (fun (r: SaleRow) -> ...)
//  spec`), the one closure typed by the source row: `OnRowClick`, `RowKey` and
//  the column accessors all take the projected `Row`, so an annotation there
//  never names the source row type.
//
//  Case 3 is the "best we can do without typed AST" boundary — typed
//  FCS resolution would let us verify against the actual inferred row
//  type even when the author elides the annotation. v1 stops at the
//  untyped-AST line; future work picks up
//  the typed pass (tracked as a Tidy-Up follow-up).
//
//  The walker exposes per-call `GridDetail`; this check reads it.
// ============================================================================

open Fuaran.UI.Validator.AstWalker
open Fuaran.UI.Validator.Findings
open Fuaran.UI.Validator.Manifest

let private checkGrid (manifest: Manifest) (call: FuaranCall) (detail: GridDetail) : Finding list =
    match detail.SourceQueryName with
    | None -> []
    | Some queryName ->
        match Map.tryFind queryName manifest.QueryRowTypes with
        | None ->
            let registered = manifest.QueryRowTypes |> Map.toList |> List.map fst

            let base' =
                create
                    Warning
                    "FUARAN030"
                    call.Location
                    (sprintf
                        "Cannot verify row type for Fuaran.grid Source = binding.query \"%s\" — manifest queryRowTypes has no entry for this query. Add one to enable strict checking."
                        queryName)

            [ if List.isEmpty registered then
                  base'
              else
                  withRecovery registered None base' ]
        | Some expectedRowType ->
            detail.RowAnnotations
            |> List.choose (fun annotation ->
                if annotation.Annotation = expectedRowType then
                    None
                else
                    let base' =
                        create
                            Error
                            "FUARAN031"
                            annotation.Location
                            (sprintf
                                "Row type mismatch in Fuaran.grid: toRow parameter annotated `%s` but manifest declares query \"%s\" returns rows of type `%s`."
                                annotation.Annotation
                                queryName
                                expectedRowType)

                    Some(withRecovery [ expectedRowType ] (Some expectedRowType) base'))

let check (manifest: Manifest) (calls: FuaranCall list) : Finding list =
    // No schema in the manifest — no contract to check against, the same
    // posture as BindingResolution / MsgPayloadCheck (the run's FUARAN900
    // preamble already says these checks are silenced).
    if manifest.Queries.IsEmpty && manifest.QueryRowTypes.IsEmpty then
        []
    else
        calls
        |> List.collect (fun c ->
            match c.Ctor, c.GridDetail with
            | "grid", Some d -> checkGrid manifest c d
            | _ -> [])
