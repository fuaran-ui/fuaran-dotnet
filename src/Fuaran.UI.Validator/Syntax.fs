module Fuaran.UI.Validator.Syntax

// ============================================================================
//  The one parse and the one traversal every check shares (Phase 2053).
//
//  Each check used to own a full copy of the walker — its own `parseFile`
//  (re-deriving project options per file), its own `walkExpr` / `walkDecl`, and
//  its own copies of the identifier and literal recognisers — so a run parsed
//  every file eleven times and the copies drifted in which expressions they
//  descended into. Now `Validator.run` parses each file ONCE into a
//  `ParsedSource` list, and every check is a visitor over that list:
//
//    - `parseSources` is the only place a file is read and parsed.
//    - `iterSources visit` walks every top-level expression of every source;
//      `visit` returns `true` to let the walk descend into the expression's
//      children (`subExprs`), or `false` when it has handled — or deliberately
//      stopped — the descent itself.
//    - The recognisers below (`leafIdent`, `flattenApp`, `literalString`, …) are
//      the shared vocabulary the checks match with.
// ============================================================================

open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Fuaran.UI.Validator.Findings

// ─── Parsing ────────────────────────────────────────────────────────────────

/// One F# implementation file, parsed once.
type ParsedSource = { File: string; Input: ParsedInput }

/// Parses one source file to its untyped tree; `None` for a signature file.
/// A parameter of `Validator.runWith` so a caller can observe — or count —
/// every parse a run performs.
type Parser = string -> Async<ParsedInput option>

/// The parser a run uses by default. A file is parsed on its own, so the
/// options are the defaults with this file as the only source: deriving them
/// from a synthetic script project per file (what every check used to do)
/// costs far more than the parse and changes nothing the untyped tree carries.
let parser (checker: FSharpChecker) : Parser =
    fun file ->
        async {
            let sourceText = SourceText.ofString (File.ReadAllText file)

            let options =
                { FSharpParsingOptions.Default with
                    SourceFiles = [| file |] }

            let! result = checker.ParseFile(file, sourceText, options)

            return
                match result.ParseTree with
                | ParsedInput.ImplFile _ as tree -> Some tree
                | ParsedInput.SigFile _ -> None
        }

/// Parse every file once, in parallel, keeping the implementation files.
let parseSources (parse: Parser) (files: string list) : Async<ParsedSource list> =
    async {
        let! parsed =
            files
            |> List.map (fun file ->
                async {
                    let! input = parse file
                    return input |> Option.map (fun i -> { File = file; Input = i })
                })
            |> Async.Parallel

        return parsed |> Array.choose id |> Array.toList
    }

/// Every `.fs` source under a project directory. Discovery is
/// directory-recursive plus a filename filter; MSBuild evaluation is
/// deliberately not used, because the validator runs before / outside the
/// build and the directory scan is fast.
let discoverSourceFiles (projectDir: string) : string list =
    if Directory.Exists projectDir then
        Directory.EnumerateFiles(projectDir, "*.fs", SearchOption.AllDirectories)
        |> Seq.filter (fun p ->
            // Skip obj/ and bin/ outputs — generated intermediates shouldn't be re-validated.
            let normalized = p.Replace('\\', '/')

            not (
                normalized.Contains("/obj/")
                || normalized.Contains("/bin/")
                || normalized.EndsWith("AssemblyInfo.fs")
            ))
        |> Seq.sort
        |> List.ofSeq
    else
        []

// ─── Traversal ──────────────────────────────────────────────────────────────

