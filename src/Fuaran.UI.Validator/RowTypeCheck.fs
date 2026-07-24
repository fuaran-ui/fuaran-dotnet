module Fuaran.UI.Validator.RowTypeCheck

// ============================================================================
//  OnRowClick / RowKey row-type match.
//
//  For each `Fuaran.grid` call whose `Source = binding.query "name" ...`, look
//  up the row type the manifest declares for `name` under `queryRowTypes`.
//  Three outcomes:
//
//    1. No manifest queryRowTypes entry for "name"        → Warning (FUARAN030)
//    2. Entry present + lambda parameter type annotation differs → Error (FUARAN031)
//    3. Entry present + no lambda annotation (or matches) → silent
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
                                "Row type mismatch in Fuaran.grid: lambda parameter annotated `%s` but manifest declares query \"%s\" returns rows of type `%s`."
                                annotation.Annotation
                                queryName
                                expectedRowType)

                    Some(withRecovery [ expectedRowType ] (Some expectedRowType) base'))

let check (manifest: Manifest) (calls: FuaranCall list) : Finding list =
    calls
    |> List.collect (fun c ->
        if c.Ctor <> "grid" then
            []
        else
            match c.GridDetail with
            | Some d -> checkGrid manifest c d
            | None -> [])
