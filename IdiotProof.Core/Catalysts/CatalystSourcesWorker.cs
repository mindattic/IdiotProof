using IdiotProof.Core.Monitoring;
using IdiotProof.Core.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Catalysts;

/// <summary>
/// Runs the IP-T602 intake sources beside <see cref="CatalystIntakeWorker"/>: the press-release wires (PR Newswire,
/// Business Wire, GlobeNewswire, ACCESS Newswire, Newsfile) into the catalyst store, and StockTwits / Reddit into
/// <see cref="Data.SocialSignal"/>. Same gate as the intake (<c>catalyst.intake.enabled</c> and the Monitor leader
/// lease), then each source's own <c>catalyst.intake.sources.&lt;name&gt;</c> switch and, where it needs one, its Vault
/// key. Each source polls on its own cadence and backs off (doubling, at most 15 minutes) after a failure. Research
/// rows only: it never arms or trades.
/// </summary>
public sealed class CatalystSourcesWorker(
    CatalystIntakeSettings intakeSettings,
    CatalystSourceSettings sourceSettings,
    CatalystSourceState state,
    CatalystSourceKeys keys,
    MonitorRuntimeState monitor,
    WireFeedPoller wires,
    StockTwitsPoller stockTwits,
    RedditPoller reddit,
    TimeProvider time,
    ILogger<CatalystSourcesWorker> log) : BackgroundService
{
    public static readonly TimeSpan Cadence = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(15);

    private readonly Dictionary<string, Task> running = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> nextDue = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Cadence, time);
        try
        {
            do
            {
                try { await ReconcileAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { log.LogWarning(ex, "Catalyst sources reconcile failed"); }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        finally
        {
            try { await Task.WhenAll(running.Values); } catch { /* each poll reports its own errors */ }
        }
    }

    public async Task ReconcileAsync(CancellationToken ct)
    {
        var intake = await intakeSettings.ReadAsync(ct);
        var leader = intake.Enabled && await monitor.IsLeaderAsync(ct);
        var sources = await sourceSettings.ReadAsync(ct);
        var now = Now;

        foreach (var name in running.Where(r => r.Value.IsCompleted).Select(r => r.Key).ToList()) running.Remove(name);

        foreach (var config in sources.Values)
        {
            var info = config.Info;
            var busy = running.ContainsKey(info.Name);
            string? blocked =
                !config.Enabled ? $"off ({info.EnabledKey})" :
                !intake.Enabled ? $"off: the intake is disabled ({CatalystIntakeSettings.EnabledKey})" :
                !leader ? "standby (this Hub is not the Monitor leader)" :
                keys.Missing(info) is { } entry ? $"no key in Vault: {entry}" :
                info.Kind == CatalystSourceKind.Wire && config.Urls.Count == 0 ? $"no feed URL ({info.UrlKey})" :
                null;
            if (blocked is not null)
            {
                state.SetStatus(info.Name, blocked, busy);
                continue;
            }
            if (busy) continue;
            DateTime due;
            bool scheduled;
            lock (nextDue) scheduled = nextDue.TryGetValue(info.Name, out due);
            if (scheduled && now < due)
            {
                state.SetStatus(info.Name, "on", false, due);
                continue;
            }
            state.SetStatus(info.Name, "on (polling)", true);
            running[info.Name] = Task.Run(() => PollAsync(config, ct), CancellationToken.None);
        }
    }

    private async Task PollAsync(CatalystSourceConfig config, CancellationToken ct)
    {
        var info = config.Info;
        try
        {
            var summary = info.Name switch
            {
                CatalystSources.StockTwits => await stockTwits.PollOnceAsync(config, ct),
                CatalystSources.Reddit => await reddit.PollOnceAsync(await sourceSettings.SubredditsAsync(ct), ct),
                _ => await wires.PollOnceAsync(config, ct),
            };
            var next = Now + info.Cadence;
            lock (nextDue) nextDue[info.Name] = next;
            state.RecordPoll(info.Name, summary, Now, next);
            state.SetStatus(info.Name, "on", false, next);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            var errors = state.ConsecutiveErrors(info.Name) + 1;
            var wait = TimeSpan.FromTicks(Math.Min(MaxBackoff.Ticks, info.Cadence.Ticks * (1L << Math.Min(errors, 10))));
            var next = Now + wait;
            lock (nextDue) nextDue[info.Name] = next;
            state.RecordError(info.Name, ex.Message, Now, next);
            state.SetStatus(info.Name, $"on; backing off {wait.TotalMinutes:0.#} min after {errors} failure(s)", false, next);
            log.LogWarning("Catalyst source {Source} poll failed: {Message}", info.Name, ex.Message);
        }
    }

    private DateTime Now => time.GetUtcNow().UtcDateTime;
}
