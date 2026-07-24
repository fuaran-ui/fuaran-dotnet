module Fuaran.UI.Validator.CustomContentHashCheck

// ============================================================================
//  Build-time `NodeKind.Custom` content-hash check (Phase 134).
//
//  Phase 70 shipped the `contentHash` + `HashStrictness` surfaces but left
//  the hash itself for consumers to set BY HAND (e.g. a `"…HeatmapTab.v1"`
//  sentinel string with `AdvisoryWarning` strictness). A hand-set hash is
//  advisory by construction — nothing mechanical relates it to the body it
//  claims to fingerprint, so drift is invisible.
//
//  This check makes the relationship mechanical. It computes a deterministic
//  SHA-256 over the Custom body's *declared shape* — never its runtime
//  values — and compares it to the hand-set `Hash`:
//
//    - FUARAN062 CustomContentHashStale: the hand-set `Hash` disagrees with
//      the computed body-shape hash. Severity is governed by `Strictness`:
//      `Enforced` → Error (fails the build — the mechanical contract);
//      `StrictReplay` / `AdvisoryWarning` → Warning (the advisory posture,
//      surfacing the drift without breaking the build). The finding carries
//      the computed hash in its `Suggestion` so the author can paste it in —
//      the migration mechanism from hand-set sentinels to computed hashes.
//
//  The "body shape" the hash covers (the load-bearing definition):
//
//      fuaran-custom-body-shape:v1\n
//      moduleId=<moduleId>\n
//      componentId=<componentId>\n
//      props=<sorted prop keys, comma-joined>\n
//      exposed=<sorted exposedNodeIds, comma-joined>
//
//  SHA-256 of the UTF-8 bytes, lower-case hex. Prop *keys* (the schema), not
//  prop *values* (runtime); both key and id lists are sorted so the hash is
//  insensitive to declaration order — deterministic + replay-stable. The
//  algorithm is reproduced verbatim in `docs/migrations/134-…` so any host
//  (incl. the TS reference implementation) can compute the same digest.
//
//  Conservative shape, like the sibling Custom-health checks: the rule fires
//  only when the body shape is *statically resolvable* from the construction
//  site — literal moduleId / componentId, a `Map.empty` / `Map.ofList […]`
//  props with literal string keys, a literal `[ NodeId "…"; … ]` exposed-id
//  list, and a literal `Some { … Hash = "…"; Strictness = … }`. Anything
//  built from a let-bound variable or a function call is treated as
//  unverifiable and skipped (no false positives). A `SHA256` algorithm is
//  required to verify; other algorithms are left for a future phase.
//
//  Detection lives in a narrow AST walker following the CustomHealthCheck /
//  LocalBindingCheck precedent — independent lexical scope from the main
//  Fuaran.X smart-ctor walker.
// ============================================================================

open System.IO
open System.Security.Cryptography
open System.Text
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Fuaran.UI.Validator.Findings

let codeFUARAN062 = "FUARAN062"

/// Canonical body-shape hash. PURE + host-portable — the validator and any
/// consumer-side build target compute the same digest from the same inputs.
/// `propKeys` is the props *schema* (keys only); `exposedNodeIds` is the
/// declared interior-id list. Both are sorted internally, so callers need
/// not pre-sort. Returns lower-case hex SHA-256.
let computeBodyShapeHash
    (moduleId: string)
    (componentId: string)
    (propKeys: string list)
    (exposedNodeIds: string list)
    : string =
    let canonical =
        String.concat
            "\n"
            [ "fuaran-custom-body-shape:v1"
              "moduleId=" + moduleId
              "componentId=" + componentId
              "props=" + (propKeys |> List.sort |> String.concat ",")
              "exposed=" + (exposedNodeIds |> List.sort |> String.concat ",") ]

    use sha = SHA256.Create()

    canonical
    |> Encoding.UTF8.GetBytes
    |> sha.ComputeHash
    |> Array.map (fun b -> b.ToString("x2"))
    |> String.concat ""

