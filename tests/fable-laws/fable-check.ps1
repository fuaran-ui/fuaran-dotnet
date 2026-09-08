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

  1. PORTABILITY. The client-tier projects that ship `Content Include="**\*.fs"
     PackagePath="fable\"` are Fable-compiled UNDER THEIR OWN MSBuild PROPERTIES. Under Fable it is
     the ENTRY project's properties that govern the whole transpiled source graph, so a compile
     entered through a `<Nullable>disable</Nullable>` sample says nothing about a nullable-enabled
     consumer — which is exactly what every Fable lane in this repo did before 2026-07-29, and why
     CI's `fable-portability` job exists. This stage is that job, reachable locally. A server-only
     API leaking into a Fable-consumed file, or an F# 10 nullness cascade through a pre-nullable
     Fable library, fails HERE rather than in a consumer's browser.

     WHICH projects is DERIVED FROM THE TREE, not hand-kept (Phase 1606). See "The derivation"
     below; `-List` prints the answer with the reason beside each entry and compiles nothing.

  2. THE LAWS. `FableLaws.fsproj` beside this script is compiled and run under Node, and its output
     is compared BYTE FOR BYTE against the same program on .NET. Each line carries counts, so two
     pipelines that are each internally lawful and disagree about a merge outcome still differ
     line-for-line; a law-only probe would report that as two green runs.

  THE DERIVATION (Phase 1606). Fuaran.UI 0.78.0 shipped a Renderer whose `#if FABLE_COMPILER` arm
  did not compile — four bare `JVal` / `JStr` uses with no `open Fuaran.Core` — invisible to the
  .NET build, which compiles only the `#else` arm, and to this gate, whose hand-kept list did not
  name the Renderer. The 0.78.1 fix added it here and to the mirrored list in
  `.github/workflows/apply-parity-fable.yml`, by hand. Two lists that must agree, and that must be
  extended whenever a project gains a conditional arm, drift again. So they are computed:

    shipping   every `src/**/*.fsproj` packing its `.fs` sources under `PackagePath="fable\"` —
               i.e. every package a Fable consumer can transpile. THIS is the set the gate is
               responsible for, and it needs no list: a new package joins it by shipping the
               sources, which is the same act that makes it a Fable package at all.

    exempt     a shipping project may declare `<FablePortabilityExemption>why</...>` in its OWN
               fsproj, beside the pack path it qualifies. The property's VALUE is the reason, so an
               exemption without one cannot be written; the stage prints every exemption by name on
               every run, so it stays visible rather than becoming a list nobody re-reads. Default
               is GATED — a new package is covered on its first commit with no edit here.

    entries    the gated projects that are Fable-compiled directly, as the union of two rules:

               (a) the ROOTS of the gated reference graph — projects no other gated project
                   references. Every gated project is reachable from some root (project references
                   are acyclic), so entering the roots COVERS the whole set, and each covered
                   project's sources — conditional arms included — are compiled by some entry.
                   This is what the 0.78.0 hole actually was: `Renderer.Core` was reached through
                   `ServerDriven` and was fine; `Renderer` was reached by nothing.

               (b) every gated project holding a `#if [!]FABLE_COMPILER` arm. Redundant for
                   coverage — (a) already reaches it — but not for the PROPERTIES it is compiled
                   under, which is the question this stage exists to answer: reached transitively,
                   a project is compiled under the ENTRY's properties, not its own. A project whose
                   behaviour genuinely forks on the pipeline is the one where that distinction
                   costs something, so it is entered on its own account.

    coverage   asserted, not assumed: the closure of the entries over `ProjectReference` must equal
               the gated set. (a) makes that true by construction, so a failure here means the
               derivation stopped seeing part of the tree — which is exactly when a silent gap
               would otherwise open.

    audit      a PACKABLE `src/` project holding a conditional arm while shipping NO `fable\`
               sources fails by name. Such an arm is compiled by nothing and reaches no consumer;
               it is the 0.78.0 shape one step earlier, before the pack path is added.

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
    # Skip the client-tier portability compiles (the slow half).
    [switch] $SkipPortability,
    # Skip the law harness (the half that needs Node).
    [switch] $SkipLaws,
    # Keep the emitted JavaScript and the two captured outputs for inspection.
    [switch] $KeepOutput,
    # Print the derived portability set — entries, why each is one, what they cover, and every
    # declared exemption — then exit without compiling anything. This is the surface a reader (or
    # a workflow) consumes instead of re-deriving the list by hand.
    [switch] $List,
    # The directory the derivation walks. Defaults to `src/`, the repo's package tree. Overridden
    # ONLY by `fable-check.tests.ps1` beside this script, which runs this same derivation over a
    # scratch tree to prove the gate can go red. A gate whose scope is redirectable in ordinary use
    # would be no gate at all, so nothing else passes it.
    [string] $SrcRoot
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
# The set is DERIVED — see "The derivation" in the header above. Nothing below is a list of
# projects: a project joins the gate by shipping `fable\` sources, and leaves it only by declaring
# `<FablePortabilityExemption>` in its own fsproj.

# Repo-relative, forward slashes — stable across platforms and readable in a failure line.
function ConvertTo-RepoRelative {
    param([string] $path)
    ([IO.Path]::GetRelativePath($repoRoot, $path)) -replace '\\', '/'
}

# `XmlDocument.Load` (rather than an `[xml]` cast over `Get-Content`) so a byte-order mark or a
# declared encoding is handled by the parser rather than by luck.
function Read-ProjectXml {
    param([string] $path)
    $doc = New-Object System.Xml.XmlDocument
    $doc.Load($path)
    $doc
}

# A project ships Fable sources when it packs `*.fs` under `fable\`. The `*.fsproj` half of the
# same Include is deliberately not enough on its own: a project packing only its fsproj would give
# a consumer nothing to compile.
function Test-ShipsFableSources {
    param([System.Xml.XmlDocument] $doc)
    foreach ($content in $doc.SelectNodes('//Content[@PackagePath][@Include]')) {
        if ($content.GetAttribute('PackagePath') -notmatch '^fable[\\/]?$') { continue }
        foreach ($pattern in ($content.GetAttribute('Include') -split ';')) {
            if ($pattern.Trim() -match '\*\.fs$') { return $true }
        }
    }
    return $false
}

# `#if` / `#elif` only — a bare mention of FABLE_COMPILER in a comment or a string is not an arm.
$armPattern = '(?m)^\s*#(if|elif)\b[^\r\n]*\bFABLE_COMPILER\b'

