module Fuaran.Build

open System.IO
open Fake.Core
open Fake.Core.TargetOperators
open Fake.IO

let private repoRoot = __SOURCE_DIRECTORY__
let private solution = Path.Combine(repoRoot, "Fuaran.sln")

// ─── The test-suite roster ───────────────────────────────────────────
//
// DECLARED ONCE, in `test-suites.json` at the repo root, and read by BOTH entry
// points: the `Test` target below, and `run.ps1`.
//
// It used to be declared twice — thirty entries here, eight in `run.ps1` — and
// the two lists drifted, as two hand-maintained copies of one roster will. The
// DAG op-stream, FastPath, server-driven, IDL, analyzer and veneer-conformance
// suites all ran here and in CI while `run.ps1` — the gate other tooling invokes
// — never saw them, so a break in any of them was green locally and red only on
// a push, attached to whatever change happened to be carrying it. One file read
// by both cannot diverge; a second list, however carefully written, will.
//
// `requiresCorpus` marks a suite that loads the wire-format conformance corpus
// from a sibling of this repo. In a single-repo checkout (e.g. the
// publish-packages workflow) that corpus is absent and the suite crashes at
// startup, so those are skipped LOUDLY rather than run — full conformance in a
// workspace checkout, a named skip in a bare one. The corpus-present path leaves
// each suite's own fail-loud-if-absent contract intact.

type private TestSuite =
    {
        Project: string
        RequiresCorpus: bool
        /// Phase 1553 — the suite-level gate lane, verbatim from the roster. `None` is the ordinary
        /// tier (runs in `full` and `fast`, not in `pure`); `Some "pure"` joins the per-commit lane;
        /// `Some "slow"` leaves `fast` and `pure` both.
        Lane: string option
    }

let private readTestSuites () =
    let manifestPath = Path.Combine(repoRoot, "test-suites.json")

    if not (File.Exists manifestPath) then
        failwithf "test-suites.json not found at %s — the test roster cannot be read." manifestPath

    use doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText manifestPath)

    let suites =
        match doc.RootElement.TryGetProperty "suites" with
        | true, entries -> entries
        | _ -> failwithf "test-suites.json (%s) has no `suites` array." manifestPath

    let parsed =
        [ for entry in suites.EnumerateArray() ->
              let relative =
                  match entry.GetProperty("project").GetString() with
                  | null -> failwithf "test-suites.json (%s) has an entry with no `project`." manifestPath
                  | value -> value

              { Project = Path.Combine(repoRoot, relative.Replace('/', Path.DirectorySeparatorChar))
                RequiresCorpus =
                  match entry.TryGetProperty "requiresCorpus" with
                  | true, flag -> flag.GetBoolean()
                  | _ -> false
                Lane =
                  match entry.TryGetProperty "lane" with
                  | true, lane ->
                      match lane.GetString() with
                      | null -> None
                      // Refused, not ignored: a typo that silently demoted a suite out of `fast`
                      // would make the pre-merge lane quietly weaker over time, which is the one
                      // failure a lane must not have. `run.ps1` refuses the same set.
                      | ("pure" | "slow") as value -> Some value
                      | other ->
                          failwithf
                              "test-suites.json (%s): %s declares lane '%s'; expected 'pure' or 'slow'."
                              manifestPath
                              relative
                              other
                  | _ -> None } ]

    // An empty roster would let the gate pass having run nothing, which is the
    // one failure this file exists to make impossible.
    if List.isEmpty parsed then
        failwithf "test-suites.json (%s) lists no suites." manifestPath

    parsed

let private testSuites = readTestSuites ()

/// The absolute path of the rostered suite whose project file bears this name.
/// The targets that run ONE suite on its own (`SsrParity`, `DomPatchCorpus`)
/// resolve it through the roster rather than re-deriving the path, so a suite
/// that moves moves in exactly one place — and naming one that is not rostered
/// fails at startup rather than running a target that tests nothing.
let private rosteredSuite (projectFileName: string) =
    match
        testSuites
        |> List.tryFind (fun suite -> Path.GetFileName suite.Project = projectFileName)
    with
    | Some suite -> suite.Project
    | None -> failwithf "test-suites.json does not list %s." projectFileName

let private validatorProject =
    Path.Combine(repoRoot, "src", "Fuaran.UI.Validator", "Fuaran.UI.Validator.fsproj")

let private serverRenderTestProject =
    rosteredSuite "Fuaran.UI.Renderer.Server.Tests.fsproj"

let private serverDrivenTestProject =
    rosteredSuite "Fuaran.UI.ServerDriven.Tests.fsproj"

// Phase 169 — the public component-reference catalog. Its static site is a
// Fable transpile of these sources followed by a Vite bundle; the Vite half
// lives in the catalog publish workflow, this `catalogDir` is the Fable half.
let private catalogDir = Path.Combine(repoRoot, "samples", "catalog")

