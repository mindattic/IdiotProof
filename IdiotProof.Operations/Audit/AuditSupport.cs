using IdiotProof.Core.Data;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Audit;

/// <summary>The filters the audit operations share.</summary>
public record AuditFilterArgs
{
    [Option("since", Description = "First ET date to include, yyyy-MM-dd (default: everything kept).")]
    public DateOnly? Since { get; init; }

    [Option("kind", Description = "Only these categories, comma-separated: signal-fire, entry, exit, order-rejected, risk, broker, system, ...")]
    public string[]? Kind { get; init; }

    [Option("system", Description = "Include system-wide rows that belong to no user (default on; --no-system for only yours).")]
    public bool System { get; init; } = true;

    [Option("limit", Description = "Most rows, newest first, 1-1000 (default 50).")]
    public int Limit { get; init; } = 50;
}

/// <summary>One audit row as the audit operations return it.</summary>
public sealed record AuditRow(long Id, DateTime TimestampUtc, string TimeEt, string Kind, string Message, bool System, string? DataJson);

/// <summary>Reading and rendering the audit trail (the Monitor's and the app's append-only event log).</summary>
public static class AuditSupport
{
    /// <summary>A fresh SQL context for an audit or ledger read.</summary>
    public static Task<AppDbContext> OpenAsync(OperationContext ctx) =>
        ctx.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ctx.CancellationToken);

    /// <summary>The owner's rows (plus system rows unless excluded) matching <paramref name="f"/>, as a query.</summary>
    public static IQueryable<AuditLog> Query(AppDbContext db, Guid owner, AuditFilterArgs f)
    {
        var q = db.AuditLogs.AsNoTracking()
            .Where(a => a.UserId == owner || (f.System && a.UserId == null));
        if (f.Since is { } since)
        {
            var from = Account.AccountApis.EtMidnightUtc(since);
            q = q.Where(a => a.TimestampUtc >= from);
        }
        var kinds = Kinds(f);
        if (kinds.Count > 0) q = q.Where(a => kinds.Contains(a.Category));
        return q;
    }

    public static List<string> Kinds(AuditFilterArgs f) =>
        (f.Kind ?? []).Select(k => k.Trim().ToLowerInvariant()).Where(k => k.Length > 0).Distinct().ToList();

    public static void Validate(AuditFilterArgs f)
    {
        if (f.Limit is < 1 or > 1000) throw OperationException.Usage("--limit must be 1-1000.");
    }

    public static AuditRow ToRow(AuditLog a) =>
        new(a.Id, a.TimestampUtc, Fmt.Et(a.TimestampUtc), a.Category, a.Message, a.UserId is null, a.DataJson);

    public static string Table(IEnumerable<AuditRow> rows, int? width)
    {
        var table = new TextTable { MaxWidth = width }
            .Column("id", Align.Right).Column("when (ET)").Column("kind").Column("message", maxWidth: 110);
        foreach (var r in rows) table.Row(r.Id.ToString(), r.TimeEt, r.Kind + (r.System ? "*" : ""), r.Message);
        return table.Render();
    }

    /// <summary>Footnote for the "*" marker on system rows, when any are shown.</summary>
    public static string Legend(IEnumerable<AuditRow> rows) => rows.Any(r => r.System) ? " (* = system-wide)" : "";
}
