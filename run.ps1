#Requires -Version 7.0
<#
.SYNOPSIS
  Fuaran language-tier entry point: verify the engine (fantomas + build +
  tests + optional validator) or launch the browser demo. Stage-0 shape
  per the workspace `CLAUDE.md` "every new sibling app ships a run.ps1"
  mandate.

.DESCRIPTION
  The language tier is library-only; the default mode is verify, not
  launch. Three modes:

    pwsh ./run.ps1                  # default — verify: fantomas check +
                                    #           build + every Expecto
                                    #           suite
    pwsh ./run.ps1 -Validate        # verify + run the build-time
                                    #           validator across every
                                    #           src/*.fsproj
    pwsh ./run.ps1 -Demo            # delegate to
                                    #           dev-scripts/launch-demo.ps1
                                    #           (Fable watcher + Vite +
                                    #           browser at 24000)

  Switches stack: -SkipFormat / -SkipBuild / -SkipTests / -SkipFable for fast
  iteration loops inside the verify mode.

  -Lane pure|fast|full (Phase 1553) SELECTS TESTS rather than dropping a stage,
  so a change can be gated in seconds before a merge and the full gate spent
  once, on the merged tree. It is a NAMED lane and is therefore visible in
  whatever gate command a result quotes; a -Skip* switch is not. Default `full`
  is byte-identical to the pre-1553 gate.

  "Every Expecto suite" means the roster declared in `test-suites.json`, which
  `Build.fs`'s `Test` target reads too — so this script and the FAKE pipeline
  run the same suites in the same order, and cannot drift apart.

  The FABLE STAGE (Phase 1488) is `tests/fable-laws/fable-check.ps1`, which the
  FAKE `FableCheck` target calls too — again declared once, read by both. It
  Fable-compiles the three client-tier projects under their own settings and
  runs the Fable law harness under Node against the same harness on .NET, so a
  client-tier break fails HERE rather than in a consumer's browser.

.EXAMPLE
  pwsh ./run.ps1

  Full verify: tool restore → fantomas --check → dotnet build → every
  Expecto suite.

.EXAMPLE
  pwsh ./run.ps1 -SkipFormat -SkipBuild

  Re-test after a code edit (skip format + build for ~10s loop).

.EXAMPLE
  pwsh ./run.ps1 -Lane fast

  The pre-merge lane: format + build + every suite but the slow ones, AND the
  Fable stage with Phase 1619's content-addressed skip armed. Measured 2026-09-08
  on this repo: 86.4s without the stage against 90.6s with it on an unchanged
  tree, and 80.8s against 116.2s after editing one file in Fuaran.UI.Renderer.
  `-SkipFable` is still the switch for a loop that wants no Fable stage at all;
  it is no longer what the pre-merge lane is declared as.

.EXAMPLE
  pwsh ./run.ps1 -Demo

  Launch the Vite + Fable demo at http://localhost:24000.
