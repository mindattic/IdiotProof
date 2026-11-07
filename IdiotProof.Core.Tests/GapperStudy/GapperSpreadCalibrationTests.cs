using System.Text.Json.Nodes;
using IdiotProof.Core.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.Core.Research.Gappers;
using IdiotProof.Evidence.Economics;
using IdiotProof.Evidence.Gappers;
using static IdiotProof.Core.Tests.GapperStudy.GapperStudyTestKit;

namespace IdiotProof.Core.Tests.GapperStudy;

/// <summary>IP-T442 A2: the one-time spread multiplier — median quoted/Corwin-Schultz ratio over the first N training Tier20 events, written once, refused twice, forced with an audit line.</summary>
[TestFixture]
public class GapperSpreadCalibrationTests
{
    private static readonly DateOnly D = new(2026, 7, 17);
    private GapperStudyHost host = null!;

    [SetUp] public void SetUp() => host = new GapperStudyHost(withAlpacaKeys: true);
    [TearDown] public void TearDown() => host.Dispose();

    private sealed class FakeQuotes(double relativeSpread) : IAlpacaQuoteSource
    {
        public List<(string Symbol, DateTime From, DateTime To)> Calls { get; } = [];

        public Task<IReadOnlyList<QuoteTick>> GetQuotesAsync(string symbol, DateTime fromUtc, DateTime toUtc, int limit, CancellationToken ct)
        {
            Calls.Add((symbol, fromUtc, toUtc));
            double mid = 10;
            IReadOnlyList<QuoteTick> ticks = [new QuoteTick(fromUtc.AddSeconds(10), mid * (1 - relativeSpread / 2), mid * (1 + relativeSpread / 2)), new QuoteTick(fromUtc.AddSeconds(50), mid * (1 - relativeSpread / 2), mid * (1 + relativeSpread / 2))];
            return Task.FromResult(ticks);
        }
    }

    private async Task SeedAsync(int events)
    {
        var store = host.Store;
        await using var db = await host.DbAsync();
        for (int i = 0; i < events; i++)
        {
            var symbol = $"E{i}";
            db.GapperStudyEvents.Add(new GapperStudyEvent { Symbol = symbol, DateEt = D, Split = "train", AdjPrevClose = 10m, GapMax = 0.3, Tier20Touched = true, Tier20 = true, PmHigh = 13m });
            foreach (var prior in MarketCalendar.PriorTradingDays(D, host.Params.Features.CorwinSchultz.PriorDays))
                store.AppendDay(symbol, prior, Session(prior, m => 10 + 0.05 * (m % 2), _ => 100));
        }
        db.GapperStudyEvents.Add(new GapperStudyEvent { Symbol = "VAL", DateEt = new DateOnly(2025, 3, 3), Split = "validation", AdjPrevClose = 10m, GapMax = 0.3, Tier20Touched = true, Tier20 = true, PmHigh = 13m });
        await db.SaveChangesAsync();
    }

    [Test]
    public async Task Calibrates_Once_FromTheFrozenRecipe_AndRefusesASecondRun()
    {
        await SeedAsync(3);
        var quotes = new FakeQuotes(0.02);
        var stage = host.Get<GapperSpreadCalibrationStage>();
        stage.QuoteSourceFactory = _ => quotes;
        var recipe = GapperSpreadMultiplier.RecipeOf(host.Params);

        var r = await stage.RunAsync(new SpreadCalibrationRequest(null, null, Force: false, null), CancellationToken.None);
        var priorBars = MarketCalendar.PriorTradingDays(D, host.Params.Features.CorwinSchultz.PriorDays).SelectMany(d => host.Store.ReadDay("E0", d)).Where(b => b.Volume > 0).OrderBy(b => b.StartUtc).ToList();
        double cs = TradingCosts.CorwinSchultzSpread(priorBars);
        Assert.Multiple(() =>
        {
            Assert.That(r.EventsRequested, Is.EqualTo(recipe.Events));
            Assert.That(r.EventsSampled, Is.EqualTo(3), "only training Tier20 events, in (D, symbol) order");
            Assert.That(r.EventsUsed, Is.EqualTo(3));
            Assert.That(r.Multiplier, Is.EqualTo(0.02 / cs).Within(1e-9));
            Assert.That(quotes.Calls, Has.Count.EqualTo(3));
            Assert.That(quotes.Calls[0].From, Is.EqualTo(MinuteBarMath.ToUtc(D, GapperStudyParams.ParseEt(recipe.AtEt)).AddSeconds(-recipe.ToleranceSeconds)));
            Assert.That(File.Exists(r.File), Is.True);
        });
        var doc = JsonNode.Parse(File.ReadAllText(r.File)) as JsonObject;
        Assert.That(doc!["n"]!.GetValue<int>(), Is.EqualTo(3));
        Assert.That(doc["split"]!.GetValue<string>(), Is.EqualTo("train"));
        Assert.That(GapperSpreadMultiplier.Resolve(host.Paths, host.Params).Multiplier, Is.EqualTo(r.Multiplier));

        var second = Assert.ThrowsAsync<InvalidOperationException>(() => stage.RunAsync(new SpreadCalibrationRequest(null, null, false, null), CancellationToken.None));
        Assert.That(second!.Message, Does.Contain("--force"));

        var forced = await stage.RunAsync(new SpreadCalibrationRequest("train", recipe.Events, Force: true, null), CancellationToken.None);
        var audit = Path.Combine(host.Paths.RunsDir, GapperSpreadCalibrationStage.AuditFile);
        Assert.That(forced.Multiplier, Is.EqualTo(r.Multiplier).Within(1e-12));
        Assert.That(File.Exists(audit), Is.True);
        Assert.That(File.ReadAllText(audit), Does.Contain("FORCED"));
    }

    [Test]
    public async Task Refuses_ARecipeThatDiffersFromParams_AndReportsMissingWhenNotCalibrated()
    {
        await SeedAsync(1);
        var stage = host.Get<GapperSpreadCalibrationStage>();
        stage.QuoteSourceFactory = _ => new FakeQuotes(0.02);
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => stage.RunAsync(new SpreadCalibrationRequest("validation", 500, false, null), CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("params.json fixes"));
        var (multiplier, source) = GapperSpreadMultiplier.Resolve(host.Paths, host.Params);
        Assert.That(multiplier, Is.Null);
        Assert.That(source, Does.Contain("calibrate-spread"));
    }
}
