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
//  not have — a keyed MAC, or a signature over the chain head. That is the
//  signing seam (Core's `IAttestationSink`, Phase 320), deliberately host-side
//  so the key never enters this portable package; until a host wires it the
//  property here is corruption detection. See `CRYPTO.md`, and keep every
//  description of this chain — in docs, doc comments and error text — inside
//  that boundary.
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

module Verify =

    /// Assert (a) the `PreviousHash` chain links, (b) each `Hash` recomputes to
    /// the stored value, and (c) contiguous 1-based sequence, from genesis.
    /// Returns the first violation, or `Ok ()` on a clean chain.
    ///
    /// Phase 411 — F14 resolved: the domain's hand-rolled walker is retired.
    /// Records project onto Core's shape (`StreamEntry.toCoreRecord`, `Seq =
    /// Sequence - 1`) and `Core.OpStream.firstChainBreakWith chainConfig` is the
    /// verifier — one definition of chain integrity across every domain. Only
    /// the typed-error presentation (Core's 0-based `ChainBreak` index → the
    /// domain's 1-based `VerificationError`) stays here.
    let chain<'Msg> (records: OpRecord<'Msg> seq) : Result<unit, VerificationError> =
        let coreRecords = records |> Seq.map StreamEntry.toCoreRecord |> List.ofSeq

        match
            Fuaran.Core.OpStream.firstChainBreakWith
                HashChain.chainConfig
                StreamEntry.hashFn
                (StreamEntry.coreWitness<'Msg> ())
                coreRecords
        with
        | None -> Ok()
        | Some b ->
            // `b.Index` is Core's 0-based record index; the domain reports 1-based.
            let seq1 = b.Index + 1

            match b.Reason with
            | "sequence-number mismatch" ->
                // Expected/Got are Core's stringified 0-based seqs; re-base to 1-based.
                let parse (s: string) =
                    match System.Int32.TryParse s with
                    | true, v -> v + 1
                    | false, _ -> seq1

                Error(VerificationError.OutOfOrder(parse b.Expected, parse b.Got))
            | "prev-hash link broken" -> Error(VerificationError.PreviousHashMismatch(seq1, b.Expected, b.Got))
            | _ -> Error(VerificationError.HashMismatch(seq1, b.Expected, b.Got))
