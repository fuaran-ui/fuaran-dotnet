namespace Fuaran.UI.OpStream.Abstractions

open Fuaran.Core
open Fuaran.UI.Types

// ============================================================================
//  Elicitation envelope — question-as-UI with a typed answer contract.
//
//  An elicitation is a question posed AS a live Fuaran tree: the asker emits a
//  canonical Node tree plus a declared **answer contract** — which nodes' state
//  entries constitute the answer, each typed by a `Fuaran.Core.ValueSpace` —
//  and the interaction resolves to exactly one typed outcome:
//  `Answered` (a canonical typed JSON answer object conforming to the
//  contract) / `Declined` / `TimedOut` / `Superseded`. Never prose to
//  re-parse.
//
//  Wire shape (WIRE_FORMAT.md §18 — a new, additive top-level artefact; the
//  Node wire form of §§1–16 is embedded unchanged; canonical §2 encoding —
//  Ordinal-sorted keys, canonical floats, omit-when-absent optionals):
//
//    { "$elicitation": "1",                       // format-version tag
//      "contract": { "fields": [ {
//          "name":     "<answer key>",
//          "nodeId":   "<id of a node in tree>",
//          "required": true|false,
//          "space":    { "$type": "intRange"|"floatRange"|"stringLen"
//                                 |"enum"|"anyString", ... },
//          "stateKey": "<state key the node's binding commits to>" }, … ] },
//      "default":   { "<name>": <scalar>, … },    // optional; must conform
//      "id":        "<elicitation id>",
//      "timeoutMs": <int ≥ 1>,                    // optional; DATA only —
//                                                 //   the host's clock decides
//      "tree":      <Node> }
//
//  Outcome wire shape:
//
//    { "$type": "Answered",  "answer": { … }, "elicitationId": "…" }
//    { "$type": "Declined",  "elicitationId": "…" }
//    { "$type": "TimedOut",  "elicitationId": "…" }
//    { "$type": "Superseded", "by": "…"?, "elicitationId": "…" }
//
//  The value-space vocabulary and its wire form are REUSED from
//  `Fuaran.Core.Function` (`ValueSpace` + the capability codec's `$type`
//  tags), per the Phase 465 answer-contract design note — one space
//  vocabulary estate-wide. String spaces validate via `Space.validate`
//  verbatim; numeric spaces apply the same closed-interval rule on the typed
//  JSON number.
//
//  Strictness (default-deny by shape): every object position in this artefact
//  rejects undeclared keys (`UNDECLARED_FIELD`) — the envelope is a protocol
//  artefact, not a forward-compat carrier; its evolution is explicit via the
//  `$elicitation` version tag. Decode is fail-fast with one structured
//  `DecodeError` (the §6 envelope shape), in the deterministic order the spec
//  pins, so every conformant host surfaces the same first error.
//
//  No clock is read anywhere in this module: `timeoutMs` travels as data and
//  `TimedOut` is an outcome a HOST dispatches when its own clock says so.
// ============================================================================

/// One declared answer field: the answer-object key (`Name`), the tree node
/// whose committed state carries the value (`NodeId` + `StateKey` — the
/// Phase 62 `Binding.Local` commit target), the value space it must inhabit,
/// and whether an answer may omit it.
type AnswerField =
    { Name: string
      NodeId: NodeId
      StateKey: string
      Space: ValueSpace
      Required: bool }

/// The declared answer contract: which fields constitute a conforming answer.
/// Non-empty by construction on the wire (`CONTRACT_EMPTY`).
type AnswerContract = { Fields: AnswerField list }

/// A typed answer value — the JSON scalar shapes a `ValueSpace` ranges over.
/// (JSON has one number type: a whole-valued number always decodes as `Int`;
/// `floatRange` accepts either numeric case.)
[<RequireQualifiedAccess>]
type AnswerValue =
    | Int of int
    | Float of float
    | Str of string

/// The elicitation envelope: a question posed as a live tree + the typed
/// answer contract + optional timeout (data only) and default answer.
type ElicitationEnvelope =
    { ElicitationId: string
      Tree: Node<obj>
      Contract: AnswerContract
      TimeoutMs: int option
      Default: Map<string, AnswerValue> option }

/// The closed outcome set. `Answered` carries the canonical typed answer;
/// `Superseded` optionally names the elicitation that replaced this one.
[<RequireQualifiedAccess>]
type ElicitationOutcome =
    | Answered of answer: Map<string, AnswerValue>
    | Declined
    | TimedOut
    | Superseded of by: string option

