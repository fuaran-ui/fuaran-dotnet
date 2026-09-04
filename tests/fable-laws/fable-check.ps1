#Requires -Version 7.0
<#
.SYNOPSIS
  The repo's FABLE STAGE: compile the client tier under its own settings, then run the Fable law
  harness under Node and compare it to the same harness on .NET.

.DESCRIPTION
  WHY THIS EXISTS. Until Phase 1488 the only Fable target in this repo was the FAKE `Catalog`
  target — a transpile of a sample, run standalone, executing no assertions. So `Check` and
  `run.ps1` could both be green while the client tier did not compile under a consumer's settings,
  and neither could say anything at all about whether the transpiled algebra still behaves. Both
  halves of that gap are closed here, and this ONE script is what both entry points call: the FAKE
  `FableCheck` target and `run.ps1`. Declared once, read by both — the same posture
  `test-suites.json` takes for the test roster, and for the same reason.

  TWO STAGES, and they answer different questions.

  1. PORTABILITY. The three client-tier projects that ship `Content Include="**\*.fs"
     PackagePath="fable\"` are Fable-compiled UNDER THEIR OWN MSBuild PROPERTIES. Under Fable it is
     the ENTRY project's properties that govern the whole transpiled source graph, so a compile
     entered through a `<Nullable>disable</Nullable>` sample says nothing about a nullable-enabled
     consumer — which is exactly what every Fable lane in this repo did before 2026-07-29, and why
     CI's `fable-portability` job exists. This stage is that job, reachable locally. A server-only
     API leaking into a Fable-consumed file, or an F# 10 nullness cascade through a pre-nullable
     Fable library, fails HERE rather than in a consumer's browser.

  2. THE LAWS. `FableLaws.fsproj` beside this script is compiled and run under Node, and its output
     is compared BYTE FOR BYTE against the same program on .NET. Each line carries counts, so two
     pipelines that are each internally lawful and disagree about a merge outcome still differ
     line-for-line; a law-only probe would report that as two green runs.

  METHOD NOTES — both learned the hard way, both recorded in `CLAUDE.md` under "Fable method
  traps", and both binding on anything added here:

    * `dotnet fable`'s exit code is read DIRECTLY from `$LASTEXITCODE`. It is never piped — a pipe
      reports the LAST command's status, so `dotnet fable ... | tail` reports `tail`'s success and a
      failed compile reads as a pass.
    * The output directory is never `obj/`. Fable writes beside the project's own build
      intermediates there, re-parses part of the project, and reports errors against files the
      change never touched — an hour of looking in the wrong place.

  `dotnet fable` is a .NET local tool and `node` is a real executable, so the workspace's
  `Invoke-Npm` / `Invoke-Npx` convention does not apply here: there is no npm/npx PowerShell shim
  in this path to be defeated by.

  EXIT 0 = every portability compile succeeded AND (where Node is present) the law harness reported
  zero violations on both pipelines with byte-identical output.
