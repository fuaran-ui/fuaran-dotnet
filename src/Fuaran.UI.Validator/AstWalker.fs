module Fuaran.UI.Validator.AstWalker

// ============================================================================
//  The `Fuaran.X` call-site model.
//
//  Extracts every `Fuaran.<ctor>` smart-constructor call site from the parsed
//  sources (`Syntax.ParsedSource`) as a `FuaranCall` — its NodeId literal, the
//  tree it belongs to, the query / dispatch references in its own arguments,
//  and the per-kind field snapshots the downstream checks (NodeIdCheck,
//  BindingResolution, MsgPayloadCheck, RowTypeCheck, ButtonDisabledCheck,
//  ScalarRangeCheck, LinkCheck) reason against.
//
//  Why untyped, not typed:
//   - Anti-pattern: "Don't try to validate AGAINST the host's full type
//     universe". Untyped AST is enough for NodeId / Binding.Query name literals
//     and Action.Dispatch case-name textual match; typed AST opens the door to
//     full type-checker complexity — out of scope.
//   - The manifest IS the typed contract. The validator gates against the
//     manifest, not the host's full type universe.
//
//  Which identifiers are smart constructors, and which of them root a tree, is
//  DERIVED from the `Fuaran` module by reflection (`SmartCtors`) — never
//  hand-listed. A hand list fell behind the module twice: it carried retired
//  constructors and missed most current ones, and its tree-root set named only
//  `dashboard`, so a tree rooted at `Fuaran.box` was never checked at all.
// ============================================================================

open System
open System.Reflection
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open FSharp.Reflection
open Fuaran.UI
open Fuaran.UI.Validator.Findings
open Fuaran.UI.Validator.Syntax

/// The smart-constructor surface of the `Fuaran` module, read off the module
/// itself.
module SmartCtors =

    let private nodeDefinition = typedefof<Generated.Node<obj>>

    let private isNode (t: Type) =
        t.IsGenericType && t.GetGenericTypeDefinition() = nodeDefinition

    /// Does a value of `t` hold a `Node` as a child? Through generic arguments
    /// (lists, options, sequences, tuples) and record fields, but not through
    /// functions — a function that BUILDS a node (a cell template) is not a
    /// child — nor through unions, whose cases are payload shapes, not slots.
    let rec private holdsNode (seen: Set<string>) (depth: int) (t: Type) : bool =
        if isNode t then
            true
        elif depth = 0 || FSharpType.IsFunction t then
            false
        else
            let key = t.FullName |> Option.ofObj |> Option.defaultValue t.Name

            if seen.Contains key then
                false
            else
                let seen = seen.Add key

                let viaArgs =
                    t.IsGenericType
                    && t.GetGenericArguments() |> Array.exists (holdsNode seen (depth - 1))

                let viaFields =
                    FSharpType.IsRecord(t, BindingFlags.Public ||| BindingFlags.NonPublic)
                    && FSharpType.GetRecordFields(t, BindingFlags.Public ||| BindingFlags.NonPublic)
                       |> Array.exists (fun p -> holdsNode seen (depth - 1) p.PropertyType)

                viaArgs || viaFields

    let private fuaranModule: Type =
        // `Fuaran.box` is a member of the module by construction; reading the
        // module off it avoids restating its compiled name.
        let rec declaring (e: Quotations.Expr) =
            match e with
            | Quotations.Patterns.Call(_, mi, _) -> Option.ofObj mi.DeclaringType
            | Quotations.Patterns.Lambda(_, body) -> declaring body
            | Quotations.Patterns.Let(_, _, body) -> declaring body
            | _ -> None

        match declaring <@ Fuaran.box "" Unchecked.defaultof<Generated.BoxSpec<obj>> @> with
        | Some t -> t
        | None -> failwith "Fuaran.UI.Validator: cannot locate the Fuaran module by reflection"

    let private constructors =
        fuaranModule.GetMethods(BindingFlags.Public ||| BindingFlags.Static)
        |> Array.filter (fun m -> isNode m.ReturnType)

    /// Every public function of the `Fuaran` module that returns a `Node`.
    let names: Set<string> = constructors |> Array.map _.Name |> Set.ofArray

    /// The constructors that take a child `Node` — the ones that can hold a
    /// tree under them. An outermost call to one of these roots a tree.
    let containers: Set<string> =
        constructors
        |> Array.filter (fun m ->
            m.GetParameters()
            |> Array.exists (fun p -> holdsNode Set.empty 3 p.ParameterType))
        |> Array.map _.Name
        |> Set.ofArray

