using IdiotProof.Core.Trading;
using IdiotProof.Operations.Account;
using IdiotProof.Operations.Positions;

namespace IdiotProof.Operations.Tests.Positions;

/// <summary>positions show (IP-T105): read-only, scripted Alpaca, EF InMemory strategies and diary.</summary>
public class PositionsShowTests
{
    [Test]
    public async Task Show_JoinsTheBrokerPositionWithItsStrategyAndDiaryRow()
    {
        var kit = new PositionsKit();
        kit.Held[BrokerMode.Paper] = PositionsKit.Array(PositionsKit.Position("NVDA", 10, avg: 120m, price: 131.2m));
        var s = await kit.AddStrategyAsync("NVDA", BrokerMode.Paper, 10, title: "NVDA gap");
        await kit.AddDiaryAsync(s, isPaper: true);

        var result = await new PositionsShowOperation().ExecuteAsync(new PositionsShowArgs { Symbol = "nvda" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("Paper account — NVDA").And.Contain("10 long").And.Contain("$131.20"));
        Assert.That(result.Human, Does.Contain("'NVDA gap'").And.Contain("books 10 shares"));
        Assert.That(result.Human, Does.Contain("Open · long 10 @ $10.00"));
        Assert.That(result.Human, Does.Contain("Live account — NVDA").And.Contain("flat"));
        Assert.That(result.Human, Does.Not.Contain("MISMATCH"));
        Assert.That(kit.Requests.Select(r => r.Path), Is.All.EqualTo("/v2/positions/NVDA"));
        Assert.That(kit.Requests.Select(r => r.Method), Is.All.EqualTo(HttpMethod.Get));
    }

    [Test]
    public async Task Show_FlagsAStrategyThatBooksSharesTheBrokerDoesNotHold()
    {
        var kit = new PositionsKit();
        await kit.AddStrategyAsync("HIHO", BrokerMode.Paper, 351);

        var result = await new PositionsShowOperation().ExecuteAsync(new PositionsShowArgs { Symbol = "HIHO", Mode = AccountScope.Paper }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("flat").And.Contain("MISMATCH").And.Contain("strategies book 351 shares, the broker holds 0"));
        Assert.That(result.Human, Does.Not.Contain("Live account"));
    }

    [Test]
    public async Task Show_AnAccountWithoutKeysIsReportedNotFatal_WhenBothAreAsked()
    {
        var kit = new PositionsKit();
        kit.Refused.Add(BrokerMode.Live);
        kit.Held[BrokerMode.Paper] = PositionsKit.Array(PositionsKit.Position("AAPL", 3));

        var result = await new PositionsShowOperation().ExecuteAsync(new PositionsShowArgs { Symbol = "AAPL" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("No live Alpaca keys configured").And.Contain("3 long"));
        Assert.That(result.Human, Does.Contain("none — no IdiotProof strategy owns this position"));
    }
}
