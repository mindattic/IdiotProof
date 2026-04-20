using IdiotProof.Indicators;
using IdiotProof.Models;

namespace IdiotProof.Strategies;

/// <summary>
/// Detects losing momentum via RSI divergence, ADX weakening, MACD histogram shrinking,
/// and volume decline — signals short before falls or long before bounces.
/// </summary>
public sealed class MomentumDecayStrategy : IStrategy
{
    public string Name => "MomentumDecay";
    public string Description => "Detects losing momentum via RSI divergence, ADX weakening, MACD histogram shrinking, and volume decline — signals short before falls or long before bounces";
    public StrategyType Type => StrategyType.Custom;

    public IReadOnlyList<TradeSignal> Evaluate(string symbol, IReadOnlyList<Candle> candles, StrategyContext context)
    {
        if (candles.Count < 50) return [];

        var minConfluence = GetIntParam(context.Parameters, "MinConfluence", 2);

        var rsi = RSI.Calculate(candles, 14);
        var macdResults = MACD.Calculate(candles, 12, 26, 9);
        var adxResults = ADX.Calculate(candles, 14);
        var atr = ATR.Calculate(candles, 14);
        var ema20 = EMA.Calculate(candles, 20);

        var lastIdx = candles.Count - 1;
        var lookback = 5; // 5 candles ago for divergence

        if (lastIdx < lookback) return [];

        var prevIdx = lastIdx - lookback;

        var currentCandle = candles[lastIdx];
        var prevCandle = candles[prevIdx];

        var currentRsi = rsi[lastIdx];
        var prevRsi = rsi[prevIdx];

        var currentAdx = adxResults[lastIdx].ADX;
        var prevAdx = adxResults[prevIdx].ADX;

        var currentHistogram = macdResults[lastIdx].Histogram;
        var prevHistogram = macdResults[prevIdx].Histogram;

        var currentAtr = atr[lastIdx];
        var currentEma20 = ema20[lastIdx];

        // Recent 5-candle high and low
        var recent5High = candles.Skip(lastIdx - lookback + 1).Take(lookback).Max(c => c.High);
        var recent5Low = candles.Skip(lastIdx - lookback + 1).Take(lookback).Min(c => c.Low);

        // Volume averages
        var last3Volume = candles.Skip(lastIdx - 2).Take(3).Average(c => c.Volume);
        var last10Volume = candles.Skip(lastIdx - 9).Take(10).Average(c => c.Volume);

        var signals = new List<TradeSignal>();

        // === BEARISH Momentum Decay (SHORT signal) ===
        {
            int confluence = 0;

            // 1. Price making higher highs
            if (currentCandle.High > prevCandle.High)
                confluence++;

            // 2. RSI making lower highs (divergence)
            if (currentRsi < prevRsi)
                confluence++;

            // 3. ADX declining
            if (currentAdx < prevAdx)
                confluence++;

            // 4. MACD histogram shrinking but still positive (bullish but weakening)
            if (currentHistogram > 0m && Math.Abs(currentHistogram) < Math.Abs(prevHistogram))
                confluence++;

            // 5. Volume declining
            if (last10Volume > 0m && last3Volume < last10Volume)
                confluence++;

            if (confluence >= minConfluence)
            {
                var entry = currentCandle.Close;
                var stop = recent5High + currentAtr * 0.5m;
                var target = currentEma20 > 0m ? Math.Min(currentEma20, entry - currentAtr * 2m) : entry - currentAtr * 2m;

                var confidence = CalculateBearishConfidence(confluence, currentRsi);

                signals.Add(new TradeSignal
                {
                    Symbol = symbol,
                    Direction = TradeDirection.Short,
                    ConfidencePercent = confidence,
                    SuggestedEntry = entry,
                    SuggestedStop = stop,
                    Targets = [target],
                    Reason = $"MomentumDecay SHORT: {confluence}/5 confluence, RSI {currentRsi:F1}, ADX {currentAdx:F1} declining",
                    StrategyName = Name,
                    GeneratedUtc = DateTime.UtcNow
                });
            }
        }

        // === BULLISH Momentum Decay (LONG signal — detecting oversold bounce) ===
        {
            int confluence = 0;

            // 1. Price making lower lows
            if (currentCandle.Low < prevCandle.Low)
                confluence++;

            // 2. RSI making higher lows (positive divergence)
            if (currentRsi > prevRsi)
                confluence++;

            // 3. ADX declining (losing downward momentum)
            if (currentAdx < prevAdx)
                confluence++;

            // 4. MACD histogram: negative but getting less negative (shrinking)
            if (currentHistogram < 0m && Math.Abs(currentHistogram) < Math.Abs(prevHistogram))
                confluence++;

            // 5. Volume declining
            if (last10Volume > 0m && last3Volume < last10Volume)
                confluence++;

            if (confluence >= minConfluence)
            {
                var entry = currentCandle.Close;
                var stop = recent5Low - currentAtr * 0.5m;
                var target = currentEma20 > 0m ? Math.Max(currentEma20, entry + currentAtr * 2m) : entry + currentAtr * 2m;

                var confidence = CalculateBullishConfidence(confluence, currentRsi);

                signals.Add(new TradeSignal
                {
                    Symbol = symbol,
                    Direction = TradeDirection.Long,
                    ConfidencePercent = confidence,
                    SuggestedEntry = entry,
                    SuggestedStop = stop,
                    Targets = [target],
                    Reason = $"MomentumDecay LONG: {confluence}/5 confluence, RSI {currentRsi:F1}, ADX {currentAdx:F1} declining",
                    StrategyName = Name,
                    GeneratedUtc = DateTime.UtcNow
                });
            }
        }

        // Return strongest signal only, or both if both >= 60%
        if (signals.Count <= 1) return signals;

        var above60 = signals.Where(s => s.ConfidencePercent >= 60m).ToList();
        if (above60.Count > 1) return above60;

        // Return just the strongest
        return [signals.OrderByDescending(s => s.ConfidencePercent).First()];
    }

    private static decimal CalculateBearishConfidence(int confluence, decimal rsi)
    {
        var confidence = 40m + confluence * 12m;

        // RSI > 65 adds 10 (overbought territory strengthens bearish case)
        if (rsi > 65m)
            confidence += 10m;

        return Math.Min(confidence, 85m);
    }

    private static decimal CalculateBullishConfidence(int confluence, decimal rsi)
    {
        var confidence = 40m + confluence * 12m;

        // RSI < 35 adds 10 (oversold territory strengthens bullish case)
        if (rsi < 35m)
            confidence += 10m;

        return Math.Min(confidence, 85m);
    }

    private static int GetIntParam(Dictionary<string, object> parameters, string key, int defaultValue)
    {
        if (parameters.TryGetValue(key, out var val))
        {
            if (val is int i) return i;
            if (int.TryParse(val.ToString(), out var parsed)) return parsed;
        }
        return defaultValue;
    }
}
