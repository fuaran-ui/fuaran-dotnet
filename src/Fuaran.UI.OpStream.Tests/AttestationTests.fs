module Fuaran.UI.OpStream.Tests.AttestationTests

// System.Text.Json's GetString() is nullable; the corpus tests parse a
// controlled, committed fixture where every field is present.
#nowarn "3261"

open System
open System.Globalization
open System.IO
open System.Security.Cryptography
open System.Text.Json
open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Tests.TestSupport

// ============================================================================
//  Phase 789 — segment attestation. Three layers, go-red first:
//   1. The canonical descriptor / claim encodings reproduce the shared
//      cross-host golden (`wire-format-fixtures/attestation/`).
//   2. Every forgery class produces its typed verdict — including THE case
//      the mechanism exists for: an editor with store write access re-chains
//      consistently, and the stored attestation exposes it (`HeadMismatch`).
//   3. Verification is offline: nothing beyond the bundle and the verifier's
//      own key directory — proven by verifying after the signing key is gone.
// ============================================================================

/// Walk up from the test assembly until the workspace `wire-format-fixtures/`
/// corpus is found (a sibling of the `fuaran-dotnet/` repo).
let private corpusRoot () : string =
    let rec walk (dir: DirectoryInfo) =
        if isNull dir then
            failwith "wire-format-fixtures/ not found walking up — the Fuaran workspace checkout is required."
        else
            let candidate = Path.Combine(dir.FullName, "wire-format-fixtures", "manifest.json")

            if File.Exists candidate then
                Path.Combine(dir.FullName, "wire-format-fixtures")
            else
                walk dir.Parent

    walk (DirectoryInfo(AppContext.BaseDirectory))

let private descriptorOf (e: JsonElement) : SegmentDescriptor =
    { Algorithm = e.GetProperty("algorithm").GetString()
      ChainFormatVersion = e.GetProperty("chainFormatVersion").GetInt32()
      StreamId = e.GetProperty("streamId").GetString()
      FromSeq = e.GetProperty("fromSeq").GetInt32()
      ToSeq = e.GetProperty("toSeq").GetInt32()
      PreviousHash = e.GetProperty("previousHash").GetString()
      Head = e.GetProperty("head").GetString() }

// --- a clean three-record chain + an ECDSA key, shared by the verdict tests --

let private makeChain () : OpRecord<TestMsg> list =
    let r1 =
        buildRecord "stream-1" 1 (TreeOp.RemoveNode(NodeId "a"): TreeOp<TestMsg>) None (timestamp 100L)

    let r2 =
        buildRecord "stream-1" 2 (TreeOp.RemoveNode(NodeId "b")) (Some r1) (timestamp 200L)

    let r3 =
        buildRecord "stream-1" 3 (TreeOp.RemoveNode(NodeId "c")) (Some r2) (timestamp 300L)

    [ r1; r2; r3 ]

let private now () = timestamp 1_755_648_000L

let private run x = Async.RunSynchronously x

/// Sign a bundle over a fresh P-256 key; return the bundle plus the
/// directory entry a verifier would hold. The private key lives only inside
/// this helper's scope unless the caller keeps it.
let private attestedBundle (records: OpRecord<TestMsg> list) : EvidenceBundle<TestMsg> * KeyDirectoryEntry =
    use key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
    let signer = EcdsaP256.signer now "key-1" key

    let bundle =
        Evidence.produce signer AttestationAlgorithm.ecdsaP256Sha256V1 records
        |> run
        |> function
            | Ok b -> b
            | Error e -> failtestf "produce refused a clean chain: %s" e

    bundle, EcdsaP256.keyEntry "key-1" key

let private verifyWith (entry: KeyDirectoryEntry) (bundle: EvidenceBundle<TestMsg>) : EvidenceVerdict =
    Evidence.verify (KeyDirectory.ofList [ entry ]) EcdsaP256.verifier bundle |> run

