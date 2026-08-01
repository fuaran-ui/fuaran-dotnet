module Fuaran.UI.Validator.ManifestEmitter

// ============================================================================
//  Validator-manifest emitter — schema grounding correct-by-construction.
//
//  The language's central claim is that a Fuaran tree's bindings are
//  schema-grounded: "the AI cannot hallucinate a query", because FUARAN010 /
//  FUARAN020 gate every `binding.query` / `Action.dispatch` reference against
//  the manifest. That claim rested on a HAND-WRITTEN manifest — the one
//  artefact grounding everything else was the least grounded artefact in the
//  system, free to drift silently from the app it describes.
//
//  This module derives the manifest from the app's own F# source instead.
//
//  ── Declaration site, never reference site ────────────────────────────────
//
//  The load-bearing design rule. A facet derived from the site the validator
//  CHECKS makes that check vacuous — derive `queries` from the tree's
//  `binding.query "n"` references and every reference resolves by
//  construction, including the hallucinated one. So:
//
//    queries    ← `BindingSources.QueryResults` construction (the REGISTRATION
//                 site) — never `AstWalker`'s QueryReference list.
//    msgCases   ← the `Msg` DU's own type declaration — never `AstWalker`'s
//                 DispatchReference list.
//
//  Both checks therefore stay fully live over a generated manifest.
//
//  `queryRowTypes` is the honest exception: its only in-source evidence IS the
//  grid lambda annotation FUARAN031 compares against, so under a generated
//  manifest FUARAN031 cannot fire. The defect it detects does not vanish — it
//  moves EARLIER. Two grids reading one query but annotating different row
//  types is a generation-time conflict (`RowTypeConflicts`); the emitter omits
//  the entry rather than picking a winner, FUARAN030 then warns that the query
//  is unverifiable, and the author resolves it — in the source, or by
//  asserting the row type in the override tier, where FUARAN031 is live again.
//
//  ── Override tier ────────────────────────────────────────────────────────
//
//  `fuaran-validator.manifest.overrides.json` (same wire shape, partial) is
//  merged OVER the derived base by `Manifest.mergeOverrides`. It carries what
//  no walker can see: a dynamically registered query, a `Msg` DU that does not
//  follow the naming rule, a policy knob like `customNodeRatio`. Every entry
//  it contributes is listed under `$generated.asserted` in the emitted file,
//  so a reviewer can tell asserted from derived without diffing.
//
//  ── Anti-pattern boundary (unchanged) ─────────────────────────────────────
//
//  Untyped AST only, per the Phase 12.V line. Derivation is a set of narrow,
//  documented syntactic rules — never a type-checker pass. A rule that cannot
//  see something says so (a conflict, or simply no entry) and the override
//  tier absorbs it; the emitter never guesses.
// ============================================================================

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open Fuaran.UI.Validator.AstWalker
open Fuaran.UI.Validator.Manifest

// ─── Derived model ──────────────────────────────────────────────────────────

/// One `Msg` DU case as declared. `PayloadTypes` renders the case's field
/// types in source order (`[]` for a nullary case, `[ "int" ]` for
/// `SelectRow of int`, `[ "int"; "float" ]` for a tupled case).
///
/// The v1 manifest wire shape carries case NAMES only — `MsgPayloadCheck`
/// matches textually and reads nothing else — so payloads are derived and
/// reported but deliberately NOT emitted: adding an unconsumed key to a
/// published wire shape costs every consumer a migration for no gate. When a
/// check earns the data, the field is additive and minor-compatible.
type MsgCase =
    { Case: string
      PayloadTypes: string list }

/// Everything the walkers could establish about a project, before the
/// override tier is applied.
type Derivation =
    {
        /// Query names read off `BindingSources.QueryResults` registration sites.
        Queries: Set<string>
        /// `Msg` DU cases, deduplicated by name and sorted.
        MsgCases: MsgCase list
        /// Query → row type, where every annotated grid on that query agrees.
        QueryRowTypes: Map<string, string>
        /// Query → the >1 distinct row-type annotations found across its grids.
        /// Deliberately NOT resolved: an ambiguous source is a defect to
        /// surface, not a coin to flip.
        RowTypeConflicts: Map<string, string list>
        FilesWalked: int
    }

