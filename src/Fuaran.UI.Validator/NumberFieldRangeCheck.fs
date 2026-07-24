module Fuaran.UI.Validator.NumberFieldRangeCheck

// ============================================================================
//  Number-field range check.
//
//  FUARAN051 (Warning) — a `FormFieldKind.rangedNumber` call whose `value`
//  is a `Binding.Static <lit>` literal that falls outside the declared
//  `[Min, Max]` interval. The renderer would pass the literal straight to
//  the browser, which then either clamps it to `min` / `max` (HTML form
//  validation) or rejects the form submission silently — neither is what
//  the author intended.
//
//  Mirror of `ScalarRangeCheck` (FUARAN050) but at the form-
//  field call site rather than the `progress` ctor call site. Advisory
//  only — emits a Warning so it doesn't fail the build during incremental
//  adoption.
//
//  Static-detectable shapes:
//
//    FormFieldKind.rangedNumber (binding.static 1900.0) onChange ~min:1979.0 ~max:2028.0
//    FormFieldKind.rangedNumber (Binding.Static 1900.0) onChange ~min:1979.0 ~max:2028.0
//
//  Non-detectable shapes (no finding):
//
//    - `value` is a `Binding.Query` / `Binding.State` / `Binding.Computed`
//      (no compile-time value)
//    - `min` / `max` are non-literal expressions
//    - The call uses the lowered `FormFieldKind.RangedNumber(...)` DU
//      constructor directly (the validator's textual walker doesn't try
//      to parse DU-positional arguments)
// ============================================================================

open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Fuaran.UI.Validator.Findings

type private RangedNumberCall =
    { Location: Location
      ValueLiteral: float option
      MinLiteral: float option
      MaxLiteral: float option }

let private mkLocation (file: string) (range: range) : Location =
    { File = file
      Line = range.StartLine
      Column = range.StartColumn + 1 }

let private constFloatValue (c: SynConst) : float option =
    match c with
    | SynConst.Double d -> Some d
    | SynConst.Single f -> Some(float f)
    | SynConst.Int32 i -> Some(float i)
    | SynConst.Decimal d -> Some(float d)
    | _ -> None

let private leafIdent (expr: SynExpr) : (string list * string) option =
    match expr with
    | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) when not ids.IsEmpty ->
        let names = AstWalker.identNames ids
        let leaf = List.last names
        let prefix = names |> List.take (names.Length - 1)
        Some(prefix, leaf)
    | SynExpr.Ident i -> Some([], i.idText)
    | _ -> None

/// `FormFieldKind.rangedNumber` — leaf `rangedNumber` with `FormFieldKind`
/// immediately before.
let private (|FormFieldKindRangedNumber|_|) (expr: SynExpr) =
    match leafIdent expr with
    | Some(prefix, "rangedNumber") when not prefix.IsEmpty && List.last prefix = "FormFieldKind" -> Some()
    | _ -> None

/// Curry-decompose an application chain into (head, args). Mirrors the
/// helper in LocalBindingCheck.
let private flattenApp (expr: SynExpr) : SynExpr * SynExpr list =
    let rec loop acc =
        function
        | SynExpr.App(funcExpr = f; argExpr = a) -> loop (a :: acc) f
        | head -> head, acc

    loop [] expr

/// Walk an expression looking for the first numeric literal — recurses
/// through `Binding.Static x` / `binding.static x` / parenthesised /
/// typed shapes. Symmetric with AstWalker.firstNumericLiteralIn but kept
/// local to this check so it can evolve independently.
let rec private firstNumericLiteralIn (expr: SynExpr) : float option =
    match expr with
    | SynExpr.Const(constant = c) -> constFloatValue c
    | SynExpr.App(funcExpr = f; argExpr = a) ->
        match firstNumericLiteralIn a with
        | Some n -> Some n
        | None -> firstNumericLiteralIn f
    | SynExpr.Paren(expr = e) -> firstNumericLiteralIn e
    | SynExpr.Typed(expr = e) -> firstNumericLiteralIn e
    | _ -> None

