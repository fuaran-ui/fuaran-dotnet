module Fuaran.UI.Design.Tests.SeamTests

// Phase 2295 — the design-provider seam's laws, in memory: every record round-trips through
// SeamCodec byte-identically, the bytes are canonical (keys sorted, optional members omitted,
// lists always present) and stable, each refusal class refuses with its code at its path, and a
// hand-written result for one screen — the shape a first provider's export takes — decodes and
// names only components the Phase 2294 bundle has cards for.

open System
open System.Security.Cryptography
open System.Text
open Expecto
open Fuaran.UI
open Fuaran.UI.Design
open Fuaran.UI.Design.SeamCodec

let private hashOf (s: string) =
    "sha256:"
    + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes s)).ToLowerInvariant()

let private reference: DesignSystemRef =
    { ProviderId = "directory"
      ProjectId = None
      BundleHash = hashOf "bundle"
      IdlVersion = 1 }

let private brief: DesignBrief =
    { Module = "Orders"
      Purpose = Some "Find the orders that need attention."
      DataShapes =
        [ { Name = "orders"
            Type = "Order"
            Cardinality = Cardinality.Many } ]
      Tasks = [ "find a late order" ]
      Interactions = [ Interaction.Filter; Interaction.Drill ]
      Viewports = [ { Name = "desktop"; Width = 1280 } ]
      Density = Some Density.Standard
      Fragments = []
      DesignSystem = reference }

let private request: DesignRequest =
    { BriefHash = hashOf (encodeBrief brief)
      DesignSystem = reference
      State = RequestState.Pending
      Handle = Some "r-1" }

let private result: DesignResult =
    { Screens =
        [ { Name = "orders"
            Files =
              [ { Path = "orders/index.html"
                  MediaType = "text/html"
                  Hash = hashOf "html" } ] } ]
      Annotations =
        Some
            [ { Screen = "orders"
                Components = [ "DataGrid" ] } ]
      DesignSystem = reference
      ContentHash = hashOf "result" }

