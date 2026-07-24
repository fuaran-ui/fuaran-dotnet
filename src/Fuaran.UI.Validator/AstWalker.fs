module Fuaran.UI.Validator.AstWalker

// ============================================================================
//  AST walker.
//
//  Walks the untyped F# syntax tree (FSharp.Compiler.Service) of every source
//  file in a target .fsproj and extracts the Fuaran.X smart-ctor call shapes
//  the downstream checks (NodeIdCheck, BindingResolution, MsgPayloadCheck,
//  RowTypeCheck) reason against.
//
//  Why untyped, not typed:
//   - Anti-pattern: "Don't try to validate AGAINST the host's full
//     type universe". Untyped AST is enough for NodeId / Binding.Query name
//     literals and Action.Dispatch case-name textual match. Typed AST opens
//     the door to full type-checker complexity (FCS `CheckProjectAsync` /
//     `CheckFileInProject`) — out of scope for v1.
//   - The manifest IS the typed contract. The validator gates against the
//     manifest, not the host's full type universe.
//
//  Output of one walk: a list of `FuaranCall` records, each capturing one
//  smart-ctor invocation site (Fuaran.dashboard / .metric / .grid / .button /
//  .markdown / .callout / .progress / .stack / .gridLayout / .splitPanel /
//  .tabs / .card / .stepper / .markdownSpec / .skeleton) and its NodeId
//  string literal. Nested-children relationships are captured by the
//  per-call `ParentChain` field (top-most enclosing Fuaran.X call's NodeId,
//  used by NodeIdCheck to define per-tree scope).
// ============================================================================

open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Fuaran.UI.Validator.Findings

/// The Fuaran.X smart constructors we recognise. Anything else in the source
/// is opaque to the walker.
let private knownSmartCtors =
    Set.ofList
        [ "dashboard"
          "stack"
          "gridLayout"
          "splitPanel"
          "tabs"
          "card"
          "stepper"
          "metric"
          "markdown"
          "markdownSpec"
          "callout"
          "progress"
          "skeleton"
          "button"
          // Link smart-ctors — LinkCheck (FUARAN063) flags a blank Href.
          "link"
          "linkSpec"
          "grid"
          // NodeId uniqueness applies inside the
          // boundary's child + fallback subtrees too, so the walker must
          // recognise the smart-ctor and traverse its record body.
          "errorBoundary" ]

/// The layout-call names that root a Node<'Msg> tree. NodeId uniqueness is
/// enforced PER TREE; a tree is the subtree under the nearest enclosing
/// top-level call from this set.
let private treeRoots = Set.ofList [ "dashboard" ]

/// One Fuaran.X smart-ctor invocation site.
type FuaranCall =
    {
        /// Smart-ctor name, e.g. `"dashboard"`, `"metric"`.
        Ctor: string
        /// First positional argument's string literal — the NodeId.
        /// `None` if the call site doesn't supply a literal (rare; emits a
        /// finding from NodeIdCheck).
        NodeIdLiteral: string option
        /// NodeId of the nearest enclosing tree-root call (Fuaran.dashboard).
        /// `None` if this call site is itself the root or not under one.
        TreeRoot: string option
        /// Source location of the smart-ctor identifier.
        Location: Location
        /// `binding.query "name" ...` references inside this call's record
        /// expression (collected so BindingResolution and RowTypeCheck can
        /// reason about them without re-walking).
        QueryReferences: QueryReference list
        /// `Action.Dispatch (Case ...)` or `Action.dispatch (Case ...)`
        /// references inside this call's record expression.
        DispatchReferences: DispatchReference list
        /// Populated only for `Ctor = "grid"`. Carries grid-specific shape
        /// info needed by RowTypeCheck: the Source field's `binding.query`
        /// name (if any) and any lambda parameter type annotations found in
        /// OnRowClick / RowKey / Column accessors.
        GridDetail: GridDetail option
        /// Populated for `Ctor = "button"` and `Ctor = "callout"`
        /// (the two Kinds AccessibilityCheck cares about). Snapshots the
        /// `Accessibility` / `Label` / `Tone` fields' textual shape so the
        /// check doesn't re-walk the body.
        A11yDetail: A11yDetail option
        /// Populated for `Ctor = "progress"`. Snapshots the
        /// `Fraction` field's literal value (when one is statically present)
        /// so ScalarRangeCheck can advise on out-of-[0,1] declarations
        /// without re-walking the body.
        ProgressDetail: ProgressDetail option
        /// Populated for `Ctor = "tabs"`. Snapshots the tabs
        /// spec's `Children` / `TabHeaders` / `TabTags` / `ActiveTag` field
        /// shape so TabsCheck can surface FUARAN047 / FUARAN048 / FUARAN049
        /// findings without re-walking the body.
        TabsDetail: TabsDetail option
        /// Populated for `Ctor = "link"` / `"linkSpec"`. Snapshots the
        /// `Href` literal (when statically present) so LinkCheck can flag a
        /// blank href (FUARAN063) without re-walking the body.
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
        /// Query name referenced by the grid's `Source = binding.query "name" ...`.
        /// `None` if the grid uses a non-query binding (Static, Selection, ...).
        SourceQueryName: string option
        /// Type names annotated on lambda parameters in OnRowClick / RowKey / Column
        /// accessors. Empty when authors elide annotations (no rejection — typed AST
        /// would catch those; we don't, per the v1 anti-pattern boundary).
        RowAnnotations: RowAnnotation list
    }

