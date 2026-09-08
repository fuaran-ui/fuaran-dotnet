#Requires -Version 7.0
<#
.SYNOPSIS
  The repo's FABLE STAGE: compile the client tier under its own settings, then run the Fable law
  harness under Node and compare it to the same harness on .NET.

.DESCRIPTION
  WHY THIS EXISTS. Until Phase 1488 the only Fable target in this repo was the FAKE `Catalog`
  target — a transpile of a sample, run standalone, executing no assertions. So `Check` and
  `run.ps1` could both be green while the client tier did not compile under a consumer's settings,
  and neither could say anything at all about whether the transpiled algebra still behaves. Both
  halves of that gap are closed here, and this ONE script is what both entry points call: the FAKE
  `FableCheck` target and `run.ps1`. Declared once, read by both — the same posture
  `test-suites.json` takes for the test roster, and for the same reason.

  TWO STAGES, and they answer different questions.

  1. PORTABILITY. The client-tier projects that ship `Content Include="**\*.fs"
     PackagePath="fable\"` are Fable-compiled UNDER THEIR OWN MSBuild PROPERTIES. Under Fable it is
     the ENTRY project's properties that govern the whole transpiled source graph, so a compile
     entered through a `<Nullable>disable</Nullable>` sample says nothing about a nullable-enabled
     consumer — which is exactly what every Fable lane in this repo did before 2026-07-29, and why
     CI's `fable-portability` job exists. This stage is that job, reachable locally. A server-only
     API leaking into a Fable-consumed file, or an F# 10 nullness cascade through a pre-nullable
     Fable library, fails HERE rather than in a consumer's browser.

     WHICH projects is DERIVED FROM THE TREE, not hand-kept (Phase 1606). See "The derivation"
     below; `-List` prints the answer with the reason beside each entry and compiles nothing.

  2. THE LAWS. `FableLaws.fsproj` beside this script is compiled and run under Node, and its output
     is compared BYTE FOR BYTE against the same program on .NET. Each line carries counts, so two
     pipelines that are each internally lawful and disagree about a merge outcome still differ
     line-for-line; a law-only probe would report that as two green runs.

  THE DERIVATION (Phase 1606). Fuaran.UI 0.78.0 shipped a Renderer whose `#if FABLE_COMPILER` arm
  did not compile — four bare `JVal` / `JStr` uses with no `open Fuaran.Core` — invisible to the
  .NET build, which compiles only the `#else` arm, and to this gate, whose hand-kept list did not
  name the Renderer. The 0.78.1 fix added it here and to the mirrored list in
  `.github/workflows/apply-parity-fable.yml`, by hand. Two lists that must agree, and that must be
  extended whenever a project gains a conditional arm, drift again. So they are computed:

    shipping   every `src/**/*.fsproj` packing its `.fs` sources under `PackagePath="fable\"` —
               i.e. every package a Fable consumer can transpile. THIS is the set the gate is
               responsible for, and it needs no list: a new package joins it by shipping the
               sources, which is the same act that makes it a Fable package at all.

    exempt     a shipping project may declare `<FablePortabilityExemption>why</...>` in its OWN
               fsproj, beside the pack path it qualifies. The property's VALUE is the reason, so an
               exemption without one cannot be written; the stage prints every exemption by name on
               every run, so it stays visible rather than becoming a list nobody re-reads. Default
               is GATED — a new package is covered on its first commit with no edit here.

    entries    the gated projects that are Fable-compiled directly, as the union of two rules:

               (a) the ROOTS of the gated reference graph — projects no other gated project
                   references. Every gated project is reachable from some root (project references
                   are acyclic), so entering the roots COVERS the whole set, and each covered
                   project's sources — conditional arms included — are compiled by some entry.
                   This is what the 0.78.0 hole actually was: `Renderer.Core` was reached through
                   `ServerDriven` and was fine; `Renderer` was reached by nothing.

               (b) every gated project holding a `#if [!]FABLE_COMPILER` arm. Redundant for
                   coverage — (a) already reaches it — but not for the PROPERTIES it is compiled
                   under, which is the question this stage exists to answer: reached transitively,
                   a project is compiled under the ENTRY's properties, not its own. A project whose
                   behaviour genuinely forks on the pipeline is the one where that distinction
                   costs something, so it is entered on its own account.

    coverage   asserted, not assumed: the closure of the entries over `ProjectReference` must equal
               the gated set. (a) makes that true by construction, so a failure here means the
               derivation stopped seeing part of the tree — which is exactly when a silent gap
               would otherwise open.

    audit      a PACKABLE `src/` project holding a conditional arm while shipping NO `fable\`
               sources fails by name. Such an arm is compiled by nothing and reaches no consumer;
               it is the 0.78.0 shape one step earlier, before the pack path is added.

  THE CONTENT-ADDRESSED SKIP (Phase 1619). The portability stage above is ~65% of this repo's
  gate: twelve `--noCache` compiles, four to five minutes, and on an unchanged tree every one of
  them recomputes an answer nothing has invalidated. So each compile is keyed on a CONTENT ADDRESS,
  and in a NARROW LANE (`run.ps1 -Lane pure|fast`, read here from `FUARAN_TEST_LANE`) a compile
  whose address matches its last recorded green is SKIPPED BY ADDRESS — named, with the address
  printed. THE FULL LANE ALWAYS COMPILES: it writes the address and never reads one, so the skip is
  structurally unreachable on the lane a release cites.

  Nothing here trusts Fable's own cache — `--noCache` is untouched, and the rule that a stale cache
  must never serve a compile is not weakened but made unreachable, because a byte that changes the
  compile changes the address. What the address covers:

    * every `.fs` / `.fsi` and every `.fsproj` in the entry's TRANSITIVE `ProjectReference` closure,
      by content hash — the whole source graph Fable actually reads, not just the entry's own files;
    * every `<Compile Include>` resolving OUTSIDE its project's directory (a linked file), which a
      directory walk would otherwise miss;
    * the MSBuild files GOVERNING each project in that closure — the nearest `Directory.Build.props`
      / `.targets`, `Directory.Packages.props`, `nuget.config`, `global.json` — so a property or a
      package version moving one directory up still moves the address;
    * the Fable tool version from the nearest `.config/dotnet-tools.json`;
    * the entry's own Fable-relevant properties (`Nullable`, `LangVersion`, `DefineConstants`,
      `TargetFramework(s)`), which under Fable govern the WHOLE transpiled graph;
    * the exact compile the record stands for, as a string, so a green recorded under one command
      can never be honoured for a different one;
    * `$addressSchema` — bump it and every recorded green misses, which is the correct direction
      whenever WHAT the address covers changes.

  THE SAFETY DIRECTION IS ALWAYS "COMPILE". An unresolvable reference, an unreadable project, a
  missing tool manifest, an unrecognised lane, an absent or unparseable record: every one of them
  yields NO ADDRESS and therefore a compile. A skip needs a positive match on all of it. A record is
  written only from a GREEN compile, and a RED one deletes the record for that subject outright, so
  the next narrow run recompiles even if the tree is later restored to a state that once passed.

  Records live under the system temp root beside the portability output — outside the repo tree for
  the MAX_PATH reason below, and never committed. They are keyed by the resolved source root, so a
  second worktree and the self-test's scratch tree each keep their own.

  `-Addresses` prints the derived address per compile and exits, the way `-List` prints the derived
  set. `-ProveAddressing` runs the go-red proof for the address function itself over a scratch tree
  of plain files — no compiling, milliseconds — and it runs AUTOMATICALLY at the head of any run
  that could skip. It perturbs real inputs on disk rather than the manifest the hash is taken over:
  editing that manifest would prove only that SHA-256 is sensitive to its input, which is true of
  every hash and says nothing about whether this address reads the tree. The remaining proof — that
  the FULL lane compiles with a matching record present — needs a real end-to-end run and lives in
  `fable-check.tests.ps1` beside this file, with the rest of the derivation's go-red proof.

  THE CONCURRENT COMPILES, THE TIMINGS AND THE BUDGET (Phase 1620). The compiles above are
  independent by construction — each enters a different project, each emits into its own output
  directory, and `--noCache` means none of them reads another's leavings — so they are run
  CONCURRENTLY, at a bounded degree (the compile count, capped; see `Get-CompileParallelism`).
  Everything that is not `dotnet fable` itself stays in this runspace: the address is derived here,
  the skip decision is taken here, the record is written here. A parallel runspace holds none of
  this script's functions or state, so keeping it to the one process invocation is what makes the
  concurrency a change of SCHEDULING rather than a second implementation of the stage.

  A JOB THAT RETURNS NOTHING IS A FAILURE, NOT AN ABSENCE. Results are collated back in derivation
  order and matched to the queue by name; a queued compile with no result is named and fails, the
  same as one that exited non-zero. That property is worth stating because the obvious way to write
  this loses a failure quietly: interleaved live output makes a red compile hard to find, and a
  dropped job makes it impossible.

  THE OUTPUT DIRECTORIES ARE PER-COMPILE, AND WERE ALREADY. The scratch ROOT is per tree
  (`Get-TreeScratchRoot`, after two separate incidents where concurrent gates wiped each other's
  output mid-compile); each entry emits into its own leaf beneath it. Concurrency here is the same
  hazard one level down, and it is answered the same way — by never pointing two compiles at one
  directory, rather than by serialising them.

  THE TIMINGS ARE PRINTED because the stage's cost was invisible: it is ~65% of this repo's gate and
  nothing said so until someone measured a whole run by hand. Every compile reports its wall-clock
  beside Fable's OWN two figures — `parsed in Nms` (cracking the project graph) and `compilation
  finished in Nms` (emitting) — read out of its captured output, because the split between them is
  what says whether a stage grew by gaining a project or by growing one.

  THE BUDGET IS DECLARED, AND IT WARNS. `$FableStageBudgetSeconds` below carries its measurement and
  its date; a run past it prints a WARNING naming the largest contributors. Warn-only, deliberately:
  a gate that goes red because a machine is slow is a gate people learn to step over, and this
  number is a tripwire for a stage that GREW — Phase 1606 derives the entry set from the tree, so a
  new Fable-shipping package adds a full parse with no edit here and nothing else would say so. What
  it cannot do is ATTRIBUTE the growth: no per-compile baseline is stored, so it names the biggest
  contributors and says plainly that is what it is naming. `FUARAN_FABLE_BUDGET_SECONDS` overrides
  the number — which is also how the warning itself is falsified end to end, in
  `fable-check.tests.ps1`.

  THE PROJECT CRACK, AND WHY `--noCache` STAYS (Phase 1622 — a spike, closed as a no-op). The
  `parsed` column above is the biggest single item in this stage, and it is worth knowing exactly
  what it is before anyone tries to remove it.

  WHAT WAS MEASURED, on Fable 5.0.0, 2026-09-08, this machine. `dotnet fable` was run against a
  subject twice: once cold with `--noCache`, and once re-entering an out directory a previous
  non-`--noCache` run had left a `fable_modules/project_cracked.json` in, with one source touched
  so the emit could not be skipped. Two subjects, chosen for the two ends of the graph-size range:

                                     parsed      emitted    wall
    FableLaws (10-project closure)
      cold, --noCache                48,756ms    18,680ms   68.4s
      crack served from cache            116ms   20,200ms   21.8s
    Fuaran.UI.ServerDriven (8)
      cold, --noCache                46,358ms    17,095ms   64.2s
      crack served from cache            105ms   19,032ms   20.5s

  Read that as an observation and not as a theory of Fable's internals: with the crack served from
  disk the `parsed` figure falls by ~99.8% while the EMIT figure does not move, and the whole
  saving shows up in the wall clock. Whatever else `parsed` may cover, essentially all of it is
  work a persisted crack removes — and the compile still does everything it did before. `--noRestore`
  is NOT the lever: it took `parsed` to 43.1s / 45.9s, ~10%, so the cost is the per-project MSBuild
  design-time builds themselves. The cost scales with the PROJECT GRAPH, steeply and not linearly:
  `Fuaran.UI` has a closure of one project and Phase 1620 measured its parse at 1,500ms, against
  ~46s for a closure of eight. So the `parsed` column is a graph-size meter, not a source-size one,
  and a stage that grows a reference edge grows here.

  THE PHASE'S PREMISE WAS THAT THIS FIGURE WAS THE CRACK **PLUS** THE FCS PARSE, and that the split
  decided whether caching the crack was worth anything. The split is ~100/0, so it is worth a great
  deal — which is why the answer below is a refusal on safety grounds rather than on value.

  WHY IT IS NOT DONE. Two findings, and either alone is decisive.

    * FABLE 5.0.0 EXPOSES NO SEAM THAT SEPARATES THE CRACK FROM THE OUTPUT. It already persists the
      crack — `CacheInfo.TryRead`/`Write` over `fable_modules/project_cracked.json`, invalidated by
      `isOlderThanCache` — and `--noCache` is the ONE flag that suppresses it, the same flag that
      suppresses reuse of emitted files ("Recompile all files, including sources from packages",
      per `--help`). The CLI carries no argument that injects a crack, names a pre-computed one, or
      selects a resolver; the `ProjectCrackerResolver` interface is a LIBRARY seam, reachable only
      by a bespoke host that references `Fable.Compiler` and reimplements the compile pipeline. That
      host would no longer be the `dotnet fable` a consumer runs, which is the one thing this stage
      exists to be. (`--noCache` is a real, validated flag, not a tolerated unknown: Fable rejects
      `--zzzNotAFlag` by name, and a `--noCache` run demonstrably writes no `project_cracked.json`.)

    * THE PERSISTED CRACK IS INSEPARABLE FROM `fable_modules`, AND REUSING THAT DIRECTORY
      REINTRODUCES A STALE-GREEN CLASS THIS ESTATE HAS ALREADY RECORDED. The record is bound by
      absolute path to the `fable_modules` inside its own out directory, so reusing it means
      persisting that directory — which also holds the COPIED PACKAGE SOURCES and their emitted
      JavaScript, in version-named folders that a same-version repack off the local feed does not
      rename. Fable copies such a folder only if it is absent. So a repacked `Fuaran.Core.*` at an
      unchanged version would be compiled from the OLD copied sources, silently and green. Phase
      1619's content address does not see it either: it hashes the PIN in
      `Directory.Packages.props`, which a same-version repack does not move.

  WHAT A LATER PHASE WOULD HAVE TO SETTLE, stated so it is a decision and not a rediscovery. It
  would need its own content address over the CRACK's inputs alone (the fsproj graph, the governing
  MSBuild files, the package versions, the tool version) deciding whether to keep or destroy
  `project_cracked.json`; an answer to the same-version-repack hole above, which no file hash in
  this repo can currently supply; a reconciliation with `Get-TreeScratchRoot`'s wipe-at-start
  invariant, which exists because two gates once deleted each other's output mid-compile; and — the
  part that is a policy call rather than an engineering one — a deliberate decision to let the FULL
  lane consult a cache, which Phase 1619 made structurally unreachable there on purpose. Three of
  those are work; the fourth is not this script's to take.

  SO THE RULE STANDS UNCHANGED: every compile here passes `--noCache`, and the only mechanism that
  avoids paying for one is Phase 1619's skip, which declines to INVOKE Fable at all and never asks
  Fable to trust anything.

  METHOD NOTES — both learned the hard way, both recorded in `CLAUDE.md` under "Fable method
  traps", and both binding on anything added here:

    * `dotnet fable`'s exit code is read DIRECTLY from `$LASTEXITCODE`. It is never piped — a pipe
      reports the LAST command's status, so `dotnet fable ... | tail` reports `tail`'s success and a
      failed compile reads as a pass. CAPTURING is not piping: `$out = & dotnet fable ...` leaves
      `$LASTEXITCODE` carrying Fable's own status, and capture is what concurrency forces — a dozen
      interleaved live streams cannot be read.
    * The output directory is never `obj/`. Fable writes beside the project's own build
      intermediates there, re-parses part of the project, and reports errors against files the
      change never touched — an hour of looking in the wrong place.

  `dotnet fable` is a .NET local tool and `node` is a real executable, so the workspace's
  `Invoke-Npm` / `Invoke-Npx` convention does not apply here: there is no npm/npx PowerShell shim
  in this path to be defeated by.

  EXIT 0 = every portability compile succeeded AND (where Node is present) the law harness reported
  zero violations on both pipelines with byte-identical output.
#>
[CmdletBinding()]
param(
    # Skip the client-tier portability compiles (the slow half).
    [switch] $SkipPortability,
    # Skip the law harness (the half that needs Node).
    [switch] $SkipLaws,
    # Keep the emitted JavaScript and the two captured outputs for inspection.
    [switch] $KeepOutput,
    # Print the derived portability set — entries, why each is one, what they cover, and every
    # declared exemption — then exit without compiling anything. This is the surface a reader (or
    # a workflow) consumes instead of re-deriving the list by hand.
    [switch] $List,
    # The directory the derivation walks. Defaults to `src/`, the repo's package tree. Overridden
    # ONLY by `fable-check.tests.ps1` beside this script, which runs this same derivation over a
    # scratch tree to prove the gate can go red. A gate whose scope is redirectable in ordinary use
    # would be no gate at all, so nothing else passes it.
    [string] $SrcRoot,
    # Print the content address of every compile this stage would perform — the entries and the law
    # harness — then exit without compiling anything. The `-List` posture, one question along.
    [switch] $Addresses,
    # Run the go-red proof for the ADDRESS FUNCTION over a scratch tree of plain files and exit. It
    # compiles nothing and costs milliseconds, which is why the same proof also runs automatically
    # at the head of any run that could skip a compile.
    [switch] $ProveAddressing
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$scriptRoot = $PSScriptRoot
$failures = New-Object System.Collections.Generic.List[string]
$stageClock = [Diagnostics.Stopwatch]::StartNew()

# ── The declared cost of this stage (Phase 1620) ────────────────────────────
#
# MEASURED 2026-09-08 on the machine this was written on: 16 logical cores, warm NuGet and MSBuild
# caches, the 12 portability entries the derivation then produced plus the law harness, full lane,
# nothing skipped by address.
#
#   portability, strictly sequential (FUARAN_FABLE_PARALLELISM=1)   288.9s   (12 compiles)
#   portability, concurrent at the default degree of 4              134.1s   2.15x
#   the law harness: .NET run 43.4s + Fable compile 131.5s + node 1.9s      176.8s
#   stage wall-clock, concurrent                                    310.8s
#
# Two things that measurement says and the headline number does not. The portability half is where
# the concurrency is, and it is now the SMALLER half: the law harness runs three legs in sequence
# and its own `--noCache` compile alone is 131.5s, so a stage-level budget is mostly a statement
# about the laws. And per-compile wall-clocks INFLATE under contention — 427.9s of compile in 134.1s
# of wall-clock, against 288.9s of compile when nothing contends — so the rows in the table below
# are what each compile cost ON THIS RUN, never what it would cost alone.
#
# The budget is the stage's WALL CLOCK, because that is what a person waits for and what grew
# unnoticed. It is set with headroom over the measurement above — loose enough that an ordinarily
# slower machine does not trip it, tight enough that a stage which has GAINED a compile does. A
# budget nobody can breach and a budget everybody breaches are the same artefact. 480s is ~1.55x
# the measurement: headroom for a machine appreciably slower than this one, NOT for one half its
# speed. On such a machine the warning fires and is a true statement about that machine's cost,
# which is information rather than noise — and it is warn-only precisely so that reading stays
# available instead of becoming a red gate somebody has to route around.
#
# The one other host that runs this stage is CI's `fable-portability` job, and it runs it
# `-SkipLaws` — the portability half only, 134.1s of the 310.8s above — so a runner around
# twice this machine's cost still sits inside 480s. If it stops doing so the honest answer is
# to raise the number here WITH a fresh measurement beside it, never to widen it silently:
# a budget whose provenance has been edited away is a number, not a measurement.
$FableStageBudgetSeconds = 480

# How many `dotnet fable` processes may run at once. Each runs MSBuild and fsc and holds the whole
# project graph in memory; the estate has already recorded a gate killed for memory (`verify-all`
# exit 143), so this cap is load-bearing rather than tidy. The effective degree is also held at or
# below the machine's logical core count — see `Get-CompileParallelism`.
$FableStageMaxParallelCompiles = 4

# One row per compile this run performed, skipped, or failed — in the order it met them.
$timings = New-Object System.Collections.Generic.List[object]

function Add-Timing {
    param(
        [string] $label,
        [string] $state,
        [double] $seconds,
        [object] $parsedMs = $null,
        [object] $emittedMs = $null
    )
    $timings.Add([pscustomobject]@{
            Label     = $label
            State     = $state
            Seconds   = $seconds
            ParsedMs  = $parsedMs
            EmittedMs = $emittedMs
        })
}

function Get-FableTiming {
    <#
      Fable's own two figures, read out of a captured compile's output: `... parsed in 7319ms` (the
      project graph cracked) and `Fable compilation finished in 15124ms` (the emit). Either may be
      absent — a compile that failed while cracking never reaches the second — and an absent figure
      is `$null`, printed as `-`. Never zero: a zero is a measurement, and nobody made this one.
    #>
    param([string[]] $lines)
    $parsed = $null
    $emitted = $null
    foreach ($line in $lines) {
        if ($line -match 'parsed in (\d+)ms') { $parsed = [int] $Matches[1] }
        elseif ($line -match 'compilation finished in (\d+)ms') { $emitted = [int] $Matches[1] }
    }
    return [pscustomobject]@{ ParsedMs = $parsed; EmittedMs = $emitted }
}

function Get-CompileParallelism {
    <#
      The degree for this run: the compile count, capped by `$FableStageMaxParallelCompiles` and by
      the machine's logical core count, so a two-core CI runner does not inherit the dev box's
      number.

      `FUARAN_FABLE_PARALLELISM` overrides it, and `1` is the sequential lane. That override is not a
      convenience: it is what makes the sequential-versus-concurrent measurement recorded beside the
      budget above REPRODUCIBLE by someone who did not take it, rather than a number to be believed.
      An unparseable or non-positive value falls back to the default — the safety direction here is
      "run the stage", never "fail deciding how to".
    #>
    param([int] $count)
    if ($count -le 0) { return 1 }
    if ($env:FUARAN_FABLE_PARALLELISM) {
        $requested = 0
        if ([int]::TryParse($env:FUARAN_FABLE_PARALLELISM.Trim(), [ref] $requested) -and $requested -ge 1) {
            return [Math]::Min($count, $requested)
        }
    }
    $cap = [Math]::Min($FableStageMaxParallelCompiles, [Math]::Max(1, [Environment]::ProcessorCount))
    return [Math]::Min($count, $cap)
}

function Get-BudgetSeconds {
    # The declared literal, unless the environment names another. Anything unreadable falls back to
    # the literal rather than disabling the check.
    if ($env:FUARAN_FABLE_BUDGET_SECONDS) {
        $requested = 0.0
        $styles = [Globalization.NumberStyles]::Float
        $invariant = [Globalization.CultureInfo]::InvariantCulture
        if ([double]::TryParse($env:FUARAN_FABLE_BUDGET_SECONDS.Trim(), $styles, $invariant, [ref] $requested) -and $requested -gt 0) {
            return $requested
        }
    }
    return [double] $FableStageBudgetSeconds
}

function Write-Stage {
    param([string] $message)
    Write-Host ""
    Write-Host "── Fable: $message ──────────────────────────────" -ForegroundColor Cyan
}

# ── 1. Portability ──────────────────────────────────────────────────────────
#
# The set is DERIVED — see "The derivation" in the header above. Nothing below is a list of
# projects: a project joins the gate by shipping `fable\` sources, and leaves it only by declaring
# `<FablePortabilityExemption>` in its own fsproj.

# Repo-relative, forward slashes — stable across platforms and readable in a failure line.
function ConvertTo-RepoRelative {
    param([string] $path)
    ([IO.Path]::GetRelativePath($repoRoot, $path)) -replace '\\', '/'
}

# `XmlDocument.Load` (rather than an `[xml]` cast over `Get-Content`) so a byte-order mark or a
# declared encoding is handled by the parser rather than by luck.
function Read-ProjectXml {
    param([string] $path)
    $doc = New-Object System.Xml.XmlDocument
    $doc.Load($path)
    $doc
}

# A project ships Fable sources when it packs `*.fs` under `fable\`. The `*.fsproj` half of the
# same Include is deliberately not enough on its own: a project packing only its fsproj would give
# a consumer nothing to compile.
function Test-ShipsFableSources {
    param([System.Xml.XmlDocument] $doc)
    foreach ($content in $doc.SelectNodes('//Content[@PackagePath][@Include]')) {
        if ($content.GetAttribute('PackagePath') -notmatch '^fable[\\/]?$') { continue }
        foreach ($pattern in ($content.GetAttribute('Include') -split ';')) {
            if ($pattern.Trim() -match '\*\.fs$') { return $true }
        }
    }
    return $false
}

# `#if` / `#elif` only — a bare mention of FABLE_COMPILER in a comment or a string is not an arm.
$armPattern = '(?m)^\s*#(if|elif)\b[^\r\n]*\bFABLE_COMPILER\b'

