using IdiotProof.Evidence.Economics;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// The one feature computation for a forming gapper, shared by the live recorder and the study.
/// Pure: no clocks, no I/O. It trims every input to what ended at or before the decision instant,
/// forward-fills missing minutes as zero-volume bars so windows are clock minutes, and never reads
/// past t — a unit test appends post-t bars and asserts the snapshot is unchanged.
/// </summary>
public static class GapperFormationFeatures
{
    public static GapperFormationSnapshot Compute(GapperFeatureInputs inputs, GapperFeatureRegistry? registry = null)
    {
        var t = inputs.DecisionUtc;
        var sessionStart = MinuteBarMath.ToUtc(inputs.DateEt, MinuteBarMath.SessionStartEt);
        var bars = MinuteBarMath.ForwardFill(inputs.MinuteBars, sessionStart, t);
        var daily = inputs.DailyBars.Where(d => DateOnly.FromDateTime(MinuteBarMath.ToEastern(d.StartUtc)) < inputs.DateEt).ToList();
        var priorClose = inputs.PriorClose ?? (daily.Count > 0 ? daily[^1].Close : null);
        var adv20 = inputs.Adv20Shares ?? Adv(daily, 20);
        var timeEt = MinuteBarMath.ToEastern(t).TimeOfDay;

        var last = bars.Count > 0 ? bars[^1] : null;
        double pmCumVol = (double)bars.Sum(b => b.Volume);
        decimal pmHigh = bars.Count > 0 ? bars.Max(b => b.High) : 0;

        double? volRatioTod = inputs.Profile is { } profile && inputs.AdvDecile is { } decile && adv20 is { } adv
            ? profile.VolRatioTod(pmCumVol, adv, decile, timeEt)
            : null;

        var core = new GapperFormationSnapshot
        {
            Symbol = inputs.Symbol,
            DateEt = inputs.DateEt,
            DecisionUtc = t,
            GapPct = last is not null && priorClose is > 0 ? (double)(last.Close / priorClose.Value - 1m) * 100.0 : null,
            PmCumVol = pmCumVol,
            VolRatioTod = volRatioTod,
            OwnProfileRatio = OwnProfileRatio(inputs, pmCumVol, timeEt, adv20),
            Velocity1m = MinuteBarMath.Velocity(bars, t, 1),
            Velocity3m = MinuteBarMath.Velocity(bars, t, 3),
            Velocity5m = MinuteBarMath.Velocity(bars, t, 5),
            Velocity15m = MinuteBarMath.Velocity(bars, t, 15),
            Acceleration = MinuteBarMath.Acceleration(bars, t),
            VolumeRoc = MinuteBarMath.VolumeRoc(bars, t),
            TradesProxy = (int)Math.Min(int.MaxValue, TradesBetween(bars, t.AddMinutes(-inputs.TradesProxyWindowMinutes), t)),
            SpreadProxy = SpreadProxy(bars),
            DistToPmHighPct = last is not null && pmHigh > 0 ? (double)((pmHigh - last.Close) / pmHigh) * 100.0 : null,
            MinsSinceFirstAbnormal = MinsSinceFirstAbnormal(inputs, bars, sessionStart, adv20),
            SessionVwap = (double?)MinuteBarMath.SessionVwap(bars, t),
            AbnormalReturnD1 = AbnormalReturn(daily, 1),
            AbnormalReturnD2 = AbnormalReturn(daily, 2),
            AbnormalReturnD3 = AbnormalReturn(daily, 3),
            AbnormalReturnD4 = AbnormalReturn(daily, 4),
            AbnormalReturnD5 = AbnormalReturn(daily, 5),
            VolumeRatioD1 = VolumeRatio(daily, 1),
            VolumeRatioD2 = VolumeRatio(daily, 2),
            VolumeRatioD3 = VolumeRatio(daily, 3),
            VolumeRatioD4 = VolumeRatio(daily, 4),
            VolumeRatioD5 = VolumeRatio(daily, 5),
            DaysSinceReverseSplit = DaysSince(inputs.DateEt, inputs.CorporateActions?.ReverseSplit),
            DaysSince424B = DaysSince(inputs.DateEt, inputs.CorporateActions?.Offering424B, LatestFiling(inputs, t, "424B")),
            DaysSinceS1 = DaysSince(inputs.DateEt, inputs.CorporateActions?.S1, LatestFiling(inputs, t, "S-1")),
            Halted = inputs.Halted,
        };

        var catalyst = LatestCatalyst(inputs.Catalysts, t);
        if (catalyst is not null)
            core = core with
            {
                CatalystScore = catalyst.Score,
                CatalystAgeMinutes = (t - catalyst.PublishedUtc).TotalMinutes,
                CatalystDirection = catalyst.Direction,
                CatalystPreClass = catalyst.PreClass,
                CatalystStampIsUpperBound = catalyst.StampIsUpperBound,
            };

        var extra = new Dictionary<string, double>((registry ?? GapperFeatureRegistry.Default()).Compute(inputs, core), StringComparer.Ordinal);
        if (TradesRoc(bars, t, inputs.TradesRocWindowMinutes) is { } roc) extra[GapperExtraKeys.TradesRoc] = roc;
        if (catalyst?.LlmSentiment is { } sentiment) extra[GapperExtraKeys.CatalystSentiment] = sentiment;
        if (Form4Buys(inputs, t) is { } buys)
        {
            extra[GapperExtraKeys.Form4BuysD30] = buys.Filings;
            extra[GapperExtraKeys.Form4BuyDollarsD30] = buys.Dollars;
        }
        if (LatestEightK(inputs, t) is { } eightK)
        {
            extra[GapperExtraKeys.EightKItemCount] = eightK.Items.Count;
            foreach (var item in eightK.Items) extra[GapperExtraKeys.EightKItemKey(item)] = 1;
        }
        return core with { Extra = extra };
    }

