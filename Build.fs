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
    { Project: string
      RequiresCorpus: bool }

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
                  | _ -> false } ]

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
      "Fuaran.UI.Telemetry.Drift"
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
let private canonicalCss =
    Path.Combine(repoRoot, "src", "Fuaran.UI.Renderer", "content", "fuaran-reference.css")

/// The tier copies, keyed by the sibling repo shipping each. Resolved through
/// the same `..` hop above this repo that the wire-corpus gate uses.
let private tierCssCopies =
    [ "fuaran-ts", Path.Combine(repoRoot, "..", "fuaran-ts", "packages", "renderer", "css", "fuaran.css")
      "fuaran-go", Path.Combine(repoRoot, "..", "fuaran-go", "renderer", "content", "fuaran-reference.css")
      "fuaran-rs", Path.Combine(repoRoot, "..", "fuaran-rs", "css", "fuaran.css")
      // Phase 1082 rider — `fuaran-py` was the one tier shipping a byte-copy of
      // the canonical sheet that Phase 432 never registered here. Its own
      // byte-parity test (`tests/test_renderer.py`) compared against the
      // canonical file, so the drift was DETECTED in that repo and could not be
      // REPAIRED from this one: the generator rewrote three copies and left the
      // fourth for a human to remember. That asymmetry is precisely the class
      // Phase 432 closed for ts/go/rs, and py was outside it by omission rather
      // than by decision.
      "fuaran-py",
      Path.Combine(repoRoot, "..", "fuaran-py", "src", "fuaran_py", "renderer", "content", "fuaran-reference.css") ]

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
        let corpusManifest =
            Path.Combine(repoRoot, "..", "wire-format-fixtures", "manifest.json")

        let corpusPresent = File.Exists corpusManifest

        for suite in testSuites do
            if suite.RequiresCorpus && not corpusPresent then
                Trace.traceImportant (
                    sprintf
                        "SKIPPING %s — wire-format-fixtures corpus absent (single-repo checkout; conformance runs where the workspace corpus is present)."
                        (Path.GetFileNameWithoutExtension suite.Project)
                )
            else
                dotnet [ "run"; "--project"; suite.Project; "-c"; configuration ] repoRoot)

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

    Target.create "All" ignore

    Target.create "Check" ignore

    "Format" ==> "Build" ==> "Test" ==> "All" |> ignore
    "Build" ==> "Pack" |> ignore
    "Build" ==> "Validate" |> ignore
    "Build" ==> "SsrParity" |> ignore
    "Build" ==> "DomPatchCorpus" |> ignore
    "Build" ==> "Catalog" |> ignore
    "Test" ==> "Validate" ==> "Check" |> ignore

    // Phase 110 — AI-authoring pack drift check. Runs docs/tools/authoring-pack.fsx in
    // --check mode: fails the build if any corpus-derived wire example in the authoring
    // guide or prompt pack diverged from the wire-format-fixtures corpus. No HARD Build
    // dep — it is a pure fsi pass over docs/ + wire-format-fixtures/, and `-- AuthoringPack`
    // on its own should stay a seconds-long check rather than a solution build.
    Target.create "AuthoringPack" (fun _ ->
        dotnet
            [ "fsi"
              Path.Combine(repoRoot, "docs", "tools", "authoring-pack.fsx")
              "--check" ]
            repoRoot)

    "AuthoringPack" ==> "Check" |> ignore

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

    "Build" ==> "AuthoringPackDialect" ==> "Check" |> ignore

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

    "AuthoringPackFamilies" ==> "Check" |> ignore

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

    "CssCheck" ==> "Check" |> ignore

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

    "RendererWebCheck" ==> "Check" |> ignore

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
