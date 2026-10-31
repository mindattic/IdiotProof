using IdiotProof.Core.Data;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Research;

public sealed record ResearchClaimRefArgs
{
    [Argument(0, Name = "claim", Description = "Claim id, or the 8-character prefix `research claims` shows.")]
    public string Claim { get; init; } = "";
}

/// <summary>
/// One claim in full: what was claimed, where it came from (source, tier, URL, snippet), the
/// portent trigger and timeline, the realized outcome once backfilled, any Form 4 transaction
/// lines behind it, and its significance score broken into components.
/// </summary>
[Operation("research claim", Summary = "One research claim in full: source, trigger, outcome, insider lines and its score breakdown.", Risk = OperationRisk.Read)]
[Example("idiotproof research claim 3f2a91c0")]
public sealed class ResearchClaimOperation : IOperation<ResearchClaimRefArgs>
{
    public async Task<OperationResult> ExecuteAsync(ResearchClaimRefArgs args, OperationContext ctx)
    {
        var claim = await ResearchSupport.ResolveClaimAsync(ctx, args.Claim);
        var dbFactory = ResearchSupport.Db(ctx);
        var watchlist = await ScanPassRunner.GetWatchlistSymbolsAsync(dbFactory, ctx.CancellationToken);
        var breakdown = await ctx.Get<SignificanceScorer>().ExplainAsync(claim.Id, watchlist, ctx.CancellationToken);

        await using var db = await dbFactory.CreateDbContextAsync(ctx.CancellationToken);
        var insiders = await db.InsiderTransactions.AsNoTracking()
            .Where(t => t.ClaimId == claim.Id)
            .OrderBy(t => t.TransactionDate)
            .ToListAsync(ctx.CancellationToken);
        var trust = await db.SourceTrustScores.AsNoTracking()
            .FirstOrDefaultAsync(s => s.SourceName == claim.SourceName, ctx.CancellationToken);
        var hasVector = await db.ResearchClaimVectors.AnyAsync(v => v.ClaimId == claim.Id, ctx.CancellationToken);

        var affected = ResearchSupport.AffectedTickers(claim.AffectedTickersJson);
        var data = new
        {
            Claim = ResearchSupport.ToRow(claim),
            claim.SourceUrl,
            claim.PendingTrigger,
            claim.ExpectedTimeline,
            claim.TriggerConfidence,
            Analysis = claim.LlmAnswer,
            Snippet = claim.RawArticleSnippet,
            claim.PriceAtClaim,
            claim.PriceAtOutcome,
            claim.OutcomeDate,
            AffectedTickersText = claim.IsMacro && affected.Count == 0 ? claim.AffectedTickersJson : null,
            SourceTrust = trust is null ? null : new { trust.TotalClaims, trust.PortentsClaimed, trust.PortentsRealized, trust.ImmediateClaims, trust.ImmediateCorrect, trust.ConfidencePct },
            ScoreBreakdown = breakdown,
            HasVector = hasVector,
            InsiderTransactions = insiders.Select(InsiderRow.From).ToList(),
        };

        var kv = new KeyValueBlock()
            .Add("id", claim.Id.ToString())
            .Add("ticker", claim.IsMacro
                ? "(macro) " + (affected.Count > 0 ? string.Join(", ", affected) : claim.AffectedTickersJson ?? "affected set not enumerated")
                : claim.Ticker)
            .Add("claim", claim.ClaimSummary)
            .Add("type", $"{claim.ClaimType} · {claim.Sentiment} · {claim.Magnitude} magnitude")
            .Add("kind", claim.HasHappenedAlready ? "event (already happened)" : "portent (announced, not yet happened)")
            .Add("trigger", claim.PendingTrigger)
            .Add("timeline", claim.ExpectedTimeline)
            .Add("confidence", claim.TriggerConfidence)
            .Add("status", claim.Status)
            .Add("dated", $"{claim.ArticleDate:yyyy-MM-dd} (found {Fmt.Et(claim.CreatedUtc)})")
            .Add("source", $"{claim.SourceName} — {TierLabel(claim.SourceTier)}")
            .Add("url", claim.SourceUrl)
            .Add("source record", trust?.ConfidencePct is { } pct
                ? $"{Fmt.PercentPoints((decimal)pct, 0)} right over {trust.TotalClaims} claims"
                : trust is null ? null : $"{trust.TotalClaims} claims, no outcomes yet")
            .Add("outcome", Outcome(claim))
            .Add("score", Score(claim, breakdown))
            .Add("analysis", claim.LlmAnswer)
            .Add("snippet", claim.RawArticleSnippet is { } s ? TextTable.Truncate(s.ReplaceLineEndings(" "), 300) : null);

        var blocks = new List<string> { kv.Render() };
        if (insiders.Count > 0) blocks.Add("Form 4 lines:\n" + InsiderRow.Table(insiders.Select(InsiderRow.From), ctx.Width));
        blocks.Add(hasVector
            ? $"`idiotproof research similar {ResearchSupport.Short(claim.Id)}` lists look-alike claims and what happened after them."
            : "No similarity vector yet (it is computed in the background after extraction).");
        return OperationResult.Ok(data, string.Join("\n\n", blocks));
    }