    /// <summary>Trades printed by bars ending in (fromUtc, toUtc]: the feed's trade count when a bar carries one, else one per bar with volume.</summary>
    public static long TradesBetween(IReadOnlyList<Candle> bars, DateTime fromUtc, DateTime toUtc)
    {
        long sum = 0;
        foreach (var b in bars)
            if (b.EndUtc > fromUtc && b.EndUtc <= toUtc) sum += Trades(b);
        return sum;
    }

    public static long Trades(Candle b) => b is DetailedCandle { TradeCount: > 0 } d ? d.TradeCount : b.Volume > 0 ? 1 : 0;

    /// <summary>trades_k(t) / trades_k(t - k) - 1; null when the earlier window printed no trade.</summary>
    public static double? TradesRoc(IReadOnlyList<Candle> bars, DateTime nowUtc, int windowMinutes)
    {
        if (windowMinutes <= 0) return null;
        var recent = TradesBetween(bars, nowUtc.AddMinutes(-windowMinutes), nowUtc);
        var prior = TradesBetween(bars, nowUtc.AddMinutes(-2 * windowMinutes), nowUtc.AddMinutes(-windowMinutes));
        return prior > 0 ? (double)recent / prior - 1 : null;
    }

    /// <summary>The prior close the gap is measured from: the given one, else the last daily close before D.</summary>
    public static decimal? PriorCloseOf(GapperFeatureInputs inputs)
    {
        if (inputs.PriorClose is { } given) return given;
        var daily = inputs.DailyBars.Where(d => DateOnly.FromDateTime(MinuteBarMath.ToEastern(d.StartUtc)) < inputs.DateEt).ToList();
        return daily.Count > 0 ? daily[^1].Close : null;
    }

    private static (int Filings, double Dollars)? Form4Buys(GapperFeatureInputs inputs, DateTime t)
    {
        if (inputs.Form4Filings is null) return null;
        var from = MinuteBarMath.ToUtc(inputs.DateEt.AddDays(-inputs.Form4LookbackDays), TimeSpan.Zero);
        int filings = 0;
        double dollars = 0;
        foreach (var f in inputs.Form4Filings)
        {
            if (f.AcceptedUtc < from || f.AcceptedUtc > t || f.PurchaseLines <= 0) continue;
            filings++;
            dollars += f.PurchaseDollars;
        }
        return (filings, dollars);
    }

