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

      That run restores every Fuaran.Core package from the candidate folder ONLY (an isolated
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
$override = [bool] ($CoreVersion -or $CoreFeed)

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
$touchedEnv = @('FuaranCoreVersion', 'CoreParity', 'NUGET_PACKAGES')
$savedEnv = @{}
foreach ($name in $touchedEnv) { $savedEnv[$name] = [Environment]::GetEnvironmentVariable($name) }
[Environment]::SetEnvironmentVariable('CoreParity', $null)
[Environment]::SetEnvironmentVariable('FuaranCoreVersion', $null)

$pinsFile = Join-Path $repoRoot 'Directory.Packages.props'
$pins = @{}
foreach ($m in [regex]::Matches((Get-Content -Raw $pinsFile), '<PackageVersion\s+Include="(Fuaran\.Core\.[^"]+)"\s+Version="([^"]+)"')) {
    $pins[$m.Groups[1].Value] = $m.Groups[2].Value
}

$restoreArgs = @('restore', $project, '--nologo')

if ($override) {
    if (-not ($CoreVersion -and $CoreFeed)) { Fail 'a cut-time run needs BOTH -CoreVersion and -CoreFeed' }
    if (-not (Test-Path -LiteralPath $CoreFeed -PathType Container)) { Fail "-CoreFeed '$CoreFeed' is not a folder" }
    $feed = (Resolve-Path -LiteralPath $CoreFeed).Path

    $escaped = [regex]::Escape($CoreVersion)
    $surfaceIds = @(Get-ChildItem -LiteralPath $feed -Filter "Fuaran.Core.*.$CoreVersion.nupkg" |
        ForEach-Object { if ($_.Name -match "^(Fuaran\.Core\..+)\.$escaped\.nupkg$") { $Matches[1] } } |
        Sort-Object -Unique)
    if ($surfaceIds.Count -eq 0) { Fail "no Fuaran.Core.*.$CoreVersion.nupkg in $feed" }

    # Fuaran.Core.* from the candidate folder and NOWHERE else; everything else from nuget.org.
    # Package source mapping resolves by longest matching prefix, so `Fuaran.Core.*` wins over `*`.
    $config = Join-Path $scratch 'nuget.config'
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="core-candidate" value="$feed" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
    <packageSource key="core-candidate"><package pattern="Fuaran.Core.*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath $config -Encoding utf8NoBOM

    # An isolated package cache: a candidate is a draft that may be repacked at the SAME version, and
    # the shared cache would serve the first pack of it forever.
    [Environment]::SetEnvironmentVariable('NUGET_PACKAGES', (Join-Path $scratch 'packages'))
    [Environment]::SetEnvironmentVariable('FuaranCoreVersion', $CoreVersion)
    $restoreArgs += @('--configfile', $config)
    $modeLine = "cut-time run — Fuaran.Core $CoreVersion from $feed ($($surfaceIds.Count) packages)"
}
else {
    $surfaceIds = @($pins.Keys | Sort-Object)
    $modeLine = "pinned — Fuaran.Core as this repository pins it ($($pins['Fuaran.Core.Conformance']))"
}

Write-Host "==== core-fable: $modeLine" -ForegroundColor Cyan

try {
    # ── Membership ──────────────────────────────────────────────────────────

    $referenced = @([regex]::Matches((Get-Content -Raw $project), '<PackageReference\s+Include="(Fuaran\.Core\.[^"]+)"') |
        ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)

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
            $problems.Add($(if ($override) { "$id is referenced but the candidate ships no such package" }
                    else { "$id is referenced but not pinned in Directory.Packages.props" }))
        }
    }
    if ($override) {
        # Only a cut-time run can see every package Core ships, so only it can call an exclusion
        # stale: pinned, this repository simply does not consume the excluded tool packages.
        foreach ($id in $excluded) {
            if ($id -notin $surfaceIds) { $problems.Add("exclusions.json names $id, which the candidate does not ship — drop the entry") }
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

    if ($override) {
        foreach ($key in @($assets['libraries'].Keys | Where-Object { $_ -like 'Fuaran.Core.*/*' })) {
            if ($key.Split('/')[1] -ne $CoreVersion) { Fail "the cut-time restore resolved $key, not the candidate $CoreVersion" }
        }
    }

    $decision = Get-ParityDecision (ConvertTo-Version $resolvedText) $hasTable $override

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
    # writes per compiled package; a reference it ignored would leave no directory.
    $modulesDir = Join-Path $outDir 'fable_modules'
    $emitted = @(if (Test-Path -LiteralPath $modulesDir) { Get-ChildItem -LiteralPath $modulesDir -Directory | ForEach-Object Name })
    $notEmitted = @($referenced | Where-Object {
            $id = $_
            -not ($emitted | Where-Object { $_ -match ('^' + [regex]::Escape($id) + '\.\d') })
        })
    if ($notEmitted.Count -gt 0) { Fail ("referenced but not transpiled: " + ($notEmitted -join ', ')) }

    Write-Host "  compile: green — $($referenced.Count) Fuaran.Core packages transpiled at $resolvedText" -ForegroundColor Green

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
