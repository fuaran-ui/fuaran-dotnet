namespace Fuaran.UI.OpStream.Dag.Sqlite

open System
open System.Text
open Microsoft.Data.Sqlite
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.SqliteStore

// ============================================================================
//  SqliteDagSink — Microsoft.Data.Sqlite-backed IDagOpStreamSink<'Msg>.
//
//  The durable counterpart of the in-memory DAG sink. The schema is VERSIONED
//  (Phase 2645, the cut — `SqliteDagSchema.version`, recorded in `dag_schema`):
//
//      CREATE TABLE dag_schema ( version INTEGER NOT NULL );
//      CREATE TABLE dag_node (                       -- the live set: whole Core nodes
//          stream_id            TEXT    NOT NULL,
//          hash                 TEXT    NOT NULL,    -- Dag.nodeId (DagOpRecord.nodeId)
//          parents_json         TEXT    NOT NULL,    -- JSON array of parent hashes (author order)
//          op_json              TEXT    NOT NULL,
//          prompt_id            TEXT    NULL,
//          actor_json           TEXT    NOT NULL,    -- canonical typed-actor JSON (Actor.encode)
//          timestamp            INTEGER NOT NULL,
//          result_envelope_json TEXT    NOT NULL,
//          PRIMARY KEY (stream_id, hash)
//      );
//      CREATE TABLE dag_skeleton (                   -- pruned nodes: address + links, no payload
//          stream_id TEXT NOT NULL, hash TEXT NOT NULL, parents_json TEXT NOT NULL,
//          PRIMARY KEY (stream_id, hash)
//      );
//      CREATE TABLE dag_head ( stream_id TEXT PRIMARY KEY, head TEXT NOT NULL );
//
//  The pre-cut table (`dag_op_record`, with its `outcome_hash`, `tombstoned`
//  and retired `content_fingerprint` columns) is NOT read: its addresses do not
//  recompute under the cut, so a database holding it is refused by name on
//  open, and the one-release re-mint tool re-addresses it. A database recording
//  another schema version is refused the same way.
//
//  The trunk-head CAS (`TryAdvanceHead`) is a CONDITIONAL UPDATE
//  (`UPDATE dag_head SET head = @new WHERE stream_id = @s AND head = @expected`)
//  — atomic at the statement level, so two racing advancers from the same
//  expected head leave exactly one winner (rows-affected = 1). The genesis
//  advance (`expected = None`) is an `INSERT … ON CONFLICT DO NOTHING` whose
//  rows-affected distinguishes "head was unset" (1) from "head already set" (0).
//
//  Op JSON goes through the host `IOpJsonCodec<'Msg>` (closure-bearing typed
//  ops can't round-trip generically). `Reachable` is Core's `Dag.ancestorsOf`
//  over the stream's live nodes.
//
//  ── VERIFY ON READ (Phase 793) ─────────────────────────────────────────────
//  `Records` and `TryGet` re-verify what they hand back
//  (`DagVerify.recordsResolving` / `DagVerify.record`) rather than trusting the
//  rows because this sink wrote them. This store outlives the process and is a file any other program can
//  touch, so it is where "computed on write, never checked on read" actually
//  costs something. `Add` runs the admission both DAG sinks share
//  (`DagWriteAdmission.admit`, Phase 1937): the address recomputed, every parent
//  present, a held address a no-op — so a forged record never reaches a row.
//
//  The topology reads (`Parents` / `Heads`) deliberately
//  do NOT verify: they read only (hash, parents) and never return a record, so
//  there is no content address in scope to recompute — the check would have to
//  load every payload to say nothing more than `Records` already says. A caller
//  that wants the whole store proved calls `Records`.
// ============================================================================

