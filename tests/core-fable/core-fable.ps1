#Requires -Version 7.0
<#
.SYNOPSIS
  The Fuaran.Core Fable gate (Phase 217): compile every public Fuaran.Core package under Fable,
  and run the Core cross-pipeline VALUE table on .NET and under node, byte-compared.

.DESCRIPTION
  WHY IT IS HERE. Fuaran.Core publishes "Fable-clean on encode AND decode" for its public packages,
  and until Phase 217 it gated that claim itself, with an in-repo Fable smoke and a node parity leg.
  The Fable compiler belongs where a Fable toolchain already lives — in applications and in this
  repository, the language's .NET implementation — so both legs moved here, and Fuaran.Core keeps
  the half that needs no Fable: the declaration of which packages are on the surface, and the .NET
  pin of every parity vector.

  TWO LEGS, AND THEY PROVE DIFFERENT THINGS.

    compile   `CoreFable.fsproj` references every Core package on the surface and touches each
              one's public encode/decode symbols (`Program.fs`). A clean `dotnet fable` compile is
              the portability claim. After the compile, every referenced package must appear in
              the emitted `fable_modules/` — checked, so a reference Fable silently ignored cannot
              pass for one it compiled.

    parity    `Fuaran.Core.ParityVectors` (shipped in the Fuaran.Core.Conformance package since
              0.31.0) is printed by the SAME program on .NET and, transpiled, under node, and the
              two outputs are byte-compared. A compile cannot disagree about a number: two recorded
              Core defects shipped behind a green compile — `Hash.fnv1a` divergent between the
              pipelines until 0.6.0, and `Wire.Json.render` throwing under Fable for any float until
              Phase 118 — and this leg is what catches that class.

  IT FAILS WITHOUT `node`; IT NEVER SKIPS the parity leg it can run. A check that reports success on
  a machine where it did not run teaches everyone to trust a green that means nothing.

  THE PIN AND THE CUT (read this before touching the mode logic). This repository consumes
  Fuaran.Core as packages at the version `Directory.Packages.props` pins, and a pin must stay
  publicly restorable — so this gate normally sees Core as it WAS RELEASED, not as it is now. Two
  consequences, each handled rather than assumed:

    * The cut-time run. Every Fuaran.Core version cut cites a green run of THIS script against the
      candidate packages before the version is released:

          pwsh ./tests/core-fable/core-fable.ps1 -CoreVersion <candidate> -CoreFeed <folder of .nupkg>

      That run restores every package Fuaran.Core produces (all but the compute layer — see TWO
      PRODUCERS below) from the candidate folder ONLY (an isolated
      package cache, so a same-version repack can never be served stale), derives the surface from
      the packages the candidate actually contains, and requires the parity leg to run. It is what
      keeps a divergence from being discovered only when this repository next raises its pin.

    * A pin that predates the table. `ParityVectors` first ships in 0.31.0. Pinned below that, the
      parity leg cannot run here, and the script SAYS SO in the output and names the cut-time rule
      that covers the gap. It reads whether the table is present off the RESTORE (the package's own
      file list), not off a version number — and then checks that answer against the version: a pin
      at or above 0.31.0 whose restored package lacks the table FAILS (the tripwire), so the leg
      cannot quietly stay off once the pin reaches it. The decision table is proven on every run
      (`Test-ParityDecision`), before anything is compiled.

  TWO PRODUCERS, ONE GATE. From 0.33.0 the compute layer ships from its own repository, and
  from its 0.36.0 under its own ids — `Fuaran.Compute.DataFrame`, `Fuaran.Compute.ColumnOps`,
  `Fuaran.Compute.Conformance`, `Fuaran.Compute.PipelineQuery` — while every `Fuaran.Core.*`
  package still ships from Fuaran.Core. This
  repository pins the two on separate versions (`FuaranCoreComputeVersion` in
  `Directory.Packages.props`), and this gate takes a candidate for each independently:

          pwsh ./tests/core-fable/core-fable.ps1 -ComputeVersion <candidate> -ComputeFeed <folder of .nupkg>

  restores the three compute packages from that folder only, at that version, with every
  Fuaran.Core package at this repository's pin; `-CoreVersion`/`-CoreFeed` does the same for the
  Fuaran.Core packages with the compute packages at their pin (from nuget.org — a pin is public);
  both pairs together certify two candidates against each other. Which producer owns a package is
  the `$ComputeOwned` list below, kept in step with `Directory.Packages.props` beside this script.
  Neither pair passed: every package follows `Directory.Packages.props`, exactly as before.

  A CORE-ONLY CUT SKIPS THE COMPUTE PACKAGES, and says so. The compute packages pin an older
  Fuaran.Core until this Core release is published, so compiling them against the Core candidate
  reports their producer's next raise, not a defect in the candidate; they are gated by their own
  producer's cut (`-ComputeVersion`/`-ComputeFeed`). The script sets `CoreFableSkipCompute=true`,
  which drops their references from the restore and leaves `CORE_FABLE_COMPUTE` undefined, so
  `Program.fs` compiles only its Core touches. Pinned, compute-only and both-producers runs compile
  them as before. The smoke program is written for the pinned Core line (0.34.0); the arms that let it
  compile against an older line were retired with the pin raise.

  MEMBERSHIP IS CHECKED, NOT REMEMBERED. The Core packages this gate is responsible for are derived
  — from this repository's own `Fuaran.Core.*` pins by default, and from the candidate's packages
  in a cut-time run — and each must be referenced by `CoreFable.fsproj` or listed in
  `exclusions.json` beside it with a reason and the deciding phase (those entries mirror
  Fuaran.Core's own `fable-exclusions.json`). A Core package that is neither fails by name.

  RUN IT STANDALONE from anywhere:  pwsh ./tests/core-fable/core-fable.ps1
  `tests/fable-laws/fable-check.ps1` runs it as part of the Fable stage. Exit 0 = green.
#>
[CmdletBinding()]
param(
    # A cut-time run: the candidate Fuaran.Core version, restored from -CoreFeed only.
    [string] $CoreVersion,
    # A cut-time run: a folder holding the candidate's .nupkg files.
    [string] $CoreFeed,
    # A cut-time run of the SECOND producer: the candidate compute-layer version, restored from
    # -ComputeFeed only. Independent of -CoreVersion; either pair may be passed alone.
    [string] $ComputeVersion,
    # A cut-time run of the second producer: a folder holding the compute candidate's .nupkg files.
    [string] $ComputeFeed,
    # Keep the emitted JavaScript and both captured outputs in the scratch root for inspection.
    [switch] $KeepOutput
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# Seeded because every guard below reads it: `$LASTEXITCODE` is `$null` until a native command runs,
# and `$null -ne 0` is true, so an unseeded guard can fail (or pass) on a stage that never executed.
$global:LASTEXITCODE = 0

# The first Fuaran.Core version whose Conformance package ships `ParityVectors`.
$VectorsSince = [version] '0.31.0'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$project = Join-Path $PSScriptRoot 'CoreFable.fsproj'
$coreOverride = [bool] ($CoreVersion -or $CoreFeed)
$computeOverride = [bool] ($ComputeVersion -or $ComputeFeed)
$override = $coreOverride -or $computeOverride
# A Core-only cut compiles the Core surface alone (see the header): the compute packages are gated by
# their own producer's cut.
$skipCompute = $coreOverride -and -not $computeOverride

# The packages the SECOND producer ships (its repository's derived roster, every one of which this
# gate references). From its 0.36.0 they carry their own ids and the `Fuaran.Compute` namespace; the
# C#-only half is gone. Every Fuaran.Core.* package is Fuaran.Core's. Keep in step with the compute
# ItemGroups in this directory's Directory.Packages.props and CoreFable.fsproj.
$ComputeOwned = @('Fuaran.Compute.DataFrame', 'Fuaran.Compute.ColumnOps', 'Fuaran.Compute.Conformance', 'Fuaran.Compute.PipelineQuery')

function Fail([string] $message) {
    Write-Host "==== core-fable: FAILED — $message" -ForegroundColor Red
    exit 1
}

function ConvertTo-Version([string] $text) {
    # A pre-release suffix orders BELOW its release, so `0.31.0-x` is treated as `0.31.0` only for
    # the tripwire's "at or above" question; nothing else compares versions.
    $core = ($text -split '[-+]')[0]
    $parsed = $null
    if ([version]::TryParse($core, [ref] $parsed)) { return $parsed }
    return $null
}

# ── The parity decision, and its proof ───────────────────────────────────────

function Get-ParityDecision {
    <#
      run          the restored Conformance package ships the table — run the leg.
      predates     it does not, and the pinned version is below the first that ships it — say so.
      tripwire     it does not, and the version is at or above that — FAIL: the leg must be running.
      candidate    a cut-time run whose candidate lacks the table — FAIL: the cut cannot cite it.
    #>
    # $IsOverride is a Fuaran.Core cut-time run: the table ships in a Fuaran.Core package, so a
    # compute-only candidate run reads the pinned Core like the default mode does.
    param([version] $Resolved, [bool] $HasTable, [bool] $IsOverride)
    if ($HasTable) { return 'run' }
    if ($IsOverride) { return 'candidate' }
    if ($null -eq $Resolved -or $Resolved -ge $VectorsSince) { return 'tripwire' }
    return 'predates'
}

function Test-ParityDecision {
    # The go-red proof for the one decision that could make this gate quietly vacuous. Milliseconds,
    # so it runs on every invocation rather than when someone remembers.
    $cases = @(
        @{ V = '0.30.0'; T = $false; O = $false; Want = 'predates' }
        @{ V = '0.31.0'; T = $false; O = $false; Want = 'tripwire' }
        @{ V = '0.32.1'; T = $false; O = $false; Want = 'tripwire' }
        @{ V = '0.31.0'; T = $true; O = $false; Want = 'run' }
        @{ V = '0.30.0'; T = $true; O = $false; Want = 'run' }
        @{ V = '0.31.0'; T = $false; O = $true; Want = 'candidate' }
        @{ V = 'unreadable'; T = $false; O = $false; Want = 'tripwire' }
    )
    foreach ($c in $cases) {
        $got = Get-ParityDecision (ConvertTo-Version $c.V) $c.T $c.O
        if ($got -ne $c.Want) {
            Fail "the parity decision is broken: version $($c.V), table=$($c.T), cut-time=$($c.O) decided '$got', expected '$($c.Want)'"
        }
    }
}

Test-ParityDecision

# ── Mode ────────────────────────────────────────────────────────────────────

# Outside the repository, like the Fable stage's own scratch roots (MAX_PATH), and keyed on this
# script's location so two worktrees never wipe each other's output.
$sha = [Security.Cryptography.SHA256]::Create()
$treeKey = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($PSScriptRoot.ToLowerInvariant()))) -replace '-', '').Substring(0, 12).ToLowerInvariant()
$sha.Dispose()
$scratch = Join-Path ([IO.Path]::GetTempPath()) "fuaran-core-fable-$treeKey"
Remove-Item -Recurse -Force $scratch -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
$outDir = Join-Path $scratch 'out'

