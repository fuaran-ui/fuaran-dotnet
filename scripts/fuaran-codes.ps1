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

  THE CODE SPACE IS SHARED BY THREE REGISTRIES, and that is what makes the
  by-eyeball mint unsafe rather than merely untidy. Each is DERIVED from its own
  sources, never listed here:

    preemit   the tree-time validator's `describe` arms
              (src/Fuaran.UI/PreEmitValidate.fs)
    walker    the build-time source-AST walker's findings
              (src/Fuaran.UI.Validator/*.fs)
    analyzer  the Roslyn diagnostic descriptors for the C#/VB surfaces
              (src/Fuaran.UI.Analyzers/**/*.cs)

  A fourth source is consulted for ALLOCATION only: the conformance corpus's
  `validator/defect-vocabulary.json`, which is a PROJECTION of `preemit` and so
  can never be a collision partner — but it can be AHEAD of this checkout, and a
  mint that ignored it would hand back a number the corpus has already published.

  And so can a SIBLING WORKTREE of this same repository: a concurrent session's
  unpushed branch is invisible to `git log` and to this tree, which is exactly
  where the FUARAN114 collision came from. Every other worktree's three
  registries are read too (`-SkipWorktrees` opts out).

.PARAMETER Next
  Print the next free code (or `-Count N` of them, contiguously). The floor is
  strictly above the maximum over EVERY source, so a mint is safe against a
  sibling worktree's unpushed allocation as well as against this tree's.

.PARAMETER Check
  Report every code claimed by more than one registry for a DIFFERENT rule, and
  exit 1. A code deliberately mirrored across registries — the same rule stated
  at two layers — is declared in `$Mirrors` below with its reason; a declaration
  that has stopped being true is reported too, on the `AuthoringSurfacePin`
  posture: the value of an enumeration is that it is complete, so an entry whose
  subject has been renumbered or retired fails here rather than outliving it.

  WHAT IT DOES NOT CHECK, stated so the green is not read as wider than it is:
  two codes WITHIN one registry. `describe` is a match over a defect DU and
  several cases legitimately share one code (the corpus vocabulary carries the
  case list per code for exactly that reason), so a within-registry repeat is
  ordinary rather than a defect, and nothing here can tell the two apart.

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
$LASTEXITCODE = 0

$repoRoot = Split-Path -Parent $PSScriptRoot

# ── Deliberate cross-registry mirrors ────────────────────────────────────────
# ONE rule stated at two layers under one code. Every entry names the registries
# that share it and why sharing is correct; anything else that shares a code is
# two rules under one name, which is the defect.
$Mirrors = [ordered]@{
    "FUARAN001" = "walker+analyzer — NodeId uniqueness, the same rule read off F# source and off C#/VB source"
    "FUARAN010" = "walker+analyzer — Binding.Query name resolution against the module manifest, likewise"
    "FUARAN047" = "walker+preemit — the Tabs header/children parity rule, stated at source-AST time and at tree time"
    "FUARAN048" = "walker+preemit — the Tabs tag/children parity rule, likewise"
    "FUARAN049" = "walker+preemit — the Tabs activeTag-without-tags rule, likewise"
}

function Get-CodesFromTree {
    <#
      .SYNOPSIS
        The three in-repo registries of one checkout, as registry -> code set.
      .DESCRIPTION
        Regexes over DECLARATION sites, not over every mention: a doc comment
        cross-referencing another rule's code must not read as a claim on it.
        `preemit` and `walker` claim a code where the source carries the bare
        string literal that `describe` / a finding returns; `analyzer` claims one
        at a descriptor's `id:` argument.
    #>
    param([string] $root)

    $result = [ordered]@{ preemit = @(); walker = @(); analyzer = @() }

    $preemitFile = Join-Path $root "src/Fuaran.UI/PreEmitValidate.fs"
    if (Test-Path $preemitFile) {
        $result.preemit = @(
            Select-String -Path $preemitFile -Pattern '^\s*"(FUARAN\d{3})",\s*$' -AllMatches
            | ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups[1].Value }
        ) | Sort-Object -Unique
    }

    $walkerDir = Join-Path $root "src/Fuaran.UI.Validator"
    if (Test-Path $walkerDir) {
        $result.walker = @(
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
            $LASTEXITCODE = 0
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
        $wtCodes = Get-CodesFromTree $wt
        $flat = @($wtCodes.preemit) + @($wtCodes.walker) + @($wtCodes.analyzer)
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
$allForFloor = @($local.preemit) + @($local.walker) + @($local.analyzer) + @($siblingCodes)
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

if ($List) {
    $claims = @{}
    foreach ($registry in @("preemit", "walker", "analyzer")) {
        foreach ($c in $local.$registry) {
            if (-not $claims.ContainsKey($c)) { $claims[$c] = @() }
            $claims[$c] += $registry
        }
    }
    foreach ($c in ($claims.Keys | Sort-Object)) {
        "{0}  {1}" -f $c, ($claims[$c] -join ", ")
    }
    exit 0
}

if ($Next) {
    if ($Count -lt 1) { Write-Error "-Count must be at least 1."; exit 1 }

    Write-Host "highest allocated: FUARAN$($max.ToString('000'))" -ForegroundColor DarkGray
    Write-Host ("  sources: preemit {0} · walker {1} · analyzer {2} · corpus {3} · sibling worktrees {4}" -f `
            $local.preemit.Count, $local.walker.Count, $local.analyzer.Count,
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
$claimedBy = @{}
foreach ($registry in @("preemit", "walker", "analyzer")) {
    foreach ($c in $local.$registry) {
        if (-not $claimedBy.ContainsKey($c)) { $claimedBy[$c] = @() }
        $claimedBy[$c] += $registry
    }
}

$shared = @($claimedBy.Keys | Where-Object { $claimedBy[$_].Count -gt 1 } | Sort-Object)
$undeclared = @($shared | Where-Object { -not $Mirrors.Contains($_) })
$orphaned = @($Mirrors.Keys | Where-Object { $shared -notcontains $_ })

$failed = $false

if ($undeclared.Count -gt 0) {
    $failed = $true
    Write-Host "FUARAN code collision — one code, two registries, and nothing says they are one rule:" -ForegroundColor Red
    foreach ($c in $undeclared) {
        Write-Host ("  {0}  claimed by {1}" -f $c, ($claimedBy[$c] -join " + ")) -ForegroundColor Red
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

# The vacuity guard. Every assertion above is a "no undeclared collision" claim,
# which a run that read no source at all satisfies perfectly.
if ($local.preemit.Count -lt 50 -or $local.walker.Count -lt 20 -or $local.analyzer.Count -lt 3) {
    $failed = $true
    Write-Host ("Refusing to report clean: read {0} preemit / {1} walker / {2} analyzer codes — too few to be the real registries." -f `
            $local.preemit.Count, $local.walker.Count, $local.analyzer.Count) -ForegroundColor Red
}

if ($failed) { exit 1 }

Write-Host ("FUARAN codes OK — {0} preemit / {1} walker / {2} analyzer, {3} declared mirror(s), highest FUARAN{4}." -f `
        $local.preemit.Count, $local.walker.Count, $local.analyzer.Count, $Mirrors.Count, $max.ToString("000"))
exit 0
