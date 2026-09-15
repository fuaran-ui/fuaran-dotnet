#Requires -Version 7.0
# THE PROOF LEG, parameterised — the reusable half of `proofs/check.ps1` (Phase 155).
#
# This file is the KIT's engine. It knows how to run a proof leg; it knows nothing about which
# models a repository has, where its oracle host lives, or what its project is called. A
# repository's own `proofs/check.ps1` is the thin caller that supplies those (see
# `templates/check.ps1` beside this file), and it is the caller — not this script — that holds the
# module list, so the list stays where a reader and a sibling session expect to find it.
#
# EVERY MODULE in -Modules goes through the same three steps, and each step can fail on its own.
#   1. CHECK  — <ProofsDir>/<Module>.fst is verified by the PINNED F*/Z3 (-PinFile) with every SMT
#               query proved -Quake times over varying seeds and every escape hatch (assume /
#               admit) reported as an error. -Runs N repeats the whole check N times from a cold
#               cache — CI asks for 3, which is exit criterion 1 made literal. A run checks EVERY
#               module before the next run starts, so -Runs still means "N cold-cache
#               verifications of everything", as it did when there was one model.
#               Each module's wall clock is MEASURED AGAINST A DECLARED BUDGET (Phase 148):
#               -BudgetFile says what each module is expected to cost, every green line prints
#               the measured seconds beside that budget, and an overshoot is a named COST warning
#               rather than a failure — prover time varies by machine and by load, so the budget
#               is a smoke detector and not a gate. -Strict promotes every cost finding to a red
#               leg, for a session that wants one. A fixed CI job timeout is deliberately NOT what
#               this is: a timeout says a run died and nothing about which module.
#               The clock is also measured against a declared FLOOR (Phase 164), and that one
#               IS a gate: a module that verifies in less than its floorSeconds FAILS the leg on
#               the spot, naming the module and the time. The two directions are not symmetric.
#               An overshoot is a real measurement of a real cost; an undershoot means the
#               measuring apparatus is broken — almost always a second writer in the cache — so
#               everything after it would be measured with the same broken apparatus. -NoFloor
#               is the deliberate opt-out for a machine genuinely that fast.
#               The cache the cold runs use is PER INVOCATION (<WorkDir>/cache-<pid>, or
#               -CacheDir), created and removed by this script, so that "cold cache" cannot be
#               quietly falsified by another run in the same worktree. See "Running it" in the
#               adopting repository's proofs README for the 2026-09-14 incident that bought both.
#   2. EXTRACT — each checked model is extracted to F# and DIFFED against its committed oracle
#               (<OracleDir>/<Module>.fs). A difference fails: the oracle the suite runs must be
#               the model the theorem is about, byte for byte. -Extract overwrites the committed
#               files with the fresh extractions instead (then commit them). A model named in
#               -ProofOnly is EXEMPT and says so on its own line: an oracle exists so a
#               differential can run the extracted model beside the production code, so a model
#               with no production code to run beside earns no oracle, and committing one would
#               commit generated F# that nothing compiles, calls or compares. The exemption is
#               the caller's to declare and to justify — what step 2 buys for the other models
#               ("the artefact is the model, byte for byte") an exempt one must get some other
#               way, one level further up, and the caller says where.
#   3. HOST   — the -HostFilters the caller declared, each its own invocation of the host test
#               project (-HostProject / -HostProjectFile) with its own failure message. Separate
#               invocations rather than one prefix filter, so two failures read as what they are
#               rather than as one red suite. -SkipOracleHost leaves them all to the repository's
#               own gate, which already runs the whole suite.
#
# The prover is resolved from $env:FSTAR_HOME (a release directory holding bin/fstar.exe), else
# from <ProofsDir>/.fstar/ (a previous install by this script), else DOWNLOADED from the pinned
# GitHub release, hash-verified, and unpacked there. Both directories, and <WorkDir>, are expected
# to be gitignored by the adopting repository.
# Only the Windows release is pinned by the pin file's shape — the leg runs on the Windows CI
# runner and the Windows dev machines; on another OS set FSTAR_HOME to a matching release and the
# pin's version check still applies.
#
# Public behaviour is the contract. Every line this script prints, and every exit code it returns,
# is what the pre-kit `check.ps1` printed and returned — a repository's tests may parse them.
[CmdletBinding()]
param(
    # The models, in the order they are to be checked. The CALLER owns this list.
    [Parameter(Mandatory)][string[]] $Modules,
    # Models that are CHECKED but not EXTRACTED — see step 2 in the header. A narrow, declared
    # exemption, never a default.
    [string[]] $ProofOnly = @(),
    # The directory holding the .fst models. Everything else defaults relative to it.
    [Parameter(Mandatory)][string] $ProofsDir,
    # The repository root the host step runs from. Defaults to the parent of -ProofsDir.
    [string] $RepoRoot,
    # The pinned prover declaration. Defaults to <ProofsDir>/fstar-pin.json.
    [string] $PinFile,
    # The per-module cost budgets and time floors. Defaults to <ProofsDir>/modules.json.
    [string] $BudgetFile,
    # The committed extractions the fresh ones are diffed against. Defaults to <ProofsDir>/oracle.
    [string] $OracleDir,
    # Where the checked-module cache and the fresh extractions go. Defaults to <ProofsDir>/obj.
    [string] $WorkDir,
    # The host test project, as a path relative to -RepoRoot: the directory `dotnet run --project`
    # takes, and the .fsproj `dotnet build` takes.
    [string] $HostProject,
    [string] $HostProjectFile,
    # One entry per host invocation: @{ Filter = '<Expecto filter>'; Failure = '<message on red>' }.
    # An empty list means there is no host step, which is a legitimate shape for a repository whose
    # models have no differential yet.
    [hashtable[]] $HostFilters = @(),
    # The prover flags the leg is defined by. Defaults are the values the leg was cut with.
    [int] $Quake = 3,
    [int] $ZRlimit = 40,
    [switch] $Extract,
    [switch] $SkipOracleHost,
    [switch] $Strict,
    [switch] $NoFloor,
    [string] $CacheDir,
    [int]    $Runs = 1
)

