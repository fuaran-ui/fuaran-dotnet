#Requires -Version 7.0
<#
.SYNOPSIS
  The go-red proof for `fable-check.ps1`'s DERIVED portability set (Phase 1606).

.DESCRIPTION
  The gate beside this file no longer carries a list of projects: it derives one from the tree, and
  the whole claim it makes is that a package shipping `fable\` sources is compiled WITHOUT ANYONE
  EDITING A LIST. A claim of that shape is only worth what its falsification is worth — a
  derivation that silently selected nothing would report a green portability stage forever, and
  read exactly like a working one.

  So this script builds a scratch package tree under `.selftest/`, points the gate's `-SrcRoot` at
  it, and asserts four things:

    1. A clean scratch package that ships `fable\` sources is DERIVED AS AN ENTRY and compiles —
       so a red result below is the defect and not the harness.
    2. The same package carrying a Fable-only defect — a `#if FABLE_COMPILER` arm naming something
       that does not exist, which is exactly the shape Fuaran.UI 0.78.0 shipped — FAILS the gate,
       with no list edited anywhere. This is the phase's acceptance criterion, executed.
    3. Of two packages REFERENCED by a gated entry, the one with no conditional arm is covered
       transitively rather than entered (the rule that keeps the entry set small) and the one with
       an arm is entered on its own account (the rule that compiles it under its OWN properties).
    4. A `<FablePortabilityExemption>` holds a package out AND is echoed by name, so an exemption
       cannot be a silent one.

  THE CONTENT-ADDRESSED SKIP (Phase 1619) is proved here too, and for the same reason one step
  sharper: a cache that returns a stale green is worse than no cache, so the claim worth
  falsifying is not "it skips" but "a changed input MISSES". Five more assertions, end to end
  through real Fable compiles rather than through the address function alone:

    5. In a NARROW lane a second run over an unchanged tree is SKIPPED BY ADDRESS, with the address
       printed — and the first run was not, so the skip is a state that was reached rather than one
       that was always there.
    6. A one-byte edit to a TRANSITIVELY-REFERENCED source — a file the entry does not name and
       which is not itself an entry — makes the narrow lane recompile the entry.
    7. The FULL lane compiles with a matching record present: it writes addresses and consults
       none, so the skip is structurally unreachable on the lane a release cites.
    8. A RED compile clears the record, so restoring the tree to a state that once passed still
       recompiles rather than standing on the earlier green.

  The go-red proof for the ADDRESS FUNCTION itself — that the hash moves with the sources, the
  Fable tool version and the entry's properties — is `fable-check.ps1 -ProveAddressing`, which
  compiles nothing, costs milliseconds, and therefore runs inside the gate on every invocation
  that could skip. It is exercised here as well so a single command covers both halves.

  It is NOT part of the gate. It compiles scratch projects with Fable (tens of seconds) to prove a
  property of the gate rather than of the repo, which is a different question asked at a different
  cadence — the posture the workspace's own `surface-guard.tests.ps1` takes. Run it when
  `fable-check.ps1`'s derivation changes:

      pwsh -NoProfile -File tests/fable-laws/fable-check.tests.ps1

  EXIT 0 = every assertion held.
#>
[CmdletBinding()]
param(
    # Keep the scratch tree for inspection after the run.
    [switch] $KeepScratch
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$gate = Join-Path $PSScriptRoot 'fable-check.ps1'
$scratch = Join-Path $PSScriptRoot '.selftest'
$failures = New-Object System.Collections.Generic.List[string]

function New-ScratchProject {
    <#
      One scratch package: an fsproj that packs its sources under `fable\` (which is what puts it
      in the derived set) plus one source file. `$references` names sibling scratch projects, so a
      test can build the reference shape it wants to assert about.
    #>
    param(
        [string] $name,
        [string] $source,
        [string[]] $references = @(),
        [string] $exemption = ''
    )

    $dir = Join-Path $scratch $name
    New-Item -ItemType Directory -Force -Path $dir | Out-Null

    $referenceItems = ($references | ForEach-Object {
            "    <ProjectReference Include=`"..\$_\$_.fsproj`" />"
        }) -join "`n"

    $exemptionProperty =
    if ($exemption) {
        "  <PropertyGroup>`n    <FablePortabilityExemption>$exemption</FablePortabilityExemption>`n  </PropertyGroup>"
    }
    else { '' }

    @"
