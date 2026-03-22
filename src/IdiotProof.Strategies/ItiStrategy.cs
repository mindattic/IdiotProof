using IdiotProof.Indicators;
using IdiotProof.Models;

namespace IdiotProof.Strategies;

/// <summary>
/// ITI (Is This It?) strategy — 8-category scoring engine for long/short signal detection.
/// Uses EMA34, VWAP, RSI(14), MACD(12,26,9), ADX/DI(14).
/// Ported from Gappy's ItiEvaluator.
/// </summary>
public sealed class ItiStrategy : IStrategy
{
    public string Name => "ITI";
    public string Description => "Is This It? — 8-category scoring model for entry/exit signals";
    public StrategyType Type => StrategyType.Iti;

    public record CategoryScores(
        decimal VwapAlignment, decimal EmaSlope, decimal EmaPosition, decimal EmaPullback,
        decimal Rsi, decimal Macd, decimal Adx, decimal Timing);

    public record ItiScore(int Index, DateTime TimeCst, TradeDirection Direction, CategoryScores Scores)
    {
        public decimal ConfidencePercent => Math.Round(
            (Scores.VwapAlignment + Scores.EmaSlope + Scores.EmaPosition + Scores.EmaPullback +
             Scores.Rsi + Scores.Macd + Scores.Adx + Scores.Timing) / 8m * 100m, 0);
    }

    public IReadOnlyList<TradeSignal> Evaluate(string symbol, IReadOnlyList<Candle> candles, StrategyContext context)
    {
        if (candles.Count < 2) return [];

        var ema34 = EMA.Calculate(candles, 34);
        var vwap = VWAP.Calculate(candles);
        var rsi = RSI.Calculate(candles, 14);
        var macd = MACD.Calculate(candles, 12, 26, 9);
        var adxdi = ADX.Calculate(candles, 14);

        var tz = context.Timezone;
        var scoresLong = new List<ItiScore>();
        var scoresShort = new List<ItiScore>();

        for (int i = 1; i < candles.Count; i++)
        {
            var timeCst = TimeZoneInfo.ConvertTimeFromUtc(candles[i].StartUtc, tz);
            var timingScore = timeCst.TimeOfDay >= new TimeSpan(9, 0, 0) && timeCst.TimeOfDay <= new TimeSpan(15, 58, 0) ? 1m : 0m;

            scoresLong.Add(ScoreCandle(i, TradeDirection.Long, candles, ema34, vwap, rsi, macd, adxdi, timingScore, timeCst));
            scoresShort.Add(ScoreCandle(i, TradeDirection.Short, candles, ema34, vwap, rsi, macd, adxdi, timingScore, timeCst));
        }

        var bestLong = scoresLong.OrderByDescending(s => s.ConfidencePercent).ThenBy(s => s.TimeCst).FirstOrDefault();
        var bestShort = scoresShort.OrderByDescending(s => s.ConfidencePercent).ThenBy(s => s.TimeCst).FirstOrDefault();

        var selected = SelectEntry(bestLong, bestShort);
        if (selected == null) return [];

        var signals = new List<TradeSignal>();
        signals.Add(new TradeSignal
        {
            Symbol = symbol,
            Direction = selected.Direction,
            ConfidencePercent = selected.ConfidencePercent,
            SuggestedEntry = candles[selected.Index].Close,
            Reason = $"ITI {selected.Direction} signal: {selected.ConfidencePercent}% confidence",
            StrategyName = Name
        });

        return signals;
    }

    private static ItiScore ScoreCandle(int i, TradeDirection dir, IReadOnlyList<Candle> candles,
        decimal[] ema34, decimal[] vwap, decimal[] rsi, MacdResult[] macd, AdxResult[] adxdi,
        decimal timingScore, DateTime timeCst)
    {
        var c = candles[i];
        var bodyMin = Math.Min(c.Open, c.Close);
        var bodyMax = Math.Max(c.Open, c.Close);
        var ema = ema34[i];

        decimal vwapAlign = dir == TradeDirection.Long ? c.Close > vwap[i] ? 1m : 0m : c.Close < vwap[i] ? 1m : 0m;

        decimal emaSlope = 0m;
        if (i > 0)
            emaSlope = dir == TradeDirection.Long ? ema > ema34[i - 1] ? 1m : 0m : ema < ema34[i - 1] ? 1m : 0m;

        decimal emaPosition = dir == TradeDirection.Long ? bodyMin >= ema ? 1m : 0m : bodyMax <= ema ? 1m : 0m;

        decimal emaPullback = 0m;
        var dist = dir == TradeDirection.Long ? Math.Abs(bodyMin - ema) : Math.Abs(bodyMax - ema);
        if (dist == 0m) emaPullback = 1m;
        else if (dist <= (bodyMax - bodyMin) * 0.5m) emaPullback = 0.5m;

        decimal rsiScore = 0m;
        if (rsi[i] >= 30m && rsi[i] <= 70m)
        {
            if (dir == TradeDirection.Long)
                rsiScore = i > 0 && rsi[i] >= rsi[i - 1] ? 1m : 0.5m;
            else
                rsiScore = i > 0 && rsi[i] <= rsi[i - 1] ? 1m : 0.5m;
        }

        decimal macdScore = 0m;
        if (i > 0)
        {
            var crossUp = macd[i].Macd > macd[i].Signal && macd[i - 1].Macd <= macd[i - 1].Signal;
            var crossDown = macd[i].Macd < macd[i].Signal && macd[i - 1].Macd >= macd[i - 1].Signal;
            var emaAlignIdx = FindFirstEmaStrictIndex(i, dir, candles, ema34);
            var withinWindow = emaAlignIdx >= 0 && i - emaAlignIdx >= 0 && i - emaAlignIdx <= 3;
            if (dir == TradeDirection.Long)
                macdScore = crossUp ? withinWindow ? 1m : 0.5m : 0m;
            else
                macdScore = crossDown ? withinWindow ? 1m : 0.5m : 0m;
        }

        decimal adxScore = 0m;
        if (i > 0)
        {
            var rising = adxdi[i].ADX > adxdi[i - 1].ADX;
            adxScore = adxdi[i].ADX > 20m && rising ? 1m : 0m;
        }

        var scores = new CategoryScores(vwapAlign, emaSlope, emaPosition, emaPullback, rsiScore, macdScore, adxScore, timingScore);
        return new ItiScore(i, timeCst, dir, scores);
    }

    private static int FindFirstEmaStrictIndex(int currentIdx, TradeDirection dir, IReadOnlyList<Candle> candles, decimal[] ema)
    {
        for (int k = Math.Max(0, currentIdx - 10); k <= currentIdx; k++)
        {
            var bodyMin = Math.Min(candles[k].Open, candles[k].Close);
            var bodyMax = Math.Max(candles[k].Open, candles[k].Close);
            if (dir == TradeDirection.Long && bodyMin >= ema[k]) return k;
            if (dir == TradeDirection.Short && bodyMax <= ema[k]) return k;
        }
        return -1;
    }

    private static ItiScore? SelectEntry(ItiScore? bestLong, ItiScore? bestShort)
    {
        if (bestLong == null && bestShort == null) return null;
        if (bestLong != null && bestShort != null)
        {
            if (bestLong.ConfidencePercent == bestShort.ConfidencePercent)
                return bestLong.TimeCst <= bestShort.TimeCst ? bestLong : bestShort;
            return bestLong.ConfidencePercent > bestShort.ConfidencePercent ? bestLong : bestShort;
        }
        return bestLong ?? bestShort;
    }
}