/// Per-call accessibility-relevant field snapshot. Populated only
/// for calls whose Ctor is interactive (`button`) or notification-shaped
/// (`callout`); other Ctors leave `A11yDetail = None`. Consumed by
/// AccessibilityCheck to surface FUARAN040 / FUARAN041 findings without re-walking
/// the body.
and A11yDetail =
    {
        /// True when the record body contains `Accessibility = None` or
        /// `Accessibility = Option.None` as an explicit field. Inheriting the
        /// smart-ctor default (the common path) leaves this `false`.
        AccessibilityExplicitNone: bool
        /// True when a `Label = ...` field is present anywhere in the record
        /// body. Used by FUARAN040 to decide whether a "derivable label" exists.
        HasLabelField: bool
        /// The literal value when `Label = TextSource.Literal "X"` (a simple
        /// literal shape). `None` when the Label is a non-literal expression
        /// (e.g. `binding.query "..." ...`) — in which case FUARAN040 trusts the
        /// binding to produce a non-empty string at runtime.
        LabelLiteralValue: string option
        /// The leaf case-name when `Tone = ToneVariant.X` is set (e.g.
        /// `"Warning"`, `"Critical"`, `"Info"`). `None` if no Tone field.
        ToneCaseName: string option
        /// True when the record body contains `Disabled = Some (Binding.Static false)`
        /// — a constant-false disabled binding that never disables the button,
        /// so it is a no-op equivalent to omitting `Disabled`. Almost always an
        /// unfinished binding. Consumed by ButtonDisabledCheck (FUARAN064).
        /// `Some (Binding.Static true)` (a permanently-disabled placeholder) is
        /// legitimate and deliberately NOT flagged, so it leaves this `false`.
        DisabledBoundToStaticFalse: bool
    }

/// Per-`progress`-call snapshot of the `Fraction` field's
/// statically-knowable literal. `FractionLiteral = Some v` when the
/// `Fraction = Binding.Static <lit>` (or any expression carrying a bare
/// numeric literal); `None` when the fraction is bound to a query /
/// state / computed source (no static value to range-check).
and ProgressDetail = { FractionLiteral: float option }

/// Per-`link` / `linkSpec`-call snapshot of the `Href` field's
/// statically-knowable literal. `HrefLiteral = Some s` when the href is a
/// compile-time string — the positional `Fuaran.link "id" "href" "label"`
/// 2nd argument, or `Href = Binding.Static "..."` in the record form.
/// `None` when the href is bound to a query / state / computed source (no
/// static value to check). LinkCheck (FUARAN063) flags a blank literal.
and LinkDetail = { HrefLiteral: string option }

/// Per-`tabs`-call snapshot of the spec fields TabsCheck reasons
/// against. List-length fields are populated only when the field's value is
/// a *static* list literal (`Children = [a; b; c]` or
/// `TabHeaders = Some [...]`); a non-literal expression (e.g. a let-bound
/// variable) leaves the slot `None` and the corresponding count rule is
/// silenced for that call site — the validator stays conservative rather
/// than guessing.
and TabsDetail =
    {
        /// Length of the `Children = [...]` list literal, or `None` if the
        /// field is absent / not a static list.
        ChildrenListLength: int option
        /// Length of the inner list when `TabHeaders = Some [...]`, or `None`
        /// when the field is absent / `None` / `Some <non-literal>`.
        TabHeadersListLength: int option
        /// Length of the inner list when `TabTags = Some [...]`, or `None`
        /// when the field is absent / `None` / `Some <non-literal>`.
        TabTagsListLength: int option
        /// True when the `TabTags` field is present in the record body
        /// (regardless of `Some` / `None` shape). FUARAN049 needs to
        /// distinguish "TabTags = None" from "TabTags field absent" —
        /// both starve `ActiveTag`, so we treat them the same.
        TabTagsExplicitlyPresent: bool
        /// True when `TabTags = Some _` is written (any inner shape).
        TabTagsSome: bool
        /// True when `ActiveTag = Some _` is written (any inner shape).
        ActiveTagSome: bool
    }