module private DagJson =

    /// Read the `user_id` column back as a typed `Actor`. Phase 1144 stores
    /// `Actor.encode` there; a pre-1144 row holds a bare id string and lifts to
    /// the `Human` case, the same fallback the linear sqlite sink has used since
    /// Phase 320. Lifting keeps an old database READABLE — it does not make an
    /// old record's content address valid, which no read path can.
    let readActor (raw: string) : Actor =
        Actor.tryDecode raw |> Option.defaultValue (Actor.ofLegacyString raw)

    /// JSON array of parent hashes. Hashes are 64-hex / opaque tokens with no
    /// JSON-special characters, but each is spelled through Core's canonical
    /// escape all the same (Phase 2026).
    let encodeParents (parents: string list) : string =
        "["
        + (parents |> List.map Fuaran.Core.JsonString.quote |> String.concat ",")
        + "]"

    /// Inverse of `encodeParents`. The writer is the only producer, so a small
    /// scanner over the quoted tokens is enough.
    let decodeParents (json: string) : string list =
        let trimmed = json.Trim()

        if trimmed = "[]" || trimmed = "" then
            []
        else
            // Strip the surrounding [ ], split top-level quoted strings.
            let inner = trimmed.Trim('[', ']')
            let result = ResizeArray<string>()

            // Phase 2028: the parents are 64-hex hashes, so a row holds no escape
            // and each parent is the substring between a pair of quotes. A row
            // with a backslash anywhere takes the escape-aware scanner, unchanged.
            if inner.IndexOf '\\' < 0 then
                let mutable openQuote = inner.IndexOf '"'

                while openQuote >= 0 do
                    let closeQuote = inner.IndexOf('"', openQuote + 1)

                    if closeQuote < 0 then
                        // An unterminated last string: the scanner drops it too.
                        openQuote <- -1
                    else
                        result.Add(inner.Substring(openQuote + 1, closeQuote - openQuote - 1))
                        openQuote <- inner.IndexOf('"', closeQuote + 1)
            else
                let sb = StringBuilder()
                let mutable inStr = false
                let mutable escaped = false

                for ch in inner do
                    if escaped then
                        sb.Append ch |> ignore
                        escaped <- false
                    elif ch = '\\' then
                        escaped <- true
                    elif ch = '"' then
                        if inStr then
                            result.Add(sb.ToString())
                            sb.Clear() |> ignore

                        inStr <- not inStr
                    elif inStr then
                        sb.Append ch |> ignore

            List.ofSeq result

/// The DAG table schema this sink reads and writes (Phase 2645).
module SqliteDagSchema =
    /// Recorded in `dag_schema`; a database recording another version is refused.
    [<Literal>]
    let version = 1

