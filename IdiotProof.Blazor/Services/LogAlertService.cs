using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Tracks unread ERROR/FATAL audit events so the Logs nav tab can show a badge
/// without the user having to open the page first. Polls the DB every 60 s;
/// resets when the user marks the log as read (visits /logs).
///
/// Thread-safety: lastSeenTicks is a volatile long (ET-safe read/write), and
/// RefreshAsync snapshots it before the await so a concurrent MarkRead() call
/// during the DB round-trip does not re-show dismissed errors.
///
/// Persistence: lastSeenUtc survives app restarts via SettingsKv key
/// "ui.logAlertLastSeen". Errors before the last restart remain visible.
/// </summary>
public sealed class LogAlertService : IDisposable
{
    private readonly IDbContextFactory<AppDbContext> dbFactory;
    private readonly SettingsRepository settings;
    private readonly System.Threading.Timer timer;

    // long (DateTime.Ticks) accessed via Interlocked so background timer and
    // Blazor circuit thread can read/write without a lock. volatile long is
    // illegal in C# (64-bit type); Interlocked.Read/Exchange give the same guarantee.
    private long lastSeenTicks = DateTime.UtcNow.Ticks;
    private volatile int unreadErrors;
    private bool initialized;

    public event Action? Changed;
    public int UnreadErrorCount => unreadErrors;

    private static readonly string[] ErrorCategories = ["strategy-error", "order-rejected"];
    private const string SettingsKey = "ui.logAlertLastSeen";

    public LogAlertService(IDbContextFactory<AppDbContext> dbFactory, SettingsRepository settings)
    {
        this.dbFactory = dbFactory;
        this.settings  = settings;
        timer = new System.Threading.Timer(async _ => await RefreshAsync(), null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60));
    }

    private async Task RefreshAsync()
    {
        try
        {
            // Lazy-init: restore persisted lastSeenUtc from DB on first poll.
            if (!initialized)
            {
                initialized = true;
                var stored = await settings.GetAsync(SettingsKey);
                if (stored is not null &&
                    DateTime.TryParse(stored, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                {
                    Interlocked.Exchange(ref lastSeenTicks, parsed.Ticks);
                }
            }

            // Snapshot before the await so a concurrent MarkRead() advancing
            // lastSeenTicks does not get overwritten with a stale count.
            var sinceTicks = Interlocked.Read(ref lastSeenTicks);
            var since      = new DateTime(sinceTicks, DateTimeKind.Utc);

            await using var db = await dbFactory.CreateDbContextAsync();
            var count = await db.AuditLogs
                .CountAsync(a => ErrorCategories.Contains(a.Category) && a.TimestampUtc > since);

            // Discard result if MarkRead() fired during the DB round-trip.
            if (Interlocked.Read(ref lastSeenTicks) != sinceTicks) return;

            if (count != unreadErrors)
            {
                unreadErrors = count;
                Changed?.Invoke();
            }
        }
        catch { /* non-fatal; badge just stays stale */ }
    }

    public void MarkRead()
    {
        var now = DateTime.UtcNow;
        unreadErrors = 0;
        // Write lastSeenTicks last so the discard-guard in RefreshAsync sees it
        // AFTER unreadErrors has been zeroed (prevents a racing refresh from
        // re-showing dismissed errors).
        Interlocked.Exchange(ref lastSeenTicks, now.Ticks);
        Changed?.Invoke();
        // Persist asynchronously — fire and forget is fine; a missed write means
        // a restart shows old errors again (acceptable vs. blocking the UI thread).
        _ = settings.SetAsync(SettingsKey, now.ToString("O"));
    }

    public void Dispose() => timer.Dispose();
}
