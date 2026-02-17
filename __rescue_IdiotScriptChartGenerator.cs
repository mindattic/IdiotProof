// ============================================================================
// IdiotScriptChartGenerator - ASCII chart generator for IdiotScript strategies
// ============================================================================
//
// Generates ASCII price charts to visualize strategy entry/exit points.
// Charts use ASCII-only characters for console compatibility.
//
// CHARACTER SET:
// |  - Vertical axis
// -  - Horizontal lines
// +  - Corners and intersections
// *  - Price points
// /  - Price rising
// \  - Price falling
// <- - Annotation arrows
// -> - Time arrow
//
// ============================================================================

using IdiotProof.Shared.Enums;
using IdiotProof.Shared.Models;

namespace IdiotProof.Shared.Scripting;

/// <summary>
/// Generates ASCII price charts for IdiotScript strategies.
/// </summary>
public static class IdiotScriptChartGenerator
{
    private const int PriceLabelWidth = 8;
    private const int ChartWidth = 40;

    /// <summary>
    /// Generates an ASCII chart for a strategy definition.
    /// </summary>
    public static string GenerateChart(StrategyDefinition strategy)
    {
        var stats = strategy.GetStats();
        var priceLevels = CollectPriceLevels(strategy, stats);

        if (priceLevels.Count == 0)
            return string.Empty;

        // Determine chart type based on conditions
        bool hasBreakout = strategy.Segments.Any(s => s.Type == SegmentType.Breakout);
        bool hasPullback = strategy.Segments.Any(s => s.Type == SegmentType.Pullback);
        bool hasVwap = strategy.Segments.Any(s => s.Type == SegmentType.IsAboveVwap || s.Type == SegmentType.IsBelowVwap);

        if (hasBreakout && hasPullback)
            return GenerateBreakoutPullbackChart(priceLevels);
        else
            return GenerateSimpleChart(priceLevels, hasVwap);
    }

    /// <summary>
    /// Generates chart comment lines (prefixed with #).
    /// </summary>
    public static string GenerateChartComments(StrategyDefinition strategy)
    {
        var chart = GenerateChart(strategy);
        if (string.IsNullOrEmpty(chart))
            return string.Empty;

        var lines = chart.Split('\n');
        return string.Join("\n", lines.Select(line => $"# {line}"));
    }

    private static List<PriceLevel> CollectPriceLevels(StrategyDefinition strategy, StrategyStats stats)
    {
        var levels = new List<PriceLevel>();

        // Entry price
        if (stats.Price > 0)
            levels.Add(new PriceLevel(stats.Price, "Entry", PriceLevelType.Entry));

        // Take profit
        if (stats.TakeProfit > 0)
            levels.Add(new PriceLevel(stats.TakeProfit, "Take profit target", PriceLevelType.TakeProfit));

        // Stop loss
        if (stats.StopLoss > 0)
            levels.Add(new PriceLevel(stats.StopLoss, "Stop loss", PriceLevelType.StopLoss));

        // Breakout level
        var breakoutSegment = strategy.Segments.FirstOrDefault(s => s.Type == SegmentType.Breakout);
        if (breakoutSegment != null)
        {
            var breakoutPrice = GetParameterValue<double>(breakoutSegment, "Level", 0);
            if (breakoutPrice > 0)
                levels.Add(new PriceLevel(breakoutPrice, "Breakout / Resistance", PriceLevelType.Breakout));
        }

        // Pullback level
        var pullbackSegment = strategy.Segments.FirstOrDefault(s => s.Type == SegmentType.Pullback);
        if (pullbackSegment != null)
        {
            var pullbackPrice = GetParameterValue<double>(pullbackSegment, "Level", 0);
            if (pullbackPrice > 0)
                levels.Add(new PriceLevel(pullbackPrice, "Pullback / Support", PriceLevelType.Pullback));
        }

        // Extended target (if we have entry and TP)
        if (stats.Price > 0 && stats.TakeProfit > 0)
        {
            var extendedTarget = stats.TakeProfit + (stats.TakeProfit - stats.Price) * 0.5;
            levels.Add(new PriceLevel(extendedTarget, "Extended target", PriceLevelType.Extended));
        }

        // Sort by price descending (highest at top)
        return levels.OrderByDescending(l => l.Price).ToList();
    }

