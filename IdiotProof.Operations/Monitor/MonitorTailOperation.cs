using IdiotProof.Operations.Rendering;
using IdiotProof.Operations.Strategies;
using IdiotProof.Operations.Streaming;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Monitor;

public sealed record MonitorTailArgs
{
    [Option("lines", Description = "Most recent events to show (1-500).", Short = 'n')]
    public int Lines { get; init; } = 40;

    [Option("minutes", Description = "How far back to look for events (1-1440).")]
    public int Minutes { get; init; } = 60;

    [Option("progress", Description = "Also show each active strategy's live condition progress.")]
    public bool Progress { get; init; } = true;

    [Option("follow", Description = "After the snapshot, keep pushing new events and progress changes (a terminal streams until Ctrl+C; MCP/--json get a bounded window).", Short = 'f')]
    public bool Follow { get; init; }

    [Option("seconds", Description = "With --follow: stop after this many seconds, 1-3600 (MCP/--json default 15, at most 300).")]
    public int? Seconds { get; init; }
}

/// <summary>
/// <c>monitor tail</c> (IP-T258): the Monitor's recent gate verdicts and lifecycle events from the
/// audit log, plus each active strategy's live condition progress. <c>--follow</c> then pushes
/// each new event and progress change through the Hub's stream (IP-T324) until Ctrl+C.
/// </summary>
[Operation("monitor tail", Summary = "Recent Monitor activity: gate verdicts (fire, veto, block, entry, exit, reject) and live condition progress; --follow streams new ones.", Risk = OperationRisk.Read,
    Description = "A snapshot of the last --minutes of the Monitor's audit trail (your strategies' events plus its start/stop/pause events), newest last, followed by where each of your active strategies stands. --follow keeps pushing new events and progress changes: live in a terminal until Ctrl+C, a bounded window (--seconds, default 15) for MCP and --json.")]
[Streaming]
[Example("idiotproof monitor tail")]
[Example("idiotproof monitor tail --follow")]
[Example("idiotproof monitor tail --lines 100 --minutes 390")]
[Example("idiotproof monitor tail --no-progress")]
public sealed class MonitorTailOperation : IOperation<MonitorTailArgs>
{
    /// <summary>The audit categories that are Monitor progress or gate verdicts.</summary>
    public static readonly string[] Categories =
    [
        "signal-fire", "signal-vetoed", "signal-blocked", "entry", "exit", "exit-partial", "order-rejected",
        "order-placement-exception", "position-reconciled", "strategy-error", "holding",
        "monitor-start", "monitor-stop", "monitor-hibernate", "monitor-resume", "monitor-paused", "monitor-unpaused", "monitor-tick-once",
    ];

