#Requires -Version 7.0
# fuaran-dotnet — the proof leg (Phase 1754, the proof kit's first adopter).
#
# THE KIT IS A PINNED RELEASE ASSET. `proofs-kit.json` beside this file names it (kit, asset, url,
# sha256); the block below the declarations fetches it into proofs/.kit/ (or takes the local file
# -KitAsset / FUARAN_PROOFS_KIT_ASSET names: the offline path), REFUSES it unless its sha256 is the
# pin's, unpacks it fresh, and runs the engine (`kit/check-proof-leg.ps1` in the asset) from it with
# the asset's prover pin and its read-only models. Nothing of the kit is copied into this repository.
# This file is the caller: it declares what THIS repository has — the models, which of them are
# checked but not extracted, where the oracle host lives, and which Expecto families the host step
# runs. Read the engine's header in the asset for what the steps are, and the asset's `kit/README.md`
# for what the kit is and how a repository adopts it.
#
# WHAT THIS LEG PROVES, in one line: the round trip of the generated encoder and decoder over this
# repository's OWN wire vocabulary. `proofs/README.md` states the boundary — in particular that it
# says nothing about any host's hand-written decoder, which the shared corpus certifies instead.
#
# The flags are the kit's and are forwarded verbatim:
#   -Runs N          N cold-cache verifications of every model (CI asks for 3)
#   -Extract         rewrite the committed oracle/*.fs from a fresh extraction, then commit them
#   -SkipOracleHost  leave the Expecto families to the repository's own gate, which runs the suite
#   -Strict          the recorded run: a declaration defect is red (a cost overrun stays a finding)
#   -CacheDir <dir>  put the checked-module cache somewhere you name
#   -KitAsset <file> the kit asset as a local file instead of the pin's url (verified all the same)
#
# And one of this caller's own (fuaran#2012), because this repository now runs TWO legs:
#   -Leg all|vocabulary|program   which leg to run; `all` (the default) runs both, in that order.
#                    `program` alone is minutes; `vocabulary` alone is the hour-and-a-half
#                    VocabularyProofs check (see modules.json) — a session that touched only the
#                    program models re-checks them without paying for the other.
[CmdletBinding()]
param(
    [ValidateSet('all', 'vocabulary', 'program')]
    [string] $Leg = 'all',
    [switch] $Extract,
    [switch] $SkipOracleHost,
    [switch] $Strict,
    [string] $CacheDir,
    [int]    $Runs = 1,
    [string] $KitAsset
)

$ErrorActionPreference = 'Stop'

# ---- DECLARATION 1: the models -------------------------------------------------------------------
# Each is proofs/<name>.fst, or a kit model opened READ-ONLY from the asset's `models/` (never
# copied). Order matters where one model `open`s another: a model must follow what it opens. The
# kit's shared `Twin` (which `WireDecode` opens) is checked first, from `$kitShared`: it is no
# model's subject and carries no claim.
#   WireDecode       — the kit's decode-combinator model over Phase 135's `jval` value model, opened
#                      from the asset. Generic in its numeric carriers. `Vocabulary` opens it, so it
#                      comes first.
#   Vocabulary       — GENERATED from src/Fuaran.UI.Idl/idl.json by Fuaran.Core.Idl.Codegen's F*
#                      target: this vocabulary's types, its discriminated encoder and its
#                      tag-dispatch decoder. Opens WireDecode.
#   VocabularyProofs — GENERATED from the same walk: the round trip over `Vocabulary`
#                      (`dec_node (enc_node x) == Ok x`) and decoder totality. Opens both.
#
# Adding a model is adding its name here AND a budget entry to modules.json: nothing else is
# per-module. The line below is also READ AS TEXT by the `Proofs.Ladder` family
# (`../src/Fuaran.UI.Idl.Tests/FStarVocabularyTests.fs`), which matches
# `^\$modules\s*=\s*@\(...\)` against this file — so it stays ONE literal line.
$modules = @('WireDecode', 'Vocabulary', 'VocabularyProofs')
$kitShared = @('Twin')