/// Recognise an optional named argument of `rangedNumber`. F# parses
/// `(min = 1979.0)` as the infix-equality expression
/// `SynExpr.App(SynExpr.App(SynExpr.LongIdent ["op_Equality"], <ident>),
/// <value>)` (the parser doesn't distinguish named-argument syntax from
/// infix `=` at this layer — the type-checker resolves it later based
/// on the called function's signature). Returns the value expression
/// whose LHS ident matches `argName`, otherwise `None`.
let private firstNumericLiteralOfNamedArg (argName: string) (args: SynExpr list) : float option =
    let isOpEqualityIdent (e: SynExpr) : bool =
        match e with
        | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) when not ids.IsEmpty ->
            (List.last ids).idText = "op_Equality"
        | SynExpr.Ident i -> i.idText = "op_Equality"
        | _ -> false

    let identMatches (target: string) (e: SynExpr) : bool =
        let rec inner (x: SynExpr) =
            match x with
            | SynExpr.Paren(expr = inner') -> inner inner'
            | SynExpr.Typed(expr = inner') -> inner inner'
            | SynExpr.Ident i -> i.idText = target || i.idText = "?" + target
            | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) when not ids.IsEmpty ->
                let leaf = (List.last ids).idText
                leaf = target || leaf = "?" + target
            | _ -> false

        inner e

    let rec namedAssignValue (target: string) (e: SynExpr) : SynExpr option =
        match e with
        | SynExpr.Paren(expr = inner') -> namedAssignValue target inner'
        | SynExpr.Typed(expr = inner') -> namedAssignValue target inner'
        // Infix equality `name = value` lowered to
        // `App(App(op_Equality, name), value)`.
        | SynExpr.App(funcExpr = SynExpr.App(funcExpr = opEq; argExpr = nameExpr); argExpr = value) when
            isOpEqualityIdent opEq && identMatches target nameExpr
            ->
            Some value
        | _ -> None

    args
    |> List.tryPick (namedAssignValue argName)
    |> Option.bind firstNumericLiteralIn

type private WalkState =
    { File: string
      mutable Calls: RangedNumberCall list }

let rec private walkExpr (state: WalkState) (expr: SynExpr) =
    let head, args = flattenApp expr

    match head with
    | FormFieldKindRangedNumber ->
        // First positional argument is the `value` (a Binding<float>);
        // its first numeric literal is the Static value if any.
        let valueLiteral =
            match args with
            | first :: _ -> firstNumericLiteralIn first
            | [] -> None

        let minLiteral = firstNumericLiteralOfNamedArg "min" args
        let maxLiteral = firstNumericLiteralOfNamedArg "max" args

        state.Calls <-
            { Location = mkLocation state.File (head.Range)
              ValueLiteral = valueLiteral
              MinLiteral = minLiteral
              MaxLiteral = maxLiteral }
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

let private walkFile (checker: FSharpChecker) (file: string) : Async<RangedNumberCall list> =
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
                match call.ValueLiteral, call.MinLiteral, call.MaxLiteral with
                | Some v, minOpt, maxOpt ->
                    let belowMin =
                        match minOpt with
                        | Some m -> v < m
                        | None -> false

                    let aboveMax =
                        match maxOpt with
                        | Some m -> v > m
                        | None -> false

                    if belowMin || aboveMax then
                        let rangeJson =
                            let minPart =
                                match minOpt with
                                | Some m -> sprintf "\"min\":%g," m
                                | None -> ""

                            let maxPart =
                                match maxOpt with
                                | Some m -> sprintf "\"max\":%g" m
                                | None -> "\"max\":null"

                            sprintf "{\"kind\":\"decimalRange\",%s%s}" minPart maxPart

                        let suggestion =
                            match minOpt, maxOpt with
                            | Some lo, Some hi -> sprintf "set value to a number in [%g, %g]" lo hi
                            | Some lo, None -> sprintf "set value to a number >= %g" lo
                            | None, Some hi -> sprintf "set value to a number <= %g" hi
                            | None, None -> "no range declared"

                        let base' =
                            create
                                Warning
                                "FUARAN051"
                                call.Location
                                (sprintf
                                    "FormFieldKind.rangedNumber: the Binding.Static value literal %g is outside the declared [Min, Max] range. The renderer will pass the literal through to the browser's <input type=number>, which will clamp or reject the field on submission. supportedRange=%s"
                                    v
                                    rangeJson)

                        Some(withRecovery [] (Some suggestion) base')
                    else
                        None
                | None, _, _ -> None)

        return findings
    }