type SqliteDagSink<'Msg>(connectionString: string, codec: IOpJsonCodec<'Msg>, loadVerification: LoadVerification) =


    /// `IDagOpStreamSink` has no error channel — its reads return bare records —
    /// so a broken DAG is refused the way this sink already refuses an
    /// undecodable row: by name, naming the stream and the record.
    /// `LoadVerification.Tail` has no meaning over a SET (no total order to take
    /// a tail of), so it verifies in full — erring towards more checking.
    let verifyOnRead (streamId: string) (check: unit -> Result<unit, DagVerificationError>) =
        match loadVerification with
        | LoadVerification.Off -> ()
        | LoadVerification.Full
        | LoadVerification.Tail _ ->
            match check () with
            | Ok() -> ()
            | Error e -> invalidOp ("SqliteDagSink: " + DagVerify.describe streamId e)

    let openConnection () : SqliteConnection =
        SqliteConnections.openConnection connectionString

    let ensureSchema () =
        use conn = openConnection ()

        SqliteConnections.useWriteAheadLog conn

        let scalar (sql: string) =
            use q = conn.CreateCommand()
            q.CommandText <- sql
            q.ExecuteScalar()

        if not (isNull (scalar "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'dag_op_record';")) then
            invalidOp
                "SqliteDagSink: this database holds a pre-2645 DAG table (dag_op_record). Its addresses do not recompute under the cut (Core's Dag.nodeId); re-mint the store with the one-release re-mint tool before opening it."

        use cmd = conn.CreateCommand()

        cmd.CommandText <-
            """CREATE TABLE IF NOT EXISTS dag_schema (
    version INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS dag_node (
    stream_id            TEXT    NOT NULL,
    hash                 TEXT    NOT NULL,
    parents_json         TEXT    NOT NULL,
    op_json              TEXT    NOT NULL,
    prompt_id            TEXT    NULL,
    actor_json           TEXT    NOT NULL,
    timestamp            INTEGER NOT NULL,
    result_envelope_json TEXT    NOT NULL,
    PRIMARY KEY (stream_id, hash)
);
CREATE TABLE IF NOT EXISTS dag_skeleton (
    stream_id    TEXT NOT NULL,
    hash         TEXT NOT NULL,
    parents_json TEXT NOT NULL,
    PRIMARY KEY (stream_id, hash)
);
CREATE TABLE IF NOT EXISTS dag_head (
    stream_id TEXT PRIMARY KEY,
    head      TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_dag_node_hash ON dag_node (hash);
CREATE INDEX IF NOT EXISTS idx_dag_skeleton_hash ON dag_skeleton (hash);
INSERT INTO dag_schema (version) SELECT @v WHERE NOT EXISTS (SELECT 1 FROM dag_schema);"""

        cmd.Parameters.AddWithValue("@v", SqliteDagSchema.version) |> ignore
        cmd.ExecuteNonQuery() |> ignore

        match scalar "SELECT version FROM dag_schema;" with
        | :? int64 as v when v = int64 SqliteDagSchema.version -> ()
        | v ->
            invalidOp (
                sprintf
                    "SqliteDagSink: this database records DAG schema version %O; this sink reads version %d only."
                    v
                    SqliteDagSchema.version
            )

    do ensureSchema ()

    /// The `result_envelope_json` column through the decoder the linear sink
    /// uses (Phase 1937). A shape the writer never produced is refused by name;
    /// the copy this sink carried read it as `Failure("", "")`.
    let envelopeAt (streamId: string) (hash: string) (json: string) : OpResultEnvelope =
        match ResultEnvelopeJson.decode json with
        | Ok envelope -> envelope
        | Error msg ->
            invalidOp (
                sprintf
                    "SqliteDagSink: failed to decode the result envelope of record %s in stream '%s': %s"
                    hash
                    streamId
                    msg
            )

    /// Read all records for `streamId` (no decode of op — used by topology /
    /// Heads which only need (hash, parents)).
    let loadParentMap (streamId: string) : Map<string, string list> =
        use conn = openConnection ()
        use cmd = conn.CreateCommand()
        cmd.CommandText <- "SELECT hash, parents_json FROM dag_node WHERE stream_id = @s;"
        cmd.Parameters.AddWithValue("@s", streamId) |> ignore
        use reader = cmd.ExecuteReader()
        let acc = System.Collections.Generic.Dictionary<string, string list>()

        while reader.Read() do
            acc[reader.GetString 0] <- DagJson.decodeParents (reader.GetString 1)

        acc |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq

    /// The parents of ONE hash, by primary-key lookup. `Parents` used to answer
    /// this by loading the entire stream's parent map and indexing into it —
    /// every row of a stream read, decoded and discarded, to return one row's
    /// worth of answer. The traversals above still need the whole map; a single
    /// point lookup does not.
    let readParents (streamId: string) (hash: string) : string list =
        use conn = openConnection ()
        use cmd = conn.CreateCommand()

        cmd.CommandText <-
            "SELECT parents_json FROM dag_node WHERE stream_id = @s AND hash = @h UNION ALL SELECT parents_json FROM dag_skeleton WHERE stream_id = @s AND hash = @h;"

        cmd.Parameters.AddWithValue("@s", streamId) |> ignore
        cmd.Parameters.AddWithValue("@h", hash) |> ignore

        match cmd.ExecuteScalar() with
        | :? string as json -> DagJson.decodeParents json
        | _ -> []

    /// A record as a row stores it: the STORED address, never a recomputed one,
    /// so the read path's verification can find a row whose address is wrong.
    let stored
        (streamId: string)
        (hash: string)
        (parents: string list)
        (actor: Actor)
        (entry: StreamEntry<'Msg>)
        : DagOpRecord<'Msg> =
        { StreamId = streamId
          Node =
            { Id = hash
              Parents = parents
              Actor = actor
              Op = entry } }

    let readRecord (streamId: string) (hash: string) : DagOpRecord<'Msg> option =
        use conn = openConnection ()
        use cmd = conn.CreateCommand()

        cmd.CommandText <-
            """SELECT parents_json, op_json, prompt_id, actor_json, timestamp, result_envelope_json
FROM dag_node WHERE stream_id = @s AND hash = @h;"""

        cmd.Parameters.AddWithValue("@s", streamId) |> ignore
        cmd.Parameters.AddWithValue("@h", hash) |> ignore
        use reader = cmd.ExecuteReader()

        if not (reader.Read()) then
            None
        else
            let opJson = reader.GetString 1

            let op =
                match codec.DecodeOp opJson with
                | Ok o -> o
                | Error error ->
                    raise (
                        CodecDecodeFailed(
                            sprintf "SqliteDagSink: codec failed to decode op at (%s, %s)" streamId hash,
                            error
                        )
                    )

            let promptId = if reader.IsDBNull 2 then None else Some(reader.GetString 2)

            Some(
                stored
                    streamId
                    hash
                    (DagJson.decodeParents (reader.GetString 0))
                    (DagJson.readActor (reader.GetString 3))
                    { Op = op
                      PromptId = promptId
                      Timestamp = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64 4)
                      ResultEnvelope = envelopeAt streamId hash (reader.GetString 5) }
            )

    /// Bulk read of every record in a stream in ONE query + connection, replacing
    /// the `Records` member's N+1 (a `readRecord` per hash, each opening its own
    /// connection). `ORDER BY hash` reproduces the old order exactly — the prior
    /// path materialised hashes via `loadParentMap |> Map.toList` (ascending hash)
    /// then `readRecord` per hash, and hashes are pure-ASCII hex so SQLite's
    /// BINARY collation matches F#'s ordinal string compare.
    let readAllRecords (streamId: string) : DagOpRecord<'Msg> list =
        use conn = openConnection ()
        use cmd = conn.CreateCommand()

        cmd.CommandText <-
            """SELECT hash, parents_json, op_json, prompt_id, actor_json, timestamp, result_envelope_json
FROM dag_node WHERE stream_id = @s ORDER BY hash;"""

        cmd.Parameters.AddWithValue("@s", streamId) |> ignore
        use reader = cmd.ExecuteReader()
        let acc = System.Collections.Generic.List<DagOpRecord<'Msg>>()

        while reader.Read() do
            let hash = reader.GetString 0
            let opJson = reader.GetString 2

            let op =
                match codec.DecodeOp opJson with
                | Ok o -> o
                | Error error ->
                    raise (
                        CodecDecodeFailed(
                            sprintf "SqliteDagSink: codec failed to decode op at (%s, %s)" streamId hash,
                            error
                        )
                    )

            let promptId = if reader.IsDBNull 3 then None else Some(reader.GetString 3)

            acc.Add(
                stored
                    streamId
                    hash
                    (DagJson.decodeParents (reader.GetString 1))
                    (DagJson.readActor (reader.GetString 4))
                    { Op = op
                      PromptId = promptId
                      Timestamp = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64 5)
                      ResultEnvelope = envelopeAt streamId hash (reader.GetString 6) }
            )

        List.ofSeq acc

    /// Which of `hashes` name a record ANYWHERE in this store. Parent linkage is
    /// resolved store-wide, not within the stream being read: a guest branch's
    /// genesis is anchored on the `Mount` op in the HOST stream, so a
    /// stream-scoped set is not a closed parent universe. One query over the
    /// candidate parents, not one query per parent.
    let presentHashesOn (conn: SqliteConnection) (hashes: string list) : Set<string> =
        match hashes with
        | [] -> Set.empty
        | _ ->
            use cmd = conn.CreateCommand()

            // Placeholder NAMES are generated; the hashes themselves are BOUND.
            // Concatenating the ids into the text would be an injection seam over
            // values that arrive from a wire decoder, and would defeat SQLite's
            // statement cache on every distinct arity besides.
            let names = hashes |> List.mapi (fun i _ -> "@h" + string i)

            let inList = String.concat "," names

            cmd.CommandText <-
                "SELECT hash FROM dag_node WHERE hash IN ("
                + inList
                + ") UNION SELECT hash FROM dag_skeleton WHERE hash IN ("
                + inList
                + ");"

            hashes
            |> List.iteri (fun i h -> cmd.Parameters.AddWithValue("@h" + string i, h) |> ignore)

            use reader = cmd.ExecuteReader()
            let acc = ResizeArray<string>()

            while reader.Read() do
                acc.Add(reader.GetString 0)

            Set.ofSeq acc

    let presentHashes (hashes: string list) : Set<string> =
        match hashes with
        | [] -> Set.empty
        | _ ->
            use conn = openConnection ()
            presentHashesOn conn hashes

    /// Store `record`'s row on `conn`, inside the caller's transaction. The one
    /// INSERT both `Add` and `AddAll` reach, after admission.
    let insertOn (conn: SqliteConnection) (record: DagOpRecord<'Msg>) : unit =
        use cmd = conn.CreateCommand()

        cmd.CommandText <-
            """INSERT INTO dag_node
    (stream_id, hash, parents_json, op_json, prompt_id, actor_json, timestamp, result_envelope_json)
VALUES
    (@s, @h, @parents, @op, @prompt, @actor, @ts, @env);"""

        cmd.Parameters.AddWithValue("@s", record.StreamId) |> ignore
        cmd.Parameters.AddWithValue("@h", record.Hash) |> ignore

        cmd.Parameters.AddWithValue("@parents", DagJson.encodeParents record.Parents)
        |> ignore

        cmd.Parameters.AddWithValue("@op", codec.EncodeOp record.Op) |> ignore

        cmd.Parameters.AddWithValue(
            "@prompt",
            (match record.PromptId with
             | Some p -> box p
             | None -> box DBNull.Value)
        )
        |> ignore

        // Phase 1144 — the `user_id` column now holds the canonical typed-actor
        // JSON (`Actor.encode`), mirroring what the LINEAR sqlite sink has stored
        // since Phase 320. The column is reused rather than renamed: pre-1144 rows
        // hold a bare id string and read back via `Actor.ofLegacyString`, so an
        // existing database still OPENS. Its records' content addresses do not
        // carry forward, though — see docs/migrations/1144-typed-actor-dag-fold.md.
        cmd.Parameters.AddWithValue("@actor", Actor.encode record.Actor) |> ignore

        cmd.Parameters.AddWithValue("@ts", record.Timestamp.ToUnixTimeSeconds())
        |> ignore

        cmd.Parameters.AddWithValue("@env", ResultEnvelopeJson.encode record.ResultEnvelope)
        |> ignore

        cmd.ExecuteNonQuery() |> ignore

    /// Admit and store every record of `records`, in order, in ONE `BEGIN
    /// IMMEDIATE` transaction (Phase 2117; `Add` is the one-record case). Each
    /// record runs the admission both DAG sinks share (`DagWriteAdmission.admit`,
    /// Phase 1937) on the transaction's own connection, so a parent stored earlier
    /// in the batch is present and an address stored earlier is held. A refusal
    /// throws before the commit and the transaction rolls back on dispose: every
    /// record or none. `what` names the write in the contention refusal.
    let addAll (what: string) (streamId: string) (records: DagOpRecord<'Msg> list) : unit =
        match records with
        | [] -> ()
        | _ ->
            // A busy database is CONTENTION, not corruption (Phase 1525).
            // `Add` / `AddAll` have no "try again" channel — they return unit, and
            // a caller cannot tell a completed append from a swallowed one — so
            // unlike the CAS below a busy store is refused BY NAME rather than
            // reported as success. The name is the point: a raw `SqliteException`
            // escaping here reads as a store defect and sends the reader to the
            // wrong question.
            try
                use conn = openConnection ()

                // `BEGIN IMMEDIATE` (Phase 1525). The admission below is a
                // read-then-write over the same rows — the parent-presence and
                // held-address SELECTs, then the INSERT. A DEFERRED transaction
                // takes its write lock only when the first write executes, so two
                // writers can both pass their reads and one then fails to upgrade;
                // the reads that justified the write are stale by the time it
                // lands. IMMEDIATE takes the write lock up front, so the reads and
                // the write see one state.
                use tx = conn.BeginTransaction(false)

                for record in records do
                    // The address recomputed, every parent present store-wide (one
                    // batched `IN (...)` query, not one per parent: a guest branch's
                    // genesis hangs off a record in the HOST stream), and an address
                    // the stream already holds a no-op.
                    let present = presentHashesOn conn record.Parents

                    let held (hash: string) =
                        use probe = conn.CreateCommand()

                        probe.CommandText <-
                            "SELECT 1 FROM dag_node WHERE stream_id = @s AND hash = @h UNION ALL SELECT 1 FROM dag_skeleton WHERE stream_id = @s AND hash = @h;"

                        probe.Parameters.AddWithValue("@s", record.StreamId) |> ignore
                        probe.Parameters.AddWithValue("@h", hash) |> ignore
                        not (isNull (probe.ExecuteScalar()))

                    match DagWriteAdmission.admit present.Contains held record with
                    | Error refusal -> invalidOp (DagWriteAdmission.describe "SqliteDagSink" record refusal)
                    | Ok DagAdmission.AlreadyHeld -> ()
                    | Ok DagAdmission.Store -> insertOn conn record

                tx.Commit()
            with :? SqliteException as ex when SqliteConnections.isContention ex ->
                invalidOp (
                    sprintf
                        "SqliteDagSink: could not append %s to stream '%s' — SQLITE_BUSY (%d) after busy_timeout=%dms: another writer holds the database. This is contention, not corruption — retry the append."
                        what
                        streamId
                        ex.SqliteErrorCode
                        SqliteConnections.busyTimeoutMs
                )

    /// Two-arg constructor — verifies every read (Phase 793).
    new(connectionString: string, codec: IOpJsonCodec<'Msg>) =
        SqliteDagSink<'Msg>(connectionString, codec, LoadVerification.Full)

    interface IDagOpStreamSink<'Msg> with

        member _.Add(record: DagOpRecord<'Msg>) : Async<unit> =
            async { addAll ("record " + record.Hash) record.StreamId [ record ] }

        member _.TryGet(streamId: string, hash: string) : Async<DagOpRecord<'Msg> option> =
            async {
                let found = readRecord streamId hash

                match found with
                | None -> ()
                | Some r -> verifyOnRead streamId (fun () -> DagVerify.record r)

                return found
            }

        member _.Head(streamId: string) : Async<string option> =
            async {
                use conn = openConnection ()
                use cmd = conn.CreateCommand()
                cmd.CommandText <- "SELECT head FROM dag_head WHERE stream_id = @s;"
                cmd.Parameters.AddWithValue("@s", streamId) |> ignore

                match cmd.ExecuteScalar() with
                | null -> return None
                | :? string as h -> return Some h
                | _ -> return None
            }

        member _.TryAdvanceHead(streamId: string, expected: string option, newHead: string) : Async<bool> =
            async {
                // SQLITE_BUSY here is the tier's CONTENTION outcome, not an
                // error (Phase 1525). `TryAdvanceHead` already has a channel for
                // "another writer got there first" — `false`, on which every
                // caller re-reads the head and retries (`DagMerge.mergeIntoTrunk`
                // is the worked example). A writer that could not take the lock
                // within `busy_timeout` has lost exactly that race, so reporting
                // it as `false` says what happened; letting a raw
                // `SqliteException` out of the sole concurrency primitive turned
                // a retryable outcome into a crash in the caller's retry loop.
                //
                // Only the CAS is treated this way. A busy `Add` is NOT silently
                // swallowed — it has no "try again" channel, so it is refused by
                // name below rather than reported as a completed append.
                try
                    use conn = openConnection ()

                    match expected with
                    | None ->
                        // Genesis advance: succeeds iff no head row exists yet.
                        use cmd = conn.CreateCommand()

                        cmd.CommandText <-
                            "INSERT INTO dag_head (stream_id, head) VALUES (@s, @new) ON CONFLICT(stream_id) DO NOTHING;"

                        cmd.Parameters.AddWithValue("@s", streamId) |> ignore
                        cmd.Parameters.AddWithValue("@new", newHead) |> ignore
                        return cmd.ExecuteNonQuery() = 1
                    | Some e ->
                        // Conditional CAS — atomic at the statement level.
                        use cmd = conn.CreateCommand()
                        cmd.CommandText <- "UPDATE dag_head SET head = @new WHERE stream_id = @s AND head = @expected;"
                        cmd.Parameters.AddWithValue("@s", streamId) |> ignore
                        cmd.Parameters.AddWithValue("@new", newHead) |> ignore
                        cmd.Parameters.AddWithValue("@expected", e) |> ignore
                        return cmd.ExecuteNonQuery() = 1
                with :? SqliteException as ex when SqliteConnections.isContention ex ->
                    return false
            }

        member _.Parents(streamId: string, hash: string) : Async<string list> =
            async { return readParents streamId hash }

        member _.Reachable(streamId: string, hash: string) : Async<Set<string>> =
            async { return Fuaran.Core.Dag.ancestorsOf (DagOpRecord.toDag (readAllRecords streamId)) hash }

        member _.Heads(streamId: string) : Async<string list> =
            async {
                let m = loadParentMap streamId

                let referenced = m |> Map.toSeq |> Seq.collect snd |> Set.ofSeq

                return
                    m
                    |> Map.toList
                    |> List.map fst
                    |> List.filter (fun h -> not (referenced.Contains h))
            }

        member _.Records(streamId: string) : Async<DagOpRecord<'Msg> list> =
            async {
                let records = readAllRecords streamId

                verifyOnRead streamId (fun () ->
                    let present = records |> List.collect _.Parents |> List.distinct |> presentHashes

                    DagVerify.recordsResolving present.Contains records)

                return records
            }

        member _.Tombstone(streamId: string, hash: string) : Async<bool> =
            async {
                use conn = openConnection ()
                use tx = conn.BeginTransaction(false)

                let parents =
                    use cmd = conn.CreateCommand()
                    cmd.CommandText <- "SELECT parents_json FROM dag_node WHERE stream_id = @s AND hash = @h;"
                    cmd.Parameters.AddWithValue("@s", streamId) |> ignore
                    cmd.Parameters.AddWithValue("@h", hash) |> ignore

                    match cmd.ExecuteScalar() with
                    | :? string as json -> Some json
                    | _ -> None

                match parents with
                | Some json ->
                    use move = conn.CreateCommand()

                    move.CommandText <-
                        """INSERT INTO dag_skeleton (stream_id, hash, parents_json) VALUES (@s, @h, @p);
DELETE FROM dag_node WHERE stream_id = @s AND hash = @h;"""

                    move.Parameters.AddWithValue("@s", streamId) |> ignore
                    move.Parameters.AddWithValue("@h", hash) |> ignore
                    move.Parameters.AddWithValue("@p", json) |> ignore
                    move.ExecuteNonQuery() |> ignore
                    tx.Commit()
                    return true
                | _ -> return false
            }

        member _.Skeleton(streamId: string) : Async<DagSkeletonNode list> =
            async {
                use conn = openConnection ()
                use cmd = conn.CreateCommand()
                cmd.CommandText <- "SELECT hash, parents_json FROM dag_skeleton WHERE stream_id = @s ORDER BY hash;"
                cmd.Parameters.AddWithValue("@s", streamId) |> ignore
                use reader = cmd.ExecuteReader()
                let acc = ResizeArray<DagSkeletonNode>()

                while reader.Read() do
                    acc.Add
                        { StreamId = streamId
                          Hash = reader.GetString 0
                          Parents = DagJson.decodeParents (reader.GetString 1) }

                return List.ofSeq acc
            }

        member _.Streams() : Async<string list> =
            async {
                use conn = openConnection ()
                use cmd = conn.CreateCommand()
                cmd.CommandText <- "SELECT stream_id FROM dag_node UNION SELECT stream_id FROM dag_skeleton;"
                use reader = cmd.ExecuteReader()
                let acc = ResizeArray<string>()

                while reader.Read() do
                    acc.Add(reader.GetString 0)

                return List.ofSeq acc
            }

    /// Phase 2117 — the atomic batch add: one `BEGIN IMMEDIATE` transaction
    /// over every record, committed only when each was admitted.
    interface IDagOpStreamBatchSink<'Msg> with
        member _.AddAll(records: DagOpRecord<'Msg> list) : Async<unit> =
            async {
                match records with
                | [] -> ()
                | first :: _ -> addAll (sprintf "a batch of %d records" (List.length records)) first.StreamId records
            }

module SqliteDagSink =
    /// Fresh sink as the abstraction interface. Verifies every read (Phase 793).
    let create<'Msg> (connectionString: string) (codec: IOpJsonCodec<'Msg>) : IDagOpStreamSink<'Msg> =
        upcast SqliteDagSink<'Msg>(connectionString, codec)

    /// `create` under an explicit read-path verification mode. Naming a cheaper
    /// mode here is the ONLY way to get one — there is no silent fast path.
    let createWith<'Msg>
        (loadVerification: LoadVerification)
        (connectionString: string)
        (codec: IOpJsonCodec<'Msg>)
        : IDagOpStreamSink<'Msg> =
        upcast SqliteDagSink<'Msg>(connectionString, codec, loadVerification)