function Test-HasConditionalArm {
    param([string] $projectDir)
    $sources = Get-ChildItem -Path $projectDir -Recurse -Filter *.fs -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
    foreach ($source in $sources) {
        if ([IO.File]::ReadAllText($source.FullName) -match $armPattern) { return $true }
    }
    return $false
}

$srcRoot = if ($SrcRoot) { (Resolve-Path $SrcRoot).Path } else { Join-Path $repoRoot 'src' }
$projects = [ordered]@{}

foreach ($file in (Get-ChildItem -Path $srcRoot -Recurse -Filter *.fsproj -File | Sort-Object FullName)) {
    $doc = Read-ProjectXml $file.FullName
    $dir = $file.Directory.FullName

    $exemptionNode = $doc.SelectSingleNode('//PropertyGroup/FablePortabilityExemption')
    $packableNode = $doc.SelectSingleNode('//PropertyGroup/IsPackable')

    $references = @(
        foreach ($node in $doc.SelectNodes('//ProjectReference[@Include]')) {
            [IO.Path]::GetFullPath((Join-Path $dir ($node.GetAttribute('Include') -replace '\\', [IO.Path]::DirectorySeparatorChar)))
        }
    )

    $projects[$file.FullName] = [pscustomobject]@{
        Path       = $file.FullName
        Relative   = ConvertTo-RepoRelative $file.FullName
        Name       = [IO.Path]::GetFileNameWithoutExtension($file.Name)
        Ships      = Test-ShipsFableSources $doc
        Packable   = -not ($packableNode -and $packableNode.InnerText.Trim() -eq 'false')
        Exemption  = if ($exemptionNode) { ($exemptionNode.InnerText.Trim() -replace '\s+', ' ') } else { '' }
        HasArm     = Test-HasConditionalArm $dir
        References = $references
    }
}

