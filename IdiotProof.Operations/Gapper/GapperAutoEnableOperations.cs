using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Engine.Settings;
using IdiotProof.Monitor;
using IdiotProof.Operations.Rendering;
using IdiotProof.Operations.Strategies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Gapper;

public sealed record GapperAutoEnableArgs
{
    [Option("dry-run", Description = "Preview what would arm; writes and arms nothing.", Short = 'n')]
    public bool DryRun { get; init; }
}

/// <summary>
/// Runs the auto-gapper for the caller now (the old <c>auto-gapper --arm</c>): screens the live
/// movers, synthesizes a per-ticker plan for each qualifier, arms the top N as PAPER strategies
/// owned by the caller, and records the scan and every candidate. Refuses unless the auto-gapper
/// is configured paper-only; the scanner itself also refuses when the paper route resolves live.
/// </summary>
[Operation("gapper auto enable", Summary = "Run the auto-gapper for you now and arm its top picks as paper gappers (paper-only; --dry-run previews).", Risk = OperationRisk.Write)]
[Example("idiotproof gapper auto enable --dry-run")]
[Example("idiotproof gapper auto enable")]
public sealed class GapperAutoEnableOperation : IOperation<GapperAutoEnableArgs>
{
    public async Task<OperationResult> ExecuteAsync(GapperAutoEnableArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var settings = GapperAutoRules.Settings(ctx);
        if (!GapperAutoRules.IsPaperOnly(settings))
            return OperationResult.Blocked($"The auto-gapper routes '{settings.AutoGapperBrokerMode}', not paper. It only ever arms paper strategies — set AutoGapperBrokerMode (IDIOTPROOF_AUTOGAPPER_BROKER) to paper.");

        var scanner = ctx.Services.GetService<AutoGapperScanner>() ?? ActivatorUtilities.CreateInstance<AutoGapperScanner>(ctx.Services);
        var started = ctx.UtcNow.AddSeconds(-1);
        var r = await scanner.RunScanAsync(owner, args.DryRun, phase: "manual", ctx.CancellationToken);

        var data = new { r.Screened, r.Qualified, r.Armed, r.Skipped, r.Note, DryRun = args.DryRun };
        var text = $"Auto-gapper {(args.DryRun ? "preview" : "run")}: screened {r.Screened}, qualified {r.Qualified}, " +
                   $"{(args.DryRun ? "would arm" : "armed")} {r.Armed}, skipped {r.Skipped} ({r.Note}).";
        if (args.DryRun)
            return OperationResult.Ok(data, text + (r.Armed > 0 ? "\nRun it without --dry-run to arm them (paper)." : ""));

        await using var db = await ctx.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ctx.CancellationToken);
        var scan = await db.AutoGapperScans.AsNoTracking().Where(s => s.ScanStartedUtc >= started && s.Phase == "manual")
            .OrderByDescending(s => s.ScanStartedUtc).FirstOrDefaultAsync(ctx.CancellationToken);
        if (scan is not null)
            text += "\n\n" + GapperAutoCandidatesOperation.Table(ctx,
                await db.AutoGapperCandidates.AsNoTracking().Where(c => c.ScanId == scan.Id).OrderBy(c => c.Rank).ToListAsync(ctx.CancellationToken));
        return OperationResult.Ok(data, text + "\n`idiotproof gapper auto disable` disarms every auto-armed gapper that holds no position.");
    }
}

[Operation("gapper auto disable", Summary = "Disarm every auto-armed gapper of yours that holds no position (holding ones stay armed to manage their exit).", Risk = OperationRisk.Write)]
[Example("idiotproof gapper auto disable")]
public sealed class GapperAutoDisableOperation : IOperation<NoArgs>
{
    public async Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var repo = ctx.Get<StrategyRepository>();
        var armed = (await repo.GetAllForUserAsync(owner, ctx.CancellationToken))
            .Where(s => s.IsActive && s.Author == GapperAutoRules.AuthorTag).OrderBy(s => s.Symbol).ToList();

        var rows = new List<(string Symbol, Guid Id, string Outcome)>();
        foreach (var s in armed)
        {
            var result = await repo.SetActiveAsync(s.Id, false, owner, ctx.CancellationToken);
            rows.Add((s.Symbol, s.Id, result switch
            {
                StrategyMutation.Ok => "disarmed",
                StrategyMutation.PositionOpen => $"kept armed — holding {s.PositionQty} sh",
                _ => result.ToString(),
            }));
        }

        var data = rows.Select(r => new { r.Symbol, r.Id, r.Outcome }).ToList();
        if (rows.Count == 0) return OperationResult.Ok(data, "No auto-armed gappers are active.");
        var table = new TextTable { MaxWidth = ctx.Width }.Column("id").Column("sym").Column("outcome");
        foreach (var r in rows) table.Row(StrategyRefs.ShortGuid(r.Id), r.Symbol, r.Outcome);
        return OperationResult.Ok(data, table.Render() + $"\n{rows.Count(r => r.Outcome == "disarmed")} of {rows.Count} disarmed.");
    }
}
