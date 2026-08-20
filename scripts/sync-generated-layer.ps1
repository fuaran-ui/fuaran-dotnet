#Requires -Version 7.0
<#
.SYNOPSIS
    Drift-guard (and, on request, adopt) the IDL-generated structural layer.

.DESCRIPTION
    The wire vocabulary's single source is the IDL in Fuaran-Core; the generator
    (`Fuaran.Core.Idl.Gen.fsharpModuleWith`) emits the structural layer — types +
    encoder + plumbing — and Core commits its own copy at
    tests/Fuaran.Core.Tests/UiGenerated.fs, drift-guarded there against the
    generator by IdlUiGenTests.

    The tier's src/Fuaran.UI/Generated.fs is that artefact with one transform: the
    module declaration (the module name is a generator parameter, so the emitted
    body is identical either way).

    DEMOTED (Phase 704). This script's DEFAULT is now the CHECK, not the copy.
    The copy is behind an explicit -Adopt, for two reasons:

      * A hand-run byte-copy is the wrong primary mechanism. It is the thing
        Phase 704 set out to remove, and half of what forced it is now gone —
        `Fuaran.Core.Idl` is packable from Core 0.4.0, so the ENGINE is consumable
        by any workspace off the feed rather than reachable only through a sibling
        checkout.
      * Running the copy unprompted has already caused damage. Between the phases
        that hand-added regions here and the ones that backfilled them into the
        IDL, the tier was AHEAD of Core, and a bare run erased 292 lines and broke
        the build. A guard that defaults to reporting cannot do that.

    WHAT STILL BLOCKS RETIRING IT OUTRIGHT: the vocabulary. Under the home rule
    recorded in Fuaran-Core's DECISIONS.md D14 and in ../docs/DECISIONS.md D5, a
    domain's vocabulary lives in the domain's own repo — but `uiIdl` and its
    declared-support record are still Core's engine-certification fixture, and the
    generator needs BOTH plus the host prelude to emit. `idl.json` cannot stand in:
    the artifact module renders, it does not parse. So the tier cannot yet
    regenerate locally, and this byte-pin is what keeps the two copies honest
    meanwhile.

    RETIRE THIS SCRIPT when the vocabulary lands in this repo: the check becomes a
    regenerate-and-compare against the packaged engine, in-process, with no sibling
    checkout involved at all. The .fantomasignore entry for Generated.fs survives
    that change — the file stays generated output either way.

    The check is enforced, not remembered: .github/workflows/apply-parity-fable.yml
    runs it on every push.

.PARAMETER Check
    Explicit form of the default. Retained so existing callers keep working.

.PARAMETER Adopt
    Overwrite the tier's copy from Core's. Only correct when CORE moved; if the
    TIER copy was edited, adopting erases it — see the drift message.
#>
[CmdletBinding()]
param(
    [switch] $Check,
    [switch] $Adopt
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if ($Check -and $Adopt) {
    Write-Error "-Check and -Adopt are mutually exclusive."
    exit 2
}

# The demotion, in one line: checking is what happens unless you ask for the copy.
$adopting = [bool] $Adopt

$source = Join-Path $PSScriptRoot "..\..\..\Fuaran-Core\tests\Fuaran.Core.Tests\UiGenerated.fs"
$target = Join-Path $PSScriptRoot "..\src\Fuaran.UI\Generated.fs"

if (-not (Test-Path $source)) {
    Write-Host "Fuaran-Core is not checked out beside this repo ($source) — nothing to compare against."
    Write-Host "The committed src/Fuaran.UI/Generated.fs stays as-is; it is regression-locked by the corpus."
    exit 0
}

# The single transform: the generated module's name. Everything else is verbatim.
$emitted = (Get-Content -Raw $source) -replace '(?m)^module Fuaran\.Core\.Tests\.UiGenerated$', 'module Fuaran.UI.Generated'

if ($adopting) {
    Set-Content -Path $target -Value $emitted -NoNewline
    Write-Host "Adopted src/Fuaran.UI/Generated.fs from Fuaran-Core (module renamed to Fuaran.UI.Generated)."
    exit 0
}

# --- the default: check ---------------------------------------------------
if (-not (Test-Path $target)) {
    Write-Error "Generated.fs is missing — run pwsh ./scripts/sync-generated-layer.ps1 -Adopt to seed it."
    exit 1
}

$current = Get-Content -Raw $target

if ($current -ne $emitted) {
    # Say which way the drift runs before advising. There was a stretch where this
    # advice was actively harmful: the tier was AHEAD (hand-added regions Core could
    # not emit), and re-running the copy erased 292 lines and broke the build. Under
    # the declared-support model any tier-side lead is a defect by definition, but
    # the remedy differs by direction and the guard should name both rather than
    # assume. Adopting is now opt-in for that reason.
    Write-Error ("src/Fuaran.UI/Generated.fs differs from Fuaran-Core's UiGenerated.fs.`n" +
        "  If CORE moved (an IDL / UiIdlSupport change was regenerated there): pwsh ./scripts/sync-generated-layer.ps1 -Adopt.`n" +
        "  If the TIER copy was edited: do NOT adopt over it - Generated.fs is generated output, and content it needs that Core cannot emit belongs in Fuaran-Core's UiIdlSupport.fs (docs / splices / refines / kind projections), regenerated there and then adopted here.")
    exit 1
}

Write-Host "Generated.fs is in sync with Fuaran-Core's IDL-generated layer."
exit 0
