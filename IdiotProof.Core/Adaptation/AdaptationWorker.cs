using IdiotProof.Core.Data;
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
                if (!await adaptation.IsGloballyEnabledAsync(stoppingToken)) continue;
                await PassAsync(stoppingToken);
                await adaptation.RecordOutcomesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogWarning(ex, "Adaptation pass failed"); }
        }
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
