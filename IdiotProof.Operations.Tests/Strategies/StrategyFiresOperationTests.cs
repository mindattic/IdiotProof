using IdiotProof.Operations.Strategies;

namespace IdiotProof.Operations.Tests.Strategies;

/// <summary>strategy fires (IP-T160): recent gate verdicts.</summary>
public class StrategyFiresOperationTests
{
    [Test]
    public async Task Fires_ShowsTheGateVerdicts_AndWhereItStandsNow()
    {
        var kit = new StrategyKit();
        var s = await kit.AddAsync("NVDA", title: "VWAP", active: true);
        await kit.AddProgressAsync(s.Id, 0, 1, "IsAboveVwap");
        var t = new DateTime(2026, 10, 29, 14, 0, 0, DateTimeKind.Utc);
        await kit.AddAuditAsync("signal-fire", "[VWAP] NVDA all 1 conditions met — entering gate checks", t);
        await kit.AddAuditAsync("signal-blocked", "[VWAP] NVDA blocked by RiskGuardian: daily loss limit reached", t.AddSeconds(1));
        await kit.AddAuditAsync("holding", "[VWAP] HOLDING 1 NVDA entry 1 → now 2", t.AddSeconds(2));
        await kit.AddAuditAsync("signal-vetoed", "[Other] NVDA vetoed by LLM panel", t.AddSeconds(3));

        var result = await new StrategyFiresOperation().ExecuteAsync(new StrategyFiresArgs { Strategy = "NVDA" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("BLOCKED").And.Contain("daily loss limit reached").And.Contain("all met"));
        Assert.That(result.Human, Does.Not.Contain("HOLDING").And.Not.Contain("VETOED"), "only this strategy's gate verdicts");
        Assert.That(result.Human, Does.Contain("0/1 conditions; waiting on IsAboveVwap"));
        Assert.That(result.Human!.IndexOf("BLOCKED", StringComparison.Ordinal), Is.LessThan(result.Human.IndexOf("all met", StringComparison.Ordinal)), "newest first");
    }

    [Test]
    public async Task Fires_WithNoVerdicts_SaysWhy()
    {
        var kit = new StrategyKit();
        await kit.AddAsync("NVDA");
        var result = await new StrategyFiresOperation().ExecuteAsync(new StrategyFiresArgs { Strategy = "NVDA" }, kit.Context());
        Assert.That(result.Human, Does.Contain("never all passed together").And.Contain("inactive"));
    }
}
