using IdiotProof.Operations.Rendering;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Research;

/// <summary>
/// Explains a claim's significance score term by term — magnitude, trigger confidence, the
/// look-alike claims' history, the source's track record, watchlist membership and recency —
/// with the input behind each term and the formula that combines them. Read-only: it recomputes
/// the score as of now and shows it next to the stored one.
/// </summary>
[Operation("research score", Summary = "Explain a claim's 0-100 significance score term by term (magnitude, confidence, history, source, watchlist, recency).", Risk = OperationRisk.Read)]
[Example("idiotproof research score 3f2a91c0")]
public sealed class ResearchScoreOperation : IOperation<ResearchClaimRefArgs>
{
    public async Task<OperationResult> ExecuteAsync(ResearchClaimRefArgs args, OperationContext ctx)
    {
        var claim = await ResearchSupport.ResolveClaimAsync(ctx, args.Claim);
        var watchlist = await ScanPassRunner.GetWatchlistSymbolsAsync(ResearchSupport.Db(ctx), ctx.CancellationToken);
        var b = await ctx.Get<SignificanceScorer>().ExplainAsync(claim.Id, watchlist, ctx.CancellationToken)
            ?? throw OperationException.Failed($"Claim {claim.Id} disappeared while scoring.");

        var corr = b.Correlation;
        var terms = new List<ScoreTerm>
        {
            new("magnitude", $"{claim.Magnitude} (High 100, Medium 60, Low 20)", b.MagnitudeScore, "points"),
            new("confidence", claim.HasHappenedAlready
                ? "already happened (×1.0)"
                : $"portent, trigger confidence {claim.TriggerConfidence ?? "unstated"} (High 1.0, Medium 0.7, Low 0.4, unstated 0.85)",
                b.ConfidenceMultiplier, "×"),
            new("history", corr.WithOutcomes == 0
                ? $"{corr.SimilarCount} look-alike claims, none with outcomes yet"
                : $"{corr.WithOutcomes} look-alikes with outcomes: {corr.BullishCount} up, {corr.BearishCount} down"
                  + (corr.AvgPctChange is { } avg ? $", average {ResearchSupport.N(avg, "+0.00;-0.00")}%" : "")
                  + " (min(30, 3×n) × consistency)",
                b.HistoryBonus, "points"),
            new("source", b.SourceConfidencePct is { } pct
                ? $"{claim.SourceName} right {ResearchSupport.N(pct, "0")}% of the time ((pct − 50) / 50 × 10)"
                : $"{claim.SourceName} has no settled claims yet",
                b.SourceBonus, "points"),
            new("watchlist", b.WatchlistMatch ? "on a watchlist" : "not on any watchlist", b.WatchlistBonus, "points"),
            new("recency", $"{ResearchSupport.N(b.DaysOld, "0.0")} days old (1 − days/30, floor 0.5)", b.RecencyMultiplier, "×"),
        };

        var data = new { Claim = ResearchSupport.ToRow(claim), Stored = claim.SignificanceScore, Breakdown = b, Terms = terms };

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("term").Column("value", Align.Right).Column("from", maxWidth: 80);
        foreach (var t in terms)
            table.Row(t.Term, t.Unit == "×" ? "×" + ResearchSupport.N(t.Value, "0.00") : ResearchSupport.N(t.Value, "0.0"), t.Input);

        var formula = $"score = clamp(({ResearchSupport.N(b.MagnitudeScore, "0")} × {ResearchSupport.N(b.ConfidenceMultiplier, "0.00")}"
            + $" + {ResearchSupport.N(b.HistoryBonus)} + {ResearchSupport.N(b.SourceBonus)} + {ResearchSupport.N(b.WatchlistBonus)})"
            + $" × {ResearchSupport.N(b.RecencyMultiplier, "0.00")}, 0, 100) = {ResearchSupport.N(b.Score)}";
        var stored = claim.SignificanceScore is { } s
            ? $"Stored score {ResearchSupport.N(s)} (set by the last scan pass; recency has moved since)."
            : "Not scored yet — the next `idiotproof research scan` stores it.";
        return OperationResult.Ok(data,
            $"{claim.Ticker} {ResearchSupport.Short(claim.Id)}: {claim.ClaimSummary}\n{table.Render()}\n{formula}\n{stored}");
    }
}

/// <summary>One term of a significance score: what it is, what it was computed from, its value.</summary>
public sealed record ScoreTerm(string Term, string Input, double Value, string Unit);
