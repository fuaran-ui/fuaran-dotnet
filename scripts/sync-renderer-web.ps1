#Requires -Version 7.0
<#
.SYNOPSIS
    Sync (or drift-guard) the embedded browser-renderer assets in
    src/Fuaran.UI.Renderer.Web/content/.

.DESCRIPTION
    `Fuaran.UI.Renderer.Web` embeds a BUILT ARTEFACT from another repository —
    the `@fuaran-ui/renderer` standalone browser bundle — so that a .NET
    consumer needs no Node toolchain. This script is the only sanctioned way
    that copy is made, and the reference-CSS discipline generalised: the copy is
    GENERATED, never hand-placed, and a check target fails when it is stale.

    The claim the package makes is precise and worth restating, because it is
    easy to overstate: **maintainers run Node to produce the bundle; consumers
    never do.** The bundle and its fingerprint are COMMITTED, so this repo — and
    a single-repo CI checkout, and a consumer's restore — build with no Node
    present at all.

    WHAT IS SYNCED

      content/fuaran-renderer.js      a byte copy of the fuaran-ts standalone
                                     bundle
      content/fuaran-renderer.json    the fingerprint sidecar: which package and
                                     version the bundle came from, the bundle's
                                     own BUNDLE_VERSION / WIRE_PROFILE stamps,
                                     the reference stylesheet's class-vocabulary
                                     fingerprint at sync time, and a SHA-256 of
                                     the copied bytes

    The reference STYLESHEET is deliberately not synced here: the package embeds
    the canonical file where it lies (a `<EmbeddedResource>` link), so there is
    no copy of it in this package to drift.

    WHAT THE CHECK CAN AND CANNOT SEE

    Two questions, and only one is answerable offline:

      * Does the recorded renderer VERSION still match the sibling's
        package.json, and does the recorded vocabulary fingerprint still match
        `Theme.vocabularyFingerprint`? Answerable from committed text, always,
        and this is what runs in the `Check` gate.

      * Do the embedded BYTES still match a freshly built bundle? Answerable
        only where the bundle has been built — it is a gitignored build output
        in fuaran-ts. Checked when present, reported as NOT CHECKED when not.

    A version-level match with unbuilt bytes is therefore a weaker statement
    than a byte match, and the check says which it made rather than printing one
    word for both.

.PARAMETER Check
    Report drift and exit non-zero; write nothing. The default when neither
    switch is given.

.PARAMETER Sync
    Copy the bundle and rewrite the fingerprint.

.PARAMETER SkipBuild
    With -Sync, use the bundle already in fuaran-ts rather than rebuilding it.
    Useful when the workspace build has just run.
