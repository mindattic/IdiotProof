using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using IdiotProof.Brokers;
using IdiotProof.Core.Monitoring.FadeShort;
using IdiotProof.Core.Services;
using IdiotProof.Core.Trading;
using IdiotProof.Engine.Settings;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;

namespace IdiotProof.Monitor.Tests;

/// <summary>
/// IP-T480: the fade-short borrow pilot's signal math from synthetic minute bars, Rule 201 from daily bars, the
/// locate-quote parser, and the no-order guard (no dependency on the order path; GET-only allow-listed calls).
/// </summary>
[TestFixture]
public sealed class FadeShortBorrowPilotTests
{
    // Monday 2026-11-02 (EST): 09:30 ET = 14:30 UTC.
    private static readonly DateOnly Day = new(2026, 11, 2);

    private static Candle Bar(int hour, int minute, decimal open, decimal high, decimal low, decimal close, decimal volume = 1000)
    {
        var start = MinuteBarMath.ToUtc(Day, new TimeSpan(hour, minute, 0));
        return new Candle { Symbol = "ABCD", StartUtc = start, EndUtc = start.AddMinutes(1), Open = open, High = high, Low = low, Close = close, Volume = volume };
    }

    /// <summary>Prior close 1.00; a premarket that peaks at <paramref name="pmHigh"/>; the 09:30 open; a 09:44 close.</summary>
    private static List<Candle> Day1(decimal pmHigh, decimal open0930, decimal close0944, params Candle[] extra)
    {
        var bars = new List<Candle>
        {
            Bar(4, 0, 1.10m, 1.20m, 1.05m, 1.15m),
            Bar(7, 15, 1.30m, pmHigh, 1.25m, 1.35m),
            Bar(9, 29, 1.35m, 1.38m, 1.30m, 1.36m),
            Bar(9, 30, open0930, open0930 + 0.02m, open0930 - 0.02m, open0930),
            Bar(9, 44, close0944, close0944 + 0.01m, close0944 - 0.01m, close0944),
        };
        bars.AddRange(extra);
        return bars;
    }

