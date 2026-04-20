using IdiotProof.Indicators;
using IdiotProof.Models;

namespace IdiotProof.Strategies;

/// <summary>
/// Detects premarket gap moves, enters on consolidation above premarket high after open,
/// exits at ATR-based target or on momentum loss.
/// </summary>
public sealed class PremarketBreakoutStrategy : IStrategy
{
    public string Name => "PremarketBreakout";
    public string Description => "Detects premarket gap moves, enters on consolidation above premarket high after open, exits at ATR-based target or on momentum loss";
    public StrategyType Type => StrategyType.Custom;

    // Premarket: 4:00 AM ET to 9:30 AM ET
    private static readonly TimeSpan PremarketStart = new(4, 0, 0);
    private static readonly TimeSpan PremarketEnd = new(9, 30, 0);
    private static readonly TimeSpan RthStart = new(9, 30, 0);
    private static readonly TimeSpan RthEnd = new(16, 0, 0);

    public IReadOnlyList<TradeSignal> Evaluate(string symbol, IReadOnlyList<Candle> candles, StrategyContext context)
    {
        if (candles.Count < 60) return [];

        // Read parameters
        var gapMinPercent = GetParam(context.Parameters, "GapMinPercent", 1.5m);
        var atrMultiplier = GetParam(context.Parameters, "AtrMultiplier", 1.5m);

        var tz = context.Timezone;
        var evalLocalTime = TimeZoneInfo.ConvertTimeFromUtc(context.EvaluationTimeUtc, tz);
        var evalTimeOfDay = evalLocalTime.TimeOfDay;

        // Check if we're in a valid session (premarket or within first 2 hours of RTH)
        var inPremarket = evalTimeOfDay >= PremarketStart && evalTimeOfDay < PremarketEnd;
        var inRthFirst2Hours = evalTimeOfDay >= RthStart && evalTimeOfDay < RthStart.Add(TimeSpan.FromHours(2));

        if (!inPremarket && !inRthFirst2Hours) return [];

        // Separate premarket candles from all candles
        var premarketCandles = candles
            .Where(c =>
            {
                var local = TimeZoneInfo.ConvertTimeFromUtc(c.StartUtc, tz);
                return local.TimeOfDay >= PremarketStart && local.TimeOfDay < PremarketEnd;
            })
            .ToList();

        if (premarketCandles.Count == 0) return [];

        // Find the last candle before premarket (previous close)
        var firstPremarketStart = premarketCandles[0].StartUtc;
        var previousClose = candles
            .Where(c => c.StartUtc < firstPremarketStart)
            .Select(c => c.Close)
            .LastOrDefault();

        if (previousClose == 0m) return [];

        // Calculate premarket high and low
        var premarketHigh = premarketCandles.Max(c => c.High);
        var premarketLow = premarketCandles.Min(c => c.Low);

        // Gap percentage
        var gapPercent = (premarketHigh - previousClose) / previousClose * 100m;

        // Indicators over full candle series
        var rsi = RSI.Calculate(candles, 14);
        var atr = ATR.Calculate(candles, 14);

        var lastIdx = candles.Count - 1;
        var currentCandle = candles[lastIdx];
        var currentClose = currentCandle.Close;
        var currentRsi = rsi[lastIdx];
        var currentAtr = atr[lastIdx];

        // Average volume over the full series
        var avgVolume = candles.Average(c => c.Volume);

        var signals = new List<TradeSignal>();

        // LONG signal: gap up
        if (gapPercent > gapMinPercent)
        {
            // Price must be within 0.5% of premarket high (consolidating near high)
            var distFromPremHigh = Math.Abs(currentClose - premarketHigh) / premarketHigh * 100m;

            if (distFromPremHigh <= 0.5m && currentRsi >= 40m && currentRsi <= 70m && currentCandle.Volume > avgVolume)
            {
                var entry = premarketHigh;
                var target = entry + currentAtr * atrMultiplier;
                var stopByLow = premarketLow;
                var stopByAtr = entry - currentAtr * 0.75m;
                var stop = Math.Max(stopByLow, stopByAtr); // use the tighter (higher) stop

                var confidence = CalculateLongConfidence(gapPercent, currentRsi, currentCandle.Volume, avgVolume);

                signals.Add(new TradeSignal
                {
                    Symbol = symbol,
                    Direction = TradeDirection.Long,
                    ConfidencePercent = confidence,
                    SuggestedEntry = entry,
                    SuggestedStop = stop,
                    Targets = [target],
                    Reason = $"PremarketBreakout LONG: gap {gapPercent:F2}%, price near premarket high {premarketHigh:F2}, RSI {currentRsi:F1}",
                    StrategyName = Name,
                    GeneratedUtc = DateTime.UtcNow
                });
            }
        }

        // SHORT signal: gap down
        if (gapPercent < -gapMinPercent)
        {
            // Price must be within 0.5% of premarket low
            var distFromPremLow = Math.Abs(currentClose - premarketLow) / premarketLow * 100m;

            if (distFromPremLow <= 0.5m && currentRsi >= 30m && currentRsi <= 60m && currentCandle.Volume > avgVolume)
            {
                var entry = premarketLow;
                var target = entry - currentAtr * atrMultiplier;
                var stopByHigh = premarketHigh;
                var stopByAtr = entry + currentAtr * 0.75m;
                var stop = Math.Min(stopByHigh, stopByAtr); // use the tighter (lower) stop

                var confidence = CalculateShortConfidence(gapPercent, currentRsi, currentCandle.Volume, avgVolume);

                signals.Add(new TradeSignal
                {
                    Symbol = symbol,
                    Direction = TradeDirection.Short,
                    ConfidencePercent = confidence,
                    SuggestedEntry = entry,
                    SuggestedStop = stop,
                    Targets = [target],
                    Reason = $"PremarketBreakout SHORT: gap {gapPercent:F2}%, price near premarket low {premarketLow:F2}, RSI {currentRsi:F1}",
                    StrategyName = Name,
                    GeneratedUtc = DateTime.UtcNow
                });
            }
        }

        return signals;
    }

