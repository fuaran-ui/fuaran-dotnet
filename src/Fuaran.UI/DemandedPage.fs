namespace Fuaran.UI

open Fuaran.Core
open Fuaran.UI.Types

// ============================================================================
//  The demanded projection of a page, and the signed read-set (Phase 2522,
//  WIRE_FORMAT §32.4–32.6).
//
//  `DemandedPage.ofDocument` answers "what can this page ever reach" as a
//  document a deployer can read before the page loads: the tables its requests
//  read, with the metrics and subject levels each reads; the pipeline verbs its
//  pipeline requests run; the node kinds the tree uses; the capabilities and the
//  endpoints the tree names; and the request kinds the sidecar holds. It is a
//  pure, total function of the page document, so a verifier recomputes it.
//
//  `SignedPage` binds a page to a key by the pattern the bounded program core's
//  signed envelope set (`Fuaran.Program.Bounded.SignedEnvelope`): the signature
//  is over a canonical preimage of (tree hash, requests hash, demanded bytes),
//  made through `Fuaran.Core.IAttestationSink`, and verification is by
//  RECOMPUTATION — the verifier re-derives the demanded document from the page
//  it holds, compares, and checks the signature over the preimage it computed,
//  never over the bytes it was handed. The crypto is the host's, as a
//  `ClaimVerifier` over the host's own key type; this file holds none.
//
//  The refusals are named apart, because each asks for a different response:
//  no key offered (refused, never skipped), a foreign key, a document that is
//  not a page, a page breaking the page rules, a page whose reach drifted from
//  its signed read-set (the excess named in the read-set's own vocabulary), and
//  a bad signature over a read-set that matches.
//
//  What is NOT claimed. The signature says a named key paired THIS tree and
//  THIS sidecar with THIS read-set. It does not say the reach is acceptable —
//  that is `DemandedPage.check`'s question, asked of a host's declared coverage
//  — and it does not cover the provenance, which a reader takes as the
//  writer's assertion.
// ============================================================================

/// One table a page reads: the metrics and subject levels its requests read it
/// by, each sorted, none twice.
type DemandedRead =
    { Table: string
      Metrics: string list
      Levels: string list }

/// What a page can ever reach, as a document. Every list is sorted (Ordinal)
/// and holds no member twice; `Reads` is sorted by table.
type DemandedPage =
    {
        /// The tables the requests of declared kinds read.
        Reads: DemandedRead list
        /// The verbs the pipeline requests run.
        Verbs: string list
        /// The wire kind of every node the tree holds, at any position.
        Kinds: string list
        /// The capabilities the tree names: every `Mount`'s capability tags and
        /// every `Invoke`'s capability id.
        Capabilities: string list
        /// The endpoints the tree's `Call` actions name.
        Endpoints: string list
        /// The kinds of the sidecar's requests, declared or not.
        RequestKinds: string list
    }

