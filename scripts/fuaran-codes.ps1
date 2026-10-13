#Requires -Version 7.0
<#
.SYNOPSIS
  Allocate the next free `FUARAN*` defect code, and fail on a collision.

.DESCRIPTION
  Phase 1646. Defect codes used to be minted by reading the highest number a
  session happened to have open, which is the by-eyeball allocation the roadmap
  engine's own `next-id` replaced for phase ids — and it failed the same way:
  two phases minted FUARAN114 in one evening, one landed, the other renumbered
  its pair at fourteen sites. A pre-existing instance of the same class is the
  one this script was written to close, where the VB analyzer and the F# source
  walker each minted FUARAN060 and FUARAN061 for different defects, so one code
  named two things depending on which tool reported it.

  THE CODE SPACE IS SHARED BY THREE FAMILIES, and that is what makes the
  by-eyeball mint unsafe rather than merely untidy. Since Phase 2052 the two F#
  families are ONE registry, in one file, and this script reads that registry
  rather than every check site:

    preemit   the tree-time validator's code table, `DefectCodes.table`
              (src/Fuaran.UI/PreEmitValidate.fs). One code may span several
              rows — a code with more than one message shape — so a repeat
              there is ordinary. FUARAN068's literal is
              `CustomRegistry.propDefectCode` (src/Fuaran.UI/CustomRegistry.fs),
              which the table row reads: that module compiles first.
    findings  every other F# code, `FindingCodes` in the same file: the
              build-time walker's, the dead-on-decode lint's, the query-binding
              check's and the reserved ones. One entry per code, so a repeat
              there IS a collision.
    analyzer  the Roslyn diagnostic descriptors for the C#/VB surfaces
              (src/Fuaran.UI.Analyzers/**/*.cs), a C# assembly that cannot read
              the F# registry.

  `FindingCodes.retired` lists codes still accepted for one release (FUARAN001,
  the walker's duplicate-NodeId code until both validators took FUARAN-DUP-ID).
  A retired code still holds its number: it is in the allocation floor and a
  collision partner, so it is never handed out again while anything names it.

  A fourth source is consulted for ALLOCATION only: the conformance corpus's
  `validator/defect-vocabulary.json`, which is a PROJECTION of `preemit` and so
  can never be a collision partner — but it can be AHEAD of this checkout, and a
  mint that ignored it would hand back a number the corpus has already published.

  And so can a SIBLING WORKTREE of this same repository: a concurrent session's
  unpushed branch is invisible to `git log` and to this tree, which is exactly
  where the FUARAN114 collision came from. Every other worktree's registries
  are read too (`-SkipWorktrees` opts out) — including a branch cut before the
  registry existed, whose walker codes are still literals at the check sites.

.PARAMETER Next
  Print the next free code (or `-Count N` of them, contiguously). The floor is
  strictly above the maximum over EVERY source, so a mint is safe against a
  sibling worktree's unpushed allocation as well as against this tree's.

.PARAMETER Check
  Report, and exit 1 on:
    - a code claimed by more than one registry for a DIFFERENT rule. A code
      deliberately mirrored across registries — the same rule stated at two
      layers — is declared in `$Mirrors` below with its reason; a declaration
      that has stopped being true is reported too, on the `AuthoringSurfacePin`
      posture: the value of an enumeration is that it is complete, so an entry
      whose subject has been renumbered or retired fails here rather than
      outliving it;
    - a code with two `findings` entries;
    - a quoted `FUARAN*` literal in an F# source outside the registry (Phase
      2052): a check names its code by referring to a registry entry, so a
      literal anywhere else is a code the registry does not hold.

  WHAT IT DOES NOT CHECK, stated so the green is not read as wider than it is:
  two rows of `DefectCodes.table` under one code. Several defect cases
  legitimately share one code (the corpus vocabulary carries the case list per
  code for exactly that reason), so nothing here can tell that from a defect.
  Test sources are not scanned for literals: a test asserting a code by its
  string is asserting the published contract.

