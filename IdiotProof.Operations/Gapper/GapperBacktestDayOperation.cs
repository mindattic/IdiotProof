using IdiotProof.Core.Trading;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Operations.Rendering;
using IdiotProof.Operations.Replay;
using IdiotProof.Operations.Strategies;
using IdiotProof.Scripting;
using IdiotProof.Strategies;

namespace IdiotProof.Operations.Gapper;

/// <summary>A saved <c>gapper backtest-day</c> result (store kind <c>gb</c>), the input to <c>gapper apply-tuned</c>.</summary>
public sealed record GapperBacktestRun(string Symbol, DateOnly Day, string ProfileName, string DataSource, GapperProfile Profile, GapperDayBacktestReport Report);

public sealed record GapperBacktestDayArgs : GapperDialArgs
{
    [Argument(0, Name = "symbol", Description = "The ticker to replay.")]
    public string Symbol { get; init; } = "";

    [Argument(1, Name = "date", Required = false, Description = "The ET trading day (yyyy-MM-dd); default the last completed trading day.")]
    public DateOnly? Date { get; init; }
}

/// <summary>
/// Replays a gapper profile over one past day's 4:00 AM-4:00 PM ET minute bars through
/// <see cref="GapperDayBacktester"/> — the same entry walk and <c>GapperExitEvaluator</c> the
/// Monitor runs — and reports the fills, max favorable/adverse excursion, what every giveback
/// setting would have done, and a hindsight-tuned profile. The result is saved under an id that
/// <c>gapper apply-tuned</c> takes.
/// </summary>
[Operation("gapper backtest-day", Summary = "Replay a gapper profile over one past day: fills, MFE/MAE, the giveback grid and a hindsight-tuned profile (saved for apply-tuned).", Risk = OperationRisk.Read)]
[Example("idiotproof gapper backtest-day NVDA 2026-10-29")]
[Example("idiotproof gapper backtest-day ABCD 2026-10-29 --profile penny-runner --giveback 20")]
public sealed class GapperBacktestDayOperation : IOperation<GapperBacktestDayArgs>
{
    public async Task<OperationResult> ExecuteAsync(GapperBacktestDayArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var symbol = GapperRules.NormalizeSymbol(args.Symbol);
        var profile = args.Tune(ctx);
        var profileName = GapperRules.ResolveProfile(ctx, args.Profile).Name + (args.AnyDial ? " (tuned)" : "");
        var day = args.Date ?? MarketTime.PreviousEquityTradingDayEt(ctx.UtcNow);
        var todayEt = DateOnly.FromDateTime(EasternClock.ToEastern(ctx.UtcNow));
        if (day > todayEt) return OperationResult.Usage($"{day:yyyy-MM-dd} is in the future — backtest a past day.");

        var feed = ctx.Get<IMarketDataFeed>();
        var source = feed is MockDataFeed ? "Mock (synthetic prices — rehearsal only)" : feed.FeedName;
        var startUtc = EtToUtc(day, new TimeOnly(4, 0));
        var endUtc = EtToUtc(day, new TimeOnly(16, 0));
        var wall = ctx.UtcNow.AddMinutes(-1);
        if (endUtc > wall) endUtc = wall;
        if (endUtc <= startUtc) return OperationResult.Usage($"{day:yyyy-MM-dd}'s premarket hasn't started yet — pick a past day.");

        var candles = new List<Candle>();
        await foreach (var c in feed.GetHistoricalCandlesAsync(symbol, startUtc, endUtc, TimeSpan.FromMinutes(1), ctx.CancellationToken))
            candles.Add(c);
        var previousClose = await feed.GetPreviousCloseAsync(symbol, startUtc.AddHours(2), ctx.CancellationToken);

        var report = GapperDayBacktester.Run(symbol, profile, candles, previousClose, day);
        var run = new GapperBacktestRun(symbol, day, profileName, source, profile, report);
        var id = await SavedRunStore.For(ctx).SaveAsync("gb", owner, run, ctx.UtcNow, ctx.CancellationToken);

        return OperationResult.Ok(new { Id = id, run.Symbol, Day = day.ToString("yyyy-MM-dd"), run.ProfileName, DataSource = source, Bars = candles.Count, previousClose, Report = report },
            Render(id, run, candles.Count, previousClose));
    }

    internal static string Render(string id, GapperBacktestRun run, int bars, decimal? previousClose)
    {
        var r = run.Report;
        var kv = new KeyValueBlock()
            .Add("run", id)
            .Add("replay", $"{run.Symbol} {run.Day:ddd yyyy-MM-dd} · {run.ProfileName} · {bars} bars from {run.DataSource}")
            .Add("previous close", previousClose is { } pc ? Fmt.Money(pc) : "—");
        if (!r.Entered)
        {
            kv.Add("entry", "none — " + r.NoEntryReason);
            return kv.Render() + Suggestions(r);
        }
        kv.Add("entry", $"{Fmt.EtTime(r.Entry!.Utc)} @ {Fmt.Money(r.Entry.Price)} ({r.Quantity} sh, gap {Fmt.PercentPoints((decimal)r.GapAtEntryPercent, 1, signed: true)})")
          .Add("exit", r.Exit is { } x ? $"{Fmt.EtTime(x.Utc)} @ {Fmt.Money(x.Price)} — {x.Reason}" : "—")
          .Add("P&L", $"{Fmt.SignedMoney((decimal)r.PnL)} ({Fmt.PercentPoints((decimal)r.ReturnPercent, 2, signed: true)})")
          .Add("MFE", $"peak {Fmt.Money(r.PeakPrice)} at {Fmt.EtTime(r.PeakUtc)} ({Fmt.PercentPoints((decimal)r.MaxFavorablePercent, 2, signed: true)})")
          .Add("MAE", $"trough {Fmt.Money(r.TroughPrice)} ({Fmt.PercentPoints((decimal)r.MaxAdversePercent, 2, signed: true)})");

        var grid = new TextTable().Column("giveback", Align.Right).Column("exit", Align.Right).Column("P&L", Align.Right).Column("why");
        foreach (var g in r.GivebackGrid)
            grid.Row(GapperRules.Num(g.GivebackPercent) + "%", Fmt.Money(g.ExitPrice), Fmt.SignedMoney((decimal)g.PnL), g.ExitReason);

        var text = kv.Render() + "\n\nGiveback grid (same day, same entry):\n" + grid.Render() + Suggestions(r);
        if (r.TunedProfile is { } t)
            text += $"\n\nHindsight-tuned: stop {GapperRules.Num(t.StopLossPercent)}%, giveback {GapperRules.Num(t.PeakGivebackPercent)}%" +
                    $" (was {GapperRules.Num(run.Profile.StopLossPercent)}%, {GapperRules.Num(run.Profile.PeakGivebackPercent)}%)." +
                    $"\nApply it: idiotproof gapper apply-tuned {id}";
        return text;
    }

    private static string Suggestions(GapperDayBacktestReport r) =>
        r.Suggestions.Count == 0 ? "" : "\n\nHindsight:\n" + string.Join("\n", r.Suggestions.Select(s => "  - " + s));

    internal static DateTime EtToUtc(DateOnly day, TimeOnly time) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day.ToDateTime(time), DateTimeKind.Unspecified), MarketTime.Eastern);
}
