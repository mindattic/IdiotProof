using IdiotProof.Core.Monitoring;
using IdiotProof.Core.Services;
using IdiotProof.Scripting;
using Microsoft.Data.SqlClient;

namespace IdiotProof.Core.History;

/// <summary>
/// Wakes the history database at the start of its online window and keeps it from auto-pausing until the window
/// ends (IP-T542). Azure SQL serverless only: on a local server <see cref="TickAsync"/> returns at once.
/// <para>
/// The Monitor calls <see cref="TickAsync"/> at the top of every tick, so it runs only while this Hub holds the
/// leader lease, overnight included. Once per trading day, on the first tick inside the window (03:30 ET by
/// default, or the first tick after a Hub that starts mid-window takes the lease), it opens the database with the
/// retrying open and runs one trivial query, so the 03:55 ET formation recorder and the Monitor's first reads never
/// race a cold resume. After that it runs the same trivial query every <c>history.online.keepAliveMinutes</c>,
/// because a serverless database pauses after its idle delay even in the middle of the session.
/// </para>
/// <para>
/// The open and the query run on their own task: a tick only starts them, so a slow resume never holds up an
/// evaluation pass, and nothing here throws into the Monitor. A wake that fails is logged as an error, written to
/// the audit log and texted to the owner once per day, then tried again every <c>history.online.wakeRetrySeconds</c>
/// until the database answers.
/// </para>
/// </summary>
public sealed class HistoryWakeKeeper(
    HistoryDatabase history,
    HistoryOnlineWindow window,
    EmailSmsAlertSender alerts,
    AuditLogRepository audit,
    TimeProvider time,
    ILogger<HistoryWakeKeeper> log)
{
    public const string AuditWake = "history-wake";
    public const string AuditWakeFailed = "history-wake-failed";
    private static readonly TimeSpan AlertTimeout = TimeSpan.FromSeconds(30);

    // Written by the one task in flight and read by the next tick only after that task has completed.
    private Task? inFlight;
    private DateOnly wokenDay;
    private DateOnly alertedDay;
    private DateTime lastAnswerUtc = DateTime.MinValue;
    private DateTime nextWakeAttemptUtc = DateTime.MinValue;

    /// <summary>The ET day the database last answered a wake, for status output; default when it has not.</summary>
    public DateOnly WokenDay => wokenDay;

    /// <summary>One look; starts at most one background open. Never throws except for cancellation.</summary>
    public async Task TickAsync(CancellationToken ct)
    {
        if (!window.IsAzure) return;
        if (inFlight is { IsCompleted: false }) return;
        try
        {
            var config = await window.ReadAsync(ct).ConfigureAwait(false);
            var now = time.GetUtcNow().UtcDateTime;
            if (!config.Enforced || !config.IsOnline(now)) return;
            var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, MarketTime.Eastern));

            if (wokenDay != day)
            {
                if (now < nextWakeAttemptUtc) return;
                inFlight = Task.Run(() => WakeAsync(day, config, ct), CancellationToken.None);
            }
            else if (now - lastAnswerUtc >= config.KeepAlive)
            {
                inFlight = Task.Run(() => KeepAliveAsync(ct), CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            log.LogWarning(ex, "History database wake check failed; trying again on the next tick.");
        }
    }

    private async Task TouchAsync(CancellationToken ct)
    {
        await using var c = await history.OpenAsync(ct).ConfigureAwait(false); // waits out a resume (SqlConnect retry policy)
        await using var q = new SqlCommand("SELECT 1", c);
        await q.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }

    private async Task WakeAsync(DateOnly day, HistoryOnlineConfig config, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var database = history.DatabaseName;
        log.LogInformation("History database wake: opening {Database} for the {Day:yyyy-MM-dd} session (online window {Window}).",
            database, day, config.Describe(day));
        try
        {
            await TouchAsync(ct).ConfigureAwait(false);
            var seconds = time.GetElapsedTime(started).TotalSeconds;
            lastAnswerUtc = time.GetUtcNow().UtcDateTime;
            wokenDay = day;
            log.LogInformation("History database wake: {Database} answered after {Seconds:F1}s.", database, seconds);
            await TryAuditAsync(AuditWake, $"History database {database} awake for the {day:yyyy-MM-dd} session after {seconds:F1}s (online window {config.Describe(day)}).");
            if (alertedDay == day)
                await TryAlertAsync($"IdiotProof: history database {database} is awake again ({seconds:F0}s).", ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown or the leader lease was lost; the next leader's first tick wakes it.
        }
        catch (Exception ex)
        {
            var seconds = time.GetElapsedTime(started).TotalSeconds;
            nextWakeAttemptUtc = time.GetUtcNow().UtcDateTime + config.WakeRetry;
            log.LogError(ex, "HISTORY DATABASE WAKE FAILED: {Database} did not answer after {Seconds:F0}s ({Message}). Formation reference reads fail and bars fall back to the app cache until it does; trying again in {Retry}s.",
                database, seconds, ex.Message.ReplaceLineEndings(" "), config.WakeRetrySeconds);
            if (alertedDay != day)
            {
                alertedDay = day;
                await TryAuditAsync(AuditWakeFailed, $"History database {database} did not answer the {day:yyyy-MM-dd} wake after {seconds:F0}s: {ex.Message.ReplaceLineEndings(" ")}");
                await TryAlertAsync($"IdiotProof ALERT: history database {database} did not wake for the {day:MMM d} session ({Short(ex.Message)}). Retrying every {config.WakeRetrySeconds}s.", ct).ConfigureAwait(false);
            }
        }
    }

    private async Task KeepAliveAsync(CancellationToken ct)
    {
        try
        {
            await TouchAsync(ct).ConfigureAwait(false);
            lastAnswerUtc = time.GetUtcNow().UtcDateTime;
            log.LogDebug("History database keep-alive answered.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            // Back to the wake path, which retries, logs loudly and alerts.
            log.LogWarning("History database keep-alive failed ({Message}); waking it again.", ex.Message.ReplaceLineEndings(" "));
            wokenDay = default;
        }
    }

    private async Task TryAuditAsync(string category, string message)
    {
        try { await audit.LogAsync(category, message, ct: CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) { log.LogWarning(ex, "Failed to write the {Category} audit row.", category); }
    }

    private async Task TryAlertAsync(string text, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(AlertTimeout);
        try
        {
            if (!await alerts.TrySendAsync(text, timeout.Token).ConfigureAwait(false))
                log.LogWarning("History database alert was not delivered: {Text}", text);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "History database alert failed: {Text}", text);
        }
        catch (OperationCanceledException) { }
    }

    private static string Short(string message)
    {
        var oneLine = message.ReplaceLineEndings(" ").Trim();
        return oneLine.Length <= 70 ? oneLine : oneLine[..67] + "...";
    }
}