.PARAMETER List
  Print every code with the registries that claim it, in numeric order.

.EXAMPLE
  pwsh ./scripts/fuaran-codes.ps1 -Next
  pwsh ./scripts/fuaran-codes.ps1 -Next -Count 2
  pwsh ./scripts/fuaran-codes.ps1 -Check
#>
[CmdletBinding()]
param(
    [switch] $Next,
    [switch] $Check,
    [switch] $List,
    [int] $Count = 1,
    [string] $CorpusRoot,
    [switch] $SkipWorktrees
)

$ErrorActionPreference = "Stop"
# Seeded because a stage that never runs a native command otherwise compares
# `$null` and takes the success branch — the estate-wide launcher trap.
$global:LASTEXITCODE = 0

$repoRoot = Split-Path -Parent $PSScriptRoot

$registryRel = "src/Fuaran.UI/PreEmitValidate.fs"
$customRegistryRel = "src/Fuaran.UI/CustomRegistry.fs"

# ── Deliberate cross-registry mirrors ────────────────────────────────────────
# ONE rule stated at two layers under one code. Every entry names the registries
# that share it and why sharing is correct; anything else that shares a code is
# two rules under one name, which is the defect.
$Mirrors = [ordered]@{
    "FUARAN001" = "retired+analyzer — NodeId uniqueness. The F# validators report it as FUARAN-DUP-ID since Phase 2052 and accept FUARAN001 for one release; the Roslyn analyzers keep FUARAN001 because a hyphenated id cannot be named in a #pragma"
    "FUARAN010" = "findings+analyzer — Binding.Query name resolution against the module manifest, read off F# source and off C#/VB source"
}

function Get-CodesFromTree {
    <#
      .SYNOPSIS
        The registries of one checkout, as registry -> code list.
      .DESCRIPTION
        Regexes over DECLARATION sites, not over every mention: a doc comment
        cross-referencing another rule's code must not read as a claim on it.
        `preemit` claims a code where a table row carries it on its own line,
        `findings` where a `FindingCodes` entry is declared, `retired` where
        `FindingCodes.retired` names it, `analyzer` at a descriptor's `id:`.
        `legacy` is the pre-registry walker shape (a literal at a check site),
        read so a sibling branch cut before Phase 2052 still raises the floor.
        `findings` is NOT de-duplicated: a repeat there is what -Check reports.
    #>
    param([string] $root)

    $result = [ordered]@{ preemit = @(); findings = @(); retired = @(); analyzer = @(); legacy = @() }

    $registryFile = Join-Path $root $registryRel
    if (Test-Path $registryFile) {
        $text = Get-Content -Raw -Path $registryFile
        $result.preemit = @(
            [regex]::Matches($text, '(?m)^\s*"(FUARAN\d{3})",\s*$') | ForEach-Object { $_.Groups[1].Value }
        ) | Sort-Object -Unique
        $result.findings = @(
            # `\s+` spans a line break: Fantomas wraps an entry whose line runs long.
            [regex]::Matches($text, '(?m)^\s*let \w+ =\s+(?:walker|code FindingFamily\.\w+) Severity\.\w+ "(FUARAN\d{3})"\s*$') |
                ForEach-Object { $_.Groups[1].Value }
        )
        $result.retired = @(
            [regex]::Matches($text, '(?m)^\s*let retired: .*$') |
                ForEach-Object { [regex]::Matches($_.Value, '"(FUARAN\d{3})"') } | ForEach-Object { $_.Groups[1].Value }
        ) | Sort-Object -Unique
    }

    $customFile = Join-Path $root $customRegistryRel
    if (Test-Path $customFile) {
        $custom = @(
            Select-String -Path $customFile -Pattern 'let propDefectCode = "(FUARAN\d{3})"' |
                ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups[1].Value }
        )
        $result.preemit = @($result.preemit) + $custom | Sort-Object -Unique
    }

    $walkerDir = Join-Path $root "src/Fuaran.UI.Validator"
    if (Test-Path $walkerDir) {
        $result.legacy = @(
            Get-ChildItem -Path $walkerDir -Filter *.fs -File
            | Select-String -Pattern '"(FUARAN\d{3})"' -AllMatches
            | ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups[1].Value }
        ) | Sort-Object -Unique
    }

    $analyzerDir = Join-Path $root "src/Fuaran.UI.Analyzers"
    if (Test-Path $analyzerDir) {
        $result.analyzer = @(
            Get-ChildItem -Path $analyzerDir -Filter *.cs -File -Recurse
            | Select-String -Pattern 'id:\s*"(FUARAN\d{3})"' -AllMatches
            | ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups[1].Value }
        ) | Sort-Object -Unique
    }

    return $result
}

