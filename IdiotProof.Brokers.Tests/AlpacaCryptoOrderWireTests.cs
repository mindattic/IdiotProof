using System.Net;
using System.Text.Json;
using IdiotProof.Brokers;
using IdiotProof.Models;

namespace IdiotProof.Brokers.Tests;

/// <summary>A crypto order on the wire (IP-T315): slash pair, fractional qty, gtc, no extended_hours flag. Scripted handler only.</summary>
public class AlpacaCryptoOrderWireTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"id":"ord-1"}""", System.Text.Encoding.UTF8, "application/json") };
        }
    }

    [Test]
    public async Task CryptoOrder_SendsThePairAndFractionalQty_WithoutExtendedHours()
    {
        var handler = new RecordingHandler();
        var result = await new AlpacaBrokerClient(handler).PlaceOrderAsync(new OrderRequest
        {
            Symbol = "BTC/USD", FractionalQuantity = 0.0015m, Side = OrderSide.Buy, Type = OrderType.Limit, LimitPrice = 70000m,
            TimeInForce = "GTC", AssetClass = AssetClass.Crypto,
        });

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var root = JsonDocument.Parse(handler.Bodies.Single()).RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("symbol").GetString(), Is.EqualTo("BTC/USD"));
            Assert.That(root.GetProperty("qty").GetDecimal(), Is.EqualTo(0.0015m));
            Assert.That(root.GetProperty("time_in_force").GetString(), Is.EqualTo("gtc"));
            Assert.That(root.TryGetProperty("extended_hours", out _), Is.False);
        });
    }
}
