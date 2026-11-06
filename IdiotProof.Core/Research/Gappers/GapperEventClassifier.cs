using IdiotProof.Core.Evidence;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;

namespace IdiotProof.Core.Research.Gappers;

/// <summary>One tier's touch: when the level was first reached and the premarket dollar volume printed by then.</summary>
public sealed record TierTouch(string Tier, double Threshold, bool Touched, bool Qualified, TimeOnly? TouchTimeEt, decimal? PmDollarVolumeAtTouch);

/// <summary>Everything the gapper rule says about one symbol-day's minute bars.</summary>
public sealed record GapperDayClassification(
    double GapMax,
    IReadOnlyList<TierTouch> Tiers,
    string? SubType,
    decimal PmHigh,
    TimeOnly? PmHighTimeEt,
    decimal? RthHigh,
    TimeOnly? HodTimeEt,
    decimal PmDollarVolume,
    int MinuteBars,
    double SipShare)
{
    public TierTouch? Tier(string name) => Tiers.FirstOrDefault(t => t.Tier == name);

    /// <summary>Highest tier whose touch met the premarket dollar-volume floor, or null.</summary>
    public string? HighestQualifiedTier => Tiers.Where(t => t.Qualified).OrderByDescending(t => t.Threshold).FirstOrDefault()?.Tier;

    public bool IsGapper => Tiers.Count > 0 && Tiers.OrderBy(t => t.Threshold).First().Qualified;
}

/// <summary>
/// The frozen gapper rule (protocol §Gapper day): from the extended-hours bars inside the gapper
/// window, <c>gapMax = max(high) / adjPrevClose - 1</c>, nested tiers, the premarket/open sub-type
/// from the bar that first touches the lowest tier, and the premarket dollar-volume floor per tier.
/// </summary>
public static class GapperEventClassifier
{
    public const string PremarketSubType = "premarket";
    public const string OpenSubType = "open";

    public static GapperDayClassification Classify(GapperStudyParams p, IReadOnlyList<Candle> dayBars, decimal adjPrevClose, DateOnly dateEt)
    {
        var g = p.Gapper;
        var windowStart = MinuteBarMath.ToUtc(dateEt, GapperStudyParams.ParseEt(g.Window.StartEt));
        var windowEnd = MinuteBarMath.ToUtc(dateEt, GapperStudyParams.ParseEt(g.Window.EndEt));
        var pmCutoff = MinuteBarMath.ToUtc(dateEt, GapperStudyParams.ParseEt(g.PremarketCutoffEt));
        var rthClose = MinuteBarMath.ToUtc(dateEt, new TimeSpan(16, 0, 0));

        var ordered = dayBars.Where(b => b.StartUtc >= windowStart).OrderBy(b => b.StartUtc).ToList();
        var window = ordered.Where(b => b.EndUtc <= windowEnd).ToList();
        double gapMax = window.Count > 0 && adjPrevClose > 0 ? (double)(window.Max(b => b.High) / adjPrevClose - 1m) : double.NegativeInfinity;
        if (window.Count == 0) gapMax = 0;

        var tiers = new List<TierTouch>();
        TierTouch? lowest = null;
        foreach (var (name, threshold) in g.OrderedTiers())
        {
            var level = adjPrevClose * (1m + (decimal)threshold);
            decimal pmDollar = 0;
            TierTouch touch = new(name, threshold, false, false, null, null);
            foreach (var b in window)
            {
                pmDollar += b.Close * b.Volume;
                if (b.High >= level && adjPrevClose > 0)
                {
                    touch = new TierTouch(name, threshold, true, pmDollar >= g.PremarketDollarVolumeFloor, TimeEt(b.StartUtc), pmDollar);
                    break;
                }
            }
            tiers.Add(touch);
            lowest ??= touch;
        }

        string? subType = null;
        if (lowest is { Touched: true, TouchTimeEt: { } touchTime })
            subType = MinuteBarMath.ToUtc(dateEt, touchTime.ToTimeSpan()).AddMinutes(1) <= pmCutoff ? PremarketSubType : OpenSubType;

        var pm = ordered.Where(b => b.EndUtc <= pmCutoff).ToList();
        var pmHighBar = pm.Count > 0 ? pm.MaxBy(b => b.High) : null;
        var rth = ordered.Where(b => b.StartUtc >= pmCutoff && b.EndUtc <= rthClose).ToList();
        var rthHighBar = rth.Count > 0 ? rth.MaxBy(b => b.High) : null;
        var session = ordered.Where(b => b.EndUtc <= rthClose).ToList();
        var hodBar = session.Count > 0 ? session.MaxBy(b => b.High) : null;

        return new GapperDayClassification(
            gapMax, tiers, subType,
            pmHighBar?.High ?? 0, pmHighBar is null ? null : TimeEt(pmHighBar.StartUtc),
            rthHighBar?.High, hodBar is null ? null : TimeEt(hodBar.StartUtc),
            pm.Sum(b => b.Close * b.Volume), ordered.Count,
            ordered.Count == 0 ? 0 : (double)ordered.Count(b => (b.Note ?? "").EndsWith(":sip", StringComparison.OrdinalIgnoreCase)) / ordered.Count);
    }

    public static TimeOnly TimeEt(DateTime utc) => TimeOnly.FromTimeSpan(MinuteBarMath.ToEastern(utc).TimeOfDay);
}