/// A parsed, literal `Some { Algorithm = …; Hash = …; Strictness = … }`.
type private HashLiteral =
    {
        Algorithm: string
        Hash: string
        /// The `HashStrictness` DU case name as written (`"Enforced"` /
        /// `"StrictReplay"` / `"AdvisoryWarning"`).
        Strictness: string
    }

/// One statically-classified Custom construction site.
type private CustomSite =
    {
        ModuleId: string option
        ComponentId: string option
        /// `Some keys` when props resolved to a literal key set; `None` when
        /// the props expression is not statically resolvable.
        PropKeys: string list option
        /// `Some ids` when exposedNodeIds resolved to literals; `None` when
        /// not statically resolvable.
        ExposedIds: string list option
        /// `Some hl` when the contentHash arg is a literal `Some { … }`;
        /// `None` when it is `None` or not statically resolvable.
        Hash: HashLiteral option
        Location: Location
    }

let private mkLocation (file: string) (range: range) : Location =
    { File = file
      Line = range.StartLine
      Column = range.StartColumn + 1 }

let private constStringValue (c: SynConst) =
    match c with
    | SynConst.String(text = s) -> Some s
    | _ -> None

let rec private unwrap (e: SynExpr) =
    match e with
    | SynExpr.Paren(expr = e') -> unwrap e'
    | SynExpr.Typed(expr = e') -> unwrap e'
    | _ -> e

let private literalString (expr: SynExpr) : string option =
    match unwrap expr with
    | SynExpr.Const(constant = c) -> constStringValue c
    | _ -> None

let private leafIdent (expr: SynExpr) : (string list * string) option =
    match expr with
    | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) when not ids.IsEmpty ->
        let names = AstWalker.identNames ids
        Some(names |> List.take (names.Length - 1), List.last names)
    | SynExpr.Ident i -> Some([], i.idText)
    | _ -> None

let private (|FuaranCustomCtor|_|) (expr: SynExpr) =
    match leafIdent expr with
    | Some(prefix, "custom") when not prefix.IsEmpty && List.last prefix = "Fuaran" -> Some()
    | _ -> None

let private (|NodeKindCustomCtor|_|) (expr: SynExpr) =
    match leafIdent expr with
    | Some(prefix, "Custom") when not prefix.IsEmpty && List.last prefix = "NodeKind" -> Some()
    | _ -> None

/// Curry-decompose an application chain into (head, args).
let private flattenApp (expr: SynExpr) : SynExpr * SynExpr list =
    let rec loop acc =
        function
        | SynExpr.App(funcExpr = f; argExpr = a) -> loop (a :: acc) f
        | head -> head, acc

    loop [] expr

/// Items of a `[ … ]` / `[| … |]` literal, or `None` when the expression is
/// not a literal list (a variable, a comprehension with generators, etc.).
let private listItems (expr: SynExpr) : SynExpr list option =
    match unwrap expr with
    | SynExpr.ArrayOrList(exprs = items) -> Some items
    | SynExpr.ArrayOrListComputed(expr = inner) ->
        // Only a bare sequential/single-element computed list is literal
        // enough; anything with a generator (`for`/`yield`) is not.
        let rec flatten e =
            match e with
            | SynExpr.Sequential(expr1 = a; expr2 = b) ->
                match flatten a, flatten b with
                | Some xs, Some ys -> Some(xs @ ys)
                | _ -> None
            | SynExpr.ArrayOrList(exprs = items) -> Some items
            | other -> Some [ other ]

        match unwrap inner with
        | SynExpr.Sequential _ as s -> flatten s
        | single -> Some [ single ]
    | _ -> None

/// Props key-set extraction. `Map.empty` → `Some []`; `Map.ofList […]` /
/// `Map.ofSeq […]` / `dict […]` / `readOnlyDict […]` over a literal list of
/// `("key", _)` tuples with literal string keys → `Some keys`. Anything else
/// → `None` (unresolvable; the rule conservatively skips the site).
let private propKeys (expr: SynExpr) : string list option =
    let e = unwrap expr

    match leafIdent e with
    | Some(prefix, "empty") when not prefix.IsEmpty && List.last prefix = "Map" -> Some []
    | _ ->
        let head, args = flattenApp e

        let isMapCtor =
            match leafIdent head with
            | Some(prefix, ("ofList" | "ofSeq")) when not prefix.IsEmpty && List.last prefix = "Map" -> true
            | Some(_, ("dict" | "readOnlyDict")) -> true
            | _ -> false

        if not isMapCtor then
            None
        else
            match args with
            | [ listExpr ] ->
                match listItems listExpr with
                | None -> None
                | Some items ->
                    let keys =
                        items
                        |> List.map (fun item ->
                            match unwrap item with
                            | SynExpr.Tuple(exprs = keyExpr :: _) -> literalString keyExpr
                            | _ -> None)

                    if keys |> List.forall Option.isSome then
                        Some(keys |> List.map Option.get)
                    else
                        None
            | _ -> None

/// exposedNodeIds extraction. `[]` → `Some []`; `[ NodeId "a"; NodeId "b" ]`
/// → `Some ["a"; "b"]`. Anything else → `None`.
let private exposedIds (expr: SynExpr) : string list option =
    match listItems expr with
    | None -> None
    | Some items ->
        let ids =
            items
            |> List.map (fun item ->
                let head, args = flattenApp (unwrap item)

                match leafIdent head, args with
                | Some(_, "NodeId"), [ arg ] -> literalString arg
                | _ -> None)

        if ids |> List.forall Option.isSome then
            Some(ids |> List.map Option.get)
        else
            None

/// Parse a literal `Some { Algorithm = …; Hash = …; Strictness = … }` content
/// hash. `None` / a non-literal expression → `None` (the site carries no
/// verifiable hand-set hash).
let private parseContentHash (expr: SynExpr) : HashLiteral option =
    let e = unwrap expr
    let head, args = flattenApp e

    match leafIdent head, args with
    | Some(_, "Some"), [ recordArg ] ->
        match unwrap recordArg with
        | SynExpr.Record(recordFields = fields) ->
            let mutable algorithm = "SHA256"
            let mutable hash = None
            let mutable strictness = None

            for SynExprRecordField(fieldName = (SynLongIdent(id = ids), _); expr = fieldExpr) in fields do
                let name = if ids.IsEmpty then "" else (List.last ids).idText

                match name, fieldExpr with
                | "Algorithm", Some fe -> literalString fe |> Option.iter (fun s -> algorithm <- s)
                | "Hash", Some fe -> hash <- literalString fe
                | "Strictness", Some fe -> strictness <- leafIdent fe |> Option.map snd
                | _ -> ()

            match hash, strictness with
            | Some h, Some s ->
                Some
                    { Algorithm = algorithm
                      Hash = h
                      Strictness = s }
            | _ -> None
        | _ -> None
    | _ -> None

let private classifyFuaranCustom (file: string) (loc: range) (args: SynExpr list) : CustomSite option =
    match args with
    | _ :: moduleArg :: componentArg :: propsArg :: hashArg :: exposedArg :: _ ->
        Some
            { ModuleId = literalString moduleArg
              ComponentId = literalString componentArg
              PropKeys = propKeys propsArg
              ExposedIds = exposedIds exposedArg
              Hash = parseContentHash hashArg
              Location = mkLocation file loc }
    | _ -> None

let private classifyNodeKindCustom (file: string) (loc: range) (args: SynExpr list) : CustomSite option =
    match args with
    | [ SynExpr.Paren(expr = SynExpr.Tuple(exprs = items)) ]
    | [ SynExpr.Tuple(exprs = items) ] ->
        match items with
        | moduleArg :: componentArg :: propsArg :: hashArg :: exposedArg :: _ ->
            Some
                { ModuleId = literalString moduleArg
                  ComponentId = literalString componentArg
                  PropKeys = propKeys propsArg
                  ExposedIds = exposedIds exposedArg
                  Hash = parseContentHash hashArg
                  Location = mkLocation file loc }
        | _ -> None
    | _ -> None

type private WalkState =
    { File: string
      mutable Sites: CustomSite list }

let rec private walkExpr (state: WalkState) (expr: SynExpr) =
    let head, args = flattenApp expr

    match head with
    | FuaranCustomCtor ->
        match classifyFuaranCustom state.File head.Range args with
        | Some site -> state.Sites <- site :: state.Sites
        | None -> ()

        args |> List.iter (walkExpr state)
    | NodeKindCustomCtor ->
        match classifyNodeKindCustom state.File head.Range args with
        | Some site -> state.Sites <- site :: state.Sites
        | None -> ()

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

let private walkFile (checker: FSharpChecker) (file: string) =
    async {
        let source = File.ReadAllText file
        let! parseResult = parseFile checker file source
        let state = { File = file; Sites = [] }

        match parseResult.ParseTree with
        | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) -> modules |> List.iter (walkModule state)
        | ParsedInput.SigFile _ -> ()

        return state.Sites |> List.rev
    }

