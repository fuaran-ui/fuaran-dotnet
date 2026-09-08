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

  THE CONTENT-ADDRESSED SKIP (Phase 1619). The portability stage above is ~65% of this repo's
  gate: twelve `--noCache` compiles, four to five minutes, and on an unchanged tree every one of
  them recomputes an answer nothing has invalidated. So each compile is keyed on a CONTENT ADDRESS,
  and in a NARROW LANE (`run.ps1 -Lane pure|fast`, read here from `FUARAN_TEST_LANE`) a compile
  whose address matches its last recorded green is SKIPPED BY ADDRESS — named, with the address
  printed. THE FULL LANE ALWAYS COMPILES: it writes the address and never reads one, so the skip is
  structurally unreachable on the lane a release cites.

  Nothing here trusts Fable's own cache — `--noCache` is untouched, and the rule that a stale cache
  must never serve a compile is not weakened but made unreachable, because a byte that changes the
  compile changes the address. What the address covers:

    * every `.fs` / `.fsi` and every `.fsproj` in the entry's TRANSITIVE `ProjectReference` closure,
      by content hash — the whole source graph Fable actually reads, not just the entry's own files;
    * every `<Compile Include>` resolving OUTSIDE its project's directory (a linked file), which a
      directory walk would otherwise miss;
    * the MSBuild files GOVERNING each project in that closure — the nearest `Directory.Build.props`
      / `.targets`, `Directory.Packages.props`, `nuget.config`, `global.json` — so a property or a
      package version moving one directory up still moves the address;
    * the Fable tool version from the nearest `.config/dotnet-tools.json`;
    * the entry's own Fable-relevant properties (`Nullable`, `LangVersion`, `DefineConstants`,
      `TargetFramework(s)`), which under Fable govern the WHOLE transpiled graph;
    * the exact compile the record stands for, as a string, so a green recorded under one command
      can never be honoured for a different one;
    * `$addressSchema` — bump it and every recorded green misses, which is the correct direction
      whenever WHAT the address covers changes.

  THE SAFETY DIRECTION IS ALWAYS "COMPILE". An unresolvable reference, an unreadable project, a
  missing tool manifest, an unrecognised lane, an absent or unparseable record: every one of them
  yields NO ADDRESS and therefore a compile. A skip needs a positive match on all of it. A record is
  written only from a GREEN compile, and a RED one deletes the record for that subject outright, so
  the next narrow run recompiles even if the tree is later restored to a state that once passed.

  Records live under the system temp root beside the portability output — outside the repo tree for
  the MAX_PATH reason below, and never committed. They are keyed by the resolved source root, so a
  second worktree and the self-test's scratch tree each keep their own.

  `-Addresses` prints the derived address per compile and exits, the way `-List` prints the derived
  set. `-ProveAddressing` runs the go-red proof for the address function itself over a scratch tree
  of plain files — no compiling, milliseconds — and it runs AUTOMATICALLY at the head of any run
  that could skip. It perturbs real inputs on disk rather than the manifest the hash is taken over:
  editing that manifest would prove only that SHA-256 is sensitive to its input, which is true of
  every hash and says nothing about whether this address reads the tree. The remaining proof — that
  the FULL lane compiles with a matching record present — needs a real end-to-end run and lives in
  `fable-check.tests.ps1` beside this file, with the rest of the derivation's go-red proof.

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
    [string] $SrcRoot,
    # Print the content address of every compile this stage would perform — the entries and the law
    # harness — then exit without compiling anything. The `-List` posture, one question along.
    [switch] $Addresses,
    # Run the go-red proof for the ADDRESS FUNCTION over a scratch tree of plain files and exit. It
    # compiles nothing and costs milliseconds, which is why the same proof also runs automatically
    # at the head of any run that could skip a compile.
    [switch] $ProveAddressing
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

