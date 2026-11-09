using IdiotProof.Models;

namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// One population morning of the alarm sweep (IP-T470, IP-T475), window minutes only: index 0 is the
/// minute whose bar ends at <see cref="AlarmSweep.WindowStartEt"/>, the last the one ending at
/// <see cref="AlarmSweep.WindowEndEt"/>. Every series is read from bars ending at or before its minute.
/// </summary>
public sealed class AlarmMorning
{
    public required string Symbol { get; init; }
    public required DateOnly Day { get; init; }
    public required double Weight { get; init; }
    public required bool Sampled { get; init; }
    public required bool Reach20 { get; init; }
    /// <summary>Window-relative minute of the first +20% / +10% touch (bar high), int.MaxValue when none by the reach cut-off; negative before the window.</summary>
    public required int First20 { get; init; }
    public required int First10 { get; init; }
    /// <summary>Volume / trade-count CUSUM paths per drift in <see cref="AlarmSweep.Drifts"/>; null without an expectation.</summary>
    public required float[]?[] SVol { get; init; }
    public required float[]?[] STr { get; init; }
    public required float[] MaxSVol { get; init; }
    public required float[] MaxSTr { get; init; }
    public required float[] PmRet { get; init; }
    public required float[] Conf { get; init; }
    public required float[] CumDollar { get; init; }
    public required float[] VolRatio { get; init; }
    /// <summary>Fresh news (an item within <see cref="AlarmSweep.FreshNewsMinutes"/> of the minute); null when news was not loaded.</summary>
    public bool[]? News { get; init; }

    /// <summary>Still forming at the window start: reaches +20% by the cut-off, first touch after the 06:00 bar.</summary>
    public bool Forming => Reach20 && First20 > 0;

    /// <summary>Amendment 04's scorable morning: has a label and is still below +20% at 06:00 (every non-reaching morning, and every forming one).</summary>
    public bool Scorable => !Reach20 || First20 > 0;
}

/// <summary>Which within-morning score a rank rule sorts by.</summary>
public enum AlarmScoreKind { Composite, CusumVol, GainersNews }

/// <summary>
/// A rank-k alarm rule: among the morning's names not yet alarmed and below <see cref="GapHi"/>, the
/// <see cref="K"/> best by <see cref="Score"/> (weighted count of better-ranked eligible names below K)
/// alarm when the gap is at least <see cref="GapLo"/>, premarket dollar volume at least
/// <see cref="MinDollar"/>, and (with <see cref="NeedConf"/>) conf at least <see cref="MinConf"/>.
/// </summary>
public sealed record AlarmRankRule(AlarmScoreKind Score, int K, double GapLo, double GapHi, bool NeedConf,
    double MinDollar = AlarmSweep.MinPmDollarVolume, double MinConf = AlarmSweep.MinConf);

/// <summary>What one morning's alarm meant: alarmed, a false alarm, or a hit with its lead.</summary>
public readonly record struct AlarmOutcome(bool Alarmed, bool False, bool Hit, int Lead20, int? Lead10, double PmRetAtAlarm);

/// <summary>Per minute, each score's row order, best first (NaN scores left out), ties by row index.</summary>
public sealed class AlarmScoreTable
{
    public required double[][] Composite { get; init; }
    public required double[][] CusumVol { get; init; }
    public required double[][] GainersNews { get; init; }
    public required int[][] SortedComposite { get; init; }
    public required int[][] SortedCusumVol { get; init; }
    public required int[][] SortedGainersNews { get; init; }

    public int[][] Sorted(AlarmScoreKind kind) => kind switch
    {
        AlarmScoreKind.Composite => SortedComposite,
        AlarmScoreKind.CusumVol => SortedCusumVol,
        _ => SortedGainersNews,
    };
}

/// <summary>
/// The minute-by-minute alarm sweep shared by <c>explore alarms</c> (train exploration) and amendment
/// 04's confirmatory test (H25, H26), split-agnostic and pure: a morning's window series from its
/// minute bars (<see cref="Build"/>), the within-morning scores (<see cref="Scores"/>), the rank-k pass
/// logic (<see cref="RankAlarms"/>) and what an alarm meant (<see cref="Outcome"/>).
/// </summary>
public static class AlarmSweep
{
    public static readonly TimeSpan WindowStartEt = new(6, 0, 0);
    public static readonly TimeSpan WindowEndEt = new(9, 10, 0);

    /// <summary>The protocol's premarket dollar-volume floor at the alarm minute.</summary>
    public const double MinPmDollarVolume = 50_000;

    /// <summary>conf(t) floor of the rank rules: most of the gap has actually traded through.</summary>
    public const double MinConf = 0.5;

    /// <summary>Fresh news: an item published within this many minutes of t.</summary>
    public const double FreshNewsMinutes = 60;

