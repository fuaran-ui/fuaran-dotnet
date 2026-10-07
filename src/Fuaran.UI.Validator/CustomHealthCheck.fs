module Fuaran.UI.Validator.CustomHealthCheck

// ============================================================================
//  Custom bounded-escape health check.
//
//  Four defect codes for the `NodeKind.Custom` escape hatch's safety
//  surfaces. The cultural-posture rationale: Custom is the
//  language's principled escape hatch, but it must remain a *last-resort*
//  one. These rules make Custom-creep visible so the operator can act on
//  it (closing typed-contract gaps upstream in the language) rather than
//  absorbing creep silently.
//
//   - FUARAN052 NOT IMPLEMENTED HERE — it surfaces at op-stream replay
//     time inside the apply engine (the build-time validator has no
//     op-stream view). The constant lives in `Findings.codeFUARAN052`
//     for documentation and cross-reference; the apply engine surfaces
//     the runtime defect through `ApplyError`-shaped records.
//
//   - FUARAN053 CustomExposedNodeIdsUnverified (Warning): a Custom node
//     declares `exposedNodeIds = [...]` but no `RegisterCustomRenderer`
//     for the same `(moduleId, componentId)` appears in the project's
//     source. Without a registered renderer we can't verify the emit-sites
//     for the declared ids — the AI consumer / op-stream replay will see
//     "ids declared" but the layout observer won't find them at runtime.
//     Conservative shape per the anti-pattern: best-effort detection
//     of the registration cross-reference; Warning default; consumers opt
//     into Error via the manifest.
//
//   - FUARAN054 CustomNodeRatioExceedsHealthy (Advisory Warning): the
//     project's `NodeKind.Custom` construction-site count divided by the
//     total Fuaran.X smart-ctor count exceeds the manifest-declared
//     `customNodeRatio` (default `0.05`). The advisory's structured
//     payload lists the contributing call sites so the operator can
//     audit. THIS IS THE PROJECT-HEALTH METRIC — the cultural-posture
//     mitigation against Custom-creep.
//
//   - FUARAN055 CustomLacksContentHash (Advisory Warning): a Custom node
//     constructed without a `contentHash` (the safety feature wasn't
//     adopted for this escape). Advisory only — opting out is a valid
//     choice but the validator surfaces it so the operator sees the
//     surface.
//
//  Detection is a visitor over the shared parse (`Syntax`); the smart-ctor
//  population FUARAN054 divides by is the call-site model `AstWalker` already
//  extracted, over the constructor set it derives from the `Fuaran` module.
// ============================================================================


open FSharp.Compiler.Syntax
open Fuaran.UI.Validator.AstWalker
open Fuaran.UI.Validator.Findings
open Fuaran.UI.Validator.Syntax

/// Documentation constant for FUARAN052 — surfaced by the apply engine,
/// not this module. Kept here so a code-search for `FUARAN052` lands on
/// the right comment block.
let codeFUARAN052 = "FUARAN052"

let codeFUARAN053 = "FUARAN053"
let codeFUARAN054 = "FUARAN054"
let codeFUARAN055 = "FUARAN055"

let defaultCustomNodeRatio = 0.05

/// One observed `NodeKind.Custom` / `Fuaran.custom` construction site.
/// One observed `NodeKind.Custom` / `Fuaran.custom` construction site.
type private CustomConstructionCall =
    { ModuleId: string option
      ComponentId: string option
      ContentHashIsNone: bool
      ExposedNodeIdsIsNonEmpty: bool
      Location: Location }

/// One observed `RegisterCustomRenderer(moduleId, componentId, fn)` call
/// site. Pairs with `CustomConstructionCall` for FUARAN053 cross-reference.
and private RegisterRendererCall =
    { ModuleId: string
      ComponentId: string
      Location: Location }

/// An exposed-ids argument that is not written as empty: `[]` / `None` /
/// `Some []` read as empty, anything else as declaring interior ids.
let private declaresExposedIds (expr: SynExpr) : bool =
    let isEmptyList e =
        listItems e |> Option.map List.isEmpty |> Option.defaultValue false

    not (
        isEmptyList expr
        || isExplicitNone expr
        || (applied "Some" expr |> Option.map isEmptyList |> Option.defaultValue false)
    )

