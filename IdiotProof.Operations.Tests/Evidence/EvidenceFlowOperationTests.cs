using System.IO.Compression;
using IdiotProof.Evidence.Microstructure;
using IdiotProof.Operations.Evidence;
using Microsoft.Extensions.DependencyInjection;
using static IdiotProof.Operations.Tests.Evidence.EvidenceTestKit;

namespace IdiotProof.Operations.Tests.Evidence;

/// <summary>evidence flow (IP-T392).</summary>
public class EvidenceFlowOperationTests
{
    private sealed class FakeTicks(Func<DateOnly, IReadOnlyList<TradePrint>> trades) : ITickStore
    {
        public List<DateOnly> TradeDays { get; } = new();
        public int QuoteCalls { get; private set; }

        public Task<IReadOnlyList<TradePrint>> GetTradesAsync(string symbol, DateOnly dayEt, CancellationToken ct)
        {
            TradeDays.Add(dayEt);
            return Task.FromResult(trades(dayEt));
        }

        public Task<IReadOnlyList<QuoteTick>> GetQuotesAsync(string symbol, DateOnly dayEt, CancellationToken ct)
        {
            QuoteCalls++;
            return Task.FromResult<IReadOnlyList<QuoteTick>>([]);
        }
    }

    private static OperationContext WithTicks(FakeBarStore store, ITickStore ticks)
    {
        var ctx = Context(store);
        var services = new ServiceCollection();
        services.AddSingleton<IdiotProof.Core.Evidence.IHistoricalBarStore>(store);
        services.AddSingleton(ticks);
        return new OperationContext { Services = services.BuildServiceProvider(), Clock = ctx.Clock };
    }

    private static IReadOnlyList<TradePrint> TwoPrintsPerMinute(DateOnly day)
    {
        var open = new DateTime(day.Year, day.Month, day.Day, 14, 30, 0, DateTimeKind.Utc);
        var list = new List<TradePrint>();
        for (int m = 0; m < 120; m++)
        {
            double p = Sawtooth(m);
            list.Add(new TradePrint(open.AddMinutes(m).AddSeconds(10), p - 0.001, 300));
            list.Add(new TradePrint(open.AddMinutes(m).AddSeconds(40), p, 200));
        }
        return list;
    }

    [Test]
    public async Task Flow_SignsTradesPerSession_AndSplitsTrainAndTestBySession()
    {
        var days = Weekdays(3);
        var store = new FakeBarStore();
        store.Minute["SAW"] = days.SelectMany(d => Session("SAW", d, Sawtooth, minutes: 120)).ToList();
        var ticks = new FakeTicks(TwoPrintsPerMinute);

        var result = await new EvidenceFlowOperation().ExecuteAsync(new EvidenceFlowArgs
        {
            Symbol = "SAW", From = days[0], To = days[2], VpinWindow = 5, LambdaWindow = 10,
        }, WithTicks(store, ticks));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(ticks.TradeDays, Is.EqualTo(days));
        Assert.That(ticks.QuoteCalls, Is.EqualTo(0), "the tick rule needs no quotes");
        Assert.That(result.Human, Does.Contain("3 sessions (2 train / 1 test)"));
        Assert.That(result.Human, Does.Contain("720 trades"));
        Assert.That(result.Human, Does.Contain("imbalance").And.Contain("vpin").And.Contain("kyle-lambda").And.Contain("absorption"));
    }

    [Test]
    public async Task Flow_LeeReadyFetchesQuotes_BulkNeedsNoTicksAtAll()
    {
        var days = Weekdays(2);
        var store = new FakeBarStore();
        store.Minute["SAW"] = days.SelectMany(d => Session("SAW", d, Sawtooth, minutes: 120)).ToList();

        var lee = new FakeTicks(TwoPrintsPerMinute);
        var r1 = await new EvidenceFlowOperation().ExecuteAsync(new EvidenceFlowArgs
        {
            Symbol = "SAW", From = days[0], To = days[1], Signing = FlowSigning.LeeReady,
        }, WithTicks(store, lee));
        Assert.That(r1.IsOk, Is.True, r1.Message);
        Assert.That(lee.QuoteCalls, Is.EqualTo(2));

        var bulk = new FakeTicks(TwoPrintsPerMinute);
        var r2 = await new EvidenceFlowOperation().ExecuteAsync(new EvidenceFlowArgs
        {
            Symbol = "SAW", From = days[0], To = days[1], Signing = FlowSigning.Bulk,
        }, WithTicks(store, bulk));
        Assert.That(r2.IsOk, Is.True, r2.Message);
        Assert.That(bulk.TradeDays, Is.Empty);
        Assert.That(r2.Human, Does.Contain("bulk volume classification"));
    }

    [Test]
    public async Task Flow_NeedsTwoSessions()
    {
        var days = Weekdays(1);
        var store = new FakeBarStore();
        store.Minute["SAW"] = Session("SAW", days[0], Sawtooth, minutes: 60).ToList();
        var result = await new EvidenceFlowOperation().ExecuteAsync(new EvidenceFlowArgs { Symbol = "SAW", From = days[0], To = days[0] },
            WithTicks(store, new FakeTicks(TwoPrintsPerMinute)));
        Assert.That(result.Human, Does.Contain("need at least 2 sessions"));
    }

    [Test]
    public async Task TickStore_ServesAFinishedSessionFromItsCache_WithoutTheNetwork()
    {
        var root = Path.Combine(Path.GetTempPath(), "ip-t392-" + Guid.NewGuid().ToString("N"));
        try
        {
            var day = new DateOnly(2026, 11, 2);
            var t = new DateTime(2026, 11, 2, 14, 31, 0, DateTimeKind.Utc);
            Directory.CreateDirectory(Path.Combine(root, "SAW"));
            await using (var w = new StreamWriter(new GZipStream(File.Create(Path.Combine(root, "SAW", "2026-11-02.trades.csv.gz")), CompressionLevel.Fastest)))
                await w.WriteLineAsync($"{t.Ticks},3.05,250");

            // The context has no Alpaca client registered: any network attempt would throw.
            var store = new AlpacaTickStore(Context(new FakeBarStore()), root);
            var trades = await store.GetTradesAsync("saw", day, CancellationToken.None);
            Assert.That(trades, Has.Count.EqualTo(1));
            Assert.That(trades[0], Is.EqualTo(new TradePrint(t, 3.05, 250)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
