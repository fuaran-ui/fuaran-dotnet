module Fuaran.UI.Validator.FragmentCheck

// ============================================================================
//  Fragment-reuse check.
//
//  Three defect codes — FUARAN056 / FUARAN057 / FUARAN058 — guard the
//  reusable-subtree primitive:
//
//   - FUARAN056 DuplicateFragmentName (Error): the same fragment name is
//     declared by two or more `Fuaran.fragmentDecl` invocations. Validator
//     scope is the project (not per-tree, since the AST walker cannot
//     reliably bound "what tree this decl belongs to" lexically). The
//     renderer's runtime resolver uses last-decl-wins which is the
//     structurally identical outcome for any valid tree; this rule flags
//     the colliding decls so authors disambiguate.
//
//   - FUARAN057 UnresolvedFragmentRef (Error): a `Fuaran.fragmentRef`'s
//     name literal doesn't match any `Fuaran.fragmentDecl` name literal
//     in the project. Scope is project-wide for the same reason. The
//     renderer's runtime resolver renders a labelled placeholder; this
//     rule surfaces the gap at build time so authors don't ship the
//     placeholder to production.
//
//   - FUARAN058 CyclicFragmentRef (Error): a `Fuaran.fragmentDecl`'s
//     `Body` transitively contains a `Fuaran.fragmentRef` back to the
//     decl's own name (directly or via intermediate decls). Cycles loop
//     infinitely at render time; the renderer's runtime cycle-guard
//     catches them with a placeholder, but the build-time rule is the
//     authoritative signal. Detection is graph-based (decl name → set
//     of ref names appearing inside its body's textual scope).
//
//  All three rules walk the untyped F# AST per the validator's scope —
//  no typed-checker dependency. The decl/ref names are extracted from
//  string literals at the AST call sites; non-literal forms (e.g.
//  `Fuaran.fragmentRef id (computedName ())`) leave the slot None and
//  the corresponding rule silently skips that call site.
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

let private constStringValue (c: SynConst) =
    match c with
    | SynConst.String(text = s) -> Some s
    | _ -> None

