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
