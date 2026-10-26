/// Phase 2393 — the guard that keeps the temp-directory leak fixed.
///
/// A test that reaches the temp root directly, instead of through `tests/temp-dir/TempDir.fs`, is how
/// the suites came to leave a directory behind per case per run. This suite scans every TEST source in
/// the repository (each `src/*.Tests` / `src/*.TestSupport` project and `tests/`) and fails on any file
/// outside the helper that names a temp-root API. It runs here, in one suite, because the scan is
/// repository-wide; the helper itself is linked into each suite that uses it.
///
/// Production sources are out of its scope: a library may legitimately use the temp root, and its
/// lifetime rules are its own.
module Fuaran.UI.OpStream.Dag.Tests.TempDirGuardTests

open System.IO
open System.Text.RegularExpressions
open Expecto

let private repoRoot =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))

/// The temp-root APIs. Assembled from fragments so this file's own text does not match.
let private tempRootApi =
    let t = "Temp"

    Regex(sprintf @"\bGet%sPath\b|\bGet%sFileName\b|\bCreate%sSubdirectory\b" t t t, RegexOptions.Compiled)

/// The one file allowed to name them.
let private helper = "tests/temp-dir/TempDir.fs"

/// Callers that predate the helper, in projects outside Phase 2393's footprint. Each deletes what it
/// creates through its own `finally`, so none of them reaches into the temp root without cleaning up —
/// except where noted, which is reported against the phase rather than fixed outside its files. This
/// list may only SHRINK: an entry that no longer names a temp-root API fails the third test, so moving
/// a file onto the helper means deleting its line here in the same change.
let private grandfathered =
    set
        [ // Fixed-name files (`fuaran-cli-good.json`, `fuaran-cli-bad.json`) are rewritten, not deleted.
          "src/Fuaran.UI.Cli.Tests/CliTests.fs"
          "src/Fuaran.UI.OpStream.Contract.Tests/Cells.fs"
          // The SQLite cases here delete best-effort behind an open connection pool, the leak `TempDb`
          // fixes in this suite.
          "src/Fuaran.UI.OpStream.Tests/ActionInvocationTests.fs"
          "src/Fuaran.UI.OpStream.Tests/CheckpointTests.fs"
          "src/Fuaran.UI.OpStream.Tests/CodecSeamTests.fs"
          "src/Fuaran.UI.OpStream.Tests/PersistenceLawsTests.fs"
          "src/Fuaran.UI.OpStream.Tests/SqliteTests.fs"
          "src/Fuaran.UI.OpStream.Tests/VerifyOnReadTests.fs"
          "src/Fuaran.UI.OpStream.Tests/WritePathIntegrityTests.fs"
          "src/Fuaran.UI.Site.Tests/ExportTests.fs"
          "src/Fuaran.UI.Tests/IdlCertificationSupport.fs" ]

/// The lines of `text` that name a temp-root API, 1-based.
let offendingLines (text: string) : int list =
    text.Split('\n')
    |> Array.indexed
    |> Array.choose (fun (i, line) -> if tempRootApi.IsMatch line then Some(i + 1) else None)
    |> Array.toList

let private isTestSource (rel: string) =
    let segments = rel.Split('/')

    let generated =
        segments |> Array.exists (fun s -> s = "bin" || s = "obj" || s = "node_modules")

    not generated
    && (segments.[0] = "tests"
        || (segments.[0] = "src"
            && segments.Length > 2
            && (segments.[1].EndsWith ".Tests" || segments.[1].EndsWith ".TestSupport")))

/// Every test source in the repository, repo-relative and forward-slashed.
let private testSources () : string list =
    [ for dir in [ "src"; "tests" ] do
          let root = Path.Combine(repoRoot, dir)

          if Directory.Exists root then
              for f in Directory.EnumerateFiles(root, "*.fs", SearchOption.AllDirectories) do
                  let rel =
                      Path.GetRelativePath(repoRoot, f).Replace(Path.DirectorySeparatorChar, '/')

                  if isTestSource rel then
                      yield rel ]

/// `(file, lines)` for every test source that names a temp-root API.
let private callers () : (string * int list) list =
    testSources ()
    |> List.choose (fun rel ->
        match offendingLines (File.ReadAllText(Path.Combine(repoRoot, rel))) with
        | [] -> None
        | lines -> Some(rel, lines))

[<Tests>]
let tests =
    testList
        "Phase 2393 — tests reach the temp root only through TempDir"
        [ test "the matcher sees each temp-root API and nothing else" {
              let t = "Temp"
              Expect.equal (offendingLines (sprintf "let p = Path.Get%sPath()" t)) [ 1 ] "the path API"
              Expect.equal (offendingLines (sprintf "x\nlet f = Path.Get%sFileName()" t)) [ 2 ] "the file API"

              Expect.equal
                  (offendingLines (sprintf "Directory.Create%sSubdirectory \"x\"" t))
                  [ 1 ]
                  "the subdirectory API"

              Expect.isEmpty (offendingLines "use tmp = TempDir.create \"x\"") "the helper's own call shape"
          }

          test "the scan reaches the helper, so a quiet result is not a vacuous one" {
              let found = callers () |> List.map fst
              Expect.contains found helper "the helper is a test source the scan reads"
          }

          test "no test source outside the helper names a temp-root API" {
              let offenders =
                  callers ()
                  |> List.filter (fun (rel, _) -> rel <> helper && not (grandfathered.Contains rel))

              Expect.isEmpty
                  offenders
                  (sprintf
                      "these test sources reach the temp root directly; take the directory from TempDir (`use tmp = TempDir.create \"name\"` or `TempDir.withTempDir`), which deletes it: %A"
                      offenders)
          }

          test "every grandfathered caller still is one (the list only shrinks)" {
              let live = callers () |> List.map fst |> Set.ofList
              let stale = grandfathered - live

              Expect.isEmpty
                  stale
                  (sprintf "these files no longer reach the temp root; delete their grandfathered entries: %A" stale)
          } ]
