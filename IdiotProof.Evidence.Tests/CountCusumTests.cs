using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Tests;

/// <summary>
/// The count CUSUM, the onset minute track and the cross-sectional ranks (IP-T470): an alarm on a
/// synthetic step, no lookahead (bars after t change nothing), the conf guard and the weighted ranks.
/// 2026-07-17 is EDT, so 04:00 ET is 08:00 UTC.
/// </summary>
public class CountCusumTests
{
    private static readonly DateOnly Day = new(2026, 7, 17);
    private static readonly DateTime SessionOpen = new(2026, 7, 17, 8, 0, 0, DateTimeKind.Utc);

    private static DetailedCandle Bar(DateTime startUtc, double close, double volume, long trades) => new()
    {
        Symbol = "T", StartUtc = startUtc, EndUtc = startUtc.AddMinutes(1),
        Open = (decimal)close, High = (decimal)close, Low = (decimal)close, Close = (decimal)close, Volume = (decimal)volume, TradeCount = trades,
    };

    /// <summary>Flat uniform profiles: every minute of the 16-hour day is 1/960 of the day.</summary>
    private static TimeOfDayProfile Uniform() =>
        new(Enumerable.Range(0, TimeOfDayProfile.DecileCount).Select(_ => (IReadOnlyList<double>)Enumerable.Range(0, TimeOfDayProfile.MinutesPerDay + 1).Select(m => (double)m / TimeOfDayProfile.MinutesPerDay).ToList()).ToList());

    private static GapperFeatureInputs Inputs(List<Candle> bars, DateTime t) => new()
    {
        Symbol = "T", DateEt = Day, DecisionUtc = t, MinuteBars = bars, PriorClose = 10m, Profile = Uniform(), TradeProfile = Uniform(),
        AdvDecile = 3, Adv20Shares = 960_000, Adt20Trades = 9_600,
    };

    [Test]
    public void Cusum_AlarmsOnASyntheticStep_AfterTheExpectedDelay()
    {
        // 30 normal minutes, then 5x the expectation: z = ln 5, net ln 5 - ln 2 = 0.916 a minute, h = 5 needs 6 minutes.
        var expected = Enumerable.Repeat(1000.0, 60).ToList();
        var observed = Enumerable.Range(0, 60).Select(i => i < 30 ? 1000.0 : 5000.0).ToList();
        var path = CountCusum.Path(observed, expected, pseudocount: 1e-9, CountCusum.DefaultDrift, CountCusum.DefaultCap);
        Assert.Multiple(() =>
        {
            Assert.That(path.Take(30).All(s => s == 0), Is.True, "no drift before the step");
            Assert.That(CountCusum.FirstAlarm(path, 5), Is.EqualTo(35));
            Assert.That(CountCusum.FirstAlarm(observed, expected, 1e-9, CountCusum.DefaultDrift, 5), Is.EqualTo(35));
            Assert.That(CountCusum.FirstAlarm(observed.Take(30).ToList(), expected.Take(30).ToList(), 1e-9, CountCusum.DefaultDrift, 5), Is.EqualTo(-1));
        });
    }

    [Test]
    public void Cusum_CapStopsOneBlockPrintAlarming()
    {
        var expected = Enumerable.Repeat(100.0, 10).ToList();
        var observed = Enumerable.Repeat(100.0, 10).ToList();
        observed[3] = 1_000_000; // one huge cross: capped at ln 20, minus ln 2 = 2.30 < h
        Assert.That(CountCusum.FirstAlarm(observed, expected, 1e-9, CountCusum.DefaultDrift, 3), Is.EqualTo(-1));
    }

