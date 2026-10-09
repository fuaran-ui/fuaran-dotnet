#Requires -Version 7.0
<#
.SYNOPSIS
  The packed-consumer leg (Phase 2128, widened by Phase 2139): pack `Fuaran.UI.Telemetry.Default`,
  `Fuaran.UI.OpStream.Dag.Merge` and everything they reference, restore a Fable consumer against
  THOSE PACKAGES, and compile it under Fable.

.DESCRIPTION
  WHAT IT CATCHES. `Fuaran.UI.Telemetry.Default` packed no `fable/` sources until Phase 2128, so a
  Fable consumer reaching it read it as a compiled assembly. Its `NoOpSink.create` returns a type
  from the `Fuaran.UI.Telemetry.Abstractions` ASSEMBLY, which Fable had replaced with that
  package's sources, and the type identity broke: FS0074 "IFuaranTelemetrySink is defined in an
  assembly that is not referenced", at every call site. A .NET build of the same consumer is green,
  and so is every Fable compile inside this repository, because under Fable a ProjectReference is
  always compiled from source. Only a consumer of the PACKED package sees what a consumer of the
  published package sees, so that is what this leg builds.

  THE STATIC HALF of the same property is `build/FablePackCheck.fs` (the FAKE `FablePackCheck`
  target): every package this repository publishes that a Fable consumer can reach ships its
  sources or declares itself .NET-only. That check reads project files; this leg proves the packed
  artefact for the case that was broken.

  Phase 2139 adds the second producer: `Fuaran.UI.OpStream.Dag.Merge` was published as an assembly
  only, so a browser consumer could run the merge engine solely through a source reference into this
  repository. It now ships its sources, and the consumer calls into the merge engine through the
  packed package, so a regression to an assembly-only package fails here by name.

  WHAT IT PACKS is DERIVED from the producer roots below, not listed: each root and the transitive
  closure of its `ProjectReference`s, each packed under one throwaway version (`<Version>-fablepack`) into a
  scratch folder outside the repository. The consumer restores those ids from that folder and
  nowhere else (exact-id source mapping beats every prefix), and everything else from this
  repository's own sources. The package cache is isolated to the scratch root and the packed ids are
  evicted from it before every restore, so a repack at the same version is never served stale.

  WHAT GREEN MEANS. The Fable compile exits 0 AND the emitted `fable_modules/` holds a directory for
  every packed package — a package Fable read as an assembly rather than transpiling leaves none, so
  a compile that went green by ignoring the sources cannot pass.

  RUN IT STANDALONE from anywhere:  pwsh ./tests/fable-pack-consumer/fable-pack-consumer.ps1
  `tests/fable-laws/fable-check.ps1` runs it as part of the Fable stage. Exit 0 = green.
#>
[CmdletBinding()]
param(
    # Keep the emitted JavaScript and the packed packages in the scratch root for inspection.
    [switch] $KeepOutput
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# Seeded because every guard below reads it: `$LASTEXITCODE` is `$null` until a native command runs.
$global:LASTEXITCODE = 0

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$project = Join-Path $PSScriptRoot 'FablePackConsumer.fsproj'
# The producers whose PACKED artefacts the consumer compiles against. Each one's closure is packed too.
$producerRoots = @(
    (Join-Path $repoRoot 'src' 'Fuaran.UI.Telemetry.Default' 'Fuaran.UI.Telemetry.Default.fsproj')
    # Phase 2139: the merge engine, offered to Fable consumers.
    (Join-Path $repoRoot 'src' 'Fuaran.UI.OpStream.Dag.Merge' 'Fuaran.UI.OpStream.Dag.Merge.fsproj')
)

function Fail([string] $message) {
    Write-Host "==== fable-pack-consumer: FAILED — $message" -ForegroundColor Red
    exit 1
}

# ── What to pack: the producers and their ProjectReference closure ──────────

function Get-ProjectReferences([string] $fsproj) {
    $dir = Split-Path -Parent $fsproj
    @([regex]::Matches((Get-Content -Raw -LiteralPath $fsproj), '<ProjectReference\s+Include="([^"]+)"') |
        ForEach-Object { [IO.Path]::GetFullPath((Join-Path $dir ($_.Groups[1].Value -replace '\\', '/'))) })
}

$closure = [System.Collections.Generic.List[string]]::new()
$pending = [System.Collections.Generic.Queue[string]]::new()
foreach ($root in $producerRoots) { $pending.Enqueue([IO.Path]::GetFullPath($root)) }
while ($pending.Count -gt 0) {
    $next = $pending.Dequeue()
    if ($closure -contains $next) { continue }
    if (-not (Test-Path -LiteralPath $next -PathType Leaf)) { Fail "a project in the closure is missing: $next" }
    $closure.Add($next)
    foreach ($ref in (Get-ProjectReferences $next)) { $pending.Enqueue($ref) }
}
# Referenced projects first, so a reader of the log sees the foundations packed before their users.
$closure.Reverse()
$packedIds = @($closure | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_) })
# A reading that found only the root has stopped seeing the project graph.
if ($packedIds.Count -lt ($producerRoots.Count + 2)) { Fail "only $($packedIds.Count) project(s) read in the closure of $($producerRoots.Count) producer(s) — the reading is broken" }

