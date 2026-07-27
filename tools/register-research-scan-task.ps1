#Requires -Version 5.1
<#
.SYNOPSIS
    Publish IdiotProof.ResearchScanner and register it as a Windows Scheduled Task.

.DESCRIPTION
    The research scanner is a one-shot console app (see IdiotProof.ResearchScanner) —
    it runs a single scan pass and exits, so it is NOT a Windows Service like
    IdiotProof.Monitor. This script publishes it to C:\Apps\IdiotProof\ResearchScanner\
    and registers (or updates) a Scheduled Task that fires it on a recurring interval,
    silently, so the /research tab always has fresh data when the user opens it.

    This script does NOT run automatically as part of any other tooling — it makes a
    persistent, machine-wide change (a Scheduled Task definition) and must be run
    deliberately, by hand, with an elevated PowerShell prompt (Register-ScheduledTask
    requires admin rights to create a task that runs whether or not a user is logged on).

.PARAMETER IntervalMinutes
    Minutes between scan passes. Default 60 (hourly) — rule filings and most 8-K/Form 4
    activity are infrequent enough that hourly is a reasonable default; tune with
    -IntervalMinutes if you want tighter coverage of the watchlist.

.PARAMETER TaskName
    Scheduled Task name. Default "IdiotProof Research Scan".

.EXAMPLE
    # Review what it would do, then run elevated to actually register the task:
    powershell -ExecutionPolicy Bypass -File tools\register-research-scan-task.ps1
    powershell -ExecutionPolicy Bypass -File tools\register-research-scan-task.ps1 -IntervalMinutes 30
#>
param(
    [int]$IntervalMinutes = 60,
    [string]$TaskName = 'IdiotProof Research Scan'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = Split-Path $PSScriptRoot   # tools\ -> repo root
$out  = 'C:\Apps\IdiotProof\ResearchScanner'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "Run this from an elevated (Administrator) PowerShell prompt — Register-ScheduledTask needs it."
}

# ── Publish (Release) ──────────────────────────────────────────────────────
Write-Host ''
Write-Host "  Publishing ResearchScanner (Release) -> $out" -ForegroundColor Cyan
dotnet publish "$repo\IdiotProof.ResearchScanner\IdiotProof.ResearchScanner.csproj" `
    --configuration Release `
    --output $out `
    --no-self-contained
if ($LASTEXITCODE -ne 0) { throw "ResearchScanner publish failed (exit $LASTEXITCODE)" }

$exePath = Join-Path $out 'idiotproof-research-scanner.exe'
if (-not (Test-Path $exePath)) { throw "Published exe not found at $exePath" }

# ── Register / update the Scheduled Task ───────────────────────────────────
Write-Host ''
Write-Host "  Registering Scheduled Task '$TaskName' (every $IntervalMinutes min)..." -ForegroundColor Cyan

$action  = New-ScheduledTaskAction -Execute $exePath -WorkingDirectory $out
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date) `
    -RepetitionInterval (New-TimeSpan -Minutes $IntervalMinutes) `
    -RepetitionDuration ([TimeSpan]::MaxValue)
$settings = New-ScheduledTaskSettingsSet `
    -MultipleInstances IgnoreNew `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 30) `
    -DontStopOnIdleEnd `
    -StartWhenAvailable

# Runs as the current user (not SYSTEM) — LocalDB and the MindAttic Vault
# keyrings are user-scoped, same reasoning as the Monitor's "must run as
# USER" hosting note.
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType S4U -RunLevel Limited

Register-ScheduledTask -TaskName $TaskName `
    -Action $action -Trigger $trigger -Settings $settings -Principal $principal `
    -Force | Out-Null

Write-Host ''
Write-Host '  Registered.' -ForegroundColor Green
Write-Host "    Exe      : $exePath" -ForegroundColor Gray
Write-Host "    Interval : every $IntervalMinutes minutes" -ForegroundColor Gray
Write-Host "    Task     : Task Scheduler Library \ $TaskName" -ForegroundColor Gray
Write-Host ''
Write-Host "  Run it once by hand to verify before waiting for the schedule:" -ForegroundColor Yellow
Write-Host "    Start-ScheduledTask -TaskName '$TaskName'" -ForegroundColor Gray
Write-Host ''
