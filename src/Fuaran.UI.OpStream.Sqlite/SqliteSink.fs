namespace Fuaran.UI.OpStream.Sqlite

open System
open System.Globalization
open Microsoft.Data.Sqlite
open Fuaran.UI.OpStream.Abstractions

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

module private ResultEnvelopeJson =
    // Hand-rolled, no Newtonsoft / no Fable.SimpleJson — same posture as
    // ArgsJsonContract. Closed shape so we don't need a codec from the host.

    let private escape (s: string) : string =
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

    let encode (envelope: OpResultEnvelope) : string =
        match envelope with
        | OpResultEnvelope.Success -> "{\"$type\":\"Success\"}"
        | OpResultEnvelope.Failure(code, message) ->
            sprintf "{\"$type\":\"Failure\",\"code\":%s,\"message\":%s}" (escape code) (escape message)

    /// Parse the SqliteSink-written envelope JSON back into a typed
    /// `OpResultEnvelope`. The shape is closed and the encoder is the only
    /// writer, so a small hand-rolled tokeniser is enough — no need to
    /// drag in a full JSON parser.
    let decode (json: string) : Result<OpResultEnvelope, string> =
        // Look for the discriminator literally — keys are not whitespace-aware
        // by construction (we control the encoder). Failure carries two
        // escaped strings; we extract via the same single-quote tokeniser.
        if json = "{\"$type\":\"Success\"}" then
            Ok OpResultEnvelope.Success
        elif json.StartsWith("{\"$type\":\"Failure\"") then
            let extractString (key: string) : string option =
                let needle = sprintf "\"%s\":\"" key

                match json.IndexOf needle with
                | -1 -> None
                | start ->
                    let valueStart = start + needle.Length
                    let mutable i = valueStart
                    let sb = System.Text.StringBuilder()
                    let mutable closed = false
                    let mutable failed = false

                    while not closed && not failed && i < json.Length do
                        let c = json[i]

                        if c = '\\' && i + 1 < json.Length then
                            match json[i + 1] with
                            | '"' ->
                                sb.Append '"' |> ignore
                                i <- i + 2
                            | '\\' ->
                                sb.Append '\\' |> ignore
                                i <- i + 2
                            | 'u' when i + 5 < json.Length ->
                                let hex = json.Substring(i + 2, 4)

                                match UInt16.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture) with
                                | true, cp ->
                                    sb.Append(char cp) |> ignore
                                    i <- i + 6
                                | false, _ -> failed <- true
                            | _ -> failed <- true
                        elif c = '"' then
                            closed <- true
                        else
                            sb.Append c |> ignore
                            i <- i + 1

                    if closed && not failed then Some(sb.ToString()) else None

            match extractString "code", extractString "message" with
            | Some code, Some message -> Ok(OpResultEnvelope.Failure(code, message))
            | _ -> Error("OpResultEnvelope.Failure: missing code or message field")
        else
            Error(sprintf "Unknown OpResultEnvelope shape: %s" json)