function Test-HasConditionalArm {
    param([string] $projectDir)
    $sources = Get-ChildItem -Path $projectDir -Recurse -Filter *.fs -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
    foreach ($source in $sources) {
        if ([IO.File]::ReadAllText($source.FullName) -match $armPattern) { return $true }
    }
    return $false
}

$srcRoot = if ($SrcRoot) { (Resolve-Path $SrcRoot).Path } else { Join-Path $repoRoot 'src' }
$projects = [ordered]@{}

foreach ($file in (Get-ChildItem -Path $srcRoot -Recurse -Filter *.fsproj -File | Sort-Object FullName)) {
    $doc = Read-ProjectXml $file.FullName
    $dir = $file.Directory.FullName

    $exemptionNode = $doc.SelectSingleNode('//PropertyGroup/FablePortabilityExemption')
    $packableNode = $doc.SelectSingleNode('//PropertyGroup/IsPackable')

    $references = @(
        foreach ($node in $doc.SelectNodes('//ProjectReference[@Include]')) {
            [IO.Path]::GetFullPath((Join-Path $dir ($node.GetAttribute('Include') -replace '\\', [IO.Path]::DirectorySeparatorChar)))
        }
    )

    $projects[$file.FullName] = [pscustomobject]@{
        Path       = $file.FullName
        Relative   = ConvertTo-RepoRelative $file.FullName
        Name       = [IO.Path]::GetFileNameWithoutExtension($file.Name)
        Ships      = Test-ShipsFableSources $doc
        Packable   = -not ($packableNode -and $packableNode.InnerText.Trim() -eq 'false')
        Exemption  = if ($exemptionNode) { ($exemptionNode.InnerText.Trim() -replace '\s+', ' ') } else { '' }
        HasArm     = Test-HasConditionalArm $dir
        References = $references
    }
}

$shipping = @($projects.Values | Where-Object Ships)
$exempt = @($shipping | Where-Object { $_.Exemption })
$gated = @($shipping | Where-Object { -not $_.Exemption })
$gatedPaths = [System.Collections.Generic.HashSet[string]]::new(
    [string[]] @($gated | ForEach-Object Path), [StringComparer]::OrdinalIgnoreCase)

# Rule (a) — the roots: no OTHER gated project references them.
$referenced = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($project in $gated) {
    foreach ($reference in $project.References) {
        if ($gatedPaths.Contains($reference)) { [void] $referenced.Add($reference) }
    }
}

# Rule (a) ∪ rule (b).
$entries = @($gated | Where-Object { (-not $referenced.Contains($_.Path)) -or $_.HasArm } | Sort-Object Relative)

