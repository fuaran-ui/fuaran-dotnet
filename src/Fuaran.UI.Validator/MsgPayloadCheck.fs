module Fuaran.UI.Validator.MsgPayloadCheck

// ============================================================================
//  Action.Dispatch payload check.
//
//  Every `Action.Dispatch (Case ...)` reference is cross-checked against the
//  manifest's `msgCases` list. Catches the `LoadDate` / `LoadData` class of
//  typo that would otherwise compile (because pattern-matching a string-named
//  case against a fresh case the validator hasn't seen would not be caught
//  by FCS at the validator-pass level — we deliberately stop short of full
//  type-checker resolution).
//
//  Without a manifest msgCases list: silenced (same posture as
//  BindingResolution). Missing case name in dispatch payload (anonymous
//  function call, value reference, etc.) is silently passed — the AST walker
//  returns None for those payload shapes and they never reach this check.
// ============================================================================

open Fuaran.UI.Validator.AstWalker
open Fuaran.UI.Validator.Findings
open Fuaran.UI.Validator.Manifest

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
    if Set.isEmpty manifest.MsgCases then
        []
    else
        let registered = manifest.MsgCases
        let registeredList = registered |> Set.toList

        calls
        |> List.collect _.DispatchReferences
        |> List.collect (fun d ->
            if registered.Contains d.CaseName then
                []
            else
                let suggestion = suggestSimilar registeredList d.CaseName

                let base' =
                    create
                        Error
                        "FUARAN020"
                        d.Location
                        (sprintf
                            "Action.Dispatch payload references unknown Msg case \"%s\" — case is not in the module's manifest msgCases list."
                            d.CaseName)

                [ withRecovery registeredList suggestion base' ])
