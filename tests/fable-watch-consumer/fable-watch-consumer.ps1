#Requires -Version 7.0
<#
.SYNOPSIS
  The watch-mode leg (Phase 2174): compile a client-tier consumer under `dotnet fable watch` and
  assert the first compile prints no error and reaches the command Fable is told to run after it.

.DESCRIPTION
  WHAT IT CATCHES. Fable's watch mode reports diagnostics its one-shot compile does not. Fuaran.UI
  0.92.0 carried two "Cannot get type info of generic parameter T" errors — `binding.selectionField`
  in `Fuaran.fs` and the decoder's `Selection` arm in `JsonDecode.fs`, each a non-inline generic
  function reaching `Binding.projectSelectionField`, which reads `typeof<'T>` under Fable — that only
  `fable watch` printed. Fable does not start its `--run` command after a compile that reported an
  error, so a consumer whose dev script is `fable watch --run vite` never reached Vite, while every
  one-shot compile in this repository, the Fable stage's included, stayed green. The one-shot
  compile emitted `const target = null` at both sites, so the cell coercion the projection exists
  for silently never ran either.

  WHAT IT DOES. Restores `FableWatchConsumer.fsproj` (ProjectReferences to `Fuaran.UI`,
  `Fuaran.UI.Ops` and `Fuaran.UI.Renderer`, under this repository's own MSBuild properties), then
  starts `dotnet fable watch` over it with `--run` naming a tiny script that writes a marker file.
  It reads Fable's output until the watcher reports it is watching, gives the `--run` command a
  bounded moment to write the marker, then stops the whole process tree. A watcher never exits on
  its own, so stopping it is the script's job on every path, success or not.

  WHAT GREEN MEANS. All three of: Fable printed its "Watching" line (the first compile finished),
  it printed no `error` diagnostic, and the marker exists (it ran the `--run` command, which is the
  consumer's "reaches Vite"). A watcher that hangs, dies, or never runs the command fails by name.

  RUN IT STANDALONE from anywhere:  pwsh ./tests/fable-watch-consumer/fable-watch-consumer.ps1
  `tests/fable-laws/fable-check.ps1` runs it as part of the Fable stage. Exit 0 = green.
#>
[CmdletBinding()]
param(
    # The longest the first watch compile may take before the leg fails as hung.
    [int] $TimeoutSeconds = 900,
    # Keep the emitted JavaScript and the captured output in the scratch root for inspection.
    [switch] $KeepOutput
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# Seeded because every guard below reads it: `$LASTEXITCODE` is `$null` until a native command runs.
$global:LASTEXITCODE = 0

$project = Join-Path $PSScriptRoot 'FableWatchConsumer.fsproj'

function Fail([string] $message) {
    Write-Host "==== fable-watch-consumer: FAILED — $message" -ForegroundColor Red
    exit 1
}

# ── Scratch ─────────────────────────────────────────────────────────────────

# Outside the repository (MAX_PATH, and nothing here is ever committed), keyed on this script's
# location so two worktrees never wipe each other's output.
$sha = [Security.Cryptography.SHA256]::Create()
$treeKey = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($PSScriptRoot.ToLowerInvariant()))) -replace '-', '').Substring(0, 12).ToLowerInvariant()
$sha.Dispose()
$scratch = Join-Path ([IO.Path]::GetTempPath()) "fuaran-fable-watch-consumer-$treeKey"
$outDir = Join-Path $scratch 'out'
$marker = Join-Path $scratch 'run-reached.marker'
$runScript = Join-Path $scratch 'mark.ps1'
$logPath = Join-Path $scratch 'watch.log'
Remove-Item -Recurse -Force -LiteralPath $scratch -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $scratch | Out-Null

# The `--run` command: what a consumer's Vite is to its dev script. pwsh is the one runtime this
# script already guarantees, so the leg needs no Node.
Set-Content -LiteralPath $runScript -Encoding utf8NoBOM -Value @'
param([string] $Path)
Set-Content -LiteralPath $Path -Value 'reached' -Encoding utf8NoBOM
'@

# ── Restore ─────────────────────────────────────────────────────────────────

Write-Host "==== fable-watch-consumer: restoring $project"
& dotnet restore $project --nologo
if ($LASTEXITCODE -ne 0) { Fail "restore exited $LASTEXITCODE" }

