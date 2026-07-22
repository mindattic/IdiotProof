using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Tracks unread ERROR/FATAL audit events so the Logs nav tab can show a badge
/// without the user having to open the page first. Polls the DB every 60 s;
/// resets when the user marks the log as read (visits /logs).
/// </summary>
public sealed class LogAlertService : IDisposable
{
    private readonly IDbContextFactory<AppDbContext> dbFactory;
    private readonly System.Threading.Timer timer;
    private DateTime lastSeenUtc = DateTime.UtcNow;
    private int unreadErrors;

    public event Action? Changed;
    public int UnreadErrorCount => unreadErrors;

    private static readonly string[] ErrorCategories = ["strategy-error", "order-rejected"];

    public LogAlertService(IDbContextFactory<AppDbContext> dbFactory)
    {
        this.dbFactory = dbFactory;
        timer = new System.Threading.Timer(async _ => await RefreshAsync(), null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60));
    }

    private async Task RefreshAsync()
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var count = await db.AuditLogs
                .CountAsync(a => ErrorCategories.Contains(a.Category) && a.TimestampUtc > lastSeenUtc);
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
        lastSeenUtc = DateTime.UtcNow;
        unreadErrors = 0;
        Changed?.Invoke();
    }

    public void Dispose() => timer.Dispose();
}