/// Which merged entries exist because the override tier ASSERTED them rather
/// than because the source demonstrates them.
type Provenance =
    {
        AssertedQueries: Set<string>
        AssertedMsgCases: Set<string>
        AssertedQueryRowTypes: Set<string>
        AssertedCustomNodeRatio: bool
        /// Override entries the derivation now produces identically — the
        /// override is a no-op and can be deleted. Reported, never removed
        /// automatically: pruning someone's asserted contract is their call.
        RedundantOverrides: string list
    }

let emptyProvenance =
    { AssertedQueries = Set.empty
      AssertedMsgCases = Set.empty
      AssertedQueryRowTypes = Set.empty
      AssertedCustomNodeRatio = false
      RedundantOverrides = [] }

/// One difference between the manifest the source implies and the manifest
/// committed to the repo. Every case names the entry, so a CI failure tells a
/// developer what changed rather than that something did.
type Drift =
    /// The source registers it; the committed manifest lacks it. A query or
    /// case was added without regenerating.
    | MissingFromCommitted of facet: string * name: string
    /// The committed manifest carries it; the source no longer does. A query
    /// or case was deleted without regenerating.
    | StaleInCommitted of facet: string * name: string
    | RowTypeDiffers of query: string * committed: string * generated: string
    | CustomNodeRatioDiffers of committed: float option * generated: float option

let describeDrift (drift: Drift) : string =
    match drift with
    | MissingFromCommitted(facet, name) ->
        sprintf "%s: \"%s\" is registered in source but missing from the committed manifest" facet name
    | StaleInCommitted(facet, name) ->
        sprintf "%s: \"%s\" is in the committed manifest but no longer registered in source" facet name
    | RowTypeDiffers(query, committed, generated) ->
        sprintf "queryRowTypes: query \"%s\" is declared `%s` but the source annotates `%s`" query committed generated
    | CustomNodeRatioDiffers(committed, generated) ->
        let render =
            function
            | Some(v: float) -> string v
            | None -> "(unset)"

        sprintf "customNodeRatio: committed %s, generated %s" (render committed) (render generated)

// ─── Type rendering (payload shapes) ────────────────────────────────────────

/// Render a `SynType` back to a readable source-shaped name. Only ever shown
/// to a human (payload reporting) — nothing compares against it — so an
/// unrecognised shape degrades to `_` rather than failing the pass.
let rec private renderSynType (t: SynType) : string =
    match t with
    | SynType.LongIdent(longDotId = SynLongIdent(id = ids)) when not ids.IsEmpty -> (List.last ids).idText
    | SynType.App(typeName = name; typeArgs = args; isPostfix = true) ->
        match args with
        | [ single ] -> sprintf "%s %s" (renderSynType single) (renderSynType name)
        | _ -> renderSynType name
    | SynType.App(typeName = name; typeArgs = args) ->
        match args with
        | [] -> renderSynType name
        | _ -> sprintf "%s<%s>" (renderSynType name) (args |> List.map renderSynType |> String.concat ", ")
    | SynType.Array(elementType = element) -> sprintf "%s[]" (renderSynType element)
    | SynType.Paren(innerType = inner) -> renderSynType inner
    | SynType.Fun(argType = argType; returnType = returnType) ->
        sprintf "%s -> %s" (renderSynType argType) (renderSynType returnType)
    | SynType.Var(typar = SynTypar(ident = i)) -> "'" + i.idText
    | SynType.Tuple(path = segments) ->
        segments
        |> List.choose (fun segment ->
            match segment with
            | SynTupleTypeSegment.Type t -> Some(renderSynType t)
            | _ -> None)
        |> String.concat " * "
    | _ -> "_"

// ─── Facet 1: `Msg` DU case names + payload shapes ──────────────────────────

/// Which union type declarations count as the module's message type.
///
/// `Msg` is the §4b convention; `AppMsg` / `GridMsg` are the common qualified
/// forms. The rule is deliberately NAME-based and narrow rather than
/// "every DU in the project": over-inclusion would admit unrelated case names
/// into `msgCases` and weaken FUARAN020, which is the whole point of the gate.
/// Under-inclusion is the safe direction — it produces a loud, locatable
/// FUARAN020 that the override tier resolves.
let isMsgTypeName (typeName: string) =
    typeName = "Msg" || typeName.EndsWith "Msg"