type SqliteSink<'Msg>
    (
        connectionString: string,
        codec: IOpJsonCodec<'Msg>,
        nodeCodec: INodeJsonCodec<'Msg>,
        loadVerification: LoadVerification
    ) =

    let openConnection () : SqliteConnection =
        let conn = new SqliteConnection(connectionString)
        conn.Open()
        conn

    let ensureSchema () =
        use conn = openConnection ()
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

    /// The one INSERT into `op_stream`. Callers own the connection, and pass a
    /// transaction where a second statement has to land or not land with it
    /// (Phase 1485's keyed append). Extracted rather than duplicated so the
    /// column list, the typed-actor encoding and the SQLITE_CONSTRAINT
    /// translation cannot drift between the three append paths.
    let insertRecordOn
        (conn: SqliteConnection)
        (tx: SqliteTransaction option)
        (record: OpRecord<'Msg>)
        : AppendReceipt =
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

    /// The chain head as `IOpStreamCasSink.Head` reports it — the hash of the
    /// row at the highest sequence, or the genesis anchor for an empty stream.
    /// Takes the caller's connection (and transaction) so the compare and the
    /// append below are one atomic read-then-write rather than two races.
    let headOn (conn: SqliteConnection) (tx: SqliteTransaction option) (streamId: string) : string =
        use cmd = conn.CreateCommand()

        match tx with
        | Some t -> cmd.Transaction <- t
        | None -> ()

        cmd.CommandText <- "SELECT hash FROM op_stream WHERE stream_id = @stream_id ORDER BY sequence DESC LIMIT 1;"

        cmd.Parameters.AddWithValue("@stream_id", streamId) |> ignore

        match cmd.ExecuteScalar() with
        | :? string as h -> h
        | _ -> HashChain.genesisPreviousHash

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

    /// Three-arg constructor — verifies the whole loaded segment on `Replay`.
    new(connectionString: string, codec: IOpJsonCodec<'Msg>, nodeCodec: INodeJsonCodec<'Msg>) =
        SqliteSink<'Msg>(connectionString, codec, nodeCodec, LoadVerification.Full)

    /// Legacy two-arg constructor, kept for callers that don't
    /// need checkpoint snapshot round-trip (hash-chain verification only).
    /// Defaults the node codec to `NodeJsonCodec.encodeOnly`; AppendCheckpoint
    /// works (the encoder is purely additive) but LatestCheckpointAtOrBefore
    /// will return a decoder error if a checkpoint exists.
    new(connectionString: string, codec: IOpJsonCodec<'Msg>) =
        SqliteSink<'Msg>(connectionString, codec, NodeJsonCodec.encodeOnly<'Msg> (), LoadVerification.Full)

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
                    | Error msg ->
                        failwithf
                            "SqliteSink: codec failed to decode op at (StreamId=%s, Sequence=%d): %s"
                            (reader.GetString(0))
                            (reader.GetInt32(1))
                            msg
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
                use cmd = conn.CreateCommand()
                cmd.CommandText <- "SELECT COALESCE(MAX(sequence), 0) FROM op_stream WHERE stream_id = @stream_id;"
                cmd.Parameters.AddWithValue("@stream_id", streamId) |> ignore
                let result = cmd.ExecuteScalar()

                return
                    match result with
                    | :? int64 as n -> int n
                    | :? int as n -> n
                    | _ -> 0
            }

        member _.Streams() : Async<string list> =
            async {
                use conn = openConnection ()
                use cmd = conn.CreateCommand()
                cmd.CommandText <- "SELECT DISTINCT stream_id FROM op_stream;"
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
                    | Error msg ->
                        return
                            failwithf
                                "SqliteSink: node codec failed to decode snapshot at (StreamId=%s, Sequence=%d): %s"
                                (reader.GetString(0))
                                (reader.GetInt32(1))
                                msg
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
                    | Error msg ->
                        failwithf
                            "SqliteSink: node codec failed to decode snapshot at (StreamId=%s, Sequence=%d): %s"
                            (reader.GetString(0))
                            (reader.GetInt32(1))
                            msg
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
                use cmd = conn.CreateCommand()

                cmd.CommandText <- "DELETE FROM op_stream WHERE stream_id = @stream_id AND sequence <= @through;"

                cmd.Parameters.AddWithValue("@stream_id", streamId) |> ignore
                cmd.Parameters.AddWithValue("@through", throughSequence) |> ignore
                return cmd.ExecuteNonQuery()
            }

        member _.TruncateCheckpointsBefore(streamId: string, beforeSequence: int) : Async<int> =
            async {
                use conn = openConnection ()
                use cmd = conn.CreateCommand()

                cmd.CommandText <- "DELETE FROM op_checkpoint WHERE stream_id = @stream_id AND sequence < @before;"

                cmd.Parameters.AddWithValue("@stream_id", streamId) |> ignore
                cmd.Parameters.AddWithValue("@before", beforeSequence) |> ignore
                return cmd.ExecuteNonQuery()
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
                use conn = openConnection ()
                use tx = conn.BeginTransaction()
                let actual = headOn conn (Some tx) record.StreamId

                if actual <> expectedHead then
                    // Nothing was written, so the rollback is what makes "the stream is
                    // untouched" true rather than merely intended.
                    tx.Rollback()
                    return CasAppendOutcome.StaleHead(expectedHead, actual)
                else
                    let receipt = insertRecordOn conn (Some tx) record
                    tx.Commit()
                    return CasAppendOutcome.Appended receipt
            }

    interface IOpStreamKeyedSink<'Msg> with

        member _.AppendKeyed(record: OpRecord<'Msg>, invocationKey: string) : Async<KeyedAppendOutcome> =
            async {
                use conn = openConnection ()
                use tx = conn.BeginTransaction()

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