let private mkLocation (file: string) (range: range) : Location =
    { File = file
      Line = range.StartLine
      Column = range.StartColumn + 1 }

/// Extract a string literal value from a SynConst, if it is one.
let private constStringValue (c: SynConst) =
    match c with
    | SynConst.String(text = s) -> Some s
    | _ -> None

/// Extract a numeric literal value from a SynConst as a float, covering
/// the const shapes a `Fraction` literal can take (`0.5`, `1`, `1.0f`,
/// a decimal). `None` for non-numeric consts.
let private constFloatValue (c: SynConst) : float option =
    match c with
    | SynConst.Double d -> Some d
    | SynConst.Single f -> Some(float f)
    | SynConst.Int32 i -> Some(float i)
    | SynConst.Decimal d -> Some(float d)
    | _ -> None

/// Try to read the first positional argument of an application as a string
/// literal — the NodeId for any Fuaran.X "id" { ... } call.
let private firstArgStringLiteral (expr: SynExpr) : string option =
    match expr with
    | SynExpr.Const(constant = c) -> constStringValue c
    | SynExpr.Paren(expr = SynExpr.Const(constant = c)) -> constStringValue c
    | _ -> None

/// Project a long-ident's segments to their source-text names — the shared
/// helper behind every check module's `leafIdent` recogniser.
let identNames (ids: Ident list) : string list = ids |> List.map _.idText

/// Recognise the last-segment identifier of a long-ident applied call:
///   Fuaran.dashboard           -> Some "dashboard"
///   Fuaran.UI.Fuaran.metric         -> Some "metric"
///   binding.query            -> Some "query"
///   Action.dispatch          -> Some "dispatch"
///   Action.Dispatch          -> Some "Dispatch"
let private leafIdent (expr: SynExpr) : (string list * string * range) option =
    match expr with
    | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) when not ids.IsEmpty ->
        let names = identNames ids
        let leaf = List.last names
        let prefix = names |> List.take (names.Length - 1)
        Some(prefix, leaf, (List.last ids).idRange)
    | SynExpr.Ident i -> Some([], i.idText, i.idRange)
    | _ -> None

/// `Fuaran.<ctor>` recognition — the last-segment ident in `knownSmartCtors`
/// AND a prefix that ends in `Fuaran` (or is just `Fuaran`). The prefix check is
/// loose on purpose: `Fuaran.dashboard`, `Fuaran.UI.Fuaran.dashboard`, and
/// `open Fuaran in Fuaran.dashboard` all qualify. A bare `dashboard` (no
/// qualifier) is treated as ambiguous and not recognised — Fuaran's smart
/// ctors are always qualified per §4c.
let private (|FuaranSmartCtor|_|) (expr: SynExpr) =
    match leafIdent expr with
    | Some(prefix, leaf, r) when knownSmartCtors.Contains leaf ->
        match prefix with
        | [] -> None
        | _ when List.last prefix = "Fuaran" -> Some(leaf, r)
        | _ -> None
    | _ -> None

/// `binding.query` recognition — leaf `query` with `binding` directly before.
let private (|BindingQuery|_|) (expr: SynExpr) =
    match leafIdent expr with
    | Some(prefix, "query", r) when not prefix.IsEmpty && List.last prefix = "binding" -> Some r
    | _ -> None

/// `Action.dispatch` (lower-case smart-ctor) or `Action.Dispatch` (raw DU
/// case constructor). We treat both as a dispatch reference.
let private (|ActionDispatch|_|) (expr: SynExpr) =
    match leafIdent expr with
    | Some(prefix, ("dispatch" | "Dispatch"), r) when not prefix.IsEmpty && List.last prefix = "Action" -> Some r
    | _ -> None

/// Pull the leading constructor name out of a dispatch payload expression.
///   SelectRow 5           -> Some "SelectRow"
///   UpdateRefGRP(1, 2.0)  -> Some "UpdateRefGRP"
///   (LoadData)            -> Some "LoadData"
/// Anything more complex (a let-bound value, a function call, a tuple of
/// many cases) returns None — we don't infer.
let rec private payloadCaseName (expr: SynExpr) : string option =
    match expr with
    | SynExpr.App(funcExpr = f) -> payloadCaseName f
    | SynExpr.Paren(expr = inner) -> payloadCaseName inner
    | SynExpr.Ident i -> Some i.idText
    | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) when not ids.IsEmpty -> Some (List.last ids).idText
    | _ -> None

type private WalkState =
    { File: string
      mutable Calls: FuaranCall list
      mutable TreeRootStack: string list }

let private pushRoot (state: WalkState) (rootId: string) =
    state.TreeRootStack <- rootId :: state.TreeRootStack