    private static decimal CalculateLongConfidence(decimal gapPercent, decimal rsi, decimal volume, decimal avgVolume)
    {
        var confidence = 50m;

        // Gap contribution: gap% * 5 clamped to 30
        var gapContrib = Math.Min(gapPercent * 5m, 30m);
        confidence += gapContrib;

        // RSI between 50-60 adds 10
        if (rsi >= 50m && rsi <= 60m)
            confidence += 10m;

        // Volume above 1.5x average adds 10
        if (avgVolume > 0m && volume >= avgVolume * 1.5m)
            confidence += 10m;

        return Math.Min(confidence, 90m);
    }

    private static decimal CalculateShortConfidence(decimal gapPercent, decimal rsi, decimal volume, decimal avgVolume)
    {
        var confidence = 50m;

        // Gap contribution: |gap%| * 5 clamped to 30
        var gapContrib = Math.Min(Math.Abs(gapPercent) * 5m, 30m);
        confidence += gapContrib;

        // RSI between 40-50 adds 10 (oversold zone for short)
        if (rsi >= 40m && rsi <= 50m)
            confidence += 10m;

        // Volume above 1.5x average adds 10
        if (avgVolume > 0m && volume >= avgVolume * 1.5m)
            confidence += 10m;

        return Math.Min(confidence, 90m);
    }

    private static decimal GetParam(Dictionary<string, object> parameters, string key, decimal defaultValue)
    {
        if (parameters.TryGetValue(key, out var val))
        {
            if (val is decimal d) return d;
            if (val is double dbl) return (decimal)dbl;
            if (val is int i) return i;
            if (decimal.TryParse(val.ToString(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }
        return defaultValue;
    }
}
