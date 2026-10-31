using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Operations.Rendering;
using IdiotProof.Operations.Strategies;
using IdiotProof.Scripting;
using IdiotProof.Strategies.Backtesting;

using IdiotProof.Core.Monitoring;
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
        var to = args.To ?? from;
        var todayEt = DateOnly.FromDateTime(EasternClock.ToEastern(ctx.UtcNow));
        if (to < from) return OperationResult.Usage("--to is before --from.");
        if (to > todayEt) return OperationResult.Usage($"--to {to:yyyy-MM-dd} is in the future.");
        if (to.DayNumber - from.DayNumber + 1 > MaxDays) return OperationResult.Usage($"At most {MaxDays} calendar days per run.");

        var feed = ctx.Get<IMarketDataFeed>();
        var source = feed is MockDataFeed ? "Mock (synthetic prices — rehearsal only)" : $"{feed.FeedName} ({ReplayKit.FeedLabel(feed)})";
        var days = new List<BacktestDayRecord>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            ReplayDay loaded;
            try { loaded = await StrategyReplay.LoadDayAsync(feed, ReplayKit.FeedLabel(feed), def.Symbol, day, ctx.UtcNow, ctx.CancellationToken); }
            catch (ReplayException ex) { days.Add(new BacktestDayRecord(day, 0, ex.Message, 0, [], [], [])); continue; }

            var report = StrategyBacktester.Run(def, loaded.Candles, new BacktestOptions { DefaultQuantity = args.Qty, PreviousClose = loaded.PreviousClose });
            days.Add(new BacktestDayRecord(day, report.BarsProcessed, report.Note, report.Triggers.Count,
                report.Trades.Select(t => new BacktestTradeRecord(t.Cycle, t.Direction.ToString(), t.EntryUtc, t.EntryPrice, t.Quantity, t.StopPrice,
                    t.ExitUtc, t.Exits.Count > 0 ? t.Exits[^1].Price : null, t.PnL, t.ReturnPercent, t.ExitReason)).ToList(),
                report.ConditionTable.FirstOrDefault()?.Labels.ToList() ?? def.EntryConditions.Select(c => c.ToScript()).ToList(),
                report.ConditionTable.Select(r => new BacktestConditionRow(r.Utc, r.Open, r.High, r.Low, r.Close, r.Results.ToList(),
                    r.AllSatisfied, r.HasTriggerFire, r.OpenedTrade, r.InPosition)).ToList()));
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
}