    /// <summary>CUSUM drift grid k: a sustained 1.5x, 2x or 3x over the time-of-day expectation.</summary>
    public static readonly double[] Drifts = [Math.Log(1.5), Math.Log(2), Math.Log(3)];

    /// <summary>The drift the composite's CUSUM rank reads (ln 2).</summary>
    public const int DefaultDriftIndex = 1;

    /// <summary>Weight of a news-fresh name in the gainers + news score, so fresh news always ranks first.</summary>
    public const double NewsBoost = 1e6;

    public static int WindowStartIndex => OnsetSeries.IndexOf(WindowStartEt);
    public static int WindowEndIndex => OnsetSeries.IndexOf(WindowEndEt);
    public static int WindowMinutes => WindowEndIndex - WindowStartIndex + 1;

    /// <summary>
    /// One morning's window series. First touches follow the labeler's Reach20By0945 (bar highs of bars
    /// from 04:00 ending by <paramref name="reachEt"/>); null without a prior close or without any bar
    /// in that span. <paramref name="news"/> null leaves <see cref="AlarmMorning.News"/> null.
    /// </summary>
    public static AlarmMorning? Build(string symbol, DateOnly day, double weight, bool sampled, decimal priorClose, IReadOnlyList<Candle> bars,
        int? advDecile, double? adv20Shares, double? adt20Trades, IReadOnlyList<CatalystObservation>? news,
        TimeOfDayProfile profile, TimeOfDayProfile? tradeProfile, TimeSpan reachEt, double reachThreshold)
    {
        if (priorClose <= 0 || bars.Count == 0) return null;
        var start = MinuteBarMath.ToUtc(day, MinuteBarMath.SessionStartEt);
        var reachBy = MinuteBarMath.ToUtc(day, reachEt);
        int i0 = WindowStartIndex, i1 = WindowEndIndex, w = i1 - i0 + 1;

        bool any = false;
        int first20 = int.MaxValue, first10 = int.MaxValue;
        double prev = (double)priorClose;
        foreach (var b in bars)
        {
            if (b.StartUtc < start || b.EndUtc > reachBy) continue;
            any = true;
            int idx = (int)Math.Round((b.EndUtc - start).TotalMinutes) - 1 - i0;
            double high = (double)b.High;
            if (high / prev - 1 >= reachThreshold && idx < first20) first20 = idx;
            if (high / prev - 1 >= 0.10 && idx < first10) first10 = idx;
        }
        if (!any) return null;

        var series = OnsetTrack.Compute(new GapperFeatureInputs
        {
            Symbol = symbol, DateEt = day, DecisionUtc = MinuteBarMath.ToUtc(day, WindowEndEt), MinuteBars = bars, PriorClose = priorClose,
            Profile = profile, AdvDecile = advDecile, Adv20Shares = adv20Shares, Adt20Trades = adt20Trades, TradeProfile = tradeProfile,
        });
        if (series.Minutes <= i1) return null;
        float[] Window(double[] v) { var r = new float[w]; for (int i = 0; i < w; i++) r[i] = (float)v[i0 + i]; return r; }
        var sVol = new float[]?[Drifts.Length];
        var sTr = new float[]?[Drifts.Length];
        var maxVol = new float[Drifts.Length];
        var maxTr = new float[Drifts.Length];
        for (int d = 0; d < Drifts.Length; d++)
        {
            var pv = series.CusumVolume(Drifts[d]);
            var pt = series.CusumTrades(Drifts[d]);
            sVol[d] = pv is null ? null : Window(pv);
            sTr[d] = pt is null ? null : Window(pt);
            maxVol[d] = sVol[d] is { } a ? a.Max() : float.NaN;
            maxTr[d] = sTr[d] is { } b ? b.Max() : float.NaN;
        }
        bool[]? fresh = null;
        if (news is not null)
        {
            fresh = new bool[w];
            if (news.Count > 0)
                for (int i = 0; i < w; i++)
                {
                    var t = start.AddMinutes(OnsetSeries.EndMinute(i0 + i));
                    fresh[i] = GapperFormationFeatures.LatestCatalyst(news, t) is { } c && (t - c.PublishedUtc).TotalMinutes <= FreshNewsMinutes;
                }
        }
        return new AlarmMorning
        {
            Symbol = symbol, Day = day, Weight = weight, Sampled = sampled, Reach20 = first20 != int.MaxValue, First20 = first20, First10 = first10,
            SVol = sVol, STr = sTr, MaxSVol = maxVol, MaxSTr = maxTr,
            PmRet = Window(series.PmRet), Conf = Window(series.Conf), CumDollar = Window(series.CumDollar), VolRatio = Window(series.VolRatioTod), News = fresh,
        };
    }

