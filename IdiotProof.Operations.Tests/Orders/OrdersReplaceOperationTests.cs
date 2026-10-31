using System.Net;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Orders;

namespace IdiotProof.Operations.Tests.Orders;

/// <summary>orders replace (IP-T303): read the working order, PATCH only the changes through the gateway.</summary>
public class OrdersReplaceOperationTests
{
    private OrdersTestKit kit = null!;

    [SetUp]
    public void SetUp() => kit = new OrdersTestKit();

    [Test]
    public async Task Replace_SendsOnlyTheChanges_KeepingTheOrdersTrailUnit()
    {
        kit.Alpaca.Paper.Routes["GET /v2/orders/t-1"] = (HttpStatusCode.OK,
            OrdersTestKit.OrderJson("t-1", side: "sell", type: "trailing_stop", limit: null, trailPercent: "3"));

        var result = await new OrdersReplaceOperation().ExecuteAsync(new OrdersReplaceArgs { Id = "t-1", Trail = 2m, Quantity = 4m }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var (id, changes) = kit.Paper.Replaced.Single();
        Assert.Multiple(() =>
        {
            Assert.That(id, Is.EqualTo("t-1"));
            Assert.That(changes.TrailPercent, Is.EqualTo(2m));
            Assert.That(changes.TrailPrice, Is.Null);
            Assert.That(changes.Quantity, Is.EqualTo(4));
            Assert.That(changes.TimeInForce, Is.Empty, "TIF unchanged");
            Assert.That(result.Human, Does.Contain("REPLACE order t-1 (NVDA) -> qty 4, trail 2%").And.Contain("new order t-1-r"));
        });
    }

    [Test]
    public async Task Replace_ALimitOnAMarketOrder_IsAUsageError()
    {
        kit.Alpaca.Paper.Routes["GET /v2/orders/m-1"] = (HttpStatusCode.OK, OrdersTestKit.OrderJson("m-1", type: "market", limit: null));

        var result = await new OrdersReplaceOperation().ExecuteAsync(new OrdersReplaceArgs { Id = "m-1", Limit = 101m }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.UsageError));
        Assert.That(kit.Paper.Replaced, Is.Empty);
    }

    [Test]
    public async Task Replace_Live_CodeIsBoundToTheChange()
    {
        kit.Alpaca.Live.Routes["GET /v2/orders/l-2"] = (HttpStatusCode.OK, OrdersTestKit.OrderJson("l-2"));
        var args = new OrdersReplaceArgs { Id = "l-2", Limit = 101m, Mode = BrokerMode.Live };

        var first = await new OrdersReplaceOperation().ExecuteAsync(args, kit.Context());
        var otherPrice = await new OrdersReplaceOperation().ExecuteAsync(args with { Limit = 105m }, kit.Context(first.Confirmation!.Code));
        var same = await new OrdersReplaceOperation().ExecuteAsync(args, kit.Context(first.Confirmation.Code));

        Assert.That(first.Status, Is.EqualTo(OperationStatus.ConfirmationRequired));
        Assert.That(otherPrice.Status, Is.EqualTo(OperationStatus.Blocked));
        Assert.That(same.IsOk, Is.True, same.Message);
        Assert.That(kit.Live.Replaced.Single().Changes.LimitPrice, Is.EqualTo(101m));
    }
}
