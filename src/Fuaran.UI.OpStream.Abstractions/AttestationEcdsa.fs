namespace Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  The .NET attestation provider (Phase 789) — ECDSA P-256 / SHA-256 through
//  the BCL only, plus a file-backed reference key directory.
//
//  Server-only by design, fenced out of the Fable pipeline entirely: browser
//  hosts VERIFY (over the same `IAttestationVerifier` seam, via WebCrypto's
//  asynchronous ECDSA) and never sign — a browser cannot hold an issued
//  identity, so client-originated ops are attestable only once a server signs
//  them. Until a browser-side verifier lands, the signing AND verifying
//  property is explicitly scoped to .NET hosts; the portable vocabulary in
//  `Attestation.fs` compiles on both pipelines so the types travel now.
//
//  RULE, not a hope: no hand-rolled ECDSA SIGNER, ever. Signing goes through
//  the BCL (or WebCrypto), both of which own nonce generation — ECDSA's
//  nonce-reuse hazard is the honest cost of choosing the primitive both hosts
//  ship, and this rule is the mitigation. (A hand-written VERIFIER would be
//  acceptable if a browser route ever needs one: verification consumes no
//  secret.)
//
//  Key custody stays host-side: this module accepts an `ECDsa` handle the
//  HOST obtained (KMS, HSM, OS credential store, a permission-restricted
//  file) and never loads, stores, or exports private material itself. The
//  file-backed directory below holds PUBLIC keys only.
// ============================================================================

#if !FABLE_COMPILER

open System
open System.Globalization
open System.Security.Cryptography
open System.Text
open System.Text.Json

