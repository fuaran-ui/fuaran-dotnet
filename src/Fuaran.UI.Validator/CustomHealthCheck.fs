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
//  Detection lives in a narrow AST walker following the LocalBindingCheck
//  precedent — independent lexical scope from the main Fuaran.X smart-
//  ctor walker.
// ============================================================================

open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Fuaran.UI.Validator.Findings

/// Documentation constant for FUARAN052 — surfaced by the apply engine,
/// not this module. Kept here so a code-search for `FUARAN052` lands on
/// the right comment block.
let codeFUARAN052 = "FUARAN052"

let codeFUARAN053 = "FUARAN053"
let codeFUARAN054 = "FUARAN054"
let codeFUARAN055 = "FUARAN055"

let defaultCustomNodeRatio = 0.05

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

/// Mirror of LocalBindingCheck's pattern — a smart-ctor / non-smart-ctor
/// `Fuaran.X` invocation. For FUARAN054 we only need the count, not the
/// kind. We count the leaf identifier when it's in the known set.
and private FuaranSmartCtorCall = { Ctor: string; Location: Location }

let private mkLocation (file: string) (range: range) : Location =
    { File = file
      Line = range.StartLine
      Column = range.StartColumn + 1 }

let private constStringValue (c: SynConst) =
    match c with
    | SynConst.String(text = s) -> Some s
    | _ -> None