/// True when `hash` is a canonical 64-char lower/upper-case hex string —
/// i.e. it at least *looks* like a SHA-256 digest rather than a hand-set
/// sentinel (e.g. `"Individual.HeatmapTab.v1"`).
let private looksLikeHexDigest (hash: string) =
    hash.Length = 64 && hash |> Seq.forall System.Uri.IsHexDigit

/// Public entry — walks the supplied source files and returns findings.
let checkSources (checker: FSharpChecker) (files: string list) : Async<Finding list> =
    async {
        let! perFile = files |> List.map (walkFile checker) |> Async.Parallel
        let allSites = perFile |> Array.collect List.toArray |> Array.toList

        let findings =
            allSites
            |> List.choose (fun site ->
                match site.ModuleId, site.ComponentId, site.PropKeys, site.ExposedIds, site.Hash with
                | Some moduleId, Some componentId, Some keys, Some ids, Some hl when
                    hl.Algorithm.ToUpperInvariant() = "SHA256"
                    ->
                    let computed = computeBodyShapeHash moduleId componentId keys ids

                    if System.String.Equals(computed, hl.Hash, System.StringComparison.OrdinalIgnoreCase) then
                        None
                    else
                        let severity = if hl.Strictness = "Enforced" then Error else Warning

                        let sentinelNote =
                            if looksLikeHexDigest hl.Hash then
                                ""
                            else
                                " The current value is not a 64-char hex digest, so it looks like a hand-set sentinel rather than a computed hash."

                        let enforcement =
                            if severity = Error then
                                "build-failing (Strictness = Enforced)"
                            else
                                sprintf
                                    "advisory (Strictness = %s — flip to Enforced to fail the build on drift)"
                                    hl.Strictness

                        let message =
                            sprintf
                                "Custom node %s.%s has a stale contentHash: the declared Hash '%s' disagrees with the build-time SHA-256 over the body's declared shape (props schema + exposedNodeIds + moduleId/componentId).%s This check is %s. Expected computed hash: %s"
                                moduleId
                                componentId
                                hl.Hash
                                sentinelNote
                                enforcement
                                computed

                        create severity codeFUARAN062 site.Location message
                        |> withRecovery
                            [ "Hash" ]
                            (Some(
                                sprintf
                                    "Set contentHash.Hash = \"%s\" (or regenerate after the body shape stabilises)."
                                    computed
                            ))
                        |> Some
                | _ -> None)

        return findings
    }