/// One Fuaran.X smart-ctor invocation site.
type FuaranCall =
    {
        /// Smart-ctor name, e.g. `"box"`, `"metric"`.
        Ctor: string
        /// First positional argument's string literal — the NodeId.
        NodeIdLiteral: string option
        /// NodeId of the tree root this call belongs to — the human-readable
        /// tree NAME reported in findings. It is NOT an identity: see `TreeRootKey`.
        TreeRoot: string option
        /// Identity of the tree-root CALL SITE — unique per root invocation
        /// across the whole walk. Two `Fuaran.box "doc"` invocations are two
        /// DIFFERENT trees that happen to share a root id (a template and its
        /// stand-in, two fixtures), so per-tree checks group on this and report
        /// `TreeRoot`.
        TreeRootKey: string option
        /// Source location of the smart-ctor identifier.
        Location: Location
        /// `binding.query "name" ...` references in this call's OWN arguments —
        /// a nested Fuaran.X call's references belong to that call, not to its
        /// ancestors.
        QueryReferences: QueryReference list
        /// `Action.Dispatch (Case ...)` / `Action.dispatch (Case ...)` references
        /// in this call's own arguments.
        DispatchReferences: DispatchReference list
        /// `Ctor = "grid"` only: the source query and the row type.
        GridDetail: GridDetail option
        /// `Ctor = "button"` only.
        ButtonDetail: ButtonDetail option
        /// `Ctor = "progress"` only.
        ProgressDetail: ProgressDetail option
        /// `Ctor = "link"` / `"linkSpec"` only.
        LinkDetail: LinkDetail option
    }

and QueryReference = { Name: string; Location: Location }

and DispatchReference =
    { CaseName: string; Location: Location }

and RowAnnotation =
    { Annotation: string
      Location: Location }

and GridDetail =
    {
        /// Query name of the grid's `Source = binding.query "name" ...`.
        /// `None` for a non-query source (Static, State, Selection, ...).
        SourceQueryName: string option
        /// The row type annotated on the `toRow` projection's parameter
        /// (`Fuaran.grid "id" (fun (r: SaleRow) -> ...) spec`) — the one place a
        /// grid names its source row type: every other closure of the spec takes
        /// the projected `Row`. Empty when the author elides the annotation.
        RowAnnotations: RowAnnotation list
    }

/// `Fuaran.button` snapshot for ButtonDisabledCheck (FUARAN064).
and ButtonDetail =
    {
        /// `Disabled = Some (Binding.Static (Some false))` — a constant-false
        /// disabled binding, which never disables the button.
        DisabledBoundToStaticFalse: bool
    }

/// `Fuaran.progress` snapshot: the `Fraction` field's static literal, if any.
and ProgressDetail = { FractionLiteral: float option }

/// `Fuaran.link` / `Fuaran.linkSpec` snapshot: the href's static literal —
/// the positional `Fuaran.link "id" "href" "label"` 2nd argument, or
/// `Href = Binding.Static (Some "...")` in the record form.
and LinkDetail = { HrefLiteral: string option }

/// `Fuaran.<ctor>` — a derived smart-ctor name under a `Fuaran` qualifier. A
/// bare `dashboard` is not recognised: smart ctors are always qualified.
let private (|FuaranSmartCtor|_|) (expr: SynExpr) =
    match leafIdent expr with
    | Some(prefix, leaf, r) when
        SmartCtors.names.Contains leaf
        && not prefix.IsEmpty
        && List.last prefix = "Fuaran"
        ->
        Some(leaf, r)
    | _ -> None

