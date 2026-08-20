namespace Fuaran.UI.OpStream.Abstractions

open System
open System.Globalization
open Fuaran.UI.Ops.Types

// ============================================================================
//  Hash-chain primitive — SHA-256 over the canonical encoding.
//
//  Portable on BOTH pipelines (Phase 405): the digest routes through
//  `Fuaran.UI.Hashing.sha256Hex` — the pure, Fable-safe FIPS 180-4 SHA-256 the
//  language tier has shipped since Phase 164 (BCL-parity-tested) — so
//  `computeHash` + `Verify.chain` compile under Fable and a browser host can
//  verify a chain in-session, at parity with the TS host's dependency-free
//  SHA-256 (`@fuaran-ui/op-stream/src/hashChain.ts`). The pre-405 BCL
//  (`System.Security.Cryptography`) implementation produced byte-identical
//  digests, so existing chains verify unchanged.
//
//  Per `Fuaran.Core`'s GP3 ("crypto stays host-side", certified by
//  `Conformance.hashFnLaws`, fuaran-core#65) this is the host-owned crypto that
//  Phase 406 supplies to `Core.OpStream` as its `HashFn`.
//
//  `genesisPreviousHash` — sixty-four '0' characters — anchors every stream's
//  first record per the pinned algorithm in `docs/migrations/12-Z-op-stream.md`.
//
//  ── WHAT THE CHAIN PROVES, AND WHAT IT DOES NOT ────────────────────────────
//  The chain is an UNKEYED content digest. `Verify.chain` therefore detects
//  **accidental corruption, truncation and reordering** of a stored stream, and
//  — relative to an anchor you already trust — substitution of one record for
//  another. It is NOT tamper evidence: every input to every hash lives in the
//  store beside the hash, so anyone who can write the store edits a record,
//  recomputes the chain from that point, and verification passes. Using a
//  stronger unkeyed hash would not change this; collision resistance stops a
//  forged record sharing an EXISTING hash, not a writer who is free to change
//  the hash too.
//
//  Detecting an edit by someone with write access needs a secret the writer does
//  not have — a signature over the chain POSITION. That is the segment-
//  attestation seam beside this file (`Attestation.fs`, Phase 789, over Core's
//  Phase-320 `IAttestationSink` precedent): opt-in, additive, the key held
//  host-side so it never enters this portable package. The chain here stays
//  unkeyed and its own property stays corruption detection — for any stream a
//  host has not attested (the default), that is the WHOLE property. See
//  `CRYPTO.md`, and keep every description of this chain — in docs, doc
//  comments and error text — inside that boundary.
// ============================================================================

