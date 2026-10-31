using IdiotProof.Core.Trading;
using IdiotProof.Operations.Positions;

namespace IdiotProof.Operations.Tests.Positions;

/// <summary>positions close-all (IP-T306): every position out through the gateway, cancels first on request.</summary>
public class PositionsCloseAllTests
{
    [Test]
    public async Task Preview_ListsEverythingThatWouldClose_AndSendsNothing()
    {
        var kit = new PositionsKit();
        kit.Held[BrokerMode.Paper] = PositionsKit.Array(PositionsKit.Position("AAPL", 3), PositionsKit.Position("TSLA", -2));

        var result = await new PositionsCloseAllOperation().ExecuteAsync(new PositionsCloseAllArgs { CancelOrders = true, Preview = true }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("CANCEL ALL open orders").And.Contain("SELL 3 AAPL MKT").And.Contain("BUY 2 TSLA MKT").And.Contain("Preview only"));
        Assert.That(kit.Brokers[BrokerMode.Paper].Placed, Is.Empty);
        Assert.That(kit.Brokers[BrokerMode.Paper].CancelledAll, Is.Empty);
    }

    [Test]
    public async Task Paper_CancelsOrdersFirst_ThenClosesEveryPosition()
    {
        var kit = new PositionsKit();
        kit.Held[BrokerMode.Paper] = PositionsKit.Array(PositionsKit.Position("AAPL", 3), PositionsKit.Position("NVDA", 10));

        var result = await new PositionsCloseAllOperation().ExecuteAsync(new PositionsCloseAllArgs { CancelOrders = true }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(kit.Brokers[BrokerMode.Paper].CancelledAll, Is.EqualTo(new string?[] { null }), "cancel every symbol's orders");
        Assert.That(kit.Brokers[BrokerMode.Paper].Placed.Select(o => o.Symbol), Is.EqualTo(new[] { "AAPL", "NVDA" }));
        Assert.That(result.Human, Does.Contain("2 orders cancelled").And.Contain("3 exits sent."));
    }

    [Test]
    public async Task Live_OneCodeForTheWholeSet_NothingSentUntilConfirmed()
    {
        var kit = new PositionsKit();
        kit.Held[BrokerMode.Live] = PositionsKit.Array(PositionsKit.Position("AAPL", 3));
        var args = new PositionsCloseAllArgs { CancelOrders = true, Mode = BrokerMode.Live };

        var first = await new PositionsCloseAllOperation().ExecuteAsync(args, kit.Context());

        Assert.That(first.Status, Is.EqualTo(OperationStatus.ConfirmationRequired), first.Message);
        Assert.That(first.Confirmation!.Preview, Does.Contain("CANCEL ALL").And.Contain("SELL 3 AAPL"));
        Assert.That(kit.Brokers[BrokerMode.Live].Placed, Is.Empty);
        Assert.That(kit.Brokers[BrokerMode.Live].CancelledAll, Is.Empty);

        var wrong = await new PositionsCloseAllOperation().ExecuteAsync(args with { CancelOrders = false }, kit.Context(first.Confirmation.Code));
        Assert.That(wrong.Status, Is.EqualTo(OperationStatus.Blocked), "the code covers exactly the previewed set");

        var again = await new PositionsCloseAllOperation().ExecuteAsync(args, kit.Context());
        var confirmed = await new PositionsCloseAllOperation().ExecuteAsync(args, kit.Context(again.Confirmation!.Code));

        Assert.That(confirmed.IsOk, Is.True, confirmed.Message);
        Assert.That(kit.Brokers[BrokerMode.Live].CancelledAll, Has.Count.EqualTo(1));
        Assert.That(kit.Brokers[BrokerMode.Live].Placed.Single().Symbol, Is.EqualTo("AAPL"));

        var replay = await new PositionsCloseAllOperation().ExecuteAsync(args, kit.Context(again.Confirmation.Code));
        Assert.That(replay.Status, Is.EqualTo(OperationStatus.Blocked), "a code works once");
        Assert.That(kit.Brokers[BrokerMode.Live].Placed, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task NothingHeld_IsOk()
    {
        var kit = new PositionsKit();

        var result = await new PositionsCloseAllOperation().ExecuteAsync(new PositionsCloseAllArgs(), kit.Context());

        Assert.That(result.IsOk, Is.True);
        Assert.That(result.Human, Does.Contain("Nothing to close"));
    }
}