let rec private collectMsgCasesFromDecl (decl: SynModuleDecl) (acc: ResizeArray<MsgCase>) =
    match decl with
    | SynModuleDecl.Types(typeDefns = defns) ->
        for SynTypeDefn(typeInfo = SynComponentInfo(longId = ids); typeRepr = repr) in defns do
            match ids with
            | [] -> ()
            | _ when isMsgTypeName (List.last ids).idText ->
                match repr with
                | SynTypeDefnRepr.Simple(simpleRepr = SynTypeDefnSimpleRepr.Union(unionCases = cases)) ->
                    for SynUnionCase(ident = SynIdent(caseIdent, _); caseType = caseType) in cases do
                        let payloadTypes =
                            match caseType with
                            | SynUnionCaseKind.Fields fields ->
                                fields
                                |> List.map (fun (SynField(fieldType = fieldType)) -> renderSynType fieldType)
                            | _ -> []

                        acc.Add
                            { Case = caseIdent.idText
                              PayloadTypes = payloadTypes }
                | _ -> ()
            | _ -> ()
    | SynModuleDecl.NestedModule(decls = decls) -> decls |> List.iter (fun d -> collectMsgCasesFromDecl d acc)
    | _ -> ()

// ─── Facet 2: registered query names ────────────────────────────────────────

/// The `BindingSources` field whose construction site IS the query registry.
[<Literal>]
let private queryResultsField = "QueryResults"

/// Per-file scan state: every `let`-bound expression (so a `QueryResults`
/// field assigned a previously-bound value can be followed one hop — the
/// idiomatic shape, `let queryMap = Map.ofList [...]` then
/// `{ …empty with QueryResults = queryMap }`), and every expression assigned
/// to a `QueryResults` field.
type private FileScan =
    { Bindings: Dictionary<string, SynExpr>
      QueryResultExprs: ResizeArray<SynExpr> }

let private bindingName (SynBinding(headPat = pat)) =
    match pat with
    | SynPat.Named(ident = SynIdent(id, _)) -> Some id.idText
    | SynPat.LongIdent(longDotId = SynLongIdent(id = ids)) when not ids.IsEmpty -> Some (List.last ids).idText
    | _ -> None

/// Recursive descent recording `let` bindings and `QueryResults = …` field
/// assignments. Mirrors the shape list every other narrow walker in this
/// package uses (LocalBindingCheck, NumberFieldRangeCheck, …) — each rule owns
/// its own traversal so its lexical scope stays independent of the Fuaran.X
/// smart-ctor walker.
let rec private scanExpr (scan: FileScan) (expr: SynExpr) =
    let recur e = scanExpr scan e

    match expr with
    | SynExpr.Record(recordFields = fields) ->
        for SynExprRecordField(fieldName = (SynLongIdent(id = ids), _); expr = fieldExpr) in fields do
            match fieldExpr with
            | Some fieldExpr ->
                match ids with
                | [] -> ()
                | _ when (List.last ids).idText = queryResultsField -> scan.QueryResultExprs.Add fieldExpr
                | _ -> ()

                recur fieldExpr
            | None -> ()
    | SynExpr.LetOrUse synLet ->
        for binding in synLet.Bindings do
            let (SynBinding(expr = boundExpr)) = binding

            match bindingName binding with
            | Some name -> scan.Bindings[name] <- boundExpr
            | None -> ()

            recur boundExpr

        recur synLet.Body
    | SynExpr.App(funcExpr = f; argExpr = a) ->
        recur f
        recur a
    | SynExpr.Paren(expr = e) -> recur e
    | SynExpr.Tuple(exprs = es) -> es |> List.iter recur
    | SynExpr.Sequential(expr1 = a; expr2 = b) ->
        recur a
        recur b
    | SynExpr.IfThenElse(ifExpr = c; thenExpr = t; elseExpr = e) ->
        recur c
        recur t
        e |> Option.iter recur
    | SynExpr.Match(expr = scrutinee; clauses = clauses) ->
        recur scrutinee

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

