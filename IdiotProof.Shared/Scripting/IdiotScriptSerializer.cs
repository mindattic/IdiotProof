// ============================================================================
// IdiotScriptSerializer - Convert strategies to IdiotScript format
// ============================================================================
//
// Converts StrategyDefinition objects back to IdiotScript text format.
// This enables round-trip conversion: IdiotScript → Strategy → IdiotScript
//
// OUTPUT FORMAT (PascalCase with parentheses):
// Ticker(AAPL).Session(IS.PREMARKET).Qty(100).Breakout(150).Pullback(148).AboveVwap().TakeProfit(155).TrailingStopLoss(15).ClosePosition(IS.BELL)
//
// ============================================================================

using System.Text;
using IdiotProof.Shared.Enums;
using IdiotProof.Shared.Models;

namespace IdiotProof.Shared.Scripting;

/// <summary>
/// Converts StrategyDefinition objects to IdiotScript text format.
/// Uses period (.) as the universal delimiter.
/// Outputs PascalCase commands for consistency.
/// </summary>
public static class IdiotScriptSerializer
{
    /// <summary>
    /// Converts a StrategyDefinition to an IdiotScript string.
    /// </summary>
    public static string Serialize(StrategyDefinition strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);

        var parts = new List<string>();

        // Symbol (required, goes first)
        parts.Add($"Ticker({strategy.Symbol})");

        // Name
        // Always include if present (tests expect explicit Name serialization)
        if (!string.IsNullOrEmpty(strategy.Name))
        {
            parts.Add($"Name(\"{strategy.Name}\")");
        }

        // Description
        if (!string.IsNullOrEmpty(strategy.Description))
        {
            parts.Add($"Desc(\"{strategy.Description}\")");
        }

        // Enabled (only if disabled)
        if (!strategy.Enabled)
        {
            parts.Add("Enabled(false)");
        }

        // Group segments by category
        var orderedSegments = strategy.Segments.OrderBy(s => s.Order).ToList();

        // Session
        var sessionSegment = orderedSegments.FirstOrDefault(s => s.Type == SegmentType.SessionDuration);
        if (sessionSegment != null)
        {
            var session = GetParameterValue<string>(sessionSegment, "Session", "");
            if (!string.IsNullOrEmpty(session))
            {
                var sessionConstant = GetSessionConstant(session);
                parts.Add($"Session({sessionConstant})");
            }
        }

        // Quantity (get from Buy/Sell segment)
        var orderSegment = orderedSegments.FirstOrDefault(s => s.Type is SegmentType.Buy or SegmentType.Sell);
        if (orderSegment != null)
        {
            var qty = GetParameterValue<int>(orderSegment, "Quantity", 1);
            if (qty > 1)
            {
                parts.Add($"Qty({qty})");
            }
        }

        // Entry price (from order limit price or price conditions)
        var limitPrice = GetParameterValue<double?>(orderSegment, "LimitPrice", null);
        if (limitPrice.HasValue && limitPrice.Value > 0)
        {
            parts.Add($"Entry({FormatPrice(limitPrice.Value)})");
        }

        // Build conditions
        var conditions = BuildConditions(orderedSegments);
        parts.AddRange(conditions);

        // Order direction (only if SELL)
        if (orderSegment?.Type == SegmentType.Sell)
        {
            parts.Add("Sell()");
        }

        // Take profit
        var tpSegment = orderedSegments.FirstOrDefault(s => s.Type is SegmentType.TakeProfit or SegmentType.TakeProfitRange);
        if (tpSegment != null)
        {
            if (tpSegment.Type == SegmentType.TakeProfitRange)
            {
                var low = GetParameterValue<double>(tpSegment, "LowPrice", 0);
                var high = GetParameterValue<double>(tpSegment, "HighPrice", 0);
                parts.Add($"TakeProfit({FormatPrice(low)}, {FormatPrice(high)})");
            }
            else
            {
                var price = GetParameterValue<double>(tpSegment, "Price", 0);
                if (price > 0)
                    parts.Add($"TakeProfit({FormatPrice(price)})");
            }
        }