# Every property below is read by MSBuild from the environment, which is what lets `dotnet restore`,
# `dotnet fable` and `dotnet build` all see the same values. Cleared at the end either way.
$touchedEnv = @('FuaranCoreVersion', 'CoreFableComputeVersion', 'CoreFableSkipCompute', 'CoreParity', 'NUGET_PACKAGES')
$savedEnv = @{}
foreach ($name in $touchedEnv) { $savedEnv[$name] = [Environment]::GetEnvironmentVariable($name) }
[Environment]::SetEnvironmentVariable('CoreParity', $null)
[Environment]::SetEnvironmentVariable('FuaranCoreVersion', $null)
[Environment]::SetEnvironmentVariable('CoreFableComputeVersion', $null)
[Environment]::SetEnvironmentVariable('CoreFableSkipCompute', $null)

$pinsFile = Join-Path $repoRoot 'Directory.Packages.props'
$pinsText = Get-Content -Raw $pinsFile
# A pin may name a version property (`Version="$(FuaranCoreComputeVersion)"`, the second producer's
# pin), so the file's own property values are read first and substituted. A pin naming a property
# the file does not define fails rather than being read as a version.
$pinProps = @{}
foreach ($m in [regex]::Matches($pinsText, '<(\w+Version)>\s*([^<\s]+)\s*</\1>')) { $pinProps[$m.Groups[1].Value] = $m.Groups[2].Value }
$pins = @{}
foreach ($m in [regex]::Matches($pinsText, '<PackageVersion\s+Include="(Fuaran\.(?:Core|Compute)\.[^"]+)"\s+Version="([^"]+)"')) {
    $id = $m.Groups[1].Value
    $version = $m.Groups[2].Value
    $ref = [regex]::Match($version, '^\$\((\w+)\)$')
    if ($ref.Success) {
        $propName = $ref.Groups[1].Value
        if (-not $pinProps.ContainsKey($propName)) { Fail "the pin for $id names the property $propName, which Directory.Packages.props does not define" }
        $version = $pinProps[$propName]
    }
    $pins[$id] = $version
}

