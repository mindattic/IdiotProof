# IP-T499: builds the self-contained "Momentum. calls vs reality" page from the claim tracker tables.
# usage: powershell -File tools/momentum/artifact/build-artifact.ps1 [-Out D:\IdiotProofData\momentum\momentum-calls-vs-reality.html]
param(
    [string]$Out = 'D:\IdiotProofData\momentum\momentum-calls-vs-reality.html',
    [string]$JsonOut,
    [string]$ConnectionString = $(if ($env:ConnectionStrings__IdiotProof) { $env:ConnectionStrings__IdiotProof } else { 'Server=(localdb)\MSSQLLocalDB;Database=IdiotProof;Trusted_Connection=True;TrustServerCertificate=True;' })
)
$ErrorActionPreference = 'Stop'
$et = [TimeZoneInfo]::FindSystemTimeZoneById('Eastern Standard Time')

function Query([string]$sql) {
    $cn = New-Object System.Data.SqlClient.SqlConnection $ConnectionString
    $cn.Open()
    try {
        $cmd = $cn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 300
        $t = New-Object System.Data.DataTable; $t.Load($cmd.ExecuteReader()); return , $t
    } finally { $cn.Close() }
}
function V($x) { if ($x -is [DBNull]) { $null } else { $x } }
function Num($x, [int]$d = 2) { $x = V $x; if ($null -eq $x) { $null } else { [Math]::Round([double]$x, $d) } }
function EtTime($x) {
    $x = V $x; if ($null -eq $x) { return $null }
    [TimeZoneInfo]::ConvertTimeFromUtc([DateTime]::SpecifyKind($x, 'Utc'), $et).ToString('HH:mm')
}
function Clip([string]$s, [int]$n) { if (-not $s) { return $null }; if ($s.Length -le $n) { $s } else { $s.Substring(0, $n).TrimEnd() + '...' } }
# Windows PowerShell 5.1 emits a parsed JSON array as one pipeline object and unrolls arrays returned from expressions;
# the leading comma keeps one-element and empty lists as lists.
function JsonList($s) {
    $s = V $s
    $list = New-Object System.Collections.ArrayList
    if ($s) { $o = ConvertFrom-Json $s; foreach ($x in $o) { [void]$list.Add($x) } }
    return , $list.ToArray()
}

$videos = Query 'SELECT VideoId, Title, Kind, TradeDate, PublishedUtc FROM MomentumVideos'
$vmap = @{}
foreach ($v in $videos) { $vmap[$v.VideoId] = $v }

$calls = Query @'
SELECT c.Id, c.VideoId, c.TradeDate, c.Rank, c.ClaimType, c.SpokenSymbol, c.Symbol, c.SetupTags, c.Scenarios, c.RawQuote,
       o.ScenarioIndex, o.Verdict, o.PriorClose, o.Open0930, o.PremarketHigh, o.DayHigh, o.DayClose, o.DayHighPct, o.DayClosePct,
       o.GainerRank, o.TopGapper, o.TriggerLevel, o.TriggerHit, o.TriggerUtc, o.EntryPrice, o.EntryUtc, o.ExitPrice, o.ExitUtc, o.ExitReason,
       o.PlanPnlPct, o.MaxRunAfterTriggerPct, o.MaxDrawdownAfterTriggerPct, o.TargetsJson, o.TargetsHit, o.FirstTargetHit,
       o.InvalidationLevel, o.InvalidationSource, o.Notes, o.PrevDayGainerRank, o.PrevDayClosePct
FROM MomentumClaims c JOIN MomentumClaimOutcomes o ON o.ClaimId = c.Id
WHERE c.ClaimType IN ('watchlist-scenario', 'morning-update-scenario')
ORDER BY c.TradeDate DESC, c.VideoId, c.Rank, o.ScenarioIndex
'@

