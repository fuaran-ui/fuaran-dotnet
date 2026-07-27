module Fuaran.UI.Validator.LocalBindingCheck

// ============================================================================
//  Local-binding check.
//
//  Three defect codes — FUARAN042 / FUARAN043 / FUARAN044 — guard the typed
//  contract for `binding.local`:
//
//   - FUARAN042 LocalBindingMissingFormatParse  (Error): the `format`
//     argument of `binding.local` is `None` (statically detected). The
//     renderer's default `string<'T>` fallback works for trivial cases but
//     the canonical Salary-style use needs a thousands-separated formatter;
//     the anti-pattern "Don't bypass Parse errors silently" pairs
//     with this rule.
//
//   - FUARAN043 LocalBindingFlushOnNeverCommits (Warning): a `binding.local`
//     with `flushOn = OnCommitAction` declared on a field whose `Id`
//     never appears as the payload of an `Action.CommitLocal` in the same
//     module. Without the explicit-commit-action partner, the buffer
//     never drains.
//
//   - FUARAN044 LocalBindingOnNonInputField     (Error): `binding.local`
//     appears outside a `FormFieldKind.Text(...)` / `FormFieldKind.Number(...)`
//     / `FormFieldKind.RangedNumber(...)` enclosing position (e.g. assigned
//     to a `Metric.Source` slot). The renderer's useState-slot wiring is only
//     active for those three kinds; anywhere else the binding silently
//     resolves to its InitialFrom-side and the local-buffer semantics are
//     lost. The recognised set is `localHostingKinds` below and must track
//     `Render.fs`'s `Binding.Local` arms — omitting a kind the renderer does
//     support turns this Error into a false positive on correct code.
//
//  All three rules walk the untyped F# AST per the validator's scope —
//  they don't consult the typed type-universe. A `binding.local` call
//  enclosing context is detected lexically (the nearest enclosing
//  local-hosting `FormFieldKind` application).
// ============================================================================

open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Fuaran.UI.Validator.Findings

/// One observed `binding.local` invocation.
type private LocalBindingCall =
    {
        Location: Location
        /// True when the AST shows the `format` argument as literal `None`
        /// / `Option.None`. The smart-ctor signature is:
        /// `local initialFrom flushOn onCommit format parse` — `format` is
        /// the 4th positional arg.
        FormatExplicitNone: bool
        /// True when the AST shows `flushOn = OnCommitAction` (or the
        /// fully-qualified `LocalFlushTrigger.OnCommitAction`).
        FlushOnIsCommitAction: bool
        /// The enclosing kind shape — `Some "Text"` / `Some "Number"` /
        /// `Some "RangedNumber"` when the binding sits inside one of the
        /// local-hosting `FormFieldKind` constructors; `None` for any other
        /// position.
        EnclosingKind: string option
    }

/// Every `Action.CommitLocal "..."` reference in the project, paired with
/// the literal node-id string.
and private CommitLocalReference =
    { NodeIdLiteral: string
      Location: Location }

let private mkLocation (file: string) (range: range) : Location =
    { File = file
      Line = range.StartLine
      Column = range.StartColumn + 1 }

let private constStringValue (c: SynConst) =
    match c with
    | SynConst.String(text = s) -> Some s
    | _ -> None

let private firstArgStringLiteral (expr: SynExpr) : string option =
    match expr with
    | SynExpr.Const(constant = c) -> constStringValue c
    | SynExpr.Paren(expr = SynExpr.Const(constant = c)) -> constStringValue c
    | _ -> None

/// Recognise the last-segment identifier of an applied call.
let private leafIdent (expr: SynExpr) : (string list * string) option =
    match expr with
    | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) when not ids.IsEmpty ->
        let names = AstWalker.identNames ids
        let leaf = List.last names
        let prefix = names |> List.take (names.Length - 1)
        Some(prefix, leaf)
    | SynExpr.Ident i -> Some([], i.idText)
    | _ -> None

/// `binding.local` — leaf `local` with `binding` immediately before.
let private (|BindingLocal|_|) (expr: SynExpr) =
    match leafIdent expr with
    | Some(prefix, "local") when not prefix.IsEmpty && List.last prefix = "binding" -> Some()
    | _ -> None

