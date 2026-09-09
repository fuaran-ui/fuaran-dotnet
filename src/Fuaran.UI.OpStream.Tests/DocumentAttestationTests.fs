module Fuaran.UI.OpStream.Tests.DocumentAttestationTests

// System.Text.Json's GetString() is nullable; the corpus tests parse a
// controlled, committed fixture where every field is present.
#nowarn "3261"

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  Phase 1549 — document attestation. Three layers, go-red first:
//   1. The canonical claim pre-image reproduces the shared cross-host golden
//      (`wire-format-fixtures/attestation/document-corpus.json`), with no
//      crypto involved at all.
//   2. Every vector in that corpus produces its named verdict — accept, and
//      the THREE refusals, because a corpus of accept vectors alone cannot
//      tell a working verifier from one that always says yes.
//   3. Verification is offline: nothing beyond the envelope and the verifier's
//      own key directory, proven by verifying after the signing key is gone.
// ============================================================================

/// The shared corpus root, via the ONE resolver (Phase 1647).
let private corpusRoot () : string = Fuaran.Tests.CorpusRoot.find ()

let private corpus () : JsonDocument =
    JsonDocument.Parse(File.ReadAllText(Path.Combine(corpusRoot (), "attestation", "document-corpus.json")))

let private claimsOf (e: JsonElement) : Map<string, string> =
    e.GetProperty("claims").EnumerateObject()
    |> Seq.map (fun p -> p.Name, p.Value.GetString())
    |> Map.ofSeq

let private keysOf (e: JsonElement) : KeyDirectoryEntry list =
    [ for k in e.GetProperty("keys").EnumerateArray() ->
          { KeyId = k.GetProperty("keyId").GetString()
            Algorithm = k.GetProperty("algorithm").GetString()
            PublicKeySpki = k.GetProperty("publicKeySpki").GetString()
            NotBefore = None
            Expires = None
            RevokedFrom = None } ]

let private run x = Async.RunSynchronously x

let private decodeWith (keys: KeyDirectoryEntry list) (json: string) =
    DocumentEnvelope.decodeAttestedDocument (KeyDirectory.ofList keys) EcdsaP256.claimVerifier json
    |> run

/// The name this test file uses for a verdict, so a corpus expectation and an
/// actual outcome are compared as the same kind of thing.
let private verdictName (outcome: Result<AttestedDocument, DocumentRefusal>) : string =
    match outcome with
    | Ok _ -> "verified"
    | Error DocumentRefusal.NotAttested -> "not-attested"
    | Error(DocumentRefusal.InvalidEnvelope _) -> "invalid-envelope"
    | Error(DocumentRefusal.UnsupportedFormat _) -> "unsupported-format"
    | Error(DocumentRefusal.UnknownKey _) -> "unknown-key"
    | Error(DocumentRefusal.SignatureInvalid _) -> "signature-invalid"
    | Error(DocumentRefusal.DocumentDecode _) -> "document-decode"
    | Error(DocumentRefusal.DigestMismatch _) -> "digest-mismatch"

/// A `Fuaran.Core` attestation sink over a host-held P-256 key — the Phase-320
/// seam `DocumentEnvelope.attest` signs through, so the round-trip test exercises
/// the same adaptation a host would use rather than a bespoke signing path.
let private coreSink (keyId: string) (key: ECDsa) : Fuaran.Core.IAttestationSink =
    { new Fuaran.Core.IAttestationSink with
        member _.Sign payload =
            Some
                { Head = payload
                  KeyId = keyId
                  Signature =
                    Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes payload, HashAlgorithmName.SHA256)) }

        member _.Verify attestation payload =
            key.VerifyData(
                Encoding.UTF8.GetBytes payload,
                Convert.FromBase64String attestation.Signature,
                HashAlgorithmName.SHA256
            ) }

let private now () =
    DateTimeOffset.FromUnixTimeSeconds 1_755_648_000L

let private sampleTree: Node<obj> =
    { Id = "markdown-1"
      Kind = NodeKind.Markdown { Text = TextSource.Literal "Updated hourly." }
      Accessibility = None
      ExtraAttributes = None
      Motion = None
      State = None
      Style = None
      Tooltip = None
      Visible = None }

