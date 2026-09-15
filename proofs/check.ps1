#Requires -Version 7.0
# fuaran-dotnet — the proof leg (Phase 1754, the proof kit's first adopter).
#
# THE ENGINE IS `kit/check-proof-leg.ps1` and this file is the caller: it declares what THIS
# repository has — the models, which of them are checked but not extracted, where the oracle host
# lives, and which Expecto families the host step runs — and the kit runs the leg. Read the kit
# script's header for what the three steps are and why each is shaped the way it is; read
# `kit/README.md` in the kit's own repository for what the kit is and how a repository adopts it.
# Nothing about the mechanism lives here, and nothing about this repository lives in the kit.
#
# WHAT THIS LEG PROVES, in one line: the round trip of the generated encoder and decoder over this
# repository's OWN wire vocabulary. `proofs/README.md` states the boundary — in particular that it
# says nothing about any host's hand-written decoder, which the shared corpus certifies instead.
#
# The flags are the kit's and are forwarded verbatim:
#   -Runs N          N cold-cache verifications of every model (CI asks for 3)
#   -Extract         rewrite the committed oracle/*.fs from a fresh extraction, then commit them
#   -SkipOracleHost  leave the Expecto families to the repository's own gate, which runs the suite
#   -Strict          promote every cost finding to a red leg
#   -NoFloor         do not enforce the per-module time floors declared in modules.json
#   -CacheDir <dir>  put the checked-module cache somewhere you name
[CmdletBinding()]
param(
    [switch] $Extract,
    [switch] $SkipOracleHost,
    [switch] $Strict,
    [switch] $NoFloor,
    [string] $CacheDir,
    [int]    $Runs = 1
)

$ErrorActionPreference = 'Stop'

# ---- DECLARATION 1: the models -------------------------------------------------------------------
# Each is proofs/<name>.fst. Order matters where one model `open`s another: a model must follow what
# it opens.
#   WireDecode       — the kit's decode-combinator model over Phase 135's `jval` value model, COPIED
#                      from the kit's repository and declared in ../copies.json. Generic in its
#                      numeric carriers. `Vocabulary` opens it, so it comes first.
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
# and `WireDecode` gets it from `../copies.json`, which holds it byte-equivalent to the kit's own.
$proofOnly = @('WireDecode', 'Vocabulary', 'VocabularyProofs')

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

# ---- nothing below here is per-repository --------------------------------------------------------

$legArgs = @{
    Modules         = $modules
    ProofOnly       = $proofOnly
    ProofsDir       = $PSScriptRoot
    HostProject     = $hostProject
    HostProjectFile = $hostProjectFile
    HostFilters     = $hostFilters
    Runs            = $Runs
}
if ($Extract) { $legArgs.Extract = $true }
if ($SkipOracleHost) { $legArgs.SkipOracleHost = $true }
if ($Strict) { $legArgs.Strict = $true }
if ($NoFloor) { $legArgs.NoFloor = $true }
if ($CacheDir) { $legArgs.CacheDir = $CacheDir }

# `&` and not `.`: a dot-sourced script's `exit` does NOT propagate to its caller, so a dot-source
# here would print the kit's red line and then return 0 — a green leg over a failed proof. The kit's
# template records that both forms were measured before this one was chosen; do not "simplify" it.
& (Join-Path $PSScriptRoot 'kit/check-proof-leg.ps1') @legArgs
exit $LASTEXITCODE