# ── The watch compile ───────────────────────────────────────────────────────

$pwshPath = (Get-Process -Id $PID).Path
$psi = [Diagnostics.ProcessStartInfo]::new('dotnet')
foreach ($arg in @('fable', 'watch', $project, '-o', $outDir, '--noCache', '--run', $pwshPath, '-NoProfile', '-File', $runScript, $marker)) {
    $psi.ArgumentList.Add($arg)
}
$psi.WorkingDirectory = $PSScriptRoot
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.RedirectStandardInput = $true

$proc = [Diagnostics.Process]::new()
$proc.StartInfo = $psi

$captured = [Collections.Generic.List[string]]::new()
$watching = $false
$exitedEarly = $false
$timedOut = $false
$started = $false
$clock = [Diagnostics.Stopwatch]::StartNew()

Write-Host "==== fable-watch-consumer: dotnet fable watch (bounded at ${TimeoutSeconds}s)"
try {
    $started = $proc.Start()
    # Both streams read line by line through pending tasks polled from this thread: no event
    # handler runs on another thread, and neither pipe can fill and stall the watcher.
    $readers = @($proc.StandardOutput, $proc.StandardError)
    $pending = @($readers | ForEach-Object { $_.ReadLineAsync() })

    $watchingAt = $null
    while ($true) {
        for ($i = 0; $i -lt $readers.Count; $i++) {
            while ($null -ne $pending[$i] -and $pending[$i].IsCompleted) {
                $line = $pending[$i].Result
                if ($null -eq $line) { $pending[$i] = $null; break } # the stream closed
                $captured.Add($line)
                Write-Host "  | $line"
                if (-not $watching -and $line -match '^\s*Watching\b') {
                    $watching = $true
                    $watchingAt = $clock.Elapsed.TotalSeconds
                }
                $pending[$i] = $readers[$i].ReadLineAsync()
            }
        }
        # Green path: the marker is written only by the `--run` command, which Fable starts only
        # after a first compile that reported no error.
        if ($watching -and (Test-Path -LiteralPath $marker -PathType Leaf)) { break }
        # Watching with no marker: give the `--run` command a bounded moment to start, then decide.
        if ($watching -and ($clock.Elapsed.TotalSeconds - $watchingAt) -gt 60) { break }
        if ($proc.HasExited -and $null -eq $pending[0] -and $null -eq $pending[1]) { $exitedEarly = $true; break }
        if ($clock.Elapsed.TotalSeconds -gt $TimeoutSeconds) { $timedOut = $true; break }
        Start-Sleep -Milliseconds 250
    }
}
finally {
    # A watcher never exits on its own: stop the whole tree (Fable, its MSBuild children and the
    # `--run` command) on every path.
    if ($started -and -not $proc.HasExited) {
        try { $proc.Kill($true) } catch { }
        [void] $proc.WaitForExit(30000)
    }
    Set-Content -LiteralPath $logPath -Value $captured -Encoding utf8NoBOM
}

# ── Verdict ─────────────────────────────────────────────────────────────────

# Fable prints `<file>(l,c): (l,c) error FABLE: …` and `… error FSHARP: …`; match the severity word
# followed by a code and a colon, never a bare "error" inside a message.
$errors = @($captured | Where-Object { $_ -cmatch '\berror\s+[A-Z][A-Za-z0-9]*:' })
$reached = Test-Path -LiteralPath $marker -PathType Leaf

if (-not $KeepOutput) { Remove-Item -Recurse -Force -LiteralPath $outDir -ErrorAction SilentlyContinue }

if ($timedOut) { Fail "the first watch compile did not finish within ${TimeoutSeconds}s (log: $logPath)" }
if ($exitedEarly -and -not $watching) { Fail "dotnet fable watch exited (code $($proc.ExitCode)) before it reported watching (log: $logPath)" }
if ($errors.Count -gt 0) {
    Fail ("the watch compile reported $($errors.Count) error(s):`n" + (($errors | ForEach-Object { "     $_" }) -join "`n"))
}
if (-not $reached) { Fail "the watch compile reported no error but never ran its --run command (log: $logPath)" }

Write-Host ("==== fable-watch-consumer: green — the watch compile printed no error and reached its --run command in {0:F1}s" -f $clock.Elapsed.TotalSeconds) -ForegroundColor Green
exit 0
