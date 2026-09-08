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
    param([string[]] $extraArguments = @())
    # Never piped — the same rule the gate itself states about `dotnet fable`: a pipe would report
    # the last command's status and a red gate would read as a pass, which is the one answer this
    # script must never give.
    $output = & pwsh -NoProfile -File $gate -SrcRoot $scratch -SkipLaws @extraArguments 2>&1
    [pscustomobject]@{ Exit = $LASTEXITCODE; Text = ($output | Out-String) }
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
