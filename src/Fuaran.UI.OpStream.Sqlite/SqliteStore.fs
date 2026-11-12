namespace Fuaran.UI.OpStream.SqliteStore

open System
open System.Globalization
open Microsoft.Data.Sqlite
open Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  SqliteStore — the plumbing every SQLite sink in the op-stream tier shares
//  (Phase 1937): opening a connection, the database's journal mode, telling
//  contention from a failure, and the result-envelope column codec.
//
//  ONE SOURCE FILE, COMPILED INTO TWO ASSEMBLIES. `Fuaran.UI.OpStream.Sqlite`
//  (the linear sink and the action log) and `Fuaran.UI.OpStream.Dag.Sqlite` (the
//  DAG sink) do not reference each other, and neither should: a linear package
//  must not come to depend on a DAG one, and a shared package for a few dozen
//  lines of plumbing would be a public surface nobody asked for. So this file
//  is linked into both projects, and everything in it is `internal` — each
//  assembly carries its own private copy of the SAME text, and no type here is
//  on either package's surface. Before, the three sinks carried three copies
//  that had drifted: the action log set neither `busy_timeout` nor WAL (the
//  Phase 1525 M-C1 fix reached the other two), the linear sink caught SQLITE_BUSY
//  but not SQLITE_LOCKED, and the DAG sink's envelope decoder was the
//  unhardened copy of the linear one.
// ============================================================================

/// Connections, journal mode and contention, as every SQLite sink here opens them.
module internal SqliteConnections =

    /// How long a connection waits for another writer's lock before SQLite gives
    /// up (Phase 1525, M-C1). The default is ZERO: a second connection that finds
    /// the write lock held fails at once rather than waiting out a transaction
    /// about to commit, so an ordinary two-writer moment reads as a failure. Five
    /// seconds absorbs a commit and still reports a writer that is truly stuck.
    let busyTimeoutMs = 5000

    /// Open a connection with `busy_timeout` set. The pragma is CONNECTION-scoped,
    /// so it is set on every connection a sink opens; set once at schema time it
    /// would protect only the schema connection.
    let openConnection (connectionString: string) : SqliteConnection =
        let conn = new SqliteConnection(connectionString)
        conn.Open()
        use pragma = conn.CreateCommand()
        pragma.CommandText <- sprintf "PRAGMA busy_timeout = %d;" busyTimeoutMs
        pragma.ExecuteNonQuery() |> ignore
        conn

    /// Put the database file in WAL mode. Journal mode is a property of the FILE
    /// and persists, so a sink does this once, from its schema step. Under the
    /// default rollback journal a reader and a writer exclude each other outright
    /// (a verifying read blocks a compare-and-append, and the reverse); under WAL
    /// they do not. An in-memory or shared-cache database cannot take WAL, and
    /// SQLite reports the mode it adopted rather than failing, so the answer is
    /// read and ignored: refusing such a database over a journal preference would
    /// break every in-memory host for no integrity gain.
    let useWriteAheadLog (conn: SqliteConnection) : unit =
        use cmd = conn.CreateCommand()
        cmd.CommandText <- "PRAGMA journal_mode = WAL;"
        cmd.ExecuteScalar() |> ignore

    /// `true` when `ex` is SQLite's contention signal: SQLITE_BUSY (5), or its
    /// sibling SQLITE_LOCKED (6), raised once `busy_timeout` has elapsed with the
    /// lock still held. Not a corrupt store and not a programming error: another
    /// writer holds the database right now.
    let isContention (ex: SqliteException) : bool =
        ex.SqliteErrorCode = 5 || ex.SqliteErrorCode = 6

/// The `result_envelope_json` column. Hand-rolled, no JSON library: the shape is
/// closed and the sink is its only writer, so a small reader is enough. Both
/// sinks write the same bytes and read them through this one decoder.
module internal ResultEnvelopeJson =

    /// The column for one envelope. A string is spelled through Core's canonical
    /// escape (Phase 2026), so a control character is `\u00xx` and the column is
    /// valid JSON.
    let encode (envelope: OpResultEnvelope) : string =
        match envelope with
        | OpResultEnvelope.Success -> "{\"$type\":\"Success\"}"
        | OpResultEnvelope.Failure(code, message) ->
            "{\"$type\":\"Failure\",\"code\":"
            + Fuaran.Core.JsonString.quote code
            + ",\"message\":"
            + Fuaran.Core.JsonString.quote message
            + "}"

    /// The envelope a column holds, or the reason it is not one. A shape the
    /// writer never produced is an `Error` naming it, never a `Failure("", "")`.
    let decode (json: string) : Result<OpResultEnvelope, string> =
        if json = "{\"$type\":\"Success\"}" then
            Ok OpResultEnvelope.Success
        // Phase 1525 (L-A9): ORDINAL. These are structural tokens this module
        // wrote, not human text; a culture-sensitive comparison can fold, ignore
        // or reorder characters, so under some installed culture the same bytes
        // would match differently.
        elif json.StartsWith("{\"$type\":\"Failure\"", StringComparison.Ordinal) then
            let extractString (key: string) : string option =
                let needle = sprintf "\"%s\":\"" key

                // The escape-reading loop, from the value's first character. A raw
                // control character (a row written before Phase 2026) is taken as
                // it stands; `\uXXXX` is the writer's spelling since then.
                let readEscaped (valueStart: int) : string option =
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

                match json.IndexOf(needle, StringComparison.Ordinal) with
                | -1 -> None
                | start ->
                    let valueStart = start + needle.Length
                    // Phase 2028: a value with no backslash before its closing quote
                    // is the substring between them, with no builder. Anything else
                    // takes the escape-reading loop from the top.
                    let quote = json.IndexOf('"', valueStart)

                    if quote >= 0 && json.IndexOf('\\', valueStart, quote - valueStart) < 0 then
                        Some(json.Substring(valueStart, quote - valueStart))
                    else
                        readEscaped valueStart

            match extractString "code", extractString "message" with
            | Some code, Some message -> Ok(OpResultEnvelope.Failure(code, message))
            | _ -> Error "OpResultEnvelope.Failure: missing code or message field"
        else
            Error(sprintf "Unknown OpResultEnvelope shape: %s" json)
