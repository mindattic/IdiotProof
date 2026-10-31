using System.Globalization;
using IdiotProof.Core.Data;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Diary;

public sealed record DiaryStatsArgs : DiaryFilterArgs
{
    [Option("by", Description = "Also break the numbers down per strategy or per symbol.")]
    public DiaryPnlBy? By { get; init; }
}

/// <summary>Performance of a set of closed trades.</summary>
public sealed record DiaryStats(
    int Trades, int Wins, int Losses, int Breakeven, decimal? WinRate, decimal NetPnL, decimal GrossProfit, decimal GrossLoss,
    decimal? ProfitFactor, decimal? AvgWin, decimal? AvgLoss, decimal? Payoff, decimal Expectancy, decimal? AvgReturnPct,
    int TradesWithR, decimal? AvgR, int TradesWithExcursion, decimal? AvgMfePct, decimal? AvgMaePct, double? AvgHoldMinutes,
    decimal? LargestWin, decimal? LargestLoss, int MaxConsecutiveLosses, decimal MaxDrawdown);

/// <summary>
/// How the closed trades performed: win rate, expectancy per trade, profit factor and payoff,
/// average R (realized P&amp;L over the dollars the stop put at risk, where a stop was set),
/// average MFE/MAE (best and worst excursion while held, from the Monitor's recorded bars where
/// they still exist), hold time, streaks and drawdown. Optionally per strategy or symbol.
/// </summary>
[Operation("diary stats", Summary = "Trading performance from the diary: win rate, expectancy, profit factor, avg R, MFE/MAE, drawdown.", Risk = OperationRisk.Read)]
[Example("idiotproof diary stats")]
[Example("idiotproof diary stats --mode paper --since 2026-10-01")]
[Example("idiotproof diary stats --by strategy")]
public sealed class DiaryStatsOperation : IOperation<DiaryStatsArgs>
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public async Task<OperationResult> ExecuteAsync(DiaryStatsArgs args, OperationContext ctx)
    {
        var closed = (await DiarySupport.LoadAsync(ctx, args, TradeDiaryStatus.Closed))
            .Where(t => t.ExitUtc is not null && t.RealizedPnL is not null)
            .OrderBy(t => t.ExitUtc)
            .ToList();
        var excursions = await ExcursionsAsync(ctx, closed);

        var overall = Compute(closed, excursions);
        var groups = closed.Where(_ => args.By is not null)
            .GroupBy(t => args.By == DiaryPnlBy.Strategy ? t.StrategyTitle : t.Symbol)
            .Select(g => new { Group = g.Key, Stats = Compute(g.ToList(), excursions) })
            .OrderByDescending(g => g.Stats.NetPnL)
            .ToList();

        var head = $"Diary stats ({DiarySupport.Describe(args)})";
        var data = new { Overall = overall, By = args.By?.ToString().ToLowerInvariant(), Groups = groups };
        if (closed.Count == 0) return OperationResult.Ok(data, $"{head}\nNo closed trades match.");

        var s = overall;
        var kv = new KeyValueBlock()
            .Add("trades", $"{s.Trades} closed: {s.Wins} won, {s.Losses} lost" + (s.Breakeven > 0 ? $", {s.Breakeven} flat" : ""))
            .Add("win rate", Pct(s.WinRate))
            .Add("net P&L", $"{Fmt.SignedMoney(s.NetPnL)} ({Fmt.Money(s.GrossProfit)} won, {Fmt.Money(s.GrossLoss)} lost)")
            .Add("expectancy", $"{Fmt.SignedMoney(s.Expectancy)} per trade" + (s.AvgReturnPct is { } ar ? $" ({Fmt.PercentPoints(ar, 2, signed: true)} avg return)" : ""))
            .Add("profit factor", s.ProfitFactor is { } pf ? pf.ToString("0.00", Inv) : "n/a (no losing trades)")
            .Add("avg win / loss", $"{Money(s.AvgWin)} / {Money(s.AvgLoss)}" + (s.Payoff is { } po ? $" (payoff {po.ToString("0.00", Inv)})" : ""))
            .Add("avg R", s.AvgR is { } r ? $"{r.ToString("+0.00;-0.00", Inv)}R over {s.TradesWithR} trades with a stop" : "n/a (no trade recorded a stop)")
            .Add("MFE / MAE", s.AvgMfePct is { } mfe
                ? $"{Fmt.PercentPoints(mfe, 2, signed: true)} / {Fmt.PercentPoints(s.AvgMaePct!.Value, 2, signed: true)} avg over {s.TradesWithExcursion} trades with recorded bars"
                : "n/a (the Monitor's bars for these trades are no longer kept)")
            .Add("avg hold", s.AvgHoldMinutes is { } h ? DiarySupport.Hold(h) : null)
            .Add("best / worst", $"{Money(s.LargestWin)} / {Money(s.LargestLoss)}")
            .Add("max drawdown", $"{Fmt.Money(s.MaxDrawdown)} (closed-trade equity); longest losing streak {s.MaxConsecutiveLosses}");

        var blocks = new List<string> { head, kv.Render() };
        if (groups.Count > 0)
        {
            var table = new TextTable { MaxWidth = ctx.Width }
                .Column(args.By!.Value.ToString().ToLowerInvariant(), maxWidth: 30).Column("trades", Align.Right)
                .Column("win %", Align.Right).Column("net", Align.Right).Column("expect", Align.Right)
                .Column("PF", Align.Right).Column("avg R", Align.Right);
            foreach (var g in groups)
                table.Row(g.Group, g.Stats.Trades.ToString(), Pct(g.Stats.WinRate), Fmt.SignedMoney(g.Stats.NetPnL),
                    Fmt.SignedMoney(g.Stats.Expectancy), g.Stats.ProfitFactor?.ToString("0.00", Inv) ?? "-",
                    g.Stats.AvgR?.ToString("+0.00;-0.00", Inv) ?? "-");
            blocks.Add(table.Render());
        }
        return OperationResult.Ok(data, string.Join("\n", blocks));
    }

    /// <summary>The arithmetic, over trades in exit order; <paramref name="excursions"/> maps trade id to (MFE%, MAE%).</summary>
    public static DiaryStats Compute(IReadOnlyList<TradeDiaryEntry> trades, IReadOnlyDictionary<Guid, (decimal Mfe, decimal Mae)> excursions)
    {
        var pnls = trades.Select(t => t.RealizedPnL ?? 0).ToList();
        var wins = pnls.Where(p => p > 0).ToList();
        var losses = pnls.Where(p => p < 0).ToList();
        var grossProfit = wins.Sum();
        var grossLoss = -losses.Sum();
        var net = pnls.Sum();

        var rs = trades.Select(t => (Risk: DiarySupport.InitialRisk(t), Pnl: t.RealizedPnL ?? 0))
            .Where(x => x.Risk is > 0).Select(x => x.Pnl / x.Risk!.Value).ToList();
        var ex = trades.Where(t => excursions.ContainsKey(t.Id)).Select(t => excursions[t.Id]).ToList();
        var returns = trades.Where(t => t.ReturnPercent is not null).Select(t => t.ReturnPercent!.Value).ToList();

        int streak = 0, maxStreak = 0;
        decimal equity = 0, peak = 0, drawdown = 0;
        foreach (var p in pnls)
        {
            streak = p < 0 ? streak + 1 : 0;
            maxStreak = Math.Max(maxStreak, streak);
            equity += p;
            peak = Math.Max(peak, equity);
            drawdown = Math.Max(drawdown, peak - equity);
        }

        return new DiaryStats(
            trades.Count, wins.Count, losses.Count, trades.Count - wins.Count - losses.Count,
            trades.Count > 0 ? Math.Round((decimal)wins.Count / trades.Count, 4) : null,
            net, grossProfit, grossLoss,
            grossLoss > 0 ? Math.Round(grossProfit / grossLoss, 2) : null,
            wins.Count > 0 ? Math.Round(wins.Average(), 2) : null,
            losses.Count > 0 ? Math.Round(losses.Average(), 2) : null,
            wins.Count > 0 && losses.Count > 0 ? Math.Round(wins.Average() / -losses.Average(), 2) : null,
            trades.Count > 0 ? Math.Round(net / trades.Count, 2) : 0,
            returns.Count > 0 ? Math.Round(returns.Average(), 2) : null,
            rs.Count, rs.Count > 0 ? Math.Round(rs.Average(), 2) : null,
            ex.Count, ex.Count > 0 ? Math.Round(ex.Average(e => e.Mfe), 2) : null, ex.Count > 0 ? Math.Round(ex.Average(e => e.Mae), 2) : null,
            trades.Count > 0 ? Math.Round(trades.Average(t => (t.ExitUtc!.Value - t.EntryUtc).TotalMinutes), 1) : null,
            wins.Count > 0 ? wins.Max() : null, losses.Count > 0 ? losses.Min() : null,
            maxStreak, drawdown);
    }

    /// <summary>
    /// Maximum favourable / adverse excursion (percent of entry, direction-aware) for each trade
    /// whose holding window is still covered by the Monitor's recorded one-minute bars.
    /// </summary>
    public static async Task<Dictionary<Guid, (decimal Mfe, decimal Mae)>> ExcursionsAsync(OperationContext ctx, IReadOnlyList<TradeDiaryEntry> trades)
    {
        var result = new Dictionary<Guid, (decimal, decimal)>();
        if (trades.Count == 0) return result;
        var ids = trades.Select(t => t.StrategyId).Distinct().ToList();
        var dates = trades.SelectMany(t => new[] { Fmt.EtDate(t.EntryUtc), Fmt.EtDate(t.ExitUtc!.Value) }).Distinct().ToList();

        await using var db = await ctx.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ctx.CancellationToken);
        var bars = await db.LiveBars.AsNoTracking()
            .Where(b => ids.Contains(b.StrategyId) && dates.Contains(b.DateEt))
            .Select(b => new { b.StrategyId, b.DateEt, b.Min, b.High, b.Low })
            .ToListAsync(ctx.CancellationToken);
        if (bars.Count == 0) return result;

        foreach (var t in trades)
        {
            if (t.EntryPrice <= 0) continue;
            var from = (Fmt.EtDate(t.EntryUtc), MinuteEt(t.EntryUtc));
            var to = (Fmt.EtDate(t.ExitUtc!.Value), MinuteEt(t.ExitUtc.Value));
            var held = bars.Where(b => b.StrategyId == t.StrategyId
                    && Compare((b.DateEt, b.Min), from) >= 0 && Compare((b.DateEt, b.Min), to) <= 0)
                .ToList();
            if (held.Count == 0) continue;
            var high = (decimal)held.Max(b => b.High);
            var low = (decimal)held.Min(b => b.Low);
            var up = (high - t.EntryPrice) / t.EntryPrice * 100m;
            var down = (low - t.EntryPrice) / t.EntryPrice * 100m;
            result[t.Id] = t.Direction == "Short" ? (Math.Round(-down, 4), Math.Round(-up, 4)) : (Math.Round(up, 4), Math.Round(down, 4));
        }
        return result;
    }

    private static int MinuteEt(DateTime utc)
    {
        var et = EasternClock.ToEastern(utc);
        return et.Hour * 60 + et.Minute;
    }

    private static int Compare((string Date, int Min) a, (string Date, int Min) b)
    {
        var c = string.CompareOrdinal(a.Date, b.Date);
        return c != 0 ? c : a.Min.CompareTo(b.Min);
    }

    private static string Pct(decimal? fraction) => fraction is { } f ? Fmt.Percent(f, 1) : "-";

    private static string Money(decimal? value) => value is { } v ? Fmt.SignedMoney(v) : "-";
}
