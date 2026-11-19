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
function ToEt($dto) { [TimeZoneInfo]::ConvertTimeFromUtc(([DateTimeOffset]::Parse([string]$dto, $inv)).UtcDateTime, $et) }
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
    Share { $_.r4Alarm } 'R4 alarm 06:00-09:10'
    Share { $_.radarRank } 'on the evening radar (top 300)'
    Share { $_.armed } 'armed'
    Share { $_.entered } 'entered'
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
if ($csMdFrom -and (Test-Path $csMdFrom)) { Move-Item -Force $csMdFrom $csMd }
}
$study = Get-Content $csJson -Raw -Encoding UTF8 | ConvertFrom-Json

# 2. Radar as of 20:00 ET the evening before.
"radar $Date (top $RadarTop)..."
$radar = Hub @('gapper', 'radar', '--date', $Date, '--top', "$RadarTop")
$radarRank = @{}; $radarPts = @{}
$k = 0
foreach ($r in @($radar.rows)) { $k++; if (-not $radarRank.ContainsKey($r.symbol)) { $radarRank[$r.symbol] = $k; $radarPts[$r.symbol] = $r.points } }

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

$rows = foreach ($d in @($study.stockDays)) {
    $t = Trade $d.symbol
    $rs = $d.lastReverseSplit
    [pscustomobject][ordered]@{
        rank = $d.rank; symbol = $d.symbol; named = [bool]$d.named; name = $d.name
        catalyst = $d.catalyst; group = $d.group; catalystEt = $d.catalystEt; catalystHeadline = $d.catalystHeadline
        # signed: negative = the move started before the catalyst was published (leak, promotion, sympathy?)
        catalystToIgnitionMin = if ($d.catalystEt -and $d.ignitionEt) { [int]($day.Add([TimeSpan]::Parse($d.ignitionEt, $inv)) - [datetime]::ParseExact($d.catalystEt, 'yyyy-MM-dd HH:mm', $inv)).TotalMinutes } else { $null }
        catalystToHodMin = $d.catalystToHodMin
        priorClose = $d.priorClose; sharesOutstanding = $d.sharesOutstanding; floatUnder10M = $d.floatUnder10M
        reverseSplit = if ($rs) { "1:$($rs.ratio) $($rs.exDate)" } else { $null }; daysSinceReverseSplit = $d.daysSinceReverseSplit
        recentDilutionFiling = [bool]$d.recentDilutionFiling; priorTopMoverDays = $d.priorTopMoverDays
        foreignFiler = [bool]$d.foreignFiler; country = $d.country
        pmHighEt = $d.pmHighEt; pmHighGain = $d.pmHighGain; ignitionEt = $d.ignitionEt; hodEt = $d.hodEt
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
    }
}
$rows = @($rows)
$journal = [ordered]@{
    label = 'case study (opened window, not a test)'; ticket = 'IP-T592'; date = $Date; generatedEt = (NowEt).ToString('yyyy-MM-dd HH:mm')
    top = $Top; named = $named; radarAsOf = $radar.as_of_utc; radarTop = $RadarTop
    armedWindowEt = "$($armStart.ToString('MM-dd HH:mm')) .. $($armEnd.ToString('MM-dd HH:mm'))"
    caseStudy = "data/$Date.casestudy.json"; runners = $rows
}
[IO.File]::WriteAllText($jsonOut, ($journal | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding $false))
"journal data -> $jsonOut"

if ((Test-Path $mdPath) -and -not $Force) { "kept the existing $mdPath (-Force rewrites it; the hand-written sections would be lost)"; exit 0 }