function Get-EntryReason {
    param($project)
    $reasons = @()
    if (-not $referenced.Contains($project.Path)) { $reasons += 'graph root' }
    if ($project.HasArm) { $reasons += 'conditional arm' }
    return ($reasons -join ' + ')
}

# What the entries actually cover, over ProjectReference within the gated set.
$covered = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$pending = New-Object System.Collections.Generic.Queue[string]
foreach ($entry in $entries) { $pending.Enqueue($entry.Path) }
while ($pending.Count -gt 0) {
    $path = $pending.Dequeue()
    if (-not $covered.Add($path)) { continue }
    foreach ($reference in $projects[$path].References) {
        if ($gatedPaths.Contains($reference)) { $pending.Enqueue($reference) }
    }
}

$uncovered = @($gated | Where-Object { -not $covered.Contains($_.Path) })
$orphanArms = @($projects.Values | Where-Object { $_.HasArm -and $_.Packable -and (-not $_.Ships) })

if ($List) {
    Write-Host "Fable portability set — derived from $($shipping.Count) package(s) shipping fable\ sources"
    Write-Host ""
    Write-Host "  entries ($($entries.Count)) — Fable-compiled directly, under their own properties:"
    foreach ($entry in $entries) {
        Write-Host ("    {0,-58} ({1})" -f $entry.Relative, (Get-EntryReason $entry))
    }
    Write-Host ""
    Write-Host "  covered: $($covered.Count) of $($gated.Count) gated — entered directly or reached by ProjectReference."
    foreach ($project in $uncovered) {
        Write-Host "    UNCOVERED $($project.Relative)" -ForegroundColor Red
    }
    if ($exempt.Count -gt 0) {
        Write-Host ""
        Write-Host "  exempt ($($exempt.Count)) — declared in the project's own fsproj:"
        foreach ($project in $exempt) {
            Write-Host "    $($project.Relative)" -ForegroundColor Yellow
            Write-Host "      $($project.Exemption)" -ForegroundColor DarkGray
        }
    }
    foreach ($project in $orphanArms) {
        Write-Host "    ORPHAN ARM $($project.Relative) — conditional arm, no fable\ sources" -ForegroundColor Red
    }
    exit 0
}

# Outside the repo tree on purpose: Fable emits a deep `fable_modules/` graph, and a deep output
# path under an already-deep worktree hits MAX_PATH, where fsc fails without a readable error.
$portabilityRoot = Join-Path ([IO.Path]::GetTempPath()) 'fuaran-fable-portability'

if (-not $SkipPortability) {
    Write-Stage "portability — $($entries.Count) entries covering $($covered.Count) of $($gated.Count) gated projects"

    foreach ($project in $exempt) {
        Write-Host "  EXEMPT $($project.Relative) — $($project.Exemption)" -ForegroundColor Yellow
    }

    # A derivation that stops seeing part of the tree is the silent gap this stage exists to close,
    # so it fails rather than quietly compiling a smaller set.
    foreach ($project in $uncovered) {
        $failures.Add("$($project.Relative) ships fable\ sources, is not exempt, and no entry reaches it")
    }

    foreach ($project in $orphanArms) {
        $failures.Add("$($project.Relative) holds a #if FABLE_COMPILER arm but packs no fable\ sources — the arm is compiled by nothing")
    }

    Remove-Item -Recurse -Force $portabilityRoot -ErrorAction SilentlyContinue

    foreach ($project in $entries) {
        $outDir = Join-Path $portabilityRoot $project.Name

        Write-Host "  fable $($project.Relative)  [$(Get-EntryReason $project)]" -ForegroundColor DarkGray

        # --noCache is mandatory: a stale .fable cache can serve a compile that no longer reflects
        # the sources, which is the one answer this stage must never give.
        dotnet fable $project.Path -o $outDir --noCache

        if ($LASTEXITCODE -ne 0) {
            $failures.Add("Fable portability compile FAILED for $($project.Relative) (exit $LASTEXITCODE)")
            Write-Host "  FAILED: $($project.Relative)" -ForegroundColor Red
        }
    }

    if (-not $KeepOutput) {
        Remove-Item -Recurse -Force $portabilityRoot -ErrorAction SilentlyContinue
    }
}

# ── 2. The laws ─────────────────────────────────────────────────────────────

$lawsOut = Join-Path $PSScriptRoot 'output'
$lineShape = '^(MERGE|MERGELAW|MERGEFINDING|MERGEFAIL|ADEQUACY|KIT|KITFAIL|DEFLATE|DEFLATEFAIL|TOTAL) '

if (-not $SkipLaws) {
    Write-Stage 'laws — TreeMerge.merge3Way + FoldConfluence.laneFoldLaws + Deflate.inflate, .NET vs Node'

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
