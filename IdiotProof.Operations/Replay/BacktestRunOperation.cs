using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Operations.Rendering;
using IdiotProof.Operations.Strategies;
using IdiotProof.Scripting;
using IdiotProof.Strategies.Backtesting;

using IdiotProof.Core.Evidence;
using IdiotProof.Core.Monitoring;
using IdiotProof.Core.Services;
using IdiotProof.Engine.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
namespace IdiotProof.Operations.Replay;

/// <summary>A saved <c>backtest run</c> (store kind <c>bt</c>): the per-day results and condition tables.</summary>
public sealed record BacktestRunRecord(string Strategy, string Symbol, DateOnly From, DateOnly To, string DataSource, int DefaultQuantity, List<BacktestDayRecord> Days);

/// <summary>One replayed day of a backtest.</summary>
public sealed record BacktestDayRecord(DateOnly Day, int Bars, string? Note, int Triggers, List<BacktestTradeRecord> Trades, List<string> Labels, List<BacktestConditionRow> Conditions);

/// <summary>One simulated trade of a backtest day.</summary>
public sealed record BacktestTradeRecord(int Cycle, string Direction, DateTime EntryUtc, decimal EntryPrice, int Quantity, decimal StopPrice,
    DateTime? ExitUtc, decimal? ExitPrice, decimal PnL, decimal ReturnPercent, string ExitReason);

/// <summary>One candle of a day's per-condition truth table.</summary>
public sealed record BacktestConditionRow(DateTime Utc, decimal Open, decimal High, decimal Low, decimal Close, List<bool> Results,
    bool AllSatisfied, bool HasTriggerFire, bool OpenedTrade, bool InPosition);

public sealed record BacktestRunArgs
{
    [Argument(0, Name = "strategy", Required = false, Description = "A saved strategy: id, id prefix, stamp id or symbol (or use --script).")]
    public string? Strategy { get; init; }

    [Option("script", Description = "Backtest this IdiotScript, canonical JSON or stamp instead of a saved strategy (`-` reads stdin).")]
    public string? Script { get; init; }

    [Option("from", Description = "First ET trading day (yyyy-MM-dd); default the last completed trading day.")]
    public DateOnly? From { get; init; }

    [Option("to", Description = "Last ET trading day (yyyy-MM-dd); default --from.")]
    public DateOnly? To { get; init; }

    [Option("qty", Description = "Share count when the strategy doesn't pin one (default 100).")]
    public int Qty { get; init; } = 100;

    [Option("days", Description = "Replay this many trading days from --from as ONE continuous run, so a sequenced strategy's hand-off between steps (and its Within deadline) crosses the day boundaries. Not with --to.")]
    public int? Days { get; init; }
}

/// <summary>
/// Backtests a DSL strategy day by day with <see cref="StrategyBacktester"/> over each ET trading
/// day's 4:00 AM-8:00 PM minute bars (with that day's previous close for gap math): trigger fires,
/// trades, P&amp;L per day and in total. The per-candle condition tables are saved under a run id
/// that <c>backtest conditions</c> reads back.
/// </summary>
[Operation("backtest run", Summary = "Backtest a saved strategy or a script over a range of ET days with StrategyBacktester: triggers, trades and P&L per day (saved for backtest conditions).", Risk = OperationRisk.Read)]
[Example("idiotproof backtest run NVDA --from 2026-10-26 --to 2026-10-30")]
[Example("idiotproof backtest run --script \"Ticker(\\\"INTC\\\").IsAboveVwap().StopLoss(2).TakeProfit(4)\" --from 2026-10-29")]
[Example("idiotproof backtest run NVDA --from 2026-10-20 --days 7", Description = "A two-step sequence replayed as one run across seven trading days.")]
public sealed class BacktestRunOperation : IOperation<BacktestRunArgs>
{
    /// <summary>The longest range one call replays.</summary>
    public const int MaxDays = 31;

