module Fuaran.UI.Ops.CustomCardJson

open Fuaran.Core
open Fuaran.UI

// ============================================================================
//  The contract-card CODEC (Phase 1108; WIRE_FORMAT.md §25).
//
//  `Fuaran.UI.CustomCard` owns what a card MEANS — the hash verdict, the
//  placeholder derivation, the card-driven prop validation. This module owns
//  what a card IS on the wire: one canonical encoding, one decoder, and the §6
//  `DecodeError` envelope for every way a document can fail to be a card.
//
//  It sits here rather than beside the types for one reason: `DecodeError` is
//  declared in `Fuaran.UI.Ops.JsonDecode`, and reusing it is not a convenience
//  but the point. A card refusal that spoke its own error vocabulary would be a
//  second refusal shape for hosts to learn, and the corpus manifest's
//  `expectedErrorCode` / `expectedPath` columns — the machinery every other
//  reject family in this corpus is expressed in — would have nothing to record.
//
//  DEFAULT-DENY BY SHAPE, as §18 does it. Every object position refuses an
//  undeclared key (`UNDECLARED_FIELD`). A card is a protocol artefact, not a
//  forward-compatibility carrier: its evolution is the explicit `$card` version
//  bump, and a decoder that shrugged at an unknown key would silently accept a
//  document from a newer producer while ignoring exactly the part that was new.
//
//  ENCODE IS CANONICAL AND SORTED (`Canon.render`) — Ordinal keys, recursively.
//  Decode is order-tolerant, because it looks up by name. That asymmetry is the
//  §2 discipline and it is what makes byte-comparison a usable conformance leg.
// ============================================================================

open Fuaran.UI.Ops.JsonDecode

/// The card-specific refusal, riding the standard §6 envelope. Structural
/// failures reuse the §6 codes; these two name what is specific to this artefact.
[<RequireQualifiedAccess>]
module CardErrorCode =
    /// `$card` / `$cards` present but not a version this decoder implements.
    [<Literal>]
    let UNSUPPORTED_VERSION = "UNSUPPORTED_VERSION"

    /// A key the card shape does not declare (default-deny by shape).
    [<Literal>]
    let UNDECLARED_FIELD = "UNDECLARED_FIELD"

    /// A bundle carries two cards for one `(moduleId, componentId)`.
    ///
    /// Refused rather than last-write-wins, and this is the one place the two
    /// differ deliberately. A STORE resolves duplicates by the order its host
    /// chose; a bundle is a DOCUMENT, and a document that says two different
    /// things about one identity has no order to appeal to — accepting it would
    /// make the card a reader picks depend on decoder implementation detail.
    [<Literal>]
    let DUPLICATE_CARD = "DUPLICATE_CARD"

let private err (code: string) (path: string) (message: string) : DecodeError =
    { Code = code
      Path = path
      Message = message
      ExpectedShape = None }

/// The first key present in `fields` that `declared` does not list. The §18
/// undeclared-key probe, in declaration order so the refusal is deterministic
/// across hosts rather than dependent on map iteration.
let private firstUndeclared (declared: string list) (fields: (string * JVal) list) : string option =
    fields
    |> List.tryPick (fun (k, _) -> if List.contains k declared then None else Some k)

let private tryString (path: string) (key: string) (fields: (string * JVal) list) : Result<string, DecodeError> =
    match fields |> List.tryPick (fun (k, v) -> if k = key then Some v else None) with
    | Some(JStr s) -> Ok s
    | Some _ -> Error(err "WRONG_TYPE" (path + "." + key) (key + " must be a string"))
    | None -> Error(err "MISSING_FIELD" (path + "." + key) (key + " is required"))

let private tryOptionalString
    (path: string)
    (key: string)
    (fields: (string * JVal) list)
    : Result<string option, DecodeError> =
    match fields |> List.tryPick (fun (k, v) -> if k = key then Some v else None) with
    | Some(JStr s) -> Ok(Some s)
    | Some _ -> Error(err "WRONG_TYPE" (path + "." + key) (key + " must be a string when present"))
    | None -> Ok None

