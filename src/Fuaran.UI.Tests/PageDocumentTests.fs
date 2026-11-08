module Fuaran.UI.Tests.PageDocumentTests

// ============================================================================
//  Phase 2522 - the page document and its demanded projection.
//
//  A page document is a tree, a sidecar of requests its query sources name,
//  and provenance (WIRE_FORMAT §32). These tests pin its codec (byte-identical
//  round trips, the binding rule's two refusals), the demanded projection
//  (hand-stated answers, not the implementation's), the signed read-set
//  (sign, verify by recomputation, and each refusal named apart: no key, a
//  foreign key, drift with the excess named, a bad signature), the validator's
//  page-document mode (FUARAN166, FUARAN167), the per-source schema check, and
//  the `page-document/` corpus family, which this suite emits with
//  `FUARAN_REGEN=1` and otherwise compares byte for byte.
// ============================================================================

open System
open System.IO
open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types

// ─── authored cases ──────────────────────────────────────────────────────────

let private documentOf (tree: string) (requests: string) (provenance: string) : string =
    sprintf """{"kind":"pageDocument","version":1,"tree":%s,"requests":%s,"provenance":%s}""" tree requests provenance

let private provenance =
    """{"author":"team-admin","at":"2026-10-30T09:00:00Z","opsHeads":{"platform":"sha256:0000000000000000000000000000000000000000000000000000000000000001"},"seededFrom":"sales/overview@1.0.0"}"""