$shipping = @($projects.Values | Where-Object Ships)
$exempt = @($shipping | Where-Object { $_.Exemption })
$gated = @($shipping | Where-Object { -not $_.Exemption })
$gatedPaths = [System.Collections.Generic.HashSet[string]]::new(
    [string[]] @($gated | ForEach-Object Path), [StringComparer]::OrdinalIgnoreCase)

# Rule (a) — the roots: no OTHER gated project references them.
$referenced = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($project in $gated) {
    foreach ($reference in $project.References) {
        if ($gatedPaths.Contains($reference)) { [void] $referenced.Add($reference) }
    }
}

# Rule (a) ∪ rule (b).
$entries = @($gated | Where-Object { (-not $referenced.Contains($_.Path)) -or $_.HasArm } | Sort-Object Relative)

function Get-EntryReason {
    param($project)
    $reasons = @()
    if (-not $referenced.Contains($project.Path)) { $reasons += 'graph root' }
    if ($project.HasArm) { $reasons += 'conditional arm' }
    return ($reasons -join ' + ')
}

# What the entries actually cover, over ProjectReference within the gated set.
$covered = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$pending = New-Object System.Collections.Generic.Queue[string]
foreach ($entry in $entries) { $pending.Enqueue($entry.Path) }
while ($pending.Count -gt 0) {
    $path = $pending.Dequeue()
    if (-not $covered.Add($path)) { continue }
    foreach ($reference in $projects[$path].References) {
        if ($gatedPaths.Contains($reference)) { $pending.Enqueue($reference) }
    }
}

