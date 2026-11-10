using IdiotProof.Core.Data;
using IdiotProof.Core.Ledger;
using IdiotProof.Operations;
using IdiotProof.Scripting;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Hub;

/// <summary>One scheduled step: an operation's argv, run at each ET time of day listed.</summary>
/// <param name="TradingDaysOnly">Run only on NYSE trading days (else every day: evening research runs on Sundays for Monday).</param>
public sealed record HubJob(string Name, IReadOnlyList<string> Argv, IReadOnlyList<TimeSpan> TimesEt, bool TradingDaysOnly);

/// <summary>
/// The Hub's 24/7 job runner (IP-T512). The Hub is the one process that talks to Alpaca and the
/// database, so the research and arming steps that used to be run by hand or by separate tools run
/// here, on an ET schedule, as ordinary operations through the Hub's own dispatcher: owner
/// resolution, the transaction gateway's gates, the command ledger and the console echo all apply
/// exactly as for a CLI call (caller "scheduler").
/// Runs only in the Hub that hosts the Monitor (a dev Hub with IDIOTPROOF_HUB_MONITOR=0 never does)
/// and can be switched off with IDIOTPROOF_HUB_SCHEDULER=0. A missed time is not caught up after a
/// restart; each job runs at most once per listed time per day.
/// </summary>
public sealed class HubScheduler(IServiceScopeFactory scopes, HubSettings settings, ILogger<HubScheduler> logger) : BackgroundService
{
    public const string EnabledEnvVar = "IDIOTPROOF_HUB_SCHEDULER";

    /// <summary>How late after its time a job may still start (a tick, a long previous job).</summary>
    private static readonly TimeSpan StartWindow = TimeSpan.FromMinutes(10);

    private static TimeSpan T(int h, int m, int s = 0) => new(h, m, s);

    /// <summary>
    /// The schedule (ET). Evenings: import and extract Momentum.'s new videos as they land (his
    /// watchlist usually posts 19:00-21:00), verify finished sessions, then rank the next session's
    /// possible gappers into the "Gapper radar" Alpaca paper watchlist. Mornings: pick up his morning
    /// update, re-rank with it, and at 09:29:30 arm the paper forward test of his picks (amendment 04;
    /// Paper, 1 share, never Live).
    /// </summary>
    public static readonly IReadOnlyList<HubJob> Jobs =
    [
        new("momentum import", ["momentum", "import"], [T(7, 0), T(8, 0), T(8, 45), T(19, 15), T(20, 0), T(20, 45), T(21, 30), T(22, 30)], TradingDaysOnly: false),
        new("momentum extract", ["momentum", "extract"], [T(7, 2), T(8, 2), T(8, 47), T(19, 17), T(20, 2), T(20, 47), T(21, 32), T(22, 32)], TradingDaysOnly: false),
        new("momentum verify", ["momentum", "verify"], [T(20, 10)], TradingDaysOnly: false),
        new("momentum report", ["momentum", "report"], [T(20, 40)], TradingDaysOnly: false),
        new("gapper radar", ["gapper", "radar", "--top", "50", "--watchlist", "Gapper radar", "--save"], [T(20, 20), T(21, 45), T(22, 45), T(8, 55)], TradingDaysOnly: false),
        new("gapper momentum-arm", ["gapper", "momentum-arm", "--activate"], [T(9, 29, 30)], TradingDaysOnly: true),
    ];

    private readonly HashSet<string> fired = [];

    public static bool Enabled(HubSettings settings) =>
        settings.MonitorHosted && Environment.GetEnvironmentVariable(EnabledEnvVar) != "0";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Enabled(settings)) return;
        HubConsole.Line($"Scheduler on: {Jobs.Count} jobs ({string.Join(", ", Jobs.Select(j => j.Name))}); set {EnabledEnvVar}=0 to turn it off.");
        // Times already past at startup are not caught up: mark them fired for today.
        MarkPast(DateTime.UtcNow);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await TickAsync(DateTime.UtcNow, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Hub scheduler tick failed."); }
        }
    }

    private void MarkPast(DateTime utc)
    {
        var et = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), MarketTime.Eastern);
        foreach (var job in Jobs)
            foreach (var t in job.TimesEt.Where(t => t <= et.TimeOfDay))
                fired.Add(Key(job, DateOnly.FromDateTime(et), t));
    }

    private static string Key(HubJob job, DateOnly day, TimeSpan t) => $"{job.Name}|{day:yyyy-MM-dd}|{t}";

    /// <summary>The jobs due at <paramref name="utc"/> (pure: used by the tick and by tests).</summary>
    public static IEnumerable<(HubJob Job, TimeSpan At)> Due(DateTime utc, IReadOnlySet<string> alreadyFired)
    {
        var et = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), MarketTime.Eastern);
        var day = DateOnly.FromDateTime(et);
        foreach (var job in Jobs)
        {
            if (job.TradingDaysOnly && !MarketTime.IsEquityTradingDay(utc)) continue;
            foreach (var t in job.TimesEt)
                if (et.TimeOfDay >= t && et.TimeOfDay < t + StartWindow && !alreadyFired.Contains(Key(job, day, t)))
                    yield return (job, t);
        }
    }

    private async Task TickAsync(DateTime utc, CancellationToken ct)
    {
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), MarketTime.Eastern));
        foreach (var (job, at) in Due(utc, fired).ToList())
        {
            fired.Add(Key(job, day, at));
            await RunAsync(job, ct);
        }
        // forget yesterday's keys
        fired.RemoveWhere(k => !k.Contains($"|{day:yyyy-MM-dd}|", StringComparison.Ordinal));
    }

    private async Task RunAsync(HubJob job, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var request = new HubOperationRequest { Argv = job.Argv, Caller = OperationCaller.Scheduler, Output = OutputMode.Human, Width = 160 };
        try
        {
            var (_, result) = await HubEndpoints.ExecuteAsync(request, null, sp.GetRequiredService<OperationDispatcher>(),
                sp.GetRequiredService<IDbContextFactory<AppDbContext>>(), sp.GetRequiredService<CommandLedgerRepository>(), settings,
                sp.GetRequiredService<ILoggerFactory>(), ct);
            if (!result.IsOk)
                logger.LogWarning("Scheduled {Job} finished {Status}: {Message}", job.Name, result.Status, result.Message);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scheduled {Job} failed.", job.Name);
        }
    }
}