function Get-CandidateIds([string] $folder, [string] $version) {
    $escaped = [regex]::Escape($version)
    @(Get-ChildItem -LiteralPath $folder -Filter "Fuaran.*.$version.nupkg" |
        ForEach-Object { if ($_.Name -match "^(Fuaran\.(?:Core|Compute)\..+)\.$escaped\.nupkg$") { $Matches[1] } } |
        Sort-Object -Unique)
}

# The version a restored package must resolve at: its producer's candidate in a cut-time run of that
# producer, otherwise this repository's pin (which may be absent for a transitive package).
function Get-ExpectedVersion([string] $id) {
    if ($id -in $ComputeOwned) { if ($computeOverride) { return $ComputeVersion } }
    elseif ($coreOverride) { return $CoreVersion }
    return $pins[$id]
}

$restoreArgs = @('restore', $project, '--nologo')
$modeParts = New-Object System.Collections.Generic.List[string]

if ($coreOverride) {
    if (-not ($CoreVersion -and $CoreFeed)) { Fail 'a cut-time run needs BOTH -CoreVersion and -CoreFeed' }
    if (-not (Test-Path -LiteralPath $CoreFeed -PathType Container)) { Fail "-CoreFeed '$CoreFeed' is not a folder" }
    $coreFeedPath = (Resolve-Path -LiteralPath $CoreFeed).Path

    # The candidate's own packages, less the compute layer: a candidate cut before the split still
    # carries it, and from the split on those packages come from their own producer.
    $coreIds = @(Get-CandidateIds $coreFeedPath $CoreVersion | Where-Object { $_ -notin $ComputeOwned })
    if ($coreIds.Count -eq 0) { Fail "no Fuaran.Core.*.$CoreVersion.nupkg in $coreFeedPath" }
    [Environment]::SetEnvironmentVariable('FuaranCoreVersion', $CoreVersion)
    $modeParts.Add("Fuaran.Core $CoreVersion from $coreFeedPath ($($coreIds.Count) packages)")
}
else {
    $coreIds = @($pins.Keys | Where-Object { $_ -notin $ComputeOwned })
}

