using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Tests;

/// <summary>
/// IP-T442 A2 feature completion: trade-count proxy and rate of change from bar trade counts, 8-K
/// items inside the catalyst lookback, LLM sentiment, Form 4 purchases over the lookback (null without a
/// CIK), and the post-registration velocity-death-near-PM-high variant with its registry flag.
/// 2026-07-17 is EDT: 04:00 ET = 08:00 UTC.
/// </summary>
public class GapperFeatureCompletionTests
{
    private static readonly DateOnly Day = new(2026, 7, 17);
    private static DateTime Et(int h, int m) => MinuteBarMath.ToUtc(Day, new TimeSpan(h, m, 0));

    private static DetailedCandle Bar(int h, int m, double close, double volume, int trades, double? high = null) => new()
    {
        Symbol = "T", StartUtc = Et(h, m), EndUtc = Et(h, m).AddMinutes(1),
        Open = (decimal)close, High = (decimal)(high ?? close), Low = (decimal)close, Close = (decimal)close, Volume = (decimal)volume, TradeCount = trades,
    };

    [Test]
    public void TradesProxy_SumsBarTradeCounts_AndTradesRoc_ComparesFiveMinuteWindows()
    {
        var bars = new List<Candle>();
        for (int m = 0; m < 20; m++) bars.Add(Bar(7, m, 10, 100, m < 15 ? 4 : 2));
        var t = Et(7, 20);
        var snap = GapperFormationFeatures.Compute(new GapperFeatureInputs { Symbol = "T", DateEt = Day, DecisionUtc = t, MinuteBars = bars, PriorClose = 8m });
        Assert.Multiple(() =>
        {
            Assert.That(snap.TradesProxy, Is.EqualTo(10 * 4 + 5 * 2), "bars ending 07:06..07:20: ten with 4 trades, five with 2");
            Assert.That(snap.Extra[GapperExtraKeys.TradesRoc], Is.EqualTo(10.0 / 20.0 - 1).Within(1e-12), "last five minutes (2 each) over the five before them (4 each)");
            Assert.That(GapperFormationFeatures.TradesRoc(bars, Et(7, 5), 5), Is.Null, "the earlier window printed nothing");
        });
    }

    [Test]
    public void TradesProxy_FallsBackToBarsWithVolume_WhenNoTradeCountIsCarried()
    {
        var bars = new List<Candle>();
        for (int m = 0; m < 10; m++) bars.Add(new Candle { Symbol = "T", StartUtc = Et(7, m), EndUtc = Et(7, m).AddMinutes(1), Open = 10, High = 10, Low = 10, Close = 10, Volume = m % 2 == 0 ? 50 : 0 });
        var snap = GapperFormationFeatures.Compute(new GapperFeatureInputs { Symbol = "T", DateEt = Day, DecisionUtc = Et(7, 10), MinuteBars = bars, PriorClose = 8m });
        Assert.That(snap.TradesProxy, Is.EqualTo(5));
    }