let private (|BindingQuery|_|) (expr: SynExpr) =
    match leafIdent expr with
    | Some(prefix, "query", r) when not prefix.IsEmpty && List.last prefix = "binding" -> Some r
    | _ -> None

/// `Action.dispatch` (smart ctor) or `Action.Dispatch` (DU case).
let private (|ActionDispatch|_|) (expr: SynExpr) =
    match leafIdent expr with
    | Some(prefix, ("dispatch" | "Dispatch"), r) when not prefix.IsEmpty && List.last prefix = "Action" -> Some r
    | _ -> None

/// The leading constructor name of a dispatch payload (`SelectRow 5` ->
/// `SelectRow`). Anything more complex returns None — we don't infer.
let rec private payloadCaseName (expr: SynExpr) : string option =
    match expr with
    | SynExpr.App(funcExpr = f) -> payloadCaseName f
    | SynExpr.Paren(expr = inner) -> payloadCaseName inner
    | SynExpr.Ident i -> Some i.idText
    | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) when not ids.IsEmpty -> Some (List.last ids).idText
    | _ -> None

/// Is `expr` an application of a recognised smart constructor?
let private isSmartCtorCall (expr: SynExpr) =
    match flattenApp expr with
    | FuaranSmartCtor _, _ :: _ -> true
    | _ -> false

/// Query / dispatch references in one call's own arguments. The walk STOPS at
/// a nested smart-ctor call: that call is recorded with its own references, so
/// descending would file each reference once per enclosing call — the defect
/// that reported one unresolved query once per nesting depth.
let private collectReferences (file: string) (args: SynExpr list) =
    let queries = ResizeArray<QueryReference>()
    let dispatches = ResizeArray<DispatchReference>()

    let rec visit (expr: SynExpr) =
        if isSmartCtorCall expr then
            false
        else
            match flattenApp expr with
            | BindingQuery r, nameArg :: rest ->
                literalString nameArg
                |> Option.iter (fun name ->
                    queries.Add
                        { Name = name
                          Location = mkLocation file r })

                rest |> List.iter (iterExpr visit)
                false
            | ActionDispatch r, payload :: _ ->
                payloadCaseName payload
                |> Option.iter (fun case ->
                    dispatches.Add
                        { CaseName = case
                          Location = mkLocation file r })

                true
            | _ -> true

    args |> List.iter (iterExpr visit)
    List.ofSeq queries, List.ofSeq dispatches

/// The type name of a parameter annotation's head identifier.
let rec private synTypeName (t: SynType) : string option =
    match t with
    | SynType.LongIdent(longDotId = SynLongIdent(id = ids)) when not ids.IsEmpty -> Some (List.last ids).idText
    | SynType.App(typeName = inner) -> synTypeName inner
    | SynType.Paren(innerType = inner) -> synTypeName inner
    | SynType.Var(typar = SynTypar(ident = i)) -> Some i.idText
    | _ -> None

/// The annotated type of a lambda's first parameter: `fun (r: SaleRow) -> ...`.
let private lambdaParamAnnotation (file: string) (expr: SynExpr) : RowAnnotation option =
    match unwrap expr with
    | SynExpr.Lambda(args = SynSimplePats.SimplePats(pats = SynSimplePat.Typed(SynSimplePat.Id(ident = i), t, _) :: _)) ->
        synTypeName t
        |> Option.map (fun name ->
            { Annotation = name
              Location = mkLocation file i.idRange })
    | _ -> None

let private buildGridDetail (file: string) (toRow: SynExpr) (spec: SynExpr) : GridDetail =
    let sourceQueryName =
        fieldValue "Source" spec
        |> Option.bind (fun source ->
            match flattenApp (unwrap source) with
            | BindingQuery _, nameArg :: _ -> literalString nameArg
            | _ -> None)

    { SourceQueryName = sourceQueryName
      RowAnnotations = lambdaParamAnnotation file toRow |> Option.toList }