let private tryBool (path: string) (key: string) (fields: (string * JVal) list) : Result<bool, DecodeError> =
    match fields |> List.tryPick (fun (k, v) -> if k = key then Some v else None) with
    | Some(JBool b) -> Ok b
    | Some _ -> Error(err "WRONG_TYPE" (path + "." + key) (key + " must be a boolean"))
    | None -> Error(err "MISSING_FIELD" (path + "." + key) (key + " is required"))

// ─── Encode ──────────────────────────────────────────────────────────────────

let private encodePayload (language: string) (gate: string option) : JVal =
    match gate with
    | Some g -> JObj [ "gate", JStr g; "language", JStr language ]
    | None -> JObj [ "language", JStr language ]

let private encodeProp (p: CustomPropCard) : JVal =
    let payload =
        match p.PayloadLanguage with
        | Some l -> [ "payload", encodePayload l p.PayloadGate ]
        | None -> []

    JObj(
        [ "name", JStr p.Name ]
        @ payload
        @ [ "required", JBool p.Required; "type", JStr p.Type ]
    )

/// A card as a `JVal`. `Canon.render` supplies the ordering, so the field order
/// written here is for a reader of this source and not for the wire.
let encodeCard (card: CustomKindCard) : JVal =
    let summary =
        match card.Summary with
        | Some s -> [ "summary", JStr s ]
        | None -> []

    JObj(
        [ "$card", JStr CustomCard.formatVersion
          "componentId", JStr card.ComponentId
          "contentHash", JObj [ "algorithm", JStr card.Hash.Algorithm; "hash", JStr card.Hash.Hash ]
          "moduleId", JStr card.ModuleId
          "props", JArr(card.Props |> List.map encodeProp) ]
        @ summary
    )

/// One card as canonical wire bytes.
let encodeCardJson (card: CustomKindCard) : string = Canon.render (encodeCard card)

/// A card BUNDLE — the shape a host publishes (§25.2). Cards are emitted sorted
/// by `(moduleId, componentId)` Ordinal, so two deployments that hold the same
/// cards publish the same bytes whatever order their registries iterated in.
let encodeBundleJson (cards: CustomKindCard list) : string =
    let sorted =
        cards
        |> List.sortWith (fun a b ->
            match System.String.CompareOrdinal(a.ModuleId, b.ModuleId) with
            | 0 -> System.String.CompareOrdinal(a.ComponentId, b.ComponentId)
            | c -> c)

    Canon.render (
        JObj
            [ "$cards", JStr CustomCard.bundleFormatVersion
              "cards", JArr(sorted |> List.map encodeCard) ]
    )

/// Every card a registry holds, as one bundle document — the EXPORT half of the
/// artefact (Phase 1108 task 2). A deployment publishes this; a foreign host
/// reads it. Nothing else has to be arranged: `DescribeForAi` already assembled
/// the cards.
let exportBundleJson (registry: CustomRegistry) : string =
    encodeBundleJson (registry.DescribeForAi())

// ─── Decode ──────────────────────────────────────────────────────────────────

let private decodePayload (path: string) (jv: JVal) : Result<string * string option, DecodeError> =
    match jv with
    | JObj fields ->
        match firstUndeclared [ "gate"; "language" ] fields with
        | Some stray ->
            Error(err CardErrorCode.UNDECLARED_FIELD (path + "." + stray) ("undeclared key '" + stray + "'"))
        | None ->
            tryString path "language" fields
            |> Result.bind (fun language ->
                tryOptionalString path "gate" fields |> Result.map (fun gate -> language, gate))
    | _ -> Error(err "WRONG_TYPE" path "payload must be an object")

