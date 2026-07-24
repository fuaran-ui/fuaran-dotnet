module Fuaran.UI.Validator.FormatCoherenceCheck

// ============================================================================
//  Format coherence check (Phase 102).
//
//  FUARAN061 (Error) — a locale-aware currency `Format` whose ISO-4217 code is
//  a blank string literal. A `Format.Currency ""` (or the smart-ctor
//  `localeFormat.currency ""`) is an incoherent combination: the renderer
//  hands the code to `Intl.NumberFormat({ style: 'currency', currency: "" })`,
//  which throws a `RangeError` at render time (and the .NET fallback emits a
//  stray bare-code prefix). The typed surface keeps `isoCode` mandatory, so the
//  only static incoherence is an *empty / whitespace* literal — exactly what
//  this rule rejects at build time, per the phase's "Currency without an ISO
//  code" acceptance criterion.
//
//  Static-detectable shapes:
//
//    Format.Currency ""
//    Format.Currency "   "
//    localeFormat.currency ""
//    binding.format src (Format.Currency "") locale.ambient   // nested
//
//  Non-detectable shapes (no finding):
//
//    - `isoCode` is a non-literal expression (a `let`-bound value, a function
//      result) — no compile-time string to inspect.
//    - A non-blank but invalid code (e.g. "ZZ") — the typed surface can't
//      distinguish a real ISO-4217 code from a typo without a currency table;
//      that's a runtime `RangeError`, out of scope for the static rule.
// ============================================================================

open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Fuaran.UI.Validator.Findings

type private CurrencyCall =
    { Location: Location
      IsoLiteral: string option }

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

/// `Format.Currency` (the DU case) or `localeFormat.currency` (the smart-ctor)
/// — the leaf is `Currency` / `currency` with the matching qualifier directly
/// before it.
let private (|CurrencyCtor|_|) (expr: SynExpr) =
    match leafIdent expr with
    | Some(prefix, "Currency") when not prefix.IsEmpty && List.last prefix = "Format" -> Some()
    | Some(prefix, "currency") when not prefix.IsEmpty && List.last prefix = "localeFormat" -> Some()
    | _ -> None

/// Curry-decompose an application chain into (head, args).
let private flattenApp (expr: SynExpr) : SynExpr * SynExpr list =
    let rec loop acc =
        function
        | SynExpr.App(funcExpr = f; argExpr = a) -> loop (a :: acc) f
        | head -> head, acc

    loop [] expr

/// First string literal reached through paren / typed wrappers.
let rec private firstStringLiteralIn (expr: SynExpr) : string option =
    match expr with
    | SynExpr.Const(constant = SynConst.String(text = s)) -> Some s
    | SynExpr.Paren(expr = e) -> firstStringLiteralIn e
    | SynExpr.Typed(expr = e) -> firstStringLiteralIn e
    | _ -> None

type private WalkState =
    { File: string
      mutable Calls: CurrencyCall list }

let rec private walkExpr (state: WalkState) (expr: SynExpr) =
    let head, args = flattenApp expr

    match head with
    | CurrencyCtor ->
        let isoLiteral =
            match args with
            | first :: _ -> firstStringLiteralIn first
            | [] -> None

        state.Calls <-
            { Location = mkLocation state.File head.Range
              IsoLiteral = isoLiteral }
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

let private walkFile (checker: FSharpChecker) (file: string) : Async<CurrencyCall list> =
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
                match call.IsoLiteral with
                | Some iso when System.String.IsNullOrWhiteSpace iso ->
                    let base' =
                        create
                            Error
                            "FUARAN061"
                            call.Location
                            "Format.Currency was given a blank ISO-4217 currency code. The renderer passes it to Intl.NumberFormat({ style: 'currency', currency: '' }), which throws a RangeError at render time. Supply a valid ISO-4217 code (e.g. \"GBP\", \"USD\", \"EUR\")."

                    Some(
                        withRecovery
                            []
                            (Some "Replace the empty string with a valid ISO-4217 currency code, e.g. \"GBP\".")
                            base'
                    )
                | _ -> None)

        return findings
    }
