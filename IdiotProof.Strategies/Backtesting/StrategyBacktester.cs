using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Backtesting;

/// <summary>
/// Replays a DSL <see cref="StrategyDefinition"/> over a window of candles bar-by-bar,
/// recording when each entry condition fires and simulating the resulting trades (entry,
/// scale-out targets, stop, time/EOD exit) so a user can run "yesterday" and confirm the
/// triggers went off as designed and see the P&amp;L.
///
/// PURE: candles in, <see cref="BacktestReport"/> out. No I/O, no data-feed dependency —
/// the caller fetches the day's candles (Alpaca/Mock) and passes them in. That keeps the
/// engine deterministic and unit-testable.
///
/// Trigger semantics (documented so the timeline is interpretable):
///   • Breakout(level)  — latches true the first bar whose High ≥ level (price broke out).
///                        Stays latched for the rest of the cycle.
///   • Pullback(support)— only considered after a Breakout has latched; latches true the
///                        first subsequent bar whose Low ≤ support (the retest). If no
///                        support level was given, latches on the first bar that closes
///                        below the breakout bar's high (any retracement).
///   • Everything else (VWAP/EMA/ADX/RSI/HoldsAbove/…) is evaluated against the per-bar
///     IndicatorSnapshot, exactly as the live <see cref="DslStrategy"/> does. Its first
///     true bar is recorded as the fire time; it must be true on the entry bar to count.
///
/// A position opens when, on the same bar, EVERY entry condition is satisfied
/// (pattern latches + instantaneous conditions). Exit order within a bar is stop-first
/// (worst case), then targets, then the optional time exit. Leftover shares close at the
/// last bar. With .Repeat() the cycle resets and the strategy can re-enter.
/// </summary>
public static class StrategyBacktester
{
    private static readonly int[] DefaultEmaPeriods = [9, 21, 31, 50, 200];