let private decodeProp (path: string) (jv: JVal) : Result<CustomPropCard, DecodeError> =
    match jv with
    | JObj fields ->
        match firstUndeclared [ "name"; "payload"; "required"; "type" ] fields with
        | Some stray ->
            Error(err CardErrorCode.UNDECLARED_FIELD (path + "." + stray) ("undeclared key '" + stray + "'"))
        | None ->
            tryString path "name" fields
            |> Result.bind (fun name ->
                tryString path "type" fields
                |> Result.bind (fun typeTag ->
                    // The tag is checked HERE, at decode, not left for the
                    // validator to shrug at. A card whose type vocabulary this
                    // build cannot read is a document this build cannot honour,
                    // and saying so at the boundary is what keeps
                    // `CardValidation.Unresolvable` a report about a DECODED card
                    // rather than a way for an unreadable one to travel further.
                    match CustomRegistry.tryParsePropTypeTag typeTag with
                    | None ->
                        Error(
                            err
                                "UNKNOWN_DU_CASE"
                                (path + ".type")
                                ("'" + typeTag + "' is not a declared prop type in this build")
                        )
                    | Some _ ->
                        tryBool path "required" fields
                        |> Result.bind (fun required ->
                            let payload =
                                fields |> List.tryPick (fun (k, v) -> if k = "payload" then Some v else None)

                            match payload with
                            | None ->
                                Ok
                                    { Name = name
                                      Type = typeTag
                                      Required = required
                                      PayloadLanguage = None
                                      PayloadGate = None }
                            | Some p ->
                                decodePayload (path + ".payload") p
                                |> Result.map (fun (language, gate) ->
                                    { Name = name
                                      Type = typeTag
                                      Required = required
                                      PayloadLanguage = Some language
                                      PayloadGate = gate }))))
    | _ -> Error(err "WRONG_TYPE" path "a prop row must be an object")

let private decodeHash (path: string) (jv: JVal) : Result<CardContentHash, DecodeError> =
    match jv with
    | JObj fields ->
        match firstUndeclared [ "algorithm"; "hash" ] fields with
        | Some stray ->
            Error(err CardErrorCode.UNDECLARED_FIELD (path + "." + stray) ("undeclared key '" + stray + "'"))
        | None ->
            tryString path "algorithm" fields
            |> Result.bind (fun algorithm ->
                tryString path "hash" fields
                |> Result.map (fun hash -> { Algorithm = algorithm; Hash = hash }))
    | _ -> Error(err "WRONG_TYPE" path "contentHash must be an object")

