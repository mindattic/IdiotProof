using IdiotProof.Models;

namespace IdiotProof.Strategies;

/// <summary>
/// Simple buy-low/sell-high strategy. Buys when price drops from recent high
/// by a threshold, sells at profit target. Ported from Ahab.
/// </summary>
public sealed class LowHighStrategy : IStrategy
{
    public string Name => "LowHigh";
    public string Description => "Buy on dip from recent high, sell at profit target";
    public StrategyType Type => StrategyType.LowHigh;

    public IReadOnlyList<TradeSignal> Evaluate(string symbol, IReadOnlyList<Candle> candles, StrategyContext context)
    {
        if (candles.Count < 5) return [];

        var buyDropPercent = context.Parameters.TryGetValue("BuyDropPercent", out var bdp) && bdp is decimal d ? d : 0.01m;
        var sellProfitPercent = context.Parameters.TryGetValue("SellProfitPercent", out var spp) && spp is decimal s ? s : 0.02m;

        var signals = new List<TradeSignal>();

        // Find the session high
        decimal sessionHigh = candles.Max(c => c.High);
        var lastCandle = candles[^1];

        // Check for buy signal: price dropped from high by threshold
        var dropThreshold = sessionHigh * (1 - buyDropPercent);
        if (lastCandle.Close <= dropThreshold)
        {
            var target = lastCandle.Close * (1 + sellProfitPercent);
            signals.Add(new TradeSignal
            {
                Symbol = symbol,
                Direction = TradeDirection.Long,
                ConfidencePercent = 60m,
                SuggestedEntry = lastCandle.Close,
                SuggestedStop = lastCandle.Close * (1 - buyDropPercent),
                Targets = [target],
                Reason = $"Price dropped {buyDropPercent:P0} from session high {sessionHigh:F2}",
                StrategyName = Name
            });
        }

        return signals;
    }
}
