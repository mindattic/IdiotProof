using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Audit;

public sealed record AuditTailArgs
{
    [Option("lines", Description = "How many of the latest rows to start with, 0-500 (default 20).", Short = 'n')]
    public int Lines { get; init; } = 20;

    [Option("follow", Description = "Keep watching for new rows until --seconds pass or --max new rows arrive.", Short = 'f')]
    public bool Follow { get; init; }

    [Option("seconds", Description = "With --follow: how long to watch, 1-300 (default 30).")]
    public int Seconds { get; init; } = 30;

    [Option("max", Description = "With --follow: stop after this many new rows, 1-1000 (default 200).")]
    public int Max { get; init; } = 200;

    [Option("kind", Description = "Only these categories, comma-separated.")]
    public string[]? Kind { get; init; }

    [Option("system", Description = "Include system-wide rows (default on; --no-system for only yours).")]
    public bool System { get; init; } = true;
}

/// <summary>
/// The newest audit rows, oldest first like <c>tail</c>; with <c>--follow</c>, keeps polling for
/// new ones for a bounded time (an operation returns one result, so following ends after
/// <c>--seconds</c> or <c>--max</c> new rows and returns everything it saw).
/// </summary>
[Operation("audit tail", Summary = "The latest audit rows, oldest first; --follow watches for new ones for a bounded time (--seconds, --max).", Risk = OperationRisk.Read)]
[Example("idiotproof audit tail")]
[Example("idiotproof audit tail --follow --seconds 60 --kind signal-fire,entry,exit")]
public sealed class AuditTailOperation : IOperation<AuditTailArgs>
{
    /// <summary>How often --follow polls the table.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    public async Task<OperationResult> ExecuteAsync(AuditTailArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        if (args.Lines is < 0 or > 500) return OperationResult.Usage("--lines must be 0-500.");
        if (args.Seconds is < 1 or > 300) return OperationResult.Usage("--seconds must be 1-300.");
        if (args.Max is < 1 or > 1000) return OperationResult.Usage("--max must be 1-1000.");
        var filter = new AuditFilterArgs { Kind = args.Kind, System = args.System };

        List<AuditRow> latest;
        long lastId;
        await using (var db = await AuditSupport.OpenAsync(ctx))
        {
            latest = (await AuditSupport.Query(db, owner, filter)
                    .OrderByDescending(a => a.Id).Take(args.Lines).ToListAsync(ctx.CancellationToken))
                .OrderBy(a => a.Id).Select(AuditSupport.ToRow).ToList();
            // Follow from the newest row in the table, not just the newest shown (--lines 0 shows none).
            lastId = await db.AuditLogs.MaxAsync(a => (long?)a.Id, ctx.CancellationToken) ?? 0;
        }

        var fresh = new List<AuditRow>();
        var stoppedBy = "";
        if (args.Follow)
        {
            var deadline = ctx.Clock.GetUtcNow() + TimeSpan.FromSeconds(args.Seconds);
            stoppedBy = "time";
            while (ctx.Clock.GetUtcNow() < deadline)
            {
                var wait = deadline - ctx.Clock.GetUtcNow();
                await Task.Delay(wait < PollInterval ? wait : PollInterval, ctx.Clock, ctx.CancellationToken);
                await using var db = await AuditSupport.OpenAsync(ctx);
                // Cap the window at the table's current top id first, so rows the filter hides still
                // advance the cursor and a row landing mid-poll is picked up next time, not skipped.
                var top = await db.AuditLogs.MaxAsync(a => (long?)a.Id, ctx.CancellationToken) ?? lastId;
                if (top <= lastId) continue;
                var room = args.Max - fresh.Count;
                var from = lastId;
                var rows = await AuditSupport.Query(db, owner, filter)
                    .Where(a => a.Id > from && a.Id <= top).OrderBy(a => a.Id).Take(room).ToListAsync(ctx.CancellationToken);
                fresh.AddRange(rows.Select(AuditSupport.ToRow));
                lastId = rows.Count == room ? rows[^1].Id : top;
                if (fresh.Count >= args.Max) { stoppedBy = "max"; break; }
            }
        }

        var all = latest.Concat(fresh).ToList();
        var data = new { Rows = latest, New = fresh, Followed = args.Follow, StoppedBy = args.Follow ? stoppedBy : null };
        var parts = new List<string>();
        parts.Add(all.Count == 0 ? "No audit rows." : AuditSupport.Table(all, ctx.Width));
        if (args.Follow)
            parts.Add(fresh.Count == 0
                ? $"No new rows in {args.Seconds}s."
                : $"{fresh.Count} new row(s) " + (stoppedBy == "max" ? $"(stopped at --max {args.Max})" : $"in {args.Seconds}s") + AuditSupport.Legend(all) + ".");
        else if (all.Count > 0)
            parts.Add($"{all.Count} rows{AuditSupport.Legend(all)}. --follow watches for new ones.");
        return OperationResult.Ok(data, string.Join("\n", parts));
    }
}
