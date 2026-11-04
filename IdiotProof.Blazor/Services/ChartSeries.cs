using IdiotProof.Models;
using IdiotProof.Strategies.Backtesting;

namespace IdiotProof.Blazor.Services;

/// <summary>One OHLC point in Lightweight Charts' shape (time = Unix seconds, UTC).</summary>
public sealed record ChartBar(long Time, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);

/// <summary>A marker drawn on a bar: an entry, an exit or a trigger.</summary>
public sealed record ChartMarker(long Time, string Position, string Shape, string Color, string Text);

/// <summary>Everything Chart.razor hands to ipChart.render.</summary>
public sealed record ChartPayload(string Symbol, IReadOnlyList<ChartBar> Bars, IReadOnlyList<ChartMarker> Markers);

/// <summary>
/// Maps candles and a backtest report into Lightweight Charts' series format. The library throws on
/// unsorted or duplicate times, so bars are ordered, de-duplicated (last wins) and invalid bars dropped,
/// and every marker is snapped onto the bar that contains it.
/// </summary>
public static class ChartSeries
{
    public static IReadOnlyList<ChartBar> Bars(IEnumerable<Candle> candles)
    {
        var byTime = new SortedDictionary<long, ChartBar>();
        foreach (var c in candles)
        {
            if (!c.IsValid) continue;
            long t = ToUnix(c.StartUtc);
            byTime[t] = new ChartBar(t, c.Open, c.High, c.Low, c.Close, c.Volume);
        }
        return byTime.Values.ToList();
    }

    public static IReadOnlyList<ChartMarker> Markers(BacktestReport? report, IReadOnlyList<ChartBar> bars)
    {
        if (report is null || bars.Count == 0) return [];
        var markers = new List<ChartMarker>();
        foreach (var trade in report.Trades)
        {
            bool isLong = trade.Direction == TradeDirection.Long;
            markers.Add(new ChartMarker(Snap(trade.EntryUtc, bars), isLong ? "belowBar" : "aboveBar",
                isLong ? "arrowUp" : "arrowDown", "#2970e8", $"{(isLong ? "Buy" : "Short")} {trade.EntryPrice:0.00##}"));
            foreach (var fill in trade.Exits)
                markers.Add(new ChartMarker(Snap(fill.Utc, bars), isLong ? "aboveBar" : "belowBar",
                    isLong ? "arrowDown" : "arrowUp", fill.Price >= trade.EntryPrice == isLong ? "#0f9d78" : "#e0483d",
                    $"Exit {fill.Price:0.00##}{(string.IsNullOrEmpty(fill.Reason) ? "" : " · " + fill.Reason)}"));
        }
        foreach (var t in report.Triggers.Where(t => !t.OpenedPosition))
            markers.Add(new ChartMarker(Snap(t.Utc, bars), "aboveBar", "circle", "#b07800", t.Condition));
        markers.Sort((a, b) => a.Time.CompareTo(b.Time));
        return markers;
    }

    public static ChartPayload Payload(string symbol, IEnumerable<Candle> candles, BacktestReport? report = null)
    {
        var bars = Bars(candles);
        return new ChartPayload(symbol, bars, Markers(report, bars));
    }

    private static long Snap(DateTime utc, IReadOnlyList<ChartBar> bars)
    {
        long t = ToUnix(utc);
        long snapped = bars[0].Time;
        foreach (var b in bars)
        {
            if (b.Time > t) break;
            snapped = b.Time;
        }
        return snapped;
    }

    private static long ToUnix(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
}