let private site (moduleArg: SynExpr option) (componentArg: SynExpr option) hashArg exposedArg location =
    { ModuleId = moduleArg |> Option.bind literalString
      ComponentId = componentArg |> Option.bind literalString
      ContentHashIsNone = hashArg |> Option.map isExplicitNone |> Option.defaultValue false
      ExposedNodeIdsIsNonEmpty = exposedArg |> Option.map declaresExposedIds |> Option.defaultValue false
      Location = location }

/// A Custom construction: `Fuaran.custom id moduleId componentId props hash
/// exposed`, or `NodeKind.Custom { ModuleId = ...; ComponentId = ...; ... }`
/// (the generated case carries a `CustomSpec` record).
let private customSite (file: string) (head: SynExpr) (args: SynExpr list) : CustomConstructionCall option =
    let location = mkLocation file head.Range

    if isQualified "Fuaran" "custom" head && args.Length >= 2 then
        let at i = List.tryItem i args
        Some(site (at 1) (at 2) (at 4) (at 5) location)
    elif isQualified "NodeKind" "Custom" head then
        match args with
        | [ spec ] ->
            let field name = fieldValue name spec

            match unwrap spec with
            | SynExpr.Record _ ->
                Some(
                    site
                        (field "ModuleId")
                        (field "ComponentId")
                        (field "ContentHash")
                        (field "ExposedNodeIds")
                        location
                )
            | _ -> None
        | _ -> None
    else
        None

/// `<runtime>.RegisterCustomRenderer("moduleId", "componentId", ...)`.
let private registration (file: string) (head: SynExpr) (args: SynExpr list) : RegisterRendererCall option =
    match leafIdent head, args with
    | Some(_, "RegisterCustomRenderer", _), [ tuple ] ->
        match unwrap tuple with
        | SynExpr.Tuple(exprs = moduleArg :: componentArg :: _) ->
            match literalString moduleArg, literalString componentArg with
            | Some moduleId, Some componentId ->
                Some
                    { ModuleId = moduleId
                      ComponentId = componentId
                      Location = mkLocation file head.Range }
            | _ -> None
        | _ -> None
    | _ -> None

