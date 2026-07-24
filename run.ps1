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

Write-Host ""
Write-Host "✓ Verify passed." -ForegroundColor Green
exit 0