let private popRoot (state: WalkState) =
    match state.TreeRootStack with
    | [] -> ()
    | _ :: rest -> state.TreeRootStack <- rest

let private currentRoot (state: WalkState) : string option =
    match state.TreeRootStack with
    | top :: _ -> Some top
    | [] -> None

/// Walk a record-expression body collecting query / dispatch references.
/// We descend into every sub-expression; the references are textual and
/// untyped, so the walker is intentionally simple.
let rec private collectReferences
    (file: string)
    (expr: SynExpr)
    (queries: ResizeArray<QueryReference>)
    (dispatches: ResizeArray<DispatchReference>)
    =
    let recur e =
        collectReferences file e queries dispatches

    match expr with
    | SynExpr.App(funcExpr = BindingQuery r; argExpr = SynExpr.Const(constant = c)) ->
        match constStringValue c with
        | Some name ->
            queries.Add
                { Name = name
                  Location = mkLocation file r }
        | None -> ()
    | SynExpr.App(funcExpr = SynExpr.App(funcExpr = BindingQuery r; argExpr = SynExpr.Const(constant = c)); argExpr = _) ->
        match constStringValue c with
        | Some name ->
            queries.Add
                { Name = name
                  Location = mkLocation file r }
        | None -> ()
    | SynExpr.App(funcExpr = ActionDispatch r; argExpr = payload) ->
        match payloadCaseName payload with
        | Some caseName ->
            dispatches.Add
                { CaseName = caseName
                  Location = mkLocation file r }
        | None -> ()

        recur payload
    | SynExpr.App(funcExpr = f; argExpr = a) ->
        recur f
        recur a
    | SynExpr.Paren(expr = e) -> recur e
    | SynExpr.Tuple(exprs = es) -> es |> List.iter recur
    | SynExpr.Record(recordFields = fields) ->
        for SynExprRecordField(expr = fieldExpr) in fields do
            fieldExpr |> Option.iter recur
    | SynExpr.LetOrUse synLet ->
        for SynBinding(expr = e) in synLet.Bindings do
            recur e

        recur synLet.Body
    | SynExpr.Sequential(expr1 = a; expr2 = b) ->
        recur a
        recur b
    | SynExpr.IfThenElse(ifExpr = c; thenExpr = t; elseExpr = e) ->
        recur c
        recur t
        e |> Option.iter recur
    | SynExpr.Match(expr = scrut; clauses = clauses) ->
        recur scrut

        for SynMatchClause(resultExpr = r) in clauses do
            recur r
    | SynExpr.Lambda(body = b) -> recur b
    | SynExpr.ArrayOrList(exprs = es) -> es |> List.iter recur
    | SynExpr.ArrayOrListComputed(expr = e) -> recur e
    | SynExpr.ComputationExpr(expr = e) -> recur e
    | SynExpr.TypeApp(expr = e) -> recur e
    | SynExpr.Typed(expr = e) -> recur e
    | SynExpr.Do(expr = e) -> recur e
    | SynExpr.DotGet(expr = e) -> recur e
    | SynExpr.DotSet(targetExpr = t; rhsExpr = r) ->
        recur t
        recur r
    | SynExpr.LongIdentSet(expr = e) -> recur e
    | SynExpr.New(expr = e) -> recur e
    | SynExpr.AddressOf(expr = e) -> recur e
    | _ -> ()

/// Find the `binding.query "name"` literal that appears inside `expr` (the
/// first such reference, depth-first). Used for the grid's `Source` field
/// resolution.
let rec private firstQueryNameIn (expr: SynExpr) : string option =
    match expr with
    | SynExpr.App(funcExpr = BindingQuery _; argExpr = SynExpr.Const(constant = c)) -> constStringValue c
    | SynExpr.App(funcExpr = SynExpr.App(funcExpr = BindingQuery _; argExpr = SynExpr.Const(constant = c)); argExpr = _) ->
        constStringValue c
    | SynExpr.App(funcExpr = f; argExpr = a) ->
        match firstQueryNameIn f with
        | Some n -> Some n
        | None -> firstQueryNameIn a
    | SynExpr.Paren(expr = e) -> firstQueryNameIn e
    | SynExpr.Typed(expr = e) -> firstQueryNameIn e
    | SynExpr.Lambda(body = b) -> firstQueryNameIn b
    | _ -> None

/// Find the first numeric literal that appears inside `expr` (depth-first,
/// arg-before-func so `Binding.Static 0.5` resolves to `0.5` rather than
/// descending the `Binding.Static` ident). Used for the progress `Fraction`
/// field's static-value resolution.
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

