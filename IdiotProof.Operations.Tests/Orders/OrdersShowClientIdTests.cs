using System.Net;
using IdiotProof.Operations.Account;
using IdiotProof.Operations.Orders;

namespace IdiotProof.Operations.Tests.Orders;

/// <summary>orders show --client-id and nested legs (IP-T304).</summary>
public class OrdersShowClientIdTests
{
    private OrdersTestKit kit = null!;

    [SetUp]
    public void SetUp() => kit = new OrdersTestKit();

    [Test]
    public async Task ByClientId_UsesAlpacasLookup_AndRendersTheBracketLegs()
    {
        var legs = string.Join(",",
            OrdersTestKit.OrderJson("tp-1", side: "sell", limit: "110", status: "held"),
            OrdersTestKit.OrderJson("sl-1", side: "sell", type: "stop", limit: null, status: "held"));
        kit.Alpaca.Paper.Routes["GET /v2/orders:by_client_order_id"] = (HttpStatusCode.OK,
            OrdersTestKit.OrderJson("b-1", orderClass: "bracket", clientId: "entry-nvda", legs: legs));

        var result = await new OrdersShowOperation().ExecuteAsync(new OrdersShowArgs { ClientId = "entry-nvda", Mode = AccountScope.Paper }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(kit.Alpaca.Paper.Calls.Single(), Does.Contain("client_order_id=entry-nvda").And.Contain("nested=true"));
            Assert.That(result.Human, Does.Contain("bracket").And.Contain("Legs (2)"));
            Assert.That(result.Human, Does.Contain("tp-1").And.Contain("LMT 110").And.Contain("sl-1").And.Contain("held"));
        });
    }

    [Test]
    public async Task IdAndClientId_Together_IsAUsageError()
    {
        var result = await new OrdersShowOperation().ExecuteAsync(new OrdersShowArgs { Id = "x", ClientId = "y" }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.UsageError));
    }

    [Test]
    public async Task List_ShowsLegsIndentedUnderTheirParent()
    {
        kit.Alpaca.Paper.Routes["GET /v2/orders"] = (HttpStatusCode.OK, "[" + OrdersTestKit.OrderJson("o-1", orderClass: "oco",
            legs: OrdersTestKit.OrderJson("o-1-sl", side: "sell", type: "stop", limit: null, status: "held")) + "]");

        var result = await new OrdersListOperation().ExecuteAsync(new OrdersListArgs { Mode = AccountScope.Paper }, kit.Context());

        Assert.That(result.Human, Does.Contain("  -> NVDA"));
        Assert.That(result.Human!.IndexOf("o-1-sl", StringComparison.Ordinal), Is.GreaterThan(result.Human.IndexOf("oco", StringComparison.Ordinal)));
    }
}
