using IdiotProof.Core.Monitoring;
using IdiotProof.Core.Services;
using IdiotProof.Scripting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.History;

/// <summary>
/// Hub-hosted history keeper (IP-T454), in the shape of the other leader-only workers: idle unless
/// SettingsKv <c>history.backfill.enabled</c> is "true" and this instance holds the Monitor leader
/// lease. When on: every ten minutes it queues any newly met ticker for five years of history, after
/// <see cref="NightlyEt"/> it queues the last session for every held symbol and runs the market-wide splits and EDGAR
/// filings pass, and between those it runs
/// queued jobs one at a time. Writes only to the history database; never trades.
/// <para>
/// When the history database is on Azure SQL Database (IP-T542) all of it happens only inside the database's
/// online window (<see cref="HistoryOnlineWindow"/>): the worker stays off the database outside it, and the
/// nightly pass moves from <see cref="NightlyEt"/> to shortly after the session close, inside the window's tail.
/// </para>
/// </summary>
public sealed class HistoryBackfillWorker(
    MonitorRuntimeState monitor,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<HistoryBackfillWorker> log) : BackgroundService
{
    public const string EnabledKey = "history.backfill.enabled";
    public static readonly TimeSpan Cadence = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DiscoverEvery = TimeSpan.FromMinutes(10);
    public static readonly TimeOnly NightlyEt = new(20, 15);

    private DateTime lastDiscoverUtc = DateTime.MinValue;
    private DateOnly lastNightly;
    private bool requeued;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Cadence, time);
        do
        {
            try { await TickAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (SqlException ex) { log.LogWarning("History backfill: history database unavailable ({Message}).", ex.Message); }
            catch (Exception ex) { log.LogWarning(ex, "History backfill tick failed."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<bool> EnabledAsync(SettingsRepository settings, CancellationToken ct) =>
        string.Equals(await settings.GetAsync(EnabledKey, ct), "true", StringComparison.OrdinalIgnoreCase) && await monitor.IsLeaderAsync(ct);

    public async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsRepository>();
        if (!await EnabledAsync(settings, ct)) return;
        var now = time.GetUtcNow().UtcDateTime;

        // IP-T542: on Azure SQL Database the history database is serverless and sleeps outside its online window
        // (trading days, 03:30-20:30 ET by default), so nothing here may query it then. Null on a local server,
        // where every line below runs at any hour exactly as before.
        var azure = scope.ServiceProvider.GetService<HistoryOnlineWindow>() is { IsAzure: true } window ? window : null;
        var online = azure is null ? null : await azure.ReadAsync(ct);
        if (online is not null && !online.IsOnline(now)) return;

        var history = scope.ServiceProvider.GetRequiredService<HistoryDatabase>();
        await history.EnsureSchemaAsync(ct);
        var backfill = scope.ServiceProvider.GetRequiredService<HistoryBackfillService>();

        if (!requeued)
        {
            await backfill.RequeueStaleAsync(TimeSpan.FromMinutes(1), ct);
            requeued = true;
        }
        if (now - lastDiscoverUtc >= DiscoverEvery)
        {
            await backfill.DiscoverAsync(ct);
            lastDiscoverUtc = now;
        }
        var et = TimeZoneInfo.ConvertTimeFromUtc(now, MarketTime.Eastern);
        var todayEt = DateOnly.FromDateTime(et);
        // On Azure the nightly pass runs inside the window's tail (session close + history.backfill.nightlyAfterCloseMinutes).
        var nightlyEt = online is { Enforced: true } ? online.NightlyEt(todayEt) : NightlyEt;
        if (TimeOnly.FromDateTime(et) >= nightlyEt && lastNightly != todayEt)
        {
            var n = await backfill.QueueNightlyTopUpAsync(ct);
            lastNightly = todayEt;
            log.LogInformation("History backfill: nightly top-up queued {Count} symbols.", n);
            await backfill.TopUpReferenceAsync(ct: ct); // splits and EDGAR filings for the whole market since the last pass
        }

        // Run jobs until the queue is empty or the switch goes off; each job is resumable.
        // On Azure the queue also stops at the end of the online window; what is left is picked up at the next window.
        while (!ct.IsCancellationRequested && await EnabledAsync(settings, ct) && (azure is null || await azure.IsOpenAsync(ct))
               && await backfill.ClaimAsync(ct) is { } job)
        {
            var r = await backfill.RunAsync(job, ct);
            log.LogInformation("History backfill {Symbol} ({Reason}): {Daily} daily, {Minute} minute bars over {Months} months, {News} news, {Splits} splits, {Filings} filings{Error}.",
                job.Symbol, job.Reason, r.DailyBars, r.MinuteBars, r.MonthsFetched, r.NewsItems, r.Splits, r.Filings, r.Error is null ? "" : "; failed: " + r.Error);
        }
    }
}
