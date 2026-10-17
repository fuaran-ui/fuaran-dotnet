/// Phase 2301 — the vocabulary change-set (WIRE_FORMAT.md §11) as ONE command:
///
///     dotnet run --project Build.fsproj -- ChangeSet [--dry-run] [--legs <host,host>]
///
/// It performs, in order, the five stages the §11 steps describe, each a named stage with its own
/// exit, and stops at the first stage that fails, because every later stage reads what the earlier
/// one wrote:
///
///   regenerate  `Generated.fs`, the C# and VB authoring mirrors and the proof vocabulary, from the IDL
///   emit        the corpus payloads, `manifest.json`, `schema.json`, `idl.json`, `enum-tokens.json`
///   resync      both bundled corpus snapshots (TypeScript, Python), re-synced and re-declared
///   legs        every roster host certified against the corpus FUARAN_WIRE_FIXTURES names — the
///               corpus repository's `conformance/cross-host-conformance.mjs --roster`, whose roster
///               and checks (fixture `since`, codec-host `corpusFamilies`, snapshot freshness) are
///               rows of ITS data, so a new family or host is never an edit here
///   report      one line per stage, and the verdict
///
/// GREEN means every stage ran and passed. `--legs` narrows the leg stage for development and the
/// verdict is then PARTIAL, which fails the target exactly as a red does: a narrowed run is never a
/// green. `--dry-run` prints the plan, including the leg stage's own plan, and runs nothing.
///
/// Exit codes are read from each process this module starts, never from shell state, so no stage
/// can inherit another stage's verdict (the launcher mandate's Gotcha 2 cannot arise here).
module ChangeSet

open System
open System.Diagnostics
open System.IO
open System.Text.Json

type Options = { DryRun: bool; Legs: string option }

/// The change-set's own flags, taken out of the argument list before the build framework sees it
/// (it refuses options it does not know). Everything else is returned untouched.
let splitArgs (args: string array) : Options * string array =
    let rec go (opts: Options) (rest: string list) (acc: string list) =
        match rest with
        | "--dry-run" :: tail -> go { opts with DryRun = true } tail acc
        | "--legs" :: csv :: tail when not (csv.StartsWith "--") -> go { opts with Legs = Some csv } tail acc
        | "--legs" :: _ -> failwith "ChangeSet: --legs needs a comma-separated host list"
        | a :: tail -> go opts tail (a :: acc)
        | [] -> opts, List.rev acc |> Array.ofList

    go { DryRun = false; Legs = None } (List.ofArray args) []

type private Stage =
    {
        Name: string
        /// What the stage does, printed by `--dry-run` and above each run.
        Plan: string list
        Run: unit -> Result<unit, string>
    }

let private start (psi: ProcessStartInfo) : Process =
    match Process.Start psi with
    | null -> failwithf "ChangeSet: could not start %s" psi.FileName
    | p -> p

/// Start one process, streams inherited, and return its exit code.
let private exec (env: (string * string) list) (cwd: string) (file: string) (args: string list) : int =
    let psi =
        ProcessStartInfo(file, args, WorkingDirectory = cwd, UseShellExecute = false)

    for (k, v) in env do
        psi.Environment[k] <- v

    printfn "  $ %s %s   (in %s)" file (String.Join(" ", args)) cwd
    use p = start psi
    p.WaitForExit()
    p.ExitCode

let private step env cwd file args : Result<unit, string> =
    match exec env cwd file args with
    | 0 -> Ok()
    | code -> Error(sprintf "`%s %s` exited %d" file (String.Join(" ", args)) code)

let private fixtureIds (corpusRoot: string) : Set<string> =
    let path = Path.Combine(corpusRoot, "manifest.json")

    if not (File.Exists path) then
        Set.empty
    else
        use doc = JsonDocument.Parse(File.ReadAllText path)

        doc.RootElement.GetProperty("fixtures").EnumerateArray()
        |> Seq.map (fun f ->
            string (f.GetProperty("kind").GetString())
            + "/"
            + string (f.GetProperty("id").GetString()))
        |> Set.ofSeq

/// `<Version>` from this repository's `Directory.Build.props` — the reference tier a new fixture's
/// `since` is stamped with (§11.3).
let private referenceVersion (repoRoot: string) : string =
    let text = File.ReadAllText(Path.Combine(repoRoot, "Directory.Build.props"))
    let m = Text.RegularExpressions.Regex.Match(text, @"<Version>([^<]+)</Version>")

    if m.Success then
        m.Groups[1].Value.Trim()
    else
        failwith "ChangeSet: Directory.Build.props declares no <Version>"

let private isTagged (repoRoot: string) (version: string) : bool =
    let psi =
        ProcessStartInfo(
            "git",
            [ "tag"; "--list"; "v" + version ],
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            UseShellExecute = false
        )

    use p = start psi
    let out = p.StandardOutput.ReadToEnd()
    p.WaitForExit()
    out.Trim() <> ""

