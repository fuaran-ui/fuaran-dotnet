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
//  The durable counterpart of the in-memory DAG sink. Two tables:
//
//      CREATE TABLE dag_op_record (
//          stream_id            TEXT    NOT NULL,
//          hash                 TEXT    NOT NULL,
//          parents_json         TEXT    NOT NULL,   -- JSON array of parent hashes (author order)
//          op_json              TEXT    NOT NULL,
//          outcome_hash         TEXT    NULL,        -- merge nodes only
//          prompt_id            TEXT    NULL,
//          user_id              TEXT    NOT NULL,   -- canonical typed-actor JSON (Actor.encode)
//          timestamp            INTEGER NOT NULL,
//          result_envelope_json TEXT    NOT NULL,
//          tombstoned           INTEGER NOT NULL,    -- 0 / 1
//          PRIMARY KEY (stream_id, hash)
//      );
//      CREATE TABLE dag_head ( stream_id TEXT PRIMARY KEY, head TEXT NOT NULL );
//
//  The trunk-head CAS (`TryAdvanceHead`) is a CONDITIONAL UPDATE
//  (`UPDATE dag_head SET head = @new WHERE stream_id = @s AND head = @expected`)
//  — atomic at the statement level, so two racing advancers from the same
//  expected head leave exactly one winner (rows-affected = 1). The genesis
//  advance (`expected = None`) is an `INSERT … ON CONFLICT DO NOTHING` whose
//  rows-affected distinguishes "head was unset" (1) from "head already set" (0).
//
//  Op JSON goes through the host `IOpJsonCodec<'Msg>` (closure-bearing typed
//  ops can't round-trip generically). Topology queries load the stream's
//  (hash, parents) rows into memory and delegate to the pure `DagTopology`
//  algorithms — a recursive-CTE implementation is a later optimisation.
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
//  The topology reads (`Parents` / `Reachable` / `Lca` / `Heads`) deliberately
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

