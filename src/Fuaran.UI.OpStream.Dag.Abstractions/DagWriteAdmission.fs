namespace Fuaran.UI.OpStream.Dag.Abstractions

// ============================================================================
//  DagWriteAdmission — what a DAG sink checks before it stores a record
//  (Phase 1937), written once and called by both DAG sinks.
//
//  The linear sinks have refused a record whose hash does not recompute since
//  Phase 1525 (`WriteAdmission.Full`); the DAG sinks did not. A record with a
//  wrong `Hash` was stored, refused only when read back — and then by every
//  whole-stream read from that moment on — and a later honest record at the
//  same address was refused as a collision: address squatting. Each sink also
//  carried its own copy of the parent-presence check, with hand-synchronised
//  refusal text.
//
//  Three checks, in this order:
//
//   1. The address is recomputed (`DagVerify.record`): a live record whose
//      `Hash` is not the content address of its own fields is refused, naming
//      both. A tombstoned record's payload was pruned, so its address is taken
//      as it stands — the rule the read path applies.
//   2. Every parent is in the store (store-wide: a guest branch's genesis hangs
//      off a record in the HOST stream).
//   3. An address the stream already holds is an idempotent no-op (the Phase
//      2289 ruling). Check 1 makes the address the content, so a record that
//      passes it at a held address IS the record held there; the content
//      fingerprint that used to decide this is retired.
//
//  The address recomputed is Core's `Dag.nodeId` over the record's fields
//  (`DagOpRecord.recomputeHash`, the cut of Phase 2645). A record carrying an
//  address of any earlier rule fails check 1 and is refused by name; this
//  repository holds no code that reads such an address.
// ============================================================================

/// Why a DAG sink refused to store a record.
[<RequireQualifiedAccess>]
type DagAdmissionRefusal =
    /// The record's `Hash` is not the content address of its own fields.
    | AddressMismatch of stored: string * recomputed: string
    /// A parent the record names is not in the store.
    | MissingParent of parent: string

/// What a DAG sink does with a record the admission let through.
[<RequireQualifiedAccess>]
type DagAdmission =
    /// The address is new to the stream: store the record.
    | Store
    /// The stream already holds this address: store nothing.
    | AlreadyHeld

module DagWriteAdmission =

    /// Admit `record`: its address recomputes, every parent is present
    /// (`parentPresent`, store-wide), and an address the stream already holds
    /// (`addressHeld`) is a no-op.
    let admit<'Msg>
        (parentPresent: string -> bool)
        (addressHeld: string -> bool)
        (record: DagOpRecord<'Msg>)
        : Result<DagAdmission, DagAdmissionRefusal> =
        match DagVerify.record record with
        | Error(DagVerificationError.HashMismatch(stored, recomputed)) ->
            Error(DagAdmissionRefusal.AddressMismatch(stored, recomputed))
        | Error(DagVerificationError.DanglingParent(_, parent)) -> Error(DagAdmissionRefusal.MissingParent parent)
        | Ok() ->
            match record.Parents |> List.tryFind (parentPresent >> not) with
            | Some missing -> Error(DagAdmissionRefusal.MissingParent missing)
            | None when addressHeld record.Hash -> Ok DagAdmission.AlreadyHeld
            | None -> Ok DagAdmission.Store

    /// The refusal as a sink reports it: one wording for both DAG sinks.
    let describe (sinkName: string) (record: DagOpRecord<'Msg>) (refusal: DagAdmissionRefusal) : string =
        let detail =
            match refusal with
            | DagAdmissionRefusal.AddressMismatch(stored, recomputed) ->
                sprintf
                    "content-address check failed: the record's hash does not recompute from its own fields (record carries %s, recomputes to %s)"
                    stored
                    recomputed
            | DagAdmissionRefusal.MissingParent parent ->
                sprintf "parent-presence check failed: parent %s is not in the store" parent

        sprintf
            "%s: refused record %s in stream '%s' — %s. Nothing was written."
            sinkName
            record.Hash
            record.StreamId
            detail
