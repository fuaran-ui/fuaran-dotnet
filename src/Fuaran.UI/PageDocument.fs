namespace Fuaran.UI

open Fuaran.Core
open Fuaran.UI.Types

// ============================================================================
//  The page document (Phase 2522, WIRE_FORMAT §32).
//
//  A page a user reshapes at run time is data a host stores, verifies and
//  renders without compiling anything. The tree alone is not that data: a
//  `Binding.Query` names a source by string, and nothing in the tree says what
//  the source IS. The page document is the tree with the rest of the answer
//  beside it — a sidecar of requests keyed by string, each a request kind and
//  its canonical JSON payload, and the provenance a reader needs to say where
//  the page came from.
//
//  ── The binding rule ─────────────────────────────────────────────────────────
//  Every `Binding.Query` in the tree names a key of the sidecar, and every key
//  of the sidecar is named by some `Binding.Query`. Both directions are refused
//  at decode (`PageDocumentDefect`): a query naming no request is a source the
//  host cannot resolve and the read-set cannot describe; a request no query
//  names is reach the page claims and never uses. The second rule a page's
//  safety rests on — a data-bearing node names a request, and carries no inline
//  rows — is the validator's page-document mode (`PreEmitValidate.validatePage`,
//  FUARAN166 / FUARAN167), because it is a judgement over the tree's slots
//  rather than over the document's shape.
//
//  ── Request kinds are the host's ─────────────────────────────────────────────
//  The language tier does not know what a request means. It carries each one as
//  a kind and a canonical JSON payload, and it READS exactly the fields the two
//  declared kinds name (`PageRequest.reach`): `presentation` and `pipeline` each
//  declare a `tableId` and its `metrics`; a presentation request may declare a
//  `subjectLevel`; a pipeline request declares its `pipeline`, whose step verbs
//  the demanded projection lists. Every other member of a payload is opaque, and
//  a request of any other kind is carried and named, never read.
//
//  ── Codec discipline ─────────────────────────────────────────────────────────
//  One version, every undeclared member refused, total over its input. The tree
//  rides inside in its own canonical form and is decoded by the tier's
//  structural decoder; the document is written under `Rendering.CrossHost`, so
//  its bytes are the same on every host. FSharp.Core + `Fuaran.Core` +
//  `Fuaran.Compute`; Fable-clean.
// ============================================================================

/// One request in a page's sidecar: what kind it is, its payload, and the page
/// parameters a change of which re-resolves it.
type PageRequest =
    {
        /// The request kind. `presentation` and `pipeline` are declared
        /// (`PageRequestKind`); any other kind is the host's alone.
        Kind: string
        /// The request as its kind writes it, carried verbatim as canonical JSON.
        Payload: JVal
        /// The page parameters (state keys and filter names) the request reads:
        /// a change of one re-resolves it. Written sorted; no name twice.
        DependsOn: string list
    }

/// Where a page document came from. Carried for a reader; the signature over a
/// page does not cover it (WIRE_FORMAT §32.5).
type PageProvenance =
    {
        /// What the document was seeded from, as its author names it, when it was.
        SeededFrom: string option
        /// The head of the op stream each layer the document folds stood at, by
        /// layer name.
        OpsHeads: Map<string, string>
        /// The principal that wrote the document, as the writing host names it.
        Author: string
        /// When it was written: `YYYY-MM-DDTHH:MM:SSZ`, UTC.
        At: string
    }

/// A stored page: the tree, the requests its sources name, and its provenance.
type PageDocument =
    {
        /// The page's tree. Every `Binding.Query` in it names a key of `Requests`.
        Tree: Node<obj>
        /// The requests the tree's sources name, by key.
        Requests: Map<string, PageRequest>
        Provenance: PageProvenance
    }

/// A page document whose sources and sidecar disagree.
[<RequireQualifiedAccess>]
type PageDocumentDefect =
    /// A `Binding.Query` read by `nodeId` names `key`, which the sidecar does
    /// not hold.
    | DanglingRequestKey of nodeId: string * key: string
    /// The sidecar holds `key`, and no `Binding.Query` in the tree names it.
    | UnnamedRequest of key: string