#>
[CmdletBinding()]
param(
    [switch] $Check,
    [switch] $Sync,
    [switch] $SkipBuild
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$tsRepo = Join-Path (Split-Path $repoRoot -Parent) "fuaran-ts"
$rendererPkg = Join-Path $tsRepo "packages/renderer"
$builtBundle = Join-Path $rendererPkg "standalone/fuaran-renderer.global.js"
$standaloneSrc = Join-Path $rendererPkg "src/standalone.tsx"
$tsPackageJson = Join-Path $rendererPkg "package.json"

$contentDir = Join-Path $repoRoot "src/Fuaran.UI.Renderer.Web/content"
$embeddedBundle = Join-Path $contentDir "fuaran-renderer.js"
$embeddedFingerprint = Join-Path $contentDir "fuaran-renderer.json"
$themeSource = Join-Path $repoRoot "src/Fuaran.UI.Renderer.Core/Theme.fs"

# Sibling launcher conventions - see workspace CLAUDE.md "Sibling launcher
# conventions (mandate)". Copy-pasted from the canonical body there; do not
# diverge without updating the workspace doc.
function Invoke-Pnpm {
    # Node ships a pnpm.ps1 shim that rebuilds args from the caller's
    # command-line text via Substring(InvocationName.Length). Called from inside
    # another .ps1 as `& pnpm build`, the slice eats the leading characters and
    # pnpm sees a mangled command. Resolving pnpm.cmd directly skips the shim.
    #
    # `Get-Command pnpm.cmd` returns EVERY pnpm.cmd on PATH - pin to the first
    # match, or `$cmd.Source` is an array and `& $cmd.Source` concatenates the
    # paths into one bogus string.
    [CmdletBinding()]
    param([Parameter(ValueFromRemainingArguments = $true)] $Arguments)
    $cmd = Get-Command pnpm.cmd -CommandType Application -ErrorAction Stop | Select-Object -First 1
    & $cmd.Source @Arguments
}

function Get-Sha256([string] $path) {
    (Get-FileHash -Path $path -Algorithm SHA256).Hash
}

# Read a single-quoted string constant out of a TypeScript source file. The
# stamps are `export const BUNDLE_VERSION = '0.1.0';` - a regex over the source
# rather than an execution of the bundle, so the check needs no Node runtime and
# a stamp that stops matching fails loudly here rather than reporting agreement.
function Read-TsConst([string] $path, [string] $name) {
    $text = Get-Content -Raw -Path $path
    $m = [regex]::Match($text, "export\s+const\s+$name\s*=\s*'([^']*)'")
    if (-not $m.Success) {
        throw "Cannot read '$name' from $path. The standalone bundle's stamp is what the fingerprint records; a stamp this script cannot read is a sync that would silently record nothing."
    }
    $m.Groups[1].Value
}

function Read-JsonField([string] $path, [string] $name) {
    $text = Get-Content -Raw -Path $path
    $m = [regex]::Match($text, "`"$name`"\s*:\s*`"([^`"]*)`"")
    if (-not $m.Success) { throw "Cannot read '$name' from $path." }
    $m.Groups[1].Value
}

function Read-VocabularyFingerprint {
    $text = Get-Content -Raw -Path $themeSource
    $m = [regex]::Match($text, 'let\s+vocabularyFingerprint\s*=\s*"([^"]*)"')
    if (-not $m.Success) {
        throw "Cannot read `vocabularyFingerprint` from $themeSource - the fingerprint's whole job is to record it, so a value this script cannot read must fail rather than record an empty string."
    }
    $m.Groups[1].Value
}

# The sidecar's canonical text. MUST agree byte for byte with
# Fingerprint.toJson in the F# package - the same six keys, the same order, two
# spaces of indent, a trailing newline. Two writers of one format is a drift
# hazard, and the package's own round-trip test is what holds them together.
function Format-Fingerprint($fp) {
    $lines = @(
        "  `"rendererPackage`": `"$($fp.rendererPackage)`"",
        "  `"rendererVersion`": `"$($fp.rendererVersion)`"",
        "  `"bundleVersion`": `"$($fp.bundleVersion)`"",
        "  `"wireProfile`": `"$($fp.wireProfile)`"",
        "  `"vocabularyFingerprint`": `"$($fp.vocabularyFingerprint)`"",
        "  `"bundleSha256`": `"$($fp.bundleSha256)`""
    )
    "{`n" + ($lines -join ",`n") + "`n}`n"
}

if (-not $Sync) { $Check = $true }

$siblingPresent = Test-Path $rendererPkg
$bundleBuilt = Test-Path $builtBundle

# ─────────────────────────────────────────────────────────────── SYNC ────

if ($Sync) {
    if (-not $siblingPresent) {
        throw "fuaran-ts is not in this checkout ($rendererPkg). The bundle is produced there; a sync cannot invent it."
    }

    if (-not $SkipBuild) {
        Write-Host "Building the fuaran-ts standalone bundle..."
        Push-Location $tsRepo
        try {
            Invoke-Pnpm build
            if ($LASTEXITCODE -ne 0) { throw "pnpm build failed (exit $LASTEXITCODE)." }
        }
        finally { Pop-Location }
    }

    if (-not (Test-Path $builtBundle)) {
        throw "No standalone bundle at $builtBundle. Run without -SkipBuild, or build it with ``pnpm --filter @fuaran-ui/renderer run build:standalone``."
    }

    New-Item -ItemType Directory -Force -Path $contentDir | Out-Null

    # A byte copy, not a text copy: this is minified JavaScript and there is no
    # newline convention to honour. Copy-Item preserves the bytes exactly, which
    # is what makes the sync idempotent - run it twice and the second run writes
    # identical bytes and reports "already identical".
    Copy-Item -Path $builtBundle -Destination $embeddedBundle -Force

    $fp = [ordered] @{
        rendererPackage       = Read-JsonField $tsPackageJson "name"
        rendererVersion       = Read-JsonField $tsPackageJson "version"
        bundleVersion         = Read-TsConst $standaloneSrc "BUNDLE_VERSION"
        wireProfile           = Read-TsConst $standaloneSrc "WIRE_PROFILE"
        vocabularyFingerprint = Read-VocabularyFingerprint
        bundleSha256          = Get-Sha256 $embeddedBundle
    }

    # -NoNewline plus an explicit trailing newline in the text, and UTF8 with no
    # BOM: the F# reader is byte-comparing this file's own output on the next
    # check, so "what a PowerShell default happens to write" is not good enough.
    $text = Format-Fingerprint $fp
    [System.IO.File]::WriteAllText($embeddedFingerprint, $text, [System.Text.UTF8Encoding]::new($false))

    Write-Host ""
    Write-Host "Embedded renderer assets synced:"
    Write-Host ("  bundle      {0}  ({1:N0} bytes)" -f $embeddedBundle, (Get-Item $embeddedBundle).Length)
    Write-Host ("  renderer    {0}@{1}" -f $fp.rendererPackage, $fp.rendererVersion)
    Write-Host ("  bundle      v{0}, wire profile {1}" -f $fp.bundleVersion, $fp.wireProfile)
    Write-Host ("  vocabulary  {0}" -f $fp.vocabularyFingerprint)
    Write-Host ("  sha256      {0}" -f $fp.bundleSha256)
    Write-Host ""
    Write-Host "Commit both files in the same change-set as the renderer change that prompted the sync."
    exit 0
}

