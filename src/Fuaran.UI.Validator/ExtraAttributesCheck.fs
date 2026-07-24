module Fuaran.UI.Validator.ExtraAttributesCheck

// ============================================================================
//  ExtraAttributes-sanitization check.
//
//  FUARAN060 (Warning) — a `Node.withExtraAttribute "key" "value"` call whose
//  literal `key` argument violates the data-* / aria-* allowlist OR matches
//  a known dangerous key prefix (`on*` event handlers, `style`). Renderer-
//  side `Sanitize.sanitizeExtraAttributes` is the runtime floor, but the
//  build-time signal catches the author mistake before it ships.
//
//  The rule walks the untyped F# AST. It detects
//  `Node.withExtraAttribute <literal-key> <literal-value>` shapes
//  specifically — non-literal key expressions (e.g. a computed key from a
//  let binding) are silenced; the renderer-time floor is the only gate for
//  those.
//
//  Mirror of `LocalBindingCheck` / `NumberFieldRangeCheck` — narrow AST
//  walker with its own pattern, kept separate from the main Fuaran.X
//  smart-ctor walker.
// ============================================================================

open System
open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Fuaran.UI.Validator.Findings

type private ExtraAttrCall =
    { Location: Location
      KeyLiteral: string }

let private mkLocation (file: string) (range: range) : Location =
    { File = file
      Line = range.StartLine
      Column = range.StartColumn + 1 }

let private constStringValue (c: SynConst) =
    match c with
    | SynConst.String(text = s) -> Some s
    | _ -> None

let private argStringLiteral (expr: SynExpr) : string option =
    match expr with
    | SynExpr.Const(constant = c) -> constStringValue c
    | SynExpr.Paren(expr = SynExpr.Const(constant = c)) -> constStringValue c
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

let private (|NodeWithExtraAttribute|_|) (expr: SynExpr) =
    match leafIdent expr with
    | Some(prefix, "withExtraAttribute") when not prefix.IsEmpty && List.last prefix = "Node" -> Some()
    | _ -> None

let private flattenApp (expr: SynExpr) : SynExpr * SynExpr list =
    let rec loop acc =
        function
        | SynExpr.App(funcExpr = f; argExpr = a) -> loop (a :: acc) f
        | head -> head, acc

    loop [] expr

let private isAllowedKey (key: string) : bool =
    if String.IsNullOrEmpty key then
        false
    else
        let trimmed = key.Trim()

        if trimmed.StartsWith("on", StringComparison.OrdinalIgnoreCase) then
            false
        elif trimmed.Equals("style", StringComparison.OrdinalIgnoreCase) then
            false
        else
            trimmed.StartsWith("data-", StringComparison.Ordinal)
            || trimmed.StartsWith("aria-", StringComparison.Ordinal)

type private WalkState =
    { File: string
      mutable Calls: ExtraAttrCall list }

let rec private walkExpr (state: WalkState) (expr: SynExpr) =
    let head, args = flattenApp expr

    match head, args with
    | NodeWithExtraAttribute, keyExpr :: _ ->
        match argStringLiteral keyExpr with
        | Some literalKey when not (isAllowedKey literalKey) ->
            state.Calls <-
                { Location = mkLocation state.File keyExpr.Range
                  KeyLiteral = literalKey }
                :: state.Calls
        | _ -> ()

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

let private walkFile (checker: FSharpChecker) (file: string) : Async<ExtraAttrCall list> =
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
            |> List.map (fun call ->
                let reason =
                    let trimmed = call.KeyLiteral.Trim()

                    if trimmed.StartsWith("on", StringComparison.OrdinalIgnoreCase) then
                        "event-handler attribute (on*) — would inject inline script if reached the DOM"
                    elif trimmed.Equals("style", StringComparison.OrdinalIgnoreCase) then
                        "raw CSS sink — vector for content-spoofing and legacy expression() injection"
                    else
                        "outside the data-* / aria-* allowlist"

                let base' =
                    create
                        Warning
                        "FUARAN060"
                        call.Location
                        (sprintf
                            "Node.withExtraAttribute key \"%s\" is %s. The render-time Sanitize.sanitizeExtraAttributes floor will drop this entry, but the build-time signal catches it earlier. Use a data-* or aria-* key, or move the behaviour into a typed Action / Accessibility field."
                            call.KeyLiteral
                            reason)

                withRecovery
                    [ "data-<custom-name>"; "aria-<standard-name>" ]
                    (Some "rename the key to a data-* test hook or aria-* accessibility attribute")
                    base')

        return findings
    }
