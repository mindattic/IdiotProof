using System.Net;
using IdiotProof.Operations.Account;
using IdiotProof.Operations.Orders;

namespace IdiotProof.Operations.Tests.Orders;

/// <summary>orders history (IP-T109): closed orders since N days ago, ET dates, fill totals.</summary>
public class OrdersHistoryOperationTests
{
    [Test]
    public async Task History_AsksForClosedOrdersSinceTheWindow_AndTotalsTheFills()
    {
        var kit = new OrdersTestKit();
        kit.Alpaca.Paper.Routes["GET /v2/orders"] = (HttpStatusCode.OK, "[" + string.Join(",",
            OrdersTestKit.OrderJson("f-1", status: "filled", filledQty: "10", avg: "100", filledAt: "2026-10-29T14:00:00Z"),
            OrdersTestKit.OrderJson("f-2", side: "sell", status: "filled", filledQty: "10", avg: "105", filledAt: "2026-10-30T19:59:00Z"),
            OrdersTestKit.OrderJson("c-1", status: "canceled")) + "]");

        var result = await new OrdersHistoryOperation().ExecuteAsync(new OrdersHistoryArgs { Days = 3, Mode = AccountScope.Paper },
            kit.Context(nowUtc: new DateTime(2026, 10, 30, 21, 0, 0, DateTimeKind.Utc)));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(kit.Alpaca.Paper.Calls.Single(), Does.Contain("status=closed").And.Contain("after=2026-10-27T21%3A00%3A00Z"));
            Assert.That(result.Human, Does.StartWith("3 closed orders since").And.Contain("2 filled, 1 canceled").And.Contain("bought $1,000.00, sold $1,050.00"));
            Assert.That(result.Human, Does.Contain("Closed (ET)").And.Contain("3:59 PM"), "fill time in ET");
            var lines = result.Human!.Split('\n');
            Assert.That(Array.FindIndex(lines, l => l.Contains("f-2")), Is.LessThan(Array.FindIndex(lines, l => l.Contains("f-1"))), "newest first");
        });
    }

    [Test]
    public async Task History_RejectsAnOutOfRangeWindow()
    {
        var result = await new OrdersHistoryOperation().ExecuteAsync(new OrdersHistoryArgs { Days = 0 }, new OrdersTestKit().Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.UsageError));
    }
}
