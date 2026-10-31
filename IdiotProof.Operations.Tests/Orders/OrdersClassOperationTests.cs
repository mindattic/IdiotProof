using IdiotProof.Core.Trading;
using IdiotProof.Models;
using IdiotProof.Operations.Orders;

namespace IdiotProof.Operations.Tests.Orders;

/// <summary>orders bracket / oco / oto (IP-T302) through the real gateway onto fake brokers.</summary>
public class OrdersClassOperationTests
{
    private OrdersTestKit kit = null!;

    [SetUp]
    public void SetUp() => kit = new OrdersTestKit();

    [Test]
    public async Task Bracket_CarriesBothChildren_AndItsStopLossSatisfiesTheGuardian()
    {
        var result = await new OrdersBracketOperation().ExecuteAsync(new OrderClassArgs
        {
            Symbol = "NVDA", Side = OrderSide.Buy, Quantity = 10, Limit = 100m, TakeProfit = 110m, StopLoss = 98m, StopLossLimit = 97.5m,
            TimeInForce = "gtc", Mode = BrokerMode.Paper,
        }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var o = kit.Paper.Placed.Single();
        Assert.Multiple(() =>
        {
            Assert.That(o.OrderClass, Is.EqualTo(OrderClass.Bracket));
            Assert.That(o.TakeProfit!.LimitPrice, Is.EqualTo(110m));
            Assert.That((o.StopLoss!.StopPrice, o.StopLoss.LimitPrice), Is.EqualTo((98m, (decimal?)97.5m)));
            Assert.That(o.Type, Is.EqualTo(OrderType.Limit));
            Assert.That(result.Human, Does.Contain("BRACKET TP 110 SL 98/97.5"));
        });
    }

    [Test]
    public async Task Bracket_TakeProfitBelowTheStop_IsBlocked()
    {
        var result = await new OrdersBracketOperation().ExecuteAsync(new OrderClassArgs
        {
            Symbol = "NVDA", Side = OrderSide.Buy, Quantity = 1, Limit = 100m, TakeProfit = 95m, StopLoss = 98m, Mode = BrokerMode.Paper,
        }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Blocked));
        Assert.That(kit.Paper.Placed, Is.Empty);
    }

    [Test]
    public async Task Oco_IsAnExitLimitWithNoParentPrice()
    {
        var result = await new OrdersOcoOperation().ExecuteAsync(new OrderClassArgs
        {
            Symbol = "NVDA", Side = OrderSide.Sell, Quantity = 10, TakeProfit = 140m, StopLoss = 127m, TimeInForce = "gtc", Mode = BrokerMode.Paper,
        }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var o = kit.Paper.Placed.Single();
        Assert.That((o.OrderClass, o.Type, o.LimitPrice), Is.EqualTo((OrderClass.Oco, OrderType.Limit, (decimal?)null)));
    }

    [Test]
    public async Task Oto_WithOnlyATakeProfit_NeedsAPlannedStop()
    {
        var args = new OrderClassArgs { Symbol = "NVDA", Side = OrderSide.Buy, Quantity = 10, Limit = 100m, TakeProfit = 110m, Mode = BrokerMode.Paper };

        var noStop = await new OrdersOtoOperation().ExecuteAsync(args, kit.Context());
        var withStop = await new OrdersOtoOperation().ExecuteAsync(args with { Stop = 98m }, kit.Context());

        Assert.That(noStop.Status, Is.EqualTo(OperationStatus.Blocked));
        Assert.That(withStop.IsOk, Is.True, withStop.Message);
        Assert.That(kit.Paper.Placed.Single().OrderClass, Is.EqualTo(OrderClass.Oto));
        Assert.That(kit.Paper.Placed.Single().StopLoss, Is.Null);
    }

    [Test]
    public void Oto_WithBothChildren_IsAUsageError()
    {
        Assert.ThrowsAsync<OperationException>(() => new OrdersOtoOperation().ExecuteAsync(new OrderClassArgs
        {
            Symbol = "NVDA", Side = OrderSide.Buy, Quantity = 1, TakeProfit = 110m, StopLoss = 98m,
        }, kit.Context()));
    }

    [Test]
    public async Task LiveBracket_NeedsACode()
    {
        var result = await new OrdersBracketOperation().ExecuteAsync(new OrderClassArgs
        {
            Symbol = "NVDA", Side = OrderSide.Buy, Quantity = 1, Limit = 100m, TakeProfit = 110m, StopLoss = 98m, Mode = BrokerMode.Live,
        }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.ConfirmationRequired), result.Message);
        Assert.That(kit.Live.Placed, Is.Empty);
    }
}
