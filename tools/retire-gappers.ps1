#Requires -Version 5.1
<#
.SYNOPSIS
    Retires the day's gapper strategies once their session is over (IP-T590). Run daily at 09:45 ET by
    MindAttic.Scheduler's IdiotProof.GapperRetire job, and safe to run by hand at any time.

.DESCRIPTION
    A gapper strategy is good for one premarket only (owner, 2026-11-19): the radar arms a fresh set every
    morning, and a strategy left active trades the next day on a stale signal (on 11-19, 28 of 56 buy orders
    came from 11-18 strategies). This soft-deletes (`strategy delete`: deactivated, hidden, reversible with
    --restore) every Paper gapper strategy that books no position. A strategy still booking a position is
    skipped and retried on the next run, so an exit is never orphaned. Live strategies are never touched.

    -Before limits it to strategies created before that time (ET); the default retires every flat one, which
    is right after 09:30 because every gapper's sell-by (09:28) has passed.

    A gapper strategy is one whose kind is "gapper" (IP-T593: `strategy list --kind gapper`), so a renamed one
    is still retired. An older Hub that does not know --kind (refuses it, or ignores it and returns rows with
    no kind) falls back to the title match ('Gapper' in the title) until the Hub is redeployed.
#>
param(
    [string]$Cli = 'C:\Apps\IdiotProof\Cli\idiotproof.exe',
    [datetime]$Before = [datetime]::MaxValue,
    [switch]$DryRun
)

$logDir = Join-Path $env:LOCALAPPDATA 'MindAttic\IdiotProof\Logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir 'scheduler-jobs.log'
$et = [TimeZoneInfo]::FindSystemTimeZoneById('Eastern Standard Time')
function Stamp { [TimeZoneInfo]::ConvertTimeFromUtc([DateTime]::UtcNow, $et).ToString('yyyy-MM-dd HH:mm:ss') + ' ET' }
function Log([string]$line) { Add-Content -Path $log -Value $line -Encoding UTF8; $line }
$job = 'IdiotProof.GapperRetire'

Log "$(Stamp) [$job] start (pid $PID)$(if ($DryRun) { ' dry run' })"
function List([string[]]$extra) {
    try { & $Cli strategy list --mode paper @extra --json 2>$null | Out-String | ConvertFrom-Json }
    catch { Log "$(Stamp) [$job] strategy list $($extra -join ' ') failed: $($_.Exception.Message)"; $null }
}
# Select by kind; an older Hub refuses --kind (usage error) or ignores it (rows carry no kind): match titles then.
$byKind = $true
$result = List @('--kind', 'gapper')
if ($null -eq $result -or $result.status -ne 'ok') { $byKind = $false; $result = List @() }
if ($null -eq $result -or $result.status -ne 'ok' -or $null -eq $result.data) { Log "$(Stamp) [$job] strategy list returned nothing (Hub down?)"; exit 1 }
$list = @($result.data)
if ($byKind -and $list.Count -gt 0 -and -not @($list | Where-Object { $_.PSObject.Properties['kind'] }).Count) { $byKind = $false }
if (-not $byKind) { Log "    this Hub has no strategy kind yet; selecting gappers by 'Gapper' in the title" }

# Strategy ids are UUIDv7: the first 48 bits are the creation time in Unix milliseconds.
function Created([string]$id) {
    $ms = [Convert]::ToInt64(($id -replace '-', '').Substring(0, 12), 16)
    [TimeZoneInfo]::ConvertTimeFromUtc([DateTimeOffset]::FromUnixTimeMilliseconds($ms).UtcDateTime, $et)
}

function IsGapper($s) { if ($byKind) { $s.kind -eq 'gapper' } else { $s.title -match 'Gapper' } }
$gappers = @($list | Where-Object { (IsGapper $_) -and $_.mode -eq 'Paper' -and (Created $_.id) -lt $Before })
$held = @($gappers | Where-Object { $_.position -ne 0 })
$retire = @($gappers | Where-Object { $_.position -eq 0 })
$done = 0; $failed = 0
foreach ($s in $retire) {
    if ($DryRun) { $done++; continue }
    $out = & $Cli strategy delete $s.id 2>&1 | Out-String
    if ($out -match 'Deleted ') { $done++ } else { $failed++; Log "    $($s.symbol) $($s.id): $($out.Trim() -replace '\s+', ' ')" }
}
if ($held.Count) { Log "    skipped (still booking a position, retried next run): $(($held | ForEach-Object symbol) -join ', ')" }
Log "$(Stamp) [$job] retired $done of $($retire.Count) flat gapper strategies, $($held.Count) held skipped, $failed failed"
if ($failed) { exit 1 }
exit 0