#>
[CmdletBinding()]
param(
    [switch] $SkipFormat,
    [switch] $SkipBuild,
    [switch] $SkipTests,
    [switch] $SkipFable,
    [switch] $Validate,
    [switch] $SkipPublishCheck,
    # Phase 1647 - the cross-host validator-coverage projection. A switch rather than a lane:
    # it is seconds, node-only, and reads committed text, so no lane wants it dropped.
    [switch] $SkipValidatorCoverage,
    # Phase 1646 - the FUARAN defect-code collision check. A switch rather than a lane, on
    # -SkipValidatorCoverage's reasoning: it reads committed source text with regexes, costs
    # under a second, and needs no build, so no lane wants it dropped.
    [switch] $SkipCodesCheck,
    # Phase 1648 - the shipped BROWSER SCRIPTS. Three JavaScript files ship in this repo's
    # packages and run in a reader's browser, and none of them had behavioural coverage of any
    # kind: Phase 1532 could add only a source-SHAPE guard, which the defect that motivated this
    # stage (a server-driven ReadFileBody that could not reach a file input, and said nothing
    # about it) passes cleanly. A switch rather than a lane, on -SkipCodesCheck's reasoning:
    # node-only, no build, under a second.
    [switch] $SkipContentJs,
    [switch] $Demo,

    # Phase 1553 - the gate LANE, on THIS one file. Tooling that records which gate produced a
    # result resolves this script by its filename and pins its hash, so a SECOND gate script would
    # read as permanent drift; one parameter on the existing file moves that hash exactly once.
    #
    #   pure - the roster's `"lane": "pure"` suites only: no filesystem, no corpus. Seconds.
    #   fast - every suite but the roster's `"lane": "slow"` ones, with `Lanes.slow`-marked
    #          subtrees dropped inside the suites that do run. The pre-merge lane.
    #   full - the whole suite (default). The RELEASE lane, and the only lane a release may cite.
    #
    # The lane rides the recorded gate COMMAND STRING, so a result says which lane produced it
    # rather than leaving a reader to assume the full one - which is what makes a lane honest where
    # a dropped stage is not. `-SkipFable` composes with it and is NOT implied by any lane: the
    # Fable leg is a stage, not a lane. A pre-merge invocation used to name both, because on
    # measurement the Fable stage was ~65% of this gate's wall-clock and a "fast" lane that left it
    # in was not fast.
    #
    # Phase 1619 narrowed that last point rather than retiring it. The lane now also reaches the
    # FABLE STAGE, where a narrow lane may skip a compile whose CONTENT ADDRESS matches its last
    # recorded green - a named skip, address printed, so an unchanged tree costs seconds there
    # instead of minutes. `full` writes the address and never consults one, so the skip is
    # structurally unreachable on the lane a release cites, and `-SkipFable` remains the right
    # switch for a loop that wants no Fable stage at all rather than a cheap one.
    #
    # Phase 1623 measured the WHOLE LANE rather than the stage, and the side's declared fast lane
    # dropped `-SkipFable` on the result. On this repo, 2026-09-08: `-Lane fast -SkipFable` 86.4s
    # against `-Lane fast` 90.6s on an unchanged tree - the Fable stage itself 3.0s, 13 subjects
    # skipped by name - and 80.8s against 116.2s after editing one file in Fuaran.UI.Renderer, the
    # stage 35.2s for one recompile and twelve skips. So the pre-merge lane buys back the coverage a
    # worker most needs before a merge (a client-tier break, a law divergence) for ~4s on an
    # unchanged tree, which is inside this gate's own run-to-run noise. The FIRST run in a fresh
    # clone or worktree still pays the stage in full - 145.1s against 291.2s with no record to
    # match - once.
    #
    # WHAT A LANE DECIDES IS WHETHER THE SKIP IS ARMED, NEVER WHICH HALF RUNS. Both halves of the
    # stage - the client-tier portability compiles and the law harness - are reached by every lane,
    # and 1619's address makes each of them seconds on an unchanged tree, so there is nothing left
    # for a half-dropping lane to buy. Selecting a half is what the stage script's own
    # `-SkipPortability` / `-SkipLaws` are for: they stay switches, named in whatever command a
    # result quotes, for exactly the reason `-SkipFable` is not a lane.
    [ValidateSet('pure', 'fast', 'full')]
    [string] $Lane = 'full'
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

function Write-Step {
    param([string] $message)
    Write-Host ""
    Write-Host "── $message ──────────────────────────────────────────────" -ForegroundColor Cyan
}

# ─── -Demo: delegate to the launcher ─────────────────────────────────
if ($Demo) {
    $inner = Join-Path $PSScriptRoot "dev-scripts/launch-demo.ps1"
    if (-not (Test-Path $inner)) {
        Write-Error "Demo launcher not found at $inner"
        exit 1
    }
    Write-Host "Fuaran: launching demo -> dev-scripts/launch-demo.ps1"
    Write-Host ""
    & $inner
    exit $LASTEXITCODE
}

# ─── Default: verify ─────────────────────────────────────────────────
# One configuration for the build and every suite, read from the same env var
# `Build.fs` reads — so the two entry points can never name different output
# trees, and a session iterating in bin/Debug/ can point the whole gate there
# with `$env:FUARAN_BUILD_CONFIGURATION = 'Debug'` instead of building one tree
# and testing another. Default Release, unchanged.
$config = if ($env:FUARAN_BUILD_CONFIGURATION) { $env:FUARAN_BUILD_CONFIGURATION } else { "Release" }

$sln = "Fuaran.sln"

# ─── The test-suite roster ───────────────────────────────────────────
# DECLARED ONCE, in `test-suites.json` at the repo root, and read by BOTH entry
# points: this script and `Build.fs`'s `Test` target (what CI runs).
#
# It used to be declared twice — thirty entries in `Build.fs`, eight here — and
# the two lists drifted, as two hand-maintained copies of one roster will. The
# DAG op-stream, FastPath, server-driven, IDL, analyzer and veneer-conformance
# suites all ran in the FAKE target and in CI while this script, which is the
# gate other tooling invokes, never saw them: a break in any of them was green
# locally and red only on a push. One file read by both cannot diverge.
#
# `requiresCorpus` (default false) marks a suite that loads the wire-format
# conformance corpus from a sibling of this repo. In a single-repo checkout that
# corpus is absent and the suite crashes at startup, so it is skipped by name —
# never silently, and never treated as a pass.
$manifestPath = Join-Path $PSScriptRoot "test-suites.json"
if (-not (Test-Path $manifestPath)) {
    Write-Error "test-suites.json not found at $manifestPath — the test roster cannot be read."
    exit 1
}
$testSuites = @((Get-Content -Raw -Path $manifestPath | ConvertFrom-Json).suites)
if ($testSuites.Count -eq 0) {
    # An empty roster would let this gate pass having run nothing, which is the
    # one failure the shared file exists to make impossible.
    Write-Error "test-suites.json ($manifestPath) lists no suites."
    exit 1
}
# Phase 1647 - FUARAN_WIRE_FIXTURES overrides the sibling walk, on the one contract every corpus
# reader in this repo honours. A git worktree is not beside the corpus, so without it every
# corpus-requiring suite was silently declared absent and the gate went green having certified
# against nothing.
$corpusRoot =
    if ($env:FUARAN_WIRE_FIXTURES) { $env:FUARAN_WIRE_FIXTURES.Trim() }
    else { Join-Path $PSScriptRoot "../wire-format-fixtures" }
$corpusPresent = Test-Path (Join-Path $corpusRoot "manifest.json")

# Phase 1553 - the SUITE-level half of the lane. `lane` is declared per suite in the roster above
# ("pure" / "slow"; absent = the ordinary tier), so this filter and Build.fs's read the same
# declaration. An unrecognised value is REFUSED rather than ignored: a typo that silently demoted a
# suite out of `fast` would make the pre-merge lane quietly weaker over time, which is the one
# failure a lane must not have.
$recognisedLanes = @("pure", "slow")
foreach ($suite in $testSuites) {
    if ($suite.lane -and ($recognisedLanes -notcontains $suite.lane)) {
        Write-Error "test-suites.json: $($suite.project) declares lane '$($suite.lane)'; expected one of: $($recognisedLanes -join ', ')."
        exit 1
    }
}

function Test-SuiteInLane {
    param($suite, [string] $lane)
    switch ($lane) {
        "pure" { return $suite.lane -eq "pure" }
        "fast" { return $suite.lane -ne "slow" }
        default { return $true }
    }
}

if ($Lane -ne "full") {
    $admitted = @($testSuites | Where-Object { Test-SuiteInLane $_ $Lane })
    if ($admitted.Count -eq 0) {
        # A lane that runs nothing and exits 0 is the one answer a gate must never give.
        Write-Error "-Lane $Lane admits no suite in test-suites.json - nothing would run."
        exit 1
    }
    Write-Host ""
    Write-Host "Lane '$Lane': $($admitted.Count) of $($testSuites.Count) suites (releases cite the full lane only)." -ForegroundColor Yellow
    $testSuites = $admitted
}

Write-Step "dotnet tool restore"
dotnet tool restore
if ($LASTEXITCODE -ne 0) { Write-Error "dotnet tool restore failed (exit $LASTEXITCODE)"; exit $LASTEXITCODE }

if (-not $SkipFormat) {
    Write-Step "fantomas --check"
    dotnet fantomas --check .
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Fantomas check failed — run 'dotnet fantomas .' to format in place."
        exit $LASTEXITCODE
    }
}

