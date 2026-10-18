/// The design-provider seam's wire codec (Phase 2295; `wire-format-fixtures/DESIGN_SEAM.md`).
///
/// Encoding goes through `Canon.render`: Ordinal-sorted keys at every depth, the corpus's string
/// escape and float layout, so every host writes the same bytes for the same record. A record
/// document carries `"$type"`; a nested record is typed by its position. An absent optional
/// member is omitted, and a list member is always present, empty or not.
///
/// Decoding is STRICT on the records and LENIENT on annotations, and the asymmetry is the point.
/// A brief, a request, a reference or a result is an agreement between two parties, so a missing
/// required member or an unknown member is a disagreement and is refused. A screen annotation is
/// evidence a provider reports about its own output, so an unknown member there is ignored rather
/// than refused: refusing it would throw away the rest of the evidence.
module Fuaran.UI.Design.SeamCodec

open Fuaran.Core

/// Why a document was refused. Each code is stable and shared with the corpus's `design-seam-reject`
/// vectors.
[<RequireQualifiedAccess>]
type SeamErrorCode =
    /// The text is not JSON.
    | Malformed
    /// A member, or the document, is the wrong JSON type, or `$type` names another record.
    | WrongType
    /// A required member is absent.
    | MissingMember
    /// A record carries a member its record does not declare.
    | UnknownMember
    /// An interaction outside the closed vocabulary.
    | UnknownInteraction
    /// A cardinality, density or request state outside its closed vocabulary.
    | UnknownToken
    /// A hash not spelled `sha256:` and 64 lowercase hexadecimal digits.
    | BadHash
    /// A required string that is empty, or a viewport width that is not positive.
    | OutOfRange
    /// A result citing a different design-system bundle than its request.
    | DesignSystemMismatch

module SeamErrorCode =

    /// The code as the corpus spells it.
    let name (code: SeamErrorCode) : string =
        match code with
        | SeamErrorCode.Malformed -> "DESIGN_MALFORMED"
        | SeamErrorCode.WrongType -> "DESIGN_WRONG_TYPE"
        | SeamErrorCode.MissingMember -> "DESIGN_MISSING_MEMBER"
        | SeamErrorCode.UnknownMember -> "DESIGN_UNKNOWN_MEMBER"
        | SeamErrorCode.UnknownInteraction -> "DESIGN_UNKNOWN_INTERACTION"
        | SeamErrorCode.UnknownToken -> "DESIGN_UNKNOWN_TOKEN"
        | SeamErrorCode.BadHash -> "DESIGN_BAD_HASH"
        | SeamErrorCode.OutOfRange -> "DESIGN_OUT_OF_RANGE"
        | SeamErrorCode.DesignSystemMismatch -> "DESIGN_SYSTEM_MISMATCH"

/// A refusal: its code, the path of the offending value (`$`, `$.member`, `$.list[2]`), and a
/// sentence saying what was wrong.
type SeamError =
    { Code: SeamErrorCode
      Path: string
      Message: string }

// ─── Encoding ───────────────────────────────────────────────────────────────

let private str (s: string) = JStr s
let private strs (xs: string list) = JArr(xs |> List.map JStr)
let private opt name (f: 'a -> JVal) (v: 'a option) = v |> Option.map (fun x -> name, f x)

let private interactionToken (i: Interaction) : string =
    match i with
    | Interaction.Filter -> "Filter"
    | Interaction.Select -> "Select"
    | Interaction.Drill -> "Drill"
    | Interaction.Compare -> "Compare"
    | Interaction.Export -> "Export"
    | Interaction.Navigate -> "Navigate"

let private cardinalityToken (c: Cardinality) : string =
    match c with
    | Cardinality.One -> "One"
    | Cardinality.Optional -> "Optional"
    | Cardinality.Many -> "Many"

let private densityToken (d: Density) : string =
    match d with
    | Density.Compact -> "Compact"
    | Density.Standard -> "Standard"
    | Density.Spacious -> "Spacious"

let private stateToken (s: RequestState) : string =
    match s with
    | RequestState.Manual -> "Manual"
    | RequestState.Pending -> "Pending"
    | RequestState.Ready -> "Ready"

let private refFields (r: DesignSystemRef) : (string * JVal) list =
    [ Some("providerId", str r.ProviderId)
      opt "projectId" str r.ProjectId
      Some("bundleHash", str r.BundleHash)
      Some("idlVersion", JInt r.IdlVersion) ]
    |> List.choose id