[<RequireQualifiedAccess>]
module DemandedPage =

    /// The demanded document's own kind and version, carried in the document.
    [<Literal>]
    let Kind = "demandedPage"

    [<Literal>]
    let Version = 1

    /// The page that reaches nothing.
    let empty: DemandedPage =
        { Reads = []
          Verbs = []
          Kinds = []
          Capabilities = []
          Endpoints = []
          RequestKinds = [] }

    let private norm (xs: string list) : string list =
        xs
        |> List.distinct
        |> List.sortWith (fun a b -> System.String.CompareOrdinal(a, b))

    let private normReads (reads: DemandedRead list) : DemandedRead list =
        reads
        |> List.groupBy _.Table
        |> List.map (fun (table, rs) ->
            { Table = table
              Metrics = rs |> List.collect _.Metrics |> norm
              Levels = rs |> List.collect _.Levels |> norm })
        |> List.sortWith (fun a b -> System.String.CompareOrdinal(a.Table, b.Table))

    /// The same document with every list in its canonical order.
    let normalise (d: DemandedPage) : DemandedPage =
        { Reads = normReads d.Reads
          Verbs = norm d.Verbs
          Kinds = norm d.Kinds
          Capabilities = norm d.Capabilities
          Endpoints = norm d.Endpoints
          RequestKinds = norm d.RequestKinds }

    // ─── the projection ──────────────────────────────────────────────────────

    let rec private kindsOf (acc: ResizeArray<string>) (n: Node<'Msg>) =
        acc.Add(KindPolicy.wireKindName n.Kind)
        NodeChildren.children NodeChildren.Reach.keyed n |> List.iter (kindsOf acc)

    let private member' (name: string) (members: (string * JVal) list) : JVal option =
        members |> List.tryPick (fun (k, v) -> if k = name then Some v else None)

    /// The names the tree's canonical JSON carries at the three discriminators
    /// that name reach: `Mount` (its `capabilities`), `Invoke` (its
    /// `capabilityId`, whether action or binding) and `Call` (its `endpoint`).
    /// Each discriminator is written by exactly those cases, so the walk reads
    /// what the wire carries, nothing the type model would have to be asked.
    let private reachOf (tree: JVal) : string list * string list =
        let capabilities = ResizeArray<string>()
        let endpoints = ResizeArray<string>()

        let rec walk (v: JVal) =
            match v with
            | JObj members ->
                match member' "$type" members with
                | Some(JStr "Mount") ->
                    match member' "capabilities" members with
                    | Some(JArr tags) ->
                        for t in tags do
                            match t with
                            | JStr s -> capabilities.Add s
                            | _ -> ()
                    | _ -> ()
                | Some(JStr "Invoke") ->
                    match member' "capabilityId" members with
                    | Some(JStr s) -> capabilities.Add s
                    | _ -> ()
                | Some(JStr "Call") ->
                    match member' "endpoint" members with
                    | Some(JStr s) -> endpoints.Add s
                    | _ -> ()
                | _ -> ()

                for (_, child) in members do
                    walk child
            | JArr items ->
                for item in items do
                    walk item
            | _ -> ()

        walk tree
        List.ofSeq capabilities, List.ofSeq endpoints

    /// The demanded document of a page — what its requests read and run, what
    /// its tree uses and names — or the refusal `PageDocument.validate` gives a
    /// document `PageDocument.decode` would not accept. Pure and total over a
    /// valid document, so a verifier recomputes it from the page alone.
    let ofDocument (doc: PageDocument) : Result<DemandedPage, PageRefusal> =
        PageDocument.validate doc
        |> Result.map (fun () ->
            let reaches =
                doc.Requests
                |> Map.toList
                |> List.choose (fun (_, r) ->
                    match PageRequest.reach r with
                    | Ok reach -> reach
                    | Error _ -> None)

            let kinds = ResizeArray<string>()
            kindsOf kinds doc.Tree
            let capabilities, endpoints = reachOf (Generated.encodeNodeJson doc.Tree)

            normalise
                { Reads =
                    reaches
                    |> List.map (fun r ->
                        { Table = r.Table
                          Metrics = r.Metrics
                          Levels = r.Levels })
                  Verbs = reaches |> List.collect _.Verbs
                  Kinds = List.ofSeq kinds
                  Capabilities = capabilities
                  Endpoints = endpoints
                  RequestKinds = doc.Requests |> Map.toList |> List.map (fun (_, r) -> r.Kind) })

    // ─── the wire form ───────────────────────────────────────────────────────

    let private strs (xs: string list) : JVal = JArr(xs |> List.map JStr)

    /// The document as one JSON value, its lists in canonical order.
    let toJson (d: DemandedPage) : JVal =
        let d = normalise d

        JObj
            [ "kind", JStr Kind
              "version", JInt Version
              "reads",
              JArr(
                  d.Reads
                  |> List.map (fun r ->
                      JObj [ "table", JStr r.Table; "metrics", strs r.Metrics; "levels", strs r.Levels ])
              )
              "verbs", strs d.Verbs
              "kinds", strs d.Kinds
              "capabilities", strs d.Capabilities
              "endpoints", strs d.Endpoints
              "requestKinds", strs d.RequestKinds ]

    /// The demanded document's canonical bytes (`Rendering.CrossHost`) — what a
    /// signature covers and a deployer reads.
    let encode (d: DemandedPage) : string =
        Canonical.write Rendering.CrossHost (toJson d)

    let private read: Decoder<DemandedRead> =
        Decoder.closed [ "levels"; "metrics"; "table" ] (fun el ->
            Decoder.field "table" PageRequest.nonEmptyString el
            |> Result.bind (fun table ->
                Decoder.field "metrics" (PageRequest.names false) el
                |> Result.bind (fun metrics ->
                    Decoder.field "levels" (PageRequest.names false) el
                    |> Result.map (fun levels ->
                        { Table = table
                          Metrics = metrics
                          Levels = levels }))))

    let private document: Decoder<DemandedPage> =
        fun el ->
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
                    Decoder.members
                        [ "capabilities"
                          "endpoints"
                          "kind"
                          "kinds"
                          "reads"
                          "requestKinds"
                          "verbs"
                          "version" ]
                        el)
            |> Result.bind (fun () ->
                let names name =
                    Decoder.field name (PageRequest.names false) el

                Decoder.field "reads" (Decoder.list read) el
                |> Result.bind (fun reads ->
                    names "verbs"
                    |> Result.bind (fun verbs ->
                        names "kinds"
                        |> Result.bind (fun kinds ->
                            names "capabilities"
                            |> Result.bind (fun capabilities ->
                                names "endpoints"
                                |> Result.bind (fun endpoints ->
                                    names "requestKinds"
                                    |> Result.map (fun requestKinds ->
                                        normalise
                                            { Reads = reads
                                              Verbs = verbs
                                              Kinds = kinds
                                              Capabilities = capabilities
                                              Endpoints = endpoints
                                              RequestKinds = requestKinds })))))))

    /// Read a demanded document back: one version, every undeclared member
    /// refused, its lists normalised (a document in another order is the same
    /// document; a name twice is refused).
    let decode (json: string) : Result<DemandedPage, DecodeError> =
        Decoder.parse ReadOptions.strict json |> Result.bind document

    // ─── coverage ────────────────────────────────────────────────────────────

    let private except (xs: string list) (ys: string list) : string list =
        xs |> List.filter (fun x -> not (List.contains x ys))

    /// Everything `a` reaches that `b` does not, as a demanded document: a table
    /// `b` does not read appears whole, a table it reads appears with only the
    /// metrics and levels `b` lacks.
    let excess (a: DemandedPage) (b: DemandedPage) : DemandedPage =
        let a = normalise a
        let b = normalise b

        { Reads =
            a.Reads
            |> List.choose (fun r ->
                match b.Reads |> List.tryFind (fun o -> o.Table = r.Table) with
                | None -> Some r
                | Some o ->
                    match except r.Metrics o.Metrics, except r.Levels o.Levels with
                    | [], [] -> None
                    | metrics, levels ->
                        Some
                            { Table = r.Table
                              Metrics = metrics
                              Levels = levels })
          Verbs = except a.Verbs b.Verbs
          Kinds = except a.Kinds b.Kinds
          Capabilities = except a.Capabilities b.Capabilities
          Endpoints = except a.Endpoints b.Endpoints
          RequestKinds = except a.RequestKinds b.RequestKinds }

    /// `true` when the document reaches nothing.
    let isEmpty (d: DemandedPage) : bool = normalise d = empty

    /// Hold a page's demanded document to a host's declared coverage — the
    /// most the host serves, written as a demanded document. `Ok ()` when the
    /// coverage holds everything the page reaches; otherwise the excess, every
    /// item named, in the demanded document's own vocabulary.
    let check (coverage: DemandedPage) (demanded: DemandedPage) : Result<unit, DemandedPage> =
        let over = excess demanded coverage
        if isEmpty over then Ok() else Error over