    /// <summary>
    /// Per minute: composite = mean of the finite weighted percentile ranks of the time-of-day volume
    /// ratio, the gap so far and the ln 2 volume CUSUM; cusumVol = that CUSUM; gainers + news =
    /// <see cref="NewsBoost"/> when fresh news exists, plus the gap so far.
    /// </summary>
    public static AlarmScoreTable Scores(IReadOnlyList<AlarmMorning> rows)
    {
        int w = rows[0].PmRet.Length, n = rows.Count;
        var weights = rows.Select(r => r.Weight).ToList();
        var composite = new double[w][]; var cusumVol = new double[w][]; var gainers = new double[w][];
        var sComp = new int[w][]; var sCusum = new int[w][]; var sGain = new int[w][];
        int[] Order(double[] v) => Enumerable.Range(0, n).Where(r => double.IsFinite(v[r])).OrderByDescending(r => v[r]).ThenBy(r => r).ToArray();
        for (int i = 0; i < w; i++)
        {
            var volRank = CrossSectionalRank.PercentileRanks(rows.Select(r => (double)r.VolRatio[i]).ToList(), weights);
            var retRank = CrossSectionalRank.PercentileRanks(rows.Select(r => (double)r.PmRet[i]).ToList(), weights);
            var cusum = rows.Select(r => r.SVol[DefaultDriftIndex] is { } s ? (double)s[i] : double.NaN).ToArray();
            var cusumRank = CrossSectionalRank.PercentileRanks(cusum, weights);
            var comp = new double[n]; var gn = new double[n];
            for (int r = 0; r < n; r++)
            {
                double sum = 0; int cnt = 0;
                foreach (var v in new[] { volRank[r], retRank[r], cusumRank[r] }) if (double.IsFinite(v)) { sum += v; cnt++; }
                comp[r] = cnt > 0 ? sum / cnt : double.NaN;
                gn[r] = float.IsNaN(rows[r].PmRet[i]) ? double.NaN : (rows[r].News?[i] == true ? NewsBoost : 0) + rows[r].PmRet[i];
            }
            composite[i] = comp; cusumVol[i] = cusum; gainers[i] = gn;
            sComp[i] = Order(comp); sCusum[i] = Order(cusum); sGain[i] = Order(gn);
        }
        return new AlarmScoreTable { Composite = composite, CusumVol = cusumVol, GainersNews = gainers, SortedComposite = sComp, SortedCusumVol = sCusum, SortedGainersNews = sGain };
    }

    /// <summary>
    /// The first alarm minute of every row under a rank-k rule (-1 = none). Minute by minute, walk the
    /// rows best first; a row already alarmed or at or above the gap cap is skipped (not ranked); stop
    /// once the weighted count of better-ranked eligible rows reaches K; a ranked row alarms when it
    /// passes the gap floor, the dollar-volume floor, conf (when required) and <paramref name="gate"/>.
    /// </summary>
    public static int[] RankAlarms(IReadOnlyList<AlarmMorning> rows, int[][] sorted, AlarmRankRule rule, Func<AlarmMorning, int, bool>? gate = null)
    {
        int n = rows.Count, w = sorted.Length;
        var alarm = new int[n];
        Array.Fill(alarm, -1);
        for (int i = 0; i < w; i++)
        {
            double cum = 0;
            foreach (var r in sorted[i])
            {
                var row = rows[r];
                if (alarm[r] >= 0 || !(row.PmRet[i] < rule.GapHi)) continue;
                if (1 + cum > rule.K) break;
                bool pass = row.PmRet[i] >= rule.GapLo && row.CumDollar[i] >= rule.MinDollar
                    && (!rule.NeedConf || row.Conf[i] >= rule.MinConf)
                    && (gate is null || gate(row, i));
                cum += row.Weight;
                if (pass) alarm[r] = i;
            }
        }
        return alarm;
    }

    /// <summary>
    /// What an alarm at window minute <paramref name="alarm"/> (-1 = none) meant: a false alarm on a
    /// morning that never reaches +20% by the cut-off; a hit on a forming morning alarmed before its first
    /// +20% touch (lead in minutes, and to +10% when +10% came after the alarm); otherwise neither.
    /// </summary>
    public static AlarmOutcome Outcome(AlarmMorning row, int alarm)
    {
        if (alarm < 0) return default;
        if (!row.Reach20) return new AlarmOutcome(true, true, false, 0, null, row.PmRet[alarm]);
        if (row.Forming && alarm < row.First20)
            return new AlarmOutcome(true, false, true, row.First20 - alarm, row.First10 > alarm && row.First10 != int.MaxValue ? row.First10 - alarm : null, row.PmRet[alarm]);
        return new AlarmOutcome(true, false, false, 0, null, row.PmRet[alarm]);
    }
}
