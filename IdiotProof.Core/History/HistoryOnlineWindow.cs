using System.Globalization;
using IdiotProof.Core.Hosting;
using IdiotProof.Core.Services;
using IdiotProof.Scripting;

namespace IdiotProof.Core.History;

/// <summary>The <c>history.online.*</c> settings as one snapshot (IP-T542). Times are ET.</summary>
public sealed record HistoryOnlineConfig
{
    /// <summary>False = no window: the history database may be used at any hour and the Hub neither wakes nor holds it.</summary>
    public bool Enforced { get; init; } = true;
    public int LeadMinutes { get; init; } = 30;
    public int TailMinutes { get; init; } = 30;
    public int KeepAliveMinutes { get; init; } = 20;
    public int WakeRetrySeconds { get; init; } = 60;
    public int NightlyAfterCloseMinutes { get; init; } = 5;

    public TimeSpan Lead => TimeSpan.FromMinutes(LeadMinutes);
    public TimeSpan Tail => TimeSpan.FromMinutes(TailMinutes);
    public TimeSpan KeepAlive => TimeSpan.FromMinutes(KeepAliveMinutes);
    public TimeSpan WakeRetry => TimeSpan.FromSeconds(WakeRetrySeconds);

    /// <summary>Whether <paramref name="utcNow"/> is inside the online window (always true when not enforced).</summary>
    public bool IsOnline(DateTime utcNow) => !Enforced || TradingSchedule.IsDatabaseOnline(utcNow, Lead, Tail);

    /// <summary>
    /// When the history keeper's nightly pass runs on <paramref name="etDay"/> when the database is on Azure: the
    /// session close plus <see cref="NightlyAfterCloseMinutes"/>, kept inside the tail so the pass starts before
    /// the window ends.
    /// </summary>
    public TimeOnly NightlyEt(DateOnly etDay)
    {
        var after = Math.Clamp(NightlyAfterCloseMinutes, 0, Math.Max(0, TailMinutes - 1));
        return TimeOnly.FromTimeSpan(TradingSchedule.SessionClose(etDay) + TimeSpan.FromMinutes(after));
    }

    /// <summary>"03:30-20:30 ET" for a trading day, "closed (weekend or market holiday)" otherwise.</summary>
    public string Describe(DateOnly etDay) =>
        TradingSchedule.DatabaseOnlineWindow(etDay, Lead, Tail) is { } w
            ? $"{Hhmm(w.Start)}-{Hhmm(w.End)} ET"
            : "closed (weekend or market holiday)";

    private static string Hhmm(TimeSpan t) => string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalHours:00}:{t.Minutes:00}");
}

/// <summary>
/// The hours the history database is kept awake (IP-T542). On Azure SQL Database the history database is serverless:
/// billed per second while awake, auto-paused when idle, and up to about a minute to resume. So on trading days it
/// is online from premarket open minus a lead to the after-hours close plus a tail (03:30-20:30 ET by default,
/// 03:30-17:30 on an early-close day; never a weekend or market holiday), and outside those hours nothing scheduled
/// may query it, so it pauses and costs nothing. The calendar is <see cref="TradingSchedule"/>'s.
/// <para>
/// Everything here applies only when the history connection string names an Azure SQL Database host
/// (<see cref="IsAzure"/>, told from the server name so asking never wakes the database). On a local SQL Server
/// <see cref="IsOpenAsync"/> is always true and nothing changes.
/// </para>
/// </summary>
public sealed class HistoryOnlineWindow(HistoryDatabase history, SettingsRepository settings, TimeProvider time)
{
    public const string EnforcedKey = "history.online.enforced";
    public const string LeadMinutesKey = "history.online.leadMinutes";
    public const string TailMinutesKey = "history.online.tailMinutes";
    public const string KeepAliveMinutesKey = "history.online.keepAliveMinutes";
    public const string WakeRetrySecondsKey = "history.online.wakeRetrySeconds";
    public const string NightlyAfterCloseMinutesKey = "history.backfill.nightlyAfterCloseMinutes";

    public const int MaxLeadOrTailMinutes = 240;

    /// <summary>The longest keep-alive spacing accepted: under the shortest serverless auto-pause delay worth using (60 minutes).</summary>
    public const int MaxKeepAliveMinutes = 55;

    /// <summary>How long a settings read is reused, so a per-second caller costs one read a minute.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly Lock gate = new();
    private HistoryOnlineConfig cached = new();
    private DateTime cachedAtUtc = DateTime.MinValue;

    /// <summary>True when the history database is on Azure SQL Database. False on a local server: no window applies.</summary>
    public bool IsAzure { get; } = SqlConnect.LooksLikeAzureSqlDatabase(history.ConnectionString);

    /// <summary>
    /// The current settings. Never throws for a failed read: the last values read (the defaults before any) are
    /// kept and the read is tried again after <see cref="Ttl"/>, so a caller's gate never fails on the app database.
    /// </summary>
    public async Task<HistoryOnlineConfig> ReadAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        lock (gate)
        {
            if (now - cachedAtUtc < Ttl) return cached;
        }
        HistoryOnlineConfig? fresh = null;
        try
        {
            var d = new HistoryOnlineConfig();
            fresh = new HistoryOnlineConfig
            {
                Enforced = await settings.GetBoolAsync(EnforcedKey, ct).ConfigureAwait(false) ?? d.Enforced,
                LeadMinutes = Math.Clamp(await settings.GetIntAsync(LeadMinutesKey, ct).ConfigureAwait(false) ?? d.LeadMinutes, 0, MaxLeadOrTailMinutes),
                TailMinutes = Math.Clamp(await settings.GetIntAsync(TailMinutesKey, ct).ConfigureAwait(false) ?? d.TailMinutes, 0, MaxLeadOrTailMinutes),
                KeepAliveMinutes = Math.Clamp(await settings.GetIntAsync(KeepAliveMinutesKey, ct).ConfigureAwait(false) ?? d.KeepAliveMinutes, 1, MaxKeepAliveMinutes),
                WakeRetrySeconds = Math.Clamp(await settings.GetIntAsync(WakeRetrySecondsKey, ct).ConfigureAwait(false) ?? d.WakeRetrySeconds, 5, 3600),
                NightlyAfterCloseMinutes = Math.Clamp(await settings.GetIntAsync(NightlyAfterCloseMinutesKey, ct).ConfigureAwait(false) ?? d.NightlyAfterCloseMinutes, 0, MaxLeadOrTailMinutes),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // keep what was read last; retried after Ttl
        }
        lock (gate)
        {
            if (fresh is not null) cached = fresh;
            cachedAtUtc = now;
            return cached;
        }
    }

    /// <summary>
    /// Whether scheduled work may query the history database now: always on a local server, and on Azure only
    /// inside the online window.
    /// </summary>
    public async Task<bool> IsOpenAsync(CancellationToken ct = default) =>
        !IsAzure || (await ReadAsync(ct).ConfigureAwait(false)).IsOnline(time.GetUtcNow().UtcDateTime);
}