# ────────────────────────────────────────────────────────────── CHECK ────

$problems = @()

if (-not (Test-Path $embeddedBundle)) {
    $problems += "the embedded bundle is MISSING ($embeddedBundle). Run this script with -Sync."
}
if (-not (Test-Path $embeddedFingerprint)) {
    $problems += "the embedded fingerprint is MISSING ($embeddedFingerprint). Run this script with -Sync."
}

if ($problems.Count -eq 0) {
    $recordedVersion = Read-JsonField $embeddedFingerprint "rendererVersion"
    $recordedVocab = Read-JsonField $embeddedFingerprint "vocabularyFingerprint"
    $recordedProfile = Read-JsonField $embeddedFingerprint "wireProfile"
    $recordedSha = Read-JsonField $embeddedFingerprint "bundleSha256"
    $actualSha = Get-Sha256 $embeddedBundle

    Write-Host ("Embedded renderer  {0}@{1}, bundle sha256={2}" -f (Read-JsonField $embeddedFingerprint "rendererPackage"), $recordedVersion, $recordedSha)

    # The self-consistency check, which needs no sibling at all: does the
    # fingerprint describe the bytes sitting beside it? A hand-edited bundle is
    # the one drift that no amount of sibling checking would catch.
    if ($recordedSha -ne $actualSha) {
        $problems += "the fingerprint records sha256=$recordedSha but the embedded bundle hashes to $actualSha - the bundle was replaced without re-running this script."
    }

    # The always-answerable half: the vocabulary the assets were synced against
    # versus the one this build's renderer emits.
    $currentVocab = Read-VocabularyFingerprint
    if ($recordedVocab -eq $currentVocab) {
        Write-Host ("  vocabulary  matches   {0}" -f $currentVocab)
    }
    else {
        $problems += "the assets were synced against class vocabulary '$recordedVocab' but Theme.vocabularyFingerprint is now '$currentVocab' - the shipped stylesheet and the renderer emitting the classes have parted company."
    }

    if ($siblingPresent) {
        $siblingVersion = Read-JsonField $tsPackageJson "version"
        $siblingProfile = Read-TsConst $standaloneSrc "WIRE_PROFILE"

        if ($recordedVersion -eq $siblingVersion) {
            Write-Host ("  renderer    matches   {0}" -f $siblingVersion)
        }
        else {
            $problems += "the embedded bundle was built from @fuaran-ui/renderer $recordedVersion but the sibling is now $siblingVersion - re-sync."
        }

        if ($recordedProfile -ne $siblingProfile) {
            $problems += "the embedded bundle stamps wire profile '$recordedProfile' but the sibling now stamps '$siblingProfile' - re-sync."
        }

        if ($bundleBuilt) {
            # The strong check. Only available where the bundle has been built,
            # because it is a gitignored build output in fuaran-ts.
            $freshSha = Get-Sha256 $builtBundle
            if ($freshSha -eq $actualSha) {
                Write-Host "  bytes       identical to the built bundle"
            }
            else {
                $problems += "the embedded bundle differs from the freshly built one in fuaran-ts (embedded=$actualSha, built=$freshSha) - re-sync."
            }
        }
        else {
            # Reported, not silent: "nothing to check here" and "everything
            # checked" must not read alike.
            Write-Host "  bytes       NOT CHECKED - the fuaran-ts standalone bundle is not built in this checkout"
        }
    }
    else {
        Write-Host "  renderer    NOT CHECKED - fuaran-ts is absent from this checkout"
        Write-Host "  bytes       NOT CHECKED - fuaran-ts is absent from this checkout"
    }
}

if ($problems.Count -gt 0) {
    Write-Host ""
    Write-Error ("Embedded-renderer drift ({0}):`n  {1}`n`nThe embedded assets are GENERATED from fuaran-ts. Run ``pwsh ./scripts/sync-renderer-web.ps1 -Sync`` and commit the result in the same change-set as the change that prompted it." -f $problems.Count, ($problems -join "`n  "))
    exit 1
}

Write-Host ""
Write-Host "Embedded renderer assets are in sync."
exit 0
