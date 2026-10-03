using IdiotProof.Brokers;
using IdiotProof.Models;
using IdiotProof.Monitor;

namespace IdiotProof.Monitor.Tests;

/// <summary>
/// The order shapes the Monitor places for longs and shorts, driven through the in-memory
/// SandboxBrokerClient (no network, no real broker).
/// </summary>
[TestFixture]
public sealed class DirectionalOrdersTests
{
    [Test]
    public void ShortEntry_IsASellToOpen_BelowThePrice()
    {
        var order = DirectionalOrders.Entry("TSLA", TradeDirection.Short, 10, 200m, extendedHours: true);

        Assert.Multiple(() =>
        {
            Assert.That(order.Side, Is.EqualTo(OrderSide.Sell));
            Assert.That(order.PositionIntent, Is.EqualTo("sell_to_open"));
            Assert.That(order.LimitPrice, Is.EqualTo(199.60m));
            Assert.That(order.Type, Is.EqualTo(OrderType.Limit));
            Assert.That(order.TimeInForce, Is.EqualTo("DAY"));
            Assert.That(order.ExtendedHours, Is.True);
        });
    }

    [Test]
    public void LongEntry_IsUnchanged_BuyAbovePrice_NoIntent()
    {
        var order = DirectionalOrders.Entry("TSLA", TradeDirection.Long, 10, 200m, extendedHours: false);

        Assert.Multiple(() =>
        {
            Assert.That(order.Side, Is.EqualTo(OrderSide.Buy));
            Assert.That(order.PositionIntent, Is.Null);
            Assert.That(order.LimitPrice, Is.EqualTo(200.40m));
        });
    }

    [Test]
    public void Exits_AlwaysClose_OnTheOppositeSide()
    {
        var shortExit = DirectionalOrders.Exit("TSLA", TradeDirection.Short, 10, 200m, false);
        var longExit  = DirectionalOrders.Exit("TSLA", TradeDirection.Long, 10, 200m, false);

        Assert.Multiple(() =>
        {
            Assert.That(shortExit.Side, Is.EqualTo(OrderSide.Buy));
            Assert.That(shortExit.PositionIntent, Is.EqualTo("buy_to_close"));
            Assert.That(shortExit.LimitPrice, Is.EqualTo(201.00m));
            Assert.That(longExit.Side, Is.EqualTo(OrderSide.Sell));
            Assert.That(longExit.PositionIntent, Is.EqualTo("sell_to_close"));
            Assert.That(longExit.LimitPrice, Is.EqualTo(199.00m));
        });
    }

    [Test]
    public void HeldShares_CountsOnlyThePositionsOwnSide()
    {
        var shortPos = new Position { Symbol = "TSLA", Quantity = -10m };
        var longPos  = new Position { Symbol = "TSLA", Quantity = 7.5m };

        Assert.Multiple(() =>
        {
            Assert.That(DirectionalOrders.HeldShares(shortPos, TradeDirection.Short), Is.EqualTo(10));
            Assert.That(DirectionalOrders.HeldShares(shortPos, TradeDirection.Long), Is.EqualTo(0));
            Assert.That(DirectionalOrders.HeldShares(longPos, TradeDirection.Long), Is.EqualTo(7));
            Assert.That(DirectionalOrders.HeldShares(longPos, TradeDirection.Short), Is.EqualTo(0));
            Assert.That(DirectionalOrders.HeldShares(null, TradeDirection.Short), Is.EqualTo(0));
        });
    }

    [Test]
    public void RealizedPnl_IsInvertedForAShort()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DirectionalOrders.RealizedPnl(TradeDirection.Short, 200m, 190m, 10), Is.EqualTo(100m));
            Assert.That(DirectionalOrders.RealizedPnl(TradeDirection.Short, 200m, 210m, 10), Is.EqualTo(-100m));
            Assert.That(DirectionalOrders.RealizedPnl(TradeDirection.Long, 200m, 210m, 10), Is.EqualTo(100m));
        });
    }

    [Test]
    public async Task ShortRoundTrip_OnTheSandboxBroker_OpensThenFlattens()
    {
        var broker = new SandboxBrokerClient();

        var entry = await broker.PlaceOrderAsync(DirectionalOrders.Entry("TSLA", TradeDirection.Short, 10, 200m, false));
        Assert.That(entry.IsSuccess, Is.True, entry.Message);

        var held = (await broker.GetPositionsAsync()).Single(p => p.Symbol == "TSLA");
        Assert.That(held.Quantity, Is.EqualTo(-10m), "the account is short 10");
        Assert.That(DirectionalOrders.HeldShares(held, TradeDirection.Short), Is.EqualTo(10));

        // Price falls, the exit brain says cover.
        var exit = await broker.PlaceOrderAsync(
            DirectionalOrders.Exit("TSLA", TradeDirection.Short, DirectionalOrders.HeldShares(held, TradeDirection.Short), 190m, false));
        Assert.That(exit.IsSuccess, Is.True, exit.Message);

        Assert.That(await broker.GetPositionsAsync(), Is.Empty, "covered back to flat");
        var pnl = DirectionalOrders.RealizedPnl(TradeDirection.Short,
            DirectionalOrders.EntryLimit(TradeDirection.Short, 200m), DirectionalOrders.ExitLimit(TradeDirection.Short, 190m), 10);
        Assert.That(pnl, Is.EqualTo((199.60m - 190.95m) * 10));
    }
}
