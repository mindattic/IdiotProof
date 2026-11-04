using System.Text.Json;
using IdiotProof.Core.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.Models;
using IdiotProof.Operations.Evidence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Evidence;

public class EvidenceEventsOperationTests
{
    private ServiceProvider services = null!;
    private IDbContextFactory<AppDbContext> db = null!;
    private FakeBarStore store = null!;
    private List<DateOnly> calendar = null!;

    [SetUp]
    public void SetUp()
    {
        store = new FakeBarStore();
        services = new ServiceCollection()
            .AddLogging()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"events-{Guid.NewGuid():N}"))
            .AddSingleton<IHistoricalBarStore>(store)
            .BuildServiceProvider();
        db = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        calendar = new List<DateOnly>();
        for (var d = new DateOnly(2026, 1, 5); calendar.Count < 260; d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) calendar.Add(d);
    }

    [TearDown]
    public void TearDown() => services.Dispose();

    private OperationContext Ctx() => new()
    {
        Services = services, OwnerId = Guid.NewGuid(), Caller = OperationCaller.Cli,
        Clock = new FixedClock(new DateTime(2026, 12, 31, 17, 0, 0, DateTimeKind.Utc)),
    };

    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc);
    }

    /// <summary>Daily bars stamped at midnight ET (05:00 UTC in winter), closes from compounded returns.</summary>
    private void Daily(string symbol, Func<int, double> returnOn)
    {
        double close = 3.0;
        var list = new List<Candle>();
        for (int i = 0; i < calendar.Count; i++)
        {
            close *= 1 + (i == 0 ? 0 : returnOn(i));
            var d = calendar[i];
            var start = new DateTime(d.Year, d.Month, d.Day, 5, 0, 0, DateTimeKind.Utc);
            decimal c = (decimal)close;
            list.Add(new Candle { Symbol = symbol, StartUtc = start, EndUtc = start.AddDays(1), Open = c, High = c, Low = c, Close = c, Volume = 1_000_000 });
        }
        store.Daily[symbol] = list;
    }

    private async Task Claims(params ResearchClaim[] claims)
    {
        await using var c = await db.CreateDbContextAsync();
        c.ResearchClaims.AddRange(claims);
        await c.SaveChangesAsync();
    }

    private static ResearchClaim Claim(string ticker, DateOnly day, string type, string sentiment) => new()
    {
        Ticker = ticker, ArticleDate = day, ClaimType = type, Sentiment = sentiment, ClaimSummary = "test",
    };

    [Test]
    public async Task PlantedBullishContracts_ShowPositiveSignificantCar_AndBearishSentimentIsSigned()
    {
        var rng = new Random(5);
        var market = calendar.Select(_ => 0.01 * Gaussian(rng)).ToArray();
        Daily("SPY", i => market[i]);
        var claims = new List<ResearchClaim>();
        for (int s = 0; s < 12; s++)
        {
            int day = 180 + s * 5;
            var sym = $"P{s}";
            var noise = calendar.Select(_ => 0.02 * Gaussian(rng)).ToArray();
            Daily(sym, i => 1.2 * market[i] + noise[i] + (i == day ? 0.08 : 0));
            claims.Add(Claim(sym, calendar[day], "Contract", "Bullish"));
        }
        await Claims(claims.ToArray());

        var result = await new EvidenceEventsOperation().ExecuteAsync(new EvidenceEventsArgs
        {
            From = calendar[0], To = calendar[^1], Windows = "0:0", Estimation = 100, Gap = 5, Bootstrap = 200,
        }, Ctx());

        Assert.That(result.IsOk, Is.True, result.Human);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Data));
        var groups = json.RootElement.GetProperty("Groups").EnumerateArray().ToList();
        var contract = groups.Single(g => g.GetProperty("Group").GetString() == "Contract");
        Assert.That(contract.GetProperty("Events").GetInt32(), Is.EqualTo(12));
        Assert.That(contract.GetProperty("MeanCar").GetDouble(), Is.EqualTo(0.08).Within(0.025));
        Assert.That(contract.GetProperty("BmpP").GetDouble(), Is.LessThan(0.01));
        Assert.That(contract.GetProperty("BootstrapP").GetDouble(), Is.LessThan(0.05));
        Assert.That(contract.GetProperty("MeanSignedCar").GetDouble(), Is.GreaterThan(0.05));
        Assert.That(groups.Any(g => g.GetProperty("Group").GetString() == "Contract/Bullish"), Is.True);
    }

    [Test]
    public async Task ThinGroups_AreOmitted_AndSkipReasonsReported()
    {
        Daily("SPY", _ => 0.001);
        Daily("AAA", _ => 0.002);
        await Claims(Claim("AAA", calendar[200], "MA", "Bullish"), Claim("AAA", calendar[5], "MA", "Bullish"), Claim("ZZZ", calendar[200], "MA", "Bearish"));
        var result = await new EvidenceEventsOperation().ExecuteAsync(new EvidenceEventsArgs
        {
            From = calendar[0], To = calendar[^1], Windows = "0:1", Estimation = 100, Bootstrap = 0,
        }, Ctx());
        Assert.That(result.IsOk, Is.True, result.Human);
        Assert.That(result.Human, Does.Contain("too few estimation days").Or.Contain("estimation window before data"));
        Assert.That(result.Human, Does.Contain("no bars"));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Data));
        Assert.That(json.RootElement.GetProperty("Groups").GetArrayLength(), Is.EqualTo(0), "one valid MA event is below --min-events");
    }

    [Test]
    public async Task MacroClaims_AndBadWindows_AreHandled()
    {
        Assert.That(EvidenceEventsOperation.TryParseWindows("-1:1, 0:5", out var w, out _), Is.True);
        Assert.That(w, Has.Count.EqualTo(2));
        Assert.That(EvidenceEventsOperation.TryParseWindows("2:1", out _, out var err), Is.False);
        Assert.That(err, Does.Contain("2:1"));

        var usage = await new EvidenceEventsOperation().ExecuteAsync(new EvidenceEventsArgs { Windows = "x" }, Ctx());
        Assert.That(usage.IsOk, Is.False);

        var macro = Claim("", calendar[200], "Regulatory", "Bearish");
        macro.IsMacro = true;
        await Claims(macro);
        var none = await new EvidenceEventsOperation().ExecuteAsync(new EvidenceEventsArgs { From = calendar[0], To = calendar[^1] }, Ctx());
        Assert.That(none.Human, Does.Contain("No single-ticker claims"));
    }

    private static double Gaussian(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }
}