    public async Task<OperationResult> ExecuteAsync(MonitorTailArgs args, OperationContext ctx)
    {
        if (args.Lines is < 1 or > 500) return OperationResult.Usage("--lines must be 1-500.");
        if (args.Minutes is < 1 or > 1440) return OperationResult.Usage("--minutes must be 1-1440.");
        var owner = ctx.RequireOwner();
        var now = ctx.UtcNow;

        var rows = await ctx.Get<AuditLogRepository>().GetByCategoriesAsync(Categories, 1000, now.AddMinutes(-args.Minutes), null, ctx.CancellationToken);
        var lastId = rows.Count == 0 ? 0 : rows.Max(r => r.Id);
        var events = rows
            .Where(r => r.UserId is null || r.UserId == owner)
            .OrderByDescending(r => r.TimestampUtc).ThenByDescending(r => r.Id)
            .Take(args.Lines)
            .OrderBy(r => r.TimestampUtc).ThenBy(r => r.Id)
            .Select(r => new { r.TimestampUtc, r.Category, r.Message })
            .ToList();

        if (args.Seconds is < 1 or > 3600) return OperationResult.Usage("--seconds must be 1-3600.");
        var progress = args.Progress ? await ProgressAsync(ctx, owner, ctx.CancellationToken) : [];

        var text = new System.Text.StringBuilder();
        text.Append($"Monitor activity, last {args.Minutes} min (as of {Fmt.Et(now)}):");
        if (events.Count == 0) text.Append("\n  (no gate verdicts or lifecycle events)");
        foreach (var e in events)
            text.Append($"\n  {Fmt.EtTime(e.TimestampUtc),-11} {e.Category,-17} {e.Message.ReplaceLineEndings(" ")}");

        if (args.Progress)
        {
            text.Append("\n\nLive condition progress:");
            if (progress.Count == 0) text.Append("\n  (no active strategies)");
            else
            {
                var table = new TextTable { MaxWidth = ctx.Width }
                    .Column("id").Column("sym").Column("title", maxWidth: 28).Column("pos", Align.Right).Column("cond", Align.Right)
                    .Column("price", Align.Right).Column("evaluated").Column("waiting on", maxWidth: 40);
                foreach (var p in progress)
                    table.Row(p.Id, p.Symbol, p.Title, p.Position.ToString(), p.Progress ?? "-", p.LastPrice is { } px ? Fmt.Money(px) : "",
                        p.EvaluatedUtc is { } at ? Fmt.EtTime(at) : "never", p.WaitingOn ?? (p.Progress is null ? "" : "(all met)"));
                text.Append('\n').Append(table.Render());
            }
        }
        if (!args.Follow)
        {
            text.Append("\n\nSnapshot - run again, or add --follow, to follow.");
            return OperationResult.Ok(new { AsOfUtc = now, args.Minutes, Events = events, Progress = progress }, text.ToString());
        }

        using var stream = OperationStream.Open(ctx, args.Seconds);
        if (stream.Live)
        {
            foreach (var line in text.ToString().Split('\n')) await stream.EmitAsync(line);
            await stream.EmitAsync("");
            await stream.EmitAsync("-- following the Monitor (Ctrl+C stops) --");
        }
        var seen = progress.ToDictionary(p => p.Id, Signature);
        var since = now.AddMinutes(-1);
        await stream.RunAsync(async ct =>
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(FollowPoll, ct);
                var fresh = (await ctx.Get<AuditLogRepository>().GetByCategoriesAsync(Categories, 500, since, null, ct))
                    .Where(r => r.Id > lastId && (r.UserId is null || r.UserId == owner))
                    .OrderBy(r => r.Id).ToList();
                foreach (var r in fresh)
                {
                    await stream.EmitAsync($"  {Fmt.EtTime(r.TimestampUtc),-11} {r.Category,-17} {r.Message.ReplaceLineEndings(" ")}",
                        new { r.TimestampUtc, r.Category, r.Message });
                    lastId = r.Id;
                }
                if (!args.Progress) continue;
                foreach (var p in await ProgressAsync(ctx, owner, ct))
                {
                    var sig = Signature(p);
                    if (seen.TryGetValue(p.Id, out var before) && before == sig) continue;
                    seen[p.Id] = sig;
                    await stream.EmitAsync($"  {Fmt.EtTime(ctx.UtcNow),-11} {"progress",-17} {p.Symbol} {p.Progress ?? "-"}" +
                        (p.WaitingOn is { } w ? $" waiting on {w}" : p.Progress is null ? "" : " (all met)") +
                        (p.LastPrice is { } px ? $" @ {Fmt.Money(px)}" : ""), p);
                }
            }
        });
        return stream.Finish(text + "\n\nFollowing:", new { AsOfUtc = now, args.Minutes, Events = events, Progress = progress });
    }

    /// <summary>How often --follow polls the audit log and the progress rows.</summary>
    public static readonly TimeSpan FollowPoll = TimeSpan.FromSeconds(1);

    private static string Signature(ProgressLine p) => $"{p.Position}|{p.Progress}|{p.WaitingOn}|{p.LastPrice}";

    private static async Task<List<ProgressLine>> ProgressAsync(OperationContext ctx, Guid owner, CancellationToken ct)
    {
        var active = (await ctx.Get<StrategyRepository>().GetActiveAsync(ct)).Where(s => s.OwnerUserId == owner).ToList();
        var map = await ctx.Get<ConditionProgressRepository>().GetForStrategyIdsAsync(active.Select(s => s.Id), ct);
        return active.OrderBy(s => s.Symbol).ThenBy(s => s.Title).Select(s =>
        {
            map.TryGetValue(s.Id, out var p);
            return new ProgressLine(StrategyRefs.ShortGuid(s.Id), s.Symbol, s.Title, s.PositionQty,
                p is null ? null : $"{p.PassedCount}/{p.TotalCount}", p?.FirstFailingVerb, p?.LastPrice, p?.EvaluatedUtc);
        }).ToList();
    }

    /// <summary>One active strategy's standing as the Monitor last wrote it.</summary>
    public sealed record ProgressLine(string Id, string Symbol, string Title, int Position, string? Progress, string? WaitingOn, decimal? LastPrice, DateTime? EvaluatedUtc);
}