let private briefFields (b: DesignBrief) : (string * JVal) list =
    [ Some("module", str b.Module)
      opt "purpose" str b.Purpose
      Some(
          "dataShapes",
          JArr(
              b.DataShapes
              |> List.map (fun d ->
                  JObj
                      [ "name", str d.Name
                        "type", str d.Type
                        "cardinality", str (cardinalityToken d.Cardinality) ])
          )
      )
      Some("tasks", strs b.Tasks)
      Some("interactions", JArr(b.Interactions |> List.map (interactionToken >> JStr)))
      Some(
          "viewports",
          JArr(
              b.Viewports
              |> List.map (fun v -> JObj [ "name", str v.Name; "width", JInt v.Width ])
          )
      )
      opt "density" (densityToken >> JStr) b.Density
      Some("fragments", strs b.Fragments)
      Some("designSystem", JObj(refFields b.DesignSystem)) ]
    |> List.choose id

let private requestFields (r: DesignRequest) : (string * JVal) list =
    [ Some("briefHash", str r.BriefHash)
      Some("designSystem", JObj(refFields r.DesignSystem))
      Some("state", str (stateToken r.State))
      opt "handle" str r.Handle ]
    |> List.choose id

let private resultFields (r: DesignResult) : (string * JVal) list =
    [ Some(
          "screens",
          JArr(
              r.Screens
              |> List.map (fun s ->
                  JObj
                      [ "name", str s.Name
                        "files",
                        JArr(
                            s.Files
                            |> List.map (fun f ->
                                JObj [ "path", str f.Path; "mediaType", str f.MediaType; "hash", str f.Hash ])
                        ) ])
          )
      )
      opt
          "annotations"
          (fun anns ->
              JArr(
                  anns
                  |> List.map (fun a -> JObj [ "screen", str a.Screen; "components", strs a.Components ])
              ))
          r.Annotations
      Some("designSystem", JObj(refFields r.DesignSystem))
      Some("contentHash", str r.ContentHash) ]
    |> List.choose id

/// A design-system reference as its canonical document.
let encodeRef (r: DesignSystemRef) : string =
    Canon.render (Canon.typed "DesignSystemRef" (refFields r))

/// A brief as its canonical document. Its UTF-8 bytes are what `DesignRequest.BriefHash` hashes.
let encodeBrief (b: DesignBrief) : string =
    Canon.render (Canon.typed "DesignBrief" (briefFields b))

/// A request as its canonical document.
let encodeRequest (r: DesignRequest) : string =
    Canon.render (Canon.typed "DesignRequest" (requestFields r))

/// A result as its canonical document.
let encodeResult (r: DesignResult) : string =
    Canon.render (Canon.typed "DesignResult" (resultFields r))

/// A request and the result a provider returned for it, as one canonical document — the shape
/// the corpus uses to state the exchange rule (`checkExchange`).
let encodeExchange (request: DesignRequest) (result: DesignResult) : string =
    Canon.render (
        Canon.typed
            "DesignExchange"
            [ "request", Canon.typed "DesignRequest" (requestFields request)
              "result", Canon.typed "DesignResult" (resultFields result) ]
    )

// ─── Decoding ───────────────────────────────────────────────────────────────

type private D<'a> = Result<'a, SeamError>

let private fail code path message : D<'a> =
    Error
        { Code = code
          Path = path
          Message = message }

let private kindOf (v: JVal) =
    match v with
    | JStr _ -> "a string"
    | JInt _ -> "an integer"
    | JFloat _ -> "a number"
    | JBool _ -> "a boolean"
    | JArr _ -> "an array"
    | JObj _ -> "an object"

