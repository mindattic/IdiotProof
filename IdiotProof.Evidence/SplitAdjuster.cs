using IdiotProof.Models;

namespace IdiotProof.Evidence;

/// <summary>Split-adjusted bars plus how many splits inside the window were applied.</summary>
public sealed record SplitAdjustedSeries(IReadOnlyList<Candle> Bars, int SplitsApplied);

/// <summary>
/// Back-adjusts raw bars for the splits whose ex-date falls inside the window, so returns across a
/// split are real returns instead of a +900% reverse-split "jump". Only splits that happened by the
/// window's last bar are applied: a later split rescales every bar equally and cannot change a
/// return, and applying it would leak a future event into the past. Raw prices stay the source of
/// truth for price-level filters (the point-in-time under-$5 universe), so callers adjust only
/// when they compute returns.
/// </summary>
public static class SplitAdjuster
{
    public static SplitAdjustedSeries Adjust(IReadOnlyList<Candle> bars, IEnumerable<SplitEvent> splits, Func<Candle, DateOnly> dateOf)
    {
        if (bars.Count == 0) return new SplitAdjustedSeries(bars, 0);
        var ordered = bars.OrderBy(b => b.StartUtc).ToList();
        var first = dateOf(ordered[0]);
        var last = dateOf(ordered[^1]);
        var relevant = splits
            .Where(s => s.OldRate > 0 && s.NewRate > 0 && s.OldRate != s.NewRate && s.ExDate > first && s.ExDate <= last)
            .OrderBy(s => s.ExDate)
            .ToList();
        if (relevant.Count == 0) return new SplitAdjustedSeries(ordered, 0);

        var adjusted = new List<Candle>(ordered.Count);
        foreach (var bar in ordered)
        {
            var day = dateOf(bar);
            decimal price = 1m, volume = 1m;
            foreach (var s in relevant)
            {
                if (day >= s.ExDate) continue;
                price *= s.PriceFactor;
                volume /= s.PriceFactor;
            }
            adjusted.Add(price == 1m ? bar : new Candle
            {
                Symbol = bar.Symbol, StartUtc = bar.StartUtc, EndUtc = bar.EndUtc,
                Open = bar.Open * price, High = bar.High * price, Low = bar.Low * price, Close = bar.Close * price,
                Volume = bar.Volume * volume, Note = bar.Note,
            });
        }
        return new SplitAdjustedSeries(adjusted, relevant.Count);
    }
}