let rec private scanDecl (scan: FileScan) (decl: SynModuleDecl) =
    match decl with
    | SynModuleDecl.Let(bindings = bindings) ->
        for binding in bindings do
            let (SynBinding(expr = boundExpr)) = binding

            match bindingName binding with
            | Some name -> scan.Bindings[name] <- boundExpr
            | None -> ()

            scanExpr scan boundExpr
    | SynModuleDecl.NestedModule(decls = decls) -> decls |> List.iter (scanDecl scan)
    | SynModuleDecl.Expr(expr = e) -> scanExpr scan e
    | _ -> ()

let private stringConst (expr: SynExpr) =
    match expr with
    | SynExpr.Const(constant = SynConst.String(text = s)) -> Some s
    | _ -> None

/// Harvest query names from an expression assigned to `QueryResults`.
///
/// One rule, applied to whatever collection idiom the author reached for
/// (`Map.ofList` / `Map.ofSeq` / `dict` / `readOnlyDict` / a raw list): the
/// FIRST element of a pair whose head is a string literal is a registered
/// name. Plus `Map.add "name" …`, the incremental form. Recognising the
/// shape rather than the constructor keeps the rule stable as idioms change,
/// and the scope is narrow — only expressions reached from a `QueryResults`
/// assignment are harvested, so an unrelated string-keyed pair elsewhere in
/// the file is never seen.
///
/// `depth` bounds the one-hop-per-level identifier resolution so a pair of
/// mutually-referencing bindings cannot loop.
let rec private harvestQueryNames (scan: FileScan) (depth: int) (expr: SynExpr) (acc: HashSet<string>) =
    if depth > 3 then
        ()
    else
        let recur e = harvestQueryNames scan depth e acc

        match expr with
        | SynExpr.Ident ident ->
            match scan.Bindings.TryGetValue ident.idText with
            | true, bound -> harvestQueryNames scan (depth + 1) bound acc
            | _ -> ()
        | SynExpr.Tuple(exprs = head :: _ :: _) ->
            match stringConst head with
            | Some name -> acc.Add name |> ignore
            | None -> ()
        | SynExpr.App(funcExpr = SynExpr.App(funcExpr = mapAdd; argExpr = keyArg); argExpr = valueArg) ->
            match leafIdentName mapAdd, stringConst keyArg with
            | Some "add", Some name -> acc.Add name |> ignore
            | _ -> ()

            recur mapAdd
            recur keyArg
            recur valueArg
        | SynExpr.App(funcExpr = f; argExpr = a) ->
            recur f
            recur a
        | SynExpr.Paren(expr = e) -> recur e
        | SynExpr.Typed(expr = e) -> recur e
        | SynExpr.ArrayOrList(exprs = es) -> es |> List.iter recur
        | SynExpr.ArrayOrListComputed(expr = e) -> recur e
        | SynExpr.ComputationExpr(expr = e) -> recur e
        | SynExpr.Sequential(expr1 = a; expr2 = b) ->
            recur a
            recur b
        | SynExpr.LetOrUse synLet ->
            for SynBinding(expr = e) in synLet.Bindings do
                recur e

            recur synLet.Body
        | SynExpr.IfThenElse(ifExpr = c; thenExpr = t; elseExpr = e) ->
            recur c
            recur t
            e |> Option.iter recur
        | SynExpr.Lambda(body = b) -> recur b
        | _ -> ()

/// Leaf identifier of a (possibly long) identifier expression — `Map.add`
/// yields `"add"`. Local to the harvest rule; `AstWalker.identNames` is the
/// shared segment projection underneath.
and private leafIdentName (expr: SynExpr) : string option =
    match expr with
    | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) when not ids.IsEmpty -> Some(List.last (identNames ids))
    | SynExpr.Ident i -> Some i.idText
    | _ -> None

// ─── Facet 3: per-query row types ───────────────────────────────────────────

