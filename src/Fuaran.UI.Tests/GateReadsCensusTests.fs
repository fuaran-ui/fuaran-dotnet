module Fuaran.UI.Tests.GateReadsCensusTests

// WHAT THIS REPOSITORY'S GATE READS — `gate-reads.json`, checked rather than trusted.
//
// A tool that lands changes may stand on an earlier green run of `run.ps1` across later commits only
// when none of them touched a file the gate reads. The record at the repository root is where that set
// is written down for a reader outside this repository: `reads` names the non-code files the gate
// opens beyond the code-shaped default (sources, tests, proofs, build and project files, the gate
// scripts), and `notRead` waives, with a reason, a file the census below flags that a human has
// confirmed is not opened. A record nobody checks decays one convenient omission at a time, and its
// failure mode is the permissive one — a commit to a file the gate reads crossed without a gate run —
// so it is held here in two halves.
//
// 1. THE GATE. `run.ps1` runs more than the suites: the roster in `test-suites.json`, the Fable stage
//    (`tests/fable-laws/fable-check.ps1`), the validator-coverage check over `validator-coverage.json`,
//    the defect-code check (`scripts/fuaran-codes.ps1`), the browser-script harness and the renderer
//    writer's self-test (`scripts/sync-renderer-web.ps1`), and the generated-artefact drift checks
//    (`Build.fsproj -- DriftChecks`, which reconciles the authoring pack under `docs/` through
//    `docs/tools/authoring-pack.fsx`). The test below pins that set of stages and the inputs each
//    names, so a stage added to the gate turns it red until the record is re-examined. No stage sweeps
//    the tracked set.
//
// 2. THE SUITE. The record lists what the suites open, checked by a STATIC census of their own sources
//    and of the drift checks' scripts (`Build.fs`, `docs/tools/*.fsx`): a source reaches the real
//    repository only through a root locator (`repoRoot`, the walk to the solution file, the
//    authoring pack's `docsDir` / `packDir`, `__SOURCE_DIRECTORY__`, or a directory walk over
//    `d.FullName`), so in every file that names one, every quoted string that is a candidate's file
//    name or repo-relative path, and every `Path.Combine(<locator>, "a", "b", ...)` prefix, counts as a
//    read. It over-reports by design, and each over-report is settled by a reviewed `notRead` entry.
//    The wire-format corpus most suites read sits OUTSIDE this repository, so no commit here moves it
//    and no entry here can name it.
//
// The census walks the file system rather than asking git for the tracked set: an untracked file
// only ever widens the candidates.

open System
open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Expecto

let private solutionName = "Fuaran.sln"
let private recordName = "gate-reads.json"

/// This file's own name: excluded from its own census, because its go-red fixtures quote paths on
/// purpose.
let private selfName = "GateReadsCensusTests.fs"

let private repoRoot =
    lazy
        (let rec walk (d: DirectoryInfo | null) =
            match d with
            | null -> failtestf "could not locate the repository root (no %s above the test assembly)" solutionName
            | d when File.Exists(Path.Combine(d.FullName, solutionName)) -> d.FullName
            | d -> walk d.Parent

         walk (DirectoryInfo(AppContext.BaseDirectory)))

// ── the path grammar the record is read with ─────────────────────────────────────────────────────────

/// Does `path` fall under `spec`? A spec with no `*` names a file or a directory and matches the path
/// itself or anything beneath it; a spec with wildcards is a glob in which `**` crosses `/` and `*`
/// does not. Ordinal, never case-folded: folding can only widen a match.
let matches (spec: string) (path: string) : bool =
    let s = spec.Trim().Replace('\\', '/').Trim('/')
    let p = path.Trim().Replace('\\', '/').TrimStart('/')

    if s = "" || p = "" then
        false
    elif not (s.Contains "*") then
        p = s || p.StartsWith(s + "/", StringComparison.Ordinal)
    else
        let pattern =
            Regex.Escape(s).Replace(@"\*\*", "\u0000").Replace(@"\*", "[^/]*").Replace("\u0000", ".*")

        Regex.IsMatch(p, "^" + pattern + "$")