    public async Task<OperationResult> ExecuteAsync(BacktestRunArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        if (args.Qty is < 1 or > 1_000_000) return OperationResult.Usage("--qty must be 1-1,000,000.");
        StrategyDefinition def;
        string name;
        if (!string.IsNullOrWhiteSpace(args.Script))
        {
            if (!string.IsNullOrWhiteSpace(args.Strategy)) return OperationResult.Usage("Pass a saved strategy or --script, not both.");
            def = StrategyImportOperation.Parse(args.Script).Definition;
            name = def.Name ?? $"{def.Symbol} (script {def.ShortId()})";
        }
        else if (!string.IsNullOrWhiteSpace(args.Strategy))
        {
            var row = await StrategyRefs.ResolveAsync(ctx, args.Strategy);
            def = StrategyRefs.RequireDefinition(row);
            name = row.Title;
        }
        else return OperationResult.Usage("Name a strategy (id, prefix, stamp id or symbol) or pass --script.");

        var from = args.From ?? MarketTime.PreviousEquityTradingDayEt(ctx.UtcNow);
        var todayEt = DateOnly.FromDateTime(EasternClock.ToEastern(ctx.UtcNow));
        IReadOnlyList<DateOnly>? continuous = null;
        if (args.Days is { } runDays)
        {
            if (args.To is not null) return OperationResult.Usage("Pass --days or --to, not both.");
            if (runDays is < 1 or > MaxDays) return OperationResult.Usage($"--days must be 1-{MaxDays}.");
            continuous = BacktestDayRunner.TradingDays(from, runDays);
        }
        var to = continuous is not null ? continuous[^1] : args.To ?? from;
        if (to < from) return OperationResult.Usage("--to is before --from.");
        if (to > todayEt) return OperationResult.Usage($"--to {to:yyyy-MM-dd} is in the future.");
        if (to.DayNumber - from.DayNumber + 1 > MaxDays) return OperationResult.Usage($"At most {MaxDays} calendar days per run.");

        var feed = ctx.Get<IMarketDataFeed>();
        var source = feed is MockDataFeed ? "Mock (synthetic prices — rehearsal only)" : $"{feed.FeedName} ({ReplayKit.FeedLabel(feed)})";
        var days = new List<BacktestDayRecord>();
        LeaderContextCache? leaderCache = null;
        IReadOnlyList<string> peers = [];
        if (LeaderContextCache.Uses(def))
        {
            var settings = ctx.Get<AppSettings>();
            leaderCache = new LeaderContextCache(feed, ctx.Services.GetService<IHistoricalBarStore>(), () => settings.LeaderEtfs, NullLogger.Instance);
            peers = (await ctx.Get<StrategyRepository>().GetActiveAsync(ctx.CancellationToken)).Select(s => s.Symbol).ToList();
        }
        if (continuous is not null)
        {
            // One run over every day's bars, so a step hand-off that happens on a later day replays.
            var candles = new List<Candle>();
            decimal? previousClose = null;
            var skipped = new List<string>();
            foreach (var day in continuous)
            {
                try
                {
                    var loaded = await StrategyReplay.LoadDayAsync(feed, ReplayKit.FeedLabel(feed), def.Symbol, day, ctx.UtcNow, ctx.CancellationToken);
                    if (candles.Count == 0) previousClose = loaded.PreviousClose;
                    candles.AddRange(loaded.Candles);
                }
                catch (ReplayException ex) { skipped.Add($"{day:yyyy-MM-dd}: {ex.Message}"); }
            }
            var (leaders, leaderNote) = await BacktestDayRunner.LeadersForDayAsync(def, continuous[0], feed, leaderCache, peers, ctx.CancellationToken);
            var report = StrategyBacktester.Run(def, candles, new BacktestOptions { DefaultQuantity = args.Qty, PreviousClose = previousClose, Leaders = leaders });
            var notes = new List<string> { $"{continuous.Count} trading days {from:yyyy-MM-dd} → {to:yyyy-MM-dd} replayed as one run." };
            if (report.StepTransitions.Count > 0)
                notes.Add("Step transitions: " + string.Join(", ", report.StepTransitions.Select(t => $"{t.FromStep + 1}→{t.ToStep + 1} at {Fmt.Et(t.Utc)}")) + ".");
            if (report.Note is not null) notes.Add(report.Note);
            if (leaderNote is not null) notes.Add(leaderNote);
            notes.AddRange(skipped);
            days.Add(Record(from, report, string.Join(" ", notes), def));
        }
        else
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            ReplayDay loaded;
            try { loaded = await StrategyReplay.LoadDayAsync(feed, ReplayKit.FeedLabel(feed), def.Symbol, day, ctx.UtcNow, ctx.CancellationToken); }
            catch (ReplayException ex) { days.Add(new BacktestDayRecord(day, 0, ex.Message, 0, [], [], [])); continue; }

            var (leaders, leaderNote) = await BacktestDayRunner.LeadersForDayAsync(def, day, feed, leaderCache, peers, ctx.CancellationToken);
            var report = StrategyBacktester.Run(def, loaded.Candles, new BacktestOptions { DefaultQuantity = args.Qty, PreviousClose = loaded.PreviousClose, Leaders = leaders });
            if (leaderNote is not null) report.Note = report.Note is null ? leaderNote : $"{report.Note} {leaderNote}";
            days.Add(Record(day, report, report.Note, def));
        }
        if (days.Count == 0) return OperationResult.Usage("That range has no weekdays.");