# ── The content address (Phase 1619) ────────────────────────────────────────
#
# See "THE CONTENT-ADDRESSED SKIP" in the header for what the address covers and why. Everything in
# this section is READ-ONLY over the tree; the only thing it writes is one small JSON record per
# compile, under the system temp root.

# WHAT the address covers, versioned. Bump this whenever that set changes and every recorded green
# misses on the next run — which is the correct direction: a record minted under an older, narrower
# notion of "the inputs" is precisely the stale green this mechanism exists to make impossible.
$addressSchema = 'fable-address/1'

# The lane arrives through the environment `run.ps1` already sets for the test stage, so the stage
# needs no parameter of its own and cannot be handed a lane the rest of the gate did not run under.
$lane = if ($env:FUARAN_TEST_LANE) { $env:FUARAN_TEST_LANE.Trim().ToLowerInvariant() } else { 'full' }

# ONLY the two narrow lanes may consult a record. `full`, an unset variable and a lane name this
# script has never heard of all compile. That the unknown case resolves to "compile" rather than to
# "skip" is the whole safety posture in one line.
$laneMaySkip = $lane -in @('pure', 'fast')

# Keyed by the resolved source root: a second worktree of this repo, and the scratch tree
# `fable-check.tests.ps1` points `-SrcRoot` at, each keep their own records rather than answering
# for one another.
$addressRoot = $null

function Get-TextSha256 {
    param([string] $text)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text))) -replace '-', '').ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

$fileHashCache = @{}
$governingCache = @{}
$toolVersionCache = @{}
$projectXmlCache = @{}
$projectInputCache = @{}

function Clear-AddressCaches {
    # The caches assume the tree does not move under them, which is true of a gate run and false of
    # the proof below, which perturbs files on purpose.
    $script:fileHashCache = @{}
    $script:governingCache = @{}
    $script:toolVersionCache = @{}
    $script:projectXmlCache = @{}
    $script:projectInputCache = @{}
}

function Get-CachedFileSha256 {
    param([string] $path)
    if ($fileHashCache.ContainsKey($path)) { return $fileHashCache[$path] }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    $fileHashCache[$path] = $hash
    return $hash
}

# The MSBuild files that govern a project without living beside it. `Nullable` and `LangVersion` are
# set in this repo's root `Directory.Build.props`, not in any fsproj, so an address blind to them
# would be blind to the two properties the portability stage exists to vary.
$governingFileNames = @('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'nuget.config', 'global.json')

function Get-NearestFile {
    # The first file of that name at or above $startDir — MSBuild's own resolution order for the
    # names above, and the same walk `dotnet` performs for a tool manifest.
    param([string] $startDir, [string] $name)
    $dir = $startDir
    while ($dir) {
        $candidate = Join-Path $dir $name
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return (Resolve-Path -LiteralPath $candidate).Path }
        $parent = Split-Path -Parent $dir
        if (-not $parent -or $parent -eq $dir) { return $null }
        $dir = $parent
    }
    return $null
}

function Get-GoverningFiles {
    param([string] $projectDir)
    if ($governingCache.ContainsKey($projectDir)) { return $governingCache[$projectDir] }
    $found = @(foreach ($name in $governingFileNames) {
            $path = Get-NearestFile $projectDir $name
            if ($path) { $path }
        })
    $governingCache[$projectDir] = $found
    return $found
}

function Get-FableToolVersion {
    # Resolved from the entry's own directory upward, so the proof's scratch tree can carry its own
    # manifest and a bump there is a real bump rather than a simulated one. `$null` on anything
    # unreadable, which propagates to "no address" and therefore to "compile".
    param([string] $startDir)
    if ($toolVersionCache.ContainsKey($startDir)) { return $toolVersionCache[$startDir] }
    $version = $null
    $manifest = Get-NearestFile $startDir (Join-Path '.config' 'dotnet-tools.json')
    if ($manifest) {
        try {
            $parsed = (Get-Content -Raw -LiteralPath $manifest | ConvertFrom-Json).tools.fable.version
            if (-not [string]::IsNullOrWhiteSpace($parsed)) { $version = [string] $parsed }
        }
        catch { $version = $null }
    }
    $toolVersionCache[$startDir] = $version
    return $version
}

