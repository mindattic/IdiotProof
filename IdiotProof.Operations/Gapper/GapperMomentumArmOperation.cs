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

    [Option("expire", Description = "Only deactivate every active Momentum pick strategy that holds no position (run after the close); arms nothing.")]
    public bool Expire { get; init; }
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

    /// <summary>
    /// Deactivates every active, flat Momentum pick strategy (a held position keeps its strategy so its exits run),
    /// except, when <paramref name="keepCreatedOn"/> is set, the ones created that ET day. Returns their symbols.
    /// </summary>
    private static async Task<List<string>> ExpireAsync(OperationContext ctx, Guid owner, DateOnly? keepCreatedOn)
    {
        var repo = ctx.Get<StrategyRepository>();
        var stale = (await repo.GetAllForUserAsync(owner, ctx.CancellationToken))
            .Where(s => s.IsActive && s.PositionQty == 0 && IsMomentumPick(s)
                        && (keepCreatedOn is not { } keep || DateOnly.FromDateTime(EasternClock.ToEastern(s.CreatedUtc)) != keep))
            .ToList();
        var done = new List<string>();
        foreach (var s in stale)
            if ((await StrategyActivateOperation.SetActiveAsync(ctx, s, false)).IsOk) done.Add(s.Symbol);
        return done;
    }

    /// <summary>A Momentum pick strategy: kind "momentum" (IP-T593), or a row saved before kinds existed by its title.</summary>
    private static bool IsMomentumPick(IdiotProof.Core.Data.Strategy s) =>
        s.Kind is { } k ? k == StrategyRepository.KindMomentum : s.Title.StartsWith(TitlePrefix, StringComparison.Ordinal);

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

        // A strategy armed for one session must not carry into the next: its entry window is a time of day, so
        // yesterday's premarket-high break would fire again on a stale level. --expire (scheduled after the close)
        // and every arming run first deactivate the flat Momentum pick strategies.
        if (args.Expire || !args.DryRun)
        {
            var expired = await ExpireAsync(ctx, owner, keepCreatedOn: args.Expire ? null : today);
            if (args.Expire)
                return OperationResult.Ok(new { Expired = expired }, expired.Count == 0
                    ? "No active Momentum pick strategy to expire."
                    : $"Expired {expired.Count} flat Momentum pick strateg{(expired.Count == 1 ? "y" : "ies")}: {string.Join(", ", expired)}.");
        }
        if (!args.DryRun && (d != today || nowEt.TimeOfDay < ArmAt))
            return OperationResult.Usage($"Strategies are saved only for today at or after 09:29 ET, once the premarket high is known (now {nowEt:yyyy-MM-dd HH:mm} ET). Use --dry-run to look.");
        // Run daily by MindAttic.Scheduler (which also catches up a missed run late): a non-trading day, or a run
        // after the 15:30 ET entry window closed, arms nothing and is not an error.
        if (!args.DryRun && (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || IdiotProof.Scripting.MarketTime.IsMarketHoliday(d)))
            return OperationResult.Ok(new { Session = d, Picks = 0, Saved = 0 }, $"{d:yyyy-MM-dd} is not a trading day; nothing armed.");
        if (!args.DryRun && nowEt.TimeOfDay >= new TimeSpan(15, 30, 0))
            return OperationResult.Ok(new { Session = d, Picks = 0, Saved = 0 }, $"Past the 15:30 ET entry window ({nowEt:HH:mm} ET); nothing armed today.");
        var forming = args.DryRun && d == today && nowEt.TimeOfDay < ArmAt ? "Before 09:29 ET the premarket high is still forming.\n" : "";

        var picks = await ctx.Get<GapperRadarService>().MomentumPicksAsync(d, ctx.UtcNow, ctx.CancellationToken);
        if (picks.Count == 0)
            return OperationResult.Ok(new { Session = d, Picks = 0 }, $"No Momentum. picks recorded for {d:yyyy-MM-dd} (run the tracker's import + extract after his video).");

        var existing = args.DryRun ? [] : (await ctx.Get<StrategyRepository>().GetAllForUserAsync(owner, ctx.CancellationToken))
            .Where(s => s.CreatedUtc >= ctx.UtcNow.AddHours(-18) && IsMomentumPick(s)).Select(s => s.Symbol).ToHashSet(StringComparer.Ordinal);
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
                        $"IP-T502 amendment 04 forward test: Momentum. pick #{rank} for {d:yyyy-MM-dd}, premarket high {pmh}.",
                        kind: StrategyRepository.KindMomentum);
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
