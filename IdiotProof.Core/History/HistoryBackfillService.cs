using System.Data;
using IdiotProof.Core.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Scripting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.History;

public sealed record BackfillJob(long Id, string Symbol, string Reason, DateOnly FromEt, DateOnly ToEt, string Status);

public sealed record BackfillRunResult(BackfillJob Job, int DailyBars, int MinuteBars, int MonthsFetched, string? Error);

/// <summary>
/// Keeps the permanent history complete (IP-T454). Every ticker the app has seen (a strategy, a
/// tracked ticker, a gapper candidate, or one named by hand) is queued once for
/// <see cref="Years"/> years of daily and 1-minute extended-hours bars; a nightly pass queues the
/// last session for every symbol already held. Jobs run one at a time through the same read-through
/// <see cref="HistoricalBarStore"/>, so a day already held is never fetched again, paced month by month
/// so a backfill never crowds out the live Monitor's share of the data API.
/// </summary>
public sealed class HistoryBackfillService(
    HistoryDatabase history,
    IMarketDataFeed feed,
    IDbContextFactory<AppDbContext> appDb,
    TimeProvider time,
    ILogger<HistoryBackfillService> log,
    ISplitSource? splits = null)
{
    public const int Years = 5;
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Done = "done";
    public const string Failed = "failed";

    /// <summary>Pause between monthly minute-bar requests.</summary>
    public TimeSpan MonthPause { get; init; } = TimeSpan.FromSeconds(2);

    private DateOnly TodayEt => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(time.GetUtcNow().UtcDateTime, MarketTime.Eastern));

    /// <summary>Queue symbols for [today - years, yesterday] unless a pending or running job exists or the daily range is already held. Returns the symbols queued.</summary>
    public async Task<IReadOnlyList<string>> EnqueueAsync(IEnumerable<string> symbols, string reason, int years = Years, CancellationToken ct = default)
    {
        var to = TodayEt.AddDays(-1);
        var from = to.AddYears(-years);
        var queued = new List<string>();
        await using var c = await history.OpenAsync(ct).ConfigureAwait(false);
        foreach (var raw in symbols.Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length is > 0 and <= 16).Distinct())
        {
            await using var q = new SqlCommand("""
                IF NOT EXISTS (SELECT 1 FROM dbo.BackfillJobs WHERE Symbol = @s AND Status IN ('pending', 'running'))
                   AND NOT EXISTS (SELECT 1 FROM dbo.CoverageRanges WHERE Symbol = @s AND Kind = '1Day' AND FromEt <= @a AND ToEt >= DATEADD(day, -7, @b))
                BEGIN
                    INSERT dbo.BackfillJobs (Symbol, Reason, FromEt, ToEt, Status, RequestedUtc) VALUES (@s, @r, @a, @b, 'pending', SYSUTCDATETIME());
                    SELECT 1;
                END
                """, c);
            q.Parameters.Add("@s", SqlDbType.VarChar, 16).Value = raw;
            q.Parameters.Add("@r", SqlDbType.NVarChar, 200).Value = reason.Length > 200 ? reason[..200] : reason;
            q.Parameters.Add("@a", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
            q.Parameters.Add("@b", SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
            if (await q.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null) queued.Add(raw);
        }
        if (queued.Count > 0) log.LogInformation("History backfill: queued {Count} symbols ({Reason}).", queued.Count, reason);
        return queued;
    }

    /// <summary>Every symbol the app has met: strategies, tracked tickers and gapper candidates.</summary>
    public async Task<IReadOnlyList<string>> KnownSymbolsAsync(CancellationToken ct)
    {
        await using var db = await appDb.CreateDbContextAsync(ct).ConfigureAwait(false);
        var strategies = await db.Strategies.AsNoTracking().Select(s => s.Symbol).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var tracked = await db.TrackedTickers.AsNoTracking().Select(t => t.Symbol).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var candidates = await db.AutoGapperCandidates.AsNoTracking().Select(a => a.Symbol).Distinct().ToListAsync(ct).ConfigureAwait(false);
        return strategies.Concat(tracked).Concat(candidates)
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToUpperInvariant())
            .Where(s => s.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-'))
            .Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();
    }

    /// <summary>Queue every known symbol that is not yet held for the full window.</summary>
    public async Task<IReadOnlyList<string>> DiscoverAsync(CancellationToken ct) =>
        await EnqueueAsync(await KnownSymbolsAsync(ct).ConfigureAwait(false), "new ticker", Years, ct).ConfigureAwait(false);

    /// <summary>Queue the last completed session for every symbol the history holds (the read-through store skips what is already there).</summary>
    public async Task<int> QueueNightlyTopUpAsync(CancellationToken ct)
    {
        var to = TodayEt.AddDays(-1);
        while (!MarketCalendarIsTradingDay(to)) to = to.AddDays(-1);
        await using var c = await history.OpenAsync(ct).ConfigureAwait(false);
        await using var q = new SqlCommand("""
            INSERT dbo.BackfillJobs (Symbol, Reason, FromEt, ToEt, Status, RequestedUtc)
            SELECT s.Symbol, N'nightly top-up', @d, @d, 'pending', SYSUTCDATETIME()
            FROM (SELECT DISTINCT Symbol FROM dbo.CoverageRanges WHERE Kind = '1Day') s
            WHERE NOT EXISTS (SELECT 1 FROM dbo.BackfillJobs j WHERE j.Symbol = s.Symbol AND j.Status IN ('pending', 'running'))
              AND NOT EXISTS (SELECT 1 FROM dbo.CoverageRanges r WHERE r.Symbol = s.Symbol AND r.Kind = '1Day' AND r.ToEt >= @d)
            """, c);
        q.Parameters.Add("@d", SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
        return await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static bool MarketCalendarIsTradingDay(DateOnly d) => MarketCalendar.IsTradingDay(d);

    /// <summary>Claim the oldest pending job (null when the queue is empty).</summary>
    public async Task<BackfillJob?> ClaimAsync(CancellationToken ct)
    {
        await using var c = await history.OpenAsync(ct).ConfigureAwait(false);
        await using var q = new SqlCommand("""
            WITH next AS (SELECT TOP (1) * FROM dbo.BackfillJobs WITH (UPDLOCK, READPAST, ROWLOCK) WHERE Status = 'pending' ORDER BY RequestedUtc, Id)
            UPDATE next SET Status = 'running', StartedUtc = SYSUTCDATETIME()
            OUTPUT inserted.Id, inserted.Symbol, inserted.Reason, inserted.FromEt, inserted.ToEt, inserted.Status;
            """, c);
        await using var r = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new BackfillJob(r.GetInt64(0), r.GetString(1), r.GetString(2), DateOnly.FromDateTime(r.GetDateTime(3)), DateOnly.FromDateTime(r.GetDateTime(4)), r.GetString(5));
    }

    /// <summary>Run one job: daily bars for the whole window, then minute bars month by month. Bars already held are not fetched.</summary>
    public async Task<BackfillRunResult> RunAsync(BackfillJob job, CancellationToken ct)
    {
        var store = new HistoricalBarStore(new SqlHistoryBarCache(history), feed, time, splits: splits);
        int daily = 0, minute = 0, months = 0;
        string? error = null;
        try
        {
            daily = (await store.GetBarsAsync(job.Symbol, job.FromEt, job.ToEt, BarTimeframe.Day1, ct).ConfigureAwait(false)).Count;
            for (var start = new DateOnly(job.FromEt.Year, job.FromEt.Month, 1); start <= job.ToEt; start = start.AddMonths(1))
            {
                var a = start < job.FromEt ? job.FromEt : start;
                var b = start.AddMonths(1).AddDays(-1);
                if (b > job.ToEt) b = job.ToEt;
                minute += (await store.GetBarsAsync(job.Symbol, a, b, BarTimeframe.Minute1, ct).ConfigureAwait(false)).Count;
                months++;
                if (MonthPause > TimeSpan.Zero && job.FromEt != job.ToEt) await Task.Delay(MonthPause, time, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error = ex.Message;
            log.LogWarning(ex, "History backfill {Symbol} failed.", job.Symbol);
        }
        await FinishAsync(job.Id, error, ct).ConfigureAwait(false);
        return new BackfillRunResult(job, daily, minute, months, error);
    }

    private async Task FinishAsync(long id, string? error, CancellationToken ct)
    {
        await using var c = await history.OpenAsync(ct).ConfigureAwait(false);
        await using var q = new SqlCommand("UPDATE dbo.BackfillJobs SET Status = @st, FinishedUtc = SYSUTCDATETIME(), Error = @e WHERE Id = @id", c);
        q.Parameters.Add("@st", SqlDbType.VarChar, 16).Value = error is null ? Done : Failed;
        q.Parameters.Add("@e", SqlDbType.NVarChar, 2000).Value = error is null ? DBNull.Value : error.Length > 2000 ? error[..2000] : error;
        q.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>A job left running by a crash or a restart goes back to pending (its finished days are held, so it resumes where it stopped).</summary>
    public async Task<int> RequeueStaleAsync(TimeSpan olderThan, CancellationToken ct)
    {
        await using var c = await history.OpenAsync(ct).ConfigureAwait(false);
        await using var q = new SqlCommand("UPDATE dbo.BackfillJobs SET Status = 'pending', StartedUtc = NULL WHERE Status = 'running' AND StartedUtc < DATEADD(second, -@s, SYSUTCDATETIME())", c);
        q.Parameters.Add("@s", SqlDbType.Int).Value = (int)olderThan.TotalSeconds;
        return await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