/// ECDSA P-256 / SHA-256 (`ecdsa-p256-sha256-v1`) over the platform BCL.
module EcdsaP256 =

    /// Export the base64 `SubjectPublicKeyInfo` of a key — the encoding a
    /// `KeyDirectoryEntry` carries, importable by the BCL and WebCrypto alike.
    let exportPublicKeySpki (key: ECDsa) : string =
        Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())

    /// A directory entry for a key this process holds — the shape a host
    /// publishes so verifiers elsewhere can resolve its attestations.
    let keyEntry (keyId: string) (key: ECDsa) : KeyDirectoryEntry =
        { KeyId = keyId
          Algorithm = AttestationAlgorithm.ecdsaP256Sha256V1
          PublicKeySpki = exportPublicKeySpki key
          NotBefore = None
          Expires = None
          RevokedFrom = None }

    // ── curve identity ────────────────────────────────────────────────────
    //
    //  `ecdsa-p256-sha256-v1` names ONE curve, and a key size does not name a
    //  curve (Phase 1525). `KeySize = 256` is satisfied by every 256-bit curve
    //  — Brainpool P256r1, secp256k1, and any explicit-parameters curve a host
    //  hands in — so the pre-1525 size gate admitted keys this algorithm id does
    //  not describe, on both the signing and the verifying side. What follows
    //  from that is not merely a mislabelled artefact: a verifier that accepts a
    //  differently-curved key has accepted a signature the registered id does not
    //  cover, and the id is what the whole algorithm-agility story rests on (a
    //  new primitive is a new id, never a format change). So both sides check the
    //  CURVE, and both name it in the refusal.

    /// NIST P-256's object identifier — the one curve `ecdsa-p256-sha256-v1`
    /// names. Also written secp256r1 / prime256v1; those are the same curve.
    [<Literal>]
    let private p256Oid = "1.2.840.10045.3.1.7"

    /// The friendly names the platforms print for that one curve. The OID is the
    /// authority; these exist because a Windows CNG export can carry a friendly
    /// name with no OID value, in which case the OID test alone would refuse a
    /// perfectly good P-256 key.
    let private p256FriendlyNames =
        [ "nistP256"; "ECDSA_P256"; "secp256r1"; "prime256v1" ]

    /// How a curve identifies itself, for a refusal message. Never assumes an
    /// OID or a friendly name is present — an unidentifiable curve is still
    /// reported, as unidentifiable.
    let private describeCurve (curve: ECCurve) : string =
        if not curve.IsNamed then
            "an explicit-parameters curve (the key names no curve at all)"
        else
            match curve.Oid.FriendlyName, curve.Oid.Value with
            | null, null -> "a named curve carrying neither an OID nor a name"
            | null, v -> "OID " + v
            | n, null -> n
            | n, v -> n + " (OID " + v + ")"

    /// `Ok ()` when `curve` is NIST P-256; otherwise `Error` naming what it
    /// actually is.
    let private checkCurve (curve: ECCurve) : Result<unit, string> =
        if not curve.IsNamed then
            Error(describeCurve curve)
        else
            let oidValue = curve.Oid.Value
            let friendly = curve.Oid.FriendlyName

            let matchesOid =
                match oidValue with
                | null -> false
                | v -> String.Equals(v, p256Oid, StringComparison.Ordinal)

            let matchesName =
                match friendly with
                | null -> false
                | n ->
                    p256FriendlyNames
                    |> List.exists (fun c -> String.Equals(c, n, StringComparison.OrdinalIgnoreCase))

            if matchesOid || matchesName then
                Ok()
            else
                Error(describeCurve curve)

    /// A signer over a host-supplied P-256 key. Refuses (throws) at
    /// construction for a key on any other CURVE — not merely of another size:
    /// key size does not identify a curve, and a signer that silently produced
    /// attestations no registered algorithm id describes would be a defect, not
    /// a fallback. The refusal names the curve the key is actually on.
    /// `now` supplies the self-asserted `SignedAt`; `adopted` marks the
    /// vouched-after-the-fact claim tier.
    let signerWith (now: unit -> DateTimeOffset) (adopted: bool) (keyId: string) (key: ECDsa) : IAttestationSigner =
        match checkCurve (key.ExportParameters(false).Curve) with
        | Error actual ->
            invalidArg
                (nameof key)
                ("ecdsa-p256-sha256-v1 requires a key on the NIST P-256 curve; this key is on "
                 + actual)
        | Ok() -> ()

        { new IAttestationSigner with
            member _.SignSegment descriptor =
                async {
                    if descriptor.Algorithm <> AttestationAlgorithm.ecdsaP256Sha256V1 then
                        return
                            invalidArg
                                (nameof descriptor)
                                ("this signer signs only "
                                 + AttestationAlgorithm.ecdsaP256Sha256V1
                                 + "; the descriptor declares "
                                 + descriptor.Algorithm)
                    else
                        // Normalised to the resolution the claim payload binds
                        // (unix seconds), so the `SignedAt` this attestation
                        // STORES is exactly the value the signature COVERS — a
                        // store round trip cannot change the signed bytes
                        // (Phase 1525).
                        let signedAt = SegmentAttestation.signedInstant (now ())

                        let payload = SegmentAttestation.claimPayload descriptor keyId signedAt adopted
                        // BCL default signature format for ECDsa is IEEE
                        // P1363 (r||s) — byte-compatible with WebCrypto.
                        let signature =
                            key.SignData(Encoding.UTF8.GetBytes payload, HashAlgorithmName.SHA256)

                        return
                            Some
                                { Descriptor = descriptor
                                  KeyId = keyId
                                  SignedAt = signedAt
                                  Adopted = adopted
                                  Signature = Convert.ToBase64String signature }
                } }

    /// A contemporaneous (non-adopted) signer — the common case.
    let signer (now: unit -> DateTimeOffset) (keyId: string) (key: ECDsa) : IAttestationSigner =
        signerWith now false keyId key

    /// The one crypto step both verifiers below take: import the directory
    /// entry's SPKI public key, check it is on the curve the algorithm id names,
    /// and verify `signature` over the UTF-8 bytes of `payload`. Answers `false`
    /// for a key on another curve or for malformed key/signature bytes — the
    /// callers render that as `SignatureInvalid`, which is where the typed
    /// verdict belongs.
    ///
    /// Shared deliberately (Phase 1549): a second copy of the curve check is a
    /// second place for the two ends of an algorithm id to drift apart, and the
    /// curve test is the whole of what keeps the id honest.
    let private verifyPayloadUnder (key: KeyDirectoryEntry) (payload: string) (signature: string) : bool =
        try
            use ecdsa = ECDsa.Create()
            let mutable bytesRead = 0

            ecdsa.ImportSubjectPublicKeyInfo(ReadOnlySpan<byte>(Convert.FromBase64String key.PublicKeySpki), &bytesRead)

            match checkCurve (ecdsa.ExportParameters(false).Curve) with
            | Error _ -> false
            | Ok() ->
                ecdsa.VerifyData(
                    Encoding.UTF8.GetBytes payload,
                    Convert.FromBase64String signature,
                    HashAlgorithmName.SHA256
                )
        with
        | :? CryptographicException
        | :? FormatException -> false

    /// The crypto verifier: imports the directory entry's SPKI public key and
    /// checks the signature over the canonical claim payload. Dispatches on
    /// the algorithm id — anything other than `ecdsa-p256-sha256-v1` (on the
    /// key or the claim), a key on a curve other than NIST P-256, or malformed
    /// key/signature bytes answers `false`; `Evidence.verify` renders that as
    /// `SignatureInvalid`.
    ///
    /// The curve test is the CURVE, not the key size (Phase 1525): a 256-bit key
    /// on secp256k1 or Brainpool P256r1 passed the pre-1525 size gate, so a
    /// directory entry naming `ecdsa-p256-sha256-v1` could be verified under a
    /// key the id does not describe. This side cannot name the curve in a
    /// message — the interface answers `bool`, deliberately, so the typed verdict
    /// stays `Evidence.verify`'s to give — but it refuses the same set the
    /// signer refuses, which is what keeps the two ends of the id honest.
    let verifier: IAttestationVerifier =
        { new IAttestationVerifier with
            member _.VerifySignature attestation key =
                async {
                    if
                        key.Algorithm <> AttestationAlgorithm.ecdsaP256Sha256V1
                        || attestation.Descriptor.Algorithm <> AttestationAlgorithm.ecdsaP256Sha256V1
                    then
                        return false
                    else
                        return
                            verifyPayloadUnder key (SegmentAttestation.claimPayloadOf attestation) attestation.Signature
                } }

    /// The document arm of the same crypto (Phase 1549): verify a signature over
    /// an arbitrary canonical claim payload. `decodeAttestedDocument` builds the
    /// payload and owns every decision above the crypto, exactly as
    /// `Evidence.verify` does for a segment, so this seam answers `bool` for the
    /// same reason its sibling does.
    ///
    /// The algorithm dispatch is on the KEY: a document claim's algorithm is
    /// checked against the resolved key before this is ever reached, so an id
    /// this provider does not implement is refused there rather than here.
    let claimVerifier: IClaimSignatureVerifier =
        { new IClaimSignatureVerifier with
            member _.VerifyClaim claimPayload signature key =
                async {
                    if key.Algorithm <> AttestationAlgorithm.ecdsaP256Sha256V1 then
                        return false
                    else
                        return verifyPayloadUnder key claimPayload signature
                } }

