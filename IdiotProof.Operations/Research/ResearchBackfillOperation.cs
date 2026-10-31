using IdiotProof.Engine.Settings;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Research;

public sealed record ResearchBackfillArgs
{
    [Option("min-wait-days", Description = "Only claims at least this many days old, 1-365 (default 5).")]
    public int MinWaitDays { get; init; } = 5;

    [Option("window-days", Description = "Measure the price move this many days after the claim, 1-365 (default 7).")]
    public int WindowDays { get; init; } = 7;

    [Option("max", Description = "Most claims to backfill this run, 1-5000 (default 200).")]
    public int Max { get; init; } = 200;
}

/// <summary>
/// Runs <see cref="OutcomeBackfillService"/> now: for claims old enough that their predicted
/// move has had time to happen, records the close on the claim date and on the outcome date
/// (Alpaca daily bars), settles pending portents as Realized or Disproven, and credits each
/// source's track record. This is what feeds the significance score's history and source terms.
/// </summary>
[Operation("research backfill", Summary = "Record what each old enough claim's stock actually did (Alpaca daily closes) and settle portents.", Risk = OperationRisk.Write)]
[Example("idiotproof research backfill")]
[Example("idiotproof research backfill --min-wait-days 10 --window-days 10 --max 500")]
public sealed class ResearchBackfillOperation : IOperation<ResearchBackfillArgs>
{
    public async Task<OperationResult> ExecuteAsync(ResearchBackfillArgs args, OperationContext ctx)
    {
        if (args.MinWaitDays is < 1 or > 365) return OperationResult.Usage("--min-wait-days must be 1-365.");
        if (args.WindowDays is < 1 or > 365) return OperationResult.Usage("--window-days must be 1-365.");
        if (args.Max is < 1 or > 5000) return OperationResult.Usage("--max must be 1-5000.");
        if (string.IsNullOrWhiteSpace(ctx.Get<AppSettings>().AlpacaApiKeyId))
            return OperationResult.Blocked("The host has no Alpaca key for outcome backfill (the scanner's AlpacaApiKeyId setting).");

        var dbFactory = ResearchSupport.Db(ctx);
        var cutoff = DateOnly.FromDateTime(ctx.UtcNow).AddDays(-args.MinWaitDays);
        List<Guid> waiting;
        await using (var db = await dbFactory.CreateDbContextAsync(ctx.CancellationToken))
            waiting = await db.ResearchClaims
                .Where(c => !c.IsMacro && c.Ticker != "" && c.OutcomeDate == null && c.ArticleDate <= cutoff)
                .Select(c => c.Id)
                .ToListAsync(ctx.CancellationToken);

        var count = await ctx.Get<OutcomeBackfillService>().BackfillAsync(args.MinWaitDays, args.WindowDays, args.Max, ctx.CancellationToken);

        List<ResearchClaimRow> settled = [];
        if (count > 0)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ctx.CancellationToken);
            settled = (await db.ResearchClaims.AsNoTracking()
                    .Where(c => waiting.Contains(c.Id) && c.OutcomeDate != null)
                    .OrderBy(c => c.Ticker).ThenBy(c => c.ArticleDate)
                    .ToListAsync(ctx.CancellationToken))
                .Select(ResearchSupport.ToRow)
                .ToList();
        }

        var data = new { Candidates = waiting.Count, Backfilled = count, args.MinWaitDays, args.WindowDays, Claims = settled };
        if (count == 0)
            return OperationResult.Ok(data, waiting.Count == 0
                ? "Nothing to backfill: every claim old enough already has its outcome."
                : $"{waiting.Count} claims are waiting, but none could be settled yet (outcome window not over, or no bars for the ticker).");

        var realized = settled.Count(r => r.Status == "Realized");
        var disproven = settled.Count(r => r.Status == "Disproven");
        return OperationResult.Ok(data,
            $"Backfilled {count} of {waiting.Count} waiting claims ({args.WindowDays}-day move): {realized} realized, {disproven} disproven.\n"
            + ResearchSupport.ClaimsTable(settled, ctx.Width));
    }
}
