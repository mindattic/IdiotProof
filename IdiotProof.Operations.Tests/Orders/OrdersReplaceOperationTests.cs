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
    [Test]
    public async Task Replace_RaisingAnEntrysSize_WithoutAStop_IsVetoedByTheRiskGuardian()
    {
        kit.Alpaca.Paper.Routes["GET /v2/orders/b-1"] = (HttpStatusCode.OK, OrdersTestKit.OrderJson("b-1", qty: "10"));

        var result = await new OrdersReplaceOperation().ExecuteAsync(new OrdersReplaceArgs { Id = "b-1", Quantity = 20m }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Blocked), result.Message);
        Assert.That(kit.Paper.Replaced, Is.Empty, "a bigger entry never reached the broker unchecked");
    }

    [Test]
    public async Task Replace_RaisingAnEntrysSize_PastTheRiskCap_IsVetoed_ButWithinItIsReplaced()
    {
        kit.Alpaca.Paper.Routes["GET /v2/orders/b-2"] = (HttpStatusCode.OK, OrdersTestKit.OrderJson("b-2", qty: "1"));

        // $1 of risk a share: 500 shares is $750 worst case, past the default $100 cap; 20 is $30.
        var tooBig = await new OrdersReplaceOperation().ExecuteAsync(new OrdersReplaceArgs { Id = "b-2", Quantity = 500m, Stop = 99m }, kit.Context());
        var fine = await new OrdersReplaceOperation().ExecuteAsync(new OrdersReplaceArgs { Id = "b-2", Quantity = 20m, Stop = 99m }, kit.Context());

        Assert.That(tooBig.Status, Is.EqualTo(OperationStatus.Blocked), tooBig.Message);
        Assert.That(fine.IsOk, Is.True, fine.Message);
        Assert.That(kit.Paper.Replaced.Single().Changes.Quantity, Is.EqualTo(20));
    }

    [Test]
    public async Task Replace_ShrinkingAnEntry_OrResizingAnExit_NeedsNoStop()
    {
        kit.Alpaca.Paper.Routes["GET /v2/orders/b-3"] = (HttpStatusCode.OK, OrdersTestKit.OrderJson("b-3", qty: "10"));
        kit.Alpaca.Paper.Routes["GET /v2/orders/s-3"] = (HttpStatusCode.OK, OrdersTestKit.OrderJson("s-3", side: "sell", qty: "10"));

        var smaller = await new OrdersReplaceOperation().ExecuteAsync(new OrdersReplaceArgs { Id = "b-3", Quantity = 5m }, kit.Context());
        var exit = await new OrdersReplaceOperation().ExecuteAsync(new OrdersReplaceArgs { Id = "s-3", Quantity = 15m }, kit.Context());

        Assert.That(smaller.IsOk, Is.True, smaller.Message);
        Assert.That(exit.IsOk, Is.True, exit.Message);
    }
}