let check (manifest: Manifest.Manifest) (calls: FuaranCall list) (sources: ParsedSource list) : Finding list =
    let allCustoms = sources |> collectApps customSite
    let allRegistrations = sources |> collectApps registration

    // The FUARAN054 population: every Fuaran.X smart-ctor call site, plus the
    // `NodeKind.Custom` constructions that bypass a smart ctor.
    let smartCtorCount = calls.Length

    let fuaranCustomInSmartCtors =
        calls |> List.filter (fun c -> c.Ctor = "custom") |> List.length

    let fuaran055Findings =
        allCustoms
        |> List.filter _.ContentHashIsNone
        |> List.map (fun c ->
            let label =
                match c.ModuleId, c.ComponentId with
                | Some m, Some cid -> sprintf "%s.%s" m cid
                | _ -> "<unresolved>"

            create
                Warning
                codeFUARAN055
                c.Location
                (sprintf
                    "Custom node %s lacks a contentHash. The bounded-escape safety feature is opt-in — adding a contentHash lets op-stream replay verify the body hasn't drifted from the registered renderer."
                    label))

    // FUARAN053 — Custom declares exposedNodeIds but no matching
    // RegisterCustomRenderer is found in the project. Conservative:
    // fires only when (moduleId, componentId) is resolvable as literals
    // on the Custom side AND no registration matches.
    let registrationSet =
        allRegistrations |> List.map (fun r -> r.ModuleId, r.ComponentId) |> Set.ofList

    let fuaran053Findings =
        allCustoms
        |> List.filter (fun c ->
            c.ExposedNodeIdsIsNonEmpty
            && (match c.ModuleId, c.ComponentId with
                | Some m, Some cid -> not (Set.contains (m, cid) registrationSet)
                | _ -> false))
        |> List.map (fun c ->
            let label =
                match c.ModuleId, c.ComponentId with
                | Some m, Some cid -> sprintf "%s.%s" m cid
                | _ -> "<unresolved>"

            create
                Warning
                codeFUARAN053
                c.Location
                (sprintf
                    "Custom node %s declares exposedNodeIds but no RegisterCustomRenderer call for the same (moduleId, componentId) appears in the project. The layout observer / AiTools introspection / Apply structural ops can't address the declared interior NodeIds without a registered renderer."
                    label))

    // FUARAN054 — project Custom-ratio advisory. Custom contributions
    // = Customs (counted once each). Total = SmartCtors + direct
    // NodeKind.Custom (which aren't already counted under SmartCtors
    // when `Fuaran.custom` wasn't used). To keep the metric stable, we
    // compute Total = SmartCtors.Length + (Customs constructed via
    // NodeKind.Custom that weren't also captured as Fuaran.custom).
    //
    // Heuristic: SmartCtors already includes `Fuaran.custom` invocations
    // (they're in the knownFuaranSmartCtors set). Direct `NodeKind.Custom`
    // construction sites are NOT in SmartCtors. So:
    //   totalTypedNodes = SmartCtors.Length + (Customs that aren't Fuaran.custom)
    // For ratio purposes, every Custom counts as a Custom; the question
    // is what divides into. We treat the ratio as customCount /
    // max(1, smartCtors + directNodeKindCustomCount).
    let threshold =
        manifest.CustomNodeRatio |> Option.defaultValue defaultCustomNodeRatio

    let customCount = allCustoms.Length

    // Direct NodeKind.Custom constructions are the Customs not made through a
    // `Fuaran.custom` call site.

    let directNodeKindCustomCount = max 0 (customCount - fuaranCustomInSmartCtors)
    let totalTypedNodes = smartCtorCount + directNodeKindCustomCount

    // Tiny-project floor: a 1-or-2 Custom project against a small
    // total isn't a creep signal — it's structural noise. Skip the
    // advisory unless the project has at least 3 Custom sites AND at
    // least 20 typed nodes. Larger projects with one outlier Custom
    // still don't fire; the metric is about *patterns*, not isolated
    // cases. The Fuaran.UI language-tier itself contains the
    // `Fuaran.custom` smart-ctor definition (which the AST walker
    // legitimately counts as a Custom-construction site since it
    // delegates to `NodeKind.Custom(...)` internally); this floor
    // keeps the validator from firing on the language tier itself.
    let minCustomCountToFire = 3
    let minPopulationToFire = 20

    let fuaran054Findings =
        if totalTypedNodes < minPopulationToFire || customCount < minCustomCountToFire then
            []
        else
            let ratio = float customCount / float totalTypedNodes

            if ratio > threshold then
                let contributors =
                    allCustoms
                    |> List.map (fun c ->
                        let label =
                            match c.ModuleId, c.ComponentId with
                            | Some m, Some cid -> sprintf "%s.%s" m cid
                            | _ -> "<unresolved>"

                        sprintf "%s @ %s:%d" label c.Location.File c.Location.Line)
                    |> String.concat ", "

                let projectLocation =
                    match allCustoms with
                    | c :: _ -> { c.Location with Line = 1; Column = 1 }
                    | [] ->
                        { File = "<project>"
                          Line = 1
                          Column = 1 }

                [ create
                      Warning
                      codeFUARAN054
                      projectLocation
                      (sprintf
                          "Project Custom-node ratio %.3f (%d Custom / %d total) exceeds the healthy threshold of %.3f. This project-health metric tracks Custom as the language's last-resort escape; consider whether each Custom signals a typed-contract gap the language should close upstream rather than absorbing the creep. Contributors: %s. Raise customNodeRatio in fuaran-validator.manifest.json to acknowledge legitimately higher usage."
                          ratio
                          customCount
                          totalTypedNodes
                          threshold
                          contributors) ]
            else
                []


    fuaran055Findings @ fuaran053Findings @ fuaran054Findings