# ---- DECLARATION 1b: the program models (fuaran#2012) — a SECOND leg ------------------------------
# The bounded program core's UI adapter (src/Fuaran.UI.Program, src/Fuaran.UI.Program.Server) moved
# here from the program repository, and the two proof claims that are ABOUT that adapter moved with
# it (../proofs.json `model-agrees-with-shipped-code`, `budget-model-agrees-with-shipped-code`).
# Their models are COPIES: proofs/program/<Module>.fst and the extractions under
# proofs/program/oracle/ are byte copies of the program repository's, declared in ../copies.json
# with that repository's files as canonical. This leg re-checks the copied models on the shared pin,
# re-extracts them and byte-diffs each extraction against its copied oracle, then runs the two
# differential hosts beside the adapter. A second invocation of the kit rather than more entries in
# the first, because the kit takes one proofs directory and one host project, and these models have
# their own of each — and their own runtime floor (oracle/Prims.fs), which is the program
# repository's rather than the kit's.
#   BoundedFold — the shared bounded fold over the action view, with the UI witness's fourteen arms.
#   Budget      — the interaction budget: the saturating arithmetic, the tree walk, the G2 gate.
# The line below is READ AS TEXT by the `Proofs.Ladder` family, like `$modules` above: ONE literal line.
$programModules = @('BoundedFold', 'Budget')

# ---- The extraction exemption, and why it covers everything here ---------------------------------
#
# An oracle exists so a differential can run the extracted model beside the PRODUCTION code over the
# same inputs. None of the three models has production code on this side to run beside: `WireDecode`
# models decode combinators this repository does not ship, and the generated pair models the decoder
# a GENERATOR emits into a consuming host rather than one any package here contains — every host's
# decoder is hand-written and certified against the shared wire-format corpus instead. Extracting
# them anyway would commit generated F# that nothing compiles, calls or compares, which is what an
# oracle is meant to be the opposite of.
#
# The exemption is NARROW and it is not a hole in the discipline: what step 2 buys for an extracted
# model — "the artefact is the model, byte for byte" — the generated pair gets from the GENERATION
# diff in the host step instead, one level further up, against the `idl.json` it is generated from;
# and `WireDecode` and `Twin` get it from the asset, whose sha256 the pin verifies before the leg runs.
$proofOnly = @('Twin', 'WireDecode', 'Vocabulary', 'VocabularyProofs')

# ---- DECLARATION 2: the host families ------------------------------------------------------------
# Separate invocations rather than one prefix filter, so each failure reads as what it is rather
# than as one red suite.
#   Proofs.Vocabulary — the GENERATION diff: each committed `.fst` held to a fresh generation from
#                       src/Fuaran.UI.Idl/idl.json, and the emitted header held to naming every kind
#                       it does not cover. A vocabulary that moves without a regeneration is
#                       VOCABULARY DRIFT, and this is where it is named.
#   Proofs.Ladder     — ../proofs.json against this tree.
$hostFilters = @(
    @{
        Filter  = 'Proofs.Vocabulary'
        Failure = 'the generated vocabulary (Proofs.Vocabulary) is RED — a committed F* model or proof script and src/Fuaran.UI.Idl/idl.json disagree, or the target refused a construct'
    }
    @{
        Filter  = 'Proofs.Ladder'
        Failure = 'the claims ladder (Proofs.Ladder) is RED — ../proofs.json and this tree disagree; the failing row and clause are named above'
    }
)

# ---- DECLARATION 3: where the host lives ---------------------------------------------------------
# Paths relative to the repository root. The two families live in the IDL suite, beside the
# regeneration triple they are the proof-side twin of, and that suite is seconds long and reads
# nothing outside this repository.
$hostProject = 'src/Fuaran.UI.Idl.Tests'
$hostProjectFile = 'src/Fuaran.UI.Idl.Tests/Fuaran.UI.Idl.Tests.fsproj'

