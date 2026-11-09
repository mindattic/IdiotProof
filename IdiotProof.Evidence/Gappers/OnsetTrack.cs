using IdiotProof.Models;

namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// One symbol-day's premarket as clock-minute series from 04:00 ET up to the decision instant
/// (IP-T470): observed and expected volume and trade count for the count CUSUM, and the
/// volume-confirmed gap (methods-onset.md P4). Index i is the minute whose bar ends at 04:00 + (i + 1)
/// minutes; a minute with no bar has zero volume and no prints and carries the last close. Only bars
/// ending at or before t are read, so appending later bars changes nothing. NaN = undefined.
/// </summary>
public sealed class OnsetSeries
{
    public required DateOnly DateEt { get; init; }
    public required double[] Volume { get; init; }
    public required double[] Trades { get; init; }
    /// <summary>ADV20 x the profile's share for the minute; null without a profile, a decile or ADV.</summary>
    public double[]? ExpectedVolume { get; init; }
    /// <summary>ADT20 x the trade profile's share for the minute; null without a trade profile or ADT.</summary>
    public double[]? ExpectedTrades { get; init; }
    public double VolumePseudocount { get; init; }
    public double TradePseudocount { get; init; } = CountCusum.TradePseudocount;
    /// <summary>Last close over the prior close, minus 1 (a fraction).</summary>
    public required double[] PmRet { get; init; }
    /// <summary>Premarket VWAP so far over the prior close, minus 1 (a fraction).</summary>
    public required double[] PmVwapRet { get; init; }
    /// <summary><see cref="PmVwapRet"/> / <see cref="PmRet"/>: how much of the gap has traded through (guarded, see <see cref="OnsetTrack.Conf"/>).</summary>
    public required double[] Conf { get; init; }
    /// <summary>Cumulative volume over what the profile expects by this minute (pmVolRatio).</summary>
    public required double[] VolRatioTod { get; init; }
    /// <summary>Premarket dollar volume so far.</summary>
    public required double[] CumDollar { get; init; }
    /// <summary>Share of the dollar volume so far traded above prior close x 1.10.</summary>
    public required double[] DollarShareAbove10 { get; init; }

    public int Minutes => Volume.Length;

    /// <summary>Minutes after 04:00 ET at which minute <paramref name="index"/>'s bar ends.</summary>
    public static int EndMinute(int index) => index + 1;

    /// <summary>The index of the minute whose bar ends at <paramref name="timeEt"/>.</summary>
    public static int IndexOf(TimeSpan timeEt) => (int)Math.Round((timeEt - MinuteBarMath.SessionStartEt).TotalMinutes) - 1;

    /// <summary>The volume CUSUM path for drift k; null without an expectation.</summary>
    public double[]? CusumVolume(double drift, double? cap = null) =>
        ExpectedVolume is null ? null : CountCusum.Path(Volume, ExpectedVolume, VolumePseudocount, drift, cap ?? CountCusum.DefaultCap);

    /// <summary>The trade-count CUSUM path for drift k; null without an expectation.</summary>
    public double[]? CusumTrades(double drift, double? cap = null) =>
        ExpectedTrades is null ? null : CountCusum.Path(Trades, ExpectedTrades, TradePseudocount, drift, cap ?? CountCusum.DefaultCap);
}

public static class OnsetTrack
{
    /// <summary>The dollar-share feature counts trading above prior close x (1 + this).</summary>
    public const double ConfirmedAboveFraction = 0.10;

    /// <summary><see cref="Conf"/> is undefined when |pmRet| is below this (no gap to confirm, and no division by ~zero).</summary>
    public const double ConfMinAbsGap = 1e-3;

    /// <summary><see cref="Conf"/> is clamped to +/- this so a near-zero gap cannot produce a huge ratio.</summary>
    public const double ConfClamp = 10;

    /// <summary>pmVwapRet / pmRet; null when either is missing or |pmRet| &lt; <see cref="ConfMinAbsGap"/>.</summary>
    public static double? Conf(double? pmVwapRet, double? pmRet)
    {
        if (pmVwapRet is not { } v || pmRet is not { } r || !double.IsFinite(v) || !double.IsFinite(r) || Math.Abs(r) < ConfMinAbsGap) return null;
        return Math.Clamp(v / r, -ConfClamp, ConfClamp);
    }

    /// <summary>The price a bar's dollar volume is valued at: its own VWAP when the feed gave one, else the typical price.</summary>
    public static double BarPrice(Candle b) => b is DetailedCandle { Vwap: > 0 } d ? (double)d.Vwap : (double)(b.High + b.Low + b.Close) / 3.0;

    /// <summary>
    /// ADT20 for the trade-count baseline: the given value, else the mean trade count of the last 20
    /// daily bars before D that carry one, else the mean full-day print count of the prior days' minute
    /// bars; null when none is known.
    /// </summary>
    public static double? ResolveAdt20(GapperFeatureInputs inputs)
    {
        if (inputs.Adt20Trades is { } given) return given > 0 ? given : null;
        var counts = inputs.DailyBars
            .Where(d => DateOnly.FromDateTime(MinuteBarMath.ToEastern(d.StartUtc)) < inputs.DateEt && d is DetailedCandle { TradeCount: > 0 })
            .Select(d => (double)((DetailedCandle)d).TradeCount).TakeLast(20).ToList();
        if (counts.Count > 0) return counts.Average();
        var prior = inputs.PriorDayMinuteBars.Where(day => day.Count > 0).Select(day => (double)day.Sum(GapperFormationFeatures.Trades)).ToList();
        return prior.Count > 0 && prior.Average() > 0 ? prior.Average() : null;
    }

