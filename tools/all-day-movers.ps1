#Requires -Version 5.1
<#
.SYNOPSIS
    All-day market data collector (IP-T601). Run every 5 minutes, around the clock, by MindAttic.Scheduler's
    IdiotProof.AllDayMovers job; a no-op outside its own 03:55-20:05 ET window. At 16:30 ET it backfills the
    day's movers into the permanent history store and runs the runner journal for the session.

.DESCRIPTION
    Owner, 2026-11-19: "run the entire trading window and keep collecting data" (not just the premarket
    gappers). Each tick, 03:55-20:05 ET weekdays: `market movers` (top gainers/losers) and `market most-actives`
    (top volume) are read and folded into the day's running set at
    D:\IdiotProofData\all-day-movers\<yyyy-MM-dd>.json: every symbol that was ever a top mover that day, its
    best rank/source, first/last seen (ET), and a one-time quote snapshot (bid/ask/last/spread) taken the
    moment it was first seen. A quote whose own trade time is already more than 2 minutes stale when read is
    flagged `possibleHalt` -- a heuristic read off the quote, not a real halt feed (Alpaca's halt/LULD stream
    is not polled here; a future pass could add it). Catalysts need no separate recording here: the catalyst
    intake workers (IP-T443, IP-T602) already watch news/EDGAR/wires for every symbol they mention, day-round.

    Once per day, the first tick at or after 16:30 ET: every symbol the day ever saw is queued with
    `idiotproof history backfill --symbols ...` (permanent daily + 1-minute bars, news, filings; data already
    held is never re-fetched) for the Hub's own history.backfill worker to drain in the background -- queuing
    only, never --run, so this job returns quickly instead of blocking on however many symbols piled up that
    day (a 5-year pull per symbol can run well past this job's own time limit). `tools\gapper-journal.ps1
    -Date <today> -Extra <that list>`
    runs the session's case study and journal entry with the day's full mover list standing in for the
    owner's screenshot. A `journalTriggeredEt` marker in the day's file keeps this from repeating if the job
    fires again before midnight; -DryRun skips both the API calls and the file write for a dry check.

.PARAMETER Cli
    Path to the idiotproof CLI; falls back to the one on PATH.

.PARAMETER Top
    How many gainers/losers and most-actives to read each tick (default 20, matches the radar's own top-N).

.PARAMETER DryRun
    Read-only: fetches and prints what would change, writes nothing, triggers nothing.
#>
param(
    [string]$Cli = 'C:\Apps\IdiotProof\Cli\idiotproof.exe',
    [int]$Top = 20,
    [switch]$DryRun
)

if (-not (Test-Path $Cli)) { $Cli = 'idiotproof' }

$logDir = Join-Path $env:LOCALAPPDATA 'MindAttic\IdiotProof\Logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir 'scheduler-jobs.log'
$et = [TimeZoneInfo]::FindSystemTimeZoneById('Eastern Standard Time')
function Stamp { [TimeZoneInfo]::ConvertTimeFromUtc([DateTime]::UtcNow, $et).ToString('yyyy-MM-dd HH:mm:ss') + ' ET' }
function Log([string]$line) { Add-Content -Path $log -Value $line -Encoding UTF8; $line }
$job = 'IdiotProof.AllDayMovers'

$nowEt = [TimeZoneInfo]::ConvertTimeFromUtc([DateTime]::UtcNow, $et)
$todayEt = $nowEt.Date
if ($nowEt.DayOfWeek -eq [DayOfWeek]::Saturday -or $nowEt.DayOfWeek -eq [DayOfWeek]::Sunday) { exit 0 }
$windowStart = $todayEt.AddHours(3).AddMinutes(55)   # 03:55 ET
$windowEnd = $todayEt.AddHours(20).AddMinutes(5)     # 20:05 ET
if ($nowEt -lt $windowStart -or $nowEt -gt $windowEnd) { exit 0 }  # outside the window: quiet no-op (no log line, this fires every 5 min)

$dataDir = 'D:\IdiotProofData\all-day-movers'
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
$dateKey = $todayEt.ToString('yyyy-MM-dd')
$file = Join-Path $dataDir "$dateKey.json"

function LoadDay {
    if (Test-Path $file) { try { return Get-Content $file -Raw | ConvertFrom-Json } catch { } }
    [pscustomobject]@{ date = $dateKey; symbols = @{}; journalTriggeredEt = $null }
}
function SaveDay($day) { if (-not $DryRun) { ($day | ConvertTo-Json -Depth 6) | Set-Content -Path $file -Encoding UTF8 } }

function Hub([string[]]$cmdArgs) {
    try { & $Cli @cmdArgs --json 2>$null | Out-String | ConvertFrom-Json }
    catch { Log "$(Stamp) [$job] $($cmdArgs -join ' ') failed: $($_.Exception.Message)"; $null }
}

$day = LoadDay
# A fresh day's $day.symbols is the plain Hashtable from LoadDay's default (IDictionary: copy keys straight, there
# are none yet). A reloaded day's is a PSCustomObject (ConvertFrom-Json's shape for a JSON object): walking its
# PSObject.Properties would, on the Hashtable branch instead, read the .NET type's own members (Count, Keys, ...)
# as if they were symbols -- so the two shapes are normalized separately, never through PSObject.Properties on a
# live Hashtable.
$symbols = @{}
if ($day.symbols -is [System.Collections.IDictionary]) {
    foreach ($k in $day.symbols.Keys) { $symbols[$k] = $day.symbols[$k] }
} elseif ($day.symbols) {
    foreach ($p in $day.symbols.PSObject.Properties) { $symbols[$p.Name] = $p.Value }
}

$movers = Hub @('market', 'movers', '--top', "$Top")
$actives = Hub @('market', 'most-actives', '--top', "$Top")
$seen = New-Object Collections.Generic.List[pscustomobject]
if ($movers) {
    $r = 0; foreach ($g in @($movers.data.gainers)) { $r++; $seen.Add([pscustomobject]@{ Symbol = $g.symbol; Rank = $r; Source = 'gainers' }) }
    $r = 0; foreach ($l in @($movers.data.losers)) { $r++; $seen.Add([pscustomobject]@{ Symbol = $l.symbol; Rank = $r; Source = 'losers' }) }
}
if ($actives) { foreach ($a in @($actives.data.most_actives)) { $seen.Add([pscustomobject]@{ Symbol = $a.symbol; Rank = $a.rank; Source = 'most-actives' }) } }

# One symbol can appear in more than one list this tick (e.g. a top gainer that's also most-active); fold to its
# best (lowest) rank so "timesSeen" below counts ticks, not list-memberships.
$bestThisTick = [ordered]@{}
foreach ($s in $seen) {
    if (-not $bestThisTick.Contains($s.Symbol) -or $s.Rank -lt $bestThisTick[$s.Symbol].Rank) { $bestThisTick[$s.Symbol] = $s }
}

$nowIso = $nowEt.ToString('HH:mm:ss')
$new = 0
foreach ($s in $bestThisTick.Values) {
    $sym = $s.Symbol
    if ($symbols.Contains($sym)) {
        $e = $symbols[$sym]
        $e.lastSeenEt = $nowIso
        $e.timesSeen = [int]$e.timesSeen + 1
        if ($s.Rank -lt [int]$e.bestRank) { $e.bestRank = $s.Rank; $e.bestSource = $s.Source }
        $symbols[$sym] = $e
        continue
    }
    $new++
    $quote = $null; $possibleHalt = $false
    if (-not $DryRun) {
        $q = Hub @('market', 'quote', $sym)
        $qd = @($q.data.quotes) | Select-Object -First 1
        if ($qd) {
            $quote = [pscustomobject]@{ last = $qd.last; bid = $qd.bid; ask = $qd.ask; spread = $qd.spread; quoteTimeEt = $qd.quote_time }
            try { $possibleHalt = ((Get-Date) - [datetime]$qd.quote_time).TotalMinutes -gt 2 } catch { }
        }
    }
    $symbols[$sym] = [pscustomobject]@{
        firstSeenEt = $nowIso; lastSeenEt = $nowIso; bestRank = $s.Rank; bestSource = $s.Source; timesSeen = 1
        firstQuote = $quote; possibleHalt = $possibleHalt
    }
}
$day.symbols = $symbols
if ($new -gt 0) { Log "$(Stamp) [$job] tick: $($symbols.Count) symbol(s) tracked today, $new new" }

$journalWindow = $todayEt.AddHours(16).AddMinutes(30)
if (-not $day.journalTriggeredEt -and $nowEt -ge $journalWindow -and $symbols.Count -gt 0) {
    $list = ($symbols.Keys | Sort-Object) -join ','
    Log "$(Stamp) [$job] 16:30 ET window: queuing $($symbols.Count) symbol(s) for history backfill and running the journal"
    if (-not $DryRun) {
        # Queue only (no --run): the Hub's own history.backfill worker drains it, so this job never blocks on
        # however many symbols piled up today.
        $out = & $Cli history backfill --symbols $list 2>&1 | Out-String
        Log "    history backfill (queued): $($out.Trim() -replace '\s+', ' ')"
        $jOut = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'gapper-journal.ps1') -Date $dateKey -Extra $list 2>&1 | Out-String
        Log "    gapper-journal: $($jOut.Trim() -replace '\s+', ' ')"
    }
    $day.journalTriggeredEt = $nowEt.ToString('yyyy-MM-dd HH:mm:ss')
}

SaveDay $day
exit 0
