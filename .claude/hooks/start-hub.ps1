# SessionStart (IP-T82): start the IdiotProof Hub when it is not already running, so every
# `idiotproof` / mcp__idiotproof__* call in the session has a Hub to talk to.
#
# TRADING SAFETY: a Hub started by this hook does NOT run the Monitor (the trading loop) unless you
# opt in. By default it starts with IDIOTPROOF_HUB_MONITOR=0: every operation works (reads, previews,
# strategy edits, `trade ...` through the gateway with its confirmation codes) but nothing evaluates
# strategies or places orders unattended. To have the hook start the trading Monitor too, set
#   IDIOTPROOF_HUB_AUTOSTART_MONITOR=1
# in the environment Claude Code is launched from. A Hub that is already running (yours, the
# Windows service, run.bat) is never touched: the hook only checks /api/health and, when nothing
# answers, starts one.
#
# Never fails the session: any problem is one line of context, exit 0.
$ErrorActionPreference = 'Stop'
try {
    $root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $port = if ($env:IDIOTPROOF_HUB_PORT -match '^\d+$') { [int]$env:IDIOTPROOF_HUB_PORT } else { 5910 }

    try {
        $health = Invoke-RestMethod -Uri "http://127.0.0.1:$port/api/health" -TimeoutSec 2
        $monitor = if ($health.monitor_hosted) { 'hosted' } else { 'off' }
        Write-Output "[idiotproof] Hub already running on :$port (PID $($health.process_id), Monitor $monitor)."
        exit 0
    }
    catch { }

    if ($env:IDIOTPROOF_HUB_AUTOSTART_MONITOR -eq '1') {
        $env:IDIOTPROOF_HUB_MONITOR = '1'
        $what = 'WITH the Monitor (IDIOTPROOF_HUB_AUTOSTART_MONITOR=1): it evaluates and trades active strategies'
    }
    else {
        $env:IDIOTPROOF_HUB_MONITOR = '0'
        $what = 'with the Monitor OFF (set IDIOTPROOF_HUB_AUTOSTART_MONITOR=1 to auto-start trading; `idiotproof hub stop` then `idiotproof hub start` to switch)'
    }

    # `hub start` (the CLI) starts the Hub in its own window, inherits this environment and waits
    # until it answers. idiotproof.cmd prefers the published CLI, then the last local build.
    $cmd = Join-Path $root 'idiotproof.cmd'
    $out = & cmd.exe /d /c "`"$cmd`" hub start" 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0) {
        Write-Output "[idiotproof] Started the Hub on :$port $what."
    }
    else {
        Write-Output "[idiotproof] Could not start the Hub (exit $LASTEXITCODE): $($out.Trim() -replace '\s+', ' ')"
    }
}
catch {
    Write-Output "[idiotproof] Hub autostart skipped: $($_.Exception.Message)"
}
exit 0
