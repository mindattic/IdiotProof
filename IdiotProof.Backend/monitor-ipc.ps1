#!/usr/bin/env pwsh
# =============================================================================
# IPC Communication Monitor Script
# Run this to watch IPC logs in real-time or analyze overnight activity
# Logs are stored in: MyDocuments\IdiotProof\Logs\
# =============================================================================

param(
    [switch]$Watch,      # Watch logs in real-time
    [switch]$Summary,    # Show summary of today's activity
    [int]$Lines = 50     # Number of lines to show (default 50)
)

$LogsPath = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "IdiotProof\Logs"
$Today = Get-Date -Format "yyyy-MM-dd"
$IpcLogFile = Join-Path $LogsPath "ipc_$Today.log"

Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host "                    IdiotProof IPC Communication Monitor" -ForegroundColor Cyan
Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host "Log folder: $LogsPath" -ForegroundColor Gray
Write-Host ""

# Check if logs directory exists
if (-not (Test-Path $LogsPath)) {
    Write-Host "Logs directory not found: $LogsPath" -ForegroundColor Yellow
    Write-Host "Make sure the backend has been run at least once." -ForegroundColor Yellow
    exit 1
}

# List available log files
Write-Host "Available log files:" -ForegroundColor Green
Get-ChildItem $LogsPath -Filter "*.log" | Sort-Object LastWriteTime -Descending | ForEach-Object {
    $size = "{0:N2} KB" -f ($_.Length / 1KB)
    Write-Host "  $($_.Name) - $size - Last modified: $($_.LastWriteTime)"
}
Write-Host ""

if ($Watch) {
    # Real-time monitoring
    if (-not (Test-Path $IpcLogFile)) {
        Write-Host "Waiting for IPC log file to be created..." -ForegroundColor Yellow
        Write-Host "Start the backend to begin logging." -ForegroundColor Yellow
        while (-not (Test-Path $IpcLogFile)) {
            Start-Sleep -Seconds 1
        }
    }
    
    Write-Host "Watching IPC log in real-time (Ctrl+C to stop):" -ForegroundColor Green
    Write-Host "------------------------------------------------------------------------------" -ForegroundColor Gray
    Get-Content $IpcLogFile -Wait -Tail 10
}
elseif ($Summary) {
    # Summary analysis
    if (-not (Test-Path $IpcLogFile)) {
        Write-Host "No IPC log file found for today ($Today)" -ForegroundColor Yellow
        exit 1
    }
    
    $content = Get-Content $IpcLogFile
    $totalLines = $content.Count
    
    $connections = ($content | Select-String -Pattern "\[CONNECTION\].*CONNECTED" | Measure-Object).Count
    $disconnections = ($content | Select-String -Pattern "\[CONNECTION\].*DISCONNECTED" | Measure-Object).Count
    $requests = ($content | Select-String -Pattern "\[REQUEST\]" | Measure-Object).Count
    $broadcasts = ($content | Select-String -Pattern "\[BROADCAST\]" | Measure-Object).Count
    $heartbeats = ($content | Select-String -Pattern "\[HEARTBEAT\]" | Measure-Object).Count
    $errors = ($content | Select-String -Pattern "\[ERROR\]" | Measure-Object).Count
    
    Write-Host "Summary for $Today" -ForegroundColor Green
    Write-Host "------------------------------------------------------------------------------" -ForegroundColor Gray
    Write-Host "Total log entries:    $totalLines"
    Write-Host "Client connections:   $connections"
    Write-Host "Client disconnections: $disconnections"
    Write-Host "Requests received:    $requests"
    Write-Host "Broadcasts sent:      $broadcasts"
    Write-Host "Heartbeats:           $heartbeats"
    Write-Host "Errors:               $errors" -ForegroundColor $(if ($errors -gt 0) { "Red" } else { "Green" })
    Write-Host ""
    
    if ($errors -gt 0) {
        Write-Host "Errors found:" -ForegroundColor Red
        $content | Select-String -Pattern "\[ERROR\]" | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    }
    
    # Show first and last activity times
    if ($totalLines -gt 0) {
        $firstLine = $content[0]
        $lastLine = $content[-1]
        Write-Host ""
        Write-Host "First activity: $($firstLine.Substring(0, 23))" -ForegroundColor Cyan
        Write-Host "Last activity:  $($lastLine.Substring(0, 23))" -ForegroundColor Cyan
    }
}
else {
    # Show recent logs
    if (-not (Test-Path $IpcLogFile)) {
        Write-Host "No IPC log file found for today ($Today)" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Checking for session logs..." -ForegroundColor Cyan
        Get-ChildItem $LogsPath -Filter "session_*.txt" | Sort-Object LastWriteTime -Descending | Select-Object -First 3 | ForEach-Object {
            Write-Host "  $($_.Name)"
        }
        exit 0
    }
    
    Write-Host "Last $Lines lines from IPC log:" -ForegroundColor Green
    Write-Host "------------------------------------------------------------------------------" -ForegroundColor Gray
    Get-Content $IpcLogFile -Tail $Lines
}

Write-Host ""
Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host "Usage:" -ForegroundColor Yellow
Write-Host "  .\monitor-ipc.ps1           # Show last 50 log entries"
Write-Host "  .\monitor-ipc.ps1 -Lines 100  # Show last 100 log entries"
Write-Host "  .\monitor-ipc.ps1 -Watch      # Watch logs in real-time"
Write-Host "  .\monitor-ipc.ps1 -Summary    # Show activity summary"
Write-Host "==============================================================================" -ForegroundColor Cyan