if (-not $SkipBuild) {
    Write-Step "dotnet build $sln -c $config"
    dotnet build $sln -c $config
    if ($LASTEXITCODE -ne 0) { Write-Error "Build failed (exit $LASTEXITCODE)"; exit $LASTEXITCODE }
}

if (-not $SkipTests) {
    # The TEST-level half: the suites that link tests/lanes/Lanes.fs read this at their own Expecto
    # entry point and drop their `Lanes.slow`-marked subtrees. A suite that links nothing ignores it,
    # which is why an unmarked suite needs no edit to join the scheme.
    $env:FUARAN_TEST_LANE = $Lane
    try {
        foreach ($suite in $testSuites) {
            $project = $suite.project

            if ($suite.requiresCorpus -and -not $corpusPresent) {
                Write-Step "SKIPPING $project"
                Write-Host "wire-format-fixtures corpus absent (single-repo checkout; conformance runs where the workspace corpus is present)." -ForegroundColor Yellow
                continue
            }

            Write-Step "Expecto: $project"
            # Expecto console runner — `dotnet run --project`, NOT `dotnet test`
            # (`dotnet test` silently no-ops on Expecto consoles).
            # `--no-build` MUST precede `--project` or `dotnet run` forwards
            # it to Expecto.
            if ($SkipBuild) {
                dotnet run --project $project -c $config
            }
            else {
                dotnet run --no-build --project $project -c $config
            }
            if ($LASTEXITCODE -ne 0) { Write-Error "$project failed (exit $LASTEXITCODE)"; exit $LASTEXITCODE }
        }
    }
    finally {
        Remove-Item Env:FUARAN_TEST_LANE -ErrorAction SilentlyContinue
    }
}

