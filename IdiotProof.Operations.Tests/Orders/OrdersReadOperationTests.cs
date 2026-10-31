using System.Net;
using IdiotProof.Operations.Account;
using IdiotProof.Operations.Orders;

namespace IdiotProof.Operations.Tests.Orders;

/// <summary>orders list / orders show (IP-T107) against scripted Alpaca paper and live accounts.</summary>
public class OrdersReadOperationTests
{
    private OrdersTestKit kit = null!;

    [SetUp]
    public void SetUp() => kit = new OrdersTestKit();

    [Test]
    public async Task List_ReadsBothAccounts_NewestFirst_WithAnAccountColumn()
    {
        kit.Alpaca.Paper.Routes["GET /v2/orders"] = (HttpStatusCode.OK, "[" + OrdersTestKit.OrderJson("p-1", submitted: "2026-10-30T14:05:00Z") + "]");
        kit.Alpaca.Live.Routes["GET /v2/orders"] = (HttpStatusCode.OK, "[" + OrdersTestKit.OrderJson("l-1", symbol: "AAPL", submitted: "2026-10-30T15:30:00Z") + "]");

        var result = await new OrdersListOperation().ExecuteAsync(new OrdersListArgs { Symbol = "nvda,aapl" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var lines = result.Human!.Split('\n');
        Assert.Multiple(() =>
        {
            Assert.That(lines[0], Is.EqualTo("2 open orders"));
            Assert.That(lines[1], Does.StartWith("Acct"));
            Assert.That(lines.Skip(2).First(l => l.Contains("l-1")), Does.Contain("Live").And.Contain("11:30 AM"), "times are ET");
            Assert.That(Array.FindIndex(lines, l => l.Contains("l-1")), Is.LessThan(Array.FindIndex(lines, l => l.Contains("p-1"))));
            Assert.That(result.Human, Does.Contain("LMT 100"));
            Assert.That(kit.Alpaca.Paper.Calls.Single(), Does.Contain("status=open").And.Contain("nested=true").And.Contain("symbols=NVDA%2CAAPL"));
        });
    }

    [Test]
    public async Task List_OneAccountFailing_StillShowsTheOther()
    {
        kit.Alpaca.Paper.Routes["GET /v2/orders"] = (HttpStatusCode.OK, "[]");
        kit.Alpaca.Live.Routes["GET /v2/orders"] = (HttpStatusCode.Forbidden, """{"code":40310000,"message":"forbidden"}""");

        var result = await new OrdersListOperation().ExecuteAsync(new OrdersListArgs { Status = OrderStatusFilter.Closed }, kit.Context());

        Assert.That(result.IsOk, Is.True);
        Assert.That(result.Human, Does.Contain("Live account: Alpaca 403").And.Contain("No closed orders."));
    }

    [Test]
    public async Task Show_FindsTheOrderInWhicheverAccountHasIt()
    {
        kit.Alpaca.Live.Routes["GET /v2/orders/l-9"] = (HttpStatusCode.OK,
            OrdersTestKit.OrderJson("l-9", status: "filled", filledQty: "10", avg: "99.5", filledAt: "2026-10-30T14:06:00Z"));

        var result = await new OrdersShowOperation().ExecuteAsync(new OrdersShowArgs { Id = "l-9" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Human, Does.Contain("Live").And.Contain("10/10 @ $99.50").And.Contain("filled").And.Contain("10:06 AM"));
            Assert.That(kit.Alpaca.Paper.Calls.Single(), Does.StartWith("GET /v2/orders/l-9"), "paper was asked first and said not found");
        });
    }

    [Test]
    public async Task Show_UnknownEverywhere_Fails()
    {
        var result = await new OrdersShowOperation().ExecuteAsync(new OrdersShowArgs { Id = "nope", Mode = AccountScope.Paper }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Failed));
        Assert.That(result.Message, Does.Contain("not found"));
    }
}