/// Why a page document was refused.
[<RequireQualifiedAccess>]
type PageRefusal =
    /// The document is not the declared shape: Core's decode refusal, its path
    /// from the document root.
    | Malformed of DecodeError
    /// The document is the declared shape, and its sources and sidecar
    /// disagree: every disagreement, in tree order and then key order.
    | Unbound of PageDocumentDefect list

/// What a request of a declared kind reads, by the fields its kind declares.
type RequestReach =
    {
        /// The table the request reads.
        Table: string
        /// The table's metrics it reads.
        Metrics: string list
        /// The subject levels it reads at.
        Levels: string list
        /// The pipeline verbs it runs, in step order.
        Verbs: string list
    }

/// The request kinds whose fields the language tier reads.
[<RequireQualifiedAccess>]
module PageRequestKind =

    /// A presentation request: `tableId`, `metrics`, optionally `subjectLevel`.
    [<Literal>]
    let Presentation = "presentation"

    /// A pipeline request: `tableId`, `metrics` and `pipeline`.
    [<Literal>]
    let Pipeline = "pipeline"

[<RequireQualifiedAccess>]
module PageRequest =

    let private nonEmpty: Decoder<string> =
        Decoder.str
        |> Decoder.andThen (fun s ->
            if s = "" then
                Error(RootDecodeCode.OutOfRange, "a non-empty string", "\"\"")
            else
                Ok s)

    /// A list of non-empty strings, none twice.
    let internal names (atLeastOne: bool) : Decoder<string list> =
        Decoder.list nonEmpty
        |> Decoder.andThen (fun xs ->
            if atLeastOne && List.isEmpty xs then
                Error(RootDecodeCode.OutOfRange, "at least one name", "an empty array")
            else
                match xs |> List.countBy id |> List.tryFind (fun (_, n) -> n > 1) with
                | Some(x, _) -> Error(RootDecodeCode.OutOfRange, "each name once", "'" + x + "' twice")
                | None -> Ok xs)

    let internal nonEmptyString = nonEmpty

    /// The verb of a pipeline step: the reference codec's `$type` for it.
    let private verbOf (step: Fuaran.Compute.Transform) : string =
        match Fuaran.Compute.DataFrameCodec.encodeTransform step with
        | JObj members ->
            members
            |> List.tryPick (fun (k, v) ->
                match k, v with
                | "$type", JStr tag -> Some tag
                | _ -> None)
            |> Option.defaultValue ""
        | _ -> ""

    let private pipelineVerbs: Decoder<string list> =
        Decoder.json
        |> Decoder.andThen (fun j ->
            match Fuaran.Compute.DataFrameCodec.decodePipelineJson j with
            | Ok steps -> Ok(steps |> List.map verbOf)
            | Error e ->
                Error(RootDecodeCode.OutOfRange, "a pipeline the reference evaluator reads", ColumnCodec.errorString e))

    let private presentation: Decoder<RequestReach> =
        fun el ->
            Decoder.field "tableId" nonEmpty el
            |> Result.bind (fun table ->
                Decoder.field "metrics" (names true) el
                |> Result.bind (fun metrics ->
                    Decoder.optField "subjectLevel" nonEmpty el
                    |> Result.map (fun level ->
                        { Table = table
                          Metrics = metrics
                          Levels = Option.toList level
                          Verbs = [] })))

    let private pipeline: Decoder<RequestReach> =
        fun el ->
            Decoder.field "tableId" nonEmpty el
            |> Result.bind (fun table ->
                Decoder.field "metrics" (names true) el
                |> Result.bind (fun metrics ->
                    Decoder.field "pipeline" pipelineVerbs el
                    |> Result.map (fun verbs ->
                        { Table = table
                          Metrics = metrics
                          Levels = []
                          Verbs = verbs })))

    /// What a request reads, by the fields its kind declares: `Ok None` for a
    /// kind the language tier does not declare, whose payload is opaque; a
    /// refusal (its path from the payload) when a declared kind's field is
    /// absent or ill-formed. Members a kind does not declare are never read.
    let reach (request: PageRequest) : Result<RequestReach option, DecodeError> =
        match request.Kind with
        | PageRequestKind.Presentation -> presentation request.Payload |> Result.map Some
        | PageRequestKind.Pipeline -> pipeline request.Payload |> Result.map Some
        | _ -> Ok None