    [Test]
    public void EightKItems_Sentiment_AndForm4Buys_FollowTheirWindows()
    {
        var t = Et(8, 0);
        var bars = new List<Candle> { Bar(7, 59, 10, 100, 3) };
        var filings = new List<FilingObservation>
        {
            new("8-K", t.AddHours(-30), ["7.01"]),
            new("8-K", t.AddHours(-2), ["2.02", "9.01"]),
            new("424B4", t.AddHours(-1), []),
            new("8-K", t.AddMinutes(5), ["1.01"]),
        };
        var form4 = new List<Form4FilingObservation>
        {
            new(MinuteBarMath.ToUtc(Day.AddDays(-40), new TimeSpan(16, 0, 0)), 2, 50_000),
            new(MinuteBarMath.ToUtc(Day.AddDays(-10), new TimeSpan(16, 0, 0)), 1, 12_000),
            new(MinuteBarMath.ToUtc(Day.AddDays(-3), new TimeSpan(16, 0, 0)), 0, 0),
            new(t.AddHours(1), 3, 99_000),
        };
        var catalysts = new List<CatalystObservation> { new(t.AddMinutes(-30), "Fda", 1, 1, 80, false, 1) };
        var inputs = new GapperFeatureInputs { Symbol = "T", DateEt = Day, DecisionUtc = t, MinuteBars = bars, PriorClose = 8m, Filings = filings, Form4Filings = form4, Catalysts = catalysts, Form4LookbackDays = 30 };
        var snap = GapperFormationFeatures.Compute(inputs);
        var noCik = GapperFormationFeatures.Compute(inputs with { Form4Filings = null });
        var unclassified = GapperFormationFeatures.Compute(inputs with { Catalysts = [new CatalystObservation(t.AddMinutes(-30), "Fda", 1, 1, 80)] });
        Assert.Multiple(() =>
        {
            Assert.That(GapperExtraKeys.EightKItems(snap.Extra), Is.EqualTo("2.02,9.01"), "the latest 8-K accepted inside the 24h lookback, never the one after t");
            Assert.That(snap.Extra[GapperExtraKeys.EightKItemCount], Is.EqualTo(2));
            Assert.That(snap.Extra[GapperExtraKeys.EightKItemKey("9.01")], Is.EqualTo(1));
            Assert.That(snap.Extra[GapperExtraKeys.CatalystSentiment], Is.EqualTo(1));
            Assert.That(unclassified.Extra.ContainsKey(GapperExtraKeys.CatalystSentiment), Is.False);
            Assert.That(snap.Extra[GapperExtraKeys.Form4BuysD30], Is.EqualTo(1), "one filing inside [D-30, t] with a purchase line; the 40-day-old one and the post-t one are out, the no-purchase one does not count");
            Assert.That(snap.Extra[GapperExtraKeys.Form4BuyDollarsD30], Is.EqualTo(12_000));
            Assert.That(noCik.Extra.ContainsKey(GapperExtraKeys.Form4BuysD30), Is.False, "no CIK mapping: absent, not zero");
            Assert.That(noCik.Extra.ContainsKey(GapperExtraKeys.Form4BuyDollarsD30), Is.False);
            Assert.That(GapperExtraKeys.EightKItems(new Dictionary<string, double>()), Is.Null);
            Assert.That(snap.Extra.ContainsKey("overnightReturnD1"), Is.False, "no daily bars: the registry add-on stays absent, the protocol extras ride alongside");
        });
    }

    [Test]
    public void VelocityDeathNearPmHigh_MeasuresMinutesFromTheHigh_AfterAQualifyingRun()
    {
        var bars = new List<Candle>();
        double close = 10;
        for (int m = 0; m < 30; m++) { close = 10 + 0.1 * (m + 1); bars.Add(Bar(7, 30 + m, close, 100, 3)); }
        for (int m = 0; m < 20; m++) { close -= 0.1; bars.Add(Bar(8, m, close, 100, 3)); }
        var feature = new VelocityDeathNearPmHighFeature(0.20, 30, 3, 2);
        var core = new GapperFormationSnapshot { Symbol = "T", DateEt = Day, DecisionUtc = Et(9, 45) };
        var qualifying = feature.Compute(new GapperFeatureInputs { Symbol = "T", DateEt = Day, DecisionUtc = Et(9, 45), MinuteBars = bars, PriorClose = 10m }, core);
        var smallRun = feature.Compute(new GapperFeatureInputs { Symbol = "T", DateEt = Day, DecisionUtc = Et(9, 45), MinuteBars = bars, PriorClose = 12.5m }, core);
        var beforeDeath = feature.Compute(new GapperFeatureInputs { Symbol = "T", DateEt = Day, DecisionUtc = Et(8, 1), MinuteBars = bars, PriorClose = 10m }, core);
        Assert.Multiple(() =>
        {
            Assert.That(qualifying, Is.EqualTo(4.0).Within(1e-9), "high bar starts 07:59; v3 is negative at the 08:02 and 08:03 reads");
            Assert.That(smallRun, Is.Null, "13.00 over 12.50 is a 4% run, under the 20% tier");
            Assert.That(beforeDeath, Is.Null, "nothing ending after t is read");
        });
    }

    [Test]
    public void Registry_FlagsPostRegistrationFeatures()
    {
        var registry = GapperFeatureRegistry.Default().Add(new VelocityDeathNearPmHighFeature(0.2), postRegistration: true);
        Assert.Multiple(() =>
        {
            Assert.That(registry.IsPostRegistration(VelocityDeathNearPmHighFeature.FeatureName), Is.True);
            Assert.That(registry.IsPostRegistration("overnightReturnD1"), Is.False);
            Assert.That(registry.PostRegistrationNames, Is.EquivalentTo(new[] { VelocityDeathNearPmHighFeature.FeatureName }));
        });
    }
}