# ─── The Fable stage (Phase 1488) ────────────────────────────────────
# One script, called by this entry point AND by `Build.fs`'s `FableCheck`
# target — the same "declared once, read by both" posture `test-suites.json`
# takes for the test roster, so the two gates cannot run different Fable stages.
#
# It comes AFTER the .NET suites for the reason the FAKE ordering states: both
# stages are about the code, and when both run the .NET suites are the cheaper
# diagnosis, so they answer first.
if (-not $SkipFable) {
    Write-Step "Fable stage (client-tier portability + the Fable law harness)"

    $fableCheck = Join-Path $PSScriptRoot "tests/fable-laws/fable-check.ps1"
    if (-not (Test-Path $fableCheck)) {
        Write-Error "Fable stage script not found at $fableCheck"
        exit 1
    }
    # The lane reaches the stage through the SAME variable the test stage sets (Phase 1619), so the
    # Fable stage cannot run under a lane the rest of this gate did not. In a narrow lane it may
    # skip a compile whose content address matches its last recorded green, naming every skip; in
    # `full` it writes the address and never reads one, so the skip is structurally unreachable on
    # the only lane a release may cite. See `tests/fable-laws/fable-check.ps1`.
    $env:FUARAN_TEST_LANE = $Lane
    try {
        & $fableCheck
    }
    finally {
        Remove-Item Env:FUARAN_TEST_LANE -ErrorAction SilentlyContinue
    }
    if ($LASTEXITCODE -ne 0) { Write-Error "Fable stage failed (exit $LASTEXITCODE)"; exit $LASTEXITCODE }
}

