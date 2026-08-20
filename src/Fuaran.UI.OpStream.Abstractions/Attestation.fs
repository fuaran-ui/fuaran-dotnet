namespace Fuaran.UI.OpStream.Abstractions

open System
open Fuaran.UI.Ops.Types

// ============================================================================
//  Segment attestation — signed evidence BESIDE the unkeyed chain (Phase 789).
//
//  The chain stays an UNKEYED SHA-256 content digest (see `HashChain.fs` and
//  `CRYPTO.md`): it detects accidental corruption, and anyone who can write
//  the store can re-chain around an edit. What this file adds is the property
//  re-chaining cannot forge: a SIGNATURE over the chain POSITION. The holder
//  of a signing key asserts that, at the time it signed, the stream identified
//  by `StreamId` contained exactly the records at sequences `[FromSeq, ToSeq]`
//  whose chain head is `Head`, anchored at `PreviousHash`. An editor with
//  store write access can still rewrite records and recompute every digest —
//  but the stored attestation covers the OLD head, and the recomputed head no
//  longer matches it (`EvidenceVerdict.HeadMismatch`). Stripping the
//  attestation yields `Unattested`, never `Attested`, so the attack degrades
//  to visible denial rather than silent forgery.
//
//  Boundaries, stated as carefully as the property — keep every description of
//  this mechanism inside them (the discipline `CRYPTO.md` sets):
//   - It does not prove a USER authored anything. The key is held by a host,
//     so the claim is "this host accepted these records". A browser holds no
//     signing key: what a browser cannot hold is an issued identity — any
//     script on the origin can USE a non-extractable key without reading it —
//     so client-originated ops are attestable only once a server signs them.
//   - It does not defend against the KEY HOLDER. A compromised signer signs
//     its own forgery; the mechanism moves the question to key custody.
//   - It does not prove COMPLETENESS. It proves what was recorded, never that
//     everything was recorded; an op never appended leaves no trace.
//   - `SignedAt` is asserted by the signer (it is bound inside the signed
//     claim, so a store-writer cannot alter it — but the SIGNER can backdate).
//     The revocation boundary that compares against it is therefore a
//     co-operative-failure mechanism, not a defence against a hostile signer.
//
//  Design posture:
//   - ADDITIVE. The chain format is untouched (`StreamEntry.chainFormatVersion`
//     stays 2); every existing store keeps verifying unchanged, and its
//     evidentiary status is honestly `Unattested` — a true statement about it,
//     not a defect in it. Vouching for pre-attestation history after the fact
//     is a permanently distinct claim tier (`Adopted = true`), never a
//     back-dated signature.
//   - OPT-IN, and the unattested mode is NAMED (`AttestationSigner.unattested`)
//     rather than silently defaulted-into.
//   - The REQUIRED-or-not policy lives with the VERIFIER, never the store. An
//     attacker who can strip an attestation could also strip a stored policy
//     declaration, so a store cannot be trusted to describe its own trust
//     level; a consumer asking an evidence question requires an attestation
//     and treats `Unattested` as a refusal.
//   - ALGORITHM-AGILE. The algorithm id is a field bound inside the signed
//     claim; verifiers dispatch on it and refuse ids they do not implement. A
//     new primitive is a new registered id, never a format change.
//   - No key, and no crypto that needs one, enters the portable surface of
//     this package. Implementations live behind the asynchronous seams below
//     (browser crypto is asynchronous, so the seam is asynchronous; a
//     synchronous host provider adapts trivially — `AttestationSigner.ofCoreSink`).
//
//  Cross-host contract: the canonical descriptor / claim encodings below are
//  pinned byte-for-byte, with golden vectors in
//  `wire-format-fixtures/attestation/descriptor-corpus.json`.
// ============================================================================