let private packableProjects =
    [ "Fuaran.UI"
      "Fuaran.UI.Renderer.Core"
      // Phase 526 — Chart → Drawing lowering. Renderer + Renderer.Server project-reference
      // it, so their nupkgs declare a dependency on it; it must pack alongside them.
      "Fuaran.UI.Charts"
      "Fuaran.UI.Renderer"
      "Fuaran.UI.Renderer.Server"
      // Phase 577 — the embedded browser renderer for .NET hosts. Ships the
      // built @fuaran-ui/renderer bundle and the canonical stylesheet as
      // embedded assets, so a consumer needs no Node toolchain.
      "Fuaran.UI.Renderer.Web"
      "Fuaran.UI.Ops.Abstractions"
      "Fuaran.UI.Ops"
      "Fuaran.UI.AiTools"
      // Phase 512 — the public signature-searchable pattern bank (a Fable-clean
      // façade over Fuaran.Core.FunctionRegistry + a domain-neutral seed catalogue).
      "Fuaran.UI.FastPath"
      "Fuaran.UI.Validator"
      "Fuaran.UI.OpStream.Abstractions"
      "Fuaran.UI.OpStream.InMemory"
      "Fuaran.UI.OpStream.Sqlite"
      "Fuaran.UI.OpStream.Replay"
      // Phase 178 — opt-in (rung-4) branching DAG packages. Reference the
      // linear abstractions; nothing in the light path references them back.
      "Fuaran.UI.OpStream.Dag.Abstractions"
      "Fuaran.UI.OpStream.Dag.InMemory"
      "Fuaran.UI.OpStream.Dag.Sqlite"
      "Fuaran.UI.OpStream.Dag.Merge"
      // Phase 186 — DAG-aware op-stream inspector substrate (render model +
      // audition + primacy/retention overlay + arbitrary-coordinate diff).
      // Derived read-only view; rung-4 (requires the DAG packages).
      "Fuaran.UI.OpStream.Dag.Inspect"
      "Fuaran.UI.LayoutObserver.Abstractions"
      "Fuaran.UI.LayoutObserver"
      "Fuaran.UI.Telemetry.Abstractions"
      "Fuaran.UI.Telemetry.Default"
      // Phase 1532 set `IsPackable=false` on `Fuaran.UI.Telemetry.Drift` and left it named
      // here, where `dotnet pack` then ran on it and produced nothing — a dead entry that
      // reads as a shipped package to anyone auditing this list, which is the one thing the
      // list is for. Removed in Phase 1648. The project is unchanged and still builds; what
      // is gone is a line asserting a publication that stopped happening.
      // Phase 183 — incremental re-derivation engine (effect-aware memoisation
      // over FragmentApply.apply). Consumes Fuaran.UI + Renderer + OpStream +
      // Telemetry abstractions.
      "Fuaran.UI.Memo"
      "Fuaran.UI.StyleObserver.Abstractions"
      "Fuaran.UI.StyleObserver"
      "Fuaran.UI.ThemeManifest"
      "Fuaran.UI.ServerDriven"
      "Fuaran.UI.ServerDriven.AspNetCore"
      "Fuaran.UI.ServerDriven.WebSocket"
      "Fuaran.UI.Giraffe"
      // Phase 205 — structure-only clean room: content-free skeleton projection
      // + substitutable structural-op gate + audit. Additive + Fable-clean.
      "Fuaran.UI.Ops.CleanRoom"
      // Host-neutral validated-exemplar seam (decode + pre-emit-validate +
      // canonical round-trip). Graduated out of the fuaran-ui.io docs site; no
      // Renderer / Giraffe / Markdig dependency.
      "Fuaran.UI.Content"
      // Phase 380 — the certified fragment library: a curated set of
      // parameterised FragmentDecls (typed holes, declared effect classes,
      // corpus fixtures), certified valid-for-all-bindings before it ships.
      // Fable-clean — Fuaran.UI only, no renderer / ops / validator dependency.
      "Fuaran.UI.Fragments"
      // The page-set layer for pure-SSR sites (page model + frontmatter +
      // route derivation + SiteCheck gate + RenderPlan + auto-nav + static
      // export) and its Giraffe host adapter — Giraffe isolated to the
      // adapter, matching the Fuaran.UI.Giraffe precedent.
      "Fuaran.UI.Site"
      "Fuaran.UI.Site.Giraffe" ]
    |> List.map (fun name -> Path.Combine(repoRoot, "src", name, $"{name}.fsproj"))
    // Phase 304 — the C# authoring veneer packs alongside the F# tier. It is a
    // .csproj (appended after the .fsproj map). Phase 314 appends the Roslyn
    // analyzer (also a .csproj, packed as a NuGet analyzer).
    |> fun fsprojs ->
        fsprojs
        @ [ Path.Combine(repoRoot, "src", "Fuaran.UI.CSharp", "Fuaran.UI.CSharp.csproj")
            Path.Combine(repoRoot, "src", "Fuaran.UI.Analyzers", "Fuaran.UI.Analyzers.csproj")
            // Phase 310 — the VB XML-literal veneer (a .vbproj).
            Path.Combine(repoRoot, "src", "Fuaran.UI.VisualBasic", "Fuaran.UI.VisualBasic.vbproj") ]

// ─── Phase 432 — the reference stylesheet and its tier copies ──────────────
//
// `src/Fuaran.UI.Renderer/content/fuaran-reference.css` is the CANONICAL
// stylesheet: the artefact packaged into `Fuaran.UI.Renderer`, and the one the
// class-coverage suite reads. Every other host tier ships a BYTE-COPY of it.
//
// Each consuming tier already locks its own copy — the F# coverage suite for
// the TypeScript one, `conformance/render_test.go` for Go, `tests/render.rs`
// for Rust — so drift is caught. But only by the tier that drifted, and only in
// a repo the author who caused it was not in. That is precisely how the
// preceding phase landed: it added two rule families to the canonical sheet,
// re-copied the TypeScript tier, and left the Go and Rust copies serving a
// stylesheet two families behind, with nothing on the authoring side to say so.
//
// These targets close it from the AUTHORING side, and replace the hand copy:
//
//   `-- Css`        rewrites every copy present in this checkout from the
//                   canonical sheet — the generator. `-- Css --check` runs the
//                   check below instead of writing.
//   `-- CssCheck`   fails, naming every copy that is not byte-identical. Wired
//                   into `Check`, so the gate the author already runs reports
//                   the drift they have just created.
//
// The copies are plain byte copies and stay so across clones: all four repos
// pin `* text=auto eol=lf`, so there is no newline translation to reproduce and
// no generation step beyond the copy itself.
/// Phase 1647 — the corpus root on the one contract every reader in this repo
/// honours: FUARAN_WIRE_FIXTURES first, the sibling walk second. A git worktree
/// is not beside the corpus, so without the override every corpus-dependent
/// step here silently declared itself absent.
let private corpusRootPath =
    match System.Environment.GetEnvironmentVariable "FUARAN_WIRE_FIXTURES" with
    | null
    | "" -> Path.Combine(repoRoot, "..", "wire-format-fixtures")
    | raw -> raw.Trim()

let private canonicalCss =
    Path.Combine(repoRoot, "src", "Fuaran.UI.Renderer", "content", "fuaran-reference.css")

/// Phase 1647 — where each tier's repo root is, per tier.
///
/// The default is the `..` hop above this repo, which is what a normal
/// workspace checkout wants. It is also what makes `-- Css` WRITE INTO SIBLING
/// PRIMARY TREES: a worker running the sync from a git worktree rewrites four
/// files in four checkouts it does not own, where a concurrent session may have
/// uncommitted work — so a campaign that touches the canonical stylesheet
/// cannot safely run the generator at all.
///
/// `FUARAN_CSS_SIBLINGS` redirects it. The value is a `;`-separated list of
/// `<tier>=<repo root>` entries; a tier the list does not name keeps the
/// default hop, so pointing ONE tier at a worktree is as expressible as
/// pointing all four:
///
///     $env:FUARAN_CSS_SIBLINGS = "fuaran-ts=C:\repos\Fuaran-ToolUp\wt\ts-1648"
///     $env:FUARAN_CSS_SIBLINGS = "fuaran-ts=…\wt\ts;fuaran-go=…\wt\go"
///
/// A malformed entry — no `=`, an unknown tier, an empty path — is REFUSED
/// rather than ignored: silently keeping the default would write into the very
/// primary tree the override was set to protect, which is the one failure this
/// seam exists to prevent.
let private cssSiblingRoots: Map<string, string> =
    let known = set [ "fuaran-ts"; "fuaran-go"; "fuaran-rs"; "fuaran-py" ]

    match System.Environment.GetEnvironmentVariable "FUARAN_CSS_SIBLINGS" with
    | null
    | "" -> Map.empty
    | raw ->
        raw.Split(
            ';',
            System.StringSplitOptions.RemoveEmptyEntries
            ||| System.StringSplitOptions.TrimEntries
        )
        |> Array.map (fun entry ->
            match entry.Split('=', 2) with
            | [| tier; path |] when known.Contains(tier.Trim()) && path.Trim() <> "" ->
                tier.Trim(), Path.GetFullPath(path.Trim())
            | _ ->
                failwithf
                    "FUARAN_CSS_SIBLINGS: '%s' is not a '<tier>=<repo root>' entry naming one of %A. An override that cannot be read must not fall back to the default — that would write into the primary tree it was set to protect."
                    entry
                    (Set.toList known))
        |> Map.ofArray