    [Test]
    public void Signal_Fires_WhenPmHighAtLeast50_GapAtLeast20_ExtensionBelow10()
    {
        var s = FadeShortPilotMath.Evaluate(Day1(pmHigh: 1.60m, open0930: 1.40m, close0944: 1.45m), Day, prevClose: 1.00m);
        Assert.That(s, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(s!.PmHighPct, Is.EqualTo(60).Within(1e-9));
            Assert.That(s.GapPct, Is.EqualTo(45).Within(1e-9));
            Assert.That(s.ExtensionPct, Is.EqualTo((1.45 / 1.40 - 1) * 100).Within(1e-9));
            Assert.That(s.PmHighAtLeast100, Is.False);
            Assert.That(s.Fired, Is.True);
        });
    }

    [Test]
    public void Signal_DoesNotFire_WhenExtensionFromOpenIs10OrMore()
    {
        var s = FadeShortPilotMath.Evaluate(Day1(1.60m, open0930: 1.40m, close0944: 1.54m), Day, 1.00m);
        Assert.That(s!.ExtensionPct, Is.EqualTo(10).Within(1e-9));
        Assert.That(s.Fired, Is.False);
    }

    [Test]
    public void Signal_DoesNotFire_WhenGapAt0945IsBelow20()
    {
        var s = FadeShortPilotMath.Evaluate(Day1(1.60m, open0930: 1.15m, close0944: 1.18m), Day, 1.00m);
        Assert.That(s!.GapPct, Is.LessThan(20));
        Assert.That(s.Fired, Is.False);
    }

    [Test]
    public void Signal_DoesNotFire_WhenPremarketHighIsBelow50()
    {
        var s = FadeShortPilotMath.Evaluate(Day1(1.45m, 1.40m, 1.42m), Day, 1.00m);
        Assert.That(s!.PmHighPct, Is.EqualTo(45).Within(1e-9));
        Assert.That(s.Fired, Is.False);
    }

    [Test]
    public void Signal_FlagsTheHundredPercentTier()
    {
        var s = FadeShortPilotMath.Evaluate(Day1(2.10m, 1.40m, 1.45m), Day, 1.00m);
        Assert.Multiple(() =>
        {
            Assert.That(s!.PmHighAtLeast100, Is.True);
            Assert.That(s.Fired, Is.True);
        });
    }

    [Test]
    public void Signal_IgnoresBarsEndingAfter0945()
    {
        var late = Bar(9, 45, 1.46m, 3.00m, 1.46m, 3.00m);
        var withLate = FadeShortPilotMath.Evaluate(Day1(1.60m, 1.40m, 1.45m, late), Day, 1.00m);
        var without = FadeShortPilotMath.Evaluate(Day1(1.60m, 1.40m, 1.45m), Day, 1.00m);
        Assert.That(withLate, Is.EqualTo(without));
    }

    [Test]
    public void Signal_IsNull_WithoutA0930Bar()
    {
        var bars = Day1(1.60m, 1.40m, 1.45m).Where(b => MinuteBarMath.ToEastern(b.StartUtc).Hour < 9 || MinuteBarMath.ToEastern(b.StartUtc).Minute < 30).ToList();
        Assert.That(FadeShortPilotMath.Evaluate(bars, Day, 1.00m), Is.Null);
    }

    [Test]
    public void PremarketHigh_UsesOnly0400To0929Bars()
    {
        var bars = Day1(1.60m, 1.40m, 1.45m, Bar(9, 35, 1.45m, 5.00m, 1.40m, 1.50m));
        var pm = FadeShortPilotMath.PremarketHigh(bars, Day, 1.00m, MinuteBarMath.ToUtc(Day, new TimeSpan(9, 40, 0)));
        Assert.That(pm!.Value.PmHigh, Is.EqualTo(1.60m));
        Assert.That(pm.Value.Pct, Is.EqualTo(60).Within(1e-9));
    }

    private static Candle Daily(DateOnly d, decimal low, decimal close)
    {
        var start = MinuteBarMath.ToUtc(d, TimeSpan.Zero);
        return new Candle { Symbol = "ABCD", StartUtc = start, EndUtc = start.AddDays(1), Open = close, High = Math.Max(close, low) + 0.1m, Low = low, Close = close, Volume = 1 };
    }

    [Test]
    public void PriorDays_SsrActive_WhenD1LowIsTenPercentBelowD2Close()
    {
        var bars = new[] { Daily(new DateOnly(2026, 10, 29), 1.9m, 2.00m), Daily(new DateOnly(2026, 10, 30), 1.70m, 1.80m), Daily(Day, 1.0m, 3.0m) };
        var (prev, x, ssr) = FadeShortPilotMath.PriorDays(bars, Day);
        Assert.Multiple(() =>
        {
            Assert.That(prev, Is.EqualTo(1.80m), "today's bar is excluded; D-1 is Friday");
            Assert.That(x, Is.EqualTo(-0.15).Within(1e-9));
            Assert.That(ssr, Is.True);
        });
    }

    [Test]
    public void PriorDays_SsrInactive_WhenD1LowHoldsAboveTheTrigger()
    {
        var bars = new[] { Daily(new DateOnly(2026, 10, 29), 1.9m, 2.00m), Daily(new DateOnly(2026, 10, 30), 1.85m, 1.95m) };
        var (_, x, ssr) = FadeShortPilotMath.PriorDays(bars, Day);
        Assert.That(x, Is.EqualTo(-0.075).Within(1e-9));
        Assert.That(ssr, Is.False);
    }

    [Test]
    public void PriorTradingDay_SkipsTheWeekend()
    {
        Assert.That(FadeShortPilotMath.PriorTradingDay(Day), Is.EqualTo(new DateOnly(2026, 10, 30)));
    }

    [Test]
    public void ParseLocateQuotes_ReadsQuotesAndErrors()
    {
        using var doc = JsonDocument.Parse("""
            {"quotes":[{"symbol":"ABCD","available_qty":1200,"price":0.0123,"quoted_at":"2026-11-02T14:40:00Z"},
                       {"symbol":"NONE","available_qty":0,"price":0.05,"quoted_at":"2026-11-02T14:40:00Z"}],
             "errors":[{"symbol":"AAPL","code":"easy_to_borrow","message":"no locate needed"}]}
            """);
        var q = FadeShortPilotMath.ParseLocateQuotes(doc.RootElement);
        Assert.Multiple(() =>
        {
            Assert.That(q["ABCD"].Available, Is.True);
            Assert.That(q["ABCD"].AvailableQty, Is.EqualTo(1200));
            Assert.That(q["ABCD"].FeePerShare, Is.EqualTo(0.0123m));
            Assert.That(q["NONE"].Available, Is.False);
            Assert.That(q["AAPL"].ErrorCode, Is.EqualTo("easy_to_borrow"));
            Assert.That(q["AAPL"].Available, Is.Null);
        });
    }

    [Test]
    public void ParseAsset_ReadsTheBorrowFields()
    {
        using var doc = JsonDocument.Parse("""{"symbol":"ABCD","status":"active","shortable":true,"easy_to_borrow":false,"marginable":true,"borrow_status":"hard_to_borrow"}""");
        Assert.That(FadeShortPilotMath.ParseAsset(doc.RootElement), Is.EqualTo(new AssetBorrowState(true, false, true, "active", "hard_to_borrow")));
    }

    // ---- the no-order guard ----

    private static readonly Type[] OrderPath =
    [
        typeof(TransactionGateway), typeof(IOrderRouting), typeof(IBrokerClient), typeof(UserBrokerResolver), typeof(AlpacaApi),
    ];

    private static IEnumerable<Type> CtorDependencies(Type t) =>
        t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SelectMany(c => c.GetParameters()).Select(p => p.ParameterType);

    [Test]
    public void Worker_HasNoDependencyOnTheOrderOrTransactionGateway()
    {
        var deps = CtorDependencies(typeof(FadeShortBorrowPilotWorker)).ToList();
        Assert.That(deps, Is.Not.Empty);
        foreach (var dep in deps)
            foreach (var banned in OrderPath.Append(typeof(AlpacaApiFactory)))
                Assert.That(banned.IsAssignableFrom(dep), Is.False, $"FadeShortBorrowPilotWorker must not depend on {banned.Name} (has {dep.Name}).");
        Assert.That(deps, Does.Contain(typeof(IFadeShortBorrowReader)), "Alpaca is reached only through the read-only reader.");
    }

    [Test]
    public void Worker_HasNoFieldOrPropertyOfAnOrderPathType()
    {
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var members = typeof(FadeShortBorrowPilotWorker).GetFields(flags).Select(f => f.FieldType)
            .Concat(typeof(FadeShortBorrowPilotWorker).GetProperties(flags).Select(p => p.PropertyType));
        foreach (var t in members)
            foreach (var banned in OrderPath)
                Assert.That(banned.IsAssignableFrom(t), Is.False, $"{t.Name} is on the order path.");
    }

    [Test]
    public void Reader_DependsOnlyOnTheApiFactory_NotTheGatewayOrBrokerClient()
    {
        foreach (var dep in CtorDependencies(typeof(AlpacaFadeShortBorrowReader)))
            foreach (var banned in new[] { typeof(TransactionGateway), typeof(IOrderRouting), typeof(IBrokerClient), typeof(UserBrokerResolver) })
                Assert.That(banned.IsAssignableFrom(dep), Is.False, $"AlpacaFadeShortBorrowReader must not depend on {banned.Name}.");
    }

    [Test]
    public void Session_ExposesOnlyReadMethods()
    {
        var names = typeof(IFadeShortBorrowSession).GetMethods().Select(m => m.Name).Where(n => n != "DisposeAsync").OrderBy(n => n).ToList();
        Assert.That(names, Is.EqualTo(new[] { "AssetAsync", "DailyBarsAsync", "LocateQuotesAsync", "MinuteBarsAsync", "MoversAsync" }));
    }

    [TestCase("/v2/assets/ABCD", true)]
    [TestCase("/v1/locates/quotes", true)]
    [TestCase("/v1/locates", false)]
    [TestCase("/v2/orders", false)]
    [TestCase("/v2/orders/abc", false)]
    [TestCase("/v2/positions/ABCD", false)]
    [TestCase("/v2/assets/ABCD/orders", false)]
    [TestCase("/v2/account", false)]
    public void TradingAllowList(string path, bool allowed)
    {
        Assert.That(FadeShortReadOnlyEndpoints.IsAllowedTrading(path), Is.EqualTo(allowed));
        if (!allowed) Assert.Throws<InvalidOperationException>(() => FadeShortReadOnlyEndpoints.AssertTrading(path));
    }

    [TestCase("/v2/stocks/ABCD/bars", true)]
    [TestCase("/v1beta1/screener/stocks/movers", true)]
    [TestCase("/v2/stocks/bars", false)]
    [TestCase("/v2/orders", false)]
    public void DataAllowList(string path, bool allowed) =>
        Assert.That(FadeShortReadOnlyEndpoints.IsAllowedData(path), Is.EqualTo(allowed));

    [Test]
    public async Task Reader_SendsOnlyAllowListedGets_OverThePaperRoute()
    {
        var handler = new RecordingHandler();
        var reader = new AlpacaFadeShortBorrowReader(new ScriptedFactory(handler));
        await using (var s = await reader.OpenAsync(Guid.NewGuid(), CancellationToken.None))
        {
            await s.MoversAsync(50, CancellationToken.None);
            await s.MinuteBarsAsync("ABCD", MinuteBarMath.ToUtc(Day, new TimeSpan(4, 0, 0)), MinuteBarMath.ToUtc(Day, new TimeSpan(9, 45, 0)), "sip", CancellationToken.None);
            await s.DailyBarsAsync("ABCD", Day.AddDays(-14), Day.AddDays(-1), "sip", CancellationToken.None);
            await s.AssetAsync("ABCD", CancellationToken.None);
            await s.LocateQuotesAsync(["ABCD", "EFGH"], CancellationToken.None);
        }

        Assert.That(handler.Requests, Has.Count.EqualTo(5));
        foreach (var (method, uri) in handler.Requests)
        {
            Assert.That(method, Is.EqualTo(HttpMethod.Get), uri.ToString());
            Assert.That(uri.AbsolutePath, Does.Not.Contain("/orders").And.Not.Contain("/positions"));
            Assert.That(uri.AbsolutePath == "/v1/locates", Is.False, "a locate request is never made");
            if (uri.Host.StartsWith("data.", StringComparison.Ordinal)) Assert.That(FadeShortReadOnlyEndpoints.IsAllowedData(uri.AbsolutePath), Is.True, uri.ToString());
            else
            {
                Assert.That(uri.Host, Is.EqualTo("paper-api.alpaca.markets"));
                Assert.That(FadeShortReadOnlyEndpoints.IsAllowedTrading(uri.AbsolutePath), Is.True, uri.ToString());
            }
        }
    }

    [Test]
    public void Reader_RefusesALiveClient()
    {
        var reader = new AlpacaFadeShortBorrowReader(new ScriptedFactory(new RecordingHandler(), paper: false));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.OpenAsync(Guid.NewGuid(), CancellationToken.None));
    }

    private sealed class ScriptedFactory(HttpMessageHandler handler, bool paper = true) : AlpacaApiFactory(null!, null!, new AppSettings())
    {
        public override Task<AlpacaApi> ForOwnerAsync(Guid ownerUserId, BrokerMode mode, CancellationToken ct = default)
        {
            Assert.That(mode, Is.EqualTo(BrokerMode.Paper), "the pilot only ever asks for the paper route");
            return Task.FromResult(new AlpacaApi("k", "s", paper, handler));
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri Uri)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!));
            var path = request.RequestUri!.AbsolutePath;
            var body = path.EndsWith("/bars", StringComparison.Ordinal) ? """{"bars":[{"t":"2026-11-02T14:30:00Z","o":1.4,"h":1.5,"l":1.3,"c":1.45,"v":1000}],"next_page_token":null}"""
                : path.Contains("movers", StringComparison.Ordinal) ? """{"gainers":[{"symbol":"ABCD","percent_change":55.0,"change":0.55,"price":1.55}],"losers":[]}"""
                : path.StartsWith("/v2/assets/", StringComparison.Ordinal) ? """{"symbol":"ABCD","status":"active","shortable":true,"easy_to_borrow":false,"marginable":true}"""
                : """{"quotes":[],"errors":[]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
