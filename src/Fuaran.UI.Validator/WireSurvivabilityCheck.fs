module Fuaran.UI.Validator.WireSurvivabilityCheck

// ============================================================================
//  Wire-survivability check (Phase 378) — FUARAN084.
//
//  The build-time, author-facing lens over the wire-survivability boundary
//  (`Fuaran.UI.WireSurvivability`). It flags a `Binding.Computed` — the one
//  whole-case host-only escape that is cleanly detectable from the untyped AST:
//  a single `(BindingContext -> 'T)` closure that erases to the `"<closure>"`
//  sentinel, so a decoded / op-stream-replayed / introspected tree (and the
//  TypeScript / Python hosts) sees an inert placeholder.
//
//  Severity is context-sensitive per the phase's contract:
//    - hand-authored source  -> Warning (advisory; `Binding.Computed` is a
//      sanctioned F#-only escape),
//    - orchestrated / AI-emitted context (`orchestrated = true`) -> Error
//      (an AI author should stay on the wire-survivable substrate).
//
//  Scope boundary: the two other Phase 378 targets — a closure-formatted grid
//  column where `Column.Field` + `CellFormat` would survive, and a non-primitive
//  `Binding.Static` (the `"<opaque>"` FUARAN041 case) — need the runtime value's
//  TYPE to classify precisely, which the untyped AST does not carry (the
//  validator's deliberate v1 boundary — `AstWalker.fs`). Those stay with the
//  runtime `DeadOnDecode` lint (FUARAN080/081), which has the decoded tree in
//  hand; this build-time check owns the syntactically-unambiguous `Computed`.
//
//  The DECODE-SIDE twin is `Fuaran.UI.AffordanceInertness` (Phase 924), and the
//  departure from this module's shape is deliberate rather than an omission.
//  This check parses SOURCE and warns an AUTHOR before an emission exists; that
//  one takes a decoded `Node` tree and tells a CONSUMER which of the affordances
//  it is already holding do nothing. Neither can do the other's job: an author's
//  closures are real, so running this module's judgement over an F#-authored
//  tree is a false accusation, and by the time a tree has crossed the wire there
//  is no source left to parse and nothing left to refuse. Both read
//  `WireSurvivability`; only that table is shared.
// ============================================================================

open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Fuaran.UI.Validator.Findings

let private mkLocation (file: string) (range: range) : Location =
    { File = file
      Line = range.StartLine
      Column = range.StartColumn + 1 }

/// Recognise the last-segment identifier of an applied call.
let private leafIdent (expr: SynExpr) : (string list * string) option =
    match expr with
    | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) when not ids.IsEmpty ->
        let names = AstWalker.identNames ids
        Some(names |> List.take (names.Length - 1), List.last names)
    | SynExpr.Ident i -> Some([], i.idText)
    | _ -> None

/// `Binding.Computed (...)` (the raw DU case) or `binding.computed (...)` (a
/// lower-case smart-ctor, if present) — leaf `Computed`/`computed` with
/// `Binding`/`binding` immediately before.
let private (|BindingComputed|_|) (expr: SynExpr) =
    match leafIdent expr with
    | Some(prefix, ("Computed" | "computed")) when
        not prefix.IsEmpty
        && (List.last prefix = "Binding" || List.last prefix = "binding")
        ->
        Some()
    | _ -> None

type private WalkState =
    { File: string
      mutable Sites: Location list }

let rec private walkExpr (state: WalkState) (expr: SynExpr) =
    match expr with
    | SynExpr.App(funcExpr = (BindingComputed as f); argExpr = a) ->
        state.Sites <- mkLocation state.File f.Range :: state.Sites
        walkExpr state a
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

let private walkFile (checker: FSharpChecker) (file: string) : Async<Location list> =
    async {
        let source = File.ReadAllText file
        let! parseResult = parseFile checker file source
        let state = { File = file; Sites = [] }

        match parseResult.ParseTree with
        | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) -> modules |> List.iter (walkModule state)
        | ParsedInput.SigFile _ -> ()

        return state.Sites |> List.rev
    }

/// The recoverable-alternative hint, sourced from the single classification
/// table so the check and the docs never drift.
let private alternative =
    Fuaran.UI.WireSurvivability.byCase
    |> Map.tryFind "Binding.Computed"
    |> Option.bind (fun c -> c.Alternative)
    |> Option.defaultValue "Binding.State / Binding.Filter / Binding.Transform / Binding.Format"

/// Public entry. `orchestrated = true` escalates the finding to Error (an
/// AI-emitted tree must stay wire-survivable); hand-authored source gets a
/// Warning (advisory).
let checkSources (orchestrated: bool) (checker: FSharpChecker) (files: string list) : Async<Finding list> =
    async {
        let! perFile = files |> List.map (walkFile checker) |> Async.Parallel

        let severity = if orchestrated then Error else Warning

        let message =
            sprintf
                "Binding.Computed is host-only — it erases to the \"<closure>\" sentinel on the wire, so a decoded / op-stream-replayed / introspected tree (and the TypeScript / Python hosts) sees an inert placeholder. Prefer %s.%s"
                alternative
                (if orchestrated then
                     " (An orchestrated / AI-emitted tree must stay on the wire-survivable substrate.)"
                 else
                     " (Advisory: a sanctioned F#-only escape in hand-authored code.)")

        return
            perFile
            |> Array.toList
            |> List.collect id
            |> List.map (fun loc ->
                { create severity "FUARAN084" loc message with
                    Suggestion = Some alternative })
    }