/// The root of one tier's repo — the override if the list names it, else the
/// historical `..` hop.
let private cssSiblingRoot (tier: string) =
    match Map.tryFind tier cssSiblingRoots with
    | Some root -> root
    | None -> Path.Combine(repoRoot, "..", tier)

/// The tier copies, keyed by the sibling repo shipping each.
let private tierCssCopies =
    [ "fuaran-ts", Path.Combine(cssSiblingRoot "fuaran-ts", "packages", "renderer", "css", "fuaran.css")
      "fuaran-go", Path.Combine(cssSiblingRoot "fuaran-go", "renderer", "content", "fuaran-reference.css")
      "fuaran-rs", Path.Combine(cssSiblingRoot "fuaran-rs", "css", "fuaran.css")
      // Phase 1082 rider — `fuaran-py` was the one tier shipping a byte-copy of
      // the canonical sheet that Phase 432 never registered here. Its own
      // byte-parity test (`tests/test_renderer.py`) compared against the
      // canonical file, so the drift was DETECTED in that repo and could not be
      // REPAIRED from this one: the generator rewrote three copies and left the
      // fourth for a human to remember. That asymmetry is precisely the class
      // Phase 432 closed for ts/go/rs, and py was outside it by omission rather
      // than by decision.
      "fuaran-py",
      Path.Combine(cssSiblingRoot "fuaran-py", "src", "fuaran_ui", "renderer", "content", "fuaran-reference.css") ]

// ─── Phase 433 — the vocabulary fingerprint stamp ──────────────────────────
//
// The canonical sheet carries a `fuaran-vocabulary-fingerprint:` stamp in its
// header naming the class vocabulary it is written against, so an SSR host
// serving it can refuse a sheet that disagrees with the renderer emitting the
// classes. The value's home is `Theme.vocabularyFingerprint` — the shipping
// constant a host compares against — and the stamp is GENERATED from it, so the
// two cannot drift silently: `-- Css` restamps, `-- CssCheck` fails.
//
// Read out of the source TEXT rather than by referencing the renderer: this is
// a FAKE build project, and giving the build a project reference on the library
// it builds to read one string would be a far larger coupling than a regex over
// a `let` binding. A regex that stops matching fails loudly below rather than
// quietly reporting agreement, which is the failure mode that would matter.
let private themeSourcePath =
    Path.Combine(repoRoot, "src", "Fuaran.UI.Renderer.Core", "Theme.fs")

let private fingerprintMarker = "fuaran-vocabulary-fingerprint:"

let private pinnedFingerprint () =
    let m =
        System.Text.RegularExpressions.Regex.Match(
            File.ReadAllText themeSourcePath,
            @"let\s+vocabularyFingerprint\s*=\s*""([^""]+)"""
        )

    if not m.Success then
        failwithf
            "Could not read `vocabularyFingerprint` from %s. The stylesheet stamp is generated from that constant; if it has been renamed or reshaped, update this reader in the same change-set rather than leaving the stamp unchecked."
            themeSourcePath

    m.Groups[1].Value

let private stampPattern =
    System.Text.RegularExpressions.Regex(System.Text.RegularExpressions.Regex.Escape fingerprintMarker + @"\s*(\S+)")

/// The fingerprint the canonical sheet is currently stamped with, or `None` when
/// the stamp is absent entirely — reported as a finding, never as agreement.
let private stampedFingerprint () =
    let m = stampPattern.Match(File.ReadAllText canonicalCss)
    if m.Success then Some m.Groups[1].Value else None

/// Rewrite the canonical sheet's stamp to the pinned constant. Returns whether
/// anything moved, so the sync can say so. Writes with the file's own bytes
/// otherwise untouched — a single-token substitution, not a re-render — because
/// the tier copies are byte copies and every other byte is hand-authored.
let private stampCanonical () =
    let pinned = pinnedFingerprint ()

    match stampedFingerprint () with
    | Some current when current = pinned -> false
    | Some _ ->
        let text = File.ReadAllText canonicalCss
        let stamped = stampPattern.Replace(text, fingerprintMarker + " " + pinned, 1)
        File.WriteAllText(canonicalCss, stamped)
        true
    | None ->
        failwithf
            "%s carries no `%s` stamp. A served stylesheet with no fingerprint is one no host can check — restore the stamp comment in the header (see docs/HOST-STYLING-CHECKLIST.md §1.5d); this target refuses to invent its position."
            canonicalCss
            fingerprintMarker

/// What this checkout can say about one tier copy. `Absent` and `Missing` are
/// deliberately distinct: a sibling that is not cloned is a narrower checkout
/// and says nothing, whereas a copy deleted from a sibling that IS cloned is a
/// finding. Collapsing them would let a deleted copy read as "not checked".
type private CssCopyState =
    | Absent
    | Missing
    | Drifted of digest: string
    | Identical

let private sha256Of (path: string) =
    System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes path))

let private inspectCssCopies () =
    let canonical = sha256Of canonicalCss

    let copies =
        tierCssCopies
        |> List.map (fun (tier, path) ->
            // `GetDirectoryName` is genuinely nullable (a rootless path has no
            // parent); a null here is the same answer as a directory that is not
            // there, so both fall to `Absent`.
            let siblingDir =
                match Path.GetDirectoryName path with
                | null -> None
                | dir -> Some dir

            let state =
                match siblingDir with
                | Some dir when Directory.Exists dir ->
                    if not (File.Exists path) then
                        Missing
                    else
                        let digest = sha256Of path
                        if digest = canonical then Identical else Drifted digest
                | _ -> Absent

            tier, path, state)

    canonical, copies

/// The stamp link, checked before the byte-copy one. Ordered that way because a
/// wrong stamp propagates: syncing first would write the stale fingerprint into
/// three sibling repos and report success doing it.
let private checkStamp () =
    let pinned = pinnedFingerprint ()

    match stampedFingerprint () with
    | Some current when current = pinned -> Trace.tracefn "  %-10s stamped    %s" "vocabulary" pinned
    | Some current ->
        failwithf
            "Reference-CSS vocabulary-fingerprint drift — the canonical sheet is stamped `%s` but `Theme.vocabularyFingerprint` is `%s`. A host asserting the renderer's constant would refuse the sheet this package ships. Run `dotnet run --project Build.fsproj -- Css` to restamp, and commit the tier copies with it."
            current
            pinned
    | None ->
        failwithf
            "%s carries no `%s` stamp. A served stylesheet with no fingerprint is one no host can check — see docs/HOST-STYLING-CHECKLIST.md §1.5d."
            canonicalCss
            fingerprintMarker