if ($computeOverride) {
    if (-not ($ComputeVersion -and $ComputeFeed)) { Fail 'a compute cut-time run needs BOTH -ComputeVersion and -ComputeFeed' }
    if (-not (Test-Path -LiteralPath $ComputeFeed -PathType Container)) { Fail "-ComputeFeed '$ComputeFeed' is not a folder" }
    $computeFeedPath = (Resolve-Path -LiteralPath $ComputeFeed).Path

    $computeIds = @(Get-CandidateIds $computeFeedPath $ComputeVersion | Where-Object { $_ -in $ComputeOwned })
    if ($computeIds.Count -eq 0) { Fail "no compute package ($($ComputeOwned -join ', ')) at $ComputeVersion in $computeFeedPath" }
    [Environment]::SetEnvironmentVariable('CoreFableComputeVersion', $ComputeVersion)
    $modeParts.Add("compute $ComputeVersion from $computeFeedPath ($($computeIds.Count) packages)")
}
else {
    $computeIds = @($pins.Keys | Where-Object { $_ -in $ComputeOwned })
}

$surfaceIds = @(@($coreIds) + @($computeIds) | Sort-Object -Unique)

if ($override) {
    # Each overridden producer's packages from its candidate folder and NOWHERE else; everything else
    # from nuget.org. Package source mapping picks the MOST SPECIFIC pattern — an exact id beats a
    # prefix, and `Fuaran.Core.*` beats `*` — so the compute ids are named exactly, routed to their
    # candidate folder when that producer is overridden and to nuget.org (where a pin must be
    # restorable) when it is not, so a Fuaran.Core candidate folder can never serve them.
    # Both candidates in ONE folder is one source: NuGet drops a second source with the same path, and
    # the patterns mapped to the dropped key would then resolve nowhere.
    $computePatterns = @($ComputeOwned | ForEach-Object { "<package pattern=`"$_`" />" })
    $sources = [ordered]@{ 'nuget.org' = @{ Path = 'https://api.nuget.org/v3/index.json'; Patterns = @('<package pattern="*" />') } }
    if ($coreOverride) {
        $sources['core-candidate'] = @{ Path = $coreFeedPath; Patterns = @('<package pattern="Fuaran.Core.*" />') }
    }
    if ($computeOverride) {
        if ($coreOverride -and [string]::Equals($coreFeedPath, $computeFeedPath, [StringComparison]::OrdinalIgnoreCase)) {
            $sources['core-candidate'].Patterns += $computePatterns
        }
        else {
            $sources['compute-candidate'] = @{ Path = $computeFeedPath; Patterns = $computePatterns }
        }
    }
    elseif ($coreOverride) {
        $sources['nuget.org'].Patterns += $computePatterns
    }
    $sourceLines = @($sources.Keys | ForEach-Object { "<add key=`"$_`" value=`"$($sources[$_].Path)`" />" })
    $mapLines = @($sources.Keys | ForEach-Object { "<packageSource key=`"$_`">$($sources[$_].Patterns -join '')</packageSource>" })

    $config = Join-Path $scratch 'nuget.config'
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    $($sourceLines -join "`n    ")
  </packageSources>
  <packageSourceMapping>
    $($mapLines -join "`n    ")
  </packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath $config -Encoding utf8NoBOM

    # An isolated package cache: a candidate is a draft that may be repacked at the SAME version, and
    # the shared cache would serve the first pack of it forever.
    [Environment]::SetEnvironmentVariable('NUGET_PACKAGES', (Join-Path $scratch 'packages'))
    $restoreArgs += @('--configfile', $config)
    if (-not $coreOverride) { $modeParts.Insert(0, "Fuaran.Core at the pin ($($pins['Fuaran.Core.Conformance']))") }
    if ($skipCompute) { $modeParts.Add('compute packages skipped') }
    elseif (-not $computeOverride) { $modeParts.Add("compute at the pin ($($pins['Fuaran.Compute.DataFrame']))") }
    $modeLine = "cut-time run — " + ($modeParts -join '; ')
}
else {
    $modeLine = "pinned — Fuaran.Core as this repository pins it ($($pins['Fuaran.Core.Conformance'])), the compute packages at $($pins['Fuaran.Compute.DataFrame'])"
}

Write-Host "==== core-fable: $modeLine" -ForegroundColor Cyan

if ($skipCompute) {
    [Environment]::SetEnvironmentVariable('CoreFableSkipCompute', 'true')
    Write-Host '  compute packages SKIPPED: a Core-only cut compiles the Core surface; the compute packages are gated by their own producer''s cut (-ComputeVersion/-ComputeFeed)' -ForegroundColor Yellow
}

try {
    # ── Membership ──────────────────────────────────────────────────────────

    # A package that first ships at a Core version this repository does not pin yet is referenced
    # with `CandidateFrom="<version>"` (the project restores it only for a candidate at or past that
    # version, since no pin exists for it): it is a MEMBER here only when the Core this run compiles
    # against — the candidate, or the pin — is at or past that version. A reference without the
    # marker counts unconditionally, as before. Without this, the first package Core adds after a
    # pin raise fails the pinned run as "referenced but not pinned" and the cut-time run as "neither
    # referenced nor excluded" — one of the two, whichever way the project is written.
    $effectiveCore = if ($coreOverride) { [version]$CoreVersion } else { [version]$pins['Fuaran.Core.Conformance'] }
    [xml]$projectXml = Get-Content -Raw $project
    $referenced = @($projectXml.SelectNodes('//PackageReference') |
        Where-Object { $_.Include -match '^Fuaran\.(Core|Compute)\.' } |
        Where-Object { -not $_.CandidateFrom -or [version]$_.CandidateFrom -le $effectiveCore } |
        ForEach-Object { $_.Include } | Sort-Object -Unique)

    $exclusions = @(Get-Content -Raw (Join-Path $PSScriptRoot 'exclusions.json') | ConvertFrom-Json)
    $malformed = @($exclusions | Where-Object { -not $_.package -or -not $_.reason -or -not $_.phase })
    if ($malformed.Count -gt 0) { Fail 'every exclusions.json entry carries a package, a reason and the phase that decided it' }
    $excluded = @($exclusions | ForEach-Object { $_.package })

    $problems = New-Object System.Collections.Generic.List[string]
    foreach ($id in $surfaceIds) {
        if ($id -notin $referenced -and $id -notin $excluded) {
            $problems.Add("$id is neither referenced by CoreFable.fsproj nor listed in exclusions.json")
        }
    }
    foreach ($id in $referenced) {
        if ($id -in $excluded) { $problems.Add("$id is referenced AND excluded — drop one") }
        if ($id -notin $surfaceIds) {
            $problems.Add($(if ($id -in $ComputeOwned -and $computeOverride) { "$id is referenced but the compute candidate ships no such package" }
                    elseif ($id -notin $ComputeOwned -and $coreOverride) { "$id is referenced but the candidate ships no such package" }
                    else { "$id is referenced but not pinned in Directory.Packages.props" }))
        }
    }
    # Only a cut-time run can see every package a producer ships, so only it can call an exclusion
    # stale — and only an exclusion of THAT producer's: pinned, this repository simply does not
    # consume the excluded tool packages.
    foreach ($id in $excluded) {
        $isCompute = $id -in $ComputeOwned
        if ((($isCompute -and $computeOverride) -or (-not $isCompute -and $coreOverride)) -and $id -notin $surfaceIds) {
            $problems.Add("exclusions.json names $id, which the $(if ($isCompute) { 'compute ' })candidate does not ship — drop the entry")
        }
    }
    if ($referenced.Count -lt 10) { $problems.Add("only $($referenced.Count) Core references were read from CoreFable.fsproj — the reading is broken") }
    if ($problems.Count -gt 0) { Fail ("membership:`n     " + ($problems -join "`n     ")) }

    Write-Host "  membership: $($referenced.Count) referenced, $($excluded.Count) excluded, $($surfaceIds.Count) on the $(if ($override) { 'candidate' } else { 'pinned' }) surface" -ForegroundColor DarkGray

    # ── Restore, and read what was actually restored ──────────────────────────

    & dotnet @restoreArgs
    if ($LASTEXITCODE -ne 0) { Fail 'restore' }

    $assets = Get-Content -Raw (Join-Path $PSScriptRoot 'obj' 'project.assets.json') | ConvertFrom-Json -AsHashtable
    $conformanceKey = @($assets['libraries'].Keys | Where-Object { $_ -like 'Fuaran.Core.Conformance/*' }) | Select-Object -First 1
    if (-not $conformanceKey) { Fail 'Fuaran.Core.Conformance is not in the restore — the parity decision has nothing to read' }
    $resolvedText = $conformanceKey.Split('/')[1]
    $hasTable = @($assets['libraries'][$conformanceKey]['files']) -contains 'fable/ParityVectors.fs'

    $computeKey = @($assets['libraries'].Keys | Where-Object { $_ -like 'Fuaran.Compute.DataFrame/*' }) | Select-Object -First 1
    $computeResolvedText = if ($computeKey) { $computeKey.Split('/')[1] } else { 'unresolved' }

    if ($override) {
        foreach ($key in @($assets['libraries'].Keys | Where-Object { $_ -like 'Fuaran.Core.*/*' -or $_ -like 'Fuaran.Compute.*/*' })) {
            $id, $resolvedVersion = $key.Split('/')
            $expected = Get-ExpectedVersion $id
            if ($expected -and $resolvedVersion -ne $expected) {
                $what = if ($id -in $ComputeOwned) { if ($computeOverride) { 'the compute candidate' } else { 'the compute pin' } }
                elseif ($coreOverride) { 'the candidate' } else { 'the pin' }
                Fail "the cut-time restore resolved $key, not $what $expected"
            }
        }
    }

    $decision = Get-ParityDecision (ConvertTo-Version $resolvedText) $hasTable $coreOverride

    switch ($decision) {
        'tripwire' {
            Fail ("Fuaran.Core.Conformance $resolvedText is at or above $VectorsSince, the first version that " +
                "ships ParityVectors, but the restored package does not carry fable/ParityVectors.fs. The parity " +
                "leg must run at this pin — find out why the table is missing rather than moving `$VectorsSince.")
        }
        'candidate' {
            Fail "the candidate Fuaran.Core.Conformance $resolvedText carries no fable/ParityVectors.fs, so a cut-time run cannot certify it"
        }
    }

    $parity = $decision -eq 'run'
    if ($parity) { [Environment]::SetEnvironmentVariable('CoreParity', 'true') }

    $node = Get-Command node -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($parity -and -not $node) {
        Fail ('no `node` on PATH. The parity leg RUNS the transpiled code; without a JS runtime the ' +
            'cross-pipeline value claim cannot be made at all, and reporting it as skipped-green would ' +
            'assert exactly what was not checked. Install Node.')
    }

    # ── The compile leg ─────────────────────────────────────────────────────

    # Assignment, never a pipe: a pipe reports the LAST command's status. Output under the scratch
    # root, never under obj/ (Fable re-parses beside a project's own intermediates there).
    $fableOutput = & dotnet fable $project -o $outDir --noCache --noRestore 2>&1
    $fableExit = $LASTEXITCODE
    foreach ($line in @($fableOutput | ForEach-Object { [string] $_ })) { Write-Host "  $line" }
    if ($fableExit -ne 0) { Fail "the Fable compile of the Core surface (exit $fableExit) — a public Fuaran.Core package is not Fable-clean" }

    $entry = Join-Path $outDir 'Program.js'
    if (-not (Test-Path -LiteralPath $entry)) { Fail "no emitted entry point at $entry" }

    # Every referenced package was actually transpiled. `fable_modules/<id>.<version>/` is what Fable
    # writes per compiled package; a reference it ignored would leave no directory. A Core-only cut
    # does not compile the compute packages, so it expects them absent — and checks that they are,
    # so a skip that did not happen cannot pass for one that did.
    $modulesDir = Join-Path $outDir 'fable_modules'
    $emitted = @(if (Test-Path -LiteralPath $modulesDir) { Get-ChildItem -LiteralPath $modulesDir -Directory | ForEach-Object Name })
    $compiled = @(if ($skipCompute) { $referenced | Where-Object { $_ -notin $ComputeOwned } } else { $referenced })
    if ($skipCompute) {
        $leaked = @($referenced | Where-Object { $_ -in $ComputeOwned } | Where-Object {
                $id = $_
                $emitted | Where-Object { $_ -match ('^' + [regex]::Escape($id) + '\.\d') }
            })
        if ($leaked.Count -gt 0) { Fail ("a Core-only cut transpiled the compute packages it skips: " + ($leaked -join ', ')) }
    }
    $notEmitted = @($compiled | Where-Object {
            $id = $_
            -not ($emitted | Where-Object { $_ -match ('^' + [regex]::Escape($id) + '\.\d') })
        })
    if ($notEmitted.Count -gt 0) { Fail ("referenced but not transpiled: " + ($notEmitted -join ', ')) }

    $computeNote = if ($skipCompute) { 'compute packages skipped' } else { "compute packages at $computeResolvedText" }
    Write-Host "  compile: green — $($compiled.Count) Fuaran.Core packages transpiled at $resolvedText ($computeNote)" -ForegroundColor Green

    # ── The parity leg ──────────────────────────────────────────────────────

    if (-not $parity) {
        Write-Host ''
        Write-Host "==== core-fable: PARITY LEG NOT RUN AT THIS PIN — Fuaran.Core.Conformance $resolvedText predates" -ForegroundColor Yellow
        Write-Host "     the ParityVectors table (first shipped in $VectorsSince). What covers the gap: every Fuaran.Core" -ForegroundColor Yellow
        Write-Host '     version cut cites a green run of this script against its candidate packages' -ForegroundColor Yellow
        Write-Host '     (-CoreVersion <v> -CoreFeed <folder>), in which the parity leg is REQUIRED. When this' -ForegroundColor Yellow
        Write-Host "     repository's pin reaches $VectorsSince the leg runs here by default, and if it does not, this" -ForegroundColor Yellow
        Write-Host '     script fails.' -ForegroundColor Yellow
        Write-Host "==== core-fable: green (compile leg; parity leg not runnable at $resolvedText)" -ForegroundColor Green
        exit 0
    }

    # The .NET side, built fresh: `CoreParity` changes the compiled defines, and an incremental build
    # can serve a binary compiled without them.
    & dotnet build $project --no-restore --no-incremental --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { Fail 'the .NET build of the vector program' }

    $dotnetOut = @(& dotnet run --project $project --no-build -- --vectors | Where-Object { $_ -like 'VEC *' })
    if ($LASTEXITCODE -ne 0) { Fail 'the .NET run of the vector table did not succeed' }

    $fableOut = @(& $node.Source $entry --vectors | Where-Object { $_ -like 'VEC *' })
    if ($LASTEXITCODE -ne 0) { Fail 'the node run of the vector table did not succeed' }

    # Emptiness and a count mismatch are failures in their own right: one side not producing the
    # table would otherwise read as "0 divergences", the vacuous green this leg exists to refuse.
    if ($dotnetOut.Count -eq 0) { Fail 'the .NET run emitted no vector lines at all' }
    if ($fableOut.Count -ne $dotnetOut.Count) { Fail ".NET emitted $($dotnetOut.Count) vectors, the node run emitted $($fableOut.Count)" }

    # `-cne` — case-SENSITIVE. Every value is lowercase hex or a canonical numeric/JSON layout, and a
    # case difference in a digest is a divergence, not a formatting preference.
    $diverged = @(0..($dotnetOut.Count - 1) | Where-Object { $dotnetOut[$_] -cne $fableOut[$_] })

    if ($KeepOutput) {
        Set-Content -Path (Join-Path $scratch 'parity-dotnet.txt') -Value $dotnetOut
        Set-Content -Path (Join-Path $scratch 'parity-fable.txt') -Value $fableOut
        Write-Host "  kept: $scratch" -ForegroundColor DarkGray
    }

    if ($diverged.Count -gt 0) {
        Write-Host "==== core-fable: FAILED — $($diverged.Count) of $($dotnetOut.Count) vectors differ between the pipelines" -ForegroundColor Red
        Write-Host '     (.NET is the canonical side)'
        foreach ($i in $diverged | Select-Object -First 10) {
            Write-Host "       .NET  $($dotnetOut[$i])"
            Write-Host "       Fable $($fableOut[$i])"
        }
        if ($diverged.Count -gt 10) { Write-Host "       … and $($diverged.Count - 10) more" }
        exit 1
    }

    Write-Host "==== core-fable: green — compile leg, and $($dotnetOut.Count)/$($dotnetOut.Count) vectors byte-identical on both pipelines at $resolvedText" -ForegroundColor Green
    exit 0
}
finally {
    foreach ($name in $touchedEnv) { [Environment]::SetEnvironmentVariable($name, $savedEnv[$name]) }
    if (-not $KeepOutput) { Remove-Item -Recurse -Force $scratch -ErrorAction SilentlyContinue }
}
