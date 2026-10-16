namespace Fuaran.UI.OpStream.Dag.Abstractions

open Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  DagLift — the verified, one-way lift from a linear op-stream segment to a
//  DAG branch, with a receipt anyone can recompute (Phase 2116).
//
//  The linear chain and the DAG stay two formats (see `docs/DAG_STRATEGY.md`).
//  What a consumer sometimes needs is to take one linear stream and continue it
//  as a DAG branch, either from the DAG's genesis or hung off an existing DAG
//  node (the guest-fork form, whose first record's parent is an ANCHOR such as
//  a host stream's `Mount` op). This module is that route:
//
//    * `ofLinearVerified` / `ofLinearAnchored` verify the linear segment
//      (`Verify.segment`) and REFUSE on a break, then embed it exactly as
//      `DagOpRecord.ofLinear` does (one shared fold). `DagOpRecord.ofLinear`
//      stays, and is the documented UNVERIFIED form.
//    * `LiftReceipt` records what was lifted: the linear range (stream, chain
//      format, sequences, the anchor it hangs off, its head) and the DAG branch
//      it became (stream, head, DAG anchor).
//    * `LiftReceipt.verify` recomputes the lift from the linear records and
//      compares every field, the DAG head included.
//
//  NO CRYPTOGRAPHY IS ADDED. Both hashes are unkeyed content digests. A receipt
//  that recomputes proves that THESE linear records lift to THAT DAG head, and
//  nothing about who wrote either: anyone who can write the records can write a
//  matching receipt. It becomes evidence only in combination with a signed
//  segment attestation over the same linear range (`Attestation.fs`), whose
//  fields the receipt's linear half repeats. See `CRYPTO.md`, "Lift receipts".
//
//  The lift does not re-address anything that exists: the DAG content address
//  is computed exactly as `DagOpRecord.create` always has, and the linear
//  records are read, never written. Fable-clean: the chain walk and the digest
//  are the portable ones the linear tier ships.
// ============================================================================

/// Why a linear segment was not lifted.
[<RequireQualifiedAccess>]
type LiftRefusal =
    /// There is nothing to lift, and a receipt over nothing would claim nothing.
    | EmptySegment
    /// The records belong to more than one stream. The chain pre-image does not
    /// fold the stream id, so the chain walk cannot see this; the receipt names
    /// one linear stream, so the lift refuses it.
    | MixedStreams of streamId: string * otherStreamId: string
    /// The linear segment does not re-walk: a broken link, a hash that does not
    /// recompute, or a gap or reordering in the sequence (`Verify.segment`).
    | BrokenLinearSegment of VerificationError

/// What a lift produced, stated so that it can be recomputed: the linear range
/// it read and the DAG branch it became. The linear half repeats the fields of
/// a segment attestation's `SegmentDescriptor` (stream, chain format, range,
/// previous hash, head), so the two can be compared field by field.
type LiftReceipt =
    {
        /// The linear stream the records were read from.
        LinearStreamId: string
        /// The chain format the linear records hash under
        /// (`StreamEntry.chainFormatVersion`).
        ChainFormatVersion: int
        /// First lifted sequence (1-based, inclusive).
        FromSequence: int
        /// Last lifted sequence (1-based, inclusive).
        ToSequence: int
        /// The `PreviousHash` of the record at `FromSequence`
        /// (`HashChain.genesisPreviousHash` when `FromSequence = 1`).
        LinearPreviousHash: string
        /// The `Hash` of the record at `ToSequence`: the linear head.
        LinearHead: string
        /// The stream the DAG records are keyed under. A lift keeps the linear
        /// stream id, so this equals `LinearStreamId`.
        DagStreamId: string
        /// The content address of the last lifted DAG record: the branch head.
        DagHead: string
        /// The DAG node the first lifted record hangs off (`ofLinearAnchored`),
        /// or `None` when the branch starts at a DAG genesis (`ofLinearVerified`).
        Anchor: string option
    }