let private literalStringExpr (expr: SynExpr) : string option =
    let rec inner (e: SynExpr) =
        match e with
        | SynExpr.Const(constant = c) -> constStringValue c
        | SynExpr.Paren(expr = e') -> inner e'
        | SynExpr.Typed(expr = e') -> inner e'
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

let private (|FuaranCall|_|) (expectedLeaf: string) (expr: SynExpr) : unit option =
    match leafIdent expr with
    | Some(prefix, leaf) when leaf = expectedLeaf && not prefix.IsEmpty && List.last prefix = "Fuaran" -> Some()
    | _ -> None

let private flattenApp (expr: SynExpr) : SynExpr * SynExpr list =
    let rec loop acc =
        function
        | SynExpr.App(funcExpr = f; argExpr = a) -> loop (a :: acc) f
        | head -> head, acc

    loop [] expr

/// Extract the literal `name` from a `FragmentId "name"` expression in a
/// record body (the `Name = FragmentId "..."` shape). Returns `None` for
/// any non-literal shape (computed name, bound variable, etc.).
let private fragmentIdLiteral (expr: SynExpr) : string option =
    let rec inner (e: SynExpr) =
        match e with
        | SynExpr.Paren(expr = e') -> inner e'
        | SynExpr.Typed(expr = e') -> inner e'
        | SynExpr.App(funcExpr = head; argExpr = arg) ->
            match leafIdent head with
            | Some(_, "FragmentId") -> literalStringExpr arg
            | _ -> None
        | _ -> None

    inner expr

// ─── Parameterised-fragment hole parsing (Phase 180) ──────────────────────
//
// The build-time counterpart of the runtime `HoleDecl.isTotal` /
// `HoleValueSpace.validate` predicates. Two decl-derivable defects are lifted
// to the AST walker; the remaining checks have no build-time authoring surface
// (arg binding + effect are typed/runtime concerns the untyped AST cannot reach)
// and ship as runtime equivalents that are tested:
//   - unbound required hole + slot kind-constraint → `FragmentApply.apply`
//     (renderer-side, at bind time);
//   - effect understatement → `FunctionTool.auditFragmentEffect` (a decl-level
//     `EffectClass.covers` audit over the body's observed effect — arg-independent,
//     so it lives with the fragment→artifact-function projection, not the apply path).
//
//   - FUARAN059 NonTotalRepeatHole (Error): a `HoleDecl.Repeat` whose count
//     value-space is not a bounded `HoleValueSpace.IntRange` — totality breach
//     (invariant 1). An unbounded repeat count diverges at apply time.
//   - FUARAN065 HoleDefaultOutOfRange (Error): a `HoleDecl.Value`'s literal
//     default falls outside its declared value-space — the value-space-mismatch
//     check applied to the decl's own default (a guaranteed apply-time failure).
//
// Both parse only LITERAL spaces / defaults; a computed space or default leaves
// the slot unknown and the rule silently skips it (same posture as the
// name-literal rules above).

type private SpaceInfo =
    | IntRangeSpace of int * int
    | FloatRangeSpace of float * float
    | StringLenSpace of int * int
    | EnumSpace of string list
    | AnyStringSpace
    | UnknownSpace

type private DefaultLit =
    | IntLit of int
    | FloatLit of float
    | StrLit of string
    | BoolLit of bool
    | UnknownLit

type private HoleInfo =
    { Case: string // "Value" | "Slot" | "Repeat"
      Name: string option
      Space: SpaceInfo option
      Default: DefaultLit option // None ⇒ no default given; Some ⇒ a default present
      Location: Location }

/// Match `HoleDecl.<case>(<args…>)` and return the case leaf + the flattened
/// tuple argument expressions. `HoleDecl.Slot(n, c)` parses as one paren-tuple
/// argument; we unwrap it to the element list.
let private (|HoleDeclCase|_|) (expr: SynExpr) : (string * SynExpr list) option =
    let head, args = flattenApp expr

    match leafIdent head, args with
    | Some(prefix, leaf), [ singleArg ] when
        (leaf = "Value" || leaf = "Slot" || leaf = "Repeat")
        && not prefix.IsEmpty
        && List.last prefix = "HoleDecl"
        ->
        let rec elems (e: SynExpr) =
            match e with
            | SynExpr.Paren(expr = e') -> elems e'
            | SynExpr.Typed(expr = e') -> elems e'
            | SynExpr.Tuple(exprs = es) -> es
            | other -> [ other ]

        Some(leaf, elems singleArg)
    | _ -> None

let private literalIntExpr (expr: SynExpr) : int option =
    let rec inner (e: SynExpr) =
        match e with
        | SynExpr.Const(constant = SynConst.Int32 n) -> Some n
        | SynExpr.Paren(expr = e') -> inner e'
        | SynExpr.Typed(expr = e') -> inner e'
        | _ -> None

    inner expr

let private literalFloatExpr (expr: SynExpr) : float option =
    let rec inner (e: SynExpr) =
        match e with
        | SynExpr.Const(constant = SynConst.Double f) -> Some f
        | SynExpr.Const(constant = SynConst.Int32 n) -> Some(float n)
        | SynExpr.Paren(expr = e') -> inner e'
        | SynExpr.Typed(expr = e') -> inner e'
        | _ -> None

    inner expr

/// Parse a `HoleValueSpace.<case>(…)` expression into a `SpaceInfo`. Non-literal
/// bounds collapse to `UnknownSpace` (the rule then skips).
let private parseSpace (expr: SynExpr) : SpaceInfo =
    let head, args = flattenApp expr

    match leafIdent head with
    | Some(prefix, leaf) when not prefix.IsEmpty && List.last prefix = "HoleValueSpace" ->
        let elems =
            match args with
            | [ single ] ->
                let rec go (e: SynExpr) =
                    match e with
                    | SynExpr.Paren(expr = e') -> go e'
                    | SynExpr.Typed(expr = e') -> go e'
                    | SynExpr.Tuple(exprs = es) -> es
                    | other -> [ other ]

                go single
            | many -> many

        match leaf, elems with
        | "IntRange", [ a; b ] ->
            match literalIntExpr a, literalIntExpr b with
            | Some lo, Some hi -> IntRangeSpace(lo, hi)
            | _ -> UnknownSpace
        | "FloatRange", [ a; b ] ->
            match literalFloatExpr a, literalFloatExpr b with
            | Some lo, Some hi -> FloatRangeSpace(lo, hi)
            | _ -> UnknownSpace
        | "StringLen", [ a; b ] ->
            match literalIntExpr a, literalIntExpr b with
            | Some lo, Some hi -> StringLenSpace(lo, hi)
            | _ -> UnknownSpace
        | "Enum", [ listExpr ] ->
            let rec listElems (e: SynExpr) =
                match e with
                | SynExpr.Paren(expr = e') -> listElems e'
                | SynExpr.Typed(expr = e') -> listElems e'
                | SynExpr.ArrayOrList(exprs = es) -> Some es
                | SynExpr.ArrayOrListComputed(expr = inner) ->
                    match inner with
                    | SynExpr.Sequential _ -> None // non-trivial computed list — skip
                    | _ -> None
                | _ -> None

            match listElems listExpr with
            | Some es ->
                let lits = es |> List.choose literalStringExpr

                if lits.Length = es.Length then
                    EnumSpace lits
                else
                    UnknownSpace
            | None -> UnknownSpace
        | "AnyString", _ -> AnyStringSpace
        | _ -> UnknownSpace
    | _ -> UnknownSpace

/// Parse the `defaultValue` element of a `HoleDecl.Value` tuple — `Some(box …)`
/// or `None`. Returns `None` for no-default; `Some UnknownLit` when a default is
/// present but its literal can't be read.
let private parseDefault (expr: SynExpr) : DefaultLit option =
    let rec unwrap (e: SynExpr) =
        match e with
        | SynExpr.Paren(expr = e') -> unwrap e'
        | SynExpr.Typed(expr = e') -> unwrap e'
        | _ -> e

    match unwrap expr with
    | SynExpr.Ident i when i.idText = "None" -> None
    | SynExpr.App(funcExpr = head; argExpr = arg) ->
        match leafIdent head with
        | Some(_, "Some") ->
            // Dig through an optional `box`.
            let rec lit (e: SynExpr) =
                match unwrap e with
                | SynExpr.App(funcExpr = h; argExpr = a) ->
                    match leafIdent h with
                    | Some(_, "box") -> lit a
                    | _ -> UnknownLit
                | SynExpr.Const(constant = SynConst.Int32 n) -> IntLit n
                | SynExpr.Const(constant = SynConst.Double f) -> FloatLit f
                | SynExpr.Const(constant = SynConst.String(text = s)) -> StrLit s
                | SynExpr.Const(constant = SynConst.Bool b) -> BoolLit b
                | _ -> UnknownLit

            Some(lit arg)
        | _ -> Some UnknownLit
    | _ -> Some UnknownLit

let private parseHole (file: string) (expr: SynExpr) : HoleInfo option =
    match expr with
    | HoleDeclCase(leaf, elems) ->
        let loc = mkLocation file (flattenApp expr |> fst).Range

        match leaf, elems with
        | "Value", (nameE :: spaceE :: rest) ->
            { Case = "Value"
              Name = literalStringExpr nameE
              Space = Some(parseSpace spaceE)
              Default =
                (match rest with
                 | defE :: _ -> parseDefault defE
                 | [] -> None)
              Location = loc }
            |> Some
        | "Slot", (nameE :: _) ->
            { Case = "Slot"
              Name = literalStringExpr nameE
              Space = None
              Default = None
              Location = loc }
            |> Some
        | "Repeat", (nameE :: spaceE :: _) ->
            { Case = "Repeat"
              Name = literalStringExpr nameE
              Space = Some(parseSpace spaceE)
              Default = None
              Location = loc }
            |> Some
        | _ -> None
    | _ -> None

/// One observed `Fuaran.fragmentDecl` invocation, captured with the decl's
/// declared name + body expression (for nested-ref scanning) + parsed holes.
type private DeclCall =
    { Name: string option
      DeclLocation: Location
      BodyExpr: SynExpr option
      Holes: HoleInfo list }

/// One observed `Fuaran.fragmentRef` invocation, captured with the ref's
/// referenced name string.
type private RefCall =
    { Name: string option
      RefLocation: Location }

type private WalkState =
    { File: string
      mutable Decls: DeclCall list
      mutable Refs: RefCall list }

let private extractDeclSpec (file: string) (specExpr: SynExpr) : string option * SynExpr option * HoleInfo list =
    // Recognise `{ Name = FragmentId "..."; Body = <expr>; Holes = [...]; ... }`
    // record shape. Returns (literal-name, body-expr, parsed-holes). Authors
    // using `{ Defaults.fragmentDecl with Name = ... }` are handled — record
    // copy syntax `{ original with Field = value; ... }` parses as
    // `SynExpr.Record` with `copyInfo = Some`.
    let rec inner (e: SynExpr) =
        match e with
        | SynExpr.Paren(expr = e') -> inner e'
        | SynExpr.Typed(expr = e') -> inner e'
        | SynExpr.Record(recordFields = fields) ->
            let mutable nameLit: string option = None
            let mutable bodyExpr: SynExpr option = None
            let mutable holes: HoleInfo list = []

            for SynExprRecordField(fieldName = (SynLongIdent(id = ids), _); expr = valueExpr) in fields do
                let leaf = if List.isEmpty ids then "" else (List.last ids).idText

                match leaf, valueExpr with
                | "Name", Some v -> nameLit <- fragmentIdLiteral v
                | "Body", Some v -> bodyExpr <- Some v
                | "Holes", Some v ->
                    let rec listElems (le: SynExpr) =
                        match le with
                        | SynExpr.Paren(expr = e') -> listElems e'
                        | SynExpr.Typed(expr = e') -> listElems e'
                        | SynExpr.ArrayOrList(exprs = es) -> es
                        | SynExpr.ArrayOrListComputed(expr = inner') ->
                            let rec flat acc =
                                function
                                | SynExpr.Sequential(expr1 = a; expr2 = b) -> flat (a :: acc) b
                                | last -> List.rev (last :: acc)

                            flat [] inner'
                        | _ -> []

                    holes <- listElems v |> List.choose (parseHole file)
                | _ -> ()

            nameLit, bodyExpr, holes
        | _ -> None, None, []

    inner specExpr

let rec private walkExpr (state: WalkState) (expr: SynExpr) =
    let head, args = flattenApp expr

    match head, args with
    | FuaranCall "fragmentDecl", [ _idArg; specArg ] ->
        let nameLit, bodyExpr, holes = extractDeclSpec state.File specArg

        state.Decls <-
            { Name = nameLit
              DeclLocation = mkLocation state.File head.Range
              BodyExpr = bodyExpr
              Holes = holes }
            :: state.Decls

        bodyExpr |> Option.iter (walkExpr state)
    | FuaranCall "fragmentRef", [ _idArg; nameArg ] ->
        let nameLit = literalStringExpr nameArg

        state.Refs <-
            { Name = nameLit
              RefLocation = mkLocation state.File head.Range }
            :: state.Refs
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

let private walkFile (checker: FSharpChecker) (file: string) : Async<DeclCall list * RefCall list> =
    async {
        let source = File.ReadAllText file
        let! parseResult = parseFile checker file source

        let state = { File = file; Decls = []; Refs = [] }

        match parseResult.ParseTree with
        | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) -> modules |> List.iter (walkModule state)
        | ParsedInput.SigFile _ -> ()

        return state.Decls |> List.rev, state.Refs |> List.rev
    }

/// Scan an arbitrary expression for the names referenced by enclosed
/// `Fuaran.fragmentRef "..." "name"` calls. Used by FUARAN058's cycle
/// detection to build the "decl name → set of ref names" graph from each
/// decl's body without re-walking the whole file.
let private refNamesInExpr (root: SynExpr) : Set<string> =
    let mutable acc = Set.empty

    let rec walk (expr: SynExpr) =
        let head, args = flattenApp expr

        match head, args with
        | FuaranCall "fragmentRef", [ _; nameArg ] ->
            match literalStringExpr nameArg with
            | Some n -> acc <- Set.add n acc
            | None -> ()
        | _ ->
            match expr with
            | SynExpr.App(funcExpr = f; argExpr = a) ->
                walk f
                walk a
            | SynExpr.Paren(expr = e) -> walk e
            | SynExpr.Tuple(exprs = es) -> es |> List.iter walk
            | SynExpr.Record(recordFields = fields) ->
                for SynExprRecordField(expr = fieldExpr) in fields do
                    fieldExpr |> Option.iter walk
            | SynExpr.LetOrUse synLet ->
                for SynBinding(expr = e) in synLet.Bindings do
                    walk e

                walk synLet.Body
            | SynExpr.Sequential(expr1 = a; expr2 = b) ->
                walk a
                walk b
            | SynExpr.IfThenElse(ifExpr = c; thenExpr = t; elseExpr = e) ->
                walk c
                walk t
                e |> Option.iter walk
            | SynExpr.Match(expr = scrut; clauses = clauses) ->
                walk scrut

                for SynMatchClause(resultExpr = r) in clauses do
                    walk r
            | SynExpr.Lambda(body = b) -> walk b
            | SynExpr.ArrayOrList(exprs = es) -> es |> List.iter walk
            | SynExpr.ArrayOrListComputed(expr = e) -> walk e
            | SynExpr.ComputationExpr(expr = e) -> walk e
            | SynExpr.TypeApp(expr = e) -> walk e
            | SynExpr.Typed(expr = e) -> walk e
            | SynExpr.Do(expr = e) -> walk e
            | SynExpr.DotGet(expr = e) -> walk e
            | _ -> ()

    walk root
    acc

/// Detect a cycle starting from `startName` over the supplied adjacency map.
/// Returns true iff a path leads back to `startName`.
let private hasCycle (adjacency: Map<string, Set<string>>) (startName: string) : bool =
    let rec walk (visited: Set<string>) (current: string) =
        match Map.tryFind current adjacency with
        | None -> false
        | Some targets ->
            targets
            |> Set.exists (fun next ->
                if next = startName then true
                elif Set.contains next visited then false
                else walk (Set.add next visited) next)

    walk Set.empty startName

/// Public entry — walks the supplied source files and returns findings.
let checkSources (checker: FSharpChecker) (files: string list) : Async<Finding list> =
    async {
        let! perFile = files |> List.map (walkFile checker) |> Async.Parallel

        let allDecls = perFile |> Array.collect (fst >> List.toArray) |> Array.toList

        let allRefs = perFile |> Array.collect (snd >> List.toArray) |> Array.toList

        let declNames =
            allDecls
            |> List.choose (fun d -> d.Name |> Option.map (fun n -> n, d))
            |> List.groupBy fst

        // FUARAN056: duplicate fragment names.
        let duplicateFindings =
            declNames
            |> List.collect (fun (name, occurrences) ->
                match occurrences with
                | _ :: _ :: _ ->
                    occurrences
                    |> List.map (fun (_, decl) ->
                        create
                            Error
                            "FUARAN056"
                            decl.DeclLocation
                            (sprintf
                                "Fragment name '%s' is declared %d times in this project. Fragment names must be unique per project — the renderer's runtime resolver picks one decl per name, the other(s) become unreachable. Rename the colliding decls or merge their bodies."
                                name
                                occurrences.Length))
                | _ -> [])

        let knownNames = declNames |> List.map fst |> Set.ofList

        // FUARAN057: unresolved references.
        let unresolvedFindings =
            allRefs
            |> List.choose (fun r ->
                match r.Name with
                | Some name when not (Set.contains name knownNames) ->
                    Some(
                        create
                            Error
                            "FUARAN057"
                            r.RefLocation
                            (sprintf
                                "Fragment reference '%s' has no matching Fuaran.fragmentDecl in this project. The renderer will substitute a labelled placeholder at runtime. Declare a `Fuaran.fragmentDecl _ { Name = FragmentId \"%s\"; Body = ... }` somewhere in the tree, or fix the typo on this reference."
                                name
                                name)
                    )
                | _ -> None)

        // FUARAN058: cyclic fragment references. Build the directed graph
        // "decl name → set of fragment names referenced inside its body",
        // then run a per-name reachability search back to itself.
        let adjacency: Map<string, Set<string>> =
            allDecls
            |> List.choose (fun d ->
                match d.Name, d.BodyExpr with
                | Some name, Some body -> Some(name, refNamesInExpr body)
                | _ -> None)
            // Multiple decls sharing the same name (already a FUARAN056
            // defect) collapse their out-edges via Set.union — keep the
            // graph defensive.
            |> List.fold
                (fun acc (name, refs) ->
                    let merged =
                        match Map.tryFind name acc with
                        | Some existing -> Set.union existing refs
                        | None -> refs

                    Map.add name merged acc)
                Map.empty

        let cyclicFindings =
            allDecls
            |> List.choose (fun d ->
                match d.Name with
                | Some name when hasCycle adjacency name ->
                    Some(
                        create
                            Error
                            "FUARAN058"
                            d.DeclLocation
                            (sprintf
                                "Fragment '%s' transitively references itself via Fuaran.fragmentRef. The renderer's runtime cycle-guard renders a labelled placeholder rather than recursing forever, but this defect should be fixed at the source — break the cycle by inlining one of the bodies or restructuring the reuse pattern."
                                name)
                    )
                | _ -> None)

        // FUARAN059 / FUARAN065: parameterised-hole defects (Phase 180), lifted
        // from the runtime `HoleDecl.isTotal` / `HoleValueSpace.validate`
        // predicates. Decl-derivable only — see the parsing-section note.
        let allHoles = allDecls |> List.collect _.Holes

        let totalityFindings =
            allHoles
            |> List.choose (fun h ->
                match h.Case, h.Space with
                | "Repeat", Some(IntRangeSpace _) -> None
                | "Repeat", Some UnknownSpace -> None // computed count-space — skip
                | "Repeat", Some _ ->
                    Some(
                        create
                            Error
                            "FUARAN059"
                            h.Location
                            (sprintf
                                "Repeat hole '%s' has an unbounded count value-space. A repeat/iteration count must be a bounded HoleValueSpace.IntRange (totality, invariant 1) — an unbounded count diverges at apply time. Change the count-space to IntRange(min, max)."
                                (defaultArg h.Name "<unnamed>"))
                    )
                | _ -> None)

        let defaultViolation (space: SpaceInfo) (def: DefaultLit) : string option =
            match space, def with
            | IntRangeSpace(lo, hi), IntLit n ->
                if n >= lo && n <= hi then
                    None
                else
                    Some(sprintf "value %d outside [%d, %d]" n lo hi)
            | FloatRangeSpace(lo, hi), FloatLit f ->
                if f >= lo && f <= hi then
                    None
                else
                    Some(sprintf "value %g outside [%g, %g]" f lo hi)
            | StringLenSpace(lo, hi), StrLit s ->
                if s.Length >= lo && s.Length <= hi then
                    None
                else
                    Some(sprintf "string length %d outside [%d, %d]" s.Length lo hi)
            | EnumSpace choices, StrLit s ->
                if List.contains s choices then
                    None
                else
                    Some(sprintf "'%s' not in {%s}" s (String.concat ", " choices))
            | AnyStringSpace, StrLit _ -> None
            // Default literal kind disagrees with the value-space domain.
            | IntRangeSpace _, _ -> Some "default is not an int matching the IntRange value-space"
            | FloatRangeSpace _, _ -> Some "default is not a float matching the FloatRange value-space"
            | (StringLenSpace _ | EnumSpace _ | AnyStringSpace), _ ->
                Some "default is not a string matching the value-space"
            | UnknownSpace, _ -> None // computed space — skip

        let defaultRangeFindings =
            allHoles
            |> List.choose (fun h ->
                match h.Case, h.Space, h.Default with
                | "Value", Some space, Some def when def <> UnknownLit ->
                    match defaultViolation space def with
                    | Some why ->
                        Some(
                            create
                                Error
                                "FUARAN065"
                                h.Location
                                (sprintf
                                    "Value hole '%s' has a default that violates its value-space: %s. The default is validated against the hole's space at apply time, so this binding can never succeed. Fix the default or widen the value-space."
                                    (defaultArg h.Name "<unnamed>")
                                    why)
                        )
                    | None -> None
                | _ -> None)

        return
            duplicateFindings
            @ unresolvedFindings
            @ cyclicFindings
            @ totalityFindings
            @ defaultRangeFindings
    }
