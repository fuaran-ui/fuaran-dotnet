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

  Switches stack: -SkipFormat / -SkipBuild / -SkipTests for fast iteration
  loops inside the verify mode.

.EXAMPLE
  pwsh ./run.ps1

  Full verify: tool restore → fantomas --check → dotnet build → every
  Expecto suite.

.EXAMPLE
  pwsh ./run.ps1 -SkipFormat -SkipBuild

  Re-test after a code edit (skip format + build for ~10s loop).

.EXAMPLE
  pwsh ./run.ps1 -Demo

  Launch the Vite + Fable demo at http://localhost:24000.
#>
[CmdletBinding()]
param(
    [switch] $SkipFormat,
    [switch] $SkipBuild,
    [switch] $SkipTests,
    [switch] $Validate,
    [switch] $SkipPublishCheck,
    [switch] $Demo
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
$sln = "Fuaran.sln"
$testProjects = @(
    "src/Fuaran.UI.Tests/Fuaran.UI.Tests.fsproj"
    "src/Fuaran.UI.Ops.Tests/Fuaran.UI.Ops.Tests.fsproj"
    "src/Fuaran.UI.AiTools.Tests/Fuaran.UI.AiTools.Tests.fsproj"
    "src/Fuaran.UI.Validator.Tests/Fuaran.UI.Validator.Tests.fsproj"
    "src/Fuaran.UI.JsonDecode.Tests/Fuaran.UI.JsonDecode.Tests.fsproj"
    "src/Fuaran.UI.OpStream.Tests/Fuaran.UI.OpStream.Tests.fsproj"
    "src/Fuaran.UI.LayoutObserver.Tests/Fuaran.UI.LayoutObserver.Tests.fsproj"
    "src/Fuaran.UI.Telemetry.Tests/Fuaran.UI.Telemetry.Tests.fsproj"
)

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
    Write-Step "dotnet build $sln -c Release"
    dotnet build $sln -c Release
    if ($LASTEXITCODE -ne 0) { Write-Error "Build failed (exit $LASTEXITCODE)"; exit $LASTEXITCODE }
}

if (-not $SkipTests) {
    foreach ($project in $testProjects) {
        Write-Step "Expecto: $project"
        # Expecto console runner — `dotnet run --project`, NOT `dotnet test`
        # (`dotnet test` silently no-ops on Expecto consoles).
        # `--no-build` MUST precede `--project` or `dotnet run` forwards
        # it to Expecto.
        if ($SkipBuild) {
            dotnet run --project $project -c Release
        }
        else {
            dotnet run --no-build --project $project -c Release
        }
        if ($LASTEXITCODE -ne 0) { Write-Error "$project failed (exit $LASTEXITCODE)"; exit $LASTEXITCODE }
    }
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
        dotnet run --no-build --project $validatorProject -c Release -- $project.FullName
        if ($LASTEXITCODE -ne 0) { Write-Error "Validator failed on $($project.FullName) (exit $LASTEXITCODE)"; exit $LASTEXITCODE }
    }
}

# ─── Publish readiness: is the public channel behind <Version>? ──────
# The packages restore from nuget.org for every consumer outside this
# workspace — including eval-suite's free-tier CI, which builds against the
# RELEASED packages and has no local feed. Publication is triggered by a `v*`
# tag (see .github/workflows/publish-packages.yml), so a <Version> bump that
# is never tagged leaves those consumers pinning a version that exists only
# on the machine that packed it.
#
# That is not hypothetical: <Version> ran 0.18.0 -> 0.26.0 between 2026-08-13
# and 2026-08-16 with no tag pushed after v0.18.0, and eval-suite's every-PR
# conformance gate was red on NU1102 for five days as a result — 60
# consecutive failing runs, whose cause was a wall of "Unable to find package"
# lines rather than anything naming the omission.
#
# WARN, never fail: the commit that bumps <Version> legitimately precedes its
# tag, so a hard gate here would block the very change it is asking for. The
# point is that the gap is stated at the moment it opens, not discovered days
# later in a consumer's CI.
if (-not $SkipPublishCheck) {
    Write-Step "Publish readiness (<Version> vs the newest v* tag)"

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
            Write-Host ""
            Write-Host "  <Version> is $version; the newest tag is v$newestTag." -ForegroundColor Yellow
            Write-Host "  Consumers that restore from nuget.org cannot see $version - they will fail" -ForegroundColor Yellow
            Write-Host "  with NU1102, naming every package rather than the missing tag." -ForegroundColor Yellow
            Write-Host ""
            Write-Host "  Publish it with the release gesture (the tag IS the trigger):" -ForegroundColor Yellow
            Write-Host "      git tag v$version" -ForegroundColor Cyan
            Write-Host "      git push origin v$version" -ForegroundColor Cyan
            Write-Host ""
            Write-Host "  Deliberately holding a version back is fine - re-run with -SkipPublishCheck." -ForegroundColor DarkGray
        }
        else {
            Write-Host "v$newestTag published; <Version> is $version - the public channel is current." -ForegroundColor Green
        }
    }
}

Write-Host ""
Write-Host "✓ Verify passed." -ForegroundColor Green
exit 0
