using System.Net;
using System.Text.Json;
using IdiotProof.Brokers;
using IdiotProof.Models;

namespace IdiotProof.Brokers.Tests;

/// <summary>
/// The full <c>POST /v2/orders</c> surface (IP-T301/IP-T302): fractional qty, trail_price, client_order_id,
/// order classes with take_profit / stop_loss children, multi-leg legs, plus the replace (PATCH) body and
/// cancel-all. Scripted handler only; nothing reaches Alpaca.
/// </summary>
public class AlpacaOrderWireTests
{
    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request.Method, request.RequestUri!.PathAndQuery, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static async Task<JsonElement> PlaceAndCapture(OrderRequest order)
    {
        var handler = new RecordingHandler(_ => Json("""{"id":"ord-1"}"""));
        var result = await new AlpacaBrokerClient(handler).PlaceOrderAsync(order);
        Assert.That(result.IsSuccess, Is.True, result.Message);
        return JsonDocument.Parse(handler.Calls.Single().Body).RootElement.Clone();
    }

    [Test]
    public async Task Bracket_SendsOrderClassAndBothChildren()
    {
        var root = await PlaceAndCapture(new OrderRequest
        {
            Symbol = "nvda", Quantity = 10, Side = OrderSide.Buy, Type = OrderType.Limit, LimitPrice = 100m, TimeInForce = "GTC",
            OrderClass = OrderClass.Bracket, ClientOrderId = "mine-1",
            TakeProfit = new TakeProfitLeg { LimitPrice = 110m }, StopLoss = new StopLossLeg { StopPrice = 95m, LimitPrice = 94.5m },
        });

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("order_class").GetString(), Is.EqualTo("bracket"));
            Assert.That(root.GetProperty("take_profit").GetProperty("limit_price").GetDecimal(), Is.EqualTo(110m));
            Assert.That(root.GetProperty("stop_loss").GetProperty("stop_price").GetDecimal(), Is.EqualTo(95m));
            Assert.That(root.GetProperty("stop_loss").GetProperty("limit_price").GetDecimal(), Is.EqualTo(94.5m));
            Assert.That(root.GetProperty("client_order_id").GetString(), Is.EqualTo("mine-1"));
            Assert.That(root.GetProperty("time_in_force").GetString(), Is.EqualTo("gtc"));
            Assert.That(root.GetProperty("qty").GetInt32(), Is.EqualTo(10));
        });
    }

    [Test]
    public async Task SimpleOrder_HasNoClassOrChildren()
    {
        var root = await PlaceAndCapture(new OrderRequest { Symbol = "NVDA", Quantity = 1, Side = OrderSide.Buy, Type = OrderType.Market });

        Assert.That(root.TryGetProperty("order_class", out _), Is.False);
        Assert.That(root.TryGetProperty("take_profit", out _), Is.False);
        Assert.That(root.TryGetProperty("client_order_id", out _), Is.False);
    }

    [Test]
    public async Task FractionalQty_GoesOutAsADecimal()
    {
        var root = await PlaceAndCapture(new OrderRequest { Symbol = "AAPL", FractionalQuantity = 0.25m, Side = OrderSide.Buy, Type = OrderType.Market });

        Assert.That(root.GetProperty("qty").GetDecimal(), Is.EqualTo(0.25m));
        Assert.That(root.TryGetProperty("notional", out _), Is.False);
    }

    [Test]
    public async Task TrailPrice_WinsOverPercent_AndOnlyOneIsSent()
    {
        var root = await PlaceAndCapture(new OrderRequest { Symbol = "AAPL", Quantity = 2, Side = OrderSide.Sell, Type = OrderType.TrailingStop, TrailPrice = 1.5m, TimeInForce = "GTC" });

        Assert.That(root.GetProperty("type").GetString(), Is.EqualTo("trailing_stop"));
        Assert.That(root.GetProperty("trail_price").GetDecimal(), Is.EqualTo(1.5m));
        Assert.That(root.TryGetProperty("trail_percent", out _), Is.False);
    }

    [Test]
    public async Task MultiLeg_SendsLegs_WithoutATopLevelSide()
    {
        var root = await PlaceAndCapture(new OrderRequest
        {
            Quantity = 1, Type = OrderType.Limit, LimitPrice = 1.2m, OrderClass = OrderClass.Mleg, AssetClass = AssetClass.Option,
            Legs =
            [
                new OrderLeg { Symbol = "AAPL261120C00200000", Side = OrderSide.Buy, PositionIntent = "buy_to_open" },
                new OrderLeg { Symbol = "AAPL261120C00210000", Side = OrderSide.Sell, PositionIntent = "sell_to_open" },
            ],
        });

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("order_class").GetString(), Is.EqualTo("mleg"));
            Assert.That(root.TryGetProperty("side", out _), Is.False);
            Assert.That(root.GetProperty("legs").GetArrayLength(), Is.EqualTo(2));
            Assert.That(root.GetProperty("legs")[1].GetProperty("side").GetString(), Is.EqualTo("sell"));
            Assert.That(root.GetProperty("legs")[0].GetProperty("ratio_qty").GetInt32(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Replace_PatchesOnlyTheChangedFields_AndReturnsTheNewId()
    {
        var handler = new RecordingHandler(_ => Json("""{"id":"ord-2","replaces":"ord-1"}"""));

        var result = await new AlpacaBrokerClient(handler).ReplaceOrderAsync("ord-1", new OrderRequest { Quantity = 5, LimitPrice = 101.5m, TimeInForce = "" });

        var call = handler.Calls.Single();
        using var body = JsonDocument.Parse(call.Body);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.BrokerOrderId, Is.EqualTo("ord-2"));
            Assert.That(call.Method, Is.EqualTo(HttpMethod.Patch));
            Assert.That(call.Path, Is.EqualTo("/v2/orders/ord-1"));
            Assert.That(body.RootElement.GetProperty("qty").GetInt32(), Is.EqualTo(5));
            Assert.That(body.RootElement.GetProperty("limit_price").GetDecimal(), Is.EqualTo(101.5m));
            Assert.That(body.RootElement.TryGetProperty("time_in_force", out _), Is.False);
            Assert.That(body.RootElement.TryGetProperty("stop_price", out _), Is.False);
        });
    }

    [Test]
    public async Task CancelAll_ForASymbol_ListsThenCancelsEach()
    {
        var handler = new RecordingHandler(r => r.Method == HttpMethod.Get
            ? Json("""[{"id":"a","symbol":"TSLA"},{"id":"b","symbol":"TSLA"}]""")
            : new HttpResponseMessage(HttpStatusCode.NoContent));

        var result = await new AlpacaBrokerClient(handler).CancelAllOrdersAsync("tsla");

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Message, Does.StartWith("2 of 2 open TSLA"));
        Assert.That(handler.Calls[0].Path, Does.Contain("symbols=TSLA").And.Contain("status=open"));
        Assert.That(handler.Calls.Skip(1).Select(c => (c.Method, c.Path)),
            Is.EqualTo(new[] { (HttpMethod.Delete, "/v2/orders/a"), (HttpMethod.Delete, "/v2/orders/b") }));
    }

    [Test]
    public async Task CancelAll_Everything_CountsTheMultiStatusAnswer()
    {
        var handler = new RecordingHandler(_ => Json("""[{"id":"a","status":200},{"id":"b","status":500}]""", HttpStatusCode.MultiStatus));

        var result = await new AlpacaBrokerClient(handler).CancelAllOrdersAsync();

        Assert.That(handler.Calls.Single().Method, Is.EqualTo(HttpMethod.Delete));
        Assert.That(handler.Calls.Single().Path, Is.EqualTo("/v2/orders"));
        Assert.That(result.Message, Is.EqualTo("1 order(s) cancelled, 1 could not be."));
    }
}