[<Tests>]
let tests =
    testList
        "Fuaran.UI.OpStream — Document attestation (Phase 1549)"
        [ test "claim corpus: the pre-image bytes + sha reproduce the golden" {
              use doc = corpus ()
              let mutable checked_ = 0

              for e in doc.RootElement.GetProperty("claims").EnumerateArray() do
                  let descriptor: DocumentDescriptor =
                      { Algorithm = "ecdsa-p256-sha256-v1"
                        Digest = e.GetProperty("digest").GetString() }

                  let canonical =
                      DocumentAttestation.claimPayload
                          descriptor
                          (e.GetProperty("keyId").GetString())
                          (DateTimeOffset.FromUnixTimeSeconds(e.GetProperty("signedAtUnixSeconds").GetInt64()))
                          (claimsOf e)

                  Expect.equal canonical (e.GetProperty("canonical").GetString()) "claim pre-image bytes"

                  Expect.equal
                      (Hashing.sha256Hex canonical)
                      (e.GetProperty("sha256").GetString())
                      "claim pre-image sha256"

                  checked_ <- checked_ + 1

              // A corpus that enumerated nothing would pass every assertion
              // above vacuously — the shape of green that hides a defect.
              Expect.isGreaterThan checked_ 1 "the claim corpus enumerated nothing"
          }

          test "document corpus: every vector produces the verdict it names" {
              use doc = corpus ()
              let mutable checked_ = 0

              for e in doc.RootElement.GetProperty("documents").EnumerateArray() do
                  let id = e.GetProperty("id").GetString()
                  let expected = e.GetProperty("expect").GetString()
                  let outcome = decodeWith (keysOf e) (e.GetProperty("envelope").GetString())

                  Expect.equal (verdictName outcome) expected ("vector " + id)
                  checked_ <- checked_ + 1

              Expect.isGreaterThan checked_ 3 "the document corpus enumerated fewer vectors than it declares"
          }

          test "the accept vector carries its claims through verification" {
              use doc = corpus ()

              let accept =
                  doc.RootElement.GetProperty("documents").EnumerateArray()
                  |> Seq.find (fun e -> e.GetProperty("id").GetString() = "accept-markdown")

              match decodeWith (keysOf accept) (accept.GetProperty("envelope").GetString()) with
              | Ok document ->
                  Expect.equal document.Attestation.KeyId "doc-key-2026" "the verified key id"
                  Expect.equal (Map.tryFind "model" document.Attestation.Claims) (Some "example-model-1") "model claim"

                  Expect.isTrue
                      (Map.containsKey "promptDigest" document.Attestation.Claims)
                      "the prompt digest claim survives verification"

                  Expect.isEmpty document.Warnings "a key with no lifecycle fields warns about nothing"
                  Expect.equal document.Tree.Id "markdown-1" "the decoded document"
              | Error refusal -> failtestf "the accept vector was refused: %A" refusal
          }

          test "round trip: a signed document verifies offline after the signing key is gone" {
              let keyId = "round-trip-key"

              let envelope, entry =
                  use key = ECDsa.Create(ECCurve.NamedCurves.nistP256)

                  let attestation =
                      DocumentEnvelope.attest
                          now
                          AttestationAlgorithm.ecdsaP256Sha256V1
                          keyId
                          (Map.ofList [ "model", "example-model-1" ])
                          (coreSink keyId key)
                          sampleTree

                  match attestation with
                  | None -> failtest "the sink refused to sign"
                  | Some a -> DocumentEnvelope.encode a sampleTree, EcdsaP256.keyEntry keyId key
              // The key handle is disposed here: everything below runs on the
              // envelope and the verifier's own directory, and nothing else.

              match decodeWith [ entry ] envelope with
              | Ok document ->
                  Expect.equal (CanonicalJson.encodeNode document.Tree) (CanonicalJson.encodeNode sampleTree) "the tree"
              | Error refusal -> failtestf "a freshly signed document was refused: %A" refusal
          }

          test "an edited document is a digest mismatch, not a bad signature" {
              let keyId = "tamper-key"
              use key = ECDsa.Create(ECCurve.NamedCurves.nistP256)

              let attestation =
                  DocumentEnvelope.attest
                      now
                      AttestationAlgorithm.ecdsaP256Sha256V1
                      keyId
                      Map.empty
                      (coreSink keyId key)
                      sampleTree
                  |> Option.get

              let edited =
                  { sampleTree with
                      Kind = NodeKind.Markdown { Text = TextSource.Literal "Updated daily." } }

              // The attestation is untouched and still authentic; only the
              // document beneath it moved.
              let envelope = DocumentEnvelope.encode attestation edited

              match decodeWith [ EcdsaP256.keyEntry keyId key ] envelope with
              | Error(DocumentRefusal.DigestMismatch(claimed, actual)) ->
                  Expect.notEqual claimed actual "the two digests must differ for the refusal to mean anything"
              | other -> failtestf "an edited document was not reported as a digest mismatch: %A" other
          }

          test "an ordinary document is NotAttested, and decodes unchanged through the plain path" {
              let plain = CanonicalJson.encodeNode sampleTree

              Expect.equal (verdictName (decodeWith [] plain)) "not-attested" "an unenveloped document"

              match Fuaran.UI.Ops.JsonDecode.decodeNodeObj plain with
              | Ok tree -> Expect.equal (CanonicalJson.encodeNode tree) plain "the ordinary decode is untouched"
              | Error e -> failtestf "the ordinary decode refused a canonical document: %A" e
          }

          test "an undeclared envelope member is refused rather than ignored" {
              use doc = corpus ()

              let accept =
                  doc.RootElement.GetProperty("documents").EnumerateArray()
                  |> Seq.find (fun e -> e.GetProperty("id").GetString() = "accept-markdown")

              let envelope = accept.GetProperty("envelope").GetString()

              // One member added at the top level, nothing else changed.
              let widened = envelope.Replace("\"attestation\":{", "\"extra\":1,\"attestation\":{")

              Expect.equal (verdictName (decodeWith (keysOf accept) widened)) "invalid-envelope" "a stray member"
          }

          test "capability admission is default-deny on every outcome but an admitted one" {
              use doc = corpus ()

              let vectors =
                  doc.RootElement.GetProperty("documents").EnumerateArray()
                  |> Seq.map (fun e ->
                      e.GetProperty("id").GetString(), decodeWith (keysOf e) (e.GetProperty("envelope").GetString()))
                  |> List.ofSeq

              let declared = [ "notify"; "call:reports.*" ]

              for id, outcome in vectors do
                  let granted = DocumentEnvelope.grantedCapabilities (fun _ -> true) outcome declared

                  match outcome with
                  | Ok _ -> Expect.equal granted declared ("an admitted document grants its request: " + id)
                  | Error _ -> Expect.isEmpty granted ("a refusal grants nothing: " + id)

              // A host that declines the key grants nothing even on a verified
              // document — a valid signature by a stranger is still a stranger.
              let verified = vectors |> List.map snd |> List.find Result.isOk

              Expect.isEmpty
                  (DocumentEnvelope.grantedCapabilities (fun _ -> false) verified declared)
                  "a verified document the host declines to admit"
          } ]
