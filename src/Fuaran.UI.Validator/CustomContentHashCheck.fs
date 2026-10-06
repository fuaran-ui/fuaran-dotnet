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
//  Detection is a visitor over the shared parse (`Syntax`), recognising both
//  construction shapes: `Fuaran.custom` and the `NodeKind.Custom { ... }`
//  record the generated case carries.
// ============================================================================


open System.Security.Cryptography
open System.Text
open FSharp.Compiler.Syntax
open Fuaran.UI.Validator.Findings
open Fuaran.UI.Validator.Syntax

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


/// Props key-set extraction. `Map.empty` → `Some []`; `Map.ofList […]` /
/// `Map.ofSeq […]` / `dict […]` / `readOnlyDict […]` over a literal list of
/// `("key", _)` tuples with literal string keys → `Some keys`. Anything else
/// → `None` (unresolvable; the rule conservatively skips the site).
let private propKeys (expr: SynExpr) : string list option =
    let e = unwrap expr

    match leafIdent e with
    | Some(prefix, "empty", _) when not prefix.IsEmpty && List.last prefix = "Map" -> Some []
    | _ ->
        let head, args = flattenApp e

        let isMapCtor =
            match leafIdent head with
            | Some(prefix, ("ofList" | "ofSeq"), _) when not prefix.IsEmpty && List.last prefix = "Map" -> true
            | Some(_, ("dict" | "readOnlyDict"), _) -> true
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
                | Some(_, "NodeId", _), [ arg ] -> literalString arg
                | _ when args.IsEmpty -> literalString item
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
    | Some(_, "Some", _), [ recordArg ] ->
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
                | "Strictness", Some fe -> strictness <- leafIdent (unwrap fe) |> Option.map (fun (_, leaf, _) -> leaf)
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

/// The record form's `ExposedNodeIds: string list option` — `None` is the
/// empty declaration.
let private exposedIdsOption (expr: SynExpr) : string list option =
    if isExplicitNone expr then
        Some []
    else
        applied "Some" expr |> Option.bind exposedIds

/// A Custom construction carrying its body shape: `Fuaran.custom id moduleId
/// componentId props hash exposed`, or `NodeKind.Custom { ModuleId = ...;
/// ComponentId = ...; Props = ...; ContentHash = ...; ExposedNodeIds = ... }`.
let private customSite (file: string) (head: SynExpr) (args: SynExpr list) : CustomSite option =
    let location = mkLocation file head.Range

    if isQualified "Fuaran" "custom" head then
        match args with
        | _ :: moduleArg :: componentArg :: propsArg :: hashArg :: exposedArg :: _ ->
            Some
                { ModuleId = literalString moduleArg
                  ComponentId = literalString componentArg
                  PropKeys = propKeys propsArg
                  ExposedIds = exposedIds exposedArg
                  Hash = parseContentHash hashArg
                  Location = location }
        | _ -> None
    elif isQualified "NodeKind" "Custom" head then
        match args with
        | [ spec ] when
            (match unwrap spec with
             | SynExpr.Record _ -> true
             | _ -> false)
            ->
            let field name = fieldValue name spec

            Some
                { ModuleId = field "ModuleId" |> Option.bind literalString
                  ComponentId = field "ComponentId" |> Option.bind literalString
                  PropKeys = field "Props" |> Option.bind propKeys
                  ExposedIds = field "ExposedNodeIds" |> Option.bind exposedIdsOption
                  Hash = field "ContentHash" |> Option.bind parseContentHash
                  Location = location }
        | _ -> None
    else
        None

let private looksLikeHexDigest (hash: string) =
    hash.Length = 64 && hash |> Seq.forall System.Uri.IsHexDigit

/// Public entry — walks the supplied source files and returns findings.
let check (sources: ParsedSource list) : Finding list =
    let allSites = sources |> collectApps customSite

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


    findings
