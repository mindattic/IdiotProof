using System.Text.Json;
using IdiotProof.Core.Adaptation;
using IdiotProof.Core.Data;
using IdiotProof.Evidence;
using IdiotProof.Evidence.Adaptation;
using IdiotProof.Operations.Evidence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Evidence;

public class EvidenceCalibrationOperationTests
{
    private ServiceProvider services = null!;
    private IDbContextFactory<AppDbContext> db = null!;
    private readonly Guid owner = Guid.NewGuid();
    private static readonly DateTime Decided = new(2026, 11, 2, 15, 0, 0, DateTimeKind.Utc);

    [SetUp]
    public void SetUp()
    {
        services = new ServiceCollection()
            .AddLogging()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"calib-{Guid.NewGuid():N}"))
            .BuildServiceProvider();
        db = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
    }

    [TearDown]
    public void TearDown() => services.Dispose();

    private OperationContext Ctx(Guid? who = null) => new()
    {
        Services = services, OwnerId = who ?? owner, Caller = OperationCaller.Cli, Clock = TimeProvider.System,
    };

    private async Task AddAsync(params object[] rows)
    {
        await using var c = await db.CreateDbContextAsync();
        foreach (var r in rows) c.Add(r);
        await c.SaveChangesAsync();
    }

    private ExhaustionDecisionRow Exhaustion(double p, decimal? pnl, bool recorded = true, Guid? who = null) => new()
    {
        OwnerUserId = who ?? owner, Symbol = "SNDL", EtDate = "2026-11-02", DecidedUtc = Decided, Side = "Long", Armed = true,
        StrategyId = Guid.NewGuid(), ReversalProbability = p, FeaturesJson = "{}", RealizedPnL = pnl, RealizedTrades = pnl is null ? null : 1,
        OutcomeRecordedUtc = recorded ? Decided.AddHours(1) : null,
    };

    private AdaptationDecision Adaptation(string chosen, double posteriorMean, decimal pnl)
    {
        var scores = new[]
        {
            new VariantScore(new ParameterVariant("other", 1.0, null, null), 1, 1, 0, 2, 2, 0.5, 0.2),
            new VariantScore(new ParameterVariant(chosen, 2.0, 1.0, null), 3, 1, 0.4, 4, 2, posteriorMean, 0.8),
        };
        return new AdaptationDecision
        {
            StrategyId = Guid.NewGuid(), OwnerUserId = owner, Symbol = "MULN", EtDate = "2026-11-02", DecidedUtc = Decided,
            Outcome = AdaptationService.Applied, FeaturesJson = "{}", VariantsJson = JsonSerializer.Serialize(scores), ChosenVariant = chosen,
            RealizedTrades = 1, RealizedPnL = pnl, OutcomeRecordedUtc = Decided.AddHours(1),
        };
    }

    [Test]
    public async Task ReportsBothSources_FromRecordedOutcomesOnly_ForTheCallerOnly()
    {
        await AddAsync(
            Exhaustion(0.7, 0.05m), Exhaustion(0.7, 0.03m), Exhaustion(0.7, -0.02m),
            Exhaustion(0.7, null, recorded: false),
            Exhaustion(0.7, 0.10m, who: Guid.NewGuid()),
            Adaptation("tight", 0.6, 1.5m), Adaptation("tight", 0.6, -0.5m));

        var result = await new EvidenceCalibrationOperation().ExecuteAsync(new EvidenceCalibrationArgs(), Ctx());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Ok), result.Human ?? result.Message);
        var json = JsonSerializer.SerializeToElement(result.Data);
        Assert.That(json.GetProperty("Samples").GetInt32(), Is.EqualTo(5));
        var groups = json.GetProperty("Groups").EnumerateArray().ToList();
        var ex = groups.Single(g => g.GetProperty("Template").GetString() == "exhaustion-long");
        Assert.That(ex.GetProperty("Samples").GetInt32(), Is.EqualTo(3));
        Assert.That(ex.GetProperty("Wins").GetInt32(), Is.EqualTo(2));
        var ad = groups.Single(g => g.GetProperty("Source").GetString() == "adaptation");
        Assert.That(ad.GetProperty("Template").GetString(), Is.EqualTo("tight"));
        Assert.That(ad.GetProperty("MeanPredicted").GetDouble(), Is.EqualTo(0.6).Within(1e-9), "the chosen variant's posterior, not the other variant's");
    }

    [Test]
    public async Task SourceFilter_AndEmptyState_AndBadArgs()
    {
        await AddAsync(Adaptation("tight", 0.6, 1m));
        var exOnly = await new EvidenceCalibrationOperation().ExecuteAsync(new EvidenceCalibrationArgs { Source = "exhaustion" }, Ctx());
        Assert.That(exOnly.Status, Is.EqualTo(OperationStatus.Ok));
        Assert.That(exOnly.Human, Does.Contain("No decisions with realized outcomes yet"));

        var bad = await new EvidenceCalibrationOperation().ExecuteAsync(new EvidenceCalibrationArgs { Source = "gut" }, Ctx());
        Assert.That(bad.Status, Is.EqualTo(OperationStatus.UsageError));
    }

    [Test]
    public void ChosenPosteriorMean_ReadsTheChosenVariant_AndToleratesJunk()
    {
        var d = Adaptation("wide", 0.73, 1m);
        Assert.That(EvidenceCalibrationOperation.ChosenPosteriorMean(d.VariantsJson, "wide"), Is.EqualTo(0.73).Within(1e-12));
        Assert.That(EvidenceCalibrationOperation.ChosenPosteriorMean(d.VariantsJson, "missing"), Is.Null);
        Assert.That(EvidenceCalibrationOperation.ChosenPosteriorMean("not json", "wide"), Is.Null);
    }
}
