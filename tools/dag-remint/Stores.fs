namespace Fuaran.UI.OpStream.Dag.Remint

open System
open System.IO
open Microsoft.Data.Sqlite
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.Sqlite

// ============================================================================
//  Stores — the two store forms a pre-2645 DAG was kept in, re-minted
//  (Phase 2648).
//
//  SQLite (`SqliteDagSink` before the cut: `dag_op_record` + `dag_head`). The
//  re-mint reads the old tables, writes the re-minted store to `<db>.remint`
//  through the cut's own `SqliteDagSink` (schema version 1: `dag_node`,
//  `dag_skeleton`, `dag_head`), reads it back verified, and only then replaces
//  the database, keeping the original beside it as `<db>.pre-2645` — the
//  rollback. A database that already holds the cut's schema and no pre-cut table
//  is verified and left untouched (the re-mint is idempotent).
//
//  Wire (`DagWire.encodeRecord` before the cut, one record per line). The
//  re-minted live records are written in the cut's wire form, one per line; the
//  skeleton has no wire form and is reported in the receipt. A file whose every
//  line is already a post-cut record that verifies is left as it is.
// ============================================================================

/// Why a store could not be re-minted.
[<RequireQualifiedAccess>]
type StoreFault =
    /// The re-mint itself refused (the record named).
    | Remint of RemintFault
    /// The store could not be read as either generation.
    | Unreadable of reason: string
    /// The store mixes pre-cut and post-cut records.
    | MixedGeneration of reason: string
    /// The re-minted store did not read back verified (a defect in the tool).
    | ReadBackFailed of reason: string

module StoreFault =
    let describe (f: StoreFault) : string =
        match f with
        | StoreFault.Remint r -> RemintFault.describe r
        | StoreFault.Unreadable why -> "the store cannot be read: " + why
        | StoreFault.MixedGeneration why -> "the store mixes pre-2645 and post-2645 records: " + why
        | StoreFault.ReadBackFailed why -> "the re-minted store did not read back verified: " + why

