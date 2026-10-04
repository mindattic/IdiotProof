using System.Net;
using System.Text.Json;
using IdiotProof.Brokers;
using IdiotProof.Models;

namespace IdiotProof.Brokers.Tests;

/// <summary>
/// Equity short wire format for <c>POST /v2/orders</c>, checked against the real paper account on
/// 2026-10-03 (<c>AlpacaPaperShortIntegrationTests</c> in IdiotProof.Monitor.Tests). Alpaca takes
/// <c>side</c> (<c>buy</c>/<c>sell</c>) and the optional <c>position_intent</c> on equity orders as well as
/// options, and echoes the intent back on the order. <c>qty</c> is always positive: the side carries the
/// direction. A short shows up in <c>/v2/positions</c> as a negative <c>qty</c> with <c>side: "short"</c>.
/// An account that may not short gets <c>403 {"code":40310000,"message":"account is not allowed to short"}</c>,
/// which is different from the <c>422</c> (code 40010001) a bad <c>position_intent</c> gets.
/// </summary>
public class AlpacaEquityShortWireTests
{
    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [TestCase(OrderSide.Sell, "sell_to_open", "sell", 199.60)]
    [TestCase(OrderSide.Buy, "buy_to_close", "buy", 201.00)]
    public async Task ShortOrders_SendSideAndIntent_WithPositiveQty(OrderSide side, string intent, string wireSide, decimal limit)
    {
        var handler = new RecordingHandler(_ => Json("""{"id":"ord-1","status":"accepted","side":"sell","position_intent":"sell_to_open","qty":"10"}"""));
        var broker = new AlpacaBrokerClient(handler);

        var result = await broker.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "tsla", Quantity = 10, Side = side, Type = OrderType.Limit, LimitPrice = limit,
            TimeInForce = "DAY", PositionIntent = intent,
        });

        Assert.That(result.IsSuccess, Is.True, result.Message);
        using var doc = JsonDocument.Parse(handler.Bodies.Single());
        var root = doc.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("symbol").GetString(), Is.EqualTo("TSLA"));
            Assert.That(root.GetProperty("qty").GetInt32(), Is.EqualTo(10), "qty is positive; side carries the direction");
            Assert.That(root.GetProperty("side").GetString(), Is.EqualTo(wireSide));
            Assert.That(root.GetProperty("position_intent").GetString(), Is.EqualTo(intent));
            Assert.That(root.GetProperty("type").GetString(), Is.EqualTo("limit"));
            Assert.That(root.GetProperty("time_in_force").GetString(), Is.EqualTo("day"));
            Assert.That(root.GetProperty("limit_price").GetDecimal(), Is.EqualTo(limit));
            Assert.That(root.GetProperty("extended_hours").GetBoolean(), Is.False);
            Assert.That(root.TryGetProperty("notional", out _), Is.False);
        });
    }

    [Test]
    public async Task LongEntry_SendsNoIntent()
    {
        var handler = new RecordingHandler(_ => Json("""{"id":"ord-2"}"""));
        var broker = new AlpacaBrokerClient(handler);

        await broker.PlaceOrderAsync(new OrderRequest { Symbol = "TSLA", Quantity = 1, Side = OrderSide.Buy, Type = OrderType.Limit, LimitPrice = 200.40m });

        using var doc = JsonDocument.Parse(handler.Bodies.Single());
        Assert.That(doc.RootElement.TryGetProperty("position_intent", out _), Is.False, "a null intent is omitted, not sent empty");
    }

    [Test]
    public async Task AccountThatCannotShort_IsAFailedOrder_WithAlpacasReason()
    {
        // Body copied from the paper account's answer on 2026-10-03 (shorting_enabled=false, equity under $2,000).
        var broker = new AlpacaBrokerClient(new RecordingHandler(_ =>
            Json("""{"code":40310000,"message":"account is not allowed to short"}""", HttpStatusCode.Forbidden)));

        var result = await broker.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL", Quantity = 1, Side = OrderSide.Sell, Type = OrderType.Limit, LimitPrice = 665.87m, PositionIntent = "sell_to_open",
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.BrokerOrderId, Is.Empty);
            Assert.That(result.Message, Does.Contain("HTTP 403").And.Contain("not allowed to short"));
        });
    }

    [Test]
    public async Task ShortPosition_ParsesAsNegativeQuantity()
    {
        var broker = new AlpacaBrokerClient(new RecordingHandler(_ => Json("""
            [{"symbol":"TSLA","asset_class":"us_equity","side":"short","qty":"-10","avg_entry_price":"199.6","market_value":"-1900","unrealized_pl":"96"}]
            """)));

        var position = (await broker.GetPositionsAsync()).Single();

        Assert.Multiple(() =>
        {
            Assert.That(position.Quantity, Is.EqualTo(-10m));
            Assert.That(position.AssetClass, Is.EqualTo(AssetClass.Equity));
        });
    }
}