        // Stop loss
        var slSegment = orderedSegments.FirstOrDefault(s => s.Type == SegmentType.StopLoss);
        if (slSegment != null)
        {
            var price = GetParameterValue<double>(slSegment, "Price", 0);
            if (price > 0)
                parts.Add($"StopLoss({FormatPrice(price)})");
        }

        // Trailing stop loss
        var tslSegment = orderedSegments.FirstOrDefault(s => s.Type == SegmentType.TrailingStopLoss);
        if (tslSegment != null)
        {
            var percent = GetParameterValue<double>(tslSegment, "Percentage", 0);
            if (percent > 0)
            {
                var tslConstant = GetTslConstant(percent);
                parts.Add($"TrailingStopLoss({tslConstant})");
            }
        }

        // Close position
        var closeSegment = orderedSegments.FirstOrDefault(s => s.Type == SegmentType.ClosePosition);
        if (closeSegment != null)
        {
            var time = GetParameterValue<TimeOnly?>(closeSegment, "Time", null);
            var onlyIfProfitable = GetParameterValue<bool>(closeSegment, "OnlyIfProfitable", true);

            if (time.HasValue)
            {
                var timeConstant = GetTimeConstant(time.Value);
                if (onlyIfProfitable)
                    parts.Add($"ClosePosition({timeConstant}, Y)");
                else
                    parts.Add($"ClosePosition({timeConstant})");
            }
        }

        // Repeat (execution behavior)
        if (strategy.RepeatEnabled)
        {
            parts.Add("Repeat()");
        }

