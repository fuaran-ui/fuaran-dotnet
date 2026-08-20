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

    /// A signer over a host-supplied P-256 key. Refuses (throws) at
    /// construction for a key of any other size — a signer that silently
    /// produced attestations no registered algorithm id describes would be a
    /// defect, not a fallback. `now` supplies the self-asserted `SignedAt`;
    /// `adopted` marks the vouched-after-the-fact claim tier.
    let signerWith (now: unit -> DateTimeOffset) (adopted: bool) (keyId: string) (key: ECDsa) : IAttestationSigner =
        if key.KeySize <> 256 then
            invalidArg
                (nameof key)
                ("ecdsa-p256-sha256-v1 requires a P-256 key; this key's size is "
                 + string key.KeySize)

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
                        let signedAt = now ()

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

    /// The crypto verifier: imports the directory entry's SPKI public key and
    /// checks the signature over the canonical claim payload. Dispatches on
    /// the algorithm id — anything other than `ecdsa-p256-sha256-v1` (on the
    /// key or the claim), a non-P-256 key, or malformed key/signature bytes
    /// answers `false`; `Evidence.verify` renders that as `SignatureInvalid`.
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
                        try
                            use ecdsa = ECDsa.Create()
                            let mutable bytesRead = 0

                            ecdsa.ImportSubjectPublicKeyInfo(
                                ReadOnlySpan<byte>(Convert.FromBase64String key.PublicKeySpki),
                                &bytesRead
                            )

                            if ecdsa.KeySize <> 256 then
                                return false
                            else
                                let payload = SegmentAttestation.claimPayloadOf attestation

                                return
                                    ecdsa.VerifyData(
                                        Encoding.UTF8.GetBytes payload,
                                        Convert.FromBase64String attestation.Signature,
                                        HashAlgorithmName.SHA256
                                    )
                        with
                        | :? CryptographicException
                        | :? FormatException -> return false
                } }

/// The file-backed reference `IKeyDirectory`: a JSON document of PUBLIC keys
/// (`{"keys":[{"keyId":…,"algorithm":…,"publicKeySpki":…,"notBefore":…,
/// "expires":…,"revokedFrom":…}]}`, timestamps ISO-8601, the three lifecycle
/// fields optional/null). A reference implementation — production hosts
/// resolve keys however their trust root demands (a published keyring, a
/// certificate chain, an out-of-band fingerprint); the seam exists so that
/// choice is the host's, never this package's.
module FileKeyDirectory =

    let private optionalDate (element: JsonElement) (name: string) : DateTimeOffset option =
        match element.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.String ->
            match value.GetString() with
            | null -> None
            | s -> Some(DateTimeOffset.Parse(s, CultureInfo.InvariantCulture))
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