    public static BacktestReport Run(
        StrategyDefinition definition,
        IReadOnlyList<Candle> candles,
        BacktestOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(candles);
        options ??= new BacktestOptions();

        var symbol = definition.Symbol;
        var ordered = candles.Where(c => c is not null)
                             .OrderBy(c => c.StartUtc)
                             .ToList();

        var report = new BacktestReport
        {
            Symbol        = symbol,
            StrategyName  = string.IsNullOrWhiteSpace(definition.Name) ? symbol : definition.Name!,
            Date          = ordered.Count > 0 ? DateOnly.FromDateTime(ordered[0].StartUtc) : default,
            BarsProcessed = ordered.Count,
            FirstBarUtc   = ordered.Count > 0 ? ordered[0].StartUtc : null,
            LastBarUtc    = ordered.Count > 0 ? ordered[^1].StartUtc : null,
            HasBranching  = definition.HasBranching,
        };

        // Every condition any branch could add, tracked every bar so a pattern latch
        // (Breakout/Pullback) keeps its state when the active branch flips.
        var allConditions = CollectAllEntryConditions(definition);

        if (ordered.Count < 2 || allConditions.Count == 0)
        {
            if (allConditions.Count == 0)
                report.Note = "Strategy has no entry conditions to evaluate.";
            else
                // Without this note, a weekend/holiday (or feed outage) day
                // rendered as "no entry condition fired — the strategy would
                // have stayed flat", which is a false claim about a day that
                // was never replayed at all.
                report.Note = "No market data for this day — weekend, market holiday, or the data feed returned nothing. Nothing was replayed.";
            return report;
        }

        var emaPeriods = CollectEmaPeriods(definition);
        var allTriggers = BuildTriggers(allConditions);
        var triggerFor = new Dictionary<ICondition, TrackedTrigger>(ReferenceEqualityComparer.Instance);
        for (int k = 0; k < allConditions.Count; k++) triggerFor[allConditions[k]] = allTriggers[k];

        // Branches resolve per bar, like the Monitor; an open trade keeps the branch it entered on
        // (the Monitor's ResolvedEntryScriptJson), so its stop/targets don't flip mid-trade.
        var entryDef = definition;
        var isLong = definition.Direction != TradeDirection.Short;
        var triggers = allTriggers;

        BacktestTrade? open = null;
        int remaining = 0;
        int nextTarget = 0;
        List<(decimal Price, int Pct, string Label)> targets = [];
        decimal stop = 0m;
        int cycle = 1;
        bool blockReentry = false;

        for (int i = 0; i < ordered.Count; i++)
        {
            var bar = ordered[i];

            // Snapshot "as of" this bar — same builder the live path uses,
            // including the previous close so gap conditions can evaluate.
            var window = ordered.Take(i + 1).ToList();
            var snapshot = IndicatorSnapshotBuilder.BuildWithEmas(symbol, window, emaPeriods, options.PreviousClose);

            var active = StrategyBranchResolver.Resolve(definition, snapshot);
            var branchState = ReadBranches(definition, snapshot);
            triggers = active.EntryConditions.Select(c => triggerFor[c]).ToList();

            // 1) Update every trigger's satisfied state for this bar.
            foreach (var t in allTriggers) t.Update(bar, snapshot, allTriggers);

            bool allSatisfied = true;
            bool hadFireThisBar = false;
            foreach (var t in triggers)
            {
                if (t.TryReportFire())
                {
                    hadFireThisBar = true;
                    report.Triggers.Add(new TriggerFire
                    {
                        Utc       = bar.StartUtc,
                        Condition = t.Label,
                        Price     = bar.Close,
                        Cycle     = cycle,
                    });
                }
                if (!t.Satisfied) allSatisfied = false;
            }

            bool wasInPosition = open is not null;
            bool openedTradeThisBar = false;

            // 2) Manage an open position first (a bar can both manage and, after a
            //    repeat-reset, not re-enter until the next bar).
            if (open is not null)
            {
                ManageOpenBar(open, bar, isLong, ref remaining, ref nextTarget, targets, stop, entryDef);
                if (remaining > 0 && definition.ExitWhenWhileFails && !definition.WhileHolds(snapshot))
                {
                    open.Exits.Add(new BacktestFill
                    {
                        Utc = bar.StartUtc, Price = bar.Close, Shares = remaining, Reason = "While condition failed",
                    });
                    remaining = 0;
                }
                if (remaining == 0)
                {
                    FinalizeTrade(open, isLong);
                    report.Trades.Add(open);
                    open = null;

                    if (definition.ShouldRepeat && (definition.MaxCycles is not { } maxCycles || cycle < maxCycles))
                    {
                        cycle++;
                        foreach (var t in allTriggers) t.Reset();
                    }
                    else
                    {
                        blockReentry = true;
                    }
                }
                AppendConditionRow(report, bar, triggers, allSatisfied, hadFireThisBar, openedTradeThisBar, wasInPosition, branchState);
                continue; // never enter and manage on the same bar
            }

            // 3) Flat: open a position if the full setup is satisfied on this bar.
            // A While() guard pauses entries while false; it never triggers an entry by itself.
            if (!blockReentry && allSatisfied && definition.WhileHolds(snapshot))
            {
                var entryPrice = bar.Close;
                var qty = ResolveQuantity(active, entryPrice, options);
                if (qty <= 0)
                {
                    AppendConditionRow(report, bar, triggers, allSatisfied, hadFireThisBar, false, false, branchState);
                    continue;
                }

                entryDef = active;
                isLong = active.Direction != TradeDirection.Short;
                stop = ResolveStop(active, entryPrice, isLong);
                targets = ResolveTargets(active, isLong);
                nextTarget = 0;
                remaining = qty;
                openedTradeThisBar = true;

                open = new BacktestTrade
                {
                    Cycle     = cycle,
                    Direction = active.Direction,
                    EntryUtc  = bar.StartUtc,
                    EntryPrice = entryPrice,
                    Quantity  = qty,
                    StopPrice = stop,
                    PeakSinceEntry = entryPrice,
                };

                // Mark the trigger fires on this bar as the ones that opened the trade.
                foreach (var fire in report.Triggers.Where(f => f.Cycle == cycle && f.Utc == bar.StartUtc).ToList())
                {
                    var idx = report.Triggers.IndexOf(fire);
                    report.Triggers[idx] = new TriggerFire
                    {
                        Utc = fire.Utc, Condition = fire.Condition, Price = fire.Price,
                        Cycle = fire.Cycle, OpenedPosition = true,
                    };
                }
            }

            AppendConditionRow(report, bar, triggers, allSatisfied, hadFireThisBar, openedTradeThisBar, wasInPosition, branchState);
        }

        // Close anything still open at the last bar.
        if (open is not null && remaining > 0)
        {
            var last = ordered[^1];
            open.Exits.Add(new BacktestFill
            {
                Utc = last.StartUtc, Price = last.Close, Shares = remaining, Reason = "End of session",
            });
            remaining = 0;
            FinalizeTrade(open, isLong);
            report.Trades.Add(open);
        }

        return report;
    }