function Get-StrayLiterals {
    <#
      .SYNOPSIS
        Every quoted `FUARAN*` literal in a non-test F# source outside the
        registry, as "path:line: code".
      .DESCRIPTION
        Comment lines are skipped (a doc comment citing a code is not a claim on
        it), and so are test projects. CustomRegistry's one literal is the
        registry's own entry, read through `CustomRegistry.propDefectCode`.
    #>
    param([string] $root)

    $src = Join-Path $root "src"
    $allowed = @(
        (Join-Path $root $registryRel),
        (Join-Path $root $customRegistryRel)
    ) | ForEach-Object { [IO.Path]::GetFullPath($_) }

    return @(
        Get-ChildItem -Path $src -Filter *.fs -File -Recurse
        | Where-Object { $_.FullName -notmatch '[\\/][^\\/]*Tests?[\\/]' -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
        | Where-Object { $allowed -notcontains [IO.Path]::GetFullPath($_.FullName) }
        | Select-String -CaseSensitive -Pattern '"FUARAN(\d+|-[A-Z-]+)"'
        | Where-Object { $_.Line -notmatch '^\s*//' }
        | ForEach-Object {
            "{0}:{1}: {2}" -f [IO.Path]::GetRelativePath($root, $_.Path), $_.LineNumber, $_.Matches[0].Value
        }
    )
}

function Get-CorpusCodes {
    <#
      .SYNOPSIS
        The conformance corpus's published pre-emit vocabulary, for the
        ALLOCATION floor only.
      .DESCRIPTION
        Absent corpus is reported by name rather than passing quietly — the
        `CssCheck` posture: "nothing to read here" must not read as "the floor is
        complete". It is not fatal, because a single-repo checkout is a
        legitimate place to run `-Check`.
    #>
    param([string] $root)

    $path = Join-Path $root "validator/defect-vocabulary.json"
    if (-not (Test-Path $path)) {
        return $null
    }

    $doc = Get-Content -Raw -Path $path | ConvertFrom-Json
    return @($doc.codes | ForEach-Object { $_.code } | Where-Object { $_ -match '^FUARAN\d{3}$' }) | Sort-Object -Unique
}

function Get-SiblingWorktrees {
    <#
      .SYNOPSIS
        Every OTHER worktree of this repository, as a path list.
      .DESCRIPTION
        A concurrent session's unpushed branch is invisible to this tree and to
        `git log`, and it is the source the FUARAN114 collision actually came
        from. Degrades to an empty list with one warning where git cannot answer
        — an allocator that refused to run outside a git checkout would be
        useless in exactly the CI shapes that most want a stable answer.
    #>
    param([string] $root)

    try {
        $lines = & git -C $root worktree list --porcelain 2>$null
        if ($LASTEXITCODE -ne 0) {
            $global:LASTEXITCODE = 0
            Write-Host "  (sibling worktrees NOT consulted — 'git worktree list' failed here)" -ForegroundColor Yellow
            return @()
        }
    }
    catch {
        Write-Host "  (sibling worktrees NOT consulted — git is not available here)" -ForegroundColor Yellow
        return @()
    }

    $me = (Resolve-Path $root).Path.TrimEnd([IO.Path]::DirectorySeparatorChar)
    return @(
        $lines
        | Where-Object { $_ -like "worktree *" }
        | ForEach-Object { $_.Substring("worktree ".Length) }
        | Where-Object { Test-Path $_ }
        | Where-Object { (Resolve-Path $_).Path.TrimEnd([IO.Path]::DirectorySeparatorChar) -ne $me }
    )
}

function Get-Flat {
    param($codes)
    return @($codes.preemit) + @($codes.findings) + @($codes.retired) + @($codes.analyzer) + @($codes.legacy)
}

# ── Gather ───────────────────────────────────────────────────────────────────
$local = Get-CodesFromTree $repoRoot

if (-not $CorpusRoot) {
    # Phase 1647's one corpus-root contract. A second variable for one thing is
    # the divergence that work exists to end, so this reader honours the same
    # name every other reader in the repo does.
    $CorpusRoot = if ($env:FUARAN_WIRE_FIXTURES) { $env:FUARAN_WIRE_FIXTURES.Trim() }
    else { Join-Path $repoRoot "../wire-format-fixtures" }
}

$corpusCodes = Get-CorpusCodes $CorpusRoot

$siblingCodes = @()
$siblingSources = @()
if (-not $SkipWorktrees) {
    foreach ($wt in (Get-SiblingWorktrees $repoRoot)) {
        $flat = @(Get-Flat (Get-CodesFromTree $wt))
        if ($flat.Count -gt 0) {
            $siblingCodes += $flat
            $siblingSources += $wt
        }
    }
}

function Get-Ordinal {
    param([string] $code)
    return [int]$code.Substring("FUARAN".Length)
}

# ── -List / -Next: the allocation floor ──────────────────────────────────────
# Strictly above the maximum over EVERY source. The reserved high band (900+,
# the walker's own tooling codes) is excluded from the floor: it is a separate
# range, and folding it in would push every future mint to 901.
$reservedFloor = 900
$allForFloor = @(Get-Flat $local) + @($siblingCodes)
if ($null -ne $corpusCodes) { $allForFloor += $corpusCodes }

$ordinals = @(
    $allForFloor
    | Sort-Object -Unique
    | ForEach-Object { Get-Ordinal $_ }
    | Where-Object { $_ -lt $reservedFloor }
)

if ($ordinals.Count -eq 0) {
    Write-Error "No FUARAN codes found under $repoRoot — the registries could not be read, so no allocation is safe."
    exit 1
}

$max = ($ordinals | Measure-Object -Maximum).Maximum

# Each code in this tree, with the registries that claim it (a code repeated
# within `findings` appears once per entry, which is what -Check reports).
$claimedBy = @{}
foreach ($registry in @("preemit", "findings", "retired", "analyzer")) {
    foreach ($c in @($local.$registry)) {
        if (-not $claimedBy.ContainsKey($c)) { $claimedBy[$c] = @() }
        $claimedBy[$c] += $registry
    }
}

if ($List) {
    foreach ($c in ($claimedBy.Keys | Sort-Object)) {
        "{0}  {1}" -f $c, ($claimedBy[$c] -join ", ")
    }
    exit 0
}

if ($Next) {
    if ($Count -lt 1) { Write-Error "-Count must be at least 1."; exit 1 }

    Write-Host "highest allocated: FUARAN$($max.ToString('000'))" -ForegroundColor DarkGray
    Write-Host ("  sources: preemit {0} · findings {1} · retired {2} · analyzer {3} · corpus {4} · sibling worktrees {5}" -f `
            @($local.preemit).Count, @($local.findings).Count, @($local.retired).Count, @($local.analyzer).Count,
        $(if ($null -eq $corpusCodes) { "NOT READ (absent at $CorpusRoot)" } else { "$($corpusCodes.Count)" }),
        $(if ($siblingSources.Count -eq 0) { "none" } else { $siblingSources -join "; " })) -ForegroundColor DarkGray

    for ($i = 1; $i -le $Count; $i++) {
        "FUARAN{0:000}" -f ($max + $i)
    }
    exit 0
}

if (-not $Check) {
    Write-Error "Nothing to do — pass -Next, -Check or -List."
    exit 1
}

# ── -Check: the collision report ─────────────────────────────────────────────
$failed = $false

$repeated = @($claimedBy.Keys | Where-Object { @($claimedBy[$_] | Where-Object { $_ -eq "findings" }).Count -gt 1 } | Sort-Object)
if ($repeated.Count -gt 0) {
    $failed = $true
    Write-Host "FUARAN code declared twice in FindingCodes — one code, two rules:" -ForegroundColor Red
    foreach ($c in $repeated) { Write-Host "  $c" -ForegroundColor Red }
}

$shared = @($claimedBy.Keys | Where-Object { @($claimedBy[$_] | Sort-Object -Unique).Count -gt 1 } | Sort-Object)
$undeclared = @($shared | Where-Object { -not $Mirrors.Contains($_) })
$orphaned = @($Mirrors.Keys | Where-Object { $shared -notcontains $_ })

if ($undeclared.Count -gt 0) {
    $failed = $true
    Write-Host "FUARAN code collision — one code, two registries, and nothing says they are one rule:" -ForegroundColor Red
    foreach ($c in $undeclared) {
        Write-Host ("  {0}  claimed by {1}" -f $c, (($claimedBy[$c] | Sort-Object -Unique) -join " + ")) -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "  Renumber the later claimant with: pwsh ./scripts/fuaran-codes.ps1 -Next" -ForegroundColor Yellow
    Write-Host "  — or, if the two really are ONE rule stated at two layers, declare it in" -ForegroundColor Yellow
    Write-Host '    scripts/fuaran-codes.ps1''s $Mirrors table with the reason.' -ForegroundColor Yellow
}

if ($orphaned.Count -gt 0) {
    $failed = $true
    Write-Host "Stale mirror declaration — these codes no longer share a registry:" -ForegroundColor Red
    foreach ($c in $orphaned) {
        Write-Host ("  {0}  declared as: {1}" -f $c, $Mirrors[$c]) -ForegroundColor Red
    }
    Write-Host '  Remove the entry from $Mirrors — a declaration nothing checks is worse than none.' -ForegroundColor Yellow
}

$stray = @(Get-StrayLiterals $repoRoot)
if ($stray.Count -gt 0) {
    $failed = $true
    Write-Host "FUARAN literal outside the registry — name the code through a FindingCodes entry (or a DefectCodes row):" -ForegroundColor Red
    foreach ($s in $stray) { Write-Host "  $s" -ForegroundColor Red }
}

# The vacuity guard. Every assertion above is a "no undeclared collision" claim,
# which a run that read no source at all satisfies perfectly.
if (@($local.preemit).Count -lt 50 -or @($local.findings).Count -lt 20 -or @($local.analyzer).Count -lt 3) {
    $failed = $true
    Write-Host ("Refusing to report clean: read {0} preemit / {1} findings / {2} analyzer codes — too few to be the real registries." -f `
            @($local.preemit).Count, @($local.findings).Count, @($local.analyzer).Count) -ForegroundColor Red
}

if ($failed) { exit 1 }

Write-Host ("FUARAN codes OK — {0} preemit / {1} findings / {2} retired / {3} analyzer, {4} declared mirror(s), no literal outside the registry, highest FUARAN{5}." -f `
        @($local.preemit).Count, @($local.findings).Count, @($local.retired).Count, @($local.analyzer).Count, $Mirrors.Count, $max.ToString("000"))
exit 0