module HashChain =

    /// Sixty-four '0' characters. The `PreviousHash` of every stream's
    /// `Sequence = 1` record per the pinned algorithm in
    /// `docs/migrations/12-Z-op-stream.md`.
    let genesisPreviousHash = String.replicate 64 "0"

    /// The domain's chain binding for Core's list operations (Phase 411): Core's
    /// canonical delimited payload with the domain's 64-zero genesis sentinel —
    /// `StreamConfig.Genesis` is exactly the seam Core ships for a domain
    /// sentinel (the F4/F13 machinery). With the pre-image seq on Core's 0-based
    /// basis (see `computeHash`), `Core.OpStream.firstChainBreakWith chainConfig`
    /// verifies a UI chain directly.
    let chainConfig: Fuaran.Core.StreamConfig =
        { Fuaran.Core.OpStream.canonicalConfig with
            Genesis = genesisPreviousHash }

    /// SHA-256 → 64 lower-case hex characters. Pure managed (Fable-clean);
    /// byte-identical to the BCL — see `Fuaran.UI.Hashing`.
    let sha256Hex (payload: string) : string = Fuaran.UI.Hashing.sha256Hex payload

    /// The content address of a checkpoint snapshot (Phase 412 — A2). Binds the
    /// snapshot to its **position in the chain**: `previousChainHead` (the chain
    /// head at the checkpoint's op-index) + `sequence` + the canonical tree, in a
    /// delimited pre-image — the discipline Core's Phase-244 `snapPayload` uses
    /// (`hashFn prevHash {"snapshot":true,"seq":…,"state":…}`).
    ///
    /// Pre-412 the snapshot hash was `sha256(canonicalTree)` alone — position-free.
    /// What the binding adds: a valid snapshot + hash from one `(head, seq)` no
    /// longer validates at a different one, so **cross-position snapshot
    /// substitution** (replaying a real older snapshot as a later checkpoint) is
    /// caught, as is accidental corruption. Paired with the replay path anchoring
    /// `previousChainHead` to the real op-chain (including the empty-tail case),
    /// a checkpoint can only be trusted at a position the verified chain confirms.
    /// This is defence-in-depth, not tamper-proofing against an adversary who
    /// rewrites the whole checkpoint record consistently — that is the signed
    /// attestation seam's job (Core `IAttestationSink`, Phase 320).
    let snapshotHash (previousChainHead: string) (sequence: int) (canonicalTree: string) : string =
        let payload =
            "{\"snapshot\":true,\"seq\":"
            + sequence.ToString(CultureInfo.InvariantCulture)
            + ",\"tree\":"
            + canonicalTree
            + "}"

        sha256Hex (previousChainHead + "|" + payload)

    /// Compute the hash for an op-record. Identical algorithm on every host —
    /// verification re-derives this and compares.
    ///
    /// Phase 406 re-expresses the pre-image over `Fuaran.Core.OpStream`'s canonical
    /// format: the op + timestamp + prompt + apply-outcome bundle into a
    /// `StreamEntry` envelope that becomes Core's opaque `op`, wrapped in Core's
    /// delimited `{"seq":…,"actor":…,"op":…}` payload and hashed by the certified
    /// SHA-256 `HashFn`. This folds `promptId` and `resultEnvelope` INTO the chain
    /// (closing the pre-406 provenance hole) and delimits the pre-image (closing the
    /// raw seq++timestamp ambiguity). The actor is still part of the integrity chain
    /// (Phase 320) via Core's payload; the TS host mirrors `StreamEntry.encode` for a
    /// byte-identical pre-image (Phase 407).
    /// `sequence` is the domain's public **1-based** value; the pre-image folds
    /// **Core's 0-based record index** (`sequence - 1`, Phase 411) so a mapped
    /// record chain verifies under `Core.OpStream.firstChainBreakWith` with no
    /// translation beyond `Seq = Sequence - 1`. The 1-based presentation (the
    /// `0 = empty stream` sentinel, checkpoint-at-0) is API-only — it never
    /// enters the hash.
    let computeHash<'Msg>
        (previousHash: string)
        (op: TreeOp<'Msg>)
        (sequence: int)
        (timestamp: DateTimeOffset)
        (actor: Actor)
        (promptId: string option)
        (resultEnvelope: OpResultEnvelope)
        : string =
        StreamEntry.chainHash
            previousHash
            (sequence - 1)
            actor
            { Op = op
              Timestamp = timestamp
              PromptId = promptId
              ResultEnvelope = resultEnvelope }

[<RequireQualifiedAccess>]
type VerificationError =
    /// `record.PreviousHash` does not match `prior.Hash`.
    | PreviousHashMismatch of sequence: int * expected: string * actual: string
    /// `record.Hash` does not match the recomputed hash of its fields.
    | HashMismatch of sequence: int * expected: string * actual: string
    /// Records are not in ascending Sequence order, or have a gap.
    | OutOfOrder of expectedSequence: int * actualSequence: int

/// How much of a loaded segment a **read path** re-verifies (Phase 793).
///
/// This exists so that a fast path is a NAMED, deliberate choice at the seam
/// that takes it, rather than a silent default: "we verify" quietly becoming
/// "we sometimes verify" is the failure this whole phase exists to remove. The
/// sinks all DEFAULT to `Full`; the cheaper modes must be passed explicitly at
/// construction, and each states what it stops proving.
///
/// Cost, and when the cheaper modes are defensible, are documented in
/// `CRYPTO.md` ("Verification on the read paths").
[<RequireQualifiedAccess>]
type LoadVerification =
    /// Recompute the whole loaded segment. The default everywhere.
    | Full
    /// Recompute only the last `records` of the loaded segment, anchored on
    /// that sub-segment's own first `PreviousHash`. Detects corruption WITHIN
    /// the window and nothing before it — appropriate for an append-heavy host
    /// that re-reads a growing stream and has verified the prefix already.
    /// `records <= 0` verifies nothing (equivalent to `Off`).
    | Tail of records: int
    /// Do not verify on load. The caller takes responsibility for integrity —
    /// e.g. it verifies once per process at start-up, or the "store" is a
    /// per-process structure it wrote itself and never re-reads from disk.
    | Off

module Verify =

    /// Map Core's 0-based, segment-relative `ChainBreak` onto the domain's
    /// 1-based absolute `VerificationError`. `offset0` is the Core-basis index
    /// of the segment's first record (0 for a genesis-anchored stream).
    let private ofChainBreak (offset0: int) (b: Fuaran.Core.ChainBreak) : VerificationError =
        let seq1 = b.Index + offset0 + 1

        match b.Reason with
        | "sequence-number mismatch" ->
            // Expected/Got are Core's stringified 0-based segment-relative seqs.
            let parse (s: string) =
                match System.Int32.TryParse s with
                | true, v -> v + offset0 + 1
                | false, _ -> seq1

            VerificationError.OutOfOrder(parse b.Expected, parse b.Got)
        | "prev-hash link broken" -> VerificationError.PreviousHashMismatch(seq1, b.Expected, b.Got)
        | _ -> VerificationError.HashMismatch(seq1, b.Expected, b.Got)

    /// Verify a CONTIGUOUS SEGMENT of a stream against an anchor the caller
    /// already trusts: assert (a) the first record links to
    /// `expectedPreviousHash` and sits at `expectedFirstSequence`, (b) every
    /// subsequent `PreviousHash` links to its predecessor's `Hash`, (c) every
    /// `Hash` recomputes, and (d) the sequence is contiguous ascending. Returns
    /// the first violation, or `Ok ()` on a clean segment. An empty segment is
    /// vacuously `Ok`.
    ///
    /// **Why this exists (Phase 793).** `chain` hardcodes a genesis start, so it
    /// cannot verify anything a real read path returns: a `Replay(from, to)`
    /// slice, a post-checkpoint journal tail, or a store whose prefix was
    /// truncated by compaction all legitimately begin above sequence 1 and would
    /// be reported `OutOfOrder` by a genesis-anchored walker. Two call sites had
    /// already discovered that and each was on its way to a bespoke walker; this
    /// is the one definition, still delegating to Core's `firstChainBreakWith`
    /// (the Phase-411 F14 posture — the domain owns the presentation, never the
    /// walk).
    ///
    /// The mechanism: Core's walker anchors at index 0 / `cfg.Genesis`, so the
    /// segment is re-based onto that origin (`Seq - offset0`) and the payload
    /// binding shifts the real sequence back in — the hash pre-image is always
    /// computed over the record's TRUE sequence, only the contiguity counter
    /// moves.
    let segmentFrom<'Msg>
        (expectedPreviousHash: string)
        (expectedFirstSequence: int)
        (records: OpRecord<'Msg> seq)
        : Result<unit, VerificationError> =
        let recordList = records |> Seq.map StreamEntry.toCoreRecord |> List.ofSeq

        if List.isEmpty recordList then
            Ok()
        else
            // Core's 0-based index the segment's first record should occupy.
            let offset0 = expectedFirstSequence - 1

            let cfg: Fuaran.Core.StreamConfig =
                { Payload = fun seq0 actor op -> Fuaran.Core.OpStream.canonicalConfig.Payload (seq0 + offset0) actor op
                  Genesis = expectedPreviousHash }

            let rebased = recordList |> List.map (fun r -> { r with Seq = r.Seq - offset0 })

            match
                Fuaran.Core.OpStream.firstChainBreakWith
                    cfg
                    StreamEntry.hashFn
                    (StreamEntry.coreWitness<'Msg> ())
                    rebased
            with
            | None -> Ok()
            | Some b -> Error(ofChainBreak offset0 b)

    /// Assert (a) the `PreviousHash` chain links, (b) each `Hash` recomputes to
    /// the stored value, and (c) contiguous 1-based sequence, from genesis.
    /// Returns the first violation, or `Ok ()` on a clean chain.
    ///
    /// The STRICT whole-stream verifier: it additionally proves the stream
    /// starts at sequence 1 with the genesis anchor. A read that returns a slice
    /// or a compacted tail wants `segment` / `segmentFrom` instead — this one
    /// reports `OutOfOrder` on any segment that does not begin at the origin,
    /// which is correct but is not a corruption finding.
    ///
    /// Phase 411 — F14 resolved: the domain's hand-rolled walker is retired.
    /// Records project onto Core's shape (`StreamEntry.toCoreRecord`, `Seq =
    /// Sequence - 1`) and `Core.OpStream.firstChainBreakWith` is the verifier —
    /// one definition of chain integrity across every domain. Only the typed-error
    /// presentation (Core's 0-based `ChainBreak` index → the domain's 1-based
    /// `VerificationError`) stays here.
    let chain<'Msg> (records: OpRecord<'Msg> seq) : Result<unit, VerificationError> =
        segmentFrom HashChain.genesisPreviousHash 1 records

    /// Verify a contiguous segment read from a store when the caller holds NO
    /// external anchor — the shape every sink read path is in. The segment is
    /// checked against its own first record's `PreviousHash` and `Sequence`,
    /// except that a segment beginning at sequence 1 must carry the genesis
    /// anchor (so a whole-stream read is verified exactly as strictly as
    /// `chain`).
    ///
    /// **State what this proves, because it is less than `chain`.** Every record
    /// in the segment is proved internally consistent and correctly linked to
    /// its neighbours — which is precisely the accidental-corruption,
    /// truncation-within-the-segment and reordering detection the unkeyed
    /// SHA-256 genuinely provides. What it does NOT prove for a segment starting
    /// above sequence 1 is the segment's POSITION: its first `PreviousHash` is
    /// taken on trust, so a whole segment lifted from elsewhere in the same
    /// stream is not detected here. A caller that holds the real anchor (a
    /// checkpoint's `PreviousChainHead`, the prior record's `Hash`) should pass
    /// it to `segmentFrom` and get that stronger property. And none of this is
    /// tamper evidence — see `CRYPTO.md`.
    let segment<'Msg> (records: OpRecord<'Msg> seq) : Result<unit, VerificationError> =
        let recordList = List.ofSeq records

        match recordList with
        | [] -> Ok()
        | first :: _ ->
            let anchor =
                if first.Sequence = 1 then
                    HashChain.genesisPreviousHash
                else
                    first.PreviousHash

            segmentFrom anchor first.Sequence recordList

    /// `segment` under a `LoadVerification` mode — the entry point every sink
    /// read path calls, so the mode is honoured identically everywhere.
    let loaded<'Msg> (mode: LoadVerification) (records: OpRecord<'Msg> list) : Result<unit, VerificationError> =
        match mode with
        | LoadVerification.Off -> Ok()
        | LoadVerification.Full -> segment records
        | LoadVerification.Tail n when n <= 0 -> Ok()
        | LoadVerification.Tail n ->
            let length = List.length records

            if n >= length then
                segment records
            else
                segment (List.skip (length - n) records)

    /// Human-readable rendering of a violation — the shape a read path that has
    /// no `Result` channel (the sink interfaces return a bare list) uses to
    /// refuse BY NAME rather than by an opaque throw.
    let describe (streamId: string) (error: VerificationError) : string =
        match error with
        | VerificationError.PreviousHashMismatch(sequence, expected, actual) ->
            sprintf
                "op-stream chain broken in stream '%s' at sequence %d: PreviousHash does not link (expected %s, stored %s)"
                streamId
                sequence
                expected
                actual
        | VerificationError.HashMismatch(sequence, expected, actual) ->
            sprintf
                "op-stream chain broken in stream '%s' at sequence %d: Hash does not recompute (expected %s, stored %s)"
                streamId
                sequence
                expected
                actual
        | VerificationError.OutOfOrder(expectedSequence, actualSequence) ->
            sprintf
                "op-stream chain broken in stream '%s': expected sequence %d, found %d (a gap, a reordering, or a truncation)"
                streamId
                expectedSequence
                actualSequence
