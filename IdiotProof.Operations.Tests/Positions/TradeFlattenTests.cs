using IdiotProof.Core.Trading;
using IdiotProof.Models;
using IdiotProof.Operations.Positions;

namespace IdiotProof.Operations.Tests.Positions;

/// <summary>
/// trade flatten (IP-T112): exits sized from the scripted broker
/// position, through the real TransactionGateway, placed on a recording fake broker.
/// </summary>
public class TradeFlattenTests
{
    [Test]
    public async Task Flatten_Strategy_ClosesWhatItBooks_CappedAtTheBroker()
    {
        var kit = new PositionsKit();
        kit.Held[BrokerMode.Paper] = PositionsKit.Array(PositionsKit.Position("AMD", 8));
        var s = await kit.AddStrategyAsync("AMD", BrokerMode.Paper, 5);

        var result = await new TradeFlattenOperation().ExecuteAsync(new TradeFlattenArgs { Strategy = "AMD" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(kit.Brokers[BrokerMode.Paper].Placed.Single().Quantity, Is.EqualTo(5), "only the strategy's 5 of the 8 held");
    }

    [Test]
    public async Task Flatten_StrategyWithAPhantomPosition_SendsNothing()
    {
        var kit = new PositionsKit();
        await kit.AddStrategyAsync("HIHO", BrokerMode.Paper, 351);

        var result = await new TradeFlattenOperation().ExecuteAsync(new TradeFlattenArgs { Strategy = "HIHO" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("holds none"));
        Assert.That(kit.Brokers[BrokerMode.Paper].Placed, Is.Empty);
    }

    [Test]
    public async Task Flatten_All_ClosesEveryLongAndShort_AndAFractionalLongExactly_SkippingCrypto()
    {
        var kit = new PositionsKit();
        kit.Held[BrokerMode.Paper] = PositionsKit.Array(
            PositionsKit.Position("AAPL", 3), PositionsKit.Position("TSLA", -2),
            PositionsKit.Position("BTC/USD", 0.5m, assetClass: "crypto"), PositionsKit.Position("SPY", 0.25m));

        var result = await new TradeFlattenOperation().ExecuteAsync(new TradeFlattenArgs { All = true }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(kit.Brokers[BrokerMode.Paper].Placed.Select(o => $"{o.Side} {o.SizeQuantity} {o.Symbol}"),
            Is.EqualTo(new[] { "Sell 3 AAPL", "Sell 0.25 SPY", "Buy 2 TSLA" }));
        Assert.That(kit.Brokers[BrokerMode.Paper].Placed.Single(o => o.Symbol == "SPY").FractionalQuantity, Is.EqualTo(0.25m));
        Assert.That(result.Human, Does.Contain("skipped BTC/USD"));
    }

    [Test]
    public async Task Flatten_AllLive_OneCodeForTheSet_RefusedOnceThePositionsChange()
    {
        var kit = new PositionsKit();
        kit.Held[BrokerMode.Live] = PositionsKit.Array(PositionsKit.Position("AAPL", 3), PositionsKit.Position("NVDA", 10));
        var args = new TradeFlattenArgs { All = true, Mode = BrokerMode.Live };

        var first = await new TradeFlattenOperation().ExecuteAsync(args, kit.Context());
        Assert.That(first.Status, Is.EqualTo(OperationStatus.ConfirmationRequired), first.Message);
        Assert.That(first.Confirmation!.Preview, Does.Contain("SELL 3 AAPL").And.Contain("SELL 10 NVDA"));

        kit.Held[BrokerMode.Live] = PositionsKit.Array(PositionsKit.Position("AAPL", 3), PositionsKit.Position("NVDA", 12));
        var changed = await new TradeFlattenOperation().ExecuteAsync(args, kit.Context(first.Confirmation.Code));
        Assert.That(changed.Status, Is.EqualTo(OperationStatus.Blocked));
        Assert.That(kit.Brokers[BrokerMode.Live].Placed, Is.Empty);

        var again = await new TradeFlattenOperation().ExecuteAsync(args, kit.Context());
        var confirmed = await new TradeFlattenOperation().ExecuteAsync(args, kit.Context(again.Confirmation!.Code));

        Assert.That(confirmed.IsOk, Is.True, confirmed.Message);
        Assert.That(kit.Brokers[BrokerMode.Live].Placed.Select(o => o.Quantity), Is.EqualTo(new[] { 3, 12 }));
    }

    [Test]
    public void Flatten_NeedsExactlyOneTarget()
    {
        var kit = new PositionsKit();

        Assert.ThrowsAsync<OperationException>(() => new TradeFlattenOperation().ExecuteAsync(new TradeFlattenArgs(), kit.Context()));
        Assert.ThrowsAsync<OperationException>(() => new TradeFlattenOperation().ExecuteAsync(new TradeFlattenArgs { All = true, Symbol = "AAPL" }, kit.Context()));
    }
}