/// Registered attestation algorithm ids. An id names the signature primitive,
/// the digest, AND the signature encoding; it is bound inside the signed claim
/// and verifiers dispatch on it. `ed25519-v1` is reserved for when the .NET
/// BCL carries a standalone Ed25519 signer — adding it is a new id, a new
/// provider and a key-directory entry, with no format change.
module AttestationAlgorithm =

    /// ECDSA over NIST P-256 with SHA-256; signature = base64 of the 64-byte
    /// IEEE P1363 `r||s` — the format the BCL and WebCrypto both produce, so
    /// the same attestation verifies on either host with no re-encoding.
    [<Literal>]
    let ecdsaP256Sha256V1 = "ecdsa-p256-sha256-v1"

/// The declared range a segment attestation covers — one claim, every field
/// load-bearing. It binds the RANGE (not just a tip, so a partial extract is
/// showable), the STREAM ID (which the chain pre-image deliberately excludes,
/// so an attested segment cannot be relabelled into a different stream), the
/// ANCHOR (`PreviousHash` — closing the gap `Verify.segment` names, where a
/// segment starting above sequence 1 has its first `PreviousHash` taken on
/// trust), and the chain format version (so a verifier knows which pre-image
/// rule to re-walk under).
type SegmentDescriptor =
    {
        /// Registered algorithm id (see `AttestationAlgorithm`).
        Algorithm: string
        /// The chain format the covered records hash under
        /// (`StreamEntry.chainFormatVersion` for every store this package writes).
        ChainFormatVersion: int
        /// The stream the range belongs to.
        StreamId: string
        /// First covered sequence (1-based, inclusive).
        FromSeq: int
        /// Last covered sequence (1-based, inclusive).
        ToSeq: int
        /// The chain anchor: the `PreviousHash` of the record at `FromSeq`
        /// (`HashChain.genesisPreviousHash` when `FromSeq = 1`).
        PreviousHash: string
        /// The `Hash` of the record at `ToSeq` — the attested chain head.
        Head: string
    }

