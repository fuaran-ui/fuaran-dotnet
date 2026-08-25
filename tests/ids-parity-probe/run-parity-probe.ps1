#Requires -Version 7.0
# fuaran — the cross-pipeline VALUE probe for the renderer's deterministic correlation ids
# (Phase 960).
#
# WHY THIS IS SEPARATE FROM THE FAKE GATE. That gate's Fable step (where one runs) is a COMPILE
# gate: it proves the package transpiles, which is a different claim from "and computes the same
# number". Nothing in a .NET test suite can make the second claim either.
# `Ids.deterministicCorrelationId` carried a naive FNV-1a multiply that diverged between the two
# pipelines on most seeds behind a fully green suite — measured, not hypothetical. So this is the
# only check in the repo that can catch that class for the correlation-id path, and it is the one
# to run when a value is claimed to be portable.
#
# It is NOT wired into run.ps1 or the FAKE targets on purpose: it needs a Node runtime, and the
# default gate is deliberately dependency-free. Run it by hand after touching `Ids.mul32` or the
# `deterministicCorrelationId` loop.
#
# Exit 0 = the two pipelines produced byte-identical output for every corpus entry.
[CmdletBinding()]
param(
    # Keep the emitted JS and the two captured outputs for inspection instead of comparing quietly.
    [switch] $KeepOutput
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not (Get-Command node -CommandType Application -ErrorAction SilentlyContinue)) {
    Write-Host '==== parity probe: SKIPPED — no `node` on PATH (this probe needs a JS runtime)' -ForegroundColor Yellow
    exit 0
}

$outDir = Join-Path $PSScriptRoot 'out'
Remove-Item -Recurse -Force $outDir -ErrorAction SilentlyContinue

# The .NET side. Filter to the corpus lines so build chatter can never enter the comparison.
$dotnetOut = (dotnet run --project IdsParityProbe.fsproj) | Where-Object { $_ -match '^\d{3} ' }
if ($LASTEXITCODE -ne 0) { Write-Host '==== parity probe: the .NET run FAILED' -ForegroundColor Red; exit 1 }

# The transpiled side. `dotnet fable` needs no npm/npx, so the workspace Invoke-Npm convention does
# not apply here; `node` is a real executable, not a PowerShell shim.
dotnet fable IdsParityProbe.fsproj -o $outDir --noCache | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host '==== parity probe: the Fable compile FAILED' -ForegroundColor Red; exit 1 }

$fableOut = (node (Join-Path $outDir 'Program.js')) | Where-Object { $_ -match '^\d{3} ' }
if ($LASTEXITCODE -ne 0) { Write-Host '==== parity probe: the node run FAILED' -ForegroundColor Red; exit 1 }

if ($dotnetOut.Count -eq 0 -or $fableOut.Count -ne $dotnetOut.Count) {
    # A count mismatch means one side did not produce the corpus at all. Reporting that as "0
    # divergences" would be a vacuous green, so it is a failure in its own right.
    Write-Host "==== parity probe: FAILED — .NET emitted $($dotnetOut.Count) lines, Fable emitted $($fableOut.Count)" -ForegroundColor Red
    exit 1
}

$diverged = @(0..($dotnetOut.Count - 1) | Where-Object { $dotnetOut[$_] -cne $fableOut[$_] })

if ($diverged.Count -gt 0) {
    Write-Host "==== parity probe: FAILED — $($diverged.Count) of $($dotnetOut.Count) corpus entries diverge" -ForegroundColor Red
    Write-Host '     (index / .NET / Fable — .NET is the canonical side)'
    foreach ($i in $diverged | Select-Object -First 10) {
        Write-Host "       .NET  $($dotnetOut[$i])"
        Write-Host "       Fable $($fableOut[$i])"
    }
    if ($diverged.Count -gt 10) { Write-Host "       … and $($diverged.Count - 10) more" }
    exit 1
}

if ($KeepOutput) {
    Set-Content -Path (Join-Path $PSScriptRoot 'probe-dotnet.txt') -Value $dotnetOut
    Set-Content -Path (Join-Path $PSScriptRoot 'probe-fable.txt') -Value $fableOut
} else {
    Remove-Item -Recurse -Force $outDir -ErrorAction SilentlyContinue
}

Write-Host "==== parity probe: green — $($dotnetOut.Count)/$($dotnetOut.Count) corpus entries byte-identical on both pipelines" -ForegroundColor Green
exit 0
