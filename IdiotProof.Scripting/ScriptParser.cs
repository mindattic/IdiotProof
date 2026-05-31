using System.Globalization;
using System.Text.RegularExpressions;

namespace IdiotProof.Scripting;

/// <summary>
/// Best-effort IdiotScript parser. Walks fluent-style <c>.Verb(args)</c> tokens
/// in a stored ScriptText and dispatches onto the canonical <see cref="StrategyBuilder"/>.
/// Unknown verbs are silently skipped — the goal is "render what we can" rather
/// than "fail on first surprise." A Roslyn-based proper parser is on the roadmap.
///
/// Used by the Monitor (autonomous evaluation) and by any UI surface that wants
/// to visualize a saved strategy. Adding a new builder verb? Add one case here
/// so both paths recognize it.
/// </summary>
public static class ScriptParser
{
    public static StrategyDefinition? ParseScript(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Accept both the quoted canonical form Ticker("NCI") emitted by the serializer
        // and the unquoted Ticker(NCI) used in hand-authored scripts.
        var tickerMatch = Regex.Match(text, "Ticker\\(\\s*\\\"?([^\\\"()\\s,]+)\\\"?", RegexOptions.IgnoreCase);
        if (!tickerMatch.Success) return null;

        var builder = Stock.Ticker(tickerMatch.Groups[1].Value);

        foreach (Match v in Regex.Matches(text, "\\.(\\w+)\\s*(?:\\(([^)]*)\\))?", RegexOptions.IgnoreCase))
        {
            ApplyVerb(builder, v.Groups[1].Value, v.Groups[2].Success ? v.Groups[2].Value : "");
        }

        return builder.Build();
    }

