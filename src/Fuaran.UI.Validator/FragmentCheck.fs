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


open FSharp.Compiler.Syntax
open Fuaran.UI.Validator.Findings
open Fuaran.UI.Validator.Syntax

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
    | Some(prefix, leaf, _), [ singleArg ] when
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
    | Some(prefix, leaf, _) when not prefix.IsEmpty && List.last prefix = "HoleValueSpace" ->
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
                let lits = es |> List.choose literalString

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
    match unwrap expr with
    | SynExpr.Ident i when i.idText = "None" -> None
    | SynExpr.App(funcExpr = head; argExpr = arg) ->
        match leafIdent head with
        | Some(_, "Some", _) ->
            // The default is a `Scalar`: dig through its case constructor.
            let rec lit (e: SynExpr) =
                match unwrap e with
                | SynExpr.App(funcExpr = h; argExpr = a) ->
                    match leafIdent h with
                    | Some(prefix, ("Int" | "Float" | "Bool" | "Str"), _) when
                        not prefix.IsEmpty && List.last prefix = "Scalar"
                        ->
                        lit a
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
              Name = literalString nameE
              Space = Some(parseSpace spaceE)
              Default =
                (match rest with
                 | defE :: _ -> parseDefault defE
                 | [] -> None)
              Location = loc }
            |> Some
        | "Slot", (nameE :: _) ->
            { Case = "Slot"
              Name = literalString nameE
              Space = None
              Default = None
              Location = loc }
            |> Some
        | "Repeat", (nameE :: spaceE :: _) ->
            { Case = "Repeat"
              Name = literalString nameE
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

let private extractDeclSpec (file: string) (specExpr: SynExpr) : string option * SynExpr option * HoleInfo list =
    // Recognise `{ Name = "..."; Body = <expr>; Holes = [...]; ... }`
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
                | "Name", Some v -> nameLit <- literalString v
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


/// `Fuaran.fragmentDecl "id" spec` sites (walking on into the decl's body) and
/// `Fuaran.fragmentRef "id" "name"` sites.
let private scan (sources: ParsedSource list) : DeclCall list * RefCall list =
    let decls = ResizeArray<DeclCall>()
    let refs = ResizeArray<RefCall>()

    for source in sources do
        let rec visit (expr: SynExpr) =
            let head, args = flattenApp expr

            match args with
            | [ _; specArg ] when isQualified "Fuaran" "fragmentDecl" head ->
                let nameLit, bodyExpr, holes = extractDeclSpec source.File specArg

                decls.Add
                    { Name = nameLit
                      DeclLocation = mkLocation source.File head.Range
                      BodyExpr = bodyExpr
                      Holes = holes }

                bodyExpr |> Option.iter (iterExpr visit)
                false
            | [ _; nameArg ] when isQualified "Fuaran" "fragmentRef" head ->
                refs.Add
                    { Name = literalString nameArg
                      RefLocation = mkLocation source.File head.Range }

                false
            | _ -> true

        for expr in topLevelExprs source do
            iterExpr visit expr

    List.ofSeq decls, List.ofSeq refs

/// The fragment names referenced anywhere inside a decl body.
let private refNamesInExpr (root: SynExpr) : Set<string> =
    let mutable acc = Set.empty

    root
    |> iterExpr (fun expr ->
        match flattenApp expr with
        | head, [ _; nameArg ] when isQualified "Fuaran" "fragmentRef" head ->
            literalString nameArg |> Option.iter (fun n -> acc <- Set.add n acc)
            false
        | _ -> true)

    acc

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

let check (sources: ParsedSource list) : Finding list =
    let allDecls, allRefs = scan sources

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
                            "Fragment reference '%s' has no matching Fuaran.fragmentDecl in this project. The renderer will substitute a labelled placeholder at runtime. Declare a `Fuaran.fragmentDecl _ { Name = \"%s\"; Body = ... }` somewhere in the tree, or fix the typo on this reference."
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

    duplicateFindings
    @ unresolvedFindings
    @ cyclicFindings
    @ totalityFindings
    @ defaultRangeFindings