/// The sub-expressions a walk descends into — the one statement of it.
let subExprs (expr: SynExpr) : SynExpr list =
    match expr with
    | SynExpr.App(funcExpr = f; argExpr = a) -> [ f; a ]
    | SynExpr.Paren(expr = e)
    | SynExpr.Lambda(body = e)
    | SynExpr.ArrayOrListComputed(expr = e)
    | SynExpr.ComputationExpr(expr = e)
    | SynExpr.TypeApp(expr = e)
    | SynExpr.Typed(expr = e)
    | SynExpr.Do(expr = e)
    | SynExpr.DotGet(expr = e)
    | SynExpr.LongIdentSet(expr = e)
    | SynExpr.New(expr = e)
    | SynExpr.AddressOf(expr = e)
    | SynExpr.YieldOrReturn(expr = e)
    | SynExpr.YieldOrReturnFrom(expr = e) -> [ e ]
    | SynExpr.Tuple(exprs = es)
    | SynExpr.ArrayOrList(exprs = es) -> es
    | SynExpr.Record(recordFields = fields) -> fields |> List.choose (fun (SynExprRecordField(expr = e)) -> e)
    | SynExpr.LetOrUse synLet -> [ for SynBinding(expr = e) in synLet.Bindings -> e ] @ [ synLet.Body ]
    | SynExpr.Sequential(expr1 = a; expr2 = b)
    | SynExpr.DotSet(targetExpr = a; rhsExpr = b)
    | SynExpr.While(whileExpr = a; doExpr = b)
    | SynExpr.TryFinally(tryExpr = a; finallyExpr = b) -> [ a; b ]
    | SynExpr.ForEach(enumExpr = a; bodyExpr = b) -> [ a; b ]
    | SynExpr.IfThenElse(ifExpr = c; thenExpr = t; elseExpr = e) -> [ c; t ] @ Option.toList e
    | SynExpr.Match(expr = scrut; clauses = clauses) -> scrut :: [ for SynMatchClause(resultExpr = r) in clauses -> r ]
    | SynExpr.MatchLambda(matchClauses = clauses) -> [ for SynMatchClause(resultExpr = r) in clauses -> r ]
    | SynExpr.TryWith(tryExpr = t; withCases = clauses) -> t :: [ for SynMatchClause(resultExpr = r) in clauses -> r ]
    | _ -> []

/// Walk `expr` top-down. `visit` returns `true` to descend into the
/// children, `false` when it has handled (or deliberately stopped) the descent.
let rec iterExpr (visit: SynExpr -> bool) (expr: SynExpr) : unit =
    if visit expr then
        for child in subExprs expr do
            iterExpr visit child

let rec private declExprs (decl: SynModuleDecl) : SynExpr list =
    match decl with
    | SynModuleDecl.Let(bindings = bs) -> [ for SynBinding(expr = e) in bs -> e ]
    | SynModuleDecl.NestedModule(decls = ds) -> ds |> List.collect declExprs
    | SynModuleDecl.Expr(expr = e) -> [ e ]
    | _ -> []

/// The top-level expressions of one source: every module-level binding's
/// body and every bare module-level expression, nested modules included.
let topLevelExprs (source: ParsedSource) : SynExpr list =
    match source.Input with
    | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) ->
        modules
        |> List.collect (fun (SynModuleOrNamespace(decls = decls)) -> decls |> List.collect declExprs)
    | ParsedInput.SigFile _ -> []

/// Walk every top-level expression of every source with `visit (file) expr`.
let iterSources (visit: string -> SynExpr -> bool) (sources: ParsedSource list) : unit =
    for source in sources do
        for expr in topLevelExprs source do
            iterExpr (visit source.File) expr


// ─── Recognisers ────────────────────────────────────────────────────────────

let mkLocation (file: string) (range: range) : Location =
    { File = file
      Line = range.StartLine
      Column = range.StartColumn + 1 }

/// Peel parentheses and type annotations.
let rec unwrap (expr: SynExpr) : SynExpr =
    match expr with
    | SynExpr.Paren(expr = e)
    | SynExpr.Typed(expr = e) -> unwrap e
    | _ -> expr

let constString (c: SynConst) : string option =
    match c with
    | SynConst.String(text = s) -> Some s
    | _ -> None

/// The numeric literal shapes a bound can take (`0.5`, `1`, `1.0f`, a decimal).
let constFloat (c: SynConst) : float option =
    match c with
    | SynConst.Double d -> Some d
    | SynConst.Single f -> Some(float f)
    | SynConst.Int32 i -> Some(float i)
    | SynConst.Decimal d -> Some(float d)
    | _ -> None

/// A string literal, through parentheses and annotations.
let literalString (expr: SynExpr) : string option =
    match unwrap expr with
    | SynExpr.Const(constant = c) -> constString c
    | _ -> None

/// Project a long-ident's segments to their source-text names.
let identNames (ids: Ident list) : string list = ids |> List.map _.idText

/// The qualifier segments, the last segment and its range of an identifier:
///   Fuaran.dashboard -> ["Fuaran"], "dashboard"
///   binding.query    -> ["binding"], "query"
///   Some             -> [], "Some"
let leafIdent (expr: SynExpr) : (string list * string * range) option =
    match expr with
    | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) when not ids.IsEmpty ->
        let names = identNames ids
        Some(List.take (names.Length - 1) names, List.last names, (List.last ids).idRange)
    | SynExpr.Ident i -> Some([], i.idText, i.idRange)
    | _ -> None

