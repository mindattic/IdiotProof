using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Engine.Settings;
using IdiotProof.Monitor;
using IdiotProof.Operations.Rendering;
using IdiotProof.Operations.Strategies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Gapper;

public sealed record GapperAutoCandidatesArgs
{
    [Option("date", Description = "ET scan day (yyyy-MM-dd); default the latest scan's day.", Short = 'd')]
    public DateOnly? Date { get; init; }
}

[Operation("gapper auto candidates", Summary = "Every ticker an auto-gapper scan screened on a day: signals, the adaptive plan it chose, and armed or why skipped.", Risk = OperationRisk.Read)]
[Example("idiotproof gapper auto candidates")]
[Example("idiotproof gapper auto candidates --date 2026-10-29")]
public sealed class GapperAutoCandidatesOperation : IOperation<GapperAutoCandidatesArgs>
{
    public async Task<OperationResult> ExecuteAsync(GapperAutoCandidatesArgs args, OperationContext ctx)
    {
        ctx.RequireOwner();
        await using var db = await ctx.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ctx.CancellationToken);
        var day = args.Date?.ToString("yyyy-MM-dd")
            ?? await db.AutoGapperScans.AsNoTracking().OrderByDescending(s => s.ScanStartedUtc).Select(s => s.ScanEtDate).FirstOrDefaultAsync(ctx.CancellationToken);
        if (day is null) return OperationResult.Ok(Array.Empty<object>(), "No auto-gapper scans recorded yet. `idiotproof gapper auto enable --dry-run` previews one.");

        var rows = await db.AutoGapperCandidates.AsNoTracking().Where(c => c.ScanEtDate == day)
            .OrderBy(c => c.CapturedUtc).ThenBy(c => c.Rank).ToListAsync(ctx.CancellationToken);
        if (rows.Count == 0) return OperationResult.Ok(rows, $"No auto-gapper candidates recorded for {day}.");
        return OperationResult.Ok(rows, $"Auto-gapper candidates, {day}:\n" + Table(ctx, rows) +
            $"\n{rows.Count} screened, {rows.Count(r => r.Armed)} armed.");
    }

    internal static string Table(OperationContext ctx, IReadOnlyList<AutoGapperCandidate> rows)
    {
        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("#", Align.Right).Column("sym").Column("price", Align.Right).Column("gap", Align.Right).Column("vol×", Align.Right)
            .Column("atr", Align.Right).Column("class").Column("stop", Align.Right).Column("trail", Align.Right).Column("giveback", Align.Right)
            .Column("size", Align.Right).Column("outcome");
        foreach (var c in rows)
            table.Row(c.Rank.ToString(), c.Symbol, Fmt.Money(c.Price), Fmt.PercentPoints((decimal)c.GapPercent, 1, signed: true),
                c.VolumeRatio is { } v ? GapperRules.Num(v) : "—", c.AtrPercent is { } a ? GapperRules.Num(a) + "%" : "—", c.BehaviorClass,
                GapperRules.Num(c.StopLossPercent) + "%", c.TrailingStopPercent is { } t ? GapperRules.Num(t) + "%" : "—",
                $"{GapperRules.Num(c.PeakGivebackPercent)}% @{c.ArmExitEt}", Fmt.Money(c.Notional, 0),
                c.Armed ? $"ARMED {(c.StrategyId is { } id ? StrategyRefs.ShortGuid(id) : "")}" : "skip: " + (c.SkipReason ?? "—"));
        return table.Render();
    }
}