#>
[CmdletBinding()]
param(
    # Skip the three client-tier portability compiles (the slow half, ~1 min).
    [switch] $SkipPortability,
    # Skip the law harness (the half that needs Node).
    [switch] $SkipLaws,
    # Keep the emitted JavaScript and the two captured outputs for inspection.
    [switch] $KeepOutput
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$failures = New-Object System.Collections.Generic.List[string]

function Write-Stage {
    param([string] $message)
    Write-Host ""
    Write-Host "── Fable: $message ──────────────────────────────" -ForegroundColor Cyan
}

# ── 1. Portability ──────────────────────────────────────────────────────────
#
# ONLY ADD PROJECTS THAT PASS. A guard that is red on known-open work trains everyone to ignore
# it, which is worse than not having it. This list is the one CI's `fable-portability` job carries:
# `Fuaran.UI` is the root of the graph, and `ServerDriven` / `StyleObserver` are the two
# nullable-enabled entry points that transitively pull in Renderer.Core / ThemeManifest /
# StyleObserver.Abstractions — which is how those are covered without a compile each.

$portabilityProjects = @(
    'src/Fuaran.UI/Fuaran.UI.fsproj'
    'src/Fuaran.UI.StyleObserver/Fuaran.UI.StyleObserver.fsproj'
    'src/Fuaran.UI.ServerDriven/Fuaran.UI.ServerDriven.fsproj'
)

# Outside the repo tree on purpose: Fable emits a deep `fable_modules/` graph, and a deep output
# path under an already-deep worktree hits MAX_PATH, where fsc fails without a readable error.
$portabilityRoot = Join-Path ([IO.Path]::GetTempPath()) 'fuaran-fable-portability'

if (-not $SkipPortability) {
    Write-Stage "portability — $($portabilityProjects.Count) client-tier projects under their own settings"

    Remove-Item -Recurse -Force $portabilityRoot -ErrorAction SilentlyContinue

    foreach ($project in $portabilityProjects) {
        $full = Join-Path $repoRoot $project
        $leaf = [IO.Path]::GetFileNameWithoutExtension($project)
        $outDir = Join-Path $portabilityRoot $leaf

        Write-Host "  fable $project" -ForegroundColor DarkGray

        # --noCache is mandatory: a stale .fable cache can serve a compile that no longer reflects
        # the sources, which is the one answer this stage must never give.
        dotnet fable $full -o $outDir --noCache

        if ($LASTEXITCODE -ne 0) {
            $failures.Add("Fable portability compile FAILED for $project (exit $LASTEXITCODE)")
            Write-Host "  FAILED: $project" -ForegroundColor Red
        }
    }

    if (-not $KeepOutput) {
        Remove-Item -Recurse -Force $portabilityRoot -ErrorAction SilentlyContinue
    }
}

# ── 2. The laws ─────────────────────────────────────────────────────────────

$lawsOut = Join-Path $PSScriptRoot 'output'
$lineShape = '^(MERGE|MERGELAW|MERGEFINDING|MERGEFAIL|ADEQUACY|KIT|KITFAIL|TOTAL) '

if (-not $SkipLaws) {
    Write-Stage 'laws — TreeMerge.merge3Way + FoldConfluence.laneFoldLaws, .NET vs Node'

    if (-not (Get-Command node -CommandType Application -ErrorAction SilentlyContinue)) {
        # A NAMED skip, never a silent one — the posture `test-suites.json`'s corpus gate takes.
        # The portability stage above needed no Node and has already run, so the compile half of
        # this gate is intact on a machine that has never installed one.
        Write-Host 'SKIPPED — no `node` on PATH; the law harness needs a JS runtime.' -ForegroundColor Yellow
        Write-Host '         (The portability compiles above ran; only the behavioural half is skipped.)' -ForegroundColor Yellow
    }
    else {
        Remove-Item -Recurse -Force $lawsOut -ErrorAction SilentlyContinue

        # The .NET leg. Filtered to the harness's own line shapes so build chatter can never enter
        # the comparison.
        $dotnetOut = @(dotnet run --project (Join-Path $PSScriptRoot 'FableLaws.fsproj') -c Release |
            Where-Object { $_ -match $lineShape })
        $dotnetExit = $LASTEXITCODE

        dotnet fable (Join-Path $PSScriptRoot 'FableLaws.fsproj') -o $lawsOut --noCache

        if ($LASTEXITCODE -ne 0) {
            $failures.Add("Fable compile of the law harness FAILED (exit $LASTEXITCODE)")
        }
        else {
            $fableOut = @(node (Join-Path $lawsOut 'Program.js') | Where-Object { $_ -match $lineShape })
            $fableExit = $LASTEXITCODE

            if ($dotnetOut.Count -eq 0) {
                $failures.Add('the law harness produced no output on .NET — it did not run to completion')
            }
            elseif ($fableOut.Count -ne $dotnetOut.Count) {
                # A count mismatch means one side did not run the laws at all. Reporting that as
                # "0 divergences" would be a vacuous green, so it is a failure in its own right.
                $failures.Add("the law harness emitted $($dotnetOut.Count) lines on .NET and $($fableOut.Count) under Node")
            }
            else {
                $diverged = @(0..($dotnetOut.Count - 1) | Where-Object { $dotnetOut[$_] -cne $fableOut[$_] })

                if ($diverged.Count -gt 0) {
                    $failures.Add("$($diverged.Count) of $($dotnetOut.Count) law lines diverge between the pipelines")
                    Write-Host '  (.NET is the canonical side)' -ForegroundColor Red
                    foreach ($i in $diverged | Select-Object -First 10) {
                        Write-Host "    .NET  $($dotnetOut[$i])" -ForegroundColor Red
                        Write-Host "    Fable $($fableOut[$i])" -ForegroundColor Red
                    }
                }
            }

            # Both the exit codes AND the summary line are asserted. Fable drops `main`'s return
            # value, so the Node exit status is set by hand in `Program.fs`; reading only that
            # would make the whole stage depend on one line of interop staying correct.
            foreach ($line in $dotnetOut) { Write-Host "  $line" }

            $total = $dotnetOut | Where-Object { $_ -match '^TOTAL ' } | Select-Object -Last 1

            if (-not $total) {
                $failures.Add('no TOTAL line — the law harness did not run to completion')
            }
            elseif ($total -notmatch 'violations=(\d+)') {
                $failures.Add("unreadable summary line: $total")
            }
            elseif ([int]$Matches[1] -ne 0) {
                $failures.Add("$([int]$Matches[1]) law violation(s) — $total")
            }

            if ($dotnetExit -ne 0) { $failures.Add("the .NET law run exited $dotnetExit") }
            if ($fableExit -ne 0) { $failures.Add("the Node law run exited $fableExit") }
        }

        if (-not $KeepOutput) {
            Remove-Item -Recurse -Force $lawsOut -ErrorAction SilentlyContinue
        }
    }
}

# ── 3. Verdict ──────────────────────────────────────────────────────────────

Write-Host ""

if ($failures.Count -gt 0) {
    Write-Host "==== Fable stage: FAILED ($($failures.Count))" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "     $f" -ForegroundColor Red }
    exit 1
}

Write-Host '==== Fable stage: green' -ForegroundColor Green
exit 0