let matchesAny (specs: string list) (path: string) =
    specs |> List.exists (fun s -> matches s path)

/// Code-shaped paths every reader of the record treats as gate inputs already, so they are never
/// census candidates. Kept deliberately NARROWER than any reader's default: a narrower list only
/// makes more files candidates, which is the restrictive direction.
let private implied =
    [ "src/**"
      "tests/**"
      "proofs/**"
      ".config/**"
      solutionName
      "*.props"
      "**/*.props"
      "Build.fs"
      "Build.fsproj"
      "global.json"
      "nuget.config"
      ".editorconfig"
      ".fantomasignore"
      "run.ps1"
      recordName ]

// ── the record ───────────────────────────────────────────────────────────────────────────────────────

type private Record =
    { Kind: string
      Reads: string list
      NotRead: (string * string) list }

let private parseRecord (text: string) : Record =
    use doc = JsonDocument.Parse text
    let root = doc.RootElement

    let str (e: JsonElement) (name: string) =
        match e.TryGetProperty name with
        | true, v when v.ValueKind = JsonValueKind.String -> (string (v.GetString())).Trim()
        | _ -> ""

    let arr (name: string) =
        match root.TryGetProperty name with
        | true, a when a.ValueKind = JsonValueKind.Array -> [ for e in a.EnumerateArray() -> e ]
        | _ -> []

    { Kind = str root "kind"
      Reads =
        arr "reads"
        |> List.filter (fun e -> e.ValueKind = JsonValueKind.String)
        |> List.map (fun e -> (string (e.GetString())).Trim())
      NotRead =
        arr "notRead"
        |> List.filter (fun e -> e.ValueKind = JsonValueKind.Object)
        |> List.map (fun e -> str e "path", str e "reason") }

let private record =
    lazy
        (let p = Path.Combine(repoRoot.Value, recordName)

         if not (File.Exists p) then
             failtestf "this repository records what its gate reads: %s is missing" p

         parseRecord (File.ReadAllText p))

// ── the census (PURE over (source path, text) — the go-red runs THIS) ──────────────────────────────

let private locator =
    Regex(@"[rR]epoRoot|docsDir|packDir|__SOURCE_DIRECTORY__|Fuaran\.sln\b|\b(d|dir)\.FullName", RegexOptions.Compiled)

