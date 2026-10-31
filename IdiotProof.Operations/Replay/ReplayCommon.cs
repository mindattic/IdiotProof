using IdiotProof.Blazor.Data;
using IdiotProof.DataFeeds;
using IdiotProof.Monitor;
using IdiotProof.Operations.Market;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Replay;

/// <summary>
/// Shared plumbing for the <c>replay ...</c>, <c>scan</c> and <c>dataset</c> operations: the
/// Hub's market data feed (never the Mock feed — a replay persists into the archive and the ML
/// dataset, so made-up prices would poison both), the feed tier label, run lookup by id or id
/// prefix, and the payoff table every replay prints.
/// </summary>
public static class ReplayKit
{
    /// <summary>The Hub's feed, or a Blocked result when it is the Mock feed.</summary>
    public static IMarketDataFeed RequireRealFeed(OperationContext ctx)
    {
        var feed = ctx.Get<IMarketDataFeed>();
        var active = feed is SwitchableMarketDataFeed sw ? sw.Active ?? feed : feed;
        if (active is MockDataFeed)
            throw OperationException.Blocked("The Hub is on the Mock data feed (no Alpaca keys resolved); a replay of made-up prices would pollute the archive and the dataset.");
        return feed;
    }

    /// <summary>The data tier a run is labelled with: the Alpaca feed's tier (sip/iex), else the feed's name.</summary>
    public static string FeedLabel(IMarketDataFeed feed)
    {
        var active = feed is SwitchableMarketDataFeed sw ? sw.Active ?? feed : feed;
        return active switch
        {
            AlpacaDataFeed a => a.FeedTier,
            _ when string.Equals(active.FeedName, "Alpaca", StringComparison.OrdinalIgnoreCase) => MarketData.Feed,
            _ => active.FeedName,
        };
    }

    /// <summary>The archive folder: <paramref name="outDir"/> when given, else the legacy archive root.</summary>
    public static string ArchiveRoot(string? outDir) => string.IsNullOrWhiteSpace(outDir) ? StrategyReplay.DefaultArchiveRoot : outDir.Trim();

    /// <summary>The ET trading day to replay: the given one, else the last completed trading day.</summary>
    public static DateOnly Day(DateOnly? date, OperationContext ctx) =>
        date ?? IdiotProof.Scripting.MarketTime.PreviousEquityTradingDayEt(ctx.UtcNow);

    /// <summary>Upper-cases and checks a ticker.</summary>
    public static string Symbol(string raw)
    {
        var s = (raw ?? "").Trim().ToUpperInvariant();
        if (s.Length is < 1 or > 10 || !s.All(c => char.IsLetterOrDigit(c) || c == '.'))
            throw OperationException.Usage($"'{raw}' is not a ticker symbol.");
        return s;
    }

    /// <summary>Loads a day, turning "no session yet / no bars" into a usage or failure result.</summary>
    public static async Task<ReplayDay> LoadDayAsync(OperationContext ctx, IMarketDataFeed feed, string symbol, DateOnly day)
    {
        try { return await StrategyReplay.LoadDayAsync(feed, FeedLabel(feed), symbol, day, ctx.UtcNow, ctx.CancellationToken); }
        catch (ReplayException ex) { throw OperationException.Failed(ex.Message); }
    }

    /// <summary>A replay run by full id or an id prefix (at least 6 hex digits), or a usage error.</summary>
    public static async Task<ReplayRun> FindRunAsync(OperationContext ctx, string reference)
    {
        var r = (reference ?? "").Trim().Replace("-", "").ToLowerInvariant();
        if (r.Length < 6 || !r.All(Uri.IsHexDigit))
            throw OperationException.Usage($"'{reference}' is not a replay run id — use the id (or its first 6+ characters) from `idiotproof replay list`.");
        await using var db = await ctx.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ctx.CancellationToken);
        if (Guid.TryParseExact(r, "N", out var id))
            return await db.ReplayRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ctx.CancellationToken)
                ?? throw OperationException.Usage($"No replay run {id}.");
        var all = await db.ReplayRuns.AsNoTracking().Select(x => new { x.Id, x.Symbol, x.Strategy, x.DateEt }).ToListAsync(ctx.CancellationToken);
        var hits = all.Where(x => x.Id.ToString("N").StartsWith(r, StringComparison.Ordinal)).ToList();
        return hits.Count switch
        {
            1 => await db.ReplayRuns.AsNoTracking().FirstAsync(x => x.Id == hits[0].Id, ctx.CancellationToken),
            0 => throw OperationException.Usage($"No replay run starts with '{reference}'. `idiotproof replay list` shows them."),
            _ => throw OperationException.Usage($"'{reference}' matches {hits.Count} runs — add characters:\n" +
                string.Join("\n", hits.Take(10).Select(h => $"  {ShortId(h.Id)}  {h.Symbol,-6} {h.DateEt}  {h.Strategy}"))),
        };
    }

    /// <summary>The first 8 hex digits of a run id (what tables show and commands accept).</summary>
    public static string ShortId(Guid id) => id.ToString("N")[..8];

    /// <summary>The run's one-line verdict: payoffs and total P&amp;L, or the best near-miss.</summary>
    public static string Verdict(ReplayOutcome o) => o.Payoffs.Count == 0
        ? $"no fire — best {o.BestPassed}/{o.ConditionCount} conditions in-session (waiting on {o.BestFail ?? "n/a"})"
        : $"{o.Payoffs.Count} round-trip(s){(o.Repeat ? " (repeat)" : " (one-shot)")}, total {Fmt.PercentPoints((decimal)o.Run.TotalPnl, 2, signed: true)}";

    /// <summary>The payoff table (entry/exit ET, prices, P&amp;L, exit reason).</summary>
    public static string Payoffs(IEnumerable<ReplayPayoff> payoffs, int? width)
    {
        var table = new TextTable { MaxWidth = width }
            .Column("entry ET").Column("entry", Align.Right).Column("exit ET").Column("exit", Align.Right).Column("P&L", Align.Right).Column("why");
        foreach (var p in payoffs)
            table.Row(p.EntryEt, Fmt.Money(p.EntryPx), p.ExitEt, Fmt.Money(p.ExitPx), Fmt.PercentPoints((decimal)p.PnlPct, 2, signed: true), p.Reason);
        return table.Render();
    }

    /// <summary>A run as JSON-friendly data (no DataJson / HTML blobs).</summary>
    public static object RunData(ReplayRun r) => new
    {
        r.Id, Short = ShortId(r.Id), r.Symbol, r.Strategy, r.DateEt, r.Feed, r.Kind, r.Stamp, r.GeneratedUtc,
        r.Fired, r.PayoffCount, r.TotalPnl, r.FirstFireEt, r.EntryPrice,
    };
}