module SegmentDescriptor =

    /// The descriptor FORMAT version, folded first into the canonical encoding
    /// (the same self-describing discipline as `StreamEntry.chainFormatVersion`).
    /// It versions the descriptor/claim shape independently of the chain format.
    [<Literal>]
    let descriptorVersion = 1

    /// Canonical JSON string escaping — mirrors `CanonicalJson.appendRawString`
    /// (only `"` / `\` / control chars, control as `\u00xx`) so the bytes are
    /// identical to the encoder the rest of the wire format uses.
    let internal jstr (s: string) : string =
        let sb = System.Text.StringBuilder()
        sb.Append '"' |> ignore

        for ch in s do
            match ch with
            | '"' -> sb.Append "\\\"" |> ignore
            | '\\' -> sb.Append "\\\\" |> ignore
            | c when c < ' ' -> sb.Append(sprintf "\\u%04x" (int c)) |> ignore
            | c -> sb.Append c |> ignore

        sb.Append '"' |> ignore
        sb.ToString()

    /// The pinned field body shared by `encode` and the claim payload. Field
    /// order — descriptorVersion / algorithm / chainFormatVersion / streamId /
    /// fromSeq / toSeq / previousHash / head — is a cross-host contract; the
    /// golden vectors in `wire-format-fixtures/attestation/` pin the bytes.
    let internal fields (d: SegmentDescriptor) : string =
        "\"descriptorVersion\":"
        + string descriptorVersion
        + ",\"algorithm\":"
        + jstr d.Algorithm
        + ",\"chainFormatVersion\":"
        + string d.ChainFormatVersion
        + ",\"streamId\":"
        + jstr d.StreamId
        + ",\"fromSeq\":"
        + string d.FromSeq
        + ",\"toSeq\":"
        + string d.ToSeq
        + ",\"previousHash\":"
        + jstr d.PreviousHash
        + ",\"head\":"
        + jstr d.Head

    /// The canonical encoding of a descriptor — deterministic bytes, pinned
    /// field order, the estate's hand-built canonical-JSON discipline.
    let encode (d: SegmentDescriptor) : string = "{" + fields d + "}"

    /// Build the descriptor a contiguous, already-read record list claims:
    /// range from first/last `Sequence`, anchor from the first record's
    /// `PreviousHash`, head from the last record's `Hash`. Chain integrity is
    /// NOT checked here — `Evidence.produce` verifies before signing.
    let forRecords (algorithm: string) (records: OpRecord<'Msg> list) : Result<SegmentDescriptor, string> =
        match records with
        | [] -> Error "cannot describe an empty segment"
        | first :: _ ->
            let last = List.last records

            if records |> List.exists (fun r -> r.StreamId <> first.StreamId) then
                Error "cannot describe a segment spanning more than one stream"
            else
                Ok
                    { Algorithm = algorithm
                      ChainFormatVersion = StreamEntry.chainFormatVersion
                      StreamId = first.StreamId
                      FromSeq = first.Sequence
                      ToSeq = last.Sequence
                      PreviousHash = first.PreviousHash
                      Head = last.Hash }

/// A signature over a segment claim. `Signature` covers the canonical CLAIM
/// payload (`SegmentAttestation.claimPayload`), which binds the descriptor
/// PLUS `KeyId`, `SignedAt` and `Adopted` — so a store-writer can alter none
/// of them without invalidating the signature. (An unbound `SignedAt` would
/// let a store-writer dodge a revocation boundary; an unbound `Adopted` would
/// let one promote a vouched-after-the-fact claim to a witnessed one.)
type SegmentAttestation =
    {
        Descriptor: SegmentDescriptor
        /// Names the signing key in the verifier's key directory.
        KeyId: string
        /// Asserted by the SIGNER — see the boundary note in the file header.
        SignedAt: DateTimeOffset
        /// `true` = an adoption: the key holder vouches for pre-attestation
        /// history AFTER THE FACT. Permanently a distinct claim tier from a
        /// contemporaneous attestation — it says "I vouch for this now", never
        /// "this was witnessed when produced" — and verifiers surface it as a
        /// warning so a consumer can weigh the two differently.
        Adopted: bool
        /// Signature bytes in the algorithm id's declared encoding
        /// (base64 IEEE P1363 `r||s` for `ecdsa-p256-sha256-v1`).
        Signature: string
    }

module SegmentAttestation =

    /// The exact string whose UTF-8 bytes the signature covers. Pinned field
    /// order: the descriptor fields, then keyId / signedAt / adopted.
    /// `signedAt` is unix seconds, matching the chain pre-image's timestamp
    /// resolution.
    let claimPayload (d: SegmentDescriptor) (keyId: string) (signedAt: DateTimeOffset) (adopted: bool) : string =
        "{"
        + SegmentDescriptor.fields d
        + ",\"keyId\":"
        + SegmentDescriptor.jstr keyId
        + ",\"signedAt\":"
        + string (signedAt.ToUnixTimeSeconds())
        + ",\"adopted\":"
        + (if adopted then "true" else "false")
        + "}"

    /// The claim payload an existing attestation asserts.
    let claimPayloadOf (a: SegmentAttestation) : string =
        claimPayload a.Descriptor a.KeyId a.SignedAt a.Adopted

/// One key's entry in a verifier's key directory. `PublicKeySpki` is the
/// base64 DER `SubjectPublicKeyInfo` — the one public-key encoding the BCL
/// (`ImportSubjectPublicKeyInfo`) and WebCrypto (`importKey("spki", …)`) both
/// consume, so a directory serves either host unchanged.
type KeyDirectoryEntry =
    {
        KeyId: string
        /// The algorithm this key signs under (see `AttestationAlgorithm`).
        Algorithm: string
        PublicKeySpki: string
        /// Advisory validity start — a signature dated before it is flagged,
        /// never failed.
        NotBefore: DateTimeOffset option
        /// Advisory expiry — a lapsed expiry is a warning on the verdict, never
        /// a verification failure, so an expiry can never silently invalidate
        /// history.
        Expires: DateTimeOffset option
        /// Revocation boundary. A signature dated BEFORE it is valid and
        /// flagged; one dated at or after it is void. The comparison is against
        /// the self-asserted `SignedAt`, so this is a co-operative-failure
        /// mechanism — see the file header.
        RevokedFrom: DateTimeOffset option
    }

/// How a verifier obtains keys. Distribution is a HOST responsibility reached
/// through this seam — never a hardcoded path, and never the evidence bundle
/// itself: a bundle carrying its own trust root proves only that it agrees
/// with itself. Establishing that a key id belongs to a party is out of band
/// by construction (a published keyring, a certificate, a fingerprint
/// exchanged some other way).
type IKeyDirectory =
    /// Resolve a key id to its directory entry, or `None` for an unknown key.
    abstract member ResolveKey: keyId: string -> Async<KeyDirectoryEntry option>

/// The signing half of the attestation seam. Asynchronous by design — browser
/// crypto is asynchronous, and a synchronous host implementation adapts
/// trivially (`AttestationSigner.ofCoreSink`). The key never crosses this
/// interface: an implementation holds it host-side (KMS / HSM / credential
/// store) and only the attestation comes back.
type IAttestationSigner =
    /// Sign a segment claim, or `None` for the named unattested posture.
    abstract member SignSegment: descriptor: SegmentDescriptor -> Async<SegmentAttestation option>

/// The crypto half of verification: does `attestation.Signature` verify over
/// the canonical claim payload under `key`? Implementations dispatch on the
/// algorithm id and answer `false` for ids they do not implement. Everything
/// ABOVE the crypto — key resolution, revocation, range and chain checks —
/// lives in `Evidence.verify`, which returns a typed verdict rather than a
/// boolean precisely because "the signature is wrong", "the key is unknown"
/// and "there is no attestation" demand different responses from a consumer.
type IAttestationVerifier =
    abstract member VerifySignature: attestation: SegmentAttestation -> key: KeyDirectoryEntry -> Async<bool>

module KeyDirectory =

    /// An in-memory directory over a fixed entry list — the reference shape
    /// for tests and for a host that loads its directory elsewhere.
    let ofList (entries: KeyDirectoryEntry list) : IKeyDirectory =
        { new IKeyDirectory with
            member _.ResolveKey keyId =
                async { return entries |> List.tryFind (fun e -> e.KeyId = keyId) } }

module AttestationSigner =

    /// The NAMED unattested posture — the default is off, and choosing it is
    /// visible at the call site rather than silently defaulted-into. Streams
    /// produced under this signer are honestly `Unattested`.
    let unattested: IAttestationSigner =
        { new IAttestationSigner with
            member _.SignSegment _ = async { return None } }

    /// Adapt `Fuaran.Core`'s synchronous `IAttestationSink` (the Phase-320
    /// seam) into the asynchronous signer. Core's seam signs an opaque string,
    /// so passing the canonical claim payload — rather than a bare head —
    /// needs no Core change at all. The caller names the key id the sink
    /// signs under (Core's interface offers no way to ask without signing);
    /// if the sink's returned attestation names a DIFFERENT key, the result
    /// is discarded rather than recorded under a claim the signature does not
    /// cover. `now` is host-supplied (the same host-supplies-the-effect
    /// discipline as clocks and hashing), and stamps the self-asserted
    /// `SignedAt`.
    let ofCoreSink
        (now: unit -> DateTimeOffset)
        (adopted: bool)
        (keyId: string)
        (sink: Fuaran.Core.IAttestationSink)
        : IAttestationSigner =
        { new IAttestationSigner with
            member _.SignSegment descriptor =
                async {
                    let signedAt = now ()

                    let payload = SegmentAttestation.claimPayload descriptor keyId signedAt adopted

                    match sink.Sign payload with
                    | Some signed when signed.KeyId = keyId ->
                        return
                            Some
                                { Descriptor = descriptor
                                  KeyId = keyId
                                  SignedAt = signedAt
                                  Adopted = adopted
                                  Signature = signed.Signature }
                    | _ -> return None
                } }

/// Non-fatal findings attached to an `Attested` verdict. A consumer that
/// treats any warning as fatal is choosing a stricter policy than the
/// mechanism imposes — which is its right (the required-or-not policy lives
/// with the verifier).
[<RequireQualifiedAccess>]
type EvidenceWarning =
    /// The key's advisory expiry predates `SignedAt`.
    | KeyExpired of keyId: string
    /// `SignedAt` predates the key's advisory `NotBefore`.
    | KeySignedBeforeValidity of keyId: string
    /// The key was revoked AFTER this attestation's `SignedAt` — the
    /// signature is valid, and the key is no longer trustworthy for new
    /// attestations.
    | KeyRevokedAfterSigning of keyId: string
    /// The attestation is an adoption — the key holder vouched for the range
    /// after the fact rather than witnessing it when produced.
    | Adopted

/// What a verification actually established — a typed verdict, never a
/// boolean, because its cases demand different responses: `Unattested` is a
/// refusal for an evidence consumer and normal for a playground;
/// `UnknownKey` means resolve the key out of band and retry; `HeadMismatch`
/// means the records shown are not the records attested — the re-chained-store
/// signal this mechanism exists to produce.
[<RequireQualifiedAccess>]
type EvidenceVerdict =
    /// The claim verified end-to-end: the signature is authentic under a
    /// directory-resolved key, the records re-walk cleanly from the signed
    /// anchor, and the walked head equals the signed head.
    | Attested of keyId: string * algorithm: string * fromSeq: int * toSeq: int * warnings: EvidenceWarning list
    /// No attestation covers the range. A true statement about every store
    /// predating attestation, and about every store whose host chose
    /// `AttestationSigner.unattested`.
    | Unattested
    /// The records do not re-walk cleanly from the signed anchor — corruption,
    /// truncation, reordering, or a segment lifted from a different position
    /// (its first `PreviousHash` no longer matches the signed anchor).
    | ChainBroken of VerificationError
    /// The descriptor claims a chain format this verifier does not implement,
    /// so it cannot re-walk the records under the right pre-image rule.
    | UnsupportedChainFormat of version: int
    /// The signature does not verify over the canonical claim payload under
    /// the named key — or the attestation is void (signed at or after the
    /// key's revocation boundary, or under a key of a different algorithm).
    | SignatureInvalid of keyId: string
    /// The verifier's key directory does not know the key id. Deliberately
    /// distinct from `SignatureInvalid`: the remedy is establishing the key
    /// out of band, not distrusting the artefact.
    | UnknownKey of keyId: string
    /// The records present do not match the descriptor's declared stream or
    /// range bounds.
    | RangeMismatch of claimed: string * actual: string
    /// The records re-walk cleanly but their head is not the signed head —
    /// the records shown are not the records attested. This is the verdict a
    /// re-chained store produces: the edit re-computed every digest
    /// consistently, and the stored attestation still covers the old head.
    | HeadMismatch of claimed: string * actual: string

/// A self-contained evidence artefact: the records of one contiguous range
/// plus the attestation covering them. `Verify` takes ONLY the bundle (plus
/// the verifier's own key directory) — no service call, no database, no
/// private key — which is what makes the artefact evidence rather than a
/// claim that a system agrees with itself.
type EvidenceBundle<'Msg> =
    {
        Records: OpRecord<'Msg> list
        /// `None` = an unattested extract — honest, and verifiable only as such.
        Attestation: SegmentAttestation option
        /// CONVENIENCE ONLY, never a trust root: a verifier that has already
        /// established these keys by another route may preload its directory
        /// from them. `Evidence.verify` never reads this field — an adversary
        /// who mints a key pair and ships the public half has produced a bundle
        /// that agrees with itself, which is why resolution goes through the
        /// verifier's own `IKeyDirectory` or fails as `UnknownKey`.
        Keys: KeyDirectoryEntry list
    }

module Evidence =

    /// Produce an evidence bundle for a contiguous record range: verify the
    /// segment locally (a signer must never attest a chain that does not
    /// verify), then sign its descriptor. A signer answering `None` (the
    /// unattested posture) yields an honest unattested bundle.
    let produce
        (signer: IAttestationSigner)
        (algorithm: string)
        (records: OpRecord<'Msg> list)
        : Async<Result<EvidenceBundle<'Msg>, string>> =
        async {
            match SegmentDescriptor.forRecords algorithm records with
            | Error e -> return Error e
            | Ok descriptor ->
                match Verify.segmentFrom descriptor.PreviousHash descriptor.FromSeq records with
                | Error e ->
                    return
                        Error(
                            "refusing to attest a segment whose chain does not verify: "
                            + Verify.describe descriptor.StreamId e
                        )
                | Ok() ->
                    let! attestation = signer.SignSegment descriptor

                    return
                        Ok
                            { Records = records
                              Attestation = attestation
                              Keys = [] }
        }

    let private describeRange (streamId: string) (fromSeq: int) (toSeq: int) : string =
        "stream '" + streamId + "' seq " + string fromSeq + ".." + string toSeq

    /// Verify a bundle offline: authenticate the claim first (key resolution,
    /// algorithm dispatch, revocation, signature), then prove the records ARE
    /// the claimed range (bounds, chain re-walk from the SIGNED anchor, head
    /// comparison). Requires nothing beyond the bundle and the verifier's own
    /// key directory.
    let verify
        (directory: IKeyDirectory)
        (verifier: IAttestationVerifier)
        (bundle: EvidenceBundle<'Msg>)
        : Async<EvidenceVerdict> =
        async {
            match bundle.Attestation with
            | None -> return EvidenceVerdict.Unattested
            | Some attestation ->
                let d = attestation.Descriptor

                if d.ChainFormatVersion <> StreamEntry.chainFormatVersion then
                    return EvidenceVerdict.UnsupportedChainFormat d.ChainFormatVersion
                else
                    let! resolved = directory.ResolveKey attestation.KeyId

                    match resolved with
                    | None -> return EvidenceVerdict.UnknownKey attestation.KeyId
                    | Some key when key.Algorithm <> d.Algorithm ->
                        // A signature cannot be valid under a key of a
                        // different algorithm; treating it as invalid rather
                        // than unknown keeps `UnknownKey`'s remedy honest.
                        return EvidenceVerdict.SignatureInvalid attestation.KeyId
                    | Some key ->
                        let! signatureOk = verifier.VerifySignature attestation key

                        let signedAtOrAfterRevocation =
                            match key.RevokedFrom with
                            | Some boundary -> attestation.SignedAt >= boundary
                            | None -> false

                        if not signatureOk || signedAtOrAfterRevocation then
                            return EvidenceVerdict.SignatureInvalid attestation.KeyId
                        else
                            let claimed = describeRange d.StreamId d.FromSeq d.ToSeq

                            match bundle.Records with
                            | [] -> return EvidenceVerdict.RangeMismatch(claimed, "an empty record set")
                            | first :: _ ->
                                let last = List.last bundle.Records

                                let actual = describeRange first.StreamId first.Sequence last.Sequence

                                if
                                    bundle.Records |> List.exists (fun r -> r.StreamId <> d.StreamId)
                                    || first.Sequence <> d.FromSeq
                                    || last.Sequence <> d.ToSeq
                                then
                                    return EvidenceVerdict.RangeMismatch(claimed, actual)
                                else
                                    match Verify.segmentFrom d.PreviousHash d.FromSeq bundle.Records with
                                    | Error e -> return EvidenceVerdict.ChainBroken e
                                    | Ok() when last.Hash <> d.Head ->
                                        return EvidenceVerdict.HeadMismatch(d.Head, last.Hash)
                                    | Ok() ->
                                        let warnings =
                                            [ match key.Expires with
                                              | Some expiry when attestation.SignedAt > expiry ->
                                                  EvidenceWarning.KeyExpired key.KeyId
                                              | _ -> ()
                                              match key.NotBefore with
                                              | Some notBefore when attestation.SignedAt < notBefore ->
                                                  EvidenceWarning.KeySignedBeforeValidity key.KeyId
                                              | _ -> ()
                                              match key.RevokedFrom with
                                              | Some _ -> EvidenceWarning.KeyRevokedAfterSigning key.KeyId
                                              | None -> ()
                                              if attestation.Adopted then
                                                  EvidenceWarning.Adopted ]

                                        return
                                            EvidenceVerdict.Attested(
                                                attestation.KeyId,
                                                d.Algorithm,
                                                d.FromSeq,
                                                d.ToSeq,
                                                warnings
                                            )
        }