/// The file-backed reference `IKeyDirectory`: a JSON document of PUBLIC keys
/// (`{"keys":[{"keyId":…,"algorithm":…,"publicKeySpki":…,"notBefore":…,
/// "expires":…,"revokedFrom":…}]}`, timestamps ISO-8601, the three lifecycle
/// fields optional/null). A reference implementation — production hosts
/// resolve keys however their trust root demands (a published keyring, a
/// certificate chain, an out-of-band fingerprint); the seam exists so that
/// choice is the host's, never this package's.
module FileKeyDirectory =

    /// Parse one of the three ISO-8601 lifecycle timestamps.
    ///
    /// MACHINE-INDEPENDENT, and that is the whole point of the styles (Phase
    /// 1525). `DateTimeOffset.Parse` with default styles interprets a string
    /// carrying NO offset in the parsing machine's LOCAL time zone, so
    /// `"2027-01-01T00:00:00"` in a key directory becomes a different instant on
    /// every machine that reads it — and these three fields are the revocation
    /// boundary, the expiry and the validity start, so the same attestation
    /// would verify in London and be void in Sydney. A trust store cannot mean
    /// different things in different places.
    ///
    /// The choice made here is ASSUME UNIVERSAL rather than refuse: a directory
    /// is a published document, often hand-written, and an offset-less
    /// timestamp in one is overwhelmingly meant as UTC — refusing it outright
    /// would fail a store that is merely terse, and the refusal would land at
    /// load time on a host that has no way to edit someone else's keyring. So
    /// `AssumeUniversal` reads an offset-less string as UTC, `AdjustToUniversal`
    /// normalises a string that DOES carry an offset to the same instant in
    /// UTC, and `InvariantCulture` keeps the accepted grammar independent of the
    /// host's culture. Every machine then reads one instant, whichever spelling
    /// the document used.
    let private optionalDate (element: JsonElement) (name: string) : DateTimeOffset option =
        match element.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.String ->
            match value.GetString() with
            | null -> None
            | s ->
                Some(
                    DateTimeOffset.Parse(
                        s,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal ||| DateTimeStyles.AdjustToUniversal
                    )
                )
        | _ -> None

    let private requiredString (element: JsonElement) (name: string) : string =
        match element.GetProperty(name).GetString() with
        | null -> failwith ("key-directory entry field '" + name + "' must be a string")
        | s -> s

    /// Parse a key-directory JSON document. Throws with a named field on a
    /// malformed document — a trust store that fails to parse must fail
    /// loudly, never resolve as empty.
    let parse (json: string) : KeyDirectoryEntry list =
        use document = JsonDocument.Parse json

        [ for entry in document.RootElement.GetProperty("keys").EnumerateArray() ->
              { KeyId = requiredString entry "keyId"
                Algorithm = requiredString entry "algorithm"
                PublicKeySpki = requiredString entry "publicKeySpki"
                NotBefore = optionalDate entry "notBefore"
                Expires = optionalDate entry "expires"
                RevokedFrom = optionalDate entry "revokedFrom" } ]

    /// Load a directory from a file, once, at call time.
    let load (path: string) : IKeyDirectory =
        KeyDirectory.ofList (parse (IO.File.ReadAllText path))

#endif