/// The signed read-set of a page: the two content addresses and the demanded
/// bytes the signature covers, with the key id and the signature beside them.
/// `TreeHash` and `RequestsHash` are diagnostic, never trusted — a verifier
/// recomputes both.
type SignedPage =
    { TreeHash: string
      RequestsHash: string
      Demanded: string
      KeyId: string
      Signature: string }

/// Why a sign produced no record.
[<RequireQualifiedAccess>]
type PageSignRefusal =
    /// The document is not a page `PageDocument.decode` would accept.
    | InvalidDocument of PageRefusal
    /// The page breaks the page rules (FUARAN166 / FUARAN167): a read-set over
    /// a page with inline rows would attest numbers no request supplies.
    | PageRulesBroken of PreEmitValidate.PreEmitDefect list
    /// The sink answered `None` — the unattested posture. Nothing was signed.
    | SinkDeclined
    /// The sink signed a head other than the preimage it was given.
    | SinkAnsweredForAnotherHead

/// What the page reaches beyond its signed read-set, and what the read-set
/// names that the page does not reach.
type PageDrift =
    {
        /// Reached by the page, absent from the signed read-set — the dangerous
        /// direction.
        Excess: DemandedPage
        /// Named by the signed read-set, not reached by the page.
        Shortfall: DemandedPage
        /// `Some` when the signed bytes are not a demanded document at all; the
        /// excess is then the whole recomputation.
        Unreadable: DecodeError option
    }