    private static FilingObservation? LatestEightK(GapperFeatureInputs inputs, DateTime t)
    {
        var from = t.AddHours(-inputs.CatalystLookbackHours);
        return inputs.Filings
            .Where(f => f.AcceptedUtc <= t && f.AcceptedUtc >= from && f.FormType.StartsWith("8-K", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.AcceptedUtc)
            .FirstOrDefault();
    }

    /// <summary>Items within this many minutes of each other are one release seen through several copies (a Benzinga flash and its batch-stamped wire copy).</summary>
    public static readonly TimeSpan CatalystClusterWindow = TimeSpan.FromMinutes(60);

    /// <summary>
    /// The catalyst in force at t: the newest item known by t, except that every item inside the
    /// cluster window before it is the same release, so the earliest of them is the catalyst time
    /// (a batch stamp is only an upper bound; the earlier real-time flash is the truth).
    /// </summary>
    public static CatalystObservation? LatestCatalyst(IReadOnlyList<CatalystObservation> catalysts, DateTime t)
    {
        var known = catalysts.Where(c => c.PublishedUtc <= t).OrderByDescending(c => c.PublishedUtc).ToList();
        if (known.Count == 0) return null;
        var newest = known[0];
        var cluster = known.Where(c => c.PublishedUtc >= newest.PublishedUtc - CatalystClusterWindow).ToList();
        return cluster.OrderBy(c => c.PublishedUtc).ThenBy(c => c.StampIsUpperBound).First();
    }

    private static double? Adv(IReadOnlyList<Candle> daily, int days)
    {
        if (daily.Count == 0) return null;
        int from = Math.Max(0, daily.Count - days);
        decimal sum = 0;
        for (int i = from; i < daily.Count; i++) sum += daily[i].Volume;
        return (double)(sum / (daily.Count - from));
    }

    private static double? SpreadProxy(List<Candle> bars)
    {
        var traded = bars.Where(b => b.Volume > 0).TakeLast(10).ToList();
        if (traded.Count < 2) return null;
        var s = TradingCosts.CorwinSchultzSpread(traded);
        return double.IsNaN(s) ? null : s;
    }

    private static double? OwnProfileRatio(GapperFeatureInputs inputs, double pmCumVol, TimeSpan timeEt, double? adv20)
    {
        var own = new List<double>();
        foreach (var day in inputs.PriorDayMinuteBars)
        {
            if (day.Count == 0) continue;
            var dayEt = MinuteBarMath.SessionDate(day[0].EndUtc);
            var cutoff = MinuteBarMath.ToUtc(dayEt, timeEt);
            own.Add((double)day.Where(b => b.EndUtc <= cutoff).Sum(b => b.Volume));
        }
        if (own.Count == 0) return null;

        double? priorExpected = inputs.Profile is { } profile && inputs.AdvDecile is { } decile && adv20 is { } adv
            ? adv * profile.FractionAt(decile, timeEt)
            : null;
        const int priorWeight = 5;
        var expected = priorExpected is { } pe
            ? (own.Sum() + priorWeight * pe) / (own.Count + priorWeight)
            : own.Average();
        return expected > 0 ? pmCumVol / expected : null;
    }

    private static double? MinsSinceFirstAbnormal(GapperFeatureInputs inputs, List<Candle> bars, DateTime sessionStart, double? adv20)
    {
        if (inputs.Profile is not { } profile || inputs.AdvDecile is not { } decile || adv20 is not { } adv) return null;
        foreach (var b in bars)
        {
            var minuteEt = MinuteBarMath.ToEastern(b.EndUtc).TimeOfDay;
            var expected = adv * profile.MinuteFractionAt(decile, minuteEt);
            if (expected > 0 && (double)b.Volume >= inputs.AbnormalVolumeMultiple * expected)
                return (inputs.DecisionUtc - b.EndUtc).TotalMinutes;
        }
        return null;
    }

    /// <summary>Return of day D-k minus the mean daily return of the 20 sessions before it, in percent.</summary>
    private static double? AbnormalReturn(List<Candle> daily, int k)
    {
        int idx = daily.Count - k;
        if (idx < 2) return null;
        var ret = Return(daily, idx);
        if (ret is not { } r) return null;
        var baseline = new List<double>();
        for (int i = Math.Max(1, idx - 20); i < idx; i++)
            if (Return(daily, i) is { } b) baseline.Add(b);
        return baseline.Count == 0 ? null : r - baseline.Average();
    }

    private static double? Return(List<Candle> daily, int idx) =>
        idx >= 1 && daily[idx - 1].Close > 0 ? (double)(daily[idx].Close / daily[idx - 1].Close - 1m) * 100.0 : null;

    /// <summary>Volume of day D-k over the mean volume of the 20 sessions before it.</summary>
    private static double? VolumeRatio(List<Candle> daily, int k)
    {
        int idx = daily.Count - k;
        if (idx < 1) return null;
        decimal sum = 0;
        int n = 0;
        for (int i = Math.Max(0, idx - 20); i < idx; i++) { sum += daily[i].Volume; n++; }
        return n > 0 && sum > 0 ? (double)(daily[idx].Volume / (sum / n)) : null;
    }

    private static DateOnly? LatestFiling(GapperFeatureInputs inputs, DateTime t, string formPrefix)
    {
        var filing = inputs.Filings
            .Where(f => f.AcceptedUtc <= t && f.FormType.StartsWith(formPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.AcceptedUtc)
            .FirstOrDefault();
        return filing is null ? null : DateOnly.FromDateTime(MinuteBarMath.ToEastern(filing.AcceptedUtc));
    }

    private static int? DaysSince(DateOnly date, params DateOnly?[] candidates)
    {
        DateOnly? latest = null;
        foreach (var c in candidates)
            if (c is { } d && d <= date && (latest is null || d > latest)) latest = d;
        return latest is { } l ? date.DayNumber - l.DayNumber : null;
    }
}
