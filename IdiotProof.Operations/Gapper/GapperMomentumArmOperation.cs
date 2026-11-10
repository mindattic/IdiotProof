using System.Globalization;
using IdiotProof.Core.Evidence;
using IdiotProof.Core.Research.Gappers;
using IdiotProof.Core.Services;
using IdiotProof.Core.Trading;
using IdiotProof.Models;
using IdiotProof.Operations.Evidence;
using IdiotProof.Operations.Rendering;
using IdiotProof.Operations.Strategies;

namespace IdiotProof.Operations.Gapper;

public sealed record GapperMomentumArmArgs
{
    [Option("date", Description = "The session, yyyy-MM-dd (default: today ET). Strategies are saved only for today, at or after 09:29 ET.")]
    public DateOnly? Date { get; init; }

    [Option("activate", Description = "Hand the new paper strategies to the Monitor (default: saved inactive).")]
    public bool Activate { get; init; }

    [Option("dry-run", Description = "Show the picks, premarket highs and scripts without saving anything (any time, any session).")]
    public bool DryRun { get; init; }
}

/// <summary>
/// IP-T502, amendment 04 of the Momentum. edge study: the paper forward test of the best H3 rule on his picks. For each
/// pick for the session it reads the 04:00-09:29 ET premarket high and saves a 1-share Paper strategy: break of that high
/// from 09:30, +10% target, out when price loses VWAP, -10% backstop, flat by 15:55. The rule was +0.5-0.6% per trade
/// before costs on fit and validation and failed on an assumed 2% round trip; this test measures the real cost.
/// </summary>
[Operation("gapper momentum-arm", Summary = "Paper forward test (IP-T502): at 09:29 ET or later, save one 1-share Paper strategy per Momentum. pick for today: premarket-high break, +10% target, exit on VWAP loss, -10% backstop, out by 15:55. Saved inactive unless --activate; never Live.", Risk = OperationRisk.Write)]
[Example("idiotproof gapper momentum-arm --dry-run")]
[Example("idiotproof gapper momentum-arm --activate")]
public sealed class GapperMomentumArmOperation : IOperation<GapperMomentumArmArgs>
{
    public const string TitlePrefix = "Momentum pick";
    private static readonly TimeSpan PremarketStart = new(4, 0, 0), ArmAt = new(9, 29, 0), Open = new(9, 30, 0);

    public static string Script(string symbol, decimal pmHigh) =>
        $"Stock.Ticker(\"{symbol}\").Name(\"{TitlePrefix} {symbol} PM-high break\").Session(TradingSession.RTH).RequireEntryWindow(\"09:30\", \"15:30\")" +
        $".Breakout({pmHigh.ToString(CultureInfo.InvariantCulture)}).While().IsAboveVwap().EndWhile().ExitWhenFalse()" +
        ".Quantity(1).Long().StopLossPercent(10).TakeProfitPercent(10).SellBy(\"15:55\").Build()";

    public async Task<OperationResult> ExecuteAsync(GapperMomentumArmArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var nowEt = EasternClock.ToEastern(ctx.UtcNow);
        var today = DateOnly.FromDateTime(nowEt);
        var d = args.Date ?? today;
        if (!args.DryRun && (d != today || nowEt.TimeOfDay < ArmAt))
            return OperationResult.Usage($"Strategies are saved only for today at or after 09:29 ET, once the premarket high is known (now {nowEt:yyyy-MM-dd HH:mm} ET). Use --dry-run to look.");
        var forming = args.DryRun && d == today && nowEt.TimeOfDay < ArmAt ? "Before 09:29 ET the premarket high is still forming.\n" : "";

        var picks = await ctx.Get<GapperRadarService>().MomentumPicksAsync(d, ctx.UtcNow, ctx.CancellationToken);
        if (picks.Count == 0)
            return OperationResult.Ok(new { Session = d, Picks = 0 }, $"No Momentum. picks recorded for {d:yyyy-MM-dd} (run the tracker's import + extract after his video).");

        var existing = args.DryRun ? [] : (await ctx.Get<StrategyRepository>().GetAllForUserAsync(owner, ctx.CancellationToken))
            .Where(s => s.CreatedUtc >= ctx.UtcNow.AddHours(-18) && s.Title.StartsWith(TitlePrefix, StringComparison.Ordinal)).Select(s => s.Symbol).ToHashSet(StringComparer.Ordinal);
        var bars = ctx.Get<IHistoricalBarStore>();
        var table = new TextTable { MaxWidth = ctx.Width }.Column("rank", Align.Right).Column("sym").Column("pm high", Align.Right).Column("result");
        var rows = new List<object>();
        int saved = 0;
        foreach (var (sym, rank) in picks.OrderBy(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal))
        {
            var day = await bars.GetBarsAsync(sym, d, d, BarTimeframe.Minute1, ctx.CancellationToken);
            var pm = day.Where(c => EasternClock.ToEastern(c.StartUtc) is var t && DateOnly.FromDateTime(t) == d && t.TimeOfDay >= PremarketStart && t.TimeOfDay < Open).ToList();
            if (pm.Count == 0)
            {
                table.Row(rank.ToString(CultureInfo.InvariantCulture), sym, "-", "skipped: no premarket bars");
                rows.Add(new { Symbol = sym, Rank = rank, Result = "no premarket bars" });
                continue;
            }
            var pmh = pm.Max(c => c.High);
            string result;
            if (existing.Contains(sym)) result = "skipped: already saved today";
            else
            {
                var def = StrategyDrafts.FromScript(Script(sym, pmh));
                StrategyDrafts.ApplyDefaults(def, sym);
                var v = StrategyDrafts.Validate(def);
                if (v.HasErrors) result = "invalid: " + string.Join("; ", v.Findings.Select(f => f.ToString()));
                else if (args.DryRun) result = "dry run";
                else
                {
                    var created = await StrategyDrafts.SaveAsync(ctx, def, $"{TitlePrefix} {sym} {d:yyyy-MM-dd} PM-high break", BrokerMode.Paper, "momentum-arm",
                        $"IP-T502 amendment 04 forward test: Momentum. pick #{rank} for {d:yyyy-MM-dd}, premarket high {pmh}.");
                    saved++;
                    result = $"saved {StrategyRefs.ShortGuid(created.Id)} (paper)";
                    if (args.Activate)
                    {
                        var r = await StrategyActivateOperation.SetActiveAsync(ctx, created, true);
                        result += r.IsOk ? ", active" : ", not activated: " + (r.Message ?? r.Human);
                    }
                }
            }
            table.Row(rank.ToString(CultureInfo.InvariantCulture), sym, pmh.ToString(pmh < 1 ? "0.0000" : "0.00", CultureInfo.InvariantCulture), result);
            rows.Add(new { Symbol = sym, Rank = rank, PremarketHigh = pmh, Result = result });
        }
        var text = forming + $"Momentum. picks for {d:yyyy-MM-dd}: {picks.Count}, saved {saved}.\n" + table.Render() +
                   "\nRule: break of the premarket high from 09:30, +10% target, out on VWAP loss, -10% backstop, flat 15:55; 1 share, Paper. " +
                   "Forward test of a rule that failed validation on an assumed 2% cost (amendment 04): it measures the real cost, it is not a proven edge.";
        return OperationResult.Ok(new { Session = d, Picks = picks.Count, Saved = saved, Rows = rows }, text);
    }
}
