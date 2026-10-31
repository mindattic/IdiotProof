using System.Net;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Orders;

namespace IdiotProof.Operations.Tests.Orders;

/// <summary>
/// orders cancel / cancel-all (IP-T108): the order is read from the scripted Alpaca account, the cancel
/// goes through the real gateway onto a fake broker.
/// </summary>
public class OrdersCancelOperationTests
{
    private OrdersTestKit kit = null!;

    [SetUp]
    public void SetUp() => kit = new OrdersTestKit();

    [Test]
    public async Task Cancel_Paper_ReadsTheOrder_ThenCancelsItThroughTheGateway()
    {
        kit.Alpaca.Paper.Routes["GET /v2/orders/o-1"] = (HttpStatusCode.OK, OrdersTestKit.OrderJson("o-1"));

        var result = await new OrdersCancelOperation().ExecuteAsync(new OrdersCancelArgs { Id = "o-1" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(kit.Paper.Cancelled, Is.EqualTo(new[] { "o-1" }));
        Assert.That(result.Human, Does.Contain("CANCEL order o-1 (NVDA)").And.Contain("Cancel accepted"));
    }

    [Test]
    public async Task Cancel_AFilledOrder_IsRefusedBeforeTheGateway()
    {
        kit.Alpaca.Paper.Routes["GET /v2/orders/o-2"] = (HttpStatusCode.OK, OrdersTestKit.OrderJson("o-2", status: "filled"));

        var result = await new OrdersCancelOperation().ExecuteAsync(new OrdersCancelArgs { Id = "o-2" }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Failed));
        Assert.That(result.Message, Does.Contain("is filled"));
        Assert.That(kit.Paper.Cancelled, Is.Empty);
    }

    [Test]
    public async Task Cancel_Live_NeedsACode_ThenCancels()
    {
        kit.Alpaca.Live.Routes["GET /v2/orders/l-1"] = (HttpStatusCode.OK, OrdersTestKit.OrderJson("l-1"));
        var args = new OrdersCancelArgs { Id = "l-1", Mode = BrokerMode.Live };

        var first = await new OrdersCancelOperation().ExecuteAsync(args, kit.Context());
        Assert.That(first.Status, Is.EqualTo(OperationStatus.ConfirmationRequired), first.Message);
        Assert.That(kit.Live.Cancelled, Is.Empty);

        var second = await new OrdersCancelOperation().ExecuteAsync(args, kit.Context(first.Confirmation!.Code));
        Assert.That(second.IsOk, Is.True, second.Message);
        Assert.That(kit.Live.Cancelled, Is.EqualTo(new[] { "l-1" }));
    }

    [Test]
    public async Task CancelAll_PassesTheSymbol_OrNothingForEveryOrder()
    {
        var one = await new OrdersCancelAllOperation().ExecuteAsync(new OrdersCancelAllArgs { Symbol = "nvda" }, kit.Context());
        var all = await new OrdersCancelAllOperation().ExecuteAsync(new OrdersCancelAllArgs(), kit.Context());

        Assert.That(one.IsOk && all.IsOk, Is.True, one.Message + all.Message);
        Assert.That(kit.Paper.CancelAllFor, Is.EqualTo(new string?[] { "NVDA", null }));
        Assert.That(all.Human, Does.Contain("CANCEL ALL open orders · Paper").And.Contain("2 order(s) cancelled"));
    }
}