if ($Validate) {
    Write-Step "Fuaran.UI.Validator across src/*.fsproj"
    # Same loop as Build.fs `Validate` target. Walks every .fsproj under
    # src/ except the validator's own + its tests.
    $srcDir = Join-Path $PSScriptRoot "src"
    $validatorProject = "src/Fuaran.UI.Validator/Fuaran.UI.Validator.fsproj"
    $candidates = Get-ChildItem -Path $srcDir -Recurse -Filter *.fsproj `
    | Where-Object { $_.Name -ne "Fuaran.UI.Validator.fsproj" -and $_.Name -ne "Fuaran.UI.Validator.Tests.fsproj" }
    foreach ($project in $candidates) {
        Write-Host "Fuaran.UI.Validator: $($project.FullName)" -ForegroundColor DarkGray
        dotnet run --no-build --project $validatorProject -c $config -- $project.FullName
        if ($LASTEXITCODE -ne 0) { Write-Error "Validator failed on $($project.FullName) (exit $LASTEXITCODE)"; exit $LASTEXITCODE }
    }
}

# ─── Validator coverage: does this host's declaration match the vocabulary? ──
# `validator-coverage.json` declares which of the canonical pre-emit vocabulary's
# codes this host implements. It is GENERATED (Phase 1647) from the same
# reflection over the defect DU that emits the corpus vocabulary, and the corpus
# carries the script that compares the two. Nothing invoked that script from
# either entry point, so the declaration's own claim to be "checked by
# construction" was checked by nobody: it stopped at FUARAN114 while the
# vocabulary ran to FUARAN148, and a phase that merely ADDED a defect case left
# the cross-host gate red on `main` for someone else to attribute.
#
# THIS repo's root is passed explicitly rather than relying on the script's
# sibling discovery, so a worktree gates its own declaration and not the primary
# tree's. Node-only; absent corpus is NOT CHECKED by name, never a quiet pass.
if (-not $SkipValidatorCoverage) {
    Write-Step "Validator coverage (validator-coverage.json vs the corpus vocabulary)"
    $coverageScript = Join-Path $corpusRoot "validator/check-coverage.mjs"
    if (-not (Test-Path $coverageScript)) {
        Write-Host "validator coverage  NOT CHECKED - the wire-format-fixtures corpus is absent from this checkout." -ForegroundColor Yellow
    }
    else {
        node $coverageScript $PSScriptRoot
        if ($LASTEXITCODE -ne 0) {
            Write-Error "validator-coverage.json disagrees with the corpus vocabulary (exit $LASTEXITCODE). Regenerate it with: dotnet run --project src/Fuaran.UI.JsonDecode.Tests -- --emit-vocabulary"
            exit $LASTEXITCODE
        }
    }
}

# ─── FUARAN defect codes: does any one code name two different rules? ───────
# The FUARAN code space is shared by three registries - the tree-time validator's
# `describe`, the build-time source-AST walker's findings, and the Roslyn diagnostic
# descriptors - and each used to mint by reading the highest number the minting session
# happened to have open. Two phases minted FUARAN114 in one evening; the analyzer and the
# walker had each been sitting on FUARAN060/061 for a DIFFERENT defect since Phase 315.
#
# `scripts/fuaran-codes.ps1` is both halves: `-Next` allocates (reading the tree, the corpus
# vocabulary AND every sibling worktree, so an unpushed concurrent mint is visible), `-Check`
# is this stage. Declared once, called by `Build.fs`'s `CodesCheck` target too, so the two
# entry points cannot check different registries.
if (-not $SkipCodesCheck) {
    Write-Step "FUARAN defect codes (no code names two rules)"
    & (Join-Path $PSScriptRoot "scripts/fuaran-codes.ps1") -Check
    if ($LASTEXITCODE -ne 0) {
        Write-Error "FUARAN defect-code check failed (exit $LASTEXITCODE)."
        exit $LASTEXITCODE
    }
}

# ─── The shipped browser scripts (Phase 1648) ────────────────────────────────
# `tests/content-js/run.mjs` runs the three JavaScript files this repo ships to a reader's
# browser against a DOM stub and asserts what they DO. Its `--self-test` half perturbs each
# subject in memory and requires the harness to go RED, because a hand-rolled DOM stub is
# exactly the kind of test double that can pass by understanding nothing — so the falsifier
# is named and executed rather than assumed. Both halves run: the harness's own correctness
# is not a separate concern from the harness's result.
#
# `sync-renderer-web.ps1 -SelfTest` is the other half of the same gap. Phase 1532 pinned the
# fingerprint sidecar's F# READER with an F# round trip, which by construction can only prove
# one of the format's two writers; this exercises the PowerShell one against the same rules.
# Offline, no sibling, no file written.
if (-not $SkipContentJs) {
    Write-Step "Browser scripts (content-js harness + the sidecar writer's escapes)"

    $contentJs = Join-Path $PSScriptRoot "tests/content-js/run.mjs"
    if (-not (Test-Path $contentJs)) {
        Write-Error "content-js harness not found at $contentJs"
        exit 1
    }

    node $contentJs
    if ($LASTEXITCODE -ne 0) { Write-Error "content-js harness failed (exit $LASTEXITCODE)."; exit $LASTEXITCODE }

    node $contentJs --self-test
    if ($LASTEXITCODE -ne 0) {
        Write-Error "content-js harness go-red self-test failed (exit $LASTEXITCODE) - a case stayed green against a subject with its behaviour removed, so that case proves nothing."
        exit $LASTEXITCODE
    }

    & (Join-Path $PSScriptRoot "scripts/sync-renderer-web.ps1") -SelfTest
    if ($LASTEXITCODE -ne 0) {
        Write-Error "sync-renderer-web writer self-test failed (exit $LASTEXITCODE)."
        exit $LASTEXITCODE
    }
}

# ─── The standing draft: what sits between <Version> and the newest tag ──────
# The packages restore from nuget.org for every consumer outside this
# workspace — including a downstream consumer's free-tier CI that builds
# against the RELEASED packages and has no local feed. Publication is triggered by a `v*`
# tag (see .github/workflows/publish-packages.yml), so a <Version> ahead of the
# newest tag names a version those consumers cannot restore.
#
# That is not hypothetical: <Version> ran 0.18.0 -> 0.26.0 between 2026-08-13
# and 2026-08-16 with no tag pushed after v0.18.0, and a downstream consumer's
# every-PR conformance gate was red on NU1102 for five days as a result — 60
# consecutive failing runs, whose cause was a wall of "Unable to find package"
# lines rather than anything naming the omission.
#
# This step used to answer that by URGING the tag, printing the two git commands
# whenever <Version> exceeded it. That was wrong in a way the NU1102 story hides:
# a version standing ahead of the newest tag is the NORMAL state between
# releases — the draft slot every change rides until someone deliberately cuts a
# release — so the nag fired constantly, and what it rewarded was tagging
# whichever number happened to be standing. In the week to 2026-09-04 this repo
# minted 25 versions and tagged one; the other 24 were dead on arrival.
#
# So it REPORTS instead: the standing draft, the newest tag, and what has ridden
# the draft since that tag. Release timing is a separate deliberate act and is
# not this script's to prompt. The NU1102 explanation is KEPT and narrowed to the
# case where it is genuinely a defect — a consumer that already pins the untagged
# version — because that consumer's CI is red now, whatever the draft rule says.
if (-not $SkipPublishCheck) {
    Write-Step "Standing draft (<Version> vs the newest v* tag)"

    $propsPath = Join-Path $PSScriptRoot "Directory.Build.props"
    $versionMatch = Select-String -Path $propsPath -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1

    if (-not $versionMatch) {
        Write-Host "Could not read <Version> from Directory.Build.props - skipping." -ForegroundColor DarkGray
    }
    else {
        $version = $versionMatch.Matches[0].Groups[1].Value.Trim()

        # Sort tags by VERSION, not by creation date: a re-pushed or
        # back-dated tag would otherwise read as the newest.
        $tagged = @(git tag --list "v*" 2>$null | ForEach-Object { $_.TrimStart("v") } |
            Where-Object { $_ -as [version] } | Sort-Object { [version] $_ })
        $newestTag = if ($tagged.Count -gt 0) { $tagged[-1] } else { $null }

        if ($null -eq $newestTag) {
            Write-Host "No v* tag in this repo yet - nothing published." -ForegroundColor Yellow
        }
        elseif (($version -as [version]) -and ([version] $version) -gt ([version] $newestTag)) {
            # The report, not a prompt. A draft ahead of the newest tag is the
            # ordinary state between releases; what is worth SEEING is how much
            # has ridden it, because that is what a release would carry.
            $riders = @(git log --oneline "v$newestTag..HEAD" -- Directory.Build.props 2>$null)

            Write-Host ""
            Write-Host "  Draft $version stands; the newest published tag is v$newestTag." -ForegroundColor Cyan
            if ($riders.Count -gt 0) {
                Write-Host "  Version-file commits riding this draft since v${newestTag}:" -ForegroundColor DarkGray
                foreach ($r in $riders) { Write-Host "      $r" -ForegroundColor DarkGray }
            }
            else {
                Write-Host "  No version-file commit since v$newestTag - the draft was cut and nothing has ridden it yet." -ForegroundColor DarkGray
            }
            Write-Host ""
            Write-Host "  This is the normal state between releases and is NOT a defect. Cutting the" -ForegroundColor DarkGray
            Write-Host "  release is a separate deliberate act; this step does not ask for one." -ForegroundColor DarkGray
            Write-Host ""
            Write-Host "  It IS a defect for one consumer only: anything restoring from nuget.org that" -ForegroundColor Yellow
            Write-Host "  already pins $version cannot see it, and fails with NU1102 naming every" -ForegroundColor Yellow
            Write-Host "  package rather than the missing tag. If a public-path consumer pins this" -ForegroundColor Yellow
            Write-Host "  draft, either move its pin back to $newestTag or release the draft." -ForegroundColor Yellow
        }
        else {
            Write-Host "v$newestTag published; <Version> is $version - the public channel is current." -ForegroundColor Green
        }
    }
}

Write-Host ""
Write-Host "✓ Verify passed." -ForegroundColor Green
exit 0