$uncovered = @($gated | Where-Object { -not $covered.Contains($_.Path) })
$orphanArms = @($projects.Values | Where-Object { $_.HasArm -and $_.Packable -and (-not $_.Ships) })

# ── The content address (Phase 1619) ────────────────────────────────────────
#
# See "THE CONTENT-ADDRESSED SKIP" in the header for what the address covers and why. Everything in
# this section is READ-ONLY over the tree; the only thing it writes is one small JSON record per
# compile, under the system temp root.

# WHAT the address covers, versioned. Bump this whenever that set changes and every recorded green
# misses on the next run — which is the correct direction: a record minted under an older, narrower
# notion of "the inputs" is precisely the stale green this mechanism exists to make impossible.
$addressSchema = 'fable-address/1'

# The lane arrives through the environment `run.ps1` already sets for the test stage, so the stage
# needs no parameter of its own and cannot be handed a lane the rest of the gate did not run under.
$lane = if ($env:FUARAN_TEST_LANE) { $env:FUARAN_TEST_LANE.Trim().ToLowerInvariant() } else { 'full' }

# ONLY the two narrow lanes may consult a record. `full`, an unset variable and a lane name this
# script has never heard of all compile. That the unknown case resolves to "compile" rather than to
# "skip" is the whole safety posture in one line.
$laneMaySkip = $lane -in @('pure', 'fast')

# Keyed by the resolved source root: a second worktree of this repo, and the scratch tree
# `fable-check.tests.ps1` points `-SrcRoot` at, each keep their own records rather than answering
# for one another.
$addressRoot = $null

function Get-TextSha256 {
    param([string] $text)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text))) -replace '-', '').ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

function Get-TreeScratchRoot {
    <#
      A scratch output root for THIS tree.

      The scratch roots live outside the repo on purpose (see the portability root's own note for
      the MAX_PATH reasoning), and that is exactly what makes them shared: every worktree of this
      repo resolves the same fixed path under %TEMP%. Both stages that use one WIPE it at start to
      guarantee a clean compile, so two gates running at once delete each other's output mid-run.

      Phase 1605 hit it: 31 path exceptions and 1,867 cascading F# errors naming `Fuaran.Core.*`,
      which reads as a real portability break and is not one. Phase 1619's fold hit the same thing
      from the other side, its verify arm colliding with a sibling worker's gate.

      Keying the leaf on this script's own location gives one root per worktree — the same trick
      `Get-RecordPath` already plays, but keyed on the TREE rather than on `-SrcRoot`: the address
      records are a cache that two trees over identical sources SHOULD share, while scratch output
      is per-invocation and must not be. Twelve hex characters keeps the path well short of the
      MAX_PATH headroom the out-of-tree placement buys.

      Not per-PROCESS: two concurrent gates in one worktree would still collide, and that is
      deliberate. A stable per-tree path is what makes a failed compile's output still there to
      read afterwards, which is most of why these roots are outside the repo in the first place.
    #>
    param([Parameter(Mandatory)] [string] $Name)

    if (-not $script:treeScratchKey) {
        $script:treeScratchKey = (Get-TextSha256 $PSScriptRoot.ToLowerInvariant()).Substring(0, 12)
    }
    return Join-Path ([IO.Path]::GetTempPath()) "$Name-$script:treeScratchKey"
}

$fileHashCache = @{}
$governingCache = @{}
$toolVersionCache = @{}
$projectXmlCache = @{}
$projectInputCache = @{}

function Clear-AddressCaches {
    # The caches assume the tree does not move under them, which is true of a gate run and false of
    # the proof below, which perturbs files on purpose.
    $script:fileHashCache = @{}
    $script:governingCache = @{}
    $script:toolVersionCache = @{}
    $script:projectXmlCache = @{}
    $script:projectInputCache = @{}
}

function Get-CachedFileSha256 {
    param([string] $path)
    if ($fileHashCache.ContainsKey($path)) { return $fileHashCache[$path] }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    $fileHashCache[$path] = $hash
    return $hash
}

# The MSBuild files that govern a project without living beside it. `Nullable` and `LangVersion` are
# set in this repo's root `Directory.Build.props`, not in any fsproj, so an address blind to them
# would be blind to the two properties the portability stage exists to vary.
$governingFileNames = @('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'nuget.config', 'global.json')

function Get-NearestFile {
    # The first file of that name at or above $startDir — MSBuild's own resolution order for the
    # names above, and the same walk `dotnet` performs for a tool manifest.
    param([string] $startDir, [string] $name)
    $dir = $startDir
    while ($dir) {
        $candidate = Join-Path $dir $name
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return (Resolve-Path -LiteralPath $candidate).Path }
        $parent = Split-Path -Parent $dir
        if (-not $parent -or $parent -eq $dir) { return $null }
        $dir = $parent
    }
    return $null
}

function Get-GoverningFiles {
    param([string] $projectDir)
    if ($governingCache.ContainsKey($projectDir)) { return $governingCache[$projectDir] }
    $found = @(foreach ($name in $governingFileNames) {
            $path = Get-NearestFile $projectDir $name
            if ($path) { $path }
        })
    $governingCache[$projectDir] = $found
    return $found
}

function Get-FableToolVersion {
    # Resolved from the entry's own directory upward, so the proof's scratch tree can carry its own
    # manifest and a bump there is a real bump rather than a simulated one. `$null` on anything
    # unreadable, which propagates to "no address" and therefore to "compile".
    param([string] $startDir)
    if ($toolVersionCache.ContainsKey($startDir)) { return $toolVersionCache[$startDir] }
    $version = $null
    $manifest = Get-NearestFile $startDir (Join-Path '.config' 'dotnet-tools.json')
    if ($manifest) {
        try {
            $parsed = (Get-Content -Raw -LiteralPath $manifest | ConvertFrom-Json).tools.fable.version
            if (-not [string]::IsNullOrWhiteSpace($parsed)) { $version = [string] $parsed }
        }
        catch { $version = $null }
    }
    $toolVersionCache[$startDir] = $version
    return $version
}

function Get-CachedProjectXml {
    param([string] $path)
    if ($projectXmlCache.ContainsKey($path)) { return $projectXmlCache[$path] }
    $doc = $null
    try { $doc = Read-ProjectXml $path } catch { $doc = $null }
    $projectXmlCache[$path] = $doc
    return $doc
}

