module FableLaws.OperabilityLaw

// ============================================================================
//  Law 9 (Phase 2517) — the operable surface and the resolution contract,
//  identical on both pipelines.
//
//  The `operability/` family's authored trees (the same `Cases.fs` the .NET
//  suite emits the family from, linked rather than copied) are decoded, derived
//  and every recast S1–S7 instruction resolved over them. Printed: each tree's
//  canonical affordance list and each instruction's canonical outcome, so the
//  runner's line comparison is the cross-host claim — the matcher a browser
//  runs answers exactly what the reference answers. Each outcome is ALSO held
//  to the answer the case states by hand, and a disagreement is a violation on
//  either pipeline.
// ============================================================================

open Fuaran.UI.Ops
open Fuaran.UI.Operability
open Fuaran.UI.Operability.Tests

type OperabilityLaw = { Lines: string list; Violations: int }

let private expected (expect: Cases.Expect) : Outcome =
    match expect with
    | Cases.Resolves clauses ->
        clauses
        |> List.map (fun (id, value) ->
            id,
            (match value with
             | Some v -> SlotValue.Value v
             | None -> SlotValue.NoValue))
        |> Outcome.Resolved
    | Cases.Refused -> Outcome.NotResolved
    | Cases.AmbiguousAmong ids -> Outcome.Ambiguous ids

let operabilityLaw () : OperabilityLaw =
    let derive (tree: Cases.Tree) =
        match JsonDecode.decodeNodeObj tree.Json with
        | Ok node -> Ok(Operability.derive tree.MountId node)
        | Error e -> Error(sprintf "%s at %s" e.Code e.Path)

    let treeLines =
        Cases.trees
        |> List.map (fun t ->
            match derive t with
            | Ok affordances ->
                sprintf "operability tree %s %s" t.Id (Operability.canonical (Operability.affordancesJson affordances)),
                0
            | Error why -> sprintf "operability tree %s REFUSED %s" t.Id why, 1)

    let resolveLines =
        Cases.resolves
        |> List.map (fun c ->
            match derive c.Tree with
            | Ok affordances ->
                let outcome = Operability.resolve c.Instruction affordances
                let ok = outcome = expected c.Expect

                sprintf
                    "operability resolve %s %s%s"
                    c.Id
                    (Operability.canonical (Operability.outcomeJson outcome))
                    (if ok then "" else " VIOLATION"),
                (if ok then 0 else 1)
            | Error why -> sprintf "operability resolve %s REFUSED %s" c.Id why, 1)

    let all = treeLines @ resolveLines

    { Lines = all |> List.map fst
      Violations = all |> List.sumBy snd }
