namespace Fuaran.UI.OpStream.Sqlite

open System
open System.Globalization
open Microsoft.Data.Sqlite
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.SqliteStore

// ============================================================================
//  SqliteSink — Microsoft.Data.Sqlite-backed IOpStreamSink<'Msg>.
//
//  Schema (op_stream, single table, composite PK):
//
//      CREATE TABLE IF NOT EXISTS op_stream (
//          stream_id            TEXT    NOT NULL,
//          sequence             INTEGER NOT NULL,
//          previous_hash        TEXT    NOT NULL,
//          hash                 TEXT    NOT NULL,
//          op_json              TEXT    NOT NULL,
//          prompt_id            TEXT    NULL,
//          user_id              TEXT    NOT NULL,
//          timestamp            INTEGER NOT NULL,
//          result_envelope_json TEXT    NOT NULL,
//          PRIMARY KEY (stream_id, sequence)
//      );
//
//  Companion `op_checkpoint` table:
//
//      CREATE TABLE IF NOT EXISTS op_checkpoint (
//          stream_id            TEXT    NOT NULL,
//          sequence             INTEGER NOT NULL,
//          previous_chain_head  TEXT    NOT NULL,
//          snapshot_hash        TEXT    NOT NULL,
//          snapshot_json        TEXT    NOT NULL,
//          timestamp            INTEGER NOT NULL,
//          PRIMARY KEY (stream_id, sequence)
//      );
//
//  Op JSON serialisation goes through the host-provided IOpJsonCodec<'Msg>;
//  snapshot JSON serialisation goes through the host-provided
//  INodeJsonCodec<'Msg> — closure-bearing typed nodes can't
//  round-trip generically. Hosts that need hash-chain integrity only can
//  pass `NodeJsonCodec.encodeOnly` and accept that
//  LatestCheckpointAtOrBefore will surface decoder errors. Result-envelope
//  serialisation stays owned by this sink (closed shape, no host codec).
//
//  ── VERIFY ON LOAD (Phase 793) ─────────────────────────────────────────────
//  `Replay` re-verifies the hash chain of the rows it just read, rather than
//  trusting them because this sink wrote them. This is the sink whose store
//  outlives the process and is a file on a disk any other program can touch, so
//  it is the one where "the chain was computed on write and never checked"
//  actually costs something: a truncated write, a bad sector or a hand-run
//  `UPDATE` all replay silently without it. The chain is UNKEYED, so what is
//  detected is accidental corruption, truncation and reordering — not a writer
//  who edits a row and re-chains from there (CRYPTO.md).
//
//  Cost is linear in rows returned and is measured in CRYPTO.md; a host that
//  has read that and wants a cheaper posture passes an explicit
//  `LoadVerification` — the default is `Full`.
// ============================================================================