/// A lifted branch: the DAG records in linear order (first record first) and
/// the receipt describing them.
type LiftedBranch<'Msg> =
    { Records: DagOpRecord<'Msg> list
      Receipt: LiftReceipt }

/// What a receipt check rested on. It is a recomputation over the supplied
/// records with unkeyed digests, and the case says so.
[<RequireQualifiedAccess>]
type LiftBasis =
    /// The DAG head was recomputed from the supplied linear records, which
    /// re-walked as a chain over the named range. No key and no signature were
    /// involved.
    | RecomputedFromLinearRecords of linearStreamId: string * fromSequence: int * toSequence: int * linearHead: string

/// Where a receipt and its recomputation part.
[<RequireQualifiedAccess>]
type LiftMismatch =
    /// The supplied records could not be lifted at all.
    | LinearRecordsRefused of LiftRefusal
    /// The receipt names a chain format this host does not hash under, so the
    /// records cannot be re-walked under the right pre-image rule.
    | UnsupportedChainFormat of version: int
    /// The records belong to a different linear stream than the receipt names.
    | LinearStreamDiffers of receipt: string * records: string
    /// The records cover a different sequence range than the receipt names.
    | SequenceRangeDiffers of receipt: (int * int) * records: (int * int)
    /// The first record's `PreviousHash` is not the receipt's: the records sit
    /// at a different chain position.
    | LinearPreviousHashDiffers of receipt: string * records: string
    /// The records' head is not the receipt's linear head: these are not the
    /// records the receipt was issued for.
    | LinearHeadDiffers of receipt: string * records: string
    /// The receipt names a DAG stream the lift does not produce.
    | DagStreamDiffers of receipt: string * recomputed: string
    /// The records lift to a different DAG head than the receipt names.
    | DagHeadDiffers of receipt: string * recomputed: string

/// The outcome of `LiftReceipt.verify`. Deliberately not a boolean, and
/// deliberately not worded as attestation: `Recomputes` says the arithmetic
/// agrees and names what it was computed from, nothing more.
[<RequireQualifiedAccess>]
type LiftVerdict =
    /// The receipt recomputes from the supplied records.
    | Recomputes of basis: LiftBasis
    /// The receipt does not recompute from the supplied records.
    | DoesNotRecompute of mismatch: LiftMismatch

