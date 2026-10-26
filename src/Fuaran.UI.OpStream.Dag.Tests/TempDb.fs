/// Phase 2393 — a SQLite database file for one test, in its own scratch directory.
///
/// Microsoft.Data.Sqlite keeps a pooled connection to every database it has opened, and on Windows that
/// open handle refuses the delete. The suites used to delete their `.db` files best-effort and swallow
/// the refusal, so every SQLite case leaked its database file on every run while reading green. Here the
/// pools are cleared first, and the directory then goes the way every other scratch directory goes —
/// through `TempDir`, which raises rather than leaks.
module Fuaran.UI.OpStream.Dag.Tests.TempDb

open System
open System.IO
open Microsoft.Data.Sqlite
open Fuaran.UI.Testing

/// `store.db` inside a fresh `fuaran-<name>-<guid>` directory; `Dispose` releases the connection pools
/// and deletes the directory with the database and any sidecar files beside it.
[<Sealed>]
type TempDb(name: string) =
    let dir = TempDir.create name

    /// The database file's absolute path (the file itself is created by whoever opens it).
    member _.Path = Path.Combine(dir.Path, "store.db")

    interface IDisposable with
        member _.Dispose() =
            SqliteConnection.ClearAllPools()
            (dir :> IDisposable).Dispose()

/// A fresh database the caller disposes (`use db = TempDb.create "name"`).
let create (name: string) = new TempDb(name)

/// Runs `body` over a fresh database path and deletes it afterwards.
let withTempDb (name: string) (body: string -> 'a) : 'a =
    use db = create name
    body db.Path
