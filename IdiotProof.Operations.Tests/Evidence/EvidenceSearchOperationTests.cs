using IdiotProof.Operations.Evidence;
using static IdiotProof.Operations.Tests.Evidence.EvidenceTestKit;

namespace IdiotProof.Operations.Tests.Evidence;

/// <summary>evidence search: grid search with multiple-testing corrections and a holdout (IP-T393).</summary>
public class EvidenceSearchOperationTests
{
    private const string Template = "Ticker(\"X\").IsAboveVwap().Long().TakeProfitPercent({tp}).StopLossPercent({sl}).Repeat()";

    private static FakeBarStore Store(int days, params (string Symbol, Func<int, Func<int, double>> PathForDay)[] series)
    {
        var store = new FakeBarStore();
        var list = Weekdays(days);
        foreach (var (symbol, path) in series)
            store.Minute[symbol] = list.SelectMany((d, i) => Session(symbol, d, path(i), minutes: 200)).ToList();
        return store;
    }

    [Test]
    public async Task Search_ScoresEveryConfig_AndReportsCorrectionsAndHoldout()
    {
        var days = Weekdays(10);
        var store = Store(10, ("SAW", _ => Sawtooth), ("RND", i => RandomWalk(100 + i)));

        var result = await new EvidenceSearchOperation().ExecuteAsync(new EvidenceSearchArgs
        {
            Symbols = ["SAW", "RND"], Script = Template, Grid = ["tp=0.5,1", "sl=0.3,0.6"],
            From = days[0], To = days[^1], Holdout = 0.3, Resamples = 200,
        }, Context(store));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("4 configs tried on 14 selection symbol-days"));
        Assert.That(result.Human, Does.Contain("reality check p="));
        Assert.That(result.Human, Does.Contain("held out 6 symbol-days after " + days[6].ToString("yyyy-MM-dd")));
        Assert.That(result.Human, Does.Contain("q(BH)").And.Contain("DSR"));
    }

    [Test]
    public async Task Search_SelectionNeverSeesTheHoldoutDays()
    {
        var days = Weekdays(10);
        var store = Store(10, ("SAW", _ => Sawtooth));
        var args = new EvidenceSearchArgs
        {
            Symbols = ["SAW"], Script = Template, Grid = ["tp=0.5,1", "sl=0.3"], From = days[0], To = days[^1], Holdout = 0.3, Resamples = 200,
        };
        var baseline = await new EvidenceSearchOperation().ExecuteAsync(args, Context(store));

        // Wreck the holdout days: the selection table (everything above the summary) must not change.
        var wrecked = Store(10, ("SAW", i => i >= 7 ? RandomWalk(900 + i) : Sawtooth));
        var after = await new EvidenceSearchOperation().ExecuteAsync(args, Context(wrecked));

        string Selection(string human) => human[..human.IndexOf("held out", StringComparison.Ordinal)];
        Assert.That(after.IsOk, Is.True, after.Message);
        Assert.That(Selection(after.Human), Is.EqualTo(Selection(baseline.Human)));
    }

    [Test]
    public async Task Search_OnPureNoise_DoesNotClaimASurvivor()
    {
        var days = Weekdays(12);
        var store = Store(12, ("N1", i => RandomWalk(10 + i)), ("N2", i => RandomWalk(50 + i)), ("N3", i => RandomWalk(90 + i)));

        var result = await new EvidenceSearchOperation().ExecuteAsync(new EvidenceSearchArgs
        {
            Symbols = ["N1", "N2", "N3"], Script = Template, Grid = ["tp=0.3,0.6,1", "sl=0.2,0.4,0.8"],
            From = days[0], To = days[^1], Resamples = 300,
        }, Context(store));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("9 configs tried"));
        Assert.That(result.Human, Does.Contain("does NOT survive"));
    }

    [Test]
    public async Task Search_RejectsBadGrids()
    {
        var store = Store(5, ("SAW", _ => Sawtooth));
        var op = new EvidenceSearchOperation();

        var missing = await op.ExecuteAsync(new EvidenceSearchArgs { Symbols = ["SAW"], Script = Template, Grid = ["tp=1"] }, Context(store));
        Assert.That(missing.IsOk, Is.False);
        Assert.That(missing.Message ?? missing.Human, Does.Contain("sl"));

        var extra = await op.ExecuteAsync(new EvidenceSearchArgs { Symbols = ["SAW"], Script = Template, Grid = ["tp=1", "sl=1", "zz=1"] }, Context(store));
        Assert.That(extra.IsOk, Is.False);

        var tooMany = await op.ExecuteAsync(new EvidenceSearchArgs
        {
            Symbols = ["SAW"], Script = Template, Grid = ["tp=1,2,3,4", "sl=1,2,3"], MaxConfigs = 10,
        }, Context(store));
        Assert.That(tooMany.IsOk, Is.False);
        Assert.That(tooMany.Message ?? tooMany.Human, Does.Contain("12 configs"));

        var injection = await op.ExecuteAsync(new EvidenceSearchArgs { Symbols = ["SAW"], Script = Template, Grid = ["tp=1).Short(", "sl=1"] }, Context(store));
        Assert.That(injection.IsOk, Is.False);
    }

    [Test]
    public void ParseGrid_SplitsNamesAndValues()
    {
        var (axes, error) = EvidenceSearchOperation.ParseGrid(["tp=1, 2 ,3", "sl=0.5"]);
        Assert.That(error, Is.Null);
        Assert.That(axes["tp"], Is.EqualTo(new[] { "1", "2", "3" }));
        Assert.That(axes["sl"], Is.EqualTo(new[] { "0.5" }));
    }

    [Test]
    public void AbsolutePriceExits_AcrossSymbols_AreWarned_PercentExitsAreNot()
    {
        Assert.That(EvidenceSearchOperation.AbsolutePriceWarning("Ticker(X).Long().TakeProfit({tp}).StopLoss({sl})", 2),
            Does.Contain("TakeProfit").And.Contain("StopLoss").And.Contain("2 symbols"));
        Assert.That(EvidenceSearchOperation.AbsolutePriceWarning("Ticker(X).Long().TakeProfit({tp})", 1), Is.Null, "one symbol: a price level is meaningful");
        Assert.That(EvidenceSearchOperation.AbsolutePriceWarning(Template, 3), Is.Null);
        Assert.That(EvidenceSearchOperation.AbsolutePriceWarning("Ticker(X).Long().StopLossPercent({sl}).TrailingStopLoss({t})", 3), Is.Null);
    }
}
