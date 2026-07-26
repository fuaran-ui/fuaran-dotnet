#Requires -Version 7.0
<#
.SYNOPSIS
    Sync the IDL-generated structural layer from Fuaran-Core into this tier.

.DESCRIPTION
    Phase 671 step 1. The wire vocabulary's single source is the IDL in
    Fuaran-Core; the generator (`Fuaran.Core.Idl.Gen.fsharpModule`) emits the
    structural layer — types + encoder + plumbing — and Core commits its own copy
    at tests/Fuaran.Core.Tests/UiGenerated.fs, drift-guarded against the
    generator by IdlUiGenTests.

    This script byte-copies that artefact into the tier, rewriting only the
    module declaration (the module name is a generator parameter, so the emitted
    body is identical either way). That is the same shape as the reference-CSS
    byte-copy discipline the estate already runs.

    WHY A COPY RATHER THAN GENERATING HERE: `Fuaran.Core.Idl` is IsPackable=false
    and the UI IDL itself (`uiIdl`) lives in Core's *test* project, so neither is
    reachable from this tier, which consumes Core purely as NuGet packages. When
    those become consumable this script should be replaced by a direct
    `Gen.fsharpModule "Fuaran.UI.Generated" uiIdl allKindTags` invocation.

    Idempotent: running it twice produces a byte-identical file.

.PARAMETER Check
    Verify only; exit 1 if the tier's copy is stale. Used by CI / the drift guard.
#>
[CmdletBinding()]
param(
    [switch] $Check
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$source = Join-Path $PSScriptRoot "..\..\..\Fuaran-Core\tests\Fuaran.Core.Tests\UiGenerated.fs"
$target = Join-Path $PSScriptRoot "..\src\Fuaran.UI\Generated.fs"

if (-not (Test-Path $source)) {
    Write-Host "Fuaran-Core is not checked out beside this repo ($source) — nothing to sync."
    Write-Host "The committed src/Fuaran.UI/Generated.fs stays as-is; it is regression-locked by the corpus."
    exit 0
}

# The single transform: the generated module's name. Everything else is verbatim.
$emitted = (Get-Content -Raw $source) -replace '(?m)^module Fuaran\.Core\.Tests\.UiGenerated$', 'module Fuaran.UI.Generated'

if ($Check) {
    if (-not (Test-Path $target)) {
        Write-Error "Generated.fs is missing — run this script without -Check to sync it."
        exit 1
    }
    $current = Get-Content -Raw $target
    if ($current -ne $emitted) {
        Write-Error "src/Fuaran.UI/Generated.fs is STALE vs Fuaran-Core's UiGenerated.fs. Re-run: pwsh ./scripts/sync-generated-layer.ps1"
        exit 1
    }
    Write-Host "Generated.fs is in sync with Fuaran-Core's IDL-generated layer."
    exit 0
}

Set-Content -Path $target -Value $emitted -NoNewline
Write-Host "Synced src/Fuaran.UI/Generated.fs from Fuaran-Core (module renamed to Fuaran.UI.Generated)."
