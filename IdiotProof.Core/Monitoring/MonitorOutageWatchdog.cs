using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using IdiotProof.Scripting;
namespace IdiotProof.Core.Monitoring;

/// <summary>
/// Watches <see cref="MonitorRuntimeState"/> from outside the tick loop and texts the owner when the
/// Monitor stops evaluating: ticks failing, the SQL leader lease lost and not re-acquired, the lease
/// never acquired, or no tick finishing at all (IP-T359). While that lasts, Monitor-managed stops and
/// exits are not enforced, so it must be heard even when the IdiotProof DB is the thing that is down:
/// the alert goes through <see cref="EmailSmsAlertSender"/> (Vault Notifications files + SMTP), never SQL.
/// One alert per outage, a reminder every <see cref="ReminderEvery"/>, and a recovery text when ticks
/// succeed again. It runs on its own loop, so nothing here can throw into the Monitor.
/// </summary>
public sealed class MonitorOutageWatchdog(
    MonitorRuntimeState runtime,
    EmailSmsAlertSender alerts,
    TimeProvider clock,
    ILogger<MonitorOutageWatchdog> logger) : BackgroundService
{
    public const int FailedTicksThreshold = 3;
    public static readonly TimeSpan FailingFor = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan LeaseLostFor = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan NeverLeadFor = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MinStall = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ReminderEvery = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan RetryFailedSendEvery = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(30);

    private DateTime? outageSinceUtc;
    private DateTime? lastSentUtc;
    private DateTime? lastAttemptUtc;
    private bool alertDelivered;

    /// <summary>Why the Monitor is not evaluating right now, or null when it is healthy (or deliberately stopped).</summary>
    public static string? Assess(MonitorRuntimeSnapshot s, DateTime nowUtc)
    {
        if (!s.Running) return null;

        if (s.LeaseHeld)
        {
            if (s.ConsecutiveFailures >= FailedTicksThreshold)
                return $"{s.ConsecutiveFailures} ticks failed in a row ({Short(s.LastError)})";
            if (s.ConsecutiveFailures > 0 && (s.LastTickSuccessUtc ?? s.LeaseAcquiredUtc) is { } okUtc && nowUtc - okUtc >= FailingFor)
                return $"ticks failing for {Span(nowUtc - okUtc)} ({Short(s.LastError)})";

            var stall = TimeSpan.FromSeconds(Math.Max(MinStall.TotalSeconds, s.IntervalSeconds * 4));
            if ((s.LastTickUtc ?? s.LeaseAcquiredUtc) is { } lastUtc && nowUtc - lastUtc >= stall)
                return $"no tick has finished for {Span(nowUtc - lastUtc)} (loop stuck?)";
            return null;
        }

        if (s.LeaseReleasedUtc is { } lostUtc && nowUtc - lostUtc >= LeaseLostFor)
            return $"lost its SQL leader lease {Span(nowUtc - lostUtc)} ago and has not re-acquired it (DB unreachable?)";
        if (s.LeaseAcquiredUtc is null && s.StartedUtc is { } startUtc && nowUtc - startUtc >= NeverLeadFor)
            return $"has not acquired its SQL leader lease in {Span(nowUtc - startUtc)} since start (DB unreachable, or another Monitor leads)";
        return null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await CheckOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Monitor outage watchdog check failed."); }

            try { await Task.Delay(PollEvery, clock, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>One look at the Monitor; sends at most one text.</summary>
    public async Task CheckOnceAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var reason = Assess(runtime.Snapshot(), now);

        if (reason is not null)
        {
            if (outageSinceUtc is null)
            {
                outageSinceUtc = now;
                alertDelivered = false;
                lastSentUtc = null;
                lastAttemptUtc = null;
                logger.LogCritical("MONITOR OUTAGE: the Monitor {Reason}. Monitor-managed stops and exits are NOT being enforced.", reason);
            }

            var due = !alertDelivered
                ? lastAttemptUtc is not { } tried || now - tried >= RetryFailedSendEvery
                : lastSentUtc is { } sent && now - sent >= ReminderEvery;
            if (!due) return;

            var text = alertDelivered
                ? $"IdiotProof ALERT: Monitor still down after {Span(now - outageSinceUtc.Value)} - {reason}. Stops/exits not enforced."
                : $"IdiotProof ALERT: Monitor down since {Et(outageSinceUtc.Value)} - {reason}. Stops/exits not enforced.";
            lastAttemptUtc = now;
            if (await TrySendAsync(text, ct))
            {
                alertDelivered = true;
                lastSentUtc = now;
            }
            return;
        }

        if (outageSinceUtc is not { } since) return;
        outageSinceUtc = null;
        logger.LogWarning("Monitor recovered after {Duration}.", Span(now - since));
        if (alertDelivered)
            await TrySendAsync($"IdiotProof: Monitor recovered at {Et(now)} after {Span(now - since)} down. Check open positions.", ct);
        alertDelivered = false;
    }

    private async Task<bool> TrySendAsync(string text, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(SendTimeout);
        try { return await alerts.TrySendAsync(text, timeout.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Monitor outage alert timed out after {Seconds}s.", SendTimeout.TotalSeconds);
            return false;
        }
    }

    private static string Short(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return "no error text";
        var oneLine = error.ReplaceLineEndings(" ").Trim();
        return oneLine.Length <= 70 ? oneLine : oneLine[..67] + "...";
    }

    private static string Span(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m"
        : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}m {t.Seconds}s"
        : $"{(int)t.TotalSeconds}s";

    private static string Et(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), MarketTime.Eastern).ToString("h:mm tt") + " ET";
}
