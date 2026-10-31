using IdiotProof.Core.Data;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Research;

public sealed record ResearchClaimsArgs
{
    [Option("ticker", Description = "Only claims about this symbol (and macro claims that name it).")]
    public string? Ticker { get; init; }

    [Option("since", Description = "First ET date a claim was found, yyyy-MM-dd (default: 14 days ago).")]
    public DateOnly? Since { get; init; }

    [Option("min-score", Description = "Only claims scored at least this (0-100); unscored claims are left out.")]
    public double? MinScore { get; init; }

    [Option("macro", Description = "Only macro/regulatory claims (no single ticker).")]
    public bool Macro { get; init; }

    [Option("portents", Description = "Only pending portents: announced, not yet happened.")]
    public bool Portents { get; init; }

    [Option("limit", Description = "Most claims to show, 1-500 (default 50).")]
    public int Limit { get; init; } = 50;
}

/// <summary>
/// The ranked research feed: claims ordered by significance score (unscored last, newest
/// first), the same order as the web Research tab, with ticker, macro, portent and score filters.
/// </summary>
[Operation("research claims", Summary = "Ranked research feed: catalysts and portents by significance score, filterable by ticker, date, score and macro.", Risk = OperationRisk.Read)]
[Example("idiotproof research claims")]
[Example("idiotproof research claims --ticker NVDA --since 2026-10-01")]
[Example("idiotproof research claims --min-score 60 --portents")]
[Example("idiotproof research claims --macro")]
public sealed class ResearchClaimsOperation : IOperation<ResearchClaimsArgs>
{
    public async Task<OperationResult> ExecuteAsync(ResearchClaimsArgs args, OperationContext ctx)
    {
        if (args.Limit is < 1 or > 500) return OperationResult.Usage("--limit must be 1-500.");
        if (args.MinScore is < 0 or > 100) return OperationResult.Usage("--min-score must be 0-100.");

        var today = Account.AccountApis.EtDate(ctx.UtcNow);
        var since = args.Since ?? today.AddDays(-14);
        var sinceUtc = ResearchSupport.SinceUtc(since)!.Value;
        var ticker = string.IsNullOrWhiteSpace(args.Ticker) ? null : args.Ticker.Trim().ToUpperInvariant();

        await using var db = await ResearchSupport.Db(ctx).CreateDbContextAsync(ctx.CancellationToken);
        var q = db.ResearchClaims.AsNoTracking().Where(c => c.CreatedUtc >= sinceUtc);
        if (args.Macro) q = q.Where(c => c.IsMacro);
        if (args.Portents) q = q.Where(c => !c.HasHappenedAlready && c.Status == "Pending");
        if (args.MinScore is { } min) q = q.Where(c => c.SignificanceScore != null && c.SignificanceScore >= min);
        // Macro claims carry their tickers as JSON, so a ticker filter keeps them for a client-side check.
        if (ticker is not null) q = q.Where(c => c.Ticker == ticker || c.IsMacro);

        var fetched = await q
            .OrderByDescending(c => c.SignificanceScore ?? -1)
            .ThenByDescending(c => c.CreatedUtc)
            .Take(ticker is null ? args.Limit : args.Limit * 5)
            .ToListAsync(ctx.CancellationToken);

        var rows = fetched
            .Where(c => ticker is null || c.Ticker == ticker
                || ResearchSupport.AffectedTickers(c.AffectedTickersJson).Contains(ticker, StringComparer.OrdinalIgnoreCase))
            .Take(args.Limit)
            .Select(ResearchSupport.ToRow)
            .ToList();

        var lastScan = await db.ScanRuns.AsNoTracking()
            .Where(s => s.CompletedUtc != null)
            .OrderByDescending(s => s.StartedUtc)
            .FirstOrDefaultAsync(ctx.CancellationToken);

        var filters = new List<string> { $"since {since:yyyy-MM-dd}" };
        if (ticker is not null) filters.Add(ticker);
        if (args.Macro) filters.Add("macro");
        if (args.Portents) filters.Add("portents");
        if (args.MinScore is { } m) filters.Add($"score >= {ResearchSupport.N(m, "0.#")}");
        var head = $"Research claims ({string.Join(", ", filters)})";
        var scanLine = lastScan is null
            ? "No scan has run yet — `idiotproof research scan` runs one."
            : $"Last scan {Fmt.Et(lastScan.CompletedUtc)}: {lastScan.TickersScanned}/{lastScan.UniverseSize} tickers, {lastScan.ClaimsFound} claims.";

        var data = new { Since = since, Ticker = ticker, args.Macro, args.Portents, args.MinScore, Claims = rows, LastScanUtc = lastScan?.CompletedUtc };
        if (rows.Count == 0) return OperationResult.Ok(data, $"{head}\nNo claims match.\n{scanLine}");
        return OperationResult.Ok(data,
            $"{head}\n{ResearchSupport.ClaimsTable(rows, ctx.Width)}\n{rows.Count} claims. `idiotproof research claim <id>` shows one in full.\n{scanLine}");
    }
}