let private literal = Regex(@"""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled)

let private combine =
    Regex(@"Path\.Combine\(([^()]*(?:\([^()]*\)[^()]*)*)\)", RegexOptions.Compiled)

let private norm (s: string) =
    s.Replace(@"\\", "/").Replace('\\', '/').Trim('/')

/// Which of `candidates` (repo-relative, `/`-separated) the `sources` may read, each with the source
/// files that name it. Only a source mentioning a root locator is read at all.
let census (sources: (string * string) list) (candidates: string list) : (string * string list) list =
    let perSource =
        sources
        |> List.filter (fun (_, text) -> locator.IsMatch text)
        |> List.map (fun (rel, text) ->
            let lits =
                [ for m in literal.Matches text -> norm m.Groups.[1].Value ]
                |> List.filter (fun s -> s <> "")
                |> Set.ofList

            let prefixes =
                [ for m in combine.Matches text do
                      let args = m.Groups.[1].Value

                      if locator.IsMatch(args.Split(',').[0]) then
                          match
                              [ for p in literal.Matches args -> norm p.Groups.[1].Value ]
                              |> List.filter (fun p -> p <> "")
                          with
                          | [] -> ()
                          | ps -> yield String.Join("/", ps) ]

            rel, lits, prefixes)

    candidates
    |> List.choose (fun c ->
        let name = c.Substring(c.LastIndexOf('/') + 1)

        let by =
            perSource
            |> List.filter (fun (_, lits, prefixes) ->
                lits.Contains name
                || lits.Contains c
                || prefixes
                   |> List.exists (fun p -> c = p || c.StartsWith(p + "/", StringComparison.Ordinal)))
            |> List.map (fun (rel, _, _) -> rel)

        if List.isEmpty by then None else Some(c, by))

// ── the real repository ──────────────────────────────────────────────────────────────────────────────

let private skippedDirs =
    set [ ".git"; "bin"; "obj"; ".vs"; "node_modules"; "fable_modules"; "TestResults" ]

/// Every file under the root but git's own: `.git` is a directory in a clone and a FILE in a linked
/// worktree, and in neither is it something a commit can change.
let private files =
    lazy
        (let root = repoRoot.Value

         let rec walk (d: string) : string list =
             [ for f in Directory.GetFiles d do
                   if Path.GetFileName f <> ".git" then
                       yield Path.GetRelativePath(root, f).Replace('\\', '/')
               for sub in Directory.GetDirectories d do
                   if not (skippedDirs.Contains(string (Path.GetFileName sub))) then
                       yield! walk sub ]

         walk root)

/// The sources the census reads: every suite's (the `tests/` tree and each `src/*Tests*` project) and
/// the drift checks' own scripts.
let private isCensusSource (f: string) =
    let code = f.EndsWith ".fs" || f.EndsWith ".fsx"

    let suite =
        f.StartsWith "tests/"
        || (f.StartsWith "src/"
            && f.Split('/').Length > 2
            && f.Split('/').[1].Contains "Tests")

    code
    && (suite || f = "Build.fs" || f.StartsWith "docs/tools/")
    && Path.GetFileName f <> selfName

let private censusSources () : (string * string) list =
    files.Value
    |> List.filter isCensusSource
    |> List.map (fun f -> f, File.ReadAllText(Path.Combine(repoRoot.Value, f)))

let private candidates () =
    files.Value |> List.filter (fun f -> not (matchesAny implied f))

/// A script the gate invokes, by its `.ps1` / `.mjs` file name.
let private invokedScripts (text: string) : string list =
    [ for m in Regex.Matches(text, @"[\w./-]+\.(ps1|mjs)") -> string (Path.GetFileName m.Value) ]
    |> List.distinct

/// Every document `proofs.json` names — read by the suite through the ladder, so invisible to the
/// literal census.
let private ladderDocuments () : string list =
    let p = Path.Combine(repoRoot.Value, "proofs.json")

    if not (File.Exists p) then
        []
    else
        use doc = JsonDocument.Parse(File.ReadAllText p)

        let rec walk (e: JsonElement) : string list =
            match e.ValueKind with
            | JsonValueKind.Object ->
                [ for prop in e.EnumerateObject() do
                      if prop.Name = "document" && prop.Value.ValueKind = JsonValueKind.String then
                          yield string (prop.Value.GetString())
                      else
                          yield! walk prop.Value ]
            | JsonValueKind.Array ->
                [ for x in e.EnumerateArray() do
                      yield! walk x ]
            | _ -> []

        walk doc.RootElement |> List.distinct

[<Tests>]
let tests =
    testList
        "gate-reads — the record of what this repository's gate reads"
        [ test "the record is a gateReads document whose every waiver carries a reason" {
              let r = record.Value
              Expect.equal r.Kind "gateReads" "the record names its kind"

              for (p, why) in r.NotRead do
                  Expect.isTrue (p <> "" && why <> "") (sprintf "a waiver needs a path and a reason: %A" (p, why))
          }

          test "THE GATE — run.ps1 runs only the stages this record was written against, and reads their inputs" {
              let gate = File.ReadAllText(Path.Combine(repoRoot.Value, "run.ps1"))

              // `launch-demo.ps1` runs only under `-Demo`, which is not the gate; `check-coverage.mjs`
              // lives in the wire-format corpus, outside this repository.
              let known =
                  [ "run.ps1"
                    "launch-demo.ps1"
                    "fable-check.ps1"
                    "fuaran-codes.ps1"
                    "sync-renderer-web.ps1"
                    "check-coverage.mjs"
                    "run.mjs" ]

              let others =
                  invokedScripts gate |> List.filter (fun s -> not (List.contains s known))

              Expect.isEmpty
                  others
                  "the gate runs a script this record was not written against: record what it opens, then extend this test"

              Expect.isFalse
                  (gate.Contains "ls-files")
                  "the gate sweeps the tracked set: every tracked file is then a gate input, and the record must read `**`"

              Expect.isTrue
                  (gate.Contains "DriftChecks")
                  "the premise: the gate runs the generated-artefact drift checks"

              for p in
                  [ "test-suites.json"
                    "validator-coverage.json"
                    "scripts/fuaran-codes.ps1"
                    "scripts/sync-renderer-web.ps1"
                    "docs/tools/authoring-pack.fsx" ] do
                  Expect.isTrue (matchesAny record.Value.Reads p) (sprintf "a gate stage reads %s" p)
          }

          test "GO-RED — a locator-bearing source that combines the root with a path reads it" {
              let src =
                  "let root = repoRoot ()\nlet d = Path.Combine(repoRoot (), \"docs\", \"census.md\")\n"

              let hits = census [ "tests/X.fs", src ] [ "docs/census.md"; "docs/notes.md" ]
              Expect.equal (hits |> List.map fst) [ "docs/census.md" ] "the named file, and only it"
          }

          test "GO-RED — a file the authoring pack reconciles under its docs directory is read" {
              let src =
                  "let docsDir = parent\nreconcileMarkdown \"AI_AUTHORING_GUIDE.md\" false false\n"

              let hits =
                  census [ "docs/tools/x.fsx", src ] [ "docs/AI_AUTHORING_GUIDE.md"; "docs/SSR.md" ]

              Expect.equal (hits |> List.map fst) [ "docs/AI_AUTHORING_GUIDE.md" ] "the named file, and only it"
          }

          test "a source with NO root locator reads nothing, whatever it quotes" {
              let src = "let d = Path.Combine(tmp, \"docs\", \"census.md\")\n"
              Expect.isEmpty (census [ "tests/Z.fs", src ] [ "docs/census.md" ]) "a temp fixture"
          }

          test "THE SUITE — every file a census source may open is recorded as read, or waived with a reason" {
              let r = record.Value
              let waived = r.NotRead |> List.map fst

              let unaccounted =
                  census (censusSources ()) (candidates ())
                  |> List.filter (fun (p, _) -> not (matchesAny r.Reads p) && not (matchesAny waived p))
                  |> List.map (fun (p, by) -> sprintf "%s (named by %s)" p (String.concat ", " by))

              Expect.isEmpty
                  unaccounted
                  (sprintf
                      "these files are named by a source that reaches the real repository, and %s neither lists them under `reads` nor waives them under `notRead` with a reason:\n  %s"
                      recordName
                      (String.concat "\n  " unaccounted))
          }

          test "THE SUITE — every document the proof ladder names is recorded as read" {
              let reads = record.Value.Reads

              let missing =
                  ladderDocuments ()
                  |> List.filter (fun d -> not (matchesAny implied d) && not (matchesAny reads d))

              Expect.isEmpty missing (sprintf "proofs.json names documents the record does not read: %A" missing)
          }

          test "THE SUITE — every recorded read names a file, so a rename cannot leave it stale" {
              let stale =
                  record.Value.Reads
                  |> List.filter (fun spec -> not (files.Value |> List.exists (matches spec)))

              Expect.isEmpty stale (sprintf "reads naming no file: %A" stale)
          }

          test "THE SUITE — every waiver still covers a census hit, so a waiver cannot outlive its reason" {
              let r = record.Value
              let hits = census (censusSources ()) (candidates ()) |> List.map fst

              let idle =
                  r.NotRead
                  |> List.map fst
                  |> List.filter (fun spec ->
                      not (hits |> List.exists (fun h -> matches spec h && not (matchesAny r.Reads h))))

              Expect.isEmpty idle (sprintf "notRead entries no census hit needs any more — delete them: %A" idle)
          } ]
