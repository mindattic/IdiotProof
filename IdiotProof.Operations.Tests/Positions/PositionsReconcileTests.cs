using IdiotProof.Core.Trading;
using IdiotProof.Operations.Positions;

namespace IdiotProof.Operations.Tests.Positions;

/// <summary>positions reconcile (IP-T106): broker vs strategy bookkeeping, read-only.</summary>
public class PositionsReconcileTests
{
    [Test]
    public void Reconciler_ClassifiesEveryCase()
    {
        var strategies = new[]
        {
            new IdiotProof.Core.Data.Strategy { Symbol = "HIHO", BrokerMode = "Paper", PositionQty = 351 },
            new IdiotProof.Core.Data.Strategy { Symbol = "NVDA", BrokerMode = "Paper", PositionQty = 10 },
            new IdiotProof.Core.Data.Strategy { Symbol = "AMD", BrokerMode = "Paper", PositionQty = 5 },
            new IdiotProof.Core.Data.Strategy { Symbol = "TSLA", BrokerMode = "Live", PositionQty = 7 },
            new IdiotProof.Core.Data.Strategy { Symbol = "MSFT", BrokerMode = "Sandbox", PositionQty = 2 },
        };
        var held = new[]
        {
            Parse(PositionsKit.Position("NVDA", 10)), Parse(PositionsKit.Position("AMD", 8)),
            Parse(PositionsKit.Position("INTC", 4)), Parse(PositionsKit.Position("MSFT", 2)),
        };

        var rows = PositionReconciler.Compare(BrokerMode.Paper, held, strategies).ToDictionary(r => r.Symbol);

        Assert.Multiple(() =>
        {
            Assert.That(rows.Keys, Is.EquivalentTo(new[] { "AMD", "HIHO", "INTC", "MSFT", "NVDA" }));
            Assert.That(rows["HIHO"].Status, Is.EqualTo(ReconcileStatus.Phantom));
            Assert.That(rows["NVDA"].Status, Is.EqualTo(ReconcileStatus.Ok));
            Assert.That(rows["AMD"].Status, Is.EqualTo(ReconcileStatus.Mismatch));
            Assert.That(rows["AMD"].Difference, Is.EqualTo(3m));
            Assert.That(rows["INTC"].Status, Is.EqualTo(ReconcileStatus.Untracked));
            Assert.That(rows["MSFT"].Status, Is.EqualTo(ReconcileStatus.Untracked), "a Sandbox strategy never books an Alpaca position");
        });
    }

    [Test]
    public async Task Reconcile_ReportsTheHihoPhantom_AndChangesNothing()
    {
        var kit = new PositionsKit();
        kit.Held[BrokerMode.Paper] = PositionsKit.Array(PositionsKit.Position("NVDA", 10));
        await kit.AddStrategyAsync("HIHO", BrokerMode.Paper, 351);
        await kit.AddStrategyAsync("NVDA", BrokerMode.Paper, 10);
        await kit.AddStrategyAsync("GME", BrokerMode.Sandbox, 4);

        var result = await new PositionsReconcileOperation().ExecuteAsync(new PositionsReconcileArgs { ProblemsOnly = true }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("HIHO").And.Contain("phantom").And.Contain("-351"));
        Assert.That(result.Human, Does.Not.Contain("NVDA"), "--problems hides the rows that agree");
        Assert.That(result.Human, Does.Contain("1 agree, 1 disagree."));
        Assert.That(result.Human, Does.Contain("1 Sandbox strategies"));
        Assert.That(kit.Requests.Select(r => r.Method), Is.All.EqualTo(HttpMethod.Get));
        Assert.That(kit.Brokers.Values.SelectMany(b => b.Placed), Is.Empty);

        await using var db = await kit.Db.CreateDbContextAsync();
        Assert.That(db.Strategies.Single(s => s.Symbol == "HIHO").PositionQty, Is.EqualTo(351), "reconcile is read-only");
    }

    private static BrokerPosition Parse(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return BrokerPosition.Parse(doc.RootElement);
    }
}
