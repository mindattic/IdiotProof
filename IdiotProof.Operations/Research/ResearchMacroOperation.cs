using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Research;

public sealed record ResearchMacroArgs
{
    [Option("since", Description = "First ET date a claim was found, yyyy-MM-dd (default: 30 days ago).")]
    public DateOnly? Since { get; init; }

    [Option("affects", Description = "Only claims whose affected-ticker list names this symbol.")]
    public string? Affects { get; init; }

    [Option("limit", Description = "Most claims to show, 1-500 (default 50).")]
    public int Limit { get; init; } = 50;
}

/// <summary>
/// Macro and regulatory claims: events that are not about one company (an exchange listing-rule
/// change, a Federal Register rule) with the tickers each plausibly affects, newest first.
/// </summary>
[Operation("research macro", Summary = "Macro and regulatory claims (rule changes, listing standards) with the tickers each affects.", Risk = OperationRisk.Read)]
[Example("idiotproof research macro")]
[Example("idiotproof research macro --since 2026-09-01 --affects SOUN")]
public sealed class ResearchMacroOperation : IOperation<ResearchMacroArgs>
{
    public async Task<OperationResult> ExecuteAsync(ResearchMacroArgs args, OperationContext ctx)
    {
        if (args.Limit is < 1 or > 500) return OperationResult.Usage("--limit must be 1-500.");
        var since = args.Since ?? Account.AccountApis.EtDate(ctx.UtcNow).AddDays(-30);
        var sinceUtc = ResearchSupport.SinceUtc(since)!.Value;
        var affects = string.IsNullOrWhiteSpace(args.Affects) ? null : args.Affects.Trim().ToUpperInvariant();

        await using var db = await ResearchSupport.Db(ctx).CreateDbContextAsync(ctx.CancellationToken);
        var claims = await db.ResearchClaims.AsNoTracking()
            .Where(c => c.IsMacro && c.CreatedUtc >= sinceUtc)
            .OrderByDescending(c => c.CreatedUtc)
            .Take(affects is null ? args.Limit : 2000)
            .ToListAsync(ctx.CancellationToken);

        var rows = claims
            .Where(c => affects is null || ResearchSupport.AffectedTickers(c.AffectedTickersJson).Contains(affects, StringComparer.OrdinalIgnoreCase))
            .Take(args.Limit)
            .Select(c => new
            {
                Claim = ResearchSupport.ToRow(c),
                AffectedDescription = ResearchSupport.AffectedTickers(c.AffectedTickersJson).Count == 0 ? c.AffectedTickersJson : null,
                c.PendingTrigger,
                c.ExpectedTimeline,
                c.SourceUrl,
            })
            .ToList();

        var head = $"Macro/regulatory claims since {since:yyyy-MM-dd}" + (affects is null ? "" : $" naming {affects}");
        var data = new { Since = since, Affects = affects, Claims = rows };
        if (rows.Count == 0) return OperationResult.Ok(data, $"{head}\nNone found.");

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("id").Column("date").Column("score", Align.Right).Column("sent").Column("status")
            .Column("summary", maxWidth: 60).Column("affects", maxWidth: 40);
        foreach (var r in rows)
            table.Row(r.Claim.Short, r.Claim.ArticleDate.ToString("yyyy-MM-dd"), ResearchSupport.Score(r.Claim.Score),
                r.Claim.Sentiment, r.Claim.Status, r.Claim.Summary,
                r.Claim.AffectedTickers.Count > 0 ? string.Join(",", r.Claim.AffectedTickers) : r.AffectedDescription ?? "—");
        return OperationResult.Ok(data, $"{head}\n{table.Render()}\n{rows.Count} claims.");
    }
}