<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>true</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <Content Include="**\*.fsproj;**\*.fs" Exclude="**\*.fs.js;**\bin\**;**\obj\**" PackagePath="fable\" />
  </ItemGroup>
$exemptionProperty
  <ItemGroup>
    <Compile Include="Library.fs" />
  </ItemGroup>
  <ItemGroup>
$referenceItems
    <PackageReference Include="FSharp.Core" />
  </ItemGroup>
</Project>
"@ | Set-Content -Path (Join-Path $dir "$name.fsproj") -Encoding utf8NoBOM

    $source | Set-Content -Path (Join-Path $dir 'Library.fs') -Encoding utf8NoBOM
}

function Invoke-Gate {
    # The lane travels the way it travels in production — through `FUARAN_TEST_LANE`, which
    # `run.ps1` sets around the stage — rather than through a parameter this script would be the
    # only caller of. Default `full`, so the derivation assertions below run under the lane that
    # never skips and read exactly as they did before Phase 1619.
    param([string[]] $extraArguments = @(), [string] $lane = 'full')
    $previousLane = $env:FUARAN_TEST_LANE
    $env:FUARAN_TEST_LANE = $lane
    try {
        # Never piped — the same rule the gate itself states about `dotnet fable`: a pipe would
        # report the last command's status and a red gate would read as a pass, which is the one
        # answer this script must never give.
        $output = & pwsh -NoProfile -File $gate -SrcRoot $scratch -SkipLaws @extraArguments 2>&1
        $exit = $LASTEXITCODE
    }
    finally {
        if ($null -eq $previousLane) { Remove-Item Env:FUARAN_TEST_LANE -ErrorAction SilentlyContinue }
        else { $env:FUARAN_TEST_LANE = $previousLane }
    }
    [pscustomobject]@{ Exit = $exit; Text = ($output | Out-String) }
}

function Assert {
    param([string] $what, [bool] $held, [string] $detail = '')
    if ($held) {
        Write-Host "  ok   $what" -ForegroundColor Green
    }
    else {
        Write-Host "  FAIL $what" -ForegroundColor Red
        if ($detail) { Write-Host $detail -ForegroundColor DarkGray }
        $failures.Add($what)
    }
}

# No conditional arm at all — so this package is in the gate's scope only through the reference
# graph, which is precisely what assertion 3 measures.
$plainSource = @'
module SelfTest.Plain

let describe () = "plain"
'@

# A package whose `#if FABLE_COMPILER` arm is FINE. The .NET build compiles the `#else` arm, so
# both pipelines are happy — which is the control the defective case is measured against.
$cleanSource = @'
module SelfTest.Clean

#if FABLE_COMPILER
let describe () = "fable"
#else
let describe () = "dotnet"
#endif
'@

# The Fuaran.UI 0.78.0 shape, reduced: an arm naming something that does not exist. Invisible to
# `dotnet build` (which compiles only the `#else` arm) and to any Fable compile that does not enter
# this project.
$defectiveSource = @'
module SelfTest.Defective

#if FABLE_COMPILER
let describe () = NoSuchModule.noSuchValue ()
#else
let describe () = "dotnet"
#endif
'@

Remove-Item -Recurse -Force $scratch -ErrorAction SilentlyContinue

