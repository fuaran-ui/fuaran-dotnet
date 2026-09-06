#Requires -Version 7.0
<#
.SYNOPSIS
  Launch the Fuaran browser demo: Fable watcher + Vite dev server +
  browser URL. Stage-0 shape — three-process dance collapsed into one
  command.

.DESCRIPTION
  Steps:
    1. `npm ci` in samples/demo if `node_modules` is missing.
    2. `dotnet tool restore` to ensure Fable is invocable.
    3. Start the Fable watcher (`dotnet fable -o output --watch`) as a
       background process, writing `samples/demo/output/Main.js` and
       friends. Vite reads from that directory.
    4. Wait for `samples/demo/output/Main.js` to appear so Vite doesn't
       try to load the demo before Fable's first compile finishes.
    5. Start the Vite dev server (`npm run dev`) as a background process
       on port 24000 (per the workspace port allocation table).
    6. Wait for the Vite port to respond.
    7. Print the browser URL.

  Processes are left running. Stop them with `Stop-Process -Id <pids>`
  (the script prints the PID list at the end).

.PARAMETER NoBrowser
  Skip auto-opening the browser. URL is still printed.

.NOTES
  Sibling launcher conventions per the workspace `CLAUDE.md` — npm is
  invoked via the `Invoke-Npm` helper to dodge Node 22's `npm.ps1`
  Substring-slice bug.
#>
[CmdletBinding()]
param(
    [switch] $NoBrowser,
    [int] $TimeoutSeconds = 60
)

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot
$repoRoot = Split-Path -Parent $scriptDir
$demoDir = Join-Path $repoRoot "samples/demo"

if (-not (Test-Path $demoDir)) {
    Write-Error "Demo directory not found at $demoDir"
    exit 1
}

Push-Location $repoRoot