let private buildButtonDetail (spec: SynExpr) : ButtonDetail =
    let disabledFalse =
        fieldValue "Disabled" spec
        |> Option.bind (applied "Some")
        |> Option.bind staticPayload
        |> Option.map (fun e ->
            match unwrap e with
            | SynExpr.Const(constant = SynConst.Bool false) -> true
            | _ -> false)
        |> Option.defaultValue false

    { DisabledBoundToStaticFalse = disabledFalse }

/// Per-file walk state: the calls found and the tree root currently open.
type private WalkState =
    {
        File: string
        Calls: ResizeArray<FuaranCall>
        /// Monotonic per-file counter minting a distinct key per root call site,
        /// so two roots sharing a NodeId stay two trees.
        mutable RootSeq: int
        /// The open tree — set while the walk is inside an outermost container
        /// call, `None` outside every tree.
        mutable Root: (string * string) option
    }

let private recordCall (state: WalkState) (ctor: string) (ctorRange: range) (args: SynExpr list) =
    let nodeIdLiteral = args |> List.tryHead |> Option.bind literalString
    let queries, dispatches = collectReferences state.File args
    let arg i = List.tryItem i args

    // A tree is rooted by an OUTERMOST container call with a literal id. A
    // container nested inside an open tree is part of that tree: NodeId
    // uniqueness is per whole tree (op-target stability), so a nested container
    // must not open a scope of its own.
    let opensRoot =
        state.Root.IsNone && SmartCtors.containers.Contains ctor && nodeIdLiteral.IsSome

    if opensRoot then
        state.RootSeq <- state.RootSeq + 1
        let id = nodeIdLiteral.Value
        state.Root <- Some(sprintf "%s|%s#%d" state.File id state.RootSeq, id)

    state.Calls.Add
        { Ctor = ctor
          NodeIdLiteral = nodeIdLiteral
          TreeRoot = state.Root |> Option.map snd
          TreeRootKey = state.Root |> Option.map fst
          Location = mkLocation state.File ctorRange
          QueryReferences = queries
          DispatchReferences = dispatches
          GridDetail =
            match ctor, arg 1, arg 2 with
            | "grid", Some toRow, Some spec -> Some(buildGridDetail state.File toRow spec)
            | _ -> None
          ButtonDetail =
            match ctor, arg 1 with
            | "button", Some spec -> Some(buildButtonDetail spec)
            | _ -> None
          ProgressDetail =
            match ctor, arg 1 with
            | "progress", Some spec ->
                Some { FractionLiteral = fieldValue "Fraction" spec |> Option.bind firstNumericLiteral }
            | _ -> None
          LinkDetail =
            match ctor, arg 1 with
            | "link", Some href -> Some { HrefLiteral = literalString href }
            | "linkSpec", Some spec ->
                Some { HrefLiteral = fieldValue "Href" spec |> Option.bind staticPayload |> Option.bind literalString }
            | _ -> None }

    opensRoot

/// Every Fuaran.X call site in one source, in source order.
let private callsIn (source: ParsedSource) : FuaranCall list =
    let state =
        { File = source.File
          Calls = ResizeArray()
          RootSeq = 0
          Root = None }

    let rec visit (expr: SynExpr) =
        match flattenApp expr with
        | FuaranSmartCtor(ctor, range), (_ :: _ as args) ->
            let opened = recordCall state ctor range args
            args |> List.iter (iterExpr visit)

            // Close the tree this call opened — on every arity. (The one-argument
            // arm of the old walker pushed a root it never popped, filing every
            // later call in the file under a tree that had already ended.)
            if opened then
                state.Root <- None

            false
        | _ -> true

    for expr in topLevelExprs source do
        iterExpr visit expr

    List.ofSeq state.Calls

/// Every Fuaran.X call site across the parsed sources.
let calls (sources: ParsedSource list) : FuaranCall list = sources |> List.collect callsIn