    internal static string TierLabel(int tier) => tier switch
    {
        1 => "tier 1 (primary: SEC / government)",
        2 => "tier 2 (editorial press)",
        _ => "tier 3 (promotional / unknown)",
    };

    private static string? Outcome(ResearchClaim c) =>
        c.OutcomePctChange is { } pct
            ? $"{Fmt.PercentPoints(pct, 2, signed: true)} ({Fmt.Money(c.PriceAtClaim ?? 0)} -> {Fmt.Money(c.PriceAtOutcome ?? 0)} by {c.OutcomeDate:yyyy-MM-dd})"
            : c.IsMacro ? "n/a (macro claims have no single price)" : "not backfilled yet";

    private static string Score(ResearchClaim c, SignificanceBreakdown? b)
    {
        var stored = c.SignificanceScore is { } s ? ResearchSupport.Score(s) : "unscored";
        if (b is null) return stored;
        return $"{stored} stored; now {ResearchSupport.N(b.Score)} = ({ResearchSupport.N(b.MagnitudeScore, "0")} magnitude × {ResearchSupport.N(b.ConfidenceMultiplier, "0.##")} confidence"
            + $" + {ResearchSupport.N(b.HistoryBonus)} history + {ResearchSupport.N(b.SourceBonus)} source + {ResearchSupport.N(b.WatchlistBonus)} watchlist) × {ResearchSupport.N(b.RecencyMultiplier, "0.##")} recency";
    }
}

/// <summary>One Form 4 transaction line, for <c>research claim</c> and <c>research insiders</c>.</summary>
public sealed record InsiderRow(
    Guid ClaimId, string Filer, string Role, string Code, string Action, DateOnly Date, decimal Shares,
    decimal? Price, decimal? Value, decimal SharesOwnedAfter, decimal? PctOfHoldingsChanged, string? FilingUrl)
{
    public static InsiderRow From(InsiderTransaction t) => new(
        t.ClaimId, t.FilerName, t.FilerRole, t.TransactionCode, CodeLabel(t.TransactionCode), t.TransactionDate,
        t.SharesTransacted, t.PricePerShare, t.DollarValue, t.SharesOwnedAfter, t.PctOfHoldingsChanged, t.FilingUrl);

    /// <summary>The SEC transaction code in words.</summary>
    public static string CodeLabel(string code) => code.ToUpperInvariant() switch
    {
        "P" => "purchase",
        "S" => "sale",
        "A" => "grant/award",
        "M" => "option exercise",
        "F" => "tax withholding",
        "G" => "gift",
        "D" => "disposition to issuer",
        "C" => "conversion",
        "X" => "option exercise (in the money)",
        _ => code,
    };

    public static string Table(IEnumerable<InsiderRow> rows, int? width)
    {
        var table = new TextTable { MaxWidth = width }
            .Column("date").Column("filer", maxWidth: 28).Column("role").Column("action")
            .Column("shares", Align.Right).Column("price", Align.Right).Column("value", Align.Right)
            .Column("owned after", Align.Right).Column("chg", Align.Right);
        foreach (var r in rows)
            table.Row(r.Date.ToString("yyyy-MM-dd"), r.Filer, r.Role, r.Action, Fmt.Number(r.Shares, 0),
                r.Price is { } p ? Fmt.Money(p) : "", r.Value is { } v ? Fmt.Money(v, 0) : "",
                Fmt.Number(r.SharesOwnedAfter, 0), r.PctOfHoldingsChanged is { } c ? Fmt.PercentPoints(c, 1, signed: true) : "");
        return table.Render();
    }
}
