module Fuaran.UI.Validator.SegmentedChoiceCheck

// ============================================================================
//  Segmented-choice option-count check.
//
//  FUARAN045 (Warning) — a `FormFieldKind.segmentedChoice` whose `options`
//  argument is a statically-detectable `Binding.Static [ ... ]` (or
//  `binding.static [ ... ]`) list literal with more than 7 items. Segmented
//  controls work best with ≤5 visible options; >7 should reach for
//  `FormFieldKind.Choice` (dropdown) instead — the visible-options trade-
//  off inverts past that point.
//
//  Mirror of `NumberFieldRangeCheck` (FUARAN051) at the smart-
//  ctor call site. Advisory only — Warning, not Error, so the build still
//  passes during incremental adoption / experimentation.
//
//  Static-detectable shapes:
//
//    FormFieldKind.segmentedChoice
//        (binding.static [ a; b; c; d; e; f; g; h ])
//        valueBinding onChange Orientation.Horizontal
//
//    FormFieldKind.segmentedChoice
//        (Binding.Static [ ...; ]) valueBinding onChange Orientation.Vertical
//
//  Non-detectable shapes (no finding):
//
//    - `options` is a `Binding.Query` / `Binding.State` / `Binding.Computed`
//      (no compile-time count)
//    - The list literal is a `let`-bound name (`let opts = [...]` then
//      `binding.static opts` — the walker does not chase let-bindings)
//    - The call uses the lowered `FormFieldKind.SegmentedChoice(...)` DU
//      constructor directly (the walker recognises only the smart-ctor)
// ============================================================================

open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Fuaran.UI.Validator.Findings

/// Threshold above which FUARAN045 fires. ≤5 is the canonical sweet spot;
/// 6–7 still works; 8+ is the visible-options anti-pattern that should
/// migrate to `FormFieldKind.Choice`.
[<Literal>]
let private OptionCountWarningThreshold = 7

type private SegmentedChoiceCall =
    { Location: Location
      OptionCount: int option }

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

/// `FormFieldKind.segmentedChoice` — leaf `segmentedChoice` with
/// `FormFieldKind` immediately before.
let private (|FormFieldKindSegmentedChoice|_|) (expr: SynExpr) =
    match leafIdent expr with
    | Some(prefix, "segmentedChoice") when not prefix.IsEmpty && List.last prefix = "FormFieldKind" -> Some()
    | _ -> None

/// Curry-decompose an application chain into (head, args). Mirrors the
/// helper in LocalBindingCheck / NumberFieldRangeCheck.
let private flattenApp (expr: SynExpr) : SynExpr * SynExpr list =
    let rec loop acc =
        function
        | SynExpr.App(funcExpr = f; argExpr = a) -> loop (a :: acc) f
        | head -> head, acc

    loop [] expr

/// Count the leaves of a `Sequential` chain. `[a; b; c]` parses as
/// right-associative `Sequential(a, Sequential(b, c))` under
/// `ArrayOrListComputed`. Mirrors `AstWalker.sequentialLeafCount`.
let rec private sequentialLeafCount (e: SynExpr) : int =
    match e with
    | SynExpr.Sequential(expr1 = a; expr2 = b) -> sequentialLeafCount a + sequentialLeafCount b
    | _ -> 1

/// Length of a static list literal, or `None` for non-literal shapes.
/// Mirrors `AstWalker.staticListLength`.
let private staticListLength (expr: SynExpr) : int option =
    let rec inner (e: SynExpr) =
        match e with
        | SynExpr.Paren(expr = e') -> inner e'
        | SynExpr.Typed(expr = e') -> inner e'
        | SynExpr.ArrayOrList(exprs = es) -> Some es.Length
        | SynExpr.ArrayOrListComputed(isArray = false; expr = inner') -> Some(sequentialLeafCount inner')
        | _ -> None

    inner expr

/// Recurse through `binding.static <list>` / `Binding.Static <list>` /
/// parenthesised / typed shapes looking for the wrapped list literal.
let rec private firstStaticListLengthIn (expr: SynExpr) : int option =
    match expr with
    | SynExpr.Paren(expr = e) -> firstStaticListLengthIn e
    | SynExpr.Typed(expr = e) -> firstStaticListLengthIn e
    // `binding.static [..]` or `Binding.Static [..]` — application of a
    // recognised head to a list literal.
    | SynExpr.App(funcExpr = head; argExpr = arg) ->
        match leafIdent head with
        | Some(prefix, leaf) when leaf = "static" && not prefix.IsEmpty && List.last prefix = "binding" ->
            staticListLength arg
        | Some(prefix, leaf) when leaf = "Static" && not prefix.IsEmpty && List.last prefix = "Binding" ->
            staticListLength arg
        | _ ->
            // Try inner descent — handles parenthesised / typed wrappers
            // around a nested call.
            match firstStaticListLengthIn arg with
            | Some n -> Some n
            | None -> firstStaticListLengthIn head
    | SynExpr.ArrayOrList _
    | SynExpr.ArrayOrListComputed _ ->
        // Bare list literal passed as the `options` argument — no static
        // wrapper. Still count it (some authors write the list directly).
        staticListLength expr
    | _ -> None

type private WalkState =
    { File: string
      mutable Calls: SegmentedChoiceCall list }

let rec private walkExpr (state: WalkState) (expr: SynExpr) =
    let head, args = flattenApp expr

    match head with
    | FormFieldKindSegmentedChoice ->
        // First positional argument is `options : Binding<SelectOption list>`.
        let optionCount =
            match args with
            | first :: _ -> firstStaticListLengthIn first
            | [] -> None

        state.Calls <-
            { Location = mkLocation state.File head.Range
              OptionCount = optionCount }
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

let private walkFile (checker: FSharpChecker) (file: string) : Async<SegmentedChoiceCall list> =
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
                match call.OptionCount with
                | Some count when count > OptionCountWarningThreshold ->
                    let base' =
                        create
                            Warning
                            "FUARAN045"
                            call.Location
                            (sprintf
                                "FormFieldKind.segmentedChoice: %d options is more than the recommended maximum of %d for a visible-options exclusive-choice surface. Reach for FormFieldKind.Choice (dropdown) instead — the segmented-control trade-off inverts past 7 options."
                                count
                                OptionCountWarningThreshold)

                    let suggestion =
                        sprintf
                            "use FormFieldKind.Choice (dropdown) for %d options; SegmentedChoice is sized for ≤5 visible options"
                            count

                    Some(withRecovery [] (Some suggestion) base')
                | _ -> None)

        return findings
    }