[<Tests>]
let tests =
    testList
        "Fuaran.UI.OpStream — Segment attestation (Phase 789)"
        [ test "descriptor corpus: canonical bytes + sha reproduce the golden" {
              let root = corpusRoot ()

              let doc =
                  JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "attestation", "descriptor-corpus.json")))

              let mutable checked_ = 0

              for e in doc.RootElement.GetProperty("descriptors").EnumerateArray() do
                  let canonical = SegmentDescriptor.encode (descriptorOf e)
                  Expect.equal canonical (e.GetProperty("canonical").GetString()) "descriptor canonical bytes"

                  Expect.equal
                      (Fuaran.UI.Hashing.sha256Hex canonical)
                      (e.GetProperty("sha256").GetString())
                      "descriptor sha256"

                  checked_ <- checked_ + 1

              for e in doc.RootElement.GetProperty("claims").EnumerateArray() do
                  let canonical =
                      SegmentAttestation.claimPayload
                          (descriptorOf e)
                          (e.GetProperty("keyId").GetString())
                          (DateTimeOffset.FromUnixTimeSeconds(e.GetProperty("signedAtUnixSeconds").GetInt64()))
                          (e.GetProperty("adopted").GetBoolean())

                  Expect.equal canonical (e.GetProperty("canonical").GetString()) "claim canonical bytes"

                  Expect.equal
                      (Fuaran.UI.Hashing.sha256Hex canonical)
                      (e.GetProperty("sha256").GetString())
                      "claim sha256"

                  checked_ <- checked_ + 1

              Expect.isGreaterThan checked_ 3 "the corpus carries at least two descriptors and two claims"
          }

          test "sign → verify: a clean attested bundle is Attested with no warnings" {
              let bundle, entry = attestedBundle (makeChain ())

              match verifyWith entry bundle with
              | EvidenceVerdict.Attested("key-1", alg, 1, 3, []) ->
                  Expect.equal alg AttestationAlgorithm.ecdsaP256Sha256V1 "algorithm"
              | other -> failtestf "expected clean Attested, got %A" other
          }

          test "verification is offline: the signing key is disposed before verify runs" {
              // `attestedBundle` disposes the ECDsa on exit; only the bundle
              // and the PUBLIC directory entry survive. If verification
              // reached the signer, a service, or any private material, this
              // could not pass.
              let bundle, entry = attestedBundle (makeChain ())

              match verifyWith entry bundle with
              | EvidenceVerdict.Attested _ -> ()
              | other -> failtestf "expected Attested, got %A" other
          }

          test "a mid-stream segment verifies against its signed anchor" {
              let chain = makeChain ()
              let bundle, entry = attestedBundle (List.skip 1 chain)

              match verifyWith entry bundle with
              | EvidenceVerdict.Attested(_, _, 2, 3, []) -> ()
              | other -> failtestf "expected Attested over 2..3, got %A" other
          }

          // ------------------------------------------------------- forgeries --

          test "GO-RED: a re-chained edit is exposed — consistent chain, HeadMismatch" {
              // The property the phase exists for. The editor rewrites record
              // 2 and recomputes every subsequent digest, so the chain walk
              // PASSES — and the stored attestation still covers the old head.
              let bundle, entry = attestedBundle (makeChain ())

              let r1 =
                  buildRecord "stream-1" 1 (TreeOp.RemoveNode(NodeId "a"): TreeOp<TestMsg>) None (timestamp 100L)

              let forged2 =
                  buildRecord "stream-1" 2 (TreeOp.RemoveNode(NodeId "FORGED")) (Some r1) (timestamp 200L)

              let forged3 =
                  buildRecord "stream-1" 3 (TreeOp.RemoveNode(NodeId "c")) (Some forged2) (timestamp 300L)

              let rechained =
                  { bundle with
                      Records = [ r1; forged2; forged3 ] }

              Expect.isOk (Verify.chain rechained.Records) "the re-chained store is internally consistent"

              match verifyWith entry rechained with
              | EvidenceVerdict.HeadMismatch _ -> ()
              | other -> failtestf "expected HeadMismatch, got %A" other
          }

          test "GO-RED: an edit without a re-chain is ChainBroken" {
              let bundle, entry = attestedBundle (makeChain ())

              let tampered =
                  bundle.Records
                  |> List.mapi (fun i r -> if i = 1 then { r with Timestamp = timestamp 999L } else r)

              match verifyWith entry { bundle with Records = tampered } with
              | EvidenceVerdict.ChainBroken(VerificationError.HashMismatch _) -> ()
              | other -> failtestf "expected ChainBroken HashMismatch, got %A" other
          }

          test "GO-RED: a stripped attestation degrades to Unattested, never Attested" {
              let bundle, entry = attestedBundle (makeChain ())

              match verifyWith entry { bundle with Attestation = None } with
              | EvidenceVerdict.Unattested -> ()
              | other -> failtestf "expected Unattested, got %A" other
          }

          test "GO-RED: a self-minted key is UnknownKey — bundle.Keys is never a trust root" {
              // The adversary mints a key pair, signs a forged range, and
              // ships the public half inside the bundle. Internally the
              // artefact is perfectly consistent; the verifier's own
              // directory has never heard of the key.
              use forgersKey = ECDsa.Create(ECCurve.NamedCurves.nistP256)
              let signer = EcdsaP256.signer now "forger" forgersKey

              let bundle =
                  Evidence.produce signer AttestationAlgorithm.ecdsaP256Sha256V1 (makeChain ())
                  |> run
                  |> function
                      | Ok b ->
                          { b with
                              Keys = [ EcdsaP256.keyEntry "forger" forgersKey ] }
                      | Error e -> failtestf "produce failed: %s" e

              let _, honestEntry = attestedBundle (makeChain ())

              match verifyWith honestEntry bundle with
              | EvidenceVerdict.UnknownKey "forger" -> ()
              | other -> failtestf "expected UnknownKey, got %A" other
          }

          test "GO-RED: a tampered signature is SignatureInvalid" {
              let bundle, entry = attestedBundle (makeChain ())

              let tampered =
                  bundle.Attestation
                  |> Option.map (fun a ->
                      { a with
                          Signature = Convert.ToBase64String(Array.zeroCreate<byte> 64) })

              match verifyWith entry { bundle with Attestation = tampered } with
              | EvidenceVerdict.SignatureInvalid "key-1" -> ()
              | other -> failtestf "expected SignatureInvalid, got %A" other
          }

          test "GO-RED: a store-writer cannot backdate SignedAt — it is inside the signed claim" {
              let bundle, entry = attestedBundle (makeChain ())

              let backdated =
                  bundle.Attestation |> Option.map (fun a -> { a with SignedAt = timestamp 100L })

              match verifyWith entry { bundle with Attestation = backdated } with
              | EvidenceVerdict.SignatureInvalid _ -> ()
              | other -> failtestf "expected SignatureInvalid, got %A" other
          }

          test "GO-RED: a store-writer cannot promote an adoption to a witnessed claim" {
              use key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
              let signer = EcdsaP256.signerWith now true "key-1" key

              let bundle =
                  Evidence.produce signer AttestationAlgorithm.ecdsaP256Sha256V1 (makeChain ())
                  |> run
                  |> function
                      | Ok b -> b
                      | Error e -> failtestf "produce failed: %s" e

              let promoted =
                  { bundle with
                      Attestation = bundle.Attestation |> Option.map (fun a -> { a with Adopted = false }) }

              match verifyWith (EcdsaP256.keyEntry "key-1" key) promoted with
              | EvidenceVerdict.SignatureInvalid _ -> ()
              | other -> failtestf "expected SignatureInvalid, got %A" other
          }

          test "GO-RED: records outside the declared range are RangeMismatch" {
              let chain = makeChain ()
              let bundle, entry = attestedBundle (List.skip 1 chain)

              match
                  verifyWith
                      entry
                      { bundle with
                          Records = List.take 2 chain }
              with
              | EvidenceVerdict.RangeMismatch _ -> ()
              | other -> failtestf "expected RangeMismatch, got %A" other
          }

          test "GO-RED: a segment lifted off its signed anchor is ChainBroken" {
              let bundle, entry = attestedBundle (List.skip 1 (makeChain ()))

              let lifted =
                  bundle.Records
                  |> List.map (fun r ->
                      if r.Sequence = 2 then
                          { r with
                              PreviousHash = String.replicate 64 "f" }
                      else
                          r)

              match verifyWith entry { bundle with Records = lifted } with
              | EvidenceVerdict.ChainBroken _ -> ()
              | other -> failtestf "expected ChainBroken, got %A" other
          }

          // ------------------------------------------- key lifecycle verdicts --

          test "a signature made at or after revocation is void (SignatureInvalid)" {
              let bundle, entry = attestedBundle (makeChain ())

              let revokedBefore =
                  { entry with
                      RevokedFrom = Some(timestamp 1_755_000_000L) }

              match verifyWith revokedBefore bundle with
              | EvidenceVerdict.SignatureInvalid "key-1" -> ()
              | other -> failtestf "expected SignatureInvalid, got %A" other
          }

          test "a signature made before revocation is valid and flagged" {
              let bundle, entry = attestedBundle (makeChain ())

              let revokedAfter =
                  { entry with
                      RevokedFrom = Some(timestamp 1_755_700_000L) }

              match verifyWith revokedAfter bundle with
              | EvidenceVerdict.Attested(_, _, _, _, [ EvidenceWarning.KeyRevokedAfterSigning "key-1" ]) -> ()
              | other -> failtestf "expected Attested + KeyRevokedAfterSigning, got %A" other
          }

          test "a lapsed expiry is a warning, never a failure" {
              let bundle, entry = attestedBundle (makeChain ())

              let expired =
                  { entry with
                      Expires = Some(timestamp 1_755_000_000L) }

              match verifyWith expired bundle with
              | EvidenceVerdict.Attested(_, _, _, _, [ EvidenceWarning.KeyExpired "key-1" ]) -> ()
              | other -> failtestf "expected Attested + KeyExpired, got %A" other
          }

          test "an adoption verifies Attested with the Adopted warning — a distinct claim tier" {
              use key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
              let signer = EcdsaP256.signerWith now true "key-1" key

              let bundle =
                  Evidence.produce signer AttestationAlgorithm.ecdsaP256Sha256V1 (makeChain ())
                  |> run
                  |> function
                      | Ok b -> b
                      | Error e -> failtestf "produce failed: %s" e

              match verifyWith (EcdsaP256.keyEntry "key-1" key) bundle with
              | EvidenceVerdict.Attested(_, _, _, _, [ EvidenceWarning.Adopted ]) -> ()
              | other -> failtestf "expected Attested + Adopted, got %A" other
          }

          test "a key entry under a different algorithm id is SignatureInvalid" {
              let bundle, entry = attestedBundle (makeChain ())
              let foreign = { entry with Algorithm = "ed25519-v1" }

              match verifyWith foreign bundle with
              | EvidenceVerdict.SignatureInvalid "key-1" -> ()
              | other -> failtestf "expected SignatureInvalid, got %A" other
          }

          test "an unknown chain format is refused, not guessed at" {
              let bundle, entry = attestedBundle (makeChain ())

              let futureFormat =
                  bundle.Attestation
                  |> Option.map (fun a ->
                      { a with
                          Descriptor =
                              { a.Descriptor with
                                  ChainFormatVersion = 99 } })

              match
                  verifyWith
                      entry
                      { bundle with
                          Attestation = futureFormat }
              with
              | EvidenceVerdict.UnsupportedChainFormat 99 -> ()
              | other -> failtestf "expected UnsupportedChainFormat, got %A" other
          }

          // -------------------------------------------------- posture + seams --

          test "the unattested posture is named: produce yields an honest Unattested bundle" {
              let bundle =
                  Evidence.produce AttestationSigner.unattested AttestationAlgorithm.ecdsaP256Sha256V1 (makeChain ())
                  |> run
                  |> function
                      | Ok b -> b
                      | Error e -> failtestf "produce failed: %s" e

              Expect.isNone bundle.Attestation "no attestation was minted"
              let _, entry = attestedBundle (makeChain ())

              match verifyWith entry bundle with
              | EvidenceVerdict.Unattested -> ()
              | other -> failtestf "expected Unattested, got %A" other
          }

          test "produce refuses to attest a chain that does not verify" {
              let chain = makeChain ()

              let broken =
                  chain
                  |> List.mapi (fun i r ->
                      if i = 2 then
                          { r with
                              Hash = String.replicate 64 "0" }
                      else
                          r)

              use key = ECDsa.Create(ECCurve.NamedCurves.nistP256)

              match
                  Evidence.produce (EcdsaP256.signer now "key-1" key) AttestationAlgorithm.ecdsaP256Sha256V1 broken
                  |> run
              with
              | Error message -> Expect.stringContains message "refusing to attest" "names the refusal"
              | Ok _ -> failtest "a broken chain was signed"
          }

          test "the Core IAttestationSink adapter signs the claim payload verbatim" {
              use key = ECDsa.Create(ECCurve.NamedCurves.nistP256)

              let coreSink =
                  { new Fuaran.Core.IAttestationSink with
                      member _.Sign payload =
                          Some
                              { Head = payload
                                KeyId = "core-key"
                                Signature =
                                  Convert.ToBase64String(
                                      key.SignData(
                                          System.Text.Encoding.UTF8.GetBytes(payload: string),
                                          HashAlgorithmName.SHA256
                                      )
                                  ) }

                      member _.Verify _ _ = false }

              let signer = AttestationSigner.ofCoreSink now false "core-key" coreSink

              let bundle =
                  Evidence.produce signer AttestationAlgorithm.ecdsaP256Sha256V1 (makeChain ())
                  |> run
                  |> function
                      | Ok b -> b
                      | Error e -> failtestf "produce failed: %s" e

              match verifyWith (EcdsaP256.keyEntry "core-key" key) bundle with
              | EvidenceVerdict.Attested("core-key", _, _, _, _) -> ()
              | other -> failtestf "expected Attested under the Core-signed key, got %A" other
          }

          test "the Core adapter discards a sink answering under a different key id" {
              let coreSink =
                  { new Fuaran.Core.IAttestationSink with
                      member _.Sign payload =
                          Some
                              { Head = payload
                                KeyId = "other-key"
                                Signature = "sig" }

                      member _.Verify _ _ = false }

              let signer = AttestationSigner.ofCoreSink now false "expected-key" coreSink

              match SegmentDescriptor.forRecords AttestationAlgorithm.ecdsaP256Sha256V1 (makeChain ()) with
              | Error e -> failtestf "forRecords failed: %s" e
              | Ok descriptor ->
                  Expect.isNone (signer.SignSegment descriptor |> run) "a key-id mismatch is discarded, not recorded"
          }

          test "FileKeyDirectory round-trips entries and lifecycle fields" {
              use key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
              let spki = EcdsaP256.exportPublicKeySpki key

              let json =
                  "{ \"keys\": [ { \"keyId\": \"k-1\", \"algorithm\": \"ecdsa-p256-sha256-v1\", \"publicKeySpki\": \""
                  + spki
                  + "\", \"notBefore\": null, \"expires\": \"2027-01-01T00:00:00+00:00\", \"revokedFrom\": null } ] }"

              let entries = FileKeyDirectory.parse json
              Expect.hasLength entries 1 "one entry"
              Expect.equal entries.Head.KeyId "k-1" "keyId"
              Expect.equal entries.Head.PublicKeySpki spki "spki"
              Expect.isSome entries.Head.Expires "expiry parsed"
              Expect.isNone entries.Head.RevokedFrom "revocation absent"
          }

          // ------------------------------------------------- Phase 1525 --

          test "GO-RED: a key-directory timestamp is read the same on every machine" {
              // A key directory is a published document. Its three lifecycle
              // fields ARE the revocation boundary, the expiry and the validity
              // start, so if they resolve to different instants on different
              // machines the same attestation verifies in one place and is void
              // in another. Default parse styles do exactly that: a string with
              // no offset is interpreted in the READING machine's local zone.
              //
              // The chosen behaviour is AssumeUniversal ||| AdjustToUniversal:
              // an offset-less string reads as UTC, one carrying an offset
              // normalises to the same instant in UTC.
              use key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
              let spki = EcdsaP256.exportPublicKeySpki key

              let directory (expires: string) (revoked: string) : KeyDirectoryEntry =
                  let json =
                      "{ \"keys\": [ { \"keyId\": \"k-1\", \"algorithm\": \"ecdsa-p256-sha256-v1\", \"publicKeySpki\": \""
                      + spki
                      + "\", \"notBefore\": null, \"expires\": \""
                      + expires
                      + "\", \"revokedFrom\": \""
                      + revoked
                      + "\" } ] }"

                  (FileKeyDirectory.parse json).Head

              let utcInstant = DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero)

              // (1) No offset at all. Machine-INDEPENDENT only because of
              //     AssumeUniversal; on a host whose local offset is zero this
              //     arm cannot tell the two implementations apart, which is
              //     precisely why arm (2) below exists.
              let noOffset = directory "2027-01-01T00:00:00" "2027-01-01T00:00:00"
              Expect.equal noOffset.Expires (Some utcInstant) "an offset-less expiry reads as UTC"

              Expect.equal
                  (noOffset.Expires |> Option.map _.Offset)
                  (Some TimeSpan.Zero)
                  "and carries a UTC offset, not the reading machine's"

              // (2) An explicit non-UTC offset. AdjustToUniversal normalises it
              //     to the same instant in UTC — red under default styles on
              //     EVERY machine, whatever its local zone.
              let shifted = directory "2026-12-31T19:00:00-05:00" "2027-01-01T05:00:00+05:00"
              Expect.equal shifted.Expires (Some utcInstant) "an offset-bearing expiry is the same instant"

              Expect.equal
                  (shifted.Expires |> Option.map _.Offset)
                  (Some TimeSpan.Zero)
                  "normalised to UTC rather than kept in the document's offset"

              Expect.equal shifted.RevokedFrom (Some utcInstant) "and so is the revocation boundary"

              // (3) The reading is culture-independent too. th-TH is the classic
              //     trap: its default calendar is the Buddhist era, so a
              //     culture-sensitive read of the same digits lands 543 years
              //     away.
              let original = CultureInfo.CurrentCulture

              try
                  CultureInfo.CurrentCulture <- CultureInfo "th-TH"
                  let underThai = directory "2027-01-01T00:00:00" "2027-01-01T00:00:00"
                  Expect.equal underThai.Expires (Some utcInstant) "the ambient culture changes nothing"
              finally
                  CultureInfo.CurrentCulture <- original
          }

          test "GO-RED: a signer refuses a key on another 256-bit curve, naming the curve" {
              // Key size does not identify a curve. secp256k1 is 256 bits and is
              // NOT what `ecdsa-p256-sha256-v1` names, so the pre-1525 size gate
              // admitted it and this signer would have minted attestations under
              // an algorithm id that does not describe them.
              use secp = ECDsa.Create(ECCurve.CreateFromFriendlyName "secp256k1")
              Expect.equal secp.KeySize 256 "the fixture is 256 bits — a size gate cannot see the difference"

              let refusalFor (k: ECDsa) : string =
                  try
                      EcdsaP256.signer now "key-1" k |> ignore
                      failtest "the signer accepted a key the algorithm id does not describe"
                  with :? ArgumentException as e ->
                      e.Message

              let message = refusalFor secp

              // The spelling of a curve's friendly name is the PLATFORM's, not
              // ours: Windows CNG exports `secp256k1`, Linux's OpenSSL exports
              // `secP256k1`. The refusal is required to name the curve, not to
              // agree with one host's capitalisation of it — which is also why
              // `checkCurve` matches its own accepted names with
              // `OrdinalIgnoreCase`. A case-sensitive assertion here passed on
              // Windows and failed the Linux CI leg.
              Expect.isTrue
                  (message.Contains("secp256k1", StringComparison.OrdinalIgnoreCase))
                  $"the refusal names the curve the key is actually on: {message}"

              Expect.stringContains message "P-256" "and the curve the algorithm id requires"

              // The same is true of P-521, which the size gate also caught — kept
              // so the widening of the check is not mistaken for a replacement.
              use p521 = ECDsa.Create(ECCurve.NamedCurves.nistP521)
              Expect.stringContains (refusalFor p521) "P-256" "a P-521 key is still refused"
          }

          test "GO-RED: a verifier refuses an attestation signed under another 256-bit curve" {
              // The signer refuses to build one, so the forgery is assembled by
              // hand — which is exactly what an adversary with a secp256k1 key
              // and a directory entry claiming `ecdsa-p256-sha256-v1` would do.
              // The signature is REAL: it verifies under its own key, so the only
              // thing standing between it and an `Attested` verdict is the curve
              // check.
              use secp = ECDsa.Create(ECCurve.CreateFromFriendlyName "secp256k1")
              let records = makeChain ()

              match SegmentDescriptor.forRecords AttestationAlgorithm.ecdsaP256Sha256V1 records with
              | Error e -> failtestf "forRecords failed: %s" e
              | Ok descriptor ->
                  let signedAt = now ()
                  let payload = SegmentAttestation.claimPayload descriptor "key-1" signedAt false

                  let attestation =
                      { Descriptor = descriptor
                        KeyId = "key-1"
                        SignedAt = signedAt
                        Adopted = false
                        Signature =
                          Convert.ToBase64String(
                              secp.SignData(System.Text.Encoding.UTF8.GetBytes payload, HashAlgorithmName.SHA256)
                          ) }

                  let bundle =
                      { Records = records
                        Attestation = Some attestation
                        Keys = [] }

                  let entry = EcdsaP256.keyEntry "key-1" secp

                  Expect.equal
                      entry.Algorithm
                      AttestationAlgorithm.ecdsaP256Sha256V1
                      "the directory entry claims the P-256 algorithm id"

                  match verifyWith entry bundle with
                  | EvidenceVerdict.SignatureInvalid "key-1" -> ()
                  | other -> failtestf "expected SignatureInvalid for a secp256k1 key, got %A" other
          }

          test "GO-RED: SignedAt is stored at the resolution it was signed at" {
              // The claim payload binds `signedAt` as unix SECONDS. Storing the
              // signer's full-precision clock reading beside it meant the record
              // held a value the signature did not fix, and every consumer of it
              // — the revocation boundary above all — read that unfixed value.
              let subSecond () =
                  DateTimeOffset(2026, 8, 1, 12, 0, 0, 500, TimeSpan.Zero)

              use key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
              let signer = EcdsaP256.signerWith subSecond false "key-1" key

              let bundle =
                  Evidence.produce signer AttestationAlgorithm.ecdsaP256Sha256V1 (makeChain ())
                  |> run
                  |> function
                      | Ok b -> b
                      | Error e -> failtestf "produce failed: %s" e

              let attestation =
                  match bundle.Attestation with
                  | Some a -> a
                  | None -> failtest "the signer produced no attestation"

              Expect.equal
                  attestation.SignedAt
                  (SegmentAttestation.signedInstant attestation.SignedAt)
                  "the stored SignedAt is already at the bound resolution"

              // A store round trip is exactly this: the record is written at the
              // resolution the format carries and read back. With the signer
              // storing what it bound, the round trip is the identity.
              let roundTripped =
                  { attestation with
                      SignedAt = DateTimeOffset.FromUnixTimeSeconds(attestation.SignedAt.ToUnixTimeSeconds()) }

              Expect.equal roundTripped attestation "a store round trip returns the same record"

              Expect.equal
                  (SegmentAttestation.claimPayloadOf roundTripped)
                  (SegmentAttestation.claimPayloadOf attestation)
                  "so the bytes verified after the round trip are the bytes that were signed"

              match
                  verifyWith
                      (EcdsaP256.keyEntry "key-1" key)
                      { bundle with
                          Attestation = Some roundTripped }
              with
              | EvidenceVerdict.Attested("key-1", _, 1, 3, []) -> ()
              | other -> failtestf "expected Attested after the round trip, got %A" other
          }

          test "the revocation boundary is decided by the instant the signature covers" {
              // Sub-second precision on a stored `SignedAt` is not covered by the
              // signature, so a store-writer could shift it within its second. It
              // must not be able to move an attestation across a revocation
              // boundary by doing so.
              let atSecond = DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero)

              use key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
              let signer = EcdsaP256.signerWith (fun () -> atSecond) false "key-1" key

              let bundle =
                  Evidence.produce signer AttestationAlgorithm.ecdsaP256Sha256V1 (makeChain ())
                  |> run
                  |> function
                      | Ok b -> b
                      | Error e -> failtestf "produce failed: %s" e

              let attestation = Option.get bundle.Attestation

              // A store-writer nudges SignedAt forward by 900ms. The signature
              // still verifies (the payload floors it), so only the lifecycle
              // comparison can be affected.
              let nudged =
                  { bundle with
                      Attestation =
                          Some
                              { attestation with
                                  SignedAt = attestation.SignedAt.AddMilliseconds 900.0 } }

              let entry =
                  { EcdsaP256.keyEntry "key-1" key with
                      RevokedFrom = Some(atSecond.AddMilliseconds 500.0) }

              match verifyWith entry nudged with
              | EvidenceVerdict.Attested("key-1", _, _, _, warnings) ->
                  Expect.contains
                      warnings
                      (EvidenceWarning.KeyRevokedAfterSigning "key-1")
                      "revoked AFTER signing — a warning, not a void attestation"
              | other -> failtestf "expected Attested (the nudge must not void it), got %A" other
          } ]