    [Test]
    public void OnsetTrack_StepInVolumeAndTrades_AlarmsBothCharts()
    {
        // Expectation 1,000 shares and 10 prints a minute; from minute 60 the name trades 5x both.
        var bars = Enumerable.Range(0, 120).Select(i => (Candle)Bar(SessionOpen.AddMinutes(i), 10.5, i < 60 ? 1000 : 5000, i < 60 ? 10 : 50)).ToList();
        var series = OnsetTrack.Compute(Inputs(bars, SessionOpen.AddMinutes(120)));
        var vol = series.CusumVolume(CountCusum.DefaultDrift)!;
        var trades = series.CusumTrades(CountCusum.DefaultDrift)!;
        Assert.Multiple(() =>
        {
            Assert.That(series.Minutes, Is.EqualTo(120));
            Assert.That(CountCusum.FirstAlarm(vol, 4), Is.InRange(60, 66));
            Assert.That(CountCusum.FirstAlarm(trades, 4), Is.InRange(60, 66));
            Assert.That(series.PmRet[^1], Is.EqualTo(0.05).Within(1e-9));
            Assert.That(series.PmVwapRet[^1], Is.EqualTo(0.05).Within(1e-9));
            Assert.That(series.Conf[^1], Is.EqualTo(1).Within(1e-9));
        });
    }

    [Test]
    public void OnsetFeatures_AppendingBarsAfterT_ChangesNothing()
    {
        var t = SessionOpen.AddMinutes(90);
        var before = Enumerable.Range(0, 90).Select(i => (Candle)Bar(SessionOpen.AddMinutes(i), 10 + 0.01 * i, i < 60 ? 1000 : 4000, i < 60 ? 10 : 40)).ToList();
        var after = before.Concat(Enumerable.Range(90, 60).Select(i => (Candle)Bar(SessionOpen.AddMinutes(i), 15, 1_000_000, 5000))).ToList();
        var registry = new GapperFeatureRegistry().AddOnset(Uniform());
        var a = GapperFormationFeatures.Compute(Inputs(before, t), registry);
        var b = GapperFormationFeatures.Compute(Inputs(after, t), registry);
        var sa = OnsetTrack.Compute(Inputs(before, t));
        var sb = OnsetTrack.Compute(Inputs(after, t));
        Assert.Multiple(() =>
        {
            foreach (var name in OnsetFeatureNames.All)
                Assert.That(b.Extra.GetValueOrDefault(name, double.NaN), Is.EqualTo(a.Extra.GetValueOrDefault(name, double.NaN)), name);
            Assert.That(a.Extra.ContainsKey(OnsetFeatureNames.CusumVol), Is.True);
            Assert.That(sb.CusumVolume(CountCusum.DefaultDrift), Is.EqualTo(sa.CusumVolume(CountCusum.DefaultDrift)));
            Assert.That(sb.CusumTrades(CountCusum.DefaultDrift), Is.EqualTo(sa.CusumTrades(CountCusum.DefaultDrift)));
            Assert.That(sb.PmVwapRet, Is.EqualTo(sa.PmVwapRet));
            Assert.That(sb.CumDollar, Is.EqualTo(sa.CumDollar));
        });
    }

    [Test]
    public void Conf_IsGuardedAgainstAZeroGap()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OnsetTrack.Conf(0.02, 0), Is.Null);
            Assert.That(OnsetTrack.Conf(0.02, 0.0005), Is.Null);
            Assert.That(OnsetTrack.Conf(null, 0.1), Is.Null);
            Assert.That(OnsetTrack.Conf(0.05, 0.10), Is.EqualTo(0.5).Within(1e-12));
            Assert.That(OnsetTrack.Conf(1.0, 0.002), Is.EqualTo(OnsetTrack.ConfClamp));
        });
    }

    [Test]
    public void CrossSectionalRank_WeightsTheSample()
    {
        double[] values = [1, 2, 3, double.NaN];
        double[] weights = [1, 10, 1, 1];
        var ranks = CrossSectionalRank.PercentileRanks(values, weights);
        Assert.Multiple(() =>
        {
            Assert.That(ranks[0], Is.EqualTo(0.5 / 12).Within(1e-12));
            Assert.That(ranks[1], Is.EqualTo(6.0 / 12).Within(1e-12));
            Assert.That(ranks[2], Is.EqualTo(11.5 / 12).Within(1e-12));
            Assert.That(double.IsNaN(ranks[3]), Is.True);
            Assert.That(CrossSectionalRank.WeightedMedian(values, weights), Is.EqualTo(2));
            Assert.That(CrossSectionalRank.MarketNormalised(values, weights)[2], Is.EqualTo(1.5).Within(1e-12));
        });
    }
}