/// Collect over a list, stopping at the first failure — the fail-fast decode
/// order §25.3 pins, so every conformant host surfaces the SAME first error.
let private traverse (f: int -> 'a -> Result<'b, DecodeError>) (xs: 'a list) : Result<'b list, DecodeError> =
    let rec go i acc rest =
        match rest with
        | [] -> Ok(List.rev acc)
        | x :: tail ->
            match f i x with
            | Ok v -> go (i + 1) (v :: acc) tail
            | Error e -> Error e

    go 0 [] xs

let private decodeCardAt (path: string) (jv: JVal) : Result<CustomKindCard, DecodeError> =
    match jv with
    | JObj fields ->
        match firstUndeclared [ "$card"; "componentId"; "contentHash"; "moduleId"; "props"; "summary" ] fields with
        | Some stray ->
            Error(err CardErrorCode.UNDECLARED_FIELD (path + "." + stray) ("undeclared key '" + stray + "'"))
        | None ->
            tryString path "$card" fields
            |> Result.bind (fun version ->
                if version <> CustomCard.formatVersion then
                    Error(
                        err
                            CardErrorCode.UNSUPPORTED_VERSION
                            (path + ".$card")
                            ("card format version '" + version + "' is not supported by this decoder")
                    )
                else
                    tryString path "moduleId" fields
                    |> Result.bind (fun moduleId ->
                        tryString path "componentId" fields
                        |> Result.bind (fun componentId ->
                            match
                                fields
                                |> List.tryPick (fun (k, v) -> if k = "contentHash" then Some v else None)
                            with
                            | None -> Error(err "MISSING_FIELD" (path + ".contentHash") "contentHash is required")
                            | Some h ->
                                decodeHash (path + ".contentHash") h
                                |> Result.bind (fun hash ->
                                    match
                                        fields |> List.tryPick (fun (k, v) -> if k = "props" then Some v else None)
                                    with
                                    | None -> Error(err "MISSING_FIELD" (path + ".props") "props is required")
                                    | Some(JArr rows) ->
                                        rows
                                        |> traverse (fun i row ->
                                            decodeProp (path + ".props[" + string i + "]") row)
                                        |> Result.bind (fun props ->
                                            tryOptionalString path "summary" fields
                                            |> Result.map (fun summary ->
                                                { ModuleId = moduleId
                                                  ComponentId = componentId
                                                  Props = props
                                                  Hash = hash
                                                  Summary = summary }))
                                    | Some _ -> Error(err "WRONG_TYPE" (path + ".props") "props must be an array")))))
    | _ -> Error(err "WRONG_TYPE" path "a card must be an object")

/// Decode one card document.
let decodeCardJson (json: string) : Result<CustomKindCard, DecodeError> =
    match Json.parse json with
    | Error m -> Error(err "INVALID_JSON" "$" ("card is not valid JSON: " + m))
    | Ok jv -> decodeCardAt "$" jv

/// Decode a card BUNDLE, refusing a document that carries two cards for one
/// identity.
let decodeBundleJson (json: string) : Result<CustomKindCard list, DecodeError> =
    match Json.parse json with
    | Error m -> Error(err "INVALID_JSON" "$" ("card bundle is not valid JSON: " + m))
    | Ok(JObj fields) ->
        match firstUndeclared [ "$cards"; "cards" ] fields with
        | Some stray -> Error(err CardErrorCode.UNDECLARED_FIELD ("$." + stray) ("undeclared key '" + stray + "'"))
        | None ->
            tryString "$" "$cards" fields
            |> Result.bind (fun version ->
                if version <> CustomCard.bundleFormatVersion then
                    Error(
                        err
                            CardErrorCode.UNSUPPORTED_VERSION
                            "$.$cards"
                            ("card bundle format version '" + version + "' is not supported by this decoder")
                    )
                else
                    match fields |> List.tryPick (fun (k, v) -> if k = "cards" then Some v else None) with
                    | None -> Error(err "MISSING_FIELD" "$.cards" "cards is required")
                    | Some(JArr rows) ->
                        rows
                        |> traverse (fun i row -> decodeCardAt ("$.cards[" + string i + "]") row)
                        |> Result.bind (fun cards ->
                            let duplicate =
                                cards
                                |> List.mapi (fun i c -> i, (c.ModuleId, c.ComponentId))
                                |> List.tryPick (fun (i, key) ->
                                    if
                                        cards
                                        |> List.take i
                                        |> List.exists (fun c -> (c.ModuleId, c.ComponentId) = key)
                                    then
                                        Some(i, key)
                                    else
                                        None)

                            match duplicate with
                            | Some(i, (m, c)) ->
                                Error(
                                    err
                                        CardErrorCode.DUPLICATE_CARD
                                        ("$.cards[" + string i + "]")
                                        ("the bundle carries two cards for '" + m + "." + c + "'")
                                )
                            | None -> Ok cards)
                    | Some _ -> Error(err "WRONG_TYPE" "$.cards" "cards must be an array"))
    | Ok _ -> Error(err "WRONG_TYPE" "$" "a card bundle must be an object")

/// Decode a bundle straight into the store a renderer consumes.
let decodeStore (json: string) : Result<CustomCardStore, DecodeError> =
    decodeBundleJson json |> Result.map CustomCardStore.ofCards