function Get-ProjectInputs {
    <#
      One project's contribution to an address: the files whose bytes can change what Fable emits,
      and the projects it references. `Ok = $false` means something could not be resolved — a
      missing project, an unreadable fsproj, a `<Compile Include>` naming a file that is not there —
      and every such case ends as "no address", so the compile happens.

      The `.fs` set is taken by walking the project's directory rather than by reading `<Compile>`,
      which is deliberately a SUPERSET: a file the project does not compile can only cause a
      spurious MISS, never a false hit. Linked `<Compile>` items pointing outside the directory are
      added on top, because for those the walk is a subset and the error would run the other way.
    #>
    param([string] $projectPath)

    if ($projectInputCache.ContainsKey($projectPath)) { return $projectInputCache[$projectPath] }

    $result = [pscustomobject]@{ Ok = $false; Files = @(); References = @() }

    if (Test-Path -LiteralPath $projectPath -PathType Leaf) {
        $doc = Get-CachedProjectXml $projectPath
        if ($doc) {
            $dir = (Resolve-Path -LiteralPath (Split-Path -Parent $projectPath)).Path
            $files = New-Object System.Collections.Generic.List[string]
            $files.Add((Resolve-Path -LiteralPath $projectPath).Path)

            foreach ($governing in (Get-GoverningFiles $dir)) { $files.Add($governing) }

            foreach ($source in (Get-ChildItem -LiteralPath $dir -Recurse -File -ErrorAction SilentlyContinue |
                    Where-Object { ($_.Extension -eq '.fs' -or $_.Extension -eq '.fsi') -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })) {
                $files.Add($source.FullName)
            }

            $ok = $true
            $references = New-Object System.Collections.Generic.List[string]

            foreach ($node in $doc.SelectNodes('//ProjectReference[@Include]')) {
                $reference = [IO.Path]::GetFullPath((Join-Path $dir ($node.GetAttribute('Include') -replace '\\', [IO.Path]::DirectorySeparatorChar)))
                if (-not (Test-Path -LiteralPath $reference -PathType Leaf)) { $ok = $false; break }
                $references.Add($reference)
            }

            if ($ok) {
                foreach ($node in $doc.SelectNodes('//Compile[@Include]')) {
                    foreach ($pattern in ($node.GetAttribute('Include') -split ';')) {
                        $trimmed = $pattern.Trim()
                        if (-not $trimmed) { continue }
                        $full = [IO.Path]::GetFullPath((Join-Path $dir ($trimmed -replace '\\', [IO.Path]::DirectorySeparatorChar)))
                        # Inside the project directory the walk above already has it.
                        if ($full.StartsWith($dir + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { continue }
                        if ($trimmed -match '[*?]') {
                            foreach ($match in (Get-ChildItem -Path $full -File -ErrorAction SilentlyContinue)) { $files.Add($match.FullName) }
                        }
                        elseif (Test-Path -LiteralPath $full -PathType Leaf) { $files.Add((Resolve-Path -LiteralPath $full).Path) }
                        else { $ok = $false; break }
                    }
                    if (-not $ok) { break }
                }
            }

            if ($ok) {
                $result = [pscustomobject]@{ Ok = $true; Files = @($files); References = @($references) }
            }
        }
    }

    $projectInputCache[$projectPath] = $result
    return $result
}

# Under Fable the ENTRY project's properties govern the whole transpiled graph, so these four (five,
# counting the multi-targeting spelling) are read from the entry itself and named in the address.
#
# BE CLEAR ABOUT WHAT THIS ADDS, because it is easy to overstate: for a property declared in a file
# the address already hashes — the entry's own fsproj, or a `Directory.Build.props` above it — this
# component is REDUNDANT, and measurably so: emptying this list leaves the proof's Nullable
# assertion passing, carried entirely by the fsproj's content hash. It is kept because redundancy
# runs in the safe direction and because naming these four makes the manifest say what the address
# is FOR, not because it widens what the address can see. Nothing here reads an environment-set or
# command-line-set property, and a compile whose properties arrive that way is outside what any of
# this can address.
$fableRelevantProperties = @('Nullable', 'LangVersion', 'DefineConstants', 'TargetFramework', 'TargetFrameworks')

function Get-CompileAddress {
    <#
      The content address of one compile, or `$null` when any input could not be resolved. `$null`
      never means "unchanged"; it means "compile", and every caller reads it that way.
    #>
    param([string] $entryPath, [string] $semantics)

    if (-not (Test-Path -LiteralPath $entryPath -PathType Leaf)) { return $null }

    $entryPath = (Resolve-Path -LiteralPath $entryPath).Path
    $entryDir = Split-Path -Parent $entryPath

    $toolVersion = Get-FableToolVersion $entryDir
    if (-not $toolVersion) { return $null }

    $entryDoc = Get-CachedProjectXml $entryPath
    if (-not $entryDoc) { return $null }

    # The transitive closure over ProjectReference — the graph Fable reads, which is wider than the
    # gated set the derivation above selects over.
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $pending = New-Object System.Collections.Generic.Queue[string]
    $pending.Enqueue($entryPath)
    $files = New-Object System.Collections.Generic.List[string]

    while ($pending.Count -gt 0) {
        $path = $pending.Dequeue()
        if (-not $seen.Add($path)) { continue }
        $inputs = Get-ProjectInputs $path
        if (-not $inputs.Ok) { return $null }
        foreach ($file in $inputs.Files) { $files.Add($file) }
        foreach ($reference in $inputs.References) { $pending.Enqueue($reference) }
    }

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("schema $addressSchema")
    $lines.Add("entry $(ConvertTo-RepoRelative $entryPath)")
    $lines.Add("semantics $semantics")
    $lines.Add("tool fable=$toolVersion")

    foreach ($name in $fableRelevantProperties) {
        $values = @(foreach ($node in $entryDoc.SelectNodes("//PropertyGroup/$name")) { $node.InnerText.Trim() })
        $lines.Add("prop $name=$($values -join '|')")
    }

    $unique = @($files | Sort-Object -Unique)
    foreach ($file in ($unique | Sort-Object { ConvertTo-RepoRelative $_ })) {
        $lines.Add("file $(Get-CachedFileSha256 $file) $(ConvertTo-RepoRelative $file)")
    }

    return Get-TextSha256 ($lines -join "`n")
}

# ── The record ──────────────────────────────────────────────────────────────

function Get-RecordPath {
    param([string] $subject)
    if (-not $addressRoot) {
        $key = (Get-TextSha256 $srcRoot.ToLowerInvariant()).Substring(0, 16)
        $script:addressRoot = Join-Path ([IO.Path]::GetTempPath()) (Join-Path 'fuaran-fable-addresses' $key)
    }
    return Join-Path $addressRoot ((($subject -replace '[^A-Za-z0-9._-]', '_')) + '.json')
}

function Test-RecordedGreen {
    <#
      True only on a positive match of every field: the schema this binary writes, the address, and
      the exact compile the record stands for. An absent, unreadable or stale-schema record is a
      miss, never an error.
    #>
    param([string] $subject, [string] $address, [string] $semantics)
    if (-not $address) { return $null }
    $path = Get-RecordPath $subject
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    try { $record = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json } catch { return $null }
    if ($record.schema -ne $addressSchema) { return $null }
    if ($record.address -cne $address) { return $null }
    if ($record.semantics -cne $semantics) { return $null }
    return $record
}

function Write-RecordedGreen {
    param([string] $subject, [string] $address, [string] $semantics)
    if (-not $address) { return }
    $path = Get-RecordPath $subject
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
    ([pscustomobject]@{
            schema      = $addressSchema
            subject     = $subject
            address     = $address
            semantics   = $semantics
            lane        = $lane
            recordedUtc = [DateTime]::UtcNow.ToString('o')
        } | ConvertTo-Json) | Set-Content -LiteralPath $path -Encoding utf8NoBOM
}

function Clear-RecordedGreen {
    # A red compile deletes the record outright rather than merely failing to write one, so a tree
    # later restored to a state that once passed still recompiles. Strictly more conservative than
    # soundness requires, and deliberately so.
    param([string] $subject)
    Remove-Item -LiteralPath (Get-RecordPath $subject) -Force -ErrorAction SilentlyContinue
}

# The exact compile each subject's record stands for. A flag added to either line changes the
# semantics string, which misses every record written before it — which is the point of recording
# the command rather than assuming it.
$portabilitySemantics = 'dotnet fable <entry> -o <tmp> --noCache'
$lawsProject = Join-Path $PSScriptRoot 'FableLaws.fsproj'

$nodeVersionCache = $null

function Get-NodeVersionTag {
    # The laws record stands for a byte-comparison one side of which is produced by `node`, so the
    # runtime that produced it is an input to that green in a way it is not for a mere compile.
    # Probed lazily — nothing but the laws subject needs it.
    if ($null -ne $script:nodeVersionCache) { return $script:nodeVersionCache }
    $version = 'absent'
    if (Get-Command node -CommandType Application -ErrorAction SilentlyContinue) {
        try { $version = (@(& node --version) | Select-Object -First 1).Trim() } catch { $version = 'unreadable' }
    }
    $script:nodeVersionCache = $version
    return $version
}

function Get-LawsSemantics {
    "dotnet run -c Release + dotnet fable -o output --noCache + node $(Get-NodeVersionTag), byte-compared"
}

function Get-AddressSubjects {
    # Every compile THIS invocation can perform, in the order it performs them — so `-Addresses`
    # under `-SkipLaws` reports the run that would actually happen rather than a hypothetical one.
    $subjects = @(if (-not $SkipPortability) {
            foreach ($entry in $entries) {
                [pscustomobject]@{ Subject = $entry.Name; Label = $entry.Relative; Entry = $entry.Path; Semantics = $portabilitySemantics }
            }
        })
    if ((-not $SkipLaws) -and (Test-Path -LiteralPath $lawsProject)) {
        $subjects += [pscustomobject]@{ Subject = 'FableLaws'; Label = (ConvertTo-RepoRelative $lawsProject); Entry = $lawsProject; Semantics = (Get-LawsSemantics) }
    }
    return $subjects
}

if ($Addresses) {
    Write-Host "Fable content addresses — lane '$lane' ($(if ($laneMaySkip) { 'may skip a matching record' } else { 'always compiles' }))"
    # Named rather than merely used: the records are outside the repo and keyed by a hash, so
    # without this line the one place a reader would look to clear or inspect them is unguessable.
    Write-Host "records $(Split-Path -Parent (Get-RecordPath 'any'))"
    Write-Host ""
    foreach ($subject in (Get-AddressSubjects)) {
        $address = Get-CompileAddress $subject.Entry $subject.Semantics
        $recorded = Test-RecordedGreen $subject.Subject $address $subject.Semantics
        $state = if (-not $address) { 'NO ADDRESS — will compile' } elseif ($recorded) { "recorded green (lane $($recorded.lane), $($recorded.recordedUtc))" } else { 'no recorded green' }
        Write-Host ("  {0,-58} {1}" -f $subject.Label, ($address ?? '-'))
        Write-Host ("  {0,-58} {1}" -f '', $state) -ForegroundColor DarkGray
    }
    exit 0
}

if ($List) {
    Write-Host "Fable portability set — derived from $($shipping.Count) package(s) shipping fable\ sources"
    Write-Host ""
    Write-Host "  entries ($($entries.Count)) — Fable-compiled directly, under their own properties:"
    foreach ($entry in $entries) {
        Write-Host ("    {0,-58} ({1})" -f $entry.Relative, (Get-EntryReason $entry))
    }
    Write-Host ""
    Write-Host "  covered: $($covered.Count) of $($gated.Count) gated — entered directly or reached by ProjectReference."
    foreach ($project in $uncovered) {
        Write-Host "    UNCOVERED $($project.Relative)" -ForegroundColor Red
    }
    if ($exempt.Count -gt 0) {
        Write-Host ""
        Write-Host "  exempt ($($exempt.Count)) — declared in the project's own fsproj:"
        foreach ($project in $exempt) {
            Write-Host "    $($project.Relative)" -ForegroundColor Yellow
            Write-Host "      $($project.Exemption)" -ForegroundColor DarkGray
        }
    }
    foreach ($project in $orphanArms) {
        Write-Host "    ORPHAN ARM $($project.Relative) — conditional arm, no fable\ sources" -ForegroundColor Red
    }
    exit 0
}

# ── The go-red proof for the address function (Phase 1619) ──────────────────

function Invoke-AddressingProof {
    <#
      Proves the address MOVES when the things it claims to cover move. It builds a scratch tree of
      plain files, perturbs REAL INPUTS on disk, and re-derives through the same `Get-CompileAddress`
      the stage uses — never by editing the manifest the hash is taken over, which would prove only
      that SHA-256 is sensitive to its input.

      Nothing is compiled, so it costs milliseconds and can therefore run on every invocation that
      could skip a compile — which is exactly when a broken address function would serve a stale
      green. Returns the failures it found; an empty list means every assertion held.
    #>
    param([switch] $Quiet)

    $proofRoot = Get-TreeScratchRoot 'fuaran-fable-address-proof'
    $found = New-Object System.Collections.Generic.List[string]

    function New-ProofProject {
        param([string] $root, [string] $name, [string[]] $references = @(), [string] $nullable = 'enable')
        $dir = Join-Path $root $name
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        $referenceItems = ($references | ForEach-Object { "    <ProjectReference Include=`"..\$_\$_.fsproj`" />" }) -join "`n"
        @"
<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>$nullable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Library.fs" />
  </ItemGroup>
  <ItemGroup>
$referenceItems
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $dir "$name.fsproj") -Encoding utf8NoBOM
        "module Proof.$name`n`nlet describe () = `"$name`"`n" | Set-Content -LiteralPath (Join-Path $dir 'Library.fs') -Encoding utf8NoBOM
    }

    function Get-ProofAddress {
        param([string] $root, [string] $name)
        # The caches assume a tree that does not move; this one moves on purpose.
        Clear-AddressCaches
        return Get-CompileAddress (Join-Path (Join-Path $root $name) "$name.fsproj") 'proof'
    }

    function Assert-Proof {
        param([string] $what, [bool] $held)
        if ($held) { if (-not $Quiet) { Write-Host "    ok   $what" -ForegroundColor DarkGray } }
        else { $found.Add("addressing proof: $what") }
    }

    Remove-Item -Recurse -Force $proofRoot -ErrorAction SilentlyContinue
    try {
        New-Item -ItemType Directory -Force -Path (Join-Path $proofRoot '.config') | Out-Null
        '<Project><PropertyGroup><LangVersion>latest</LangVersion></PropertyGroup></Project>' |
            Set-Content -LiteralPath (Join-Path $proofRoot 'Directory.Build.props') -Encoding utf8NoBOM
        $manifestPath = Join-Path (Join-Path $proofRoot '.config') 'dotnet-tools.json'
        '{"version":1,"isRoot":true,"tools":{"fable":{"version":"1.0.0","commands":["fable"]}}}' |
            Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM

        New-ProofProject -root $proofRoot -name 'Leaf'
        New-ProofProject -root $proofRoot -name 'Root' -references @('Leaf')

        $rootBefore = Get-ProofAddress $proofRoot 'Root'
        $leafBefore = Get-ProofAddress $proofRoot 'Leaf'

        Assert-Proof 'an address is derived at all' ([bool] $rootBefore)

        # Determinism first: an address that moved on its own would make every skip a miss, which
        # LOOKS like a working gate and is a silently broken cache.
        Assert-Proof 'the same tree yields the same address twice' ((Get-ProofAddress $proofRoot 'Root') -ceq $rootBefore)

        # (a) one byte in a TRANSITIVELY-REFERENCED source — the file the entry never names.
        Add-Content -LiteralPath (Join-Path (Join-Path $proofRoot 'Leaf') 'Library.fs') -Value '// one byte' -Encoding utf8NoBOM
        $rootAfterLeaf = Get-ProofAddress $proofRoot 'Root'
        Assert-Proof 'a one-byte edit to a transitively-referenced .fs changes the address' ($rootAfterLeaf -cne $rootBefore)

        # (b) the Fable tool version — every address, not merely the one that happened to be checked.
        '{"version":1,"isRoot":true,"tools":{"fable":{"version":"2.0.0","commands":["fable"]}}}' |
            Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
        $rootAfterTool = Get-ProofAddress $proofRoot 'Root'
        $leafAfterTool = Get-ProofAddress $proofRoot 'Leaf'
        Assert-Proof 'a Fable tool-version bump changes the entry address' ($rootAfterTool -cne $rootAfterLeaf)
        Assert-Proof 'a Fable tool-version bump changes EVERY address' ($leafAfterTool -cne $leafBefore)

        # The entry's own Fable-relevant properties — under Fable these govern the whole graph.
        (Get-Content -Raw -LiteralPath (Join-Path (Join-Path $proofRoot 'Root') 'Root.fsproj')) -replace '<Nullable>enable</Nullable>', '<Nullable>disable</Nullable>' |
            Set-Content -LiteralPath (Join-Path (Join-Path $proofRoot 'Root') 'Root.fsproj') -Encoding utf8NoBOM
        Assert-Proof "a change to the entry's Nullable setting changes the address" ((Get-ProofAddress $proofRoot 'Root') -cne $rootAfterTool)

        # An unresolvable graph must yield NO address — the safety direction, asserted rather than
        # assumed, because it is the branch that decides whether an unknown tree skips or compiles.
        Remove-Item -Recurse -Force (Join-Path $proofRoot 'Leaf')
        Assert-Proof 'a missing referenced project yields no address (so the compile happens)' (-not (Get-ProofAddress $proofRoot 'Root'))
    }
    finally {
        Remove-Item -Recurse -Force $proofRoot -ErrorAction SilentlyContinue
        Clear-AddressCaches
    }

    return $found
}

if ($ProveAddressing) {
    Write-Host ''
    Write-Host '── Fable: address go-red proof ───────────────────────────' -ForegroundColor Cyan
    $proofFailures = Invoke-AddressingProof
    Write-Host ''
    if ($proofFailures.Count -gt 0) {
        Write-Host "==== address go-red proof: FAILED ($($proofFailures.Count))" -ForegroundColor Red
        foreach ($f in $proofFailures) { Write-Host "     $f" -ForegroundColor Red }
        exit 1
    }
    Write-Host '==== address go-red proof: green' -ForegroundColor Green
    exit 0
}

# The proof runs automatically whenever a skip is possible — a broken address function is only
# dangerous on the lane that consults one, and this is the last moment before it does.
if ($laneMaySkip) {
    Write-Stage "address go-red proof (lane '$lane' may skip a matching compile)"
    $proofFailures = Invoke-AddressingProof -Quiet
    if ($proofFailures.Count -gt 0) {
        # Skipping is withdrawn, not merely reported: an address function that cannot be shown to
        # move is one no compile may be skipped on.
        $laneMaySkip = $false
        foreach ($f in $proofFailures) { $failures.Add($f) }
        Write-Host '  FAILED — every compile will run; see the failures below.' -ForegroundColor Red
    }
    else {
        Write-Host '  green — the address moves with the sources, the tool version and the entry properties.' -ForegroundColor DarkGray
    }
}

# Outside the repo tree on purpose: Fable emits a deep `fable_modules/` graph, and a deep output
# path under an already-deep worktree hits MAX_PATH, where fsc fails without a readable error.
# Keyed per tree because being outside the repo is precisely what made every worktree share one
# directory — and this stage wipes its root at start. See `Get-TreeScratchRoot`.
$portabilityRoot = Get-TreeScratchRoot 'fuaran-fable-portability'

if (-not $SkipPortability) {
    Write-Stage "portability — $($entries.Count) entries covering $($covered.Count) of $($gated.Count) gated projects (lane '$lane': $(if ($laneMaySkip) { 'an unchanged entry may be skipped by address' } else { 'every entry compiles' }))"

    # Printed because it is not guessable and it is where a confusing failure comes from: a wiped
    # or half-wiped output root surfaces as path exceptions and cascading F# errors that name
    # `Fuaran.Core.*` and read as a portability break. Seeing the root is most of the diagnosis.
    Write-Host "  output root: $portabilityRoot" -ForegroundColor DarkGray

    foreach ($project in $exempt) {
        Write-Host "  EXEMPT $($project.Relative) — $($project.Exemption)" -ForegroundColor Yellow
    }

    # A derivation that stops seeing part of the tree is the silent gap this stage exists to close,
    # so it fails rather than quietly compiling a smaller set.
    foreach ($project in $uncovered) {
        $failures.Add("$($project.Relative) ships fable\ sources, is not exempt, and no entry reaches it")
    }

    foreach ($project in $orphanArms) {
        $failures.Add("$($project.Relative) holds a #if FABLE_COMPILER arm but packs no fable\ sources — the arm is compiled by nothing")
    }

    Remove-Item -Recurse -Force $portabilityRoot -ErrorAction SilentlyContinue

    $skipped = 0

    # The address is derived, and the skip decided, HERE — in this runspace, before anything runs
    # concurrently. A parallel runspace carries none of this script's functions, caches or record
    # store, so what the concurrency below covers is exactly one thing: invoking `dotnet fable`.
    $queue = New-Object System.Collections.Generic.List[object]

    foreach ($project in $entries) {
        $address = Get-CompileAddress $project.Path $portabilitySemantics

        # The full lane never reaches the second operand: `$laneMaySkip` is false there, so the
        # record is written below and never read, and the skip is unreachable rather than declined.
        $recorded = if ($laneMaySkip) { Test-RecordedGreen $project.Name $address $portabilitySemantics } else { $null }

        if ($recorded) {
            # A NAMED skip, never a silent one — the posture the Node-absent branch below takes, and
            # the reason the address is printed rather than merely consulted.
            $skipped++
            Write-Host "  SKIPPED BY ADDRESS $($project.Relative)" -ForegroundColor Yellow
            Write-Host "    address $address" -ForegroundColor DarkGray
            Write-Host "    recorded green in lane '$($recorded.lane)' at $($recorded.recordedUtc)" -ForegroundColor DarkGray
            Add-Timing $project.Relative 'skipped' 0
            continue
        }

        $queue.Add([pscustomobject]@{
                Name     = $project.Name
                Relative = $project.Relative
                Reason   = (Get-EntryReason $project)
                Path     = $project.Path
                Address  = $address
                # Its OWN leaf under the per-tree root. Two compiles must never share one directory:
                # that is the collision `Get-TreeScratchRoot` answers between gates, met again here
                # between the compiles of a single gate.
                OutDir   = (Join-Path $portabilityRoot $project.Name)
            })
    }

    $degree = Get-CompileParallelism $queue.Count
    $completed = @()

    if ($queue.Count -gt 0) {
        Write-Host "  parallelism $degree over $($queue.Count) compile(s)" -ForegroundColor DarkGray

        try {
            $completed = @($queue | ForEach-Object -ThrottleLimit $degree -Parallel {
                    # A parallel runspace starts with its own preference variables and its own
                    # location. `Continue` so that a native command writing to stderr under `2>&1`
                    # cannot terminate the runspace before its exit code is read — the exit code is
                    # what decides a compile, never the stream. The location is set so the compile
                    # runs from exactly where it ran when it ran in sequence: Fable prints paths
                    # relative to it, and every argument below is absolute regardless.
                    $ErrorActionPreference = 'Continue'
                    Set-Location $using:scriptRoot

                    $job = $_
                    $clock = [Diagnostics.Stopwatch]::StartNew()

                    # ASSIGNMENT, not a pipe. The standing rule is that `dotnet fable` is never
                    # piped, because a pipeline reports its LAST command's status and a failed
                    # compile then reads as a pass. Capturing does not move `$LASTEXITCODE`; it
                    # still carries Fable's own. And capture is what concurrency forces — a dozen
                    # interleaved live streams cannot be read, so each compile's output is replayed
                    # whole, in derivation order, by the collation below.
                    #
                    # --noCache is mandatory: a stale .fable cache can serve a compile that no
                    # longer reflects the sources, which is the one answer this stage must never
                    # give. The address does not soften that — it decides whether to INVOKE Fable at
                    # all, and it moves with every byte Fable would read.
                    #
                    # It also costs almost all of the `parsed` figure below, and that was
                    # investigated rather than assumed: see "THE PROJECT CRACK, AND WHY `--noCache`
                    # STAYS" in the header for the measurement and for the two findings that closed
                    # Phase 1622 as a no-op. Do not drop this flag to recover that time without
                    # answering both of them.
                    $output = & dotnet fable $job.Path -o $job.OutDir --noCache 2>&1
                    $exit = $LASTEXITCODE
                    $clock.Stop()

                    [pscustomobject]@{
                        Name    = $job.Name
                        Exit    = $exit
                        Seconds = $clock.Elapsed.TotalSeconds
                        Output  = @($output | ForEach-Object { [string] $_ })
                    }
                })
        }
        catch {
            # A runspace that died takes its result with it. Recorded as a failure of the stage
            # rather than rethrown, so the collation below still names every compile that has no
            # result — which is more useful than one exception naming none of them.
            $failures.Add("the concurrent portability compiles raised: $($_.Exception.Message)")
        }

        $byName = @{}
        foreach ($result in $completed) { $byName[$result.Name] = $result }

        # Collated in DERIVATION order, not completion order, so the stage reads the same way it did
        # when it ran in sequence and two runs of an unchanged tree print the same thing.
        foreach ($job in $queue) {
            Write-Host "  fable $($job.Relative)  [$($job.Reason)]" -ForegroundColor DarkGray

            $result = $byName[$job.Name]

            if (-not $result) {
                # A queued compile with NO result is a failure, never an absence. Nothing proves it
                # succeeded, and the record is cleared for the same reason a red compile clears it.
                $failures.Add("Fable portability compile for $($job.Relative) returned no result — the job did not complete")
                Write-Host "  NO RESULT: $($job.Relative)" -ForegroundColor Red
                Clear-RecordedGreen $job.Name
                Add-Timing $job.Relative 'NO RESULT' 0
                continue
            }

            foreach ($line in $result.Output) { Write-Host "    $line" }

            $figures = Get-FableTiming $result.Output

            if ($result.Exit -ne 0) {
                $failures.Add("Fable portability compile FAILED for $($job.Relative) (exit $($result.Exit))")
                Write-Host "  FAILED: $($job.Relative)" -ForegroundColor Red
                Clear-RecordedGreen $job.Name
                Add-Timing $job.Relative 'FAILED' $result.Seconds $figures.ParsedMs $figures.EmittedMs
            }
            else {
                # Written on EVERY lane, consulted on the narrow ones only — so the full lane's greens
                # are what the next narrow run stands on.
                Write-RecordedGreen $job.Name $job.Address $portabilitySemantics
                Add-Timing $job.Relative 'compiled' $result.Seconds $figures.ParsedMs $figures.EmittedMs
            }
        }
    }

    if ($skipped -gt 0) {
        Write-Host "  $skipped of $($entries.Count) entries skipped by address (lane '$lane'); the full lane compiles all $($entries.Count)." -ForegroundColor Yellow
    }

    if (-not $KeepOutput) {
        Remove-Item -Recurse -Force $portabilityRoot -ErrorAction SilentlyContinue
    }
}

# ── 2. The laws ─────────────────────────────────────────────────────────────

$lawsOut = Join-Path $PSScriptRoot 'output'
$lineShape = '^(MERGE|MERGELAW|MERGEFINDING|MERGEFAIL|ADEQUACY|KIT|KITFAIL|DEFLATE|DEFLATEFAIL|TOTAL) '

if (-not $SkipLaws) {
    Write-Stage 'laws — TreeMerge.merge3Way + FoldConfluence.laneFoldLaws + Deflate.inflate, .NET vs Node'

    # The laws stage is one addressed subject rather than two: its record stands for the whole
    # green — both legs run, byte-identical, zero violations — so honouring it skips exactly the
    # work that produced it. Its semantics string carries the `node` version, because one side of
    # that comparison is produced by a runtime a compile-only subject never touches.
    $lawsSemantics = Get-LawsSemantics
    $lawsAddress = Get-CompileAddress $lawsProject $lawsSemantics
    $lawsRecorded = if ($laneMaySkip) { Test-RecordedGreen 'FableLaws' $lawsAddress $lawsSemantics } else { $null }
    $lawsFailuresBefore = $failures.Count
    $nodePresent = [bool] (Get-Command node -CommandType Application -ErrorAction SilentlyContinue)

    $lawsLabel = ConvertTo-RepoRelative $lawsProject

    if ($lawsRecorded) {
        Add-Timing $lawsLabel 'skipped' 0
        Write-Host "  SKIPPED BY ADDRESS $(ConvertTo-RepoRelative $lawsProject)" -ForegroundColor Yellow
        Write-Host "    address $lawsAddress" -ForegroundColor DarkGray
        Write-Host "    recorded green in lane '$($lawsRecorded.lane)' at $($lawsRecorded.recordedUtc)" -ForegroundColor DarkGray
        Write-Host "    (the full lane runs both legs and byte-compares them regardless)" -ForegroundColor DarkGray
    }
    elseif (-not $nodePresent) {
        Add-Timing $lawsLabel 'no node' 0
        # A NAMED skip, never a silent one — the posture `test-suites.json`'s corpus gate takes.
        # The portability stage above needed no Node and has already run, so the compile half of
        # this gate is intact on a machine that has never installed one.
        Write-Host 'SKIPPED — no `node` on PATH; the law harness needs a JS runtime.' -ForegroundColor Yellow
        Write-Host '         (The portability compiles above ran; only the behavioural half is skipped.)' -ForegroundColor Yellow
    }
    else {
        Remove-Item -Recurse -Force $lawsOut -ErrorAction SilentlyContinue

        # The three legs are timed separately because they answer different cost questions: the
        # .NET run is a build plus an execution, the Fable compile is one more `--noCache` parse of
        # the same shape the portability stage measures, and the Node run is the only figure in this
        # stage that is a JS runtime's. A single laws number would hide which of them grew.
        #
        # This compile is the stage's largest UN-OVERLAPPED item — the portability compiles run
        # concurrently and this one does not — and its `parsed` figure is the project crack almost
        # in full. Phase 1622 measured that and declined to cache it; the header says why.

        # The .NET leg. Filtered to the harness's own line shapes so build chatter can never enter
        # the comparison.
        $dotnetClock = [Diagnostics.Stopwatch]::StartNew()
        $dotnetOut = @(dotnet run --project (Join-Path $PSScriptRoot 'FableLaws.fsproj') -c Release |
            Where-Object { $_ -match $lineShape })
        $dotnetExit = $LASTEXITCODE
        $dotnetClock.Stop()
        Add-Timing "$lawsLabel (.NET run)" 'ran' $dotnetClock.Elapsed.TotalSeconds

        # Captured rather than streamed, so Fable's own `parsed in Nms` can be read out of it —
        # the same figure the portability compiles report. Assignment is not a pipe, so
        # `$LASTEXITCODE` still carries Fable's status; the output is echoed immediately below so
        # nothing a live stream would have shown is lost.
        $lawsFableClock = [Diagnostics.Stopwatch]::StartNew()
        $lawsFableOutput = & dotnet fable (Join-Path $PSScriptRoot 'FableLaws.fsproj') -o $lawsOut --noCache 2>&1
        $lawsFableExit = $LASTEXITCODE
        $lawsFableClock.Stop()

        $lawsFableOutput = @($lawsFableOutput | ForEach-Object { [string] $_ })
        foreach ($line in $lawsFableOutput) { Write-Host "  $line" }

        $lawsFigures = Get-FableTiming $lawsFableOutput
        Add-Timing "$lawsLabel (Fable compile)" $(if ($lawsFableExit -eq 0) { 'compiled' } else { 'FAILED' }) `
            $lawsFableClock.Elapsed.TotalSeconds $lawsFigures.ParsedMs $lawsFigures.EmittedMs

        if ($lawsFableExit -ne 0) {
            $failures.Add("Fable compile of the law harness FAILED (exit $lawsFableExit)")
        }
        else {
            $nodeClock = [Diagnostics.Stopwatch]::StartNew()
            $fableOut = @(node (Join-Path $lawsOut 'Program.js') | Where-Object { $_ -match $lineShape })
            $fableExit = $LASTEXITCODE
            $nodeClock.Stop()
            Add-Timing "$lawsLabel (Node run)" 'ran' $nodeClock.Elapsed.TotalSeconds

            if ($dotnetOut.Count -eq 0) {
                $failures.Add('the law harness produced no output on .NET — it did not run to completion')
            }
            elseif ($fableOut.Count -ne $dotnetOut.Count) {
                # A count mismatch means one side did not run the laws at all. Reporting that as
                # "0 divergences" would be a vacuous green, so it is a failure in its own right.
                $failures.Add("the law harness emitted $($dotnetOut.Count) lines on .NET and $($fableOut.Count) under Node")
            }
            else {
                $diverged = @(0..($dotnetOut.Count - 1) | Where-Object { $dotnetOut[$_] -cne $fableOut[$_] })

                if ($diverged.Count -gt 0) {
                    $failures.Add("$($diverged.Count) of $($dotnetOut.Count) law lines diverge between the pipelines")
                    Write-Host '  (.NET is the canonical side)' -ForegroundColor Red
                    foreach ($i in $diverged | Select-Object -First 10) {
                        Write-Host "    .NET  $($dotnetOut[$i])" -ForegroundColor Red
                        Write-Host "    Fable $($fableOut[$i])" -ForegroundColor Red
                    }
                }
            }

            # Both the exit codes AND the summary line are asserted. Fable drops `main`'s return
            # value, so the Node exit status is set by hand in `Program.fs`; reading only that
            # would make the whole stage depend on one line of interop staying correct.
            foreach ($line in $dotnetOut) { Write-Host "  $line" }

            $total = $dotnetOut | Where-Object { $_ -match '^TOTAL ' } | Select-Object -Last 1

            if (-not $total) {
                $failures.Add('no TOTAL line — the law harness did not run to completion')
            }
            elseif ($total -notmatch 'violations=(\d+)') {
                $failures.Add("unreadable summary line: $total")
            }
            elseif ([int]$Matches[1] -ne 0) {
                $failures.Add("$([int]$Matches[1]) law violation(s) — $total")
            }

            if ($dotnetExit -ne 0) { $failures.Add("the .NET law run exited $dotnetExit") }
            if ($fableExit -ne 0) { $failures.Add("the Node law run exited $fableExit") }
        }

        if (-not $KeepOutput) {
            Remove-Item -Recurse -Force $lawsOut -ErrorAction SilentlyContinue
        }
    }

    # Only a run that actually PERFORMED the comparison may record it. A skipped-by-address run has
    # nothing new to say, and a Node-absent run ran half the stage — recording either would be a
    # green standing on work nobody did.
    if ((-not $lawsRecorded) -and $nodePresent) {
        if ($failures.Count -eq $lawsFailuresBefore) {
            Write-RecordedGreen 'FableLaws' $lawsAddress $lawsSemantics
        }
        else {
            Clear-RecordedGreen 'FableLaws'
        }
    }
}

# ── 3. Timings and the declared budget (Phase 1620) ─────────────────────────
#
# What this reports and what it does NOT. It reports where the stage's wall-clock went, split per
# compile and split again into Fable's own cracking and emitting figures, and whether the total sat
# inside the number declared at the top of this file. It does not compare any of that to a previous
# run: nothing here stores a per-compile baseline, so "the compile that grew" is not a question this
# can answer, and it says so rather than naming the largest contributor as though it were.

if ($timings.Count -gt 0) {
    Write-Stage 'timings'

    $performed = @($timings | Where-Object { $_.State -notin @('skipped', 'no node') })
    $stageSeconds = $stageClock.Elapsed.TotalSeconds
    $budget = Get-BudgetSeconds
    $budgetSource = if ($env:FUARAN_FABLE_BUDGET_SECONDS -and ($budget -ne [double] $FableStageBudgetSeconds)) {
        'FUARAN_FABLE_BUDGET_SECONDS'
    }
    else { 'declared' }

    foreach ($row in ($timings | Sort-Object -Property @{ Expression = 'Seconds'; Descending = $true })) {
        $parsed = if ($null -ne $row.ParsedMs) { '{0,7}ms' -f $row.ParsedMs } else { '{0,9}' -f '-' }
        $emitted = if ($null -ne $row.EmittedMs) { '{0,7}ms' -f $row.EmittedMs } else { '{0,9}' -f '-' }
        $colour = switch ($row.State) {
            'skipped' { 'Yellow' }
            'no node' { 'Yellow' }
            'compiled' { 'Gray' }
            'ran' { 'Gray' }
            default { 'Red' }
        }
        Write-Host ("  {0,7:F1}s  parsed {1}  emitted {2}  {3,-9} {4}" -f `
                $row.Seconds, $parsed, $emitted, $row.State, $row.Label) -ForegroundColor $colour
    }

    $compileSeconds = [double] (($timings | Measure-Object -Property Seconds -Sum).Sum)
    Write-Host ""
    Write-Host ("  {0} compile(s) performed, {1} skipped; {2:F1}s of compile in {3:F1}s of wall-clock" -f `
            $performed.Count, ($timings.Count - $performed.Count), $compileSeconds, $stageSeconds)

    # ALWAYS assessed, and the coverage line above is what keeps that honest: a narrow lane that
    # skipped most of the stage sits far inside the budget and says so beside the count that
    # explains why. Suppressing the verdict on a partial run would make the one instrument that can
    # go off unreachable from any run a test can drive — and a budget nothing can breach is not a
    # budget.
    if ($stageSeconds -gt $budget) {
        Write-Host ""
        Write-Host ("  BUDGET EXCEEDED — {0:F1}s against a {1} budget of {2:0.###}s." -f $stageSeconds, $budgetSource, $budget) -ForegroundColor Yellow
        Write-Host "  This is a WARNING and not a failure: a gate that goes red because a machine is slow" -ForegroundColor Yellow
        Write-Host "  is a gate people learn to step over. What it is for is a stage that GREW." -ForegroundColor Yellow
        Write-Host "  The largest contributors to this run (NOT a comparison — nothing here holds a" -ForegroundColor Yellow
        Write-Host "  previous run to compare against; a stage that gained a compile shows up as a new" -ForegroundColor Yellow
        Write-Host "  row rather than as a grown one):" -ForegroundColor Yellow
        foreach ($row in ($performed | Sort-Object -Property Seconds -Descending | Select-Object -First 3)) {
            Write-Host ("    {0,7:F1}s  {1}" -f $row.Seconds, $row.Label) -ForegroundColor Yellow
        }
    }
    else {
        Write-Host ("  within the {0} budget of {1:0.###}s." -f $budgetSource, $budget) -ForegroundColor DarkGray
    }
}

# ── 4. Verdict ──────────────────────────────────────────────────────────────

Write-Host ""

if ($failures.Count -gt 0) {
    Write-Host "==== Fable stage: FAILED ($($failures.Count))" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "     $f" -ForegroundColor Red }
    exit 1
}

Write-Host '==== Fable stage: green' -ForegroundColor Green
exit 0
