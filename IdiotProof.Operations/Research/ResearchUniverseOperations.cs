using IdiotProof.Core.Data;
using IdiotProof.Engine.Settings;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Research;

public sealed record ResearchUniverseListArgs
{
    [Option("exchange", Description = "Only NASDAQ or NYSE.")]
    public string? Exchange { get; init; }

    [Option("prefix", Description = "Only symbols starting with this text.")]
    public string? Prefix { get; init; }

    [Option("min-price", Description = "Only tickers whose last price is at least this.")]
    public decimal? MinPrice { get; init; }

    [Option("max-price", Description = "Only tickers whose last price is at most this.")]
    public decimal? MaxPrice { get; init; }

    [Option("limit", Description = "Most rows to show, 1-10000 (default 100).")]
    public int Limit { get; init; } = 100;
}

/// <summary>
/// The tracked ticker universe the research scan rotates through: every tradable NASDAQ/NYSE
/// equity from Alpaca's asset list with its last price, cached in SQL and refreshed daily.
/// </summary>
[Operation("research universe list", Summary = "The tracked ticker universe the research scan sweeps: symbol, exchange, last price, freshness.", Risk = OperationRisk.Read)]
[Example("idiotproof research universe list")]
[Example("idiotproof research universe list --exchange NASDAQ --max-price 5 --limit 50")]
[Example("idiotproof research universe list --prefix NV")]
public sealed class ResearchUniverseListOperation : IOperation<ResearchUniverseListArgs>
{
    public async Task<OperationResult> ExecuteAsync(ResearchUniverseListArgs args, OperationContext ctx)
    {
        if (args.Limit is < 1 or > 10000) return OperationResult.Usage("--limit must be 1-10000.");
        var exchange = string.IsNullOrWhiteSpace(args.Exchange) ? null : args.Exchange.Trim().ToUpperInvariant();
        if (exchange is not (null or "NASDAQ" or "NYSE")) return OperationResult.Usage("--exchange is NASDAQ or NYSE.");
        var prefix = string.IsNullOrWhiteSpace(args.Prefix) ? null : args.Prefix.Trim().ToUpperInvariant();

        await using var db = await ResearchSupport.Db(ctx).CreateDbContextAsync(ctx.CancellationToken);
        var total = await db.TrackedTickers.CountAsync(ctx.CancellationToken);
        var newest = await db.TrackedTickers.OrderByDescending(t => t.LastRefreshedUtc)
            .Select(t => (DateTime?)t.LastRefreshedUtc).FirstOrDefaultAsync(ctx.CancellationToken);

        var q = db.TrackedTickers.AsNoTracking().AsQueryable();
        if (exchange is not null) q = q.Where(t => t.Exchange == exchange);
        if (prefix is not null) q = q.Where(t => t.Symbol.StartsWith(prefix));
        if (args.MinPrice is { } min) q = q.Where(t => t.LastPrice != null && t.LastPrice >= min);
        if (args.MaxPrice is { } max) q = q.Where(t => t.LastPrice != null && t.LastPrice <= max);
        var matching = await q.CountAsync(ctx.CancellationToken);
        var rows = await q.OrderBy(t => t.Symbol).Take(args.Limit).ToListAsync(ctx.CancellationToken);

        var items = rows.Select(t => new
        {
            t.Symbol, t.Exchange, t.IsTradable, t.LastPrice, t.SharesOutstanding,
            MarketCap = t.LastPrice * t.SharesOutstanding, t.LastRefreshedUtc,
        }).ToList();
        var data = new { Total = total, Matching = matching, RefreshedUtc = newest, Tickers = items };
        var freshness = newest is null
            ? "The universe is empty — `idiotproof research universe refresh` loads it."
            : $"{Fmt.Number(total)} tracked, refreshed {Fmt.Et(newest)}.";
        if (items.Count == 0) return OperationResult.Ok(data, $"No tickers match.\n{freshness}");

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("symbol").Column("exchange").Column("last", Align.Right).Column("shares out", Align.Right)
            .Column("mkt cap", Align.Right).Column("refreshed");
        foreach (var t in items)
            table.Row(t.Symbol, t.Exchange, t.LastPrice is { } p ? Fmt.Money(p) : "",
                t.SharesOutstanding is { } s ? Fmt.Number(s) : "", t.MarketCap is { } m ? Fmt.Money(m, 0) : "",
                Fmt.EtDate(t.LastRefreshedUtc));
        var shown = matching > items.Count ? $"Showing {items.Count} of {Fmt.Number(matching)} matching." : $"{items.Count} matching.";
        return OperationResult.Ok(data, $"{table.Render()}\n{shown} {freshness}");
    }
}

