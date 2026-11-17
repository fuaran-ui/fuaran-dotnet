/// Phase 2301 — the vocabulary change-set (WIRE_FORMAT.md §11) as ONE command:
///
///     dotnet run --project Build.fsproj -- ChangeSet [--dry-run] [--legs <host,host>] [--base <host>=<rev>,...]
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
///
/// Phase 2665 — the run also MEASURES what the change cost by hand. Every roster host's checkout and
/// the corpus are asked which files the change touched (git, from `--base <host>=<rev>`, else from
/// where HEAD left the remote's default branch, untracked files included), and each file is classed
/// GENERATED (its header says so, or the corpus's own data lists it as a generator output) or HAND.
/// The two counts per host and their total are printed after the stage report and written to
/// `output/change-set/hand-edits.json`, beside the leg logs. `--dry-run` prints the same counts and
/// writes nothing. A `<rev>` of the form `a..b` measures a recorded change instead of the working tree.
module ChangeSet

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Text.Json
open System.Text.Json.Nodes

type Options =
    {
        DryRun: bool
        Legs: string option
        /// Phase 2665 — per-host revisions the hand-edit measurement starts from (`--base`).
        Bases: Map<string, string>
    }

/// `--base fuaran=<rev>,corpus=<a>..<b>` — one `<host>=<rev>` per roster host to override.
let private parseBases (csv: string) : Map<string, string> =
    csv.Split(',', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
    |> Array.map (fun item ->
        match item.Split('=', 2) with
        | [| host; rev |] when host.Trim() <> "" && rev.Trim() <> "" -> host.Trim(), rev.Trim()
        | _ -> failwithf "ChangeSet: --base takes <host>=<rev> items, not '%s'" item)
    |> Map.ofArray

/// The change-set's own flags, taken out of the argument list before the build framework sees it
/// (it refuses options it does not know). Everything else is returned untouched.
let splitArgs (args: string array) : Options * string array =
    let rec go (opts: Options) (rest: string list) (acc: string list) =
        match rest with
        | "--dry-run" :: tail -> go { opts with DryRun = true } tail acc
        | "--legs" :: csv :: tail when not (csv.StartsWith "--") -> go { opts with Legs = Some csv } tail acc
        | "--legs" :: _ -> failwith "ChangeSet: --legs needs a comma-separated host list"
        | "--base" :: csv :: tail when not (csv.StartsWith "--") ->
            go
                { opts with
                    Bases = Map.fold (fun m k v -> Map.add k v m) opts.Bases (parseBases csv) }
                tail
                acc
        | "--base" :: _ -> failwith "ChangeSet: --base needs <host>=<rev>[,<host>=<rev>...]"
        | a :: tail -> go opts tail (a :: acc)
        | [] -> opts, List.rev acc |> Array.ofList

    go
        { DryRun = false
          Legs = None
          Bases = Map.empty }
        (List.ofArray args)
        []

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

/// Run git in `dir` and capture its standard output; a non-zero exit is an Error naming its stderr.
let private git (dir: string) (args: string list) : Result<string, string> =
    let psi =
        ProcessStartInfo(
            "git",
            [ "-c"; "core.quotepath=off" ] @ args,
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        )

    use p = start psi
    let out = p.StandardOutput.ReadToEndAsync()
    let err = p.StandardError.ReadToEnd()
    p.WaitForExit()

    if p.ExitCode = 0 then
        Ok out.Result
    else
        Error(sprintf "git %s exited %d: %s" (String.Join(" ", args)) p.ExitCode (err.Trim()))

let private isTagged (repoRoot: string) (version: string) : bool =
    match git repoRoot [ "tag"; "--list"; "v" + version ] with
    | Ok out -> out.Trim() <> ""
    | Error why -> failwithf "ChangeSet: %s" why

// ── Phase 2665 — the hand edits a vocabulary change cost ────────────────────────────────────────────

/// Where a file the change touched came from: a generator wrote it (`why` names the evidence), or a
/// person did.
type Provenance =
    | Generated of why: string
    | Hand

/// A generated file says so in its first lines: ONE line that both calls the file generated and
/// forbids a hand edit (`GENERATED … do not edit`, `… Do not hand-edit`, Go's `Code generated … DO
/// NOT EDIT.`); a line calling it a `GENERATED FILE` (a `GENERATED-ADJACENT` file, authored prose
/// around generated examples, is not one); the C# `<auto-generated>` tag; a JSON record's
/// `"generatedBy"` member; or the stamp a test runner writes on its snapshot file. Each is one line,
/// so prose that merely mentions generation is not a header.
let generatedHeader (text: string) : bool =
    text.Split('\n')
    |> Seq.truncate 12
    |> Seq.exists (fun line ->
        let l = line.ToLowerInvariant()

        l.Contains "<auto-generated"
        || l.Contains "generated file"
        || l.Contains "\"generatedby\""
        || l.StartsWith "// jest snapshot v1"
        || l.StartsWith "// vitest snapshot v1"
        || (l.Contains "generated"
            && (l.Contains "do not edit"
                || l.Contains "do not hand-edit"
                || l.Contains "do not hand edit")))

/// Class one changed file: listed by the host's own data as a generator output, else by its header,
/// else hand-written. `head` reads the file's first characters (None when it has none to read).
let classify (listed: string -> string option) (head: string -> string option) (path: string) : Provenance =
    match listed path with
    | Some why -> Generated why
    | None ->
        match head path with
        | Some text when generatedHeader text -> Generated "its header"
        | _ -> Hand

let private readJson (path: string) : JsonElement option =
    if File.Exists path then
        use doc = JsonDocument.Parse(File.ReadAllText path)
        Some(doc.RootElement.Clone())
    else
        None

let private prop (name: string) (e: JsonElement) : JsonElement option =
    match e.ValueKind with
    | JsonValueKind.Object ->
        match e.TryGetProperty name with
        | true, v -> Some v
        | _ -> None
    | _ -> None

let private str (name: string) (e: JsonElement) : string option =
    prop name e
    |> Option.filter (fun v -> v.ValueKind = JsonValueKind.String)
    |> Option.map (fun v -> string (v.GetString()))

let private items (e: JsonElement option) : JsonElement list =
    match e with
    | Some a when a.ValueKind = JsonValueKind.Array -> List.ofSeq (a.EnumerateArray())
    | _ -> []

/// The corpus's own record of what a generator writes there, read from its data rather than restated
/// here: `manifest.json` and every file its top level names (`schema`, `idl`, `enumTokens`, …);
/// `copies.json`, which each bundling host's declare step rewrites; and every file of a family whose
/// `corpus-producers.json` record names a producer (WIRE_FORMAT.md 27.8) — a root-manifest row's
/// `*File` members, a self-enumerated family's `file`, a directory family's whole directory (and the
/// rest of a self-enumerating directory when every family in it is emitted). A file of an AUTHORED
/// family, the specification text, and anything the data does not name are hand-written: a file is
/// counted generated only on evidence, so the figure errs towards more hand edits, never fewer.
let corpusOutputs (corpusRoot: string) : string -> string option =
    let emitted =
        match
            readJson (Path.Combine(corpusRoot, "corpus-producers.json"))
            |> Option.bind (prop "families")
        with
        | Some fs when fs.ValueKind = JsonValueKind.Object ->
            fs.EnumerateObject()
            |> Seq.filter (fun f -> (prop "producer" f.Value).IsSome)
            |> Seq.map (fun f -> f.Name)
            |> Set.ofSeq
        | _ -> Set.empty

    let listed = Dictionary<string, string>(StringComparer.Ordinal)
    let authored = HashSet<string>(StringComparer.Ordinal)
    let wholeDirs = Dictionary<string, string>(StringComparer.Ordinal)

    let file (family: string) (path: string) =
        if emitted.Contains family then
            listed[path] <- sprintf "a file of the emitted family %s" family
        else
            authored.Add path |> ignore

    match readJson (Path.Combine(corpusRoot, "manifest.json")) with
    | Some m ->
        listed["manifest.json"] <- "the corpus manifest"

        for p in m.EnumerateObject() do
            if
                p.Value.ValueKind = JsonValueKind.String
                && (string (p.Value.GetString())).EndsWith ".json"
            then
                listed[string (p.Value.GetString())] <- sprintf "named by manifest.json `%s`" p.Name

        for row in items (prop "fixtures" m) do
            match str "kind" row with
            | Some kind ->
                for p in row.EnumerateObject() do
                    if p.Name.EndsWith "File" && p.Value.ValueKind = JsonValueKind.String then
                        file kind (string (p.Value.GetString()))
            | None -> ()
    | None -> ()

    listed["copies.json"] <- "the declared-copies record the resync stage rewrites"

    if Directory.Exists corpusRoot then
        for dir in Directory.GetDirectories corpusRoot do
            let name = string (Path.GetFileName dir)

            match readJson (Path.Combine(dir, "manifest.json")) with
            | Some sub when not (name.StartsWith ".") ->
                let entries =
                    items (prop "families" sub)
                    |> List.filter (fun f -> f.ValueKind = JsonValueKind.Object)
                    |> List.choose (fun f -> str "id" f |> Option.map (fun id -> name + "/" + id, str "file" f))

                if entries.IsEmpty then
                    if emitted.Contains name then
                        wholeDirs[name] <- sprintf "the emitted family %s" name
                else
                    for family, f in entries do
                        f |> Option.iter (fun f -> file family (name + "/" + f))

                    if entries |> List.forall (fun (family, _) -> emitted.Contains family) then
                        wholeDirs[name] <- sprintf "a directory whose families are all emitted (%s)" name
            | _ -> ()

    fun path ->
        match listed.TryGetValue path with
        | true, why -> Some why
        | _ when authored.Contains path -> None
        | _ ->
            match path.IndexOf '/' with
            | i when i > 0 ->
                match wholeDirs.TryGetValue(path.Substring(0, i)) with
                | true, why -> Some why
                | _ -> None
            | _ -> None

/// A bundled corpus snapshot is written whole by its host's sync step, so every file under one is a
/// generator output. Its root is where the corpus's `copies.json` places the corpus `manifest.json`
/// (a workspace-relative path); the host's part of that path is what follows its directory name.
let snapshotOutputs (corpusRoot: string) (hostDirName: string) : string -> string option =
    let marker = "/" + hostDirName + "/"

    let roots =
        readJson (Path.Combine(corpusRoot, "copies.json"))
        |> Option.map (fun c -> items (prop "records" c))
        |> Option.defaultValue []
        |> List.filter (fun r -> str "source" r = Some "manifest.json")
        |> List.collect (fun r -> items (prop "consumers" r))
        |> List.choose (fun c ->
            let c = "/" + (string (c.GetString())).Replace('\\', '/')

            match c.IndexOf marker with
            | i when i >= 0 ->
                let rest = c.Substring(i + marker.Length)

                match rest.LastIndexOf '/' with
                | j when j > 0 -> Some(rest.Substring(0, j) + "/")
                | _ -> None
            | _ -> None)

    fun path ->
        roots
        |> List.tryFind (fun root -> path.StartsWith(root, StringComparison.Ordinal))
        |> Option.map (fun root -> sprintf "the bundled corpus snapshot %s" (root.TrimEnd '/'))

/// One checkout the measurement asks: a roster host (WIRE_FORMAT.md 11.0) or the corpus itself.
type Host =
    {
        Id: string
        /// The roster's `dir`: the checkout's name beside the corpus.
        DirName: string
        Root: string
    }

/// The hosts the change-set certifies, read from the corpus's own roster (its `rootEnv` honoured as
/// the leg stage honours it), then the corpus, under the id `corpus`.
let rosterHosts (workspace: string) (corpusRoot: string) : Host list =
    let roster =
        readJson (Path.Combine(corpusRoot, "conformance", "roster.json"))
        |> Option.map (fun r -> items (prop "hosts" r))
        |> Option.defaultValue []
        |> List.choose (fun h ->
            match str "id" h, str "dir" h with
            | Some id, Some dir ->
                let root =
                    match
                        str "rootEnv" h
                        |> Option.bind (Environment.GetEnvironmentVariable >> Option.ofObj)
                    with
                    | Some v when not (String.IsNullOrWhiteSpace v) -> v.Trim()
                    | _ -> Path.Combine(workspace, dir)

                Some
                    { Id = id
                      DirName = dir
                      Root = Path.GetFullPath root }
            | _ -> None)

    roster
    @ [ { Id = "corpus"
          DirName = string (Path.GetFileName(Path.TrimEndingDirectorySeparator corpusRoot))
          Root = corpusRoot } ]

/// What the measurement found in one checkout.
type Measured =
    /// No checkout at the host's root: not measured, and said so.
    | Absent
    /// The checkout could not be asked (git refused); the reason.
    | Unmeasured of string
    /// The revision measured from, and every touched file with its provenance.
    | Counted of baseRev: string * files: (string * Provenance) list

type HostCount = { Host: Host; Measured: Measured }

let private headOf (text: string) =
    if text.Length > 4096 then text.Substring(0, 4096) else text

let private workingHead (root: string) (path: string) : string option =
    let full = Path.Combine(root, path)

    if File.Exists full then
        use r = new StreamReader(full)
        let buffer = Array.zeroCreate<char> 4096
        let n = r.ReadBlock(buffer, 0, buffer.Length)
        Some(String(buffer, 0, n))
    else
        None

let private showHead (root: string) (rev: string) (path: string) : string option =
    match git root [ "show"; rev + ":" + path ] with
    | Ok text -> Some(headOf text)
    | Error _ -> None

/// The revision a host's change is measured from when `--base` names none: where HEAD left the
/// remote's default branch (`origin/HEAD`, else `origin/main`), else HEAD itself — the working tree's
/// change and nothing more.
let private defaultBase (root: string) : string =
    [ "origin/HEAD"; "origin/main" ]
    |> List.tryPick (fun r ->
        match git root [ "merge-base"; "HEAD"; r ] with
        | Ok sha when sha.Trim() <> "" -> Some(sha.Trim())
        | _ -> None)
    |> Option.defaultValue "HEAD"

let private lines (text: string) : string list =
    text.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
    |> List.ofArray

/// Measure one checkout. `rev` is a revision (the change is the working tree against it, untracked
/// files included) or a range `a..b` (the change is what `b` holds that `a` does not).
let measureHost (listed: string -> string option) (host: Host) (rev: string option) : Measured =
    if not (Directory.Exists host.Root) then
        Absent
    else
        let rev = rev |> Option.defaultWith (fun () -> defaultBase host.Root)

        let changed, head =
            match rev.IndexOf ".." with
            | i when i > 0 ->
                let a, b = rev.Substring(0, i), rev.Substring(i + 2)

                git host.Root [ "diff"; "--name-only"; "-M"; rev ] |> Result.map lines,
                (fun p -> showHead host.Root b p |> Option.orElseWith (fun () -> showHead host.Root a p))
            | _ ->
                git host.Root [ "diff"; "--name-only"; "-M"; rev ]
                |> Result.bind (fun d ->
                    git host.Root [ "ls-files"; "--others"; "--exclude-standard" ]
                    |> Result.map (fun u -> lines d @ lines u)),
                (fun p ->
                    workingHead host.Root p
                    |> Option.orElseWith (fun () -> showHead host.Root rev p))

        match changed with
        | Ok paths ->
            Counted(
                rev,
                paths
                |> List.distinct
                |> List.sort
                |> List.map (fun p -> p, classify listed head p)
            )
        | Error why -> Unmeasured why

/// Measure every roster host and the corpus. `declared` is what the caller's own generators write,
/// as (host id, host-relative path) — the reference build's CSS tier copies, say, which no header
/// can mark; `bases` maps a host id to its `--base` revision.
let measureAll
    (workspace: string)
    (corpusRoot: string)
    (declared: (string * string) list)
    (bases: Map<string, string>)
    : HostCount list =
    let corpusListed = corpusOutputs corpusRoot

    rosterHosts workspace corpusRoot
    |> List.map (fun h ->
        let own =
            declared
            |> List.filter (fun (id, _) -> id = h.Id)
            |> List.map (fun (_, p) -> p.Replace('\\', '/'))
            |> Set.ofList

        let data =
            if h.Id = "corpus" then
                corpusListed
            else
                snapshotOutputs corpusRoot h.DirName

        let listed path =
            if own.Contains path then
                Some "an output the reference build declares"
            else
                data path

        { Host = h
          Measured = measureHost listed h (Map.tryFind h.Id bases) })

let private counts (files: (string * Provenance) list) : int * int =
    let generated =
        files
        |> List.filter (fun (_, p) ->
            match p with
            | Generated _ -> true
            | Hand -> false)
        |> List.length

    generated, files.Length - generated

/// The measurement as the small JSON record the run writes: per host its base, its two counts and
/// the hand-edited files by name; then the totals, and how many hosts went unmeasured.
let handEditsJson (verdict: string) (hostCounts: HostCount list) : string =
    let hosts = JsonArray()
    let mutable generatedTotal = 0
    let mutable handTotal = 0
    let mutable unmeasured = 0

    for hc in hostCounts do
        let o = JsonObject()
        o["host"] <- JsonValue.Create hc.Host.Id
        o["root"] <- JsonValue.Create hc.Host.Root

        match hc.Measured with
        | Absent ->
            unmeasured <- unmeasured + 1
            o["absent"] <- JsonValue.Create true
        | Unmeasured why ->
            unmeasured <- unmeasured + 1
            o["unmeasured"] <- JsonValue.Create why
        | Counted(baseRev, files) ->
            let g, h = counts files
            generatedTotal <- generatedTotal + g
            handTotal <- handTotal + h
            o["base"] <- JsonValue.Create baseRev
            o["generated"] <- JsonValue.Create g
            o["hand"] <- JsonValue.Create h
            let hand = JsonArray()

            for path, p in files do
                if p = Hand then
                    hand.Add(JsonValue.Create path)

            o["handFiles"] <- hand

        hosts.Add o

    let total = JsonObject()
    total["generated"] <- JsonValue.Create generatedTotal
    total["hand"] <- JsonValue.Create handTotal
    total["hostsUnmeasured"] <- JsonValue.Create unmeasured
    let root = JsonObject()
    root["kind"] <- JsonValue.Create "changeSetHandEdits"
    root["verdict"] <- JsonValue.Create verdict
    root["hosts"] <- hosts
    root["total"] <- total
    root.ToJsonString(JsonSerializerOptions(WriteIndented = true))

/// Print the per-host counts and the total.
let private printHandEdits (hostCounts: HostCount list) =
    printfn "\n== hand edits — the files this change touched, per host (generated / hand) =="
    let mutable g = 0
    let mutable h = 0

    for hc in hostCounts do
        match hc.Measured with
        | Absent -> printfn "  %-14s not measured: no checkout at %s" hc.Host.Id hc.Host.Root
        | Unmeasured why -> printfn "  %-14s not measured: %s" hc.Host.Id why
        | Counted(baseRev, files) ->
            let hg, hh = counts files
            g <- g + hg
            h <- h + hh
            printfn "  %-14s generated %4d   hand %4d   (from %s)" hc.Host.Id hg hh baseRev

            for path, p in files do
                if p = Hand then
                    printfn "      hand  %s" path

    printfn "  %-14s generated %4d   hand %4d" "total" g h

/// Record the measurement: written as `hand-edits.json` in `outDir`, except on a dry run, which
/// writes nothing. Returns the path written.
let recordHandEdits (dryRun: bool) (outDir: string) (verdict: string) (hostCounts: HostCount list) : string option =
    if dryRun then
        printfn "  (dry run: hand-edits.json not written)"
        None
    else
        Directory.CreateDirectory outDir |> ignore
        let path = Path.Combine(outDir, "hand-edits.json")
        File.WriteAllText(path, handEditsJson verdict hostCounts + "\n")
        printfn "  written to %s" path
        Some path

/// Run the change-set. `repoRoot` is this repository; `corpusRoot` the corpus FUARAN_WIRE_FIXTURES
/// names (else the sibling); the TypeScript and Python checkouts are FUARAN_TS_ROOT / FUARAN_PY_ROOT,
/// else siblings. `declared` is the build's own generator outputs for the hand-edit measurement
/// (see `measureAll`). Returns the process exit code: 0 green, 1 red, 3 partial.
let run (repoRoot: string) (corpusRoot: string) (declared: (string * string) list) (opts: Options) : int =
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
    // Phase 2665 — the run's output, in one place: the leg logs and the hand-edit record beside them.
    let outDir = Path.Combine(repoRoot, "output", "change-set")

    let unknownBases =
        let ids = rosterHosts workspace corpusRoot |> List.map (fun h -> h.Id) |> Set.ofList

        opts.Bases
        |> Map.keys
        |> Seq.filter (fun k -> not (ids.Contains k))
        |> List.ofSeq

    if not unknownBases.IsEmpty then
        failwithf
            "ChangeSet: --base names hosts the roster does not: %s (the roster's ids, or `corpus`)"
            (String.Join(", ", unknownBases))

    let legArgs =
        [ runner
          "--roster"
          "--workspace"
          workspace
          "--log-dir"
          Path.Combine(outDir, "legs") ]
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
        printfn "    then the hand edits per host, written to %s" (Path.Combine(outDir, "hand-edits.json"))
        printfn "\n  the leg stage's own plan:"
        let legPlan = exec corpusEnv corpusRoot "node" (legArgs @ [ "--dry-run" ])
        // The same measurement a run makes, of the trees as they stand now; nothing is written.
        let measured = measureAll workspace corpusRoot declared opts.Bases
        printHandEdits measured
        recordHandEdits true outDir "dry-run" measured |> ignore
        legPlan
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

        let verdict, code =
            match failed with
            | None -> "GREEN", 0
            | Some why when why.StartsWith "PARTIAL" -> "PARTIAL", 3
            | Some _ -> "RED", 1

        // Measured whatever the verdict: what the change touched is a fact about the trees either way,
        // and the record carries the verdict beside it.
        let measured = measureAll workspace corpusRoot declared opts.Bases
        printHandEdits measured
        recordHandEdits false outDir verdict measured |> ignore

        printfn "\n  ChangeSet: %s" verdict
        code