/// Pair each grid's query source with the row-type annotations on its lambdas.
/// A query all of whose annotated grids agree yields an entry; a query whose
/// grids disagree yields a conflict and NO entry (see the module header).
let private deriveRowTypes (calls: FuaranCall list) =
    let annotationsByQuery =
        calls
        |> List.choose (fun call ->
            match call.Ctor, call.GridDetail with
            | "grid", Some detail ->
                match detail.SourceQueryName with
                | Some query -> Some(query, detail.RowAnnotations |> List.map _.Annotation)
                | None -> None
            | _ -> None)
        |> List.groupBy fst
        |> List.map (fun (query, entries) -> query, entries |> List.collect snd |> List.distinct |> List.sort)

    let resolved =
        annotationsByQuery
        |> List.choose (fun (query, annotations) ->
            match annotations with
            | [ single ] -> Some(query, single)
            | _ -> None)
        |> Map.ofList

    let conflicts =
        annotationsByQuery
        |> List.choose (fun (query, annotations) ->
            if List.length annotations > 1 then
                Some(query, annotations)
            else
                None)
        |> Map.ofList

    resolved, conflicts

// ─── Derivation driver ──────────────────────────────────────────────────────

/// Walk every source under `projectDir` and derive the manifest facets.
let derive (checker: FSharpChecker) (projectDir: string) : Async<Derivation> =
    async {
        let sourceFiles = discoverSourceFiles projectDir

        let msgCases = ResizeArray<MsgCase>()
        let queryNames = HashSet<string>()

        for file in sourceFiles do
            let! tree = parseTree checker file

            match tree with
            | Some(ParsedInput.ImplFile(ParsedImplFileInput(contents = modules))) ->
                let scan =
                    { Bindings = Dictionary()
                      QueryResultExprs = ResizeArray() }

                for SynModuleOrNamespace(decls = decls) in modules do
                    for decl in decls do
                        collectMsgCasesFromDecl decl msgCases
                        scanDecl scan decl

                // Harvest AFTER the whole file is scanned, so a `QueryResults`
                // assignment can follow a binding declared anywhere in it.
                for expr in scan.QueryResultExprs do
                    harvestQueryNames scan 0 expr queryNames
            | _ -> ()

        // Grid row-type annotations come from the existing Fuaran.X walker —
        // the one facet needing no new walking.
        let! allCalls = sourceFiles |> List.map (walkFile checker) |> Async.Parallel

        let rowTypes, conflicts = allCalls |> Array.toList |> List.concat |> deriveRowTypes

        let dedupedMsgCases =
            msgCases |> Seq.distinctBy _.Case |> Seq.sortBy _.Case |> List.ofSeq

        return
            { Queries = Set.ofSeq queryNames
              MsgCases = dedupedMsgCases
              QueryRowTypes = rowTypes
              RowTypeConflicts = conflicts
              FilesWalked = sourceFiles.Length }
    }

/// The derived facets as a `Manifest` — the base the override tier merges over.
let toManifest (derivation: Derivation) : Manifest =
    { Queries = derivation.Queries
      MsgCases = derivation.MsgCases |> List.map _.Case |> Set.ofList
      QueryRowTypes = derivation.QueryRowTypes
      CustomNodeRatio = None }

/// Which merged entries the override tier is responsible for. An override
/// entry the derivation now produces identically is NOT asserted — it is
/// derived, and the override is redundant.
let provenanceOf (derived: Manifest) (overrides: Manifest) : Provenance =
    let assertedQueries = Set.difference overrides.Queries derived.Queries
    let assertedMsgCases = Set.difference overrides.MsgCases derived.MsgCases

    let assertedRowTypes =
        overrides.QueryRowTypes
        |> Map.filter (fun query rowType -> Map.tryFind query derived.QueryRowTypes <> Some rowType)
        |> Map.keys
        |> Set.ofSeq

    let redundant =
        [ for query in Set.intersect overrides.Queries derived.Queries do
              yield sprintf "queries: \"%s\"" query
          for case in Set.intersect overrides.MsgCases derived.MsgCases do
              yield sprintf "msgCases: \"%s\"" case
          for KeyValue(query, rowType) in overrides.QueryRowTypes do
              if Map.tryFind query derived.QueryRowTypes = Some rowType then
                  yield sprintf "queryRowTypes: \"%s\"" query ]

    { AssertedQueries = assertedQueries
      AssertedMsgCases = assertedMsgCases
      AssertedQueryRowTypes = assertedRowTypes
      AssertedCustomNodeRatio = overrides.CustomNodeRatio.IsSome
      RedundantOverrides = redundant }

