using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using IdiotProof.Brokers;
using IdiotProof.Core.Data;
using IdiotProof.Core.Services;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;
using IdiotProof.Scripting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Monitoring.FadeShort;

/// <summary>
/// The fade-short borrow pilot (IP-T480): a NO-ORDER recorder that answers "when the fade-short signal fires,
/// can we actually short the stock on Alpaca, and at what cost?". Idle unless SettingsKv
/// <c>fadeShort.pilot.enabled</c> is "true", an owner is set (<c>fadeShort.pilot.ownerUserId</c>),
/// <c>fadeShort.pilot.brokerMode</c> is paper and this instance holds the Monitor leader lease. On trading days:
/// <list type="bullet">
/// <item>09:40-09:44 ET: candidates (Alpaca movers at +15% or more, plus today's formation snapshots at +30% or
/// more) whose premarket high reached +50% over the adjusted prior close; the asset endpoint (shortable,
/// easy_to_borrow, marginable, status) and the advisory locate quote are recorded.</item>
/// <item>09:46-09:59 ET: the signal at 09:45 from minute bars, then the asset and locate quote again.</item>
/// </list>
/// Every Alpaca call goes through <see cref="IFadeShortBorrowReader"/>, a GET-only allow-list. The worker has no
/// dependency on the TransactionGateway, a broker client or the order router, and never places, reserves or
/// cancels anything (a locate request, <c>POST /v1/locates</c>, costs money and is never made).
/// </summary>
public sealed class FadeShortBorrowPilotWorker(
    FadeShortPilotSettings settings,
    MonitorRuntimeState monitor,
    IFadeShortBorrowReader reader,
    IDbContextFactory<AppDbContext> dbFactory,
    AuditLogRepository auditLog,
    TimeProvider time,
    ILogger<FadeShortBorrowPilotWorker> log) : BackgroundService
{
    public const string AuditRun = "fade-pilot";
    public const string AuditBlocked = "fade-pilot-blocked";
    public const string AuditError = "fade-pilot-error";
    public const string Phase0940 = "09:40";
    public const string Phase0946 = "09:46";

    public static readonly TimeSpan FirstCheckEt = new(9, 40, 0);
    public static readonly TimeSpan FirstCheckEndEt = new(9, 45, 0);
    public static readonly TimeSpan SecondCheckEt = new(9, 46, 0);
    public static readonly TimeSpan SecondCheckEndEt = new(10, 0, 0);
    public static readonly TimeSpan Cadence = TimeSpan.FromSeconds(30);

    /// <summary>A movers row must be up at least this much to be screened for its premarket high.</summary>
    public const double MoverMinPercent = 15;
    /// <summary>A formation snapshot row must have gapped at least this much today to be screened.</summary>
    public const double FormationMinGapPct = 30;
    public const int MoversTop = 50;
    private const int RawJsonMax = 4000;
    private static readonly TimeSpan BlockedAuditSpacing = TimeSpan.FromMinutes(30);

    private readonly HashSet<(DateOnly Day, string Phase)> done = [];
    private DateTime lastBlockedAuditUtc = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Fade-short pilot tick failed");
                try { await auditLog.LogAsync(AuditError, $"Fade-short pilot tick failed: {Trim(ex.Message, 300)}", ct: stoppingToken); }
                catch (Exception auditEx) { log.LogDebug(auditEx, "fade-pilot error audit failed"); }
            }
            try { await Task.Delay(Cadence, time, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>The phase for an ET instant: 09:40, 09:46 or null (outside both windows, a weekend or a market holiday).</summary>
    public static string? PhaseAt(DateTime nowEt)
    {
        if (nowEt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return null;
        if (MarketTime.IsMarketHoliday(DateOnly.FromDateTime(nowEt))) return null;
        var t = nowEt.TimeOfDay;
        if (t >= FirstCheckEt && t < FirstCheckEndEt) return Phase0940;
        if (t >= SecondCheckEt && t < SecondCheckEndEt) return Phase0946;
        return null;
    }

    /// <summary>One pass; true when a phase ran.</summary>
    public async Task<bool> RunOnceAsync(CancellationToken ct)
    {
        var nowUtc = time.GetUtcNow().UtcDateTime;
        var nowEt = MinuteBarMath.ToEastern(nowUtc);
        if (PhaseAt(nowEt) is not { } phase) return false;
        var dateEt = DateOnly.FromDateTime(nowEt);
        if (done.Contains((dateEt, phase))) return false;

        var cfg = await settings.ReadAsync(ct);
        if (!cfg.Enabled || cfg.OwnerUserId is not { } owner) return false;
        if (!await monitor.IsLeaderAsync(ct)) return false;
        if (!cfg.IsPaper)
        {
            if (nowUtc - lastBlockedAuditUtc >= BlockedAuditSpacing)
            {
                lastBlockedAuditUtc = nowUtc;
                log.LogError("Fade-short pilot: {Key} is '{Mode}', not paper; the pilot stays off.", FadeShortPilotSettings.BrokerModeKey, cfg.BrokerMode);
                await auditLog.LogAsync(AuditBlocked, $"Fade-short pilot blocked: {FadeShortPilotSettings.BrokerModeKey} is '{cfg.BrokerMode}', not paper.", userId: owner, ct: ct);
            }
            return false;
        }
        if (await PhaseAlreadyRecordedAsync(dateEt, phase, ct))
        {
            done.Add((dateEt, phase));
            return false;
        }

        done.Add((dateEt, phase));
        string summary;
        try { summary = await RunPhaseAsync(owner, cfg, dateEt, phase, nowUtc, ct); }
        catch
        {
            done.Remove((dateEt, phase)); // nothing was saved; the next tick inside the window retries
            throw;
        }
        log.LogInformation("Fade-short pilot {Phase} ET: {Summary}", phase, summary);
        await auditLog.LogAsync(AuditRun, $"Fade-short pilot {phase} ET {dateEt:yyyy-MM-dd}: {summary}", userId: owner, ct: ct);
        return true;
    }

    private async Task<bool> PhaseAlreadyRecordedAsync(DateOnly dateEt, string phase, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = db.FadeShortBorrowObservations.AsNoTracking().Where(o => o.DateEt == dateEt);
        return phase == Phase0940
            ? await rows.AnyAsync(o => o.Checked0940Utc != null, ct)
            : await rows.AnyAsync(o => o.Checked0946Utc != null, ct);
    }

    /// <summary>Screens, checks and records one phase; returns a one-line summary.</summary>
    internal async Task<string> RunPhaseAsync(Guid owner, FadeShortPilotConfig cfg, DateOnly dateEt, string phase, DateTime nowUtc, CancellationToken ct)
    {
        bool final = phase == Phase0946;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = (await db.FadeShortBorrowObservations.Where(o => o.DateEt == dateEt).ToListAsync(ct))
            .ToDictionary(o => o.Symbol, StringComparer.OrdinalIgnoreCase);

        await using var session = await reader.OpenAsync(owner, ct);
        var notes = new List<string>();
        var candidates = await CandidatesAsync(session, db, cfg, dateEt, notes, ct);
        foreach (var existing in rows.Keys)
            if (!candidates.ContainsKey(existing)) candidates[existing] = rows[existing].CandidateSource;

        var sessionStartUtc = MinuteBarMath.ToUtc(dateEt, MinuteBarMath.SessionStartEt);
        var decisionUtc = FadeShortPilotMath.DecisionUtc(dateEt);
        var asOfUtc = final ? decisionUtc : Floor(nowUtc);
        var priorTo = FadeShortPilotMath.PriorTradingDay(dateEt);
        var priorFrom = priorTo.AddDays(-14);
        var regShoDir = cfg.RegShoDir ?? DefaultRegShoDir();

        var checkedRows = new List<FadeShortBorrowObservation>();
        int screened = 0, failures = 0;
        foreach (var (symbol, source) in candidates)
        {
            ct.ThrowIfCancellationRequested();
            screened++;
            rows.TryGetValue(symbol, out var row);
            try
            {
                var daily = await session.DailyBarsAsync(symbol, priorFrom, priorTo, cfg.Feed, ct);
                var (prevClose, ssrX, ssr) = FadeShortPilotMath.PriorDays(daily, dateEt);
                if (prevClose is not { } prev || prev <= 0)
                {
                    if (row is not null) row.Note = Trim("no adjusted prior close", 400);
                    continue;
                }
                var bars = await session.MinuteBarsAsync(symbol, sessionStartUtc, asOfUtc, cfg.Feed, ct);
                var pm = FadeShortPilotMath.PremarketHigh(bars, dateEt, prev, asOfUtc);
                if (row is null && (pm is null || pm.Value.Pct < FadeShortPilotMath.MinPmHighPct)) continue;

                if (row is null)
                {
                    row = new FadeShortBorrowObservation { OwnerUserId = owner, DateEt = dateEt, Symbol = symbol, CandidateSource = source, CreatedUtc = nowUtc };
                    db.FadeShortBorrowObservations.Add(row);
                    rows[symbol] = row;
                }
                row.UpdatedUtc = nowUtc;
                row.PrevClose = prev;
                if (pm is { } p)
                {
                    row.PmHigh = p.PmHigh;
                    row.PmHighPct = Round(p.Pct);
                    row.PmHigh100 = p.Pct >= FadeShortPilotMath.PmHigh100Pct;
                }
                row.SsrD1LowVsD2Close = ssrX;
                row.SsrActive = ssr;
                row.RegSho = SafeRegSho(regShoDir, symbol, dateEt);

                if (final)
                {
                    row.SignalEvaluatedUtc = nowUtc;
                    var sig = FadeShortPilotMath.Evaluate(bars, dateEt, prev);
                    row.SignalFired = sig?.Fired ?? false;
                    row.GapAt0945 = sig is null ? null : Round(sig.GapPct);
                    row.ExtensionAt0945 = sig is null ? null : Round(sig.ExtensionPct);
                    row.Open0930 = sig?.Open0930;
                    row.Close0945 = sig?.Close;
                    if (sig is null) row.Note = Trim("signal not computable at 09:45 (no premarket bar, 09:30 bar or prior close)", 400);
                }

                var assetJson = await session.AssetAsync(symbol, ct);
                var asset = FadeShortPilotMath.ParseAsset(assetJson);
                if (final)
                {
                    row.Checked0946Utc = nowUtc;
                    (row.Shortable, row.EasyToBorrow, row.Marginable, row.Status) = (asset.Shortable, asset.EasyToBorrow, asset.Marginable, asset.Status);
                }
                else
                {
                    row.Checked0940Utc = nowUtc;
                    (row.Shortable0940, row.EasyToBorrow0940, row.Marginable0940, row.Status0940) = (asset.Shortable, asset.EasyToBorrow, asset.Marginable, asset.Status);
                }
                row.BorrowStatus = asset.BorrowStatus ?? row.BorrowStatus;
                row.RawJson = MergeRaw(row.RawJson, final ? "asset0946" : "asset0940", AssetSnippet(assetJson));
                checkedRows.Add(row);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                failures++;
                log.LogWarning(ex, "Fade-short pilot: {Symbol} failed at {Phase}; continuing.", symbol, phase);
                if (row is not null) row.Note = Trim($"{phase}: {ex.Message}", 400);
            }
        }

        var locateNote = await QuoteLocatesAsync(session, checkedRows, final, ct);
        await db.SaveChangesAsync(ct);

        var fired = final ? checkedRows.Count(r => r.SignalFired) : 0;
        var note = notes.Count == 0 ? "" : $"; {string.Join("; ", notes)}";
        return $"{screened} screened, {checkedRows.Count} at pmHigh >= +{FadeShortPilotMath.MinPmHighPct:0}% checked" +
               (final ? $", {fired} signal(s) fired" : "") + $", {failures} failure(s); locate {locateNote}{note}";
    }

    private async Task<Dictionary<string, string>> CandidatesAsync(IFadeShortBorrowSession session, AppDbContext db, FadeShortPilotConfig cfg,
        DateOnly dateEt, List<string> notes, CancellationToken ct)
    {
        var scored = new Dictionary<string, (double Score, HashSet<string> Sources)>(StringComparer.OrdinalIgnoreCase);
        void Add(string symbol, double score, string source)
        {
            var s = symbol.Trim().ToUpperInvariant();
            if (s.Length == 0) return;
            if (!scored.TryGetValue(s, out var cur)) scored[s] = cur = (score, new HashSet<string>(StringComparer.Ordinal));
            cur.Sources.Add(source);
            scored[s] = (Math.Max(cur.Score, score), cur.Sources);
        }

        try
        {
            foreach (var m in await session.MoversAsync(MoversTop, ct))
                if (m.Percent >= MoverMinPercent) Add(m.Symbol, m.Percent, "movers");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { notes.Add($"movers failed: {Trim(ex.Message, 120)}"); }

        try
        {
            var formation = await db.GapperFormationSnapshots.AsNoTracking()
                .Where(r => r.DateEt == dateEt && r.GapPct != null && r.GapPct >= FormationMinGapPct)
                .GroupBy(r => r.Symbol).Select(g => new { Symbol = g.Key, Gap = g.Max(r => r.GapPct) ?? 0 })
                .ToListAsync(ct);
            foreach (var f in formation) Add(f.Symbol, f.Gap, "formation");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { notes.Add($"formation read failed: {Trim(ex.Message, 120)}"); }

        return scored.OrderByDescending(kv => kv.Value.Score).Take(cfg.MaxCandidates)
            .ToDictionary(kv => kv.Key, kv => string.Join(",", kv.Value.Sources.OrderBy(x => x, StringComparer.Ordinal)), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>One advisory <c>GET /v1/locates/quotes</c> for every checked row; never a locate request.</summary>
    private async Task<string> QuoteLocatesAsync(IFadeShortBorrowSession session, List<FadeShortBorrowObservation> checkedRows, bool final, CancellationToken ct)
    {
        if (checkedRows.Count == 0) return "not queried (no candidates)";
        IReadOnlyDictionary<string, LocateQuote> quotes;
        try
        {
            var root = await session.LocateQuotesAsync(checkedRows.Select(r => r.Symbol).ToList(), ct);
            quotes = FadeShortPilotMath.ParseLocateQuotes(root);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var why = ex is AlpacaApiException api
                ? $"locate quote unavailable on the paper route (HTTP {(int)api.Status}: {Trim(api.AlpacaMessage, 80)})"
                : $"locate quote failed: {Trim(ex.Message, 120)}";
            foreach (var r in checkedRows)
                if (final || !r.LocateQueried) r.LocateNote = Trim(why, 200);
            return why;
        }

        int answered = 0;
        foreach (var r in checkedRows)
        {
            if (!quotes.TryGetValue(r.Symbol, out var q))
            {
                if (final || !r.LocateQueried) r.LocateNote = "no quote returned for this symbol";
                continue;
            }
            answered++;
            r.LocateQueried = true;
            if (final) (r.LocateAvailable, r.LocateSize, r.LocateFee) = (q.Available, q.AvailableQty, q.FeePerShare);
            else (r.LocateAvailable0940, r.LocateSize0940, r.LocateFee0940) = (q.Available, q.AvailableQty, q.FeePerShare);
            if (final || r.LocateNote is null) r.LocateNote = q.ErrorCode is null ? "quoted" : Trim($"{q.ErrorCode}: {q.ErrorMessage}", 200);
            r.RawJson = MergeRaw(r.RawJson, final ? "locate0946" : "locate0940", JsonSerializer.SerializeToNode(new
            {
                q.AvailableQty, q.FeePerShare, q.ErrorCode, q.ErrorMessage,
            }));
        }
        return $"quoted for {answered} of {checkedRows.Count}";
    }

    private bool? SafeRegSho(string dir, string symbol, DateOnly dateEt)
    {
        try { return FadeShortPilotMath.RegShoListedD1(dir, symbol, dateEt); }
        catch (Exception ex)
        {
            log.LogDebug(ex, "Reg SHO lookup failed for {Symbol}", symbol);
            return null;
        }
    }

    /// <summary>The IP-T469 Reg SHO list directory: GAPPER_REGSHO_DIR, else D:\IdiotProofData\regsho-threshold.</summary>
    public static string DefaultRegShoDir() =>
        Environment.GetEnvironmentVariable("GAPPER_REGSHO_DIR") is { Length: > 0 } dir ? dir : @"D:\IdiotProofData\regsho-threshold";

    private static JsonNode? AssetSnippet(JsonElement asset)
    {
        if (asset.ValueKind != JsonValueKind.Object) return null;
        var o = new JsonObject();
        foreach (var name in new[] { "symbol", "exchange", "status", "tradable", "shortable", "easy_to_borrow", "borrow_status", "marginable", "attributes" })
            if (asset.TryGetProperty(name, out var v)) o[name] = JsonNode.Parse(v.GetRawText());
        return o;
    }

    internal static string MergeRaw(string? existing, string key, JsonNode? value)
    {
        JsonObject root;
        try { root = existing is { Length: > 0 } && JsonNode.Parse(existing) is JsonObject o ? o : new JsonObject(); }
        catch (JsonException) { root = new JsonObject(); }
        root[key] = value;
        var text = root.ToJsonString();
        return text.Length <= RawJsonMax ? text : text[..RawJsonMax];
    }

    private static double Round(double v) => Math.Round(v, 4);
    private static DateTime Floor(DateTime utc) => new(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];
}
