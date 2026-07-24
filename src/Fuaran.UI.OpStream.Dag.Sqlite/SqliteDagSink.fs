namespace Fuaran.UI.OpStream.Dag.Sqlite

open System
open System.Text
open Microsoft.Data.Sqlite
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions

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
//          user_id              TEXT    NOT NULL,
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
// ============================================================================

module private DagJson =

    /// JSON array of parent hashes. Hashes are 64-hex / opaque tokens with no
    /// JSON-special characters, but we quote + escape defensively.
    let encodeParents (parents: string list) : string =
        let esc (s: string) =
            let sb = StringBuilder()
            sb.Append '"' |> ignore

            for ch in s do
                match ch with
                | '"' -> sb.Append "\\\"" |> ignore
                | '\\' -> sb.Append "\\\\" |> ignore
                | c -> sb.Append c |> ignore

            sb.Append '"' |> ignore
            sb.ToString()

        "[" + (parents |> List.map esc |> String.concat ",") + "]"

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

    let encodeEnvelope (envelope: OpResultEnvelope) : string =
        match envelope with
        | OpResultEnvelope.Success -> "{\"$type\":\"Success\"}"
        | OpResultEnvelope.Failure(code, message) ->
            let esc (s: string) =
                s.Replace("\\", "\\\\").Replace("\"", "\\\"")

            sprintf "{\"$type\":\"Failure\",\"code\":\"%s\",\"message\":\"%s\"}" (esc code) (esc message)

    /// Inverse of `encodeEnvelope`. `Success` is the common case; a `Failure`
    /// extracts the two quoted strings (the encoder is the only writer).
    let decodeEnvelope (json: string) : OpResultEnvelope =
        if json = "{\"$type\":\"Success\"}" then
            OpResultEnvelope.Success
        else
            let extract (key: string) =
                let needle = sprintf "\"%s\":\"" key

                match json.IndexOf needle with
                | -1 -> ""
                | start ->
                    let valueStart = start + needle.Length
                    let sb = StringBuilder()
                    let mutable i = valueStart
                    let mutable closed = false

                    while not closed && i < json.Length do
                        let c = json[i]

                        if c = '\\' && i + 1 < json.Length then
                            sb.Append json[i + 1] |> ignore
                            i <- i + 2
                        elif c = '"' then
                            closed <- true
                        else
                            sb.Append c |> ignore
                            i <- i + 1

                    sb.ToString()

            OpResultEnvelope.Failure(extract "code", extract "message")

type SqliteDagSink<'Msg>(connectionString: string, codec: IOpJsonCodec<'Msg>) =

    let emptyBatchJson = codec.EncodeOp(TreeOp.Batch [])

    let openConnection () : SqliteConnection =
        let conn = new SqliteConnection(connectionString)
        conn.Open()
        conn

    let ensureSchema () =
        use conn = openConnection ()
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
);"""

        cmd.ExecuteNonQuery() |> ignore

    do ensureSchema ()

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

    let parentsLookup (streamId: string) : string -> string list =
        let m = loadParentMap streamId
        fun h -> Map.tryFind h m |> Option.defaultValue []

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
                | Error msg -> failwithf "SqliteDagSink: codec failed to decode op at (%s, %s): %s" streamId hash msg

            let outcomeHash = if reader.IsDBNull 2 then None else Some(reader.GetString 2)
            let promptId = if reader.IsDBNull 3 then None else Some(reader.GetString 3)

            Some
                { StreamId = streamId
                  Hash = hash
                  Parents = DagJson.decodeParents (reader.GetString 0)
                  Op = op
                  OutcomeHash = outcomeHash
                  PromptId = promptId
                  UserId = reader.GetString 4
                  Timestamp = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64 5)
                  ResultEnvelope = DagJson.decodeEnvelope (reader.GetString 6)
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
                | Error msg -> failwithf "SqliteDagSink: codec failed to decode op at (%s, %s): %s" streamId hash msg

            let outcomeHash = if reader.IsDBNull 3 then None else Some(reader.GetString 3)
            let promptId = if reader.IsDBNull 4 then None else Some(reader.GetString 4)

            acc.Add
                { StreamId = streamId
                  Hash = hash
                  Parents = DagJson.decodeParents (reader.GetString 1)
                  Op = op
                  OutcomeHash = outcomeHash
                  PromptId = promptId
                  UserId = reader.GetString 5
                  Timestamp = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64 6)
                  ResultEnvelope = DagJson.decodeEnvelope (reader.GetString 7)
                  Tombstoned = reader.GetInt64 8 <> 0L }

        List.ofSeq acc

    interface IDagOpStreamSink<'Msg> with

        member _.Add(record: DagOpRecord<'Msg>) : Async<unit> =
            async {
                use conn = openConnection ()
                use cmd = conn.CreateCommand()

                cmd.CommandText <-
                    """INSERT INTO dag_op_record
    (stream_id, hash, parents_json, op_json, outcome_hash, prompt_id, user_id, timestamp, result_envelope_json, tombstoned)
VALUES
    (@s, @h, @parents, @op, @outcome, @prompt, @user, @ts, @env, @tomb)
ON CONFLICT(stream_id, hash) DO NOTHING;"""

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

                cmd.Parameters.AddWithValue("@user", record.UserId) |> ignore

                cmd.Parameters.AddWithValue("@ts", record.Timestamp.ToUnixTimeSeconds())
                |> ignore

                cmd.Parameters.AddWithValue("@env", DagJson.encodeEnvelope record.ResultEnvelope)
                |> ignore

                cmd.Parameters.AddWithValue("@tomb", (if record.Tombstoned then 1 else 0))
                |> ignore

                let affected = cmd.ExecuteNonQuery()

                // Content addressing: an existing row with the same hash must
                // carry identical content (parents + outcome). A differing one
                // is a collision defect.
                if affected = 0 then
                    use check = conn.CreateCommand()

                    check.CommandText <-
                        "SELECT parents_json, outcome_hash FROM dag_op_record WHERE stream_id=@s AND hash=@h;"

                    check.Parameters.AddWithValue("@s", record.StreamId) |> ignore
                    check.Parameters.AddWithValue("@h", record.Hash) |> ignore
                    use reader = check.ExecuteReader()

                    if reader.Read() then
                        let existingParents = DagJson.decodeParents (reader.GetString 0)
                        let existingOutcome = if reader.IsDBNull 1 then None else Some(reader.GetString 1)

                        if existingParents <> record.Parents || existingOutcome <> record.OutcomeHash then
                            invalidOp (
                                sprintf
                                    "SqliteDagSink: hash collision at %s with differing content — content addressing violated."
                                    record.Hash
                            )
            }

        member _.TryGet(streamId: string, hash: string) : Async<DagOpRecord<'Msg> option> =
            async { return readRecord streamId hash }

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
            }

        member _.Parents(streamId: string, hash: string) : Async<string list> =
            async { return parentsLookup streamId hash }

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
            async { return readAllRecords streamId }

        member _.Tombstone(streamId: string, hash: string) : Async<bool> =
            async {
                use conn = openConnection ()
                use cmd = conn.CreateCommand()

                cmd.CommandText <-
                    """UPDATE dag_op_record
SET op_json = @empty, outcome_hash = NULL, tombstoned = 1
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
    /// Fresh sink as the abstraction interface.
    let create<'Msg> (connectionString: string) (codec: IOpJsonCodec<'Msg>) : IDagOpStreamSink<'Msg> =
        upcast SqliteDagSink<'Msg>(connectionString, codec)
