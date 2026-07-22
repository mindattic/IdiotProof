using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies;

/// <summary>
/// Adapter that wraps a fluent-DSL <see cref="StrategyDefinition"/> as an
/// <see cref="IStrategy"/> so any evaluator (the IdiotProof.Monitor console,
/// the backtester) can evaluate user-authored strategies uniformly.
///
/// Evaluation semantics:
///   1. Build an IndicatorSnapshot from the supplied candles.
///   2. Walk every EntryCondition and evaluate with AND semantics — all must
///      pass for the strategy to fire. The DSL's branching syntax
///      (.If/.Then/.ElseIf/.Else) is encoded as ConditionalBlocks; each
///      block contributes the FIRST matching branch's overrides on top of
///      the base strategy.
///   3. If everything passes, emit a <see cref="TradeSignal"/> carrying the
///      direction, suggested entry, stop, and target from the StrategyDefinition.
///
/// Why a separate adapter (instead of teaching the Monitor about
/// StrategyDefinition directly): keeps IStrategy as the single integration
/// point. Anything that implements IStrategy — built-in C# strategies, DSL
/// strategies, future ML strategies — flows through the same registry / loop
/// / RiskGuardian / LLM-voting gate.
/// </summary>
public sealed class DslStrategy : IStrategy
{
    private readonly StrategyDefinition baseDefinition;

    public DslStrategy(StrategyDefinition definition)
    {
        baseDefinition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public string Name => string.IsNullOrWhiteSpace(baseDefinition.Name)
        ? $"DSL_{baseDefinition.Id:N}"
        : baseDefinition.Name;

    public string Description => $"User-authored DSL strategy on {baseDefinition.Symbol}";

    public StrategyType Type => StrategyType.FluentDsl;

    public IReadOnlyList<TradeSignal> Evaluate(string symbol, IReadOnlyList<Candle> candles, StrategyContext context)
    {
        // Symbol mismatch — DSL strategies are pinned to a symbol at authoring time.
        if (!string.Equals(baseDefinition.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
            return [];

        if (candles.Count == 0)
            return [];

        // Compute every EMA period this strategy actually references so the
        // snapshot has them populated. A strategy that says IsBetweenEma(7, 65)
        // would otherwise see null EMAs at those periods.
        var requiredEmas = EmaPeriodCollector.Collect(baseDefinition);
        var snapshot = IndicatorSnapshotBuilder.BuildWithEmas(symbol, candles, requiredEmas, context.PreviousClose);

        // Materialize a working copy with branch overrides applied. The branches
        // can flip Direction, change Stop/Target, and add EntryConditions.
        var resolved = ResolveBranches(baseDefinition, snapshot);

        // Evaluate every entry condition with AND semantics.
        foreach (var cond in resolved.EntryConditions)
        {
            if (!cond.Evaluate(snapshot))
                return [];
        }

        // All conditions passed — emit a signal.
        return [new TradeSignal
        {
            Symbol            = symbol,
            Direction         = resolved.Direction,
            ConfidencePercent = 0m,                       // unscored at the DSL layer; LLM voting fills this in
            SuggestedEntry    = (decimal)snapshot.Price,
            SuggestedStop     = resolved.StopLossPrice.HasValue
                                    ? (decimal)resolved.StopLossPrice.Value
                                    : resolved.StopLossPercent.HasValue
                                        ? (decimal)(snapshot.Price * (1.0 - resolved.StopLossPercent.Value / 100.0))
                                        : 0m,
            // Full scale-out ladder, not just T1 — TakeProfit(t1, t2, t3) sets
            // TakeProfitPrice = t1 AND populates TakeProfitTargets; emitting
            // only TakeProfitPrice silently dropped T2/T3 in the live path
            // while the backtester honored them (backtest ≠ live divergence).
            Targets           = resolved.TakeProfitTargets.Count > 0
                                 ? resolved.TakeProfitTargets.Select(t => (decimal)t.Price).ToList()
                                 : resolved.TakeProfitPrice.HasValue
                                     ? [(decimal)resolved.TakeProfitPrice.Value]
                                     : [],
            StrategyName      = Name,
            Reason            = BuildReason(resolved),
            GeneratedUtc      = snapshot.Timestamp,
        }];
    }

    /// <summary>
    /// Walks the strategy's ConditionalBlocks, picks the first matching branch
    /// per block, and clones the base definition with overrides applied.
    /// Leaves the original immutable.
    /// </summary>
    private static StrategyDefinition ResolveBranches(StrategyDefinition baseDef, Shared.IndicatorSnapshot snapshot)
    {
        if (baseDef.ConditionalBlocks.Count == 0)
            return baseDef;

        var clone = new StrategyDefinition
        {
            Id           = baseDef.Id,
            Symbol       = baseDef.Symbol,
            Name         = baseDef.Name,
            Session      = baseDef.Session,
            Quantity     = baseDef.Quantity,
            // Notional sizing and the peak-giveback exit fields were missing
            // from this clone — a branching strategy silently lost its dollar
            // sizing (fell back to "1 share"/workspace default) and its
            // momentum-rollover exit the moment it had a ConditionalBlock.
            NotionalAmount = baseDef.NotionalAmount,
            Direction    = baseDef.Direction,
            TakeProfitPrice    = baseDef.TakeProfitPrice,
            TakeProfitPercent  = baseDef.TakeProfitPercent,
            StopLossPrice      = baseDef.StopLossPrice,
            StopLossPercent    = baseDef.StopLossPercent,
            TrailingStopPercent = baseDef.TrailingStopPercent,
            ExitTime     = baseDef.ExitTime,
            PeakGivebackPercent = baseDef.PeakGivebackPercent,
            PeakGivebackArmTime = baseDef.PeakGivebackArmTime,
            IsAutonomous = baseDef.IsAutonomous,
            IsAdaptive   = baseDef.IsAdaptive,
            ShouldRepeat = baseDef.ShouldRepeat,
        };
        foreach (var c in baseDef.EntryConditions) clone.EntryConditions.Add(c);
        foreach (var t in baseDef.TakeProfitTargets) clone.TakeProfitTargets.Add(t);

        foreach (var block in baseDef.ConditionalBlocks)
        {
            var matched = block.Evaluate(snapshot);
            matched?.Overrides.ApplyTo(clone);
        }

        return clone;
    }

    private static string BuildReason(StrategyDefinition def)
    {
        if (def.EntryConditions.Count == 0) return "Conditions met";
        var conds = def.EntryConditions.Take(3).Select(c => c.ToScript());
        return $"Triggered by: {string.Join(" AND ", conds)}";
    }
}