$ErrorActionPreference = 'Stop'

$ProofsDir = [System.IO.Path]::GetFullPath($ProofsDir)
if (-not (Test-Path $ProofsDir)) { throw "the proofs directory '$ProofsDir' does not exist" }
if (-not $RepoRoot) { $RepoRoot = Split-Path $ProofsDir -Parent }
if (-not $PinFile) { $PinFile = Join-Path $ProofsDir 'fstar-pin.json' }
if (-not $BudgetFile) { $BudgetFile = Join-Path $ProofsDir 'modules.json' }
if (-not $OracleDir) { $OracleDir = Join-Path $ProofsDir 'oracle' }
if (-not $WorkDir) { $WorkDir = Join-Path $ProofsDir 'obj' }

# The budget file's own name, so that every message about it names the file the caller declared
# rather than a name baked in here.
$budgetName = Split-Path $BudgetFile -Leaf

Set-Location $ProofsDir
$LASTEXITCODE = 0

# The per-invocation cache this run owns, once section 3 has resolved one. Named here, above
# Fail, so that EVERY exit path removes it: PowerShell resolves a function body at call time, so
# Fail can call Remove-InvocationCache from before its definition. A run killed outright (a turn
# boundary, Ctrl-C) still leaves its directory behind — nothing inside a process can promise
# otherwise, which is why section 3 sweeps dead runs' directories at startup instead of trusting
# this. Leaving one behind is harmless in any case: it belongs to a pid, so it poisons nobody.
$script:invocationCache = $null
$script:invocationCacheIsOurs = $false