# Sibling launcher conventions — see workspace CLAUDE.md "Sibling launcher conventions (mandate)".
# Copy-pasted from the canonical body there; do not diverge without updating the workspace doc.
function Resolve-Npm {
    # The single resolution point for npm on this machine, shared by Invoke-Npm
    # below and by the Start-Process that launches Vite. See the comment in
    # Invoke-Npm for why the .ps1 shim must be bypassed; a background launch has
    # exactly the same problem, and `cmd /c npm run dev` sidestepped it only by
    # accident of going through cmd.exe rather than by resolving anything.
    [CmdletBinding()]
    param()
    (Get-Command npm.cmd -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
}

function Invoke-Npm {
    # Node 22.x ships an npm.ps1 shim that rebuilds args from the caller's command-line text via
    # Substring(InvocationName.Length). Called from inside another .ps1 as `& npm ci ...`, the
    # 3-char slice eats `& n` and npm sees `pm ci ...` — `Unknown command: "pm"`. Resolving npm.cmd
    # directly skips the shim.
    #
    # `Get-Command npm.cmd` returns EVERY npm.cmd on PATH — typically two (Program Files installer
    # shim + %APPDATA%\npm self-update shim). `$cmd.Source` would then be an array and `& $cmd.Source`
    # concatenates the paths into one bogus string. Pin to the first match — both shims behave alike.
    [CmdletBinding()]
    param([Parameter(ValueFromRemainingArguments = $true)] $Arguments)
    & (Resolve-Npm) @Arguments
}

function Write-Step {
    param([string] $message)
    Write-Host ""
    Write-Host "── $message ──────────────────────────────────────────────" -ForegroundColor Cyan
}

$allProcesses = New-Object System.Collections.Generic.List[object]

function Register-Proc {
    param([string] $name, [System.Diagnostics.Process] $proc)
    $allProcesses.Add([pscustomobject]@{ Name = $name; Pid = $proc.Id; Process = $proc }) | Out-Null
}

# On SUCCESS this script deliberately leaves the watcher and the dev server
# running and prints their PIDs — that is the whole point of a launcher. On
# FAILURE it used to leave them running too, silently: every `exit 1` below fired
# with the Fable watcher already started, so a run that could not reach Vite left
# an orphaned `dotnet fable --watch` holding the demo's output directory. Run the
# script twice and you had two, both writing the same files.
function Stop-Registered {
    param([string] $why)
    if ($allProcesses.Count -eq 0) { return }
    Write-Host ""
    Write-Host "Tearing down started processes ($why):" -ForegroundColor Yellow
    foreach ($entry in $allProcesses) {
        if (-not $entry.Process.HasExited) {
            Write-Host ("  stopping {0} (PID {1})" -f $entry.Name, $entry.Pid) -ForegroundColor Yellow
            try { Stop-Process -Id $entry.Pid -Force -ErrorAction Stop }
            catch { Write-Host ("    could not stop PID {0}: {1}" -f $entry.Pid, $_.Exception.Message) -ForegroundColor Red }
        }
    }
    $allProcesses.Clear()
}

# Set only once the demo is genuinely up. Until then, any exit is a failure exit
# and the finally block below tears down whatever was started.
$script:LaunchSucceeded = $false

try {
    # ─── 1. npm install ─────────────────────────────────────────────
    $nodeModules = Join-Path $demoDir "node_modules"
    if (-not (Test-Path $nodeModules)) {
        Write-Step "npm ci (samples/demo)"
        Push-Location $demoDir
        try { Invoke-Npm ci --no-fund --no-audit }
        finally { Pop-Location }
        if ($LASTEXITCODE -ne 0) { Write-Error "npm ci failed (exit $LASTEXITCODE)"; exit $LASTEXITCODE }
    }
    else {
        Write-Host "node_modules present — skipping npm ci" -ForegroundColor DarkGray
    }

    # ─── 2. dotnet tool restore ─────────────────────────────────────
    Write-Step "dotnet tool restore"
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { Write-Error "dotnet tool restore failed (exit $LASTEXITCODE)"; exit $LASTEXITCODE }

    # ─── 3. Start Fable watcher ─────────────────────────────────────
    Write-Step "dotnet fable -o output --watch (samples/demo)"
    $fableLog = Join-Path $demoDir "fable-watch.log"
    $fableProc = Start-Process -FilePath "dotnet" `
        -ArgumentList @("fable", "-o", "output", "--watch") `
        -WorkingDirectory $demoDir `
        -RedirectStandardOutput $fableLog `
        -RedirectStandardError "$fableLog.err" `
        -NoNewWindow -PassThru
    Register-Proc "Fable watcher" $fableProc

    # ─── 4. Wait for Fable's first compile ─────────────────────────
    $expectedJs = Join-Path $demoDir "output/Main.js"
    Write-Host "Waiting for Fable first compile -> $expectedJs"
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while (-not (Test-Path $expectedJs) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        if ($fableProc.HasExited) {
            Write-Error "Fable watcher exited before first compile (exit $($fableProc.ExitCode)); see $fableLog"
            exit 1
        }
    }
    if (-not (Test-Path $expectedJs)) {
        Write-Error "Fable did not produce $expectedJs within $TimeoutSeconds seconds; see $fableLog"
        exit 1
    }

    # ─── 5. Start Vite dev server ──────────────────────────────────
    Write-Step "npm run dev (samples/demo)"
    $viteLog = Join-Path $demoDir "vite-dev.log"
    # Resolve npm.cmd explicitly and start IT, per the workspace launcher
    # conventions — the same resolution `Invoke-Npm` uses, shared through
    # `Resolve-Npm`. The old `cmd /c npm run dev` avoided the npm.ps1 shim bug
    # only by routing through cmd.exe, which means it also inserted a `cmd.exe`
    # parent between this script and the dev server: `$viteProc.HasExited` and the
    # teardown below then watched the WRAPPER, not node, so a dead Vite could read
    # as a live process and a stopped wrapper could leave node running.
    $viteProc = Start-Process -FilePath (Resolve-Npm) `
        -ArgumentList @("run", "dev") `
        -WorkingDirectory $demoDir `
        -RedirectStandardOutput $viteLog `
        -RedirectStandardError "$viteLog.err" `
        -NoNewWindow -PassThru
    Register-Proc "Vite dev server" $viteProc

    # ─── 6. Wait for Vite port ─────────────────────────────────────
    $vitePort = 24000
    $url = "http://localhost:$vitePort/"
    Write-Host "Waiting for Vite at $url"
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $ready = $false
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
            if ($response.StatusCode -eq 200) { $ready = $true; break }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
        if ($viteProc.HasExited) {
            Write-Error "Vite exited before responding (exit $($viteProc.ExitCode)); see $viteLog"
            exit 1
        }
    }
    if (-not $ready) {
        Write-Error "Vite did not respond at $url within $TimeoutSeconds seconds; see $viteLog"
        exit 1
    }

    # From here the demo is up; the finally block leaves it running.
    $script:LaunchSucceeded = $true

    # ─── 7. Open browser + print summary ───────────────────────────
    if (-not $NoBrowser) {
        Start-Process $url | Out-Null
    }

    Write-Host ""
    Write-Host "✓ Fuaran demo is running." -ForegroundColor Green
    Write-Host "  URL:    $url"
    Write-Host "  Fable:  PID $($fableProc.Id) -> $fableLog"
    Write-Host "  Vite:   PID $($viteProc.Id) -> $viteLog"
    Write-Host ""
    Write-Host "  Stop: Stop-Process -Id $($fableProc.Id),$($viteProc.Id)" -ForegroundColor DarkGray
    Write-Host ""
}
finally {
    if (-not $script:LaunchSucceeded) {
        Stop-Registered "the launch did not complete"
    }
    Pop-Location
}