// ─── Emission ───────────────────────────────────────────────────────────────

/// The canonical on-disk form, pinned so byte-comparison is meaningful across
/// machines: 2-space indent, LF line endings, one trailing newline, UTF-8
/// without BOM, sets emitted in ordinal-sorted order. `JsonWriterOptions`
/// defaults `NewLine` to `Environment.NewLine`, which would make a Windows
/// emit and a Linux emit of the same source differ — pinned explicitly.
///
/// The relaxed encoder is deliberate: the default escapes every non-ASCII and
/// HTML-sensitive character (`—` → `—`, `<` → `<`), which protects a
/// JSON payload interpolated into a page and does nothing for a build artefact
/// read by a human reviewer and one parser. A manifest is never interpolated
/// anywhere; escaping only makes the diff unreadable.
let private writerOptions =
    JsonWriterOptions(
        Indented = true,
        NewLine = "\n",
        IndentCharacter = ' ',
        IndentSize = 2,
        Encoder = Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    )

let private writeNameArray (writer: Utf8JsonWriter) (name: string) (values: string seq) =
    writer.WriteStartArray name

    for value in values do
        writer.WriteStringValue value

    writer.WriteEndArray()

/// Render the merged manifest in canonical form.
///
/// The `$generated` header is the provenance surface: it says the file is
/// derived (so a reader does not hand-edit it) and names every entry the
/// override tier asserted. It is an additive top-level key — `Manifest.parse`
/// ignores unknown properties, so the validator reads a generated manifest
/// exactly as it reads a hand-written one.
let renderJson (manifest: Manifest) (provenance: Provenance) : string =
    use stream = new MemoryStream()
    use writer = new Utf8JsonWriter(stream, writerOptions)

    writer.WriteStartObject()

    writer.WriteStartObject "$generated"
    writer.WriteString("by", "Fuaran.UI.Validator manifest emitter")

    writer.WriteString(
        "doNotEdit",
        sprintf
            "Derived from source. Regenerate with: Fuaran.UI.Validator emit-manifest <project.fsproj>. Hand-assert entries in %s instead."
            overridesFileName
    )

    let hasAsserted =
        not (Set.isEmpty provenance.AssertedQueries)
        || not (Set.isEmpty provenance.AssertedMsgCases)
        || not (Set.isEmpty provenance.AssertedQueryRowTypes)
        || provenance.AssertedCustomNodeRatio

    if hasAsserted then
        writer.WriteStartObject "asserted"

        if not (Set.isEmpty provenance.AssertedQueries) then
            writeNameArray writer "queries" provenance.AssertedQueries

        if not (Set.isEmpty provenance.AssertedMsgCases) then
            writeNameArray writer "msgCases" provenance.AssertedMsgCases

        if not (Set.isEmpty provenance.AssertedQueryRowTypes) then
            writeNameArray writer "queryRowTypes" provenance.AssertedQueryRowTypes

        if provenance.AssertedCustomNodeRatio then
            writer.WriteBoolean("customNodeRatio", true)

        writer.WriteEndObject()

    writer.WriteEndObject()

    writeNameArray writer "queries" manifest.Queries
    writeNameArray writer "msgCases" manifest.MsgCases

    writer.WriteStartObject "queryRowTypes"

    for KeyValue(query, rowType) in manifest.QueryRowTypes do
        writer.WriteString(query, rowType)

    writer.WriteEndObject()

    match manifest.CustomNodeRatio with
    | Some ratio -> writer.WriteNumber("customNodeRatio", ratio)
    | None -> ()

    writer.WriteEndObject()
    writer.Flush()

    Text.Encoding.UTF8.GetString(stream.ToArray()) + "\n"

// ─── Drift detection ────────────────────────────────────────────────────────

let private facetDrift (facet: string) (generated: Set<string>) (committed: Set<string>) =
    [ for name in Set.difference generated committed -> MissingFromCommitted(facet, name)
      for name in Set.difference committed generated -> StaleInCommitted(facet, name) ]

