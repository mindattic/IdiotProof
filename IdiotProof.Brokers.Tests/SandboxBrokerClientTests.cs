using IdiotProof.Brokers;
using IdiotProof.Models;

namespace IdiotProof.Brokers.Tests;

/// <summary>
/// Sandbox fill simulation (IP-A8): orders update the in-memory position book
/// so a keyless dev session behaves like a real buy → hold → sell loop. The
/// old client accepted orders but never recorded a position.
/// </summary>
public class SandboxBrokerClientTests
{
    [Test]
    public async Task Buy_ThenGetPositions_ShowsTheFill()
    {
        var broker = new SandboxBrokerClient();
        var result = await broker.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "GAPR",
            Quantity = 100,
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            LimitPrice = 10.50m,
        });

        Assert.That(result.IsSuccess, Is.True);
        var positions = await broker.GetPositionsAsync();
        Assert.Multiple(() =>
        {
            Assert.That(positions, Has.Count.EqualTo(1));
            Assert.That(positions[0].Symbol, Is.EqualTo("GAPR"));
            Assert.That(positions[0].Quantity, Is.EqualTo(100m));
            Assert.That(positions[0].AveragePrice, Is.EqualTo(10.50m));
        });
    }

    [Test]
    public async Task Sell_FullQuantity_FlattensThePosition()
    {
        var broker = new SandboxBrokerClient();
        await broker.PlaceOrderAsync(new OrderRequest
        { Symbol = "GAPR", Quantity = 100, Side = OrderSide.Buy, Type = OrderType.Limit, LimitPrice = 10.50m });
        await broker.PlaceOrderAsync(new OrderRequest
        { Symbol = "GAPR", Quantity = 100, Side = OrderSide.Sell, Type = OrderType.Limit, LimitPrice = 11.20m });

        var positions = await broker.GetPositionsAsync();
        Assert.That(positions, Is.Empty, "a fully-sold position disappears like a real broker's book");
    }

    [Test]
    public async Task NotionalBuy_ConvertsToShares_AtTheLimitPrice()
    {
        var broker = new SandboxBrokerClient();
        var result = await broker.PlaceOrderAsync(new OrderRequest
        { Symbol = "GAPR", Notional = 1000m, Side = OrderSide.Buy, Type = OrderType.Limit, LimitPrice = 9.99m });

        Assert.That(result.IsSuccess, Is.True);
        var positions = await broker.GetPositionsAsync();
        Assert.That(positions[0].Quantity, Is.EqualTo(100m), "floor(1000 / 9.99) = 100 shares");
    }
}

/// <summary>
/// Alpaca extended-hours contract (IP-A8): a premarket gapper order MUST be
/// limit + DAY + extended_hours — anything else silently queues until the
/// 9:30 bell on Alpaca, defeating the 4AM entry. The client rejects locally.
/// </summary>
public class AlpacaBrokerClientExtendedHoursTests
{
    [Test]
    public async Task ExtendedHours_MarketOrder_IsRejectedLocally()
    {
        var broker = new AlpacaBrokerClient("key", "secret", isPaper: true);
        var result = await broker.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "GAPR",
            Quantity = 10,
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            ExtendedHours = true,
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("Limit"));
        });
    }

    [Test]
    public async Task ExtendedHours_LimitGtc_IsRejectedLocally()
    {
        var broker = new AlpacaBrokerClient("key", "secret", isPaper: true);
        var result = await broker.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "GAPR",
            Quantity = 10,
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            LimitPrice = 10m,
            TimeInForce = "GTC",
            ExtendedHours = true,
        });

        Assert.That(result.IsSuccess, Is.False);
    }
}