/// Render a SynType's leaf type name (e.g. `SampleRow`, `string`). Compound
/// shapes (tuples, generics) flatten to the head identifier; we only need a
/// stable single-token comparator against the manifest's queryRowTypes entry.
let rec private synTypeName (t: SynType) : string option =
    match t with
    | SynType.LongIdent(longDotId = SynLongIdent(id = ids)) when not ids.IsEmpty -> Some (List.last ids).idText
    | SynType.App(typeName = inner) -> synTypeName inner
    | SynType.Paren(innerType = inner) -> synTypeName inner
    | SynType.Var(typar = SynTypar(ident = i)) -> Some i.idText
    | _ -> None

/// Walk an expression collecting `fun (param: TypeName) -> ...` annotations.
/// We only record annotations on the FIRST positional lambda parameter —
/// matching the shape `OnRowClick`, `RowKey`, and Column accessor lambdas
/// take (a single row argument).
let rec private collectRowAnnotations (file: string) (expr: SynExpr) (acc: ResizeArray<RowAnnotation>) =
    let recur e = collectRowAnnotations file e acc

    match expr with
    | SynExpr.Lambda(args = SynSimplePats.SimplePats(pats = pats); body = body) ->
        match pats with
        | SynSimplePat.Typed(SynSimplePat.Id(ident = i), targetType, _) :: _ ->
            match synTypeName targetType with
            | Some name ->
                acc.Add
                    { Annotation = name
                      Location = mkLocation file i.idRange }
            | None -> ()
        | _ -> ()

        recur body
    | SynExpr.App(funcExpr = f; argExpr = a) ->
        recur f
        recur a
    | SynExpr.Paren(expr = e) -> recur e
    | SynExpr.Tuple(exprs = es) -> es |> List.iter recur
    | SynExpr.Record(recordFields = fields) ->
        for SynExprRecordField(expr = fieldExpr) in fields do
            fieldExpr |> Option.iter recur
    | SynExpr.LetOrUse synLet ->
        for SynBinding(expr = e) in synLet.Bindings do
            recur e

        recur synLet.Body
    | SynExpr.Sequential(expr1 = a; expr2 = b) ->
        recur a
        recur b
    | SynExpr.IfThenElse(ifExpr = c; thenExpr = t; elseExpr = e) ->
        recur c
        recur t
        e |> Option.iter recur
    | SynExpr.Match(expr = scrut; clauses = clauses) ->
        recur scrut

        for SynMatchClause(resultExpr = r) in clauses do
            recur r
    | SynExpr.ArrayOrList(exprs = es) -> es |> List.iter recur
    | SynExpr.ArrayOrListComputed(expr = e) -> recur e
    | SynExpr.ComputationExpr(expr = e) -> recur e
    | SynExpr.TypeApp(expr = e) -> recur e
    | SynExpr.Typed(expr = e) -> recur e
    | SynExpr.Do(expr = e) -> recur e
    | SynExpr.DotGet(expr = e) -> recur e
    | _ -> ()

