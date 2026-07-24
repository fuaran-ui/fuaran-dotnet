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
    $cmd = Get-Command npm.cmd -CommandType Application -ErrorAction Stop | Select-Object -First 1
    & $cmd.Source @Arguments
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
    # Use cmd /c npm run dev so Windows resolves npm.cmd from the shell
    # (Start-Process + npm.cmd directly works too, but cmd /c is the
    # established workspace shape).
    $viteProc = Start-Process -FilePath "cmd" `
        -ArgumentList @("/c", "npm", "run", "dev") `
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
    Pop-Location
}