let private presentationTree =
    """{"id":"page","kind":{"$type":"Box","children":[{"id":"revenue-today","kind":{"$type":"Metric","label":"Revenue today","value":{"$type":"Query","name":"revenue-today"}}},{"id":"revenue-trend","kind":{"$type":"Chart","kind":"Line","source":{"$type":"Query","name":"revenue-by-store-30d","dependsOn":["range"]},"title":"Revenue by store","xField":"day","yFields":["revenue","orders"]}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""

let private presentationRequests =
    """{"revenue-today":{"kind":"presentation","payload":{"kind":"comparison","metrics":["revenue"],"period":{"day":"2026-10-30","rangeDays":1},"tableId":"sales.store"}},"revenue-by-store-30d":{"dependsOn":["range"],"kind":"presentation","payload":{"kind":"trend","metrics":["revenue","orders"],"period":{"day":"2026-10-30","rangeDays":30},"subjectLevel":"store","tableId":"sales.store"}}}"""

/// A stored analytics page: a metric and a chart, each on a presentation request.
let presentationPage = documentOf presentationTree presentationRequests provenance

/// Phase 2568 — the same page with the chart's series read from its rows: an
/// empty `yFields` over the trend request, whose columns the resolver names (one
/// per store). Only the tree's `yFields` differs from `presentationPage`.
let derivedSeriesPage =
    documentOf
        (presentationTree.Replace("\"yFields\":[\"revenue\",\"orders\"]", "\"yFields\":[]"))
        presentationRequests
        provenance

let private pipelineTree =
    """{"id":"analysis","kind":{"$type":"Box","children":[{"id":"basket","kind":{"$type":"DataGrid","columns":[{"field":"category","kind":{"$type":"Text"},"label":"Category"},{"field":"perShopper","kind":{"$type":"Text"},"label":"Per shopper"}],"rowKeyField":"category","source":{"$type":"Query","name":"basket-by-category"}}},{"id":"orders-spark","kind":{"$type":"Sparkline","source":{"$type":"Query","name":"orders-7d","dependsOn":["store"]}}},{"id":"lookup","kind":{"$type":"Metric","label":"Lookup","value":{"$type":"Query","name":"lookup"}}},{"id":"export","kind":{"$type":"Button","label":"Export","onClick":{"$type":"Invoke","args":[],"capabilityId":"export.csv"}}},{"id":"refresh","kind":{"$type":"Button","label":"Refresh","onClick":{"$type":"Call","endpoint":"/api/refresh"}}},{"id":"guest","kind":{"$type":"Mount","capabilities":["notify"],"channel":{"direction":"OutOnly"},"scopeId":"guest-panel"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""

let private pipelineRequests =
    """{"basket-by-category":{"kind":"pipeline","payload":{"metrics":["revenue","shoppers"],"pipeline":[{"$type":"groupBy","aggs":[{"fn":"sum","name":"revenue","of":"revenue"},{"fn":"sum","name":"shoppers","of":"shoppers"}],"keys":["category"]},{"$type":"derive","expr":{"$type":"col","name":"revenue"},"name":"perShopper"}],"tableId":"sales.category"}},"lookup":{"kind":"host.lookup","payload":{"q":"anything the host reads"}},"orders-7d":{"dependsOn":["store"],"kind":"presentation","payload":{"metrics":["orders"],"subjectLevel":"store","tableId":"sales.store"}}}"""

/// A pipeline page: a grid on a pipeline request, a sparkline on a presentation
/// request, a metric on a request of a kind the language tier does not declare,
/// and the three discriminators that name reach (Invoke, Call, Mount).
let pipelinePage = documentOf pipelineTree pipelineRequests provenance

let private decodeOk (json: string) : PageDocument =
    match PageDocument.decode json with
    | Ok d -> d
    | Error r -> failtestf "authored page refused: %A" r

let private canonicalOf (json: string) : string = PageDocument.encode (decodeOk json)

let private demandedOf (doc: PageDocument) : DemandedPage =
    match DemandedPage.ofDocument doc with
    | Ok d -> d
    | Error r -> failtestf "demanded projection refused: %A" r

// ─── a test signer: a keyed digest, the shape of a real one ──────────────────

let private signatureOf (key: string) (head: string) : string = Hash.sha256Hex (key + "|" + head)

let private sinkFor (key: string) : IAttestationSink =
    { new IAttestationSink with
        member _.Sign head =
            Some
                { Head = head
                  KeyId = key
                  Signature = signatureOf key head }

        member _.Verify attestation head =
            attestation.Signature = signatureOf attestation.KeyId head }

let private crypto: Fuaran.Program.Bounded.ClaimVerifier<string> =
    { KeyId = id
      Verify = fun head signature key -> async { return signature = signatureOf key head } }

let private signOk (doc: PageDocument) : SignedPage =
    match SignedPage.sign (sinkFor "page-key") doc with
    | Ok s -> s
    | Error r -> failtestf "sign refused: %A" r

let private verify (key: string option) (doc: PageDocument) (signed: SignedPage) =
    SignedPage.verify crypto key doc signed |> Async.RunSynchronously

let private codesOf (defects: PreEmitValidate.PreEmitDefect list) : string list =
    defects
    |> List.map (fun d -> let code, _, _ = PreEmitValidate.describe d in code)

/// A page whose tree is `tree` and whose sidecar holds one presentation request
/// per key named.
let private pageWith (tree: string) (keys: string list) : string =
    let requests =
        keys
        |> List.map (fun k ->
            sprintf
                "\"%s\":{\"kind\":\"presentation\",\"payload\":{\"metrics\":[\"revenue\"],\"tableId\":\"sales.store\"}}"
                k)
        |> String.concat ","

    documentOf tree ("{" + requests + "}") provenance

let private chartInlineRows =
    """{"id":"inline-chart","kind":{"$type":"Chart","kind":"Bar","source":{"$type":"Static","value":[{"day":"Mon","revenue":10}]},"xField":"day","yFields":["revenue"]}}"""

/// Phase 2568 — an empty `yFields` over inline rows: the columns are the tree's
/// own, so the series are the author's to name.
let private chartDerivedInlineRows =
    """{"id":"inline-derived","kind":{"$type":"Chart","kind":"Line","source":{"$type":"Static","value":[{"day":"Mon","north":10,"south":7}]},"xField":"day","yFields":[]}}"""

let private metricOnState =
    """{"id":"m","kind":{"$type":"Metric","label":"Total","value":{"$type":"State","key":"total"}}}"""

let private gridStaticRows =
    """{"id":"g","kind":{"$type":"DataGrid","columns":[{"field":"a","kind":{"$type":"Text"},"label":"A"}],"source":{"$type":"Static"},"staticRows":{"headers":["A"],"rows":[["1"]]}}}"""

// ─── the corpus family ───────────────────────────────────────────────────────

/// The `page-document/` family (WIRE_FORMAT §32.7), computed by the reference.
module Vectors =

    let FamilyDir = "page-document"
    let FileName = "page-document-vectors.json"

    let private parse (text: string) : JVal =
        match Canonical.read text with
        | Ok v -> v
        | Error e -> failwithf "canonical text unreadable: %A" e

    let private accepts =
        [ "presentation-page", presentationPage
          "pipeline-page", pipelinePage
          "derived-series-page", derivedSeriesPage ]

    /// Documents decode refuses: (id, input).
    let rejects =
        [ "dangling-request-key",
          pageWith
              """{"id":"m","kind":{"$type":"Metric","label":"Revenue","value":{"$type":"Query","name":"revenue-yesterday"}}}"""
              []
          "request-no-source-names",
          pageWith
              """{"id":"m","kind":{"$type":"Metric","label":"Revenue","value":{"$type":"Query","name":"revenue"}}}"""
              [ "revenue"; "orphan" ]
          "presentation-without-metrics",
          documentOf
              """{"id":"m","kind":{"$type":"Metric","label":"Revenue","value":{"$type":"Query","name":"revenue"}}}"""
              """{"revenue":{"kind":"presentation","payload":{"tableId":"sales.store"}}}"""
              provenance
          "undeclared-member",
          presentationPage.Substring(0, presentationPage.Length - 1)
          + ",\"signature\":\"x\"}"
          "wrong-version", presentationPage.Replace("\"version\":1", "\"version\":2") ]

    /// Documents decode accepts and the page rules refuse: (id, input).
    let rules =
        [ "chart-inline-rows", pageWith chartInlineRows []
          "metric-on-state", pageWith metricOnState []
          "grid-static-rows", pageWith gridStaticRows []
          "chart-derived-inline-rows", pageWith chartDerivedInlineRows [] ]

    let private refusalJson (r: PageRefusal) : JVal =
        match r with
        | PageRefusal.Malformed e ->
            JObj
                [ "refusal", JStr "Malformed"
                  "code", JStr(DecodeError.codeName e.Code)
                  "path", JStr(DecodePath.render e.Path) ]
        | PageRefusal.Unbound defects ->
            JObj
                [ "refusal", JStr "Unbound"
                  "defects",
                  JArr(
                      defects
                      |> List.map (fun d ->
                          match d with
                          | PageDocumentDefect.DanglingRequestKey(nodeId, key) ->
                              JObj [ "defect", JStr "DanglingRequestKey"; "nodeId", JStr nodeId; "key", JStr key ]
                          | PageDocumentDefect.UnnamedRequest key ->
                              JObj [ "defect", JStr "UnnamedRequest"; "key", JStr key ])
                  ) ]

    let private ruleJson (d: PreEmitValidate.PreEmitDefect) : JVal =
        let code, _, _ = PreEmitValidate.describe d

        match d with
        | PreEmitValidate.PreEmitDefect.PageSourceNotRequest(nodeId, _, slot, _)
        | PreEmitValidate.PreEmitDefect.PageInlineRows(nodeId, _, slot) ->
            JObj [ "code", JStr code; "nodeId", JStr nodeId; "slot", JStr slot ]
        | other -> failwithf "a page rule vector raised a defect outside the page rules: %A" other

    /// The drift vector: the presentation page signed, then a request and a
    /// metric reading a table its read-set never named added to it.
    let driftedPage =
        presentationPage
            .Replace(
                "\"revenue-today\":{",
                "\"basket\":{\"kind\":\"presentation\",\"payload\":{\"metrics\":[\"shoppers\"],\"tableId\":\"sales.category\"}},\"revenue-today\":{"
            )
            .Replace(
                "\"children\":[",
                "\"children\":[{\"id\":\"basket\",\"kind\":{\"$type\":\"Metric\",\"label\":\"Shoppers\",\"value\":{\"$type\":\"Query\",\"name\":\"basket\"}}},"
            )

    let private description =
        "The page document (WIRE_FORMAT.md 32, Phase 2522). SELF-ENUMERATED (27.7): page-document/manifest.json names this file, so the directory is one corpus family, page-document. Emitted by the reference host (fuaran-dotnet: FUARAN_REGEN=1 over Fuaran.UI.Tests); the inputs are authored there and every answer is the reference's. accept: decode canonical with the host's page-document codec and re-encode it; the bytes must be equal (32.2). demanded is the canonical demanded document the host derives from it (32.4), treeHash and requestsHash its two content addresses and preimage the bytes a signature covers (32.5), each compared byte for byte. reject: decode input; the host must refuse it with expected (32.3: Malformed with the decode code and path, or Unbound with every defect in order). rules: decode input (it is accepted), run the page-document validator mode (32.6) and compare its findings, code, node id and slot in walk order, with expectedDefects. drift: verify document against a read-set signed over signedDemanded; the host must refuse it as drift, its excess and shortfall the canonical demanded documents given (32.5)."

    /// The whole family file, as the reference computes it.
    let emit () : string =
        let accept =
            accepts
            |> List.map (fun (id, json) ->
                let doc = decodeOk json
                let demanded = DemandedPage.encode (demandedOf doc)
                let treeHash = PageDocument.treeHash doc
                let requestsHash = PageDocument.requestsHash doc

                JObj
                    [ "id", JStr id
                      "canonical", JStr(PageDocument.encode doc)
                      "demanded", JStr demanded
                      "treeHash", JStr treeHash
                      "requestsHash", JStr requestsHash
                      "preimage", JStr(SignedPage.preimage treeHash requestsHash demanded) ])

        let reject =
            rejects
            |> List.map (fun (id, json) ->
                match PageDocument.decode json with
                | Ok _ -> failwithf "%s: the reference ACCEPTED a document the family says it refuses" id
                | Error r -> JObj [ "id", JStr id; "input", JStr json; "expected", refusalJson r ])

        let rule =
            rules
            |> List.map (fun (id, json) ->
                let doc = decodeOk json
                let keys = doc.Requests |> Map.toList |> List.map fst |> Set.ofList

                match PreEmitValidate.pageRuleDefects keys doc.Tree with
                | [] -> failwithf "%s: the page rules ACCEPTED a document the family says they refuse" id
                | ds ->
                    JObj
                        [ "id", JStr id
                          "input", JStr(PageDocument.encode doc)
                          "expectedDefects", JArr(ds |> List.map ruleJson) ])

        let drift =
            let signed = signOk (decodeOk presentationPage)
            let doc = decodeOk driftedPage

            match verify (Some "page-key") doc signed with
            | Error(PageVerifyRefusal.Drift d) ->
                [ JObj
                      [ "id", JStr "request-added-after-signing"
                        "document", JStr(PageDocument.encode doc)
                        "signedDemanded", JStr signed.Demanded
                        "excess", JStr(DemandedPage.encode d.Excess)
                        "shortfall", JStr(DemandedPage.encode d.Shortfall) ] ]
            | other -> failwithf "the drift vector did not drift: %A" other

        let file =
            JObj
                [ "version", JInt 1
                  "description", JStr description
                  "accept", JArr accept
                  "reject", JArr reject
                  "rules", JArr rule
                  "drift", JArr drift ]

        Canonical.write Rendering.CrossHost file + "\n"

    let regenerating = Environment.GetEnvironmentVariable "FUARAN_REGEN" = "1"

    let manifest =
        Canonical.write
            Rendering.CrossHost
            (JObj
                [ "version", JInt 1
                  "description",
                  JStr
                      "The page document (WIRE_FORMAT.md 32, Phase 2522). SELF-ENUMERATED, so the directory is one corpus family (27.7): `page-document`. page-document-vectors.json carries accepted documents with their canonical bytes, demanded documents, content addresses and signing preimages (32.2, 32.4, 32.5), documents the codec refuses (32.3), documents the page-document validator mode refuses (32.6), and a read-set drift (32.5). A host that renders a page document runs every vector through its own codec and derivation and compares byte for byte; a host that renders none declares the family not applicable (32.8). Emitted by the reference host (fuaran-dotnet, FUARAN_REGEN=1 over Fuaran.UI.Tests)."
                  "files", JArr [ JStr FileName ] ])
        + "\n"

// ─── the signed-page attestation vectors (Phase 2570) ────────────────────────

/// `attestation/page-corpus.json` in the specification repository: pages signed
/// by the .NET ECDSA P-256 provider, beside the refusals any verifier — the BCL
/// arm here, WebCrypto in the browser — must name. ECDSA signatures are not
/// deterministic and the signing key is discarded, so the file cannot be
/// re-emitted byte for byte. The check is therefore SEMANTIC: the committed
/// file holds exactly the vectors below, each document is the reference's
/// canonical page, and the .NET provider gives every expected verdict.
/// `FUARAN_REGEN=1` re-signs under a fresh key only when that check fails.
module SignedPageVectors =

    open System.Security.Cryptography
    open Fuaran.UI.OpStream.Abstractions

    let FileName = "page-corpus.json"

    [<Literal>]
    let SignerKeyId = "page-key-2026"

    /// The .NET provider, as the published `ClaimVerifier` wraps it.
    let crypto: Fuaran.Program.Bounded.ClaimVerifier<KeyDirectoryEntry> =
        { KeyId = fun key -> key.KeyId
          Verify = fun payload signature key -> EcdsaP256.claimVerifier.VerifyClaim payload signature key }

    /// What the record a vector carries was signed over.
    type SignedOver =
        | Presentation
        | Pipeline
        | PresentationForged

    /// (id, document, record, offered key name, expected verdict). The key
    /// names: `signer` (the signing key's public half), `foreign` (the same
    /// public half under another key id), `impostor` (another P-256 key under
    /// the signer's id) and `other-curve` (a secp256k1 key under the signer's
    /// id — a curve the algorithm id does not name, which both arms refuse).
    let pages: (string * string * SignedOver * string option * string) list =
        [ "accept-presentation-page", presentationPage, Presentation, Some "signer", "Verified"
          "accept-pipeline-page", pipelinePage, Pipeline, Some "signer", "Verified"
          "refuse-no-key", presentationPage, Presentation, None, "NoKey"
          "refuse-foreign-key", presentationPage, Presentation, Some "foreign", "ForeignKey"
          "refuse-drift", Vectors.driftedPage, Presentation, Some "signer", "Drift"
          "refuse-tree-moved",
          presentationPage.Replace("Revenue by store", "Store revenue"),
          Presentation,
          Some "signer",
          "BadSignature"
          "refuse-forged-signature", presentationPage, PresentationForged, Some "signer", "BadSignature"
          "refuse-impostor-key", presentationPage, Presentation, Some "impostor", "BadSignature"
          "refuse-other-curve-key", presentationPage, Presentation, Some "other-curve", "BadSignature" ]

    /// The verdict's name: `Verified`, or the refusal's §32.5 name.
    let verdictOf (result: Result<VerifiedPage, PageVerifyRefusal>) : string =
        match result with
        | Ok _ -> "Verified"
        | Error refusal -> SignedPage.refusalName refusal

    let private description =
        "Signed pages (WIRE_FORMAT.md 32.5, Phase 2570) - the page document's signed read-set under ecdsa-p256-sha256-v1, read by name like the two other files here and outside manifest.json (26.7). keys: key directory entries, each with a local `name`; publicKeySpki is base64 DER SubjectPublicKeyInfo. pages: decode `document` with the page-document codec and `signed` with the signed-record codec, offer the key named by `key` (no `key` member: offer none), verify (32.5: recompute, then check the base64 IEEE P1363 r||s signature over the UTF-8 bytes of the recomputed preimage) and assert the verdict is `expect`: Verified, or the refusal's 32.5 name (NoKey, ForeignKey, Drift, BadSignature). The signer, foreign and impostor keys are on P-256; other-curve is a secp256k1 key under the signer's id, which a verifier refuses because the algorithm id names P-256. Signed by the reference host's .NET provider under a key that was then discarded, so the vectors are re-signed, not re-emitted: fuaran-dotnet checks them semantically, and FUARAN_REGEN=1 over Fuaran.UI.Tests re-signs only when that check fails. `coverage` records which host pipelines run the file."

    let private coverage =
        JObj
            [ "fuaran-dotnet (.NET)",
              JStr "covered - Fuaran.UI.Tests (PageDocumentTests) verifies every vector through EcdsaP256 on the BCL"
              "fuaran-dotnet (Fable, browser)",
              JStr
                  "covered - tests/fable-laws law 10 verifies every vector through UiWitness.ecdsaP256 under WebCrypto and byte-compares its verdict lines with the .NET run" ]

    let private sinkOf (key: ECDsa) : IAttestationSink =
        { new IAttestationSink with
            member _.Sign head =
                Some
                    { Head = head
                      KeyId = SignerKeyId
                      Signature =
                        Convert.ToBase64String(key.SignData(Text.Encoding.UTF8.GetBytes head, HashAlgorithmName.SHA256)) }

            member _.Verify _ _ = false }

    let private entryJson (name: string) (keyId: string) (spki: string) : JVal =
        JObj
            [ "algorithm", JStr AttestationAlgorithm.ecdsaP256Sha256V1
              "keyId", JStr keyId
              "name", JStr name
              "publicKeySpki", JStr spki ]

    /// Sign every vector under a fresh key and render the whole file.
    let emitFresh () : string =
        use signer = ECDsa.Create ECCurve.NamedCurves.nistP256
        use impostor = ECDsa.Create ECCurve.NamedCurves.nistP256
        use otherCurve = ECDsa.Create(ECCurve.CreateFromValue "1.3.132.0.10")

        let spki (k: ECDsa) =
            Convert.ToBase64String(k.ExportSubjectPublicKeyInfo())

        let signedOf (json: string) =
            match SignedPage.sign (sinkOf signer) (decodeOk json) with
            | Ok s -> s
            | Error r -> failwithf "the reference refused to sign a vector page: %A" r

        let presentation = signedOf presentationPage
        let pipeline = signedOf pipelinePage

        let forged =
            let bytes = Convert.FromBase64String presentation.Signature
            bytes.[bytes.Length - 1] <- bytes.[bytes.Length - 1] ^^^ 1uy

            { presentation with
                Signature = Convert.ToBase64String bytes }

        let keys =
            [ entryJson "signer" SignerKeyId (spki signer)
              entryJson "foreign" "another-page-key" (spki signer)
              entryJson "impostor" SignerKeyId (spki impostor)
              entryJson "other-curve" SignerKeyId (spki otherCurve) ]

        let pageJson (id, json, over, key: string option, expect) =
            let record =
                match over with
                | Presentation -> presentation
                | Pipeline -> pipeline
                | PresentationForged -> forged

            JObj(
                [ "document", JStr(PageDocument.encode (decodeOk json))
                  "expect", JStr expect
                  "id", JStr id ]
                @ (key |> Option.map (fun k -> "key", JStr k) |> Option.toList)
                @ [ "signed", JStr(SignedPage.encode record) ]
            )

        Canonical.write
            Rendering.CrossHost
            (JObj
                [ "coverage", coverage
                  "description", JStr description
                  "keys", JArr keys
                  "pages", JArr(pages |> List.map pageJson)
                  "version", JInt 1 ])
        + "\n"

    let private str (name: string) (fields: (string * JVal) list) : string option =
        fields
        |> List.tryPick (fun (k, v) ->
            match v with
            | JStr s when k = name -> Some s
            | _ -> None)

    let private parseKey (v: JVal) : string * KeyDirectoryEntry =
        match v with
        | JObj fields ->
            match str "name" fields, str "keyId" fields, str "algorithm" fields, str "publicKeySpki" fields with
            | Some name, Some keyId, Some algorithm, Some spki ->
                name,
                { KeyId = keyId
                  Algorithm = algorithm
                  PublicKeySpki = spki
                  NotBefore = None
                  Expires = None
                  RevokedFrom = None }
            | _ -> failwith "a key entry lacks a member"
        | _ -> failwith "a key entry is not an object"

    /// Every way the committed file differs from what it must say; empty when
    /// it holds exactly the vectors above and the .NET provider agrees with each.
    let problems (text: string) : string list =
        match Canonical.read text with
        | Error e -> [ sprintf "the file is not canonical JSON: %A" e ]
        | Ok(JObj fields) ->
            let memberOf name =
                fields |> List.tryPick (fun (k, v) -> if k = name then Some v else None)

            let keys =
                match memberOf "keys" with
                | Some(JArr ks) -> ks |> List.map parseKey |> Map.ofList
                | _ -> Map.empty

            match memberOf "pages" with
            | Some(JArr committed) when List.length committed = List.length pages ->
                List.zip pages committed
                |> List.collect (fun ((id, json, _, key, expect), v) ->
                    match v with
                    | JObj page ->
                        let carried = str "id" page, str "document" page, str "key" page, str "expect" page
                        let wanted = Some id, Some(PageDocument.encode (decodeOk json)), key, Some expect

                        if carried <> wanted then
                            [ sprintf "%s: the vector is not the reference's (id, document, key or expect)" id ]
                        else
                            match str "signed" page |> Option.map SignedPage.decode with
                            | Some(Ok signed) ->
                                let offered = key |> Option.bind (fun k -> Map.tryFind k keys)

                                let verdict =
                                    SignedPage.verify crypto offered (decodeOk json) signed
                                    |> Async.RunSynchronously
                                    |> verdictOf

                                if verdict = expect then
                                    []
                                else
                                    [ sprintf
                                          "%s: the .NET provider answers %s, the vector expects %s"
                                          id
                                          verdict
                                          expect ]
                            | _ -> [ sprintf "%s: the signed record does not decode" id ]
                    | _ -> [ sprintf "%s: the vector is not an object" id ])
            | _ -> [ "the file does not hold exactly the reference's pages" ]
        | Ok _ -> [ "the file is not an object" ]

// ─── the tests ───────────────────────────────────────────────────────────────

[<Tests>]
let codecTests =
    testList
        "Phase 2522 - the page document codec"
        [ test "each authored page round-trips byte-identically through its canonical form" {
              for json in [ presentationPage; pipelinePage ] do
                  let canonical = canonicalOf json
                  Expect.equal (canonicalOf canonical) canonical "decode . encode is the identity on canonical bytes"
                  Expect.isTrue (Canonical.isCanonical Rendering.CrossHost canonical) "the encoding is canonical"
          }

          test "a query naming no request is refused, naming the reader and the key" {
              let json = fst Vectors.rejects.[0], snd Vectors.rejects.[0]

              match PageDocument.decode (snd json) with
              | Error(PageRefusal.Unbound [ PageDocumentDefect.DanglingRequestKey("m", "revenue-yesterday") ]) -> ()
              | other -> failtestf "expected the dangling key, got %A" other
          }

          test "a request no query names is refused, naming the key" {
              match PageDocument.decode (snd Vectors.rejects.[1]) with
              | Error(PageRefusal.Unbound [ PageDocumentDefect.UnnamedRequest "orphan" ]) -> ()
              | other -> failtestf "expected the unnamed request, got %A" other
          }

          test "a declared kind's absent field is refused at its path" {
              match PageDocument.decode (snd Vectors.rejects.[2]) with
              | Error(PageRefusal.Malformed e) ->
                  Expect.equal e.Code DecodeCode.MissingField "the code"

                  Expect.equal
                      (DecodePath.render e.Path)
                      "$[\"requests\"][\"revenue\"][\"payload\"][\"metrics\"]"
                      "the path"
              | other -> failtestf "expected MissingField, got %A" other
          }

          test "an undeclared member and another version are refused" {
              match PageDocument.decode (snd Vectors.rejects.[3]) with
              | Error(PageRefusal.Malformed e) -> Expect.equal e.Code DecodeCode.UndeclaredMember "undeclared"
              | other -> failtestf "expected UndeclaredMember, got %A" other

              match PageDocument.decode (snd Vectors.rejects.[4]) with
              | Error(PageRefusal.Malformed e) -> Expect.equal e.Code DecodeCode.OutOfRange "version"
              | other -> failtestf "expected the version refusal, got %A" other
          }

          test "parameterDependents inverts the sidecar's dependsOn" {
              let doc = decodeOk pipelinePage

              Expect.equal
                  (PageDocument.parameterDependents doc)
                  (Map.ofList [ "store", Set.ofList [ "orders-7d" ] ])
                  "store re-resolves exactly orders-7d"
          } ]

[<Tests>]
let demandedTests =
    testList
        "Phase 2522 - the demanded projection"
        [ test "the presentation page reads what its requests declare, and no more" {
              Expect.equal
                  (demandedOf (decodeOk presentationPage))
                  { DemandedPage.empty with
                      Reads =
                          [ { Table = "sales.store"
                              Metrics = [ "orders"; "revenue" ]
                              Levels = [ "store" ] } ]
                      Kinds = [ "Box"; "Chart"; "Metric" ]
                      RequestKinds = [ "presentation" ] }
                  "hand-stated"
          }

          test "the pipeline page names its verbs, capabilities, endpoints and every request kind" {
              Expect.equal
                  (demandedOf (decodeOk pipelinePage))
                  { Reads =
                      [ { Table = "sales.category"
                          Metrics = [ "revenue"; "shoppers" ]
                          Levels = [] }
                        { Table = "sales.store"
                          Metrics = [ "orders" ]
                          Levels = [ "store" ] } ]
                    Verbs = [ "derive"; "groupBy" ]
                    Kinds = [ "Box"; "Button"; "DataGrid"; "Metric"; "Mount"; "Sparkline" ]
                    Capabilities = [ "export.csv"; "notify" ]
                    Endpoints = [ "/api/refresh" ]
                    RequestKinds = [ "host.lookup"; "pipeline"; "presentation" ] }
                  "hand-stated"
          }

          test "the demanded document round-trips through its canonical bytes" {
              let d = demandedOf (decodeOk pipelinePage)

              Expect.equal (DemandedPage.decode (DemandedPage.encode d)) (Ok d) "decode . encode"
          }

          test "check names each item beyond a host's coverage" {
              let d = demandedOf (decodeOk pipelinePage)

              let coverage =
                  { d with
                      Reads =
                          [ { Table = "sales.store"
                              Metrics = [ "orders"; "revenue" ]
                              Levels = [ "store" ] } ]
                      Capabilities = [ "notify" ]
                      RequestKinds = [ "pipeline"; "presentation" ] }

              Expect.equal
                  (DemandedPage.check coverage d)
                  (Error
                      { DemandedPage.empty with
                          Reads =
                              [ { Table = "sales.category"
                                  Metrics = [ "revenue"; "shoppers" ]
                                  Levels = [] } ]
                          Capabilities = [ "export.csv" ]
                          RequestKinds = [ "host.lookup" ] })
                  "the excess, in the read-set's vocabulary"

              Expect.equal (DemandedPage.check d d) (Ok()) "a page within its own reach"
          } ]

[<Tests>]
let signingTests =
    testList
        "Phase 2522 - the signed read-set"
        [ test "a signed page verifies by recomputation" {
              let doc = decodeOk presentationPage
              let signed = signOk doc

              match verify (Some "page-key") doc signed with
              | Ok v ->
                  Expect.equal v.Demanded (demandedOf doc) "the recomputed read-set"
                  Expect.equal v.TreeHash (PageDocument.treeHash doc) "the recomputed tree hash"
              | Error e -> failtestf "expected a verified page, got %A" e

              Expect.equal (SignedPage.decode (SignedPage.encode signed)) (Ok signed) "the record round-trips"
          }

          test "a page whose requests exceed its signed read-set is drift naming the excess" {
              let signed = signOk (decodeOk presentationPage)

              match verify (Some "page-key") (decodeOk Vectors.driftedPage) signed with
              | Error(PageVerifyRefusal.Drift d) ->
                  Expect.equal
                      d.Excess
                      { DemandedPage.empty with
                          Reads =
                              [ { Table = "sales.category"
                                  Metrics = [ "shoppers" ]
                                  Levels = [] } ] }
                      "the excess is the new table"

                  Expect.isTrue (DemandedPage.isEmpty d.Shortfall) "nothing was removed"
              | other -> failtestf "expected drift, got %A" other
          }

          test "a tree moved without moving its reach is a bad signature, with both tree hashes" {
              let signed = signOk (decodeOk presentationPage)
              let moved = decodeOk (presentationPage.Replace("Revenue by store", "Store revenue"))

              match verify (Some "page-key") moved signed with
              | Error(PageVerifyRefusal.BadSignature(carried, recomputed, _, _)) ->
                  Expect.notEqual carried recomputed "the tree hash moved"
              | other -> failtestf "expected a bad signature, got %A" other
          }

          test "a forged signature is a bad signature with agreeing hashes" {
              let doc = decodeOk presentationPage
              let signed = { signOk doc with Signature = "00" }

              match verify (Some "page-key") doc signed with
              | Error(PageVerifyRefusal.BadSignature(carried, recomputed, _, _)) ->
                  Expect.equal carried recomputed "the hashes agree"
              | other -> failtestf "expected a bad signature, got %A" other
          }

          test "a foreign key and no key are refused before anything is recomputed" {
              let doc = decodeOk presentationPage
              let signed = signOk doc

              match verify (Some "another-key") doc signed with
              | Error(PageVerifyRefusal.ForeignKey("page-key", "another-key")) -> ()
              | other -> failtestf "expected a foreign key, got %A" other

              match verify None doc signed with
              | Error PageVerifyRefusal.NoKey -> ()
              | other -> failtestf "expected the no-key refusal, got %A" other
          }

          test "a page with inline rows is not signed" {
              match SignedPage.sign (sinkFor "page-key") (decodeOk (snd Vectors.rules.[0])) with
              | Error(PageSignRefusal.PageRulesBroken ds) -> Expect.equal (codesOf ds) [ "FUARAN167" ] "inline rows"
              | other -> failtestf "expected the page rules to refuse, got %A" other
          } ]

[<Tests>]
let ruleTests =
    testList
        "Phase 2522 - the page-document validator mode"
        [ test "a chart with inline rows, a metric on state and a grid with static rows are refused by name" {
              let defectsOf (json: string) =
                  let doc = decodeOk json
                  PreEmitValidate.pageRuleDefects (doc.Requests |> Map.toList |> List.map fst |> Set.ofList) doc.Tree

              Expect.equal
                  (defectsOf (snd Vectors.rules.[0]))
                  [ PreEmitValidate.PreEmitDefect.PageInlineRows("inline-chart", "Chart", "source") ]
                  "chart"

              Expect.equal
                  (defectsOf (snd Vectors.rules.[1]))
                  [ PreEmitValidate.PreEmitDefect.PageSourceNotRequest("m", "Metric", "value", "a State binding") ]
                  "metric"

              Expect.equal
                  (defectsOf (snd Vectors.rules.[2]))
                  [ PreEmitValidate.PreEmitDefect.PageInlineRows("g", "DataGrid", "staticRows") ]
                  "grid"
          }

          test "a bound page passes both rules, and the ordinary walk is not judged by them" {
              let doc = decodeOk pipelinePage
              let keys = doc.Requests |> Map.toList |> List.map fst |> Set.ofList
              Expect.isEmpty (PreEmitValidate.pageRuleDefects keys doc.Tree) "bound"

              let inline' = decodeOk (snd Vectors.rules.[0])

              Expect.isFalse
                  (PreEmitValidate.validate inline'.Tree
                   |> Result.mapError codesOf
                   |> function
                       | Error codes -> List.contains "FUARAN167" codes
                       | Ok() -> false)
                  "an ordinary tree with inline rows is not a page"

              match PreEmitValidate.validatePage PreEmitValidate.ValidateOptions.defaults Set.empty inline'.Tree with
              | Error ds -> Expect.contains (codesOf ds) "FUARAN167" "the page mode judges it"
              | Ok() -> failtest "the page mode passed inline rows"
          }

          test
              "a chart reading its series from its rows is admitted over a request and refused over inline rows (Phase 2568)" {
              let pageCodes (doc: PageDocument) =
                  let keys = doc.Requests |> Map.toList |> List.map fst |> Set.ofList

                  match PreEmitValidate.validatePage PreEmitValidate.ValidateOptions.defaults keys doc.Tree with
                  | Error ds -> codesOf ds
                  | Ok() -> []

              let derived = decodeOk derivedSeriesPage

              for code in [ "FUARAN166"; "FUARAN167"; "FUARAN168" ] do
                  Expect.isFalse (List.contains code (pageCodes derived)) (code + ": the derived chart is bound")

              let inlineDerived = decodeOk (snd Vectors.rules.[3])
              let codes = pageCodes inlineDerived
              Expect.contains codes "FUARAN167" "the page rule refuses the inline rows"
              Expect.contains codes "FUARAN168" "and the chart rule names the unstated series"

              // The read-set is the request's: the demanded document does not
              // move when the series are read from the rows.
              Expect.equal
                  (demandedOf derived)
                  (demandedOf (decodeOk presentationPage))
                  "a derived chart demands what the authored one does"
          } ]

[<Tests>]
let schemaTests =
    testList
        "Phase 2522 - each source against its own request's schema"
        [ test "checkSources holds a chart to its source's schema and a metric to its own" {
              let doc = decodeOk presentationPage

              let schemas =
                  Map.ofList
                      [ "revenue-today", [ Field.create "revenue-today" FloatType ]
                        "revenue-by-store-30d", [ Field.create "day" StringType; Field.create "revenue" FloatType ] ]

              let defects = QueryBinding.checkSources schemas doc.Tree

              Expect.equal
                  (defects |> List.map (fun d -> d.Code, d.Location.NodeId, d.Location.Column))
                  [ QueryBinding.ColumnAbsentCode, "revenue-trend", "orders" ]
                  "only the chart's absent series is a fault"
          } ]

[<Tests>]
let corpusTests =
    testList
        "Phase 2522 - the page-document corpus family"
        [ test "the committed family is what the reference computes" {
              match Fuaran.Tests.CorpusRoot.tryFind () with
              | None -> () // a single-repo checkout: no corpus to compare against
              | Some root ->
                  let dir = Path.Combine(root, Vectors.FamilyDir)
                  let path = Path.Combine(dir, Vectors.FileName)
                  let manifestPath = Path.Combine(dir, "manifest.json")
                  let emitted = Vectors.emit ()

                  if Vectors.regenerating then
                      Directory.CreateDirectory dir |> ignore
                      File.WriteAllText(path, emitted)
                      File.WriteAllText(manifestPath, Vectors.manifest)
                  else
                      Expect.isTrue (File.Exists path) (sprintf "the corpus at %s holds no %s" root path)
                      Expect.equal (File.ReadAllText path) emitted "regenerate with FUARAN_REGEN=1"
                      Expect.equal (File.ReadAllText manifestPath) Vectors.manifest "the family manifest"
          }

          test "Phase 2570 - the signed pages verify under the .NET provider as the vectors expect" {
              match Fuaran.Tests.CorpusRoot.tryFind () with
              | None -> () // a single-repo checkout: no corpus to check
              | Some root ->
                  let path = Path.Combine(root, "attestation", SignedPageVectors.FileName)

                  let found =
                      if File.Exists path then
                          SignedPageVectors.problems (File.ReadAllText path)
                      else
                          [ sprintf "the corpus at %s holds no %s" root path ]

                  if Vectors.regenerating && not (List.isEmpty found) then
                      File.WriteAllText(path, SignedPageVectors.emitFresh ())
                      Expect.isEmpty (SignedPageVectors.problems (File.ReadAllText path)) "the re-signed vectors"
                  else
                      Expect.isEmpty found "re-sign with FUARAN_REGEN=1"
          } ]
