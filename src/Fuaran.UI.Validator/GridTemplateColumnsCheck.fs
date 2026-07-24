module Fuaran.UI.Validator.GridTemplateColumnsCheck

// ============================================================================
//  Grid template-columns advisory check.
//
//  FUARAN046 (Warning) — a `Fuaran.gridLayoutTemplated` call whose verbatim
//  `templateColumns` string is structurally equivalent to the typed `Cols: int`
//  shape. The canonical equivalent-to-Cols pattern is `repeat(N, 1fr)` — the
//  default `Fuaran.gridLayout` emission. Authors reaching for the escape
//  hatch to express something the typed shape already covers spend the
//  unbounded-string review-tax for no expressivity gain.
//
//  Static-detectable shapes:
//
//    Fuaran.gridLayoutTemplated "id" "repeat(5, 1fr)" spec
//    Fuaran.gridLayoutTemplated "id" "repeat(12, 1fr)" Defaults.gridLayout
//    Fuaran.gridLayoutTemplated "id" "  repeat(5, 1fr)  " spec   (whitespace-tolerant)
//
//  Non-detectable shapes (no finding):
//
//    - The templateColumns argument is a let-bound name / parameter
//      (the walker does not chase let-bindings).
//    - The string is a sprintf / interpolated literal whose result is
//      `repeat(N, 1fr)`.
//    - The call uses the lowered record-update shape directly
//      (`{ Defaults.gridLayout with TemplateColumns = Some "repeat(N, 1fr)" }`).
//
//  Anti-pattern coverage rationale:
//
//    The string-escape shape was picked over a typed CSS-grammar DU
//    deliberately — keeping the typed surface lean and getting the gap
//    closed for irregular grids. The structural detection catches the most
//    common eval-quality regression (the `repeat(N, 1fr)` equivalence)
//    without trying to type-check arbitrary CSS. Other regressions —
//    `auto` columns paired with `1fr`, `repeat(auto-fit, ...)` without
//    `minmax`, etc. — stay in the migration doc's rule-of-thumb guidance
//    until a real eval-quality issue justifies expanding the check.
// ============================================================================

open System.IO
open System.Text.RegularExpressions
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Fuaran.UI.Validator.Findings

/// Matches `repeat(N, 1fr)` (whitespace-tolerant; integer N ≥ 1).
let private repeatOneFrRegex =
    Regex(@"^\s*repeat\(\s*\d+\s*,\s*1fr\s*\)\s*$", RegexOptions.Compiled ||| RegexOptions.IgnoreCase)

type private GridTemplatedCall =
    { Location: Location
      TemplateColumns: string option }

let private mkLocation (file: string) (range: range) : Location =
    { File = file
      Line = range.StartLine
      Column = range.StartColumn + 1 }

let private leafIdent (expr: SynExpr) : (string list * string) option =
    match expr with
    | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) when not ids.IsEmpty ->
        let names = AstWalker.identNames ids
        let leaf = List.last names
        let prefix = names |> List.take (names.Length - 1)
        Some(prefix, leaf)
    | SynExpr.Ident i -> Some([], i.idText)
    | _ -> None

/// `Fuaran.gridLayoutTemplated` — leaf `gridLayoutTemplated` with `Fuaran`
/// immediately before (the smart-ctor module name is `Fuaran` and the
/// smart-ctor lives at `Fuaran.gridLayoutTemplated`).
let private (|FuaranGridLayoutTemplated|_|) (expr: SynExpr) =
    match leafIdent expr with
    | Some(prefix, "gridLayoutTemplated") when not prefix.IsEmpty && List.last prefix = "Fuaran" -> Some()
    | _ -> None

/// Curry-decompose an application chain into (head, args). Mirrors the
/// helper in SegmentedChoiceCheck.
let private flattenApp (expr: SynExpr) : SynExpr * SynExpr list =
    let rec loop acc =
        function
        | SynExpr.App(funcExpr = f; argExpr = a) -> loop (a :: acc) f
        | head -> head, acc

    loop [] expr

/// Unwrap parens / type ascriptions to reach the inner literal.
let rec private peelWrappers (expr: SynExpr) : SynExpr =
    match expr with
    | SynExpr.Paren(expr = e) -> peelWrappers e
    | SynExpr.Typed(expr = e) -> peelWrappers e
    | _ -> expr

/// Recognise a string literal payload — `"foo"` or `(@"foo")` etc.
let private stringLiteralValue (expr: SynExpr) : string option =
    match peelWrappers expr with
    | SynExpr.Const(constant = SynConst.String(text = s)) -> Some s
    | _ -> None

type private WalkState =
    { File: string
      mutable Calls: GridTemplatedCall list }

let rec private walkExpr (state: WalkState) (expr: SynExpr) =
    let head, args = flattenApp expr

    match head with
    | FuaranGridLayoutTemplated ->
        // Positional args: id : string, templateColumns : string, spec.
        // Second positional arg is the templateColumns string.
        let templateColumns =
            match args with
            | _ :: second :: _ -> stringLiteralValue second
            | _ -> None

        state.Calls <-
            { Location = mkLocation state.File head.Range
              TemplateColumns = templateColumns }
            :: state.Calls

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

let private walkFile (checker: FSharpChecker) (file: string) : Async<GridTemplatedCall list> =
    async {
        let source = File.ReadAllText file
        let! parseResult = parseFile checker file source

        let state = { File = file; Calls = [] }

        match parseResult.ParseTree with
        | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) -> modules |> List.iter (walkModule state)
        | ParsedInput.SigFile _ -> ()

        return state.Calls |> List.rev
    }

let checkSources (checker: FSharpChecker) (files: string list) : Async<Finding list> =
    async {
        let! perFile = files |> List.map (walkFile checker) |> Async.Parallel
        let allCalls = perFile |> Array.collect List.toArray |> Array.toList

        let findings =
            allCalls
            |> List.choose (fun call ->
                match call.TemplateColumns with
                | Some s when repeatOneFrRegex.IsMatch s ->
                    let base' =
                        create
                            Warning
                            "FUARAN046"
                            call.Location
                            (sprintf
                                "Fuaran.gridLayoutTemplated: templateColumns %A is equivalent to the typed Cols-based emission. Use Fuaran.gridLayout with `Cols = N` instead — the typed shape avoids the unbounded-string escape's review tax for no expressivity gain."
                                s)

                    let suggestion =
                        "use Fuaran.gridLayout with the typed Cols field; reach for gridLayoutTemplated only when the sizing function (1fr 2fr, 100px repeat(...), min-content max-content, auto-fit minmax) can't be expressed by Cols"

                    Some(withRecovery [] (Some suggestion) base')
                | _ -> None)

        return findings
    }
