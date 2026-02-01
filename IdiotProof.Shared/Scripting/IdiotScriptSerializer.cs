// ============================================================================
// IdiotScriptSerializer - Convert strategies to IdiotScript format
// ============================================================================
//
// Converts StrategyDefinition objects back to IdiotScript text format.
// This enables round-trip conversion: IdiotScript → Strategy → IdiotScript
//
// OUTPUT FORMAT (PascalCase):
// Ticker(AAPL).Session(IS.PREMARKET).Qty(100).Breakout(150).Pullback(148).AboveVwap.TakeProfit(155).TrailingStopLoss(15).ClosePosition(IS.BELL)
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

        // Name (if not default)
        if (!string.IsNullOrEmpty(strategy.Name) && 
            !strategy.Name.EndsWith("Strategy", StringComparison.OrdinalIgnoreCase))
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
            parts.Add("Sell");
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

        return string.Join(".", parts);
    }

    /// <summary>
    /// Converts a StrategyDefinition to a formatted multi-line IdiotScript string.
    /// </summary>
    public static string SerializeFormatted(StrategyDefinition strategy)
    {
        var script = Serialize(strategy);
        return FormatScript(script);
    }

    /// <summary>
    /// Formats an IdiotScript string with line breaks for readability.
    /// </summary>
    public static string FormatScript(string script)
    {
        var sb = new StringBuilder();
        var parts = script.Split('.', StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Group related commands
            if (trimmed.StartsWith("TICKER", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("NAME", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("DESC", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"{trimmed}.");
            }
            else if (trimmed.StartsWith("SESSION", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("QTY", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"{trimmed}.");
            }
            else if (trimmed.StartsWith("TP", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("SL", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("TSL", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"{trimmed}.");
            }
            else if (trimmed.StartsWith("CLOSE", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"{trimmed}.");
            }
            else
            {
                sb.AppendLine($"{trimmed}.");
            }
        }

        return sb.ToString().TrimEnd();
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
                SegmentType.IsAboveVwap => "AboveVwap",
                SegmentType.IsBelowVwap => "BelowVwap",
                SegmentType.IsEmaAbove => BuildEmaAbove(segment),
                SegmentType.IsEmaBelow => BuildEmaBelow(segment),
                SegmentType.IsEmaBetween => BuildEmaBetween(segment),
                SegmentType.IsRsi => BuildRsi(segment),
                SegmentType.IsAdx => BuildAdx(segment),
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
        return level > 0 ? $"Breakout({FormatPrice(level)})" : "Breakout";
    }

    private static string? BuildPullback(StrategySegment segment)
    {
        var level = GetParameterValue<double>(segment, "Level", 0);
        return level > 0 ? $"Pullback({FormatPrice(level)})" : "Pullback";
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