module SqliteRemint =

    let private connect (path: string) : SqliteConnection =
        let c = new SqliteConnection(sprintf "Data Source=%s;Pooling=False" path)
        c.Open()
        c

    let private hasTable (conn: SqliteConnection) (name: string) : bool =
        use cmd = conn.CreateCommand()
        cmd.CommandText <- "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = @n;"
        cmd.Parameters.AddWithValue("@n", name) |> ignore
        not (isNull (cmd.ExecuteScalar()))

    let private parentsOf (json: string) : string list =
        use doc = System.Text.Json.JsonDocument.Parse json

        [ for p in doc.RootElement.EnumerateArray() -> p.GetString() |> Option.ofObj |> Option.defaultValue "" ]

    let private parentsJson (parents: string list) : string =
        "["
        + (parents |> List.map Fuaran.Core.JsonString.quote |> String.concat ",")
        + "]"

    /// The pre-cut rows and heads, decoded through `codec`.
    let readLegacy
        (codec: IOpJsonCodec<'Msg>)
        (path: string)
        : Result<LegacyRecord<'Msg> list * Map<string, string>, StoreFault> =
        use conn = connect path

        let rows =
            use cmd = conn.CreateCommand()

            cmd.CommandText <-
                "SELECT stream_id, hash, parents_json, op_json, outcome_hash, prompt_id, user_id, timestamp, result_envelope_json, tombstoned FROM dag_op_record ORDER BY stream_id, hash;"

            use reader = cmd.ExecuteReader()

            [ while reader.Read() do
                  let text (i: int) =
                      if reader.IsDBNull i then None else Some(reader.GetString i)

                  let streamId = reader.GetString 0
                  let hash = reader.GetString 1

                  let decoded =
                      match
                          Legacy.decodeOp codec (reader.GetString 3),
                          Legacy.decodeActor (reader.GetString 6),
                          Legacy.decodeEnvelope (reader.GetString 8)
                      with
                      | Ok op, Ok actor, Ok envelope ->
                          Ok
                              { StreamId = streamId
                                Hash = hash
                                Parents = parentsOf (reader.GetString 2)
                                Op = op
                                OutcomeHash = text 4
                                PromptId = text 5
                                Actor = actor
                                Timestamp = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64 7)
                                ResultEnvelope = envelope
                                Tombstoned = reader.GetInt64 9 <> 0L }
                      | Error e, _, _
                      | _, Error e, _
                      | _, _, Error e ->
                          Error(StoreFault.Unreadable(sprintf "stream '%s' record %s: %s" streamId hash e))

                  yield decoded ]

        let heads =
            if hasTable conn "dag_head" then
                use cmd = conn.CreateCommand()
                cmd.CommandText <- "SELECT stream_id, head FROM dag_head;"
                use reader = cmd.ExecuteReader()

                [ while reader.Read() do
                      yield reader.GetString 0, reader.GetString 1 ]
                |> Map.ofList
            else
                Map.empty

        match
            rows
            |> List.tryPick (fun r ->
                match r with
                | Error e -> Some e
                | Ok _ -> None)
        with
        | Some fault -> Error fault
        | None -> Ok(rows |> List.choose Result.toOption, heads)

    /// Every live record of a post-cut store, read back through the cut's sink
    /// (verification on), and the heads it records.
    let private readCurrent
        (codec: IOpJsonCodec<'Msg>)
        (path: string)
        : Result<(string * DagOpRecord<'Msg> list * DagSkeletonNode list * string option) list, StoreFault> =
        try
            let sink =
                SqliteDagSink.createWith<'Msg> LoadVerification.Full (sprintf "Data Source=%s;Pooling=False" path) codec

            let streams = sink.Streams() |> Async.RunSynchronously

            let read =
                streams
                |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))
                |> List.map (fun s ->
                    s,
                    sink.Records s |> Async.RunSynchronously,
                    sink.Skeleton s |> Async.RunSynchronously,
                    sink.Head s |> Async.RunSynchronously)

            let unverified =
                read
                |> List.collect (fun (_, rs, _, _) -> rs)
                |> List.tryFind (fun r -> DagOpRecord.recomputeHash r <> r.Hash)

            match unverified with
            | Some r -> Error(StoreFault.ReadBackFailed(sprintf "record %s does not recompute under Dag.nodeId" r.Hash))
            | None -> Ok read
        with :? InvalidOperationException as e ->
            Error(StoreFault.ReadBackFailed e.Message)

    let private writeCurrent (codec: IOpJsonCodec<'Msg>) (path: string) (reminted: Reminted<'Msg>) =
        let sink =
            SqliteDagSink.create<'Msg> (sprintf "Data Source=%s;Pooling=False" path) codec

        // The skeleton first: the live records that name a pruned parent are
        // admitted against it. It has no sink verb (a sink prunes a LIVE node),
        // so it is written into the schema-1 table the sink just created.
        do
            use conn = connect path
            use tx = conn.BeginTransaction()

            for n in reminted.Skeleton do
                use cmd = conn.CreateCommand()
                cmd.Transaction <- tx
                cmd.CommandText <- "INSERT INTO dag_skeleton (stream_id, hash, parents_json) VALUES (@s, @h, @p);"
                cmd.Parameters.AddWithValue("@s", n.StreamId) |> ignore
                cmd.Parameters.AddWithValue("@h", n.Hash) |> ignore
                cmd.Parameters.AddWithValue("@p", parentsJson n.Parents) |> ignore
                cmd.ExecuteNonQuery() |> ignore

            tx.Commit()

        for r in reminted.Records do
            sink.Add r |> Async.RunSynchronously

        for s in reminted.Receipt.Streams do
            match s.Heads with
            | [ (_, head) ] -> sink.TryAdvanceHead(s.StreamId, None, head) |> Async.RunSynchronously |> ignore
            | _ -> ()

        // One file, no write-ahead log beside it, before it replaces the original.
        use conn = connect path
        use cmd = conn.CreateCommand()
        cmd.CommandText <- "PRAGMA journal_mode = DELETE;"
        cmd.ExecuteScalar() |> ignore

    /// Re-mint the SQLite DAG store at `path` in place. Idempotent: a store at the
    /// cut's schema is verified and reported `AlreadyCurrent`, and nothing is written.
    let remint
        (options: RemintOptions<'Msg>)
        (codec: IOpJsonCodec<'Msg>)
        (path: string)
        : Result<RemintReceipt, StoreFault> =
        if not (File.Exists path) then
            Error(StoreFault.Unreadable(sprintf "no database at %s" path))
        else
            let legacy, current =
                use conn = connect path
                // Fold any write-ahead log into the file before it is read or replaced.
                use cmd = conn.CreateCommand()
                cmd.CommandText <- "PRAGMA wal_checkpoint(TRUNCATE);"
                cmd.ExecuteScalar() |> ignore
                hasTable conn "dag_op_record", hasTable conn "dag_schema"

            match legacy, current with
            | true, true ->
                Error(
                    StoreFault.MixedGeneration "it holds both the pre-cut dag_op_record table and the cut's dag_schema"
                )
            | false, false -> Error(StoreFault.Unreadable "it holds neither a pre-cut nor a post-cut DAG table")
            | false, true ->
                readCurrent codec path
                |> Result.map (fun read ->
                    { AlreadyCurrent = true
                      Streams =
                        read
                        |> List.map (fun (s, rs, sk, head) ->
                            { StreamId = s
                              Heads = head |> Option.map (fun h -> h, h) |> Option.toList
                              Nodes = List.length rs
                              Merges = rs |> List.filter (fun r -> List.length r.Parents > 1) |> List.length
                              Skeleton = List.length sk })
                      Addresses = Map.empty })
            | true, false ->
                match readLegacy codec path with
                | Error fault -> Error fault
                | Ok(records, heads) ->
                    match Remint.records options heads records with
                    | Error fault -> Error(StoreFault.Remint fault)
                    | Ok reminted ->
                        let staged = path + ".remint"

                        for f in [ staged; staged + "-wal"; staged + "-shm" ] do
                            if File.Exists f then
                                File.Delete f

                        writeCurrent codec staged reminted

                        match readCurrent codec staged with
                        | Error fault -> Error fault
                        | Ok read ->
                            let liveBack = read |> List.sumBy (fun (_, rs, _, _) -> List.length rs)
                            let skeletonBack = read |> List.sumBy (fun (_, _, sk, _) -> List.length sk)

                            if
                                liveBack <> List.length reminted.Records
                                || skeletonBack <> List.length reminted.Skeleton
                            then
                                Error(
                                    StoreFault.ReadBackFailed(
                                        sprintf
                                            "wrote %d live and %d pruned nodes, read back %d and %d"
                                            (List.length reminted.Records)
                                            (List.length reminted.Skeleton)
                                            liveBack
                                            skeletonBack
                                    )
                                )
                            else
                                SqliteConnection.ClearAllPools()
                                File.Replace(staged, path, path + ".pre-2645")

                                for f in [ path + "-wal"; path + "-shm" ] do
                                    if File.Exists f then
                                        File.Delete f

                                Ok reminted.Receipt

module WireRemint =

    /// Re-mint a pre-cut wire record set (one `DagWire` record per line). Returns
    /// the cut's wire lines and the receipt; a set already at the cut is returned
    /// unchanged, `AlreadyCurrent`.
    let remint
        (options: RemintOptions<'Msg>)
        (codec: IOpJsonCodec<'Msg>)
        (lines: string list)
        : Result<string list * Reminted<'Msg>, StoreFault> =
        let lines = lines |> List.filter (fun l -> not (String.IsNullOrWhiteSpace l))
        let legacy = lines |> List.map (Legacy.decodeWire codec)
        let current = lines |> List.map (DagWire.decodeRecord (Legacy.decodeOp codec))

        let oks rs = rs |> List.choose Result.toOption

        let firstError rs =
            rs
            |> List.tryPick (fun r ->
                match r with
                | Error e -> Some e
                | Ok _ -> None)

        if not lines.IsEmpty && List.forall Result.isOk current then
            let records = oks current

            match records |> List.tryFind (fun r -> DagOpRecord.recomputeHash r <> r.Hash) with
            | Some r ->
                Error(StoreFault.Unreadable(sprintf "post-cut record %s does not recompute under Dag.nodeId" r.Hash))
            | None ->
                let streams =
                    records |> List.map (fun r -> r.StreamId) |> List.distinct |> List.sort

                Ok(
                    lines,
                    { Records = records
                      Skeleton = []
                      Receipt =
                        { AlreadyCurrent = true
                          Streams =
                            streams
                            |> List.map (fun s ->
                                let mine = records |> List.filter (fun r -> r.StreamId = s)
                                let parented = mine |> List.collect (fun r -> r.Parents) |> Set.ofList

                                { StreamId = s
                                  Heads =
                                    mine
                                    |> List.map (fun r -> r.Hash)
                                    |> List.filter (parented.Contains >> not)
                                    |> List.map (fun h -> h, h)
                                  Nodes = List.length mine
                                  Merges = mine |> List.filter (fun r -> List.length r.Parents > 1) |> List.length
                                  Skeleton = 0 })
                          Addresses = Map.empty } }
                )
        elif List.forall Result.isOk legacy then
            match Remint.records options Map.empty (oks legacy) with
            | Error fault -> Error(StoreFault.Remint fault)
            | Ok reminted -> Ok(reminted.Records |> List.map (DagWire.encodeRecord codec.EncodeOp), reminted)
        elif List.exists Result.isOk current && List.exists Result.isOk legacy then
            Error(StoreFault.MixedGeneration "some lines are pre-cut records and some are the cut's")
        else
            Error(StoreFault.Unreadable(firstError legacy |> Option.defaultValue "no records"))
