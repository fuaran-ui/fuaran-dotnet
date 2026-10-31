#Requires -Version 7.0
<#
.SYNOPSIS
  The client render-cost reading (Phase 2529): Fable compile, production bundle,
  a run in a real browser, a reading on disk. Not part of the gate.

.EXAMPLE
  pwsh ./run.ps1 -Out ./reading.json
  pwsh ./run.ps1 -Out ./reading.json -Compare ./reading-run1.json
#>
param(
    [string] $Out = "reading.json",
    [int] $Samples = 40,
    [string] $Compare,
    [switch] $SkipBuild
)

$ErrorActionPreference = "Stop"
$global:LASTEXITCODE = 0
Set-Location $PSScriptRoot

function Invoke-Npm {
    # Node 22.x ships an npm.ps1 shim that rebuilds args from the caller's command-line text via
    # Substring(InvocationName.Length). Called from inside another .ps1 as `& npm ci ...`, the
    # 3-char slice eats `& n` and npm sees `pm ci ...` — `Unknown command: "pm"`. Resolving npm.cmd
    # directly skips the shim.
    #
    # `Get-Command npm.cmd` returns EVERY npm.cmd on PATH — typically two (Program Files installer
    # shim + %APPDATA%\npm self-update shim). `$cmd.Source` would then be an array and `& $cmd.Source`
    # concatenates the paths into one bogus string. Pin to the first match — both shims behave alike.
    [CmdletBinding()]
    param([Parameter(ValueFromRemainingArguments = $true)] $Arguments)
    $cmd = Get-Command npm.cmd -CommandType Application -ErrorAction Stop | Select-Object -First 1
    & $cmd.Source @Arguments
}

function Invoke-NpmAnyOs {
    # Windows takes the canonical helper (the shim bug it dodges is Windows-only); elsewhere resolve the
    # npm APPLICATION on PATH explicitly — never a bare `& npm`. Pin to the first match, as above.
    param([Parameter(ValueFromRemainingArguments = $true)] $Arguments)
    if ($IsWindows) {
        Invoke-Npm @Arguments
    }
    else {
        $cmd = Get-Command npm -CommandType Application -ErrorAction Stop | Select-Object -First 1
        & $cmd.Source @Arguments
    }
}

function Assert-Stage([string] $name) {
    if ($LASTEXITCODE -ne 0) { throw "$name failed with exit $LASTEXITCODE" }
}

if (-not $SkipBuild) {
    Invoke-NpmAnyOs ci --no-fund --no-audit
    Assert-Stage "npm ci"
    dotnet tool restore
    Assert-Stage "tool restore"
    dotnet fable Fuaran.UI.Renderer.Benchmarks.fsproj -o output --noCache
    Assert-Stage "fable"
    node ./node_modules/vite/bin/vite.js build
    Assert-Stage "vite build"
}

node ./drive.mjs $Out $Samples
Assert-Stage "the measurement"

if ($Compare) {
    node ./compare.mjs $Compare $Out
    Assert-Stage "the comparison"
}