/// `qualifier.leaf` where the last qualifier segment is `qualifier`.
let isQualified (qualifier: string) (leaf: string) (expr: SynExpr) : bool =
    match leafIdent expr with
    | Some(prefix, l, _) -> l = leaf && not prefix.IsEmpty && List.last prefix = qualifier
    | None -> false

/// Curry-decompose an application chain: `f a b c` -> (f, [a; b; c]).
let flattenApp (expr: SynExpr) : SynExpr * SynExpr list =
    let rec loop acc =
        function
        | SynExpr.App(funcExpr = f; argExpr = a) -> loop (a :: acc) f
        | head -> head, acc

    loop [] expr

/// The argument of a single-argument application of `leaf` (any qualifier):
/// `Some x` -> x for `applied "Some"`.
let applied (leaf: string) (expr: SynExpr) : SynExpr option =
    match unwrap expr with
    | SynExpr.App(funcExpr = head; argExpr = arg) ->
        match leafIdent head with
        | Some(_, l, _) when l = leaf -> Some arg
        | _ -> None
    | _ -> None

/// `None` / `Option.None` written explicitly.
let isExplicitNone (expr: SynExpr) : bool =
    match unwrap expr with
    | SynExpr.Ident i -> i.idText = "None"
    | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) when not ids.IsEmpty -> (List.last ids).idText = "None"
    | _ -> false

/// The static value of a binding: `Binding.Static (Some <x>)` — `Binding.Static`
/// carries an option, so a static value is always `Some`-wrapped at a call site
/// that compiles — or the smart constructor `binding.``static`` <x>`, which
/// wraps it. `None` for any other shape (a query, state, `Binding.Static None`).
let staticPayload (expr: SynExpr) : SynExpr option =
    match unwrap expr with
    | SynExpr.App(funcExpr = head; argExpr = arg) when isQualified "Binding" "Static" head -> applied "Some" arg
    | SynExpr.App(funcExpr = head; argExpr = arg) when isQualified "binding" "static" head -> Some arg
    | _ -> None

/// Find the first numeric literal inside `expr`, argument before function so
/// `Binding.Static (Some 0.5)` resolves to `0.5`.
let rec firstNumericLiteral (expr: SynExpr) : float option =
    match expr with
    | SynExpr.Const(constant = c) -> constFloat c
    | SynExpr.App(funcExpr = f; argExpr = a) ->
        match firstNumericLiteral a with
        | Some n -> Some n
        | None -> firstNumericLiteral f
    | SynExpr.Paren(expr = e)
    | SynExpr.Typed(expr = e) -> firstNumericLiteral e
    | _ -> None

/// The value expression of `fieldName` in a record expression.
let fieldValue (fieldName: string) (expr: SynExpr) : SynExpr option =
    match unwrap expr with
    | SynExpr.Record(recordFields = fields) ->
        fields
        |> List.tryPick (fun (SynExprRecordField(fieldName = (SynLongIdent(id = ids), _); expr = fieldExpr)) ->
            match ids with
            | [] -> None
            | _ when (List.last ids).idText = fieldName -> fieldExpr
            | _ -> None)
    | _ -> None

/// The items of a list literal: `[]`, `[a]`, `[a; b; c]` (a `Sequential` chain
/// under `ArrayOrListComputed`). `None` when `expr` is not a list literal.
let listItems (expr: SynExpr) : SynExpr list option =
    let rec sequence e =
        match e with
        | SynExpr.Sequential(expr1 = a; expr2 = b) -> sequence a @ sequence b
        | other -> [ other ]

    match unwrap expr with
    | SynExpr.ArrayOrList(isArray = false; exprs = items) -> Some items
    | SynExpr.ArrayOrListComputed(isArray = false; expr = inner) -> Some(sequence inner)
    | _ -> None

// ─── Call collection ────────────────────────────────────────────────────────

/// Every application whose head `pick file head args` recognises. On a match
/// the walk descends into the ARGUMENTS only, so a curried call is recorded
/// once rather than once per partial application inside it.
let collectApps (pick: string -> SynExpr -> SynExpr list -> 'a option) (sources: ParsedSource list) : 'a list =
    let acc = ResizeArray<'a>()

    for source in sources do
        let rec visit (expr: SynExpr) =
            match expr with
            | SynExpr.App _ ->
                let head, args = flattenApp expr

                match pick source.File head args with
                | Some hit ->
                    acc.Add hit
                    args |> List.iter (iterExpr visit)
                    false
                | None -> true
            | _ -> true

        for expr in topLevelExprs source do
            iterExpr visit expr

    List.ofSeq acc