/// Pull the field-named expression for `fieldName` out of a record-expression
/// argument. Returns None if `expr` isn't a record or the field is absent.
let private fieldValue (fieldName: string) (expr: SynExpr) : SynExpr option =
    let rec inner e =
        match e with
        | SynExpr.Paren(expr = inner') -> inner inner'
        | SynExpr.Typed(expr = inner') -> inner inner'
        | SynExpr.Record(recordFields = fields) ->
            fields
            |> List.tryPick (fun (SynExprRecordField(fieldName = (SynLongIdent(id = ids), _); expr = fieldExpr)) ->
                match ids with
                | [] -> None
                | _ when (List.last ids).idText = fieldName -> fieldExpr
                | _ -> None)
        | _ -> None

    inner expr

// A11yDetail extractors.
//
// Detect `Accessibility = None` / `Option.None` explicitly written in the
// record body. The compiler also resolves bare `None` to `Option.None` via
// implicit qualification, but the AST sees the surface text — so we match
// both common shapes. Anything else (Some-cased or unresolved) is treated
// as "not explicitly None", which is the only case that suppresses the
// inherited Defaults.Accessibility.X default.

let private isExplicitNoneExpr (expr: SynExpr) : bool =
    let rec inner (e: SynExpr) =
        match e with
        | SynExpr.Paren(expr = inner') -> inner inner'
        | SynExpr.Typed(expr = inner') -> inner inner'
        | SynExpr.Ident i -> i.idText = "None"
        | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) when not ids.IsEmpty -> (List.last ids).idText = "None"
        | _ -> false

    inner expr

// Recognise `TextSource.Literal "..."` and capture the literal string. Other
// Label shapes (`TextSource.Bound (binding...)`, `binding.query ...` directly)
// return `None` — the validator treats those as "may produce a non-empty label
// at runtime" and does not fire FUARAN040.
let private literalLabelValue (expr: SynExpr) : string option =
    let rec inner (e: SynExpr) =
        match e with
        | SynExpr.Paren(expr = inner') -> inner inner'
        | SynExpr.Typed(expr = inner') -> inner inner'
        | SynExpr.App(funcExpr = leaf; argExpr = SynExpr.Const(constant = c)) ->
            match leafIdent leaf with
            | Some(_prefix, leafName, _) when leafName = "Literal" -> constStringValue c
            | _ -> Option.None
        | SynExpr.App(funcExpr = leaf; argExpr = SynExpr.Paren(expr = SynExpr.Const(constant = c))) ->
            match leafIdent leaf with
            | Some(_prefix, leafName, _) when leafName = "Literal" -> constStringValue c
            | _ -> Option.None
        | _ -> Option.None

    inner expr

// Recognise `Tone = ToneVariant.X` and return the leaf case name (`"Warning"`,
// `"Critical"`, etc.). Returns `None` for non-DU-construction shapes.
let private toneCaseNameOf (expr: SynExpr) : string option =
    let rec inner (e: SynExpr) =
        match e with
        | SynExpr.Paren(expr = inner') -> inner inner'
        | SynExpr.Typed(expr = inner') -> inner inner'
        | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) when not ids.IsEmpty -> Some (List.last ids).idText
        | SynExpr.Ident i -> Some i.idText
        | _ -> Option.None

    inner expr

// Detect `Disabled = Some (Binding.Static <bool>)` and return the literal bool.
// Peels `Some` (the field is `Binding<bool> option`), then matches a
// `Binding.Static` whose argument is a boolean const. Non-static shapes
// (`Some (binding.state ...)`, `Some (Binding.Computed ...)`) and a non-literal
// argument return `None`. ButtonDisabledCheck (FUARAN064) flags only the
// `false` case — a constant-false disabled binding is a no-op.
let private staticBoolUnderSome (expr: SynExpr) : bool option =
    let constBool (c: SynConst) =
        match c with
        | SynConst.Bool b -> Some b
        | _ -> None

    let rec staticArg e =
        match e with
        | SynExpr.Paren(expr = inner)
        | SynExpr.Typed(expr = inner) -> staticArg inner
        | SynExpr.App(funcExpr = leaf; argExpr = SynExpr.Const(constant = c)) ->
            match leafIdent leaf with
            | Some(_, "Static", _) -> constBool c
            | _ -> None
        | SynExpr.App(funcExpr = leaf; argExpr = SynExpr.Paren(expr = SynExpr.Const(constant = c))) ->
            match leafIdent leaf with
            | Some(_, "Static", _) -> constBool c
            | _ -> None
        | _ -> None

    let rec peelSome e =
        match e with
        | SynExpr.Paren(expr = inner)
        | SynExpr.Typed(expr = inner) -> peelSome inner
        | SynExpr.App(funcExpr = leaf; argExpr = arg) ->
            match leafIdent leaf with
            | Some(_, "Some", _) -> staticArg arg
            | _ -> None
        | _ -> None

    peelSome expr

let private buildA11yDetail (bodyExpr: SynExpr) : A11yDetail =
    let accessibilityField = fieldValue "Accessibility" bodyExpr
    let labelField = fieldValue "Label" bodyExpr
    let toneField = fieldValue "Tone" bodyExpr
    let disabledField = fieldValue "Disabled" bodyExpr

    { AccessibilityExplicitNone = accessibilityField |> Option.map isExplicitNoneExpr |> Option.defaultValue false
      HasLabelField = labelField.IsSome
      LabelLiteralValue = labelField |> Option.bind literalLabelValue
      ToneCaseName = toneField |> Option.bind toneCaseNameOf
      DisabledBoundToStaticFalse = (disabledField |> Option.bind staticBoolUnderSome) = Some false }

let private buildProgressDetail (bodyExpr: SynExpr) : ProgressDetail =
    { FractionLiteral = fieldValue "Fraction" bodyExpr |> Option.bind firstNumericLiteralIn }

// Recognise `Binding.Static "..."` and capture the literal string. Mirrors
// `literalLabelValue` but keys off the `Static` leaf — the `Href` field of a
// `LinkSpec` is a `Binding<string>`, so a static href is `Binding.Static "X"`.
// Non-static shapes (`binding.query ...`, `binding.state ...`) return `None`.
let private staticStringValue (expr: SynExpr) : string option =
    let rec inner (e: SynExpr) =
        match e with
        | SynExpr.Paren(expr = inner') -> inner inner'
        | SynExpr.Typed(expr = inner') -> inner inner'
        | SynExpr.App(funcExpr = leaf; argExpr = SynExpr.Const(constant = c)) ->
            match leafIdent leaf with
            | Some(_prefix, leafName, _) when leafName = "Static" -> constStringValue c
            | _ -> Option.None
        | SynExpr.App(funcExpr = leaf; argExpr = SynExpr.Paren(expr = SynExpr.Const(constant = c))) ->
            match leafIdent leaf with
            | Some(_prefix, leafName, _) when leafName = "Static" -> constStringValue c
            | _ -> Option.None
        | _ -> Option.None

    inner expr

/// Record-form (`Fuaran.linkSpec`) link detail: the `Href = Binding.Static "X"`
/// literal when statically present.
let private buildLinkDetailRecord (bodyExpr: SynExpr) : LinkDetail =
    { HrefLiteral = fieldValue "Href" bodyExpr |> Option.bind staticStringValue }

// TabsDetail extractors.
//
// `Some [a; b; c]` is a `SynExpr.App(funcExpr = "Some", argExpr = ArrayOrList[...])`
// shape; the surface walker peels the `Some` then reads the inner list length.
// A non-literal inner (`Some someVar`, `Some (List.map ...)`) leaves the slot
// `None` — TabsCheck only fires when the count is statically resolvable.

// Count the leaves of a `Sequential` chain. `[a; b; c]` parses as the right-
// associative `Sequential(a, Sequential(b, c))` body under
// `ArrayOrListComputed`; we count both branches recursively. Anything else
// (a single `App`, an `IfThenElse`, a `Lambda`, ...) contributes one leaf —
// matching the surface-syntax intuition that one statement is one list item.
let rec private sequentialLeafCount (e: SynExpr) : int =
    match e with
    | SynExpr.Sequential(expr1 = a; expr2 = b) -> sequentialLeafCount a + sequentialLeafCount b
    | _ -> 1

let private staticListLength (expr: SynExpr) : int option =
    let rec inner (e: SynExpr) =
        match e with
        | SynExpr.Paren(expr = e') -> inner e'
        | SynExpr.Typed(expr = e') -> inner e'
        // Raw / homogeneous list literal — used for empty or single-element
        // lists and for some array shapes.
        | SynExpr.ArrayOrList(exprs = es) -> Some es.Length
        // `[a; b; c]` — multi-element list parses through ArrayOrListComputed
        // with a Sequential body whose leaves are the list items.
        | SynExpr.ArrayOrListComputed(isArray = false; expr = inner') -> Some(sequentialLeafCount inner')
        | _ -> None

    inner expr

let private someInnerExpr (expr: SynExpr) : SynExpr option =
    let rec inner (e: SynExpr) =
        match e with
        | SynExpr.Paren(expr = e') -> inner e'
        | SynExpr.Typed(expr = e') -> inner e'
        | SynExpr.App(funcExpr = leaf; argExpr = arg) ->
            match leafIdent leaf with
            | Some(_, "Some", _) -> Some arg
            | _ -> Option.None
        | _ -> Option.None

    inner expr

let private buildTabsDetail (bodyExpr: SynExpr) : TabsDetail =
    let childrenField = fieldValue "Children" bodyExpr
    let tabHeadersField = fieldValue "TabHeaders" bodyExpr
    let tabTagsField = fieldValue "TabTags" bodyExpr
    let activeTagField = fieldValue "ActiveTag" bodyExpr

    let childrenLength = childrenField |> Option.bind staticListLength

    let tabHeadersListLength =
        tabHeadersField |> Option.bind someInnerExpr |> Option.bind staticListLength

    let tabTagsListLength =
        tabTagsField |> Option.bind someInnerExpr |> Option.bind staticListLength

    let tabTagsSome = tabTagsField |> Option.bind someInnerExpr |> Option.isSome

    let activeTagSome = activeTagField |> Option.bind someInnerExpr |> Option.isSome

    { ChildrenListLength = childrenLength
      TabHeadersListLength = tabHeadersListLength
      TabTagsListLength = tabTagsListLength
      TabTagsExplicitlyPresent = tabTagsField.IsSome
      TabTagsSome = tabTagsSome
      ActiveTagSome = activeTagSome }

let private buildGridDetail (file: string) (bodyExpr: SynExpr) : GridDetail =
    let sourceQueryName = fieldValue "Source" bodyExpr |> Option.bind firstQueryNameIn

    let annotations = ResizeArray()

    for fieldName in [ "OnRowClick"; "RowKey"; "Columns" ] do
        match fieldValue fieldName bodyExpr with
        | Some e -> collectRowAnnotations file e annotations
        | None -> ()

    { SourceQueryName = sourceQueryName
      RowAnnotations = List.ofSeq annotations }

/// Visit a single Fuaran.X smart-ctor call site, recording it and (if it is a
/// tree root like `Fuaran.dashboard`) pushing onto the tree-root stack so
/// nested calls inherit its NodeId as their TreeRoot.
let private visitFuaranCall (state: WalkState) (ctor: string) (ctorRange: range) (args: SynExpr list) =
    let nodeIdLiteral =
        match args with
        | first :: _ -> firstArgStringLiteral first
        | [] -> None

    let queries = ResizeArray()
    let dispatches = ResizeArray()

    for a in args do
        collectReferences state.File a queries dispatches

    let gridDetail =
        if ctor = "grid" then
            match args with
            | _ :: bodyArg :: _ -> Some(buildGridDetail state.File bodyArg)
            | _ -> None
        else
            None

    let a11yDetail =
        if ctor = "button" || ctor = "callout" then
            match args with
            | _ :: bodyArg :: _ -> Some(buildA11yDetail bodyArg)
            | _ -> None
        else
            None

    let progressDetail =
        if ctor = "progress" then
            match args with
            | _ :: bodyArg :: _ -> Some(buildProgressDetail bodyArg)
            | _ -> None
        else
            None

    let tabsDetail =
        if ctor = "tabs" then
            match args with
            | _ :: bodyArg :: _ -> Some(buildTabsDetail bodyArg)
            | _ -> None
        else
            None

    let linkDetail =
        if ctor = "link" then
            // Positional `Fuaran.link "id" "href" "label"` — href is the 2nd arg.
            match args with
            | _ :: hrefArg :: _ -> Some { HrefLiteral = firstArgStringLiteral hrefArg }
            | _ -> None
        elif ctor = "linkSpec" then
            match args with
            | _ :: bodyArg :: _ -> Some(buildLinkDetailRecord bodyArg)
            | _ -> None
        else
            None

    let priorRoot = currentRoot state

    let call =
        { Ctor = ctor
          NodeIdLiteral = nodeIdLiteral
          TreeRoot =
            match priorRoot, treeRoots.Contains ctor, nodeIdLiteral with
            | None, true, Some id -> Some id // self-root
            | other, _, _ -> other
          Location = mkLocation state.File ctorRange
          QueryReferences = List.ofSeq queries
          DispatchReferences = List.ofSeq dispatches
          GridDetail = gridDetail
          A11yDetail = a11yDetail
          ProgressDetail = progressDetail
          TabsDetail = tabsDetail
          LinkDetail = linkDetail }

    state.Calls <- call :: state.Calls

    if treeRoots.Contains ctor then
        match nodeIdLiteral with
        | Some id -> pushRoot state id
        | None -> ()

/// Recursive walk over the syntax tree. We look for `Fuaran.<ctor> "id" { ... }`
/// shapes anywhere they appear. The outer pattern is
/// `SynExpr.App(App(Fuaran.ctor, "id"), { ... })` — F# parses curried apps left-
/// associatively, so the function form for `Fuaran.dashboard "x" body` is
/// `App(App(Fuaran.dashboard, "x"), body)`.
let rec private walkExpr (state: WalkState) (expr: SynExpr) =
    match expr with
    | SynExpr.App(
        funcExpr = SynExpr.App(funcExpr = FuaranSmartCtor(ctor, ctorRange); argExpr = idArg); argExpr = bodyArg) ->
        // Two-arg shape: Fuaran.<ctor> "id" body
        visitFuaranCall state ctor ctorRange [ idArg; bodyArg ]

        let wasRoot = treeRoots.Contains ctor && (firstArgStringLiteral idArg).IsSome

        walkExpr state bodyArg

        if wasRoot then
            popRoot state
    | SynExpr.App(funcExpr = FuaranSmartCtor(ctor, ctorRange); argExpr = arg) ->
        // One-arg shape: rare (e.g. Fuaran.skeleton id rows is two-arg via App-of-App,
        // and Fuaran.markdown body is also two-arg). Record what we have and recurse.
        visitFuaranCall state ctor ctorRange [ arg ]
        walkExpr state arg
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

/// Walk one F# source file, returning the FuaranCalls it contains.
let walkFile (checker: FSharpChecker) (file: string) : Async<FuaranCall list> =
    async {
        let source = File.ReadAllText file
        let! parseResult = parseFile checker file source

        let state =
            { File = file
              Calls = []
              TreeRootStack = [] }

        match parseResult.ParseTree with
        | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) -> modules |> List.iter (walkModule state)
        | ParsedInput.SigFile _ -> ()

        return state.Calls |> List.rev
    }

/// Walk every .fs source under a project directory. Discovery is
/// directory-recursive plus a filename filter; we deliberately do NOT use
/// MSBuild evaluation to enumerate Compile items because the validator
/// should run before / outside the build, and the directory scan is fast.
/// The `Build` FAKE target dependency keeps build-emitted .fs files (none
/// today, but future codegen could) inside the scan window.
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
        |> List.ofSeq
    else
        []
