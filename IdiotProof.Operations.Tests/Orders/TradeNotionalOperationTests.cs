using IdiotProof.Core.Trading;
using IdiotProof.Models;
using IdiotProof.Operations.Orders;

namespace IdiotProof.Operations.Tests.Orders;

/// <summary>trade notional (IP-T116): a dollar-sized DAY market order through the gateway.</summary>
public class TradeNotionalOperationTests
{
    private OrdersTestKit kit = null!;

    [SetUp]
    public void SetUp() => kit = new OrdersTestKit();

    [Test]
    public async Task Buy_SendsNotional_NoShareCount()
    {
        var result = await new TradeNotionalOperation().ExecuteAsync(new TradeNotionalArgs { Symbol = "aapl", Amount = 500m, Stop = 95m, Mode = BrokerMode.Paper }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var o = kit.Paper.Placed.Single();
        Assert.Multiple(() =>
        {
            Assert.That(o.Notional, Is.EqualTo(500m));
            Assert.That(o.SizeQuantity, Is.EqualTo(0m));
            Assert.That((o.Side, o.Type, o.TimeInForce), Is.EqualTo((OrderSide.Buy, OrderType.Market, "DAY")));
            Assert.That(result.Human, Does.Contain("BUY $500 AAPL MKT DAY").And.Contain("est. cost  $500.00"));
        });
    }

    [Test]
    public async Task Sell_IsAnExit_NoStopNeeded()
    {
        var result = await new TradeNotionalOperation().ExecuteAsync(new TradeNotionalArgs { Symbol = "AAPL", Amount = 250m, Side = OrderSide.Sell, Mode = BrokerMode.Paper }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(kit.Paper.Placed.Single().PositionIntent, Is.EqualTo("sell_to_close"));
    }

    [Test]
    public async Task Buy_WithoutAStop_IsVetoed()
    {
        var result = await new TradeNotionalOperation().ExecuteAsync(new TradeNotionalArgs { Symbol = "AAPL", Amount = 500m, Mode = BrokerMode.Paper }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Blocked));
        Assert.That(kit.Paper.Placed, Is.Empty);
    }

    [Test]
    public async Task Live_NeedsACode()
    {
        var result = await new TradeNotionalOperation().ExecuteAsync(new TradeNotionalArgs { Symbol = "AAPL", Amount = 50m, Stop = 95m, Mode = BrokerMode.Live }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.ConfirmationRequired), result.Message);
        Assert.That(kit.Live.Placed, Is.Empty);
    }
}