        return string.Join(".", parts);
    }

    /// <summary>
    /// Converts a StrategyDefinition to a formatted multi-line IdiotScript string.
    /// Each command starts on a new line with the period prefix (except the first command).
    /// </summary>
    public static string SerializeFormatted(StrategyDefinition strategy)
    {
        var script = Serialize(strategy);
        return FormatScript(script);
    }

    /// <summary>
    /// Formats an IdiotScript string with line breaks for readability.
    /// Each command starts on a new line with the period prefix (except the first command).
    /// </summary>
    public static string FormatScript(string script)
    {
        var sb = new StringBuilder();
        var parts = SplitByDelimiter(script);
        bool isFirst = true;

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            if (isFirst)
            {
                sb.Append(trimmed);
                isFirst = false;
            }
            else
            {
                sb.AppendLine();
                sb.Append($".{trimmed}");
            }
        }

        return sb.ToString();
    }

    private static List<string> SplitByDelimiter(string script)
    {
        var commands = new List<string>();
        var current = new StringBuilder();
        int parenDepth = 0;

        for (int i = 0; i < script.Length; i++)
        {
            var ch = script[i];

            if (ch == '(') parenDepth++;
            if (ch == ')') parenDepth = Math.Max(0, parenDepth - 1);

            if (ch == '.' && parenDepth == 0)
            {
                // Preserve IS. constants
                if (IsConstantPrefix(script, i))
                {
                    current.Append(ch);
                    continue;
                }

                // Preserve decimal numbers
                if (IsDecimalPoint(script, i))
                {
                    current.Append(ch);
                    continue;
                }

                commands.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            commands.Add(current.ToString());

        return commands;
    }

    private static bool IsConstantPrefix(string script, int dotIndex)
    {
        if (dotIndex < 2) return false;
        return script[dotIndex - 2] is 'I' or 'i' &&
               script[dotIndex - 1] is 'S' or 's';
    }

    private static bool IsDecimalPoint(string script, int dotIndex)
    {
        var prev = dotIndex > 0 ? script[dotIndex - 1] : '\0';
        var next = dotIndex + 1 < script.Length ? script[dotIndex + 1] : '\0';
        return char.IsDigit(prev) && char.IsDigit(next);
    }

    // ========================================================================
    // HELPER METHODS
    // ========================================================================

    private static List<string> BuildConditions(List<StrategySegment> segments)
    {
        var conditions = new List<string>();

        foreach (var segment in segments)
        {
            var condition = segment.Type switch
            {
                SegmentType.Breakout => BuildBreakout(segment),
                SegmentType.Pullback => BuildPullback(segment),
                SegmentType.IsPriceAbove => BuildPriceAbove(segment),
                SegmentType.IsPriceBelow => BuildPriceBelow(segment),
                SegmentType.IsAboveVwap => "AboveVwap()",
                SegmentType.IsBelowVwap => "BelowVwap()",
                SegmentType.IsEmaAbove => BuildEmaAbove(segment),
                SegmentType.IsEmaBelow => BuildEmaBelow(segment),
                SegmentType.IsEmaBetween => BuildEmaBetween(segment),
                SegmentType.IsEmaTurningUp => BuildEmaTurningUp(segment),
                SegmentType.IsRsi => BuildRsi(segment),
                SegmentType.IsAdx => BuildAdx(segment),
                SegmentType.IsMacd => BuildMacd(segment),
                SegmentType.IsDI => BuildDi(segment),
                SegmentType.IsMomentum => BuildMomentum(segment),
                SegmentType.IsRoc => BuildRoc(segment),
                SegmentType.IsHigherLows => "HigherLows()",
                SegmentType.IsVolumeAbove => BuildVolumeAbove(segment),
                SegmentType.IsCloseAboveVwap => "CloseAboveVwap()",
                SegmentType.IsVwapRejection => "VwapRejection()",
                _ => null
            };

            if (!string.IsNullOrEmpty(condition))
                conditions.Add(condition);
        }

        return conditions;
    }

    private static string? BuildBreakout(StrategySegment segment)
    {
        var level = GetParameterValue<double>(segment, "Level", 0);
        return level > 0 ? $"Breakout({FormatPrice(level)})" : "Breakout()";
    }

    private static string? BuildPullback(StrategySegment segment)
    {
        var level = GetParameterValue<double>(segment, "Level", 0);
        return level > 0 ? $"Pullback({FormatPrice(level)})" : "Pullback()";
    }

    private static string? BuildPriceAbove(StrategySegment segment)
    {
        var level = GetParameterValue<double>(segment, "Level", 0);
        return level > 0 ? $"Entry({FormatPrice(level)})" : null;
    }

    private static string? BuildPriceBelow(StrategySegment segment)
    {
        var level = GetParameterValue<double>(segment, "Level", 0);
        return level > 0 ? $"PriceBelow({FormatPrice(level)})" : null;
    }

    private static string? BuildEmaAbove(StrategySegment segment)
    {
        var period = GetParameterValue<int>(segment, "Period", 0);
        return period > 0 ? $"EmaAbove({period})" : null;
    }

    private static string? BuildEmaBelow(StrategySegment segment)
    {
        var period = GetParameterValue<int>(segment, "Period", 0);
        return period > 0 ? $"EmaBelow({period})" : null;
    }

    private static string? BuildEmaBetween(StrategySegment segment)
    {
        var lower = GetParameterValue<int>(segment, "LowerPeriod", 0);
        var upper = GetParameterValue<int>(segment, "UpperPeriod", 0);
        return lower > 0 && upper > 0 ? $"EmaBetween({lower}, {upper})" : null;
    }

    private static string? BuildRsi(StrategySegment segment)
    {
        var condition = GetParameterValue<string>(segment, "Condition", "");
        var value = GetParameterValue<double>(segment, "Value", 0);

        if (value <= 0) return null;

        return condition.Equals("Above", StringComparison.OrdinalIgnoreCase)
            ? $"RsiOverbought({value:F0})"
            : $"RsiOversold({value:F0})";
    }

    private static string? BuildAdx(StrategySegment segment)
    {
        var value = GetParameterValue<double>(segment, "Value", 0);
        return value > 0 ? $"AdxAbove({value:F0})" : null;
    }

    private static string? BuildMacd(StrategySegment segment)
    {
        var state = GetParameterValue<string>(segment, "State", "");
        return state.Equals("Bullish", StringComparison.OrdinalIgnoreCase)
            ? "MacdBullish()"
            : "MacdBearish()";
    }

    private static string? BuildDi(StrategySegment segment)
    {
        var direction = GetParameterValue<string>(segment, "Direction", "");
        var threshold = GetParameterValue<double>(segment, "Threshold", 25);

        return direction.Equals("Positive", StringComparison.OrdinalIgnoreCase)
            ? $"DiPositive({threshold:F0})"
            : $"DiNegative({threshold:F0})";
    }

    private static string? BuildMomentum(StrategySegment segment)
    {
        var condition = GetParameterValue<string>(segment, "Condition", "");
        var threshold = GetParameterValue<double>(segment, "Threshold", 0);

        return condition.Equals("Above", StringComparison.OrdinalIgnoreCase)
            ? $"MomentumAbove({threshold:F1})"
            : $"MomentumBelow({threshold:F1})";
    }

    private static string? BuildRoc(StrategySegment segment)
    {
        var condition = GetParameterValue<string>(segment, "Condition", "");
        var threshold = GetParameterValue<double>(segment, "Threshold", 0);

        return condition.Equals("Above", StringComparison.OrdinalIgnoreCase)
            ? $"RocAbove({threshold:F1})"
            : $"RocBelow({threshold:F1})";
    }

    private static string? BuildEmaTurningUp(StrategySegment segment)
    {
        var period = GetParameterValue<int>(segment, "Period", 0);
        return period > 0 ? $"EmaTurningUp({period})" : null;
    }

    private static string? BuildVolumeAbove(StrategySegment segment)
    {
        var multiplier = GetParameterValue<double>(segment, "Multiplier", 0);
        return multiplier > 0 ? $"VolumeAbove({multiplier:F1})" : null;
    }

    private static string FormatPrice(double price)
    {
        return price >= 100 ? $"{price:F2}" : $"${price:F2}";
    }

    private static string GetSessionConstant(string session)
    {
        return session.ToUpperInvariant() switch
        {
            "PREMARKET" => "IS.PREMARKET",
            "RTH" => "IS.RTH",
            "AFTERHOURS" => "IS.AFTERHOURS",
            "EXTENDED" => "IS.EXTENDED",
            "ACTIVE" => "IS.ACTIVE",
            "PREMARKETENDEARLY" => "IS.PREMARKET_END_EARLY",
            "PREMARKETSTARTLATE" => "IS.PREMARKET_START_LATE",
            _ => session
        };
    }

    private static string GetTslConstant(double percent)
    {
        return percent switch
        {
            0.05 => "IS.TIGHT",
            0.10 => "IS.MODERATE",
            0.15 => "IS.STANDARD",
            0.20 => "IS.LOOSE",
            0.25 => "IS.WIDE",
            _ => $"{percent * 100:F0}"
        };
    }

    private static string GetTimeConstant(TimeOnly time)
    {
        // Check for known times
        if (time == new TimeOnly(9, 20)) return "IS.BELL";
        if (time == new TimeOnly(9, 30)) return "IS.OPEN";
        if (time == new TimeOnly(16, 0)) return "IS.CLOSE";
        if (time == new TimeOnly(4, 0)) return "IS.PM_START";
        if (time == new TimeOnly(20, 0)) return "IS.AH_END";

        // Return formatted time
        return time.ToString("H:mm");
    }

    private static T GetParameterValue<T>(StrategySegment? segment, string name, T defaultValue)
    {
        if (segment == null) return defaultValue;

        var param = segment.Parameters.FirstOrDefault(p =>
            p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (param?.Value == null) return defaultValue;

        try
        {
            if (typeof(T) == typeof(TimeOnly?) && param.Value is TimeOnly time)
                return (T)(object)time;

            if (typeof(T) == typeof(TimeOnly?) && param.Value is string timeStr)
            {
                if (TimeOnly.TryParse(timeStr, out var parsedTime))
                    return (T)(object)parsedTime;
                return defaultValue;
            }

            return (T)Convert.ChangeType(param.Value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
        }
        catch
        {
            return defaultValue;
        }
    }
}
