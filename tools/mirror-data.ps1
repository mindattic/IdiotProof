#Requires -Version 5.1
<#
.SYNOPSIS
    Mirrors D:\IdiotProofData (gapper-study bars, SEC company facts, FINRA, halts, radar, momentum) to
    R:\Backup\IdiotProofData (IP-T581). Run nightly by MindAttic.Scheduler's IdiotProof.DataMirror job.

.DESCRIPTION
    The databases are backed up elsewhere (the Launcher's nightly .bak of LocalDB, the Azure.Backup* jobs'
    bacpacs of the Azure databases), but this folder was in no backup (IP-T449). One mirror, not a dated
    copy per night: robocopy /MIR copies only what changed, so a quiet night takes seconds. Build scratch
    folders (cli-bin*, *-build-*, momentum-bin) are rebuilt from source and skipped.
    robocopy exits 0-7 on success and 8+ on failure; this script exits 0 or 1 accordingly and appends the
    summary to %LOCALAPPDATA%\MindAttic\IdiotProof\Logs\scheduler-jobs.log.
#>
param(
    [string]$Source = 'D:\IdiotProofData',
    [string]$Target = 'R:\Backup\IdiotProofData'
)

$logDir = Join-Path $env:LOCALAPPDATA 'MindAttic\IdiotProof\Logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir 'scheduler-jobs.log'
$et = [TimeZoneInfo]::FindSystemTimeZoneById('Eastern Standard Time')
function Stamp { [TimeZoneInfo]::ConvertTimeFromUtc([DateTime]::UtcNow, $et).ToString('yyyy-MM-dd HH:mm:ss') + ' ET' }
function Log([string]$line) { Add-Content -Path $log -Value $line -Encoding UTF8 }

$job = 'IdiotProof.DataMirror'
Log "$(Stamp) [$job] start (pid $PID): $Source -> $Target"
if (-not (Test-Path $Source)) { Log "$(Stamp) [$job] $Source does not exist; nothing to mirror"; exit 1 }
$started = Get-Date
$output = & robocopy $Source $Target /MIR /R:2 /W:5 /MT:8 /NP /NFL /NDL /XJ `
    /XD (Join-Path $Source 'cli-bin') (Join-Path $Source 'cli-bin2') (Join-Path $Source 'momentum-bin') '*-build-*' 2>&1 | ForEach-Object { "$_" }
$code = $LASTEXITCODE
# The summary table (Dirs/Files/Bytes/Times) and any error lines.
foreach ($o in ($output | Where-Object { $_ -match '^\s*(Dirs|Files|Bytes|Times)\s*:|ERROR' } | Select-Object -Last 20)) { Log "    $($o.Trim())" }
Log ("{0} [{1}] robocopy exit {2} in {3:N0}s" -f (Stamp), $job, $code, ((Get-Date) - $started).TotalSeconds)
if ($code -ge 8) { exit 1 }
exit 0
