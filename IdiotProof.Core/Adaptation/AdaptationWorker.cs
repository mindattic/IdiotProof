using IdiotProof.Core.Data;
using IdiotProof.Core.Monitoring;
using IdiotProof.Core.Services;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
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
    ILogger<AdaptationWorker> log) : BackgroundService
{
    private static readonly TimeSpan Cadence = TimeSpan.FromMinutes(5);

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
            var result = await adaptation.AdaptAsync(id, bars, periodStartUtc, nowUtc, ct: ct);
            log.LogInformation("Adaptation {Symbol} {Strategy}: {Outcome} ({Reason})", symbol, id, result.Outcome, result.Reason);
        }
    }
}
