/// Phase 2393 — the ONE way a test in this repository makes a scratch directory, DECLARED ONCE here and
/// linked into each test project that needs it (the same shape as `tests/corpus-root/CorpusRoot.fs`).
///
/// A test that takes a directory under the temp root and never deletes it leaks one directory per case
/// per run, on every machine that runs the suite; before this phase the validator suite alone had left
/// tens of thousands of them. So the helper owns both halves: it CREATES a uniquely named directory and
/// DELETES it, contents and all, when the scope that asked for it ends.
///
/// A delete that still fails after the retries is RAISED, not swallowed. A swallowed failure is exactly
/// how a suite keeps leaking for months with every run green (an open SQLite pool holding a database
/// file open is the recorded case), so a test whose body succeeded fails on its own leak. When the BODY
/// failed, the cleanup is best-effort and the body's failure is the one reported.
///
/// `TempDirGuardTests` (in `Fuaran.UI.OpStream.Dag.Tests`) fails on any test source outside this file
/// that reaches the temp root directly.
module Fuaran.UI.Testing.TempDir

open System
open System.IO
open System.Threading

/// Every directory this helper creates is named `fuaran-<prefix>-<guid>`, so a stray one is attributable.
[<Literal>]
let RootPrefix = "fuaran-"

let private attempts = 10

/// Deletes `path` recursively, retrying a transient lock (a just-exited child process, a scanner).
/// Returns the last failure when every attempt failed.
let private tryDeleteTree (path: string) : exn option =
    let rec attempt n =
        try
            if Directory.Exists path then
                Directory.Delete(path, true)

            None
        with
        | :? IOException
        | :? UnauthorizedAccessException as e ->
            if n + 1 >= attempts then
                Some e
            else
                Thread.Sleep(25 * (n + 1))
                attempt (n + 1)

    attempt 0

/// A uniquely named directory under the temp root, created on construction and deleted with
/// everything in it on `Dispose`.
[<Sealed>]
type TempDirectory(prefix: string) =
    let path =
        Path.Combine(Path.GetTempPath(), sprintf "%s%s-%s" RootPrefix prefix (Guid.NewGuid().ToString "N"))

    do Directory.CreateDirectory path |> ignore

    /// The directory's absolute path.
    member _.Path = path

    interface IDisposable with
        member _.Dispose() =
            match tryDeleteTree path with
            | None -> ()
            | Some e ->
                raise (IOException(sprintf "the test's temp directory %s could not be deleted (it leaks)" path, e))

/// A fresh directory the caller disposes (`use dir = TempDir.create "name"`).
let create (prefix: string) = new TempDirectory(prefix)

/// Runs `body` over a fresh directory and deletes the directory afterwards, whatever `body` did.
let withTempDir (prefix: string) (body: string -> 'a) : 'a =
    let dir = create prefix

    let result =
        try
            body dir.Path
        with _ ->
            tryDeleteTree dir.Path |> ignore
            reraise ()

    (dir :> IDisposable).Dispose()
    result
