using IdiotProof.Core.Data;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Replay;

public sealed record ReplayListArgs
{
    [Option("symbol", Description = "Only this ticker.")]
    public string? Symbol { get; init; }

    [Option("since", Description = "Only runs of ET trading days on or after this date (yyyy-MM-dd).")]
    public DateOnly? Since { get; init; }

    [Option("kind", Description = "Only sim (hypothetical replays) or live (the Monitor's real trades).")]
    public string? Kind { get; init; }

    [Option("fired", Description = "Only runs that fired.")]
    public bool Fired { get; init; }

    [Option("limit", Description = "At most this many runs, newest first (default 50).")]
    public int Limit { get; init; } = 50;
}

[Operation("replay list", Summary = "The replay archive, newest first: ticker, day, strategy, sim or live, fired and P&L.", Risk = OperationRisk.Read)]
[Example("idiotproof replay list")]
[Example("idiotproof replay list --symbol NVDA --since 2026-10-01 --fired")]
public sealed class ReplayListOperation : IOperation<ReplayListArgs>
{
    public async Task<OperationResult> ExecuteAsync(ReplayListArgs args, OperationContext ctx)
    {
        if (args.Limit is < 1 or > 1000) return OperationResult.Usage("--limit must be 1-1000.");
        var kind = args.Kind?.Trim().ToLowerInvariant();
        if (kind is not (null or "sim" or "live")) return OperationResult.Usage("--kind must be sim or live.");

        await using var db = await ctx.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ctx.CancellationToken);
        var q = db.ReplayRuns.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(args.Symbol)) { var sym = ReplayKit.Symbol(args.Symbol); q = q.Where(r => r.Symbol == sym); }
        if (kind is not null) q = q.Where(r => r.Kind == kind);
        if (args.Fired) q = q.Where(r => r.Fired);
        if (args.Since is { } since) { var s = since.ToString("yyyy-MM-dd"); q = q.Where(r => string.Compare(r.DateEt, s) >= 0); }
        var total = await q.CountAsync(ctx.CancellationToken);
        var rows = await q.OrderByDescending(r => r.GeneratedUtc).Take(args.Limit)
            .Select(r => new ReplayRun
            {
                Id = r.Id, Symbol = r.Symbol, Strategy = r.Strategy, DateEt = r.DateEt, Feed = r.Feed, Kind = r.Kind, Stamp = r.Stamp,
                GeneratedUtc = r.GeneratedUtc, Fired = r.Fired, PayoffCount = r.PayoffCount, TotalPnl = r.TotalPnl, FirstFireEt = r.FirstFireEt, EntryPrice = r.EntryPrice,
            })
            .ToListAsync(ctx.CancellationToken);

        var data = rows.Select(ReplayKit.RunData).ToList();
        if (rows.Count == 0) return OperationResult.Ok(data, "No replay runs match. `idiotproof replay run <symbol> <date>` makes one.");
        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("run").Column("day").Column("sym").Column("kind").Column("strategy", maxWidth: 38).Column("fired").Column("trips", Align.Right)
            .Column("P&L", Align.Right).Column("generated");
        foreach (var r in rows)
            table.Row(ReplayKit.ShortId(r.Id), r.DateEt, r.Symbol, r.Kind.ToUpperInvariant(), r.Strategy, r.Fired ? $"● {r.FirstFireEt}" : "·",
                r.PayoffCount.ToString(), Fmt.PercentPoints((decimal)r.TotalPnl, 2, signed: true), Fmt.Et(r.GeneratedUtc));
        return OperationResult.Ok(data, table.Render() + $"\n{rows.Count} of {total} run(s). `idiotproof replay show <run>` for trades and bars.");
    }
}