public sealed record ResearchUniverseRefreshArgs
{
    [Option("force", Description = "Reload even when the cache is fresher than --max-age-hours.")]
    public bool Force { get; init; }

    [Option("max-age-hours", Description = "Only reload when the newest row is older than this, 1-720 (default 24).")]
    public int MaxAgeHours { get; init; } = 24;
}

/// <summary>
/// Reloads the tracked universe from Alpaca (active tradable NASDAQ/NYSE equities, then latest
/// prices in batches) when it is stale, or always with <c>--force</c>. Read-only on Alpaca's
/// side; writes the TrackedTickers cache. Uses the host's Alpaca data keys, like the scanner.
/// </summary>
[Operation("research universe refresh", Summary = "Reload the tracked ticker universe (asset list and last prices) from Alpaca when stale, or --force.", Risk = OperationRisk.Write)]
[Example("idiotproof research universe refresh")]
[Example("idiotproof research universe refresh --force")]
public sealed class ResearchUniverseRefreshOperation : IOperation<ResearchUniverseRefreshArgs>
{
    public async Task<OperationResult> ExecuteAsync(ResearchUniverseRefreshArgs args, OperationContext ctx)
    {
        if (args.MaxAgeHours is < 1 or > 720) return OperationResult.Usage("--max-age-hours must be 1-720.");
        if (string.IsNullOrWhiteSpace(ctx.Get<AppSettings>().AlpacaApiKeyId))
            return OperationResult.Blocked("The host has no Alpaca key for the research universe (the scanner's AlpacaApiKeyId setting).");

        var dbFactory = ResearchSupport.Db(ctx);
        var (beforeCount, beforeNewest) = await SnapshotAsync(dbFactory, ctx.CancellationToken);
        await ctx.Get<TickerUniverseService>().RefreshIfStaleAsync(
            args.Force ? TimeSpan.Zero : TimeSpan.FromHours(args.MaxAgeHours), ctx.CancellationToken);
        var (afterCount, afterNewest) = await SnapshotAsync(dbFactory, ctx.CancellationToken);

        var refreshed = afterNewest is not null && afterNewest != beforeNewest;
        await using var db = await dbFactory.CreateDbContextAsync(ctx.CancellationToken);
        var priced = await db.TrackedTickers.CountAsync(t => t.LastPrice != null, ctx.CancellationToken);
        var data = new
        {
            Refreshed = refreshed,
            Before = beforeCount,
            After = afterCount,
            Added = Math.Max(0, afterCount - beforeCount),
            Priced = priced,
            RefreshedUtc = afterNewest,
        };

        string human;
        if (refreshed)
            human = $"Universe reloaded: {Fmt.Number(afterCount)} tickers ({Fmt.Number(data.Added)} new), {Fmt.Number(priced)} with a last price, at {Fmt.Et(afterNewest)}.";
        else if (beforeNewest is not null && !args.Force)
            human = $"Universe is fresh ({Fmt.Number(afterCount)} tickers, refreshed {Fmt.Et(beforeNewest)}); nothing to do. Use --force to reload anyway.";
        else
            return OperationResult.Fail("Alpaca returned no assets — nothing was reloaded (see the Hub log).", data);
        return OperationResult.Ok(data, human);
    }

    private static async Task<(int Count, DateTime? Newest)> SnapshotAsync(
        IDbContextFactory<AppDbContext> dbFactory, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var count = await db.TrackedTickers.CountAsync(ct);
        var newest = await db.TrackedTickers.OrderByDescending(t => t.LastRefreshedUtc)
            .Select(t => (DateTime?)t.LastRefreshedUtc).FirstOrDefaultAsync(ct);
        return (count, newest);
    }
}