/// Compare the manifest the source implies against the one committed to the
/// repo. SEMANTIC, not byte-wise: what a CI gate must fail on is a contract
/// that no longer describes the app, and only a semantic diff can name the
/// entry responsible. Byte differences with no semantic difference (an older
/// header, hand-written key order, a project mid-migration) are reported
/// separately and never fail the gate — see `EmitOutcome.FormattingDrift`.
let diff (generated: Manifest) (committed: Manifest) : Drift list =
    [ yield! facetDrift "queries" generated.Queries committed.Queries
      yield! facetDrift "msgCases" generated.MsgCases committed.MsgCases

      for KeyValue(query, generatedRowType) in generated.QueryRowTypes do
          match Map.tryFind query committed.QueryRowTypes with
          | Some committedRowType when committedRowType <> generatedRowType ->
              yield RowTypeDiffers(query, committedRowType, generatedRowType)
          | Some _ -> ()
          | None -> yield MissingFromCommitted("queryRowTypes", query)

      for KeyValue(query, _) in committed.QueryRowTypes do
          if not (Map.containsKey query generated.QueryRowTypes) then
              yield StaleInCommitted("queryRowTypes", query)

      if generated.CustomNodeRatio <> committed.CustomNodeRatio then
          yield CustomNodeRatioDiffers(committed.CustomNodeRatio, generated.CustomNodeRatio) ]

// ─── Run ────────────────────────────────────────────────────────────────────

type EmitOptions =
    {
        /// Path to the target .fsproj. Sources are discovered from its directory.
        ProjectPath: string
        /// Where the generated manifest goes. `None` = the conventional
        /// sibling-of-.fsproj path.
        OutPath: string option
        /// Explicit override-tier path. `None` = convention-based discovery.
        OverridesPath: string option
    }

type EmitOutcome =
    {
        Derivation: Derivation
        Merged: Manifest
        Provenance: Provenance
        Json: string
        OutPath: string
        OverridesPath: string option
        /// True when a manifest already exists at `OutPath`.
        CommittedExists: bool
        /// Semantic differences against the committed manifest. Empty when no
        /// manifest is committed yet (there is nothing to have drifted).
        Drift: Drift list
        /// Committed manifest is semantically identical but not byte-identical.
        /// Advisory: regenerating normalises it.
        FormattingDrift: bool
    }

let private projectDirectory (projectPath: string) =
    let full = Path.GetFullPath projectPath

    match Path.GetDirectoryName full with
    | null -> full
    | dir -> dir

/// Derive, merge the override tier, render, and diff against what is
/// committed. Writes nothing — `write` is the separate, explicit step, so
/// `--check` and a real emit run exactly the same derivation.
let run (options: EmitOptions) : Async<EmitOutcome> =
    async {
        let projectDir = projectDirectory options.ProjectPath
        let checker = FSharpChecker.Create()

        let! derivation = derive checker projectDir

        let derived = toManifest derivation

        let overridesPath =
            match options.OverridesPath with
            | Some explicit when File.Exists explicit -> Some explicit
            | Some _ -> None
            | None -> discoverOverrides projectDir

        let overrides =
            match overridesPath with
            | Some path -> load path
            | None -> empty

        let merged = mergeOverrides derived overrides
        let provenance = provenanceOf derived overrides
        let json = renderJson merged provenance

        let outPath =
            options.OutPath
            |> Option.defaultValue (Path.Combine(projectDir, manifestFileName))

        let committedExists = File.Exists outPath

        let committedText = if committedExists then File.ReadAllText outPath else ""

        let drift =
            if committedExists then
                diff merged (parse committedText)
            else
                []

        return
            { Derivation = derivation
              Merged = merged
              Provenance = provenance
              Json = json
              OutPath = outPath
              OverridesPath = overridesPath
              CommittedExists = committedExists
              Drift = drift
              FormattingDrift = committedExists && List.isEmpty drift && committedText <> json }
    }

/// Write the rendered manifest to `OutPath`. `File.WriteAllText` writes UTF-8
/// without a BOM and does not translate the LF line endings already baked into
/// the rendered string, so the canonical form survives the round-trip on
/// Windows as well as Linux.
let write (outcome: EmitOutcome) : unit =
    File.WriteAllText(outcome.OutPath, outcome.Json)
