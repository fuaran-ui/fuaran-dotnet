module Fuaran.UI.Validator.BindingResolution

// ============================================================================
//  Binding.Query name resolution.
//
//  Every `binding.query "name" accessor` reference must point at a name the
//  module's manifest declares under `queries`. Unresolved names are Errors —
//  the renderer would resolve them to `NotResolved` at runtime, which then
//  surfaces through the OnLoading slot indefinitely; catching the typo at
//  build time is strictly better.
//
//  Without a manifest, this check is silenced (no manifest → no contract →
//  no findings). The validator's top-level reports the manifest-missing
//  state as its own Warning so the operator isn't surprised by silent passes.
// ============================================================================

open Fuaran.UI.Validator.AstWalker
open Fuaran.UI.Validator.Findings
open Fuaran.UI.Validator.Manifest

/// Levenshtein distance, used to produce best-guess suggestions when a
/// query name is unresolved. Small input sizes, so a naive 2D DP is fine.
let private levenshtein (a: string) (b: string) : int =
    let m = a.Length
    let n = b.Length

    if m = 0 then
        n
    elif n = 0 then
        m
    else
        let d = Array2D.create (m + 1) (n + 1) 0

        for i in 0..m do
            d[i, 0] <- i

        for j in 0..n do
            d[0, j] <- j

        for i in 1..m do
            for j in 1..n do
                let cost = if a[i - 1] = b[j - 1] then 0 else 1

                d[i, j] <- List.min [ d[i - 1, j] + 1; d[i, j - 1] + 1; d[i - 1, j - 1] + cost ]

        d[m, n]

let private suggestSimilar (candidates: string seq) (target: string) : string option =
    let best =
        candidates
        |> Seq.map (fun c -> c, levenshtein target c)
        |> Seq.sortBy snd
        |> Seq.tryHead

    match best with
    | Some(name, distance) when distance <= 3 && distance <= max 2 (target.Length / 2) -> Some name
    | _ -> None

let check (manifest: Manifest) (calls: FuaranCall list) : Finding list =
    if Set.isEmpty manifest.Queries then
        // No queries declared in the manifest — nothing to resolve against.
        // The validator's top-level should emit a single FUARAN901-style
        // Warning about manifest emptiness; per-call checks stay quiet.
        []
    else
        let registered = manifest.Queries
        let registeredList = registered |> Set.toList

        calls
        |> List.collect _.QueryReferences
        |> List.collect (fun q ->
            if registered.Contains q.Name then
                []
            else
                let suggestion = suggestSimilar registeredList q.Name

                let base' =
                    create
                        Error
                        "FUARAN010"
                        q.Location
                        (sprintf
                            "Unresolved binding.query \"%s\" — name is not in the module's manifest queries list."
                            q.Name)

                [ withRecovery registeredList suggestion base' ])