        var record = new BacktestRunRecord(name, def.Symbol, from, to, source, args.Qty, days);
        var id = await SavedRunStore.For(ctx).SaveAsync("bt", owner, record, ctx.UtcNow, ctx.CancellationToken);

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("day").Column("bars", Align.Right).Column("triggers", Align.Right).Column("trades", Align.Right).Column("won", Align.Right)
            .Column("P&L", Align.Right).Column("note", maxWidth: 40);
        foreach (var d in days)
            table.Row(d.Day.ToString("ddd yyyy-MM-dd"), d.Bars.ToString(), d.Triggers.ToString(), d.Trades.Count.ToString(),
                d.Trades.Count(t => t.PnL > 0).ToString(), d.Trades.Count == 0 ? "" : Fmt.SignedMoney(d.Trades.Sum(t => t.PnL)), d.Note ?? "");
        var all = days.SelectMany(d => d.Trades).ToList();
        var total = all.Sum(t => t.PnL);
        var text = $"{def.Symbol} \"{name}\" · {from:yyyy-MM-dd} → {to:yyyy-MM-dd} · {source}\n" + table.Render() +
                   $"\n{all.Count} trade(s), {all.Count(t => t.PnL > 0)} won, total {Fmt.SignedMoney(total)}." +
                   $"\nRun {id}: `idiotproof backtest conditions {id}` shows each candle's condition truth.";
        return OperationResult.Ok(new
        {
            Id = id, Strategy = name, def.Symbol, From = from.ToString("yyyy-MM-dd"), To = to.ToString("yyyy-MM-dd"), DataSource = source,
            Days = days.Select(d => new { Day = d.Day.ToString("yyyy-MM-dd"), d.Bars, d.Note, d.Triggers, d.Trades }).ToList(),
            TradeCount = all.Count, Wins = all.Count(t => t.PnL > 0), TotalPnL = total,
        }, text);
    }

    private static BacktestDayRecord Record(DateOnly day, BacktestReport report, string? note, StrategyDefinition def) =>
        new(day, report.BarsProcessed, note, report.Triggers.Count,
            report.Trades.Select(t => new BacktestTradeRecord(t.Cycle, t.Direction.ToString(), t.EntryUtc, t.EntryPrice, t.Quantity, t.StopPrice,
                t.ExitUtc, t.Exits.Count > 0 ? t.Exits[^1].Price : null, t.PnL, t.ReturnPercent, t.ExitReason)).ToList(),
            report.ConditionTable.FirstOrDefault()?.Labels.ToList() ?? def.EntryConditions.Select(c => c.ToScript()).ToList(),
            report.ConditionTable.Select(r => new BacktestConditionRow(r.Utc, r.Open, r.High, r.Low, r.Close, r.Results.ToList(),
                r.AllSatisfied, r.HasTriggerFire, r.OpenedTrade, r.InPosition)).ToList());
}