/// Why a signed page did not verify. Checked in the order declared; the first
/// refusal is the answer.
[<RequireQualifiedAccess>]
type PageVerifyRefusal =
    /// No public key was offered. Refused, never skipped.
    | NoKey
    /// The record was signed under one key id and the verifier was offered
    /// another. Nothing was recomputed.
    | ForeignKey of signedBy: string * offered: string
    /// The document is not a page `PageDocument.decode` would accept.
    | InvalidDocument of PageRefusal
    /// The page breaks the page rules (FUARAN166 / FUARAN167).
    | PageRulesBroken of PreEmitValidate.PreEmitDefect list
    /// The recomputed read-set is not the signed one; the difference is named.
    | Drift of drift: PageDrift
    /// The read-set describes the page exactly, but the signature does not
    /// verify over the RECOMPUTED preimage under the offered key. The carried
    /// and recomputed hashes tell "the tree or sidecar moved without moving its
    /// reach" (they differ) from "the bytes are forged" (they agree).
    | BadSignature of
        carriedTreeHash: string *
        recomputedTreeHash: string *
        carriedRequestsHash: string *
        recomputedRequestsHash: string

/// A page that verified: the RECOMPUTED facts, never the carried ones.
type VerifiedPage =
    { TreeHash: string
      RequestsHash: string
      Demanded: DemandedPage
      KeyId: string }