type SqliteDagSink<'Msg>(connectionString: string, codec: IOpJsonCodec<'Msg>, loadVerification: LoadVerification) =

    let emptyBatchJson = codec.EncodeOp(TreeOp.Batch [])

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

        use cmd = conn.CreateCommand()

        cmd.CommandText <-
            """CREATE TABLE IF NOT EXISTS dag_op_record (
    stream_id            TEXT    NOT NULL,
    hash                 TEXT    NOT NULL,
    parents_json         TEXT    NOT NULL,
    op_json              TEXT    NOT NULL,
    outcome_hash         TEXT    NULL,
    prompt_id            TEXT    NULL,
    user_id              TEXT    NOT NULL,
    timestamp            INTEGER NOT NULL,
    result_envelope_json TEXT    NOT NULL,
    tombstoned           INTEGER NOT NULL,
    PRIMARY KEY (stream_id, hash)
);
CREATE TABLE IF NOT EXISTS dag_head (
    stream_id TEXT PRIMARY KEY,
    head      TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_dag_op_record_hash ON dag_op_record (hash);"""

        cmd.ExecuteNonQuery() |> ignore

    // A database written between Phase 1525 and Phase 1937 also carries a
    // nullable `content_fingerprint` column. Nothing reads or writes it any
    // more — admission recomputes the address instead (Phase 1937) — and it
    // is left in place rather than rebuilt out of an existing table.

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
        cmd.CommandText <- "SELECT hash, parents_json FROM dag_op_record WHERE stream_id = @s;"
        cmd.Parameters.AddWithValue("@s", streamId) |> ignore
        use reader = cmd.ExecuteReader()
        let acc = System.Collections.Generic.Dictionary<string, string list>()

        while reader.Read() do
            acc[reader.GetString 0] <- DagJson.decodeParents (reader.GetString 1)

        acc |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq

    /// One whole-stream load, shared by every lookup the CALLER then makes. The
    /// traversals that use it (`reachable` / `lca`) walk an unbounded ancestor
    /// cone, so a per-hash query would be the N+1 this closure exists to avoid;
    /// the map is built once per call and thrown away, which is deliberate — the
    /// store is a file another process may be writing (WAL, above), so a map
    /// cached across calls would answer from a topology that has since moved.
    let parentsLookup (streamId: string) : string -> string list =
        let m = loadParentMap streamId
        fun h -> Map.tryFind h m |> Option.defaultValue []

    /// The parents of ONE hash, by primary-key lookup. `Parents` used to answer
    /// this by loading the entire stream's parent map and indexing into it —
    /// every row of a stream read, decoded and discarded, to return one row's
    /// worth of answer. The traversals above still need the whole map; a single
    /// point lookup does not.
    let readParents (streamId: string) (hash: string) : string list =
        use conn = openConnection ()
        use cmd = conn.CreateCommand()
        cmd.CommandText <- "SELECT parents_json FROM dag_op_record WHERE stream_id = @s AND hash = @h;"
        cmd.Parameters.AddWithValue("@s", streamId) |> ignore
        cmd.Parameters.AddWithValue("@h", hash) |> ignore

        match cmd.ExecuteScalar() with
        | :? string as json -> DagJson.decodeParents json
        | _ -> []

    let readRecord (streamId: string) (hash: string) : DagOpRecord<'Msg> option =
        use conn = openConnection ()
        use cmd = conn.CreateCommand()

        cmd.CommandText <-
            """SELECT parents_json, op_json, outcome_hash, prompt_id, user_id, timestamp, result_envelope_json, tombstoned
FROM dag_op_record WHERE stream_id = @s AND hash = @h;"""

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

            let outcomeHash = if reader.IsDBNull 2 then None else Some(reader.GetString 2)
            let promptId = if reader.IsDBNull 3 then None else Some(reader.GetString 3)

            Some
                { StreamId = streamId
                  Hash = hash
                  Parents = DagJson.decodeParents (reader.GetString 0)
                  Op = op
                  OutcomeHash = outcomeHash
                  PromptId = promptId
                  Actor = DagJson.readActor (reader.GetString 4)
                  Timestamp = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64 5)
                  ResultEnvelope = envelopeAt streamId hash (reader.GetString 6)
                  Tombstoned = reader.GetInt64 7 <> 0L }

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
            """SELECT hash, parents_json, op_json, outcome_hash, prompt_id, user_id, timestamp, result_envelope_json, tombstoned
FROM dag_op_record WHERE stream_id = @s ORDER BY hash;"""

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

            let outcomeHash = if reader.IsDBNull 3 then None else Some(reader.GetString 3)
            let promptId = if reader.IsDBNull 4 then None else Some(reader.GetString 4)

            acc.Add
                { StreamId = streamId
                  Hash = hash
                  Parents = DagJson.decodeParents (reader.GetString 1)
                  Op = op
                  OutcomeHash = outcomeHash
                  PromptId = promptId
                  Actor = DagJson.readActor (reader.GetString 5)
                  Timestamp = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64 6)
                  ResultEnvelope = envelopeAt streamId (reader.GetString 0) (reader.GetString 7)
                  Tombstoned = reader.GetInt64 8 <> 0L }

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

            cmd.CommandText <-
                "SELECT hash FROM dag_op_record WHERE hash IN ("
                + String.concat "," names
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

    /// Two-arg constructor — verifies every read (Phase 793).
    new(connectionString: string, codec: IOpJsonCodec<'Msg>) =
        SqliteDagSink<'Msg>(connectionString, codec, LoadVerification.Full)

    interface IDagOpStreamSink<'Msg> with

        member _.Add(record: DagOpRecord<'Msg>) : Async<unit> =
            async {
                // A busy database is CONTENTION, not corruption (Phase 1525).
                // `Add` has no "try again" channel — it returns unit, and a caller
                // cannot tell a completed append from a swallowed one — so unlike
                // the CAS below it is refused BY NAME rather than reported as
                // success. The name is the point: a raw `SqliteException` escaping
                // here reads as a store defect and sends the reader to the wrong
                // question.
                try
                    use conn = openConnection ()

                    // `BEGIN IMMEDIATE` (Phase 1525). The admission below is a
                    // read-then-write over the same rows — the parent-presence and
                    // held-address SELECTs, then the INSERT. A
                    // DEFERRED transaction takes its write lock only when the first
                    // write executes, so two writers can both pass their reads and
                    // one then fails to upgrade; the reads that justified the write
                    // are stale by the time it lands. IMMEDIATE takes the write lock
                    // up front, so the reads and the write see one state.
                    use tx = conn.BeginTransaction(false)

                    // ── ADMISSION (Phase 1937): the check both DAG sinks share ──
                    // The address recomputed, every parent present store-wide (one
                    // batched `IN (...)` query, not one per parent: a guest branch's
                    // genesis hangs off a record in the HOST stream), and an address
                    // the stream already holds a no-op.
                    let present = presentHashesOn conn record.Parents

                    let held (hash: string) =
                        use probe = conn.CreateCommand()
                        probe.CommandText <- "SELECT 1 FROM dag_op_record WHERE stream_id = @s AND hash = @h;"
                        probe.Parameters.AddWithValue("@s", record.StreamId) |> ignore
                        probe.Parameters.AddWithValue("@h", hash) |> ignore
                        not (isNull (probe.ExecuteScalar()))

                    match DagWriteAdmission.admit present.Contains held record with
                    | Error refusal -> invalidOp (DagWriteAdmission.describe "SqliteDagSink" record refusal)
                    | Ok DagAdmission.AlreadyHeld -> ()
                    | Ok DagAdmission.Store ->

                        use cmd = conn.CreateCommand()

                        cmd.CommandText <-
                            """INSERT INTO dag_op_record
            (stream_id, hash, parents_json, op_json, outcome_hash, prompt_id, user_id, timestamp, result_envelope_json, tombstoned)
        VALUES
            (@s, @h, @parents, @op, @outcome, @prompt, @user, @ts, @env, @tomb);"""

                        cmd.Parameters.AddWithValue("@s", record.StreamId) |> ignore
                        cmd.Parameters.AddWithValue("@h", record.Hash) |> ignore

                        cmd.Parameters.AddWithValue("@parents", DagJson.encodeParents record.Parents)
                        |> ignore

                        cmd.Parameters.AddWithValue("@op", codec.EncodeOp record.Op) |> ignore

                        cmd.Parameters.AddWithValue(
                            "@outcome",
                            (match record.OutcomeHash with
                             | Some o -> box o
                             | None -> box DBNull.Value)
                        )
                        |> ignore

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
                        cmd.Parameters.AddWithValue("@user", Actor.encode record.Actor) |> ignore

                        cmd.Parameters.AddWithValue("@ts", record.Timestamp.ToUnixTimeSeconds())
                        |> ignore

                        cmd.Parameters.AddWithValue("@env", ResultEnvelopeJson.encode record.ResultEnvelope)
                        |> ignore

                        cmd.Parameters.AddWithValue("@tomb", (if record.Tombstoned then 1 else 0))
                        |> ignore

                        cmd.ExecuteNonQuery() |> ignore

                    tx.Commit()
                with :? SqliteException as ex when SqliteConnections.isContention ex ->
                    invalidOp (
                        sprintf
                            "SqliteDagSink: could not append record %s to stream '%s' — SQLITE_BUSY (%d) after busy_timeout=%dms: another writer holds the database. This is contention, not corruption — retry the append."
                            record.Hash
                            record.StreamId
                            ex.SqliteErrorCode
                            SqliteConnections.busyTimeoutMs
                    )
            }

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
            async { return DagTopology.reachable (parentsLookup streamId) hash }

        member _.Lca(streamId: string, a: string, b: string) : Async<LcaResult> =
            async { return DagTopology.lca (parentsLookup streamId) a b }

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
                use cmd = conn.CreateCommand()

                // Drop the PAYLOAD (`op_json` → the placeholder), preserve hash +
                // parent links so the chain still verifies.
                //
                // `outcome_hash` is KEPT (Phase 1525).
                // The outcome hash used to be nulled here, which cost the store
                // the one field that tells a pruned merge node from a pruned
                // ordinary one, for no retention gain — it is a 64-hex address,
                // not payload. What it broke was idempotence across a sweep: a
                // re-add of the unchanged record no longer matched what the store
                // held, so it read as a collision under the new check and as a
                // second copy under the old one.
                cmd.CommandText <-
                    """UPDATE dag_op_record
SET op_json = @empty, tombstoned = 1
WHERE stream_id = @s AND hash = @h;"""

                cmd.Parameters.AddWithValue("@empty", emptyBatchJson) |> ignore
                cmd.Parameters.AddWithValue("@s", streamId) |> ignore
                cmd.Parameters.AddWithValue("@h", hash) |> ignore
                return cmd.ExecuteNonQuery() > 0
            }

        member _.Streams() : Async<string list> =
            async {
                use conn = openConnection ()
                use cmd = conn.CreateCommand()
                cmd.CommandText <- "SELECT DISTINCT stream_id FROM dag_op_record;"
                use reader = cmd.ExecuteReader()
                let acc = ResizeArray<string>()

                while reader.Read() do
                    acc.Add(reader.GetString 0)

                return List.ofSeq acc
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