let private expectRefused (code: SeamErrorCode) (path: string) (r: Result<'a, SeamError>) =
    match r with
    | Error e ->
        Expect.equal e.Code code $"refused with %A{code} (got: %s{e.Message})"
        Expect.equal e.Path path "at the offending value"
    | Ok _ -> failtestf "expected %A at %s, but the document decoded" code path

/// The hand-written result a first provider's export takes for one screen: a document authored
/// outside any encoder, keys out of order and whitespace present, carrying an annotation member no
/// record declares.
let private pilotResult =
    """
{
  "screens": [ { "name": "orders-overview", "files": [
      { "path": "orders-overview/index.html", "mediaType": "text/html",
        "hash": "sha256:0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0" },
      { "path": "orders-overview/styles.css", "mediaType": "text/css",
        "hash": "sha256:1111111111111111111111111111111111111111111111111111111111111111" } ] } ],
  "$type": "DesignResult",
  "annotations": [ { "screen": "orders-overview",
                     "components": [ "Heading", "Filters", "DataGrid", "Metric", "Button" ],
                     "note": "composed from the bundle's cards" } ],
  "designSystem": { "providerId": "hand", "idlVersion": 1,
                    "bundleHash": "sha256:2222222222222222222222222222222222222222222222222222222222222222" },
  "contentHash": "sha256:3333333333333333333333333333333333333333333333333333333333333333"
}
"""

[<Tests>]
let tests =
    testList
        "design seam"
        [ testList
              "round-trip"
              [ test "a reference round-trips" {
                    Expect.equal (decodeRef (encodeRef reference)) (Ok reference) "decode (encode r) = r"
                }
                test "a brief round-trips" {
                    Expect.equal (decodeBrief (encodeBrief brief)) (Ok brief) "decode (encode b) = b"
                }
                test "a request round-trips" {
                    Expect.equal (decodeRequest (encodeRequest request)) (Ok request) "decode (encode q) = q"
                }
                test "a result round-trips" {
                    Expect.equal (decodeResult (encodeResult result)) (Ok result) "decode (encode r) = r"
                }
                test "an exchange round-trips when request and result share the bundle" {
                    Expect.equal
                        (decodeExchange (encodeExchange request result))
                        (Ok(request, result))
                        "decode (encode (q, r)) = (q, r)"
                }
                test "every decoder re-encodes its own output byte-identically" {
                    for decoder, text in
                        [ "design-system-ref", encodeRef reference
                          "design-brief", encodeBrief brief
                          "design-request", encodeRequest request
                          "design-result", encodeResult result
                          "design-exchange", encodeExchange request result ] do
                        Expect.equal (reencode decoder text) (Some(Ok text)) $"%s{decoder} is a fixed point"

                    Expect.equal (reencode "design-nothing" "{}") None "an unknown decoder name is not guessed at"
                } ]

          testList
              "canonical bytes"
              [ test "a reference's bytes are pinned" {
                    Expect.equal
                        (encodeRef
                            { reference with
                                BundleHash = "sha256:" + String('a', 64) })
                        ("{\"$type\":\"DesignSystemRef\",\"bundleHash\":\"sha256:"
                         + String('a', 64)
                         + "\",\"idlVersion\":1,\"providerId\":\"directory\"}")
                        "$type first, keys ordinal, the absent projectId omitted, no whitespace"
                }
                test "an empty list is written, an absent option is not" {
                    let bytes =
                        encodeBrief
                            { brief with
                                Purpose = None
                                Density = None
                                Tasks = [] }

                    Expect.stringContains bytes "\"tasks\":[]" "a list member is always present"
                    Expect.isFalse (bytes.Contains "purpose") "an absent option is omitted"
                    Expect.isFalse (bytes.Contains "null") "nothing is written as null"
                }
                test "the bytes are stable across encodes" {
                    Expect.equal (encodeResult result) (encodeResult result) "encoding is a function of the record"
                }
                test "a non-canonical document re-encodes to the canonical bytes" {
                    let spaced = (encodeRef reference).Replace(",", " , ")

                    Expect.equal
                        (reencode "design-system-ref" spaced)
                        (Some(Ok(encodeRef reference)))
                        "whitespace is not part of the record"
                } ]

          testList
              "refusals"
              [ test "a reference missing its hash" {
                    encodeRef reference
                    |> fun s -> s.Replace($",\"bundleHash\":\"%s{reference.BundleHash}\"", "")
                    |> fun s -> s.Replace($"\"bundleHash\":\"%s{reference.BundleHash}\",", "")
                    |> decodeRef
                    |> expectRefused SeamErrorCode.MissingMember "$.bundleHash"
                }
                test "an uppercase hash" {
                    (encodeRef reference).Replace(reference.BundleHash, reference.BundleHash.ToUpperInvariant())
                    |> decodeRef
                    |> expectRefused SeamErrorCode.BadHash "$.bundleHash"
                }
                test "an interaction outside the vocabulary" {
                    (encodeBrief brief).Replace("\"Drill\"", "\"Hover\"")
                    |> decodeBrief
                    |> expectRefused SeamErrorCode.UnknownInteraction "$.interactions[1]"
                }
                test "an unknown member of a record" {
                    (encodeRequest request).Replace("{\"$type\"", "{\"colour\":\"red\",\"$type\"")
                    |> decodeRequest
                    |> expectRefused SeamErrorCode.UnknownMember "$.colour"
                }
                test "an unknown request state" {
                    (encodeRequest request).Replace("\"Pending\"", "\"Done\"")
                    |> decodeRequest
                    |> expectRefused SeamErrorCode.UnknownToken "$.state"
                }
                test "a document of another record" {
                    encodeBrief brief
                    |> decodeRequest
                    |> expectRefused SeamErrorCode.WrongType "$.$type"
                }
                test "a document with no $type" {
                    (encodeRef reference).Replace("\"$type\":\"DesignSystemRef\",", "")
                    |> decodeRef
                    |> expectRefused SeamErrorCode.MissingMember "$.$type"
                }
                test "text that is not JSON" { decodeResult "{" |> expectRefused SeamErrorCode.Malformed "$" }
                test "a viewport of no width" {
                    (encodeBrief brief).Replace("\"width\":1280", "\"width\":0")
                    |> decodeBrief
                    |> expectRefused SeamErrorCode.OutOfRange "$.viewports[0].width"
                }
                test "a result made against another bundle than its request" {
                    let other =
                        { result with
                            DesignSystem =
                                { reference with
                                    BundleHash = hashOf "another bundle" } }

                    encodeExchange request other
                    |> decodeExchange
                    |> expectRefused SeamErrorCode.DesignSystemMismatch "$.result.designSystem.bundleHash"
                }
                test "an annotation ignores an unknown member and still refuses a missing one" {
                    let withNote =
                        (encodeResult result).Replace("{\"components\"", "{\"note\":\"x\",\"components\"")

                    Expect.equal (decodeResult withNote) (Ok result) "an annotation is evidence: the member is ignored"

                    (encodeResult result).Replace("\"components\":[\"DataGrid\"],", "")
                    |> decodeResult
                    |> expectRefused SeamErrorCode.MissingMember "$.annotations[0].components"
                }
                test "every code has its corpus name" {
                    let names =
                        [ SeamErrorCode.Malformed
                          SeamErrorCode.WrongType
                          SeamErrorCode.MissingMember
                          SeamErrorCode.UnknownMember
                          SeamErrorCode.UnknownInteraction
                          SeamErrorCode.UnknownToken
                          SeamErrorCode.BadHash
                          SeamErrorCode.OutOfRange
                          SeamErrorCode.DesignSystemMismatch ]
                        |> List.map SeamErrorCode.name

                    Expect.equal (List.distinct names) names "the names are distinct"
                    Expect.allEqual (names |> List.map (fun n -> n.StartsWith "DESIGN_")) true "and prefixed"
                } ]

          testList
              "a hand-written result"
              [ test "decodes, and names only components the bundle has cards for" {
                    let decoded =
                        match decodeResult pilotResult with
                        | Ok r -> r
                        | Error e -> failtestf "the hand-written result is refused: %s at %s" e.Message e.Path

                    Expect.equal decoded.Screens.Length 1 "one screen"

                    let bundle = Bundle.build Defaults.theme None (Bundle.BundleOptions.defaults ())

                    let known =
                        bundle.Cards
                        |> List.filter (fun c ->
                            c.Group = Bundle.CardGroup.Components || c.Group = Bundle.CardGroup.Fragments)
                        |> List.map _.Name
                        |> Set.ofList

                    Expect.isEmpty (DesignResult.unknownComponents known decoded) "every annotated component is a card"

                    Expect.equal decoded.DesignSystem.IdlVersion bundle.IdlVersion "made against this IDL version"
                }
                test "a component the bundle does not have is named" {
                    let decoded =
                        decodeResult (pilotResult.Replace("\"Metric\"", "\"Carousel\""))
                        |> Result.defaultWith (fun e -> failtestf "refused: %s" e.Message)

                    let known = set [ "Heading"; "Filters"; "DataGrid"; "Button" ]

                    Expect.equal
                        (DesignResult.unknownComponents known decoded)
                        [ "Carousel" ]
                        "the stranger is reported"
                } ] ]