$ranked = @($rows | Where-Object { -not $_.named })
$md = New-Object Collections.Generic.List[string]
$md.Add("# Runner journal $Date")
$md.Add('')
$cmdLine = "tools/gapper-journal.ps1 -Date $Date"
if ($named.Count) { $cmdLine += ' -Extra "' + ($named -join ',') + '"' }
$md.Add("Case study (opened window, not a test), IP-T592. Generated $($journal.generatedEt) ET by ``$cmdLine``; the post-mortem sections are written by hand. Times ET. Sources: [case study](data/$Date.casestudy.md) ([json](data/$Date.casestudy.json)), the radar as known at $((ToEt $radar.as_of_utc).ToString('MM-dd HH:mm')) ET (top $RadarTop), paper gapper strategies created $($journal.armedWindowEt) ET, the paper account's fills on $Date.")
$md.Add('')
$md.Add('## Scoreboard')
$md.Add('')
$md.Add("- Top $Top runners (daily high over the split-adjusted prior close, `$0.10-`$20, liquid): median run $(Pct (Median @($ranked | ForEach-Object { [double]$_.runPct }))).")
$md.Add("- On the evening radar (top $RadarTop): $(@($ranked | Where-Object { $_.radarRank }).Count) of $($ranked.Count); in its top 50: $(@($ranked | Where-Object { $_.radarRank -and $_.radarRank -le 50 }).Count).")
$md.Add("- R4 alarm 06:00-09:10: $(@($ranked | Where-Object { $_.r4Alarm }).Count), of which before +20% (hit): $(@($ranked | Where-Object { $_.r4Hit }).Count).")
$md.Add("- Armed: $(@($ranked | Where-Object { $_.armed }).Count); entered: $(@($ranked | Where-Object { $_.entered }).Count).")
$md.Add('')
function Table($set) {
    $md.Add('| # | Symbol | Catalyst | Catalyst ET | Catalyst to ignition (min; < 0 moved first) | Prior close | Shares out | Reverse split | Prior top-mover days | Ignition | HOD | Run | Close vs high | R4 | Radar | Armed | Entry | Exit | Paper P/L |')
    $md.Add('|---:|---|---|---|---:|---:|---:|---|---:|---|---|---:|---:|---|---:|---|---|---|---:|')
    foreach ($r in $set) {
        $rsTxt = if ($r.reverseSplit) { "$($r.reverseSplit) ($($r.daysSinceReverseSplit)d)" } else { '-' }
        $entry = if ($r.entered) { "$($r.entryEt) @ $(Num $r.entry '0.####')$(if ($r.buys -gt 1) { " ($($r.buys) buys, avg)" })" } else { '-' }
        $exit = if ($r.exitEt) { "$($r.exitEt) @ $(Num $r.exit '0.####')" } elseif ($r.entered) { 'open' } else { '-' }
        $r4 = if ($r.r4Hit) { "$($r.r4AlarmEt) hit" } elseif ($r.r4Alarm) { $r.r4AlarmEt } else { 'none' }
        $md.Add("| $($r.rank) | $($r.symbol) | $($r.catalyst) | $(if ($r.catalystEt) { $r.catalystEt.Substring(5) } else { '-' }) | $(Num $r.catalystToIgnitionMin '0') | $(Num $r.priorClose '0.####') | $(Shares $r.sharesOutstanding) | $rsTxt | $($r.priorTopMoverDays) | $(if ($r.ignitionEt) { $r.ignitionEt } else { '-' }) | $(if ($r.hodEt) { $r.hodEt } else { '-' }) | $(Pct $r.runPct) | $(Pct $r.fadeToClosePct) | $r4 | $(if ($r.radarRank) { "$($r.radarRank)" } else { '-' }) | $(if ($r.armed) { 'yes' } else { 'no' }) | $entry | $exit | $(Pct $r.paperPlPct) |")
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
$md.Add('## Post-mortem (by hand)')
$md.Add('')
$md.Add('Per runner: the catalyst in a line (type, source, how fast it reached the tape), timing vs the first move, what in the prior profile stood out, why the radar ranked or missed it, and what our paper strategy did and why.')
$md.Add('')
foreach ($r in $ranked) { $md.Add("- **$($r.symbol)**: ") }
$md.Add('')
$md.Add('## Patterns to carry to the 10-session review')
$md.Add('')
$md.Add('- ')
[IO.File]::WriteAllLines($mdPath, $md, (New-Object Text.UTF8Encoding $false))
"journal entry -> $mdPath"
