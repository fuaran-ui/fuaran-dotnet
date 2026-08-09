namespace Fuaran.UI.OpStream.Replay

open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  Replay — fold an OpRecord sequence through the apply engine.
//
//  Given `initialTree` and a sequence of `OpRecord<'Msg>`, walks records in
//  order applying each via `Fuaran.UI.Ops.Apply.apply` and returns the final
//  tree or the first failure with context.
//
//  Failures during replay are surfaced verbatim — replay is the diagnostic
//  surface for the downstream AI consumer and the AI-emission eval suite
//  "Replay diverged at record N: <ApplyError>" is the
//  diagnostic shape both consumers want.
//
//  ── CHAIN VERIFICATION (Phase 793) ─────────────────────────────────────────
//  Replay DOES verify the hash chain, up front, before the first op reaches the
//  apply engine. It did not until Phase 793, and the note that stood here said
//  so plainly — which made a corrupt store replay silently into a
//  plausible-looking tree, and made the chain a property the code computed on
//  write and no read path ever checked. Evidence that nothing verifies is not
//  evidence.
//
//  What that buys is exactly what an UNKEYED SHA-256 provides — detection of
//  accidental corruption, truncation and reordering of a stored stream. It is
//  NOT tamper evidence: every input to every hash sits in the store beside the
//  hash, so a writer edits a record, re-chains from there, and verification
//  passes. See CRYPTO.md, and do not describe this as more than it is.
//
//  `applyToUnverified` is the escape hatch for a caller that has ALREADY
//  verified the same records (the import path does) or is replaying synthetic
//  records that were never chained. It is deliberately the longer name: the
//  default must be the verified one, and skipping must be visible at the call
//  site.
// ============================================================================

[<RequireQualifiedAccess>]
type ReplayError =
    /// The record at this sequence failed to apply against the in-flight tree.
    | ApplyFailed of sequence: int * applyError: ApplyError
    /// The records did not survive chain verification — a corrupt, truncated or
    /// reordered segment. Carries the first violation, which names the sequence
    /// of the first bad record. Nothing was applied (Phase 793).
    | ChainBroken of verificationError: VerificationError

module Replay =

    /// Apply every record in `records` to `initialTree` in source order,
    /// returning the final tree on success or the first apply failure.
    ///
    /// **Does not verify the hash chain.** Reach for this only when the caller
    /// has already verified these exact records, or when they are synthetic and
    /// were never chained. Otherwise use `applyTo`.
    ///
    /// Records are consumed once; `records` may be lazy.
    let applyToUnverified<'Msg>
        (initialTree: Node<'Msg>)
        (records: OpRecord<'Msg> seq)
        : Result<Node<'Msg>, ReplayError> =
        let mutable tree: Node<'Msg> = initialTree
        let mutable failure: ReplayError option = None

        use enumerator = records.GetEnumerator()
        let mutable stop = false

        while not stop && enumerator.MoveNext() do
            let record = enumerator.Current

            match Apply.apply record.Op tree with
            | Ok updated -> tree <- updated
            | Error applyError ->
                failure <- Some(ReplayError.ApplyFailed(record.Sequence, applyError))
                stop <- true

        match failure with
        | Some err -> Error err
        | None -> Ok tree

    /// Verify the chain, then apply every record to `initialTree` in source
    /// order. Returns the final tree, the first apply failure, or — before any
    /// op is applied — the first chain violation.
    ///
    /// **On a chain break the WHOLE stream is refused; the prefix before the
    /// break is not replayed.** Both that and "replay up to the break" are
    /// defensible, and this is the deliberate choice, for three reasons:
    ///
    ///  1. Replay exists to reconstruct a state a caller will then TRUST and
    ///     act on. A tree folded from the clean prefix of a corrupt store is a
    ///     plausible-looking wrong answer, and a plausible-looking wrong answer
    ///     is worse than a refusal — the caller cannot tell it apart from a
    ///     correct one, and nothing downstream carries the caveat.
    ///  2. "The prefix is clean" is weaker than it sounds. The break is where
    ///     verification FIRST fails, not where corruption begins: a record whose
    ///     `Hash` still recomputes is only proved self-consistent, and a
    ///     truncation shows up as a break at the join, with the deleted records
    ///     leaving no trace at all. Silently returning the prefix would present
    ///     "a shorter history" as "the history".
    ///  3. A caller that genuinely wants the prefix can have it, deliberately and
    ///     visibly: the error names the first bad sequence, so it can slice to
    ///     that boundary and call `applyToUnverified`. That is one line at the
    ///     call site and it is legible in review, which a default that quietly
    ///     truncates is not.
    ///
    /// The records are materialised — verification is a whole-segment walk — so
    /// a lazy `records` is fully enumerated here, unlike in `applyToUnverified`.
    ///
    /// The segment is verified with `Verify.segment`, so a post-checkpoint tail
    /// or a compacted stream that legitimately starts above sequence 1 verifies
    /// against its own anchor rather than being rejected for not starting at
    /// genesis. A caller holding the true anchor should verify with
    /// `Verify.segmentFrom` first and then call `applyToUnverified` — that is
    /// strictly stronger, and it is what `SessionReplay.reconstruct` does.
    let applyTo<'Msg> (initialTree: Node<'Msg>) (records: OpRecord<'Msg> seq) : Result<Node<'Msg>, ReplayError> =
        let recordList = List.ofSeq records

        match Verify.segment recordList with
        | Error verificationError -> Error(ReplayError.ChainBroken verificationError)
        | Ok() -> applyToUnverified initialTree recordList