[<RequireQualifiedAccess>]
module SignedPage =

    /// The signed record's own kind and version, carried in the record and in
    /// the preimage, so the signed bytes cannot be mistaken for any other
    /// attestation the sink is asked for.
    [<Literal>]
    let Kind = "signedPage"

    [<Literal>]
    let Version = 1

    let private signedMembers (treeHash: string) (requestsHash: string) (demanded: string) =
        [ "kind", JStr Kind
          "version", JInt Version
          "treeHash", JStr treeHash
          "requestsHash", JStr requestsHash
          "demanded", JStr demanded ]

    /// The canonical preimage the sink signs and the verifier recomputes.
    let preimage (treeHash: string) (requestsHash: string) (demanded: string) : string =
        Canonical.write Rendering.CrossHost (JObj(signedMembers treeHash requestsHash demanded))

    let private pageRules (doc: PageDocument) : PreEmitValidate.PreEmitDefect list =
        PreEmitValidate.pageRuleDefects (doc.Requests |> Map.toList |> List.map fst |> Set.ofList) doc.Tree

    /// Sign a page's read-set through a host-supplied sink: the demanded
    /// document is derived here, never taken from the caller. A document that is
    /// not a page, or a page that breaks the page rules, is not signed.
    let sign (sink: IAttestationSink) (doc: PageDocument) : Result<SignedPage, PageSignRefusal> =
        match DemandedPage.ofDocument doc with
        | Error r -> Error(PageSignRefusal.InvalidDocument r)
        | Ok demanded ->
            match pageRules doc with
            | _ :: _ as broken -> Error(PageSignRefusal.PageRulesBroken broken)
            | [] ->
                let bytes = DemandedPage.encode demanded
                let treeHash = PageDocument.treeHash doc
                let requestsHash = PageDocument.requestsHash doc
                let head = preimage treeHash requestsHash bytes

                match sink.Sign head with
                | None -> Error PageSignRefusal.SinkDeclined
                | Some a when a.Head <> head -> Error PageSignRefusal.SinkAnsweredForAnotherHead
                | Some a ->
                    Ok(
                        { TreeHash = treeHash
                          RequestsHash = requestsHash
                          Demanded = bytes
                          KeyId = a.KeyId
                          Signature = a.Signature }
                        : SignedPage
                    )

    let private driftOf (recomputed: DemandedPage) (signed: string) : PageDrift option =
        match DemandedPage.decode signed with
        | Ok carried when carried = recomputed -> None
        | Ok carried ->
            Some
                { Excess = DemandedPage.excess recomputed carried
                  Shortfall = DemandedPage.excess carried recomputed
                  Unreadable = None }
        | Error e ->
            Some
                { Excess = recomputed
                  Shortfall = DemandedPage.empty
                  Unreadable = Some e }

    /// Verify a signed page by recomputation, under a public key. COLD: the
    /// page, the key and the crypto are everything it reads. `key` is optional
    /// only so its absence can be refused. Asynchronous because the crypto seam
    /// is.
    let verify
        (crypto: Fuaran.Program.Bounded.ClaimVerifier<'Key>)
        (key: 'Key option)
        (doc: PageDocument)
        (signed: SignedPage)
        : Async<Result<VerifiedPage, PageVerifyRefusal>> =
        async {
            match key with
            | None -> return Error PageVerifyRefusal.NoKey
            | Some key when crypto.KeyId key <> signed.KeyId ->
                return Error(PageVerifyRefusal.ForeignKey(signed.KeyId, crypto.KeyId key))
            | Some key ->
                match DemandedPage.ofDocument doc with
                | Error r -> return Error(PageVerifyRefusal.InvalidDocument r)
                | Ok demanded ->
                    match pageRules doc with
                    | _ :: _ as broken -> return Error(PageVerifyRefusal.PageRulesBroken broken)
                    | [] ->
                        match driftOf demanded signed.Demanded with
                        | Some drift -> return Error(PageVerifyRefusal.Drift drift)
                        | None ->
                            let treeHash = PageDocument.treeHash doc
                            let requestsHash = PageDocument.requestsHash doc
                            // Over the RECOMPUTED preimage: the carried hashes and
                            // bytes have no say in what is checked.
                            let head = preimage treeHash requestsHash (DemandedPage.encode demanded)
                            let! verified = crypto.Verify head signed.Signature key

                            if verified then
                                return
                                    Ok
                                        { TreeHash = treeHash
                                          RequestsHash = requestsHash
                                          Demanded = demanded
                                          KeyId = crypto.KeyId key }
                            else
                                return
                                    Error(
                                        PageVerifyRefusal.BadSignature(
                                            signed.TreeHash,
                                            treeHash,
                                            signed.RequestsHash,
                                            requestsHash
                                        )
                                    )
        }

    /// The signed record as a self-describing canonical document.
    let encode (signed: SignedPage) : string =
        Canonical.write
            Rendering.CrossHost
            (JObj(
                signedMembers signed.TreeHash signed.RequestsHash signed.Demanded
                @ [ "keyId", JStr signed.KeyId; "signature", JStr signed.Signature ]
            ))

    let private address: Decoder<string> =
        Decoder.str
        |> Decoder.andThen (fun s ->
            if Fuaran.Program.Bounded.ProgramWire.isContentAddress s then
                Ok s
            else
                Error(RootDecodeCode.OutOfRange, "a 'sha256:' content address", "'" + s + "'"))

    /// Read a signed record back: one version, every undeclared member refused.
    /// The demanded document inside is NOT decoded here — `verify` reads it, and
    /// reports an unreadable one as drift.
    let decode (json: string) : Result<SignedPage, DecodeError> =
        Decoder.parse ReadOptions.strict json
        |> Result.bind (fun el ->
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
                    Decoder.members
                        [ "demanded"
                          "keyId"
                          "kind"
                          "requestsHash"
                          "signature"
                          "treeHash"
                          "version" ]
                        el)
            |> Result.bind (fun () ->
                Decoder.field "treeHash" address el
                |> Result.bind (fun treeHash ->
                    Decoder.field "requestsHash" address el
                    |> Result.bind (fun requestsHash ->
                        Decoder.field "demanded" Decoder.str el
                        |> Result.bind (fun demanded ->
                            Decoder.field "keyId" Decoder.str el
                            |> Result.bind (fun keyId ->
                                Decoder.field "signature" Decoder.str el
                                |> Result.map (fun signature ->
                                    ({ TreeHash = treeHash
                                       RequestsHash = requestsHash
                                       Demanded = demanded
                                       KeyId = keyId
                                       Signature = signature }
                                    : SignedPage))))))))