/// Run the change-set. `repoRoot` is this repository; `corpusRoot` the corpus FUARAN_WIRE_FIXTURES
/// names (else the sibling); the TypeScript and Python checkouts are FUARAN_TS_ROOT / FUARAN_PY_ROOT,
/// else siblings. Returns the process exit code: 0 green, 1 red, 3 partial.
let run (repoRoot: string) (corpusRoot: string) (opts: Options) : int =
    let corpusRoot = Path.GetFullPath corpusRoot
    let workspace = Path.GetFullPath(Path.Combine(repoRoot, ".."))

    let sibling envName dir =
        match Environment.GetEnvironmentVariable envName with
        | null
        | "" -> Path.Combine(workspace, dir)
        | v -> v.Trim()

    let tsRoot = sibling "FUARAN_TS_ROOT" "fuaran-ts"
    let pyRoot = sibling "FUARAN_PY_ROOT" "fuaran-py"
    let corpusEnv = [ "FUARAN_WIRE_FIXTURES", corpusRoot ]
    let runner = Path.Combine(corpusRoot, "conformance", "cross-host-conformance.mjs")

    let legArgs =
        [ runner; "--roster"; "--workspace"; workspace ]
        @ (opts.Legs |> Option.map (fun l -> [ "--legs"; l ]) |> Option.defaultValue [])

    let tsSync = [ "packages/conformance/scripts/sync-corpus.mjs" ]
    let pySync = [ "conformance/sync_corpus.py" ]

    let stages =
        [ { Name = "corpus"
            Plan = [ sprintf "the corpus at %s holds a manifest.json" corpusRoot ]
            Run =
              fun () ->
                  if File.Exists(Path.Combine(corpusRoot, "manifest.json")) then
                      Ok()
                  else
                      Error(
                          sprintf
                              "no corpus at %s (FUARAN_WIRE_FIXTURES, else the sibling wire-format-fixtures)"
                              corpusRoot
                      ) }
          { Name = "regenerate"
            Plan =
              [ "FUARAN_REGEN=1 dotnet run --project src/Fuaran.UI.Idl.Tests   (Generated.fs + the C#/VB mirrors)"
                "dotnet run --project src/Fuaran.UI.Idl.Tests -- --emit-fstar   (the proof vocabulary)" ]
            Run =
              fun () ->
                  step [ "FUARAN_REGEN", "1" ] repoRoot "dotnet" [ "run"; "--project"; "src/Fuaran.UI.Idl.Tests" ]
                  |> Result.bind (fun () ->
                      step [] repoRoot "dotnet" [ "run"; "--project"; "src/Fuaran.UI.Idl.Tests"; "--"; "--emit-fstar" ]) }
          { Name = "emit"
            Plan =
              [ sprintf "dotnet run --project src/Fuaran.UI.JsonDecode.Tests -- --emit-corpus %s" corpusRoot
                "refused when it adds a fixture while <Version> is a tagged release: a new fixture's `since` must name an unreleased tier" ]
            Run =
              fun () ->
                  let before = fixtureIds corpusRoot

                  step
                      []
                      repoRoot
                      "dotnet"
                      [ "run"
                        "--project"
                        "src/Fuaran.UI.JsonDecode.Tests"
                        "--"
                        "--emit-corpus"
                        corpusRoot ]
                  |> Result.bind (fun () ->
                      let added = Set.difference (fixtureIds corpusRoot) before
                      let version = referenceVersion repoRoot

                      if not added.IsEmpty && isTagged repoRoot version then
                          Error(
                              sprintf
                                  "the emit added %d fixture(s) (%s) while <Version> %s is the tagged release v%s, so their `since` would claim a release that does not carry them. Advance the draft slot first."
                                  added.Count
                                  (String.Join(", ", Seq.truncate 5 added))
                                  version
                                  version
                          )
                      else
                          Ok()) }
          { Name = "resync"
            Plan =
              [ sprintf "node %s, then --declare   (in %s)" tsSync.Head tsRoot
                sprintf "python %s, then --declare   (in %s)" pySync.Head pyRoot ]
            Run =
              fun () ->
                  step corpusEnv tsRoot "node" tsSync
                  |> Result.bind (fun () -> step corpusEnv tsRoot "node" (tsSync @ [ "--declare" ]))
                  |> Result.bind (fun () -> step corpusEnv pyRoot "python" pySync)
                  |> Result.bind (fun () -> step corpusEnv pyRoot "python" (pySync @ [ "--declare" ])) }
          { Name = "legs"
            Plan = [ sprintf "node %s" (String.Join(" ", legArgs)) ]
            Run =
              fun () ->
                  match exec corpusEnv corpusRoot "node" legArgs with
                  | 0 -> Ok()
                  | 3 -> Error "PARTIAL — the legs were narrowed with --legs; a narrowed run is never a green"
                  | code -> Error(sprintf "the leg stage is RED (exit %d) — its report above names each leg" code) } ]

    printfn "ChangeSet — the WIRE_FORMAT.md §11 change-set"
    printfn "  corpus %s" corpusRoot

    if opts.DryRun then
        for s in stages do
            printfn "  stage %s" s.Name

            for line in s.Plan do
                printfn "    %s" line

        printfn "  stage report"
        printfn "    one line per stage; GREEN only when every stage ran and passed"
        printfn "\n  the leg stage's own plan:"
        exec corpusEnv corpusRoot "node" (legArgs @ [ "--dry-run" ])
    else
        let results = ResizeArray<string * string>()
        let mutable failed = None

        for s in stages do
            if failed.IsNone then
                printfn "\n== stage %s ==" s.Name

                match s.Run() with
                | Ok() -> results.Add(s.Name, "ok")
                | Error why ->
                    results.Add(s.Name, "FAILED: " + why)
                    failed <- Some why
            else
                results.Add(s.Name, "not run (an earlier stage failed)")

        printfn "\n== stage report =="

        for (name, verdict) in results do
            printfn "  %-11s %s" name verdict

        match failed with
        | None ->
            printfn "\n  ChangeSet: GREEN"
            0
        | Some why when why.StartsWith "PARTIAL" ->
            printfn "\n  ChangeSet: PARTIAL"
            3
        | Some _ ->
            printfn "\n  ChangeSet: RED"
            1
