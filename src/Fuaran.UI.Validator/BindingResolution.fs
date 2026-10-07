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