let private literalString (expr: SynExpr) : string option =
    let rec inner (e: SynExpr) =
        match e with
        | SynExpr.Paren(expr = e') -> inner e'
        | SynExpr.Typed(expr = e') -> inner e'
        | SynExpr.Const(constant = c) -> constStringValue c
        | _ -> None

    inner expr

let private leafIdent (expr: SynExpr) : (string list * string) option =
    match expr with
    | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) when not ids.IsEmpty ->
        let names = AstWalker.identNames ids
        let leaf = List.last names
        let prefix = names |> List.take (names.Length - 1)
        Some(prefix, leaf)
    | SynExpr.Ident i -> Some([], i.idText)
    | _ -> None

let private (|FuaranCustomCtor|_|) (expr: SynExpr) =
    match leafIdent expr with
    | Some(prefix, "custom") when not prefix.IsEmpty && List.last prefix = "Fuaran" -> Some()
    | _ -> None

/// `NodeKind.Custom` direct-DU construction.
let private (|NodeKindCustomCtor|_|) (expr: SynExpr) =
    match leafIdent expr with
    | Some(prefix, "Custom") when not prefix.IsEmpty && List.last prefix = "NodeKind" -> Some()
    | _ -> None

/// `RegisterCustomRenderer` — last-segment identifier on any receiver
/// (`runtime.RegisterCustomRenderer(...)` / `BrowserRuntime.RegisterCustomRenderer(...)`).
let private (|RegisterCustomRendererCall|_|) (expr: SynExpr) =
    match leafIdent expr with
    | Some(_, "RegisterCustomRenderer") -> Some()
    | _ -> None

/// `Fuaran.X` recognised smart-ctor leaf — used by FUARAN054 to compute
/// the project's total typed-node count. Mirrors AstWalker's set.
let private knownFuaranSmartCtors =
    Set.ofList
        [ "dashboard"
          "stack"
          "gridLayout"
          "splitPanel"
          "tabs"
          "tabsTagged"
          "card"
          "stepper"
          "summaryList"
          "disclosure"
          "metric"
          "heading"
          "labelValueRow"
          "markdown"
          "markdownSpec"
          "callout"
          "progress"
          "skeleton"
          "button"
          "select"
          "form"
          "filters"
          "fileUpload"
          "chart"
          "table"
          "map"
          "grid"
          // `custom` participates too — it's a Fuaran.X smart-ctor, just
          // one we also separately track for the Custom-health rules.
          "custom"
          // `errorBoundary` is a Fuaran.X smart-
          // ctor like any other — counts in the FUARAN054 denominator so
          // adopting boundaries doesn't artificially inflate the
          // Custom-ratio numerator. Boundaries are *not* themselves a
          // last-resort escape; they're an AI-emittable shape for
          // graceful degradation.
          "errorBoundary" ]

let private (|FuaranXSmartCtor|_|) (expr: SynExpr) =
    match leafIdent expr with
    | Some(prefix, leaf) when
        not prefix.IsEmpty
        && List.last prefix = "Fuaran"
        && Set.contains leaf knownFuaranSmartCtors
        ->
        Some leaf
    | _ -> None

let private isExplicitNoneExpr (expr: SynExpr) : bool =
    let rec inner (e: SynExpr) =
        match e with
        | SynExpr.Paren(expr = e') -> inner e'
        | SynExpr.Typed(expr = e') -> inner e'
        | SynExpr.Ident i -> i.idText = "None"
        | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) when not ids.IsEmpty -> (List.last ids).idText = "None"
        | _ -> false

    inner expr

/// `[]` (empty list literal) — used by FUARAN053 to detect the "non-
/// empty exposedNodeIds" case. Anything other than `[]` (a let-bound
/// variable, a function call, etc.) is treated conservatively as
/// "possibly non-empty" so FUARAN053 fires when in doubt.
let private isEmptyListExpr (expr: SynExpr) : bool =
    let rec inner (e: SynExpr) =
        match e with
        | SynExpr.Paren(expr = e') -> inner e'
        | SynExpr.Typed(expr = e') -> inner e'
        | SynExpr.ArrayOrList(exprs = []) -> true
        | _ -> false

    inner expr

/// Curry-decompose an application chain into (head, args).
let private flattenApp (expr: SynExpr) : SynExpr * SynExpr list =
    let rec loop acc =
        function
        | SynExpr.App(funcExpr = f; argExpr = a) -> loop (a :: acc) f
        | head -> head, acc

    loop [] expr

type private WalkState =
    { File: string
      mutable Customs: CustomConstructionCall list
      mutable Registrations: RegisterRendererCall list
      mutable SmartCtors: FuaranSmartCtorCall list }

/// Extract `(moduleId, componentId, contentHashArg, exposedNodeIdsArg)`
/// from a `Fuaran.custom id moduleId componentId props contentHash exposedNodeIds`
/// call. Positional shape: `[id; moduleId; componentId; props; contentHash; exposedNodeIds]`.
let private classifyFuaranCustom (args: SynExpr list) : CustomConstructionCall option =
    match args with
    | _ :: moduleArg :: componentArg :: _ :: hashArg :: exposedArg :: _ ->
        Some
            { ModuleId = literalString moduleArg
              ComponentId = literalString componentArg
              ContentHashIsNone = isExplicitNoneExpr hashArg
              ExposedNodeIdsIsNonEmpty = not (isEmptyListExpr exposedArg)
              Location = mkLocation "" (moduleArg.Range) }
    | _ -> None

/// Extract from `NodeKind.Custom(moduleId, componentId, props, hash, exposedIds)`.
/// Tupled arg shape — the DU constructor takes a single tuple argument.
let private classifyNodeKindCustom (args: SynExpr list) : CustomConstructionCall option =
    match args with
    | [ SynExpr.Paren(expr = SynExpr.Tuple(exprs = items)) ]
    | [ SynExpr.Tuple(exprs = items) ] ->
        match items with
        | moduleArg :: componentArg :: _ :: hashArg :: exposedArg :: _ ->
            Some
                { ModuleId = literalString moduleArg
                  ComponentId = literalString componentArg
                  ContentHashIsNone = isExplicitNoneExpr hashArg
                  ExposedNodeIdsIsNonEmpty = not (isEmptyListExpr exposedArg)
                  Location = mkLocation "" (moduleArg.Range) }
        | _ -> None
    | _ -> None

let rec private walkExpr (state: WalkState) (expr: SynExpr) =
    let head, args = flattenApp expr

    match head with
    | FuaranCustomCtor ->
        match classifyFuaranCustom args with
        | Some call ->
            state.Customs <-
                { call with
                    Location = mkLocation state.File (head.Range) }
                :: state.Customs
        | None -> ()

        // Track as a Fuaran.X smart-ctor too (for FUARAN054 total).
        state.SmartCtors <-
            { Ctor = "custom"
              Location = mkLocation state.File (head.Range) }
            :: state.SmartCtors

        args |> List.iter (walkExpr state)
    | NodeKindCustomCtor ->
        match classifyNodeKindCustom args with
        | Some call ->
            state.Customs <-
                { call with
                    Location = mkLocation state.File (head.Range) }
                :: state.Customs
        | None -> ()

        // NodeKind.Custom is NOT a Fuaran.X smart-ctor — direct DU
        // construction. Counted only on the Custom side. For FUARAN054 we
        // also count it as a "Custom contribution" since the ratio is
        // CustomCount / (FuaranXCount + CustomDirectCount).
        args |> List.iter (walkExpr state)
    | RegisterCustomRendererCall ->
        match args with
        | [ SynExpr.Paren(expr = SynExpr.Tuple(exprs = items)) ]
        | [ SynExpr.Tuple(exprs = items) ] ->
            match items with
            | moduleArg :: componentArg :: _ ->
                match literalString moduleArg, literalString componentArg with
                | Some moduleId, Some componentId ->
                    state.Registrations <-
                        { ModuleId = moduleId
                          ComponentId = componentId
                          Location = mkLocation state.File (head.Range) }
                        :: state.Registrations
                | _ -> ()
            | _ -> ()
        | _ -> ()

        args |> List.iter (walkExpr state)
    | FuaranXSmartCtor ctor ->
        state.SmartCtors <-
            { Ctor = ctor
              Location = mkLocation state.File (head.Range) }
            :: state.SmartCtors

        args |> List.iter (walkExpr state)
    | _ -> descend state expr

and private descend (state: WalkState) (expr: SynExpr) =
    match expr with
    | SynExpr.App(funcExpr = f; argExpr = a) ->
        walkExpr state f
        walkExpr state a
    | SynExpr.Paren(expr = e) -> walkExpr state e
    | SynExpr.Tuple(exprs = es) -> es |> List.iter (walkExpr state)
    | SynExpr.Record(recordFields = fields) ->
        for SynExprRecordField(expr = fieldExpr) in fields do
            fieldExpr |> Option.iter (walkExpr state)
    | SynExpr.LetOrUse synLet ->
        for SynBinding(expr = e) in synLet.Bindings do
            walkExpr state e

        walkExpr state synLet.Body
    | SynExpr.Sequential(expr1 = a; expr2 = b) ->
        walkExpr state a
        walkExpr state b
    | SynExpr.IfThenElse(ifExpr = c; thenExpr = t; elseExpr = e) ->
        walkExpr state c
        walkExpr state t
        e |> Option.iter (walkExpr state)
    | SynExpr.Match(expr = scrut; clauses = clauses) ->
        walkExpr state scrut

        for SynMatchClause(resultExpr = r) in clauses do
            walkExpr state r
    | SynExpr.Lambda(body = b) -> walkExpr state b
    | SynExpr.ArrayOrList(exprs = es) -> es |> List.iter (walkExpr state)
    | SynExpr.ArrayOrListComputed(expr = e) -> walkExpr state e
    | SynExpr.ComputationExpr(expr = e) -> walkExpr state e
    | SynExpr.TypeApp(expr = e) -> walkExpr state e
    | SynExpr.Typed(expr = e) -> walkExpr state e
    | SynExpr.Do(expr = e) -> walkExpr state e
    | SynExpr.DotGet(expr = e) -> walkExpr state e
    | SynExpr.DotSet(targetExpr = t; rhsExpr = r) ->
        walkExpr state t
        walkExpr state r
    | SynExpr.LongIdentSet(expr = e) -> walkExpr state e
    | SynExpr.New(expr = e) -> walkExpr state e
    | SynExpr.AddressOf(expr = e) -> walkExpr state e
    | _ -> ()

let private walkBinding (state: WalkState) (SynBinding(expr = e)) = walkExpr state e

let rec private walkDecl (state: WalkState) (decl: SynModuleDecl) =
    match decl with
    | SynModuleDecl.Let(bindings = bs) -> bs |> List.iter (walkBinding state)
    | SynModuleDecl.NestedModule(decls = ds) -> ds |> List.iter (walkDecl state)
    | SynModuleDecl.Expr(expr = e) -> walkExpr state e
    | _ -> ()

let private walkModule (state: WalkState) (SynModuleOrNamespace(decls = decls)) = decls |> List.iter (walkDecl state)

let private parseFile (checker: FSharpChecker) (file: string) (source: string) =
    async {
        let sourceText = SourceText.ofString source
        let! projectOptions, _ = checker.GetProjectOptionsFromScript(file, sourceText)
        let parsingOptions, _ = checker.GetParsingOptionsFromProjectOptions projectOptions

        let! parseResult = checker.ParseFile(file, sourceText, parsingOptions)
        return parseResult
    }

let private walkFile (checker: FSharpChecker) (file: string) =
    async {
        let source = File.ReadAllText file
        let! parseResult = parseFile checker file source

        let state =
            { File = file
              Customs = []
              Registrations = []
              SmartCtors = [] }

        match parseResult.ParseTree with
        | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) -> modules |> List.iter (walkModule state)
        | ParsedInput.SigFile _ -> ()

        return state.Customs |> List.rev, state.Registrations |> List.rev, state.SmartCtors |> List.rev
    }

/// Public entry — walks the supplied source files and returns findings.
let checkSources (checker: FSharpChecker) (manifest: Manifest.Manifest) (files: string list) : Async<Finding list> =
    async {
        let! perFile = files |> List.map (walkFile checker) |> Async.Parallel

        let allCustoms =
            perFile |> Array.collect (fun (cs, _, _) -> List.toArray cs) |> Array.toList

        let allRegistrations =
            perFile |> Array.collect (fun (_, rs, _) -> List.toArray rs) |> Array.toList

        let allSmartCtors =
            perFile |> Array.collect (fun (_, _, ss) -> List.toArray ss) |> Array.toList

        // FUARAN055 — Custom node constructed with `contentHash = None`
        // (or syntactically equivalent). Advisory only.
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
        let smartCtorCount = allSmartCtors.Length

        // Direct NodeKind.Custom calls are those in Customs but with no
        // co-located Fuaran.custom counted in SmartCtors. Approximate by
        // counting Customs minus the `custom` ctor entries in SmartCtors.
        let fuaranCustomInSmartCtors =
            allSmartCtors |> List.filter (fun s -> s.Ctor = "custom") |> List.length

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

        return fuaran055Findings @ fuaran053Findings @ fuaran054Findings
    }
