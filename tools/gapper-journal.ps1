#Requires -Version 5.1
<#
.SYNOPSIS
    Daily runner post-mortem journal (IP-T592): after the close, case-study the session's top runners and
    join each one to what we did with it (radar rank, armed, entered, paper P/L). Also builds the
    every-10-sessions pattern review table.

.DESCRIPTION
    Per session (run after 16:00 ET, the same evening, so the paper order list still covers the day):

        powershell -File tools\gapper-journal.ps1 -Date 2026-11-19 -Extra "MPU,MDRN,SOWG"

    1. `research gappers casestudy --from D --to D --top 20 [--extra D:<names>]` (amendment 05 window): catalyst,
       prior profile, 1-minute path and the R4 alarm check per runner. Its two files are moved next to the
       journal as journal/data/D.casestudy.json and D.casestudy.md.
    2. `gapper radar --date D --top 300`: the radar as known at 20:00 ET the evening before (what the arming used).
    3. `strategy list --deleted --mode paper`: gapper strategies created in the 48 h before D 09:30 ET = armed.
    4. `orders list --status all --mode paper --limit 500`: D's paper fills per symbol, joined as entry, exit, P/L.

    Writes journal/D.json (the merged rows, the input of the review) and, unless it already exists (-Force
    rewrites), journal/D.md: the generated table plus empty "post-mortem" sections a person or agent fills in.
    The script never places, cancels or changes an order or a strategy; every command it runs is read-only
    except the radar, which writes nothing without --save/--watchlist (neither is passed).

    Every 10 sessions:

        powershell -File tools\gapper-journal.ps1 -Review

    aggregates the last 10 journal/*.json into journal/review-<first>_<last>.generated.md (catalyst types,
    radar and R4 hit rates, timing, prior-profile counts, our paper result), the starting point for the
    hand-written review-NN.md that proposes each repeat pattern as a registered amendment tested on train
    (docs/RESEARCH_PLAYBOOK.md section 8): never a direct strategy change.
#>
param(
    [string]$Date,
    [string]$Extra = '',
    [int]$Top = 20,
    [int]$RadarTop = 300,
    [switch]$Review,
    [int]$Sessions = 10,
    [switch]$Force,
    [switch]$ReuseCaseStudy,
    [string]$Cli = 'C:\Apps\IdiotProof\Cli\idiotproof.exe',
    [string]$JournalDir = (Join-Path $PSScriptRoot '..\docs\research\gapper-study-2026-11\journal')
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Cli)) { $Cli = 'idiotproof' }
$JournalDir = [IO.Path]::GetFullPath($JournalDir)
$dataDir = Join-Path $JournalDir 'data'
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
$et = [TimeZoneInfo]::FindSystemTimeZoneById('Eastern Standard Time')
$inv = [Globalization.CultureInfo]::InvariantCulture
function NowEt { [TimeZoneInfo]::ConvertTimeFromUtc([DateTime]::UtcNow, $et) }
function ToEt($dto) {
    $parsed = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse([string]$dto, $inv, [System.Globalization.DateTimeStyles]::AssumeUniversal, [ref]$parsed)) { return $null }
    [TimeZoneInfo]::ConvertTimeFromUtc($parsed.UtcDateTime, $et)
}
function Pct($x) { if ($null -eq $x) { '-' } else { $v = [double]$x * 100; ('{0}{1:0}%' -f $(if ($v -gt 0) { '+' } else { '' }), $v) } }
function Num($x, [string]$f = '0.##') { if ($null -eq $x) { '-' } else { ([double]$x).ToString($f, $inv) } }
function Shares($x) {
    if ($null -eq $x) { return '-' }
    $v = [double]$x
    if ($v -ge 1e9) { return ($v / 1e9).ToString('0.0', $inv) + 'B' }
    if ($v -ge 1e6) { return ($v / 1e6).ToString('0.0', $inv) + 'M' }
    ($v / 1e3).ToString('0', $inv) + 'k'
}
function Median([double[]]$xs) {
    if (-not $xs -or $xs.Count -eq 0) { return $null }
    $s = $xs | Sort-Object; $n = $s.Count
    if ($n % 2) { $s[[int](($n - 1) / 2)] } else { ($s[$n / 2 - 1] + $s[$n / 2]) / 2 }
}

# A Hub command as JSON, retried while the Hub restarts (publish) or Alpaca rate-limits (429).
function Hub([string[]]$cmdArgs, [int]$tries = 4) {
    $ErrorActionPreference = 'Continue'   # the CLI's "[hub] still running" note goes to stderr; PS 5.1 would make it fatal under Stop
    for ($i = 1; $i -le $tries; $i++) {
        $raw = & $Cli @cmdArgs --json 2>$null | Out-String
        $r = $null
        try { $r = $raw | ConvertFrom-Json } catch { }
        if ($r -and $r.status -eq 'ok') { return $r.data }
        $why = if ($r -and $r.error) { ($r.error | Out-String).Trim() } else { ($raw.Trim() -split "`n" | Select-Object -Last 1) }
        Write-Warning "idiotproof $($cmdArgs -join ' ') failed (try $i of $tries): $why"
        if ($i -lt $tries) { Start-Sleep -Seconds 60 }
    }
    throw "idiotproof $($cmdArgs -join ' ') failed $tries times"
}

if ($Review) {
    $files = @(Get-ChildItem $JournalDir -Filter '????-??-??.json' | Sort-Object Name | Select-Object -Last $Sessions)
    if (-not $files.Count) { throw "No journal/*.json entries under $JournalDir" }
    $days = @($files | ForEach-Object { Get-Content $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json })
    $rows = @($days | ForEach-Object { $_.runners } | Where-Object { -not $_.named })
    $first = $days[0].date; $last = $days[-1].date
    $out = New-Object Collections.Generic.List[string]
    $out.Add("<!-- generated by tools/gapper-journal.ps1 -Review (IP-T592); case study (opened window, not a test) -->")
    $out.Add('')
    $out.Add("# Runner journal review input: $($days.Count) sessions, $first .. $last")
    $out.Add('')
    $out.Add("Ranked runners only ($($rows.Count) stock-days; owner-named extras excluded). Descriptive counts over a handful of sessions: every pattern below is a candidate for a registered amendment tested on train, never a rule.")
    $out.Add('')
    $out.Add('## Per session')
    $out.Add('')
    $out.Add('| Session | Runners | Median run | On radar (top 300) | Radar top 50 | R4 hit | Armed | Entered | Paper P/L % (sum, closed) |')
    $out.Add('|---|---:|---:|---:|---:|---:|---:|---:|---:|')
    foreach ($d in $days) {
        $r = @($d.runners | Where-Object { -not $_.named })
        $pl = @($r | Where-Object { $null -ne $_.paperPlPct } | ForEach-Object { [double]$_.paperPlPct })
        $sum = if ($pl.Count) { ($pl | Measure-Object -Sum).Sum } else { $null }
        $out.Add("| $($d.date) | $($r.Count) | $(Pct (Median @($r | ForEach-Object { [double]$_.runPct }))) | $(@($r | Where-Object { $_.radarRank }).Count) | $(@($r | Where-Object { $_.radarRank -and $_.radarRank -le 50 }).Count) | $(@($r | Where-Object { $_.r4Hit }).Count) | $(@($r | Where-Object { $_.armed }).Count) | $(@($r | Where-Object { $_.entered }).Count) | $(Pct $sum) |")
    }
    $out.Add('')
    $out.Add('## By catalyst type')
    $out.Add('')
    $out.Add('| Catalyst | n | Median run | Median catalyst to ignition (min) | Median close vs high | On radar | Entered | Median paper P/L |')
    $out.Add('|---|---:|---:|---:|---:|---:|---:|---:|')
    foreach ($g in ($rows | Group-Object catalyst | Sort-Object Count -Descending)) {
        $c2i = @($g.Group | Where-Object { $null -ne $_.catalystToIgnitionMin } | ForEach-Object { [double]$_.catalystToIgnitionMin })
        $pl = @($g.Group | Where-Object { $null -ne $_.paperPlPct } | ForEach-Object { [double]$_.paperPlPct })
        $out.Add("| $($g.Name) | $($g.Count) | $(Pct (Median @($g.Group | ForEach-Object { [double]$_.runPct }))) | $(Num (Median $c2i) '0') | $(Pct (Median @($g.Group | Where-Object { $null -ne $_.fadeToClosePct } | ForEach-Object { [double]$_.fadeToClosePct }))) | $(@($g.Group | Where-Object { $_.radarRank }).Count) | $(@($g.Group | Where-Object { $_.entered }).Count) | $(Pct (Median $pl)) |")
    }
    $out.Add('')
    $out.Add('## Prior profile and timing')
    $out.Add('')
    $n = [Math]::Max(1, $rows.Count)
    function Share($pred, [string]$label) {
        $k = @($rows | Where-Object $pred).Count
        $out.Add("| $label | $k | $(('{0:0}%' -f (100.0 * $k / $n))) |")
    }
    $out.Add('| Trait | n | share of runners |')
    $out.Add('|---|---:|---:|')
    Share { $null -ne $_.sharesOutstanding -and [double]$_.sharesOutstanding -lt 10e6 } 'shares outstanding under 10M'
    Share { $null -ne $_.daysSinceReverseSplit -and [int]$_.daysSinceReverseSplit -le 60 } 'reverse split in the last 60 sessions'
    Share { $_.recentDilutionFiling } 'dilution filing in the last 90 days'
    Share { [int]$_.priorTopMoverDays -ge 1 } 'a prior top-mover day in the window'
    Share { $_.foreignFiler } 'foreign filer'
    Share { $_.catalyst -eq 'no catalyst (market beta)' } 'no catalyst found'
    Share { $_.ignitionEt -and $_.ignitionEt -lt '09:30' } 'ignition before 09:30'
    Share { $_.hodEt -and $_.hodEt -lt '09:30' } 'high of day before 09:30'
    Share { $_.lateRunner } 'late runner: first +20% in 09:30-15:59, none before (amendment 09)'
    Share { $_.hodEt -and $_.hodEt -ge '16:00' } 'high of day in the post-market (16:00-20:00)'
    Share { $null -ne $_.catalystToFirst20Min -and [double]$_.catalystToFirst20Min -lt 0 -and -not $_.catalystBeforeClose } 'first +20% before a fresh catalyst was published'
    Share { $_.r4Alarm } 'R4 alarm 06:00-09:10'
    Share { $_.radarRank } 'on the evening radar (top 300)'
    Share { $_.armed } 'armed'
    Share { $_.entered } 'entered'
    $out.Add('')
    $out.Add('## Timing across the day (04:00-20:00, IP-T604)')
    $out.Add('')
    $out.Add('Rows from sessions journaled before IP-T604 have no all-day fields and count as "unknown".')
    $out.Add('')
    $out.Add('| Session of first +20% | n | Median run | Median catalyst to first +20% (fresh catalysts, min) | Median last print vs high | High in the same session | Entered |')
    $out.Add('|---|---:|---:|---:|---:|---:|---:|')
    $order = @('premarket (04:00-09:29)', 'open (09:30-09:44)', 'morning (09:45-11:59)', 'midday (12:00-13:59)', 'afternoon (14:00-15:59)', 'post-market (16:00-19:59)')
    foreach ($g in ($rows | Group-Object { if ($_.first20Session) { $_.first20Session } elseif ($_.PSObject.Properties['first20Session']) { 'never +20%' } else { 'unknown' } } | Sort-Object { $i = [array]::IndexOf($order, $_.Name); if ($i -lt 0) { 99 } else { $i } })) {
        $c2f = @($g.Group | Where-Object { $null -ne $_.catalystToFirst20Min -and -not $_.catalystBeforeClose } | ForEach-Object { [double]$_.catalystToFirst20Min })
        $same = @($g.Group | Where-Object { $_.hodSession -and $_.hodSession -eq $_.first20Session }).Count
        $out.Add("| $($g.Name) | $($g.Count) | $(Pct (Median @($g.Group | ForEach-Object { [double]$_.runPct }))) | $(Num (Median $c2f) '0') | $(Pct (Median @($g.Group | Where-Object { $null -ne $_.fadeToLastPct } | ForEach-Object { [double]$_.fadeToLastPct }))) | $same | $(@($g.Group | Where-Object { $_.entered }).Count) |")
    }
    $out.Add('')
    $out.Add('## Our entries vs the runners')
    $out.Add('')
    $out.Add('| Session | Symbol | Run | Ignition | Entry ET | Entry vs ignition (min) | HOD ET | Paper P/L | Exit |')
    $out.Add('|---|---|---:|---|---|---:|---|---:|---|')
    foreach ($d in $days) { foreach ($r in @($d.runners | Where-Object { $_.entered })) {
        $out.Add("| $($d.date) | $($r.symbol) | $(Pct $r.runPct) | $(if ($r.ignitionEt) { $r.ignitionEt } else { '-' }) | $($r.entryEt) | $(Num $r.entryVsIgnitionMin '0') | $(if ($r.hodEt) { $r.hodEt } else { '-' }) | $(Pct $r.paperPlPct) | $(if ($r.exitEt) { $r.exitEt } else { 'open' }) |")
    } }
    $path = Join-Path $JournalDir "review-$first`_$last.generated.md"
    [IO.File]::WriteAllLines($path, $out, (New-Object Text.UTF8Encoding $false))
    "review table -> $path"
    "next: write journal/review-NN.md (repeat patterns, each proposed as a registered amendment tested on train)"
    exit 0
}

# ── One session ─────────────────────────────────────────────────────────
if (-not $Date) { $Date = (NowEt).ToString('yyyy-MM-dd') }
$day = [datetime]::ParseExact($Date, 'yyyy-MM-dd', $inv)
if ((NowEt) -lt $day.AddHours(16)) { Write-Warning "$Date is still in session (before 16:00 ET): the case study will be partial." }
$mdPath = Join-Path $JournalDir "$Date.md"
$jsonOut = Join-Path $JournalDir "$Date.json"

# 1. Case study.
# Public-docs privacy (IP-T598): the journal is committed, so no Alpaca paper account id (PA + 10 upper/digits)
# may reach it. Digit-bearing matches are redacted; all-letter ones are words (e.g. PARTICIPANTS) and are just recased.
function Protect-AccountIds([string]$Path) {
    if (-not $Path -or -not (Test-Path $Path)) { return }
    $t = [IO.File]::ReadAllText($Path)
    $r = [regex]::Replace($t, 'PA[0-9A-Z]{10}', { param($m) if ($m.Value -match '[0-9]') { '[redacted-account]' } else { $m.Value.Substring(0, 1) + $m.Value.Substring(1).ToLowerInvariant() } })
    if ($r -ne $t) { [IO.File]::WriteAllText($Path, $r, (New-Object Text.UTF8Encoding $false)) }
}

$csArgs = @('research', 'gappers', 'casestudy', '--from', $Date, '--to', $Date, '--top', "$Top")
$named = @($Extra -split '[,\s]+' | Where-Object { $_ } | ForEach-Object { $_.ToUpperInvariant() })
if ($named.Count) { $csArgs += @('--extra', "$Date`:$($named -join ',')") }
$csJson = Join-Path $dataDir "$Date.casestudy.json"
$csMd = Join-Path $dataDir "$Date.casestudy.md"
if ($ReuseCaseStudy -and (Test-Path $csJson)) { "reusing $csJson" } else {
"case study $Date (top $Top$(if ($named.Count) { ", $($named.Count) named" }))..."
$cs = Hub $csArgs
$csJsonFrom = if ($cs.json_path) { $cs.json_path } else { $cs.jsonPath }   # the CLI envelope is snake_case
$csMdFrom = if ($cs.md_path) { $cs.md_path } else { $cs.mdPath }
if (-not $csJsonFrom -or -not (Test-Path $csJsonFrom)) { throw "The case study reported no output file ($($cs | ConvertTo-Json -Compress))" }
Move-Item -Force $csJsonFrom $csJson
Protect-AccountIds $csJson
if ($csMdFrom -and (Test-Path $csMdFrom)) { Move-Item -Force $csMdFrom $csMd; Protect-AccountIds $csMd }
}
$study = Get-Content $csJson -Raw -Encoding UTF8 | ConvertFrom-Json

# 2. Radar as of 20:00 ET the evening before.
"radar $Date (top $RadarTop)..."
$radar = Hub @('gapper', 'radar', '--date', $Date, '--top', "$RadarTop")
$radarRank = @{}; $radarPts = @{}
$k = 0
if ($radar.skipped) { Write-Warning "radar skipped ($($radar.reason)): the Radar column stays blank; rerun with -Force -ReuseCaseStudy inside the database online window to fill it" }
foreach ($r in @($radar.rows | Where-Object { $_ -and $_.symbol })) { $k++; if (-not $radarRank.ContainsKey($r.symbol)) { $radarRank[$r.symbol] = $k; $radarPts[$r.symbol] = $r.points } }

# 3. Armed: paper gapper strategies created in the 48 h before D 09:30 ET (ids are UUIDv7: creation time in the first 48 bits).
"strategies..."
$strats = @(Hub @('strategy', 'list', '--deleted', '--mode', 'paper'))
function Created([string]$id) {
    $ms = [Convert]::ToInt64(($id -replace '-', '').Substring(0, 12), 16)
    [TimeZoneInfo]::ConvertTimeFromUtc([DateTimeOffset]::FromUnixTimeMilliseconds($ms).UtcDateTime, $et)
}
$armEnd = $day.AddHours(9.5); $armStart = $armEnd.AddHours(-48)
$armed = @{}
foreach ($s in $strats) {
    $isGapper = if ($s.PSObject.Properties['kind'] -and $s.kind) { $s.kind -eq 'gapper' } else { $s.title -match 'Gapper' }
    if (-not $isGapper) { continue }
    $c = Created $s.id
    if ($c -gt $armStart -and $c -le $armEnd -and -not $armed.ContainsKey($s.symbol)) { $armed[$s.symbol] = $c }
}

# 4. D's paper fills per symbol.
"paper orders..."
$ord = Hub @('orders', 'list', '--status', 'all', '--mode', 'paper', '--limit', '500')
$fills = @{}
$oldest = $null
foreach ($o in @($ord.orders)) {
    if ($o.submitted_utc) { $sub = ToEt $o.submitted_utc; if ($null -eq $oldest -or $sub -lt $oldest) { $oldest = $sub } }
    if ([double]$o.filled_qty -le 0 -or -not $o.filled_utc) { continue }
    $t = ToEt $o.filled_utc
    if ($t.Date -ne $day) { continue }
    if (-not $fills.ContainsKey($o.symbol)) { $fills[$o.symbol] = New-Object Collections.Generic.List[object] }
    $fills[$o.symbol].Add([pscustomobject]@{ Side = $o.side; Qty = [double]$o.filled_qty; Px = [double]$o.filled_avg_price; Et = $t })
}
if ($oldest -and $oldest -gt $day) { Write-Warning "The paper order list starts at $($oldest.ToString('yyyy-MM-dd HH:mm')) ET: fills before it are missing (run the journal the same evening)." }

function Trade([string]$sym) {
    if (-not $fills.ContainsKey($sym)) { return $null }
    $f = @($fills[$sym] | Sort-Object Et)
    $buys = @($f | Where-Object { $_.Side -eq 'buy' }); $sells = @($f | Where-Object { $_.Side -eq 'sell' })
    if (-not $buys.Count) { return $null }
    $bq = ($buys | Measure-Object Qty -Sum).Sum; $bv = ($buys | ForEach-Object { $_.Qty * $_.Px } | Measure-Object -Sum).Sum
    $sq = if ($sells.Count) { ($sells | Measure-Object Qty -Sum).Sum } else { 0 }
    $sv = if ($sells.Count) { ($sells | ForEach-Object { $_.Qty * $_.Px } | Measure-Object -Sum).Sum } else { 0 }
    $entry = $bv / $bq
    [pscustomobject]@{
        EntryEt = $buys[0].Et; Entry = $entry; Buys = $buys.Count
        ExitEt = if ($sells.Count -and $sq -ge $bq) { $sells[-1].Et } else { $null }
        Exit = if ($sq -gt 0) { $sv / $sq } else { $null }
        PlPct = if ($sq -gt 0) { ($sv / $sq) / $entry - 1 } else { $null }
        Open = $sq -lt $bq
    }
}
function MinBetween([string]$a, $b) {
    if (-not $a -or -not $b) { return $null }
    [int]([datetime]$b - $day.Add([TimeSpan]::Parse($a, $inv))).TotalMinutes
}

# Float / short interest (IP-T603): only when a vendor key is in Vault; blank otherwise. Current vendor reads,
# not point-in-time history, so a candidate study feature only (pre-register before use).
$fundOn = $false
try { $fs = & $Cli fundamentals status --json 2>$null | Out-String | ConvertFrom-Json; $fundOn = [bool]$fs.data.configured } catch { }
function Prop($o, [string[]]$names) { if ($null -eq $o) { return $null }; foreach ($n in $names) { $p = $o.PSObject.Properties[$n]; if ($p) { return $p.Value } }; $null }
function Fundamentals([string]$sym) {
    if (-not $fundOn) { return $null }
    try { $r = & $Cli fundamentals show $sym --json 2>$null | Out-String | ConvertFrom-Json; Prop $r.data @('snapshot') } catch { $null }
}

$rows = foreach ($d in @($study.stockDays)) {
    $t = Trade $d.symbol
    $f = Fundamentals $d.symbol
    $rs = $d.lastReverseSplit
    [pscustomobject][ordered]@{
        rank = $d.rank; symbol = $d.symbol; named = [bool]$d.named; name = $d.name
        catalyst = $d.catalyst; group = $d.group; catalystEt = $d.catalystEt; catalystHeadline = $d.catalystHeadline
        # "who knew first" (IP-T607): null before IP-T602 (2026-11-19) or for a symbol/day the live intake never saw
        catalystFirstSeenEt = $d.catalystFirstSeenEt; catalystFirstSeenSource = $d.catalystFirstSeenSource
        # signed: negative = the move started before the catalyst was published (leak, promotion, sympathy?)
        catalystToIgnitionMin = if ($d.catalystEt -and $d.ignitionEt) { [int]($day.Add([TimeSpan]::Parse($d.ignitionEt, $inv)) - [datetime]::ParseExact($d.catalystEt, 'yyyy-MM-dd HH:mm', $inv)).TotalMinutes } else { $null }
        catalystToHodMin = $d.catalystToHodMin
        priorClose = $d.priorClose; sharesOutstanding = $d.sharesOutstanding; floatUnder10M = $d.floatUnder10M
        reverseSplit = if ($rs) { "1:$($rs.ratio) $($rs.exDate)" } else { $null }; daysSinceReverseSplit = $d.daysSinceReverseSplit
        recentDilutionFiling = [bool]$d.recentDilutionFiling; priorTopMoverDays = $d.priorTopMoverDays
        foreignFiler = [bool]$d.foreignFiler; country = $d.country
        pmHighEt = $d.pmHighEt; pmHighGain = $d.pmHighGain; ignitionEt = $d.ignitionEt; hodEt = $d.hodEt
        # the whole 04:00-20:00 day (IP-T604, amendment 09); null when the Hub's case study predates IP-T604
        first10Et = $d.first10Et; first20Et = $d.first20Et; first20Session = $d.first20Session; hodSession = $d.hodSession
        lateRunner = [bool]$d.lateRunner; reach20Pre0930 = [bool]$d.reach20Pre0930
        moveStartEt = $d.moveStartEt; moveStartGain = $d.moveStartGain; moveStartVolRatioTod = $d.moveStartVolRatioTod
        reach20AfterMoveStart = $d.reach20AfterMoveStart; moveStartToFirst20Min = $d.moveStartToFirst20Min
        # signed like catalystToIgnitionMin: negative = the move came first
        catalystToFirst20Min = $d.catalystToFirst20Min; catalystToMoveStartMin = $d.catalystToMoveStartMin; catalystBeforeClose = [bool]$d.catalystBeforeClose
        rthHighEt = $d.rthHighEt; postHighEt = $d.postHighEt; postHighGain = $d.postHighGain
        lastEt = $d.lastEt; fadeToLastPct = $d.fadeToLastPct; hodToPullback10Min = $d.hodToPullback10Min
        # the 1-minute path's high (04:00-20:00) when there are bars; the daily bar alone misses premarket highs
        runPct = if ($null -ne $d.runPct) { $d.runPct } else { $d.dailyHighGain }; fadeToClosePct = $d.fadeToClosePct; closeGain = $d.closeGain
        r4Status = $d.r4.status; r4Alarm = [bool]$d.r4.alarmEt; r4AlarmEt = $d.r4.alarmEt; r4Hit = [bool]$d.r4.hit
        radarRank = $radarRank[$d.symbol]; radarPoints = $radarPts[$d.symbol]
        armed = $armed.ContainsKey($d.symbol); armedEt = if ($armed.ContainsKey($d.symbol)) { $armed[$d.symbol].ToString('MM-dd HH:mm') } else { $null }
        entered = $null -ne $t; buys = if ($t) { $t.Buys } else { 0 }
        entryEt = if ($t) { $t.EntryEt.ToString('HH:mm') } else { $null }; entry = if ($t) { [Math]::Round($t.Entry, 4) } else { $null }
        entryVsIgnitionMin = if ($t) { MinBetween $d.ignitionEt $t.EntryEt } else { $null }
        exitEt = if ($t -and $t.ExitEt) { $t.ExitEt.ToString('HH:mm') } else { $null }; exit = if ($t -and $t.Exit) { [Math]::Round($t.Exit, 4) } else { $null }
        paperPlPct = if ($t -and $null -ne $t.PlPct) { [Math]::Round($t.PlPct, 4) } else { $null }; stillOpen = if ($t) { $t.Open } else { $false }
        floatShares = Prop $f @('floatShares', 'float_shares', 'FloatShares'); shortPctFloat = Prop $f @('shortPercentOfFloat', 'short_percent_of_float', 'ShortPercentOfFloat')
        daysToCover = Prop $f @('daysToCover', 'days_to_cover', 'DaysToCover'); fundamentalsSource = Prop $f @('source', 'Source')
    }
}
$rows = @($rows)
# IP-T628: the live move-start watch (amendment 09's m0, watch only) for this session and cumulatively; an older Hub without it just skips.
$moveStarts = $null; $moveStartsAll = $null
try { $moveStarts = Hub @('gapper', 'formation', 'move-starts', '--from', $Date, '--to', $Date) 1; $moveStartsAll = Hub @('gapper', 'formation', 'move-starts') 1 }
catch { Write-Warning "move-start watch not read: $($_.Exception.Message)" }
$journal = [ordered]@{
    label = 'case study (opened window, not a test)'; ticket = 'IP-T592'; date = $Date; generatedEt = (NowEt).ToString('yyyy-MM-dd HH:mm')
    top = $Top; named = $named; radarAsOf = $radar.as_of_utc; radarTop = $RadarTop
    armedWindowEt = "$($armStart.ToString('MM-dd HH:mm')) .. $($armEnd.ToString('MM-dd HH:mm'))"
    caseStudy = "data/$Date.casestudy.json"; runners = $rows
    moveStarts = if ($moveStarts) { @(Prop $moveStarts @('rows', 'Rows')) } else { $null }
}
[IO.File]::WriteAllText($jsonOut, ($journal | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding $false))
Protect-AccountIds $jsonOut
"journal data -> $jsonOut"

if ((Test-Path $mdPath) -and -not $Force) { "kept the existing $mdPath (-Force rewrites it; the hand-written sections would be lost)"; exit 0 }

$ranked = @($rows | Where-Object { -not $_.named })
$md = New-Object Collections.Generic.List[string]
$md.Add("# Runner journal $Date")
$md.Add('')
$cmdLine = "tools/gapper-journal.ps1 -Date $Date"
if ($named.Count) { $cmdLine += ' -Extra "' + ($named -join ',') + '"' }
$md.Add("Case study (opened window, not a test), IP-T592. Generated $($journal.generatedEt) ET by ``$cmdLine``; the post-mortem sections are written by hand. Times ET. Sources: [case study](data/$Date.casestudy.md) ([json](data/$Date.casestudy.json)), $(if ($radar.as_of_utc) { "the radar as known at $((ToEt $radar.as_of_utc).ToString('MM-dd HH:mm')) ET (top $RadarTop)" } else { "no radar (skipped: $($radar.reason))" }), paper gapper strategies created $($journal.armedWindowEt) ET, the paper account's fills on $Date.")
$md.Add('')
$md.Add('## Scoreboard')
$md.Add('')
$md.Add("- Top $Top runners (daily high over the split-adjusted prior close, `$0.10-`$20, liquid): median run $(Pct (Median @($ranked | ForEach-Object { [double]$_.runPct }))).")
$md.Add("- On the evening radar (top $RadarTop): $(@($ranked | Where-Object { $_.radarRank }).Count) of $($ranked.Count); in its top 50: $(@($ranked | Where-Object { $_.radarRank -and $_.radarRank -le 50 }).Count).")
$md.Add("- R4 alarm 06:00-09:10: $(@($ranked | Where-Object { $_.r4Alarm }).Count), of which before +20% (hit): $(@($ranked | Where-Object { $_.r4Hit }).Count).")
$md.Add("- Armed: $(@($ranked | Where-Object { $_.armed }).Count); entered: $(@($ranked | Where-Object { $_.entered }).Count).")
$md.Add('')
function Table($set) {
    $md.Add('| # | Symbol | Catalyst | Catalyst ET | Catalyst to ignition (min; < 0 moved first) | Prior close | Shares out | Float | Short % float | Days to cover | Reverse split | Prior top-mover days | Ignition | HOD | Run | Close vs high | R4 | Radar | Armed | Entry | Exit | Paper P/L |')
    $md.Add('|---:|---|---|---|---:|---:|---:|---:|---:|---:|---|---:|---|---|---:|---:|---|---:|---|---|---|---:|')
    foreach ($r in $set) {
        $rsTxt = if ($r.reverseSplit) { "$($r.reverseSplit) ($($r.daysSinceReverseSplit)d)" } else { '-' }
        $entry = if ($r.entered) { "$($r.entryEt) @ $(Num $r.entry '0.####')$(if ($r.buys -gt 1) { " ($($r.buys) buys, avg)" })" } else { '-' }
        $exit = if ($r.exitEt) { "$($r.exitEt) @ $(Num $r.exit '0.####')" } elseif ($r.entered) { 'open' } else { '-' }
        $r4 = if ($r.r4Hit) { "$($r.r4AlarmEt) hit" } elseif ($r.r4Alarm) { $r.r4AlarmEt } else { 'none' }
        $md.Add("| $($r.rank) | $($r.symbol) | $($r.catalyst) | $(if ($r.catalystEt) { $r.catalystEt.Substring(5) } else { '-' }) | $(Num $r.catalystToIgnitionMin '0') | $(Num $r.priorClose '0.####') | $(Shares $r.sharesOutstanding) | $(Shares $r.floatShares) | $(if ($null -ne $r.shortPctFloat) { (Num $r.shortPctFloat '0.#') + '%' } else { '-' }) | $(Num $r.daysToCover '0.#') | $rsTxt | $($r.priorTopMoverDays) | $(if ($r.ignitionEt) { $r.ignitionEt } else { '-' }) | $(if ($r.hodEt) { $r.hodEt } else { '-' }) | $(Pct $r.runPct) | $(Pct $r.fadeToClosePct) | $r4 | $(if ($r.radarRank) { "$($r.radarRank)" } else { '-' }) | $(if ($r.armed) { 'yes' } else { 'no' }) | $entry | $exit | $(Pct $r.paperPlPct) |")
    }
}
function TimingTable($set) {
    $md.Add('| # | Symbol | First +10% | First +20% | Session | Late runner | Move start (gain) | +20% after start (min) | Catalyst to first +20% (min; < 0 moved first) | Catalyst to move start (min) | RTH high | HOD | Post-market high | Last print vs high | High to -10% (min) |')
    $md.Add('|---:|---|---|---|---|---|---|---|---:|---:|---|---|---|---:|---:|')
    foreach ($r in $set) {
        $ms = if ($r.moveStartEt) { "$($r.moveStartEt) ($(Pct $r.moveStartGain))" } else { '-' }
        $after = if ($null -eq $r.reach20AfterMoveStart) { '-' } elseif ($r.reach20AfterMoveStart) { "yes ($(Num $r.moveStartToFirst20Min '0'))" } else { 'no' }
        $post = if ($r.postHighEt) { "$($r.postHighEt) $(Pct $r.postHighGain)" } else { '-' }
        $md.Add("| $($r.rank) | $($r.symbol) | $(if ($r.first10Et) { $r.first10Et } else { '-' }) | $(if ($r.first20Et) { $r.first20Et } else { '-' }) | $(if ($r.first20Session) { $r.first20Session } else { 'never' }) | $(if ($r.lateRunner) { 'yes' } else { 'no' }) | $ms | $after | $(Num $r.catalystToFirst20Min '0') | $(Num $r.catalystToMoveStartMin '0') | $(if ($r.rthHighEt) { $r.rthHighEt } else { '-' }) | $(if ($r.hodEt) { $r.hodEt } else { '-' }) | $post | $(Pct $r.fadeToLastPct) | $(Num $r.hodToPullback10Min '0') |")
    }
}
$md.Add('## Runners')
$md.Add('')
Table $ranked
$extraRows = @($rows | Where-Object { $_.named })
if ($extraRows.Count) {
    $md.Add('')
    $md.Add('Named by the owner (screenshot), outside the ranked top; kept out of every count above:')
    $md.Add('')
    Table $extraRows
}
$md.Add('')
$md.Add('## All-day timing (04:00-20:00)')
$md.Add('')
$md.Add("Late runners (no +20% before 09:30, +20% in 09:30-15:59; amendment 09): $(@($ranked | Where-Object { $_.lateRunner }).Count) of $($ranked.Count). First +20% by session: $((@($ranked | Where-Object { $_.first20Session } | Group-Object first20Session | ForEach-Object { "$($_.Name) $($_.Count)" }) -join ', ')). Moved before a fresh catalyst (published after the prior close, first +20% before it): $(@($ranked | Where-Object { $null -ne $_.catalystToFirst20Min -and [double]$_.catalystToFirst20Min -lt 0 -and -not $_.catalystBeforeClose }).Count). Move start = amendment 09's m0 (first abnormal-volume regular-hours minute closing above the prior close before any +20% high).")
$md.Add('')
TimingTable $ranked
if ($extraRows.Count) { $md.Add(''); $md.Add('Named:'); $md.Add(''); TimingTable $extraRows }
$md.Add('')
$firstSeenRows = @($rows | Where-Object { $_.catalystFirstSeenSource })
if ($firstSeenRows.Count) {
    $md.Add('## Who knew first (IP-T607)')
    $md.Add('')
    $md.Add("$($firstSeenRows.Count) of $($rows.Count) runner(s) had a live-intake sighting (IP-T602, wires/EDGAR/Alpaca news since 2026-11-19); everyone else is from before the sources existed or was never seen by them.")
    $md.Add('')
    $md.Add('| # | Symbol | First seen | Source |')
    $md.Add('|---:|---|---|---|')
    foreach ($r in $firstSeenRows) { $md.Add("| $($r.rank) | $($r.symbol) | $($r.catalystFirstSeenEt) | $($r.catalystFirstSeenSource) |") }
    $md.Add('')
}
if ($moveStartsAll) {
    function MsRate($m) { $l = [int](Prop $m @('labeled', 'Labeled')); $h = [int](Prop $m @('hits', 'Hits')); "$h of $l$(if ($l) { ' (' + ([double]$h / $l).ToString('0%', $inv) + ')' })" }
    $dayRows = @(if ($moveStarts) { Prop $moveStarts @('rows', 'Rows') })
    $md.Add('## Move-start watch (IP-T628)')
    $md.Add('')
    $md.Add("Live amendment 09 move starts (m0), watch only. Today: $($dayRows.Count) alert(s), reached +20% after m0: $(if ($moveStarts) { MsRate (Prop $moveStarts @('all', 'All')) } else { '-' }). Since the watch began: $(MsRate (Prop $moveStartsAll @('all', 'All'))); with a fresh catalyst $(MsRate (Prop $moveStartsAll @('fresh', 'Fresh'))), without $(MsRate (Prop $moveStartsAll @('noFresh', 'no_fresh', 'NoFresh'))). Descriptive, not a test of A09.")
    if ($dayRows.Count) {
        $md.Add('')
        $md.Add('| Symbol | m0 end | Gain | Vol x | Fresh catalyst | Seen after (min) | +20% after m0 | Max after |')
        $md.Add('|---|---|---:|---:|---|---:|---|---:|')
        foreach ($x in $dayRows) {
            $hit = Prop $x @('hit', 'Hit')
            $hitText = if ($null -eq $hit) { 'pending' } elseif ($hit) { "yes $(Prop $x @('first20Et', 'first20_et', 'First20Et'))" } else { 'no' }
            $md.Add("| $(Prop $x @('symbol', 'Symbol')) | $(Prop $x @('m0EndEt', 'm0_end_et', 'M0EndEt')) | $(Pct (Prop $x @('gain', 'Gain'))) | $(Num (Prop $x @('volumeMultiple', 'volume_multiple', 'VolumeMultiple')) '0.0') | $(if (Prop $x @('freshCatalyst', 'fresh_catalyst', 'FreshCatalyst')) { 'yes' } else { 'no' }) | $(Num (Prop $x @('detectionLagMinutes', 'detection_lag_minutes', 'DetectionLagMinutes')) '0.#') | $hitText | $(Pct (Prop $x @('maxGainAfter', 'max_gain_after', 'MaxGainAfter'))) |")
        }
    }
    $md.Add('')
}
$md.Add('## Post-mortem (by hand)')
$md.Add('')
$md.Add('Per runner: the catalyst in a line (type, source, how fast it reached the tape), timing vs the first move across the whole day (premarket, regular hours or post-market; a late runner per amendment 09?), what in the prior profile stood out, why the radar ranked or missed it, and what our paper strategy did and why.')
$md.Add('')
foreach ($r in $ranked) { $md.Add("- **$($r.symbol)**: ") }
$md.Add('')
$md.Add('## Patterns to carry to the 10-session review')
$md.Add('')
$md.Add('- ')
[IO.File]::WriteAllLines($mdPath, $md, (New-Object Text.UTF8Encoding $false))
"journal entry -> $mdPath"