let private cssCheck () =
    checkStamp ()
    let canonical, copies = inspectCssCopies ()
    Trace.tracefn "Reference CSS %s — sha256=%s" canonicalCss canonical

    for tier, path, state in copies do
        match state with
        | Identical -> Trace.tracefn "  %-10s identical  %s" tier path
        // Reported, not silent: "nothing to check here" and "everything checked"
        // must not read alike. A single-repo checkout (the publish workflow) has
        // no siblings at all, which is legitimate and is what this line says.
        | Absent -> Trace.traceImportant (sprintf "  %-10s NOT CHECKED — sibling absent from this checkout" tier)
        | Missing
        | Drifted _ -> ()

    let broken =
        copies
        |> List.choose (fun (tier, path, state) ->
            match state with
            | Drifted digest -> Some(sprintf "%s — DRIFTED (sha256=%s)  %s" tier digest path)
            | Missing -> Some(sprintf "%s — MISSING (the sibling is checked out but carries no copy)  %s" tier path)
            | Absent
            | Identical -> None)

    if not (List.isEmpty broken) then
        failwithf
            "Reference-CSS drift — %d tier copy/copies diverged from the canonical sheet (sha256=%s):\n  %s\n\nThe F# sheet is canonical and the tier copies are generated from it. Run `dotnet run --project Build.fsproj -- Css` to rewrite them, and commit the result in the same change-set as the canonical edit."
            (List.length broken)
            canonical
            (System.String.Join("\n  ", broken))

let private cssSync () =
    // Restamp BEFORE the copies are inspected: the stamp is part of the bytes
    // being copied, so doing it the other way round leaves every tier one
    // generation behind and says nothing about it.
    if stampCanonical () then
        Trace.tracefn "  %-10s RESTAMPED  %s" "vocabulary" (pinnedFingerprint ())
    else
        Trace.tracefn "  %-10s already stamped %s" "vocabulary" (pinnedFingerprint ())

    let canonical, copies = inspectCssCopies ()
    Trace.tracefn "Reference CSS %s — sha256=%s" canonicalCss canonical

    for tier, _, state in copies do
        match state with
        | Absent -> Trace.traceImportant (sprintf "  %-10s SKIPPED — sibling absent from this checkout" tier)
        | Identical -> Trace.tracefn "  %-10s already identical" tier
        | Missing
        | Drifted _ -> ()

    let stale =
        copies
        |> List.filter (fun (_, _, state) ->
            match state with
            | Missing
            | Drifted _ -> true
            | Absent
            | Identical -> false)

    for tier, path, _ in stale do
        File.Copy(canonicalCss, path, true)
        Trace.tracefn "  %-10s WRITTEN  %s" tier path

    Trace.tracefn "%d tier copy/copies rewritten." (List.length stale)

/// The configuration EVERY target builds, tests, validates and packs in.
///
/// Single-sourced rather than repeated as a literal at each `dotnet` call,
/// because the hazard is DIVERGENCE rather than the value. `-- Build` writes
/// `bin/Release/`; a session that then runs `bin/Debug/<suite>.dll` directly —
/// the ordinary way to run an Expecto console, since the `dotnet run` driver
/// can hang before the suite starts — is testing a dll `-- Build` never
/// touched, and gets a green build and a stale binary with nothing saying so.
/// Two live output trees, one of which the gate is silent about.
///
/// `FUARAN_BUILD_CONFIGURATION=Debug` points the WHOLE gate at the tree such a
/// session is iterating in, so building and testing cannot name different ones.
/// The default is `Release`, unchanged, so CI and `run.ps1` behave exactly as
/// before — this adds a way to be consistent, it does not move the default.
let private configuration =
    match System.Environment.GetEnvironmentVariable "FUARAN_BUILD_CONFIGURATION" with
    | null
    | "" -> "Release"
    | c -> c

let private dotnet args workingDir =
    CreateProcess.fromRawCommand "dotnet" args
    |> CreateProcess.withWorkingDirectory workingDir
    |> CreateProcess.ensureExitCode
    |> Proc.run
    |> ignore

// ─── Phase 1488 — per-suite output capture in the gate log ─────────────────
//
// `dotnet` above INHERITS the child's streams (FAKE traces each process as
// `In/Out/Err false` — nothing redirected). One suite's output is therefore
// indistinguishable from the next one's in the gate log: a session asking "how
// many tests did the OpStream suite run" had to re-parse the raw log guessing
// where one suite stopped and another started, and a suite that printed nothing
// at all looked exactly like one that ran.
//
// This helper captures the child's stdout+stderr, echoes it verbatim under a
// banner naming the suite, and prints ONE attributed summary line per suite.
// Nothing is lost — the whole output is still in the log — and the counts are
// now attached to the suite that produced them.
//
// The cost is that a captured suite prints on completion rather than as it
// goes. Accepted deliberately for `Test`, where attribution is the point, and
// taken nowhere else: every other target keeps the streaming `dotnet` above.

/// The ANSI colour codes Expecto emits even when its output is redirected. A
/// summary regex over the raw bytes would match on a console and miss in a
/// capture, which is the worst of both.
let private ansi =
    System.Text.RegularExpressions.Regex(
        @"\x1B\[[0-9;?]*[A-Za-z]",
        System.Text.RegularExpressions.RegexOptions.Compiled
    )

/// Three details of Expecto's summary line are easy to get wrong and silent when you do — all
/// three were, across two drafts, and each one made the parser report a REAL summary as absent
/// while the suites around it parsed:
///
///   * the counts are THOUSANDS-FORMATTED (`1,520 tests run`), so `\d+` matches the leading `1`
///     and then fails — it is `[\d,]+` here;
///   * the separator before the counts is an EN DASH, not a hyphen;
///   * the test-list NAME is arbitrary text and routinely contains spaces and dashes of its own
///     (`for Phase 380 — the certified fragment library –`), so it cannot be `\S+`.
///
/// Hence `.+?` for the name, non-greedy so it stops at the first counts group, and `.` not
/// matching a newline so it cannot run past the line.
let private expectoSummary =
    System.Text.RegularExpressions.Regex(
        @"([\d,]+) tests run in \S+ for .+? ([\d,]+) passed, ([\d,]+) ignored, ([\d,]+) failed, ([\d,]+) errored",
        System.Text.RegularExpressions.RegexOptions.Compiled
    )

/// Run one test suite, capturing its output and attributing its counts to it.
/// A non-zero exit still fails the target — the capture changes what the log
/// SAYS, never what the gate DECIDES.
let private runSuiteCaptured (label: string) (args: string list) workingDir =
    Trace.tracefn "---- suite: %s ----------------------------------------" label

    let result =
        CreateProcess.fromRawCommand "dotnet" args
        |> CreateProcess.withWorkingDirectory workingDir
        |> CreateProcess.redirectOutput
        |> Proc.run

    let plain = ansi.Replace(result.Result.Output + result.Result.Error, "")

    if plain.Trim() <> "" then
        printfn "%s" plain

    let verdict =
        let m = expectoSummary.Match plain

        if m.Success then
            sprintf
                "tests=%s passed=%s ignored=%s failed=%s errored=%s"
                m.Groups[1].Value
                m.Groups[2].Value
                m.Groups[3].Value
                m.Groups[4].Value
                m.Groups[5].Value
        else
            // Not every rostered suite is an Expecto console — the C# / VB
            // conformance runners, the authoring PoC and the Fable law harness
            // are plain programs. Say so, rather than reporting a count of zero,
            // which would read as a suite that ran nothing.
            "no Expecto summary (not an Expecto console, or it did not reach its summary)"

    Trace.tracefn "---- suite: %s exit=%d %s" label result.ExitCode verdict

    if result.ExitCode <> 0 then
        failwithf "%s FAILED (exit %d)" label result.ExitCode