    private static void AppendConditionRow(
        BacktestReport report, Candle bar,
        List<TrackedTrigger> triggers, bool allSatisfied, bool hadFire, bool openedTrade, bool inPosition,
        BranchState branches)
    {
        report.ConditionTable.Add(new CandleConditionRow
        {
            Utc            = bar.StartUtc,
            Open           = bar.Open,
            High           = bar.High,
            Low            = bar.Low,
            Close          = bar.Close,
            Labels         = triggers.Select(t => t.Label).ToList(),
            Results        = triggers.Select(t => t.Satisfied).ToList(),
            AllSatisfied   = allSatisfied,
            HasTriggerFire = hadFire,
            OpenedTrade    = openedTrade,
            InPosition     = inPosition,
            BranchTaken    = branches.Taken,
            BranchConditionResults = branches.Results,
            Indicators     = branches.Indicators,
        });
    }

    private sealed record BranchState(
        IReadOnlyList<int> Taken, IReadOnlyList<IReadOnlyList<bool>> Results, IReadOnlyDictionary<string, double> Indicators);

    private static BranchState ReadBranches(StrategyDefinition def, Shared.IndicatorSnapshot snapshot)
    {
        var taken = new List<int>(def.ConditionalBlocks.Count);
        var results = new List<IReadOnlyList<bool>>(def.ConditionalBlocks.Count);
        foreach (var block in def.ConditionalBlocks)
        {
            var row = block.Branches.Select(b => b.Condition is null || b.Condition.Evaluate(snapshot)).ToList();
            results.Add(row);
            taken.Add(row.IndexOf(true));
        }

        var ind = new Dictionary<string, double> { ["Price"] = snapshot.Price, ["Volume"] = snapshot.Volume };
        void Put(string name, double? v) { if (v is { } x && !double.IsNaN(x)) ind[name] = x; }
        Put("VWAP", snapshot.Vwap);
        Put("EMA9", snapshot.Ema9);
        Put("EMA21", snapshot.Ema21);
        Put("RSI", snapshot.Rsi);
        Put("ATR", snapshot.Atr);
        return new BranchState(taken, results, ind);
    }

    // ---- exit handling for one bar ----