let private traverse (f: int -> JVal -> D<'b>) (xs: JVal list) : D<'b list> =
    let rec go i acc rest =
        match rest with
        | [] -> Ok(List.rev acc)
        | x :: tail ->
            match f i x with
            | Ok y -> go (i + 1) (y :: acc) tail
            | Error e -> Error e

    go 0 [] xs

/// An object's members, refusing any member outside `declared` unless `lenient`.
let private members (path: string) (declared: string list) (lenient: bool) (v: JVal) : D<Map<string, JVal>> =
    match v with
    | JObj fields ->
        let unknown =
            if lenient then
                None
            else
                fields |> List.tryFind (fun (k, _) -> not (List.contains k declared))

        match unknown with
        | Some(k, _) -> fail SeamErrorCode.UnknownMember $"%s{path}.%s{k}" $"'%s{k}' is not a member of this record"
        | None ->
            // The first member of a repeated key wins, as every corpus reader takes it.
            fields
            |> List.rev
            |> List.fold (fun (m: Map<string, JVal>) (k, x) -> m.Add(k, x)) Map.empty
            |> Ok
    | other -> fail SeamErrorCode.WrongType path $"expected an object, found %s{kindOf other}"

let private required (path: string) (name: string) (m: Map<string, JVal>) : D<JVal> =
    match m.TryFind name with
    | Some v -> Ok v
    | None -> fail SeamErrorCode.MissingMember $"%s{path}.%s{name}" $"the required member '%s{name}' is absent"

let private asString (path: string) (v: JVal) : D<string> =
    match v with
    | JStr s -> Ok s
    | other -> fail SeamErrorCode.WrongType path $"expected a string, found %s{kindOf other}"

let private asNonEmpty (path: string) (v: JVal) : D<string> =
    asString path v
    |> Result.bind (fun s ->
        if s = "" then
            fail SeamErrorCode.OutOfRange path "the string is empty"
        else
            Ok s)

let private asHash (path: string) (v: JVal) : D<string> =
    asString path v
    |> Result.bind (fun s ->
        if DesignHash.isWellFormed s then
            Ok s
        else
            fail SeamErrorCode.BadHash path "a hash is 'sha256:' and 64 lowercase hexadecimal digits")

let private asInt (path: string) (v: JVal) : D<int> =
    match v with
    | JInt i -> Ok i
    | other -> fail SeamErrorCode.WrongType path $"expected an integer, found %s{kindOf other}"

let private asArray (path: string) (v: JVal) : D<JVal list> =
    match v with
    | JArr xs -> Ok xs
    | other -> fail SeamErrorCode.WrongType path $"expected an array, found %s{kindOf other}"

let private asList (path: string) (item: string -> JVal -> D<'a>) (v: JVal) : D<'a list> =
    asArray path v |> Result.bind (traverse (fun i x -> item $"%s{path}[%d{i}]" x))

let private req path name m (f: string -> JVal -> D<'a>) : D<'a> =
    required path name m |> Result.bind (f $"%s{path}.%s{name}")

let private optional path name (m: Map<string, JVal>) (f: string -> JVal -> D<'a>) : D<'a option> =
    match m.TryFind name with
    | None -> Ok None
    | Some v -> f $"%s{path}.%s{name}" v |> Result.map Some

let private token (code: SeamErrorCode) (what: string) (table: (string * 'a) list) (path: string) (v: JVal) : D<'a> =
    asString path v
    |> Result.bind (fun s ->
        match table |> List.tryFind (fun (t, _) -> t = s) with
        | Some(_, x) -> Ok x
        | None ->
            let allowed = table |> List.map fst |> String.concat ", "
            fail code path $"'%s{s}' is not %s{what} (one of %s{allowed})")

let private interaction =
    token
        SeamErrorCode.UnknownInteraction
        "an interaction"
        [ "Filter", Interaction.Filter
          "Select", Interaction.Select
          "Drill", Interaction.Drill
          "Compare", Interaction.Compare
          "Export", Interaction.Export
          "Navigate", Interaction.Navigate ]

let private cardinality =
    token
        SeamErrorCode.UnknownToken
        "a cardinality"
        [ "One", Cardinality.One
          "Optional", Cardinality.Optional
          "Many", Cardinality.Many ]

let private density =
    token
        SeamErrorCode.UnknownToken
        "a density"
        [ "Compact", Density.Compact
          "Standard", Density.Standard
          "Spacious", Density.Spacious ]

let private state =
    token
        SeamErrorCode.UnknownToken
        "a request state"
        [ "Manual", RequestState.Manual
          "Pending", RequestState.Pending
          "Ready", RequestState.Ready ]

let private refMembers = [ "providerId"; "projectId"; "bundleHash"; "idlVersion" ]

let private decRefMembers (path: string) (m: Map<string, JVal>) : D<DesignSystemRef> =
    req path "providerId" m asNonEmpty
    |> Result.bind (fun providerId ->
        optional path "projectId" m asNonEmpty
        |> Result.bind (fun projectId ->
            req path "bundleHash" m asHash
            |> Result.bind (fun bundleHash ->
                req path "idlVersion" m asInt
                |> Result.map (fun idlVersion ->
                    { ProviderId = providerId
                      ProjectId = projectId
                      BundleHash = bundleHash
                      IdlVersion = idlVersion }))))

let private decRef (path: string) (v: JVal) : D<DesignSystemRef> =
    members path refMembers false v |> Result.bind (decRefMembers path)

let private decShape (path: string) (v: JVal) : D<DataShape> =
    members path [ "name"; "type"; "cardinality" ] false v
    |> Result.bind (fun m ->
        req path "name" m asNonEmpty
        |> Result.bind (fun name ->
            req path "type" m asNonEmpty
            |> Result.bind (fun ty ->
                req path "cardinality" m cardinality
                |> Result.map (fun c ->
                    { Name = name
                      Type = ty
                      Cardinality = c }))))

let private decViewport (path: string) (v: JVal) : D<Viewport> =
    members path [ "name"; "width" ] false v
    |> Result.bind (fun m ->
        req path "name" m asNonEmpty
        |> Result.bind (fun name ->
            req path "width" m asInt
            |> Result.bind (fun width ->
                if width > 0 then
                    Ok { Name = name; Width = width }
                else
                    fail SeamErrorCode.OutOfRange $"%s{path}.width" "a viewport width is a positive number of pixels")))

let private decFile (path: string) (v: JVal) : D<DesignFile> =
    members path [ "path"; "mediaType"; "hash" ] false v
    |> Result.bind (fun m ->
        req path "path" m asNonEmpty
        |> Result.bind (fun p ->
            req path "mediaType" m asNonEmpty
            |> Result.bind (fun mediaType ->
                req path "hash" m asHash
                |> Result.map (fun hash ->
                    { Path = p
                      MediaType = mediaType
                      Hash = hash }))))

let private decScreen (path: string) (v: JVal) : D<DesignScreen> =
    members path [ "name"; "files" ] false v
    |> Result.bind (fun m ->
        req path "name" m asNonEmpty
        |> Result.bind (fun name ->
            req path "files" m (fun p x -> asList p decFile x)
            |> Result.map (fun files -> { Name = name; Files = files })))

/// Lenient: an annotation is evidence, so an unknown member is ignored.
let private decAnnotation (path: string) (v: JVal) : D<ScreenAnnotation> =
    members path [] true v
    |> Result.bind (fun m ->
        req path "screen" m asNonEmpty
        |> Result.bind (fun screen ->
            req path "components" m (fun p x -> asList p asNonEmpty x)
            |> Result.map (fun components ->
                { Screen = screen
                  Components = components })))

let private decBrief (path: string) (m: Map<string, JVal>) : D<DesignBrief> =
    req path "module" m asNonEmpty
    |> Result.bind (fun moduleName ->
        optional path "purpose" m asNonEmpty
        |> Result.bind (fun purpose ->
            req path "dataShapes" m (fun p x -> asList p decShape x)
            |> Result.bind (fun shapes ->
                req path "tasks" m (fun p x -> asList p asNonEmpty x)
                |> Result.bind (fun tasks ->
                    req path "interactions" m (fun p x -> asList p interaction x)
                    |> Result.bind (fun interactions ->
                        req path "viewports" m (fun p x -> asList p decViewport x)
                        |> Result.bind (fun viewports ->
                            optional path "density" m density
                            |> Result.bind (fun dens ->
                                req path "fragments" m (fun p x -> asList p asNonEmpty x)
                                |> Result.bind (fun fragments ->
                                    req path "designSystem" m decRef
                                    |> Result.map (fun ds ->
                                        { Module = moduleName
                                          Purpose = purpose
                                          DataShapes = shapes
                                          Tasks = tasks
                                          Interactions = interactions
                                          Viewports = viewports
                                          Density = dens
                                          Fragments = fragments
                                          DesignSystem = ds })))))))))

let private decRequest (path: string) (m: Map<string, JVal>) : D<DesignRequest> =
    req path "briefHash" m asHash
    |> Result.bind (fun briefHash ->
        req path "designSystem" m decRef
        |> Result.bind (fun ds ->
            req path "state" m state
            |> Result.bind (fun st ->
                optional path "handle" m asNonEmpty
                |> Result.map (fun handle ->
                    { BriefHash = briefHash
                      DesignSystem = ds
                      State = st
                      Handle = handle }))))

let private decResult (path: string) (m: Map<string, JVal>) : D<DesignResult> =
    req path "screens" m (fun p x -> asList p decScreen x)
    |> Result.bind (fun screens ->
        optional path "annotations" m (fun p x -> asList p decAnnotation x)
        |> Result.bind (fun annotations ->
            req path "designSystem" m decRef
            |> Result.bind (fun ds ->
                req path "contentHash" m asHash
                |> Result.map (fun contentHash ->
                    { Screens = screens
                      Annotations = annotations
                      DesignSystem = ds
                      ContentHash = contentHash }))))

/// A record document: `$type` must name `tag` (checked first, so a document of another record is
/// named as such rather than as a list of unknown members), and every other member is one
/// `declared` names.
let private document
    (tag: string)
    (declared: string list)
    (decode: string -> Map<string, JVal> -> D<'a>)
    (path: string)
    (v: JVal)
    : D<'a> =
    match v with
    | JObj fields ->
        match fields |> List.tryFind (fun (k, _) -> k = "$type") with
        | None -> fail SeamErrorCode.MissingMember $"%s{path}.$type" "the required member '$type' is absent"
        | Some(_, JStr t) when t = tag -> members path ("$type" :: declared) false v |> Result.bind (decode path)
        | Some(_, JStr t) ->
            fail SeamErrorCode.WrongType $"%s{path}.$type" $"expected a '%s{tag}' document, found '%s{t}'"
        | Some(_, other) -> fail SeamErrorCode.WrongType $"%s{path}.$type" $"expected a string, found %s{kindOf other}"
    | other -> fail SeamErrorCode.WrongType path $"expected an object, found %s{kindOf other}"

let private refDocument = document "DesignSystemRef" refMembers decRefMembers

let private briefDocument =
    document
        "DesignBrief"
        [ "module"
          "purpose"
          "dataShapes"
          "tasks"
          "interactions"
          "viewports"
          "density"
          "fragments"
          "designSystem" ]
        decBrief

let private requestDocument =
    document "DesignRequest" [ "briefHash"; "designSystem"; "state"; "handle" ] decRequest

let private resultDocument =
    document "DesignResult" [ "screens"; "annotations"; "designSystem"; "contentHash" ] decResult

let private parse (text: string) (decode: string -> JVal -> D<'a>) : D<'a> =
    match Json.parse text with
    | Ok v -> decode "$" v
    | Error e -> fail SeamErrorCode.Malformed "$" e

/// Decode a `DesignSystemRef` document.
let decodeRef (text: string) : Result<DesignSystemRef, SeamError> = parse text refDocument

/// Decode a `DesignBrief` document.
let decodeBrief (text: string) : Result<DesignBrief, SeamError> = parse text briefDocument

/// Decode a `DesignRequest` document.
let decodeRequest (text: string) : Result<DesignRequest, SeamError> = parse text requestDocument

/// Decode a `DesignResult` document.
let decodeResult (text: string) : Result<DesignResult, SeamError> = parse text resultDocument

/// The exchange rule: a result answers the request it was fetched for only when it was made
/// against the same design-system bundle.
let checkExchange (request: DesignRequest) (result: DesignResult) : Result<unit, SeamError> =
    if request.DesignSystem.BundleHash = result.DesignSystem.BundleHash then
        Ok()
    else
        fail
            SeamErrorCode.DesignSystemMismatch
            "$.result.designSystem.bundleHash"
            $"the result was made against bundle %s{result.DesignSystem.BundleHash}, the request against %s{request.DesignSystem.BundleHash}"

/// Decode a `DesignExchange` document (a request and the result returned for it) and apply
/// `checkExchange`.
let decodeExchange (text: string) : Result<DesignRequest * DesignResult, SeamError> =
    parse
        text
        (document "DesignExchange" [ "request"; "result" ] (fun path m ->
            req path "request" m requestDocument
            |> Result.bind (fun request ->
                req path "result" m resultDocument
                |> Result.bind (fun result -> checkExchange request result |> Result.map (fun () -> request, result)))))

/// The corpus's decoder names (`wire-format-fixtures/DESIGN_SEAM.md`), each the entry point a
/// conformant host invokes for a `design-seam-*` vector.
let decoders: string list =
    [ "design-system-ref"
      "design-brief"
      "design-request"
      "design-result"
      "design-exchange" ]

/// Decode `text` with the entry point `decoder` names and re-encode what it decoded: the
/// round-trip law a provider is certified by. `None` for a decoder name outside `decoders`.
let reencode (decoder: string) (text: string) : Result<string, SeamError> option =
    match decoder with
    | "design-system-ref" -> Some(decodeRef text |> Result.map encodeRef)
    | "design-brief" -> Some(decodeBrief text |> Result.map encodeBrief)
    | "design-request" -> Some(decodeRequest text |> Result.map encodeRequest)
    | "design-result" -> Some(decodeResult text |> Result.map encodeResult)
    | "design-exchange" -> Some(decodeExchange text |> Result.map (fun (q, r) -> encodeExchange q r))
    | _ -> None
