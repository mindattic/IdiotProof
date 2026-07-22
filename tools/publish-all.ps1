#Requires -Version 5.1
<#
.SYNOPSIS
    Publish Monitor + Blazor to C:\IdiotProof\ and write the launch script.

.DESCRIPTION
    1. Kills any running published instances (Monitor exe, Blazor exe).
    2. Publishes IdiotProof.Monitor  -> C:\IdiotProof\Monitor\
    3. Publishes IdiotProof.Blazor   -> C:\IdiotProof\Blazor\
    4. Writes   C:\IdiotProof\Blazor\run.bat  (sets ASPNETCORE_URLS before exe)
    5. Writes   C:\IdiotProof\launch.bat       (starts both + opens browser)

.PARAMETER Launch
    After publishing, immediately run C:\IdiotProof\launch.bat.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\publish-all.ps1
    powershell -ExecutionPolicy Bypass -File tools\publish-all.ps1 -Launch
#>
param([switch]$Launch)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo       = Split-Path $PSScriptRoot   # tools\ -> repo root
$out        = 'C:\Apps\IdiotProof'
$blazorUrl  = 'http://localhost:5100'

# ── Stop running instances ─────────────────────────────────────────────────
Write-Host ''
Write-Host '  Stopping running instances...' -ForegroundColor Yellow
foreach ($name in @('idiotproof-monitor', 'IdiotProof.Blazor')) {
    $procs = Get-Process $name -ErrorAction SilentlyContinue
    if ($procs) {
        $procs | Stop-Process -Force
        Write-Host "    Stopped $name ($($procs.Count) process(es))" -ForegroundColor DarkYellow
    }
}
Start-Sleep -Milliseconds 800

# ── Publish Monitor ────────────────────────────────────────────────────────
Write-Host ''
Write-Host "  Publishing Monitor -> $out\Monitor" -ForegroundColor Cyan
dotnet publish "$repo\IdiotProof.Monitor\IdiotProof.Monitor.csproj" `
    --configuration Release `
    --output "$out\Monitor" `
    --no-self-contained
if ($LASTEXITCODE -ne 0) { throw "Monitor publish failed (exit $LASTEXITCODE)" }

# ── Publish Blazor ─────────────────────────────────────────────────────────
Write-Host ''
Write-Host "  Publishing Blazor  -> $out\Blazor" -ForegroundColor Cyan
dotnet publish "$repo\IdiotProof.Blazor\IdiotProof.Blazor.csproj" `
    --configuration Release `
    --output "$out\Blazor" `
    --no-self-contained
if ($LASTEXITCODE -ne 0) { throw "Blazor publish failed (exit $LASTEXITCODE)" }

# ── Write Blazor run wrapper ───────────────────────────────────────────────
# Runs the published Blazor exe with ASPNETCORE_URLS pre-set so it never
# conflicts with the dev server (65025/65026).
$runBatPath = "$out\Blazor\run.bat"
"@echo off" | Set-Content $runBatPath -Encoding ascii
"set ASPNETCORE_URLS=$blazorUrl" | Add-Content $runBatPath -Encoding ascii
"set ASPNETCORE_ENVIRONMENT=Production" | Add-Content $runBatPath -Encoding ascii
'"%~dp0IdiotProof.Blazor.exe"' | Add-Content $runBatPath -Encoding ascii

# ── Write top-level launch.bat ─────────────────────────────────────────────
$launchBatPath = "$out\launch.bat"
"@echo off" | Set-Content $launchBatPath -Encoding ascii
"title IdiotProof" | Add-Content $launchBatPath -Encoding ascii
"echo." | Add-Content $launchBatPath -Encoding ascii
"echo  Starting Monitor..." | Add-Content $launchBatPath -Encoding ascii
'start "IdiotProof Monitor" powershell -NoExit -Command "& ''C:\IdiotProof\Monitor\idiotproof-monitor.exe''"' | Add-Content $launchBatPath -Encoding ascii
"echo  Starting Blazor ($blazorUrl)..." | Add-Content $launchBatPath -Encoding ascii
"start `"IdiotProof Blazor`" `"$out\Blazor\run.bat`"" | Add-Content $launchBatPath -Encoding ascii
"echo  Waiting for Blazor to start..." | Add-Content $launchBatPath -Encoding ascii
"timeout /t 4 /nobreak >nul" | Add-Content $launchBatPath -Encoding ascii
"start `"`" `"$blazorUrl`"" | Add-Content $launchBatPath -Encoding ascii
"echo." | Add-Content $launchBatPath -Encoding ascii
"echo  Done. Monitor is in its own window; Blazor is at $blazorUrl" | Add-Content $launchBatPath -Encoding ascii

# ── Summary ────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '  Published successfully.' -ForegroundColor Green
Write-Host "    Monitor : $out\Monitor\idiotproof-monitor.exe" -ForegroundColor Gray
Write-Host "    Blazor  : $out\Blazor\IdiotProof.Blazor.exe  ($blazorUrl)" -ForegroundColor Gray
Write-Host "    Launcher: $out\launch.bat" -ForegroundColor Gray
Write-Host ''

if ($Launch) {
    Write-Host '  Launching...' -ForegroundColor Cyan
    Start-Process "$out\launch.bat"
}
