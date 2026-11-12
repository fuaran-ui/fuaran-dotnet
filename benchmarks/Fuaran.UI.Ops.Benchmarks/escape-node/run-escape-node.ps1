#Requires -Version 7.0
<#
.SYNOPSIS
  Phase 2026 - compile EscapeNode.fsproj with Fable and run the escape cases under node.
.DESCRIPTION
  Prints `opstream.escape.<case>.node.mean_ns` for the three `escape-rate` cases. The output
  directory is outside the repository (a fresh temp path unless -OutDir is given), never `obj/`.
  `dotnet fable` and `node` are read by their exit codes, never through a pipe.
#>
param(
    [int] $Iterations = 2000,
    [string] $OutDir = (Join-Path ([IO.Path]::GetTempPath()) "fuaran-escape-node-$PID")
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not (Get-Command node -CommandType Application -ErrorAction SilentlyContinue)) {
    Write-Host 'escape-node: no `node` on PATH' -ForegroundColor Red
    exit 1
}

$global:LASTEXITCODE = 0
$compile = & dotnet fable EscapeNode.fsproj -o $OutDir --noCache
if ($LASTEXITCODE -ne 0) {
    $compile | Write-Host
    Write-Host "escape-node: dotnet fable exited $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

$global:LASTEXITCODE = 0
& node (Join-Path $OutDir 'Program.js') $Iterations
exit $LASTEXITCODE