/// An outcome on the wire, correlated to its elicitation by id.
type ElicitationOutcomeEnvelope =
    { ElicitationId: string
      Outcome: ElicitationOutcome }

/// The elicitation-specific error codes (WIRE_FORMAT.md §18.5). They ride the
/// standard §6 `DecodeError` envelope; structural failures reuse the §6 codes
/// (`INVALID_JSON` / `MISSING_FIELD` / `WRONG_TYPE` / `UNKNOWN_DU_CASE`).
[<RequireQualifiedAccess>]
module ElicitationErrorCode =
    [<Literal>]
    let UNSUPPORTED_VERSION = "UNSUPPORTED_VERSION"

    [<Literal>]
    let UNDECLARED_FIELD = "UNDECLARED_FIELD"

    [<Literal>]
    let CONTRACT_EMPTY = "CONTRACT_EMPTY"

    [<Literal>]
    let CONTRACT_DUPLICATE_FIELD = "CONTRACT_DUPLICATE_FIELD"

    [<Literal>]
    let CONTRACT_UNKNOWN_NODE = "CONTRACT_UNKNOWN_NODE"

    [<Literal>]
    let ANSWER_MISSING_FIELD = "ANSWER_MISSING_FIELD"

    [<Literal>]
    let ANSWER_UNDECLARED_FIELD = "ANSWER_UNDECLARED_FIELD"

    [<Literal>]
    let ANSWER_TYPE_MISMATCH = "ANSWER_TYPE_MISMATCH"

    [<Literal>]
    let ANSWER_OUT_OF_SPACE = "ANSWER_OUT_OF_SPACE"

    [<Literal>]
    let DEFAULT_NONCONFORMANT = "DEFAULT_NONCONFORMANT"