/// The field kinds whose renderer arm mounts the per-NodeId `useState`
/// buffer — i.e. the positions where a `Binding.Local` actually behaves as a
/// local buffer. Kept in step with `Render.fs`'s `Binding.Local` arms:
/// `FormFieldKind.Text`, `.Number` and `.RangedNumber` (the last renders
/// "exactly like the plain Number arm, Local-bound variant included").
let private localHostingKinds = Set.ofList [ "Text"; "Number"; "RangedNumber" ]

let private (|FormFieldKindCtor|_|) (expr: SynExpr) =
    match leafIdent expr with
    | Some(prefix, leaf) when
        not prefix.IsEmpty
        && List.last prefix = "FormFieldKind"
        && localHostingKinds.Contains leaf
        ->
        Some leaf
    | _ -> None

/// `Action.CommitLocal` (or just `CommitLocal` if not qualified).
let private (|ActionCommitLocal|_|) (expr: SynExpr) =
    match leafIdent expr with
    | Some(prefix, "CommitLocal") when not prefix.IsEmpty && List.last prefix = "Action" -> Some()
    | _ -> None

let private isExplicitNoneExpr (expr: SynExpr) : bool =
    let rec inner (e: SynExpr) =
        match e with
        | SynExpr.Paren(expr = inner') -> inner inner'
        | SynExpr.Typed(expr = inner') -> inner inner'
        | SynExpr.Ident i -> i.idText = "None"
        | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) when not ids.IsEmpty -> (List.last ids).idText = "None"
        | _ -> false

    inner expr

let private isOnCommitActionExpr (expr: SynExpr) : bool =
    let rec inner (e: SynExpr) =
        match e with
        | SynExpr.Paren(expr = inner') -> inner inner'
        | SynExpr.Typed(expr = inner') -> inner inner'
        | SynExpr.Ident i -> i.idText = "OnCommitAction"
        | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) when not ids.IsEmpty ->
            (List.last ids).idText = "OnCommitAction"
        | _ -> false

    inner expr

/// Curry-decompose an application chain into (head, args). `binding.local
/// initialFrom flushOn onCommit format parse` parses as left-associative
/// `App(App(App(App(App(binding.local, init), flush), commit), format), parse)`.
let private flattenApp (expr: SynExpr) : SynExpr * SynExpr list =
    let rec loop acc =
        function
        | SynExpr.App(funcExpr = f; argExpr = a) -> loop (a :: acc) f
        | head -> head, acc

    loop [] expr

type private WalkState =
    { File: string
      mutable LocalCalls: LocalBindingCall list
      mutable CommitRefs: CommitLocalReference list
      // The currently-active enclosing FormFieldKind shape ("Text",
      // "Number", "RangedNumber", or none). Pushed when we descend into the
      // args of a local-hosting `FormFieldKind` application; popped when we
      // leave.
      mutable EnclosingKindStack: string list }

let private currentEnclosingKind (s: WalkState) : string option =
    match s.EnclosingKindStack with
    | top :: _ -> Some top
    | [] -> None

let rec private walkExpr (state: WalkState) (expr: SynExpr) =
    // Detect `Action.CommitLocal "id"` references first — simple top-level
    // app shape.
    match expr with
    | SynExpr.App(funcExpr = ActionCommitLocal; argExpr = idArg) ->
        match firstArgStringLiteral idArg with
        | Some id ->
            state.CommitRefs <-
                { NodeIdLiteral = id
                  Location = mkLocation state.File (idArg.Range) }
                :: state.CommitRefs
        | None -> ()

        walkExpr state idArg
    | _ -> ()

    // Detect `binding.local <init> <flush> <commit> <format> <parse>`.
    let head, args = flattenApp expr

    match head with
    | BindingLocal ->
        let flushArg = args |> List.tryItem 1
        let formatArg = args |> List.tryItem 3

        let detail =
            { Location = mkLocation state.File (head.Range)
              FormatExplicitNone = formatArg |> Option.map isExplicitNoneExpr |> Option.defaultValue false
              FlushOnIsCommitAction = flushArg |> Option.map isOnCommitActionExpr |> Option.defaultValue false
              EnclosingKind = currentEnclosingKind state }

        state.LocalCalls <- detail :: state.LocalCalls
        // Continue walking inside the args (a binding.local arg can itself
        // contain nested expressions — e.g. `onCommit = (SetSalary >> Action.dispatch)`).
        args |> List.iter (walkExpr state)
    | _ ->
        // Push/pop the enclosing FormFieldKind shape when traversing
        // `FormFieldKind.Text(...)` / `FormFieldKind.Number(...)`.
        match head with
        | FormFieldKindCtor kindName ->
            state.EnclosingKindStack <- kindName :: state.EnclosingKindStack
            args |> List.iter (walkExpr state)

            state.EnclosingKindStack <-
                match state.EnclosingKindStack with
                | _ :: rest -> rest
                | [] -> []
        | _ ->
            // Standard recursive descent into every sub-expression.
            descend state expr

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

/// Walk one F# source file, returning the per-file Local-binding state.
let private walkFile
    (checker: FSharpChecker)
    (file: string)
    : Async<LocalBindingCall list * CommitLocalReference list> =
    async {
        let source = File.ReadAllText file
        let! parseResult = parseFile checker file source

        let state =
            { File = file
              LocalCalls = []
              CommitRefs = []
              EnclosingKindStack = [] }

        match parseResult.ParseTree with
        | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) -> modules |> List.iter (walkModule state)
        | ParsedInput.SigFile _ -> ()

        return state.LocalCalls |> List.rev, state.CommitRefs |> List.rev
    }

/// Public entry — walks the supplied source files and returns findings.
let checkSources (checker: FSharpChecker) (files: string list) : Async<Finding list> =
    async {
        let! perFile = files |> List.map (walkFile checker) |> Async.Parallel

        let allLocalCalls = perFile |> Array.collect (fst >> List.toArray) |> Array.toList

        let allCommitRefs = perFile |> Array.collect (snd >> List.toArray) |> Array.toList

        // FUARAN043: a Local binding with FlushOn = OnCommitAction in a
        // file where NO `Action.CommitLocal _` reference appears anywhere.
        // The "anywhere" scope is intentionally module-wide rather than
        // tree-local — accurate per-NodeId scoping needs the form's field
        // ids in scope, which the AST walker doesn't currently extract.
        let anyCommitLocalRef = not (List.isEmpty allCommitRefs)

        let findings =
            allLocalCalls
            |> List.collect (fun call ->
                let mutable acc = []

                if call.FormatExplicitNone then
                    acc <-
                        create
                            Error
                            "FUARAN042"
                            call.Location
                            "binding.local was given `format = None` — a Local-bound text/number input needs a typed display formatter to round-trip the buffer (the Salary-style canonical shape uses thousands-separator formatting). Pass `Some <formatter>` or use `binding.state` instead if no formatting is needed."
                        :: acc

                if call.FlushOnIsCommitAction && not anyCommitLocalRef then
                    acc <-
                        create
                            Warning
                            "FUARAN043"
                            call.Location
                            "binding.local was declared with FlushOn = OnCommitAction, but no `Action.CommitLocal _` reference was found in the project. The buffer will never drain — wire an inline button or external commit trigger that dispatches Action.CommitLocal with this field's id, or pick a different FlushOn (OnBlur for free text, OnSubmit for form-scoped commit)."
                        :: acc

                match call.EnclosingKind with
                | None ->
                    acc <-
                        create
                            Error
                            "FUARAN044"
                            call.Location
                            "binding.local was used outside an enclosing FormFieldKind.Text(...) / FormFieldKind.Number(...) / FormFieldKind.RangedNumber(...) constructor — the renderer's useState slot is only mounted for those three field kinds. Move the Local binding into a form-field Text/Number/RangedNumber value, or use binding.state / binding.query for read-side bindings."
                        :: acc
                | Some _ -> ()

                List.rev acc)

        return findings
    }
