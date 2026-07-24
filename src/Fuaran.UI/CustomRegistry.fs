namespace Fuaran.UI

open Fuaran.Core
open Fuaran.UI.Types

// ============================================================================
//  CustomRegistry — the first-class extension registry.
//
//  A host registers a `CustomContract` once; the registry then makes that
//  third-party component behave like a built-in NodeKind in the two ways a
//  built-in gets for free:
//
//   1. AI DISCOVERY — `describeForAi` projects each registered contract's prop
//      schema into a `CustomKindCard`, the shape an orchestrator folds into the
//      model's available-kinds prompt context so it can emit the widget's props
//      correctly (name + type + required), instead of guessing an opaque bag.
//
//   2. VALIDATION — `validateProps` checks a decoded `NodeKind.Custom` prop bag
//      against the declared `PropSchema`: a missing required prop or a JVal whose
//      shape doesn't match the declared `PropType` is a defect, surfaced with the
//      `FUARAN068` code (its own allocation — FUARAN064 is the button
//      disabled-binding advisory). This closes the "a Custom node opts out of all
//      validation" gap — a registered kind is checked like a built-in.
//
//  The registry stores only the ERASED contract facts (module/component id +
//  schema + hash), not the generic `'Props`, so contracts over different prop
//  types coexist in one registry. Pure data + FSharp.Core only (Fable-clean); a
//  host owns the registry's lifetime and threads it into its validate / prompt
//  paths.
// ============================================================================

/// One prop row of a `CustomKindCard` — the declared `PropSchema` entry with its
/// `PropType` rendered as a stable tag string an orchestrator can put in a
/// prompt. A NAMED record (not anonymous) deliberately: this is shipped public
/// API — named records are binary-stable across compilers and speakable from
/// C#/VB, anonymous records are neither.
type CustomPropCard =
    { Name: string
      Type: string
      Required: bool }

/// A registered custom component, projected for the AI's available-kinds context:
/// what to emit (`ModuleId` / `ComponentId`) and the prop contract to emit it
/// against.
type CustomKindCard =
    { ModuleId: string
      ComponentId: string
      Props: CustomPropCard list }

/// A single prop-validation defect against a declared schema. `Code` is always
/// `CustomRegistry.propDefectCode` (`FUARAN068`) — carried per-defect so a host
/// folding these into a mixed defect stream keeps the code with the finding.
type CustomPropDefect =
    { Code: string
      Key: string
      Message: string }

module CustomRegistry =

    /// The `CustomPropDefect` defect code — a registered custom component's prop
    /// bag violates its declared `PropSchema` (missing required prop / mistyped
    /// value). Its own allocation: `FUARAN064` is the button disabled-binding
    /// advisory.
    [<Literal>]
    let propDefectCode = "FUARAN068"

    /// The stable prompt-facing tag of a `PropType` (what an AI reads).
    let propTypeTag (t: PropType) : string =
        match t with
        | PropType.PString -> "string"
        | PropType.PInt -> "int"
        | PropType.PFloat -> "float"
        | PropType.PBool -> "bool"
        | PropType.PEnum choices -> "enum(" + String.concat "|" choices + ")"
        | PropType.PObject -> "object"
        | PropType.PArray -> "array"
        | PropType.PJson -> "json"

    /// Whether a wire `JVal` satisfies a declared `PropType`. `PInt` accepts a
    /// `JInt`; `PFloat` accepts either (an int is an exact float); `PJson` accepts
    /// anything. Mirrors the decoder's number policy.
    let matchesType (t: PropType) (v: JVal) : bool =
        match t, v with
        | PropType.PString, JStr _ -> true
        | PropType.PInt, JInt _ -> true
        | PropType.PFloat, (JFloat _ | JInt _) -> true
        | PropType.PBool, JBool _ -> true
        | PropType.PEnum choices, JStr s -> List.contains s choices
        | PropType.PObject, JObj _ -> true
        | PropType.PArray, JArr _ -> true
        | PropType.PJson, _ -> true
        | _ -> false

/// The registered-contract facts, erased of the generic `'Props`.
type private RegisteredCustom =
    { ModuleId: string
      ComponentId: string
      Schema: PropSchema
      Hash: ContentHash }

/// An immutable registry of custom-component contracts keyed on
/// `(moduleId, componentId)`. Build by folding `register` over your contracts.
type CustomRegistry private (entries: Map<string * string, RegisteredCustom>) =

    static member Empty = CustomRegistry(Map.empty)

    /// Register a typed contract (its `'Props` is erased — only the schema + hash
    /// are retained). Re-registering the same key replaces it.
    member _.Register(contract: CustomContract<'Props>) : CustomRegistry =
        let key = (contract.ModuleId, contract.ComponentId)

        let entry =
            { ModuleId = contract.ModuleId
              ComponentId = contract.ComponentId
              Schema = contract.Schema
              Hash = contract.Hash }

        CustomRegistry(Map.add key entry entries)

    /// The AI-facing cards for every registered component — fold these into the
    /// model's available-kinds prompt context so a `Custom` node can be emitted
    /// with correct props.
    member _.DescribeForAi() : CustomKindCard list =
        entries
        |> Map.toList
        |> List.map (fun (_, e) ->
            { ModuleId = e.ModuleId
              ComponentId = e.ComponentId
              Props =
                e.Schema
                |> List.map (fun p ->
                    { Name = p.Name
                      Type = CustomRegistry.propTypeTag p.Type
                      Required = p.Required }) })

    /// Validate a decoded `NodeKind.Custom` prop bag against the registered
    /// schema for `(moduleId, componentId)`. An UNregistered component is `Ok`
    /// (the registry only speaks for what it knows — an unknown custom kind is a
    /// host trust-boundary concern, not a schema violation). A registered one is
    /// checked: every required prop present, every present prop the right
    /// `PropType`. Returns the defect list (empty = valid).
    member _.ValidateProps(moduleId: string, componentId: string, props: Map<string, JVal>) : CustomPropDefect list =
        match Map.tryFind (moduleId, componentId) entries with
        | None -> []
        | Some e ->
            let missing =
                e.Schema
                |> List.filter (fun p -> p.Required && not (Map.containsKey p.Name props))
                |> List.map (fun p ->
                    { Code = CustomRegistry.propDefectCode
                      Key = p.Name
                      Message = sprintf "required prop '%s' (%s) is missing" p.Name (CustomRegistry.propTypeTag p.Type) })

            let mistyped =
                e.Schema
                |> List.choose (fun p ->
                    match Map.tryFind p.Name props with
                    | Some v when not (CustomRegistry.matchesType p.Type v) ->
                        Some
                            { Code = CustomRegistry.propDefectCode
                              Key = p.Name
                              Message = sprintf "prop '%s' is not a %s" p.Name (CustomRegistry.propTypeTag p.Type) }
                    | _ -> None)

            missing @ mistyped