/// The verified lift from a linear segment to a DAG branch (Phase 2116).
[<RequireQualifiedAccess>]
module DagLift =

    let private lift<'Msg>
        (anchor: string option)
        (records: OpRecord<'Msg> list)
        : Result<LiftedBranch<'Msg>, LiftRefusal> =
        match records with
        | [] -> Error LiftRefusal.EmptySegment
        | first :: _ ->
            match records |> List.tryFind (fun r -> r.StreamId <> first.StreamId) with
            | Some other -> Error(LiftRefusal.MixedStreams(first.StreamId, other.StreamId))
            | None ->
                match Verify.segment records with
                | Error e -> Error(LiftRefusal.BrokenLinearSegment e)
                | Ok() ->
                    let dag = DagOpRecord.embedLinear (Option.toList anchor) records
                    let last = List.last records

                    Ok
                        { Records = dag
                          Receipt =
                            { LinearStreamId = first.StreamId
                              ChainFormatVersion = StreamEntry.chainFormatVersion
                              FromSequence = first.Sequence
                              ToSequence = last.Sequence
                              LinearPreviousHash = first.PreviousHash
                              LinearHead = last.Hash
                              DagStreamId = first.StreamId
                              DagHead = (List.last dag).Hash
                              Anchor = anchor } }

    /// Verify a contiguous linear segment and lift it to a DAG branch rooted at
    /// a DAG genesis (the first record has no parent). Refuses an empty segment,
    /// one spanning two streams, and one that does not re-walk under
    /// `Verify.segment` (a segment starting at sequence 1 must carry the genesis
    /// anchor). On success the records are exactly `DagOpRecord.ofLinear`'s.
    let ofLinearVerified<'Msg> (records: OpRecord<'Msg> list) : Result<LiftedBranch<'Msg>, LiftRefusal> =
        lift None records

    /// The guest form: verify a contiguous linear segment and lift it to a DAG
    /// branch whose first record's single parent is `anchor`, the content
    /// address of an existing DAG node (a host stream's `Mount` op for a guest
    /// fork). Every later record is a single-parent step. Refuses exactly as
    /// `ofLinearVerified` does.
    let ofLinearAnchored<'Msg>
        (anchor: string)
        (records: OpRecord<'Msg> list)
        : Result<LiftedBranch<'Msg>, LiftRefusal> =
        lift (Some anchor) records

    /// One line naming why a lift was refused, for an exception or a log.
    let describe (refusal: LiftRefusal) : string =
        match refusal with
        | LiftRefusal.EmptySegment -> "nothing to lift: the linear segment is empty"
        | LiftRefusal.MixedStreams(a, b) ->
            sprintf "the linear segment spans two streams ('%s' and '%s'); a lift reads one stream" a b
        | LiftRefusal.BrokenLinearSegment e -> "the linear segment does not re-walk as a chain: " + Verify.describe "" e

/// Recompute a `LiftReceipt` from linear records (Phase 2116).
[<RequireQualifiedAccess>]
module LiftReceipt =

    /// Recompute the lift `receipt` describes from `records` and compare every
    /// field. `Recomputes` means the records re-walk as a chain over the
    /// receipt's range, from the receipt's previous hash to its linear head,
    /// and lift (under the receipt's anchor) to its DAG head. It is an
    /// arithmetic check over unkeyed digests, not a statement about who wrote
    /// the records or the receipt; pair it with a segment attestation over the
    /// same linear range for that (see `CRYPTO.md`, "Lift receipts").
    let verify<'Msg> (receipt: LiftReceipt) (records: OpRecord<'Msg> list) : LiftVerdict =
        let differs = LiftVerdict.DoesNotRecompute

        if receipt.ChainFormatVersion <> StreamEntry.chainFormatVersion then
            differs (LiftMismatch.UnsupportedChainFormat receipt.ChainFormatVersion)
        else
            let recomputed =
                match receipt.Anchor with
                | Some anchor -> DagLift.ofLinearAnchored anchor records
                | None -> DagLift.ofLinearVerified records

            match recomputed with
            | Error refusal -> differs (LiftMismatch.LinearRecordsRefused refusal)
            | Ok lifted ->
                let r = lifted.Receipt

                if r.LinearStreamId <> receipt.LinearStreamId then
                    differs (LiftMismatch.LinearStreamDiffers(receipt.LinearStreamId, r.LinearStreamId))
                elif (r.FromSequence, r.ToSequence) <> (receipt.FromSequence, receipt.ToSequence) then
                    differs (
                        LiftMismatch.SequenceRangeDiffers(
                            (receipt.FromSequence, receipt.ToSequence),
                            (r.FromSequence, r.ToSequence)
                        )
                    )
                elif r.LinearPreviousHash <> receipt.LinearPreviousHash then
                    differs (LiftMismatch.LinearPreviousHashDiffers(receipt.LinearPreviousHash, r.LinearPreviousHash))
                elif r.LinearHead <> receipt.LinearHead then
                    differs (LiftMismatch.LinearHeadDiffers(receipt.LinearHead, r.LinearHead))
                elif r.DagStreamId <> receipt.DagStreamId then
                    differs (LiftMismatch.DagStreamDiffers(receipt.DagStreamId, r.DagStreamId))
                elif r.DagHead <> receipt.DagHead then
                    differs (LiftMismatch.DagHeadDiffers(receipt.DagHead, r.DagHead))
                else
                    LiftVerdict.Recomputes(
                        LiftBasis.RecomputedFromLinearRecords(
                            r.LinearStreamId,
                            r.FromSequence,
                            r.ToSequence,
                            r.LinearHead
                        )
                    )