$scenCache = @{}
$rows = New-Object System.Collections.Generic.List[object]
foreach ($r in $calls) {
    $sid = [string]$r.Id
    if (-not $scenCache.ContainsKey($sid)) { $scenCache[$sid] = JsonList $r.Scenarios }
    $list = $scenCache[$sid]; $i = [int]$r.ScenarioIndex
    $sc = $null; if ($i -lt $list.Count) { $sc = $list[$i] }
    $targets = New-Object System.Collections.ArrayList
    $hits = JsonList $r.TargetsJson
    if ($hits.Count) {
        foreach ($t in $hits) {
            $hit = $null; if ($t.hitUtc) { $hit = EtTime ([DateTime]::Parse($t.hitUtc, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AdjustToUniversal)) }
            [void]$targets.Add([ordered]@{ p = Num $t.level 4; t = $hit })
        }
    } elseif ($sc -and $sc.Targets) {
        foreach ($t in @($sc.Targets)) { if ($t.Price) { [void]$targets.Add([ordered]@{ p = Num $t.Price 4; t = $null }) } }
    }
    $tags = JsonList $r.SetupTags
    $vrow = $vmap[[string]$r.VideoId]
    $pub = $null
    if ($vrow -and (V $vrow.PublishedUtc)) { $pub = [TimeZoneInfo]::ConvertTimeFromUtc([DateTime]::SpecifyKind($vrow.PublishedUtc, 'Utc'), $et).ToString('yyyy-MM-dd HH:mm') }
    $rows.Add([ordered]@{
        d   = ([DateTime]$r.TradeDate).ToString('yyyy-MM-dd')
        vid = [string]$r.VideoId
        pub = $pub
        k   = if ($r.ClaimType -eq 'morning-update-scenario') { 'am' } else { 'wl' }
        rk  = V $r.Rank
        sym = V $r.Symbol
        sp  = V $r.SpokenSymbol
        tags = $tags
        nm  = $(if ($sc) { $sc.Name } else { $null })
        q   = Clip $(if ($sc -and $sc.Quote) { $sc.Quote } else { V $r.RawQuote }) 420
        v   = [string]$r.Verdict
        pc  = Num $r.PriorClose 4; op = Num $r.Open0930 4; pmh = Num $r.PremarketHigh 4
        hi  = Num $r.DayHigh 4; cl = Num $r.DayClose 4
        hiP = Num $r.DayHighPct 1; clP = Num $r.DayClosePct 1
        gr  = V $r.GainerRank; top = V $r.TopGapper; pr = V $r.PrevDayGainerRank; prP = Num $r.PrevDayClosePct 1
        tr  = Num $r.TriggerLevel 4; trT = EtTime $r.TriggerUtc
        en  = Num $r.EntryPrice 4; enT = EtTime $r.EntryUtc
        ex  = Num $r.ExitPrice 4; exT = EtTime $r.ExitUtc; xr = V $r.ExitReason
        pnl = Num $r.PlanPnlPct 2; run = Num $r.MaxRunAfterTriggerPct 1; dd = Num $r.MaxDrawdownAfterTriggerPct 1
        tg  = $targets.ToArray(); th = [int]$r.TargetsHit; ft = [bool]$r.FirstTargetHit
        st  = Num $r.InvalidationLevel 4; stS = V $r.InvalidationSource
        n   = Clip (V $r.Notes) 200
    })
}

$brags = Query @'
SELECT c.TradeDate, c.VideoId, c.Symbol, c.SpokenSymbol, c.ClaimedPct, c.ClaimWindow, c.RawQuote,
       o.Verdict, o.BragActualHighPct, o.BragActualClosePct, o.BragActualLowToHighPct
FROM MomentumClaims c JOIN MomentumClaimOutcomes o ON o.ClaimId = c.Id
WHERE c.ClaimType = 'past-performance'
ORDER BY c.TradeDate DESC
'@
$bragRows = foreach ($b in $brags) {
    [ordered]@{
        d = ([DateTime]$b.TradeDate).ToString('yyyy-MM-dd'); vid = [string]$b.VideoId
        sym = V $b.Symbol; sp = V $b.SpokenSymbol; cp = Num $b.ClaimedPct 0; w = V $b.ClaimWindow
        v = [string]$b.Verdict; ah = Num $b.BragActualHighPct 0; ac = Num $b.BragActualClosePct 0; alh = Num $b.BragActualLowToHighPct 0
        q = Clip (V $b.RawQuote) 260
    }
}

$vidRows = foreach ($v in $videos) {
    [ordered]@{ id = [string]$v.VideoId; t = [string]$v.Title; k = [string]$v.Kind; d = if (V $v.TradeDate) { ([DateTime]$v.TradeDate).ToString('yyyy-MM-dd') } else { $null } }
}
$tx = Query 'SELECT COUNT(*) total, SUM(CASE WHEN Text IS NOT NULL THEN 1 ELSE 0 END) withText, SUM(CASE WHEN ExtractedUtc IS NOT NULL THEN 1 ELSE 0 END) extracted FROM MomentumTranscripts'

$data = [ordered]@{
    generatedEt = [TimeZoneInfo]::ConvertTimeFromUtc([DateTime]::UtcNow, $et).ToString('yyyy-MM-dd h:mm tt') + ' ET'
    transcripts = [ordered]@{ total = [int]$tx.Rows[0].total; withText = [int]$tx.Rows[0].withText; extracted = [int]$tx.Rows[0].extracted }
    videos = @($vidRows)
    calls = $rows
    brags = @($bragRows)
}
$json = $data | ConvertTo-Json -Depth 8 -Compress
$json = $json.Replace('</', '<\/').Replace([string][char]0xFFFD, '')
if ($JsonOut) { [IO.File]::WriteAllText($JsonOut, $json, (New-Object Text.UTF8Encoding $false)); "wrote $JsonOut" }

$template = Get-Content -Raw -Encoding UTF8 (Join-Path $PSScriptRoot 'template.html')
$html = $template.Replace('/*__DATA__*/null', $json)
[IO.File]::WriteAllText($Out, $html, (New-Object Text.UTF8Encoding $false))
"wrote $Out ($([Math]::Round($html.Length / 1KB)) KB): $($rows.Count) scenarios, $(@($bragRows).Count) brags, $(@($vidRows).Count) videos"
