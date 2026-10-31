using IdiotProof.Core.Trading;
using IdiotProof.Models;
using IdiotProof.Operations.Orders;

namespace IdiotProof.Operations.Tests.Orders;

/// <summary>orders place (IP-T301): the full single-order surface through the real gateway, onto fake brokers.</summary>
public class OrdersPlaceOperationTests
{
    private OrdersTestKit kit = null!;

    [SetUp]
    public void SetUp() => kit = new OrdersTestKit();

    [Test]
    public async Task StopLimitGtc_WithClientIdAndIntent_ReachesThePaperBrokerAsGiven()
    {
        var result = await new OrdersPlaceOperation().ExecuteAsync(new OrdersPlaceArgs
        {
            Symbol = "nvda", Side = OrderSide.Sell, Quantity = 10, StopPrice = 120m, Limit = 119.5m, TimeInForce = "gtc",
            ClientId = "exit-nvda-1", Intent = "sell-to-close", Mode = BrokerMode.Paper,
        }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var o = kit.Paper.Placed.Single();
        Assert.Multiple(() =>
        {
            Assert.That(o.Symbol, Is.EqualTo("NVDA"));
            Assert.That(o.Type, Is.EqualTo(OrderType.StopLimit), "type inferred from --stop-price + --limit");
            Assert.That((o.StopPrice, o.LimitPrice), Is.EqualTo((120m, 119.5m)));
            Assert.That(o.TimeInForce, Is.EqualTo("GTC"));
            Assert.That(o.ClientOrderId, Is.EqualTo("exit-nvda-1"));
            Assert.That(o.PositionIntent, Is.EqualTo("sell_to_close"));
            Assert.That(result.Human, Does.Contain("SELL 10 NVDA STP 120 LMT 119.5").And.Contain("id exit-nvda-1"));
        });
    }

    [Test]
    public async Task FractionalQty_GoesToFractionalQuantity_WholeQtyStaysAnInt()
    {
        await new OrdersPlaceOperation().ExecuteAsync(new OrdersPlaceArgs { Symbol = "AAPL", Side = OrderSide.Sell, Quantity = 0.25m, Mode = BrokerMode.Paper }, kit.Context());
        await new OrdersPlaceOperation().ExecuteAsync(new OrdersPlaceArgs { Symbol = "AAPL", Side = OrderSide.Sell, Quantity = 3m, Mode = BrokerMode.Paper }, kit.Context());

        Assert.That((kit.Paper.Placed[0].Quantity, kit.Paper.Placed[0].FractionalQuantity), Is.EqualTo((0, (decimal?)0.25m)));
        Assert.That((kit.Paper.Placed[1].Quantity, kit.Paper.Placed[1].FractionalQuantity), Is.EqualTo((3, (decimal?)null)));
    }

    [Test]
    public async Task TrailingStop_ByDollars()
    {
        var result = await new OrdersPlaceOperation().ExecuteAsync(new OrdersPlaceArgs
        {
            Symbol = "TSLA", Side = OrderSide.Sell, Quantity = 5, TrailPrice = 2.5m, TimeInForce = "gtc", Mode = BrokerMode.Paper,
        }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(kit.Paper.Placed.Single().Type, Is.EqualTo(OrderType.TrailingStop));
        Assert.That(kit.Paper.Placed.Single().TrailPrice, Is.EqualTo(2.5m));
    }

    [Test]
    public async Task Notional_Gtc_IsBlockedByTheGateway()
    {
        var result = await new OrdersPlaceOperation().ExecuteAsync(new OrdersPlaceArgs
        {
            Symbol = "AAPL", Side = OrderSide.Buy, Notional = 500m, TimeInForce = "gtc", Stop = 90m, Mode = BrokerMode.Paper,
        }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Blocked));
        Assert.That(result.Message, Does.Contain("DAY only"));
        Assert.That(kit.Paper.Placed, Is.Empty);
    }

    [Test]
    public void StrayPrice_ForTheType_IsAUsageError()
    {
        var ex = Assert.ThrowsAsync<OperationException>(() => new OrdersPlaceOperation().ExecuteAsync(new OrdersPlaceArgs
        {
            Symbol = "AAPL", Side = OrderSide.Buy, Quantity = 1, Type = OrderType.Market, Limit = 100m,
        }, kit.Context()));
        Assert.That(ex!.Message, Does.Contain("--limit doesn't apply to a market order"));
    }

    [Test]
    public async Task Live_NeedsACode_AndTheCodeOnlyFitsTheSameOrder()
    {
        var args = new OrdersPlaceArgs { Symbol = "NVDA", Side = OrderSide.Buy, Quantity = 2, Limit = 100m, Stop = 98m, TimeInForce = "gtc", Mode = BrokerMode.Live };

        var first = await new OrdersPlaceOperation().ExecuteAsync(args, kit.Context());
        Assert.That(first.Status, Is.EqualTo(OperationStatus.ConfirmationRequired), first.Message);

        var changedTif = await new OrdersPlaceOperation().ExecuteAsync(args with { TimeInForce = "day" }, kit.Context(first.Confirmation!.Code));
        Assert.That(changedTif.Status, Is.EqualTo(OperationStatus.Blocked), "a different TIF is a different order");
        Assert.That(kit.Live.Placed, Is.Empty);

        var same = await new OrdersPlaceOperation().ExecuteAsync(args, kit.Context(first.Confirmation.Code));
        Assert.That(same.IsOk, Is.True, same.Message);
        Assert.That(kit.Live.Placed.Single().TimeInForce, Is.EqualTo("GTC"));
    }
}