    private static string GenerateBreakoutPullbackChart(List<PriceLevel> levels)
    {
        var lines = new List<string>();
        var sortedLevels = levels.OrderByDescending(l => l.Price).ToList();

        // Find key levels
        var breakoutLevel = levels.FirstOrDefault(l => l.Type == PriceLevelType.Breakout);
        var pullbackLevel = levels.FirstOrDefault(l => l.Type == PriceLevelType.Pullback);
        var entryLevel = levels.FirstOrDefault(l => l.Type == PriceLevelType.Entry);

        lines.Add("Price");
        lines.Add("  |");

        // Add annotation for breakout
        if (breakoutLevel != null)
        {
            lines.Add("  |       +-- Breakout triggers here");
            lines.Add("  |       |");
        }

        foreach (var level in sortedLevels)
        {
            var priceLabel = FormatPrice(level.Price);
            var annotation = GetAnnotation(level);

            string chartLine = level.Type switch
            {
                PriceLevelType.TakeProfit or PriceLevelType.Extended =>
                    $"{priceLabel} -------------------------------- {annotation}",
                PriceLevelType.Entry =>
                    $"{priceLabel} --+-------*-----------           {annotation}",
                PriceLevelType.Breakout =>
                    $"{priceLabel} --+------*   \\                   {annotation}",
                PriceLevelType.Pullback =>
                    $"{priceLabel} --+----*-------*-------          {annotation}",
                _ =>
                    $"{priceLabel} --+--*           *-----          {annotation}"
            };

            lines.Add(chartLine);

            // Add spacing between levels
            if (level != sortedLevels.Last())
            {
                lines.Add("  |       |");
            }
        }

        // Add VWAP line if applicable
        if (entryLevel != null && pullbackLevel != null)
        {
            var vwapPrice = pullbackLevel.Price * 0.96;
            var vwapLabel = FormatPrice(vwapPrice);
            lines.Add("  |       |  /");
            lines.Add($"{vwapLabel} --+--*           *-----          <- VWAP check, BUY");
            lines.Add("  |       |  /");
        }

        lines.Add("  +-------+----------------------------> Time");

        return string.Join("\n", lines);
    }

    private static string GenerateSimpleChart(List<PriceLevel> levels, bool hasVwap)
    {
        var lines = new List<string>();
        var sortedLevels = levels.OrderByDescending(l => l.Price).ToList();

        lines.Add("Price");
        lines.Add("  |");

        for (int i = 0; i < sortedLevels.Count; i++)
        {
            var level = sortedLevels[i];
            var priceLabel = FormatPrice(level.Price);
            var annotation = GetAnnotation(level);

            string chartLine;
            if (level.Type == PriceLevelType.Entry && hasVwap)
            {
                chartLine = $"{priceLabel} ----------*--------------------- {annotation} (+ VWAP)";
            }
            else
            {
                chartLine = $"{priceLabel} -------------------------------- {annotation}";
            }

            lines.Add(chartLine);

            // Add price movement between levels (only once, after TP)
            if (i == 0 && sortedLevels.Count > 1)
            {
                lines.Add("  |                  /");
                lines.Add("  |                 /");
                lines.Add("  |                *");
                lines.Add("  |               /");
            }
            else if (i < sortedLevels.Count - 1)
            {
                lines.Add("  |");
            }
        }

        // Add higher lows pattern at bottom
        lines.Add("  |            /");
        lines.Add("  |   *-------*                        <- Higher lows forming");
        lines.Add("  |  /");
        lines.Add("  +-*---------------------------------> Time");

        return string.Join("\n", lines);
    }

    private static string FormatPrice(double price)
    {
        return $"${price:F2}".PadLeft(PriceLabelWidth);
    }

    private static string GetAnnotation(PriceLevel level)
    {
        return level.Type switch
        {
            PriceLevelType.Entry => "<- Entry",
            PriceLevelType.TakeProfit => "<- Take profit",
            PriceLevelType.StopLoss => "<- Stop loss",
            PriceLevelType.Breakout => "<- Resistance",
            PriceLevelType.Pullback => "<- Pullback",
            PriceLevelType.Extended => "<- Extended target",
            _ => ""
        };
    }

    private static T GetParameterValue<T>(StrategySegment segment, string paramName, T defaultValue)
    {
        var param = segment.Parameters.FirstOrDefault(p => 
            p.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase));

        if (param?.Value == null)
            return defaultValue;

        try
        {
            if (typeof(T) == typeof(double) && param.Value is double d)
                return (T)(object)d;
            if (typeof(T) == typeof(double) && param.Value is int intVal)
                return (T)(object)(double)intVal;
            if (typeof(T) == typeof(double) && param.Value is decimal dec)
                return (T)(object)(double)dec;
            if (typeof(T) == typeof(double) && double.TryParse(param.Value.ToString(), out var parsed))
                return (T)(object)parsed;

            return (T)Convert.ChangeType(param.Value, typeof(T));
        }
        catch
        {
            return defaultValue;
        }
    }

    private record PriceLevel(double Price, string Label, PriceLevelType Type);

    private enum PriceLevelType
    {
        Entry,
        TakeProfit,
        StopLoss,
        Breakout,
        Pullback,
        Extended
    }
}