    private static void ApplyVerb(StrategyBuilder b, string name, string args)
    {
        var nums = ParseNumericArgs(args);
        switch (name.ToLowerInvariant())
        {
            // VWAP
            case "isabovevwap": case "abovevwap":     b.IsAboveVwap(); break;
            case "isbelowvwap": case "belowvwap":     b.IsBelowVwap(); break;
            case "onvwapreclaim":                     b.OnVwapReclaim(); break;
            case "onvwaploss":                        b.OnVwapLoss(); break;

            // EMA family
            case "isaboveema":     if (nums.Count >= 1) b.IsAboveEma((int)nums[0]); break;
            case "isbelowema":     if (nums.Count >= 1) b.IsBelowEma((int)nums[0]); break;
            case "isemaabove":     if (nums.Count >= 1) b.IsEmaAbove((int)nums[0]); break;
            case "isemabelow":     if (nums.Count >= 1) b.IsEmaBelow((int)nums[0]); break;
            case "isbetweenema":   if (nums.Count >= 2) b.IsBetweenEma((int)nums[0], (int)nums[1]); break;
            case "requireemastack":if (nums.Count >= 2) b.RequireEmaStack((int)nums[0], (int)nums[1]); break;
            case "onreclaim":      if (nums.Count >= 1) b.OnReclaim((int)nums[0]); break;

            // ADX/DI
            case "requireadxabove": b.RequireAdxAbove(nums.Count >= 1 ? (double)nums[0] : 20); break;
            case "isadxabove":      if (nums.Count >= 1) b.IsAdxAbove((double)nums[0]); break;
            case "isdipositive":    b.IsDiPositive(); break;
            case "isdinegative":    b.IsDiNegative(); break;

            // RSI
            case "isrsioversold":          b.IsRsiOversold(nums.Count >= 1 ? (double)nums[0] : 30); break;
            case "isrsioverbought":        b.IsRsiOverbought(nums.Count >= 1 ? (double)nums[0] : 70); break;
            case "oversold":               b.Oversold(nums.Count >= 1 ? (double)nums[0] : 30); break;
            case "overbought":             b.Overbought(nums.Count >= 1 ? (double)nums[0] : 70); break;
            case "isrsibullishdivergence": b.IsRsiBullishDivergence(); break;
            case "isrsibearishdivergence": b.IsRsiBearishDivergence(); break;

            // MACD
            case "ismacdbullish": case "bullishmacd": b.IsMacdBullish(); break;
            case "ismacdbearish": case "bearishmacd": b.IsMacdBearish(); break;

            // Volume
            case "withvolumeconfirm": b.WithVolumeConfirm(nums.Count >= 1 ? (double)nums[0] : 1.2); break;
            case "isvolumeabove":     if (nums.Count >= 1) b.IsVolumeAbove((double)nums[0]); break;
            case "volumespike":       b.VolumeSpike(nums.Count >= 1 ? (double)nums[0] : 2.0); break;

            // Gap
            case "isgapup":   b.IsGapUp(nums.Count >= 1 ? (double)nums[0] : 3); break;
            case "isgapdown": b.IsGapDown(nums.Count >= 1 ? (double)nums[0] : 3); break;

            // Support / Resistance
            case "isatsupport":    b.IsAtSupport(nums.Count >= 1 ? (double)nums[0] : 0.5); break;
            case "isatresistance": b.IsAtResistance(nums.Count >= 1 ? (double)nums[0] : 0.5); break;

            // Candle patterns
            case "isbullishengulfing": b.IsBullishEngulfing(); break;
            case "isbearishengulfing": b.IsBearishEngulfing(); break;
            case "ishammer":           b.IsHammer(); break;
            case "isshootingstar":     b.IsShootingStar(); break;
            case "isdoji":             b.IsDoji(); break;

            // Patterns (breakout/pullback state-machine triggers)
            case "breakout": b.Breakout(nums.Count >= 1 ? (double)nums[0] : null); break;
            case "pullback": b.Pullback(nums.Count >= 1 ? (double)nums[0] : null); break;

            // Price levels
            case "holdsabove":  if (nums.Count >= 1) b.HoldsAbove((double)nums[0]); break;
            case "holdsbelow":  if (nums.Count >= 1) b.HoldsBelow((double)nums[0]); break;
            case "isnear":      if (nums.Count >= 1) b.IsNear((double)nums[0], nums.Count >= 2 ? (double)nums[1] : 1.0); break;

            // Order
            case "long":             b.Long(); break;
            case "short":            b.Short(); break;
            case "quantity":         if (nums.Count >= 1) b.Quantity((int)nums[0]); break;
            case "quantityshares":   if (nums.Count >= 1) b.QuantityShares((int)nums[0]); break;
            case "quantitynotional": if (nums.Count >= 1) b.QuantityNotional(nums[0]); break;
            case "stoploss":         if (nums.Count >= 1) b.StopLoss((double)nums[0]); break;
            case "stoplosspercent":  if (nums.Count >= 1) b.StopLossPercent((double)nums[0]); break;
            case "takeprofit":
                if (nums.Count >= 3)      b.TakeProfit((double)nums[0], (double)nums[1], (double)nums[2]);
                else if (nums.Count == 2) b.TakeProfit((double)nums[0], (double)nums[1]);
                else if (nums.Count == 1) b.TakeProfit((double)nums[0]);
                break;
            case "takeprofitpercent":if (nums.Count >= 1) b.TakeProfitPercent((double)nums[0]); break;
            case "trailingstoploss": if (nums.Count >= 1) b.TrailingStopLoss((double)nums[0]); break;

            // Misc
            case "autonomoustrading": b.AutonomousTrading(); break;
            case "adaptiveorder":     b.AdaptiveOrder(); break;
            case "repeat":            b.Repeat(); break;
        }
    }

    private static List<decimal> ParseNumericArgs(string args)
    {
        var result = new List<decimal>();
        if (string.IsNullOrWhiteSpace(args)) return result;
        foreach (var raw in args.Split(','))
        {
            var s = raw.Trim().TrimEnd('m', 'M', 'd', 'D', 'f', 'F').Trim();
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            {
                result.Add(d);
            }
        }
        return result;
    }
}
