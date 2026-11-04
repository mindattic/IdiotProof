using IdiotProof.Evidence;
using IdiotProof.Operations.Evidence;
using static IdiotProof.Operations.Tests.Evidence.EvidenceTestKit;

namespace IdiotProof.Operations.Tests.Evidence;

public class EvidenceConformalOperationTests
{
    [Test]
    public async Task AnalogModel_ReportsCoverageAndTheAbstainRule()
    {
        var days = Weekdays(10);
        var store = new FakeBarStore();
        store.Minute["RW"] = days.SelectMany((d, k) => Session("RW", d, RandomWalk(300 + k))).ToList();

        var result = await new EvidenceConformalOperation().ExecuteAsync(new EvidenceConformalArgs
        {
            Symbols = ["RW"], From = days[0], To = days[^1], Horizon = 5, Window = 10, WarmupDays = 3, MinSimilarity = 0.5,
        }, Context(store));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("split conformal").And.Contain("adaptive (ACI)").And.Contain("abstain rule"));
        Assert.That(result.Human, Does.Not.Contain("— significant"), "a random walk carries no edge");
    }

    [Test]
    public async Task KalmanModel_CoversNearNominal_OnARandomWalk()
    {
        var days = Weekdays(12);
        var store = new FakeBarStore();
        store.Minute["RW"] = days.SelectMany((d, k) => Session("RW", d, RandomWalk(500 + k))).ToList();

        var result = await new EvidenceConformalOperation().ExecuteAsync(new EvidenceConformalArgs
        {
            Symbols = ["RW"], From = days[0], To = days[^1], Model = ConformalModel.Kalman, Horizon = 5, WarmupDays = 3, Alpha = 0.2,
        }, Context(store));

        Assert.That(result.IsOk, Is.True, result.Message);
        var json = System.Text.Json.JsonSerializer.Serialize(result.Data, new System.Text.Json.JsonSerializerOptions
        {
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        });
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        double aci = doc.RootElement.GetProperty("AdaptiveCoverage").GetDouble();
        Assert.That(doc.RootElement.GetProperty("Points").GetInt32(), Is.GreaterThan(400));
        Assert.That(aci, Is.EqualTo(0.8).Within(0.05), result.Human);
    }

    [Test]
    public void Forecasts_NeverUseTheTestSessionsOwnBars()
    {
        var days = Weekdays(6);
        var clean = days.Select((d, k) => Session("RW", d, RandomWalk(700 + k)).ToList()).ToList();
        var args = new EvidenceConformalArgs { Horizon = 5, Window = 10, WarmupDays = 3, MinSimilarity = 0.5 };
        var basePoints = EvidenceConformalOperation.AnalogPoints(clean, args);

        // Change only the last session's final hour: the first forecasts of that day, made before it, must not move.
        var changed = clean.Select(s => s.ToList()).ToList();
        var last = changed[^1];
        for (int i = last.Count - 60; i < last.Count; i++)
            last[i] = new IdiotProof.Models.Candle { Symbol = "RW", StartUtc = last[i].StartUtc, EndUtc = last[i].EndUtc, Open = 50m, High = 50m, Low = 50m, Close = 50m, Volume = 1000 };
        var changedPoints = EvidenceConformalOperation.AnalogPoints(changed, args);

        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(last[0].StartUtc, IdiotProof.Scripting.MarketTime.Eastern));
        var early = basePoints.Where(p => p.Day == day && p.ResolveTime < last[^60].StartUtc).ToList();
        var earlyChanged = changedPoints.Where(p => p.Day == day && p.ResolveTime < last[^60].StartUtc).ToList();
        Assert.That(early, Is.Not.Empty);
        Assert.That(earlyChanged.Select(p => (p.CallTime, p.Forecast, p.Outcome)), Is.EqualTo(early.Select(p => (p.CallTime, p.Forecast, p.Outcome))));
    }

    [Test]
    public async Task RejectsBadAlpha()
    {
        var result = await new EvidenceConformalOperation().ExecuteAsync(new EvidenceConformalArgs { Symbols = ["X"], Alpha = 1.5 }, Context(new FakeBarStore()));
        Assert.That(result.IsOk, Is.False);
    }
}