# ---- DECLARATION 3b: the program leg's host and its two families (fuaran#2012) ------------------
# The differential hosts moved with the adapter, case for case and go-red case for go-red case: each
# runs the copied extraction beside the adapter's production code over the program specification's
# driver-semantics family (a sibling clone, or FUARAN_PROGRAM_SPEC) and an arm-complete corpus.
$programHostProject = 'src/Fuaran.UI.Program.Parity.Tests'
$programHostProjectFile = 'src/Fuaran.UI.Program.Parity.Tests/Fuaran.UI.Program.Parity.Tests.fsproj'
$programHostFilters = @(
    @{
        Filter  = 'Phase 1715 - the proved bounded fold as oracle'
        Failure = 'the bounded fold differential (model-agrees-with-shipped-code) is RED — the extracted BoundedFold model and the UI adapter disagree; the failing case is named above'
    }
    @{
        Filter  = 'Phase 1716 - the proved budget as oracle'
        Failure = 'the budget differential (budget-model-agrees-with-shipped-code) is RED — the extracted Budget model and the UI adapter disagree; the failing case is named above'
    }
)

# ---- nothing below here is per-repository --------------------------------------------------------

# ---- the kit: the pinned release asset, verified before anything runs ------------------------------
$kitPinFile = Join-Path $PSScriptRoot 'proofs-kit.json'
$kitPin = if (Test-Path $kitPinFile) { Get-Content $kitPinFile -Raw | ConvertFrom-Json } else { $null }
if ($null -eq $kitPin -or $kitPin.kind -ne 'proofsKitPin' -or -not $kitPin.asset -or -not $kitPin.url -or $kitPin.sha256 -notmatch '^[0-9a-f]{64}$') {
    Write-Host "==== proofs: $kitPinFile is not a kit pin (kind proofsKitPin, kit, asset, url, a 64-hex sha256)" -ForegroundColor Red
    exit 2
}
$kitRoot = Join-Path $PSScriptRoot '.kit'
New-Item -ItemType Directory -Force $kitRoot | Out-Null
$archive = Join-Path $kitRoot $kitPin.asset
if (-not $KitAsset) { $KitAsset = $env:FUARAN_PROOFS_KIT_ASSET }
if ($KitAsset) {
    $kitSource = [System.IO.Path]::GetFullPath($KitAsset)
    Copy-Item $kitSource $archive -Force
}
else {
    $kitSource = $kitPin.url
    $held = (Test-Path $archive) -and (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -eq $kitPin.sha256
    if (-not $held) { Invoke-WebRequest -Uri $kitPin.url -OutFile $archive -UseBasicParsing }
}
$kitSha = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($kitSha -ne $kitPin.sha256) {
    Remove-Item $archive -Force
    Write-Host "==== proofs: REFUSED the kit asset from $kitSource — its sha256 is $kitSha, the pin ($kitPinFile) is $($kitPin.sha256). Nothing ran." -ForegroundColor Red
    exit 2
}
# Unpacked fresh per invocation, so this run reads exactly the verified bytes; dead runs' trees are swept.
foreach ($stale in @(Get-ChildItem $kitRoot -Directory -Filter 'run-*' -ErrorAction SilentlyContinue)) {
    $stalePid = 0
    if ([int]::TryParse($stale.Name.Substring(4), [ref] $stalePid) -and $stalePid -ne $PID -and -not (Get-Process -Id $stalePid -ErrorAction SilentlyContinue)) {
        Remove-Item $stale.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
}
$kit = Join-Path $kitRoot "run-$PID"
if (Test-Path $kit) { Remove-Item $kit -Recurse -Force }
New-Item -ItemType Directory -Force $kit | Out-Null
[System.Formats.Tar.TarFile]::ExtractToDirectory($archive, $kit, $false)
Write-Host "==== proofs: kit $($kitPin.kit) ($($kitPin.asset), sha256 $kitSha) from $kitSource" -ForegroundColor Cyan
# The host step inherits this: a ladder family holding a row about a kit model reads that model's text
# from the verified asset here, since the model is not in this tree (kit/README.md, "Importing a theorem").
$env:FUARAN_PROOFS_KIT_DIR = $kit


$common = @{ Runs = $Runs }
if ($Extract) { $common.Extract = $true }
if ($SkipOracleHost) { $common.SkipOracleHost = $true }
if ($Strict) { $common.Strict = $true }
if ($CacheDir) { $common.CacheDir = $CacheDir }

$vocabularyArgs = @{
    Modules         = $kitShared + $modules
    PinFile         = Join-Path $kit 'fstar-pin.json'
    ModelDirs       = @(Join-Path $kit 'models')
    ProofOnly       = $proofOnly
    ProofsDir       = $PSScriptRoot
    HostProject     = $hostProject
    HostProjectFile = $hostProjectFile
    HostFilters     = $hostFilters
} + $common

# The program leg: its own proofs directory, oracle and cost declarations, the kit's prover pin,
# and the program repository's z3rlimit (60: the margin its leg runs these models under, so a green
# check here is the same claim as a green check there).
$programArgs = @{
    Modules         = $programModules
    ProofsDir       = (Join-Path $PSScriptRoot 'program')
    RepoRoot        = (Split-Path $PSScriptRoot -Parent)
    PinFile         = (Join-Path $kit 'fstar-pin.json')
    ZRlimit         = 60
    HostProject     = $programHostProject
    HostProjectFile = $programHostProjectFile
    HostFilters     = $programHostFilters
} + $common

# `&` and not `.`: a dot-sourced script's `exit` does NOT propagate to its caller, so a dot-source
# here would print the kit's red line and then return 0 — a green leg over a failed proof. The kit's
# template records that both forms were measured before this one was chosen; do not "simplify" it.
# The first red leg stops the run and its exit code is this script's.
if ($Leg -in @('all', 'vocabulary')) {
    & (Join-Path $kit 'kit/check-proof-leg.ps1') @vocabularyArgs
    if ($LASTEXITCODE -ne 0) { $code = $LASTEXITCODE; Remove-Item $kit -Recurse -Force -ErrorAction SilentlyContinue; exit $code }
}
if ($Leg -in @('all', 'program')) {
    # The program leg's two differential families compare over the program specification's
    # driver-semantics family, a sibling clone (or FUARAN_PROGRAM_SPEC). The specification is public
    # (Phase 2014), so an absent corpus FAILS the leg rather than skipping its hosts: the proofs CI
    # job checks it out, and a checkout without it has not run the claims this leg certifies.
    # `-SkipOracleHost` remains the one explicit way to run the models without their hosts.
    $programSpecRoot =
        if ($env:FUARAN_PROGRAM_SPEC) { $env:FUARAN_PROGRAM_SPEC.Trim() }
        else { Join-Path (Split-Path $PSScriptRoot -Parent) '../fuaran-program-spec' }
    if (-not $SkipOracleHost -and -not (Test-Path (Join-Path $programSpecRoot 'wire-fixtures/manifest.json'))) {
        Write-Host "==== proofs: program leg - RED: the program specification corpus is ABSENT at $programSpecRoot, so the two differential hosts cannot run. Clone https://github.com/Fuaran-Core/fuaran-program-specification beside this repository as 'fuaran-program-spec', or point FUARAN_PROGRAM_SPEC at a clone of it." -ForegroundColor Red
        Remove-Item $kit -Recurse -Force -ErrorAction SilentlyContinue
        exit 1
    }
    & (Join-Path $kit 'kit/check-proof-leg.ps1') @programArgs
    if ($LASTEXITCODE -ne 0) { $code = $LASTEXITCODE; Remove-Item $kit -Recurse -Force -ErrorAction SilentlyContinue; exit $code }
}
Remove-Item $kit -Recurse -Force -ErrorAction SilentlyContinue
exit 0