function Remove-InvocationCache {
    if ($script:invocationCacheIsOurs -and $script:invocationCache -and (Test-Path $script:invocationCache)) {
        Remove-Item $script:invocationCache -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Fail([string] $message, [int] $code = 1) {
    Remove-InvocationCache
    Write-Host "==== proofs: $message" -ForegroundColor Red
    exit $code
}

# A terminating error under $ErrorActionPreference = 'Stop' bypasses Fail; this does not.
trap { Remove-InvocationCache; break }

if (-not (Test-Path $PinFile)) { Fail "the pin file '$PinFile' is missing — it declares the prover this leg is defined by" }
$pin = Get-Content $PinFile -Raw | ConvertFrom-Json
$pinnedVersion = $pin.fstar.TrimStart('v')

# ---- 1. resolve the prover ---------------------------------------------------------------------

function Resolve-FStar {
    if ($env:FSTAR_HOME) {
        $exe = Join-Path $env:FSTAR_HOME 'bin/fstar.exe'
        if (-not (Test-Path $exe)) { Fail "FSTAR_HOME is set to '$env:FSTAR_HOME' but bin/fstar.exe is not there" }
        return $exe
    }

    $local = Join-Path $ProofsDir '.fstar/fstar/bin/fstar.exe'
    if (Test-Path $local) { return $local }

    if (-not $IsWindows) {
        Fail "no FSTAR_HOME and this is not Windows — only the Windows release is pinned ($(Split-Path $PinFile -Leaf)); set FSTAR_HOME to an F* $($pin.fstar) release" 2
    }

    $asset = $pin.windows.asset
    $dir = Join-Path $ProofsDir '.fstar'
    New-Item -ItemType Directory -Force $dir | Out-Null
    $zip = Join-Path $dir $asset

    if (-not (Test-Path $zip)) {
        Write-Host "==== proofs: downloading the pinned prover $($pin.fstar) ($asset)" -ForegroundColor Cyan
        Invoke-WebRequest -Uri $pin.windows.url -OutFile $zip
    }

    $hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
    if ($hash -ne $pin.windows.sha256) {
        Remove-Item $zip -Force
        Fail "the downloaded $asset does not match the pinned sha256 (got $hash, pinned $($pin.windows.sha256)); it was deleted — re-run to fetch again"
    }

    Write-Host "==== proofs: unpacking $asset" -ForegroundColor Cyan
    Expand-Archive -Path $zip -DestinationPath $dir -Force
    if (-not (Test-Path $local)) { Fail "unpacked $asset but found no fstar/bin/fstar.exe under $dir" }
    return $local
}

$fstar = Resolve-FStar
$versionLine = (& $fstar --version 2>&1 | Select-Object -First 1)
if ($versionLine -ne "F* $pinnedVersion") {
    Fail "the resolved prover reports '$versionLine' but the pin is 'F* $pinnedVersion' ($fstar)"
}

$fstarHome = Split-Path (Split-Path $fstar -Parent) -Parent
$z3dir = Get-ChildItem (Join-Path $fstarHome 'lib/fstar') -Directory -Filter 'z3-*' | Select-Object -First 1
if ($null -eq $z3dir) { Fail "no bundled Z3 under $fstarHome/lib/fstar" }
if ($z3dir.Name -ne "z3-$($pin.z3)") { Fail "the bundled Z3 is $($z3dir.Name); the pin is z3-$($pin.z3)" }
Write-Host "==== proofs: $versionLine, bundled $($z3dir.Name), at $fstarHome" -ForegroundColor Cyan

# ---- 2. the declared cost budgets ----------------------------------------------------------------

# The budget file says what each module is expected to cost on a cold run. It is a committed
# declared artefact, so its ABSENCE is a defect and fails here; a mismatch between it and -Modules
# is a cost finding rather than a failure, because a sibling adding a model should not have their
# leg go red for a budget nobody could have measured yet — the finding names the module and what
# to do.
$costFindings = [System.Collections.Generic.List[string]]::new()

function Add-CostFinding([string] $message) {
    $script:costFindings.Add($message)
    Write-Host "==== proofs: COST — $message" -ForegroundColor Yellow
}

if (-not (Test-Path $BudgetFile)) {
    Fail "$budgetName is missing — it declares each module's cost budget (see the README's 'Running it')"
}

# The SHAPE is a failure where a mismatch is a finding, and the difference is whether the file can
# still be read as a budget at all. An entry with no `budgetSeconds` would otherwise arrive as 0 and
# every run would be infinitely over it — a flood of findings, and a division by zero rendering the
# percentage. A declared artefact is held to its shape by the code that consumes it.
# The FLOOR beside it (Phase 164) is held to its shape the same way WHEN IT IS THERE, and is a
# cost finding when it is ABSENT — a sibling adding a model should no more go red for a floor
# nobody has measured than for a budget nobody has measured. An absent floor degrades to exactly
# the pre-164 behaviour for that module, which is the safe direction; a floor of 0 is legal and
# means "this module genuinely checks in about a second", which is NOT the same statement as an
# absent one and reads differently in the file.
$budgets = @{}
$floors = @{}
foreach ($entry in (Get-Content $BudgetFile -Raw | ConvertFrom-Json).modules) {
    $name = $entry.module
    if ([string]::IsNullOrWhiteSpace($name)) { Fail "$budgetName carries an entry with no module name" }
    if ($budgets.ContainsKey($name)) { Fail "$budgetName declares '$name' twice" }

    $declaredBudget = $entry.budgetSeconds
    if ($declaredBudget -isnot [int] -and $declaredBudget -isnot [long] -and $declaredBudget -isnot [double]) {
        Fail "$budgetName entry '$name' has no numeric budgetSeconds"
    }
    if ([int]$declaredBudget -lt 1) { Fail "$budgetName entry '$name' has a budgetSeconds of $declaredBudget — a budget is a positive number of seconds" }

    $budgets[$name] = [int]$declaredBudget

    $declaredFloor = $entry.floorSeconds
    if ($null -ne $declaredFloor) {
        if ($declaredFloor -isnot [int] -and $declaredFloor -isnot [long] -and $declaredFloor -isnot [double]) {
            Fail "$budgetName entry '$name' has a non-numeric floorSeconds"
        }
        if ([int]$declaredFloor -lt 0) { Fail "$budgetName entry '$name' has a floorSeconds of $declaredFloor — a floor is a non-negative number of seconds" }
        if ([int]$declaredFloor -ge [int]$declaredBudget) {
            Fail "$budgetName entry '$name' has a floorSeconds of $declaredFloor at or above its budgetSeconds of $([int]$declaredBudget) — no run could satisfy both"
        }
        $floors[$name] = [int]$declaredFloor
    }
}

foreach ($module in $Modules) {
    if (-not $budgets.ContainsKey($module)) {
        Add-CostFinding "$module is checked by the leg and $budgetName declares no budget for it — time a cold run, budget it per the file's seeding rule, and cite your phase"
    }
    elseif (-not $floors.ContainsKey($module)) {
        Add-CostFinding "$module is checked by the leg and $budgetName declares no floorSeconds for it — nothing can tell an implausibly fast run of it from a real one; seed one per the file's floorSeeding rule and cite your phase"
    }
}
foreach ($declared in $budgets.Keys) {
    if ($Modules -notcontains $declared) {
        Add-CostFinding "$budgetName budgets '$declared', which the leg does not check — drop the entry, or add the model to check.ps1's module list"
    }
}

# ---- 3. check, -Runs times from a cold cache -------------------------------------------------------
#
# THE CACHE IS PER INVOCATION (Phase 164). Until then it was one constant directory, obj/cache,
# and clearing it at the head of a run only makes that run cold if nothing else is writing there.
# On 2026-09-14 something was: an orphaned background check.ps1 in the same worktree kept writing
# .checked files, and the replacement run reported TreeOps 0s, Skeleton 0s, Chain 0s and printed
# `==== proofs: green`. Nothing in the script could see it — it cleared the directory it was about
# to use, which a second writer defeats a moment later. A directory named for this process cannot
# be written into by another invocation at all, so the property the -Runs loop needs holds by
# construction rather than by nobody else running.

$out = Join-Path $WorkDir 'out'
$cacheRoot = $WorkDir

if ($CacheDir) {
    # A caller-named directory is CLEARED at each run head, exactly as the default one is —
    # otherwise -CacheDir would silently mean "warm", which is the opposite of what this section
    # is for. So refuse one holding anything that is not a checked-module file: the flag is for
    # naming where the cache goes, and pointing it at a directory with other contents in it would
    # delete them. It is not removed at exit; the caller named it, so the caller keeps it.
    # `[IO.Path]::Combine` and not `Join-Path`: PowerShell's Join-Path CONCATENATES a rooted second
    # argument, so `-CacheDir C:\somewhere` resolved to `<proofs>\C:\somewhere` and the run died a
    # second later with a path nobody would recognise as their own argument. .NET's Combine returns
    # the second path when it is rooted and joins when it is not, so the relative case — which is
    # what the flag's "cleared before every run" contract is written against — is unchanged.
    # (Inherited from Phase 164 and found by using the flag; a kit must not ship it.)
    $cache = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine((Get-Location).Path, $CacheDir))
    if (Test-Path $cache) {
        $foreign = Get-ChildItem $cache -Force | Where-Object { $_.PSIsContainer -or $_.Name -notlike '*.checked*' }
        if ($foreign) {
            Fail "-CacheDir '$cache' holds $($foreign.Count) entry/entries that are not checked-module files (first: $($foreign[0].Name)) — this script CLEARS its cache directory before every run, so it will only use one that is empty or holds nothing but *.checked files"
        }
    }
    $script:invocationCacheIsOurs = $false
}
else {
    # Sweep the directories left by runs that were killed before they could remove their own. Only
    # ones whose pid is gone, so a concurrent invocation's cache is never touched — which is the
    # whole point of the naming. A pid that has since been reused just leaves a directory behind;
    # that costs nothing, where deleting a live run's cache would cost the exact incident above.
    if (Test-Path $cacheRoot) {
        foreach ($stale in (Get-ChildItem $cacheRoot -Directory -Filter 'cache-*' -ErrorAction SilentlyContinue)) {
            $stalePid = 0
            if (-not [int]::TryParse($stale.Name.Substring('cache-'.Length), [ref] $stalePid)) { continue }
            if ($stalePid -eq $PID) { continue }
            if (Get-Process -Id $stalePid -ErrorAction SilentlyContinue) { continue }
            Write-Host "==== proofs: sweeping $($stale.Name), left by a run that did not finish" -ForegroundColor DarkGray
            Remove-Item $stale.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    $cache = Join-Path $cacheRoot "cache-$PID"
    $script:invocationCacheIsOurs = $true
}
$script:invocationCache = $cache
Write-Host "==== proofs: cache $cache$(if (-not $script:invocationCacheIsOurs) { ' (-CacheDir; left in place at exit)' })" -ForegroundColor Cyan

if ($NoFloor) {
    Write-Host "==== proofs: -NoFloor — the per-module time floors in $budgetName are NOT enforced on this run" -ForegroundColor Yellow
}

for ($run = 1; $run -le $Runs; $run++) {
    if (Test-Path $cache) { Remove-Item $cache -Recurse -Force }
    New-Item -ItemType Directory -Force $cache | Out-Null

    foreach ($module in $Modules) {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        & $fstar --z3rlimit $ZRlimit --quake $Quake --report_assumes error --cache_checked_modules --cache_dir $cache "$module.fst"
        if ($LASTEXITCODE -ne 0) { Fail "$module.fst did NOT verify (run $run of $Runs)" $LASTEXITCODE }

        $seconds = [int]$sw.Elapsed.TotalSeconds
        $budget = if ($budgets.ContainsKey($module)) { $budgets[$module] } else { $null }
        $cost = if ($null -eq $budget) { "${seconds}s (no budget)" } else { "${seconds}s/${budget}s" }
        Write-Host "==== proofs: $module.fst verified — run $run of $Runs, $cost, every query $Quake/$Quake under --quake" -ForegroundColor Green

        # The CEILING. Restored by Phase 155: Phase 164 deleted this block when it added the floor
        # gate below, so from a27afbc until now an overshoot printed nothing, `$costFindings` was
        # never populated from a measured time, and `-Strict` had nothing to promote — while the
        # budget file's comments and the README both went on describing a ceiling that fired. A
        # measured 32s against a 30s budget said nothing at all. It is a WARNING and the run
        # continues, which is the half the floor below is deliberately not.
        if ($null -ne $budget -and $seconds -gt $budget) {
            Add-CostFinding "$module.fst took ${seconds}s against its ${budget}s budget on run $run of $Runs — $($seconds - $budget)s over, $([int](100 * $seconds / $budget))% of budget"
        }

        # The floor fails HERE rather than joining the cost findings at the end, and the asymmetry
        # with the ceiling just above it is deliberate. An overshoot is a true measurement of a
        # true cost, so the run should continue and produce the rest of the evidence. An
        # undershoot says the measurement itself is not to be believed — the cache was not cold —
        # and every module after it is measured by the same apparatus, so carrying on would print
        # more green lines that a reader is entitled to read as evidence and that are not.
        if (-not $NoFloor -and $floors.ContainsKey($module) -and $seconds -lt $floors[$module]) {
            Fail ("$module.fst verified in ${seconds}s on run $run of $Runs, under its $($floors[$module])s floor — that is not a cold verification. " +
                'Almost always a second writer in the cache directory (see section 3). Check for another check.ps1 or fstar process against this worktree; ' +
                "if this machine really is that fast, re-seed the floor per $budgetName floorSeeding and cite your phase, or pass -NoFloor for this run.")
        }
    }
}

# ---- 4. extract, and hold the committed oracle to the model ---------------------------------------

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

foreach ($module in $Modules) {
    if ($ProofOnly -contains $module) {
        Write-Host "==== proofs: $module is checked, not extracted — no oracle runs it (see `$proofOnly)" -ForegroundColor Cyan
        continue
    }

    & $fstar --cache_checked_modules --cache_dir $cache --codegen FSharp --extract $module --odir $out "$module.fst"
    if ($LASTEXITCODE -ne 0) { Fail "extraction of $module to F# failed" $LASTEXITCODE }

    $fresh = Join-Path $out "$module.fs"
    $committed = Join-Path $OracleDir "$module.fs"
    if (-not (Test-Path $fresh)) { Fail "extraction produced no $module.fs under $out" }

    # Compare LF-normalised: the extractor writes LF and the repository pins LF, but a checkout
    # with autocrlf on would otherwise fail this for a reason that is not the model.
    $freshText = (Get-Content $fresh -Raw).Replace("`r`n", "`n")
    $committedText = if (Test-Path $committed) { (Get-Content $committed -Raw).Replace("`r`n", "`n") } else { '' }

    if ($Extract) {
        [System.IO.File]::WriteAllText($committed, $freshText, [System.Text.UTF8Encoding]::new($false))
        Write-Host "==== proofs: wrote the fresh extraction to oracle/$module.fs — commit it" -ForegroundColor Yellow
    }
    elseif ($freshText -ne $committedText) {
        Write-Host "==== proofs: the committed oracle (oracle/$module.fs) is NOT the extraction of $module.fst." -ForegroundColor Red
        Write-Host "     Fresh extraction: $fresh" -ForegroundColor Red
        Write-Host "     Re-extract with: pwsh ./proofs/check.ps1 -Extract   (then commit the result)" -ForegroundColor Red
        Fail "oracle drift"
    }
    else {
        Write-Host "==== proofs: oracle/$module.fs is byte-identical to a fresh extraction" -ForegroundColor Green
    }
}

# ---- 5. the oracle host --------------------------------------------------------------------------

if (-not $SkipOracleHost -and $HostFilters.Count -gt 0) {
    if (-not $HostProject -or -not $HostProjectFile) {
        Fail 'the caller declared host filters but no -HostProject / -HostProjectFile to run them in'
    }
    Push-Location $RepoRoot
    try {
        dotnet build $HostProjectFile --nologo
        if ($LASTEXITCODE -ne 0) { Fail "the test project did not build" $LASTEXITCODE }

        # `$hostStep` and not `$host`: `$Host` is a PowerShell automatic variable and a foreach
        # over it is a hard error, which is the kind of thing that only shows up on the first red
        # run rather than on the first green one.
        foreach ($hostStep in $HostFilters) {
            $filter = $hostStep.Filter
            dotnet run --project $HostProject --no-build -- --filter $filter
            if ($LASTEXITCODE -ne 0) { Fail $hostStep.Failure $LASTEXITCODE }
        }
    }
    finally { Pop-Location }
}

# ---- 6. the cost verdict ---------------------------------------------------------------------------
#
# Last, so that every finding is in hand and none of them can stop the evidence being produced: a
# leg that went red on the clock before running the differential would hide a real disagreement
# behind a slow afternoon.

if ($costFindings.Count -gt 0) {
    Write-Host "==== proofs: $($costFindings.Count) cost finding(s) against the budgets in ${budgetName}:" -ForegroundColor Yellow
    foreach ($f in $costFindings) { Write-Host "     $f" -ForegroundColor Yellow }
    Write-Host '     A budget is a smoke detector, not a gate: prover time varies by machine and by load,' -ForegroundColor Yellow
    Write-Host '     so one overshoot on a busy machine is noise and a persistent one is a regression.' -ForegroundColor Yellow
    Write-Host '     Bumping a budget is deliberate: new budgetSeconds + measuredSeconds + your phase in' -ForegroundColor Yellow
    Write-Host "     $budgetName, and a note saying what grew. See the README, ""Running it""." -ForegroundColor Yellow
    if ($Strict) { Fail "the cost budget is exceeded and -Strict is on ($($costFindings.Count) finding(s) above)" }
}

Remove-InvocationCache
Write-Host '==== proofs: green' -ForegroundColor Green
exit 0