$versionMatch = [regex]::Match((Get-Content -Raw (Join-Path $repoRoot 'Directory.Build.props')), '<Version>\s*([^<\s]+)\s*</Version>')
if (-not $versionMatch.Success) { Fail 'no <Version> in Directory.Build.props' }
$packVersion = "$($versionMatch.Groups[1].Value)-fablepack"

# ── Scratch ─────────────────────────────────────────────────────────────────

# Outside the repository (MAX_PATH, and nothing here is ever committed), keyed on this script's
# location so two worktrees never wipe each other's output. The package cache persists across runs
# (third-party packages are not re-downloaded) with the packed ids evicted before every restore.
$sha = [Security.Cryptography.SHA256]::Create()
$treeKey = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($PSScriptRoot.ToLowerInvariant()))) -replace '-', '').Substring(0, 12).ToLowerInvariant()
$sha.Dispose()
$scratch = Join-Path ([IO.Path]::GetTempPath()) "fuaran-fable-pack-consumer-$treeKey"
$feed = Join-Path $scratch 'feed'
$packages = Join-Path $scratch 'packages'
$outDir = Join-Path $scratch 'out'
foreach ($dir in @($feed, $outDir)) { Remove-Item -Recurse -Force -LiteralPath $dir -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Force -Path $feed, $packages | Out-Null
foreach ($id in $packedIds) {
    Remove-Item -Recurse -Force -LiteralPath (Join-Path $packages $id.ToLowerInvariant()) -ErrorAction SilentlyContinue
}

# This repository's own sources, resolved to absolute paths (the scratch config lives elsewhere),
# plus the scratch feed serving exactly the packed ids.
$repoConfig = [xml] (Get-Content -Raw (Join-Path $repoRoot 'nuget.config'))
$sourceLines = [System.Collections.Generic.List[string]]::new()
$mapLines = [System.Collections.Generic.List[string]]::new()
foreach ($add in @($repoConfig.configuration.packageSources.add)) {
    $value = [string] $add.value
    if ($value -notmatch '^https?://') {
        $value = [IO.Path]::GetFullPath((Join-Path $repoRoot $value))
        # CI mints the local folder empty; a machine without one simply has no such source.
        if (-not (Test-Path -LiteralPath $value -PathType Container)) { continue }
    }
    $sourceLines.Add("<add key=`"$($add.key)`" value=`"$value`" />")
    $mapped = @($repoConfig.configuration.packageSourceMapping.packageSource | Where-Object { $_.key -eq $add.key })
    $patterns = @($mapped | ForEach-Object { @($_.package) } | ForEach-Object { "<package pattern=`"$($_.pattern)`" />" })
    $mapLines.Add("<packageSource key=`"$($add.key)`">$($patterns -join '')</packageSource>")
}
$sourceLines.Add("<add key=`"fablepack`" value=`"$feed`" />")
$packedPatterns = @($packedIds | ForEach-Object { '<package pattern="' + $_ + '" />' }) -join ''
$mapLines.Add("<packageSource key=`"fablepack`">$packedPatterns</packageSource>")
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

Write-Host "==== fable-pack-consumer: packing $($packedIds.Count) package(s) at $packVersion — $($packedIds -join ', ')" -ForegroundColor Cyan

# Every property below is read by MSBuild from the environment, so `dotnet restore` and
# `dotnet fable` see the same values. Restored at the end either way.
$touchedEnv = @('FablePackConsumerVersion', 'NUGET_PACKAGES')
$savedEnv = @{}
foreach ($name in $touchedEnv) { $savedEnv[$name] = [Environment]::GetEnvironmentVariable($name) }

try {
    # ── Pack ──────────────────────────────────────────────────────────────────

    # `PackageVersion`, not `Version`: the package (and its dependencies on the others packed here)
    # carry the throwaway number while the assemblies are the ones the gate already built, so the
    # pack is an incremental no-op build rather than a rebuild of the closure.
    foreach ($fsproj in $closure) {
        $packOutput = & dotnet pack $fsproj -o $feed "-p:PackageVersion=$packVersion" --nologo 2>&1
        $packExit = $LASTEXITCODE
        if ($packExit -ne 0) {
            foreach ($line in @($packOutput | ForEach-Object { [string] $_ })) { Write-Host "  $line" }
            Fail "dotnet pack $([IO.Path]::GetFileName($fsproj)) (exit $packExit)"
        }
    }

    # ── Restore against the packed packages ───────────────────────────────────

    [Environment]::SetEnvironmentVariable('FablePackConsumerVersion', $packVersion)
    [Environment]::SetEnvironmentVariable('NUGET_PACKAGES', $packages)

    & dotnet restore $project --configfile $config --nologo
    if ($LASTEXITCODE -ne 0) { Fail 'restore of the consumer against the packed packages' }

    # The restore resolved every packed id from the packed version — not a released one that happened
    # to be cached or served by another source. Every id, so a closure member resolved at some other
    # version (which would compile against a different contract) fails by name.
    $assets = Get-Content -Raw (Join-Path $PSScriptRoot 'obj' 'project.assets.json') | ConvertFrom-Json -AsHashtable
    foreach ($id in $packedIds) {
        if (-not $assets['libraries'].ContainsKey("$id/$packVersion")) { Fail "the restore did not resolve $id at $packVersion" }
    }

    # ── The compile ───────────────────────────────────────────────────────────

    # Run from the repository root so the local tool manifest supplies the pinned Fable. Assignment,
    # never a pipe: a pipe reports the LAST command's status.
    Push-Location $repoRoot
    try {
        $fableOutput = & dotnet fable $project -o $outDir --noCache --noRestore 2>&1
        $fableExit = $LASTEXITCODE
    }
    finally { Pop-Location }
    $fableLines = @($fableOutput | ForEach-Object { [string] $_ })
    foreach ($line in $fableLines) { Write-Host "  $line" }
    if ($fableExit -ne 0) {
        $fs0074 = @($fableLines | Where-Object { $_ -match 'FS0074|\(code 74\)' }).Count
        if ($fs0074 -gt 0) {
            Fail ("the Fable compile reported FS0074 $fs0074 time(s) — a packed package reaching the consumer ships no " +
                'fable/ sources, so Fable read it as an assembly whose types point at an assembly it replaced with sources')
        }
        Fail "the Fable compile of the packed consumer (exit $fableExit)"
    }

    # Every packed package was TRANSPILED. Fable writes `fable_modules/<id>.<version>/` per package it
    # compiles from source; a package it read as an assembly leaves none.
    $modulesDir = Join-Path $outDir 'fable_modules'
    $emitted = @(if (Test-Path -LiteralPath $modulesDir) { Get-ChildItem -LiteralPath $modulesDir -Directory | ForEach-Object Name })
    $notEmitted = @($packedIds | Where-Object { "$_.$packVersion" -notin $emitted })
    if ($notEmitted.Count -gt 0) { Fail ('packed but not transpiled from source: ' + ($notEmitted -join ', ')) }

    Write-Host "==== fable-pack-consumer: green — $($packedIds.Count) packed package(s) transpiled from their fable/ sources at $packVersion" -ForegroundColor Green
}
finally {
    foreach ($name in $touchedEnv) { [Environment]::SetEnvironmentVariable($name, $savedEnv[$name]) }
    # The consumer's restore names throwaway packages from a scratch feed deleted below. Left in the
    # tree, its `obj/project.assets.json` reads to anything inventorying the repository's restores as
    # an input that no feed can serve again, so it goes with the feed, whatever the verdict.
    Remove-Item -Recurse -Force -LiteralPath (Join-Path $PSScriptRoot 'obj') -ErrorAction SilentlyContinue
    if (-not $KeepOutput) {
        Remove-Item -Recurse -Force -LiteralPath $outDir -ErrorAction SilentlyContinue
        Remove-Item -Recurse -Force -LiteralPath $feed -ErrorAction SilentlyContinue
    }
}
exit 0