let private init (args: string array) =
    args
    |> Array.toList
    |> Context.FakeExecutionContext.Create false "Build.fs"
    |> Context.RuntimeContext.Fake
    |> Context.setExecutionContext

let private registerTargets (args: string array) =
    Target.create "Format" (fun _ -> dotnet [ "fantomas"; "." ] repoRoot)

    Target.create "Build" (fun _ ->
        // Name the tree. A green `-- Build` used to say only "the solution
        // compiles", and a session testing `bin/Debug/` read that as "the dll I
        // am about to run is current" — which it was not.
        Trace.tracefn
            "Building %s configuration (bin/%s/) — set FUARAN_BUILD_CONFIGURATION to change it."
            configuration
            configuration

        dotnet [ "build"; solution; "-c"; configuration ] repoRoot)

    Target.create "Test" (fun _ ->
        // The roster is `test-suites.json` — see the block at the head of this
        // file. `run.ps1` reads the same file, in the same order, with the same
        // corpus gate, so the two entry points cannot run different sets.
        //
        // Phase 1553 — and the same LANE. This target is what CI runs, so it stays on the full
        // lane unless `FUARAN_TEST_LANE` says otherwise; the variable is read here (the suite
        // filter) and inherited by each suite process (the marker filter inside it), so one value
        // decides both halves. `run.ps1 -Lane` is the ordinary way to set it.
        let lane =
            match System.Environment.GetEnvironmentVariable "FUARAN_TEST_LANE" with
            | null -> "full"
            | value ->
                match value.Trim().ToLowerInvariant() with
                | "" -> "full"
                // Unrecognised fails SAFE toward running MORE, matching Lanes.parse: a typo must
                // never silently skip the slow half and report green.
                | ("pure" | "fast") as known -> known
                | _ -> "full"

        let inLane (suite: TestSuite) =
            match lane with
            | "pure" -> suite.Lane = Some "pure"
            | "fast" -> suite.Lane <> Some "slow"
            | _ -> true

        let testSuites =
            match lane with
            | "full" -> testSuites
            | _ ->
                let admitted = testSuites |> List.filter inLane

                // A lane that runs nothing and exits 0 is the one answer a gate must never give.
                if List.isEmpty admitted then
                    failwithf "FUARAN_TEST_LANE=%s admits no suite in test-suites.json — nothing would run." lane

                Trace.traceImportant (
                    sprintf
                        "Lane '%s': %d of %d suites (releases cite the full lane only)."
                        lane
                        (List.length admitted)
                        (List.length testSuites)
                )

                admitted

        let corpusManifest = Path.Combine(corpusRootPath, "manifest.json")

        let corpusPresent = File.Exists corpusManifest

        for suite in testSuites do
            if suite.RequiresCorpus && not corpusPresent then
                Trace.traceImportant (
                    sprintf
                        "SKIPPING %s — wire-format-fixtures corpus absent (single-repo checkout; conformance runs where the workspace corpus is present)."
                        (Path.GetFileNameWithoutExtension suite.Project)
                )
            else
                // `GetFileNameWithoutExtension` is `string | null` under F# 10 nullness. A rostered
                // project path always has a file name, so the fallback is unreachable rather than
                // lenient — and naming the whole path is still an attribution, which an empty label
                // would not be.
                let label =
                    Path.GetFileNameWithoutExtension suite.Project
                    |> Option.ofObj
                    |> Option.defaultValue suite.Project

                runSuiteCaptured label [ "run"; "--project"; suite.Project; "-c"; configuration ] repoRoot)

    Target.create "Validate" (fun _ ->
        let srcDir = Path.Combine(repoRoot, "src")

        let candidateProjects =
            System.IO.Directory.EnumerateFiles(srcDir, "*.fsproj", System.IO.SearchOption.AllDirectories)
            |> Seq.filter (fun p ->
                not (
                    p.EndsWith("Fuaran.UI.Validator.fsproj")
                    || p.EndsWith("Fuaran.UI.Validator.Tests.fsproj")
                ))
            |> Seq.toList

        for project in candidateProjects do
            printfn "Fuaran.UI.Validator: %s" project
            dotnet [ "run"; "--project"; validatorProject; "-c"; configuration; "--"; project ] repoRoot)

    // ─── Publication gate: a tagged pack must BE the tagged version ─────
    //
    // Deliberately NOT "refuse to pack an untagged version". Packing untagged
    // versions to the shared local feed IS the workspace's inner loop — that is
    // what the feed exists for — so a blanket refusal here would break every
    // consumer's iteration to prevent a publication mistake.
    //
    // What it refuses instead is the pair of publication-shaped packs that can
    // put wrong or unreleasable bytes on nuget.org, where nothing can be taken
    // back (a version can be unlisted, never deleted):
    //
    //   1. Running AT a tag whose name disagrees with <Version>. The workflow
    //      packs whatever <Version> says at the tagged commit, so `v0.26.0` on a
    //      tree reading 0.27.0 publishes 0.27.0 — a version nobody released,
    //      under a tag that names another. Permanent, and invisible until a
    //      consumer restores it.
    //   2. Packing to a publication output dir with no tag ref at all — which is
    //      what `workflow_dispatch` on a BRANCH does. That is documented as the
    //      "re-run against an existing tag" escape hatch; dispatched against a
    //      tag it satisfies rule 1 and passes, dispatched against a branch it
    //      would publish an untagged head, so it is refused here rather than
    //      discovered on the registry.
    //
    // Both read GITHUB_REF_TYPE / GITHUB_REF_NAME rather than `git tag`: the
    // publish workflow checks out at the default depth, where the local tag list
    // is not a reliable witness to what has been released.
    let declaredVersion () =
        let props = Path.Combine(repoRoot, "Directory.Build.props")

        if File.Exists props then
            let text = File.ReadAllText props

            let m =
                System.Text.RegularExpressions.Regex.Match(text, "<Version>([^<]+)</Version>")

            if m.Success then Some(m.Groups[1].Value.Trim()) else None
        else
            None

    let envVar name =
        match System.Environment.GetEnvironmentVariable(name: string) with
        | null
        | "" -> None
        | v -> Some v

    let assertPublishablePack (packingToPublicationDir: bool) =
        let refType = envVar "GITHUB_REF_TYPE"
        let refName = envVar "GITHUB_REF_NAME"
        let allowUntagged = (envVar "FUARAN_PACK_ALLOW_UNTAGGED").IsSome

        match declaredVersion () with
        | None -> ()
        | Some version ->
            match refType, refName with
            | Some "tag", Some tag ->
                let expected = "v" + version

                if tag <> expected then
                    failwithf
                        "Pack REFUSED: building at tag '%s' but <Version> is %s (expected tag '%s').
                         The pack takes its version from Directory.Build.props, not from the tag, so this
                         would publish %s under a tag naming another version — permanently, since nuget.org
                         versions can be unlisted but never deleted.
                         Either move the tag to a commit whose <Version> is %s, or tag %s."
                        tag
                        version
                        expected
                        version
                        (tag.TrimStart 'v')
                        expected
            | _ ->
                if packingToPublicationDir && not allowUntagged then
                    failwithf
                        "Pack REFUSED: packing to a publication output (FUARAN_PACK_OUTPUT) with no release
                         tag — <Version> is %s and this build is not running at a tag.
                         Publication is the tag gesture: `git tag v%s && git push origin v%s`.
                         (Dispatching the publish workflow against a BRANCH lands here; dispatch it against
                         the tag instead. Set FUARAN_PACK_ALLOW_UNTAGGED=1 only for a local scratch pack.)"
                        version
                        version
                        version

    Target.create "Pack" (fun _ ->
        // Default: the workspace-shared inner-loop feed, so a local `-t Pack`
        // keeps shadowing released packages for downstream consumers exactly as
        // before.
        //
        // `FUARAN_PACK_OUTPUT` overrides it, and the publish workflow sets it to
        // a repo-local dir. That is load-bearing rather than tidy: the push step
        // used to glob the SHARED feed, so its input was "whatever .nupkg files
        // happen to sit in that folder" — safe on a runner only because the
        // folder is minted empty there, and on a developer machine that glob
        // reaches ~1900 packs, nearly all of them private. Packing to a
        // repo-local dir makes the push step's input this repo's own output by
        // construction.
        let defaultFeed = Path.Combine(repoRoot, "..", "..", "..", "local-nuget-feed")

        let feed =
            match System.Environment.GetEnvironmentVariable "FUARAN_PACK_OUTPUT" with
            | null
            | "" -> defaultFeed
            | dir -> dir

        assertPublishablePack (feed <> defaultFeed)

        for project in packableProjects do
            dotnet [ "pack"; project; "-c"; configuration; "-o"; feed ] repoRoot)

    // SSR client/server class+ARIA parity gate (Phase 142). Runs the server
    // renderer's parity corpus — the executable lock keeping the Feliz client
    // renderer and the Feliz.ViewEngine server renderer on the same class+ARIA
    // contract. CI runs this target alongside Test.
    Target.create "SsrParity" (fun _ ->
        dotnet [ "run"; "--project"; serverRenderTestProject; "-c"; configuration ] repoRoot)

    // DomPatch lowering conformance gate (Phase 158 QW6). Runs the server-driven
    // test project, whose golden TreeOp→DomPatch corpus locks the lowering — the
    // cheap CI gate against silent patch-lowering drift (the DomPatch analogue of
    // SsrParity). CI runs this target alongside Test.
    Target.create "DomPatchCorpus" (fun _ ->
        dotnet [ "run"; "--project"; serverDrivenTestProject; "-c"; configuration ] repoRoot)

    // Phase 169 — catalog static-build Fable gate. `dotnet fable` transpiles the
    // public component-reference catalog so a "builds clean on .NET but breaks
    // under Fable" regression (a server-only API leaking into a client file, an
    // F# 10 nullable cascade through a pre-nullable Fable lib) fails the
    // pipeline rather than only a manual browser session. Dotnet-pure — the
    // fable tool is restored by `dotnet tool restore`; no Node/Vite here (the
    // catalog publish workflow bundles this output into the static site). Run
    // standalone (`-- Catalog`) like SsrParity / DomPatchCorpus; not folded into
    // All/Check so the inner loop stays lean.
    Target.create "Catalog" (fun _ -> dotnet [ "fable"; "-o"; "output"; "--noCache" ] catalogDir)

    // ─── Phase 1488 — the Fable stage of the gate ──────────────────────────
    //
    // `Catalog` above is a TRANSPILE of a sample: it is run standalone, it is
    // deliberately outside `Check`, and it executes no assertions. So until this
    // target landed the gate made no claim about the client tier at all — a
    // server-only API leaking into a Fable-consumed file, or an F# 10 nullness
    // cascade through a pre-nullable Fable library, passed `Check` whole and
    // surfaced in a consumer's browser. And nothing anywhere said whether the
    // TRANSPILED algebra still behaves: `TreeMerge.merge3Way` ships in a package
    // that runs in a browser, and its order-independence was demonstrated by two
    // examples on .NET and certified on neither pipeline.
    //
    // The work is in `tests/fable-laws/fable-check.ps1` rather than here, the
    // same split `RendererWeb` takes and for the same reason: the stage shells a
    // JS runtime, and a FAKE target is the wrong place for that. `run.ps1` calls
    // the SAME script, so the two entry points cannot run different Fable
    // stages — the posture `test-suites.json` takes for the test roster.
    //
    // Ordered AFTER `Test` inside `Check` (a soft `?=>`, imposing no dependency
    // of its own): both stages are about the code, and when both run the .NET
    // suites are the cheaper diagnosis, so they answer first.
    let fableCheck () =
        let script = Path.Combine(repoRoot, "tests", "fable-laws", "fable-check.ps1")

        CreateProcess.fromRawCommand "pwsh" [ "-NoProfile"; "-File"; script ]
        |> CreateProcess.withWorkingDirectory repoRoot
        |> CreateProcess.ensureExitCode
        |> Proc.run
        |> ignore

    Target.create "FableCheck" (fun _ -> fableCheck ())

    Target.create "All" ignore

    Target.create "Check" ignore

    // Phase 1674 - the FAKE-ONLY drift subset of `Check`, named once so `run.ps1` can
    // reach it.
    //
    // WHY IT EXISTS. `run.ps1` and this `Check` target are two gates over one repo, and
    // they were not the same gate. Five drift targets were reachable ONLY from `Check` -
    // which CI runs and `run.ps1` never called - so a local full lane went green over a
    // generated artefact nobody had regenerated, and the red arrived on the push,
    // attached to whatever change happened to be carrying it. It bit twice in successive
    // campaigns and each cost a push cycle: Phase 1637's version bump left
    // `docs/prompt-pack/manifest.json`'s `languageVersion` stale (`AuthoringPack` pins it
    // to `<Version>`), and Phase 1670's embedded `@fuaran-ui/renderer` bundle drifted past
    // a green `run.ps1` because that script runs the sync script's WRITER SELF-TEST, which
    // is a different assertion from `RendererWebCheck`'s byte comparison.
    //
    // It is an aggregate rather than five commands copied into `run.ps1`, and that is the
    // point: `Check` depends on THIS and `run.ps1` calls THIS, so a target added to one
    // gate cannot be missing from the other. Copying them across would have recreated the
    // two-lists-that-must-agree shape this repo has now dissolved three times (the Fable
    // portability list, the FUARAN code registries, the reference-CSS tier copies).
    //
    // MEMBERSHIP: a `Check` target whose only entry point is FAKE - the work is a function
    // in this file, or an fsi/script invocation declared only here. A target that
    // delegates to a standalone script is deliberately NOT here: `CodesCheck`
    // (`scripts/fuaran-codes.ps1 -Check`), `ContentJsCheck` (`tests/content-js/run.mjs`)
    // and `ValidatorCoverageCheck` (the corpus's own `validator/check-coverage.mjs`) are
    // each declared once and called directly by both gates, which is the same
    // one-declaration property by a shorter route. Build, Test, Validate and FableCheck
    // are not drift checks and stay out - `run.ps1` runs its own equivalents and would
    // otherwise run them twice.
    Target.create "DriftChecks" ignore

    "DriftChecks" ==> "Check" |> ignore

    "Format" ==> "Build" ==> "Test" ==> "All" |> ignore
    "Build" ==> "Pack" |> ignore
    "Build" ==> "Validate" |> ignore
    "Build" ==> "SsrParity" |> ignore
    "Build" ==> "DomPatchCorpus" |> ignore
    "Build" ==> "Catalog" |> ignore
    "Test" ==> "Validate" ==> "Check" |> ignore
    // Phase 1488. The HARD `Build` edge is real: the law harness's .NET leg is
    // run by the script through `dotnet run`, and the portability compiles need
    // the project graph restored.
    "Build" ==> "FableCheck" ==> "Check" |> ignore
    "Test" ?=> "FableCheck" |> ignore

    // Phase 110 — AI-authoring pack drift check. Runs docs/tools/authoring-pack.fsx in
    // --check mode: fails the build if any corpus-derived wire example in the authoring
    // guide or prompt pack diverged from the wire-format-fixtures corpus. No HARD Build
    // dep — it is a pure fsi pass over docs/ + wire-format-fixtures/, and `-- AuthoringPack`
    // on its own should stay a seconds-long check rather than a solution build.
    //
    // Phase 1570 — this target also carries the signature catalogue's TEACHING
    // assertions (`assertCatalogueTeaching`, beside the generator): no field is taught
    // as `any` unless its schema is literally `true` or the row is pinned; that
    // assertion is proved able to go red against a scratch schema on every run; and the
    // emitted row count per kind matches idl.json, so a silently dropped field is red
    // too. No target of its own — the script gates BOTH --check and --write from inside,
    // on the assertLenientPartition pattern, so running it here is already running them.
    // The reference-CSS principle applies: the gate runs from the AUTHORING side, so a
    // regeneration that reintroduces the class fails for the author who made it rather
    // than surfacing as somebody else's decode failure a fortnight later.
    //
    // Phase 1572 — and it is a corpus-DEPENDENT step in a repo that also builds without
    // the corpus, so the script now answers that case itself rather than throwing a
    // FileNotFoundException the caller reads as a generator defect. On a checkout with no
    // wire-format-fixtures sibling (the publish workflow's single-repo checkout) a
    // `--check` prints NOT CHECKED against each corpus-derived check by name and exits 0
    // — the `CssCheck` posture below, for the same reason: "nothing to check here" must
    // not read as "everything checked". A sibling that IS present but carries no
    // manifest/schema fails, which is the `CssCopyState.Missing` half of the same
    // distinction, and `--write` refuses in both cases.
    Target.create "AuthoringPack" (fun _ ->
        dotnet
            [ "fsi"
              Path.Combine(repoRoot, "docs", "tools", "authoring-pack.fsx")
              "--check" ]
            repoRoot)

    "AuthoringPack" ==> "DriftChecks" |> ignore

    // Phase 1647 — the corpus's cross-host coverage projection, run from the AUTHORING
    // side. `validator-coverage.json` declares which of the canonical vocabulary's codes
    // this host implements; `validator/check-coverage.mjs` in the corpus is what compares
    // the two. Nothing invoked it here, so the declaration's own claim to be "checked by
    // construction" was checked by nobody — which is how it came to stop at FUARAN114
    // while the vocabulary ran to FUARAN148, and how a phase that merely ADDED a defect
    // case left the cross-host gate red on `main` for someone else to attribute.
    //
    // THIS REPO'S root is passed explicitly rather than relying on the script's sibling
    // discovery, and the difference is not cosmetic: discovery would check the sibling
    // PRIMARY tree's declaration, so a worktree would gate a file it is not editing —
    // green while its own is stale, red for a drift it did not cause. The cross-host
    // sweep over every sibling is the corpus side's to run (`node
    // validator/check-coverage.mjs --matrix`), and is a different question.
    //
    // Node-only, no build step. Absent corpus ⇒ NOT CHECKED by name, the `CssCheck`
    // posture: "nothing to check here" must not read as "everything checked".
    Target.create "ValidatorCoverageCheck" (fun _ ->
        let script = Path.Combine(corpusRootPath, "validator", "check-coverage.mjs")

        if not (File.Exists script) then
            Trace.traceImportant
                "validator coverage  NOT CHECKED — the wire-format-fixtures corpus is absent from this checkout."
        else
            let result =
                CreateProcess.fromRawCommand "node" [ script; repoRoot ]
                |> CreateProcess.withWorkingDirectory repoRoot
                |> Proc.run

            if result.ExitCode <> 0 then
                failwithf
                    "validator-coverage.json disagrees with the corpus vocabulary (exit %d). Regenerate it with: dotnet run --project src/Fuaran.UI.JsonDecode.Tests -- --emit-vocabulary"
                    result.ExitCode)

    "ValidatorCoverageCheck" ==> "Check" |> ignore

    // Phase 840 — the lenient-dialect pack variant's drift check. Unlike AuthoringPack
    // it NEEDS the build: every dialect example block is proved loss-free by running
    // the real decoder over (canonical, dialect) pairs (docs/tools/dialect-verify.fsx
    // consumes the Release outputs of Fuaran.UI.JsonDecode.Tests), so the target
    // depends on Build rather than being a pure fsi pass.
    Target.create "AuthoringPackDialect" (fun _ ->
        dotnet
            [ "fsi"
              Path.Combine(repoRoot, "docs", "tools", "authoring-pack.fsx")
              "--check"
              "--dialect"
              "lenient" ]
            repoRoot)

    "Build" ==> "AuthoringPackDialect" ==> "DriftChecks" |> ignore

    // Phase 843 — the per-family compiled pack variants' drift check. A pure fsi pass
    // like AuthoringPack: each variant is compiled from committed inputs only (the
    // section-demand index, the flip record's per-family dialect verdicts, and the
    // canonical or lenient pack it selects), so nothing here needs the decoder. It IS
    // in the gate rather than left to the operator: a pack regen that did not
    // recompile the variants would leave every compiled artefact silently describing
    // a pack that no longer exists, and a variant's whole value is being attributable.
    Target.create "AuthoringPackFamilies" (fun _ ->
        dotnet
            [ "fsi"
              Path.Combine(repoRoot, "docs", "tools", "authoring-pack.fsx")
              "--check"
              "--family"
              "all" ]
            repoRoot)

    "AuthoringPackFamilies" ==> "DriftChecks" |> ignore

    // Phase 432 — the reference stylesheet's tier copies. Two entry points over
    // one pair of functions (see their comment above `dotnet`): `Css` generates,
    // `Css --check` and `CssCheck` verify. Both are pure IO over committed
    // files, so neither depends on `Build`.
    //
    // `CssCheck` exists as its own target rather than `Check` depending on `Css`
    // with a flag, because a target chain cannot pass one: `Check` would then
    // run the GENERATOR, silently rewriting the copies it was asked to verify. A
    // gate that repairs what it measures can never fail.
    Target.create "Css" (fun _ ->
        if args |> Array.contains "--check" then
            cssCheck ()
        else
            cssSync ())

    Target.create "CssCheck" (fun _ -> cssCheck ())

    "CssCheck" ==> "DriftChecks" |> ignore

    // Phase 577 — the embedded browser-renderer assets, on exactly the shape
    // above and for the same reason. `Fuaran.UI.Renderer.Web` embeds a BUILT
    // ARTEFACT from fuaran-ts; a byte copy across a repo boundary goes stale
    // silently, and the failure is the quiet kind (the page renders, one node
    // degrades). `RendererWeb` generates, `RendererWebCheck` verifies, and the
    // two are separate targets so `Check` cannot be made to repair what it
    // measures.
    //
    // The work is in `scripts/sync-renderer-web.ps1` rather than here, unlike
    // the CSS pair, because the SYNC half shells `pnpm` to build the bundle and
    // a FAKE target is the wrong place for a Node build. The CHECK half needs no
    // Node at all — it reads committed text — which is what lets it run in the
    // gate on a machine that has never installed one.
    let syncRendererWeb (mode: string) =
        let script = Path.Combine(repoRoot, "scripts", "sync-renderer-web.ps1")

        CreateProcess.fromRawCommand "pwsh" [ "-NoProfile"; "-File"; script; mode ]
        |> CreateProcess.withWorkingDirectory repoRoot
        |> CreateProcess.ensureExitCode
        |> Proc.run
        |> ignore

    Target.create "RendererWeb" (fun _ ->
        if args |> Array.contains "--check" then
            syncRendererWeb "-Check"
        else
            syncRendererWeb "-Sync")

    Target.create "RendererWebCheck" (fun _ -> syncRendererWeb "-Check")

    "RendererWebCheck" ==> "DriftChecks" |> ignore

    // Phase 1646 — the FUARAN defect-code collision check. The code space is
    // shared by three registries (the tree-time validator's `describe`, the
    // source-AST walker's findings, the Roslyn descriptors), each of which used
    // to mint by reading the highest number the minting session happened to have
    // open. Two phases minted FUARAN114 in one evening; the analyzer and the
    // walker had been sitting on FUARAN060/061 for two different defects each
    // since Phase 315, so one code named two things depending on which tool
    // reported it.
    //
    // The work is in `scripts/fuaran-codes.ps1` rather than here for
    // `sync-renderer-web.ps1`'s reason: it is also the ALLOCATOR an author runs
    // by hand (`-Next`), and a FAKE target is not something you invoke to be
    // handed a number. Declared once, called by both entry points — this target
    // and `run.ps1` — so the two gates cannot check different registries.
    //
    // No `Build` dependency, hard or soft: it reads committed source text with
    // regexes and needs no compile, so `-- CodesCheck` stays a seconds-long
    // check rather than a solution build.
    Target.create "CodesCheck" (fun _ ->
        let script = Path.Combine(repoRoot, "scripts", "fuaran-codes.ps1")

        CreateProcess.fromRawCommand "pwsh" [ "-NoProfile"; "-File"; script; "-Check" ]
        |> CreateProcess.withWorkingDirectory repoRoot
        |> CreateProcess.ensureExitCode
        |> Proc.run
        |> ignore)

    "CodesCheck" ==> "Check" |> ignore

    // Phase 1648 — the shipped BROWSER SCRIPTS. Three JavaScript files ride in this
    // repo's packages and run in a reader's browser, and had no behavioural coverage of
    // any kind: Phase 1532 could add only a source-SHAPE guard, and the defect that
    // motivated this target — a server-driven `ReadFileBody` that resolved to a `<label>`,
    // found no `files` and broke out in silence — has a perfectly ordinary shape.
    //
    // Declared once and called by both entry points, this target and `run.ps1`, on
    // `CodesCheck`'s reasoning. Node stdlib only; no build dependency, hard or soft.
    //
    // BOTH halves run. The `--self-test` pass perturbs each subject in memory and requires
    // the harness to go RED, because a hand-rolled DOM stub is exactly the kind of test
    // double that can pass by understanding nothing — a harness whose falsifier is unnamed
    // is not a check. The sidecar writer's self-test rides here for the same reason: Phase
    // 1532's F# round trip starts from the F# writer, so it can only ever prove one of that
    // format's two writers.
    Target.create "ContentJsCheck" (fun _ ->
        let harness = Path.Combine(repoRoot, "tests", "content-js", "run.mjs")

        for args in [ [ harness ]; [ harness; "--self-test" ] ] do
            CreateProcess.fromRawCommand "node" args
            |> CreateProcess.withWorkingDirectory repoRoot
            |> CreateProcess.ensureExitCode
            |> Proc.run
            |> ignore

        let syncScript = Path.Combine(repoRoot, "scripts", "sync-renderer-web.ps1")

        CreateProcess.fromRawCommand "pwsh" [ "-NoProfile"; "-File"; syncScript; "-SelfTest" ]
        |> CreateProcess.withWorkingDirectory repoRoot
        |> CreateProcess.ensureExitCode
        |> Proc.run
        |> ignore)

    "ContentJsCheck" ==> "Check" |> ignore

    // Phase 1094 — ORDER the docs-drift checks after the compile/test gate inside
    // `Check`, with SOFT dependencies (`?=>` — "if both targets run, this one runs
    // first", imposing no dependency of its own).
    //
    // A dependency-free target is free to run first, and these did: a `Check` whose
    // only defect was a stale generated doc exited before `Build` had compiled
    // anything, so the run could not distinguish "the pack needs a regen" from "the
    // repo does not build" — and the compile gate, which is what `Check` mainly
    // exists to be, never ran at all. The gate's first answer must be about the code.
    //
    // Soft rather than hard (`==>`) deliberately: these three genuinely do not need
    // the build (pure IO over committed files), and a hard dep would make
    // `-- AuthoringPack` / `-- CssCheck` compile the whole solution to run a text
    // comparison. `AuthoringPackDialect` keeps its HARD `Build` dep above — it runs
    // the real decoder out of the Release outputs, so there the build is an input
    // rather than an ordering preference.
    "Test" ?=> "AuthoringPack" |> ignore
    "Test" ?=> "AuthoringPackFamilies" |> ignore
    "Test" ?=> "CssCheck" |> ignore

[<EntryPoint>]
let main args =
    init args
    registerTargets args

    // FAKE's CLI selects a target only via `-t <name>` / `--target <name>`; a bare
    // positional (`dotnet run -- Validate`) falls through to <targetargs>, so the
    // default target ran regardless of the argument. Dispatch the documented
    // `-- <Target>` form by hand; flag-shaped args (`-t Pack`, `--list`) still go
    // through FAKE's own parser.
    let target =
        match args |> Array.tryHead with
        | Some t when not (t.StartsWith "-") -> t
        | _ -> "All"

    Target.runOrDefaultWithArguments target
    0