function Get-CachedProjectXml {
    param([string] $path)
    if ($projectXmlCache.ContainsKey($path)) { return $projectXmlCache[$path] }
    $doc = $null
    try { $doc = Read-ProjectXml $path } catch { $doc = $null }
    $projectXmlCache[$path] = $doc
    return $doc
}

function Get-ProjectInputs {
    <#
      One project's contribution to an address: the files whose bytes can change what Fable emits,
      and the projects it references. `Ok = $false` means something could not be resolved — a
      missing project, an unreadable fsproj, a `<Compile Include>` naming a file that is not there —
      and every such case ends as "no address", so the compile happens.

      The `.fs` set is taken by walking the project's directory rather than by reading `<Compile>`,
      which is deliberately a SUPERSET: a file the project does not compile can only cause a
      spurious MISS, never a false hit. Linked `<Compile>` items pointing outside the directory are
      added on top, because for those the walk is a subset and the error would run the other way.
    #>
    param([string] $projectPath)

    if ($projectInputCache.ContainsKey($projectPath)) { return $projectInputCache[$projectPath] }

    $result = [pscustomobject]@{ Ok = $false; Files = @(); References = @() }

    if (Test-Path -LiteralPath $projectPath -PathType Leaf) {
        $doc = Get-CachedProjectXml $projectPath
        if ($doc) {
            $dir = (Resolve-Path -LiteralPath (Split-Path -Parent $projectPath)).Path
            $files = New-Object System.Collections.Generic.List[string]
            $files.Add((Resolve-Path -LiteralPath $projectPath).Path)

            foreach ($governing in (Get-GoverningFiles $dir)) { $files.Add($governing) }

            foreach ($source in (Get-ChildItem -LiteralPath $dir -Recurse -File -ErrorAction SilentlyContinue |
                    Where-Object { ($_.Extension -eq '.fs' -or $_.Extension -eq '.fsi') -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })) {
                $files.Add($source.FullName)
            }

            $ok = $true
            $references = New-Object System.Collections.Generic.List[string]

            foreach ($node in $doc.SelectNodes('//ProjectReference[@Include]')) {
                $reference = [IO.Path]::GetFullPath((Join-Path $dir ($node.GetAttribute('Include') -replace '\\', [IO.Path]::DirectorySeparatorChar)))
                if (-not (Test-Path -LiteralPath $reference -PathType Leaf)) { $ok = $false; break }
                $references.Add($reference)
            }

            if ($ok) {
                foreach ($node in $doc.SelectNodes('//Compile[@Include]')) {
                    foreach ($pattern in ($node.GetAttribute('Include') -split ';')) {
                        $trimmed = $pattern.Trim()
                        if (-not $trimmed) { continue }
                        $full = [IO.Path]::GetFullPath((Join-Path $dir ($trimmed -replace '\\', [IO.Path]::DirectorySeparatorChar)))
                        # Inside the project directory the walk above already has it.
                        if ($full.StartsWith($dir + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { continue }
                        if ($trimmed -match '[*?]') {
                            foreach ($match in (Get-ChildItem -Path $full -File -ErrorAction SilentlyContinue)) { $files.Add($match.FullName) }
                        }
                        elseif (Test-Path -LiteralPath $full -PathType Leaf) { $files.Add((Resolve-Path -LiteralPath $full).Path) }
                        else { $ok = $false; break }
                    }
                    if (-not $ok) { break }
                }
            }

            if ($ok) {
                $result = [pscustomobject]@{ Ok = $true; Files = @($files); References = @($references) }
            }
        }
    }

    $projectInputCache[$projectPath] = $result
    return $result
}

# Under Fable the ENTRY project's properties govern the whole transpiled graph, so these four (five,
# counting the multi-targeting spelling) are read from the entry itself and named in the address.
#
# BE CLEAR ABOUT WHAT THIS ADDS, because it is easy to overstate: for a property declared in a file
# the address already hashes — the entry's own fsproj, or a `Directory.Build.props` above it — this
# component is REDUNDANT, and measurably so: emptying this list leaves the proof's Nullable
# assertion passing, carried entirely by the fsproj's content hash. It is kept because redundancy
# runs in the safe direction and because naming these four makes the manifest say what the address
# is FOR, not because it widens what the address can see. Nothing here reads an environment-set or
# command-line-set property, and a compile whose properties arrive that way is outside what any of
# this can address.
$fableRelevantProperties = @('Nullable', 'LangVersion', 'DefineConstants', 'TargetFramework', 'TargetFrameworks')

function Get-CompileAddress {
    <#
      The content address of one compile, or `$null` when any input could not be resolved. `$null`
      never means "unchanged"; it means "compile", and every caller reads it that way.
    #>
    param([string] $entryPath, [string] $semantics)

    if (-not (Test-Path -LiteralPath $entryPath -PathType Leaf)) { return $null }

    $entryPath = (Resolve-Path -LiteralPath $entryPath).Path
    $entryDir = Split-Path -Parent $entryPath

    $toolVersion = Get-FableToolVersion $entryDir
    if (-not $toolVersion) { return $null }

    $entryDoc = Get-CachedProjectXml $entryPath
    if (-not $entryDoc) { return $null }

    # The transitive closure over ProjectReference — the graph Fable reads, which is wider than the
    # gated set the derivation above selects over.
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $pending = New-Object System.Collections.Generic.Queue[string]
    $pending.Enqueue($entryPath)
    $files = New-Object System.Collections.Generic.List[string]

    while ($pending.Count -gt 0) {
        $path = $pending.Dequeue()
        if (-not $seen.Add($path)) { continue }
        $inputs = Get-ProjectInputs $path
        if (-not $inputs.Ok) { return $null }
        foreach ($file in $inputs.Files) { $files.Add($file) }
        foreach ($reference in $inputs.References) { $pending.Enqueue($reference) }
    }

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("schema $addressSchema")
    $lines.Add("entry $(ConvertTo-RepoRelative $entryPath)")
    $lines.Add("semantics $semantics")
    $lines.Add("tool fable=$toolVersion")

    foreach ($name in $fableRelevantProperties) {
        $values = @(foreach ($node in $entryDoc.SelectNodes("//PropertyGroup/$name")) { $node.InnerText.Trim() })
        $lines.Add("prop $name=$($values -join '|')")
    }

    $unique = @($files | Sort-Object -Unique)
    foreach ($file in ($unique | Sort-Object { ConvertTo-RepoRelative $_ })) {
        $lines.Add("file $(Get-CachedFileSha256 $file) $(ConvertTo-RepoRelative $file)")
    }

    return Get-TextSha256 ($lines -join "`n")
}

# ── The record ──────────────────────────────────────────────────────────────

function Get-RecordPath {
    param([string] $subject)
    if (-not $addressRoot) {
        $key = (Get-TextSha256 $srcRoot.ToLowerInvariant()).Substring(0, 16)
        $script:addressRoot = Join-Path ([IO.Path]::GetTempPath()) (Join-Path 'fuaran-fable-addresses' $key)
    }
    return Join-Path $addressRoot ((($subject -replace '[^A-Za-z0-9._-]', '_')) + '.json')
}

function Test-RecordedGreen {
    <#
      True only on a positive match of every field: the schema this binary writes, the address, and
      the exact compile the record stands for. An absent, unreadable or stale-schema record is a
      miss, never an error.
    #>
    param([string] $subject, [string] $address, [string] $semantics)
    if (-not $address) { return $null }
    $path = Get-RecordPath $subject
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    try { $record = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json } catch { return $null }
    if ($record.schema -ne $addressSchema) { return $null }
    if ($record.address -cne $address) { return $null }
    if ($record.semantics -cne $semantics) { return $null }
    return $record
}

function Write-RecordedGreen {
    param([string] $subject, [string] $address, [string] $semantics)
    if (-not $address) { return }
    $path = Get-RecordPath $subject
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
    ([pscustomobject]@{
            schema      = $addressSchema
            subject     = $subject
            address     = $address
            semantics   = $semantics
            lane        = $lane
            recordedUtc = [DateTime]::UtcNow.ToString('o')
        } | ConvertTo-Json) | Set-Content -LiteralPath $path -Encoding utf8NoBOM
}

function Clear-RecordedGreen {
    # A red compile deletes the record outright rather than merely failing to write one, so a tree
    # later restored to a state that once passed still recompiles. Strictly more conservative than
    # soundness requires, and deliberately so.
    param([string] $subject)
    Remove-Item -LiteralPath (Get-RecordPath $subject) -Force -ErrorAction SilentlyContinue
}

# The exact compile each subject's record stands for. A flag added to either line changes the
# semantics string, which misses every record written before it — which is the point of recording
# the command rather than assuming it.
$portabilitySemantics = 'dotnet fable <entry> -o <tmp> --noCache'
$lawsProject = Join-Path $PSScriptRoot 'FableLaws.fsproj'

$nodeVersionCache = $null

function Get-NodeVersionTag {
    # The laws record stands for a byte-comparison one side of which is produced by `node`, so the
    # runtime that produced it is an input to that green in a way it is not for a mere compile.
    # Probed lazily — nothing but the laws subject needs it.
    if ($null -ne $script:nodeVersionCache) { return $script:nodeVersionCache }
    $version = 'absent'
    if (Get-Command node -CommandType Application -ErrorAction SilentlyContinue) {
        try { $version = (@(& node --version) | Select-Object -First 1).Trim() } catch { $version = 'unreadable' }
    }
    $script:nodeVersionCache = $version
    return $version
}

function Get-LawsSemantics {
    "dotnet run -c Release + dotnet fable -o output --noCache + node $(Get-NodeVersionTag), byte-compared"
}

function Get-AddressSubjects {
    # Every compile THIS invocation can perform, in the order it performs them — so `-Addresses`
    # under `-SkipLaws` reports the run that would actually happen rather than a hypothetical one.
    $subjects = @(if (-not $SkipPortability) {
            foreach ($entry in $entries) {
                [pscustomobject]@{ Subject = $entry.Name; Label = $entry.Relative; Entry = $entry.Path; Semantics = $portabilitySemantics }
            }
        })
    if ((-not $SkipLaws) -and (Test-Path -LiteralPath $lawsProject)) {
        $subjects += [pscustomobject]@{ Subject = 'FableLaws'; Label = (ConvertTo-RepoRelative $lawsProject); Entry = $lawsProject; Semantics = (Get-LawsSemantics) }
    }
    return $subjects
}

if ($Addresses) {
    Write-Host "Fable content addresses — lane '$lane' ($(if ($laneMaySkip) { 'may skip a matching record' } else { 'always compiles' }))"
    # Named rather than merely used: the records are outside the repo and keyed by a hash, so
    # without this line the one place a reader would look to clear or inspect them is unguessable.
    Write-Host "records $(Split-Path -Parent (Get-RecordPath 'any'))"
    Write-Host ""
    foreach ($subject in (Get-AddressSubjects)) {
        $address = Get-CompileAddress $subject.Entry $subject.Semantics
        $recorded = Test-RecordedGreen $subject.Subject $address $subject.Semantics
        $state = if (-not $address) { 'NO ADDRESS — will compile' } elseif ($recorded) { "recorded green (lane $($recorded.lane), $($recorded.recordedUtc))" } else { 'no recorded green' }
        Write-Host ("  {0,-58} {1}" -f $subject.Label, ($address ?? '-'))
        Write-Host ("  {0,-58} {1}" -f '', $state) -ForegroundColor DarkGray
    }
    exit 0
}

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

# ── The go-red proof for the address function (Phase 1619) ──────────────────

function Invoke-AddressingProof {
    <#
      Proves the address MOVES when the things it claims to cover move. It builds a scratch tree of
      plain files, perturbs REAL INPUTS on disk, and re-derives through the same `Get-CompileAddress`
      the stage uses — never by editing the manifest the hash is taken over, which would prove only
      that SHA-256 is sensitive to its input.

      Nothing is compiled, so it costs milliseconds and can therefore run on every invocation that
      could skip a compile — which is exactly when a broken address function would serve a stale
      green. Returns the failures it found; an empty list means every assertion held.
    #>
    param([switch] $Quiet)

    $proofRoot = Join-Path ([IO.Path]::GetTempPath()) 'fuaran-fable-address-proof'
    $found = New-Object System.Collections.Generic.List[string]

    function New-ProofProject {
        param([string] $root, [string] $name, [string[]] $references = @(), [string] $nullable = 'enable')
        $dir = Join-Path $root $name
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        $referenceItems = ($references | ForEach-Object { "    <ProjectReference Include=`"..\$_\$_.fsproj`" />" }) -join "`n"
        @"
<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>$nullable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Library.fs" />
  </ItemGroup>
  <ItemGroup>
$referenceItems
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $dir "$name.fsproj") -Encoding utf8NoBOM
        "module Proof.$name`n`nlet describe () = `"$name`"`n" | Set-Content -LiteralPath (Join-Path $dir 'Library.fs') -Encoding utf8NoBOM
    }

    function Get-ProofAddress {
        param([string] $root, [string] $name)
        # The caches assume a tree that does not move; this one moves on purpose.
        Clear-AddressCaches
        return Get-CompileAddress (Join-Path (Join-Path $root $name) "$name.fsproj") 'proof'
    }

    function Assert-Proof {
        param([string] $what, [bool] $held)
        if ($held) { if (-not $Quiet) { Write-Host "    ok   $what" -ForegroundColor DarkGray } }
        else { $found.Add("addressing proof: $what") }
    }

    Remove-Item -Recurse -Force $proofRoot -ErrorAction SilentlyContinue
    try {
        New-Item -ItemType Directory -Force -Path (Join-Path $proofRoot '.config') | Out-Null
        '<Project><PropertyGroup><LangVersion>latest</LangVersion></PropertyGroup></Project>' |
            Set-Content -LiteralPath (Join-Path $proofRoot 'Directory.Build.props') -Encoding utf8NoBOM
        $manifestPath = Join-Path (Join-Path $proofRoot '.config') 'dotnet-tools.json'
        '{"version":1,"isRoot":true,"tools":{"fable":{"version":"1.0.0","commands":["fable"]}}}' |
            Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM

        New-ProofProject -root $proofRoot -name 'Leaf'
        New-ProofProject -root $proofRoot -name 'Root' -references @('Leaf')

        $rootBefore = Get-ProofAddress $proofRoot 'Root'
        $leafBefore = Get-ProofAddress $proofRoot 'Leaf'

        Assert-Proof 'an address is derived at all' ([bool] $rootBefore)

        # Determinism first: an address that moved on its own would make every skip a miss, which
        # LOOKS like a working gate and is a silently broken cache.
        Assert-Proof 'the same tree yields the same address twice' ((Get-ProofAddress $proofRoot 'Root') -ceq $rootBefore)

        # (a) one byte in a TRANSITIVELY-REFERENCED source — the file the entry never names.
        Add-Content -LiteralPath (Join-Path (Join-Path $proofRoot 'Leaf') 'Library.fs') -Value '// one byte' -Encoding utf8NoBOM
        $rootAfterLeaf = Get-ProofAddress $proofRoot 'Root'
        Assert-Proof 'a one-byte edit to a transitively-referenced .fs changes the address' ($rootAfterLeaf -cne $rootBefore)

        # (b) the Fable tool version — every address, not merely the one that happened to be checked.
        '{"version":1,"isRoot":true,"tools":{"fable":{"version":"2.0.0","commands":["fable"]}}}' |
            Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
        $rootAfterTool = Get-ProofAddress $proofRoot 'Root'
        $leafAfterTool = Get-ProofAddress $proofRoot 'Leaf'
        Assert-Proof 'a Fable tool-version bump changes the entry address' ($rootAfterTool -cne $rootAfterLeaf)
        Assert-Proof 'a Fable tool-version bump changes EVERY address' ($leafAfterTool -cne $leafBefore)

        # The entry's own Fable-relevant properties — under Fable these govern the whole graph.
        (Get-Content -Raw -LiteralPath (Join-Path (Join-Path $proofRoot 'Root') 'Root.fsproj')) -replace '<Nullable>enable</Nullable>', '<Nullable>disable</Nullable>' |
            Set-Content -LiteralPath (Join-Path (Join-Path $proofRoot 'Root') 'Root.fsproj') -Encoding utf8NoBOM
        Assert-Proof "a change to the entry's Nullable setting changes the address" ((Get-ProofAddress $proofRoot 'Root') -cne $rootAfterTool)

        # An unresolvable graph must yield NO address — the safety direction, asserted rather than
        # assumed, because it is the branch that decides whether an unknown tree skips or compiles.
        Remove-Item -Recurse -Force (Join-Path $proofRoot 'Leaf')
        Assert-Proof 'a missing referenced project yields no address (so the compile happens)' (-not (Get-ProofAddress $proofRoot 'Root'))
    }
    finally {
        Remove-Item -Recurse -Force $proofRoot -ErrorAction SilentlyContinue
        Clear-AddressCaches
    }

    return $found
}

if ($ProveAddressing) {
    Write-Host ''
    Write-Host '── Fable: address go-red proof ───────────────────────────' -ForegroundColor Cyan
    $proofFailures = Invoke-AddressingProof
    Write-Host ''
    if ($proofFailures.Count -gt 0) {
        Write-Host "==== address go-red proof: FAILED ($($proofFailures.Count))" -ForegroundColor Red
        foreach ($f in $proofFailures) { Write-Host "     $f" -ForegroundColor Red }
        exit 1
    }
    Write-Host '==== address go-red proof: green' -ForegroundColor Green
    exit 0
}

# The proof runs automatically whenever a skip is possible — a broken address function is only
# dangerous on the lane that consults one, and this is the last moment before it does.
if ($laneMaySkip) {
    Write-Stage "address go-red proof (lane '$lane' may skip a matching compile)"
    $proofFailures = Invoke-AddressingProof -Quiet
    if ($proofFailures.Count -gt 0) {
        # Skipping is withdrawn, not merely reported: an address function that cannot be shown to
        # move is one no compile may be skipped on.
        $laneMaySkip = $false
        foreach ($f in $proofFailures) { $failures.Add($f) }
        Write-Host '  FAILED — every compile will run; see the failures below.' -ForegroundColor Red
    }
    else {
        Write-Host '  green — the address moves with the sources, the tool version and the entry properties.' -ForegroundColor DarkGray
    }
}

# Outside the repo tree on purpose: Fable emits a deep `fable_modules/` graph, and a deep output
# path under an already-deep worktree hits MAX_PATH, where fsc fails without a readable error.
$portabilityRoot = Join-Path ([IO.Path]::GetTempPath()) 'fuaran-fable-portability'

if (-not $SkipPortability) {
    Write-Stage "portability — $($entries.Count) entries covering $($covered.Count) of $($gated.Count) gated projects (lane '$lane': $(if ($laneMaySkip) { 'an unchanged entry may be skipped by address' } else { 'every entry compiles' }))"

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

    $skipped = 0

    foreach ($project in $entries) {
        $outDir = Join-Path $portabilityRoot $project.Name
        $address = Get-CompileAddress $project.Path $portabilitySemantics

        # The full lane never reaches the second operand: `$laneMaySkip` is false there, so the
        # record is written below and never read, and the skip is unreachable rather than declined.
        $recorded = if ($laneMaySkip) { Test-RecordedGreen $project.Name $address $portabilitySemantics } else { $null }

        if ($recorded) {
            # A NAMED skip, never a silent one — the posture the Node-absent branch below takes, and
            # the reason the address is printed rather than merely consulted.
            $skipped++
            Write-Host "  SKIPPED BY ADDRESS $($project.Relative)" -ForegroundColor Yellow
            Write-Host "    address $address" -ForegroundColor DarkGray
            Write-Host "    recorded green in lane '$($recorded.lane)' at $($recorded.recordedUtc)" -ForegroundColor DarkGray
            continue
        }

        Write-Host "  fable $($project.Relative)  [$(Get-EntryReason $project)]" -ForegroundColor DarkGray

        # --noCache is mandatory: a stale .fable cache can serve a compile that no longer reflects
        # the sources, which is the one answer this stage must never give. The address above does
        # not soften that — it decides whether to INVOKE Fable at all, and it moves with every byte
        # Fable would read.
        dotnet fable $project.Path -o $outDir --noCache

        if ($LASTEXITCODE -ne 0) {
            $failures.Add("Fable portability compile FAILED for $($project.Relative) (exit $LASTEXITCODE)")
            Write-Host "  FAILED: $($project.Relative)" -ForegroundColor Red
            Clear-RecordedGreen $project.Name
        }
        else {
            # Written on EVERY lane, consulted on the narrow ones only — so the full lane's greens
            # are what the next narrow run stands on.
            Write-RecordedGreen $project.Name $address $portabilitySemantics
        }
    }

    if ($skipped -gt 0) {
        Write-Host "  $skipped of $($entries.Count) entries skipped by address (lane '$lane'); the full lane compiles all $($entries.Count)." -ForegroundColor Yellow
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

    # The laws stage is one addressed subject rather than two: its record stands for the whole
    # green — both legs run, byte-identical, zero violations — so honouring it skips exactly the
    # work that produced it. Its semantics string carries the `node` version, because one side of
    # that comparison is produced by a runtime a compile-only subject never touches.
    $lawsSemantics = Get-LawsSemantics
    $lawsAddress = Get-CompileAddress $lawsProject $lawsSemantics
    $lawsRecorded = if ($laneMaySkip) { Test-RecordedGreen 'FableLaws' $lawsAddress $lawsSemantics } else { $null }
    $lawsFailuresBefore = $failures.Count
    $nodePresent = [bool] (Get-Command node -CommandType Application -ErrorAction SilentlyContinue)

    if ($lawsRecorded) {
        Write-Host "  SKIPPED BY ADDRESS $(ConvertTo-RepoRelative $lawsProject)" -ForegroundColor Yellow
        Write-Host "    address $lawsAddress" -ForegroundColor DarkGray
        Write-Host "    recorded green in lane '$($lawsRecorded.lane)' at $($lawsRecorded.recordedUtc)" -ForegroundColor DarkGray
        Write-Host "    (the full lane runs both legs and byte-compares them regardless)" -ForegroundColor DarkGray
    }
    elseif (-not $nodePresent) {
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

    # Only a run that actually PERFORMED the comparison may record it. A skipped-by-address run has
    # nothing new to say, and a Node-absent run ran half the stage — recording either would be a
    # green standing on work nobody did.
    if ((-not $lawsRecorded) -and $nodePresent) {
        if ($failures.Count -eq $lawsFailuresBefore) {
            Write-RecordedGreen 'FableLaws' $lawsAddress $lawsSemantics
        }
        else {
            Clear-RecordedGreen 'FableLaws'
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