type SqliteSink<'Msg>
    (
        connectionString: string,
        codec: IOpJsonCodec<'Msg>,
        nodeCodec: INodeJsonCodec<'Msg>,
        loadVerification: LoadVerification,
        writeAdmission: WriteAdmission
    ) =

    let openConnection () : SqliteConnection =
        SqliteConnections.openConnection connectionString

    let ensureSchema () =
        use conn = openConnection ()

        SqliteConnections.useWriteAheadLog conn

        use cmd = conn.CreateCommand()

        cmd.CommandText <-
            """CREATE TABLE IF NOT EXISTS op_stream (
    stream_id            TEXT    NOT NULL,
    sequence             INTEGER NOT NULL,
    previous_hash        TEXT    NOT NULL,
    hash                 TEXT    NOT NULL,
    op_json              TEXT    NOT NULL,
    prompt_id            TEXT    NULL,
    user_id              TEXT    NOT NULL,
    timestamp            INTEGER NOT NULL,
    result_envelope_json TEXT    NOT NULL,
    PRIMARY KEY (stream_id, sequence)
);
CREATE TABLE IF NOT EXISTS op_checkpoint (
    stream_id            TEXT    NOT NULL,
    sequence             INTEGER NOT NULL,
    previous_chain_head  TEXT    NOT NULL,
    snapshot_hash        TEXT    NOT NULL,
    snapshot_json        TEXT    NOT NULL,
    timestamp            INTEGER NOT NULL,
    PRIMARY KEY (stream_id, sequence)
);
CREATE TABLE IF NOT EXISTS op_invocation (
    stream_id            TEXT    NOT NULL,
    invocation_key       TEXT    NOT NULL,
    sequence             INTEGER NOT NULL,
    hash                 TEXT    NOT NULL,
    PRIMARY KEY (stream_id, invocation_key)
);"""

        cmd.ExecuteNonQuery() |> ignore

    do ensureSchema ()

    /// Verify a segment about to be handed to a caller. `IOpStreamSink` has no
    /// error channel — `Replay` returns a bare list — so a broken chain is
    /// refused the way this sink already refuses an undecodable row: by name,
    /// with a message that says which stream and which record.
    let verifyLoaded (streamId: string) (records: OpRecord<'Msg> list) : OpRecord<'Msg> list =
        match Verify.loaded loadVerification records with
        | Ok() -> records
        | Error e -> invalidOp ("SqliteSink: " + Verify.describe streamId e)

    /// The boundary of a stream — `(latest sequence, head hash)` — on the
    /// caller's connection and transaction, so a compare and the append after it
    /// are one atomic read-then-write rather than two races. While the stream
    /// holds rows it is the row at the highest sequence. Once compaction has
    /// truncated every row, it is READ FROM THE SNAPSHOT the truncation kept —
    /// the checkpoint at the boundary, whose `previous_chain_head` is the hash of
    /// the record it replaced (Phase 1937, Core's `Compacted` rule) — and never
    /// reset to genesis; with neither, the stream is empty: `(0, genesis)`.
    let boundaryOn (conn: SqliteConnection) (tx: SqliteTransaction option) (streamId: string) : int * string =
        let read (sql: string) : (int * string) option =
            use cmd = conn.CreateCommand()

            match tx with
            | Some t -> cmd.Transaction <- t
            | None -> ()

            cmd.CommandText <- sql
            cmd.Parameters.AddWithValue("@stream_id", streamId) |> ignore
            use reader = cmd.ExecuteReader()

            if reader.Read() then
                Some(reader.GetInt32 0, reader.GetString 1)
            else
                None

        match
            read "SELECT sequence, hash FROM op_stream WHERE stream_id = @stream_id ORDER BY sequence DESC LIMIT 1;"
        with
        | Some boundary -> boundary
        | None ->
            read
                "SELECT sequence, previous_chain_head FROM op_checkpoint WHERE stream_id = @stream_id ORDER BY sequence DESC LIMIT 1;"
            |> Option.defaultValue (0, HashChain.genesisPreviousHash)

    /// The chain head as `IOpStreamCasSink.Head` reports it: the boundary's hash.
    let headOn (conn: SqliteConnection) (tx: SqliteTransaction option) (streamId: string) : string =
        snd (boundaryOn conn tx streamId)

    /// The one INSERT into `op_stream`. Callers own the connection, and pass a
    /// transaction where a second statement has to land or not land with it
    /// (Phase 1485's keyed append). Extracted rather than duplicated so the
    /// column list, the typed-actor encoding, the ADMISSION CHECK and the
    /// SQLITE_CONSTRAINT translation cannot drift between the append paths.
    let insertRecordOn
        (conn: SqliteConnection)
        (tx: SqliteTransaction option)
        (record: OpRecord<'Msg>)
        : AppendReceipt =
        // ── ADMISSION (Phase 1525, finding H-16) ────────────────────────────
        // Read on the CALLER'S connection and transaction, so inside an
        // immediate transaction the head this checks against is the head the
        // insert below extends. Before this, a record whose `Hash` did not
        // recompute or whose `PreviousHash` named nothing was written happily
        // and then made every subsequent `Replay` of the segment throw — one
        // bad write poisoning a stream for the rest of its life, in a store
        // that outlives the process.
        let latest, head = boundaryOn conn tx record.StreamId

        match Verify.admission writeAdmission head latest record with
        | Error e -> invalidOp (Verify.describeAdmission "SqliteSink" record.StreamId e)
        | Ok() -> ()

        use cmd = conn.CreateCommand()

        match tx with
        | Some t -> cmd.Transaction <- t
        | None -> ()

        cmd.CommandText <-
            """INSERT INTO op_stream
    (stream_id, sequence, previous_hash, hash, op_json, prompt_id, user_id, timestamp, result_envelope_json)
VALUES
    (@stream_id, @sequence, @previous_hash, @hash, @op_json, @prompt_id, @user_id, @timestamp, @result_envelope_json);"""

        cmd.Parameters.AddWithValue("@stream_id", record.StreamId) |> ignore
        cmd.Parameters.AddWithValue("@sequence", record.Sequence) |> ignore
        cmd.Parameters.AddWithValue("@previous_hash", record.PreviousHash) |> ignore
        cmd.Parameters.AddWithValue("@hash", record.Hash) |> ignore
        cmd.Parameters.AddWithValue("@op_json", codec.EncodeOp record.Op) |> ignore

        let promptIdValue: obj =
            match record.PromptId with
            | Some s -> upcast s
            | None -> upcast DBNull.Value

        cmd.Parameters.AddWithValue("@prompt_id", promptIdValue) |> ignore
        // Phase 320 — the `user_id` column now holds the canonical typed-actor
        // JSON (`Actor.encode`); pre-320 rows held a bare user-id string and read
        // back as `Human` via the `ofLegacyString` fallback below.
        cmd.Parameters.AddWithValue("@user_id", Actor.encode record.Actor) |> ignore

        cmd.Parameters.AddWithValue("@timestamp", record.Timestamp.ToUnixTimeSeconds())
        |> ignore

        cmd.Parameters.AddWithValue("@result_envelope_json", ResultEnvelopeJson.encode record.ResultEnvelope)
        |> ignore

        try
            cmd.ExecuteNonQuery() |> ignore
        with :? SqliteException as ex when ex.SqliteErrorCode = 19 ->
            // SQLITE_CONSTRAINT — duplicate (stream_id, sequence) PK.
            invalidOp (
                sprintf
                    "SqliteSink: duplicate (StreamId=%s, Sequence=%d) — sinks reject overwrites."
                    record.StreamId
                    record.Sequence
            )

        { StreamId = record.StreamId
          Sequence = record.Sequence
          Hash = record.Hash }

    /// The receipt a previous append under `invocationKey` produced, or `None`
    /// if the key is unseen on this stream. Reads the `op_invocation` side
    /// table rather than a column on `op_stream`, which is what lets an
    /// existing database gain the contract by opening it: `ensureSchema` adds
    /// the table, and no row of the op log is rewritten or re-read.
    let invocationReceiptOn
        (conn: SqliteConnection)
        (tx: SqliteTransaction option)
        (streamId: string)
        (invocationKey: string)
        : AppendReceipt option =
        use cmd = conn.CreateCommand()

        match tx with
        | Some t -> cmd.Transaction <- t
        | None -> ()

        cmd.CommandText <-
            "SELECT sequence, hash FROM op_invocation WHERE stream_id = @stream_id AND invocation_key = @key;"

        cmd.Parameters.AddWithValue("@stream_id", streamId) |> ignore
        cmd.Parameters.AddWithValue("@key", invocationKey) |> ignore
        use reader = cmd.ExecuteReader()

        if reader.Read() then
            Some
                { StreamId = streamId
                  Sequence = reader.GetInt32(0)
                  Hash = reader.GetString(1) }
        else
            None

    /// Claim `invocationKey` for the record just inserted. The
    /// `(stream_id, invocation_key)` primary key is the unique index that makes
    /// the claim one-writer; the SELECT above is only a fast path, and a
    /// concurrent writer that took the key between the two is refused HERE.
    let claimInvocationOn
        (conn: SqliteConnection)
        (tx: SqliteTransaction)
        (streamId: string)
        (invocationKey: string)
        (receipt: AppendReceipt)
        : unit =
        use cmd = conn.CreateCommand()
        cmd.Transaction <- tx

        cmd.CommandText <-
            """INSERT INTO op_invocation (stream_id, invocation_key, sequence, hash)
VALUES (@stream_id, @key, @sequence, @hash);"""

        cmd.Parameters.AddWithValue("@stream_id", streamId) |> ignore
        cmd.Parameters.AddWithValue("@key", invocationKey) |> ignore
        cmd.Parameters.AddWithValue("@sequence", receipt.Sequence) |> ignore
        cmd.Parameters.AddWithValue("@hash", receipt.Hash) |> ignore

        try
            cmd.ExecuteNonQuery() |> ignore
        with :? SqliteException as ex when ex.SqliteErrorCode = 19 ->
            invalidOp (
                sprintf
                    "SqliteSink: invocation key (StreamId=%s, Key=%s) was taken concurrently — retry to read the winner's receipt."
                    streamId
                    invocationKey
            )

    /// The sequence of the stream's head row, and whether a checkpoint sits at
    /// it — what a truncation must know before it may remove that row.
    let headRowOn (conn: SqliteConnection) (tx: SqliteTransaction) (streamId: string) : (int * bool) option =
        use cmd = conn.CreateCommand()
        cmd.Transaction <- tx

        cmd.CommandText <-
            """SELECT s.sequence,
       EXISTS (SELECT 1 FROM op_checkpoint c WHERE c.stream_id = s.stream_id AND c.sequence = s.sequence)
FROM op_stream s
WHERE s.stream_id = @stream_id
ORDER BY s.sequence DESC
LIMIT 1;"""

        cmd.Parameters.AddWithValue("@stream_id", streamId) |> ignore
        use reader = cmd.ExecuteReader()

        if reader.Read() then
            Some(reader.GetInt32 0, reader.GetInt64 1 <> 0L)
        else
            None

    /// Delete the ops at or below `throughSequence`, on the caller's transaction.
    ///
    /// **The boundary survives (Phase 1937, D30).** Removing a prefix never moves
    /// the head: once the truncation reaches the head row, the boundary is read
    /// from the checkpoint at the head (`boundaryOn`), so a truncation that would
    /// leave no row AND no checkpoint at the head is refused rather than
    /// resetting the stream to genesis. And the `op_invocation` rows of the
    /// removed records are KEPT: a retried `AppendKeyed` whose first attempt
    /// landed below the boundary answers `Duplicate` with the receipt that attempt
    /// produced, rather than appending the op a second time (Core's
    /// `Compacted.Keys`; this reverses Phase 1525 L-A15's deletion of them).
    let truncateOpsOn (conn: SqliteConnection) (tx: SqliteTransaction) (streamId: string) (throughSequence: int) : int =
        match headRowOn conn tx streamId with
        | Some(head, false) when throughSequence >= head ->
            invalidOp (
                sprintf
                    "SqliteSink: refused to truncate stream '%s' through %d — that removes the head record (sequence %d), and no checkpoint at the head holds the boundary. Take a checkpoint at the head first. Nothing was removed."
                    streamId
                    throughSequence
                    head
            )
        | _ -> ()

        use cmd = conn.CreateCommand()
        cmd.Transaction <- tx
        cmd.CommandText <- "DELETE FROM op_stream WHERE stream_id = @stream_id AND sequence <= @through;"
        cmd.Parameters.AddWithValue("@stream_id", streamId) |> ignore
        cmd.Parameters.AddWithValue("@through", throughSequence) |> ignore
        cmd.ExecuteNonQuery()

    /// Delete the checkpoints below `beforeSequence`, on the caller's
    /// transaction. While the stream holds no rows, the checkpoint at its
    /// boundary is the only thing that says where it ends (Phase 1937), so a call
    /// that would remove it is refused.
    let truncateCheckpointsOn
        (conn: SqliteConnection)
        (tx: SqliteTransaction)
        (streamId: string)
        (beforeSequence: int)
        : int =
        match headRowOn conn tx streamId with
        | None ->
            let latest, _ = boundaryOn conn (Some tx) streamId

            if latest >= 1 && latest < beforeSequence then
                invalidOp (
                    sprintf
                        "SqliteSink: refused to drop the checkpoints of stream '%s' before %d — the stream holds no records, and the checkpoint at %d is the boundary its head is read from. Nothing was removed."
                        streamId
                        beforeSequence
                        latest
                )
        | Some _ -> ()

        use cmd = conn.CreateCommand()
        cmd.Transaction <- tx
        cmd.CommandText <- "DELETE FROM op_checkpoint WHERE stream_id = @stream_id AND sequence < @before;"
        cmd.Parameters.AddWithValue("@stream_id", streamId) |> ignore
        cmd.Parameters.AddWithValue("@before", beforeSequence) |> ignore
        cmd.ExecuteNonQuery()

    /// Four-arg constructor — read-path mode named, write admission left at its
    /// `Full` default (Phase 1525). The pre-1525 primary constructor, kept by
    /// name so every existing call site compiles unchanged.
    new
        (
            connectionString: string,
            codec: IOpJsonCodec<'Msg>,
            nodeCodec: INodeJsonCodec<'Msg>,
            loadVerification: LoadVerification
        ) =
        SqliteSink<'Msg>(connectionString, codec, nodeCodec, loadVerification, WriteAdmission.Full)

    /// Three-arg constructor — verifies the whole loaded segment on `Replay`
    /// and checks every offered record on the write path.
    new(connectionString: string, codec: IOpJsonCodec<'Msg>, nodeCodec: INodeJsonCodec<'Msg>) =
        SqliteSink<'Msg>(connectionString, codec, nodeCodec, LoadVerification.Full, WriteAdmission.Full)

    /// Legacy two-arg constructor, kept for callers that don't
    /// need checkpoint snapshot round-trip (hash-chain verification only).
    /// Defaults the node codec to `NodeJsonCodec.encodeOnly`; AppendCheckpoint
    /// works (the encoder is purely additive) but LatestCheckpointAtOrBefore
    /// will return a decoder error if a checkpoint exists.
    new(connectionString: string, codec: IOpJsonCodec<'Msg>) =
        SqliteSink<'Msg>(
            connectionString,
            codec,
            NodeJsonCodec.encodeOnly<'Msg> (),
            LoadVerification.Full,
            WriteAdmission.Full
        )

    // The BASE interface is implemented explicitly rather than through the
    // checkpoint extension's block, because Phase 1485 gives this type three
    // interfaces that all inherit it and F# then requires the shared base to be
    // implemented once, by name (FS0363). Nothing about the four members moved.
    interface IOpStreamSink<'Msg> with

        member _.Append(record: OpRecord<'Msg>) : Async<unit> =
            async {
                use conn = openConnection ()
                insertRecordOn conn None record |> ignore
            }

        member _.Replay(streamId: string, fromSequence: int, toSequence: int) : Async<OpRecord<'Msg> list> =
            async {
                use conn = openConnection ()
                use cmd = conn.CreateCommand()

                cmd.CommandText <-
                    """SELECT stream_id, sequence, previous_hash, hash, op_json, prompt_id, user_id, timestamp, result_envelope_json
FROM op_stream
WHERE stream_id = @stream_id AND sequence BETWEEN @from AND @to
ORDER BY sequence;"""

                cmd.Parameters.AddWithValue("@stream_id", streamId) |> ignore
                cmd.Parameters.AddWithValue("@from", fromSequence) |> ignore
                cmd.Parameters.AddWithValue("@to", toSequence) |> ignore

                use reader = cmd.ExecuteReader()
                let results = ResizeArray<OpRecord<'Msg>>()

                while reader.Read() do
                    let opJson = reader.GetString(4)

                    match codec.DecodeOp opJson with
                    | Error error ->
                        raise (
                            CodecDecodeFailed(
                                sprintf
                                    "SqliteSink: codec failed to decode op at (StreamId=%s, Sequence=%d)"
                                    (reader.GetString(0))
                                    (reader.GetInt32(1)),
                                error
                            )
                        )
                    | Ok op ->
                        let envelopeJson = reader.GetString(8)

                        match ResultEnvelopeJson.decode envelopeJson with
                        | Error msg ->
                            failwithf
                                "SqliteSink: failed to decode result envelope at (StreamId=%s, Sequence=%d): %s"
                                (reader.GetString(0))
                                (reader.GetInt32(1))
                                msg
                        | Ok envelope ->
                            let promptId =
                                if reader.IsDBNull(5) then
                                    None
                                else
                                    Some(reader.GetString(5))

                            let actorRaw = reader.GetString(6)

                            let actor =
                                Actor.tryDecode actorRaw |> Option.defaultValue (Actor.ofLegacyString actorRaw)

                            let record =
                                { StreamId = reader.GetString(0)
                                  Sequence = reader.GetInt32(1)
                                  PreviousHash = reader.GetString(2)
                                  Hash = reader.GetString(3)
                                  Op = op
                                  PromptId = promptId
                                  Actor = actor
                                  Timestamp = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(7))
                                  ResultEnvelope = envelope }

                            results.Add record

                return verifyLoaded streamId (List.ofSeq results)
            }

        member _.LatestSequence(streamId: string) : Async<int> =
            async {
                use conn = openConnection ()
                return fst (boundaryOn conn None streamId)
            }

        member _.Streams() : Async<string list> =
            async {
                use conn = openConnection ()
                use cmd = conn.CreateCommand()
                // A stream compacted through its head holds no row and is still a
                // stream: its boundary checkpoint is what lists it (Phase 1937).
                cmd.CommandText <-
                    "SELECT stream_id FROM op_stream UNION SELECT stream_id FROM op_checkpoint WHERE sequence >= 1;"

                use reader = cmd.ExecuteReader()
                let results = ResizeArray<string>()

                while reader.Read() do
                    results.Add(reader.GetString(0))

                return List.ofSeq results
            }

    interface IOpStreamCheckpointSink<'Msg> with

        member _.AppendCheckpoint(checkpoint: Checkpoint<'Msg>) : Async<unit> =
            async {
                use conn = openConnection ()
                use cmd = conn.CreateCommand()

                cmd.CommandText <-
                    """INSERT INTO op_checkpoint
    (stream_id, sequence, previous_chain_head, snapshot_hash, snapshot_json, timestamp)
VALUES
    (@stream_id, @sequence, @previous_chain_head, @snapshot_hash, @snapshot_json, @timestamp);"""

                cmd.Parameters.AddWithValue("@stream_id", checkpoint.StreamId) |> ignore
                cmd.Parameters.AddWithValue("@sequence", checkpoint.Sequence) |> ignore

                cmd.Parameters.AddWithValue("@previous_chain_head", checkpoint.PreviousChainHead)
                |> ignore

                cmd.Parameters.AddWithValue("@snapshot_hash", checkpoint.SnapshotHash) |> ignore

                cmd.Parameters.AddWithValue("@snapshot_json", nodeCodec.EncodeNode checkpoint.Snapshot)
                |> ignore

                cmd.Parameters.AddWithValue("@timestamp", checkpoint.Timestamp.ToUnixTimeSeconds())
                |> ignore

                try
                    cmd.ExecuteNonQuery() |> ignore
                with :? SqliteException as ex when ex.SqliteErrorCode = 19 ->
                    invalidOp (
                        sprintf
                            "SqliteSink: duplicate checkpoint (StreamId=%s, Sequence=%d) — sinks reject overwrites."
                            checkpoint.StreamId
                            checkpoint.Sequence
                    )
            }

        member _.LatestCheckpointAtOrBefore(streamId: string, upToSequence: int) : Async<Checkpoint<'Msg> option> =
            async {
                use conn = openConnection ()
                use cmd = conn.CreateCommand()

                cmd.CommandText <-
                    """SELECT stream_id, sequence, previous_chain_head, snapshot_hash, snapshot_json, timestamp
FROM op_checkpoint
WHERE stream_id = @stream_id AND sequence <= @upto
ORDER BY sequence DESC
LIMIT 1;"""

                cmd.Parameters.AddWithValue("@stream_id", streamId) |> ignore
                cmd.Parameters.AddWithValue("@upto", upToSequence) |> ignore

                use reader = cmd.ExecuteReader()

                if not (reader.Read()) then
                    return None
                else
                    let snapshotJson = reader.GetString(4)

                    match nodeCodec.DecodeNode snapshotJson with
                    | Error error ->
                        return
                            raise (
                                CodecDecodeFailed(
                                    sprintf
                                        "SqliteSink: node codec failed to decode snapshot at (StreamId=%s, Sequence=%d)"
                                        (reader.GetString(0))
                                        (reader.GetInt32(1)),
                                    error
                                )
                            )
                    | Ok snapshot ->
                        return
                            Some
                                { StreamId = reader.GetString(0)
                                  Sequence = reader.GetInt32(1)
                                  PreviousChainHead = reader.GetString(2)
                                  SnapshotHash = reader.GetString(3)
                                  Snapshot = snapshot
                                  Timestamp = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(5)) }
            }

        member _.ListCheckpoints(streamId: string) : Async<Checkpoint<'Msg> list> =
            async {
                use conn = openConnection ()
                use cmd = conn.CreateCommand()

                cmd.CommandText <-
                    """SELECT stream_id, sequence, previous_chain_head, snapshot_hash, snapshot_json, timestamp
FROM op_checkpoint
WHERE stream_id = @stream_id
ORDER BY sequence;"""

                cmd.Parameters.AddWithValue("@stream_id", streamId) |> ignore

                use reader = cmd.ExecuteReader()
                let results = ResizeArray<Checkpoint<'Msg>>()

                while reader.Read() do
                    let snapshotJson = reader.GetString(4)

                    match nodeCodec.DecodeNode snapshotJson with
                    | Error error ->
                        raise (
                            CodecDecodeFailed(
                                sprintf
                                    "SqliteSink: node codec failed to decode snapshot at (StreamId=%s, Sequence=%d)"
                                    (reader.GetString(0))
                                    (reader.GetInt32(1)),
                                error
                            )
                        )
                    | Ok snapshot ->
                        results.Add
                            { StreamId = reader.GetString(0)
                              Sequence = reader.GetInt32(1)
                              PreviousChainHead = reader.GetString(2)
                              SnapshotHash = reader.GetString(3)
                              Snapshot = snapshot
                              Timestamp = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(5)) }

                return List.ofSeq results
            }

        member _.TruncateOpsThrough(streamId: string, throughSequence: int) : Async<int> =
            async {
                use conn = openConnection ()
                use tx = conn.BeginTransaction(deferred = false)
                let removed = truncateOpsOn conn tx streamId throughSequence
                tx.Commit()
                return removed
            }

        member _.TruncateCheckpointsBefore(streamId: string, beforeSequence: int) : Async<int> =
            async {
                use conn = openConnection ()
                use tx = conn.BeginTransaction(deferred = false)
                let removed = truncateCheckpointsOn conn tx streamId beforeSequence
                tx.Commit()
                return removed
            }

    // ── Phase 1485: the two contracts a durable port owes a consumer ────────
    // Both wrap their read-then-write in an explicit transaction. Without one,
    // the head read and the insert are two statements a concurrent writer can
    // interleave, which is a slower race rather than a compare-and-append —
    // and the keyed path's two inserts could land half-applied, leaving a
    // record no key names or a key naming no record.

    interface IOpStreamCasSink<'Msg> with

        member _.Head(streamId: string) : Async<string> =
            async {
                use conn = openConnection ()
                return headOn conn None streamId
            }

        member _.AppendIf(record: OpRecord<'Msg>, expectedHead: string) : Async<CasAppendOutcome> =
            async {
                try
                    use conn = openConnection ()
                    // Phase 1525 (finding M-C1) — IMMEDIATE, not the default
                    // DEFERRED. A deferred transaction takes no write lock until
                    // its first WRITE, so the head SELECT below ran under a read
                    // lock: two writers could both read the same head, both find
                    // it current, and serialise only at the insert — where the
                    // loser got a duplicate-sequence constraint violation
                    // (a throw) instead of the `StaleHead` this method exists to
                    // return. `BEGIN IMMEDIATE` takes the write lock AT the
                    // SELECT, which is what makes the compare and the append one
                    // act rather than two.
                    use tx = conn.BeginTransaction(deferred = false)
                    let actual = headOn conn (Some tx) record.StreamId

                    if actual <> expectedHead then
                        // Nothing was written, so the rollback is what makes "the
                        // stream is untouched" true rather than merely intended.
                        tx.Rollback()
                        return CasAppendOutcome.StaleHead(expectedHead, actual)
                    else
                        let receipt = insertRecordOn conn (Some tx) record
                        tx.Commit()
                        return CasAppendOutcome.Appended receipt
                with :? SqliteException as ex when SqliteConnections.isContention ex ->
                    // SQLITE_BUSY (or SQLITE_LOCKED) — another writer held the write lock for longer
                    // than `busy_timeout`. NOTHING was written (the immediate
                    // transaction never started), so this is a contention outcome
                    // and not a failure: report it as the value the caller's
                    // retry loop already knows how to handle, naming the head as
                    // it can now be observed. If the other writer did commit, the
                    // head has moved and the caller rebuilds against it; if it
                    // rolled back, the head is unchanged and the caller retries
                    // the same record — which is exactly what should happen.
                    // Reporting a throw here instead would make a moment of
                    // ordinary contention indistinguishable from a broken store.
                    use conn = openConnection ()
                    return CasAppendOutcome.StaleHead(expectedHead, headOn conn None record.StreamId)
            }

    interface IOpStreamKeyedSink<'Msg> with

        member _.AppendKeyed(record: OpRecord<'Msg>, invocationKey: string) : Async<KeyedAppendOutcome> =
            async {
                use conn = openConnection ()
                // IMMEDIATE for the same reason as `AppendIf` (Phase 1525): the
                // key SELECT and the two INSERTs are a read-then-write, and under
                // a deferred transaction two callers racing one key both read
                // "unseen" before either writes.
                use tx = conn.BeginTransaction(deferred = false)

                match invocationReceiptOn conn (Some tx) record.StreamId invocationKey with
                | Some receipt ->
                    // The retry contract: the FIRST receipt, unchanged, and nothing
                    // written. The second call's `record` is not consulted at all.
                    tx.Rollback()
                    return KeyedAppendOutcome.Duplicate receipt
                | None ->
                    let receipt = insertRecordOn conn (Some tx) record
                    claimInvocationOn conn tx record.StreamId invocationKey receipt
                    tx.Commit()
                    return KeyedAppendOutcome.Appended receipt
            }

    // ── Phase 1525: the atomic batch append + the atomic retention step ─────

    interface IOpStreamBatchSink<'Msg> with

        member _.AppendAll(records: OpRecord<'Msg> list) : Async<unit> =
            async {
                if not (List.isEmpty records) then
                    use conn = openConnection ()
                    use tx = conn.BeginTransaction(deferred = false)

                    // Each record meets the SAME admission check a single append
                    // meets, in order, against the head the previous insert in
                    // this transaction just produced — so a bundle that is not a
                    // contiguous chain is refused rather than half-written. The
                    // transaction is never committed on that path, so the stream
                    // is left exactly as it was.
                    for record in records do
                        insertRecordOn conn (Some tx) record |> ignore

                    tx.Commit()
            }

    interface IOpStreamCompactSink<'Msg> with

        member _.Compact(streamId: string, throughSequence: int) : Async<int * int> =
            async {
                use conn = openConnection ()
                use tx = conn.BeginTransaction(deferred = false)
                // ONE transaction over both deletions. As two calls there is a
                // window in which the ops are gone and the checkpoints that
                // justified removing them are not — a reader inside it sees a
                // stream whose surviving records begin above a checkpoint that
                // still claims to cover them.
                let ops = truncateOpsOn conn tx streamId throughSequence
                let cps = truncateCheckpointsOn conn tx streamId throughSequence
                tx.Commit()
                return ops, cps
            }

module SqliteSink =
    /// Convenience factory returning a fresh sink as the abstraction interface.
    /// The underlying instance always implements `IOpStreamCheckpointSink<'Msg>`
    /// — pass a real `INodeJsonCodec<'Msg>` via `createWithCheckpoints` if
    /// checkpoint snapshot round-trip is required.
    let create<'Msg> (connectionString: string) (codec: IOpJsonCodec<'Msg>) : IOpStreamSink<'Msg> =
        upcast SqliteSink<'Msg>(connectionString, codec)

    /// `create` under an explicit read-path verification mode. Naming a cheaper
    /// mode here is the ONLY way to get one — there is no silent fast path.
    let createWith<'Msg>
        (loadVerification: LoadVerification)
        (connectionString: string)
        (codec: IOpJsonCodec<'Msg>)
        : IOpStreamSink<'Msg> =
        upcast SqliteSink<'Msg>(connectionString, codec, NodeJsonCodec.encodeOnly<'Msg> (), loadVerification)

    /// Convenience factory returning the checkpoint-aware sink
    /// interface. Requires a real `INodeJsonCodec<'Msg>` for snapshot
    /// round-trip; hosts that only need integrity-verification of
    /// checkpoints can pass `NodeJsonCodec.encodeOnly`.
    let createWithCheckpoints<'Msg>
        (connectionString: string)
        (codec: IOpJsonCodec<'Msg>)
        (nodeCodec: INodeJsonCodec<'Msg>)
        : IOpStreamCheckpointSink<'Msg> =
        upcast SqliteSink<'Msg>(connectionString, codec, nodeCodec)

    /// `createWithCheckpoints` under an explicit read-path verification mode.
    let createWithCheckpointsAnd<'Msg>
        (loadVerification: LoadVerification)
        (connectionString: string)
        (codec: IOpJsonCodec<'Msg>)
        (nodeCodec: INodeJsonCodec<'Msg>)
        : IOpStreamCheckpointSink<'Msg> =
        upcast SqliteSink<'Msg>(connectionString, codec, nodeCodec, loadVerification)

    /// `create` under explicit read-path AND write-path modes (Phase 1525).
    /// `WriteAdmission.Off` is what a caller deliberately writing a record the
    /// admission checks would refuse — a corruption fixture, a store being
    /// rebuilt out of order and verified afterwards — must name. There is no
    /// silent way to reach it.
    let createWithModes<'Msg>
        (loadVerification: LoadVerification)
        (writeAdmission: WriteAdmission)
        (connectionString: string)
        (codec: IOpJsonCodec<'Msg>)
        : IOpStreamSink<'Msg> =
        upcast
            SqliteSink<'Msg>(
                connectionString,
                codec,
                NodeJsonCodec.encodeOnly<'Msg> (),
                loadVerification,
                writeAdmission
            )

    /// `createWithCheckpoints` under explicit read-path AND write-path modes.
    let createWithCheckpointsAndModes<'Msg>
        (loadVerification: LoadVerification)
        (writeAdmission: WriteAdmission)
        (connectionString: string)
        (codec: IOpJsonCodec<'Msg>)
        (nodeCodec: INodeJsonCodec<'Msg>)
        : IOpStreamCheckpointSink<'Msg> =
        upcast SqliteSink<'Msg>(connectionString, codec, nodeCodec, loadVerification, writeAdmission)

    /// The ONE-SHOT checkpoint re-seal for a store written before Phase 2305
    /// (`docs/migrations/2305-core-snapshot.md`; removed in the release after the
    /// one that ships it). Every row of `op_checkpoint`, in every stream, is read
    /// in ONE immediate transaction, its snapshot decoded through `nodeCodec`, and
    /// passed to `CheckpointReseal.reseal`: a checkpoint that verifies under the
    /// pre-2305 seal has its `snapshot_hash` rewritten to Core's, one already under
    /// Core's seal is left alone (so a re-run is a no-op), and ANY row that
    /// verifies under neither — or whose snapshot does not decode — refuses the
    /// whole migration by name with nothing written. Only the `snapshot_hash`
    /// column moves: the ops, their chain, the checkpoints' boundaries and the
    /// retained invocation keys are untouched. Returns the number of rows
    /// re-sealed.
    let resealCheckpoints<'Msg> (connectionString: string) (nodeCodec: INodeJsonCodec<'Msg>) : Result<int, string> =
        use conn = SqliteConnections.openConnection connectionString
        use tx = conn.BeginTransaction(deferred = false)

        let hasTable =
            use cmd = conn.CreateCommand()
            cmd.Transaction <- tx
            cmd.CommandText <- "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'op_checkpoint';"
            Convert.ToInt64(cmd.ExecuteScalar()) > 0L

        let rows =
            if not hasTable then
                []
            else
                use cmd = conn.CreateCommand()
                cmd.Transaction <- tx

                cmd.CommandText <-
                    """SELECT stream_id, sequence, previous_chain_head, snapshot_hash, snapshot_json, timestamp
FROM op_checkpoint
ORDER BY stream_id, sequence;"""

                use reader = cmd.ExecuteReader()
                let acc = ResizeArray<string * int * string * string * string * int64>()

                while reader.Read() do
                    acc.Add(
                        reader.GetString 0,
                        reader.GetInt32 1,
                        reader.GetString 2,
                        reader.GetString 3,
                        reader.GetString 4,
                        reader.GetInt64 5
                    )

                List.ofSeq acc

        let decided =
            rows
            |> List.map (fun (streamId, sequence, head, hash, json, ts) ->
                match nodeCodec.DecodeNode json with
                | Error error ->
                    Error(
                        sprintf
                            "checkpoint re-seal refused for stream '%s' at sequence %d: its snapshot does not decode (%A), so its seal cannot be checked. Nothing was written."
                            streamId
                            sequence
                            error
                    )
                | Ok snapshot ->
                    let checkpoint: Checkpoint<'Msg> =
                        { StreamId = streamId
                          Sequence = sequence
                          PreviousChainHead = head
                          SnapshotHash = hash
                          Snapshot = snapshot
                          Timestamp = DateTimeOffset.FromUnixTimeSeconds ts }

                    CheckpointReseal.reseal checkpoint |> Result.mapError CheckpointReseal.describe)

        match
            decided
            |> List.tryPick (fun r ->
                (match r with
                 | Error e -> Some e
                 | Ok _ -> None))
        with
        | Some refusal ->
            tx.Rollback()
            Error refusal
        | None ->
            let resealed =
                decided
                |> List.choose (fun r ->
                    match r with
                    | Ok(CheckpointReseal.Outcome.Resealed cp) -> Some cp
                    | _ -> None)

            for cp in resealed do
                use cmd = conn.CreateCommand()
                cmd.Transaction <- tx

                cmd.CommandText <-
                    "UPDATE op_checkpoint SET snapshot_hash = @hash WHERE stream_id = @stream_id AND sequence = @sequence;"

                cmd.Parameters.AddWithValue("@hash", cp.SnapshotHash) |> ignore
                cmd.Parameters.AddWithValue("@stream_id", cp.StreamId) |> ignore
                cmd.Parameters.AddWithValue("@sequence", cp.Sequence) |> ignore
                cmd.ExecuteNonQuery() |> ignore

            tx.Commit()
            Ok resealed.Length
