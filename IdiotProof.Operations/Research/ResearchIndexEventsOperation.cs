using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Research;

public sealed record ResearchIndexEventsArgs
{
    [Option("since", Description = "First announcement date, yyyy-MM-dd (default: 180 days ago).")]
    public DateOnly? Since { get; init; }

    [Option("ticker", Description = "Only this symbol.")]
    public string? Ticker { get; init; }

    [Option("pending", Description = "Only events whose effective date has not passed yet.")]
    public bool Pending { get; init; }
}

/// <summary>
/// S&amp;P 500 / S&amp;P 100 additions and deletions logged in <c>data/sp-index-events.json</c> and
/// turned into IndexEvent claims by the scan pass: forced passive buying (adds) or selling
/// (removals) around the effective date. Upcoming events first, then the most recent.
/// </summary>
[Operation("research index-events", Summary = "S&P 500 / S&P 100 additions and removals: announced and effective dates, pending or done.", Risk = OperationRisk.Read)]
[Example("idiotproof research index-events")]
[Example("idiotproof research index-events --pending")]
[Example("idiotproof research index-events --ticker DDOG --since 2026-01-01")]
public sealed class ResearchIndexEventsOperation : IOperation<ResearchIndexEventsArgs>
{
    public async Task<OperationResult> ExecuteAsync(ResearchIndexEventsArgs args, OperationContext ctx)
    {
        var since = args.Since ?? Account.AccountApis.EtDate(ctx.UtcNow).AddDays(-180);
        var ticker = string.IsNullOrWhiteSpace(args.Ticker) ? null : args.Ticker.Trim().ToUpperInvariant();

        await using var db = await ResearchSupport.Db(ctx).CreateDbContextAsync(ctx.CancellationToken);
        var q = db.ResearchClaims.AsNoTracking()
            .Where(c => c.ClaimType == IndexEventScanner.ClaimType && c.ArticleDate >= since);
        if (ticker is not null) q = q.Where(c => c.Ticker == ticker);
        if (args.Pending) q = q.Where(c => !c.HasHappenedAlready);
        var claims = await q.OrderByDescending(c => c.ArticleDate).Take(500).ToListAsync(ctx.CancellationToken);

        var rows = claims
            .Select(c => new
            {
                c.Id,
                Short = ResearchSupport.Short(c.Id),
                c.Ticker,
                Action = c.Sentiment == "Bullish" ? "add" : "remove",
                Index = c.ClaimSummary.Contains("S&P 100", StringComparison.Ordinal) ? "S&P 100" : "S&P 500",
                Announced = c.ArticleDate,
                Effective = DateOnly.TryParse(c.ExpectedTimeline, System.Globalization.CultureInfo.InvariantCulture, out var e) ? e : (DateOnly?)null,
                Pending = !c.HasHappenedAlready,
                c.Status,
                OutcomePct = c.OutcomePctChange,
                c.SourceUrl,
            })
            // Pending first (soonest effective date first), then done ones newest first.
            .OrderByDescending(r => r.Pending)
            .ThenBy(r => r.Pending ? r.Effective ?? DateOnly.MaxValue : DateOnly.MaxValue)
            .ThenByDescending(r => r.Announced)
            .ToList();

        var head = $"S&P index events announced since {since:yyyy-MM-dd}" + (ticker is null ? "" : $" for {ticker}") + (args.Pending ? ", pending only" : "");
        var data = new { Since = since, Ticker = ticker, args.Pending, Events = rows };
        if (rows.Count == 0)
            return OperationResult.Ok(data, $"{head}\nNone. Log announcements in data/sp-index-events.json; the next `idiotproof research scan` picks them up.");

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("id").Column("ticker").Column("action").Column("index").Column("announced").Column("effective")
            .Column("state").Column("outcome", Align.Right);
        foreach (var r in rows)
            table.Row(r.Short, r.Ticker, r.Action, r.Index, r.Announced.ToString("yyyy-MM-dd"),
                r.Effective?.ToString("yyyy-MM-dd") ?? "TBA", r.Pending ? "pending" : "effective",
                r.OutcomePct is { } o ? Fmt.PercentPoints(o, 1, signed: true) : "");
        return OperationResult.Ok(data, $"{head}\n{table.Render()}\n{rows.Count(r => r.Pending)} pending, {rows.Count(r => !r.Pending)} effective.");
    }
}