module Elicitation =

    module JsonDecode = Fuaran.UI.Ops.JsonDecode

    /// The envelope format-version tag key. `$`-prefixed so it sorts before
    /// every data key under the §2 canonical Ordinal order (reserved per
    /// §2.1).
    [<Literal>]
    let FormatKey = "$elicitation"

    /// The envelope format version this codec produces and accepts.
    [<Literal>]
    let FormatVersion = "1"

    let private err (code: string) (path: string) (message: string) : JsonDecode.DecodeError =
        { Code = code
          Path = path
          Message = message
          ExpectedShape = None }

    /// Re-root an inner decode error under `prefix` (e.g. a tree decode error
    /// at `$.kind` surfaces at `$.tree.kind`).
    let private rePath (prefix: string) (e: JsonDecode.DecodeError) : JsonDecode.DecodeError =
        let inner = e.Path

        let rerooted =
            if inner = "$" then prefix
            elif inner.StartsWith "$" then prefix + inner.Substring 1
            else prefix + "." + inner

        { e with Path = rerooted }

    let private fieldOpt (name: string) (fields: (string * JVal) list) : JVal option =
        fields |> List.tryPick (fun (k, v) -> if k = name then Some v else None)

    /// First key of `fields` (document order) not in `allowed` — the
    /// default-deny-by-shape probe every object position runs.
    let private firstUndeclared (allowed: string list) (fields: (string * JVal) list) : string option =
        fields
        |> List.tryPick (fun (k, _) -> if List.contains k allowed then None else Some k)

    let private requireNonEmptyStr
        (path: string)
        (name: string)
        (fields: (string * JVal) list)
        : Result<string, JsonDecode.DecodeError> =
        match fieldOpt name fields with
        | None -> Error(err "MISSING_FIELD" path (sprintf "missing required field '%s'" name))
        | Some(JStr s) when s <> "" -> Ok s
        | Some(JStr _) -> Error(err "WRONG_TYPE" path (sprintf "'%s' must be a non-empty string" name))
        | Some _ -> Error(err "WRONG_TYPE" path (sprintf "'%s' must be a string" name))

    // ── Value spaces (the Fuaran.Core.Function vocabulary, capability wire form) ──

    let private encodeSpace (s: ValueSpace) : JVal =
        match s with
        | IntRange(lo, hi) -> Canon.typed "intRange" [ "max", JInt hi; "min", JInt lo ]
        | FloatRange(lo, hi) -> Canon.typed "floatRange" [ "max", JFloat hi; "min", JFloat lo ]
        | StringLen(lo, hi) -> Canon.typed "stringLen" [ "max", JInt hi; "min", JInt lo ]
        | Enum xs -> Canon.typed "enum" [ "values", JArr(xs |> List.map JStr) ]
        | AnyString -> Canon.typed "anyString" []

    let private decodeSpace (path: string) (jv: JVal) : Result<ValueSpace, JsonDecode.DecodeError> =
        match jv with
        | JObj fields ->
            match fieldOpt "$type" fields with
            | None -> Error(err "MISSING_FIELD" (path + ".$type") "space must carry a '$type' discriminator")
            | Some(JStr tag) ->
                let strict (allowed: string list) (k: Result<ValueSpace, JsonDecode.DecodeError>) =
                    match firstUndeclared ("$type" :: allowed) fields with
                    | Some stray ->
                        Error(
                            err
                                ElicitationErrorCode.UNDECLARED_FIELD
                                (path + "." + stray)
                                (sprintf "undeclared key '%s' on a '%s' space" stray tag)
                        )
                    | None -> k

                let intBound (name: string) : Result<int, JsonDecode.DecodeError> =
                    match fieldOpt name fields with
                    | Some(JInt v) -> Ok v
                    | Some _ -> Error(err "WRONG_TYPE" (path + "." + name) (sprintf "'%s' must be an integer" name))
                    | None ->
                        Error(err "MISSING_FIELD" (path + "." + name) (sprintf "missing required field '%s'" name))

                let numBound (name: string) : Result<float, JsonDecode.DecodeError> =
                    match fieldOpt name fields with
                    | Some(JInt v) -> Ok(float v)
                    | Some(JFloat v) -> Ok v
                    | Some _ -> Error(err "WRONG_TYPE" (path + "." + name) (sprintf "'%s' must be a number" name))
                    | None ->
                        Error(err "MISSING_FIELD" (path + "." + name) (sprintf "missing required field '%s'" name))

                let ordered (lo: 'a) (hi: 'a) (mk: unit -> ValueSpace) : Result<ValueSpace, JsonDecode.DecodeError> =
                    if hi < lo then
                        Error(err "WRONG_TYPE" path "space bounds must satisfy min <= max")
                    else
                        Ok(mk ())

                match tag with
                | "intRange" ->
                    strict
                        [ "max"; "min" ]
                        (intBound "min"
                         |> Result.bind (fun lo ->
                             intBound "max"
                             |> Result.bind (fun hi -> ordered lo hi (fun () -> IntRange(lo, hi)))))
                | "floatRange" ->
                    strict
                        [ "max"; "min" ]
                        (numBound "min"
                         |> Result.bind (fun lo ->
                             numBound "max"
                             |> Result.bind (fun hi -> ordered lo hi (fun () -> FloatRange(lo, hi)))))
                | "stringLen" ->
                    strict
                        [ "max"; "min" ]
                        (intBound "min"
                         |> Result.bind (fun lo ->
                             intBound "max"
                             |> Result.bind (fun hi -> ordered lo hi (fun () -> StringLen(lo, hi)))))
                | "enum" ->
                    strict
                        [ "values" ]
                        (match fieldOpt "values" fields with
                         | Some(JArr items) when not items.IsEmpty ->
                             let strings =
                                 items
                                 |> List.fold
                                     (fun acc it ->
                                         match acc, it with
                                         | Ok xs, JStr s -> Ok(s :: xs)
                                         | Ok _, _ -> Error()
                                         | Error(), _ -> Error())
                                     (Ok [])

                             match strings with
                             | Ok xs -> Ok(Enum(List.rev xs))
                             | Error() ->
                                 Error(err "WRONG_TYPE" (path + ".values") "'values' must be an array of strings")
                         | Some(JArr _) ->
                             Error(err "WRONG_TYPE" (path + ".values") "'values' must be a non-empty array")
                         | Some _ -> Error(err "WRONG_TYPE" (path + ".values") "'values' must be an array of strings")
                         | None -> Error(err "MISSING_FIELD" (path + ".values") "missing required field 'values'"))
                | "anyString" -> strict [] (Ok AnyString)
                | other ->
                    Error(
                        err "UNKNOWN_DU_CASE" (path + ".$type") (sprintf "unknown value-space discriminator '%s'" other)
                    )
            | Some _ -> Error(err "WRONG_TYPE" (path + ".$type") "'$type' must be a string")
        | _ -> Error(err "WRONG_TYPE" path "space must be a JSON object")

    // ── Answer objects ──────────────────────────────────────────────────────

    /// §18.2 integer classification, identical across hosts: a whole-valued
    /// JSON number within 32-bit signed range IS an integer — JSON has one
    /// number type (`1.0` and `1` denote the same value; the canonical layout
    /// renders it `1`), so classification is by VALUE, never by spelling.
    let private wholeInt32 (f: float) : bool =
        f >= -2147483648.0 && f <= 2147483647.0 && f = floor f

    let private encodeAnswerValue (v: AnswerValue) : JVal =
        match v with
        | AnswerValue.Int i -> JInt i
        | AnswerValue.Float f -> JFloat f
        | AnswerValue.Str s -> JStr s

    let private encodeAnswer (answer: Map<string, AnswerValue>) : JVal =
        JObj(answer |> Map.toList |> List.map (fun (k, v) -> k, encodeAnswerValue v))

    let private decodeAnswerObject
        (basePath: string)
        (jv: JVal)
        : Result<Map<string, AnswerValue>, JsonDecode.DecodeError> =
        match jv with
        | JObj fields ->
            let folded =
                fields
                |> List.fold
                    (fun acc (k, v) ->
                        acc
                        |> Result.bind (fun (m: Map<string, AnswerValue>) ->
                            match v with
                            | JStr s -> Ok(m.Add(k, AnswerValue.Str s))
                            | JInt i -> Ok(m.Add(k, AnswerValue.Int i))
                            // A whole-valued float spelling (e.g. "4.0") is
                            // the same JSON value as "4" — classify by value
                            // so hosts whose parsers cannot see the spelling
                            // (JS has one number type) agree with this one.
                            | JFloat f when wholeInt32 f -> Ok(m.Add(k, AnswerValue.Int(int f)))
                            | JFloat f -> Ok(m.Add(k, AnswerValue.Float f))
                            | _ ->
                                Error(
                                    err
                                        "WRONG_TYPE"
                                        (basePath + "." + k)
                                        "answer values must be JSON scalars (string or number)"
                                )))
                    (Ok Map.empty)

            folded
        | _ -> Error(err "WRONG_TYPE" basePath "answer must be a JSON object")

    // ── Answer conformance (decode-side validation) ─────────────────────────

    /// Validate a typed answer against `contract`, reporting at `basePath`.
    /// Deterministic fail-fast order (§18.4): (1) undeclared keys, in the
    /// answer's Ordinal key order; (2) per contract field, in declaration
    /// order — missing-required, then JSON-type-vs-space, then in-space.
    let validateAnswerAt
        (basePath: string)
        (contract: AnswerContract)
        (answer: Map<string, AnswerValue>)
        : Result<unit, JsonDecode.DecodeError> =
        let declared = contract.Fields |> List.map _.Name |> Set.ofList

        let undeclared =
            answer
            |> Map.toList
            |> List.tryPick (fun (k, _) -> if Set.contains k declared then None else Some k)

        match undeclared with
        | Some stray ->
            Error(
                err
                    ElicitationErrorCode.ANSWER_UNDECLARED_FIELD
                    (basePath + "." + stray)
                    (sprintf "'%s' is not a declared answer field" stray)
            )
        | None ->
            let checkField (field: AnswerField) : Result<unit, JsonDecode.DecodeError> =
                let path = basePath + "." + field.Name

                match Map.tryFind field.Name answer with
                | None when field.Required ->
                    Error(
                        err
                            ElicitationErrorCode.ANSWER_MISSING_FIELD
                            path
                            (sprintf "required answer field '%s' is missing" field.Name)
                    )
                | None -> Ok()
                | Some value ->
                    let mismatch (expected: string) =
                        Error(
                            err
                                ElicitationErrorCode.ANSWER_TYPE_MISMATCH
                                path
                                (sprintf "'%s' must be %s for its declared space" field.Name expected)
                        )

                    let outOfSpace (detail: string) =
                        Error(
                            err
                                ElicitationErrorCode.ANSWER_OUT_OF_SPACE
                                path
                                (sprintf "'%s' is outside its declared space: %s" field.Name detail)
                        )

                    match field.Space, value with
                    | IntRange(lo, hi), AnswerValue.Int v ->
                        if v >= lo && v <= hi then
                            Ok()
                        else
                            outOfSpace (sprintf "%d is not in [%d, %d]" v lo hi)
                    | IntRange _, _ -> mismatch "an integer"
                    | FloatRange(lo, hi), AnswerValue.Float v ->
                        if v >= lo && v <= hi then
                            Ok()
                        else
                            outOfSpace (sprintf "%s is not in [%s, %s]" (string v) (string lo) (string hi))
                    | FloatRange(lo, hi), AnswerValue.Int v ->
                        let f = float v

                        if f >= lo && f <= hi then
                            Ok()
                        else
                            outOfSpace (sprintf "%d is not in [%s, %s]" v (string lo) (string hi))
                    | FloatRange _, _ -> mismatch "a number"
                    // String spaces delegate to the reused Fuaran.Core
                    // validation verbatim (the design-note reuse seam).
                    | (StringLen _ | Enum _ | AnyString), AnswerValue.Str s ->
                        if Space.validate field.Space s then
                            Ok()
                        else
                            outOfSpace "the string violates its declared space"
                    | (StringLen _ | Enum _ | AnyString), _ -> mismatch "a string"

            contract.Fields
            |> List.fold (fun acc f -> acc |> Result.bind (fun () -> checkField f)) (Ok())

    /// Validate an answer against a contract at the standard `$.answer` root —
    /// the gate a resolution runtime runs before an `Answered` outcome reaches
    /// the agent.
    let validateAnswer (contract: AnswerContract) (answer: Map<string, AnswerValue>) =
        validateAnswerAt "$.answer" contract answer

    // ── Contract codec ──────────────────────────────────────────────────────

    let private encodeField (f: AnswerField) : JVal =
        let (NodeId rawId) = f.NodeId

        JObj
            [ "name", JStr f.Name
              "nodeId", JStr rawId
              "required", JBool f.Required
              "space", encodeSpace f.Space
              "stateKey", JStr f.StateKey ]

    let private encodeContract (c: AnswerContract) : JVal =
        JObj [ "fields", JArr(c.Fields |> List.map encodeField) ]

    /// Canonical wire form of a bare answer contract (the §18.1 contract
    /// object) — for hosts that carry the contract separately from a full
    /// envelope (e.g. a resolution runtime pairing an outcome with its
    /// pending elicitation).
    let encodeContractJson (c: AnswerContract) : string = Canon.render (encodeContract c)

    /// Decode a bare canonical answer object (`{"<name>": <scalar>, …}`),
    /// reporting at the standard `$.answer` root — the typed input
    /// `validateAnswer` consumes.
    let decodeAnswerJson (json: string) : Result<Map<string, AnswerValue>, JsonDecode.DecodeError> =
        match Json.parse json with
        | Error m -> Error(err "INVALID_JSON" "$.answer" ("answer is not valid JSON: " + m))
        | Ok jv -> decodeAnswerObject "$.answer" jv

    /// Decode a contract at `basePath`. `nodeExists` is the tree-membership
    /// probe (`CONTRACT_UNKNOWN_NODE`) — the standalone answer-document path
    /// (§18.4) passes an always-true probe because it carries no tree.
    let private decodeContract
        (basePath: string)
        (nodeExists: string -> bool)
        (jv: JVal)
        : Result<AnswerContract, JsonDecode.DecodeError> =
        match jv with
        | JObj cFields ->
            match firstUndeclared [ "fields" ] cFields with
            | Some stray ->
                Error(
                    err
                        ElicitationErrorCode.UNDECLARED_FIELD
                        (basePath + "." + stray)
                        (sprintf "undeclared key '%s' on the contract" stray)
                )
            | None ->
                match fieldOpt "fields" cFields with
                | None -> Error(err "MISSING_FIELD" (basePath + ".fields") "missing required field 'fields'")
                | Some(JArr []) ->
                    Error(
                        err
                            ElicitationErrorCode.CONTRACT_EMPTY
                            (basePath + ".fields")
                            "an answer contract must declare at least one field"
                    )
                | Some(JArr items) ->
                    let decodeFieldAt (i: int) (item: JVal) : Result<AnswerField, JsonDecode.DecodeError> =
                        let path = sprintf "%s.fields[%d]" basePath i

                        match item with
                        | JObj fFields ->
                            match firstUndeclared [ "name"; "nodeId"; "required"; "space"; "stateKey" ] fFields with
                            | Some stray ->
                                Error(
                                    err
                                        ElicitationErrorCode.UNDECLARED_FIELD
                                        (path + "." + stray)
                                        (sprintf "undeclared key '%s' on an answer field" stray)
                                )
                            | None ->
                                requireNonEmptyStr (path + ".name") "name" fFields
                                |> Result.bind (fun name ->
                                    requireNonEmptyStr (path + ".nodeId") "nodeId" fFields
                                    |> Result.bind (fun nodeId ->
                                        if not (nodeExists nodeId) then
                                            Error(
                                                err
                                                    ElicitationErrorCode.CONTRACT_UNKNOWN_NODE
                                                    (path + ".nodeId")
                                                    (sprintf "'%s' names no node in the elicitation tree" nodeId)
                                            )
                                        else
                                            (match fieldOpt "required" fFields with
                                             | Some(JBool b) -> Ok b
                                             | Some _ ->
                                                 Error(
                                                     err
                                                         "WRONG_TYPE"
                                                         (path + ".required")
                                                         "'required' must be a boolean"
                                                 )
                                             | None ->
                                                 Error(
                                                     err
                                                         "MISSING_FIELD"
                                                         (path + ".required")
                                                         "missing required field 'required'"
                                                 ))
                                            |> Result.bind (fun required ->
                                                (match fieldOpt "space" fFields with
                                                 | Some spaceJv -> decodeSpace (path + ".space") spaceJv
                                                 | None ->
                                                     Error(
                                                         err
                                                             "MISSING_FIELD"
                                                             (path + ".space")
                                                             "missing required field 'space'"
                                                     ))
                                                |> Result.bind (fun space ->
                                                    requireNonEmptyStr (path + ".stateKey") "stateKey" fFields
                                                    |> Result.map (fun stateKey ->
                                                        { Name = name
                                                          NodeId = NodeId nodeId
                                                          StateKey = stateKey
                                                          Space = space
                                                          Required = required })))))
                        | _ -> Error(err "WRONG_TYPE" path "answer field must be a JSON object")

                    let rec go (i: int) (seen: Set<string>) (acc: AnswerField list) (rest: JVal list) =
                        match rest with
                        | [] -> Ok { Fields = List.rev acc }
                        | item :: tail ->
                            decodeFieldAt i item
                            |> Result.bind (fun f ->
                                if Set.contains f.Name seen then
                                    Error(
                                        err
                                            ElicitationErrorCode.CONTRACT_DUPLICATE_FIELD
                                            (sprintf "%s.fields[%d].name" basePath i)
                                            (sprintf "duplicate answer field name '%s'" f.Name)
                                    )
                                else
                                    go (i + 1) (Set.add f.Name seen) (f :: acc) tail)

                    go 0 Set.empty [] items
                | Some _ -> Error(err "WRONG_TYPE" (basePath + ".fields") "'fields' must be an array")
        | _ -> Error(err "WRONG_TYPE" basePath "contract must be a JSON object")

    // ── Envelope codec ──────────────────────────────────────────────────────

    /// Encode an elicitation envelope to canonical wire bytes. Fails only when
    /// the tree is not wire-representable (the same boundary the canonical
    /// parser enforces).
    let encodeEnvelope (env: ElicitationEnvelope) : Result<string, JsonDecode.DecodeError> =
        match Json.parse (CanonicalJson.encodeNode env.Tree) with
        | Error m -> Error(err "WRONG_TYPE" "$.tree" ("tree is not wire-representable: " + m))
        | Ok treeJv ->
            let fields =
                [ yield FormatKey, JStr FormatVersion
                  yield "contract", encodeContract env.Contract

                  match env.Default with
                  | Some d -> yield "default", encodeAnswer d
                  | None -> ()

                  yield "id", JStr env.ElicitationId

                  match env.TimeoutMs with
                  | Some t -> yield "timeoutMs", JInt t
                  | None -> ()

                  yield "tree", treeJv ]

            Ok(Canon.render (JObj fields))

    /// Decode + validate an elicitation envelope (fail-fast, §18.3 order):
    /// version tag → id → tree (standard §3.1 decode, errors re-rooted under
    /// `$.tree`) → contract (structure, duplicates, node membership) →
    /// timeoutMs → default conformance.
    let decodeEnvelope (json: string) : Result<ElicitationEnvelope, JsonDecode.DecodeError> =
        match Json.parse json with
        | Error m -> Error(err "INVALID_JSON" "$" ("input is not valid JSON: " + m))
        | Ok(JObj fields) ->
            match firstUndeclared [ FormatKey; "contract"; "default"; "id"; "timeoutMs"; "tree" ] fields with
            | Some stray ->
                Error(
                    err
                        ElicitationErrorCode.UNDECLARED_FIELD
                        ("$." + stray)
                        (sprintf "undeclared key '%s' on the elicitation envelope" stray)
                )
            | None ->
                (match fieldOpt FormatKey fields with
                 | None ->
                     Error(err "MISSING_FIELD" ("$." + FormatKey) (sprintf "missing required field '%s'" FormatKey))
                 | Some(JStr v) when v = FormatVersion -> Ok()
                 | Some(JStr v) ->
                     Error(
                         err
                             ElicitationErrorCode.UNSUPPORTED_VERSION
                             ("$." + FormatKey)
                             (sprintf
                                 "unsupported elicitation format version '%s' (this codec accepts '%s')"
                                 v
                                 FormatVersion)
                     )
                 | Some _ -> Error(err "WRONG_TYPE" ("$." + FormatKey) (sprintf "'%s' must be a string" FormatKey)))
                |> Result.bind (fun () -> requireNonEmptyStr "$.id" "id" fields)
                |> Result.bind (fun id ->
                    (match fieldOpt "tree" fields with
                     | None -> Error(err "MISSING_FIELD" "$.tree" "missing required field 'tree'")
                     | Some treeJv ->
                         match JsonDecode.decodeNodeObj (Canon.render treeJv) with
                         | Ok tree -> Ok tree
                         | Error e -> Error(rePath "$.tree" e))
                    |> Result.bind (fun tree ->
                        let ids =
                            Fuaran.UI.Ops.Introspect.allNodeIds tree
                            |> List.map (fun (NodeId raw) -> raw)
                            |> Set.ofList

                        (match fieldOpt "contract" fields with
                         | None -> Error(err "MISSING_FIELD" "$.contract" "missing required field 'contract'")
                         | Some c -> decodeContract "$.contract" (fun nodeId -> Set.contains nodeId ids) c)
                        |> Result.bind (fun contract ->
                            (match fieldOpt "timeoutMs" fields with
                             | None -> Ok None
                             | Some(JInt t) when t >= 1 -> Ok(Some t)
                             | Some _ -> Error(err "WRONG_TYPE" "$.timeoutMs" "'timeoutMs' must be an integer >= 1"))
                            |> Result.bind (fun timeoutMs ->
                                (match fieldOpt "default" fields with
                                 | None -> Ok None
                                 | Some d ->
                                     decodeAnswerObject "$.default" d
                                     |> Result.bind (fun answer ->
                                         match validateAnswerAt "$.default" contract answer with
                                         | Ok() -> Ok(Some answer)
                                         | Error inner ->
                                             Error(
                                                 err
                                                     ElicitationErrorCode.DEFAULT_NONCONFORMANT
                                                     inner.Path
                                                     (sprintf
                                                         "default answer is non-conformant (%s): %s"
                                                         inner.Code
                                                         inner.Message)
                                             )))
                                |> Result.map (fun defaultAnswer ->
                                    { ElicitationId = id
                                      Tree = tree
                                      Contract = contract
                                      TimeoutMs = timeoutMs
                                      Default = defaultAnswer })))))
        | Ok _ -> Error(err "WRONG_TYPE" "$" "elicitation envelope must be a JSON object")

    // ── Outcome codec ───────────────────────────────────────────────────────

    /// Encode an outcome to canonical wire bytes. Total — no embedded tree.
    let encodeOutcome (env: ElicitationOutcomeEnvelope) : string =
        let fields =
            match env.Outcome with
            | ElicitationOutcome.Answered answer ->
                [ "$type", JStr "Answered"
                  "answer", encodeAnswer answer
                  "elicitationId", JStr env.ElicitationId ]
            | ElicitationOutcome.Declined -> [ "$type", JStr "Declined"; "elicitationId", JStr env.ElicitationId ]
            | ElicitationOutcome.TimedOut -> [ "$type", JStr "TimedOut"; "elicitationId", JStr env.ElicitationId ]
            | ElicitationOutcome.Superseded by ->
                [ yield "$type", JStr "Superseded"

                  match by with
                  | Some b -> yield "by", JStr b
                  | None -> ()

                  yield "elicitationId", JStr env.ElicitationId ]

        Canon.render (JObj fields)

    /// Decode an outcome envelope. Contract conformance of an `Answered`
    /// answer is NOT checked here (the outcome does not carry the contract) —
    /// a resolution runtime pairs the outcome with its pending elicitation and
    /// runs `validateAnswer` before the answer reaches the agent (§18.4).
    let decodeOutcome (json: string) : Result<ElicitationOutcomeEnvelope, JsonDecode.DecodeError> =
        match Json.parse json with
        | Error m -> Error(err "INVALID_JSON" "$" ("input is not valid JSON: " + m))
        | Ok(JObj fields) ->
            (match fieldOpt "$type" fields with
             | None -> Error(err "MISSING_FIELD" "$.$type" "outcome must carry a '$type' discriminator")
             | Some(JStr tag) -> Ok tag
             | Some _ -> Error(err "WRONG_TYPE" "$.$type" "'$type' must be a string"))
            |> Result.bind (fun tag ->
                let strict (allowed: string list) (k: Result<'a, JsonDecode.DecodeError>) =
                    match firstUndeclared ([ "$type"; "elicitationId" ] @ allowed) fields with
                    | Some stray ->
                        Error(
                            err
                                ElicitationErrorCode.UNDECLARED_FIELD
                                ("$." + stray)
                                (sprintf "undeclared key '%s' on a '%s' outcome" stray tag)
                        )
                    | None -> k

                let elicitationId () =
                    requireNonEmptyStr "$.elicitationId" "elicitationId" fields

                match tag with
                | "Answered" ->
                    strict
                        [ "answer" ]
                        (elicitationId ()
                         |> Result.bind (fun id ->
                             match fieldOpt "answer" fields with
                             | None -> Error(err "MISSING_FIELD" "$.answer" "missing required field 'answer'")
                             | Some a ->
                                 decodeAnswerObject "$.answer" a
                                 |> Result.map (fun answer ->
                                     { ElicitationId = id
                                       Outcome = ElicitationOutcome.Answered answer })))
                | "Declined" ->
                    strict
                        []
                        (elicitationId ()
                         |> Result.map (fun id ->
                             { ElicitationId = id
                               Outcome = ElicitationOutcome.Declined }))
                | "TimedOut" ->
                    strict
                        []
                        (elicitationId ()
                         |> Result.map (fun id ->
                             { ElicitationId = id
                               Outcome = ElicitationOutcome.TimedOut }))
                | "Superseded" ->
                    strict
                        [ "by" ]
                        (elicitationId ()
                         |> Result.bind (fun id ->
                             (match fieldOpt "by" fields with
                              | None -> Ok None
                              | Some(JStr b) when b <> "" -> Ok(Some b)
                              | Some _ -> Error(err "WRONG_TYPE" "$.by" "'by' must be a non-empty string"))
                             |> Result.map (fun by ->
                                 { ElicitationId = id
                                   Outcome = ElicitationOutcome.Superseded by })))
                | other -> Error(err "UNKNOWN_DU_CASE" "$.$type" (sprintf "unknown outcome discriminator '%s'" other)))
        | Ok _ -> Error(err "WRONG_TYPE" "$" "outcome must be a JSON object")

    // ── Answer-conformance document (the fixture-harness operation) ─────────

    /// Validate a `{"answer": {…}, "contract": {…}}` conformance document —
    /// the operation the `elicitation-answer-accept` / `elicitation-answer-
    /// reject` corpus families drive (§18.4). The document carries no tree, so
    /// the `CONTRACT_UNKNOWN_NODE` probe does not apply here.
    let validateAnswerDocument (json: string) : Result<unit, JsonDecode.DecodeError> =
        match Json.parse json with
        | Error m -> Error(err "INVALID_JSON" "$" ("input is not valid JSON: " + m))
        | Ok(JObj fields) ->
            match firstUndeclared [ "answer"; "contract" ] fields with
            | Some stray ->
                Error(
                    err
                        ElicitationErrorCode.UNDECLARED_FIELD
                        ("$." + stray)
                        (sprintf "undeclared key '%s' on an answer-conformance document" stray)
                )
            | None ->
                (match fieldOpt "contract" fields with
                 | None -> Error(err "MISSING_FIELD" "$.contract" "missing required field 'contract'")
                 | Some c -> decodeContract "$.contract" (fun _ -> true) c)
                |> Result.bind (fun contract ->
                    (match fieldOpt "answer" fields with
                     | None -> Error(err "MISSING_FIELD" "$.answer" "missing required field 'answer'")
                     | Some a -> decodeAnswerObject "$.answer" a)
                    |> Result.bind (validateAnswerAt "$.answer" contract))
        | Ok _ -> Error(err "WRONG_TYPE" "$" "answer-conformance document must be a JSON object")