try {
    Write-Host ''
    Write-Host '── fable-check derivation: go-red proof ──────────────────' -ForegroundColor Cyan

    # ── 1 + 3 + 4. The control: one entry, one project covered through it, one exemption. ──
    New-ScratchProject -name 'SelfTest.Leaf' -source $plainSource
    New-ScratchProject -name 'SelfTest.Armed' -source $cleanSource
    New-ScratchProject -name 'SelfTest.Root' -source $cleanSource -references @('SelfTest.Leaf', 'SelfTest.Armed')
    New-ScratchProject -name 'SelfTest.Held' -source $defectiveSource `
        -exemption 'Held out on purpose, to prove an exemption is honoured and echoed.'

    $listed = Invoke-Gate -extraArguments @('-List')

    Assert 'the derivation runs over a scratch tree' ($listed.Exit -eq 0) $listed.Text
    Assert 'the referencing package is an entry' ($listed.Text -match 'SelfTest\.Root\.fsproj\s*\(') $listed.Text
    Assert 'an arm-free referenced package is covered, not entered' `
    ($listed.Text -notmatch 'SelfTest\.Leaf\.fsproj\s*\(') $listed.Text
    Assert 'an arm-carrying referenced package is entered on its own account' `
    ($listed.Text -match 'SelfTest\.Armed\.fsproj\s*\(conditional arm\)') $listed.Text
    Assert 'every gated package is covered' ($listed.Text -match 'covered: 3 of 3 gated') $listed.Text
    Assert 'the exemption is echoed with its reason' `
    ($listed.Text -match 'prove an exemption is honoured') $listed.Text

    $green = Invoke-Gate
    Assert 'a clean scratch tree passes the gate' ($green.Exit -eq 0) $green.Text

    # ── 2. The proof. One source file changes; no list anywhere does. ──
    $defectiveSource | Set-Content -Path (Join-Path $scratch 'SelfTest.Root/Library.fs') -Encoding utf8NoBOM

    $red = Invoke-Gate
    Assert 'a Fable-only defect fails the gate with no list edit' ($red.Exit -ne 0) $red.Text
    Assert 'the failure names the offending project' `
    ($red.Text -match 'SelfTest\.Root.*FAILED|FAILED.*SelfTest\.Root') $red.Text

    # ── 5-8. The content-addressed skip (Phase 1619). ──
    #
    # What is worth falsifying here is not that a skip happens — that is easy and useless — but
    # that a CHANGED INPUT MISSES, that the full lane cannot reach the skip at all, and that a red
    # run does not leave a green behind. Each assertion below is a state the run before it was NOT
    # in, so none of them can pass vacuously.

    Write-Host ''
    Write-Host '── fable-check content address: go-red proof ─────────────' -ForegroundColor Cyan

    $proofText = (& pwsh -NoProfile -File $gate -ProveAddressing 2>&1 | Out-String)
    Assert "the address function's own go-red proof holds" ($LASTEXITCODE -eq 0) $proofText

    $cleanSource | Set-Content -Path (Join-Path $scratch 'SelfTest.Root/Library.fs') -Encoding utf8NoBOM

    # Records outlive a run — they sit under the system temp root, keyed by the source root — so a
    # previous invocation of THIS script would otherwise hand the first assertion a skip and it
    # would fail for the wrong reason. The gate names the store rather than leaving it unguessable.
    $probe = Invoke-Gate -extraArguments @('-Addresses')
    $recordRoot = ((($probe.Text -split "`r?`n") | Where-Object { $_ -like 'records *' } | Select-Object -First 1) -replace '^records\s+', '').Trim()
    Assert 'the gate names its record store' ([bool] $recordRoot) $probe.Text
    if ($recordRoot) { Remove-Item -Recurse -Force -LiteralPath $recordRoot -ErrorAction SilentlyContinue }

    $first = Invoke-Gate -lane 'fast'
    Assert 'the narrow lane compiles when nothing is recorded' `
    (($first.Exit -eq 0) -and ($first.Text -match 'fable .*SelfTest\.Root') -and ($first.Text -cnotmatch 'SKIPPED BY ADDRESS')) $first.Text

    $second = Invoke-Gate -lane 'fast'
    Assert 'the narrow lane skips an unchanged compile by address' `
    (($second.Exit -eq 0) -and ($second.Text -cmatch 'SKIPPED BY ADDRESS.*SelfTest\.Root')) $second.Text
    Assert 'the skip prints the address it matched' ($second.Text -match 'address [0-9a-f]{64}') $second.Text
    Assert 'the skip invokes no compile at all' ($second.Text -cnotmatch '(?m)^\s+fable ') $second.Text

    # (a) One byte, in a file the entry does not name and which is not itself an entry — the only
    # route to it is the transitive reference graph.
    Add-Content -LiteralPath (Join-Path $scratch 'SelfTest.Leaf/Library.fs') -Value '// one byte'

    $edited = Invoke-Gate -lane 'fast'
    Assert 'a one-byte edit to a transitively-referenced .fs makes the narrow lane recompile' `
    (($edited.Exit -eq 0) -and ($edited.Text -match 'fable .*SelfTest\.Root')) $edited.Text
    # The discriminator: if everything recompiled, the assertion above would hold for a gate whose
    # address is a constant. An entry the edit cannot reach must still skip.
    Assert 'an entry that edit cannot reach still skips' `
    ($edited.Text -cmatch 'SKIPPED BY ADDRESS.*SelfTest\.Armed') $edited.Text

    # (c) Every record now matches the tree, which is precisely the state in which the full lane
    # must compile anyway.
    $full = Invoke-Gate -lane 'full'
    Assert 'the full lane compiles with matching records present' `
    (($full.Exit -eq 0) -and ($full.Text -cnotmatch 'SKIPPED BY ADDRESS')) $full.Text
    Assert 'the full lane compiles every entry' `
    (($full.Text -match 'fable .*SelfTest\.Root') -and ($full.Text -match 'fable .*SelfTest\.Armed')) $full.Text

    # A red run must delete the record rather than merely decline to write one. Restoring the file
    # below gives back the EXACT bytes whose green was recorded a moment ago, so a surviving record
    # would match and serve a skip.
    $defectiveSource | Set-Content -Path (Join-Path $scratch 'SelfTest.Root/Library.fs') -Encoding utf8NoBOM
    $failedNarrow = Invoke-Gate -lane 'fast'
    Assert 'the narrow lane still fails on a Fable-only defect' ($failedNarrow.Exit -ne 0) $failedNarrow.Text

    $cleanSource | Set-Content -Path (Join-Path $scratch 'SelfTest.Root/Library.fs') -Encoding utf8NoBOM
    $restored = Invoke-Gate -lane 'fast'
    Assert 'a red compile cleared the record, so the restored tree recompiles' `
    (($restored.Exit -eq 0) -and ($restored.Text -match 'fable .*SelfTest\.Root') -and ($restored.Text -cnotmatch 'SKIPPED BY ADDRESS.*SelfTest\.Root')) $restored.Text

    # ── The scratch output root is per TREE ──────────────────────────────────
    #
    # The portability root lives outside the repo (MAX_PATH), and the stage WIPES it at start. With
    # a fixed leaf that made every worktree share one directory, so two gates running at once
    # deleted each other's output mid-compile — surfacing as path exceptions and cascading F#
    # errors naming `Fuaran.Core.*`, which read as a portability break and are not one (Phase 1605;
    # 1619's fold hit the same collision from the other side).
    #
    # The discriminator is a SECOND COPY of the gate at a different path, run over the SAME
    # `-SrcRoot`. Same sources, different tree: the address records may legitimately be shared,
    # the scratch output must not be. A constant leaf passes the first assertion and fails this one.
    $rootOf = {
        param([string] $text)
        if ($text -match '(?m)^\s*output root:\s*(.+?)\s*$') { $Matches[1] } else { '' }
    }

    $hereRoot = & $rootOf $restored.Text
    Assert 'the portability stage reports its output root' ($hereRoot -ne '') $restored.Text
    Assert 'the output root carries a tree key rather than a bare fixed name' `
    ($hereRoot -match 'fuaran-fable-portability-[0-9a-f]{12}$') $hereRoot

    $otherTree = Join-Path ([IO.Path]::GetTempPath()) ("fable-check-tree-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Force -Path $otherTree | Out-Null
    try {
        Copy-Item -Path (Join-Path $PSScriptRoot '*.ps1') -Destination $otherTree -Force
        $previousLane = $env:FUARAN_TEST_LANE
        $env:FUARAN_TEST_LANE = 'full'
        try {
            $elsewhere = & pwsh -NoProfile -File (Join-Path $otherTree 'fable-check.ps1') -SrcRoot $scratch -SkipLaws 2>&1 | Out-String
        }
        finally {
            if ($null -eq $previousLane) { Remove-Item Env:FUARAN_TEST_LANE -ErrorAction SilentlyContinue }
            else { $env:FUARAN_TEST_LANE = $previousLane }
        }

        $thereRoot = & $rootOf $elsewhere
        Assert 'a copy of the gate at another path reports its own output root' ($thereRoot -ne '') $elsewhere
        Assert 'two trees over the same sources get DIFFERENT scratch roots' `
        (($hereRoot -ne '') -and ($thereRoot -ne '') -and ($hereRoot -ne $thereRoot)) "here=$hereRoot there=$thereRoot"
    }
    finally {
        Remove-Item -Recurse -Force $otherTree -ErrorAction SilentlyContinue
    }
}
finally {
    if (-not $KeepScratch) {
        Remove-Item -Recurse -Force $scratch -ErrorAction SilentlyContinue
    }
}

Write-Host ''

if ($failures.Count -gt 0) {
    Write-Host "==== fable-check go-red proof: FAILED ($($failures.Count))" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "     $f" -ForegroundColor Red }
    exit 1
}

Write-Host '==== fable-check go-red proof: green' -ForegroundColor Green
exit 0
