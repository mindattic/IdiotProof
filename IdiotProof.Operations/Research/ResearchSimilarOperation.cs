using IdiotProof.Blazor.Services;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Research;

public sealed record ResearchSimilarArgs
{
    [Argument(0, Name = "claim", Description = "Claim id, or the 8-character prefix `research claims` shows.")]
    public string Claim { get; init; } = "";

    [Option("max-distance", Description = "Largest Hamming distance (of 64 signature bits) that counts as similar, 0-64 (default 20).")]
    public int MaxDistance { get; init; } = 20;

    [Option("limit", Description = "Most neighbours to list, 1-100 (default 20).")]
    public int Limit { get; init; } = 20;
}

/// <summary>
/// "When signals that looked like this appeared before, what happened?" Lists a claim's nearest
/// neighbours by LSH signature (<see cref="ClaimCorrelationService"/>), nearest first, with each
/// one's realized outcome, and the aggregate the significance score's history term uses.
/// </summary>
[Operation("research similar", Summary = "Look-alike past claims (LSH neighbours) and what their stocks did afterwards.", Risk = OperationRisk.Read)]
[Example("idiotproof research similar 3f2a91c0")]
[Example("idiotproof research similar 3f2a91c0 --max-distance 12 --limit 10")]
public sealed class ResearchSimilarOperation : IOperation<ResearchSimilarArgs>
{
    public async Task<OperationResult> ExecuteAsync(ResearchSimilarArgs args, OperationContext ctx)
    {
        if (args.MaxDistance is < 0 or > 64) return OperationResult.Usage("--max-distance must be 0-64.");
        if (args.Limit is < 1 or > 100) return OperationResult.Usage("--limit must be 1-100.");
        var claim = await ResearchSupport.ResolveClaimAsync(ctx, args.Claim);
        var correlation = ctx.Get<ClaimCorrelationService>();

        var neighbors = await correlation.FindNeighborsAsync(claim.Id, args.MaxDistance, args.Limit, ctx.CancellationToken);
        var aggregate = await correlation.FindSimilarOutcomesAsync(claim.Id, args.MaxDistance, ctx.CancellationToken);

        var rows = neighbors.Select(n => new { Distance = n.Distance, Similarity = Math.Round(1 - n.Distance / 64.0, 3), Claim = ResearchSupport.ToRow(n.Claim) }).ToList();
        var data = new { Claim = ResearchSupport.ToRow(claim), args.MaxDistance, Aggregate = aggregate, Neighbors = rows };
        var head = $"{(claim.IsMacro ? "(macro)" : claim.Ticker)} {ResearchSupport.Short(claim.Id)}: {claim.ClaimSummary}";

        if (rows.Count == 0)
            return OperationResult.Ok(data, aggregate.SimilarCount == 0 && neighbors.Count == 0
                ? $"{head}\nNo look-alike claims within {args.MaxDistance} bits (or this claim has no similarity vector yet)."
                : $"{head}\nNo look-alike claims within {args.MaxDistance} bits.");

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("dist", Align.Right).Column("id").Column("date").Column("ticker").Column("type").Column("sent")
            .Column("outcome", Align.Right).Column("summary", maxWidth: 60);
        foreach (var r in rows)
            table.Row(r.Distance.ToString(), r.Claim.Short, r.Claim.ArticleDate.ToString("yyyy-MM-dd"),
                r.Claim.IsMacro ? "(macro)" : r.Claim.Ticker, r.Claim.Type, r.Claim.Sentiment,
                r.Claim.OutcomePct is { } o ? Fmt.PercentPoints(o, 1, signed: true) : "", r.Claim.Summary);

        var summary = aggregate.WithOutcomes == 0
            ? $"{rows.Count} look-alikes; none has a backfilled outcome yet."
            : $"{aggregate.WithOutcomes} of {aggregate.SimilarCount} look-alikes have outcomes: {aggregate.BullishCount} rose, {aggregate.BearishCount} fell"
              + (aggregate.AvgPctChange is { } avg ? $", average {Fmt.PercentPoints((decimal)avg, 2, signed: true)}." : ".");
        return OperationResult.Ok(data, $"{head}\n{table.Render()}\n{summary}");
    }
}