    private static void ManageOpenBar(
        BacktestTrade trade, Candle bar, bool isLong,
        ref int remaining, ref int nextTarget,
        List<(decimal Price, int Pct, string Label)> targets, decimal stop,
        StrategyDefinition def)
    {
        // High-water mark since entry — feeds the trailing-stop and
        // peak-giveback exits below. Long-shaped, like the live exit brain.
        if (isLong && bar.High > trade.PeakSinceEntry) trade.PeakSinceEntry = bar.High;

        // Stop first (conservative): assume the adverse extreme traded through the stop.
        bool stopHit = stop > 0m && (isLong ? bar.Low <= stop : bar.High >= stop);
        if (stopHit)
        {
            trade.Exits.Add(new BacktestFill
            {
                Utc = bar.StartUtc, Price = stop, Shares = remaining, Reason = "Stop loss",
            });
            remaining = 0;
            return;
        }

        // Trailing stop off the peak (Risk phase). The replay used to ignore
        // TrailingStopLoss entirely, silently holding through pullbacks the
        // live evaluator would have sold — backtest ≠ live divergence.
        if (isLong && def.TrailingStopPercent is { } tslPct && trade.PeakSinceEntry > trade.EntryPrice)
        {
            var trailFloor = trade.PeakSinceEntry * (1m - (decimal)(tslPct / 100.0));
            if (bar.Low <= trailFloor)
            {
                trade.Exits.Add(new BacktestFill
                {
                    Utc = bar.StartUtc, Price = trailFloor, Shares = remaining, Reason = "Trailing stop",
                });
                remaining = 0;
                return;
            }
        }

        // Scale-out targets, in order.
        while (nextTarget < targets.Count && remaining > 0)
        {
            var tgt = targets[nextTarget];
            bool hit = isLong ? bar.High >= tgt.Price : bar.Low <= tgt.Price;
            if (!hit) break;

            int shares = nextTarget == targets.Count - 1
                ? remaining // last target takes whatever is left (covers rounding)
                : Math.Min(remaining, (int)Math.Round(trade.Quantity * (tgt.Pct / 100.0)));
            if (shares <= 0) shares = remaining;

            trade.Exits.Add(new BacktestFill
            {
                Utc = bar.StartUtc, Price = tgt.Price, Shares = shares, Reason = tgt.Label,
            });
            remaining -= shares;
            nextTarget++;
        }
        if (remaining == 0) return;

        // Peak-giveback momentum rollover (the flagship gapper exit) — also
        // previously ignored by this replay. Close-based, mirroring the live
        // GapperExitEvaluator: sell once the close gives back N% of the
        // entry→peak run, armed from the configured ET time (or always).
        if (isLong && def.PeakGivebackPercent is { } giveback && trade.PeakSinceEntry > trade.EntryPrice)
        {
            var armed = def.PeakGivebackArmTime is not { } arm
                || Scripting.MarketTime.ToEasternTimeOfDay(bar.StartUtc) >= arm;
            var run = trade.PeakSinceEntry - trade.EntryPrice;
            var gbFloor = trade.PeakSinceEntry - run * (decimal)(giveback / 100.0);
            if (armed && bar.Close <= gbFloor)
            {
                trade.Exits.Add(new BacktestFill
                {
                    Utc = bar.StartUtc, Price = bar.Close, Shares = remaining, Reason = "Peak giveback",
                });
                remaining = 0;
                return;
            }
        }

        // Optional time exit. ExitTime is an ET (market-clock) time-of-day —
        // SellBy("09:28") means 9:28 Eastern in the DSL, the Monitor, and
        // GapperExitEvaluator alike. Comparing against the bar's raw UTC
        // time-of-day fired the exit at 09:28 UTC (= 05:28 ET premarket!),
        // silently mis-simulating every strategy that uses a time exit.
        if (def.ExitTime.HasValue && Scripting.MarketTime.ToEasternTimeOfDay(bar.StartUtc) >= def.ExitTime.Value)
        {
            trade.Exits.Add(new BacktestFill
            {
                Utc = bar.StartUtc, Price = bar.Close, Shares = remaining, Reason = "Time exit",
            });
            remaining = 0;
        }
    }

    private static void FinalizeTrade(BacktestTrade trade, bool isLong)
    {
        decimal pnl = 0m;
        foreach (var f in trade.Exits)
            pnl += (isLong ? f.Price - trade.EntryPrice : trade.EntryPrice - f.Price) * f.Shares;

        trade.PnL = pnl;
        var cost = trade.EntryPrice * trade.Quantity;
        trade.ReturnPercent = cost > 0m ? pnl / cost * 100m : 0m;
        trade.ExitReason = trade.Exits.Count > 0 ? trade.Exits[^1].Reason : "";
    }

    // ---- setup resolution ----

    private static int ResolveQuantity(StrategyDefinition def, decimal entryPrice, BacktestOptions options)
    {
        if (def.IsNotional && def.NotionalAmount is { } notional && entryPrice > 0m)
            return (int)Math.Floor(notional / entryPrice);
        return def.Quantity > 0 ? def.Quantity : options.DefaultQuantity;
    }

    private static decimal ResolveStop(StrategyDefinition def, decimal entryPrice, bool isLong)
    {
        if (def.StopLossPrice is { } sp) return (decimal)sp;
        if (def.StopLossPercent is { } pct)
        {
            var dist = entryPrice * (decimal)(pct / 100.0);
            return isLong ? entryPrice - dist : entryPrice + dist;
        }
        return 0m; // no stop configured
    }

