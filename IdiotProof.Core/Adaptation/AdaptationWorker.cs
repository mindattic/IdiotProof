using IdiotProof.Core.Data;
using IdiotProof.Core.Monitoring;
using IdiotProof.Core.Services;
using IdiotProof.DataFeeds;
using IdiotProof.Engine.Settings;
using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Strategies.Backtesting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Adaptation;

/// <summary>
/// Runs <see cref="AdaptationService"/> every few minutes over armed, opted-in PAPER strategies.
/// Kept outside the Monitor's tick loop, and does nothing at all unless the SettingsKv switch
/// <c>adaptation.enabled</c> is "true" (it is absent, i.e. off, by default). The period is the RTH
/// session (9:30 ET); premarket strategies are left alone.
/// </summary>
public sealed class AdaptationWorker(
    AdaptationService adaptation,
    IDbContextFactory<AppDbContext> dbFactory,
    IMarketDataFeed feed,
    TimeProvider time,
    MonitorRuntimeState monitor,
    ILogger<AdaptationWorker> log,
    AppSettings? appSettings = null,
    IdiotProof.Core.Evidence.IHistoricalBarStore? barStore = null) : BackgroundService
{
    private static readonly TimeSpan Cadence = TimeSpan.FromMinutes(5);
    private LeaderContextCache? leaderContextCache;
    private LeaderContextCache Leaders => leaderContextCache ??=
        new(feed, barStore, () => (appSettings ?? new AppSettings()).LeaderEtfs, log);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Cadence, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogWarning(ex, "Adaptation pass failed"); }
        }
    }

    /// <summary>
    /// One adaptation pass, run only by the instance holding the Monitor leader lease (re-checked
    /// against SQL each pass), so a standby never writes duplicate decisions or retunes strategies.
    /// Returns false when the pass was skipped.
    /// </summary>
    public async Task<bool> RunOnceAsync(CancellationToken ct)
    {
        if (!await adaptation.IsGloballyEnabledAsync(ct)) return false;
        if (!await monitor.IsLeaderAsync(ct))
        {
            log.LogDebug("Adaptation pass skipped: this instance does not hold the Monitor leader lease.");
            return false;
        }
        await PassAsync(ct);
        await adaptation.RecordOutcomesAsync(ct);
        return true;
    }

    private async Task PassAsync(CancellationToken ct)
    {
        var nowUtc = time.GetUtcNow().UtcDateTime;
        var nowEt = EasternTime.FromUtc(nowUtc);
        if (nowEt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return;
        var openEt = nowEt.Date.AddHours(9).AddMinutes(30);
        var closeEt = nowEt.Date.AddHours(16);
        if (nowEt < openEt || nowEt > closeEt) return;
        var periodStartUtc = nowUtc - (nowEt - openEt);

        List<Guid> ids;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            ids = await db.Strategies.AsNoTracking()
                .Where(s => s.IsActive && s.BrokerMode == "Paper" && s.PositionQty == 0)
                .Select(s => s.Id).ToListAsync(ct);

        foreach (var id in ids)
        {
            if (!await adaptation.IsOptedInAsync(id, ct)) continue;
            string symbol;
            await using (var db = await dbFactory.CreateDbContextAsync(ct))
                symbol = await db.Strategies.Where(s => s.Id == id).Select(s => s.Symbol).FirstAsync(ct);

            var bars = new List<Candle>();
            await foreach (var c in feed.GetHistoricalCandlesAsync(symbol, periodStartUtc, nowUtc, TimeSpan.FromMinutes(1), ct))
                bars.Add(c);
            var dayEt = DateOnly.FromDateTime(nowEt);
            var result = await adaptation.AdaptAsync(id, bars, periodStartUtc, nowUtc, ct: ct,
                leaders: (def, token) => LeadersAsync(def, dayEt, ids, token),
                priorSessions: barStore is null ? null
                    : token => barStore.GetBarsAsync(symbol, dayEt.AddDays(-14), dayEt.AddDays(-1), IdiotProof.Core.Evidence.BarTimeframe.Minute1, token));
            log.LogInformation("Adaptation {Symbol} {Strategy}: {Outcome} ({Reason})", symbol, id, result.Outcome, result.Reason);
        }
    }

    /// <summary>Prior-session leaders for a leader-gated strategy; a failure leaves the guards fail-closed.</summary>
    private async Task<BacktestLeaderContext?> LeadersAsync(StrategyDefinition def, DateOnly dayEt, IReadOnlyList<Guid> armed, CancellationToken ct)
    {
        try
        {
            List<string> peers;
            await using (var db = await dbFactory.CreateDbContextAsync(ct))
                peers = await db.Strategies.AsNoTracking().Where(s => armed.Contains(s.Id)).Select(s => s.Symbol).Distinct().ToListAsync(ct);
            return await Leaders.ForDayAsync(def.Symbol, dayEt, peers, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Adaptation leader context for {Symbol} failed; leader guards block in the shadow replay.", def.Symbol);
            return null;
        }
    }
}