[<RequireQualifiedAccess>]
module PageDocument =

    /// The document's own kind and version, carried in the document.
    [<Literal>]
    let Kind = "pageDocument"

    [<Literal>]
    let Version = 1

    // ─── the binding rule ────────────────────────────────────────────────────

    /// Every `Binding.Query` read in the tree, as (reader node id, query name),
    /// in tree order — the binding walk's query reads, the one enumeration the
    /// tier keeps of them (`BindingWalk.collect`).
    let queryReads (tree: Node<'Msg>) : (string * string) list =
        (BindingWalk.collect tree).Uses
        |> List.choose (fun u ->
            match u.Use with
            | BindingWalk.BindingUse.Query(name, _) -> Some(u.Reader, name)
            | _ -> None)
        |> List.distinct

    /// Every disagreement between the tree's sources and the sidecar: each
    /// query naming no request, in tree order, then each request no query
    /// names, in key order. Empty for a bound document.
    let defects (doc: PageDocument) : PageDocumentDefect list =
        let reads = queryReads doc.Tree
        let named = reads |> List.map snd |> Set.ofList

        let dangling =
            reads
            |> List.filter (fun (_, key) -> not (Map.containsKey key doc.Requests))
            |> List.map PageDocumentDefect.DanglingRequestKey

        let unnamed =
            doc.Requests
            |> Map.toList
            |> List.map fst
            |> List.filter (fun key -> not (Set.contains key named))
            |> List.map PageDocumentDefect.UnnamedRequest

        dangling @ unnamed

    /// The page parameters each request depends on, inverted: parameter name to
    /// the request keys a change of it re-resolves. The map a host hands the
    /// bounded driver so a selector re-resolves exactly its dependents.
    let parameterDependents (doc: PageDocument) : Map<string, Set<string>> =
        doc.Requests
        |> Map.fold
            (fun acc key request ->
                request.DependsOn
                |> List.fold
                    (fun acc parameter ->
                        let keys = Map.tryFind parameter acc |> Option.defaultValue Set.empty
                        Map.add parameter (Set.add key keys) acc)
                    acc)
            Map.empty

    // ─── the wire form ───────────────────────────────────────────────────────

    let private requestJson (request: PageRequest) : JVal =
        JObj(
            [ "kind", JStr request.Kind; "payload", request.Payload ]
            @ (match List.distinct request.DependsOn |> List.sort with
               | [] -> []
               | names -> [ "dependsOn", JArr(names |> List.map JStr) ])
        )

    let private requestsJson (requests: Map<string, PageRequest>) : JVal =
        JObj(requests |> Map.toList |> List.map (fun (key, r) -> key, requestJson r))

    let private provenanceJson (p: PageProvenance) : JVal =
        JObj(
            [ "author", JStr p.Author; "at", JStr p.At ]
            @ (match p.SeededFrom with
               | Some s -> [ "seededFrom", JStr s ]
               | None -> [])
            @ (if Map.isEmpty p.OpsHeads then
                   []
               else
                   [ "opsHeads", JObj(p.OpsHeads |> Map.toList |> List.map (fun (k, v) -> k, JStr v)) ])
        )

    /// The document as one JSON value.
    let toJson (doc: PageDocument) : JVal =
        JObj
            [ "kind", JStr Kind
              "version", JInt Version
              "tree", Generated.encodeNodeJson doc.Tree
              "requests", requestsJson doc.Requests
              "provenance", provenanceJson doc.Provenance ]

    /// The document's canonical text (`Rendering.CrossHost`): what a store
    /// holds, byte for byte.
    let encode (doc: PageDocument) : string =
        Canonical.write Rendering.CrossHost (toJson doc)

    /// The content address of the document's tree: SHA-256 over the tree's
    /// canonical encoding, in the `sha256:` form the program wire pins.
    let treeHash (doc: PageDocument) : string =
        Fuaran.Program.Bounded.ProgramWire.ContentAddressPrefix
        + Hash.sha256Hex (Generated.encodeNode doc.Tree)

    /// The content address of the document's sidecar: SHA-256 over the
    /// canonical encoding of the `requests` object.
    let requestsHash (doc: PageDocument) : string =
        Fuaran.Program.Bounded.ProgramWire.ContentAddressPrefix
        + Hash.sha256Hex (Canonical.write Rendering.CrossHost (requestsJson doc.Requests))

    // ─── reading it back ─────────────────────────────────────────────────────

    let private isDigits (s: string) (start: int) (count: int) : bool =
        Seq.forall System.Char.IsDigit (s.Substring(start, count))

    let private twoDigits (s: string) (start: int) : int =
        (int s.[start] - int '0') * 10 + (int s.[start + 1] - int '0')

    /// `YYYY-MM-DDTHH:MM:SSZ`, by hand, so every runtime reads the same text the
    /// same way.
    let private instant: Decoder<string> =
        Decoder.str
        |> Decoder.andThen (fun s ->
            let shaped =
                s.Length = 20
                && isDigits s 0 4
                && s.[4] = '-'
                && isDigits s 5 2
                && s.[7] = '-'
                && isDigits s 8 2
                && s.[10] = 'T'
                && isDigits s 11 2
                && s.[13] = ':'
                && isDigits s 14 2
                && s.[16] = ':'
                && isDigits s 17 2
                && s.[19] = 'Z'

            if
                shaped
                && twoDigits s 5 >= 1
                && twoDigits s 5 <= 12
                && twoDigits s 8 >= 1
                && twoDigits s 8 <= 31
                && twoDigits s 11 < 24
                && twoDigits s 14 < 60
                && twoDigits s 17 < 60
            then
                Ok s
            else
                Error(RootDecodeCode.OutOfRange, "a UTC instant YYYY-MM-DDTHH:MM:SSZ", "'" + s + "'"))

    let private stringMap: Decoder<Map<string, string>> =
        Decoder.obj
        |> Decoder.bind (fun members ->
            fun _ ->
                members
                |> List.fold
                    (fun acc (k, v) ->
                        acc
                        |> Result.bind (fun (m: Map<string, string>) ->
                            if Map.containsKey k m then
                                Error(
                                    DecodeError.under
                                        (PathSegment.Key k)
                                        (DecodeError.make
                                            DecodeCode.OutOfRange
                                            "each name once"
                                            ("'" + k + "' twice"))
                                )
                            else
                                PageRequest.nonEmptyString v
                                |> Result.map (fun s -> Map.add k s m)
                                |> Result.mapError (DecodeError.under (PathSegment.Key k))))
                    (Ok Map.empty))

    let private provenance: Decoder<PageProvenance> =
        Decoder.closed [ "author"; "at"; "opsHeads"; "seededFrom" ] (fun el ->
            Decoder.field "author" PageRequest.nonEmptyString el
            |> Result.bind (fun author ->
                Decoder.field "at" instant el
                |> Result.bind (fun at ->
                    Decoder.optField "seededFrom" PageRequest.nonEmptyString el
                    |> Result.bind (fun seededFrom ->
                        Decoder.fieldOr "opsHeads" Map.empty stringMap el
                        |> Result.map (fun heads ->
                            { SeededFrom = seededFrom
                              OpsHeads = heads
                              Author = author
                              At = at })))))

    let private request: Decoder<PageRequest> =
        Decoder.closed [ "dependsOn"; "kind"; "payload" ] (fun el ->
            Decoder.field "kind" PageRequest.nonEmptyString el
            |> Result.bind (fun kind ->
                Decoder.field "payload" Decoder.json el
                |> Result.bind (fun payload ->
                    Decoder.fieldOr "dependsOn" [] (PageRequest.names false) el
                    |> Result.bind (fun dependsOn ->
                        let r =
                            { Kind = kind
                              Payload = payload
                              DependsOn = List.sort dependsOn }

                        PageRequest.reach r
                        |> Result.mapError (DecodeError.under (PathSegment.Key "payload"))
                        |> Result.map (fun _ -> r)))))

    let private requests: Decoder<Map<string, PageRequest>> =
        Decoder.obj
        |> Decoder.bind (fun members ->
            fun _ ->
                members
                |> List.fold
                    (fun acc (k, v) ->
                        acc
                        |> Result.bind (fun (m: Map<string, PageRequest>) ->
                            let at = DecodeError.under (PathSegment.Key k)

                            if k = "" then
                                Error(at (DecodeError.make DecodeCode.OutOfRange "a non-empty request key" "\"\""))
                            elif Map.containsKey k m then
                                Error(
                                    at (
                                        DecodeError.make
                                            DecodeCode.OutOfRange
                                            "each request key once"
                                            ("'" + k + "' twice")
                                    )
                                )
                            else
                                request v |> Result.map (fun r -> Map.add k r m) |> Result.mapError at))
                    (Ok Map.empty))

    let private tree: Decoder<Node<obj>> =
        fun el -> Generated.decodeNode (Canonical.write Rendering.CrossHost el)

    let private document: Decoder<PageDocument> =
        fun el ->
            // `kind` and `version` are answered FIRST: a document of another kind
            // is told that, rather than refused for a member it never had to have.
            Decoder.field "kind" Decoder.str el
            |> Result.bind (fun kind ->
                if kind <> Kind then
                    Error(
                        DecodeError.under
                            (PathSegment.Key "kind")
                            (DecodeError.make DecodeCode.UnknownTag ("'" + Kind + "'") ("'" + kind + "'"))
                    )
                else
                    Decoder.field "version" Decoder.int el)
            |> Result.bind (fun version ->
                if version <> Version then
                    Error(
                        DecodeError.under
                            (PathSegment.Key "version")
                            (DecodeError.make DecodeCode.OutOfRange (string Version) (string version))
                    )
                else
                    Decoder.members [ "kind"; "provenance"; "requests"; "tree"; "version" ] el)
            |> Result.bind (fun () ->
                Decoder.field "tree" tree el
                |> Result.bind (fun t ->
                    Decoder.field "requests" requests el
                    |> Result.bind (fun rs ->
                        Decoder.field "provenance" provenance el
                        |> Result.map (fun p ->
                            { Tree = t
                              Requests = rs
                              Provenance = p }))))

    /// Every reason `doc` is not a document `decode` would accept: a request
    /// of a declared kind whose fields are ill-formed (the first, `Malformed`,
    /// its path from the document root), then the binding rule (`Unbound`).
    /// `Ok ()` for a document built in code that decodes as itself.
    let validate (doc: PageDocument) : Result<unit, PageRefusal> =
        let payloadFault =
            doc.Requests
            |> Map.toList
            |> List.tryPick (fun (key, r) ->
                match PageRequest.reach r with
                | Ok _ -> None
                | Error e ->
                    Some(
                        DecodeError.within
                            [ PathSegment.Key "requests"; PathSegment.Key key; PathSegment.Key "payload" ]
                            e
                    ))

        match payloadFault with
        | Some e -> Error(PageRefusal.Malformed e)
        | None ->
            match defects doc with
            | [] -> Ok()
            | ds -> Error(PageRefusal.Unbound ds)

    /// Read a page document: one version, every undeclared member refused, each
    /// declared request kind's fields checked, and then the binding rule — a
    /// query naming no request, or a request no query names, refuses the
    /// document with every such defect named.
    let decode (json: string) : Result<PageDocument, PageRefusal> =
        Decoder.parse ReadOptions.strict json
        |> Result.bind document
        |> Result.mapError PageRefusal.Malformed
        |> Result.bind (fun doc ->
            match defects doc with
            | [] -> Ok doc
            | ds -> Error(PageRefusal.Unbound ds))