    private static List<(decimal Price, int Pct, string Label)> ResolveTargets(StrategyDefinition def, bool isLong)
    {
        var list = new List<(decimal Price, int Pct, string Label)>();
        if (def.TakeProfitTargets.Count > 0)
        {
            foreach (var t in def.TakeProfitTargets)
                list.Add(((decimal)t.Price, t.PercentToSell, t.Label));
        }
        else if (def.TakeProfitPrice is { } tp)
        {
            list.Add(((decimal)tp, 100, "Target"));
        }

        // Hit nearest target first: ascending for long, descending for short.
        return isLong
            ? list.OrderBy(t => t.Price).ToList()
            : list.OrderByDescending(t => t.Price).ToList();
    }

    private static int[] CollectEmaPeriods(StrategyDefinition def)
    {
        // Canonical walk (EmaPeriodCollector) covers ConditionalBlock branches
        // the old local copy missed; the default set keeps common chart EMAs
        // available for ad-hoc inspection of backtest snapshots.
        var periods = new HashSet<int>(DefaultEmaPeriods);
        periods.UnionWith(EmaPeriodCollector.Collect(def));
        return periods.OrderBy(x => x).ToArray();
    }

    private static List<ICondition> CollectAllEntryConditions(StrategyDefinition def)
    {
        var seen = new HashSet<ICondition>(ReferenceEqualityComparer.Instance);
        var list = new List<ICondition>();
        foreach (var c in def.EntryConditions) if (seen.Add(c)) list.Add(c);
        foreach (var block in def.ConditionalBlocks)
            foreach (var branch in block.Branches)
                foreach (var c in branch.Overrides.EntryConditions)
                    if (seen.Add(c)) list.Add(c);
        return list;
    }

    private static List<TrackedTrigger> BuildTriggers(IReadOnlyList<ICondition> conditions)
        => conditions.Select(c => new TrackedTrigger(c)).ToList();

    /// <summary>
    /// Per-condition state during a replay. Pattern conditions (Breakout/Pullback) latch;
    /// everything else is re-evaluated each bar against the snapshot.
    /// </summary>
    private sealed class TrackedTrigger
    {
        private readonly ICondition condition;
        private readonly PatternType? pattern;
        private readonly double? level;

        public string Label { get; }
        public bool Satisfied { get; private set; }
        public bool Latched { get; private set; }          // pattern: stays true once set
        public bool IsBreakout => pattern == PatternType.Breakout;
        public bool IsPullback => pattern == PatternType.Pullback;
        public decimal BreakoutHigh { get; private set; }
        private bool everFired;

        public TrackedTrigger(ICondition c)
        {
            condition = c;
            Label = c.ToScript();
            if (c is PatternCondition pc && pc.Type is PatternType.Breakout or PatternType.Pullback)
            {
                pattern = pc.Type;
                level = pc.Level;
            }
        }

        public void Update(Candle bar, Shared.IndicatorSnapshot snapshot, List<TrackedTrigger> all)
        {
            if (IsBreakout)
            {
                if (!Latched && level is { } lvl && bar.High >= (decimal)lvl)
                {
                    Latched = true;
                    BreakoutHigh = bar.High;
                }
                Satisfied = Latched;
            }
            else if (IsPullback)
            {
                var breakout = all.FirstOrDefault(t => t.IsBreakout);
                bool brokeOut = breakout?.Latched ?? false;
                if (!Latched && brokeOut)
                {
                    decimal retest = level is { } lvl ? (decimal)lvl : (breakout?.BreakoutHigh ?? bar.High);
                    if (bar.Low <= retest)
                        Latched = true;
                }
                Satisfied = Latched;
            }
            else
            {
                Satisfied = condition.Evaluate(snapshot);
            }
        }

        /// <summary>True the first time this cycle the condition is seen satisfied while it is part of the active branch.</summary>
        public bool TryReportFire()
        {
            if (!Satisfied || everFired) return false;
            everFired = true;
            return true;
        }

        public void Reset()
        {
            Satisfied = false;
            Latched = false;
            everFired = false;
            BreakoutHigh = 0m;
        }
    }
}