    private static double? Adv20(GapperFeatureInputs inputs)
    {
        if (inputs.Adv20Shares is { } given) return given;
        var daily = inputs.DailyBars.Where(d => DateOnly.FromDateTime(MinuteBarMath.ToEastern(d.StartUtc)) < inputs.DateEt).TakeLast(20).ToList();
        return daily.Count > 0 ? (double)daily.Average(d => d.Volume) : null;
    }

    /// <summary>
    /// The minute series from 04:00 ET through the decision instant. <paramref name="tradeProfile"/>
    /// overrides <see cref="GapperFeatureInputs.TradeProfile"/>.
    /// </summary>
    public static OnsetSeries Compute(GapperFeatureInputs inputs, TimeOfDayProfile? tradeProfile = null)
    {
        var t = inputs.DecisionUtc;
        var start = MinuteBarMath.ToUtc(inputs.DateEt, MinuteBarMath.SessionStartEt);
        int n = Math.Clamp((int)Math.Floor((t - start).TotalMinutes), 0, TimeOfDayProfile.MinutesPerDay);
        var volume = new double[n];
        var trades = new double[n];
        var dollar = new double[n];
        var above = new double[n];
        var close = new double[n];
        Array.Fill(close, double.NaN);
        var lastEnd = new DateTime[n];
        var prior = GapperFormationFeatures.PriorCloseOf(inputs) is { } pc && pc > 0 ? (double)pc : double.NaN;
        double aboveLevel = prior * (1 + ConfirmedAboveFraction);
        foreach (var b in inputs.MinuteBars)
        {
            if (b.EndUtc <= start || b.EndUtc > t) continue;
            int i = (int)Math.Round((b.EndUtc - start).TotalMinutes) - 1;
            if (i < 0 || i >= n) continue;
            double v = (double)b.Volume;
            double px = BarPrice(b);
            volume[i] += v;
            trades[i] += GapperFormationFeatures.Trades(b);
            dollar[i] += v * px;
            if (px > aboveLevel) above[i] += v * px;
            if (b.EndUtc >= lastEnd[i]) { lastEnd[i] = b.EndUtc; close[i] = (double)b.Close; }
        }

        var adv = Adv20(inputs);
        var decile = inputs.AdvDecile;
        var profile = inputs.Profile;
        var trProfile = tradeProfile ?? inputs.TradeProfile;
        var adt = trProfile is null ? null : ResolveAdt20(inputs);
        double[]? expectedVolume = profile is not null && decile is not null && adv is > 0 ? new double[n] : null;
        double[]? expectedTrades = trProfile is not null && decile is not null && adt is > 0 ? new double[n] : null;

        var pmRet = new double[n]; var pmVwapRet = new double[n]; var conf = new double[n];
        var ratio = new double[n]; var cumDollar = new double[n]; var share = new double[n];
        double cumVol = 0, cumDol = 0, cumAbove = 0, last = double.NaN;
        for (int i = 0; i < n; i++)
        {
            var timeEt = MinuteBarMath.SessionStartEt + TimeSpan.FromMinutes(OnsetSeries.EndMinute(i));
            if (expectedVolume is not null) expectedVolume[i] = adv!.Value * profile!.MinuteFractionAt(decile!.Value, timeEt);
            if (expectedTrades is not null) expectedTrades[i] = adt!.Value * trProfile!.MinuteFractionAt(decile!.Value, timeEt);
            if (!double.IsNaN(close[i])) last = close[i];
            cumVol += volume[i]; cumDol += dollar[i]; cumAbove += above[i];
            cumDollar[i] = cumDol;
            pmRet[i] = double.IsNaN(last) || double.IsNaN(prior) ? double.NaN : last / prior - 1;
            pmVwapRet[i] = cumVol > 0 && !double.IsNaN(prior) ? cumDol / cumVol / prior - 1 : double.NaN;
            conf[i] = Conf(pmVwapRet[i], pmRet[i]) ?? double.NaN;
            share[i] = cumDol > 0 && !double.IsNaN(prior) ? cumAbove / cumDol : double.NaN;
            ratio[i] = profile is not null && decile is { } d2 && adv is > 0 && profile.VolRatioTod(cumVol, adv.Value, d2, timeEt) is { } r ? r : double.NaN;
        }
        return new OnsetSeries
        {
            DateEt = inputs.DateEt, Volume = volume, Trades = trades, ExpectedVolume = expectedVolume, ExpectedTrades = expectedTrades,
            VolumePseudocount = adv is > 0 ? adv.Value * CountCusum.VolumePseudocountFraction : 1,
            PmRet = pmRet, PmVwapRet = pmVwapRet, Conf = conf, VolRatioTod = ratio, CumDollar = cumDollar, DollarShareAbove10 = share,
        };
    }
}
